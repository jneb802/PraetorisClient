using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace PraetorisClient.RoadTravel;

/// <summary>
/// One-way road travel from a roadside stop to a shipping port connected to it by road. The server lists the connected
/// ports and approves each trip; More World Locations' port teleport map shows them, charges its own teleport cost and
/// teleports the player to the port.
/// </summary>
internal static class RoadTravelService
{
    private const string Request = "Praetoris.RoadTravel.Request2", Response = "Praetoris.RoadTravel.Response2";
    /// <summary>The furthest a player may stand from a stop's driver to ask for or take a trip from it.</summary>
    private const float StopReach = 8f;
    internal sealed class Destination
    {
        internal ZDOID Id;
        internal string Name = "", Route = "", Identity = "", PortGuid = "";
    }
    private sealed class Quote
    {
        internal string Id = "";
        internal ZDOID Source;
        internal string SourceIdentity = "";
        internal ZNetPeer? Peer;
        internal float Expires;
        internal readonly Dictionary<string, (ZDOID Id, string Identity)> Ports = new(StringComparer.Ordinal);
    }
    private static readonly Dictionary<long, Quote> Quotes = new();
    private static readonly Dictionary<long, float> Requests = new();
    private static ZNet? _session;
    private static float _requestDeadline;
    private static string _menu = "", _awaiting = "";
    private static Vector3 _origin;
    private static Action? _proceed;
    private static readonly List<Destination> Entries = new();
    internal static bool Ready => RoadTravelPiece.Enabled != null && RoadTravelPiece.Enabled.Value && RoadTravelBridge.Available && RoadTravelBridge.Ready() && ZNet.instance != null;
    internal static string Status => RoadTravelPiece.Enabled == null || !RoadTravelPiece.Enabled.Value ? "Road travel is disabled." :
        !RoadTravelBridge.Available ? RoadTravelBridge.Status : ZNet.instance == null ? "Join a world to use road travel." :
        !RoadTravelBridge.Ready() ? (ZNet.instance.IsServer() ? "The road network is unavailable. Ask a server administrator to check it." : "Waiting for the server's road network.") :
        "Road travel is available.";
    internal static string LastResult { get; private set; } = "No road travel requested.";
    /// <summary>The ports the server offered for the last stop opened.</summary>
    internal static IReadOnlyList<Destination> Destinations => Entries;

    internal static void Register(ZRoutedRpc rpc)
    {
        rpc.Register<ZPackage>(Request, OnRequest);
        rpc.Register<ZPackage>(Response, OnResponse);
    }

    /// <summary>Asks the server which ports are connected to the stop; the port map opens with its answer.</summary>
    internal static void Open(ZDOID stop, Vector3 stopPosition)
    {
        if (!Ready) { Show(Status); return; }
        if (_awaiting.Length > 0) { Show("A travel request is already active."); return; }
        _menu = Guid.NewGuid().ToString("N"); _proceed = null; Entries.Clear();
        _origin = stopPosition;
        _requestDeadline = Time.unscaledTime + 15;
        ZPackage package = new(); package.Write("list"); package.Write(_menu); package.Write(stop);
        Send(package);
    }

