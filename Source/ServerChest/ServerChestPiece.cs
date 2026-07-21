using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace PraetorisClient.ServerChestFeature
{
    internal static class ServerChestPiece
    {
        private const string BasePrefabName = "piece_chest_wood";
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
                Description = "Receives admin-delivered items for one registered player.",
                PieceTable = PieceTables.Hammer,
                Category = PieceCategories.Misc
            };

            CustomPiece customPiece = new(ServerChest.PrefabName, BasePrefabName, pieceConfig);
            GameObject prefab = customPiece.PiecePrefab;
            if (prefab == null)
            {
                PraetorisClientPlugin.Log.LogError("Failed to create ServerChest prefab from " + BasePrefabName + ".");
                return;
            }

            ConfigurePrefab(prefab);
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
                container.m_defaultItems.m_drops.Clear();
            }

            Piece piece = prefab.GetComponent<Piece>();
            if (piece != null)
            {
                piece.m_name = "Server Chest";
                piece.m_description = "Receives admin-delivered items for one registered player.";
            }

            if (prefab.GetComponent<ServerChest>() == null)
            {
                prefab.AddComponent<ServerChest>();
            }
        }
    }
}
