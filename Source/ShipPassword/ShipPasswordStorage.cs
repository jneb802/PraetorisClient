using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;

namespace PraetorisClient.ShipPasswordFeature
{
    internal static class ShipPasswordStorage
    {
        internal static Ship? GetShip(Container container)
        {
            Ship? ship = container.GetComponentInParent<Ship>();
            return ship != null ? ship : container.m_rootObjectOverride != null
                ? container.m_rootObjectOverride.GetComponent<Ship>() : null;
        }

        internal static ZDO? GetShipZdo(Container container)
        {
            Ship? ship = GetShip(container);
            ZNetView? nview = ship != null ? ship.GetComponent<ZNetView>() : null;
            return nview != null && nview.IsValid() ? nview.GetZDO() : null;
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.Interact))]
    internal static class ShipPasswordStorageInteractPatch
    {
        private static bool Prefix(Container __instance, Humanoid character, bool hold, ref bool __result)
        {
            ZDO? zdo = ShipPasswordStorage.GetShipZdo(__instance);
            if (!ShipPasswordData.IsProtected(zdo))
            {
                return true;
            }

            Player? player = character as Player;
            if (player != null && ShipPasswordData.HasAccess(zdo, player.GetPlayerID()))
            {
                return true;
            }

            __result = false;
            if (!hold && player != null &&
                (!__instance.m_checkGuardStone || PrivateArea.CheckAccess(__instance.transform.position)))
            {
                Ship? ship = ShipPasswordStorage.GetShip(__instance);
                ShipPasswordInput? input = ship != null && ship.m_shipControlls != null
                    ? ship.m_shipControlls.GetComponent<ShipPasswordInput>() : null;
                input?.BeginStoragePassword(player, __instance);
            }

            return false;
        }
    }

    // Vanilla open, stack, and take-all requests all call CheckAccess on the owner.
    // Keeping the check here also covers direct requests that do not show the prompt.
    [HarmonyPatch(typeof(Container), "CheckAccess")]
    internal static class ShipPasswordStorageAccessPatch
    {
        private static void Postfix(Container __instance, long playerID, ref bool __result)
        {
            ZDO? zdo = ShipPasswordStorage.GetShipZdo(__instance);
            if (ShipPasswordData.IsProtected(zdo) && !ShipPasswordData.HasAccess(zdo, playerID))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch]
    internal static class ShipPasswordStorageSenderPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Container), "RPC_RequestOpen");
            yield return AccessTools.Method(typeof(Container), "RPC_RequestStack");
            yield return AccessTools.Method(typeof(Container), "RPC_RequestTakeAll");
        }

        private static bool Prefix(Container __instance, long uid, long playerID)
        {
            if (!ShipPasswordData.IsProtected(ShipPasswordStorage.GetShipZdo(__instance)))
            {
                return true;
            }

            // A remote caller must not borrow another character's saved access.
            // Other clients are routed through the server, so resolve the nearby
            // character's ZDO owner rather than requiring a direct network peer.
            foreach (Player player in Player.GetAllPlayers())
            {
                if (player.GetPlayerID() == playerID)
                {
                    ZDO? character = ZDOMan.instance.GetZDO(player.GetZDOID());
                    return character != null && character.GetOwner() == uid;
                }
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
    internal static class ShipPasswordStorageHoverPatch
    {
        private static void Postfix(Container __instance, ref string __result)
        {
            if (ShipPasswordData.IsProtected(ShipPasswordStorage.GetShipZdo(__instance)))
            {
                __result += "\nPassword protected";
            }
        }
    }
}
