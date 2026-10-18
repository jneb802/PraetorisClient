using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace PraetorisClient;

// Temporary bridge for Epic Loot PRs #45 and #47. No replacement EpicLoot.dll or new save format.
internal static class EndgameCompatibility
{
    internal const string EndgameGuid = "warpalicious.ValheimEndgame";
    private const string Owner = "warpalicious.PraetorisClient.EndgameCompatibility";
    // Production Season 8 8.0.46. Epic Loot forks can share a version; match the actual binary.
    private const string LegacyHash = "802790e0cbd8a6f951627619a488b84e6a406e8770e2687b2fff8b63d2adf9ea";
    private static readonly Harmony Patches = new(Owner);
    private static FieldInfo socketCount = null!;
    private static FieldInfo craftedItem = null!;
    private static MethodInfo socketWeights = null!;
    private static Type rarityType = null!;

    internal static bool IsTabletName(string? name)
    {
        if (name == "ve_tablet") return true;
        const string prefix = "ve_tablet_tier";
        return name != null && name.Length == prefix.Length + 2 &&
               name.StartsWith(prefix, StringComparison.Ordinal) &&
               int.TryParse(name.Substring(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int tier) &&
               tier >= 1 && tier <= 15;
    }

    private static bool IsTablet(ItemDrop.ItemData? item) => IsTabletName(item?.m_dropPrefab?.name);

    internal static void Initialize()
    {
        if (!Chainloader.PluginInfos.ContainsKey(EndgameGuid) ||
            !Chainloader.PluginInfos.TryGetValue(EpicLootApiBridge.PluginGuid, out BepInEx.PluginInfo info)) return;

        Assembly assembly = info.Instance.GetType().Assembly;
        try
        {
            if (HasNativeSupport(assembly))
            {
                ApplyCraftResultFix(assembly);
                PraetorisClientPlugin.Log.LogInfo("Endgame compatibility: native Epic Loot enchanting/socket support detected; temporary item patches skipped.");
                return;
            }

            using SHA256 sha = SHA256.Create();
            using System.IO.FileStream stream = System.IO.File.OpenRead(assembly.Location);
            string hash = string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2")));
            if (hash != LegacyHash)
            {
                PraetorisClientPlugin.Log.LogWarning("Endgame compatibility: unsupported Epic Loot binary. No temporary patches applied; tablet enchanting/socket rules are NOT provided.");
                return;
            }

            Type item = typeof(ItemDrop.ItemData);
            rarityType = RequiredType(assembly, "EpicLoot.ItemRarity");
            socketCount = AccessTools.Field(RequiredType(assembly, "EpicLoot.MagicItem"), "SocketCount")
                          ?? throw new MissingFieldException("MagicItem.SocketCount");
            craftedItem = AccessTools.Field(RequiredType(assembly, "EpicLoot.Crafting.TransferMagicalEffects"), "CraftedItem")
                          ?? throw new MissingFieldException("TransferMagicalEffects.CraftedItem");
            socketWeights = RequiredMethod(assembly, "EpicLoot.LootRoller", "GetSocketCountsPerRarity", rarityType);

            // Manual registration: these methods must not join Praetoris Client's assembly-wide patch scan.
            Patch(RequiredMethod(assembly, "EpicLoot.EpicLoot", "IsAllowedMagicItemType", item), postfix: nameof(AllowTablet));
            Patch(RequiredMethod(assembly, "EpicLoot.GatedItemType.ItemTypeClassifier", "ClassifyFromFields", item), postfix: nameof(ClassifyTablet));
            Patch(RequiredMethod(assembly, "EpicLoot.LootRoller", "RollMagicItem", rarityType, item, typeof(float), typeof(float)), transpiler: nameof(PreventRolledSockets));
            Patch(RequiredMethod(assembly, "EpicLoot.CraftingV2.EnchantingUIController", "GetEnchantInfo", item, rarityType), transpiler: nameof(HideSocketPreview));
            Patch(RequiredMethod(assembly, "EpicLoot.Crafting.TransferMagicalEffects", "TransferMagicToCraftedItem", typeof(Player)), transpiler: nameof(PreventTransferredSockets));
            Patch(RequiredMethod(assembly, "EpicLoot.ShardStones.ShardSlotChisel", "CanApply", item, item, typeof(int).MakeByRefType(), typeof(string).MakeByRefType()), prefix: nameof(PreventChisel));
            Patch(RequiredMethod(assembly, "EpicLoot.ShardStones.ShardSocketManager", "CanSocket", item, item,
                typeof(string).MakeByRefType()), prefix: nameof(PreventPlacement));
            Patch(RequiredMethod(assembly, "EpicLoot.ShardStones.ShardSocketManager", "CanCoexist", item, item,
                typeof(IEnumerable<ItemDrop.ItemData>), typeof(string).MakeByRefType()), prefix: nameof(PreventPlacement));
            ApplyCraftResultFix(assembly);
            PraetorisClientPlugin.Log.LogInfo("Endgame compatibility: applied tablet enchanting, socket restrictions, and craft result fix to verified Epic Loot 0.14.13.");
        }
        catch (Exception exception)
        {
            Shutdown();
            PraetorisClientPlugin.Log.LogError("Endgame compatibility could not initialize; all temporary patches removed: " + exception.GetBaseException().Message);
        }
    }

    internal static void Shutdown() => Patches.UnpatchSelf();

    private static Type RequiredType(Assembly assembly, string name) => assembly.GetType(name, true)!;
    private static MethodInfo RequiredMethod(Assembly assembly, string type, string name, params Type[] parameters) =>
        AccessTools.Method(RequiredType(assembly, type), name, parameters) ?? throw new MissingMethodException(type, name);

    private static void Patch(MethodInfo target, string? prefix = null, string? postfix = null, string? transpiler = null) =>
        Patches.Patch(target, Hook(prefix), Hook(postfix), Hook(transpiler));

    private static HarmonyMethod? Hook(string? name) => name == null ? null : new HarmonyMethod(typeof(EndgameCompatibility), name);

    private static bool HasNativeSupport(Assembly assembly)
    {
        Type? config = assembly.GetType("EpicLoot.GatedItemType.ItemTypeInfo");
        Type? details = assembly.GetType("EpicLoot.GatedItemType.GatedItemDetails");
        Type? helper = assembly.GetType("EpicLoot.GatedItemType.GatedItemTypeHelper");
        MethodInfo? native = helper == null ? null : AccessTools.Method(helper, "IsSocketable", new[] { typeof(ItemDrop.ItemData) });
        if (config?.GetField("Enchantable")?.FieldType != typeof(bool) || config.GetField("Socketable")?.FieldType != typeof(bool) ||
            details?.GetProperty("Enchantable")?.PropertyType != typeof(bool) || details.GetProperty("Socketable")?.PropertyType != typeof(bool) || native == null) return false;

        // Fields alone can be present in an incomplete upstream build. Check each socket enforcement path.
        (string Type, string Method)[] paths =
        {
            ("EpicLoot.LootRoller", "RollMagicItem"),
            ("EpicLoot.CraftingV2.EnchantingUIController", "GetEnchantInfo"),
            ("EpicLoot.Crafting.TransferMagicalEffects", "TransferMagicToCraftedItem"),
            ("EpicLoot.ShardStones.ShardSlotChisel", "CanApply"),
            ("EpicLoot.ShardStones.ShardSocketManager", "ResolvePlacementEffect")
        };
        foreach ((string typeName, string methodName) in paths)
        {
            Type? type = assembly.GetType(typeName);
            if (type == null || !AccessTools.GetDeclaredMethods(type).Where(m => m.Name == methodName)
                    .Any(m => PatchProcessor.GetOriginalInstructions(m).Any(i => i.Calls(native)))) return false;
        }
        MethodInfo allowed = RequiredMethod(assembly, "EpicLoot.EpicLoot", "IsAllowedMagicItemType", typeof(ItemDrop.ItemData));
        MethodInfo? enchantable = details.GetProperty("Enchantable")?.GetGetMethod();
        return enchantable != null && PatchProcessor.GetOriginalInstructions(allowed).Any(i => i.Calls(enchantable));
    }

    private static void AllowTablet(ItemDrop.ItemData __0, ref bool __result)
    {
        if (IsTablet(__0) && __0.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Ammo &&
            __0.m_shared.m_itemType != ItemDrop.ItemData.ItemType.AmmoNonEquipable) __result = true;
    }

    private static void ClassifyTablet(ItemDrop.ItemData __0, ref string __result)
    {
        if (IsTablet(__0)) __result = "Tablet";
    }

    private static int FilterSocketCount(int count, ItemDrop.ItemData item) => IsTablet(item) ? 0 : count;

    private static int TransferSocketCount(object magicItem) =>
        IsTablet(craftedItem.GetValue(null) as ItemDrop.ItemData) ? 0 : (int)socketCount.GetValue(magicItem);

    private static List<KeyValuePair<int, float>> PreviewSocketWeights(int rarity, ItemDrop.ItemData item) =>
        IsTablet(item) ? new List<KeyValuePair<int, float>>() :
            (List<KeyValuePair<int, float>>)socketWeights.Invoke(null, new[] { Enum.ToObject(rarityType, rarity) });

    private static IEnumerable<CodeInstruction> PreventRolledSockets(IEnumerable<CodeInstruction> instructions)
    {
        int matches = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Stfld && Equals(instruction.operand, socketCount))
            {
                matches++;
                CodeInstruction load = new(OpCodes.Ldarg_1);
                load.labels.AddRange(instruction.labels);
                instruction.labels.Clear();
                load.blocks.AddRange(instruction.blocks);
                instruction.blocks.Clear();
                yield return load;
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(EndgameCompatibility), nameof(FilterSocketCount)));
            }
            yield return instruction;
        }
        if (matches != 1) throw new InvalidOperationException("Unexpected Epic Loot socket roll layout.");
    }

    private static IEnumerable<CodeInstruction> HideSocketPreview(IEnumerable<CodeInstruction> instructions)
    {
        int matches = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(socketWeights))
            {
                matches++;
                CodeInstruction load = new(OpCodes.Ldarg_0);
                load.labels.AddRange(instruction.labels);
                instruction.labels.Clear();
                load.blocks.AddRange(instruction.blocks);
                instruction.blocks.Clear();
                yield return load;
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(EndgameCompatibility), nameof(PreviewSocketWeights));
            }
            yield return instruction;
        }
        if (matches != 1) throw new InvalidOperationException("Unexpected Epic Loot socket preview layout.");
    }

    private static IEnumerable<CodeInstruction> PreventTransferredSockets(IEnumerable<CodeInstruction> instructions)
    {
        int matches = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, socketCount))
            {
                matches++;
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(EndgameCompatibility), nameof(TransferSocketCount));
            }
            yield return instruction;
        }
        if (matches != 3) throw new InvalidOperationException("Unexpected Epic Loot socket transfer layout.");
    }

    private static bool PreventChisel(ItemDrop.ItemData __1, ref int __2, ref string __3, ref bool __result)
    {
        if (!IsTablet(__1)) return true;
        __2 = 0;
        __3 = "$mod_epicloot_socket_unavailable";
        __result = false;
        return false;
    }

    private static bool PreventPlacement(ItemDrop.ItemData __0, ref string reason, ref bool __result)
    {
        if (!IsTablet(__0)) return true;
        reason = "$mod_epicloot_socket_unavailable";
        __result = false;
        return false;
    }

    private static void ShowCraftResult(object __instance)
    {
        foreach (string field in new[] { "NameText", "Description", "Icon" })
        {
            if (AccessTools.Field(__instance.GetType(), field)?.GetValue(__instance) is Behaviour component)
                component.enabled = true;
        }
    }

    private static void ApplyCraftResultFix(Assembly assembly)
    {
        Type dialog = RequiredType(assembly, "EpicLoot.Crafting.CraftSuccessDialog");
        MethodInfo show = RequiredMethod(assembly, dialog.FullName!, "Show", typeof(ItemDrop.ItemData));
        MethodInfo enabled = typeof(Behaviour).GetProperty(nameof(Behaviour.enabled))!.GetSetMethod()!;
        if (PatchProcessor.GetOriginalInstructions(show).Count(i => i.Calls(enabled)) >= 3) return;
        foreach (string field in new[] { "NameText", "Description", "Icon" })
        {
            FieldInfo member = AccessTools.Field(dialog, field) ?? throw new MissingFieldException(dialog.FullName, field);
            if (!typeof(Behaviour).IsAssignableFrom(member.FieldType))
                throw new InvalidOperationException("Unexpected Epic Loot craft result component: " + field);
        }
        Patch(show, postfix: nameof(ShowCraftResult));
        PraetorisClientPlugin.Log.LogInfo("Endgame compatibility: applied temporary craft result visibility fix.");
    }
}
