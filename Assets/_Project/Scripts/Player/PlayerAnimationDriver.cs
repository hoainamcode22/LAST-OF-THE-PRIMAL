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

        PlayerMotor _motor;
        int _pendingAction = PlayerActions.None; bool _pendingIsOneShot; float _pendingTime;
        int _pendingHealth; float _healthTime;
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
        }

        void Update()
        {
            if (!animator || !animator.isActiveAndEnabled) return;
            float dt = Time.deltaTime;
            animator.SetFloat(AnimParams.Speed, _motor.PlanarSpeed, speedDamp, dt);
            animator.SetBool(AnimParams.IsGrounded, _motor.IsGrounded);
            animator.SetFloat(AnimParams.VerticalVelocity, _motor.VerticalVelocity);
            animator.SetBool(AnimParams.IsCrouching, _motor.IsCrouching);
            animator.SetFloat(AnimParams.TurnSpeed, _motor.PlanarSpeed < 0.1f ? _motor.TurnRate : 0f, 0.1f, dt);

            var st = animator.GetCurrentAnimatorStateInfo(0);
            var nx = animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0) : st;
            bool actionTag(AnimatorStateInfo s) => s.IsTag("Action") || s.IsTag("Attack") || s.IsTag("Hurt") || s.IsTag("Dead");
            bool wasBusy = IsBusy;
            IsBusy = actionTag(st) || actionTag(nx);
            IsAttackingState = st.IsTag("Attack") || nx.IsTag("Attack");
            animator.SetBool(AnimParams.IsAttacking, _pendingAction >= PlayerActions.AttackSpear && _pendingAction <= PlayerActions.ThrowSpear || IsAttackingState);

            // clear one-shot actions once the state has been entered (or give up after 1 s)
            if (_pendingIsOneShot && _pendingAction != PlayerActions.None)
            {
                bool entered = (nx.IsTag("Action") || nx.IsTag("Attack")) && Time.time - _pendingTime > 0.02f;
                if (entered || Time.time - _pendingTime > 1f)
                {
                    animator.SetInteger(AnimParams.Action, PlayerActions.None); _pendingAction = PlayerActions.None;
                }
            }
            if (_pendingHealth != 0 && Time.time - _healthTime > 0.1f && _pendingHealth != PlayerHealthStates.Dead)
            {
                animator.SetInteger(AnimParams.HealthState, PlayerHealthStates.Normal); _pendingHealth = 0;
            }
            if (wasBusy && !IsBusy && CurrentAction != PlayerActions.None && !PlayerActions.IsLooping(CurrentAction) && !PlayerActions.IsUpperBody(CurrentAction))
            {
                int a = CurrentAction; CurrentAction = PlayerActions.None; ActionFinished?.Invoke(a);
            }
            _motor.CanMove = !IsDead && !(IsBusy && !st.IsTag("Hurt"));
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
            if (IsDead || IsBusy) return;
            PlayAction(id);
        }

        public void Hurt(bool heavy)
        {
            if (IsDead || !animator) return;
            _pendingHealth = heavy ? PlayerHealthStates.HurtHeavy : PlayerHealthStates.HurtLight; _healthTime = Time.time;
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
