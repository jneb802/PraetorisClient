using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PraetorisClient.BodyBounties;

// Use the same native artwork and controls as the crafting and shipping panels.
internal static class BountyUi
{
    internal static readonly Color Gold = new(1f, 0.85f, 0.3f);

    internal static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
    {
        GameObject item = new(name, typeof(RectTransform));
        item.transform.SetParent(parent, false);
        return Place((RectTransform)item.transform, x, y, width, height);
    }

    internal static RectTransform Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        rect.localScale = Vector3.one;
        return rect;
    }

    internal static Image Surface(Transform parent, string path, float x, float y, float width, float height)
    {
        Image image = Rect("Surface", parent, x, y, width, height).gameObject.AddComponent<Image>();
        Transform source = InventoryGui.instance.m_crafting.Find(path);
        Image template = source != null ? source.GetComponent<Image>() : null!;
        if (template != null)
        {
            image.sprite = template.sprite;
            image.material = template.material;
            image.type = template.type;
            image.color = template.color;
            image.pixelsPerUnitMultiplier = template.pixelsPerUnitMultiplier;
        }
        else image.color = new Color(0, 0, 0, 0.65f);
        image.raycastTarget = false;
        return image;
    }

    internal static TMP_Text Label(Transform parent, string value, float x, float y, float width, float height, float size = 18)
    {
        TMP_Text text = UnityEngine.Object.Instantiate(InventoryGui.instance.m_recipeDecription, parent);
        text.name = "Label";
        Place(text.rectTransform, x, y, width, height);
        text.gameObject.SetActive(true);
        text.fontSize = size;
        text.enableAutoSizing = false;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.richText = false;
        text.raycastTarget = false;
        text.text = value;
        return text;
    }

    internal static Button Button(Transform parent, string title, float x, float y, float width, Action action, bool selected = false, bool tab = false)
    {
        Button source = tab ? InventoryGui.instance.m_tabCraft : InventoryGui.instance.m_craftButton;
        Button button = UnityEngine.Object.Instantiate(source, parent);
        button.name = title;
        Place((RectTransform)button.transform, x, y, width, 38);
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(() => action());
        button.interactable = !selected;
        foreach (UIGamePad shortcut in button.GetComponentsInChildren<UIGamePad>(true))
        {
            if (shortcut.m_hint != null) shortcut.m_hint.SetActive(false);
            UnityEngine.Object.DestroyImmediate(shortcut);
        }
        foreach (UITooltip tooltip in button.GetComponentsInChildren<UITooltip>(true)) tooltip.enabled = false;
        // The cloned buttons carry extra labels (a pixel-font key hint and a gamepad hint) under the main
        // label. Title only the main label and hide the rest. The craft button's main label is 18 px high, so
        // stretch it over the button; a fixed 18 pt font in that rect is truncated to nothing.
        TMP_Text[] labels = button.GetComponentsInChildren<TMP_Text>(true);
        TMP_Text? primary = Array.Find(labels, label => label.transform.parent == button.transform) ?? (labels.Length > 0 ? labels[0] : null);
        foreach (TMP_Text label in labels)
            if (label != primary) label.gameObject.SetActive(false);
        if (primary != null)
        {
            RectTransform text = primary.rectTransform;
            text.anchorMin = Vector2.zero; text.anchorMax = Vector2.one;
            text.offsetMin = new Vector2(6, 2); text.offsetMax = new Vector2(-6, -2);
            primary.text = title;
            primary.richText = false;
            primary.alignment = TextAlignmentOptions.Center;
            primary.textWrappingMode = TextWrappingModes.NoWrap;
            primary.overflowMode = TextOverflowModes.Ellipsis;
            primary.enableAutoSizing = true;
            primary.fontSizeMin = 12;
            primary.fontSizeMax = 18;
            primary.gameObject.SetActive(true);
            if (selected)
            {
                // A selected tab is not interactable. Its "Selected" image is drawn after the label and is the same
                // orange as the label, and ButtonTextColor recolors a non-interactable label every frame. Draw the
                // label above the image in white.
                primary.transform.SetAsLastSibling();
                ButtonTextColor textColor = button.GetComponentInChildren<ButtonTextColor>(true);
                if (textColor != null) textColor.m_disabledColor = Color.white;
                primary.color = Color.white;
            }
        }
        Transform frame = button.transform.Find("Selected");
        if (frame != null) frame.gameObject.SetActive(selected);
        button.gameObject.SetActive(true);
        return button;
    }

    internal static ScrollRect Scroll(Transform parent, float x, float y, float width, float height)
    {
        RectTransform viewport = Rect("Viewport", parent, x, y, width, height);
        viewport.gameObject.AddComponent<RectMask2D>();
        viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.01f);
        ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30;
        scroll.content = Rect("Rows", viewport, 0, 0, width - 18, height);
        // The recipe list has no ScrollRect parent; InventoryGui keeps its scrollbar in a field.
        Scrollbar source = InventoryGui.instance.m_recipeListScroll;
        if (source != null)
        {
            Scrollbar bar = UnityEngine.Object.Instantiate(source, viewport);
            Place((RectTransform)bar.transform, width - 14, 0, 12, height);
            bar.onValueChanged = new Scrollbar.ScrollEvent();
            bar.gameObject.SetActive(true);
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }
        return scroll;
    }

    internal static string State(BountyRecord record) => record.State switch
    {
        BountyState.InTransit => "In transit",
        BountyState.Collecting => "Being collected",
        BountyState.Refunding => "Refund pending",
        _ => record.State.ToString()
    };

    internal static bool Available(BountyRecord record) => record.State == BountyState.Posted || record.State == BountyState.Accepted;
    internal static bool CanAccept(BountyRecord record, long player) => record.Owner != player && !record.Accepted.Contains(player)
        && (Available(record) || record.State == BountyState.InTransit);
    internal static Vector3 Position(BountyRecord record) => new(record.X, record.Y, record.Z);
    internal static float Distance(BountyRecord record) => global::Utils.DistanceXZ(Player.m_localPlayer.transform.position, Position(record));
}
