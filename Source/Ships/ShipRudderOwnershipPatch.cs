using HarmonyLib;
using PraetorisClient.ShipPasswordFeature;

namespace PraetorisClient
{
    [HarmonyPatch(typeof(ShipControlls), "RPC_RequestRespons")]
    internal static class ShipRudderOwnershipPatch
    {
        private static void Postfix(ShipControlls __instance, bool granted, ZNetView ___m_nview)
        {
            Player player = Player.m_localPlayer;
            if (!granted || player == null || !ReferenceEquals(player.GetDoodadController(), __instance))
            {
                return;
            }

            if (___m_nview != null && ___m_nview.IsValid())
            {
                ___m_nview.ClaimOwnership();
                // The grant can arrive before the previous owner's user value is synchronized.
                ___m_nview.GetZDO().Set(ZDOVars.s_user, player.GetPlayerID());
                // The password grant can also precede its ZDO update. Preserve the
                // local player's saved access when helm use transfers ownership.
                if (ShipPasswordData.IsProtected(___m_nview.GetZDO()))
                {
                    ShipPasswordData.GrantAccess(___m_nview.GetZDO(), player.GetPlayerID());
                }
            }
        }
    }
}
