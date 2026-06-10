using System;

namespace PraetorisClient
{
    internal static class PraetorisClientRpc
    {
        private static ZRoutedRpc? _registeredRpc;

        public static void Register()
        {
            if (ZRoutedRpc.instance == null || ReferenceEquals(_registeredRpc, ZRoutedRpc.instance))
            {
                return;
            }

            _registeredRpc = ZRoutedRpc.instance;
            ZRoutedRpc.instance.Register<ZPackage>(RpcNames.LinkRequest, LinkRpc.OnRequest);
            ZRoutedRpc.instance.Register<ZPackage>(RpcNames.LinkResult, LinkRpc.OnResult);
            ZRoutedRpc.instance.Register<ZPackage>(RpcNames.CreativeInventoryRequest, CreativeInventoryRpc.OnRequest);
            ZRoutedRpc.instance.Register<ZPackage>(RpcNames.CreativeBiomeOverride, CreativeBiomeOverride.OnOverride);
            ZRoutedRpc.instance.Register<ZPackage>(RpcNames.CreativeCommandZoneState, CreativeCommandZoneState.OnState);
            PraetorisClientPlugin.Log.LogInfo("Registered PraetorisClient RPC handlers.");
        }
    }
}
