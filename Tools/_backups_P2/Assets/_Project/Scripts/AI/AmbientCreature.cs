using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Combat;
using PrimalFrontier.Core;
using PrimalFrontier.World;

namespace PrimalFrontier.AI
{
    /// <summary>
    /// Background life that must not interfere with the land loop: pteranodons circling over the coast (fly / glide)
    /// and a mosasaur patrolling offshore. Simple parametric paths, cheap, no pathfinding. Can be hit and killed;
    /// a species with meat leaves a Carcass to butcher (a flyer falls to the ground first).
    /// </summary>
    public class AmbientCreature : MonoBehaviour, IDamageable
    {
        public DinosaurDefinition def;
        [Tooltip("middle of the circle it patrols. (0,0,0) = where it is placed in the scene (a flyer placed in the air circles at that height)")]
        public Vector3 center; public float radius = 40f; public float altitude = 25f;
        public bool swimmer;
        float _a, _dir = 1f, _h; Animator _anim; bool _dead; float _glideT; bool _gliding;
        public bool IsAlive => !_dead;
        float _health; float _boostUntil;
        /// <summary>enabled ambient creatures (bushes look for one to startle)</summary>
        public static readonly System.Collections.Generic.List<AmbientCreature> All = new System.Collections.Generic.List<AmbientCreature>();

        void OnEnable() { if (!All.Contains(this)) All.Add(this); Stimuli.LoudNoise -= OnLoudNoise; Stimuli.LoudNoise += OnLoudNoise; }
        void OnDisable() { All.Remove(this); if (All.Count == 0) Stimuli.LoudNoise -= OnLoudNoise; }

        /// <summary>a loud noise (tree fall, fight, roar): every ambient creature within range turns away and hurries</summary>
        static void OnLoudNoise(Vector3 p, float loudness)
        {
            float r = PerceptionConfig.Instance.ambientStartleRange * Mathf.Clamp(loudness * 0.5f, 0.5f, 1.5f), r2 = r * r;
            for (int i = 0; i < All.Count; i++) { var a = All[i]; if (a && a.IsAlive && (a.transform.position - p).sqrMagnitude < r2) a.Startle(p); }
        }

        /// <summary>something rustled nearby: turn away from it and hurry for a few seconds</summary>
        public void Startle(Vector3 from)
        {
            if (_dead) return;
            // the tangent of the circle (derivative of cos/sin) points along +_dir; flip when it heads towards 'from'
            Vector3 tangent = new Vector3(-Mathf.Sin(_a), 0f, Mathf.Cos(_a)) * _dir;
            Vector3 to = from - transform.position; to.y = 0f;
            if (Vector3.Dot(tangent, to) > 0f) _dir = -_dir;
            _boostUntil = Time.time + 3f;
            if (_landed || _air < 1f) { _airHoldUntil = Time.time + WildlifeConfig.Instance.flyerStartleAirSeconds; TakeOff(); }
        }

        /// <summary>still hurrying away from the last startle</summary>
        public bool Startled => Time.time < _boostUntil;

        void Start()
        {
            if (!GetComponent<DinoLife>()) gameObject.AddComponent<DinoLife>();     // eyes blink
            if (center == Vector3.zero) center = swimmer ? transform.position : transform.position - Vector3.up * altitude;
            _anim = GetComponent<Animator>(); if (!_anim) _anim = GetComponentInChildren<Animator>();
            _a = Random.Range(0f, Mathf.PI * 2f); _dir = Random.value < 0.5f ? 1f : -1f;
            _health = def ? def.maxHealth : 60f;
            if (_anim) _caps.Resolve(_anim);
            if (_anim && !swimmer) { _anim.SetInteger(AnimParams.ActionType, 20); _anim.SetTrigger(AnimParams.Action); }
        }

