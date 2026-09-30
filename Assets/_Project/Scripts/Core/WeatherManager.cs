using System;
using UnityEngine;
using PrimalFrontier.Audio;

namespace PrimalFrontier.Core
{
    public enum WeatherState { Clear, Rain, Storm, Cloudy }      // appended only (saved by name, compared as ints)

    /// <summary>
    /// Clear, cloudy, rain (light / normal / heavy) and storm, from a random schedule (<see cref="WeatherPlanner"/>, numbers in
    /// <see cref="WeatherConfig"/>): storms are rare, short and never back to back. What the weather does, eased over
    /// seconds so nothing pops:
    /// - clouds dim and grey the sun and sky; rain and storms thicken the fog (visibility), darken the sun and ambient
    ///   (TimeManager multipliers) and cool the air (TimeManager.AirTemperature);
    /// - rain is a camera-following particle box slanted by the wind, off inside a cave; it wets the player unless covered
    ///   and makes campfires burn faster; a storm or a long heavy rain (intensity 0.9 and up) puts an open fire out (Campfire);
    /// - wind strength follows the weather, storms gust (<see cref="WindDirection"/> / <see cref="WindStrength"/> for AI scent
    ///   and _PF_Wind for foliage / water, direction drifting slowly);
    /// - storms flash lightning with thunder distance / 343 s later (AmbienceManager plays it, SfxPlayer as a fallback);
    /// - surfaces soak in the rain and stay wet a while after it before drying (_PF_Wetness, read by ENV's shaders and
    ///   the player cloth).
    /// Global shader values: _PF_Wetness (0..1), _PF_Overcast (0..1), _PF_Wind (xy direction, z strength, w gust),
    /// _PF_StormK (0..1) and _PF_Flash (lightning 0..1) for the cloud layer (VFX/CloudVeil).
    /// Random weather can be disabled (tutorial) and forced (story: <see cref="SetWeather"/>). The exact plan (state, time
    /// left, rain kind, storm cooldown, wetness) is saved as the "weather" save section, so a storm stays a storm on load.
    /// </summary>
    public class WeatherManager : MonoBehaviour, ISaveSection
    {
        public static WeatherManager Instance { get; private set; }
        public WeatherState State => _plan != null ? _plan.State : WeatherState.Clear;
        /// <summary>rain 0..1 smoothed (rain 0.75, heavy rain 0.94, storm 1)</summary>
        public float Intensity { get; private set; }
        /// <summary>cloud cover 0..1 smoothed</summary>
        public float Overcast { get; private set; }
        /// <summary>surfaces 0..1: up in the rain, stays a while after it, then dries</summary>
        public float Wetness { get; private set; }
        /// <summary>0..1 how much of a storm is on now (darkness, wind, lightning), smoothed</summary>
        public float StormK { get; private set; }
        /// <summary>current gust 0..1 (as _PF_Wind.w)</summary>
        public float Gust { get; private set; }
        /// <summary>x fog density from the weather (1 = clear)</summary>
        public float FogMultiplier { get; private set; } = 1f;
        /// <summary>rough visibility 0..1 (1 = clear day) for AI / UI</summary>
        public float Visibility01 => 1f / Mathf.Max(1f, FogMultiplier);
        public bool IsStorm => State == WeatherState.Storm;
        /// <summary>raining at 0.9 or more (storm, or the heavy part of a heavy rain): open campfires go out</summary>
        public bool IsHeavyRain => Intensity >= 0.9f;
        public WeatherPlanner.RainKind RainKind => _plan != null ? _plan.Kind : WeatherPlanner.RainKind.None;
        /// <summary>in-game hours left of the current weather (-1 = until changed)</summary>
        public float HoursLeft => _plan == null || _plan.Until < 0 ? -1f : Mathf.Max(0f, (float)(_plan.Until - NowHours));
        /// <summary>in-game hours until a random storm may start again (0 = allowed)</summary>
        public float StormCooldownHours => _plan == null ? 0f : Mathf.Max(0f, (float)(_plan.NextStormAllowed - NowHours));

