using HarmonyLib;

namespace PraetorisClient
{
    [HarmonyPatch(typeof(ZRpc), nameof(ZRpc.Invoke))]
    internal static class RpcTraceSendPatch
    {
        private static void Prefix(ZRpc __instance, string method, object[] parameters)
        {
            if (method == "ZDOData" && parameters != null && parameters.Length > 0 && parameters[0] is ZPackage zdoPackage)
                ClientSocketMetrics.RecordZdoDataPackage(__instance, zdoPackage.Size());
        }
    }
}
