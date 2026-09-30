using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Audio;
using PrimalFrontier.Combat;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.VFX;
using PrimalFrontier.World;

namespace PrimalFrontier.AI
{
    public enum DinoState { Idle, Wander, Eat, Drink, Rest, Observe, Alert, Investigate, Flee, Chase, Attack, Return, Dead }

    /// <summary>
    /// Lightweight dinosaur brain (FSM) + kinematic movement on the terrain. Perception lives in <see cref="DinoSenses"/>
    /// (awareness meter with memory, fed by sight, hearing, motion and scent on timed ticks); this class turns the
    /// awareness level into behaviour. Herbivores graze, drink, sleep, look up at the player, pause eating, move away,
    /// ignore a calm player and go back to feeding, raise alarms and flee; defensive ones hold their ground and charge up
    /// close. A herd member (HerdGroup) follows its herd's day and keeps a loose place in it. Predators ignore, watch
    /// herds from a distance, investigate, stalk (freezing when watched), chase, attack and retreat, and follow the last
    /// known position instead of the live player once sight is lost; they eat at carcasses. Per species FireFear,
    /// NightFear, Aggression and Investigation (DinosaurDefinition) shape all of it. Weight: heavy animals speed up, brake
    /// and pivot slowly and shake the ground; small ones dart. Creatures leave tracks (TrackSigns). AI tiers by distance
    /// to the player (PerceptionConfig): near / medium / far think at 0.2 / 0.4 / 1 s (far animals animate at a reduced
    /// rate), very far is frozen with the Animator off. The Animator runs in place (Speed matches the real speed).
    /// </summary>
    public class DinosaurController : MonoBehaviour, IDamageable
    {
        public DinosaurDefinition def;
        [Tooltip("centre of its territory. (0,0,0) = where it is placed in the scene")]
        public Vector3 home; public float homeRadius = 40f;
        public DinoState State { get; private set; } = DinoState.Idle;
        public float Health { get; private set; }
        public bool IsAlive => State != DinoState.Dead;
        public static readonly List<DinosaurController> All = new List<DinosaurController>();
        /// <summary>perception of this creature (awareness, memory, look point)</summary>
        public DinoSenses Senses { get; } = new DinoSenses();
        /// <summary>AI distance tier: 0 near, 1 medium, 2 far, 3 very far</summary>
        public int Tier => _lod;
        /// <summary>asleep (rest phase of its day): weaker senses, eyes closed</summary>
        public bool Sleeping => _sleeping;
        /// <summary>what it does about a lit campfire right now (None = nothing)</summary>
        public FireMode FireBehaviour => _fireMode;
        /// <summary>game clock time of death (-1 = alive / unknown)</summary>
        public double DiedAt { get; private set; } = -1;
        /// <summary>being pushed back by a hit (bare-hand knockback) and the push speed (m/s)</summary>
        public bool IsKnockedBack => Time.time < _pushUntil;
        public float PushSpeed => _push.magnitude;
        /// <summary>where it is walking to (investigation, search, drink, wander)</summary>
        public Vector3 Destination => _dest;
        public enum FireMode { None, Wait, Circle, Avoid, Observe }
        /// <summary>current ground speed (m/s), turn rate (deg/s, + = right) and acceleration (m/s^2): DinoLife sways the body with them</summary>
        public float CurrentSpeed => _speed;
        public float YawRate { get; private set; }
        public float Accel { get; private set; }
        /// <summary>the herd it belongs to (set by HerdGroup), null for loners and predators</summary>
        public HerdGroup Herd { get; internal set; }
        /// <summary>feeding while it keeps an eye on a calm player at a distance (ignoring the player)</summary>
        public bool IgnoringPlayer => Stimuli.Now < _ignoreUntil;
        /// <summary>a predator watching a herd from a distance</summary>
        public bool WatchingPrey => _watchHerd && Stimuli.Now < _watchUntil;
        /// <summary>eating at a carcass (predators)</summary>
        public bool EatingCarcass => State == DinoState.Eat && _atCarcass;
        // herd place (HerdGroup): loose slot in the unit disc, own pace, idle timing, drink session done
        internal Vector2 HerdSlot; internal float HerdSpeedMul = 1f, HerdIdleMul = 1f, HerdReslotAt; internal int HerdDrankSession = -1;
        /// <summary>its own walking pace in the herd (x walk speed)</summary>
        public float HerdPace => HerdSpeedMul;
        /// <summary>hunting (Phase 1): the herbivore this predator is after (null = none), and whether this animal is being hunted</summary>
        public DinosaurController HuntTarget => _prey;
        public bool Hunting => _prey;
        public bool Hunted => _hunter && _hunter.HuntTarget == this;
        /// <summary>predators: hunger 0..1 (rises with game time, a meal resets it)</summary>
        public float Hunger01 => _hunger;
        /// <summary>what this creature's Animator offers (rest clips, Breathe, Intensity ...), found at runtime</summary>
        public DinoAnimCaps AnimCaps => _caps;

        Animator _anim; AudioSource _audio; CharacterAnimationEvents _ev;
        float _stateT, _think, _speed, _attackReady, _nextCall, _lodT, _lastThink;
        Vector3 _dest; Transform _player; PlayerHealth _playerHp; PlayerMotor _playerMotor;
        Vector3 _threat; bool _provoked; int _lod; bool _pendingHit; float _hitAt; bool _heavy;
        bool _attackQueued; float _tellUntil, _tellTime;
        bool _started; bool _hasPending; SavedState _pending;
        /// <summary>0..1 while winding up an attack (the readable tell before the strike), else 0</summary>
        public float Telegraph => _attackQueued && _tellTime > 0f ? Mathf.Clamp01(1f - (_tellUntil - Time.time) / _tellTime) : 0f;
        public bool HeavyAttack => _heavy;
        /// <summary>the player while it can see (or is still tracking) the player in an alert state, else null</summary>
        public Transform LookTarget => _player && !_prey && PlayerAlive && (State == DinoState.Observe || State == DinoState.Alert || State == DinoState.Investigate ||
                                       State == DinoState.Chase || State == DinoState.Attack) && PlayerDist < 40f && Tracking && !(Stimuli.Now < _lookOverrideUntil && !Senses.SeesPlayer) ? _player : null;
        /// <summary>where it looks when the player is not the target: a hunter the herd watches, a watched herd, the last stimulus</summary>
        public bool TryGetLookPoint(out Vector3 p)
        {
            if (IsAlive && _prey && _prey.def) { p = _prey.transform.position + Vector3.up * _prey.def.bodyRadius; return true; }
            if (IsAlive && Stimuli.Now < _lookOverrideUntil) { p = _lookOverride; return true; }
            p = Senses.LookPoint;
            return IsAlive && Senses.HasLookPoint && (State == DinoState.Observe || State == DinoState.Alert || State == DinoState.Investigate);
        }
        [Tooltip("wind-up before a normal / heavy attack, seconds (time for the player to react)")]
        public Vector2 attackTell = new Vector2(0.32f, 0.55f);
        static readonly HashSet<string> Sighted = new HashSet<string>();
        static readonly int SpeedH = AnimParams.Speed, ActionTypeH = AnimParams.ActionType, ActionH = AnimParams.Action, AttackH = AnimParams.Attack,
            AttackTypeH = AnimParams.AttackType, HurtH = AnimParams.Hurt, DeadH = AnimParams.Dead, AlertH = AnimParams.Alert;

        bool Herbivore => def.temperament == Temperament.Passive || def.temperament == Temperament.Defensive;
        public static void ResetSightings() => Sighted.Clear();
        float _zigPhase, _corpseAnimT;
        const float ZigZagRate = 2.1f;               // rad/s: one side-to-side swing every ~3 s
        const float CorpseAnimSeconds = 6f;          // death clip time before a mid-distance corpse stops animating
        const float FarAnimHz = 20f;                 // far tier: the Animator is stepped by hand at this rate
        /// <summary>cached layer masks (nested static class: initialised on first use from Update, never during deserialisation)</summary>
        static class Masks { internal static readonly int NotPlayer = ~LayerMask.GetMask("Player"); }
        static PerceptionConfig C => PerceptionConfig.Instance;
        static WildlifeConfig W => WildlifeConfig.Instance;

        // behaviour memory
        enum Goal { None, Threat, Approach, Food, Search, Drink, Avoid, Herd, WatchPrey, Rush, Hunt, Routine }
        Goal _goal; int _searchLeft; bool _keepDest; float _goalMemTime = -99f; bool _fireOnPlayer;
        AwarenessLevel _prevLevel; float _lastShare = -99f, _lastNotice = -99f, _lastSuspiciousCall = -99f;
        // daily life
        bool _sleeping; double _nextDrinkAt; bool _hasDrinkSpot; Vector3 _drinkSpot; float _nextDrinkLook, _drinkWalk = 60f;
        bool _herdDrinking, _drankTwice; float _herdSpeed = 1.5f; double _nextDroppingsAt;
        // campfire
        FireMode _fireMode; Campfire _fire; bool _fireArrived; float _firePatience; float _fireR, _fireUntil, _fireIgnoreUntil = -99f, _fireLeftUntil = -99f, _circleAngle, _circleDir = 1f, _provokedAt = -99f;
        bool _fireInvestigate, _fireAttackAnyway;
        // knockback
        Vector3 _push; float _pushUntil, _pushDur;
        // reactions to the player (herbivores) and to hunters near the herd
        float _tolerateAt = -1f, _ignoreUntil = -99f, _nextLookUp, _lookUpUntil = -99f, _lastPlayerDist = -1f, _approachRate;
        float _fleeAt = -1f; Vector3 _fleeFrom; float _threatLookUntil = -99f;
        Vector3 _lookOverride; float _lookOverrideUntil = -99f;
        // predators
        float _stalkRoll = -1f, _stalkFreezeUntil = -99f, _nextFreeze = -99f, _observeGiveUp = -99f, _ignorePlayerUntil = -99f;
        HerdGroup _watchHerd; float _watchUntil = -99f; bool _atCarcass;
        // individual
        float _indivSpeed = 1f, _indivTurn = 1f, _idleMul = 1f;
        // hunting herbivores (Phase 1): hunter side, prey side, the meal and the rest after it
        HuntProfile _hp; DinosaurController _prey, _hunter; float _hunger, _huntT, _nextHuntCheck; int _huntStrikes; bool _huntHitPending;
        Carcass _meal; bool _mealRest; double _restUntil = -1;
        // animation capabilities (rest clips, Breathe, Intensity), getting up without sliding, panting after a run, weather shelter
        readonly DinoAnimCaps _caps = new DinoAnimCaps(); bool _restHold; float _restHoldUntil, _runT;
        bool _hasShelter; Vector3 _shelter;
        static readonly int IntensityH = DinoAnimCaps.IntensityHash;
        // weight / tracks
        float _prevSpeed, _stepDist, _bloodDist; bool _stepLeft; int _stepCount; float _animAcc;
        // Phase 2: far herds (seen by the camera), routines (WildlifeRoutine), wading (shallow water allowed near a stop)
        Renderer[] _rends; float _routineSpeed = 1f; Vector3 _wadeC; float _wadeR, _wadeUntil = -1f;

        void Awake()
        {
            _anim = GetComponent<Animator>(); if (!_anim) _anim = GetComponentInChildren<Animator>();
            if (_anim)
            {
                _anim.keepAnimatorStateOnDisable = true;          // the far tiers switch the Animator off and on: keep the pose and state
                if (_anim.cullingMode == AnimatorCullingMode.AlwaysAnimate) _anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            }
            _ev = GetComponentInChildren<CharacterAnimationEvents>();
            if (!GetComponent<DinoLife>()) gameObject.AddComponent<DinoLife>();     // head look, attack tell, eyes, body sway
            _audio = GetComponent<AudioSource>();
            if (!_audio) { _audio = gameObject.AddComponent<AudioSource>(); _audio.spatialBlend = 1f; _audio.rolloffMode = AudioRolloffMode.Linear; _audio.maxDistance = 120f; _audio.minDistance = 4f; _audio.playOnAwake = false; }
        }
        void OnEnable() { if (State != DinoState.Dead && !All.Contains(this)) All.Add(this); if (_ev) _ev.AnimationEventRaised += OnAnimEvent; GameEvents.Raised += OnGameEvent; }
        void OnDisable() { All.Remove(this); if (_ev) _ev.AnimationEventRaised -= OnAnimEvent; GameEvents.Raised -= OnGameEvent; }

