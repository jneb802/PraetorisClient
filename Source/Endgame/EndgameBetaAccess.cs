using System;
using System.IO;
using BepInEx.Configuration;
using HarmonyLib;
using PraetorisClient.NetworkWardFeature;
using UnityEngine;

namespace PraetorisClient
{
    internal sealed class EndgameBetaAccess : MonoBehaviour
    {
        private const string RequestRpc = "PraetorisEndgameBetaRequest";
        private const string ResponseRpc = "PraetorisEndgameBetaResponse";
        private static EndgameBetaAccess? _instance;
        private ZNet? _session;
        private ZRpc? _server;
        private Teleport? _gateway;
        private Teleport? _approvedGateway;
        private int _request;
        private bool _pending;
        private float _deadline;
        private float _retryAt;

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
            _server.IsConnected() && Player.m_localPlayer != null && !Player.m_localPlayer.IsDead();

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
            _server!.Invoke(RequestRpc, _request);
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

        private static void OnRequest(ZRpc rpc, int request)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            ZNetPeer peer = ZNet.instance.GetPeer(rpc);
            ZSteamSocket? steam = NetworkWardAccess.FindSteamSocket(rpc.GetSocket());
            bool allowed = peer != null && peer.IsReady() && steam != null && IsAllowed(steam.GetHostName());
            PraetorisClientPlugin.Log.LogInfo("Endgame beta entry " + (allowed ? "approved." : "denied."));
            rpc.Invoke(ResponseRpc, request, allowed);
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

        private static void OnResponse(ZRpc rpc, int request, bool allowed)
        {
            EndgameBetaAccess? access = _instance;
            if (access == null || !access._pending || request != access._request ||
                !ReferenceEquals(rpc, access._server) || !access.CurrentConnection() ||
                Time.unscaledTime >= access._deadline) return;
            access._pending = false;
            access._retryAt = Time.unscaledTime + 1;
            Teleport? gateway = access._gateway;
            access._gateway = null;
            if (!allowed)
            {
                Notice("Valheim Endgame is currently in closed beta. You do not have access.");
                return;
            }
            Player player = Player.m_localPlayer;
            if (gateway == null || !gateway.isActiveAndEnabled || !IsEntrance(gateway) || player.IsTeleporting() ||
                Vector3.Distance(player.transform.position, gateway.transform.position) > 5) return;
            access._approvedGateway = gateway;
            try { gateway.Interact(player, false, false); }
            finally { access._approvedGateway = null; }
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

        [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
        private static class RegisterAccessRpc
        {
            private static void Postfix(ZNetPeer peer)
            {
                peer.m_rpc.Register<int>(RequestRpc, OnRequest);
                peer.m_rpc.Register<int, bool>(ResponseRpc, OnResponse);
            }
        }
    }
}
