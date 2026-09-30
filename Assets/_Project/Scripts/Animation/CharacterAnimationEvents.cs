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

        /// <summary>
        /// blend weight of the clip that fired the event being raised (1 when unknown). Blend trees fire the events of
        /// every clip they mix, so listeners can ignore the quiet ones (PlayerFeedback: footsteps below 0.5).
        /// </summary>
        public float EventClipWeight { get; private set; } = 1f;

        private void Raise(string name, string param)
        {
            if (logEvents) Debug.Log($"[AnimEvent] {name}({param}) on {name}", this);
            AnimationEventRaised?.Invoke(name, param);
        }

        // locomotion
        /// <summary>takes the AnimationEvent (not just the string) so the clip's blend weight reaches the listeners</summary>
        public void OnFootstep(AnimationEvent e)
        {
            EventClipWeight = e != null && e.isFiredByAnimator ? e.animatorClipInfo.weight : 1f;
            Raise(nameof(OnFootstep), e != null ? e.stringParameter : "");
            EventClipWeight = 1f;
        }
        public void OnJumpTakeoff(string p) => Raise(nameof(OnJumpTakeoff), p);
        public void OnLand(string p) => Raise(nameof(OnLand), p);
        // survival / interaction
        public void OnPickup(string p) => Raise(nameof(OnPickup), p);
        public void OnGatherHit(string p) => Raise(nameof(OnGatherHit), p);
        public void OnInteract(string p) => Raise(nameof(OnInteract), p);
        public void OnCraftTick(string p) => Raise(nameof(OnCraftTick), p);
        /// <summary>Drink clip (hand at the mouth); also Collect_Water (the placeholder and the builder default for the real clip)</summary>
        public void OnDrink(string p) => Raise(nameof(OnDrink), p);
        public void OnEat(string p) => Raise(nameof(OnEat), p);
        public void OnBuildHit(string p) => Raise(nameof(OnBuildHit), p);
        /// <summary>Use_Item clip; also Bandage_Use (the placeholder and the builder default for the real clip)</summary>
        public void OnUseItem(string p) => Raise(nameof(OnUseItem), p);
        // names a new action clip may carry (CHAR manifest); each is raised under its own name, none may log "no receiver"
        public void OnBandage(string p) => Raise(nameof(OnBandage), p);
        public void OnCollectWater(string p) => Raise(nameof(OnCollectWater), p);
        public void OnScoop(string p) => Raise(nameof(OnScoop), p);
        public void OnWakeUp(string p) => Raise(nameof(OnWakeUp), p);
        public void OnHarvest(string p) => Raise(nameof(OnHarvest), p);
        /// <summary>Climb_Up / Climb_Down hand-foot contacts and the Climb_Start grab. Had no receivers: every climb logged errors.</summary>
        public void OnClimbStep(string p) => Raise(nameof(OnClimbStep), p);
        public void OnClimbGrab(string p) => Raise(nameof(OnClimbGrab), p);
        // combat
        public void OnAttackHit(string p) => Raise(nameof(OnAttackHit), p);
        /// <summary>end of the attack's startup (the swing begins)</summary>
        public void OnAttackStart(string p) => Raise(nameof(OnAttackStart), p);
        /// <summary>the weapon hitbox turns on</summary>
        public void OnAttackActive(string p) => Raise(nameof(OnAttackActive), p);
        /// <summary>the weapon hitbox turns off (recovery starts)</summary>
        public void OnAttackEnd(string p) => Raise(nameof(OnAttackEnd), p);
        /// <summary>equip / unequip: the item moves between the carry socket and the hand</summary>
        public void OnEquip(string p) => Raise(nameof(OnEquip), p);
        public void OnWeaponImpact(string p) => Raise(nameof(OnWeaponImpact), p);
        public void OnThrowRelease(string p) => Raise(nameof(OnThrowRelease), p);
        /// <summary>Dodge clip, frame 4 (the push-off). Had no receiver: Unity logged an error on every dodge.</summary>
        public void OnDodge(string p) => Raise(nameof(OnDodge), p);
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
