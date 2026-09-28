using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

// Test-profile helper only. This assembly is never included in the release DLL.
[BepInPlugin("praetoris.communitychest.liveprobe", "Community Chest Live Probe", "1.0.0")]
[BepInDependency("warpalicious.PraetorisClient")]
public sealed class LiveProbe : BaseUnityPlugin
{
    private static bool DropCommit;
    private static string LastCommit = "";
    private static readonly Type Rpc = typeof(PraetorisClient.PraetorisClientPlugin).Assembly.GetType("PraetorisClient.CommunityChestFeature.CommunityChestRpc")!;
    private void Awake()
    {
        new Harmony("praetoris.communitychest.liveprobe").Patch(AccessTools.Method(Rpc, "Send"), prefix: new HarmonyMethod(typeof(LiveProbe), nameof(BeforeSend)));
        _ = new Terminal.ConsoleCommand("ccprobe", "Community chest live validation helper", args =>
        {
            string command = args.Length > 1 ? args[1] : "status";
            Player player = Player.m_localPlayer;
            InventoryGui gui = InventoryGui.instance;
            if (command == "dropcommit") { DropCommit = true; args.Context.AddString("PROBE next commit will be dropped"); return; }
            if (command == "close") { gui.Hide(); return; }
            if (command == "replay")
            {
                AccessTools.Method(Rpc, "Send").Invoke(null, new object[] { "commit", ZDOID.None, LastCommit, 0, 0L });
                args.Context.AddString("PROBE replayed last commit"); return;
            }
            if (command == "inspect")
            {
                GameObject prefab = ZNetScene.instance.GetPrefab("CommunityChest");
                int recipes = Resources.FindObjectsOfTypeAll<PieceTable>().Count(table => table.m_pieces.Contains(prefab));
                args.Context.AddString("PROBE prefab=" + (prefab != null) + " buildTables=" + recipes + " destructible=" + (prefab.GetComponent<Destructible>() != null) + " wear=" + (prefab.GetComponent<WearNTear>() != null) + " piece=" + (prefab.GetComponent<Piece>() != null) + " defaultLoot=" + prefab.GetComponent<Container>().m_defaultItems.m_drops.Count);
                GameObject vanilla = ZNetScene.instance.GetPrefab("TreasureChest_dvergrtower");
                args.Context.AddString("PROBE vanillaChest=" + (vanilla != null) + " vanillaComponents=" + string.Join(",", vanilla.GetComponents<Component>().Select(c => c.GetType().Name)));
                return;
            }
            if (command == "click" || command == "drag" || command == "wood" || command == "dropworld")
            {
                bool chest = args.Length > 2 && args[2] == "chest";
                InventoryGrid grid = (InventoryGrid)AccessTools.Field(typeof(InventoryGui), chest ? "m_containerGrid" : "m_playerGrid").GetValue(gui);
                string name = ObjectDB.instance.GetItemPrefab(command == "wood" ? "Wood" : "Coins").GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
                ItemDrop.ItemData item = grid.GetInventory().GetItem(name);
                if (item == null) { args.Context.AddString("PROBE no requested item"); return; }
                if (command == "drag")
                {
                    InventoryGrid target = (InventoryGrid)AccessTools.Field(typeof(InventoryGui), chest ? "m_playerGrid" : "m_containerGrid").GetValue(gui);
                    int amount = args.Length > 3 ? int.Parse(args[3]) : item.m_stack;
                    AccessTools.Method(typeof(InventoryGui), "SetupDragItem").Invoke(gui, new object[] { item, grid.GetInventory(), amount });
                    AccessTools.Method(typeof(InventoryGui), "OnSelectedItem").Invoke(gui, new object?[] { target, null, new Vector2i(0, 0), InventoryGrid.Modifier.Select });
                }
                else if (command == "dropworld") player.DropItem(grid.GetInventory(), item, item.m_stack);
                else AccessTools.Method(typeof(InventoryGui), "OnSelectedItem").Invoke(gui, new object[] { grid, item, item.m_gridPos, InventoryGrid.Modifier.Move });
                args.Context.AddString("PROBE inventory click dispatched"); return;
            }
            if (command == "takeall") { AccessTools.Method(typeof(InventoryGui), "OnTakeAll").Invoke(gui, null); return; }
            if (command == "stackall") { AccessTools.Method(typeof(InventoryGui), "OnStackAll").Invoke(gui, null); return; }
            if (command == "fill")
            {
                Inventory inventory = player.GetInventory();
                while (inventory.GetEmptySlots() > 0)
                    if (inventory.AddItem("Stone", 50, 1, 0, 0L, "", cheated: false) == null) break;
                args.Context.AddString("PROBE filled empty slots"); return;
            }
            if (command == "unfill")
            {
                string stone = ObjectDB.instance.GetItemPrefab("Stone").GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
                player.GetInventory().RemoveItem(stone, player.GetInventory().CountItems(stone));
                args.Context.AddString("PROBE removed test stone"); return;
            }
            string coinName = ObjectDB.instance.GetItemPrefab("Coins").GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
            Container? current = AccessTools.Field(typeof(InventoryGui), "m_currentContainer").GetValue(gui) as Container;
            args.Context.AddString("PROBE playerCoins=" + player.GetInventory().CountItems(coinName) + " chestCoins=" + (current != null ? current.GetInventory().CountItems(coinName) : -1) + " slots=" + player.GetInventory().GetEmptySlots());
        });
    }
    private static bool BeforeSend(string operation, string transaction)
    {
        if (operation != "commit") return true;
        LastCommit = transaction;
        if (!DropCommit) return true;
        DropCommit = false;
        Debug.Log("PROBE dropped commit after character save");
        return false;
    }
}
