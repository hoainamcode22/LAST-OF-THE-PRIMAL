using System.Collections.Generic;
using UnityEngine;

namespace PrimalFrontier.World
{
    /// <summary>
    /// A heat / smoke danger area around the volcano crater, a lava crack or a lava stream: three nested rings (flat
    /// distance) from warm to hot to dangerous. The air gets gradually hotter towards the centre (<see cref="HeatAt"/>,
    /// added to the air temperature by SurvivalEnvironment), and <see cref="HazardZoneMonitor"/> turns the ring the player
    /// is in into a warning (GameEventType.HazardWarning with the level), the Heat / Scorching heat status effects (stamina
    /// regeneration down, sprinting costs more, thirst up; mild health loss only in the dangerous ring) and local smoke
    /// (thicker, browner fog near the source, drifting downwind). Never instantly lethal: the inner ring drains about
    /// 0.3 health per second. Placed by PrimalAtmosphereBuilder at ENV's volcano marker and lava objects; tune in the scene.
    /// </summary>
    public class HazardZone : MonoBehaviour
    {
        public enum Level { Safe = 0, Warm = 1, Hot = 2, Dangerous = 3 }

        [Tooltip("id sent with GameEventType.HazardWarning (e.g. volcano, lava)")] public string hazardId = "volcano";
        [Header("Rings (flat radius from this object, m)")]
        public float warmRadius = 160f;
        public float hotRadius = 90f;
        public float dangerRadius = 40f;
        [Tooltip("only counts between these heights relative to this object (m)")] public float minHeight = -40f, maxHeight = 260f;
        [Header("Extra air heat, deg C (0 at the warm ring's edge, smooth in between)")]
        [Tooltip("where the hot ring starts")] public float warmHeat = 6f;
        [Tooltip("where the dangerous ring starts")] public float hotHeat = 14f;
        [Tooltip("at the centre")] public float coreHeat = 26f;
        [Header("Smoke (local fog)")]
        [Range(0, 1)] public float smoke = 0.5f;
        [Tooltip("smoke reaches this far from its centre, m")] public float smokeRadius = 140f;
        [Tooltip("the smoke centre drifts this many metres downwind")] public float smokeDrift = 40f;

        public static readonly List<HazardZone> All = new List<HazardZone>();
        /// <summary>smoke at the camera 0..1, smoothed (HazardZoneMonitor); TimeManager thickens the fog with it</summary>
        public static float LocalSmoke { get; internal set; }
        /// <summary>fog colour of volcanic smoke (by day; darker at night)</summary>
        public static Color SmokeColor = new Color(0.4f, 0.36f, 0.33f);
        /// <summary>fog density at full local smoke = normal x (1 + this)</summary>
        public static float SmokeFogBoost = 5f;

        void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
            if (Application.isPlaying) HazardZoneMonitor.Ensure();
        }
        void OnDisable() { All.Remove(this); }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() { All.Clear(); LocalSmoke = 0f; }

        bool InHeight(Vector3 p) { float dy = p.y - transform.position.y; return dy >= minHeight && dy <= maxHeight; }
        float Flat(Vector3 p) { var d = p - transform.position; d.y = 0f; return d.magnitude; }

        /// <summary>extra air heat at p, deg C (0 outside the warm ring)</summary>
        public float HeatAt(Vector3 p)
        {
            if (!isActiveAndEnabled || !InHeight(p)) return 0f;
            float d = Flat(p);
            if (d >= warmRadius) return 0f;
            if (d >= hotRadius) return Ease(0f, warmHeat, Mathf.InverseLerp(warmRadius, hotRadius, d));
            if (d >= dangerRadius) return Ease(warmHeat, hotHeat, Mathf.InverseLerp(hotRadius, dangerRadius, d));
            return Ease(hotHeat, coreHeat, Mathf.InverseLerp(dangerRadius, 0f, d));
        }
        static float Ease(float a, float b, float t) { t = Mathf.Clamp01(t); return Mathf.Lerp(a, b, t * t * (3f - 2f * t)); }

        /// <summary>ring at p; slack widens the rings (hysteresis: the monitor leaves a ring only past radius x (1 + slack))</summary>
        public Level LevelAt(Vector3 p, float slack = 0f)
        {
            if (!isActiveAndEnabled || !InHeight(p)) return Level.Safe;
            float d = Flat(p), k = 1f + Mathf.Max(0f, slack);
            if (d < dangerRadius * k) return Level.Dangerous;
            if (d < hotRadius * k) return Level.Hot;
            if (d < warmRadius * k) return Level.Warm;
            return Level.Safe;
        }

        /// <summary>smoke 0..1 at p (centre drifted downwind with the weather wind)</summary>
        public float SmokeAt(Vector3 p)
        {
            if (!isActiveAndEnabled || smoke <= 0f || smokeRadius <= 0f) return 0f;
            Vector3 c = transform.position;
            var wm = Core.WeatherManager.Instance;
            if (wm) { var w = wm.WindDirection; c += new Vector3(w.x, 0f, w.y) * smokeDrift * Mathf.Clamp(wm.WindStrength, 0.2f, 1.6f); }
            var d = p - c; d.y = 0f;
            float k = 1f - Mathf.Clamp01(d.magnitude / smokeRadius);
            float rainWash = wm ? 1f - 0.5f * wm.Intensity : 1f;
            return smoke * k * k * (3f - 2f * k) * rainWash;
        }

        // ------------------------------------------------------------------ all zones
        /// <summary>hottest zone's extra heat at p (zones do not add up)</summary>
        public static float TotalHeatAt(Vector3 p)
        {
            float h = 0f;
            for (int i = 0; i < All.Count; i++) { var z = All[i]; if (z) h = Mathf.Max(h, z.HeatAt(p)); }
            return h;
        }

        /// <summary>highest ring at p over all zones, and the zone that gives it</summary>
        public static Level MaxLevelAt(Vector3 p, out HazardZone zone, float slack = 0f)
        {
            Level best = Level.Safe; zone = null;
            for (int i = 0; i < All.Count; i++)
            {
                var z = All[i]; if (!z) continue;
                var l = z.LevelAt(p, slack);
                if (l > best || (l == best && l > Level.Safe && zone && z.HeatAt(p) > zone.HeatAt(p))) { best = l; zone = z; }
            }
            return best;
        }
        public static Level MaxLevelAt(Vector3 p) => MaxLevelAt(p, out _);

        public static float TotalSmokeAt(Vector3 p)
        {
            float s = 0f;
            for (int i = 0; i < All.Count; i++) { var z = All[i]; if (z) s = Mathf.Max(s, z.SmokeAt(p)); }
            return s;
        }

        void OnDrawGizmosSelected()
        {
            var c = transform.position;
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.8f); DrawRing(c, warmRadius);
            Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.8f); DrawRing(c, hotRadius);
            Gizmos.color = new Color(1f, 0.15f, 0.05f, 0.9f); DrawRing(c, dangerRadius);
        }
        static void DrawRing(Vector3 c, float r)
        {
            Vector3 prev = c + new Vector3(r, 0f, 0f);
            for (int i = 1; i <= 48; i++) { float a = i / 48f * Mathf.PI * 2f; var q = c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r); Gizmos.DrawLine(prev, q); prev = q; }
        }
    }
}
