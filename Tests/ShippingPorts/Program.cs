using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Reflection;
using HarmonyLib;
using PraetorisClient;
using More_World_Locations_AIO;

internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        _checks++;
    }

    private static void Main()
    {
        const string a = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        const string b = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
        BepInEx.Bootstrap.Chainloader.PluginInfos[ShippingPortDepartures.MwlGuid] = new object();
        ShippingPortDepartures.Initialize(new Harmony("shipping-port-tests"));
        PortUI.instance = new PortUI();
        ZDO origin = new() { Guid = a, Name = "Port A", Prefab = "MWL_PortTrader".GetStableHashCode() };
        PortUI.instance.m_currentPort = new Port { m_view = new ZNetView { Data = origin } };
        Port.PortInfo destination = new();

        Check(PortUI.Quote(destination).Count == 1, "Disabled by default");
        PraetorisClientPlugin.FreeDeparturePortGuid.Value = a.ToUpperInvariant();
        Check(PortUI.Quote(destination).Count == 0, "Selected departure displays no cost");
        Check(PortUI.Pay(destination, 0) == 0, "Selected departure accepts zero coins");
        origin.Name = "Renamed port";
        Check(PortUI.Quote(destination).Count == 0, "Rename retains exemption");
        origin.Guid = b;
        Check(PortUI.Quote(destination).Count == 1, "Return trip retains cost");
        Check(PortUI.Pay(destination, 99) == -1, "Normal departure rejects insufficient coins");
        Check(PortUI.Pay(destination, 150) == 50, "Normal departure consumes normal price");
        foreach (string invalid in new[] { "", "invalid", b + "," + a })
        {
            PraetorisClientPlugin.FreeDeparturePortGuid.Value = invalid;
            Check(PortUI.Quote(destination).Count == 1, "Invalid selection retains cost: " + invalid);
        }
        PraetorisClientPlugin.FreeDeparturePortGuid.Value = a;
        origin.Guid = "";
        Check(PortUI.Quote(destination).Count == 1, "Uninitialized port retains cost");
        origin.Guid = a;
        PortUI.instance.m_currentPort.m_view.Valid = false;
        Check(PortUI.Quote(destination).Count == 1, "Invalid network view retains cost");
        PortUI.instance.m_currentPort = null;
        Check(PortUI.Quote(destination).Count == 1, "Missing departure retains cost");

        ZNet.instance = new ZNet { Server = true };
        ZDOMan.instance = new ZDOMan { Data = origin };
        ZRoutedRpc.instance = new ZRoutedRpc();
        ShippingPortDepartures.Register(ZRoutedRpc.instance);
        PraetorisClientPlugin.FreeDeparturePortGuid.Value = "";
        Request("set", 20);
        Check(PraetorisClientPlugin.FreeDeparturePortGuid.Value == "", "Non-admin request rejected");
        Request("set", 10);
        Check(PraetorisClientPlugin.FreeDeparturePortGuid.Value == a, "Admin selection reads server GUID");
        Check(PraetorisClientPlugin.Instance!.Config.Saves == 1, "Selection saved");
        Request("status", 10);
        Check(ZRoutedRpc.instance.LastMessage.Contains(a), "Status reports selected GUID");
        origin.Prefab = "piece_chest".GetStableHashCode();
        origin.Guid = b;
        Request("set", 10);
        Check(PraetorisClientPlugin.FreeDeparturePortGuid.Value == a, "Non-port record rejected");
        origin.Prefab = "PortTrader".GetStableHashCode();
        Request("set", 10);
        Check(PraetorisClientPlugin.FreeDeparturePortGuid.Value == b, "Legacy trader supported and selection replaced");
        origin.Guid = "invalid";
        Request("set", 10);
        Check(PraetorisClientPlugin.FreeDeparturePortGuid.Value == b, "Invalid server GUID rejected");
        Request("clear", 20);
        Check(PraetorisClientPlugin.FreeDeparturePortGuid.Value == b, "Non-admin clear rejected");
        Request("clear", 10);
        Check(PraetorisClientPlugin.FreeDeparturePortGuid.Value == "", "Admin clear disables exemption");
        Check(PraetorisClientPlugin.Instance.Config.Saves == 3, "Changes persisted only after accepted writes");
        ZNet.instance.Server = false;
        Request("set", 10);
        Check(PraetorisClientPlugin.FreeDeparturePortGuid.Value == "", "Clients do not accept configuration requests");
        System.Console.WriteLine("Passed " + _checks + " shipping port checks with simulated game records and patch application.");
    }

    private static void Request(string operation, long sender)
    {
        ZPackage package = new();
        package.Write(operation);
        package.Write(ZDOID.None);
        ZRoutedRpc.instance!.Handlers["PraetorisClient_ShippingPort_FreeRequest"](sender, package);
    }
}