        void Update()
        {
            if (_dead) { if (!swimmer) Fall(); return; }
            if (!swimmer) FlyerWeather();
            // _air 1 = on its circle in the sky, 0 = on its perch (storm): the path blends between the two
            float air = swimmer ? 1f : _air * _air * (3f - 2f * _air);
            float speed = (def ? (swimmer ? def.walkSpeed : def.runSpeed) : 8f) * (Time.time < _boostUntil ? 1.8f : 1f) * (swimmer ? 1f : Mathf.Lerp(0.3f, 1f, air));
            if (!_landed) _a += _dir * speed / Mathf.Max(5f, radius) * Time.deltaTime;
            float wob = Mathf.Sin(Time.time * 0.13f + center.x) * radius * 0.25f;
            Vector3 p = center + new Vector3(Mathf.Cos(_a), 0, Mathf.Sin(_a)) * (radius + wob);
            if (swimmer) p.y = -1.2f + Mathf.Sin(Time.time * 0.3f) * 0.4f;
            else p.y = center.y + altitude + Mathf.Sin(Time.time * 0.4f + center.z) * 4f;
            if (!swimmer && _hasPerch && (_landed || _air < 1f)) p = _landed ? _perch : Vector3.Lerp(_perch, p, air);
            Vector3 v = p - transform.position;
            if (!swimmer && (_landed || air < 0.2f))
            {
                // coming down / sitting on the perch: level, facing where it was heading
                Vector3 f = v; f.y = 0f;
                if (!_landed && f.sqrMagnitude > 0.0001f) _groundYaw = Quaternion.LookRotation(f.normalized).eulerAngles.y;
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, _groundYaw, 0f), Time.deltaTime * 3f);
            }
            else if (v.sqrMagnitude > 0.0001f)
            {
                var look = Quaternion.LookRotation(v.normalized);
                float bank = swimmer ? 0f : -_dir * 18f * air;
                transform.rotation = Quaternion.Slerp(transform.rotation, look * Quaternion.Euler(0, 0, bank), Time.deltaTime * 2f);
                _groundYaw = transform.eulerAngles.y;
            }
            transform.position = p;
            if (!swimmer && _anim)
            {
                if (_landed) _anim.SetFloat(AnimParams.Speed, 0f);
                else if (_air > 0.5f && _flyAt < 0f)
                {
                    _glideT -= Time.deltaTime;
                    if (_glideT <= 0f)
                    {
                        _gliding = !_gliding; _glideT = _gliding ? Random.Range(4f, 9f) : Random.Range(2f, 4f);
                        _anim.SetInteger(AnimParams.ActionType, _gliding ? 21 : 20); _anim.SetTrigger(AnimParams.Action);
                    }
                }
            }
            else if (swimmer && _anim) _anim.SetFloat(AnimParams.Speed, def ? def.walkSpeed : 2f);
        }

        // ------------------------------------------------------------------ weather (Phase 1): flyers land in storms
        float _air = 1f, _groundYaw, _liftAt = -1f, _flyAt = -1f, _airHoldUntil = -99f, _perchRetry;
        bool _landed, _hasPerch; Vector3 _perch;
        readonly DinoAnimCaps _caps = new DinoAnimCaps();
        /// <summary>sitting on the ground waiting out bad weather</summary>
        public bool Landed => _landed;
        /// <summary>0 = on the perch .. 1 = up on its circle</summary>
        public float Air => _air;

        void FlyerWeather()
        {
            WildlifeWeather.Refresh();
            var w = WildlifeConfig.Instance;
            bool ground = WildlifeWeather.FlyersGrounded && Time.time >= _airHoldUntil;
            if (ground && !_hasPerch && Time.time >= _perchRetry) { _perchRetry = Time.time + 5f; _hasPerch = FindPerch(out _perch); }
            float want = ground && _hasPerch ? 0f : 1f;
            if (_landed)
            {
                if (want <= 0f) _liftAt = -1f;
                else
                {
                    // the weather cleared: wait a little on the perch, then take off (a startle goes at once, see Startle)
                    if (_liftAt < 0f) _liftAt = Time.time + Random.Range(w.flyerTakeoffDelay.x, w.flyerTakeoffDelay.y);
                    if (Time.time >= _liftAt) TakeOff();
                }
            }
            else
            {
                _air = Mathf.MoveTowards(_air, want, Time.deltaTime / Mathf.Max(1f, w.flyerLandSeconds));
                if (_air <= 0f && _hasPerch) Touchdown();
            }
            if (_flyAt > 0f && Time.time >= _flyAt)
            {
                _flyAt = -1f; _gliding = false; _glideT = Random.Range(2f, 4f);
                if (_anim) { _anim.SetInteger(AnimParams.ActionType, 20); _anim.SetTrigger(AnimParams.Action); }
            }
        }

        void Touchdown()
        {
            _landed = true; _air = 0f; _liftAt = -1f;
            if (!_anim) return;
            _caps.Resolve(_anim);
            _anim.SetInteger(AnimParams.ActionType, _caps.Land ? 23 : 0);
            if (_caps.Land) _anim.SetTrigger(AnimParams.Action);
            _anim.SetFloat(AnimParams.Speed, 0f);
        }

        void TakeOff()
        {
            if (!_landed) return;
            _landed = false; _liftAt = -1f; _air = Mathf.Max(_air, 0.02f);
            if (!_anim) return;
            _caps.Resolve(_anim);
            if (_caps.Takeoff) { _anim.SetInteger(AnimParams.ActionType, 22); _anim.SetTrigger(AnimParams.Action); _flyAt = Time.time + 0.9f; }
            else { _anim.SetInteger(AnimParams.ActionType, 20); _anim.SetTrigger(AnimParams.Action); }
        }

        /// <summary>a dry, walkable spot under its circle to wait out a storm</summary>
        bool FindPerch(out Vector3 p)
        {
            p = default; var t = Terrain.activeTerrain; if (!t) return false;
            for (int i = 0; i < 14; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f), r = radius * Random.Range(0.15f, 0.8f);
                var q = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                if (!HerdGroup.Walkable(q)) continue;
                q.y = t.SampleHeight(q) + t.transform.position.y;
                if (q.y < 1.2f) continue;
                p = q; return true;
            }
            return false;
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Vector3 c = Application.isPlaying || center != Vector3.zero ? center : (swimmer ? transform.position : transform.position - Vector3.up * altitude);
            if (!swimmer) c.y += altitude;
            Gizmos.color = swimmer ? new Color(0.2f, 0.6f, 1f) : new Color(0.9f, 0.9f, 0.3f);
            DinosaurSpawner.DrawCircle(c, radius);
        }
