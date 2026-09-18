using System;
using System.Collections.Generic;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace PraetorisClient
{
    internal static class WardBuildIcon
    {
        private static readonly List<Sprite> Generated = new List<Sprite>();

        internal static void Apply(GameObject ward, string trophyPrefab)
        {
            // Dedicated servers register the pieces but do not need menu textures.
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            Piece piece = ward.GetComponent<Piece>();
            ItemDrop? trophy = PrefabManager.Instance.GetPrefab(trophyPrefab)?.GetComponent<ItemDrop>();
            Sprite baseIcon = piece.m_icon;
            Sprite? trophyIcon = trophy != null ? trophy.m_itemData.GetIcon() : null;
            if (baseIcon == null || trophyIcon == null)
            {
                PraetorisClientPlugin.Log.LogWarning($"Cannot create {ward.name} build icon: ward or {trophyPrefab} icon missing.");
                return;
            }

            Texture2D? wardCopy = null;
            Texture2D? trophyCopy = null;
            Texture2D? composite = null;
            try
            {
                wardCopy = AssetUtils.DuplicateTexture(baseIcon.texture, baseIcon.textureRect);
                trophyCopy = AssetUtils.DuplicateTexture(trophyIcon.texture, trophyIcon.textureRect);
                wardCopy.wrapMode = trophyCopy.wrapMode = TextureWrapMode.Clamp;
                const int size = 128;
                Color[] pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                        pixels[y * size + x] = wardCopy.GetPixelBilinear((x + 0.5f) / size, (y + 0.5f) / size);

                // Use the original trophy sprite as a large upper-right badge. Leave the
                // lower half of the ward visible so the icon still reads as a building piece.
                const int badgeSize = 76;
                for (int y = 0; y < badgeSize; y++)
                    for (int x = 0; x < badgeSize; x++)
                    {
                        Color top = trophyCopy.GetPixelBilinear((x + 0.5f) / badgeSize, (y + 0.5f) / badgeSize);
                        int index = (y + size - badgeSize) * size + x + size - badgeSize;
                        Color bottom = pixels[index];
                        float alpha = top.a + bottom.a * (1 - top.a);
                        pixels[index] = alpha > 0 ? new Color(
                            (top.r * top.a + bottom.r * bottom.a * (1 - top.a)) / alpha,
                            (top.g * top.a + bottom.g * bottom.a * (1 - top.a)) / alpha,
                            (top.b * top.a + bottom.b * bottom.a * (1 - top.a)) / alpha, alpha) : Color.clear;
                    }
                composite = new Texture2D(size, size, TextureFormat.RGBA32, false)
                { name = ward.name + "_build_icon", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                composite.SetPixels(pixels);
                composite.Apply(false, true);
                Sprite icon = Sprite.Create(composite, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
                icon.name = composite.name;
                Generated.Add(icon);
                piece.m_icon = icon;
                composite = null; // The generated sprite owns this texture until shutdown.
                PraetorisClientPlugin.Log.LogInfo($"Created {ward.name} build icon with {trophyPrefab}.");
            }
            catch (Exception exception)
            {
                PraetorisClientPlugin.Log.LogWarning($"Cannot create {ward.name} build icon: {exception.Message}");
            }
            finally
            {
                if (wardCopy != null) Object.Destroy(wardCopy);
                if (trophyCopy != null) Object.Destroy(trophyCopy);
                if (composite != null) Object.Destroy(composite);
            }
        }

        internal static void Shutdown()
        {
            foreach (Sprite icon in Generated)
            {
                if (icon == null) continue;
                Object.Destroy(icon.texture);
                Object.Destroy(icon);
            }
            Generated.Clear();
        }
    }
}
