using System;
using HarmonyLib;
using UnityEngine;

namespace PraetorisClient.ServerChestFeature
{
    internal static class ServerChestPlacement
    {
        private const string CountRequest = "Praetoris_ServerChestCount";
        private const string CountResponse = "Praetoris_ServerChestCountResult";
        private static Player? _player;
        private static float _nextRequest;
        private static float _receivedAt = -100f;
        private static int _count;
        internal static ZNetPeer? ReceivingPeer;

        internal static void Register(ZRoutedRpc rpc)
        {
            _player = null;
            _receivedAt = -100f;
            rpc.Register<long>(CountRequest, OnCountRequest);
            rpc.Register<long, int>(CountResponse, OnCountResponse);
        }

        internal static bool IsChest(Piece piece)
        {
            return piece != null && Utils.GetPrefabName(piece.gameObject) == ServerChest.PrefabName;
        }

        internal static int Count(long creator, string platform, ZDOID exclude = default)
        {
            // The server's saved network objects include chests in unloaded zones.
            int count = 0;
            string owner = ServerChest.NormalizeLookup(platform);
            foreach (ZDO chest in ServerChest.FindAllZdos())
            {
                if (chest.m_uid != exclude &&
                    ((creator != 0L && chest.GetLong(ZDOVars.s_creator) == creator) ||
                     (owner.Length > 0 && ServerChest.OwnerLookup(chest) == owner)))
                {
                    count++;
                }
            }
            return count;
        }

        internal static void Update()
        {
            Player player = Player.m_localPlayer;
            if (player != _player)
            {
                _player = player;
                _receivedAt = -100f;
                _nextRequest = 0f;
            }
            if (player == null || ZRoutedRpc.instance == null || ZNet.instance == null ||
                ZNet.instance.IsServer() || !IsChest(player.GetSelectedPiece()) || Time.unscaledTime < _nextRequest)
            {
                return;
            }
            _nextRequest = Time.unscaledTime + 2f;
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), CountRequest, player.GetPlayerID());
        }

        private static void OnCountRequest(long sender, long creator)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() ||
                !IsSenderCreator(sender, creator) ||
                !ServerChestIdentity.TryGetSenderIdentity(sender, "", "", out _, out string platform))
            {
                return;
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, CountResponse, creator, Count(creator, platform));
        }

        private static bool IsSenderCreator(long sender, long creator)
        {
            if (sender == ZNet.GetUID())
                return Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == creator;
            ZNetPeer? peer = PlayerResolver.FindPeerBySender(sender);
            ZDO? player = peer != null && ZDOMan.instance != null ? ZDOMan.instance.GetZDO(peer.m_characterID) : null;
            return creator != 0L && player != null && player.GetLong(ZDOVars.s_playerID) == creator;
        }

        private static void OnCountResponse(long sender, long creator, int count)
        {
            if (ZRoutedRpc.instance == null || sender != ZRoutedRpc.instance.GetServerPeerID() ||
                Player.m_localPlayer == null || Player.m_localPlayer.GetPlayerID() != creator)
            {
                return;
            }
            _count = count;
            _receivedAt = Time.unscaledTime;
        }

        internal static bool CanPlace(Player player)
        {
            string platform = ServerChestIdentity.GetLocalPlatformId();
            int count = Count(player.GetPlayerID(), platform);
            if (ZNet.instance != null && !ZNet.instance.IsServer())
            {
                if (Time.unscaledTime - _receivedAt > 6f)
                {
                    ServerChest.ShowMessage("Checking your Server Chest count. Please try again.");
                    _nextRequest = 0f;
                    return false;
                }
                count = Math.Max(count, _count);
            }
            if (count > 0)
            {
                ServerChest.ShowMessage("You can only place one Server Chest. Remove your existing chest first.");
                return false;
            }
            return true;
        }

        internal static void AcceptNewChest(ZDO zdo, long sender)
        {
            if (!ServerChest.IsServerChestPrefab(zdo) || ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return;
            }
            long creator = zdo.GetLong(ZDOVars.s_creator);
            bool resolved = ServerChestIdentity.TryGetSenderIdentity(sender, "", "", out string name, out string platform);
            if (!resolved || !IsSenderCreator(sender, creator) || Count(creator, platform, zdo.m_uid) > 0)
            {
                zdo.SetOwner(ZDOMan.GetSessionID());
                ZDOMan.instance.DestroyZDO(zdo);
                PraetorisClientPlugin.Log.LogInfo("Rejected duplicate or unidentified Server Chest placement.");
                return;
            }
            zdo.SetOwner(ZDOMan.GetSessionID());
            ServerChest.SetRegistration(zdo, name, platform);
            PraetorisClientPlugin.Log.LogInfo("Automatically registered new Server Chest for " + name + ".");
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
    internal static class ServerChestTryPlacePatch
    {
        private static bool Prefix(Player __instance, Piece piece, ref bool __result)
        {
            if (!ServerChestPlacement.IsChest(piece) || ServerChestPlacement.CanPlace(__instance))
                return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Piece), nameof(Piece.SetCreator))]
    internal static class ServerChestCreatorPatch
    {
        private static void Prefix(Piece __instance, out bool __state)
        {
            __state = ServerChestPlacement.IsChest(__instance) && __instance.GetCreator() == 0L;
        }

        private static void Postfix(Piece __instance, long uid, bool __state)
        {
            Player player = Player.m_localPlayer;
            if (!__state || player == null || player.GetPlayerID() != uid || __instance.GetCreator() != uid)
                return;
            ZNetView view = __instance.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || !view.IsOwner())
                return;
            if (ZNet.instance.IsServer())
                ServerChestPlacement.AcceptNewChest(view.GetZDO(), ZNet.GetUID());
            else
                ServerChest.SetRegistration(view.GetZDO(), player.GetPlayerName(), ServerChestIdentity.GetLocalPlatformId());
        }
    }

    [HarmonyPatch(typeof(ZDOMan), "RPC_ZDOData")]
    internal static class ServerChestReceivePatch
    {
        private static void Prefix(ZRpc rpc, out ZNetPeer? __state)
        {
            __state = ServerChestPlacement.ReceivingPeer;
            ServerChestPlacement.ReceivingPeer = ZNet.instance != null && ZNet.instance.IsServer()
                ? ZNet.instance.GetPeer(rpc) : null;
        }

        private static Exception? Finalizer(Exception? __exception, ZNetPeer? __state)
        {
            ServerChestPlacement.ReceivingPeer = __state;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(ZDO), nameof(ZDO.Deserialize))]
    internal static class ServerChestDeserializePatch
    {
        private static void Prefix(ZDO __instance, out bool __state)
        {
            // Apply the limit only to new client placements, not saved world objects.
            __state = ServerChestPlacement.ReceivingPeer != null && __instance.GetPrefab() == 0;
        }

        private static void Postfix(ZDO __instance, bool __state)
        {
            if (__state && ServerChestPlacement.ReceivingPeer != null)
                ServerChestPlacement.AcceptNewChest(__instance, ServerChestPlacement.ReceivingPeer.m_uid);
        }
    }
}
