using System.Collections.Generic;
using EpicLoot;
using EpicLoot.src.Magic.MagicItemEffects.Helpers;
using HarmonyLib;
using UnityEngine;

namespace PraetorisClient.EpicLootFeature
{
    // Only the thrower's client simulates a projectile (Projectile.FixedUpdate is owner-only), so bending its
    // velocity there is the whole effect; every other client just sees the synced position.
    internal static class HomingMissile
    {
        private const float DefaultAcquireAngle = 25f;
        private const float DefaultAcquireRange = 20f;
        private const float DefaultLoseRangeMultiplier = 1.6f;
        private const float DefaultStartTurnRate = 45f;
        private const float DefaultFullTurnRate = 1440f;
        private const float DefaultRampSeconds = 3f;

        private static int _losMask;

        internal static readonly Dictionary<Projectile, HomingProjectile> Flights = new Dictionary<Projectile, HomingProjectile>();

        internal static float AcquireAngle => Config("AcquireAngle", DefaultAcquireAngle, 1f, 180f);
        internal static float LoseRangeMultiplier => Config("LoseRangeMultiplier", DefaultLoseRangeMultiplier, 1f, 10f);
        private static float StartTurnRate => Config("StartTurnRate", DefaultStartTurnRate, 0f, 3600f);
        private static float FullTurnRate => Config("FullTurnRate", DefaultFullTurnRate, 0f, 36000f);
        private static float RampSeconds => Config("RampSeconds", DefaultRampSeconds, 0.01f, 60f);

        private static float Config(string key, float fallback, float min, float max)
        {
            return EffectConfig.GetClamped(PraetorisMagicEffects.HomingMissile, key, fallback, min, max);
        }

        internal static float RangeOf(ItemDrop.ItemData? item)
        {
            float range = item != null && item.IsMagic()
                ? item.GetMagicItem().GetTotalEffectValue(PraetorisMagicEffects.HomingMissile)
                : 0f;
            return range > 0f ? range : DefaultAcquireRange;
        }

        internal static float TurnRateAt(float flightTime)
        {
            float ramp = Mathf.Clamp01(flightTime / RampSeconds);
            return Mathf.Lerp(StartTurnRate, FullTurnRate, ramp * ramp);
        }

        internal static bool IsHomingWeapon(ItemDrop.ItemData? item)
        {
            return item != null && item.IsMagic() &&
                item.HasMagicEffect(PraetorisMagicEffects.HomingMissile, includeSocketed: true) &&
                (item.m_shared.m_skillType == Skills.SkillType.Spears ||
                 item.HasMagicEffect(MagicEffectType.Throwable, includeSocketed: true));
        }

        internal static bool IsValidTarget(Player? owner, Character? character)
        {
            if (owner == null || character == null || character == owner || character.IsDead())
            {
                return false;
            }

            return BaseAI.IsEnemy(owner, character) ||
                (character.GetBaseAI() != null && character.GetBaseAI().IsAggravatable());
        }

        internal static Character? FindTarget(Player owner, Vector3 origin, Vector3 dir, float maxAngle, float range)
        {
            Character? best = null;
            float bestScore = float.MaxValue;
            foreach (Character character in Character.GetAllCharacters())
            {
                if (!IsValidTarget(owner, character))
                {
                    continue;
                }

                Vector3 to = character.GetCenterPoint() - origin;
                float distance = to.magnitude;
                if (distance > range || distance < 0.5f)
                {
                    continue;
                }

                float angle = Vector3.Angle(dir, to);
                if (angle > maxAngle)
                {
                    continue;
                }

                float score = angle / maxAngle + 0.25f * distance / range;
                if (score < bestScore && HasLineOfSight(origin, character))
                {
                    best = character;
                    bestScore = score;
                }
            }

            return best;
        }

        internal static Vector3 TopPoint(Character character)
        {
            if (character.m_head != null)
            {
                return character.m_head.position;
            }

            Bounds bounds = character.m_collider != null
                ? character.m_collider.bounds
                : new Bounds(character.transform.position, Vector3.one);
            return new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        }

        internal static Character? LockedTarget()
        {
            foreach (HomingProjectile flight in Flights.Values)
            {
                if (flight != null && flight.InFlight && flight.Target != null)
                {
                    return flight.Target;
                }
            }

            return null;
        }

