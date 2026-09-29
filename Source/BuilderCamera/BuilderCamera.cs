using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PraetorisClient.BuilderCameraFeature
{
    internal static partial class BuilderCamera
    {
        private static BuilderWard? _ward;
        private static bool _active;
        private static Vector3 _position;
        private static Vector3 _bodyPosition;
        private static Quaternion _rotation;
        private static float _health;
        private static GUIStyle? _statusStyle;
        private static int CollisionMask => LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
        internal static bool Active => _active;
        internal static string Status() => $"Build camera: active={_active}, equipped={(Player.m_localPlayer && Equipped(Player.m_localPlayer))}, camera={_position}, body={(Player.m_localPlayer ? Player.m_localPlayer.transform.position.ToString() : "none")}, wardLimit={WardRange.Value}, bodyLimit={BodyRange.Value}";

        internal static void DrawStatus()
        {
            if (!_active || !_ward || Hud.IsUserHidden()) return;
            _statusStyle ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 16 };
            GUI.Box(new Rect(Screen.width / 2f - 240f, 45f, 480f, 58f),
                $"BUILD CAMERA\n{ToggleKey.Value}: exit  |  Space / Left Ctrl: up / down", _statusStyle);
        }

        private static bool UiOpen => Console.IsVisible() || Menu.IsVisible() || InventoryGui.IsVisible() || StoreGui.IsVisible()
            || Minimap.IsOpen() || (Chat.instance && Chat.instance.HasFocus()) || Hud.IsPieceSelectionVisible() || Hud.InRadial();

        private static bool Equipped(Player player)
        {
            ItemDrop.ItemData tool = player.RightItem;
            bool beltEquipped = player.GetInventory().GetAllItems().Exists(item => item.m_equipped && item.m_dropPrefab && item.m_dropPrefab.name == BeltPrefab);
            return beltEquipped
                && tool != null && tool.m_shared.m_buildPieces && player.InPlaceMode();
        }

        internal static void Update()
        {
            Player player = Player.m_localPlayer;
            if (!player || !GameCamera.instance) { Stop(null); return; }
            if (_active && (!Equipped(player) || player.IsDead() || player.IsTeleporting() || player.InCutscene()
                || player.IsAttached() || !_ward || !PrivateArea.CheckAccess(_ward.transform.position, flash: false)
                || Vector3.Distance(player.transform.position, _bodyPosition) > 0.5f || player.GetHealth() < _health))
            {
                Stop("Build camera ended: equipment, body position, health, or ward changed.");
                return;
            }
            _health = player.GetHealth();
            if (ToggleKey.Value.IsDown() && !UiOpen)
            {
                if (_active) { Stop("Build camera ended."); return; }
                Start(player);
            }
            if (!_active) return;
            if (!WithinLimits(_position))
            {
                Stop("Build camera ended: ward unavailable or distance limit reached.");
                return;
            }
        }

        private static void Start(Player player)
        {
            if (!Equipped(player) || player.IsDead() || player.IsTeleporting() || player.IsAttached() || player.InCutscene() || GameCamera.InFreeFly())
            {
                player.Message(MessageHud.MessageType.Center, "Equip a Builder Belt and hold a build tool to use the build camera.");
                return;
            }
            _ward = null;
            float nearest = WardRange.Value;
            foreach (BuilderWard ward in BuilderWard.Instances)
            {
                float distance = Vector3.Distance(ward.transform.position, player.transform.position);
                if (distance <= nearest && PrivateArea.CheckAccess(ward.transform.position, flash: false)) { _ward = ward; nearest = distance; }
            }
            if (!_ward) { player.Message(MessageHud.MessageType.Center, "A Builder's Ward is required nearby."); return; }
            _position = GameCamera.instance.transform.position;
            _rotation = GameCamera.instance.transform.rotation;
            _bodyPosition = player.transform.position;
            _health = player.GetHealth();
            if (!WithinLimits(_position) || Physics.CheckSphere(_position, 0.2f, CollisionMask, QueryTriggerInteraction.Ignore))
            {
                _ward = null;
                player.Message(MessageHud.MessageType.Center, "Move the camera into clear space within the ward's range.");
                return;
            }
            player.m_autoRun = false;
            player.SetMoveDir(Vector3.zero);
            _active = true;
            player.Message(MessageHud.MessageType.Center, "Build camera active. Your body remains vulnerable.");
        }

        internal static void Stop(string? reason)
        {
            bool wasRunning = _active;
            _active = false;
            _ward = null;
            if (wasRunning && reason != null && Player.m_localPlayer) Player.m_localPlayer.Message(MessageHud.MessageType.Center, reason);
        }

        private static bool WithinLimits(Vector3 point) => _ward && Player.m_localPlayer
            && Vector3.Distance(point, _ward.transform.position) <= WardRange.Value
            && Vector3.Distance(point, Player.m_localPlayer.transform.position) <= BodyRange.Value;

        private static void MoveCamera(GameCamera camera, float dt)
        {
            if (Physics.CheckSphere(_position, 0.18f, CollisionMask, QueryTriggerInteraction.Ignore)) { Stop("Build camera ended: camera space is blocked."); return; }
            if (!UiOpen)
            {
                Vector2 mouse = ZInput.GetMouseDelta();
                Vector3 angles = _rotation.eulerAngles;
                float pitch = angles.x > 180f ? angles.x - 360f : angles.x;
                _rotation = Quaternion.Euler(Mathf.Clamp(pitch - mouse.y * 1.5f, -85f, 85f), angles.y + mouse.x * 1.5f, 0f);
                Vector3 input = new Vector3((ZInput.GetKey(KeyCode.D) ? 1 : 0) - (ZInput.GetKey(KeyCode.A) ? 1 : 0),
                    (ZInput.GetKey(KeyCode.Space) ? 1 : 0) - (ZInput.GetKey(KeyCode.LeftControl) ? 1 : 0),
                    (ZInput.GetKey(KeyCode.W) ? 1 : 0) - (ZInput.GetKey(KeyCode.S) ? 1 : 0));
                Vector3 delta = (_rotation * new Vector3(input.x, 0f, input.z) + Vector3.up * input.y).normalized
                    * (ZInput.GetKey(KeyCode.LeftShift) ? 10f : 5f) * Mathf.Min(dt, 0.1f);
                if (delta.sqrMagnitude > 0f)
                {
                    if (Physics.SphereCast(_position, 0.2f, delta.normalized, out RaycastHit hit, delta.magnitude + 0.02f, CollisionMask, QueryTriggerInteraction.Ignore))
                        delta = delta.normalized * Mathf.Max(0f, hit.distance - 0.02f);
                    Vector3 candidate = _position + delta;
                    if (WithinLimits(candidate) && !Physics.CheckSphere(candidate, 0.18f, CollisionMask, QueryTriggerInteraction.Ignore)) _position = candidate;
                }
            }
            camera.transform.SetPositionAndRotation(_position, _rotation);
        }

        // Change only the distance checks used by building. Normal placement, cost, station,
        // protected-area, repair and removal checks still run on the real player.
        private static float BuildDistance(Vector3 first, Vector3 second)
        {
            if (!_active) return Vector3.Distance(first, second);
            if (!Equipped(Player.m_localPlayer)) return float.MaxValue;
            Vector3 eye = Player.m_localPlayer.m_eye.position;
            Vector3 target = (first - eye).sqrMagnitude < (second - eye).sqrMagnitude ? second : first;
            return WithinLimits(target) ? Vector3.Distance(_position, target) : float.MaxValue;
        }

        [HarmonyPatch(typeof(GameCamera), "UpdateCamera")]
        private static class CameraUpdate
        {
            private static bool Prefix(GameCamera __instance, float dt)
            {
                if (!_active) return true;
                MoveCamera(__instance, dt);
                return !_active;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
        private static class BodyControls
        {
            private static void Prefix(Player __instance, ref Vector3 movedir, ref bool attack, ref bool attackHold,
                ref bool secondaryAttack, ref bool secondaryAttackHold, ref bool block, ref bool blockHold,
                ref bool jump, ref bool crouch, ref bool run, ref bool autoRun, ref bool dodge)
            {
                if (!_active || __instance != Player.m_localPlayer) return;
                movedir = Vector3.zero;
                attack = attackHold = secondaryAttack = secondaryAttackHold = block = blockHold = jump = crouch = run = autoRun = dodge = false;
                __instance.m_autoRun = false;
                __instance.SetMoveDir(Vector3.zero);
            }
        }

        [HarmonyPatch]
        private static class BuildReach
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(Player), "PieceRayTest");
                yield return AccessTools.Method(typeof(Player), "RemovePiece");
                yield return AccessTools.Method(typeof(Player), "UpdateWearNTearHover");
            }
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                MethodInfo distance = AccessTools.Method(typeof(Vector3), nameof(Vector3.Distance));
                MethodInfo replacement = AccessTools.Method(typeof(BuilderCamera), nameof(BuildDistance));
                foreach (CodeInstruction instruction in instructions)
                {
                    if (instruction.Calls(distance)) instruction.operand = replacement;
                    yield return instruction;
                }
            }
        }

        [HarmonyPatch(typeof(Player), "Interact")]
        private static class BlockInteraction { private static bool Prefix(Player __instance) => !_active || __instance != Player.m_localPlayer; }

        [HarmonyPatch(typeof(Player), "AutoPickup")]
        private static class BlockPickup { private static bool Prefix(Player __instance) => !_active || __instance != Player.m_localPlayer; }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UseItem))]
        private static class EndBeforeItemUse
        {
            private static void Prefix(Humanoid __instance)
            {
                if (_active && __instance == Player.m_localPlayer) Stop("Build camera ended: using an item.");
            }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
        private static class BlockCombat
        {
            private static bool Prefix(Humanoid __instance, ref bool __result)
            {
                if (!_active || __instance != Player.m_localPlayer) return true;
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
        private static class EndOnDamage
        {
            private static void Prefix(Character __instance, HitData hit)
            {
                if (__instance == Player.m_localPlayer && hit.GetTotalDamage() > 0f) Stop("Build camera ended: you took damage.");
            }
        }
    }
}
