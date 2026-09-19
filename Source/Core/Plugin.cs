using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using EpicLoot.Config;
using EpicLootAPI;
using EpicLootLeslieAlphaTest.src;
using EpicLootLeslieAlphaTest.src.StatusEffects;
using EpicLootLeslieAlphaTest.src.StatusEffects.VFX;
using EpicLootLeslieAlphaTest.src.Utilities;
using HarmonyLib;
using Jotunn.Managers;
using Jotunn.Utils;
using PraetorisClient.CreatureOwnership;
using PraetorisClient.Maintenance;
using PraetorisClient.ServerChestFeature;
using PraetorisClient.SurtlingBoats;
using System;
using System.IO;
using System.Reflection;

namespace PraetorisClient
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [BepInDependency(EpicLootApiBridge.PluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("org.tristan.rcon", BepInDependency.DependencyFlags.SoftDependency)]

    public class PraetorisClientPlugin : BaseUnityPlugin
    {
        private const string ModName = "PraetorisClient";
        private const string ModVersion = "0.1.77";
        private const string Author = "warpalicious";
        private const string ModGUID = Author + "." + ModName;
        private const string EpicLootGuid = "randyknapp.mods.epicloot";
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
        internal static ConfigEntry<bool> SurtlingBoatsEnabled = null!;
        internal static ConfigEntry<string> SurtlingBoatFuelItemPrefab = null!;
        internal static ConfigEntry<float> SurtlingBoatSecondsPerFuelItem = null!;
        internal static ConfigEntry<bool> SurtlingBoatFreeFuel = null!;
        internal static ConfigEntry<float> SurtlingBoatBackBoost = null!;
        internal static ConfigEntry<float> SurtlingBoatSlowBoost = null!;
        internal static ConfigEntry<float> SurtlingBoatHalfBoost = null!;
        internal static ConfigEntry<float> SurtlingBoatFullBoost = null!;
        internal static ConfigEntry<KeyboardShortcut> SurtlingBoatToggleKey = null!;
        internal static ConfigEntry<float> CreatureOwnerWardRadius = null!;
        internal static ConfigEntry<float> CreatureOwnerWardUpdateIntervalSeconds = null!;
        internal static ConfigEntry<bool> DebugCreatureOwnerWard = null!;
        internal static ConfigEntry<string> NetworkWardAllowedSteamIds = null!;
        internal static ConfigEntry<bool> DebugServerChest = null!;
        internal static ConfigEntry<bool> BlockPeerServerSyncConfigSync = null!;
        internal static ConfigEntry<bool> ProtectCraftyBoxesWardChests = null!;
        internal static ConfigEntry<string> MaintenanceEndUtc = null!;
        internal static ConfigEntry<bool> MaintenanceDailyWindowEnabled = null!;
        internal static ConfigEntry<string> MaintenanceDailyWindowStartUtc = null!;
        internal static ConfigEntry<int> MaintenanceDailyWindowDurationMinutes = null!;
        internal static ConfigEntry<string> MaintenanceDailyWindowSuppressedUntilUtc = null!;

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
            bool epicLootLoaded = Chainloader.PluginInfos.ContainsKey(EpicLootGuid);
            if (epicLootLoaded)
            {
                InitializeEpicLoot();
            }
            else
            {
                Log.LogInfo("Epic Loot is not loaded. Epic Loot integration is disabled.");
            }

            Instance = this;
            BindConfig();
            SynchronizationManager.OnConfigurationSynchronized += OnConfigurationSynchronized;
            PraetorisMagicEffects.Register();
            if (epicLootLoaded)
            {
                EpicLootFeature.PraetorisShardstones.Initialize();
            }
            CreatureOwnerWardPiece.Initialize();
            CreatureOwnerWardCommand.Register();
            NetworkWardFeature.NetworkWardPiece.Initialize();
            ServerChestPiece.Initialize();
            ServerChestCommand.Register();
            MaintenanceCommand.Register();
            ServerGuideFeature.ServerGuide.Initialize();
            if (Chainloader.PluginInfos.ContainsKey(ServerChestRconCommand.ValheimRconGuid))
            {
                ServerChestRconCommand.Register();
                MaintenanceRconCommand.Register();
            }

            CleanseMeadFeature.Initialize();
            SiegePortalTestCommand.Register();
            FrameTimeMonitor.Initialize();
            RpcTraceTelemetry.Initialize();
            ApplyHarmonyPatches(epicLootLoaded);
            CraftyBoxesWardGuard.TryApply(_harmony);
            ProtectedLocationNoBuild.ApplyToLoadedLocations();
            SocketMetricPatches.ApplyManualPatches(_harmony);
            SetupWatcher();
        }

        private static void InitializeEpicLoot()
        {
            DisableEpicLootConfigurationChoice();
            PrefabManager.OnPrefabsRegistered += () =>
            {
                if (Loaded)
                {
                    return;
                }

                HumanoidFactory.Create();
                Loaded = true;
            };
            PrefabManager.OnPrefabsRegistered += InfusionVFX.Init;
            MagicEffects.Init();
            SERegistry.RegisterStatusEffects();
            EpicLootAPI.EpicLoot.RegisterAll();
        }

        private static void DisableEpicLootConfigurationChoice()
        {
            if (ELConfig.AlwaysShowWelcomeMessage == null || !ELConfig.AlwaysShowWelcomeMessage.Value)
            {
                return;
            }

            ELConfig.AlwaysShowWelcomeMessage.Value = false;
            Log.LogInfo("Disabled the Epic Loot configuration choice window on the main menu.");
        }

        private void ApplyHarmonyPatches(bool epicLootLoaded)
        {
            foreach (Type type in AccessTools.GetTypesFromAssembly(Assembly.GetExecutingAssembly()))
            {
                string typeNamespace = type.Namespace ?? "";
                if (!epicLootLoaded &&
                    (typeNamespace.StartsWith("EpicLootLeslieAlphaTest", StringComparison.Ordinal) ||
                     typeNamespace.StartsWith("PraetorisClient.EpicLootFeature", StringComparison.Ordinal)))
                {
                    continue;
                }

                _harmony.CreateClassProcessor(type).Patch();
            }
        }

        private void Update()
        {
            MaintenanceMode.UpdateScheduledWindow();

            if (Game.instance == null)
                RpcTraceTelemetry.BackgroundUpdate();

            SurtlingBoatFeature.Update();
            ServerGuideFeature.ServerGuide.Update();
        }

        private void OnDestroy()
        {
            SynchronizationManager.OnConfigurationSynchronized -= OnConfigurationSynchronized;
            CleanseMeadFeature.Shutdown();
            CreatureOwnerWardPiece.Shutdown();
            NetworkWardFeature.NetworkWardPiece.Shutdown();
            WardBuildIcon.Shutdown();
            ServerChestPiece.Shutdown();
            SurtlingBoatFeature.Shutdown();
            ServerGuideFeature.GuideImages.Clear();

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
            NetworkWardAllowedSteamIds = Config.Bind("NetworkWard", "AllowedSteamIds", "",
                "Server-only whitelist for Network Ward access. Comma-separated SteamID64 values (Steam_ prefix also accepted). Empty denies everyone, including admins. Client settings cannot grant access. Requires authenticated Steam connections.");
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
            SurtlingBoatsEnabled = Config.Bind("SurtlingBoats", "Enabled", true, SyncedDescription("Allows a ship driver to use fuel from the ship inventory for an extra motor force."));
            SurtlingBoatFuelItemPrefab = Config.Bind("SurtlingBoats", "FuelItemPrefab", "SurtlingCore", SyncedDescription("Prefab name of the item consumed from the ship inventory."));
            SurtlingBoatSecondsPerFuelItem = Config.Bind("SurtlingBoats", "SecondsPerFuelItem", 300f, SyncedDescription("Seconds of active motor force supplied by one fuel item."));
            SurtlingBoatFreeFuel = Config.Bind("SurtlingBoats", "FreeFuel", false, SyncedDescription("Supplies motor force without consuming an item from the ship inventory."));
            SurtlingBoatBackBoost = Config.Bind("SurtlingBoats", "BackBoost", 1.5f, SyncedDescription("Extra motor force while the ship moves backward."));
            SurtlingBoatSlowBoost = Config.Bind("SurtlingBoats", "SlowBoost", 1.02f, SyncedDescription("Extra motor force at rowing speed."));
            SurtlingBoatHalfBoost = Config.Bind("SurtlingBoats", "HalfBoost", 1.2f, SyncedDescription("Extra motor force at half sail."));
            SurtlingBoatFullBoost = Config.Bind("SurtlingBoats", "FullBoost", 1.5f, SyncedDescription("Extra motor force at full sail."));
            SurtlingBoatToggleKey = Config.Bind("SurtlingBoats", "ToggleKey", new KeyboardShortcut(UnityEngine.KeyCode.LeftShift), "Local key used by the current ship driver to enable or disable the motor.");
            BlockPeerServerSyncConfigSync = Config.Bind("ServerSyncProtection", "BlockPeerServerSyncConfigSync", true, SyncedDescription("Blocks outgoing ServerSync config packets so Praetoris clients do not publish client-to-client config changes."));
            ProtectCraftyBoxesWardChests = Config.Bind("Compatibility", "ProtectCraftyBoxesWardChests", true, SyncedDescription("Prevents AzuCraftyBoxes from reading or removing items from Protective Wards chests when the local player does not have ward access."));
            CreatureOwnerWardRadius = Config.Bind("CreatureOwnerWard", "Radius", 40f, SyncedDescription("Meters around an active Creature Owner Ward where monster ZDO ownership is assigned to the configured connected player."));
            CreatureOwnerWardUpdateIntervalSeconds = Config.Bind("CreatureOwnerWard", "UpdateIntervalSeconds", 2f, SyncedDescription("Seconds between active Creature Owner Ward reassignment checks."));
            DebugCreatureOwnerWard = Config.Bind("CreatureOwnerWard", "Debug", false, SyncedDescription("When true, logs Creature Owner Ward owner resolution and creature ownership changes."));
            DebugServerChest = Config.Bind("ServerChest", "Debug", false, SyncedDescription("When true, logs ServerChest registration, delivery, command, and ZDO save details."));
            MaintenanceEndUtc = Config.Bind("Maintenance", "EndUtc", "", "Server-local maintenance end time in UTC. Use maintenance_start and maintenance_end instead of editing this value while the server runs.");
            MaintenanceDailyWindowEnabled = Config.Bind("Maintenance", "DailyWindowEnabled", false, "When true, the server enters maintenance during the configured daily UTC time window.");
            MaintenanceDailyWindowStartUtc = Config.Bind("Maintenance", "DailyWindowStartUtc", "08:00", "Daily maintenance start time in 24-hour HH:mm UTC format.");
            MaintenanceDailyWindowDurationMinutes = Config.Bind("Maintenance", "DailyWindowDurationMinutes", 5, "Daily maintenance duration in minutes. Valid values are 1 through 1440.");
            MaintenanceDailyWindowSuppressedUntilUtc = Config.Bind("Maintenance", "DailyWindowSuppressedUntilUtc", "", "Internal UTC end time used when maintenance_end ends the current daily window early.");
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
