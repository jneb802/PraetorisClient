using System;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace PraetorisClient
{
    internal static class PublicBasicPickables
    {
        internal const string ProtectiveWardsGuid = "shudnal.ProtectiveWards";

        internal static void TryApply(Harmony harmony)
        {
            if (!Chainloader.PluginInfos.TryGetValue(ProtectiveWardsGuid, out BepInEx.PluginInfo plugin)) return;

            try
            {
                MethodInfo target = AccessTools.DeclaredMethod(plugin.Instance.GetType(),
                    "BlockUnauthorizedWardInteraction", new[] { typeof(Component), typeof(Humanoid), typeof(bool) })
                    ?? throw new MissingMethodException("ProtectiveWards.BlockUnauthorizedWardInteraction(Component, Humanoid, bool)");
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(PublicBasicPickables), nameof(AllowBasicPickable)));
                PraetorisClientPlugin.Log.LogInfo("Protective Wards allows public flint, branch, and stone pickables.");
            }
            catch (Exception exception)
            {
                PraetorisClientPlugin.Log.LogWarning("Could not enable public basic pickables; ward protection remains unchanged: " + exception.Message);
            }
        }

        private static bool AllowBasicPickable(Component __0, ref bool __result)
        {
            if (__0 is not Pickable pickable) return true;

            string prefabName = Utils.GetPrefabName(pickable.gameObject);
            if (prefabName != "Pickable_Flint" && prefabName != "Pickable_Branch" && prefabName != "Pickable_Stone") return true;

            // Skip only Protective Wards' denial. Pickable.Interact still runs its normal checks and RPC.
            __result = false;
            return false;
        }
    }
}
