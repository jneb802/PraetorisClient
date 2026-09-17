using EpicLoot.Adventure;
using HarmonyLib;

namespace PraetorisClient.EpicLootFeature
{
    [HarmonyPatch(typeof(TreasureMapChest), nameof(TreasureMapChest.Reinitialize))]
    internal static class EpicLootTreasureChestReinitializePatch
    {
        private static void Postfix(TreasureMapChest __instance)
        {
            Container container = __instance.GetComponent<Container>();
            if (container == null || !container.m_checkGuardStone)
            {
                return;
            }

            container.m_checkGuardStone = false;
        }
    }
}
