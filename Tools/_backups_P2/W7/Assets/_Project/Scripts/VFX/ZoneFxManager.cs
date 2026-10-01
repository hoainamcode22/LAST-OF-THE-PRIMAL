using System;
using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Core;
using PrimalFrontier.World;

namespace PrimalFrontier.VFX
{
    /// <summary>
    /// Zone atmosphere through pooled particle systems. A small pool per kind (library entry: prefab, pool size, range) is
    /// made once at start; every half second the nearest anchors of each kind within range of the camera get a system
    /// (moved there, emission volume set to the anchor), the others give theirs back. Emission fades in and out with the
    /// distance, so nothing pops; it also follows the hour and the weather: wetland fog and mist thicker at dawn and night,
    /// insects and dragonflies by day, fireflies only on dry nights, dust motes in sunlight, flies by day, wind dust with the
    /// wind, rain damping dust / insects / ash. Fog-like particles dim at night. Nothing is instantiated after start.
    /// Also the zone haze: per zone a fog tint and amount by time of day, weighted by the zone's blend band
    /// (ZoneManager.WeightOf), published as <see cref="HazeAmount"/> / <see cref="HazeColor"/> for HazardZoneMonitor, which
    /// mixes it with the volcanic smoke into the scene fog (TimeManager). Built by PrimalAtmosphereBuilder.Zones2.
    /// </summary>
    public class ZoneFxManager : MonoBehaviour
    {
        [Serializable]
        public class Entry
        {
            public ZoneFxKind kind;
            public GameObject prefab;
            [Min(1)] public int pool = 3;
            [Tooltip("an anchor further from the camera than this gets no system (m)")] public float range = 80f;
            [Tooltip("anchor radius the prefab's emission rate is meant for: bigger anchors emit more")] public float refRadius = 6f;
            [Tooltip("colour darkens at night (fog, dust, smoke; not glowing insects)")] public bool dimAtNight = true;
        }

        [Serializable]
        public class Haze
        {
            [Tooltip("ZoneManager zone id")] public string zone;
            public Color color = new Color(0.6f, 0.64f, 0.66f);
            [Range(0, 1)] public float day = 0.08f, dawn = 0.3f, night = 0.2f;
            [Tooltip("applies only indoors (cave); otherwise it fades out indoors")] public bool indoor;
        }

        public List<Entry> library = new List<Entry>();
        public List<Haze> haze = new List<Haze>();
        [Tooltip("seconds to fade a system in / out")] public float fadeSeconds = 2.5f;

        public static ZoneFxManager Instance { get; private set; }
        /// <summary>0..1 zone haze at the camera (eased); HazardZoneMonitor adds it to the local fog</summary>
        public static float HazeAmount { get; private set; }
        public static Color HazeColor { get; private set; } = new Color(0.6f, 0.64f, 0.66f);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() { HazeAmount = 0f; }

        class Slot
        {
            public Entry e; public ParticleSystem ps; public float baseRate; public Color baseColor;
            public ZoneFxAnchor anchor; public float fade, target, scale = 1f; public bool playing;
        }
        readonly List<Slot> _slots = new List<Slot>();
        readonly List<ZoneFxAnchor> _cand = new List<ZoneFxAnchor>();
        readonly List<float> _candD = new List<float>();
        readonly ZoneManager.Zone[] _hazeZones = new ZoneManager.Zone[16];
        float _next, _nextZones, _hazeTarget, _indoor; Color _hazeColorTarget = new Color(0.6f, 0.64f, 0.66f);
        float _rateScale = 1f;

