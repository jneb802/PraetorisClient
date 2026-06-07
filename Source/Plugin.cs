using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace PraetorisClient
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class PraetorisClientPlugin : BaseUnityPlugin
    {
        private const string ModName = "PraetorisClient";
        private const string ModVersion = "0.1.1";
        private const string Author = "warpalicious";
        private const string ModGUID = Author + "." + ModName;
        private const string LinkApiUrlEnv = "PRAETORISCLIENT_LINK_API_URL";
        private const string BotApiKeyEnv = "PRAETORISCLIENT_BOT_API_KEY";

        private readonly Harmony _harmony = new(ModGUID);
        private DateTime _lastReloadTime;
        private FileSystemWatcher? _configWatcher;
        private const long ReloadDelayTicks = 10000000;

        public static PraetorisClientPlugin? Instance { get; private set; }
        public static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource(ModName);

        internal static ConfigEntry<string> LinkApiUrl = null!;
        internal static ConfigEntry<string> BotApiKey = null!;
        internal static ConfigEntry<string> LinkCommand = null!;

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
            SiegePortalTestCommand.Register();
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
            SetupWatcher();
        }

        private void OnDestroy()
        {
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
        }

        private void SetupWatcher()
        {
            try
            {
                _lastReloadTime = DateTime.Now;
                _configWatcher?.Dispose();
                _configWatcher = new FileSystemWatcher(Paths.ConfigPath, ModGUID + ".cfg");
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
            string configPath = Path.Combine(Paths.ConfigPath, ModGUID + ".cfg");
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
