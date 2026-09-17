using System;
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
        private const string RequestRpc = "PraetorisClient.GuideRequest";
        private const string ResponseRpc = "PraetorisClient.GuideResponse";
        private static readonly Dictionary<long, float> LastRequests = new Dictionary<long, float>();
        internal static List<GuidePage> Pages { get; private set; } = new List<GuidePage>();
        private static ZNet? _session;
        private static string _text = "";
        private static string _revision = "";
        private static DateTime _fileStamp;
        private static long _fileLength = -1;
        private static float _nextCheck;
        private static float _nextRequest;
        private static bool _received;
        internal static string FilePath => Path.Combine(Paths.ConfigPath, "PraetorisClient.ServerGuide.txt");

        internal static void Register(ZRoutedRpc rpc)
        {
            rpc.Register<string>(RequestRpc, OnRequest);
            rpc.Register<ZPackage>(ResponseRpc, OnResponse);
        }

        internal static void Initialize()
        {
            _ = new Terminal.ConsoleCommand("praetoris_guide", "Open the server guide in the compendium.", args =>
            {
                if (Player.m_localPlayer == null || InventoryGui.instance == null)
                {
                    args.Context.AddString("Join a world before opening the guide.");
                    return;
                }
                InventoryGui.instance.Show(null);
                InventoryGui.instance.OnOpenTexts();
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

        private static string Status() => $"Server guide: pages={Pages.Count}, received={_received}, revision={_revision}";

        internal static void Update()
        {
            if (!ReferenceEquals(_session, ZNet.instance))
            {
                _session = ZNet.instance;
                Pages = new List<GuidePage>();
                _text = _revision = "";
                _fileStamp = default;
                _fileLength = -1;
                _nextCheck = _nextRequest = 0;
                _received = false;
                LastRequests.Clear();
            }
            if (_session == null || ZRoutedRpc.instance == null)
                return;
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
                    File.WriteAllText(FilePath, "# Welcome\nWelcome to the server guide.\n\nThe server owner can edit PraetorisClient.ServerGuide.txt in BepInEx/config.\n", new UTF8Encoding(false));
                FileInfo file = new FileInfo(FilePath);
                if (!force && file.LastWriteTimeUtc == _fileStamp && file.Length == _fileLength)
                    return Status();
                _fileStamp = file.LastWriteTimeUtc;
                _fileLength = file.Length;
                if (file.Length > GuideDocument.MaximumBytes)
                    throw new FormatException("The guide must be at most 128 KiB.");
                string text = File.ReadAllText(FilePath, Encoding.UTF8);
                List<GuidePage> pages = GuideDocument.Parse(text);
                using SHA256 hash = SHA256.Create();
                string revision = Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(text)));
                Pages = pages;
                _text = text;
                _revision = revision;
                _received = true;
                PraetorisClientPlugin.Log.LogInfo($"Loaded server guide: {pages.Count} pages, revision={revision}");
                return Status();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is FormatException)
            {
                string message = "Server guide reload failed; keeping the previous pages. " + ex.Message;
                PraetorisClientPlugin.Log.LogWarning(message);
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
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, ResponseRpc, package);
        }

        private static void OnResponse(long sender, ZPackage package)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer() || sender != ZRoutedRpc.instance.GetServerPeerID())
                return;
            try
            {
                if (package.Size() > GuideDocument.MaximumBytes + 8)
                    throw new FormatException("Guide response exceeds the size limit.");
                string text = package.ReadString();
                List<GuidePage> pages = GuideDocument.Parse(text);
                using SHA256 hash = SHA256.Create();
                _revision = Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(text)));
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
