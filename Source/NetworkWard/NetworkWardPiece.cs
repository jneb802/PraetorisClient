using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace PraetorisClient.NetworkWardFeature
{
    internal static class NetworkWardPiece
    {
        internal const string PrefabName = "PraetorisNetworkWard";
        internal static void Initialize()
        {
            PrefabManager.OnVanillaPrefabsAvailable += Register;
            _ = new Terminal.ConsoleCommand("networkward_open", "Interact with the nearest Network Ward within 5m.", args =>
            {
                Player player = Player.m_localPlayer;
                if (player == null) return;
                NetworkWard? nearest = null;
                float distance = 5f;
                foreach (NetworkWard ward in Object.FindObjectsByType<NetworkWard>(FindObjectsSortMode.None))
                {
                    float current = Vector3.Distance(player.transform.position, ward.transform.position);
                    if (current < distance) { nearest = ward; distance = current; }
                }
                args.Context.AddString(nearest != null && nearest.Interact(player, false, false)
                    ? "Network Ward authorization requested." : "No Network Ward within 5m.");
            });
            _ = new Terminal.ConsoleCommand("networkward_status", "Print the displayed Network Ward traffic sample.",
                args =>
                {
                    foreach (string line in NetworkWardWindow.Status().Split('\n'))
                        if (line.Length > 0) args.Context.AddString(line);
                });
        }

        internal static void Shutdown() => PrefabManager.OnVanillaPrefabsAvailable -= Register;

        private static void Register()
        {
            CustomPiece piece = new CustomPiece(PrefabName, "guard_stone", new PieceConfig
            {
                Name = "Network Ward",
                Description = "Inspect nearby objects and the network traffic they generate. Does not protect buildings or change ownership.",
                PieceTable = PieceTables.Hammer,
                Category = PieceCategories.Misc,
                Requirements = new[] { new RequirementConfig("RoundLog", 10), new RequirementConfig("GreydwarfEye", 2) }
            });
            GameObject prefab = piece.PiecePrefab;
            PrivateArea area = prefab.GetComponent<PrivateArea>();
            if (area != null)
            {
                if (area.m_areaMarker != null) area.m_areaMarker.gameObject.SetActive(false);
                if (area.m_enabledEffect != null) area.m_enabledEffect.SetActive(false);
                Object.DestroyImmediate(area);
            }
            foreach (ItemStand stand in prefab.GetComponentsInChildren<ItemStand>(true)) Object.DestroyImmediate(stand);
            foreach (EffectArea effect in prefab.GetComponentsInChildren<EffectArea>(true))
            {
                foreach (Collider collider in effect.GetComponents<Collider>()) Object.DestroyImmediate(collider);
                Object.DestroyImmediate(effect);
            }
            prefab.AddComponent<NetworkWard>();
            AddShamanTrophy(prefab);
            PieceManager.Instance.AddPiece(piece);
            PrefabManager.OnVanillaPrefabsAvailable -= Register;
        }

        private static void AddShamanTrophy(GameObject prefab)
        {
            KitbashConfig config = new KitbashConfig { FixReferences = true, Layer = "piece" };
            config.KitbashSources.Add(new KitbashSourceConfig
            {
                Name = "network_ward_shaman_trophy",
                SourcePrefab = "TrophyGreydwarfShaman",
                SourcePath = "attach/model",
                Position = new Vector3(0, 1.9f, 0),
                Rotation = Quaternion.Euler(0, 180, 0),
                Scale = new Vector3(0.65f, 0.65f, 0.65f)
            });
            KitbashObject kitbash = KitbashManager.Instance.AddKitbash(prefab, config);
            kitbash.OnKitbashApplied += () => WardBuildIcon.Apply(kitbash.Prefab != null ? kitbash.Prefab : prefab, "TrophyGreydwarfShaman");
        }
    }

    public sealed class NetworkWard : MonoBehaviour, Hoverable, Interactable
    {
        private void Awake()
        {
            // A cyan beacon distinguishes this diagnostic piece from protective wards.
            GameObject beacon = new GameObject("NetworkBeacon");
            beacon.transform.SetParent(transform, false);
            beacon.transform.localPosition = new Vector3(0, 1.8f, 0);
            Light light = beacon.AddComponent<Light>();
            light.color = new Color(0.2f, 0.8f, 1f);
            light.range = 5;
            light.intensity = 2;
        }

        public string GetHoverName() => "Network Ward";
        public float GetHoverOffset() => 0;
        public string GetHoverText() => Localization.instance.Localize("Network Ward\n[<color=yellow><b>$KEY_Use</b></color>] Inspect network traffic");
        public bool Interact(Humanoid human, bool hold, bool alt)
        {
            ZNetView view = GetComponent<ZNetView>();
            if (hold || human != Player.m_localPlayer || view == null || !view.IsValid()) return false;
            NetworkWardAccess.Request(this);
            return true;
        }
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    }
}
