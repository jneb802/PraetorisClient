using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace PraetorisClient
{
    internal static class CrossbowAmmoSelection
    {
        private sealed class PendingSelection
        {
            internal ItemDrop.ItemData Weapon = null!;
            internal ItemDrop.ItemData Ammo = null!;
            internal float Expires;
        }

        private static readonly ConditionalWeakTable<Player, PendingSelection> Pending = new();

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UseItem))]
        private static class RememberAmmoRequestPatch
        {
            private static void Prefix(Humanoid __instance, Inventory inventory, ItemDrop.ItemData item, bool fromInventoryGui)
            {
                if (__instance is not Player player || player != Player.m_localPlayer || item == null ||
                    item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Ammo) return;

                // A newer explicit choice always replaces an earlier pending choice.
                Pending.Remove(player);
                ItemDrop.ItemData weapon = player.GetCurrentWeapon();
                if (!player.InAttack() || !CrossbowEffectsRuntime.IsCrossbow(weapon) ||
                    ExpandedLoadRuntime.Capacity(weapon) <= 1 || player.GetAmmoItem() == item ||
                    item.m_shared.m_ammoType != weapon.m_shared.m_ammoType ||
                    !player.GetInventory().ContainsItem(item) ||
                    (inventory != null && inventory != player.GetInventory())) return;

                // Hotbar use can feed an interactable. Do not turn that action into ammo selection.
                if (!fromInventoryGui && player.GetHoverObject()?.GetComponentInParent<Interactable>() != null) return;
                Pending.Add(player, new PendingSelection { Weapon = weapon, Ammo = item, Expires = Time.time + 2f });
            }
        }

        [HarmonyPatch(typeof(Player), "UpdateWeaponLoading")]
        private static class ApplyAmmoRequestPatch
        {
            private static void Prefix(Player __instance)
            {
                if (__instance != Player.m_localPlayer || !Pending.TryGetValue(__instance, out PendingSelection choice)) return;
                if (__instance.IsDead() || Time.time > choice.Expires || __instance.GetCurrentWeapon() != choice.Weapon ||
                    !__instance.GetInventory().ContainsItem(choice.Ammo) || choice.Ammo.m_stack <= 0)
                {
                    Pending.Remove(__instance);
                    return;
                }
                if (__instance.InAttack() || __instance.InDodge()) return;
                if (__instance.GetAmmoItem() == choice.Ammo || __instance.EquipItem(choice.Ammo))
                    Pending.Remove(__instance);
            }
        }
    }
}
