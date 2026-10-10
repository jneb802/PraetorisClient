// Small game substitutes for policy tests. These do not emulate Unity or Harmony patch execution.
using System;
using System.Collections.Generic;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type type, string method) { }
    }
}

public sealed class Skills
{
    public void RaiseSkill() { }
    public void CheatRaiseSkill() { }
    public void OnDeath() { }
    public void Clear() { }
}

public sealed class SEMan
{
    public readonly HashSet<int> Effects = new();
    public void AddStatusEffect(int hash) => Effects.Add(hash);
    public void RemoveStatusEffect(int hash) => Effects.Remove(hash);
}

public sealed class Player
{
    public static Player? m_localPlayer;
    public readonly ZNetView m_nview = new();
    public bool Inside;
    public bool Dead;
    public bool Pvp;
    private readonly Skills _skills = new();
    private readonly SEMan _effects = new();
    public bool IsDead() => Dead;
    public bool IsPVPEnabled() => Pvp;
    public void SetPVP(bool enabled) => Pvp = enabled;
    public bool CanSwitchPVP() => true;
    public Skills GetSkills() => _skills;
    public SEMan GetSEMan() => _effects;
    public void OnDeath() { }
}

public sealed class ZNetView
{
    public bool Valid = true;
    public bool Owner = true;
    public bool IsValid() => Valid;
    public bool IsOwner() => Owner;
}

namespace PraetorisClient.PvpWardFeature
{
    internal static class PvpWard
    {
        internal static bool Contains(Player player) => player.Inside;
    }

    internal static class PvpWardPrefab
    {
        internal const int EffectHash = 123;
    }
}