        [Tooltip("tuning asset (empty = Resources/WeatherConfig, or built-in defaults)")] public WeatherConfig config;
        public bool allowRandom = true;
        [Header("Wind direction (strength comes from WeatherConfig)")]
        public Vector2 windDirection = new Vector2(0.8f, 0.6f);
        [Tooltip("the wind slowly swings this many degrees either side of windDirection (0 = fixed)")] [Range(0, 180)] public float windDriftDegrees = 60f;
        [Tooltip("in-game hours for one slow swing of the wind direction")] [Min(0.5f)] public float windDriftHours = 8f;
        /// <summary>current wind direction (world xz, normalized): the way the wind blows, as _PF_Wind.xy; scent drifts along it</summary>
        public Vector2 WindDirection { get; private set; } = new Vector2(0.8f, 0.6f);
        /// <summary>current wind strength (calm 0.35 .. storm 1.4, gusts on top in storms), as _PF_Wind.z</summary>
        public float WindStrength { get; private set; } = 0.35f;
        [Header("Rain")]
        public Material rainMaterial;
        [Tooltip("drop budget of the rain box around the camera")] public int maxParticles = 2200;

        /// <summary>a lightning strike: distance in metres (thunder follows distance / 343 s later)</summary>
        public event Action<float> LightningStruck;
        public event Action<WeatherState> Changed;

        WeatherPlanner _plan; WeatherConfig _rulesFrom;
        ParticleSystem _rain; float _cloudTarget, _stormTarget; float _nextFlash = 5f, _nextSlant;
        Light _flash; float _flashT = -1f, _flashPeak, _thunderAt = -1f, _thunderMeters; float _gustTarget, _nextGust, _dryDelay;
        bool _registered; float _nextRegCheck;
        static readonly int WetId = Shader.PropertyToID("_PF_Wetness"), OvercastId = Shader.PropertyToID("_PF_Overcast"), WindId = Shader.PropertyToID("_PF_Wind"),
            StormId = Shader.PropertyToID("_PF_StormK"), FlashId = Shader.PropertyToID("_PF_Flash");

        public WeatherConfig Cfg => config ? config : WeatherConfig.Instance;
        static double NowHours => GameClock.Now / Math.Max(1.0, GameClock.SecondsPerHour);

        void Awake() { Instance = this; Plan(); }
        void OnEnable() { SaveSystem.RegisterSection(this); _registered = true; }
        void OnDisable() { if (_registered) { SaveSystem.UnregisterSection(this); _registered = false; } }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start() { BuildRain(); }

        /// <summary>the planner, with its rules copied from the current config (again when the config was swapped)</summary>
        WeatherPlanner Plan()
        {
            var c = Cfg;
            if (_plan != null && ReferenceEquals(_rulesFrom, c)) return _plan;
            _rulesFrom = c;
            var r = new WeatherPlanner.Rules
            {
                cloudChancePerHour = c.cloudChancePerHour, rainChancePerHour = c.rainChancePerHour, lightRainShare = c.lightRainShare,
                heavyRainShare = c.heavyRainShare, heavyRainBuild = c.heavyRainBuild, stormChance = c.stormChance,
                firstStormAfterHours = c.firstStormAfterHours, minHoursBetweenStorms = c.minHoursBetweenStorms,
                cloudMin = c.cloudHours.x, cloudMax = c.cloudHours.y, afterRainMin = c.afterRainCloudHours.x, afterRainMax = c.afterRainCloudHours.y,
                rainMin = c.rainHours.x, rainMax = c.rainHours.y, heavyMin = c.heavyRainHours.x, heavyMax = c.heavyRainHours.y,
                leadMin = c.stormLeadHours.x, leadMax = c.stormLeadHours.y, stormMin = c.stormHours.x, stormMax = c.stormHours.y,
                tailMin = c.stormTailHours.x, tailMax = c.stormTailHours.y,
                lightRain = c.lightRain, normalRain = c.normalRain, heavyRain = c.heavyRain, stormRain = c.stormRain,
            };
            if (_plan == null) _plan = new WeatherPlanner(r, () => UnityEngine.Random.value);
            else _plan.rules = r;
            return _plan;
        }

