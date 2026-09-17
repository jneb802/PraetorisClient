using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PraetorisClient.ServerGuideFeature
{
    internal sealed class GuideReader : MonoBehaviour
    {
        private TextsDialog _dialog = null!;
        private readonly Dictionary<TextsDialog.TextInfo, GuidePage> _pages = new Dictionary<TextsDialog.TextInfo, GuidePage>();
        private readonly GuideHistory _history = new GuideHistory();
        private ScrollRect? _original;
        private Scrollbar _nativeScrollbar = null!;
        private ScrollRect _scroll = null!;
        private RectTransform _content = null!;
        private GameObject _root = null!;
        private Button _back = null!;
        private Button _forward = null!;
        private GuidePage? _current;
        private string _revision = "";
        private int _assetVersion;
        private int _renderVersion;
        private bool _traversing;
        private float _width;

        internal static GuideReader For(TextsDialog dialog)
        {
            GuideReader reader = dialog.GetComponent<GuideReader>() ?? dialog.gameObject.AddComponent<GuideReader>();
            reader._dialog = dialog;
            return reader;
        }

        internal void RegisterPages()
        {
            if (_revision != ServerGuide.Revision) _history.Prune(ServerGuide.Pages.Select(page => page.Title));
            _revision = ServerGuide.Revision;
            _pages.Clear();
            for (int index = ServerGuide.Pages.Count - 1; index >= 0; index--)
            {
                GuidePage page = ServerGuide.Pages[index];
                TextsDialog.TextInfo info = new TextsDialog.TextInfo("Server Guide: " + page.Title, page.Body);
                _pages.Add(info, page);
                _dialog.m_texts.Insert(0, info);
            }
        }

        internal void Select(TextsDialog.TextInfo info)
        {
            if (!_pages.TryGetValue(info, out GuidePage page))
            {
                _current = null;
                if (_root != null)
                {
                    _root.SetActive(false);
                    SetNativeVisible(true);
                }
                return;
            }
            EnsureUI();
            if (!_traversing) _history.Visit(page.Title, _scroll.verticalNormalizedPosition);
            _current = page;
            SetNativeVisible(false);
            _root.SetActive(true);
            Render(_history.Current?.Scroll ?? 1f);
        }

        private void Navigate(string title)
        {
            for (int index = 0; index < _dialog.m_texts.Count; index++)
                if (_pages.TryGetValue(_dialog.m_texts[index], out GuidePage page) && string.Equals(page.Title, title, StringComparison.OrdinalIgnoreCase))
                {
                    _dialog.ShowText(index);
                    return;
                }
        }

        private void Move(int direction)
        {
            GuideHistory.Entry? entry = _history.Move(direction, _scroll.verticalNormalizedPosition);
            if (entry == null) return;
            _traversing = true;
            try { Navigate(entry.Title); }
            finally { _traversing = false; }
        }

        private void Update()
        {
            if (_current == null || _root == null || !_root.activeInHierarchy) return;
            if (_revision != ServerGuide.Revision)
            {
                string title = _current.Title;
                _dialog.FillTextList();
                if (_pages.Values.Any(page => string.Equals(page.Title, title, StringComparison.OrdinalIgnoreCase))) Navigate(title);
                else if (_dialog.m_texts.Count > 0) _dialog.ShowText(0);
                return;
            }
            if (_assetVersion != GuideImages.Version || Mathf.Abs(_width - _scroll.viewport.rect.width) > 1f)
                Render(_scroll.verticalNormalizedPosition);
            if (Input.GetMouseButtonDown(3)) Move(-1);
            if (Input.GetMouseButtonDown(4)) Move(1);
        }

        private void EnsureUI()
        {
            if (_root != null) return;
            _nativeScrollbar = _dialog.m_rightScrollbar;
            _original = _dialog.m_textArea.GetComponentInParent<ScrollRect>(true);
            RectTransform source = ReadingArea();
            RectTransform root = CreateRect("PraetorisGuideReader", source.parent);
            root.anchorMin = source.anchorMin;
            root.anchorMax = source.anchorMax;
            root.pivot = source.pivot;
            root.anchoredPosition = source.anchoredPosition;
            root.sizeDelta = source.sizeDelta;
            root.localScale = source.localScale;
            _root = root.gameObject;

            Button template = _dialog.GetComponentsInChildren<Button>(true).FirstOrDefault(button =>
                Enumerable.Range(0, button.onClick.GetPersistentEventCount()).Any(index => button.onClick.GetPersistentMethodName(index) == "OnClose"))
                ?? _dialog.m_elementPrefab.GetComponent<Button>();
            RectTransform toolbar = CreateRect("Navigation", root);
            toolbar.anchorMin = new Vector2(0, 1);
            toolbar.anchorMax = Vector2.one;
            toolbar.pivot = new Vector2(0.5f, 1);
            toolbar.sizeDelta = new Vector2(0, 42);
            HorizontalLayoutGroup navigation = toolbar.gameObject.AddComponent<HorizontalLayoutGroup>();
            navigation.spacing = 12;
            navigation.childControlHeight = navigation.childControlWidth = true;
            navigation.childForceExpandHeight = navigation.childForceExpandWidth = false;
            _back = MakeButton(template, toolbar, "Back", () => Move(-1));
            _forward = MakeButton(template, toolbar, "Forward", () => Move(1));

            RectTransform body = CreateRect("GuideScroll", root);
            Stretch(body);
            body.offsetMax = new Vector2(0, -54);
            _scroll = body.gameObject.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 30;
            RectTransform viewport = CreateRect("Viewport", body);
            Stretch(viewport);
            viewport.offsetMax = new Vector2(-14, 0);
            viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.01f);
            viewport.gameObject.AddComponent<RectMask2D>();
            _scroll.viewport = viewport;
            _content = CreateRect("Content", viewport);
            _content.anchorMin = new Vector2(0, 1);
            _content.anchorMax = Vector2.one;
            _content.pivot = new Vector2(0.5f, 1);
            _content.sizeDelta = Vector2.zero;
            VerticalLayoutGroup layout = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 8, 20);
            layout.spacing = 18;
            layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            ContentSizeFitter fitter = _content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll.content = _content;
            Scrollbar bar = Instantiate(_nativeScrollbar, body);
            bar.name = "GuideScrollbar";
            bar.onValueChanged = new Scrollbar.ScrollEvent();
            RectTransform barRect = (RectTransform)bar.transform;
            barRect.anchorMin = new Vector2(1, 0);
            barRect.anchorMax = Vector2.one;
            barRect.offsetMin = new Vector2(-8, 0);
            barRect.offsetMax = Vector2.zero;
            _scroll.verticalScrollbar = bar;
            _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }

        private RectTransform ReadingArea()
        {
            if (_original != null) return (RectTransform)_original.transform;
            RectMask2D? rectMask = _dialog.m_textArea.GetComponentInParent<RectMask2D>(true);
            if (rectMask != null) return (RectTransform)rectMask.transform;
            Mask? mask = _dialog.m_textArea.GetComponentInParent<Mask>(true);
            if (mask != null) return (RectTransform)mask.transform;
            Transform parent = _dialog.m_textArea.transform.parent;
            while (parent.parent != null && !_dialog.m_textAreaTopic.transform.IsChildOf(parent)) parent = parent.parent;
            return (RectTransform)parent;
        }

        private void SetNativeVisible(bool visible)
        {
            if (_original != null) _original.gameObject.SetActive(visible);
            _dialog.m_textArea.gameObject.SetActive(visible);
            _dialog.m_textAreaTopic.gameObject.SetActive(visible);
            _nativeScrollbar.gameObject.SetActive(visible);
            // Keep Valheim's own controller scrolling code and its version-specific input API.
            _dialog.m_rightScrollbar = visible ? _nativeScrollbar : _scroll.verticalScrollbar;
        }

        private Button MakeButton(Button template, Transform parent, string label, Action action)
        {
            RectTransform rect = CreateRect(label, parent);
            Image? skin = template.targetGraphic as Image ?? template.GetComponentInChildren<Image>(true);
            Image background = rect.gameObject.AddComponent<Image>();
            background.sprite = skin?.sprite;
            background.type = skin?.type ?? Image.Type.Simple;
            background.color = skin?.color ?? new Color(0.2f, 0.15f, 0.1f);
            if (background.color.a < 0.1f) background.color = new Color(0.16f, 0.12f, 0.08f, 0.9f);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.colors = template.colors;
            button.onClick.AddListener(() => action());
            TMP_Text text = CreateText(rect, label);
            Stretch(text.rectTransform);
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(1f, 0.75f, 0.35f);
            text.raycastTarget = false;
            LayoutElement layout = button.gameObject.AddComponent<LayoutElement>();
            layout.minWidth = layout.preferredWidth = 140;
            layout.minHeight = layout.preferredHeight = 40;
            button.gameObject.SetActive(true);
            return button;
        }

        private void Render(float scrollPosition)
        {
            if (_current == null) return;
            foreach (Transform child in _content)
            {
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
            _assetVersion = GuideImages.Version;
            _width = _scroll.viewport.rect.width;
            TMP_Text heading = AddText(GuideMarkup.Escape(_current.Title));
            heading.fontSize = _dialog.m_textArea.fontSize * 1.5f;
            heading.color = new Color(1f, 0.75f, 0.35f);
            foreach (GuideBlock block in GuideMarkup.Parse(_current.Body))
            {
                if (block.Image.Length == 0)
                {
                    TMP_Text text = AddText(block.Text);
                    GuideLinkClick links = text.gameObject.AddComponent<GuideLinkClick>();
                    links.Follow = index => { if (index >= 0 && index < block.Links.Count) Navigate(block.Links[index]); };
                    continue;
                }
                Sprite? sprite = GuideImages.Get(block.Image);
                if (sprite == null)
                    AddText(GuideImages.HasFailed(block.Image) ? "Image unavailable." : "Loading image…");
                else
                {
                    RectTransform imageRect = CreateRect("GuideImage", _content);
                    Image image = imageRect.gameObject.AddComponent<Image>();
                    image.sprite = sprite;
                    image.preserveAspect = true;
                    LayoutElement size = imageRect.gameObject.AddComponent<LayoutElement>();
                    size.preferredHeight = Mathf.Min(320, Mathf.Max(1, _width - 24) * sprite.rect.height / sprite.rect.width);
                }
                if (block.Text.Length > 0)
                {
                    TMP_Text caption = AddText(GuideMarkup.Escape(block.Text));
                    caption.fontSize *= 0.85f;
                    caption.color = new Color(0.8f, 0.8f, 0.8f);
                }
            }
            _back.interactable = _history.CanBack;
            _forward.interactable = _history.CanForward;
            _back.GetComponentInChildren<TMP_Text>().alpha = _history.CanBack ? 1f : 0.35f;
            _forward.GetComponentInChildren<TMP_Text>().alpha = _history.CanForward ? 1f : 0.35f;
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            _scroll.verticalNormalizedPosition = Mathf.Clamp01(scrollPosition);
            int version = ++_renderVersion;
            if (isActiveAndEnabled) StartCoroutine(RestoreScroll(scrollPosition, version));
        }

        private void OnEnable()
        {
            if (_current != null && _root != null) Render(_history.Current?.Scroll ?? 1f);
        }

        private void OnDisable()
        {
            if (_history.Current != null && _scroll != null) _history.Current.Scroll = _scroll.verticalNormalizedPosition;
        }

        private IEnumerator RestoreScroll(float position, int version)
        {
            yield return null;
            if (version != _renderVersion) yield break;
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            _scroll.verticalNormalizedPosition = Mathf.Clamp01(position);
        }

        private TMP_Text AddText(string value)
        {
            return CreateText(_content, value);
        }

        private TMP_Text CreateText(Transform parent, string value)
        {
            RectTransform rect = CreateRect("GuideText", parent);
            TMP_Text text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            TMP_Text source = _dialog.m_textArea;
            text.font = source.font;
            text.fontSharedMaterial = source.fontSharedMaterial;
            text.fontSize = source.fontSize;
            text.fontStyle = source.fontStyle;
            text.color = source.color;
            text.lineSpacing = source.lineSpacing;
            text.paragraphSpacing = source.paragraphSpacing;
            text.text = value;
            text.richText = true;
            text.enableAutoSizing = false;
#pragma warning disable CS0618 // Shared API across the game's TextMesh Pro versions.
            text.enableWordWrapping = true;
#pragma warning restore CS0618
            text.overflowMode = TextOverflowModes.Overflow;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.raycastTarget = true;
            return text;
        }

        internal static string Status()
        {
            if (InventoryGui.instance == null || InventoryGui.instance.m_textsDialog == null) return "";
            GuideReader? reader = InventoryGui.instance.m_textsDialog.GetComponent<GuideReader>();
            if (reader == null || !reader.isActiveAndEnabled) return "";
            return $", page={reader._current?.Title ?? "none"}, back={reader._history.CanBack}, forward={reader._history.CanForward}, scroll={reader._scroll?.verticalNormalizedPosition:0.00}";
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            RectTransform rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.gameObject.layer = parent.gameObject.layer;
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private void OnDestroy()
        {
            if (_root != null) Destroy(_root);
        }
    }

    internal sealed class GuideLinkClick : MonoBehaviour, IPointerClickHandler
    {
        internal Action<int>? Follow;
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            TMP_Text text = GetComponent<TMP_Text>();
            int link = TMP_TextUtilities.FindIntersectingLink(text, eventData.position, eventData.pressEventCamera);
            if (link >= 0 && int.TryParse(text.textInfo.linkInfo[link].GetLinkID(), out int index)) Follow?.Invoke(index);
        }
    }
}
