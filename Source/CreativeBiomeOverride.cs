using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PraetorisClient
{
    internal static class CreativeBiomeOverride
    {
        private const int ProtocolVersion = 2;
        private const float VisualBiomeMargin = 16f;
        private const float TerrainSourcePadding = 256f;
        private static readonly Dictionary<string, OverrideZone> Zones = new();
        private static readonly FieldInfo? HeightmapBuildDataField = AccessTools.Field(typeof(Heightmap), "m_buildData");
        private static bool _samplingSourceTerrain;

        public static void OnOverride(long sender, ZPackage pkg)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer())
            {
                return;
            }

            try
            {
                int version = pkg.ReadInt();
                if (version < 1 || version > ProtocolVersion)
                {
                    PraetorisClientPlugin.Log.LogWarning($"Ignoring creative biome override version {version}; expected 1-{ProtocolVersion}.");
                    return;
                }

                int count = pkg.ReadInt();
                if (count <= 0)
                {
                    ClearAll();
                    return;
                }

                for (int index = 0; index < count; index++)
                {
                    string zoneId = pkg.ReadString();
                    bool enabled = pkg.ReadBool();
                    Vector3 center = pkg.ReadVector3();
                    float radius = pkg.ReadSingle();
                    Heightmap.Biome biome = (Heightmap.Biome)pkg.ReadInt();
                    bool suppressSpawns = count == 1 && pkg.GetPos() < pkg.Size()
                        ? pkg.ReadBool()
                        : !zoneId.StartsWith("siege_", StringComparison.OrdinalIgnoreCase);
                    bool useTerrainSource = false;
                    Vector3 terrainSourceCenter = Vector3.zero;
                    if (version >= 2 && pkg.GetPos() < pkg.Size())
                    {
                        useTerrainSource = pkg.ReadBool();
                        terrainSourceCenter = pkg.ReadVector3();
                    }

                    if (!enabled || biome == Heightmap.Biome.None || radius <= 0f)
                    {
                        Remove(zoneId);
                        continue;
                    }

                    Set(zoneId, center, radius, biome, suppressSpawns, useTerrainSource, terrainSourceCenter);
                }
            }
            catch (Exception ex)
            {
                PraetorisClientPlugin.Log.LogWarning($"Failed to apply creative biome override: {ex}");
            }
        }

        public static bool TryGetBiome(float x, float z, out Heightmap.Biome biome)
        {
            if (_samplingSourceTerrain)
            {
                biome = Heightmap.Biome.None;
                return false;
            }

            foreach (OverrideZone zone in Zones.Values)
            {
                if (!zone.ContainsTerrain(x, z))
                {
                    continue;
                }

                if (zone.UseTerrainSource && WorldGenerator.instance != null)
                {
                    Vector2 source = zone.MapToSource(x, z);
                    _samplingSourceTerrain = true;
                    try
                    {
                        biome = WorldGenerator.instance.GetBiome(source.x, source.y);
                    }
                    finally
                    {
                        _samplingSourceTerrain = false;
                    }

                    return true;
                }

                if (!zone.UseTerrainSource)
                {
                    biome = zone.Biome;
                    return true;
                }
            }

            biome = Heightmap.Biome.None;
            return false;
        }

        private static bool TryGetVisualBiome(float x, float z, out Heightmap.Biome biome)
        {
            foreach (OverrideZone zone in Zones.Values)
            {
                if (zone.ContainsVisual(x, z))
                {
                    biome = zone.Biome;
                    return true;
                }
            }

            biome = Heightmap.Biome.None;
            return false;
        }

        private static bool TryMapToSource(float x, float z, out Vector2 source)
        {
            source = Vector2.zero;
            if (_samplingSourceTerrain)
            {
                return false;
            }

            foreach (OverrideZone zone in Zones.Values)
            {
                if (zone.UseTerrainSource && zone.ContainsTerrain(x, z))
                {
                    source = zone.MapToSource(x, z);
                    return true;
                }
            }

            return false;
        }

        public static bool ContainsSpawnBlockedZone(Vector3 point)
        {
            foreach (OverrideZone zone in Zones.Values)
            {
                if (zone.SuppressSpawns && zone.Contains(point.x, point.z))
                {
                    return true;
                }
            }

            return false;
        }

        private static void Set(
            string zoneId,
            Vector3 center,
            float radius,
            Heightmap.Biome biome,
            bool suppressSpawns,
            bool useTerrainSource,
            Vector3 terrainSourceCenter)
        {
            zoneId = NormalizeZoneId(zoneId);
            bool hadExistingZone = Zones.TryGetValue(zoneId, out OverrideZone existingZone);
            OverrideZone zone = new(center, radius, biome, suppressSpawns, useTerrainSource, terrainSourceCenter);
            Zones[zoneId] = zone;
            if (hadExistingZone)
            {
                RefreshTerrain(existingZone);
            }

            RefreshTerrain(zone);
            string terrain = useTerrainSource
                ? $", terrainSource={terrainSourceCenter.x:0.##},{terrainSourceCenter.z:0.##}"
                : string.Empty;
            PraetorisClientPlugin.Log.LogInfo($"Creative biome override {zoneId}: {biome} at {center.x:0.##},{center.z:0.##} radius {radius:0.##}, suppressSpawns={suppressSpawns}{terrain}.");
        }

        private static void Remove(string zoneId)
        {
            zoneId = NormalizeZoneId(zoneId);
            if (!Zones.TryGetValue(zoneId, out OverrideZone zone))
            {
                return;
            }

            Zones.Remove(zoneId);
            RefreshTerrain(zone);
            PraetorisClientPlugin.Log.LogInfo($"Removed creative biome override {zoneId}.");
        }

        private static void ClearAll()
        {
            if (Zones.Count == 0)
            {
                return;
            }

            List<OverrideZone> zones = new(Zones.Values);
            Zones.Clear();
            foreach (OverrideZone zone in zones)
            {
                RefreshTerrain(zone);
            }

            PraetorisClientPlugin.Log.LogInfo("Cleared creative biome overrides.");
        }

        private static void RefreshTerrain(OverrideZone zone)
        {
            foreach (Heightmap heightmap in Heightmap.GetAllHeightmaps())
            {
                if (heightmap == null || !Intersects(heightmap, zone))
                {
                    continue;
                }

                HeightmapBuildDataField?.SetValue(heightmap, null);
                heightmap.Poke(false);
            }

            if (ClutterSystem.instance != null)
            {
                ClutterSystem.instance.ResetGrass(zone.Center, zone.TerrainRadius);
            }
        }

        private static bool Intersects(Heightmap heightmap, OverrideZone zone)
        {
            float halfSize = heightmap.m_width * heightmap.m_scale * 0.5f;
            Vector3 center = heightmap.transform.position;
            float dx = Math.Max(Math.Abs(center.x - zone.Center.x) - halfSize, 0f);
            float dz = Math.Max(Math.Abs(center.z - zone.Center.z) - halfSize, 0f);
            return dx * dx + dz * dz <= zone.TerrainRadiusSquared;
        }

        private static string NormalizeZoneId(string zoneId)
        {
            return string.IsNullOrWhiteSpace(zoneId) ? "creative" : zoneId.Trim();
        }

        private sealed class OverrideZone
        {
            public OverrideZone(
                Vector3 center,
                float radius,
                Heightmap.Biome biome,
                bool suppressSpawns,
                bool useTerrainSource,
                Vector3 terrainSourceCenter)
            {
                Center = center;
                Radius = radius;
                RadiusSquared = radius * radius;
                VisualRadius = radius + VisualBiomeMargin;
                VisualRadiusSquared = VisualRadius * VisualRadius;
                TerrainRadius = useTerrainSource ? radius + TerrainSourcePadding : radius;
                TerrainRadiusSquared = TerrainRadius * TerrainRadius;
                Biome = biome;
                SuppressSpawns = suppressSpawns;
                UseTerrainSource = useTerrainSource;
                TerrainSourceCenter = terrainSourceCenter;
            }

            public Vector3 Center { get; }
            public float Radius { get; }
            public float RadiusSquared { get; }
            public float VisualRadius { get; }
            public float VisualRadiusSquared { get; }
            public float TerrainRadius { get; }
            public float TerrainRadiusSquared { get; }
            public Heightmap.Biome Biome { get; }
            public bool SuppressSpawns { get; }
            public bool UseTerrainSource { get; }
            public Vector3 TerrainSourceCenter { get; }

            public bool Contains(float x, float z)
            {
                return ContainsRadius(x, z, RadiusSquared);
            }

            public bool ContainsTerrain(float x, float z)
            {
                return ContainsRadius(x, z, TerrainRadiusSquared);
            }

            private bool ContainsRadius(float x, float z, float radiusSquared)
            {
                float dx = x - Center.x;
                float dz = z - Center.z;
                return dx * dx + dz * dz <= radiusSquared;
            }

            public Vector2 MapToSource(float x, float z)
            {
                return new Vector2(
                    TerrainSourceCenter.x + (x - Center.x),
                    TerrainSourceCenter.z + (z - Center.z));
            }

            public bool ContainsVisual(float x, float z)
            {
                float dx = x - Center.x;
                float dz = z - Center.z;
                return dx * dx + dz * dz <= VisualRadiusSquared;
            }
        }

        [HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.GetBiome), typeof(float), typeof(float), typeof(float), typeof(bool))]
        private static class WorldGeneratorGetBiomePatch
        {
            private static bool Prefix(float wx, float wy, ref Heightmap.Biome __result)
            {
                if (!TryGetBiome(wx, wy, out Heightmap.Biome biome))
                {
                    return true;
                }

                __result = biome;
                return false;
            }
        }

        [HarmonyPatch(typeof(Heightmap), nameof(Heightmap.GetBiome), typeof(Vector3), typeof(float), typeof(bool))]
        private static class HeightmapGetBiomePatch
        {
            private static bool Prefix(Vector3 point, ref Heightmap.Biome __result)
            {
                if (!TryGetBiome(point.x, point.z, out Heightmap.Biome biome))
                {
                    return true;
                }

                __result = biome;
                return false;
            }
        }

        [HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.GetBiomeHeight))]
        private static class WorldGeneratorGetBiomeHeightPatch
        {
            private static void Prefix(ref Heightmap.Biome biome, ref float wx, ref float wy)
            {
                if (!TryMapToSource(wx, wy, out Vector2 source) || WorldGenerator.instance == null)
                {
                    return;
                }

                _samplingSourceTerrain = true;
                try
                {
                    biome = WorldGenerator.instance.GetBiome(source.x, source.y);
                }
                finally
                {
                    _samplingSourceTerrain = false;
                }

                wx = source.x;
                wy = source.y;
            }
        }

        [HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.GetHeight), typeof(float), typeof(float))]
        private static class WorldGeneratorGetHeightPatch
        {
            private static bool Prefix(float wx, float wy, ref float __result)
            {
                if (!TryMapToSource(wx, wy, out Vector2 source) || WorldGenerator.instance == null)
                {
                    return true;
                }

                _samplingSourceTerrain = true;
                try
                {
                    __result = WorldGenerator.instance.GetHeight(source.x, source.y);
                }
                finally
                {
                    _samplingSourceTerrain = false;
                }

                return false;
            }
        }

        [HarmonyPatch]
        private static class WorldGeneratorGetHeightWithMaskPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    typeof(WorldGenerator),
                    nameof(WorldGenerator.GetHeight),
                    new[] { typeof(float), typeof(float), typeof(Color).MakeByRefType() });
            }

            private static bool Prefix(float wx, float wy, ref Color mask, ref float __result)
            {
                if (!TryMapToSource(wx, wy, out Vector2 source) || WorldGenerator.instance == null)
                {
                    return true;
                }

                _samplingSourceTerrain = true;
                try
                {
                    __result = WorldGenerator.instance.GetHeight(source.x, source.y, out mask);
                }
                finally
                {
                    _samplingSourceTerrain = false;
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(Heightmap), "GetBiomeColor", typeof(float), typeof(float))]
        private static class HeightmapGetBiomeColorPatch
        {
            private static bool Prefix(Heightmap __instance, float ix, float iy, ref Color __result)
            {
                if (__instance == null)
                {
                    return true;
                }

                float halfSize = __instance.m_width * __instance.m_scale * 0.5f;
                Vector3 center = __instance.transform.position;
                float wx = center.x + (ix - 0.5f) * halfSize * 2f;
                float wz = center.z + (iy - 0.5f) * halfSize * 2f;
                if (!TryGetVisualBiome(wx, wz, out Heightmap.Biome biome))
                {
                    return true;
                }

                __result = Heightmap.GetBiomeColor(biome);
                return false;
            }
        }

        [HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.IsAshlands))]
        private static class WorldGeneratorIsAshlandsPatch
        {
            private static bool Prefix(float x, float y, ref bool __result)
            {
                if (!TryGetBiome(x, y, out Heightmap.Biome biome))
                {
                    return true;
                }

                __result = biome == Heightmap.Biome.AshLands;
                return false;
            }
        }

        [HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.IsDeepnorth))]
        private static class WorldGeneratorIsDeepnorthPatch
        {
            private static bool Prefix(float x, float y, ref bool __result)
            {
                if (!TryGetBiome(x, y, out Heightmap.Biome biome))
                {
                    return true;
                }

                __result = biome == Heightmap.Biome.DeepNorth;
                return false;
            }
        }

        [HarmonyPatch(typeof(SpawnSystem), "IsSpawnPointGood")]
        private static class SpawnSystemIsSpawnPointGoodPatch
        {
            private static bool Prefix(ref Vector3 spawnPoint, ref bool __result)
            {
                if (!ContainsSpawnBlockedZone(spawnPoint))
                {
                    return true;
                }

                __result = false;
                return false;
            }
        }

    }
}
