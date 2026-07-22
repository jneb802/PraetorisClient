using System;
using System.Globalization;
using System.Threading;
using UnityEngine;

namespace PraetorisClient
{
    internal static class RpcTraceTelemetry
    {
        internal const int ProtocolVersion = 2;
        internal const string Schema = "valheim.trace.rpc.v1";
        private const double IdentityRefreshSeconds = 1.0;

        private static long _sequence;
        private static bool _shutdownCapture;
        private static bool _runtimeStartSubmitted;
        private static string _runtimeId = "";
        private static RpcTracePlayerIdentity _cachedIdentity = new("", "", "", "");
        private static long _cachedIdentityPeerId;
        private static double _nextIdentityRefreshRealtime;

        internal static void Initialize()
        {
            _shutdownCapture = false;
            _runtimeStartSubmitted = false;
            _runtimeId = TraceRuntimeMetadata.BuildRuntimeId("client");
            _cachedIdentity = new RpcTracePlayerIdentity("", "", "", "");
            _cachedIdentityPeerId = 0L;
            _nextIdentityRefreshRealtime = 0d;
            RpcTraceLocalStore.Initialize();
        }

        internal static void Shutdown()
        {
            _shutdownCapture = true;
            RpcTraceLocalStore.Shutdown();
        }

        internal static void Update()
        {
            if (!IsTracingEnabled())
            {
                RpcTraceLocalStore.CloseCurrentFileIfOpen();
                return;
            }

            MaybeWriteRuntimeStart();
            ClientSocketMetrics.Update();
            RpcProbeClient.Update();
            RpcTraceLocalStore.FlushIfDue(Time.realtimeSinceStartupAsDouble);
        }

        internal static bool IsTracingEnabled()
        {
            return !PraetorisClientPlugin.MeasurementDisableNetworkMetrics.Value
                && (PraetorisClientPlugin.SocketMetricsEnabled.Value || PraetorisClientPlugin.RpcProbeEnabled.Value);
        }

        internal static TelemetryJson ObjectWithEnvelope(string eventType, long localPeerId)
        {
            return ObjectWithEnvelope(eventType, CaptureEnvelopeContext(localPeerId));
        }

        internal static TelemetryJson ObjectWithEnvelope(string eventType, TraceEnvelopeContext context)
        {
            long sequence = Interlocked.Increment(ref _sequence);
            TelemetryJson json = TelemetryJson.Object();
            json.Prop("schema", Schema);
            json.Prop("eventType", eventType);
            json.Prop("traceId", "client:" + context.LocalPeerId.ToString(CultureInfo.InvariantCulture) + ":" + sequence.ToString(CultureInfo.InvariantCulture));
            json.Prop("tracePlayerId", context.TracePlayerId);
            json.Prop("steamId", context.SteamId);
            json.Prop("platformUserId", context.PlatformUserId);
            json.Prop("playerName", context.PlayerName);
            json.Prop("sequence", sequence);
            json.Prop("localPeerId", context.LocalPeerId);
            json.Prop("timeUtc", context.TimeUtc);
            json.Prop("timeUtcTicks", context.TimeUtcTicks);
            json.Prop("realtime", context.Realtime);
            json.Prop("worldName", context.WorldName);
            json.Prop("worldUid", context.WorldUid);
            return json;
        }

        internal static TraceEnvelopeContext CaptureEnvelopeContext(long localPeerId)
        {
            DateTime now = DateTime.UtcNow;
            RpcTracePlayerIdentity identity = GetCachedIdentity(localPeerId);
            return new TraceEnvelopeContext(
                localPeerId,
                identity.TracePlayerId,
                identity.SteamId,
                identity.PlatformUserId,
                identity.PlayerName,
                now.ToString("o", CultureInfo.InvariantCulture),
                now.Ticks,
                Time.realtimeSinceStartupAsDouble,
                ZNet.m_world != null ? ZNet.m_world.m_name : "",
                ZNet.m_world != null ? ZNet.m_world.m_uid : 0L);
        }

