using System.Collections.Generic;
using UnityEngine;

namespace PrimalFrontier.Combat.Weapons
{
    /// <summary>
    /// Reuses Projectile instances per prefab (arrows, thrown spears): Get takes a sleeping one or builds it once,
    /// Return puts it back to sleep, so shooting never Instantiates / Destroys after the first few shots. Ammo without
    /// a model uses one procedural arrow mesh (shaft, knapped stone head, three fletchings), built once and shared.
    /// </summary>
    public static class ProjectilePool
    {
        static readonly Dictionary<Object, Stack<Projectile>> Free = new Dictionary<Object, Stack<Projectile>>();
        static Transform _root;
        static Mesh _arrowMesh; static Material[] _arrowMats;
        /// <summary>key used for ammo without a model (the procedural arrow)</summary>
        static Object _proceduralKey;
        public static int Created { get; private set; }

        static Transform Root
        {
            get
            {
                if (_root) return _root;
                var go = new GameObject("[ProjectilePool]");
                if (Application.isPlaying) Object.DontDestroyOnLoad(go);
                _root = go.transform;
                return _root;
            }
        }

        /// <summary>a ready (active, unparented) projectile for this model; null prefab = the procedural arrow</summary>
        public static Projectile Get(GameObject prefab)
        {
            Object key = prefab ? prefab : ProceduralKey();
            if (!Free.TryGetValue(key, out var stack)) Free[key] = stack = new Stack<Projectile>();
            while (stack.Count > 0)
            {
                var p = stack.Pop();
                if (!p) continue;                                   // destroyed with a scene
                p.transform.SetParent(null, false);
                p.gameObject.SetActive(true);
                return p;
            }
            GameObject go;
            if (prefab)
            {
                go = Object.Instantiate(prefab);
                foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
            }
            else go = CreateArrowModel(null);
            go.name = "Projectile_" + (prefab ? prefab.name : "Arrow");
            var proj = go.GetComponent<Projectile>();
            if (!proj) proj = go.AddComponent<Projectile>();
            proj.PoolKey = key;
            Created++;
            return proj;
        }

        /// <summary>puts it back to sleep (Projectile calls this instead of Destroy)</summary>
        public static void Return(Projectile p)
        {
            if (!p) return;
            if (p.PoolKey == null) { Object.Destroy(p.gameObject); return; }
            p.gameObject.SetActive(false);
            p.transform.SetParent(Root, false);
            if (!Free.TryGetValue(p.PoolKey, out var stack)) Free[p.PoolKey] = stack = new Stack<Projectile>();
            stack.Push(p);
        }

        static Object ProceduralKey()
        {
            if (_proceduralKey) return _proceduralKey;
            ArrowMesh();
            _proceduralKey = _arrowMesh;
            return _proceduralKey;
        }

        // ------------------------------------------------------------------ procedural arrow
        /// <summary>
        /// a new arrow model (shared mesh and materials) under parent: tip along +Y, pivot a little behind the middle
        /// (flight raycasts start there), nock at y = -0.40, tip at y = +0.38.
        /// </summary>
        public static GameObject CreateArrowModel(Transform parent)
        {
            var go = new GameObject("Arrow_Procedural");
            if (parent) go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = ArrowMesh();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = ArrowMaterials();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go;
        }

        public static Mesh ArrowMesh()
        {
            if (_arrowMesh) return _arrowMesh;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var shaft = new List<int>(); var head = new List<int>(); var fletch = new List<int>();
            const float Nock = -0.40f, NeckY = 0.30f, TipY = 0.38f, R = 0.0045f;
            // shaft: 6-sided tube
            const int Sides = 6;
            for (int i = 0; i < Sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / Sides, a1 = (i + 1) * Mathf.PI * 2f / Sides;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                int b = v.Count;
                v.Add(d0 * R + Vector3.up * Nock); v.Add(d1 * R + Vector3.up * Nock); v.Add(d1 * R + Vector3.up * NeckY); v.Add(d0 * R + Vector3.up * NeckY);
                n.Add(d0); n.Add(d1); n.Add(d1); n.Add(d0);
                shaft.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
            }
            // head: 4-sided knapped point, a little flattened
            Vector3 tip = Vector3.up * TipY;
            Vector3[] ring = { new Vector3(0.012f, NeckY, 0), new Vector3(0, NeckY, 0.005f), new Vector3(-0.012f, NeckY, 0), new Vector3(0, NeckY, -0.005f) };
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = ring[i], c = ring[(i + 1) % 4];
                Vector3 fn = Vector3.Cross(c - a, tip - a).normalized;
                int b = v.Count;
                v.Add(a); v.Add(tip); v.Add(c); n.Add(-fn); n.Add(-fn); n.Add(-fn);
                head.AddRange(new[] { b, b + 1, b + 2 });
            }
            // fletching: three vanes, both faces
            for (int i = 0; i < 3; i++)
            {
                float ang = i * Mathf.PI * 2f / 3f;
                Vector3 d = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)), side = Vector3.Cross(Vector3.up, d);
                Vector3 p0 = d * R + Vector3.up * (Nock + 0.02f), p1 = d * R + Vector3.up * (Nock + 0.15f), p2 = d * 0.018f + Vector3.up * (Nock + 0.12f), p3 = d * 0.016f + Vector3.up * (Nock + 0.03f);
                int b = v.Count;
                v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3); for (int k = 0; k < 4; k++) n.Add(side);
                v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3); for (int k = 0; k < 4; k++) n.Add(-side);
                fletch.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
                fletch.AddRange(new[] { b + 4, b + 6, b + 5, b + 4, b + 7, b + 6 });
            }
            var m = new Mesh { name = "Arrow_Procedural" };
            m.SetVertices(v); m.SetNormals(n);
            m.subMeshCount = 3;
            m.SetTriangles(shaft, 0); m.SetTriangles(head, 1); m.SetTriangles(fletch, 2);
            m.RecalculateBounds();
            _arrowMesh = m;
            return m;
        }

        static Material[] ArrowMaterials()
        {
            if (_arrowMats != null && _arrowMats[0]) return _arrowMats;
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (!sh) sh = Shader.Find("Standard");
            _arrowMats = new[] { Mat(sh, "M_Arrow_Shaft", new Color(0.47f, 0.34f, 0.2f)), Mat(sh, "M_Arrow_Head", new Color(0.3f, 0.3f, 0.32f)), Mat(sh, "M_Arrow_Fletch", new Color(0.78f, 0.74f, 0.66f)) };
            return _arrowMats;
        }

        static Material Mat(Shader sh, string name, Color c)
        {
            var m = new Material(sh) { name = name, enableInstancing = true };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.2f);
            return m;
        }
    }
}
