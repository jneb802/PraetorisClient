using System;
using System.Reflection;
using PraetorisClient;
using PraetorisClient.SurtlingBoats;
using UnityEngine;
using UnityEngine.UI;

internal static class Program
{
    private static void Assert(bool condition, string claim)
    {
        if (!condition) { throw new Exception(claim); }
        Console.WriteLine("PASS " + claim);
    }

    private static Ship NewShip(ZDO? data = null)
    {
        GameObject root = new("test ship", typeof(Ship), typeof(Container), typeof(ZNetView));
        if (data != null) { root.GetComponent<ZNetView>()!.Data = data; }
        root.GetComponent<ZNetView>()!.Data.Set(SurtlingBoatFeature.EnabledZdoKey, true);
        return root.GetComponent<Ship>()!;
    }

    private static Inventory InventoryOf(Ship ship) => ship.GetComponent<Container>()!.Inventory;
    private static ZDO DataOf(Ship ship) => ship.GetComponent<ZNetView>()!.Data;

    private static Sprite AddPrefab(string prefab)
    {
        GameObject item = new(prefab, typeof(ItemDrop));
        item.GetComponent<ItemDrop>()!.m_itemData.m_shared.m_name = prefab;
        ObjectDB.instance!.Items[prefab] = item;
        return item.GetComponent<ItemDrop>()!.m_itemData.GetIcon();
    }

    private static Image IconOf(Ship ship)
    {
        Player.m_localPlayer = new Player { ControlledShip = ship };
        SurtlingBoatFeature.Update();
        FieldInfo field = typeof(SurtlingBoatFeature).GetField("_fuelIcon", BindingFlags.Static | BindingFlags.NonPublic)!;
        return ((GameObject)field.GetValue(null)!).GetComponent<Image>()!;
    }

