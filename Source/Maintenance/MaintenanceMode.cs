using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace PraetorisClient.Maintenance
{
    internal static class MaintenanceMode
    {
        private const string TimestampFormat = "O";
        private const int MaximumDurationMinutes = 10080;

        internal static string Start(string durationText)
        {
            if (!CanControl(out string error))
            {
                return error;
            }

            if (!int.TryParse(durationText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int durationMinutes) ||
                durationMinutes < 1 ||
                durationMinutes > MaximumDurationMinutes)
            {
                return $"Duration must be from 1 to {MaximumDurationMinutes} minutes.";
            }

            DateTimeOffset endUtc = DateTimeOffset.UtcNow.AddMinutes(durationMinutes);
            SetEnd(endUtc);
            int disconnectedPlayers = DisconnectConnectedPlayers(endUtc);
            string message = $"Maintenance active until {FormatUtc(endUtc)}. Disconnecting {disconnectedPlayers} connected player(s).";
            PraetorisClientPlugin.Log.LogInfo(message);
            return message;
        }

        internal static string End()
        {
            if (!CanControl(out string error))
            {
                return error;
            }

            ClearEnd();
            const string message = "Maintenance ended. New player connections are allowed.";
            PraetorisClientPlugin.Log.LogInfo(message);
            return message;
        }

        internal static string Status()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return "Maintenance status is available only on the server.";
            }

            int connectedPlayers = ZNet.instance.GetConnectedPeers().Count;
            if (!TryGetActiveEnd(out DateTimeOffset endUtc))
            {
                return $"Maintenance inactive. Connected players: {connectedPlayers}.";
            }

            TimeSpan remaining = endUtc - DateTimeOffset.UtcNow;
            long remainingSeconds = Math.Max(0L, (long)Math.Ceiling(remaining.TotalSeconds));
            return $"Maintenance active until {FormatUtc(endUtc)}. Remaining seconds: {remainingSeconds}. Connected players: {connectedPlayers}.";
        }

        internal static bool TryGetActiveEnd(out DateTimeOffset endUtc)
        {
            endUtc = default;
            string configuredEnd = PraetorisClientPlugin.MaintenanceEndUtc.Value.Trim();
            if (string.IsNullOrEmpty(configuredEnd))
            {
                return false;
            }

            if (!DateTimeOffset.TryParseExact(
                    configuredEnd,
                    TimestampFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out endUtc))
            {
                PraetorisClientPlugin.Log.LogError("Maintenance EndUtc is invalid. Maintenance will remain inactive until the value is corrected.");
                return false;
            }

            endUtc = endUtc.ToUniversalTime();
            if (endUtc > DateTimeOffset.UtcNow)
            {
                return true;
            }

            ClearEnd();
            PraetorisClientPlugin.Log.LogInfo("Maintenance expired. New player connections are allowed.");
            return false;
        }

        internal static void RejectPeer(ZNetPeer peer, DateTimeOffset endUtc)
        {
            if (peer == null || peer.m_rpc == null || ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return;
            }

            SendNotice(peer, endUtc);
            ScheduleKick(peer);
            PraetorisClientPlugin.Log.LogInfo("Rejected a player connection because maintenance is active.");
        }

        private static bool CanControl(out string error)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer())
            {
                error = "Maintenance can be changed only on the server.";
                return false;
            }

            error = "";
            return true;
        }

        private static int DisconnectConnectedPlayers(DateTimeOffset endUtc)
        {
            List<ZNetPeer> peers = ZNet.instance.GetConnectedPeers().ToList();
            foreach (ZNetPeer peer in peers)
            {
                peer.m_rpc.Invoke("SavePlayerProfile");
                SendNotice(peer, endUtc);
                ScheduleKick(peer);
            }

            return peers.Count;
        }

        private static void SendNotice(ZNetPeer peer, DateTimeOffset endUtc)
        {
            peer.m_rpc.Invoke(RpcNames.MaintenanceNotice, endUtc.ToUnixTimeSeconds());
        }

        private static void ScheduleKick(ZNetPeer peer)
        {
            if (ZNet.PeersToDisconnectAfterKick.ContainsKey(peer))
            {
                return;
            }

            peer.m_rpc.Invoke("Kicked");
            ZNet.PeersToDisconnectAfterKick[peer] = Time.time + 1.0f;
        }

        private static void SetEnd(DateTimeOffset endUtc)
        {
            PraetorisClientPlugin.MaintenanceEndUtc.Value = endUtc.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture);
            SaveConfig();
        }

        private static void ClearEnd()
        {
            if (string.IsNullOrEmpty(PraetorisClientPlugin.MaintenanceEndUtc.Value))
            {
                return;
            }

            PraetorisClientPlugin.MaintenanceEndUtc.Value = "";
            SaveConfig();
        }

        private static void SaveConfig()
        {
            PraetorisClientPlugin.Instance?.Config.Save();
        }

        private static string FormatUtc(DateTimeOffset value)
        {
            return value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
        }
    }
}
