using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using UnityEngine;

namespace PraetorisClient.ServerGuideFeature
{
    internal static class GuideImages
    {
        private const string RequestRpc = "PraetorisClient.GuideImageRequest";
        private const string ResponseRpc = "PraetorisClient.GuideImageResponse";
        private static Dictionary<string, GuideImageData> _images = new Dictionary<string, GuideImageData>();
        private static readonly Dictionary<long, float> LastRequests = new Dictionary<long, float>();
        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
        private static readonly HashSet<string> Failed = new HashSet<string>();
        // Select the byte[] overload explicitly. Unity 6 also exposes a Span overload,
        // whose framework types are unavailable to this net481 plugin at compile time.
        private static readonly Func<Texture2D, byte[], bool, bool> LoadPng =
            (Func<Texture2D, byte[], bool, bool>)Delegate.CreateDelegate(typeof(Func<Texture2D, byte[], bool, bool>),
                typeof(ImageConversion).GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) }));
        private static string _pending = "";
        private static byte[] _buffer = Array.Empty<byte>();
        private static int _offset;
        private static float _nextRequest;
        private static float _nextCleanup;
        internal static int Version { get; private set; }
        internal static string Status => $"{_images.Values.Count(image => image.Bytes.Length > 0)}/{_images.Count}";
        internal static string DirectoryPath => Path.Combine(Paths.ConfigPath, "PraetorisClient.ServerGuide.images");

        internal static void Register(ZRoutedRpc rpc)
        {
            rpc.Register<ZPackage>(RequestRpc, OnRequest);
            rpc.Register<ZPackage>(ResponseRpc, OnResponse);
        }

        internal static string SourceStamp(List<GuidePage> pages) => string.Join("|", GuideMarkup.Images(pages).Select(name =>
        {
            FileInfo file = new FileInfo(Path.Combine(DirectoryPath, name));
            return name + (file.Exists ? $":{file.LastWriteTimeUtc.Ticks}:{file.Length}:{file.Attributes}" : ":missing");
        }));

        internal static Dictionary<string, GuideImageData> Load(List<GuidePage> pages)
        {
            Directory.CreateDirectory(DirectoryPath);
            List<string> names = GuideMarkup.Images(pages).ToList();
            if (names.Count > GuideImageData.MaximumImages) throw new FormatException("A guide can reference at most 8 PNG images.");
            return names.ToDictionary(name => name, name => GuideImageData.Load(DirectoryPath, name));
        }

        internal static string GetRevision(string text, Dictionary<string, GuideImageData> images) =>
            GuideImageData.Digest(Encoding.UTF8.GetBytes(text + "\0" + string.Join("|", images.Values.OrderBy(image => image.Name, StringComparer.Ordinal)
                .Select(image => image.Name + ":" + image.Hash + ":" + image.Length))));

        internal static void WriteManifest(ZPackage package)
        {
            package.Write(_images.Count);
            foreach (GuideImageData image in _images.Values)
            {
                package.Write(image.Name);
                package.Write(image.Hash);
                package.Write(image.Length);
            }
        }

        internal static Dictionary<string, GuideImageData> ReadManifest(ZPackage package, List<GuidePage> pages)
        {
            HashSet<string> names = new HashSet<string>(GuideMarkup.Images(pages), StringComparer.Ordinal);
            int count = package.ReadInt();
            if (count != names.Count || count > GuideImageData.MaximumImages) throw new FormatException("Invalid image count.");
            Dictionary<string, GuideImageData> images = new Dictionary<string, GuideImageData>();
            for (int index = 0; index < count; index++)
            {
                GuideImageData image = new GuideImageData { Name = package.ReadString(), Hash = package.ReadString(), Length = package.ReadInt() };
                if (!names.Remove(image.Name) || image.Hash.Length != 44 || Convert.FromBase64String(image.Hash).Length != 32 || image.Length < 33 || image.Length > GuideImageData.MaximumBytes)
                    throw new FormatException("Invalid image metadata.");
                images.Add(image.Name, image);
            }
            return images;
        }

        internal static void Set(Dictionary<string, GuideImageData> images)
        {
            Clear();
            _images = images;
        }

        internal static void Clear()
        {
            foreach (Sprite sprite in Sprites.Values)
            {
                UnityEngine.Object.Destroy(sprite.texture);
                UnityEngine.Object.Destroy(sprite);
            }
            Sprites.Clear();
            Failed.Clear();
            LastRequests.Clear();
            _images = new Dictionary<string, GuideImageData>();
            _pending = "";
            _buffer = Array.Empty<byte>();
            _offset = 0;
            _nextRequest = _nextCleanup = 0;
            Version++;
        }

        internal static bool HasFailed(string name) => Failed.Contains(name);

        internal static Sprite? Get(string name)
        {
            if (Sprites.TryGetValue(name, out Sprite sprite)) return sprite;
            if (!_images.TryGetValue(name, out GuideImageData image) || image.Bytes.Length == 0 || Failed.Contains(name)) return null;
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!LoadPng(texture, image.Bytes, true))
            {
                UnityEngine.Object.Destroy(texture);
                Failed.Add(name);
                PraetorisClientPlugin.Log.LogWarning("Could not decode guide image: " + name);
                return null;
            }
            texture.wrapMode = TextureWrapMode.Clamp;
            sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            Sprites.Add(name, sprite);
            return sprite;
        }

        internal static void Update()
        {
            if (ZNet.instance.IsServer())
            {
                if (Time.realtimeSinceStartup < _nextCleanup) return;
                _nextCleanup = Time.realtimeSinceStartup + 5f;
                foreach (long peer in new List<long>(LastRequests.Keys))
                    if (ZNet.instance.GetPeer(peer) == null) LastRequests.Remove(peer);
                return;
            }
            if (Player.m_localPlayer == null || Time.realtimeSinceStartup < _nextRequest) return;
            if (_pending.Length == 0)
            {
                GuideImageData? image = _images.Values.FirstOrDefault(data => data.Bytes.Length == 0 && !Failed.Contains(data.Name));
                if (image == null) return;
                _pending = image.Name;
                _buffer = new byte[image.Length];
                _offset = 0;
            }
            ZPackage request = new ZPackage();
            request.Write(_pending);
            request.Write(_images[_pending].Hash);
            request.Write(_offset);
            _nextRequest = Time.realtimeSinceStartup + 2f;
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RequestRpc, request);
        }

        private static void OnRequest(long sender, ZPackage package)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZNet.instance.GetPeer(sender) == null || package.Size() > 256) return;
            float now = Time.realtimeSinceStartup;
            if (LastRequests.TryGetValue(sender, out float last) && now - last < 0.1f) return;
            LastRequests[sender] = now;
            try
            {
                string name = package.ReadString();
                string hash = package.ReadString();
                int offset = package.ReadInt();
                if (!_images.TryGetValue(name, out GuideImageData image) || hash != image.Hash || offset < 0 || offset >= image.Length || offset % GuideImageData.ChunkBytes != 0) return;
                byte[] chunk = new byte[Math.Min(GuideImageData.ChunkBytes, image.Length - offset)];
                Buffer.BlockCopy(image.Bytes, offset, chunk, 0, chunk.Length);
                ZPackage response = new ZPackage();
                response.Write(name);
                response.Write(hash);
                response.Write(offset);
                response.Write(chunk);
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, ResponseRpc, response);
            }
            catch (IOException) { /* Ignore malformed peer requests. */ }
            catch (ArgumentException) { /* Ignore malformed peer requests. */ }
            catch (FormatException) { /* Ignore malformed peer requests. */ }
        }

        private static void OnResponse(long sender, ZPackage package)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer() || sender != ZRoutedRpc.instance.GetServerPeerID() || package.Size() > GuideImageData.ChunkBytes + 256) return;
            try
            {
                string name = package.ReadString();
                string hash = package.ReadString();
                int offset = package.ReadInt();
                if (name != _pending || !_images.TryGetValue(name, out GuideImageData image) || hash != image.Hash || offset != _offset) return;
                byte[] chunk = package.ReadByteArray();
                if (chunk.Length != Math.Min(GuideImageData.ChunkBytes, image.Length - offset)) throw new FormatException("Invalid image chunk size.");
                Buffer.BlockCopy(chunk, 0, _buffer, offset, chunk.Length);
                _offset += chunk.Length;
                _nextRequest = Time.realtimeSinceStartup + 0.2f;
                if (_offset != image.Length) return;
                GuideImageData.Validate(_buffer);
                if (GuideImageData.Digest(_buffer) != image.Hash) throw new FormatException("Image checksum does not match.");
                image.Bytes = _buffer;
                _buffer = Array.Empty<byte>();
                _pending = "";
                Version++;
                PraetorisClientPlugin.Log.LogInfo($"Received guide image: {name}, bytes={image.Length}, hash={image.Hash}");
            }
            catch (Exception ex) when (ex is IOException || ex is FormatException || ex is ArgumentException)
            {
                Failed.Add(_pending);
                _pending = "";
                _buffer = Array.Empty<byte>();
                Version++;
                PraetorisClientPlugin.Log.LogWarning("Invalid guide image response: " + ex.Message);
            }
        }
    }
}
