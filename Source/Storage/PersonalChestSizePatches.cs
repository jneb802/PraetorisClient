using HarmonyLib;

namespace PraetorisClient.Storage
{
    [HarmonyPatch(typeof(Container), "Awake")]
    internal static class PersonalChestSizePatch
    {
        private const string PersonalChestPrefabName = "piece_personal_chest";
        private const int WoodChestColumns = 5;
        private const int WoodChestRows = 2;

        [HarmonyPrefix]
        private static void Prefix(Container __instance)
        {
            if (Utils.GetPrefabName(__instance.gameObject) != PersonalChestPrefabName)
            {
                return;
            }

            __instance.m_width = WoodChestColumns;
            __instance.m_height = WoodChestRows;
        }
    }
}
