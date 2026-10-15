using System.Collections.Generic;
using HarmonyLib;

namespace PraetorisClient.Map
{
    [HarmonyPatch(typeof(Minimap), "UpdateBiome")]
    internal static class AlternateBiomeLabel
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Minimap __instance, Player player)
        {
            if (__instance.m_mode != Minimap.MapMode.Small || player == null) return;

            BiomeSector sector = player.GetCurrentBiomeData();
            if (sector == null) return;

            string label = sector.GetName();
            if (PraetorisClientPlugin.ShowAlternateBiomeName.Value && sector.AltBiomes.Count > 0)
            {
                List<string> names = new List<string>();
                foreach (AltBiome biome in sector.AltBiomes)
                {
                    if (biome == null || string.IsNullOrWhiteSpace(biome.m_name)) continue;
                    string name = Localization.instance.Localize(biome.m_name).Trim();
                    if (name.Length > 0 && !names.Contains(name)) names.Add(name);
                }
                if (names.Count > 0) label += " (" + string.Join(", ", names) + ")";
            }

            // Rebuild from the game's biome name so toggling the setting removes the suffix.
            if (__instance.m_biomeNameSmall.text != label) __instance.m_biomeNameSmall.text = label;
        }
    }
}
