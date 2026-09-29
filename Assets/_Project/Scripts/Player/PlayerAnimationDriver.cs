using System;
using UnityEngine;
using PrimalFrontier.Animation;

namespace PrimalFrontier.Player
{
    /// <summary>
    /// Feeds PlayerAnimator.controller from the motor and exposes a small action API for gameplay:
    /// PlayAction / StopAction / Attack / Hurt / Die / Respawn. One-shot parameters are reset automatically once the
    /// Animator has entered the matching state, so a trigger never plays twice.
    /// </summary>
    [RequireComponent(typeof(PlayerMotor))]
    public class PlayerAnimationDriver : MonoBehaviour
    {
        public Animator animator;
        public float speedDamp = 0.08f;
        [Tooltip("damping of VelX / VelZ (2D strafe locomotion)")] public float velDamp = 0.1f;
        [Tooltip("the Animator's IsGrounded goes false only after this long off the ground (steps and bumps do not play Fall)")]
        public float fallDelay = 0.12f;
        [Tooltip("...or once the body is this far below the last ground (m)")] public float fallDrop = 0.6f;
        bool _paramsKnown, _hasVel, _hasIsMoving, _hasStrafe, _hasBodyBusy, _hasHurtLight, _hasLocoRate, _hasLocoClips;
        float _locoRateIdle = 1f;
        int _braking = PlayerActions.None; float _brakeT;
        /// <summary>a full-body action is waiting for the motor to brake (PlayAction while moving)</summary>
        public bool Braking => _braking != PlayerActions.None;
        /// <summary>leaving an action: the exit blend has not reached exitMoveAt yet (no movement, no turning)</summary>
        public bool ExitHold { get; private set; }
        float _hurtLightTime = -1f;
        float _airT, _lastGroundY;
        [Header("Actions (phase A timing)")]
        [Tooltip("full-body actions wait until the motor has braked below this planar speed (m/s)")] public float actionBrakeSpeed = 0.5f;
        [Tooltip("...or at most this long (s)")] public float actionBrakeTimeout = 0.6f;
        [Tooltip("leaving an action: movement and turning come back once the exit blend is this far (0..1)")] [Range(0, 1)] public float exitMoveAt = 0.6f;
        [Header("Locomotion")]
        [Tooltip("Speed (m/s) at which the Locomotion state reaches full rate: below it the time-scaled Idle child plays at its own rate")]
        public float locoRateFullAt = 0.3f;
        [Tooltip("phase C: select start / stop / pivot clips (states exist only when the clips are in the FBX). Off until reviewed.")]
        public bool useStartStopClips = false;
        [Tooltip("seconds of standing still before an idle variation (look around, shift weight) plays; random in this range")]
        public Vector2 idleVariationEvery = new Vector2(7f, 14f);
        float _idleT, _idleNext = 9f; int _hasIdleVariant = -1;

        PlayerMotor _motor;
        int _pendingAction = PlayerActions.None; bool _pendingIsOneShot; float _pendingTime;
        int _pendingHealth; float _healthTime; int _pendingHealthState;
        int _queuedAttack; float _queuedAttackTime;
        static readonly int HurtHash = Animator.StringToHash("Hurt"), HurtHeavyHash = Animator.StringToHash("Hurt_Heavy"), DeathHash = Animator.StringToHash("Death");
        public int CurrentAction { get; private set; }
        public bool IsDead { get; private set; }
        /// <summary>true while a full-body action / attack / hurt / death state owns the body</summary>
        public bool IsBusy { get; private set; }
        public bool IsAttackingState { get; private set; }
        /// <summary>the base layer is in (or entering) a full-body Hurt / Hurt_Heavy state</summary>
        public bool IsHurtState { get; private set; }
        public event Action<int> ActionStarted, ActionFinished;

