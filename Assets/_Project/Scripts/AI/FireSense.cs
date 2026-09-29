using UnityEngine;
using PrimalFrontier.Core;
using PrimalFrontier.World;

namespace PrimalFrontier.AI
{
    /// <summary>
    /// What wildlife reads from campfires (read only). The one place that talks to the Campfire API, so SURV's read API
    /// (Fuel01 / Intensity01 / State / Sheltered) swaps in here alone. Allocation free (Campfire.All is a List).
    /// </summary>
    public static class FireSense
    {
        /// <summary>fuel seconds that count as a full-strength fire (interim until Campfire.Intensity01 exists)</summary>
        public const float FullFuelSeconds = 300f;

        /// <summary>0 (out) .. 1 (strong)</summary>
        public static float Intensity01(Campfire c)
        {
            if (!c || !c.IsLit) return 0f;
            return Mathf.Clamp01(c.Fuel / Mathf.Min(FullFuelSeconds, c.MaxFuel));
        }

        public static bool Sheltered(Campfire c) => c && Shelter.Covers(c.transform.position + Vector3.up * 0.5f);
        public static bool RainedOn(Campfire c) { var w = WeatherManager.Instance; return c && w && w.RainingAt(c.transform.position + Vector3.up * 0.5f); }

        /// <summary>fear radius of one species around one lit fire (0 = no fear); night 0..1</summary>
        public static float FearRadius(in FireFearProfile f, Campfire c, float night)
        {
            if (!c || !c.IsLit || f.predatorFear <= 0f) return 0f;
            float intensity = Intensity01(c);
            if (intensity < f.ignoreBelowIntensity) return 0f;
            float r = Mathf.Lerp(f.fearRadiusDay, f.fearRadiusNight, Mathf.Clamp01(night));
            if (RainedOn(c)) r *= f.rainMultiplier;
            r *= Mathf.Lerp(1f, intensity, Mathf.Clamp01(f.fuelMultiplier));
            return Mathf.Max(0f, r);
        }

        /// <summary>current night factor (0 day .. 1 night)</summary>
        public static float Night { get { var t = TimeManager.Instance; return t ? t.NightFactor : 0f; } }

        /// <summary>the lit fire whose fear circle for this species contains p (the deepest one), null = none</summary>
        public static Campfire FearAt(DinosaurDefinition def, Vector3 p, out float radius, float margin = 0f)
        {
            radius = 0f; Campfire best = null; float bestDepth = 0f;
            if (!def || def.fireFear.predatorFear <= 0f) return null;
            float night = Night; var all = Campfire.All;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i]; if (!c || !c.IsLit) continue;
                float r = FearRadius(def.fireFear, c, night); if (r <= 0f) continue;
                Vector3 d = p - c.transform.position; d.y = 0f;
                float depth = r + margin - d.magnitude;
                if (depth > bestDepth) { bestDepth = depth; best = c; radius = r; }
            }
            return best;
        }

        /// <summary>firelight on the player at p (0 = none): strongest lit fire within the light radius</summary>
        public static float LightAt(Vector3 p, PerceptionConfig cfg)
        {
            float best = 0f; var all = Campfire.All; float r = Mathf.Max(0.1f, cfg.fireLightRadius);
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i]; if (!c || !c.IsLit) continue;
                Vector3 d = p - c.transform.position; d.y = 0f; float m = d.magnitude; if (m >= r) continue;
                float l = (Sheltered(c) ? cfg.shelteredFireLight : cfg.fireLight) * (1f - m / r) * Mathf.Lerp(0.5f, 1f, Intensity01(c));
                if (l > best) best = l;
            }
            return best;
        }
    }
}
