using System;
using HarmonyLib;
using UnityEngine;

namespace PraetorisClient
{
    internal static class SiegePortalBridge
    {
        private const int ProtocolVersion = 2;
        internal const string SiegeIdZdoKey = "valheimCreativeSiegeId";
        internal const string SiegeEntryPositionZdoKey = "valheimCreativeSiegeEntryPosition";
        internal const string SiegeTagPrefix = "siege:";

        internal static bool TryHandle(TeleportWorld portal, Player player)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer() || ZRoutedRpc.instance == null)
            {
                return false;
            }

            if (player == null || Player.m_localPlayer == null || player != Player.m_localPlayer)
            {
                return false;
            }

            ZNetView nview = portal.GetComponent<ZNetView>();
            ZDO? portalZdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            if (portalZdo == null || !TryGetSiegeTarget(portal, portalZdo, out string siegeId, out Vector3 entryPosition))
            {
                return false;
            }

            return RequestSiegeEntry(player, siegeId, entryPosition);
        }

        internal static bool RequestSiegeEntry(Player player, string siegeId, Vector3 entryPosition)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer() || ZRoutedRpc.instance == null)
            {
                return false;
            }

            if (player == null || Player.m_localPlayer == null || player != Player.m_localPlayer)
            {
                return false;
            }

            if (player.m_nview == null || !player.m_nview.IsValid())
            {
                player.Message(MessageHud.MessageType.Center, "Siege portal failed: character was not ready.");
                return true;
            }

            ZDO? playerZdo = player.m_nview.GetZDO();
            if (playerZdo == null)
            {
                player.Message(MessageHud.MessageType.Center, "Siege portal failed: character was not ready.");
                return true;
            }

            ZPackage package = new();
            package.Write(ProtocolVersion);
            package.Write(playerZdo.m_uid);
            package.Write(siegeId);
            package.Write(entryPosition);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcNames.SiegePortalEnter, package);
            player.Message(MessageHud.MessageType.Center, $"Entering siege: {siegeId}");
            PraetorisClientPlugin.Log.LogInfo($"Requested siege portal enter for {siegeId} at entry offset {entryPosition}.");
            return true;
        }

        private static bool TryGetSiegeTarget(TeleportWorld portal, ZDO portalZdo, out string siegeId, out Vector3 entryPosition)
        {
            SiegeGateway gateway = portal.GetComponent<SiegeGateway>();
            if (gateway != null && gateway.TryGetTarget(out siegeId, out entryPosition))
            {
                return true;
            }

            siegeId = portalZdo.GetString(SiegeIdZdoKey).Trim();
            if (!string.IsNullOrWhiteSpace(siegeId))
            {
                entryPosition = portalZdo.GetVec3(SiegeEntryPositionZdoKey, Vector3.zero);
                return true;
            }

            string tag = portalZdo.GetString(ZDOVars.s_tag).Trim();
            if (tag.StartsWith(SiegeTagPrefix, StringComparison.OrdinalIgnoreCase))
            {
                siegeId = tag.Substring(SiegeTagPrefix.Length).Trim();
                entryPosition = portalZdo.GetVec3(SiegeEntryPositionZdoKey, Vector3.zero);
                return !string.IsNullOrWhiteSpace(siegeId);
            }

            entryPosition = Vector3.zero;
            return false;
        }
    }

    [HarmonyPatch(typeof(Teleport), nameof(Teleport.Interact))]
    internal static class TeleportSiegeGatewayPatch
    {
        private static bool Prefix(Teleport __instance, Humanoid character, bool hold, ref bool __result)
        {
            if (hold || character == null || Player.m_localPlayer == null || character != Player.m_localPlayer)
            {
                return true;
            }

            SiegeGateway gateway = __instance.GetComponent<SiegeGateway>();
            if (gateway == null)
            {
                return true;
            }

            __result = gateway.TryEnter(Player.m_localPlayer);
            return false;
        }
    }

    [HarmonyPatch(typeof(TeleportWorld), "Awake")]
    internal static class TeleportWorldSiegeGatewayPatch
    {
        private static void Postfix(TeleportWorld __instance)
        {
            ZNetView nview = __instance.GetComponent<ZNetView>();
            ZDO? zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            if (zdo == null ||
                (!zdo.GetBool("HasFieldsSiegeGateway") &&
                 string.IsNullOrWhiteSpace(zdo.GetString($"{nameof(SiegeGateway)}.m_siegeId")) &&
                 string.IsNullOrWhiteSpace(zdo.GetString(SiegePortalBridge.SiegeIdZdoKey))))
            {
                return;
            }

            SiegeGateway gateway = __instance.GetComponent<SiegeGateway>();
            if (gateway == null)
            {
                gateway = __instance.gameObject.AddComponent<SiegeGateway>();
            }

            gateway.LoadFromZdo(zdo);
        }
    }

    [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Teleport))]
    internal static class TeleportWorldSiegePortalPatch
    {
        private static bool Prefix(TeleportWorld __instance, Player player)
        {
            return !SiegePortalBridge.TryHandle(__instance, player);
        }
    }
}
