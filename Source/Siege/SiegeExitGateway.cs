using UnityEngine;

namespace PraetorisClient
{
    public class SiegeExitGateway : MonoBehaviour
    {
        internal bool TryExit(Player player)
        {
            return SiegePortalBridge.RequestSiegeReturn(player);
        }
    }
}
