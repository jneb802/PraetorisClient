using System;
using System.Collections.Generic;
using UnityEngine;

namespace PraetorisClient
{
    internal static class ValheimEventsTelemetry
    {
        private const int ProtocolVersion = 1;
        private static readonly List<ExploredCell> PendingExplorationCells = new();
        private static float _nextExplorationFlushTime;
        private static long _sequence;

        internal static DamageObservationState CaptureDamageBefore(Character? target, HitData? hit)
        {
            if (!CanCaptureCombat() || target == null || hit == null || target.m_nview == null || !target.m_nview.IsOwner())
                return DamageObservationState.Empty;

            try
            {
                ZDOID targetZdo = GetCharacterZdoId(target);
                ZDOID attackerZdo = hit.m_attacker;
                Character? attacker = hit.GetAttacker();
                ClientZdoSnapshot targetBefore = ClientZdoSnapshot.Capture(target);
                ClientZdoSnapshot attackerBefore = CaptureCharacterOrZdo(attacker, attackerZdo);
                if (!targetBefore.IsPlayerSnapshot() && !attackerBefore.IsPlayerSnapshot())
                    return DamageObservationState.Empty;

                return new DamageObservationState
                {
                    Exists = true,
                    TargetZdo = targetZdo,
                    AttackerZdo = attackerZdo,
                    HealthBefore = target.GetHealth(),
                    MaxHealthBefore = target.GetMaxHealth(),
                    TargetBefore = targetBefore,
                    AttackerBefore = attackerBefore,
                };
            }
            catch (Exception ex)
            {
                PraetorisClientPlugin.Log.LogWarning($"Failed to capture damage telemetry before state: {ex.GetType().Name}: {ex.Message}");
                return DamageObservationState.Empty;
            }
        }

        internal static void LogDamageApplied(Character? target, HitData? hit, HitData.DamageModifier modifier, DamageObservationState before)
        {
            if (!CanCaptureCombat() || !before.Exists || target == null || hit == null)
                return;

            try
            {
                float healthAfter = target.GetHealth();
                float maxHealthAfter = target.GetMaxHealth();
                float damageApplied = Math.Max(0f, before.HealthBefore - healthAfter);
                if (damageApplied <= 0f)
                    return;

                ZDOID targetZdo = GetCharacterZdoId(target);
                ZDOID attackerZdo = hit.m_attacker.IsNone() ? before.AttackerZdo : hit.m_attacker;
                Character? attacker = hit.GetAttacker();
                ClientZdoSnapshot targetAfter = ClientZdoSnapshot.Capture(target);
                ClientZdoSnapshot attackerAfter = CaptureCharacterOrZdo(attacker, attackerZdo);
                ClientHitSummary hitSummary = ClientHitSummary.From(hit);

                TelemetryJson json = ObjectWithEnvelope("damage_applied");
                json.Prop("stage", "praetoris_client_apply_damage");
                json.Prop("method", "ApplyDamage");
                json.Prop("methodKnown", true);
                json.Prop("sourceClass", "Character");
                json.Prop("sourceMod", "PraetorisClient");
                json.Prop("targetZdo", ClientZdoSnapshot.FormatId(targetZdo));
                json.Prop("attackerZdo", ClientZdoSnapshot.FormatId(attackerZdo));
                json.Prop("damageApplied", damageApplied);
                json.Prop("healthBefore", before.HealthBefore);
                json.Prop("healthAfter", healthAfter);
                json.Prop("maxHealthBefore", before.MaxHealthBefore);
                json.Prop("maxHealthAfter", maxHealthAfter);
                json.Prop("damageModifier", FormatDamageModifier(modifier));
                hitSummary.Write(json, "hit");
                before.TargetBefore.Write(json, "targetBefore");
                targetAfter.Write(json, "targetAfter");
                before.AttackerBefore.Write(json, "attackerBefore");
                attackerAfter.Write(json, "attackerAfter");
                json.End();
                Send(json.ToString());
            }
            catch (Exception ex)
            {
                PraetorisClientPlugin.Log.LogWarning($"Failed to send damage telemetry: {ex.GetType().Name}: {ex.Message}");
            }
        }

