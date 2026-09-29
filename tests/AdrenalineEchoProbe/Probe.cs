using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using EpicLoot;
using EpicLoot.ShardStones;
using HarmonyLib;
using UnityEngine;

[BepInPlugin("local.adrenaline.echo.probe", "Adrenaline Echo Probe", "1.0.0")]
[BepInDependency("warpalicious.PraetorisClient")]
public class Probe : BaseUnityPlugin
{
    private static Probe instance;
    private static ItemDrop.ItemData staff, trinket;
    private static string label = "idle";
    private static readonly List<Shot> shots = new List<Shot>();
    private static readonly AccessTools.FieldRef<Player, float> Adrenaline = AccessTools.FieldRefAccess<Player, float>("m_adrenaline");
    private static readonly AccessTools.FieldRef<Projectile, ItemDrop.ItemData> ProjectileWeapon = AccessTools.FieldRefAccess<Projectile, ItemDrop.ItemData>("m_weapon");
    private void Awake()
    {
        instance = this;
        new Harmony("local.adrenaline.echo.probe").PatchAll();
        new Terminal.ConsoleCommand("echo_probe", "Adrenaline Echo live validation", args =>
        {
            try
            {
                string command = args.Length > 1 ? args[1] : "status";
                if (Player.m_localPlayer == null || ZNet.instance == null || !ZNet.instance.IsServer())
                {
                    args.Context.AddString("ECHO_PROBE requires a loaded local proof world.");
                    return;
                }
                if (command == "seed") Seed();
                if (command == "suite")
                {
                    if (staff == null || trinket == null) throw new InvalidOperationException("Run seed successfully before suite.");
                    StartCoroutine(Suite());
                }
                if (command == "fill") { label = "manual"; Player.m_localPlayer.AddAdrenaline(10000f); }
                if (command == "plain") Enchant(false);
                if (command == "enchanted") Enchant(true);
                if (command == "status") Report();
                args.Context.AddString("ECHO_PROBE " + command + " OK");
            }
            catch (Exception e) { Logger.LogError(e); args.Context.AddString("ECHO_PROBE FAIL " + e.Message); }
        });
    }
    private static void Print(string value) => instance.Logger.LogInfo("ECHO_PROBE " + value);
    private static ItemDrop.ItemData Give(string prefab)
    {
        ItemDrop.ItemData item = ObjectDB.instance.GetItemPrefab(prefab).GetComponent<ItemDrop>().m_itemData.Clone();
        item.m_dropPrefab = ObjectDB.instance.GetItemPrefab(prefab);
        item.m_durability = item.GetMaxDurability();
        Player.m_localPlayer.GetInventory().AddItem(item);
        return item;
    }
    private static void Seed()
    {
        Player p = Player.m_localPlayer;
        foreach (string food in new[] { "Salad", "SeekerAspic", "YggdrasilPorridge" })
        {
            ItemDrop.ItemData foodItem = Give(food);
            p.ConsumeItem(p.GetInventory(), foodItem);
        }
        staff = Give("StaffFireball");
        trinket = Give("TrinketBronzeStamina");
        trinket.SaveMagicItem(new MagicItem { Rarity = ItemRarity.Epic, SocketCount = 1 });
        ItemDrop.ItemData shard = Give("1347551238_Epic_ShardStone");
        bool added = ShardSocketManager.AddShard(trinket, shard);
        if (!added) throw new Exception("Cannot socket registered shard: " + trinket.GetTooltip());
        p.GetInventory().RemoveItem(shard);
        p.EquipItem(trinket, false);
        p.EquipItem(staff, false);
        Print("socketed=" + added + " effect=" + trinket.GetMagicItem().Sockets[0].Effect.EffectType +
              " category=" + Shards.GetCFG().Shards[(ShardType)0x50520006].Category +
              " fullSE=" + trinket.m_shared.m_fullAdrenalineSE?.name);
        foreach (ItemRarity rarity in new[] {ItemRarity.Epic,ItemRarity.Legendary,ItemRarity.Mythic,(ItemRarity)5})
            Print("prefab=" + rarity + " present=" + (ObjectDB.instance.GetItemPrefab("1347551238_" + rarity + "_ShardStone") != null));
        Enchant(false);
    }
    private static void Enchant(bool enabled)
    {
        MagicItem magic = new MagicItem { Rarity = ItemRarity.Legendary };
        if (enabled)
        {
            magic.Effects.Add(new MagicItemEffect("ModifyDamage", 100f));
            magic.Effects.Add(new MagicItemEffect("AddFrostDamage", 50f));
            magic.Effects.Add(new MagicItemEffect("ModifyProjectileSpeed", 100f));
        }
        staff.SaveMagicItem(magic);
        Player.m_localPlayer.UnequipItem(staff, false);
        Player.m_localPlayer.EquipItem(staff, false);
        Print("enchant=" + enabled + " damage=" + staff.GetDamage().GetTotalDamage());
    }
    private static void Report()
    {
        Player p = Player.m_localPlayer;
        Print("adrenaline=" + p.GetAdrenaline() + "/" + p.GetMaxAdrenaline() + " weapon=" + p.GetCurrentWeapon()?.m_dropPrefab?.name + " shots=" + shots.Count);
    }
    private IEnumerator Suite()
    {
        Player p = Player.m_localPlayer;
        float plainDamage = 0f;
        float plainSpeed = 0f;
        Dictionary<string, float> doubleConfig = MagicItemEffectDefinitions.AllDefinitions["DoubleMagicShot"].Config;
        bool hadChance = doubleConfig.TryGetValue("Chance", out float originalChance);
        foreach (string test in new[] {"partial", "drain", "plain", "refresh", "enchanted", "staggered", "no-shard", "melee", "unarmed", "no-ammo", "bow", "double-magic"})
        {
            label = test;
            shots.Clear();
            Adrenaline(p) = 0f;
            if (test == "enchanted") Enchant(true);
            if (test == "no-shard")
            {
                trinket.SaveMagicItem(new MagicItem { Rarity = ItemRarity.Epic, SocketCount = 1 });
                p.UnequipItem(trinket, false); p.EquipItem(trinket, false);
            }
            if (test == "melee")
            {
                ItemDrop.ItemData shard = Give("1347551238_Epic_ShardStone");
                if (!ShardSocketManager.AddShard(trinket, shard)) throw new Exception("Restore socket failed");
                p.GetInventory().RemoveItem(shard);
                p.UnequipItem(trinket, false); p.EquipItem(trinket, false);
                p.EquipItem(Give("SwordBronze"), false);
            }
            if (test == "unarmed") p.UnequipItem(p.GetCurrentWeapon(), false);
            if (test == "no-ammo") p.EquipItem(Give("Bow"), false);
            if (test == "bow") Give("ArrowWood");
            if (test == "double-magic")
            {
                staff.SaveMagicItem(new MagicItem { Rarity = ItemRarity.Legendary, Effects = new List<MagicItemEffect> { new MagicItemEffect("DoubleMagicShot", 100f) } });
                p.EquipItem(staff, false);
                doubleConfig["Chance"] = 1f;
            }
            p.AddEitr(1000f);
            p.AddStamina(1000f);
            int ammoBefore = p.GetInventory().GetAllItems().Where(item => item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo).Sum(item => item.m_stack);
            float durability = staff.m_durability, stamina = p.GetStamina(), eitr = p.GetEitr(), health = p.GetHealth();
            UnityEngine.Random.InitState(491);
            if (test == "staggered")
            {
                p.Stagger(Vector3.back);
                yield return new WaitForSeconds(0.2f);
                Print("STAGGER active=" + p.IsStaggering());
            }
            if (test == "partial") p.AddAdrenaline(0.01f);
            else if (test == "drain") {Adrenaline(p) = 10f; p.AddAdrenaline(-10f);}
            else p.AddAdrenaline(10000f);
            yield return new WaitForSeconds(0.2f);
            int expected = test == "double-magic" ? 2 : test == "plain" || test == "refresh" || test == "enchanted" || test == "staggered" || test == "bow" ? 1 : 0;
            bool passed = shots.Count == expected && shots.All(shot => shot.sameWeapon) && Mathf.Approximately(staff.m_durability, durability);
            int ammoAfter = p.GetInventory().GetAllItems().Where(item => item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo).Sum(item => item.m_stack);
            // Food decay lowers the resource caps even when no shot is fired. Check spending
            // against the remaining cap rather than treating that independent change as a cost.
            passed &= ammoBefore == ammoAfter && p.GetEitr() >= Mathf.Min(eitr, p.GetMaxEitr()) - 0.01f &&
                p.GetStamina() >= Mathf.Min(stamina, p.GetMaxStamina()) - 0.01f &&
                p.GetHealth() >= Mathf.Min(health, p.GetMaxHealth()) - 0.01f;
            if (test == "plain" && shots.Count == 1) { plainDamage = shots[0].damage; plainSpeed = shots[0].speed; }
            if (test == "enchanted") passed &= shots.Count == 1 && shots[0].frost > 0f && shots[0].damage > plainDamage && Mathf.Approximately(shots[0].speed, plainSpeed * 2f);
            Print("CASE=" + test + " " + (passed ? "PASS" : "FAIL") + " shots=" + shots.Count + " expected=" + expected +
                  " durabilityDelta=" + (staff.m_durability-durability) + " healthDelta=" + (p.GetHealth()-health) +
                  " staminaDelta=" + (p.GetStamina()-stamina) + " eitrDelta=" + (p.GetEitr()-eitr) + " ammoDelta=" + (ammoAfter-ammoBefore));
            yield return new WaitForSeconds(2f);
        }
        if (hadChance) doubleConfig["Chance"] = originalChance;
        else doubleConfig.Remove("Chance");
        Print("SUITE_DONE");
    }
    private class Shot { public float damage, frost, speed; public bool sameWeapon; }
    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
    private class Capture
    {
        private static void Postfix(Projectile __instance, Character owner)
        {
            if (owner != Player.m_localPlayer) return;
            ItemDrop.ItemData weapon = ProjectileWeapon(__instance);
            float speed = __instance.GetVelocity().magnitude;
            shots.Add(new Shot {
                damage = __instance.m_damage.GetTotalDamage(), frost = __instance.m_damage.m_frost,
                speed = speed,
                sameWeapon = ReferenceEquals(weapon, Player.m_localPlayer.GetCurrentWeapon())
            });
            Print("SHOT case=" + label + " prefab=" + __instance.name + " sameWeapon=" + ReferenceEquals(weapon,Player.m_localPlayer.GetCurrentWeapon()) +
                " damage=" + __instance.m_damage.GetTotalDamage() + " fire=" + __instance.m_damage.m_fire + " frost=" + __instance.m_damage.m_frost +
                " speed=" + speed + " staggering=" + Player.m_localPlayer.IsStaggering() +
                " effects=" + string.Join(",", weapon?.GetMagicItem()?.Effects.Select(e => e.EffectType) ?? Enumerable.Empty<string>()));
        }
    }
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    private class Hit
    {
        private static void Prefix(Character __instance, HitData hit)
        {
            if (hit.GetAttacker() == Player.m_localPlayer && !__instance.IsPlayer())
                Print("HIT case=" + label + " target=" + __instance.name + " fire=" + hit.m_damage.m_fire + " frost=" + hit.m_damage.m_frost);
        }
    }
}
