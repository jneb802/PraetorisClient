namespace PraetorisClient.Maintenance
{
    internal static class MaintenanceCommand
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
                "maintenance_start",
                "Start server maintenance. Usage: maintenance_start <minutes>",
                args =>
                {
                    if (args.Length != 2)
                    {
                        args.Context.AddString("Usage: maintenance_start <minutes>");
                        return;
                    }

                    args.Context.AddString(MaintenanceMode.Start(args[1]));
                },
                onlyServer: true);

            _ = new Terminal.ConsoleCommand(
                "maintenance_status",
                "Print server maintenance status.",
                args => args.Context.AddString(MaintenanceMode.Status()),
                onlyServer: true);

            _ = new Terminal.ConsoleCommand(
                "maintenance_end",
                "End server maintenance.",
                args => args.Context.AddString(MaintenanceMode.End()),
                onlyServer: true);
        }
    }
}
