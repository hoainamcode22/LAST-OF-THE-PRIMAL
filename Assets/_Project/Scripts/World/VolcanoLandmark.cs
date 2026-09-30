using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using PrimalFrontier.Audio;
using PrimalFrontier.Core;

namespace PrimalFrontier.World
{
    /// <summary>
    /// The smoking mountain across the sea (scenery, not reachable). Drives its effects: the smoke plume drifts with the
    /// weather wind, the crater glow and embers show at dusk and night, and every few minutes the mountain rumbles
    /// (a low distant boom, a faint camera tremor and a darker puff of smoke). All particle systems are children
    /// placed by the builder; move / retune them in the scene.
    /// Built here at run time (no prefab change needed): ash fall, small grey flakes drifting down around the camera
    /// (one pooled, prewarmed system, at most 400 flakes) with a strength from the flat distance of the player to the
    /// mountain, more downwind and after a rumble, less in the rain, none indoors; and the heat shimmer, a few faint
    /// PF/Heat Shimmer billboards over the crater and the lava flow (only when <see cref="hazeMaterial"/> is set, by
    /// bridge PrimalVolcanoBuilder.Atmosphere). Switch either off with <see cref="ashFall"/> / <see cref="heatHaze"/>.
    /// Eruptions (Phase 1 wave 2a, owner decision: the offshore mountain is THE volcano): every
    /// <see cref="eruptionEvery"/> play seconds a rumble builds up (sound + growing tremor), then the crater bursts: fire
    /// fountain, light flash, dark smoke puffs, a boom that arrives after distance / speed of sound, and
    /// <see cref="bombCount"/> pooled glowing bombs on ballistic arcs toward the island. Most fall in the sea (splash, steam,
    /// hiss), <see cref="bombShoreShare"/> land on the volcano-facing shore (dust, embers, flames and a light that fade, a
    /// dark rock that sinks away after <see cref="bombStayTime"/>). A bomb landing within <see cref="bombDamageRadius"/> of
    /// the player deals <see cref="bombDamage"/> (0 = harmless). No terrain change. Children and the bomb prefab come from
    /// bridge PrimalVolcanoBuilder.Eruption. The copy ENV put on the island vent (Lava/VentSystem) is switched off.
    /// </summary>
    public class VolcanoLandmark : MonoBehaviour
    {
        public ParticleSystem smoke, embers, glow;
        [Tooltip("small halos along the lava flow (night)")] public ParticleSystem[] flowGlows = new ParticleSystem[0];
        public Renderer lava;
        [Tooltip("real seconds between rumbles")] public Vector2 rumbleEvery = new Vector2(240f, 540f);
        [Range(0, 1)] public float rumbleVolume = 0.4f;
        public Color lavaEmission = new Color(4f, 1.1f, 0.15f);
        public float embersNight = 10f, embersDay = 1.5f;
        [Tooltip("how strongly the weather wind pushes the smoke (7 for the big mountain, less for a small vent)")] public float smokeWind = 7f;
        [Tooltip("size (m) of the dark smoke puffs a rumble adds")] public float rumblePuffSize = 150f;
        [Header("Ash fall (around the player, stronger near the mountain)")]
        public bool ashFall = true;
        [Tooltip("alpha-blended flake material (M_VFX_Chip); empty = the smoke plume's material")] public Material ashMaterial;
        [Tooltip("flat distance (m) from the player to the mountain with the most ash")] public float ashFullDistance = 420f;
        [Tooltip("flat distance (m) where the ash starts")] public float ashStartDistance = 900f;
        [Range(0, 400)] public int ashMaxParticles = 320;
        [Tooltip("flakes per second at full strength")] public float ashRate = 40f;
        [Header("Heat shimmer (over the crater and the lava)")]
        public bool heatHaze = true;
        [Tooltip("PF/Heat Shimmer material (M_VFX_HeatShimmer); empty = no shimmer")] public Material hazeMaterial;
        [Tooltip("crater column width / height (m)")] public Vector2 hazeSize = new Vector2(120f, 170f);

