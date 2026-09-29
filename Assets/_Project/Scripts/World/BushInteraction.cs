using UnityEngine;
using PrimalFrontier.AI;
using PrimalFrontier.Audio;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.VFX;

namespace PrimalFrontier.World
{
    /// <summary>stored as ints in the scene: append only</summary>
    public enum BushVariant { Plain = 0, Berries = 1, HiddenItem = 2, AnimalFlush = 3 }

    /// <summary>what the last rustle with feedback did (light / strong rustle, or a hidden discovery)</summary>
    public enum RustleReaction { None, Light, Strong, Discovery }
    /// <summary>what a discovery turned out to be</summary>
    public enum BushDiscovery { None, Berries, BirdBurst, Fiber, StartledAnimal }

    /// <summary>one entry of the hidden item table (weighted pick, count min..max)</summary>
    [System.Serializable]
    public struct BushLoot
    {
        public ItemDefinition item;
        [Min(1)] public int min;
        [Min(1)] public int max;
        [Min(0)] public float weight;
    }

    /// <summary>
    /// Walk-through bush (root: trigger collider + kinematic Rigidbody; child "Visual": the mesh). The player or a creature
    /// entering makes the visual shake on a damped spring (tilt away from the mover + slight squash; running is stronger,
    /// crouching smaller), then, after a short random delay and a per-bush cooldown, a pooled leaf burst and a rustle.
    /// Variants: Berries (a ResourceNode on the same object, added by the builder), HiddenItem (the first player rustle
    /// drops a pickup), AnimalFlush (a bigger burst, as if a small animal darted out; re-arms after flushCooldown).
    /// Every other rustle with feedback rolls a reaction: 70 % light rustle, 20 % strong rustle (bigger burst, louder,
    /// an extra shake), 10 % hidden discovery when the player did it (per-bush discoveryCooldown plus a short global gap;
    /// otherwise a strong rustle): a few berries on a berry bush, a bird / insect burst (pooled leaves + high rustles),
    /// a fibre pickup, or a nearby small animal startled (small passive creature or an ambient flyer within startleRange).
    /// Idle bushes cost nothing: the component disables itself when settled and trigger messages re-enable it.
    /// Nothing beyond cullDistance from the camera reacts (creature triggers out there return before any lookup).
    /// One trigger collider per bush. Effects are pooled; pickups only spawn on a discovery. No allocations per frame.
    /// </summary>
    [DisallowMultipleComponent]
    public class BushInteraction : MonoBehaviour
    {
        public BushVariant variant = BushVariant.Plain;
        [Tooltip("the mesh child that shakes (never the root with the trigger). Empty = child 'Visual', else the first child")]
        public Transform visual;

        [Header("Shake")]
        [Tooltip("tilt in degrees at full strength (running); walking is about half, crouching a quarter")] public float maxAngle = 15f;
        [Tooltip("squash fraction at full strength")] public float squash = 0.06f;
        [Tooltip("spring frequency (Hz)")] public float frequency = 2.3f;
        [Tooltip("damping ratio (lower = longer wobble)")] [Range(0.05f, 1f)] public float damping = 0.22f;

        [Header("Feedback")]
        [Tooltip("seconds between leaf bursts / rustles of this bush (random in the range)")] public Vector2 cooldown = new Vector2(1.5f, 3f);
        [Tooltip("no reaction at all beyond this distance from the camera")] public float cullDistance = 40f;
        [Tooltip("light rustle while the player keeps moving inside")] public float brushInterval = 0.8f;
        public float brushMinSpeed = 0.5f;

        [Header("Hidden item (variant HiddenItem)")]
        public BushLoot[] hiddenLoot;

        [Header("Animal flush (variant AnimalFlush)")]
        public float flushCooldown = 60f;
        [Tooltip("a small dust puff where the animal runs off")] public bool flushDust = true;

        [Header("Reaction roll (each rustle with feedback)")]
        [Tooltip("chance of a strong rustle (bigger leaf burst, louder, extra shake); the rest is a light rustle")] [Range(0, 1)] public float strongChance = 0.2f;
        [Tooltip("chance of a hidden discovery when the player rustles the bush (berries, bird / insect burst, fibre, startled animal)")] [Range(0, 1)] public float discoveryChance = 0.1f;
        [Tooltip("seconds before this bush can give another discovery (a strong rustle instead)")] public float discoveryCooldown = 120f;
        [Tooltip("small animals within this horizontal range (m) can be startled by a discovery")] public float startleRange = 25f;
        [Tooltip("pickup count of a berry / fibre discovery")] public Vector2Int discoveryCount = new Vector2Int(1, 2);

