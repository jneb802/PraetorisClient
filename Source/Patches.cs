using HarmonyLib;

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
            ValheimEventsTelemetry.Update();
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
}
