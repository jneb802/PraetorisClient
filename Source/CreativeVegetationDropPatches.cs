using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PraetorisClient
{
    internal static class CreativeVegetationDropPatches
    {
        private static readonly DropTable EmptyDropTable = new()
        {
            m_drops = new List<DropTable.DropData>(),
            m_dropMin = 0,
            m_dropMax = 0,
            m_dropChance = 0f
        };

        [HarmonyPatch]
        private static class TreeBaseRpcDamagePatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(typeof(TreeBase), "RPC_Damage");
            }

            private static void Prefix(TreeBase __instance, ref DropPatchState? __state)
            {
                __state = ReplaceDropsIfSuppressed(__instance, __instance.m_dropWhenDestroyed);
                if (__state != null)
                {
                    __instance.m_dropWhenDestroyed = EmptyDropTable;
                }
            }

            private static void Postfix(TreeBase __instance, DropPatchState? __state)
            {
                if (__state != null)
                {
                    __instance.m_dropWhenDestroyed = __state.DropTable;
                }
            }
        }

        [HarmonyPatch]
        private static class TreeBaseSpawnLogPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(typeof(TreeBase), "SpawnLog");
            }

            private static bool Prefix(TreeBase __instance)
            {
                return !CreativeBiomeOverride.ShouldSuppressVegetationDrops(__instance);
            }
        }

        [HarmonyPatch]
        private static class TreeLogDestroyPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(typeof(TreeLog), "Destroy");
            }

            private static void Prefix(TreeLog __instance, ref TreeLogPatchState? __state)
            {
                if (!CreativeBiomeOverride.ShouldSuppressVegetationDrops(__instance))
                {
                    __state = null;
                    return;
                }

                __state = new TreeLogPatchState(__instance.m_dropWhenDestroyed, __instance.m_subLogPrefab);
                __instance.m_dropWhenDestroyed = EmptyDropTable;
                __instance.m_subLogPrefab = null;
            }

            private static void Postfix(TreeLog __instance, TreeLogPatchState? __state)
            {
                if (__state == null)
                {
                    return;
                }

                __instance.m_dropWhenDestroyed = __state.DropTable;
                __instance.m_subLogPrefab = __state.SubLogPrefab;
            }
        }

        [HarmonyPatch]
        private static class PickableRpcPickPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(typeof(Pickable), "RPC_Pick");
            }

            private static bool Prefix(Pickable __instance)
            {
                if (!CreativeBiomeOverride.ShouldSuppressVegetationDrops(__instance))
                {
                    return true;
                }

                ZNetView netView = __instance.GetComponent<ZNetView>();
                if (netView == null || !netView.IsOwner())
                {
                    return true;
                }

                __instance.SetPicked(true);
                return false;
            }
        }

        [HarmonyPatch]
        private static class DropOnDestroyedPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(typeof(DropOnDestroyed), "OnDestroyed");
            }

            private static bool Prefix(DropOnDestroyed __instance)
            {
                return !CreativeBiomeOverride.ShouldSuppressVegetationDrops(__instance);
            }
        }

        [HarmonyPatch]
        private static class MineRockRpcHitPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(typeof(MineRock), "RPC_Hit");
            }

            private static void Prefix(MineRock __instance, HitData hit, ref DropPatchState? __state)
            {
                __state = ReplaceDropsIfSuppressed(__instance, __instance.m_dropItems, hit.m_point);
                if (__state != null)
                {
                    __instance.m_dropItems = EmptyDropTable;
                }
            }

            private static void Postfix(MineRock __instance, DropPatchState? __state)
            {
                if (__state != null)
                {
                    __instance.m_dropItems = __state.DropTable;
                }
            }
        }

        [HarmonyPatch]
        private static class MineRock5DamageAreaPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(typeof(MineRock5), "DamageArea");
            }

            private static void Prefix(MineRock5 __instance, HitData hit, ref DropPatchState? __state)
            {
                __state = ReplaceDropsIfSuppressed(__instance, __instance.m_dropItems, hit.m_point);
                if (__state != null)
                {
                    __instance.m_dropItems = EmptyDropTable;
                }
            }

            private static void Postfix(MineRock5 __instance, DropPatchState? __state)
            {
                if (__state != null)
                {
                    __instance.m_dropItems = __state.DropTable;
                }
            }
        }

        private static DropPatchState? ReplaceDropsIfSuppressed(Component component, DropTable dropTable)
        {
            return CreativeBiomeOverride.ShouldSuppressVegetationDrops(component)
                ? new DropPatchState(dropTable)
                : null;
        }

        private static DropPatchState? ReplaceDropsIfSuppressed(Component component, DropTable dropTable, Vector3 point)
        {
            return CreativeBiomeOverride.ShouldSuppressVegetationDrops(component, point)
                ? new DropPatchState(dropTable)
                : null;
        }

        private sealed class DropPatchState
        {
            internal DropPatchState(DropTable dropTable)
            {
                DropTable = dropTable;
            }

            internal DropTable DropTable { get; }
        }

        private sealed class TreeLogPatchState
        {
            internal TreeLogPatchState(DropTable dropTable, GameObject subLogPrefab)
            {
                DropTable = dropTable;
                SubLogPrefab = subLogPrefab;
            }

            internal DropTable DropTable { get; }
            internal GameObject SubLogPrefab { get; }
        }
    }
}
