using System;
using System.Collections.Generic;
using System.Reflection;
using EpicLoot.ShardStones;
using PraetorisClient.EpicLootFeature;

// Runs the production handlers against small game/API substitutes. This is not live-game validation.
internal static class Program
{
    private static int checks;
    private static void Equal(float expected, float actual)
    {
        checks++;
        if (Math.Abs(expected - actual) > 0.0001f)
            throw new Exception($"Expected {expected}, got {actual}");
    }
    private static object[] Invoke(string patch, string method, params object[] args)
    {
        typeof(FullHealthShard).GetNestedType(patch, BindingFlags.NonPublic)!
            .GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
        return args;
    }
    private static void Main()
    {
        FullHealthShard.RegisterEffects();
        Equal(7, EpicLootApiBridge.Definitions.Count);
        foreach (Newtonsoft.Json.Linq.JObject definition in EpicLootApiBridge.Definitions)
        {
            Equal(6, ((Newtonsoft.Json.Linq.JObject)definition["ValuesPerRarity"]!).Count);
            Equal(1, (bool)definition["Requirements"]!["NoRoll"]! ? 1 : 0);
        }
        Equal(7, FullHealthShard.TypeEffects.Count);
        Player player = new Player();
        Player.m_localPlayer = player;
        ItemDrop.ItemData weapon = new ItemDrop.ItemData();
        player.Equipped = weapon;
        SEMan seman = new SEMan { m_character = player };
        Attack attack = new Attack { m_character = player, m_weapon = weapon };
        foreach (float health in new[] { 100f, 99.995f, 99.98f, 50f, 0f })
        {
            player.Health = health;
            bool full = health >= 99.99f;
            Equal(full ? 1 : 0, FullHealthShard.IsFullHealth(player) ? 1 : 0);
            foreach (ShardSlotCategory slot in new[] { ShardSlotCategory.MeleeWeapon, ShardSlotCategory.RangedWeapon, ShardSlotCategory.MagicWeapon })
            {
                weapon.Slot = slot;
                HitData hit = new HitData();
                Invoke("AttackDamagePatch", "Postfix", attack, hit);
                Equal(full && slot != ShardSlotCategory.MagicWeapon ? 125 : 100, hit.m_damage.Value);
            }
            Equal(full ? 7.5f : 10, (float)Invoke("AttackEitrPatch", "Postfix", player, weapon, 10f)[2]);
            Equal(full ? 125 : 100, (float)Invoke("BlockPowerPatch", "Postfix", weapon, 100f)[1]);
            Equal(full ? 1.25f : 1, (float)Invoke("StaminaRegenPatch", "Postfix", seman, 1f)[1]);
            Equal(full ? 12.5f : 10, (float)Invoke("AdrenalinePatch", "Prefix", player, 10f)[1]);
            Equal(-10, (float)Invoke("AdrenalinePatch", "Prefix", player, -10f)[1]);
            Equal(full ? 7.5f : 10, (float)Invoke("RunningStaminaPatch", "Postfix", seman, 10f)[1]);
        }
        player.Health = 100;
        player.Dead = true;
        Equal(0, FullHealthShard.IsFullHealth(player) ? 1 : 0);
        player.Dead = false;
        Equal(0, FullHealthShard.IsFullHealth(null) ? 1 : 0);
        EpicLootApiBridge.Bonus = 200;
        Equal(2, (float)Invoke("AttackEitrPatch", "Postfix", player, weapon, 10f)[2]);
        Equal(2, (float)Invoke("RunningStaminaPatch", "Postfix", seman, 10f)[1]);
        Equal(0, (float)Invoke("AttackEitrPatch", "Postfix", player, weapon, 0f)[2]);
        player.Equipped = null;
        Equal(100, (float)Invoke("BlockPowerPatch", "Postfix", weapon, 100f)[1]);
        seman.m_character = new Character();
        Equal(1, (float)Invoke("StaminaRegenPatch", "Postfix", seman, 1f)[1]);
        Console.WriteLine($"Passed {checks} handler and definition checks. Live game checks remain pending.");
    }
}

public class Character { }
public class Player : Character
{
    public static Player? m_localPlayer;
    public float Health = 100;
    public bool Dead;
    public ItemDrop.ItemData? Equipped;
    public bool IsDead() => Dead;
    public float GetHealth() => Health;
    public float GetMaxHealth() => 100;
    public bool IsItemEquiped(ItemDrop.ItemData item) => item == Equipped;
    public void AddAdrenaline(float v) { }
}
public class ItemDrop
{
    public class ItemData
    {
        public ShardSlotCategory Slot;
        public float GetBlockPower(int quality, float skill) => 100;
    }
}
public class Attack
{
    public Character m_character = null!;
    public ItemDrop.ItemData m_weapon = null!;
    public float GetAttackEitr(Character character, ItemDrop.ItemData weapon) => 10;
}
public class SEMan
{
    public Character m_character = null!;
    public void ModifyStaminaRegen(ref float value) { }
    public void ModifyRunStaminaDrain() { }
}
public class HitData
{
    public DamageTypes m_damage = new DamageTypes();
    public class DamageTypes
    {
        public float Value = 100;
        public void Modify(float multiplier) => Value *= multiplier;
    }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type type, string method, params Type[] args) { }
    }
}
namespace UnityEngine
{
    public static class Mathf
    {
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
    }
}
namespace EpicLoot.ShardStones
{
    public enum ShardSlotCategory { MeleeWeapon, RangedWeapon, MagicWeapon, Shield, Armor, Trinket, Utility }
    public static class Shards
    {
        public static ShardSlotCategory? ResolveCategory(ItemDrop.ItemData item) => item.Slot;
        public static ShardSlotCategory? GroupOf(ShardSlotCategory slot) => slot;
    }
}
internal static class EpicLootApiBridge
{
    internal static float Bonus = 25;
    internal static readonly List<Newtonsoft.Json.Linq.JObject> Definitions = new List<Newtonsoft.Json.Linq.JObject>();
    internal static bool TryAddMagicEffect(string json, out string key)
    {
        Newtonsoft.Json.Linq.JObject definition = Newtonsoft.Json.Linq.JObject.Parse(json);
        Definitions.Add(definition);
        key = (string)definition["Type"]!;
        return true;
    }
    internal static float GetTotalPlayerActiveMagicEffectValue(Player player, string effect, float scale) => Bonus * scale;
    internal static float GetTotalActiveMagicEffectValueForWeapon(Player? player, ItemDrop.ItemData weapon, string effect, float scale)
    {
        if (player != null) throw new Exception("Item bonuses must not include other equipped weapons.");
        return Bonus * scale;
    }
}
