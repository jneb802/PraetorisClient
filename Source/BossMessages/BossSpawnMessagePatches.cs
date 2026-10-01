using HarmonyLib;

namespace PraetorisClient
{
    [HarmonyPatch(typeof(MessageHud), nameof(MessageHud.ShowMessage))]
    internal static class MessageHudBossSpawnMessagePatch
    {
        private static bool Prefix(MessageHud.MessageType type, string text)
        {
            return !BossSpawnMessageSuppression.ShouldSuppress(type, text);
        }
    }

    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
    internal static class ZNetSceneBossSpawnMessagePatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            BossSpawnMessageSuppression.Refresh();
        }
    }
}