        void BuildRain()
        {
            var go = new GameObject("Rain"); go.transform.SetParent(transform, false);
            _rain = go.AddComponent<ParticleSystem>();
            _rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _rain.main; main.loop = true; main.startLifetime = 1.1f; main.startSpeed = 0f; main.startSize3D = true;
            main.startSizeX = 0.022f; main.startSizeY = 0.6f; main.startSizeZ = 1f; main.maxParticles = Mathf.Max(200, maxParticles);
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.startColor = new Color(0.78f, 0.83f, 0.9f, 0.32f);
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate; main.playOnAwake = false;
            var em = _rain.emission; em.rateOverTime = 0f;
            var sh = _rain.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(36f, 1f, 36f); sh.position = new Vector3(0, 11f, 0);
            var vel = _rain.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-1.2f, -0.6f); vel.y = new ParticleSystem.MinMaxCurve(-15f, -12f); vel.z = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
            var r = _rain.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.035f; r.lengthScale = 1f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            if (rainMaterial) r.sharedMaterial = rainMaterial;
            _rain.Play();
        }

        /// <summary>
        /// set the weather by hand (story, tests, title / new game). Rain = normal rain (0.75), Storm = full storm (1).
        /// hours &lt;= 0 = until changed (the random schedule never ends a hand-set rain or storm). instant = no easing.
        /// </summary>
        public void SetWeather(WeatherState s, float hours = -1f, bool instant = false)
        {
            Plan().Set(s, NowHours, hours);
            Targets();
            if (instant) Snap();
            Raise();
        }

        void Raise()
        {
            Changed?.Invoke(State);
            GameEvents.Raise(GameEventType.WeatherChanged, State.ToString(), 1);
        }

        void Targets()
        {
            var c = Cfg; var s = State;
            _cloudTarget = s == WeatherState.Clear ? 0f : s == WeatherState.Cloudy ? c.cloudyCover : s == WeatherState.Rain ? c.rainCover : c.stormCover;
            if (_plan.Kind == WeatherPlanner.RainKind.StormLead) _cloudTarget = Mathf.Lerp(c.rainCover, c.stormCover, 0.8f);
            _stormTarget = s == WeatherState.Storm ? 1f : 0f;
        }

        void Snap()
        {
            var c = Cfg;
            Intensity = _plan.TargetRain(NowHours); Overcast = _cloudTarget; StormK = _stormTarget;
            if (Intensity > 0f) { Wetness = Mathf.Max(Wetness, 0.8f); _dryDelay = c.dryDelaySeconds; }
        }