        internal static void AddClockFields(TelemetryJson json)
        {
            json.Prop("serverMinusClientOffsetMs", 0.0);
            json.Prop("clockRoundTripMs", 0.0);
            json.Prop("clockOffsetQuality", "none");
            json.Prop("clockOffsetAgeMs", 0.0);
            json.Prop("clockSampleCount", 0);
        }

        internal static void AddClockFields(TelemetryJson json, TraceEnvelopeContext context)
        {
            AddClockFields(json);
        }

        internal static long GetPeerIdForRpc(ZRpc rpc)
        {
            if (ZNet.instance == null)
                return 0L;

            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
            {
                if (peer != null && peer.m_rpc == rpc)
                    return peer.m_uid;
            }

            if (!ZNet.instance.IsServer())
            {
                ZNetPeer serverPeer = ZNet.instance.GetServerPeer();
                if (serverPeer != null && serverPeer.m_rpc == rpc)
                    return serverPeer.m_uid;
            }

            return 0L;
        }

        internal static long GetLocalPeerId()
        {
            try
            {
                return ZDOMan.instance != null ? ZDOMan.GetSessionID() : 0L;
            }
            catch
            {
                return 0L;
            }
        }

        private static void MaybeWriteRuntimeStart()
        {
            if (_runtimeStartSubmitted || !CanCaptureMetricRows())
                return;

            _runtimeStartSubmitted = true;
            long localPeerId = GetLocalPeerId();
            TelemetryJson json = ObjectWithEnvelope("runtime_start", localPeerId);
            json.Prop("role", "client");
            json.Prop("runtimeId", _runtimeId);
            json.Prop("gameVersion", TraceRuntimeMetadata.GetGameVersion());
            json.Prop("traceSource", "PraetorisClient");
            json.Prop("traceModGuid", PraetorisClientPlugin.TraceModGuid);
            json.Prop("traceModName", PraetorisClientPlugin.TraceModName);
            json.Prop("traceModVersion", PraetorisClientPlugin.TraceModVersion);
            TraceRuntimeMetadata.WritePlugins(json);
            json.End();
            RpcTraceLocalStore.Append(json.ToString(), localPeerId);
        }

        private static bool CanCaptureMetricRows()
        {
            return IsTracingEnabled()
                && !_shutdownCapture
                && ZNet.instance != null
                && ZRoutedRpc.instance != null
                && !ZNet.instance.IsServer()
                && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected;
        }

        private static RpcTracePlayerIdentity GetCachedIdentity(long localPeerId)
        {
            double realtime = Time.realtimeSinceStartupAsDouble;
            if (_cachedIdentityPeerId == localPeerId
                && realtime < _nextIdentityRefreshRealtime
                && !string.IsNullOrWhiteSpace(_cachedIdentity.TracePlayerId))
                return _cachedIdentity;

            _cachedIdentity = RpcTracePlayerIdentity.Create(localPeerId);
            _cachedIdentityPeerId = localPeerId;
            _nextIdentityRefreshRealtime = realtime + IdentityRefreshSeconds;
            return _cachedIdentity;
        }

        internal readonly struct TraceEnvelopeContext
        {
            internal TraceEnvelopeContext(
                long localPeerId,
                string tracePlayerId,
                string steamId,
                string platformUserId,
                string playerName,
                string timeUtc,
                long timeUtcTicks,
                double realtime,
                string worldName,
                long worldUid)
            {
                LocalPeerId = localPeerId;
                TracePlayerId = tracePlayerId ?? "";
                SteamId = steamId ?? "";
                PlatformUserId = platformUserId ?? "";
                PlayerName = playerName ?? "";
                TimeUtc = timeUtc ?? "";
                TimeUtcTicks = timeUtcTicks;
                Realtime = realtime;
                WorldName = worldName ?? "";
                WorldUid = worldUid;
            }

            internal long LocalPeerId { get; }
            internal string TracePlayerId { get; }
            internal string SteamId { get; }
            internal string PlatformUserId { get; }
            internal string PlayerName { get; }
            internal string TimeUtc { get; }
            internal long TimeUtcTicks { get; }
            internal double Realtime { get; }
            internal string WorldName { get; }
            internal long WorldUid { get; }
        }
    }
}
