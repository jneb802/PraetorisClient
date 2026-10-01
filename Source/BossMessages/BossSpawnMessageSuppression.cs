using System;
using System.Collections.Generic;
using UnityEngine;

namespace PraetorisClient
{
    internal static class BossSpawnMessageSuppression
    {
        private static readonly string[] VanillaSpawnMessages =
        {
            "$event_boss01_start",
            "$event_boss02_start",
            "$enemy_eikthyr_alertmessage",
            "$enemy_gdking_alertmessage",
            "$enemy_boss_bonemass_spawnmessage",
            "$enemy_boss_dragon_spawnmessage",
            "$enemy_boss_goblinking_spawnmessage",
            "$enemy_boss_queen_alertmessage",
            "$enemy_boss_fader_alertmessage"
        };

        private static readonly HashSet<string> SpawnMessages = new(StringComparer.Ordinal);
        private static readonly HashSet<string> BossEvents = new(StringComparer.Ordinal);
        private static ZNetScene? _scene;

        internal static void Refresh()
        {
            SpawnMessages.Clear();
            SpawnMessages.UnionWith(VanillaSpawnMessages);
            BossEvents.Clear();
            _scene = ZNetScene.instance;
            if (_scene == null)
            {
                return;
            }

            foreach (GameObject prefab in _scene.m_prefabs)
            {
                if (prefab == null)
                {
                    continue;
                }

                Character character = prefab.GetComponent<Character>();
                if (character == null || !character.IsBoss())
                {
                    continue;
                }

                Add(BossEvents, character.m_bossEvent);
                BaseAI bossAi = prefab.GetComponent<BaseAI>();
                if (bossAi != null)
                {
                    Add(SpawnMessages, bossAi.m_spawnMessage);
                    Add(SpawnMessages, bossAi.m_alertedMessage);
                }
            }
        }

        internal static bool ShouldSuppress(MessageHud.MessageType type, string text)
        {
            if (type != MessageHud.MessageType.Center || string.IsNullOrEmpty(text) ||
                !PraetorisClientPlugin.SuppressBossSpawnMessages.Value ||
                (ZNet.instance != null && ZNet.instance.IsDedicated()))
            {
                return false;
            }

            if (_scene != ZNetScene.instance || SpawnMessages.Count == 0)
            {
                Refresh();
            }

            // Forced boss events can display messages locally without a routed RPC.
            // Read their current settings because event mods can change them at runtime.
            RandEventSystem events = RandEventSystem.instance;
            if (events != null)
            {
                foreach (RandomEvent bossEvent in events.m_events)
                {
                    if (BossEvents.Contains(bossEvent.m_name) && Matches(text, bossEvent.m_startMessage))
                    {
                        return true;
                    }
                }
            }

            if (SpawnMessages.Contains(text))
            {
                return true;
            }

            // Some mods localize a message before calling ShowMessage.
            // Resolve the current language at display time, including language changes.
            if (Localization.instance != null)
            {
                foreach (string message in SpawnMessages)
                {
                    if (Matches(text, message))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool Matches(string text, string message)
        {
            return !string.IsNullOrEmpty(message) &&
                (string.Equals(text, message, StringComparison.Ordinal) ||
                 (Localization.instance != null &&
                  string.Equals(text, Localization.instance.Localize(message), StringComparison.Ordinal)));
        }

        private static void Add(HashSet<string> values, string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                values.Add(value);
            }
        }
    }
}
