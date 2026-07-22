using EpicLootAPI;
using UnityEngine;
using Jotunn.Managers;
using static EpicLootLeslieAlphaTest.src.MagicEffectss.Infusion;

namespace EpicLootLeslieAlphaTest.src
{
    public static class MagicEffects
    {
        public static void Init()
        {
            var ancestralSlam = new MagicItemEffectDefinition("AncestralSlam", "Ancestral Slam", "Summons ancestral spirit to mirror your attacks for each enemy hit.");
            ancestralSlam.Requirements.AllowedItemTypes.Add("TwoHandedWeapon");
            ancestralSlam.Requirements.AllowedSkillTypes.Add(Skills.SkillType.Clubs);
            ancestralSlam.Requirements.AllowedRarities.Add(ItemRarity.Epic, ItemRarity.Legendary, ItemRarity.Mythic);
            ancestralSlam.SelectionWeight = 1;
            ancestralSlam.Register();

            var glancingBlows = new MagicItemEffectDefinition("GlancingBlows", "Glancing Blows", "Increases your block value by 50%. Take addtional damage equal to 50% of all blocked or parried hits. This additional damage is only mitigate by player armor and is not reduced from player resistances.");
            //glancingBlows.Requirements.AllowedItemTypes.Add("Bucklers", "RoundShields", "TowerShields", "OneHandedWeapon", "TwoHandedWeapon"); no one seems to be using so letting it roll on anything
            glancingBlows.Requirements.ItemHasBlockPower = true;
            glancingBlows.Requirements.AllowedRarities.Add(ItemRarity.Magic, ItemRarity.Rare, ItemRarity.Epic, ItemRarity.Legendary, ItemRarity.Mythic);
            glancingBlows.ValuesPerRarity.Magic.Set(1, 1, 1);
            glancingBlows.ValuesPerRarity.Rare = new ValueDef(1, 1, 1);
            glancingBlows.ValuesPerRarity.Epic = new ValueDef(1, 1, 1);
            glancingBlows.ValuesPerRarity.Legendary = new ValueDef(1, 1, 1);
            glancingBlows.ValuesPerRarity.Mythic = new ValueDef(1, 1, 1);
            glancingBlows.SelectionWeight = 3;
            glancingBlows.Register();

            var retaliation = new MagicItemEffectDefinition("Retaliation", "Retaliation +{0}", "On block gain a stack of Relatliation. On Attack consumes a stack of Relataliation to increase the attack speed of your next attack string. All stacks of Retaliation expire after 5 seconds. Consuming or gaining a stack of Retaliation resets the expiration timer.");
            //retaliation.Requirements.AllowedItemTypes.Add("Bucklers", "RoundShields", "TowerShields", "OneHandedWeapon", "TwoHandedWeapon"); just let them have it this server
            retaliation.Requirements.ItemHasBlockPower = true;
            retaliation.Requirements.AllowedRarities.Add(ItemRarity.Magic, ItemRarity.Rare, ItemRarity.Epic, ItemRarity.Legendary, ItemRarity.Mythic);
            retaliation.ValuesPerRarity.Magic.Set(1, 1, 1);
            retaliation.ValuesPerRarity.Rare = new ValueDef(2, 2, 1);
            retaliation.ValuesPerRarity.Epic = new ValueDef(3, 3, 1);
            retaliation.ValuesPerRarity.Legendary = new ValueDef(4, 4, 1);
            retaliation.ValuesPerRarity.Mythic = new ValueDef(5, 5, 1);
            retaliation.SelectionWeight = 5;
            retaliation.Register();

            var infusion = new MagicItemEffectDefinition("Infusion", "Infusion +{0}%", "Cleanses certain status effects and adds <b><color=yellow>X</color></b>% of weapon damage as their respective type. Certain cleansed status effects grant addittional bonuses.");
            infusion.Requirements.AllowedItemTypes.Add("OneHandedWeapon", "TwoHandedWeapon");
            infusion.Requirements.AllowedSkillTypes.Add(Skills.SkillType.Clubs, Skills.SkillType.Swords, Skills.SkillType.Polearms, Skills.SkillType.Axes, Skills.SkillType.Knives, Skills.SkillType.Unarmed, Skills.SkillType.Spears, Skills.SkillType.Pickaxes);
            infusion.Requirements.AllowedRarities.Add(ItemRarity.Magic, ItemRarity.Rare, ItemRarity.Epic, ItemRarity.Legendary, ItemRarity.Mythic);
            infusion.ValuesPerRarity.Magic.Set(2, 4, 1);
            infusion.ValuesPerRarity.Rare = new ValueDef(3, 6, 1);
            infusion.ValuesPerRarity.Epic = new ValueDef(4, 8, 1);
            infusion.ValuesPerRarity.Legendary = new ValueDef(5, 10, 1);
            infusion.ValuesPerRarity.Mythic = new ValueDef(6, 12, 1);
            infusion.SelectionWeight = 2;
            infusion.Ability = "Infusion";
            AbilityProxyDefinition infusionProxy = new AbilityProxyDefinition("Infusion", AbilityActivationMode.Activated, typeof(InfusionAbilityProxy));
            infusionProxy.Ability.Cooldown = 120f;
            PrefabManager.OnPrefabsRegistered += () => EpicLootAPI.EpicLoot.RegisterAsset("InfusionIcon", ObjectDB.instance.GetStatusEffect("SetEffect_MageArmor".GetStableHashCode())?.m_icon);
            infusionProxy.Ability.IconAsset = "InfusionIcon";
            infusion.Register();

            var whirlwind = new MagicItemEffectDefinition("Whirlwind", "Whirlwind", "Spin");
            whirlwind.Requirements.AllowedItemTypes.Add("TwoHandedWeapon");
            whirlwind.Requirements.AllowedSkillTypes.Add(Skills.SkillType.Polearms);
            whirlwind.Requirements.AllowedRarities.Add(ItemRarity.Epic, ItemRarity.Legendary, ItemRarity.Mythic);
            whirlwind.SelectionWeight = 1;
            whirlwind.Register();

            var onslaught = new MagicItemEffectDefinition($"Onslaught", "Onslaught +{0}%", "Grants Onslaught on last hit of an attack string and when hitting a staggered enemy. While in last hit of attack string during Onslaught you continue attacking using the last hit animation of your attack string. Each consecutive Onslaught hit deals {0}% increased damage per hit. Onslaught expires if an enemy has not been struck with an attack in last hit animation for 2 seconds.");
            onslaught.Requirements.AllowedItemTypes.Add("OneHandedWeapon", "TwoHandedWeapon");
            onslaught.Requirements.AllowedSkillTypes.Add(Skills.SkillType.Clubs);
            onslaught.Requirements.AllowedRarities.Add(ItemRarity.Magic, ItemRarity.Rare, ItemRarity.Epic, ItemRarity.Legendary, ItemRarity.Mythic);
            onslaught.ValuesPerRarity.Magic.Set(5, 8, 1);
            onslaught.ValuesPerRarity.Rare = new ValueDef(6, 10, 1);
            onslaught.ValuesPerRarity.Epic = new ValueDef(7, 12, 1);
            onslaught.ValuesPerRarity.Legendary = new ValueDef(8, 14, 1);
            onslaught.ValuesPerRarity.Mythic = new ValueDef(9, 16, 1);
            onslaught.SelectionWeight = 5;
            onslaught.Register();

            var perfectStrike = new MagicItemEffectDefinition($"PerfectStrike", "Perfect Strike +{0}%", "Allows Perfect Strikes to be performed. When attack is repeated with perfect timing your next strike will deal {0}% increased damage.");
            perfectStrike.Requirements.AllowedItemTypes.Add("TwoHandedWeapon");
            perfectStrike.Requirements.AllowedSkillTypes.Add(Skills.SkillType.Axes);
            perfectStrike.Requirements.AllowedRarities.Add(ItemRarity.Magic, ItemRarity.Rare, ItemRarity.Epic, ItemRarity.Legendary, ItemRarity.Mythic);
            perfectStrike.ValuesPerRarity.Magic.Set(30, 50, 2);
            perfectStrike.ValuesPerRarity.Rare = new ValueDef(40, 60, 2);
            perfectStrike.ValuesPerRarity.Epic = new ValueDef(50, 70, 2);
            perfectStrike.ValuesPerRarity.Legendary = new ValueDef(50, 80, 2);
            perfectStrike.ValuesPerRarity.Mythic = new ValueDef(50, 100, 1);
            perfectStrike.SelectionWeight = 5;
            perfectStrike.Register();
        }
    }
}

