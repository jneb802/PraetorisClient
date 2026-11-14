using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using HarmonyLib;
using UnityEngine;

namespace PraetorisClient.BodyBounties;

internal sealed class BountyMessage
{
    public string Action = "";
    public string Id = "";
    public string Port = "";
    public string Stone = "";
    public string Fingerprint = "";
    public int Reward;
    public bool Success;
    public string Error = "";
    public long World;
    public List<BountyRecord> Records = new();
    public BountyTransaction? Transaction;
}

internal static class BountyService
{
    private const string RequestRpc = "Praetoris_BountyRequest_v1";
    private const string ReplyRpc = "Praetoris_BountyReply_v1";
    private static BountyLedger? _ledger;
    private static long _world;
    private static readonly Dictionary<long, float> LastRequest = new();
    private static readonly Dictionary<long, Dictionary<string, BountyMessage>> PendingLifecycle = new();
    internal static readonly Dictionary<string, BountyRecord> ClientRecords = new();
    internal static event Action<BountyMessage>? Received;
    internal static long ClientWorld;

    internal static void Register(ZRoutedRpc rpc)
    {
        _ledger = null;
        LastRequest.Clear();
        PendingLifecycle.Clear();
        ClientRecords.Clear();
        rpc.Register<ZPackage>(RequestRpc, OnRequest);
        rpc.Register<ZPackage>(ReplyRpc, OnReply);
    }

    internal static ZDOID ParseId(string value)
    {
        string[] pieces = value.Split(':');
        return pieces.Length == 2 && long.TryParse(pieces[0], out long user) && uint.TryParse(pieces[1], out uint id)
            ? new ZDOID(user, id) : ZDOID.None;
    }

    internal static bool FromServer(long sender) => ZNet.instance != null &&
        sender == (ZNet.instance.IsServer() ? ZNet.GetUID() : ZRoutedRpc.instance.GetServerPeerID());

    internal static void Request(BountyMessage message)
    {
        if (ZRoutedRpc.instance == null || (Player.m_localPlayer == null && message.Action != "stoneReady")) return;
        Send(ZNet.instance.IsServer() ? ZNet.GetUID() : ZRoutedRpc.instance.GetServerPeerID(), RequestRpc, message);
    }

    private static void Send(long peer, string rpc, BountyMessage message)
    {
        ZPackage package = new ZPackage();
        package.Write(JsonConvert.SerializeObject(message));
        if (peer == ZNet.GetUID())
        {
            package.SetPos(0);
            if (rpc == RequestRpc) OnRequest(peer, package); else OnReply(peer, package);
        }
        else ZRoutedRpc.instance.InvokeRoutedRPC(peer, rpc, package);
    }

    private static BountyLedger Ledger
    {
        get
        {
            long world = ZNet.instance.GetWorldUID();
            if (_ledger == null || _world != world)
            {
                _world = world;
                _ledger = new BountyLedger(world, Path.Combine(Utils.GetSaveDataPath(FileHelpers.FileSource.Local), "PraetorisBodyBounties"));
                RelinkStones(_ledger);
            }
            return _ledger;
        }
    }

