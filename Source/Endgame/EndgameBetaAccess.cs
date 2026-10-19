using System;
using System.Collections;
using System.IO;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using PraetorisClient.NetworkWardFeature;
using UnityEngine;

namespace PraetorisClient
{
    internal sealed class EndgameBetaAccess : MonoBehaviour
    {
        private static CustomRPC _rpc = null!;
        private static EndgameBetaAccess? _instance;
        private ZNet? _session;
        private ZRpc? _server;
        private Teleport? _gateway;
        private Teleport? _approvedGateway;
        private int _request;
        private bool _pending;
        private float _deadline;
        private float _retryAt;

        internal static void Initialize()
        {
            _rpc = NetworkManager.Instance.AddRPC("EndgameBetaAccess", OnRequest, OnResponse);
        }

        private static bool IsEntrance(Teleport gateway)
        {
            Transform? exterior = gateway.transform.parent;
            while (exterior != null && exterior.name != "Exterior") exterior = exterior.parent;
            if (exterior == null || exterior.parent == null) return false;
            Transform root = exterior.parent;
            if (root.name != "MWL_MapRoom1" && root.name != "MWL_MapRoom1(Clone)") return false;
            return root.Find("Exterior/Blueprint/Gateway") == gateway.transform &&
                root.Find("Interior/ExteriorGateway") == gateway.m_targetPoint?.transform;
        }

        private bool CurrentConnection() => _session != null && _session == ZNet.instance &&
            !_session.IsServer() && _server != null && ReferenceEquals(_server, _session.GetServerRPC()) &&
            _server.IsConnected() && ZRoutedRpc.instance != null &&
            Player.m_localPlayer != null && !Player.m_localPlayer.IsDead();

        private void Request(Teleport gateway)
        {
            if (_pending || Time.unscaledTime < _retryAt) return;
            _session = ZNet.instance;
            _server = _session?.GetServerRPC();
            if (!CurrentConnection())
            {
                Notice("Endgame beta access requires approval from a connected Steam server.");
                _retryAt = Time.unscaledTime + 1;
                return;
            }
            _gateway = gateway;
            _request = _request == int.MaxValue ? 1 : _request + 1;
            _pending = true;
            _deadline = Time.unscaledTime + 5;
            ZPackage package = new ZPackage();
            package.Write(_request);
            _rpc.SendPackage(ZRoutedRpc.instance.GetServerPeerID(), package);
        }

        private void Update()
        {
            if (!_pending) return;
            if (!CurrentConnection()) { _pending = false; _gateway = null; return; }
            if (Time.unscaledTime < _deadline) return;
            _pending = false;
            _gateway = null;
            _retryAt = Time.unscaledTime + 1;
            Notice("Unable to verify Endgame beta access. Please try again.");
        }

        private static IEnumerator OnRequest(long sender, ZPackage package)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || package.Size() != sizeof(int)) yield break;
            ZNetPeer peer = ZNet.instance.GetPeer(sender);
            if (peer == null || !peer.IsReady()) yield break;
            int request = package.ReadInt();
            ZSteamSocket? steam = NetworkWardAccess.FindSteamSocket(peer.m_rpc.GetSocket());
            bool allowed = steam != null && IsAllowed(steam.GetHostName());
            PraetorisClientPlugin.Log.LogInfo("Endgame beta entry " + (allowed ? "approved." : "denied."));
            ZPackage response = new ZPackage();
            response.Write(request);
            response.Write(allowed);
            _rpc.SendPackage(sender, response);
            yield break;
        }

        private static bool IsAllowed(string steamId)
        {
            try
            {
                ConfigFile config = PraetorisClientPlugin.Instance!.Config;
                if (!File.Exists(config.ConfigFilePath)) return false;
                ConfigFile snapshot = new ConfigFile(config.ConfigFilePath, false) { SaveOnConfigSet = false };
                string allowed = snapshot.Bind("EndgameBeta", "AllowedSteamIds", "").Value;
                config.Reload();
                PraetorisClientPlugin.EndgameBetaAllowedSteamIds.Value = allowed;
                return NetworkWardAccessList.Contains(allowed, steamId);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                PraetorisClientPlugin.Log.LogWarning("Endgame beta access denied: cannot read server configuration. " + exception.Message);
                return false;
            }
        }

        private static IEnumerator OnResponse(long sender, ZPackage package)
        {
            EndgameBetaAccess? access = _instance;
            if (access == null || !access._pending || !access.CurrentConnection() ||
                sender != ZRoutedRpc.instance.GetServerPeerID() ||
                package.Size() != sizeof(int) + sizeof(bool) || Time.unscaledTime >= access._deadline) yield break;
            int request = package.ReadInt();
            bool allowed = package.ReadBool();
            if (request != access._request) yield break;
            access._pending = false;
            access._retryAt = Time.unscaledTime + 1;
            Teleport? gateway = access._gateway;
            access._gateway = null;
            if (!allowed)
            {
                Notice("Valheim Endgame is currently in closed beta. You do not have access.");
                yield break;
            }
            Player player = Player.m_localPlayer;
            if (gateway == null || !gateway.isActiveAndEnabled || !IsEntrance(gateway) || player.IsTeleporting() ||
                Vector3.Distance(player.transform.position, gateway.transform.position) > 5) yield break;
            access._approvedGateway = gateway;
            try { gateway.Interact(player, false, false); }
            finally { access._approvedGateway = null; }
            yield break;
        }

        private static void Notice(string message)
        {
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, message);
            PraetorisClientPlugin.Log.LogInfo(message);
        }

        private void OnDestroy()
        {
            _pending = false;
            _approvedGateway = _gateway = null;
            if (_instance == this) _instance = null;
        }

        [HarmonyPatch(typeof(Teleport), nameof(Teleport.Interact))]
        private static class GateEntry
        {
            private static bool Prefix(Teleport __instance, Humanoid character, bool hold, ref bool __result)
            {
                if (!IsEntrance(__instance)) return true;
                if (_instance != null && _instance._approvedGateway == __instance) return true;
                __result = false;
                if (hold || character != Player.m_localPlayer || character is not Player player ||
                    player.IsDead() || player.IsTeleporting() ||
                    Vector3.Distance(player.transform.position, __instance.transform.position) > 5) return false;
                if (_instance == null) _instance = PraetorisClientPlugin.Instance!.gameObject.AddComponent<EndgameBetaAccess>();
                _instance.Request(__instance);
                return false;
            }
        }
    }
}
