using System;
using System.IO;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace PraetorisClient.NetworkWardFeature
{
    internal sealed class NetworkWardAccess : MonoBehaviour
    {
        private const string RequestRpc = "PraetorisNetworkWardAccessRequest";
        private const string ResponseRpc = "PraetorisNetworkWardAccessResponse";
        private const string Denied = "Network Ward access denied. Ask an admin to whitelist your Steam ID.";
        private static NetworkWardAccess? _instance;
        private static DateTime _serverConfigWriteUtc;
        private static string _serverWhitelist = "";
        private ZNet? _session;
        private ZRpc? _server;
        private NetworkWard? _ward;
        private int _request;
        private bool _pending;
        private bool _approved;
        private bool _opening;
        private float _deadline;
        private float _expires;
        private float _refreshAt;

        internal static bool HasAccess => _instance != null && _instance._approved &&
            Time.unscaledTime < _instance._expires && _instance.CurrentConnection();

        internal static void Request(NetworkWard ward)
        {
            if (NetworkWardWindow.IsOpen) return;
            if (_instance == null) _instance = PraetorisClientPlugin.Instance!.gameObject.AddComponent<NetworkWardAccess>();
            if (_instance._pending) return;
            _instance._approved = false;
            _instance._session = ZNet.instance;
            _instance._server = ZNet.instance?.GetServerRPC();
            _instance._ward = ward;
            if (!_instance.CurrentConnection())
            {
                Notice("Network Ward access requires approval from a connected Steam server.");
                return;
            }
            Notice("Checking Network Ward access...");
            _instance.Send(true);
        }

        private bool CurrentConnection() => _session != null && _session == ZNet.instance &&
            !_session.IsServer() && _server != null && ReferenceEquals(_server, _session.GetServerRPC()) &&
            _server.IsConnected() && Player.m_localPlayer != null && !Player.m_localPlayer.IsDead();

        private void Send(bool opening)
        {
            _opening = opening;
            _request = _request == int.MaxValue ? 1 : _request + 1;
            _pending = true;
            _deadline = Time.unscaledTime + 5;
            _server!.Invoke(RequestRpc, _request);
        }

        private void Update()
        {
            if (!CurrentConnection() || _ward == null)
            {
                _approved = _pending = false;
                return;
            }
            if (_pending && Time.unscaledTime >= _deadline)
            {
                _pending = _approved = false;
                Notice("Unable to verify Network Ward access. Please try again.");
            }
            if (_approved && Time.unscaledTime >= _expires) _approved = false;
            if (_approved && NetworkWardWindow.IsOpen && !_pending && Time.unscaledTime >= _refreshAt) Send(false);
        }

        private static void OnRequest(ZRpc rpc, int request)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            ZNetPeer peer = ZNet.instance.GetPeer(rpc);
            // Direct connection RPCs cannot claim another routed sender ID. Only use
            // the authenticated Steam socket, never an ID supplied in a request payload.
            ZSteamSocket? steam = FindSteamSocket(rpc.GetSocket());
            bool ready = peer != null && peer.IsReady();
            bool configLoaded = ready && steam != null && RefreshServerConfig();
            bool allowed = configLoaded && NetworkWardAccessList.Contains(
                _serverWhitelist, steam!.GetHostName());
            if (!allowed)
            {
                string reason = !ready ? "connection not ready" : steam == null ? "unsupported transport " + peer!.m_socket.GetType().Name :
                    !configLoaded ? "server configuration unavailable" : "Steam ID is not whitelisted";
                PraetorisClientPlugin.Log.LogInfo("Network Ward authorization denied: " + reason + ".");
            }
            rpc.Invoke(ResponseRpc, request, allowed);
        }

        private static ZSteamSocket? FindSteamSocket(ISocket socket)
        {
            // ServerSync and ConditionalConfigSync temporarily nest BufferingSocket
            // wrappers during login. Their Original fields retain the real connection.
            for (int depth = 0; depth < 8; depth++)
            {
                if (socket is ZSteamSocket steam) return steam;
                if (socket.GetType().Name != "BufferingSocket" ||
                    !(AccessTools.Field(socket.GetType(), "Original")?.GetValue(socket) is ISocket original)) return null;
                socket = original;
            }
            return null;
        }

        private static bool RefreshServerConfig()
        {
            // Profile managers can symlink BepInEx/config. FileSystemWatcher does not
            // reliably report edits through these links, so verify changes at approval.
            try
            {
                string path = PraetorisClientPlugin.Instance!.Config.ConfigFilePath;
                if (!File.Exists(path)) return false;
                DateTime written = File.GetLastWriteTimeUtc(path);
                if (written != _serverConfigWriteUtc)
                {
                    // Read a fresh policy snapshot: Config.Reload() retains the previous
                    // value when a key is removed. A missing whitelist must deny access.
                    ConfigFile snapshot = new ConfigFile(path, false) { SaveOnConfigSet = false };
                    _serverWhitelist = snapshot.Bind("NetworkWard", "AllowedSteamIds", "").Value;
                    // Keep the plugin's config in agreement so its shutdown Save()
                    // cannot overwrite an administrator's edit with the old value.
                    PraetorisClientPlugin.Instance.Config.Reload();
                    PraetorisClientPlugin.NetworkWardAllowedSteamIds.Value = _serverWhitelist;
                    _serverConfigWriteUtc = File.GetLastWriteTimeUtc(path);
                }
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                PraetorisClientPlugin.Log.LogWarning("Network Ward approval denied: cannot read server configuration. " + exception.Message);
                return false;
            }
        }

        private static void OnResponse(ZRpc rpc, int request, bool allowed)
        {
            NetworkWardAccess? access = _instance;
            if (access == null || !access._pending || request != access._request ||
                !ReferenceEquals(rpc, access._server) || !access.CurrentConnection() ||
                Time.unscaledTime >= access._deadline) return;
            access._pending = false;
            access._approved = allowed;
            if (!allowed) { Notice(Denied); return; }
            access._expires = Time.unscaledTime + 10;
            access._refreshAt = Time.unscaledTime + 3;
            if (access._opening && !NetworkWardWindow.IsOpen && access._ward != null &&
                Vector3.Distance(Player.m_localPlayer.transform.position, access._ward.transform.position) <= 5)
                NetworkWardWindow.Open(access._ward);
        }

        private static void Notice(string message)
        {
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, message);
            PraetorisClientPlugin.Log.LogInfo(message);
        }

        private void OnDestroy()
        {
            _approved = _pending = false;
            if (_instance == this) _instance = null;
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
