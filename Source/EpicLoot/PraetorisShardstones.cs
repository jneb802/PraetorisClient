using System;
using System.Collections.Generic;
using System.Linq;
using EpicLoot;
using EpicLoot.ShardStones;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Newtonsoft.Json;
using UnityEngine;

namespace PraetorisClient.EpicLootFeature
{
    internal static class PraetorisShardstones
    {
        // These values are saved in socket data. Never renumber or reuse them.
        // Epic Loot parses numeric ShardType values and reconstructs canonical prefab names.
        private static readonly ShardSpec[] Specs =
        {
            new ShardSpec((ShardType)0x50520001, "Point Blank", "Orange", PraetorisMagicEffects.PointBlank,
                ShardCategory.Core, ItemRarity.Magic, PraetorisMagicEffects.PointBlankValues),
            new ShardSpec((ShardType)0x50520002, "Reload on Kill", "Firewalker", PraetorisMagicEffects.ReloadOnKill,
                ShardCategory.Unique, ItemRarity.Epic, new float[] { 1, 1, 1, 1 }),
            new ShardSpec((ShardType)0x50520003, "Piercing Shot", "Stormcaller", PraetorisMagicEffects.PiercingShot,
                ShardCategory.Unique, ItemRarity.Epic, PraetorisMagicEffects.PiercingShotValues)
        };

        internal static void Initialize()
        {
            AddDefinitions(Shards.GetCFG());
            AddLoot(LootRoller.GetCFG());
            CustomLocalization localization = LocalizationManager.Instance.GetLocalization();
            foreach (ShardSpec spec in Specs)
            {
                localization.AddTranslation($"mod_epicloot_shard_{spec.Id}", spec.Name);
                foreach (ItemRarity rarity in spec.Values.Keys)
                {
                    string name = spec.Prefab(rarity);
                    CustomItem item = new CustomItem(name, $"{spec.Template}_{rarity}_ShardStone", new ItemConfig
                    {
                        Name = $"$mod_epicloot_{rarity} Shard of {spec.Name}",
                        Description = "$mod_epicloot_assets_shardstone_introduce"
                    });
                    if (item.ItemPrefab == null)
                    {
                        throw new InvalidOperationException($"Missing shardstone template for {name}");
                    }
                    ItemDrop.ItemData data = item.ItemDrop.m_itemData;
                    data.m_shared.m_ammoType = $"{spec.Id}|{rarity}|ShardStone";
                    data.m_dropPrefab = item.ItemPrefab;
                    Shards.EnsureShardMetadata(data);
                    if (!ItemManager.Instance.AddItem(item))
                    {
                        throw new InvalidOperationException($"Could not register shardstone {name}");
                    }
                }

                ItemRarity[] rarities = spec.Values.Keys.OrderBy(rarity => (int)rarity).ToArray();
                for (int index = 1; index < rarities.Length; index++)
                {
                    ItemRarity previous = rarities[index - 1];
                    ItemRarity next = rarities[index];
                    EpicLootAPI.MaterialConversion conversion = new EpicLootAPI.MaterialConversion(
                        EpicLootAPI.MaterialConversionType.ShardUpgrade,
                        $"PraetorisShardUpgrade_{spec.Id}_{next}", spec.Prefab(next));
                    conversion.Resources.Add(new EpicLootAPI.MaterialConversionRequirement(spec.Prefab(previous)));
                    conversion.Resources.Add(new EpicLootAPI.MaterialConversionRequirement($"Shard{previous}", 4 + (int)previous));
                    if (!conversion.Register())
                    {
                        throw new InvalidOperationException($"Could not register shard upgrade {conversion.Name}");
                    }
                }
            }

            ItemManager.OnItemsRegistered += EnableItems;
        }

        private static void EnableItems()
        {
            foreach (ShardSpec spec in Specs)
            {
                foreach (ItemRarity rarity in spec.Values.Keys)
                {
                    GameObject prefab = PrefabManager.Instance.GetPrefab(spec.Prefab(rarity));
                    if (prefab != null)
                    {
                        prefab.SetActive(true);
                        prefab.GetComponent<ItemDrop>().m_itemData.m_dropPrefab = prefab;
                    }
                }
            }
        }

