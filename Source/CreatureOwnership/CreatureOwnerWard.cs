using System.Text;
using UnityEngine;

namespace PraetorisClient.CreatureOwnership
{
    public class CreatureOwnerWard : MonoBehaviour, Hoverable, Interactable, TextReceiver
    {
        private const int OwnerNameCharacterLimit = 32;

        public string m_name = "Creature Owner Ward";
        public float m_radius = 10.0f;
        public GameObject m_enabledEffect = null!;
        public CircleProjector m_areaMarker = null!;
        public EffectList m_activateEffect = new EffectList();
        public EffectList m_deactivateEffect = new EffectList();
        public MeshRenderer m_model = null!;

        private ZNetView _nview = null!;
        private Piece _piece = null!;
        private float _nextOwnershipUpdate;
        private bool _hasKnownStatus;
        private bool _lastKnownEnabled;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _piece = GetComponent<Piece>();
            m_radius = Mathf.Max(1.0f, PraetorisClientPlugin.CreatureOwnerWardRadius.Value);

            if (m_areaMarker != null)
            {
                m_areaMarker.m_radius = m_radius;
                m_areaMarker.gameObject.SetActive(false);
            }

            if (m_enabledEffect != null)
            {
                m_enabledEffect.SetActive(false);
            }

            if (!_nview.IsValid())
            {
                return;
            }

            InvokeRepeating(nameof(UpdateStatus), 0.0f, 1.0f);
        }

        private void Update()
        {
            if (!_nview.IsValid() ||
                ZNet.instance == null ||
                ZDOMan.instance == null ||
                !IsEnabled())
            {
                return;
            }

            _nextOwnershipUpdate -= Time.deltaTime;
            if (_nextOwnershipUpdate > 0.0f)
            {
                return;
            }

            _nextOwnershipUpdate = Mathf.Max(0.25f, PraetorisClientPlugin.CreatureOwnerWardUpdateIntervalSeconds.Value);
            if (ZNet.instance.IsServer())
            {
                CreatureOwnerWardServer.UpdateWard(_nview.GetZDO());
                return;
            }

            if (_nview.IsOwner() && Player.m_localPlayer != null)
            {
                SendUpdate(Player.m_localPlayer.GetPlayerID());
            }
        }

        public string GetHoverName()
        {
            return m_name;
        }

        public string GetHoverText()
        {
            if (!_nview.IsValid())
            {
                return "";
            }

            ShowAreaMarker();
            bool enabled = IsEnabled();
            string ownerName = GetOwnerName();
            if (ownerName.Length == 0)
            {
                ownerName = "not set";
            }

            StringBuilder text = new StringBuilder(256);
            text.Append(m_name);
            text.Append(enabled ? " (creature ownership active)" : " (creature ownership inactive)");
            text.Append("\nCurrent owner: ");
            text.Append(ownerName);
            text.Append("\nRadius: ");
            text.Append(m_radius.ToString("0.#"));
            text.Append("m");

            if (Player.m_localPlayer != null && IsCreator(Player.m_localPlayer.GetPlayerID()))
            {
                text.Append(enabled
                    ? "\n[<color=yellow><b>$KEY_Use</b></color>] Deactivate"
                    : "\n[<color=yellow><b>$KEY_Use</b></color>] Activate");
                text.Append("\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] Set owner");
            }

            return Localization.instance.Localize(text.ToString());
        }

        public bool Interact(Humanoid human, bool hold, bool alt)
        {
            if (hold || !_nview.IsValid())
            {
                return false;
            }

            Player? player = human as Player;
            if (player == null || !IsCreator(player.GetPlayerID()))
            {
                return false;
            }

            if (alt)
            {
                TextInput.instance.RequestText(this, "Set Player Name", OwnerNameCharacterLimit);
                return true;
            }

            SendSetEnabled(player.GetPlayerID(), !IsEnabled());
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            return false;
        }

        internal string CommandSetOwner(Player player, string ownerName)
        {
            if (!_nview.IsValid())
            {
                return "Owner ward has no valid ZDO.";
            }

            if (!IsCreator(player.GetPlayerID()))
            {
                return "Only the owner ward creator can set the creature owner.";
            }

            SendSetOwner(player.GetPlayerID(), (ownerName ?? "").Trim());
            return "Owner ward owner set request sent.";
        }

