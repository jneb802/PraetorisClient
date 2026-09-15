using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;

namespace PraetorisClient
{
    internal static class CraftyBoxesWardGuard
    {
        private const string CraftyBoxesGuid = "Azumatt.AzuCraftyBoxes";
        private const string ProtectiveWardsGuid = "shudnal.ProtectiveWards";
        private const string VanillaContainerTypeName = "AzuCraftyBoxes.IContainers.VanillaContainer";
        private const string ProtectiveWardsTypeName = "ProtectiveWards.ProtectiveWards";

        private delegate bool IsActivePlayerWardDelegate(PrivateArea ward);
        private delegate bool IsPointInsideWardAreaDelegate(PrivateArea ward, Vector3 point, float radius);
        private delegate bool HasWardAccessDelegate(PrivateArea ward, Player player);

        private static Func<object, Vector3>? _getContainerPosition;
        private static IsActivePlayerWardDelegate? _isActivePlayerWard;
        private static IsPointInsideWardAreaDelegate? _isPointInsideWardArea;
        private static HasWardAccessDelegate? _hasWardAccess;
        private static FieldInfo? _protectChestsField;
        private static ConfigEntry<bool>? _protectChestsConfig;
        private static bool _runtimeFailureLogged;

        internal static void TryApply(Harmony harmony)
        {
            if (!Chainloader.PluginInfos.ContainsKey(CraftyBoxesGuid) ||
                !Chainloader.PluginInfos.ContainsKey(ProtectiveWardsGuid))
            {
                return;
            }

            try
            {
                Type vanillaContainerType = AccessTools.TypeByName(VanillaContainerTypeName)
                    ?? throw new TypeLoadException("Could not find " + VanillaContainerTypeName + ".");
                Type protectiveWardsType = AccessTools.TypeByName(ProtectiveWardsTypeName)
                    ?? throw new TypeLoadException("Could not find " + ProtectiveWardsTypeName + ".");

                MethodInfo getPositionMethod = AccessTools.DeclaredMethod(vanillaContainerType, "GetPosition")
                    ?? throw new MissingMethodException(VanillaContainerTypeName, "GetPosition");
                _getContainerPosition = CreatePositionGetter(vanillaContainerType, getPositionMethod);

                MethodInfo isActiveMethod = AccessTools.DeclaredMethod(
                    protectiveWardsType,
                    "IsActivePlayerWard",
                    new[] { typeof(PrivateArea) })
                    ?? throw new MissingMethodException(ProtectiveWardsTypeName, "IsActivePlayerWard");
                MethodInfo isInsideMethod = AccessTools.DeclaredMethod(
                    protectiveWardsType,
                    "IsPointInsideWardArea",
                    new[] { typeof(PrivateArea), typeof(Vector3), typeof(float) })
                    ?? throw new MissingMethodException(ProtectiveWardsTypeName, "IsPointInsideWardArea");
                MethodInfo hasAccessMethod = AccessTools.DeclaredMethod(
                    protectiveWardsType,
                    "HasAccessToWardOrConnectedWard",
                    new[] { typeof(PrivateArea), typeof(Player) })
                    ?? throw new MissingMethodException(ProtectiveWardsTypeName, "HasAccessToWardOrConnectedWard");

                _isActivePlayerWard = (IsActivePlayerWardDelegate)Delegate.CreateDelegate(typeof(IsActivePlayerWardDelegate), isActiveMethod);
                _isPointInsideWardArea = (IsPointInsideWardAreaDelegate)Delegate.CreateDelegate(typeof(IsPointInsideWardAreaDelegate), isInsideMethod);
                _hasWardAccess = (HasWardAccessDelegate)Delegate.CreateDelegate(typeof(HasWardAccessDelegate), hasAccessMethod);
                _protectChestsField = AccessTools.Field(protectiveWardsType, "wardAccessProtectChests")
                    ?? throw new MissingFieldException(ProtectiveWardsTypeName, "wardAccessProtectChests");

                Patch(harmony, vanillaContainerType, "ItemCount", nameof(BlockItemCountPrefix));
                Patch(harmony, vanillaContainerType, "ProcessContainerInventory", nameof(BlockProcessContainerInventoryPrefix));
                Patch(harmony, vanillaContainerType, "RemoveItem", nameof(BlockVoidAccessPrefix));
                Patch(harmony, vanillaContainerType, "GetInventory", nameof(BlockGetInventoryPrefix));

                PraetorisClientPlugin.Log.LogInfo("AzuCraftyBoxes access to Protective Wards chests is guarded.");
            }
            catch (Exception ex)
            {
                PraetorisClientPlugin.Log.LogWarning("Could not enable the AzuCraftyBoxes Protective Wards guard: " + ex.Message);
            }
        }

        private static void Patch(Harmony harmony, Type targetType, string targetMethodName, string prefixMethodName)
        {
            MethodInfo target = AccessTools.DeclaredMethod(targetType, targetMethodName)
                ?? throw new MissingMethodException(targetType.FullName, targetMethodName);
            MethodInfo prefix = AccessTools.DeclaredMethod(typeof(CraftyBoxesWardGuard), prefixMethodName)
                ?? throw new MissingMethodException(typeof(CraftyBoxesWardGuard).FullName, prefixMethodName);
            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
        }

        private static Func<object, Vector3> CreatePositionGetter(Type containerType, MethodInfo getPositionMethod)
        {
            ParameterExpression instance = Expression.Parameter(typeof(object), "instance");
            MethodCallExpression call = Expression.Call(Expression.Convert(instance, containerType), getPositionMethod);
            return Expression.Lambda<Func<object, Vector3>>(call, instance).Compile();
        }

        private static bool BlockItemCountPrefix(object __instance, ref int __result)
        {
            if (!ShouldBlock(__instance))
            {
                return true;
            }

            __result = 0;
            return false;
        }

        private static bool BlockProcessContainerInventoryPrefix(object __instance, int totalAmount, ref int __result)
        {
            if (!ShouldBlock(__instance))
            {
                return true;
            }

            __result = totalAmount;
            return false;
        }

        private static bool BlockVoidAccessPrefix(object __instance)
        {
            return !ShouldBlock(__instance);
        }

        private static bool BlockGetInventoryPrefix(object __instance, ref Inventory __result)
        {
            if (!ShouldBlock(__instance))
            {
                return true;
            }

            __result = null!;
            return false;
        }

        private static bool ShouldBlock(object container)
        {
            if (!PraetorisClientPlugin.ProtectCraftyBoxesWardChests.Value)
            {
                return false;
            }

            Player player = Player.m_localPlayer;
            if (player == null || !ProtectiveWardsProtectChests())
            {
                return false;
            }

            try
            {
                Vector3 position = _getContainerPosition!(container);
                foreach (PrivateArea ward in PrivateArea.m_allAreas)
                {
                    if (_isActivePlayerWard!(ward) &&
                        _isPointInsideWardArea!(ward, position, 0f) &&
                        !_hasWardAccess!(ward, player))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                if (!_runtimeFailureLogged)
                {
                    _runtimeFailureLogged = true;
                    PraetorisClientPlugin.Log.LogWarning("The AzuCraftyBoxes Protective Wards access check failed: " + ex.Message);
                }
            }

            return false;
        }

        private static bool ProtectiveWardsProtectChests()
        {
            if (_protectChestsConfig == null)
            {
                _protectChestsConfig = _protectChestsField?.GetValue(null) as ConfigEntry<bool>;
            }

            return _protectChestsConfig?.Value == true;
        }
    }
}
