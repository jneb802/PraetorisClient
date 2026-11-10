using System;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace PraetorisClient.RoadTravel;

internal sealed class RoadTravelWindow : MonoBehaviour
{
    private static RoadTravelWindow? _instance;
    private GameObject? _panel;
    private bool _blocked;
    private int _page, _selected = -1;
    private Vector3 _origin;
    private const int PageSize = 6;
    internal static void Open()
    {
        if (Player.m_localPlayer == null || GUIManager.CustomGUIFront == null) return;
        if (_instance == null) _instance = Player.m_localPlayer.gameObject.AddComponent<RoadTravelWindow>();
        Close(); _instance._origin = Player.m_localPlayer.transform.position; _instance._page = 0; _instance._selected = -1;
        GUIManager.BlockInput(true); _instance._blocked = true; _instance.Draw();
    }
    internal static void Close()
    {
        if (_instance == null) return;
        if (_instance._panel != null) Destroy(_instance._panel);
        _instance._panel = null;
        if (_instance._blocked) { GUIManager.BlockInput(false); _instance._blocked = false; }
    }
    private void OnDestroy() { Close(); if (_instance == this) _instance = null; }
    private void Update()
    {
        if (_panel == null) return;
        Player player = Player.m_localPlayer;
        if (player == null || player.IsDead() || player.IsTeleporting() || !RoadTravelService.Ready || Vector3.Distance(player.transform.position, _origin) > 5 ||
            ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyButtonB"))
        { Close(); ZInput.ResetButtonStatus("JoyButtonB"); return; }
        Rect parent = ((RectTransform)_panel.transform.parent).rect;
        _panel.transform.localScale = Vector3.one * Mathf.Min(1, Mathf.Min(parent.width / 700, parent.height / 590));
    }
    private static void Place(GameObject go, float x, float y, float width, float height)
    {
        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
    }
    private void Label(string text, float x, float y, float width, float height, int size = 20)
    {
        GUIManager gui = GUIManager.Instance;
        GameObject label = gui.CreateText(text, _panel!.transform, Vector2.zero, Vector2.zero, Vector2.zero,
            gui.AveriaSerif, size, Color.white, true, Color.black, width, height, false);
        Place(label, x, y, width, height);
        Text component = label.GetComponent<Text>(); component.supportRichText = false; component.raycastTarget = false;
    }
    private void Button(string text, float x, float y, float width, Action action)
    {
        GameObject go = GUIManager.Instance.CreateButton(text, _panel!.transform, Vector2.zero, Vector2.zero, Vector2.zero, width, 42);
        Place(go, x, y, width, 42); go.GetComponent<Button>().onClick.AddListener(() => action());
    }
    private void Draw()
    {
        if (_panel != null) Destroy(_panel);
        _panel = GUIManager.Instance.CreateWoodpanel(GUIManager.CustomGUIFront.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, 660, 550, false);
        _panel.name = "RoadTravelPanel";
        Label("Road travel", 30, 20, 600, 45, 32);
        Label("Choose a destination connected by road.", 30, 75, 600, 35);
        if (RoadTravelService.Destinations.Count == 0) Label("No connected destinations are available.", 30, 130, 600, 60);
        for (int row = 0; row < PageSize; row++)
        {
            int index = _page * PageSize + row;
            if (index >= RoadTravelService.Destinations.Count) break;
            RoadTravelService.Destination destination = RoadTravelService.Destinations[index];
            Button((_selected == index ? "✓ " : "") + destination.Name + " — " + destination.Price + " coins", 30, 120 + row * 48, 600,
                () => { _selected = index; Draw(); });
        }
        if (_page > 0) Button("Previous", 30, 425, 145, () => { _page--; Draw(); });
        if ((_page + 1) * PageSize < RoadTravelService.Destinations.Count) Button("Next", 190, 425, 145, () => { _page++; Draw(); });
        Button("Close", 30, 485, 160, Close);
        if (_selected >= 0 && _selected < RoadTravelService.Destinations.Count)
        {
            int chosen = _selected;
            Button("Travel · " + RoadTravelService.Destinations[chosen].Price + " coins", 330, 485, 300, () => RoadTravelService.Confirm(chosen));
        }
    }
}
