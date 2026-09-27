using System;
using UnityEngine;
using PrimalFrontier.Audio;

namespace PrimalFrontier.Core
{
    public enum WeatherState { Clear, Rain, Storm }

    /// <summary>
    /// Clear / rain (and the opening storm). Rain is a camera-following particle box, dims the sun, thickens fog,
    /// cools the air, wets the player unless covered, and makes campfires burn fuel faster. Random rain can be
    /// disabled (tutorial) and forced (story).
    /// </summary>
    public class WeatherManager : MonoBehaviour
    {
        public static WeatherManager Instance { get; private set; }
        public WeatherState State { get; private set; } = WeatherState.Clear;
        public float Intensity { get; private set; }            // 0..1 smoothed
        public bool allowRandom = true;
        [Tooltip("chance per in-game hour that rain starts")] [Range(0, 1)] public float rainChancePerHour = 0.08f;
        public Vector2 rainHours = new Vector2(1f, 3f);
        public Material rainMaterial;
        public int maxParticles = 1400;

        ParticleSystem _rain; float _target; double _until = -1; double _nextRoll; float _nextThunder;
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
            _target = s == WeatherState.Clear ? 0f : s == WeatherState.Rain ? 0.75f : 1f;
            _until = hours > 0f ? GameClock.Now + GameClock.Hours(hours) : -1;
            if (instant) Intensity = _target;
            Changed?.Invoke(s);
            GameEvents.Raise(GameEventType.WeatherChanged, s.ToString(), 1);
        }

        void Update()
        {
            // schedule
            if (_until > 0 && GameClock.Now >= _until) SetWeather(WeatherState.Clear);
            if (allowRandom && State == WeatherState.Clear && GameClock.Now >= _nextRoll)
            {
                _nextRoll = GameClock.Now + GameClock.Hours(1f);
                if (UnityEngine.Random.value < rainChancePerHour) SetWeather(WeatherState.Rain, UnityEngine.Random.Range(rainHours.x, rainHours.y));
            }
            Intensity = Mathf.MoveTowards(Intensity, _target, Time.deltaTime / 12f);

            // presentation
            var cam = Camera.main;
            if (_rain)
            {
                if (cam) _rain.transform.position = cam.transform.position + cam.transform.forward * 6f;
                var em = _rain.emission; em.rateOverTime = Intensity * maxParticles * 0.9f;
            }
            var tm = TimeManager.Instance; if (tm) tm.overcast = Intensity;
            if (State == WeatherState.Storm && Time.time > _nextThunder)
            {
                _nextThunder = Time.time + UnityEngine.Random.Range(6f, 16f);
                SfxPlayer.Instance.Play2D(SfxId.Thunder, UnityEngine.Random.Range(0.5f, 1f));
            }
        }

        public bool RainingAt(Vector3 p) => Intensity > 0.25f && !World.Shelter.Covers(p) && !(World.ZoneManager.Instance && World.ZoneManager.Instance.IsIndoor(p));
    }
}
