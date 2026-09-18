using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace PraetorisClient.NetworkWardFeature
{
    // Observe the socket boundary, including NPS's direct relay sends that bypass ZRpc.Invoke.
    [HarmonyPatch]
    internal static class NetworkTrafficSendPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ZSteamSocket), "Send", new[] { typeof(ZPackage) });
            yield return AccessTools.Method(typeof(ZPlayFabSocket), "Send", new[] { typeof(ZPackage) });
            yield return AccessTools.Method(typeof(ZSocket), "Send", new[] { typeof(ZPackage) });
        }

        private static void Prefix(ISocket __instance, ZPackage __0)
        {
            if (NetworkTraffic.Active && __instance.IsConnected()) NetworkTraffic.Observe(__0, true);
        }
    }

    [HarmonyPatch(typeof(ZRpc), "HandlePackage")]
    internal static class NetworkTrafficReceivePatch
    {
        private static void Prefix(ZPackage __0) => NetworkTraffic.Observe(__0, false);
    }
}
