using System.Collections.Generic;
using UnityEngine;

namespace PraetorisClient.BuilderCameraFeature
{
    public sealed class BuilderWard : MonoBehaviour, Hoverable, Interactable
    {
        internal static readonly List<BuilderWard> Instances = new List<BuilderWard>();
        private const string FuelKey = "praetoris_builder_fuel";
        private ZNetView _view = null!;
        private readonly Dictionary<long, float> _paidUntil = new Dictionary<long, float>();
        internal float Fuel => _view && _view.IsValid() ? _view.GetZDO().GetFloat(FuelKey) : 0f;

        private void Awake()
        {
            _view = GetComponent<ZNetView>();
            if (!_view || !_view.IsValid()) return;
            Instances.Add(this);
            _view.Register<long>("BuilderFuel", AddFuel);
            _view.Register<long, int>("BuilderLease", Grant);
            _view.Register<int, float>("BuilderGrant", ReceiveGrant);
        }

        private void OnDestroy() { Instances.Remove(this); }

        public string GetHoverName() => "Builder's Ward";
        public float GetHoverOffset() => 0f;
        public string GetHoverText() => $"Builder's Ward\nFuel: {Fuel:0.0}/{BuilderCamera.MaxFuel.Value} greydwarf eyes\n[<color=yellow><b>$KEY_Use</b></color>] Add greydwarf eye\nEquip Builder Belt + build tool; {BuilderCamera.ToggleKey.Value} toggles camera";
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;
            return UseItem(user, user.GetInventory().GetItem("$item_greydwarfeye"));
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            if (!(user is Player player) || player != Player.m_localPlayer || !_view.IsValid() || !PrivateArea.CheckAccess(transform.position)) return false;
            if (item == null || !item.m_dropPrefab || item.m_dropPrefab.name != "GreydwarfEye")
            {
                player.Message(MessageHud.MessageType.Center, "Requires greydwarf eyes.");
                return false;
            }
            if (Fuel > BuilderCamera.MaxFuel.Value - 1f + 0.00001f)
            {
                player.Message(MessageHud.MessageType.Center, "Builder's Ward is full.");
                return false;
            }
            // Like vanilla fueled pieces, the interacting player pays before the owner records fuel.
            player.GetInventory().RemoveItem(item, 1);
            _view.InvokeRPC("BuilderFuel", player.GetPlayerID());
            return true;
        }

        private Player? RequestPlayer(long sender, long playerId)
        {
            Player player = Player.GetPlayer(playerId);
            ZNetView view = player ? player.GetComponent<ZNetView>() : null!;
            return view && view.IsValid() && view.GetZDO().GetOwner() == sender ? player : null;
        }

        private void AddFuel(long sender, long playerId)
        {
            if (!_view.IsOwner()) return;
            Player? player = RequestPlayer(sender, playerId);
            if (!player || Vector3.Distance(player.transform.position, transform.position) > 6f) return;
            _view.GetZDO().Set(FuelKey, Mathf.Min(BuilderCamera.MaxFuel.Value, Fuel + 1f));
        }

        internal void Request(Player player, int requestId) { _view.InvokeRPC("BuilderLease", player.GetPlayerID(), requestId); }

        private void Grant(long sender, long playerId, int requestId)
        {
            if (!_view.IsOwner()) return;
            Player? player = RequestPlayer(sender, playerId);
            float seconds = 0f;
            if (player && !player.IsDead() && Vector3.Distance(player.transform.position, transform.position) <= BuilderCamera.WardRange.Value)
            {
                if (_paidUntil.TryGetValue(sender, out float until) && until > Time.time)
                    seconds = until - Time.time;
                else
                {
                    seconds = Mathf.Min(1f, Fuel * BuilderCamera.SecondsPerEye.Value);
                    if (seconds > 0f)
                    {
                        float remaining = Mathf.Max(0f, Fuel - seconds / BuilderCamera.SecondsPerEye.Value);
                        _view.GetZDO().Set(FuelKey, remaining < 0.000001f ? 0f : remaining);
                        _paidUntil[sender] = Time.time + seconds;
                    }
                }
            }
            _view.InvokeRPC(sender, "BuilderGrant", requestId, seconds);
        }

        private void ReceiveGrant(long sender, int requestId, float seconds)
        {
            if (_view.IsValid() && sender == _view.GetZDO().GetOwner()) BuilderCamera.OnGrant(this, requestId, seconds);
        }
    }
}
