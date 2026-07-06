using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PraetorisClient
{
    internal static partial class PraetorisMagicEffects
    {
        private const string IncreaseEffectDurationDefinitionJson = @"{
  ""Type"": ""IncreaseEffectDuration"",
  ""DisplayText"": ""Effect Duration +{0:0.#}%"",
  ""Description"": ""Increase the duration of timed status effects applied by this item by +<b><color=yellow>X</color></b>%. Also affects supported timed status effects from equipped gear."",
  ""Requirements"": {
    ""AllowedItemTypes"": [ ""Staff"", ""Bow"", ""Crossbows"", ""Helmet"", ""Chest"", ""Legs"", ""Shoulder"", ""Utility"", ""Trinket"" ]
  },
  ""ValuesPerRarity"": {
    ""Magic"": { ""MinValue"": 10, ""MaxValue"": 15, ""Increment"": 1 },
    ""Rare"": { ""MinValue"": 15, ""MaxValue"": 25, ""Increment"": 1 },
    ""Epic"": { ""MinValue"": 25, ""MaxValue"": 35, ""Increment"": 1 },
    ""Legendary"": { ""MinValue"": 35, ""MaxValue"": 45, ""Increment"": 1 },
    ""Mythic"": { ""MinValue"": 45, ""MaxValue"": 60, ""Increment"": 1 }
  },
  ""SelectionWeight"": 3,
  ""Prefixes"": [ ""Lingering"" ],
  ""Suffixes"": [ ""Persistence"" ]
}";

        private static class IncreaseEffectDurationRuntime
        {
            private const float ModifiedTtlTolerance = 0.01f;

            private static readonly Stack<EffectSource> EffectSources = new Stack<EffectSource>();
            private static readonly ConditionalWeakTable<StatusEffect, DurationMarker> DurationMarkers = new ConditionalWeakTable<StatusEffect, DurationMarker>();
            private static readonly HashSet<string> SupportedStatusEffectNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Staff_shield",
                "SE_Inspiration_DO",
                "SE_InspirationTrinket_DO",
                "SE_MoonCookieRiko_DO",
                "SE_SunCookieRiko_DO",
                "SE_MushroomcallerArmor_DO",
                "SE_MushroomcallerBelt_DO",
                "SE_MushroomcallerSlow_DO",
                "SE_TotemcallerArmor_DO",
                "SE_TotemcallerBelt_DO",
                "SE_TotemcallerShield_DO",
                "SE_TotemcallerOrbit_DO",
                "SE_TotemSlow_DO",
                "SE_TotemBuff_DO",
                "SE_DeathcallerArmor_DO",
                "SE_DeathcallerBelt_DO",
                "SE_DeathcallerDeathTrigger_DO",
                "SE_DeathcallerDebuff_DO",
                "SE_FrostcallerArmor_DO",
                "SE_FrostcallerBelt_DO",
                "SE_FrostcallerAura_DO",
                "SE_StonecallerArmor_DO",
                "SE_StonecallerBelt_DO",
                "SE_StonecallerShield_DO",
                "SE_FirecallerArmor_DO",
                "SE_FirecallerBelt_DO",
                "SE_FirecallerOrbit_DO",
                "SE_FirecallerShield_DO",
                "SE_WindcallerArmor_DO",
                "SE_WindcallerBelt_DO",
                "SE_WindcallerShield_DO",
                "SE_WindcallerPetal_DO",
                "SE_LightcallerArmor_DO",
                "SE_LightcallerBelt_DO",
                "SE_LightcallerAura_DO",
                "SE_LightcallerHeal_DO",
                "SE_LightcallerSigil_DO",
                "SE_StormcallerArmor_DO",
                "SE_StormcallerBelt_DO",
                "SE_StormcallerOrbit_DO",
                "SE_BloodcallerArmor_DO",
                "SE_BloodcallerTrinket_DO",
                "SE_BloodcallerHeal_DO",
                "SE_BloodDebuff_DO",
                "SE_BloodBuff_DO",
                "SE_BloodcallerDeathTrigger_DO",
                "SE_BloodcallerDeathHeal_DO",
                "SE_EitrWellBuff_DO",
                "SE_BloodFountainBuff_DO"
            };

            private readonly struct EffectSource
            {
                public readonly Player Player;
                public readonly ItemDrop.ItemData Item;

                public EffectSource(Player player, ItemDrop.ItemData item)
                {
                    Player = player;
                    Item = item;
                }
            }

            private sealed class DurationMarker
            {
                public float BaseTtl;
                public float ModifiedTtl;
            }

            internal static bool PushSource(Player player, ItemDrop.ItemData item)
            {
                if (player == null || item == null)
                {
                    return false;
                }

                EffectSources.Push(new EffectSource(player, item));
                return true;
            }

            internal static bool HasSource()
            {
                return EffectSources.Count > 0;
            }

            internal static void PopSource(bool shouldPop)
            {
                if (shouldPop && EffectSources.Count > 0)
                {
                    EffectSources.Pop();
                }
            }

            internal static void TryApplyDurationIncrease(SEMan seMan, StatusEffect? statusEffect)
            {
                if (seMan == null || statusEffect == null || statusEffect.m_ttl <= 0f)
                {
                    return;
                }

                float durationIncrease = GetDurationIncrease(seMan.m_character as Player, statusEffect);
                if (durationIncrease <= 0f)
                {
                    return;
                }

                float baseTtl = statusEffect.m_ttl;
                if (DurationMarkers.TryGetValue(statusEffect, out DurationMarker marker))
                {
                    baseTtl = Mathf.Abs(statusEffect.m_ttl - marker.ModifiedTtl) <= ModifiedTtlTolerance ? marker.BaseTtl : statusEffect.m_ttl;
                }
                else
                {
                    marker = new DurationMarker();
                    DurationMarkers.Add(statusEffect, marker);
                }

                marker.BaseTtl = baseTtl;
                marker.ModifiedTtl = baseTtl * (1f + durationIncrease);
                statusEffect.m_ttl = marker.ModifiedTtl;
            }

            private static float GetDurationIncrease(Player? targetPlayer, StatusEffect statusEffect)
            {
                if (EffectSources.Count > 0)
                {
                    EffectSource source = EffectSources.Peek();
                    return GetWeaponEffectValue(source.Player, source.Item, IncreaseEffectDuration, PercentScale);
                }

                if (targetPlayer != null && IsSupportedStatusEffect(statusEffect))
                {
                    return GetPlayerEffectValue(targetPlayer, IncreaseEffectDuration, PercentScale);
                }

                return 0f;
            }

            private static bool IsSupportedStatusEffect(StatusEffect statusEffect)
            {
                return statusEffect is SE_Shield || SupportedStatusEffectNames.Contains(GetStatusEffectName(statusEffect));
            }

            private static string GetStatusEffectName(Object statusEffect)
            {
                return statusEffect == null ? string.Empty : statusEffect.name.Replace("(Clone)", string.Empty).Trim();
            }
        }

        [HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
        private static class IncreaseEffectDuration_Projectile_OnHit_Patch
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Projectile __instance, out bool __state)
            {
                Player? player = __instance?.m_owner as Player;
                ItemDrop.ItemData? item = __instance?.m_weapon;
                __state = player != null && item != null && IncreaseEffectDurationRuntime.PushSource(player, item);
            }

            private static void Postfix(bool __state)
            {
                IncreaseEffectDurationRuntime.PopSource(__state);
            }
        }

        [HarmonyPatch(typeof(Aoe), "OnHit")]
        private static class IncreaseEffectDuration_Aoe_OnHit_Patch
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Aoe __instance, out bool __state)
            {
                Player? player = __instance?.m_owner as Player;
                ItemDrop.ItemData? item = __instance?.m_itemData;
                __state = player != null && item != null && IncreaseEffectDurationRuntime.PushSource(player, item);
            }

            private static void Postfix(bool __state)
            {
                IncreaseEffectDurationRuntime.PopSource(__state);
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
        private static class IncreaseEffectDuration_Character_ApplyDamage_Patch
        {
            private static void Prefix(HitData hit, out bool __state)
            {
                __state = false;
                if (IncreaseEffectDurationRuntime.HasSource() ||
                    hit == null ||
                    hit.m_statusEffectHash == 0 ||
                    hit.GetAttacker() is not Player player)
                {
                    return;
                }

                __state = IncreaseEffectDurationRuntime.PushSource(player, player.GetCurrentWeapon());
            }

            private static void Postfix(Character __instance, HitData hit, bool __state)
            {
                SEMan? seMan = __instance?.m_seman;
                if (seMan != null && hit != null && hit.m_statusEffectHash != 0)
                {
                    IncreaseEffectDurationRuntime.TryApplyDurationIncrease(
                        seMan,
                        seMan.GetStatusEffect(hit.m_statusEffectHash));
                }

                IncreaseEffectDurationRuntime.PopSource(__state);
            }
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.AddStatusEffect), typeof(StatusEffect), typeof(bool), typeof(int), typeof(float))]
        private static class IncreaseEffectDuration_SEMan_AddStatusEffect_StatusEffect_Patch
        {
            private static void Postfix(SEMan __instance, StatusEffect statusEffect, bool resetTime, StatusEffect __result)
            {
                StatusEffect activeStatusEffect = __result;
                if (activeStatusEffect == null && resetTime && statusEffect != null)
                {
                    activeStatusEffect = __instance.GetStatusEffect(statusEffect.NameHash());
                }

                IncreaseEffectDurationRuntime.TryApplyDurationIncrease(__instance, activeStatusEffect);
            }
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.AddStatusEffect), typeof(int), typeof(bool), typeof(int), typeof(float))]
        private static class IncreaseEffectDuration_SEMan_AddStatusEffect_Hash_Patch
        {
            private static void Postfix(SEMan __instance, int nameHash, bool resetTime, StatusEffect __result)
            {
                StatusEffect activeStatusEffect = __result;
                if (activeStatusEffect == null && resetTime)
                {
                    activeStatusEffect = __instance.GetStatusEffect(nameHash);
                }

                IncreaseEffectDurationRuntime.TryApplyDurationIncrease(__instance, activeStatusEffect);
            }
        }
    }
}
