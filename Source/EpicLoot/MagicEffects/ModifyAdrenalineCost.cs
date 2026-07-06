using HarmonyLib;
using UnityEngine;

namespace PraetorisClient
{
    internal static partial class PraetorisMagicEffects
    {
        private const string ModifyAdrenalineCostDefinitionJson = @"{
  ""Type"": ""ModifyAdrenalineCost"",
  ""DisplayText"": ""Adrenaline Required -{0:0.#}%"",
  ""Description"": ""Reduce required adrenaline by <b><color=yellow>X</color></b>%."",
  ""Requirements"": {
    ""AllowedItemTypes"": [ ""Trinket"" ],
    ""AllowedRarities"": [ ""Magic"", ""Rare"", ""Epic"", ""Legendary"", ""Mythic"" ]
  },
  ""ValuesPerRarity"": {
    ""Magic"": { ""MinValue"": 1, ""MaxValue"": 5, ""Increment"": 1 },
    ""Rare"": { ""MinValue"": 5, ""MaxValue"": 10, ""Increment"": 1 },
    ""Epic"": { ""MinValue"": 10, ""MaxValue"": 20, ""Increment"": 1 },
    ""Legendary"": { ""MinValue"": 20, ""MaxValue"": 30, ""Increment"": 1 },
    ""Mythic"": { ""MinValue"": 30, ""MaxValue"": 40, ""Increment"": 1 }
  },
  ""SelectionWeight"": 1,
  ""Prefixes"": [ ""Efficient"" ],
  ""Suffixes"": [ ""Efficiency"" ]
}";

        [HarmonyPatch(typeof(Player), nameof(Player.GetMaxAdrenaline))]
        private static class AdrenalineCost_Player_GetMaxAdrenaline_Patch
        {
            private static void Postfix(Player __instance, ref float __result)
            {
                if (__instance == null || __result <= 0f)
                {
                    return;
                }

                float reduction = GetPlayerEffectValue(__instance, ModifyAdrenalineCost, PercentScale);
                if (reduction > 0f)
                {
                    __result = Mathf.Max(1f, __result * Mathf.Clamp(1f - reduction, 0.5f, 1f));
                }
            }
        }
    }
}