        /// <summary>bushes currently running Update (shaking, feedback pending or player inside): for profiling</summary>
        public static int ActiveCount { get; private set; }
        /// <summary>every bush that woke up once (Awake to OnDestroy); the perception cover query reads it</summary>
        public static readonly System.Collections.Generic.List<BushInteraction> All = new System.Collections.Generic.List<BushInteraction>();
        /// <summary>the bush the player stands in (null = none): cover for the perception system</summary>
        public static BushInteraction PlayerBush { get; private set; }
        /// <summary>leaf bursts / rustles played since start (capture + log)</summary>
        public static int FeedbackCount { get; private set; }
        /// <summary>hidden discoveries since start (any bush)</summary>
        public static int DiscoveryCount { get; private set; }
        /// <summary>tests: replaces Random.value for the reaction roll and the discovery pick (null = random)</summary>
        public static System.Func<float> ReactionRoll;
        /// <summary>at least this many seconds between two discoveries anywhere (no farming by running through a thicket)</summary>
        public static float GlobalDiscoveryGap = 8f;

        public bool IsShaking => _shaking;
        /// <summary>horizontal radius of the trigger (m)</summary>
        public float Radius => _radius;
        public bool PlayerInside => _playerColliders > 0;
        public bool HiddenItemTaken => _hiddenSpent;
        /// <summary>current tilt in degrees</summary>
        public float Tilt => _ang;
        /// <summary>what the last rustle with feedback did / what the last discovery was</summary>
        public RustleReaction LastReaction { get; private set; }
        public BushDiscovery LastDiscovery { get; private set; }

        Transform _vis; Quaternion _baseRot; Vector3 _baseScale;
        Vector3 _axis = Vector3.right;                     // tilt axis in the visual's parent space
        float _ang, _angVel, _sq, _sqVel; bool _shaking;
        Vector3 _fxLocal = new Vector3(0f, 0.7f, 0f); float _radius = 0.9f;
        float _fxAt = -1f, _fx2At = -1f, _fxStrength; Vector3 _fxDir; bool _fxCrouch, _fxFlush, _fxHidden;
        float _nextFeedback, _nextFlush, _nextBrush, _lastImpulse = -10f, _lastStrength, _lastFx = -10f, _lastMotion = -10f;
        bool _fxPlayer;
        int _playerColliders; bool _hiddenSpent, _counted;
        RustleReaction _fxKind; bool _fx2Bird; float _nextDiscovery;

        static Camera _cam;
        static Transform _playerRoot; static PlayerMotor _motor; static CharacterController _cc;
        static float _globalNextDiscovery; static ItemDefinition _fiber; static bool _fiberLooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            ActiveCount = 0; FeedbackCount = 0; DiscoveryCount = 0; _cam = null; _playerRoot = null; _motor = null; _cc = null; All.Clear(); PlayerBush = null;
            _globalNextDiscovery = 0f; _fiber = null; _fiberLooked = false; ReactionRoll = null;
        }

        static float Roll() => ReactionRoll != null ? Mathf.Clamp01(ReactionRoll()) : Random.value;

