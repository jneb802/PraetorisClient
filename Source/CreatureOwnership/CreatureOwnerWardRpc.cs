namespace PraetorisClient.CreatureOwnership
{
    internal static class CreatureOwnerWardRpc
    {
        internal static readonly int OwnerNameHash = "PraetorisOwnerWard_ownerName".GetStableHashCode();

        internal static void Register(ZRoutedRpc rpc)
        {
            rpc.Register<ZDOID, string>(RpcNames.CreatureOwnerWardSetOwner, OnSetOwner);
            rpc.Register<ZDOID, bool>(RpcNames.CreatureOwnerWardSetEnabled, OnSetEnabled);
        }

        private static void OnSetOwner(long sender, ZDOID wardId, string ownerName)
        {
            ZDO? zdo = GetWritableWard(wardId, sender);
            if (zdo == null)
            {
                return;
            }

            string cleanedOwnerName = (ownerName ?? "").Trim();
            zdo.Set(OwnerNameHash, cleanedOwnerName);
            DebugLog("Owner set to '" + cleanedOwnerName + "' on " + wardId + ".");
        }

        private static void OnSetEnabled(long sender, ZDOID wardId, bool enabled)
        {
            ZDO? zdo = GetWritableWard(wardId, sender);
            if (zdo == null)
            {
                return;
            }

            zdo.Set(ZDOVars.s_enabled, enabled);
            DebugLog("Owner ward " + wardId + " enabled=" + enabled + ".");
        }

        private static ZDO? GetWritableWard(ZDOID wardId, long sender)
        {
            ZDO? zdo = GetWard(wardId);
            if (zdo == null)
            {
                return null;
            }

            if (!PlayerResolver.TryGetSenderPlayerId(sender, out long playerId, out _))
            {
                return null;
            }

            return playerId != 0L ? zdo : null;
        }

        private static ZDO? GetWard(ZDOID wardId)
        {
            if (ZNet.instance == null ||
                !ZNet.instance.IsServer() ||
                ZDOMan.instance == null ||
                wardId.IsNone())
            {
                return null;
            }

            ZDO zdo = ZDOMan.instance.GetZDO(wardId);
            if (zdo == null || zdo.GetPrefab() != CreatureOwnerWardPiece.PrefabName.GetStableHashCode())
            {
                return null;
            }

            return zdo;
        }

        private static void DebugLog(string message)
        {
            if (PraetorisClientPlugin.DebugCreatureOwnerWard.Value)
            {
                PraetorisClientPlugin.Log.LogInfo(message);
            }
        }
    }
}
