//using EpicLoot;
using EpicLootAPI;
using EpicLootLeslieAlphaTest.src.Utilities;
using HarmonyLib;
using Jotunn;
using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;

namespace EpicLootLeslieAlphaTest.src.MagicEffectss
{
    public static class PerfectStrike
    {
        private static float _attackStartTime;
        private static float _clickSpam = .25f;
        private static float _nowClicked;
        private static bool _clicked = false;
        private static bool _windowSFXplayed = false;
        private static int _lastPerfectChain = 0;
        private static bool isPerfectStrike = false;
        private static bool _perfectDamagePending = false;
        private static Attack _currentAttack;
        private static string _currentAnim;
        private static int _currentChain;
        private static float _currentAnimSpeed;
        private static GameObject _cleanVFXPrefab;
        private static GameObject _windowVFXInstance;

        private static readonly Dictionary<(string, int, string), float> _attackDeltas = new()
        {
            // battleaxe
            { ("battleaxe_attack", 0, "default"), 0.878f },  { ("battleaxe_attack", 0, "fast"), 0.599f },
            { ("battleaxe_attack", 1, "default"), 1.040f },  { ("battleaxe_attack", 1, "fast"), 0.761f },
            { ("battleaxe_attack", 2, "default"), 1.304f },  { ("battleaxe_attack", 2, "fast"), 1.078f },
            { ("battleaxe_secondary", 0, "default"), 0.843f },

            // greatsword
            { ("greatsword", 0, "default"), 1.642f },  { ("greatsword", 0, "fast"), 1.258f },
            { ("greatsword", 1, "default"), 0.839f },  { ("greatsword", 1, "fast"), 0.579f },
            { ("greatsword", 2, "default"), 0.958f },  { ("greatsword", 2, "fast"), 0.682f },
            { ("greatsword_secondary", 0, "default"), 2.125f },  { ("greatsword_secondary", 0, "fast"), 1.703f },
        };

        [HarmonyPatch(typeof(Attack), nameof(Attack.Start))]
        public static class Attack_PerfectStrikeAnimation_Patch
        {
            public static void Postfix(Attack __instance, Humanoid character)
            {
                if (character != Player.m_localPlayer || !Player.m_localPlayer.HasActiveMagicEffect("PerfectStrike", out float _)) return;

                if (isPerfectStrike)
                {
                    int target = (_lastPerfectChain == 2) ? 1 : 2;
                    __instance.m_currentAttackCainLevel = 2;
                    __instance.m_zanim.SetTrigger(__instance.m_attackAnimation + target);
                    _lastPerfectChain = target;
                    _clicked = false;
                    isPerfectStrike = false;
                    _perfectDamagePending = true;
                    //Jotunn.Logger.LogInfo($"[ATT.Post] anim={__instance.m_attackAnimation + target} chainLvl={__instance.m_currentAttackCainLevel}");
                }

                // Cache attack info for FixedUpdate window checks
                _currentAttack = __instance;
                _currentAnim = __instance.m_attackAnimation;
                _currentChain = __instance.m_currentAttackCainLevel;
                _currentAnimSpeed = __instance.m_speedFactor;
                _attackStartTime = Time.time;
            }
        }

