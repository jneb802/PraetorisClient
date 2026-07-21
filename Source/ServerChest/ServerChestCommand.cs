using System.Collections.Generic;
using System.Globalization;

namespace PraetorisClient.ServerChestFeature
{
    internal static class ServerChestCommand
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
                "serverchest_send",
                "Send item(s) to a registered ServerChest. Usage: serverchest_send <characterName> <itemPrefab> <amount> [quality]",
                args =>
                {
                    if (args.Length < 4)
                    {
                        args.Context.AddString("Usage: serverchest_send <characterName> <itemPrefab> <amount> [quality]");
                        return;
                    }

                    if (!TryParsePositiveInt(args[3], out int amount))
                    {
                        args.Context.AddString("Amount must be greater than zero.");
                        return;
                    }

                    int quality = 1;
                    if (args.Length >= 5 && !TryParsePositiveInt(args[4], out quality))
                    {
                        args.Context.AddString("Quality must be greater than zero.");
                        return;
                    }

                    List<ServerChestService.SendItem> items = new()
                    {
                        new ServerChestService.SendItem
                        {
                            PrefabName = args[2],
                            Amount = amount,
                            Quality = quality
                        }
                    };
                    ExecuteOrRoute(args.Context, args[1], items);
                },
                onlyAdmin: true);

            _ = new Terminal.ConsoleCommand(
                "serverchest_send_bulk",
                "Send multiple items to a registered ServerChest. Usage: serverchest_send_bulk <characterName> <itemPrefab>:<amount>[:quality] ...",
                args =>
                {
                    if (args.Length < 3)
                    {
                        args.Context.AddString("Usage: serverchest_send_bulk <characterName> <itemPrefab>:<amount>[:quality] ...");
                        return;
                    }

                    List<ServerChestService.SendItem> items = new();
                    for (int index = 2; index < args.Length; index++)
                    {
                        if (!TryParseBulkItem(args[index], out ServerChestService.SendItem item, out string error))
                        {
                            args.Context.AddString(error);
                            return;
                        }

                        items.Add(item);
                    }

                    ExecuteOrRoute(args.Context, args[1], items);
                },
                onlyAdmin: true);

            _ = new Terminal.ConsoleCommand(
                "serverchest_status",
                "Print ServerChest status. Usage: serverchest_status <characterName>",
                args =>
                {
                    if (args.Length < 2)
                    {
                        args.Context.AddString("Usage: serverchest_status <characterName>");
                        return;
                    }

                    if (ZNet.instance != null && ZNet.instance.IsServer())
                    {
                        args.Context.AddString(ServerChestService.Status(args[1]).Message);
                    }
                    else
                    {
                        ServerChestRpc.RequestStatus(args[1]);
                        args.Context.AddString("ServerChest status request submitted.");
                    }
                },
                onlyAdmin: true);

            _ = new Terminal.ConsoleCommand(
                "serverchest_find",
                "Find registered ServerChests. Usage: serverchest_find <characterName>",
                args =>
                {
                    string query = args.Length >= 2 ? args[1] : "";
                    if (ZNet.instance != null && ZNet.instance.IsServer())
                    {
                        args.Context.AddString(ServerChestService.Find(query).Message);
                    }
                    else
                    {
                        ServerChestRpc.RequestFind(query);
                        args.Context.AddString("ServerChest find request submitted.");
                    }
                },
                onlyAdmin: true);
        }

        private static void ExecuteOrRoute(Terminal context, string characterName, IReadOnlyList<ServerChestService.SendItem> items)
        {
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                ServerChestService.CommandResult result = ServerChestService.SendItems(characterName, items);
                context.AddString(result.Message);
                return;
            }

            ServerChestService.CommandResult validation = ServerChestService.ValidateItems(items);
            if (!validation.Success)
            {
                context.AddString(validation.Message);
                return;
            }

            ServerChestRpc.RequestSend(characterName, items);
            context.AddString("ServerChest send request submitted.");
        }

        private static bool TryParseBulkItem(string token, out ServerChestService.SendItem item, out string error)
        {
            item = new ServerChestService.SendItem();
            error = "";
            string[] parts = token.Split(':');
            if (parts.Length < 2 || parts.Length > 3)
            {
                error = "Bulk item must be <itemPrefab>:<amount>[:quality].";
                return false;
            }

            if (!TryParsePositiveInt(parts[1], out int amount))
            {
                error = "Amount must be greater than zero for " + parts[0] + ".";
                return false;
            }

            int quality = 1;
            if (parts.Length == 3 && !TryParsePositiveInt(parts[2], out quality))
            {
                error = "Quality must be greater than zero for " + parts[0] + ".";
                return false;
            }

            item.PrefabName = parts[0];
            item.Amount = amount;
            item.Quality = quality;
            return true;
        }

        private static bool TryParsePositiveInt(string value, out int parsed)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) && parsed > 0;
        }
    }
}
