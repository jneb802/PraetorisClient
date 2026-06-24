using System;
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

    internal static class DamageTextSuppression
    {
        [ThreadStatic]
        private static int _suppressDepth;

        [ThreadStatic]
        private static int _aoeDamageDepth;

        [ThreadStatic]
        private static int _aoePieceDamageDepth;

        internal static bool IsSuppressing => _suppressDepth > 0;

        internal static bool IsAoeDamage => _aoeDamageDepth > 0;

        internal static bool IsAoePieceDamage => _aoePieceDamageDepth > 0;

        internal static void BeginSuppress()
        {
            _suppressDepth++;
        }

        internal static void EndSuppress()
        {
            if (_suppressDepth > 0)
            {
                _suppressDepth--;
            }
        }

        internal static void BeginAoeDamage()
        {
            _aoeDamageDepth++;
        }

        internal static void EndAoeDamage()
        {
            if (_aoeDamageDepth > 0)
            {
                _aoeDamageDepth--;
            }
        }

        internal static void BeginAoePieceDamage()
        {
            _aoePieceDamageDepth++;
        }

        internal static void EndAoePieceDamage()
        {
            if (_aoePieceDamageDepth > 0)
            {
                _aoePieceDamageDepth--;
            }
        }

        internal static bool BeginIf(bool shouldSuppress)
        {
            if (!shouldSuppress)
            {
                return false;
            }

            BeginSuppress();
            return true;
        }

        internal static void EndIf(bool shouldSuppress)
        {
            if (shouldSuppress)
            {
                EndSuppress();
            }
        }

        internal static bool ShouldSuppressAoePieceDamageText(HitData? hit)
        {
            return PraetorisClientPlugin.SuppressEnvironmentDamageText.Value
                && hit != null
                && (IsAoePieceDamage || hit.m_radius > 0f);
        }

        internal static bool ShouldSuppressNonPlayerVegetationDamageText(HitData? hit)
        {
            return PraetorisClientPlugin.SuppressEnvironmentDamageText.Value
                && hit != null
                && !IsPlayerDamage(hit);
        }

        internal static bool BeginAoePieceDamageIfNeeded(HitData? hit)
        {
            if (!PraetorisClientPlugin.SuppressEnvironmentDamageText.Value || !IsAoeDamage || hit == null || hit.m_radius > 0f)
            {
                return false;
            }

            BeginAoePieceDamage();
            return true;
        }

        private static bool IsPlayerDamage(HitData hit)
        {
            if (hit.m_hitType == HitData.HitType.PlayerHit)
            {
                return true;
            }

            Character attacker = hit.GetAttacker();
            return attacker != null && attacker.IsPlayer();
        }
    }

    [HarmonyPatch(typeof(DamageText), nameof(DamageText.ShowText), typeof(DamageText.TextType), typeof(Vector3), typeof(string), typeof(bool))]
    internal static class DamageTextShowTextEnvironmentSuppressionPatch
    {
        private static bool Prefix()
        {
            return !DamageTextSuppression.IsSuppressing;
        }
    }

    [HarmonyPatch(typeof(Projectile), "DoAOE")]
    internal static class ProjectileDoAoeDamageTextContextPatch
    {
        private static void Prefix(ref bool __state)
        {
            DamageTextSuppression.BeginAoeDamage();
            __state = true;
        }

        private static void Finalizer(bool __state)
        {
            if (__state)
            {
                DamageTextSuppression.EndAoeDamage();
            }
        }
    }

    [HarmonyPatch(typeof(Attack), "DoAreaAttack")]
    internal static class AttackDoAreaAttackDamageTextContextPatch
    {
        private static void Prefix(ref bool __state)
        {
            DamageTextSuppression.BeginAoeDamage();
            __state = true;
        }

        private static void Finalizer(bool __state)
        {
            if (__state)
            {
                DamageTextSuppression.EndAoeDamage();
            }
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Damage))]
    internal static class WearNTearDamageAoeMarkerPatch
    {
        private static void Prefix(HitData hit, ref bool __state)
        {
            __state = DamageTextSuppression.BeginAoePieceDamageIfNeeded(hit);
        }

        private static void Finalizer(bool __state)
        {
            if (__state)
            {
                DamageTextSuppression.EndAoePieceDamage();
            }
        }
    }

    [HarmonyPatch(typeof(WearNTear), "RPC_Damage")]
    internal static class WearNTearRpcDamageTextSuppressionPatch
    {
        private static void Prefix(HitData hit, ref bool __state)
        {
            __state = DamageTextSuppression.BeginIf(DamageTextSuppression.ShouldSuppressAoePieceDamageText(hit));
        }

        private static void Finalizer(bool __state)
        {
            DamageTextSuppression.EndIf(__state);
        }
    }

    [HarmonyPatch(typeof(TreeBase), "RPC_Damage")]
    internal static class TreeBaseRpcDamageTextSuppressionPatch
    {
        private static void Prefix(HitData hit, ref bool __state)
        {
            __state = DamageTextSuppression.BeginIf(DamageTextSuppression.ShouldSuppressNonPlayerVegetationDamageText(hit));
        }

        private static void Finalizer(bool __state)
        {
            DamageTextSuppression.EndIf(__state);
        }
    }

    [HarmonyPatch(typeof(TreeLog), "RPC_Damage")]
    internal static class TreeLogRpcDamageTextSuppressionPatch
    {
        private static void Prefix(HitData hit, ref bool __state)
        {
            __state = DamageTextSuppression.BeginIf(DamageTextSuppression.ShouldSuppressNonPlayerVegetationDamageText(hit));
        }

        private static void Finalizer(bool __state)
        {
            DamageTextSuppression.EndIf(__state);
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
