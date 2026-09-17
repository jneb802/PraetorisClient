using HarmonyLib;
using UnityEngine;

namespace PraetorisClient.ServerGuideFeature
{
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Awake))]
    internal static class ServerGuideInventoryPatch
    {
        private static void Postfix(InventoryGui __instance) => GuideWindow.Create(__instance);
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Update))]
    internal static class ServerGuideClosePatch
    {
        private static bool Prefix()
        {
            if (GuideWindow.Reader == null || !GuideWindow.Reader.gameObject.activeInHierarchy) return true;
            if (!ZInput.GetKeyDown(KeyCode.Escape) && !ZInput.GetButtonDown("JoyButtonB")) return true;
            GuideWindow.Reader.Close();
            ZInput.ResetButtonStatus("JoyButtonB");
            return false;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
    internal static class ServerGuideHidePatch
    {
        private static void Postfix() => GuideWindow.Reader?.Close();
    }
}
