using System;
using UnityEngine;
using PrimalFrontier.Audio;

namespace PrimalFrontier.Core
{
    public enum WeatherState { Clear, Rain, Storm, Cloudy }      // appended only (saved by name, compared as ints)

    /// <summary>
    /// Clear -> Cloudy -> Rain -> Cloudy -> Clear (and the opening storm with lightning). Clouds dim the sun and
    /// thicken the fog; rain is a camera-following particle box that cools the air, wets the player unless covered and
    /// makes campfires burn fuel faster; storms add lightning flashes with thunder a moment later. Surfaces get wet in
    /// the rain and dry slowly after it. Global shader values for custom shaders: _PF_Wetness (0..1),
    /// _PF_Overcast (0..1), _PF_Wind (xy direction, z strength, w gust). Random weather can be disabled (tutorial)
    /// and forced (story).
    /// </summary>
    public class WeatherManager : MonoBehaviour
    {
        public static WeatherManager Instance { get; private set; }
        public WeatherState State { get; private set; } = WeatherState.Clear;
        public float Intensity { get; private set; }            // rain 0..1 smoothed
        public float Overcast { get; private set; }             // cloud cover 0..1 smoothed
        public float Wetness { get; private set; }              // surfaces 0..1: up in the rain, dries after
        public bool allowRandom = true;
        [Tooltip("chance per in-game hour that clouds come in on a clear day")] [Range(0, 1)] public float cloudChancePerHour = 0.12f;
        [Tooltip("chance per in-game hour that a cloudy sky starts to rain")] [Range(0, 1)] public float rainChancePerHour = 0.25f;
        public Vector2 cloudHours = new Vector2(1.5f, 4f);
        public Vector2 rainHours = new Vector2(1f, 3f);
        [Header("Wind (for foliage / water shaders)")]
        public Vector2 windDirection = new Vector2(0.8f, 0.6f);
        public float calmWind = 0.35f, stormWind = 1.4f;
        [Tooltip("the wind slowly swings this many degrees either side of windDirection (0 = fixed)")] [Range(0, 180)] public float windDriftDegrees = 60f;
        [Tooltip("in-game hours for one slow swing of the wind direction")] [Min(0.5f)] public float windDriftHours = 8f;
        /// <summary>current wind direction (world xz, normalized): the way the wind blows, as _PF_Wind.xy; scent drifts along it</summary>
        public Vector2 WindDirection { get; private set; } = new Vector2(0.8f, 0.6f);
        /// <summary>current wind strength (calm 0.35 .. storm 1.4), as _PF_Wind.z</summary>
        public float WindStrength { get; private set; } = 0.35f;
        [Header("Lightning (storms)")]
        public Color lightningColor = new Color(0.8f, 0.86f, 1f);
        public float lightningIntensity = 2.6f;
        public Material rainMaterial;
        public int maxParticles = 1400;

        ParticleSystem _rain; float _target, _cloudTarget; double _until = -1; double _nextRoll; float _nextFlash = 5f;
        Light _flash; float _flashT = -1f, _thunderAt = -1f, _thunderVol; float _gust, _gustTarget, _nextGust;
        static readonly int WetId = Shader.PropertyToID("_PF_Wetness"), OvercastId = Shader.PropertyToID("_PF_Overcast"), WindId = Shader.PropertyToID("_PF_Wind");
        public event Action<WeatherState> Changed;

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start() { BuildRain(); }

        void BuildRain()
        {
            var go = new GameObject("Rain"); go.transform.SetParent(transform, false);
            _rain = go.AddComponent<ParticleSystem>();
            _rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _rain.main; main.loop = true; main.startLifetime = 1.1f; main.startSpeed = 0f; main.startSize3D = true;
            main.startSizeX = 0.025f; main.startSizeY = 0.55f; main.startSizeZ = 1f; main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.startColor = new Color(0.8f, 0.85f, 0.9f, 0.35f);
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate; main.playOnAwake = false;
            var em = _rain.emission; em.rateOverTime = 0f;
            var sh = _rain.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(34f, 1f, 34f); sh.position = new Vector3(0, 11f, 0);
            var vel = _rain.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-1.2f, -0.6f); vel.y = new ParticleSystem.MinMaxCurve(-15f, -12f); vel.z = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
            var r = _rain.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.035f; r.lengthScale = 1f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            if (rainMaterial) r.sharedMaterial = rainMaterial;
            _rain.Play();
        }

        public void SetWeather(WeatherState s, float hours = -1f, bool instant = false)
        {
            State = s;
            _target = s == WeatherState.Rain ? 0.75f : s == WeatherState.Storm ? 1f : 0f;
            _cloudTarget = s == WeatherState.Clear ? 0f : s == WeatherState.Cloudy ? 0.5f : s == WeatherState.Rain ? 0.8f : 1f;
            _until = hours > 0f ? GameClock.Now + GameClock.Hours(hours) : -1;
            if (instant) { Intensity = _target; Overcast = _cloudTarget; if (_target > 0f) Wetness = Mathf.Max(Wetness, 0.8f); }
            Changed?.Invoke(s);
            GameEvents.Raise(GameEventType.WeatherChanged, s.ToString(), 1);
        }