        private static bool HasLineOfSight(Vector3 origin, Character character)
        {
            if (_losMask == 0)
            {
                _losMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");
            }

            return !Physics.Linecast(origin, character.GetCenterPoint(), _losMask);
        }

        [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
        private static class ProjectileSetupPatch
        {
            private static void Postfix(Projectile __instance, Character owner, ItemDrop.ItemData item)
            {
                if (owner is not Player player || player != Player.m_localPlayer || !IsHomingWeapon(item) ||
                    __instance.m_nview == null || !__instance.m_nview.IsValid())
                {
                    return;
                }

                __instance.gameObject.AddComponent<HomingProjectile>().Begin(__instance, player);
            }
        }

        [HarmonyPatch(typeof(Projectile), nameof(Projectile.FixedUpdate))]
        private static class ProjectileFixedUpdatePatch
        {
            private static void Prefix(Projectile __instance)
            {
                if (Flights.TryGetValue(__instance, out HomingProjectile homing) &&
                    __instance.m_nview != null && __instance.m_nview.IsValid() && __instance.m_nview.IsOwner())
                {
                    homing.Steer(Time.fixedDeltaTime);
                }
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Update))]
        private static class PlayerUpdatePatch
        {
            private static void Postfix(Player __instance)
            {
                if (__instance == Player.m_localPlayer)
                {
                    HomingWisp.Update(__instance);
                }
            }
        }
    }

    internal sealed class HomingProjectile : MonoBehaviour
    {
        private const float ReacquireInterval = 0.2f;

        private Projectile _projectile = null!;
        private Player _owner = null!;
        private float _gravity;
        private float _reacquireTimer;
        private float _flightTime;
        private float _range;

        internal Character? Target { get; private set; }
        internal bool InFlight => _projectile != null && !_projectile.m_didHit;

        internal void Begin(Projectile projectile, Player owner)
        {
            _projectile = projectile;
            _owner = owner;
            _gravity = projectile.m_gravity;
            _range = HomingMissile.RangeOf(projectile.m_weapon);
            HomingMissile.Flights[projectile] = this;

            Target = HomingWisp.MarkedTarget;
            if (Target == null)
            {
                Vector3 heading = projectile.m_vel.sqrMagnitude > 0.01f ? projectile.m_vel.normalized : owner.transform.forward;
                Target = HomingMissile.FindTarget(owner, transform.position, heading, HomingMissile.AcquireAngle, _range);
            }
            ApplyGravity();
        }

        private void OnDestroy()
        {
            if (_projectile != null)
            {
                HomingMissile.Flights.Remove(_projectile);
            }
        }

        internal void Steer(float dt)
        {
            if (_projectile.m_didHit)
            {
                return;
            }

            _flightTime += dt;
            Vector3 velocity = _projectile.m_vel;
            float speed = velocity.magnitude;
            if (speed < 0.01f)
            {
                return;
            }

            Vector3 heading = velocity / speed;
            if (!HomingMissile.IsValidTarget(_owner, Target) ||
                Vector3.Distance(transform.position, Target!.GetCenterPoint()) > _range * HomingMissile.LoseRangeMultiplier)
            {
                Target = null;
                _reacquireTimer -= dt;
                if (_reacquireTimer <= 0f)
                {
                    _reacquireTimer = ReacquireInterval;
                    Target = HomingMissile.FindTarget(_owner, transform.position, heading, HomingMissile.AcquireAngle, _range);
                }

                ApplyGravity();
                if (Target == null)
                {
                    return;
                }
            }

            Vector3 wanted = (Target.GetCenterPoint() - transform.position).normalized;
            Vector3 steered = Vector3.RotateTowards(heading, wanted, HomingMissile.TurnRateAt(_flightTime) * Mathf.Deg2Rad * dt, 0f);
            _projectile.m_vel = steered * speed;
        }

        private void ApplyGravity()
        {
            _projectile.m_gravity = Target != null ? 0f : _gravity;
        }
    }

    // A local-only copy of the Wisplight ball. It is built under an inactive holder and stripped of its ZNetView
    // and behaviours before it ever wakes, so it never registers a ZDO.
    internal static class HomingWisp
    {
        private static readonly Color PreviewColor = new Color(1f, 0.55f, 0.15f);
        private static readonly Color LockColor = new Color(1f, 0.18f, 0.08f);