        void Start()
        {
            if (def == null) { enabled = false; return; }
            Health = def.maxHealth;
            if (home == Vector3.zero) home = transform.position;
            _dest = transform.position; _think = Random.value * 0.3f; _nextCall = Time.time + Random.Range(8f, 30f);
            _zigPhase = Random.value * Mathf.PI * 2f; _circleDir = Random.value < 0.5f ? -1f : 1f;
            _nextDrinkAt = GameClock.Now + GameClock.Hours(Mathf.Max(0.5f, def.drinkEveryHours) * Random.Range(0.2f, 1f));
            // no two animals alike: own pace, turning and patience
            _indivSpeed = Random.Range(0.93f, 1.07f); _indivTurn = Random.Range(0.88f, 1.12f); _idleMul = Random.Range(0.75f, 1.3f);
            _hp = def.Hunt; _hunger = _hp.hunts ? Random.Range(0.1f, 0.5f) : 0f; _nextHuntCheck = Time.time + Random.Range(2f, 8f);
            if (_anim) _caps.Resolve(_anim);
            ExtendCull(gameObject, def.IsHerbivore ? C.herdCullDistance : C.creatureCullDistance);
            var w = W; _nextDroppingsAt = GameClock.Now + GameClock.Hours(Random.Range(w.droppingsHours.x, w.droppingsHours.y));
            _lastThink = Time.time;
            Senses.ForgetAll();
            Snap();
            Enter(Random.value < 0.5f ? DinoState.Eat : DinoState.Idle);
            _started = true;
            if (_hasPending) { _hasPending = false; ApplySaved(_pending); }
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Vector3 c = home != Vector3.zero ? home : transform.position;
            Gizmos.color = def && (def.temperament == Temperament.Predator || def.temperament == Temperament.Territorial) ? new Color(1f, 0.3f, 0.2f) : new Color(0.3f, 1f, 0.4f);
            DinosaurSpawner.DrawCircle(c, homeRadius);
            if (def && def.sightRange > 0f) { Gizmos.color = new Color(1f, 1f, 1f, 0.25f); DinosaurSpawner.DrawCircle(transform.position, Application.isPlaying ? Senses.EffectiveSight : def.sightRange); }
            if (def && def.hearingRange > 0f) { Gizmos.color = new Color(0.4f, 0.7f, 1f, 0.2f); DinosaurSpawner.DrawCircle(transform.position, def.hearingRange); }
            if (Application.isPlaying && Senses.HasMemory) { Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(Senses.LastKnownPos, 0.6f); Gizmos.DrawLine(transform.position + Vector3.up, Senses.LastKnownPos); }
            if (Application.isPlaying) { Gizmos.color = Color.Lerp(Color.green, Color.red, Senses.Awareness); Gizmos.DrawCube(transform.position + Vector3.up * (def ? def.bodyRadius * 2.6f : 3f), new Vector3(Senses.Awareness * 2f + 0.05f, 0.15f, 0.15f)); }
        }
#endif

        // ------------------------------------------------------------------ player
        bool FindPlayer()
        {
            if (_player) return true;
            if (!PlayerLocator.Player) return false;
            _player = PlayerLocator.Player; _playerHp = _player.GetComponent<PlayerHealth>(); _playerMotor = _player.GetComponent<PlayerMotor>();
            return true;
        }
        float PlayerDist => _player ? Vector3.Distance(transform.position, _player.position) : 9999f;
        bool PlayerAlive => _playerHp && !_playerHp.IsDead;
        /// <summary>sees the player, lost sight less than trackGrace ago, follows by smell, or was just hit</summary>
        bool Tracking => Senses.SeesPlayer || Senses.ScentTracking || Stimuli.Now - Senses.LastSeenTime <= C.trackGrace;
        /// <summary>centre of where it lives right now (the herd's centre for a herd member)</summary>
        Vector3 HomeNow => Herd ? Herd.Anchor : home;
        float Indiv => Herd ? HerdSpeedMul : _indivSpeed;
        static float NightK { get { var tm = TimeManager.Instance; return tm ? tm.NightFactor : 0f; } }

        // ------------------------------------------------------------------ brain
        Transform[] _bones; float _nextDrip;

        void Update()
        {
            if (def == null) return;
            FindPlayer();
            var c = C;
            float dist = PlayerDist;
            // the migrating herd stays animated further out (the crossing is watched from the lookout, 150-240 m away)
            float tierDist = Herd && Herd.OnRoute ? dist * 0.8f : dist;
            int lod = tierDist < c.nearDistance ? 0 : tierDist < c.mediumDistance ? 1 : tierDist < c.farDistance ? 2 : 3;
            // Phase 2: a herd in view keeps walking and animating (at a lower rate) out to herdVisibleDistance instead of freezing
            if (lod == 3 && Herd && dist < c.herdVisibleDistance && SeenByCamera()) lod = 2;
            if (lod != _lod) { _lod = lod; if (_anim && State != DinoState.Dead) _anim.enabled = lod < 2; }
            if (State == DinoState.Dead) { CorpseAnimator(lod); return; }
            if (lod == 3) { _lodT += Time.deltaTime; if (_lodT < c.veryFarStep) return; }
            float dt = lod == 3 ? _lodT : Time.deltaTime; _lodT = 0f;
            _stateT += dt; _think -= dt;
            if (_fleeAt >= 0f && Time.time >= _fleeAt) { _fleeAt = -1f; if (State != DinoState.Flee && State != DinoState.Chase && State != DinoState.Attack) FleeFrom(_fleeFrom, false); }
            if (_think <= 0f)
            {
                _think = lod == 0 ? c.thinkNear : lod == 1 ? c.thinkMedium : c.thinkFar;
                float tdt = Mathf.Clamp(Time.time - _lastThink, 0.01f, 2f); _lastThink = Time.time;
                Think(dist, tdt);
            }
            float yaw0 = transform.eulerAngles.y;
            Act(dt, dist);
            Move(dt);
            // turn rate and acceleration (DinoLife leans, sways and swings the tail with them)
            YawRate = Mathf.Lerp(YawRate, Mathf.DeltaAngle(yaw0, transform.eulerAngles.y) / Mathf.Max(0.001f, dt), Mathf.Clamp01(dt * 8f));
            Accel = Mathf.Lerp(Accel, (_speed - _prevSpeed) / Mathf.Max(0.001f, dt), Mathf.Clamp01(dt * 6f)); _prevSpeed = _speed;
            if (_anim)
            {
                if (_anim.enabled)
                {
                    _anim.SetFloat(SpeedH, _speed, 0.12f, dt);
                    if (!_caps.Resolved) _caps.Resolve(_anim);
                    // run slot body: Chase (hunters) / Flee (grazers) clip when the controller has it
                    if (_caps.Intensity) _anim.SetFloat(IntensityH, (State == DinoState.Chase && !Herbivore) || State == DinoState.Flee ? 1f : 0f, 0.3f, dt);
                }
                else if (lod == 2)
                {
                    // far: step the Animator by hand at a lower rate (the herd across the valley still walks, for less)
                    _anim.SetFloat(SpeedH, _speed);
                    _animAcc += Time.deltaTime;
                    float hz = dist > c.farDistance ? c.farHerdAnimHz : c.farAnimHz;
                    if (_animAcc >= 1f / Mathf.Max(1f, hz > 0f ? hz : FarAnimHz)) { _anim.Update(_animAcc); _animAcc = 0f; }
                }
            }
            if (_pendingHit && Time.time >= _hitAt) ResolveHit();
            // time spent running (a long run ends with hard breathing: Breathe)
            if (_speed > def.walkSpeed * 1.8f) _runT += dt; else if (_speed < def.walkSpeed) _runT = Mathf.Max(0f, _runT - dt * 0.25f);
            // badly hurt: a blood trail behind it while it moves
            if (lod == 0 && _speed > 0.5f && Health < def.maxHealth * 0.4f && Time.time > _nextDrip)
            {
                _nextDrip = Time.time + Random.Range(0.5f, 1.1f);
                BloodFX.Drip(transform.position - transform.forward * def.bodyRadius * 0.3f + transform.right * Random.Range(-0.3f, 0.3f) * def.bodyRadius, def.bodyRadius * 0.6f, transform);
            }
            if (Time.time > _nextCall && lod <= 1 && State != DinoState.Chase && !_sleeping && !_prey)
            {
                // all creatures call less in rain and storms (WildlifeWeather.CallMul)
                _nextCall = Time.time + Random.Range(15f, 40f) * (Herd ? 1.4f : 1f) / Mathf.Max(0.05f, WildlifeWeather.CallMul);
                PlayClip(def.calls, 0.8f);
                if (Herbivore && _speed < 0.3f && Calm && State != DinoState.Rest && Random.value < 0.5f && _anim) { _anim.SetInteger(ActionTypeH, DinoActions.Call); _anim.SetTrigger(ActionH); }
            }
        }

        bool Calm => State == DinoState.Idle || State == DinoState.Wander || State == DinoState.Eat || State == DinoState.Rest || State == DinoState.Drink;

        void Think(float dist, float dt)
        {
            var c = C;
            Life(c, dt);
            Senses.Tick(this, dt, _lod, c);
            var lvl = Senses.Level; bool see = Senses.SeesPlayer;
            if (see && dist < 35f && Sighted.Add(def.id)) GameEvents.Raise(GameEventType.CreatureSighted, def.id, 1, transform.position);
            if (lvl > _prevLevel) LevelRose(_prevLevel, lvl, c);
            _prevLevel = lvl;
            // how fast the player is closing in (m/s), while it can see the player
            if (see) { if (_lastPlayerDist >= 0f) _approachRate = Mathf.Lerp(_approachRate, (_lastPlayerDist - dist) / Mathf.Max(0.05f, dt), 0.5f); _lastPlayerDist = dist; }
            else { _lastPlayerDist = -1f; _approachRate = 0f; }
            if (lvl == AwarenessLevel.Unaware) _stalkRoll = -1f;
            if (_sleeping && lvl >= AwarenessLevel.Suspicious) WakeUp();
            if (Stimuli.Now - _provokedAt > 20f && State != DinoState.Chase && State != DinoState.Attack && State != DinoState.Flee) _provoked = false;
            if (FireCheck(c, dist)) return;
            bool hurt = Health < def.maxHealth * def.retreatHealth;
            Vector3 threatPos = see && _player ? _player.position : Senses.HasMemory ? Senses.LastKnownPos : _player ? _player.position : transform.position;
            float now = Stimuli.Now;
            switch (def.temperament)
            {
                case Temperament.Passive:
                {
                    // flee: hit, too close, alert and close in view, or a threat it knows is near but can no longer see
                    float personal = def.personalSpace * (1f + 0.5f * def.NightFear * NightK);
                    bool close = see && dist < personal * 1.6f && lvl >= AwarenessLevel.Alerted;
                    float memDist = Senses.HasMemory ? Flat(Senses.LastKnownPos - transform.position) : float.MaxValue;
                    bool heardClose = !see && lvl >= AwarenessLevel.Alerted && memDist < personal;
                    bool lostClose = !see && lvl == AwarenessLevel.Engaged && memDist < personal * 2.5f;
                    if (_provoked || close || heardClose || lostClose || (see && dist < personal * 0.6f)) { FleeFrom(threatPos, true); return; }
                    if (State == DinoState.Flee) return;
                    if (lvl >= AwarenessLevel.Investigating && !see && Senses.HasMemory) { if (_goal != Goal.Avoid) WalkAway(Senses.LastKnownPos, Goal.Avoid); return; }
                    if (lvl >= AwarenessLevel.Suspicious && ReactToPlayer(see, dist, personal, false)) return;
                    if (Senses.HasAversion && Calm && _goal != Goal.Avoid) { WalkAway(Senses.AversionPos, Goal.Avoid); return; }
                    break;
                }
                case Temperament.Defensive:
                {
                    float charge = def.personalSpace * Mathf.Lerp(0.7f, 1.4f, def.Aggression) * (1f + 0.3f * def.NightFear * NightK);
                    if (_provoked || (lvl == AwarenessLevel.Engaged && see && dist < charge))
                    {
                        if (hurt) { FleeFrom(threatPos, true); return; }
                        if (State != DinoState.Chase && State != DinoState.Attack) { Roar(); Enter(DinoState.Chase); }
                        return;
                    }
                    if (State == DinoState.Flee || State == DinoState.Chase || State == DinoState.Attack) break;
                    if (lvl >= AwarenessLevel.Suspicious && ReactToPlayer(see, dist, charge, true)) return;
                    if (Senses.HasAversion && Calm && _goal != Goal.Avoid) { WalkAway(Senses.AversionPos, Goal.Avoid); return; }
                    break;
                }
                case Temperament.Territorial:
                case Temperament.Predator:
                {
                    // badly hurt: hunters retreat (only the most aggressive territorial giants fight on)
                    if (hurt && (def.temperament == Temperament.Predator || def.Aggression < 0.85f)) { if (_prey) EndHunt(false); FleeFrom(_player ? _player.position : transform.position - transform.forward, false); return; }
                    if (State == DinoState.Flee) return;
                    if (_goal == Goal.Rush && State == DinoState.Investigate) return;          // testing a herd: let the rush run
                    Vector3 pp = _player ? _player.position : home;
                    bool inTerritory = def.temperament == Temperament.Predator || Flat(pp - home) < def.territoryRadius * Mathf.Lerp(0.8f, 1.15f, def.Aggression);
                    bool tracking = Tracking;
                    float aggro = def.aggroRange * Mathf.Lerp(0.65f, 1.3f, def.Aggression);
                    if (PlayerAlive && ((lvl == AwarenessLevel.Engaged && tracking && dist < aggro && inTerritory) || _provoked))
                    {
                        CancelWatch(); if (_prey) EndHunt(false);
                        if (State != DinoState.Chase && State != DinoState.Attack) { if (State != DinoState.Alert && dist > def.attackRange * 3f) { Enter(DinoState.Alert); Roar(); } else Enter(DinoState.Chase); }
                        return;
                    }
                    // hunting a herbivore: the hunt runs its course unless the player became a real threat
                    if (_prey) { if (lvl >= AwarenessLevel.Alerted) EndHunt(false); else if (HuntThink(dt)) return; }
                    if (State == DinoState.Chase || State == DinoState.Attack || State == DinoState.Alert) break;
                    if (PlayerAlive && lvl == AwarenessLevel.Engaged && tracking && now >= _ignorePlayerUntil)
                    {
                        CancelWatch();
                        // seen but still far: an aggressive hunter stalks closer (inside its territory); a cautious one watches, then loses interest
                        if (_stalkRoll < 0f) _stalkRoll = Random.value;
                        bool stalks = _stalkRoll < 0.25f + 0.75f * def.Aggression;
                        if (stalks && (inTerritory || Flat(pp - home) < def.territoryRadius * 1.3f))
                        {
                            GoInvestigate(pp, Goal.Approach);
                            StalkFreeze(see, dist);
                            return;
                        }
                        if (State != DinoState.Observe) { Enter(DinoState.Observe); _observeGiveUp = now + Random.Range(12f, 25f); }
                        else if (now > _observeGiveUp && _observeGiveUp > 0f) { _ignorePlayerUntil = now + 40f; _observeGiveUp = -99f; Enter(DinoState.Return); }
                        return;
                    }
                    if (lvl >= AwarenessLevel.Investigating && Senses.HasMemory)
                    {
                        CancelWatch();
                        // a curious hunter walks to the noise; an incurious one only looks towards it unless it is close
                        bool curious = def.Investigation >= 0.35f || Flat(Senses.LastKnownPos - transform.position) < 20f || Senses.LastSense == SenseKind.Sight || Senses.LastSense == SenseKind.Damage;
                        if (!curious) { if (State != DinoState.Observe) Enter(DinoState.Observe); return; }
                        bool newInfo = Senses.LastKnownTime > _goalMemTime + 0.5f && (_dest - Senses.LastKnownPos).sqrMagnitude > 16f;
                        if ((_goal != Goal.Threat && _goal != Goal.Search) || State != DinoState.Investigate || newInfo) GoInvestigate(Senses.LastKnownPos, Goal.Threat);
                        return;
                    }
                    if (lvl == AwarenessLevel.Suspicious && Calm) { CancelWatch(); Enter(DinoState.Observe); return; }
                    float interestAt = c.interestThreshold * Mathf.Lerp(1.4f, 0.7f, def.Investigation);
                    if (Senses.HasInterest && Senses.Interest >= interestAt && (Calm || State == DinoState.Observe) && Stimuli.Now > _fireLeftUntil)
                    {
                        CancelWatch();
                        if (_goal != Goal.Food) GameEvents.Raise(GameEventType.ScentInvestigated, def.id, 1, Senses.InterestPos);
                        GoInvestigate(Senses.InterestPos, Goal.Food); return;
                    }
                    if ((Calm || WatchingPrey) && lvl == AwarenessLevel.Unaware && TryStartHunt()) return;
                    if (WatchPreyThink()) return;
                    break;
                }
            }
            if (Herd && Herbivore && HerdThink()) return;
            if (State == DinoState.Observe && lvl == AwarenessLevel.Unaware && _stateT > 2f && Stimuli.Now > _threatLookUntil && Stimuli.Now > _lookUpUntil && !WatchingPrey) Enter(DinoState.Idle);
            if (State == DinoState.Chase)
            {
                bool leash = Vector3.Distance(transform.position, HomeNow) > def.territoryRadius * 2.5f + (Herd ? 40f : 0f);
                float giveUp = Herbivore ? Mathf.Max(def.personalSpace * 2f, 8f) : def.aggroRange * Mathf.Lerp(0.65f, 1.3f, def.Aggression) * 1.8f;
                if (!PlayerAlive || dist > giveUp || leash)
                {
                    _provoked = false;
                    if (!Herbivore && !leash && PlayerAlive && Senses.HasMemory) GoInvestigate(Senses.LastKnownPos, Goal.Search);
                    else Enter(Herbivore ? DinoState.Observe : DinoState.Return);
                }
            }
        }

