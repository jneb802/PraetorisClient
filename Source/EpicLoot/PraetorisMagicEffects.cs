using HarmonyLib;
using UnityEngine;

namespace PraetorisClient
{
    internal static class PraetorisMagicEffects
    {
        internal const string DecreaseAdrenalineRequired = "DecreaseAdrenalineRequired";
        private const string ItemConsumesAdrenalineRequirement = "Praetoris.ItemConsumesAdrenaline";

        private const string DecreaseAdrenalineRequiredDefinitionJson = @"{
  ""Type"": ""DecreaseAdrenalineRequired"",
  ""DisplayText"": ""Adrenaline Required -{0:0.#}%"",
  ""Description"": ""Reduce required adrenaline by <b><color=yellow>X</color></b>%. "",
  ""Requirements"": {
    ""CustomFlags"": [
      ""Praetoris.ItemConsumesAdrenaline""
    ]
  },
  ""ValuesPerRarity"": {
    ""Magic"": {
      ""MinValue"": 5,
      ""MaxValue"": 10,
      ""Increment"": 1
    },
    ""Rare"": {
      ""MinValue"": 10,
      ""MaxValue"": 15,
      ""Increment"": 1
    },
    ""Epic"": {
      ""MinValue"": 15,
      ""MaxValue"": 25,
      ""Increment"": 1
    },
    ""Legendary"": {
      ""MinValue"": 25,
      ""MaxValue"": 35,
      ""Increment"": 1
    },
    ""Mythic"": {
      ""MinValue"": 35,
      ""MaxValue"": 50,
      ""Increment"": 1
    }
  },
  ""SelectionWeight"": 1,
  ""Prefixes"": [
    ""Efficient""
  ],
  ""Suffixes"": [
    ""Efficiency""
  ]
}";

        internal static void Register()
        {
            if (!EpicLootApiBridge.TryRegisterMagicEffectRequirement(ItemConsumesAdrenalineRequirement, ItemConsumesAdrenaline))
            {
                return;
            }

            if (EpicLootApiBridge.TryAddMagicEffect(DecreaseAdrenalineRequiredDefinitionJson, out string key))
            {
                PraetorisClientPlugin.Log.LogInfo("Registered Epic Loot magic effect " + DecreaseAdrenalineRequired + " with key " + key + ".");
            }
        }

        private static bool ItemConsumesAdrenaline(
            ItemDrop.ItemData item,
            object magicItem,
            string magicEffectType,
            bool checkLootRoll,
            bool checkAugmentRoll,
            bool checkRuneRoll)
        {
            return item?.m_shared?.m_fullAdrenalineSE != null;
        }

        [HarmonyPatch(typeof(Player), nameof(Player.GetMaxAdrenaline))]
        private static class DecreaseAdrenalineRequired_Player_GetMaxAdrenaline_Patch
        {
            private static void Postfix(Player __instance, ref float __result)
            {
                if (__instance == null || __result <= 0f)
                {
                    return;
                }

                float reduction = 0f;
                foreach (ItemDrop.ItemData item in __instance.GetInventory().GetEquippedItems())
                {
                    if (item?.m_shared?.m_fullAdrenalineSE == null)
                    {
                        continue;
                    }

                    reduction += EpicLootApiBridge.GetTotalActiveMagicEffectValue(
                        null,
                        item,
                        DecreaseAdrenalineRequired,
                        0.01f);
                }

                if (reduction > 0f)
                {
                    __result = Mathf.Max(1f, __result * (1f - reduction));
                }
            }
        }
    }
}
