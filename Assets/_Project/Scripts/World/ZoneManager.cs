using System;
using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Core;

namespace PrimalFrontier.World
{
    /// <summary>
    /// Named areas of the island as simple circles: the old Blender markers (ZONE_*, HAB_*) and the PC-phase locations
    /// ENV marks under Markers/Zones/&lt;id&gt; (beach, forest, deep_forest, river, waterfall, meadow, canyon, wetland, cave, ridge,
    /// volcano, ...; registered by PrimalAtmosphereBuilder with <see cref="LocationDefaults"/>). Raises ZoneEntered (id,
    /// amount 1 on the first visit) when the player walks in. Per zone: indoor (cave: no rain, muffled sound), air
    /// temperature offset by day and by night (canyon / deep forest shade cooler by day, wetland cool, volcano warm),
    /// humidity, and a stable air temperature for caves (a cave is cooler than a hot afternoon and warmer than a cold
    /// night). Where zones overlap the smallest one decides the climate, with a soft edge (no step at the border).
    /// Phase 2 (WORLD, PrimalAtmosphereBuilder.Zones2): <see cref="ZoneKind.Region"/> zones are the six explored environments
    /// (Migration Valley, Prehistoric Wetland, Bone Valley, Giant Fern Forest, Volcanic Foothills, Deep Water Cave): a circle or
    /// a capsule (<see cref="Zone.segment"/>) with a blend band that atmosphere, fog and sound read as a 0..1 weight
    /// (<see cref="WeightOf"/>), announced by the zone toast on entry. <see cref="ZoneKind.Landmark"/> zones are landmarks and
    /// hidden spots: they count as visited (ZoneEntered, amount 1) when the player walks in or, with a sight range, sees them
    /// (in view and not hidden behind terrain), which unlocks their journal page and marks them on the map. A zone can have a
    /// height band (a cave under a cliff: indoor only below the cliff top). A zone is left only a little past its edge.
    /// </summary>
    public class ZoneManager : MonoBehaviour
    {
        /// <summary>Place = the older named places (0: every zone saved before Phase 2), Region = a Phase 2 environment, Landmark = a landmark / hidden spot</summary>
        public enum ZoneKind { Place = 0, Region = 1, Landmark = 2 }

        [Serializable]
        public class Zone
        {
            public string id; public string displayName; public Vector3 center; public float radius = 20f; public bool indoor;
            [Tooltip("air offset, deg C (by day when separateNight is on)")] public float temperatureOffset;
            [Tooltip("use nightTemperatureOffset at night (blended through dusk / dawn)")] public bool separateNight;
            public float nightTemperatureOffset;
            [Tooltip("0 = normal, 1 = saturated air (wetland, waterfall spray)")] [Range(0, 1)] public float humidity;
            [Tooltip("cave air: the air is pulled towards stableAirTemperature (stableAirWeight 0..1)")] public bool stableAir;
            public float stableAirTemperature = 15f;
            [Range(0, 1)] public float stableAirWeight = 0.8f;
            [Header("Phase 2")]
            public ZoneKind kind;
            [Tooltip("capsule: the zone runs from center - segment to center + segment (flat); zero = a circle")] public Vector3 segment;
            [Tooltip("metres past the radius over which the zone's atmosphere / sound weight fades to 0")] public float blend;
            [Tooltip("the zone toast shows the display name on entry")] public bool announce;
            [Tooltip("landmark: counts as found once seen from this far (m, in view, not behind terrain); 0 = only by walking in")] public float sightRange;
            [Tooltip("landmark: the point looked at is this high above the centre (m)")] public float sightHeight = 3f;
            [Tooltip("landmark: a line of sight that ends this close to the point still sees it (its own collider)")] public float sightSize = 4f;
            [Tooltip("only between minY and maxY (world height) does the zone count (a cave below a cliff)")] public bool heightBand;
            public float minY = -1000f, maxY = 1000f;
            [Tooltip("a cave: the zone is only the inside of these capsules (passages / rooms with their own floor and height); empty = the circle / capsule above")]
            public Part[] parts = new Part[0];

