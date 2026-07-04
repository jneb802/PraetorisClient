using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PraetorisClient.CreatureOwnership
{
    internal static class CreatureOwnerWardCommand
    {
        private static bool _registered;

        internal static void Register()
        {
            if (_registered)
            {
                return;
            }

            _registered = true;
            _ = new Terminal.ConsoleCommand(
                "ownerward_set_nearest",
                "Set the nearest Creature Owner Ward owner. Usage: ownerward_set_nearest <ownerName> [radius]",
                args =>
                {
                    if (args.Length < 2)
                    {
                        args.Context.AddString("Usage: ownerward_set_nearest <ownerName> [radius]");
                        return;
                    }

                    Player player = Player.m_localPlayer;
                    if (player == null)
                    {
                        args.Context.AddString("No local player found.");
                        return;
                    }

                    float radius = ParseRadius(args, 2, 20.0f);
                    CreatureOwnerWard? ownerWard = FindNearest(player.transform.position, radius, out float distance);
                    if (ownerWard == null)
                    {
                        args.Context.AddString("No Creature Owner Ward found within " + radius.ToString("0.#", CultureInfo.InvariantCulture) + "m.");
                        return;
                    }

                    args.Context.AddString(ownerWard.CommandSetOwner(player, args[1]));
                    args.Context.AddString("Nearest Creature Owner Ward distance=" + distance.ToString("0.#", CultureInfo.InvariantCulture) + "m.");
                },
                onlyAdmin: true);

            _ = new Terminal.ConsoleCommand(
                "ownerward_enable_nearest",
                "Activate or deactivate the nearest Creature Owner Ward. Usage: ownerward_enable_nearest [true|false] [radius]",
                args =>
                {
                    Player player = Player.m_localPlayer;
                    if (player == null)
                    {
                        args.Context.AddString("No local player found.");
                        return;
                    }

                    bool enabled = true;
                    if (args.Length >= 2)
                    {
                        if (!bool.TryParse(args[1], out enabled))
                        {
                            args.Context.AddString("Usage: ownerward_enable_nearest [true|false] [radius]");
                            return;
                        }
                    }

                    int radiusIndex = args.Length >= 2 ? 2 : 1;
                    float radius = ParseRadius(args, radiusIndex, 20.0f);
                    CreatureOwnerWard? ownerWard = FindNearest(player.transform.position, radius, out float distance);
                    if (ownerWard == null)
                    {
                        args.Context.AddString("No Creature Owner Ward found within " + radius.ToString("0.#", CultureInfo.InvariantCulture) + "m.");
                        return;
                    }

                    args.Context.AddString(ownerWard.CommandSetEnabled(player, enabled));
                    args.Context.AddString("Nearest Creature Owner Ward distance=" + distance.ToString("0.#", CultureInfo.InvariantCulture) + "m.");
                },
                onlyAdmin: true);

            _ = new Terminal.ConsoleCommand(
                "ownerward_status_nearest",
                "Print nearest Creature Owner Ward status. Usage: ownerward_status_nearest [radius]",
                args =>
                {
                    Player player = Player.m_localPlayer;
                    if (player == null)
                    {
                        args.Context.AddString("No local player found.");
                        return;
                    }

                    float radius = ParseRadius(args, 1, 20.0f);
                    CreatureOwnerWard? ownerWard = FindNearest(player.transform.position, radius, out float distance);
                    if (ownerWard == null)
                    {
                        args.Context.AddString("No Creature Owner Ward found within " + radius.ToString("0.#", CultureInfo.InvariantCulture) + "m.");
                        return;
                    }

                    args.Context.AddString(ownerWard.CommandStatus());
                    args.Context.AddString("Nearest Creature Owner Ward distance=" + distance.ToString("0.#", CultureInfo.InvariantCulture) + "m.");
                },
                onlyAdmin: true);
        }

        private static float ParseRadius(Terminal.ConsoleEventArgs args, int index, float defaultRadius)
        {
            if (args.Length <= index)
            {
                return defaultRadius;
            }

            if (!float.TryParse(args[index], NumberStyles.Float, CultureInfo.InvariantCulture, out float radius))
            {
                return defaultRadius;
            }

            return Mathf.Clamp(radius, 1.0f, 100.0f);
        }

        private static CreatureOwnerWard? FindNearest(Vector3 center, float radius, out float distance)
        {
            distance = float.MaxValue;
            CreatureOwnerWard? best = null;
            HashSet<CreatureOwnerWard> seen = new HashSet<CreatureOwnerWard>();
            foreach (Collider collider in Physics.OverlapSphere(center, radius))
            {
                CreatureOwnerWard ownerWard = collider.GetComponentInParent<CreatureOwnerWard>();
                if (ownerWard == null || !seen.Add(ownerWard))
                {
                    continue;
                }

                float current = Vector3.Distance(center, ownerWard.transform.position);
                if (current < distance)
                {
                    distance = current;
                    best = ownerWard;
                }
            }

            return best;
        }
    }
}
