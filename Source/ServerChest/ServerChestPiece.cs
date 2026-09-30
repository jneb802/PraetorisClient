using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace PraetorisClient.ServerChestFeature
{
    internal static class ServerChestPiece
    {
        private const string BasePrefabName = "TreasureChest_dvergrtower";
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

            PieceConfig pieceConfig = new()
            {
                Name = "Server Chest",
                Description = "A chest used to receive items from server admins. Limit one per player.",
                PieceTable = PieceTables.Hammer,
                Category = PieceCategories.Misc,
                CraftingStation = "piece_workbench",
                Requirements = new[] { new RequirementConfig("Wood", 10) }
            };

            GameObject prefab = PrefabManager.Instance.CreateClonedPrefab(ServerChest.PrefabName, BasePrefabName);
            if (prefab == null)
            {
                PraetorisClientPlugin.Log.LogError("Failed to create ServerChest prefab from " + BasePrefabName + ".");
                return;
            }

            Piece buildPiece = prefab.GetComponent<Piece>() ?? prefab.AddComponent<Piece>();
            buildPiece.m_canBeRemoved = true;
            if (!Application.isBatchMode)
            {
                Sprite icon = RenderManager.Instance.Render(prefab);
                if (icon != null)
                    buildPiece.m_icon = icon;
            }
            ConfigurePrefab(prefab);
            CustomPiece customPiece = new(prefab, false, pieceConfig);
            PieceManager.Instance.AddPiece(customPiece);
            _registered = true;
            PrefabManager.OnVanillaPrefabsAvailable -= Register;
        }

        private static void ConfigurePrefab(GameObject prefab)
        {
            Container container = prefab.GetComponent<Container>();
            if (container != null)
            {
                container.m_name = "Server Chest";
                container.m_width = ServerChest.MaxColumns;
                container.m_height = ServerChest.MaxRows;
                container.m_defaultItems = new DropTable();
                container.m_autoDestroyEmpty = false;
                container.m_discoverStat = PlayerStatType.None;
            }

            Piece piece = prefab.GetComponent<Piece>();
            if (piece != null)
            {
                piece.m_name = "Server Chest";
                piece.m_description = "A chest used to receive items from server admins. Limit one per player.";
            }

            if (prefab.GetComponent<ServerChest>() == null)
            {
                prefab.AddComponent<ServerChest>();
            }
        }

    }
}
