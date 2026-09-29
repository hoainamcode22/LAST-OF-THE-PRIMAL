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
    /// awareness level into behaviour. Herbivores graze, drink, sleep, watch, back away, raise alarms and flee;
    /// defensive ones hold their ground and charge up close; predators ignore, investigate, approach, circle a fire,
    /// attack and retreat, and follow the last known position instead of the live player once sight is lost. Campfire
    /// fear is per species (DinosaurDefinition.fireFear). AI tiers by distance to the player (PerceptionConfig): near /
    /// medium / far think at 0.2 / 0.4 / 1 s, very far is frozen with the Animator off. Bare-hand hits are scaled by body
    /// size and push small creatures back. The Animator runs in place (Speed matches the real speed).
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
        public Transform LookTarget => _player && PlayerAlive && (State == DinoState.Observe || State == DinoState.Alert || State == DinoState.Investigate ||
                                       State == DinoState.Chase || State == DinoState.Attack) && PlayerDist < 40f && Tracking ? _player : null;
        /// <summary>where it looks when the player is not the target: the last stimulus (noise, bush, smell)</summary>
        public bool TryGetLookPoint(out Vector3 p)
        {
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
        /// <summary>cached layer masks (nested static class: initialised on first use from Update, never during deserialisation)</summary>
        static class Masks { internal static readonly int NotPlayer = ~LayerMask.GetMask("Player"); }
        static PerceptionConfig C => PerceptionConfig.Instance;

        // behaviour memory
        enum Goal { None, Threat, Approach, Food, Search, Drink, Avoid }
        Goal _goal; int _searchLeft; bool _keepDest; float _goalMemTime = -99f; bool _fireOnPlayer;
        AwarenessLevel _prevLevel; float _lastShare = -99f, _lastNotice = -99f, _lastSuspiciousCall = -99f;
        // daily life
        bool _sleeping; double _nextDrinkAt; bool _hasDrinkSpot; Vector3 _drinkSpot; float _nextDrinkLook, _drinkWalk = 60f;
        // campfire
        FireMode _fireMode; Campfire _fire; bool _fireArrived; float _firePatience; float _fireR, _fireUntil, _fireIgnoreUntil = -99f, _fireLeftUntil = -99f, _circleAngle, _circleDir = 1f, _provokedAt = -99f;
        // knockback
        Vector3 _push; float _pushUntil, _pushDur;

        void Awake()
        {
            _anim = GetComponent<Animator>(); if (!_anim) _anim = GetComponentInChildren<Animator>();
            _ev = GetComponentInChildren<CharacterAnimationEvents>();
            if (!GetComponent<DinoLife>()) gameObject.AddComponent<DinoLife>();     // head look, attack tell, eyes
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

        // ------------------------------------------------------------------ brain
        Transform[] _bones; float _nextDrip;

        void Update()
        {
            if (def == null) return;
            FindPlayer();
            var c = C;
            float dist = PlayerDist;
            int lod = dist < c.nearDistance ? 0 : dist < c.mediumDistance ? 1 : dist < c.farDistance ? 2 : 3;
            if (lod != _lod) { _lod = lod; if (_anim && State != DinoState.Dead) _anim.enabled = lod < 3; }
            if (State == DinoState.Dead) { CorpseAnimator(lod); return; }
            if (lod == 3) { _lodT += Time.deltaTime; if (_lodT < c.veryFarStep) return; }
            float dt = lod == 3 ? _lodT : Time.deltaTime; _lodT = 0f;
            _stateT += dt; _think -= dt;
            if (_think <= 0f)
            {
                _think = lod == 0 ? c.thinkNear : lod == 1 ? c.thinkMedium : c.thinkFar;
                float tdt = Mathf.Clamp(Time.time - _lastThink, 0.01f, 2f); _lastThink = Time.time;
                Think(dist, tdt);
            }
            Act(dt, dist);
            Move(dt);
            if (_anim && _anim.enabled) _anim.SetFloat(SpeedH, _speed, 0.12f, dt);
            if (_pendingHit && Time.time >= _hitAt) ResolveHit();
            // badly hurt: a blood trail behind it while it moves
            if (lod == 0 && _speed > 0.5f && Health < def.maxHealth * 0.4f && Time.time > _nextDrip)
            {
                _nextDrip = Time.time + Random.Range(0.5f, 1.1f);
                BloodFX.Drip(transform.position - transform.forward * def.bodyRadius * 0.3f + transform.right * Random.Range(-0.3f, 0.3f) * def.bodyRadius, def.bodyRadius * 0.6f, transform);
            }
            if (Time.time > _nextCall && lod <= 1 && State != DinoState.Chase && !_sleeping) { _nextCall = Time.time + Random.Range(15f, 40f); PlayClip(def.calls, 0.8f); }
        }

        bool Calm => State == DinoState.Idle || State == DinoState.Wander || State == DinoState.Eat || State == DinoState.Rest || State == DinoState.Drink;

        void Think(float dist, float dt)
        {
            var c = C;
            Life(c);
            Senses.Tick(this, dt, _lod, c);
            var lvl = Senses.Level; bool see = Senses.SeesPlayer;
            if (see && dist < 35f && Sighted.Add(def.id)) GameEvents.Raise(GameEventType.CreatureSighted, def.id, 1, transform.position);
            if (lvl > _prevLevel) LevelRose(_prevLevel, lvl, c);
            _prevLevel = lvl;
            if (_sleeping && lvl >= AwarenessLevel.Suspicious) WakeUp();
            if (Stimuli.Now - _provokedAt > 20f && State != DinoState.Chase && State != DinoState.Attack && State != DinoState.Flee) _provoked = false;
            if (FireCheck(c, dist)) return;
            bool hurt = Health < def.maxHealth * def.retreatHealth;
            Vector3 threatPos = see && _player ? _player.position : Senses.HasMemory ? Senses.LastKnownPos : _player ? _player.position : transform.position;
            switch (def.temperament)
            {
                case Temperament.Passive:
                {
                    // flee: hit, too close, alert and close in view, or a threat it knows is near but can no longer see
                    bool close = see && dist < def.personalSpace * 1.6f && lvl >= AwarenessLevel.Alerted;
                    float memDist = Senses.HasMemory ? Flat(Senses.LastKnownPos - transform.position) : float.MaxValue;
                    bool heardClose = !see && lvl >= AwarenessLevel.Alerted && memDist < def.personalSpace;
                    bool lostClose = !see && lvl == AwarenessLevel.Engaged && memDist < def.personalSpace * 2.5f;
                    if (_provoked || close || heardClose || lostClose || (see && dist < def.personalSpace * 0.6f)) { Flee(threatPos); return; }
                    if (State == DinoState.Flee) return;
                    if (lvl >= AwarenessLevel.Investigating && !see && Senses.HasMemory) { WalkAway(Senses.LastKnownPos, Goal.Avoid); return; }
                    if (lvl >= AwarenessLevel.Suspicious) { if (State != DinoState.Observe) Enter(DinoState.Observe); return; }
                    if (Senses.HasAversion && Calm && _goal != Goal.Avoid) { WalkAway(Senses.AversionPos, Goal.Avoid); return; }
                    break;
                }
                case Temperament.Defensive:
                    if (_provoked || (lvl == AwarenessLevel.Engaged && see && dist < def.personalSpace))
                    {
                        if (hurt) { Flee(threatPos); return; }
                        if (State != DinoState.Chase && State != DinoState.Attack) { Roar(); Enter(DinoState.Chase); }
                        return;
                    }
                    if (State == DinoState.Flee || State == DinoState.Chase || State == DinoState.Attack) break;
                    if (lvl >= AwarenessLevel.Suspicious) { if (State != DinoState.Observe) Enter(DinoState.Observe); return; }
                    if (Senses.HasAversion && Calm && _goal != Goal.Avoid) { WalkAway(Senses.AversionPos, Goal.Avoid); return; }
                    break;
                case Temperament.Territorial:
                case Temperament.Predator:
                {
                    if (hurt && def.temperament == Temperament.Predator) { Flee(_player ? _player.position : transform.position - transform.forward); return; }
                    if (State == DinoState.Flee) return;
                    Vector3 pp = _player ? _player.position : home;
                    bool inTerritory = def.temperament == Temperament.Predator || Flat(pp - home) < def.territoryRadius;
                    bool tracking = Tracking;
                    if (PlayerAlive && ((lvl == AwarenessLevel.Engaged && tracking && dist < def.aggroRange && inTerritory) || _provoked))
                    {
                        if (State != DinoState.Chase && State != DinoState.Attack) { if (State != DinoState.Alert && dist > def.attackRange * 3f) { Enter(DinoState.Alert); Roar(); } else Enter(DinoState.Chase); }
                        return;
                    }
                    if (State == DinoState.Chase || State == DinoState.Attack || State == DinoState.Alert) break;
                    if (PlayerAlive && lvl == AwarenessLevel.Engaged && tracking)
                    {
                        // seen but still far: stalk closer (inside its territory), else watch from the edge
                        if (inTerritory || Flat(pp - home) < def.territoryRadius * 1.3f) { GoInvestigate(pp, Goal.Approach); return; }
                        if (State != DinoState.Observe) Enter(DinoState.Observe);
                        return;
                    }
                    if (lvl >= AwarenessLevel.Investigating && Senses.HasMemory)
                    {
                        bool newInfo = Senses.LastKnownTime > _goalMemTime + 0.5f && (_dest - Senses.LastKnownPos).sqrMagnitude > 16f;
                        if ((_goal != Goal.Threat && _goal != Goal.Search) || State != DinoState.Investigate || newInfo) GoInvestigate(Senses.LastKnownPos, Goal.Threat);
                        return;
                    }
                    if (lvl == AwarenessLevel.Suspicious && Calm) { Enter(DinoState.Observe); return; }
                    if (Senses.HasInterest && Senses.Interest >= c.interestThreshold && (Calm || State == DinoState.Observe) && Stimuli.Now > _fireLeftUntil)
                    {
                        if (_goal != Goal.Food) GameEvents.Raise(GameEventType.ScentInvestigated, def.id, 1, Senses.InterestPos);
                        GoInvestigate(Senses.InterestPos, Goal.Food); return;
                    }
                    break;
                }
            }
            if (State == DinoState.Observe && lvl == AwarenessLevel.Unaware && _stateT > 2f) Enter(DinoState.Idle);
            if (State == DinoState.Chase)
            {
                bool leash = Vector3.Distance(transform.position, home) > def.territoryRadius * 2.5f;
                float giveUp = Herbivore ? Mathf.Max(def.personalSpace * 2f, 8f) : def.aggroRange * 1.8f;
                if (!PlayerAlive || dist > giveUp || leash)
                {
                    _provoked = false;
                    if (!Herbivore && !leash && PlayerAlive && Senses.HasMemory) GoInvestigate(Senses.LastKnownPos, Goal.Search);
                    else Enter(Herbivore ? DinoState.Observe : DinoState.Return);
                }
            }
        }

        /// <summary>awareness went up: wake, call, share with the herd / pack, tell the game once in a while</summary>
        void LevelRose(AwarenessLevel from, AwarenessLevel to, PerceptionConfig c)
        {
            float now = Stimuli.Now;
            if (to >= AwarenessLevel.Suspicious && from < AwarenessLevel.Suspicious)
            {
                if (now - _lastNotice > 10f && (Senses.LastSense == SenseKind.Sight || Senses.LastSense == SenseKind.Noise || Senses.LastSense == SenseKind.Motion || Senses.LastSense == SenseKind.Scent))
                { _lastNotice = now; GameEvents.Raise(GameEventType.PlayerNoticed, def.id, (int)to, transform.position); }
                if (now - _lastSuspiciousCall > 8f && Random.value < 0.4f && _lod == 0) { _lastSuspiciousCall = now; PlayClip(def.calls, 0.45f); }
                if (_anim && Calm) { _anim.SetInteger(ActionTypeH, DinoActions.LookAround); _anim.SetTrigger(ActionH); }
            }
            if (to >= AwarenessLevel.Alerted && from < AwarenessLevel.Alerted && now - _lastShare > c.shareCooldown)
            {
                _lastShare = now;
                if (Herbivore && def.alarmCall) PlayClip(def.calls, 1f);
                ShareAlert(to == AwarenessLevel.Engaged && !Herbivore ? 1f : c.herdShareLevel, c);
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
                if (d.def == def) { if (r > 0f && q < r2) d.Senses.Share(level, at, c); }
                else if (Herbivore && def.alarmCall && d.Herbivore && q < a2) d.Senses.Share(c.alarmShareLevel, at, c);
            }
        }

        // ------------------------------------------------------------------ daily life
        bool Resting(float hour)
        {
            var tm = TimeManager.Instance; if (!tm) return false;
            switch (def.activity)
            {
                case ActivityCycle.Diurnal: return tm.NightFactor > 0.7f;
                case ActivityCycle.Nocturnal: return hour >= 11f && hour < 15f;
                default: return false;
            }
        }

        void Life(PerceptionConfig c)
        {
            var tm = TimeManager.Instance;
            bool rest = tm && Resting(tm.hour);
            if (_sleeping && (!rest || !Calm)) WakeUp();
            Senses.SightMul = _sleeping ? c.sleepSight : 1f;
            Senses.HearingMul = _sleeping ? c.sleepHearing : 1f;
            if (!Calm || Senses.Level >= AwarenessLevel.Suspicious) return;
            if (rest && !_sleeping && (State == DinoState.Idle || State == DinoState.Eat || (State == DinoState.Wander && _goal == Goal.None)))
            {
                _sleeping = true; _goal = Goal.None; Enter(DinoState.Rest); _stateT = -Random.Range(c.sleepSeconds.x, c.sleepSeconds.y);
                return;
            }
            // thirst: go to fresh water now and then (daytime for diurnal species)
            if (!rest && !_sleeping && def.drinkEveryHours > 0f && GameClock.Now >= _nextDrinkAt && _goal == Goal.None && (State == DinoState.Idle || State == DinoState.Eat || State == DinoState.Wander) && Time.time >= _nextDrinkLook)
            {
                _nextDrinkLook = Time.time + 20f;
                if (!_hasDrinkSpot) _hasDrinkSpot = DrinkSpots.Nearest(home, def.drinkSearchRadius, out _drinkSpot);
                if (_hasDrinkSpot && !FearBlocks(_drinkSpot)) { _goal = Goal.Drink; GoTo(_drinkSpot, DinoState.Wander); _drinkWalk = Flat(_drinkSpot - transform.position) / Mathf.Max(0.3f, def.walkSpeed) * 2f + 20f; }
                else _nextDrinkAt = GameClock.Now + GameClock.Hours(Mathf.Max(0.5f, def.drinkEveryHours) * 0.5f);
            }
        }

        void WakeUp() { _sleeping = false; if (State == DinoState.Rest) _stateT = 99f; }

        float RoamRadius
        {
            get
            {
                var tm = TimeManager.Instance; if (!tm || tm.NightFactor < 0.7f) return homeRadius;
                if (def.activity == ActivityCycle.Nocturnal) return homeRadius * C.nightRoam;
                if (def.activity == ActivityCycle.Diurnal) return homeRadius * C.nightHuddle;
                return homeRadius;
            }
        }

        // ------------------------------------------------------------------ goals
        void GoTo(Vector3 p, DinoState s) { _dest = p; _keepDest = true; Enter(s); _keepDest = false; }

        void GoInvestigate(Vector3 p, Goal g)
        {
            bool same = State == DinoState.Investigate && _goal == g;
            _goal = g; _dest = p; _goalMemTime = Senses.LastKnownTime;
            if (g == Goal.Threat || g == Goal.Search) _searchLeft = C.searchPoints;
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
        /// inside one makes it wait, circle, watch or walk around at the edge until its patience runs out, then leave.
        /// Weak fear (below 0.5) only hesitates. Returns true when the fire decided this tick.
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
                    if (ff.predatorFear < 0.5f) { _fireIgnoreUntil = now + 30f; return false; }       // hesitated long enough: walks in
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
                StartFire(inside, rIn, FireMode.Avoid, c); _fireOnPlayer = false; return true;
            }
            // the thing it is going for is inside a fear circle
            Vector3 target; bool onPlayer = false;
            if (State == DinoState.Chase || State == DinoState.Alert || State == DinoState.Attack || (State == DinoState.Investigate && _goal == Goal.Approach)) { if (!_player) return false; target = _player.position; onPlayer = true; }
            else if (State == DinoState.Investigate || (State == DinoState.Wander && _goal == Goal.Drink)) target = _dest;
            else return false;
            var fire = FireSense.FearAt(def, target, out float r2);
            if (!fire) return false;
            FireMode mode = ff.predatorFear < 0.5f ? FireMode.Wait : ff.response == FireResponse.Circle ? FireMode.Circle : ff.response == FireResponse.Observe ? FireMode.Observe
                          : ff.response == FireResponse.Avoid ? FireMode.Avoid : ff.response == FireResponse.Leave ? FireMode.None : FireMode.Wait;
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
                    Steer(p, def.walkSpeed, dt);
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
            if (_fireMode != FireMode.None && State != DinoState.Dead && State != DinoState.Flee && State != DinoState.Return) { FireAct(dt); return; }
            switch (State)
            {
                case DinoState.Idle:
                    _speed = Mathf.MoveTowards(_speed, 0f, def.acceleration * dt * 2f);
                    if (_stateT > Random.Range(4f, 9f)) Enter(Random.value < 0.55f ? DinoState.Wander : Random.value < 0.6f ? DinoState.Eat : DinoState.Rest);
                    break;
                case DinoState.Wander:
                    Steer(_dest, def.walkSpeed, dt);
                    if (_goal == Goal.Drink)
                    {
                        if (Flat(_dest - transform.position) < def.bodyRadius * 1.5f + 1.2f) { _goal = Goal.None; Enter(DinoState.Drink); _stateT = -Random.Range(C.drinkSeconds.x, C.drinkSeconds.y) + 10f; }
                        else if (_stateT > _drinkWalk) { _goal = Goal.None; _nextDrinkAt = GameClock.Now + GameClock.Hours(1f); Enter(DinoState.Idle); }
                        break;
                    }
                    if (Arrived(2f) || _stateT > 25f) { _goal = Goal.None; Enter(Random.value < 0.5f ? DinoState.Eat : DinoState.Idle); }
                    break;
                case DinoState.Drink:
                    _speed = Mathf.MoveTowards(_speed, 0f, def.acceleration * dt * 2f);
                    if (_stateT > 10f)
                    {
                        _nextDrinkAt = GameClock.Now + GameClock.Hours(Mathf.Max(0.5f, def.drinkEveryHours) * Random.Range(0.8f, 1.2f));
                        Enter(DinoState.Wander);
                    }
                    break;
                case DinoState.Eat: case DinoState.Rest:
                    _speed = Mathf.MoveTowards(_speed, 0f, def.acceleration * dt * 2f);
                    if (_sleeping) { if (_stateT > 0f) { _stateT = -Random.Range(C.sleepSeconds.x, C.sleepSeconds.y); if (_anim && Random.value < 0.3f) { _anim.SetInteger(ActionTypeH, DinoActions.Rest); _anim.SetTrigger(ActionH); } } break; }
                    if (_stateT > Random.Range(8f, 16f)) Enter(DinoState.Wander);
                    break;
                case DinoState.Observe:
                case DinoState.Alert:
                    _speed = Mathf.MoveTowards(_speed, 0f, def.acceleration * dt * 3f);
                    if (_player && Senses.SeesPlayer) Face(_player.position, dt);
                    else if (Senses.HasLookPoint) Face(Senses.LookPoint, dt);
                    if (State == DinoState.Alert && _stateT > 1.8f) Enter(DinoState.Chase);
                    break;
                case DinoState.Investigate:
                {
                    float sp = _goal == Goal.Approach ? def.walkSpeed : _goal == Goal.Food ? def.walkSpeed * 1.1f : def.walkSpeed * 1.3f;
                    if (_goal == Goal.Approach && _player && Senses.SeesPlayer) _dest = _player.position;
                    Steer(_dest, sp, dt);
                    if (Arrived(_goal == Goal.Approach ? def.attackRange * 2f : 3f) || _stateT > 15f) ArriveInvestigate();
                    break;
                }
                case DinoState.Flee:
                {
                    Vector3 away = transform.position - _threat; away.y = 0; if (away.sqrMagnitude < 0.01f) away = transform.forward;
                    away.Normalize();
                    // zig-zag prey: a sideways swing on top of the escape direction (harder to hit with arrows)
                    if (def.fleeZigZag > 0f) away = (away + Vector3.Cross(Vector3.up, away) * (Mathf.Sin(_stateT * ZigZagRate + _zigPhase) * def.fleeZigZag * 1.2f)).normalized;
                    _dest = transform.position + away * 20f;
                    Steer(_dest, def.runSpeed, dt);
                    if (_stateT > 9f || Vector3.Distance(transform.position, _threat) > def.fleeDistance) { _provoked = false; _goal = Goal.None; Enter(DinoState.Idle); }
                    break;
                }
                case DinoState.Chase:
                    if (!_player || !PlayerAlive) { Enter(DinoState.Return); break; }
                    if (!Tracking && (!_provoked || Stimuli.Now - _provokedAt > 6f))
                    {
                        // lost it: go to where it was last known and search there
                        if (Senses.HasMemory) GoInvestigate(Senses.LastKnownPos, Goal.Search); else Enter(DinoState.Return);
                        break;
                    }
                    Steer(_player.position, def.runSpeed, dt, true);
                    if (dist < def.attackRange + def.bodyRadius * 0.5f && Time.time >= _attackReady) StartAttack(dist);
                    break;
                case DinoState.Attack:
                    _speed = Mathf.MoveTowards(_speed, 0f, def.acceleration * dt * 3f);
                    if (_player) Face(_player.position, dt * (_attackQueued ? 1.2f : 0.6f));
                    if (_attackQueued && Time.time >= _tellUntil) Strike();
                    if (!_attackQueued && _stateT > 1.6f + _tellTime) Enter(PlayerAlive ? DinoState.Chase : DinoState.Return);
                    break;
                case DinoState.Return:
                    Steer(home, def.walkSpeed * 1.2f, dt);
                    if (Vector3.Distance(transform.position, home) < homeRadius * 0.5f) { _goal = Goal.None; Enter(DinoState.Idle); }
                    break;
            }
        }

        /// <summary>reached the investigated spot: search around the last known position, sniff a smell, or give up</summary>
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
                    // at the smell: sniff (eat pose) for a moment; the spot cools down so it does not come straight back
                    Senses.DropInterest(c); _goal = Goal.None; Enter(DinoState.Eat); _stateT = 4f; return;
                case Goal.Approach:
                    _goal = Goal.None; Enter(DinoState.Observe); return;
                default:
                    _goal = Goal.None; Enter(DinoState.Idle); return;
            }
        }

        void Enter(DinoState s)
        {
            var old = State; State = s; _stateT = 0f;
            if (s != DinoState.Attack) _attackQueued = false;
            if (s == DinoState.Flee || s == DinoState.Chase || s == DinoState.Return) _fireMode = FireMode.None;
            if (s != DinoState.Rest) _sleeping = false;
            if (s == DinoState.Wander && !_keepDest) { _goal = Goal.None; _dest = RandomPointNear(home, RoamRadius); }
            if (!_anim) return;
            _anim.SetBool(AlertH, s == DinoState.Observe || s == DinoState.Alert);
            int action = s == DinoState.Eat ? DinoActions.Eat : s == DinoState.Drink ? DinoActions.Drink : s == DinoState.Rest ? DinoActions.Rest : s == DinoState.Chase && Herbivore ? 10 : DinoActions.None;
            _anim.SetInteger(ActionTypeH, action);
            if (action != DinoActions.None) _anim.SetTrigger(ActionH);
        }

        public void Flee(Vector3 from)
        {
            if (State == DinoState.Dead) return;
            _threat = from; if (State != DinoState.Flee) { Enter(DinoState.Flee); if (Random.value < 0.6f) PlayClip(def.calls, 1f); }
            // herd reaction: same species nearby flee too
            foreach (var d in All)
                if (d != this && d.def == def && d.State != DinoState.Flee && d.State != DinoState.Dead && (d.transform.position - transform.position).sqrMagnitude < 30f * 30f) { d._threat = from; d.Enter(DinoState.Flee); }
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

        void Footstep()
        {
            if (_lod > 0) return;
            if (def.heavyFootsteps)
            {
                SfxPlayer.Instance.Play(def.bodyRadius > 1.5f ? SfxId.DinoStepHeavy : SfxId.DinoStep, transform.position, Mathf.Clamp01(def.bodyRadius / 2f));
                if (_speed > def.walkSpeed * 1.2f) VfxPool.Instance.Play(VfxId.DinoFootDust, transform.position, Vector3.up, null, def.bodyRadius * 0.6f);
                if (def.bodyRadius > 1.4f && PlayerDist < 25f) { var cam = Camera.main ? Camera.main.GetComponent<ThirdPersonCamera>() : null; if (cam) cam.AddShake(0.015f * def.bodyRadius, 0.2f); }
            }
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

        void Die()
        {
            Health = 0f; Enter(DinoState.Dead); _speed = 0f; _pendingHit = false; _fireMode = FireMode.None; _sleeping = false;
            DiedAt = GameClock.Now;
            All.Remove(this);            // a body is no longer a live creature (tutorial, minimap, herd); OnDisable removes it again harmlessly
            DeadPose();
            PlayClip(def.deaths, 1f);
            BloodFX.Death(transform.position + transform.forward * def.bodyRadius * 0.4f, Mathf.Clamp(def.bodyRadius * 1.1f, 0.8f, 3.5f), transform);
            GameEvents.Raise(GameEventType.CreatureKilled, def.id, 1, transform.position);
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
        }

        public SavedState Capture()
        {
            var s = new SavedState { dead = State == DinoState.Dead, health = _started ? Health : (def ? def.maxHealth : 0f), pos = transform.position, yaw = transform.eulerAngles.y, diedAt = DiedAt };
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
            if (!s.dead)
            {
                Health = Mathf.Clamp(s.health, 1f, def.maxHealth);
                Senses.ForgetAll(); _provoked = false; _goal = Goal.None; Enter(DinoState.Idle);
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
            // slow down to turn: big animals cannot pivot at full speed
            float ang = d.sqrMagnitude > 0.01f ? Vector3.Angle(transform.forward, d) : 0f;
            if (ang > 60f) want *= 0.4f;
            // obstacle / water ahead: turn away
            if (Blocked(out Vector3 avoid)) { d = avoid; want *= 0.5f; }
            _speed = Mathf.MoveTowards(_speed, want, def.acceleration * dt * (want < _speed ? 2f : 1f));
            Face(transform.position + d, dt);
        }

        void Face(Vector3 p, float dt)
        {
            Vector3 d = p - transform.position; d.y = 0; if (d.sqrMagnitude < 0.01f) return;
            float ts = def.turnSpeed * (_speed > def.walkSpeed * 1.5f ? 0.7f : 1f);
            var want = Quaternion.LookRotation(d.normalized);
            var cur = Quaternion.Euler(0, transform.eulerAngles.y, 0);
            var yaw = Quaternion.RotateTowards(cur, want, ts * dt);
            transform.rotation = yaw * Quaternion.Euler(_pitch, 0, 0);
        }

        float _pitch;
        void Move(float dt)
        {
            if (_speed > 0.01f) transform.position += Quaternion.Euler(0, transform.eulerAngles.y, 0) * Vector3.forward * _speed * dt;
            if (Time.time < _pushUntil) { float k = (_pushUntil - Time.time) / _pushDur; transform.position += _push * (k * 1.6f) * dt; }
            Snap();
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
            bool water = t && t.SampleHeight(ahead) + t.transform.position.y < 0.6f;
            bool wall = Physics.SphereCast(transform.position + Vector3.up * def.bodyRadius, def.bodyRadius * 0.6f, fwd, out var hit, def.bodyRadius * 1.5f + _speed * 0.5f, Masks.NotPlayer, QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(transform) && !(hit.collider is TerrainCollider);
            if (!water && !wall) return false;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            avoid = (Vector3.Dot(right, home - transform.position) > 0 ? right : -right) + fwd * 0.2f;
            if (water && _goal != Goal.Drink) _dest = RandomPointNear(home, homeRadius);
            else if (water) { _goal = Goal.None; Enter(DinoState.Drink); }              // reached the shore on the way to drink
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
                if ((!t || t.SampleHeight(p) + t.transform.position.y > 0.8f) && !FearBlocks(p)) return p;
            }
            return c;
        }

        public void ForceState(DinoState s) => Enter(s);

        /// <summary>tests / story: thirsty now (goes to fresh water at the next calm moment)</summary>
        public void ThirstNow() { _nextDrinkAt = 0; _nextDrinkLook = 0f; }

        /// <summary>after sleeping: every creature forgets the player (awareness, memory, smells)</summary>
        public static void ForgetPlayerAll()
        {
            for (int i = 0; i < All.Count; i++) { var d = All[i]; if (!d) continue; d.Senses.ForgetAll(); d._provoked = false; if (d.State == DinoState.Chase || d.State == DinoState.Investigate || d.State == DinoState.Observe || d.State == DinoState.Alert) d.Enter(DinoState.Idle); }
        }
    }
}
