using System;
using HarmonyLib;

namespace PraetorisClient.PvpWardFeature
{
    internal static class PvpWardPlayer
    {
        private static Player? _player;
        private static bool _previousPvp;
        internal static Skills? ProtectedDeath;

        internal static void RememberPvp(Player player)
        {
            if (_player == player) return;
            _previousPvp = player.IsPVPEnabled();
            _player = player;
        }

        internal static void Update(Player player)
        {
            if (player != Player.m_localPlayer || player.m_nview == null ||
                !player.m_nview.IsValid() || !player.m_nview.IsOwner()) return;
            bool inside = !player.IsDead() && PvpWard.Contains(player);
            if (inside)
            {
                RememberPvp(player);
                player.SetPVP(true);
                player.GetSEMan().AddStatusEffect(PvpWardPrefab.EffectHash);
            }
            else
            {
                player.GetSEMan().RemoveStatusEffect(PvpWardPrefab.EffectHash);
                if (_player == player)
                {
                    _player = null;
                    player.SetPVP(_previousPvp);
                }
            }
        }

        internal static void Forget(Player player)
        {
            if (_player == player) _player = null;
        }

        internal static bool BlocksSkills(Skills skills) => Player.m_localPlayer != null &&
            Player.m_localPlayer.GetSkills() == skills && PvpWard.Contains(Player.m_localPlayer);
    }

    [HarmonyPatch(typeof(Player), "Update")]
    internal static class PvpWardPlayerUpdatePatch
    {
        private static void Postfix(Player __instance) => PvpWardPlayer.Update(__instance);
    }

    [HarmonyPatch(typeof(Player), "OnDestroy")]
    internal static class PvpWardPlayerDestroyPatch
    {
        private static void Prefix(Player __instance) => PvpWardPlayer.Forget(__instance);
    }

    [HarmonyPatch(typeof(Player), nameof(Player.SetPVP))]
    internal static class PvpWardSetPvpPatch
    {
        private static void Prefix(Player __instance, ref bool enabled)
        {
            if (__instance == Player.m_localPlayer && !__instance.IsDead() && PvpWard.Contains(__instance))
            {
                PvpWardPlayer.RememberPvp(__instance);
                enabled = true;
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.CanSwitchPVP))]
    internal static class PvpWardCanSwitchPvpPatch
    {
        private static void Postfix(Player __instance, ref bool __result)
        {
            if (PvpWard.Contains(__instance)) __result = false;
        }
    }

    [HarmonyPatch(typeof(Skills), nameof(Skills.RaiseSkill))]
    internal static class PvpWardSkillGainPatch
    {
        private static bool Prefix(Skills __instance) => !PvpWardPlayer.BlocksSkills(__instance);
    }

    [HarmonyPatch(typeof(Skills), nameof(Skills.CheatRaiseSkill))]
    internal static class PvpWardDirectSkillGainPatch
    {
        // Some mods grant levels through the same method as the console command.
        private static bool Prefix(Skills __instance, float value) => value <= 0f || !PvpWardPlayer.BlocksSkills(__instance);
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
    internal static class PvpWardDeathPatch
    {
        private static void Prefix(Player __instance, out Skills? __state)
        {
            __state = PvpWardPlayer.ProtectedDeath;
            // Capture the position before death processing clears effects or changes player state.
            if (__instance == Player.m_localPlayer && PvpWard.Contains(__instance))
                PvpWardPlayer.ProtectedDeath = __instance.GetSkills();
        }

        private static Exception? Finalizer(Exception? __exception, Skills? __state)
        {
            PvpWardPlayer.ProtectedDeath = __state;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Skills), nameof(Skills.OnDeath))]
    internal static class PvpWardDeathSkillLossPatch
    {
        private static bool Prefix(Skills __instance) =>
            PvpWardPlayer.ProtectedDeath != __instance && !PvpWardPlayer.BlocksSkills(__instance);
    }

    [HarmonyPatch(typeof(Skills), nameof(Skills.Clear))]
    internal static class PvpWardDeathSkillResetPatch
    {
        // Also cover the world's DeathSkillsReset setting, but allow ordinary loading/reset operations.
        private static bool Prefix(Skills __instance) => PvpWardPlayer.ProtectedDeath != __instance;
    }
}