        private const string WisplightItem = "Demister";
        private const string BallPrefab = "demister_ball";
        private const float SearchInterval = 0.1f;

        private static GameObject? _holder;
        private static GameObject? _template;
        private static HomingWispBall? _ball;
        private static Character? _marked;
        private static float _searchTimer;

        internal static Character? MarkedTarget => HomingMissile.IsValidTarget(Player.m_localPlayer, _marked) ? _marked : null;

        internal static void Update(Player player)
        {
            Character? locked = HomingMissile.LockedTarget();

            Character? preview = null;
            if (!player.IsDead() && HomingMissile.IsHomingWeapon(player.GetCurrentWeapon()))
            {
                if (player.InAttack() && HomingMissile.IsValidTarget(player, _marked))
                {
                    preview = _marked;
                }
                else
                {
                    _searchTimer -= Time.deltaTime;
                    if (_searchTimer > 0f && HomingMissile.IsValidTarget(player, _marked))
                    {
                        preview = _marked;
                    }
                    else
                    {
                        _searchTimer = SearchInterval;
                        Vector3 origin = player.GetEyePoint();
                        preview = HomingMissile.FindTarget(player, origin, player.GetAimDir(origin),
                            HomingMissile.AcquireAngle, HomingMissile.RangeOf(player.GetCurrentWeapon()));
                    }
                }
            }
            _marked = preview;

            Character? wanted = locked ?? preview;
            if (wanted != null)
            {
                if (_ball == null)
                {
                    _ball = Spawn(player.GetCenterPoint());
                }
                _ball?.SetGoal(wanted, locked != null ? LockColor : PreviewColor);
            }
            else if (_ball != null)
            {
                _ball.ReturnHome(player);
            }
        }

        private static HomingWispBall? Spawn(Vector3 position)
        {
            GameObject? template = GetTemplate();
            if (template == null)
            {
                return null;
            }

            GameObject ball = Object.Instantiate(template, position, Quaternion.identity);
            ball.name = template.name;
            return ball.GetComponent<HomingWispBall>();
        }

        private static GameObject? GetTemplate()
        {
            if (_template != null)
            {
                return _template;
            }

            GameObject? source = FindSourcePrefab();
            if (source == null)
            {
                return null;
            }

            if (_holder == null)
            {
                _holder = new GameObject("Praetoris_HomingWispTemplate");
                _holder.SetActive(false);
                Object.DontDestroyOnLoad(_holder);
            }

            _template = Object.Instantiate(source, _holder.transform, false);
            _template.name = "Praetoris_HomingWisp";
            foreach (ParticleSystemRenderer glint in _template.GetComponents<ParticleSystemRenderer>())
            {
                Object.DestroyImmediate(glint);
            }
            foreach (ParticleSystem glint in _template.GetComponents<ParticleSystem>())
            {
                Object.DestroyImmediate(glint);
            }
            foreach (LODGroup lod in _template.GetComponentsInChildren<LODGroup>(true))
            {
                Object.DestroyImmediate(lod);
            }
            foreach (MonoBehaviour behaviour in _template.GetComponentsInChildren<MonoBehaviour>(true))
            {
                Object.DestroyImmediate(behaviour);
            }
            foreach (ParticleSystemForceField field in _template.GetComponentsInChildren<ParticleSystemForceField>(true))
            {
                Object.DestroyImmediate(field);
            }
            foreach (AudioSource audio in _template.GetComponentsInChildren<AudioSource>(true))
            {
                Object.DestroyImmediate(audio);
            }
            foreach (Collider collider in _template.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(collider);
            }
            foreach (Rigidbody body in _template.GetComponentsInChildren<Rigidbody>(true))
            {
                Object.DestroyImmediate(body);
            }
            _template.AddComponent<HomingWispBall>();
            return _template;
        }

        private static GameObject? FindSourcePrefab()
        {
            GameObject? item = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(WisplightItem) : null;
            if (item != null)
            {
                return item;
            }

            return ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(BallPrefab) : null;
        }
    }

