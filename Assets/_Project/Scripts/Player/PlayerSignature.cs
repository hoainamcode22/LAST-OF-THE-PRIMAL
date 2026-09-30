using UnityEngine;
using PrimalFrontier.AI;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Survival;
using PrimalFrontier.VFX;
using PrimalFrontier.World;

namespace PrimalFrontier.Player
{
    /// <summary>
    /// What the world can notice about the player (perception, AI/PERCEPTION_DESIGN.md 5), sampled 10 times a second:
    /// visibility factors (ambient light, torch / firelight, cave, posture, motion, cover from bushes / thickets / trees /
    /// shelter, clearing exposure), movement noise pulses (gait x surface), player scents (body, bleeding, carried raw
    /// meat) and the smells of lit campfires (cooking, food left on the fire, smoke). Gameplay facts that make noise come
    /// in through GameEvents (gathering, chopping, TreeFelled, combat, building, dodge, fire lit, ProjectileLanded as a
    /// distraction at the landing spot) and the motor / health events (landing, hurt), so no other system is edited for it.
    /// Bleeding = PlayerStatusEffects.Has(Bleeding); footstep surface = PlayerFeedback.LastFootSurface. Creatures read <see cref="Read"/>; the HUD reads the
    /// visibility and noise levels. Allocation free after Start.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerSignature : MonoBehaviour
    {
        /// <summary>the factors creatures combine per species (DinoSenses.Visibility)</summary>
        public struct Snapshot
        {
            public bool valid, alive;
            public Vector3 position, aimPoint;
            public float ambient, local, beacon, caveMul;
            public float posture, motion, cover, exposure;
            public bool crouching, inBush, torch, sheltered, moving;
            public int surface;
            public float time;
            /// <summary>neutral daylight values (no player)</summary>
            public static Snapshot Neutral => new Snapshot { ambient = 1f, beacon = 1f, caveMul = 1f, posture = 1f, motion = 1f, exposure = 1f };
        }

        public static PlayerSignature Instance { get; private set; }
        /// <summary>the live signature (valid = false without a player)</summary>
        public static Snapshot Current;
        static bool _hasTest; static Snapshot _test;
        /// <summary>tests: creatures read this instead of the live player</summary>
        public static void SetTest(in Snapshot s) { _test = s; _test.valid = true; _hasTest = true; }
        public static void ClearTest() { _hasTest = false; }
        public static Snapshot Read => _hasTest ? _test : Current;

        [Tooltip("night vision of the reference observer the HUD shows visibility for")] [Range(0, 1)] public float hudNightVision = 0.2f;
        public float sampleInterval = 0.1f;

        /// <summary>0..1.5 visibility to a reference observer (HUD)</summary>
        public float Visibility { get; private set; }
        /// <summary>0..1 recent player noise (HUD ring), decays over half a second</summary>
        public float NoiseLevel => Mathf.Clamp01(Stimuli.LastPlayerNoise * Mathf.Clamp01(1f - (Stimuli.Now - Stimuli.LastPlayerNoiseTime) / 0.6f));
        /// <summary>current carried meat scent strength (0 = none)</summary>
        public float CarriedScent { get; private set; }
        public int TreesAround { get; private set; }
        public int FelledAround { get; private set; }

        PlayerMotor _motor; PlayerHealth _hp; PlayerEquipment _eq; InventorySystem _inv; PlayerState _state; PlayerSurvival _sv; PlayerClimb _climb;
        PlayerFeedback _feedback; TreeHarvest _trees;
        float _nextSample, _nextPulse, _nextSurface, _nextCover, _nextBody, _nextBleed, _nextCarried, _nextFire, _nextSmoke, _nextLookup;
        float _bushCover, _thicket, _treeCover; bool _clearing; bool _carriedDirty = true;
        static readonly RaycastHit[] Hits = new RaycastHit[4];
        static readonly System.Collections.Generic.Dictionary<int, int> SurfaceByCollider = new System.Collections.Generic.Dictionary<int, int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; Current = default; _hasTest = false; SurfaceByCollider.Clear(); }

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) { Instance = null; Current = default; } }

        void OnEnable()
        {
            _motor = GetComponent<PlayerMotor>(); _hp = GetComponent<PlayerHealth>(); _eq = GetComponent<PlayerEquipment>();
            _inv = GetComponent<InventorySystem>(); _sv = GetComponent<PlayerSurvival>(); TryGetComponent(out _feedback);
            GameEvents.Raised -= OnGameEvent; GameEvents.Raised += OnGameEvent;
            if (_motor) { _motor.Landed -= OnLanded; _motor.Landed += OnLanded; }
            if (_hp) { _hp.Damaged -= OnDamaged; _hp.Damaged += OnDamaged; }
            if (_inv) { _inv.Changed -= OnInventory; _inv.Changed += OnInventory; }
            _carriedDirty = true;
        }

        void OnDisable()
        {
            GameEvents.Raised -= OnGameEvent;
            if (_motor) _motor.Landed -= OnLanded;
            if (_hp) _hp.Damaged -= OnDamaged;
            if (_inv) _inv.Changed -= OnInventory;
            if (Instance == this) Current.valid = false;
        }

        void Start() { _trees = FindFirstObjectByType<TreeHarvest>(); }

        void OnInventory() => _carriedDirty = true;

        // ------------------------------------------------------------------ tick
        void Update() => Tick();

        /// <summary>one update (Update calls it; tests call it directly)</summary>
        public void Tick()
        {
            float now = Stimuli.Now;
            var c = PerceptionConfig.Instance;
            if (now >= _nextSurface) { _nextSurface = now + 0.5f; Current.surface = SampleSurface(); }
            if (now >= _nextCover) { _nextCover = now + 0.25f; SampleCover(c); }
            if (now >= _nextSample) { _nextSample = now + sampleInterval; Sample(c, now); }
            Pulse(c, now);
            Scents(c, now);
        }

        void Sample(PerceptionConfig c, float now)
        {
            ref var s = ref Current;
            s.valid = true; s.time = now;
            s.alive = !_hp || !_hp.IsDead;
            s.position = transform.position;
            float h = _motor && _motor.Controller ? _motor.Controller.height : 1.8f;
            s.aimPoint = s.position + Vector3.up * (h * 0.75f);
            // light
            var tm = TimeManager.Instance; var wm = WeatherManager.Instance;
            float day = tm ? tm.Daylight01 : 1f, night = tm ? tm.NightFactor : 0f;
            float overcast = wm ? wm.Overcast : 0f, rain = wm ? wm.Intensity : 0f;
            s.ambient = Mathf.Lerp(c.nightAmbient, 1f, day) * (1f - c.overcastPenalty * overcast) * (1f - c.rainVisPenalty * rain);
            s.torch = _eq && _eq.TorchLit;
            s.local = Mathf.Max(s.torch ? c.torchLight : 0f, FireSense.LightAt(s.position, c));
            s.beacon = s.torch && night > 0.5f ? c.torchNightBeacon : 1f;
            var zones = ZoneManager.Instance;
            s.caveMul = !s.torch && zones && zones.IsIndoor(s.position) ? c.caveLight : 1f;
            // posture / motion
            if ((!_climb || !_state) && now >= _nextLookup)
            {
                // PlayerClimb is added on the first climb, PlayerState by the animation driver: look again now and then
                _nextLookup = now + 2f;
                if (!_climb) TryGetComponent(out _climb);             // TryGetComponent: no editor allocation when missing
                if (!_state) TryGetComponent(out _state);
            }
            s.crouching = _motor && _motor.IsCrouching;
            s.posture = _climb && _climb.IsClimbing ? c.climbPosture : s.crouching ? c.crouchPosture : 1f;
            float speed = _motor ? _motor.MeasuredPlanarSpeed : 0f;
            s.moving = speed > c.stillSpeed;
            var act = _state ? _state.Activity : PlayerActivity.Idle;
            bool busy = act == PlayerActivity.Attack || act == PlayerActivity.HeavyAttack || act == PlayerActivity.Gather || act == PlayerActivity.Build || act == PlayerActivity.Interact;
            s.motion = busy ? c.runMotion
                : !s.moving ? c.stillMotion
                : s.crouching ? c.crouchWalkMotion
                : _motor && _motor.IsSprinting ? c.sprintMotion
                : speed > c.runSpeedThreshold ? c.runMotion : c.walkMotion;
            // cover (bush / thicket / trees sampled at 4 Hz)
            float bush = _bushCover;
            if (bush > 0f && s.moving) bush *= c.bushMovingMul;
            s.sheltered = Shelter.Covers(s.position + Vector3.up);
            float cover = Mathf.Max(Mathf.Max(bush, _thicket), Mathf.Max(_treeCover, s.sheltered ? c.shelterCover : 0f));
            if (s.torch) cover *= c.torchCoverMul;
            s.cover = cover; s.inBush = _bushCover > 0f;
            s.exposure = _clearing && _bushCover <= 0f ? c.clearingExposure : 1f;
            Visibility = DinoSenses.Visibility(s, hudNightVision);
        }

        void SampleCover(PerceptionConfig c)
        {
            Vector3 p = transform.position;
            var inside = BushInteraction.PlayerBush;
            bool crouch = _motor && _motor.IsCrouching;
            _bushCover = inside ? (crouch ? c.bushCrouchedCover : c.bushCover) : 0f;
            _thicket = Mathf.Min(c.thicketCap, CoverMap.BushesNear(p, c.bushEdge) * c.nearBushCover);
            int trees = CoverMap.TreesNear(p, c.treeRadius, out _);
            _treeCover = Mathf.Min(c.treeCap, trees * c.perTreeCover);
            TreesAround = trees;
            CoverMap.TreesNear(p, c.clearingRadius, out int felled);
            FelledAround = felled;
            _clearing = felled >= c.clearingFelled;
        }

        /// <summary>
        /// Ground under the player: the surface of the last footstep (PlayerFeedback.LastFootSurface, from the real terrain
        /// layer / collider). Fallback without PlayerFeedback, allocation free: collider names classified once per collider,
        /// terrain by a cheap rule (beach height = sand, steep = rock, else grass).
        /// </summary>
        int SampleSurface()
        {
            if (_feedback) return (int)_feedback.LastFootSurface;
            Vector3 p = transform.position;
            if (p.y < 0.35f) return (int)Surface.Water;
            int n = Physics.RaycastNonAlloc(p + Vector3.up * 0.4f, Vector3.down, Hits, 1.2f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; int surface = (int)Surface.Dirt; bool any = false;
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i]; if (!h.collider || h.collider.transform.IsChildOf(transform) || h.distance >= best) continue;
                best = h.distance; any = true;
                if (h.collider is TerrainCollider)
                {
                    float slope = Vector3.Angle(h.normal, Vector3.up);
                    surface = h.point.y < 2.2f ? (int)Surface.Sand : slope > 32f ? (int)Surface.Rock : (int)Surface.Grass;
                }
                else
                {
                    int id = h.collider.GetInstanceID();
                    if (!SurfaceByCollider.TryGetValue(id, out surface)) { surface = (int)SurfaceDetector.FromName((h.collider.sharedMaterial ? h.collider.sharedMaterial.name : "") + " " + h.collider.name); SurfaceByCollider[id] = surface; }
                }
            }
            return any ? surface : (int)Surface.Dirt;
        }

        void Pulse(PerceptionConfig c, float now)
        {
            if (now < _nextPulse) return;
            _nextPulse = now + Mathf.Max(0.05f, c.pulseInterval);
            if (!_motor || !_motor.IsGrounded || (_hp && _hp.IsDead)) return;
            float speed = _motor.MeasuredPlanarSpeed;
            if (speed <= c.stillSpeed) return;
            float gait = _motor.IsCrouching ? c.crouchStep : _motor.IsSprinting ? c.sprintStep : speed > c.runSpeedThreshold ? c.runStep : c.walkStep;
            Stimuli.Noise(transform.position, gait * c.SurfaceLoudness(Current.surface), NoiseTag.Footstep, StimulusSource.Player);
        }

        // ------------------------------------------------------------------ scents
        void Scents(PerceptionConfig c, float now)
        {
            Vector3 p = transform.position;
            bool alive = !_hp || !_hp.IsDead;
            if (alive && now >= _nextBody)
            {
                _nextBody = now + c.bodyScentInterval;
                float wet = _sv ? _sv.Wetness : 0f;
                Stimuli.Scent(p, c.bodyScent * Mathf.Lerp(1f, c.wetBodyMul, wet), ScentKind.Player, StimulusSource.Player);
            }
            if (alive && IsBleeding() && now >= _nextBleed) { _nextBleed = now + c.bleedScentInterval; Stimuli.Scent(p, c.bleedScent, ScentKind.Blood, StimulusSource.Player); }
            if (_carriedDirty) { _carriedDirty = false; CarriedScent = Carried(c); }
            if (alive && CarriedScent > 0f && now >= _nextCarried) { _nextCarried = now + c.carriedScentInterval; Stimuli.Scent(p, CarriedScent, ScentKind.Meat, StimulusSource.Player); }
            if (now >= _nextFire) { _nextFire = now + c.fireScentInterval; FireScents(c, now >= _nextSmoke); if (now >= _nextSmoke) _nextSmoke = now + c.fireSmokeInterval; }
        }

        /// <summary>bleeding: SURV's status effect (PlayerStatusEffects.Has(Bleeding)); PlayerHealth only when there is no effects component</summary>
        bool IsBleeding()
        {
            var fx = PlayerStatusEffects.Player;
            if (fx) return fx.Has(StatusEffectIds.Bleeding);
            return _hp && _hp.IsBleeding;
        }

        float Carried(PerceptionConfig c)
        {
            if (!_inv || _inv.Slots == null) return 0f;
            float sum = 0f; var slots = _inv.Slots;
            for (int i = 0; i < slots.Length; i++)
            {
                var st = slots[i]; if (st == null || !st.item || st.count <= 0) continue;
                if (c.ScentOf(st.item, out var sc) && sc.carriedPerUnit > 0f) sum += sc.carriedPerUnit * st.count;
            }
            return Mathf.Min(c.carriedScentCap, sum);
        }

        /// <summary>every lit campfire: the strongest food on it (+ a little per extra food), burnt food and wood smoke</summary>
        static void FireScents(PerceptionConfig c, bool smoke)
        {
            var all = Campfire.All;
            for (int f = 0; f < all.Count; f++)
            {
                var fire = all[f]; if (!fire || !fire.IsLit) continue;
                float best = 0f, burnt = 0f; int foods = 0;
                for (int i = 0; i < Campfire.MaxSlots; i++)
                {
                    var st = fire.StateOf(i); if (st == Campfire.CookState.Empty) continue;
                    var item = fire.ItemOn(i); if (!item || fire.WaterOn(i) != null) continue;
                    if (st == Campfire.CookState.Burned) { burnt = c.burntSmoke; continue; }
                    if (!c.ScentOf(item, out var sc)) continue;
                    float v = st == Campfire.CookState.Ready ? sc.onFire : sc.cooking;
                    if (v <= 0f) continue;
                    foods++; if (v > best) best = v;
                }
                float mul = FireSense.Sheltered(fire) ? c.shelteredScentMul : 1f;
                Vector3 at = fire.transform.position + Vector3.up * 0.5f;
                if (best > 0f) Stimuli.Scent(at, Mathf.Min(c.fireScentCap, best + c.extraSlotScent * Mathf.Max(0, foods - 1)) * mul, ScentKind.Meat, StimulusSource.World);
                if (burnt > 0f) Stimuli.Scent(at, burnt * mul, ScentKind.Smoke, StimulusSource.World);
                if (smoke) Stimuli.Scent(at, c.fireSmoke * Mathf.Lerp(0.5f, 1f, FireSense.Intensity01(fire)) * mul, ScentKind.Smoke, StimulusSource.World);
            }
        }

        // ------------------------------------------------------------------ noises from gameplay facts
        void OnGameEvent(GameEvent e)
        {
            var c = PerceptionConfig.Instance;
            switch (e.type)
            {
                case GameEventType.ResourceGathered: Gathered(e.position, c); break;
                case GameEventType.CreatureHit: Stimuli.Noise(e.position, c.combatLoudness, NoiseTag.Combat, StimulusSource.Player); break;
                case GameEventType.StructurePlaced: Stimuli.Noise(e.position, c.buildLoudness, NoiseTag.Build, StimulusSource.Player); break;
                case GameEventType.FireLit: Stimuli.Noise(e.position, c.fireLitLoudness, NoiseTag.Fire, StimulusSource.Player); break;
                case GameEventType.PlayerDodged: Stimuli.Noise(transform.position, c.dodgeLoudness, NoiseTag.Landing, StimulusSource.Player); break;
                case GameEventType.TreeFelled: Stimuli.Noise(e.position, c.treeFallLoudness, NoiseTag.TreeFall, StimulusSource.Player); break;
                // a missed arrow / thrown spear lands: heard where it lies, not where the player is (a distraction)
                case GameEventType.ProjectileLanded: Stimuli.Noise(e.position, c.projectileLandLoudness * Mathf.Max(1, e.amount), NoiseTag.Impact, StimulusSource.Distraction); break;
            }
        }

        /// <summary>a gathering hit: loud with an axe on a tree or a pick on stone, quiet by hand</summary>
        void Gathered(Vector3 at, PerceptionConfig c)
        {
            var held = _eq ? _eq.HeldItem : null;
            ToolKind tool = held ? held.tool : ToolKind.None;
            bool onTree = _trees && _trees.Current >= 0 && (_trees.FocusPoint - at).sqrMagnitude < 9f;
            if (onTree || (tool & ToolKind.Chop) != 0 && (tool & ToolKind.Mine) == 0)
            {
                bool axe = (tool & ToolKind.Chop) != 0;
                Stimuli.Noise(at, axe ? c.chopLoudness : c.handGatherLoudness * 2f, NoiseTag.Chop, StimulusSource.Player);
            }
            else if ((tool & ToolKind.Mine) != 0) Stimuli.Noise(at, c.mineLoudness, NoiseTag.Mine, StimulusSource.Player);
            else Stimuli.Noise(at, c.handGatherLoudness, NoiseTag.Gather, StimulusSource.Player);
        }

        void OnLanded(float impact)
        {
            var c = PerceptionConfig.Instance;
            if (impact > 8f) Stimuli.Noise(transform.position, c.hardLandingLoudness, NoiseTag.Landing, StimulusSource.Player);
            else if (impact > 3f) Stimuli.Noise(transform.position, c.landingLoudness, NoiseTag.Landing, StimulusSource.Player);
        }

        void OnDamaged(float amount, Vector3 from, bool heavy)
        {
            var c = PerceptionConfig.Instance;
            Stimuli.Noise(transform.position, c.hurtLoudness * (heavy ? 1.35f : 1f), NoiseTag.Voice, StimulusSource.Player);
        }

        /// <summary>forget cached state (new game / load)</summary>
        public void ResetState() { _carriedDirty = true; }
    }
}
