using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace PraetorisClient.Tombstones
{
    internal static class TombstoneAccess
    {
        internal const string GroupsGuid = "org.bepinex.plugins.groups";
        private const string RequestRpc = "PraetorisClient_TombstoneRequest";
        private const string QueryRpc = "PraetorisClient_TombstoneGroupQuery";
        private const string ReplyRpc = "PraetorisClient_TombstoneGroupReply";
        private const string DecisionRpc = "PraetorisClient_TombstoneDecision";
        private const float Lifetime = 5f;
        private const int MaximumPending = 64;
        private static readonly Dictionary<long, LocalRequest> LocalRequests = new();
        private static readonly Dictionary<long, GroupRequest> GroupRequests = new();
        private static readonly Dictionary<(ZDOID, long, long, int), float> Grants = new();
        private static MethodInfo? _findGroupMember;
        private static ZRoutedRpc? _rpc;
        private static long _nextRequest;
        private static Container? _allowedContainer;
        private static long _allowedPlayer;
        private static bool _executingRequest;

        private sealed class LocalRequest
        {
            internal Container Container = null!;
            internal int Action;
            internal float Time;
        }

        private sealed class GroupRequest
        {
            internal long Request;
            internal long Sender;
            internal long Player;
            internal long Creator;
            internal long CreatorPeer;
            internal ZDOID Stone;
            internal int Action;
            internal float Time;
        }

        internal static void Register(ZRoutedRpc rpc)
        {
            _rpc = rpc;
            LocalRequests.Clear();
            GroupRequests.Clear();
            Grants.Clear();
            _allowedContainer = null;
            _executingRequest = false;
            rpc.Register<ZPackage>(RequestRpc, OnRequest);
            rpc.Register<ZPackage>(QueryRpc, OnGroupQuery);
            rpc.Register<ZPackage>(ReplyRpc, OnGroupReply);
            rpc.Register<ZPackage>(DecisionRpc, OnDecision);
        }

        private static bool SameLocalGroup(long player)
        {
            if (!Chainloader.PluginInfos.TryGetValue(GroupsGuid, out BepInEx.PluginInfo plugin)) return false;
            _findGroupMember ??= plugin.Instance.GetType().Assembly.GetType("Groups.API")?
                .GetMethod("FindGroupMemberByPlayerId", BindingFlags.Public | BindingFlags.Static);
            return _findGroupMember?.Invoke(null, new object[] { player }) != null;
        }

        private static long ServerPeer => ZNet.instance.IsServer() ? ZNet.GetUID() : _rpc!.GetServerPeerID();
        private static bool FromServer(long sender) => _rpc != null && sender == ServerPeer;

        private static void Send(long target, string name, ZPackage package)
        {
            if (target == ZNet.GetUID())
            {
                switch (name)
                {
                    case RequestRpc: OnRequest(target, package); break;
                    case QueryRpc: OnGroupQuery(target, package); break;
                    case ReplyRpc: OnGroupReply(target, package); break;
                    case DecisionRpc: OnDecision(target, package); break;
                }
            }
            else _rpc!.InvokeRoutedRPC(target, name, package);
        }

        // Called only by an open, take-all, or stack action. No periodic work.
        internal static bool Request(Container container, int action)
        {
            if (container == _allowedContainer) return true;
            if (_rpc == null || Player.m_localPlayer == null) return false;
            ZNetView view = container.GetComponent<ZNetView>();
            if (view == null || !view.IsValid()) return false;
            foreach (long key in LocalRequests.Where(entry => Time.unscaledTime - entry.Value.Time > Lifetime).Select(entry => entry.Key).ToArray())
                LocalRequests.Remove(key);
            if (LocalRequests.Values.Any(request => request.Container == container && request.Action == action)) return false;
            if (LocalRequests.Count >= MaximumPending) return false;
            long request = ++_nextRequest;
            LocalRequests[request] = new LocalRequest { Container = container, Action = action, Time = Time.unscaledTime };
            ZPackage package = new();
            package.Write(request);
            package.Write(view.GetZDO().m_uid);
            package.Write(action);
            package.Write(SameLocalGroup(view.GetZDO().GetLong(ZDOVars.s_owner)));
            Send(ServerPeer, RequestRpc, package);
            return false;
        }

        private static bool TryStone(ZDOID id, out ZDO stone)
        {
            stone = ZDOMan.instance.GetZDO(id);
            return stone != null && stone.GetPrefab() == "Player_tombstone".GetStableHashCode();
        }

        private static bool IsAdmin(long sender, ZNetPeer? peer) =>
            sender == ZNet.GetUID() && Player.m_localPlayer != null ||
            peer != null && ZNet.instance.IsAdmin(PlayerResolver.SafeHostName(peer));

        private static void OnRequest(long sender, ZPackage package)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            try
            {
                long request = package.ReadLong();
                ZDOID id = package.ReadZDOID();
                int action = package.ReadInt();
                bool group = package.ReadBool();
                if (action < 0 || action > 2 || !TryStone(id, out ZDO stone) ||
                    !PlayerResolver.TryGetSenderPlayerId(sender, out long player, out ZNetPeer? peer)) return;
                GroupRequest check = new() { Request = request, Sender = sender, Player = player,
                    Creator = stone.GetLong(ZDOVars.s_owner), Stone = id, Action = action, Time = Time.unscaledTime };
                if (!PraetorisClientPlugin.TombstoneOwnerAccessEnabled.Value || check.Creator == player ||
                    PraetorisClientPlugin.TombstoneAdminAccessEnabled.Value && IsAdmin(sender, peer))
                { Decide(check, true); return; }
                if (PraetorisClientPlugin.TombstoneGroupAccessEnabled.Value && group)
                {
                    if (Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == check.Creator)
                        check.CreatorPeer = ZNet.GetUID();
                    else
                        foreach (ZNetPeer member in ZNet.instance.GetConnectedPeers())
                            if (member.IsReady() && PlayerResolver.TryGetPeerPlayerId(member, out long creator) && creator == check.Creator)
                            { check.CreatorPeer = member.m_uid; break; }
                    foreach (long key in GroupRequests.Where(entry => Time.unscaledTime - entry.Value.Time > Lifetime).Select(entry => entry.Key).ToArray())
                        GroupRequests.Remove(key);
                    if (check.CreatorPeer != 0 && GroupRequests.Count < MaximumPending)
                    {
                        long query = ++_nextRequest;
                        GroupRequests[query] = check;
                        ZPackage question = new();
                        question.Write(query); question.Write(player);
                        Send(check.CreatorPeer, QueryRpc, question);
                        return;
                    }
                }
                Decide(check, false);
            }
            catch (Exception) { PraetorisClientPlugin.Log.LogWarning("Rejected an invalid tombstone access request."); }
        }

        private static void OnGroupQuery(long sender, ZPackage package)
        {
            if (!FromServer(sender) || Player.m_localPlayer == null) return;
            try
            {
                long query = package.ReadLong();
                bool member = SameLocalGroup(package.ReadLong());
                ZPackage reply = new();
                reply.Write(query); reply.Write(member);
                Send(ServerPeer, ReplyRpc, reply);
            }
            catch (Exception) { PraetorisClientPlugin.Log.LogWarning("Rejected an invalid tombstone group query."); }
        }

        private static void OnGroupReply(long sender, ZPackage package)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            try
            {
                long query = package.ReadLong();
                bool member = package.ReadBool();
                if (!GroupRequests.TryGetValue(query, out GroupRequest check) || sender != check.CreatorPeer) return;
                GroupRequests.Remove(query);
                bool valid = Time.unscaledTime - check.Time <= Lifetime &&
                    PlayerResolver.TryGetSenderPlayerId(sender, out long creator, out ZNetPeer? _) && creator == check.Creator &&
                    PlayerResolver.TryGetSenderPlayerId(check.Sender, out long player, out ZNetPeer? _) && player == check.Player;
                Decide(check, valid && member && PraetorisClientPlugin.TombstoneGroupAccessEnabled.Value);
            }
            catch (Exception) { PraetorisClientPlugin.Log.LogWarning("Rejected an invalid tombstone group reply."); }
        }

        private static void Decide(GroupRequest check, bool allowed)
        {
            if (!TryStone(check.Stone, out ZDO stone)) return;
            ZPackage decision = new();
            decision.Write(check.Request); decision.Write(check.Stone); decision.Write(check.Sender);
            decision.Write(check.Player); decision.Write(check.Action); decision.Write(allowed);
            // The owner receives the single-use grant before the requester resumes.
            long owner = stone.GetOwner();
            if (allowed && owner != check.Sender) Send(owner, DecisionRpc, new ZPackage(decision.GetArray()));
            Send(check.Sender, DecisionRpc, decision);
        }

        private static void OnDecision(long sender, ZPackage package)
        {
            if (!FromServer(sender)) return;
            try
            {
                long request = package.ReadLong();
                ZDOID stone = package.ReadZDOID();
                long peer = package.ReadLong();
                long player = package.ReadLong();
                int action = package.ReadInt();
                bool allowed = package.ReadBool();
                foreach ((ZDOID, long, long, int) key in Grants.Where(entry => Time.unscaledTime - entry.Value > Lifetime).Select(entry => entry.Key).ToArray())
                    Grants.Remove(key);
                if (allowed) Grants[(stone, peer, player, action)] = Time.unscaledTime;
                if (peer != ZNet.GetUID() || !LocalRequests.TryGetValue(request, out LocalRequest local)) return;
                LocalRequests.Remove(request);
                if (!allowed)
                { Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "Not your tombstone."); return; }
                if (Time.unscaledTime - local.Time > Lifetime || local.Container == null || Player.m_localPlayer == null ||
                    Player.m_localPlayer.GetPlayerID() != player || local.Action != action ||
                    local.Container.GetComponent<ZNetView>().GetZDO().m_uid != stone) return;
                WithAccess(local.Container, player, () =>
                {
                    switch (action)
                    {
                        case 0: local.Container.Interact(Player.m_localPlayer, false, false); break;
                        case 1: local.Container.TakeAll(Player.m_localPlayer); break;
                        case 2: local.Container.StackAll(); break;
                    }
                });
            }
            catch (Exception) { PraetorisClientPlugin.Log.LogWarning("Rejected an invalid tombstone access decision."); }
        }

        internal static bool CanAccess(Container container, long player) =>
            container == _allowedContainer && player == _allowedPlayer ||
            container.GetComponent<ZNetView>().GetZDO().GetLong(ZDOVars.s_owner) == player && player != 0;

        private static void WithAccess(Container container, long player, Action action)
        {
            Container? previous = _allowedContainer;
            long previousPlayer = _allowedPlayer;
            _allowedContainer = container; _allowedPlayer = player;
            try { action(); }
            finally { _allowedContainer = previous; _allowedPlayer = previousPlayer; }
        }

        internal static bool HandleRequest(Container container, long sender, long player, MethodBase method)
        {
            if (_executingRequest) return true;
            int action = method.Name == "RPC_RequestOpen" ? 0 : method.Name == "RPC_RequestTakeAll" ? 1 : 2;
            (ZDOID, long, long, int) key = (container.GetComponent<ZNetView>().GetZDO().m_uid, sender, player, action);
            if (Grants.TryGetValue(key, out float time) && Time.unscaledTime - time <= Lifetime)
            {
                Grants.Remove(key);
                _executingRequest = true;
                try { WithAccess(container, player, () => method.Invoke(container, new object[] { sender, player })); }
                finally { _executingRequest = false; }
            }
            else
            {
                string response = action == 0 ? "RPC_OpenResponse" : action == 1 ? "RPC_TakeAllResponse" : "RPC_StackResponse";
                container.GetComponent<ZNetView>().InvokeRPC(sender, response, false);
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.Interact))]
    internal static class TombstoneInteractPatch
    {
        private static bool Prefix(Container __instance, bool hold, ref bool __result)
        {
            if (hold || __instance.GetComponent<TombStone>() == null) return true;
            if (TombstoneAccess.Request(__instance, 0)) return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.TakeAll))]
    internal static class TombstoneTakeAllPatch
    {
        private static bool Prefix(Container __instance, ref bool __result)
        {
            if (__instance.GetComponent<TombStone>() == null || TombstoneAccess.Request(__instance, 1)) return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.StackAll))]
    internal static class TombstoneStackPatch
    {
        private static bool Prefix(Container __instance) =>
            __instance.GetComponent<TombStone>() == null || TombstoneAccess.Request(__instance, 2);
    }

    [HarmonyPatch(typeof(Container), "CheckAccess")]
    internal static class TombstoneContainerAccessPatch
    {
        private static bool Prefix(Container __instance, long playerID, ref bool __result)
        {
            if (__instance.GetComponent<TombStone>() == null) return true;
            __result = TombstoneAccess.CanAccess(__instance, playerID);
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

        private static bool Prefix(Container __instance, long uid, long playerID, MethodBase __originalMethod) =>
            __instance.GetComponent<TombStone>() == null || !__instance.IsOwner() ||
            TombstoneAccess.HandleRequest(__instance, uid, playerID, __originalMethod);
    }
}
