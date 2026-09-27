using UnityEngine;

namespace PrimalFrontier.Animation
{
    public enum HitZoneType { Body, Head, Legs, Tail, Neck }

    /// <summary>Lightweight damage zone on a child primitive collider (head/body/legs/tail). No MeshColliders.</summary>
    [RequireComponent(typeof(Collider))]
    public class HitZone : MonoBehaviour
    {
        [SerializeField] private HitZoneType zone = HitZoneType.Body;
        [SerializeField, Range(0f, 5f)] private float damageMultiplier = 1f;

        public HitZoneType Zone => zone;
        public float DamageMultiplier => damageMultiplier;

        public void Configure(HitZoneType z, float multiplier)
        {
            zone = z; damageMultiplier = multiplier;
        }
    }
}
