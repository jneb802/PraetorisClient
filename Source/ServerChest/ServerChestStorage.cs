using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;

namespace PraetorisClient.ServerChestFeature
{
    // Vanilla saves use byte row coordinates and a ushort stack count. Keep each
    // embedded vanilla inventory within those bounds, then restore global rows.
    internal static class ServerChestStorage
    {
        private const int FormatMarker = -1396918321;
        private const int FormatVersion = 1;
        private const int ChunkSlots = ServerChest.Columns * 256;
        private static readonly System.Reflection.MethodInfo InventoryChanged = AccessTools.Method(typeof(Inventory), "Changed");

        internal static void Save(Inventory inventory, ZPackage package)
        {
            ServerChest.CompactInventory(inventory);
            List<ItemDrop.ItemData> items = inventory.GetAllItemsInGridOrder();
            if (items.Count <= ChunkSlots)
            {
                inventory.Save(package);
                return;
            }

            package.Write(FormatMarker);
            package.Write(FormatVersion);
            package.Write(items.Count);
            for (int start = 0; start < items.Count; start += ChunkSlots)
            {
                Inventory chunk = new("ServerChest", null, ServerChest.Columns, 256);
                int count = Math.Min(ChunkSlots, items.Count - start);
                for (int index = 0; index < count; index++)
                {
                    ItemDrop.ItemData item = items[start + index].Clone();
                    item.m_gridPos = new Vector2i(index % ServerChest.Columns, index / ServerChest.Columns);
                    chunk.GetAllItems().Add(item);
                }

                ZPackage data = new();
                chunk.Save(data);
                package.Write(data);
            }
        }

        internal static void Load(Inventory inventory, ZPackage package)
        {
            // Decode into a separate inventory: a failed read must not replace
            // existing contents or trigger a save of partially loaded items.
            Inventory loaded = new("ServerChest", null, ServerChest.Columns, 256);
            List<ItemDrop.ItemData> items;
            int position = package.GetPos();
            if (package.ReadInt() != FormatMarker)
            {
                package.SetPos(position);
                loaded.Load(package);
                items = loaded.GetAllItemsInGridOrder();
            }
            else
            {
                if (package.ReadInt() != FormatVersion)
                    throw new InvalidDataException("Unsupported ServerChest storage version.");
                int count = package.ReadInt();
                if (count <= ChunkSlots)
                    throw new InvalidDataException("Invalid ServerChest stack count.");
                for (int start = 0; start < count; start += ChunkSlots)
                {
                    ZPackage data = package.ReadPackage();
                    Inventory chunk = new("ServerChest", null, ServerChest.Columns, 256);
                    chunk.Load(data);
                    if (data.GetPos() != data.Size() || chunk.NrOfItems() != Math.Min(ChunkSlots, count - start))
                        throw new InvalidDataException("Incomplete ServerChest storage chunk.");
                    // Serialization order is the order of slots across chunks.
                    loaded.GetAllItems().AddRange(chunk.GetAllItemsInGridOrder());
                }
                items = loaded.GetAllItems();
            }

            for (int index = 0; index < items.Count; index++)
                items[index].m_gridPos = new Vector2i(index % ServerChest.Columns, index / ServerChest.Columns);
            inventory.GetAllItems().Clear();
            inventory.GetAllItems().AddRange(items);
            ServerChest.ResizeToFit(inventory);
            InventoryChanged.Invoke(inventory, new object[] { false, false });
        }
    }

    [HarmonyPatch(typeof(Container), "Save")]
    internal static class ServerChestContainerSavePatch
    {
        private static bool Prefix(Container __instance, ZNetView ___m_nview, ref uint ___m_lastRevision)
        {
            ServerChest chest = __instance.GetComponent<ServerChest>();
            if (chest == null) return true;
            ZDO zdo = ___m_nview.GetZDO();
            ServerChest.SaveInventoryToZdo(zdo, __instance.GetInventory());
            chest.LoadedItemData = zdo.GetByteArray(ZDOVars.s_items);
            ___m_lastRevision = zdo.DataRevision;
            return false;
        }
    }

    [HarmonyPatch(typeof(Container), "Load")]
    internal static class ServerChestContainerLoadPatch
    {
        private static bool Prefix(Container __instance, ref bool __result, ZNetView ___m_nview,
            ref uint ___m_lastRevision, ref bool ___m_loading)
        {
            ServerChest chest = __instance.GetComponent<ServerChest>();
            if (chest == null) return true;
            ZDO zdo = ___m_nview.GetZDO();
            // Deliveries can arrive after the open response. Unlike ordinary
            // chests, this inventory must accept those updates while open.
            // Local withdrawals already save and update the byte cache below.
            if (zdo.DataRevision == ___m_lastRevision)
                return false;
            byte[] data = zdo.GetByteArray(ZDOVars.s_items);
            // Opening and closing changes the ZDO revision too. Reuse the live
            // inventory when the item bytes have not changed.
            if (ReferenceEquals(data, chest.LoadedItemData) ||
                (data != null && chest.LoadedItemData != null && data.SequenceEqual(chest.LoadedItemData)))
            {
                ___m_lastRevision = zdo.DataRevision;
                __result = true;
                return false;
            }
            ___m_loading = true;
            try
            {
                if (data != null && data.Length > 0)
                    ServerChestStorage.Load(__instance.GetInventory(), new ZPackage(data));
                chest.LoadedItemData = data;
                ___m_lastRevision = zdo.DataRevision;
                __result = true;
            }
            finally
            {
                ___m_loading = false;
            }
            return false;
        }
    }
}
