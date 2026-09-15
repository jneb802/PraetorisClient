using System;
using System.Collections.Generic;
using UnityEngine;

namespace PraetorisClient.ShipPasswordFeature
{
    internal static class ShipPasswordRpc
    {
        private const float MaximumRequestDistance = 30f;
        private const float MinimumAttemptIntervalSeconds = 1f;
        private static readonly Dictionary<string, float> LastAttempts = new Dictionary<string, float>();

        internal static void Register(ZRoutedRpc rpc)
        {
            rpc.Register<ZPackage>(RpcNames.ShipPasswordSetRequest, OnSetRequest);
            rpc.Register<ZPackage>(RpcNames.ShipPasswordSetGrant, OnSetGrant);
            rpc.Register<ZPackage>(RpcNames.ShipPasswordControlRequest, OnControlRequest);
            rpc.Register<ZPackage>(RpcNames.ShipPasswordControlGrant, OnControlGrant);
            rpc.Register<ZPackage>(RpcNames.ShipPasswordResponse, OnResponse);
        }

        internal static void RequestSetPassword(ZDOID shipId, string password)
        {
            SendRequest(RpcNames.ShipPasswordSetRequest, shipId, password);
        }

        internal static void RequestControl(ZDOID shipId, string password)
        {
            SendRequest(RpcNames.ShipPasswordControlRequest, shipId, password);
        }

        private static void SendRequest(string rpcName, ZDOID shipId, string password)
        {
            if (ZRoutedRpc.instance == null)
            {
                ShowMessage("Ship password service is unavailable.");
                return;
            }

            ZPackage package = new ZPackage();
            package.Write(shipId);
            package.Write(password ?? "");
            ZRoutedRpc.instance.InvokeRoutedRPC(
                ZRoutedRpc.instance.GetServerPeerID(),
                rpcName,
                package);
        }

        private static void OnSetRequest(long sender, ZPackage package)
        {
            if (!IsServer())
            {
                return;
            }

            ZDOID shipId = package.ReadZDOID();
            string password = package.ReadString();
            if (password.Length > ShipPasswordData.MaximumPasswordLength)
            {
                SendResponse(sender, false, "Ship passwords can contain at most 32 characters.");
                return;
            }

            if (!TryGetAuthorizedShip(sender, shipId, creatorRequired: true, out ZDO? shipZdo, out _))
            {
                SendResponse(sender, false, "Only the nearby ship creator can change its password.");
                return;
            }

            string saltValue = "";
            string verifierValue = "";
            if (password.Length > 0)
            {
                ShipPasswordData.CreateVerifier(password, out saltValue, out verifierValue);
            }

            long ownerPeerId = GetOwnerPeerId(shipZdo!);
            ZPackage grant = new ZPackage();
            grant.Write(shipId);
            grant.Write(sender);
            grant.Write(saltValue);
            grant.Write(verifierValue);
            ZRoutedRpc.instance.InvokeRoutedRPC(ownerPeerId, RpcNames.ShipPasswordSetGrant, grant);
        }

        private static void OnControlRequest(long sender, ZPackage package)
        {
            if (!IsServer())
            {
                return;
            }

            ZDOID shipId = package.ReadZDOID();
            string password = package.ReadString();
            if (!TryGetAuthorizedShip(sender, shipId, creatorRequired: false, out ZDO? shipZdo, out long playerId))
            {
                SendResponse(sender, false, "You must be aboard the ship to use its helm.");
                return;
            }

            if (!AllowAttempt(sender, shipId))
            {
                SendResponse(sender, false, "Wait before trying the ship password again.");
                return;
            }

            if (!ShipPasswordData.IsProtected(shipZdo) || !ShipPasswordData.Verify(shipZdo!, password))
            {
                PraetorisClientPlugin.Log.LogInfo("Rejected ship password for " + shipId + ".");
                SendResponse(sender, false, "Incorrect ship password.");
                return;
            }

            long ownerPeerId = GetOwnerPeerId(shipZdo!);

            ZPackage grant = new ZPackage();
            grant.Write(shipId);
            grant.Write(sender);
            grant.Write(playerId);
            ZRoutedRpc.instance.InvokeRoutedRPC(ownerPeerId, RpcNames.ShipPasswordControlGrant, grant);
            PraetorisClientPlugin.Log.LogInfo("Accepted ship password for " + shipId + ".");
        }

        private static void OnSetGrant(long sender, ZPackage package)
        {
            if (ZRoutedRpc.instance == null || sender != ZRoutedRpc.instance.GetServerPeerID())
            {
                return;
            }

            ZDOID shipId = package.ReadZDOID();
            long requestingPeerId = package.ReadLong();
            string saltValue = package.ReadString();
            string verifierValue = package.ReadString();
            GameObject? shipObject = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(shipId) : null;
            ZNetView? nview = shipObject != null ? shipObject.GetComponent<ZNetView>() : null;
            if (nview == null || !nview.IsValid() || !nview.IsOwner())
            {
                SendResponse(requestingPeerId, false, "Ship password update expired. Try again.");
                return;
            }

            ShipPasswordData.ApplyVerifier(nview.GetZDO(), saltValue, verifierValue);
            bool passwordSet = verifierValue.Length > 0;
            PraetorisClientPlugin.Log.LogInfo(
                (passwordSet ? "Set" : "Cleared") + " ship password for " + shipId + ".");
            SendResponse(
                requestingPeerId,
                true,
                passwordSet ? "Ship password set." : "Ship password cleared.");
        }

