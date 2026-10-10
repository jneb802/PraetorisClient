using System;
using System.Reflection;
using PraetorisClient.PvpWardFeature;

internal static class Program
{
    private static int _passed;

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        _passed++;
        Console.WriteLine("PASS: " + name);
    }

    private static object? Call(Type type, string method, params object?[] args) =>
        type.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);

    private static bool Allows(Type patch, Skills skills) => (bool)Call(patch, "Prefix", skills)!;

    private static void Main()
    {
        Player local = new Player();
        Player.m_localPlayer = local;
        PvpWardPlayer.Update(local);
        Check(!local.Pvp && local.GetSEMan().Effects.Count == 0, "Outside area leaves PvP and effects unchanged");
        Check(Allows(typeof(PvpWardSkillGainPatch), local.GetSkills()), "Outside area permits normal skill gain");

        local.Inside = true;
        PvpWardPlayer.Update(local);
        Check(local.Pvp && local.GetSEMan().Effects.Contains(PvpWardPrefab.EffectHash), "Entry forces PvP and adds the status effect");
        Check(!Allows(typeof(PvpWardSkillGainPatch), local.GetSkills()), "Inside area blocks normal skill gain");
        Check(!(bool)Call(typeof(PvpWardDirectSkillGainPatch), "Prefix", local.GetSkills(), 10f)!, "Inside area blocks direct positive skill grants");
        Check((bool)Call(typeof(PvpWardDirectSkillGainPatch), "Prefix", local.GetSkills(), -10f)!, "Explicit skill reductions still work");
        Check(Allows(typeof(PvpWardSkillGainPatch), new Skills()), "Local arena does not block another player's skills");

        object?[] pvpArgs = { local, false };
        Call(typeof(PvpWardSetPvpPatch), "Prefix", pvpArgs);
        Check((bool)pvpArgs[1]!, "PvP off requests are forced on inside the area");
        object?[] switchArgs = { local, true };
        Call(typeof(PvpWardCanSwitchPvpPatch), "Postfix", switchArgs);
        Check(!(bool)switchArgs[1]!, "The PvP toggle is disabled inside the area");

        local.GetSEMan().Effects.Clear();
        Check(!Allows(typeof(PvpWardSkillGainPatch), local.GetSkills()), "Cleansing the status effect does not allow skill gain");
        Check(!Allows(typeof(PvpWardDeathSkillLossPatch), local.GetSkills()), "Cleansing the status effect does not allow death skill loss");
        PvpWardPlayer.Update(local);
        Check(local.GetSEMan().Effects.Contains(PvpWardPrefab.EffectHash), "The status effect returns after cleansing");

        Player remote = new Player { Inside = true };
        PvpWardPlayer.Update(remote);
        Check(!remote.Pvp && remote.GetSEMan().Effects.Count == 0, "Only the local player is changed by the update");

        local.Inside = false;
        PvpWardPlayer.Update(local);
        Check(!local.Pvp && local.GetSEMan().Effects.Count == 0, "Exit restores PvP off and removes the status effect");
        Check(Allows(typeof(PvpWardDeathSkillLossPatch), local.GetSkills()), "Death outside the area keeps normal skill loss");

        local.Pvp = true;
        local.Inside = true;
        PvpWardPlayer.Update(local);
        local.Inside = false;
        PvpWardPlayer.Update(local);
        Check(local.Pvp, "Exit preserves PvP when it was already on before entry");

        local.Pvp = false;
        local.Inside = true;
        PvpWardPlayer.Update(local);
        object?[] deathArgs = { local, null };
        Call(typeof(PvpWardDeathPatch), "Prefix", deathArgs);
        local.Dead = true;
        local.Inside = false;
        local.GetSEMan().Effects.Clear();
        Check(!Allows(typeof(PvpWardDeathSkillLossPatch), local.GetSkills()), "Death captures protection before effects and position change");
        Check(!Allows(typeof(PvpWardDeathSkillResetPatch), local.GetSkills()), "DeathSkillsReset cannot clear protected skills");
        Check(Allows(typeof(PvpWardDeathSkillResetPatch), remote.GetSkills()), "Protected death does not affect another player's skill reset");
        Exception failure = new Exception("simulated death failure");
        Check(ReferenceEquals(Call(typeof(PvpWardDeathPatch), "Finalizer", failure, deathArgs[1]), failure), "Death exceptions remain visible");
        Check(PvpWardPlayer.ProtectedDeath == null && Allows(typeof(PvpWardDeathSkillResetPatch), local.GetSkills()), "Death cleanup restores normal skill reset even after an exception");
        PvpWardPlayer.Update(local);
        Check(!local.Pvp && local.GetSEMan().Effects.Count == 0, "Death restores the prior PvP setting and removes the effect");

        local.Dead = false;
        local.Inside = true;
        PvpWardPlayer.Update(local);
        PvpWardPlayer.Forget(local);
        Player next = new Player { Inside = true, Pvp = true };
        Player.m_localPlayer = next;
        PvpWardPlayer.Update(next);
        next.Inside = false;
        PvpWardPlayer.Update(next);
        Check(next.Pvp, "A new player instance does not inherit the old player's PvP preference");

        next.Pvp = false;
        next.Inside = true;
        object?[] earlyToggle = { next, false };
        Call(typeof(PvpWardSetPvpPatch), "Prefix", earlyToggle);
        next.SetPVP((bool)earlyToggle[1]!);
        PvpWardPlayer.Update(next);
        next.Inside = false;
        PvpWardPlayer.Update(next);
        Check(!next.Pvp, "A PvP call before the first arena update still preserves the entry preference");

        next.Inside = true;
        next.m_nview.Valid = false;
        PvpWardPlayer.Update(next);
        Check(!next.Pvp && next.GetSEMan().Effects.Count == 0, "Disconnected players are not updated");
        next.m_nview.Valid = true;
        next.m_nview.Owner = false;
        PvpWardPlayer.Update(next);
        Check(!next.Pvp && next.GetSEMan().Effects.Count == 0, "Players without local network ownership are not updated");
        Console.WriteLine($"{_passed} policy checks passed. Unity, Harmony execution, and multiplayer still require live validation.");
    }
}