    private static void Main()
    {
        ObjectDB.instance = new ObjectDB();
        Hud.m_instance = new Hud();
        Sprite surtlingIcon = AddPrefab("SurtlingCore");
        Sprite moltenIcon = AddPrefab("MoltenCore");

        Ship both = NewShip();
        InventoryOf(both).Items["SurtlingCore"] = 2;
        InventoryOf(both).Items["MoltenCore"] = 1;
        Assert(IconOf(both).sprite == moltenIcon, "next-fuel icon prefers Molten Core");
        Assert(!SurtlingBoatFeature.TryConsumeFuel(both, Ship.Speed.Stop, out _, out _), "stopped boat does not refuel");
        Assert(InventoryOf(both).Count("MoltenCore") == 1, "stopped boat keeps its core");
        Assert(SurtlingBoatFeature.TryConsumeFuel(both, Ship.Speed.Full, out float seconds, out float boost) && seconds == 300f && boost == 2f,
            "both fuels select Molten Core with 300 seconds and full boost");
        Assert(InventoryOf(both).Count("SurtlingCore") == 2 && InventoryOf(both).Count("MoltenCore") == 0, "refill consumes only one Molten Core");
        Assert(IconOf(both).sprite == moltenIcon, "active Molten Core icon remains after last core leaves inventory");
        foreach ((Ship.Speed speed, float expected) in new[] { (Ship.Speed.Back, 2f), (Ship.Speed.Slow, 1.02f), (Ship.Speed.Half, 1.4f), (Ship.Speed.Full, 2f) })
        {
            Assert(SurtlingBoatFeature.TryConsumeFuel(both, speed, out _, out boost) && boost == expected, "active Molten Core boost at " + speed);
        }
        Assert(InventoryOf(both).Count("SurtlingCore") == 2, "changing speed does not consume another core");

        SurtlingBoatFeature.SetFuelSeconds(both, 123f, true);
        Ship reloaded = NewShip(DataOf(both));
        Assert(SurtlingBoatFeature.TryConsumeFuel(reloaded, Ship.Speed.Half, out seconds, out boost) && seconds == 123f && boost == 1.4f,
            "reloaded boat restores Molten Core type and remaining fuel");
        Assert(IconOf(reloaded).sprite == moltenIcon, "reloaded boat restores Molten Core icon");
        ZNetView remote = reloaded.GetComponent<ZNetView>()!;
        remote.Owner = false;
        Assert(IconOf(reloaded).sprite == moltenIcon, "non-owner reads synchronized Molten Core icon");
        Assert(SurtlingBoatFeature.GetFuelSeconds(reloaded) == 123f, "non-owner reads synchronized fuel seconds");
        remote.Owner = true;
        remote.Data.Owner = 2;
        Assert(SurtlingBoatFeature.TryConsumeFuel(reloaded, Ship.Speed.Back, out seconds, out boost) && seconds == 123f && boost == 2f,
            "new owner restores Molten Core state");

        SurtlingBoatFeature.SetFuelSeconds(both, 0f, true);
        Assert(SurtlingBoatFeature.TryConsumeFuel(both, Ship.Speed.Full, out seconds, out boost) && boost == 1.5f && seconds == 300f,
            "empty Molten Core supply falls back to Surtling Core");
        Assert(IconOf(both).sprite == surtlingIcon, "fallback switches icon to Surtling Core");
        InventoryOf(both).Items["MoltenCore"] = 1;
        Assert(SurtlingBoatFeature.TryConsumeFuel(both, Ship.Speed.Half, out _, out boost) && boost == 1.2f && InventoryOf(both).Count("MoltenCore") == 1,
            "adding Molten Core preserves already burning Surtling Core");
        Assert(IconOf(both).sprite == surtlingIcon, "inventory change does not replace active-fuel icon");

        ZDO legacyData = new();
        legacyData.Set(SurtlingBoatFeature.FuelSecondsZdoKey, 40f);
        Ship legacy = NewShip(legacyData);
        Assert(SurtlingBoatFeature.TryConsumeFuel(legacy, Ship.Speed.Full, out seconds, out boost) && seconds == 40f && boost == 1.5f,
            "old saved fuel without type remains Surtling Core fuel");

        Ship empty = NewShip();
        Assert(!SurtlingBoatFeature.TryConsumeFuel(empty, Ship.Speed.Full, out _, out _), "empty inventory gives no boost");
        Assert(IconOf(empty).sprite == surtlingIcon && IconOf(empty).color.g == 0.3f, "empty motor shows red Surtling Core icon");
        InventoryOf(empty).Items["SurtlingCore"] = 1;
        PraetorisClientPlugin.SurtlingBoatMoltenFuelItemPrefab.Value = "MissingPrefab";
        Assert(SurtlingBoatFeature.TryConsumeFuel(empty, Ship.Speed.Back, out _, out boost) && boost == 1.5f, "missing secondary prefab falls back to Surtling Core");
        PraetorisClientPlugin.SurtlingBoatMoltenFuelItemPrefab.Value = "MoltenCore";

        Ship disabledBoost = NewShip();
        InventoryOf(disabledBoost).Items["MoltenCore"] = 1;
        PraetorisClientPlugin.SurtlingBoatMoltenFullBoost.Value = 0f;
        Assert(!SurtlingBoatFeature.TryConsumeFuel(disabledBoost, Ship.Speed.Full, out _, out _) && InventoryOf(disabledBoost).Count("MoltenCore") == 1,
            "zero boost does not consume fuel");
        PraetorisClientPlugin.SurtlingBoatMoltenFullBoost.Value = 2f;
        PraetorisClientPlugin.SurtlingBoatMoltenSecondsPerFuelItem.Value = 0f;
        InventoryOf(disabledBoost).Items["SurtlingCore"] = 1;
        Assert(SurtlingBoatFeature.TryConsumeFuel(disabledBoost, Ship.Speed.Full, out _, out boost) && boost == 1.5f,
            "disabled Molten Core duration falls back to primary fuel");
        PraetorisClientPlugin.SurtlingBoatMoltenSecondsPerFuelItem.Value = 300f;

        Ship free = NewShip();
        InventoryOf(free).Items["MoltenCore"] = 1;
        PraetorisClientPlugin.SurtlingBoatFreeFuel.Value = true;
        Assert(SurtlingBoatFeature.TryConsumeFuel(free, Ship.Speed.Full, out seconds, out boost) && float.IsPositiveInfinity(seconds) && boost == 1.5f && InventoryOf(free).Count("MoltenCore") == 1,
            "free-fuel mode keeps original boost and consumes no core");
        Assert(IconOf(free).sprite == surtlingIcon, "free-fuel mode keeps primary icon");
        SurtlingBoatFeature.Shutdown();
    }
}
