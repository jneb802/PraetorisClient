using System;
using GUIFramework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PraetorisClient.ServerChestFeature
{
    internal sealed class ServerChestRegistrationPanel : MonoBehaviour
    {
        private static ServerChestRegistrationPanel? _instance;

        private GameObject? _panel;
        private GuiInputField? _input;
        private TMP_Text? _topic;
        private ServerChest? _chest;

        internal static void Open(ServerChest chest)
        {
            ServerChestRegistrationPanel panel = EnsureInstance();
            panel.Show(chest);
        }

        private static ServerChestRegistrationPanel EnsureInstance()
        {
            if (_instance != null)
            {
                return _instance;
            }

            GameObject root = new("ServerChestRegistrationPanel");
            Object.DontDestroyOnLoad(root);
            _instance = root.AddComponent<ServerChestRegistrationPanel>();
            _instance.Build();
            return _instance;
        }

        private void Build()
        {
            if (TextInput.instance == null || TextInput.instance.m_panel == null)
            {
                PraetorisClientPlugin.Log.LogWarning("TextInput panel is unavailable; ServerChest registration UI cannot be created yet.");
                return;
            }

            Transform parent = TextInput.instance.m_panel.transform.parent;
            _panel = Object.Instantiate(TextInput.instance.m_panel, parent);
            _panel.name = "ServerChestRegistrationPanel";
            _panel.SetActive(false);
            _input = _panel.GetComponentInChildren<GuiInputField>(true);
            _topic = FindTopicText(_panel);
            ConfigureButtons(_panel);
        }

        private void Show(ServerChest chest)
        {
            _chest = chest;
            if (_panel == null)
            {
                Build();
            }

            if (_panel == null || _input == null)
            {
                ServerChest.ShowMessage("ServerChest registration panel is unavailable.");
                return;
            }

            if (_topic != null)
            {
                _topic.text = "Register ServerChest";
            }

            _input.characterLimit = 120;
            _input.text = chest.GetRegistrationPreview();
            _panel.SetActive(true);
            _input.ActivateInputField();
        }

        private void Hide()
        {
            if (_panel != null)
            {
                _panel.SetActive(false);
            }

            _chest = null;
        }

        private void Register()
        {
            ServerChest? chest = _chest;
            Hide();
            if (chest != null)
            {
                chest.RequestRegistration();
            }
        }

        private TMP_Text? FindTopicText(GameObject panel)
        {
            TMP_Text[] texts = panel.GetComponentsInChildren<TMP_Text>(true);
            foreach (TMP_Text text in texts)
            {
                if (TextInput.instance != null && TextInput.instance.m_topic != null && text.name == TextInput.instance.m_topic.name)
                {
                    return text;
                }
            }

            return texts.Length > 0 ? texts[0] : null;
        }

        private void ConfigureButtons(GameObject panel)
        {
            Button[] buttons = panel.GetComponentsInChildren<Button>(true);
            if (buttons.Length == 0)
            {
                PraetorisClientPlugin.Log.LogWarning("ServerChest registration panel has no buttons.");
                return;
            }

            for (int index = 0; index < buttons.Length; index++)
            {
                Button button = buttons[index];
                button.onClick.RemoveAllListeners();
                bool registerButton = index == buttons.Length - 1;
                SetButtonLabel(button, registerButton ? "Register" : "Cancel");
                if (registerButton)
                {
                    button.onClick.AddListener(Register);
                }
                else
                {
                    button.onClick.AddListener(Hide);
                }
            }
        }

        private static void SetButtonLabel(Button button, string label)
        {
            TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.text = label;
            }
        }
    }
}