        internal static void AddDefinitions(ShardStonesConfig config)
        {
            if (config == null)
            {
                return;
            }
            foreach (ShardSpec spec in Specs)
            {
                if (config.Shards.TryGetValue(spec.Id, out ShardDefinition existing))
                {
                    if (existing?.UniformEffect?.EffectType != spec.Effect)
                    {
                        throw new InvalidOperationException($"Shardstone ID {spec.Id} is already in use.");
                    }
                    continue;
                }
                config.Shards.Add(spec.Id, new ShardDefinition
                {
                    Category = spec.Category,
                    Rarities = spec.Values.Keys.ToList(),
                    UniformEffect = new ShardEffectDefinition
                    {
                        EffectType = spec.Effect,
                        ValuesPerRarity = new Dictionary<ItemRarity, float>(spec.Values)
                    }
                });
            }
        }

        internal static void AddLoot(LootConfig config)
        {
            if (config?.ItemSets == null)
            {
                return;
            }
            // Follow the existing shard distribution, including biome rarity weights.
            // Work on the item sets themselves so config reloads and synced configs use the same path.
            foreach (LootItemSet set in config.ItemSets)
            {
                if (set.Loot == null)
                {
                    continue;
                }
                List<LootDrop> drops = set.Loot.ToList();
                foreach (ShardSpec spec in Specs)
                {
                    LootDrop? template = drops.FirstOrDefault(drop => drop.Item != null &&
                        drop.Item.StartsWith(spec.Template + "_", StringComparison.Ordinal) &&
                        drop.Item.EndsWith("_ShardStone", StringComparison.Ordinal));
                    if (template == null)
                    {
                        continue;
                    }
                    string target = template.Item.Replace(spec.Template + "_", spec.Id + "_");
                    if (drops.Any(drop => drop.Item == target))
                    {
                        continue;
                    }
                    LootDrop added = JsonConvert.DeserializeObject<LootDrop>(JsonConvert.SerializeObject(template))
                        ?? throw new InvalidOperationException($"Could not copy shard loot {template.Item}");
                    added.Item = target;
                    if (added.RarityItems != null)
                    {
                        foreach (ItemRarity rarity in added.RarityItems.Keys.ToArray())
                        {
                            added.RarityItems[rarity] = spec.Prefab(rarity);
                        }
                    }
                    drops.Add(added);
                }
                set.Loot = drops.ToArray();
            }
        }

        private sealed class ShardSpec
        {
            internal readonly ShardType Id;
            internal readonly string Name;
            internal readonly string Template;
            internal readonly string Effect;
            internal readonly ShardCategory Category;
            internal readonly Dictionary<ItemRarity, float> Values = new Dictionary<ItemRarity, float>();

            internal ShardSpec(ShardType id, string name, string template, string effect,
                ShardCategory category, ItemRarity firstRarity, float[] values)
            {
                Id = id;
                Name = name;
                Template = template;
                Effect = effect;
                Category = category;
                for (int index = 0; index < values.Length; index++)
                {
                    Values.Add((ItemRarity)((int)firstRarity + index), values[index]);
                }
            }

            internal string Prefab(ItemRarity rarity) => $"{Id}_{rarity}_ShardStone";
        }
    }

    [HarmonyPatch(typeof(Shards), nameof(Shards.InitializeShardDefinitions))]
    internal static class PraetorisShardConfigPatch
    {
        private static void Prefix(ShardStonesConfig config) => PraetorisShardstones.AddDefinitions(config);
    }

    [HarmonyPatch(typeof(LootRoller), nameof(LootRoller.Initialize))]
    internal static class PraetorisShardLootPatch
    {
        private static void Prefix(LootConfig lootConfig) => PraetorisShardstones.AddLoot(lootConfig);
    }
}
