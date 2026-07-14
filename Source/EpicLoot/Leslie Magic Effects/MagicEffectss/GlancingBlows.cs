using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;
using EpicLootAPI;

namespace EpicLootLeslieAlphaTest.src.MagicEffectss
{
    
    public static class GlancingBlows
    {
        private static float effectValue = 1f;
        private static float initialHitValue = 0f;

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.BlockAttack))]

        public class Block_Attack_GlancingBlowsBlockAttackPrefix_Patch
        {
            public static void Prefix(HitData hit)
            {
                initialHitValue = hit.GetTotalBlockableDamage();
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.BlockAttack))]

        public class Block_Attack_GlancingBlowsBlockAttackPostfix_Patch
        {
            public static void Postfix(HitData hit, Character attacker)
            {
                if (!Player.m_localPlayer) return;


                if (!Player.m_localPlayer.HasActiveMagicEffect("GlancingBlows", out float _, 1f)) return;

                float glanceDmg = initialHitValue * .5f; // make that .float a config maybe. nah
                float hp = Player.m_localPlayer.GetHealth();
                float mitigatedGlanceDmg = (float)Math.Round(glanceDmg - Player.m_localPlayer.GetBodyArmor(), 2);
                if (!Player.m_localPlayer.IsDead() && mitigatedGlanceDmg > 0f)
                {
                    Player.m_localPlayer.SetHealth(hp - mitigatedGlanceDmg);
                    DamageText.instance.ShowText(DamageText.TextType.Normal, Player.m_localPlayer.transform.position, mitigatedGlanceDmg.ToString(), true);
                }
            }
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetBlockPower), typeof(int), typeof(float))]

        public class GlancingBlowsDamage_Patch
        {
            public static void Postfix(float skillFactor, ref float __result)
            { 
                if (!Player.m_localPlayer) return;

                if (!Player.m_localPlayer.HasActiveMagicEffect("GlancingBlows", out float _, 1f)) return;
                __result *= 1.5f;
            }
        }

    }
}
