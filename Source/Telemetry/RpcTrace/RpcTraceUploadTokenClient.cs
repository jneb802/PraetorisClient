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
        private const int TokenCacheVersion = 1;
        private const string TokenCacheFileName = "http_upload_token.cache";
        private const float RequestRetrySeconds = 30f;
        private const float ConfigurationRetrySeconds = 300f;
        private const long RefreshBeforeExpirySeconds = 60L;
        private static string _sessionId = "";
        private static float _nextRequestTime;
        private static float _requestDeadlineTime;
        private static bool _requestPending;

        internal static bool UploadEnabled { get; private set; }
        internal static string EndpointUrl { get; private set; } = "";
        internal static string Token { get; private set; } = "";
        internal static int MaxBatchBytes { get; private set; } = 128 * 1024;
        internal static float FlushIntervalSeconds { get; private set; } = 10f;
        internal static long TokenExpiresUnixSeconds { get; private set; }

        internal static string SessionId
        {
            get
            {
                EnsureSessionId();
                return _sessionId;
            }
        }

        internal static void Initialize()
        {
            EnsureSessionId();
            UploadEnabled = false;
            EndpointUrl = "";
            Token = "";
            TokenExpiresUnixSeconds = 0L;
            _requestPending = false;
            _nextRequestTime = 0f;
            LoadCachedToken();
        }

        internal static void Update()
        {
            if (PraetorisClientPlugin.MeasurementDisableHttpTraceUpload.Value ||
                !PraetorisClientPlugin.RpcTraceHttpUploadPreferred.Value ||
                !RpcTraceTelemetry.IsTracingEnabled())
            {
                ClearToken(deleteCachedToken: false);
                return;
            }

            if (!CanRequestToken())
            {
                if (_requestPending && Time.realtimeSinceStartup > _requestDeadlineTime)
                    _requestPending = false;
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
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return UploadEnabled
                && !string.IsNullOrWhiteSpace(EndpointUrl)
                && !string.IsNullOrWhiteSpace(Token)
                && TokenExpiresUnixSeconds - now > RefreshBeforeExpirySeconds;
        }

        internal static bool ShouldRetryUpload(long responseCode, string responseText)
        {
            if (RpcTraceUploadRetryPolicy.ShouldRetryUpload(responseCode, responseText))
                return true;

            ClearToken(deleteCachedToken: true);
            _requestPending = false;
            _nextRequestTime = Time.realtimeSinceStartup + ConfigurationRetrySeconds;
            PraetorisClientPlugin.Log.LogWarning(
                "HTTP RPC trace upload rejected by receiver configuration: "
                + (string.IsNullOrWhiteSpace(responseText) ? "HTTP " + responseCode : responseText)
                + ". Keeping local trace files and pausing token requests.");
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
                long expiresUnixSeconds = package.ReadLong();

                if (protocolVersion != RpcTraceTelemetry.ProtocolVersion)
                    return;

                _requestPending = false;
                _nextRequestTime = Time.realtimeSinceStartup + RequestRetrySeconds;

                if (!enabled)
                {
                    ClearToken(deleteCachedToken: true);
                    if (!string.IsNullOrWhiteSpace(message))
                        PraetorisClientPlugin.Log.LogInfo("HTTP RPC trace upload unavailable: " + message);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(sessionId))
                    _sessionId = sessionId;

                UploadEnabled = true;
                EndpointUrl = endpointUrl;
                Token = token;
                MaxBatchBytes = Math.Max(4096, maxBatchBytes);
                FlushIntervalSeconds = Math.Max(1f, flushIntervalSeconds);
                TokenExpiresUnixSeconds = expiresUnixSeconds;
                SaveCachedToken();
                PraetorisClientPlugin.Log.LogInfo(
                    "Received HTTP RPC trace upload token for session "
                    + _sessionId
                    + " expiring at "
                    + expiresUnixSeconds.ToString(CultureInfo.InvariantCulture)
                    + ".");
            }
            catch (Exception ex)
            {
                _requestPending = false;
                PraetorisClientPlugin.Log.LogWarning($"Failed to process RPC trace upload token response from peer {sender}: {ex.Message}");
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
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcNames.RpcTraceUploadTokenRequest, package);
                _requestPending = true;
                _nextRequestTime = Time.realtimeSinceStartup + RequestRetrySeconds;
                _requestDeadlineTime = Time.realtimeSinceStartup + RequestRetrySeconds;
            }
            catch (Exception ex)
            {
                _requestPending = false;
                _nextRequestTime = Time.realtimeSinceStartup + RequestRetrySeconds;
                PraetorisClientPlugin.Log.LogWarning("Failed to request RPC trace upload token: " + ex.Message);
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

        private static void ClearToken(bool deleteCachedToken)
        {
            UploadEnabled = false;
            EndpointUrl = "";
            Token = "";
            TokenExpiresUnixSeconds = 0L;

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
                    || !TryReadLong(values, "expiresUnixSeconds", out long expiresUnixSeconds)
                    || string.IsNullOrWhiteSpace(endpointUrl)
                    || string.IsNullOrWhiteSpace(token))
                {
                    DeleteCachedToken();
                    return;
                }

                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (expiresUnixSeconds - now <= RefreshBeforeExpirySeconds)
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
                TokenExpiresUnixSeconds = expiresUnixSeconds;
                PraetorisClientPlugin.Log.LogInfo(
                    "Loaded cached HTTP RPC trace upload token for session "
                    + _sessionId
                    + " expiring at "
                    + expiresUnixSeconds.ToString(CultureInfo.InvariantCulture)
                    + ".");
            }
            catch (Exception ex)
            {
                DeleteCachedToken();
                PraetorisClientPlugin.Log.LogWarning("Failed to load cached HTTP RPC trace upload token: " + ex.Message);
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
                        "expiresUnixSeconds=" + TokenExpiresUnixSeconds.ToString(CultureInfo.InvariantCulture),
                    });

                if (File.Exists(path))
                    File.Delete(path);
                File.Move(tempPath, path);
            }
            catch (Exception ex)
            {
                PraetorisClientPlugin.Log.LogWarning("Failed to cache HTTP RPC trace upload token: " + ex.Message);
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
                PraetorisClientPlugin.Log.LogWarning("Failed to delete cached HTTP RPC trace upload token: " + ex.Message);
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

        private static bool TryReadLong(Dictionary<string, string> values, string key, out long value)
        {
            value = 0L;
            return values.TryGetValue(key, out string text)
                && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
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
            return Path.Combine(Paths.BepInExRootPath, "logs", "PraetorisClient", "RpcTrace", TokenCacheFileName);
        }

        private static void EnsureSessionId()
        {
            if (string.IsNullOrWhiteSpace(_sessionId))
                _sessionId = Guid.NewGuid().ToString("N");
        }
    }
}
