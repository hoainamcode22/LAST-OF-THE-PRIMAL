using UnityEngine;

namespace PrimalFrontier.AI
{
    /// <summary>
    /// Global numbers of the wildlife layer (PC phase): herds, the herd day, the migration world event, predators watching
    /// prey, tracking signs and body weight. Per species values live in DinosaurDefinition (behaviour, locomotion, tracks).
    /// One asset in Resources/WildlifeConfig (PrimalWildlifeBuilder writes it); defaults are used when it is missing.
    /// </summary>
    [CreateAssetMenu(menuName = "Primal Frontier/Wildlife Config", fileName = "WildlifeConfig")]
    public class WildlifeConfig : ScriptableObject
    {
        static WildlifeConfig _inst, _override;
        public static WildlifeConfig Instance
        {
            get
            {
                if (_override) return _override;
                if (!_inst) _inst = Resources.Load<WildlifeConfig>("WildlifeConfig");
                if (!_inst) { _inst = CreateInstance<WildlifeConfig>(); _inst.hideFlags = HideFlags.DontSave; }
                return _inst;
            }
        }
        /// <summary>tests: use these values (null = back to the asset)</summary>
        public static void Override(WildlifeConfig c) => _override = c;

        [Header("Herd shape")]
        [Tooltip("members keep this far apart at least (m, x body radius)")] public float minSpacing = 2.4f;
        [Tooltip("spread radius of the herd around its centre while grazing (m) + grazePerMember per member")] public float grazeSpread = 6f;
        public float grazePerMember = 0.9f;
        [Tooltip("spread while travelling: width x length of the loose column (m)")] public Vector2 travelSpread = new Vector2(7f, 16f);
        [Tooltip("spread at night (x graze spread; x (1 - nightFear / 2) per species)")] public float nightSpread = 0.55f;
        [Tooltip("a member re-picks its own place in the herd every this many seconds")] public Vector2 reslotSeconds = new Vector2(25f, 60f);
        [Tooltip("individual walking speed spread (x walk speed)")] public Vector2 speedSpread = new Vector2(0.86f, 1.12f);
        [Tooltip("individual idle / eat duration spread")] public Vector2 idleSpread = new Vector2(0.7f, 1.45f);
        [Tooltip("a straggler this far (x spread) behind its place trots to catch up")] public float catchUp = 1.4f;
        [Tooltip("trot speed to catch up (x walk speed)")] public float catchUpSpeed = 1.7f;
        [Tooltip("the herd centre waits when the members lag this far behind (x spread)")] public float anchorWait = 1.2f;
        [Tooltip("herd centre speed (x slowest member walk speed)")] public float travelSpeed = 0.82f;

        [Header("Herd day (TimeManager phases)")]
        [Tooltip("afternoon rest in the heat lasts this many game hours after the afternoon starts, then the herd grazes again")] public float afternoonRestHours = 1.6f;
        [Tooltip("seconds each member drinks (range); some drink twice")] public Vector2 drinkSeconds = new Vector2(9f, 16f);
        [Tooltip("fresh water further than this from the grazing place is not used for the midday drink (m)")] public float waterSearch = 220f;

        [Header("Reactions to the player (herbivores)")]
        [Tooltip("watching the player: comfort distance = observeDistance x this; closer = moves away")] public float comfortFactor = 0.62f;
        [Tooltip("seconds it watches a calm player before it goes back to feeding (range, x (1 - investigation / 3))")] public Vector2 toleranceSeconds = new Vector2(4f, 10f);
        [Tooltip("it ignores a calm player at a distance for this long before watching again (range)")] public Vector2 ignoreSeconds = new Vector2(18f, 40f);
        [Tooltip("feeding while it ignores the player: looks up every this many seconds (range)")] public Vector2 lookUpEvery = new Vector2(4f, 9f);
        [Tooltip("the player coming closer faster than this (m/s) is a threat")] public float approachSpeed = 1.1f;
        [Tooltip("alarm spreads through the herd at this speed (m/s) + a random reaction delay")] public float alarmSpeed = 22f;
        public Vector2 alarmDelay = new Vector2(0.05f, 0.45f);

