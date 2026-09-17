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
        // Copy layout references before removing the native component. Its Awake patches
        // must not add other mods' compendium pages or controls to this window.
        private sealed class GuideLayout
        {
            internal RectTransform m_listRoot = null!;
            internal ScrollRect m_leftScrollRect = null!;
            internal Scrollbar m_leftScrollbar = null!;
            internal Scrollbar m_rightScrollbar = null!;
            internal GameObject m_elementPrefab = null!;
            internal TMP_Text m_textArea = null!;
            internal TMP_Text m_textAreaTopic = null!;
            internal ScrollRectEnsureVisible m_recipeEnsureVisible = null!;
            internal float m_spacing;
        }
        private GuideLayout _dialog = null!;
        private readonly Dictionary<GuidePage, GameObject> _pages = new Dictionary<GuidePage, GameObject>();
        private readonly Dictionary<GuidePage, string> _searchText = new Dictionary<GuidePage, string>();
        private TMP_InputField _search = null!;
        private TMP_Text _results = null!;
        private int _matchCount;
        internal bool SearchFocused => _search != null && _search.isFocused;
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
        private int _renderVersion;
        private bool _traversing;
        private float _width;

        internal void Initialize(TextsDialog dialog)
        {
            _dialog = new GuideLayout
            {
                m_listRoot = dialog.m_listRoot,
                m_leftScrollRect = dialog.m_listRoot.GetComponentInParent<ScrollRect>(true),
                m_leftScrollbar = dialog.m_leftScrollbar,
                m_rightScrollbar = dialog.m_rightScrollbar,
                m_elementPrefab = dialog.m_elementPrefab,
                m_textArea = dialog.m_textArea,
                m_textAreaTopic = dialog.m_textAreaTopic,
                m_recipeEnsureVisible = dialog.m_recipeEnsureVisible,
                m_spacing = dialog.m_spacing
            };
            EnsureUI();
            HideNativeReader();
        }

        internal void Open()
        {
            gameObject.SetActive(true);
            RefreshPages();
        }

        internal void Close() => gameObject.SetActive(false);

        internal void Escape()
        {
            if (SearchFocused) _search.DeactivateInputField();
            else Close();
        }

        private void RefreshPages()
        {
            string title = _current?.Title ?? "";
            if (_revision != ServerGuide.Revision) _history.Prune(ServerGuide.Pages.Select(page => page.Title));
            _revision = ServerGuide.Revision;
            foreach (GameObject entry in _pages.Values) { entry.SetActive(false); Destroy(entry); }
            _pages.Clear();
            _searchText.Clear();
            for (int index = 0; index < ServerGuide.Pages.Count; index++)
            {
                GuidePage page = ServerGuide.Pages[index];
                GameObject entry = Instantiate(_dialog.m_elementPrefab, _dialog.m_listRoot);
                ((RectTransform)entry.transform).anchoredPosition = new Vector2(0, -index * _dialog.m_spacing);
                Utils.FindChild(entry.transform, "name").GetComponent<TMP_Text>().text = page.Title;
                Button button = entry.GetComponent<Button>();
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(() => Navigate(page.Title));
                entry.SetActive(true);
                _pages.Add(page, entry);
                _searchText.Add(page, GuideSearch.Text(page));
            }
            ApplyFilter();
            GuidePage? selected = ServerGuide.Pages.FirstOrDefault(page => string.Equals(page.Title, title, StringComparison.OrdinalIgnoreCase))
                ?? ServerGuide.Pages.FirstOrDefault();
            if (selected != null) Select(selected);
            else
            {
                _current = null;
                foreach (Transform child in _content) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
                AddText("Server Guide").fontSize *= 1.5f;
                AddText("This server has not published any guide pages.");
                UpdateButtons();
            }
        }

        private void Select(GuidePage page)
        {
            if (!_traversing) _history.Visit(page.Title, _scroll.verticalNormalizedPosition);
            _current = page;
            foreach (KeyValuePair<GuidePage, GameObject> entry in _pages)
                Utils.FindChild(entry.Value.transform, "selected").gameObject.SetActive(entry.Key == page);
            if (_pages[page].activeSelf) _dialog.m_recipeEnsureVisible.CenterOnItem((RectTransform)_pages[page].transform);
            Render(_history.Current?.Scroll ?? 1f);
        }

        private void Navigate(string title)
        {
            GuidePage? page = _pages.Keys.FirstOrDefault(candidate => string.Equals(candidate.Title, title, StringComparison.OrdinalIgnoreCase));
            if (page == null) return;
            // Links and history can lead outside the current search results.
            if (!_pages[page].activeSelf) _search.text = "";
            Select(page);
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
            if (_root == null || !_root.activeInHierarchy) return;
            if (_revision != ServerGuide.Revision)
            {
                RefreshPages();
                return;
            }
            if (_current == null) return;
            if (Mathf.Abs(_width - _scroll.viewport.rect.width) > 1f)
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

            Button template = GetComponentsInChildren<Button>(true).FirstOrDefault(button =>
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
            CreateSearch(template);

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

        private void CreateSearch(Button template)
        {
            RectTransform list = _dialog.m_leftScrollRect.viewport != null
                ? _dialog.m_leftScrollRect.viewport : (RectTransform)_dialog.m_listRoot.parent;
            RectTransform frame = CreateRect("GuidePageBrowser", list.parent);
            frame.anchorMin = list.anchorMin;
            frame.anchorMax = list.anchorMax;
            frame.pivot = list.pivot;
            frame.anchoredPosition = list.anchoredPosition;
            frame.sizeDelta = list.sizeDelta;
            frame.localScale = list.localScale;
            list.SetParent(frame, false);
            list.localScale = Vector3.one;
            Stretch(list);
            list.offsetMax = new Vector2(0, -72);

            RectTransform field = CreateRect("SearchPages", frame);
            field.anchorMin = new Vector2(0, 1);
            field.anchorMax = Vector2.one;
            field.pivot = new Vector2(0.5f, 1);
            field.offsetMin = new Vector2(0, -42);
            field.offsetMax = new Vector2(-74, 0);
            Image background = field.gameObject.AddComponent<Image>();
            background.color = new Color(0.12f, 0.09f, 0.06f, 0.9f);
            RectTransform area = CreateRect("TextArea", field);
            Stretch(area);
            area.offsetMin = new Vector2(10, 4);
            area.offsetMax = new Vector2(-10, -4);
            area.gameObject.AddComponent<RectMask2D>();
            TMP_Text value = CreateText(area, "");
            Stretch(value.rectTransform);
            value.fontSize = 20;
            value.alignment = TextAlignmentOptions.MidlineLeft;
            value.richText = false;
            TMP_Text placeholder = CreateText(area, "Search pages…");
            Stretch(placeholder.rectTransform);
            placeholder.fontSize = 20;
            placeholder.alignment = TextAlignmentOptions.MidlineLeft;
            placeholder.alpha = 0.55f;
            _search = field.gameObject.AddComponent<TMP_InputField>();
            _search.targetGraphic = background;
            _search.textViewport = area;
            _search.textComponent = value;
            _search.placeholder = placeholder;
            _search.lineType = TMP_InputField.LineType.SingleLine;
            _search.characterLimit = 80;
            _search.customCaretColor = true;
            _search.caretColor = new Color(1f, 0.75f, 0.35f);
            _search.onValueChanged.AddListener(_ => ApplyFilter());

            Button clear = MakeButton(template, frame, "Clear", () => _search.text = "");
            RectTransform clearRect = (RectTransform)clear.transform;
            clearRect.anchorMin = clearRect.anchorMax = Vector2.one;
            clearRect.pivot = Vector2.one;
            clearRect.anchoredPosition = Vector2.zero;
            clearRect.sizeDelta = new Vector2(68, 42);
            clear.GetComponentInChildren<TMP_Text>().fontSize = 18;
            _results = CreateText(frame, "");
            _results.fontSize = 17;
            _results.alpha = 0.75f;
            RectTransform label = _results.rectTransform;
            label.anchorMin = new Vector2(0, 1);
            label.anchorMax = Vector2.one;
            label.pivot = new Vector2(0.5f, 1);
            label.anchoredPosition = new Vector2(0, -46);
            label.sizeDelta = new Vector2(0, 24);
        }

        private void ApplyFilter()
        {
            int index = 0;
            foreach (KeyValuePair<GuidePage, GameObject> entry in _pages)
            {
                bool matches = GuideSearch.Matches(_searchText[entry.Key], _search.text);
                entry.Value.SetActive(matches);
                if (matches) ((RectTransform)entry.Value.transform).anchoredPosition = new Vector2(0, -index++ * _dialog.m_spacing);
            }
            _matchCount = index;
            _results.text = index == 0 ? "No matching pages" : $"{index} of {_pages.Count} pages";
            float height = _dialog.m_leftScrollRect.viewport != null
                ? _dialog.m_leftScrollRect.viewport.rect.height : ((RectTransform)_dialog.m_leftScrollRect.transform).rect.height;
            _dialog.m_listRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(height, index * _dialog.m_spacing));
            _dialog.m_leftScrollbar.size = height / Mathf.Max(1, _dialog.m_listRoot.rect.height);
            _dialog.m_leftScrollRect.verticalNormalizedPosition = 1f;
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

        private void HideNativeReader()
        {
            if (_original != null) _original.gameObject.SetActive(false);
            _dialog.m_textArea.gameObject.SetActive(false);
            _dialog.m_textAreaTopic.gameObject.SetActive(false);
            _nativeScrollbar.gameObject.SetActive(false);
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
                    AddText("Image unavailable in this mod build.");
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
            UpdateButtons();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
            _scroll.verticalNormalizedPosition = Mathf.Clamp01(scrollPosition);
            int version = ++_renderVersion;
            if (isActiveAndEnabled) StartCoroutine(RestoreScroll(scrollPosition, version));
        }

        private void UpdateButtons()
        {
            _back.interactable = _history.CanBack;
            _forward.interactable = _history.CanForward;
            _back.GetComponentInChildren<TMP_Text>().alpha = _history.CanBack ? 1f : 0.35f;
            _forward.GetComponentInChildren<TMP_Text>().alpha = _history.CanForward ? 1f : 0.35f;
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
            rect.gameObject.SetActive(false);
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
            rect.gameObject.SetActive(true);
            return text;
        }

        internal static string Status()
        {
            GuideReader? reader = GuideWindow.Reader;
            if (reader == null || !reader.isActiveAndEnabled) return "";
            return $", page={reader._current?.Title ?? "none"}, matches={reader._matchCount}/{reader._pages.Count}, searchFocused={reader.SearchFocused}, back={reader._history.CanBack}, forward={reader._history.CanForward}, scroll={reader._scroll?.verticalNormalizedPosition:0.00}";
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
