using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PraetorisClient
{
    internal static class CrossbowEffectDefinitions
    {
        internal const string Recoil = "PraetorisRecoilDash";
        internal const string Capacity = "PraetorisHeavyLoad";
        internal const string ActiveReload = "PraetorisActiveReload";
        internal const string Grapple = "PraetorisGrapplingHook";

        internal static void Register()
        {
            AddEffect(Recoil, "Recoil Dash: {0:0}% crossbow recoil force",
                "Crossbow shots push you opposite your facing direction. Granted by a Unique shardstone.");
            AddEffect(Grapple, "Grappling Hook: crossbow special attack",
                "Use the crossbow special attack to launch a grappling hook without spending bolts. Block to release. Uses the normal grappling hook stamina cost.");
            AddEffect(Capacity, "Heavy Load: +{0:0} crossbow shots (longer reload)",
                "Each extra shot adds one normal reload duration. One extra shot takes twice as long to reload; two take three times as long.");
            AddEffect(ActiveReload, "Active Reload: +{0:0}% damage on the next shot after a timed reload",
                "During a crossbow reload, press this Epic Loot ability key while the reload bar is in the marked window. Success finishes the reload immediately. You get one attempt per reload.", ActiveReload);

            if (!EpicLootApiBridge.TryRegisterProxyAbility(JsonConvert.SerializeObject(new
                { ID = ActiveReload, IconAsset = "BerserkerIcon", ActivationMode = "Activated", Cooldown = 0, Action = "Custom" }),
                CrossbowEffectsRuntime.CreateAbilityCallbacks(), out _))
                throw new InvalidOperationException("Could not register Active Reload ability.");

            string[] ids = { "PraetorisArbalistHelm", "PraetorisArbalistChest", "PraetorisArbalistLegs", "PraetorisArbalistCrossbow" };
            string[] names = { "Arbalist's Helm", "Arbalist's Coat", "Arbalist's Leggings", "Arbalist's Crossbow" };
            string[] types = { "Helmet", "Chest", "Legs", "Bow" };
            for (int index = 0; index < ids.Length; index++)
            {
                string json = JsonConvert.SerializeObject(new
                {
                    ID = ids[index], Name = names[index], Description = "Part of the Arbalist set.",
                    IsSetItem = true, SelectionWeight = 1,
                    Requirements = new
                    {
                        AllowedItemTypes = new[] { types[index] },
                        AllowedSkillTypes = index == 3 ? new[] { "Crossbows" } : Array.Empty<string>()
                    }
                });
                if (string.IsNullOrEmpty(EpicLoot.API.AddLegendaryItem("Legendary", json)))
                    throw new InvalidOperationException("Could not register " + ids[index]);
            }
            string set = JsonConvert.SerializeObject(new
            {
                ID = "PraetorisArbalist", Name = "Arbalist", LegendaryIDs = ids,
                SetBonuses = new[]
                {
                    new { Count = 2, Effect = new { Type = Capacity, Values = new { MinValue = 1, MaxValue = 1 } } },
                    new { Count = 4, Effect = new { Type = ActiveReload, Values = new { MinValue = 35, MaxValue = 35 } } }
                }
            });
            if (string.IsNullOrEmpty(EpicLoot.API.AddLegendarySet("Legendary", set)))
                throw new InvalidOperationException("Could not register Arbalist set.");
        }

        private static void AddEffect(string type, string display, string description, string? ability = null)
        {
            JObject definition = new JObject
            {
                ["Type"] = type, ["DisplayText"] = display, ["Description"] = description,
                ["CanBeAugmented"] = false, ["CanBeDisenchanted"] = false, ["CanBeRunified"] = false,
                ["Requirements"] = new JObject { ["NoRoll"] = true }
            };
            if (ability != null) definition["Ability"] = ability;
            if (!EpicLootApiBridge.TryAddMagicEffect(definition.ToString(), out _))
                throw new InvalidOperationException("Could not register " + type);
        }
    }
}