        [Header("Eruption (the offshore volcano): rumble build-up, fire fountain, smoke puff, glowing bombs toward the island")]
        [Tooltip("off = no eruptions (the island vent copy has it off)")] public bool eruptions = true;
        [Tooltip("play seconds between eruptions (random in the range)")] public Vector2 eruptionEvery = new Vector2(120f, 360f);
        [Tooltip("play seconds before the first eruption")] public Vector2 firstEruption = new Vector2(90f, 200f);
        [Tooltip("seconds of rumble and growing tremor before the burst")] public float rumbleBuildUp = 6f;
        [Tooltip("seconds the fire fountain runs")] public float fountainTime = 4.5f;
        [Tooltip("lava / fire fountain from the crater (emission 0 between eruptions)")] public ParticleSystem fountain;
        [Tooltip("fountain particles per second during a burst")] public float fountainRate = 70f;
        [Tooltip("point light over the crater, flashes during a burst")] public Light flash;
        public float flashIntensity = 12000f;
        [Tooltip("dark smoke puffs a burst adds to the plume")] public int plumePuffs = 6;
        public float plumePuffSize = 190f;
        [Header("Volcanic bombs (pooled, ballistic arcs toward the island)")]
        [Tooltip("PFB_ENV_VolcanicBomb: children Rock (emissive), FireTrail, SmokeTrail, Flames, Glow (light)")] public GameObject bombPrefab;
        [Tooltip("bombs per eruption (the pool holds the max, at most 16)")] public Vector2Int bombCount = new Vector2Int(3, 12);
        [Tooltip("part of the bombs aimed at the volcano-facing shore / slopes (the rest fall in the sea)")] [Range(0, 1)] public float bombShoreShare = 0.3f;
        [Tooltip("seconds in the air")] public Vector2 bombFlightTime = new Vector2(11f, 15f);
        [Tooltip("sideways spread (m) of the aim points across the island")] public float bombLateralSpread = 170f;
        [Tooltip("sea bombs land this far (m) off the shore")] public Vector2 bombSeaOffshore = new Vector2(25f, 260f);
        [Tooltip("shore bombs land up to this far (m) inland")] public float bombInland = 45f;
        [Tooltip("rock diameter (m)")] public Vector2 bombSize = new Vector2(1.2f, 2.4f);
        [Tooltip("seconds a landed rock glows and burns before it is a dark cooling rock")] public float bombGlowTime = 9f;
        [Tooltip("seconds a landed rock stays before it sinks away")] public float bombStayTime = 60f;
        public Color bombHot = new Color(6f, 1.6f, 0.25f);
        [Tooltip("no bomb lands within this flat distance (m) of the player spawn (the start beach stays safe); 0 = off")] public float spawnSafeRadius = 120f;
        [Tooltip("fallback spawn position when the scene has no GameManager.spawnPoint")] public Vector3 spawnSafeCentre = new Vector3(-20f, 1.5f, 211f);
        [Tooltip("damage to the player from a bomb landing within bombDamageRadius; 0 = harmless")] public float bombDamage = 25f;
        public float bombDamageRadius = 2.5f;
        [Tooltip("camera shake from an impact within this distance (m)")] public float bombShakeRadius = 45f;
        [Tooltip("burst boom volume (falls off with distance, arrives after distance / speed of sound)")] [Range(0, 1)] public float boomVolume = 0.9f;
        [Tooltip("a bomb passing within this distance (m) of the camera whooshes")] public float whooshDistance = 40f;
        [Tooltip("impact thud / splash / hiss audible up to this distance (m)")] public float impactHearing = 260f;
        public float speedOfSound = 343f;
        [Tooltip("loudness (x hearing range) of the noise stimulus a bomb landing on the island raises for wildlife; 0 = none")] public float impactNoise = 2.5f;

        float _nextRumble; MaterialPropertyBlock _mpb; ParticleSystem.Particle[] _glowP;
        ParticleSystem _ash, _haze; float _ashK, _ashTarget, _nextAshCheck = -1f, _ashBoostUntil = -1f;
        static readonly int WindId = Shader.PropertyToID("_PF_Wind"), EmissionId = Shader.PropertyToID("_EmissionColor");

        void Start()
        {
            _nextRumble = Time.time + Random.Range(90f, 200f);
            _mpb = new MaterialPropertyBlock();
            if (ashFall) BuildAsh();
            if (heatHaze && hazeMaterial) BuildHaze();
            if (eruptions) InitEruption();
        }

        void Update()
        {
            var tm = TimeManager.Instance;
            float day = tm ? tm.Daylight01 : 1f;                 // 0 night .. 1 full day
            float night = 1f - Mathf.Clamp01(day * 1.4f);
            // smoke drifts with the wind (stronger up high)
            if (smoke)
            {
                Vector4 w = Shader.GetGlobalVector(WindId);
                float s = (w.z > 0f ? w.z : 0.4f) * smokeWind;
                var v = smoke.velocityOverLifetime;
                v.x = new ParticleSystem.MinMaxCurve(w.x * s * 0.6f, w.x * s * 1.3f);
                v.z = new ParticleSystem.MinMaxCurve(w.y * s * 0.6f, w.y * s * 1.3f);
            }
            UpdateEruption(Time.time);
            if (embers) { var em = embers.emission; em.rateOverTime = Mathf.Lerp(embersDay, embersNight, night); }
            // long-lived halo particles over the crater and along the flow: faint by day, strong at night
            Halo(glow, Mathf.Max(Mathf.Lerp(0.05f, 0.38f, night), _flashK * 0.8f));
            foreach (var g in flowGlows) Halo(g, Mathf.Lerp(0.0f, 0.2f, night));
            if (lava)
            {
                lava.GetPropertyBlock(_mpb);
                float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 0.7f) * Mathf.Sin(Time.time * 1.9f);
                _mpb.SetColor(EmissionId, lavaEmission * pulse * Mathf.Lerp(0.6f, 1.3f, night) * (1f + 1.5f * _flashK));
                lava.SetPropertyBlock(_mpb);
            }
            UpdateAsh(day);
            if (Time.time >= _nextRumble) Rumble();
        }

