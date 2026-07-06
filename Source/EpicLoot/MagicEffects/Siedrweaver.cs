using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace PraetorisClient
{
    internal static partial class PraetorisMagicEffects
    {
        private const string SiedrweaverDefinitionJson = @"{
  ""Type"": ""Siedrweaver"",
  ""DisplayText"": ""Siedrweaver"",
  ""Description"": ""Siedrweaver [Activated]: Consumes eitr to heal yourself and nearby allies over time."",
  ""Ability"": ""Siedrweaver"",
  ""Requirements"": {
    ""NoRoll"": true
  }
}";

        private const string SeidrweaverStatusEffect = "SE_Praetoris_Seidrweaver";
        private const float SiedrweaverEitrCost = 30f;
        private const float SiedrweaverHeal = 50f;
        private const float SiedrweaverRange = 10f;

        private static void RegisterSiedrweaverProxyAbility()
        {
            RegisterProxyAbility(
                @"{
  ""ID"": ""Siedrweaver"",
  ""IconAsset"": ""UndyingIcon"",
  ""ActivationMode"": ""Activated"",
  ""Cooldown"": 600,
  ""Action"": ""Custom""
}",
                SiedrweaverAbilityRuntime.CreateCallbacks());
        }

        private sealed class PraetorisSeidrweaverStatusEffect : SE_Stats
        {
        }

        private static class SiedrweaverAbilityRuntime
        {
            private static Player? _player;
            private static float _cooldown;

            internal static Dictionary<string, Delegate> CreateCallbacks()
            {
                return new Dictionary<string, Delegate>
                {
                    ["Initialize"] = new Action<Player, string, float>(Initialize),
                    ["CanActivate"] = new Func<bool>(CanActivate),
                    ["TryActivate"] = new Action(TryActivate),
                    ["IsOnCooldown"] = new Func<bool>(IsOnCooldown),
                    ["TimeUntilCooldownEnds"] = new Func<float>(TimeUntilCooldownEnds),
                    ["PercentCooldownComplete"] = new Func<float>(PercentCooldownComplete),
                    ["GetCooldownEndTime"] = new Func<float>(GetCooldownEndTime),
                    ["SetCooldownEndTime"] = new Action<float>(SetCooldownEndTime),
                    ["OnRemoved"] = new Action(OnRemoved)
                };
            }

            private static void Initialize(Player player, string abilityId, float cooldown)
            {
                _player = player;
                _cooldown = cooldown;
            }

            private static bool CanActivate()
            {
                return _player != null && !IsOnCooldown() && _player.HaveEitr(SiedrweaverEitrCost);
            }

            private static void TryActivate()
            {
                if (_player == null || IsOnCooldown())
                {
                    return;
                }

                if (!_player.HaveEitr(SiedrweaverEitrCost))
                {
                    Hud.instance?.EitrBarEmptyFlash();
                    return;
                }

                SetCooldownEndTime(GetNetworkTime() + _cooldown);
                AddStatusToPlayersInRange(_player, SeidrweaverStatusEffect, SiedrweaverHeal, SiedrweaverRange);
                _player.m_skillLevelupEffects.Create(_player.GetHeadPoint(), Quaternion.identity);
                _player.UseEitr(SiedrweaverEitrCost);
            }

            private static bool IsOnCooldown()
            {
                return _player != null && PraetorisMagicEffects.IsOnCooldown(_player, Siedrweaver);
            }

            private static float TimeUntilCooldownEnds()
            {
                return _player == null ? 0f : Mathf.Max(0f, GetCooldownEndTime() - GetNetworkTime());
            }

            private static float PercentCooldownComplete()
            {
                if (_cooldown <= 0f || !IsOnCooldown())
                {
                    return 1f;
                }

                return 1f - TimeUntilCooldownEnds() / _cooldown;
            }

            private static float GetCooldownEndTime()
            {
                return _player == null ? 0f : GetCooldownEnd(_player, Siedrweaver);
            }

            private static void SetCooldownEndTime(float cooldownEndTime)
            {
                if (_player != null)
                {
                    SetCooldownEnd(_player, Siedrweaver, cooldownEndTime);
                }
            }

            private static void OnRemoved()
            {
                _player = null;
                _cooldown = 0f;
            }
        }

        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
        private static class ObjectDB_Awake_Patch
        {
            private static void Postfix(ObjectDB __instance)
            {
                RegisterStatusEffects(__instance);
            }
        }

        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
        private static class ObjectDB_CopyOtherDB_Patch
        {
            private static void Postfix(ObjectDB __instance)
            {
                RegisterStatusEffects(__instance);
            }
        }

        private static void RegisterStatusEffects(ObjectDB objectDB)
        {
            if (objectDB == null)
            {
                return;
            }

            StatusEffect seidrweaver = CreateSeidrweaverStatusEffect();
            if (objectDB.GetStatusEffect(seidrweaver.NameHash()) == null)
            {
                objectDB.m_StatusEffects.Add(seidrweaver);
            }
        }

        private static StatusEffect CreateSeidrweaverStatusEffect()
        {
            PraetorisSeidrweaverStatusEffect statusEffect = ScriptableObject.CreateInstance<PraetorisSeidrweaverStatusEffect>();
            statusEffect.name = SeidrweaverStatusEffect;
            statusEffect.m_name = "Siedrweaver";
            statusEffect.m_tooltip = "Restores health over time.";
            statusEffect.m_ttl = 12f;
            statusEffect.m_healthOverTime = SiedrweaverHeal;
            statusEffect.m_healthOverTimeDuration = 12f;
            statusEffect.m_healthOverTimeInterval = 1f;
            return statusEffect;
        }
    }
}
