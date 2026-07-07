using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace PraetorisClient
{
    internal static class PraetorisMagicEffects
    {
        internal const string IncreaseEffectDuration = "IncreaseEffectDuration";
        internal const string ModifyTrinketDuration = "ModifyTrinketDuration";
        internal const string IncreaseAdrenalineGained = "IncreaseAdrenalineGained";
        internal const string ModifyAdrenaline = "ModifyAdrenaline";
        internal const string DecreaseAdrenalineRequired = "DecreaseAdrenalineRequired";
        internal const string ModifyAdrenalineCost = "ModifyAdrenalineCost";
        internal const string PiercingShot = "PiercingShot";
        internal const string ReloadOnKill = "ReloadOnKill";
        internal const string ArrowRain = "ArrowRain";
        internal const string Siedrweaver = "Siedrweaver";
        internal const string Sturdy = "Sturdy";
        internal const string StaminaLeech = "StaminaLeech";

        private const string ItemConsumesAdrenalineRequirement = "Praetoris.ItemConsumesAdrenaline";
        private const string ItemUsesAdrenalineOnAttackRequirement = "Praetoris.ItemUsesAdrenalineOnAttack";
        private const string SeidrweaverStatusEffect = "SE_Praetoris_Seidrweaver";
        private const float PercentScale = 0.01f;
        private const float SiedrweaverEitrCost = 30f;
        private const float SiedrweaverHeal = 50f;
        private const float SiedrweaverRange = 10f;

        private static readonly string[] MagicEffectDefinitionJson =
        {
            @"{
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
}",
            @"{
  ""Type"": ""ModifyTrinketDuration"",
  ""DisplayText"": ""Trinket Duration +{0:0.#}%"",
  ""Description"": ""Increases the duration of this trinket's full adrenaline status effect by +<b><color=yellow>X</color></b>%."",
  ""Requirements"": {
    ""AllowedItemTypes"": [ ""Trinket"" ],
    ""ExternalRequirements"": [ ""Praetoris.ItemConsumesAdrenaline"" ]
  },
  ""ValuesPerRarity"": {
    ""Magic"": { ""MinValue"": 1, ""MaxValue"": 5, ""Increment"": 1 },
    ""Rare"": { ""MinValue"": 5, ""MaxValue"": 10, ""Increment"": 1 },
    ""Epic"": { ""MinValue"": 10, ""MaxValue"": 20, ""Increment"": 1 },
    ""Legendary"": { ""MinValue"": 20, ""MaxValue"": 30, ""Increment"": 1 },
    ""Mythic"": { ""MinValue"": 30, ""MaxValue"": 40, ""Increment"": 1 }
  },
  ""SelectionWeight"": 1,
  ""Prefixes"": [ ""Enduring"" ],
  ""Suffixes"": [ ""Endurance"" ]
}",
            @"{
  ""Type"": ""IncreaseAdrenalineGained"",
  ""DisplayText"": ""Adrenaline Gained +{0:0.#}%"",
  ""Description"": ""Increase adrenaline gained by this weapon by <b><color=yellow>X</color></b>%."",
  ""Requirements"": {
    ""ExternalRequirements"": [ ""Praetoris.ItemUsesAdrenalineOnAttack"" ]
  },
  ""ValuesPerRarity"": {
    ""Magic"": { ""MinValue"": 5, ""MaxValue"": 10, ""Increment"": 1 },
    ""Rare"": { ""MinValue"": 10, ""MaxValue"": 15, ""Increment"": 1 },
    ""Epic"": { ""MinValue"": 15, ""MaxValue"": 25, ""Increment"": 1 },
    ""Legendary"": { ""MinValue"": 25, ""MaxValue"": 40, ""Increment"": 1 },
    ""Mythic"": { ""MinValue"": 40, ""MaxValue"": 60, ""Increment"": 1 }
  },
  ""SelectionWeight"": 1,
  ""Prefixes"": [ ""Eager"" ],
  ""Suffixes"": [ ""Adrenaline"" ]
}",
            @"{
  ""Type"": ""ModifyAdrenaline"",
  ""DisplayText"": ""Adrenaline +{0:0.#}%"",
  ""Description"": ""Increase adrenaline gained by <b><color=yellow>X</color></b>%."",
  ""Requirements"": {
    ""AllowedItemTypes"": [ ""Trinket"" ],
    ""AllowedRarities"": [ ""Magic"", ""Rare"", ""Epic"", ""Legendary"", ""Mythic"" ]
  },
  ""ValuesPerRarity"": {
    ""Magic"": { ""MinValue"": 1, ""MaxValue"": 5, ""Increment"": 1 },
    ""Rare"": { ""MinValue"": 5, ""MaxValue"": 10, ""Increment"": 1 },
    ""Epic"": { ""MinValue"": 10, ""MaxValue"": 20, ""Increment"": 1 },
    ""Legendary"": { ""MinValue"": 20, ""MaxValue"": 30, ""Increment"": 1 },
    ""Mythic"": { ""MinValue"": 30, ""MaxValue"": 40, ""Increment"": 1 }
  },
  ""SelectionWeight"": 1,
  ""Prefixes"": [ ""Eager"" ],
  ""Suffixes"": [ ""Adrenaline"" ]
}",
            @"{
  ""Type"": ""DecreaseAdrenalineRequired"",
  ""DisplayText"": ""Adrenaline Required -{0:0.#}%"",
  ""Description"": ""Reduce required adrenaline by <b><color=yellow>X</color></b>%."",
  ""Requirements"": {
    ""ExternalRequirements"": [ ""Praetoris.ItemConsumesAdrenaline"" ]
  },
  ""ValuesPerRarity"": {
    ""Magic"": { ""MinValue"": 5, ""MaxValue"": 10, ""Increment"": 1 },
    ""Rare"": { ""MinValue"": 10, ""MaxValue"": 15, ""Increment"": 1 },
    ""Epic"": { ""MinValue"": 15, ""MaxValue"": 25, ""Increment"": 1 },
    ""Legendary"": { ""MinValue"": 25, ""MaxValue"": 35, ""Increment"": 1 },
    ""Mythic"": { ""MinValue"": 35, ""MaxValue"": 50, ""Increment"": 1 }
  },
  ""SelectionWeight"": 1,
  ""Prefixes"": [ ""Efficient"" ],
  ""Suffixes"": [ ""Efficiency"" ]
}",
            @"{
  ""Type"": ""ModifyAdrenalineCost"",
  ""DisplayText"": ""Adrenaline Required -{0:0.#}%"",
  ""Description"": ""Reduce required adrenaline by <b><color=yellow>X</color></b>%."",
  ""Requirements"": {
    ""AllowedItemTypes"": [ ""Trinket"" ],
    ""AllowedRarities"": [ ""Magic"", ""Rare"", ""Epic"", ""Legendary"", ""Mythic"" ]
  },
  ""ValuesPerRarity"": {
    ""Magic"": { ""MinValue"": 1, ""MaxValue"": 5, ""Increment"": 1 },
    ""Rare"": { ""MinValue"": 5, ""MaxValue"": 10, ""Increment"": 1 },
    ""Epic"": { ""MinValue"": 10, ""MaxValue"": 20, ""Increment"": 1 },
    ""Legendary"": { ""MinValue"": 20, ""MaxValue"": 30, ""Increment"": 1 },
    ""Mythic"": { ""MinValue"": 30, ""MaxValue"": 40, ""Increment"": 1 }
  },
  ""SelectionWeight"": 1,
  ""Prefixes"": [ ""Efficient"" ],
  ""Suffixes"": [ ""Efficiency"" ]
}",
            @"{
  ""Type"": ""PiercingShot"",
  ""DisplayText"": ""Piercing Shot +{0:0}"",
  ""Description"": ""Projectiles from this weapon pierce through up to <b><color=yellow>X</color></b> enemies before stopping."",
  ""Requirements"": {
    ""AllowedItemTypes"": [ ""Bow"", ""Crossbows"" ],
    ""AllowedSkillTypes"": [ ""Bows"", ""Crossbows"" ]
  },
  ""ValuesPerRarity"": {
    ""Magic"": { ""MinValue"": 1, ""MaxValue"": 1, ""Increment"": 1 },
    ""Rare"": { ""MinValue"": 1, ""MaxValue"": 2, ""Increment"": 1 },
    ""Epic"": { ""MinValue"": 2, ""MaxValue"": 2, ""Increment"": 1 },
    ""Legendary"": { ""MinValue"": 2, ""MaxValue"": 3, ""Increment"": 1 },
    ""Mythic"": { ""MinValue"": 3, ""MaxValue"": 3, ""Increment"": 1 }
  },
  ""SelectionWeight"": 4,
  ""Prefixes"": [ ""Piercing"" ],
  ""Suffixes"": [ ""Piercing"" ]
}",
            @"{
  ""Type"": ""ReloadOnKill"",
  ""DisplayText"": ""Reload on Kill [Passive]: Killing an enemy with a crossbow instantly reloads your current crossbow."",
  ""Requirements"": {
    ""NoRoll"": true
  }
}",
            @"{
  ""Type"": ""ArrowRain"",
  ""DisplayText"": ""Hrafnstorm [Triggered]: Your arrow calls down a volley of spectral arrows."",
  ""Description"": ""On impact, your arrow summons a brief storm of arrows around the target."",
  ""Ability"": ""ArrowRain"",
  ""Requirements"": {
    ""NoRoll"": true
  }
}",
            @"{
  ""Type"": ""Siedrweaver"",
  ""DisplayText"": ""Siedrweaver"",
  ""Description"": ""Siedrweaver [Activated]: Consumes eitr to heal yourself and nearby allies over time."",
  ""Ability"": ""Siedrweaver"",
  ""Requirements"": {
    ""NoRoll"": true
  }
}",
            @"{
  ""Type"": ""Sturdy"",
  ""DisplayText"": ""Sturdy +{0:0.#}%"",
  ""Description"": ""Reduce crossbow pushback force by <b><color=yellow>X</color></b>%."",
  ""Requirements"": {
    ""AllowedSkillTypes"": [ ""Crossbows"" ],
    ""AllowedRarities"": [ ""Epic"", ""Legendary"", ""Mythic"" ]
  },
  ""ValuesPerRarity"": {
    ""Epic"": { ""MinValue"": 10, ""MaxValue"": 20, ""Increment"": 1 },
    ""Legendary"": { ""MinValue"": 20, ""MaxValue"": 30, ""Increment"": 1 },
    ""Mythic"": { ""MinValue"": 30, ""MaxValue"": 50, ""Increment"": 1 }
  },
  ""SelectionWeight"": 1,
  ""Prefixes"": [ ""Sturdy"" ],
  ""Suffixes"": [ ""Sturdiness"" ]
}",
            @"{
  ""Type"": ""StaminaLeech"",
  ""DisplayText"": ""Stamina Leech +{0:0.#}%"",
  ""Description"": ""Recover <b><color=yellow>X</color></b>% of the attack stamina cost when hitting enemies."",
  ""Requirements"": {
    ""AllowedItemTypes"": [ ""TwoHandedWeapon"", ""Shield"" ],
    ""AllowedRarities"": [ ""Magic"", ""Rare"", ""Epic"", ""Legendary"", ""Mythic"" ]
  },
  ""ValuesPerRarity"": {
    ""Magic"": { ""MinValue"": 1, ""MaxValue"": 2, ""Increment"": 0.5 },
    ""Rare"": { ""MinValue"": 1, ""MaxValue"": 3, ""Increment"": 0.5 },
    ""Epic"": { ""MinValue"": 1, ""MaxValue"": 5, ""Increment"": 0.5 },
    ""Legendary"": { ""MinValue"": 1, ""MaxValue"": 10, ""Increment"": 0.5 },
    ""Mythic"": { ""MinValue"": 1, ""MaxValue"": 15, ""Increment"": 0.5 }
  },
  ""SelectionWeight"": 1,
  ""Prefixes"": [ ""Tiring"" ],
  ""Suffixes"": [ ""Stamina Leech"" ]
}"
        };

        internal static void Register()
        {
            if (!EpicLootApiBridge.TryRegisterMagicEffectRequirement(ItemConsumesAdrenalineRequirement, ItemConsumesAdrenaline) ||
                !EpicLootApiBridge.TryRegisterMagicEffectRequirement(ItemUsesAdrenalineOnAttackRequirement, ItemUsesAdrenalineOnAttack))
            {
                return;
            }

            RegisterProxyAbilities();
            foreach (string definitionJson in MagicEffectDefinitionJson)
            {
                if (EpicLootApiBridge.TryAddMagicEffect(definitionJson, out string key))
                {
                    PraetorisClientPlugin.Log.LogInfo("Registered Epic Loot magic effect with key " + key + ".");
                }
            }
        }

        private static void RegisterProxyAbilities()
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

        private static void RegisterProxyAbility(string json, Dictionary<string, Delegate> callbacks)
        {
            if (EpicLootApiBridge.TryRegisterProxyAbility(json, callbacks, out string key))
            {
                PraetorisClientPlugin.Log.LogInfo("Registered Epic Loot proxy ability with key " + key + ".");
            }
        }

        private static bool ItemConsumesAdrenaline(
            ItemDrop.ItemData item,
            object magicItem,
            string magicEffectType,
            bool checkLootRoll,
            bool checkAugmentRoll,
            bool checkRuneRoll)
        {
            return item?.m_shared?.m_fullAdrenalineSE != null;
        }

        private static bool ItemUsesAdrenalineOnAttack(
            ItemDrop.ItemData item,
            object magicItem,
            string magicEffectType,
            bool checkLootRoll,
            bool checkAugmentRoll,
            bool checkRuneRoll)
        {
            return item?.m_shared?.m_attack.m_attackUseAdrenaline > 0f;
        }

        private static bool PlayerHasEffect(Player player, string effectType, out float value, float scale = 1f)
        {
            return EpicLootApiBridge.PlayerHasActiveMagicEffect(player, effectType, out value, scale);
        }

        private static float GetPlayerEffectValue(Player player, string effectType, float scale = 1f)
        {
            return EpicLootApiBridge.GetTotalPlayerActiveMagicEffectValue(player, effectType, scale);
        }

        private static float GetWeaponEffectValue(Player? player, ItemDrop.ItemData weapon, string effectType, float scale = 1f)
        {
            return EpicLootApiBridge.GetTotalActiveMagicEffectValueForWeapon(player, weapon, effectType, scale);
        }

        private static bool WeaponHasEffect(Player player, ItemDrop.ItemData weapon, string effectType, out float value, float scale = 1f)
        {
            value = GetWeaponEffectValue(player, weapon, effectType, scale);
            if (value > 0f)
            {
                return true;
            }

            value = GetWeaponEffectValue(null, weapon, effectType, scale);
            return value > 0f;
        }

        private static void AddStatusToPlayersInRange(Player sourcePlayer, string statusEffectName, float skillLevel, float range)
        {
            if (sourcePlayer == null)
            {
                return;
            }

            int statusHash = statusEffectName.GetStableHashCode();
            Vector3 sourcePoint = sourcePlayer.transform.position;
            Collider[] results = Physics.OverlapSphere(sourcePoint, range, Character.s_characterLayer);
            HashSet<Player> affectedPlayers = new HashSet<Player>();
            foreach (Collider collider in results)
            {
                Player player = collider.GetComponentInParent<Player>();
                if (player == null || affectedPlayers.Contains(player))
                {
                    continue;
                }

                affectedPlayers.Add(player);
                player.GetSEMan().AddStatusEffect(statusHash, true, 0, skillLevel);
            }
        }

        private static float GetNetworkTime()
        {
            return ZNet.instance == null ? Time.time : (float)ZNet.instance.GetTimeSeconds();
        }

        private static float GetCooldownEnd(Player? player, string abilityId)
        {
            ZNetView? netView = player == null ? null : player.GetComponent<ZNetView>();
            return netView?.GetZDO()?.GetFloat("EpicLoot." + abilityId + ".CooldownEnd") ?? 0f;
        }

        private static void SetCooldownEnd(Player? player, string abilityId, float cooldownEnd)
        {
            ZNetView? netView = player == null ? null : player.GetComponent<ZNetView>();
            netView?.GetZDO()?.Set("EpicLoot." + abilityId + ".CooldownEnd", cooldownEnd);
        }

        private static bool IsOnCooldown(Player player, string abilityId)
        {
            return GetNetworkTime() < GetCooldownEnd(player, abilityId);
        }

        private sealed class PraetorisSeidrweaverStatusEffect : SE_Stats
        {
        }

        private sealed class ArrowRainProjectileHook : MonoBehaviour
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

        [HarmonyPatch(typeof(Player), nameof(Player.GetMaxAdrenaline))]
        private static class AdrenalineCost_Player_GetMaxAdrenaline_Patch
        {
            private static void Postfix(Player __instance, ref float __result)
            {
                if (__instance == null || __result <= 0f)
                {
                    return;
                }

                float reduction = 0f;
                foreach (ItemDrop.ItemData item in __instance.GetInventory().GetEquippedItems())
                {
                    if (item?.m_shared?.m_fullAdrenalineSE == null)
                    {
                        continue;
                    }

                    reduction += EpicLootApiBridge.GetTotalActiveMagicEffectValue(null, item, DecreaseAdrenalineRequired, PercentScale);
                }

                reduction += GetPlayerEffectValue(__instance, ModifyAdrenalineCost, PercentScale);
                if (reduction > 0f)
                {
                    __result = Mathf.Max(1f, __result * Mathf.Clamp(1f - reduction, 0.5f, 1f));
                }
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.AddAdrenaline))]
        private static class ModifyAdrenaline_Player_AddAdrenaline_Patch
        {
            private static void Prefix(Player __instance, ref float v)
            {
                if (__instance == null || v <= 0f)
                {
                    return;
                }

                float modifier = GetPlayerEffectValue(__instance, ModifyAdrenaline, PercentScale);
                if (modifier > 0f)
                {
                    v *= 1f + modifier;
                }
            }
        }

        private static class IncreaseAdrenalineGainedRuntime
        {
            private static int _modifiedAttackDepth;

            internal struct ModifiedAttackState
            {
                public bool Modified;
                public float OriginalAttackUseAdrenaline;
            }

            internal static ModifiedAttackState Apply(Attack attack)
            {
                ModifiedAttackState state = new ModifiedAttackState
                {
                    OriginalAttackUseAdrenaline = attack.m_attackUseAdrenaline
                };

                if (attack.m_character is Player player && attack.m_attackUseAdrenaline > 0f)
                {
                    float modifier = GetWeaponEffectValue(player, attack.m_weapon, IncreaseAdrenalineGained, PercentScale);
                    if (modifier > 0f)
                    {
                        attack.m_attackUseAdrenaline *= 1f + modifier;
                        state.Modified = true;
                    }
                }

                return state;
            }

            internal static void Restore(Attack attack, ModifiedAttackState state)
            {
                if (state.Modified)
                {
                    attack.m_attackUseAdrenaline = state.OriginalAttackUseAdrenaline;
                }
            }

            internal static bool ShouldSkipFireProjectileBurstApply()
            {
                return _modifiedAttackDepth > 0;
            }

            internal static void IncrementDepth()
            {
                _modifiedAttackDepth++;
            }

            internal static void DecrementDepth()
            {
                _modifiedAttackDepth--;
            }
        }

        [HarmonyPatch(typeof(Attack), nameof(Attack.OnAttackTrigger))]
        private static class IncreaseAdrenalineGained_Attack_OnAttackTrigger_Patch
        {
            private static void Prefix(Attack __instance, ref IncreaseAdrenalineGainedRuntime.ModifiedAttackState __state)
            {
                __state = IncreaseAdrenalineGainedRuntime.Apply(__instance);
                IncreaseAdrenalineGainedRuntime.IncrementDepth();
            }

            private static void Postfix(Attack __instance, IncreaseAdrenalineGainedRuntime.ModifiedAttackState __state)
            {
                IncreaseAdrenalineGainedRuntime.DecrementDepth();
                IncreaseAdrenalineGainedRuntime.Restore(__instance, __state);
            }
        }

        [HarmonyPatch(typeof(Attack), "FireProjectileBurst")]
        private static class IncreaseAdrenalineGained_Attack_FireProjectileBurst_Patch
        {
            private static void Prefix(Attack __instance, ref IncreaseAdrenalineGainedRuntime.ModifiedAttackState __state)
            {
                if (!IncreaseAdrenalineGainedRuntime.ShouldSkipFireProjectileBurstApply())
                {
                    __state = IncreaseAdrenalineGainedRuntime.Apply(__instance);
                }
            }

            private static void Postfix(Attack __instance, IncreaseAdrenalineGainedRuntime.ModifiedAttackState __state)
            {
                IncreaseAdrenalineGainedRuntime.Restore(__instance, __state);
            }
        }

        [HarmonyPatch(typeof(StatusEffect), nameof(StatusEffect.Setup))]
        private static class ModifyTrinketDuration_StatusEffect_Setup_Patch
        {
            private static void Postfix(StatusEffect __instance)
            {
                if (__instance?.m_character is not Player player)
                {
                    return;
                }

                if (player.m_trinketItem is not { m_equipped: true } trinket ||
                    trinket.m_shared.m_fullAdrenalineSE == null ||
                    __instance.NameHash() != trinket.m_shared.m_fullAdrenalineSE.NameHash())
                {
                    return;
                }

                float modifier = EpicLootApiBridge.GetTotalActiveMagicEffectValue(player, trinket, ModifyTrinketDuration, PercentScale);
                if (modifier > 0f)
                {
                    __instance.m_ttl *= 1f + modifier;
                }
            }
        }

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

        private static class PiercingShotRuntime
        {
            internal static readonly MethodInfo IsValidTargetMethod = AccessTools.Method(typeof(Projectile), "IsValidTarget");
            internal static readonly MethodInfo AttackProjectileMarker = AccessTools.DeclaredMethod(typeof(PiercingShot_Attack_FireProjectileBurst_Patch), nameof(PiercingShot_Attack_FireProjectileBurst_Patch.MarkAttackProjectile));
            internal static readonly MethodInfo Instantiator = AccessTools.GetDeclaredMethods(typeof(Object))
                .Where(method => method.Name == "Instantiate" && method.GetGenericArguments().Length == 1)
                .Select(method => method.MakeGenericMethod(typeof(GameObject)))
                .First(method => method.GetParameters().Length == 3 && method.GetParameters()[1].ParameterType == typeof(Vector3));
        }

        private sealed class PiercingShotProjectile : MonoBehaviour
        {
            private readonly HashSet<Character> _piercedCharacters = new HashSet<Character>();

            internal int RemainingPierces { get; set; }

            internal bool HasPierced(Character character)
            {
                return _piercedCharacters.Contains(character);
            }

            internal void RecordPierce(Character character)
            {
                _piercedCharacters.Add(character);
                RemainingPierces--;
            }
        }

        [HarmonyPatch(typeof(Attack), "FireProjectileBurst")]
        private static class PiercingShot_Attack_FireProjectileBurst_Patch
        {
            internal static GameObject MarkAttackProjectile(GameObject attackProjectile, Attack attack)
            {
                if (attackProjectile == null || attack?.m_character != Player.m_localPlayer || attack.m_weapon == null)
                {
                    return attackProjectile!;
                }

                int pierces = Mathf.FloorToInt(GetWeaponEffectValue(Player.m_localPlayer, attack.m_weapon, PiercingShot));
                if (pierces <= 0)
                {
                    return attackProjectile;
                }

                Projectile projectile = attackProjectile.GetComponent<Projectile>();
                if (projectile == null || projectile.m_aoe > 0f || projectile.m_onlySpawnedProjectilesDealDamage)
                {
                    return attackProjectile;
                }

                PiercingShotProjectile piercingShotProjectile = attackProjectile.GetComponent<PiercingShotProjectile>() ??
                    attackProjectile.AddComponent<PiercingShotProjectile>();
                piercingShotProjectile.RemainingPierces = Mathf.Max(piercingShotProjectile.RemainingPierces, pierces);

                return attackProjectile;
            }

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                foreach (CodeInstruction instruction in instructions)
                {
                    yield return instruction;
                    if (instruction.opcode == OpCodes.Call && instruction.OperandIs(PiercingShotRuntime.Instantiator))
                    {
                        yield return new CodeInstruction(OpCodes.Ldarg_0);
                        yield return new CodeInstruction(OpCodes.Call, PiercingShotRuntime.AttackProjectileMarker);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
        private static class PiercingShot_Projectile_OnHit_Patch
        {
            private static bool Prefix(Projectile __instance, Collider collider, Vector3 hitPoint, bool water, Vector3 normal)
            {
                if (__instance == null ||
                    water ||
                    collider == null ||
                    !__instance.TryGetComponent(out PiercingShotProjectile piercingShotProjectile))
                {
                    return true;
                }

                GameObject hitObject = Projectile.FindHitObject(collider);
                if (hitObject == null ||
                    hitObject.GetComponent<Character>() is not Character character ||
                    hitObject.GetComponent<IDestructible>() is not IDestructible destructible)
                {
                    return true;
                }

                if (piercingShotProjectile.HasPierced(character))
                {
                    return false;
                }

                if (piercingShotProjectile.RemainingPierces <= 0 || !IsValidTarget(__instance, destructible))
                {
                    return true;
                }

                IHitProjectile hitProjectile = collider.GetComponent<IHitProjectile>();
                if (hitProjectile != null &&
                    !hitProjectile.OnProjectileHit(__instance.m_owner, __instance.m_weapon, __instance, collider, hitPoint, water, normal))
                {
                    return false;
                }

                DamageTarget(__instance, collider, hitPoint, destructible);
                piercingShotProjectile.RecordPierce(character);
                __instance.m_hitEffects.Create(hitPoint, Quaternion.identity);
                __instance.m_onHit?.Invoke(collider, hitPoint, water);

                if (__instance.m_hitNoise > 0f)
                {
                    BaseAI.DoProjectileHitNoise(__instance.transform.position, __instance.m_hitNoise, __instance.m_owner);
                }

                __instance.m_owner?.RaiseSkill(__instance.m_skill, __instance.m_raiseSkillAmount);
                __instance.m_owner?.AddAdrenaline(__instance.m_adrenaline);

                return false;
            }

            private static bool IsValidTarget(Projectile projectile, IDestructible target)
            {
                return (bool)PiercingShotRuntime.IsValidTargetMethod.Invoke(projectile, new object[] { target });
            }

            private static void DamageTarget(Projectile projectile, Collider collider, Vector3 hitPoint, IDestructible target)
            {
                HitData hit = new HitData
                {
                    m_hitCollider = collider,
                    m_damage = projectile.m_damage,
                    m_pushForce = projectile.m_attackForce,
                    m_backstabBonus = projectile.m_backstabBonus,
                    m_point = hitPoint,
                    m_dir = projectile.transform.forward,
                    m_statusEffectHash = projectile.m_statusEffectHash,
                    m_dodgeable = projectile.m_dodgeable,
                    m_blockable = projectile.m_blockable,
                    m_ranged = true,
                    m_skill = projectile.m_skill,
                    m_skillRaiseAmount = projectile.m_raiseSkillAmount,
                    m_hitType = projectile.m_owner is Player ? HitData.HitType.PlayerHit : HitData.HitType.EnemyHit,
                    m_healthReturn = projectile.m_healthReturn
                };
                hit.SetAttacker(projectile.m_owner);

                target.Damage(hit);

                if (projectile.m_healthReturn > 0f && projectile.m_owner != null)
                {
                    projectile.m_owner.Heal(projectile.m_healthReturn);
                }
            }
        }

        [HarmonyPatch(typeof(Character), "OnDeath")]
        private static class ReloadOnKill_Character_OnDeath_Patch
        {
            private static readonly AccessTools.FieldRef<Character, HitData> LastHit =
                AccessTools.FieldRefAccess<Character, HitData>("m_lastHit");

            private static readonly MethodInfo SetWeaponLoadedMethod =
                AccessTools.Method(typeof(Player), "SetWeaponLoaded");

            private static readonly MethodInfo CancelReloadActionMethod =
                AccessTools.Method(typeof(Player), "CancelReloadAction");

            private static void Postfix(Character __instance)
            {
                Player player = Player.m_localPlayer;
                if (__instance == null || __instance.IsPlayer() || player == null)
                {
                    return;
                }

                HitData lastHit = LastHit(__instance);
                if (lastHit == null || !lastHit.m_ranged || lastHit.GetAttacker() != player)
                {
                    return;
                }

                ItemDrop.ItemData currentWeapon = player.GetCurrentWeapon();
                if (currentWeapon == null ||
                    currentWeapon.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Bow ||
                    !currentWeapon.m_shared.m_attack.m_requiresReload ||
                    currentWeapon.m_shared.m_skillType != lastHit.m_skill)
                {
                    return;
                }

                if (!WeaponHasEffect(player, currentWeapon, ReloadOnKill, out _))
                {
                    return;
                }

                CancelReloadActionMethod?.Invoke(player, Array.Empty<object>());
                SetWeaponLoadedMethod?.Invoke(player, new object[] { currentWeapon });
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

        [HarmonyPatch(typeof(Character), nameof(Character.ApplyPushback), typeof(Vector3), typeof(float))]
        private static class Sturdy_Character_ApplyPushback_Patch
        {
            private static void Prefix(Character __instance, ref float pushForce)
            {
                if (__instance is Player player && PlayerHasEffect(player, Sturdy, out float modifier, PercentScale))
                {
                    pushForce *= Mathf.Clamp01(1f - modifier);
                }
            }
        }

        [HarmonyPatch(typeof(Attack), "AddHitPoint")]
        private static class StaminaLeech_Attack_AddHitPoint_Patch
        {
            private static float _lastStaminaLeech;

            private static void Postfix(Attack __instance, GameObject go)
            {
                if (__instance?.m_character is not Player player ||
                    _lastStaminaLeech + 1f > Time.time ||
                    go == null ||
                    go.GetComponentInParent<Character>() is not { } character ||
                    !PlayerHasEffect(player, StaminaLeech, out float modifier, PercentScale))
                {
                    return;
                }

                float staminaReturn = __instance.GetAttackStamina() * modifier;
                player.AddStamina(staminaReturn);
                DamageText.instance?.ShowText(DamageText.TextType.Bonus, character.GetTopPoint(), "+" + staminaReturn.ToString("0.0") + " $item_food_stamina", true);
                _lastStaminaLeech = Time.time;
            }
        }
    }
}
