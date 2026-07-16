using EpicLootAPI;
//using EpicLoot;
using EpicLootLeslieAlphaTest.src.StatusEffects;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static Attack;

namespace EpicLootLeslieAlphaTest.src.MagicEffectss
{
    public static class Retaliation
    {
        public static string AttackType;

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.BlockAttack))]

        public class Block_Attack_Retaliate_Patch
        {
            public static void Postfix(Humanoid __instance, bool __result)
            {
                if (!Player.m_localPlayer.HasActiveMagicEffect("Retaliation", out float _, 1f) ||
                    __result == false ||
                    __instance != Player.m_localPlayer) return;

                SEMan seman = __instance.GetSEMan();
                var existing = seman.GetStatusEffect(SE_Retaliation.EffectName.GetStableHashCode()) as SE_Retaliation;
                if (existing != null)
                {
                    existing.AddStack();
                }
                else
                {
                    var se = seman.AddStatusEffect(SE_Retaliation.EffectName.GetStableHashCode()) as SE_Retaliation;
                    se.AddStack();
                }
            }
        }

        [HarmonyPatch(typeof(Attack), nameof(Attack.OnAttackTrigger))]
        public class Humanoid_Attack_Patch
        {
            static void Prefix(Attack __instance)
            {
                if (__instance.m_character != Player.m_localPlayer || !Player.m_localPlayer.HasActiveMagicEffect("Retaliation", out float _, 1f)) return;
                AttackType = __instance.m_attackAnimation;
                //var se = Player.m_localPlayer.GetSEMan().GetStatusEffect(SE_Retaliation.EffectName.GetStableHashCode()) as SE_Retaliation;
                //if (!__instance.m_character.InAttack() && se.m_time >= .3f) se.ConsumeStack();
            }
        }
    }
}
