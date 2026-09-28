using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace PrimalFrontier.Core
{
    /// <summary>
    /// Day / night: sun path, light colour and intensity, trilight ambient, fog, sky exposure. One in-game hour lasts
    /// <see cref="secondsPerHour"/> real seconds (36 min per day by default). Raises NightStarted / DayStarted.
    /// Air temperature for survival comes from here (warm afternoon, cool night). Sets the global shader value
    /// _PF_NightFactor (0 while the sun is well up .. 1 at night; stars, heat shimmer).
    /// </summary>
    public class TimeManager : MonoBehaviour
    {
        public static TimeManager Instance { get; private set; }
        [Range(0, 24)] public float hour = 9f;
        public int day = 1;
        public float secondsPerHour = 90f;
        public bool paused;
        public Light sun;
        public float sunriseHour = 6f, sunsetHour = 19.5f;
        public float daySunIntensity = 1.6f, nightMoonIntensity = 0.12f;
        public Gradient sunColor, skyAmbient, equatorAmbient, groundAmbient, fogColor;
        public AnimationCurve fogDensity;
        public float yaw = -135f;
        [Tooltip("weather dims the sun (0..1), set by WeatherManager")] public float overcast;

        public bool IsNight => hour >= sunsetHour + 0.5f || hour < sunriseHour;
        public float Daylight01 { get; private set; }
        /// <summary>0 while the sun is well up (above about 10 degrees), rising through dusk to 1 at night</summary>
        public float NightFactor { get; private set; }
        public event Action NightStarted, DayStarted;
        public event Action<int> NewDay;
        bool _wasNight; Material _sky;
        static readonly int NightId = Shader.PropertyToID("_PF_NightFactor");

        void Awake()
        {
            Instance = this;
            if (sunColor == null || sunColor.colorKeys.Length < 2) Defaults();
            _wasNight = IsNight;
            GameClock.SecondsPerHour = secondsPerHour;
        }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            if (!sun) sun = RenderSettings.sun;
            if (!sun) foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { sun = l; break; }
            if (RenderSettings.skybox) { _sky = new Material(RenderSettings.skybox); RenderSettings.skybox = _sky; }   // never edit the asset
            RenderSettings.ambientMode = AmbientMode.Trilight;
            Apply();
        }

        /// <summary>built-in sun / sky / fog colours (Inspector: right click the component)</summary>
        [ContextMenu("Reset colours to defaults")]
        public void Defaults()
        {
            Gradient G(params (float t, Color c)[] k)
            {
                var g = new Gradient(); var ck = new GradientColorKey[k.Length]; for (int i = 0; i < k.Length; i++) ck[i] = new GradientColorKey(k[i].c, k[i].t);
                g.SetKeys(ck, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) }); return g;
            }
            // t = hour / 24
            sunColor = G((0.20f, new Color(0.45f, 0.5f, 0.75f)), (0.26f, new Color(1f, 0.62f, 0.38f)), (0.35f, new Color(1f, 0.9f, 0.78f)), (0.55f, new Color(1f, 0.95f, 0.86f)),
                         (0.76f, new Color(1f, 0.75f, 0.5f)), (0.82f, new Color(0.95f, 0.45f, 0.3f)), (0.88f, new Color(0.45f, 0.5f, 0.75f)));
            skyAmbient = G((0.0f, new Color(0.05f, 0.07f, 0.13f)), (0.24f, new Color(0.3f, 0.3f, 0.38f)), (0.35f, new Color(0.55f, 0.62f, 0.72f)), (0.6f, new Color(0.6f, 0.67f, 0.76f)),
                           (0.8f, new Color(0.5f, 0.4f, 0.42f)), (0.86f, new Color(0.08f, 0.1f, 0.18f)), (1f, new Color(0.05f, 0.07f, 0.13f)));
            equatorAmbient = G((0.0f, new Color(0.04f, 0.05f, 0.09f)), (0.25f, new Color(0.35f, 0.28f, 0.25f)), (0.4f, new Color(0.45f, 0.46f, 0.44f)), (0.78f, new Color(0.5f, 0.36f, 0.28f)),
                               (0.86f, new Color(0.06f, 0.07f, 0.12f)), (1f, new Color(0.04f, 0.05f, 0.09f)));
            groundAmbient = G((0.0f, new Color(0.02f, 0.02f, 0.03f)), (0.3f, new Color(0.22f, 0.2f, 0.17f)), (0.8f, new Color(0.2f, 0.15f, 0.12f)), (0.88f, new Color(0.03f, 0.03f, 0.04f)), (1f, new Color(0.02f, 0.02f, 0.03f)));
            fogColor = G((0.0f, new Color(0.06f, 0.08f, 0.12f)), (0.25f, new Color(0.62f, 0.55f, 0.52f)), (0.4f, new Color(0.68f, 0.74f, 0.78f)), (0.78f, new Color(0.75f, 0.6f, 0.5f)),
                         (0.86f, new Color(0.1f, 0.11f, 0.16f)), (1f, new Color(0.06f, 0.08f, 0.12f)));
            fogDensity = new AnimationCurve(new Keyframe(0f, 0.004f), new Keyframe(0.25f, 0.0035f), new Keyframe(0.4f, 0.0016f), new Keyframe(0.75f, 0.0018f), new Keyframe(0.86f, 0.003f), new Keyframe(1f, 0.004f));
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
        }

        /// <summary>jump forward (sleeping). Game clock and respawn timers move with it.</summary>
        public void SkipHours(float hours)
        {
            GameClock.Now += hours * secondsPerHour;
            Advance(hours); Apply();
        }
        public float HoursUntil(float targetHour) { float d = targetHour - hour; if (d <= 0f) d += 24f; return d; }

        public void Set(int d, float h) { day = d; hour = Mathf.Repeat(h, 24f); _wasNight = IsNight; Apply(); }

        public void Apply()
        {
            float t = hour / 24f;
            // sun elevation: 0 at sunrise/sunset, 1 at noon; negative at night (moon light from the opposite side)
            float dayLen = sunsetHour - sunriseHour;
            float u = (hour - sunriseHour) / dayLen;           // 0..1 during the day
            bool day = u >= 0f && u <= 1f;
            float elev = day ? Mathf.Sin(u * Mathf.PI) : 0f;
            Daylight01 = Mathf.Clamp01(elev * 3f);
            float nf = day ? 1f - Mathf.Clamp01(elev * 6f) : 1f;
            NightFactor = nf * nf;
            Shader.SetGlobalFloat(NightId, NightFactor);
            if (sun)
            {
                float pitch = day ? Mathf.Lerp(4f, 176f, u) : Mathf.Lerp(10f, 170f, Mathf.Repeat((hour - sunsetHour) / (24f - dayLen), 1f));
                sun.transform.rotation = Quaternion.Euler(day ? pitch : 35f + 20f * Mathf.Sin(Mathf.Repeat((hour - sunsetHour) / (24f - dayLen), 1f) * Mathf.PI), yaw + (day ? 0f : 180f), 0f);
                float k = 1f - overcast * 0.6f;
                sun.intensity = day ? Mathf.Lerp(0.15f, daySunIntensity, Mathf.Clamp01(elev * 2.5f)) * k : nightMoonIntensity * (1f - overcast * 0.7f);
                sun.color = sunColor.Evaluate(t);
                sun.shadowStrength = day ? Mathf.Lerp(0.4f, 0.85f, elev) * (1f - overcast * 0.5f) : 0.35f;
            }
            float amb = 1f - overcast * 0.35f;
            RenderSettings.ambientSkyColor = skyAmbient.Evaluate(t) * amb;
            RenderSettings.ambientEquatorColor = equatorAmbient.Evaluate(t) * amb;
            RenderSettings.ambientGroundColor = groundAmbient.Evaluate(t) * amb;
            var fc = fogColor.Evaluate(t); RenderSettings.fogColor = Color.Lerp(fc, fc * 0.7f + new Color(0.2f, 0.22f, 0.25f) * 0.3f, overcast);
            RenderSettings.fogDensity = fogDensity.Evaluate(t) * (1f + overcast * 1.5f);
            if (_sky && _sky.HasProperty("_Exposure")) _sky.SetFloat("_Exposure", Mathf.Lerp(0.12f, 1.15f, Mathf.Clamp01(elev * 2f + 0.05f)) * (1f - overcast * 0.4f));
        }

        /// <summary>air temperature (deg C) before shelter / fire / wetness</summary>
        public float AirTemperature()
        {
            // coolest before dawn (15), warmest mid-afternoon (28)
            float c = Mathf.Cos((hour - 15f) / 24f * Mathf.PI * 2f);
            return Mathf.Lerp(15f, 28f, (c + 1f) * 0.5f) - overcast * 5f;
        }

    }
}
