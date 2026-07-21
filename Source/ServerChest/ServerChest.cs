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
        internal const int MaxColumns = 8;
        internal const int MaxRows = 8;
        internal const int MaxSlots = MaxColumns * MaxRows;

        private static readonly Dictionary<Inventory, ServerChest> InventoryOwners = new();
        private static readonly FieldInfoWrapper<int> InventoryWidth = new(typeof(Inventory), "m_width");
        private static readonly FieldInfoWrapper<int> InventoryHeight = new(typeof(Inventory), "m_height");
        private static bool _suppressInventoryChanged;

        private Container? _container;
        private Inventory? _inventory;
        private ZNetView? _nview;

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
                _inventory.m_onChanged -= OnInventoryChanged;
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
                _inventory.m_onChanged -= OnInventoryChanged;
                InventoryOwners.Remove(_inventory);
            }

            _inventory = inventory;
            InventoryOwners[inventory] = this;
            ApplyMaxInventoryShape(inventory);
            ServerChestLog.Debug("registered live inventory zdo=" + GetZdoId() + " items=" + inventory.NrOfItems().ToString(CultureInfo.InvariantCulture));
            inventory.m_onChanged += OnInventoryChanged;
        }

        private void OnInventoryChanged()
        {
            if (_suppressInventoryChanged || _inventory == null || _nview == null || !_nview.IsValid() || !_nview.IsOwner())
            {
                return;
            }

            CompactInventory(_inventory);
            ApplyMaxInventoryShape(_inventory);
            SaveInventoryToZdo(_nview.GetZDO(), _inventory);
            ServerChestLog.Debug("live inventory changed zdo=" + _nview.GetZDO().m_uid + " stacks=" + _inventory.NrOfItems().ToString(CultureInfo.InvariantCulture) + " items=" + _inventory.NrOfItemsIncludingStacks().ToString(CultureInfo.InvariantCulture));
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

        internal static void ApplyMaxInventoryShape(Inventory inventory)
        {
            InventoryWidth.Set(inventory, MaxColumns);
            InventoryHeight.Set(inventory, MaxRows);
        }

        internal static void CompactInventory(Inventory inventory)
        {
            List<ItemDrop.ItemData> items = inventory.GetAllItemsInGridOrder();
            for (int index = 0; index < items.Count; index++)
            {
                items[index].m_gridPos = new Vector2i(index % MaxColumns, index / MaxColumns);
            }
        }

        internal static Inventory LoadInventoryFromZdo(ZDO zdo)
        {
            Inventory inventory = new("ServerChest", null, MaxColumns, MaxRows);
            string base64 = zdo.GetString(ZDOVars.s_items);
            if (!string.IsNullOrEmpty(base64))
            {
                try
                {
                    inventory.Load(new ZPackage(base64));
                }
                catch (Exception ex)
                {
                    PraetorisClientPlugin.Log.LogWarning("Failed to load ServerChest inventory from ZDO " + zdo.m_uid + ": " + ex.Message);
                }
            }

            ApplyMaxInventoryShape(inventory);
            CompactInventory(inventory);
            ServerChestLog.Debug("loaded inventory zdo=" + zdo.m_uid + " stacks=" + inventory.NrOfItems().ToString(CultureInfo.InvariantCulture) + " items=" + inventory.NrOfItemsIncludingStacks().ToString(CultureInfo.InvariantCulture) + " dataLength=" + base64.Length.ToString(CultureInfo.InvariantCulture));
            return inventory;
        }

        internal static void SaveInventoryToZdo(ZDO zdo, Inventory inventory)
        {
            CompactInventory(inventory);
            ZPackage package = new();
            inventory.Save(package);
            string base64 = package.GetBase64();
            zdo.Set(ZDOVars.s_items, base64);
            ServerChestLog.Debug("saved inventory zdo=" + zdo.m_uid + " stacks=" + inventory.NrOfItems().ToString(CultureInfo.InvariantCulture) + " items=" + inventory.NrOfItemsIncludingStacks().ToString(CultureInfo.InvariantCulture) + " dataLength=" + base64.Length.ToString(CultureInfo.InvariantCulture));
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

        internal static void WithSuppressedInventoryChanged(Action action)
        {
            bool previous = _suppressInventoryChanged;
            _suppressInventoryChanged = true;
            try
            {
                action();
            }
            finally
            {
                _suppressInventoryChanged = previous;
            }
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
