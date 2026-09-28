using System;
using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.AI;
using PrimalFrontier.Animation;
using PrimalFrontier.Audio;
using PrimalFrontier.VFX;

namespace PrimalFrontier.Combat.Weapons
{
    /// <summary>
    /// Damage volume of a melee weapon, only live during the attack's Active window. Blade mode (on the held model):
    /// a capsule from the base to the tip of the model's mesh along its local +Y (grip at the origin), swept between
    /// the last and the current frame's segment. Sphere mode (no model mesh; on Combat/AttackOrigin): the old
    /// ResolveMelee sphere in front of the chest. Each IDamageable is hit once per swing, several targets can be hit,
    /// the HitZone multiplier applies; creatures bleed by themselves, other targets get a dust puff.
    /// </summary>
    public class WeaponHitbox : MonoBehaviour
    {
        const int MaxColliders = 32;
        static readonly Collider[] Buffer = new Collider[MaxColliders];
        static readonly IDamageable[] Owners = new IDamageable[MaxColliders];

        [Tooltip("local base of the blade (m)")] public Vector3 localBase;
        [Tooltip("local tip of the blade (m)")] public Vector3 localTip = new Vector3(0, 0.6f, 0);
        public float radius = 0.12f;
        [Tooltip("sphere mode: reach in front of this transform, along the owner's forward (m)")] public float reach = 1.35f;
        public bool sphereMode;

        /// <summary>(target, hit, collider) after TakeHit was called</summary>
        public event Action<IDamageable, HitInfo, Collider> Hit;
        public bool Active { get; private set; }
        public int HitCount { get; private set; }
        public bool HasBlade => !sphereMode;
        public WeaponData Data => _data;

        readonly HashSet<IDamageable> _hitThisSwing = new HashSet<IDamageable>();
        WeaponData _data; Transform _owner; int _mask;
        float _damage; bool _heavy; bool _downStroke;
        Vector3 _lastBase, _lastTip; bool _hasLast;

        /// <summary>blade mode on a held model; false when the model has no mesh (use sphere mode instead)</summary>
        public bool SetupBlade(WeaponData data, Transform owner, int mask)
        {
            _data = data; _owner = owner; _mask = mask; sphereMode = false;
            radius = data ? Mathf.Max(0.02f, data.hitRadius) : radius;
            if (!MeasureBlade(transform, out localBase, out localTip)) { sphereMode = true; return false; }
            return true;
        }

        /// <summary>sphere mode (AttackOrigin): the old single overlap in front of the chest</summary>
        public void SetupSphere(WeaponData data, Transform owner, int mask)
        {
            _data = data; _owner = owner; _mask = mask; sphereMode = true;
            reach = data ? data.reach : reach;
        }

        /// <summary>
        /// base / tip from the mesh bounds of the model (in the model root's space, grip at the origin). Tool / weapon
        /// convention of the Blender pipeline: the handle runs along local Z with the working end towards -Z (it leaves
        /// the fist on the thumb side, see PlayerHierarchy.TryComputeGrip); models whose long axis is local Y are still
        /// measured along +Y.
        /// </summary>
        public static bool MeasureBlade(Transform model, out Vector3 basePoint, out Vector3 tipPoint)
        {
            basePoint = Vector3.zero; tipPoint = Vector3.up * 0.6f;
            if (!MeasureLocalBounds(model, out var b)) return false;
            if (b.size.z >= b.size.y)
            {
                float far = Mathf.Abs(b.min.z) >= Mathf.Abs(b.max.z) ? b.min.z : b.max.z;
                if (Mathf.Abs(far) <= 0.05f) return false;
                // long weapons (spear) only hurt near the point; short blades from a little past the hand
                float from = Mathf.Abs(far) > 0.9f ? 0.7f : 0.2f;
                basePoint = new Vector3(b.center.x, b.center.y, far * from);
                tipPoint = new Vector3(b.center.x, b.center.y, far);
                return true;
            }
            if (b.max.y <= 0.05f) return false;
            float top = b.max.y, bottom = Mathf.Max(0f, b.min.y);
            basePoint = new Vector3(b.center.x, Mathf.Lerp(bottom, top, 0.2f), b.center.z);
            tipPoint = new Vector3(b.center.x, top, b.center.z);
            return true;
        }

