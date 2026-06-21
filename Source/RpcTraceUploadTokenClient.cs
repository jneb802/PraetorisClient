using System;
using System.Globalization;
using UnityEngine;

namespace PraetorisClient
{
    internal static class RpcTraceUploadTokenClient
    {
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
        }

        internal static void Update()
        {
            if (PraetorisClientPlugin.MeasurementDisableHttpTraceUpload.Value ||
                !PraetorisClientPlugin.RpcTraceHttpUploadPreferred.Value ||
                !RpcTraceTelemetry.IsTracingEnabled())
            {
                UploadEnabled = false;
                EndpointUrl = "";
                Token = "";
                TokenExpiresUnixSeconds = 0L;
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

            UploadEnabled = false;
            EndpointUrl = "";
            Token = "";
            TokenExpiresUnixSeconds = 0L;
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
                    UploadEnabled = false;
                    EndpointUrl = "";
                    Token = "";
                    TokenExpiresUnixSeconds = 0L;
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
        private static void EnsureSessionId()
        {
            if (string.IsNullOrWhiteSpace(_sessionId))
                _sessionId = Guid.NewGuid().ToString("N");
        }
    }
}
