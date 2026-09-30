using UnityEngine;

namespace PrimalFrontier.Core
{
    /// <summary>
    /// Every weather tuning number in one asset (Resources/WeatherConfig, made by PrimalAtmosphereBuilder; a default
    /// instance with the values below is used when the asset is missing). Times in in-game hours unless the name says
    /// seconds (one in-game hour = TimeManager.secondsPerHour real seconds, 90 by default).
    /// Random schedule, rolled once per in-game hour: Clear -> (cloudChancePerHour) Cloudy -> (rainChancePerHour) Rain
    /// -> Cloudy -> Clear. A rain is light, normal or heavy (heavy rains start normal and turn heavy part way, so only a
    /// long one puts an open fire out). A few rains become a storm: stormChance, never before firstStormAfterHours of a
    /// new game and never within minHoursBetweenStorms of the last storm (no storms back to back); a storm is short:
    /// heavy rain lead-in, the storm itself (stormHours), then a normal rain tail.
    /// </summary>
    [CreateAssetMenu(menuName = "Primal Frontier/Weather Config", fileName = "WeatherConfig")]
    public class WeatherConfig : ScriptableObject
    {
        [Header("Schedule (rolled once per in-game hour)")]
        [Tooltip("chance per hour that clouds come in on a clear day")] [Range(0, 1)] public float cloudChancePerHour = 0.12f;
        [Tooltip("chance per hour that a cloudy sky starts to rain")] [Range(0, 1)] public float rainChancePerHour = 0.25f;
        [Tooltip("how long a cloudy spell lasts when no rain comes")] public Vector2 cloudHours = new Vector2(1.5f, 4f);
        [Tooltip("cloudy hours after a rain before the sky clears")] public Vector2 afterRainCloudHours = new Vector2(0.5f, 1.5f);
        [Tooltip("light / normal rain duration")] public Vector2 rainHours = new Vector2(1f, 3f);
        [Tooltip("heavy rain duration (turns heavy after heavyRainBuild of it)")] public Vector2 heavyRainHours = new Vector2(0.7f, 1.5f);
        [Tooltip("share of rains that are light")] [Range(0, 1)] public float lightRainShare = 0.4f;
        [Tooltip("share of rains that are heavy (the rest are normal)")] [Range(0, 1)] public float heavyRainShare = 0.2f;
        [Tooltip("part of a heavy rain's time that is still normal rain before it turns heavy")] [Range(0, 0.9f)] public float heavyRainBuild = 0.35f;

        [Header("Storms (rare, short, never back to back)")]
        [Tooltip("chance that a rain which starts becomes a storm (when storms are allowed)")] [Range(0, 1)] public float stormChance = 0.18f;
        [Tooltip("no random storm in the first hours of a new game (the opening shipwreck storm is scripted)")] public float firstStormAfterHours = 20f;
        [Tooltip("hours after a storm ends before another one may start")] public float minHoursBetweenStorms = 30f;
        [Tooltip("heavy rain before the storm breaks")] public Vector2 stormLeadHours = new Vector2(0.2f, 0.4f);
        [Tooltip("the storm itself")] public Vector2 stormHours = new Vector2(0.45f, 1.1f);
        [Tooltip("normal rain after the storm")] public Vector2 stormTailHours = new Vector2(0.4f, 1f);

        [Header("Rain intensity (0..1; campfires count 0.9 and up as heavy rain)")]
        [Range(0, 1)] public float lightRain = 0.5f;
        [Range(0, 1)] public float normalRain = 0.75f;
        [Range(0, 1)] public float heavyRain = 0.94f;
        [Range(0, 1)] public float stormRain = 1f;
        [Tooltip("slow natural swell of a light / normal rain (+- this much)")] [Range(0, 0.2f)] public float rainSwell = 0.06f;
        [Tooltip("real seconds for the rain to go from nothing to full")] public float rainRampSeconds = 12f;
        [Tooltip("real seconds for the storm part (darkness, wind, lightning) to come in / leave")] public float stormRampSeconds = 10f;

        [Header("Cloud cover (0..1)")]
        [Range(0, 1)] public float cloudyCover = 0.5f;
        [Range(0, 1)] public float rainCover = 0.8f;
        [Range(0, 1)] public float stormCover = 1f;
        [Tooltip("real seconds for the clouds to come in fully")] public float cloudRampSeconds = 20f;

