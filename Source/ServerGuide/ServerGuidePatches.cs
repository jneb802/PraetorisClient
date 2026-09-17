using HarmonyLib;

namespace PraetorisClient.ServerGuideFeature
{
    [HarmonyPatch(typeof(TextsDialog), nameof(TextsDialog.UpdateTextsList))]
    internal static class ServerGuideTextsPatch
    {
        private static void Postfix(TextsDialog __instance)
        {
            for (int index = ServerGuide.Pages.Count - 1; index >= 0; index--)
            {
                GuidePage page = ServerGuide.Pages[index];
                __instance.m_texts.Insert(0, new TextsDialog.TextInfo("Server Guide: " + page.Title, page.Body));
            }
        }
    }
}
