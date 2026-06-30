using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Managers;
using Jotunn.Utils;

namespace PraetorisClient
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    public class PraetorisClientPlugin : BaseUnityPlugin
    {
        private const string ModName = "PraetorisClient";
        private const string ModVersion = "0.1.36";
        private const string Author = "warpalicious";
        private const string ModGUID = Author + "." + ModName;
        private const string LinkApiUrlEnv = "PRAETORISCLIENT_LINK_API_URL";
        private const string BotApiKeyEnv = "PRAETORISCLIENT_BOT_API_KEY";

        internal static string TraceModGuid => ModGUID;
        internal static string TraceModName => ModName;
        internal static string TraceModVersion => ModVersion;

        private readonly Harmony _harmony = new(ModGUID);
        private DateTime _lastReloadTime;
        private FileSystemWatcher? _configWatcher;
        private const long ReloadDelayTicks = 10000000;

        public static PraetorisClientPlugin? Instance { get; private set; }
        public static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource(ModName);

        internal static ConfigEntry<string> LinkApiUrl = null!;
        internal static ConfigEntry<string> BotApiKey = null!;
        internal static ConfigEntry<string> LinkCommand = null!;
        internal static ConfigEntry<bool> ValheimEventsTelemetryEnabled = null!;
        internal static ConfigEntry<bool> CombatTelemetryEnabled = null!;
        internal static ConfigEntry<bool> ExplorationTelemetryEnabled = null!;
        internal static ConfigEntry<float> ExplorationFlushSeconds = null!;
        internal static ConfigEntry<bool> RpcTraceEnabled = null!;
        internal static ConfigEntry<bool> RpcTraceCaptureSendReceive = null!;
        internal static ConfigEntry<string> RpcTraceNameDenyList = null!;
        internal static ConfigEntry<bool> RpcTraceHttpUploadPreferred = null!;
        internal static ConfigEntry<bool> RpcTraceDeferHttpUploadDuringGameplay = null!;
        internal static ConfigEntry<int> RpcTraceMaxBatchRows = null!;
        internal static ConfigEntry<float> RpcTraceBatchIntervalSeconds = null!;
        internal static ConfigEntry<bool> SuppressEnvironmentDamageText = null!;
        internal static ConfigEntry<bool> ZdoTraceEnabled = null!;
        internal static ConfigEntry<string> ZdoTracePrefabFilter = null!;
        internal static ConfigEntry<string> ZdoTraceZdoIdFilter = null!;
        internal static ConfigEntry<float> ZdoTraceSampleRate = null!;
        internal static ConfigEntry<int> ZdoTraceMaxEventsPerSecond = null!;
        internal static ConfigEntry<bool> FrameMetricsEnabled = null!;
        internal static ConfigEntry<float> FrameMetricsSummaryIntervalSeconds = null!;
        internal static ConfigEntry<float> FrameMetricsLongFrameThresholdMs = null!;
        internal static ConfigEntry<bool> FrameMetricsLogLongFrames = null!;
        internal static ConfigEntry<bool> SocketMetricsEnabled = null!;
        internal static ConfigEntry<float> SocketMetricsSampleIntervalSeconds = null!;
        internal static ConfigEntry<bool> RpcProbeEnabled = null!;
        internal static ConfigEntry<float> RpcProbeIntervalSeconds = null!;
        internal static ConfigEntry<int> RpcProbePayloadBytes = null!;
        internal static ConfigEntry<float> RpcProbeTimeoutSeconds = null!;
        internal static ConfigEntry<bool> MeasurementDisableRpcAndZdoTrace = null!;
        internal static ConfigEntry<bool> MeasurementDisableHttpTraceUpload = null!;

        internal static string GetLinkApiUrl()
        {
            string envValue = Environment.GetEnvironmentVariable(LinkApiUrlEnv);
            return string.IsNullOrWhiteSpace(envValue) ? LinkApiUrl.Value : envValue.Trim();
        }

        internal static string GetBotApiKey()
        {
            string envValue = Environment.GetEnvironmentVariable(BotApiKeyEnv);
            return string.IsNullOrWhiteSpace(envValue) ? BotApiKey.Value : envValue.Trim();
        }

        public void Awake()
        {
            Instance = this;
            BindConfig();
            SynchronizationManager.OnConfigurationSynchronized += OnConfigurationSynchronized;
            SiegePortalTestCommand.Register();
            FrameTimeMonitor.Initialize();
            RpcTraceTelemetry.Initialize();
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
            SocketMetricPatches.ApplyManualPatches(_harmony);
            SetupWatcher();
        }

        private void Update()
        {
            if (Game.instance == null)
                RpcTraceTelemetry.BackgroundUpdate();
        }

        private void OnDestroy()
        {
            SynchronizationManager.OnConfigurationSynchronized -= OnConfigurationSynchronized;

            try
            {
                _configWatcher?.Dispose();
                _configWatcher = null;
            }
            catch (Exception ex)
            {
                Log.LogWarning("Failed to dispose configuration watcher: " + ex.Message);
            }

            try
            {
                RpcTraceTelemetry.Shutdown();
                FrameTimeMonitor.Shutdown();
            }
            catch (Exception ex)
            {
                Log.LogWarning("Failed to shut down telemetry: " + ex.Message);
            }

            try
            {
                Config.Save();
            }
            catch (Exception ex)
            {
                Log.LogWarning("Failed to save configuration during shutdown: " + ex.Message);
            }

            try
            {
                _harmony.UnpatchSelf();
            }
            catch (Exception ex)
            {
                Log.LogWarning("Failed to unpatch PraetorisClient during shutdown: " + ex.Message);
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void BindConfig()
        {
            LinkApiUrl = Config.Bind("BotApi", "LinkApiUrl", "", "Compatible bot Valheim link endpoint. Prefer the PRAETORISCLIENT_LINK_API_URL environment variable on dedicated servers.");
            BotApiKey = Config.Bind("BotApi", "ApiKey", "", "API key sent to the bot in the X-API-Key header. Prefer the PRAETORISCLIENT_BOT_API_KEY environment variable on dedicated servers.");
            LinkCommand = Config.Bind("Linking", "LinkCommand", "!link", "In-game chat command consumed before it is sent as chat.");
            ValheimEventsTelemetryEnabled = Config.Bind("ValheimEvents", "Enabled", true, SyncedDescription("Sends client-observed telemetry to the server-side ValheimEvents mod."));
            CombatTelemetryEnabled = Config.Bind("ValheimEvents", "CombatTelemetry", true, SyncedDescription("Sends client-observed combat and death telemetry."));
            ExplorationTelemetryEnabled = Config.Bind("ValheimEvents", "ExplorationTelemetry", true, SyncedDescription("Sends client-observed minimap exploration telemetry."));
            ExplorationFlushSeconds = Config.Bind("ValheimEvents", "ExplorationFlushSeconds", 2f, SyncedDescription("How long newly explored minimap cells are batched before sending."));
            RpcTraceEnabled = Config.Bind("RpcTrace", "Enabled", true, SyncedDescription("Sends client-observed routed RPC trace rows to the server-side ValheimTracer receiver."));
            RpcTraceCaptureSendReceive = Config.Bind("RpcTrace", "CaptureSendReceive", true, SyncedDescription("Captures raw routed RPC send and receive points in addition to handled RPC points."));
            RpcTraceNameDenyList = Config.Bind("RpcTrace", "RpcNameDenyList", "", SyncedDescription("Comma-separated routed RPC names to exclude from client trace capture."));
            RpcTraceHttpUploadPreferred = Config.Bind("RpcTrace", "HttpUploadPreferred", true, SyncedDescription("Uses ValheimTracer-issued HTTP upload tokens for trace batches when the server supports it."));
            RpcTraceDeferHttpUploadDuringGameplay = Config.Bind("RpcTrace", "DeferHttpUploadDuringGameplay", true, SyncedDescription("Defers HTTP trace upload while the client is actively in-world. Trace rows are still captured locally and uploaded from menu/background when a token is available."));
            RpcTraceMaxBatchRows = Config.Bind("RpcTrace", "MaxBatchRows", 250, SyncedDescription("Maximum trace rows to write to one local gzip file before rotating it for upload."));
            RpcTraceBatchIntervalSeconds = Config.Bind("RpcTrace", "BatchIntervalSeconds", 10f, SyncedDescription("Maximum seconds to keep a local trace gzip file open before rotating it for upload."));
            SuppressEnvironmentDamageText = Config.Bind("Network", "SuppressEnvironmentDamageText", true, "Suppresses low-value environment damage text from AoE damage to pieces and non-player vegetation damage while preserving character combat damage text.");
            ZdoTraceEnabled = Config.Bind("ZdoTrace", "Enabled", true, "Enables ZDOData package and selected ZDO revision tracing.");
            ZdoTracePrefabFilter = Config.Bind("ZdoTrace", "PrefabFilter", "", "Comma-separated prefab names or prefab hashes to trace. Empty means no prefab filter.");
            ZdoTraceZdoIdFilter = Config.Bind("ZdoTrace", "ZdoIdFilter", "", "Comma-separated ZDO ids to trace in user:id format. Empty means no ZDO id filter.");
            ZdoTraceSampleRate = Config.Bind("ZdoTrace", "SampleRate", 1f, "Deterministic sample rate for ZDO revisions not matched by filters. 0 disables sampling, 1 captures all revisions.");
            ZdoTraceMaxEventsPerSecond = Config.Bind("ZdoTrace", "MaxEventsPerSecond", 0, "Maximum non-forced ZDO trace events per second. Set to 0 for no limit.");
            FrameMetricsEnabled = Config.Bind("FrameMetrics", "Enabled", true, "Writes client frame-time summaries to BepInEx/logs/PraetorisClient/FrameMetrics.");
            FrameMetricsSummaryIntervalSeconds = Config.Bind("FrameMetrics", "SummaryIntervalSeconds", 30f, "Seconds per frame metrics summary window.");
            FrameMetricsLongFrameThresholdMs = Config.Bind("FrameMetrics", "LongFrameThresholdMs", 150f, "Frame duration counted as a long frame.");
            FrameMetricsLogLongFrames = Config.Bind("FrameMetrics", "LogLongFrames", true, "Writes individual long-frame rows to CSV.");
            SocketMetricsEnabled = Config.Bind("SocketMetrics", "Enabled", true, "Writes client socket queue and connection-quality metric samples into the trace upload stream.");
            SocketMetricsSampleIntervalSeconds = Config.Bind("SocketMetrics", "SampleIntervalSeconds", 5f, "Seconds per client socket metrics sample window.");
            RpcProbeEnabled = Config.Bind("RpcProbe", "Enabled", true, "Enables active client-to-server-to-client RPC latency probes.");
            RpcProbeIntervalSeconds = Config.Bind("RpcProbe", "IntervalSeconds", 2f, "Seconds between active RPC probe requests from this client.");
            RpcProbePayloadBytes = Config.Bind("RpcProbe", "PayloadBytes", 128, "Synthetic payload bytes included in each active RPC probe.");
            RpcProbeTimeoutSeconds = Config.Bind("RpcProbe", "TimeoutSeconds", 10f, "Seconds before a pending active RPC probe is recorded as timed out.");
            MeasurementDisableRpcAndZdoTrace = Config.Bind("Measurement", "DisableRpcAndZdoTrace", false, "Local measurement override. When true, disables PraetorisClient RPC/ZDO trace capture and upload even if synced config enables it.");
            MeasurementDisableHttpTraceUpload = Config.Bind("Measurement", "DisableHttpTraceUpload", false, "Local measurement override. When true, keeps RPC/ZDO trace capture enabled but prevents HTTP trace upload token requests and uploads.");
        }

        private static ConfigDescription SyncedDescription(string description)
        {
            ConfigurationManagerAttributes adminOnly = new()
            {
                IsAdminOnly = true
            };

            return new ConfigDescription(description, null, adminOnly);
        }

        private static void OnConfigurationSynchronized(object sender, ConfigurationSynchronizationEventArgs args)
        {
            if (args.UpdatedPluginGUIDs != null && args.UpdatedPluginGUIDs.Contains(ModGUID))
            {
                string scope = args.InitialSynchronization ? "initial" : "updated";
                Log.LogInfo($"Jotunn synchronized PraetorisClient configuration ({scope}).");
            }
        }

        private void SetupWatcher()
        {
            try
            {
                _lastReloadTime = DateTime.Now;
                _configWatcher?.Dispose();
                _configWatcher = new FileSystemWatcher(BepInEx.Paths.ConfigPath, ModGUID + ".cfg");
                _configWatcher.Changed += ReadConfigValues;
                _configWatcher.Created += ReadConfigValues;
                _configWatcher.Renamed += ReadConfigValues;
                _configWatcher.IncludeSubdirectories = true;
                _configWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                Log.LogWarning("Failed to start configuration watcher: " + ex.Message);
            }
        }

        private void ReadConfigValues(object sender, FileSystemEventArgs e)
        {
            DateTime now = DateTime.Now;
            long time = now.Ticks - _lastReloadTime.Ticks;
            string configPath = Path.Combine(BepInEx.Paths.ConfigPath, ModGUID + ".cfg");
            if (!File.Exists(configPath) || time < ReloadDelayTicks)
            {
                return;
            }

            try
            {
                Log.LogInfo("Reloading configuration.");
                Config.Reload();
            }
            catch (Exception ex)
            {
                Log.LogError("Failed to reload configuration: " + ex.Message);
            }

            _lastReloadTime = now;
        }
    }
}