        void Awake()
        {
            _motor = GetComponent<PlayerMotor>();
            if (!animator) animator = GetComponentInChildren<Animator>();
            if (animator && !animator.GetComponent<PlayerIK>()) animator.gameObject.AddComponent<PlayerIK>();   // feet on the ground, head looks, lean
            if (animator && !animator.GetComponent<TwistBoneDriver>()) animator.gameObject.AddComponent<TwistBoneDriver>(); // forearm twist (off without twist bones)
            if (!GetComponent<PlayerState>()) gameObject.AddComponent<PlayerState>();                         // what the player is doing (read-only)
            if (!GetComponent<PlayerWetLook>()) gameObject.AddComponent<PlayerWetLook>();                     // darker, shinier when wet
        }

        void Update()
        {
            if (!animator || !animator.isActiveAndEnabled) return;
            float dt = Time.deltaTime;
            if (!_paramsKnown) FindParams();
            // Speed: commanded speed, capped by what the body really covers (+0.3 so starting is not delayed; walls stop the legs)
            float planar = _motor.PlanarSpeed;
            bool grounded = _motor.IsGrounded;
            float speed = grounded ? Mathf.Min(planar, _motor.MeasuredPlanarSpeed + 0.3f) : planar;
            animator.SetFloat(AnimParams.Speed, speed, speedDamp, dt);
            if (_hasVel)
            {
                Vector3 lv = _motor.LocalPlanarVelocity;
                float k = planar > 0.01f ? speed / planar : 0f;
                animator.SetFloat(AnimParams.VelX, lv.x * k, velDamp, dt);
                animator.SetFloat(AnimParams.VelZ, lv.z * k, velDamp, dt);
            }
            if (_hasIsMoving) animator.SetBool(AnimParams.IsMoving, speed > 0.15f);
            // Idle is time-scaled to Walk's cycle inside the Locomotion tree (so walking starts / stops keep the walk cadence);
            // standing still, the state slows back down so Idle plays at its own rate
            if (_hasLocoRate) animator.SetFloat(AnimParams.LocoRate, Mathf.Lerp(_locoRateIdle, 1f, Mathf.Clamp01(animator.GetFloat(AnimParams.Speed) / Mathf.Max(0.01f, locoRateFullAt))));
            if (_hasStrafe) animator.SetBool(AnimParams.Strafe, _motor.AimMode);
            // air: a jump is airborne at once; otherwise only after fallDelay off the ground or a real drop
            float y = transform.position.y;
            if (grounded) { _airT = 0f; _lastGroundY = y; } else _airT += dt;
            bool airborne = !grounded && (_motor.VerticalVelocity > 0.5f || _airT >= fallDelay || _lastGroundY - y > fallDrop);
            animator.SetBool(AnimParams.IsGrounded, !airborne);
            animator.SetFloat(AnimParams.VerticalVelocity, _motor.VerticalVelocity);
            animator.SetBool(AnimParams.IsCrouching, _motor.IsCrouching);
            animator.SetFloat(AnimParams.TurnSpeed, _motor.PlanarSpeed < 0.1f ? _motor.TurnRate : 0f, 0.1f, dt);

            var st = animator.GetCurrentAnimatorStateInfo(0);
            var nx = animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0) : st;
            bool actionTag(AnimatorStateInfo s) => s.IsTag("Action") || s.IsTag("Attack") || s.IsTag("Hurt") || s.IsTag("Dead");
            bool wasBusy = IsBusy;
            bool inTrans = animator.IsInTransition(0);
            IsBusy = inTrans ? actionTag(nx) : actionTag(st);          // blending out of an action already gives control back
            IsAttackingState = st.IsTag("Attack") || nx.IsTag("Attack");
            IsHurtState = inTrans ? nx.IsTag("Hurt") : st.IsTag("Hurt");
            animator.SetBool(AnimParams.IsAttacking, PlayerActions.IsAttack(_pendingAction) || IsAttackingState);
            // upper-body poses (sword idle) give way while the base layer owns the whole body
            if (_hasBodyBusy)
                animator.SetBool(AnimParams.FullBodyBusy, IsBusy || st.IsTag("Climb") || st.IsTag("ClimbEnd") || nx.IsTag("Climb") || nx.IsTag("ClimbEnd"));

