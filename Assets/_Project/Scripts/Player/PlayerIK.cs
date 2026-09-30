using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Combat.Weapons;
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
    /// - hands: the left hand holds a two-handed weapon's OffHandGrip (PlayerEquipment); while the bow draws, the right
    ///   hand goes from the string toward the cheek with the draw (WeaponController.DrawProgress); elbow hints keep the
    ///   IK arms bending outward / down. Points on a hand (grip, bow, nock) are tracked in that hand's pre-IK goal space,
    ///   calibrated in LateUpdate on frames without IK on that hand, so they follow the current pose without a frame lag.
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
        [Tooltip("pelvis offset speed limit (m/s)")] public float pelvisMaxRate = 1.5f;
        [Tooltip("both feet this far below the capsule's support (m): the capsule is perched on an edge, the pelvis does not drop")] public float perchGap = 0.12f;
        [Range(0, 1)] public float runningFootWeight = 0.35f;
        [Tooltip("a planted foot keeps its world spot while the body turns / pivots over it (foot lock); released when the clip lifts it or it would stretch further than this (m)")]
        public float footLockMaxDrift = 0.18f;
        [Tooltip("foot lock only below this planar speed (m/s): at a run the clips' own contacts are used")] public float footLockMaxSpeed = 2.2f;
        [Header("Look")]
        public bool lookIK = true;
        [Range(0, 1)] public float lookWeight = 0.75f;
        [Range(0, 1)] public float bodyWeight = 0.15f, headWeight = 0.75f, eyesWeight = 1f;
        [Range(0, 1)] public float clampWeight = 0.55f;
        public float interactLookRange = 4f;
        [Tooltip("body weight of the look while moving faster than a walk or turning faster than 90 deg/s (the chest stays with the hips)")]
        [Range(0, 1)] public float movingBodyWeight = 0.03f;
        [Tooltip("smoothing of the look point (s)")] public float lookSmoothTime = 0.15f;
        [Tooltip("targets further than this from the body's forward are not looked at; the look eases to straight ahead instead")] public float lookCone = 95f;
        [Tooltip("set by gameplay: a creature, a sound, a cinematic target. Overrides the automatic choice")] public Transform LookTarget;
        /// <summary>climbing: hands (and feet) go onto this trunk's surface (set by PlayerClimb)</summary>
        [System.NonSerialized] public Climbable Climb;
        /// <summary>the right hand reaches for this (picking fruit)</summary>
        [System.NonSerialized] public Transform ReachTarget;
        /// <summary>climbing a face / pulling over a lip: the feet leave the face (legs swinging over the edge)</summary>
        [System.NonSerialized] public bool ClimbFeetFree;
        float _climbW, _reachW, _holdW, _feetW = 1f;
        Vector3 _holdL, _holdR; bool _holds;
        /// <summary>
        /// hands placed by an action program (kneel-drink scoop, container to the mouth): world targets and weights set every
        /// frame by PlayerInteraction; the weights ease toward what is asked (0 = the clip's hands)
        /// </summary>
        public void SetActionHands(Vector3 left, float wl, Vector3 right, float wr) { _actL = left; _actR = right; _actWantL = wl; _actWantR = wr; _actFrame = Time.frameCount; }
        Vector3 _actL, _actR; float _actWantL, _actWantR, _actWL, _actWR; int _actFrame = -10;
        /// <summary>current weight of the action-program hands (0 = the clip's hands); diagnostics / tests</summary>
        public float ActionHandWeight => Mathf.Max(_actWL, _actWR);
        /// <summary>where the action program last asked the hands to be (midpoint of both goals); diagnostics / tests</summary>
        public Vector3 ActionHandGoal => (_actL + _actR) * 0.5f;
        /// <summary>
        /// an action program's look: head and upper body turn toward a point (the spine bends forward to scoop water, then
        /// straightens as the hands come up). Set every frame; eases out when no longer asked. The legs are untouched.
        /// </summary>
        public void SetActionLook(Vector3 at, float weight, float bodyWeight, float drop = 0f) { _actLook = at; _actLookWant = weight; _actLookBody = bodyWeight; _actDropWant = drop; _actLookFrame = Time.frameCount; }
        Vector3 _actLook; float _actLookWant, _actLookBody, _actLookW, _actLookBodyW, _actDropWant, _actDrop; int _actLookFrame = -10;
        /// <summary>how far an action program has lowered the body right now (m, the feet stay planted); diagnostics / tests</summary>
        public float ActionDrop => _actDrop;
        /// <summary>frame of the last IK pass that placed the hands (diagnostics)</summary>
        public int LastHandIKFrame => _ikFrame;
        /// <summary>both hands grip these points (a ledge lip) while climbing; eased in and out</summary>
        public void SetHolds(Vector3 left, Vector3 right) { _holdL = left; _holdR = right; _holds = true; ClimbFeetFree = false; }
        public void ClearHolds() { _holds = false; }
        [Header("Lean")]
        public float maxLean = 9f;
        public float leanScale = 0.35f;
        [Header("Hands")]
        public bool handIK = true;
        [Tooltip("seconds to blend the off-hand grip / bow draw IK in and out")] public float handBlendTime = 0.15f;
        [Range(0, 1)] public float offHandRotationWeight = 1f;
        [Tooltip("elbow hint from the elbow, character space (x outward, y up, z forward)")] public Vector3 elbowHintOffset = new Vector3(0.35f, -0.3f, -0.1f);
        [Tooltip("draw hand anchor at full draw: head bone + this, character space (x right, y up, z forward)")] public Vector3 cheekOffset = new Vector3(0.06f, -0.07f, 0.06f);
        [Tooltip("string distance from the bow grip at rest (m)")] public float braceHeight = 0.16f;

        /// <summary>a point rigidly attached to a hand, stored in that hand's IK-goal space</summary>
        struct HandPoint { public Transform t; public Vector3 pos; public bool valid; }

        Animator _a; PlayerMotor _motor; PlayerAnimationDriver _drv; PlayerInteraction _pi; Transform _root;
        PlayerEquipment _eq; PlayerHierarchy _hier; WeaponController _wc; Transform _head, _elbowL, _elbowR;
        float _pelvis, _wL, _wR, _lookW, _lean; Vector3 _lookPos; bool _hasLook;
        float _bodyW = -1f, _moveK; Vector3 _lookVel;
        float _offW, _drawW, _drawK;
        Vector3 _goalPosL, _goalPosR; Quaternion _goalRotL = Quaternion.identity, _goalRotR = Quaternion.identity;
        float _appliedL, _appliedR; int _ikFrame = -1;
        HandPoint _grip, _bow, _nock;
        public float PelvisOffset => _pelvis;
        /// <summary>the capsule rests on an edge above the ground under both feet (no pelvis drop this frame)</summary>
        public bool Perched { get; private set; }
        /// <summary>switched off by states that own the whole body (climbing, lying, cinematics)</summary>
        public bool Suspended { get; set; }

        void Awake()
        {
            _a = GetComponent<Animator>();
            _motor = GetComponentInParent<PlayerMotor>(); _drv = GetComponentInParent<PlayerAnimationDriver>(); _pi = GetComponentInParent<PlayerInteraction>();
            _eq = GetComponentInParent<PlayerEquipment>();
            _root = _motor ? _motor.transform : transform;
            int playerLayer = _root.gameObject.layer;
            if (playerLayer != 0) groundMask &= ~(1 << playerLayer);   // never the player's own colliders
            groundMask &= ~(1 << 2);                                    // nor Ignore Raycast
        }

        /// <summary>IK passes run so far (diagnostics)</summary>
        public int IKPasses { get; private set; }

        void OnAnimatorIK(int layer)
        {
            IKPasses++;
            if (layer != 0 || _a == null || !_a.isHuman) return;
            float dt = Time.deltaTime;
            _climbW = Mathf.MoveTowards(_climbW, Climb ? 1f : 0f, dt * 3f);
            _reachW = Mathf.MoveTowards(_reachW, ReachTarget ? 1f : 0f, dt * 2.5f);
            if (_climbW > 0.001f && Climb) { _offW = _drawW = 0f; ClimbIK(); return; }
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
            // perched: the capsule rests on a narrow prop / step edge while the ground under BOTH feet is clearly lower (a stair has
            // one foot on the capsule's level). Then the body floats over the gap instead of sinking the pelvis to reach the ground.
            Perched = feetOn && GroundUnder(AvatarIKGoal.LeftFoot, baseY) < -perchGap && GroundUnder(AvatarIKGoal.RightFoot, baseY) < -perchGap;
            Foot(AvatarIKGoal.LeftFoot, ref _wL, feetOn && !Perched, speedK, baseY, up, ref wantPelvis, dt);
            Foot(AvatarIKGoal.RightFoot, ref _wR, feetOn && !Perched, speedK, baseY, up, ref wantPelvis, dt);
            if (!feetOn || Perched) wantPelvis = 0f;
            // the pelvis follows the feet at the feet's own weight (0.35 at a run), eased and rate limited: a capsule perched on a
            // step / prop edge no longer drops the body 0.2-0.3 m in a few frames (probe 2026-09-28)
            float pelvisNext = Mathf.Lerp(_pelvis, Mathf.Clamp(wantPelvis, -maxPelvisDrop, 0.02f), 1f - Mathf.Exp(-pelvisSmooth * dt));
            _pelvis = Mathf.MoveTowards(_pelvis, pelvisNext, pelvisMaxRate * dt);

            // ---------------- lean into turns
            float turn = _motor ? _motor.TurnRate : 0f;       // deg/s, + = left
            float wantLean = grounded && body && !actionOwnsBody ? Mathf.Clamp(turn * Mathf.Deg2Rad * speed / 9.81f * Mathf.Rad2Deg * leanScale, -maxLean, maxLean) : 0f;
            _lean = Mathf.Lerp(_lean, wantLean, 1f - Mathf.Exp(-6f * dt));

            // an action program may sink the body deeper (kneeling to scoop water on a crouch placeholder): the feet IK keeps
            // the feet planted, the knees bend more
            _actDrop = Mathf.MoveTowards(_actDrop, feetOn && Time.frameCount - _actLookFrame <= 1 ? Mathf.Clamp(_actDropWant, 0f, 0.3f) : 0f, dt * 0.6f);
            if (_actDrop > 1e-4f) _a.bodyPosition -= up * _actDrop;
            if (Mathf.Abs(_pelvis) > 1e-4f || Mathf.Abs(_lean) > 0.01f)
            {
                _a.bodyPosition += up * _pelvis;
                if (Mathf.Abs(_lean) > 0.01f)
                {
                    // lean about the ground under the capsule, not about the hips: the feet stay where they are and the body tilts
                    // over them (rotating at the hips swung the planted feet sideways, 0.15-0.2 m per stance in a reversal)
                    var q = Quaternion.AngleAxis(_lean, _root.forward);
                    Vector3 pivot = _root.position;
                    _a.bodyPosition = pivot + q * (_a.bodyPosition - pivot);
                    _a.bodyRotation = q * _a.bodyRotation;
                }
            }

            // ---------------- look
            Vector3 target = default;
            bool lookOn = lookIK && body && !actionOwnsBody && PickLook(out target);
            if (lookOn)
            {
                if (!_hasLook) { _lookPos = target; _lookVel = Vector3.zero; }
                else _lookPos = Vector3.SmoothDamp(_lookPos, target, ref _lookVel, lookSmoothTime, Mathf.Infinity, dt);
                _hasLook = true;
            }
            _lookW = Mathf.MoveTowards(_lookW, lookOn ? lookWeight : 0f, Mathf.Min(dt * 2.5f, 0.08f));
            // chest stays with the hips while running / turning (the look twisted it +-33 deg in turns): low body weight, eased
            float moveK = Mathf.Max(Mathf.InverseLerp(1.35f, 2.2f, speed), Mathf.InverseLerp(45f, 90f, Mathf.Abs(turn)));
            _moveK = Mathf.MoveTowards(_moveK, moveK, dt * 4f);
            float wantBody = Mathf.Lerp(bodyWeight, movingBodyWeight, moveK);
            _bodyW = _bodyW < 0f ? wantBody : Mathf.MoveTowards(_bodyW, wantBody, Mathf.Min(dt * 1.5f, 0.05f));
            // an action program's look (scooping water) takes over while asked, eased in and out
            bool actLook = body && Time.frameCount - _actLookFrame <= 1;
            _actLookW = Mathf.MoveTowards(_actLookW, actLook ? _actLookWant : 0f, dt * 3f);
            _actLookBodyW = Mathf.MoveTowards(_actLookBodyW, actLook ? _actLookBody : 0f, dt * 2f);
            if (_actLookW > 0.001f)
            {
                _a.SetLookAtWeight(Mathf.Max(_actLookW, _lookW), _actLookBodyW, 0.9f, eyesWeight, 0.35f);
                _a.SetLookAtPosition(_lookW > 0.001f && _hasLook ? Vector3.Lerp(_lookPos, _actLook, _actLookW / Mathf.Max(_actLookW, _lookW)) : _actLook);
            }
            else if (_lookW > 0.001f && _hasLook)
            {
                _a.SetLookAtWeight(_lookW, _bodyW, headWeight, eyesWeight, clampWeight);
                _a.SetLookAtPosition(_lookPos);
            }
            else _a.SetLookAtWeight(0f);

            // ---------------- hands
            HandIK(body, dt);
        }

        // ------------------------------------------------------------------ hands
        void HandIK(bool body, float dt)
        {
            ResolveRefs();
            // pre-IK hand goals of this frame (the animated pose); points on the hands are expressed in these frames
            _goalPosL = _a.GetIKPosition(AvatarIKGoal.LeftHand); _goalRotL = _a.GetIKRotation(AvatarIKGoal.LeftHand);
            _goalPosR = _a.GetIKPosition(AvatarIKGoal.RightHand); _goalRotR = _a.GetIKRotation(AvatarIKGoal.RightHand);
            _ikFrame = Time.frameCount;
            float rate = dt / Mathf.Max(0.01f, handBlendTime);
            bool on = handIK && body;

            // off hand on a two-handed weapon held in the right hand (a bow sits in the left hand: never)
            Track(ref _grip, _eq ? _eq.OffHandGrip : null);
            bool rightHeld = _eq && _hier && _eq.HeldSocket && _eq.HeldSocket == _hier.RightHandWeaponSocket;
            int act = _drv ? _drv.CurrentAction : PlayerActions.None;
            bool handsElsewhere = act == PlayerActions.CarryItem || act == PlayerActions.ThrowSpear;      // both arms / the free arm aims the throw
            bool gripOn = on && rightHeld && !handsElsewhere && _grip.valid && _grip.t && _grip.t.gameObject.activeInHierarchy;
            _offW = Mathf.MoveTowards(_offW, gripOn ? 1f : 0f, rate);

            // bow draw: right hand from the string toward the cheek with the draw progress
            Track(ref _bow, _hier ? _hier.LeftHandWeaponSocket : null);
            Track(ref _nock, _hier ? _hier.ArrowSocket : null);
            var data = _wc ? _wc.CurrentData : null;
            bool drawing = on && data && data.IsRanged && _wc.IsDrawing && _bow.valid && _head;
            if (drawing) _drawK = _wc.DrawProgress;                     // blending out keeps the last draw (follow-through)
            _drawW = Mathf.MoveTowards(_drawW, drawing ? 1f : 0f, rate);

            float wl = 0f, wr = 0f;
            if (_offW > 0.001f && _grip.valid && _grip.t)
            {
                wl = _offW;
                Vector3 p = _goalPosR + _goalRotR * _grip.pos;
                // the animated left hand keeps its orientation relative to the line between the hands
                Vector3 dAnim = _goalPosL - _goalPosR, dNew = p - _goalPosR;
                Quaternion adj = dAnim.sqrMagnitude > 1e-4f && dNew.sqrMagnitude > 1e-4f ? Quaternion.FromToRotation(dAnim, dNew) : Quaternion.identity;
                _a.SetIKPosition(AvatarIKGoal.LeftHand, p);
                _a.SetIKRotation(AvatarIKGoal.LeftHand, adj * _goalRotL);
                if (_hier) _hier.LeftHandIK.SetPositionAndRotation(p, adj * _goalRotL);
            }
            if (_drawW > 0.001f && _bow.valid && _head)
            {
                wr = _drawW;
                Vector3 grip = _goalPosL + _goalRotL * _bow.pos;
                Vector3 cheek = _head.position + _root.rotation * cheekOffset;
                Vector3 toCheek = cheek - grip;
                Vector3 rest = grip + (toCheek.sqrMagnitude > 1e-4f ? toCheek.normalized * braceHeight : Vector3.zero);
                Vector3 nockPos = Vector3.Lerp(rest, cheek, _drawK);
                // the goal is the wrist: shift by where the nock sits on the hand
                Vector3 wrist = nockPos - (_nock.valid ? _goalRotR * _nock.pos : Vector3.zero);
                _a.SetIKPosition(AvatarIKGoal.RightHand, wrist);
                if (_hier) _hier.RightHandIK.position = wrist;
            }
            // action programs (drink at water, container to the mouth): asked for this frame or last, else they fade out
            bool asked = Time.frameCount - _actFrame <= 1 && on;
            _actWL = Mathf.MoveTowards(_actWL, asked ? _actWantL : 0f, dt * 4f);
            _actWR = Mathf.MoveTowards(_actWR, asked ? _actWantR : 0f, dt * 4f);
            float wlRot = wl * offHandRotationWeight;
            if (_actWL > 0.001f) { _a.SetIKPosition(AvatarIKGoal.LeftHand, Vector3.Lerp(wl > 0f ? _a.GetIKPosition(AvatarIKGoal.LeftHand) : _goalPosL, _actL, _actWL / Mathf.Max(_actWL, wl))); wl = Mathf.Max(wl, _actWL); }
            if (_actWR > 0.001f) { _a.SetIKPosition(AvatarIKGoal.RightHand, Vector3.Lerp(wr > 0f ? _a.GetIKPosition(AvatarIKGoal.RightHand) : _goalPosR, _actR, _actWR / Mathf.Max(_actWR, wr))); wr = Mathf.Max(wr, _actWR); }
            _a.SetIKPositionWeight(AvatarIKGoal.LeftHand, wl); _a.SetIKRotationWeight(AvatarIKGoal.LeftHand, wlRot);
            _a.SetIKPositionWeight(AvatarIKGoal.RightHand, wr); _a.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
            ElbowHint(AvatarIKHint.LeftElbow, _elbowL, -1f, wl, _hier ? _hier.LeftElbowHint : null);
            ElbowHint(AvatarIKHint.RightElbow, _elbowR, 1f, wr, _hier ? _hier.RightElbowHint : null);
            _appliedL = wl; _appliedR = wr;
        }

        /// <summary>elbow bends outward / down while its hand is on IK (the elbow bone is last frame's: a direction guide only)</summary>
        void ElbowHint(AvatarIKHint hint, Transform elbow, float side, float w, Transform node)
        {
            _a.SetIKHintPositionWeight(hint, elbow ? w : 0f);
            if (w <= 0.001f || !elbow) return;
            Vector3 o = elbowHintOffset;
            Vector3 p = elbow.position + _root.rotation * new Vector3(o.x * side, o.y, o.z);
            _a.SetIKHintPosition(hint, p);
            if (node) node.position = p;
        }

        static void Track(ref HandPoint h, Transform t)
        {
            if (ReferenceEquals(h.t, t)) return;
            h.t = t; h.valid = false;                                   // new point: calibrate before use
        }

        /// <summary>after the Animator wrote the pose: store each hand point in its hand's goal space (only on frames
        /// without IK on that hand, where the final hand is the animated goal)</summary>
        void LateUpdate()
        {
            if (_ikFrame != Time.frameCount) return;                    // no IK pass this frame (culled, climbing)
            if (_appliedR < 0.01f) { Calibrate(ref _grip, _goalPosR, _goalRotR); Calibrate(ref _nock, _goalPosR, _goalRotR); }
            if (_appliedL < 0.01f) Calibrate(ref _bow, _goalPosL, _goalRotL);
        }

        static void Calibrate(ref HandPoint h, Vector3 goalPos, Quaternion goalRot)
        {
            if (!h.t) { h.valid = false; return; }
            h.pos = Quaternion.Inverse(goalRot) * (h.t.position - goalPos);
            h.valid = true;
        }

        void ResolveRefs()
        {
            if (!_head) _head = _a.GetBoneTransform(HumanBodyBones.Head);
            if (!_elbowL) _elbowL = _a.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            if (!_elbowR) _elbowR = _a.GetBoneTransform(HumanBodyBones.RightLowerArm);
            if ((!_hier || !_wc) && (Time.frameCount & 15) == 0)       // added at run time by other components
            {
                if (!_hier) _root.TryGetComponent(out _hier);
                if (!_wc) _root.TryGetComponent(out _wc);
            }
        }

        /// <summary>ground height under the animated ankle relative to the capsule bottom (0 when nothing is hit)</summary>
        float GroundUnder(AvatarIKGoal goal, float baseY)
        {
            Vector3 p = _a.GetIKPosition(goal);
            return Physics.Raycast(new Vector3(p.x, baseY + rayAbove, p.z), Vector3.down, out var hit, rayAbove + rayBelow, groundMask, QueryTriggerInteraction.Ignore) ? hit.point.y - baseY : 0f;
        }

        Vector3 _lockL, _lockR; bool _lockedL, _lockedR;
        /// <summary>
        /// foot lock: while a foot is planted and the body turns / pivots slowly, the ankle keeps the world spot it planted on
        /// (the animated foot would rotate and slide with the capsule). Released when the clip lifts the foot, when the
        /// animated foot drifts further than footLockMaxDrift from the spot, or at speed.
        /// </summary>
        Vector3 LockFoot(AvatarIKGoal goal, Vector3 animated, float planted, bool allow)
        {
            ref Vector3 spot = ref (goal == AvatarIKGoal.LeftFoot ? ref _lockL : ref _lockR);
            ref bool locked = ref (goal == AvatarIKGoal.LeftFoot ? ref _lockedL : ref _lockedR);
            if (!allow || planted < 0.85f) { locked = false; return animated; }
            if (!locked) { spot = animated; locked = true; return animated; }
            Vector3 d = animated - spot; d.y = 0f;
            if (d.magnitude > footLockMaxDrift) { locked = false; return animated; }
            // ease back toward the animation as the drift grows, so the release never pops
            float k = Mathf.InverseLerp(footLockMaxDrift * 0.5f, footLockMaxDrift, d.magnitude);
            Vector3 p = Vector3.Lerp(new Vector3(spot.x, animated.y, spot.z), animated, k);
            return p;
        }

        void Foot(AvatarIKGoal goal, ref float w, bool on, float speedK, float baseY, Vector3 up, ref float wantPelvis, float dt)
        {
            Vector3 p = _a.GetIKPosition(goal);                // animated ankle (world), before IK
            float ankleUp = p.y - baseY;                       // how far the animation lifts this ankle
            float sole = goal == AvatarIKGoal.LeftFoot ? _a.leftFeetBottomHeight : _a.rightFeetBottomHeight;
            if (sole < 0.02f) sole = plantedAnkle;              // ankle height of a flat, planted foot
            float planted = 1f - Mathf.Clamp01((ankleUp - sole - 0.02f) / 0.12f);
            bool lockOk = on && _motor && _motor.PlanarSpeed < footLockMaxSpeed && Mathf.Abs(_motor.TurnRate) > 20f && !(_drv && _drv.IsBusy);
            p = LockFoot(goal, p, planted, lockOk);
            float target = 0f; Vector3 pos = p; Quaternion rot = _a.GetIKRotation(goal);
            if (on && Physics.Raycast(new Vector3(p.x, baseY + rayAbove, p.z), Vector3.down, out var hit, rayAbove + rayBelow, groundMask, QueryTriggerInteraction.Ignore)
                && Vector3.Angle(hit.normal, up) < 50f)
            {
                float ground = hit.point.y - baseY;            // ground under this foot relative to the capsule bottom
                if (planted > 0.5f) wantPelvis = Mathf.Min(wantPelvis, ground * speedK);     // same weight as the foot goal
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

        /// <summary>
        /// hands and feet on the trunk surface or the rock face plane (inside its width); while picking, the right hand goes
        /// to the fruit; while pulling over a lip, both hands grip the lip holds and the feet leave the face at the end
        /// </summary>
        void ClimbIK()
        {
            float dt = Time.deltaTime;
            _holdW = Mathf.MoveTowards(_holdW, _holds ? 1f : 0f, dt * 5f);
            _feetW = Mathf.MoveTowards(_feetW, ClimbFeetFree ? 0f : 1f, dt * 4f);
            Vector3 a = Climb.Bottom, b = Climb.Top; Vector3 ab = b - a; float len2 = Mathf.Max(0.01f, ab.sqrMagnitude);
            bool face = Climb.IsFace;
            Vector3 OnTrunk(Vector3 p, float extra)
            {
                if (face) return Climb.OnFace(p, extra);
                float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2); Vector3 axis = a + ab * t;
                Vector3 d = p - axis; d.y = 0f; if (d.sqrMagnitude < 1e-4f) d = -_root.forward;
                return axis + d.normalized * (Climb.trunkRadius + extra);
            }
            void Goal(AvatarIKGoal g, float w, float extra)
            {
                Vector3 p = _a.GetIKPosition(g);
                Vector3 target = OnTrunk(p, extra);
                if (g == AvatarIKGoal.RightHand && ReachTarget) target = Vector3.Lerp(target, ReachTarget.position, _reachW);
                if (_holdW > 0.001f && (g == AvatarIKGoal.LeftHand || g == AvatarIKGoal.RightHand))
                    target = Vector3.Lerp(target, g == AvatarIKGoal.LeftHand ? _holdL : _holdR, _holdW);
                _a.SetIKPositionWeight(g, w); _a.SetIKPosition(g, target);
                _a.SetIKRotationWeight(g, 0f);
            }
            Goal(AvatarIKGoal.LeftHand, _climbW, 0.03f);
            Goal(AvatarIKGoal.RightHand, _climbW, 0.03f);
            Goal(AvatarIKGoal.LeftFoot, _climbW * 0.7f * _feetW, 0.06f);
            Goal(AvatarIKGoal.RightFoot, _climbW * 0.7f * _feetW, 0.06f);
            ElbowHint(AvatarIKHint.LeftElbow, _elbowL ? _elbowL : (_elbowL = _a.GetBoneTransform(HumanBodyBones.LeftLowerArm)), -1f, _climbW, null);
            ElbowHint(AvatarIKHint.RightElbow, _elbowR ? _elbowR : (_elbowR = _a.GetBoneTransform(HumanBodyBones.RightLowerArm)), 1f, _climbW, null);
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
            bool Ok(Vector3 t) { var d = t - head; d.y = 0f; return d.sqrMagnitude > 0.04f && Vector3.Angle(fwd, d) < lookCone; }
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
                // where the camera looks, kept inside a cone that narrows while moving / turning (no weight drop at the cone edge:
                // the point is rotated onto the edge, and the eased look point follows it)
                var t = cam.transform.position + cam.transform.forward * 12f;
                var d = t - head; float dy = d.y; d.y = 0f;
                if (d.sqrMagnitude > 0.04f)
                {
                    float limit = Mathf.Lerp(80f, 35f, _moveK);
                    float a = Vector3.SignedAngle(fwd, d, Vector3.up);
                    if (Mathf.Abs(a) > limit) d = Quaternion.AngleAxis(Mathf.Sign(a) * limit, Vector3.up) * fwd * d.magnitude;
                    target = head + d + Vector3.up * dy; return true;
                }
            }
            // nothing to look at: straight ahead
            target = head + fwd * 8f; return true;
        }
    }
}
