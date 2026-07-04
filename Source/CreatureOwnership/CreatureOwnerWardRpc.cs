namespace PraetorisClient.CreatureOwnership
{
    internal static class CreatureOwnerWardRpc
    {
        internal static readonly int OwnerNameHash = "PraetorisOwnerWard_ownerName".GetStableHashCode();

        internal static void Register(ZRoutedRpc rpc)
        {
            rpc.Register<ZDOID, long, string>(RpcNames.CreatureOwnerWardSetOwner, OnSetOwner);
            rpc.Register<ZDOID, long, bool>(RpcNames.CreatureOwnerWardSetEnabled, OnSetEnabled);
            rpc.Register<ZDOID, long>(RpcNames.CreatureOwnerWardUpdate, OnUpdate);
        }

        private static void OnSetOwner(long sender, ZDOID wardId, long playerId, string ownerName)
        {
            ZDO? zdo = GetWritableWard(wardId, playerId);
            if (zdo == null)
            {
                return;
            }

            string cleanedOwnerName = (ownerName ?? "").Trim();
            zdo.Set(OwnerNameHash, cleanedOwnerName);
            DebugLog("Owner set to '" + cleanedOwnerName + "' on " + wardId + ".");
        }

        private static void OnSetEnabled(long sender, ZDOID wardId, long playerId, bool enabled)
        {
            ZDO? zdo = GetWritableWard(wardId, playerId);
            if (zdo == null)
            {
                return;
            }

            zdo.Set(ZDOVars.s_enabled, enabled);
            DebugLog("Owner ward " + wardId + " enabled=" + enabled + ".");
        }

        private static void OnUpdate(long sender, ZDOID wardId, long playerId)
        {
            ZDO? zdo = GetWard(wardId);
            if (zdo == null)
            {
                return;
            }

            CreatureOwnerWardServer.UpdateWard(zdo);
        }

        private static ZDO? GetWritableWard(ZDOID wardId, long playerId)
        {
            ZDO? zdo = GetWard(wardId);
            if (zdo == null)
            {
                return null;
            }

            long creator = zdo.GetLong(ZDOVars.s_creator);
            return creator == 0L || creator == playerId ? zdo : null;
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
