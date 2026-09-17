using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;

namespace PraetorisClient.ServerGuideFeature
{
    internal static class GuideConfigValues
    {
        internal static object Resolve(string reference)
        {
            List<ConfigEntryBase> matches = new List<ConfigEntryBase>();
            foreach (PluginInfo plugin in Chainloader.PluginInfos.Values)
            {
                if (plugin.Instance == null) continue;
                foreach (string name in new[] { plugin.Metadata.GUID, plugin.Metadata.Name }.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!reference.StartsWith(name + ".", StringComparison.OrdinalIgnoreCase)) continue;
                    string setting = reference.Substring(name.Length + 1);
                    foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> entry in plugin.Instance.Config)
                        if (string.Equals(setting, entry.Key.Key, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(setting, entry.Key.Section + "." + entry.Key.Key, StringComparison.OrdinalIgnoreCase))
                            if (!matches.Contains(entry.Value)) matches.Add(entry.Value);
                }
            }
            if (matches.Count == 0)
                throw new FormatException($"Config reference '{reference}' was not found in the server's loaded mod settings.");
            if (matches.Count > 1)
                throw new FormatException($"Config reference '{reference}' is ambiguous. Use the plugin GUID, section, and setting name.");
            return matches[0].BoxedValue;
        }
    }
}
