using HarmonyLib;
using UnityEngine;

namespace PraetorisClient.Storage
{
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class PersonalChestSizePatch
    {
        private const string PersonalChestPrefabName = "piece_chest_private";
        private const string WoodChestPrefabName = "piece_chest_wood";

        [HarmonyPostfix]
        private static void Postfix(ZNetScene __instance)
        {
            GameObject? personalChestPrefab = __instance.GetPrefab(PersonalChestPrefabName);
            GameObject? woodChestPrefab = __instance.GetPrefab(WoodChestPrefabName);
            Container? personalChest = personalChestPrefab?.GetComponent<Container>();
            Container? woodChest = woodChestPrefab?.GetComponent<Container>();
            if (personalChest == null || woodChest == null)
            {
                PraetorisClientPlugin.Log.LogWarning("Could not apply the wooden chest inventory size to the personal chest prefab.");
                return;
            }

            if (personalChest.m_width == woodChest.m_width && personalChest.m_height == woodChest.m_height)
            {
                return;
            }

            personalChest.m_width = woodChest.m_width;
            personalChest.m_height = woodChest.m_height;
        }
    }
}