        [Header("Predators and prey")]
        [Tooltip("a herd watches a predator within this range (m)")] public float predatorWatch = 38f;
        [Tooltip("a herd moves away from a predator within this range (m)")] public float predatorAvoid = 26f;
        [Tooltip("a herd flees a predator within this range, or one that runs at it (m)")] public float predatorFlee = 15f;
        [Tooltip("a calm predator wanders towards the nearest herd in reach this often (0..1 per wander)")] [Range(0, 1)] public float watchPreyChance = 0.35f;
        [Tooltip("it watches the herd from this distance (m, range)")] public Vector2 watchPreyDistance = new Vector2(30f, 42f);
        [Tooltip("seconds it watches (range)")] public Vector2 watchPreySeconds = new Vector2(10f, 22f);
        [Tooltip("chance (x aggression) that watching ends in a short rush that tests the herd (only with the player within 150 m)")] [Range(0, 1)] public float testRushChance = 0.25f;
        [Tooltip("rush length (s)")] public float testRushSeconds = 2.6f;
        [Tooltip("a predator eats at a carcass this long (s, range)")] public Vector2 carcassEatSeconds = new Vector2(25f, 45f);

        [Header("Migration (world event)")]
        [Tooltip("the migration herd sets off every this many game days (in the morning)")] [Min(1)] public int migrationEveryDays = 2;
        [Tooltip("game hours after the morning phase starts before it sets off")] public float migrationStartDelayHours = 0.25f;
        [Tooltip("walking speed on the route (x the herd travel speed)")] public float migrationSpeed = 1f;
        [Tooltip("the herd stops to drink at the ford this long (s)")] public float fordPause = 35f;
        [Tooltip("the route point where it stops to drink (index; -1 = none)")] public int fordIndex = 6;
        [Tooltip("the player within this distance of the moving herd, with a line of sight, watches it (MigrationSeen)")] public float viewDistance = 170f;
        [Tooltip("seconds of watching before MigrationSeen")] public float seenSeconds = 3f;
        [Tooltip("dust puffs per second from the moving herd (whole herd, near and mid distance)")] public float dustPerSecond = 3.5f;
        [Tooltip("dust puff size (x body radius)")] public float dustScale = 1.6f;
        [Tooltip("the low rumble of the moving herd is heard this far (m)")] public float rumbleDistance = 260f;
        [Tooltip("herd calls on the move: every this many seconds (range, whole herd)")] public Vector2 migrationCallEvery = new Vector2(5f, 11f);
        [Tooltip("calls carry this far during the migration (m)")] public float callDistance = 240f;
        [Tooltip("small birds burst out of the trees next to the moving herd: one flock every this many seconds (range)")] public Vector2 birdFlushEvery = new Vector2(7f, 14f);
        [Tooltip("trees within this distance of the herd flush birds (m)")] public float birdFlushRange = 22f;

        [Header("Herd sighting")]
        [Tooltip("the player sees a herd (HerdSighted): at least this many members in view within range")] public int sightedMembers = 3;
        public float sightedRange = 95f;

        [Header("Tracking signs")]
        [Tooltip("ground signs drawn at most this far from the camera (m)")] public float drawDistance = 48f;
        [Tooltip("most signs kept (the oldest are reused)")] public int capacity = 900;
        [Tooltip("game hours a footprint stays fully fresh; it fades out over the next fadeHours")] public float freshHours = 2f;
        public float fadeHours = 10f;
        [Tooltip("rain wears signs out this much faster (x, at full rain)")] public float rainWear = 3f;
        [Tooltip("a footprint pair every this many strides (x species stride) while walking; herds far from the player leave fewer")] public float printEveryStrides = 1f;
        [Tooltip("creatures further than this from the player leave prints every 3rd stride (m)")] public float sparsePrintDistance = 120f;
        [Tooltip("flattened plants every this many metres behind a herd on the move")] public float flattenEvery = 7f;
        [Tooltip("a herbivore leaves droppings about every this many game hours of grazing (range)")] public Vector2 droppingsHours = new Vector2(5f, 12f);
        [Tooltip("the player examines a sign from this far (m)")] public float examineRange = 2.2f;
        [Tooltip("examining a trail marks every sign of the same creature within this range as read (m)")] public float examinedRadius = 14f;
        [Tooltip("after reading a sign, the same kind of sign of the same creature is not offered again for this many game hours")] public float readForHours = 6f;
        [Tooltip("claw scratches placed on trunks in each predator territory at the start")] public int scratchesPerTerritory = 7;
        [Tooltip("old droppings placed in each herd area at the start")] public int droppingsPerHerd = 4;
        [Tooltip("old prints seeded along each creature's home at the start")] public int seedPrintsPerCreature = 14;

