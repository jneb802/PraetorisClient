using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace PraetorisClient.GuardStoneFeature
{
    internal static class GuardStoneCountHover
    {
        private const string RequestRpc = "PraetorisGuardStoneCountRequest";
        private const string ResultRpc = "PraetorisGuardStoneCountResult";
        private const float RefreshSeconds = 2f;
        private const float StaleSeconds = 6f;
        private static readonly Dictionary<long, float> LastRequests = new Dictionary<long, float>();
        private static long _playerId;
        private static float _nextRequest;
        private static float _receivedAt = float.NegativeInfinity;
        private static int _count;
        private static int _limit;

        internal static void Register(ZRoutedRpc rpc)
        {
            LastRequests.Clear();
            _playerId = 0L;
            _nextRequest = 0f;
            _receivedAt = float.NegativeInfinity;
            rpc.Register(RequestRpc, OnRequest);
            rpc.Register<long, int, int>(ResultRpc, OnResult);
        }

        internal static string GetCountText()
        {
            long playerId = Player.m_localPlayer.GetPlayerID();
            if (_playerId != playerId)
            {
                _playerId = playerId;
                _nextRequest = 0f;
                _receivedAt = float.NegativeInfinity;
            }

            float now = Time.realtimeSinceStartup;
            if (now >= _nextRequest && ZRoutedRpc.instance != null)
            {
                _nextRequest = now + RefreshSeconds;
                if (ZNet.instance.IsServer())
                {
                    SetCount(GuardStoneBuildLimit.CountWorldGuardStones(playerId),
                        PraetorisClientPlugin.GuardStonePlayerBuildLimit.Value);
                }
                else
                {
                    ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RequestRpc);
                }
            }

            return now - _receivedAt <= StaleSeconds
                ? $"\nYour wards: {_count} / {_limit}"
                : "\nYour wards: unavailable";
        }

        private static void OnRequest(long sender)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null ||
                !PlayerResolver.TryGetSenderPlayerId(sender, out long playerId, out _))
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (LastRequests.TryGetValue(sender, out float previous) && now - previous < RefreshSeconds)
            {
                return;
            }

            LastRequests[sender] = now;
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, ResultRpc, playerId,
                GuardStoneBuildLimit.CountWorldGuardStones(playerId),
                PraetorisClientPlugin.GuardStonePlayerBuildLimit.Value);
        }

        private static void OnResult(long sender, long playerId, int count, int limit)
        {
            if (ZRoutedRpc.instance == null || sender != ZRoutedRpc.instance.GetServerPeerID() ||
                Player.m_localPlayer == null || playerId != _playerId ||
                playerId != Player.m_localPlayer.GetPlayerID() || count < 0 || limit < 0)
            {
                return;
            }

            SetCount(count, limit);
        }

        private static void SetCount(int count, int limit)
        {
            _count = count;
            _limit = limit;
            _receivedAt = Time.realtimeSinceStartup;
        }
    }

    [HarmonyPatch(typeof(PrivateArea), nameof(PrivateArea.GetHoverText))]
    internal static class GuardStoneCountHoverPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(PrivateArea __instance, ref string __result)
        {
            if (Player.m_localPlayer != null && ZNet.instance != null && ZDOMan.instance != null &&
                !string.IsNullOrEmpty(__result) &&
                GuardStoneBuildLimit.IsGuardStone(__instance.GetComponent<Piece>()))
            {
                __result += GuardStoneCountHover.GetCountText();
            }
        }
    }
}
