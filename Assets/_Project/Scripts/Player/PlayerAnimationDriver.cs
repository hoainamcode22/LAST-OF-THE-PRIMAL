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
        bool _paramsKnown, _hasVel, _hasIsMoving, _hasStrafe, _hasBodyBusy, _hasHurtLight;
        float _hurtLightTime = -1f;
        float _airT, _lastGroundY;
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
            _motor.CanMove = !IsDead && !(IsBusy && !st.IsTag("Hurt"));

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
            }
            _paramsKnown = true;
        }

        /// <summary>start a full-body or upper-body action (PlayerActions id)</summary>
        public void PlayAction(int id)
        {
            if (IsDead || !animator) return;
            CurrentAction = id;
            bool oneShot = !PlayerActions.IsLooping(id) && !PlayerActions.IsUpperBody(id);
            animator.SetInteger(AnimParams.Action, id);
            _pendingAction = id; _pendingIsOneShot = oneShot; _pendingTime = Time.time;
            ActionStarted?.Invoke(id);
        }

        /// <summary>end a looping action (gather, craft, build, sleep) or the upper-body pose</summary>
        public void StopAction()
        {
            if (!animator) return;
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
