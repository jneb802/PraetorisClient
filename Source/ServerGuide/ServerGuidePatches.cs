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
            if (ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyButtonB"))
            {
                GuideWindow.Reader.Escape();
                ZInput.ResetButtonStatus("JoyButtonB");
                return false;
            }
            // Typed search letters must not trigger inventory shortcuts such as Use or Inventory.
            if (GuideWindow.Reader.SearchFocused)
            {
                ZInput.ResetButtonStatus("Inventory");
                ZInput.ResetButtonStatus("Use");
                ZInput.ResetButtonStatus("JoyButtonY");
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
    internal static class ServerGuideHidePatch
    {
        private static void Postfix() => GuideWindow.Reader?.Close();
    }
}
