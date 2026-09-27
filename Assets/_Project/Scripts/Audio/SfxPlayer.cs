using System.Collections.Generic;
using UnityEngine;

namespace PrimalFrontier.Audio
{
    public enum SfxId
    {
        None = 0,
        FootSand, FootDirt, FootRock, FootMud, FootWater, Land,
        HitFlesh, HitHeavy, HurtGrunt, Death,
        WoodChop, StoneHit, LeafRustle, Pickup,
        Craft, CraftKnap, Build,
        Eat, Drink, WaterSplash,
        FireIgnite, FireExtinguish, Sizzle,
        SpearWhoosh, SpearImpact, BowDraw, BowRelease, ArrowImpact,
        UiClick, UiObjective, UiRecipe,
        DinoStep, DinoStepHeavy,
        // appended (keep order: the library stores the numbers)
        WoodBreak, Thunder, ShipCreak, BreathIn, BreathOut, RoarDistant,
    }

    /// <summary>Pooled 3D one-shots with variant + pitch randomisation (never the same sample twice in a row).</summary>
    public class SfxPlayer : MonoBehaviour
    {
        static SfxPlayer _inst;
        public static SfxPlayer Instance
        {
            get
            {
                if (_inst == null)
                {
                    var go = new GameObject("[SfxPlayer]"); _inst = go.AddComponent<SfxPlayer>();
                    if (Application.isPlaying) DontDestroyOnLoad(go);
                    _inst.Init(Resources.Load<SfxLibrary>("SfxLibrary"));
                }
                return _inst;
            }
        }

        readonly Dictionary<SfxId, SfxLibrary.Entry> _map = new Dictionary<SfxId, SfxLibrary.Entry>();
        readonly Dictionary<SfxId, int> _last = new Dictionary<SfxId, int>();
        readonly List<AudioSource> _sources = new List<AudioSource>();
        int _next;
        public int Voices = 16;

        void Init(SfxLibrary lib)
        {
            if (lib != null) foreach (var e in lib.entries) if (e.clips != null && e.clips.Length > 0) _map[e.id] = e;
            for (int i = 0; i < Voices; i++)
            {
                var g = new GameObject("voice" + i); g.transform.SetParent(transform, false);
                var a = g.AddComponent<AudioSource>(); a.playOnAwake = false; a.spatialBlend = 1f; a.rolloffMode = AudioRolloffMode.Linear;
                a.minDistance = 1f; a.dopplerLevel = 0f; _sources.Add(a);
            }
        }

        public AudioSource Play(SfxId id, Vector3 pos, float volume = 1f, float spatial = 1f)
        {
            if (id == SfxId.None || !_map.TryGetValue(id, out var e)) return null;
            int n = e.clips.Length, k = Random.Range(0, n);
            if (n > 1 && _last.TryGetValue(id, out var prev) && prev == k) k = (k + 1) % n;
            _last[id] = k;
            var src = _sources[_next]; _next = (_next + 1) % _sources.Count;
            src.transform.position = pos; src.clip = e.clips[k]; src.volume = (e.volume > 0 ? e.volume : 1f) * volume;
            src.pitch = 1f + Random.Range(-e.pitchJitter, e.pitchJitter); src.spatialBlend = spatial;
            src.maxDistance = e.maxDistance > 0 ? e.maxDistance : 25f;
            src.Play();
            return src;
        }

        public void Play2D(SfxId id, float volume = 1f) => Play(id, Camera.main ? Camera.main.transform.position : Vector3.zero, volume, 0f);
        void OnDestroy() { if (_inst == this) _inst = null; }
    }
}
