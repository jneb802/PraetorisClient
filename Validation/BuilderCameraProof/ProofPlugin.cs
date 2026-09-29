using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx;
using PraetorisClient;
using PraetorisClient.BuilderCameraFeature;
using UnityEngine;

// Live-test helper only. Never include this assembly in a release.
[BepInPlugin("praetoris.validation.buildercamera", "Builder camera proof helper", "1.0.0")]
[BepInDependency("warpalicious.PraetorisClient")]
public sealed class ProofPlugin : BaseUnityPlugin
{
    private static readonly Type CameraType = typeof(PraetorisClientPlugin).Assembly.GetType("PraetorisClient.BuilderCameraFeature.BuilderCamera");
    private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private void Awake()
    {
        new Terminal.ConsoleCommand("builderproof", "Live-test helper: status, refuel, unequip <prefab>, aim <x> <y> <z>, damage, purchase, emptywood", Run, isCheat: true);
    }

    private static void Run(Terminal.ConsoleEventArgs args)
    {
        Player player = Player.m_localPlayer;
        if (!player) { args.Context.AddString("No player."); return; }
        BuilderWard ward = UnityEngine.Object.FindObjectsByType<BuilderWard>(FindObjectsSortMode.None)
            .Where(w => w.GetComponent<ZNetView>() && w.GetComponent<ZNetView>().IsValid())
            .OrderBy(w => Vector3.Distance(w.transform.position, player.transform.position)).FirstOrDefault();
        string action = args.Length > 1 ? args[1] : "status";
        if (action == "reload") PraetorisClientPlugin.Instance!.Config.Reload();
        else if (action == "refuel") args.Context.AddString("Refuel accepted=" + (ward && ward.Interact(player, false, false)));
        else if (action == "protect")
        {
            Vector3 position = new Vector3(float.Parse(args[2], CultureInfo.InvariantCulture), float.Parse(args[3], CultureInfo.InvariantCulture), float.Parse(args[4], CultureInfo.InvariantCulture));
            GameObject guard = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab("guard_stone"), position, Quaternion.identity);
            guard.GetComponent<Piece>().SetCreator(-12345, default);
            guard.GetComponent<PrivateArea>().m_radius = 3f;
            guard.GetComponent<ZNetView>().GetZDO().Set(ZDOVars.s_enabled, true);
        }
        else if (action == "unequip")
        {
            ItemDrop.ItemData item = player.GetInventory().GetAllItems().Find(i => i.m_dropPrefab && i.m_dropPrefab.name == args[2]);
            if (item != null) player.UnequipItem(item, false);
        }
        else if (action == "aim")
        {
            Vector3 target = new Vector3(float.Parse(args[2], CultureInfo.InvariantCulture), float.Parse(args[3], CultureInfo.InvariantCulture), float.Parse(args[4], CultureInfo.InvariantCulture));
            Quaternion rotation = Quaternion.LookRotation(target - GameCamera.instance.transform.position);
            CameraType.GetField("_rotation", StaticFlags).SetValue(null, rotation);
            GameCamera.instance.transform.rotation = rotation;
            player.SetLookDir(rotation * Vector3.forward);
            typeof(Player).GetField("m_lookPitch", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).SetValue(player,
                rotation.eulerAngles.x > 180f ? rotation.eulerAngles.x - 360f : rotation.eulerAngles.x);
        }
        else if (action == "damage")
        {
            HitData hit = new HitData();
            hit.m_damage.m_damage = 1f;
            player.ApplyDamage(hit, true, true);
        }
        else if (action == "purchase")
        {
            Trader trader = UnityEngine.Object.FindObjectsByType<Trader>(FindObjectsSortMode.None).OrderBy(t => Vector3.Distance(t.transform.position, player.transform.position)).First();
            Trader.TradeItem item = trader.GetAvailableItems().Single(i => i.m_prefab && i.m_prefab.name == "PraetorisBuilderBelt");
            trader.Interact(player, false, false);
            typeof(StoreGui).GetField("m_selectedItem", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).SetValue(StoreGui.instance, item);
            StoreGui.instance.OnBuyItem();
            args.Context.AddString("Purchased through StoreGui.OnBuyItem; price=" + item.m_price);
        }
        else if (action == "emptywood") player.GetInventory().RemoveItem("$item_wood", 10000);
        args.Context.AddString(CameraType.GetMethod("Status", StaticFlags).Invoke(null, null).ToString());
        args.Context.AddString($"Health={player.GetHealth():0.00} noCost={player.NoCostCheat()} eye={player.m_eye.position} camera={GameCamera.instance.transform.position} forward={GameCamera.instance.transform.forward}");
        if (ward) args.Context.AddString($"Ward={ward.transform.position} fuel={ward.GetComponent<ZNetView>().GetZDO().GetFloat("praetoris_builder_fuel"):0.000000} {ward.GetHoverText().Replace('\n', ' ')}");
        foreach (ItemDrop.ItemData item in player.GetInventory().GetAllItems())
            args.Context.AddString($"ITEM {item.m_dropPrefab?.name} count={item.m_stack} equipped={item.m_equipped}");
    }
}