        /// <summary>bounds of every mesh under model, in the model root's local space (false = no mesh)</summary>
        public static bool MeasureLocalBounds(Transform model, out Bounds b)
        {
            bool any = false; b = new Bounds();
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh) Encapsulate(model, mf.transform, mf.sharedMesh.bounds, ref b, ref any);
            foreach (var sm in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (sm.sharedMesh) Encapsulate(model, sm.transform, sm.sharedMesh.bounds, ref b, ref any);
            return any;
        }

        static void Encapsulate(Transform root, Transform t, Bounds local, ref Bounds b, ref bool any)
        {
            Vector3 c = local.center, e = local.extents;
            for (int i = 0; i < 8; i++)
            {
                var corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                var p = root.InverseTransformPoint(t.TransformPoint(corner));
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
            }
        }

        // ------------------------------------------------------------------ swing
        /// <summary>a new swing: forget who was hit, set its damage (before the zone multiplier)</summary>
        public void BeginSwing(float damage, bool heavy, bool downStroke, float sphereReach = -1f)
        {
            _hitThisSwing.Clear(); HitCount = 0;
            _damage = damage; _heavy = heavy; _downStroke = downStroke;
            if (sphereReach > 0f) reach = sphereReach;
            SetActive(false);
        }

        public void SetActive(bool on)
        {
            if (Active == on) return;
            Active = on; _hasLast = false;
        }

        public bool AlreadyHit(IDamageable d) => _hitThisSwing.Contains(d);

        void LateUpdate()
        {
            if (!Active || _owner == null) return;
            if (sphereMode) { SweepSphere(); return; }
            Vector3 b = transform.TransformPoint(localBase), t = transform.TransformPoint(localTip);
            if (!_hasLast) { _lastBase = b; _lastTip = t; _hasLast = true; }
            // sub-steps so a fast swing does not skip a target between frames
            float move = Mathf.Max((t - _lastTip).magnitude, (b - _lastBase).magnitude);
            int steps = Mathf.Clamp(Mathf.CeilToInt(move / Mathf.Max(0.05f, radius * 1.5f)), 1, 4);
            Vector3 dir = t - _lastTip;
            for (int s = 1; s <= steps; s++)
            {
                float k = (float)s / steps;
                Vector3 pb = Vector3.Lerp(_lastBase, b, k), pt = Vector3.Lerp(_lastTip, t, k);
                int n = Physics.OverlapCapsuleNonAlloc(pb, pt, radius, Buffer, _mask, QueryTriggerInteraction.Collide);
                Resolve(n, (pb + pt) * 0.5f, dir);
            }
            _lastBase = b; _lastTip = t;
        }

        void SweepSphere()
        {
            Vector3 fwd = _owner.forward;
            Vector3 origin = transform.position;
            int n = Physics.OverlapSphereNonAlloc(origin + fwd * reach * 0.6f, reach * 0.55f, Buffer, _mask, QueryTriggerInteraction.Collide);
            Resolve(n, origin + fwd * 0.5f, fwd);
        }

        /// <summary>one immediate pass (legacy OnAttackHit on a clip without window events)</summary>
        public void SweepNow()
        {
            if (_owner == null) return;
            if (sphereMode) { SweepSphere(); return; }
            Vector3 b = transform.TransformPoint(localBase), t = transform.TransformPoint(localTip);
            int n = Physics.OverlapCapsuleNonAlloc(b, t, radius, Buffer, _mask, QueryTriggerInteraction.Collide);
            Resolve(n, (b + t) * 0.5f, _owner.forward);
        }

        /// <summary>the old ResolveMelee sphere from any origin (used when a blade missed at the legacy hit event)</summary>
        public void SweepSphereFrom(Vector3 origin, float sphereReach)
        {
            if (_owner == null) return;
            Vector3 fwd = _owner.forward;
            int n = Physics.OverlapSphereNonAlloc(origin + fwd * sphereReach * 0.6f, sphereReach * 0.55f, Buffer, _mask, QueryTriggerInteraction.Collide);
            Resolve(n, origin + fwd * 0.5f, fwd);
        }

