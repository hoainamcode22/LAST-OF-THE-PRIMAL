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
    }

    /// <summary>Creatures (and later breakables) the player can hit. HitZone colliders forward to the IDamageable in a parent.</summary>
    public interface IDamageable
    {
        bool IsAlive { get; }
        void TakeHit(HitInfo hit);
    }
}
