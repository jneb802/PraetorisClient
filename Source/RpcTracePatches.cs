using HarmonyLib;

namespace PraetorisClient
{
    [HarmonyPatch(typeof(ZRoutedRpc), nameof(ZRoutedRpc.InvokeRoutedRPC), typeof(long), typeof(ZDOID), typeof(string), typeof(object[]))]
    internal static class RpcTraceInvokeNamePatch
    {
        private static void Prefix(string methodName)
        {
            RpcTraceTelemetry.RegisterRpcName(methodName);
        }
    }

    [HarmonyPatch(typeof(ZRpc), nameof(ZRpc.Invoke))]
    internal static class RpcTraceSendPatch
    {
        private static void Prefix(ZRpc __instance, string method, object[] parameters)
        {
            if (method != "RoutedRPC" || parameters == null || parameters.Length == 0 || parameters[0] is not ZPackage package)
                return;

            ZRoutedRpc.RoutedRPCData? data = RpcTraceTelemetry.TryReadRoutedRpcData(package);
            RpcTraceTelemetry.TraceRoutedRpc("rpc_send", data, RpcTraceTelemetry.GetPeerIdForRpc(__instance));
        }
    }

    [HarmonyPatch(typeof(ZRoutedRpc), "RPC_RoutedRPC")]
    internal static class RpcTraceReceivePatch
    {
        private static void Prefix(ZPackage pkg)
        {
            ZRoutedRpc.RoutedRPCData? data = RpcTraceTelemetry.TryReadRoutedRpcData(pkg);
            RpcTraceTelemetry.TraceRoutedRpc("rpc_receive", data, ZDOMan.instance != null ? ZDOMan.GetSessionID() : 0L);
        }
    }

    [HarmonyPatch(typeof(ZRoutedRpc), "HandleRoutedRPC")]
    internal static class RpcTraceHandlePatch
    {
        private static void Prefix(ZRoutedRpc.RoutedRPCData data)
        {
            RpcTraceTelemetry.TraceRoutedRpc("rpc_handle", data, ZDOMan.instance != null ? ZDOMan.GetSessionID() : 0L);
        }
    }
}