            // clear one-shot actions once the state has been entered (or give up after 1 s)
            if (_pendingIsOneShot && _pendingAction != PlayerActions.None)
            {
                bool entered = (nx.IsTag("Action") || nx.IsTag("Attack")) && Time.time - _pendingTime > 0.02f;
                if (entered || Time.time - _pendingTime > 1f)
                {
                    animator.SetInteger(AnimParams.Action, PlayerActions.None); _pendingAction = PlayerActions.None;
                }
            }
            // hurt pulses stay set until the Animator has actually entered the reaction state (or 1 s passed)
            if (_pendingHealth != 0 && _pendingHealth != PlayerHealthStates.Dead)
            {
                bool entered = st.shortNameHash == _pendingHealthState || (inTrans && nx.shortNameHash == _pendingHealthState);
                if (entered || Time.time - _healthTime > 1f)
                {
                    animator.SetInteger(AnimParams.HealthState, PlayerHealthStates.Normal); _pendingHealth = 0;
                }
            }
            // a light-hit trigger the HitReaction layer did not take (layer missing / muted) must not flinch much later
            if (_hurtLightTime >= 0f && Time.time - _hurtLightTime > 0.3f) { animator.ResetTrigger(AnimParams.HurtLight); _hurtLightTime = -1f; }
            // buffered attack (pressed during the previous swing)
            if (_queuedAttack != 0 && !IsBusy && !IsDead)
            {
                int q = _queuedAttack; _queuedAttack = 0;
                if (Time.time - _queuedAttackTime < 1.2f) PlayAction(q);       // generous: slow frames must not drop a press
            }
            if (wasBusy && !IsBusy && CurrentAction != PlayerActions.None && !PlayerActions.IsLooping(CurrentAction) && !PlayerActions.IsUpperBody(CurrentAction))
            {
                int a = CurrentAction; CurrentAction = PlayerActions.None; ActionFinished?.Invoke(a);
            }
            // brake first: the action starts once the body is (nearly) standing
            if (_braking != PlayerActions.None && (IsDead || !_motor.IsGrounded || _motor.PlanarSpeed <= actionBrakeSpeed || Time.time - _brakeT > actionBrakeTimeout))
            {
                int b = _braking; _braking = PlayerActions.None;
                if (!IsDead) StartAction(b);
            }
            // leaving an action (not an attack / hurt): no movement or turning until the exit blend is exitMoveAt through
            ExitHold = inTrans && st.IsTag("Action") && !actionTag(nx) && animator.GetAnimatorTransitionInfo(0).normalizedTime < exitMoveAt;
            _motor.CanMove = !IsDead && !(IsBusy && !st.IsTag("Hurt")) && _braking == PlayerActions.None && !ExitHold;
            if (_hasLocoClips) UpdateLocoClips(speed);

            // idle life: after standing still for a while, look around / shift weight (Idle_Variation), then back
            bool still = _motor.PlanarSpeed < 0.05f && _motor.IsGrounded && !IsBusy && !IsDead && !_motor.IsCrouching && st.IsName("Locomotion") && !inTrans;
            _idleT = still ? _idleT + dt : 0f;
            if (_idleT > _idleNext)
            {
                if (_hasIdleVariant < 0) { _hasIdleVariant = 0; foreach (var prm in animator.parameters) if (prm.nameHash == AnimParams.IdleVariant) _hasIdleVariant = 1; }
                if (_hasIdleVariant == 1) animator.SetTrigger(AnimParams.IdleVariant);
                _idleT = 0f; _idleNext = UnityEngine.Random.Range(idleVariationEvery.x, idleVariationEvery.y);
            }
        }

