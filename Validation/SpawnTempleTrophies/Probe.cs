using System;
using System.Collections;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;

[BepInPlugin("praetoris.validation.templetrophies", "Temple Trophy Probe", "1.0.0")]
public class Probe : BaseUnityPlugin
{
    private static ItemStand[] stands = new ItemStand[0];
    private int failures;
    private void Awake()
    {
        new Terminal.ConsoleCommand("temple_probe", "list/prefabs/test index direct|auto|power/clear index/spawn prefab", args =>
        {
            try { Run(args, message => { Logger.LogInfo(message); args.Context.AddString(message); }); }
            catch (Exception e) { args.Context.AddString("FAIL " + e); }
        });
    }
    private void Run(Terminal.ConsoleEventArgs args, Action<string> output)
    {
        Player player = Player.m_localPlayer;
        string action = args[1];
        if (action == "suite")
        {
            StartCoroutine(Suite(output));
            return;
        }
        if (action == "goto")
        {
            if (ZoneSystem.instance.FindClosestLocation(args[2], player.transform.position, out ZoneSystem.LocationInstance location))
            {
                player.TeleportTo(location.m_position + Vector3.up * 2, Quaternion.identity, true);
                output("GOTO " + args[2] + " " + location.m_position);
            }
            else output("FAIL missing location " + args[2]);
            return;
        }
        if (action == "prefabs")
        {
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs.Where(p => p.GetComponent<BossStone>() != null))
                output("PREFAB " + prefab.name + " supported=" + string.Join(",", prefab.GetComponent<BossStone>().m_itemStand.m_supportedItems.Select(s => s.name)));
            return;
        }
        if (action == "list")
        {
            stands = UnityEngine.Object.FindObjectsOfType<ItemStand>().OrderBy(s => Vector3.Distance(s.transform.position, player.transform.position)).ToArray();
            for (int i = 0; i < stands.Length; ++i)
            {
                ItemStand stand = stands[i];
                BossStone stone = stand.GetComponentInParent<BossStone>();
                ZNetView view = stand.m_netViewOverride != null ? stand.m_netViewOverride : stand.GetComponent<ZNetView>();
                GameObject prefab = view != null && view.IsValid() ? ZNetScene.instance.GetPrefab(view.GetZDO().GetPrefab()) : null;
                output($"STAND {i} prefab={prefab?.name} boss={stone?.name} pos={stand.transform.position} auto={stand.m_autoAttach} item={stand.GetAttachedItem()} resetLocation={view?.GetZDO()?.GetString("VRN_location", "")} supported={string.Join(",", stand.m_supportedItems.Select(s => s.name))} hover={stand.GetHoverText().Replace("\n", "|")}");
            }
            foreach (Location location in UnityEngine.Object.FindObjectsOfType<Location>().Where(l => Vector3.Distance(l.transform.position, player.transform.position) < 100))
                output($"LOCATION {location.name} pos={location.transform.position} radius={location.m_exteriorRadius}");
            return;
        }
        if (action == "spawn")
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(args[2]);
            Vector3 position = player.transform.position + player.transform.forward * 4;
            position.y = ZoneSystem.instance.GetGroundHeight(position);
            GameObject obj = Instantiate(prefab, position, Quaternion.identity);
            Piece piece = obj.GetComponent<Piece>();
            if (piece != null) piece.SetCreator(player.GetPlayerID(), Splatform.PlatformManager.DistributionPlatform.LocalUser.PlatformUserID);
            output("SPAWN " + obj.name + " " + position);
            return;
        }
        ItemStand target = stands[int.Parse(args[2])];
        if (action == "clear")
        {
            target.DestroyAttachment();
            output("CLEAR " + target.name);
            return;
        }
        if (action == "test") StartCoroutine(Test(target, args[3], output));
    }
    private IEnumerator Suite(Action<string> output)
    {
        failures = 0;
        Player player = Player.m_localPlayer;
        BossStone[] originals = UnityEngine.Object.FindObjectsOfType<BossStone>()
            .Where(s => Utils.GetPrefabName(s.gameObject).StartsWith("BossStone_", StringComparison.Ordinal))
            .OrderBy(s => s.name).ToArray();
        output("SUITE originalCount=" + originals.Length);
        if (originals.Length != 7) { ++failures; output("FAIL expected seven natural temple trophy stones"); }
        foreach (BossStone stone in originals)
        {
            if (stone.m_itemStand.HaveAttachment())
            {
                yield return Test(stone.m_itemStand, "power", output);
                stone.m_itemStand.DestroyAttachment();
                yield return new WaitForSeconds(0.5f);
            }
            yield return Check(stone.m_itemStand, "direct", false, output);
            yield return Check(stone.m_itemStand, "auto", false, output);
        }
        Location temple = UnityEngine.Object.FindObjectsOfType<Location>().First(l => Utils.GetPrefabName(l.gameObject) == "StartTemple");
        GameObject[] clones = ZNetScene.instance.m_prefabs.Where(p => p.GetComponent<BossStone>() != null
            && (p.name.StartsWith("piece_BossStone_", StringComparison.Ordinal) || p.name.StartsWith("VRN_BossStone_", StringComparison.Ordinal))).ToArray();
        foreach (GameObject prefab in clones)
        {
            GameObject obj = Instantiate(prefab, temple.transform.position + Vector3.up, Quaternion.identity);
            yield return new WaitForSeconds(0.5f);
            ItemStand stand = obj.GetComponent<BossStone>().m_itemStand;
            yield return Check(stand, "direct", true, output);
            stand.DestroyAttachment();
            yield return new WaitForSeconds(0.5f);
            yield return Check(stand, "auto", true, output);
            ZNetScene.instance.Destroy(obj);
        }
        GameObject normal = Instantiate(ZNetScene.instance.GetPrefab("itemstand"), temple.transform.position + Vector3.up, Quaternion.identity);
        yield return new WaitForSeconds(0.5f);
        yield return Check(normal.GetComponent<ItemStand>(), "direct", true, output);
        ZNetScene.instance.Destroy(normal);
        output($"SUITE COMPLETE originals={originals.Length} clones={clones.Length} failures={failures}");
    }
    private IEnumerator Check(ItemStand stand, string mode, bool allowed, Action<string> output)
    {
        Player player = Player.m_localPlayer;
        string trophy = stand.m_supportedItems.Count > 0 ? stand.m_supportedItems[0].name : "TrophyDeer";
        string itemName = ObjectDB.instance.GetItemPrefab(trophy).GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
        int before = player.GetInventory().CountItems(itemName);
        yield return Test(stand, mode, output);
        int after = player.GetInventory().CountItems(itemName);
        bool passed = allowed ? stand.HaveAttachment() && after == before : !stand.HaveAttachment() && after == before + 1;
        if (!passed) ++failures;
        output($"{(passed ? "PASS" : "FAIL")} expectedAllowed={allowed} mode={mode} target={stand.transform.root.name} inventoryBeforeAdd={before} after={after} attached={stand.GetAttachedItem()}");
        // Remove only the extra unconsumed trophy created for this check.
        if (!allowed && after > before) player.GetInventory().RemoveItem(itemName, 1);
    }
    private IEnumerator Test(ItemStand stand, string mode, Action<string> output)
    {
        Player player = Player.m_localPlayer;
        player.SetGodMode(true);
        player.SetGhostMode(true);
        if (mode == "power")
        {
            player.SetGuardianPower("");
            bool result = stand.Interact(player, false, false);
            yield return new WaitForSeconds(3);
            output($"POWER result={result} actual={player.GetGuardianPowerName()} expected={stand.m_guardianPower?.name}");
            yield break;
        }
        string trophy = stand.m_supportedItems.Count > 0 ? stand.m_supportedItems[0].name : "TrophyDeer";
        player.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab(trophy), 1);
        ItemDrop.ItemData item = player.GetInventory().GetItem(ObjectDB.instance.GetItemPrefab(trophy).GetComponent<ItemDrop>().m_itemData.m_shared.m_name);
        if (item == null) { output("FAIL inventory could not add " + trophy); yield break; }
        int before = player.GetInventory().CountItems(item.m_shared.m_name);
        int attachmentBefore = stand.GetAttachedItem();
        bool used = mode == "auto" ? stand.Interact(player, false, false) : stand.UseItem(player, item);
        yield return new WaitForSeconds(2);
        int after = player.GetInventory().CountItems(item.m_shared.m_name);
        output($"TEST mode={mode} target={stand.transform.root.name}/{stand.name} trophy={trophy} used={used} before={before} after={after} attachmentBefore={attachmentBefore} attachmentAfter={stand.GetAttachedItem()} hover={stand.GetHoverText().Replace("\n", "|")}");
    }
}
