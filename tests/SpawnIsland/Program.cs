using System;
using System.IO;
using PraetorisClient.GuardStoneFeature;

internal static class Program
{
    private static int _checks;

    private static void Main()
    {
        Func<double, double, double> terrain = (x, z) =>
            (Math.Abs(x) < 40 && Math.Abs(z) < 40) || (x >= 80 && x < 104 && Math.Abs(z) < 16) ? 40 : 20;
        SpawnIslandBoundary island = Survey(terrain);
        Check(island.Contains(0, 0), "spawn land is protected");
        Check(island.Contains(-39, -39), "negative coordinates are protected");
        Check(island.Contains(43, 0), "coastal margin is protected");
        Check(!island.Contains(49, 0), "open water beyond the margin is unprotected");
        Check(!island.Contains(90, 0), "separate island is unprotected");
        Check(!island.Contains(double.NaN, 0) && !island.Contains(double.PositiveInfinity, 0), "non-finite points are rejected");
        Check(!island.Contains(double.MaxValue, 0), "distant positions do not overflow into the island");

        SpawnIslandBoundary restored = SpawnIslandBoundary.Decode(island.Encode());
        Check(restored.WorldId == 123, "world identity survives serialization");
        for (int z = -160; z <= 160; z += 4)
            for (int x = -160; x <= 160; x += 4)
                if (restored.Contains(x, z) != island.Contains(x, z))
                    throw new Exception("Round-trip changed the boundary at " + x + "," + z);
        Check(true, "all sampled positions retain the same boundary after reload");

        // A lake away from the starting cell is filled, but a bay open to the sea is not.
        SpawnIslandBoundary lake = Survey((x, z) =>
            Math.Abs(x) < 72 && Math.Abs(z) < 72 && !(x >= 16 && x < 48 && z >= 16 && z < 48) ? 40 : 20);
        Check(lake.Contains(32, 32), "enclosed lakes are protected");
        SpawnIslandBoundary bay = Survey((x, z) =>
            Math.Abs(x) < 72 && Math.Abs(z) < 72 && !(x >= 16 && z >= 16 && z < 64) ? 40 : 20);
        Check(!bay.Contains(48, 40), "a wide bay remains open");

        SpawnIslandBoundary diagonal = Survey((x, z) =>
            (x >= -16 && x < 16 && z >= -16 && z < 16) ||
            (x >= 16 && x < 64 && z >= 16 && z < 64) ? 40 : 20);
        Check(!diagonal.Contains(48, 48), "diagonal contact does not join land masses");
        SpawnIslandBoundary bridge = Survey((x, z) =>
            (Math.Abs(x) < 16 && Math.Abs(z) < 16) ||
            (x >= 0 && x < 80 && Math.Abs(z) < 8) ||
            (x >= 64 && x < 104 && Math.Abs(z) < 24) ? 40 : 20);
        Check(bridge.Contains(88, 0), "a sampled land bridge connects both shores");

        SpawnIslandBoundary shifted = Survey((x, z) => Math.Abs(x + 200) < 40 && Math.Abs(z - 300) < 40 ? 40 : 20, -200, 300);
        Check(shifted.Contains(-200, 300) && !shifted.Contains(0, 0), "spawn location is not assumed to be the world origin");

        double mutableHeight = 40;
        SpawnIslandBoundary frozen = Survey((x, z) => Math.Abs(x) < 40 && Math.Abs(z) < 40 ? mutableHeight : 20);
        mutableHeight = 0;
        Check(frozen.Contains(0, 0), "terrain changes cannot change the saved map");

        ExpectFailure(() => Survey((x, z) => 20), "underwater spawn fails instead of saving an empty map");
        ExpectFailure(() => Survey((x, z) => 40), "survey refuses to truncate a large island");
        ExpectFailure(() => Survey((x, z) => double.NaN), "invalid terrain height fails");
        ExpectFailure(() => SpawnIslandBoundary.Decode("not base64"), "corrupt config fails");
        ExpectFailure(() => SpawnIslandBoundary.Decode(Convert.ToBase64String(new byte[] { 1, 2, 3 })), "truncated compressed data fails");
        ExpectFailure(() => SpawnIslandBoundary.Decode(new string('A', SpawnIslandBoundary.MaximumEncodedLength + 1)), "oversized config fails");
        ExpectFailure(() => new SpawnIslandBoundary(1, 0, 0, 3, new byte[2]), "empty map fails");
        ExpectFailure(() => new SpawnIslandSurvey(1, 0, 0, 8192, 30, terrain), "oversized survey fails");
        ExpectFailure(() => new SpawnIslandSurvey(1, 0, 0, 128, 30, terrain).Build(), "incomplete survey cannot be saved");

        byte[] singleCell = new byte[2];
        singleCell[0] = 1 << 4;
        SpawnIslandBoundary precise = new SpawnIslandBoundary(1, -2, -2, 3, singleCell);
        Check(precise.Contains(-8, -8) && precise.Contains(-0.01, -0.01), "negative grid cell includes its lower edge");
        Check(!precise.Contains(0, -1) && !precise.Contains(-8.01, -1), "grid cell excludes points across either edge");
        SpawnIslandBoundary maximum = Survey(terrain, radius: 4096);
        Check(maximum.Width == SpawnIslandBoundary.MaximumWidth && maximum.Contains(0, 0), "maximum survey size completes");
        Check(SpawnIslandBoundary.Decode(maximum.Encode()).Contains(0, 0), "maximum-size map reloads");
        System.Xml.Linq.XDocument.Parse(island.ToSvg());
        Check(true, "preview is valid XML");
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "spawn-island-test-preview.svg"), island.ToSvg());
        System.Console.WriteLine("PASS: " + _checks + " boundary checks.");
    }

    private static SpawnIslandBoundary Survey(Func<double, double, double> terrain, double spawnX = 0, double spawnZ = 0, int radius = 128)
    {
        SpawnIslandSurvey survey = new SpawnIslandSurvey(123, spawnX, spawnZ, radius, 30, terrain);
        for (int steps = 0; !survey.Complete; steps++)
        {
            if (steps > 1000) throw new Exception("Survey failed to finish.");
            survey.Step();
        }
        return survey.Build();
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        _checks++;
        System.Console.WriteLine("PASS: " + description);
    }

    private static void ExpectFailure(Action action, string description)
    {
        try { action(); }
        catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is FormatException ||
                                           exception is ArgumentException || exception is InvalidOperationException)
        {
            Check(true, description);
            return;
        }
        throw new Exception("FAIL: " + description);
    }
}
