using System;
using System.Reflection;
using BepInEx.Bootstrap;

namespace PraetorisClient
{
    internal static class EpicLootApiBridge
    {
        internal const string PluginGuid = "randyknapp.mods.epicloot";
        private const string ApiTypeName = "EpicLoot.API, EpicLoot";

        private static bool _initialized;
        private static Type? _apiType;
        private static MethodInfo? _addMagicEffect;
        private static MethodInfo? _registerMagicEffectRequirement;
        private static MethodInfo? _getTotalActiveMagicEffectValue;

        internal static bool TryRegisterMagicEffectRequirement(
            string customFlag,
            Func<ItemDrop.ItemData, object, string, bool, bool, bool, bool> requirement)
        {
            if (!TryInitialize())
            {
                return false;
            }

            try
            {
                object? result = _registerMagicEffectRequirement?.Invoke(null, new object[] { customFlag, requirement });
                return result is bool registered && registered;
            }
            catch (Exception ex)
            {
                PraetorisClientPlugin.Log.LogWarning("Epic Loot API RegisterMagicEffectRequirement failed: " + GetExceptionMessage(ex));
                return false;
            }
        }

        internal static bool TryAddMagicEffect(string json, out string key)
        {
            key = string.Empty;
            if (!TryInitialize())
            {
                return false;
            }

            try
            {
                object? result = _addMagicEffect?.Invoke(null, new object[] { json });
                key = result as string ?? string.Empty;
                return !string.IsNullOrWhiteSpace(key);
            }
            catch (Exception ex)
            {
                PraetorisClientPlugin.Log.LogWarning("Epic Loot API AddMagicEffect failed: " + GetExceptionMessage(ex));
                return false;
            }
        }

        internal static float GetTotalActiveMagicEffectValue(
            Player? player,
            ItemDrop.ItemData item,
            string effectType,
            float scale)
        {
            if (item == null || !TryInitialize())
            {
                return 0f;
            }

            try
            {
                object? result = _getTotalActiveMagicEffectValue?.Invoke(
                    null,
                    new object?[] { player, item, effectType, scale });
                return result == null ? 0f : Convert.ToSingle(result);
            }
            catch (Exception ex)
            {
                PraetorisClientPlugin.Log.LogWarning("Epic Loot API GetTotalActiveMagicEffectValue failed: " + GetExceptionMessage(ex));
                return 0f;
            }
        }

        private static string GetExceptionMessage(Exception ex)
        {
            Exception baseException = ex.GetBaseException();
            return baseException == ex ? ex.Message : ex.Message + " Base exception: " + baseException.Message;
        }

        private static bool TryInitialize()
        {
            if (_initialized)
            {
                return _apiType != null &&
                    _addMagicEffect != null &&
                    _registerMagicEffectRequirement != null &&
                    _getTotalActiveMagicEffectValue != null;
            }

            _initialized = true;

            if (!Chainloader.PluginInfos.ContainsKey(PluginGuid))
            {
                PraetorisClientPlugin.Log.LogInfo("Epic Loot is not loaded; Praetoris magic effects were not registered.");
                return false;
            }

            _apiType = Type.GetType(ApiTypeName);
            if (_apiType == null)
            {
                PraetorisClientPlugin.Log.LogWarning("Epic Loot is loaded, but EpicLoot.API could not be resolved.");
                return false;
            }

            _addMagicEffect = _apiType.GetMethod("AddMagicEffect", BindingFlags.Public | BindingFlags.Static);
            _registerMagicEffectRequirement = _apiType.GetMethod(
                "RegisterMagicEffectRequirement",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string), typeof(Func<ItemDrop.ItemData, object, string, bool, bool, bool, bool>) },
                null);
            _getTotalActiveMagicEffectValue = _apiType.GetMethod(
                "GetTotalActiveMagicEffectValue",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(Player), typeof(ItemDrop.ItemData), typeof(string), typeof(float) },
                null);

            if (_addMagicEffect == null || _registerMagicEffectRequirement == null || _getTotalActiveMagicEffectValue == null)
            {
                PraetorisClientPlugin.Log.LogWarning("Epic Loot API is missing a required magic effect method.");
                return false;
            }

            return true;
        }
    }
}
