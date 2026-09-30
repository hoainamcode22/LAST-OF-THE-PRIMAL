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
            if (_anim && !swimmer) { _anim.SetInteger(AnimParams.ActionType, 20); _anim.SetTrigger(AnimParams.Action); }
        }

        void Update()
        {
            if (_dead) { if (!swimmer) Fall(); return; }
            float speed = (def ? (swimmer ? def.walkSpeed : def.runSpeed) : 8f) * (Time.time < _boostUntil ? 1.8f : 1f);
            _a += _dir * speed / Mathf.Max(5f, radius) * Time.deltaTime;
            float wob = Mathf.Sin(Time.time * 0.13f + center.x) * radius * 0.25f;
            Vector3 p = center + new Vector3(Mathf.Cos(_a), 0, Mathf.Sin(_a)) * (radius + wob);
            if (swimmer) p.y = -1.2f + Mathf.Sin(Time.time * 0.3f) * 0.4f;
            else p.y = center.y + altitude + Mathf.Sin(Time.time * 0.4f + center.z) * 4f;
            Vector3 v = p - transform.position;
            if (v.sqrMagnitude > 0.0001f)
            {
                var look = Quaternion.LookRotation(v.normalized);
                float bank = swimmer ? 0f : -_dir * 18f;
                transform.rotation = Quaternion.Slerp(transform.rotation, look * Quaternion.Euler(0, 0, bank), Time.deltaTime * 2f);
            }
            transform.position = p;
            if (!swimmer && _anim)
            {
                _glideT -= Time.deltaTime;
                if (_glideT <= 0f)
                {
                    _gliding = !_gliding; _glideT = _gliding ? Random.Range(4f, 9f) : Random.Range(2f, 4f);
                    _anim.SetInteger(AnimParams.ActionType, _gliding ? 21 : 20); _anim.SetTrigger(AnimParams.Action);
                }
            }
            else if (swimmer && _anim) _anim.SetFloat(AnimParams.Speed, def ? def.walkSpeed : 2f);
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
            else _dir = -_dir;
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
