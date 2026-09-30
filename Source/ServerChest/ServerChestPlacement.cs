using System;
using HarmonyLib;
using UnityEngine;

namespace PraetorisClient.ServerChestFeature
{
    internal static class ServerChestPlacement
    {
        private const string CountRequest = "Praetoris_ServerChestCount";
        private const string CountResponse = "Praetoris_ServerChestCountResult";
        private const string PlacementRequest = "Praetoris_ServerChestPlace";
        private const string PlacementResponse = "Praetoris_ServerChestPlaceResult";
        private static Player? _player;
        private static float _nextRequest;
        private static float _receivedAt = -100f;
        private static int _count;
        private static ZDOID _pendingChest;
        private static float _placementDeadline;
        private static float _nextPlacementRequest;

        internal static void Register(ZRoutedRpc rpc)
        {
            _player = null;
            _receivedAt = -100f;
            _pendingChest = ZDOID.None;
            rpc.Register<long>(CountRequest, OnCountRequest);
            rpc.Register<long, int>(CountResponse, OnCountResponse);
            rpc.Register<ZDOID>(PlacementRequest, OnPlacementRequest);
            rpc.Register<ZDOID, bool>(PlacementResponse, OnPlacementResponse);
        }

        internal static void RequestPlacement(ZDO zdo)
        {
            if (ZNet.instance.IsServer())
            {
                AcceptNewChest(zdo, ZNet.GetUID());
                return;
            }
            _player = Player.m_localPlayer;
            _pendingChest = zdo.m_uid;
            _placementDeadline = Time.unscaledTime + 15f;
            _nextPlacementRequest = 0f;
        }

        private static void UpdatePlacementRequest()
        {
            if (_pendingChest.IsNone() || ZRoutedRpc.instance == null || ZDOMan.instance == null)
                return;
            if (Time.unscaledTime >= _placementDeadline)
            {
                _pendingChest = ZDOID.None;
                ServerChest.ShowMessage("Server Chest registration timed out. Use manual registration or rebuild the chest.");
                return;
            }
            if (Time.unscaledTime < _nextPlacementRequest)
                return;
            _nextPlacementRequest = Time.unscaledTime + 1f;
            long server = ZRoutedRpc.instance.GetServerPeerID();
            ZDOMan.instance.ForceSendZDO(server, _pendingChest);
            ZRoutedRpc.instance.InvokeRoutedRPC(server, PlacementRequest, _pendingChest);
        }

        private static void OnPlacementRequest(long sender, ZDOID chestId)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null ||
                chestId.IsNone() || chestId.UserID != sender)
                return;
            // Object synchronization can arrive after this request. The client retries only this chest.
            ZDO chest = ZDOMan.instance.GetZDO(chestId);
            if (chest == null)
                return;
            bool accepted = AcceptNewChest(chest, sender);
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, PlacementResponse, chestId, accepted);
        }

        private static void OnPlacementResponse(long sender, ZDOID chestId, bool accepted)
        {
            if (ZRoutedRpc.instance == null || sender != ZRoutedRpc.instance.GetServerPeerID() ||
                chestId != _pendingChest)
                return;
            _pendingChest = ZDOID.None;
            _nextRequest = 0f;
            if (accepted)
            {
                _count = Math.Max(1, _count);
                _receivedAt = Time.unscaledTime;
            }
            ServerChest.ShowMessage(accepted ? "Server Chest registered." : "Server Chest placement rejected.");
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
                _pendingChest = ZDOID.None;
            }
            UpdatePlacementRequest();
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
            if (!_pendingChest.IsNone())
            {
                ServerChest.ShowMessage("Waiting for Server Chest registration.");
                return false;
            }
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

        private static bool AcceptNewChest(ZDO zdo, long sender)
        {
            if (!ServerChest.IsServerChestPrefab(zdo) || ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return false;
            }
            long creator = zdo.GetLong(ZDOVars.s_creator);
            bool resolved = ServerChestIdentity.TryGetSenderIdentity(sender, "", "", out string name, out string platform);
            if (!resolved || !IsSenderCreator(sender, creator))
                return false;
            // A repeated request must not register or delete an already accepted chest.
            if (zdo.GetOwner() == ZDOMan.GetSessionID() &&
                ServerChest.OwnerLookup(zdo) == ServerChest.NormalizeLookup(platform))
                return true;
            if (zdo.GetOwner() != sender)
                return false;
            if (Count(creator, platform, zdo.m_uid) > 0)
            {
                zdo.SetOwner(ZDOMan.GetSessionID());
                ZDOMan.instance.DestroyZDO(zdo);
                PraetorisClientPlugin.Log.LogInfo("Rejected duplicate Server Chest placement.");
                return false;
            }
            zdo.SetOwner(ZDOMan.GetSessionID());
            ServerChest.SetRegistration(zdo, name, platform);
            PraetorisClientPlugin.Log.LogInfo("Automatically registered new Server Chest for " + name + ".");
            return true;
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
            ServerChestPlacement.RequestPlacement(view.GetZDO());
        }
    }
}
