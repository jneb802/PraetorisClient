using System.Collections.Generic;
using ValheimRcon;
using ValheimRcon.Commands;

namespace PraetorisClient.Maintenance
{
    internal static class MaintenanceRconCommand
    {
        private static bool _registered;

        internal static void Register()
        {
            if (_registered)
            {
                return;
            }

            _registered = true;
            RconProxy.Instance.RegisterCommand("maintenance_start", "Start server maintenance. Usage: maintenance_start <minutes>", Start);
            RconProxy.Instance.RegisterCommand("maintenance_status", "Print server maintenance status.", Status);
            RconProxy.Instance.RegisterCommand("maintenance_end", "End server maintenance.", End);
            PraetorisClientPlugin.Log.LogInfo("Registered maintenance RCON commands.");
        }

        private static CommandResult Start(CommandArgs args)
        {
            IReadOnlyList<string> arguments = args.Arguments;
            if (arguments.Count != 1)
            {
                return Result("Usage: maintenance_start <minutes>");
            }

            return Result(MaintenanceMode.Start(arguments[0]));
        }

        private static CommandResult Status(CommandArgs args)
        {
            return Result(MaintenanceMode.Status());
        }

        private static CommandResult End(CommandArgs args)
        {
            return Result(MaintenanceMode.End());
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
