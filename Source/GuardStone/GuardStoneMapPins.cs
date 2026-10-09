using System.Collections.Generic;
using UnityEngine;

namespace PraetorisClient.GuardStoneFeature
{
    internal static class GuardStoneMapPins
    {
        private const string RequestRpc = "PraetorisWardPinsRequest";
        private const string ResultRpc = "PraetorisWardPinsResult";
        private const float RefreshSeconds = 5f;
        private const float StaleSeconds = 30f;
        private static readonly Dictionary<long, float> LastRequests = new Dictionary<long, float>();
        private static readonly Dictionary<long, Dictionary<ZDOID, Vector3>> WorldWards =
            new Dictionary<long, Dictionary<ZDOID, Vector3>>();
        private static readonly Dictionary<ZDOID, Minimap.PinData> Pins = new Dictionary<ZDOID, Minimap.PinData>();
        private static float _nextWorldRefresh;
        private static float _nextRequest;
        private static float _receivedAt = float.NegativeInfinity;
        private static long _playerId;
        private static int _requestId;
        private static Minimap? _map;
        private static Sprite? _icon;

        internal static void Register(ZRoutedRpc rpc)
        {
            Clear();
            LastRequests.Clear();
            WorldWards.Clear();
            _nextWorldRefresh = 0f;
            rpc.Register<int>(RequestRpc, OnRequest);
            rpc.Register<ZPackage>(ResultRpc, OnResult);
        }

        internal static void Update()
        {
            Player? player = Player.m_localPlayer;
            Minimap? map = Minimap.instance;
            if (player == null || map == null || ZNet.instance == null || ZDOMan.instance == null ||
                ZRoutedRpc.instance == null)
            {
                Clear();
                return;
            }

            long playerId = player.GetPlayerID();
            if (_map != map || _playerId != playerId)
            {
                Clear();
                _map = map;
                _playerId = playerId;
            }

            if (_icon == null)
            {
                GameObject? shield = ObjectDB.instance?.GetItemPrefab("ShieldBanded");
                _icon = shield?.GetComponent<ItemDrop>()?.m_itemData.GetIcon();
                if (_icon == null) return;
            }

            float now = Time.realtimeSinceStartup;
            if (now - _receivedAt > StaleSeconds) RemovePins();
            if (now < _nextRequest) return;
            _nextRequest = now + RefreshSeconds;
            _requestId++;
            if (ZNet.instance.IsServer())
            {
                Apply(GetWards(playerId));
            }
            else
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RequestRpc, _requestId);
            }
        }

        private static Dictionary<ZDOID, Vector3> GetWards(long playerId)
        {
            // One world scan serves all requesting players during each refresh interval.
            float now = Time.realtimeSinceStartup;
            if (now >= _nextWorldRefresh)
            {
                _nextWorldRefresh = now + RefreshSeconds;
                WorldWards.Clear();
                foreach (ZDO ward in GuardStoneBuildLimit.GetWorldGuardStones())
                {
                    long creator = ward.GetLong(ZDOVars.s_creator, 0L);
                    if (creator == 0L) continue;
                    if (!WorldWards.TryGetValue(creator, out Dictionary<ZDOID, Vector3> wards))
                    {
                        wards = new Dictionary<ZDOID, Vector3>();
                        WorldWards.Add(creator, wards);
                    }
                    wards.Add(ward.m_uid, ward.GetPosition());
                }
            }

            return WorldWards.TryGetValue(playerId, out Dictionary<ZDOID, Vector3> result)
                ? result : new Dictionary<ZDOID, Vector3>();
        }

        private static void OnRequest(long sender, int requestId)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null ||
                !PlayerResolver.TryGetSenderPlayerId(sender, out long playerId, out _)) return;

            float now = Time.realtimeSinceStartup;
            if (LastRequests.TryGetValue(sender, out float previous) && now - previous < RefreshSeconds - 0.5f) return;
            LastRequests[sender] = now;
            Dictionary<ZDOID, Vector3> wards = GetWards(playerId);
            ZPackage package = new ZPackage();
            package.Write(requestId);
            package.Write(playerId);
            package.Write(wards.Count);
            foreach (KeyValuePair<ZDOID, Vector3> ward in wards)
            {
                package.Write(ward.Key);
                package.Write(ward.Value);
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, ResultRpc, package);
        }

        private static void OnResult(long sender, ZPackage package)
        {
            if (ZRoutedRpc.instance == null || sender != ZRoutedRpc.instance.GetServerPeerID() ||
                _map == null || Player.m_localPlayer == null || _playerId != Player.m_localPlayer.GetPlayerID()) return;

            if (package.ReadInt() != _requestId || package.ReadLong() != _playerId) return;
            int count = package.ReadInt();
            if (count < 0 || count > 10000) return;
            Dictionary<ZDOID, Vector3> wards = new Dictionary<ZDOID, Vector3>();
            for (int i = 0; i < count; i++)
            {
                ZDOID id = package.ReadZDOID();
                wards[id] = package.ReadVector3();
            }
            Apply(wards);
        }

        private static void Apply(Dictionary<ZDOID, Vector3> wards)
        {
            if (_map == null || _icon == null) return;
            _receivedAt = Time.realtimeSinceStartup;
            List<ZDOID> removed = new List<ZDOID>();
            foreach (KeyValuePair<ZDOID, Minimap.PinData> pin in Pins)
            {
                if (!wards.ContainsKey(pin.Key))
                {
                    _map.RemovePin(pin.Value);
                    removed.Add(pin.Key);
                }
            }
            foreach (ZDOID id in removed) Pins.Remove(id);
            foreach (KeyValuePair<ZDOID, Vector3> ward in wards)
            {
                if (Pins.TryGetValue(ward.Key, out Minimap.PinData pin))
                {
                    pin.m_pos = ward.Value;
                    continue;
                }

                // Unsaved pins are rebuilt each session and excluded from cartography sharing.
                pin = _map.AddPin(ward.Value, Minimap.PinType.None, "$piece_guardstone", false, false);
                pin.m_icon = _icon;
                Pins.Add(ward.Key, pin);
            }
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
            _nextRequest = 0f;
            _receivedAt = float.NegativeInfinity;
            // Do not reuse request IDs across worlds or characters.
        }
    }
}
