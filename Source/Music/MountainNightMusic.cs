using System;
using System.Collections;
using System.IO;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

namespace PraetorisClient.Music
{
    internal static class MountainNightMusic
    {
        private const string MusicName = "praetoris_mountain_night";
        private static readonly int MusicHash = MusicName.GetStableHashCode();
        private static ConfigEntry<bool> _enabled = null!;

        internal static void Initialize(ConfigFile config)
        {
            _enabled = config.Bind("Music", "MountainNightEnabled", true,
                "Play the Mountain night track outdoors at night. This is a local setting.");
        }

        private static IEnumerator Load(MusicMan musicMan)
        {
            if (Application.isBatchMode)
                yield break;

            string path = Path.Combine(Path.GetDirectoryName(typeof(MountainNightMusic).Assembly.Location)!,
                "Audio", "mountain-night.ogg");
            if (!File.Exists(path))
            {
                PraetorisClientPlugin.Log.LogWarning("Mountain night audio is missing; keeping normal music.");
                yield break;
            }

            using UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, AudioType.OGGVORBIS);
            ((DownloadHandlerAudioClip)request.downloadHandler).compressed = true;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                PraetorisClientPlugin.Log.LogWarning($"Mountain night audio could not load: {request.error}. Keeping normal music.");
                yield break;
            }

            AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
            if (!clip || !musicMan)
            {
                if (clip)
                    UnityEngine.Object.Destroy(clip);
                yield break;
            }

            clip.name = MusicName;
            MusicMan.NamedMusic mountain = musicMan.FindMusic("mountain");
            MusicMan.NamedMusic music = new MusicMan.NamedMusic
            {
                m_name = MusicName,
                m_clips = new[] { clip },
                m_ambientMusic = true,
                m_loop = Settings.ContinousMusic,
                m_fadeInTime = mountain?.m_fadeInTime ?? 10f,
                m_alwaysFadeout = mountain?.m_alwaysFadeout ?? true,
                m_resume = mountain?.m_resume ?? true,
                // The source averages about 2 dB louder than the original Mountain track.
                m_volume = (mountain?.m_volume ?? 0.3f) * 0.8f
            };
            musicMan.m_music.Add(music);
            musicMan.m_musicHashes[MusicHash] = music;
            PraetorisClientPlugin.Log.LogInfo("Mountain night audio loaded.");
        }

        [HarmonyPatch(typeof(MusicMan), nameof(MusicMan.Awake))]
        private static class LoadPatch
        {
            private static void Postfix(MusicMan __instance)
            {
                if (MusicMan.instance == __instance)
                    __instance.StartCoroutine(Load(__instance));
            }
        }

        [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.GetAmbientMusic))]
        private static class AmbientMusicPatch
        {
            private static void Postfix(ref string __result)
            {
                Player player = Player.m_localPlayer;
                MusicMan musicMan = MusicMan.instance;
                if (_enabled.Value && player && musicMan && EnvMan.IsNight()
                    && !player.InInterior() && player.GetCurrentBiome() == Heightmap.Biome.Mountain
                    && musicMan.m_musicHashes.ContainsKey(MusicHash))
                    __result = MusicName;
            }
        }

        [HarmonyPatch(typeof(MusicMan), nameof(MusicMan.OnDestroy))]
        private static class CleanupPatch
        {
            private static void Prefix(MusicMan __instance)
            {
                if (__instance.m_musicHashes.TryGetValue(MusicHash, out MusicMan.NamedMusic music))
                {
                    foreach (AudioClip clip in music.m_clips)
                        UnityEngine.Object.Destroy(clip);
                }
            }
        }
    }
}
