using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PraetorisClient.BodyBounties;

internal static class BountyClientTransactions
{
    internal static void Apply(BountyMessage message)
    {
        Player player = Player.m_localPlayer;
        BountyTransaction transaction = message.Transaction!;
        if (player == null || player.GetPlayerID() != transaction.Player || message.Records.Count != 1) return;
        string receiptKey = "Praetoris.BountyReceipt." + message.World + "." + transaction.Id;
        if (!player.m_customData.TryGetValue(receiptKey, out string receipt))
        {
            Inventory inventory = player.GetInventory();
            // Inventory mods can enforce different slots on the player inventory
            // than on an ordinary copy. Apply against the real inventory and keep
            // the original references so rollback also preserves equipment links.
            List<ItemDrop.ItemData> originalItems = inventory.GetAllItems().ToList();
            int[] originalStacks = originalItems.Select(item => item.m_stack).ToArray();
            Vector2i[] originalPositions = originalItems.Select(item => item.m_gridPos).ToArray();
            void Restore()
            {
                inventory.GetAllItems().Clear();
                for (int index = 0; index < originalItems.Count; index++)
                {
                    ItemDrop.ItemData item = originalItems[index];
                    item.m_stack = originalStacks[index];
                    item.m_gridPos = originalPositions[index];
                    inventory.GetAllItems().Add(item);
                }
                inventory.Changed();
            }
            bool fits;
            try { fits = ApplyTo(inventory, transaction, message.Records[0]); }
            catch { Restore(); throw; }
            if (!fits) Restore();
            receipt = fits ? "ok" : "failed";
            player.m_customData[receiptKey] = receipt;
        }
        // Inventory and receipt are serialized in the same character save. Do not
        // acknowledge success until the save reports success. A repeated offer
        // retries the save rather than applying the inventory operation again.
        PlayerProfile profile = Game.instance.GetPlayerProfile();
        profile.SavePlayerData(player);
        if (!profile.Save())
            throw new InvalidOperationException("Character save failed. Reopen the bounty page to retry the pending transaction.");
        BountyService.Request(new BountyMessage { Action = "receipt", Id = transaction.Id, Success = receipt == "ok" });
        if (receipt != "ok") player.Message(MessageHud.MessageType.Center,
            "Bounty transaction could not complete. Check inventory space, coins, and the required parcel.");
    }

    private static bool ApplyTo(Inventory inventory, BountyTransaction transaction, BountyRecord record)
    {
        switch (transaction.Kind)
        {
            case "reserve":
                List<ItemDrop.ItemData> coins = inventory.GetAllItems().Where(item => item.m_dropPrefab != null && item.m_dropPrefab.name == "Coins").ToList();
                if (coins.Sum(item => (long)item.m_stack) < transaction.Coins) return false;
                int remaining = transaction.Coins;
                foreach (ItemDrop.ItemData coin in coins)
                {
                    int amount = Math.Min(remaining, coin.m_stack);
                    inventory.RemoveItem(coin, amount);
                    remaining -= amount;
                    if (remaining == 0) break;
                }
                return remaining == 0;
            case "parcel": return inventory.AddItem(BountyInventory.Parcel(record));
            case "refund": return AddCoins(inventory, transaction.Coins);
            case "deliver":
                ItemDrop.ItemData? parcel = inventory.GetAllItems().FirstOrDefault(item => BountyInventory.IsParcel(item) && item.m_customData[BountyInventory.ParcelKey] == record.Parcel);
                if (parcel == null) return false;
                inventory.RemoveItem(parcel);
                return AddCoins(inventory, transaction.Coins);
            case "reclaim":
                Inventory contents = BountyInventory.Load(transaction.Items);
                foreach (ItemDrop.ItemData item in contents.GetAllItems())
                    if (!inventory.AddItem(item.Clone())) return false;
                return true;
            default: throw new InvalidOperationException("Unsupported bounty transaction.");
        }
    }

    private static bool AddCoins(Inventory inventory, int count)
    {
        GameObject prefab = ObjectDB.instance.GetItemPrefab("Coins");
        if (prefab == null || count < 0) return false;
        ItemDrop.ItemData prototype = prefab.GetComponent<ItemDrop>().m_itemData;
        while (count > 0)
        {
            ItemDrop.ItemData item = prototype.Clone();
            item.m_dropPrefab = prefab;
            item.m_stack = Math.Min(count, prototype.m_shared.m_maxStackSize);
            count -= item.m_stack;
            if (!inventory.AddItem(item)) return false;
        }
        return true;
    }
}
