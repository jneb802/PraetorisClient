using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PraetorisClient.CreatureOwnership
{
    internal static class CreatureOwnerWardPiece
    {
        internal const string PrefabName = "PraetorisCreatureOwnerWard";
        private const string BasePrefabName = "guard_stone";
        private const string OriginalRequirementItemName = "SurtlingCore";
        private const string ReplacementRequirementItemName = "MoltenCore";
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

            PieceConfig pieceConfig = new PieceConfig
            {
                Name = "Creature Owner Ward",
                Description = "Reassigns nearby monster ownership while active.",
                PieceTable = PieceTables.Hammer,
                Category = PieceCategories.Misc
            };

            CustomPiece customPiece = new CustomPiece(PrefabName, BasePrefabName, pieceConfig);
            GameObject prefab = customPiece.PiecePrefab;
            if (prefab == null)
            {
                PraetorisClientPlugin.Log.LogError("Failed to create creature owner ward prefab from " + BasePrefabName + ".");
                return;
            }

            AttachOwnerWardComponent(prefab);
            ReplaceSurtlingCoreRequirement(prefab);
            AddSurtlingTrophyKitbash(prefab);
            PieceManager.Instance.AddPiece(customPiece);
            _registered = true;
            PrefabManager.OnVanillaPrefabsAvailable -= Register;
        }

        private static void AttachOwnerWardComponent(GameObject prefab)
        {
            PrivateArea privateArea = prefab.GetComponent<PrivateArea>();
            CreatureOwnerWard ownerWard = prefab.GetComponent<CreatureOwnerWard>() ?? prefab.AddComponent<CreatureOwnerWard>();

            if (privateArea != null)
            {
                ownerWard.m_name = "Creature Owner Ward";
                ownerWard.m_radius = privateArea.m_radius;
                ownerWard.m_enabledEffect = privateArea.m_enabledEffect;
                ownerWard.m_areaMarker = privateArea.m_areaMarker;
                ownerWard.m_activateEffect = privateArea.m_activateEffect;
                ownerWard.m_deactivateEffect = privateArea.m_deactivateEffect;
                ownerWard.m_model = privateArea.m_model;
                Object.DestroyImmediate(privateArea);
            }

            ownerWard.m_radius = Mathf.Max(1.0f, PraetorisClientPlugin.CreatureOwnerWardRadius.Value);
        }

        private static void ReplaceSurtlingCoreRequirement(GameObject prefab)
        {
            Piece piece = prefab.GetComponent<Piece>();
            if (piece == null)
            {
                PraetorisClientPlugin.Log.LogWarning("Creature Owner Ward prefab has no Piece component; recipe requirement was not changed.");
                return;
            }

            if (piece.m_resources == null || piece.m_resources.Length == 0)
            {
                PraetorisClientPlugin.Log.LogWarning("Creature Owner Ward prefab has no recipe requirements; recipe requirement was not changed.");
                return;
            }

            bool replaced = false;
            foreach (Piece.Requirement requirement in piece.m_resources)
            {
                ItemDrop item = requirement.m_resItem;
                if (item == null || item.name != OriginalRequirementItemName)
                {
                    continue;
                }

                requirement.m_resItem = Mock<ItemDrop>.Create(ReplacementRequirementItemName);
                replaced = true;
            }

            if (!replaced)
            {
                PraetorisClientPlugin.Log.LogWarning(
                    "Creature Owner Ward recipe did not contain " + OriginalRequirementItemName + "; " + ReplacementRequirementItemName + " replacement was not applied.");
            }
        }

        private static void AddSurtlingTrophyKitbash(GameObject prefab)
        {
            KitbashConfig kitbashConfig = new KitbashConfig
            {
                FixReferences = true,
                Layer = "piece"
            };

            kitbashConfig.KitbashSources.Add(new KitbashSourceConfig
            {
                Name = "owner_ward_surtling_trophy",
                SourcePrefab = "TrophySurtling",
                SourcePath = "attach/model",
                Position = new Vector3(0.0f, 1.9f, 0.0f),
                Rotation = Quaternion.Euler(0.0f, 180.0f, 0.0f),
                Scale = new Vector3(0.65f, 0.65f, 0.65f),
                Materials = new[] { "imp_mat" }
            });

            KitbashManager.Instance.AddKitbash(prefab, kitbashConfig);
        }
    }
}
