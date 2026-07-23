using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;

namespace PraetorisClient
{
    internal static class RpcTraceUploadTokenClient
    {
        private const int TokenCacheVersion = 2;
        private const string TokenCacheFileName = "http_network_metric_upload_token.cache";
        private const float RequestRetrySeconds = 30f;
        private const float ConfigurationRetrySeconds = 300f;
        private static string _sessionId = "";
        private static float _nextRequestTime;
        private static float _requestDeadlineTime;
        private static bool _requestPending;
        private static float _nextRequestGateLogTime;

        internal static bool UploadEnabled { get; private set; }
        internal static string EndpointUrl { get; private set; } = "";
        internal static string Token { get; private set; } = "";
        internal static int MaxBatchBytes { get; private set; } = 128 * 1024;
        internal static float FlushIntervalSeconds { get; private set; } = 10f;

        internal static void Initialize()
        {
            EnsureSessionId();
            UploadEnabled = false;
            EndpointUrl = "";
            Token = "";
            _requestPending = false;
            _nextRequestTime = 0f;
            _nextRequestGateLogTime = 0f;
            LoadCachedToken();
        }

        internal static void Update()
        {
            if (PraetorisClientPlugin.MeasurementDisableNetworkMetricHttpUpload.Value
                || !PraetorisClientPlugin.NetworkMetricHttpUploadPreferred.Value
                || !RpcTraceTelemetry.IsTracingEnabled())
            {
                ClearToken(deleteCachedToken: false);
                return;
            }

            if (!CanRequestToken())
            {
                if (_requestPending && Time.realtimeSinceStartup > _requestDeadlineTime)
                    _requestPending = false;
                LogRequestGateIfDue();
                return;
            }

            if (HasUsableToken())
                return;
            if (Time.realtimeSinceStartup < _nextRequestTime)
                return;

            RequestToken();
        }

        internal static bool HasUsableToken()
        {
            return UploadEnabled
                && !string.IsNullOrWhiteSpace(EndpointUrl)
                && !string.IsNullOrWhiteSpace(Token);
        }

        internal static bool ShouldRetryUpload(long responseCode, string responseText)
        {
            if (RpcTraceUploadRetryPolicy.ShouldRetryUpload(responseCode, responseText))
                return true;

            ClearToken(deleteCachedToken: true);
            _requestPending = false;
            _nextRequestTime = Time.realtimeSinceStartup + ConfigurationRetrySeconds;
            PraetorisClientPlugin.Log.LogWarning(
                "HTTP network metric upload rejected by receiver configuration: "
                + (string.IsNullOrWhiteSpace(responseText) ? "HTTP " + responseCode : responseText)
                + ". Keeping local metric files and pausing token requests.");
            return false;
        }

        internal static void OnTokenResponse(long sender, ZPackage package)
        {
            try
            {
                int protocolVersion = package.ReadInt();
                bool enabled = package.ReadBool();
                string message = package.ReadString();
                string endpointUrl = package.ReadString();
                string token = package.ReadString();
                string sessionId = package.ReadString();
                int maxBatchBytes = package.ReadInt();
                float flushIntervalSeconds = package.ReadSingle();

                if (protocolVersion != RpcTraceTelemetry.ProtocolVersion)
                    return;

                _requestPending = false;
                _nextRequestTime = Time.realtimeSinceStartup + RequestRetrySeconds;
                _nextRequestGateLogTime = 0f;

                if (!enabled)
                {
                    ClearToken(deleteCachedToken: true);
                    if (!string.IsNullOrWhiteSpace(message))
                        PraetorisClientPlugin.Log.LogInfo("HTTP network metric upload unavailable: " + message);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(sessionId))
                    _sessionId = sessionId;

                UploadEnabled = true;
                EndpointUrl = endpointUrl;
                Token = token;
                MaxBatchBytes = Math.Max(4096, maxBatchBytes);
                FlushIntervalSeconds = Math.Max(1f, flushIntervalSeconds);
                SaveCachedToken();
                PraetorisClientPlugin.Log.LogInfo("Received HTTP network metric upload token for session " + _sessionId + ".");
            }
            catch (Exception ex)
            {
                _requestPending = false;
                PraetorisClientPlugin.Log.LogWarning($"Failed to process network metric upload token response from peer {sender}: {ex.Message}");
            }
        }

        private static void RequestToken()
        {
            if (ZRoutedRpc.instance == null)
                return;

            try
            {
                EnsureSessionId();
                RpcTracePlayerIdentity identity = RpcTracePlayerIdentity.Create(RpcTraceTelemetry.GetLocalPeerId());
                ZPackage package = new();
                package.Write(RpcTraceTelemetry.ProtocolVersion);
                package.Write(_sessionId);
                package.Write(identity.TracePlayerId);
                package.Write(identity.SteamId);
                package.Write(identity.PlatformUserId);
                package.Write(identity.PlayerName);
                PraetorisClientPlugin.Log.LogInfo("Requesting HTTP network metric upload token for session " + _sessionId + ".");
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcNames.RpcTraceUploadTokenRequest, package);
                _requestPending = true;
                _nextRequestTime = Time.realtimeSinceStartup + RequestRetrySeconds;
                _requestDeadlineTime = Time.realtimeSinceStartup + RequestRetrySeconds;
                _nextRequestGateLogTime = 0f;
            }
            catch (Exception ex)
            {
                _requestPending = false;
                _nextRequestTime = Time.realtimeSinceStartup + RequestRetrySeconds;
                PraetorisClientPlugin.Log.LogWarning("Failed to request network metric upload token: " + ex.Message);
            }
        }

