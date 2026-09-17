namespace PraetorisClient.ServerChestFeature
{
    internal static class ServerChestLog
    {
        internal static void Debug(string message)
        {
            if (PraetorisClientPlugin.DebugServerChest != null && PraetorisClientPlugin.DebugServerChest.Value)
            {
                PraetorisClientPlugin.Log.LogInfo("[ServerChest] " + message);
            }
        }

        internal static void Warning(string message)
        {
            PraetorisClientPlugin.Log.LogWarning("[ServerChest] " + message);
        }
    }
}
