using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace PraetorisClient.CommunityChestFeature
{
    internal sealed class CommunityChest : MonoBehaviour
    {
        internal const string PrefabName = "CommunityChest";
        internal const string BasePrefab = "TreasureChest_dvergrtower";
        internal static bool Is(Component component) => component != null && component.GetComponent<CommunityChest>() != null;

        internal static void Initialize()
        {
            PrefabManager.OnVanillaPrefabsAvailable += Register;
            CommunityChestClient.RegisterCommands();
        }

        internal static void Shutdown() => PrefabManager.OnVanillaPrefabsAvailable -= Register;

        private static void Register()
        {
            GameObject prefab = PrefabManager.Instance.CreateClonedPrefab(PrefabName, BasePrefab);
            if (prefab == null) return;
            Container container = prefab.GetComponent<Container>();
            container.m_name = "Community Chest";
            container.m_width = 8;
            container.m_height = 5;
            container.m_defaultItems = new DropTable();
            container.m_autoDestroyEmpty = false;
            container.m_checkGuardStone = false;
            container.m_privacy = Container.PrivacySetting.Public;
            container.m_openEffects = new EffectList();
            container.m_closeEffects = new EffectList();
            Piece piece = prefab.GetComponent<Piece>();
            if (piece != null) Object.DestroyImmediate(piece);
            Destructible destructible = prefab.GetComponent<Destructible>();
            if (destructible != null) Object.DestroyImmediate(destructible);
            WearNTear wear = prefab.GetComponent<WearNTear>();
            if (wear != null) Object.DestroyImmediate(wear);
            prefab.AddComponent<CommunityChest>();
            prefab.GetComponent<ZNetView>().m_persistent = true;
            PrefabManager.Instance.AddPrefab(new CustomPrefab(prefab, false));
            PrefabManager.OnVanillaPrefabsAvailable -= Register;
            PraetorisClientPlugin.Log.LogInfo("Registered CommunityChest (admin placement only).");
        }
    }
}
