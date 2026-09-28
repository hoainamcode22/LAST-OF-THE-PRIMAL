using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Audio;
using PrimalFrontier.Combat.Weapons;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.VFX;
using PrimalFrontier.World;

namespace PrimalFrontier.Combat
{
    /// <summary>
    /// Thrown spear / arrow: simple ballistic flight with raycasts between frames (no rigidbody), damage on hit,
    /// then it lies where it landed as a pickup (spear always, arrow 60 %). Instances come from ProjectilePool and go
    /// back to it when they land (state is reset on every launch). Creatures bleed on their own, so a hit on flesh
    /// plays the flesh sound instead of the dust / splinter impact.
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        public float gravity = -9.81f;
        public float maxLife = 8f;
        public float recoverChance = 1f;
        Vector3 _vel; HitInfo _hit; ItemStack _stack; GameObject _owner; float _t; int _mask; bool _done;
        VfxId _impactFx; SfxId _impactSfx;
        static int _defaultMask = -1;
        /// <summary>set by ProjectilePool: the pool stack this instance returns to (null = not pooled, destroyed on landing)</summary>
        public Object PoolKey { get; set; }

        public static Projectile Launch(ItemStack stack, Vector3 pos, Vector3 velocity, HitInfo hit, GameObject owner, float recoverChance, bool isArrow)
        {
            var item = stack != null ? stack.item : null;
            var p = ProjectilePool.Get(item ? item.handPrefab : null);
            p._vel = velocity; p._hit = hit; p._stack = item ? stack : null; p._owner = owner; p.recoverChance = recoverChance;
            p._t = 0f; p._done = false;
            if (_defaultMask == -1) _defaultMask = ~LayerMask.GetMask("Player", "Ignore Raycast");
            p._mask = _defaultMask;
            p._impactFx = isArrow ? VfxId.ArrowImpact : VfxId.SpearImpact; p._impactSfx = isArrow ? SfxId.ArrowImpact : SfxId.SpearImpact;
            p.transform.position = pos; p.Orient();
            return p;
        }

        // the weapon models point their blade along local +Y
        void Orient() { if (_vel.sqrMagnitude > 0.01f) transform.rotation = Quaternion.FromToRotation(Vector3.up, _vel.normalized); }

        void Update()
        {
            if (_done) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            _t += dt;
            Vector3 a = transform.position; Vector3 step = _vel * dt;
            _vel.y += gravity * dt;
            if (Physics.Raycast(a, step.normalized, out var rh, step.magnitude + 0.25f, _mask, QueryTriggerInteraction.Collide) && !rh.collider.transform.IsChildOf(_owner ? _owner.transform : transform))
            {
                Impact(rh); return;
            }
            transform.position = a + step; Orient();
            if (_t > maxLife || transform.position.y < -20f) Land(transform.position, false);
        }

        void Impact(RaycastHit rh)
        {
            _done = true;
            var zone = rh.collider.GetComponent<HitZone>();
            var dmg = rh.collider.GetComponentInParent<IDamageable>();
            bool flesh = false;
            if (dmg != null && dmg.IsAlive)
            {
                flesh = WeaponHitbox.IsFlesh(dmg);
                var h = _hit; h.point = rh.point; h.direction = _vel.normalized; h.zoneMultiplier = zone ? zone.DamageMultiplier : 1f; h.ranged = true;
                h.damage *= h.zoneMultiplier * Mathf.Clamp(_vel.magnitude / 20f, 0.6f, 1.2f);
                dmg.TakeHit(h);
                GameEvents.Raise(GameEventType.CreatureHit, (dmg as Component) ? ((Component)dmg).name : "creature", Mathf.RoundToInt(h.damage), rh.point);
            }
            if (flesh) SfxPlayer.Instance.Play(SfxId.HitFlesh, rh.point, 0.8f);           // blood comes from the creature
            else
            {
                VfxPool.Instance.Play(_impactFx, rh.point, rh.normal);
                SfxPlayer.Instance.Play(_impactSfx, rh.point);
            }
            if (rh.collider is TerrainCollider || dmg == null) Land(rh.point, true);
            else Land(rh.point + rh.normal * 0.3f, false);                           // falls off the animal
        }

        void Land(Vector3 at, bool stuck)
        {
            _done = true;
            if (_stack != null && Random.value < recoverChance)
            {
                _stack.durability -= _stack.item.HasDurability ? 2f : 0f;
                if (!_stack.item.HasDurability || _stack.durability > 0f)
                {
                    var pk = WorldPickup.DropStack(_stack, at);
                    if (pk && stuck) pk.transform.rotation = transform.rotation;         // stays stuck in the ground at its angle
                }
            }
            _stack = null; _owner = null;
            if (PoolKey != null) ProjectilePool.Return(this);
            else Destroy(gameObject);
        }
    }
}
