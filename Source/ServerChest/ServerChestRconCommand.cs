using System.Collections.Generic;
using ValheimRcon;
using ValheimRcon.Commands;

namespace PraetorisClient.ServerChestFeature
{
    internal static class ServerChestRconCommand
    {
        internal const string ValheimRconGuid = "org.tristan.rcon";

        private static bool _registered;

        internal static void Register()
        {
            if (_registered)
            {
                return;
            }

            _registered = true;
            RconProxy.Instance.RegisterCommand("serverchest_send", "Send item(s) to a registered ServerChest. Usage: serverchest_send <characterName> <itemPrefab> <amount> [quality]", Send);
            RconProxy.Instance.RegisterCommand("serverchest_send_bulk", "Send multiple items to a registered ServerChest. Usage: serverchest_send_bulk <characterName> <itemPrefab>:<amount>[:quality] ...", SendBulk);
            RconProxy.Instance.RegisterCommand("serverchest_status", "Print ServerChest status. Usage: serverchest_status <characterName>", Status);
            RconProxy.Instance.RegisterCommand("serverchest_find", "Find registered ServerChests. Usage: serverchest_find <characterName>", Find);
            PraetorisClientPlugin.Log.LogInfo("Registered ServerChest RCON commands.");
        }

        private static CommandResult Send(CommandArgs args)
        {
            IReadOnlyList<string> arguments = args.Arguments;
            if (arguments.Count < 3)
            {
                return Result("Usage: serverchest_send <characterName> <itemPrefab> <amount> [quality]");
            }

            if (!ServerChestCommand.TryParsePositiveInt(arguments[2], out int amount))
            {
                return Result("Amount must be greater than zero.");
            }

            int quality = 1;
            if (arguments.Count >= 4 && !ServerChestCommand.TryParsePositiveInt(arguments[3], out quality))
            {
                return Result("Quality must be greater than zero.");
            }

            List<ServerChestService.SendItem> items = new()
            {
                new ServerChestService.SendItem
                {
                    PrefabName = arguments[1],
                    Amount = amount,
                    Quality = quality
                }
            };

            return Result(ServerChestService.SendItems(arguments[0], items).Message);
        }

        private static CommandResult SendBulk(CommandArgs args)
        {
            IReadOnlyList<string> arguments = args.Arguments;
            if (arguments.Count < 2)
            {
                return Result("Usage: serverchest_send_bulk <characterName> <itemPrefab>:<amount>[:quality] ...");
            }

            List<ServerChestService.SendItem> items = new();
            for (int index = 1; index < arguments.Count; index++)
            {
                if (!ServerChestCommand.TryParseBulkItem(arguments[index], out ServerChestService.SendItem item, out string error))
                {
                    return Result(error);
                }

                items.Add(item);
            }

            return Result(ServerChestService.SendItems(arguments[0], items).Message);
        }

        private static CommandResult Status(CommandArgs args)
        {
            IReadOnlyList<string> arguments = args.Arguments;
            if (arguments.Count < 1)
            {
                return Result("Usage: serverchest_status <characterName>");
            }

            return Result(ServerChestService.Status(arguments[0]).Message);
        }

        private static CommandResult Find(CommandArgs args)
        {
            IReadOnlyList<string> arguments = args.Arguments;
            string query = arguments.Count >= 1 ? arguments[0] : "";
            return Result(ServerChestService.Find(query).Message);
        }

        private static CommandResult Result(string message)
        {
            return new CommandResult
            {
                Text = message
            };
        }
    }
}
