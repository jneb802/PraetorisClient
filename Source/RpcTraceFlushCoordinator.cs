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

            PreserveTraceFilesForNextLaunch("quit");
            return true;
        }

        internal static void PrepareForApplicationQuit()
        {
            if (!RpcTraceTelemetry.IsTracingEnabled())
                return;

            PreserveTraceFilesForNextLaunch("application_quit");
        }

        private static bool OnWantsToQuit()
        {
            if (_allowQuit)
                return true;

            if (!RpcTraceTelemetry.IsTracingEnabled())
                return true;

            PreserveTraceFilesForNextLaunch("quit");
            _allowQuit = true;
            return true;
        }

        private static void PreserveTraceFilesForNextLaunch(string reason)
        {
            RpcTraceTelemetry.DisableCaptureForShutdown();
            if (RpcTraceLocalStore.HasPendingFiles())
                LogHttpDeferredToNextLaunch(reason);
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

        private static void LogHttpDeferredToNextLaunch(string reason)
        {
            if (Time.realtimeSinceStartup < _nextUploadUnavailableLogTime)
                return;

            _nextUploadUnavailableLogTime = Time.realtimeSinceStartup + UploadUnavailableLogIntervalSeconds;
            PraetorisClientPlugin.Log.LogInfo(
                "RPC trace HTTP upload deferred during "
                + (string.IsNullOrWhiteSpace(reason) ? "shutdown" : reason)
                + "; keeping local trace files for upload on next launch.");
        }
    }
}
