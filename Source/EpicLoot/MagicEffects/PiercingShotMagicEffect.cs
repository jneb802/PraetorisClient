using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PraetorisClient
{
    internal static partial class PraetorisMagicEffects
    {
        private const string PiercingShotDefinitionJson = @"{
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
}";

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
    }
}
