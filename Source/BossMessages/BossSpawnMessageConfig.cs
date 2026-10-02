using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;

namespace PraetorisClient
{
    internal static class BossSpawnMessageConfig
    {
        internal static bool ApplyingManagedConfiguration;

        internal static void Draw(ConfigEntryBase entry)
        {
            if (ZNet.instance != null && !ZNet.instance.IsServer())
            {
                GUILayout.Label((bool)entry.BoxedValue ? "Enabled by server" : "Disabled by server");
                return;
            }

            entry.BoxedValue = GUILayout.Toggle((bool)entry.BoxedValue, "Enabled");
        }

        // Preserve other synchronized settings. Only this setting rejects client changes,
        // including changes submitted by an administrator's client.
        internal static void KeepServerValue(ZPackage package)
        {
            ZPackage source = new(package.GetArray());
            source.SetPos(package.GetPos());
            ZPackage result = new();
            result.Write(source.ReadByte());
            int count = source.ReadInt();
            result.Write(count);
            bool changed = false;
            for (int index = 0; index < count; index++)
            {
                string file = source.ReadString();
                string section = source.ReadString();
                string key = source.ReadString();
                string value = source.ReadString();
                if (file == "warpalicious.PraetorisClient.cfg" && section == "BossMessages" && key == "SuppressBossSpawnMessages")
                {
                    value = PraetorisClientPlugin.SuppressBossSpawnMessages.Value.ToString();
                    changed = true;
                }
                result.Write(file);
                result.Write(section);
                result.Write(key);
                result.Write(value);
            }

            if (changed)
            {
                package.Load(result.GetArray());
            }
        }
    }

    [HarmonyPatch(typeof(ConfigEntry<bool>), nameof(ConfigEntry<bool>.Value), MethodType.Setter)]
    internal static class BossSpawnMessageClientSettingLock
    {
        private static bool Prefix(ConfigEntry<bool> __instance)
        {
            return __instance != PraetorisClientPlugin.SuppressBossSpawnMessages ||
                   ZNet.instance == null || ZNet.instance.IsServer() || BossSpawnMessageConfig.ApplyingManagedConfiguration;
        }
    }

    [HarmonyPatch(typeof(SynchronizationManager), "ApplyConfigZPackage")]
    internal static class BossSpawnMessageServerConfiguration
    {
        private static void Prefix(ZPackage configPkg, out bool __state)
        {
            __state = BossSpawnMessageConfig.ApplyingManagedConfiguration;
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                BossSpawnMessageConfig.KeepServerValue(configPkg);
            }
            else
            {
                BossSpawnMessageConfig.ApplyingManagedConfiguration = true;
            }
        }

        private static void Finalizer(bool __state)
        {
            BossSpawnMessageConfig.ApplyingManagedConfiguration = __state;
        }
    }

    // Jotunn restores the client's saved local values while ZNet is being destroyed.
    // Allow that restoration so the server's setting does not persist after leaving.
    [HarmonyPatch(typeof(SynchronizationManager), "ResetAdminConfigs")]
    internal static class BossSpawnMessageLocalConfigurationRestore
    {
        private static void Prefix(out bool __state)
        {
            __state = BossSpawnMessageConfig.ApplyingManagedConfiguration;
            BossSpawnMessageConfig.ApplyingManagedConfiguration = true;
        }

        private static void Finalizer(bool __state)
        {
            BossSpawnMessageConfig.ApplyingManagedConfiguration = __state;
        }
    }
}
