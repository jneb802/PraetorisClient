using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace PraetorisClient.BodyBounties;

internal enum BountyState { Reserving, Posted, Accepted, Collecting, InTransit, Delivered, Reclaimed, Cancelled, Refunding }

internal sealed class BountyRecord
{
    public string Id = Guid.NewGuid().ToString("N");
    public string Stone = "";
    public long Owner;
    public string OwnerName = "";
    public float X, Y, Z;
    public int Reward;
    public float Weight;
    public long PostedUtc;
    public BountyState State;
    public HashSet<long> Accepted = new();
    public string Contents = "";
    public string Fingerprint = "";
    public string Parcel = Guid.NewGuid().ToString("N");
    public long Collector;
    public string Pending = "";
}

internal sealed class BountyTransaction
{
    public string Id = Guid.NewGuid().ToString("N");
    public string Bounty = "";
    public long Player;
    public string Kind = "";
    public string Items = "";
    public int Coins;
    public bool Completed;
    public bool Failed;
}

internal sealed class BountyLedgerData
{
    public int Version = 1;
    public long World;
    public Dictionary<string, BountyRecord> Bounties = new();
    public Dictionary<string, BountyTransaction> Transactions = new();
}

// A failed read or write disables mutations. Never replace an unreadable ledger
// with an empty one: that would permit duplicate rewards and destroy escrow.
internal sealed class BountyLedger
{
    private readonly string _path;
    internal BountyLedgerData Data { get; private set; }
    internal bool Faulted { get; private set; }

    internal BountyLedger(long world, string directory)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, world + ".json");
        Data = File.Exists(_path)
            ? JsonConvert.DeserializeObject<BountyLedgerData>(File.ReadAllText(_path))
                ?? throw new InvalidDataException("The bounty ledger is empty.")
            : new BountyLedgerData { World = world };
        if (Data.Version != 1 || Data.World != world)
            throw new InvalidDataException("Unsupported bounty ledger version or world.");
    }

    internal void Commit(Action<BountyLedgerData> change)
    {
        if (Faulted) throw new IOException("Bounty storage is unavailable. Restart after repairing storage.");
        // Apply to a copy so an exception cannot expose an uncommitted mutation.
        BountyLedgerData next = JsonConvert.DeserializeObject<BountyLedgerData>(JsonConvert.SerializeObject(Data))!;
        change(next);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(next));
        string temporary = _path + ".pending";
        try
        {
            using (FileStream stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(_path)) File.Replace(temporary, _path, _path + ".previous");
            else File.Move(temporary, _path);
            Data = next;
        }
        catch
        {
            Faulted = true;
            throw;
        }
    }
}
