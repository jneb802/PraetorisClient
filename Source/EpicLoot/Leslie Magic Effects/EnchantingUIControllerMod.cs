using EpicLoot;
using EpicLoot.CraftingV2;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using UnityEngine;

namespace EpicLootLeslieAlphaTest.src
{
    [HarmonyPatch(typeof(EnchantingUIController), "BuildEnchantedRune")]
    internal class EnchantingUIControllerMod

    {
        // remove rune extract limit block based on power modifier 
        // still capped at 999
        // rounds extracted values to 2 decimals
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var allDefinitionsField = AccessTools.Field(typeof(MagicItemEffectDefinitions), nameof(MagicItemEffectDefinitions.AllDefinitions));
            var effectValueField = AccessTools.Field(typeof(MagicItemEffect), nameof(MagicItemEffect.EffectValue));

            var codeMatcher = new CodeMatcher(instructions);

            codeMatcher.MatchStartForward(
                new CodeMatch(OpCodes.Ldsfld, allDefinitionsField));

            int startPos = codeMatcher.Pos;

            codeMatcher.MatchStartForward(
                new CodeMatch(OpCodes.Stfld, effectValueField));

            int endPos = codeMatcher.Pos;

            codeMatcher.Start()
                .Advance(startPos)
                .RemoveInstructions(endPos - startPos + 1);

            return codeMatcher.InstructionEnumeration();
        }

        [HarmonyPostfix]
        static void Postfix(ref ItemDrop.ItemData __result)
        {
            if (__result == null) return;
            var magicItem = __result.GetMagicItem();
            if (magicItem == null) return;
            foreach (var effect in magicItem.Effects)
            {
                effect.EffectValue = (float)Math.Round(effect.EffectValue, 2);
            }
            __result.SaveMagicItem(magicItem);
        }
    }

    public static class EnchantingHelper
    {
        internal static bool AwaitingConfirmation = false;
        internal static float PendingDestroyChance = 0f;
        public static float RuneTooPowerfulEtchDestructionChance(ItemDrop.ItemData item, ItemDrop.ItemData rune)
        {
            List<MagicItemEffect> runeEffects = rune.GetMagicItem().Effects;
            MagicItemEffect runeEffect = runeEffects[0];

            var valueType = MagicItemEffectDefinitions.AllDefinitions[runeEffect.EffectType].ValuesPerRarity.GetValueDefForRarity(item.GetRarity());

            if (valueType != null)
            {
                float maxDefaultValue = (MagicItemEffectDefinitions.AllDefinitions[runeEffect.EffectType].ValuesPerRarity.GetValueDefForRarity(item.GetRarity()).MaxValue);


                bool tooPowerful = runeEffect.EffectValue >= maxDefaultValue * 1.1f; // bool value to run a check instead of x > y 
                float howPowerful = ((runeEffect.EffectValue / maxDefaultValue) - 1f); // Effect power over expressed as a %

                Debug.LogWarning($"[EpicLootAlpha] effectValue: {runeEffect.EffectValue}, maxDefault: {maxDefaultValue}, tooPowerful: {tooPowerful}");
                if (tooPowerful)
                {
                    float flatChance = howPowerful * 100f;
                    flatChance = Mathf.Clamp(flatChance, 10f, 90f); // 10% over 10% chance base. sliding to 90% chance to break if over 90% stronger. 10 value to 19 value becomes 90% chance to break on etching that value. 
                    float destroyChance = Mathf.Round(flatChance / 5f) * 5f; // Round to increments of 5%
                    return destroyChance;
                }
            }

            return 0f;
        }
    }

    [HarmonyPatch(typeof(EnchantingUIController), "RuneEnhanceItemAndReturnSuccess")]
    internal class RuneEnhanceItem_DestroyChance_Patch
    {
        [HarmonyPrefix]
        static bool Prefix(ItemDrop.ItemData item, ItemDrop.ItemData rune, int enchantment, ref GameObject __result)
        {

            List<MagicItemEffect> runeEffects = rune.GetMagicItem().Effects;

            MagicItemEffect runeEffect = runeEffects[0];

            var valueType = MagicItemEffectDefinitions.AllDefinitions[runeEffect.EffectType].ValuesPerRarity.GetValueDefForRarity(item.GetRarity());

            if (valueType != null)
            {
                float destroyChance = EnchantingHelper.RuneTooPowerfulEtchDestructionChance(item, rune);

                if (destroyChance > 0f)
                {
                    float roll = UnityEngine.Random.value * 100f;
                    //EpicLoot.LogWarningForce($"Etch roll {roll:F1} destroy chance {destroyChance}%");
                    if (destroyChance > roll)
                    {
                        Player.m_localPlayer.UnequipItem(item);
                        Player.m_localPlayer.GetInventory().RemoveItem(item);
                        Player.m_localPlayer.GetInventory().RemoveItem(rune);
                        Player.m_localPlayer.Message(MessageHud.MessageType.Center, $"You rolled {roll:F1} You are not worthy of the rune's power. The item and rune has been destroyed.");
                        __result = null;
                        return false;
                    }
                }
            }
            return true;
        }
    }
}

