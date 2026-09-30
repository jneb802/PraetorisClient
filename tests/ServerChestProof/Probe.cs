using System;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

[BepInPlugin("praetoris.validation.serverchest", "Server Chest Proof", "1.0.0")]
public class Probe : BaseUnityPlugin
{
    private Container chest;
    private void Awake()
    {
        new Terminal.ConsoleCommand("chest_proof", "spawn/register/open/close/state/take/deposit/normal/roundtrip <count>/cleanup", args =>
        {
            try { Run(args, s => { Logger.LogInfo(s); args.Context.AddString(s); }); }
            catch (Exception e) { Logger.LogError(e); args.Context.AddString("FAIL " + e); }
        }, onlyAdmin: true);
    }
    private Container Chest()
    {
        if (chest == null) chest = UnityEngine.Object.FindObjectsOfType<Container>().Where(c => c.name.StartsWith("ServerChest") && c.GetComponent<ZNetView>().GetZDO().GetBool("ServerChestProof")).OrderBy(c => Vector3.Distance(c.transform.position, Player.m_localPlayer.transform.position)).First();
        return chest;
    }
    private void Run(Terminal.ConsoleEventArgs args, Action<string> log)
    {
        string action = args[1];
        if (action == "responses")
        {
            System.Collections.Generic.List<string> lines = (System.Collections.Generic.List<string>)AccessTools.Field(typeof(Terminal), "m_chatBuffer").GetValue(Console.instance);
            foreach (string line in lines.ToArray().Where(l => l.Contains("ServerChest") || l.Contains("Delivered") || l.Contains("registered"))) log(System.Text.RegularExpressions.Regex.Replace(line, @"platformId=\S+", "platformId=[redacted]"));
            return;
        }
        if (action == "adopt")
        {
            chest = UnityEngine.Object.FindObjectsOfType<Container>().Single(c => c.name.StartsWith("ServerChest") && c.GetComponent<ZNetView>().GetZDO().m_uid.ToString() == args[2]);
            chest.GetComponent<ZNetView>().ClaimOwnership();
            chest.GetComponent<ZNetView>().GetZDO().Set("ServerChestProof", true);
            log("ADOPT exact test chest " + args[2]); return;
        }
        if (action == "roundtrip" || action == "saveonly")
        {
            int count = int.Parse(args[2]);
            Type storage = AccessTools.TypeByName("PraetorisClient.ServerChestFeature.ServerChestStorage");
            Inventory source = new Inventory("proof", null, 8, Math.Max(1, (count + 7) / 8));
            ItemDrop.ItemData template = ObjectDB.instance.GetItemPrefab("SwordIron").GetComponent<ItemDrop>().m_itemData;
            for (int i = 0; i < count; i++)
            {
                ItemDrop.ItemData item = template.Clone(); item.m_dropPrefab = ObjectDB.instance.GetItemPrefab("SwordIron");
                item.m_gridPos = new Vector2i(i % 8, i / 8); item.m_quality = 2; item.m_durability = 42.5f;
                item.m_crafterName = "ChestProof"; item.m_crafterID = 123;
                item.m_customData["proof-index"] = i.ToString();
                source.GetAllItems().Add(item);
            }
            ZPackage package = new ZPackage();
            AccessTools.Method(storage, "Save").Invoke(null, new object[] { source, package });
            if (action == "saveonly")
            {
                ZPackage read = new ZPackage(package.GetArray());
                int marker = read.ReadInt(); int version = read.ReadInt(); int total = read.ReadInt(); int actual = 0;
                while (read.GetPos() < read.Size())
                {
                    ZPackage chunk = read.ReadPackage(); chunk.ReadInt(); actual += chunk.ReadUShort();
                }
                log((marker < 0 && version == 1 && total == count && actual == count ? "PASS" : "FAIL") + " grouped save count=" + count + " encodedStacks=" + actual + " bytes=" + package.Size());
                return;
            }
            Inventory loaded = new Inventory("proof", null, 8, 1);
            AccessTools.Method(storage, "Load").Invoke(null, new object[] { loaded, new ZPackage(package.GetArray()) });
            bool valid = loaded.NrOfItems() == count && loaded.GetHeight() == Math.Max(1, (count + 7) / 8);
            for (int i = 0; i < count; i++)
            {
                ItemDrop.ItemData item = loaded.GetAllItems()[i];
                valid &= item.m_gridPos == new Vector2i(i % 8, i / 8) && item.m_quality == 2 && Math.Abs(item.m_durability - 42.5f) < .01f && item.m_crafterName == "ChestProof" && item.m_customData["proof-index"] == i.ToString();
            }
            log((valid ? "PASS" : "FAIL") + " roundtrip count=" + count + " rows=" + loaded.GetHeight() + " bytes=" + package.Size() + " metadata-and-positions=" + valid);
            if (count > 2048)
            {
                byte[] truncated = package.GetArray().Take(package.Size() - 5).ToArray();
                try { AccessTools.Method(storage, "Load").Invoke(null, new object[] { loaded, new ZPackage(truncated) }); log("FAIL truncated data accepted"); }
                catch { log((loaded.NrOfItems() == count ? "PASS" : "FAIL") + " truncated data rejected without replacing contents"); }
            }
            return;
        }
        Player player = Player.m_localPlayer;
        if (action == "spawn")
        {
            Vector3 position = player.transform.position + player.transform.forward * 2;
            position.y = ZoneSystem.instance.GetGroundHeight(position);
            chest = Instantiate(ZNetScene.instance.GetPrefab("ServerChest"), position, Quaternion.identity).GetComponent<Container>();
            chest.GetComponent<ZNetView>().GetZDO().Set("ServerChestProof", true);
            log("SPAWN chest at " + position); return;
        }
        if (action == "register")
        {
            Component component = Chest().GetComponent(AccessTools.TypeByName("PraetorisClient.ServerChestFeature.ServerChest"));
            AccessTools.Method(component.GetType(), "RequestRegistration").Invoke(component, null);
            log("REGISTER requested"); return;
        }
        if (action == "open") { Chest().Interact(player, false, false); log("OPEN requested"); return; }
        if (action == "close") { InventoryGui.instance.Hide(); log("CLOSE"); return; }
        if (action == "normal")
        {
            Container normal = Instantiate(ZNetScene.instance.GetPrefab("piece_chest_wood"), player.transform.position + player.transform.right * 2, Quaternion.identity).GetComponent<Container>();
            normal.gameObject.name = "ChestProofWood";
            normal.GetComponent<ZNetView>().GetZDO().Set("ServerChestProof", true);
            normal.Interact(player, false, false); log("NORMAL requested"); return;
        }
        if (action == "cleanup")
        {
            InventoryGui.instance.Hide();
            foreach (Container c in UnityEngine.Object.FindObjectsOfType<Container>().Where(c => c == chest || c.GetComponent<ZNetView>().GetZDO().GetBool("ServerChestProof")))
            { c.GetComponent<ZNetView>().ClaimOwnership(); c.GetInventory().RemoveAll(); ZNetScene.instance.Destroy(c.gameObject); }
            log("CLEANUP"); return;
        }
        Inventory inventory = Chest().GetInventory();
        InventoryGrid grid = InventoryGui.instance.ContainerGrid;
        if (action == "cache")
        {
            ItemDrop.ItemData first = inventory.GetAllItems().First();
            ZDO zdo = Chest().GetComponent<ZNetView>().GetZDO();
            zdo.Set("ServerChestProofRevision", zdo.GetInt("ServerChestProofRevision") + 1);
            bool refreshed = (bool)AccessTools.Method(typeof(Container), "Load").Invoke(Chest(), null);
            log((refreshed && ReferenceEquals(first, inventory.GetAllItems().First()) ? "PASS" : "FAIL") + " unchanged item data retains live inventory"); return;
        }
        if (action == "take")
        {
            ItemDrop.ItemData item = inventory.GetAllItemsInGridOrder().Last();
            int before = inventory.NrOfItems();
            grid.m_onSelected(grid, item, item.m_gridPos, InventoryGrid.Modifier.Move);
            log("TAKE before=" + before + " after=" + inventory.NrOfItems()); return;
        }
        if (action == "deposit")
        {
            ItemDrop.ItemData item = player.GetInventory().GetAllItems().First();
            int before = inventory.NrOfItems();
            bool moved = inventory.MoveItemToThis(player.GetInventory(), item, item.m_stack, 0, inventory.GetHeight() - 1);
            log((!moved && before == inventory.NrOfItems() ? "PASS" : "FAIL") + " deposit blocked=" + !moved); return;
        }
        if (action == "state")
        {
            ZDO zdo = Chest().GetComponent<ZNetView>().GetZDO();
            log("DATA stacks=" + inventory.NrOfItems() + " rows=" + inventory.GetHeight() + " bytes=" + (zdo.GetByteArray(ZDOVars.s_items)?.Length ?? 0) + " revision=" + zdo.DataRevision + " loadedRevision=" + AccessTools.Field(typeof(Container), "m_lastRevision").GetValue(Chest()) + " owner=" + Chest().IsOwner());
            ScrollRect scroll = grid.m_gridRoot.GetComponentInParent<ScrollRect>();
            System.Collections.Generic.List<InventoryElement> elements = (System.Collections.Generic.List<InventoryElement>)AccessTools.Field(typeof(InventoryGrid), "m_elements").GetValue(grid);
            Container current = (Container)AccessTools.Field(typeof(InventoryGui), "m_currentContainer").GetValue(InventoryGui.instance);
            log("STATE stacks=" + inventory.NrOfItems() + " rows=" + inventory.GetHeight() + " uniquePositions=" + inventory.GetAllItems().Select(i => i.m_gridPos).Distinct().Count() + " gridElements=" + elements.Count + " active=" + elements.Count(e => e.gameObject.activeSelf) + " contentHeight=" + grid.m_gridRoot.rect.height + " scroll=" + (scroll == null ? "missing" : scroll.verticalNormalizedPosition.ToString()) + " viewport=" + (scroll == null ? "missing" : (scroll.viewport != null ? scroll.viewport.rect.height : ((RectTransform)scroll.transform).rect.height).ToString()) + " current=" + current?.name);
        }
    }
}
