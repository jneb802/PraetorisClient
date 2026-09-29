using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using EpicLoot.MagicItemEffects;
using HarmonyLib;
using UnityEngine;

namespace PraetorisClient
{
    internal static class AdrenalineEchoRuntime
    {
        private static Attack? activeEcho;
        private static Vector3 echoPosition;
        private static bool collectingProjectiles;
        private static readonly List<Projectile> EchoProjectiles = new List<Projectile>();
        private static readonly FieldInfo PendingShot = AccessTools.Field(typeof(MultiShot), "_pendingShot");
        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> Trinket = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_trinketItem");
        private static readonly AccessTools.FieldRef<Attack, ItemDrop.ItemData?> AttackAmmo = AccessTools.FieldRefAccess<Attack, ItemDrop.ItemData?>("m_ammoItem");
        private static readonly Func<Humanoid, ItemDrop.ItemData, ItemDrop.ItemData> FindAmmo =
            AccessTools.MethodDelegate<Func<Humanoid, ItemDrop.ItemData, ItemDrop.ItemData>>(AccessTools.Method(typeof(Attack), "FindAmmo"));
        private static readonly Action<Attack> FireProjectileBurst =
            AccessTools.MethodDelegate<Action<Attack>>(AccessTools.Method(typeof(Attack), "FireProjectileBurst"));

        private static void OnTrinketActivated(Player player)
        {
            if (player != Player.m_localPlayer || player.IsDead() || activeEcho != null ||
                Trinket(player) is not { m_equipped: true } trinket ||
                trinket.m_shared.m_fullAdrenalineSE == null ||
                EpicLootApiBridge.GetTotalActiveMagicEffectValue(player, trinket, PraetorisMagicEffects.AdrenalineEcho, 1f) <= 0f)
            {
                return;
            }

            ItemDrop.ItemData weapon = player.GetCurrentWeapon();
            if (weapon?.m_shared.m_attack?.m_attackType != Attack.AttackType.Projectile)
            {
                return;
            }

            // Leave the attack that filled adrenaline before entering another attack's Harmony hooks.
            // Epic Loot temporarily changes shared weapon damage while a projectile burst runs.
            PraetorisClientPlugin.Instance?.StartCoroutine(FireAfterAttack(player, weapon));
        }

        private static IEnumerator FireAfterAttack(Player player, ItemDrop.ItemData weapon)
        {
            yield return null;
            if (player == null || player != Player.m_localPlayer || player.IsDead() ||
                player.GetCurrentWeapon() != weapon || !weapon.m_equipped)
            {
                yield break;
            }

            ItemDrop.ItemData ammo = FindAmmo(player, weapon);
            if (!string.IsNullOrEmpty(weapon.m_shared.m_ammoType) &&
                (ammo == null || (ammo.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Ammo &&
                                  ammo.m_shared.m_itemType != ItemDrop.ItemData.ItemType.AmmoNonEquipable)))
            {
                yield break;
            }
            GameObject? projectile = ammo?.m_shared.m_attack.m_attackProjectile;
            if (projectile == null)
            {
                projectile = weapon.m_shared.m_attack.m_attackProjectile;
            }
            if (projectile == null || projectile.GetComponent<Projectile>() == null)
            {
                yield break;
            }

            Attack echo = weapon.m_shared.m_attack.Clone();
            AttackAmmo(echo) = ammo;
            echo.m_projectiles = 1;
            echo.m_projectileBursts = 1;
            echo.m_perBurstResourceUsage = false;
            echo.m_attackStamina = 0f;
            echo.m_attackHealth = 0f;
            echo.m_attackHealthPercentage = 0f;
            echo.m_attackUseAdrenaline = 0f;
            echo.m_blockReloadTime = 0f;
            echo.m_requiresReload = false;
            echo.m_destroyPreviousProjectile = false;
            echo.m_consumeItem = false;
            echo.m_toggleFlying = false;
            echo.m_recoilPushback = 0f;
            echo.m_selfDamage = 0;

            // MultiShot stores its pending roll globally, including between a staff's bursts.
            // Give the echo its own normal roll, then restore the attack already in progress.
            object pendingShot = PendingShot.GetValue(null);
            bool tripleShot = MultiShot.IsTripleShotActive;
            int shotProjectiles = MultiShot.ShotProjectiles;
            GameObject previousProjectile = weapon.m_lastProjectile;
            float previousAttackTime = weapon.m_lastAttackTime;
            activeEcho = echo;
            echoPosition = player.transform.position;
            collectingProjectiles = true;
            try
            {
                echo.StartWithoutAnimation(player, player.GetComponent<Rigidbody>(), player.GetVisEquipment(), weapon, 1f);
                collectingProjectiles = false;
                // Finish all setup and burst hooks before impact. Epic Loot adds some impact
                // effects after Projectile.Setup, including Explosive Arrows.
                foreach (Projectile spawnedProjectile in EchoProjectiles)
                {
                    if (spawnedProjectile != null)
                    {
                        spawnedProjectile.OnHit(null, echoPosition, false, Vector3.up);
                    }
                }
            }
            catch (Exception exception)
            {
                PraetorisClientPlugin.Log.LogError("Adrenaline Echo failed: " + exception);
            }
            finally
            {
                activeEcho = null;
                collectingProjectiles = false;
                EchoProjectiles.Clear();
                PendingShot.SetValue(null, pendingShot);
                MultiShot.IsTripleShotActive = tripleShot;
                MultiShot.ShotProjectiles = shotProjectiles;
                weapon.m_lastProjectile = previousProjectile;
                weapon.m_lastAttackTime = previousAttackTime;
            }
        }

