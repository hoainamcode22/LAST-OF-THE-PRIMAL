using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Combat;
using PrimalFrontier.Core;

namespace PrimalFrontier.AI
{
    /// <summary>
    /// Background life that must not interfere with the land loop: pteranodons circling over the coast (fly / glide)
    /// and a mosasaur patrolling offshore. Simple parametric paths, cheap, no pathfinding. Can be hit and killed.
    /// </summary>
    public class AmbientCreature : MonoBehaviour, IDamageable
    {
        public DinosaurDefinition def;
        [Tooltip("middle of the circle it patrols. (0,0,0) = where it is placed in the scene (a flyer placed in the air circles at that height)")]
        public Vector3 center; public float radius = 40f; public float altitude = 25f;
        public bool swimmer;
        float _a, _dir = 1f, _h; Animator _anim; bool _dead; float _glideT; bool _gliding;
        public bool IsAlive => !_dead;
        float _health;

        void Start()
        {
            if (center == Vector3.zero) center = swimmer ? transform.position : transform.position - Vector3.up * altitude;
            _anim = GetComponent<Animator>(); _a = Random.Range(0f, Mathf.PI * 2f); _dir = Random.value < 0.5f ? 1f : -1f;
            _health = def ? def.maxHealth : 60f;
            if (_anim && !swimmer) { _anim.SetInteger(AnimParams.ActionType, 20); _anim.SetTrigger(AnimParams.Action); }
        }

        void Update()
        {
            if (_dead) { if (!swimmer) Fall(); return; }
            float speed = def ? (swimmer ? def.walkSpeed : def.runSpeed) : 8f;
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
            _health -= hit.damage;
            if (_anim) _anim.SetTrigger(AnimParams.Hurt);
            if (_health <= 0f)
            {
                _dead = true; if (_anim) _anim.SetBool(AnimParams.Dead, true);
                GameEvents.Raise(GameEventType.CreatureKilled, def ? def.id : name, 1, transform.position);
            }
            else _dir = -_dir;
        }
    }
}
