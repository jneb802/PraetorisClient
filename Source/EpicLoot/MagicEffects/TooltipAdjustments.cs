using System.Globalization;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;

namespace PraetorisClient
{
    internal static partial class PraetorisMagicEffects
    {
        private static readonly Regex DurationLineRegex = new Regex(
            @"(?<prefix>\$(?:se_ttl:|se_shield_ttl)\s*<color=(?<color>[^>]+)>)(?<value>[^<]+)(?<suffix></color>)",
            RegexOptions.Compiled);

        private static readonly Regex MaxAdrenalineLineRegex = new Regex(
            @"(?<prefix>\$item_maxadrenaline:\s*<color=(?<color>[^>]+)>)(?<value>[^<]+)(?<suffix></color>)",
            RegexOptions.Compiled);

        private static readonly Regex RarityLineColorRegex = new Regex(
            @"\$mod_epicloot_itemtooltip_rarity:\s*<color=(?<color>[^>]+)>",
            RegexOptions.Compiled);

        private static readonly Regex ValueColorRegex = new Regex(
            @"<color=[^>]+>$",
            RegexOptions.Compiled);

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip), typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int))]
        [HarmonyAfter(new[] { EpicLootApiBridge.PluginGuid })]
        private static class PraetorisMagicEffects_ItemData_GetTooltip_Patch
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(ItemDrop.ItemData item, ref string __result)
            {
                if (item == null || string.IsNullOrEmpty(__result))
                {
                    return;
                }

                string magicColor = GetMagicColor(__result);
                __result = ApplyIncreaseEffectDurationTooltip(item, __result, magicColor);
                __result = ApplyModifyAdrenalineCostTooltip(item, __result, magicColor);
            }
        }

        private static string ApplyIncreaseEffectDurationTooltip(ItemDrop.ItemData item, string tooltip, string magicColor)
        {
            float durationIncrease = GetItemEffectValue(item, IncreaseEffectDuration, PercentScale);
            if (durationIncrease <= 0f)
            {
                return tooltip;
            }

            StatusEffect? itemStatusEffect = GetPrimaryItemStatusEffect(item);
            tooltip = ReplaceDurationLine(tooltip, itemStatusEffect, durationIncrease, magicColor);
            tooltip = ReplaceDurationLine(
                tooltip,
                item.m_shared.m_fullAdrenalineSE,
                durationIncrease,
                magicColor,
                requireSupportedStatusEffect: true);
            return tooltip;
        }

        private static string ApplyModifyAdrenalineCostTooltip(ItemDrop.ItemData item, string tooltip, string magicColor)
        {
            if (item.m_shared.m_maxAdrenaline <= 0f)
            {
                return tooltip;
            }

            float reduction = GetItemEffectValue(item, ModifyAdrenalineCost, PercentScale);
            if (reduction <= 0f)
            {
                return tooltip;
            }

            float baseAdrenaline = item.m_shared.m_maxAdrenaline;
            float modifiedAdrenaline = Mathf.Max(1f, baseAdrenaline * Mathf.Clamp(1f - reduction, 0.5f, 1f));
            return ReplaceFirstValueLine(tooltip, MaxAdrenalineLineRegex, baseAdrenaline, modifiedAdrenaline, magicColor);
        }

        private static StatusEffect? GetPrimaryItemStatusEffect(ItemDrop.ItemData item)
        {
            if (item.m_shared.m_attackStatusEffect != null)
            {
                return item.m_shared.m_attackStatusEffect;
            }

            if (item.m_shared.m_consumeStatusEffect != null)
            {
                return item.m_shared.m_consumeStatusEffect;
            }

            if (item.m_shared.m_equipStatusEffect != null)
            {
                return item.m_shared.m_equipStatusEffect;
            }

            return null;
        }

        private static string ReplaceDurationLine(
            string tooltip,
            StatusEffect? statusEffect,
            float durationIncrease,
            string magicColor,
            bool requireSupportedStatusEffect = false)
        {
            if (statusEffect == null ||
                statusEffect.m_ttl <= 1f ||
                (requireSupportedStatusEffect && !IncreaseEffectDurationRuntime.IsSupportedStatusEffect(statusEffect)))
            {
                return tooltip;
            }

            float baseDuration = statusEffect.m_ttl;
            float modifiedDuration = baseDuration * (1f + durationIncrease);
            return ReplaceFirstValueLine(tooltip, DurationLineRegex, baseDuration, modifiedDuration, magicColor);
        }

        private static string ReplaceFirstValueLine(
            string tooltip,
            Regex regex,
            float baseValue,
            float modifiedValue,
            string magicColor)
        {
            bool replaced = false;
            return regex.Replace(tooltip, match =>
            {
                if (replaced || !MatchesTooltipNumber(match.Groups["value"].Value, baseValue))
                {
                    return match.Value;
                }

                replaced = true;
                string color = match.Groups["color"].Value;
                return BuildValuePrefix(match.Groups["prefix"].Value, magicColor) +
                       FormatTooltipNumber(modifiedValue) +
                       match.Groups["suffix"].Value +
                       " (<color=" + color + ">" + FormatTooltipNumber(baseValue) + "</color>)";
            });
        }

        private static string BuildValuePrefix(string prefix, string magicColor)
        {
            return ValueColorRegex.Replace(prefix, "<color=" + magicColor + ">");
        }

        private static float GetItemEffectValue(ItemDrop.ItemData item, string effectType, float scale)
        {
            return EpicLootApiBridge.GetTotalActiveMagicEffectValueForWeapon(null, item, effectType, scale);
        }

        private static bool MatchesTooltipNumber(string value, float expected)
        {
            if (!TryParseTooltipNumber(value, out float actual))
            {
                return false;
            }

            return Mathf.Abs(actual - expected) <= 0.05f;
        }

        private static bool TryParseTooltipNumber(string value, out float result)
        {
            string trimmed = value.Trim();
            return float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ||
                   float.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out result);
        }

        private static string FormatTooltipNumber(float value)
        {
            return value.ToString("0.#", CultureInfo.InvariantCulture);
        }

        private static string GetMagicColor(string tooltip)
        {
            Match match = RarityLineColorRegex.Match(tooltip);
            return match.Success ? match.Groups["color"].Value : "orange";
        }
    }
}
