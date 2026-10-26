using HarmonyLib;
using UnityEngine;

namespace PraetorisClient
{
    internal static class SerpentSpawnExclusion
    {
        internal enum Center
        {
            StartingStones,
            WorldCenter
        }

        internal static bool IsBlocked(SpawnSystem.SpawnData spawn, Vector3 position)
        {
            if (!PraetorisClientPlugin.SerpentSpawnExclusionEnabled.Value ||
                spawn.m_prefab == null || spawn.m_prefab.name != "Serpent")
                return false;

            float radius = PraetorisClientPlugin.SerpentSpawnExclusionRadius.Value;
            if (radius <= 0f)
                return false;

            Vector3 center = Vector3.zero;
            if (PraetorisClientPlugin.SerpentSpawnExclusionCenter.Value == Center.StartingStones &&
                (ZoneSystem.instance == null || !ZoneSystem.instance.GetLocationIcon("StartTemple", out center)))
                return true;

            float x = position.x - center.x;
            float z = position.z - center.z;
            return x * x + z * z < radius * radius;
        }
    }

    [HarmonyPatch(typeof(SpawnSystem), nameof(SpawnSystem.IsSpawnPointGood))]
    internal static class SerpentSpawnPointPatch
    {
        private static bool Prefix(SpawnSystem.SpawnData spawn, Vector3 spawnPoint, ref bool __result)
        {
            if (!SerpentSpawnExclusion.IsBlocked(spawn, spawnPoint))
                return true;

            __result = false;
            return false;
        }
    }

    // Also protect direct calls to the world spawner, including calls from other mods.
    [HarmonyPatch(typeof(SpawnSystem), nameof(SpawnSystem.Spawn))]
    internal static class SerpentWorldSpawnPatch
    {
        private static bool Prefix(SpawnSystem.SpawnData critter, Vector3 spawnPoint)
        {
            return !SerpentSpawnExclusion.IsBlocked(critter, spawnPoint);
        }
    }
}
