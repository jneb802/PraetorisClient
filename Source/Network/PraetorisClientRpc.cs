using System;
using PraetorisClient.CreatureOwnership;
using PraetorisClient.ServerChestFeature;
using PraetorisClient.ShipPasswordFeature;

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
            ZRoutedRpc.instance.Register<ZPackage>(RpcNames.CreativeCommandZoneState, CreativeCommandZoneState.OnState);
            ZRoutedRpc.instance.Register<ZPackage>(RpcNames.RpcTraceUploadTokenResponse, RpcTraceUploadTokenClient.OnTokenResponse);
            CreatureOwnerWardRpc.Register(ZRoutedRpc.instance);
            GuardStoneFeature.GuardStoneCountHover.Register(ZRoutedRpc.instance);
            ServerChestRpc.Register(ZRoutedRpc.instance);
            ShipPasswordRpc.Register(ZRoutedRpc.instance);
            ServerGuideFeature.ServerGuide.Register(ZRoutedRpc.instance);
            PraetorisClientPlugin.Log.LogInfo("Registered PraetorisClient RPC handlers.");
        }
    }
}
