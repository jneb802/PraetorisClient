using EpicLoot_UnityLib;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine.UI;
using UnityEngine;

namespace EpicLootLeslieAlphaTest.src
{
    [HarmonyPatch(typeof(EnchantingTableUIPanelBase), "OnMainButtonClicked")]
    internal class RuneUIMod_OnMainButtonClicked_Patch
    {
        
        static bool Prefix(EnchantingTableUIPanelBase __instance)
        {
            if (__instance is not RuneUI runeUI) return true;

            FieldInfo inProgressField = AccessTools.Field(typeof(EnchantingTableUIPanelBase), "_inProgress");
            bool inProgress = (bool)(inProgressField.GetValue(__instance));
            if (inProgress) return true;

            FieldInfo runeActionField = AccessTools.Field(typeof(RuneUI), "_runeAction");
            int runeAction = (int)(runeActionField.GetValue(__instance));
            if (runeAction != 1) return true;

            if (EnchantingHelper.AwaitingConfirmation)
            {
                EnchantingHelper.AwaitingConfirmation = false;
                EnchantingHelper.PendingDestroyChance = 0f;
                return true;
            }

            MultiSelectItemList availableItems = (MultiSelectItemList)AccessTools.Field(typeof(EnchantingTableUIPanelBase), "AvailableItems").GetValue(__instance);
            var selectedItem = availableItems.GetSingleSelectedItem<InventoryItemListElement>();
            if (selectedItem?.Item1.GetItem() == null) return true;

            MultiSelectItemList availableRunes = (MultiSelectItemList)AccessTools.Field(typeof(RuneUI), "AvailableRunes").GetValue(__instance); 
            var selectedRune = availableRunes.GetSingleSelectedItem<InventoryItemListElement>();
            if (selectedRune?.Item1.GetItem() == null) return true;

            float destroyChance = EnchantingHelper.RuneTooPowerfulEtchDestructionChance(selectedItem.Item1.GetItem(), selectedRune.Item1.GetItem());

            if (destroyChance > 0f)
            {
                EnchantingHelper.AwaitingConfirmation = true;
                EnchantingHelper.PendingDestroyChance = destroyChance;

                Text warning = (Text)AccessTools.Field(typeof(RuneUI), "Warning").GetValue(__instance);
                warning.text = $"WARNING: {destroyChance:0}% chance to DESTROY your item!! Click Etch again to confirm.";
                warning.color = UnityEngine.Color.red;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(RuneUI), "DoMainAction")]
    internal class RuneUI_DoMainACtion_Patch
    {
        static bool Prefix(RuneUI __instance)
        {
            EnchantingHelper.AwaitingConfirmation = false;

            FieldInfo runeActioneField = AccessTools.Field(typeof(RuneUI), "_runeAction");
            int runeAction = (int)runeActioneField.GetValue(__instance);
            if (runeAction != 1) return true;

            MultiSelectItemList availableItems = (MultiSelectItemList)AccessTools.Field(typeof(EnchantingTableUIPanelBase), "AvailableItems").GetValue(__instance);
            var selectedItem = availableItems.GetSelectedItems<InventoryItemListElement>().FirstOrDefault();

            __instance.Cancel();

            if (selectedItem?.Item1.GetItem() == null) return false;

            MultiSelectItemList availableRunes = (MultiSelectItemList)AccessTools.Field(typeof(RuneUI), "AvailableRunes").GetValue(__instance);
            ItemDrop.ItemData rune = availableRunes.GetSingleSelectedItem<InventoryItemListElement>().Item1.GetItem();
            ItemDrop.ItemData itemToEtch = selectedItem.Item1.GetItem();
            int enchantIndex = (int)AccessTools.Field(typeof(RuneUI), "_selectedEnchantmentIndex").GetValue(__instance);

            FieldInfo successDialogField = AccessTools.Field(typeof(RuneUI), "_successDialog");
            GameObject existingDialog = (GameObject)successDialogField.GetValue(__instance);
            if (existingDialog != null) UnityEngine.Object.Destroy(existingDialog);
            
            GameObject result = RuneUI.RuneEnchancedItem(itemToEtch, rune, enchantIndex);

            if (result != null)
            {
                successDialogField.SetValue(__instance, result);
                result.SetActive(true);
                InventoryManagement.Instance.RemoveExactItem(rune, 1);
            }
            else
            {
                successDialogField.SetValue(__instance, null);
                AccessTools.Method(typeof(RuneUI), "Unlock").Invoke(__instance, null);
            }

            MultiSelectItemList costList = (MultiSelectItemList)AccessTools.Field(typeof(RuneUI), "CostList").GetValue(__instance);
            costList.SetItems(new List<IListElement>());
            __instance.DeselectAll();
            AccessTools.Method(typeof(RuneUI), "RefreshAvailableItems").Invoke(__instance, null);
            AccessTools.Field(typeof(RuneUI), "_selectedEnchantmentIndex").SetValue(__instance, -1);
            costList.SetItems(new List<IListElement>());
            availableRunes.SetItems(new List<IListElement>());

            return false;
        }
    }

    [HarmonyPatch(typeof(RuneUI), "Lock")]
    internal class RuneUI_Lock_Patch
    {
        static void Postfix(RuneUI __instance)
        {
            var mainButton = (Button)AccessTools.Field(typeof(EnchantingTableUIPanelBase), "MainButton").GetValue(__instance);
            if (mainButton != null)
                mainButton.interactable = true;
        }
    }
}
