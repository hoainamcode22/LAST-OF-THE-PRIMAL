using System;
using UnityEngine;
using PrimalFrontier.Core;

namespace PrimalFrontier.Player
{
    public enum PlayerMode { Explore, Combat, Aiming, Climbing, Building, Busy, Sleeping, Dead }
    /// <summary>
    /// what the body is doing, finer than PlayerMode (directive 3.5 item 9): one answer for camera, audio, AI and UI instead
    /// of attack / busy booleans read from several scripts
    /// </summary>
    public enum PlayerActivity { Idle, Move, Run, Sprint, Jump, Attack, HeavyAttack, Block, Aim, Gather, Interact, Climb, Build, Eat, Drink, Hurt, Dead, Sleep }

    /// <summary>
    /// One place to ask "what is the player doing right now" (camera, HUD, music, AI can read it or listen to
    /// Changed). Read-only: it is worked out every frame from the systems that own each situation (health, climbing,
    /// build mode, combat, the animation driver, the game state), so it can never disagree with them.
    /// Priority: Dead > Sleeping > Climbing > Building > Aiming > Busy (an action animation) > Combat > Explore.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerState : MonoBehaviour
    {
        public static PlayerState Instance { get; private set; }
        public PlayerMode Mode { get; private set; }
        public float TimeInMode => Time.time - _since;
        /// <summary>(previous, current)</summary>
        public event Action<PlayerMode, PlayerMode> Changed;
        /// <summary>the body's activity this frame (Idle / Move / Run / Sprint / Jump / Attack / HeavyAttack / Gather ...)</summary>
        public PlayerActivity Activity { get; private set; }
        /// <summary>(previous, current)</summary>
        public event Action<PlayerActivity, PlayerActivity> ActivityChanged;

        PlayerHealth _hp; PlayerClimb _climb; PlayerCombat _combat; PlayerAnimationDriver _drv; PlayerMotor _motor; float _since;

        void Awake()
        {
            Instance = this; _hp = GetComponent<PlayerHealth>(); _combat = GetComponent<PlayerCombat>(); _drv = GetComponent<PlayerAnimationDriver>();
            _motor = GetComponent<PlayerMotor>();
        }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            var a = EvaluateActivity();
            if (a != Activity) { var was = Activity; Activity = a; ActivityChanged?.Invoke(was, a); }
            var m = Evaluate();
            if (m == Mode) return;
            var old = Mode; Mode = m; _since = Time.time;
            Changed?.Invoke(old, m);
        }

        PlayerActivity EvaluateActivity()
        {
            if (_hp && _hp.IsDead) return PlayerActivity.Dead;
            int act = _drv ? _drv.CurrentAction : Animation.PlayerActions.None;
            if ((GameManager.Instance && GameManager.Instance.State == GameState.Sleeping) || act == Animation.PlayerActions.Sleep) return PlayerActivity.Sleep;
            if (!_climb) _climb = GetComponent<PlayerClimb>();
            if (_climb && _climb.IsClimbing) return PlayerActivity.Climb;
            if (_drv && _drv.IsHurtState) return PlayerActivity.Hurt;
            var wc = _combat ? _combat.Weapons : null;
            if (wc && wc.IsAttacking) return wc.CurrentIsHeavy ? PlayerActivity.HeavyAttack : PlayerActivity.Attack;
            if (_drv && _drv.IsAttackingState) return Animation.PlayerActions.IsHeavyAttack(act) ? PlayerActivity.HeavyAttack : PlayerActivity.Attack;
            if (_combat && _combat.IsBlocking) return PlayerActivity.Block;
            if (_combat && _combat.Aiming) return PlayerActivity.Aim;
            var bs = Building.BuildSystem.Instance;
            if ((bs && bs.Active) || act == Animation.PlayerActions.Build) return PlayerActivity.Build;
            if (act == Animation.PlayerActions.Eat) return PlayerActivity.Eat;
            if (act == Animation.PlayerActions.Drink || act == Animation.PlayerActions.DrinkKneel || act == Animation.PlayerActions.CollectWater) return PlayerActivity.Drink;
            if (Animation.PlayerActions.IsGather(act)) return PlayerActivity.Gather;
            if (act != Animation.PlayerActions.None && !Animation.PlayerActions.IsUpperBody(act) && act != Animation.PlayerActions.Dodge) return PlayerActivity.Interact;
            if (_motor)
            {
                if (!_motor.IsGrounded) return PlayerActivity.Jump;
                float v = _motor.PlanarSpeed;
                if (v < 0.1f) return PlayerActivity.Idle;
                if (_motor.IsSprinting) return PlayerActivity.Sprint;
                return v > _motor.walkSpeed + 0.3f ? PlayerActivity.Run : PlayerActivity.Move;
            }
            return PlayerActivity.Idle;
        }

        PlayerMode Evaluate()
        {
            if (_hp && _hp.IsDead) return PlayerMode.Dead;
            if (GameManager.Instance && GameManager.Instance.State == GameState.Sleeping) return PlayerMode.Sleeping;
            if (!_climb) _climb = GetComponent<PlayerClimb>();
            if (_climb && _climb.IsClimbing) return PlayerMode.Climbing;
            var bs = Building.BuildSystem.Instance;
            if (bs && bs.Active) return PlayerMode.Building;
            if (_combat && _combat.Aiming) return PlayerMode.Aiming;
            if (_drv && _drv.IsBusy) return PlayerMode.Busy;
            if (_combat && _combat.InCombat) return PlayerMode.Combat;
            return PlayerMode.Explore;
        }
    }
}