    // Valheim renumbers object IDs when it loads a world, so a saved tombstone ID is stale after a restart.
    // Re-link each open bounty to its tombstone by owner and time of death, before anything looks it up.
    private static void RelinkStones(BountyLedger ledger)
    {
        List<ZDO> stones = new List<ZDO>();
        int index = 0;
        while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative("Player_tombstone", stones, ref index)) { }
        Dictionary<string, string> moved = new Dictionary<string, string>();
        foreach (BountyRecord record in ledger.Data.Bounties.Values)
        {
            if (record.State == BountyState.Cancelled || record.State == BountyState.Reclaimed) continue;
            ZDO? current = ZDOMan.instance.GetZDO(ParseId(record.Stone));
            if (current != null && IsStone(current, record)) continue;
            ZDO? match = stones.FirstOrDefault(stone => IsStone(stone, record));
            if (match != null) moved[record.Id] = match.m_uid.ToString();
        }
        if (moved.Count == 0) return;
        ledger.Commit(data => { foreach (KeyValuePair<string, string> stone in moved) data.Bounties[stone.Key].Stone = stone.Value; });
        PraetorisClientPlugin.Log.LogInfo($"Re-linked {moved.Count} body retrieval bounties to their tombstones after the world load.");
    }

    private static bool IsStone(ZDO stone, BountyRecord record) =>
        stone.GetPrefab() == "Player_tombstone".GetStableHashCode() && stone.GetLong(ZDOVars.s_owner) == record.Owner &&
        (record.TimeOfDeath != 0 ? stone.GetLong(ZDOVars.s_timeOfDeath) == record.TimeOfDeath
            : global::Utils.DistanceXZ(stone.GetPosition(), new Vector3(record.X, 0, record.Z)) < 1f);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static bool Near(ZNetPeer? peer, ZDO target, float distance)
    {
        ZDO? character = peer != null ? ZDOMan.instance.GetZDO(peer.m_characterID) : Player.m_localPlayer?.m_nview.GetZDO();
        return character != null && Vector3.Distance(character.GetPosition(), target.GetPosition()) <= distance;
    }

    private static ZDO Stone(string id)
    {
        ZDO stone = ZDOMan.instance.GetZDO(ParseId(id));
        Require(stone != null && stone.GetPrefab() == "Player_tombstone".GetStableHashCode(), "That tombstone no longer exists.");
        return stone!;
    }

    private static void RequirePort(string id, ZNetPeer? peer)
    {
        ZDO port = ZDOMan.instance.GetZDO(ParseId(id));
        Require(port != null && port.GetPrefab() == BountyFeature.NpcAnchorPrefab.GetStableHashCode() &&
            Near(peer, port, 8f), "Use a nearby bounty NPC.");
    }

    internal static string Contents(ZDO stone)
    {
        byte[] bytes = stone.GetByteArray(ZDOVars.s_items);
        return bytes == null ? "" : Convert.ToBase64String(bytes);
    }

    private static BountyRecord Describe(ZDO stone, long player)
    {
        string contents = Contents(stone);
        Require(!string.IsNullOrEmpty(contents), "The tombstone inventory is not available yet.");
        Inventory inventory = BountyInventory.Load(contents);
        Require(inventory.NrOfItems() > 0, "The tombstone is empty.");
        Vector3 position = stone.GetPosition();
        return new BountyRecord { Stone = stone.m_uid.ToString(), Owner = stone.GetLong(ZDOVars.s_owner),
            OwnerName = stone.GetString(ZDOVars.s_ownerName), X = position.x, Y = position.y, Z = position.z,
            TimeOfDeath = stone.GetLong(ZDOVars.s_timeOfDeath),
            Weight = inventory.GetTotalWeight(), Contents = contents, Fingerprint = BountyInventory.Hash(contents) };
    }

    private static bool Active(BountyRecord bounty) => bounty.State != BountyState.Cancelled && bounty.State != BountyState.Reclaimed;

    internal static BountyRecord? ServerRecord(string stone) => Ledger.Data.Bounties.Values.FirstOrDefault(bounty => bounty.Stone == stone && bounty.State != BountyState.Cancelled);

    private static void OnRequest(long sender, ZPackage package)
    {
        if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
        string action = "";
        try
        {
            Require(package.Size() <= 65536, "Request too large.");
            BountyMessage message = JsonConvert.DeserializeObject<BountyMessage>(package.ReadString()) ?? throw new InvalidOperationException("Invalid request.");
            action = message.Action;
            if (!PlayerResolver.TryGetSenderPlayerId(sender, out _, out _) &&
                (message.Action == "join" || message.Action == "stoneLoaded") &&
                PlayerResolver.FindPeerBySender(sender)?.IsReady() == true)
            {
                if (!PendingLifecycle.TryGetValue(sender, out Dictionary<string, BountyMessage> pending))
                    PendingLifecycle[sender] = pending = new Dictionary<string, BountyMessage>();
                if (pending.Count < 256) pending[message.Action + message.Stone] = message;
                return;
            }
            Require(PlayerResolver.TryGetSenderPlayerId(sender, out _, out _) ||
                message.Action == "stoneReady" && sender == ZNet.GetUID(), "Player identity is unavailable.");
            PlayerResolver.TryGetSenderPlayerId(sender, out long player, out ZNetPeer? peer);
            // Receipts are not rate-limited: they finish an already durable operation.
            if (message.Action != "receipt" && message.Action != "stoneReady" && message.Action != "stoneLoaded" && message.Action != "ownerRecovered" && message.Action != "join")
            {
                if (LastRequest.TryGetValue(sender, out float last) && Time.realtimeSinceStartup - last < 0.1f) return;
                LastRequest[sender] = Time.realtimeSinceStartup;
            }
            BountyLedger ledger = Ledger;
            switch (message.Action)
            {
                case "stoneLoaded":
                    ZDO loaded = Stone(message.Stone);
                    Require(Near(peer, loaded, 150f), "Tombstone is outside the loaded area.");
                    BountyRecord? known = ledger.Data.Bounties.Values.FirstOrDefault(bounty => bounty.Stone == message.Stone && bounty.State != BountyState.Cancelled);
                    bool consumed = known != null && (known.State == BountyState.InTransit || known.State == BountyState.Delivered || known.State == BountyState.Reclaimed);
                    Send(sender, ReplyRpc, new BountyMessage { Action = "stoneStatus", Stone = message.Stone,
                        Success = consumed, Error = known?.State == BountyState.Collecting ? "locked" : "", Records = known == null ? new List<BountyRecord>() : new List<BountyRecord> { Public(known) } });
                    return;
                case "ownerRecovered":
                    BountyRecord? recovered = ledger.Data.Bounties.Values.FirstOrDefault(bounty => bounty.Stone == message.Stone &&
                        bounty.Owner == player && (bounty.State == BountyState.Posted || bounty.State == BountyState.Accepted));
                    if (recovered != null && recovered.Pending == "") StartTransaction(sender, recovered, player, "refund", recovered.Reward);
                    return;
                case "list":
                case "join":
                    RefreshAvailable();
                    Recover(sender, player);
                    SendList(sender, player);
                    return;
                case "stones":
                    RequirePort(message.Port, peer);
                    RefreshAvailable();
                    List<ZDO> stones = new List<ZDO>();
                    int index = 0;
                    while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative("Player_tombstone", stones, ref index)) { }
                    List<BountyRecord> previews = new List<BountyRecord>();
                    foreach (ZDO stone in stones.Where(stone => stone.GetLong(ZDOVars.s_owner) == player))
                    {
                        if (ledger.Data.Bounties.Values.Any(bounty => bounty.Stone == stone.m_uid.ToString() && Active(bounty))) continue;
                        try { previews.Add(Describe(stone, player)); }
                        catch (InvalidOperationException) { }
                    }
                    Send(sender, ReplyRpc, new BountyMessage { Action = "stones", Records = previews, World = _world });
                    return;
                case "post":
                    RequirePort(message.Port, peer);
                    Require(message.Reward > 0, "Enter a positive whole-number reward in Coins.");
                    ZDO target = Stone(message.Stone);
                    Require(target.GetLong(ZDOVars.s_owner) == player, "Only the owner can post this tombstone.");
                    Require(!ledger.Data.Bounties.Values.Any(bounty => bounty.Stone == message.Stone && Active(bounty)), "This tombstone already has a bounty.");
                    BountyRecord posted = Describe(target, player);
                    Require(posted.Fingerprint == message.Fingerprint, "The contents changed. Select the tombstone again.");
                    posted.Reward = message.Reward;
                    posted.PostedUtc = DateTime.UtcNow.Ticks;
                    posted.State = BountyState.Reserving;
                    BountyTransaction reservation = new BountyTransaction { Bounty = posted.Id, Player = player, Kind = "reserve", Coins = posted.Reward };
                    posted.Pending = reservation.Id;
                    ledger.Commit(data => { data.Bounties.Add(posted.Id, posted); data.Transactions.Add(reservation.Id, reservation); });
                    Offer(sender, reservation);
                    return;
                case "receipt":
                    Complete(sender, player, message);
                    return;
            }
            Require(ledger.Data.Bounties.TryGetValue(message.Id, out BountyRecord record), "This bounty no longer exists.");
            switch (message.Action)
            {
                case "accept":
                    Require(record.Owner != player, "You cannot accept your own bounty.");
                    Require(record.State == BountyState.Posted || record.State == BountyState.Accepted || record.State == BountyState.InTransit,
                        "This bounty is not available.");
                    ledger.Commit(data => { BountyRecord bounty = data.Bounties[record.Id]; bounty.Accepted.Add(player);
                        if (bounty.State == BountyState.Posted) bounty.State = BountyState.Accepted; });
                    Changed();
                    break;
                case "cancel":
                    RequirePort(message.Port, peer);
                    Require(record.Owner == player, "Only the owner can cancel this bounty.");
                    Require(record.State == BountyState.Posted || record.State == BountyState.Accepted, "Pickup has already started.");
                    StartTransaction(sender, record, player, "refund", record.Reward);
                    break;
                case "inspect":
                    ZDO inspected = Stone(record.Stone);
                    Require(Near(peer, inspected, 5f), "Move closer to the tombstone.");
                    Require(record.Accepted.Contains(player) && record.Owner != player, "Accept this bounty first.");
                    Require(record.State == BountyState.Accepted, "This bounty cannot be collected.");
                    BountyRecord current = Describe(inspected, player);
                    current.Id = record.Id; current.Reward = record.Reward; current.Contents = "";
                    Send(sender, ReplyRpc, new BountyMessage { Action = "confirm", Records = new List<BountyRecord> { current }, World = _world });
                    break;
                case "collect":
                    ZDO collecting = Stone(record.Stone);
                    Require(Near(peer, collecting, 5f), "Move closer to the tombstone.");
                    Require(record.Accepted.Contains(player) && record.Owner != player && record.State == BountyState.Accepted && record.Pending == "", "This bounty cannot be collected.");
                    BountyRecord actual = Describe(collecting, player);
                    Require(actual.Fingerprint == message.Fingerprint, "The contents changed. Inspect the tombstone again.");
                    // The network owner locks its live Container before issuing a parcel.
                    ledger.Commit(data => { BountyRecord bounty = data.Bounties[record.Id]; bounty.State = BountyState.Collecting;
                        bounty.Collector = player; bounty.Contents = actual.Contents; bounty.Weight = actual.Weight; bounty.Fingerprint = actual.Fingerprint; });
                    TombstoneBounty.RequestLock(collecting, record.Id, actual.Fingerprint);
                    break;
                case "stoneReady":
                    ZDO ready = Stone(record.Stone);
                    Require(ready.GetOwner() == sender && record.State == BountyState.Collecting, "Unexpected tombstone owner response.");
                    if (!message.Success || message.Fingerprint != record.Fingerprint)
                    {
                        ledger.Commit(data => data.Bounties[record.Id].State = BountyState.Accepted);
                        TombstoneBounty.ReleaseLock(ready, false);
                        PraetorisClientPlugin.Log.LogWarning($"Bounty pickup refused by the tombstone owner: success={message.Success}, fingerprint matches={message.Fingerprint == record.Fingerprint}.");
                        long retriever = PeerFor(record.Collector);
                        if (retriever != 0) Send(retriever, ReplyRpc, new BountyMessage { Action = "error",
                            Error = "Pickup failed: the tombstone is open or its contents changed. Inspect it again." });
                        Changed();
                        break;
                    }
                    Require(record.Pending == "", "Pickup already has a transaction.");
                    long collectorPeer = PeerFor(record.Collector);
                    Require(collectorPeer != 0, "Retriever disconnected. Pickup will resume when they reconnect.");
                    StartTransaction(collectorPeer, record, record.Collector, "parcel", 0);
                    break;
                case "deliver":
                    RequirePort(message.Port, peer);
                    Require(record.State == BountyState.InTransit && record.Accepted.Contains(player) && record.Owner != player,
                        "Accept this bounty and carry its parcel to deliver it.");
                    StartTransaction(sender, record, player, "deliver", record.Reward);
                    break;
                case "reclaim":
                    RequirePort(message.Port, peer);
                    Require(record.State == BountyState.Delivered && record.Owner == player, "These contents are not available to you.");
                    StartTransaction(sender, record, player, "reclaim", 0);
                    break;
                default: throw new InvalidOperationException("Unknown bounty action.");
            }
        }
        catch (Exception error)
        {
            PraetorisClientPlugin.Log.LogWarning($"Bounty request rejected ({action}): {error.Message}");
            // Clients send these lifecycle requests on their own, for example when a distant tombstone loads.
            // Their failures are not player actions; showing them would print unexplained center-screen messages.
            if (action != "stoneLoaded" && action != "ownerRecovered" && action != "join" && action != "stoneReady")
                Send(sender, ReplyRpc, new BountyMessage { Action = "error", Error = error.Message });
        }
    }

    private static long PeerFor(long player)
    {
        foreach (ZNetPeer peer in ZNet.instance.GetConnectedPeers())
            if (PlayerResolver.TryGetPeerPlayerId(peer, out long id) && id == player) return peer.m_uid;
        return Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == player ? ZNet.GetUID() : 0;
    }

    private static void RefreshAvailable()
    {
        Dictionary<string, BountyRecord?> changed = new Dictionary<string, BountyRecord?>();
        foreach (BountyRecord record in Ledger.Data.Bounties.Values)
        {
            if (record.Pending != "" || (record.State != BountyState.Posted && record.State != BountyState.Accepted)) continue;
            ZDO stone = ZDOMan.instance.GetZDO(ParseId(record.Stone));
            if (stone == null) { changed[record.Id] = null; continue; }
            string contents = Contents(stone);
            if (string.IsNullOrEmpty(contents) || BountyInventory.Hash(contents) == record.Fingerprint) continue;
            if (BountyInventory.Load(contents).NrOfItems() == 0) changed[record.Id] = null;
            else changed[record.Id] = Describe(stone, record.Owner);
        }
        if (changed.Count == 0) return;
        Ledger.Commit(data =>
        {
            foreach (KeyValuePair<string, BountyRecord?> change in changed)
            {
                BountyRecord record = data.Bounties[change.Key];
                if (change.Value == null) record.State = BountyState.Refunding;
                else
                {
                    record.Contents = change.Value.Contents;
                    record.Weight = change.Value.Weight;
                    record.Fingerprint = change.Value.Fingerprint;
                }
            }
        });
    }

    private static void StartTransaction(long sender, BountyRecord bounty, long player, string kind, int coins)
    {
        Require(bounty.Pending == "", "A transaction is already pending. Reopen the bounty page to resume it.");
        BountyTransaction transaction = new BountyTransaction { Bounty = bounty.Id, Player = player, Kind = kind,
            Coins = coins, Items = kind == "reclaim" ? bounty.Contents : "" };
        Ledger.Commit(data => { data.Transactions.Add(transaction.Id, transaction); data.Bounties[bounty.Id].Pending = transaction.Id;
            if (kind == "refund") data.Bounties[bounty.Id].State = BountyState.Refunding; });
        Offer(sender, transaction);
    }

    private static void Offer(long sender, BountyTransaction transaction)
    {
        BountyRecord record = Public(Ledger.Data.Bounties[transaction.Bounty]);
        Send(sender, ReplyRpc, new BountyMessage { Action = "transaction", Transaction = transaction,
            Records = new List<BountyRecord> { record }, World = _world });
    }

    private static void Complete(long sender, long player, BountyMessage message)
    {
        Require(Ledger.Data.Transactions.TryGetValue(message.Id, out BountyTransaction transaction) && transaction.Player == player,
            "Unknown inventory receipt.");
        if (transaction.Completed) { SendList(sender, player); return; }
        BountyRecord record = Ledger.Data.Bounties[transaction.Bounty];
        Require(record.Pending == transaction.Id, "Transaction is not pending.");
        Ledger.Commit(data =>
        {
            BountyTransaction operation = data.Transactions[transaction.Id];
            operation.Completed = true; operation.Failed = !message.Success;
            operation.Items = "";
            BountyRecord bounty = data.Bounties[transaction.Bounty];
            bounty.Pending = "";
            if (!message.Success)
            {
                if (operation.Kind == "reserve") bounty.State = BountyState.Cancelled;
                if (operation.Kind == "parcel") bounty.State = BountyState.Accepted;
                return;
            }
            switch (operation.Kind)
            {
                case "reserve": bounty.State = BountyState.Posted; break;
                case "refund": bounty.State = BountyState.Cancelled; break;
                case "parcel": bounty.State = BountyState.InTransit; break;
                case "deliver": bounty.State = BountyState.Delivered; break;
                case "reclaim": bounty.State = BountyState.Reclaimed; bounty.Contents = ""; break;
            }
        });
        if (transaction.Kind == "parcel")
        {
            ZDO stone = ZDOMan.instance.GetZDO(ParseId(record.Stone));
            if (stone != null) TombstoneBounty.ReleaseLock(stone, message.Success);
        }
        if (transaction.Kind == "reserve" && message.Success)
            foreach (ZNetPeer peer in ZNet.instance.GetConnectedPeers())
                Send(peer.m_uid, ReplyRpc, new BountyMessage { Action = "announcement", Error = record.OwnerName + " posted a body retrieval bounty for " + record.Reward + " Coins. Open Body retrieval bounties in the compendium." });
        Changed();
    }

    private static void Recover(long sender, long player)
    {
        foreach (BountyRecord interrupted in Ledger.Data.Bounties.Values.Where(bounty => bounty.State == BountyState.Collecting &&
            bounty.Pending == "" && PeerFor(bounty.Collector) == 0).ToArray())
        {
            Ledger.Commit(data => data.Bounties[interrupted.Id].State = BountyState.Accepted);
            ZDO abandoned = ZDOMan.instance.GetZDO(ParseId(interrupted.Stone));
            if (abandoned != null) TombstoneBounty.ReleaseLock(abandoned, false);
        }
        foreach (BountyRecord bounty in Ledger.Data.Bounties.Values.Where(bounty => bounty.Owner == player && bounty.Pending == "" &&
            (bounty.State == BountyState.Posted || bounty.State == BountyState.Accepted || bounty.State == BountyState.Refunding)).ToArray())
        {
            ZDO stone = ZDOMan.instance.GetZDO(ParseId(bounty.Stone));
            if (bounty.State == BountyState.Refunding || stone == null ||
                !string.IsNullOrEmpty(Contents(stone)) && BountyInventory.Load(Contents(stone)).NrOfItems() == 0)
                StartTransaction(sender, bounty, player, "refund", bounty.Reward);
        }
        foreach (BountyTransaction pending in Ledger.Data.Transactions.Values.Where(transaction => !transaction.Completed && transaction.Player == player).ToArray())
            Offer(sender, pending);
        foreach (BountyRecord bounty in Ledger.Data.Bounties.Values.Where(bounty => bounty.State == BountyState.Collecting && bounty.Pending == "" && bounty.Collector == player).ToArray())
        {
            ZDO stone = ZDOMan.instance.GetZDO(ParseId(bounty.Stone));
            if (stone != null) TombstoneBounty.RequestLock(stone, bounty.Id, bounty.Fingerprint);
        }
    }

    private static BountyRecord Public(BountyRecord record)
    {
        return new BountyRecord
        {
            Id = record.Id, Stone = record.Stone, Owner = record.Owner, OwnerName = record.OwnerName,
            X = record.X, Y = record.Y, Z = record.Z, Reward = record.Reward, Weight = record.Weight,
            PostedUtc = record.PostedUtc, State = record.State, Accepted = new HashSet<long>(record.Accepted),
            Fingerprint = record.Fingerprint, Parcel = record.Parcel, Collector = record.Collector, Pending = record.Pending
        };
    }

    internal static void CharacterDataReceived()
    {
        if (PendingLifecycle.Count == 0 || ZNet.instance == null || !ZNet.instance.IsServer()) return;
        foreach (long sender in PendingLifecycle.Keys.ToArray())
        {
            if (PlayerResolver.FindPeerBySender(sender) == null) { PendingLifecycle.Remove(sender); continue; }
            if (!PlayerResolver.TryGetSenderPlayerId(sender, out _, out _)) continue;
            BountyMessage[] messages = PendingLifecycle[sender].Values.ToArray();
            PendingLifecycle.Remove(sender);
            foreach (BountyMessage message in messages)
            {
                ZPackage package = new ZPackage();
                package.Write(JsonConvert.SerializeObject(message));
                package.SetPos(0);
                OnRequest(sender, package);
            }
        }
    }

    private static void SendList(long sender, long player)
    {
        List<BountyRecord> records = Ledger.Data.Bounties.Values.Where(bounty => Active(bounty) || bounty.Owner == player || bounty.Accepted.Contains(player)).Select(Public).ToList();
        Send(sender, ReplyRpc, new BountyMessage { Action = "list", Records = records, World = _world });
    }

    private static void Changed()
    {
        foreach (ZNetPeer peer in ZNet.instance.GetConnectedPeers())
            if (PlayerResolver.TryGetPeerPlayerId(peer, out long player)) SendList(peer.m_uid, player);
        if (Player.m_localPlayer != null) SendList(ZNet.GetUID(), Player.m_localPlayer.GetPlayerID());
    }

    private static void OnReply(long sender, ZPackage package)
    {
        if (!FromServer(sender)) return;
        try
        {
            BountyMessage message = JsonConvert.DeserializeObject<BountyMessage>(package.ReadString())!;
            if (message.World != 0) ClientWorld = message.World;
            if (message.Action == "list")
            {
                ClientRecords.Clear();
                foreach (BountyRecord record in message.Records) ClientRecords[record.Id] = record;
                TombstoneBounty.RefreshLoaded();
            }
            if (message.Action == "stoneStatus")
            {
                foreach (BountyRecord record in message.Records) ClientRecords[record.Id] = record;
                TombstoneBounty.ApplyStatus(message.Stone, message.Error == "locked", message.Success);
            }
            if (message.Action == "transaction") BountyClientTransactions.Apply(message);
            if (message.Action == "announcement")
                Chat.instance?.AddString("<color=#EBC66B>" + System.Security.SecurityElement.Escape(message.Error) + "</color>");
            Received?.Invoke(message);
        }
        catch (Exception error) { PraetorisClientPlugin.Log.LogError("Bounty response failed: " + error.Message); }
    }
}

[HarmonyPatch(typeof(ZNet), "RPC_CharacterID")]
internal static class BountyCharacterIdentity
{
    private static void Postfix() => BountyService.CharacterDataReceived();
}

[HarmonyPatch(typeof(ZDOMan), "RPC_ZDOData")]
internal static class BountyCharacterData
{
    private static void Postfix() => BountyService.CharacterDataReceived();
}
