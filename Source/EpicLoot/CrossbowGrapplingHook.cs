using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace PraetorisClient
{
    internal static class CrossbowGrapplingHook
    {
        private sealed class HookShot
        {
            internal ItemDrop.ItemData Weapon = null!;
            internal bool Fired;
        }

        private static readonly ConditionalWeakTable<Attack, HookShot> Attacks = new();
        private static readonly ConditionalWeakTable<ItemDrop.ItemData, HookShot> Projectiles = new();
        [ThreadStatic] private static ItemDrop.ItemData? _spawningWeapon;

        private static bool HasHook(ItemDrop.ItemData? weapon) =>
            weapon?.m_shared.m_skillType == Skills.SkillType.Crossbows &&
            weapon.m_shared.m_attack.m_attackType == Attack.AttackType.Projectile &&
            // Read the socketed weapon directly. During projectile impact Epic Loot
            // temporarily substitutes the utility projectile's item for held weapons.
            EpicLootApiBridge.GetTotalActiveMagicEffectValue(null, weapon, CrossbowEffectDefinitions.Grapple, 1f) > 0f;

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.HaveSecondaryAttack))]
        private static class SpecialAttackHintPatch
        {
            private static void Postfix(ItemDrop.ItemData __instance, ref bool __result)
            {
                Player player = Player.m_localPlayer;
                if (!__result && player != null && HasHook(__instance)) __result = true;
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
        private static class SpecialAttackPatch
        {
            private static bool Prefix(Humanoid __instance, bool secondaryAttack, ref bool __result)
            {
                if (!secondaryAttack || __instance is not Player player || player != Player.m_localPlayer) return true;
                ItemDrop.ItemData weapon = player.GetCurrentWeapon();
                if (!HasHook(weapon)) return true;
                __result = false;
                if (player.IsDead() || player.IsTeleporting() || player.InAttack() || player.InDodge() ||
                    !player.CanMove() || player.IsKnockedBack() || player.IsStaggering() ||
                    (player.InMinorAction() && !player.IsReloadActionQueued())) return false;

                ItemDrop? hook = ObjectDB.instance.GetItemPrefab("GrapplingHook")?.GetComponent<ItemDrop>();
                if (hook == null || hook.m_itemData.m_shared.m_attack.m_attackProjectile?.GetComponent<Projectile>() == null) return false;
                Attack attack = hook.m_itemData.m_shared.m_attack.Clone();
                attack.m_requiresReload = false;
                attack.m_attackAnimation = weapon.m_shared.m_attack.m_attackAnimation;
                attack.m_attackChainLevels = 1;
                attack.m_attackRandomAnimations = 1;
                attack.m_perBurstResourceUsage = false;
                // A private item supplies animation/stamina context without changing equipped
                // weapon data, spending bolts, or inheriting offensive enchantments.
                ItemDrop.ItemData proxy = new ItemDrop.ItemData
                {
                    m_dropPrefab = hook.gameObject,
                    m_shared = new ItemDrop.ItemData.SharedData
                    {
                        m_name = hook.m_itemData.m_shared.m_name,
                        m_itemType = ItemDrop.ItemData.ItemType.Tool,
                        m_skillType = Skills.SkillType.None,
                        m_ammoType = "",
                        m_useDurability = false,
                        m_attack = attack
                    }
                };
                if (!attack.Start(player, player.m_body, player.m_zanim, player.m_animEvent,
                    player.m_visEquipment, proxy, player.m_currentAttack, player.m_timeSinceLastAttack, 0f)) return false;

                player.m_currentAttack?.Stop();
                player.m_previousAttack = player.m_currentAttack;
                player.CancelReloadAction();
                player.StartAttackGroundCheck();
                player.m_currentAttack = attack;
                player.m_currentAttackIsSecondary = true;
                player.m_lastCombatTimer = 0f;
                HookShot shot = new HookShot { Weapon = weapon };
                Attacks.Add(attack, shot);
                Projectiles.Add(proxy, shot);
                __result = true;
                return false;
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.OnAttackTrigger))]
        private static class LaunchHookPatch
        {
            private static bool Prefix(Humanoid __instance)
            {
                Attack attack = __instance.m_currentAttack;
                if (attack == null || !Attacks.TryGetValue(attack, out HookShot shot)) return true;
                if (shot.Fired) return false;
                shot.Fired = true;
                if (__instance is not Player player || player != Player.m_localPlayer || player.IsDead() ||
                    player.GetCurrentWeapon() != shot.Weapon || !HasHook(shot.Weapon)) return false;

                // Use the native hook projectile and rope physics. Bypass offensive shot
                // callbacks so Triple Shot, ammo refunds, recoil and damage bonuses do not
                // turn this utility attack into extra shots or consume a loaded bolt.
                attack.GetProjectileSpawnPoint(out Vector3 origin, out Vector3 direction);
                GameObject instance = UnityEngine.Object.Instantiate(attack.m_attackProjectile, origin, Quaternion.LookRotation(direction));
                Projectile projectile = instance.GetComponent<Projectile>();
                projectile.m_respawnItemOnHit = false;
                projectile.Setup(player, direction * attack.m_projectileVel, 0f,
                    new HitData { m_skill = Skills.SkillType.None, m_skillRaiseAmount = 0f }, attack.m_weapon, null);
                projectile.m_damage = default;
                projectile.m_attackForce = 0f;
                attack.m_triggerEffect.Create(origin, Quaternion.LookRotation(direction), null, 1f, -1, player.GetZDOID());
                player.StartCoroutine(player.EndAttackGroundCheck());
                return false;
            }
        }

        [HarmonyPatch(typeof(Projectile), "SpawnOnHit")]
        private static class HookImpactPatch
        {
            private static bool Prefix(Projectile __instance, out ItemDrop.ItemData? __state)
            {
                __state = _spawningWeapon;
                if (__instance.m_weapon == null || !Projectiles.TryGetValue(__instance.m_weapon, out HookShot shot)) return true;
                Player player = Player.m_localPlayer;
                if (player == null || __instance.m_owner != player || player.IsDead() ||
                    player.GetCurrentWeapon() != shot.Weapon || !HasHook(shot.Weapon)) return false;
                _spawningWeapon = shot.Weapon;
                return true;
            }
            private static void Finalizer(ItemDrop.ItemData? __state) => _spawningWeapon = __state;
        }

        [HarmonyPatch(typeof(GrapplingPoint), nameof(GrapplingPoint.Activate))]
        private static class BindHookPatch
        {
            private static void Prefix(GrapplingPoint __instance, Character character)
            {
                if (_spawningWeapon == null || character != Player.m_localPlayer) return;
                __instance.m_equipCheck = _spawningWeapon.m_dropPrefab.GetComponent<ItemDrop>();
                HookBinding binding = __instance.gameObject.GetComponent<HookBinding>() ?? __instance.gameObject.AddComponent<HookBinding>();
                binding.Weapon = _spawningWeapon;
                binding.Point = __instance;
                Player.m_localPlayer.CancelReloadAction();
            }
        }

        [HarmonyPatch(typeof(Character), "UpdateWalking")]
        private static class GroundPullPatch
        {
            private static void Postfix(Character __instance)
            {
                Player player = Player.m_localPlayer;
                if (player == null || __instance != player || !__instance.IsOnGround()) return;
                GrapplingPoint point = GrapplingPoint.m_localGrappler;
                if (point == null || point.m_secondary || point.Method != GrapplingPoint.GrapplingMethod.ConstantVelocity) return;
                HookBinding binding = point.GetComponent<HookBinding>();
                if (binding == null || player.GetCurrentWeapon() != binding.Weapon || !HasHook(binding.Weapon)) return;
                // Native ground steering overwrites the rope's velocity. Apply the same
                // pull after steering; the rigidbody still handles terrain and collisions.
                Vector3 offset = point.transform.position - __instance.transform.position;
                if (offset.magnitude > point.m_closePullDist)
                    __instance.SetVelocity(offset.normalized * point.m_pullForce);
            }
        }

        // Native hooks can create a hanging rope when the pull ends. Keep the same
        // crossbow binding on that replacement rather than requiring a held hook tool.
        [HarmonyPatch(typeof(GrapplingPoint), nameof(GrapplingPoint.Break))]
        private static class CarryBindingPatch
        {
            private static void Prefix(GrapplingPoint __instance, out ItemDrop.ItemData? __state)
            {
                __state = _spawningWeapon;
                HookBinding binding = __instance.GetComponent<HookBinding>();
                if (binding != null) _spawningWeapon = binding.Weapon;
            }
            private static void Finalizer(ItemDrop.ItemData? __state) => _spawningWeapon = __state;
        }

        private sealed class HookBinding : MonoBehaviour
        {
            internal ItemDrop.ItemData Weapon = null!;
            internal GrapplingPoint Point = null!;
            private void Update()
            {
                Player player = Player.m_localPlayer;
                if (player == null || Point == null) return;
                if (player.IsDead() || player.GetCurrentWeapon() != Weapon || !HasHook(Weapon)) Point.Break(true);
            }
        }
    }
}