        private static bool CanRequestToken()
        {
            return !_requestPending
                && ZNet.instance != null
                && ZRoutedRpc.instance != null
                && !ZNet.instance.IsServer()
                && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected;
        }

        private static void LogRequestGateIfDue()
        {
            if (Time.realtimeSinceStartup < _nextRequestGateLogTime)
                return;

            _nextRequestGateLogTime = Time.realtimeSinceStartup + RequestRetrySeconds;
            string status = ZNet.instance == null ? "no-znet" : ZNet.GetConnectionStatus().ToString();
            string routedRpc = ZRoutedRpc.instance == null ? "no-routed-rpc" : "routed-rpc-ready";
            string server = ZNet.instance == null ? "unknown-server-state" : (ZNet.instance.IsServer() ? "server" : "client");
            PraetorisClientPlugin.Log.LogDebug(
                "Network metric upload token request deferred: pending="
                + _requestPending
                + ", status="
                + status
                + ", "
                + routedRpc
                + ", "
                + server
                + ".");
        }

        private static void ClearToken(bool deleteCachedToken)
        {
            UploadEnabled = false;
            EndpointUrl = "";
            Token = "";

            if (deleteCachedToken)
                DeleteCachedToken();
        }

        private static void LoadCachedToken()
        {
            string path = GetTokenCachePath();
            if (!File.Exists(path))
                return;

            try
            {
                Dictionary<string, string> values = ReadCacheFile(path);
                if (!TryReadInt(values, "version", out int version) || version != TokenCacheVersion)
                {
                    DeleteCachedToken();
                    return;
                }

                string sessionId = Decode(values, "sessionId");
                string endpointUrl = Decode(values, "endpointUrl");
                string token = Decode(values, "token");
                if (!TryReadInt(values, "maxBatchBytes", out int maxBatchBytes)
                    || !TryReadFloat(values, "flushIntervalSeconds", out float flushIntervalSeconds)
                    || string.IsNullOrWhiteSpace(endpointUrl)
                    || string.IsNullOrWhiteSpace(token))
                {
                    DeleteCachedToken();
                    return;
                }

                if (!string.IsNullOrWhiteSpace(sessionId))
                    _sessionId = sessionId;

                UploadEnabled = true;
                EndpointUrl = endpointUrl;
                Token = token;
                MaxBatchBytes = Math.Max(4096, maxBatchBytes);
                FlushIntervalSeconds = Math.Max(1f, flushIntervalSeconds);
                PraetorisClientPlugin.Log.LogInfo("Loaded cached HTTP network metric upload token for session " + _sessionId + ".");
            }
            catch (Exception ex)
            {
                DeleteCachedToken();
                PraetorisClientPlugin.Log.LogWarning("Failed to load cached HTTP network metric upload token: " + ex.Message);
            }
        }

        private static void SaveCachedToken()
        {
            try
            {
                string path = GetTokenCachePath();
                string directory = Path.GetDirectoryName(path) ?? "";
                Directory.CreateDirectory(directory);
                string tempPath = path + ".tmp";
                File.WriteAllLines(
                    tempPath,
                    new[]
                    {
                        "version=" + TokenCacheVersion.ToString(CultureInfo.InvariantCulture),
                        "sessionId=" + Encode(_sessionId),
                        "endpointUrl=" + Encode(EndpointUrl),
                        "token=" + Encode(Token),
                        "maxBatchBytes=" + MaxBatchBytes.ToString(CultureInfo.InvariantCulture),
                        "flushIntervalSeconds=" + FlushIntervalSeconds.ToString("R", CultureInfo.InvariantCulture),
                    });

                if (File.Exists(path))
                    File.Delete(path);
                File.Move(tempPath, path);
            }
            catch (Exception ex)
            {
                PraetorisClientPlugin.Log.LogWarning("Failed to cache HTTP network metric upload token: " + ex.Message);
            }
        }

        private static void DeleteCachedToken()
        {
            try
            {
                string path = GetTokenCachePath();
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                PraetorisClientPlugin.Log.LogWarning("Failed to delete cached HTTP network metric upload token: " + ex.Message);
            }
        }

        private static Dictionary<string, string> ReadCacheFile(string path)
        {
            Dictionary<string, string> values = new(StringComparer.Ordinal);
            foreach (string line in File.ReadAllLines(path))
            {
                int separator = line.IndexOf('=');
                if (separator <= 0)
                    continue;

                string key = line.Substring(0, separator);
                string value = line.Substring(separator + 1);
                values[key] = value;
            }

            return values;
        }

        private static bool TryReadInt(Dictionary<string, string> values, string key, out int value)
        {
            value = 0;
            return values.TryGetValue(key, out string text)
                && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryReadFloat(Dictionary<string, string> values, string key, out float value)
        {
            value = 0f;
            return values.TryGetValue(key, out string text)
                && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static string Decode(Dictionary<string, string> values, string key)
        {
            if (!values.TryGetValue(key, out string encoded) || string.IsNullOrWhiteSpace(encoded))
                return "";

            return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }

        private static string Encode(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));
        }

        private static string GetTokenCachePath()
        {
            return Path.Combine(Paths.BepInExRootPath, "logs", "PraetorisClient", "NetworkMetrics", TokenCacheFileName);
        }

        private static void EnsureSessionId()
        {
            if (string.IsNullOrWhiteSpace(_sessionId))
                _sessionId = Guid.NewGuid().ToString("N");
        }
    }
}