        /// <summary>
        /// A herbivore that sees or senses the player (directive 45): looks up and pauses eating, walks away (the herd with
        /// it) when the player comes inside its comfort distance, and after a while goes back to feeding if the player keeps
        /// still and keeps away, looking up now and then. Defensive species hold their ground instead of walking off.
        /// Returns true when the reaction decided this tick; false = calm behaviour goes on (ignoring the player).
        /// </summary>
        bool ReactToPlayer(bool see, float dist, float personal, bool defensive)
        {
            var w = W; float now = Stimuli.Now;
            if (!see)
            {
                if (now < _ignoreUntil) return false;
                if (State != DinoState.Observe && Calm) Enter(DinoState.Observe);
                return State == DinoState.Observe;
            }
            float comfort = Mathf.Max(personal * 1.8f, def.observeDistance * w.comfortFactor) * (1f + 0.3f * def.NightFear * NightK);
            var snap = PlayerSignature.Read;
            bool threatening = (_approachRate > w.approachSpeed || snap.motion >= C.runMotion - 0.01f) && dist < def.observeDistance;
            if (dist < comfort || (threatening && dist < comfort * 1.4f))
            {
                _ignoreUntil = -99f;
                if (defensive && def.Aggression >= 0.5f)
                {
                    // stands its ground, faces the player, a warning call and a shake of the head
                    if (State != DinoState.Observe) { Enter(DinoState.Observe); if (dist < comfort * 0.8f && _anim) { _anim.SetInteger(ActionTypeH, DinoActions.Threaten); _anim.SetTrigger(ActionH); PlayClip(def.calls, 0.7f); } }
                    return true;
                }
                if (_goal != Goal.Avoid || State != DinoState.Wander)
                {
                    WalkAway(_player.position, Goal.Avoid);
                    if (Herd) Herd.Disturb(_player.position, threatening ? 1.5f : 1f);
                }
                return true;
            }
            if (now < _ignoreUntil && !threatening)
            {
                // feeding while it keeps an eye on the player: look up now and then
                if (State == DinoState.Observe && now >= _lookUpUntil) { Enter(DinoState.Eat); _nextLookUp = now + Random.Range(w.lookUpEvery.x, w.lookUpEvery.y); }
                else if (State == DinoState.Eat && now >= _nextLookUp) { Enter(DinoState.Observe); _lookUpUntil = now + Random.Range(1.5f, 3f); }
                return State == DinoState.Observe;
            }
            if (State != DinoState.Observe) { Enter(DinoState.Observe); }
            if (_tolerateAt < 0f) _tolerateAt = now + Random.Range(w.toleranceSeconds.x, w.toleranceSeconds.y) * (1f - def.Investigation / 3f) * _idleMul;
            if (!threatening && now >= _tolerateAt)
            {
                // it has seen enough: back to feeding
                _ignoreUntil = now + Random.Range(w.ignoreSeconds.x, w.ignoreSeconds.y);
                _nextLookUp = now + Random.Range(w.lookUpEvery.x, w.lookUpEvery.y);
                Enter(DinoState.Eat);
                return false;
            }
            return true;
        }

        /// <summary>awareness went up: wake, call, share with the herd / pack, tell the game once in a while</summary>
        void LevelRose(AwarenessLevel from, AwarenessLevel to, PerceptionConfig c)
        {
            float now = Stimuli.Now;
            if (to >= AwarenessLevel.Suspicious && from < AwarenessLevel.Suspicious)
            {
                if (now - _lastNotice > 10f && (Senses.LastSense == SenseKind.Sight || Senses.LastSense == SenseKind.Noise || Senses.LastSense == SenseKind.Motion || Senses.LastSense == SenseKind.Scent))
                { _lastNotice = now; GameEvents.Raise(GameEventType.PlayerNoticed, def.id, (int)to, transform.position); }
                if (now - _lastSuspiciousCall > 8f && Random.value < 0.4f * WildlifeWeather.CallMul && _lod == 0) { _lastSuspiciousCall = now; PlayClip(def.calls, 0.45f); }
                // lying down it lifts its head and looks (Rest_Shift) instead of getting up for a look
                if (_anim && Calm) { _anim.SetInteger(ActionTypeH, State == DinoState.Rest && _caps.RestShift ? DinoActions.Rest : DinoActions.LookAround); _anim.SetTrigger(ActionH); }
            }
            if (to >= AwarenessLevel.Alerted && from < AwarenessLevel.Alerted && now - _lastShare > c.shareCooldown)
            {
                _lastShare = now;
                if (Herbivore && def.alarmCall && !IgnoringPlayer) PlayClip(def.calls, 1f);
                if (!IgnoringPlayer) ShareAlert(to == AwarenessLevel.Engaged && !Herbivore ? 1f : c.herdShareLevel, c);
            }
        }

        void ShareAlert(float level, PerceptionConfig c)
        {
            if (!Senses.HasMemory) return;
            Vector3 p = transform.position, at = Senses.LastKnownPos;
            float r = def.herdShareRadius, r2 = r * r, a2 = r * r * 0.25f;
            for (int i = 0; i < All.Count; i++)
            {
                var d = All[i]; if (d == this || !d || !d.def || !d.IsAlive) continue;
                float q = (d.transform.position - p).sqrMagnitude;
                bool sameHerd = Herd && d.Herd == Herd;
                if (d.def == def) { if ((r > 0f && q < r2) || sameHerd) d.Senses.Share(level, at, c); }
                else if (Herbivore && def.alarmCall && d.Herbivore && q < a2) d.Senses.Share(c.alarmShareLevel, at, c);
            }
        }

        // ------------------------------------------------------------------ daily life
        bool Resting(float hour)
        {
            var tm = TimeManager.Instance; if (!tm) return false;
            switch (def.activity)
            {
                case ActivityCycle.Diurnal: return tm.NightFactor > 0.7f || tm.CurrentPhase == TimeManager.Phase.Night;          // the whole night phase
                case ActivityCycle.Nocturnal: return hour >= 11f && hour < 15f;
                default: return false;
            }
        }

        void Life(PerceptionConfig c, float dt)
        {
            var tm = TimeManager.Instance;
            WildlifeWeather.Refresh();
            bool rest = Herd ? Herd.Activity == HerdActivity.Sleep : tm && Resting(tm.hour);
            // a fed predator sleeps its meal off; in a storm hunters lie low
            if (!Herd && GameClock.Now < _restUntil) rest = true;
            if (!Herd && def.IsPredator && WildlifeWeather.IsStorm && !_prey) rest = true;
            if (_hp.hunts && !_atCarcass) _hunger = Mathf.Min(1f, _hunger + _hp.hungerPerHour * dt / Mathf.Max(1f, GameClock.SecondsPerHour));
            if (_sleeping && (!rest || !Calm)) WakeUp();
            float jumpy = Herbivore && !_sleeping ? 1f + 0.4f * def.NightFear * NightK : 1f;        // night-shy herbivores startle sooner in the dark
            Senses.SightMul = _sleeping ? c.sleepSight : 1f;
            Senses.HearingMul = (_sleeping ? c.sleepHearing : 1f) * jumpy * (1f - c.stormHearingLoss * WildlifeWeather.Storm);
            Senses.SmellMul = 1f - c.rainSmellLoss * WildlifeWeather.Rain;
            // droppings now and then while it grazes (tracking signs)
            if (Herbivore && def.tracks.droppingsSize > 0f && GameClock.Now >= _nextDroppingsAt && (State == DinoState.Eat || State == DinoState.Idle))
            {
                var w = W; _nextDroppingsAt = GameClock.Now + GameClock.Hours(Random.Range(w.droppingsHours.x, w.droppingsHours.y));
                if (_lod <= 2) TrackSigns.Droppings(def, transform.position - transform.forward * def.bodyRadius * 1.4f);
            }
            if (!Calm || Senses.Level >= AwarenessLevel.Suspicious) return;
            if (rest && !_sleeping && (State == DinoState.Idle || State == DinoState.Eat || (State == DinoState.Rest && Herd) || (State == DinoState.Wander && _goal == Goal.None)))
            {
                _sleeping = true; _goal = Goal.None; Enter(DinoState.Rest); _stateT = -Random.Range(c.sleepSeconds.x, c.sleepSeconds.y);
                return;
            }
            // thirst: go to fresh water now and then (daytime for diurnal species); a herd drinks together at midday instead
            if (!Herd && !rest && !_sleeping && def.drinkEveryHours > 0f && GameClock.Now >= _nextDrinkAt && _goal == Goal.None && (State == DinoState.Idle || State == DinoState.Eat || State == DinoState.Wander) && Time.time >= _nextDrinkLook)
            {
                _nextDrinkLook = Time.time + 20f;
                if (!_hasDrinkSpot) _hasDrinkSpot = DrinkSpots.Nearest(home, def.drinkSearchRadius, out _drinkSpot);
                if (_hasDrinkSpot && !FearBlocks(_drinkSpot)) { _goal = Goal.Drink; GoTo(_drinkSpot, DinoState.Wander); _drinkWalk = Flat(_drinkSpot - transform.position) / Mathf.Max(0.3f, def.walkSpeed) * 2f + 20f; }
                else _nextDrinkAt = GameClock.Now + GameClock.Hours(Mathf.Max(0.5f, def.drinkEveryHours) * 0.5f);
            }
        }

        void WakeUp() { _sleeping = false; if (State == DinoState.Rest) _stateT = 99f; }

        /// <summary>wander radius: night rules, then the weather (predators patrol less in heavy rain / storms, herbivores stay closer in rain)</summary>
        float RoamRadius => RoamRadiusBase * (def.IsPredator ? WildlifeWeather.PatrolMul : Herbivore ? WildlifeWeather.HerbRoamMul : 1f);
        float RoamRadiusBase
        {
            get
            {
                var tm = TimeManager.Instance; if (!tm || tm.NightFactor < 0.7f) return homeRadius;
                // night: hunters with no fear of the dark roam wider, the rest keep close to home
                if (def.activity == ActivityCycle.Nocturnal) return homeRadius * Mathf.Lerp(C.nightRoam, 1f, def.NightFear);
                return homeRadius * Mathf.Lerp(C.nightRoam, C.nightHuddle, def.NightFear);
            }
        }

