using System;
using UnityEngine;
using PrimalFrontier.Survival;

namespace PrimalFrontier.Player
{
    /// <summary>
    /// Player health and the damage path. Hunger, thirst, temperature... live in PlayerSurvival and call into this; status
    /// effects (bleeding, injuries, sickness) live in PlayerStatusEffects (added here when missing).
    /// Wounds from a hit that landed (not dodged, not fully blocked), numbers in SurvivalConfig (Injuries):
    /// - a hit with bleedSeconds (the attacker cuts) bleeds that long; a cut while already bleeding, or any hit of at least
    ///   deepWoundDamage, opens a deep wound (deepWoundSeconds) that needs a bandage (or clots after that time);
    /// - a heavy hit of at least injuryMinDamage injures a limb with injuryChance (leg: slower, arm: weaker blows);
    /// - a very hard landing (fallInjurySpeed) hurts the leg.
    /// </summary>
    public class PlayerHealth : MonoBehaviour
    {
        public float maxHealth = 100f;
        [Tooltip("legacy, unused: bleeding now drains through the 'bleeding' status effect (StatusEffectDefinition.healthPerSecond)")]
        public float bleedDamagePerSecond = 1.2f;
        public float Health { get; private set; }
        public bool IsDead { get; private set; }
        public bool IsBleeding => Effects && Effects.Has(StatusEffectIds.Bleeding);
        public float Normalized => Health / maxHealth;

        /// <summary>amount, source world position, heavy hit</summary>
        public event Action<float, Vector3, bool> Damaged;
        public event Action<float> Healed;
        public event Action<bool> BleedingChanged;
        public event Action Died, Revived;

        /// <summary>no damage from hits until this time (dodge window)</summary>
        public float InvulnerableUntil { get; set; }
        public bool Invulnerable => Time.time < InvulnerableUntil;
        public event Action<Vector3> Evaded;

        /// <summary>
        /// The one hook between an incoming hit and the health bar, so attackers never need to know about the guard.
        /// It may lower the damage, change the heavy flag (a held guard does not stagger, a broken one does) and the
        /// bleeding, and returns true when the guard held (feedback then skips the blood). Only for hits that were not
        /// dodged; starvation, cold and bleeding (ApplyRaw) never pass through it.
        /// </summary>
        public delegate bool HitFilter(ref float amount, Vector3 source, ref bool heavy, ref float bleedSeconds);
        /// <summary>set by PlayerCombat (block); null = every hit lands in full</summary>
        public HitFilter IncomingHitFilter { get; set; }
        /// <summary>the last hit that reached TakeDamage was stopped by the guard (valid inside Damaged and after it)</summary>
        public bool LastHitBlocked { get; private set; }
        /// <summary>tests: replaces UnityEngine.Random.value for the limb injury roll (null = random)</summary>
        public static Func<float> InjuryRoll;

        PlayerStatusEffects _fx; bool _fxBound;
        /// <summary>the status effects on this body (created on first use)</summary>
        public PlayerStatusEffects Effects
        {
            get
            {
                if (!_fx) { _fx = GetComponent<PlayerStatusEffects>(); if (!_fx) _fx = gameObject.AddComponent<PlayerStatusEffects>(); _fxBound = false; }
                if (!_fxBound && _fx) { _fx.EffectChanged -= OnEffectChanged; _fx.EffectChanged += OnEffectChanged; _fxBound = true; }
                return _fx;
            }
        }

        void Awake() { Health = maxHealth; _ = Effects; }
        void OnDestroy() { if (_fx) _fx.EffectChanged -= OnEffectChanged; }

        void OnEffectChanged(StatusEffectDefinition def, bool on)
        {
            if (def && def.id == StatusEffectIds.Bleeding) BleedingChanged?.Invoke(on);
        }

        public void TakeDamage(float amount, Vector3 source, bool heavy = false, float bleedSeconds = 0f)
        {
            if (IsDead || amount <= 0f) return;
            if (Invulnerable) { Evaded?.Invoke(source); return; }         // dodged
            var filter = IncomingHitFilter;
            LastHitBlocked = filter != null && filter(ref amount, source, ref heavy, ref bleedSeconds);
            if (amount <= 0f) return;                                     // the guard stopped all of it (its own feedback played)
            ApplyRaw(amount);
            if (!IsDead) Wound(amount, heavy, bleedSeconds);
            Damaged?.Invoke(amount, source, heavy);
        }

        /// <summary>bleeding and limb injuries from a hit that landed (rules in the class summary)</summary>
        void Wound(float amount, bool heavy, float bleedSeconds)
        {
            var c = SurvivalConfig.Instance; var fx = Effects; if (!fx) return;
            bool wasBleeding = fx.Has(StatusEffectIds.Bleeding);
            bool deep = amount >= c.deepWoundDamage || (c.repeatCutDeepens && wasBleeding && bleedSeconds > 0f);
            float bleed = deep ? Mathf.Max(bleedSeconds, c.deepWoundSeconds) : bleedSeconds;
            if (bleed > 0f)
            {
                bool wasDeep = wasBleeding && fx.RemainingOf(StatusEffectIds.Bleeding) > c.deepWoundSeconds * 0.5f;
                fx.Apply(StatusEffectIds.Bleeding, 1f, bleed, !deep);
                if (deep && !wasDeep) fx.Say("A deep wound. It will not stop by itself soon: use a bandage.");
            }
            if (heavy && amount >= c.injuryMinDamage)
            {
                float roll = InjuryRoll != null ? InjuryRoll() : UnityEngine.Random.value;
                if (roll < c.injuryChance)
                {
                    // the same roll picks the limb: low rolls the leg, the rest the arm (legInjuryShare of the injuries)
                    bool leg = roll < c.injuryChance * c.legInjuryShare;
                    fx.Apply(leg ? StatusEffectIds.LegInjury : StatusEffectIds.ArmInjury, 1f, c.injurySeconds);
                }
            }
        }

        /// <summary>a hard landing (PlayerSurvival forwards PlayerMotor.Landed)</summary>
        public void Landed(float impactSpeed)
        {
            var c = SurvivalConfig.Instance;
            if (IsDead || c.fallInjurySpeed <= 0f || impactSpeed < c.fallInjurySpeed) return;
            Effects.Apply(StatusEffectIds.LegInjury, 1f, c.injurySeconds);
        }

        /// <summary>damage without hit reaction (starvation, cold, bleeding)</summary>
        public void ApplyRaw(float amount)
        {
            if (IsDead) return;
            Health = Mathf.Max(0f, Health - amount);
            if (Health <= 0f) { IsDead = true; if (_fx) _fx.Clear(); Died?.Invoke(); }
        }

        public void Heal(float amount)
        {
            if (IsDead || amount <= 0f) return;
            Health = Mathf.Min(maxHealth, Health + amount); Healed?.Invoke(amount);
        }

        /// <summary>a bandage: the bleeding stops (with the cured message)</summary>
        public void StopBleeding() { if (_fx) _fx.Remove(StatusEffectIds.Bleeding, true); }

        public void Revive(float healthFraction = 0.5f)
        {
            IsDead = false; Health = maxHealth * Mathf.Clamp01(healthFraction);
            if (_fx) _fx.Clear();
            Revived?.Invoke();
        }

        public void SetHealth(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) value = maxHealth;
            Health = Mathf.Clamp(value, 0f, maxHealth); IsDead = Health <= 0f;
        }
    }
}
