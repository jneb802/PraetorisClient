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
        private const int MaximumDailyWindowDurationMinutes = 1440;
        private static DateTimeOffset _nextScheduledCheckUtc;
        private static DateTimeOffset? _activeScheduledWindowEndUtc;
        private static string _lastScheduleError = "";

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

            DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
            PraetorisClientPlugin.MaintenanceEndUtc.Value = "";
            if (TryGetScheduledWindowEnd(nowUtc, false, out DateTimeOffset scheduledEndUtc))
            {
                PraetorisClientPlugin.MaintenanceDailyWindowSuppressedUntilUtc.Value = FormatTimestamp(scheduledEndUtc);
            }
            else
            {
                PraetorisClientPlugin.MaintenanceDailyWindowSuppressedUntilUtc.Value = "";
            }

            _activeScheduledWindowEndUtc = null;
            SaveConfig();
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
            DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
            bool manualActive = TryGetManualEnd(nowUtc, out DateTimeOffset manualEndUtc);
            bool scheduledActive = TryGetScheduledWindowEnd(nowUtc, true, out DateTimeOffset scheduledEndUtc);
            if (!manualActive && !scheduledActive)
            {
                return false;
            }

            if (manualActive && scheduledActive)
            {
                endUtc = manualEndUtc >= scheduledEndUtc ? manualEndUtc : scheduledEndUtc;
            }
            else
            {
                endUtc = manualActive ? manualEndUtc : scheduledEndUtc;
            }

            return true;
        }

        internal static void UpdateScheduledWindow()
        {
            DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
            if (nowUtc < _nextScheduledCheckUtc)
            {
                return;
            }

            _nextScheduledCheckUtc = nowUtc.AddSeconds(1.0);
            if (ZNet.instance == null || !ZNet.instance.IsServer())
            {
                _activeScheduledWindowEndUtc = null;
                return;
            }

            if (!TryGetScheduledWindowEnd(nowUtc, true, out DateTimeOffset scheduledEndUtc))
            {
                _activeScheduledWindowEndUtc = null;
                return;
            }

            if (_activeScheduledWindowEndUtc == scheduledEndUtc)
            {
                return;
            }

            _activeScheduledWindowEndUtc = scheduledEndUtc;
            int disconnectedPlayers = DisconnectConnectedPlayers(scheduledEndUtc);
            PraetorisClientPlugin.Log.LogInfo($"Scheduled maintenance active until {FormatUtc(scheduledEndUtc)}. Disconnecting {disconnectedPlayers} connected player(s).");
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
            PraetorisClientPlugin.MaintenanceEndUtc.Value = FormatTimestamp(endUtc);
            SaveConfig();
        }

        private static bool TryGetManualEnd(DateTimeOffset nowUtc, out DateTimeOffset endUtc)
        {
            endUtc = default;
            string configuredEnd = PraetorisClientPlugin.MaintenanceEndUtc.Value.Trim();
            if (string.IsNullOrEmpty(configuredEnd))
            {
                return false;
            }

            if (!TryParseTimestamp(configuredEnd, out endUtc))
            {
                PraetorisClientPlugin.Log.LogError("Maintenance EndUtc is invalid. Manual maintenance will remain inactive until the value is corrected.");
                return false;
            }

            if (endUtc > nowUtc)
            {
                return true;
            }

            ClearEnd();
            PraetorisClientPlugin.Log.LogInfo("Manual maintenance expired.");
            return false;
        }

        private static bool TryGetScheduledWindowEnd(DateTimeOffset nowUtc, bool honorSuppression, out DateTimeOffset endUtc)
        {
            endUtc = default;
            if (!PraetorisClientPlugin.MaintenanceDailyWindowEnabled.Value)
            {
                _lastScheduleError = "";
                return false;
            }

            if (!TimeSpan.TryParseExact(
                    PraetorisClientPlugin.MaintenanceDailyWindowStartLocalTime.Value.Trim(),
                    "hh\\:mm",
                    CultureInfo.InvariantCulture,
                    out TimeSpan localStartTime))
            {
                LogScheduleErrorOnce("Maintenance DailyWindowStartLocalTime must use 24-hour HH:mm format.");
                return false;
            }

            int durationMinutes = PraetorisClientPlugin.MaintenanceDailyWindowDurationMinutes.Value;
            if (durationMinutes < 1 || durationMinutes > MaximumDailyWindowDurationMinutes)
            {
                LogScheduleErrorOnce($"Maintenance DailyWindowDurationMinutes must be from 1 to {MaximumDailyWindowDurationMinutes}.");
                return false;
            }

            TimeZoneInfo timeZone;
            try
            {
                timeZone = TimeZoneInfo.FindSystemTimeZoneById(PraetorisClientPlugin.MaintenanceDailyWindowTimeZone.Value.Trim());
            }
            catch (TimeZoneNotFoundException)
            {
                LogScheduleErrorOnce("Maintenance DailyWindowTimeZone was not found on this server.");
                return false;
            }
            catch (InvalidTimeZoneException)
            {
                LogScheduleErrorOnce("Maintenance DailyWindowTimeZone is invalid on this server.");
                return false;
            }

            DateTime localNow = TimeZoneInfo.ConvertTime(nowUtc, timeZone).DateTime;
            DateTime localStart = localNow.Date.Add(localStartTime);
            if (!TryConvertLocalTimeToUtc(localStart, timeZone, out DateTimeOffset startUtc))
            {
                LogScheduleErrorOnce("The configured daily maintenance start time does not exist on this daylight-saving transition date.");
                return false;
            }

            DateTimeOffset candidateEndUtc = startUtc.AddMinutes(durationMinutes);
            if (nowUtc < startUtc)
            {
                localStart = localStart.AddDays(-1.0);
                if (!TryConvertLocalTimeToUtc(localStart, timeZone, out startUtc))
                {
                    LogScheduleErrorOnce("The configured daily maintenance start time does not exist on this daylight-saving transition date.");
                    return false;
                }

                candidateEndUtc = startUtc.AddMinutes(durationMinutes);
            }

            if (nowUtc < startUtc || nowUtc >= candidateEndUtc)
            {
                _lastScheduleError = "";
                ClearExpiredSuppression(nowUtc);
                return false;
            }

            _lastScheduleError = "";
            endUtc = candidateEndUtc;
            return !honorSuppression || !IsScheduledWindowSuppressed(nowUtc, candidateEndUtc);
        }

        private static bool TryConvertLocalTimeToUtc(DateTime localTime, TimeZoneInfo timeZone, out DateTimeOffset utcTime)
        {
            DateTime unspecifiedLocalTime = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
            if (timeZone.IsInvalidTime(unspecifiedLocalTime))
            {
                utcTime = default;
                return false;
            }

            if (timeZone.IsAmbiguousTime(unspecifiedLocalTime))
            {
                TimeSpan daylightOffset = timeZone.GetAmbiguousTimeOffsets(unspecifiedLocalTime).Max();
                utcTime = new DateTimeOffset(unspecifiedLocalTime, daylightOffset).ToUniversalTime();
                return true;
            }

            utcTime = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(unspecifiedLocalTime, timeZone), TimeSpan.Zero);
            return true;
        }

        private static bool IsScheduledWindowSuppressed(DateTimeOffset nowUtc, DateTimeOffset scheduledEndUtc)
        {
            string configuredEnd = PraetorisClientPlugin.MaintenanceDailyWindowSuppressedUntilUtc.Value.Trim();
            if (string.IsNullOrEmpty(configuredEnd))
            {
                return false;
            }

            if (!TryParseTimestamp(configuredEnd, out DateTimeOffset suppressedUntilUtc))
            {
                PraetorisClientPlugin.Log.LogError("Maintenance DailyWindowSuppressedUntilUtc is invalid. The current scheduled window will not be suppressed.");
                return false;
            }

            if (suppressedUntilUtc <= nowUtc)
            {
                ClearScheduledSuppression();
                return false;
            }

            return suppressedUntilUtc >= scheduledEndUtc;
        }

        private static void ClearExpiredSuppression(DateTimeOffset nowUtc)
        {
            string configuredEnd = PraetorisClientPlugin.MaintenanceDailyWindowSuppressedUntilUtc.Value.Trim();
            if (!string.IsNullOrEmpty(configuredEnd) &&
                TryParseTimestamp(configuredEnd, out DateTimeOffset suppressedUntilUtc) &&
                suppressedUntilUtc <= nowUtc)
            {
                ClearScheduledSuppression();
            }
        }

        private static void ClearScheduledSuppression()
        {
            if (string.IsNullOrEmpty(PraetorisClientPlugin.MaintenanceDailyWindowSuppressedUntilUtc.Value))
            {
                return;
            }

            PraetorisClientPlugin.MaintenanceDailyWindowSuppressedUntilUtc.Value = "";
            SaveConfig();
        }

        private static bool TryParseTimestamp(string value, out DateTimeOffset timestampUtc)
        {
            bool parsed = DateTimeOffset.TryParseExact(
                value,
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out timestampUtc);
            timestampUtc = timestampUtc.ToUniversalTime();
            return parsed;
        }

        private static string FormatTimestamp(DateTimeOffset value)
        {
            return value.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture);
        }

        private static void LogScheduleErrorOnce(string message)
        {
            if (_lastScheduleError == message)
            {
                return;
            }

            _lastScheduleError = message;
            PraetorisClientPlugin.Log.LogError(message + " Scheduled maintenance is inactive.");
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