// The feature source is linked unchanged. Game records and patch application are simulated.
namespace More_World_Locations_AIO
{
    public class Port
    {
        public ZNetView m_view = new();
        public class PortInfo { }
    }
    public class PortUI
    {
        public static PortUI? instance;
        public Port? m_currentPort;
        public static Action<object>? AfterRequirements;
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static List<int> GetTeleportRequirements(Port.PortInfo destination)
        {
            List<int> requirements = new() { 100 };
            AfterRequirements?.Invoke(requirements);
            return requirements;
        }
        public static List<int> Quote(Port.PortInfo destination) => GetTeleportRequirements(destination);
        public static int Pay(Port.PortInfo destination, int coins)
        {
            List<int> requirements = GetTeleportRequirements(destination);
            int cost = requirements.Count == 0 ? 0 : requirements[0];
            return coins < cost ? -1 : coins - cost;
        }
    }
}
namespace HarmonyLib
{
    public static class AccessTools
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        public static Type? TypeByName(string name) => typeof(AccessTools).Assembly.GetType(name);
        public static FieldInfo? Field(Type type, string name) => type.GetField(name, Flags);
        public static MethodInfo? DeclaredMethod(Type type, string name, Type[] parameters) => type.GetMethod(name, Flags, null, parameters, null);
    }
    public sealed class HarmonyMethod
    {
        public readonly MethodInfo Method;
        public HarmonyMethod(Type type, string name) => Method = type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
    }
    public sealed class Harmony
    {
        public Harmony(string name) { }
        public void Patch(MethodInfo method, HarmonyMethod postfix)
        {
            if (method.Name != "GetTeleportRequirements") throw new Exception("Wrong patch target.");
            PortUI.AfterRequirements = (Action<object>)Delegate.CreateDelegate(typeof(Action<object>), postfix.Method);
        }
    }
}
namespace BepInEx.Bootstrap
{
    public static class Chainloader { public static Dictionary<string, object> PluginInfos = new(); }
}
namespace PraetorisClient
{
    internal sealed class Setting { public string Value = ""; }
    internal sealed class ConfigFile { public int Saves; public void Save() => Saves++; }
    internal sealed class Logger { public void LogInfo(string text) => System.Console.WriteLine(text); public void LogWarning(string text) => System.Console.WriteLine(text); }
    internal sealed class PraetorisClientPlugin
    {
        public static PraetorisClientPlugin? Instance = new();
        public static Setting FreeDeparturePortGuid = new();
        public static Logger Log = new();
        public ConfigFile Config = new();
    }
    internal static class PlayerResolver
    {
        public static ZNetPeer? FindPeerBySender(long sender) => new() { Host = sender == 10 ? "admin" : "player" };
        public static string SafeHostName(ZNetPeer peer) => peer.Host;
    }
}
namespace UnityEngine
{
    public struct Vector3 { public static float Distance(Vector3 first, Vector3 second) => 0; }
    public sealed class Transform { public Vector3 position; }
}
public struct ZDOID { public static ZDOID None; }
public class ZDO
{
    public string Guid = "", Name = "";
    public int Prefab;
    public ZDOID m_uid;
    public int GetPrefab() => Prefab;
    public string GetString(string key) => key == "PortGUID" ? Guid : Name;
}
public class ZNetView
{
    public bool Valid = true;
    public ZDO Data = new();
    public UnityEngine.Transform transform = new();
    public bool IsValid() => Valid;
    public ZDO GetZDO() => Data;
}
public class Player { public static Player? m_localPlayer; public UnityEngine.Transform transform = new(); }
public class ZNet
{
    public static ZNet? instance;
    public bool Server;
    public bool IsServer() => Server;
    public bool IsAdmin(string host) => host == "admin";
    public bool LocalPlayerIsAdminOrHost() => true;
}
public class ZNetPeer { public string Host = ""; }
public class ZDOMan { public static ZDOMan? instance; public ZDO? Data; public ZDO? GetZDO(ZDOID id) => Data; }
public class ZPackage
{
    private readonly Queue<object> _values = new();
    public void Write(string value) => _values.Enqueue(value);
    public void Write(ZDOID value) => _values.Enqueue(value);
    public string ReadString() => (string)_values.Dequeue();
    public ZDOID ReadZDOID() => (ZDOID)_values.Dequeue();
}
public class ZRoutedRpc
{
    public static ZRoutedRpc? instance;
    public Dictionary<string, Action<long, ZPackage>> Handlers = new();
    public string LastMessage = "";
    public void Register<T>(string name, Action<long, T> handler) => Handlers[name] = (Action<long, ZPackage>)(object)handler;
    public long GetServerPeerID() => 1;
    public void InvokeRoutedRPC(long target, string name, ZPackage package) => LastMessage = package.ReadString();
}
public class Terminal
{
    public void AddString(string value) { }
    public class Args
    {
        public int Length;
        public Terminal Context = new();
        public string this[int index] => "";
    }
    public class ConsoleCommand
    {
        public ConsoleCommand(string name, string help, Action<Args> command, bool onlyAdmin) { }
    }
}
public class Console : Terminal { public static Console? instance; }
public static class StringExtensions
{
    public static int GetStableHashCode(this string value)
    {
        unchecked { int hash = 17; foreach (char character in value) hash = hash * 31 + character; return hash; }
    }
}