        void Resolve(int n, Vector3 probe, Vector3 motion)
        {
            if (n <= 0) return;
            for (int i = 0; i < n; i++)
            {
                var c = Buffer[i];
                Owners[i] = null;
                if (!c || c.transform.IsChildOf(_owner)) continue;
                var d = c.GetComponentInParent<IDamageable>();
                if (d != null && d.IsAlive && !_hitThisSwing.Contains(d)) Owners[i] = d;
            }
            for (int i = 0; i < n; i++)
            {
                var d = Owners[i]; if (d == null) continue;
                // the best zone of this target among the colliders touched this step (head beats body)
                var best = Buffer[i]; float bestMult = ZoneMult(best);
                for (int j = i + 1; j < n; j++)
                {
                    if (!ReferenceEquals(Owners[j], d)) continue;
                    float m = ZoneMult(Buffer[j]); if (m > bestMult) { bestMult = m; best = Buffer[j]; }
                    Owners[j] = null;
                }
                Owners[i] = null;
                Apply(d, best, bestMult, probe, motion);
            }
            for (int i = 0; i < n; i++) { Buffer[i] = null; Owners[i] = null; }
        }

        static float ZoneMult(Collider c) { var z = c.GetComponent<HitZone>(); return z ? z.DamageMultiplier : 1f; }

        void Apply(IDamageable d, Collider c, float mult, Vector3 probe, Vector3 motion)
        {
            _hitThisSwing.Add(d); HitCount++;
            Vector3 fwd = _owner.forward;
            Vector3 pt = c.ClosestPoint(probe);
            Vector3 dir = motion.sqrMagnitude > 1e-4f ? Vector3.Lerp(fwd, motion.normalized, 0.5f).normalized : fwd;
            if (_downStroke) dir = (Vector3.down * 0.5f + fwd).normalized;
            var hit = new HitInfo
            {
                damage = _damage * mult, point = pt, direction = dir, attacker = _owner.gameObject,
                weapon = _data ? _data.kind : Items.WeaponKind.None, heavy = _heavy, zoneMultiplier = mult
            };
            bool flesh = IsFlesh(d);
            d.TakeHit(hit);
            if (flesh) { if (_data && _data.hitVfx != VfxId.None) VfxPool.Instance.Play(_data.hitVfx, pt, -dir); }
            else VfxPool.Instance.Play(_data && _data.hitVfxHard != VfxId.None ? _data.hitVfxHard : VfxId.HitDust, pt, -dir);
            SfxId sfx = _data ? _data.hitSfx : SfxId.HitFlesh;
            if (sfx != SfxId.None) SfxPlayer.Instance.Play(sfx, pt);
            Hit?.Invoke(d, hit, c);
        }

        /// <summary>creatures bleed on their own (BloodFX in their TakeHit); anything else gets a neutral impact</summary>
        public static bool IsFlesh(IDamageable d) => d is DinosaurController || d is AmbientCreature;

        void OnDisable() { Active = false; _hasLast = false; }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Active ? new Color(1f, 0.2f, 0.1f, 0.9f) : new Color(1f, 0.85f, 0.2f, 0.6f);
            if (sphereMode)
            {
                Vector3 fwd = _owner ? _owner.forward : transform.forward;
                Gizmos.DrawWireSphere(transform.position + fwd * reach * 0.6f, reach * 0.55f);
                return;
            }
            Vector3 b = transform.TransformPoint(localBase), t = transform.TransformPoint(localTip);
            Gizmos.DrawWireSphere(b, radius); Gizmos.DrawWireSphere(t, radius);
            Vector3 axis = (t - b).sqrMagnitude > 1e-6f ? (t - b).normalized : Vector3.up;
            Vector3 side = Vector3.Cross(axis, Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up).normalized * radius;
            Vector3 side2 = Vector3.Cross(axis, side).normalized * radius;
            Gizmos.DrawLine(b + side, t + side); Gizmos.DrawLine(b - side, t - side);
            Gizmos.DrawLine(b + side2, t + side2); Gizmos.DrawLine(b - side2, t - side2);
        }
    }
}