        void Update()
        {
            // a temporary second manager (tests) took the "weather" save key and left: take it back
            if (Instance == this && Time.unscaledTime >= _nextRegCheck)
            {
                _nextRegCheck = Time.unscaledTime + 5f;
                bool mine = false; var secs = SaveSystem.Sections;
                for (int i = 0; i < secs.Count; i++) if (ReferenceEquals(secs[i], this)) { mine = true; break; }
                if (!mine) { SaveSystem.RegisterSection(this); _registered = true; }
            }
            var c = Cfg; var plan = Plan();
            if (plan.Tick(NowHours, allowRandom)) { Targets(); Raise(); }
            float dt = Time.deltaTime;
            // rain intensity: towards the plan's target with a slow natural swell (heavy rain stays heavy, never below 0.9)
            float target = plan.TargetRain(NowHours);
            var kind = plan.Kind;
            if (target > 0f && (kind == WeatherPlanner.RainKind.Light || kind == WeatherPlanner.RainKind.Normal || kind == WeatherPlanner.RainKind.StormTail))
                target = Mathf.Clamp01(target + (Mathf.PerlinNoise((float)(NowHours * 0.9), 3.7f) * 2f - 1f) * c.rainSwell);
            Intensity = Mathf.MoveTowards(Intensity, target, dt / Mathf.Max(0.5f, c.rainRampSeconds));
            Overcast = Mathf.MoveTowards(Overcast, Mathf.Max(_cloudTarget, Intensity * c.rainCover), dt / Mathf.Max(0.5f, c.cloudRampSeconds));
            StormK = Mathf.MoveTowards(StormK, _stormTarget, dt / Mathf.Max(0.5f, c.stormRampSeconds));
            UpdateWetness(c, dt);

            // presentation
            var cam = Camera.main;
            bool indoor = cam && World.ZoneManager.Instance && World.ZoneManager.Instance.IsIndoor(cam.transform.position);
            // wind: calm, stronger with clouds and rain, strongest in storms with gusts on top
            float cloudK = Mathf.Clamp01(Overcast / Mathf.Max(0.01f, c.cloudyCover));
            if (Time.time > _nextGust)
            {
                var gs = StormK > 0.5f ? c.stormGustSeconds : c.gustSeconds;
                _nextGust = Time.time + UnityEngine.Random.Range(gs.x, gs.y);
                _gustTarget = StormK > 0.5f ? UnityEngine.Random.Range(0.3f, 1f) : UnityEngine.Random.Range(0f, 1f);
            }
            Gust = Mathf.MoveTowards(Gust, _gustTarget, dt * (0.4f + StormK * 0.8f));
            float wind = Mathf.Lerp(c.calmWind, c.cloudyWind, cloudK);
            wind = Mathf.Lerp(wind, c.rainWind, Mathf.Clamp01(Intensity));
            wind = Mathf.Lerp(wind, c.stormWind, StormK) + StormK * c.stormGust * Gust;
            WindStrength = Mathf.MoveTowards(WindStrength, wind, dt * 0.5f);
            var wd = DriftedWind();
            WindDirection = wd;
            if (_rain)
            {
                if (cam)
                {
                    // the box sits upwind so slanted drops still fall around the camera
                    Vector3 slant = new Vector3(wd.x, 0f, wd.y) * (WindStrength * c.rainWindSlant);
                    _rain.transform.position = cam.transform.position + cam.transform.forward * 6f - slant * 0.55f;
                    if (Time.time >= _nextSlant)
                    {
                        _nextSlant = Time.time + 0.25f;
                        var vel = _rain.velocityOverLifetime;
                        vel.x = new ParticleSystem.MinMaxCurve(slant.x * 0.8f - 0.3f, slant.x * 1.2f + 0.3f);
                        vel.z = new ParticleSystem.MinMaxCurve(slant.z * 0.8f - 0.3f, slant.z * 1.2f + 0.3f);
                        vel.y = new ParticleSystem.MinMaxCurve(-15f - StormK * 3f, -12f - StormK * 2f);
                    }
                }
                var em = _rain.emission; em.rateOverTime = indoor ? 0f : Intensity * maxParticles * c.dropsPerIntensity;
            }
            // light and visibility (TimeManager applies them to the sun, ambient, fog and sky)
            FogMultiplier = Fog(c, Overcast, Intensity, StormK);
            float flash = Lightning(c, dt);
            var tm = TimeManager.Instance;
            if (tm) PushLight(tm, c, Overcast, Intensity, StormK, flash);
            Shader.SetGlobalVector(WindId, new Vector4(wd.x, wd.y, WindStrength, Gust));
            Shader.SetGlobalFloat(WetId, Wetness);
            Shader.SetGlobalFloat(OvercastId, Overcast);
            Shader.SetGlobalFloat(StormId, StormK);
            Shader.SetGlobalFloat(FlashId, flash);
        }

