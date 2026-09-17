using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace PraetorisClient.ServerGuideFeature
{
    internal static class GuideImages
    {
        private const string Prefix = "PraetorisClient.GuideImages.";
        private static readonly Assembly Assembly = typeof(GuideImages).Assembly;
        private static readonly Dictionary<string, string> Names = Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal))
            .ToDictionary(name => name.Substring(Prefix.Length), name => name, StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> Failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Unity 6 also exposes a Span overload unavailable to this net481 plugin.
        private static readonly Func<Texture2D, byte[], bool, bool> LoadPng =
            (Func<Texture2D, byte[], bool, bool>)Delegate.CreateDelegate(typeof(Func<Texture2D, byte[], bool, bool>),
                typeof(ImageConversion).GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) }));

        internal static int Count => Names.Count;

        internal static void Validate(IEnumerable<GuidePage> pages)
        {
            foreach (string name in GuideMarkup.Images(pages))
                if (!Names.ContainsKey(name))
                    throw new FormatException("Guide image is not embedded in this mod build: " + name);
        }

        internal static Sprite? Get(string name)
        {
            if (Sprites.TryGetValue(name, out Sprite sprite)) return sprite;
            if (Failed.Contains(name)) return null;
            using Stream? stream = Names.TryGetValue(name, out string resource) ? Assembly.GetManifestResourceStream(resource) : null;
            if (stream == null)
            {
                Failed.Add(name);
                PraetorisClientPlugin.Log.LogWarning("Missing embedded guide image: " + name);
                return null;
            }
            using MemoryStream data = new MemoryStream();
            stream.CopyTo(data);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!LoadPng(texture, data.ToArray(), true))
            {
                UnityEngine.Object.Destroy(texture);
                Failed.Add(name);
                PraetorisClientPlugin.Log.LogWarning("Could not decode embedded guide image: " + name);
                return null;
            }
            texture.wrapMode = TextureWrapMode.Clamp;
            sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            Sprites.Add(name, sprite);
            return sprite;
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
        }
    }
}