        internal string CommandSetEnabled(Player player, bool enabled)
        {
            if (!_nview.IsValid())
            {
                return "Owner ward has no valid ZDO.";
            }

            if (!IsCreator(player.GetPlayerID()))
            {
                return "Only the owner ward creator can toggle the owner ward.";
            }

            SendSetEnabled(player.GetPlayerID(), enabled);

            return "Owner ward active state request sent.";
        }

        internal string CommandStatus()
        {
            string ownerName = GetOwnerName();
            if (ownerName.Length == 0)
            {
                ownerName = "not set";
            }

            return "Owner ward " + GetDebugName() +
                   " active=" + IsEnabled() +
                   " owner='" + ownerName + "'" +
                   " radius=" + m_radius.ToString("0.#") + "m.";
        }

        public string GetText()
        {
            return GetOwnerName();
        }

        public void SetText(string text)
        {
            if (!_nview.IsValid() || Player.m_localPlayer == null)
            {
                return;
            }

            string ownerName = (text ?? "").Trim();
            SendSetOwner(Player.m_localPlayer.GetPlayerID(), ownerName);
        }

        private void SendSetOwner(long playerId, string ownerName)
        {
            if (ZRoutedRpc.instance == null)
            {
                return;
            }

            ZRoutedRpc.instance.InvokeRoutedRPC(
                ZRoutedRpc.instance.GetServerPeerID(),
                RpcNames.CreatureOwnerWardSetOwner,
                _nview.GetZDO().m_uid,
                playerId,
                ownerName);
        }

        private void SendSetEnabled(long playerId, bool enabled)
        {
            if (ZRoutedRpc.instance == null)
            {
                return;
            }

            ZRoutedRpc.instance.InvokeRoutedRPC(
                ZRoutedRpc.instance.GetServerPeerID(),
                RpcNames.CreatureOwnerWardSetEnabled,
                _nview.GetZDO().m_uid,
                playerId,
                enabled);
        }

        private void SendUpdate(long playerId)
        {
            if (ZRoutedRpc.instance == null)
            {
                return;
            }

            ZRoutedRpc.instance.InvokeRoutedRPC(
                ZRoutedRpc.instance.GetServerPeerID(),
                RpcNames.CreatureOwnerWardUpdate,
                _nview.GetZDO().m_uid,
                playerId);
        }

        private void UpdateStatus()
        {
            bool enabled = IsEnabled();
            if (_hasKnownStatus && enabled != _lastKnownEnabled)
            {
                EffectList effect = enabled ? m_activateEffect : m_deactivateEffect;
                effect.Create(transform.position, transform.rotation);
            }

            _hasKnownStatus = true;
            _lastKnownEnabled = enabled;

            if (m_enabledEffect != null)
            {
                m_enabledEffect.SetActive(enabled);
            }

            if (m_model != null)
            {
                foreach (Material material in m_model.materials)
                {
                    if (enabled)
                    {
                        material.EnableKeyword("_EMISSION");
                    }
                    else
                    {
                        material.DisableKeyword("_EMISSION");
                    }
                }
            }
        }

        private bool IsEnabled()
        {
            return _nview.IsValid() && _nview.GetZDO().GetBool(ZDOVars.s_enabled);
        }

        private string GetOwnerName()
        {
            return !_nview.IsValid() ? "" : _nview.GetZDO().GetString(CreatureOwnerWardRpc.OwnerNameHash, "");
        }

        private bool IsCreator(long playerId)
        {
            return _piece != null && (_piece.GetCreator() == 0L || _piece.GetCreator() == playerId);
        }

        private void ShowAreaMarker()
        {
            if (m_areaMarker == null)
            {
                return;
            }

            m_areaMarker.gameObject.SetActive(true);
            CancelInvoke(nameof(HideAreaMarker));
            Invoke(nameof(HideAreaMarker), 0.2f);
        }

        private void HideAreaMarker()
        {
            if (m_areaMarker != null)
            {
                m_areaMarker.gameObject.SetActive(false);
            }
        }

        private string GetDebugName()
        {
            return _nview.IsValid() ? _nview.GetZDO().m_uid.ToString() : gameObject.name;
        }

    }
}