        // ------------------------------------------------------------------ herd member
        /// <summary>
        /// Goes where its herd is going (a loose place in the column, trotting to catch up, a bite when the herd waits),
        /// drinks at its turn on its own bit of bank (or where it stands at the ford), rests and sleeps with the others,
        /// grazes around its own place, and watches a hunter the herd has seen. True when the herd decided this tick.
        /// </summary>
        bool HerdThink()
        {
            var h = Herd; if (!h || !h.isActiveAndEnabled) return false;
            float now = Stimuli.Now;
            if (_goal == Goal.Avoid && State == DinoState.Wander) return true;          // walking away from something: let it finish
            if (!(Calm || (State == DinoState.Observe && Senses.Level == AwarenessLevel.Unaware))) return false;
            if (now < _threatLookUntil) { if (State != DinoState.Observe) Enter(DinoState.Observe); return true; }
            if (State == DinoState.Observe && now < _lookUpUntil) return true;
            var w = W;
            Vector3 slot = h.SlotPosition(this);
            float d = Flat(slot - transform.position);
            switch (h.Activity)
            {
                case HerdActivity.Travel: case HerdActivity.Migrate:
                {
                    if (d > 2.5f || h.AnchorMoving)
                    {
                        if (State == DinoState.Eat && d < 6f && _stateT < 2.5f * HerdIdleMul) return true;      // finish the mouthful
                        if (State == DinoState.Drink && _stateT < 10f && d < 8f) return true;
                        HerdWalk(h.AnchorMoving ? slot + h.Heading * 3f : slot, h.SpeedFor(this, slot));
                    }
                    else if (State == DinoState.Wander) Enter(Random.value < 0.6f ? DinoState.Eat : DinoState.Idle);      // the herd waits: a bite
                    return true;
                }
                case HerdActivity.Drink:
                {
                    if (HerdDrankSession == h.DrinkSession) return GrazeNear(h, slot, d);
                    if (State == DinoState.Drink) return true;
                    if (h.OnRoute)
                    {
                        // at the ford: heads down and drink where it stands
                        _herdDrinking = true; _goal = Goal.None; Enter(DinoState.Drink); _stateT = -Random.Range(w.drinkSeconds.x, w.drinkSeconds.y) + 10f;
                        return true;
                    }
                    if (_goal != Goal.Drink && h.DrinkSpotFor(this, out var spot))
                    {
                        _herdDrinking = true; _goal = Goal.Drink; _herdSpeed = def.walkSpeed * HerdSpeedMul;
                        GoTo(spot, DinoState.Wander);
                        _drinkWalk = Flat(spot - transform.position) / Mathf.Max(0.3f, def.walkSpeed) * 2f + 20f;
                    }
                    return true;
                }
                case HerdActivity.Rest: case HerdActivity.Sleep:
                    if (d > 3f && !(State == DinoState.Rest && d < 6f)) { HerdWalk(slot, h.SpeedFor(this, slot)); return true; }
                    if (State == DinoState.Wander && _goal == Goal.Herd) { _goal = Goal.None; Enter(DinoState.Rest); _stateT = -Random.Range(20f, 60f) * HerdIdleMul; }
                    else if ((State == DinoState.Idle || State == DinoState.Eat) && _stateT > 3f * HerdIdleMul && Random.value < 0.35f) Enter(DinoState.Rest);
                    return true;
                default:
                    return GrazeNear(h, slot, d);
            }
        }

        bool GrazeNear(HerdGroup h, Vector3 slot, float d)
        {
            if (d > h.Spread * 0.9f + 4f) { HerdWalk(slot, h.SpeedFor(this, slot)); return true; }
            if (_goal == Goal.Herd && State == DinoState.Wander && d < 3f) { _goal = Goal.None; Enter(Random.value < 0.65f ? DinoState.Eat : DinoState.Idle); }
            return false;          // ordinary grazing: idle / eat / wander around its own place
        }

        void HerdWalk(Vector3 p, float speed)
        {
            _herdSpeed = speed; _dest = p;
            if (State != DinoState.Wander || _goal != Goal.Herd) { _goal = Goal.Herd; _keepDest = true; Enter(DinoState.Wander); _keepDest = false; }
        }

        /// <summary>the herd saw a hunter: look at it for a while (DinoLife turns the head)</summary>
        public void WatchThreat(Vector3 at, float seconds)
        {
            float until = Stimuli.Now + seconds;
            _threatLookUntil = until; _lookOverride = at; _lookOverrideUntil = until;
        }

        /// <summary>the alarm reaches it: bolt after 'delay' seconds (turning to look first)</summary>
        public void FleeLater(Vector3 from, float delay)
        {
            if (!IsAlive || State == DinoState.Flee) return;
            float at = Time.time + Mathf.Max(0f, delay);
            if (_fleeAt < 0f || at < _fleeAt) { _fleeAt = at; _fleeFrom = from; }
            _lookOverride = from; _lookOverrideUntil = Stimuli.Now + delay + 0.6f;
        }

        /// <summary>a herd call that carries across the valley (migration): true when a call played</summary>
        public bool HerdCall(float distance)
        {
            if (!IsAlive || def.calls == null || def.calls.Length == 0 || !_audio || _sleeping) return false;
            _audio.maxDistance = Mathf.Max(_audio.maxDistance, distance);
            _audio.pitch = Random.Range(0.9f, 1.08f);
            _audio.PlayOneShot(def.calls[Random.Range(0, def.calls.Length)], 0.95f * AudioBus.Sfx);
            if (_speed < 0.4f && _anim) { _anim.SetInteger(ActionTypeH, DinoActions.Call); _anim.SetTrigger(ActionH); }
            return true;
        }

        /// <summary>tests / story: this animal leaves its herd for good and lives alone (its own drinking and sleeping)</summary>
        public void LeaveHerd() { Loner = true; if (Herd) { var h = Herd; Herd = null; h.Members.Remove(this); } if (_goal == Goal.Herd) _goal = Goal.None; }
        /// <summary>never joins a herd (LeaveHerd)</summary>
        public bool Loner { get; private set; }

        /// <summary>put it somewhere at once (a missed migration moves the herd while nobody looks)</summary>
        public void TeleportTo(Vector3 p, float yaw)
        {
            transform.rotation = Quaternion.Euler(0f, yaw, 0f); transform.position = p; Snap();
            _speed = 0f; _goal = Goal.None; _fleeAt = -1f; if (IsAlive) Enter(DinoState.Idle);
        }

        // ------------------------------------------------------------------ predators and prey
        /// <summary>a calm hunter sometimes goes to watch the nearest herd within reach, from a distance, for a while</summary>
        bool TryWatchPrey()
        {
            var w = W;
            HerdGroup best = null; float bd = homeRadius * 1.5f + 40f;
            foreach (var h in HerdGroup.All)
            {
                if (!h || h.AliveCount == 0) continue;
                float q = Flat(h.Centroid - home);
                if (q < bd) { bd = q; best = h; }
            }
            if (!best) return false;
            Vector3 from = transform.position - best.Centroid; from.y = 0f;
            if (from.sqrMagnitude < 1f) from = Vector3.forward;
            float stand = best.Spread * 0.5f + Random.Range(w.watchPreyDistance.x, w.watchPreyDistance.y);
            Vector3 p = best.Centroid + Quaternion.Euler(0f, Random.Range(-35f, 35f), 0f) * from.normalized * stand;
            if (!HerdGroup.Walkable(p) || FearBlocks(p)) return false;
            _watchHerd = best; _goal = Goal.WatchPrey; _dest = p;
            return true;
        }

        bool WatchPreyThink()
        {
            if (!_watchHerd) return false;
            float now = Stimuli.Now;
            if (_goal == Goal.WatchPrey && State == DinoState.Wander) return true;          // walking to its lookout
            if (State == DinoState.Observe && _watchUntil > 0f)
            {
                if (_watchHerd) { _lookOverride = _watchHerd.Centroid + Vector3.up; _lookOverrideUntil = now + 1f; }
                if (now < _watchUntil) return true;
                // done watching: now and then a short rush that tests the herd (only worth it with the player near enough to see it)
                var w = W; _watchUntil = -99f;
                bool seen = PlayerDist < 150f;
                if (seen && Random.value < w.testRushChance * def.Aggression && _watchHerd)
                {
                    _goal = Goal.Rush; _dest = _watchHerd.Centroid; _rushUntil = Time.time + w.testRushSeconds;
                    _keepDest = true; Enter(DinoState.Investigate); _keepDest = false;
                    Roar();
                    return true;
                }
                CancelWatch(); Enter(DinoState.Idle);
                return true;
            }
            return false;
        }
        float _rushUntil;

        void BeginWatch()
        {
            var w = W;
            _goal = Goal.None; Enter(DinoState.Observe);
            _watchUntil = Stimuli.Now + Random.Range(w.watchPreySeconds.x, w.watchPreySeconds.y);
            if (_watchHerd) { _lookOverride = _watchHerd.Centroid + Vector3.up; _lookOverrideUntil = Stimuli.Now + 1f; }
        }

        void CancelWatch() { if (_goal == Goal.WatchPrey || _goal == Goal.Rush) _goal = Goal.None; _watchHerd = null; _watchUntil = -99f; }

        /// <summary>stalking: it freezes for a moment when the player looks its way</summary>
        void StalkFreeze(bool see, float dist)
        {
            if (!see || dist < def.attackRange * 4f || Time.time < _nextFreeze) return;
            var cam = Camera.main; if (!cam) return;
            Vector3 to = transform.position - cam.transform.position; to.y = 0f;
            Vector3 cf = cam.transform.forward; cf.y = 0f;
            if (to.sqrMagnitude < 1f || cf.sqrMagnitude < 0.01f || Vector3.Angle(cf, to) > 25f) return;
            _stalkFreezeUntil = Time.time + Random.Range(1f, 2.2f); _nextFreeze = Time.time + Random.Range(4f, 7f);
        }

        // ------------------------------------------------------------------ hunting herbivores (Phase 1)
        /// <summary>a hungry hunter now and then picks a straggler of its prey species and starts stalking it (HuntDirector allows it)</summary>
        bool TryStartHunt()
        {
            if (!_hp.hunts || _prey || _sleeping || _hunger < _hp.hungerToHunt || Time.time < _nextHuntCheck) return false;
            var w = W;
            _nextHuntCheck = Time.time + w.huntCheckSeconds * Random.Range(0.8f, 1.25f);
            if (Health < def.maxHealth * 0.6f || GameClock.Now < _restUntil || HuntDirector.InSafeArea(transform.position)) return false;
            float chance = w.huntStartChance * WildlifeWeather.HuntMul * Mathf.Lerp(0.6f, 1.4f, Mathf.InverseLerp(_hp.hungerToHunt, 1f, _hunger));
            if (chance <= 0f || Random.value >= chance || !HuntDirector.CanStart()) return false;
            var prey = HuntDirector.PickPrey(this, _hp);
            if (!prey || FearBlocks(prey.transform.position) || !HuntDirector.Begin(this, prey)) return false;
            CancelWatch();
            _prey = prey; prey._hunter = this; _huntStrikes = 0; _huntT = 0f; _huntHitPending = false;
            _goal = Goal.Hunt; _dest = prey.transform.position;
            Enter(DinoState.Investigate);
            return true;
        }

        /// <summary>the running hunt: stalk until close (or seen), charge, keep the prey running, give up in time. True = decided this tick</summary>
        bool HuntThink(float dt)
        {
            var p = _prey; var w = W;
            bool on = p && p.IsAlive && p.isActiveAndEnabled && p._hunter == this && HuntDirector.Hunter == this && !WildlifeWeather.IsStorm
                      && !HuntDirector.InSafeArea(p.transform.position) && Health >= def.maxHealth * def.retreatHealth;
            if (!on)
            {
                EndHunt(false);
                if (State == DinoState.Chase || State == DinoState.Attack || State == DinoState.Investigate) Enter(DinoState.Return);
                return true;
            }
            _huntT += dt;
            float d = Flat(p.transform.position - transform.position);
            switch (State)
            {
                case DinoState.Investigate when _goal == Goal.Hunt:
                    _dest = p.transform.position;
                    if (d < _hp.chargeDistance || p.State == DinoState.Flee) { HuntCharge(); return true; }
                    if (_huntT > w.huntStalkSeconds) { EndHunt(false); Enter(DinoState.Return); }
                    return true;
                case DinoState.Chase:
                    if (_huntT > _hp.chaseSeconds || d > w.huntGiveUpDistance) { EndHunt(false); Enter(DinoState.Return); return true; }
                    if (p.State != DinoState.Flee) p.FleeFrom(transform.position, false);         // keep it running
                    return true;
                case DinoState.Attack:
                    return true;
            }
            EndHunt(false);                 // something else took over (a fire, a fright): the hunt is off
            return false;
        }

        /// <summary>close enough: the rush breaks cover, the prey bolts and its herd with it</summary>
        void HuntCharge()
        {
            _huntT = 0f; _goal = Goal.None;
            Enter(DinoState.Chase);
            if (_prey) { _prey.FleeFrom(transform.position, true); _dest = _prey.transform.position; }
            PlayClip(def.roars, 0.8f);
        }

        void HuntChaseAct(float dt)
        {
            var p = _prey; if (!p || !p.IsAlive) { EndHunt(false); Enter(DinoState.Return); return; }
            Vector3 pp = p.transform.position;
            _dest = pp;
            Steer(pp + p.transform.forward * Mathf.Min(p.CurrentSpeed * 0.5f, 4f), def.runSpeed * _indivSpeed, dt, true);
            float reach = def.attackRange + p.def.bodyRadius + def.bodyRadius * 0.3f;
            if (Flat(pp - transform.position) < reach && Time.time >= _attackReady) HuntStrike();
        }

        void HuntStrike()
        {
            Enter(DinoState.Attack);
            _attackReady = Time.time + def.attackCooldown * Random.Range(0.8f, 1.2f);
            _heavy = false; _tellTime = 0.2f; _tellUntil = Time.time + _tellTime; _attackQueued = true;
        }

        void HuntAttackAct(float dt)
        {
            var p = _prey;
            if (!p || !p.IsAlive) { EndHunt(false); Enter(DinoState.Return); return; }
            _speed = Mathf.MoveTowards(_speed, p.CurrentSpeed * 0.8f, def.acceleration * dt * 3f);
            Face(p.transform.position, dt * 1.5f);
            if (_attackQueued && Time.time >= _tellUntil)
            {
                _attackQueued = false;
                if (_anim) { _anim.SetInteger(AttackTypeH, _caps.Bite ? 2 : 0); _anim.SetTrigger(AttackH); }
                _huntHitPending = true; _hitAt = Time.time + (_caps.Bite ? 0.3f : 0.5f);
            }
            if (_huntHitPending && Time.time >= _hitAt) { _huntHitPending = false; ResolveHuntHit(); if (!_prey || State != DinoState.Attack) return; }
            if (!_attackQueued && !_huntHitPending && _stateT > 0.9f + _tellTime) Enter(DinoState.Chase);
        }

