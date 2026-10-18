using System.Collections;
using System.Reflection;

// Exercise the actual candidate and Epic Loot IL without starting Unity. No copied compatibility implementation.
if (args.Length < 3 || args.Length > 4)
    throw new ArgumentException("Usage: <PraetorisClient.dll> <EpicLoot.dll> <Valheim install> [native]");
string game = Path.GetFullPath(args[2]);
string[] search =
{
    Path.Combine(game, "BepInEx/core"),
    Path.Combine(game, "Valheim.app/Contents/Resources/Data/Managed"),
    Path.Combine(game, "valheim_Data/Managed"),
    Path.GetDirectoryName(Path.GetFullPath(args[0]))!,
    Path.GetDirectoryName(Path.GetFullPath(args[1]))!
};
AppDomain.CurrentDomain.AssemblyResolve += (_, request) =>
{
    AssemblyName name = new(request.Name);
    foreach (string directory in search)
    {
        string path = Path.Combine(directory, name.Name + ".dll");
        if (File.Exists(path)) return Assembly.LoadFrom(path);
    }
    string? plugin = Directory.EnumerateFiles(Path.Combine(game, "BepInEx/plugins"), name.Name + ".dll", SearchOption.AllDirectories).FirstOrDefault();
    return plugin == null ? null : Assembly.LoadFrom(plugin);
};
Assembly epic = Assembly.LoadFrom(Path.GetFullPath(args[1]));
Assembly client = Assembly.LoadFrom(Path.GetFullPath(args[0]));
Type compatibility = client.GetType("PraetorisClient.EndgameCompatibility", true)!;
const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
object? Call(string name, params object?[] values) => compatibility.GetMethod(name, flags)!.Invoke(null, values);
void Require(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    System.Console.WriteLine("PASS: " + name);
}
Require((bool)Call("IsTabletName", "ve_tablet")!, "legacy tablet");
for (int tier = 1; tier <= 15; tier++)
    Require((bool)Call("IsTabletName", "ve_tablet_tier" + tier.ToString("00"))!, "tablet tier " + tier);
foreach (string? name in new[] { null, "", "SwordIron", "ve_tablet_tier00", "ve_tablet_tier16", "ve_tablet_tier1", "ve_tablet_tier01(Clone)", "ve_tablet_tier+1" })
    Require(!(bool)Call("IsTabletName", new object?[] { name })!, "reject unrelated/invalid name " + (name ?? "null"));
bool native = args.Length == 4 && args[3] == "native";
Require((bool)Call("HasNativeSupport", epic)! == native, native ? "upstream build recognized without item patches" : "legacy build does not claim native flags");
if (native) return;

Assembly harmony = Assembly.LoadFrom(Path.Combine(search[0], "0Harmony.dll"));
Type processor = harmony.GetType("HarmonyLib.PatchProcessor", true)!;
MethodInfo read = processor.GetMethods().Single(m => m.Name == "GetOriginalInstructions" && m.GetParameters().Length == 2 && !m.GetParameters()[1].ParameterType.IsByRef);
Type magic = epic.GetType("EpicLoot.MagicItem", true)!;
FieldInfo socketCount = magic.GetField("SocketCount")!;
Type rarity = epic.GetType("EpicLoot.ItemRarity", true)!;
Type item = epic.GetType("EpicLoot.EpicLoot", true)!.GetMethod("IsAllowedMagicItemType")!.GetParameters()[0].ParameterType;
Type roller = epic.GetType("EpicLoot.LootRoller", true)!;
MethodInfo weights = roller.GetMethod("GetSocketCountsPerRarity")!;
compatibility.GetField("socketCount", flags)!.SetValue(null, socketCount);
compatibility.GetField("socketWeights", flags)!.SetValue(null, weights);

(MethodInfo Method, string Transform, string Helper, int Count)[] cases =
{
    (roller.GetMethod("RollMagicItem", new[] { rarity, item, typeof(float), typeof(float) })!, "PreventRolledSockets", "FilterSocketCount", 1),
    (epic.GetType("EpicLoot.CraftingV2.EnchantingUIController", true)!.GetMethod("GetEnchantInfo", flags)!, "HideSocketPreview", "PreviewSocketWeights", 1),
    (epic.GetType("EpicLoot.Crafting.TransferMagicalEffects", true)!.GetMethod("TransferMagicToCraftedItem", flags)!, "PreventTransferredSockets", "TransferSocketCount", 3)
};
foreach ((MethodInfo method, string transform, string helper, int count) in cases)
{
    object instructions = read.Invoke(null, new object?[] { method, null })!;
    object[] rewritten = ((IEnumerable)Call(transform, instructions)!).Cast<object>().ToArray();
    int calls = rewritten.Count(i => (i.GetType().GetField("operand")!.GetValue(i) as MethodInfo)?.Name == helper);
    Require(calls == count, method.Name + " rewrites actual production IL");
    // The version guard must not quietly accept a changed method body.
    object empty = Activator.CreateInstance(instructions.GetType())!;
    bool rejected = false;
    try { _ = ((IEnumerable)Call(transform, empty)!).Cast<object>().ToArray(); }
    catch (InvalidOperationException) { rejected = true; }
    Require(rejected, transform + " rejects changed IL");
}

compatibility.GetField("rarityType", flags)!.SetValue(null, rarity);
compatibility.GetField("craftedItem", flags)!.SetValue(null,
    epic.GetType("EpicLoot.Crafting.TransferMagicalEffects", true)!.GetField("CraftedItem", flags));
MethodInfo Required(string type, string method) => epic.GetType(type, true)!.GetMethod(method, flags)!;
(MethodInfo Method, string? Prefix, string? Postfix, string? Transpiler)[] patches =
{
    (Required("EpicLoot.EpicLoot", "IsAllowedMagicItemType"), null, "AllowTablet", null),
    (Required("EpicLoot.GatedItemType.ItemTypeClassifier", "ClassifyFromFields"), null, "ClassifyTablet", null),
    (cases[0].Method, null, null, cases[0].Transform),
    (cases[1].Method, null, null, cases[1].Transform),
    (cases[2].Method, null, null, cases[2].Transform),
    (Required("EpicLoot.ShardStones.ShardSlotChisel", "CanApply"), "PreventChisel", null, null),
    (Required("EpicLoot.ShardStones.ShardSocketManager", "CanSocket"), "PreventPlacement", null, null),
    (Required("EpicLoot.ShardStones.ShardSocketManager", "CanCoexist"), "PreventPlacement", null, null),
    (epic.GetType("EpicLoot.Crafting.CraftSuccessDialog", true)!.GetMethod("Show")!, null, "ShowCraftResult", null)
};
foreach ((MethodInfo method, string? prefix, string? postfix, string? transpiler) in patches)
{
    Require(method != null, "patch target exists");
    foreach (string? callback in new[] { prefix, postfix, transpiler }.Where(name => name != null))
        Require(compatibility.GetMethod(callback!, flags) != null, "callback exists: " + callback);
}
foreach (string name in new[] { "CanSocket", "CanCoexist" })
    Require(Required("EpicLoot.ShardStones.ShardSocketManager", name).GetParameters()
            .Any(p => p.Name == "reason" && p.ParameterType == typeof(string).MakeByRefType()),
        name + " callback binds the actual rejection reason");
System.Console.WriteLine("Runtime attachment and gameplay require a Unity client; this harness checks policy, targets, and IL transformations only.");
