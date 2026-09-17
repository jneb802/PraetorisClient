using System;
using HarmonyLib;

namespace PraetorisClient
{
    internal static class ServerSyncProtection
    {
        private const string ServerSyncConfigSyncMethodSuffix = " ConfigSync";

        internal static bool ShouldBlockOutgoing(string methodName)
        {
            if (!IsEnabledOnClient || !IsServerSyncConfigSyncMethod(methodName))
            {
                return false;
            }

            PraetorisClientPlugin.Log.LogInfo("Blocked outgoing peer ServerSync config packet: " + methodName);
            return true;
        }

        internal static bool IsEnabledOnClient
        {
            get
            {
                return PraetorisClientPlugin.BlockPeerServerSyncConfigSync?.Value == true
                       && ZNet.instance != null
                       && !ZNet.instance.IsServer();
            }
        }

        private static bool IsServerSyncConfigSyncMethod(string methodName)
        {
            return !string.IsNullOrEmpty(methodName)
                   && methodName.EndsWith(ServerSyncConfigSyncMethodSuffix, StringComparison.Ordinal);
        }
    }

    [HarmonyPatch(typeof(ZRoutedRpc), nameof(ZRoutedRpc.InvokeRoutedRPC), typeof(long), typeof(string), typeof(object[]))]
    internal static class ServerSyncProtectionOutgoingPatch
    {
        private static bool Prefix(string methodName)
        {
            return !ServerSyncProtection.ShouldBlockOutgoing(methodName);
        }
    }
}
