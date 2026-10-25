using System;
using System.Collections.Generic;
using System.Linq;
using SoftReferenceableAssets;
using UnityEngine;

namespace PraetorisClient
{
    internal static class ShippingPortRestoration
    {
        private const string RequestRpc = "PraetorisClient_PortRestore_Request";
        private const string ResponseRpc = "PraetorisClient_PortRestore_Response";
        private static readonly HashSet<int> TraderHashes = new()
        {
            "PortTrader".GetStableHashCode(), "MWL_PortTrader".GetStableHashCode()
        };

        internal static void Initialize()
        {
            _ = new Terminal.ConsoleCommand("shippingport_restore",
                "Restore a loaded MWL port: shippingport_restore <preview|repair|rebuild> <PortGUID>. Preview changes nothing; rebuild also repairs. Stand near the port to load it.",
                args =>
                {
                    if (args.Length != 3 || !ValidOperation(args[1]) || !Guid.TryParse(args[2], out Guid guid))
                    {
                        args.Context.AddString("Usage: shippingport_restore <preview|repair|rebuild> <PortGUID>");
                        return;
                    }
                    if (ZNet.instance == null) return;
                    if (ZNet.instance.IsServer())
                        args.Context.AddString(Execute(args[1], guid));
                    else if (Jotunn.Managers.SynchronizationManager.Instance.PlayerIsAdmin)
                    {
                        ZPackage package = new();
                        package.Write(args[1]);
                        package.Write(guid.ToString());
                        ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RequestRpc, package);
                        args.Context.AddString("Port restoration request sent to the server.");
                    }
                    else args.Context.AddString("Only a server administrator can restore a shipping port.");
                });
        }

        internal static void Register(ZRoutedRpc rpc)
        {
            rpc.Register<ZPackage>(RequestRpc, OnRequest);
            rpc.Register<ZPackage>(ResponseRpc, OnResponse);
        }

        private static bool ValidOperation(string operation) =>
            operation == "preview" || operation == "repair" || operation == "rebuild";

        private static void OnRequest(long sender, ZPackage package)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            ZNetPeer? peer = PlayerResolver.FindPeerBySender(sender);
            string host = peer == null ? "" : PlayerResolver.SafeHostName(peer);
            string result = "Only a server administrator can restore a shipping port.";
            if (peer != null && !string.IsNullOrWhiteSpace(host) && ZNet.instance.IsAdmin(host))
            {
                try
                {
                    string operation = package.ReadString();
                    result = ValidOperation(operation) && Guid.TryParse(package.ReadString(), out Guid guid)
                        ? Execute(operation, guid) : "Invalid port restoration request.";
                }
                catch (Exception exception)
                {
                    result = "Invalid port restoration request: " + exception.Message;
                }
            }
            ZPackage response = new();
            response.Write(result);
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, ResponseRpc, response);
        }

        private static void OnResponse(long sender, ZPackage package)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer() ||
                sender != ZRoutedRpc.instance.GetServerPeerID()) return;
            string result = package.ReadString();
            if (Console.instance != null) Console.instance.AddString(result);
            PraetorisClientPlugin.Log.LogInfo(result);
        }

        private static string Execute(string operation, Guid guid)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null || ZoneSystem.instance == null)
                return "Port restoration requires a running server world.";
            try
            {
                List<ZDO> traders = ZDOMan.instance.m_objectsByID.Values.Where(zdo =>
                    TraderHashes.Contains(zdo.GetPrefab()) && Guid.TryParse(zdo.GetString("PortGUID"), out Guid value) && value == guid).ToList();
                if (traders.Count != 1) return "Refused: expected exactly one saved trader with this PortGUID; found " + traders.Count + ".";
                ZDO trader = traders[0];
                List<LocationProxy> proxies = UnityEngine.Object.FindObjectsByType<LocationProxy>(FindObjectsSortMode.None).Where(proxy =>
                    proxy.GetComponent<ZNetView>() is ZNetView view && view.IsValid() &&
                    Vector3.Distance(proxy.transform.position, trader.GetPosition()) < 100f).ToList();
                List<LocationProxy> ports = proxies.Where(proxy => IsPortLocation(ZoneSystem.instance.GetLocation(
                    proxy.GetComponent<ZNetView>().GetZDO().GetInt(ZDOVars.s_location)))).ToList();
                if (ports.Count != 1) return "Refused: expected one loaded MWL port location near the trader. Stand beside the port and retry.";
                LocationProxy selected = ports[0];
                ZoneSystem.ZoneLocation location = ZoneSystem.instance.GetLocation(selected.GetComponent<ZNetView>().GetZDO().GetInt(ZDOVars.s_location));
                location.m_prefab.Load();
                try
                {
                    PortStructurePlan plan = PortStructurePlan.Create(location.m_prefab.Asset, selected, trader);
                    string summary = $"Port {trader.GetString("PortName")} ({guid}): layout={location.m_prefab.Name}, existing={plan.Existing.Count}, damaged={plan.Damaged.Count}, missing={plan.Missing.Count}.";
                    if (operation == "preview")
                    {
                        string missing = string.Join(", ", plan.Missing.GroupBy(entry => Utils.GetPrefabName(entry.Template.gameObject))
                            .Select(group => group.Key + " x" + group.Count()));
                        return summary + " Missing: " + (missing.Length == 0 ? "none" : missing) + ". Preview only; no changes.";
                    }
                    int restored = 0;
                    if (operation == "rebuild")
                    {
                        foreach (PortStructurePlan.Entry entry in plan.Missing)
                        {
                            GameObject piece = UnityEngine.Object.Instantiate(entry.Template.gameObject, entry.Position, entry.Rotation);
                            piece.HoldReferenceTo(location.m_prefab);
                            ZNetView view = piece.GetComponent<ZNetView>();
                            if (!view || !view.IsValid()) throw new InvalidOperationException("A restored piece has no saved network object.");
                            view.GetZDO().Set(ZDOVars.s_preSnow, true);
                            restored++;
                        }
                    }
                    foreach (WearNTear piece in plan.Damaged) piece.Repair();
                    string result = summary + $" Restored={restored}, repairRequests={plan.Damaged.Count}. Trader, shipment chests, and terrain retained.";
                    PraetorisClientPlugin.Log.LogInfo(result);
                    return result;
                }
                finally { location.m_prefab.Release(); }
            }
            catch (Exception exception)
            {
                PraetorisClientPlugin.Log.LogWarning("Port restoration stopped: " + exception);
                return "Port restoration stopped: " + exception.Message + " Run preview to inspect the current state before retrying.";
            }
        }

        private static bool IsPortLocation(ZoneSystem.ZoneLocation? location)
        {
            if (location == null) return false;
            string name = location.m_prefab.Name;
            return name == "MWL_Port1" || name == "MWL_Port2" || name == "MWL_Port3" ||
                name == "MWL_Port4" || name == "MWL_Port5" || name == "MWL_Port6";
        }
    }
}
