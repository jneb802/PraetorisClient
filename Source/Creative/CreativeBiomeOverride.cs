using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PraetorisClient
{
    internal static class CreativeBiomeOverride
    {
        private const int ProtocolVersion = 6;
        private const string ZdoVegetationMarker = "valheimCreative.vegetation";
        private const string ZdoVegetationSlotId = "valheimCreative.vegetationSlot";
        private const float VisualBiomeMargin = 16f;
        private static readonly Dictionary<string, OverrideZone> Zones = new();
        private static readonly FieldInfo? HeightmapBuildDataField = AccessTools.Field(typeof(Heightmap), "m_buildData");
        [ThreadStatic]
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

                    int terrainSourceWorldSeed = WorldGenerator.instance != null ? WorldGenerator.instance.GetSeed() : 0;
                    string terrainSourceWorldSeedName = ZNet.World != null ? ZNet.World.m_seedName : string.Empty;
                    if (version >= 6 && pkg.GetPos() < pkg.Size())
                    {
                        terrainSourceWorldSeed = pkg.ReadInt();
                        terrainSourceWorldSeedName = pkg.ReadString();
                    }

                    float terrainPatchHalfSize = CalculateTerrainPatchHalfSize(radius);
                    if (version >= 3 && pkg.GetPos() < pkg.Size())
                    {
                        float receivedTerrainPatchHalfSize = pkg.ReadSingle();
                        if (receivedTerrainPatchHalfSize > 0f)
                        {
                            terrainPatchHalfSize = receivedTerrainPatchHalfSize;
                        }
                    }

                    float terrainEdgeFalloffWidth = 0f;
                    float terrainEdgeFloorHeight = 0f;
                    if (version >= 4 && pkg.GetPos() < pkg.Size())
                    {
                        terrainEdgeFalloffWidth = Mathf.Max(0f, pkg.ReadSingle());
                    }

                    if (version >= 4 && pkg.GetPos() < pkg.Size())
                    {
                        terrainEdgeFloorHeight = pkg.ReadSingle();
                    }

                    bool suppressVegetationDrops = !zoneId.StartsWith("siege_", StringComparison.OrdinalIgnoreCase);
                    if (version >= 5 && pkg.GetPos() < pkg.Size())
                    {
                        suppressVegetationDrops = pkg.ReadBool();
                    }

                    if (!enabled || biome == Heightmap.Biome.None || radius <= 0f)
                    {
                        Remove(zoneId);
                        continue;
                    }

                    Set(zoneId, center, radius, biome, suppressSpawns, useTerrainSource, terrainSourceCenter, terrainSourceWorldSeed, terrainSourceWorldSeedName, terrainPatchHalfSize, terrainEdgeFalloffWidth, terrainEdgeFloorHeight, suppressVegetationDrops);
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

                if (zone.UseTerrainSource && zone.TerrainSourceGenerator != null)
                {
                    Vector2 source = zone.MapToSource(x, z);
                    _samplingSourceTerrain = true;
                    try
                    {
                        biome = zone.TerrainSourceGenerator.GetBiome(source.x, source.y);
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

        private static bool TryGetTerrainSample(float x, float z, out TerrainSample sample)
        {
            sample = default;
            if (_samplingSourceTerrain)
            {
                return false;
            }

            foreach (OverrideZone zone in Zones.Values)
            {
                if (zone.UseTerrainSource && zone.ContainsTerrain(x, z))
                {
                    sample = zone.CreateTerrainSample(x, z);
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

        public static bool ShouldSuppressVegetationDrops(Component component)
        {
            if (component == null)
            {
                return false;
            }

            return ShouldSuppressVegetationDrops(component, component.transform.position);
        }

        public static bool ShouldSuppressVegetationDrops(Component component, Vector3 point)
        {
            if (component == null)
            {
                return false;
            }

            foreach (OverrideZone zone in Zones.Values)
            {
                if (!zone.SuppressVegetationDrops || !zone.Contains(point.x, point.z))
                {
                    continue;
                }

                ZNetView netView = component.GetComponent<ZNetView>() ?? component.GetComponentInParent<ZNetView>();
                ZDO? zdo = netView != null ? netView.GetZDO() : null;
                if (zdo != null && IsMarkedCreativeVegetation(zdo))
                {
                    return true;
                }

                return component.GetComponent<TreeBase>() != null ||
                       component.GetComponent<TreeLog>() != null ||
                       component.GetComponent<Pickable>() != null ||
                       component.GetComponent<MineRock>() != null ||
                       component.GetComponent<MineRock5>() != null ||
                       component.GetComponent<DropOnDestroyed>() != null;
            }

            return false;
        }

        public static bool ContainsTerrainOverride(Vector3 point)
        {
            foreach (OverrideZone zone in Zones.Values)
            {
                if (zone.UseTerrainSource && zone.ContainsTerrain(point.x, point.z))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsMarkedCreativeVegetation(ZDO zdo)
        {
            return zdo.GetBool(ZdoVegetationMarker) ||
                   !string.IsNullOrWhiteSpace(zdo.GetString(ZdoVegetationSlotId));
        }

        private static void Set(
            string zoneId,
            Vector3 center,
            float radius,
            Heightmap.Biome biome,
            bool suppressSpawns,
            bool useTerrainSource,
            Vector3 terrainSourceCenter,
            int terrainSourceWorldSeed,
            string terrainSourceWorldSeedName,
            float terrainPatchHalfSize,
            float terrainEdgeFalloffWidth,
            float terrainEdgeFloorHeight,
            bool suppressVegetationDrops)
        {
            zoneId = NormalizeZoneId(zoneId);
            bool hadExistingZone = Zones.TryGetValue(zoneId, out OverrideZone existingZone);
            OverrideZone zone = new(center, radius, biome, suppressSpawns, useTerrainSource, terrainSourceCenter, terrainSourceWorldSeed, terrainSourceWorldSeedName, terrainPatchHalfSize, terrainEdgeFalloffWidth, terrainEdgeFloorHeight, suppressVegetationDrops);
            if (hadExistingZone && existingZone.HasSameState(zone))
            {
                return;
            }

            Zones[zoneId] = zone;
            if (hadExistingZone)
            {
                RefreshTerrain(existingZone);
            }

            RefreshTerrain(zone);
            string terrain = useTerrainSource
                ? $", terrainSource={terrainSourceCenter.x:0.##},{terrainSourceCenter.z:0.##}"
                : string.Empty;
            PraetorisClientPlugin.Log.LogInfo($"Creative biome override {zoneId}: {biome} at {center.x:0.##},{center.z:0.##} radius {radius:0.##}, suppressSpawns={suppressSpawns}, suppressVegetationDrops={suppressVegetationDrops}{terrain}.");
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
            foreach (Heightmap heightmap in UnityEngine.Object.FindObjectsByType<Heightmap>(FindObjectsSortMode.None))
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
                ClutterSystem.instance.ResetGrass(zone.Center, zone.GrassResetRadius);
            }
        }

        private static bool Intersects(Heightmap heightmap, OverrideZone zone)
        {
            Vector3 center = heightmap.transform.position;
            if (zone.UseTerrainSource)
            {
                return zone.IntersectsTerrain(heightmap);
            }

            float halfSize = heightmap.m_width * heightmap.m_scale * 0.5f;
            float dx = Math.Max(Math.Abs(center.x - zone.Center.x) - halfSize, 0f);
            float dz = Math.Max(Math.Abs(center.z - zone.Center.z) - halfSize, 0f);
            return dx * dx + dz * dz <= zone.RadiusSquared;
        }

        private static float CalculateTerrainPatchHalfSize(float radius)
        {
            float normalizedRadius = Mathf.Max(1f, radius);
            float extraBeyondCenterSector = Mathf.Max(0f, normalizedRadius - ZoneSystem.c_ZoneHalfSize);
            float sectorRings = Mathf.Ceil(extraBeyondCenterSector / ZoneSystem.c_ZoneSize);
            return ZoneSystem.c_ZoneHalfSize + sectorRings * ZoneSystem.c_ZoneSize;
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
                Vector3 terrainSourceCenter,
                int terrainSourceWorldSeed,
                string terrainSourceWorldSeedName,
                float terrainPatchHalfSize,
                float terrainEdgeFalloffWidth,
                float terrainEdgeFloorHeight,
                bool suppressVegetationDrops)
            {
                Center = center;
                Radius = radius;
                RadiusSquared = radius * radius;
                TerrainOuterRadius = radius + Mathf.Max(0f, terrainEdgeFalloffWidth);
                TerrainOuterRadiusSquared = TerrainOuterRadius * TerrainOuterRadius;
                TerrainEdgeFalloffWidth = Mathf.Max(0f, terrainEdgeFalloffWidth);
                TerrainEdgeFloorHeight = terrainEdgeFloorHeight;
                VisualRadius = radius + VisualBiomeMargin;
                VisualRadiusSquared = VisualRadius * VisualRadius;
                TerrainPatchHalfSize = useTerrainSource
                    ? Mathf.Max(ZoneSystem.c_ZoneHalfSize, terrainPatchHalfSize)
                    : radius;
                GrassResetRadius = useTerrainSource
                    ? Mathf.Sqrt(2f) * (TerrainPatchHalfSize + ZoneSystem.c_ZoneHalfSize)
                    : radius;
                Biome = biome;
                SuppressSpawns = suppressSpawns;
                UseTerrainSource = useTerrainSource;
                TerrainSourceCenter = terrainSourceCenter;
                TerrainSourceWorldSeed = terrainSourceWorldSeed;
                TerrainSourceWorldSeedName = terrainSourceWorldSeedName ?? string.Empty;
                TerrainSourceGenerator = useTerrainSource
                    ? CreativeTerrainWorldGenerator.Get(terrainSourceWorldSeed, TerrainSourceWorldSeedName)
                    : null;
                SuppressVegetationDrops = suppressVegetationDrops;
            }

            public Vector3 Center { get; }
            public float Radius { get; }
            public float RadiusSquared { get; }
            public float TerrainOuterRadius { get; }
            public float TerrainOuterRadiusSquared { get; }
            public float TerrainEdgeFalloffWidth { get; }
            public float TerrainEdgeFloorHeight { get; }
            public float VisualRadius { get; }
            public float VisualRadiusSquared { get; }
            public float TerrainPatchHalfSize { get; }
            public float GrassResetRadius { get; }
            public Heightmap.Biome Biome { get; }
            public bool SuppressSpawns { get; }
            public bool UseTerrainSource { get; }
            public Vector3 TerrainSourceCenter { get; }
            public int TerrainSourceWorldSeed { get; }
            public string TerrainSourceWorldSeedName { get; }
            public WorldGenerator? TerrainSourceGenerator { get; }
            public bool SuppressVegetationDrops { get; }

            public bool Contains(float x, float z)
            {
                return ContainsRadius(x, z, RadiusSquared);
            }

            public bool ContainsTerrain(float x, float z)
            {
                if (!UseTerrainSource)
                {
                    return ContainsRadius(x, z, RadiusSquared);
                }

                Vector3 sectorCenter = ZoneSystem.GetZonePos(ZoneSystem.GetZone(new Vector3(x, 0f, z)));
                return Mathf.Abs(sectorCenter.x - Center.x) <= TerrainPatchHalfSize &&
                       Mathf.Abs(sectorCenter.z - Center.z) <= TerrainPatchHalfSize &&
                       ContainsRadius(x, z, TerrainOuterRadiusSquared);
            }

            public bool IntersectsTerrain(Heightmap heightmap)
            {
                Vector3 center = heightmap.transform.position;
                Vector3 sectorCenter = ZoneSystem.GetZonePos(ZoneSystem.GetZone(center));
                if (Mathf.Abs(sectorCenter.x - Center.x) > TerrainPatchHalfSize ||
                    Mathf.Abs(sectorCenter.z - Center.z) > TerrainPatchHalfSize)
                {
                    return false;
                }

                float halfSize = heightmap.m_width * heightmap.m_scale * 0.5f;
                float dx = Math.Max(Math.Abs(center.x - Center.x) - halfSize, 0f);
                float dz = Math.Max(Math.Abs(center.z - Center.z) - halfSize, 0f);
                return dx * dx + dz * dz <= TerrainOuterRadiusSquared;
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

            public TerrainSample CreateTerrainSample(float x, float z)
            {
                float distance = DistanceFromCenter(x, z);
                float sourceWeight = 1f;
                if (TerrainEdgeFalloffWidth > 0f && distance > Radius)
                {
                    float t = Mathf.Clamp01((distance - Radius) / TerrainEdgeFalloffWidth);
                    sourceWeight = 1f - Mathf.SmoothStep(0f, 1f, t);
                }

                return new TerrainSample(MapToSource(x, z), sourceWeight, TerrainEdgeFloorHeight, TerrainSourceGenerator);
            }

            public bool ContainsVisual(float x, float z)
            {
                if (UseTerrainSource)
                {
                    return ContainsTerrain(x, z);
                }

                float dx = x - Center.x;
                float dz = z - Center.z;
                return dx * dx + dz * dz <= VisualRadiusSquared;
            }

            public bool HasSameState(OverrideZone other)
            {
                return Approximately(Center, other.Center)
                    && Mathf.Approximately(Radius, other.Radius)
                    && Biome == other.Biome
                    && SuppressSpawns == other.SuppressSpawns
                    && UseTerrainSource == other.UseTerrainSource
                    && Approximately(TerrainSourceCenter, other.TerrainSourceCenter)
                    && TerrainSourceWorldSeed == other.TerrainSourceWorldSeed
                    && string.Equals(TerrainSourceWorldSeedName, other.TerrainSourceWorldSeedName, StringComparison.Ordinal)
                    && Mathf.Approximately(TerrainPatchHalfSize, other.TerrainPatchHalfSize)
                    && Mathf.Approximately(TerrainEdgeFalloffWidth, other.TerrainEdgeFalloffWidth)
                    && Mathf.Approximately(TerrainEdgeFloorHeight, other.TerrainEdgeFloorHeight)
                    && SuppressVegetationDrops == other.SuppressVegetationDrops;
            }

            private float DistanceFromCenter(float x, float z)
            {
                float dx = x - Center.x;
                float dz = z - Center.z;
                return Mathf.Sqrt(dx * dx + dz * dz);
            }

            private static bool Approximately(Vector3 left, Vector3 right)
            {
                return Mathf.Approximately(left.x, right.x)
                    && Mathf.Approximately(left.y, right.y)
                    && Mathf.Approximately(left.z, right.z);
            }
        }

        private readonly struct TerrainSample
        {
            public TerrainSample(Vector2 source, float sourceWeight, float floorHeight, WorldGenerator? generator)
            {
                Source = source;
                SourceWeight = sourceWeight;
                FloorHeight = floorHeight;
                Generator = generator;
            }

            public Vector2 Source { get; }
            public float SourceWeight { get; }
            public float FloorHeight { get; }
            public WorldGenerator? Generator { get; }

            public float ApplyHeight(float sourceHeight)
            {
                return Mathf.Lerp(FloorHeight, sourceHeight, SourceWeight);
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
            private static bool Prefix(ref Heightmap.Biome biome, float wx, float wy, ref Color mask, bool preGeneration, ref float __result)
            {
                if (!TryGetTerrainSample(wx, wy, out TerrainSample sample) || sample.Generator == null)
                {
                    return true;
                }

                _samplingSourceTerrain = true;
                try
                {
                    biome = sample.Generator.GetBiome(sample.Source.x, sample.Source.y);
                    __result = sample.ApplyHeight(sample.Generator.GetBiomeHeight(biome, sample.Source.x, sample.Source.y, out mask, preGeneration));
                }
                finally
                {
                    _samplingSourceTerrain = false;
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.GetHeight), typeof(float), typeof(float))]
        private static class WorldGeneratorGetHeightPatch
        {
            private static bool Prefix(float wx, float wy, ref float __result)
            {
                if (!TryGetTerrainSample(wx, wy, out TerrainSample sample) || sample.Generator == null)
                {
                    return true;
                }

                _samplingSourceTerrain = true;
                try
                {
                    __result = sample.ApplyHeight(sample.Generator.GetHeight(sample.Source.x, sample.Source.y));
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
                if (!TryGetTerrainSample(wx, wy, out TerrainSample sample) || sample.Generator == null)
                {
                    return true;
                }

                _samplingSourceTerrain = true;
                try
                {
                    __result = sample.ApplyHeight(sample.Generator.GetHeight(sample.Source.x, sample.Source.y, out mask));
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
