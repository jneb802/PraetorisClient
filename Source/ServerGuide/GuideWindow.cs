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

            Button source = inventory.m_repairButton;
            RectTransform repairRect = (RectTransform)source.transform;
            // Attach to the screen-navigation panel, away from crafting and mod filters.
            RectTransform backdrop = Instantiate((RectTransform)inventory.m_repairPanel, inventory.m_infoPanel, false);
            backdrop.name = "ServerGuideBackdrop";
            backdrop.anchorMin = backdrop.anchorMax = new Vector2(0f, 0.5f);
            backdrop.pivot = new Vector2(1f, 0.5f);
            // Repair's wide backing normally extends behind the crafting panel.
            backdrop.sizeDelta = Vector2.one * ((RectTransform)inventory.m_repairPanel).rect.height;
            backdrop.anchoredPosition = Vector2.zero;
            backdrop.gameObject.SetActive(true);
            foreach (Graphic graphic in backdrop.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;
            // Copy the repair button's appearance without its repair action or gamepad shortcut.
            GameObject iconObject = new GameObject("ServerGuideIcon", typeof(RectTransform), typeof(Image), typeof(Button), typeof(UITooltip));
            RectTransform rect = (RectTransform)iconObject.transform;
            rect.SetParent(backdrop, false);
            iconObject.layer = source.gameObject.layer;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = repairRect.sizeDelta;
            rect.localScale = repairRect.localScale;
            Image frame = iconObject.GetComponent<Image>();
            Image sourceFrame = source.GetComponent<Image>();
            frame.sprite = sourceFrame.sprite;
            frame.type = sourceFrame.type;
            frame.material = sourceFrame.material;
            frame.color = sourceFrame.color;
            GameObject book = new GameObject("Book", typeof(RectTransform), typeof(Image));
            book.layer = iconObject.layer;
            RectTransform bookRect = (RectTransform)book.transform;
            bookRect.SetParent(rect, false);
            bookRect.anchorMin = Vector2.zero;
            bookRect.anchorMax = Vector2.one;
            bookRect.offsetMin = new Vector2(8f, 8f);
            bookRect.offsetMax = new Vector2(-8f, -8f);
            window._icon = BookIcon();
            Image image = book.GetComponent<Image>();
            image.sprite = window._icon;
            image.preserveAspect = true;
            image.raycastTarget = false;
            Button open = iconObject.GetComponent<Button>();
            open.targetGraphic = frame;
            open.transition = source.transition;
            open.colors = source.colors;
            open.spriteState = source.spriteState;
            open.navigation = new Navigation { mode = Navigation.Mode.None };
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
            // Draw at twice the display size for smooth edges on the small button.
            const int size = 128;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            Color[] pixels = new Color[size * size];
            Color cover = new Color(0.32f, 0.16f, 0.07f);
            Color edge = new Color(0.78f, 0.49f, 0.18f);
            Color ink = new Color(0.48f, 0.31f, 0.14f);
            for (int y = 16; y < 112; y++)
                for (int x = 10; x < 118; x++)
                {
                    float distance = Mathf.Abs(x - 63.5f);
                    float slope = distance * 0.22f;
                    float bottom = 20f + slope;
                    float top = 94f + slope;
                    if (y < bottom - 5f || y > top + 3f) continue;
                    Color color = cover;
                    if (x >= 13 && x <= 114 && y >= bottom - 2f && y <= top + 1f) color = edge;
                    if (x >= 17 && x <= 110 && distance > 3f && y >= bottom + 4f && y <= top - 3f)
                    {
                        float shade = Mathf.Clamp01(distance / 24f);
                        color = Color.Lerp(new Color(0.67f, 0.48f, 0.25f), new Color(1f, 0.9f, 0.65f), shade);
                        float row = y - slope;
                        bool line = (row >= 43f && row < 46f) || (row >= 56f && row < 59f) || (row >= 69f && row < 72f) || (row >= 82f && row < 85f);
                        if (line && distance > 12f && distance < 39f) color = ink;
                    }
                    pixels[y * size + x] = color;
                }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        private void OnDestroy()
        {
            if (Reader != null) Destroy(Reader.gameObject);
            Reader = null;
            if (_icon != null) { Destroy(_icon.texture); Destroy(_icon); }
        }
    }
}
