using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace PraetorisClient
{
    internal static class WitheringBombFeature
    {
        internal const string ItemPrefabName = "PraetorisWitheringBomb";
        internal const string StatusEffectName = "SE_PraetorisWithered";
        private const string BaseItemPrefabName = "BombBile";
        private const string ProjectilePrefabName = "PraetorisWitheringBombProjectile";
        private const string AoePrefabName = "PraetorisWitheringBombAoe";
        private static bool _registered;

        internal static void Initialize()
        {
            PrefabManager.OnVanillaPrefabsAvailable += Register;
        }

        internal static void Shutdown()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Register;
            _registered = false;
        }

        private static void Register()
        {
            if (_registered)
            {
                PrefabManager.OnVanillaPrefabsAvailable -= Register;
                return;
            }

            CustomItem customItem = new(ItemPrefabName, BaseItemPrefabName, CreateItemConfig());
            if (!customItem.ItemPrefab || !customItem.ItemDrop)
            {
                PraetorisClientPlugin.Log.LogError("Failed to clone " + BaseItemPrefabName + " for the Withering Bomb.");
                return;
            }

            Sprite icon = customItem.ItemDrop.m_itemData.m_shared.m_icons[0];
            WitheredStatusEffect statusEffect = WitheredStatusEffect.Create(icon);
            if (!ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(statusEffect, false)))
            {
                PraetorisClientPlugin.Log.LogError("Failed to register the Withered status effect.");
                return;
            }

            if (!ConfigureProjectile(customItem.ItemDrop, statusEffect))
            {
                return;
            }

            if (ItemManager.Instance.AddItem(customItem))
            {
                _registered = true;
                PrefabManager.OnVanillaPrefabsAvailable -= Register;
                PraetorisClientPlugin.Log.LogInfo("Registered Withering Bomb and its no-regeneration status effect.");
            }
        }

        private static ItemConfig CreateItemConfig()
        {
            return new ItemConfig
            {
                Name = "Withering Bomb",
                Description = "A bile bomb that prevents enemies from regenerating health for a short time.",
                Amount = 3,
                CraftingStation = CraftingStations.BlackForge,
                MinStationLevel = 1,
                Requirements = new[]
                {
                    new RequirementConfig("Bilebag", 1),
                    new RequirementConfig("Sap", 1),
                    new RequirementConfig("Resin", 3)
                }
            };
        }

        private static bool ConfigureProjectile(ItemDrop itemDrop, StatusEffect statusEffect)
        {
            ItemDrop.ItemData.SharedData shared = itemDrop.m_itemData.m_shared;
            shared.m_attackStatusEffect = statusEffect;
            shared.m_attackStatusEffectChance = 1f;

            Attack attack = shared.m_attack;
            GameObject baseProjectile = attack.m_attackProjectile;
            if (!baseProjectile)
            {
                PraetorisClientPlugin.Log.LogError("The vanilla bile bomb does not define an attack projectile.");
                return false;
            }

            GameObject projectilePrefab = PrefabManager.Instance.CreateClonedPrefab(ProjectilePrefabName, baseProjectile);
            Projectile? projectile = projectilePrefab != null ? projectilePrefab.GetComponent<Projectile>() : null;
            if (projectile == null)
            {
                PraetorisClientPlugin.Log.LogError("Failed to clone the vanilla bile bomb projectile.");
                return false;
            }

            projectile.m_statusEffect = statusEffect.name;
            if (projectile.m_spawnOnHit != null)
            {
                GameObject aoePrefab = PrefabManager.Instance.CreateClonedPrefab(AoePrefabName, projectile.m_spawnOnHit);
                Aoe? aoe = aoePrefab != null ? aoePrefab.GetComponent<Aoe>() : null;
                if (aoe == null)
                {
                    PraetorisClientPlugin.Log.LogError("Failed to clone the vanilla bile bomb impact area.");
                    return false;
                }

                aoe.m_statusEffect = statusEffect.name;
                aoe.m_statusEffectIfBoss = statusEffect.name;
                projectile.m_spawnOnHit = aoePrefab;
                PrefabManager.Instance.AddPrefab(new CustomPrefab(aoePrefab, false));
            }

            PrefabManager.Instance.AddPrefab(new CustomPrefab(projectilePrefab, false));
            attack.m_attackProjectile = projectilePrefab;
            return true;
        }
    }

    internal sealed class WitheredStatusEffect : StatusEffect
    {
        private bool _loggedRegenerationBlock;

        internal static WitheredStatusEffect Create(Sprite icon)
        {
            WitheredStatusEffect statusEffect = CreateInstance<WitheredStatusEffect>();
            statusEffect.name = WitheringBombFeature.StatusEffectName;
            statusEffect.m_name = "Withered";
            statusEffect.m_tooltip = "Health regeneration is disabled.";
            statusEffect.m_icon = icon;
            statusEffect.m_flashIcon = true;
            statusEffect.m_ttl = PraetorisClientPlugin.WitheringBombDurationSeconds.Value;
            return statusEffect;
        }

        public override bool CanAdd(Character character)
        {
            return character != null && !character.IsPlayer();
        }

        public override void Setup(Character character)
        {
            m_ttl = PraetorisClientPlugin.WitheringBombDurationSeconds.Value;
            base.Setup(character);
            PraetorisClientPlugin.Log.LogDebug(
                $"Applied Withered to {character.m_name} for {m_ttl:0.0} seconds.");
        }

        public override void ResetTime()
        {
            m_ttl = PraetorisClientPlugin.WitheringBombDurationSeconds.Value;
            base.ResetTime();
        }

        public override void Stop()
        {
            PraetorisClientPlugin.Log.LogDebug("Withered expired on " + m_character.m_name + ".");
            base.Stop();
        }

        internal void RecordRegenerationBlock()
        {
            if (_loggedRegenerationBlock)
            {
                return;
            }

            _loggedRegenerationBlock = true;
            PraetorisClientPlugin.Log.LogDebug("Blocked health regeneration for " + m_character.m_name + ".");
        }
    }

    [HarmonyPatch(typeof(BaseAI), "UpdateRegeneration")]
    internal static class WitheringBombRegenerationPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(BaseAI __instance)
        {
            Character? character = __instance != null ? __instance.GetComponent<Character>() : null;
            if (character == null)
            {
                return true;
            }

            WitheredStatusEffect? statusEffect = character.GetSEMan()
                .GetStatusEffect(WitheringBombFeature.StatusEffectName.GetStableHashCode()) as WitheredStatusEffect;
            if (statusEffect == null)
            {
                return true;
            }

            statusEffect.RecordRegenerationBlock();
            return false;
        }
    }
}
