using System;
using System.Collections.Generic;
using UnityEngine;

namespace PraetorisClient
{
    internal static partial class PraetorisMagicEffects
    {
        internal const string IncreaseEffectDuration = "IncreaseEffectDuration";
        internal const string ModifyAdrenaline = "ModifyAdrenaline";
        internal const string ModifyAdrenalineCost = "ModifyAdrenalineCost";
        internal const string PiercingShot = "PiercingShot";
        internal const string ReloadOnKill = "ReloadOnKill";
        internal const string ArrowRain = "ArrowRain";
        internal const string Siedrweaver = "Siedrweaver";
        internal const string Sturdy = "Sturdy";
        internal const string StaminaLeech = "StaminaLeech";

        private const float PercentScale = 0.01f;

        private static readonly string[] MagicEffectDefinitionJson =
        {
            IncreaseEffectDurationDefinitionJson,
            ModifyAdrenalineDefinitionJson,
            ModifyAdrenalineCostDefinitionJson,
            PiercingShotDefinitionJson,
            ReloadOnKillDefinitionJson,
            ArrowRainDefinitionJson,
            SiedrweaverDefinitionJson,
            SturdyDefinitionJson,
            StaminaLeechDefinitionJson
        };

        internal static void Register()
        {
            RegisterProxyAbilities();
            foreach (string definitionJson in MagicEffectDefinitionJson)
            {
                if (EpicLootApiBridge.TryAddMagicEffect(definitionJson, out string key))
                {
                    PraetorisClientPlugin.Log.LogInfo("Registered Epic Loot magic effect with key " + key + ".");
                }
            }
        }

        private static void RegisterProxyAbilities()
        {
            RegisterSiedrweaverProxyAbility();
            RegisterArrowRainProxyAbility();
        }

        private static void RegisterProxyAbility(string json, Dictionary<string, Delegate> callbacks)
        {
            if (EpicLootApiBridge.TryRegisterProxyAbility(json, callbacks, out string key))
            {
                PraetorisClientPlugin.Log.LogInfo("Registered Epic Loot proxy ability with key " + key + ".");
            }
        }

        private static bool PlayerHasEffect(Player player, string effectType, out float value, float scale = 1f)
        {
            return EpicLootApiBridge.PlayerHasActiveMagicEffect(player, effectType, out value, scale);
        }

        private static float GetPlayerEffectValue(Player player, string effectType, float scale = 1f)
        {
            return EpicLootApiBridge.GetTotalPlayerActiveMagicEffectValue(player, effectType, scale);
        }

        private static float GetWeaponEffectValue(Player player, ItemDrop.ItemData weapon, string effectType, float scale = 1f)
        {
            return EpicLootApiBridge.GetTotalActiveMagicEffectValueForWeapon(player, weapon, effectType, scale);
        }

        private static void AddStatusToPlayersInRange(Player sourcePlayer, string statusEffectName, float skillLevel, float range)
        {
            if (sourcePlayer == null)
            {
                return;
            }

            int statusHash = statusEffectName.GetStableHashCode();
            Vector3 sourcePoint = sourcePlayer.transform.position;
            Collider[] results = Physics.OverlapSphere(sourcePoint, range, Character.s_characterLayer);
            HashSet<Player> affectedPlayers = new HashSet<Player>();
            foreach (Collider collider in results)
            {
                Player player = collider.GetComponentInParent<Player>();
                if (player == null || affectedPlayers.Contains(player))
                {
                    continue;
                }

                affectedPlayers.Add(player);
                player.GetSEMan().AddStatusEffect(statusHash, true, 0, skillLevel);
            }
        }

        private static float GetNetworkTime()
        {
            return ZNet.instance == null ? Time.time : (float)ZNet.instance.GetTimeSeconds();
        }

        private static float GetCooldownEnd(Player? player, string abilityId)
        {
            ZNetView? netView = player == null ? null : player.GetComponent<ZNetView>();
            return netView?.GetZDO()?.GetFloat("EpicLoot." + abilityId + ".CooldownEnd") ?? 0f;
        }

        private static void SetCooldownEnd(Player? player, string abilityId, float cooldownEnd)
        {
            ZNetView? netView = player == null ? null : player.GetComponent<ZNetView>();
            netView?.GetZDO()?.Set("EpicLoot." + abilityId + ".CooldownEnd", cooldownEnd);
        }

        private static bool IsOnCooldown(Player player, string abilityId)
        {
            return GetNetworkTime() < GetCooldownEnd(player, abilityId);
        }
    }
}
