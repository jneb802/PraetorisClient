using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PraetorisClient.ServerChestFeature
{
    [HarmonyPatch(typeof(Container), nameof(Container.Interact))]
    internal static class ServerChestContainerInteractPatch
    {
        private static bool Prefix(Container __instance, Humanoid character, bool hold, bool alt, ref bool __result)
        {
            if (!ServerChest.IsServerChest(__instance))
            {
                return true;
            }

            if (hold)
            {
                __result = false;
                return false;
            }

            if (!alt)
            {
                return true;
            }

            ServerChest serverChest = __instance.GetComponent<ServerChest>();
            serverChest.OpenRegistrationPanel();
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.UpdateInventory))]
    internal static class ServerChestInventoryGridUpdatePatch
    {
        private static bool Prefix(InventoryGrid __instance, Inventory inventory)
        {
            if (!ServerChest.TryGetByInventory(inventory, out _))
            {
                return true;
            }

            ServerChest.ApplyMaxInventoryShape(inventory);
            return true;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "UpdateContainer")]
    internal static class ServerChestInventoryGuiUpdateContainerPatch
    {
        private static readonly FieldInfo? CurrentContainerField = AccessTools.Field(typeof(InventoryGui), "m_currentContainer");
        private static readonly FieldInfo? ElementsField = AccessTools.Field(typeof(InventoryGrid), "m_elements");
        private static readonly Dictionary<Type, FieldInfo?> ElementGoFields = new();

        private static void Postfix(InventoryGui __instance)
        {
            Container? currentContainer = CurrentContainerField != null ? CurrentContainerField.GetValue(__instance) as Container : null;
            if (currentContainer == null)
            {
                return;
            }

            InventoryGrid containerGrid = __instance.ContainerGrid;
            if (!ServerChest.IsServerChest(currentContainer))
            {
                RestoreGrid(containerGrid);
                return;
            }

            Inventory inventory = currentContainer.GetInventory();
            int visibleSlots = inventory.NrOfItems();
            if (containerGrid.m_gridRoot != null)
            {
                containerGrid.m_gridRoot.gameObject.SetActive(visibleSlots > 0);
                float visibleRows = visibleSlots <= 0 ? 0f : (float)Math.Ceiling(visibleSlots / (double)ServerChest.MaxColumns);
                containerGrid.m_gridRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, visibleRows * containerGrid.m_elementSpace);
            }

            SetGridElementsActive(containerGrid, visibleSlots);
        }

        private static void RestoreGrid(InventoryGrid inventoryGrid)
        {
            if (inventoryGrid.m_gridRoot != null)
            {
                inventoryGrid.m_gridRoot.gameObject.SetActive(true);
            }

            SetGridElementsActive(inventoryGrid, int.MaxValue);
        }

        private static void SetGridElementsActive(InventoryGrid inventoryGrid, int visibleSlots)
        {
            IList? elements = GetElements(inventoryGrid);
            if (elements == null)
            {
                return;
            }

            for (int index = 0; index < elements.Count; index++)
            {
                GameObject? elementGo = GetElementGameObject(elements[index]);
                if (elementGo != null)
                {
                    elementGo.SetActive(index < visibleSlots);
                }
            }
        }

        private static IList? GetElements(InventoryGrid inventoryGrid)
        {
            return ElementsField != null ? ElementsField.GetValue(inventoryGrid) as IList : null;
        }

        private static GameObject? GetElementGameObject(object element)
        {
            Type elementType = element.GetType();
            if (!ElementGoFields.TryGetValue(elementType, out FieldInfo? field))
            {
                field = AccessTools.Field(elementType, "m_go");
                ElementGoFields[elementType] = field;
            }

            return field != null ? field.GetValue(element) as GameObject : null;
        }
    }

    [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.DropItem))]
    internal static class ServerChestInventoryGridDropItemPatch
    {
        private static bool Prefix(InventoryGrid __instance, Inventory fromInventory, ref bool __result)
        {
            Inventory target = __instance.GetInventory();
            if (target != null &&
                fromInventory != null &&
                !ReferenceEquals(target, fromInventory) &&
                ServerChest.TryGetByInventory(target, out _))
            {
                __result = false;
                ServerChest.ShowMessage("Items cannot be placed into a ServerChest.");
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveItemToThis), typeof(Inventory), typeof(ItemDrop.ItemData))]
    internal static class ServerChestInventoryMoveItemToThisPatch
    {
        private static bool Prefix(Inventory __instance, Inventory fromInventory)
        {
            if (ShouldBlockMove(__instance, fromInventory))
            {
                ServerChest.ShowMessage("Items cannot be placed into a ServerChest.");
                return false;
            }

            return true;
        }

        internal static bool ShouldBlockMove(Inventory target, Inventory source)
        {
            return target != null &&
                   source != null &&
                   !ReferenceEquals(target, source) &&
                   ServerChest.TryGetByInventory(target, out _);
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveItemToThis), typeof(Inventory), typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int))]
    internal static class ServerChestInventoryMoveItemToThisAmountPatch
    {
        private static bool Prefix(Inventory __instance, Inventory fromInventory, ref bool __result)
        {
            if (ServerChestInventoryMoveItemToThisPatch.ShouldBlockMove(__instance, fromInventory))
            {
                __result = false;
                ServerChest.ShowMessage("Items cannot be placed into a ServerChest.");
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.StackAll))]
    internal static class ServerChestInventoryStackAllPatch
    {
        private static bool Prefix(Inventory __instance, ref int __result)
        {
            if (ServerChest.TryGetByInventory(__instance, out _))
            {
                __result = 0;
                ServerChest.ShowMessage("Items cannot be placed into a ServerChest.");
                return false;
            }

            return true;
        }
    }
}
