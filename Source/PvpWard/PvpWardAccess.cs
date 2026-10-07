using HarmonyLib;
using Jotunn.Managers;

namespace PraetorisClient.PvpWardFeature
{
    internal static class PvpWardAccess
    {
        private const string SetEnabledRpc = "PraetorisClient_PvpWardSetEnabled";
        internal static bool LocalIsAdmin => ZNet.instance != null &&
            (ZNet.instance.IsServer() || SynchronizationManager.Instance.PlayerIsAdmin);

        internal static void Register(ZRoutedRpc rpc) => rpc.Register<ZDOID, bool>(SetEnabledRpc, OnSetEnabled);

        internal static bool CanPlace(Player player, Piece piece)
        {
            if (piece.GetComponent<PvpWard>() == null || LocalIsAdmin) return true;
            player.Message(MessageHud.MessageType.Center, "Only admins can place PvP Arena Wards.");
            return false;
        }

        internal static void SetEnabled(ZDOID id, bool enabled)
        {
            if (ZNet.instance.IsServer()) OnSetEnabled(ZNet.GetUID(), id, enabled);
            else ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), SetEnabledRpc, id, enabled);
        }

        private static void OnSetEnabled(long sender, ZDOID id, bool enabled)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null) return;
            ZNetPeer? peer = PlayerResolver.FindPeerBySender(sender);
            if (sender != ZNet.GetUID() && (peer == null || !ZNet.instance.IsAdmin(PlayerResolver.SafeHostName(peer)))) return;
            ZDO ward = ZDOMan.instance.GetZDO(id);
            if (ward == null || ward.GetPrefab() != PvpWardPrefab.Name.GetStableHashCode()) return;
            ward.SetOwner(ZNet.GetUID());
            ward.Set(ZDOVars.s_enabled, enabled);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.PlacePiece))]
    internal static class PvpWardPlacementPatch
    {
        private static bool Prefix(Player __instance, Piece piece) => PvpWardAccess.CanPlace(__instance, piece);
    }

    [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
    internal static class PvpWardTryPlacementPatch
    {
        private static bool Prefix(Player __instance, Piece piece, ref bool __result)
        {
            if (PvpWardAccess.CanPlace(__instance, piece)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Piece), nameof(Piece.CanBeRemoved))]
    internal static class PvpWardRemovalPatch
    {
        private static void Postfix(Piece __instance, ref bool __result)
        {
            if (__instance.GetComponent<PvpWard>() != null && !PvpWardAccess.LocalIsAdmin) __result = false;
        }
    }
}
