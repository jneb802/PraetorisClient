using System;
using Splatform;

namespace PraetorisClient
{
    internal readonly struct RpcTracePlayerIdentity
    {
        internal RpcTracePlayerIdentity(string tracePlayerId, string steamId, string platformUserId, string playerName)
        {
            TracePlayerId = tracePlayerId;
            SteamId = steamId;
            PlatformUserId = platformUserId;
            PlayerName = playerName;
        }

        internal string TracePlayerId { get; }
        internal string SteamId { get; }
        internal string PlatformUserId { get; }
        internal string PlayerName { get; }

        internal static RpcTracePlayerIdentity Create(long localPeerId)
        {
            string platformUserId = "";
            string steamId = "";
            string playerName = "";

            try
            {
                PlatformUserID userId = PlatformManager.DistributionPlatform.LocalUser.PlatformUserID;
                platformUserId = userId.ToString();
                if (string.Equals(userId.m_platform.ToString(), "Steam", StringComparison.OrdinalIgnoreCase))
                    steamId = userId.m_userID ?? "";
            }
            catch
            {
            }

            if (string.IsNullOrWhiteSpace(steamId) && platformUserId.StartsWith("Steam_", StringComparison.OrdinalIgnoreCase))
                steamId = platformUserId.Substring("Steam_".Length);

            try
            {
                if (Player.m_localPlayer != null)
                    playerName = Player.m_localPlayer.GetPlayerName();
            }
            catch
            {
            }

            if (IsMissingPlayerName(playerName))
            {
                try
                {
                    if (Game.instance != null && Game.instance.GetPlayerProfile() != null)
                        playerName = Game.instance.GetPlayerProfile().GetName();
                }
                catch
                {
                }
            }

            string tracePlayerId = !string.IsNullOrWhiteSpace(steamId)
                ? "steam:" + steamId
                : !string.IsNullOrWhiteSpace(platformUserId)
                    ? platformUserId
                    : "peer:" + localPeerId;

            return new RpcTracePlayerIdentity(tracePlayerId, steamId, platformUserId, playerName ?? "");
        }

        private static bool IsMissingPlayerName(string playerName)
        {
            return string.IsNullOrWhiteSpace(playerName) || string.Equals(playerName, "...", StringComparison.Ordinal);
        }
    }
}