        /// <summary>the strike lands (or the prey got out of reach): a kill with killChance, else a wound and the chase goes on</summary>
        void ResolveHuntHit()
        {
            var p = _prey; if (!p || !p.IsAlive) { EndHunt(false); Enter(DinoState.Return); return; }
            float reach = (def.attackRange + p.def.bodyRadius + def.bodyRadius * 0.3f) * 1.35f;
            if (Flat(p.transform.position - transform.position) > reach) return;
            _huntStrikes++;
            float kill = _hp.killChance * HuntDirector.KillMul(p) * (p.Health < p.def.maxHealth * 0.5f ? 1.5f : 1f);
            if (Random.value < kill) { HuntKill(p); return; }
            p.PredatorWound(this, p.def.maxHealth * _hp.woundFraction);
            if (_huntStrikes >= Mathf.Max(1, _hp.strikes)) { EndHunt(false); Enter(DinoState.Return); }
        }

        /// <summary>brought down: the body becomes a Carcass; the hunter walks up to it and eats, then rests</summary>
        void HuntKill(DinosaurController p)
        {
            Vector3 at = p.transform.position;
            p._hunter = null; _prey = null; _huntHitPending = false; _goal = Goal.None;
            HuntDirector.End(this, true, at);
            p.KilledByPredator(this);
            _meal = p.GetComponent<Carcass>(); _mealRest = true;
            Vector3 dir = at - transform.position; dir.y = 0f; if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
            GoInvestigate(at - dir.normalized * (p.def.bodyRadius + def.bodyRadius * 0.6f + 0.5f), Goal.Food);
        }

        void EndHunt(bool kill)
        {
            var p = _prey; _prey = null; _huntHitPending = false;
            if (p && p._hunter == this) p._hunter = null;
            if (_goal == Goal.Hunt) _goal = Goal.None;
            HuntDirector.End(this, kill, p ? p.transform.position : transform.position);
        }

        /// <summary>done eating at a carcass: less hungry; after its own kill it takes its share of meat and rests (Life lies it down)</summary>
        void FinishMeal()
        {
            _atCarcass = false;
            _hunger = Mathf.Max(0f, _hunger - 0.6f);
            if (_mealRest)
            {
                _mealRest = false; _hunger = 0f;
                if (_meal && _hp.eatsMeat > 0) _meal.PredatorFeed(_hp.eatsMeat);
                _meal = null;
                _restUntil = GameClock.Now + GameClock.Hours(_hp.restHoursAfterMeal);
                Enter(DinoState.Idle);
                return;
            }
            Enter(DinoState.Wander);
        }

        /// <summary>a predator's strike that did not bring it down: hurt and bleeding, it runs on with its herd</summary>
        internal void PredatorWound(DinosaurController by, float dmg)
        {
            if (!IsAlive) return;
            Health = Mathf.Max(1f, Health - Mathf.Max(0f, dmg));
            if (_sleeping) WakeUp();
            if (_anim) _anim.SetTrigger(HurtH);
            PlayClip(def.hurts, 1f);
            Vector3 from = by ? by.transform.position : transform.position - transform.forward;
            if (_bones == null) { var smr = GetComponentInChildren<SkinnedMeshRenderer>(); _bones = smr ? smr.bones : new Transform[0]; }
            Vector3 dir = transform.position - from; dir.y = 0f; if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
            BloodFX.Hit(transform.position + Vector3.up * def.bodyRadius, dir.normalized, Mathf.Clamp(def.bodyRadius * 0.6f, 0.6f, 2.2f), false, transform, _bones);
            Stimuli.Noise(transform.position, 2f, NoiseTag.Voice, StimulusSource.Creature);
            FleeFrom(from, true);
        }

        /// <summary>brought down by a predator (no CreatureKilled event: the player did not kill it; the body is a Carcass as usual)</summary>
        internal void KilledByPredator(DinosaurController by)
        {
            if (!IsAlive) return;
            _hunter = null;
            Die(true);
        }

        /// <summary>getting up from lying (Rest_Up): stand still until the clip is done (no sliding); fleeing and charging go at once</summary>
        bool RestHold(float dt)
        {
            if (State == DinoState.Flee || State == DinoState.Chase || State == DinoState.Attack || State == DinoState.Dead || State == DinoState.Rest ||
                Time.time > _restHoldUntil || !DinoAnimCaps.InRestPose(_anim)) { _restHold = false; return false; }
            _speed = Mathf.MoveTowards(_speed, 0f, def.acceleration * dt * 4f);
            return true;
        }

        /// <summary>heavy rain / storm, a herbivore on its own: a spot among trees near home (found once)</summary>
        bool LonerShelter(out Vector3 p)
        {
            if (!_hasShelter) { _shelter = HerdGroup.ShadeNear(home); _hasShelter = true; }
            var r = Random.insideUnitCircle * 3f;
            p = _shelter + new Vector3(r.x, 0f, r.y);
            return true;
        }

        // ------------------------------------------------------------------ goals
        void GoTo(Vector3 p, DinoState s) { _dest = p; _keepDest = true; Enter(s); _keepDest = false; }

        void GoInvestigate(Vector3 p, Goal g)
        {
            bool same = State == DinoState.Investigate && _goal == g;
            _goal = g; _dest = p; _goalMemTime = Senses.LastKnownTime;
            if (g == Goal.Threat || g == Goal.Search) _searchLeft = Mathf.Max(1, Mathf.RoundToInt(C.searchPoints * Mathf.Lerp(0.4f, 1.6f, def.Investigation)));
            if (!same) { _keepDest = true; Enter(DinoState.Investigate); _keepDest = false; }
        }

        void WalkAway(Vector3 from, Goal g)
        {
            Vector3 away = transform.position - from; away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = -transform.forward;
            _goal = g;
            GoTo(transform.position + away.normalized * Mathf.Max(12f, def.personalSpace * 1.5f), DinoState.Wander);
        }

        // ------------------------------------------------------------------ campfire fear
        bool IgnoringFire => Stimuli.Now < _fireIgnoreUntil || (def.fireFear.ignoreWhenProvoked && _provoked && Stimuli.Now - _provokedAt < 8f);

        /// <summary>is p inside a fire circle this species will not enter (fear >= 0.5 and not ignoring)</summary>
        bool FearBlocks(Vector3 p)
        {
            if (def.fireFear.predatorFear < 0.5f || IgnoringFire) return false;
            return FireSense.FearAt(def, p, out _) != null;
        }

        /// <summary>
        /// Campfires in the way: the creature itself inside a fear circle moves out; a target (player, smell, water)
        /// inside one makes it wait, circle, watch, investigate the edge or walk around until its patience runs out, then
        /// leave. Weak fear (below 0.5) only hesitates; AttackAnyway hesitates briefly and comes in while it is after the
        /// player. Returns true when the fire decided this tick.
        /// </summary>
        bool FireCheck(PerceptionConfig c, float dist)
        {
            var ff = def.fireFear;
            if (ff.predatorFear <= 0f || State == DinoState.Flee) { _fireMode = FireMode.None; return false; }
            float now = Stimuli.Now;
            if (_fireMode != FireMode.None)
            {
                bool gone = !_fire || !_fire.IsLit || IgnoringFire;
                float r = gone ? 0f : FireSense.FearRadius(ff, _fire, FireSense.Night);
                bool targetInside = !gone && r > 0f && TargetInside(_fire, r);
                if (gone || !targetInside) { _fireMode = FireMode.None; _stateT = 0f; return false; }
                _fireR = r;
                // patience runs only once it stands at the edge (walking there from far does not count)
                if (!_fireArrived && Flat(transform.position - _fire.transform.position) <= r + c.edgeMargin + def.bodyRadius + 2f) { _fireArrived = true; _fireUntil = now + _firePatience; }
                if (now >= _fireUntil)
                {
                    _fireMode = FireMode.None;
                    if (ff.predatorFear < 0.5f || _fireAttackAnyway) { _fireIgnoreUntil = now + 30f; return false; }       // hesitated long enough: walks in
                    // gives up: leaves for home, calmer, and keeps away from the fire for a while
                    _fireLeftUntil = now + 45f; _goal = Goal.None;
                    if (Senses.HasInterest) Senses.DropInterest(c);
                    Enter(DinoState.Return);
                    return true;
                }
                return true;
            }
            if (IgnoringFire) return false;
            // standing inside a fear circle (fire lit next to it): move out
            var inside = FireSense.FearAt(def, transform.position, out float rIn);
            if (inside && ff.predatorFear >= 0.5f && State != DinoState.Chase && State != DinoState.Attack)
            {
                _fireInvestigate = _fireAttackAnyway = false;
                StartFire(inside, rIn, FireMode.Avoid, c); _fireOnPlayer = false; return true;
            }
            // the thing it is going for is inside a fear circle
            Vector3 target; bool onPlayer = false;
            if (_prey && (State == DinoState.Chase || State == DinoState.Attack)) target = _prey.transform.position;
            else if (State == DinoState.Chase || State == DinoState.Alert || State == DinoState.Attack || (State == DinoState.Investigate && _goal == Goal.Approach)) { if (!_player) return false; target = _player.position; onPlayer = true; }
            else if (State == DinoState.Investigate || (State == DinoState.Wander && _goal == Goal.Drink)) target = _dest;
            else return false;
            var fire = FireSense.FearAt(def, target, out float r2);
            if (!fire) return false;
            _fireInvestigate = ff.response == FireResponse.Investigate;
            _fireAttackAnyway = ff.response == FireResponse.AttackAnyway && onPlayer;
            FireMode mode = ff.predatorFear < 0.5f || _fireAttackAnyway ? FireMode.Wait
                          : ff.response == FireResponse.Circle || _fireInvestigate ? FireMode.Circle
                          : ff.response == FireResponse.Observe ? FireMode.Observe
                          : ff.response == FireResponse.Avoid ? FireMode.Avoid
                          : ff.response == FireResponse.Leave || ff.response == FireResponse.AttackAnyway ? FireMode.None : FireMode.Wait;
            if (mode == FireMode.None) { _fireLeftUntil = now + 45f; _goal = Goal.None; if (Senses.HasInterest) Senses.DropInterest(c); Enter(DinoState.Return); return true; }
            StartFire(fire, r2, mode, c); _fireOnPlayer = onPlayer;
            return true;
        }

        bool TargetInside(Campfire fire, float r)
        {
            Vector3 target = _fireOnPlayer && _player ? _player.position : _dest;
            if (_fireMode == FireMode.Avoid && Flat(transform.position - fire.transform.position) < r) return true;
            return Flat(target - fire.transform.position) < r;
        }

        void StartFire(Campfire fire, float r, FireMode mode, PerceptionConfig c)
        {
            var ff = def.fireFear;
            _fire = fire; _fireR = r; _fireMode = mode;
            float patience = ff.patienceSeconds * (ff.predatorFear < 0.5f ? Mathf.Lerp(0.3f, 1f, ff.predatorFear * 2f) : 1f);
            if (_fireInvestigate) patience *= 0.5f;                   // a short look along the edge, then it goes
            if (_fireAttackAnyway) patience = Mathf.Min(patience, 3f);
            _firePatience = Mathf.Max(2f, patience); _fireArrived = false;
            _fireUntil = Stimuli.Now + 60f;                                   // cap on the walk to the edge; the patience starts there
            Vector3 d = transform.position - fire.transform.position; d.y = 0f;
            _circleAngle = Mathf.Atan2(d.z, d.x);
            _attackQueued = false;
            Enter(mode == FireMode.Circle || mode == FireMode.Avoid ? DinoState.Investigate : DinoState.Observe);
        }

