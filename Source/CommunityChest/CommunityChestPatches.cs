using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PraetorisClient.CommunityChestFeature
{
    // Community Chest inventories are private display snapshots, never shared ZDO contents.
    [HarmonyPatch(typeof(Container), "Awake")]
    internal static class CommunityChestAwakePatch
    {
        private static bool Prefix(Container __instance)
        {
            if (!CommunityChest.Is(__instance)) return true;
            AccessTools.Field(typeof(Container), "m_nview").SetValue(__instance, __instance.GetComponent<ZNetView>());
            AccessTools.Field(typeof(Container), "m_inventory").SetValue(__instance, new Inventory(__instance.m_name, __instance.m_bkg, 8, 5));
            return false;
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.Interact))]
    internal static class CommunityChestInteractPatch
    {
        private static bool Prefix(Container __instance, bool hold, ref bool __result)
        {
            if (!CommunityChest.Is(__instance)) return true;
            if (!hold) CommunityChestClient.Open(__instance);
            __result = !hold;
            return false;
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
    internal static class CommunityChestHoverPatch
    {
        private static bool Prefix(Container __instance, ref string __result)
        {
            if (!CommunityChest.Is(__instance)) return true;
            __result = Localization.instance.Localize("Community Chest\nOnly you can access these coins\n[<color=yellow><b>$KEY_Use</b></color>] Open");
            return false;
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.IsOwner))]
    internal static class CommunityChestOwnerPatch
    {
        private static bool Prefix(Container __instance, ref bool __result)
        {
            if (!CommunityChest.Is(__instance)) return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class CommunityChestSharedInventoryPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string method in new[] { "Save", "Load", "CheckForChanges", "OnDestroyed", "RPC_RequestOpen", "RPC_OpenResponse", "RPC_RequestTakeAll", "RPC_TakeAllResponse", "RPC_RequestStack", "RPC_StackResponse" })
                yield return AccessTools.Method(typeof(Container), method);
        }
        private static bool Prefix(Container __instance) => !CommunityChest.Is(__instance);
    }

    [HarmonyPatch(typeof(Container), nameof(Container.SetInUse))]
    internal static class CommunityChestUsePatch
    {
        private static bool Prefix(Container __instance, bool inUse)
        {
            if (!CommunityChest.Is(__instance)) return true;
            AccessTools.Field(typeof(Container), "m_inUse").SetValue(__instance, inUse);
            if (__instance.m_open != null) __instance.m_open.SetActive(inUse);
            if (__instance.m_closed != null) __instance.m_closed.SetActive(!inUse);
            return false;
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.CanBeRemoved))]
    internal static class CommunityChestRemovePatch
    {
        private static bool Prefix(Container __instance, ref bool __result)
        {
            if (!CommunityChest.Is(__instance)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "OnSelectedItem")]
    internal static class CommunityChestSelectPatch
    {
        private static bool Prefix(InventoryGui __instance, InventoryGrid grid, ItemDrop.ItemData item, InventoryGrid.Modifier mod)
        {
            if (!CommunityChestClient.IsOpen || mod != InventoryGrid.Modifier.Move ||
                AccessTools.Field(typeof(InventoryGui), "m_dragGo").GetValue(__instance) as GameObject != null) return true;
            CommunityChestClient.QuickTransfer(grid, item);
            return false;
        }
    }

    [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.DropItem))]
    internal static class CommunityChestDragPatch
    {
        private static bool Prefix(InventoryGrid __instance, Inventory fromInventory, ItemDrop.ItemData item, int amount, ref bool __result)
        {
            if (!CommunityChestClient.IsOpen) return true;
            Inventory chest = CommunityChestClient.CurrentChest!.GetInventory();
            Inventory target = __instance.GetInventory();
            if (fromInventory != chest && target != chest) return true;
            if (fromInventory == target) return true;
            __result = false;
            if (item == null || !fromInventory.ContainsItem(item) || amount <= 0 || amount > item.m_stack) return false;
            if (item.m_shared.m_name != CommunityChestClient.CoinName)
            {
                CommunityChestClient.Message("The Community Chest accepts coins only.");
                return false;
            }
            Inventory player = Player.m_localPlayer.GetInventory();
            if ((fromInventory != chest || target != player) && (fromInventory != player || target != chest)) return false;
            CommunityChestClient.Transfer(fromInventory == chest ? -amount : amount);
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.DropItem))]
    internal static class CommunityChestWorldDropPatch
    {
        private static bool Prefix(Inventory inventory, ref bool __result)
        {
            if (!CommunityChestClient.IsOpen || inventory != CommunityChestClient.CurrentChest!.GetInventory()) return true;
            CommunityChestClient.Message("Withdraw coins into your inventory before dropping them.");
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "OnTakeAll")]
    internal static class CommunityChestTakeAllPatch
    {
        private static bool Prefix()
        {
            if (!CommunityChestClient.IsOpen) return true;
            CommunityChestClient.TransferAll(false);
            return false;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "OnStackAll")]
    internal static class CommunityChestStackAllPatch
    {
        private static bool Prefix()
        {
            if (!CommunityChestClient.IsOpen) return true;
            CommunityChestClient.TransferAll(true);
            return false;
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.StackAll))]
    internal static class CommunityChestContainerStackPatch
    {
        private static bool Prefix(Container __instance)
        {
            if (!CommunityChest.Is(__instance)) return true;
            if (CommunityChestClient.IsOpen) CommunityChestClient.TransferAll(true);
            return false;
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.TakeAll))]
    internal static class CommunityChestContainerTakePatch
    {
        private static bool Prefix(Container __instance, ref bool __result)
        {
            if (!CommunityChest.Is(__instance)) return true;
            if (CommunityChestClient.IsOpen) CommunityChestClient.TransferAll(false);
            __result = false;
            return false;
        }
    }
}
