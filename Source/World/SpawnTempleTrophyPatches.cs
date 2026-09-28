using System.Collections.Generic;
using HarmonyLib;

namespace PraetorisClient
{
    internal static class SpawnTempleTrophies
    {
        internal const string BlockedMessage = "You cannot hang trophies on the spawn temple stones.";
        private static readonly List<Location> LoadedLocations =
            AccessTools.StaticFieldRefAccess<List<Location>>(typeof(Location), "s_allLocations");
        private static readonly HashSet<int> VanillaStonePrefabs = new()
        {
            "BossStone_Eikthyr".GetStableHashCode(),
            "BossStone_TheElder".GetStableHashCode(),
            "BossStone_Bonemass".GetStableHashCode(),
            "BossStone_DragonQueen".GetStableHashCode(),
            "BossStone_Yagluth".GetStableHashCode(),
            "BossStone_TheQueen".GetStableHashCode(),
            "BossStone_Fader".GetStableHashCode()
        };

        internal static bool IsProtected(ItemStand stand)
        {
            if (!PraetorisClientPlugin.SpawnTempleTrophiesEnabled.Value)
            {
                return false;
            }

            BossStone stone = stand.GetComponentInParent<BossStone>();
            if (stone == null || stone.m_itemStand != stand || stone.GetComponent<Piece>() != null)
            {
                return false;
            }

            ZNetView view = stone.GetComponent<ZNetView>();
            if (view == null || !view.IsValid())
            {
                return false;
            }

            // Use the registered network prefab, not the child hook's shared name.
            // Reset stones use VRN_BossStone_*; buildable stones use piece_BossStone_*.
            if (!VanillaStonePrefabs.Contains(view.GetZDO().GetPrefab()))
            {
                return false;
            }

            // Networked stones are detached from the location hierarchy after spawning.
            // Loaded Location instances are available on both clients and servers.
            foreach (Location location in LoadedLocations)
            {
                if (location != null && Utils.GetPrefabName(location.gameObject) == "StartTemple"
                    && location.IsInside(stone.transform.position, 0f))
                {
                    return true;
                }
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(ItemStand), nameof(ItemStand.UseItem))]
    internal static class SpawnTempleTrophyUseItemPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ItemStand __instance, Humanoid user, ref bool __result)
        {
            if (!SpawnTempleTrophies.IsProtected(__instance) || __instance.HaveAttachment())
            {
                return true;
            }

            user.Message(MessageHud.MessageType.Center, SpawnTempleTrophies.BlockedMessage);
            // Handle the action without queuing an attachment or consuming the item.
            // Interact's automatic attachment also enters this method.
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(ItemStand), nameof(ItemStand.GetHoverText))]
    internal static class SpawnTempleTrophyHoverPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ItemStand __instance, ref string __result)
        {
            if (Player.m_localPlayer != null && !__instance.HaveAttachment()
                && SpawnTempleTrophies.IsProtected(__instance))
            {
                __result = SpawnTempleTrophies.BlockedMessage;
            }
        }
    }
}
