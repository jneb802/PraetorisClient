using System;
using System.IO;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace PraetorisClient.GuardStoneFeature
{
    internal static class SpawnIslandWards
    {
        internal const string BlockedMessage = "You cannot build wards on spawn island.";
        private static string? _cachedValue;
        private static SpawnIslandBoundary? _boundary;
        private static SpawnIslandSurvey? _survey;
        private static WorldGenerator? _surveyWorld;
        private static long _surveyWorldId;
        private static Terminal? _output;

        internal static void Register()
        {
            _ = new Terminal.ConsoleCommand("spawnisland_generate",
                "Survey spawn island once for this world. Usage: spawnisland_generate [radiusMetres: 2048 or 4096]. Writes a candidate and SVG preview; does not activate it.",
                args =>
                {
                    int radius = 2048;
                    if (args.Length > 2 || (args.Length == 2 && (!int.TryParse(args[1], out radius) ||
                        (radius != 2048 && radius != 4096))))
                    {
                        args.Context.AddString("Usage: spawnisland_generate [2048|4096]");
                        return;
                    }
                    RunCommand(args.Context, () => BeginSurvey(radius, args.Context));
                }, onlyServer: true);
            _ = new Terminal.ConsoleCommand("spawnisland_activate",
                "Activate the saved candidate for this world after reviewing its SVG coastline.",
                args => RunCommand(args.Context, Activate), onlyServer: true);
            _ = new Terminal.ConsoleCommand("spawnisland_status", "Show the saved spawn island ward restriction.",
                args => args.Context.AddString(Status()));
        }

        private static void RunCommand(Terminal output, Func<string> command)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || WorldGenerator.instance == null)
            {
                output.AddString("Run this command on the server with the season world loaded.");
                return;
            }
            try { output.AddString(command()); }
            catch (Exception exception)
            {
                output.AddString("Spawn island command failed: " + exception.Message);
                PraetorisClientPlugin.Log.LogWarning("Spawn island command failed: " + exception.Message);
            }
        }

        private static string CandidatePath(long worldId) => Path.Combine(Paths.ConfigPath,
            "PraetorisClient", "spawn-island", worldId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".boundary");

        private static string BeginSurvey(int radius, Terminal output)
        {
            if (_survey != null) return "An island survey is already running.";
            long worldId = ZNet.instance.GetWorldUID();
            ReadBoundary();
            if (_boundary != null && _boundary.WorldId == worldId)
                return "This world already has an active boundary. It will not be recalculated.";
            string path = CandidatePath(worldId);
            if (File.Exists(path))
                return "This world already has a saved candidate: " + path + ". Review its SVG and run spawnisland_activate.";
            if (ZoneSystem.instance == null || !ZoneSystem.instance.GetLocationIcon("StartTemple", out Vector3 spawn))
                return "The original spawn temple is not available yet. No boundary was generated.";

            WorldGenerator generator = WorldGenerator.instance;
            _survey = new SpawnIslandSurvey(worldId, spawn.x, spawn.z, radius,
                ZoneSystem.instance.m_waterLevel, (x, z) => generator.GetHeight((float)x, (float)z));
            _surveyWorld = generator;
            _surveyWorldId = worldId;
            _output = output;
            return "Started spawn island survey. Terrain samples run over multiple frames. The result must be reviewed before activation.";
        }

        internal static void Update()
        {
            if (_survey == null) return;
            try
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer() ||
                    !ReferenceEquals(WorldGenerator.instance, _surveyWorld) || ZNet.instance.GetWorldUID() != _surveyWorldId)
                    throw new InvalidOperationException("World changed during the survey; no boundary was saved.");
                _survey.Step();
                if (!_survey.Complete) return;
                SpawnIslandBoundary boundary = _survey.Build();
                string path = CandidatePath(boundary.WorldId);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                // Write the preview first. A candidate is never overwritten automatically.
                File.WriteAllText(path + ".svg", boundary.ToSvg());
                using (FileStream file = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                using (StreamWriter writer = new StreamWriter(file)) writer.Write(boundary.Encode());
                Report("Saved spawn island candidate after " + _survey.Samples + " terrain samples: " + path +
                    ". Review " + path + ".svg against the world map, then run spawnisland_activate.");
                ClearSurvey();
            }
            catch (Exception exception)
            {
                Report("Spawn island survey failed: " + exception.Message);
                ClearSurvey();
            }
        }

        internal static void ClearSurvey()
        {
            _survey = null;
            _surveyWorld = null;
            _output = null;
        }

        private static void Report(string message)
        {
            PraetorisClientPlugin.Log.LogInfo(message);
            if (_output != null) _output.AddString(message);
        }

        private static string Activate()
        {
            PraetorisClientPlugin plugin = PraetorisClientPlugin.Instance ??
                throw new InvalidOperationException("PraetorisClient is not loaded.");
            long worldId = ZNet.instance.GetWorldUID();
            string path = CandidatePath(worldId);
            FileInfo file = new FileInfo(path);
            if (!file.Exists) return "No candidate exists for this world. Run spawnisland_generate first.";
            if (file.Length > SpawnIslandBoundary.MaximumEncodedLength)
                throw new InvalidDataException("Candidate exceeds the size limit.");
            string encoded = File.ReadAllText(path).Trim();
            SpawnIslandBoundary candidate = SpawnIslandBoundary.Decode(encoded);
            if (candidate.WorldId != worldId)
                throw new InvalidDataException("Candidate belongs to a different world.");
            ReadBoundary();
            if (_boundary != null && _boundary.WorldId == worldId)
                return "This world already has an active boundary. It will not be replaced.";
            string previous = PraetorisClientPlugin.SpawnIslandWardBoundary.Value;
            try
            {
                PraetorisClientPlugin.SpawnIslandWardBoundary.Value = encoded;
                plugin.Config.Save();
                // Jotunn sends changed synchronized entries on ConfigReloaded, not Config.Save.
                plugin.Config.Reload();
            }
            catch
            {
                PraetorisClientPlugin.SpawnIslandWardBoundary.Value = previous;
                throw;
            }
            return PraetorisClientPlugin.SpawnIslandWardsEnabled.Value
                ? "Activated the saved spawn island boundary for this world. Wards are now blocked inside it. Keep the config and candidate for the entire season."
                : "Saved the spawn island boundary for this world. The rule is disabled by SpawnIslandWards.Enabled. Enable it to block wards inside the boundary.";
        }

        private static void ReadBoundary()
        {
            string value = PraetorisClientPlugin.SpawnIslandWardBoundary.Value;
            if (value == _cachedValue) return;
            _cachedValue = value;
            _boundary = null;
            if (string.IsNullOrEmpty(value)) return;
            try { _boundary = SpawnIslandBoundary.Decode(value); }
            catch (Exception exception)
            {
                PraetorisClientPlugin.Log.LogWarning("Invalid spawn island boundary; ward placement is blocked until corrected: " + exception.Message);
            }
        }

        internal static string? GetBlockReason(Vector3 position)
        {
            if (!PraetorisClientPlugin.SpawnIslandWardsEnabled.Value) return null;
            ReadBoundary();
            if (string.IsNullOrEmpty(_cachedValue)) return null;
            if (_boundary == null || ZNet.instance == null || _boundary.WorldId != ZNet.instance.GetWorldUID())
                return "Ward placement is unavailable until an administrator sets the spawn island boundary for this world.";
            return _boundary.Contains(position.x, position.z) ? BlockedMessage : null;
        }

        private static string Status()
        {
            ReadBoundary();
            if (_survey != null) return "Spawn island survey in progress; terrain samples: " + _survey.Samples + ".";
            if (!PraetorisClientPlugin.SpawnIslandWardsEnabled.Value)
                return "Spawn island ward restriction is disabled by SpawnIslandWards.Enabled. Any saved boundary is retained.";
            if (string.IsNullOrEmpty(_cachedValue)) return "No active spawn island boundary. This rule is inactive.";
            if (_boundary == null) return "Invalid spawn island boundary. All new wards are blocked until corrected.";
            if (ZNet.instance == null || _boundary.WorldId != ZNet.instance.GetWorldUID())
                return "Saved boundary belongs to a different world. Generate and activate this season's boundary before building wards.";
            return "Saved spawn island boundary is active for this world. No terrain recalculation is scheduled.";
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
    internal static class SpawnIslandWardPlacementPatch
    {
        private static readonly Action<Player, bool> RefreshGhost =
            AccessTools.MethodDelegate<Action<Player, bool>>(AccessTools.Method(typeof(Player), "UpdatePlacementGhost"));
        internal static readonly AccessTools.FieldRef<Player, GameObject> Ghost =
            AccessTools.FieldRefAccess<Player, GameObject>("m_placementGhost");

        private static bool Prefix(Player __instance, Piece piece, ref bool __result)
        {
            if (!GuardStoneBuildLimit.IsGuardStone(piece)) return true;
            // Match the actual snapped placement position, not the player's feet or last frame.
            RefreshGhost(__instance, false);
            GameObject ghost = Ghost(__instance);
            if (ghost == null || !ghost.activeSelf) return true;
            string? reason = SpawnIslandWards.GetBlockReason(ghost.transform.position);
            if (reason == null) return true;
            __instance.Message(MessageHud.MessageType.Center, reason);
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
    internal static class SpawnIslandWardPreviewPatch
    {
        private static readonly AccessTools.FieldRef<Player, Player.PlacementStatus> PlacementStatus =
            AccessTools.FieldRefAccess<Player, Player.PlacementStatus>("m_placementStatus");
        private static readonly Action<Player, bool> SetGhostValid =
            AccessTools.MethodDelegate<Action<Player, bool>>(AccessTools.Method(typeof(Player), "SetPlacementGhostValid"));

        private static void Postfix(Player __instance)
        {
            GameObject ghost = SpawnIslandWardPlacementPatch.Ghost(__instance);
            if (ghost == null || !ghost.activeSelf || !GuardStoneBuildLimit.IsGuardStone(ghost.GetComponent<Piece>()) ||
                SpawnIslandWards.GetBlockReason(ghost.transform.position) == null) return;
            PlacementStatus(__instance) = Player.PlacementStatus.Invalid;
            SetGhostValid(__instance, false);
        }
    }
}
