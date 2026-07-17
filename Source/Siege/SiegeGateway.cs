using UnityEngine;

namespace PraetorisClient
{
    public class SiegeGateway : MonoBehaviour
    {
        public string m_siegeId = string.Empty;
        public Vector3 m_entryPosition = Vector3.zero;

        private ZNetView? _nview;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
        }

        private void Start()
        {
            ApplyToZdo();
        }

        internal bool TryGetTarget(out string siegeId, out Vector3 entryPosition)
        {
            ApplyToZdo();
            siegeId = m_siegeId.Trim();
            entryPosition = m_entryPosition;
            return !string.IsNullOrWhiteSpace(siegeId);
        }

        internal bool TryEnter(Player player)
        {
            if (!TryGetTarget(out string siegeId, out Vector3 entryPosition))
            {
                return false;
            }

            return SiegePortalBridge.RequestSiegeEntry(player, siegeId, entryPosition);
        }

        internal void LoadFromZdo(ZDO zdo)
        {
            string configuredSiegeId = zdo.GetString($"{nameof(SiegeGateway)}.{nameof(m_siegeId)}").Trim();
            if (!string.IsNullOrWhiteSpace(configuredSiegeId))
            {
                m_siegeId = configuredSiegeId;
            }

            if (zdo.GetVec3($"{nameof(SiegeGateway)}.{nameof(m_entryPosition)}", out Vector3 configuredEntryPosition))
            {
                m_entryPosition = configuredEntryPosition;
            }

            ApplyToZdo(zdo);
        }

        private void ApplyToZdo()
        {
            _nview ??= GetComponent<ZNetView>();
            if (_nview == null || !_nview.IsValid())
            {
                return;
            }

            ApplyToZdo(_nview.GetZDO());
        }

        private void ApplyToZdo(ZDO zdo)
        {
            string siegeId = m_siegeId.Trim();
            if (string.IsNullOrWhiteSpace(siegeId))
            {
                return;
            }

            zdo.Set(SiegePortalBridge.SiegeIdZdoKey, siegeId);
            zdo.Set(SiegePortalBridge.SiegeEntryPositionZdoKey, m_entryPosition);
            zdo.Set(ZDOVars.s_tag, SiegePortalBridge.SiegeTagPrefix + siegeId);
        }
    }
}
