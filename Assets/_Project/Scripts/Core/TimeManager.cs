using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace PrimalFrontier.Core
{
    /// <summary>
    /// Day / night: sun path, light colour and intensity, trilight ambient, fog, sky exposure, and the named parts of the
    /// day (<see cref="Phase"/>: Dawn, Morning, Noon, Afternoon, Dusk, Night; <see cref="CurrentPhase"/>, event
    /// <see cref="PhaseChanged"/> and GameEventType.DayPhaseChanged). One in-game hour lasts <see cref="secondsPerHour"/>
    /// real seconds (36 min per day by default). Raises NightStarted / DayStarted.
    /// Light: the sun climbs on a tilted arc (east to south to west, at most <see cref="maxSunElevation"/> degrees), its
    /// strength follows the elevation (weak and golden near the horizon, strong and neutral-warm in the afternoon), then
    /// fades out below the horizon before the same light turns into a cool moon light (no pop at dusk). Only the direct
    /// light turns orange at sunset: the ambient stays cooler, so the island is not tinted orange. The night keeps a
    /// readable blue ambient floor. WeatherManager writes the weather part every frame (<see cref="overcast"/>,
    /// <see cref="rain"/>, <see cref="storm"/>, the sun / ambient / fog multipliers) and HazardZone smoke thickens the
    /// fog locally. Air temperature for survival comes from here (warm afternoon, cool night, clouds cool the day and
    /// keep the night warmer, rain and storms cool). Sets the global shader value _PF_NightFactor (0 while the sun is
    /// well up .. 1 at night; stars, heat shimmer).
    /// </summary>
    public class TimeManager : MonoBehaviour
    {
        /// <summary>named parts of the day (boundaries: <see cref="dawnStart"/>.. fields; Night starts with IsNight)</summary>
        public enum Phase { Dawn, Morning, Noon, Afternoon, Dusk, Night }

        public static TimeManager Instance { get; private set; }
        [Range(0, 24)] public float hour = 9f;
        public int day = 1;
        public float secondsPerHour = 90f;
        public bool paused;
        public Light sun;
        public float sunriseHour = 6f, sunsetHour = 19.5f;
        public float daySunIntensity = 1.75f, nightMoonIntensity = 0.2f;
        public Gradient sunColor, skyAmbient, equatorAmbient, groundAmbient, fogColor;
        public AnimationCurve fogDensity;
        [Tooltip("compass direction (deg) the sunrise light shines towards; the sun rises opposite and sets on this side")] public float yaw = -135f;
        [Tooltip("weather dims the sun (0..1), set by WeatherManager")] public float overcast;

        [Header("Day phases (hours; Night starts at sunsetHour + 0.5, when IsNight turns on)")]
        public float dawnStart = 5.25f;
        public float morningStart = 7.25f, noonStart = 11f, afternoonStart = 14f, duskStart = 18f;

        [Header("Sun and moon")]
        [Tooltip("highest sun elevation at midday, degrees")] [Range(20f, 90f)] public float maxSunElevation = 66f;
        [Tooltip("sun strength near the horizon (x daySunIntensity); full strength from about 40 degrees")] [Range(0f, 1f)] public float lowSunStrength = 0.32f;
        [Tooltip("the morning light is this much softer than the afternoon's (x intensity until mid-morning)")] [Range(0.5f, 1f)] public float morningSoftness = 0.86f;
        public Color moonColor = new Color(0.62f, 0.72f, 1f);
        [Range(10f, 80f)] public float moonElevation = 42f;

        [Header("Weather (written by WeatherManager every frame; 0 / 1 = no weather)")]
        [Range(0, 1)] public float rain;
        [Range(0, 1)] public float storm;
        [Tooltip("x sun intensity (storm darkness)")] public float sunMultiplier = 1f;
        [Tooltip("x ambient light (storm darkness)")] public float ambientMultiplier = 1f;
        [Tooltip("x fog density (clouds, rain, storm)")] public float fogMultiplier = 1f;
        [Tooltip("0..1: fog and sky drift to rain grey")] [Range(0, 1)] public float weatherGrey;

        [Header("Air temperature (deg C, before shelter / fire / wetness / zones)")]
        public float coolestAir = 16f, warmestAir = 29f;
        public float coolestHour = 5f, warmestHour = 15f;
        [Tooltip("clouds by day: this much cooler at full cover")] public float cloudDayCooling = 3f;
        [Tooltip("clouds at night keep the warmth in: this much warmer at full cover")] public float cloudNightWarming = 2f;
        [Tooltip("a clear night loses heat to the sky: this much cooler")] public float clearNightCooling = 1.5f;
        [Tooltip("rain at full intensity cools the air this much")] public float rainCooling = 2f;
        [Tooltip("a storm cools a bit more (wind)")] public float stormCooling = 1f;

        /// <summary>version of the built-in lighting keys: a scene saved with older keys gets the new defaults at load</summary>
        public const int LightingVersion = 3;
        [SerializeField, HideInInspector] int lightingVersion;

        public bool IsNight => hour >= sunsetHour + 0.5f || hour < sunriseHour;
        public float Daylight01 { get; private set; }
        /// <summary>0 while the sun is well up (above about 10 degrees), rising through dusk to 1 at night</summary>
        public float NightFactor { get; private set; }
        /// <summary>sun elevation in degrees (negative below the horizon)</summary>
        public float SunElevation { get; private set; }
        /// <summary>the current part of the day</summary>
        public Phase CurrentPhase { get; private set; }
        public event Action NightStarted, DayStarted;
        public event Action<int> NewDay;
        /// <summary>the day moved into a new phase (also GameEventType.DayPhaseChanged, id = phase name, amount = (int)phase)</summary>
        public event Action<Phase> PhaseChanged;
        bool _wasNight; Material _sky; bool _skyBase; Color _skyTint0 = new Color(0.52f, 0.58f, 0.66f);
        static readonly int NightId = Shader.PropertyToID("_PF_NightFactor"), ExposureId = Shader.PropertyToID("_Exposure"),
            TintId = Shader.PropertyToID("_SkyTint");

        void Awake()
        {
            Instance = this;
            if (lightingVersion < LightingVersion || sunColor == null || sunColor.colorKeys.Length < 2) Defaults();
            _wasNight = IsNight;
            CurrentPhase = PhaseAt(hour);
            GameClock.SecondsPerHour = secondsPerHour;
        }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            if (!sun) sun = RenderSettings.sun;
            if (!sun) foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { sun = l; break; }
            if (RenderSettings.skybox) UseSky(new Material(RenderSettings.skybox));   // never edit the asset
            RenderSettings.ambientMode = AmbientMode.Trilight;
            Apply();
        }

        /// <summary>the sky material Apply drives (a copy, never the asset); editor previews pass their own copy, null = none</summary>
        public void UseSky(Material copy)
        {
            _sky = copy; _skyBase = false;
            if (_sky) { RenderSettings.skybox = _sky; if (_sky.HasProperty(TintId)) _skyTint0 = _sky.GetColor(TintId); _skyBase = true; }
        }

        /// <summary>built-in sun / sky / fog colours (Inspector: right click the component)</summary>
        [ContextMenu("Reset colours to defaults")]
        public void Defaults()
        {
            Gradient G(params (float h, Color c)[] k)
            {
                var g = new Gradient(); var ck = new GradientColorKey[k.Length]; for (int i = 0; i < k.Length; i++) ck[i] = new GradientColorKey(k[i].c, k[i].h / 24f);
                g.SetKeys(ck, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) }); return g;
            }
            // keys by hour (at most 8 each). Direct sun: golden morning, neutral-warm afternoon, golden-orange sunset.
            sunColor = G((5.5f, new Color(1f, 0.62f, 0.44f)), (6.3f, new Color(1f, 0.74f, 0.54f)), (7.6f, new Color(1f, 0.84f, 0.66f)), (9.6f, new Color(1f, 0.92f, 0.82f)),
                         (13f, new Color(1f, 0.96f, 0.9f)), (16.4f, new Color(1f, 0.93f, 0.81f)), (18.4f, new Color(1f, 0.77f, 0.5f)), (19.5f, new Color(1f, 0.57f, 0.31f)));
            // ambient stays cooler than the sun at the golden hours (shadows keep a blue sky tone): the island is never all orange
            skyAmbient = G((0f, new Color(0.1f, 0.13f, 0.22f)), (5.15f, new Color(0.12f, 0.14f, 0.24f)), (6.25f, new Color(0.36f, 0.38f, 0.5f)), (7.9f, new Color(0.52f, 0.58f, 0.7f)),
                           (13.2f, new Color(0.58f, 0.66f, 0.78f)), (18.7f, new Color(0.5f, 0.55f, 0.68f)), (20.2f, new Color(0.2f, 0.21f, 0.34f)), (24f, new Color(0.1f, 0.13f, 0.22f)));
            equatorAmbient = G((0f, new Color(0.06f, 0.08f, 0.13f)), (5.15f, new Color(0.07f, 0.08f, 0.14f)), (6.25f, new Color(0.34f, 0.32f, 0.34f)), (8.2f, new Color(0.46f, 0.45f, 0.43f)),
                               (13.2f, new Color(0.48f, 0.49f, 0.47f)), (19f, new Color(0.46f, 0.41f, 0.4f)), (20.3f, new Color(0.12f, 0.11f, 0.18f)), (24f, new Color(0.06f, 0.08f, 0.13f)));
            groundAmbient = G((0f, new Color(0.03f, 0.035f, 0.05f)), (5.3f, new Color(0.035f, 0.035f, 0.05f)), (7.2f, new Color(0.2f, 0.18f, 0.15f)), (13.2f, new Color(0.24f, 0.22f, 0.18f)),
                              (19.2f, new Color(0.21f, 0.17f, 0.13f)), (20.6f, new Color(0.05f, 0.05f, 0.07f)), (24f, new Color(0.03f, 0.035f, 0.05f)));
            fogColor = G((0f, new Color(0.07f, 0.09f, 0.14f)), (5.3f, new Color(0.1f, 0.11f, 0.17f)), (6.25f, new Color(0.56f, 0.55f, 0.58f)), (8.2f, new Color(0.66f, 0.7f, 0.74f)),
                         (13.2f, new Color(0.7f, 0.76f, 0.82f)), (18.7f, new Color(0.72f, 0.64f, 0.58f)), (20.2f, new Color(0.24f, 0.22f, 0.3f)), (24f, new Color(0.07f, 0.09f, 0.14f)));
            // morning mist at dawn, clear afternoon, a little haze at dusk
            fogDensity = new AnimationCurve(new Keyframe(0f, 0.0032f), new Keyframe(5.3f / 24f, 0.0036f), new Keyframe(6.5f / 24f, 0.0042f), new Keyframe(8.6f / 24f, 0.0022f),
                                            new Keyframe(12f / 24f, 0.0015f), new Keyframe(18f / 24f, 0.0016f), new Keyframe(20.2f / 24f, 0.0026f), new Keyframe(1f, 0.0032f));
            daySunIntensity = 1.75f; nightMoonIntensity = 0.2f; moonColor = new Color(0.62f, 0.72f, 1f);
            lightingVersion = LightingVersion;
        }

        void Update()
        {
            if (paused) return;
            float dh = Time.deltaTime / Mathf.Max(1f, secondsPerHour);
            GameClock.Now += Time.deltaTime;
            Advance(dh);
            Apply();
        }

        void Advance(float hours)
        {
            hour += hours;
            while (hour >= 24f) { hour -= 24f; day++; NewDay?.Invoke(day); }
            bool night = IsNight;
            if (night != _wasNight)
            {
                _wasNight = night;
                if (night) { NightStarted?.Invoke(); GameEvents.Raise(GameEventType.NightStarted, "night", day); }
                else { DayStarted?.Invoke(); GameEvents.Raise(GameEventType.DayStarted, "day", day); }
            }
            var ph = PhaseAt(hour);
            if (ph != CurrentPhase)
            {
                CurrentPhase = ph;
                PhaseChanged?.Invoke(ph);
                GameEvents.Raise(GameEventType.DayPhaseChanged, ph.ToString(), (int)ph);
            }
        }

        /// <summary>the part of the day at an hour (0..24)</summary>
        public Phase PhaseAt(float h)
        {
            h = Mathf.Repeat(h, 24f);
            if (h >= sunsetHour + 0.5f || h < dawnStart) return Phase.Night;
            if (h < morningStart) return Phase.Dawn;
            if (h < noonStart) return Phase.Morning;
            if (h < afternoonStart) return Phase.Noon;
            if (h < duskStart) return Phase.Afternoon;
            return Phase.Dusk;
        }

        /// <summary>hour at which a phase starts</summary>
        public float PhaseStart(Phase p) => p switch
        {
            Phase.Dawn => dawnStart, Phase.Morning => morningStart, Phase.Noon => noonStart,
            Phase.Afternoon => afternoonStart, Phase.Dusk => duskStart, _ => sunsetHour + 0.5f,
        };

        /// <summary>jump forward (sleeping). Game clock and respawn timers move with it.</summary>
        public void SkipHours(float hours)
        {
            GameClock.Now += hours * secondsPerHour;
            Advance(hours); Apply();
        }
        public float HoursUntil(float targetHour) { float d = targetHour - hour; if (d <= 0f) d += 24f; return d; }

        /// <summary>set the clock (new game, load, tests): no NightStarted / DayStarted / DayPhaseChanged for the jump</summary>
        public void Set(int d, float h) { day = d; hour = Mathf.Repeat(h, 24f); _wasNight = IsNight; CurrentPhase = PhaseAt(hour); Apply(); }

        /// <summary>signed sun elevation (degrees) at an hour: a sine arc over the day that continues below the horizon (twilight)</summary>
        public float SunElevationAt(float h)
        {
            float u = (Mathf.Repeat(h, 24f) - sunriseHour) / Mathf.Max(1f, sunsetHour - sunriseHour);
            if (u < -0.2f || u > 1.2f) return -40f;
            return maxSunElevation * Mathf.Sin(u * Mathf.PI);
        }

        public void Apply()
        {
            float t = hour / 24f;
            float dayLen = Mathf.Max(1f, sunsetHour - sunriseHour);
            float u = (hour - sunriseHour) / dayLen;           // 0..1 during the day
            bool day = u >= 0f && u <= 1f;
            float elev = day ? Mathf.Sin(u * Mathf.PI) : 0f;   // 0..1 (read by Daylight01 / NightFactor: same meaning as before)
            Daylight01 = Mathf.Clamp01(elev * 3f);
            float nf = day ? 1f - Mathf.Clamp01(elev * 6f) : 1f;
            NightFactor = nf * nf;
            Shader.SetGlobalFloat(NightId, NightFactor);
            float el = SunElevationAt(hour); SunElevation = el;
            float oc = Mathf.Clamp01(overcast), st = Mathf.Clamp01(storm);
            if (sun)
            {
                // the sun fades out at the horizon and the same light fades in as the moon a little later: no pop at dusk / dawn
                float sunK = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-2f, 7f, el));
                float moonK = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-3f, -12f, el));
                if (el > -2.5f)
                {
                    float az = yaw + 180f + 180f * Mathf.Clamp(u, -0.2f, 1.2f);          // sunrise opposite yaw, south-ish at noon, sets towards yaw
                    Vector3 toSun = Quaternion.Euler(-Mathf.Max(el, 0.5f), az, 0f) * Vector3.forward;
                    sun.transform.rotation = Quaternion.LookRotation(-toSun);
                    float strength = Mathf.Lerp(lowSunStrength, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(el / 40f)));
                    float soft = hour < noonStart ? Mathf.Lerp(morningSoftness, 1f, Mathf.InverseLerp(sunriseHour + 1f, noonStart - 0.5f, hour)) : 1f;
                    sun.intensity = daySunIntensity * strength * soft * sunK * (1f - oc * 0.6f) * Mathf.Max(0f, sunMultiplier);
                    var c = sunColor.Evaluate(t);
                    float g = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
                    sun.color = Color.Lerp(c, new Color(g, g, g * 1.04f), oc * 0.55f);          // cloud light is less golden
                    sun.shadowStrength = Mathf.Lerp(0.45f, 0.88f, Mathf.Clamp01(el / 30f)) * (1f - oc * 0.55f);
                }
                else
                {
                    float nt = Mathf.Repeat((hour - sunsetHour) / (24f - dayLen), 1f);          // 0 at sunset .. 1 at sunrise
                    sun.transform.rotation = Quaternion.Euler(moonElevation - 10f + 20f * Mathf.Sin(nt * Mathf.PI), yaw + 180f + 60f * (nt - 0.5f), 0f);
                    sun.intensity = nightMoonIntensity * moonK * (1f - oc * 0.7f) * Mathf.Max(0f, sunMultiplier);
                    sun.color = moonColor;
                    sun.shadowStrength = 0.45f * (1f - oc * 0.6f);
                }
            }
            float amb = (1f - oc * 0.3f) * Mathf.Max(0f, ambientMultiplier);
            float grey = Mathf.Clamp01(weatherGrey);
            RenderSettings.ambientSkyColor = Desat(skyAmbient.Evaluate(t), grey * 0.5f) * amb;
            RenderSettings.ambientEquatorColor = Desat(equatorAmbient.Evaluate(t), grey * 0.5f) * amb;
            RenderSettings.ambientGroundColor = Desat(groundAmbient.Evaluate(t), grey * 0.4f) * amb;
            // fog: weather greys and thickens it, volcanic smoke (HazardZone) thickens and browns it locally
            var fc = fogColor.Evaluate(t);
            float lum = fc.r * 0.3f + fc.g * 0.59f + fc.b * 0.11f;
            fc = Color.Lerp(fc, new Color(lum, lum * 1.02f, lum * 1.06f) * (1f - st * 0.35f), grey);
            float smoke = World.HazardZone.LocalSmoke;
            if (smoke > 0f) fc = Color.Lerp(fc, World.HazardZone.SmokeColor * Mathf.Lerp(0.35f, 1f, Daylight01), smoke * 0.75f);
            RenderSettings.fogColor = fc;
            RenderSettings.fogDensity = fogDensity.Evaluate(t) * Mathf.Max(0.1f, fogMultiplier) * (1f + smoke * World.HazardZone.SmokeFogBoost);
            RenderSettings.reflectionIntensity = Mathf.Lerp(0.25f, 1f, Daylight01) * (1f - oc * 0.3f);
            if (_sky)
            {
                if (_sky.HasProperty(ExposureId)) _sky.SetFloat(ExposureId, Mathf.Lerp(0.12f, 1.15f, Mathf.Clamp01(elev * 2f + 0.05f)) * (1f - oc * 0.4f) * (1f - st * 0.45f));
                if (_skyBase && _sky.HasProperty(TintId))
                {
                    float sl = _skyTint0.r * 0.3f + _skyTint0.g * 0.59f + _skyTint0.b * 0.11f;
                    _sky.SetColor(TintId, Color.Lerp(_skyTint0, new Color(sl, sl, sl * 1.05f), Mathf.Max(oc * 0.7f, grey)));
                }
                // the atmosphere thickness stays the asset's: a thicker procedural sky turns the horizon olive at dawn / dusk
            }
        }

        static Color Desat(Color c, float k)
        {
            if (k <= 0f) return c;
            float l = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
            return Color.Lerp(c, new Color(l, l, l), k);
        }

        /// <summary>diurnal air (deg C) at an hour before weather: coolest at coolestHour, warmest at warmestHour (cosine segments)</summary>
        public float DiurnalAir(float h)
        {
            h = Mathf.Repeat(h, 24f);
            float rise = Mathf.Repeat(warmestHour - coolestHour, 24f); if (rise < 1f) rise = 1f;
            float since = Mathf.Repeat(h - coolestHour, 24f);
            float k = since < rise ? (1f - Mathf.Cos(Mathf.PI * since / rise)) * 0.5f
                                   : (1f + Mathf.Cos(Mathf.PI * (since - rise) / Mathf.Max(1f, 24f - rise))) * 0.5f;
            return Mathf.Lerp(coolestAir, warmestAir, k);
        }

        /// <summary>air temperature (deg C) before shelter / fire / wetness / zones: the day curve and the weather</summary>
        public float AirTemperature()
        {
            float d = Daylight01, n = 1f - d, oc = Mathf.Clamp01(overcast);
            return DiurnalAir(hour) - cloudDayCooling * oc * d + cloudNightWarming * oc * n - clearNightCooling * (1f - oc) * n
                   - rainCooling * Mathf.Clamp01(rain) - stormCooling * Mathf.Clamp01(storm);
        }
    }
}