        /// <summary>once: which optional parameters the controller has (the parameters array is allocated by Unity)</summary>
        void FindParams()
        {
            if (!animator.runtimeAnimatorController) return;
            foreach (var p in animator.parameters)
            {
                if (p.nameHash == AnimParams.VelX && p.type == AnimatorControllerParameterType.Float) _hasVel = true;
                else if (p.nameHash == AnimParams.IsMoving && p.type == AnimatorControllerParameterType.Bool) _hasIsMoving = true;
                else if (p.nameHash == AnimParams.Strafe && p.type == AnimatorControllerParameterType.Bool) _hasStrafe = true;
                else if (p.nameHash == AnimParams.FullBodyBusy && p.type == AnimatorControllerParameterType.Bool) _hasBodyBusy = true;
                else if (p.nameHash == AnimParams.HurtLight && p.type == AnimatorControllerParameterType.Trigger) _hasHurtLight = true;
                else if (p.nameHash == AnimParams.LocoRate && p.type == AnimatorControllerParameterType.Float) _hasLocoRate = true;
                else if (p.nameHash == AnimParams.LocoIdleRate && p.type == AnimatorControllerParameterType.Float) _locoRateIdle = p.defaultFloat > 0f ? p.defaultFloat : 1f;
                else if (p.nameHash == AnimParams.UseLocoClips && p.type == AnimatorControllerParameterType.Bool) _hasLocoClips = true;
            }
            _paramsKnown = true;
        }

        /// <summary>start a full-body or upper-body action (PlayerActions id)</summary>
        public void PlayAction(int id)
        {
            if (IsDead || !animator) return;
            CurrentAction = id;
            // full-body actions never start at speed: brake first (the motor stops steering; Update starts the action)
            if (PlayerActions.NeedsStop(id) && _motor && _motor.IsGrounded && _motor.PlanarSpeed > actionBrakeSpeed)
            {
                _braking = id; _brakeT = Time.time; _motor.CanMove = false;
                return;
            }
            _braking = PlayerActions.None;
            StartAction(id);
        }

        void StartAction(int id)
        {
            bool oneShot = !PlayerActions.IsLooping(id) && !PlayerActions.IsUpperBody(id);
            animator.SetInteger(AnimParams.Action, id);
            _pendingAction = id; _pendingIsOneShot = oneShot; _pendingTime = Time.time;
            ActionStarted?.Invoke(id);
        }

        /// <summary>end a looping action (gather, craft, build, sleep) or the upper-body pose</summary>
        public void StopAction()
        {
            if (!animator) return;
            _braking = PlayerActions.None;
            int a = CurrentAction;
            animator.SetInteger(AnimParams.Action, PlayerActions.None);
            _pendingAction = PlayerActions.None; CurrentAction = PlayerActions.None;
            if (a != PlayerActions.None) ActionFinished?.Invoke(a);
        }

        public void Attack(int id)
        {
            if (IsDead) return;
            if (IsBusy) { _queuedAttack = id; _queuedAttackTime = Time.time; return; }   // buffer one attack
            PlayAction(id);
        }

        /// <summary>
        /// hit reaction. Light: the HurtLight trigger (additive upper-body flinch on the HitReaction layer), so the base
        /// layer keeps its locomotion and the motor keeps moving; HealthState is not touched. Heavy, or a controller built
        /// before the HitReaction layer (no HurtLight parameter): the full-body Hurt / Hurt_Heavy states via HealthState.
        /// </summary>
        public void Hurt(bool heavy)
        {
            if (IsDead || !animator) return;
            if (!_paramsKnown) FindParams();
            if (!heavy && _hasHurtLight)
            {
                animator.SetTrigger(AnimParams.HurtLight); _hurtLightTime = Time.time;
                if (PlayerActions.IsLooping(CurrentAction)) StopAction();
                return;
            }
            _pendingHealth = heavy ? PlayerHealthStates.HurtHeavy : PlayerHealthStates.HurtLight; _healthTime = Time.time;
            _pendingHealthState = heavy ? HurtHeavyHash : HurtHash;
            animator.SetInteger(AnimParams.HealthState, _pendingHealth);
            if (PlayerActions.IsLooping(CurrentAction)) StopAction();
        }

