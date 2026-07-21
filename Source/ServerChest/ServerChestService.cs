using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace PraetorisClient.ServerChestFeature
{
    internal static class ServerChestService
    {
        internal sealed class SendItem
        {
            internal string PrefabName { get; set; } = "";
            internal int Amount { get; set; }
            internal int Quality { get; set; } = 1;
        }

        internal sealed class CommandResult
        {
            internal bool Success { get; set; }
            internal string Message { get; set; } = "";

            internal static CommandResult Ok(string message)
            {
                return new CommandResult { Success = true, Message = message };
            }

            internal static CommandResult Fail(string message)
            {
                return new CommandResult { Success = false, Message = message };
            }
        }

        internal static CommandResult RegisterChest(ZDOID chestId, long sender, string requestedName, string requestedPlatformId)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return CommandResult.Fail("ServerChest registration must run on the server.");
            }

            ZDO? zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(chestId) : null;
            if (zdo == null || !ServerChest.IsServerChestPrefab(zdo))
            {
                return CommandResult.Fail("ServerChest was not found.");
            }

            if (!ServerChestIdentity.TryGetSenderIdentity(sender, requestedName, requestedPlatformId, out string characterName, out string platformId))
            {
                return CommandResult.Fail("Could not resolve player identity.");
            }

            string ownerKey = ServerChest.NormalizeLookup(platformId);
            string existingOwnerKey = ServerChest.OwnerLookup(zdo);
            if (!string.IsNullOrWhiteSpace(existingOwnerKey) && existingOwnerKey != ownerKey)
            {
                return CommandResult.Fail("This ServerChest is already registered to another player.");
            }

            foreach (ZDO existing in ServerChest.FindAllZdos())
            {
                if (existing.m_uid == zdo.m_uid)
                {
                    continue;
                }

                if (ServerChest.OwnerLookup(existing) == ownerKey)
                {
                    return CommandResult.Fail("You already have a registered ServerChest.");
                }
            }

            zdo.SetOwner(ZNet.GetUID());
            ServerChest.SetRegistration(zdo, characterName, platformId);
            ServerChestLog.Debug("register complete owner=" + characterName + " platformId=" + platformId + " zdo=" + zdo.m_uid);
            return CommandResult.Ok("Successfully registered ServerChest for " + characterName);
        }

        internal static CommandResult SendItems(string characterName, IReadOnlyList<SendItem> items)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return CommandResult.Fail("ServerChest send must run on the server.");
            }

            if (string.IsNullOrWhiteSpace(characterName))
            {
                return CommandResult.Fail("Character name is required.");
            }

            if (items.Count == 0)
            {
                return CommandResult.Fail("At least one item is required.");
            }

            CommandResult validation = ValidateItems(items);
            if (!validation.Success)
            {
                return validation;
            }

            CommandResult lookup = FindOneByCharacterName(characterName, out ZDO? zdo);
            if (!lookup.Success || zdo == null)
            {
                ServerChestLog.Debug("send lookup failed owner=" + characterName + " message=" + lookup.Message);
                return lookup;
            }

            Inventory inventory = ServerChest.LoadInventoryFromZdo(zdo);
            ServerChestLog.Debug("send start owner=" + characterName + " zdo=" + zdo.m_uid + " requestItems=" + DescribeSendItems(items) + " existingStacks=" + inventory.NrOfItems().ToString(CultureInfo.InvariantCulture) + " existingItems=" + inventory.NrOfItemsIncludingStacks().ToString(CultureInfo.InvariantCulture));
            int requiredAdditionalSlots = CountRequiredNewSlots(inventory, items);
            ServerChestLog.Debug("send capacity owner=" + characterName + " requiredAdditionalSlots=" + requiredAdditionalSlots.ToString(CultureInfo.InvariantCulture) + " currentStacks=" + inventory.NrOfItems().ToString(CultureInfo.InvariantCulture) + " maxSlots=" + ServerChest.MaxSlots.ToString(CultureInfo.InvariantCulture));
            if (inventory.NrOfItems() + requiredAdditionalSlots > ServerChest.MaxSlots)
            {
                return CommandResult.Fail("ServerChest does not have enough delivery capacity for this request.");
            }

            zdo.SetOwner(ZNet.GetUID());
            bool addFailed = false;
            string addFailure = "";
            ServerChest.WithSuppressedInventoryChanged(() =>
            {
                ServerChest.ApplyMaxInventoryShape(inventory);
                foreach (SendItem item in items)
                {
                    if (!TryAddItemAmount(inventory, item, out string error))
                    {
                        addFailed = true;
                        addFailure = error;
                        return;
                    }
                }

                if (!addFailed)
                {
                    ServerChest.SaveInventoryToZdo(zdo, inventory);
                }
            });

            if (addFailed)
            {
                ServerChestLog.Debug("send failed owner=" + characterName + " zdo=" + zdo.m_uid + " error=" + addFailure);
                return CommandResult.Fail("ServerChest delivery failed while adding items: " + addFailure + " No delivery was saved.");
            }

            int totalAmount = items.Sum(item => item.Amount);
            ServerChestLog.Debug("send complete owner=" + ServerChest.OwnerName(zdo) + " zdo=" + zdo.m_uid + " delivered=" + totalAmount.ToString(CultureInfo.InvariantCulture) + " finalStacks=" + inventory.NrOfItems().ToString(CultureInfo.InvariantCulture) + " finalItems=" + inventory.NrOfItemsIncludingStacks().ToString(CultureInfo.InvariantCulture));
            return CommandResult.Ok("Delivered " + totalAmount.ToString(CultureInfo.InvariantCulture) + " item(s) to ServerChest for " + ServerChest.OwnerName(zdo) + ".");
        }

        internal static CommandResult Status(string characterName)
        {
            CommandResult lookup = FindOneByCharacterName(characterName, out ZDO? zdo);
            if (!lookup.Success || zdo == null)
            {
                return lookup;
            }

            Inventory inventory = ServerChest.LoadInventoryFromZdo(zdo);
            int stackCount = inventory.NrOfItems();
            int itemCount = inventory.NrOfItemsIncludingStacks();
            int width = stackCount <= 0 ? 0 : Math.Min(ServerChest.MaxColumns, stackCount);
            int height = stackCount <= 0 ? 0 : Math.Max(1, (int)Math.Ceiling(stackCount / (double)width));
            Vector3 position = zdo.GetPosition();
            string message =
                "ServerChest owner=" + ServerChest.OwnerName(zdo) +
                " platformId=" + ServerChest.OwnerPlatformId(zdo) +
                " zdo=" + zdo.m_uid +
                " position=" + position.x.ToString("0.#", CultureInfo.InvariantCulture) + "," +
                position.y.ToString("0.#", CultureInfo.InvariantCulture) + "," +
                position.z.ToString("0.#", CultureInfo.InvariantCulture) +
                " items=" + itemCount.ToString(CultureInfo.InvariantCulture) +
                " stacks=" + stackCount.ToString(CultureInfo.InvariantCulture) +
                " grid=" + width.ToString(CultureInfo.InvariantCulture) + "x" + height.ToString(CultureInfo.InvariantCulture);
            ServerChestLog.Debug("status result query=" + characterName + " message=" + message);
            return CommandResult.Ok(message);
        }

        internal static CommandResult Find(string characterName)
        {
            string lookup = ServerChest.NormalizeLookup(characterName);
            List<ZDO> matches = ServerChest.FindAllZdos()
                .Where(zdo => string.IsNullOrWhiteSpace(lookup) || ServerChest.OwnerNameLookup(zdo).Contains(lookup))
                .ToList();
            ServerChestLog.Debug("find query=" + characterName + " normalized=" + lookup + " matches=" + matches.Count.ToString(CultureInfo.InvariantCulture));

            if (matches.Count == 0)
            {
                return CommandResult.Fail("No registered ServerChest found for " + characterName + ".");
            }

            List<string> lines = new();
            foreach (ZDO zdo in matches)
            {
                Vector3 position = zdo.GetPosition();
                lines.Add(
                    ServerChest.OwnerName(zdo) +
                    " platformId=" + ServerChest.OwnerPlatformId(zdo) +
                    " zdo=" + zdo.m_uid +
                    " position=" + position.x.ToString("0.#", CultureInfo.InvariantCulture) + "," +
                    position.y.ToString("0.#", CultureInfo.InvariantCulture) + "," +
                    position.z.ToString("0.#", CultureInfo.InvariantCulture));
            }

            return CommandResult.Ok(string.Join("\n", lines));
        }

        internal static CommandResult FindOneByCharacterName(string characterName, out ZDO? zdo)
        {
            zdo = null;
            string lookup = ServerChest.NormalizeLookup(characterName);
            List<ZDO> allChests = ServerChest.FindAllZdos();
            foreach (ZDO candidate in allChests)
            {
                ServerChestLog.Debug("scan zdo=" + candidate.m_uid + " owner=" + ServerChest.OwnerName(candidate) + " ownerLookup=" + ServerChest.OwnerNameLookup(candidate) + " platformId=" + ServerChest.OwnerPlatformId(candidate) + " dataLength=" + candidate.GetString(ZDOVars.s_items).Length.ToString(CultureInfo.InvariantCulture));
            }

            List<ZDO> matches = allChests
                .Where(candidate => ServerChest.OwnerNameLookup(candidate) == lookup)
                .ToList();
            ServerChestLog.Debug("lookup owner=" + characterName + " normalized=" + lookup + " allChests=" + allChests.Count.ToString(CultureInfo.InvariantCulture) + " matches=" + matches.Count.ToString(CultureInfo.InvariantCulture));

            if (matches.Count == 0)
            {
                return CommandResult.Fail("No registered ServerChest found for " + characterName + ".");
            }

            if (matches.Count > 1)
            {
                return CommandResult.Fail("Multiple ServerChests match " + characterName + "; use serverchest_find to resolve.");
            }

            zdo = matches[0];
            return CommandResult.Ok("");
        }

        internal static CommandResult ValidateItems(IReadOnlyList<SendItem> items)
        {
            foreach (SendItem item in items)
            {
                if (string.IsNullOrWhiteSpace(item.PrefabName))
                {
                    return CommandResult.Fail("Item prefab is required.");
                }

                if (item.Amount <= 0)
                {
                    return CommandResult.Fail("Amount must be greater than zero for " + item.PrefabName + ".");
                }

                GameObject? prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(item.PrefabName) : null;
                if (prefab == null)
                {
                    return CommandResult.Fail("Item prefab not found: " + item.PrefabName + ".");
                }

                ItemDrop itemDrop = prefab.GetComponent<ItemDrop>();
                if (itemDrop == null)
                {
                    return CommandResult.Fail("Prefab is not an item: " + item.PrefabName + ".");
                }

                int maxQuality = Math.Max(1, itemDrop.m_itemData.m_shared.m_maxQuality);
                if (item.Quality < 1 || item.Quality > maxQuality)
                {
                    return CommandResult.Fail("Quality for " + item.PrefabName + " must be between 1 and " + maxQuality.ToString(CultureInfo.InvariantCulture) + ".");
                }
            }

            return CommandResult.Ok("");
        }

        private static int CountRequiredNewSlots(Inventory inventory, IReadOnlyList<SendItem> items)
        {
            int required = 0;
            Dictionary<string, int> freeStackSpace = new();
            foreach (ItemDrop.ItemData existing in inventory.GetAllItems())
            {
                string key = StackKey(existing.m_shared.m_name, existing.m_quality, existing.m_worldLevel);
                int free = Math.Max(0, existing.m_shared.m_maxStackSize - existing.m_stack);
                if (!freeStackSpace.ContainsKey(key))
                {
                    freeStackSpace[key] = 0;
                }

                freeStackSpace[key] += free;
            }

            foreach (SendItem item in items)
            {
                GameObject prefab = ObjectDB.instance.GetItemPrefab(item.PrefabName);
                ItemDrop itemDrop = prefab.GetComponent<ItemDrop>();
                int maxStack = Math.Max(1, itemDrop.m_itemData.m_shared.m_maxStackSize);
                int worldLevel = (int)(byte)Game.m_worldLevel;
                string key = StackKey(itemDrop.m_itemData.m_shared.m_name, item.Quality, worldLevel);
                int remaining = item.Amount;
                if (freeStackSpace.TryGetValue(key, out int free) && free > 0)
                {
                    int used = Math.Min(remaining, free);
                    remaining -= used;
                    freeStackSpace[key] = free - used;
                }

                if (remaining > 0)
                {
                    int newSlots = (int)Math.Ceiling(remaining / (double)maxStack);
                    required += newSlots;
                    freeStackSpace[key] = newSlots * maxStack - remaining;
                }
            }

            return required;
        }

        private static string StackKey(string sharedName, int quality, int worldLevel)
        {
            return sharedName + "|" + quality.ToString(CultureInfo.InvariantCulture) + "|" + worldLevel.ToString(CultureInfo.InvariantCulture);
        }

        private static bool TryAddItemAmount(Inventory inventory, SendItem item, out string error)
        {
            error = "";
            GameObject prefab = ObjectDB.instance.GetItemPrefab(item.PrefabName);
            ItemDrop itemDrop = prefab.GetComponent<ItemDrop>();
            string sharedName = itemDrop.m_itemData.m_shared.m_name;
            int maxStack = Math.Max(1, itemDrop.m_itemData.m_shared.m_maxStackSize);
            int worldLevel = (int)(byte)Game.m_worldLevel;
            int beforeAmount = CountMatchingAmount(inventory, sharedName, item.Quality, worldLevel);
            int remaining = item.Amount;
            int stackIndex = 0;

            while (remaining > 0)
            {
                int stackAmount = Math.Min(remaining, maxStack);
                int chunkBeforeAmount = CountMatchingAmount(inventory, sharedName, item.Quality, worldLevel);
                ItemDrop.ItemData added = inventory.AddItem(item.PrefabName, stackAmount, item.Quality, 0, 0L, "");
                int chunkAfterAmount = CountMatchingAmount(inventory, sharedName, item.Quality, worldLevel);
                int delta = chunkAfterAmount - chunkBeforeAmount;
                ServerChestLog.Debug("add stack prefab=" + item.PrefabName + " quality=" + item.Quality.ToString(CultureInfo.InvariantCulture) + " requested=" + stackAmount.ToString(CultureInfo.InvariantCulture) + " delta=" + delta.ToString(CultureInfo.InvariantCulture) + " stackIndex=" + stackIndex.ToString(CultureInfo.InvariantCulture) + " stacksNow=" + inventory.NrOfItems().ToString(CultureInfo.InvariantCulture));

                if (added == null || delta != stackAmount)
                {
                    error = "expected to add " + stackAmount.ToString(CultureInfo.InvariantCulture) + " " + item.PrefabName + " but added " + delta.ToString(CultureInfo.InvariantCulture) + ".";
                    return false;
                }

                remaining -= stackAmount;
                stackIndex++;
            }

            int afterAmount = CountMatchingAmount(inventory, sharedName, item.Quality, worldLevel);
            int totalDelta = afterAmount - beforeAmount;
            if (totalDelta != item.Amount)
            {
                error = "expected total " + item.Amount.ToString(CultureInfo.InvariantCulture) + " " + item.PrefabName + " but added " + totalDelta.ToString(CultureInfo.InvariantCulture) + ".";
                return false;
            }

            return true;
        }

        private static int CountMatchingAmount(Inventory inventory, string sharedName, int quality, int worldLevel)
        {
            int amount = 0;
            foreach (ItemDrop.ItemData existing in inventory.GetAllItems())
            {
                if (existing.m_shared.m_name == sharedName && existing.m_quality == quality && existing.m_worldLevel == worldLevel)
                {
                    amount += existing.m_stack;
                }
            }

            return amount;
        }

        private static string DescribeSendItems(IReadOnlyList<SendItem> items)
        {
            List<string> parts = new();
            foreach (SendItem item in items)
            {
                parts.Add(item.PrefabName + ":" + item.Amount.ToString(CultureInfo.InvariantCulture) + ":q" + item.Quality.ToString(CultureInfo.InvariantCulture));
            }

            return string.Join(",", parts);
        }
    }
}
