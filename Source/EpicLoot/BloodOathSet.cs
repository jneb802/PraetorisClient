using System;
using EpicLootAPI;

namespace PraetorisClient.EpicLootFeature
{
    internal static class BloodOathSet
    {
        private const string SetId = "PraetorisBloodOath";
        private const string ChestId = "PraetorisBloodOathChest";
        private const string LegsId = "PraetorisBloodOathLegs";

        internal static void Initialize()
        {
            RegisterPiece(ChestId, "Blood Oath Vestments", ItemDrop.ItemData.ItemType.Chest);
            RegisterPiece(LegsId, "Blood Oath Leggings", ItemDrop.ItemData.ItemType.Legs);

            LegendarySetInfo set = new LegendarySetInfo(LegendaryType.Legendary, SetId, "Blood Oath");
            set.LegendaryIDs.Add(ChestId);
            set.LegendaryIDs.Add(LegsId);
            set.SetBonuses.Add(new SetBonusInfo(2, PraetorisMagicEffects.BloodOath, 1, 1, 1));
            if (!set.Register())
            {
                throw new InvalidOperationException("Could not register the Blood Oath legendary set.");
            }
        }

        private static void RegisterPiece(string id, string name, ItemDrop.ItemData.ItemType itemType)
        {
            LegendaryInfo piece = new LegendaryInfo(LegendaryType.Legendary, id, name,
                "A vow written in blood. Equip both Blood Oath pieces to reduce health to 1 and stop natural health regeneration.")
            {
                IsSetItem = true
            };
            piece.Requirements.AddAllowedItemTypes(itemType);
            if (!piece.Register())
            {
                throw new InvalidOperationException($"Could not register legendary item {id}.");
            }
        }
    }
}