        public void Die()
        {
            if (IsDead || !animator) return;
            IsDead = true; StopAction();
            _pendingHealth = PlayerHealthStates.Dead;
            animator.SetInteger(AnimParams.HealthState, PlayerHealthStates.Dead);
        }

        public void Respawn()
        {
            if (!animator) return;
            IsDead = false; _pendingHealth = 0;
            animator.SetInteger(AnimParams.HealthState, PlayerHealthStates.Normal);
        }

        // ------------------------------------------------------------------ phase C: start / stop / pivot selection (off)
        float _prevSpeedCmd; bool _wasMoving; int _locoEventFrames;
        /// <summary>
        /// Chooses a start / stop / pivot clip from the motor's commanded speed change and the Locomotion foot phase, and hands it
        /// to the controller as LocoEvent (one frame) + LocoMirror (lead foot). The controller only has the states when the
        /// clips exist (PrimalCharacterBuilder); nothing happens unless useStartStopClips is on.
        /// Start: commanded speed leaves 0 (walk start below walk speed, run start above). Stop: input released from walk / run
        /// speed. Pivot: the motor reports a reversal (> 135 deg) at run speed. Turn_180: standing, input opposite the facing.
        /// Lead foot: Locomotion normalized time (walk / run clips start with the left foot planted at 0): phase in [0.25, 0.75)
        /// means the right foot is planted, so the clip is mirrored.
        /// </summary>
        void UpdateLocoClips(float speed)
        {
            bool on = useStartStopClips;
            animator.SetBool(AnimParams.UseLocoClips, on);
            if (_locoEventFrames > 0 && --_locoEventFrames == 0) animator.SetInteger(AnimParams.LocoEvent, LocoEvents.None);
            if (!on || IsBusy) { _prevSpeedCmd = _motor.PlanarSpeed; return; }
            float cmd = _motor.PlanarSpeed, target = _motor.TargetSpeed;
            bool moving = target > 0.05f;
            int ev = LocoEvents.None;
            if (moving && !_wasMoving && cmd < 0.3f)
                ev = Vector3.Angle(_motor.transform.forward, _motor.TargetDirection) > _motor.reversalAngle ? LocoEvents.Turn180
                   : target > _motor.walkSpeed + 0.2f ? LocoEvents.RunStart : LocoEvents.WalkStart;
            else if (!moving && _wasMoving && cmd > 0.6f) ev = cmd > _motor.walkSpeed + 0.4f ? LocoEvents.RunStop : LocoEvents.WalkStop;
            else if (_motor.PivotRequested && cmd > _motor.walkSpeed + 0.4f) ev = LocoEvents.RunPivot180;
            if (ev != LocoEvents.None)
            {
                var st = animator.GetCurrentAnimatorStateInfo(0);
                float phase = st.normalizedTime - Mathf.Floor(st.normalizedTime);
                animator.SetBool(AnimParams.LocoMirror, phase >= 0.25f && phase < 0.75f);
                animator.SetInteger(AnimParams.LocoEvent, ev); _locoEventFrames = 2;
            }
            _wasMoving = moving; _prevSpeedCmd = cmd;
        }

        /// <summary>opening: freeze on the first frame of Wake_Up (lying on the sand)</summary>
        public void SetUnconscious()
        {
            if (!animator) return;
            animator.Play("Unconscious", 0, 0f);
            animator.SetInteger(AnimParams.Action, PlayerActions.None);
            CurrentAction = PlayerActions.WakeUp;
        }
    }
}
