using HarmonyLib;

namespace PraetorisClient
{
    internal static partial class PraetorisMagicEffects
    {
        private const string ModifyAdrenalineDefinitionJson = @"{
  ""Type"": ""ModifyAdrenaline"",
  ""DisplayText"": ""Adrenaline +{0:0.#}%"",
  ""Description"": ""Increase adrenaline gained by <b><color=yellow>X</color></b>%."",
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
  ""Prefixes"": [ ""Eager"" ],
  ""Suffixes"": [ ""Adrenaline"" ]
}";

        [HarmonyPatch(typeof(Player), nameof(Player.AddAdrenaline))]
        private static class ModifyAdrenaline_Player_AddAdrenaline_Patch
        {
            private static void Prefix(Player __instance, ref float v)
            {
                if (__instance == null || v <= 0f)
                {
                    return;
                }

                float modifier = GetPlayerEffectValue(__instance, ModifyAdrenaline, PercentScale);
                if (modifier > 0f)
                {
                    v *= 1f + modifier;
                }
            }
        }
    }
}
