using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PraetorisClient.CreatureOwnership
{
    [HarmonyPatch(typeof(Player), "CheckPlacementGhostVSPlayers")]
    internal static class CreatureOwnerWardPlacementPatch
    {
        private static readonly FieldInfo? PlacementGhostField = AccessTools.Field(typeof(Player), "m_placementGhost");

        private static bool Prefix(Player __instance, ref bool __result)
        {
            GameObject? placementGhost = PlacementGhostField?.GetValue(__instance) as GameObject;
            if (placementGhost == null || placementGhost.GetComponent<CreatureOwnerWard>() == null)
            {
                return true;
            }

            __result = false;
            return false;
        }
    }
}
