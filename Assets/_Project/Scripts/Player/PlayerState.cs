using System;
using UnityEngine;
using PrimalFrontier.Core;

namespace PrimalFrontier.Player
{
    public enum PlayerMode { Explore, Combat, Aiming, Climbing, Building, Busy, Sleeping, Dead }

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

        PlayerHealth _hp; PlayerClimb _climb; PlayerCombat _combat; PlayerAnimationDriver _drv; float _since;

        void Awake() { Instance = this; _hp = GetComponent<PlayerHealth>(); _combat = GetComponent<PlayerCombat>(); _drv = GetComponent<PlayerAnimationDriver>(); }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            var m = Evaluate();
            if (m == Mode) return;
            var old = Mode; Mode = m; _since = Time.time;
            Changed?.Invoke(old, m);
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
