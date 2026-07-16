using EpicLoot;
using EpicLootAPI;
using EpicLootLeslieAlphaTest.src.StatusEffects;
using HarmonyLib;
using Jotunn;
using System.Diagnostics;

namespace EpicLootLeslieAlphaTest.src.MagicEffectss
{
    public static class Onslaught
    {
        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        public class Attack_Trigger_Patch
        {
            public static void Prefix(Character __instance, HitData hit)
            {
                Humanoid attacker = hit.GetAttacker() as Humanoid;

                if (attacker == null || Player.m_localPlayer != attacker || !Player.m_localPlayer.GetCurrentWeapon().HasMagicEffect("Onslaught")) return;

                int chain = attacker.m_currentAttack?.m_currentAttackCainLevel ?? -1;
                if (chain >= 2 || __instance.IsStaggering())
                {
                    SEMan seman = Player.m_localPlayer.GetSEMan();
                    var existing = seman.GetStatusEffect(SE_Onslaught.EffectName.GetStableHashCode()) as SE_Onslaught;
                    if (existing != null)
                    {
                        existing.AddStack();
                    }
                    else
                    {
                        var se = seman.AddStatusEffect(SE_Onslaught.EffectName.GetStableHashCode()) as SE_Onslaught;
                        se.AddStack();
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Attack), nameof(Attack.Start))]
        public class Attack_Modify_Chain_Patch
        {
            public static void Postfix(Attack __instance, Humanoid character, ref bool __result)
            {
                if (character != Player.m_localPlayer) return;
                var se = character.GetSEMan().GetStatusEffect(SE_Onslaught.EffectName.GetStableHashCode()) as SE_Onslaught;
                if (se == null || se.m_stacks <= 0 || !character.GetCurrentWeapon().HasMagicEffect("Onslaught")) return;

                //Jotunn.Logger.LogWarning($"Attack pactch launched attack anim = {__instance.m_attackAnimation + "2"}");
                //character.m_animator.ResetTrigger(__instance.m_attackAnimation + __instance.m_currentAttackCainLevel);
                __instance.m_currentAttackCainLevel = 2; // check if higher and set there for double axe
                __instance.m_zanim.SetTrigger(__instance.m_attackAnimation + "2");
                se.ConsumeStack();
            }
        }
    }
}
