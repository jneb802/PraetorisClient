using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Jotunn.Managers;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PraetorisClient.BodyBounties;

internal static class BountyWindow
{
    internal const string Title = "Body retrieval bounties";
    private static GameObject? _panel;
    private static string _port = "";
    private static string _selected = "";
    private static int _tab;
    private static bool _waitingForStones;
    private static bool _showingList;
    private static Action? _back;
    internal static int OpenedFrame { get; private set; }
    private static readonly List<Minimap.PinData> AcceptedPins = new();
    internal static bool Selecting => BountyMapPicker.IsOpen;
    internal static bool Visible => _panel != null && _panel.activeInHierarchy;

    internal static void Initialize() => BountyService.Received += Receive;

    internal static void Open(string port = "")
    {
        if (port.Length == 0) return;
        EndSelection();
        _port = port; _tab = 0; _selected = "";
        DrawList();
        BountyService.Request(new BountyMessage { Action = "list" });
    }

    internal static void Close()
    {
        _waitingForStones = false;
        _showingList = false;
        if (_panel != null) { _panel.SetActive(false); UnityEngine.Object.Destroy(_panel); }
        _panel = null; _back = null;
        GUIManager.BlockInput(false);
    }

    internal static void Back()
    {
        Action? back = _back;
        Close(); back?.Invoke();
        ZInput.ResetButtonStatus("JoyButtonB");
    }

    private static void Receive(BountyMessage message)
    {
        if (message.Action == "error")
        { Player.m_localPlayer?.Message(MessageHud.MessageType.Center, message.Error); _waitingForStones = false; return; }
        if (message.Action == "list")
        {
            RestorePins();
            if (Visible && !Selecting && _showingList) DrawList();
        }
        if (message.Action == "stones" && _waitingForStones)
        {
            _waitingForStones = false;
            if (message.Records.Count == 0)
            { Player.m_localPlayer.Message(MessageHud.MessageType.Center, "No available tombstones."); return; }
            Close(); BountyMapPicker.Open(message.Records, Preview, DrawList);
        }
        if (message.Action == "confirm" && message.Records.Count == 1) Confirm(message.Records[0]);
    }

    private static Transform Panel(float width = 900, float height = 650, string title = Title, Action? back = null)
    {
        Close();
        RectTransform rect = BountyUi.Rect("BodyRetrievalBounties", GUIManager.CustomGUIFront.transform, 0, 0, width, height);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        _panel = rect.gameObject; _back = back;
        OpenedFrame = Time.frameCount;
        BountyUi.Surface(rect, "Bkg", -10, -10, width + 20, height + 20).raycastTarget = true;
        BountyUi.Label(rect, title, 25, 16, width - 50, 45, 28).color = BountyUi.Gold;
        BountyUi.Surface(rect, "TabsButtons/TabBorder", 25, 65, width - 50, 2);
        BountyUi.Button(rect, back == null ? "Close" : "Back", width - 135, height - 55, 110, Back);
        _panel.AddComponent<BountyWindowInput>();
        GUIManager.BlockInput(true);
        return rect;
    }

