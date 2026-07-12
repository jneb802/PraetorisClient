using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace PraetorisClient
{
    [HarmonyPatch]
    internal static class RpcTraceRoutedRpcRegisterNamePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (MethodInfo method in typeof(ZRoutedRpc).GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name == nameof(ZRoutedRpc.Register)
                    && !method.IsGenericMethodDefinition
                    && parameters.Length >= 1
                    && parameters[0].ParameterType == typeof(string))
                    yield return method;
            }
        }

        private static void Prefix(string name, Delegate f)
        {
            RpcTraceTelemetry.RegisterRpcName(name, f);
        }
    }

    [HarmonyPatch]
    internal static class RpcTraceZNetViewRegisterNamePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (MethodInfo method in typeof(ZNetView).GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name == nameof(ZNetView.Register)
                    && !method.IsGenericMethodDefinition
                    && parameters.Length >= 1
                    && parameters[0].ParameterType == typeof(string))
                    yield return method;
            }
        }

        private static void Prefix(string name, Delegate f)
        {
            RpcTraceTelemetry.RegisterRpcName(name, f);
        }
    }

    [HarmonyPatch(typeof(ZRoutedRpc), nameof(ZRoutedRpc.InvokeRoutedRPC), typeof(long), typeof(ZDOID), typeof(string), typeof(object[]))]
    internal static class RpcTraceInvokeNamePatch
    {
        private static void Prefix(string methodName)
        {
            RpcTraceTelemetry.RegisterRpcName(methodName);
        }
    }

    [HarmonyPatch(typeof(ZRoutedRpc), nameof(ZRoutedRpc.InvokeRoutedRPC), typeof(long), typeof(string), typeof(object[]))]
    internal static class RpcTracePeerInvokeNamePatch
    {
        private static void Prefix(string methodName)
        {
            RpcTraceTelemetry.RegisterRpcName(methodName);
        }
    }

    [HarmonyPatch(typeof(ZRoutedRpc), nameof(ZRoutedRpc.InvokeRoutedRPC), typeof(string), typeof(object[]))]
    internal static class RpcTraceServerInvokeNamePatch
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
            if (method == "ZDOData" && parameters != null && parameters.Length > 0 && parameters[0] is ZPackage zdoPackage)
            {
                ClientSocketMetrics.RecordZdoDataPackage(__instance, zdoPackage.Size());
                ZdoTraceTelemetry.TracePackageSend(__instance, zdoPackage);
                return;
            }

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

    [HarmonyPatch(typeof(ZDOMan), "RPC_ZDOData")]
    internal static class RpcTraceZdoDataReceivePatch
    {
        private static void Prefix(ZRpc __0, ZPackage __1)
        {
            ZdoTraceTelemetry.BeginReceive(__0, __1);
        }

        private static void Finalizer()
        {
            ZdoTraceTelemetry.EndReceive();
        }
    }

    [HarmonyPatch(typeof(ZRpc), "HandlePackage")]
    internal static class RpcTraceZdoDataHandlePackagePatch
    {
        private static void Prefix(ZRpc __instance, ZPackage __0)
        {
            ZdoTraceTelemetry.BeginReceiveFromRpcPackage(__instance, __0);
        }

        private static void Finalizer()
        {
            ZdoTraceTelemetry.EndReceive();
        }
    }

    [HarmonyPatch(typeof(ZDO), nameof(ZDO.Deserialize))]
    internal static class RpcTraceZdoDeserializePatch
    {
        private static void Postfix(ZDO __instance)
        {
            ZdoTraceTelemetry.OnDeserializeComplete(__instance);
        }
    }
}
