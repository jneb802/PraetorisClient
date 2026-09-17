using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PraetorisClient.ServerGuideFeature
{
    internal sealed class GuideWindow : MonoBehaviour
    {
        internal static GuideReader? Reader { get; private set; }
        private Sprite _icon = null!;

        internal static void Create(InventoryGui inventory)
        {
            GuideWindow window = inventory.gameObject.AddComponent<GuideWindow>();
            TextsDialog dialog = Instantiate(inventory.m_textsDialog, inventory.m_textsDialog.transform.parent);
            dialog.name = "PraetorisServerGuide";
            dialog.gameObject.SetActive(false);
            dialog.enabled = false;
            Reader = dialog.gameObject.AddComponent<GuideReader>();
            Reader.Initialize(dialog);
            foreach (TMP_Text text in dialog.GetComponentsInChildren<TMP_Text>(true))
                if (IsCompendiumTitle(text.text))
                    text.text = "Server Guide";
            foreach (Text text in dialog.GetComponentsInChildren<Text>(true))
                if (IsCompendiumTitle(text.text))
                    text.text = "Server Guide";
            foreach (Button button in dialog.GetComponentsInChildren<Button>(true))
                if (Calls(button, "OnClose"))
                {
                    button.onClick = new Button.ButtonClickedEvent();
                    button.onClick.AddListener(Reader.Close);
                }
            DestroyImmediate(dialog);

            Button source = inventory.GetComponentsInChildren<Button>(true).First(button => Calls(button, "OnOpenTexts"));
            // Place the book in the right side of the crafting panel's header.
            GameObject iconObject = new GameObject("ServerGuideIcon", typeof(RectTransform), typeof(Image), typeof(Button), typeof(UITooltip));
            RectTransform rect = (RectTransform)iconObject.transform;
            rect.SetParent(inventory.m_crafting, false);
            iconObject.layer = source.gameObject.layer;
            rect.anchorMin = rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(-48, -42);
            rect.sizeDelta = new Vector2(64, 64);
            window._icon = BookIcon();
            Image image = iconObject.GetComponent<Image>();
            image.sprite = window._icon;
            image.color = new Color(1f, 0.76f, 0.38f);
            Button open = iconObject.GetComponent<Button>();
            open.targetGraphic = image;
            open.onClick.AddListener(() => Open(inventory));
            UITooltip tooltip = iconObject.GetComponent<UITooltip>();
            tooltip.m_tooltipPrefab = source.GetComponent<UITooltip>()?.m_tooltipPrefab
                ?? inventory.GetComponentsInChildren<UITooltip>(true).First(item => item.m_tooltipPrefab != null).m_tooltipPrefab;
            tooltip.m_text = "Server Guide";
        }

        internal static void Open(InventoryGui inventory)
        {
            if (Reader == null || Player.m_localPlayer == null) return;
            inventory.m_textsDialog.gameObject.SetActive(false);
            inventory.m_skillsDialog.gameObject.SetActive(false);
            inventory.m_trophiesPanel.SetActive(false);
            Reader.transform.SetAsLastSibling();
            Reader.Open();
        }

        private static bool Calls(Button button, string method) =>
            Enumerable.Range(0, button.onClick.GetPersistentEventCount()).Any(index => button.onClick.GetPersistentMethodName(index) == method);

        private static bool IsCompendiumTitle(string text) =>
            text.IndexOf("compendium", StringComparison.OrdinalIgnoreCase) >= 0 ||
            Localization.instance.Localize(text).IndexOf("compendium", StringComparison.OrdinalIgnoreCase) >= 0;

        private static Sprite BookIcon()
        {
            // A small, distinct open-book silhouette; no external image download or asset bundle.
            Texture2D texture = new Texture2D(48, 48, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[48 * 48];
            for (int y = 8; y < 39; y++)
                for (int x = 4; x < 44; x++)
                {
                    int edge = x < 24 ? x - 4 : 43 - x;
                    int slope = edge / 6;
                    bool page = y >= 10 - slope && y <= 38 - slope;
                    bool line = (y == 19 - slope || y == 26 - slope || y == 33 - slope) && x > 8 && x < 39;
                    if (page && !line && x != 23 && x != 24) pixels[y * 48 + x] = Color.white;
                }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, 48, 48), new Vector2(0.5f, 0.5f));
        }

        private void OnDestroy()
        {
            if (Reader != null) Destroy(Reader.gameObject);
            Reader = null;
            if (_icon != null) { Destroy(_icon.texture); Destroy(_icon); }
        }
    }
}
