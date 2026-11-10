using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace PraetorisClient.RoadTravel;

internal static class RoadTravelService
{
    private const string Request = "Praetoris.RoadTravel.Request1", Response = "Praetoris.RoadTravel.Response1";
    private const string PendingKey = "Praetoris.RoadTravel.Reserved1";
    internal sealed class Destination
    {
        internal ZDOID Id;
        internal string Name = "", Route = "", Identity = "";
        internal Vector3 Position;
        internal int Price;
        internal bool Port;
    }
    private sealed class Quote
    {
        internal string Id = "";
        internal ZDOID Source;
        internal bool FromPort;
        internal ZNetPeer? Peer;
        internal float Expires;
        internal Dictionary<ZDOID, int> Prices = new();
        internal string SourceIdentity = "";
        internal Dictionary<ZDOID, string> Identities = new();
    }
    private sealed class Reservation
    {
        public string World = "";
        public float OriginX, OriginY, OriginZ, TargetX, TargetY, TargetZ;
        [JsonIgnore] public Vector3 Origin { get => new(OriginX, OriginY, OriginZ); set { OriginX = value.x; OriginY = value.y; OriginZ = value.z; } }
        [JsonIgnore] public Vector3 Target { get => new(TargetX, TargetY, TargetZ); set { TargetX = value.x; TargetY = value.y; TargetZ = value.z; } }
        public int Coins;
        public bool Returning;
    }
    private static readonly Dictionary<long, Quote> Quotes = new();
    private static readonly Dictionary<long, float> Requests = new();
    private static readonly Dictionary<long, float> SyncRequests = new();
    private static readonly int StopHash = RoadTravelPiece.StopPrefab.GetStableHashCode();
    private static readonly Dictionary<ZDOID, ZDO> KnownStops = new();
    private static ZDOMan? _stopWorld;
    private static bool _stopsSeeded;
    private static bool _received;
    private static ZNet? _session;
    private static Player? _player;
    private static float _nextSync, _tripDeadline;
    private static int _sentRevision = -1;
    private static int _remoteRevision = -1;
    private static float _requestDeadline;
    private static byte[] _snapshot = Array.Empty<byte>();
    private static string _menu = "", _awaiting = "";
    private static Reservation? _reservation;
    private static readonly List<Destination> Entries = new();
    internal static bool Ready => RoadTravelPiece.Enabled != null && RoadTravelPiece.Enabled.Value && RoadTravelBridge.Available && RoadTravelBridge.Ready() && ZNet.instance != null && (ZNet.instance.IsServer() || _received);
    internal static string Status => RoadTravelPiece.Enabled == null || !RoadTravelPiece.Enabled.Value ? "Road travel is disabled." :
        !RoadTravelBridge.Available ? RoadTravelBridge.Status : ZNet.instance == null ? "Join a world to use road travel." :
        !ZNet.instance.IsServer() && !_received ? "Waiting for server road data." :
        !RoadTravelBridge.Ready() ? "The road network is unavailable. Ask a server administrator to check it." : "Road travel is available.";
    internal static string LastResult { get; private set; } = "No road travel requested.";
    internal static IReadOnlyList<Destination> Destinations => Entries;

    internal static void Register(ZRoutedRpc rpc)
    {
        rpc.Register<ZPackage>(Request, OnRequest);
        rpc.Register<ZPackage>(Response, OnResponse);
    }

