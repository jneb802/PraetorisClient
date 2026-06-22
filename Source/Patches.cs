using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PraetorisClient
{
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Awake))]
    internal static class ZNetAwakePatch
    {
        private static void Postfix()
        {
            CreativeCommandZoneState.Clear();
            PraetorisClientRpc.Register();
        }
    }

    [HarmonyPatch(typeof(Chat), nameof(Chat.SendInput))]
    internal static class ChatSendInputPatch
    {
        private static bool Prefix(Chat __instance)
        {
            return !LinkCommandHandler.TryHandle(__instance);
        }
    }

    [HarmonyPatch(typeof(DamageText), nameof(DamageText.ShowText), typeof(DamageText.TextType), typeof(Vector3), typeof(string), typeof(bool))]
    internal static class DamageTextShowTextLocalOnlyPatch
    {
        private static readonly MethodInfo? RpcDamageTextMethod = AccessTools.Method(typeof(DamageText), "RPC_DamageText", new[] { typeof(long), typeof(ZPackage) });
        private static readonly object[] RpcDamageTextArguments = new object[2];
        private static bool _reportedMissingMethod;
        private static bool _reportedInvokeFailure;

        private static bool Prefix(DamageText __instance, DamageText.TextType type, Vector3 pos, string text, bool player)
        {
            if (!PraetorisClientPlugin.LocalDamageTextOnly.Value)
            {
                return true;
            }

            if (RpcDamageTextMethod == null)
            {
                ReportMissingMethod();
                return true;
            }

            ZPackage package = new();
            package.Write((int)type);
            package.Write(pos);
            package.Write(text);
            package.Write(player);
            package.SetPos(0);

            try
            {
                RpcDamageTextArguments[0] = ZNet.GetUID();
                RpcDamageTextArguments[1] = package;
                RpcDamageTextMethod.Invoke(__instance, RpcDamageTextArguments);
            }
            catch (Exception ex)
            {
                ReportInvokeFailure(ex);
                return true;
            }

            return false;
        }

        private static void ReportMissingMethod()
        {
            if (_reportedMissingMethod)
            {
                return;
            }

            _reportedMissingMethod = true;
            PraetorisClientPlugin.Log.LogWarning("Could not find DamageText.RPC_DamageText; allowing vanilla damage text routing.");
        }

        private static void ReportInvokeFailure(Exception ex)
        {
            if (_reportedInvokeFailure)
            {
                return;
            }

            _reportedInvokeFailure = true;
            PraetorisClientPlugin.Log.LogWarning("Failed to display local damage text; allowing vanilla damage text routing. " + ex.Message);
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
    internal static class CharacterApplyDamageTelemetryPatch
    {
        private static void Prefix(Character __instance, HitData hit, ref DamageObservationState __state)
        {
            __state = ValheimEventsTelemetry.CaptureDamageBefore(__instance, hit);
        }

        private static void Postfix(Character __instance, HitData hit, HitData.DamageModifier mod, DamageObservationState __state)
        {
            ValheimEventsTelemetry.LogDamageApplied(__instance, hit, mod, __state);
        }
    }

    [HarmonyPatch(typeof(Player), "OnDeath")]
    internal static class PlayerDeathTelemetryPatch
    {
        private static void Prefix(Player __instance, ref DeathObservationState __state)
        {
            __state = ValheimEventsTelemetry.CaptureDeathBefore(__instance);
        }

        private static void Postfix(Player __instance, DeathObservationState __state)
        {
            ValheimEventsTelemetry.LogPlayerDied(__instance, __state);
        }
    }

    [HarmonyPatch(typeof(Minimap), "Explore", typeof(int), typeof(int))]
    internal static class MinimapExploreTelemetryPatch
    {
        private static void Postfix(Minimap __instance, int x, int y, bool __result)
        {
            if (__result)
                ValheimEventsTelemetry.RecordExploredCell(__instance, x, y);
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.Update))]
    internal static class GameUpdateTelemetryPatch
    {
        private static void Postfix()
        {
            FrameTimeMonitor.Update();
            ValheimEventsTelemetry.Update();
            RpcTraceTelemetry.Update();
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.Logout))]
    internal static class GameLogoutRpcTracePatch
    {
        private static bool Prefix(Game __instance, bool save, bool changeToStartScene)
        {
            return RpcTraceTelemetry.ShouldAllowLogout(__instance, save, changeToStartScene);
        }
    }

    [HarmonyPatch(typeof(Game), "OnApplicationQuit")]
    internal static class GameApplicationQuitRpcTracePatch
    {
        private static void Prefix()
        {
            RpcTraceTelemetry.OnApplicationQuitFallback();
        }
    }

    [HarmonyPatch(typeof(Menu), "QuitGame")]
    internal static class MenuQuitGameRpcTracePatch
    {
        private static bool Prefix()
        {
            return RpcTraceTelemetry.ShouldAllowMenuQuit();
        }
    }

    [HarmonyPatch(typeof(Menu), nameof(Menu.OnQuitYes))]
    internal static class MenuQuitYesRpcTracePatch
    {
        private static bool Prefix()
        {
            return RpcTraceTelemetry.ShouldAllowMenuQuit();
        }
    }

    [HarmonyPatch(typeof(Terminal.ConsoleCommand), nameof(Terminal.ConsoleCommand.RunAction))]
    internal static class ConsoleCommandRunActionCreativeZoneGuardPatch
    {
        private static bool Prefix(Terminal.ConsoleEventArgs args)
        {
            return CreativeCommandZoneState.CanRunCommand(args);
        }
    }

    [HarmonyPatch(typeof(Skills), nameof(Skills.RaiseSkill))]
    internal static class SkillsRaiseSkillCreativeZonePatch
    {
        private static bool Prefix(Skills __instance)
        {
            Player localPlayer = Player.m_localPlayer;
            return localPlayer == null ||
                   __instance != localPlayer.GetSkills() ||
                   !CreativeCommandZoneState.IsLocalPlayerInsideActiveZone();
        }
    }

    [HarmonyPatch(typeof(Player), "EdgeOfWorldKill")]
    internal static class PlayerEdgeOfWorldKillCreativePatch
    {
        private static bool Prefix(Player __instance)
        {
            return __instance == null || !CreativeBiomeOverride.ContainsTerrainOverride(__instance.transform.position);
        }
    }
}
