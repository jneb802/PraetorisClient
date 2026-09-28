using System;
using System.IO;
using PraetorisClient.CommunityChestFeature;

string root = Path.Combine(Path.GetTempPath(), "community-chest-" + Guid.NewGuid().ToString("N"));
int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    checks++;
    Console.WriteLine("PASS " + name);
}
void Reject(Action action, string name)
{
    bool rejected = false;
    try { action(); } catch (InvalidOperationException) { rejected = true; } catch (IOException) { rejected = true; }
    Check(rejected, name);
}
try
{
    string alice = CommunityChestStore.AccountPath(root, 123, "account-a");
    string bob = CommunityChestStore.AccountPath(root, 123, "account-b");
    string otherWorld = CommunityChestStore.AccountPath(root, 456, "account-a");
    CommunityChestRecord record = CommunityChestStore.Read(alice);
    CommunityChestStore.Prepare(record, "deposit", 42, 250, 0);
    CommunityChestStore.Write(alice, record);
    record = CommunityChestStore.Read(alice);
    Check(record.Balance == 0 && record.PendingAmount == 250, "restart preserves pending deposit without crediting it");
    Reject(() => CommunityChestStore.Prepare(record, "other", 42, 100, 0), "second transfer cannot replace pending transfer");
    Reject(() => CommunityChestStore.Commit(record, "deposit", 43), "different character cannot finish pending transfer");
    CommunityChestStore.Commit(record, "deposit", 42);
    CommunityChestStore.Write(alice, record);
    record = CommunityChestStore.Read(alice);
    CommunityChestStore.Commit(record, "deposit", 42);
    Check(record.Balance == 250 && record.Revision == 1, "repeated commit credits once after restart");
    Check(CommunityChestStore.Read(bob).Balance == 0, "accounts have separate balances");
    Check(CommunityChestStore.Read(otherWorld).Balance == 0, "worlds have separate balances");
    Reject(() => CommunityChestStore.Prepare(record, "old", 42, 1, 0), "stale revision is rejected");
    Reject(() => CommunityChestStore.Prepare(record, "deposit", 42, 250, 1), "completed transfer cannot be prepared again");
    Reject(() => CommunityChestStore.Prepare(record, "large", 42, int.MaxValue, 1), "deposit overflow is rejected");
    Reject(() => CommunityChestStore.Prepare(record, "negative", 42, int.MinValue, 1), "withdrawal overflow is rejected");
    Reject(() => CommunityChestStore.Prepare(record, "empty", 42, -251, 1), "overdraft is rejected");
    Reject(() => CommunityChestStore.Prepare(record, "zero", 42, 0, 1), "zero transfer is rejected");
    CommunityChestStore.Prepare(record, "withdraw", 42, -150, 1);
    CommunityChestStore.Write(alice, record);
    record = CommunityChestStore.Read(alice);
    Check(record.Balance == 250 && record.PendingAmount == -150, "restart preserves pending withdrawal");
    CommunityChestStore.Commit(record, "withdraw", 42);
    CommunityChestStore.Write(alice, record);
    Check(CommunityChestStore.Read(alice).Balance == 100, "withdrawal persists");
    Check(File.Exists(alice + ".bak"), "previous record retained as operator backup");
    File.WriteAllText(alice, "{invalid");
    bool corruptRejected = false;
    try { CommunityChestStore.Read(alice); } catch (Newtonsoft.Json.JsonException) { corruptRejected = true; }
    Check(corruptRejected, "corrupt record does not reset balance or restore stale backup");
    File.WriteAllText(alice, "{}");
    bool incompleteRejected = false;
    try { CommunityChestStore.Read(alice); } catch (Newtonsoft.Json.JsonException) { incompleteRejected = true; }
    Check(incompleteRejected, "missing fields cannot silently reset an account balance");
    File.Delete(alice);
    Reject(() => CommunityChestStore.Read(alice), "missing primary with existing backup requires operator recovery");
    Console.WriteLine(checks + " checks passed.");
}
finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