        void Awake()
        {
            _vis = visual ? visual : transform.Find("Visual");
            if (!_vis && transform.childCount > 0) _vis = transform.GetChild(0);
            if (_vis) { _baseRot = _vis.localRotation; _baseScale = _vis.localScale; }

            // a CharacterController only raises trigger messages when one side has a Rigidbody
            var rb = GetComponent<Rigidbody>();
            if (!rb) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false; rb.interpolation = RigidbodyInterpolation.None;

            var col = GetComponent<Collider>();
            if (!col) { var s = gameObject.AddComponent<SphereCollider>(); s.radius = 0.9f; s.center = new Vector3(0f, 0.7f, 0f); col = s; }
            col.isTrigger = true;
            float scale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));
            if (col is SphereCollider sc) { _fxLocal = sc.center + Vector3.up * sc.radius * 0.2f; _radius = sc.radius * scale; }
            else if (col is BoxCollider bc) { _fxLocal = bc.center; _radius = Mathf.Max(bc.size.x, bc.size.z) * 0.5f * scale; }
            else if (col is CapsuleCollider cc) { _fxLocal = cc.center; _radius = cc.radius * scale; }

            enabled = false;                                // idle until something walks in (trigger messages reach disabled behaviours)
            if (!All.Contains(this)) All.Add(this);
        }

        void OnDestroy() { All.Remove(this); if (PlayerBush == this) PlayerBush = null; }

        void OnEnable() { if (!_counted) { _counted = true; ActiveCount++; } }

        void OnDisable()
        {
            if (_counted) { _counted = false; ActiveCount--; }
            // deactivated mid-shake (or the player left without an exit message): come back to rest
            if (_shaking) { _ang = _angVel = _sq = _sqVel = 0f; _shaking = false; ApplyRest(); }
            _playerColliders = 0; _fxAt = _fx2At = -1f; _fx2Bird = false;
            if (PlayerBush == this) PlayerBush = null;
        }

        // ------------------------------------------------------------------ triggers
        void OnTriggerEnter(Collider other)
        {
            if (other is TerrainCollider) return;
            if (IsPlayer(other))
            {
                _playerColliders++;
                if (_playerColliders > 1) return;          // a second collider of the player (held item): one reaction
                PlayerBush = this;
                bool crouch = _motor && _motor.IsCrouching;
                float s = Mathf.Clamp(0.25f + PlayerSpeed() * 0.16f, 0.3f, 1.25f);   // walk ~0.47, run ~0.86, sprint ~1.24
                if (crouch) s *= 0.45f;
                _nextBrush = Time.time + brushInterval;
                enabled = true;                             // tracks the player while inside
                Rustle(_playerRoot ? _playerRoot.position : other.transform.position, s, crouch, true);
                return;
            }
            if (Far(transform.position, cullDistance)) return;            // far creatures: no lookups at all
            var dino = other.GetComponentInParent<DinosaurController>();
            if (dino)
            {
                if (dino.IsAlive) Rustle(dino.transform.position, DinoStrength(dino), false, false);
                return;
            }
            var amb = other.GetComponentInParent<AmbientCreature>();
            if (amb && amb.IsAlive) Rustle(amb.transform.position, 0.8f, false, false);
        }

        void OnTriggerExit(Collider other)
        {
            if (other is TerrainCollider || _playerColliders == 0) return;
            if (IsPlayer(other)) { _playerColliders = Mathf.Max(0, _playerColliders - 1); if (_playerColliders == 0 && PlayerBush == this) PlayerBush = null; }
        }

        /// <summary>
        /// Shake the bush as if something pushed through it from 'from'. strength: 0.3 walk .. 1.25 sprint (crouch x0.45).
        /// Plays the leaf burst + rustle unless this bush is in its cooldown. player = the player did it (hidden items).
        /// </summary>
        public void Rustle(Vector3 from, float strength, bool crouch, bool player)
        {
            if (!gameObject.activeInHierarchy) return;
            Vector3 pos = transform.position;
            if (!InRange(pos)) return;
            float now = Time.time;
            bool flush = variant == BushVariant.AnimalFlush && now >= _nextFlush;
            bool hidden = player && variant == BushVariant.HiddenItem && !_hiddenSpent;
            // several colliders of one creature / quick re-entries: one impulse
            if (!flush && !hidden && now - _lastImpulse < 0.2f && strength <= _lastStrength) return;
            _lastImpulse = now; _lastStrength = strength;

            Vector3 dir = pos - from; dir.y = 0f;
            if (dir.sqrMagnitude > 1e-4f) dir.Normalize(); else dir = Vector3.zero;
            if (flush) strength = Mathf.Max(strength, 0.9f) * 1.25f;
            Kick(dir, strength);
            // a visibly shaking bush gives the player away to anything looking this way (perception)
            if (player && now - _lastMotion > 0.5f) { _lastMotion = now; Stimuli.Motion(pos + Vector3.up * 0.6f, Mathf.Min(1.2f, strength), StimulusSource.Player); }

            if (now >= _nextFeedback || flush || hidden)
            {
                _nextFeedback = now + Random.Range(cooldown.x, Mathf.Max(cooldown.x, cooldown.y));
                _fxAt = now + Random.Range(0f, 0.15f);
                _fxStrength = strength; _fxCrouch = crouch; _fxDir = dir; _fxPlayer = player;
                _fxFlush = flush; if (flush) _nextFlush = now + flushCooldown;
                if (hidden) { _fxHidden = true; _hiddenSpent = true; }
                _fxKind = flush || hidden ? RustleReaction.Strong : RollReaction(player, now);
            }
            enabled = true;
        }

        /// <summary>70 % light, 20 % strong, 10 % discovery (player only, when this bush and the island are off cooldown)</summary>
        RustleReaction RollReaction(bool player, float now)
        {
            float r = Roll();
            if (r < discoveryChance)
            {
                if (player && now >= _nextDiscovery && now >= _globalNextDiscovery) return RustleReaction.Discovery;
                return RustleReaction.Strong;
            }
            return r < discoveryChance + strongChance ? RustleReaction.Strong : RustleReaction.Light;
        }

        // ------------------------------------------------------------------ update (only while active)
        void Update()
        {
            float now = Time.time;
            if (_shaking) Step(Mathf.Min(Time.deltaTime, 0.05f));
            if (_fxAt >= 0f && now >= _fxAt) { _fxAt = -1f; Feedback(); }
            if (_fx2At >= 0f && now >= _fx2At) { _fx2At = -1f; FlushSecond(); }
            if (_playerColliders > 0) TrackPlayer(now);
            if (!_shaking && _fxAt < 0f && _fx2At < 0f && _playerColliders == 0) enabled = false;
        }

        void Kick(Vector3 dir, float strength)
        {
            if (!_vis) return;
            float s = Mathf.Clamp(strength, 0f, 1.6f);
            float w = 2f * Mathf.PI * Mathf.Max(0.2f, frequency);
            // tilt the top away from the mover (axis = up x dir), a little random so bushes never move in lockstep
            Vector3 axisW;
            if (dir.sqrMagnitude > 0f) axisW = Quaternion.AngleAxis(Random.Range(-35f, 35f), Vector3.up) * Vector3.Cross(Vector3.up, dir);
            else { float a = Random.Range(0f, 2f * Mathf.PI); axisW = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)); }
            var parent = _vis.parent;
            Vector3 axisP = parent ? parent.InverseTransformDirection(axisW) : axisW;
            if (axisP.sqrMagnitude < 1e-6f) axisP = Vector3.right;
            axisP.Normalize();
            float impulse = maxAngle * s * w * Random.Range(0.85f, 1.1f);
            if (_shaking && Mathf.Abs(_ang) > 0.5f) _angVel += Vector3.Dot(axisP, _axis) >= 0f ? impulse : -impulse;   // keep the axis: no pop
            else { _axis = axisP; _angVel += impulse; }
            _sqVel += squash * s * w * 1.4f;
            float cap = maxAngle * 2.2f * w;
            _angVel = Mathf.Clamp(_angVel, -cap, cap);
            _shaking = true;
        }

        void Step(float dt)
        {
            float w = 2f * Mathf.PI * Mathf.Max(0.2f, frequency);
            _angVel += (-w * w * _ang - 2f * damping * w * _angVel) * dt;
            _ang += _angVel * dt;
            float w2 = w * 1.4f;
            _sqVel += (-w2 * w2 * _sq - 2f * Mathf.Min(1f, damping * 1.3f) * w2 * _sqVel) * dt;
            _sq += _sqVel * dt;
            float lim = maxAngle * 1.8f; _ang = Mathf.Clamp(_ang, -lim, lim); _sq = Mathf.Clamp(_sq, -0.25f, 0.25f);
            if (Mathf.Abs(_ang) < 0.03f && Mathf.Abs(_angVel) < 0.3f && Mathf.Abs(_sq) < 0.0005f && Mathf.Abs(_sqVel) < 0.01f)
            {
                _ang = _angVel = _sq = _sqVel = 0f; _shaking = false; ApplyRest(); return;
            }
            _vis.localRotation = Quaternion.AngleAxis(_ang, _axis) * _baseRot;
            _vis.localScale = new Vector3(_baseScale.x * (1f + _sq * 0.5f), _baseScale.y * (1f - _sq), _baseScale.z * (1f + _sq * 0.5f));
        }

        void ApplyRest() { if (_vis) { _vis.localRotation = _baseRot; _vis.localScale = _baseScale; } }

        void TrackPlayer(float now)
        {
            var root = _playerRoot;
            if (!root) { _playerColliders = 0; return; }
            Vector3 d = root.position - transform.position; d.y = 0f;
            float r = _radius + 1.5f;
            if (d.sqrMagnitude > r * r) { _playerColliders = 0; if (PlayerBush == this) PlayerBush = null; return; }   // exit message missed (teleport, collider disabled)
            if (now < _nextBrush) return;
            _nextBrush = now + brushInterval;
            float speed = PlayerSpeed();
            if (speed < brushMinSpeed || !InRange(transform.position)) return;
            bool crouch = _motor && _motor.IsCrouching;
            float s = Mathf.Clamp(0.12f + speed * 0.07f, 0.15f, 0.55f) * (crouch ? 0.5f : 1f);
            Vector3 dir = Vector3.zero;
            if (_cc) { dir = _cc.velocity; dir.y = 0f; if (dir.sqrMagnitude > 1e-4f) dir.Normalize(); else dir = Vector3.zero; }
            Kick(dir, s);
            var pc = AI.PerceptionConfig.Instance;
            Stimuli.Noise(transform.position, pc.bushBrushLoudness * (crouch ? 0.5f : 1f), NoiseTag.Rustle, StimulusSource.Player);
            if (now - _lastMotion > 0.5f) { _lastMotion = now; Stimuli.Motion(transform.position + Vector3.up * 0.6f, s, StimulusSource.Player); }
            // quiet rustle only (no leaf burst: no VFX spam), never right after the main feedback
            if (now - _lastFx < 0.5f || _fxAt >= 0f) return;
            var src = SfxPlayer.Instance.Play(SfxId.LeafRustle, transform.TransformPoint(_fxLocal), crouch ? 0.15f : Mathf.Lerp(0.2f, 0.35f, Mathf.InverseLerp(0.5f, 6f, speed)));
            if (src) src.pitch *= Random.Range(0.95f, 1.1f);
        }

        // ------------------------------------------------------------------ feedback
        void Feedback()
        {
            if (_fxHidden) { _fxHidden = false; SpawnHidden(); }            // already claimed: drop it even if the camera moved away
            Vector3 center = transform.TransformPoint(_fxLocal);
            if (!InRange(center)) return;
            float now = Time.time; _lastFx = now; FeedbackCount++;
            var kind = _fxKind; LastReaction = kind;
            Vector3 p = center - _fxDir * (_radius * 0.35f);                 // on the side the mover came in
            float s01 = Mathf.InverseLerp(0.3f, 1.25f, _fxStrength);
            bool low = GameSettings.Quality == 0;
            bool strong = kind != RustleReaction.Light;
            float vfxScale = _fxFlush ? Random.Range(0.85f, 1.1f) : Mathf.Lerp(0.35f, 0.6f, s01) * (strong ? 1.45f : 0.85f);
            float vol = _fxFlush ? 0.7f : Mathf.Min(0.85f, Mathf.Lerp(0.35f, 0.7f, s01) * (strong ? 1.3f : 0.85f));
            if (!low || !Far(p, 20f)) VfxPool.Instance.Play(VfxId.Leaves, p, Vector3.up, null, low ? vfxScale * 0.8f : vfxScale);
            var src = SfxPlayer.Instance.Play(SfxId.LeafRustle, p, vol);
            if (src) src.pitch *= _fxCrouch ? Random.Range(0.9f, 0.98f) : Random.Range(0.95f, 1.08f);
            // the rustle is heard (perception): light / strong / flush or discovery burst
            {
                var pc = AI.PerceptionConfig.Instance;
                float loud = _fxFlush || kind == RustleReaction.Discovery ? pc.bushFlushLoudness : strong ? pc.bushStrongLoudness : pc.bushLightLoudness;
                if (_fxCrouch) loud *= 0.6f;
                Stimuli.Noise(p, loud, NoiseTag.Rustle, _fxPlayer ? StimulusSource.Player : StimulusSource.Creature);
            }
            if (strong && !_fxFlush) Kick(_fxDir, 0.35f);                     // something bigger moved inside
            if (kind == RustleReaction.Discovery) Discover(center, now);
            if (_fxFlush)
            {
                _fx2At = now + Random.Range(0.12f, 0.22f);
                if (flushDust)
                {
                    Vector3 away = _fxDir.sqrMagnitude > 0f ? _fxDir : transform.forward;
                    VfxPool.Instance.Play(VfxId.DinoFootDust, transform.position + away * (_radius + 0.6f), Vector3.up, null, 0.3f);
                }
            }
        }

        /// <summary>the animal darting off: a second, higher rustle on the far side (bird burst: high and at the top)</summary>
        void FlushSecond()
        {
            if (_fx2Bird)
            {
                _fx2Bird = false;
                Vector3 top = transform.TransformPoint(_fxLocal) + Vector3.up * (_radius * 0.8f);
                if (!InRange(top)) return;
                var b = SfxPlayer.Instance.Play(SfxId.LeafRustle, top, 0.45f);
                if (b) b.pitch *= Random.Range(1.45f, 1.7f);
                return;
            }
            Vector3 away = _fxDir.sqrMagnitude > 0f ? _fxDir : transform.forward;
            Vector3 p = transform.TransformPoint(_fxLocal) + away * (_radius + 0.4f);
            if (!InRange(p)) return;
            var src = SfxPlayer.Instance.Play(SfxId.LeafRustle, p, 0.55f);
            if (src) src.pitch *= Random.Range(1.15f, 1.3f);
            Kick(away, 0.5f);
        }

        // ------------------------------------------------------------------ discovery
        /// <summary>a hidden discovery: berries (berry bushes), a bird / insect burst, fibre, or a startled small animal</summary>
        void Discover(Vector3 center, float now)
        {
            _nextDiscovery = now + discoveryCooldown;
            _globalNextDiscovery = now + GlobalDiscoveryGap;
            DiscoveryCount++;
            ResourceNode node = null;
            bool berries = variant == BushVariant.Berries && TryGetComponent(out node) && node.yieldItem;
            if (!_fiberLooked) { _fiberLooked = true; var db = ItemDatabase.Instance; _fiber = db ? db.Item("fiber") : null; }
            bool fiber = _fiber;
            Component animal = NearbySmallAnimal(transform.position);
            // pick one of the available outcomes (the bird burst is always possible)
            int n = 1 + (berries ? 1 : 0) + (fiber ? 1 : 0) + (animal ? 1 : 0);
            int pick = Mathf.Min(n - 1, (int)(Roll() * n));
            BushDiscovery d = BushDiscovery.BirdBurst;
            if (berries && pick-- == 0) d = BushDiscovery.Berries;
            else if (fiber && pick-- == 0) d = BushDiscovery.Fiber;
            else if (animal && pick-- == 0) d = BushDiscovery.StartledAnimal;
            LastDiscovery = d;
            int cMin = Mathf.Max(1, discoveryCount.x), count = Random.Range(cMin, Mathf.Max(cMin, discoveryCount.y) + 1);
            Vector3 edge = transform.position - _fxDir * (_radius * 0.8f + 0.2f) + Vector3.up * 0.3f;
            switch (d)
            {
                case BushDiscovery.Berries:
                    if (WorldPickup.Drop(node.yieldItem, count, edge)) PlayerInteraction.Notify("A few " + node.yieldItem.displayName.ToLowerInvariant() + " fell from the bush.");
                    break;
                case BushDiscovery.Fiber:
                    if (WorldPickup.Drop(_fiber, count, edge)) PlayerInteraction.Notify("Loose " + _fiber.displayName.ToLowerInvariant() + " caught in the bush.");
                    break;
                case BushDiscovery.StartledAnimal:
                    if (animal is DinosaurController dc) dc.Flee(transform.position);
                    else if (animal is AmbientCreature ac) ac.Startle(transform.position);
                    Kick(_fxDir, 0.5f);
                    break;
                default:
                    // birds / insects bursting out of the crown: a big upward leaf burst and two high rustles
                    Vector3 top = center + Vector3.up * (_radius * 0.6f);
                    VfxPool.Instance.Play(VfxId.Leaves, top, Vector3.up, null, Random.Range(0.95f, 1.2f));
                    var s = SfxPlayer.Instance.Play(SfxId.LeafRustle, top, 0.6f);
                    if (s) s.pitch *= Random.Range(1.3f, 1.5f);
                    _fx2Bird = true; _fx2At = now + Random.Range(0.1f, 0.18f);
                    break;
            }
        }

        /// <summary>a small passive / defensive creature, or an ambient flyer / swimmer, within startleRange (horizontal); null = none</summary>
        Component NearbySmallAnimal(Vector3 p)
        {
            Component best = null; float bd = startleRange * startleRange;
            var dinos = DinosaurController.All;
            for (int i = 0; i < dinos.Count; i++)
            {
                var d = dinos[i];
                if (!d || !d.IsAlive || !d.def || d.def.bodyRadius > 0.7f) continue;
                if (d.def.temperament != Temperament.Passive && d.def.temperament != Temperament.Defensive) continue;
                float dx = d.transform.position.x - p.x, dz = d.transform.position.z - p.z, q = dx * dx + dz * dz;
                if (q < bd) { bd = q; best = d; }
            }
            var amb = AmbientCreature.All;
            for (int i = 0; i < amb.Count; i++)
            {
                var a = amb[i];
                if (!a || !a.IsAlive) continue;
                float dx = a.transform.position.x - p.x, dz = a.transform.position.z - p.z, q = dx * dx + dz * dz;
                if (q < bd) { bd = q; best = a; }
            }
            return best;
        }

        void SpawnHidden()
        {
            if (hiddenLoot == null || hiddenLoot.Length == 0) return;
            float total = 0f;
            for (int i = 0; i < hiddenLoot.Length; i++) if (hiddenLoot[i].item && hiddenLoot[i].weight > 0f) total += hiddenLoot[i].weight;
            if (total <= 0f) return;
            float r = Random.value * total; int pick = -1;
            for (int i = 0; i < hiddenLoot.Length; i++)
            {
                if (!hiddenLoot[i].item || hiddenLoot[i].weight <= 0f) continue;
                pick = i; r -= hiddenLoot[i].weight;
                if (r <= 0f) break;
            }
            if (pick < 0) return;
            var l = hiddenLoot[pick];
            int min = Mathf.Max(1, l.min), max = Mathf.Max(min, l.max);
            int n = Random.Range(min, max + 1);
            // at the bush edge on the player's side, settled on the ground by WorldPickup
            Vector3 at = transform.position - _fxDir * (_radius * 0.8f + 0.2f) + Vector3.up * 0.3f;
            if (WorldPickup.Drop(l.item, n, at)) PlayerInteraction.Notify("Something fell out of the bush: " + l.item.displayName + (n > 1 ? " x" + n : "") + ".");
        }

        // ------------------------------------------------------------------ helpers
        bool InRange(Vector3 p) => !Far(p, cullDistance);

        static bool Far(Vector3 p, float d)
        {
            if (!_cam || !_cam.isActiveAndEnabled) _cam = Camera.main;
            return _cam && (p - _cam.transform.position).sqrMagnitude > d * d;
        }

        static bool IsPlayer(Collider c)
        {
            var root = PlayerLocator.Player;
            if (root)
            {
                if (c.transform != root && !c.transform.IsChildOf(root)) return false;
                Cache(root); return true;
            }
            var m = c.GetComponentInParent<PlayerMotor>();
            if (!m && !c.CompareTag("Player")) return false;
            Cache(m ? m.transform : c.transform); return true;
        }

        static void Cache(Transform root)
        {
            if (root == _playerRoot && (_motor || _cc)) return;
            _playerRoot = root; _motor = root.GetComponent<PlayerMotor>(); _cc = root.GetComponent<CharacterController>();
        }

        static float PlayerSpeed()
        {
            if (_motor) return _motor.PlanarSpeed;
            if (_cc) { var v = _cc.velocity; v.y = 0f; return v.magnitude; }
            return 1.5f;
        }

        static float DinoStrength(DinosaurController d)
        {
            float size = d.def ? Mathf.Clamp(d.def.bodyRadius * 0.45f, 0.4f, 1.1f) : 0.7f;
            switch (d.State)
            {
                case DinoState.Flee: case DinoState.Chase: case DinoState.Attack: return Mathf.Min(1.3f, size + 0.45f);
                case DinoState.Idle: case DinoState.Rest: case DinoState.Eat: case DinoState.Drink: case DinoState.Observe: return size * 0.6f;
                default: return size;
            }
        }
    }
}