        void Update()
        {
            // schedule: clear -> cloudy -> (rain -> cloudy) -> clear
            if (_until > 0 && GameClock.Now >= _until)
            {
                if (State == WeatherState.Rain || State == WeatherState.Storm) SetWeather(WeatherState.Cloudy, UnityEngine.Random.Range(0.5f, 1.5f));
                else SetWeather(WeatherState.Clear);
            }
            if (allowRandom && GameClock.Now >= _nextRoll)
            {
                _nextRoll = GameClock.Now + GameClock.Hours(1f);
                if (State == WeatherState.Clear && UnityEngine.Random.value < cloudChancePerHour) SetWeather(WeatherState.Cloudy, UnityEngine.Random.Range(cloudHours.x, cloudHours.y));
                else if (State == WeatherState.Cloudy && UnityEngine.Random.value < rainChancePerHour) SetWeather(WeatherState.Rain, UnityEngine.Random.Range(rainHours.x, rainHours.y));
            }
            float dt = Time.deltaTime;
            Intensity = Mathf.MoveTowards(Intensity, _target, dt / 12f);
            Overcast = Mathf.MoveTowards(Overcast, Mathf.Max(_cloudTarget, Intensity), dt / 20f);
            // surfaces soak in ~40 s of rain and dry in a few minutes (faster under a clear sky)
            Wetness = Intensity > 0.2f ? Mathf.MoveTowards(Wetness, 1f, dt * Intensity / 40f) : Mathf.MoveTowards(Wetness, 0f, dt * (1.3f - Overcast) / 240f);

            // presentation
            var cam = Camera.main;
            if (_rain)
            {
                if (cam) _rain.transform.position = cam.transform.position + cam.transform.forward * 6f;
                var em = _rain.emission; em.rateOverTime = Intensity * maxParticles * 0.9f;
            }
            var tm = TimeManager.Instance; if (tm) tm.overcast = Overcast;
            // wind: calm with slow gusts, strong in storms
            if (Time.time > _nextGust) { _nextGust = Time.time + UnityEngine.Random.Range(3f, 9f); _gustTarget = UnityEngine.Random.Range(0f, 1f); }
            _gust = Mathf.MoveTowards(_gust, _gustTarget, dt * 0.4f);
            float wind = Mathf.Lerp(calmWind, stormWind, Mathf.Max(Intensity, Overcast * 0.4f));
            var wd = DriftedWind();
            WindDirection = wd; WindStrength = wind;
            Shader.SetGlobalVector(WindId, new Vector4(wd.x, wd.y, wind, _gust));
            Shader.SetGlobalFloat(WetId, Wetness);
            Shader.SetGlobalFloat(OvercastId, Overcast);
            Lightning(dt);
        }

        void Lightning(float dt)
        {
            if (State == WeatherState.Storm && Intensity > 0.5f && Time.time > _nextFlash)
            {
                _nextFlash = Time.time + UnityEngine.Random.Range(7f, 18f);
                _flashT = 0f;
                float dist = UnityEngine.Random.Range(0.2f, 1f);                     // near strikes: louder, sooner
                _thunderAt = Time.time + Mathf.Lerp(0.3f, 3f, dist); _thunderVol = Mathf.Lerp(1f, 0.45f, dist);
                if (!_flash)
                {
                    var go = new GameObject("Lightning"); go.transform.SetParent(transform, false);
                    _flash = go.AddComponent<Light>(); _flash.type = LightType.Directional; _flash.shadows = LightShadows.None;
                    _flash.color = lightningColor; _flash.enabled = false;
                }
                _flash.transform.rotation = Quaternion.Euler(UnityEngine.Random.Range(35f, 70f), UnityEngine.Random.Range(0f, 360f), 0f);
            }
            if (_flash && _flashT >= 0f)
            {
                _flashT += dt;
                // two quick pulses and a fade (~0.45 s)
                float t = _flashT, k = t < 0.06f ? t / 0.06f : t < 0.12f ? 1f - (t - 0.06f) / 0.06f * 0.7f : t < 0.18f ? 0.3f + (t - 0.12f) / 0.06f * 0.7f : Mathf.Max(0f, 1f - (t - 0.18f) / 0.27f);
                _flash.enabled = k > 0.01f; _flash.intensity = k * lightningIntensity;
                if (t > 0.45f) { _flashT = -1f; _flash.enabled = false; }
            }
            if (_thunderAt > 0f && Time.time >= _thunderAt)
            {
                _thunderAt = -1f;
                SfxPlayer.Instance.Play2D(SfxId.Thunder, _thunderVol);
            }
        }

        /// <summary>
        /// windDirection turned by a slow, smooth swing (Perlin noise over the game clock, +- windDriftDegrees over about
        /// windDriftHours): foliage follows without jumps and scent drifts with it, so the wind is worth checking.
        /// </summary>
        Vector2 DriftedWind()
        {
            var b = windDirection.sqrMagnitude > 0.001f ? windDirection.normalized : Vector2.right;
            if (windDriftDegrees <= 0f) return b;
            float t = (float)(GameClock.Now / System.Math.Max(1.0, GameClock.Hours(windDriftHours)));
            float a = (Mathf.PerlinNoise(t, 0.37f) * 2f - 1f) * windDriftDegrees * Mathf.Deg2Rad;
            float cs = Mathf.Cos(a), sn = Mathf.Sin(a);
            return new Vector2(b.x * cs - b.y * sn, b.x * sn + b.y * cs);
        }

        public bool RainingAt(Vector3 p) => Intensity > 0.25f && !World.Shelter.Covers(p) && !(World.ZoneManager.Instance && World.ZoneManager.Instance.IsIndoor(p));
    }
}
