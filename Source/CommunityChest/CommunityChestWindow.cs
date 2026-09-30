using System;
using System.Collections.Generic;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace PraetorisClient.CommunityChestFeature
{
    internal sealed class CommunityChestWindow : MonoBehaviour
    {
        private const float Width = 600;
        private const float Height = 480;
        private static CommunityChestWindow? _instance;
        private static int _closedFrame = -1;
        private CommunityChest? _chest;
        private GameObject _panel = null!;
        private Text _balance = null!;
        private Text _carried = null!;
        private Text _message = null!;
        private InputField _amount = null!;
        private readonly List<Button> _transfers = new List<Button>();
        private bool _open;
        internal static bool IsOpen => _instance != null && _instance._open;
        internal static bool BlocksMenu => IsOpen || _closedFrame == Time.frameCount;

        internal static void Open(CommunityChest chest)
        {
            if (_instance == null) _instance = Player.m_localPlayer.gameObject.AddComponent<CommunityChestWindow>();
            Close();
            if (_instance._panel == null) _instance.CreatePanel();
            if (InventoryGui.instance != null) InventoryGui.instance.Hide();
            _instance._chest = chest;
            _instance._amount.text = "1";
            _instance._message.text = "Loading your balance...";
            _instance._balance.text = "Stored coins: loading...";
            _instance._panel!.SetActive(true);
            _instance._open = true;
            GUIManager.BlockInput(true);
        }

        internal static void Close()
        {
            if (_instance == null || !_instance._open) return;
            _instance._open = false;
            _closedFrame = Time.frameCount;
            if (_instance._panel != null) _instance._panel.SetActive(false);
            GUIManager.BlockInput(false);
        }

        internal static void SetMessage(string message)
        {
            if (_instance != null && _instance._message != null) _instance._message.text = message;
        }

        private void OnDestroy()
        {
            if (_instance != this) return;
            Close();
            if (_panel != null) Destroy(_panel);
            CommunityChestClient.Reset();
            _instance = null;
        }

        private void Update()
        {
            CommunityChestClient.Tick();
            if (!_open) return;
            Player player = Player.m_localPlayer;
            if (_chest == null || player == null || player.IsDead() || player.IsTeleporting() ||
                Vector3.Distance(player.transform.position, _chest.transform.position) > 5f ||
                ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyButtonB"))
            {
                Close();
                ZInput.ResetButtonStatus("JoyButtonB");
                return;
            }
            Rect parent = ((RectTransform)_panel.transform.parent).rect;
            _panel.transform.localScale = Vector3.one * Mathf.Min(1f,
                Mathf.Min(parent.width / (Width + 40), parent.height / (Height + 40)));
            bool busy = CommunityChestClient.Busy;
            if (!busy) _balance.text = "Stored coins: " + CommunityChestClient.Balance.ToString("N0") +
                " / " + CommunityChestStore.Capacity.ToString("N0");
            _carried.text = "Carried coins: " + player.GetInventory().CountItems(CommunityChestClient.CoinName).ToString("N0");
            foreach (Button button in _transfers) button.interactable = !busy;
            if (busy) _message.text = "Waiting for the server...";
        }

        private void Transfer(bool deposit)
        {
            if (!int.TryParse(_amount.text, out int amount) || amount <= 0 || amount > CommunityChestStore.Capacity)
            {
                SetMessage("Enter an amount from 1 to " + CommunityChestStore.Capacity.ToString("N0") + ".");
                return;
            }
            CommunityChestClient.Transfer(deposit ? amount : -amount);
        }

        private void ChangeAmount(int delta)
        {
            int.TryParse(_amount.text, out int amount);
            _amount.text = Mathf.Clamp(amount + delta, 1, CommunityChestStore.Capacity).ToString();
        }

        private static void Place(GameObject target, float x, float y, float width, float height)
        {
            RectTransform rect = (RectTransform)target.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private Text Label(string value, float x, float y, float width, float height, int size = 20)
        {
            GUIManager gui = GUIManager.Instance;
            GameObject obj = gui.CreateText(value, _panel.transform, Vector2.zero, Vector2.zero, Vector2.zero,
                gui.AveriaSerif, size, Color.white, true, Color.black, width, height, false);
            Place(obj, x, y, width, height);
            Text text = obj.GetComponent<Text>();
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            text.supportRichText = false;
            return text;
        }

        private Button Button(string label, float x, float y, float width, Action click, bool transfer = false)
        {
            GameObject obj = GUIManager.Instance.CreateButton(label, _panel.transform,
                Vector2.zero, Vector2.zero, Vector2.zero, width, 38);
            Place(obj, x, y, width, 38);
            Button button = obj.GetComponent<Button>();
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            button.onClick.AddListener(() => click());
            if (transfer) _transfers.Add(button);
            return button;
        }

        private void CreatePanel()
        {
            _panel = GUIManager.Instance.CreateWoodpanel(GUIManager.CustomGUIFront.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Width, Height, false);
            _panel.name = "CommunityChestBank";
            _panel.SetActive(false);
            Label("Community Chest", 30, 18, 540, 48, 34);
            Label("Your personal coin bank", 30, 65, 540, 30);
            _balance = Label("", 30, 107, 540, 36, 26);
            _carried = Label("", 30, 145, 540, 30);
            Label("Amount", 30, 188, 110, 38);
            Button("-", 145, 188, 50, () => ChangeAmount(-1));
            GameObject input = DefaultControls.CreateInputField(new DefaultControls.Resources());
            input.transform.SetParent(_panel.transform, false);
            input.name = "CoinAmount";
            Place(input, 205, 188, 190, 38);
            _amount = input.GetComponent<InputField>();
            _amount.contentType = InputField.ContentType.IntegerNumber;
            _amount.characterLimit = 5;
            _amount.textComponent.font = GUIManager.Instance.AveriaSerif;
            _amount.textComponent.fontSize = 22;
            _amount.textComponent.alignment = TextAnchor.MiddleCenter;
            _amount.text = "1";
            Button("+", 405, 188, 50, () => ChangeAmount(1));
            for (int index = 0; index < 4; index++)
            {
                int amount = (int)Math.Pow(10, index);
                Button(amount.ToString(), 65 + index * 120, 235, 110, () => _amount.text = amount.ToString());
            }
            Button("Deposit", 65, 286, 230, () => Transfer(true), true);
            Button("Withdraw", 305, 286, 230, () => Transfer(false), true);
            Button("Deposit all", 65, 332, 230, () => CommunityChestClient.TransferAll(true), true);
            Button("Withdraw all", 305, 332, 230, () => CommunityChestClient.TransferAll(false), true);
            _message = Label("", 30, 377, 540, 48, 18);
            Button close = Button("Close", 215, 429, 170, Close);
            _panel.AddComponent<CanvasGroup>();
            UIGroupHandler group = _panel.AddComponent<UIGroupHandler>();
            group.m_groupPriority = 50;
            group.m_defaultElement = close.gameObject;
        }
    }

    // Prevent Escape/controller Back from also opening the pause menu behind the bank.
    [HarmonyPatch(typeof(Menu), "Update")]
    internal static class CommunityChestMenuPatch
    {
        private static bool Prefix() => !CommunityChestWindow.BlocksMenu;
    }
}
