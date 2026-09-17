using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using UnityEngine;

namespace PraetorisClient.ServerGuideFeature
{
    internal static class ServerGuide
    {
        private const string RequestRpc = "PraetorisClient.GuideRequestV3";
        private const string ResponseRpc = "PraetorisClient.GuideResponseV3";
        private sealed class Snapshot
        {
            internal ZNetPeer Peer = null!;
            internal string Text = "";
            internal float LastSent = float.NegativeInfinity;
        }
        private static readonly Dictionary<long, Snapshot> Snapshots = new Dictionary<long, Snapshot>();
        internal static List<GuidePage> Pages { get; private set; } = new List<GuidePage>();
        private static ZNet? _session;
        private static string _lastValidText = "";
        private static string _revision = "";
        internal static string Revision => _revision;
        private static string _lastError = "";
        private static float _nextCleanup;
        private static float _nextRequest;
        private static bool _received;
        internal static string FilePath => Path.Combine(Paths.ConfigPath, "PraetorisClient.ServerGuide.txt");

        internal static void Register(ZRoutedRpc rpc)
        {
            rpc.Register(RequestRpc, OnRequest);
            rpc.Register<ZPackage>(ResponseRpc, OnResponse);
        }

        internal static void Initialize()
        {
            _ = new Terminal.ConsoleCommand("praetoris_guide", "Open the server guide.", args =>
            {
                if (Player.m_localPlayer == null || InventoryGui.instance == null)
                {
                    args.Context.AddString("Join a world before opening the guide.");
                    return;
                }
                PraetorisClientPlugin.Instance?.StartCoroutine(OpenGuide());
                args.Context.AddString(Status());
            });
            _ = new Terminal.ConsoleCommand("praetoris_guide_status", "Show server guide synchronization status.",
                args => args.Context.AddString(Status()));
        }

        private static string Status() => $"Server guide: pages={Pages.Count}, embeddedImages={GuideImages.Count}, received={_received}, revision={_revision}" + GuideReader.Status();

        private static IEnumerator OpenGuide()
        {
            InventoryGui.instance.Show(null);
            yield return null;
            if (InventoryGui.instance != null && Player.m_localPlayer != null) GuideWindow.Open(InventoryGui.instance);
        }

        internal static void Update()
        {
            if (!ReferenceEquals(_session, ZNet.instance))
            {
                _session = ZNet.instance;
                Pages = new List<GuidePage>();
                _lastValidText = _revision = _lastError = "";
                GuideImages.Clear();
                _nextCleanup = _nextRequest = 0;
                _received = false;
                Snapshots.Clear();
            }
            if (_session == null || ZRoutedRpc.instance == null) return;
            if (_session.IsServer())
            {
                // A local host also keeps the guide it loaded when entering this world.
                if (!_received) Accept(ReadText());
                if (Time.realtimeSinceStartup < _nextCleanup) return;
                _nextCleanup = Time.realtimeSinceStartup + 10f;
                foreach (long peer in new List<long>(Snapshots.Keys))
                    if (!ReferenceEquals(_session.GetPeer(peer), Snapshots[peer].Peer)) Snapshots.Remove(peer);
            }
            else if (!_received && Player.m_localPlayer != null && Time.realtimeSinceStartup >= _nextRequest)
            {
                // Retry only until the initial snapshot arrives. Never poll for edits.
                _nextRequest = Time.realtimeSinceStartup + 10f;
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RequestRpc);
            }
        }

        private static string ReadText()
        {
            try
            {
                if (!File.Exists(FilePath))
                    File.WriteAllText(FilePath, "# Welcome\nWelcome to the server guide.\n", new UTF8Encoding(false));
                if (new FileInfo(FilePath).Length > GuideDocument.MaximumBytes)
                    throw new FormatException("The guide must be at most 128 KiB.");
                string text = File.ReadAllText(FilePath, Encoding.UTF8);
                GuideDocument.Parse(text);
                text = GuideConfigText.Expand(text, GuideConfigValues.Resolve);
                GuideImages.Validate(GuideDocument.Parse(text));
                _lastValidText = text;
                _lastError = "";
                return text;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is FormatException)
            {
                string message = "Server guide load failed; using the previous valid text for this connection. " + ex.Message;
                if (_lastError != message) PraetorisClientPlugin.Log.LogWarning(message);
                _lastError = message;
                return _lastValidText;
            }
        }

        private static void OnRequest(long sender)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            ZNetPeer peer = ZNet.instance.GetPeer(sender);
            if (peer == null) return;
            if (!Snapshots.TryGetValue(sender, out Snapshot snapshot) || !ReferenceEquals(snapshot.Peer, peer))
            {
                snapshot = new Snapshot { Peer = peer, Text = ReadText() };
                Snapshots[sender] = snapshot;
            }
            float now = Time.realtimeSinceStartup;
            if (now - snapshot.LastSent < 5f) return;
            snapshot.LastSent = now;
            ZPackage package = new ZPackage();
            package.Write(snapshot.Text);
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, ResponseRpc, package);
            PraetorisClientPlugin.Log.LogInfo($"Sent server guide connection snapshot: textBytes={Encoding.UTF8.GetByteCount(snapshot.Text)}");
        }

        private static void OnResponse(long sender, ZPackage package)
        {
            if (_received || ZNet.instance == null || ZNet.instance.IsServer() || sender != ZRoutedRpc.instance.GetServerPeerID()) return;
            try
            {
                if (package.Size() > GuideDocument.MaximumBytes + 4)
                    throw new FormatException("Guide response exceeds the size limit.");
                Accept(package.ReadString());
                PraetorisClientPlugin.Log.LogInfo($"Received server guide connection snapshot: {Pages.Count} pages, revision={_revision}");
            }
            catch (Exception ex) when (ex is IOException || ex is FormatException || ex is ArgumentException)
            {
                PraetorisClientPlugin.Log.LogWarning("Invalid server guide response: " + ex.Message);
            }
        }

        private static void Accept(string text)
        {
            List<GuidePage> pages = GuideDocument.Parse(text);
            using SHA256 hash = SHA256.Create();
            _revision = Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(text)));
            Pages = pages;
            _received = true;
        }
    }
}
