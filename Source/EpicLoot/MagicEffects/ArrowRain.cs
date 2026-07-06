using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace PraetorisClient
{
    internal static partial class PraetorisMagicEffects
    {
        private const string ArrowRainDefinitionJson = @"{
  ""Type"": ""ArrowRain"",
  ""DisplayText"": ""Hrafnstorm [Triggered]: Your arrow calls down a volley of spectral arrows."",
  ""Description"": ""On impact, your arrow summons a brief storm of arrows around the target."",
  ""Ability"": ""ArrowRain"",
  ""Requirements"": {
    ""AllowedItemTypes"": [ ""Bow"" ],
    ""AllowedSkillTypes"": [ ""Bows"" ]
  }
}";

        private static void RegisterArrowRainProxyAbility()
        {
            RegisterProxyAbility(
                @"{
  ""ID"": ""ArrowRain"",
  ""IconAsset"": ""BerserkerIcon"",
  ""ActivationMode"": ""Triggerable"",
  ""Cooldown"": 10,
  ""Action"": ""Custom""
}",
                ArrowRainAbilityRuntime.CreateCallbacks());
        }

        private sealed class ArrowRainProjectileHook : MonoBehaviour
        {
        }

        private static class ArrowRainAbilityRuntime
        {
            private static Player? _player;
            private static float _cooldown;

            internal static Dictionary<string, Delegate> CreateCallbacks()
            {
                return new Dictionary<string, Delegate>
                {
                    ["Initialize"] = new Action<Player, string, float>(Initialize),
                    ["ShouldTrigger"] = new Func<bool>(() => false),
                    ["IsOnCooldown"] = new Func<bool>(IsOnCooldown),
                    ["TimeUntilCooldownEnds"] = new Func<float>(TimeUntilCooldownEnds),
                    ["PercentCooldownComplete"] = new Func<float>(PercentCooldownComplete),
                    ["GetCooldownEndTime"] = new Func<float>(GetCooldownEndTime),
                    ["SetCooldownEndTime"] = new Action<float>(SetCooldownEndTime),
                    ["OnRemoved"] = new Action(OnRemoved)
                };
            }

            internal static void TryTrigger(Projectile source, Collider collider, Vector3 hitPoint, bool water)
            {
                if (_player == null ||
                    IsOnCooldown() ||
                    source == null ||
                    source.m_owner != _player ||
                    source.m_originalHitData == null ||
                    source.m_type != ProjectileType.Arrow ||
                    ZNetScene.instance.GetPrefab(source.name.Replace("(Clone)", string.Empty)) is not { } projectilePrefab)
                {
                    return;
                }

                HitData hitData = new HitData
                {
                    m_damage = source.m_originalHitData.m_damage,
                    m_pushForce = source.m_originalHitData.m_pushForce,
                    m_backstabBonus = source.m_originalHitData.m_backstabBonus,
                    m_ranged = true,
                    m_hitType = HitData.HitType.PlayerHit
                };
                hitData.SetAttacker(_player);
                hitData.ApplyModifier(0.5f);

                SpawnArrowRain(projectilePrefab, hitPoint, hitData);
                SetCooldownEndTime(GetNetworkTime() + _cooldown);
            }

            private static void Initialize(Player player, string abilityId, float cooldown)
            {
                _player = player;
                _cooldown = cooldown;
            }

            private static bool IsOnCooldown()
            {
                return _player != null && PraetorisMagicEffects.IsOnCooldown(_player, ArrowRain);
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
                return _player == null ? 0f : GetCooldownEnd(_player, ArrowRain);
            }

            private static void SetCooldownEndTime(float cooldownEndTime)
            {
                if (_player != null)
                {
                    SetCooldownEnd(_player, ArrowRain, cooldownEndTime);
                }
            }

            private static void OnRemoved()
            {
                _player = null;
                _cooldown = 0f;
            }

            private static void SpawnArrowRain(GameObject projectilePrefab, Vector3 position, HitData hitData)
            {
                for (int i = 0; i < 10; i++)
                {
                    float radius = Random.Range(3f, 10f);
                    Vector2 offset = Random.insideUnitCircle * radius;
                    Vector3 spawnPosition = position + new Vector3(offset.x, Random.Range(35f, 55f), offset.y);
                    GameObject projectileObject = Object.Instantiate(projectilePrefab, spawnPosition, Quaternion.identity);
                    if (!projectileObject.TryGetComponent(out Projectile projectile))
                    {
                        continue;
                    }

                    Vector3 velocity = (position - spawnPosition).normalized * Random.Range(25f, 35f);
                    projectile.Setup(null, velocity, 10f, hitData, null, null);
                }
            }
        }

        [HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
        private static class ArrowRain_Projectile_OnHit_Patch
        {
            private static void Prefix(Projectile __instance)
            {
                if (__instance == null ||
                    __instance.m_owner is not Player player ||
                    player != Player.m_localPlayer ||
                    __instance.m_type != ProjectileType.Arrow ||
                    __instance.GetComponent<ArrowRainProjectileHook>() != null)
                {
                    return;
                }

                __instance.gameObject.AddComponent<ArrowRainProjectileHook>();
                __instance.m_onHit += (collider, point, water) => ArrowRainAbilityRuntime.TryTrigger(__instance, collider, point, water);
            }
        }
    }
}