        /// <summary>systems made at start (all kinds)</summary>
        public int PooledCount => _slots.Count;
        public int ActiveCount { get { int n = 0; foreach (var s in _slots) if (s.playing) n++; return n; } }

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) { Instance = null; HazeAmount = 0f; } }

        void Start()
        {
            bool low = GameSettings.Quality == 0;
            _rateScale = low ? 0.6f : 1f;
            foreach (var e in library)
            {
                if (e == null || !e.prefab) continue;
                int n = low ? Mathf.Max(1, e.pool / 2) : e.pool;
                for (int i = 0; i < n; i++)
                {
                    var go = Instantiate(e.prefab, transform);
                    go.name = e.prefab.name + "_" + i;
                    var ps = go.GetComponent<ParticleSystem>();
                    if (!ps) { Destroy(go); continue; }
                    var main = ps.main; main.playOnAwake = false;
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    _slots.Add(new Slot { e = e, ps = ps, baseRate = ps.emission.rateOverTimeMultiplier, baseColor = main.startColor.color });
                }
            }
            HazardZoneMonitor.Ensure();
        }

        void Update()
        {
            var cam = Camera.main; if (!cam) return;
            var eye = cam.transform.position; float dt = Time.deltaTime;
            if (Time.time >= _next) { _next = Time.time + 0.5f; Tick(eye); }
            float step = dt / Mathf.Max(0.1f, fadeSeconds);
            for (int i = 0; i < _slots.Count; i++)
            {
                var s = _slots[i];
                if (!s.playing) continue;
                s.fade = Mathf.MoveTowards(s.fade, s.anchor ? s.target : 0f, step);
                var em = s.ps.emission; em.rateOverTimeMultiplier = s.baseRate * s.fade * s.scale * _rateScale;
                if (s.fade <= 0f && !s.anchor) { s.ps.Stop(true, ParticleSystemStopBehavior.StopEmitting); s.playing = false; }
            }
            HazeAmount = Mathf.MoveTowards(HazeAmount, _hazeTarget, dt * 0.12f);
            HazeColor = Color.Lerp(HazeColor, _hazeColorTarget, Mathf.Clamp01(dt * 0.6f));
        }

        // ------------------------------------------------------------------ every half second
        void Tick(Vector3 eye)
        {
            var zm = ZoneManager.Instance;
            _indoor = zm && zm.IsIndoor(eye) ? 1f : 0f;
            var tm = TimeManager.Instance; float day = tm ? tm.Daylight01 : 1f;
            float bright = Mathf.Lerp(0.2f, 1f, day);
            var wm = WeatherManager.Instance;
            foreach (var e in library)
            {
                if (e == null || !e.prefab) continue;
                // nearest anchors of this kind within range
                _cand.Clear(); _candD.Clear();
                for (int i = 0; i < ZoneFxAnchor.All.Count; i++)
                {
                    var a = ZoneFxAnchor.All[i];
                    if (!a || a.kind != e.kind) continue;
                    float d = Vector3.Distance(eye, a.transform.position) - a.radius;
                    if (d > e.range) continue;
                    int k = _candD.Count; while (k > 0 && _candD[k - 1] > d) k--;
                    _cand.Insert(k, a); _candD.Insert(k, d);
                }
                int keep = 0; for (int i = 0; i < _slots.Count; i++) if (_slots[i].e == e) keep++;
                if (_cand.Count > keep) { _cand.RemoveRange(keep, _cand.Count - keep); _candD.RemoveRange(keep, _candD.Count - keep); }
                float env = Env(e.kind, tm, wm);
                // give back the systems of anchors no longer chosen
                for (int i = 0; i < _slots.Count; i++) { var s = _slots[i]; if (s.e == e && s.anchor && !_cand.Contains(s.anchor)) s.anchor = null; }
                for (int c = 0; c < _cand.Count; c++)
                {
                    var a = _cand[c]; Slot held = null;
                    for (int i = 0; i < _slots.Count; i++) if (_slots[i].e == e && _slots[i].anchor == a) { held = _slots[i]; break; }
                    if (held == null)
                    {
                        for (int i = 0; i < _slots.Count; i++) { var s = _slots[i]; if (s.e == e && !s.anchor && !s.ps.IsAlive(true)) { held = s; break; } }
                        if (held == null) continue;                        // all busy fading out: next tick
                        Place(held, a);
                    }
                    float fall = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(e.range * 0.65f, e.range, _candD[c]));
                    held.target = Mathf.Clamp(env * a.strength * fall, 0f, 2f);
                    if (e.dimAtNight) { var m = held.ps.main; var bc = held.baseColor; m.startColor = new Color(bc.r * bright, bc.g * bright, bc.b * bright, bc.a); }
                    if (e.kind == ZoneFxKind.WindDust && wm) Wind(held, wm);
                }
            }
            Hazes(eye, zm, tm);
        }

        void Place(Slot s, ZoneFxAnchor a)
        {
            s.anchor = a; s.fade = 0f; s.target = 0f;
            var t = s.ps.transform; t.SetPositionAndRotation(a.transform.position, Quaternion.identity);
            var sh = s.ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(a.radius * 2f, a.height, a.radius * 2f);
            float r = Mathf.Max(0.5f, s.e.refRadius);
            s.scale = Mathf.Clamp(a.radius * a.radius / (r * r), 0.25f, 6f);
            var em = s.ps.emission; em.rateOverTimeMultiplier = 0f;
            s.ps.Play(true); s.playing = true;
        }

        static void Wind(Slot s, WeatherManager wm)
        {
            var v = s.ps.velocityOverLifetime; if (!v.enabled) return;
            var w = wm.WindDirection; float k = Mathf.Lerp(1.5f, 5f, Mathf.Clamp01((wm.WindStrength - 0.3f) / 1.1f));
            v.x = new ParticleSystem.MinMaxCurve(w.x * k); v.z = new ParticleSystem.MinMaxCurve(w.y * k);
        }

        /// <summary>0..1 density of a kind right now (hour, weather, indoors, predator silence)</summary>
        float Env(ZoneFxKind k, TimeManager tm, WeatherManager wm)
        {
            float day = tm ? tm.Daylight01 : 1f, night = tm ? tm.NightFactor : 0f, hour = tm ? tm.hour : 12f;
            float dawn = Mathf.Clamp01(1f - Mathf.Abs(hour - 6.2f) / 1.8f);
            float rain = wm ? wm.Intensity : 0f, wind = wm ? wm.WindStrength : 0.35f, over = wm ? wm.Overcast : 0f;
            var amb = AmbienceManager.Instance; float life = amb ? 1f - amb.Silence01 : 1f;
            float outK = 1f - _indoor;
            switch (k)
            {
                case ZoneFxKind.GroundFog: return Mathf.Lerp(0.3f, 1f, Mathf.Max(dawn, night * 0.8f)) * (1f + 0.2f * rain) * outK;
                case ZoneFxKind.WaterMist: return Mathf.Lerp(0.35f, 1f, Mathf.Max(dawn, night * 0.9f)) * outK;
                case ZoneFxKind.Insects: return Mathf.Lerp(0.12f, 1f, day) * (1f - 0.85f * rain) * life * outK;
                case ZoneFxKind.Dragonflies: return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.8f, day)) * (1f - rain) * life * outK;
                case ZoneFxKind.Fireflies: return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 1f, night)) * (1f - rain) * life * outK;
                case ZoneFxKind.DustMotes: return day * (1f - 0.6f * over) * (1f - rain) * outK;
                case ZoneFxKind.Haze: return (0.7f + 0.3f * dawn) * outK;
                case ZoneFxKind.Flies: return Mathf.Lerp(0.1f, 1f, day) * (1f - 0.7f * rain);
                case ZoneFxKind.WindDust: return Mathf.Lerp(0.2f, 1f, Mathf.Clamp01((wind - 0.3f) / 0.9f)) * (1f - 0.9f * rain) * outK;
                case ZoneFxKind.SmokeWisp: return (1f - 0.3f * rain) * outK;
                case ZoneFxKind.AshFall: return (1f - 0.6f * rain) * outK;
                case ZoneFxKind.HeatShimmer: return Mathf.Lerp(0.5f, 1f, day) * (1f - 0.6f * rain) * outK;
                default: return 1f;                                            // cave drips / mist
            }
        }

        void Hazes(Vector3 eye, ZoneManager zm, TimeManager tm)
        {
            if (!zm || haze.Count == 0) { _hazeTarget = 0f; return; }
            if (Time.time >= _nextZones) { _nextZones = Time.time + 5f; for (int i = 0; i < haze.Count && i < _hazeZones.Length; i++) _hazeZones[i] = haze[i] != null ? zm.Find(haze[i].zone) : null; }
            float day = tm ? tm.Daylight01 : 1f, night = tm ? tm.NightFactor : 0f, hour = tm ? tm.hour : 12f;
            float dawn = Mathf.Clamp01(1f - Mathf.Abs(hour - 6.2f) / 1.8f);
            float clear = 1f; float r = 0f, g = 0f, b = 0f, sum = 0f;
            for (int i = 0; i < haze.Count && i < _hazeZones.Length; i++)
            {
                var h = haze[i]; var z = _hazeZones[i]; if (h == null || z == null) continue;
                float w = z.Weight(eye); if (w <= 0f) continue;
                w *= h.indoor ? _indoor : 1f - _indoor;
                float a = Mathf.Lerp(Mathf.Lerp(h.night, h.day, day), h.dawn, dawn) * w;
                if (a <= 0f) continue;
                clear *= 1f - Mathf.Clamp01(a);
                r += h.color.r * a; g += h.color.g * a; b += h.color.b * a; sum += a;
            }
            _hazeTarget = 1f - clear;
            if (sum > 0f) _hazeColorTarget = new Color(r / sum, g / sum, b / sum);
        }
    }
}