        [Header("Body weight")]
        [Tooltip("heavy footfalls shake the camera within this distance (m, x weight)")] public float shakeDistance = 34f;
        [Tooltip("heavy step sounds are this much louder when running")] public float runStepBoost = 1.4f;

        // ---- appended (Phase 1, AI): weather reactions, predators hunting herbivores, resting
        [Header("Weather (WeatherManager, sampled once for all creatures)")]
        [Tooltip("seconds between weather samples")] [Min(0.2f)] public float weatherCheckSeconds = 2f;
        [Tooltip("rain below this intensity changes nothing")] [Range(0, 1)] public float rainMinIntensity = 0.25f;
        [Tooltip("rain at or above this (or a storm): herds go to tree cover and rest there")] [Range(0, 1)] public float shelterIntensity = 0.6f;
        [Tooltip("heavy rain (predators patrol less, hunt rarely)")] [Range(0, 1)] public float heavyRainIntensity = 0.9f;
        [Tooltip("rain at or above this (or a storm): flyers land")] [Range(0, 1)] public float flyerLandRain = 0.92f;
        [Tooltip("light rain: the grazing place moves this far (0..1) towards the herd's tree cover")] [Range(0, 1)] public float rainGrazeTowardCover = 0.5f;
        [Tooltip("herd spread in full rain / in a storm (x, bunching)")] [Range(0.2f, 1f)] public float rainSpread = 0.75f, stormSpread = 0.5f;
        [Tooltip("loner herbivores roam this much of their radius in full rain / a storm")] [Range(0.1f, 1f)] public float rainRoamMul = 0.6f, stormRoamMul = 0.4f;
        [Tooltip("extra chance an idle animal lies down, full rain / storm")] [Range(0, 1)] public float rainRestBias = 0.35f, stormRestBias = 0.6f;
        [Tooltip("x call rate in full rain / storm (all creatures call less)")] [Range(0.05f, 1f)] public float rainCallMul = 0.4f, stormCallMul = 0.2f;
        [Tooltip("x chance to start a hunt in full rain / heavy rain (storm: never)")] [Range(0, 1)] public float rainHuntMul = 0.5f, heavyRainHuntMul = 0.25f;
        [Tooltip("x predator patrol radius / patience / prey watching in full rain, heavy rain, storm")] [Range(0.1f, 1f)] public float rainPatrolMul = 0.8f, heavyRainPatrolMul = 0.55f, stormPatrolMul = 0.35f;
        [Tooltip("flyers glide down to a perch over this many seconds")] [Min(1)] public float flyerLandSeconds = 6f;
        [Tooltip("flyers take off again this long after the weather clears (s, range)")] public Vector2 flyerTakeoffDelay = new Vector2(4f, 15f);
        [Tooltip("a startled perched flyer keeps flying at least this long (s)")] public float flyerStartleAirSeconds = 25f;

        [Header("Hunting (predators on herbivores, one hunt on the island at a time)")]
        [Tooltip("after a kill no new hunt starts for this many minutes (game clock, x 0.8..1.25)")] [Min(0)] public float huntCooldownMinutes = 12f;
        [Tooltip("after a failed hunt")] [Min(0)] public float huntFailCooldownMinutes = 6f;
        [Tooltip("after a new game or a load")] [Min(0)] public float huntFirstDelayMinutes = 8f;
        [Tooltip("a hungry predator considers a hunt every this many seconds")] [Min(0.5f)] public float huntCheckSeconds = 6f;
        [Tooltip("chance per check (x hunger, x weather)")] [Range(0, 1)] public float huntStartChance = 0.25f;
        [Tooltip("prey within this distance of the hunter (m)")] public float huntSearchRadius = 110f;
        [Tooltip("a herd with this many alive or fewer is never hunted")] [Min(1)] public int huntMinHerd = 3;
        [Tooltip("a loner's species with this many alive or fewer is never hunted")] [Min(1)] public int huntLonerMin = 2;
        [Tooltip("x kill chance when the herd is one above its minimum")] [Range(0, 1)] public float huntNearMinKillMul = 0.5f;
        [Tooltip("no hunting inside this radius around the player's start point by day (m)")] public float huntSafeStartRadius = 90f;
        [Tooltip("stalking speed (x walk speed)")] [Range(0.3f, 1.5f)] public float huntStalkSpeed = 0.75f;
        [Tooltip("gives up stalking after this many seconds")] public float huntStalkSeconds = 100f;
        [Tooltip("gives up the chase when the prey is further than this (m)")] public float huntGiveUpDistance = 55f;

