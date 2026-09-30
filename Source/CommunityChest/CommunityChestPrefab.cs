using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace PraetorisClient.CommunityChestFeature
{
    internal sealed class CommunityChest : MonoBehaviour, Hoverable, Interactable
    {
        internal const string PrefabName = "CommunityChest";
        internal const string BasePrefab = "TreasureChest_dvergrtower";
        public string GetHoverName() => "Community Chest";
        public float GetHoverOffset() => 0;

        public string GetHoverText() => Localization.instance.Localize(
            "Community Chest\nYour personal coin bank\n[<color=yellow><b>$KEY_Use</b></color>] Open bank");

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || user != Player.m_localPlayer) return false;
            CommunityChestClient.Open(this);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        internal static void Initialize()
        {
            PrefabManager.OnVanillaPrefabsAvailable += Register;
            CommunityChestClient.RegisterCommands();
        }

        internal static void Shutdown()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Register;
            CommunityChestWindow.Close();
        }

        private static void Register()
        {
            GameObject prefab = PrefabManager.Instance.CreateClonedPrefab(PrefabName, BasePrefab);
            if (prefab == null) return;
            Container container = prefab.GetComponent<Container>();
            if (container != null)
            {
                if (container.m_open != null) container.m_open.SetActive(false);
                if (container.m_closed != null) container.m_closed.SetActive(true);
                Object.DestroyImmediate(container);
            }
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
