using UnityEngine;
using PrimalFrontier.Items;

namespace PrimalFrontier.Combat
{
    public struct HitInfo
    {
        public float damage;
        public Vector3 point, direction;
        public GameObject attacker;
        public WeaponKind weapon;
        public bool heavy, ranged;
        public float zoneMultiplier;
        /// <summary>bare-hand strike: the target decides how much it matters (large creatures barely notice)</summary>
        public bool unarmed;
        /// <summary>push-back speed (m/s) the target may apply to itself; 0 = none</summary>
        public float knockback;
    }

    /// <summary>Creatures (and later breakables) the player can hit. HitZone colliders forward to the IDamageable in a parent.</summary>
    public interface IDamageable
    {
        bool IsAlive { get; }
        void TakeHit(HitInfo hit);
    }
}
