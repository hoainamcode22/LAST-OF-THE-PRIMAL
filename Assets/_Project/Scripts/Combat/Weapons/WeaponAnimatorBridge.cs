using System;
using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Player;

namespace PrimalFrontier.Combat.Weapons
{
    /// <summary>
    /// The weapons' only door to the Animator: plays actions through PlayerAnimationDriver, writes WeaponType /
    /// CombatMode / AttackSpeed (only when the controller has them, and only on change), reads the attack state's
    /// normalized time for the fallback hit windows, and forwards the clip events (OnAttackStart / Active / End,
    /// OnAttackHit ...) to the WeaponController.
    /// </summary>
    public class WeaponAnimatorBridge
    {
        readonly PlayerAnimationDriver _drv;
        readonly CharacterAnimationEvents _events;
        readonly Action<string, string> _onEvent;
        bool _paramsKnown, _hasCombatMode, _hasWeaponType, _hasAttackSpeed;
        bool _combatMode; int _weaponType = -1; float _attackSpeed = -1f;

        public WeaponAnimatorBridge(PlayerAnimationDriver driver, CharacterAnimationEvents events, Action<string, string> onEvent)
        {
            _drv = driver; _events = events; _onEvent = onEvent;
            if (_events) _events.AnimationEventRaised += Forward;
        }

        public void Dispose() { if (_events) _events.AnimationEventRaised -= Forward; }
        void Forward(string name, string param) => _onEvent?.Invoke(name, param);

        public PlayerAnimationDriver Driver => _drv;
        public Animator Animator => _drv ? _drv.animator : null;
        public bool IsDead => _drv && _drv.IsDead;
        public bool IsBusy => _drv && _drv.IsBusy;
        public bool IsAttackingState => _drv && _drv.IsAttackingState;
        public int CurrentAction => _drv ? _drv.CurrentAction : PlayerActions.None;

        // ------------------------------------------------------------------ actions
        public void Play(int action) { if (_drv && action != PlayerActions.None) _drv.PlayAction(action); }
        public void Stop() { if (_drv) _drv.StopAction(); }

        // ------------------------------------------------------------------ parameters
        bool Ready()
        {
            var a = Animator;
            if (!a || !a.isActiveAndEnabled || !a.runtimeAnimatorController) return false;
            if (_paramsKnown) return true;
            foreach (var p in a.parameters)                                   // once: the array is allocated by Unity
            {
                if (p.nameHash == AnimParams.CombatMode && p.type == AnimatorControllerParameterType.Bool) _hasCombatMode = true;
                else if (p.nameHash == AnimParams.WeaponType && p.type == AnimatorControllerParameterType.Int) _hasWeaponType = true;
                else if (p.nameHash == AnimParams.AttackSpeed && p.type == AnimatorControllerParameterType.Float) _hasAttackSpeed = true;
            }
            _paramsKnown = true;
            return true;
        }

        /// <summary>the weapon in hand (null = none): WeaponType = WeaponKind, AttackSpeed = data.attackSpeed</summary>
        public void SetWeapon(WeaponData data)
        {
            if (!Ready()) { _weaponType = -1; return; }
            int type = data ? (int)data.kind : 0;
            if (_hasWeaponType && type != _weaponType) Animator.SetInteger(AnimParams.WeaponType, type);
            _weaponType = type;
            float speed = data ? Mathf.Max(0.1f, data.attackSpeed) : 1f;
            if (_hasAttackSpeed && !Mathf.Approximately(speed, _attackSpeed)) Animator.SetFloat(AnimParams.AttackSpeed, speed);
            _attackSpeed = speed;
        }

        public void SetCombatMode(bool on)
        {
            if (!Ready() || on == _combatMode) return;
            _combatMode = on;
            if (_hasCombatMode) Animator.SetBool(AnimParams.CombatMode, on);
        }

        // ------------------------------------------------------------------ attack state (fallback windows)
        /// <summary>the Attack-tagged state the body is entering or fully in (never one it is blending out of)</summary>
        public bool TryGetEnteringAttack(out int stateHash)
        {
            stateHash = 0;
            var a = Animator; if (!a || !a.isActiveAndEnabled) return false;
            if (a.IsInTransition(0))
            {
                var nx = a.GetNextAnimatorStateInfo(0);
                if (nx.IsTag("Attack")) { stateHash = nx.fullPathHash; return true; }
                return false;
            }
            var st = a.GetCurrentAnimatorStateInfo(0);
            if (st.IsTag("Attack")) { stateHash = st.fullPathHash; return true; }
            return false;
        }

        /// <summary>normalized time of that state while it plays, blends in or blends out; false once it is gone</summary>
        public bool TryGetStateTime(int stateHash, out float normalized)
        {
            normalized = 0f;
            var a = Animator; if (!a || !a.isActiveAndEnabled || stateHash == 0) return false;
            var st = a.GetCurrentAnimatorStateInfo(0);
            if (a.IsInTransition(0))
            {
                var nx = a.GetNextAnimatorStateInfo(0);
                if (nx.fullPathHash == stateHash) { normalized = nx.normalizedTime; return true; }
                if (st.fullPathHash == stateHash) { normalized = st.normalizedTime; return true; }
                return false;
            }
            if (st.fullPathHash == stateHash) { normalized = st.normalizedTime; return true; }
            return false;
        }
    }
}
