using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;

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
        private static NetworkWardWindow? _instance;
        private static int _closedFrame = -1;
        private NetworkWard? _ward;
        private bool _open;
        private bool _blocked;
        private float _nextRefresh;
        private float _radius = 40;
        private bool _groups = true;
        private Vector2 _scroll;
        private readonly HashSet<string> _expanded = new HashSet<string>();
        private readonly List<DisplayRow> _display = new List<DisplayRow>();
        private TrafficObject? _selected;
        private float _highlightUntil;
        private GUIStyle _title = null!;
        private GUIStyle _text = null!;
        private GUIStyle _muted = null!;
        private GUIStyle _number = null!;
        private GUIStyle _button = null!;
        private GUIStyle _small = null!;
        internal static bool IsOpen => _instance != null && _instance._open;
        internal static bool BlocksMenu => IsOpen || _closedFrame == Time.frameCount;

        internal static void Open(NetworkWard ward)
        {
            if (_instance == null) _instance = Player.m_localPlayer.gameObject.AddComponent<NetworkWardWindow>();
            _instance.Close();
            _instance._ward = ward;
            _instance._open = true;
            _instance._scroll = Vector2.zero;
            _instance._expanded.Clear();
            _instance._selected = null;
            GUIManager.BlockInput(true);
            _instance._blocked = true;
            NetworkTraffic.Start(ward.transform.position, _instance._radius);
            _instance.Rebuild();
        }

        private void Close()
        {
            if (_open) _closedFrame = Time.frameCount;
            _open = false;
            NetworkTraffic.Stop();
            if (_blocked) { GUIManager.BlockInput(false); _blocked = false; }
        }

        private void OnDestroy() { Close(); if (_instance == this) _instance = null; }

        private void Update()
        {
            if (!_open) return;
            if (_ward == null || Player.m_localPlayer == null || Player.m_localPlayer.IsDead() ||
                Vector3.Distance(_ward.transform.position, Player.m_localPlayer.transform.position) > _radius + 10 ||
                ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyButtonB"))
            {
                Close();
                ZInput.ResetButtonStatus("JoyButtonB");
                return;
            }
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 0.5f;
                NetworkTraffic.Refresh(); Rebuild();
            }
        }

        private void Rebuild()
        {
            _display.Clear();
            if (!_groups)
            {
                foreach (TrafficObject row in NetworkTraffic.Rows) AddObject(row, false);
                return;
            }
            foreach (IGrouping<string, TrafficObject> group in NetworkTraffic.Rows.GroupBy(row => row.Prefab)
                         .OrderByDescending(group => group.Sum(row => row.Sent + row.Received)).ThenBy(group => group.Key))
            {
                _display.Add(new DisplayRow { Label = (_expanded.Contains(group.Key) ? "−  " : "+  ") + group.First().Name,
                    Detail = group.Key, Key = group.Key, Count = group.Count(), Sent = group.Sum(row => row.Sent), Received = group.Sum(row => row.Received) });
                if (_expanded.Contains(group.Key)) foreach (TrafficObject row in group) AddObject(row, true);
            }
        }

        private void AddObject(TrafficObject row, bool nested) => _display.Add(new DisplayRow
        {
            Label = (nested ? "     " : "") + row.Name + " #" + row.Id.ID,
            Detail = $"{row.Distance:0} m  ·  {NetworkTraffic.Owner(row)}", Count = 1,
            Sent = row.Sent, Received = row.Received, Object = row
        });

        private void Styles()
        {
            if (_text != null) return;
            _text = new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.MiddleLeft, richText = false };
            _text.normal.textColor = new Color(0.91f, 0.93f, 0.95f);
            _title = new GUIStyle(_text) { fontSize = 30, fontStyle = FontStyle.Bold };
            _title.normal.textColor = new Color(1f, 0.79f, 0.39f);
            _muted = new GUIStyle(_text) { fontSize = 14 };
            _muted.normal.textColor = new Color(0.6f, 0.7f, 0.76f);
            _small = new GUIStyle(_muted) { fontSize = 12 };
            _number = new GUIStyle(_text) { alignment = TextAnchor.MiddleRight };
            _button = new GUIStyle(GUI.skin.button) { fontSize = 15, richText = false };
        }

        private static void Fill(Rect rect, Color color)
        {
            Color old = GUI.color; GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = old;
        }

        private void OnGUI()
        {
            Styles();
            if (!_open) { DrawHighlight(); return; }
            Matrix4x4 saved = GUI.matrix;
            float scale = Mathf.Min(Screen.width / 1160f, Screen.height / 840f);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1080 * scale) / 2, (Screen.height - 760 * scale) / 2), Quaternion.identity, Vector3.one * scale);
            Fill(new Rect(0, 0, 1080, 760), new Color(0.035f, 0.055f, 0.07f, 0.98f));
            Fill(new Rect(0, 0, 1080, 4), new Color(0.25f, 0.8f, 0.9f));
            GUI.Label(new Rect(28, 18, 720, 42), "NETWORK WARD", _title);
            GUI.Label(new Rect(30, 62, 880, 28), "This client's traffic  ·  Loaded networked objects  ·  ZDO state + object RPCs", _muted);
            if (GUI.Button(new Rect(965, 26, 85, 34), "Close", _button)) { Close(); GUI.matrix = saved; return; }
            long sent = NetworkTraffic.Rows.Sum(row => row.Sent);
            long received = NetworkTraffic.Rows.Sum(row => row.Received);
            Stat(30, "SENT / SECOND", Rate(sent)); Stat(285, "RECEIVED / SECOND", Rate(received));
            Stat(540, "LOADED OBJECTS", NetworkTraffic.Rows.Count.ToString());
            Stat(795, "SAMPLE WINDOW", $"{NetworkTraffic.Seconds:0} / 10 seconds");
            GUI.Label(new Rect(30, 188, 64, 30), "Radius", _muted);
            float[] radii = { 20, 40, 80, 160 };
            for (int i = 0; i < radii.Length; i++)
            {
                float radius = radii[i];
                if (GUI.Button(new Rect(98 + i * 78, 188, 72, 30), (_radius == radius ? "• " : "") + radius + " m", _button))
                { _radius = radius; NetworkTraffic.Start(_ward!.transform.position, radius); _scroll = Vector2.zero; Rebuild(); }
            }
            if (GUI.Button(new Rect(456, 188, 190, 30), _groups ? "View: Object types" : "View: Individual objects", _button))
            { _groups = !_groups; _scroll = Vector2.zero; Rebuild(); }
            GUI.Label(new Rect(674, 188, 260, 30), "Sorted by combined traffic ↓", _muted);
            if (GUI.Button(new Rect(960, 188, 90, 30), "Reset", _button))
            { NetworkTraffic.Start(_ward!.transform.position, _radius); Rebuild(); }
            Fill(new Rect(24, 234, 1032, 38), new Color(0.1f, 0.16f, 0.2f));
            GUI.Label(new Rect(38, 238, 385, 28), "OBJECT", _muted);
            GUI.Label(new Rect(430, 238, 90, 28), "LOADED", _muted);
            GUI.Label(new Rect(590, 238, 130, 28), "SENT / S", _muted);
            GUI.Label(new Rect(752, 238, 130, 28), "RECEIVED / S", _muted);
            GUI.Label(new Rect(946, 238, 95, 28), "SHARE", _muted);
            _scroll = GUI.BeginScrollView(new Rect(24, 274, 1032, 362), _scroll, new Rect(0, 0, 1008, Mathf.Max(360, _display.Count * 54)));
            // Draw visible rows only; large bases must not create thousands of GUI controls per frame.
            int first = Mathf.Max(0, (int)(_scroll.y / 54));
            int last = Mathf.Min(_display.Count, first + 9);
            for (int i = first; i < last; i++)
            {
                DisplayRow row = _display[i]; float y = i * 54;
                Fill(new Rect(0, y, 1008, 52), row.Object != null && row.Object == _selected ? new Color(0.12f, 0.3f, 0.35f) : i % 2 == 0 ? new Color(0.07f, 0.105f, 0.13f) : new Color(0.05f, 0.08f, 0.1f));
                if (GUI.Button(new Rect(8, y, 395, 52), GUIContent.none, GUIStyle.none))
                {
                    if (row.Object != null) _selected = row.Object;
                    else { if (!_expanded.Add(row.Key)) _expanded.Remove(row.Key); Rebuild(); break; }
                }
                GUI.Label(new Rect(14, y + 2, 382, 28), row.Label, _text);
                GUI.Label(new Rect(14, y + 28, 382, 21), row.Detail, _small);
                GUI.Label(new Rect(405, y + 10, 75, 30), row.Count.ToString(), _number);
                GUI.Label(new Rect(510, y + 10, 174, 30), Rate(row.Sent), _number);
                GUI.Label(new Rect(690, y + 10, 174, 30), Rate(row.Received), _number);
                float share = sent + received > 0 ? 100f * (row.Sent + row.Received) / (sent + received) : 0;
                GUI.Label(new Rect(894, y + 10, 95, 30), $"{share:0.0}%", _number);
            }
            GUI.EndScrollView();
            if (NetworkTraffic.Rows.Count == 0) GUI.Label(new Rect(46, 292, 960, 36), "No loaded networked objects within this radius.", _muted);
            if (_selected != null)
            {
                GUI.Label(new Rect(30, 649, 800, 28), $"Selected: {_selected.Name} #{_selected.Id.ID}  ·  {_selected.Distance:0} m  ·  Owner: {NetworkTraffic.Owner(_selected)}", _text);
                if (GUI.Button(new Rect(885, 648, 165, 32), "Show in world", _button))
                {
                    if (_selected.View != null && Player.m_localPlayer != null)
                        Player.m_localPlayer.SetLookDir((_selected.View.transform.position + Vector3.up - Player.m_localPlayer.GetEyePoint()).normalized);
                    _highlightUntil = Time.unscaledTime + 8;
                    Close();
                }
            }
            else GUI.Label(new Rect(30, 649, 1000, 28), "Expand an object type, then select an instance to locate it in the world.", _muted);
            GUI.Label(new Rect(30, 693, 1010, 23), "Application bytes only. Excludes shared headers, untargeted messages and transport overhead. Quiet objects show zero.", _small);
            GUI.Label(new Rect(30, 717, 1010, 23), NetworkTraffic.ParseErrors == 0 ? "Sampling runs while this window is open. Rates use up to ten completed seconds." : $"Attribution incomplete: {NetworkTraffic.ParseErrors} packet parse errors. Check the log.", _small);
            GUI.matrix = saved;
        }

        private void Stat(float x, string title, string value)
        {
            Fill(new Rect(x, 108, 240, 62), new Color(0.075f, 0.12f, 0.15f));
            GUI.Label(new Rect(x + 12, 112, 218, 20), title, _small);
            GUI.Label(new Rect(x + 12, 133, 218, 32), value, _text);
        }

        private static string Rate(long bytes)
        {
            float rate = bytes / NetworkTraffic.Seconds;
            return rate >= 1024 ? $"{rate / 1024:0.00} KiB" : $"{rate:0} B";
        }

        private void DrawHighlight()
        {
            if (_selected?.View == null || Time.unscaledTime > _highlightUntil || Camera.main == null) return;
            Vector3 position = Camera.main.WorldToScreenPoint(_selected.View.transform.position + Vector3.up);
            if (position.z <= 0) return;
            Rect marker = new Rect(position.x - 12, Screen.height - position.y - 12, 24, 24);
            Fill(new Rect(marker.x, marker.y, 24, 3), Color.cyan);
            Fill(new Rect(marker.x, marker.y + 21, 24, 3), Color.cyan);
            Fill(new Rect(marker.x, marker.y, 3, 24), Color.cyan);
            Fill(new Rect(marker.x + 21, marker.y, 3, 24), Color.cyan);
            GUI.Label(new Rect(marker.x - 100, marker.y - 35, 360, 30), _selected.Name + " #" + _selected.Id.ID, _text);
        }

        internal static string Status()
        {
            if (!IsOpen) return "Network Ward closed; sampling inactive.";
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
