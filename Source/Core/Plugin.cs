using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using EpicLootAPI;
using EpicLootLeslieAlphaTest.src;
using EpicLootLeslieAlphaTest.src.StatusEffects;
using EpicLootLeslieAlphaTest.src.StatusEffects.VFX;
using EpicLootLeslieAlphaTest.src.Utilities;
using HarmonyLib;
using Jotunn.Managers;
using Jotunn.Utils;
using PraetorisClient.CreatureOwnership;
using PraetorisClient.ServerChestFeature;
using System;
using System.IO;
using System.Reflection;

namespace PraetorisClient
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [BepInDependency("randyknapp.mods.epicloot", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("org.tristan.rcon", BepInDependency.DependencyFlags.SoftDependency)]

    public class PraetorisClientPlugin : BaseUnityPlugin
    {
        private const string ModName = "PraetorisClient";
        private const string ModVersion = "0.1.52";
        private const string Author = "warpalicious";
        private const string ModGUID = Author + "." + ModName;
        private const string LinkApiUrlEnv = "PRAETORISCLIENT_LINK_API_URL";
        private const string BotApiKeyEnv = "PRAETORISCLIENT_BOT_API_KEY";

        internal static string TraceModGuid => ModGUID;
        internal static string TraceModName => ModName;
        internal static string TraceModVersion => ModVersion;
        
        //Hello World
        
        private readonly Harmony _harmony = new(ModGUID);
        private DateTime _lastReloadTime;
        private FileSystemWatcher? _configWatcher;
        private const long ReloadDelayTicks = 10000000;

        private static bool Loaded = false;

        public static PraetorisClientPlugin? Instance { get; private set; }
        public static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource(ModName);

        internal static ConfigEntry<string> LinkApiUrl = null!;
        internal static ConfigEntry<string> BotApiKey = null!;
        internal static ConfigEntry<string> LinkCommand = null!;
        internal static ConfigEntry<int> MetricMaxBatchRows = null!;
        internal static ConfigEntry<float> MetricBatchIntervalSeconds = null!;
        internal static ConfigEntry<bool> NetworkMetricHttpUploadPreferred = null!;
        internal static ConfigEntry<bool> SuppressEnvironmentDamageText = null!;
        internal static ConfigEntry<bool> FrameMetricsEnabled = null!;
        internal static ConfigEntry<float> FrameMetricsSummaryIntervalSeconds = null!;
        internal static ConfigEntry<float> FrameMetricsLongFrameThresholdMs = null!;
        internal static ConfigEntry<bool> FrameMetricsLogLongFrames = null!;
        internal static ConfigEntry<bool> SocketMetricsEnabled = null!;
        internal static ConfigEntry<float> SocketMetricsSampleIntervalSeconds = null!;
        internal static ConfigEntry<int> SocketMetricsSendQueueBudgetBytes = null!;
        internal static ConfigEntry<bool> RpcProbeEnabled = null!;
        internal static ConfigEntry<float> RpcProbeIntervalSeconds = null!;
        internal static ConfigEntry<int> RpcProbePayloadBytes = null!;
        internal static ConfigEntry<float> RpcProbeTimeoutSeconds = null!;
        internal static ConfigEntry<bool> MeasurementDisableNetworkMetrics = null!;
        internal static ConfigEntry<bool> MeasurementDisableNetworkMetricHttpUpload = null!;
        internal static ConfigEntry<bool> DisableBoatWaterImpactDamage = null!;
        internal static ConfigEntry<float> CreatureOwnerWardRadius = null!;
        internal static ConfigEntry<float> CreatureOwnerWardUpdateIntervalSeconds = null!;
        internal static ConfigEntry<bool> DebugCreatureOwnerWard = null!;
        internal static ConfigEntry<bool> DebugServerChest = null!;

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
            // Leslie EpicLoot additions
            PrefabManager.OnPrefabsRegistered += () => { if (Loaded) return; HumanoidFactory.Create(); Loaded = true; };
            PrefabManager.OnPrefabsRegistered += () => InfusionVFX.Init();
            MagicEffects.Init();
            SERegistry.RegisterStatusEffects();
            EpicLootAPI.EpicLoot.RegisterAll();
            //

            Instance = this;
            BindConfig();
            SynchronizationManager.OnConfigurationSynchronized += OnConfigurationSynchronized;
            CreatureOwnerWardPiece.Initialize();
            CreatureOwnerWardCommand.Register();
            ServerChestPiece.Initialize();
            ServerChestCommand.Register();
            if (Chainloader.PluginInfos.ContainsKey(ServerChestRconCommand.ValheimRconGuid))
            {
                ServerChestRconCommand.Register();
            }

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
            CreatureOwnerWardPiece.Shutdown();
            ServerChestPiece.Shutdown();

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
            MetricMaxBatchRows = Config.Bind("NetworkMetrics", "MaxBatchRows", 250, SyncedDescription("Maximum probe/socket metric rows to write to one local gzip file before rotating it."));
            MetricBatchIntervalSeconds = Config.Bind("NetworkMetrics", "BatchIntervalSeconds", 10f, SyncedDescription("Maximum seconds to keep a local probe/socket metric gzip file open before rotating it."));
            NetworkMetricHttpUploadPreferred = Config.Bind("NetworkMetrics", "HttpUploadPreferred", true, SyncedDescription("Uses ValheimTracer-issued HTTP upload tokens to deliver probe/socket metric batches when the server supports it."));
            SuppressEnvironmentDamageText = Config.Bind("Network", "SuppressEnvironmentDamageText", true, "Suppresses low-value environment damage text from AoE damage to pieces and non-player vegetation damage while preserving character combat damage text.");
            FrameMetricsEnabled = Config.Bind("FrameMetrics", "Enabled", true, "Writes client frame-time summaries to BepInEx/logs/PraetorisClient/FrameMetrics.");
            FrameMetricsSummaryIntervalSeconds = Config.Bind("FrameMetrics", "SummaryIntervalSeconds", 30f, "Seconds per frame metrics summary window.");
            FrameMetricsLongFrameThresholdMs = Config.Bind("FrameMetrics", "LongFrameThresholdMs", 150f, "Frame duration counted as a long frame.");
            FrameMetricsLogLongFrames = Config.Bind("FrameMetrics", "LogLongFrames", true, "Writes individual long-frame rows to CSV.");
            SocketMetricsEnabled = Config.Bind("SocketMetrics", "Enabled", true, SyncedDescription("Writes client socket queue and connection-quality metric samples to local metric files."));
            SocketMetricsSampleIntervalSeconds = Config.Bind("SocketMetrics", "SampleIntervalSeconds", 5f, "Seconds per client socket metrics sample window.");
            SocketMetricsSendQueueBudgetBytes = Config.Bind("SocketMetrics", "SendQueueBudgetBytes", 0, SyncedDescription("Socket send queue budget in bytes for skip/headroom metrics. Set to 0 to auto-detect VBNetTweaks ZDOQueueLimit, falling back to Valheim's vanilla 10240 bytes."));
            RpcProbeEnabled = Config.Bind("RpcProbe", "Enabled", true, SyncedDescription("Enables active client-to-server-to-client RPC latency probes."));
            RpcProbeIntervalSeconds = Config.Bind("RpcProbe", "IntervalSeconds", 2f, "Seconds between active RPC probe requests from this client.");
            RpcProbePayloadBytes = Config.Bind("RpcProbe", "PayloadBytes", 128, "Synthetic payload bytes included in each active RPC probe.");
            RpcProbeTimeoutSeconds = Config.Bind("RpcProbe", "TimeoutSeconds", 10f, "Seconds before a pending active RPC probe is recorded as timed out.");
            MeasurementDisableNetworkMetrics = Config.Bind("Measurement", "DisableNetworkMetrics", false, "Local measurement override. When true, disables PraetorisClient RPC probe and socket metric capture even if synced config enables it.");
            MeasurementDisableNetworkMetricHttpUpload = Config.Bind("Measurement", "DisableNetworkMetricHttpUpload", false, "Local measurement override. When true, keeps network metrics on disk and does not upload them over HTTP.");
            DisableBoatWaterImpactDamage = Config.Bind("Ships", "DisableBoatWaterImpactDamage", true, SyncedDescription("Prevents boats from losing health when Valheim's water-force impact handling applies boat impact damage. Other boat damage sources still apply normally."));
            CreatureOwnerWardRadius = Config.Bind("CreatureOwnerWard", "Radius", 40f, SyncedDescription("Meters around an active Creature Owner Ward where monster ZDO ownership is assigned to the configured connected player."));
            CreatureOwnerWardUpdateIntervalSeconds = Config.Bind("CreatureOwnerWard", "UpdateIntervalSeconds", 2f, SyncedDescription("Seconds between active Creature Owner Ward reassignment checks."));
            DebugCreatureOwnerWard = Config.Bind("CreatureOwnerWard", "Debug", false, SyncedDescription("When true, logs Creature Owner Ward owner resolution and creature ownership changes."));
            DebugServerChest = Config.Bind("ServerChest", "Debug", false, SyncedDescription("When true, logs ServerChest registration, delivery, command, and ZDO save details."));
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
