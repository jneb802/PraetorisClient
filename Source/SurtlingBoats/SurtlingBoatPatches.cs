using HarmonyLib;
using UnityEngine;

namespace PraetorisClient.SurtlingBoats
{
    [HarmonyPatch(typeof(Ship))]
    internal static class SurtlingBoatPatches
    {
        [HarmonyPostfix]
        [HarmonyPatch("Awake")]
        private static void ShipAwakePostfix(Ship __instance)
        {
            ZNetView? netView = __instance.GetComponent<ZNetView>();
            if (netView != null)
            {
                netView.Register<long>(SurtlingBoatFeature.ToggleRpcName,
                    (sender, playerId) => SurtlingBoatFeature.HandleToggle(__instance, playerId));
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Ship.CustomFixedUpdate))]
        private static void ShipCustomFixedUpdatePostfix(Ship __instance, float fixedDeltaTime)
        {
            if (!PraetorisClientPlugin.SurtlingBoatsEnabled.Value || !SurtlingBoatFeature.IsEnabled(__instance) || !__instance.IsOwner())
            {
                return;
            }

            float boost = SurtlingBoatFeature.GetBoost(__instance.GetSpeedSetting());
            if (boost <= 0f || !SurtlingBoatFeature.TryConsumeFuel(__instance, out float fuelSeconds))
            {
                return;
            }

            Rigidbody? body = __instance.GetComponent<Rigidbody>();
            if (body == null)
            {
                return;
            }

            float direction = __instance.GetSpeedSetting() == Ship.Speed.Back ? -1f : 1f;
            float rudderAmount = Mathf.Clamp01(Mathf.Abs(__instance.GetRudderValue()));
            float rudderFactor = Mathf.Lerp(1f, 0.25f, rudderAmount);
            Vector3 force = direction * __instance.transform.forward * (__instance.m_backwardForce * boost) * rudderFactor * fixedDeltaTime;
            Vector3 forcePosition = __instance.transform.position + __instance.transform.forward * __instance.m_stearForceOffset;
            body.AddForceAtPosition(force, forcePosition, ForceMode.Impulse);

            if (!PraetorisClientPlugin.SurtlingBoatFreeFuel.Value)
            {
                SurtlingBoatFeature.SetFuelSeconds(__instance, fuelSeconds - fixedDeltaTime);
            }
        }
    }
}
