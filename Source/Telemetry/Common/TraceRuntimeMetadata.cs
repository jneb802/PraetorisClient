using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;

namespace PraetorisClient
{
    internal readonly struct TraceMethodSource
    {
        internal TraceMethodSource(
            string pluginGuid,
            string pluginName,
            string pluginVersion,
            string assemblyName,
            bool isVanilla,
            bool isModded)
        {
            PluginGuid = pluginGuid;
            PluginName = pluginName;
            PluginVersion = pluginVersion;
            AssemblyName = assemblyName;
            IsVanilla = isVanilla;
            IsModded = isModded;
        }

        internal string PluginGuid { get; }
        internal string PluginName { get; }
        internal string PluginVersion { get; }
        internal string AssemblyName { get; }
        internal bool IsVanilla { get; }
        internal bool IsModded { get; }
    }

    internal static class TraceRuntimeMetadata
    {
        private static readonly TraceMethodSource UnknownSource = new("", "", "", "", false, false);

        internal static string GetGameVersion()
        {
            try
            {
                return global::Version.GetVersionString();
            }
            catch
            {
                return Application.version ?? "";
            }
        }

        internal static string BuildRuntimeId(string role)
        {
            return role
                + ":"
                + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")
                + ":"
                + Guid.NewGuid().ToString("N");
        }

        internal static void WritePlugins(TelemetryJson json)
        {
            List<TraceMethodSource> plugins = GetRuntimePlugins();
            json.BeginArray("plugins");
            foreach (TraceMethodSource plugin in plugins)
            {
                json.BeginArrayObject();
                json.Prop("pluginGuid", plugin.PluginGuid);
                json.Prop("pluginName", plugin.PluginName);
                json.Prop("pluginVersion", plugin.PluginVersion);
                json.Prop("assemblyName", plugin.AssemblyName);
                json.Prop("isVanilla", plugin.IsVanilla);
                json.EndArrayObject();
            }
            json.EndArray();
        }

        internal static TraceMethodSource GetMethodSource(Delegate? callback)
        {
            Assembly? assembly = callback?.Method?.DeclaringType?.Assembly;
            if (assembly == null)
                return UnknownSource;

            string assemblyName = assembly.GetName().Name ?? "";
            if (IsVanillaAssembly(assemblyName))
                return new TraceMethodSource("vanilla", "Valheim", GetGameVersion(), assemblyName, true, false);

            foreach (PluginInfo pluginInfo in Chainloader.PluginInfos.Values)
            {
                Assembly? pluginAssembly = pluginInfo.Instance != null ? pluginInfo.Instance.GetType().Assembly : null;
                if (pluginAssembly == null || !ReferenceEquals(pluginAssembly, assembly))
                    continue;

                return new TraceMethodSource(
                    pluginInfo.Metadata.GUID ?? "",
                    pluginInfo.Metadata.Name ?? "",
                    pluginInfo.Metadata.Version?.ToString() ?? "",
                    assemblyName,
                    false,
                    true);
            }

            return new TraceMethodSource("", "", "", assemblyName, false, false);
        }

        private static List<TraceMethodSource> GetRuntimePlugins()
        {
            List<TraceMethodSource> plugins = new()
            {
                new TraceMethodSource("vanilla", "Valheim", GetGameVersion(), "assembly_valheim", true, false)
            };

            foreach (PluginInfo pluginInfo in Chainloader.PluginInfos.Values)
            {
                string assemblyName = pluginInfo.Instance != null
                    ? pluginInfo.Instance.GetType().Assembly.GetName().Name ?? ""
                    : "";
                plugins.Add(new TraceMethodSource(
                    pluginInfo.Metadata.GUID ?? "",
                    pluginInfo.Metadata.Name ?? "",
                    pluginInfo.Metadata.Version?.ToString() ?? "",
                    assemblyName,
                    false,
                    true));
            }

            plugins.Sort((left, right) => string.Compare(left.PluginGuid, right.PluginGuid, StringComparison.OrdinalIgnoreCase));
            return plugins;
        }

        private static bool IsVanillaAssembly(string assemblyName)
        {
            return string.Equals(assemblyName, "assembly_valheim", StringComparison.OrdinalIgnoreCase)
                || string.Equals(assemblyName, "Valheim", StringComparison.OrdinalIgnoreCase);
        }
    }
}
