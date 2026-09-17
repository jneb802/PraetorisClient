using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace PraetorisClient.ServerChestFeature
{
    internal static class ServerChestPiece
    {
        internal const string BasePrefabName = "piece_chest_wood";
        private static bool _registered;
        private static ContainerShape? _vanillaWoodChestShape;

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

            _vanillaWoodChestShape ??= CaptureVanillaWoodChestShape();

            CustomPiece customPiece = new(ServerChest.PrefabName, BasePrefabName, pieceConfig);
            GameObject prefab = customPiece.PiecePrefab;
            if (prefab == null)
            {
                PraetorisClientPlugin.Log.LogError("Failed to create ServerChest prefab from " + BasePrefabName + ".");
                return;
            }

            ConfigurePrefab(prefab);
            PieceManager.Instance.AddPiece(customPiece);
            RestoreVanillaWoodChestPrefab();
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

        internal static bool TryRestoreVanillaWoodChest(Container container, out bool restored)
        {
            restored = false;
            if (!IsVanillaWoodChest(container))
            {
                return false;
            }

            ContainerShape? shape = _vanillaWoodChestShape;
            if (shape == null)
            {
                return true;
            }

            restored |= RestoreContainerComponent(container, shape.Value);
            Inventory inventory = container.GetInventory();
            ServerChest serverChest = container.GetComponent<ServerChest>();
            if (serverChest != null)
            {
                if (inventory != null)
                {
                    ServerChest.ForgetInventory(inventory);
                }

                Object.Destroy(serverChest);
                restored = true;
            }

            if (inventory == null || (inventory.GetWidth() == shape.Value.Width && inventory.GetHeight() == shape.Value.Height))
            {
                return true;
            }

            int capacity = shape.Value.Width * shape.Value.Height;
            if (inventory.NrOfItems() > capacity)
            {
                PraetorisClientPlugin.Log.LogWarning("Leaving " + BasePrefabName + " at expanded size because it has more stacks than vanilla capacity.");
                return true;
            }

            ServerChest.CompactInventory(inventory, shape.Value.Width);
            ServerChest.ApplyInventoryShape(inventory, shape.Value.Width, shape.Value.Height);
            restored = true;
            return true;
        }

        private static ContainerShape? CaptureVanillaWoodChestShape()
        {
            GameObject basePrefab = PrefabManager.Instance.GetPrefab(BasePrefabName);
            Container? container = basePrefab != null ? basePrefab.GetComponent<Container>() : null;
            if (container == null)
            {
                PraetorisClientPlugin.Log.LogWarning("Unable to capture vanilla wood chest container shape.");
                return null;
            }

            return new ContainerShape(container.m_name, container.m_width, container.m_height);
        }

        private static void RestoreVanillaWoodChestPrefab()
        {
            ContainerShape? shape = _vanillaWoodChestShape;
            if (shape == null)
            {
                return;
            }

            GameObject basePrefab = PrefabManager.Instance.GetPrefab(BasePrefabName);
            Container? container = basePrefab != null ? basePrefab.GetComponent<Container>() : null;
            if (container == null)
            {
                return;
            }

            RestoreContainerComponent(container, shape.Value);
            ServerChest serverChest = container.GetComponent<ServerChest>();
            if (serverChest != null)
            {
                Object.Destroy(serverChest);
            }
        }

        private static bool RestoreContainerComponent(Container container, ContainerShape shape)
        {
            bool restored = false;
            if (container.m_name != shape.Name)
            {
                container.m_name = shape.Name;
                restored = true;
            }

            if (container.m_width != shape.Width)
            {
                container.m_width = shape.Width;
                restored = true;
            }

            if (container.m_height != shape.Height)
            {
                container.m_height = shape.Height;
                restored = true;
            }

            return restored;
        }

        private static bool IsVanillaWoodChest(Container container)
        {
            if (container == null)
            {
                return false;
            }

            string objectName = container.gameObject.name;
            return objectName == BasePrefabName || objectName.StartsWith(BasePrefabName + "(");
        }

        private readonly struct ContainerShape
        {
            internal ContainerShape(string name, int width, int height)
            {
                Name = name;
                Width = width;
                Height = height;
            }

            internal string Name { get; }
            internal int Width { get; }
            internal int Height { get; }
        }
    }
}
