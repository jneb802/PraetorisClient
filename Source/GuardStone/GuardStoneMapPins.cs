using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace PraetorisClient.GuardStoneFeature
{
    internal static class GuardStoneMapPins
    {
        private const string RequestRpc = "PraetorisWardPinsSubscribe";
        private const string ResultRpc = "PraetorisWardPinsSnapshot";
        private const string ChangeRpc = "PraetorisWardPinsChanged";
        private static readonly int WardHash = "guard_stone".GetStableHashCode();
        private static readonly Dictionary<long, Subscription> Subscribers = new Dictionary<long, Subscription>();
        private static readonly Dictionary<long, int> PendingRequests = new Dictionary<long, int>();
        private static readonly Dictionary<ZDOID, Minimap.PinData> Pins = new Dictionary<ZDOID, Minimap.PinData>();
        private static long _playerId;
        private static int _requestId;
        private static Minimap? _map;
        private static Sprite? _icon;

        private sealed class Subscription
        {
            internal long PlayerId;
            internal int RequestId;
        }

        internal static void Register(ZRoutedRpc rpc)
        {
            Clear();
            Subscribers.Clear();
            PendingRequests.Clear();
            rpc.Register<int>(RequestRpc, OnRequest);
            rpc.Register<ZPackage>(ResultRpc, OnResult);
            rpc.Register<ZPackage>(ChangeRpc, OnChange);
        }

        private static IEnumerator LoadPins(Player player)
        {
            Clear();
            int requestId = _requestId;
            // Wait only during spawning. Once initialized, changes arrive through ward events.
            while (player != null && player == Player.m_localPlayer && requestId == _requestId)
            {
                Minimap? map = Minimap.instance;
                Sprite? icon = ObjectDB.instance?.GetItemPrefab("ShieldBanded")?
                    .GetComponent<ItemDrop>()?.m_itemData.GetIcon();
                if (map != null && icon != null && ZNet.instance != null &&
                    ZDOMan.instance != null && ZRoutedRpc.instance != null)
                {
                    _map = map;
                    _icon = icon;
                    _playerId = player.GetPlayerID();
                    if (ZNet.instance.IsServer())
                    {
                        foreach (ZDO ward in GuardStoneBuildLimit.GetWorldGuardStones())
                        {
                            if (ward.GetLong(ZDOVars.s_creator, 0L) == _playerId)
                                AddPin(ward.m_uid, ward.GetPosition());
                        }
                    }
                    else
                    {
                        ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RequestRpc, requestId);
                    }
                    yield break;
                }
                yield return null;
            }
        }

        private static void OnRequest(long sender, int requestId)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null ||
                ZNet.instance.GetPeer(sender) == null) return;
            if (!PlayerResolver.TryGetSenderPlayerId(sender, out long playerId, out _))
            {
                // The subscription can arrive before the character's first network data.
                PendingRequests[sender] = requestId;
                return;
            }
            PendingRequests.Remove(sender);
            if (Subscribers.TryGetValue(sender, out Subscription previous) &&
                previous.PlayerId == playerId && previous.RequestId == requestId) return;
            Subscription subscription = new Subscription { PlayerId = playerId, RequestId = requestId };
            Subscribers[sender] = subscription;
            List<ZDO> wards = new List<ZDO>();
            foreach (ZDO ward in GuardStoneBuildLimit.GetWorldGuardStones())
            {
                if (ward.GetLong(ZDOVars.s_creator, 0L) == playerId) wards.Add(ward);
            }
            ZPackage package = Header(subscription);
            package.Write(wards.Count);
            foreach (ZDO ward in wards)
            {
                package.Write(ward.m_uid);
                package.Write(ward.GetPosition());
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, ResultRpc, package);
        }

        private static ZPackage Header(Subscription subscription)
        {
            ZPackage package = new ZPackage();
            package.Write(subscription.RequestId);
            package.Write(subscription.PlayerId);
            return package;
        }

        private static void CharacterDataReceived(ZRpc rpc)
        {
            ZNetPeer? peer = ZNet.instance?.GetPeer(rpc);
            if (peer != null && PendingRequests.TryGetValue(peer.m_uid, out int requestId))
                OnRequest(peer.m_uid, requestId);
        }

        private static bool Accept(long sender, ZPackage package)
        {
            return ZRoutedRpc.instance != null && sender == ZRoutedRpc.instance.GetServerPeerID() &&
                _map != null && Player.m_localPlayer != null && _playerId == Player.m_localPlayer.GetPlayerID() &&
                package.ReadInt() == _requestId && package.ReadLong() == _playerId;
        }

        private static void OnResult(long sender, ZPackage package)
        {
            if (!Accept(sender, package)) return;
            int count = package.ReadInt();
            if (count < 0 || count > 10000) return;
            RemovePins();
            for (int index = 0; index < count; index++) AddPin(package.ReadZDOID(), package.ReadVector3());
        }

        private static void OnChange(long sender, ZPackage package)
        {
            if (!Accept(sender, package)) return;
            ZDOID id = package.ReadZDOID();
            Vector3 position = package.ReadVector3();
            if (package.ReadBool()) RemovePin(id);
            else AddPin(id, position);
        }

        private static void Changed(ZDO ward, bool removed)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ward.GetPrefab() != WardHash) return;
            long creator = ward.GetLong(ZDOVars.s_creator, 0L);
            if (creator == 0L) return;
            if (creator == _playerId)
            {
                if (removed) RemovePin(ward.m_uid);
                else AddPin(ward.m_uid, ward.GetPosition());
            }
            foreach (KeyValuePair<long, Subscription> entry in Subscribers)
            {
                if (entry.Value.PlayerId != creator || ZNet.instance.GetPeer(entry.Key) == null) continue;
                ZPackage package = Header(entry.Value);
                package.Write(ward.m_uid);
                package.Write(ward.GetPosition());
                package.Write(removed);
                ZRoutedRpc.instance.InvokeRoutedRPC(entry.Key, ChangeRpc, package);
            }
        }

        private static void AddPin(ZDOID id, Vector3 position)
        {
            if (_map == null || _icon == null) return;
            if (Pins.TryGetValue(id, out Minimap.PinData pin))
            {
                pin.m_pos = position;
                return;
            }
            // Unsaved pins are rebuilt each session and excluded from cartography sharing.
            pin = _map.AddPin(position, Minimap.PinType.None, "$piece_guardstone", false, false);
            pin.m_icon = _icon;
            Pins.Add(id, pin);
        }

        private static void RemovePin(ZDOID id)
        {
            if (!Pins.TryGetValue(id, out Minimap.PinData pin)) return;
            if (_map != null) _map.RemovePin(pin);
            Pins.Remove(id);
        }

        private static void RemovePins()
        {
            if (_map != null)
            {
                foreach (Minimap.PinData pin in Pins.Values) _map.RemovePin(pin);
            }
            Pins.Clear();
        }

        internal static void Clear()
        {
            RemovePins();
            _map = null;
            _icon = null;
            _playerId = 0L;
            // Invalidate pending initialization and replies from an earlier character or world.
            _requestId++;
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        private static class SpawnPatch
        {
            private static void Postfix(Player __instance)
            {
                if (__instance == Player.m_localPlayer) __instance.StartCoroutine(LoadPins(__instance));
            }
        }

        [HarmonyPatch(typeof(Player), "OnDestroy")]
        private static class PlayerDestroyedPatch
        {
            private static void Prefix(Player __instance)
            {
                if (__instance == Player.m_localPlayer) Clear();
            }
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Disconnect))]
        private static class DisconnectPatch
        {
            private static void Prefix(ZNetPeer peer)
            {
                Subscribers.Remove(peer.m_uid);
                PendingRequests.Remove(peer.m_uid);
            }
        }

        [HarmonyPatch(typeof(ZNet), "RPC_CharacterID")]
        private static class CharacterIdPatch
        {
            private static void Postfix(ZRpc rpc) => CharacterDataReceived(rpc);
        }

        [HarmonyPatch(typeof(ZDOMan), "RPC_ZDOData")]
        private static class CharacterDataPatch
        {
            private static void Postfix(ZRpc rpc) => CharacterDataReceived(rpc);
        }

        [HarmonyPatch(typeof(Piece), nameof(Piece.SetCreator))]
        private static class PlacementPatch
        {
            private static void Postfix(Piece __instance)
            {
                if (!GuardStoneBuildLimit.IsGuardStone(__instance)) return;
                ZDO? ward = __instance.GetComponent<ZNetView>()?.GetZDO();
                if (ward != null) Changed(ward, false);
            }
        }

        [HarmonyPatch(typeof(ZDO), nameof(ZDO.Deserialize))]
        private static class ReceivedWardPatch
        {
            private static void Prefix(ZDO __instance, out bool __state)
            {
                __state = GuardStoneZdoReceiveContext.Depth > 0 && __instance.GetPrefab() == 0;
            }

            private static void Postfix(ZDO __instance, bool __state)
            {
                if (__state) Changed(__instance, false);
            }
        }

        [HarmonyPatch(typeof(ZDOMan), "HandleDestroyedZDO")]
        private static class DestroyedWardPatch
        {
            private static void Prefix(ZDOMan __instance, ZDOID uid)
            {
                ZDO? ward = __instance.GetZDO(uid);
                if (ward != null) Changed(ward, true);
            }
        }
    }
}