    private static void DrawList()
    {
        if (Player.m_localPlayer == null) return;
        Transform panel = Panel();
        _showingList = true;
        string[] tabs = { "Available", "Accepted", "My bounties" };
        for (int i = 0; i < tabs.Length; i++)
        {
            int tab = i;
            BountyUi.Button(panel, tabs[i], 25 + i * 165, 78, 155,
                () => { _tab = tab; _selected = ""; DrawList(); }, _tab == i, true);
        }
        BountyUi.Button(panel, "Post bounty", 650, 78, 225, () =>
        {
            if (_waitingForStones) return;
            _waitingForStones = true;
            BountyService.Request(new BountyMessage { Action = "stones", Port = _port });
        }, false, true);
        BountyUi.Surface(panel, "RecipeList/Recipes", 25, 132, 360, 445);
        BountyUi.Surface(panel, "Decription", 400, 132, 475, 445);
        long player = Player.m_localPlayer.GetPlayerID();
        IEnumerable<BountyRecord> source = BountyService.ClientRecords.Values;
        source = _tab == 2 ? source.Where(record => record.Owner == player) : _tab == 1
            ? source.Where(record => record.Accepted.Contains(player))
            : source.Where(record => BountyUi.Available(record) || record.State == BountyState.InTransit);
        List<BountyRecord> records = source.OrderByDescending(record => record.PostedUtc).ToList();
        BountyRecord? selected = records.FirstOrDefault(record => record.Id == _selected) ?? records.FirstOrDefault();
        _selected = selected?.Id ?? "";
        ScrollRect scroll = BountyUi.Scroll(panel, 32, 140, 346, 429);
        for (int i = 0; i < records.Count; i++)
        {
            BountyRecord record = records[i];
            Image row = BountyUi.Surface(scroll.content, "selected_frame/selected (1)", 0, i * 76, 325, 70);
            row.color = record == selected ? new Color(1, 0.85f, 0.3f, 0.3f) : new Color(1, 1, 1, 0.04f);
            row.raycastTarget = true;
            Button button = row.gameObject.AddComponent<Button>(); button.targetGraphic = row;
            button.onClick.AddListener(() => { _selected = record.Id; DrawList(); });
            BountyUi.Label(row.transform, record.OwnerName, 10, 6, 300, 28, 22);
            BountyUi.Label(row.transform, $"{record.Reward} Coins · {BountyUi.State(record)}", 10, 37, 300, 27, 16);
        }
        scroll.content.sizeDelta = new Vector2(scroll.content.sizeDelta.x, Mathf.Max(429, records.Count * 76));
        if (selected == null) BountyUi.Label(panel, "No bounties in this category.", 425, 155, 425, 80);
        else Details(panel, selected, player);
        BountyUi.Label(panel, "Select a bounty to inspect its details and available actions.", 25, 590, 705, 42, 16);
    }

    private static void Details(Transform panel, BountyRecord record, long player)
    {
        BountyUi.Label(panel, record.OwnerName, 420, 152, 430, 45, 28).color = BountyUi.Gold;
        BountyUi.Label(panel, $"Reward: {record.Reward} Coins on delivery\nParcel weight: {record.Weight:0.0}\nStatus: {BountyUi.State(record)}\nDistance: {BountyUi.Distance(record):0} m\nPosted: {new DateTime(record.PostedUtc, DateTimeKind.Utc).ToLocalTime():g}", 420, 215, 430, 175, 20);
        float y = 395;
        void Action(string name, Action action)
        { BountyUi.Button(panel, name, 420, y, 430, action); y += 48; }
        if (BountyUi.CanAccept(record, player)) Action("Accept bounty", () => Send("accept", record));
        if (BountyUi.Available(record) && !Game.m_noMap)
            Action("View on map", () => { Close(); BountyMapPicker.Open(new List<BountyRecord> { record }, null, DrawList); });
        if (record.State == BountyState.InTransit && record.Accepted.Contains(player)) Action("Deliver parcel", () => Send("deliver", record));
        if (record.Owner == player)
        {
            if (record.State == BountyState.Delivered) Action("Reclaim contents", () => Send("reclaim", record));
            if (BountyUi.Available(record)) Action("Cancel bounty", () => Send("cancel", record));
        }
    }

    private static void Send(string action, BountyRecord record) =>
        BountyService.Request(new BountyMessage { Action = action, Id = record.Id, Port = _port });

    private static void Confirm(BountyRecord record)
    {
        Transform panel = Panel(650, 320, "Collect tombstone parcel");
        BountyUi.Label(panel, $"Collect {record.OwnerName}'s tombstone?", 25, 80, 600, 45, 22);
        BountyUi.Label(panel, $"{record.Reward} Coins on delivery. Parcel weight: {record.Weight:0.0}", 25, 135, 600, 70);
        BountyUi.Button(panel, "Confirm pickup", 25, 220, 235, () =>
        {
            Close(); BountyService.Request(new BountyMessage { Action = "collect", Id = record.Id, Fingerprint = record.Fingerprint });
        });
        BountyUi.Button(panel, "Cancel", 280, 220, 160, Close);
    }

