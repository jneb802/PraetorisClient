using HarmonyLib;

namespace PraetorisClient.ServerGuideFeature
{
    [HarmonyPatch(typeof(TextsDialog), nameof(TextsDialog.UpdateTextsList))]
    internal static class ServerGuideTextsPatch
    {
        private static void Postfix(TextsDialog __instance)
        {
            GuideReader.For(__instance).RegisterPages();
        }
    }

    [HarmonyPatch(typeof(TextsDialog), nameof(TextsDialog.ShowText), typeof(TextsDialog.TextInfo))]
    internal static class ServerGuideShowPatch
    {
        private static void Postfix(TextsDialog __instance, TextsDialog.TextInfo text) => GuideReader.For(__instance).Select(text);
    }
}