        internal static DeathObservationState CaptureDeathBefore(Player? player)
        {
            if (!CanCaptureCombat() || player == null || player.m_nview == null || !player.m_nview.IsOwner())
                return DeathObservationState.Empty;

            try
            {
                HitData? lastHit = player.m_lastHit;
                ZDOID attackerZdo = lastHit != null ? lastHit.m_attacker : ZDOID.None;
                Character? attacker = lastHit != null ? lastHit.GetAttacker() : null;

                return new DeathObservationState
                {
                    Exists = true,
                    PlayerZdo = GetCharacterZdoId(player),
                    AttackerZdo = attackerZdo,
                    HealthBefore = player.GetHealth(),
                    MaxHealthBefore = player.GetMaxHealth(),
                    HasLastHit = lastHit != null,
                    LastHit = lastHit != null ? ClientHitSummary.From(lastHit) : ClientHitSummary.Empty,
                    PlayerBefore = ClientZdoSnapshot.Capture(player),
                    AttackerBefore = CaptureCharacterOrZdo(attacker, attackerZdo),
                };
            }
            catch (Exception ex)
            {
                PraetorisClientPlugin.Log.LogWarning($"Failed to capture death telemetry before state: {ex.GetType().Name}: {ex.Message}");
                return DeathObservationState.Empty;
            }
        }

        internal static void LogPlayerDied(Player? player, DeathObservationState before)
        {
            if (!CanCaptureCombat() || !before.Exists || player == null)
                return;

            try
            {
                float healthAfter = player.GetHealth();
                float maxHealthAfter = player.GetMaxHealth();
                HitData? lastHit = player.m_lastHit;
                ZDOID attackerZdo = lastHit != null && !lastHit.m_attacker.IsNone() ? lastHit.m_attacker : before.AttackerZdo;
                Character? attacker = lastHit != null ? lastHit.GetAttacker() : null;
                ClientZdoSnapshot playerAfter = ClientZdoSnapshot.Capture(player);
                ClientZdoSnapshot attackerAfter = CaptureCharacterOrZdo(attacker, attackerZdo);

                TelemetryJson json = ObjectWithEnvelope("player_died");
                json.Prop("stage", "praetoris_client_player_on_death");
                json.Prop("method", "OnDeath");
                json.Prop("methodKnown", true);
                json.Prop("sourceClass", "Player");
                json.Prop("sourceMod", "PraetorisClient");
                json.Prop("playerZdo", ClientZdoSnapshot.FormatId(before.PlayerZdo));
                json.Prop("attackerZdo", ClientZdoSnapshot.FormatId(attackerZdo));
                json.Prop("deathDetectedBy", "praetoris_client_player_on_death");
                json.Prop("damageApplied", Math.Max(0f, before.HealthBefore - healthAfter));
                json.Prop("healthBefore", before.HealthBefore);
                json.Prop("healthAfter", healthAfter);
                json.Prop("maxHealthBefore", before.MaxHealthBefore);
                json.Prop("maxHealthAfter", maxHealthAfter);
                if (before.HasLastHit)
                    before.LastHit.Write(json, "hit");
                before.PlayerBefore.Write(json, "playerBefore");
                playerAfter.Write(json, "playerAfter");
                before.AttackerBefore.Write(json, "attackerBefore");
                attackerAfter.Write(json, "attackerAfter");
                json.End();
                Send(json.ToString());
            }
            catch (Exception ex)
            {
                PraetorisClientPlugin.Log.LogWarning($"Failed to send death telemetry: {ex.GetType().Name}: {ex.Message}");
            }
        }

        internal static void RecordExploredCell(Minimap minimap, int x, int y)
        {
            if (!CanSendTelemetry() || !PraetorisClientPlugin.ExplorationTelemetryEnabled.Value || minimap == null || Player.m_localPlayer == null)
                return;

            PendingExplorationCells.Add(new ExploredCell(x, y));
            if (Time.time >= _nextExplorationFlushTime)
                FlushExploration(minimap, "interval");
        }

