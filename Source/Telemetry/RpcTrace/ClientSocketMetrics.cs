using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace PraetorisClient
{
    internal static class ClientSocketMetrics
    {
        private const int VanillaSendQueueBudgetBytes = 10240;
        private const int LowHeadroomBytes = 2048;

        private static readonly object MetricsLock = new();
        private static readonly Dictionary<long, PeerWindow> PeerWindows = new();
        private static float _timer;
        private static float _lastWriteTime;
        private static int _sampleIndex;
        private static int _patchWarningCount;
        private static FieldInfo? _vbNetTweaksZdoQueueLimitField;
        private static PropertyInfo? _vbNetTweaksConfigEntryValueProperty;
        private static int _lastVbNetTweaksLookupFrame = -1;

        internal static int SendQueueBudgetBytes => ResolveSendQueueBudgetBytes();

        internal static void Update()
        {
            if (!CanCapture())
                return;

            if (_lastWriteTime <= 0f)
                _lastWriteTime = UnityEngine.Time.realtimeSinceStartup;

            _timer += UnityEngine.Time.unscaledDeltaTime;
            float interval = Math.Max(1f, PraetorisClientPlugin.SocketMetricsSampleIntervalSeconds.Value);
            if (_timer < interval)
                return;

            _timer = 0f;
            WriteSample();
        }

        internal static void BeginSendZdos(ZDOMan.ZDOPeer zdoPeer, bool flush, out SendZdoMetricState state)
        {
            state = default;
            if (!CanCapture() || zdoPeer?.m_peer == null)
                return;

            ZNetPeer peer = zdoPeer.m_peer;
            int sendQueueBytes = peer.m_socket.GetSendQueueSize();
            int budgetBytes = SendQueueBudgetBytes;
            int headroomBytes = budgetBytes - sendQueueBytes;
            bool socketBudgetSkip = (!flush && sendQueueBytes > budgetBytes) || headroomBytes < LowHeadroomBytes;
            int sentBefore = ZDOMan.instance != null ? ZDOMan.instance.GetSentZDOs() : 0;
            long startTicks = Stopwatch.GetTimestamp();
            string playerName = string.IsNullOrEmpty(peer.m_playerName) ? peer.m_uid.ToString(System.Globalization.CultureInfo.InvariantCulture) : peer.m_playerName;

            lock (MetricsLock)
            {
                PeerWindow window = GetOrCreateWindow(peer.m_uid, playerName);
                window.PlayerName = playerName;
                window.AttemptCount++;
                if (flush)
                    window.FlushAttemptCount++;
                if (socketBudgetSkip)
                    window.SocketSkipCount++;
                window.LatestSendQueueBytes = sendQueueBytes;
                window.LatestHeadroomBytes = headroomBytes;
                window.MaxSendQueueBytes = Math.Max(window.MaxSendQueueBytes, sendQueueBytes);
                window.MinHeadroomBytes = Math.Min(window.MinHeadroomBytes, headroomBytes);
                window.MaxKnownZdos = Math.Max(window.MaxKnownZdos, zdoPeer.m_zdos.Count);
                window.MaxForceSend = Math.Max(window.MaxForceSend, zdoPeer.m_forceSend.Count);
                window.MaxInvalidSector = Math.Max(window.MaxInvalidSector, zdoPeer.m_invalidSector.Count);

                float now = UnityEngine.Time.realtimeSinceStartup;
                if (window.LastAttemptTime > 0f)
                    window.CadenceSamplesMs.Add((now - window.LastAttemptTime) * 1000f);
                window.LastAttemptTime = now;
            }

            state = new SendZdoMetricState(peer.m_uid, playerName, sentBefore, startTicks);
        }

        internal static void EndSendZdos(SendZdoMetricState state)
        {
            if (!CanCapture())
                return;

            int sentCount = ZDOMan.instance != null ? Math.Max(0, ZDOMan.instance.GetSentZDOs() - state.SentBefore) : 0;
            float elapsedMs = (float)((Stopwatch.GetTimestamp() - state.StartTicks) * 1000.0 / Stopwatch.Frequency);
            lock (MetricsLock)
            {
                PeerWindow window = GetOrCreateWindow(state.PeerUid, state.PlayerName);
                window.SentZdoCount += sentCount;
                window.SendDurationSamplesMs.Add(elapsedMs);
            }
        }

        internal static void RecordZdoDataPackage(ZRpc rpc, int packageBytes)
        {
            if (!CanCapture() || ZNet.instance == null)
                return;

            long peerUid = RpcTraceTelemetry.GetPeerIdForRpc(rpc);
            if (peerUid == 0L)
                return;

            string playerName = peerUid.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ZNetPeer serverPeer = ZNet.instance.GetServerPeer();
            if (serverPeer != null && serverPeer.m_uid == peerUid && !string.IsNullOrEmpty(serverPeer.m_playerName))
                playerName = serverPeer.m_playerName;

            lock (MetricsLock)
            {
                PeerWindow window = GetOrCreateWindow(peerUid, playerName);
                window.ZdoDataBytes += packageBytes;
                window.ZdoDataPackages++;
            }
        }

        internal static void LogPatchWarning(string message, Exception exception)
        {
            if (_patchWarningCount++ < 5)
                PraetorisClientPlugin.Log.LogWarning($"{message}: {exception.GetType().Name}: {exception.Message}");
        }

        private static bool CanCapture()
        {
            return RpcTraceTelemetry.IsTracingEnabled()
                && PraetorisClientPlugin.SocketMetricsEnabled.Value
                && ZNet.instance != null
                && !ZNet.instance.IsServer()
                && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected;
        }

        private static PeerWindow GetOrCreateWindow(long peerUid, string playerName)
        {
            if (!PeerWindows.TryGetValue(peerUid, out PeerWindow window))
            {
                window = new PeerWindow(peerUid, playerName);
                PeerWindows[peerUid] = window;
            }

            return window;
        }

        private static void WriteSample()
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            float windowSeconds = Math.Max(0.001f, now - _lastWriteTime);
            _lastWriteTime = now;
            _sampleIndex++;

            Dictionary<long, SocketSnapshot> socketSnapshots = ReadSocketSnapshots();
            List<PeerSample> samples;
            lock (MetricsLock)
            {
                samples = PeerWindows.Values.Select(window => window.TakeSample(windowSeconds)).ToList();
            }

            int totalAttempts = 0;
            int totalSkips = 0;
            float totalSentZdosSec = 0f;
            float totalZdoBytesSec = 0f;
            float totalZdoPackagesSec = 0f;
            int maxSendQueue = 0;
            int budgetBytes = SendQueueBudgetBytes;
            int minHeadroom = budgetBytes;
            float worstDurationP95 = 0f;
            float worstCadenceP95 = 0f;
            long localPeerId = RpcTraceTelemetry.GetLocalPeerId();
            RpcTraceTelemetry.TraceEnvelopeContext context = RpcTraceTelemetry.CaptureEnvelopeContext(localPeerId);

            foreach (PeerSample sample in samples)
            {
                socketSnapshots.TryGetValue(sample.PeerUid, out SocketSnapshot socket);
                int latestQueue = socket.HasValue ? socket.SendQueueBytes : sample.LatestSendQueueBytes;
                int latestHeadroom = socket.HasValue ? budgetBytes - socket.SendQueueBytes : sample.LatestHeadroomBytes;

                totalAttempts += sample.AttemptCount;
                totalSkips += sample.SocketSkipCount;
                totalSentZdosSec += sample.SentZdosSec;
                totalZdoBytesSec += sample.ZdoDataBytesSec;
                totalZdoPackagesSec += sample.ZdoDataPackagesSec;
                maxSendQueue = Math.Max(maxSendQueue, Math.Max(sample.MaxSendQueueBytes, latestQueue));
                minHeadroom = Math.Min(minHeadroom, Math.Min(sample.MinHeadroomBytes, latestHeadroom));
                worstDurationP95 = Math.Max(worstDurationP95, sample.SendDurationP95Ms);
                worstCadenceP95 = Math.Max(worstCadenceP95, sample.CadenceP95Ms);

                TelemetryJson json = RpcTraceTelemetry.ObjectWithEnvelope("socket_metric_peer_sample", context);
                json.Prop("role", "client");
                json.Prop("peerId", sample.PeerUid);
                json.Prop("playerName", sample.PlayerName);
                json.Prop("sampleIndex", _sampleIndex);
                json.Prop("windowSeconds", windowSeconds);
                json.Prop("zdoAttemptCount", sample.AttemptCount);
                json.Prop("zdoSocketSkipCount", sample.SocketSkipCount);
                json.Prop("flushAttemptCount", sample.FlushAttemptCount);
                json.Prop("sentZdosPerSecond", sample.SentZdosSec);
                json.Prop("zdoDataBytesPerSecond", sample.ZdoDataBytesSec);
                json.Prop("zdoDataPackagesPerSecond", sample.ZdoDataPackagesSec);
                json.Prop("sendQueueLatestBytes", latestQueue);
                json.Prop("sendQueueMaxBytes", Math.Max(sample.MaxSendQueueBytes, latestQueue));
                json.Prop("sendHeadroomLatestBytes", latestHeadroom);
                json.Prop("sendHeadroomMinBytes", Math.Min(sample.MinHeadroomBytes, latestHeadroom));
                json.Prop("knownZdosMax", sample.MaxKnownZdos);
                json.Prop("forceSendMax", sample.MaxForceSend);
                json.Prop("invalidSectorMax", sample.MaxInvalidSector);
                json.Prop("sendDurationP50Ms", sample.SendDurationP50Ms);
                json.Prop("sendDurationP95Ms", sample.SendDurationP95Ms);
                json.Prop("sendDurationMaxMs", sample.SendDurationMaxMs);
                json.Prop("cadenceP50Ms", sample.CadenceP50Ms);
                json.Prop("cadenceP95Ms", sample.CadenceP95Ms);
                json.Prop("cadenceMaxMs", sample.CadenceMaxMs);
                json.Prop("socketPingMs", socket.Ping);
                json.Prop("socketLocalQuality", socket.LocalQuality);
                json.Prop("socketRemoteQuality", socket.RemoteQuality);
                json.Prop("socketOutBytesPerSecond", socket.OutBytesSec);
                json.Prop("socketInBytesPerSecond", socket.InBytesSec);
                RpcTraceTelemetry.AddClockFields(json, context);
                json.End();
                RpcTraceLocalStore.Append(json.ToString(), localPeerId, context.WorldUid);
            }

            TelemetryJson summary = RpcTraceTelemetry.ObjectWithEnvelope("socket_metric_summary_sample", context);
            summary.Prop("role", "client");
            summary.Prop("peerId", localPeerId);
            summary.Prop("sampleIndex", _sampleIndex);
            summary.Prop("windowSeconds", windowSeconds);
            summary.Prop("peerCount", samples.Count);
            summary.Prop("totalZdoAttemptCount", totalAttempts);
            summary.Prop("totalZdoSocketSkipCount", totalSkips);
            summary.Prop("maxSendQueueBytes", maxSendQueue);
            summary.Prop("minSendHeadroomBytes", socketSnapshots.Count > 0 || samples.Count > 0 ? minHeadroom : budgetBytes);
            summary.Prop("totalSentZdosPerSecond", totalSentZdosSec);
            summary.Prop("totalZdoDataBytesPerSecond", totalZdoBytesSec);
            summary.Prop("totalZdoDataPackagesPerSecond", totalZdoPackagesSec);
            summary.Prop("worstSendDurationP95Ms", worstDurationP95);
            summary.Prop("worstCadenceP95Ms", worstCadenceP95);
            RpcTraceTelemetry.AddClockFields(summary, context);
            summary.End();
            RpcTraceLocalStore.Append(summary.ToString(), localPeerId, context.WorldUid);
        }

        private static int ResolveSendQueueBudgetBytes()
        {
            int configuredBudget = PraetorisClientPlugin.SocketMetricsSendQueueBudgetBytes != null
                ? PraetorisClientPlugin.SocketMetricsSendQueueBudgetBytes.Value
                : 0;
            if (configuredBudget > 0)
                return configuredBudget;

            int vbNetTweaksBudget = ReadVbNetTweaksZdoQueueLimit();
            return vbNetTweaksBudget > 0 ? vbNetTweaksBudget : VanillaSendQueueBudgetBytes;
        }

        private static int ReadVbNetTweaksZdoQueueLimit()
        {
            try
            {
                EnsureVbNetTweaksLookup();
                object? configEntry = _vbNetTweaksZdoQueueLimitField?.GetValue(null);
                if (configEntry == null)
                    return 0;

                _vbNetTweaksConfigEntryValueProperty ??= configEntry.GetType().GetProperty("Value");
                object? value = _vbNetTweaksConfigEntryValueProperty?.GetValue(configEntry);
                return value is int budgetBytes ? budgetBytes : 0;
            }
            catch (Exception exception)
            {
                LogPatchWarning("Failed to read VBNetTweaks ZDOQueueLimit", exception);
                return 0;
            }
        }

        private static void EnsureVbNetTweaksLookup()
        {
            if (_vbNetTweaksZdoQueueLimitField != null)
                return;

            int frame = UnityEngine.Time.frameCount;
            if (_lastVbNetTweaksLookupFrame == frame)
                return;

            _lastVbNetTweaksLookupFrame = frame;
            Type? pluginType = Type.GetType("VBNetTweaks.VBNetTweaks, VBNetTweaks");
            _vbNetTweaksZdoQueueLimitField = pluginType?.GetField("ZDOQueueLimit", BindingFlags.Public | BindingFlags.Static);
        }

        private static Dictionary<long, SocketSnapshot> ReadSocketSnapshots()
        {
            Dictionary<long, SocketSnapshot> snapshots = new();
            if (ZNet.instance == null)
                return snapshots;

            ZNetPeer serverPeer = ZNet.instance.GetServerPeer();
            if (serverPeer == null || !serverPeer.IsReady())
                return snapshots;

            try
            {
                serverPeer.m_socket.GetConnectionQuality(out float localQuality, out float remoteQuality, out int ping, out float outBytesSec, out float inBytesSec);
                snapshots[serverPeer.m_uid] = new SocketSnapshot(true, serverPeer.m_socket.GetSendQueueSize(), ping, localQuality, remoteQuality, outBytesSec, inBytesSec);
            }
            catch (Exception exception)
            {
                PraetorisClientPlugin.Log.LogWarning($"Failed to read client socket snapshot: {exception.Message}");
            }

            return snapshots;
        }

        private sealed class PeerWindow
        {
            internal PeerWindow(long peerUid, string playerName)
            {
                PeerUid = peerUid;
                PlayerName = playerName;
                MinHeadroomBytes = SendQueueBudgetBytes;
            }

            internal long PeerUid { get; }
            internal string PlayerName { get; set; }
            internal int AttemptCount { get; set; }
            internal int SocketSkipCount { get; set; }
            internal int FlushAttemptCount { get; set; }
            internal int SentZdoCount { get; set; }
            internal long ZdoDataBytes { get; set; }
            internal int ZdoDataPackages { get; set; }
            internal int LatestSendQueueBytes { get; set; }
            internal int LatestHeadroomBytes { get; set; } = SendQueueBudgetBytes;
            internal int MaxSendQueueBytes { get; set; }
            internal int MinHeadroomBytes { get; set; }
            internal int MaxKnownZdos { get; set; }
            internal int MaxForceSend { get; set; }
            internal int MaxInvalidSector { get; set; }
            internal float LastAttemptTime { get; set; }
            internal List<float> SendDurationSamplesMs { get; } = new();
            internal List<float> CadenceSamplesMs { get; } = new();

            internal PeerSample TakeSample(float windowSeconds)
            {
                List<float> durations = SendDurationSamplesMs.ToList();
                List<float> cadences = CadenceSamplesMs.ToList();
                PeerSample sample = new(PeerUid, PlayerName, AttemptCount, SocketSkipCount, FlushAttemptCount, SentZdoCount / windowSeconds, ZdoDataBytes / windowSeconds, ZdoDataPackages / windowSeconds, LatestSendQueueBytes, LatestHeadroomBytes, MaxSendQueueBytes, MinHeadroomBytes, MaxKnownZdos, MaxForceSend, MaxInvalidSector, MetricsMath.Median(durations), MetricsMath.Percentile(durations, 0.95f), durations.Count == 0 ? 0f : durations.Max(), MetricsMath.Median(cadences), MetricsMath.Percentile(cadences, 0.95f), cadences.Count == 0 ? 0f : cadences.Max());

                AttemptCount = 0;
                SocketSkipCount = 0;
                FlushAttemptCount = 0;
                SentZdoCount = 0;
                ZdoDataBytes = 0;
                ZdoDataPackages = 0;
                MaxSendQueueBytes = LatestSendQueueBytes;
                MinHeadroomBytes = LatestHeadroomBytes;
                MaxKnownZdos = 0;
                MaxForceSend = 0;
                MaxInvalidSector = 0;
                SendDurationSamplesMs.Clear();
                CadenceSamplesMs.Clear();
                return sample;
            }
        }

        private readonly struct PeerSample
        {
            internal PeerSample(long peerUid, string playerName, int attemptCount, int socketSkipCount, int flushAttemptCount, float sentZdosSec, float zdoDataBytesSec, float zdoDataPackagesSec, int latestSendQueueBytes, int latestHeadroomBytes, int maxSendQueueBytes, int minHeadroomBytes, int maxKnownZdos, int maxForceSend, int maxInvalidSector, float sendDurationP50Ms, float sendDurationP95Ms, float sendDurationMaxMs, float cadenceP50Ms, float cadenceP95Ms, float cadenceMaxMs)
            {
                PeerUid = peerUid;
                PlayerName = playerName;
                AttemptCount = attemptCount;
                SocketSkipCount = socketSkipCount;
                FlushAttemptCount = flushAttemptCount;
                SentZdosSec = sentZdosSec;
                ZdoDataBytesSec = zdoDataBytesSec;
                ZdoDataPackagesSec = zdoDataPackagesSec;
                LatestSendQueueBytes = latestSendQueueBytes;
                LatestHeadroomBytes = latestHeadroomBytes;
                MaxSendQueueBytes = maxSendQueueBytes;
                MinHeadroomBytes = minHeadroomBytes;
                MaxKnownZdos = maxKnownZdos;
                MaxForceSend = maxForceSend;
                MaxInvalidSector = maxInvalidSector;
                SendDurationP50Ms = sendDurationP50Ms;
                SendDurationP95Ms = sendDurationP95Ms;
                SendDurationMaxMs = sendDurationMaxMs;
                CadenceP50Ms = cadenceP50Ms;
                CadenceP95Ms = cadenceP95Ms;
                CadenceMaxMs = cadenceMaxMs;
            }

            internal long PeerUid { get; }
            internal string PlayerName { get; }
            internal int AttemptCount { get; }
            internal int SocketSkipCount { get; }
            internal int FlushAttemptCount { get; }
            internal float SentZdosSec { get; }
            internal float ZdoDataBytesSec { get; }
            internal float ZdoDataPackagesSec { get; }
            internal int LatestSendQueueBytes { get; }
            internal int LatestHeadroomBytes { get; }
            internal int MaxSendQueueBytes { get; }
            internal int MinHeadroomBytes { get; }
            internal int MaxKnownZdos { get; }
            internal int MaxForceSend { get; }
            internal int MaxInvalidSector { get; }
            internal float SendDurationP50Ms { get; }
            internal float SendDurationP95Ms { get; }
            internal float SendDurationMaxMs { get; }
            internal float CadenceP50Ms { get; }
            internal float CadenceP95Ms { get; }
            internal float CadenceMaxMs { get; }
        }

        private readonly struct SocketSnapshot
        {
            internal SocketSnapshot(bool hasValue, int sendQueueBytes, int ping, float localQuality, float remoteQuality, float outBytesSec, float inBytesSec)
            {
                HasValue = hasValue;
                SendQueueBytes = sendQueueBytes;
                Ping = ping;
                LocalQuality = localQuality;
                RemoteQuality = remoteQuality;
                OutBytesSec = outBytesSec;
                InBytesSec = inBytesSec;
            }

            internal bool HasValue { get; }
            internal int SendQueueBytes { get; }
            internal int Ping { get; }
            internal float LocalQuality { get; }
            internal float RemoteQuality { get; }
            internal float OutBytesSec { get; }
            internal float InBytesSec { get; }
        }
    }
}
