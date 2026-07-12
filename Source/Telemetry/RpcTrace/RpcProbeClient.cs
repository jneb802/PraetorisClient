using System;
using System.Collections.Generic;
using UnityEngine;

namespace PraetorisClient
{
    internal static class RpcProbeClient
    {
        private static readonly Dictionary<int, PendingClientProbe> Pending = new();
        private static ZRoutedRpc? _registeredRpc;
        private static float _sendTimer;
        private static int _sequence;
        private static byte[] _payload = Array.Empty<byte>();

        internal static void Update()
        {
            if (!CanProbe())
                return;

            TryRegisterRpcs();
            WriteTimedOutClientProbes();

            _sendTimer += Time.unscaledDeltaTime;
            float interval = Math.Max(0.25f, PraetorisClientPlugin.RpcProbeIntervalSeconds.Value);
            if (_sendTimer < interval)
                return;

            _sendTimer = 0f;
            SendClientProbe();
        }

        private static void TryRegisterRpcs()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || ReferenceEquals(_registeredRpc, rpc))
                return;

            try
            {
                rpc.Register<ZPackage>(RpcNames.RpcProbeForward, OnProbeForward);
                rpc.Register<ZPackage>(RpcNames.RpcProbeAck, OnProbeAck);
                RpcTraceTelemetry.RegisterRpcName(RpcNames.RpcProbeRequest);
                RpcTraceTelemetry.RegisterRpcName(RpcNames.RpcProbeForward);
                RpcTraceTelemetry.RegisterRpcName(RpcNames.RpcProbeReply);
                RpcTraceTelemetry.RegisterRpcName(RpcNames.RpcProbeAck);
                _registeredRpc = rpc;
                PraetorisClientPlugin.Log.LogInfo("Registered RPC probe client handlers.");
            }
            catch (Exception exception)
            {
                _registeredRpc = rpc;
                PraetorisClientPlugin.Log.LogWarning($"Failed to register RPC probe client handlers: {exception.GetType().Name}: {exception.Message}");
            }
        }

        private static void SendClientProbe()
        {
            if (ZNet.instance == null || ZRoutedRpc.instance == null)
                return;

            ZNetPeer serverPeer = ZNet.instance.GetServerPeer();
            if (serverPeer == null)
                return;

            BuildPayload();
            int sequence = ++_sequence;
            int sendQueueBytes = serverPeer.m_socket.GetSendQueueSize();
            int headroomBytes = ClientSocketMetrics.SendQueueBudgetBytes - sendQueueBytes;
            Pending[sequence] = new PendingClientProbe(Time.realtimeSinceStartup, _payload.Length, sendQueueBytes, headroomBytes);

            ZPackage package = new();
            package.Write(RpcTraceTelemetry.ProtocolVersion);
            package.Write(sequence);
            package.Write(GetLocalPlayerName());
            package.Write(Time.realtimeSinceStartupAsDouble);
            package.Write(_payload);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcNames.RpcProbeRequest, package);
        }

        private static void OnProbeForward(long senderPeerUid, ZPackage package)
        {
            if (!CanProbe())
                return;

            try
            {
                int protocolVersion = package.ReadInt();
                long originPeerUid = package.ReadLong();
                package.ReadString();
                int sequence = package.ReadInt();
                byte[] payload = package.ReadByteArray();
                if (protocolVersion != RpcTraceTelemetry.ProtocolVersion)
                    return;

                ZPackage reply = new();
                reply.Write(RpcTraceTelemetry.ProtocolVersion);
                reply.Write(originPeerUid);
                reply.Write(sequence);
                reply.Write(payload);
                ZRoutedRpc.instance.InvokeRoutedRPC(senderPeerUid, RpcNames.RpcProbeReply, reply);
            }
            catch (Exception exception)
            {
                PraetorisClientPlugin.Log.LogWarning($"RPC probe forward failed from {senderPeerUid}: {exception.GetType().Name}: {exception.Message}");
            }
        }

        private static void OnProbeAck(long senderPeerUid, ZPackage package)
        {
            if (!CanProbe())
                return;

            try
            {
                int protocolVersion = package.ReadInt();
                int sequence = package.ReadInt();
                long targetPeerUid = package.ReadLong();
                string targetPlayerName = package.ReadString();
                int payloadBytes = package.ReadInt();
                float serverRelayMs = package.ReadSingle();
                bool success = package.ReadBool();
                string result = package.ReadString();
                if (protocolVersion != RpcTraceTelemetry.ProtocolVersion)
                    return;

                HandleProbeAck(sequence, targetPeerUid, targetPlayerName, payloadBytes, serverRelayMs, success, result);
            }
            catch (Exception exception)
            {
                PraetorisClientPlugin.Log.LogWarning($"RPC probe ack failed from {senderPeerUid}: {exception.GetType().Name}: {exception.Message}");
            }
        }

        private static void HandleProbeAck(int sequence, long targetPeerUid, string targetPlayerName, int payloadBytes, float serverRelayMs, bool success, string result)
        {
            if (!Pending.TryGetValue(sequence, out PendingClientProbe pending))
                return;

            Pending.Remove(sequence);
            float fullRoundtripMs = (Time.realtimeSinceStartup - pending.ClientSentAt) * 1000f;
            WriteClientSample(sequence, targetPeerUid, targetPlayerName, payloadBytes, fullRoundtripMs, serverRelayMs, pending.ServerSendQueueBytes, pending.ServerHeadroomBytes, success ? result : $"failed:{result}");
        }

        private static void WriteTimedOutClientProbes()
        {
            if (Pending.Count == 0)
                return;

            float timeoutSeconds = Math.Max(1f, PraetorisClientPlugin.RpcProbeTimeoutSeconds.Value);
            float now = Time.realtimeSinceStartup;
            List<int> timedOutSequences = new();
            foreach (KeyValuePair<int, PendingClientProbe> entry in Pending)
            {
                if (now - entry.Value.ClientSentAt >= timeoutSeconds)
                    timedOutSequences.Add(entry.Key);
            }

            foreach (int sequence in timedOutSequences)
            {
                PendingClientProbe pending = Pending[sequence];
                Pending.Remove(sequence);
                WriteClientSample(sequence, 0L, "", pending.PayloadBytes, timeoutSeconds * 1000f, 0f, pending.ServerSendQueueBytes, pending.ServerHeadroomBytes, "timeout");
            }
        }

        private static void WriteClientSample(int sequence, long targetPeerUid, string targetPlayerName, int payloadBytes, float fullRoundtripMs, float serverRelayMs, int serverSendQueueBytes, int serverHeadroomBytes, string result)
        {
            long localPeerId = RpcTraceTelemetry.GetLocalPeerId();
            RpcTraceTelemetry.TraceEnvelopeContext context = RpcTraceTelemetry.CaptureEnvelopeContext(localPeerId);
            TelemetryJson json = RpcTraceTelemetry.ObjectWithEnvelope("rpc_probe_client_sample", context);
            json.Prop("role", "client");
            json.Prop("probeSequence", sequence);
            json.Prop("originPeerId", localPeerId);
            json.Prop("originPlayerName", context.PlayerName);
            json.Prop("targetPeerId", targetPeerUid);
            json.Prop("targetPlayerName", targetPlayerName ?? "");
            json.Prop("payloadBytes", payloadBytes);
            json.Prop("fullRoundtripMs", fullRoundtripMs);
            json.Prop("serverRelayMs", serverRelayMs);
            json.Prop("serverSendQueueBeforeBytes", serverSendQueueBytes);
            json.Prop("serverSendHeadroomBeforeBytes", serverHeadroomBytes);
            json.Prop("probeResult", result ?? "");
            RpcTraceTelemetry.AddClockFields(json, context);
            json.End();
            RpcTraceLocalStore.Append(json.ToString(), localPeerId, context.WorldUid);
        }

        private static void BuildPayload()
        {
            int payloadBytes = Math.Max(0, Math.Min(8192, PraetorisClientPlugin.RpcProbePayloadBytes.Value));
            if (_payload.Length == payloadBytes)
                return;

            _payload = new byte[payloadBytes];
            for (int index = 0; index < _payload.Length; index++)
                _payload[index] = (byte)(index % 251);
        }

        private static bool CanProbe()
        {
            return RpcTraceTelemetry.IsTracingEnabled()
                && PraetorisClientPlugin.RpcProbeEnabled.Value
                && ZNet.instance != null
                && ZRoutedRpc.instance != null
                && !ZNet.instance.IsServer()
                && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected;
        }

        private static string GetLocalPlayerName()
        {
            if (Player.m_localPlayer == null)
                return "unknown";

            string playerName = Player.m_localPlayer.GetPlayerName();
            return string.IsNullOrEmpty(playerName) ? "unknown" : playerName;
        }

        private readonly struct PendingClientProbe
        {
            internal PendingClientProbe(float clientSentAt, int payloadBytes, int serverSendQueueBytes, int serverHeadroomBytes)
            {
                ClientSentAt = clientSentAt;
                PayloadBytes = payloadBytes;
                ServerSendQueueBytes = serverSendQueueBytes;
                ServerHeadroomBytes = serverHeadroomBytes;
            }

            internal float ClientSentAt { get; }
            internal int PayloadBytes { get; }
            internal int ServerSendQueueBytes { get; }
            internal int ServerHeadroomBytes { get; }
        }
    }
}