    internal static void Open(ZDOID source, bool fromPort)
    {
        if (!Ready) { Show(Status); return; }
        if (_reservation != null || _awaiting.Length > 0) { Show("A travel request is already active."); return; }
        RoadTravelWindow.Close();
        _menu = Guid.NewGuid().ToString("N"); Entries.Clear();
        _requestDeadline = Time.unscaledTime + 15;
        ZPackage package = new(); package.Write("list"); package.Write(_menu); package.Write(source); package.Write(fromPort);
        Send(package);
    }
    internal static void Confirm(int index)
    {
        if (index < 0 || index >= Entries.Count || _menu.Length == 0 || _awaiting.Length > 0 || _reservation != null) return;
        _awaiting = _menu;
        _requestDeadline = Time.unscaledTime + 15;
        ZPackage package = new(); package.Write("travel"); package.Write(_menu); package.Write(Entries[index].Id);
        Send(package); RoadTravelWindow.Close();
    }
    private static void Send(ZPackage package)
    {
        if (ZNet.instance.IsServer()) { package.SetPos(0); OnRequest(ZNet.GetUID(), package); }
        else ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), Request, package);
    }
    private static void Reply(long peer, ZPackage package)
    {
        if (peer == ZNet.GetUID() && ZNet.instance.IsServer()) { package.SetPos(0); OnResponse(peer, package); }
        else ZRoutedRpc.instance.InvokeRoutedRPC(peer, Response, package);
    }
    private static bool Authenticated(long sender, out ZNetPeer? peer, out Vector3 position)
    {
        peer = PlayerResolver.FindPeerBySender(sender); position = default;
        if (peer != null && peer.IsReady() && ZDOMan.instance.GetZDO(peer.m_characterID) is ZDO player)
        { position = player.GetPosition(); return true; }
        if (sender == ZNet.GetUID() && Player.m_localPlayer != null) { position = Player.m_localPlayer.transform.position; return true; }
        return false;
    }
    private static void OnRequest(long sender, ZPackage package)
    {
        if (ZNet.instance == null || !ZNet.instance.IsServer() || !RoadTravelBridge.Available ||
            !Authenticated(sender, out ZNetPeer? peer, out Vector3 position)) return;
        try
        {
            string operation = package.ReadString();
            if (operation == "sync")
            {
                int previous = package.ReadInt();
                if (SyncRequests.TryGetValue(sender, out float next) && Time.unscaledTime < next) return;
                SyncRequests[sender] = Time.unscaledTime + 2;
                if (!Ready) { ZPackage unavailable = new(); unavailable.Write("unavailable"); Reply(sender, unavailable); return; }
                if (previous == RoadTravelBridge.Revision()) return;
                if (_sentRevision != RoadTravelBridge.Revision())
                {
                    using MemoryStream stream = new();
                    using (GZipStream zip = new(stream, CompressionMode.Compress, true))
                    { byte[] data = RoadTravelBridge.Export(); zip.Write(data, 0, data.Length); }
                    _snapshot = stream.ToArray(); _sentRevision = RoadTravelBridge.Revision();
                }
                ZPackage result = new(); result.Write("sync"); result.Write(_sentRevision); result.Write(_snapshot); Reply(sender, result); return;
            }
            if (operation == "list")
            {
                string nonce = package.ReadString(); ZDOID id = package.ReadZDOID(); bool fromPort = package.ReadBool();
                if (nonce.Length != 32 || (Requests.TryGetValue(sender, out float next) && Time.unscaledTime < next)) return;
                Requests[sender] = Time.unscaledTime + 0.5f;
                if (!TryResolve(id, fromPort, out Destination source) || Vector3.Distance(position, source.Position) > 8)
                { Reject(sender, nonce, "Stand beside a valid road travel stop or port road guide."); return; }
                Quote quote = new() { Id = nonce, Source = id, SourceIdentity = source.Identity, FromPort = fromPort, Peer = peer, Expires = Time.unscaledTime + 45 };
                List<Destination> destinations = Eligible(source);
                ZPackage result = new(); result.Write("list"); result.Write(nonce); result.Write(destinations.Count);
                foreach (Destination destination in destinations)
                {
                    destination.Price = Fare(source); quote.Prices[destination.Id] = destination.Price; quote.Identities[destination.Id] = destination.Identity;
                    result.Write(destination.Id); result.Write(destination.Name); result.Write(destination.Price);
                }
                Quotes[sender] = quote; Reply(sender, result); return;
            }
            if (operation == "travel")
            {
                string nonce = package.ReadString(); ZDOID destinationId = package.ReadZDOID();
                if (!Quotes.TryGetValue(sender, out Quote quote) || quote.Id != nonce || !ReferenceEquals(quote.Peer, peer) ||
                    Time.unscaledTime > quote.Expires || !quote.Prices.TryGetValue(destinationId, out int price))
                { Reject(sender, nonce, "The travel offer expired. Open the road travel menu again."); return; }
                Quotes.Remove(sender); // Consume the offer before replying. Repeated input cannot approve a second trip.
                if (!TryResolve(quote.Source, quote.FromPort, out Destination source) || Vector3.Distance(position, source.Position) > 8 ||
                    !TryResolve(destinationId, !quote.FromPort, out Destination destination) || source.Identity != quote.SourceIdentity ||
                    destination.Identity != quote.Identities[destinationId] || !RoadTravelBridge.Connected(source.Route, destination.Route) || price != Fare(source))
                { Reject(sender, nonce, "The road connection, destination, or price changed. No coins were taken."); return; }
                ZPackage result = new(); result.Write("travel"); result.Write(nonce); result.Write(destination.Position); result.Write(price);
                Reply(sender, result);
                PraetorisClientPlugin.Log.LogInfo("Approved road travel to " + destination.Name + " for " + price + " coins.");
            }
        }
        catch (Exception e) when (e is IOException || e is ArgumentException)
        { PraetorisClientPlugin.Log.LogWarning("Rejected malformed road travel request: " + e.Message); }
    }
    private static void Reject(long sender, string nonce, string message)
    {
        ZPackage result = new(); result.Write("error"); result.Write(nonce); result.Write(message); Reply(sender, result);
    }
    internal static bool TryResolve(ZDOID id, bool port, out Destination endpoint)
    {
        endpoint = new();
        if (!Ready || ZDOMan.instance == null || ZDOMan.instance.GetZDO(id) is not ZDO zdo) return false;
        if (port)
        {
            if (!RoadTravelBridge.Location(zdo, out Vector3 location) ||
                !RoadTravelBridge.Access(location, out string route, out Vector3 edge, out Vector3 direction, out float width)) return false;
            endpoint = new Destination { Id = id, Port = true, Route = route, Identity = World + ":port:" + zdo.GetString("PortGUID"), Name = zdo.GetString("PortName", "Shipping port"),
                Position = edge + Vector3.Cross(Vector3.up, direction) * (width * 0.5f + 1f) };
            return true;
        }
        if (zdo.GetPrefab() != RoadTravelPiece.StopPrefab.GetStableHashCode() ||
            !RoadTravelBridge.Find(zdo.GetPosition(), 32, out string stopRoute, out Vector3 center, out Vector3 tangent, out float roadWidth)) return false;
        float distance = Utils.DistanceXZ(center, zdo.GetPosition());
        if (Mathf.Abs(distance - (roadWidth * 0.5f + Mathf.Clamp(RoadTravelPiece.Clearance.Value, 1, 4))) > 0.8f) return false;
        string identity = zdo.GetString(RoadStop.IdentityKey);
        if (string.IsNullOrEmpty(identity) && ZNet.instance.IsServer())
        { identity = Guid.NewGuid().ToString("N"); zdo.Set(RoadStop.IdentityKey, identity); }
        if (!Guid.TryParse(identity, out _)) return false;
        Vector3 away = zdo.GetPosition() - center; away.y = 0; away.Normalize();
        endpoint = new Destination { Id = id, Port = false, Route = stopRoute, Identity = World + ":stop:" + identity,
            Name = "Road stop (" + Mathf.RoundToInt(zdo.GetPosition().x) + ", " + Mathf.RoundToInt(zdo.GetPosition().z) + ")",
            Position = zdo.GetPosition() + away * 2f };
        return true;
    }
    private static List<Destination> Eligible(Destination source)
    {
        List<Destination> result = new();
        IEnumerable<ZDO> candidates = source.Port ? Stops() : RoadTravelBridge.Ports();
        foreach (ZDO candidate in candidates)
            if (TryResolve(candidate.m_uid, !source.Port, out Destination destination) && RoadTravelBridge.Connected(source.Route, destination.Route)) result.Add(destination);
        return result.OrderBy(d => d.Name, StringComparer.Ordinal).ThenBy(d => d.Id.ToString(), StringComparer.Ordinal).ToList();
    }
    private static void EnsureStopWorld()
    {
        if (_stopWorld == ZDOMan.instance) return;
        _stopWorld = ZDOMan.instance; KnownStops.Clear(); _stopsSeeded = false;
    }
    private static void ObserveStop(ZDO data)
    {
        if (data.GetPrefab() != StopHash) return;
        EnsureStopWorld(); KnownStops[data.m_uid] = data;
    }
    private static List<ZDO> Stops()
    {
        EnsureStopWorld();
        if (!_stopsSeeded)
        {
            foreach (ZDO data in ZDOMan.instance.m_objectsByID.Values) ObserveStop(data);
            _stopsSeeded = true;
        }
        List<ZDO> result = new();
        foreach (ZDOID id in KnownStops.Keys.ToList())
        {
            ZDO? data = ZDOMan.instance.GetZDO(id);
            if (data == null || data.GetPrefab() != StopHash) KnownStops.Remove(id);
            else result.Add(data);
        }
        return result;
    }
    [HarmonyPatch(typeof(ZDO), nameof(ZDO.SetPrefab))]
    private static class NewStop
    {
        private static void Postfix(ZDO __instance) => ObserveStop(__instance);
    }
    [HarmonyPatch(typeof(ZDO), nameof(ZDO.Deserialize))]
    private static class ReceivedStop
    {
        private static void Postfix(ZDO __instance) => ObserveStop(__instance);
    }
    private static int Fare(Destination source)
    {
        if (source.Port && ZDOMan.instance.GetZDO(source.Id) is ZDO port &&
            Guid.TryParse(port.GetString("PortGUID"), out Guid id) && Guid.TryParse(PraetorisClientPlugin.FreeDeparturePortGuid.Value, out Guid free) && id == free) return 0;
        return Math.Max(0, Math.Min(1000000, RoadTravelPiece.Fare.Value));
    }
    private static void OnResponse(long sender, ZPackage package)
    {
        if (ZNet.instance == null || ZRoutedRpc.instance == null || sender != ZRoutedRpc.instance.GetServerPeerID()) return;
        try
        {
            string operation = package.ReadString();
            if (operation == "unavailable") { _received = false; _remoteRevision = -1; RoadTravelWindow.Close(); return; }
            if (operation == "sync")
            {
                if (!RoadTravelBridge.Available) return;
                int revision = package.ReadInt();
                using GZipStream zip = new(new MemoryStream(package.ReadByteArray()), CompressionMode.Decompress);
                using MemoryStream data = new(); zip.CopyTo(data);
                _received = RoadTravelBridge.Import(data.ToArray());
                _remoteRevision = _received ? revision : -1;
                if (!_received) PraetorisClientPlugin.Log.LogWarning("The server road network could not be read. Road travel is disabled.");
                if (_received) PraetorisClientPlugin.Log.LogInfo("Received authoritative road travel network.");
                return;
            }
            string nonce = package.ReadString();
            if (nonce != _menu) return;
            if (operation == "error") { _awaiting = ""; Show(package.ReadString()); return; }
            if (operation == "list")
            {
                _requestDeadline = 0;
                Entries.Clear(); int count = package.ReadInt();
                if (count < 0 || count > 10000) return;
                for (int i = 0; i < count; i++) Entries.Add(new Destination { Id = package.ReadZDOID(), Name = package.ReadString(), Price = package.ReadInt() });
                LastResult = "Road travel destinations: " + count; RoadTravelWindow.Open(); return;
            }
            if (operation == "travel" && _awaiting == nonce && _reservation == null)
            {
                _awaiting = ""; _menu = "";
                Vector3 target = package.ReadVector3(); int price = package.ReadInt();
                BeginTravel(target, price);
            }
        }
        catch (Exception e) when (e is IOException || e is ArgumentException)
        { Show("Road travel response was invalid: " + e.Message); }
    }
    private static string World => ZNet.instance?.GetWorldUID().ToString() ?? "";
    private static string Currency => ObjectDB.instance.GetItemPrefab("Coins").GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
    private static void BeginTravel(Vector3 target, int price)
    {
        Player player = Player.m_localPlayer;
        if (player == null || player.IsDead() || player.IsTeleporting() || player.m_teleportCooldown < 2 || price < 0 || price > 1000000 || !RoadTravelBridge.CanTravel(player))
        { Show("Travel is unavailable. Check carried items and wait for any current teleport to finish. No coins were taken."); return; }
        if (player.GetInventory().CountItems(Currency) < price) { Show("You do not have enough coins. No coins were taken."); return; }
        _reservation = new Reservation { World = World, Origin = player.transform.position, Target = target, Coins = price };
        player.GetInventory().RemoveItem(Currency, price);
        SaveReservation();
        if (!player.TeleportTo(target + Vector3.up, Quaternion.identity, true)) { _reservation.Returning = true; SaveReservation(); }
        _tripDeadline = Time.unscaledTime + 45;
        Show("Road travel started. Reserved " + price + " coins.");
    }
    private static void SaveReservation()
    {
        Player player = Player.m_localPlayer;
        if (player == null) return;
        if (_reservation == null) player.m_customData.Remove(PendingKey);
        else player.m_customData[PendingKey] = JsonConvert.SerializeObject(_reservation);
        Game.instance?.SavePlayerProfile(false);
    }
    internal static void Update()
    {
        if (!ReferenceEquals(_session, ZNet.instance))
        {
            _session = ZNet.instance; _received = false; _nextSync = 0; _sentRevision = _remoteRevision = -1; _snapshot = Array.Empty<byte>();
            Quotes.Clear(); Requests.Clear(); SyncRequests.Clear(); Entries.Clear(); _menu = _awaiting = ""; _reservation = null; _player = null;
            RoadTravelWindow.Close(); RoadTravelAgents.Clear();
        }
        Player player = Player.m_localPlayer;
        if (_session == null || ZRoutedRpc.instance == null || player == null) return;
        if (_player != player && RoadTravelBridge.Available) RoadTravelBridge.SetWorld(_session.GetWorldUID());
        if (RoadTravelPiece.Enabled.Value && RoadTravelBridge.Available && !_session.IsServer() && Time.unscaledTime >= _nextSync)
        {
            _nextSync = Time.unscaledTime + 5; ZPackage request = new(); request.Write("sync"); request.Write(_remoteRevision); Send(request);
        }
        if (_awaiting.Length > 0 && Time.unscaledTime > _requestDeadline)
        { _awaiting = _menu = ""; Show("Road travel request timed out. No coins were taken."); }
        if (_player != player)
        {
            _player = player; _reservation = null;
            if (player.m_customData.TryGetValue(PendingKey, out string saved))
            {
                try { _reservation = JsonConvert.DeserializeObject<Reservation>(saved); }
                catch (JsonException e) { PraetorisClientPlugin.Log.LogError("Could not recover road travel reservation: " + e.Message); }
                if (_reservation != null) { _reservation.Returning = true; _tripDeadline = Time.unscaledTime + 45; }
            }
        }
        UpdateReservation(player);
        if (Ready) RoadTravelAgents.Update();
        else RoadTravelAgents.Clear();
    }
    private static void UpdateReservation(Player player)
    {
        Reservation? trip = _reservation;
        if (trip == null || trip.World != World || player.IsDead()) return;
        if (player.IsTeleporting())
        {
            if (Time.unscaledTime < _tripDeadline) return;
            player.m_teleporting = false;
            trip.Returning = true; SaveReservation(); _tripDeadline = Time.unscaledTime + 45;
        }
        if (!trip.Returning)
        {
            if (Utils.DistanceXZ(player.transform.position, trip.Target) <= 5 && ZoneSystem.instance.FindFloor(player.transform.position, out float floor) &&
                floor > ZoneSystem.instance.m_waterLevel + 0.2f && Mathf.Abs(player.transform.position.y - floor) < 3 && player.IsOnGround())
            { int paid = trip.Coins; _reservation = null; SaveReservation(); Show("Road travel complete. Paid " + paid + " coins."); return; }
            if (Time.unscaledTime < _tripDeadline) return;
            trip.Returning = true; SaveReservation();
        }
        if (Vector3.Distance(player.transform.position, trip.Origin) > 5)
        {
            if (Time.unscaledTime > _tripDeadline) { Show("Travel could not finish. Returning to the departure point before refunding coins."); _tripDeadline = Time.unscaledTime + 45; }
            if (player.m_teleportCooldown >= 2) player.TeleportTo(trip.Origin, Quaternion.identity, true);
            return;
        }
        // Keep the reservation if there is no inventory space. No coins disappear on failed travel.
        if (trip.Coins > 0)
        {
            GameObject coins = ObjectDB.instance.GetItemPrefab("Coins");
            int stack = coins.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize;
            while (trip.Coins > 0)
            {
                int amount = Math.Min(stack, trip.Coins);
                if (!player.GetInventory().CanAddItem(coins, amount)) { Show("Make inventory space to receive the reserved road travel coins."); return; }
                player.GetInventory().AddItem(coins, amount); trip.Coins -= amount; SaveReservation();
            }
        }
        _reservation = null; SaveReservation(); Show("Travel failed. Reserved coins were returned at the departure point.");
    }
    internal static void Show(string text)
    {
        LastResult = text; Player.m_localPlayer?.Message(MessageHud.MessageType.Center, text);
        PraetorisClientPlugin.Log.LogInfo(text);
    }
}
