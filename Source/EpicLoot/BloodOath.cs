using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using EpicLoot;
using HarmonyLib;

namespace PraetorisClient.EpicLootFeature
{
    internal static class BloodOath
    {
        // Persist activation with the character so healing is not lost on reconnect.
        private const string ActiveKey = "PraetorisClient.BloodOath.Active";
        private static Player? loadingPlayer;

        private static bool HasEffect(Player player)
        {
            ItemDrop.ItemData? chest = player.m_chestItem;
            return chest != null && chest.m_equipped && chest.IsMagic(out MagicItem magicItem) &&
                magicItem.HasEffect(PraetorisMagicEffects.BloodOath, includeSocketed: true);
        }

        private static bool Synchronize(Player player)
        {
            if (player != Player.m_localPlayer || player == loadingPlayer || player.m_nview == null ||
                player.m_nview.GetZDO() == null || !player.m_nview.IsOwner())
            {
                return false;
            }

            if (player.IsDead() || player.GetHealth() <= 0f)
            {
                player.m_customData.Remove(ActiveKey);
                return false;
            }

            bool active = HasEffect(player);
            if (active)
            {
                if (!player.m_customData.ContainsKey(ActiveKey))
                {
                    // Do not treat this sacrifice as incoming damage or a blood-magic casting cost.
                    // Never raise a living player's health if it is already below one.
                    if (player.GetHealth() > 1f)
                    {
                        player.SetHealth(1f);
                    }
                    player.m_customData[ActiveKey] = "1";
                }
            }
            else
            {
                player.m_customData.Remove(ActiveKey);
            }
            return active;
        }

        private static void HealFromFood(Character character, float amount, bool showText)
        {
            if (character is Player player && Synchronize(player))
            {
                return;
            }
            character.Heal(amount, showText);
        }

        // Polling also catches socket edits and config changes without an equipment change.
        [HarmonyPatch(typeof(Player), "FixedUpdate")]
        private static class UpdatePatch
        {
            private static void Prefix(Player __instance) => Synchronize(__instance);
        }

        // Observe removal/re-equipping immediately, including changes within a single frame.
        [HarmonyPatch(typeof(Humanoid), "SetupEquipment")]
        private static class EquipmentPatch
        {
            private static void Postfix(Humanoid __instance)
            {
                if (__instance is Player player)
                {
                    Synchronize(player);
                }
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Load))]
        private static class LoadPatch
        {
            private static void Prefix(Player __instance, out Player? __state)
            {
                __state = loadingPlayer;
                loadingPlayer = __instance;
            }

            private static void Finalizer(Player? __state)
            {
                // Equipment is restored in several steps inside Load. Do not clear a saved
                // activation marker while only part of the equipment has been restored.
                loadingPlayer = __state;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Save))]
        private static class SavePatch
        {
            private static void Prefix(Player __instance) => Synchronize(__instance);
        }

        [HarmonyPatch(typeof(Player), "UpdateFood")]
        private static class FoodRegenerationPatch
        {
            // Epic Loot adjusts the regeneration amount in this method. Replace only the final
            // food-healing call, after its transpiler, so flat and percentage regen are both blocked.
            // Character.Heal itself stays unchanged, including mead healing over time and life steal.
            [HarmonyAfter("randyknapp.mods.epicloot")]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> result = new List<CodeInstruction>(instructions);
                MethodInfo heal = AccessTools.Method(typeof(Character), nameof(Character.Heal), new[] { typeof(float), typeof(bool) });
                MethodInfo replacement = AccessTools.Method(typeof(BloodOath), nameof(HealFromFood));
                int replaced = 0;
                foreach (CodeInstruction instruction in result)
                {
                    if (instruction.Calls(heal))
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = replacement;
                        replaced++;
                    }
                }
                if (replaced != 1)
                {
                    throw new InvalidOperationException($"Blood Oath expected one food regeneration call, found {replaced}.");
                }
                return result;
            }
        }
    }
}
