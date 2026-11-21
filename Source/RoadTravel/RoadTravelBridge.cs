using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace PraetorisClient.RoadTravel;

internal static class RoadTravelBridge
{
    internal const string RoadsGuid = "warpalicious.ProceduralRoads";
    internal delegate bool FindRoad(Vector3 position, float radius, out string route, out Vector3 point, out Vector3 direction, out float width);
    internal delegate bool LocationAccess(Vector3 center, out string route, out Vector3 point, out Vector3 direction, out float width);
    internal delegate bool PortLocation(ZDO port, out Vector3 center);
    internal delegate bool PortMap(Vector3 origin, string originName, ICollection<string> portGuids, Action<string, Action> approve);
    internal static FindRoad Find = null!;
    internal static LocationAccess Access = null!;
    internal static PortLocation Location = null!;
    internal static Func<string, string, bool> Connected = null!;
    internal static Func<List<ZDO>> Ports = null!;
    internal static PortMap OpenMap = null!;
    internal static Func<bool> Ready = () => false;
    internal static bool Available { get; private set; }
    internal static string Status { get; private set; } = "Road travel requires compatible ProceduralRoads and More World Locations.";

    internal static void Initialize()
    {
        if (!Chainloader.PluginInfos.ContainsKey(RoadsGuid) || !Chainloader.PluginInfos.ContainsKey(ShippingPortDepartures.MwlGuid)) return;
        try
        {
            Type roads = AccessTools.TypeByName("ProceduralRoads.RoadTravelApi") ?? throw new TypeLoadException("Road API missing");
            Type ports = AccessTools.TypeByName("More_World_Locations_AIO.RoadTravelPorts") ?? throw new TypeLoadException("Port API missing");
            // Roads API 2 answers on clients from the server's copy, which ProceduralRoads sends itself.
            if ((int)roads.GetField("ApiVersion")!.GetRawConstantValue() != 2 || (int)ports.GetField("ApiVersion")!.GetRawConstantValue() != 2)
                throw new NotSupportedException("Road travel API version differs");
            Find = Bind<FindRoad>(roads, "TryFindRoad"); Access = Bind<LocationAccess>(roads, "TryGetLocationAccess");
            Connected = Bind<Func<string, string, bool>>(roads, "Connected");
            Ready = Bind<Func<bool>>(roads, "get_Ready");
            Ports = Bind<Func<List<ZDO>>>(ports, "GetPorts"); Location = Bind<PortLocation>(ports, "TryGetLocation");
            OpenMap = Bind<PortMap>(ports, "OpenRoadTravelMap");
            Available = true; Status = "Road travel integration ready.";
            PraetorisClientPlugin.Log.LogInfo(Status);
        }
        catch (Exception e) { Status = "Road travel unavailable: " + e.Message; PraetorisClientPlugin.Log.LogWarning(Status); }
    }
    private static T Bind<T>(Type type, string method) where T : Delegate =>
        (T)Delegate.CreateDelegate(typeof(T), type.GetMethod(method, BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException(type.FullName, method));
}
