using System;
using System.Globalization;
using HarmonyLib;

namespace PraetorisClient.Maintenance
{
    [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
    internal static class MaintenanceConnectionRpcPatch
    {
        private static void Postfix(ZNetPeer peer)
        {
            peer.m_rpc.Register<long>(RpcNames.MaintenanceNotice, MaintenanceClientNotice.OnNotice);
        }
    }

    [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
    internal static class MaintenancePeerInfoPatch
    {
        private static bool Prefix(ZNet __instance, ZRpc rpc)
        {
            if (!__instance.IsServer() || !MaintenanceMode.TryGetActiveEnd(out DateTimeOffset endUtc))
            {
                return true;
            }

            ZNetPeer peer = __instance.GetPeer(rpc);
            if (peer != null)
            {
                MaintenanceMode.RejectPeer(peer, endUtc);
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(FejdStartup), "ShowConnectError")]
    internal static class MaintenanceConnectErrorPatch
    {
        private static void Postfix(FejdStartup __instance)
        {
            if (ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.ErrorKicked ||
                !MaintenanceClientNotice.TryConsumeMessage(out string message))
            {
                return;
            }

            __instance.m_connectionFailedPanel.SetActive(true);
            __instance.m_connectionFailedError.text = message;
        }
    }

    internal static class MaintenanceClientNotice
    {
        private static string _pendingMessage = "";
        private static DateTimeOffset _receivedUtc;

        internal static void OnNotice(ZRpc rpc, long endUnixSeconds)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer())
            {
                return;
            }

            DateTimeOffset endUtc;
            try
            {
                endUtc = DateTimeOffset.FromUnixTimeSeconds(endUnixSeconds);
            }
            catch (ArgumentOutOfRangeException)
            {
                PraetorisClientPlugin.Log.LogWarning("Received an invalid maintenance end time from the server.");
                return;
            }

            DateTimeOffset localEnd = endUtc.ToLocalTime();
            string formattedEnd = localEnd.ToString("dddd, MMMM d, yyyy 'at' h:mm tt", CultureInfo.CurrentCulture);
            _pendingMessage = "Server maintenance is in progress.\nMaintenance ends at " + formattedEnd + " (your local time).";
            _receivedUtc = DateTimeOffset.UtcNow;
            PraetorisClientPlugin.Log.LogInfo("Received server maintenance notice ending at " + endUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture) + ".");
        }

        internal static bool TryConsumeMessage(out string message)
        {
            message = "";
            if (string.IsNullOrEmpty(_pendingMessage) || DateTimeOffset.UtcNow - _receivedUtc > TimeSpan.FromMinutes(1.0))
            {
                _pendingMessage = "";
                return false;
            }

            message = _pendingMessage;
            _pendingMessage = "";
            return true;
        }
    }
}