        [Header("Resting")]
        [Tooltip("a lying rest (species with the Rest_Down / Rest_Loop / Rest_Up clips) lasts this long (s, range) before the usual idle time")] public Vector2 restSeconds = new Vector2(20f, 45f);
        [Tooltip("after running this long (s) an animal stops to breathe hard (Breathe clip)")] public float breatheAfterRun = 6f;
        [Tooltip("hunger a predator loses per second while eating at a carcass")] public float carcassHungerPerSecond = 0.02f;

        // ---- appended (Phase 2, AI): zones (no-go areas, heat, routines)
        /// <summary>a place creatures keep out of (WildlifeZones): destinations, wander points, routine stops, perches and placement skip it</summary>
        [System.Serializable]
        public class NoGoArea
        {
            public string id = "area";
            [Tooltip("AI anchor / Markers/Zones id / 'spawn' giving the centre (empty or missing = center below)")] public string anchor;
            public Vector3 center; [Min(0)] public float radius = 12f;
            [Tooltip("also blocks a chase (only a fleeing animal may cross)")] public bool hard;
            [Tooltip("applies only to predators")] public bool predatorsOnly;
            [Tooltip("applies to land creatures with a body radius at or above this (m, 0 = all)")] [Min(0)] public float minBody;
            [Tooltip("ambient flyers never perch inside it either")] public bool flyers = true;
        }
        [Header("Zones (Phase 2): heat and no-go areas")]
        [Tooltip("heat hazard ring (1 warm, 2 hot, 3 dangerous) from which destinations, perches and placement are refused")] [Range(1, 3)] public int heatAvoidLevel = 1;
        [Tooltip("heat ring a calm walking creature turns away from")] [Range(1, 3)] public int heatHardLevel = 2;
        [Tooltip("heat ring even a chasing creature turns away from")] [Range(1, 3)] public int heatChaseLevel = 3;
        public System.Collections.Generic.List<NoGoArea> noGoAreas = new System.Collections.Generic.List<NoGoArea>
        {
            // the Deep Water Cave (CAVE): the old grotto, the east mouth on the waterfall pool, the back door; the chamber and passages
            // lie under the cliff, so these circles are where a land creature could walk in
            new NoGoArea { id = "cave_grotto", anchor = "cave_mouth", center = new Vector3(-18f, 18.4f, -134f), radius = 13f, hard = true },
            new NoGoArea { id = "cave_east_mouth", anchor = "cave_east_mouth", center = new Vector3(90f, 19.45f, -157.4f), radius = 7f, hard = true },
            new NoGoArea { id = "cave_back_door", anchor = "cave_back_door", center = new Vector3(12.4f, 22.5f, -141.4f), radius = 8f, hard = true },
            new NoGoArea { id = "start_beach", anchor = "spawn", center = new Vector3(0f, 1.8f, 205f), radius = 55f, predatorsOnly = true, flyers = false },
        };

        [Header("Routines (Phase 2): patrols, visits, scavenging")]
        [Tooltip("a routine re-checks its creature every this many seconds")] [Min(0.2f)] public float routineTick = 1f;
        [Tooltip("a routine point counts as reached within this distance (m, + body radius)")] public float routineReach = 5f;
        [Tooltip("walking speed on a routine leg (x walk speed)")] [Range(0.4f, 1.6f)] public float routineSpeed = 1f;
        [Tooltip("gives a routine up when it could not move on for this long (s)")] public float routineStuckSeconds = 90f;
        [Tooltip("a scavenging flyer lands at a carcass anchor only by day, at most this often (s, range)")] public Vector2 scavengeEvery = new Vector2(150f, 420f);
        [Tooltip("it stays on the ground this long (s, range)")] public Vector2 scavengeStay = new Vector2(25f, 70f);
        [Tooltip("the player closer than this keeps scavengers in the air (m)")] public float scavengeShyDistance = 22f;
    }
}
