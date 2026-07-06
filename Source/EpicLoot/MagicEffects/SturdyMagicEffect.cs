using HarmonyLib;
using UnityEngine;

namespace PraetorisClient
{
    internal static partial class PraetorisMagicEffects
    {
        private const string SturdyDefinitionJson = @"{
  ""Type"": ""Sturdy"",
  ""DisplayText"": ""Sturdy +{0:0.#}%"",
  ""Description"": ""Reduce crossbow pushback force by <b><color=yellow>X</color></b>%."",
  ""Requirements"": {
    ""AllowedSkillTypes"": [ ""Crossbows"" ],
    ""AllowedRarities"": [ ""Epic"", ""Legendary"", ""Mythic"" ]
  },
  ""ValuesPerRarity"": {
    ""Epic"": { ""MinValue"": 10, ""MaxValue"": 20, ""Increment"": 1 },
    ""Legendary"": { ""MinValue"": 20, ""MaxValue"": 30, ""Increment"": 1 },
    ""Mythic"": { ""MinValue"": 30, ""MaxValue"": 50, ""Increment"": 1 }
  },
  ""SelectionWeight"": 1,
  ""Prefixes"": [ ""Sturdy"" ],
  ""Suffixes"": [ ""Sturdiness"" ]
}";

        [HarmonyPatch(typeof(Character), nameof(Character.ApplyPushback), typeof(Vector3), typeof(float))]
        private static class Sturdy_Character_ApplyPushback_Patch
        {
            private static void Prefix(Character __instance, ref float pushForce)
            {
                if (__instance is Player player && PlayerHasEffect(player, Sturdy, out float modifier, PercentScale))
                {
                    pushForce *= Mathf.Clamp01(1f - modifier);
                }
            }
        }
    }
}
