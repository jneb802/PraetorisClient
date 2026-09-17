using Splatform;

namespace PraetorisClient.ServerChestFeature
{
    internal static class ServerChestIdentity
    {
        internal static string GetLocalPlatformId()
        {
            try
            {
                PlatformUserID userId = PlatformManager.DistributionPlatform.LocalUser.PlatformUserID;
                return userId.ToString();
            }
            catch
            {
                return "";
            }
        }

        internal static bool TryGetSenderIdentity(long sender, string requestedName, string requestedPlatformId, out string characterName, out string platformId)
        {
            characterName = (requestedName ?? "").Trim();
            platformId = (requestedPlatformId ?? "").Trim();

            ZNetPeer? peer = PlayerResolver.FindPeerBySender(sender);
            if (peer != null)
            {
                if (!string.IsNullOrWhiteSpace(peer.m_playerName))
                {
                    characterName = peer.m_playerName.Trim();
                }

                string stableId = PlayerResolver.StablePlayerId(peer);
                if (!string.IsNullOrWhiteSpace(stableId))
                {
                    platformId = stableId.Trim();
                }
            }
            else if (sender == ZNet.GetUID())
            {
                if (Game.instance != null && Game.instance.GetPlayerProfile() != null)
                {
                    characterName = Game.instance.GetPlayerProfile().GetName();
                }

                string localPlatformId = GetLocalPlatformId();
                if (!string.IsNullOrWhiteSpace(localPlatformId))
                {
                    platformId = localPlatformId;
                }
            }

            return !string.IsNullOrWhiteSpace(characterName) && !string.IsNullOrWhiteSpace(platformId);
        }
    }
}
