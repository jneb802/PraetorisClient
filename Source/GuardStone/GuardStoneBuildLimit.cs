using System.Collections.Generic;
using HarmonyLib;

namespace PraetorisClient.GuardStoneFeature
{
    internal static class GuardStoneBuildLimit
    {
        private const string PrefabName = "guard_stone";
        private static readonly int PrefabHash = PrefabName.GetStableHashCode();
        private static readonly AccessTools.FieldRef<ZDOMan, Dictionary<ZDOID, ZDO>> ObjectsById =
            AccessTools.FieldRefAccess<ZDOMan, Dictionary<ZDOID, ZDO>>("m_objectsByID");

        internal static bool IsGuardStone(Piece piece)
        {
            return piece != null && Utils.GetPrefabName(piece.gameObject) == PrefabName;
        }

        internal static int CountLoadedGuardStones(long creatorId)
        {
            int count = 0;
            foreach (PrivateArea privateArea in PrivateArea.m_allAreas)
            {
                Piece? piece = privateArea != null ? privateArea.GetComponent<Piece>() : null;
                if (piece != null && IsGuardStone(piece) && piece.GetCreator() == creatorId)
                {
                    count++;
                }
            }

            return count;
        }

        internal static void EnforceServerLimit(ZDO zdo)
        {
            if (zdo == null || zdo.GetPrefab() != PrefabHash || ZDOMan.instance == null)
            {
                return;
            }

            int limit = PraetorisClientPlugin.GuardStonePlayerBuildLimit.Value;
            long creatorId = zdo.GetLong(ZDOVars.s_creator, 0L);
            if (creatorId == 0L)
            {
                Reject(zdo, creatorId, 0, limit, "missing creator");
                return;
            }

            int count = CountWorldGuardStones(creatorId);
            if (count > limit)
            {
                Reject(zdo, creatorId, count, limit, "player limit exceeded");
            }
        }

        internal static int CountWorldGuardStones(long creatorId)
        {
            int count = 0;
            Dictionary<ZDOID, ZDO> objectsById = ObjectsById(ZDOMan.instance);
            foreach (ZDO candidate in objectsById.Values)
            {
                if (candidate.GetPrefab() == PrefabHash && candidate.GetLong(ZDOVars.s_creator, 0L) == creatorId)
                {
                    count++;
                }
            }

            return count;
        }

        private static void Reject(ZDO zdo, long creatorId, int count, int limit, string reason)
        {
            zdo.SetOwner(ZDOMan.GetSessionID());
            ZDOMan.instance.DestroyZDO(zdo);
            PraetorisClientPlugin.Log.LogWarning(
                $"Rejected new {PrefabName} ZDO {zdo.m_uid} for creator {creatorId}: {reason}; count={count}, limit={limit}.");
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
    internal static class GuardStoneTryPlacePiecePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Player __instance, Piece piece, ref bool __result)
        {
            if (!GuardStoneBuildLimit.IsGuardStone(piece))
            {
                return true;
            }

            int limit = PraetorisClientPlugin.GuardStonePlayerBuildLimit.Value;
            int currentCount = GuardStoneBuildLimit.CountLoadedGuardStones(__instance.GetPlayerID());
            if (currentCount < limit)
            {
                return true;
            }

            __instance.Message(MessageHud.MessageType.Center, $"Guard stone limit reached ({limit}).");
            __result = false;
            return false;
        }
    }

    internal static class GuardStoneZdoReceiveContext
    {
        internal static int Depth;
    }

    [HarmonyPatch(typeof(ZDOMan), "RPC_ZDOData")]
    internal static class GuardStoneZdoDataPatch
    {
        [HarmonyPrefix]
        private static void Prefix(out bool __state)
        {
            __state = ZNet.instance != null && ZNet.instance.IsServer();
            if (__state)
            {
                GuardStoneZdoReceiveContext.Depth++;
            }
        }

        [HarmonyFinalizer]
        private static System.Exception? Finalizer(System.Exception? __exception, bool __state)
        {
            if (__state)
            {
                GuardStoneZdoReceiveContext.Depth--;
            }

            return __exception;
        }
    }

    [HarmonyPatch(typeof(ZDO), nameof(ZDO.Deserialize))]
    internal static class GuardStoneZdoDeserializePatch
    {
        [HarmonyPrefix]
        private static void Prefix(ZDO __instance, out bool __state)
        {
            __state = GuardStoneZdoReceiveContext.Depth > 0 && __instance.GetPrefab() == 0;
        }

        [HarmonyPostfix]
        private static void Postfix(ZDO __instance, bool __state)
        {
            if (__state)
            {
                GuardStoneBuildLimit.EnforceServerLimit(__instance);
            }
        }
    }
}
