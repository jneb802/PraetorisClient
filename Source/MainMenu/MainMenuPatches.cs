using HarmonyLib;
using UnityEngine.UI;

namespace PraetorisClient.MainMenu
{
    [HarmonyPatch(typeof(FejdStartup), "SetupGui")]
    internal static class MainMenuSetupPatch
    {
        private static void Postfix(FejdStartup __instance)
        {
            __instance.m_moddedText.SetActive(false);
            __instance.m_merchStoreButtonParent.SetActive(false);

            // SetupGui links every menu button to the store button on the right.
            // Clear those links so keyboard and controller focus stays on the menu.
            foreach (Button button in __instance.m_menuList.GetComponentsInChildren<Button>(true))
            {
                GuiUtils.SetNavigationRight(button, null);
            }
        }
    }
}
