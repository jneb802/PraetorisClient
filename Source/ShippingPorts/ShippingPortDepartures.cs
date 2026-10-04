using BepInEx.Bootstrap;
using HarmonyLib;
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace PraetorisClient
{
    internal static class ShippingPortDepartures
    {
        internal const string MwlGuid = "warpalicious.More_World_Locations_AIO";
        private const string RequestRpc = "PraetorisClient_ShippingPort_FreeRequest";
        private const string ResponseRpc = "PraetorisClient_ShippingPort_FreeResponse";
        private const string GuidKey = "PortGUID";
        private static FieldInfo? _uiInstance;
        private static FieldInfo? _currentPort;
        private static FieldInfo? _portView;
        private static bool _available;
        private static bool _failureLogged;

        internal static void Initialize(Harmony harmony)
        {
            RegisterCommand();
            if (!Chainloader.PluginInfos.ContainsKey(MwlGuid)) return;
            try
            {
                Type uiType = AccessTools.TypeByName("More_World_Locations_AIO.PortUI")
                    ?? throw new TypeLoadException("MWL PortUI is unavailable.");
                Type portType = AccessTools.TypeByName("More_World_Locations_AIO.Port")
                    ?? throw new TypeLoadException("MWL Port is unavailable.");
                Type destinationType = portType.GetNestedType("PortInfo")
                    ?? throw new TypeLoadException("MWL PortInfo is unavailable.");
                _uiInstance = AccessTools.Field(uiType, "instance")
                    ?? throw new MissingFieldException(uiType.FullName, "instance");
                _currentPort = AccessTools.Field(uiType, "m_currentPort")
                    ?? throw new MissingFieldException(uiType.FullName, "m_currentPort");
                _portView = AccessTools.Field(portType, "m_view")
                    ?? throw new MissingFieldException(portType.FullName, "m_view");
                MethodInfo requirements = AccessTools.DeclaredMethod(uiType, "GetTeleportRequirements", new[] { destinationType })
                    ?? throw new MissingMethodException(uiType.FullName, "GetTeleportRequirements");
                if (!requirements.IsStatic || !typeof(IList).IsAssignableFrom(requirements.ReturnType))
                    throw new InvalidOperationException("MWL teleport requirements have an unsupported signature.");
                harmony.Patch(requirements, postfix: new HarmonyMethod(typeof(ShippingPortDepartures), nameof(ApplyFreeDeparture)));
                _available = true;
                PraetorisClientPlugin.Log.LogInfo("MWL free departure compatibility is ready.");
            }
            catch (Exception exception)
            {
                PraetorisClientPlugin.Log.LogWarning("MWL free departures are unavailable. Normal prices remain in effect: " + exception);
            }
        }

        // MWL uses this result for both the displayed cost and the actual payment.
        private static void ApplyFreeDeparture(object __result)
        {
            if (!Guid.TryParse(PraetorisClientPlugin.FreeDeparturePortGuid.Value, out Guid selected)) return;
            try
            {
                ZNetView? view = GetCurrentPortView();
                if (view == null || !view.IsValid()) return;
                if (Guid.TryParse(view.GetZDO().GetString(GuidKey), out Guid departure) && departure == selected)
                    ((IList)__result).Clear();
            }
            catch (Exception exception)
            {
                if (_failureLogged) return;
                _failureLogged = true;
                PraetorisClientPlugin.Log.LogWarning("Could not read the MWL departure port. Normal prices remain in effect: " + exception.Message);
            }
        }

        private static ZNetView? GetCurrentPortView()
        {
            object? ui = _uiInstance?.GetValue(null);
            object? port = ui == null ? null : _currentPort?.GetValue(ui);
            return port == null ? null : _portView?.GetValue(port) as ZNetView;
        }

        private static void RegisterCommand()
        {
            _ = new Terminal.ConsoleCommand("shippingport_free",
                "MWL free departures: shippingport_free guid|set|clear|status. Open the port panel before guid or set.",
                args =>
                {
                    string operation = args.Length == 2 ? args[1].ToLowerInvariant() : "status";
                    if (args.Length > 2 || (operation != "guid" && operation != "set" && operation != "clear" && operation != "status"))
                    {
                        args.Context.AddString("Usage: shippingport_free guid|set|clear|status");
                        return;
                    }
                    if (ZNet.instance == null || (!ZNet.instance.IsServer() && !Jotunn.Managers.SynchronizationManager.Instance.PlayerIsAdmin))
                    {
                        args.Context.AddString("Only an administrator or host can use this command.");
                        return;
                    }
                    ZDOID id = ZDOID.None;
                    if (operation == "guid" || operation == "set")
                    {
                        ZNetView? view = _available ? GetCurrentPortView() : null;
                        if (view == null || !view.IsValid() || Player.m_localPlayer == null ||
                            Vector3.Distance(Player.m_localPlayer.transform.position, view.transform.position) > 10f)
                        {
                            args.Context.AddString("Open the shipping port panel while standing beside its trader first.");
                            return;
                        }
                        if (operation == "guid")
                        {
                            args.Context.AddString("PortGUID: " + view.GetZDO().GetString(GuidKey));
                            return;
                        }
                        id = view.GetZDO().m_uid;
                    }
                    if (ZNet.instance.IsServer())
                    {
                        args.Context.AddString(Execute(operation, id));
                        return;
                    }
                    if (ZRoutedRpc.instance == null) return;
                    ZPackage package = new();
                    package.Write(operation);
                    package.Write(id);
                    ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RequestRpc, package);
                    args.Context.AddString("Shipping port request sent to the server.");
                });
        }

        internal static void Register(ZRoutedRpc rpc)
        {
            rpc.Register<ZPackage>(RequestRpc, OnRequest);
            rpc.Register<ZPackage>(ResponseRpc, OnResponse);
        }

        private static void OnRequest(long sender, ZPackage package)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            ZNetPeer? peer = PlayerResolver.FindPeerBySender(sender);
            string host = peer == null ? "" : PlayerResolver.SafeHostName(peer);
            if (peer == null || string.IsNullOrWhiteSpace(host) || !ZNet.instance.IsAdmin(host))
            {
                SendResponse(sender, "Only a server administrator can change free departures.");
                return;
            }
            try
            {
                string operation = package.ReadString();
                ZDOID id = package.ReadZDOID();
                SendResponse(sender, Execute(operation, id));
            }
            catch (Exception exception)
            {
                PraetorisClientPlugin.Log.LogWarning("Rejected shipping port request: " + exception.Message);
                SendResponse(sender, "The shipping port request failed.");
            }
        }

        private static string Execute(string operation, ZDOID id)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || PraetorisClientPlugin.Instance == null)
                return "Shipping port settings can only be changed on the server.";
            if (operation == "status")
            {
                string guid = PraetorisClientPlugin.FreeDeparturePortGuid.Value;
                return string.IsNullOrWhiteSpace(guid) ? "Free departures are disabled." : "Free departure PortGUID: " + guid;
            }
            if (operation == "clear")
            {
                PraetorisClientPlugin.FreeDeparturePortGuid.Value = "";
                PraetorisClientPlugin.Instance.Config.Save();
                PraetorisClientPlugin.Instance.Config.Reload();
                return "Free departures are disabled. All ports use the normal price.";
            }
            if (operation != "set") return "Unknown shipping port operation.";
            if (!_available) return "MWL free departure compatibility is unavailable on the server.";
            ZDO? zdo = ZDOMan.instance?.GetZDO(id);
            if (zdo == null || (zdo.GetPrefab() != "MWL_PortTrader".GetStableHashCode() && zdo.GetPrefab() != "PortTrader".GetStableHashCode()) ||
                !Guid.TryParse(zdo.GetString(GuidKey), out Guid selected))
                return "The selected object is not an initialized MWL shipping port.";
            PraetorisClientPlugin.FreeDeparturePortGuid.Value = selected.ToString();
            PraetorisClientPlugin.Instance.Config.Save();
            PraetorisClientPlugin.Instance.Config.Reload();
            return "Free departures enabled for " + zdo.GetString("PortName") + " (PortGUID: " + selected + "). Trips to this port keep the normal price.";
        }

        private static void SendResponse(long target, string message)
        {
            ZPackage package = new();
            package.Write(message);
            ZRoutedRpc.instance?.InvokeRoutedRPC(target, ResponseRpc, package);
        }

        private static void OnResponse(long sender, ZPackage package)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer() || ZRoutedRpc.instance == null ||
                sender != ZRoutedRpc.instance.GetServerPeerID()) return;
            if (Console.instance != null) Console.instance.AddString(package.ReadString());
        }
    }
}
