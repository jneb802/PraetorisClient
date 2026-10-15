using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace PraetorisClient
{
    internal static class CrossbowEffectsRuntime
    {
        private const float WindowStart = 0.50f;
        private const float WindowEnd = 0.65f;
        private sealed class ReloadState
        {
            internal Player.MinorActionData? Action;
            internal bool Attempted;
            internal ItemDrop.ItemData? BonusWeapon;
            internal float BonusDamage;
        }
        private static readonly ConditionalWeakTable<Player, ReloadState> States = new();

        internal static bool IsCrossbow(ItemDrop.ItemData? weapon) =>
            weapon?.m_shared.m_skillType == Skills.SkillType.Crossbows && weapon.m_shared.m_attack.m_requiresReload;

        internal static float Value(Player player, ItemDrop.ItemData weapon, string effect) =>
            IsCrossbow(weapon) ? EpicLootApiBridge.GetTotalActiveMagicEffectValueForWeapon(player, weapon, effect) : 0f;

        internal static int ExtraCapacity(Player player, ItemDrop.ItemData weapon) =>
            Mathf.Clamp(Mathf.FloorToInt(Value(player, weapon, CrossbowEffectDefinitions.Capacity)), 0, 3);

        private static Player.MinorActionData? CurrentReload(Player player)
        {
            if (player.IsDead() || player.InAttack() || player.m_actionQueue.Count == 0) return null;
            Player.MinorActionData action = player.m_actionQueue[0];
            return action.m_type == Player.MinorActionData.ActionType.Reload &&
                action.m_item == player.GetCurrentWeapon() && IsCrossbow(action.m_item) ? action : null;
        }

        private static ReloadState State(Player player, Player.MinorActionData? action)
        {
            ReloadState state = States.GetOrCreateValue(player);
            if (state.Action != action)
            {
                state.Action = action;
                state.Attempted = false;
            }
            return state;
        }

        internal static Dictionary<string, Delegate> CreateAbilityCallbacks() => new()
        {
            ["TryActivate"] = new Action(TryActiveReload)
        };

        private static void TryActiveReload()
        {
            Player player = Player.m_localPlayer;
            if (player == null) return;
            Player.MinorActionData? action = CurrentReload(player);
            if (action == null || action.m_duration <= 0f) return;
            float bonus = Value(player, action.m_item, CrossbowEffectDefinitions.ActiveReload);
            if (bonus <= 0f) return;
            ReloadState state = State(player, action);
            if (state.Attempted) return;
            state.Attempted = true;
            float progress = action.m_time / action.m_duration;
            if (progress < WindowStart || progress > WindowEnd)
            {
                player.Message(MessageHud.MessageType.Center, "Active reload missed");
                return;
            }

            // Complete this exact reload through the game's normal action-queue path.
            // This preserves its animation, loaded-state callbacks, and other queued actions.
            action.m_time = action.m_duration + 0.001f;
            state.BonusWeapon = action.m_item;
            state.BonusDamage = bonus;
            player.Message(MessageHud.MessageType.Center, $"Active reload! Next shot +{bonus:0}% damage");
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetWeaponLoadingTime))]
        private static class ReloadDurationPatch
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(ItemDrop.ItemData __instance, ref float __result)
            {
                Player player = Player.m_localPlayer;
                if (player != null) __result *= 1 + ExtraCapacity(player, __instance);
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.ResetLoadedWeapon))]
        private static class ClearBonusPatch
        {
            private static void Postfix(Player __instance)
            {
                if (!States.TryGetValue(__instance, out ReloadState state)) return;
                state.BonusWeapon = null;
                state.BonusDamage = 0f;
            }
        }

        [HarmonyPatch(typeof(Attack), "ProjectileAttackTriggered")]
        private static class ShotPatch
        {
            private static void Prefix(Attack __instance)
            {
                if (__instance.m_character is not Player player || player != Player.m_localPlayer ||
                    !IsCrossbow(__instance.m_weapon)) return;
                float recoil = Value(player, __instance.m_weapon, CrossbowEffectDefinitions.Recoil);
                if (recoil > 0f)
                {
                    // Native pushback decays rapidly. Use the weapon's force as the dash base,
                    // with a minimum for crossbows whose normal recoil is zero.
                    float force = Mathf.Max(20f, __instance.m_recoilPushback) * recoil * 0.01f;
                    player.ApplyPushback(-player.transform.forward, force);
                }
                if (States.TryGetValue(player, out ReloadState state) && state.BonusWeapon == __instance.m_weapon)
                {
                    float bonus = Value(player, __instance.m_weapon, CrossbowEffectDefinitions.ActiveReload);
                    if (bonus > 0f) __instance.m_damageMultiplier *= 1f + Mathf.Min(bonus, state.BonusDamage) * 0.01f;
                    state.BonusWeapon = null;
                    state.BonusDamage = 0f;
                }
            }
        }

        [HarmonyPatch(typeof(EpicLoot.Abilities.AbilityController), "GetAvailableAbilities")]
        private static class AbilityOrderPatch
        {
            private static void Postfix(List<EpicLoot.Abilities.AbilityDefinition> __result)
            {
                int index = __result.FindIndex(ability => ability.ID == CrossbowEffectDefinitions.ActiveReload);
                if (index <= 0) return;
                EpicLoot.Abilities.AbilityDefinition ability = __result[index];
                __result.RemoveAt(index);
                __result.Insert(0, ability);
            }
        }

        [HarmonyPatch(typeof(EpicLoot.Abilities.AbilityController), "UpdatePlayerAbilities")]
        private static class CurrentAbilityOrderPatch
        {
            private static void Postfix(List<EpicLoot.Abilities.Ability> ____currentAbilities)
            {
                int index = ____currentAbilities.FindIndex(ability => ability.AbilityDef.ID == CrossbowEffectDefinitions.ActiveReload);
                if (index <= 0) return;
                EpicLoot.Abilities.Ability ability = ____currentAbilities[index];
                ____currentAbilities.RemoveAt(index);
                ____currentAbilities.Insert(0, ability);
            }
        }

        [HarmonyPatch(typeof(Player), "Awake")]
        private static class PlayerAwakePatch
        {
            private static void Postfix(Player __instance) => __instance.gameObject.AddComponent<ActiveReloadDisplay>();
        }

        private sealed class ActiveReloadDisplay : MonoBehaviour
        {
            private GUIStyle? _label;
            private void OnGUI()
            {
                Player player = Player.m_localPlayer;
                if (player == null || player.gameObject != gameObject || Hud.IsUserHidden()) return;
                Player.MinorActionData? action = CurrentReload(player);
                if (action == null || Value(player, action.m_item, CrossbowEffectDefinitions.ActiveReload) <= 0f) return;
                ReloadState state = State(player, action);
                float width = 320f;
                Rect bar = new Rect((Screen.width - width) / 2f, Screen.height * 0.68f, width, 14f);
                Color saved = GUI.color;
                GUI.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
                GUI.DrawTexture(bar, Texture2D.whiteTexture);
                GUI.color = state.Attempted ? Color.gray : Color.green;
                GUI.DrawTexture(new Rect(bar.x + width * WindowStart, bar.y, width * (WindowEnd - WindowStart), bar.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(bar.x + width * Mathf.Clamp01(action.m_time / action.m_duration), bar.y - 3, 3, bar.height + 6), Texture2D.whiteTexture);
                _label ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 18, fontStyle = FontStyle.Bold };
                string key = EpicLoot.Abilities.AbilityController.GetBindingKeycode(0);
                GUI.Label(new Rect(bar.x - 100, bar.y - 30, width + 200, 28),
                    state.Attempted ? "Active reload: attempt used" : $"Active reload: [{key}] in the green window", _label);
                GUI.color = saved;
            }
        }
    }
}
