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
    /// </summary>
    public class ZoneManager : MonoBehaviour
    {
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
        public static ZoneManager Instance { get; private set; }
        public Zone Current { get; private set; }
        readonly HashSet<string> _inside = new HashSet<string>();
        readonly HashSet<string> _visited = new HashSet<string>();
        public IEnumerable<string> Visited => _visited;
        float _next;

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (Time.time < _next) return; _next = Time.time + 0.4f;
            var pp = PlayerLocator.Position; if (!pp.HasValue) return;
            var p = pp.Value; Zone best = null; float bestR = float.MaxValue;
            foreach (var z in zones)
            {
                var d = p - z.center; d.y = 0;
                bool inside = d.sqrMagnitude <= z.radius * z.radius;
                if (inside && z.radius < bestR) { best = z; bestR = z.radius; }
                if (inside && _inside.Add(z.id))
                {
                    bool first = _visited.Add(z.id);
                    GameEvents.Raise(GameEventType.ZoneEntered, z.id, first ? 1 : 0, p);
                }
                else if (!inside) _inside.Remove(z.id);
            }
            Current = best;
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
            foreach (var z in zones) { if (!z.indoor) continue; var d = p - z.center; d.y = 0; if (d.sqrMagnitude <= z.radius * z.radius) return true; }
            return false;
        }

        /// <summary>the smallest zone at p with a climate (offset, humidity or stable air) and its edge weight 0..1</summary>
        Zone ClimateZone(Vector3 p, out float weight, bool humidityOnly = false)
        {
            Zone best = null; float bestR = float.MaxValue; weight = 0f;
            for (int i = 0; i < zones.Count; i++)
            {
                var z = zones[i];
                bool has = humidityOnly ? z.humidity > 0f : (z.temperatureOffset != 0f || (z.separateNight && z.nightTemperatureOffset != 0f) || z.stableAir);
                if (!has || z.radius >= bestR) continue;
                var d = p - z.center; d.y = 0;
                float r = z.radius; float d2 = d.sqrMagnitude;
                if (d2 > r * r) continue;
                best = z; bestR = r;
                float edge = Mathf.Max(0.5f, r * climateEdge);
                weight = Mathf.Clamp01((r - Mathf.Sqrt(d2)) / edge);
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

        public void SetVisited(IEnumerable<string> ids) { _visited.Clear(); foreach (var i in ids) _visited.Add(i); _inside.Clear(); }
    }
}
