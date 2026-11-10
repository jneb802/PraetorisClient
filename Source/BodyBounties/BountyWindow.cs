using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace PraetorisClient.BodyBounties;

internal static class BountyWindow
{
    internal const string Title = "Body retrieval bounties";
    private static GameObject? _panel;
    private static string _port = "";
    private static int _tab;
    private static readonly Dictionary<Minimap.PinData, BountyRecord> SelectionPins = new();
    private static readonly List<Minimap.PinData> AcceptedPins = new();
    internal static bool Selecting;
    internal static bool Visible => _panel != null && _panel.activeSelf;

    internal static void Initialize() => BountyService.Received += Receive;

    internal static void Open(string port = "")
    {
        _port = port;
        _tab = 0;
        DrawList();
        BountyService.Request(new BountyMessage { Action = "list" });
    }

    internal static void Close()
    {
        if (_panel != null) UnityEngine.Object.Destroy(_panel);
        _panel = null;
        GUIManager.BlockInput(false);
    }

    private static void Receive(BountyMessage message)
    {
        if (message.Action == "error")
        { Player.m_localPlayer?.Message(MessageHud.MessageType.Center, message.Error); return; }
        if (message.Action == "list")
        {
            RestorePins();
            if (Visible && !Selecting) DrawList();
        }
        if (message.Action == "stones") ShowStones(message.Records);
        if (message.Action == "confirm" && message.Records.Count == 1) Confirm(message.Records[0]);
    }

    private static void Panel(float width = 900, float height = 650)
    {
        Close();
        _panel = GUIManager.Instance.CreateWoodpanel(GUIManager.CustomGUIFront.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, width, height, false);
        _panel.name = "BodyRetrievalBounties";
        _panel.AddComponent<BountyWindowInput>();
        Label(_panel.transform, Title, 25, 15, width - 90, 45, 28);
        Button(_panel.transform, "Close", width - 110, height - 50, 90, () => { EndSelection(); Close(); });
        GUIManager.BlockInput(true);
    }

