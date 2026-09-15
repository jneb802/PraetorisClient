using HarmonyLib;
using TMPro;
using UnityEngine;

namespace PraetorisClient.ShipPasswordFeature
{
    internal sealed class ShipPasswordInput : MonoBehaviour, TextReceiver
    {
        private enum InputMode
        {
            None,
            SetPassword,
            EnterPassword
        }

        private ShipControlls _controls = null!;
        private InputMode _mode;

        private void Awake()
        {
            _controls = GetComponent<ShipControlls>();
        }

        internal bool BeginSetPassword(Player player)
        {
            Piece piece = _controls.GetComponentInParent<Piece>();
            if (piece == null || !piece.IsCreator())
            {
                ShowMessage("Only the ship creator can set its password.");
                return false;
            }

            if (!CanUseInput(player, requireStandingOnShip: false))
            {
                return false;
            }

            return Open(InputMode.SetPassword, "Set ship password");
        }

        internal bool BeginEnterPassword(Player player)
        {
            if (!CanUseInput(player, requireStandingOnShip: true))
            {
                return false;
            }

            return Open(InputMode.EnterPassword, "Enter ship password");
        }

        public string GetText()
        {
            return "";
        }

        public void SetText(string text)
        {
            InputMode mode = _mode;
            _mode = InputMode.None;

            ZNetView? nview = _controls.m_ship != null ? _controls.m_ship.GetComponent<ZNetView>() : null;
            if (nview == null || !nview.IsValid())
            {
                ShowMessage("The ship is no longer available.");
                return;
            }

            string password = text ?? "";
            if (mode == InputMode.SetPassword)
            {
                ShipPasswordRpc.RequestSetPassword(nview.GetZDO().m_uid, password);
            }
            else if (mode == InputMode.EnterPassword)
            {
                if (password.Length == 0)
                {
                    ShowMessage("Enter the ship password.");
                    return;
                }

                ShipPasswordRpc.RequestControl(nview.GetZDO().m_uid, password);
            }
        }

        private bool CanUseInput(Player player, bool requireStandingOnShip)
        {
            if (player == null || player != Player.m_localPlayer || _controls.m_ship == null)
            {
                return false;
            }

            if (Vector3.Distance(player.transform.position, _controls.m_attachPoint.position) >= _controls.m_maxUseRange)
            {
                return false;
            }

            if (requireStandingOnShip && (player.IsEncumbered() || player.GetStandingOnShip() != _controls.m_ship))
            {
                return false;
            }

            return true;
        }

        private bool Open(InputMode mode, string topic)
        {
            if (TextInput.instance == null)
            {
                return false;
            }

            _mode = mode;
            ShipPasswordInputMask.Begin();
            TextInput.instance.RequestText(this, topic, ShipPasswordData.MaximumPasswordLength);
            return false;
        }

        private static void ShowMessage(string message)
        {
            if (Player.m_localPlayer != null)
            {
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, message);
            }
        }
    }

    internal static class ShipPasswordInputMask
    {
        private static bool _active;
        private static TMP_InputField.ContentType _previousContentType;

        internal static void Begin()
        {
            if (TextInput.instance == null || TextInput.instance.m_inputField == null)
            {
                return;
            }

            _previousContentType = TextInput.instance.m_inputField.contentType;
            TextInput.instance.m_inputField.contentType = TMP_InputField.ContentType.Password;
            TextInput.instance.m_inputField.ForceLabelUpdate();
            _active = true;
        }

        internal static void End()
        {
            if (!_active)
            {
                return;
            }

            _active = false;
            if (TextInput.instance == null || TextInput.instance.m_inputField == null)
            {
                return;
            }

            TextInput.instance.m_inputField.contentType = _previousContentType;
            TextInput.instance.m_inputField.ForceLabelUpdate();
        }
    }

    [HarmonyPatch(typeof(ShipControlls), "Awake")]
    internal static class ShipPasswordControlsAwakePatch
    {
        private static void Postfix(ShipControlls __instance)
        {
            if (__instance.GetComponent<ShipPasswordInput>() == null)
            {
                __instance.gameObject.AddComponent<ShipPasswordInput>();
            }
        }
    }

    [HarmonyPatch(typeof(ShipControlls), nameof(ShipControlls.Interact))]
    internal static class ShipPasswordInteractPatch
    {
        private static bool Prefix(
            ShipControlls __instance,
            Humanoid character,
            bool repeat,
            bool alt,
            ref bool __result)
        {
            if (repeat)
            {
                return true;
            }

            Player? player = character as Player;
            ShipPasswordInput? input = __instance.GetComponent<ShipPasswordInput>();
            if (player == null || input == null)
            {
                return true;
            }

            if (alt)
            {
                __result = input.BeginSetPassword(player);
                return false;
            }

            ZNetView? nview = __instance.m_ship != null ? __instance.m_ship.GetComponent<ZNetView>() : null;
            if (nview == null || !nview.IsValid() || !ShipPasswordData.IsProtected(nview.GetZDO()))
            {
                return true;
            }

            __result = input.BeginEnterPassword(player);
            return false;
        }
    }

    [HarmonyPatch(typeof(ShipControlls), nameof(ShipControlls.GetHoverText))]
    internal static class ShipPasswordHoverPatch
    {
        private static void Postfix(ShipControlls __instance, ref string __result)
        {
            ZNetView? nview = __instance.m_ship != null ? __instance.m_ship.GetComponent<ZNetView>() : null;
            if (nview == null || !nview.IsValid())
            {
                return;
            }

            bool protectedShip = ShipPasswordData.IsProtected(nview.GetZDO());
            if (protectedShip)
            {
                __result += "\nPassword protected";
            }

            Piece? piece = __instance.GetComponentInParent<Piece>();
            if (piece != null && piece.IsCreator())
            {
                string action = protectedShip ? "Change or clear password" : "Set password";
                __result += "\n[<color=yellow><b>" +
                            Localization.instance.Localize("$KEY_AltPlace + $KEY_Use") +
                            "</b></color>] " + action;
            }
        }
    }

    [HarmonyPatch(typeof(ShipControlls), "RPC_RequestControl")]
    internal static class ShipPasswordVanillaControlPatch
    {
        private static bool Prefix(ShipControlls __instance)
        {
            ZNetView? nview = __instance.m_ship != null ? __instance.m_ship.GetComponent<ZNetView>() : null;
            return nview == null || !nview.IsValid() || !ShipPasswordData.IsProtected(nview.GetZDO());
        }
    }

    [HarmonyPatch(typeof(TextInput), nameof(TextInput.Hide))]
    internal static class ShipPasswordTextInputHidePatch
    {
        private static void Prefix()
        {
            ShipPasswordInputMask.End();
        }
    }
}