            /// <summary>one passage segment or room of a part-built zone: flat capsule a -> b with half widths ra / rb, floor height a.y -> b.y, room height h</summary>
            [Serializable]
            public struct Part
            {
                public Vector3 a, b; public float ra, rb, height;
                public Part(Vector3 a, Vector3 b, float ra, float rb, float height) { this.a = a; this.b = b; this.ra = ra; this.rb = rb; this.height = height; }
            }

            bool HasParts => parts != null && parts.Length > 0;

            /// <summary>part-built zone: how far p is outside the nearest part whose floor / ceiling holds p (negative = inside); large when none does</summary>
            float PartGap(Vector3 p)
            {
                float best = 1e6f;
                for (int i = 0; i < parts.Length; i++)
                {
                    var q = parts[i];
                    float abx = q.b.x - q.a.x, abz = q.b.z - q.a.z, l2 = abx * abx + abz * abz;
                    float t = l2 < 1e-4f ? 0f : Mathf.Clamp01(((p.x - q.a.x) * abx + (p.z - q.a.z) * abz) / l2);
                    float floor = Mathf.Lerp(q.a.y, q.b.y, t);
                    if (p.y < floor - 1.5f || p.y > floor + q.height + 0.5f) continue;
                    float dx = q.a.x + abx * t - p.x, dz = q.a.z + abz * t - p.z;
                    float gap = Mathf.Sqrt(dx * dx + dz * dz) - Mathf.Lerp(q.ra, q.rb, t);
                    if (gap < best) best = gap;
                }
                return best;
            }

            /// <summary>flat distance from p to the zone's centre (or its centre line)</summary>
            public float Distance(Vector3 p)
            {
                if (HasParts) return radius + PartGap(p);
                float x = p.x - center.x, z = p.z - center.z;
                if (segment.x != 0f || segment.z != 0f)
                {
                    float sx = segment.x, sz = segment.z, l2 = sx * sx + sz * sz;
                    float t = Mathf.Clamp((x * sx + z * sz) / l2, -1f, 1f);
                    x -= sx * t; z -= sz * t;
                }
                return Mathf.Sqrt(x * x + z * z);
            }
            public bool InBand(Vector3 p) => !heightBand || (p.y >= minY && p.y <= maxY);
            public bool Contains(Vector3 p, float margin = 0f) => InBand(p) && Distance(p) <= radius + margin;
            /// <summary>1 inside the radius, smoothstep down to 0 at radius + blend (0 outside the height band)</summary>
            public float Weight(Vector3 p)
            {
                if (!InBand(p)) return 0f;
                float d = Distance(p);
                if (d <= radius) return 1f;
                if (blend <= 0.01f || d >= radius + blend) return 0f;
                float t = 1f - (d - radius) / blend;
                return t * t * (3f - 2f * t);
            }
        }

        /// <summary>design values for a PC-phase location id (ENV markers)</summary>
        public struct LocationInfo
        {
            public string id, displayName; public float radius, dayOffset, nightOffset, humidity; public bool indoor, stable;
            public LocationInfo(string id, string name, float radius, float day, float night, float humidity, bool indoor = false, bool stable = false)
            { this.id = id; displayName = name; this.radius = radius; dayOffset = day; nightOffset = night; this.humidity = humidity; this.indoor = indoor; this.stable = stable; }
        }

