using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PraetorisClient
{
    internal static class CreativeCommandZoneState
    {
        private const int ProtocolVersion = 1;
        private const string DeniedMessage = "Creative commands can only be used inside your creative zone.";
        private static bool _active;
        private static Vector3 _center;
        private static float _radius;
        private static long _ownerPlayerId;
        private static long _playerId;
        private static string _slotId = string.Empty;

        internal static void OnState(long sender, ZPackage package)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer())
            {
                return;
            }

            try
            {
                int version = package.ReadInt();
                if (version != ProtocolVersion)
                {
                    PraetorisClientPlugin.Log.LogWarning($"Ignoring creative command zone state protocol {version}; expected {ProtocolVersion}.");
                    return;
                }

                bool active = package.ReadBool();
                Vector3 center = package.ReadVector3();
                float radius = package.ReadSingle();
                long ownerPlayerId = package.ReadLong();
                long playerId = package.ReadLong();
                string slotId = package.ReadString();

                if (!active || radius <= 0f || playerId == 0L)
                {
                    Clear();
                    return;
                }

                _active = true;
                _center = center;
                _radius = radius;
                _ownerPlayerId = ownerPlayerId;
                _playerId = playerId;
                _slotId = slotId ?? string.Empty;
                PraetorisClientPlugin.Log.LogInfo($"Creative command zone active: {_slotId} at {_center.x:0.##},{_center.z:0.##} radius {_radius:0.##}.");
            }
            catch (Exception ex)
            {
                Clear();
                PraetorisClientPlugin.Log.LogWarning($"Failed to apply creative command zone state: {ex}");
            }
        }

        internal static void Clear()
        {
            _active = false;
            _center = Vector3.zero;
            _radius = 0f;
            _ownerPlayerId = 0L;
            _playerId = 0L;
            _slotId = string.Empty;
        }

        internal static bool CanRunCommand(Terminal.ConsoleEventArgs args)
        {
            if (args == null ||
                ZNet.instance == null ||
                ZNet.instance.IsServer() ||
                !IsProtectedCommand(args.FullLine))
            {
                return true;
            }

            if (IsLocalPlayerInsideActiveZone())
            {
                return true;
            }

            Terminal context = args.Context ?? global::Console.instance;
            context?.AddString(DeniedMessage);
            return false;
        }

        private static bool IsProtectedCommand(string rawCommand)
        {
            if (!PraetorisClientPlugin.EnableCreativeCommandZoneGuard.Value)
            {
                return false;
            }

            string normalized = NormalizeCommand(rawCommand);
            if (normalized.Length == 0)
            {
                return false;
            }

            foreach (string protectedCommand in GetProtectedCommandPrefixes())
            {
                if (normalized.StartsWith(protectedCommand, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsLocalPlayerInsideActiveZone()
        {
            Player player = Player.m_localPlayer;
            if (!_active || player == null || _radius <= 0f)
            {
                return false;
            }

            if (_playerId != 0L && player.GetPlayerID() != _playerId)
            {
                return false;
            }

            Vector3 position = player.transform.position;
            float dx = position.x - _center.x;
            float dz = position.z - _center.z;
            return dx * dx + dz * dz <= _radius * _radius;
        }

        private static IEnumerable<string> GetProtectedCommandPrefixes()
        {
            return PraetorisClientPlugin.CreativeCommandZoneProtectedCommands.Value
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim().ToLowerInvariant())
                .Where(value => value.Length > 0);
        }

        private static string NormalizeCommand(string rawCommand)
        {
            if (string.IsNullOrWhiteSpace(rawCommand))
            {
                return string.Empty;
            }

            string[] parts = rawCommand.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? string.Empty : parts[0].ToLowerInvariant();
        }
    }
}
