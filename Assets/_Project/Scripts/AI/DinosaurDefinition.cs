using UnityEngine;

namespace PrimalFrontier.AI
{
    public enum Temperament { Passive, Defensive, Territorial, Predator, AmbientFlyer, AmbientSwimmer }

    /// <summary>Per-species stats and senses. One asset per dinosaur (Data/Dinosaurs).</summary>
    [CreateAssetMenu(menuName = "Primal Frontier/Dinosaur Definition", fileName = "DINO_New")]
    public class DinosaurDefinition : ScriptableObject
    {
        public string id = "dino";
        public string displayName = "Dinosaur";
        public GameObject prefab;
        public Temperament temperament;
        [Header("Body")]
        public float maxHealth = 300f;
        public float walkSpeed = 1.6f, runSpeed = 5.5f;
        public float turnSpeed = 60f;            // deg/s at walk (halved for big animals when running)
        public float acceleration = 2.5f;        // m/s^2 (heavy animals accelerate slowly)
        public float bodyRadius = 1.2f;
        [Header("Senses")]
        public float sightRange = 40f;
        [Range(10, 360)] public float fov = 220f;
        public float hearingRange = 25f;
        [Header("Behaviour")]
        public float observeDistance = 30f;      // herbivores stop and watch the player
        public float personalSpace = 10f;        // too close: defend / flee
        public float aggroRange = 30f;           // predators start a chase
        public float territoryRadius = 45f;
        public float fleeDistance = 45f;
        [Range(0, 1)] public float retreatHealth = 0.25f;
        [Header("Attack")]
        public float attackRange = 3.5f;
        public float attackDamage = 25f, heavyDamage = 45f;
        public float attackCooldown = 2.2f;
        public float bleedSeconds;
        [Header("Herd / spawning")]
        public int groupMin = 1, groupMax = 2;
        [Header("Loot")]
        public int meat = 3, hide = 1, bone = 2;
        [Header("Audio (optional)")]
        public AudioClip[] calls; public AudioClip[] roars; public AudioClip[] hurts; public AudioClip[] deaths;
        public bool heavyFootsteps = true;
        [Header("Journal")]
        public string journalId;
        // ---- appended (hunting)
        [Header("Hunting")]
        [Tooltip("old behaviour: loot sprays as separate pickups around the body instead of a carcass to butcher")]
        public bool sprayLoot;
        [Tooltip("0 = flees in a straight line, 1 = strong zig-zag (harder to shoot)")]
        [Range(0, 1)] public float fleeZigZag;
        [Tooltip("multiplies sight and hearing range (1 = unchanged, > 1 = skittish)")]
        [Min(0)] public float alertSensitivity = 1f;
    }
}
