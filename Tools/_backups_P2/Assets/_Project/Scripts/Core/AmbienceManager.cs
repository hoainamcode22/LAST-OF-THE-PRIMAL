using System.Collections.Generic;
using UnityEngine;

namespace PrimalFrontier.Core
{
    /// <summary>
    /// The island's sound bed. 2D layers eased by the world state: forest by day (leaves, air), day insects, night insects
    /// (crickets), soft wind always and strong wind with the weather, light and heavy rain, the sea (by how much sea is
    /// around), a cave room tone indoors; plus one-shots around the listener: primitive bird calls by day (with an answer
    /// from elsewhere now and then), distant dinosaur calls (more at dusk and night), small night creatures, and thunder
    /// (WeatherManager asks <see cref="PlayThunder"/> distance / 343 s after a flash). Water, wetland and lava are 3D loops
    /// (<see cref="AmbienceEmitter"/>, placed by PrimalAtmosphereBuilder at ENV's water and markers).
    /// Moments of silence: insects, frogs and birds go quiet when a predator is close (awake, within a range that grows
    /// with its size) or has just noticed the player (GameEventType.PlayerNoticed from a predator); they come back slowly.
    /// Indoors (cave zone) the outside is muffled through a low-pass filter. All clips are original (Tools/Audio/
    /// ambience_synth.py); a missing PC-phase clip falls back to the older six loops, so nothing goes silent.
    /// Volumes ease, nothing pops. The legacy <see cref="stormOn"/> / <see cref="SnapStorm"/> (intro) keep working.
    /// </summary>
    public class AmbienceManager : MonoBehaviour
    {
        public static AmbienceManager Instance { get; private set; }
        [Header("Legacy loops (GameManager / PrimalSceneBaker assign them; used when a layer below is empty)")]
        public AudioClip ocean, forest, night, wind, rain, storm;
        public float oceanMax = 0.5f, forestMax = 0.4f, nightMax = 0.38f, windMax = 0.3f, rainMax = 0.5f, stormMax = 0.8f;
        public bool stormOn;
        public Terrain terrain;

        [Header("Layers (2D loops, PrimalAtmosphereBuilder)")]
        public AudioClip forestDay;
        public AudioClip insectsDay, nightInsects, windSoft, windStrong, rainLight, rainHeavy, oceanSurf, caveRoom;
        public float insectsMax = 0.26f, nightInsectsMax = 0.34f, windStrongMax = 0.55f, rainHeavyMax = 0.62f, caveMax = 0.32f;

        [Header("One-shots")]
        public AudioClip[] birdCalls = new AudioClip[0];
        public AudioClip[] distantCalls = new AudioClip[0];
        public AudioClip[] nightCritters = new AudioClip[0];
        public AudioClip[] thunderNear = new AudioClip[0];
        public AudioClip[] thunderFar = new AudioClip[0];
        [Range(0, 1)] public float birdVolume = 0.42f, distantCallVolume = 0.5f, critterVolume = 0.3f, thunderVolume = 1f;
        [Tooltip("real seconds between bird calls by day")] public Vector2 birdEvery = new Vector2(4f, 13f);
        [Tooltip("real seconds between distant dinosaur calls (day / night)")] public Vector2 distantCallDay = new Vector2(45f, 120f), distantCallNight = new Vector2(22f, 60f);
        public Vector2 critterEvery = new Vector2(9f, 26f);

        [Header("Moments of silence (predators)")]
        [Tooltip("an awake predator this close hushes the insects and birds (x its size, 1 = raptor-sized)")] public float silenceRadius = 34f;
        [Tooltip("seconds of silence after a predator noticed the player")] public float noticedSilenceSeconds = 24f;
        [Tooltip("seconds to fall silent / to come back")] public float hushSeconds = 2.5f, returnSeconds = 9f;

        [Header("Cave")]
        [Tooltip("low-pass cutoff of the outside sounds while indoors, Hz")] public float indoorCutoff = 850f;

