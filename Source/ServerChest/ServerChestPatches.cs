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

    [HarmonyPatch(typeof(Player), "FindHoverObject")]
    internal static class ServerChestFindHoverObjectPatch
    {
        private static readonly int InteractMask = LayerMask.GetMask(
            "item",
            "piece",
            "piece_nonsolid",
            "Default",
            "static_solid",
            "Default_small",
            "character",
            "character_net",
            "terrain",
            "vehicle");
        private static readonly RaycastHit[] HoverHits = new RaycastHit[128];
        private static readonly IComparer<RaycastHit> RaycastHitComparer = Comparer<RaycastHit>.Create(CompareRaycastHits);

        private static void Postfix(Player __instance, ref GameObject hover)
        {
            if (hover == null || !IsServerChestHover(hover) || GameCamera.instance == null)
            {
                return;
            }

            Transform cameraTransform = GameCamera.instance.transform;
            int length = Physics.RaycastNonAlloc(cameraTransform.position, cameraTransform.forward, HoverHits, 50f, InteractMask);
            Array.Sort(HoverHits, 0, length, RaycastHitComparer);

            for (int index = 0; index < length; index++)
            {
                RaycastHit hit = HoverHits[index];
                if (IsLocalPlayerHit(__instance, hit))
                {
                    continue;
                }

                if (Vector3.Distance(__instance.m_eye.position, hit.point) >= __instance.m_maxInteractDistance)
                {
                    break;
                }

                GameObject? candidate = GetHoverCandidate(hit);
                if (candidate == null || candidate == hover)
                {
                    continue;
                }

                if (IsServerChestHover(candidate))
                {
                    continue;
                }

                Container container = candidate.GetComponentInParent<Container>();
                if (container != null)
                {
                    hover = candidate;
                    return;
                }

                return;
            }
        }

        private static int CompareRaycastHits(RaycastHit left, RaycastHit right)
        {
            return left.distance.CompareTo(right.distance);
        }

        private static bool IsLocalPlayerHit(Player player, RaycastHit hit)
        {
            return hit.collider != null &&
                   hit.collider.attachedRigidbody != null &&
                   hit.collider.attachedRigidbody.gameObject == player.gameObject;
        }

        private static GameObject? GetHoverCandidate(RaycastHit hit)
        {
            if (hit.collider == null)
            {
                return null;
            }

            if (hit.collider.GetComponent<Hoverable>() != null)
            {
                return hit.collider.gameObject;
            }

            if (hit.collider.attachedRigidbody != null)
            {
                return hit.collider.attachedRigidbody.gameObject;
            }

            return hit.collider.gameObject;
        }

        private static bool IsServerChestHover(GameObject gameObject)
        {
            Container container = gameObject.GetComponentInParent<Container>();
            return container != null && ServerChest.IsServerChest(container);
        }
    }

    [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.UpdateInventory))]
    internal static class ServerChestInventoryGridUpdatePatch
    {
        private static readonly FieldInfo? ElementsField = AccessTools.Field(typeof(InventoryGrid), "m_elements");
        private static readonly Dictionary<Type, FieldInfo?> ElementGoFields = new();

        private static bool Prefix(InventoryGrid __instance, Inventory inventory)
        {
            if (!ServerChest.TryGetByInventory(inventory, out _))
            {
                return true;
            }

            ServerChest.ApplyMaxInventoryShape(inventory);
            return true;
        }

        private static void Postfix(InventoryGrid __instance, Inventory inventory)
        {
            if (!ServerChest.TryGetByInventory(inventory, out _))
            {
                return;
            }

            int visibleSlots = inventory.NrOfItems();
            if (__instance.m_gridRoot != null)
            {
                __instance.m_gridRoot.gameObject.SetActive(visibleSlots > 0);
                float visibleRows = visibleSlots <= 0 ? 0f : (float)Math.Ceiling(visibleSlots / (double)ServerChest.MaxColumns);
                __instance.m_gridRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, visibleRows * __instance.m_elementSpace);
            }

            IList? elements = ElementsField != null ? ElementsField.GetValue(__instance) as IList : null;
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
