using UnityEngine;

namespace PraetorisClient
{
    internal static class RpcTraceFlushCoordinator
    {
        private const float UploadUnavailableLogIntervalSeconds = 30f;
        private static bool _allowQuit;
        private static float _nextUploadUnavailableLogTime;

        internal static void Initialize()
        {
            Application.wantsToQuit -= OnWantsToQuit;
            Application.wantsToQuit += OnWantsToQuit;
            _allowQuit = false;
            _nextUploadUnavailableLogTime = 0f;
        }

        internal static void Shutdown()
        {
            Application.wantsToQuit -= OnWantsToQuit;
        }

        internal static void RequestFlush(string reason)
        {
            if (!RpcTraceTelemetry.IsTracingEnabled())
                return;

            if (RpcTraceHttpUploadCoordinator.CanAcceptFlushRequest())
            {
                RpcTraceHttpUploadCoordinator.RequestFlush(reason);
                return;
            }

            LogHttpUnavailable(reason);
        }

        internal static void Update()
        {
            if (!RpcTraceTelemetry.IsTracingEnabled())
                return;

            if (!RpcTraceHttpUploadCoordinator.IsActive()
                && !RpcTraceHttpUploadCoordinator.CanAcceptFlushRequest()
                && RpcTraceLocalStore.HasPendingFiles())
                LogHttpUnavailable("background");
        }

        internal static bool ShouldAllowLogout(Game game, bool save, bool changeToStartScene)
        {
            if (!RpcTraceTelemetry.IsTracingEnabled())
                return true;

            if (RpcTraceHttpUploadCoordinator.CanAcceptFlushRequest())
            {
                RpcTraceHttpUploadCoordinator.RequestFlush("logout");
            }
            else if (RpcTraceLocalStore.HasPendingFiles())
            {
                LogHttpUnavailable("logout");
            }

            RpcTraceTelemetry.SuppressCaptureUntilDisconnected();
            return true;
        }

        internal static bool ShouldAllowMenuQuit()
        {
            if (!RpcTraceTelemetry.IsTracingEnabled())
                return true;

            if (RpcTraceHttpUploadCoordinator.CanAcceptFlushRequest())
            {
                RpcTraceHttpUploadCoordinator.RequestFlush("quit");
            }
            else if (RpcTraceLocalStore.HasPendingFiles())
            {
                LogHttpUnavailable("quit");
            }

            RpcTraceTelemetry.DisableCaptureForShutdown();
            return true;
        }

        private static bool OnWantsToQuit()
        {
            if (_allowQuit)
                return true;

            if (!RpcTraceTelemetry.IsTracingEnabled())
                return true;

            if (RpcTraceHttpUploadCoordinator.CanAcceptFlushRequest())
            {
                RpcTraceHttpUploadCoordinator.RequestFlush("quit");
            }
            else if (RpcTraceLocalStore.HasPendingFiles())
            {
                LogHttpUnavailable("quit");
            }

            RpcTraceTelemetry.DisableCaptureForShutdown();
            _allowQuit = true;
            return true;
        }

        private static void LogHttpUnavailable(string reason)
        {
            if (Time.realtimeSinceStartup < _nextUploadUnavailableLogTime)
                return;

            _nextUploadUnavailableLogTime = Time.realtimeSinceStartup + UploadUnavailableLogIntervalSeconds;
            PraetorisClientPlugin.Log.LogWarning(
                "RPC trace HTTP upload is unavailable during "
                + (string.IsNullOrWhiteSpace(reason) ? "flush" : reason)
                + "; keeping local trace files for later HTTP retry.");
        }
    }
}