#endif

        void Fall()
        {
            var t = Terrain.activeTerrain; float g = t ? t.SampleHeight(transform.position) + t.transform.position.y : 0f;
            if (transform.position.y > g + 0.2f) transform.position += Vector3.down * 9f * Time.deltaTime;
        }

        public void TakeHit(HitInfo hit)
        {
            if (_dead) return;
            _health -= hit.damage * (hit.unarmed ? DinosaurController.UnarmedScale(def) : 1f);
            if (_anim) _anim.SetTrigger(AnimParams.Hurt);
            if (_health <= 0f)
            {
                _dead = true; DiedAt = GameClock.Now; if (_anim) _anim.SetBool(AnimParams.Dead, true);
                GameEvents.Raise(GameEventType.CreatureKilled, def ? def.id : name, 1, transform.position);
                if (def && def.meat > 0)
                {
                    var c = GetComponent<Carcass>(); if (!c) c = gameObject.AddComponent<Carcass>();
                    c.Setup(def.displayName, def.id, def.meat, def.hide, def.bone);
                }
            }
            else { _dir = -_dir; if (_landed || _air < 1f) { _airHoldUntil = Time.time + WildlifeConfig.Instance.flyerStartleAirSeconds; TakeOff(); } }
        }

        /// <summary>game clock time of death (-1 = alive)</summary>
        public double DiedAt { get; private set; } = -1;

        /// <summary>save / load: a dead flyer / swimmer comes back dead, its carcass with what was left (CreatureSave)</summary>
        public void RestoreDead(DinosaurController.SavedState s)
        {
            if (!s.dead) return;
            _dead = true; DiedAt = s.diedAt; if (_anim) _anim.SetBool(AnimParams.Dead, true);
            transform.position = s.pos;
            if (s.gone) { gameObject.SetActive(false); return; }
            if (def && def.meat > 0 && s.carcass)
            {
                var c = GetComponent<Carcass>(); if (!c) c = gameObject.AddComponent<Carcass>();
                c.Setup(def.displayName, def.id, def.meat, def.hide, def.bone);
                c.RestoreLeft(s.meat, s.hide, s.bone, s.expireAt);
            }
        }
    }
}
