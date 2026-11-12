using System;
using System.Globalization;
using System.Security.Cryptography;
using HarmonyLib;
using UnityEngine;

namespace PraetorisClient.BodyBounties;

internal static class BountyInventory
{
    internal const string ParcelPrefab = "Praetoris_BodyParcel";
    internal const string ParcelKey = "Praetoris.BountyParcel";
    internal const string WeightKey = "Praetoris.BountyWeight";
    internal const string OwnerKey = "Praetoris.BountyOwner";
    internal const string RewardKey = "Praetoris.BountyReward";

    internal static string Save(Inventory inventory)
    {
        ZPackage package = new ZPackage();
        inventory.Save(package);
        return Convert.ToBase64String(package.GetArray());
    }

    internal static Inventory Load(string contents)
    {
        Inventory inventory = new Inventory("Bounty contents", null, 8, 32);
        if (!string.IsNullOrEmpty(contents))
        {
            byte[] bytes = Convert.FromBase64String(contents);
            ZPackage header = new ZPackage(bytes);
            int version = header.ReadInt();
            int count = (global::Version.Item)version >= global::Version.Item.Smaller ? header.ReadUShort() : header.ReadInt();
            inventory.Load(new ZPackage(bytes));
            if (inventory.NrOfItems() != count)
                throw new InvalidOperationException("An item prefab is missing. Contents remain in storage until the mod is restored.");
        }
        return inventory;
    }

    internal static string Hash(string contents)
    {
        using SHA256 hash = SHA256.Create();
        return Convert.ToBase64String(hash.ComputeHash(Convert.FromBase64String(contents)));
    }

    internal static ItemDrop.ItemData Parcel(BountyRecord bounty)
    {
        GameObject prefab = ObjectDB.instance.GetItemPrefab(ParcelPrefab);
        if (prefab == null) throw new InvalidOperationException("The parcel item is unavailable.");
        ItemDrop.ItemData item = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
        item.m_dropPrefab = prefab;
        item.m_stack = 1;
        item.m_customData[ParcelKey] = bounty.Parcel;
        item.m_customData[WeightKey] = bounty.Weight.ToString("R", CultureInfo.InvariantCulture);
        item.m_customData[OwnerKey] = bounty.OwnerName;
        item.m_customData[RewardKey] = bounty.Reward.ToString(CultureInfo.InvariantCulture);
        return item;
    }

    internal static bool IsParcel(ItemDrop.ItemData item) =>
        item.m_dropPrefab != null && item.m_dropPrefab.name == ParcelPrefab && item.m_customData.ContainsKey(ParcelKey);

}

// Inventory.Load can reconstruct ItemData without instantiating an ItemDrop.
// Read the instance metadata at the point of use; never change SharedData.
[HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetWeight))]
internal static class BountyParcelWeight
{
    private static bool Prefix(ItemDrop.ItemData __instance, ref float __result)
    {
        if (!BountyInventory.IsParcel(__instance)) return true;
        __result = __instance.m_customData.TryGetValue(BountyInventory.WeightKey, out string value) &&
            float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float weight) &&
            !float.IsNaN(weight) && !float.IsInfinity(weight) && weight >= 0 ? weight : float.MaxValue;
        return false;
    }
}

public sealed class BountyParcel : MonoBehaviour
{
    internal static Sprite CreateIcon()
    {
        const int size = 96;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = "BodyParcelIcon";
        Color[] pixels = new Color[size * size];
        for (int y = 12; y < 83; y++)
            for (int x = 12; x < 84; x++)
            {
                float edge = Mathf.Abs(x - 47.5f);
                float halfWidth = y > 70 ? 24f : 36f - Mathf.Abs(y - 40f) * 0.15f;
                if (edge > halfWidth) continue;
                Color cloth = Color.Lerp(new Color(0.35f, 0.19f, 0.08f), new Color(0.78f, 0.61f, 0.34f), 1f - edge / 44f);
                if (Mathf.Abs(x - 48) < 3 || Mathf.Abs(y - 48) < 3) cloth = new Color(0.92f, 0.82f, 0.58f);
                if ((x - 48) * (x - 48) + (y - 48) * (y - 48) < 70) cloth = new Color(0.62f, 0.14f, 0.10f);
                pixels[y * size + x] = cloth;
            }
        texture.SetPixels(pixels);
        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    private void Awake()
    {
        ItemDrop item = GetComponent<ItemDrop>();
        if (item != null) item.m_itemData.m_shared.m_maxStackSize = 1;
    }
}