        [HarmonyPatch(typeof(Attack), nameof(Attack.GetProjectileSpawnPoint))]
        private static class EchoPositionPatch
        {
            private static void Postfix(Attack __instance, ref Vector3 spawnPoint)
            {
                if (__instance == activeEcho) spawnPoint = echoPosition;
            }
        }

        [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
        private static class EchoSetupPatch
        {
            [HarmonyPriority(Priority.Last)]
            private static void Prefix(Projectile __instance, Character owner, ref Vector3 velocity)
            {
                if (!collectingProjectiles || owner != Player.m_localPlayer) return;
                __instance.transform.position = echoPosition;
                velocity = Vector3.zero;
                __instance.m_hitOwner = false;
                __instance.m_bounce = false;
                __instance.m_onlyStopOnTerrain = false;
                __instance.m_respawnItemOnHit = false;
                EchoProjectiles.Add(__instance);
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.AddAdrenaline))]
        private static class ActivationPatch
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                // This branch runs only when full adrenaline activates equipment, including a
                // refresh of an existing status effect and a fill from zero in one hit.
                return new CodeMatcher(instructions)
                    .MatchStartForward(new CodeMatch(OpCodes.Ldfld,
                        AccessTools.Field(typeof(Player), nameof(Player.m_adrenalinePopEffects))))
                    .ThrowIfNotMatch("Cannot locate trinket activation for Adrenaline Echo.")
                    .Insert(new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(AdrenalineEchoRuntime), nameof(OnTrinketActivated))))
                    .InstructionEnumeration();
            }
        }

        [HarmonyPatch(typeof(Attack), nameof(Attack.UseAmmo))]
        private static class EchoAmmoPatch
        {
            private static bool Prefix(Attack __instance, ref ItemDrop.ItemData? ammoItem, ref bool __result)
            {
                if (__instance != activeEcho) return true;
                ammoItem = AttackAmmo(__instance);
                __result = true;
                return false;
            }
        }

        [HarmonyPatch(typeof(Attack), nameof(Attack.OnAttackTrigger))]
        private static class EchoStaggerPatch
        {
            private static bool BlocksAttack(bool staggering, Attack attack) => staggering && attack != activeEcho;

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                return new CodeMatcher(instructions)
                    .MatchStartForward(new CodeMatch(instruction => instruction.Calls(
                        AccessTools.Method(typeof(Character), nameof(Character.IsStaggering)))))
                    .ThrowIfNotMatch("Cannot locate attack stagger check for Adrenaline Echo.")
                    .Advance(1)
                    .Insert(new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(EchoStaggerPatch), nameof(BlocksAttack))))
                    .InstructionEnumeration();
            }
        }

        [HarmonyPatch(typeof(Attack), nameof(Attack.GetAttackEitr), new Type[] { })]
        private static class EchoEitrPatch
        {
            private static bool Prefix(Attack __instance, ref float __result)
            {
                if (__instance != activeEcho) return true;
                // Vanilla reads eitr cost from the weapon's shared template, not this clone.
                __result = 0f;
                return false;
            }
        }

        [HarmonyPatch(typeof(Attack), nameof(Attack.ProjectileAttackTriggered))]
        private static class EchoProjectilePatch
        {
            private static bool Prefix(Attack __instance)
            {
                if (__instance != activeEcho) return true;
                // Use the normal patched burst with the real ItemData. This preserves Epic Loot
                // damage, speed, multishot, projectile markers and weapon attribution on impact.
                // The burst supplies the projectile effects without durability or reload changes.
                FireProjectileBurst(__instance);
                return false;
            }
        }
    }
}