        [HarmonyPatch(typeof(PlayerController), nameof(PlayerController.FixedUpdate))]
        public static class Input_AntiSpam_Patch
        {
            public static void Postfix(PlayerController __instance)
            {
                if (__instance.m_character != Player.m_localPlayer || !Player.m_localPlayer.HasActiveMagicEffect("PerfectStrike", out float _)) return;

                if (!Player.m_localPlayer.InAttack())
                {
                    _clicked = false;
                    _windowSFXplayed = false;
                    if (_windowVFXInstance != null)
                    {
                        Object.Destroy(_windowVFXInstance);
                        _windowVFXInstance = null;
                    }
                    return;
                }

                // Lookup the window for the current attack
                float delta = 0f;
                float windowStart = 0f;
                float windowEnd = 0f;
                bool hasWindow = _currentAnim != null && _attackDeltas.TryGetValue((_currentAnim, _currentChain, "default"), out delta);

                if (hasWindow)
                {
                    windowStart = delta * 0.5f;
                    windowEnd = delta * 0.75f;
                }

                float elapsed = Time.time - _attackStartTime;

                // VFX — show during window, destroy when out (skip primary chain 0)
                bool skipFeedback = _currentChain == 0 && _currentAnim != null && !_currentAnim.Contains("secondary");
                if (hasWindow && !skipFeedback)
                {
                    bool inWindow = elapsed >= windowStart && elapsed <= windowEnd;

                    if (inWindow && _windowVFXInstance == null)
                    {
                        if (_cleanVFXPrefab == null)
                        {
                            var original = ZNetScene.instance.GetPrefab("vfx_kiln_addore");
                            if (original != null)
                            {
                                bool wasActive = original.activeSelf;
                                original.SetActive(false);
                                _cleanVFXPrefab = Object.Instantiate(original);
                                original.SetActive(wasActive);
                                Object.DestroyImmediate(_cleanVFXPrefab.GetComponent<ZNetView>());
                            }
                        }
                        if (_cleanVFXPrefab != null)
                        {
                            var weaponGO = Player.m_localPlayer.m_visEquipment.m_rightItemInstance;
                            Transform parent = weaponGO != null ? weaponGO.transform : Player.m_localPlayer.m_visEquipment.m_rightHand;
                            _windowVFXInstance = Object.Instantiate(_cleanVFXPrefab, parent);
                            _windowVFXInstance.SetActive(true);
                            _windowVFXInstance.transform.localPosition = Vector3.zero;
                        }
                    }
                    else if (!inWindow && _windowVFXInstance != null)
                    {
                        Object.Destroy(_windowVFXInstance);
                        _windowVFXInstance = null;
                    }
                }

                // Click detection — check if click lands in the window
                if (ZInput.GetButtonDown("Attack"))
                {
                    _nowClicked = Time.time;

                    if (!_clicked)
                    {
                        _clicked = true;
                        if (hasWindow)
                        {
                            isPerfectStrike = elapsed >= windowStart && elapsed <= windowEnd;
                            //Jotunn.Logger.LogInfo($"[Click] elapsed={elapsed:F3} window={windowStart:F3}-{windowEnd:F3} isPerfect={isPerfectStrike} anim={_currentAnim} chain={_currentChain}");
                        }
                    }
                    else if (!isPerfectStrike)
                    {
                        // Second click only cancels if it was a miss (clicked too early)
                        // If already perfect, preserve it for Attack.Start to consume
                    }
                }

                // SFX on perfect strike — one-shot (skip primary chain 0)
                if (isPerfectStrike && !_windowSFXplayed && !skipFeedback)
                {
                    var sfx = ZNetScene.instance.GetPrefab("sfx_perfectblock");
                    if (sfx != null)
                    {
                        var instance = Object.Instantiate(sfx, Player.m_localPlayer.transform);
                        instance.transform.localPosition = Vector3.zero;
                        _windowSFXplayed = true;
                    }
                }

                // Anti-spam reset
                if (Time.time - _nowClicked >= _clickSpam)
                {
                    _clicked = false;
                }
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        public static class PerfectStrike_Damage_Patch
        {
            public static void Postfix(Character __instance, HitData hit)
            {
                if (_perfectDamagePending)
                {
                    hit.m_damage.Modify(Player.m_localPlayer.GetTotalActiveMagicEffectValue("PerfectStrike", .01f));
                    //Jotunn.Logger.LogWarning($"[PF]Damage patch fired damage = {hit.m_damage}");
                    _perfectDamagePending = false;
                    _windowSFXplayed = false;
                }
            }
        }
    }
}
