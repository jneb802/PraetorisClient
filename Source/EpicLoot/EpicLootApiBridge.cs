using System;
using System.Collections.Generic;
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
        private static MethodInfo? _registerProxyAbility;
        private static MethodInfo? _registerMagicEffectRequirement;
        private static MethodInfo? _getTotalActiveMagicEffectValue;
        private static MethodInfo? _getTotalActiveMagicEffectValueForWeapon;
        private static MethodInfo? _getTotalPlayerActiveMagicEffectValue;
        private static MethodInfo? _playerHasActiveMagicEffect;

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

        internal static bool TryRegisterProxyAbility(string json, Dictionary<string, Delegate> delegates, out string key)
        {
            key = string.Empty;
            if (!TryInitialize() || _registerProxyAbility == null)
            {
                return false;
            }

            try
            {
                object? result = _registerProxyAbility.Invoke(null, new object[] { json, delegates });
                key = result as string ?? string.Empty;
                return !string.IsNullOrWhiteSpace(key);
            }
            catch (Exception ex)
            {
                PraetorisClientPlugin.Log.LogWarning("Epic Loot API RegisterProxyAbility failed: " + GetExceptionMessage(ex));
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

        internal static float GetTotalActiveMagicEffectValueForWeapon(
            Player? player,
            ItemDrop.ItemData item,
            string effectType,
            float scale = 1f)
        {
            if (item == null || !TryInitialize())
            {
                return 0f;
            }

            MethodInfo? method = _getTotalActiveMagicEffectValueForWeapon ?? _getTotalActiveMagicEffectValue;
            if (method == null)
            {
                return 0f;
            }

            try
            {
                object? result = method.Invoke(null, new object?[] { player, item, effectType, scale });
                return result == null ? 0f : Convert.ToSingle(result);
            }
            catch (Exception ex)
            {
                PraetorisClientPlugin.Log.LogWarning("Epic Loot API GetTotalActiveMagicEffectValueForWeapon failed: " + GetExceptionMessage(ex));
                return 0f;
            }
        }

        internal static float GetTotalPlayerActiveMagicEffectValue(
            Player player,
            string effectType,
            float scale = 1f,
            ItemDrop.ItemData? ignoreThisItem = null)
        {
            if (player == null || !TryInitialize() || _getTotalPlayerActiveMagicEffectValue == null)
            {
                return 0f;
            }

            try
            {
                object? result = _getTotalPlayerActiveMagicEffectValue.Invoke(
                    null,
                    new object?[] { player, effectType, scale, ignoreThisItem });
                return result == null ? 0f : Convert.ToSingle(result);
            }
            catch (Exception ex)
            {
                PraetorisClientPlugin.Log.LogWarning("Epic Loot API GetTotalPlayerActiveMagicEffectValue failed: " + GetExceptionMessage(ex));
                return 0f;
            }
        }

        internal static bool PlayerHasActiveMagicEffect(
            Player player,
            string effectType,
            out float effectValue,
            float scale = 1f,
            ItemDrop.ItemData? ignoreThisItem = null)
        {
            effectValue = 0f;
            if (player == null || !TryInitialize() || _playerHasActiveMagicEffect == null)
            {
                return false;
            }

            try
            {
                object?[] parameters = { player, effectType, effectValue, scale, ignoreThisItem };
                object? result = _playerHasActiveMagicEffect.Invoke(null, parameters);
                effectValue = parameters[2] == null ? 0f : Convert.ToSingle(parameters[2]);
                return result is bool active && active;
            }
            catch (Exception ex)
            {
                PraetorisClientPlugin.Log.LogWarning("Epic Loot API PlayerHasActiveMagicEffect failed: " + GetExceptionMessage(ex));
                return false;
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
            _registerProxyAbility = _apiType.GetMethod(
                "RegisterProxyAbility",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string), typeof(Dictionary<string, Delegate>) },
                null);
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
            _getTotalActiveMagicEffectValueForWeapon = _apiType.GetMethod(
                "GetTotalActiveMagicEffectValueForWeapon",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(Player), typeof(ItemDrop.ItemData), typeof(string), typeof(float) },
                null);
            _getTotalPlayerActiveMagicEffectValue = _apiType.GetMethod(
                "GetTotalPlayerActiveMagicEffectValue",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(Player), typeof(string), typeof(float), typeof(ItemDrop.ItemData) },
                null);
            _playerHasActiveMagicEffect = _apiType.GetMethod(
                "PlayerHasActiveMagicEffect",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(Player), typeof(string), typeof(float).MakeByRefType(), typeof(float), typeof(ItemDrop.ItemData) },
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