        /// <summary>0 = normal life .. 1 = everything small holds its breath (a predator is near)</summary>
        public float Silence01 { get; private set; }
        /// <summary>0..1 how indoors the listener is (eased)</summary>
        public float Indoor01 { get; private set; }

        class Layer { public AudioSource src; public AudioLowPassFilter lp; }
        Layer _ocean, _forest, _insects, _night, _windSoft, _windStrong, _rainL, _rainH, _storm, _cave;
        readonly List<AudioSource> _pool = new List<AudioSource>();
        AudioSource _thunder; AudioLowPassFilter _thunderLp;
        float _oceanness, _next, _nextScan, _silenceTarget, _hushUntil = -1f;
        float _nextBird = 3f, _nextCall = 30f, _nextCritter = 10f, _answerAt = -1f; Vector3 _answerFrom;
        int _lastBird = -1, _lastCall = -1;

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; GameEvents.Raised -= OnEvent; }
        void OnEnable() { GameEvents.Raised -= OnEvent; GameEvents.Raised += OnEvent; }
        void OnDisable() { GameEvents.Raised -= OnEvent; }

        void Start()
        {
            Layer Make(AudioClip c, string n)
            {
                var g = new GameObject("AMB_" + n); g.transform.SetParent(transform, false);
                var a = g.AddComponent<AudioSource>(); a.clip = c; a.loop = true; a.spatialBlend = 0f; a.volume = 0f; a.playOnAwake = false;
                a.dopplerLevel = 0f; a.priority = 40;
                var lp = g.AddComponent<AudioLowPassFilter>(); lp.cutoffFrequency = 22000f;
                if (c) { a.time = Random.Range(0f, c.length * 0.95f); a.Play(); }
                return new Layer { src = a, lp = lp };
            }
            _ocean = Make(oceanSurf ? oceanSurf : ocean, "Ocean");
            _forest = Make(forestDay ? forestDay : forest, "Forest");
            _insects = Make(insectsDay, "InsectsDay");
            _night = Make(nightInsects ? nightInsects : night, "Night");
            _windSoft = Make(windSoft ? windSoft : wind, "Wind");
            _windStrong = Make(windStrong, "WindStrong");
            _rainL = Make(rainLight ? rainLight : rain, "Rain");
            _rainH = Make(rainHeavy, "RainHeavy");
            _storm = Make(storm, "Storm");
            _cave = Make(caveRoom, "Cave");
            var tg = new GameObject("AMB_Thunder"); tg.transform.SetParent(transform, false);
            _thunder = tg.AddComponent<AudioSource>(); _thunder.playOnAwake = false; _thunder.spatialBlend = 0f; _thunder.dopplerLevel = 0f; _thunder.priority = 20;
            _thunderLp = tg.AddComponent<AudioLowPassFilter>(); _thunderLp.cutoffFrequency = 22000f;
            for (int i = 0; i < 6; i++)
            {
                var g = new GameObject("AMB_OneShot" + i); g.transform.SetParent(transform, false);
                var a = g.AddComponent<AudioSource>(); a.playOnAwake = false; a.spatialBlend = 1f; a.dopplerLevel = 0f; a.priority = 90;
                a.rolloffMode = AudioRolloffMode.Linear; a.minDistance = 6f; a.maxDistance = 90f; a.spread = 40f;
                g.AddComponent<AudioLowPassFilter>().cutoffFrequency = 22000f;
                _pool.Add(a);
            }
            if (!terrain) terrain = Terrain.activeTerrain;
        }

        void OnEvent(GameEvent e)
        {
            // a predator noticed the player: the small life around goes quiet for a while
            if (e.type != GameEventType.PlayerNoticed || string.IsNullOrEmpty(e.id)) return;
            foreach (var c in AI.DinosaurController.All)
            {
                if (!c || c.def == null || c.def.id != e.id || !IsPredator(c)) continue;
                _hushUntil = Time.time + noticedSilenceSeconds;
                return;
            }
        }

