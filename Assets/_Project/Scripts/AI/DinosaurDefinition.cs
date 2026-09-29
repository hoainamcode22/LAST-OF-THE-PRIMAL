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

        // ---- appended (perception, Phase 3): values per species from PrimalPerceptionBuilder (AI/PERCEPTION_DESIGN.md 11.2)
        [Header("Perception")]
        [Tooltip("0 = sight follows the light fully, 1 = sees as well at night")] [Range(0, 1)] public float nightVision = 0.2f;
        [Tooltip("awareness rate from sight (range = sightRange)")] [Min(0)] public float sightGain = 1f;
        [Tooltip("awareness bump from noise (range = hearingRange x loudness)")] [Min(0)] public float hearingGain = 1f;
        [Tooltip("multiplies smelled concentration")] [Min(0)] public float smellSensitivity = 0.6f;
        [Tooltip("awareness lost per second after the hold")] [Min(0.01f)] public float awarenessDecay = 0.08f;
        [Tooltip("seconds the last known position stays useful")] [Min(0)] public float memorySeconds = 12f;
        [Tooltip("alerts spread to the same species within this range (0 = solitary)")] [Min(0)] public float herdShareRadius = 30f;
        [Tooltip("herbivore call that also warns other herbivores")] public bool alarmCall;
        [Tooltip("interest in meat / blood scent (0 = ignores it)")] [Range(0, 1)] public float meatDrive;
        [Tooltip("while hunting, follows the player by smell within this range after losing sight (0 = never)")] [Min(0)] public float scentTrackRange;
        [Header("Campfire fear")]
        public FireFearProfile fireFear = FireFearProfile.Default;
        [Header("Daily life")]
        public ActivityCycle activity = ActivityCycle.Diurnal;
        [Tooltip("game hours between drinks (0 = never goes to water)")] [Min(0)] public float drinkEveryHours = 10f;
        [Tooltip("looks for fresh water this far from home (m)")] [Min(0)] public float drinkSearchRadius = 160f;
        [Header("Bare-hand hits")]
        [Tooltip("multiplies unarmed damage (1 = full, 0.05 = barely notices); negative = from body size (PerceptionConfig.unarmedFullBody)")]
        public float unarmedDamageScale = -1f;
    }

    /// <summary>when a species is awake</summary>
    public enum ActivityCycle { Diurnal, Nocturnal, Cathemeral }

    /// <summary>what a creature does when a lit campfire is in its way</summary>
    public enum FireResponse { Avoid, Observe, Circle, Wait, Leave }

    /// <summary>
    /// Campfire fear of one species (owner decision: configurable, not a universal safe zone). The fear radius is
    /// lerp(day, night, NightFactor) x (rain on the fire ? rainMultiplier : 1) x lerp(1, fire intensity, fuelMultiplier).
    /// predatorFear 0 ignores fires; below 0.5 the creature only hesitates (patience x (1 - fear)) and then walks in;
    /// 0.5 and above it stays outside (its response) until its patience runs out, then leaves.
    /// </summary>
    [System.Serializable]
    public struct FireFearProfile
    {
        [Tooltip("0 = ignores fire, 1 = never goes inside the radius")] [Range(0, 1)] public float predatorFear;
        [Tooltip("fear radius by day (m)")] [Min(0)] public float fearRadiusDay;
        [Tooltip("fear radius at night (m)")] [Min(0)] public float fearRadiusNight;
        [Tooltip("radius multiplier while rain falls on an unsheltered fire")] [Min(0)] public float rainMultiplier;
        [Tooltip("0 = fuel does not matter, 1 = radius follows the fire intensity (Fuel01) fully")] [Range(0, 1)] public float fuelMultiplier;
        [Tooltip("what it does at the edge")] public FireResponse response;
        [Tooltip("seconds it waits / circles / watches at the edge before leaving")] [Min(0)] public float patienceSeconds;
        [Tooltip("a fire weaker than this intensity is ignored (apex)")] [Range(0, 1)] public float ignoreBelowIntensity;
        [Tooltip("being hit makes it ignore the fire for a while")] public bool ignoreWhenProvoked;

        public static FireFearProfile Default => new FireFearProfile
        {
            predatorFear = 0.6f, fearRadiusDay = 5f, fearRadiusNight = 8f, rainMultiplier = 0.7f, fuelMultiplier = 0.5f,
            response = FireResponse.Wait, patienceSeconds = 20f, ignoreBelowIntensity = 0f, ignoreWhenProvoked = true,
        };
    }
}
