using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PraetorisClient.BodyBounties;

// The shipping picker pattern: large map, highlighted target, sidebar, explicit confirmation, back.
internal static class BountyMapPicker
{
    private sealed class Entry
    {
        internal Minimap.PinData Pin = null!;
        internal BountyRecord Record = null!;
    }

    private static readonly List<Entry> Entries = new();
    private static GameObject? _sidebar;
    private static TMP_Text? _details;
    private static Action<BountyRecord>? _select;
    private static Action? _return;
    private static Entry? _hovered;
    private static int _cycle = -1;
    private static float _nextDetails;
    private static int _ignoreFrame;
    private static bool _deathPinsVisible;
    internal static bool IsOpen { get; private set; }

    internal static void Open(List<BountyRecord> records, Action<BountyRecord>? select, Action returnToView)
    {
        Close(false);
        if (Minimap.instance == null || Player.m_localPlayer == null || Game.m_noMap)
        {
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "A map is required to select a tombstone.");
            returnToView();
            return;
        }
        Minimap map = Minimap.instance;
        map.ShowPointOnMap(records.Count > 0 ? BountyUi.Position(records[0]) : Player.m_localPlayer.transform.position);
        if (map.m_mode != Minimap.MapMode.Large) { returnToView(); return; }
        IsOpen = true; _select = select; _return = returnToView;
        _cycle = -1; _ignoreFrame = Time.frameCount;
        _deathPinsVisible = map.m_visibleIconTypes[(int)Minimap.PinType.Death];
        map.m_visibleIconTypes[(int)Minimap.PinType.Death] = true;
        foreach (BountyRecord record in records)
        {
            Minimap.PinData pin = map.AddPin(BountyUi.Position(record), Minimap.PinType.Death,
                select == null ? "Bounty: " + record.OwnerName : "Your tombstone", false, false);
            pin.m_doubleSize = true;
            Entries.Add(new Entry { Pin = pin, Record = record });
        }
        RectTransform sidebar = BountyUi.Rect("BountyMapSidebar", map.m_largeRoot.transform, 24, 0, 330, 360);
        sidebar.anchorMin = sidebar.anchorMax = sidebar.pivot = new Vector2(0, 0.5f);
        sidebar.anchoredPosition = new Vector2(24, 0);
        _sidebar = sidebar.gameObject;
        BountyUi.Surface(sidebar, "Bkg", 0, 0, 330, 360).raycastTarget = true;
        BountyUi.Label(sidebar, select == null ? "Bounty location" : "Choose your tombstone", 16, 18, 298, 65, 24).color = BountyUi.Gold;
        _details = BountyUi.Label(sidebar, "Point at a tombstone to inspect it.", 16, 90, 298, 140, 18);
        BountyUi.Label(sidebar, "Mouse: select an icon\nController: A selects · LB/RB cycles\nEscape / B: return", 16, 235, 298, 70, 16);
        BountyUi.Button(sidebar, "Back", 16, 312, 298, () => Close(true));
    }

    internal static void Close(bool returnToView)
    {
        if (!IsOpen) return;
        IsOpen = false;
        if (Minimap.instance != null)
        {
            foreach (Entry entry in Entries) Minimap.instance.RemovePin(entry.Pin);
            Minimap.instance.m_visibleIconTypes[(int)Minimap.PinType.Death] = _deathPinsVisible;
            Minimap.instance.m_pinUpdateRequired = true;
        }
        Entries.Clear(); _hovered = null; _select = null;
        if (_sidebar != null) { _sidebar.SetActive(false); UnityEngine.Object.Destroy(_sidebar); }
        _sidebar = null; _details = null;
        Action? back = _return; _return = null;
        BountyWindow.Close();
        if (returnToView && Minimap.instance != null)
        {
            Minimap.instance.SetMapMode(Minimap.MapMode.Small);
            if (Player.m_localPlayer != null && !Player.m_localPlayer.IsDead()) back?.Invoke();
        }
        ZInput.ResetButtonStatus("JoyButtonA"); ZInput.ResetButtonStatus("JoyButtonB");
    }

    private static Entry? Target()
    {
        Minimap map = Minimap.instance;
        Vector3 pointer = ZInput.IsExclusiveGamepadActive()
            ? new Vector3(Screen.width / 2f, Screen.height / 2f) : ZInput.pointerPosition;
        if (!ZInput.IsExclusiveGamepadActive() && _sidebar != null && RectTransformUtility.RectangleContainsScreenPoint(
            (RectTransform)_sidebar.transform, pointer)) return null;
        Vector3 point = map.ScreenToWorldPoint(pointer);
        float radius = map.PinInteractRadius;
        Entry? nearest = null;
        foreach (Entry entry in Entries)
        {
            float distance = global::Utils.DistanceXZ(entry.Pin.m_pos, point);
            if (distance >= radius) continue;
            radius = distance; nearest = entry;
        }
        return nearest;
    }

    private static bool TakeInput() => !BountyWindow.Visible && !global::Console.IsVisible()
        && !TextInput.IsVisible() && !Menu.IsActive() && !InventoryGui.IsVisible()
        && (Chat.instance == null || !Chat.instance.HasFocus()) && !ZInput.VirtualKeyboardOpen && !Minimap.InTextInput();

    private static void Choose(Entry? target)
    {
        if (target == null || _select == null || !TakeInput()) return;
        _ignoreFrame = Time.frameCount;
        _select(target.Record);
    }

    internal static void Tick()
    {
        if (!IsOpen || Minimap.instance.m_mode != Minimap.MapMode.Large || Player.m_localPlayer == null) return;
        if (BountyWindow.Visible || !TakeInput() || Time.frameCount == _ignoreFrame) return;
        if (ZInput.IsGamepadActive() && Minimap.instance.inputDelay <= 0)
        {
            if (ZInput.GetButtonDown("JoyTabLeft") || ZInput.GetButtonDown("JoyTabRight"))
            {
                int step = ZInput.GetButtonDown("JoyTabLeft") ? -1 : 1;
                if (Entries.Count > 0)
                {
                    _cycle = (_cycle + step + Entries.Count) % Entries.Count;
                    Minimap.instance.ShowPointOnMap(Entries[_cycle].Pin.m_pos);
                }
                ZInput.ResetButtonStatus("JoyTabLeft"); ZInput.ResetButtonStatus("JoyTabRight");
            }
            if (ZInput.GetButtonDown("JoyButtonA"))
            { ZInput.ResetButtonStatus("JoyButtonA"); Choose(Target()); }
        }
        Entry? target = Target();
        foreach (Entry entry in Entries)
        {
            Color color = entry == target ? BountyUi.Gold : Color.white;
            if (entry.Pin.m_iconElement != null) entry.Pin.m_iconElement.color = color;
            TMP_Text? label = entry.Pin.m_NamePinData?.PinNameText;
            if (label != null) label.color = color;
        }
        if (_details != null && (target != _hovered || Time.unscaledTime >= _nextDetails))
        {
            _hovered = target; _nextDetails = Time.unscaledTime + 0.5f;
            _details.text = target == null ? "Point at a tombstone to inspect it." :
                $"{target.Record.OwnerName}\nDistance: {BountyUi.Distance(target.Record):0} m\nWeight: {target.Record.Weight:0.0}\n" +
                (_select == null ? $"Reward: {target.Record.Reward} Coins" : "Select to preview contents and set your coin reward.");
        }
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.Update))]
    private static class MapUpdate
    {
        private static void Prefix() => Tick();
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnMapLeftClick))]
    private static class MapClick
    {
        private static bool Prefix()
        {
            if (!IsOpen) return true;
            if (BountyWindow.Visible) return false;
            Entry? target = Target();
            if (target == null) return true;
            if (TakeInput() && Time.frameCount != _ignoreFrame) Choose(target);
            return false;
        }
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnMapDblClick))]
    private static class MapDoubleClick
    {
        private static bool Prefix() => !IsOpen || !BountyWindow.Visible && Target() == null;
    }

    [HarmonyPatch(typeof(Minimap), "RemovePinUnderPointer")]
    private static class MapRightClick
    {
        private static bool Prefix() => !IsOpen || !BountyWindow.Visible && Target() == null;
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.SetMapMode))]
    private static class MapClose
    {
        private static bool Prefix(Minimap.MapMode mode)
        {
            if (!IsOpen || mode == Minimap.MapMode.Large) return true;
            if (BountyWindow.Visible && Player.m_localPlayer != null && !Player.m_localPlayer.IsDead())
            { BountyWindow.Back(); return false; }
            return true;
        }
        private static void Postfix(Minimap.MapMode mode)
        { if (IsOpen && mode != Minimap.MapMode.Large && Minimap.instance.m_mode != Minimap.MapMode.Large) Close(true); }
    }

    [HarmonyPatch(typeof(Minimap), "OnDestroy")]
    private static class MapDestroyed
    {
        private static void Prefix() => Close(false);
    }
}
