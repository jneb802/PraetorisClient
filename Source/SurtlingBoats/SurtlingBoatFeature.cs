using System;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace PraetorisClient.SurtlingBoats
{
    internal static class SurtlingBoatFeature
    {
        internal const string ToggleRpcName = "PraetorisClient_SurtlingBoatToggle";
        internal const string EnabledZdoKey = "PraetorisClient_SurtlingBoatEnabled";
        internal const string FuelSecondsZdoKey = "PraetorisClient_SurtlingBoatFuelSeconds";

        private static GameObject? _fuelIcon;
        private static string _fuelIconPrefab = "";

        internal static void Update()
        {
            Player? player = Player.m_localPlayer;
            Ship? ship = player?.GetControlledShip();
            if (player == null || ship == null || !player.TakeInput())
            {
                SetFuelIconVisible(false);
                return;
            }

            ConfigEntry<KeyboardShortcut>? toggleKey = PraetorisClientPlugin.SurtlingBoatToggleKey;
            if (PraetorisClientPlugin.SurtlingBoatsEnabled.Value && toggleKey != null && toggleKey.Value.IsDown())
            {
                RequestToggle(ship, player);
            }

            UpdateFuelIcon(ship);
        }

        internal static void Shutdown()
        {
            if (_fuelIcon != null)
            {
                UnityEngine.Object.Destroy(_fuelIcon);
                _fuelIcon = null;
            }

            _fuelIconPrefab = "";
        }

        internal static bool IsEnabled(Ship ship)
        {
            ZNetView? netView = ship.GetComponent<ZNetView>();
            return netView != null && netView.IsValid() && netView.GetZDO().GetBool(EnabledZdoKey, false);
        }

        internal static float GetFuelSeconds(Ship ship)
        {
            if (PraetorisClientPlugin.SurtlingBoatFreeFuel.Value)
            {
                return float.PositiveInfinity;
            }

            ZNetView? netView = ship.GetComponent<ZNetView>();
            if (netView == null || !netView.IsValid())
            {
                return 0f;
            }

            if (!netView.IsOwner())
            {
                return Mathf.Max(0f, netView.GetZDO().GetFloat(FuelSecondsZdoKey, 0f));
            }

            return GetFuelState(ship, netView).RemainingSeconds;
        }

        internal static void SetFuelSeconds(Ship ship, float seconds, bool forceNetworkSync = false)
        {
            ZNetView? netView = ship.GetComponent<ZNetView>();
            if (netView != null && netView.IsValid() && netView.IsOwner())
            {
                SurtlingBoatFuelState state = GetFuelState(ship, netView);
                state.RemainingSeconds = Mathf.Max(0f, seconds);
                if (forceNetworkSync || state.RemainingSeconds <= 0f || Time.time >= state.NextNetworkSyncAt)
                {
                    netView.GetZDO().Set(FuelSecondsZdoKey, state.RemainingSeconds);
                    state.NextNetworkSyncAt = Time.time + 1f;
                }
            }
        }

        internal static float GetBoost(Ship.Speed speed)
        {
            switch (speed)
            {
                case Ship.Speed.Back:
                    return Mathf.Max(0f, PraetorisClientPlugin.SurtlingBoatBackBoost.Value);
                case Ship.Speed.Slow:
                    return Mathf.Max(0f, PraetorisClientPlugin.SurtlingBoatSlowBoost.Value);
                case Ship.Speed.Half:
                    return Mathf.Max(0f, PraetorisClientPlugin.SurtlingBoatHalfBoost.Value);
                case Ship.Speed.Full:
                    return Mathf.Max(0f, PraetorisClientPlugin.SurtlingBoatFullBoost.Value);
                default:
                    return 0f;
            }
        }

        internal static bool TryConsumeFuel(Ship ship, out float fuelSeconds)
        {
            fuelSeconds = GetFuelSeconds(ship);
            if (PraetorisClientPlugin.SurtlingBoatFreeFuel.Value)
            {
                return true;
            }

            if (fuelSeconds > 0f)
            {
                return true;
            }

            float secondsPerFuelItem = Mathf.Max(0f, PraetorisClientPlugin.SurtlingBoatSecondsPerFuelItem.Value);
            if (secondsPerFuelItem <= 0f)
            {
                return false;
            }

            Container? container = ship.GetComponentInChildren<Container>();
            Inventory? inventory = container?.GetInventory();
            string fuelPrefabName = PraetorisClientPlugin.SurtlingBoatFuelItemPrefab.Value.Trim();
            GameObject? fuelPrefab = ObjectDB.instance?.GetItemPrefab(fuelPrefabName);
            ItemDrop? fuelItem = fuelPrefab?.GetComponent<ItemDrop>();
            string? sharedName = fuelItem?.m_itemData.m_shared.m_name;
            if (inventory == null || string.IsNullOrEmpty(sharedName) || !inventory.HaveItem(sharedName, false))
            {
                return false;
            }

            inventory.RemoveItem(sharedName, 1, -1, false);
            fuelSeconds = secondsPerFuelItem;
            SetFuelSeconds(ship, fuelSeconds, true);
            return true;
        }

        internal static void HandleToggle(Ship ship, long requestedPlayerId)
        {
            ZNetView? netView = ship.GetComponent<ZNetView>();
            if (netView == null || !netView.IsValid() || !netView.IsOwner() || ship.m_shipControlls.GetUser() != requestedPlayerId)
            {
                return;
            }

            bool enabled = !netView.GetZDO().GetBool(EnabledZdoKey, false);
            if (!enabled)
            {
                SetFuelSeconds(ship, GetFuelSeconds(ship), true);
            }

            netView.GetZDO().Set(EnabledZdoKey, enabled);
        }

        private static void RequestToggle(Ship ship, Player player)
        {
            ZNetView? netView = ship.GetComponent<ZNetView>();
            if (netView == null || !netView.IsValid())
            {
                return;
            }

            bool willEnable = !IsEnabled(ship);
            netView.InvokeRPC(ToggleRpcName, player.GetPlayerID());
            player.Message(MessageHud.MessageType.Center, willEnable ? "Surtling motor enabled" : "Surtling motor disabled");
        }

        private static void UpdateFuelIcon(Ship ship)
        {
            if (!PraetorisClientPlugin.SurtlingBoatsEnabled.Value || !IsEnabled(ship) || Hud.m_instance == null)
            {
                SetFuelIconVisible(false);
                return;
            }

            Image? image = GetOrCreateFuelIcon();
            if (image == null)
            {
                return;
            }

            RectTransform rectTransform = image.rectTransform;
            rectTransform.SetParent(Hud.m_instance.m_shipWindIndicatorRoot, false);
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = new Vector2(25f, 25f);
            image.color = GetFuelSeconds(ship) > 0f ? Color.white : new Color(1f, 0.3f, 0.2f, 0.8f);
            image.gameObject.SetActive(true);
        }

        private static Image? GetOrCreateFuelIcon()
        {
            string fuelPrefabName = PraetorisClientPlugin.SurtlingBoatFuelItemPrefab.Value.Trim();
            if (_fuelIcon != null && string.Equals(_fuelIconPrefab, fuelPrefabName, StringComparison.Ordinal))
            {
                return _fuelIcon.GetComponent<Image>();
            }

            Shutdown();
            GameObject? fuelPrefab = ObjectDB.instance?.GetItemPrefab(fuelPrefabName);
            ItemDrop? fuelItem = fuelPrefab?.GetComponent<ItemDrop>();
            Sprite? sprite = fuelItem?.m_itemData.GetIcon();
            if (sprite == null)
            {
                return null;
            }

            _fuelIcon = new GameObject("PraetorisClient Surtling Boat Fuel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _fuelIconPrefab = fuelPrefabName;
            Image image = _fuelIcon.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        private static void SetFuelIconVisible(bool visible)
        {
            if (_fuelIcon != null)
            {
                _fuelIcon.SetActive(visible);
            }
        }

        private static SurtlingBoatFuelState GetFuelState(Ship ship, ZNetView netView)
        {
            SurtlingBoatFuelState? state = ship.GetComponent<SurtlingBoatFuelState>();
            if (state == null)
            {
                state = ship.gameObject.AddComponent<SurtlingBoatFuelState>();
            }

            long ownerId = netView.GetZDO().GetOwner();
            if (!state.Initialized || state.OwnerId != ownerId)
            {
                state.Initialized = true;
                state.OwnerId = ownerId;
                state.RemainingSeconds = Mathf.Max(0f, netView.GetZDO().GetFloat(FuelSecondsZdoKey, 0f));
                state.NextNetworkSyncAt = 0f;
            }

            return state;
        }
    }

    internal sealed class SurtlingBoatFuelState : MonoBehaviour
    {
        internal bool Initialized;
        internal long OwnerId;
        internal float RemainingSeconds;
        internal float NextNetworkSyncAt;
    }
}
