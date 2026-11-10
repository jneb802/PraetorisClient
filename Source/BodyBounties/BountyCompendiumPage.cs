using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PraetorisClient.BodyBounties;

// Like the endgame leaderboard, this replaces only the book's text area, not the book itself.
[HarmonyPatch]
internal sealed class BountyCompendiumPage : MonoBehaviour
{
    private sealed class PageInfo : TextsDialog.TextInfo
    {
        internal PageInfo() : base(BountyWindow.Title, "") { }
    }

    private TextsDialog _dialog = null!;
    private GameObject _original = null!;
    private RectTransform _panel = null!;
    private ScrollRect _scroll = null!;
    private TMP_Text _table = null!;
    private TMP_Text _status = null!;
    private Button _accept = null!;
    private Button _map = null!;
    private bool _selected;
    private int _tab;
    private int _row;
    private readonly List<BountyRecord> _records = new();

    [HarmonyPatch(typeof(TextsDialog), "UpdateTextsList"), HarmonyPostfix]
    private static void AddPage(TextsDialog __instance)
    {
        if (__instance.name.IndexOf("Lore", StringComparison.OrdinalIgnoreCase) < 0) __instance.m_texts.Add(new PageInfo());
    }

    [HarmonyPatch(typeof(TextsDialog), "ShowText", typeof(TextsDialog.TextInfo)), HarmonyPrefix]
    private static void BeforeShow(TextsDialog __instance)
    {
        BountyCompendiumPage page = __instance.GetComponent<BountyCompendiumPage>();
        if (page != null) page.Hide();
    }

    [HarmonyPatch(typeof(TextsDialog), "ShowText", typeof(TextsDialog.TextInfo)), HarmonyPostfix, HarmonyAfter("randyknapp.mods.epicloot")]
    private static void Show(TextsDialog __instance, TextsDialog.TextInfo text)
    {
        if (text is not PageInfo) return;
        BountyCompendiumPage page = __instance.GetComponent<BountyCompendiumPage>() ?? __instance.gameObject.AddComponent<BountyCompendiumPage>();
        if (page._panel == null) page.Create(__instance);
        page._original.SetActive(false); page._panel.gameObject.SetActive(true); page._selected = true;
        page.Refresh();
        BountyService.Request(new BountyMessage { Action = "list" });
    }

    private void Create(TextsDialog dialog)
    {
        _dialog = dialog;
        Transform area = dialog.transform.Find("Texts_frame/TextArea") ?? dialog.transform.Find("TextArea");
        if (area == null) area = dialog.m_textArea.GetComponentInParent<ScrollRect>().transform;
        _original = area.gameObject;
        RectTransform source = (RectTransform)area;
        _panel = BountyUi.Rect("BodyBountyTable", area.parent, 0, 0, source.rect.width, source.rect.height);
        _panel.anchorMin = source.anchorMin; _panel.anchorMax = source.anchorMax;
        _panel.pivot = source.pivot; _panel.sizeDelta = source.sizeDelta; _panel.anchoredPosition = source.anchoredPosition;
        float width = source.rect.width;
        float height = source.rect.height;
        string[] tabs = { "Available", "Accepted", "My bounties" };
        for (int i = 0; i < tabs.Length; i++)
        {
            int tab = i;
            BountyUi.Button(_panel, tabs[i], i * (width / 3), 0, width / 3 - 5,
                () => { _tab = tab; _row = 0; Refresh(); });
        }
        _scroll = BountyUi.Scroll(_panel, 0, 50, width, height - 180);
        _scroll.horizontal = true;
        _table = _scroll.content.gameObject.AddComponent<TextMeshProUGUI>();
        _table.font = dialog.m_textArea.font; _table.fontSize = 19; _table.color = dialog.m_textArea.color;
        _table.alignment = TextAlignmentOptions.TopLeft; _table.textWrappingMode = TextWrappingModes.NoWrap;
        _table.richText = true; _table.raycastTarget = true;
        BountyTableClick click = _table.gameObject.AddComponent<BountyTableClick>();
        click.Text = _table;
        click.Select = index => { if (index >= 0 && index < _records.Count) { _row = index; Refresh(); } };
        _status = BountyUi.Label(_panel, "", 0, height - 120, width, 60, 17);
        BountyUi.Button(_panel, "Previous", 0, height - 55, 105, () => { _row = Math.Max(0, _row - 1); Refresh(); });
        BountyUi.Button(_panel, "Next", 112, height - 55, 85, () => { _row = Math.Min(_records.Count - 1, _row + 1); Refresh(); });
        _accept = BountyUi.Button(_panel, "Accept", 204, height - 55, 110, () =>
        {
            if (_row < 0 || _row >= _records.Count) return;
            BountyService.Request(new BountyMessage { Action = "accept", Id = _records[_row].Id });
        });
        _map = BountyUi.Button(_panel, "Map", 321, height - 55, 85, ViewMap);
        // Horizontal scrolling is necessary for the fixed, aligned columns, as on the leaderboard.
        Scrollbar horizontal = Instantiate(dialog.m_rightScrollbar, _panel);
        BountyUi.Place((RectTransform)horizontal.transform, 0, height - 142, width, 12);
        horizontal.direction = Scrollbar.Direction.LeftToRight;
        horizontal.onValueChanged = new Scrollbar.ScrollEvent(); horizontal.gameObject.SetActive(true);
        _scroll.horizontalScrollbar = horizontal;
        _scroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
    }

