using System;
using UnityEngine;

namespace PrimalFrontier.Player
{
    /// <summary>Player health with bleeding. Other survival stats (hunger, thirst...) live in PlayerVitals and call into this.</summary>
    public class PlayerHealth : MonoBehaviour
    {
        public float maxHealth = 100f;
        public float bleedDamagePerSecond = 1.2f;
        public float Health { get; private set; }
        public bool IsDead { get; private set; }
        public bool IsBleeding => Time.time < _bleedUntil;
        public float Normalized => Health / maxHealth;

        /// <summary>amount, source world position, heavy hit</summary>
        public event Action<float, Vector3, bool> Damaged;
        public event Action<float> Healed;
        public event Action<bool> BleedingChanged;
        public event Action Died, Revived;

        float _bleedUntil; bool _wasBleeding;
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

        void Awake() { Health = maxHealth; }

        void Update()
        {
            if (IsDead) return;
            if (IsBleeding) ApplyRaw(bleedDamagePerSecond * Time.deltaTime);
            if (_wasBleeding != IsBleeding) { _wasBleeding = IsBleeding; BleedingChanged?.Invoke(_wasBleeding); }
        }

        public void TakeDamage(float amount, Vector3 source, bool heavy = false, float bleedSeconds = 0f)
        {
            if (IsDead || amount <= 0f) return;
            if (Invulnerable) { Evaded?.Invoke(source); return; }         // dodged
            var filter = IncomingHitFilter;
            LastHitBlocked = filter != null && filter(ref amount, source, ref heavy, ref bleedSeconds);
            if (amount <= 0f) return;                                     // the guard stopped all of it (its own feedback played)
            ApplyRaw(amount);
            if (bleedSeconds > 0f) _bleedUntil = Mathf.Max(_bleedUntil, Time.time + bleedSeconds);
            Damaged?.Invoke(amount, source, heavy);
        }

        /// <summary>damage without hit reaction (starvation, cold, bleeding)</summary>
        public void ApplyRaw(float amount)
        {
            if (IsDead) return;
            Health = Mathf.Max(0f, Health - amount);
            if (Health <= 0f) { IsDead = true; _bleedUntil = 0f; Died?.Invoke(); }
        }

        public void Heal(float amount)
        {
            if (IsDead || amount <= 0f) return;
            Health = Mathf.Min(maxHealth, Health + amount); Healed?.Invoke(amount);
        }

        public void StopBleeding() { _bleedUntil = 0f; }

        public void Revive(float healthFraction = 0.5f)
        {
            IsDead = false; Health = maxHealth * Mathf.Clamp01(healthFraction); _bleedUntil = 0f; Revived?.Invoke();
        }

        public void SetHealth(float value) { Health = Mathf.Clamp(value, 0f, maxHealth); IsDead = Health <= 0f; }
    }
}