    /// <summary>The port map's confirmation: ask the server, and pay and teleport (<paramref name="proceed"/>) once it approves.</summary>
    private static void Approve(string portGuid, Action proceed)
    {
        if (_menu.Length == 0 || _awaiting.Length > 0) return;
        _awaiting = _menu; _proceed = proceed;
        _requestDeadline = Time.unscaledTime + 15;
        ZPackage package = new(); package.Write("travel"); package.Write(_menu); package.Write(portGuid);
        Send(package);
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
            if (operation == "list")
            {
                string nonce = package.ReadString(); ZDOID id = package.ReadZDOID();
                if (nonce.Length != 32 || (Requests.TryGetValue(sender, out float next) && Time.unscaledTime < next)) return;
                Requests[sender] = Time.unscaledTime + 0.5f;
                if (!TryResolveStop(id, out Destination source) || Vector3.Distance(position, Position(source)) > StopReach)
                { Reject(sender, nonce, "Stand beside a road travel wagon."); return; }
                Quote quote = new() { Id = nonce, Source = id, SourceIdentity = source.Identity, Peer = peer, Expires = Time.unscaledTime + 120 };
                List<Destination> ports = ConnectedPorts(source);
                ZPackage result = new(); result.Write("list"); result.Write(nonce); result.Write(ports.Count);
                foreach (Destination port in ports)
                {
                    quote.Ports[port.PortGuid] = (port.Id, port.Identity);
                    result.Write(port.Id); result.Write(port.Name); result.Write(port.PortGuid);
                }
                Quotes[sender] = quote; Reply(sender, result); return;
            }
            if (operation == "travel")
            {
                string nonce = package.ReadString(); string guid = package.ReadString();
                if (!Quotes.TryGetValue(sender, out Quote quote) || quote.Id != nonce || !ReferenceEquals(quote.Peer, peer) ||
                    Time.unscaledTime > quote.Expires || !quote.Ports.TryGetValue(guid, out (ZDOID Id, string Identity) offered))
                { Reject(sender, nonce, "The travel offer expired. Talk to the driver again."); return; }
                Quotes.Remove(sender); // Consume the offer before replying. Repeated input cannot approve a second trip.
                if (!TryResolveStop(quote.Source, out Destination source) || source.Identity != quote.SourceIdentity ||
                    Vector3.Distance(position, Position(source)) > StopReach ||
                    !TryResolvePort(offered.Id, out Destination port) || port.Identity != offered.Identity ||
                    !RoadTravelBridge.Connected(source.Route, port.Route))
                { Reject(sender, nonce, "The road connection or the port changed. Talk to the driver again."); return; }
                ZPackage result = new(); result.Write("travel"); result.Write(nonce); result.Write(guid);
                Reply(sender, result);
                PraetorisClientPlugin.Log.LogInfo("Approved road travel to " + port.Name + ".");
            }
        }
        catch (Exception e) when (e is IOException || e is ArgumentException)
        { PraetorisClientPlugin.Log.LogWarning("Rejected malformed road travel request: " + e.Message); }
    }
    private static void Reject(long sender, string nonce, string message)
    {
        ZPackage result = new(); result.Write("error"); result.Write(nonce); result.Write(message); Reply(sender, result);
    }
    private static Vector3 Position(Destination stop) => ZDOMan.instance.GetZDO(stop.Id)?.GetPosition() ?? Vector3.positiveInfinity;

    /// <summary>A built stop beside a road: its route, and its saved identity (set by the server on first use).</summary>
    internal static bool TryResolveStop(ZDOID id, out Destination stop)
    {
        stop = new();
        if (!Ready || ZDOMan.instance == null || ZDOMan.instance.GetZDO(id) is not ZDO zdo) return false;
        if (zdo.GetPrefab() != RoadTravelPiece.StopPrefab.GetStableHashCode() ||
            !RoadTravelBridge.Find(zdo.GetPosition(), 32, out string route, out Vector3 center, out _, out float width)) return false;
        float distance = Utils.DistanceXZ(center, zdo.GetPosition());
        if (Mathf.Abs(distance - (width * 0.5f + Mathf.Clamp(RoadTravelPiece.Clearance.Value, 1, 4))) > 0.8f) return false;
        string identity = zdo.GetString(RoadStop.IdentityKey);
        if (string.IsNullOrEmpty(identity) && ZNet.instance.IsServer())
        { identity = Guid.NewGuid().ToString("N"); zdo.Set(RoadStop.IdentityKey, identity); }
        if (!Guid.TryParse(identity, out _)) return false;
        stop = new Destination { Id = id, Route = route, Identity = World + ":stop:" + identity,
            Name = "Road stop (" + Mathf.RoundToInt(zdo.GetPosition().x) + ", " + Mathf.RoundToInt(zdo.GetPosition().z) + ")" };
        return true;
    }

    /// <summary>A shipping port's trader, resolved to its location and the road that ends there.</summary>
    internal static bool TryResolvePort(ZDOID id, out Destination port)
    {
        port = new();
        if (!Ready || ZDOMan.instance == null || ZDOMan.instance.GetZDO(id) is not ZDO zdo) return false;
        string guid = zdo.GetString("PortGUID");
        if (string.IsNullOrEmpty(guid) || !RoadTravelBridge.Location(zdo, out Vector3 location) ||
            !RoadTravelBridge.Access(location, out string route, out _, out _, out _)) return false;
        port = new Destination { Id = id, Route = route, PortGuid = guid, Identity = World + ":port:" + guid,
            Name = zdo.GetString("PortName", "Shipping port") };
        return true;
    }

    private static List<Destination> ConnectedPorts(Destination stop)
    {
        List<Destination> result = new();
        foreach (ZDO candidate in RoadTravelBridge.Ports())
            if (TryResolvePort(candidate.m_uid, out Destination port) && RoadTravelBridge.Connected(stop.Route, port.Route)) result.Add(port);
        return result.OrderBy(d => d.Name, StringComparer.Ordinal).ThenBy(d => d.Id.ToString(), StringComparer.Ordinal).ToList();
    }

    private static void OnResponse(long sender, ZPackage package)
    {
        if (ZNet.instance == null || ZRoutedRpc.instance == null || sender != ZRoutedRpc.instance.GetServerPeerID()) return;
        try
        {
            string operation = package.ReadString();
            string nonce = package.ReadString();
            if (nonce != _menu) return;
            if (operation == "error") { _awaiting = ""; _proceed = null; Show(package.ReadString()); return; }
            if (operation == "list")
            {
                _requestDeadline = 0;
                Entries.Clear(); int count = package.ReadInt();
                if (count < 0 || count > 10000) return;
                for (int i = 0; i < count; i++) Entries.Add(new Destination { Id = package.ReadZDOID(), Name = package.ReadString(), PortGuid = package.ReadString() });
                LastResult = "Road travel destinations: " + count;
                if (count == 0) { Show("No shipping port is connected to this road."); return; }
                HashSet<string> guids = new(Entries.Select(e => e.PortGuid), StringComparer.Ordinal);
                if (!RoadTravelBridge.OpenMap(_origin, "Road travel wagon", guids, Approve)) Show("Road travel needs the map.");
                return;
            }
            if (operation == "travel" && _awaiting == nonce && _proceed != null)
            {
                string guid = package.ReadString();
                Action proceed = _proceed;
                _awaiting = _menu = ""; _proceed = null;
                proceed();
                LastResult = "Road travel approved to " + (Entries.FirstOrDefault(e => e.PortGuid == guid)?.Name ?? guid) + ".";
                PraetorisClientPlugin.Log.LogInfo(LastResult);
            }
        }
        catch (Exception e) when (e is IOException || e is ArgumentException)
        { Show("Road travel response was invalid: " + e.Message); }
    }
    private static string World => ZNet.instance?.GetWorldUID().ToString() ?? "";

    internal static void Update()
    {
        if (!ReferenceEquals(_session, ZNet.instance))
        {
            _session = ZNet.instance;
            Quotes.Clear(); Requests.Clear(); Entries.Clear(); _menu = _awaiting = ""; _proceed = null;
        }
        if (_awaiting.Length > 0 && Time.unscaledTime > _requestDeadline)
        { _awaiting = _menu = ""; _proceed = null; Show("Road travel request timed out. No coins were taken."); }
    }

    internal static void Show(string text)
    {
        LastResult = text; Player.m_localPlayer?.Message(MessageHud.MessageType.Center, text);
        PraetorisClientPlugin.Log.LogInfo(text);
    }
}
