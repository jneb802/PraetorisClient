using System.Collections.Generic;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace PraetorisClient.PvpWardFeature
{
    internal static class PvpWardPrefab
    {
        internal const string Name = "PraetorisPvpWard";
        internal const string EffectName = "PraetorisPvpArena";
        internal static readonly int EffectHash = EffectName.GetStableHashCode();

        internal static void Initialize() => PrefabManager.OnVanillaPrefabsAvailable += Register;
        internal static void Shutdown() => PrefabManager.OnVanillaPrefabsAvailable -= Register;

        private static void Register()
        {
            GameObject prefab = PrefabManager.Instance.CreateClonedPrefab(Name, "guard_stone");
            Piece piece = prefab.GetComponent<Piece>();
            piece.m_name = "PvP Arena Ward";
            piece.m_description = "Forces PvP nearby. No skill gain or death skill loss while inside the enabled area.";
            piece.m_resources = System.Array.Empty<Piece.Requirement>();
            PrivateArea area = prefab.GetComponent<PrivateArea>();
            PvpWard ward = prefab.AddComponent<PvpWard>();
            ward.EnabledEffect = area.m_enabledEffect;
            ward.AreaMarker = area.m_areaMarker;
            if (ward.EnabledEffect != null) ward.EnabledEffect.SetActive(false);
            if (ward.AreaMarker != null) ward.AreaMarker.gameObject.SetActive(false);
            Object.DestroyImmediate(area);
            foreach (ItemStand stand in prefab.GetComponentsInChildren<ItemStand>(true))
            {
                foreach (Collider collider in stand.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
                Object.DestroyImmediate(stand);
            }
            foreach (EffectArea effect in prefab.GetComponentsInChildren<EffectArea>(true))
            {
                foreach (Collider collider in effect.GetComponents<Collider>()) Object.DestroyImmediate(collider);
                Object.DestroyImmediate(effect);
            }

            StatusEffect status = ScriptableObject.CreateInstance<StatusEffect>();
            status.name = EffectName;
            status.m_name = "PvP Arena";
            status.m_tooltip = "PvP is required. Skills cannot increase. Death causes no skill loss inside the enabled ward.";
            status.m_icon = piece.m_icon;
            ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(status, false));
            // Register only the network prefab. No PieceTable entry, recipe, or build-menu entry.
            PrefabManager.Instance.AddPrefab(new CustomPrefab(prefab, false));
            PrefabManager.OnVanillaPrefabsAvailable -= Register;
        }
    }

    public sealed class PvpWard : MonoBehaviour, Hoverable, Interactable
    {
        private static readonly HashSet<PvpWard> Wards = new();
        public GameObject EnabledEffect = null!;
        public CircleProjector AreaMarker = null!;
        private ZNetView _view = null!;
        private float _showMarkerUntil;

        internal static float Radius => PraetorisClientPlugin.PvpWardRadius.Value;
        internal bool IsEnabled => _view != null && _view.IsValid() &&
            _view.GetZDO().GetBool(ZDOVars.s_enabled, true);

        private void Awake()
        {
            _view = GetComponent<ZNetView>();
            if (_view != null && _view.IsValid()) Wards.Add(this);
        }

        private void OnDestroy() => Wards.Remove(this);

        private void Update()
        {
            if (EnabledEffect != null && EnabledEffect.activeSelf != IsEnabled)
                EnabledEffect.SetActive(IsEnabled);
            if (AreaMarker == null) return;
            AreaMarker.m_radius = Radius;
            bool visible = _view != null && _view.IsValid() && Time.time < _showMarkerUntil;
            if (AreaMarker.gameObject.activeSelf != visible) AreaMarker.gameObject.SetActive(visible);
        }

        internal static bool Contains(Player player)
        {
            if (player == null) return false;
            float radiusSquared = Radius * Radius;
            foreach (PvpWard ward in Wards)
                if (ward != null && ward.IsEnabled &&
                    (player.transform.position - ward.transform.position).sqrMagnitude <= radiusSquared)
                    return true;
            return false;
        }

        public string GetHoverName() => "PvP Arena Ward";
        public float GetHoverOffset() => 0f;
        public string GetHoverText()
        {
            _showMarkerUntil = Time.time + 0.5f;
            string text = $"PvP Arena Ward ({(IsEnabled ? "enabled" : "disabled")})\nRadius: {Radius:0.#} m\nForced PvP. No skill gain or death skill loss.";
            if (PvpWardAccess.LocalIsAdmin)
                text += "\n[<color=yellow><b>$KEY_Use</b></color>] " + (IsEnabled ? "Disable" : "Enable");
            return Localization.instance.Localize(text);
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || user != Player.m_localPlayer || !PvpWardAccess.LocalIsAdmin || !_view.IsValid()) return false;
            PvpWardAccess.SetEnabled(_view.GetZDO().m_uid, !IsEnabled);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    }
}
