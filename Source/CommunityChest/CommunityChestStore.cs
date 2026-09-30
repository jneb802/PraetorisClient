using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace PraetorisClient.CommunityChestFeature
{
    // One durable record per account and world. Never recover automatically from an
    // older backup: that could restore coins which have already been withdrawn.
    internal sealed class CommunityChestRecord
    {
        [JsonProperty(Required = Required.Always)] public int Format = 1;
        [JsonProperty(Required = Required.Always)] public int Balance;
        [JsonProperty(Required = Required.Always)] public long Revision;
        [JsonProperty(Required = Required.Always)] public string LastTransaction = "";
        [JsonProperty(Required = Required.Always)] public string PendingTransaction = "";
        [JsonProperty(Required = Required.Always)] public long PendingCharacter;
        [JsonProperty(Required = Required.Always)] public int PendingAmount;
    }

    internal static class CommunityChestStore
    {
        internal const int Capacity = 20000;

        internal static string AccountPath(string root, long world, string account)
        {
            using SHA256 hash = SHA256.Create();
            string key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(account))).Replace("-", "");
            return Path.Combine(root, world.ToString(System.Globalization.CultureInfo.InvariantCulture), key + ".json");
        }

        internal static CommunityChestRecord Read(string path)
        {
            if (!File.Exists(path))
            {
                if (File.Exists(path + ".bak")) throw new IOException("Primary chest record is missing; operator recovery required.");
                return new CommunityChestRecord();
            }
            CommunityChestRecord record = JsonConvert.DeserializeObject<CommunityChestRecord>(File.ReadAllText(path))
                ?? throw new IOException("Chest record is empty.");
            if (record.Format != 1 || record.Balance < 0 || record.Balance > Capacity || record.Revision < 0 ||
                record.PendingTransaction == null || record.LastTransaction == null ||
                (record.PendingTransaction.Length != 0 && (record.PendingCharacter == 0 || record.PendingAmount == 0 ||
                 (long)record.Balance + record.PendingAmount < 0 || (long)record.Balance + record.PendingAmount > Capacity)))
                throw new IOException("Chest record is invalid; operator recovery required.");
            return record;
        }

        internal static void Write(string path, CommunityChestRecord record)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(record, Formatting.Indented));
            string temporary = path + ".new";
            using (FileStream stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }

        internal static void Prepare(CommunityChestRecord record, string transaction, long character, int amount, long revision)
        {
            if (record.PendingTransaction.Length != 0) throw new InvalidOperationException("Finish the pending transfer with its original character first.");
            if (revision != record.Revision) throw new InvalidOperationException("Chest changed. Open it again.");
            if (transaction == record.LastTransaction) throw new InvalidOperationException("Transfer already completed. Open the chest again.");
            if (amount == 0 || (long)record.Balance + amount < 0 || (long)record.Balance + amount > Capacity)
                throw new InvalidOperationException("Not enough coins or chest space.");
            record.PendingTransaction = transaction;
            record.PendingCharacter = character;
            record.PendingAmount = amount;
        }

        internal static void Commit(CommunityChestRecord record, string transaction, long character)
        {
            if (record.LastTransaction == transaction) return;
            if (record.PendingTransaction != transaction || record.PendingCharacter != character)
                throw new InvalidOperationException("Transfer does not match this character.");
            record.Balance += record.PendingAmount;
            record.Revision++;
            record.LastTransaction = transaction;
            record.PendingTransaction = "";
            record.PendingAmount = 0;
            record.PendingCharacter = 0;
        }
    }
}
