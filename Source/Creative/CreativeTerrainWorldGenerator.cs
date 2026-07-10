using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace PraetorisClient
{
    internal static class CreativeTerrainWorldGenerator
    {
        private static readonly ConstructorInfo? WorldGeneratorConstructor =
            AccessTools.Constructor(typeof(WorldGenerator), new[] { typeof(World) });

        private static readonly Dictionary<string, WorldGenerator> Generators = new(StringComparer.Ordinal);

        internal static WorldGenerator? Get(int worldSeed, string worldSeedName)
        {
            if (WorldGenerator.instance != null && WorldGenerator.instance.GetSeed() == worldSeed)
            {
                return WorldGenerator.instance;
            }

            string key = string.IsNullOrWhiteSpace(worldSeedName)
                ? worldSeed.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : worldSeedName.Trim();
            if (Generators.TryGetValue(key, out WorldGenerator generator))
            {
                return generator;
            }

            if (WorldGeneratorConstructor == null)
            {
                return WorldGenerator.instance;
            }

            World world = CreateWorld(worldSeed, worldSeedName);
            generator = (WorldGenerator)WorldGeneratorConstructor.Invoke(new object[] { world });
            Generators[key] = generator;
            return generator;
        }

        private static World CreateWorld(int worldSeed, string worldSeedName)
        {
            string seedName = worldSeedName ?? string.Empty;
            World world = string.IsNullOrWhiteSpace(seedName)
                ? new World()
                : new World($"creative_source_{worldSeed}", seedName);

            world.m_fileName = world.m_name = $"creative_source_{worldSeed}";
            world.m_seedName = seedName;
            world.m_seed = worldSeed;
            world.m_worldGenVersion = Version.m_worldGenVersion;
            return world;
        }
    }
}
