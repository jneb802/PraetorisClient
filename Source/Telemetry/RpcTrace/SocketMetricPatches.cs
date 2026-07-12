using System;
using System.Reflection;
using HarmonyLib;

namespace PraetorisClient
{
    internal static class SocketMetricPatches
    {
        internal static void ApplyManualPatches(Harmony harmony)
        {
            MethodInfo sendZdosMethod = AccessTools.Method(typeof(ZDOMan), "SendZDOs");
            if (sendZdosMethod == null)
            {
                PraetorisClientPlugin.Log.LogWarning("Could not find ZDOMan.SendZDOs; client socket metric send cadence capture is inactive.");
                return;
            }

            harmony.Patch(
                sendZdosMethod,
                prefix: new HarmonyMethod(typeof(SocketMetricPatches), nameof(SendZdosPrefix)),
                postfix: new HarmonyMethod(typeof(SocketMetricPatches), nameof(SendZdosPostfix)));
        }

        private static void SendZdosPrefix(object __0, bool __1, out SendZdoMetricState __state)
        {
            __state = default;
            try
            {
                ClientSocketMetrics.BeginSendZdos((ZDOMan.ZDOPeer)__0, __1, out __state);
            }
            catch (Exception exception)
            {
                ClientSocketMetrics.LogPatchWarning("SendZDOs client socket metric prefix failed", exception);
            }
        }

        private static void SendZdosPostfix(in SendZdoMetricState __state)
        {
            if (!__state.IsValid)
                return;

            try
            {
                ClientSocketMetrics.EndSendZdos(__state);
            }
            catch (Exception exception)
            {
                ClientSocketMetrics.LogPatchWarning("SendZDOs client socket metric postfix failed", exception);
            }
        }
    }
}
