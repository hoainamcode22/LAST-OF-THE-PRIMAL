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
    /// Lightweight dinosaur brain (FSM) + kinematic movement on the terrain. Senses: sight (range, field of view,
    /// line of sight), hearing (sprint, fights), distance. Herbivores graze, watch, flee or defend; predators patrol,
    /// investigate, chase and attack, and retreat when badly hurt. AI LOD: near = full, mid = slower thinking,
    /// far = frozen. The Animator runs in place (Speed parameter matches the real speed).
    /// </summary>
    public class DinosaurController : MonoBehaviour, IDamageable
    {
        public DinosaurDefinition def;
        public Vector3 home; public float homeRadius = 40f;
        public DinoState State { get; private set; } = DinoState.Idle;
        public float Health { get; private set; }
        public bool IsAlive => State != DinoState.Dead;
        public static readonly List<DinosaurController> All = new List<DinosaurController>();

        Animator _anim; AudioSource _audio; CharacterAnimationEvents _ev;
        float _stateT, _think, _speed, _attackReady, _nextCall, _lodT;
        Vector3 _dest; Transform _player; PlayerHealth _playerHp; PlayerMotor _playerMotor;
        Vector3 _threat; bool _provoked; int _lod; bool _pendingHit; float _hitAt; bool _heavy;
        static readonly HashSet<string> Sighted = new HashSet<string>();
        static readonly int SpeedH = AnimParams.Speed, ActionTypeH = AnimParams.ActionType, ActionH = AnimParams.Action, AttackH = AnimParams.Attack,
            AttackTypeH = AnimParams.AttackType, HurtH = AnimParams.Hurt, DeadH = AnimParams.Dead, AlertH = AnimParams.Alert;

        bool Herbivore => def.temperament == Temperament.Passive || def.temperament == Temperament.Defensive;
        public static void ResetSightings() => Sighted.Clear();

        void Awake()
        {
            _anim = GetComponent<Animator>(); if (!_anim) _anim = GetComponentInChildren<Animator>();
            _ev = GetComponentInChildren<CharacterAnimationEvents>();
            _audio = GetComponent<AudioSource>();
            if (!_audio) { _audio = gameObject.AddComponent<AudioSource>(); _audio.spatialBlend = 1f; _audio.rolloffMode = AudioRolloffMode.Linear; _audio.maxDistance = 120f; _audio.minDistance = 4f; _audio.playOnAwake = false; }
        }
        void OnEnable() { All.Add(this); if (_ev) _ev.AnimationEventRaised += OnAnimEvent; GameEvents.Raised += OnGameEvent; }
        void OnDisable() { All.Remove(this); if (_ev) _ev.AnimationEventRaised -= OnAnimEvent; GameEvents.Raised -= OnGameEvent; }

        void Start()
        {
            if (def == null) { enabled = false; return; }
            Health = def.maxHealth;
            if (home == Vector3.zero) home = transform.position;
            _dest = transform.position; _think = Random.value * 0.3f; _nextCall = Time.time + Random.Range(8f, 30f);
            Snap();
            Enter(Random.value < 0.5f ? DinoState.Eat : DinoState.Idle);
        }

        // ------------------------------------------------------------------ perception
        bool FindPlayer()
        {
            if (_player) return true;
            if (!PlayerLocator.Player) return false;
            _player = PlayerLocator.Player; _playerHp = _player.GetComponent<PlayerHealth>(); _playerMotor = _player.GetComponent<PlayerMotor>();
            return true;
        }
        float PlayerDist => _player ? Vector3.Distance(transform.position, _player.position) : 9999f;
        bool PlayerAlive => _playerHp && !_playerHp.IsDead;

        bool CanSee()
        {
            if (!_player || !PlayerAlive) return false;
            Vector3 d = _player.position - transform.position; float dist = d.magnitude;
            float range = def.sightRange * (_playerMotor && _playerMotor.IsCrouching ? 0.6f : 1f) * (TimeManager.Instance && TimeManager.Instance.IsNight ? 0.6f : 1f);
            if (dist > range) return false;
            d.y = 0; if (Vector3.Angle(transform.forward, d) > def.fov * 0.5f && dist > def.bodyRadius * 3f) return false;
            Vector3 eye = transform.position + Vector3.up * Mathf.Max(1f, def.bodyRadius * 1.4f);
            return !Physics.Linecast(eye, _player.position + Vector3.up * 1.4f, ~LayerMask.GetMask("Player"), QueryTriggerInteraction.Ignore) || dist < def.bodyRadius * 4f;
        }
        bool CanHear()
        {
            if (!_player || !PlayerAlive || !_playerMotor) return false;
            float r = def.hearingRange * (_playerMotor.IsSprinting ? 1f : _playerMotor.IsCrouching ? 0.15f : _playerMotor.PlanarSpeed > 0.5f ? 0.45f : 0.1f);
            return PlayerDist < r;
        }

        // ------------------------------------------------------------------ brain
        Transform[] _bones; float _nextDrip;

        void Update()
        {
            if (def == null) return;
            FindPlayer();
            float dist = PlayerDist;
            int lod = dist < 90f ? 0 : dist < 200f ? 1 : 2;
            if (lod != _lod) { _lod = lod; if (_anim) _anim.enabled = lod < 2 || State == DinoState.Dead; }
            if (State == DinoState.Dead) return;
            if (lod == 2) { _lodT += Time.deltaTime; if (_lodT < 1f) return; }
            float dt = lod == 2 ? _lodT : Time.deltaTime; _lodT = 0f;
            _stateT += dt; _think -= dt;
            if (_think <= 0f) { _think = lod == 0 ? 0.2f : 0.6f; Think(dist); }
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
            if (Time.time > _nextCall && lod == 0 && State != DinoState.Chase) { _nextCall = Time.time + Random.Range(15f, 40f); PlayClip(def.calls, 0.8f); }
        }

        void Think(float dist)
        {
            bool see = CanSee(), hear = CanHear();
            if (see && dist < 35f && Sighted.Add(def.id)) GameEvents.Raise(GameEventType.CreatureSighted, def.id, 1, transform.position);
            bool hurt = Health < def.maxHealth * def.retreatHealth;
            switch (def.temperament)
            {
                case Temperament.Passive:
                    if ((see && dist < def.personalSpace * 1.6f) || _provoked || (hear && dist < def.personalSpace)) { Flee(_player.position); return; }
                    if (see && dist < def.observeDistance && State != DinoState.Flee) { if (State != DinoState.Observe) Enter(DinoState.Observe); return; }
                    break;
                case Temperament.Defensive:
                    if (_provoked || (see && dist < def.personalSpace))
                    {
                        if (hurt) { Flee(_player.position); return; }
                        if (State != DinoState.Chase && State != DinoState.Attack) { Roar(); Enter(DinoState.Chase); }
                        return;
                    }
                    if (see && dist < def.observeDistance && State != DinoState.Flee && State != DinoState.Chase) { if (State != DinoState.Observe) Enter(DinoState.Observe); return; }
                    break;
                case Temperament.Territorial:
                case Temperament.Predator:
                    if (hurt && def.temperament == Temperament.Predator) { Flee(_player ? _player.position : transform.position - transform.forward); return; }
                    bool inTerritory = Vector3.Distance(_player ? _player.position : home, home) < def.territoryRadius || def.temperament == Temperament.Predator;
                    if (PlayerAlive && ((see && dist < def.aggroRange && inTerritory) || _provoked))
                    {
                        if (State != DinoState.Chase && State != DinoState.Attack) { if (State != DinoState.Alert && dist > def.attackRange * 3f) { Enter(DinoState.Alert); Roar(); } else Enter(DinoState.Chase); }
                        return;
                    }
                    if (hear && State != DinoState.Chase) { _dest = _player.position; Enter(DinoState.Investigate); return; }
                    break;
            }
            if (State == DinoState.Observe && (!see || dist > def.observeDistance * 1.3f)) Enter(DinoState.Idle);
            if (State == DinoState.Chase && (!PlayerAlive || dist > def.aggroRange * 1.8f || Vector3.Distance(transform.position, home) > def.territoryRadius * 2.5f)) { _provoked = false; Enter(DinoState.Return); }
        }

        void Act(float dt, float dist)
        {
            switch (State)
            {
                case DinoState.Idle:
                    _speed = Mathf.MoveTowards(_speed, 0f, def.acceleration * dt * 2f);
                    if (_stateT > Random.Range(4f, 9f)) Enter(Random.value < 0.55f ? DinoState.Wander : Random.value < 0.6f ? DinoState.Eat : DinoState.Rest);
                    break;
                case DinoState.Wander:
                    Steer(_dest, def.walkSpeed, dt);
                    if (Arrived(2f) || _stateT > 25f) Enter(Random.value < 0.5f ? DinoState.Eat : DinoState.Idle);
                    break;
                case DinoState.Eat: case DinoState.Drink: case DinoState.Rest:
                    _speed = Mathf.MoveTowards(_speed, 0f, def.acceleration * dt * 2f);
                    if (_stateT > Random.Range(8f, 16f)) Enter(DinoState.Wander);
                    break;
                case DinoState.Observe:
                case DinoState.Alert:
                    _speed = Mathf.MoveTowards(_speed, 0f, def.acceleration * dt * 3f);
                    if (_player) Face(_player.position, dt);
                    if (State == DinoState.Alert && _stateT > 1.8f) Enter(DinoState.Chase);
                    break;
                case DinoState.Investigate:
                    Steer(_dest, def.walkSpeed * 1.3f, dt);
                    if (Arrived(3f) || _stateT > 15f) Enter(DinoState.Idle);
                    break;
                case DinoState.Flee:
                {
                    Vector3 away = transform.position - _threat; away.y = 0; if (away.sqrMagnitude < 0.01f) away = transform.forward;
                    _dest = transform.position + away.normalized * 20f;
                    Steer(_dest, def.runSpeed, dt);
                    if (_stateT > 9f || Vector3.Distance(transform.position, _threat) > def.fleeDistance) { _provoked = false; Enter(DinoState.Idle); }
                    break;
                }
                case DinoState.Chase:
                    if (!_player || !PlayerAlive) { Enter(DinoState.Return); break; }
                    Steer(_player.position, def.runSpeed, dt, true);
                    if (dist < def.attackRange + def.bodyRadius * 0.5f && Time.time >= _attackReady) StartAttack(dist);
                    break;
                case DinoState.Attack:
                    _speed = Mathf.MoveTowards(_speed, 0f, def.acceleration * dt * 3f);
                    if (_player) Face(_player.position, dt * 0.6f);
                    if (_stateT > 1.6f) Enter(PlayerAlive ? DinoState.Chase : DinoState.Return);
                    break;
                case DinoState.Return:
                    Steer(home, def.walkSpeed * 1.2f, dt);
                    if (Vector3.Distance(transform.position, home) < homeRadius * 0.5f) Enter(DinoState.Idle);
                    break;
            }
        }

        void Enter(DinoState s)
        {
            var old = State; State = s; _stateT = 0f;
            if (!_anim) return;
            _anim.SetBool(AlertH, s == DinoState.Observe || s == DinoState.Alert);
            int action = s == DinoState.Eat ? DinoActions.Eat : s == DinoState.Drink ? DinoActions.Drink : s == DinoState.Rest ? DinoActions.Rest : s == DinoState.Chase && Herbivore ? 10 : DinoActions.None;
            _anim.SetInteger(ActionTypeH, action);
            if (action != DinoActions.None) _anim.SetTrigger(ActionH);
            if (s == DinoState.Wander) _dest = RandomPointNear(home, homeRadius);
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
        }

        void StartAttack(float dist)
        {
            Enter(DinoState.Attack);
            _attackReady = Time.time + def.attackCooldown * Random.Range(0.8f, 1.2f);
            _heavy = Random.value < 0.3f;
            if (_anim) { _anim.SetInteger(AttackTypeH, _heavy ? 1 : 0); _anim.SetTrigger(AttackH); }
            _pendingHit = true; _hitAt = Time.time + (_heavy ? 0.9f : 0.6f);        // fallback when the event does not arrive
            PlayClip(def.roars, 0.6f);
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
            if (set == null || set.Length == 0 || _lod > 0) return;
            _audio.pitch = Random.Range(0.92f, 1.08f); _audio.PlayOneShot(set[Random.Range(0, set.Length)], vol * AudioBus.Sfx);
        }

        // ------------------------------------------------------------------ damage
        public void TakeHit(HitInfo hit)
        {
            if (State == DinoState.Dead) return;
            Health -= hit.damage;
            _provoked = true; if (hit.attacker) _threat = hit.attacker.transform.position;
            if (_bones == null) { var smr = GetComponentInChildren<SkinnedMeshRenderer>(); _bones = smr ? smr.bones : new Transform[0]; }
            BloodFX.Hit(hit.point, hit.direction, Mathf.Clamp(def.bodyRadius * 0.6f, 0.6f, 2.2f), hit.heavy || hit.damage > def.maxHealth * 0.15f, transform, _bones);
            if (Health <= 0f) { Die(); return; }
            if (_anim) _anim.SetTrigger(HurtH);
            PlayClip(def.hurts, 1f);
            if (def.temperament == Temperament.Passive || Health < def.maxHealth * def.retreatHealth) Flee(_threat);
            else if (State != DinoState.Chase && State != DinoState.Attack) Enter(DinoState.Chase);
        }

        void Die()
        {
            Health = 0f; Enter(DinoState.Dead); _speed = 0f; _pendingHit = false;
            if (_anim) { _anim.enabled = true; _anim.SetBool(DeadH, true); _anim.SetFloat(SpeedH, 0f); }
            PlayClip(def.deaths, 1f);
            BloodFX.Death(transform.position + transform.forward * def.bodyRadius * 0.4f, Mathf.Clamp(def.bodyRadius * 1.1f, 0.8f, 3.5f), transform);
            GameEvents.Raise(GameEventType.CreatureKilled, def.id, 1, transform.position);
            // loot next to the body
            var db = ItemDatabase.Instance;
            if (db)
            {
                int i = 0;
                void Drop(string id, int n) { var it = db.Item(id); if (it && n > 0) WorldPickup.Drop(it, n, transform.position + Quaternion.Euler(0, 70 * i++, 0) * transform.right * (def.bodyRadius + 0.8f) + Vector3.up * 0.5f); }
                Drop("raw_meat", def.meat); Drop("hide", def.hide); Drop("bone", def.bone);
            }
        }

        void OnGameEvent(GameEvent e)
        {
            if (State == DinoState.Dead || !Herbivore) return;
            if (e.type == GameEventType.PredatorWarning && _player && PlayerDist < 160f) { Flee(_player.position + (transform.position - _player.position).normalized * -5f); }
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
            bool wall = Physics.SphereCast(transform.position + Vector3.up * def.bodyRadius, def.bodyRadius * 0.6f, fwd, out var hit, def.bodyRadius * 1.5f + _speed * 0.5f, ~LayerMask.GetMask("Player"), QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(transform) && !(hit.collider is TerrainCollider);
            if (!water && !wall) return false;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            avoid = (Vector3.Dot(right, home - transform.position) > 0 ? right : -right) + fwd * 0.2f;
            if (water) _dest = RandomPointNear(home, homeRadius);
            return true;
        }

        bool Arrived(float r) { var d = _dest - transform.position; d.y = 0; return d.magnitude < r; }

        Vector3 RandomPointNear(Vector3 c, float r)
        {
            var t = Terrain.activeTerrain;
            for (int i = 0; i < 8; i++)
            {
                var p = c + Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward * Random.Range(r * 0.2f, r);
                if (!t || t.SampleHeight(p) + t.transform.position.y > 0.8f) return p;
            }
            return c;
        }

        public void ForceState(DinoState s) => Enter(s);
    }
}