        /// <summary>x fog density for a cloud cover / rain / storm amount</summary>
        public static float Fog(WeatherConfig c, float overcast, float rain, float stormK)
        {
            float cloudK = Mathf.Clamp01(overcast / Mathf.Max(0.01f, c.cloudyCover));
            return Mathf.Lerp(1f, c.fogCloudy, cloudK) * (1f + (c.fogRain - 1f) * Mathf.Clamp01(rain)) * Mathf.Lerp(1f, c.fogStorm, Mathf.Clamp01(stormK));
        }

        /// <summary>the weather part of the light (TimeManager applies it): also used by the editor previews</summary>
        public static void PushLight(TimeManager tm, WeatherConfig c, float overcast, float rain, float stormK, float flash = 0f)
        {
            float cloudK = Mathf.Clamp01(overcast / Mathf.Max(0.01f, c.cloudyCover));
            tm.overcast = overcast; tm.rain = rain; tm.storm = stormK;
            tm.sunMultiplier = Mathf.Lerp(1f, c.rainSun, rain) * Mathf.Lerp(1f, c.stormSun, stormK);
            tm.ambientMultiplier = Mathf.Lerp(1f, c.rainAmbient, rain) * Mathf.Lerp(1f, c.stormAmbient, stormK) + flash * 1.1f;
            tm.fogMultiplier = Fog(c, overcast, rain, stormK);
            tm.weatherGrey = Mathf.Max(cloudK * c.greyCloudy, Mathf.Max(rain * c.greyRain, stormK * c.greyStorm));
        }

        /// <summary>preview values of a state at full strength (cover, rain, storm): editor captures</summary>
        public static void Preview(WeatherState s, WeatherConfig c, out float overcast, out float rain, out float stormK)
        {
            overcast = s == WeatherState.Clear ? 0f : s == WeatherState.Cloudy ? c.cloudyCover : s == WeatherState.Rain ? c.rainCover : c.stormCover;
            rain = s == WeatherState.Rain ? c.normalRain : s == WeatherState.Storm ? c.stormRain : 0f;
            stormK = s == WeatherState.Storm ? 1f : 0f;
        }

        /// <summary>soak in the rain; after it, stay wet dryDelaySeconds, then dry (slower under cloud and at night)</summary>
        void UpdateWetness(WeatherConfig c, float dt)
        {
            if (Intensity > 0.15f)
            {
                // light rain leaves surfaces damp (lightRainWetness), normal rain and up soak them fully
                float cap = Intensity < c.lightRain ? Mathf.Lerp(c.lightRainWetness * 0.7f, c.lightRainWetness, Mathf.InverseLerp(0.15f, c.lightRain, Intensity))
                                                    : Mathf.Lerp(c.lightRainWetness, 1f, Mathf.InverseLerp(c.lightRain, (c.lightRain + c.normalRain) * 0.5f, Intensity));
                if (Wetness < cap) Wetness = Mathf.MoveTowards(Wetness, cap, dt * Intensity / Mathf.Max(1f, c.soakSeconds));
                _dryDelay = c.dryDelaySeconds;
            }
            else if (_dryDelay > 0f) _dryDelay -= dt;
            else if (Wetness > 0f)
            {
                var tm = TimeManager.Instance;
                float secs = Mathf.Lerp(c.drySecondsClear, c.drySecondsOvercast, Overcast) * (tm && tm.Daylight01 < 0.2f ? c.nightDrySlowdown : 1f);
                Wetness = Mathf.MoveTowards(Wetness, 0f, dt / Mathf.Max(1f, secs));
            }
        }

