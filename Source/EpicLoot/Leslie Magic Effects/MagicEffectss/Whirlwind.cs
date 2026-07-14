using EpicLootAPI;
using HarmonyLib;
using Jotunn;
using UnityEngine;
using EpicLoot;

namespace EpicLootLeslieAlphaTest.src.MagicEffectss
{
    public static class Whirlwind
    {
        private static bool InWhirlwind = false;
        private static bool WhirlwindDamage = false;
        private static float overFlowAnimSpeed = 0f;
        
        

        [HarmonyPatch(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.CustomFixedUpdate))]
        [HarmonyPriority(Priority.Last)]
        public static class Whirlwind_CharAnim_CFU_Patch
        {
            public static void Postfix(CharacterAnimEvent __instance)
            {
                if (__instance.m_character == null || Player.m_localPlayer == null || __instance.m_character != Player.m_localPlayer) return;

                InWhirlwind = (Player.m_localPlayer.GetCurrentWeapon().HasMagicEffect("Whirlwind")) && __instance.m_character != null && __instance.m_character.InAttack() && __instance.m_character.m_secondaryAttackHold == true && ((Humanoid)__instance.m_character).m_currentAttack.m_attackAnimation == "atgeir_secondary";

                if (InWhirlwind)
                {
                    if (__instance.m_animator.speed > 1f && __instance.m_animator.speed != 10f) { overFlowAnimSpeed = Mathf.Clamp(__instance.m_animator.speed -2f, 0f, .4f); } // atgeir_secondary speed is 2f by default
                    __instance.m_animator.speed = 10f;
                    WhirlwindDamage = true;
                    __instance.m_character.m_attack = false;
                    __instance.m_character.m_attackHold = false;
                }
                else if (!__instance.m_character.InAttack())
                {
                    WhirlwindDamage = false;
                    overFlowAnimSpeed = 0f;
                }
            }
        }
        
        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        public static class Whirlwind_Damage_Patch
        {
            public static void Prefix(Character __instance, HitData hit)
            {
                if (hit.GetAttacker() != Player.m_localPlayer) return;
                if (InWhirlwind)
                {
                    hit.m_damage.Modify(.4f * (1f + overFlowAnimSpeed));
                    hit.m_staggerMultiplier = 1f;
                }
            }
        }

        [HarmonyPatch(typeof(Attack), nameof(Attack.GetAttackStamina))]
        public static class Whirlwind_Stamina_Patch
        {
            public static void Postfix(ref float __result)
            {
                if (InWhirlwind) __result *= .3f;
            }
        }

    }
}
