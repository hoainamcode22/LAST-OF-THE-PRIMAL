using System.Collections.Generic;
using UnityEngine;

namespace PrimalFrontier.VFX
{
    public enum VfxId
    {
        None = 0,
        HitLight, HitHeavy, Bleed,
        FootSand, FootDirt, FootMud, FootRock, LandDust,
        WoodChips, StoneChips, Leaves, CraftDust, CraftSparks,
        FoodCrumbs, Steam, WaterSplash, WaterDrops,
        FireIgnite, FireExtinguish, CookSmoke,
        SpearImpact, ArrowImpact,
        DinoFootDust, DinoImpactDust,
        BloodSpray, BloodSprayHeavy, HitDust,          // appended: ids are serialised as ints in the library
        PunchImpactSmall, PunchImpactHeavy, DustImpact, Heal, BoilBubbles,   // phase 3 (registered by the Lead)
    }

    /// <summary>
    /// Pooled one-shot and timed particle effects. Effects return to the pool when their particle system stops
    /// (stopAction = Callback), so nothing is instantiated or destroyed during gameplay after warm-up.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class VfxPool : MonoBehaviour
    {
        static VfxPool _inst;
        /// <summary>the [VfxPool] placed in the scene (under [Systems]); created on first use if the scene has none</summary>
        public static VfxPool Instance
        {
            get
            {
                if (_inst == null)
                {
                    var go = new GameObject("[VfxPool]");
                    if (Application.isPlaying) DontDestroyOnLoad(go);
                    var c = go.AddComponent<VfxPool>();          // Awake registers it while playing
                    if (_inst == null) c.Register();
                }
                return _inst;
            }
        }

        [Tooltip("Particle effect prefabs by id. Empty = Resources/VfxLibrary.")] public VfxLibrary library;

        void Awake() => Register();
        void Register()
        {
            if (_inst == this) return;
            if (_inst != null) { var old = _inst; _inst = null; if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject); }
            _inst = this;
            Init(library ? library : Resources.Load<VfxLibrary>("VfxLibrary"));
        }

        readonly Dictionary<VfxId, GameObject> _prefabs = new Dictionary<VfxId, GameObject>();
        readonly Dictionary<VfxId, Stack<PooledEffect>> _free = new Dictionary<VfxId, Stack<PooledEffect>>();
        public int ActiveCount { get; private set; }
        public int CreatedCount { get; private set; }
        public int MaxActive = 48;   // hard cap (mobile)

        void Init(VfxLibrary lib)
        {
            if (lib == null) { Debug.LogWarning("[VfxPool] Resources/VfxLibrary not found - effects disabled"); return; }
            foreach (var e in lib.entries)
            {
                if (e.prefab == null) continue;
                _prefabs[e.id] = e.prefab; _free[e.id] = new Stack<PooledEffect>();
                for (int i = 0; i < e.prewarm; i++) _free[e.id].Push(Create(e.id));
            }
        }

        PooledEffect Create(VfxId id)
        {
            var go = Instantiate(_prefabs[id], transform); go.SetActive(false);
            var pe = go.GetOrAdd<PooledEffect>();
            pe.Id = id; pe.Pool = this; CreatedCount++;
            return pe;
        }

        /// <summary>play an effect; parent keeps it attached (bleeding, steam on food). Returns null if unavailable / capped.</summary>
        public PooledEffect Play(VfxId id, Vector3 pos, Quaternion rot, Transform parent = null, float scale = 1f)
        {
            id = BloodFilter(id, ref scale);
            if (id == VfxId.None || !_prefabs.ContainsKey(id)) return null;
            if (ActiveCount >= Mathf.Min(MaxActive, PrimalFrontier.Core.GameSettings.MaxVfx)) return null;
            if (PrimalFrontier.Core.GameSettings.Quality == 0 && Cosmetic(id) && FarFromCamera(pos, 22f)) return null;   // Low: skip small far-away puffs
            var stack = _free[id];
            var pe = stack.Count > 0 ? stack.Pop() : Create(id);
            var t = pe.transform;
            t.SetParent(parent != null ? parent : transform, true);
            t.SetPositionAndRotation(pos, rot); t.localScale = Vector3.one * scale;
            pe.gameObject.SetActive(true); pe.Begin();
            ActiveCount++;
            return pe;
        }

        public PooledEffect Play(VfxId id, Vector3 pos, Vector3 normal, Transform parent = null, float scale = 1f) =>
            Play(id, pos, normal.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(normal) : Quaternion.identity, parent, scale);

        static bool Cosmetic(VfxId id) => id == VfxId.FootSand || id == VfxId.FootDirt || id == VfxId.FootMud || id == VfxId.FootRock ||
                                          id == VfxId.DinoFootDust || id == VfxId.WaterDrops || id == VfxId.FoodCrumbs;
        static Camera _cam;
        static bool FarFromCamera(Vector3 p, float d)
        {
            if (!_cam) _cam = Camera.main;
            return _cam && (p - _cam.transform.position).sqrMagnitude > d * d;
        }

        /// <summary>the blood setting: Off swaps every blood effect for a neutral impact puff, Reduced shrinks them</summary>
        static VfxId BloodFilter(VfxId id, ref float scale)
        {
            bool blood = id == VfxId.HitLight || id == VfxId.HitHeavy || id == VfxId.Bleed || id == VfxId.BloodSpray || id == VfxId.BloodSprayHeavy;
            if (!blood) return id;
            var lvl = PrimalFrontier.Core.GameSettings.Blood;
            if (lvl == PrimalFrontier.Core.BloodLevel.Off) return id == VfxId.Bleed ? VfxId.None : VfxId.HitDust;
            if (lvl == PrimalFrontier.Core.BloodLevel.Reduced)
            {
                scale *= 0.6f;
                if (id == VfxId.BloodSprayHeavy) return VfxId.BloodSpray;
                if (id == VfxId.HitHeavy) return VfxId.HitLight;
            }
            return id;
        }

        internal void Return(PooledEffect pe)
        {
            if (pe == null) return;
            pe.gameObject.SetActive(false);
            pe.transform.SetParent(transform, false);
            if (_free.TryGetValue(pe.Id, out var s)) s.Push(pe);
            ActiveCount = Mathf.Max(0, ActiveCount - 1);
        }

        internal void Forget(PooledEffect pe) { ActiveCount = Mathf.Max(0, ActiveCount - 1); }

        /// <summary>debug: ids of effects currently playing</summary>
        public string ActiveNames()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var pe in FindObjectsByType<PooledEffect>(FindObjectsSortMode.None)) if (pe.Running) sb.Append(pe.Id).Append(pe.transform.parent == transform ? "" : "(attached)").Append(' ');
            return sb.ToString();
        }
        void OnDestroy() { if (_inst == this) _inst = null; }
    }
}
