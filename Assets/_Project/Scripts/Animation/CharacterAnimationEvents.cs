using System;
using UnityEngine;

namespace PrimalFrontier.Animation
{
    /// <summary>
    /// Receives AnimationEvents baked into the character/dinosaur clips (see the *_anim.json files next to each model)
    /// and forwards them as one C# event, so gameplay (audio, VFX, damage) listens here instead of polling in Update().
    /// Every function name used by a clip must exist on this component, otherwise Unity logs "has no receiver".
    /// </summary>
    [DisallowMultipleComponent]
    public class CharacterAnimationEvents : MonoBehaviour
    {
        /// <summary>(eventName, parameter) e.g. ("OnFootstep", "L"), ("OnAttackHit", "spear_thrust"), ("OnBite", "")</summary>
        public event Action<string, string> AnimationEventRaised;

        [Tooltip("Log every received event (debug).")]
        [SerializeField] private bool logEvents;

        private void Raise(string name, string param)
        {
            if (logEvents) Debug.Log($"[AnimEvent] {name}({param}) on {name}", this);
            AnimationEventRaised?.Invoke(name, param);
        }

        // locomotion
        public void OnFootstep(string foot) => Raise(nameof(OnFootstep), foot);
        public void OnJumpTakeoff(string p) => Raise(nameof(OnJumpTakeoff), p);
        public void OnLand(string p) => Raise(nameof(OnLand), p);
        // survival / interaction
        public void OnPickup(string p) => Raise(nameof(OnPickup), p);
        public void OnGatherHit(string p) => Raise(nameof(OnGatherHit), p);
        public void OnInteract(string p) => Raise(nameof(OnInteract), p);
        public void OnCraftTick(string p) => Raise(nameof(OnCraftTick), p);
        public void OnDrink(string p) => Raise(nameof(OnDrink), p);
        public void OnEat(string p) => Raise(nameof(OnEat), p);
        public void OnBuildHit(string p) => Raise(nameof(OnBuildHit), p);
        public void OnUseItem(string p) => Raise(nameof(OnUseItem), p);
        public void OnWakeUp(string p) => Raise(nameof(OnWakeUp), p);
        // combat
        public void OnAttackHit(string p) => Raise(nameof(OnAttackHit), p);
        public void OnWeaponImpact(string p) => Raise(nameof(OnWeaponImpact), p);
        public void OnThrowRelease(string p) => Raise(nameof(OnThrowRelease), p);
        public void OnBowDrawStart(string p) => Raise(nameof(OnBowDrawStart), p);
        public void OnBowRelease(string p) => Raise(nameof(OnBowRelease), p);
        public void OnHurt(string p) => Raise(nameof(OnHurt), p);
        public void OnBodyFall(string p) => Raise(nameof(OnBodyFall), p);
        // creatures
        public void OnBite(string p) => Raise(nameof(OnBite), p);
        public void OnRoar(string p) => Raise(nameof(OnRoar), p);
        public void OnCall(string p) => Raise(nameof(OnCall), p);
        public void OnTailHit(string p) => Raise(nameof(OnTailHit), p);
        public void OnHornHit(string p) => Raise(nameof(OnHornHit), p);
        public void OnClawHit(string p) => Raise(nameof(OnClawHit), p);
        public void OnBreath(string p) => Raise(nameof(OnBreath), p);
    }
}
