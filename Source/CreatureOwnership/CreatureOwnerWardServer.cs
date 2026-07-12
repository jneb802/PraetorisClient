using System.Collections.Generic;
using UnityEngine;

namespace PraetorisClient.CreatureOwnership
{
    internal static class CreatureOwnerWardServer
    {
        private const int MaxLoggedCreatureIds = 4096;
        private static readonly List<ZDO> NearbyZdos = new List<ZDO>();
        private static readonly HashSet<ZDOID> LoggedCreatureIds = new HashSet<ZDOID>();

        internal static void UpdateWard(ZDO wardZdo)
        {
            if (wardZdo == null || !wardZdo.GetBool(ZDOVars.s_enabled))
            {
                return;
            }

            string ownerName = wardZdo.GetString(CreatureOwnerWardRpc.OwnerNameHash, "");
            if (ownerName.Length == 0)
            {
                ownerName = ResolveCreatorName(wardZdo);
                if (ownerName.Length > 0)
                {
                    wardZdo.Set(CreatureOwnerWardRpc.OwnerNameHash, ownerName);
                }
            }

            if (ownerName.Length == 0)
            {
                return;
            }

            ZNetPeer? ownerPeer = ResolveOwnerPeer(ownerName);
            if (ownerPeer == null)
            {
                DebugLog("Owner ward " + wardZdo.m_uid + " could not resolve connected owner '" + ownerName + "'.");
                return;
            }

            Vector3 wardPosition = wardZdo.GetPosition();
            float radius = Mathf.Max(1.0f, PraetorisClientPlugin.CreatureOwnerWardRadius.Value);
            int checkedCreatures = 0;
            int reassignedCreatures = 0;
            int sectorArea = Mathf.CeilToInt(radius / ZoneSystem.c_ZoneSize) + 1;

            NearbyZdos.Clear();
            ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(wardPosition), sectorArea, 0, NearbyZdos);
            foreach (ZDO zdo in NearbyZdos)
            {
                if (zdo.m_uid == wardZdo.m_uid ||
                    !IsCandidate(zdo, out string prefabName) ||
                    Utils.DistanceXZ(wardPosition, zdo.GetPosition()) > radius)
                {
                    continue;
                }

                checkedCreatures++;
                if (TryAssignOwner(wardZdo, zdo, prefabName, ownerPeer))
                {
                    reassignedCreatures++;
                }
            }

            if (checkedCreatures > 0)
            {
                DebugLog(
                    "Owner ward tick " + wardZdo.m_uid +
                    " owner='" + ownerPeer.m_playerName +
                    "' checked=" + checkedCreatures +
                    " reassigned=" + reassignedCreatures + ".");
            }
        }

        private static bool TryAssignOwner(ZDO wardZdo, ZDO zdo, string prefabName, ZNetPeer ownerPeer)
        {
            long previousOwner = zdo.GetOwner();
            if (PraetorisClientPlugin.DebugCreatureOwnerWard.Value)
            {
                if (LoggedCreatureIds.Count >= MaxLoggedCreatureIds)
                {
                    LoggedCreatureIds.Clear();
                }

                if (LoggedCreatureIds.Add(zdo.m_uid))
                {
                    DebugLog(
                        "Owner ward observed creature prefab=" + prefabName +
                        " zdo=" + zdo.m_uid +
                        " owner=" + previousOwner +
                        " targetOwner=" + ownerPeer.m_uid + ".");
                }
            }

            if (previousOwner == ownerPeer.m_uid)
            {
                return false;
            }

            zdo.SetOwner(ownerPeer.m_uid);
            ZDOMan.instance.ForceSendZDO(ownerPeer.m_uid, zdo.m_uid);
            DebugLog(
                "Owner ward reassigned creature '" + prefabName +
                "' by ward=" + wardZdo.m_uid +
                " zdo=" + zdo.m_uid +
                " from=" + previousOwner +
                " to=" + ownerPeer.m_uid +
                " (" + ownerPeer.m_playerName + ").");
            return true;
        }

        private static bool IsCandidate(ZDO zdo, out string prefabName)
        {
            prefabName = zdo.GetPrefab().ToString();
            if (ZNetScene.instance == null)
            {
                return false;
            }

            GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
            if (prefab == null)
            {
                return false;
            }

            prefabName = prefab.name;
            Character character = prefab.GetComponent<Character>();
            if (character == null || prefab.GetComponent<Player>() != null || prefab.GetComponent<Tameable>() != null)
            {
                return false;
            }

            if (character.m_boss || character.m_faction == Character.Faction.Boss)
            {
                return true;
            }

            return prefab.GetComponent<MonsterAI>() != null &&
                   character.m_faction != Character.Faction.Players &&
                   character.m_faction != Character.Faction.PlayerSpawned;
        }

        private static ZNetPeer? ResolveOwnerPeer(string ownerName)
        {
            string normalizedOwner = Normalize(ownerName);
            ZNetPeer? match = null;

            foreach (ZNetPeer peer in ZNet.instance.GetConnectedPeers())
            {
                if (!peer.IsReady())
                {
                    continue;
                }

                bool isMatch =
                    Normalize(peer.m_playerName) == normalizedOwner ||
                    Normalize(PlayerResolver.PlatformDisplayName(peer)) == normalizedOwner ||
                    Normalize(PlayerResolver.StablePlayerId(peer)) == normalizedOwner;
                if (!isMatch)
                {
                    continue;
                }

                if (match != null)
                {
                    return null;
                }

                match = peer;
            }

            return match;
        }

        private static string ResolveCreatorName(ZDO wardZdo)
        {
            long creatorId = wardZdo.GetLong(ZDOVars.s_creator);
            if (creatorId == 0L || ZNet.instance == null)
            {
                return "";
            }

            foreach (ZNetPeer peer in ZNet.instance.GetConnectedPeers())
            {
                if (!peer.IsReady() ||
                    !PlayerResolver.TryGetPeerPlayerId(peer, out long peerPlayerId) ||
                    peerPlayerId != creatorId)
                {
                    continue;
                }

                return peer.m_playerName ?? "";
            }

            Player creator = Player.GetPlayer(creatorId);
            return creator == null ? "" : creator.GetPlayerName();
        }

        private static string Normalize(string value)
        {
            return (value ?? "").Trim().ToLowerInvariant();
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