        // ------------------------------------------------------------------ ash fall
        void BuildAsh()
        {
            var mat = ashMaterial;
            if (!mat && smoke) { var sr = smoke.GetComponent<ParticleSystemRenderer>(); if (sr) mat = sr.sharedMaterial; }
            if (!mat) return;
            var go = new GameObject("Ash_Fall");
            go.transform.SetParent(transform, false);
            var cam = Camera.main;
            go.transform.SetPositionAndRotation(cam ? cam.transform.position : transform.position, Quaternion.identity);
            _ash = go.AddComponent<ParticleSystem>();
            _ash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = _ash.main;
            m.loop = true; m.duration = 10f; m.playOnAwake = false;
            m.simulationSpace = ParticleSystemSimulationSpace.World; m.scalingMode = ParticleSystemScalingMode.Local;
            m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            m.maxParticles = Mathf.Clamp(ashMaxParticles, 0, 400);
            m.startLifetime = new ParticleSystem.MinMaxCurve(8f, 12f); m.startSpeed = 0f;
            m.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.09f);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.startColor = AshColor(1f);
            var sh = _ash.shape;
            sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(70f, 1f, 70f); sh.position = new Vector3(0f, 14f, 0f);
            var vel = _ash.velocityOverLifetime;                           // all three axes in the same curve mode
            vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f); vel.y = new ParticleSystem.MinMaxCurve(-2f, -1.1f); vel.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            var noise = _ash.noise;
            noise.enabled = true; noise.strength = 0.35f; noise.frequency = 0.25f; noise.scrollSpeed = 0.2f; noise.quality = ParticleSystemNoiseQuality.Low;
            var rot = _ash.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
            var col = _ash.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat; r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.maxParticleSize = 0.02f;                                     // a flake next to the lens never fills the screen
            // prewarmed at the current strength, so a game loaded near the mountain starts with ash in the air
            _ashK = _ashTarget = cam ? AshTarget(cam.transform.position) : 0f;
            var em = _ash.emission; em.rateOverTime = _ashK * ashRate;
            m.prewarm = _ashK > 0.01f;
            _ash.Play();
        }

        static ParticleSystem.MinMaxGradient AshColor(float light) =>
            new ParticleSystem.MinMaxGradient(new Color(0.33f * light, 0.32f * light, 0.31f * light, 0.85f), new Color(0.55f * light, 0.53f * light, 0.5f * light, 0.75f));

        void UpdateAsh(float day)
        {
            if (!_ash) return;
            var cam = Camera.main; if (!cam) return;
            Vector3 c = cam.transform.position;
            _ash.transform.position = c;
            if (Time.time >= _nextAshCheck)
            {
                _nextAshCheck = Time.time + 0.5f;
                _ashTarget = AshTarget(c);
                Vector4 w = Shader.GetGlobalVector(WindId);
                float s = (w.z > 0f ? w.z : 0.35f) * 1.6f;
                var v = _ash.velocityOverLifetime;
                v.x = new ParticleSystem.MinMaxCurve(w.x * s - 0.3f, w.x * s + 0.3f);
                v.z = new ParticleSystem.MinMaxCurve(w.y * s - 0.3f, w.y * s + 0.3f);
                var m = _ash.main; m.startColor = AshColor(Mathf.Lerp(0.3f, 1f, day));   // grey flakes, dim at night
            }
            _ashK = Mathf.MoveTowards(_ashK, _ashTarget, Time.deltaTime * 0.15f);
            var em = _ash.emission; em.rateOverTime = _ashK * ashRate;
        }

        /// <summary>0..1: flat distance to the mountain, wind (more downwind), rain (washes it out), indoors (none), rumble</summary>
        float AshTarget(Vector3 camPos)
        {
            Vector3 p = PlayerLocator.Player ? PlayerLocator.Player.position : camPos;
            Vector3 d = p - transform.position; d.y = 0f;
            float dist = d.magnitude;
            float k = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ashFullDistance, ashStartDistance, dist));
            if (k <= 0f) return 0f;
            Vector4 w = Shader.GetGlobalVector(WindId);
            var wd = new Vector2(w.x, w.y);
            if (wd.sqrMagnitude > 0.01f && dist > 1f) k *= Mathf.Lerp(0.45f, 1f, Vector2.Dot(wd.normalized, new Vector2(d.x, d.z) / dist) * 0.5f + 0.5f);
            var wm = WeatherManager.Instance; if (wm) k *= 1f - 0.75f * wm.Intensity;
            if (Time.time < _ashBoostUntil) k = Mathf.Min(1f, k * 1.8f + 0.1f);
            var zm = ZoneManager.Instance; if (zm && zm.IsIndoor(camPos)) k = 0f;
            return Mathf.Clamp01(k);
        }

        // ------------------------------------------------------------------ heat shimmer
        void BuildHaze()
        {
            var go = new GameObject("Heat_Shimmer");
            go.transform.SetParent(transform, false);
            _haze = go.AddComponent<ParticleSystem>();
            _haze.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = _haze.main;
            m.loop = true; m.duration = 10f; m.playOnAwake = false; m.maxParticles = 12;
            m.simulationSpace = ParticleSystemSimulationSpace.World; m.scalingMode = ParticleSystemScalingMode.Local;
            m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            m.startLifetime = 1e6f; m.startSpeed = 0f; m.startSize3D = true;
            var em = _haze.emission; em.rateOverTime = 0f;
            var sh = _haze.shape; sh.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = hazeMaterial; r.renderMode = ParticleSystemRenderMode.VerticalBillboard;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.sortingFudge = 12f;
            r.SetActiveVertexStreams(new List<ParticleSystemVertexStream> { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV, ParticleSystemVertexStream.StableRandomX });
            _haze.Play();
            Vector3 top = smoke ? smoke.transform.position : transform.position + Vector3.up * 200f;
            EmitHaze(top + Vector3.up * (hazeSize.y * 0.4f), hazeSize, 0.9f);
            EmitHaze(top + new Vector3(hazeSize.x * 0.25f, hazeSize.y * 0.3f, -hazeSize.x * 0.15f), hazeSize * 0.7f, 0.6f);
            int n = 2;
            for (int i = 0; flowGlows != null && i < flowGlows.Length && n < m.maxParticles; i += 2)
                if (flowGlows[i]) { EmitHaze(flowGlows[i].transform.position + Vector3.up * 18f, new Vector2(45f, 55f), 0.5f); n++; }
        }

        void EmitHaze(Vector3 pos, Vector2 size, float alpha)
        {
            var p = new ParticleSystem.EmitParams
            {
                position = pos, applyShapeToPosition = false, velocity = Vector3.zero, startLifetime = 1e6f,
                startSize3D = new Vector3(size.x, size.y, 1f), startColor = new Color(1f, 1f, 1f, alpha)
            };
            _haze.Emit(p, 1);
        }

        void Halo(ParticleSystem ps, float alpha)
        {
            if (!ps) return;
            if (_glowP == null) _glowP = new ParticleSystem.Particle[1];
            if (ps.GetParticles(_glowP) > 0)
            {
                var c = (Color)_glowP[0].startColor; c.a = alpha;
                _glowP[0].startColor = c; ps.SetParticles(_glowP, 1);
            }
        }

        public void Rumble()
        {
            _nextRumble = Time.time + Random.Range(rumbleEvery.x, rumbleEvery.y);
            var a = SfxPlayer.Instance.Play(SfxId.Thunder, Camera.main ? Camera.main.transform.position : transform.position, rumbleVolume, 0f);
            if (a) a.pitch = 0.5f;
            var cam = Camera.main ? Camera.main.GetComponent<Player.ThirdPersonCamera>() : null;
            if (cam) cam.AddShake(0.012f, 1.6f);
            if (smoke)
            {
                var p = new ParticleSystem.EmitParams { startColor = new Color(0.12f, 0.11f, 0.11f, 0.7f), startSize = rumblePuffSize, startLifetime = Mathf.Clamp(rumblePuffSize / 3f, 8f, 50f) };
                smoke.Emit(p, 3);
            }
            if (embers) embers.Emit(25);
            _ashBoostUntil = Time.time + 60f;                              // a rumble puts more ash in the air for a minute
            _nextAshCheck = -1f;
            GameEvents.Raise(GameEventType.VolcanoRumble, "volcano", 1, transform.position);
        }
        // ------------------------------------------------------------------ eruption
        class Bomb
        {
            public GameObject go; public Transform t, rock; public Renderer rend; public ParticleSystem fireTrail, smokeTrail, flames; public Light light;
            public int state;            // 0 free, 1 waiting to launch, 2 flying, 3 on the ground (cooling), 4 in the sea (trails fading)
            public float launchAt, flight, t0, landedAt, size, lightBase, heat = -1f;
            public Vector3 p0, v0, spin; public bool whooshed;
        }
        struct Delayed { public bool used; public SfxId id; public Vector3 pos; public float at, vol, pitch, spatial, maxDist, shake; }

        Bomb[] _bombs; readonly Delayed[] _delayed = new Delayed[24];
        MaterialPropertyBlock _bombMpb;
        [System.NonSerialized] Terrain[] _terrains; [System.NonSerialized] float _sea; [System.NonSerialized] Vector3 _islandCentre, _safe;   // not kept over a script reload
        int _phase; float _phaseEnd, _nextEruption, _nextTremor, _flashK;
        Player.PlayerHealth _health; Transform _healthOf; Player.ThirdPersonCamera _tpc;
        static readonly Vector3 Gravity = new Vector3(0f, -9.81f, 0f);

        /// <summary>true from the rumble build-up to the end of the fountain</summary>
        public bool Erupting => _phase != 0;
        /// <summary>bombs waiting, flying or lying on the ground</summary>
        public int BombsActive { get { int n = 0; if (_bombs != null) foreach (var b in _bombs) if (b.state != 0) n++; return n; } }

        void InitEruption()
        {
            _nextEruption = Time.time + Random.Range(firstEruption.x, firstEruption.y);
            if (fountain) { var em = fountain.emission; em.rateOverTime = 0f; }
            if (flash) flash.enabled = false;
            _bombMpb = new MaterialPropertyBlock();
            int n = bombPrefab ? Mathf.Clamp(bombCount.y, 1, 16) : 0;
            _bombs = new Bomb[n];
            if (n == 0) return;
            var holder = new GameObject("Eruption_Bombs").transform;
            holder.SetParent(transform, false);
            for (int i = 0; i < n; i++)
            {
                var go = Instantiate(bombPrefab, holder); go.name = "Bomb_" + i; go.SetActive(false);
                var b = new Bomb { go = go, t = go.transform, rock = go.transform.Find("Rock") };
                b.rend = b.rock ? b.rock.GetComponent<Renderer>() : null;
                b.fireTrail = Part(go, "FireTrail"); b.smokeTrail = Part(go, "SmokeTrail"); b.flames = Part(go, "Flames");
                var lt = go.transform.Find("Glow"); b.light = lt ? lt.GetComponent<Light>() : null;
                if (b.light) { b.lightBase = b.light.intensity; b.light.enabled = false; }
                _bombs[i] = b;
            }
        }

        static ParticleSystem Part(GameObject go, string name) { var t = go.transform.Find(name); return t ? t.GetComponent<ParticleSystem>() : null; }

        /// <summary>start an eruption now (build-up first); debug / tests</summary>
        public void Erupt() { if (_bombs == null) InitEruption(); if (_phase == 0) StartBuildUp(Time.time); }

        void UpdateEruption(float now)
        {
            if (_bombs == null) return;                                    // eruptions were off at Start
            if (_phase == 0 && eruptions && now >= _nextEruption) StartBuildUp(now);
            else if (_phase == 1)
            {
                if (now >= _nextTremor)
                {
                    _nextTremor = now + 0.45f;
                    float k = 1f - Mathf.Clamp01((_phaseEnd - now) / Mathf.Max(0.1f, rumbleBuildUp));
                    Shake(Mathf.Lerp(0.003f, 0.014f, k) * Proximity(), 0.7f);
                }
                if (now >= _phaseEnd) Burst(now);
            }
            else if (_phase == 2 && now >= _phaseEnd) EndBurst(now);
            _flashK = _phase == 2 ? Mathf.MoveTowards(_flashK, 0.55f + 0.45f * Mathf.PerlinNoise(now * 3f, 0.3f), Time.deltaTime * 6f)
                                  : Mathf.MoveTowards(_flashK, 0f, Time.deltaTime * 0.5f);
            if (flash) { bool on = _flashK > 0.01f; if (flash.enabled != on) flash.enabled = on; if (on) flash.intensity = flashIntensity * _flashK; }
            for (int i = 0; i < _bombs.Length; i++) UpdateBomb(_bombs[i], now);
            UpdateDelayed(now);
        }

        void StartBuildUp(float now)
        {
            _phase = 1; _phaseEnd = now + rumbleBuildUp; _nextTremor = now;
            var a = SfxPlayer.Instance.Play(SfxId.Thunder, CamPos(), Mathf.Clamp01(rumbleVolume * 1.4f), 0f);
            if (a) a.pitch = 0.42f;
            if (smoke)
            {
                var p = new ParticleSystem.EmitParams { startColor = new Color(0.2f, 0.19f, 0.18f, 0.6f), startSize = plumePuffSize * 0.6f, startLifetime = 30f };
                smoke.Emit(p, 2);
            }
            if (embers) embers.Emit(30);
        }

        void Burst(float now)
        {
            _phase = 2; _phaseEnd = now + fountainTime; _flashK = 1.4f;
            if (fountain) { var em = fountain.emission; em.rateOverTime = fountainRate; fountain.Emit(Mathf.RoundToInt(fountainRate * 0.6f)); if (!fountain.isPlaying) fountain.Play(); }
            if (smoke)
            {
                var p = new ParticleSystem.EmitParams { startColor = new Color(0.1f, 0.09f, 0.09f, 0.75f), startSize = plumePuffSize, startLifetime = Mathf.Clamp(plumePuffSize / 3.5f, 10f, 55f) };
                smoke.Emit(p, Mathf.Max(0, plumePuffs));
            }
            if (embers) embers.Emit(60);
            // the boom arrives after distance / speed of sound, like thunder
            float d = Vector3.Distance(CamPos(), Crater()), delay = d / Mathf.Max(1f, speedOfSound);
            float vol = boomVolume * Mathf.Lerp(1f, 0.35f, Mathf.InverseLerp(250f, 1400f, d));
            Queue(SfxId.Thunder, Vector3.zero, now + delay, vol, 0.72f, 0f, 0f, 0.022f * Proximity());
            Queue(SfxId.DinoStepHeavy, Vector3.zero, now + delay + 0.05f, vol * 0.8f, 0.45f, 0f, 0f, 0f);
            int want = Mathf.Clamp(Random.Range(bombCount.x, bombCount.y + 1), 0, _bombs.Length), got = 0;
            for (int i = 0; i < _bombs.Length && got < want; i++)
            {
                var b = _bombs[i]; if (b.state != 0) continue;
                b.state = 1; b.launchAt = now + Random.Range(0f, fountainTime * 0.6f); got++;
            }
            _ashBoostUntil = now + 90f; _nextAshCheck = -1f;
            _nextRumble = Mathf.Max(_nextRumble, now + 60f);
            GameEvents.Raise(GameEventType.VolcanoRumble, "volcano", 2, transform.position);
        }

        void EndBurst(float now)
        {
            _phase = 0;
            if (fountain) { var em = fountain.emission; em.rateOverTime = 0f; }
            _nextEruption = now + Random.Range(eruptionEvery.x, eruptionEvery.y);
        }

        void Launch(Bomb b, float now)
        {
            var target = PickBombTarget(Random.value < bombShoreShare);
            b.p0 = Crater() + new Vector3(Random.Range(-12f, 12f), 8f, Random.Range(-12f, 12f));
            b.flight = Random.Range(bombFlightTime.x, bombFlightTime.y);
            b.v0 = (target - b.p0 - 0.5f * b.flight * b.flight * Gravity) / b.flight;
            b.t0 = now; b.size = Random.Range(bombSize.x, bombSize.y); b.whooshed = false; b.state = 2;
            b.spin = Random.insideUnitSphere * 240f;
            b.t.SetPositionAndRotation(b.p0, Random.rotation);
            if (b.rock) b.rock.localScale = Vector3.one * b.size;
            if (b.rend) b.rend.enabled = true;
            SetHeat(b, 1f);
            if (b.light) b.light.enabled = false;
            b.go.SetActive(true);
            if (b.flames) b.flames.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (b.fireTrail) { b.fireTrail.Clear(true); b.fireTrail.Play(true); }
            if (b.smokeTrail) { b.smokeTrail.Clear(true); b.smokeTrail.Play(true); }
        }

        void UpdateBomb(Bomb b, float now)
        {
            switch (b.state)
            {
                case 1: if (now >= b.launchAt) Launch(b, now); break;
                case 2:
                {
                    float t = now - b.t0; bool end = t >= b.flight; if (end) t = b.flight;
                    Vector3 p = b.p0 + b.v0 * t + 0.5f * t * t * Gravity;
                    b.t.position = p;
                    if (b.rock) b.rock.Rotate(b.spin * Time.deltaTime, Space.Self);
                    float gy = Ground(p);
                    bool overSea = gy < _sea;
                    if (end || (!overSea && p.y <= gy) || (overSea && p.y <= _sea)) { Impact(b, p, gy, now); break; }
                    if (!b.whooshed && (p - CamPos()).sqrMagnitude < whooshDistance * whooshDistance)
                    {
                        b.whooshed = true;
                        var a = SfxPlayer.Instance.Play(SfxId.SpearWhoosh, p, 1f, 1f);
                        if (a) { a.pitch = 0.35f; a.maxDistance = whooshDistance * 1.5f; }
                    }
                    break;
                }
                case 3:
                {
                    float age = now - b.landedAt, heat = 1f - Mathf.Clamp01(age / Mathf.Max(0.1f, bombGlowTime));
                    SetHeat(b, heat * heat);
                    if (b.light && b.light.enabled)
                    {
                        if (heat <= 0f) b.light.enabled = false;
                        else b.light.intensity = b.lightBase * heat * (0.7f + 0.3f * Mathf.PerlinNoise(now * 7f, b.size * 13f));
                    }
                    if (b.flames && b.flames.isEmitting && age > bombGlowTime * 0.6f) b.flames.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    if (age > bombStayTime)
                    {
                        b.t.position += Vector3.down * (b.size * 0.3f * Time.deltaTime);   // sinks into the ground over ~4 s
                        if (age > bombStayTime + 4f) Free(b);
                    }
                    break;
                }
                case 4: if (now - b.landedAt > 6f) Free(b); break;
            }
        }

        void Impact(Bomb b, Vector3 p, float groundY, float now)
        {
            bool sea = groundY < _sea;
            p.y = sea ? _sea : groundY;
            b.landedAt = now;
            if (b.fireTrail) b.fireTrail.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (b.smokeTrail) b.smokeTrail.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            float dc = Vector3.Distance(CamPos(), p);
            var fx = VFX.VfxPool.Instance;
            if (sea)
            {
                b.state = 4; b.t.position = p;
                if (b.rend) b.rend.enabled = false;
                fx.Play(VFX.VfxId.WaterSplash, p, Vector3.up, null, 3.5f);
                fx.Play(VFX.VfxId.Steam, p + Vector3.up * 0.5f, Vector3.up, null, 3f);
                QueueImpact(SfxId.WaterSplash, p, dc, 1f, 0.55f, now);
                QueueImpact(SfxId.FireHiss, p, dc, 0.9f, 0.7f, now);
            }
            else
            {
                b.state = 3; b.t.position = p - Vector3.up * (b.size * 0.25f);
                fx.Play(VFX.VfxId.DustImpact, p, Vector3.up, null, 3f);
                fx.Play(VFX.VfxId.CraftSparks, p + Vector3.up * 0.3f, Vector3.up, null, 2.5f);
                fx.Play(VFX.VfxId.FireIgnite, p, Vector3.up, null, 1.5f);
                if (b.flames) b.flames.Play(true);
                if (b.light) { b.light.intensity = b.lightBase; b.light.enabled = true; }
                QueueImpact(SfxId.DinoStepHeavy, p, dc, 1f, 0.6f, now);
                QueueImpact(SfxId.StoneHit, p, dc, 0.8f, 0.5f, now);
                if (impactNoise > 0f) Stimuli.Noise(p, impactNoise, NoiseTag.Impact, StimulusSource.World);
            }
            if (dc < bombShakeRadius) Shake(Mathf.Lerp(0.06f, 0.006f, dc / bombShakeRadius), 0.5f);
            HitPlayer(p);
        }

        void HitPlayer(Vector3 p)
        {
            if (bombDamage <= 0f) return;
            var pl = PlayerLocator.Player; if (!pl) return;
            if ((pl.position - p).sqrMagnitude > bombDamageRadius * bombDamageRadius) return;
            if (_healthOf != pl)
            {
                _healthOf = pl; _health = pl.GetComponentInParent<Player.PlayerHealth>();
                if (!_health) _health = pl.GetComponentInChildren<Player.PlayerHealth>();
            }
            if (_health) _health.TakeDamage(bombDamage, p, true);
        }

        void Free(Bomb b)
        {
            b.state = 0;
            if (b.light) b.light.enabled = false;
            b.go.SetActive(false);
        }

        void SetHeat(Bomb b, float k)
        {
            if (!b.rend || Mathf.Abs(k - b.heat) < 0.01f) return;
            b.heat = k;
            b.rend.GetPropertyBlock(_bombMpb);
            _bombMpb.SetColor(EmissionId, bombHot * k);
            b.rend.SetPropertyBlock(_bombMpb);
        }

        void QueueImpact(SfxId id, Vector3 p, float dist, float vol, float pitch, float now)
        {
            if (dist > impactHearing) return;
            Queue(id, p, now + dist / Mathf.Max(1f, speedOfSound), vol, pitch, 1f, impactHearing, 0f);
        }

        /// <summary>a sound (spatial 0 = at the camera) and / or a camera shake at a later time; fixed ring, no allocation</summary>
        void Queue(SfxId id, Vector3 pos, float at, float vol, float pitch, float spatial, float maxDist, float shake)
        {
            for (int i = 0; i < _delayed.Length; i++)
                if (!_delayed[i].used)
                {
                    _delayed[i] = new Delayed { used = true, id = id, pos = pos, at = at, vol = vol, pitch = pitch, spatial = spatial, maxDist = maxDist, shake = shake };
                    return;
                }
        }

        void UpdateDelayed(float now)
        {
            for (int i = 0; i < _delayed.Length; i++)
            {
                if (!_delayed[i].used || now < _delayed[i].at) continue;
                var d = _delayed[i]; _delayed[i].used = false;
                if (d.id != SfxId.None)
                {
                    var a = SfxPlayer.Instance.Play(d.id, d.spatial > 0f ? d.pos : CamPos(), d.vol, d.spatial);
                    if (a) { a.pitch = d.pitch; if (d.maxDist > 0f) a.maxDistance = d.maxDist; }
                }
                if (d.shake > 0f) Shake(d.shake, 1.2f);
            }
        }

        void Shake(float amp, float dur)
        {
            if (!_tpc) { var c = Camera.main; if (c) _tpc = c.GetComponent<Player.ThirdPersonCamera>(); }
            if (_tpc) _tpc.AddShake(amp, dur);
        }

        /// <summary>1 near the volcano, 0.35 far away (tremor and boom strength)</summary>
        float Proximity() => Mathf.Lerp(1f, 0.35f, Mathf.InverseLerp(250f, 1300f, Vector3.Distance(CamPos(), Crater())));
        Vector3 CamPos() { var c = Camera.main; return c ? c.transform.position : transform.position; }
        Vector3 Crater() => fountain ? fountain.transform.position : smoke ? smoke.transform.position : transform.position + Vector3.up * 230f;

        void EnsureWorld()
        {
            if (_terrains != null) return;
            _terrains = Terrain.activeTerrains;
            if (_terrains == null || _terrains.Length == 0) _terrains = FindObjectsByType<Terrain>(FindObjectsSortMode.None);
            var shore = FindFirstObjectByType<OceanShore>(FindObjectsInactive.Include);
            _sea = shore ? shore.seaLevel : 0f;
            float best = -1f;
            foreach (var t in _terrains)
            {
                if (!t || !t.terrainData) continue;
                var s = t.terrainData.size; if (s.x * s.z <= best) continue;
                best = s.x * s.z; _islandCentre = t.transform.position + new Vector3(s.x * 0.5f, 0f, s.z * 0.5f);
            }
            if (best < 0f) _islandCentre = Vector3.zero;
            var gm = GameManager.Instance ? GameManager.Instance : FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);
            _safe = gm && gm.spawnPoint ? gm.spawnPoint.position : spawnSafeCentre;
        }

        bool NearSpawn(Vector3 q) { float dx = q.x - _safe.x, dz = q.z - _safe.z; return spawnSafeRadius > 0f && dx * dx + dz * dz < spawnSafeRadius * spawnSafeRadius; }

        /// <summary>world ground height at p (the highest terrain under it), -infinity off every terrain</summary>
        float Ground(Vector3 p)
        {
            EnsureWorld();
            float best = float.NegativeInfinity;
            for (int i = 0; i < _terrains.Length; i++)
            {
                var t = _terrains[i]; if (!t) continue;
                var tp = t.transform.position; var s = t.terrainData.size;
                if (p.x < tp.x || p.z < tp.z || p.x > tp.x + s.x || p.z > tp.z + s.z) continue;
                best = Mathf.Max(best, t.SampleHeight(p) + tp.y);
            }
            return best;
        }

        /// <summary>
        /// a landing point: an aim line from the volcano across the island (random sideways offset), the first point on it
        /// above the sea is the volcano-facing shore; shore bombs land 2 m .. bombInland past it, sea bombs bombSeaOffshore
        /// before it. Also used by the builder's check in edit mode.
        /// </summary>
        public Vector3 PickBombTarget(bool shore)
        {
            EnsureWorld();
            // the start beach stays safe: re-aim (other shore first, then the sea); last resort pushed out of the safe circle
            for (int i = 0; i < 12; i++)
            {
                var q = TargetOnce(i < 6 && shore);
                if (!NearSpawn(q)) return q;
            }
            var r = TargetOnce(false);
            if (NearSpawn(r))
            {
                var o = new Vector3(r.x - _safe.x, 0f, r.z - _safe.z); if (o.sqrMagnitude < 1f) o = transform.position - _safe; o.y = 0f;
                r = new Vector3(_safe.x, 0f, _safe.z) + o.normalized * (spawnSafeRadius + 5f); r.y = Mathf.Max(Ground(r), _sea);
            }
            return r;
        }

        Vector3 TargetOnce(bool shore)
        {
            Vector3 from = transform.position; from.y = 0f;
            Vector3 c = _islandCentre; c.y = 0f;
            Vector3 dir = c - from; if (dir.sqrMagnitude < 1f) dir = Vector3.forward; dir.Normalize();
            Vector3 aim = c + new Vector3(-dir.z, 0f, dir.x) * Random.Range(-bombLateralSpread, bombLateralSpread);
            Vector3 d = aim - from; float len = d.magnitude; d /= Mathf.Max(1f, len);
            float shoreAt = len;
            for (float s = 0f; s < len; s += 6f) if (Ground(from + d * s) > _sea + 0.3f) { shoreAt = s; break; }
            float along = shore ? shoreAt + Random.Range(2f, Mathf.Max(2.5f, bombInland)) : shoreAt - Random.Range(bombSeaOffshore.x, bombSeaOffshore.y);
            var q = from + d * along; q.y = Mathf.Max(Ground(q), _sea);
            return q;
        }
    }
}