        /// <summary>
        /// PC-phase location ids (Documentation/PCPhase/PC_ROUND_OWNERSHIP.md) with their climate. Radius is only the fallback
        /// when neither LOCATIONS.md nor the marker gives one. Offsets in deg C (day, night).
        /// </summary>
        public static readonly LocationInfo[] LocationDefaults =
        {
            new LocationInfo("beach", "The Beach", 45f, 0f, 0.5f, 0.6f),
            new LocationInfo("shipwreck", "The Shipwreck", 20f, 0f, 0.5f, 0.6f),
            new LocationInfo("forest", "The Forest", 70f, -1f, 0.5f, 0.6f),
            new LocationInfo("deep_forest", "Deep Forest", 55f, -2.5f, 1f, 0.8f),
            new LocationInfo("river", "The River", 30f, -1f, -1f, 0.75f),
            new LocationInfo("waterfall", "The Waterfall", 25f, -2f, -1.5f, 0.95f),
            new LocationInfo("meadow", "The Meadow", 70f, 0.5f, -0.5f, 0.5f),
            new LocationInfo("canyon", "The Canyon", 40f, -2f, -1f, 0.55f),
            new LocationInfo("wetland", "The Wetland", 45f, -1.5f, -0.5f, 0.95f),
            new LocationInfo("cave", "The Cave", 14f, 0f, 0f, 0.8f, true, true),
            new LocationInfo("ridge", "Rocky Ridge", 50f, -1f, -1.5f, 0.4f),
            new LocationInfo("volcano", "The Volcano", 120f, 3f, 3f, 0.3f),
            new LocationInfo("predator_territory", "Predator Territory", 50f, 0f, 0f, 0.6f),
            new LocationInfo("herbivore_valley", "Herbivore Valley", 60f, 0f, 0f, 0.6f),
            new LocationInfo("old_camp", "The Old Camp", 15f, 0f, 0f, 0.6f),
            new LocationInfo("fossil_bed", "Fossil Bed", 18f, 0f, 0f, 0.5f),
            new LocationInfo("nest", "The Nest", 15f, 0f, 0f, 0.6f),
            new LocationInfo("migration_view", "High Ground", 20f, -0.5f, -1f, 0.45f),
        };
        public static LocationInfo? Location(string id)
        {
            foreach (var l in LocationDefaults) if (l.id == id) return l;
            return null;
        }

        public List<Zone> zones = new List<Zone>();
        [Tooltip("part of a zone's radius over which its climate fades in at the border")] [Range(0.01f, 0.6f)] public float climateEdge = 0.2f;
        [Tooltip("a zone is left only this far past its edge (m): no enter / leave flicker along a border")] [Min(0)] public float exitMargin = 3f;
        public static ZoneManager Instance { get; private set; }
        public Zone Current { get; private set; }
        readonly HashSet<string> _inside = new HashSet<string>();
        readonly HashSet<string> _visited = new HashSet<string>();
        public IEnumerable<string> Visited => _visited;
        public bool WasVisited(string id) => id != null && _visited.Contains(id);
        readonly Dictionary<string, int> _sightTicks = new Dictionary<string, int>(StringComparer.Ordinal);
        float _next;

        void Awake() { Instance = this; }
        void Start() { if (Application.isPlaying) UI.ZoneToast.Ensure(); }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (Time.time < _next) return; _next = Time.time + 0.4f;
            var pp = PlayerLocator.Position; if (!pp.HasValue) return;
            var p = pp.Value; Zone best = null; float bestR = float.MaxValue;
            foreach (var z in zones)
            {
                bool was = _inside.Contains(z.id);
                bool inside = z.Contains(p, was ? exitMargin : 0f);
                if (inside && z.kind != ZoneKind.Landmark && z.Distance(p) <= z.radius && z.radius < bestR) { best = z; bestR = z.radius; }
                if (inside && !was)
                {
                    _inside.Add(z.id);
                    bool first = _visited.Add(z.id);
                    GameEvents.Raise(GameEventType.ZoneEntered, z.id, first ? 1 : 0, p);
                }
                else if (!inside && was) _inside.Remove(z.id);
            }
            Current = best;
            Sight();
        }

        /// <summary>landmarks with a sight range: found once in view (inner 80 % of the screen) and not hidden behind terrain, two ticks in a row</summary>
        void Sight()
        {
            var cam = Camera.main; if (!cam) return;
            var eye = cam.transform.position;
            for (int i = 0; i < zones.Count; i++)
            {
                var z = zones[i];
                if (z.kind != ZoneKind.Landmark || z.sightRange <= 0f || _visited.Contains(z.id)) continue;
                var target = z.center + Vector3.up * z.sightHeight;
                var d = target - eye; float dist = d.magnitude;
                bool seen = false;
                if (dist <= z.sightRange && dist > 0.5f)
                {
                    var v = cam.WorldToViewportPoint(target);
                    if (v.z > 0f && v.x > 0.1f && v.x < 0.9f && v.y > 0.1f && v.y < 0.9f)
                    {
                        // start past the player (third-person camera behind the survivor): only the land and big objects hide it
                        var from = eye + d * (Mathf.Min(6f, dist * 0.5f) / dist);
                        seen = !Physics.Linecast(from, target, out var hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) || (hit.point - target).sqrMagnitude <= z.sightSize * z.sightSize;
                    }
                }
                _sightTicks.TryGetValue(z.id, out int n);
                n = seen ? n + 1 : 0; _sightTicks[z.id] = n;
                if (n < 2) continue;
                _visited.Add(z.id);
                GameEvents.Raise(GameEventType.ZoneEntered, z.id, 1, eye);
            }
        }

