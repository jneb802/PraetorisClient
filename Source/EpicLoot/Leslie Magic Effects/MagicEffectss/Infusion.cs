using EpicLootAPI;
using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;

namespace EpicLootLeslieAlphaTest.src.MagicEffectss
{
    public static class Infusion
    {
        //private static float LightningLastHit = 0f;
        private static bool isInfused = false;
        public enum InfusionElement
        {
            Fire,
            Frost,
            Lightning,
            Poison,
            Wind,
            Blood
        }

        static readonly List<(string keyword, InfusionElement element)> elementMap = new()
        {
            ("fire", InfusionElement.Fire),
            ("burn", InfusionElement.Fire),
            ("frost", InfusionElement.Frost),
            ("freez", InfusionElement.Frost),
            ("lightning", InfusionElement.Lightning),
            ("shock", InfusionElement.Lightning),
            ("poison", InfusionElement.Poison),
            ("wind", InfusionElement.Wind),
            ("aero", InfusionElement.Wind),
            ("moder", InfusionElement.Wind),
            ("leaf", InfusionElement.Wind),
            ("petal", InfusionElement.Wind),
            ("blood", InfusionElement.Blood)
        };

        static readonly Dictionary<string, System.Func<StatusEffect, float>> efficacyType = new()
        {
            { "Burning", se => ((SE_Burning)se).m_fireDamageLeft },

            { "Frost", se => ((SE_Frost)se).m_ttl * 10f},

            { "Lightning", se => se.m_ttl > 0 && Player.m_localPlayer.m_lastHit != null ? Player.m_localPlayer.m_lastHit.m_damage.m_lightning : 0f},

            { "Poison", se => ((SE_Poison)se).m_damageLeft }
        };

        static float GetEfficacy(StatusEffect se)
        {
            Jotunn.Logger.LogWarning($"GetEfficacy: {se.name} found {efficacyType.ContainsKey(se.name)}");
            if (efficacyType.TryGetValue(se.name, out var efficacyAddition))
            {
                //Jotunn.Logger.LogWarning($"{efficacyAddition(se)}"); 
                return efficacyAddition(se);
            }
            return 0f;
        }

        public static float Efficacy;

        public class InfusionAbilityProxy : Proxy
        {
            public static IEnumerator RechargeAnimation(Player player, float duration)
            {
                player.m_zanim.SetBool("recharge_lightningstaff", true);
                yield return new WaitForSeconds(duration);
                player.m_zanim.SetBool("recharge_lightningstaff", false);
                player.m_zanim.SetTrigger("recharge_lightningstaff_done");
            }

            public override void Activate()
            {
                if (IsOnCooldown() || Player.m_localPlayer == null) return;
                SetCooldownEndTime(GetTime() + Cooldown); // where define cooldown


                List<StatusEffect> playerStatusEffects = new List<StatusEffect>(Player.m_localPlayer.GetSEMan().GetStatusEffects());
                isInfused = false;
                Dictionary<InfusionElement, float> bestEfficacy = new();
                List<StatusEffect> toRemove = new();

                for (int i = playerStatusEffects.Count - 1; i >= 0; --i)
                {
                    string seName = playerStatusEffects[i].name.ToLower();
                    foreach (var (keyword, element) in elementMap)
                    {
                        if (seName.Contains(keyword))
                        {
                            float eff = GetEfficacy(playerStatusEffects[i]);
                            toRemove.Add(playerStatusEffects[i]);
                            if (!bestEfficacy.ContainsKey(element) || eff > bestEfficacy[element])
                                bestEfficacy[element] = eff;
                            break;
                        }
                    }
                }

                Player.m_localPlayer.StartCoroutine(DeferRemoveAndApply(toRemove, bestEfficacy));

                static IEnumerator DeferRemoveAndApply(List<StatusEffect> toRemove, Dictionary<InfusionElement, float> bestEfficacy)
                {
                    foreach (var se in toRemove) Player.m_localPlayer.GetSEMan().RemoveStatusEffect(se);
                    yield return null;

                    foreach (KeyValuePair<InfusionElement, float> kvp in bestEfficacy)
                    {
                        Infusion.Efficacy = kvp.Value;
                        Player.m_localPlayer.GetSEMan().AddStatusEffect($"SE_{kvp.Key}Infused".GetStableHashCode());
                        isInfused = true;
                    }

                    if (isInfused)
                    {
                        Player.m_localPlayer.StartCoroutine(RechargeAnimation(Player.m_localPlayer, 1f));
                    }
                    if (!isInfused)
                    {
                        Player.m_localPlayer.m_zanim.SetTrigger("emote_shrug");
                    }
                }
            }
        }


    }
}