    private static RectTransform Place(GameObject item, float x, float y, float width, float height)
    {
        RectTransform rect = (RectTransform)item.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    private static void Label(Transform parent, string value, float x, float y, float width, float height, int size = 18)
    {
        GameObject item = GUIManager.Instance.CreateText(value, parent, Vector2.zero, Vector2.zero, Vector2.zero,
            GUIManager.Instance.AveriaSerif, size, Color.white, true, Color.black, width, height, false);
        Place(item, x, y, width, height);
        Text text = item.GetComponent<Text>(); text.supportRichText = false; text.raycastTarget = false;
        text.alignment = TextAnchor.MiddleLeft;
    }

    private static void Button(Transform parent, string title, float x, float y, float width, Action action)
    {
        GameObject item = GUIManager.Instance.CreateButton(title, parent, Vector2.zero, Vector2.zero, Vector2.zero, width, 36);
        Place(item, x, y, width, 36);
        item.GetComponent<Button>().onClick.AddListener(() => action());
    }

    private static RectTransform Scroll(float y, float height, float width)
    {
        GameObject item = DefaultControls.CreateScrollView(new DefaultControls.Resources());
        item.transform.SetParent(_panel!.transform, false);
        Place(item, 20, y, width, height);
        ScrollRect scroll = item.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        UnityEngine.Object.Destroy(scroll.horizontalScrollbar.gameObject);
        scroll.horizontalScrollbar = null;
        item.GetComponent<Image>().color = Color.clear;
        GUIManager.Instance.ApplyScrollRectStyle(scroll);
        Place(scroll.content.gameObject, 0, 0, width - 25, height);
        return scroll.content;
    }

    private static void DrawList()
    {
        Panel();
        string[] tabs = { "Available", "Accepted", "My bounties" };
        for (int i = 0; i < tabs.Length; i++)
        {
            int tab = i;
            Button(_panel!.transform, tabs[i], 25 + i * 180, 75, 165, () => { _tab = tab; DrawList(); });
        }
        if (_port != "") Button(_panel!.transform, "Post bounty", 650, 75, 210, () =>
            BountyService.Request(new BountyMessage { Action = "stones", Port = _port }));
        long player = Player.m_localPlayer.GetPlayerID();
        IEnumerable<BountyRecord> records = BountyService.ClientRecords.Values;
        records = _tab == 2 ? records.Where(record => record.Owner == player) : _tab == 1
            ? records.Where(record => record.Accepted.Contains(player))
            : records.Where(record => record.State == BountyState.Posted || record.State == BountyState.Accepted || record.State == BountyState.InTransit);
        RectTransform rows = Scroll(125, 460, 860);
        int row = 0;
        foreach (BountyRecord record in records.OrderByDescending(record => record.PostedUtc))
        {
            float y = row++ * 130;
            Label(rows, record.OwnerName + " — " + record.Reward + " Coins — " + record.Weight.ToString("0.0") + " weight", 5, y, 815, 30);
            Label(rows, record.State + " | " + new DateTime(record.PostedUtc, DateTimeKind.Utc).ToLocalTime().ToString("g"), 5, y + 30, 815, 28, 16);
            float x = 5;
            void Action(string name, Action action) { Button(rows, name, x, y + 65, 170, action); x += 180; }
            if (record.Owner != player && !record.Accepted.Contains(player) &&
                (record.State == BountyState.Posted || record.State == BountyState.Accepted || record.State == BountyState.InTransit))
                Action("Accept bounty", () => Send("accept", record));
            if (record.State == BountyState.Posted || record.State == BountyState.Accepted)
                Action("View on map", () => { Close(); Minimap.instance.ShowPointOnMap(new Vector3(record.X, record.Y, record.Z)); });
            if (_port != "" && record.State == BountyState.InTransit && record.Accepted.Contains(player))
                Action("Deliver parcel", () => Send("deliver", record));
            if (_port != "" && record.Owner == player)
            {
                if (record.State == BountyState.Delivered) Action("Reclaim contents", () => Send("reclaim", record));
                if (record.State == BountyState.Posted || record.State == BountyState.Accepted) Action("Cancel bounty", () => Send("cancel", record));
            }
        }
        rows.sizeDelta = new Vector2(rows.sizeDelta.x, Math.Max(460, row * 130));
        if (row == 0) Label(rows, "No bounties in this category.", 5, 5, 800, 45);
        if (_port == "") Label(_panel!.transform, "Visit a bounty NPC to post, cancel, deliver, or reclaim.", 25, 595, 650, 35, 16);
    }

    private static void Send(string action, BountyRecord record) =>
        BountyService.Request(new BountyMessage { Action = action, Id = record.Id, Port = _port });

    private static void Confirm(BountyRecord record)
    {
        Panel(650, 300);
        Label(_panel!.transform, "Collect " + record.OwnerName + "'s tombstone?", 25, 75, 600, 45, 22);
        Label(_panel.transform, record.Reward + " Coins on delivery. Parcel weight: " + record.Weight.ToString("0.0"), 25, 125, 600, 45);
        Button(_panel.transform, "Confirm pickup", 25, 205, 210, () =>
        {
            Close();
            BountyService.Request(new BountyMessage { Action = "collect", Id = record.Id, Fingerprint = record.Fingerprint });
        });
        Button(_panel.transform, "Cancel", 255, 205, 160, Close);
    }

    private static void RestorePins()
    {
        if (Minimap.instance == null || Player.m_localPlayer == null) return;
        foreach (Minimap.PinData pin in AcceptedPins) Minimap.instance.RemovePin(pin);
        AcceptedPins.Clear();
        long player = Player.m_localPlayer.GetPlayerID();
        foreach (BountyRecord record in BountyService.ClientRecords.Values)
            if (record.Accepted.Contains(player) && record.State == BountyState.Accepted)
                AcceptedPins.Add(Minimap.instance.AddPin(new Vector3(record.X, record.Y, record.Z), Minimap.PinType.Death,
                    "Bounty: " + record.OwnerName, false, false));
    }

    private static void ShowStones(List<BountyRecord> records)
    {
        EndSelection(); Close();
        Selecting = true;
        foreach (BountyRecord record in records)
        {
            Minimap.PinData pin = Minimap.instance.AddPin(new Vector3(record.X, record.Y, record.Z), Minimap.PinType.Death,
                "Post body retrieval bounty", false, false);
            SelectionPins.Add(pin, record);
        }
        Minimap.instance.SetMapMode(Minimap.MapMode.Large);
        Player.m_localPlayer.Message(MessageHud.MessageType.Center, records.Count == 0 ? "No available tombstones." : "Select a tombstone icon to post a bounty.");
    }

    internal static bool MapClick()
    {
        if (!Selecting) return true;
        Vector3 point = Minimap.instance.ScreenToWorldPoint(ZInput.pointerPosition);
        KeyValuePair<Minimap.PinData, BountyRecord> selected = SelectionPins.OrderBy(pair => Vector3.Distance(pair.Key.m_pos, point)).FirstOrDefault();
        if (selected.Key == null || Vector3.Distance(selected.Key.m_pos, point) > Minimap.instance.PinInteractRadius) return false;
        Preview(selected.Value);
        return false;
    }

    private static void Preview(BountyRecord record)
    {
        Panel(500, 600);
        ((RectTransform)_panel!.transform).anchoredPosition = new Vector2(Screen.width * 0.25f, 0);
        Inventory inventory = BountyInventory.Load(record.Contents);
        RectTransform rows = Scroll(70, 350, 460);
        int index = 0;
        foreach (ItemDrop.ItemData item in inventory.GetAllItems())
        {
            GameObject icon = new GameObject("Item", typeof(RectTransform), typeof(Image));
            icon.transform.SetParent(rows, false);
            Place(icon, 5, index * 52, 45, 45);
            icon.GetComponent<Image>().sprite = item.GetIcon();
            Label(rows, Localization.instance.Localize(item.m_shared.m_name) + " ×" + item.m_stack, 60, index * 52, 355, 45);
            index++;
        }
        rows.sizeDelta = new Vector2(rows.sizeDelta.x, Math.Max(350, index * 52));
        Label(_panel.transform, "Reward in Coins", 25, 430, 200, 40);
        GameObject input = DefaultControls.CreateInputField(new DefaultControls.Resources());
        input.transform.SetParent(_panel.transform, false);
        Place(input, 240, 430, 210, 40);
        InputField reward = input.GetComponent<InputField>();
        reward.contentType = InputField.ContentType.IntegerNumber;
        reward.characterLimit = 10;
        reward.textComponent.font = GUIManager.Instance.AveriaSerif;
        reward.textComponent.color = Color.black;
        reward.textComponent.fontSize = 20;
        Button(_panel.transform, "Reserve coins and post", 25, 490, 300, () =>
        {
            if (!int.TryParse(reward.text, out int coins) || coins <= 0)
            { Player.m_localPlayer.Message(MessageHud.MessageType.Center, "Enter a positive whole number of Coins."); return; }
            BountyService.Request(new BountyMessage { Action = "post", Port = _port, Stone = record.Stone, Fingerprint = record.Fingerprint, Reward = coins });
            EndSelection();
            Minimap.instance.SetMapMode(Minimap.MapMode.Small);
            DrawList();
        });
    }

    internal static void EndSelection()
    {
        if (Minimap.instance != null) foreach (Minimap.PinData pin in SelectionPins.Keys) Minimap.instance.RemovePin(pin);
        SelectionPins.Clear(); Selecting = false;
    }
}

internal sealed class BountyWindowInput : MonoBehaviour
{
    private void Update()
    {
        // Local input only. Network requests originate from explicit UI actions.
        if (ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyButtonB"))
        {
            BountyWindow.EndSelection();
            BountyWindow.Close();
            ZInput.ResetButtonStatus("JoyButtonB");
        }
    }
}

[HarmonyPatch(typeof(TextsDialog), "UpdateTextsList")]
internal static class BountyCompendiumPage
{
    private static void Postfix(TextsDialog __instance) => __instance.m_texts.Add(new TextsDialog.TextInfo(BountyWindow.Title, "Browse, accept, and track body retrieval bounties."));
}

[HarmonyPatch(typeof(TextsDialog), "ShowText", new Type[] { typeof(TextsDialog.TextInfo) })]
internal static class BountyCompendiumOpen
{
    private static void Postfix(TextsDialog.TextInfo text)
    {
        if (text.m_topic == BountyWindow.Title) BountyWindow.Open();
    }
}

[HarmonyPatch(typeof(Minimap), nameof(Minimap.OnMapLeftClick))]
internal static class BountyMapSelection
{
    private static bool Prefix() => BountyWindow.MapClick();
}

[HarmonyPatch(typeof(PlayerController), "TakeInput")]
internal static class BountyInput
{
    private static void Postfix(ref bool __result)
    {
        if (BountyWindow.Visible) __result = false;
    }
}

[HarmonyPatch(typeof(Minimap), nameof(Minimap.SetMapMode))]
internal static class BountyMapClose
{
    private static void Postfix(Minimap.MapMode mode)
    {
        if (mode != Minimap.MapMode.Large && BountyWindow.Selecting)
        { BountyWindow.EndSelection(); BountyWindow.Close(); }
    }
}
