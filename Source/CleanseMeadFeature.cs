using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace PraetorisClient
{
    internal static class CleanseMeadFeature
    {
        private const string ItemPrefabName = "Warp_MeadCleanse";
        private const string ItemBasePrefabName = "MeadHealthLingering";
        private const string MeadBasePrefabName = "Warp_MeadBaseCleanse";
        private const string MeadBaseBasePrefabName = "MeadBaseHealthLingering";
        private const string ItemName = "Cleanse Mead";
        private const string ItemDescription = "Cleanses ALL status effects. Be mindful of your burdens!";
        private const string MeadBaseName = "Mead Base: Cleanse";
        private const string MeadBaseDescription = "Ferment this to create cleanse mead.";

        private static bool _registered;

        internal static void Initialize()
        {
            PrefabManager.OnVanillaPrefabsAvailable += Register;
        }

        internal static void Shutdown()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Register;
            _registered = false;
        }

        private static void Register()
        {
            if (_registered)
            {
                PrefabManager.OnVanillaPrefabsAvailable -= Register;
                return;
            }

            CustomItem customItem = new CustomItem(ItemPrefabName, ItemBasePrefabName);
            if (!customItem.ItemPrefab || !customItem.ItemDrop)
            {
                PraetorisClientPlugin.Log.LogWarning("Failed to clone " + ItemBasePrefabName + " for cleanse mead.");
                return;
            }

            CustomItem meadBase = new CustomItem(MeadBasePrefabName, MeadBaseBasePrefabName, CreateMeadBaseConfig());
            if (!meadBase.ItemPrefab || !meadBase.ItemDrop)
            {
                PraetorisClientPlugin.Log.LogWarning("Failed to clone " + MeadBaseBasePrefabName + " for cleanse mead base.");
                return;
            }

            if (!TryGetIcon(customItem.ItemDrop, out Sprite icon))
            {
                return;
            }

            CleanseMeadStatusEffect statusEffect = CleanseMeadStatusEffect.Create(icon);
            if (!ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(statusEffect, false)))
            {
                return;
            }

            ConfigureItem(customItem.ItemPrefab, statusEffect, icon);
            ConfigureMeadBase(meadBase.ItemPrefab, statusEffect);

            if (!ItemManager.Instance.AddItem(customItem))
            {
                return;
            }

            if (!ItemManager.Instance.AddItem(meadBase))
            {
                return;
            }

            if (ItemManager.Instance.AddItemConversion(new CustomItemConversion(CreateFermenterConversionConfig())))
            {
                _registered = true;
                PrefabManager.OnVanillaPrefabsAvailable -= Register;
                PraetorisClientPlugin.Log.LogInfo("Registered cleanse mead.");
            }
        }

        private static ItemConfig CreateMeadBaseConfig()
        {
            return new ItemConfig
            {
                Name = MeadBaseName,
                Description = MeadBaseDescription,
                CraftingStation = CraftingStations.MeadKetill,
                Amount = 1,
                Requirements = new[]
                {
                    new RequirementConfig("Pukeberries", 6),
                    new RequirementConfig("FreshSeaweed", 1),
                    new RequirementConfig("FragrantBundle", 1)
                }
            };
        }

        private static FermenterConversionConfig CreateFermenterConversionConfig()
        {
            return new FermenterConversionConfig
            {
                FromItem = MeadBasePrefabName,
                ToItem = ItemPrefabName,
                ProducedItems = 6
            };
        }

        private static void ConfigureItem(GameObject itemPrefab, StatusEffect statusEffect, Sprite icon)
        {
            ItemDrop itemDrop = itemPrefab.GetComponent<ItemDrop>();
            ItemDrop.ItemData.SharedData shared = itemDrop.m_itemData.m_shared;
            shared.m_name = ItemName;
            shared.m_description = ItemDescription;
            shared.m_icons = new[] { icon };
            shared.m_consumeStatusEffect = statusEffect;
        }

        private static void ConfigureMeadBase(GameObject itemPrefab, StatusEffect statusEffect)
        {
            ItemDrop itemDrop = itemPrefab.GetComponent<ItemDrop>();
            ItemDrop.ItemData.SharedData shared = itemDrop.m_itemData.m_shared;
            shared.m_consumeStatusEffect = statusEffect;
        }

        private static bool TryGetIcon(ItemDrop itemDrop, out Sprite icon)
        {
            icon = null!;
            Sprite[] icons = itemDrop.m_itemData.m_shared.m_icons;
            if (icons == null || icons.Length == 0 || !icons[0])
            {
                PraetorisClientPlugin.Log.LogWarning("Cleanse mead could not find the cloned " + ItemBasePrefabName + " icon.");
                return false;
            }

            icon = icons[0];
            return true;
        }
    }
}