    internal sealed class HomingWispBall : MonoBehaviour
    {
        private const float FlySpeed = 30f;
        private const float FlySnap = 6f;
        private const float HoverHeight = 0.8f;
        private const float BobAmplitude = 0.15f;
        private const float BobRate = 2.5f;
        private const float HomeRadius = 0.4f;
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseColor = Shader.PropertyToID("_Color");
        private static readonly string[] HiddenParts = { "effects/flame/energy (1)", "effects/flame/embers" };

        private Character? _target;
        private Player? _home;
        private bool _returning;
        private Color _color;
        private ParticleSystem[] _systems = System.Array.Empty<ParticleSystem>();
        private Light[] _lights = System.Array.Empty<Light>();
        private float _bobPhase;
        private readonly Dictionary<ParticleSystem, float> _alphas = new Dictionary<ParticleSystem, float>();
        private readonly Dictionary<Material, float> _emissions = new Dictionary<Material, float>();
        private readonly HashSet<Material> _tintable = new HashSet<Material>();

        private void Awake()
        {
            foreach (string path in HiddenParts)
            {
                Transform child = transform.Find(path);
                if (child != null)
                {
                    child.gameObject.SetActive(false);
                }
            }

            _systems = GetComponentsInChildren<ParticleSystem>(true);
            _lights = GetComponentsInChildren<Light>(true);
            _bobPhase = Random.Range(0f, Mathf.PI * 2f);
            foreach (ParticleSystem system in _systems)
            {
                ParticleSystem.MinMaxGradient startColor = system.main.startColor;
                _alphas[system] = startColor.mode == ParticleSystemGradientMode.TwoColors ? startColor.colorMax.a : startColor.color.a;
            }
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.materials)
                {
                    if (material.HasProperty(EmissionColor))
                    {
                        Color emission = material.GetColor(EmissionColor);
                        _emissions[material] = Mathf.Max(emission.r, emission.g, emission.b);
                    }
                    if (renderer is not ParticleSystemRenderer && material.HasProperty(BaseColor))
                    {
                        _tintable.Add(material);
                    }
                }
            }
        }

        internal void SetGoal(Character target, Color color)
        {
            _target = target;
            _returning = false;
            if (color != _color)
            {
                _color = color;
                Tint(color);
            }
        }

        internal void ReturnHome(Player home)
        {
            _target = null;
            _home = home;
            _returning = true;
        }

        private void Update()
        {
            Vector3 goal;
            if (_target != null && !_target.IsDead())
            {
                float bob = BobAmplitude * Mathf.Sin(Time.time * BobRate + _bobPhase);
                goal = HomingMissile.TopPoint(_target) + Vector3.up * (HoverHeight + bob);
            }
            else if (_returning && _home != null)
            {
                goal = _home.GetCenterPoint();
            }
            else
            {
                Destroy(gameObject);
                return;
            }

            float distance = Vector3.Distance(transform.position, goal);
            float step = (FlySpeed + distance * FlySnap) * Time.deltaTime;
            transform.position = Vector3.MoveTowards(transform.position, goal, step);
            if (_returning && distance < HomeRadius)
            {
                Destroy(gameObject);
            }
        }

        private void Tint(Color color)
        {
            Color pale = Color.Lerp(color, Color.white, 0.35f);
            foreach (ParticleSystem system in _systems)
            {
                float alpha = _alphas.TryGetValue(system, out float a) ? a : 1f;
                Color tint = color;
                Color paleTint = pale;
                tint.a = alpha;
                paleTint.a = alpha;
                ParticleSystem.MainModule main = system.main;
                main.startColor = new ParticleSystem.MinMaxGradient(tint, paleTint);
                RecolorLiveParticles(system, tint);
            }
            foreach (Light light in _lights)
            {
                light.color = color;
            }
            foreach (KeyValuePair<Material, float> emission in _emissions)
            {
                emission.Key.SetColor(EmissionColor, color * emission.Value);
            }
            foreach (Material material in _tintable)
            {
                material.SetColor(BaseColor, color);
            }
        }

        // A start colour only reaches particles born after it is set; the flare is one particle that never dies.
        private static void RecolorLiveParticles(ParticleSystem system, Color tint)
        {
            int count = system.particleCount;
            if (count == 0)
            {
                return;
            }

            var particles = new ParticleSystem.Particle[count];
            int live = system.GetParticles(particles);
            for (int i = 0; i < live; i++)
            {
                particles[i].startColor = tint;
            }
            system.SetParticles(particles, live);
        }
    }
}
