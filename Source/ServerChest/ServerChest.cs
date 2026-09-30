using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace PraetorisClient.ServerChestFeature
{
    internal sealed class ServerChest : MonoBehaviour
    {
        internal const string PrefabName = "ServerChest";
        internal const string OwnerNameKey = "ServerChestOwnerName";
        internal const string OwnerNameLookupKey = "ServerChestOwnerNameKey";
        internal const string OwnerPlatformIdKey = "ServerChestOwnerPlatformId";
        internal const string OwnerLookupKey = "ServerChestOwnerKey";
        internal const int Columns = 8;

        private static readonly Dictionary<Inventory, ServerChest> InventoryOwners = new();
        private static readonly FieldInfoWrapper<int> InventoryWidth = new(typeof(Inventory), "m_width");
        private static readonly FieldInfoWrapper<int> InventoryHeight = new(typeof(Inventory), "m_height");

        private Container? _container;
        private Inventory? _inventory;
        private ZNetView? _nview;
        internal byte[]? LoadedItemData;
        private static readonly System.Reflection.MethodInfo LoadContainer = AccessTools.Method(typeof(Container), "Load");

        internal static bool PrepareWithdrawal(Inventory inventory, ItemDrop.ItemData? item = null)
        {
            if (inventory == null || !TryGetByInventory(inventory, out ServerChest chest))
                return true;

            // A delivery can update the ZDO before Container's next periodic load.
            // Refresh before any transfer can save the previous inventory over it.
            if (chest._nview == null || !chest._nview.IsValid() || !chest._nview.IsOwner())
                return false;
            try
            {
                LoadContainer.Invoke(chest._container, null);
            }
            catch (Exception exception)
            {
                ServerChestLog.Warning("Cannot refresh chest before withdrawal: " + exception.GetBaseException().Message);
                ShowMessage("ServerChest could not load. Please try again.");
                return false;
            }

            // Loading replaces ItemData instances. Never transfer an old drag or
            // click selection: it could duplicate an item or overwrite new items.
            if (item != null && !inventory.ContainsItem(item))
            {
                ShowMessage("ServerChest contents changed. Please select the item again.");
                return false;
            }
            return true;
        }

        private void Awake()
        {
            _container = GetComponent<Container>();
            _nview = GetComponent<ZNetView>();
            WearNTear wearNTear = GetComponent<WearNTear>();
            if (wearNTear != null)
            {
                wearNTear.m_onDestroyed += OnDestroyed;
            }

            InvokeRepeating(nameof(RefreshInventoryRegistration), 0.1f, 0.5f);
        }

        private void OnDestroy()
        {
            if (_inventory != null)
            {
                InventoryOwners.Remove(_inventory);
            }
        }

        private void RefreshInventoryRegistration()
        {
            if (_container == null)
            {
                _container = GetComponent<Container>();
            }

            Inventory? inventory = _container != null ? _container.GetInventory() : null;
            if (inventory == null || ReferenceEquals(inventory, _inventory))
            {
                return;
            }

            if (_inventory != null)
            {
                InventoryOwners.Remove(_inventory);
            }

            _inventory = inventory;
            InventoryOwners[inventory] = this;
            ResizeToFit(inventory);
            ServerChestLog.Debug("registered live inventory zdo=" + GetZdoId() + " items=" + inventory.NrOfItems().ToString(CultureInfo.InvariantCulture));
        }

        private void OnDestroyed()
        {
            ZDO? zdo = _nview != null && _nview.IsValid() ? _nview.GetZDO() : null;
            if (zdo == null || !_nview!.IsOwner())
            {
                return;
            }

            ClearRegistration(zdo);
        }

        internal void OpenRegistrationPanel()
        {
            ServerChestRegistrationPanel.Open(this);
        }

        internal ZDOID GetZdoId()
        {
            ZDO? zdo = _nview != null && _nview.IsValid() ? _nview.GetZDO() : null;
            return zdo != null ? zdo.m_uid : ZDOID.None;
        }

        internal string GetRegistrationPreview()
        {
            string characterName = Game.instance != null && Game.instance.GetPlayerProfile() != null
                ? Game.instance.GetPlayerProfile().GetName()
                : Player.m_localPlayer != null
                    ? Player.m_localPlayer.GetPlayerName()
                    : "";
            string platformId = ServerChestIdentity.GetLocalPlatformId();
            return characterName + ":" + platformId;
        }

        internal void RequestRegistration()
        {
            ZDOID zdoId = GetZdoId();
            if (zdoId.IsNone())
            {
                ShowMessage("ServerChest is not ready.");
                return;
            }

            string characterName = Game.instance != null && Game.instance.GetPlayerProfile() != null
                ? Game.instance.GetPlayerProfile().GetName()
                : Player.m_localPlayer != null
                    ? Player.m_localPlayer.GetPlayerName()
                    : "";
            string platformId = ServerChestIdentity.GetLocalPlatformId();
            ServerChestRpc.RequestRegistration(zdoId, characterName, platformId);
        }

        internal static bool TryGetByInventory(Inventory inventory, out ServerChest serverChest)
        {
            return InventoryOwners.TryGetValue(inventory, out serverChest);
        }

        internal static bool IsServerChestPrefab(ZDO zdo)
        {
            return zdo != null && zdo.GetPrefab() == PrefabName.GetStableHashCode();
        }

        internal static bool IsServerChest(Container container)
        {
            return container != null && container.GetComponent<ServerChest>() != null;
        }

        internal static string NormalizeLookup(string value)
        {
            return (value ?? "").Trim().ToLowerInvariant();
        }

        internal static void SetRegistration(ZDO zdo, string ownerName, string ownerPlatformId)
        {
            string trimmedName = (ownerName ?? "").Trim();
            string trimmedPlatformId = (ownerPlatformId ?? "").Trim();
            zdo.Set(OwnerNameKey, trimmedName);
            zdo.Set(OwnerNameLookupKey, NormalizeLookup(trimmedName));
            zdo.Set(OwnerPlatformIdKey, trimmedPlatformId);
            zdo.Set(OwnerLookupKey, NormalizeLookup(trimmedPlatformId));
            ServerChestLog.Debug("set registration zdo=" + zdo.m_uid + " owner=" + trimmedName + " platformId=" + trimmedPlatformId);
        }

        internal static void ClearRegistration(ZDO zdo)
        {
            zdo.Set(OwnerNameKey, "");
            zdo.Set(OwnerNameLookupKey, "");
            zdo.Set(OwnerPlatformIdKey, "");
            zdo.Set(OwnerLookupKey, "");
            ServerChestLog.Debug("cleared registration zdo=" + zdo.m_uid);
        }

        internal static string OwnerName(ZDO zdo)
        {
            return zdo.GetString(OwnerNameKey);
        }

        internal static string OwnerNameLookup(ZDO zdo)
        {
            return zdo.GetString(OwnerNameLookupKey);
        }

        internal static string OwnerPlatformId(ZDO zdo)
        {
            return zdo.GetString(OwnerPlatformIdKey);
        }

        internal static string OwnerLookup(ZDO zdo)
        {
            return zdo.GetString(OwnerLookupKey);
        }

        internal static void ResizeToFit(Inventory inventory, int additionalSlots = 0)
        {
            int slots = checked(inventory.NrOfItems() + additionalSlots);
            int rows = Math.Max(1, slots / Columns + (slots % Columns == 0 ? 0 : 1));
            ApplyInventoryShape(inventory, Columns, rows);
        }

        internal static void ApplyInventoryShape(Inventory inventory, int columns, int rows)
        {
            InventoryWidth.Set(inventory, columns);
            InventoryHeight.Set(inventory, rows);
        }

        internal static void CompactInventory(Inventory inventory)
        {
            CompactInventory(inventory, Columns);
        }

        internal static void CompactInventory(Inventory inventory, int columns)
        {
            List<ItemDrop.ItemData> items = inventory.GetAllItemsInGridOrder();
            for (int index = 0; index < items.Count; index++)
            {
                items[index].m_gridPos = new Vector2i(index % columns, index / columns);
            }
        }

        internal static Inventory LoadInventoryFromZdo(ZDO zdo)
        {
            Inventory inventory = new("ServerChest", null, Columns, 1);
            byte[]? data = zdo.GetByteArray(ZDOVars.s_items);
            if (data != null && data.Length > 0)
            {
                try
                {
                    ServerChestStorage.Load(inventory, new ZPackage(data));
                }
                catch (Exception ex)
                {
                    PraetorisClientPlugin.Log.LogWarning("Failed to load ServerChest inventory from ZDO " + zdo.m_uid + ": " + ex.Message);
                    throw;
                }
            }

            CompactInventory(inventory);
            ResizeToFit(inventory);
            ServerChestLog.Debug("loaded inventory zdo=" + zdo.m_uid + " stacks=" + inventory.NrOfItems().ToString(CultureInfo.InvariantCulture) + " items=" + inventory.NrOfItemsIncludingStacks().ToString(CultureInfo.InvariantCulture) + " dataLength=" + (data?.Length ?? 0).ToString(CultureInfo.InvariantCulture));
            return inventory;
        }

        internal static void SaveInventoryToZdo(ZDO zdo, Inventory inventory)
        {
            CompactInventory(inventory);
            ZPackage package = new();
            ResizeToFit(inventory);
            ServerChestStorage.Save(inventory, package);
            byte[] data = package.GetArray();
            zdo.Set(ZDOVars.s_items, data);
            ServerChestLog.Debug("saved inventory zdo=" + zdo.m_uid + " stacks=" + inventory.NrOfItems().ToString(CultureInfo.InvariantCulture) + " items=" + inventory.NrOfItemsIncludingStacks().ToString(CultureInfo.InvariantCulture) + " dataLength=" + data.Length.ToString(CultureInfo.InvariantCulture));
        }

        internal static List<ZDO> FindAllZdos()
        {
            List<ZDO> result = new();
            if (ZDOMan.instance == null)
            {
                return result;
            }

            int index = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(PrefabName, result, ref index))
            {
            }

            result.RemoveAll(zdo => zdo == null || !zdo.IsValid());
            return result;
        }

        internal static void ShowMessage(string message)
        {
            if (Player.m_localPlayer != null)
            {
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, message);
            }
            else
            {
                PraetorisClientPlugin.Log.LogInfo(message);
            }
        }

        private sealed class FieldInfoWrapper<T>
        {
            private readonly System.Reflection.FieldInfo? _field;

            internal FieldInfoWrapper(Type type, string fieldName)
            {
                _field = AccessTools.Field(type, fieldName);
            }

            internal void Set(object instance, T value)
            {
                if (_field == null)
                {
                    PraetorisClientPlugin.Log.LogWarning("Missing reflected field for ServerChest inventory shape.");
                    return;
                }

                _field.SetValue(instance, value);
            }
        }
    }
}
