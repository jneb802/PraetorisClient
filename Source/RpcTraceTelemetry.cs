using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PraetorisClient
{
    internal static class RpcTraceTelemetry
    {
        internal const int ProtocolVersion = 2;
        internal const string Schema = "valheim.trace.rpc.v1";
        private const float ClockSyncIntervalSeconds = 10f;
        private const int ClockSampleWindow = 9;
        private const double MaxClockRoundTripMs = 2000.0;
        private const double MaxClockOffsetJumpMs = 250.0;

        private static readonly object Sync = new();
        private static readonly Dictionary<int, string> RpcNamesByHash = new();
        private static readonly List<ClockSample> ClockSamples = new();
        private static HashSet<string> _denyList = new(StringComparer.OrdinalIgnoreCase);
        private static string _lastDenyListConfig = "";
        private static float _nextClockSyncTime;
        private static long _sequence;
        private static int _clockSequence;
        private static double _lastServerMinusClientOffsetMs;
        private static double _selectedClockRoundTripMs;
        private static double _lastGoodClockSyncRealtime;
        private static int _validClockSampleCount;
        private static bool _hasClockOffset;
        private static bool _shutdownCapture;
        private static bool _suppressCaptureUntilDisconnected;
        private static bool _runtimeStartSubmitted;
        private static string _runtimeId = "";

        private readonly struct ClockSample
        {
            internal ClockSample(double offsetMs, double roundTripMs, double realtime)
            {
                OffsetMs = offsetMs;
                RoundTripMs = roundTripMs;
                Realtime = realtime;
            }

            internal double OffsetMs { get; }
            internal double RoundTripMs { get; }
            internal double Realtime { get; }
        }

        internal static void Initialize()
        {
            _shutdownCapture = false;
            _suppressCaptureUntilDisconnected = false;
            _runtimeStartSubmitted = false;
            _runtimeId = TraceRuntimeMetadata.BuildRuntimeId("client");
            RpcTraceLocalStore.Initialize();
            RpcTraceUploadTokenClient.Initialize();
            RpcTraceHttpUploadCoordinator.Initialize();
            RpcTraceFlushCoordinator.Initialize();
        }

        internal static string RuntimeId => _runtimeId;

        internal static void Shutdown()
        {
            _shutdownCapture = true;
            RpcTraceHttpUploadCoordinator.Shutdown();
            RpcTraceFlushCoordinator.Shutdown();
            RpcTraceLocalStore.CloseCurrentFile();
        }

        internal static void DisableCaptureForShutdown()
        {
            _shutdownCapture = true;
            RpcTraceLocalStore.CloseCurrentFile();
        }

        internal static void SuppressCaptureUntilDisconnected()
        {
            _suppressCaptureUntilDisconnected = true;
            RpcTraceLocalStore.CloseCurrentFile();
        }

        internal static void RegisterRpcName(string methodName, Delegate? callback = null)
        {
            if (string.IsNullOrEmpty(methodName))
                return;

            bool added = false;
            lock (Sync)
            {
                int methodHash = methodName.GetStableHashCode();
                if (!RpcNamesByHash.TryGetValue(methodHash, out string existing) || existing != methodName)
                {
                    RpcNamesByHash[methodHash] = methodName;
                    added = true;
                }
            }

            if (added)
                WriteObservedRpcMethod(methodName, callback);
        }

        internal static void TraceRoutedRpc(string eventType, ZRoutedRpc.RoutedRPCData? data, long receiverPeerId)
        {
            if (!CanCapture() || data == null || IsTraceRpc(data))
                return;

            if (!PraetorisClientPlugin.RpcTraceCaptureSendReceive.Value && eventType != "rpc_handle")
                return;

            try
            {
                RefreshDenyList();
                string rpcName = GetRpcName(data.m_methodHash);
                if (IsDenied(rpcName))
                    return;

                long localPeerId = GetLocalPeerId();
                TelemetryJson json = ObjectWithEnvelope(eventType, localPeerId);
                json.Prop("role", "client");
                json.Prop("rpcTraceId", BuildRpcTraceId(data.m_senderPeerID, data.m_msgID));
                json.Prop("msgId", data.m_msgID);
                json.Prop("senderPeerId", data.m_senderPeerID);
                json.Prop("receiverPeerId", receiverPeerId);
                json.Prop("targetPeerId", data.m_targetPeerID);
                json.Prop("methodHash", data.m_methodHash);
                json.Prop("rpcName", rpcName);
                json.Prop("targetZdo", ClientZdoSnapshot.FormatId(data.m_targetZDO));
                json.Prop("payloadBytes", data.m_parameters != null ? data.m_parameters.Size() : 0);
                json.Prop("serverMinusClientOffsetMs", _lastServerMinusClientOffsetMs);
                json.Prop("clockRoundTripMs", _selectedClockRoundTripMs);
                json.Prop("clockOffsetQuality", GetClockOffsetQuality());
                json.Prop("clockOffsetAgeMs", GetClockOffsetAgeMs());
                json.Prop("clockSampleCount", _validClockSampleCount);
                json.End();
                RpcTraceLocalStore.Append(json.ToString(), localPeerId);
            }
            catch (Exception ex)
            {
                PraetorisClientPlugin.Log.LogWarning($"Failed to capture RPC trace row: {ex.GetType().Name}: {ex.Message}");
            }
        }

        internal static ZRoutedRpc.RoutedRPCData? TryReadRoutedRpcData(ZPackage? package)
        {
            if (package == null)
                return null;

            try
            {
                ZPackage copy = new(package.GetArray());
                ZRoutedRpc.RoutedRPCData data = new();
                data.Deserialize(copy);
                return data;
            }
            catch (Exception ex)
            {
                PraetorisClientPlugin.Log.LogWarning($"Failed to read routed RPC trace package: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
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

        internal static void Update()
        {
            if (!IsTracingEnabled())
            {
                RpcTraceLocalStore.CloseCurrentFile();
                return;
            }

            if (_suppressCaptureUntilDisconnected && ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connected)
                _suppressCaptureUntilDisconnected = false;

            MaybeWriteRuntimeStart();
            MaybeSendClockSyncRequest();
            RpcTraceLocalStore.FlushIfDue(Time.realtimeSinceStartupAsDouble);
            RpcTraceUploadTokenClient.Update();
            RpcTraceHttpUploadCoordinator.Update();
            RpcTraceFlushCoordinator.Update();
        }

        internal static void OnClockResponse(long sender, ZPackage package)
        {
            if (!IsTracingEnabled())
                return;

            long clientReceiveUtcTicks = DateTime.UtcNow.Ticks;
            double clientReceiveRealtime = Time.realtimeSinceStartupAsDouble;

            try
            {
                int protocolVersion = package.ReadInt();
                int sequence = package.ReadInt();
                long clientPeerId = package.ReadLong();
                long serverPeerId = package.ReadLong();
                long clientSendUtcTicks = package.ReadLong();
                double clientSendRealtime = package.ReadDouble();
                long serverReceiveUtcTicks = package.ReadLong();
                double serverReceiveRealtime = package.ReadDouble();
                long serverSendUtcTicks = package.ReadLong();
                double serverSendRealtime = package.ReadDouble();

                if (protocolVersion != ProtocolVersion)
                    return;

                double clientElapsedMs = (clientReceiveRealtime - clientSendRealtime) * 1000.0;
                double serverProcessingMs = (serverSendRealtime - serverReceiveRealtime) * 1000.0;
                double roundTripMs = clientElapsedMs - serverProcessingMs;
                double offsetMs = (TicksToMilliseconds(serverReceiveUtcTicks - clientSendUtcTicks)
                    + TicksToMilliseconds(serverSendUtcTicks - clientReceiveUtcTicks)) / 2.0;
                bool accepted = UpdateClockOffset(offsetMs, roundTripMs, clientReceiveRealtime);

                long localPeerId = GetLocalPeerId();
                TelemetryJson json = ObjectWithEnvelope("clock_sync_sample", localPeerId);
                json.Prop("role", "client");
                json.Prop("senderPeerId", sender);
                json.Prop("clientPeerId", clientPeerId);
                json.Prop("serverPeerId", serverPeerId);
                json.Prop("clockSequence", sequence);
                json.Prop("clientSendUtcTicks", clientSendUtcTicks);
                json.Prop("serverReceiveUtcTicks", serverReceiveUtcTicks);
                json.Prop("serverSendUtcTicks", serverSendUtcTicks);
                json.Prop("clientReceiveUtcTicks", clientReceiveUtcTicks);
                json.Prop("clientSendRealtime", clientSendRealtime);
                json.Prop("serverReceiveRealtime", serverReceiveRealtime);
                json.Prop("serverSendRealtime", serverSendRealtime);
                json.Prop("clientReceiveRealtime", clientReceiveRealtime);
                json.Prop("roundTripMs", roundTripMs);
                json.Prop("serverProcessingMs", serverProcessingMs);
                json.Prop("sampleServerMinusClientOffsetMs", offsetMs);
                json.Prop("serverMinusClientOffsetMs", _lastServerMinusClientOffsetMs);
                json.Prop("clockSampleAccepted", accepted);
                json.Prop("clockOffsetQuality", GetClockOffsetQuality());
                json.Prop("clockOffsetAgeMs", GetClockOffsetAgeMs());
                json.Prop("clockSampleCount", _validClockSampleCount);
                json.End();
                RpcTraceLocalStore.Append(json.ToString(), localPeerId);
            }
            catch (Exception ex)
            {
                PraetorisClientPlugin.Log.LogWarning($"Failed to process RPC trace clock response: {ex.GetType().Name}: {ex.Message}");
            }
        }

        internal static bool ShouldAllowLogout(Game game, bool save, bool changeToStartScene)
        {
            return RpcTraceFlushCoordinator.ShouldAllowLogout(game, save, changeToStartScene);
        }

        internal static bool ShouldAllowMenuQuit()
        {
            return RpcTraceFlushCoordinator.ShouldAllowMenuQuit();
        }

        internal static void OnApplicationQuitFallback()
        {
            RpcTraceFlushCoordinator.RequestFlush("application_quit");
        }

        internal static bool IsTracingEnabled()
        {
            return PraetorisClientPlugin.RpcTraceEnabled.Value;
        }

        internal static TelemetryJson ObjectWithEnvelope(string eventType, long localPeerId)
        {
            DateTime now = DateTime.UtcNow;
            long sequence = ++_sequence;
            RpcTracePlayerIdentity identity = RpcTracePlayerIdentity.Create(localPeerId);
            TelemetryJson json = TelemetryJson.Object();
            json.Prop("schema", Schema);
            json.Prop("eventType", eventType);
            json.Prop("traceId", "client:" + localPeerId.ToString(CultureInfo.InvariantCulture) + ":" + sequence.ToString(CultureInfo.InvariantCulture));
            json.Prop("tracePlayerId", identity.TracePlayerId);
            json.Prop("steamId", identity.SteamId);
            json.Prop("platformUserId", identity.PlatformUserId);
            json.Prop("playerName", identity.PlayerName);
            json.Prop("sequence", sequence);
            json.Prop("localPeerId", localPeerId);
            json.Prop("timeUtc", now.ToString("o", CultureInfo.InvariantCulture));
            json.Prop("timeUtcTicks", now.Ticks);
            json.Prop("realtime", Time.realtimeSinceStartupAsDouble);
            json.Prop("worldName", ZNet.m_world != null ? ZNet.m_world.m_name : "");
            json.Prop("worldUid", ZNet.m_world != null ? ZNet.m_world.m_uid : 0L);
            return json;
        }

        private static void WriteObservedRpcMethod(string rpcName, Delegate? callback)
        {
            if (!IsTracingEnabled() || _shutdownCapture)
                return;

            try
            {
                long localPeerId = GetLocalPeerId();
                TraceMethodSource source = TraceRuntimeMetadata.GetMethodSource(callback);
                TelemetryJson json = ObjectWithEnvelope("rpc_method_observed", localPeerId);
                json.Prop("role", "client");
                json.Prop("runtimeId", _runtimeId);
                json.Prop("methodHash", rpcName.GetStableHashCode());
                json.Prop("rpcName", rpcName);
                json.Prop("gameVersion", TraceRuntimeMetadata.GetGameVersion());
                json.Prop("pluginGuid", source.PluginGuid);
                json.Prop("pluginName", source.PluginName);
                json.Prop("pluginVersion", source.PluginVersion);
                json.Prop("assemblyName", source.AssemblyName);
                json.Prop("isVanilla", source.IsVanilla);
                json.Prop("isModded", source.IsModded);
                json.Prop("sourceKind", "registered");
                json.End();
                RpcTraceLocalStore.Append(json.ToString(), localPeerId);
            }
            catch (Exception ex)
            {
                PraetorisClientPlugin.Log.LogWarning($"Failed to capture RPC method registration: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void MaybeWriteRuntimeStart()
        {
            if (_runtimeStartSubmitted || !CanCapture())
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

        private static bool CanCapture()
        {
            return IsTracingEnabled() &&
                   !_shutdownCapture &&
                   !_suppressCaptureUntilDisconnected &&
                   ZNet.instance != null &&
                   ZRoutedRpc.instance != null &&
                   !ZNet.instance.IsServer() &&
                   ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected;
        }

        internal static bool CanCaptureZdoTrace()
        {
            return IsTracingEnabled() &&
                   !_shutdownCapture &&
                   !_suppressCaptureUntilDisconnected &&
                   ZNet.instance != null &&
                   !ZNet.instance.IsServer();
        }

        internal static void AddClockFields(TelemetryJson json)
        {
            json.Prop("serverMinusClientOffsetMs", _lastServerMinusClientOffsetMs);
            json.Prop("clockRoundTripMs", _selectedClockRoundTripMs);
            json.Prop("clockOffsetQuality", GetClockOffsetQuality());
            json.Prop("clockOffsetAgeMs", GetClockOffsetAgeMs());
            json.Prop("clockSampleCount", _validClockSampleCount);
        }

        private static void MaybeSendClockSyncRequest()
        {
            if (!CanCapture() || Time.realtimeSinceStartup < _nextClockSyncTime || ZRoutedRpc.instance == null)
                return;

            _nextClockSyncTime = Time.realtimeSinceStartup + ClockSyncIntervalSeconds;
            int sequence = ++_clockSequence;
            ZPackage package = new();
            package.Write(ProtocolVersion);
            package.Write(sequence);
            package.Write(GetLocalPeerId());
            package.Write(DateTime.UtcNow.Ticks);
            package.Write(Time.realtimeSinceStartupAsDouble);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcNames.RpcTraceClockRequest, package);
        }

        private static bool IsTraceRpc(ZRoutedRpc.RoutedRPCData data)
        {
            return data.m_methodHash == RpcNames.RpcTraceClockRequest.GetStableHashCode()
                || data.m_methodHash == RpcNames.RpcTraceClockResponse.GetStableHashCode()
                || data.m_methodHash == RpcNames.RpcTraceUploadTokenRequest.GetStableHashCode()
                || data.m_methodHash == RpcNames.RpcTraceUploadTokenResponse.GetStableHashCode();
        }

        private static string GetRpcName(int methodHash)
        {
            lock (Sync)
            {
                return RpcNamesByHash.TryGetValue(methodHash, out string rpcName) ? rpcName : "";
            }
        }

        private static bool IsDenied(string rpcName)
        {
            return !string.IsNullOrEmpty(rpcName) && _denyList.Contains(rpcName);
        }

        private static void RefreshDenyList()
        {
            string config = PraetorisClientPlugin.RpcTraceNameDenyList.Value ?? "";
            if (string.Equals(config, _lastDenyListConfig, StringComparison.Ordinal))
                return;

            HashSet<string> next = new(StringComparer.OrdinalIgnoreCase);
            foreach (string item in config.Split(','))
            {
                string trimmed = item.Trim();
                if (trimmed.Length > 0)
                    next.Add(trimmed);
            }

            _denyList = next;
            _lastDenyListConfig = config;
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

        private static double TicksToMilliseconds(long ticks)
        {
            return ticks / (double)TimeSpan.TicksPerMillisecond;
        }

        private static string BuildRpcTraceId(long senderPeerId, long msgId)
        {
            long worldUid = ZNet.m_world != null ? ZNet.m_world.m_uid : 0L;
            return worldUid.ToString(CultureInfo.InvariantCulture)
                + ":"
                + senderPeerId.ToString(CultureInfo.InvariantCulture)
                + ":"
                + msgId.ToString(CultureInfo.InvariantCulture);
        }

        private static bool UpdateClockOffset(double offsetMs, double roundTripMs, double realtime)
        {
            if (roundTripMs < 0.0 || roundTripMs > MaxClockRoundTripMs || double.IsNaN(offsetMs) || double.IsInfinity(offsetMs))
                return false;

            if (ClockSamples.Count >= 3)
            {
                double medianOffset = GetMedianOffset();
                if (Math.Abs(offsetMs - medianOffset) > MaxClockOffsetJumpMs)
                    return false;
            }

            ClockSamples.Add(new ClockSample(offsetMs, roundTripMs, realtime));
            while (ClockSamples.Count > ClockSampleWindow)
                ClockSamples.RemoveAt(0);

            List<ClockSample> ordered = new(ClockSamples);
            ordered.Sort((left, right) => left.RoundTripMs.CompareTo(right.RoundTripMs));

            int take = Math.Min(5, ordered.Count);
            double offsetTotal = 0.0;
            for (int i = 0; i < take; i++)
                offsetTotal += ordered[i].OffsetMs;

            _lastServerMinusClientOffsetMs = offsetTotal / take;
            _selectedClockRoundTripMs = ordered[0].RoundTripMs;
            _lastGoodClockSyncRealtime = realtime;
            _validClockSampleCount++;
            _hasClockOffset = true;
            return true;
        }

        private static double GetMedianOffset()
        {
            List<double> offsets = new(ClockSamples.Count);
            foreach (ClockSample sample in ClockSamples)
                offsets.Add(sample.OffsetMs);

            offsets.Sort();
            int middle = offsets.Count / 2;
            if (offsets.Count % 2 == 1)
                return offsets[middle];

            return (offsets[middle - 1] + offsets[middle]) / 2.0;
        }

        private static string GetClockOffsetQuality()
        {
            if (!_hasClockOffset)
                return "none";

            if (GetClockOffsetAgeMs() > 30000.0)
                return "stale";

            return ClockSamples.Count >= 3 ? "good" : "warming";
        }

        private static double GetClockOffsetAgeMs()
        {
            if (!_hasClockOffset)
                return 0.0;

            return Math.Max(0.0, (Time.realtimeSinceStartupAsDouble - _lastGoodClockSyncRealtime) * 1000.0);
        }
    }
}
