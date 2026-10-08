using System;
using System.Collections.Generic;
using EpicLoot.ShardStones;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace PraetorisClient.EpicLootFeature
{
    internal static class FullHealthShard
    {
        internal const string MeleeDamage = "PraetorisFullHealthMeleeDamage";
        internal const string ProjectileDamage = "PraetorisFullHealthProjectileDamage";
        internal const string EitrCost = "PraetorisFullHealthEitrCost";
        internal const string BlockArmor = "PraetorisFullHealthBlockArmor";
        internal const string StaminaRegen = "PraetorisFullHealthStaminaRegen";
        internal const string AdrenalineGain = "PraetorisFullHealthAdrenalineGain";
        internal const string RunCost = "PraetorisFullHealthRunCost";

        // Initial balance: Magic, Rare, Epic, Legendary, Mythic, Ancient.
        internal static readonly float[] Values = { 5, 7, 10, 15, 20, 25 };
        internal const float HealthTolerance = 0.01f;
        internal const float MaximumCostReduction = 0.8f;
        internal static readonly IReadOnlyDictionary<ShardSlotCategory, string> TypeEffects =
            new Dictionary<ShardSlotCategory, string>
            {
                [ShardSlotCategory.MeleeWeapon] = MeleeDamage,
                [ShardSlotCategory.RangedWeapon] = ProjectileDamage,
                [ShardSlotCategory.MagicWeapon] = EitrCost,
                [ShardSlotCategory.Shield] = BlockArmor,
                [ShardSlotCategory.Armor] = StaminaRegen,
                [ShardSlotCategory.Trinket] = AdrenalineGain,
                [ShardSlotCategory.Utility] = RunCost
            };

        internal static bool IsFullHealth(Player? player)
        {
            return player != null && !player.IsDead() && player.GetHealth() > 0f &&
                player.GetMaxHealth() > 0f && player.GetHealth() >= player.GetMaxHealth() - HealthTolerance;
        }

        private static float PlayerBonus(Player? player, string effect)
        {
            return IsFullHealth(player)
                ? Mathf.Max(0f, EpicLootApiBridge.GetTotalPlayerActiveMagicEffectValue(player!, effect, 0.01f))
                : 0f;
        }

        private static float WeaponBonus(Player? player, ItemDrop.ItemData weapon, string effect)
        {
            return IsFullHealth(player)
                ? Mathf.Max(0f, EpicLootApiBridge.GetTotalActiveMagicEffectValueForWeapon(null, weapon, effect, 0.01f))
                : 0f;
        }

        internal static void RegisterEffects()
        {
            Dictionary<string, string> descriptions = new Dictionary<string, string>
            {
                [MeleeDamage] = "Melee damage +{0:0.#}% while at full health (this weapon).",
                [ProjectileDamage] = "Projectile damage +{0:0.#}% while at full health when fired (this weapon).",
                [EitrCost] = "Attack eitr cost -{0:0.#}% while at full health (this weapon; maximum reduction 80%).",
                [BlockArmor] = "Block armor +{0:0.#}% while at full health (this shield).",
                [StaminaRegen] = "Stamina regeneration +{0:0.#}% while at full health.",
                [AdrenalineGain] = "Adrenaline gain +{0:0.#}% while at full health.",
                [RunCost] = "Running stamina cost -{0:0.#}% while at full health (maximum reduction 80%)."
            };
            string[] rarities = { "Magic", "Rare", "Epic", "Legendary", "Mythic", "Ancient" };
            foreach (KeyValuePair<string, string> pair in descriptions)
            {
                JObject values = new JObject();
                for (int index = 0; index < rarities.Length; index++)
                {
                    values[rarities[index]] = new JObject
                    {
                        ["MinValue"] = Values[index], ["MaxValue"] = Values[index], ["Increment"] = 1
                    };
                }
                JObject definition = new JObject
                {
                    ["Type"] = pair.Key,
                    ["DisplayText"] = pair.Value,
                    ["Description"] = "Requires full health. Copies follow the server's shard stacking rules. Weapon and shield bonuses apply only to the item holding the shard.",
                    ["CanBeAugmented"] = false,
                    ["CanBeDisenchanted"] = false,
                    ["CanBeRunified"] = false,
                    ["Requirements"] = new JObject { ["NoRoll"] = true },
                    ["ValuesPerRarity"] = values
                };
                if (!EpicLootApiBridge.TryAddMagicEffect(definition.ToString(), out string key))
                {
                    throw new InvalidOperationException($"Could not register full-health effect {pair.Key}");
                }
            }
        }

        [HarmonyPatch(typeof(Attack), "ModifyDamage")]
        private static class AttackDamagePatch
        {
            private static void Postfix(Attack __instance, HitData hitData)
            {
                if (__instance.m_character is not Player player || __instance.m_weapon == null)
                {
                    return;
                }
                ShardSlotCategory? slot = Shards.ResolveCategory(__instance.m_weapon);
                ShardSlotCategory? group = slot.HasValue ? Shards.GroupOf(slot.Value) : null;
                string? effect = group == ShardSlotCategory.MeleeWeapon ? MeleeDamage :
                    group == ShardSlotCategory.RangedWeapon ? ProjectileDamage : null;
                if (effect != null)
                {
                    hitData.m_damage.Modify(1f + WeaponBonus(player, __instance.m_weapon, effect));
                }
            }
        }

        // Patch the cost query so affordability checks and actual payment use the same cost.
        [HarmonyPatch(typeof(Attack), nameof(Attack.GetAttackEitr), typeof(Character), typeof(ItemDrop.ItemData))]
        private static class AttackEitrPatch
        {
            private static void Postfix(Character character, ItemDrop.ItemData weapon, ref float __result)
            {
                if (__result > 0f && character is Player player)
                {
                    __result *= 1f - Mathf.Min(MaximumCostReduction, WeaponBonus(player, weapon, EitrCost));
                }
            }
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetBlockPower), typeof(int), typeof(float))]
        private static class BlockPowerPatch
        {
            private static void Postfix(ItemDrop.ItemData __instance, ref float __result)
            {
                Player? player = Player.m_localPlayer;
                if (player != null && player.IsItemEquiped(__instance))
                {
                    __result *= 1f + WeaponBonus(player, __instance, BlockArmor);
                }
            }
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyStaminaRegen))]
        private static class StaminaRegenPatch
        {
            private static void Postfix(SEMan __instance, ref float staminaMultiplier)
            {
                staminaMultiplier += PlayerBonus(__instance.m_character as Player, StaminaRegen);
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.AddAdrenaline))]
        private static class AdrenalinePatch
        {
            private static void Prefix(Player __instance, ref float v)
            {
                if (v > 0f)
                {
                    v *= 1f + PlayerBonus(__instance, AdrenalineGain);
                }
            }
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyRunStaminaDrain))]
        private static class RunningStaminaPatch
        {
            private static void Postfix(SEMan __instance, ref float drain)
            {
                if (drain > 0f)
                {
                    drain *= 1f - Mathf.Min(MaximumCostReduction, PlayerBonus(__instance.m_character as Player, RunCost));
                }
            }
        }
    }
}