        /// <summary>storm lightning (returns the flash 0..1 this frame for the ambient boost)</summary>
        float Lightning(WeatherConfig c, float dt)
        {
            if (StormK > 0.5f && Intensity > 0.5f && Time.time > _nextFlash)
            {
                _nextFlash = Time.time + UnityEngine.Random.Range(c.lightningSeconds.x, c.lightningSeconds.y) / Mathf.Lerp(0.6f, 1f, StormK);
                bool near = UnityEngine.Random.value < c.nearStrikeShare;
                var r = near ? c.nearStrikeMeters : c.farStrikeMeters;
                float m = UnityEngine.Random.Range(r.x, r.y);
                _flashT = 0f; _flashPeak = Mathf.Lerp(1f, 0.3f, Mathf.InverseLerp(c.nearStrikeMeters.x, c.farStrikeMeters.y, m));
                _thunderAt = Time.time + Mathf.Min(11f, m / 343f); _thunderMeters = m;
                if (!_flash)
                {
                    var go = new GameObject("Lightning"); go.transform.SetParent(transform, false);
                    _flash = go.AddComponent<Light>(); _flash.type = LightType.Directional; _flash.shadows = LightShadows.None; _flash.enabled = false;
                }
                _flash.color = c.lightningColor;
                _flash.transform.rotation = Quaternion.Euler(UnityEngine.Random.Range(35f, 70f), UnityEngine.Random.Range(0f, 360f), 0f);
                LightningStruck?.Invoke(m);
            }
            float k = 0f;
            if (_flash && _flashT >= 0f)
            {
                _flashT += dt;
                // two quick pulses and a fade (~0.5 s)
                float t = _flashT;
                k = t < 0.06f ? t / 0.06f : t < 0.12f ? 1f - (t - 0.06f) / 0.06f * 0.7f : t < 0.19f ? 0.3f + (t - 0.12f) / 0.07f * 0.7f : Mathf.Max(0f, 1f - (t - 0.19f) / 0.3f);
                k *= _flashPeak;
                _flash.enabled = k > 0.01f; _flash.intensity = k * c.lightningIntensity;
                if (t > 0.5f) { _flashT = -1f; _flash.enabled = false; k = 0f; }
            }
            if (_thunderAt > 0f && Time.time >= _thunderAt)
            {
                _thunderAt = -1f;
                float vol = Mathf.Lerp(1f, 0.4f, Mathf.InverseLerp(c.nearStrikeMeters.x, c.farStrikeMeters.y, _thunderMeters));
                var amb = AmbienceManager.Instance;
                if (!amb || !amb.PlayThunder(_thunderMeters, vol)) SfxPlayer.Instance.Play2D(SfxId.Thunder, vol);
            }
            return k;
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

        // ------------------------------------------------------------------ save section "weather"
        [Serializable] class Saved { public string state; public int kind; public float hoursLeft = -1f, hoursDone, stormIn, intensity, overcast, storm, wetness; }
        public const string SaveKey = "weather";
        public string SectionKey => SaveKey;

        public string CaptureSection()
        {
            var p = Plan(); double now = NowHours;
            return JsonUtility.ToJson(new Saved
            {
                state = p.State.ToString(), kind = (int)p.Kind, hoursLeft = p.Until < 0 ? -1f : (float)Math.Max(0.05, p.Until - now), hoursDone = (float)Math.Max(0, now - p.Start),
                stormIn = StormCooldownHours, intensity = Intensity, overcast = Overcast, storm = StormK, wetness = Wetness,
            });
        }

        public void RestoreSection(string json)
        {
            var s = JsonUtility.FromJson<Saved>(json);
            if (s == null || !Enum.TryParse<WeatherState>(s.state, out var st)) return;
            var kind = Enum.IsDefined(typeof(WeatherPlanner.RainKind), s.kind) ? (WeatherPlanner.RainKind)s.kind : WeatherPlanner.RainKind.None;
            Plan().Restore(st, kind, NowHours, s.hoursLeft, s.hoursDone, s.stormIn);
            Targets();
            Intensity = Mathf.Clamp01(s.intensity); Overcast = Mathf.Clamp01(s.overcast); StormK = Mathf.Clamp01(s.storm); Wetness = Mathf.Clamp01(s.wetness);
            if (Intensity > 0.15f) _dryDelay = Cfg.dryDelaySeconds;
            Raise();
        }
    }
}
