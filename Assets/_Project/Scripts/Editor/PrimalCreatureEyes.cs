using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using PrimalFrontier.Core;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// Readable eyes for the creatures: the original eyes are small and sunk into the skin, so from game distance the
    /// faces looked blank. This adds a glossy eyeball (Eye_L / Eye_R) under each creature's Head bone at the eye
    /// position of its Blender build (characters/work/dino/*_joints.json, values copied below), a little larger and
    /// further out, with an iris texture (round pupil for plant eaters, slit for hunters) and a species colour.
    /// The eyes are added to LOD0 / LOD1 of the creature's LOD group. DinoLife makes them blink and glance.
    /// PC phase (DINO, 2026-09-30): per-species size (Spec.k), iris colour baked into a per-species texture with a
    /// catchlight (T_CreatureEye_<id>), wetter material, and skin eyelids (EyeLid_L / EyeLid_R: a thin shell with an almond
    /// opening, heavier upper lid on hunters, coloured with the average skin texel around the eye), so the eye sits in a
    /// socket instead of on the skin. Previewed in Blender: scripts/pf_dino_eyes_preview.py.
    /// The Rift Tyrant (hero model) already has modelled eyes and lids and is left alone.
    /// Safe to run again: prefabs that already have Eye_L / Eye_R are skipped (arg "force" rebuilds them).
    /// </summary>
    public static class PrimalCreatureEyes
    {
        struct Spec { public string id; public float x, y, z, r; public Color iris; public bool slit; public float k; }
        // Blender model space (x right, -y forward, z up), eye radius, iris colour, pupil shape
        static readonly Spec[] Specs =
        {
            new Spec { id = "Triceratops",     x = 0.30f,  y = -3.2713f, z = 1.8238f, r = 0.055f, iris = new Color(0.78f, 0.50f, 0.16f), k = 1.25f },
            new Spec { id = "Parasaurolophus", x = 0.14f,  y = -2.9331f, z = 3.3136f, r = 0.040f, iris = new Color(0.85f, 0.62f, 0.20f), k = 1.3f },
            new Spec { id = "Ankylosaurus",    x = 0.27f,  y = -2.6120f, z = 1.0177f, r = 0.035f, iris = new Color(0.72f, 0.64f, 0.22f), k = 1.35f },
            new Spec { id = "Velociraptor",    x = 0.035f, y = -0.6735f, z = 0.9200f, r = 0.013f, iris = new Color(1.00f, 0.72f, 0.14f), slit = true, k = 1.5f },
            new Spec { id = "Carnotaurus",     x = 0.15f,  y = -2.9179f, z = 3.1914f, r = 0.035f, iris = new Color(1.00f, 0.50f, 0.10f), slit = true, k = 1.35f },
            new Spec { id = "Spinosaurus",     x = 0.13f,  y = -4.6654f, z = 4.0661f, r = 0.040f, iris = new Color(0.95f, 0.82f, 0.25f), slit = true, k = 1.3f },
            new Spec { id = "Pteranodon",      x = 0.04f,  y = -0.7422f, z = 0.9194f, r = 0.015f, iris = new Color(0.75f, 0.28f, 0.12f), k = 1.4f },
            new Spec { id = "Mosasaurus",      x = 0.32f,  y = -4.40f,   z = 0.34f,   r = 0.050f, iris = new Color(0.78f, 0.84f, 0.62f), k = 1.25f },
        };
        const string Root = "Assets/Art/Characters/Dinosaurs";
        const string Shared = "Assets/_Project/Art/Characters/Creatures";
        [Tooltip("how far the eyeball sits out of the socket (in eye radii); the size factor vs the modelled eye is per species (Spec.k)")]
        public static float OutK = 0.12f;
        /// <summary>eyelid shell (eye-local units, eyeball radius 0.5): rings (opening scale, radius), almond opening half
        /// angles (deg): horizontal, upper for plant eaters / hunters (hunters get the heavier brow), lower</summary>
        static readonly (float u, float r)[] LidRings = { (0.95f, 0.5f), (1.0f, 0.535f), (1.15f, 0.54f), (1.35f, 0.535f), (1.6f, 0.525f), (1.9f, 0.51f) };
        const int LidSeg = 24; const float LidA = 62f, LidUpHerb = 40f, LidUpPred = 30f, LidLo = 50f;

        [MenuItem("Primal Frontier/Tools/Add Creature Eyes")]
        static void Menu() => EditorUtility.DisplayDialog("Primal Frontier", Build(""), "OK");

        [PrimalBridgeCommand]
        public static string Build(string arg)
        {
            var log = new StringBuilder();
            bool force = arg == "force";
            Directory.CreateDirectory(Shared);
            var mesh = EyeMesh();
            foreach (var s in Specs)
            {
                string path = $"{Root}/{s.id}/Prefab/DINO_{s.id}.prefab";
                if (!AssetDatabase.LoadAssetAtPath<GameObject>(path)) { log.AppendLine($"{s.id}: no prefab at {path}"); continue; }
                var mat = EyeMaterial(s, EyeTexture($"T_CreatureEye_{s.id}", s.slit, s.iris));
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var head = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Head");
                    if (!head) { log.AppendLine($"{s.id}: no Head bone"); continue; }
                    var oldL = head.Find("Eye_L"); var oldR = head.Find("Eye_R");
                    if ((oldL || oldR) && !force) { log.AppendLine($"{s.id}: eyes kept"); continue; }
                    foreach (var nm in new[] { "Eye_L", "Eye_R", "EyeLid_L", "EyeLid_R" }) { var o = head.Find(nm); if (o) Object.DestroyImmediate(o.gameObject); }
                    var anim = root.GetComponentInChildren<Animator>(); var model = anim ? anim.transform : root.transform;
                    // the eye data is in the mesh's bind space and the prefab's default pose can differ from it (head
                    // raised...): find the modelled eyeball's vertices in bind space, read where the skin puts them
                    // in the default pose (baked mesh), and put the new eye there, under the Head bone
                    var smr = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderBy(r => r.name.Contains("LOD0") ? 0 : 1).FirstOrDefault();
                    if (!smr || !smr.sharedMesh) { log.AppendLine($"{s.id}: no skinned mesh"); continue; }
                    var bindV = smr.sharedMesh.vertices;
                    var baked = new Mesh(); smr.BakeMesh(baked, true); var posedV = baked.vertices; Object.DestroyImmediate(baked);
                    var map = FindMapping(bindV, s, out float mapErr);
                    if (map == null) { log.AppendLine($"{s.id}: modelled eye not found (best {mapErr:0.000} m)"); continue; }
                    var centres = new Vector3[2]; bool ok = true;
                    for (int side = 0; side < 2; side++)
                    {
                        Vector3 meshPos = map(new Vector3(side == 0 ? -s.x : s.x, s.y, s.z));
                        Vector3 sum = Vector3.zero; int n = 0; float best = float.MaxValue; float rr = map(new Vector3(s.r, 0f, 0f)).magnitude;
                        // grow the search until enough of the (decimated) eyeball is found
                        for (float grow = 1.1f; grow <= 4.01f && n < 6; grow += 0.5f)
                        {
                            sum = Vector3.zero; n = 0;
                            for (int i = 0; i < bindV.Length; i++)
                            {
                                float d2 = (bindV[i] - meshPos).sqrMagnitude; if (d2 < best) best = d2;
                                if (d2 < rr * rr * grow * grow) { sum += posedV[i]; n++; }
                            }
                        }
                        if (n < 6) { log.AppendLine($"{s.id}: modelled eye not found (nearest vertex {Mathf.Sqrt(best):0.000} m)"); ok = false; break; }
                        centres[side] = smr.transform.TransformPoint(sum / n);
                    }
                    if (!ok) continue;
                    Vector3 mid = (centres[0] + centres[1]) * 0.5f;
                    Vector3 headFwd = (mid - head.position).normalized;
                    var made = new List<Renderer>();
                    var skinUv = smr.sharedMesh.uv;
                    var skinIdx = SkinVertices(smr);
                    var lidMat = LidMaterial(s.id, smr, skinUv, skinIdx, posedV, smr.transform.InverseTransformPoint(centres[0]), map(new Vector3(s.r, 0f, 0f)).magnitude * 3f);
                    for (int side = 0; side < 2; side++)
                    {
                        Vector3 lateral = (centres[side] - mid).normalized;
                        Vector3 outW = (lateral + headFwd * 0.55f + model.up * 0.1f).normalized;
                        float scale = model.lossyScale.x;
                        var e = new GameObject(Vector3.Dot(lateral, model.right) < 0 ? "Eye_L" : "Eye_R");
                        e.transform.SetParent(head, false);
                        e.transform.position = centres[side] + outW * s.r * OutK * scale;
                        e.transform.rotation = Quaternion.LookRotation(outW, model.up);
                        float dia = s.r * s.k * 2f * scale; var ls = head.lossyScale;
                        e.transform.localScale = new Vector3(dia / ls.x, dia / ls.y, dia / ls.z);
                        e.AddComponent<MeshFilter>().sharedMesh = mesh;
                        var mr = e.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
                        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = true;
                        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
                        made.Add(mr);
                        // skin eyelid shell with an almond opening: the eye sits in a socket instead of on the skin
                        if (lidMat != null && skinUv != null && skinUv.Length == posedV.Length)
                        {
                            string sideName = e.name.EndsWith("_L") ? "L" : "R";
                            var lid = new GameObject("EyeLid_" + sideName);
                            lid.transform.SetParent(head, false);
                            lid.transform.SetPositionAndRotation(e.transform.position, e.transform.rotation);
                            lid.transform.localScale = e.transform.localScale;
                            var lm = LidMesh(s, sideName);
                            lid.AddComponent<MeshFilter>().sharedMesh = lm;
                            var lr = lid.AddComponent<MeshRenderer>(); lr.sharedMaterial = lidMat;
                            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = true;
                            made.Add(lr);
                            log.AppendLine($"{s.id}: lid {sideName} (skin colour {lidMat.GetColor("_BaseColor")})");
                        }
                    }
                    var lg = root.GetComponentInChildren<LODGroup>();
                    if (lg)
                    {
                        var lods = lg.GetLODs();
                        for (int i = 0; i < Mathf.Min(2, lods.Length); i++)
                            lods[i].renderers = lods[i].renderers.Where(r => r && !r.name.StartsWith("Eye_") && !r.name.StartsWith("EyeLid_")).Concat(made).ToArray();
                        lg.SetLODs(lods);
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    log.AppendLine($"{s.id}: eyes added (r {s.r * s.k:0.000} m, {(s.slit ? "slit" : "round")} pupil, catchlight, {(lidMat != null ? "skin lids" : "no lids")}){(lg ? ", in LOD0/LOD1" : "")}");
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[PrimalCreatureEyes]\n" + log);
            return log.ToString();
        }

        /// <summary>
        /// The mesh's vertex space depends on how the FBX was written (axis conversion, units). Try every axis order /
        /// sign and cm / m, keep the one that puts both build eye centres on modelled vertices.
        /// </summary>
        static System.Func<Vector3, Vector3> FindMapping(Vector3[] v, Spec s, out float err)
        {
            int[][] perms = { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
            System.Func<Vector3, Vector3> best = null; err = float.MaxValue;
            // sample the vertices for speed
            int step = Mathf.Max(1, v.Length / 20000);
            foreach (var pm in perms)
                for (int sg = 0; sg < 8; sg++)
                    foreach (float k in new[] { 1f, 100f, 0.01f })
                    {
                        var p = pm; int g = sg; float kk = k;
                        System.Func<Vector3, Vector3> f = b =>
                        {
                            float[] a = { b.x, b.y, b.z };
                            return new Vector3(a[p[0]] * ((g & 1) != 0 ? -1 : 1), a[p[1]] * ((g & 2) != 0 ? -1 : 1), a[p[2]] * ((g & 4) != 0 ? -1 : 1)) * kk;
                        };
                        float e = 0f;
                        foreach (float sx in new[] { -s.x, s.x })
                        {
                            Vector3 q = f(new Vector3(sx, s.y, s.z)); float m = float.MaxValue;
                            for (int i = 0; i < v.Length; i += step) { float d = (v[i] - q).sqrMagnitude; if (d < m) m = d; }
                            e = Mathf.Max(e, Mathf.Sqrt(m) / kk);
                        }
                        if (e < err) { err = e; best = f; }
                    }
            return err < s.r * 3.5f ? best : null;
        }

        /// <summary>diagnostic: bone positions in model space (Unity) for mapping the Blender build data</summary>
        [PrimalBridgeCommand]
        public static string Bones(string arg)
        {
            var sb = new StringBuilder();
            foreach (var s in Specs)
            {
                string path = $"{Root}/{s.id}/Prefab/DINO_{s.id}.prefab";
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (!go) continue;
                var anim = go.GetComponentInChildren<Animator>(); var model = anim ? anim.transform : go.transform;
                sb.AppendLine($"== {s.id} root scale {go.transform.localScale} model {model.name} scale {model.lossyScale}");
                var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
                if (smr) sb.AppendLine($"   smr {smr.name} bounds c {model.InverseTransformPoint(smr.bounds.center)} size {smr.bounds.size} rootBone {(smr.rootBone ? smr.rootBone.name : "-")}");
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                {
                    string n = t.name;
                    if (n == "Head" || n.StartsWith("Neck") || n == "Pelvis" || n == "Hips" || n == "Root" || n.StartsWith("Jaw") || n.Contains("Snout") || n == "Spine_01" || n.StartsWith("Eye"))
                        sb.AppendLine(System.FormattableString.Invariant($"   {n}: {model.InverseTransformPoint(t.position).x:F3},{model.InverseTransformPoint(t.position).y:F3},{model.InverseTransformPoint(t.position).z:F3}"));
                }
            }
            return sb.ToString();
        }

        // ---- shared assets
        static Mesh EyeMesh()
        {
            string p = $"{Shared}/MESH_CreatureEye.asset";
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(p);
            if (m) return m;
            const int lat = 8, lon = 14;
            var v = new System.Collections.Generic.List<Vector3>(); var uv = new System.Collections.Generic.List<Vector2>(); var tri = new System.Collections.Generic.List<int>();
            for (int i = 0; i <= lat; i++)
            {
                float a = Mathf.PI * i / lat;                                     // 0 = front pole (+Z)
                for (int j = 0; j <= lon; j++)
                {
                    float b = 2f * Mathf.PI * j / lon;
                    var p3 = new Vector3(Mathf.Sin(a) * Mathf.Cos(b), Mathf.Sin(a) * Mathf.Sin(b), Mathf.Cos(a)) * 0.5f;
                    v.Add(p3); uv.Add(new Vector2(p3.x + 0.5f, p3.y + 0.5f));      // iris projected on the front
                }
            }
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    int a0 = i * (lon + 1) + j, a1 = a0 + 1, b0 = a0 + lon + 1, b1 = b0 + 1;
                    tri.AddRange(new[] { a0, b0, a1, a1, b0, b1 });
                }
            m = new Mesh { name = "MESH_CreatureEye" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds();
            // outward normals (the sphere is centred): exact
            var n = v.Select(x => x.normalized).ToArray(); m.normals = n; m.RecalculateTangents();
            AssetDatabase.CreateAsset(m, p);
            return m;
        }

        static Texture2D EyeTexture(string name, bool slit, Color iris)
        {
            string p = $"{Shared}/{name}.png";
            const int N = 128;
            var tex = new Texture2D(N, N, TextureFormat.RGB24, false);
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2f - 1f, w = (y + 0.5f) / N * 2f - 1f, r = Mathf.Sqrt(u * u + w * w);
                    float ang = Mathf.Atan2(w, u);
                    // iris: fibres + slight inner glow, dark limbal ring at the edge
                    float fib = 0.9f + 0.1f * Mathf.Sin(ang * 37f + Mathf.Sin(ang * 11f) * 2f) + 0.06f * Mathf.Sin(ang * 83f);
                    float g = fib * Mathf.Lerp(1.12f, 0.9f, r);
                    g *= Mathf.Lerp(1f, 0.22f, SS(0.72f, 0.98f, r));
                    bool pupil = slit ? (u / 0.1f) * (u / 0.1f) + (w / 0.72f) * (w / 0.72f) < 1f : r < 0.26f;
                    float pupilEdge = slit ? 0f : SS(0.26f, 0.3f, r);
                    if (pupil) g = 0.02f; else if (!slit) g = Mathf.Lerp(0.02f, g, pupilEdge);
                    if (r > 1f) g = 0.12f;
                    var c = pupil ? new Color(0.015f, 0.015f, 0.015f) : new Color(g * iris.r, g * iris.g, g * iris.b);
                    // catchlight: a small soft highlight above the pupil, so the eye reads as wet and alive in any light
                    float cl = Mathf.Exp(-(u * u + (w - 0.42f) * (w - 0.42f)) / (2f * 0.075f * 0.075f)) * 0.9f;
                    px[y * N + x] = new Color(Mathf.Clamp01(c.r + cl), Mathf.Clamp01(c.g + cl), Mathf.Clamp01(c.b + cl), 1f);
                }
            tex.SetPixels(px); tex.Apply();
            File.WriteAllBytes(p, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(p);
            ti.sRGBTexture = true; ti.mipmapEnabled = true; ti.wrapMode = TextureWrapMode.Clamp; ti.maxTextureSize = 128; ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        }

        /// <summary>vertex indices of the skin sub-mesh (the one whose material is not the modelled eye)</summary>
        static HashSet<int> SkinVertices(SkinnedMeshRenderer smr)
        {
            var m = smr.sharedMesh; var mats = smr.sharedMaterials; var set = new HashSet<int>();
            for (int sm = 0; sm < m.subMeshCount; sm++)
            {
                string mn = sm < mats.Length && mats[sm] ? mats[sm].name : "";
                if (mn.Contains("_Eye") || mn.Contains("Membrane")) continue;
                foreach (int i in m.GetTriangles(sm)) set.Add(i);
            }
            return set;
        }

        /// <summary>
        /// eyelid material: the average skin colour around the eye (read from the creature's diffuse texture at the UVs of
        /// the skin vertices near the eye), a little darker for the socket; lit, both faces drawn (the lid is a thin shell)
        /// </summary>
        static Material LidMaterial(string id, SkinnedMeshRenderer smr, Vector2[] uv, HashSet<int> skinIdx, Vector3[] posedV, Vector3 eyeLocal, float radius)
        {
            var skin = smr.sharedMaterials.FirstOrDefault(m => m && !m.name.Contains("_Eye") && !m.name.Contains("Membrane"));
            Color avg = new Color(0.35f, 0.3f, 0.24f); int cnt = 0;
            var baseTex = skin && skin.HasProperty("_BaseMap") ? skin.GetTexture("_BaseMap") as Texture2D : null;
            string tp = baseTex ? AssetDatabase.GetAssetPath(baseTex) : null;
            if (!string.IsNullOrEmpty(tp) && File.Exists(tp) && uv != null)
            {
                var t = new Texture2D(2, 2);
                if (t.LoadImage(File.ReadAllBytes(tp)))
                {
                    Color sum = Color.black;
                    foreach (int i in skinIdx)
                        if ((posedV[i] - eyeLocal).sqrMagnitude < radius * radius) { sum += t.GetPixelBilinear(uv[i].x, uv[i].y); cnt++; }
                    if (cnt > 0) avg = sum / cnt;
                }
                Object.DestroyImmediate(t);
            }
            if (skin && skin.HasProperty("_BaseColor")) avg *= skin.GetColor("_BaseColor");
            avg *= 0.85f; avg.a = 1f;
            string p = $"{Shared}/M_CreatureLid_{id}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_CreatureLid_" + id }; AssetDatabase.CreateAsset(m, p); }
            m.SetColor("_BaseColor", avg); m.SetFloat("_Smoothness", 0.35f); m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            m.doubleSidedGI = true; m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// Eyelid shell around one eye, in the eye's local space (eyeball radius 0.5): a sphere cap with an almond opening
        /// centred on the eye's forward axis (heavier upper lid on hunters), its margin tucked onto the eyeball.
        /// </summary>
        static Mesh LidMesh(Spec s, string side)
        {
            float up = s.slit ? LidUpPred : LidUpHerb;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var tri = new List<int>();
            foreach (var (u, rad) in LidRings)
                for (int j = 0; j < LidSeg; j++)
                {
                    float ph = 2f * Mathf.PI * j / LidSeg;
                    float b = Mathf.Sin(ph) > 0f ? up : LidLo;
                    float tmax = 1f / Mathf.Sqrt(Mathf.Pow(Mathf.Cos(ph) / LidA, 2f) + Mathf.Pow(Mathf.Sin(ph) / b, 2f));
                    float th = Mathf.Min(u * tmax, 155f) * Mathf.Deg2Rad;
                    var d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Sin(th) * Mathf.Sin(ph), Mathf.Cos(th));
                    v.Add(d * rad); n.Add(d);
                }
            for (int k = 0; k < LidRings.Length - 1; k++)
                for (int j = 0; j < LidSeg; j++)
                {
                    int a0 = k * LidSeg + j, a1 = k * LidSeg + (j + 1) % LidSeg, b0 = a0 + LidSeg, b1 = a1 + LidSeg;
                    tri.AddRange(new[] { a0, b0, a1, a1, b0, b1 });
                }
            string path = $"{Shared}/MESH_CreatureLid_{(s.slit ? "Hunter" : "Grazer")}.asset";
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool isNew = m == null;
            if (isNew) m = new Mesh();
            m.Clear(); m.name = $"MESH_CreatureLid_{(s.slit ? "Hunter" : "Grazer")}";
            m.SetVertices(v); m.SetNormals(n); m.SetTriangles(tri, 0); m.RecalculateBounds();
            if (isNew) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            return m;
        }

        static float SS(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

        static Material EyeMaterial(Spec s, Texture2D tex)
        {
            string p = $"{Shared}/M_CreatureEye_{s.id}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_CreatureEye_" + s.id, enableInstancing = true }; AssetDatabase.CreateAsset(m, p); }
            m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", Color.white);            // iris colour is baked in the texture
            m.SetFloat("_Smoothness", 0.92f); m.SetFloat("_Metallic", 0f);
            m.SetFloat("_EnvironmentReflections", 1f); m.SetFloat("_SpecularHighlights", 1f);
            // a faint glow of the iris colour so the eye reads in shade (the pupil stays black)
            m.EnableKeyword("_EMISSION"); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.SetTexture("_EmissionMap", tex); m.SetColor("_EmissionColor", Color.white * 0.16f);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