    private void ViewMap()
    {
        if (_row < 0 || _row >= _records.Count || !BountyUi.Available(_records[_row])) return;
        BountyRecord record = _records[_row];
        InventoryGui.instance.Hide();
        BountyMapPicker.Open(new List<BountyRecord> { record }, null, () =>
        {
            InventoryGui.instance.Show(null);
            _dialog.Setup(Player.m_localPlayer);
            TextsDialog.TextInfo? info = _dialog.m_texts.FirstOrDefault(text => text is PageInfo);
            if (info != null) _dialog.ShowText(info);
        });
    }

    private void Receive(BountyMessage message)
    {
        if (!_selected) return;
        if (message.Action == "list") Refresh();
        if (message.Action == "error" && _status != null) _status.text = message.Error;
    }

    private static string Safe(string value) => value.Replace("<", "").Replace(">", "").Replace("\n", " ").Replace("\r", " ");

    private void Refresh()
    {
        if (_panel == null || Player.m_localPlayer == null) return;
        long player = Player.m_localPlayer.GetPlayerID();
        _records.Clear();
        _records.AddRange(BountyService.ClientRecords.Values.Where(record => _tab == 2 ? record.Owner == player :
            _tab == 1 ? record.Accepted.Contains(player) : BountyUi.Available(record) || record.State == BountyState.InTransit)
            .OrderByDescending(record => record.PostedUtc));
        _row = Mathf.Clamp(_row, 0, Mathf.Max(0, _records.Count - 1));
        StringBuilder text = new();
        text.AppendLine("Click a row to select it. Visit an NPC to post, cancel, deliver, or reclaim.");
        text.AppendLine("\n<color=#FFD580>Owner<pos=250>Reward<pos=370>Weight<pos=480>Status<pos=680>Posted</color>");
        for (int i = 0; i < _records.Count; i++)
        {
            BountyRecord record = _records[i];
            string name = Safe(record.OwnerName);
            if (name.Length > 20) name = name.Substring(0, 19) + "…";
            if (i == _row) text.Append("<color=#FFD580>");
            text.Append($"<link=\"{i}\">{(i == _row ? "▶ " : "   ")}{name}<pos=250>{record.Reward} Coins<pos=370>{record.Weight:0.0}<pos=480>{BountyUi.State(record)}<pos=680>{new DateTime(record.PostedUtc, DateTimeKind.Utc).ToLocalTime():g}</link>");
            if (i == _row) text.Append("</color>");
            text.AppendLine();
        }
        if (_records.Count == 0) text.AppendLine("No bounties in this category.");
        _table.text = text.ToString();
        _scroll.content.sizeDelta = new Vector2(950, Mathf.Max(_scroll.viewport.rect.height, _table.preferredHeight + 20));
        BountyRecord? selected = _records.Count > 0 ? _records[_row] : null;
        _status.text = selected == null ? "No bounty selected." : $"Selected: {selected.OwnerName} · Reward: {selected.Reward} Coins · {BountyUi.State(selected)}";
        _accept.interactable = selected != null && BountyUi.CanAccept(selected, player);
        _map.interactable = selected != null && BountyUi.Available(selected) && !Game.m_noMap;
    }

    private void Update()
    {
        if (!_selected || !ZInput.IsExclusiveGamepadActive()) return;
        _scroll.horizontalNormalizedPosition = Mathf.Clamp01(_scroll.horizontalNormalizedPosition + ZInput.GetJoyRightStickX() * Time.unscaledDeltaTime);
        _scroll.verticalNormalizedPosition = Mathf.Clamp01(_scroll.verticalNormalizedPosition - ZInput.GetJoyRightStickY() * Time.unscaledDeltaTime);
    }

    private void Hide()
    {
        if (!_selected) return;
        _selected = false;
        if (_panel != null) _panel.gameObject.SetActive(false);
        if (_original != null) _original.SetActive(true);
    }
    private void OnEnable() => BountyService.Received += Receive;
    private void OnDisable() { BountyService.Received -= Receive; Hide(); }
    private void OnDestroy() { if (_panel != null) Destroy(_panel.gameObject); }
}

internal sealed class BountyTableClick : MonoBehaviour, IPointerClickHandler
{
    internal TMP_Text Text = null!;
    internal Action<int> Select = null!;
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        int link = TMP_TextUtilities.FindIntersectingLink(Text, eventData.position, eventData.pressEventCamera);
        if (link >= 0 && int.TryParse(Text.textInfo.linkInfo[link].GetLinkID(), out int row)) Select(row);
    }
}