        private static void OnControlGrant(long sender, ZPackage package)
        {
            if (ZRoutedRpc.instance == null || sender != ZRoutedRpc.instance.GetServerPeerID())
            {
                return;
            }

            ZDOID shipId = package.ReadZDOID();
            long requestingPeerId = package.ReadLong();
            long playerId = package.ReadLong();
            GameObject? shipObject = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(shipId) : null;
            Ship? ship = shipObject != null ? shipObject.GetComponent<Ship>() : null;
            ZNetView? nview = shipObject != null ? shipObject.GetComponent<ZNetView>() : null;
            ShipControlls? controls = ship != null ? ship.m_shipControlls : null;

            if (ship == null || nview == null || controls == null || !nview.IsOwner() ||
                !ShipPasswordData.IsProtected(nview.GetZDO()) || !ship.IsPlayerInBoat(playerId))
            {
                SendResponse(requestingPeerId, false, "Ship control request expired. Try again.");
                return;
            }

            if (controls.GetUser() != playerId && controls.HaveValidUser())
            {
                SendResponse(requestingPeerId, false, "$msg_inuse");
                return;
            }

            nview.GetZDO().Set(ZDOVars.s_user, playerId);
            nview.InvokeRPC(requestingPeerId, "RequestRespons", true);
            SendResponse(requestingPeerId, true, "Ship password accepted.");
        }

        private static void OnResponse(long sender, ZPackage package)
        {
            bool success = package.ReadBool();
            string message = package.ReadString();
            ShowMessage(message);
            if (!success)
            {
                PraetorisClientPlugin.Log.LogInfo("Ship password request failed: " + message);
            }
        }

        private static bool TryGetAuthorizedShip(
            long sender,
            ZDOID shipId,
            bool creatorRequired,
            out ZDO? shipZdo,
            out long playerId)
        {
            shipZdo = null;
            playerId = 0L;
            if (ZDOMan.instance == null || ZNetScene.instance == null || shipId.IsNone())
            {
                return false;
            }

            ZDO? candidate = ZDOMan.instance.GetZDO(shipId);
            GameObject? prefab = candidate != null ? ZNetScene.instance.GetPrefab(candidate.GetPrefab()) : null;
            if (candidate == null || prefab == null || prefab.GetComponent<Ship>() == null)
            {
                return false;
            }

            if (!PlayerResolver.TryGetSenderPlayerId(sender, out playerId, out ZNetPeer? peer))
            {
                return false;
            }

            Vector3 playerPosition;
            if (peer != null && !peer.m_characterID.IsNone())
            {
                ZDO? characterZdo = ZDOMan.instance.GetZDO(peer.m_characterID);
                if (characterZdo == null)
                {
                    return false;
                }

                playerPosition = characterZdo.GetPosition();
            }
            else if (Player.m_localPlayer != null)
            {
                playerPosition = Player.m_localPlayer.transform.position;
            }
            else
            {
                return false;
            }

            if (Vector3.Distance(playerPosition, candidate.GetPosition()) > MaximumRequestDistance)
            {
                return false;
            }

            if (creatorRequired && candidate.GetLong(ZDOVars.s_creator) != playerId)
            {
                return false;
            }

            shipZdo = candidate;
            return true;
        }

        private static bool AllowAttempt(long sender, ZDOID shipId)
        {
            string key = sender + ":" + shipId;
            float now = Time.realtimeSinceStartup;
            if (LastAttempts.TryGetValue(key, out float lastAttempt) &&
                now - lastAttempt < MinimumAttemptIntervalSeconds)
            {
                return false;
            }

            LastAttempts[key] = now;
            return true;
        }

        private static long GetOwnerPeerId(ZDO shipZdo)
        {
            long ownerPeerId = shipZdo.GetOwner();
            return ownerPeerId != 0L ? ownerPeerId : ZNet.GetUID();
        }

        private static bool IsServer()
        {
            return ZNet.instance != null && ZNet.instance.IsServer();
        }

        private static void SendResponse(long target, bool success, string message)
        {
            if (ZRoutedRpc.instance == null)
            {
                return;
            }

            ZPackage response = new ZPackage();
            response.Write(success);
            response.Write(message);
            ZRoutedRpc.instance.InvokeRoutedRPC(target, RpcNames.ShipPasswordResponse, response);
        }

        private static void ShowMessage(string message)
        {
            if (Player.m_localPlayer != null)
            {
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, message);
            }
        }
    }
}
