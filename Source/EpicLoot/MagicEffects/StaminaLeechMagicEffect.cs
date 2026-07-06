using HarmonyLib;
using UnityEngine;

namespace PraetorisClient
{
    internal static partial class PraetorisMagicEffects
    {
        private const string StaminaLeechDefinitionJson = @"{
  ""Type"": ""StaminaLeech"",
  ""DisplayText"": ""Stamina Leech +{0:0.#}%"",
  ""Description"": ""Recover <b><color=yellow>X</color></b>% of the attack stamina cost when hitting enemies."",
  ""Requirements"": {
    ""AllowedItemTypes"": [ ""TwoHandedWeapon"", ""Shield"" ],
    ""AllowedRarities"": [ ""Magic"", ""Rare"", ""Epic"", ""Legendary"", ""Mythic"" ]
  },
  ""ValuesPerRarity"": {
    ""Magic"": { ""MinValue"": 1, ""MaxValue"": 2, ""Increment"": 0.5 },
    ""Rare"": { ""MinValue"": 1, ""MaxValue"": 3, ""Increment"": 0.5 },
    ""Epic"": { ""MinValue"": 1, ""MaxValue"": 5, ""Increment"": 0.5 },
    ""Legendary"": { ""MinValue"": 1, ""MaxValue"": 10, ""Increment"": 0.5 },
    ""Mythic"": { ""MinValue"": 1, ""MaxValue"": 15, ""Increment"": 0.5 }
  },
  ""SelectionWeight"": 1,
  ""Prefixes"": [ ""Tiring"" ],
  ""Suffixes"": [ ""Stamina Leech"" ]
}";

        [HarmonyPatch(typeof(Attack), "AddHitPoint")]
        private static class StaminaLeech_Attack_AddHitPoint_Patch
        {
            private static float _lastStaminaLeech;

            private static void Postfix(Attack __instance, GameObject go)
            {
                if (__instance?.m_character is not Player player ||
                    _lastStaminaLeech + 1f > Time.time ||
                    go == null ||
                    go.GetComponentInParent<Character>() is not { } character ||
                    !PlayerHasEffect(player, StaminaLeech, out float modifier, PercentScale))
                {
                    return;
                }

                float staminaReturn = __instance.GetAttackStamina() * modifier;
                player.AddStamina(staminaReturn);
                DamageText.instance?.ShowText(DamageText.TextType.Bonus, character.GetTopPoint(), "+" + staminaReturn.ToString("0.0") + " $item_food_stamina", true);
                _lastStaminaLeech = Time.time;
            }
        }
    }
}
