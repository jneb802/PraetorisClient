using System;
using System.Collections.Generic;
using HarmonyLib;

namespace PraetorisClient
{
    internal static class ProtectedLocationNoBuild
    {
        private static readonly HashSet<string> ProtectedPrefabs = new(StringComparer.Ordinal)
        {
            "Hildir_crypt",
            "Crypt4",
            "Hildir_cave",
            "Hildir_plainsfortress",
            "SunkenCrypt4",
            "Crypt3",
            "TrollCave02",
            "Crypt2",
            "Mistlands_DvergrTownEntrance1",
            "Mistlands_DvergrTownEntrance2"
        };

        internal static void ApplyToLocation(Location location)
        {
            if (location == null)
            {
                return;
            }

            string prefabName = Utils.GetPrefabName(location.gameObject);
            if (ProtectedPrefabs.Contains(prefabName))
            {
                location.m_noBuild = true;
            }
        }

        internal static void ApplyToLoadedLocations()
        {
            foreach (Location location in Location.s_allLocations)
            {
                ApplyToLocation(location);
            }
        }
    }

    [HarmonyPatch(typeof(Location), "Awake")]
    internal static class ProtectedLocationNoBuildAwakePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Location __instance)
        {
            ProtectedLocationNoBuild.ApplyToLocation(__instance);
        }
    }
}
