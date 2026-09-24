using System;
using System.Collections.Generic;
using HarmonyLib;

namespace PraetorisClient.CreatorHoverFeature
{
    [HarmonyPatch(typeof(Piece), nameof(Piece.SetCreator))]
    internal static class VehicleCreatorNamePatch
    {
        private static void Prefix(Piece __instance, out bool __state)
        {
            __state = __instance.GetCreator() == 0L;
        }

        private static void Postfix(Piece __instance, long uid, bool __state)
        {
            Player builder = Player.m_localPlayer;
            if (!__state || uid == 0L || __instance.GetCreator() != uid ||
                builder == null || builder.GetPlayerID() != uid ||
                (__instance.GetComponent<Ship>() == null && __instance.GetComponent<Vagon>() == null))
            {
                return;
            }

            ZNetView view = __instance.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || !view.IsOwner())
            {
                return;
            }

            string characterName = builder.GetPlayerName();
            if (!string.IsNullOrWhiteSpace(characterName))
            {
                view.GetZDO().Set(CreatorHoverText.CreatorNameHash, characterName);
            }
        }
    }

    [HarmonyPatch(typeof(ShipControlls), nameof(ShipControlls.GetHoverText))]
    internal static class ShipCreatorHoverPatch
    {
        private static void Postfix(ShipControlls __instance, ref string __result)
        {
            Piece piece = __instance.GetComponentInParent<Piece>();
            CreatorHoverText.Append(piece, ref __result);
        }
    }

    [HarmonyPatch(typeof(Vagon), nameof(Vagon.GetHoverText))]
    internal static class VagonCreatorHoverPatch
    {
        private static void Postfix(Vagon __instance, ref string __result)
        {
            Piece piece = __instance.GetComponentInParent<Piece>();
            CreatorHoverText.Append(piece, ref __result);
        }
    }

    internal static class CreatorHoverText
    {
        internal static readonly int CreatorNameHash = "praetoris_creatorName".GetStableHashCode();

        internal static void Append(Piece piece, ref string hoverText)
        {
            if (piece == null || piece.GetCreator() == 0L)
            {
                return;
            }

            string creatorName = ResolveCreatorName(piece);
            string ownerLabel = Localization.instance.Localize("$piece_guardstone_owner");
            hoverText += "\n" + ownerLabel + ": " + creatorName;
        }

        private static string ResolveCreatorName(Piece piece)
        {
            long creatorId = piece.GetCreator();
            ZNetView view = piece.GetComponent<ZNetView>();
            if (view != null && view.IsValid())
            {
                string savedName = view.GetZDO().GetString(CreatorNameHash);
                if (!string.IsNullOrWhiteSpace(savedName))
                {
                    return CensorShittyWords.FilterUGC(savedName, UGCType.CharacterName, creatorId);
                }
            }

            Player creator = Player.GetPlayer(creatorId);
            if (creator != null)
            {
                string characterName = creator.GetPlayerName();
                if (!string.IsNullOrWhiteSpace(characterName))
                {
                    return CensorShittyWords.FilterUGC(characterName, UGCType.CharacterName, creatorId);
                }
            }

            int creatorIndex = piece.GetCreatorPlatformUserIdIndex();
            if (ZNet.World == null || ZNet.World.m_playerHistory == null)
            {
                return Localization.instance.Localize("$build_piece_author_unknown");
            }

            List<ZNet.CrossNetworkUserInfo> playerHistory = ZNet.World.m_playerHistory;
            if (creatorIndex >= 0 && creatorIndex < playerHistory.Count)
            {
                ZNet.CrossNetworkUserInfo creatorInfo = playerHistory[creatorIndex];
                string displayName = string.IsNullOrWhiteSpace(creatorInfo.m_serverAssignedDisplayName)
                    ? creatorInfo.m_displayName
                    : creatorInfo.m_serverAssignedDisplayName;
                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    return CensorShittyWords.FilterUGC(displayName, UGCType.CharacterName, creatorInfo.m_id);
                }
            }

            return Localization.instance.Localize("$build_piece_author_unknown");
        }
    }
}
