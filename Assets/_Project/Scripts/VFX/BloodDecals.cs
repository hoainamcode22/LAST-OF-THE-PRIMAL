using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Core;

namespace PrimalFrontier.VFX
{
    /// <summary>
    /// Ground blood: pooled flat quads laid on whatever is below (terrain, rocks), oriented to the surface, fading out over
    /// time. Splats from hits and drips, a growing pool under a dead creature. Hard caps keep it cheap on phones; the
    /// oldest decal is reused first. Nothing is created after warm-up.
    /// </summary>
    public class BloodDecals : MonoBehaviour
    {
        static BloodDecals _inst;
        public static BloodDecals Instance
        {
            get
            {
                if (_inst == null)
                {
                    var go = new GameObject("[BloodDecals]"); _inst = go.AddComponent<BloodDecals>();
                    if (Application.isPlaying) DontDestroyOnLoad(go);
                    _inst.Init(Resources.Load<BloodLibrary>("BloodLibrary"));
                }
                return _inst;
            }
        }

        class Decal { public Transform t; public MeshRenderer r; public float born, life, size, grow, growTime; public bool active; public Color col; }
        readonly List<Decal> _decals = new List<Decal>();
        BloodLibrary _lib; Mesh _quad; MaterialPropertyBlock _mpb;
        public int Capacity = 48;
        public int ActiveCount { get { int n = 0; foreach (var d in _decals) if (d.active) n++; return n; } }
        public int PoolCount { get; private set; }
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        void Init(BloodLibrary lib)
        {
            _lib = lib; _mpb = new MaterialPropertyBlock();
            _quad = new Mesh { name = "BloodQuad" };
            _quad.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f) };
            _quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            _quad.triangles = new[] { 0, 2, 1, 0, 3, 2 }; _quad.RecalculateNormals(); _quad.RecalculateBounds();
            if (lib == null) Debug.LogWarning("[BloodDecals] Resources/BloodLibrary not found - ground blood disabled");
        }

        Decal Take()
        {
            int cap = GameSettings.Blood == BloodLevel.Normal ? Capacity : Capacity / 3;
            Decal oldest = null; int active = 0;
            foreach (var d in _decals)
            {
                if (!d.active) return d;
                active++; if (oldest == null || d.born < oldest.born) oldest = d;
            }
            if (active >= cap && oldest != null) return oldest;
            var go = new GameObject("BloodDecal"); go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _quad;
            var r = go.AddComponent<MeshRenderer>(); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = true;
            var nd = new Decal { t = go.transform, r = r }; _decals.Add(nd); return nd;
        }

        static bool Ground(Vector3 from, Transform ignore, out RaycastHit hit)
        {
            var hits = Physics.RaycastAll(from + Vector3.up * 0.6f, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; hit = default; bool ok = false;
            foreach (var h in hits)
            {
                if (ignore != null && h.collider.transform.IsChildOf(ignore)) continue;
                if (h.collider.attachedRigidbody != null && !h.collider.attachedRigidbody.isKinematic) continue;
                if (h.distance < best) { best = h.distance; hit = h; ok = true; }
            }
            return ok;
        }

        /// <summary>a splat on the ground below 'from' (world); size in metres; ignore = the creature's own colliders</summary>
        public bool Splat(Vector3 from, float size, Transform ignore = null, float life = 90f)
        {
            if (_lib == null || _lib.splats == null || _lib.splats.Length == 0 || GameSettings.Blood == BloodLevel.Off) return false;
            if (!Ground(from, ignore, out var hit)) return false;
            var d = Take();
            d.r.sharedMaterial = _lib.splats[Random.Range(0, _lib.splats.Length)];
            Place(d, hit, size); d.life = life; d.grow = 1f; d.growTime = 0f; return true;
        }

        /// <summary>a pool that spreads under a dead creature over growTime seconds</summary>
        public bool Pool(Vector3 at, float radius, Transform ignore = null, float growTime = 6f, float life = 240f)
        {
            if (_lib == null || _lib.pool == null || GameSettings.Blood != BloodLevel.Normal) return false;
            if (!Ground(at, ignore, out var hit)) return false;
            var d = Take(); d.r.sharedMaterial = _lib.pool;
            Place(d, hit, radius * 2f); d.life = life; d.grow = 0.15f; d.growTime = growTime; PoolCount++;
            d.t.localScale = Vector3.one * d.size * d.grow; return true;
        }

        void Place(Decal d, RaycastHit hit, float size)
        {
            d.t.SetPositionAndRotation(hit.point + hit.normal * 0.02f, Quaternion.FromToRotation(Vector3.up, hit.normal) * Quaternion.Euler(0, Random.Range(0f, 360f), 0));
            d.size = size; d.t.localScale = Vector3.one * size; d.born = Time.time; d.active = true;
            d.col = new Color(1, 1, 1, 1); d.r.gameObject.SetActive(true); Apply(d, 1f);
        }

        void Apply(Decal d, float alpha)
        {
            d.r.GetPropertyBlock(_mpb);
            var c = d.r.sharedMaterial != null && d.r.sharedMaterial.HasProperty(BaseColorId) ? d.r.sharedMaterial.GetColor(BaseColorId) : Color.white;
            c.a *= alpha; _mpb.SetColor(BaseColorId, c); d.r.SetPropertyBlock(_mpb);
        }

        void Update()
        {
            float now = Time.time;
            foreach (var d in _decals)
            {
                if (!d.active) continue;
                float age = now - d.born;
                if (d.growTime > 0f && age < d.growTime) { float k = age / d.growTime; d.t.localScale = Vector3.one * d.size * Mathf.Lerp(d.grow, 1f, 1 - (1 - k) * (1 - k)); }
                float fadeStart = d.life * 0.75f;
                if (age > d.life || GameSettings.Blood == BloodLevel.Off) { d.active = false; d.r.gameObject.SetActive(false); }
                else if (age > fadeStart) Apply(d, 1f - (age - fadeStart) / (d.life - fadeStart));
            }
        }

        public void ClearAll() { foreach (var d in _decals) { d.active = false; if (d.r) d.r.gameObject.SetActive(false); } }
        void OnDestroy() { if (_inst == this) _inst = null; }
    }

    /// <summary>one place that decides what blood a hit / death / wound produces (spray, ground splats, dripping wound)</summary>
    public static class BloodFX
    {
        /// <summary>a creature (or the player) is hit: spray along the blow, droplets on the ground, a bleeding wound on the nearest bone</summary>
        public static void Hit(Vector3 point, Vector3 dir, float size, bool heavy, Transform body, Transform[] bones = null, float woundTime = 6f)
        {
            var lvl = GameSettings.Blood;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            VfxPool.Instance.Play(heavy ? VfxId.BloodSprayHeavy : VfxId.BloodSpray, point, dir + Vector3.up * 0.25f, null, Mathf.Clamp(size, 0.4f, 2.5f));
            if (lvl == BloodLevel.Off) return;
            int n = lvl == BloodLevel.Normal ? (heavy ? 4 : 2) : 1;
            for (int i = 0; i < n; i++)
            {
                var off = dir * Random.Range(0.3f, 1.2f) * size + Random.insideUnitSphere * 0.4f * size;
                BloodDecals.Instance.Splat(point + off, Random.Range(0.25f, 0.6f) * Mathf.Clamp(size, 0.5f, 2f), body);
            }
            if (lvl == BloodLevel.Normal && bones != null && bones.Length > 0)
            {
                Transform best = null; float bd = float.MaxValue;
                foreach (var b in bones) { if (!b) continue; float d = (b.position - point).sqrMagnitude; if (d < bd) { bd = d; best = b; } }
                if (best)
                {
                    var fx = VfxPool.Instance.Play(VfxId.Bleed, point, Quaternion.identity, best, Mathf.Clamp(size, 0.5f, 2f));
                    if (fx) fx.StopAfter(woundTime);
                }
            }
        }

        public static void Drip(Vector3 at, float size, Transform body)
        {
            if (GameSettings.Blood != BloodLevel.Normal) return;
            BloodDecals.Instance.Splat(at, Random.Range(0.12f, 0.3f) * Mathf.Clamp(size, 0.5f, 2f), body, 60f);
        }

        public static void Death(Vector3 at, float radius, Transform body)
        {
            if (GameSettings.Blood != BloodLevel.Normal) return;
            BloodDecals.Instance.Pool(at, radius, body);
        }
    }
}
