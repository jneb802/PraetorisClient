using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;

namespace PraetorisClient.ServerGuideFeature
{
    internal static class ServerGuide
    {
        private const string RequestRpc = "PraetorisClient.GuideRequestV2";
        private const string ResponseRpc = "PraetorisClient.GuideResponseV2";
        private static readonly Dictionary<long, float> LastRequests = new Dictionary<long, float>();
        internal static List<GuidePage> Pages { get; private set; } = new List<GuidePage>();
        private static ZNet? _session;
        private static string _text = "";
        private static string _revision = "";
        internal static string Revision => _revision;
        private static string _sourceStamp = "";
        private static string _lastError = "";
        private static float _nextCheck;
        private static float _nextRequest;
        private static bool _received;
        internal static string FilePath => Path.Combine(Paths.ConfigPath, "PraetorisClient.ServerGuide.txt");

        internal static void Register(ZRoutedRpc rpc)
        {
            rpc.Register<string>(RequestRpc, OnRequest);
            rpc.Register<ZPackage>(ResponseRpc, OnResponse);
            GuideImages.Register(rpc);
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
            _ = new Terminal.ConsoleCommand("praetoris_guide_reload", "Reload the server guide text file.", args =>
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer())
                {
                    args.Context.AddString("Run this command on the server.");
                    return;
                }
                args.Context.AddString(Reload(force: true));
            }, onlyServer: true);
        }

        private static string Status() => $"Server guide: pages={Pages.Count}, images={GuideImages.Status}, received={_received}, revision={_revision}" + GuideReader.Status();

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
                _text = _revision = "";
                _sourceStamp = _lastError = "";
                GuideImages.Clear();
                _nextCheck = _nextRequest = 0;
                _received = false;
                LastRequests.Clear();
            }
            if (_session == null || ZRoutedRpc.instance == null)
                return;
            GuideImages.Update();
            if (_session.IsServer())
            {
                if (Time.realtimeSinceStartup < _nextCheck)
                    return;
                _nextCheck = Time.realtimeSinceStartup + 5f;
                Reload();
                foreach (long peer in new List<long>(LastRequests.Keys))
                    if (_session.GetPeer(peer) == null)
                        LastRequests.Remove(peer);
            }
            else if (Player.m_localPlayer != null && Time.realtimeSinceStartup >= _nextRequest)
            {
                _nextRequest = Time.realtimeSinceStartup + 10f;
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RequestRpc, _revision);
            }
        }

        private static string Reload(bool force = false)
        {
            try
            {
                if (!File.Exists(FilePath))
                    File.WriteAllText(FilePath, "# Welcome\nWelcome to the server guide.\n", new UTF8Encoding(false));
                FileInfo file = new FileInfo(FilePath);
                if (file.Length > GuideDocument.MaximumBytes)
                    throw new FormatException("The guide must be at most 128 KiB.");
                string text = File.ReadAllText(FilePath, Encoding.UTF8);
                GuideDocument.Parse(text);
                text = GuideConfigText.Expand(text, GuideConfigValues.Resolve);
                List<GuidePage> pages = GuideDocument.Parse(text);
                string stamp = GuideImageData.Digest(Encoding.UTF8.GetBytes(text)) + ":" + GuideImages.SourceStamp(pages);
                if (!force && stamp == _sourceStamp) return Status();
                Dictionary<string, GuideImageData> images = GuideImages.Load(pages);
                string revision = GuideImages.GetRevision(text, images);
                _sourceStamp = stamp;
                _lastError = "";
                if (revision == _revision) return Status();
                GuideImages.Set(images);
                Pages = pages;
                _text = text;
                _revision = revision;
                _received = true;
                PraetorisClientPlugin.Log.LogInfo($"Loaded server guide: {pages.Count} pages, revision={revision}");
                return Status();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is FormatException)
            {
                string message = "Server guide reload failed; keeping the previous pages and images. " + ex.Message;
                if (_lastError != message) PraetorisClientPlugin.Log.LogWarning(message);
                _lastError = message;
                return message;
            }
        }

        private static void OnRequest(long sender, string revision)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZNet.instance.GetPeer(sender) == null)
                return;
            if (revision == null || revision.Length > 64)
                return;
            float now = Time.realtimeSinceStartup;
            if (LastRequests.TryGetValue(sender, out float last) && now - last < 5f)
                return;
            LastRequests[sender] = now;
            if (!_received || revision == _revision)
                return;
            ZPackage package = new ZPackage();
            package.Write(_text);
            GuideImages.WriteManifest(package);
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, ResponseRpc, package);
        }

        private static void OnResponse(long sender, ZPackage package)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer() || sender != ZRoutedRpc.instance.GetServerPeerID())
                return;
            try
            {
                if (package.Size() > GuideDocument.MaximumBytes + 2048)
                    throw new FormatException("Guide response exceeds the size limit.");
                string text = package.ReadString();
                List<GuidePage> pages = GuideDocument.Parse(text);
                Dictionary<string, GuideImageData> images = GuideImages.ReadManifest(package, pages);
                string revision = GuideImages.GetRevision(text, images);
                if (revision == _revision) return;
                GuideImages.Set(images);
                _revision = revision;
                Pages = pages;
                _received = true;
                PraetorisClientPlugin.Log.LogInfo($"Received server guide: {pages.Count} pages, revision={_revision}");
            }
            catch (Exception ex) when (ex is IOException || ex is FormatException || ex is ArgumentException)
            {
                PraetorisClientPlugin.Log.LogWarning("Invalid server guide response: " + ex.Message);
            }
        }
    }
}