        public Zone Find(string id) => zones.Find(z => z.id == id);

        /// <summary>add or update a zone by id (builder / story); returns it</summary>
        public Zone Register(string id, string displayName, Vector3 center, float radius, bool indoor = false, float temperatureOffset = 0f)
        {
            var z = Find(id);
            if (z == null) { z = new Zone { id = id }; zones.Add(z); }
            z.displayName = displayName; z.center = center; z.radius = Mathf.Max(1f, radius); z.indoor = indoor; z.temperatureOffset = temperatureOffset;
            return z;
        }

        public bool IsIndoor(Vector3 p)
        {
            foreach (var z in zones) { if (!z.indoor) continue; if (z.Contains(p)) return true; }
            return false;
        }

        /// <summary>0..1 weight of zone id at p (1 inside, fading over its blend band); 0 when there is no such zone</summary>
        public float WeightOf(string id, Vector3 p) { var z = Find(id); return z == null ? 0f : z.Weight(p); }

        /// <summary>the smallest zone at p with a climate (offset, humidity or stable air) and its edge weight 0..1</summary>
        Zone ClimateZone(Vector3 p, out float weight, bool humidityOnly = false)
        {
            Zone best = null; float bestR = float.MaxValue; weight = 0f;
            for (int i = 0; i < zones.Count; i++)
            {
                var z = zones[i];
                bool has = humidityOnly ? z.humidity > 0f : (z.temperatureOffset != 0f || (z.separateNight && z.nightTemperatureOffset != 0f) || z.stableAir);
                if (!has || z.radius >= bestR || !z.InBand(p)) continue;
                float r = z.radius; float dist = z.Distance(p);
                if (dist > r) continue;
                best = z; bestR = r;
                float edge = Mathf.Max(0.5f, r * climateEdge);
                weight = Mathf.Clamp01((r - dist) / edge);
                weight = weight * weight * (3f - 2f * weight);
            }
            return best;
        }

        /// <summary>air offset of the zone climate at p (deg C); night01 0 = day .. 1 = night (default: now)</summary>
        public float TemperatureOffset(Vector3 p, float night01 = -1f)
        {
            var z = ClimateZone(p, out float w); if (z == null) return 0f;
            if (night01 < 0f) night01 = NightNow();
            float o = z.separateNight ? Mathf.Lerp(z.temperatureOffset, z.nightTemperatureOffset, night01) : z.temperatureOffset;
            return o * w;
        }

        /// <summary>air at p after the zone climate: offsets, and cave air pulled towards its stable temperature</summary>
        public float AdjustAir(Vector3 p, float air, float night01 = -1f)
        {
            var z = ClimateZone(p, out float w); if (z == null) return air;
            if (night01 < 0f) night01 = NightNow();
            float o = z.separateNight ? Mathf.Lerp(z.temperatureOffset, z.nightTemperatureOffset, night01) : z.temperatureOffset;
            air += o * w;
            if (z.stableAir) air = Mathf.Lerp(air, z.stableAirTemperature, Mathf.Clamp01(z.stableAirWeight) * w);
            return air;
        }

        /// <summary>0..1 humidity at p (0 outside every humid zone)</summary>
        public float HumidityAt(Vector3 p)
        {
            var z = ClimateZone(p, out float w, true);
            return z == null ? 0f : z.humidity * w;
        }

        static float NightNow()
        {
            var tm = TimeManager.Instance; if (!tm) return 0f;
            return 1f - Mathf.Clamp01(tm.Daylight01 * 1.5f);
        }

        public void SetVisited(IEnumerable<string> ids) { _visited.Clear(); foreach (var i in ids) _visited.Add(i); _inside.Clear(); _sightTicks.Clear(); }
    }
}
