using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace PraetorisClient.Tombstones
{
    internal static class TombstoneAccess
    {
        internal const string GroupsGuid = "org.bepinex.plugins.groups";
        private const string ReportRpc = "PraetorisClient_TombstoneGroupReport";
        private const string StateRpc = "PraetorisClient_TombstoneAccessState";
        private const int MaximumPlayers = 128;
        private const float Interval = 2f;
        private const float Lifetime = 8f;
        private static readonly Dictionary<long, HashSet<long>> Reports = new();
        private static readonly Dictionary<long, float> ReportTimes = new();
        private static readonly Dictionary<long, long> Players = new();
        private static readonly HashSet<long> Admins = new();
        private static readonly HashSet<(long, long)> GroupPairs = new();
        private static float _nextUpdate;
        private static float _stateTime = float.NegativeInfinity;
        private static ZRoutedRpc? _rpc;
        private static MethodInfo? _groupPlayers;
        private static bool _ownerAccessOnly = true;
        private static bool _adminAccess = true;
        private static bool _groupAccess = true;

        private static bool HasFreshState => Time.unscaledTime - _stateTime <= Lifetime;
        internal static bool RestrictionEnabled => ZNet.instance != null && ZNet.instance.IsServer()
            ? PraetorisClientPlugin.TombstoneOwnerAccessEnabled.Value
            : !HasFreshState || _ownerAccessOnly;

        internal static void Register(ZRoutedRpc rpc)
        {
            _rpc = rpc;
            Reports.Clear();
            ReportTimes.Clear();
            Players.Clear();
            Admins.Clear();
            GroupPairs.Clear();
            _stateTime = float.NegativeInfinity;
            _ownerAccessOnly = true;
            _adminAccess = true;
            _groupAccess = true;
            _nextUpdate = 0f;
            rpc.Register<ZPackage>(ReportRpc, OnReport);
            rpc.Register<ZPackage>(StateRpc, OnState);
        }

        internal static void Update()
        {
            if (_rpc == null || _rpc != ZRoutedRpc.instance || ZNet.instance == null || Time.unscaledTime < _nextUpdate)
                return;
            _nextUpdate = Time.unscaledTime + Interval;
            if (Player.m_localPlayer != null)
            {
                ZPackage report = new();
                long[] members = LocalGroupPeers();
                report.Write(members.Length);
                foreach (long member in members) report.Write(member);
                if (ZNet.instance.IsServer()) OnReport(ZNet.GetUID(), report);
                else _rpc.InvokeRoutedRPC(_rpc.GetServerPeerID(), ReportRpc, report);
            }
            if (ZNet.instance.IsServer()) PublishState();
        }

        private static long[] LocalGroupPeers()
        {
            if (!Chainloader.PluginInfos.TryGetValue(GroupsGuid, out PluginInfo plugin)) return Array.Empty<long>();
            _groupPlayers ??= plugin.Instance.GetType().Assembly.GetType("Groups.API")?.GetMethod("GroupPlayers", BindingFlags.Public | BindingFlags.Static);
            if (_groupPlayers?.Invoke(null, null) is not IEnumerable members) return Array.Empty<long>();
            List<long> peers = new();
            foreach (object member in members)
            {
                if (member.GetType().GetField("peerId")?.GetValue(member) is long peer && peers.Count < MaximumPlayers)
                    peers.Add(peer);
            }
            return peers.Distinct().ToArray();
        }

        private static void OnReport(long sender, ZPackage package)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() ||
                !PlayerResolver.TryGetSenderPlayerId(sender, out long _, out ZNetPeer? _)) return;
            try
            {
                int count = package.ReadInt();
                if (count < 0 || count > MaximumPlayers) return;
                HashSet<long> members = new();
                for (int index = 0; index < count; index++) members.Add(package.ReadLong());
                Reports[sender] = members;
                ReportTimes[sender] = Time.unscaledTime;
            }
            catch (Exception) { PraetorisClientPlugin.Log.LogWarning("Rejected an invalid tombstone group report."); }
        }

        private static void PublishState()
        {
            Players.Clear();
            Admins.Clear();
            GroupPairs.Clear();
            foreach (ZNetPeer peer in ZNet.instance.GetConnectedPeers().Take(MaximumPlayers))
            {
                if (!peer.IsReady() || !PlayerResolver.TryGetPeerPlayerId(peer, out long player)) continue;
                Players[peer.m_uid] = player;
                string host = PlayerResolver.SafeHostName(peer);
                if (!string.IsNullOrWhiteSpace(host) && ZNet.instance.IsAdmin(host)) Admins.Add(player);
            }
            if (Player.m_localPlayer != null)
            {
                long player = Player.m_localPlayer.GetPlayerID();
                Players[ZNet.GetUID()] = player;
                Admins.Add(player);
            }
            foreach (long peer in Reports.Keys.ToArray())
                if (!Players.ContainsKey(peer) || Time.unscaledTime - ReportTimes[peer] > Lifetime)
                { Reports.Remove(peer); ReportTimes.Remove(peer); }
            foreach (KeyValuePair<long, HashSet<long>> report in Reports)
                foreach (long member in report.Value)
                    if (member != report.Key && Players.TryGetValue(member, out long player) &&
                        Reports.TryGetValue(member, out HashSet<long> other) && other.Contains(report.Key))
                        GroupPairs.Add((Players[report.Key], player));
            _stateTime = Time.unscaledTime;
            ZPackage state = new();
            state.Write(PraetorisClientPlugin.TombstoneOwnerAccessEnabled.Value);
            state.Write(PraetorisClientPlugin.TombstoneAdminAccessEnabled.Value);
            state.Write(PraetorisClientPlugin.TombstoneGroupAccessEnabled.Value);
            state.Write(Players.Count);
            foreach (KeyValuePair<long, long> player in Players)
            { state.Write(player.Key); state.Write(player.Value); state.Write(Admins.Contains(player.Value)); }
            state.Write(GroupPairs.Count);
            foreach ((long first, long second) in GroupPairs) { state.Write(first); state.Write(second); }
            _rpc!.InvokeRoutedRPC(ZRoutedRpc.Everybody, StateRpc, state);
        }

        private static void OnState(long sender, ZPackage package)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer() || _rpc == null || sender != _rpc.GetServerPeerID()) return;
            try
            {
                bool ownerAccessOnly = package.ReadBool();
                bool adminAccess = package.ReadBool();
                bool groupAccess = package.ReadBool();
                Dictionary<long, long> players = new();
                HashSet<long> admins = new();
                HashSet<(long, long)> pairs = new();
                int count = package.ReadInt();
                if (count < 0 || count > MaximumPlayers + 1) return;
                for (int index = 0; index < count; index++)
                {
                    long peer = package.ReadLong();
                    long player = package.ReadLong();
                    players[peer] = player;
                    if (package.ReadBool()) admins.Add(player);
                }
                int pairCount = package.ReadInt();
                if (pairCount < 0 || pairCount > MaximumPlayers * MaximumPlayers) return;
                for (int index = 0; index < pairCount; index++) pairs.Add((package.ReadLong(), package.ReadLong()));
                Players.Clear(); foreach (KeyValuePair<long, long> player in players) Players.Add(player.Key, player.Value);
                Admins.Clear(); Admins.UnionWith(admins);
                GroupPairs.Clear(); GroupPairs.UnionWith(pairs);
                _ownerAccessOnly = ownerAccessOnly;
                _adminAccess = adminAccess;
                _groupAccess = groupAccess;
                _stateTime = Time.unscaledTime;
            }
            catch (Exception) { PraetorisClientPlugin.Log.LogWarning("Rejected an invalid tombstone access state."); }
        }

        internal static bool CanAccess(TombStone stone, long player)
        {
            if (!RestrictionEnabled) return true;
            ZNetView view = stone.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || player == 0) return false;
            long owner = view.GetZDO().GetLong(ZDOVars.s_owner);
            if (owner != 0 && owner == player) return true;
            if (!HasFreshState) return false;
            bool server = ZNet.instance != null && ZNet.instance.IsServer();
            bool adminAccess = server ? PraetorisClientPlugin.TombstoneAdminAccessEnabled.Value : _adminAccess;
            bool groupAccess = server ? PraetorisClientPlugin.TombstoneGroupAccessEnabled.Value : _groupAccess;
            return adminAccess && Admins.Contains(player) ||
                   groupAccess && owner != 0 && GroupPairs.Contains((player, owner));
        }

        internal static bool IsRequester(long sender, long player)
        {
            if (sender == ZNet.GetUID() && Player.m_localPlayer != null)
                return Player.m_localPlayer.GetPlayerID() == player;
            if (ZNet.instance != null && ZNet.instance.IsServer())
                return PlayerResolver.TryGetSenderPlayerId(sender, out long actual, out ZNetPeer? _) && actual == player;
            return Time.unscaledTime - _stateTime <= Lifetime && Players.TryGetValue(sender, out long known) && known == player;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "Update")]
    internal static class TombstoneOpenInventoryPatch
    {
        private static void Prefix(InventoryGui __instance, Container ___m_currentContainer)
        {
            if (___m_currentContainer == null || Player.m_localPlayer == null) return;
            TombStone stone = ___m_currentContainer.GetComponent<TombStone>();
            if (stone != null && !TombstoneAccess.CanAccess(stone, Player.m_localPlayer.GetPlayerID()))
                __instance.Hide();
        }
    }

    [HarmonyPatch(typeof(TombStone), nameof(TombStone.Interact))]
    internal static class TombstoneInteractPatch
    {
        private static bool Prefix(TombStone __instance, Humanoid character, bool hold, ref bool __result)
        {
            if (hold || character is Player player && TombstoneAccess.CanAccess(__instance, player.GetPlayerID())) return true;
            character.Message(MessageHud.MessageType.Center, "Not your tombstone.");
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(TombStone), nameof(TombStone.GetHoverText))]
    internal static class TombstoneHoverPatch
    {
        private static void Postfix(TombStone __instance, ref string __result)
        {
            if (__result.Length > 0 && Player.m_localPlayer != null && !TombstoneAccess.CanAccess(__instance, Player.m_localPlayer.GetPlayerID()))
                __result = Localization.instance.Localize(__instance.m_text + " " + __instance.GetOwnerName()) + "\nNot your tombstone.";
        }
    }

    [HarmonyPatch(typeof(Container), "CheckAccess")]
    internal static class TombstoneContainerAccessPatch
    {
        private static bool Prefix(Container __instance, long playerID, ref bool __result)
        {
            TombStone stone = __instance.GetComponent<TombStone>();
            if (stone == null || !TombstoneAccess.RestrictionEnabled) return true;
            __result = TombstoneAccess.CanAccess(stone, playerID);
            return false;
        }
    }

    [HarmonyPatch]
    internal static class TombstoneRequestPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Container), "RPC_RequestOpen");
            yield return AccessTools.Method(typeof(Container), "RPC_RequestTakeAll");
            yield return AccessTools.Method(typeof(Container), "RPC_RequestStack");
        }

        private static bool Prefix(Container __instance, long uid, long playerID, MethodBase __originalMethod)
        {
            TombStone stone = __instance.GetComponent<TombStone>();
            if (stone == null || !TombstoneAccess.RestrictionEnabled || !__instance.IsOwner()) return true;
            if (TombstoneAccess.IsRequester(uid, playerID) && TombstoneAccess.CanAccess(stone, playerID)) return true;
            string response = __originalMethod.Name == "RPC_RequestOpen" ? "RPC_OpenResponse" :
                __originalMethod.Name == "RPC_RequestTakeAll" ? "RPC_TakeAllResponse" : "RPC_StackResponse";
            __instance.GetComponent<ZNetView>().InvokeRPC(uid, response, false);
            return false;
        }
    }
}
