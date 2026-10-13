using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace PraetorisClient
{
    internal static class ExpandedLoadRuntime
    {
        private sealed class LoadedShots
        {
            internal ItemDrop.ItemData? Weapon;
            internal int Remaining;
        }

        private static readonly ConditionalWeakTable<Player, LoadedShots> Loads = new();

        internal static bool SupportsItem(ItemDrop.ItemData? item)
        {
            return item?.m_shared?.m_attack is { m_requiresReload: true, m_attackType: Attack.AttackType.Projectile } &&
                (item.m_shared.m_skillType == Skills.SkillType.Crossbows ||
                 // Loot generation checks item templates before m_dropPrefab is assigned.
                 item.m_shared.m_name == "$item_staff_lightning");
        }

        private static int Capacity(ItemDrop.ItemData weapon)
        {
            if (!SupportsItem(weapon)) return 1;

            // Read this weapon only. An effect on another equipped item must not add shots.
            float bonus = EpicLootApiBridge.GetTotalActiveMagicEffectValue(null, weapon, PraetorisMagicEffects.ExpandedLoad, 1f);
            return 1 + Mathf.Clamp(Mathf.FloorToInt(bonus), 0, 3);
        }

        [HarmonyPatch(typeof(Player), "SetWeaponLoaded")]
        private static class SetWeaponLoadedPatch
        {
            private static void Postfix(Player __instance, ItemDrop.ItemData weapon)
            {
                if (__instance != Player.m_localPlayer) return;

                if (weapon == null)
                {
                    Loads.Remove(__instance);
                    return;
                }

                LoadedShots load = Loads.GetOrCreateValue(__instance);
                load.Weapon = weapon;
                load.Remaining = Capacity(weapon);
            }
        }

        private static void ConsumeShot(Humanoid character, Attack attack)
        {
            if (character is Player player && player == Player.m_localPlayer &&
                Loads.TryGetValue(player, out LoadedShots load) &&
                load.Weapon == attack.GetWeapon() && player.GetCurrentWeapon() == load.Weapon &&
                player.IsWeaponLoaded())
            {
                load.Remaining = Mathf.Min(load.Remaining, Capacity(attack.GetWeapon())) - 1;
                if (load.Remaining > 0) return;
            }

            character.ResetLoadedWeapon();
        }

        [HarmonyPatch(typeof(Attack), nameof(Attack.OnAttackTrigger))]
        private static class AttackTriggerPatch
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                // Only replace the reset after a successful shot. Unequip, death, and other
                // reset paths still clear the load. Failed ammo checks do not consume a shot.
                return new CodeMatcher(instructions)
                    .MatchStartForward(new CodeMatch(OpCodes.Callvirt,
                        AccessTools.Method(typeof(Humanoid), nameof(Humanoid.ResetLoadedWeapon))))
                    .ThrowIfNotMatch("Cannot locate weapon reload reset for Expanded Load.")
                    .SetInstruction(new CodeInstruction(OpCodes.Ldarg_0))
                    .Advance(1)
                    .Insert(new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(ExpandedLoadRuntime), nameof(ConsumeShot))))
                    .InstructionEnumeration();
            }
        }
    }
}