        static bool IsPredator(AI.DinosaurController c) => c.def.temperament == AI.Temperament.Predator || c.def.temperament == AI.Temperament.Territorial;

        Vector3 Listener()
        {
            var cam = Camera.main;
            return cam ? cam.transform.position : transform.position;
        }

        void Update()
        {
            Vector3 p = Listener();
            float dt = Time.deltaTime;
            if (Time.time >= _next && terrain)
            {
                _next = Time.time + 0.5f;
                // fraction of sea around at 25 m and 60 m
                int sea = 0; float ty = terrain.transform.position.y;
                for (int ring = 0; ring < 2; ring++)
                    for (int i = 0; i < 12; i++)
                    {
                        float a = i * 30f * Mathf.Deg2Rad, r = ring == 0 ? 25f : 60f;
                        var q = p + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * r;
                        if (terrain.SampleHeight(q) + ty < 0.05f) sea += ring == 0 ? 2 : 1;
                    }
                _oceanness = Mathf.Clamp01(sea / 18f);
            }
            if (Time.time >= _nextScan) { _nextScan = Time.time + 0.5f; _silenceTarget = PredatorThreat(p); }
            float hush = Mathf.Max(_silenceTarget, Time.time < _hushUntil ? 1f : 0f);
            Silence01 = Mathf.MoveTowards(Silence01, hush, dt / Mathf.Max(0.1f, hush > Silence01 ? hushSeconds : returnSeconds));

            var tm = TimeManager.Instance; var wm = WeatherManager.Instance;
            float dayK = tm ? tm.Daylight01 : 1f, nightK = tm ? tm.NightFactor : 0f;
            float rainK = wm ? wm.Intensity : 0f, stormK = wm ? wm.StormK : 0f;
            float windK = wm ? Mathf.Clamp01((wm.WindStrength - 0.35f) / 1.5f) : 0f;
            bool indoor = World.ZoneManager.Instance && World.ZoneManager.Instance.IsIndoor(p);
            Indoor01 = Mathf.MoveTowards(Indoor01, indoor ? 1f : 0f, dt / 1.5f);
            float outK = Mathf.Lerp(1f, 0.3f, Indoor01);
            float life = 1f - Silence01;
            float height = Mathf.Clamp01((p.y - 18f) / 25f);
            float k = AudioBus.Ambience;
            float noon = tm ? Mathf.Clamp01(1f - Mathf.Abs(tm.hour - 14f) / 6f) : 0.5f;     // cicadas peak in the hot afternoon

            Ease(_ocean, oceanMax * Mathf.Lerp(0.12f, 1f, _oceanness) * Mathf.Lerp(1f, 1.25f, stormK) * outK * k, dt);
            Ease(_forest, forestMax * Mathf.Lerp(0.25f, 1f, dayK) * (1f - _oceanness * 0.55f) * (1f - rainK * 0.5f) * outK * k, dt);
            Ease(_insects, insectsMax * dayK * Mathf.Lerp(0.55f, 1f, noon) * (1f - rainK * 0.9f) * (1f - _oceanness * 0.6f) * life * outK * k, dt);
            Ease(_night, nightMax * nightK * (1f - rainK * 0.8f) * (1f - _oceanness * 0.4f) * life * outK * k, dt);
            float soft = windMax * Mathf.Lerp(0.4f, 1f, Mathf.Max(height, windK)) * (1f - stormK * 0.6f);
            Ease(_windSoft, soft * Mathf.Lerp(1f, 0.5f, Indoor01) * k, dt);
            Ease(_windStrong, (_windStrong.src.clip ? windStrongMax : 0f) * Mathf.Clamp01(Mathf.Max(windK * 1.2f - 0.25f, stormK)) * Mathf.Lerp(1f, 0.4f, Indoor01) * k, dt);
            float heavy = _rainH.src.clip ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, rainK)) : 0f;
            Ease(_rainL, rainMax * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.45f, rainK)) * (1f - heavy * 0.55f) * Mathf.Lerp(1f, 0.35f, Indoor01) * k, dt);
            Ease(_rainH, rainHeavyMax * heavy * Mathf.Lerp(1f, 0.35f, Indoor01) * k, dt);
            Ease(_storm, stormMax * (stormOn ? 1f : 0f) * k, dt);
            Ease(_cave, caveMax * Indoor01 * k, dt);
            // muffle the outside indoors
            float cut = Mathf.Lerp(22000f, indoorCutoff, Indoor01);
            _ocean.lp.cutoffFrequency = cut; _forest.lp.cutoffFrequency = cut; _insects.lp.cutoffFrequency = cut; _night.lp.cutoffFrequency = cut;
            _windSoft.lp.cutoffFrequency = Mathf.Lerp(22000f, indoorCutoff * 1.4f, Indoor01); _windStrong.lp.cutoffFrequency = _windSoft.lp.cutoffFrequency;
            _rainL.lp.cutoffFrequency = Mathf.Lerp(22000f, indoorCutoff * 1.6f, Indoor01); _rainH.lp.cutoffFrequency = _rainL.lp.cutoffFrequency;
            _thunderLp.cutoffFrequency = Mathf.Lerp(22000f, 500f, Indoor01);

            OneShots(p, dayK, nightK, rainK, life, outK, k);
        }

        /// <summary>0..1: how close an awake predator is (size widens the range; awareness of the player hushes further)</summary>
        float PredatorThreat(Vector3 p)
        {
            float t = 0f;
            var all = AI.DinosaurController.All;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (!c || !c.IsAlive || c.def == null || !IsPredator(c) || c.Sleeping) continue;
                float size = Mathf.Clamp(c.def.bodyRadius / 1.2f, 0.7f, 2.4f);
                float r = silenceRadius * size;
                if (c.Senses.Level >= AI.AwarenessLevel.Suspicious) r *= 1.6f;
                var d = c.transform.position - p; d.y = 0f;
                float dist = d.magnitude;
                if (dist >= r) continue;
                float k = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(r * 0.55f, r, dist));
                if (k > t) t = k;
            }
            return t;
        }

        void OneShots(Vector3 p, float dayK, float nightK, float rainK, float life, float outK, float k)
        {
            float now = Time.time;
            bool outdoors = outK > 0.9f;
            // day birds (primitive calls in the canopy); an answer from another tree now and then
            if (now >= _nextBird)
            {
                _nextBird = now + Random.Range(birdEvery.x, birdEvery.y) / Mathf.Lerp(0.5f, 1f, dayK);
                if (outdoors && dayK > 0.3f && rainK < 0.45f && life > 0.7f && birdCalls.Length > 0)
                {
                    var at = Around(p, 14f, 40f, 4f, 14f);
                    int i = Pick(birdCalls, ref _lastBird);
                    Play(birdCalls[i], at, birdVolume * dayK * (1f - rainK) * life * k * Random.Range(0.7f, 1f), Random.Range(0.94f, 1.06f), 22000f);
                    if (Random.value < 0.35f) { _answerAt = now + Random.Range(0.6f, 1.8f); _answerFrom = Around(p, 20f, 50f, 4f, 14f); }
                }
            }
            if (_answerAt > 0f && now >= _answerAt)
            {
                _answerAt = -1f;
                if (life > 0.7f && birdCalls.Length > 0) Play(birdCalls[Pick(birdCalls, ref _lastBird)], _answerFrom, birdVolume * 0.7f * dayK * life * k, Random.Range(0.97f, 1.1f), 22000f);
            }
            // distant dinosaur calls: far away (low-passed, quiet), more at dusk and night, masked by heavy rain
            if (now >= _nextCall)
            {
                var r = nightK > 0.5f ? distantCallNight : distantCallDay;
                _nextCall = now + Random.Range(r.x, r.y);
                if (distantCalls.Length > 0 && rainK < 0.8f)
                {
                    var at = Around(p, 70f, 85f, 10f, 30f);
                    Play(distantCalls[Pick(distantCalls, ref _lastCall)], at, distantCallVolume * (1f - rainK * 0.7f) * Mathf.Lerp(1f, 0.5f, 1f - outK) * k * Random.Range(0.6f, 1f),
                         Random.Range(0.9f, 1.05f), Mathf.Lerp(1800f, 700f, 1f - outK), 160f);
                }
            }
            // small night creatures (rustles, chirps) close by
            if (now >= _nextCritter)
            {
                _nextCritter = now + Random.Range(critterEvery.x, critterEvery.y);
                if (outdoors && nightK > 0.6f && rainK < 0.5f && life > 0.6f && nightCritters.Length > 0)
                    Play(nightCritters[Random.Range(0, nightCritters.Length)], Around(p, 6f, 18f, 0.2f, 3f), critterVolume * life * k * Random.Range(0.6f, 1f), Random.Range(0.92f, 1.08f), 22000f);
            }
        }

        static int Pick(AudioClip[] a, ref int last)
        {
            int i = Random.Range(0, a.Length);
            if (a.Length > 1 && i == last) i = (i + 1) % a.Length;
            last = i; return i;
        }

        static Vector3 Around(Vector3 p, float rMin, float rMax, float hMin, float hMax)
        {
            float a = Random.Range(0f, Mathf.PI * 2f), r = Random.Range(rMin, rMax);
            return p + new Vector3(Mathf.Cos(a) * r, Random.Range(hMin, hMax), Mathf.Sin(a) * r);
        }

        void Play(AudioClip clip, Vector3 at, float vol, float pitch, float cutoff, float maxDistance = 90f)
        {
            if (!clip || vol <= 0.001f) return;
            AudioSource a = null;
            foreach (var s in _pool) if (s && !s.isPlaying) { a = s; break; }
            if (!a) return;
            a.transform.position = at; a.clip = clip; a.volume = Mathf.Clamp01(vol); a.pitch = pitch; a.maxDistance = maxDistance;
            var lp = a.GetComponent<AudioLowPassFilter>(); if (lp) lp.cutoffFrequency = cutoff;
            a.Play();
        }

        /// <summary>thunder of a strike this far away (m): a close crack or a far roll; false when no thunder clip exists</summary>
        public bool PlayThunder(float meters, float volume)
        {
            var set = meters < 900f ? thunderNear : thunderFar;
            if (set == null || set.Length == 0) set = meters < 900f ? thunderFar : thunderNear;
            if (set == null || set.Length == 0 || !_thunder) return false;
            var c = set[Random.Range(0, set.Length)]; if (!c) return false;
            if (_thunder.isPlaying && _thunder.time < 1.5f) return true;     // two strikes on top of each other: keep the first
            _thunder.clip = c; _thunder.pitch = Random.Range(0.92f, 1.04f); _thunder.panStereo = Random.Range(-0.35f, 0.35f);
            _thunder.volume = Mathf.Clamp01(volume * thunderVolume * AudioBus.Ambience);
            _thunder.Play();
            return true;
        }

        static void Ease(Layer l, float target, float dt)
        {
            if (l == null || !l.src) return;
            var a = l.src;
            if (!a.clip) { a.volume = 0f; return; }
            a.volume = Mathf.MoveTowards(a.volume, target, dt * 0.35f);
            // a silent loop does not need to play (voices): pause below the threshold, resume on the way up
            if (a.volume <= 0.0005f && target <= 0.0005f) { if (a.isPlaying) a.Pause(); }
            else if (!a.isPlaying) a.UnPause();
        }

        public void SnapStorm(bool on) { stormOn = on; if (_storm != null && _storm.src) { _storm.src.volume = on ? stormMax * AudioBus.Ambience : 0f; if (on && _storm.src.clip && !_storm.src.isPlaying) _storm.src.Play(); } }
    }
}
