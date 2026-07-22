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

    internal static class BoatWaterImpactDamage
    {
        internal static bool ShouldBlock(WearNTear wearNTear, HitData hit)
        {
            if (!PraetorisClientPlugin.DisableBoatWaterImpactDamage.Value || wearNTear == null || hit == null)
            {
                return false;
            }

            Ship ship = wearNTear.GetComponent<Ship>();
            if (ship == null || hit.GetAttacker() != null || hit.m_hitType != HitData.HitType.Undefined)
            {
                return false;
            }

            if (hit.m_damage.m_blunt <= 0f || !Mathf.Approximately(hit.m_damage.m_blunt, ship.m_waterImpactDamage))
            {
                return false;
            }

            if (hit.m_damage.m_slash != 0f ||
                hit.m_damage.m_pierce != 0f ||
                hit.m_damage.m_chop != 0f ||
                hit.m_damage.m_pickaxe != 0f ||
                hit.m_damage.m_fire != 0f ||
                hit.m_damage.m_frost != 0f ||
                hit.m_damage.m_lightning != 0f ||
                hit.m_damage.m_poison != 0f ||
                hit.m_damage.m_spirit != 0f)
            {
                return false;
            }

            if ((hit.m_dir - Vector3.up).sqrMagnitude > 0.001f)
            {
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Damage))]
    internal static class WearNTearDamageBoatWaterImpactPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(WearNTear __instance, HitData hit)
        {
            // Vanilla boat water-impact damage is a generic, attackerless, blunt-only
            // ship hit. Other ship damage paths set a different hit type, attacker,
            // direction, damage mix, or amount.
            return !BoatWaterImpactDamage.ShouldBlock(__instance, hit);
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.Update))]
    internal static class GameUpdateTelemetryPatch
    {
        private static void Postfix()
        {
            FrameTimeMonitor.Update();
            RpcTraceTelemetry.Update();
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
