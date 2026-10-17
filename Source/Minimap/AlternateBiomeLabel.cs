using System.Collections.Generic;
using HarmonyLib;
using TMPro;

namespace PraetorisClient.Map
{
    [HarmonyPatch(typeof(Minimap), "UpdateBiome")]
    internal static class AlternateBiomeLabel
    {
        private static TMP_Text? _label;
        private static VerticalAlignmentOptions _originalVerticalAlignment;

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Minimap __instance, Player player)
        {
            if (__instance.m_mode != Minimap.MapMode.Small || player == null) return;

            BiomeSector sector = player.GetCurrentBiomeData();
            if (sector == null) return;

            TMP_Text text = __instance.m_biomeNameSmall;
            if (_label != text)
            {
                _label = text;
                _originalVerticalAlignment = text.verticalAlignment;
            }

            string label = sector.GetName();
            bool hasAlternateName = false;
            if (PraetorisClientPlugin.ShowAlternateBiomeName.Value && sector.AltBiomes.Count > 0)
            {
                List<string> names = new List<string>();
                foreach (AltBiome biome in sector.AltBiomes)
                {
                    if (biome == null || string.IsNullOrWhiteSpace(biome.m_name)) continue;
                    string name = Localization.instance.Localize(biome.m_name).Trim();
                    if (name.Length > 0 && !names.Contains(name)) names.Add(name);
                }
                if (names.Count > 0)
                {
                    label += " (" + string.Join(", ", names) + ")";
                    hasAlternateName = true;
                }
            }

            // Keep the first line inside the map; wrapped lines must extend downward.
            text.verticalAlignment = hasAlternateName ? VerticalAlignmentOptions.Top : _originalVerticalAlignment;
            // Rebuild from the game's biome name so toggling the setting removes the suffix.
            if (text.text != label) text.text = label;
        }
    }
}
