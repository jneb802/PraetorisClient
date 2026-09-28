using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace PraetorisClient.CommunityChestFeature
{
    internal static class CommunityChestClient
    {
        private static Container? _chest;
        private static string _request = "";
        private static string _operation = "";
        private static bool _busy;
        private static long _world;
        private static long _revision;
        private static int _balance;
        private static float _requestedAt;
        private static string _savedTransfer = "";
        internal static Container? CurrentChest => InventoryGui.instance != null
            ? AccessTools.Field(typeof(InventoryGui), "m_currentContainer").GetValue(InventoryGui.instance) as Container : null;
        internal static bool IsOpen => CommunityChest.Is(CurrentChest!);
        internal static string CoinName => ObjectDB.instance.GetItemPrefab("Coins").GetComponent<ItemDrop>().m_itemData.m_shared.m_name;

        internal static void Open(Container chest)
        {
            if (Player.m_localPlayer == null || Player.m_localPlayer.IsDead() || Player.m_localPlayer.IsTeleporting()) return;
            if (_busy && Time.realtimeSinceStartup - _requestedAt < 10f) { Message("Waiting for the previous transfer."); return; }
            _chest = chest;
            _savedTransfer = "";
            Request("open", Guid.NewGuid().ToString("N"), 0);
        }

        private static void Request(string operation, string transaction, int amount)
        {
            _busy = true;
            _operation = operation;
            _request = transaction;
            _requestedAt = Time.realtimeSinceStartup;
            CommunityChestRpc.Send(operation, _chest != null ? _chest.GetComponent<ZNetView>().GetZDO().m_uid : ZDOID.None, transaction, amount, _revision);
        }

        internal static void Transfer(int amount)
        {
            if (_busy) { Message("Waiting for the server. If the connection was lost, open the chest again."); return; }
            if (!IsOpen || _chest == null || CurrentChest != _chest) return;
            Player player = Player.m_localPlayer;
            if (player == null || player.IsDead() || player.IsTeleporting() || Vector3.Distance(player.transform.position, _chest.transform.position) > 5f) return;
            Inventory inventory = player.GetInventory();
            if (amount == 0 || amount > inventory.CountItems(CoinName) || (long)_balance + amount < 0 || (long)_balance + amount > CommunityChestStore.Capacity)
            { Message("Not enough coins or chest space."); return; }
            if (amount < 0 && !CanReceive(inventory, -amount)) { Message("Your inventory does not have enough space."); return; }
            Request("prepare", Guid.NewGuid().ToString("N"), amount);
        }

        private static bool CanReceive(Inventory inventory, int amount)
        {
            ItemDrop.ItemData coin = ObjectDB.instance.GetItemPrefab("Coins").GetComponent<ItemDrop>().m_itemData;
            int room = inventory.GetEmptySlots() * coin.m_shared.m_maxStackSize;
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
                if (item.m_shared.m_name == CoinName && item.m_quality == 1) room += coin.m_shared.m_maxStackSize - item.m_stack;
            return amount <= room;
        }

        internal static void Receive(ZPackage package)
        {
            string operation = package.ReadString();
            string transaction = package.ReadString();
            string message = package.ReadString();
            long world = package.ReadLong();
            bool success = package.ReadBool();
            if (operation == "place") { Message(message); return; }
            if (!_busy || operation != _operation || transaction != _request) return;
            if (!success) { Fail(message); return; }
            _world = world;
            _balance = package.ReadInt();
            _revision = package.ReadLong();
            string pending = package.ReadString();
            int amount = package.ReadInt();
            if (pending.Length != 0)
            {
                ApplyPrepared(pending, amount);
                return;
            }
            _busy = false;
            if (_chest == null || Player.m_localPlayer == null || Player.m_localPlayer.IsDead()) return;
            Refresh();
            if (operation == "open" && Vector3.Distance(Player.m_localPlayer.transform.position, _chest.transform.position) <= 5f)
            {
                AccessTools.Method(typeof(InventoryGui), "SetupDragItem").Invoke(InventoryGui.instance, new object?[] { null, null, 0 });
                InventoryGui.instance.Show(_chest);
                Message("Only you can access these coins.");
            }
            else if (operation == "commit") Message("Community Chest: " + _balance + " coins stored.");
            PraetorisClientPlugin.Log.LogInfo("CommunityChest client " + operation + " balance=" + _balance + " revision=" + _revision);
        }

        private static void ApplyPrepared(string transaction, int amount)
        {
            Player player = Player.m_localPlayer;
            if (player == null) { Fail("Reconnect with the same character to finish the transfer."); return; }
            string receiptKey = "PraetorisCommunityChest:" + _world;
            bool applied = player.m_customData.TryGetValue(receiptKey, out string receipt) && receipt == transaction;
            if (!applied)
            {
                Inventory inventory = player.GetInventory();
                if (player.IsDead() || player.IsTeleporting() ||
                    (amount > 0 && inventory.CountItems(CoinName) < amount) ||
                    (amount < 0 && !CanReceive(inventory, -amount)))
                {
                    Request("cancel", transaction, 0);
                    Message("Transfer cancelled. Check your coins and inventory space.");
                    return;
                }
                if (amount > 0) inventory.RemoveItem(CoinName, amount);
                else
                {
                    ZPackage before = new ZPackage();
                    inventory.Save(before);
                    if (!AddCoins(inventory, -amount))
                    {
                        before.SetPos(0);
                        inventory.Load(before);
                        Request("cancel", transaction, 0);
                        Message("Transfer cancelled: unable to add coins.");
                        return;
                    }
                }
                player.m_customData[receiptKey] = transaction;
            }
            // Save coins and receipt together. Verify the persisted player bytes before
            // acknowledging the server. A failed save leaves the transfer pending.
            if (_savedTransfer != transaction)
            {
                if (SaveSystem.HasSessionFlag(SaveSystemSessionFlags.DontSaveCharacter)) throw new InvalidOperationException("Character saving is blocked.");
                PlayerProfile profile = Game.instance.GetPlayerProfile();
                profile.SavePlayerData(player);
                profile.SaveLogoutPoint();
                if (!profile.Save()) throw new InvalidOperationException("Character save failed.");
                PlayerProfile persisted = new PlayerProfile(profile.m_filename, profile.m_fileSource);
                if (!persisted.Load() || !((byte[])AccessTools.Field(typeof(PlayerProfile), "m_playerData").GetValue(persisted))
                    .SequenceEqual((byte[])AccessTools.Field(typeof(PlayerProfile), "m_playerData").GetValue(profile)))
                    throw new InvalidOperationException("Character save verification failed.");
                _savedTransfer = transaction;
            }
            Request("commit", transaction, 0);
        }

        private static void Refresh()
        {
            Inventory inventory = _chest!.GetInventory();
            inventory.RemoveAll();
            if (_balance > 0 && !AddCoins(inventory, _balance)) throw new InvalidOperationException("Unable to display stored coins.");
            int slot = 0;
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                item.m_gridPos = new Vector2i(slot % 8, slot / 8);
                slot++;
            }
        }

        private static bool AddCoins(Inventory inventory, int amount)
        {
            int before = inventory.CountItems(CoinName);
            int stackSize = ObjectDB.instance.GetItemPrefab("Coins").GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize;
            for (int remaining = amount; remaining > 0; remaining -= Math.Min(remaining, stackSize))
                if (inventory.AddItem("Coins", Math.Min(remaining, stackSize), 1, 0, 0L, "", cheated: false) == null) return false;
            return inventory.CountItems(CoinName) == before + amount;
        }

        internal static void QuickTransfer(InventoryGrid grid, ItemDrop.ItemData item)
        {
            if (item == null) return;
            if (item.m_shared.m_name != CoinName) { Message("The Community Chest accepts coins only."); return; }
            bool withdraw = grid.GetInventory() == CurrentChest!.GetInventory();
            Transfer((withdraw ? -1 : 1) * item.m_stack);
        }

        internal static void TransferAll(bool deposit)
        {
            if (!IsOpen || Player.m_localPlayer == null) return;
            Transfer(deposit ? Math.Min(Player.m_localPlayer.GetInventory().CountItems(CoinName), CommunityChestStore.Capacity - _balance) : -_balance);
        }

        internal static void Fail(string message)
        {
            _busy = false;
            if (IsOpen) InventoryGui.instance.Hide();
            Message(message);
        }

        internal static void Message(string message)
        {
            if (Player.m_localPlayer != null) Player.m_localPlayer.Message(MessageHud.MessageType.Center, message);
            if (Console.instance != null) Console.instance.AddString(message);
        }

        internal static void RegisterCommands()
        {
            _ = new Terminal.ConsoleCommand("communitychest_place", "Place an admin-only Community Chest near you.", args => CommunityChestRpc.Send("place", ZDOID.None, "", 0, 0), onlyAdmin: true);
            _ = new Terminal.ConsoleCommand("communitychest_open", "Open the nearest Community Chest within 5m.", args =>
            {
                if (Player.m_localPlayer == null) return;
                CommunityChest? nearest = UnityEngine.Object.FindObjectsByType<CommunityChest>(FindObjectsSortMode.None)
                    .Where(chest => Vector3.Distance(chest.transform.position, Player.m_localPlayer.transform.position) <= 5f)
                    .OrderBy(chest => Vector3.Distance(chest.transform.position, Player.m_localPlayer.transform.position)).FirstOrDefault();
                if (nearest == null) Message("No Community Chest within 5m.");
                else nearest.GetComponent<Container>().Interact(Player.m_localPlayer, false, false);
            });
            _ = new Terminal.ConsoleCommand("communitychest_transfer", "Transfer coins in an open Community Chest. Positive amount deposits; negative withdraws.", args =>
            {
                if (args.Length != 2 || !int.TryParse(args[1], out int amount) || amount < -CommunityChestStore.Capacity || amount > CommunityChestStore.Capacity)
                { Message("Usage: communitychest_transfer <amount>"); return; }
                Transfer(amount);
            });
            _ = new Terminal.ConsoleCommand("communitychest_status", "Show your open Community Chest balance and transfer state.", args =>
                args.Context.AddString("CommunityChest open=" + IsOpen + " busy=" + _busy + " balance=" + (IsOpen ? _balance.ToString() : "unavailable") + " revision=" + _revision));
        }
    }
}