        void FireAct(float dt)
        {
            var c = C;
            Vector3 fc = _fire ? _fire.transform.position : transform.position;
            float ring = _fireR + c.edgeMargin + def.bodyRadius;
            switch (_fireMode)
            {
                case FireMode.Circle:
                {
                    // walk to the ring first, then along it: the aim point leads its own angle a little (never a chord through the fire)
                    Vector3 d = transform.position - fc; d.y = 0f; float r = d.magnitude;
                    float cur = r > 0.01f ? Mathf.Atan2(d.z, d.x) : _circleAngle;
                    Vector3 p;
                    if (r > ring + 1.5f || r < ring - 0.8f) p = fc + (r > 0.01f ? d / r : Vector3.forward) * ring;
                    else { _circleAngle = cur + _circleDir * c.circleSpeed * Mathf.Deg2Rad; p = fc + new Vector3(Mathf.Cos(_circleAngle), 0f, Mathf.Sin(_circleAngle)) * ring; }
                    Steer(p, def.walkSpeed * (_fireInvestigate ? 0.7f : 1f), dt);
                    break;
                }
                case FireMode.Avoid:
                {
                    Vector3 away = transform.position - fc; away.y = 0f; if (away.sqrMagnitude < 0.01f) away = transform.forward;
                    Steer(fc + away.normalized * (ring + 3f), def.walkSpeed * 1.2f, dt);
                    break;
                }
                default:        // Wait / Observe: stop outside the ring, face the player if seen, else the fire
                {
                    Vector3 d = transform.position - fc; d.y = 0f;
                    if (d.magnitude < ring) Steer(fc + (d.sqrMagnitude > 0.01f ? d.normalized : transform.forward) * (ring + 0.5f), def.walkSpeed, dt);
                    else { _speed = Mathf.MoveTowards(_speed, 0f, def.acceleration * dt * 3f); Face(_player && Senses.SeesPlayer ? _player.position : fc, dt); }
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ actions
        void Act(float dt, float dist)
        {
            if (_restHold && RestHold(dt)) return;
            if (_fireMode != FireMode.None && State != DinoState.Dead && State != DinoState.Flee && State != DinoState.Return) { FireAct(dt); return; }
            float brake = def.acceleration * dt * Mathf.Lerp(2.4f, 1.1f, def.Weight01);
            switch (State)
            {
                case DinoState.Idle:
                    _speed = Mathf.MoveTowards(_speed, 0f, brake);
                    // rain / storm: lies down more often; predators wait longer before patrolling on
                    if (_stateT > Random.Range(4f, 9f) * _idleMul * (Herd ? HerdIdleMul : 1f) * (def.IsPredator ? 1f / Mathf.Max(0.2f, WildlifeWeather.PatrolMul) : 1f))
                        Enter(Random.value < WildlifeWeather.RestBias ? DinoState.Rest : Random.value < 0.55f ? DinoState.Wander : Random.value < 0.6f ? DinoState.Eat : DinoState.Rest);
                    break;
                case DinoState.Wander:
                    Steer(_dest, _goal == Goal.Herd ? _herdSpeed : def.walkSpeed * Indiv * (_goal == Goal.Routine ? _routineSpeed : 1f), dt);
                    if (_goal == Goal.Drink)
                    {
                        if (Flat(_dest - transform.position) < def.bodyRadius * 1.5f + 1.2f) { _goal = Goal.None; Enter(DinoState.Drink); _stateT = -Random.Range(C.drinkSeconds.x, C.drinkSeconds.y) + 10f; }
                        else if (_stateT > _drinkWalk)
                        {
                            _goal = Goal.None; _nextDrinkAt = GameClock.Now + GameClock.Hours(1f);
                            if (Herd && _herdDrinking) { HerdDrankSession = Herd.DrinkSession; _herdDrinking = false; }         // could not reach the bank: counts as done
                            Enter(DinoState.Idle);
                        }
                        break;
                    }
                    if (_goal == Goal.Routine) { if (Arrived(1.5f + def.bodyRadius) || _stateT > 60f) { _goal = Goal.None; Enter(DinoState.Idle); } break; }
                    if (_goal == Goal.Herd) break;                                   // the herd keeps moving the destination
                    if (_goal == Goal.WatchPrey) { if (Arrived(4f) || _stateT > 45f) BeginWatch(); break; }
                    if (Arrived(2f) || _stateT > 25f) { _goal = Goal.None; Enter(Random.value < 0.5f ? DinoState.Eat : DinoState.Idle); }
                    break;
                case DinoState.Drink:
                    _speed = Mathf.MoveTowards(_speed, 0f, brake);
                    if (_stateT > 10f)
                    {
                        _nextDrinkAt = GameClock.Now + GameClock.Hours(Mathf.Max(0.5f, def.drinkEveryHours) * Random.Range(0.8f, 1.2f));
                        if (Herd && _herdDrinking)
                        {
                            // some drink a second time; then the herd's drink is done for this one
                            if (!_drankTwice && Random.value < 0.3f) { _drankTwice = true; _stateT = 10f - Random.Range(4f, 8f); if (_anim) { _anim.SetInteger(ActionTypeH, DinoActions.Drink); _anim.SetTrigger(ActionH); } break; }
                            HerdDrankSession = Herd.DrinkSession; _herdDrinking = false; _drankTwice = false;
                            Enter(DinoState.Idle);
                        }
                        else Enter(DinoState.Wander);
                    }
                    break;
                case DinoState.Eat: case DinoState.Rest:
                    _speed = Mathf.MoveTowards(_speed, 0f, brake);
                    if (_sleeping) { if (_stateT > 0f) { _stateT = -Random.Range(C.sleepSeconds.x, C.sleepSeconds.y); if (_anim && Random.value < 0.3f) { _anim.SetInteger(ActionTypeH, DinoActions.Rest); _anim.SetTrigger(ActionH); } } break; }
                    if (State == DinoState.Rest && Herd && (Herd.Activity == HerdActivity.Rest || Herd.Activity == HerdActivity.Sleep))
                    {
                        // the midday rest: stand and doze with the others, shifting now and then
                        if (_stateT > 0f) { _stateT = -Random.Range(15f, 40f) * HerdIdleMul; if (_anim && Random.value < 0.4f) { _anim.SetInteger(ActionTypeH, DinoActions.Rest); _anim.SetTrigger(ActionH); } }
                        break;
                    }
                    if (_atCarcass && State == DinoState.Eat) _hunger = Mathf.Max(0f, _hunger - W.carcassHungerPerSecond * dt);
                    if (_stateT > Random.Range(8f, 16f) * _idleMul * (Herd ? HerdIdleMul : 1f)) { if (_atCarcass && State == DinoState.Eat) { FinishMeal(); break; } _atCarcass = false; Enter(DinoState.Wander); }
                    break;
                case DinoState.Observe:
                case DinoState.Alert:
                    _speed = Mathf.MoveTowards(_speed, 0f, brake * 1.4f);
                    if (_player && Senses.SeesPlayer && !(Stimuli.Now < _threatLookUntil)) Face(_player.position, dt);
                    else if (Stimuli.Now < _lookOverrideUntil) Face(_lookOverride, dt);
                    else if (Senses.HasLookPoint) Face(Senses.LookPoint, dt);
                    if (State == DinoState.Alert && _stateT > 1.8f) Enter(DinoState.Chase);
                    break;
                case DinoState.Investigate:
                {
                    if (_goal == Goal.Rush)
                    {
                        // a short run at the herd: they scatter, it stops and turns away
                        if (_watchHerd) _dest = _watchHerd.Centroid;
                        Steer(_dest, def.runSpeed * 0.85f, dt, true);
                        if (Time.time >= _rushUntil || Arrived(def.attackRange * 2f)) { CancelWatch(); Enter(DinoState.Return); }
                        break;
                    }
                    if (_goal == Goal.Hunt)
                    {
                        // stalking a herbivore: a brisk walk from far, slow and quiet once near (HuntThink charges when close)
                        if (_prey) _dest = _prey.transform.position;
                        bool far = Flat(_dest - transform.position) > Mathf.Max(20f, _hp.chargeDistance * 2.5f);
                        Steer(_dest, def.walkSpeed * (far ? 1.15f : W.huntStalkSpeed) * _indivSpeed, dt);
                        break;
                    }
                    if (_goal == Goal.Approach && Time.time < _stalkFreezeUntil) { _speed = Mathf.MoveTowards(_speed, 0f, brake * 2f); if (_player) Face(_player.position, dt); break; }
                    float sp = _goal == Goal.Approach ? def.walkSpeed * (dist < 25f ? 0.75f : 1f) : _goal == Goal.Food ? def.walkSpeed * 1.1f : def.walkSpeed * 1.3f;
                    if (_goal == Goal.Approach && _player && Senses.SeesPlayer) _dest = _player.position;
                    Steer(_dest, sp * _indivSpeed, dt);
                    if (Arrived(_goal == Goal.Approach ? def.attackRange * 2f : 3f) || _stateT > 15f) ArriveInvestigate();
                    break;
                }
                case DinoState.Flee:
                {
                    Vector3 away = transform.position - _threat; away.y = 0; if (away.sqrMagnitude < 0.01f) away = transform.forward;
                    away.Normalize();
                    // a fleeing herd keeps running roughly together: bend a little towards the herd's heading when travelling
                    if (Herd && (Herd.Activity == HerdActivity.Migrate || Herd.Activity == HerdActivity.Travel)) away = (away + Herd.Heading * 0.6f).normalized;
                    // zig-zag prey: a sideways swing on top of the escape direction (harder to hit with arrows)
                    if (def.fleeZigZag > 0f) away = (away + Vector3.Cross(Vector3.up, away) * (Mathf.Sin(_stateT * ZigZagRate + _zigPhase) * def.fleeZigZag * 1.2f)).normalized;
                    _dest = transform.position + away * 20f;
                    Steer(_dest, def.runSpeed * Indiv, dt);
                    if (_stateT > 9f || Vector3.Distance(transform.position, _threat) > def.fleeDistance) { _provoked = false; _goal = Goal.None; Enter(DinoState.Idle); }
                    break;
                }
                case DinoState.Chase:
                    if (_prey) { HuntChaseAct(dt); break; }
                    if (!_player || !PlayerAlive) { Enter(DinoState.Return); break; }
                    if (!Tracking && (!_provoked || Stimuli.Now - _provokedAt > 6f))
                    {
                        // lost it: go to where it was last known and search there
                        if (Senses.HasMemory) GoInvestigate(Senses.LastKnownPos, Goal.Search); else Enter(DinoState.Return);
                        break;
                    }
                    Steer(_player.position, def.runSpeed * _indivSpeed, dt, true);
                    if (dist < def.attackRange + def.bodyRadius * 0.5f && Time.time >= _attackReady) StartAttack(dist);
                    break;
                case DinoState.Attack:
                    if (_prey) { HuntAttackAct(dt); break; }
                    _speed = Mathf.MoveTowards(_speed, 0f, def.acceleration * dt * 3f);
                    if (_player) Face(_player.position, dt * (_attackQueued ? 1.2f : 0.6f));
                    if (_attackQueued && Time.time >= _tellUntil) Strike();
                    if (!_attackQueued && _stateT > 1.6f + _tellTime) Enter(PlayerAlive ? DinoState.Chase : DinoState.Return);
                    break;
                case DinoState.Return:
                {
                    Vector3 h = Herd ? Herd.SlotPosition(this) : home;
                    Steer(h, def.walkSpeed * 1.2f * Indiv, dt);
                    if (Vector3.Distance(transform.position, h) < (Herd ? 4f : homeRadius * 0.5f)) { _goal = Goal.None; Enter(DinoState.Idle); }
                    break;
                }
            }
        }

        /// <summary>reached the investigated spot: search around the last known position, sniff a smell (eat at a carcass), or give up</summary>
        void ArriveInvestigate()
        {
            var c = C;
            switch (_goal)
            {
                case Goal.Threat: case Goal.Search:
                    if (_searchLeft > 0 && Senses.Level >= AwarenessLevel.Suspicious)
                    {
                        _searchLeft--;
                        Vector3 around = Senses.HasMemory ? Senses.LastKnownPos : _dest;
                        _dest = around + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(c.searchRadius * 0.3f, c.searchRadius);
                        _stateT = 0f; _goal = Goal.Search;
                        if (_anim && Random.value < 0.5f) { _anim.SetInteger(ActionTypeH, DinoActions.LookAround); _anim.SetTrigger(ActionH); }
                        return;
                    }
                    _goal = Goal.None; Enter(DinoState.Idle); return;
                case Goal.Food:
                {
                    // at the smell: a carcass is eaten for a good while (the journal sees it feeding); otherwise a sniff
                    Senses.DropInterest(c); _goal = Goal.None;
                    var carcass = _meal && !_meal.Sinking && _meal.meat > 0 ? _meal : NearestCarcass(transform.position, def.bodyRadius + 5f);
                    if (carcass)
                    {
                        Face(carcass.transform.position, 1f);
                        _atCarcass = true; Enter(DinoState.Eat);
                        var w = W; _stateT = -Random.Range(w.carcassEatSeconds.x, w.carcassEatSeconds.y);
                        return;
                    }
                    _meal = null; _mealRest = false;
                    Enter(DinoState.Eat); _stateT = 4f; return;
                }
                case Goal.Approach:
                    _goal = Goal.None; Enter(DinoState.Observe); return;
                default:
                    _goal = Goal.None; Enter(DinoState.Idle); return;
            }
        }

        static Carcass NearestCarcass(Vector3 p, float r)
        {
            Carcass best = null; float bq = r * r;
            foreach (var c in Carcass.All)
            {
                if (!c || c.Sinking || c.meat <= 0) continue;
                float q = (c.transform.position - p).sqrMagnitude;
                if (q < bq) { bq = q; best = c; }
            }
            return best;
        }

        void Enter(DinoState s)
        {
            var old = State; State = s; _stateT = 0f;
            if (s != DinoState.Attack) { _attackQueued = false; _huntHitPending = false; }
            // getting up from a lying rest takes its clip: Act holds still until Rest_Up is done
            if (old == DinoState.Rest && s != DinoState.Rest && s != DinoState.Dead && _caps.RestSet) { _restHold = true; _restHoldUntil = Time.time + 4.5f; }
            if (s == DinoState.Rest && _caps.RestSet) { var wr = W; _stateT = -Random.Range(wr.restSeconds.x, wr.restSeconds.y) * (1f + WildlifeWeather.RestBias); }
            if (s == DinoState.Flee || s == DinoState.Chase || s == DinoState.Return) _fireMode = FireMode.None;
            if (s != DinoState.Rest) _sleeping = false;
            if (s != DinoState.Observe) _tolerateAt = -1f;
            if (s != DinoState.Eat) _atCarcass = false;
            if (s == DinoState.Wander && !_keepDest)
            {
                _goal = Goal.None;
                if (Herd) _dest = Herd.GrazePoint(this);
                else if (def.IsPredator && Random.value < W.watchPreyChance * WildlifeWeather.PatrolMul && TryWatchPrey()) { }
                else if (Herbivore && WildlifeWeather.Shelter && LonerShelter(out var shelterAt)) _dest = shelterAt;       // heavy rain: to the trees
                else _dest = RandomPointNear(home, RoamRadius);
            }
            if (!_anim) return;
            _anim.SetBool(AlertH, s == DinoState.Observe || s == DinoState.Alert);
            int action = s == DinoState.Eat ? DinoActions.Eat : s == DinoState.Drink ? DinoActions.Drink : s == DinoState.Rest ? DinoActions.Rest : s == DinoState.Chase && Herbivore ? 10 : DinoActions.None;
            // after a long run: stand and breathe hard (Breathe loop while it idles)
            if (s == DinoState.Idle && _runT > W.breatheAfterRun) { if (_caps.Breathe) action = DinoActions.Breathe; _runT = 0f; }
            _anim.SetInteger(ActionTypeH, action);
            if (action != DinoActions.None) _anim.SetTrigger(ActionH);
        }

        /// <summary>run from 'from' (the herd follows)</summary>
        public void Flee(Vector3 from) => FleeFrom(from, true);

        /// <summary>run from 'from'; alarmHerd: the alarm runs through its herd (or same species nearby), each after a short delay</summary>
        public void FleeFrom(Vector3 from, bool alarmHerd)
        {
            if (State == DinoState.Dead) return;
            _fleeAt = -1f;
            _threat = from; if (State != DinoState.Flee) { CancelWatch(); Enter(DinoState.Flee); if (Random.value < 0.6f) PlayClip(def.calls, 1f); }
            if (!alarmHerd) return;
            if (Herd) { Herd.Alarm(this, from); return; }
            var w = W;
            foreach (var d in All)
            {
                if (d == this || d.def != def || d.State == DinoState.Flee || d.State == DinoState.Dead) continue;
                float q = (d.transform.position - transform.position).magnitude;
                if (q < 30f) d.FleeLater(from, q / Mathf.Max(1f, w.alarmSpeed) + Random.Range(w.alarmDelay.x, w.alarmDelay.y));
            }
        }

        void Roar()
        {
            if (!_anim) return;
            _anim.SetInteger(ActionTypeH, DinoActions.Roar); _anim.SetTrigger(ActionH);
            PlayClip(def.roars, 1f);
            if (def.roars == null || def.roars.Length == 0) SfxPlayer.Instance.Play(SfxId.RoarDistant, transform.position, 1f);
            Stimuli.Noise(transform.position, 2f, NoiseTag.Voice, StimulusSource.Creature);
        }

        /// <summary>stop, face the player and wind up (growl, head drawn back: DinoLife), then Strike()</summary>
        void StartAttack(float dist)
        {
            Enter(DinoState.Attack);
            _attackReady = Time.time + def.attackCooldown * Random.Range(0.8f, 1.2f);
            _heavy = Random.value < 0.3f;
            _tellTime = _heavy ? attackTell.y : attackTell.x;
            _tellUntil = Time.time + _tellTime; _attackQueued = true;
            PlayClip(_heavy ? def.roars : (def.calls != null && def.calls.Length > 0 ? def.calls : def.roars), _heavy ? 0.7f : 0.45f);
        }

        void Strike()
        {
            _attackQueued = false;
            if (_anim) { _anim.SetInteger(AttackTypeH, _heavy ? 1 : 0); _anim.SetTrigger(AttackH); }
            _pendingHit = true; _hitAt = Time.time + (_heavy ? 0.9f : 0.6f);        // fallback when the event does not arrive
        }

        void OnAnimEvent(string fn, string p)
        {
            if (fn == "OnFootstep") Footstep();
            else if ((fn == "OnBite" || fn == "OnHornHit" || fn == "OnTailHit" || fn == "OnClawHit") && _pendingHit) ResolveHit();
            else if (fn == "OnBodyFall") { VfxPool.Instance.Play(VfxId.DinoImpactDust, transform.position, Vector3.up, null, def.bodyRadius); SfxPlayer.Instance.Play(SfxId.DinoStepHeavy, transform.position, 1f); }
        }

        void ResolveHit()
        {
            _pendingHit = false;
            if (!_player || !PlayerAlive) return;
            Vector3 d = _player.position - transform.position; float dist = d.magnitude; d.y = 0;
            if (dist > def.attackRange + def.bodyRadius * 0.9f || Vector3.Angle(transform.forward, d) > 70f) return;      // dodged
            _playerHp.TakeDamage(_heavy ? def.heavyDamage : def.attackDamage, transform.position + Vector3.up, _heavy || def.attackDamage >= 30f, def.bleedSeconds);
            var m = _player.GetComponent<PlayerMotor>(); if (m) m.AddImpulse(d.normalized * (_heavy ? 6f : 3f) + Vector3.up * 2f);
            VfxPool.Instance.Play(VfxId.DinoImpactDust, _player.position, Vector3.up, null, 0.8f);
        }

        /// <summary>a foot comes down (clip event, near tier): heavy animals thud and shake the ground by the player; small ones tap</summary>
        void Footstep()
        {
            if (_lod > 0) return;
            float w = def.Weight01, dist = PlayerDist; bool run = _speed > def.walkSpeed * 1.5f;
            if (def.heavyFootsteps)
            {
                float vol = Mathf.Lerp(0.3f, 1f, w) * (run ? W.runStepBoost : 1f);
                SfxPlayer.Instance.Play(w > 0.7f ? SfxId.DinoStepHeavy : SfxId.DinoStep, transform.position, Mathf.Clamp01(vol));
                if (run || (w > 0.6f && Random.value < 0.35f)) VfxPool.Instance.Play(VfxId.DinoFootDust, transform.position, Vector3.up, null, def.bodyRadius * 0.6f);
            }
            float shake = def.locomotion.stepShake > 0f ? def.locomotion.stepShake : def.heavyFootsteps && def.bodyRadius > 1.4f ? 0.015f * def.bodyRadius : 0f;
            if (shake <= 0f) return;
            float range = W.shakeDistance * Mathf.Lerp(0.5f, 1.2f, w);
            if (dist >= range) return;
            var cam = Camera.main ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
            if (cam) { float k = 1f - dist / range; cam.AddShake(shake * k * k * (run ? 1.5f : 1f), 0.18f); }
        }

        void PlayClip(AudioClip[] set, float vol)
        {
            if (set == null || set.Length == 0 || _lod > 1 || !_audio) return;
            _audio.pitch = Random.Range(0.92f, 1.08f); _audio.PlayOneShot(set[Random.Range(0, set.Length)], vol * AudioBus.Sfx);
        }

        // ------------------------------------------------------------------ damage
        /// <summary>bare-hand damage multiplier: full at or below PerceptionConfig.unarmedFullBody, x (full / radius)^2 above</summary>
        public static float UnarmedScale(DinosaurDefinition d)
        {
            if (!d) return 1f;
            if (d.unarmedDamageScale >= 0f) return d.unarmedDamageScale;
            float r = Mathf.Max(0.01f, d.bodyRadius), full = C.unarmedFullBody;
            return r <= full ? 1f : Mathf.Clamp01((full / r) * (full / r));
        }

        public void TakeHit(HitInfo hit)
        {
            if (State == DinoState.Dead) return;
            float dmg = hit.damage * (hit.unarmed ? UnarmedScale(def) : 1f);
            Health -= dmg;
            _provoked = true; _provokedAt = Stimuli.Now; if (hit.attacker) _threat = hit.attacker.transform.position;
            _ignoreUntil = -99f; CancelWatch(); if (_prey) EndHunt(false);
            if (_sleeping) WakeUp();
            Senses.OnDamaged(hit.attacker ? hit.attacker.transform.position : hit.point - hit.direction);
            if (hit.knockback > 0f && def && def.bodyRadius <= C.knockbackMaxBody) Knockback(hit);
            if (_bones == null) { var smr = GetComponentInChildren<SkinnedMeshRenderer>(); _bones = smr ? smr.bones : new Transform[0]; }
            BloodFX.Hit(hit.point, hit.direction, Mathf.Clamp(def.bodyRadius * 0.6f, 0.6f, 2.2f), hit.heavy || dmg > def.maxHealth * 0.15f, transform, _bones);
            if (Health <= 0f) { Die(); return; }
            if (_anim) _anim.SetTrigger(HurtH);
            PlayClip(def.hurts, 1f);
            Stimuli.Noise(transform.position, 2f, NoiseTag.Voice, StimulusSource.Creature);
            if (def.temperament == Temperament.Passive || Health < def.maxHealth * def.retreatHealth) Flee(_threat);
            else if (State != DinoState.Chase && State != DinoState.Attack) { _fireMode = FireMode.None; Enter(DinoState.Chase); }
        }

        void Knockback(HitInfo hit)
        {
            Vector3 dir = hit.direction; dir.y = 0f;
            if (hit.attacker) { var a = transform.position - hit.attacker.transform.position; a.y = 0f; if (a.sqrMagnitude > 0.01f) dir = a; }
            if (dir.sqrMagnitude < 1e-4f) dir = -transform.forward;
            float k = 1f - 0.5f * Mathf.Clamp01(def.bodyRadius / Mathf.Max(0.01f, C.knockbackMaxBody));
            _pushDur = Mathf.Max(0.05f, C.knockbackSeconds);
            _push = dir.normalized * hit.knockback * k; _pushUntil = Time.time + _pushDur;
        }

        void Die(bool byPredator = false)
        {
            if (_prey) EndHunt(false);
            _hunter = null;
            Health = 0f; Enter(DinoState.Dead); _speed = 0f; _pendingHit = false; _fireMode = FireMode.None; _sleeping = false;
            DiedAt = GameClock.Now;
            All.Remove(this);            // a body is no longer a live creature (tutorial, minimap, herd); OnDisable removes it again harmlessly
            DeadPose();
            PlayClip(def.deaths, 1f);
            BloodFX.Death(transform.position + transform.forward * def.bodyRadius * 0.4f, Mathf.Clamp(def.bodyRadius * 1.1f, 0.8f, 3.5f), transform);
            TrackSigns.Blood(def, transform.position + transform.forward * def.bodyRadius * 0.4f);
            if (!byPredator) GameEvents.Raise(GameEventType.CreatureKilled, def.id, 1, transform.position);          // a predator's kill is HuntDirector's PredatorHunt
            if (def.sprayLoot) SprayLoot();
            else
            {
                // the body becomes a carcass to butcher; it sinks and switches this object off when done (Carcass)
                var c = GetComponent<Carcass>(); if (!c) c = gameObject.AddComponent<Carcass>();
                c.Setup(def.displayName, def.id, def.meat, def.hide, def.bone);
            }
        }

        void DeadPose()
        {
            if (!_anim) return;
            // the corpse animator is paused when far: keep the Death state and the pose while it is disabled
            _anim.keepAnimatorStateOnDisable = true; _anim.writeDefaultValuesOnDisable = false;
            _anim.enabled = true; _anim.SetBool(DeadH, true); _anim.SetFloat(SpeedH, 0f); _corpseAnimT = 0f;
        }

        /// <summary>old loot path (DinosaurDefinition.sprayLoot): pickups in a ring next to the body</summary>
        void SprayLoot()
        {
            var db = ItemDatabase.Instance;
            if (!db) return;
            int i = 0;
            void Drop(string id, int n) { var it = db.Item(id); if (it && n > 0) WorldPickup.Drop(it, n, transform.position + Quaternion.Euler(0, 70 * i++, 0) * transform.right * (def.bodyRadius + 0.8f) + Vector3.up * 0.5f); }
            Drop("raw_meat", def.meat); Drop("hide", def.hide); Drop("bone", def.bone);
        }

        /// <summary>the death clip plays near the player; a corpse stops animating when far (and at mid distance once the clip is over)</summary>
        void CorpseAnimator(int lod)
        {
            if (!_anim) return;
            bool run = lod == 0 || (lod <= 2 && _corpseAnimT < CorpseAnimSeconds);
            if (_anim.enabled != run) _anim.enabled = run;
            if (run) _corpseAnimT += Time.deltaTime;
        }

        void OnGameEvent(GameEvent e)
        {
            if (State == DinoState.Dead || !Herbivore) return;
            if (e.type == GameEventType.PredatorWarning && _player && PlayerDist < 160f) { Flee(_player.position + (transform.position - _player.position).normalized * -5f); }
        }

        // ------------------------------------------------------------------ save / restore (CreatureSave)
        [System.Serializable]
        public struct SavedState
        {
            public bool dead; public float health; public Vector3 pos; public float yaw; public double diedAt;
            public bool carcass; public int meat, hide, bone; public double expireAt; public bool gone;
            /// <summary>predators: hunger 0..1 (appended; older saves read 0)</summary>
            public float hunger;
        }

        public SavedState Capture()
        {
            var s = new SavedState { dead = State == DinoState.Dead, health = _started ? Health : (def ? def.maxHealth : 0f), pos = transform.position, yaw = transform.eulerAngles.y, diedAt = DiedAt, hunger = _hunger };
            if (_hasPending && !_started) return _pending;
            var c = GetComponent<Carcass>();
            if (s.dead && c) { s.carcass = true; s.meat = c.meat; s.hide = c.hide; s.bone = c.bone; s.expireAt = c.ExpireAt; s.gone = c.Sinking || !gameObject.activeSelf; }
            else if (s.dead) s.gone = !gameObject.activeSelf;
            return s;
        }

        /// <summary>put a saved state back (before Start: applied when Start runs)</summary>
        public void RestoreSaved(SavedState s)
        {
            if (!_started) { _pending = s; _hasPending = true; return; }
            ApplySaved(s);
        }

        void ApplySaved(SavedState s)
        {
            if (!def) return;
            transform.rotation = Quaternion.Euler(0f, s.yaw, 0f);
            transform.position = s.pos; Snap();
            // hunts are not saved: a load ends any hunt (HuntDirector restarts its delay)
            if (_prey) EndHunt(false);
            _hunter = null; _meal = null; _mealRest = false; _restUntil = -1; _restHold = false;
            if (_hp.hunts) _hunger = Mathf.Clamp01(s.hunger);
            if (!s.dead)
            {
                Health = Mathf.Clamp(s.health, 1f, def.maxHealth);
                Senses.ForgetAll(); _provoked = false; _goal = Goal.None; _fleeAt = -1f; _ignoreUntil = -99f; CancelWatch(); Enter(DinoState.Idle);
                return;
            }
            // a body: no death sounds, blood or events on load
            Health = 0f; Enter(DinoState.Dead); _speed = 0f; DiedAt = s.diedAt; All.Remove(this);
            DeadPose(); if (_anim) _corpseAnimT = CorpseAnimSeconds;
            if (s.gone) { gameObject.SetActive(false); return; }
            if (!def.sprayLoot && s.carcass)
            {
                var c = GetComponent<Carcass>(); if (!c) c = gameObject.AddComponent<Carcass>();
                c.Setup(def.displayName, def.id, def.meat, def.hide, def.bone);
                c.RestoreLeft(s.meat, s.hide, s.bone, s.expireAt);
            }
        }

        // ------------------------------------------------------------------ movement
        void Steer(Vector3 target, float maxSpeed, float dt, bool chase = false)
        {
            Vector3 d = target - transform.position; d.y = 0;
            float dist = d.magnitude;
            float want = dist < 1.5f && !chase ? 0f : maxSpeed;
            // slow down to turn: big animals cannot pivot at full speed, and keep walking round in an arc instead of spinning on the spot
            float ang = d.sqrMagnitude > 0.01f ? Vector3.Angle(transform.forward, d) : 0f;
            if (ang > 60f) { float slow = want * 0.4f; want = def.Weight01 > 0.55f && want > 0f ? Mathf.Max(slow, def.walkSpeed * 0.3f) : slow; }
            // obstacle / water ahead: turn away
            if (Blocked(out Vector3 avoid)) { d = avoid; want *= 0.5f; }
            float brake = Mathf.Lerp(3f, 1.3f, def.Weight01);                   // a heavy animal cannot stop on the spot either
            _speed = Mathf.MoveTowards(_speed, want, def.acceleration * dt * (want < _speed ? brake : 1f));
            Face(transform.position + d, dt);
        }

        void Face(Vector3 p, float dt)
        {
            Vector3 d = p - transform.position; d.y = 0; if (d.sqrMagnitude < 0.01f) return;
            // standing still a heavy animal shuffles round slowly (pivotRate); a small one spins on the spot
            float mv = Mathf.Clamp01(_speed / Mathf.Max(0.1f, def.walkSpeed));
            float ts = def.turnSpeed * _indivTurn * Mathf.Lerp(Mathf.Clamp(def.locomotion.pivotRate, 0.1f, 1f), 1f, mv) * (_speed > def.walkSpeed * 1.5f ? 0.7f : 1f);
            var want = Quaternion.LookRotation(d.normalized);
            var cur = Quaternion.Euler(0, transform.eulerAngles.y, 0);
            var yaw = Quaternion.RotateTowards(cur, want, ts * dt);
            transform.rotation = yaw * Quaternion.Euler(_pitch, 0, 0);
        }

        float _pitch;
        void Move(float dt)
        {
            Vector3 before = transform.position;
            if (_speed > 0.01f) transform.position += Quaternion.Euler(0, transform.eulerAngles.y, 0) * Vector3.forward * _speed * dt;
            if (Time.time < _pushUntil) { float k = (_pushUntil - Time.time) / _pushDur; transform.position += _push * (k * 1.6f) * dt; }
            Snap();
            Tracks(before, dt);
        }

        /// <summary>footprints every step (fewer when far from the player) and blood drops while badly hurt (TrackSigns)</summary>
        void Tracks(Vector3 before, float dt)
        {
            Vector3 mv = transform.position - before; mv.y = 0f; float moved = mv.magnitude;
            if (moved < 0.001f) return;
            var tp = def.tracks;
            if (tp.printSize > 0f)
            {
                _stepDist += moved;
                float step = tp.stride * 0.5f * W.printEveryStrides * Mathf.Max(1f, _speed / Mathf.Max(0.1f, def.walkSpeed) * 0.6f);   // running strides are longer
                if (_stepDist >= step)
                {
                    _stepDist = Mathf.Min(_stepDist - step, step);
                    _stepLeft = !_stepLeft; _stepCount++;
                    bool sparse = PlayerDist > W.sparsePrintDistance;
                    if (!sparse || _stepCount % 3 == 0) TrackSigns.Step(def, transform.position, mv / moved, _stepLeft);
                }
            }
            if (Health < def.maxHealth * 0.4f)
            {
                _bloodDist += moved;
                if (_bloodDist > 3.5f) { _bloodDist = 0f; TrackSigns.Blood(def, transform.position - transform.forward * def.bodyRadius * 0.4f + transform.right * Random.Range(-0.4f, 0.4f)); }
            }
        }

        void Snap()
        {
            var t = Terrain.activeTerrain; if (!t) return;
            Vector3 p = transform.position; float ty = t.transform.position.y;
            float h = t.SampleHeight(p) + ty;
            Vector3 fwd = Quaternion.Euler(0, transform.eulerAngles.y, 0) * Vector3.forward;
            float L = Mathf.Max(1f, def.bodyRadius * 1.6f);
            float hf = t.SampleHeight(p + fwd * L) + ty, hb = t.SampleHeight(p - fwd * L) + ty;
            _pitch = Mathf.Lerp(_pitch, Mathf.Clamp(-Mathf.Atan2(hf - hb, 2f * L) * Mathf.Rad2Deg, -18f, 18f), 0.1f);
            p.y = Mathf.Max(h, def.temperament == Temperament.AmbientSwimmer ? p.y : -99f);
            transform.position = p;
            transform.rotation = Quaternion.Euler(_pitch, transform.eulerAngles.y, 0);
        }

        bool Blocked(out Vector3 avoid)
        {
            avoid = Vector3.zero;
            var t = Terrain.activeTerrain;
            Vector3 fwd = Quaternion.Euler(0, transform.eulerAngles.y, 0) * Vector3.forward;
            Vector3 ahead = transform.position + fwd * (def.bodyRadius * 2f + _speed * 0.6f);
            bool water = t && t.SampleHeight(ahead) + t.transform.position.y < (InWade(ahead) ? -1f : 0.6f);
            bool wall = Physics.SphereCast(transform.position + Vector3.up * def.bodyRadius, def.bodyRadius * 0.6f, fwd, out var hit, def.bodyRadius * 1.5f + _speed * 0.5f, Masks.NotPlayer, QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(transform) && !(hit.collider is TerrainCollider);
            // Phase 2: no-go areas and heat rings (a chase only turns at the hard ones; fleeing crosses anything)
            bool chasing = State == DinoState.Chase || State == DinoState.Attack || _prey;
            bool nogo = State != DinoState.Flee && _speed > 0.05f && WildlifeZones.Forbidden(def, ahead, true, chasing);
            if (nogo) water = true;
            if (!water && !wall) return false;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 hn = HomeNow;
            avoid = (Vector3.Dot(right, hn - transform.position) > 0 ? right : -right) + fwd * 0.2f;
            if (water && _goal != Goal.Drink && _goal != Goal.Herd && _goal != Goal.Routine) _dest = RandomPointNear(hn, homeRadius);
            else if (water && !nogo && _goal == Goal.Drink) { _goal = Goal.None; Enter(DinoState.Drink); }              // reached the shore on the way to drink
            return true;
        }

        bool Arrived(float r) { var d = _dest - transform.position; d.y = 0; return d.magnitude < r; }
        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

        Vector3 RandomPointNear(Vector3 c, float r)
        {
            var t = Terrain.activeTerrain;
            for (int i = 0; i < 8; i++)
            {
                var p = c + Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward * Random.Range(r * 0.2f, r);
                if ((!t || t.SampleHeight(p) + t.transform.position.y > 0.8f || InWade(p)) && !FearBlocks(p) && !WildlifeZones.Forbidden(def, p)) return p;
            }
            return c;
        }

        public void ForceState(DinoState s) => Enter(s);

        // ------------------------------------------------------------------ Phase 2: routines, wading, far visibility
        /// <summary>free to take the next routine step (alive, calm, not in a herd, not hunting, sleeping, fed-resting or at a fire)</summary>
        public bool RoutineReady =>
            IsAlive && def && !Herd && !_prey && _fireMode == FireMode.None && !_sleeping && !_restHold && GameClock.Now >= _restUntil &&
            Senses.Level < AwarenessLevel.Suspicious &&
            (State == DinoState.Idle || (State == DinoState.Eat && !_atCarcass) || State == DinoState.Rest || State == DinoState.Return ||
             (State == DinoState.Observe && !WatchingPrey) || (State == DinoState.Wander && (_goal == Goal.None || _goal == Goal.Routine)));

        /// <summary>on its way to a routine point</summary>
        public bool OnRoutineLeg => State == DinoState.Wander && _goal == Goal.Routine;

        /// <summary>a routine sends it walking to p (x walk speed); false = busy with something more important now</summary>
        public bool RoutineWalk(Vector3 p, float speedMul)
        {
            if (!RoutineReady) return false;
            _routineSpeed = Mathf.Clamp(speedMul, 0.3f, 2f);
            if (OnRoutineLeg && (_dest - p).sqrMagnitude < 1f) return true;
            _goal = Goal.Routine; _dest = p; _keepDest = true; Enter(DinoState.Wander); _keepDest = false;
            return true;
        }

        /// <summary>a routine stop: feed (head down, also fishing in the shallows) or watch a point for 'seconds'</summary>
        public bool RoutineAct(bool feed, float seconds, Vector3 lookAt)
        {
            if (!RoutineReady || OnRoutineLeg) return false;
            float now = Stimuli.Now;
            if (feed)
            {
                if (State != DinoState.Eat) { Face(lookAt, 1f); Enter(DinoState.Eat); }
                _stateT = -Mathf.Max(1f, seconds);
                if (!Herbivore) _atCarcass = NearestCarcass(transform.position, def.bodyRadius + 6f) != null;      // feeding at a body (journal: seen eating)
                return true;
            }
            _lookOverride = lookAt; _lookOverrideUntil = now + seconds; _threatLookUntil = now + seconds;
            if (State != DinoState.Observe) Enter(DinoState.Observe);
            return true;
        }

        /// <summary>shallow water within r of c counts as walkable ground for 'seconds' (a wading visit)</summary>
        public void SetWade(Vector3 c, float r, float seconds) { _wadeC = c; _wadeR = r; _wadeUntil = Time.time + Mathf.Max(0f, seconds); }
        public bool Wading => Time.time < _wadeUntil;
        bool InWade(Vector3 p) => Time.time < _wadeUntil && Flat(p - _wadeC) < _wadeR;

        /// <summary>any of its renderers seen by a camera this frame</summary>
        bool SeenByCamera()
        {
            if (_rends == null) _rends = GetComponentsInChildren<Renderer>(false);
            for (int i = 0; i < _rends.Length; i++) { var r = _rends[i]; if (r && r.isVisible) return true; }
            return false;
        }

        /// <summary>
        /// keep a creature drawn out to at least 'minDistance' m: lowers this instance's last LOD threshold (screen relative
        /// height at the main camera's field of view and QualitySettings.lodBias) when it would cull it sooner. Returns the
        /// resulting cull distance (0 = no LODGroup).
        /// </summary>
        public static float ExtendCull(GameObject go, float minDistance, float fov = 0f)
        {
            var lg = go ? go.GetComponentInChildren<LODGroup>() : null; if (!lg) return 0f;
            var lods = lg.GetLODs(); if (lods == null || lods.Length == 0) return 0f;
            if (fov <= 0f) { var cam = Camera.main; fov = cam ? cam.fieldOfView : 60f; }
            float size = WorldSize(lg), k = 2f * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad), bias = Mathf.Max(0.01f, QualitySettings.lodBias);
            int last = lods.Length - 1;
            float want = size * bias / (k * Mathf.Max(1f, minDistance));
            if (minDistance > 0f && lods[last].screenRelativeTransitionHeight > want)
            {
                lods[last].screenRelativeTransitionHeight = want;
                for (int i = last - 1; i >= 0; i--)
                    if (lods[i].screenRelativeTransitionHeight <= lods[i + 1].screenRelativeTransitionHeight) lods[i].screenRelativeTransitionHeight = lods[i + 1].screenRelativeTransitionHeight * 1.25f;
                lg.SetLODs(lods);
            }
            float t = Mathf.Max(1e-4f, lods[last].screenRelativeTransitionHeight);
            return size * bias / (k * t);
        }

        /// <summary>distance (m) at which this LODGroup culls the object at the given field of view (checks)</summary>
        public static float CullDistance(LODGroup lg, float fov = 60f)
        {
            if (!lg) return 0f; var lods = lg.GetLODs(); if (lods == null || lods.Length == 0) return 0f;
            float k = 2f * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad), t = Mathf.Max(1e-4f, lods[lods.Length - 1].screenRelativeTransitionHeight);
            return WorldSize(lg) * Mathf.Max(0.01f, QualitySettings.lodBias) / (k * t);
        }

        static float WorldSize(LODGroup lg)
        {
            var s = lg.transform.lossyScale;
            return lg.size * Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
        }

        /// <summary>tests / story: thirsty now (goes to fresh water at the next calm moment)</summary>
        public void ThirstNow() { _nextDrinkAt = 0; _nextDrinkLook = 0f; }

        /// <summary>after sleeping: every creature forgets the player (awareness, memory, smells)</summary>
        public static void ForgetPlayerAll()
        {
            for (int i = 0; i < All.Count; i++)
            {
                var d = All[i]; if (!d) continue;
                d.Senses.ForgetAll(); d._provoked = false; d._ignoreUntil = -99f; d._ignorePlayerUntil = -99f; d._stalkRoll = -1f;
                if (d.State == DinoState.Chase || d.State == DinoState.Investigate || d.State == DinoState.Observe || d.State == DinoState.Alert) d.Enter(DinoState.Idle);
            }
        }
    }
}
