using UnityEngine;

namespace PrimalFrontier.Core
{
    /// <summary>
    /// Layered 2D ambience: ocean (by how much sea is around), forest by day, insects at night, wind on high ground,
    /// rain, and the opening storm. Hear the island before seeing it. Volumes ease, nothing pops.
    /// </summary>
    public class AmbienceManager : MonoBehaviour
    {
        public AudioClip ocean, forest, night, wind, rain, storm;
        public float oceanMax = 0.55f, forestMax = 0.45f, nightMax = 0.4f, windMax = 0.35f, rainMax = 0.6f, stormMax = 0.8f;
        public bool stormOn;
        public Terrain terrain;
        AudioSource _ocean, _forest, _night, _wind, _rain, _storm;
        float _oceanness; float _next;

        void Start()
        {
            AudioSource Make(AudioClip c, string n)
            {
                var g = new GameObject("AMB_" + n); g.transform.SetParent(transform, false);
                var a = g.AddComponent<AudioSource>(); a.clip = c; a.loop = true; a.spatialBlend = 0f; a.volume = 0f; a.playOnAwake = false;
                a.time = c ? Random.Range(0f, c.length) : 0f; if (c) a.Play(); return a;
            }
            _ocean = Make(ocean, "Ocean"); _forest = Make(forest, "Forest"); _night = Make(night, "Night");
            _wind = Make(wind, "Wind"); _rain = Make(rain, "Rain"); _storm = Make(storm, "Storm");
            if (!terrain) terrain = Terrain.activeTerrain;
        }

        void Update()
        {
            var cam = Camera.main; Vector3 p = cam ? cam.transform.position : transform.position;
            if (Time.time >= _next && terrain)
            {
                _next = Time.time + 0.5f;
                // fraction of sea around at 25 m and 60 m
                int sea = 0, n = 0; float ty = terrain.transform.position.y;
                for (int ring = 0; ring < 2; ring++)
                    for (int i = 0; i < 12; i++)
                    {
                        float a = i * 30f * Mathf.Deg2Rad, r = ring == 0 ? 25f : 60f;
                        var q = p + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * r; n++;
                        if (terrain.SampleHeight(q) + ty < 0.05f) sea += ring == 0 ? 2 : 1;
                    }
                _oceanness = Mathf.Clamp01(sea / 18f);
            }
            var tm = TimeManager.Instance; var wm = WeatherManager.Instance;
            float dayK = tm ? tm.Daylight01 : 1f; bool nightNow = tm && tm.IsNight;
            float rainK = wm ? wm.Intensity : 0f;
            bool indoor = World.ZoneManager.Instance && World.ZoneManager.Instance.IsIndoor(p);
            float height = p.y;
            float k = AudioBus.Ambience;
            Ease(_ocean, oceanMax * Mathf.Lerp(0.15f, 1f, _oceanness) * (indoor ? 0.3f : 1f) * k);
            Ease(_forest, forestMax * dayK * (1f - _oceanness * 0.6f) * (1f - rainK * 0.6f) * (indoor ? 0.2f : 1f) * k);
            Ease(_night, nightMax * (nightNow ? 1f : 0f) * (1f - rainK * 0.5f) * (indoor ? 0.3f : 1f) * k);
            Ease(_wind, windMax * Mathf.Clamp01((height - 18f) / 25f + rainK * 0.3f) * (indoor ? 0.5f : 1f) * k);
            Ease(_rain, rainMax * rainK * (indoor ? 0.35f : 1f) * k);
            Ease(_storm, stormMax * (stormOn ? 1f : 0f) * k);
        }

        static void Ease(AudioSource a, float target) { if (a) a.volume = Mathf.MoveTowards(a.volume, target, Time.deltaTime * 0.35f); }
        public void SnapStorm(bool on) { stormOn = on; if (_storm) _storm.volume = on ? stormMax * AudioBus.Ambience : 0f; }
    }
}