        internal static void Update()
        {
            if (PendingExplorationCells.Count == 0 || Minimap.instance == null || Time.time < _nextExplorationFlushTime)
                return;

            FlushExploration(Minimap.instance, "timer");
        }

        private static void FlushExploration(Minimap minimap, string reason)
        {
            if (PendingExplorationCells.Count == 0 || Player.m_localPlayer == null)
                return;

            try
            {
                Vector3 position = Player.m_localPlayer.transform.position;
                Vector2i zone = ZoneSystem.GetZone(position);
                string biome = WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(position).ToString() : "";
                int count = PendingExplorationCells.Count;

                TelemetryJson json = ObjectWithEnvelope("player_exploration");
                json.Prop("stage", "praetoris_client_minimap_explore");
                json.Prop("method", "Minimap.Explore");
                json.Prop("methodKnown", true);
                json.Prop("sourceClass", "Minimap");
                json.Prop("sourceMod", "PraetorisClient");
                json.Prop("reason", reason);
                json.Prop("playerZdo", Player.m_localPlayer.m_nview != null ? ClientZdoSnapshot.FormatId(Player.m_localPlayer.m_nview.GetZDO().m_uid) : "");
                json.Prop("playerName", Player.m_localPlayer.GetPlayerName());
                json.Prop("position", position);
                json.Prop("biome", biome);
                json.Prop("zoneX", zone.x);
                json.Prop("zoneY", zone.y);
                json.Prop("distanceFromCenter", Utils.DistanceXZ(Vector3.zero, position));
                json.Prop("newExploredCellCount", count);
                json.Prop("mapTextureSize", minimap.m_textureSize);
                json.Prop("mapPixelSize", minimap.m_pixelSize);
                json.BeginArray("cells");
                int maxCells = Math.Min(count, 128);
                for (int i = 0; i < maxCells; i++)
                {
                    ExploredCell cell = PendingExplorationCells[i];
                    json.BeginArrayObject();
                    json.Prop("x", cell.X);
                    json.Prop("y", cell.Y);
                    json.EndArrayObject();
                }
                json.EndArray();
                json.End();

                PendingExplorationCells.Clear();
                _nextExplorationFlushTime = Time.time + Math.Max(0.5f, PraetorisClientPlugin.ExplorationFlushSeconds.Value);
                Send(json.ToString());
            }
            catch (Exception ex)
            {
                PendingExplorationCells.Clear();
                PraetorisClientPlugin.Log.LogWarning($"Failed to send exploration telemetry: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static TelemetryJson ObjectWithEnvelope(string eventType)
        {
            TelemetryJson json = TelemetryJson.Object();
            json.Prop("schema", "valheim.events.client.v1");
            json.Prop("eventType", eventType);
            json.Prop("sequence", ++_sequence);
            json.Prop("clientTimeUtc", DateTime.UtcNow.ToString("o"));
            json.Prop("timeUtc", DateTime.UtcNow.ToString("o"));
            json.Prop("worldTime", ZNet.instance != null ? ZNet.instance.GetTime().ToString("o") : "");
            json.Prop("worldName", ZNet.m_world != null ? ZNet.m_world.m_name : "");
            json.Prop("worldUid", ZNet.m_world != null ? ZNet.m_world.m_uid : 0L);
            return json;
        }

        private static bool CanCaptureCombat()
        {
            return CanSendTelemetry() && PraetorisClientPlugin.CombatTelemetryEnabled.Value;
        }

        private static bool CanSendTelemetry()
        {
            return PraetorisClientPlugin.ValheimEventsTelemetryEnabled.Value &&
                   ZNet.instance != null &&
                   ZRoutedRpc.instance != null &&
                   !ZNet.instance.IsServer() &&
                   ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected;
        }

        private static void Send(string payload)
        {
            if (!CanSendTelemetry())
                return;

            ZPackage package = new();
            package.Write(ProtocolVersion);
            package.Write(payload);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcNames.ValheimEventsTelemetry, package);
        }

        private static ZDOID GetCharacterZdoId(Character character)
        {
            if (character.m_nview == null)
                return ZDOID.None;

            ZDO zdo = character.m_nview.GetZDO();
            return zdo != null ? zdo.m_uid : ZDOID.None;
        }

        private static ClientZdoSnapshot CaptureCharacterOrZdo(Character? character, ZDOID id)
        {
            ClientZdoSnapshot snapshot = ClientZdoSnapshot.Capture(character);
            return snapshot.Exists ? snapshot : ClientZdoSnapshot.Capture(id);
        }

        private static string FormatDamageModifier(HitData.DamageModifier modifier)
        {
            return modifier switch
            {
                HitData.DamageModifier.Resistant => "resistant",
                HitData.DamageModifier.Weak => "weak",
                HitData.DamageModifier.Immune => "immune",
                HitData.DamageModifier.Ignore => "ignore",
                HitData.DamageModifier.VeryResistant => "very_resistant",
                HitData.DamageModifier.VeryWeak => "very_weak",
                HitData.DamageModifier.SlightlyResistant => "slightly_resistant",
                HitData.DamageModifier.SlightlyWeak => "slightly_weak",
                _ => "normal",
            };
        }

        private readonly struct ExploredCell
        {
            internal readonly int X;
            internal readonly int Y;

            internal ExploredCell(int x, int y)
            {
                X = x;
                Y = y;
            }
        }
    }

    internal struct DamageObservationState
    {
        internal static readonly DamageObservationState Empty = new()
        {
            Exists = false,
            TargetZdo = ZDOID.None,
            AttackerZdo = ZDOID.None,
            TargetBefore = ClientZdoSnapshot.Empty,
            AttackerBefore = ClientZdoSnapshot.Empty,
        };

        internal bool Exists;
        internal ZDOID TargetZdo;
        internal ZDOID AttackerZdo;
        internal float HealthBefore;
        internal float MaxHealthBefore;
        internal ClientZdoSnapshot TargetBefore;
        internal ClientZdoSnapshot AttackerBefore;
    }

    internal struct DeathObservationState
    {
        internal static readonly DeathObservationState Empty = new()
        {
            Exists = false,
            PlayerZdo = ZDOID.None,
            AttackerZdo = ZDOID.None,
            LastHit = ClientHitSummary.Empty,
            PlayerBefore = ClientZdoSnapshot.Empty,
            AttackerBefore = ClientZdoSnapshot.Empty,
        };

        internal bool Exists;
        internal ZDOID PlayerZdo;
        internal ZDOID AttackerZdo;
        internal float HealthBefore;
        internal float MaxHealthBefore;
        internal bool HasLastHit;
        internal ClientHitSummary LastHit;
        internal ClientZdoSnapshot PlayerBefore;
        internal ClientZdoSnapshot AttackerBefore;
    }

    internal readonly struct ClientHitSummary
    {
        internal static readonly ClientHitSummary Empty = new(false, ZDOID.None, "", "", 0f, default, false, false, false, false, 0, 0f, 1f, 1f, Vector3.zero, Vector3.zero, 0, "", 0f, 0f, 0, 0, -1, 0f, 0f);

        private readonly bool _exists;
        private readonly ZDOID _attacker;
        private readonly string _hitType;
        private readonly string _skill;
        private readonly float _totalDamage;
        private readonly HitData.DamageTypes _damage;
        private readonly bool _dodgeable;
        private readonly bool _blockable;
        private readonly bool _ranged;
        private readonly bool _ignorePvp;
        private readonly short _toolTier;
        private readonly float _pushForce;
        private readonly float _backstabBonus;
        private readonly float _staggerMultiplier;
        private readonly Vector3 _point;
        private readonly Vector3 _dir;
        private readonly int _statusEffectHash;
        private readonly string _statusEffectName;
        private readonly float _skillRaiseAmount;
        private readonly float _skillLevel;
        private readonly short _itemLevel;
        private readonly byte _itemWorldLevel;
        private readonly short _weakSpot;
        private readonly float _healthReturn;
        private readonly float _radius;

        private ClientHitSummary(bool exists, ZDOID attacker, string hitType, string skill, float totalDamage, HitData.DamageTypes damage, bool dodgeable, bool blockable, bool ranged, bool ignorePvp, short toolTier, float pushForce, float backstabBonus, float staggerMultiplier, Vector3 point, Vector3 dir, int statusEffectHash, string statusEffectName, float skillRaiseAmount, float skillLevel, short itemLevel, byte itemWorldLevel, short weakSpot, float healthReturn, float radius)
        {
            _exists = exists;
            _attacker = attacker;
            _hitType = hitType;
            _skill = skill;
            _totalDamage = totalDamage;
            _damage = damage;
            _dodgeable = dodgeable;
            _blockable = blockable;
            _ranged = ranged;
            _ignorePvp = ignorePvp;
            _toolTier = toolTier;
            _pushForce = pushForce;
            _backstabBonus = backstabBonus;
            _staggerMultiplier = staggerMultiplier;
            _point = point;
            _dir = dir;
            _statusEffectHash = statusEffectHash;
            _statusEffectName = statusEffectName;
            _skillRaiseAmount = skillRaiseAmount;
            _skillLevel = skillLevel;
            _itemLevel = itemLevel;
            _itemWorldLevel = itemWorldLevel;
            _weakSpot = weakSpot;
            _healthReturn = healthReturn;
            _radius = radius;
        }

        internal static ClientHitSummary From(HitData hit)
        {
            return new ClientHitSummary(true, hit.m_attacker, hit.m_hitType.ToString(), hit.m_skill.ToString(), hit.m_damage.GetTotalDamage(), hit.m_damage, hit.m_dodgeable, hit.m_blockable, hit.m_ranged, hit.m_ignorePVP, hit.m_toolTier, hit.m_pushForce, hit.m_backstabBonus, hit.m_staggerMultiplier, hit.m_point, hit.m_dir, hit.m_statusEffectHash, hit.m_statusEffectHash != 0 ? hit.m_statusEffectHash.ToString() : "", hit.m_skillRaiseAmount, hit.m_skillLevel, hit.m_itemLevel, hit.m_itemWorldLevel, hit.m_weakSpot, hit.m_healthReturn, hit.m_radius);
        }

        internal void Write(TelemetryJson json, string propertyName)
        {
            if (!_exists)
                return;

            json.BeginObject(propertyName);
            json.Prop("attackerZdo", ClientZdoSnapshot.FormatId(_attacker));
            json.Prop("hitType", _hitType);
            json.Prop("skill", _skill);
            json.Prop("totalDamage", _totalDamage);
            json.Prop("damage", _damage.m_damage);
            json.Prop("blunt", _damage.m_blunt);
            json.Prop("slash", _damage.m_slash);
            json.Prop("pierce", _damage.m_pierce);
            json.Prop("chop", _damage.m_chop);
            json.Prop("pickaxe", _damage.m_pickaxe);
            json.Prop("fire", _damage.m_fire);
            json.Prop("frost", _damage.m_frost);
            json.Prop("lightning", _damage.m_lightning);
            json.Prop("poison", _damage.m_poison);
            json.Prop("spirit", _damage.m_spirit);
            json.Prop("dodgeable", _dodgeable);
            json.Prop("blockable", _blockable);
            json.Prop("ranged", _ranged);
            json.Prop("ignorePvp", _ignorePvp);
            json.Prop("toolTier", _toolTier);
            json.Prop("pushForce", _pushForce);
            json.Prop("backstabBonus", _backstabBonus);
            json.Prop("staggerMultiplier", _staggerMultiplier);
            json.Prop("point", _point);
            json.Prop("direction", _dir);
            json.Prop("statusEffectHash", _statusEffectHash);
            json.Prop("statusEffectName", _statusEffectName);
            json.Prop("skillRaiseAmount", _skillRaiseAmount);
            json.Prop("skillLevel", _skillLevel);
            json.Prop("itemLevel", _itemLevel);
            json.Prop("itemWorldLevel", _itemWorldLevel);
            json.Prop("weakSpot", _weakSpot);
            json.Prop("healthReturn", _healthReturn);
            json.Prop("radius", _radius);
            json.EndObject();
        }
    }
}