    private static void RestorePins()
    {
        if (Minimap.instance == null || Player.m_localPlayer == null) return;
        foreach (Minimap.PinData pin in AcceptedPins) Minimap.instance.RemovePin(pin);
        AcceptedPins.Clear();
        long player = Player.m_localPlayer.GetPlayerID();
        foreach (BountyRecord record in BountyService.ClientRecords.Values)
            if (record.Accepted.Contains(player) && record.State == BountyState.Accepted)
                AcceptedPins.Add(Minimap.instance.AddPin(BountyUi.Position(record), Minimap.PinType.Death, "Bounty: " + record.OwnerName, false, false));
    }

    private static void Preview(BountyRecord record)
    {
        Transform panel = Panel(600, 650, "Post body retrieval bounty", () => { if (!Selecting) DrawList(); });
        BountyUi.Label(panel, $"Distance: {BountyUi.Distance(record):0} m · Weight: {record.Weight:0.0}", 25, 78, 550, 35);
        BountyUi.Surface(panel, "RecipeList/Recipes", 25, 120, 550, 335);
        Inventory inventory = BountyInventory.Load(record.Contents);
        ScrollRect scroll = BountyUi.Scroll(panel, 32, 128, 536, 319);
        int index = 0;
        foreach (ItemDrop.ItemData item in inventory.GetAllItems())
        {
            RectTransform cell = BountyUi.Rect("Item", scroll.content, 5, index * 52, 45, 45);
            Image icon = cell.gameObject.AddComponent<Image>();
            icon.sprite = item.GetIcon(); icon.preserveAspect = true; icon.raycastTarget = false;
            BountyUi.Label(scroll.content, Localization.instance.Localize(item.m_shared.m_name) + " ×" + item.m_stack, 60, index * 52, 440, 45);
            index++;
        }
        scroll.content.sizeDelta = new Vector2(scroll.content.sizeDelta.x, Math.Max(319, index * 52));
        BountyUi.Label(panel, "Reward in Coins", 25, 470, 230, 40);
        RectTransform rect = BountyUi.Rect("Reward", panel, 265, 465, 310, 42);
        // TMP_InputField initializes in OnEnable. Configure it while inactive, or it never accepts text.
        rect.gameObject.SetActive(false);
        Image background = rect.gameObject.AddComponent<Image>(); background.color = new Color(0, 0, 0, 0.5f);
        TMP_Text text = BountyUi.Label(rect, "", 10, 5, 290, 32, 22);
        TMP_InputField reward = rect.gameObject.AddComponent<TMP_InputField>();
        reward.textViewport = rect; reward.textComponent = (TextMeshProUGUI)text;
        reward.contentType = TMP_InputField.ContentType.IntegerNumber; reward.characterLimit = 10; reward.targetGraphic = background;
        rect.gameObject.SetActive(true);
        reward.Select(); reward.ActivateInputField();
        BountyUi.Label(panel, "Posting reserves your coins. The retriever is paid on delivery.", 25, 520, 550, 50, 16);
        BountyUi.Button(panel, "Reserve coins and post", 25, 590, 400, () =>
        {
            if (!int.TryParse(reward.text, out int coins) || coins <= 0)
            { Player.m_localPlayer.Message(MessageHud.MessageType.Center, "Enter a positive whole number of Coins."); return; }
            BountyService.Request(new BountyMessage { Action = "post", Port = _port, Stone = record.Stone, Fingerprint = record.Fingerprint, Reward = coins });
            Close(); BountyMapPicker.Close(true);
        });
    }

    internal static void EndSelection() => BountyMapPicker.Close(false);
}

internal sealed class BountyWindowInput : MonoBehaviour
{
    private void Update()
    {
        if (Player.m_localPlayer == null || Player.m_localPlayer.IsDead())
        { BountyWindow.EndSelection(); BountyWindow.Close(); return; }
        if (Time.frameCount > BountyWindow.OpenedFrame &&
            (ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyButtonB"))) BountyWindow.Back();
    }
}

[HarmonyPatch(typeof(PlayerController), "TakeInput")]
internal static class BountyInput
{
    private static void Postfix(ref bool __result) { if (BountyWindow.Visible) __result = false; }
}