        [Header("Wet surfaces (_PF_Wetness 0..1)")]
        [Tooltip("real seconds of full rain to soak dry surfaces")] public float soakSeconds = 50f;
        [Tooltip("surfaces stay fully wet this many real seconds after the rain stops, then start to dry")] public float dryDelaySeconds = 45f;
        [Tooltip("real seconds to dry from soaked under a clear sky by day")] public float drySecondsClear = 240f;
        [Tooltip("real seconds to dry from soaked under full cloud")] public float drySecondsOvercast = 600f;
        [Tooltip("drying is this many times slower at night")] public float nightDrySlowdown = 1.6f;
        [Tooltip("wetness a light rain reaches (normal rain and up soak fully)")] [Range(0, 1)] public float lightRainWetness = 0.8f;

        [Header("Visibility (x fog density)")]
        public float fogCloudy = 1.25f;
        [Tooltip("at rain intensity 1")] public float fogRain = 1.9f;
        [Tooltip("on top of the rain, at full storm")] public float fogStorm = 1.7f;
        [Tooltip("fog / sky drift to rain grey (0..1)")] [Range(0, 1)] public float greyCloudy = 0.25f, greyRain = 0.7f, greyStorm = 0.9f;

        [Header("Darkness (x sun / ambient light)")]
        [Tooltip("at rain intensity 1 (clouds already dim the sun in TimeManager)")] [Range(0, 1)] public float rainSun = 0.8f;
        [Range(0, 1)] public float rainAmbient = 0.88f;
        [Tooltip("on top of the rain, at full storm")] [Range(0, 1)] public float stormSun = 0.4f;
        [Range(0, 1)] public float stormAmbient = 0.62f;

        [Header("Wind (strength as _PF_Wind.z; AI scent reads it too)")]
        public float calmWind = 0.35f;
        public float cloudyWind = 0.55f;
        [Tooltip("at rain intensity 1")] public float rainWind = 0.9f;
        public float stormWind = 1.4f;
        [Tooltip("storm gusts add up to this much strength")] public float stormGust = 0.45f;
        [Tooltip("real seconds between gust changes (calm / storm)")] public Vector2 gustSeconds = new Vector2(3f, 9f), stormGustSeconds = new Vector2(1.2f, 3.5f);

        [Header("Lightning and thunder (storms)")]
        [Tooltip("real seconds between strikes")] public Vector2 lightningSeconds = new Vector2(6f, 16f);
        [Tooltip("share of strikes that are close")] [Range(0, 1)] public float nearStrikeShare = 0.25f;
        [Tooltip("close / far strike distance, m (thunder comes distance / 343 s later)")] public Vector2 nearStrikeMeters = new Vector2(250f, 700f), farStrikeMeters = new Vector2(1200f, 3800f);
        [Tooltip("flash light intensity of a close strike (far ones are dimmer)")] public float lightningIntensity = 2.6f;
        public Color lightningColor = new Color(0.8f, 0.86f, 1f);

        [Header("Rain particles")]
        [Tooltip("drops per second at full intensity (x maxParticles of the manager)")] public float dropsPerIntensity = 1.3f;
        [Tooltip("how much the wind slants the rain (m/s per unit of wind strength)")] public float rainWindSlant = 3f;

        // ------------------------------------------------------------------ access
        static WeatherConfig _instance;
        /// <summary>Resources/WeatherConfig, or a default instance with the values above</summary>
        public static WeatherConfig Instance
        {
            get
            {
                if (_instance) return _instance;
                _instance = Resources.Load<WeatherConfig>("WeatherConfig");
                if (!_instance) { _instance = CreateInstance<WeatherConfig>(); _instance.name = "WeatherConfig (default)"; _instance.hideFlags = HideFlags.DontSave; }
                return _instance;
            }
        }
        /// <summary>tests / tools: force a config (null = reload from Resources)</summary>
        public static void Override(WeatherConfig c) { _instance = c; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() => _instance = null;

        public static float Range(Vector2 r, System.Func<float> rnd) => Mathf.Lerp(Mathf.Min(r.x, r.y), Mathf.Max(r.x, r.y), rnd());
    }
}
