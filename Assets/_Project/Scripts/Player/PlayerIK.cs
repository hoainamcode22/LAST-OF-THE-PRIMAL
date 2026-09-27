using UnityEngine;
using PrimalFrontier.World;

namespace PrimalFrontier.Player
{
    /// <summary>
    /// Humanoid IK pass for the player (sits next to the Animator, on the "Model" child):
    /// - feet: each planted foot is placed on the real ground under it (slopes, rocks, steps) and tilted to the surface,
    ///   the pelvis drops so the lower foot can reach; feet in the air keep the animation.
    /// - look: head and upper body turn towards what matters (the thing you can interact with, a creature set as
    ///   LookTarget, otherwise where the camera looks), within a natural range.
    /// - lean: the body leans into turns in proportion to speed x turn rate (feet stay planted through the foot IK).
    /// Needs "IK Pass" on the base layer of PlayerAnimator (PrimalCharacterBuilder sets it). Every value is tunable in
    /// the Inspector.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class PlayerIK : MonoBehaviour
    {
        [Header("Feet")]
        public bool footIK = true;
        [Tooltip("layers the feet stand on (the Player layer is always ignored)")] public LayerMask groundMask = ~0;
        public float rayAbove = 0.5f, rayBelow = 0.75f;
        [Tooltip("ankle height of a planted foot in the animation (m); higher = the foot is lifting")] public float plantedAnkle = 0.1f;
        public float maxPelvisDrop = 0.35f;
        public float pelvisSmooth = 10f;
        [Range(0, 1)] public float runningFootWeight = 0.35f;
        [Header("Look")]
        public bool lookIK = true;
        [Range(0, 1)] public float lookWeight = 0.75f;
        [Range(0, 1)] public float bodyWeight = 0.15f, headWeight = 0.75f, eyesWeight = 1f;
        [Range(0, 1)] public float clampWeight = 0.55f;
        public float interactLookRange = 4f;
        [Tooltip("set by gameplay: a creature, a sound, a cinematic target. Overrides the automatic choice")] public Transform LookTarget;
        /// <summary>climbing: hands (and feet) go onto this trunk's surface (set by PlayerClimb)</summary>
        [System.NonSerialized] public Climbable Climb;
        /// <summary>the right hand reaches for this (picking fruit)</summary>
        [System.NonSerialized] public Transform ReachTarget;
        float _climbW, _reachW;
        [Header("Lean")]
        public float maxLean = 9f;
        public float leanScale = 0.35f;

        Animator _a; PlayerMotor _motor; PlayerAnimationDriver _drv; PlayerInteraction _pi; Transform _root;
        float _pelvis, _wL, _wR, _lookW, _lean; Vector3 _lookPos; bool _hasLook;
        public float PelvisOffset => _pelvis;
        /// <summary>switched off by states that own the whole body (climbing, lying, cinematics)</summary>
        public bool Suspended { get; set; }

        void Awake()
        {
            _a = GetComponent<Animator>();
            _motor = GetComponentInParent<PlayerMotor>(); _drv = GetComponentInParent<PlayerAnimationDriver>(); _pi = GetComponentInParent<PlayerInteraction>();
            _root = _motor ? _motor.transform : transform;
            int playerLayer = _root.gameObject.layer;
            if (playerLayer != 0) groundMask &= ~(1 << playerLayer);   // never the player's own colliders
            groundMask &= ~(1 << 2);                                    // nor Ignore Raycast
        }

        void OnAnimatorIK(int layer)
        {
            if (layer != 0 || _a == null || !_a.isHuman) return;
            float dt = Time.deltaTime;
            _climbW = Mathf.MoveTowards(_climbW, Climb ? 1f : 0f, dt * 3f);
            _reachW = Mathf.MoveTowards(_reachW, ReachTarget ? 1f : 0f, dt * 2.5f);
            if (_climbW > 0.001f && Climb) { ClimbIK(); return; }
            bool body = !Suspended && _drv != null && !_drv.IsDead;
            bool actionOwnsBody = _drv != null && _drv.IsBusy;
            bool grounded = _motor == null || _motor.IsGrounded;
            float speed = _motor ? _motor.PlanarSpeed : 0f;

            // ---------------- feet + pelvis
            float wantPelvis = 0f;
            bool feetOn = footIK && body && grounded;
            float speedK = Mathf.Lerp(1f, runningFootWeight, Mathf.InverseLerp(1.5f, 4f, speed));
            var up = _root.up;
            float baseY = _root.position.y;
            Foot(AvatarIKGoal.LeftFoot, ref _wL, feetOn, speedK, baseY, up, ref wantPelvis, dt);
            Foot(AvatarIKGoal.RightFoot, ref _wR, feetOn, speedK, baseY, up, ref wantPelvis, dt);
            if (!feetOn) wantPelvis = 0f;
            _pelvis = Mathf.Lerp(_pelvis, Mathf.Clamp(wantPelvis, -maxPelvisDrop, 0.02f), 1f - Mathf.Exp(-pelvisSmooth * dt));

            // ---------------- lean into turns
            float turn = _motor ? _motor.TurnRate : 0f;       // deg/s, + = left
            float wantLean = grounded && body && !actionOwnsBody ? Mathf.Clamp(turn * Mathf.Deg2Rad * speed / 9.81f * Mathf.Rad2Deg * leanScale, -maxLean, maxLean) : 0f;
            _lean = Mathf.Lerp(_lean, wantLean, 1f - Mathf.Exp(-6f * dt));

            if (Mathf.Abs(_pelvis) > 1e-4f || Mathf.Abs(_lean) > 0.01f)
            {
                _a.bodyPosition += up * _pelvis;
                if (Mathf.Abs(_lean) > 0.01f) _a.bodyRotation = Quaternion.AngleAxis(_lean, _root.forward) * _a.bodyRotation;
            }

            // ---------------- look
            Vector3 target = default;
            bool lookOn = lookIK && body && !actionOwnsBody && PickLook(out target);
            if (lookOn) { _lookPos = _hasLook ? Vector3.Lerp(_lookPos, target, 1f - Mathf.Exp(-8f * dt)) : target; _hasLook = true; }
            _lookW = Mathf.MoveTowards(_lookW, lookOn ? lookWeight : 0f, dt * 2.5f);
            if (_lookW > 0.001f && _hasLook)
            {
                _a.SetLookAtWeight(_lookW, bodyWeight, headWeight, eyesWeight, clampWeight);
                _a.SetLookAtPosition(_lookPos);
            }
            else _a.SetLookAtWeight(0f);
        }

        void Foot(AvatarIKGoal goal, ref float w, bool on, float speedK, float baseY, Vector3 up, ref float wantPelvis, float dt)
        {
            Vector3 p = _a.GetIKPosition(goal);                // animated ankle (world), before IK
            float ankleUp = p.y - baseY;                       // how far the animation lifts this ankle
            float sole = goal == AvatarIKGoal.LeftFoot ? _a.leftFeetBottomHeight : _a.rightFeetBottomHeight;
            if (sole < 0.02f) sole = plantedAnkle;              // ankle height of a flat, planted foot
            float planted = 1f - Mathf.Clamp01((ankleUp - sole - 0.02f) / 0.12f);
            float target = 0f; Vector3 pos = p; Quaternion rot = _a.GetIKRotation(goal);
            if (on && Physics.Raycast(new Vector3(p.x, baseY + rayAbove, p.z), Vector3.down, out var hit, rayAbove + rayBelow, groundMask, QueryTriggerInteraction.Ignore)
                && Vector3.Angle(hit.normal, up) < 50f)
            {
                float ground = hit.point.y - baseY;            // ground under this foot relative to the capsule bottom
                if (planted > 0.5f) wantPelvis = Mathf.Min(wantPelvis, ground);
                pos = new Vector3(p.x, hit.point.y + ankleUp, p.z);
                // tilt the foot to the surface (limited so it never folds)
                var tilt = Quaternion.FromToRotation(up, Vector3.Slerp(up, hit.normal, 0.8f));
                rot = tilt * rot;
                target = planted * speedK;
            }
            w = Mathf.MoveTowards(w, target, dt * 6f);
            _a.SetIKPositionWeight(goal, w); _a.SetIKRotationWeight(goal, w * 0.8f);
            if (w > 0.001f) { _a.SetIKPosition(goal, pos); _a.SetIKRotation(goal, rot); }
        }

        /// <summary>hands and feet on the trunk surface; while picking, the right hand goes to the fruit</summary>
        void ClimbIK()
        {
            Vector3 a = Climb.Bottom, b = Climb.Top; Vector3 ab = b - a; float len2 = Mathf.Max(0.01f, ab.sqrMagnitude);
            Vector3 OnTrunk(Vector3 p, float extra)
            {
                float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2); Vector3 axis = a + ab * t;
                Vector3 d = p - axis; d.y = 0f; if (d.sqrMagnitude < 1e-4f) d = -_root.forward;
                return axis + d.normalized * (Climb.trunkRadius + extra);
            }
            void Goal(AvatarIKGoal g, float w, float extra)
            {
                Vector3 p = _a.GetIKPosition(g);
                Vector3 target = OnTrunk(p, extra);
                if (g == AvatarIKGoal.RightHand && ReachTarget) target = Vector3.Lerp(target, ReachTarget.position, _reachW);
                _a.SetIKPositionWeight(g, w); _a.SetIKPosition(g, target);
                _a.SetIKRotationWeight(g, 0f);
            }
            Goal(AvatarIKGoal.LeftHand, _climbW, 0.03f);
            Goal(AvatarIKGoal.RightHand, _climbW, 0.03f);
            Goal(AvatarIKGoal.LeftFoot, _climbW * 0.7f, 0.06f);
            Goal(AvatarIKGoal.RightFoot, _climbW * 0.7f, 0.06f);
            // look up the trunk, or at the fruit being picked
            Vector3 look = ReachTarget ? ReachTarget.position : _root.position + Vector3.up * 2.6f + _root.forward * 0.4f;
            _a.SetLookAtWeight(0.7f * _climbW, 0.1f, 0.8f, 1f, 0.5f); _a.SetLookAtPosition(look);
            _pelvis = 0f; _wL = _wR = 0f;
        }

        bool PickLook(out Vector3 target)
        {
            target = default;
            Vector3 head = _root.position + Vector3.up * 1.6f;
            Vector3 fwd = _root.forward;
            bool Ok(Vector3 t) { var d = t - head; d.y = 0f; return d.sqrMagnitude > 0.04f && Vector3.Angle(fwd, d) < 95f; }
            if (LookTarget && Ok(LookTarget.position)) { target = LookTarget.position + Vector3.up * 0.5f; return true; }
            var it = _pi ? _pi.Target : null;
            if (it && (it.transform.position - _root.position).sqrMagnitude < interactLookRange * interactLookRange && Ok(it.transform.position))
            {
                var c = it.GetComponentInChildren<Collider>();
                target = c ? c.bounds.center : it.transform.position; return true;
            }
            var cam = Camera.main;
            if (cam && (_motor == null || _motor.PlanarSpeed < 4.5f))
            {
                var t = cam.transform.position + cam.transform.forward * 12f;
                if (Ok(t)) { target = t; return true; }
            }
            return false;
        }
    }
}
