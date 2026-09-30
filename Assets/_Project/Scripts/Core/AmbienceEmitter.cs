using UnityEngine;

namespace PrimalFrontier.Core
{
    /// <summary>
    /// A 3D ambience loop at a place: river, stream, waterfall, wetland (day insects and water, frogs at night), surf, lava.
    /// Plays <see cref="clip"/> by day and cross-fades to <see cref="nightClip"/> at night when one is set; frogs and insects
    /// (<see cref="hushable"/>) go quiet with AmbienceManager.Silence01 when a predator is near; heavy rain masks the quieter
    /// ones a little. Stops its voice while the listener is beyond maxDistance (no cost far away). Placed by
    /// PrimalAtmosphereBuilder under [Atmosphere]/Emitters; move / retune them in the scene.
    /// </summary>
    public class AmbienceEmitter : MonoBehaviour
    {
        public enum Kind { River, Stream, Waterfall, Wetland, Surf, Lava, Cave, Custom }
        public Kind kind = Kind.River;
        public AudioClip clip;
        [Tooltip("played instead at night (cross-fade over dusk / dawn); empty = the same clip all day")] public AudioClip nightClip;
        [Range(0, 1)] public float volume = 0.6f;
        [Range(0, 1)] public float nightVolume = 0.6f;
        public float minDistance = 8f, maxDistance = 60f;
        [Range(0, 360)] public float spread = 90f;
        [Tooltip("goes quiet when a predator is near (frogs, insects)")] public bool hushable;
        [Tooltip("heavy rain masks it this much (0..1)")] [Range(0, 1)] public float rainMask = 0.3f;

        AudioSource _day, _night; float _next; bool _near;

        void Start()
        {
            _day = Make(clip, "Day");
            if (nightClip) _night = Make(nightClip, "Night");
        }

        AudioSource Make(AudioClip c, string n)
        {
            var g = new GameObject("Src_" + n); g.transform.SetParent(transform, false);
            var a = g.AddComponent<AudioSource>();
            a.clip = c; a.loop = true; a.playOnAwake = false; a.spatialBlend = 1f; a.dopplerLevel = 0f; a.spread = spread;
            a.rolloffMode = AudioRolloffMode.Logarithmic; a.minDistance = Mathf.Max(0.5f, minDistance); a.maxDistance = Mathf.Max(minDistance + 1f, maxDistance);
            a.volume = 0f; a.priority = 64;
            if (c) a.time = Random.Range(0f, c.length * 0.95f);
            return a;
        }

        void Update()
        {
            if (!_day) return;
            if (Time.time >= _next)
            {
                _next = Time.time + 0.5f;
                var cam = Camera.main;
                float d = cam ? Vector3.Distance(cam.transform.position, transform.position) : 0f;
                _near = d < maxDistance * 1.1f;
                Voice(_day, _near); Voice(_night, _near);
            }
            if (!_near) return;
            var tm = TimeManager.Instance; var wm = WeatherManager.Instance; var amb = AmbienceManager.Instance;
            float nightK = _night && tm ? Mathf.SmoothStep(0f, 1f, tm.NightFactor) : 0f;
            float life = hushable && amb ? 1f - amb.Silence01 : 1f;
            float rain = wm ? 1f - rainMask * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 1f, wm.Intensity)) : 1f;
            float k = AudioBus.Ambience * rain * life;
            Fade(_day, volume * (1f - nightK) * k);
            if (_night) Fade(_night, nightVolume * nightK * k);
        }

        /// <summary>ease the volume; a source that has faded out pauses (no voice), and resumes when it is needed again</summary>
        static void Fade(AudioSource a, float target)
        {
            a.volume = Mathf.MoveTowards(a.volume, target, Time.deltaTime * 0.4f);
            if (a.volume <= 0.0005f && target <= 0.0005f) { if (a.isPlaying) a.Pause(); }
            else if (!a.isPlaying && a.clip) { a.UnPause(); if (!a.isPlaying) a.Play(); }
        }

        static void Voice(AudioSource a, bool on)
        {
            if (!a || !a.clip) return;
            if (!on && a.isPlaying) { a.Stop(); a.volume = 0f; }       // far away: no voice (Fade starts it again on the way back)
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.5f); Gizmos.DrawWireSphere(transform.position, minDistance);
            Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.2f); Gizmos.DrawWireSphere(transform.position, maxDistance);
        }
    }
}
