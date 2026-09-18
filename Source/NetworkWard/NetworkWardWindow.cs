using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace PraetorisClient.NetworkWardFeature
{
    internal sealed class NetworkWardWindow : MonoBehaviour
    {
        private sealed class DisplayRow
        {
            internal string Label = "";
            internal string Detail = "";
            internal string Key = "";
            internal int Count;
            internal long Sent;
            internal long Received;
            internal TrafficObject? Object;
        }

        private sealed class RowView
        {
            internal RectTransform Root = null!;
            internal Image Background = null!;
            internal Text Label = null!;
            internal Text Detail = null!;
            internal Text Count = null!;
            internal Text Sent = null!;
            internal Text Received = null!;
            internal Text Share = null!;
            internal DisplayRow? Data;
        }

        private const float Width = 1060;
        private const float Height = 720;
        private const float RowHeight = 54;
        private static readonly Color Gold = new Color(1f, 0.77f, 0.38f);
        private static readonly Color Muted = new Color(0.77f, 0.72f, 0.63f);
        private static NetworkWardWindow? _instance;
        private static int _closedFrame = -1;
        private NetworkWard? _ward;
        private bool _open;
        private bool _blocked;
        private float _nextRefresh;
        private float _radius = 40;
        private bool _groups = true;
        private readonly HashSet<string> _expanded = new HashSet<string>();
        private readonly List<DisplayRow> _display = new List<DisplayRow>();
        private readonly List<RowView> _rowViews = new List<RowView>();
        private readonly List<Button> _radiusButtons = new List<Button>();
        private TrafficObject? _selected;
        private float _highlightUntil;
        private GameObject _panel = null!;
        private ScrollRect _scroll = null!;
        private RectTransform _content = null!;
        private Text _sent = null!;
        private Text _received = null!;
        private Text _count = null!;
        private Text _sample = null!;
        private Text _selection = null!;
        private Text _status = null!;
        private Text _empty = null!;
        private Button _viewButton = null!;
        private Button _showButton = null!;
        private GUIStyle? _markerStyle;
        internal static bool IsOpen => _instance != null && _instance._open;
        internal static bool BlocksMenu => IsOpen || _closedFrame == Time.frameCount;

        internal static void Open(NetworkWard ward)
        {
            if (!NetworkWardAccess.HasAccess) return;
            if (_instance == null) _instance = Player.m_localPlayer.gameObject.AddComponent<NetworkWardWindow>();
            _instance.Close();
            if (_instance._panel == null) _instance.CreatePanel();
            _instance._ward = ward;
            _instance._open = true;
            _instance._panel!.SetActive(true);
            _instance._expanded.Clear();
            _instance._selected = null;
            _instance._content.anchoredPosition = Vector2.zero;
            GUIManager.BlockInput(true);
            _instance._blocked = true;
            NetworkTraffic.Start(ward.transform.position, _instance._radius);
            _instance.Rebuild();
        }

        private void Close()
        {
            if (_open) _closedFrame = Time.frameCount;
            _open = false;
            if (_panel != null) _panel.SetActive(false);
            NetworkTraffic.Stop();
            if (_blocked) { GUIManager.BlockInput(false); _blocked = false; }
        }

        private void OnDestroy()
        {
            Close();
            if (_panel != null) Destroy(_panel);
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            if (!_open) return;
            if (!NetworkWardAccess.HasAccess || _ward == null || Player.m_localPlayer == null || Player.m_localPlayer.IsDead() ||
                Vector3.Distance(_ward.transform.position, Player.m_localPlayer.transform.position) > _radius + 10 ||
                ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyButtonB"))
            {
                Close();
                ZInput.ResetButtonStatus("JoyButtonB");
                return;
            }
            Rect parent = ((RectTransform)_panel.transform.parent).rect;
            float scale = Mathf.Min(1f, Mathf.Min(parent.width / (Width + 40), parent.height / (Height + 40)));
            _panel.transform.localScale = Vector3.one * scale;
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 0.5f;
                NetworkTraffic.Refresh();
                Rebuild();
            }
        }

        private static RectTransform Place(GameObject target, float x, float y, float width, float height)
        {
            RectTransform rect = (RectTransform)target.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        private static Text Label(Transform parent, string value, float x, float y, float width, float height,
            int size = 18, TextAnchor alignment = TextAnchor.MiddleLeft, bool title = false)
        {
            GUIManager gui = GUIManager.Instance;
            GameObject obj = gui.CreateText(value, parent, Vector2.zero, Vector2.zero, Vector2.zero,
                title ? gui.NorseBold : gui.AveriaSerif, size, title ? Gold : Color.white, true, Color.black, width, height, false);
            Place(obj, x, y, width, height);
            Text text = obj.GetComponent<Text>();
            text.alignment = alignment;
            text.raycastTarget = false;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            return text;
        }

        private static Button MakeButton(Transform parent, string label, float x, float y, float width, Action click)
        {
            GameObject obj = GUIManager.Instance.CreateButton(label, parent, Vector2.zero, Vector2.zero, Vector2.zero, width, 36);
            Place(obj, x, y, width, 36);
            Button button = obj.GetComponent<Button>();
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => click());
            return button;
        }

        private static Image Background(Transform parent, string name, float x, float y, float width, float height, Color color)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(parent, false);
            Place(obj, x, y, width, height);
            Image image = obj.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private void CreatePanel()
        {
            GUIManager gui = GUIManager.Instance;
            _panel = gui.CreateWoodpanel(GUIManager.CustomGUIFront.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, Width, Height, false);
            _panel.name = "NetworkWardPanel";
            Transform root = _panel.transform;
            Label(root, "Network Ward", 30, 15, 1000, 62, 42, TextAnchor.MiddleCenter, true);
            _sent = Stat(root, "Sent / second", 35);
            _received = Stat(root, "Received / second", 285);
            _count = Stat(root, "Loaded objects", 535);
            _sample = Stat(root, "Sample window", 785);
            Label(root, "Radius", 35, 159, 65, 36).color = Gold;
            foreach (float radius in new[] { 20f, 40f, 80f, 160f })
            {
                float selectedRadius = radius;
                _radiusButtons.Add(MakeButton(root, radius + " m", 100 + _radiusButtons.Count * 79, 159, 74, () =>
                {
                    _radius = selectedRadius;
                    ResetSample();
                }));
            }
            _viewButton = MakeButton(root, "Object types", 448, 159, 200, () =>
            {
                _groups = !_groups;
                _content.anchoredPosition = Vector2.zero;
                Rebuild();
            });
            Label(root, "Most traffic first", 676, 159, 215, 36, 16).color = Muted;
            MakeButton(root, "Reset", 922, 159, 100, ResetSample);

            Image inset = Background(root, "ObjectListPanel", 27, 213, 1006, 386, Color.white);
            inset.sprite = gui.GetSprite("woodpanel_settings");
            inset.type = Image.Type.Sliced;
            Label(root, "Object", 45, 216, 380, 34).color = Gold;
            Label(root, "Loaded", 427, 216, 80, 34, 18, TextAnchor.MiddleRight).color = Gold;
            Label(root, "Sent / s", 529, 216, 170, 34, 18, TextAnchor.MiddleRight).color = Gold;
            Label(root, "Received / s", 709, 216, 170, 34, 18, TextAnchor.MiddleRight).color = Gold;
            Label(root, "Share", 897, 216, 94, 34, 18, TextAnchor.MiddleRight).color = Gold;

            GameObject scrollObject = DefaultControls.CreateScrollView(new DefaultControls.Resources());
            scrollObject.transform.SetParent(root, false);
            Place(scrollObject, 35, 255, 988, 334);
            _scroll = scrollObject.GetComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.inertia = false;
            Destroy(_scroll.horizontalScrollbar.gameObject);
            _scroll.horizontalScrollbar = null;
            _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            gui.ApplyScrollRectStyle(_scroll);
            _scroll.GetComponent<Image>().color = Color.clear;
            _scroll.viewport.offsetMin = Vector2.zero;
            _scroll.viewport.offsetMax = new Vector2(-18, 0);
            _content = _scroll.content;
            Place(_content.gameObject, 0, 0, 970, 334);
            _scroll.onValueChanged.AddListener(_ => PaintRows());
            for (int i = 0; i < 9; i++) CreateRow();
            _empty = Label(root, "No loaded networked objects within this radius.", 45, 267, 940, 40);
            _selection = Label(root, "", 35, 605, 780, 32, 16);
            _showButton = MakeButton(root, "Show in world", 842, 605, 180, ShowInWorld);
            _status = Label(root, "", 35, 640, 990, 25, 14);
            _status.color = Muted;
            MakeButton(root, "Close", 450, 674, 160, Close);
        }

        private static Text Stat(Transform parent, string name, float x)
        {
            Label(parent, name, x, 85, 235, 26, 18).color = Muted;
            return Label(parent, "", x, 111, 235, 36, 27);
        }

        private void ResetSample()
        {
            NetworkTraffic.Start(_ward!.transform.position, _radius);
            _content.anchoredPosition = Vector2.zero;
            _selected = null;
            Rebuild();
        }

        private void CreateRow()
        {
            RowView row = new RowView();
            row.Background = Background(_content, "ObjectRow", 0, 0, 970, RowHeight - 2, Color.clear);
            row.Root = (RectTransform)row.Background.transform;
            Button button = row.Root.gameObject.AddComponent<Button>();
            button.targetGraphic = row.Background;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.colors = GUIManager.Instance.ValheimButtonColorBlock;
            button.onClick.AddListener(() =>
            {
                if (row.Data == null) return;
                if (row.Data.Object != null) _selected = row.Data.Object;
                else if (!_expanded.Add(row.Data.Key)) _expanded.Remove(row.Data.Key);
                Rebuild();
            });
            row.Label = Label(row.Root, "", 10, 0, 375, 30, 20);
            row.Label.horizontalOverflow = HorizontalWrapMode.Wrap;
            row.Detail = Label(row.Root, "", 10, 29, 375, 21, 14);
            row.Detail.color = Muted;
            row.Count = Label(row.Root, "", 392, 7, 80, 36, 20, TextAnchor.MiddleRight);
            row.Sent = Label(row.Root, "", 494, 7, 170, 36, 20, TextAnchor.MiddleRight);
            row.Received = Label(row.Root, "", 674, 7, 170, 36, 20, TextAnchor.MiddleRight);
            row.Share = Label(row.Root, "", 862, 7, 94, 36, 20, TextAnchor.MiddleRight);
            _rowViews.Add(row);
        }

        private void Rebuild()
        {
            _display.Clear();
            if (!_groups) foreach (TrafficObject row in NetworkTraffic.Rows) AddObject(row, false);
            else foreach (IGrouping<string, TrafficObject> group in NetworkTraffic.Rows.GroupBy(row => row.Prefab)
                         .OrderByDescending(group => group.Sum(row => row.Sent + row.Received)).ThenBy(group => group.Key))
            {
                _display.Add(new DisplayRow { Label = (_expanded.Contains(group.Key) ? "−  " : "+  ") + group.First().Name,
                    Detail = group.Key, Key = group.Key, Count = group.Count(), Sent = group.Sum(row => row.Sent), Received = group.Sum(row => row.Received) });
                if (_expanded.Contains(group.Key)) foreach (TrafficObject row in group) AddObject(row, true);
            }
            _content.sizeDelta = new Vector2(970, Mathf.Max(334, _display.Count * RowHeight));
            _content.anchoredPosition = new Vector2(0, Mathf.Clamp(_content.anchoredPosition.y, 0, _content.sizeDelta.y - 334));
            _sent.text = Rate(NetworkTraffic.Rows.Sum(row => row.Sent));
            _received.text = Rate(NetworkTraffic.Rows.Sum(row => row.Received));
            _count.text = NetworkTraffic.Rows.Count.ToString();
            _sample.text = $"{NetworkTraffic.Seconds:0} / 10 seconds";
            foreach (Button button in _radiusButtons)
                button.interactable = button.GetComponentInChildren<Text>().text != _radius + " m";
            _viewButton.GetComponentInChildren<Text>().text = _groups ? "Object types" : "Individual objects";
            _selection.text = _selected == null ? "Select an object to locate it in the world." :
                $"{_selected.Name} #{_selected.Id.ID}  ·  {_selected.Distance:0} m  ·  Owner: {NetworkTraffic.Owner(_selected)}";
            _showButton.gameObject.SetActive(_selected != null);
            _empty.gameObject.SetActive(_display.Count == 0);
            _status.text = NetworkTraffic.ParseErrors == 0
                ? "This client · State + object RPCs · Application bytes; excludes transport overhead"
                : $"Attribution incomplete: {NetworkTraffic.ParseErrors} packet parse errors. Check the log.";
            PaintRows();
        }

        private void AddObject(TrafficObject row, bool nested) => _display.Add(new DisplayRow
        {
            Label = (nested ? "    " : "") + row.Name + " #" + row.Id.ID,
            Detail = $"{row.Distance:0} m  ·  {NetworkTraffic.Owner(row)}", Count = 1,
            Sent = row.Sent, Received = row.Received, Object = row
        });

        private void PaintRows()
        {
            if (_content == null) return;
            int first = Mathf.Max(0, (int)(_content.anchoredPosition.y / RowHeight));
            long total = NetworkTraffic.Rows.Sum(row => row.Sent + row.Received);
            for (int i = 0; i < _rowViews.Count; i++)
            {
                RowView view = _rowViews[i];
                int index = first + i;
                view.Root.gameObject.SetActive(index < _display.Count);
                if (index >= _display.Count) continue;
                DisplayRow row = _display[index];
                view.Data = row;
                view.Root.anchoredPosition = new Vector2(0, -index * RowHeight);
                view.Background.color = row.Object != null && row.Object == _selected
                    ? new Color(0.55f, 0.37f, 0.12f, 0.5f) : new Color(0, 0, 0, index % 2 == 0 ? 0.32f : 0.14f);
                view.Label.text = row.Label;
                view.Detail.text = row.Detail;
                view.Count.text = row.Count.ToString();
                view.Sent.text = Rate(row.Sent);
                view.Received.text = Rate(row.Received);
                view.Share.text = total > 0 ? $"{100f * (row.Sent + row.Received) / total:0.0}%" : "0.0%";
            }
        }

        private void ShowInWorld()
        {
            if (_selected?.View != null && Player.m_localPlayer != null)
                Player.m_localPlayer.SetLookDir((_selected.View.transform.position + Vector3.up - Player.m_localPlayer.GetEyePoint()).normalized);
            _highlightUntil = Time.unscaledTime + 8;
            Close();
        }

        private static string Rate(long bytes)
        {
            float rate = bytes / NetworkTraffic.Seconds;
            return rate >= 1024 ? $"{rate / 1024:0.00} KiB" : $"{rate:0} B";
        }

        private void OnGUI()
        {
            if (!NetworkWardAccess.HasAccess || _open || _selected?.View == null || Time.unscaledTime > _highlightUntil || Camera.main == null) return;
            if (_markerStyle == null) _markerStyle = new GUIStyle(GUI.skin.label)
                { font = GUIManager.Instance.AveriaSerif, fontSize = 18, richText = false };
            Vector3 position = Camera.main.WorldToScreenPoint(_selected.View.transform.position + Vector3.up);
            if (position.z <= 0) return;
            Rect marker = new Rect(position.x - 12, Screen.height - position.y - 12, 24, 24);
            Color saved = GUI.color;
            GUI.color = Color.cyan;
            GUI.DrawTexture(new Rect(marker.x, marker.y, 24, 3), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(marker.x, marker.y + 21, 24, 3), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(marker.x, marker.y, 3, 24), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(marker.x + 21, marker.y, 3, 24), Texture2D.whiteTexture);
            GUI.color = saved;
            GUI.Label(new Rect(marker.x - 100, marker.y - 35, 360, 30), _selected.Name + " #" + _selected.Id.ID, _markerStyle);
        }

        internal static string Status()
        {
            if (!IsOpen || !NetworkWardAccess.HasAccess) return "Network Ward closed; sampling inactive.";
            NetworkTraffic.Refresh();
            StringBuilder text = new StringBuilder($"Network Ward: client scope, radius={NetworkTraffic.Radius}, seconds={NetworkTraffic.Seconds}, objects={NetworkTraffic.Rows.Count}, parseErrors={NetworkTraffic.ParseErrors}\n");
            foreach (TrafficObject row in NetworkTraffic.Rows.Take(30))
                text.AppendLine($"{row.Prefab} #{row.Id.ID}: sent={row.Sent}, received={row.Received}, state={row.StateBytes}, rpc={row.RpcBytes}, owner={NetworkTraffic.Owner(row)}");
            return text.ToString();
        }
    }

    [HarmonyPatch(typeof(Menu), "Update")]
    internal static class NetworkWardMenuPatch
    {
        private static bool Prefix() => !NetworkWardWindow.BlocksMenu;
    }
}
