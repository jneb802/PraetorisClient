using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace PraetorisClient.BuilderCameraFeature
{
    internal static partial class BuilderCamera
    {
        internal const string BeltPrefab = "PraetorisBuilderBelt";
        internal const string WardPrefab = "PraetorisBuilderWard";
        internal static ConfigEntry<int> BeltPrice = null!;
        internal static ConfigEntry<string> TraderPrefab = null!;
        internal static ConfigEntry<float> WardRange = null!;
        internal static ConfigEntry<float> BodyRange = null!;
        internal static ConfigEntry<KeyboardShortcut> ToggleKey = null!;

        internal static void Initialize(ConfigFile config)
        {
            BeltPrice = Bind(config, "BeltPrice", 1500, "Coins charged by the trader.", new AcceptableValueRange<int>(1, 100000));
            TraderPrefab = Bind(config, "TraderPrefab", "Hildir", "Trader prefab that sells the Builder Belt (Hildir, Haldor, or BogWitch).");
            WardRange = Bind(config, "WardRange", 30f, "Maximum camera and build target distance from the ward, in meters.", new AcceptableValueRange<float>(2f, 100f));
            BodyRange = Bind(config, "BodyRange", 30f, "Maximum camera and build target distance from the player's body, in meters.", new AcceptableValueRange<float>(2f, 100f));
            ToggleKey = config.Bind("BuilderCamera", "ToggleKey", new KeyboardShortcut(KeyCode.B), new ConfigDescription(
                "Local client hotkey to toggle build camera. Equip the Builder Belt and hold a build tool near a Builder's Ward. WASD moves; Space rises; Left Ctrl lowers; Shift moves faster.",
                null, new ConfigurationManagerAttributes { IsAdminOnly = false }));
            PrefabManager.OnVanillaPrefabsAvailable += RegisterContent;
            new Terminal.ConsoleCommand("buildercamera", "Toggle build camera, or inspect it with: buildercamera status", args =>
            {
                if (args.Length > 1 && args[1] == "status") args.Context.AddString(Status());
                else if (_active) Stop("Build camera ended.");
                else if (Player.m_localPlayer && GameCamera.instance) Start(Player.m_localPlayer);
            });
        }

        private static ConfigEntry<T> Bind<T>(ConfigFile config, string key, T value, string description, AcceptableValueBase? range = null)
        {
            return config.Bind("BuilderCamera", key, value, new ConfigDescription(description, range, new ConfigurationManagerAttributes { IsAdminOnly = true }));
        }

        internal static void Shutdown()
        {
            Stop(null);
            PrefabManager.OnVanillaPrefabsAvailable -= RegisterContent;
        }

        private static void RegisterContent()
        {
            CustomItem belt = new CustomItem(BeltPrefab, "BeltStrength");
            belt.ItemDrop.m_itemData.m_shared.m_name = "Builder Belt";
            belt.ItemDrop.m_itemData.m_shared.m_description = "Equip this belt and hold a build tool near a Builder's Ward to use the build camera. Your body remains vulnerable. Default key: B.";
            belt.ItemDrop.m_itemData.m_shared.m_equipStatusEffect = null;
            belt.ItemDrop.m_itemData.m_shared.m_setStatusEffect = null;
            belt.ItemDrop.m_itemData.m_shared.m_setName = "";
            belt.ItemDrop.m_itemData.m_shared.m_maxQuality = 1;
            ItemManager.Instance.AddItem(belt);

            CustomPiece ward = new CustomPiece(WardPrefab, "guard_stone", new PieceConfig
            {
                Name = "Builder's Ward",
                Description = "Enables unlimited use of a Builder Belt camera nearby. Does not protect your body or grant building permission.",
                PieceTable = PieceTables.Hammer,
                Category = PieceCategories.Misc,
                Requirements = new[] { new RequirementConfig("Wood", 10, recover: true), new RequirementConfig("Stone", 10, recover: true), new RequirementConfig("GreydwarfEye", 5, recover: true) }
            });
            PrivateArea area = ward.PiecePrefab.GetComponent<PrivateArea>();
            if (area.m_areaMarker) area.m_areaMarker.gameObject.SetActive(false);
            if (area.m_enabledEffect) area.m_enabledEffect.SetActive(false);
            UnityEngine.Object.DestroyImmediate(area);
            foreach (ItemStand stand in ward.PiecePrefab.GetComponentsInChildren<ItemStand>(true)) UnityEngine.Object.DestroyImmediate(stand);
            foreach (EffectArea effect in ward.PiecePrefab.GetComponentsInChildren<EffectArea>(true)) UnityEngine.Object.DestroyImmediate(effect);
            ward.PiecePrefab.AddComponent<BuilderWard>();
            WardBuildIcon.Apply(ward.PiecePrefab, "GreydwarfEye");
            PieceManager.Instance.AddPiece(ward);
            PrefabManager.OnVanillaPrefabsAvailable -= RegisterContent;
        }

        [HarmonyPatch(typeof(Trader), nameof(Trader.GetAvailableItems))]
        private static class TraderItems
        {
            private static void Postfix(Trader __instance, List<Trader.TradeItem> __result)
            {
                if (!string.Equals(Utils.GetPrefabName(__instance.gameObject), TraderPrefab.Value, StringComparison.OrdinalIgnoreCase)) return;
                GameObject prefab = ObjectDB.instance.GetItemPrefab(BeltPrefab);
                if (prefab && !__result.Exists(item => item.m_prefab && item.m_prefab.name == BeltPrefab))
                    __result.Add(new Trader.TradeItem
                    {
                        m_prefab = prefab.GetComponent<ItemDrop>(), m_price = BeltPrice.Value, m_stack = 1,
                        m_buyPlayerEffects = new EffectList(), m_tooltip = "", m_name = "Builder Belt",
                        m_requiredGlobalKey = "", m_buyKey = "", m_incrementKey = ""
                    });
            }
        }
    }
}
