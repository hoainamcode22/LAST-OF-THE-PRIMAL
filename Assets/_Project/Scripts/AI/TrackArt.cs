using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PrimalFrontier.AI
{
    /// <summary>
    /// Original procedural art for the tracking signs: one 4 x 4 decal atlas (footprints of five foot shapes, flattened
    /// plants, blood drops, claw grooves, a stain), low-poly dung piles and bone scatter, and a two-frame bird silhouette.
    /// Everything is generated from code (no imported art), shared, and made once.
    /// </summary>
    public static class TrackArt
    {
        public const int Cells = 4, CellPx = 128, AtlasPx = Cells * CellPx;
        public enum Cell { Theropod = 0, Dromaeosaur = 1, Hadrosaur = 2, Ceratopsian = 3, Ankylosaur = 4, Flattened = 5, Blood = 6, Scratch = 7, ScratchSmall = 8, Stain = 9 }
        public const int CellCount = 10;

        static Mesh[] _quads, _mirrored; static Mesh _dung, _bones;
        static Material _decal, _dungM, _dungOldM, _bonesM, _birdM;
        static readonly Material[] _decalBuckets = new Material[4];
        static bool _init;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _init = false; }

        /// <summary>the quad of one atlas cell; mirrored = the texture flipped left / right (left feet)</summary>
        public static Mesh Quad(Cell c, bool mirrored = false) { Ensure(); return mirrored ? _mirrored[(int)c] : _quads[(int)c]; }
        public static Mesh Dung { get { Ensure(); return _dung; } }
        public static Mesh Bones { get { Ensure(); return _bones; } }
        /// <summary>decal material at one of four fade steps (0 = full, 3 = faint)</summary>
        public static Material Decal(int bucket) { Ensure(); return _decalBuckets[Mathf.Clamp(bucket, 0, 3)]; }
        public static Material DungMaterial(bool old) { Ensure(); return old ? _dungOldM : _dungM; }
        public static Material BonesMaterial { get { Ensure(); return _bonesM; } }
        public static Material BirdMaterial { get { Ensure(); return _birdM; } }
        public static readonly float[] BucketAlpha = { 1f, 0.72f, 0.46f, 0.22f };

        static void Ensure()
        {
            if (_init && _decal) return;
            _init = true;
            _quads = new Mesh[CellCount]; _mirrored = new Mesh[CellCount];
            for (int i = 0; i < CellCount; i++) { _quads[i] = CellQuad(i, false); _mirrored[i] = CellQuad(i, true); }
            _dung = DungMesh(); _bones = BonesMesh();
            var art = Resources.Load<WildlifeArt>("WildlifeArt");
            Texture2D nrm = null;
            _decal = art && art.decals ? art.decals : DecalMaterial(Atlas(out nrm), nrm);
            _dungM = art && art.dung ? art.dung : OpaqueMaterial("Wildlife_Dung", new Color(0.2f, 0.14f, 0.075f), 0.5f);
            _dungOldM = art && art.dungOld ? art.dungOld : OpaqueMaterial("Wildlife_DungOld", new Color(0.33f, 0.27f, 0.18f), 0.08f);
            _bonesM = art && art.bones ? art.bones : OpaqueMaterial("Wildlife_Bones", new Color(0.76f, 0.71f, 0.6f), 0.22f);
            _birdM = art && art.birds ? art.birds : BirdMaterialMake(BirdSheet());
            for (int b = 0; b < 4; b++)
            {
                var m = new Material(_decal) { name = _decal.name + "_" + b, enableInstancing = true };
                var col = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white; col.a = BucketAlpha[b];
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
                _decalBuckets[b] = m;
            }
        }

        // ------------------------------------------------------------------ materials
        static Shader Lit() => Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        public static Material DecalMaterial(Texture2D atlas, Texture2D normals = null)
        {
            var m = new Material(Lit()) { name = "Wildlife_Decals", enableInstancing = true };
            m.SetTexture("_BaseMap", atlas); m.SetColor("_BaseColor", Color.white);
            if (normals && m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", normals); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f); m.SetFloat("_Smoothness", 0.12f); m.SetFloat("_Metallic", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent - 10;
            return m;
        }

        public static Material OpaqueMaterial(string name, Color c, float smooth)
        {
            var m = new Material(Lit()) { name = name, enableInstancing = true };
            m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0f);
            return m;
        }

        public static Material BirdMaterialMake(Texture2D sheet)
        {
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            var m = new Material(sh) { name = "Wildlife_Birds" };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", sheet); else m.mainTexture = sheet;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Surface")) { m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); }
            if (m.HasProperty("_SrcBlend")) { m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); }
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        // ------------------------------------------------------------------ meshes
        /// <summary>a 1 x 1 quad in XZ (normal +Y, texture up = +Z) showing one atlas cell</summary>
        static Mesh CellQuad(int cell, bool mirrored)
        {
            float inset = 1.5f / AtlasPx;
            float u0 = (cell % Cells) / (float)Cells + inset, v0 = (cell / Cells) / (float)Cells + inset;
            float u1 = u0 + 1f / Cells - inset * 2f, v1 = v0 + 1f / Cells - inset * 2f;
            if (mirrored) { float tmp = u0; u0 = u1; u1 = tmp; }
            var m = new Mesh { name = "TrackQuad_" + (Cell)cell + (mirrored ? "_M" : "") };
            m.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f) };
            m.uv = new[] { new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1) };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            var tg = mirrored ? new Vector4(-1, 0, 0, 1) : new Vector4(1, 0, 0, 1);             // u runs the other way on a mirrored quad
            m.tangents = new[] { tg, tg, tg, tg };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.bounds = new Bounds(Vector3.zero, new Vector3(1f, 0.1f, 1f));
            return m;
        }

        /// <summary>a lumpy pile of three to five squashed blobs, about 1 m across and 0.45 m high (scaled per species)</summary>
        static Mesh DungMesh()
        {
            var rnd = new System.Random(4127);
            var verts = new List<Vector3>(); var tris = new List<int>();
            int blobs = 4;
            for (int b = 0; b < blobs; b++)
            {
                float a = b * 2.4f + (float)rnd.NextDouble();
                float rad = b == 0 ? 0f : 0.22f + (float)rnd.NextDouble() * 0.12f;
                var c = new Vector3(Mathf.Cos(a) * rad, 0f, Mathf.Sin(a) * rad);
                float size = b == 0 ? 0.36f : 0.2f + (float)rnd.NextDouble() * 0.1f;
                Blob(verts, tris, c, new Vector3(size, size * (b == 0 ? 1.1f : 0.8f), size), rnd, 0.18f);
            }
            var m = new Mesh { name = "WildlifeDung" };
            m.SetVertices(verts); m.SetTriangles(tris, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>five to seven bones lying flat: long shafts with knobbed ends, a curved rib or two</summary>
        static Mesh BonesMesh()
        {
            var rnd = new System.Random(911);
            var verts = new List<Vector3>(); var tris = new List<int>();
            for (int i = 0; i < 6; i++)
            {
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f, r = (float)rnd.NextDouble() * 0.45f;
                var c = new Vector3(Mathf.Cos(a) * r, 0.05f, Mathf.Sin(a) * r);
                float len = 0.35f + (float)rnd.NextDouble() * 0.45f, thick = 0.035f + (float)rnd.NextDouble() * 0.03f;
                float yaw = (float)rnd.NextDouble() * 360f;
                bool rib = i >= 4;
                Bone(verts, tris, c, Quaternion.Euler(0f, yaw, 0f), len, thick, rib ? 0.35f : 0f);
            }
            var m = new Mesh { name = "WildlifeBones" };
            m.SetVertices(verts); m.SetTriangles(tris, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        static void Blob(List<Vector3> v, List<int> t, Vector3 c, Vector3 s, System.Random rnd, float noise)
        {
            const int seg = 9, rings = 6;
            int b = v.Count;
            for (int r = 0; r <= rings; r++)
            {
                float phi = Mathf.Lerp(0f, Mathf.PI * 0.5f, r / (float)rings);           // upper half: a pile sits on the ground
                for (int k = 0; k < seg; k++)
                {
                    float th = k / (float)seg * Mathf.PI * 2f;
                    float n = 1f + ((float)rnd.NextDouble() - 0.5f) * noise * (r > 0 && r < rings ? 1f : 0.3f);
                    var p = new Vector3(Mathf.Cos(th) * Mathf.Cos(phi) * n, Mathf.Sin(phi), Mathf.Sin(th) * Mathf.Cos(phi) * n);
                    v.Add(c + Vector3.Scale(p, s));
                }
            }
            for (int r = 0; r < rings; r++)
                for (int k = 0; k < seg; k++)
                {
                    int a0 = b + r * seg + k, a1 = b + r * seg + (k + 1) % seg, b0 = a0 + seg, b1 = a1 + seg;
                    t.Add(a0); t.Add(b0); t.Add(a1); t.Add(a1); t.Add(b0); t.Add(b1);
                }
        }

        static void Bone(List<Vector3> v, List<int> t, Vector3 c, Quaternion rot, float len, float thick, float bend)
        {
            const int seg = 6, rings = 8;
            int b = v.Count;
            for (int r = 0; r <= rings; r++)
            {
                float u = r / (float)rings;                      // 0..1 along the bone
                float knob = 1f + 0.9f * Mathf.Pow(Mathf.Abs(u * 2f - 1f), 6f);    // thicker ends
                float x = (u - 0.5f) * len, curve = bend * len * (0.25f - (u - 0.5f) * (u - 0.5f));
                for (int k = 0; k < seg; k++)
                {
                    float th = k / (float)seg * Mathf.PI * 2f;
                    var p = new Vector3(x, Mathf.Sin(th) * thick * knob, curve + Mathf.Cos(th) * thick * knob);
                    v.Add(c + rot * p);
                }
            }
            for (int r = 0; r < rings; r++)
                for (int k = 0; k < seg; k++)
                {
                    int a0 = b + r * seg + k, a1 = b + r * seg + (k + 1) % seg, b0 = a0 + seg, b1 = a1 + seg;
                    t.Add(a0); t.Add(b0); t.Add(a1); t.Add(a1); t.Add(b0); t.Add(b1);
                }
        }

        // ------------------------------------------------------------------ textures
        static readonly Color Soil = new Color(0.2f, 0.145f, 0.095f), Rim = new Color(0.44f, 0.35f, 0.24f);

        static float[] _height;                 // depth of the prints / grooves while the atlas is drawn (normal map)

        /// <summary>the decal atlas (RGBA, mipmapped). Deterministic: the builder bakes the same pixels into an asset</summary>
        public static Texture2D Atlas() { var t = Atlas(out var n); if (Application.isPlaying) Object.Destroy(n); else Object.DestroyImmediate(n); return t; }

        /// <summary>the decal atlas and its normal map (prints read as pressed-in mud, grooves as cut bark)</summary>
        public static Texture2D Atlas(out Texture2D normals)
        {
            _height = new float[AtlasPx * AtlasPx];
            var px = new Color[AtlasPx * AtlasPx];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(Soil.r, Soil.g, Soil.b, 0f);
            var rnd = new System.Random(7351);
            for (int cell = 0; cell < CellCount; cell++)
            {
                int ox = (cell % Cells) * CellPx, oy = (cell / Cells) * CellPx;
                switch ((Cell)cell)
                {
                    case Cell.Flattened: Flattened(px, ox, oy, rnd); break;
                    case Cell.Blood: Drops(px, ox, oy, rnd); break;
                    case Cell.Scratch: Grooves(px, ox, oy, 4, 0.05f, rnd); break;
                    case Cell.ScratchSmall: Grooves(px, ox, oy, 3, 0.025f, rnd); break;
                    case Cell.Stain: Stain(px, ox, oy, rnd); break;
                    default: Print(px, ox, oy, (Cell)cell, rnd); break;
                }
            }
            var tex = new Texture2D(AtlasPx, AtlasPx, TextureFormat.RGBA32, true, false) { name = "Wildlife_TrackAtlas", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            tex.SetPixels(px); tex.Apply(true);
            // normals from the depth field (tangent space, RGB with alpha 1: URP unpacks it like a DXT5nm map)
            var np = new Color[AtlasPx * AtlasPx]; const float strength = 5f;
            for (int y = 0; y < AtlasPx; y++)
                for (int x = 0; x < AtlasPx; x++)
                {
                    float hl = H(x - 1, y), hr = H(x + 1, y), hd = H(x, y - 1), hu = H(x, y + 1);
                    var n = new Vector3(-(hr - hl) * strength, -(hu - hd) * strength, 1f).normalized;
                    np[y * AtlasPx + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            normals = new Texture2D(AtlasPx, AtlasPx, TextureFormat.RGBA32, true, true) { name = "Wildlife_TrackAtlas_N", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            normals.SetPixels(np); normals.Apply(true);
            _height = null;
            return tex;
        }

        static float H(int x, int y) => x < 0 || y < 0 || x >= AtlasPx || y >= AtlasPx ? 0f : _height[y * AtlasPx + x];
        static void Depth(int ox, int oy, int x, int y, float h)
        {
            if (_height == null || x < 3 || y < 3 || x >= CellPx - 3 || y >= CellPx - 3) return;
            int i = (oy + y) * AtlasPx + ox + x;
            if (Mathf.Abs(h) > Mathf.Abs(_height[i])) _height[i] = h;
        }

        static void Put(Color[] px, int ox, int oy, int x, int y, Color c)
        {
            if (x < 3 || y < 3 || x >= CellPx - 3 || y >= CellPx - 3) return;         // keep a clear border (no mip bleed)
            int i = (oy + y) * AtlasPx + ox + x;
            var d = px[i];
            float a = c.a + d.a * (1f - c.a);
            if (a <= 1e-4f) return;
            px[i] = new Color((c.r * c.a + d.r * d.a * (1f - c.a)) / a, (c.g * c.a + d.g * d.a * (1f - c.a)) / a, (c.b * c.a + d.b * d.a * (1f - c.a)) / a, a);
        }

        // signed distance helpers (cell units 0..1)
        static float Capsule(Vector2 p, Vector2 a, Vector2 b, float ra, float rb)
        {
            Vector2 pa = p - a, ba = b - a; float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Mathf.Max(1e-6f, ba.sqrMagnitude));
            return (pa - ba * h).magnitude - Mathf.Lerp(ra, rb, h);
        }
        static float Ellipse(Vector2 p, Vector2 c, Vector2 r) { var q = new Vector2((p.x - c.x) / r.x, (p.y - c.y) / r.y); return (q.magnitude - 1f) * Mathf.Min(r.x, r.y); }
        static float Smin(float a, float b, float k) { float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k); return Mathf.Lerp(b, a, h) - k * h * (1f - h); }

        static float PrintSdf(Cell c, Vector2 p)
        {
            switch (c)
            {
                case Cell.Theropod:
                {
                    // three thick toes with claw tips, the middle one longest, a small heel pad
                    float d = Ellipse(p, new Vector2(0.5f, 0.3f), new Vector2(0.11f, 0.09f));
                    d = Smin(d, Capsule(p, new Vector2(0.5f, 0.34f), new Vector2(0.5f, 0.82f), 0.075f, 0.032f), 0.04f);
                    d = Smin(d, Capsule(p, new Vector2(0.46f, 0.35f), new Vector2(0.26f, 0.69f), 0.066f, 0.029f), 0.04f);
                    d = Smin(d, Capsule(p, new Vector2(0.54f, 0.35f), new Vector2(0.74f, 0.69f), 0.066f, 0.029f), 0.04f);
                    d = Mathf.Min(d, Capsule(p, new Vector2(0.5f, 0.82f), new Vector2(0.5f, 0.9f), 0.02f, 0.004f));             // claw marks
                    d = Mathf.Min(d, Capsule(p, new Vector2(0.26f, 0.69f), new Vector2(0.22f, 0.76f), 0.018f, 0.004f));
                    d = Mathf.Min(d, Capsule(p, new Vector2(0.74f, 0.69f), new Vector2(0.78f, 0.76f), 0.018f, 0.004f));
                    return d;
                }
                case Cell.Dromaeosaur:
                {
                    // two toes pressed in (the sickle-clawed second toe is held up and barely touches)
                    float d = Ellipse(p, new Vector2(0.5f, 0.3f), new Vector2(0.1f, 0.09f));
                    d = Smin(d, Capsule(p, new Vector2(0.47f, 0.35f), new Vector2(0.43f, 0.8f), 0.062f, 0.026f), 0.035f);
                    d = Smin(d, Capsule(p, new Vector2(0.55f, 0.35f), new Vector2(0.61f, 0.75f), 0.056f, 0.024f), 0.035f);
                    d = Mathf.Min(d, Capsule(p, new Vector2(0.43f, 0.8f), new Vector2(0.42f, 0.87f), 0.016f, 0.004f));
                    d = Mathf.Min(d, Capsule(p, new Vector2(0.61f, 0.75f), new Vector2(0.63f, 0.81f), 0.015f, 0.004f));
                    d = Mathf.Min(d, Ellipse(p, new Vector2(0.36f, 0.46f), new Vector2(0.018f, 0.03f)));
                    return d;
                }
                case Cell.Hadrosaur:
                {
                    float d = Ellipse(p, new Vector2(0.5f, 0.36f), new Vector2(0.2f, 0.14f));
                    d = Smin(d, Capsule(p, new Vector2(0.5f, 0.45f), new Vector2(0.5f, 0.78f), 0.1f, 0.085f), 0.05f);
                    d = Smin(d, Capsule(p, new Vector2(0.43f, 0.44f), new Vector2(0.27f, 0.7f), 0.09f, 0.075f), 0.05f);
                    d = Smin(d, Capsule(p, new Vector2(0.57f, 0.44f), new Vector2(0.73f, 0.7f), 0.09f, 0.075f), 0.05f);
                    return d;
                }
                case Cell.Ceratopsian:
                {
                    // a broad sole with four short blunt toes grown into it (a scalloped front edge, not separate pads)
                    float d = Ellipse(p, new Vector2(0.5f, 0.44f), new Vector2(0.23f, 0.2f));
                    d = Smin(d, Ellipse(p, new Vector2(0.32f, 0.6f), new Vector2(0.075f, 0.09f)), 0.07f);
                    d = Smin(d, Ellipse(p, new Vector2(0.44f, 0.65f), new Vector2(0.075f, 0.09f)), 0.07f);
                    d = Smin(d, Ellipse(p, new Vector2(0.56f, 0.65f), new Vector2(0.075f, 0.09f)), 0.07f);
                    d = Smin(d, Ellipse(p, new Vector2(0.68f, 0.6f), new Vector2(0.075f, 0.09f)), 0.07f);
                    return d;
                }
                default:        // Ankylosaur: wide, short and round, three stubby toes merged into the front
                {
                    float d = Ellipse(p, new Vector2(0.5f, 0.45f), new Vector2(0.27f, 0.2f));
                    d = Smin(d, Ellipse(p, new Vector2(0.34f, 0.6f), new Vector2(0.085f, 0.075f)), 0.07f);
                    d = Smin(d, Ellipse(p, new Vector2(0.5f, 0.63f), new Vector2(0.085f, 0.075f)), 0.07f);
                    d = Smin(d, Ellipse(p, new Vector2(0.66f, 0.6f), new Vector2(0.085f, 0.075f)), 0.07f);
                    return d;
                }
            }
        }

        static void Print(Color[] px, int ox, int oy, Cell c, System.Random rnd)
        {
            float px1 = 1f / CellPx;
            for (int y = 0; y < CellPx; y++)
                for (int x = 0; x < CellPx; x++)
                {
                    var p = new Vector2((x + 0.5f) * px1, (y + 0.5f) * px1);
                    float d = PrintSdf(c, p);
                    float grain = ((float)rnd.NextDouble() - 0.5f) * 0.08f;
                    if (d < 0f)
                    {
                        // pressed in: darker and wetter towards the deepest part
                        float depth = Mathf.Clamp01(-d / 0.06f);
                        var col = Color.Lerp(Soil, Soil * 0.62f, depth); col.a = Mathf.Clamp01(Mathf.SmoothStep(0f, 1f, -d / 0.012f) * (0.62f + depth * 0.3f) + grain);
                        Put(px, ox, oy, x, y, col);
                        Depth(ox, oy, x, y, -Mathf.SmoothStep(0f, 1f, -d / 0.02f) * (0.5f + 0.5f * depth));
                    }
                    else if (d < 0.03f)
                    {
                        // squeezed-up mud rim
                        float k = 1f - d / 0.03f; var col = Rim; col.a = Mathf.Clamp01(k * k * 0.42f + grain * 0.5f);
                        Put(px, ox, oy, x, y, col);
                        Depth(ox, oy, x, y, k * k * 0.3f);
                    }
                }
        }

        static void Flattened(Color[] px, int ox, int oy, System.Random rnd)
        {
            var pale = new Color(0.63f, 0.6f, 0.36f); var dark = new Color(0.33f, 0.36f, 0.18f);         // crushed stems go pale
            // trampled ground under the pressed stems: bruised green-brown, soft edges
            var under = new Color(0.25f, 0.24f, 0.13f);
            for (int y = 0; y < CellPx; y++)
                for (int x = 0; x < CellPx; x++)
                {
                    var q = new Vector2((x + 0.5f) / CellPx, (y + 0.5f) / CellPx);
                    float d = Ellipse(q, new Vector2(0.5f, 0.5f), new Vector2(0.38f, 0.43f));
                    if (d > 0.02f) continue;
                    var c = under; c.a = Mathf.Clamp01(-d / 0.12f + 0.15f) * 0.45f;
                    Put(px, ox, oy, x, y, c);
                }
            for (int s = 0; s < 120; s++)
            {
                // stems pressed flat, all bent over the same way (+v, the way the animals went), inside an ellipse
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f, r = Mathf.Sqrt((float)rnd.NextDouble());
                var p0 = new Vector2(0.5f + Mathf.Cos(a) * r * 0.34f, 0.42f + Mathf.Sin(a) * r * 0.34f);
                float ang = ((float)rnd.NextDouble() - 0.5f) * 0.35f, len = 0.14f + (float)rnd.NextDouble() * 0.18f;
                var dir = new Vector2(Mathf.Sin(ang), Mathf.Cos(ang));
                var col = Color.Lerp(dark, pale, (float)rnd.NextDouble()); col.a = 0.55f + (float)rnd.NextDouble() * 0.35f;
                Stroke(px, ox, oy, p0, p0 + dir * len, 1.1f + (float)rnd.NextDouble() * 0.9f, col, 0.03f * ((float)rnd.NextDouble() - 0.5f));
            }
        }

        static void Stroke(Color[] px, int ox, int oy, Vector2 a, Vector2 b, float widthPx, Color c, float curve)
        {
            int steps = Mathf.CeilToInt((b - a).magnitude * CellPx * 1.5f) + 1;
            var n = new Vector2(-(b - a).y, (b - a).x).normalized;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps; var p = Vector2.Lerp(a, b, t) + n * curve * Mathf.Sin(t * Mathf.PI);
                float w = widthPx * (1f - t * 0.6f);
                int cx = Mathf.RoundToInt(p.x * CellPx), cy = Mathf.RoundToInt(p.y * CellPx), rr = Mathf.CeilToInt(w);
                for (int y = -rr; y <= rr; y++)
                    for (int x = -rr; x <= rr; x++)
                    {
                        float dd = Mathf.Sqrt(x * x + y * y); if (dd > w) continue;
                        var col = c; col.a *= 1f - dd / (w + 0.5f);
                        Put(px, ox, oy, cx + x, cy + y, col);
                    }
            }
        }

        static void Drops(Color[] px, int ox, int oy, System.Random rnd)
        {
            var blood = new Color(0.26f, 0.035f, 0.025f);
            for (int s = 0; s < 9; s++)
            {
                var c = new Vector2(0.25f + (float)rnd.NextDouble() * 0.5f, 0.2f + (float)rnd.NextDouble() * 0.6f);
                float r = s == 0 ? 0.1f : 0.025f + (float)rnd.NextDouble() * 0.05f;
                for (int y = 0; y < CellPx; y++)
                    for (int x = 0; x < CellPx; x++)
                    {
                        var p = new Vector2((x + 0.5f) / CellPx, (y + 0.5f) / CellPx);
                        float ang = Mathf.Atan2(p.y - c.y, p.x - c.x);
                        float rr = r * (1f + 0.25f * Mathf.Sin(ang * 5f + s) + 0.12f * Mathf.Sin(ang * 11f + s * 2f));
                        float d = (p - c).magnitude - rr; if (d > 0.004f) continue;
                        var col = blood * (0.8f + (float)rnd.NextDouble() * 0.2f); col.a = Mathf.Clamp01(0.85f * Mathf.SmoothStep(0f, 1f, -d / 0.008f + 0.5f));
                        Put(px, ox, oy, x, y, col);
                    }
            }
        }

        static void Grooves(Color[] px, int ox, int oy, int count, float width, System.Random rnd)
        {
            var edge = new Color(0.15f, 0.1f, 0.065f); var wood = new Color(0.74f, 0.6f, 0.42f);
            float span = width * 2.6f * (count - 1);
            for (int g = 0; g < count; g++)
            {
                float x0 = 0.5f - span * 0.5f + g * width * 2.6f + ((float)rnd.NextDouble() - 0.5f) * 0.02f;
                float slant = 0.08f + ((float)rnd.NextDouble() - 0.5f) * 0.03f;
                float top = 0.9f - (float)rnd.NextDouble() * 0.08f, bottom = 0.1f + (float)rnd.NextDouble() * 0.1f;
                for (int y = 0; y < CellPx; y++)
                {
                    float v = (y + 0.5f) / CellPx; if (v < bottom || v > top) continue;
                    float t = (v - bottom) / (top - bottom);
                    float w = width * Mathf.Sin(t * Mathf.PI) * (0.7f + 0.3f * t);
                    float cx = x0 + slant * (t - 0.5f);
                    for (int x = 0; x < CellPx; x++)
                    {
                        float u = (x + 0.5f) / CellPx, d = Mathf.Abs(u - cx);
                        if (d > w * 1.6f) continue;
                        Color col;
                        if (d < w * 0.55f) { col = Color.Lerp(wood, wood * 0.8f, (float)rnd.NextDouble() * 0.4f); col.a = 0.95f; }      // torn fresh wood
                        else if (d < w) { col = edge; col.a = 0.9f; }                                                                      // dark torn bark edge
                        else { col = edge; col.a = 0.35f * (1f - (d - w) / (w * 0.6f)); }
                        Put(px, ox, oy, x, y, col);
                        if (d < w) Depth(ox, oy, x, y, -(1f - d / w));
                    }
                }
            }
        }

        static void Stain(Color[] px, int ox, int oy, System.Random rnd)
        {
            var c0 = new Color(0.19f, 0.075f, 0.045f);
            float ph = (float)rnd.NextDouble() * 6f;
            for (int y = 0; y < CellPx; y++)
                for (int x = 0; x < CellPx; x++)
                {
                    var p = new Vector2((x + 0.5f) / CellPx - 0.5f, (y + 0.5f) / CellPx - 0.5f);
                    float ang = Mathf.Atan2(p.y, p.x);
                    float r = 0.33f * (1f + 0.18f * Mathf.Sin(ang * 3f + ph) + 0.1f * Mathf.Sin(ang * 7f + ph * 2f));
                    float d = p.magnitude - r; if (d > 0.02f) continue;
                    var col = c0 * (0.85f + (float)rnd.NextDouble() * 0.15f); col.a = Mathf.Clamp01(0.7f * Mathf.SmoothStep(0f, 1f, -d / 0.08f + 0.2f));
                    Put(px, ox, oy, x, y, col);
                }
        }

        /// <summary>two frames side by side (wings up, wings down) of a small dark bird silhouette</summary>
        public static Texture2D BirdSheet()
        {
            const int W = 64, H = 32;
            var px = new Color[W * H];
            var ink = new Color(0.07f, 0.06f, 0.055f, 0f);
            for (int i = 0; i < px.Length; i++) px[i] = ink;
            for (int f = 0; f < 2; f++)
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < 32; x++)
                    {
                        var p = new Vector2((x + 0.5f) / 32f - 0.5f, (y + 0.5f) / H - 0.5f);
                        float body = Ellipse(p, Vector2.zero, new Vector2(0.07f, 0.1f));
                        float lift = f == 0 ? 0.16f : -0.1f;
                        float wl = Capsule(p, new Vector2(-0.04f, 0.02f), new Vector2(-0.4f, lift), 0.06f, 0.015f);
                        float wr = Capsule(p, new Vector2(0.04f, 0.02f), new Vector2(0.4f, lift), 0.06f, 0.015f);
                        float tail = Capsule(p, new Vector2(0f, -0.06f), new Vector2(0f, -0.2f), 0.03f, 0.05f);
                        float d = Mathf.Min(Mathf.Min(body, tail), Mathf.Min(wl, wr));
                        if (d > 0.02f) continue;
                        var c = ink; c.a = Mathf.Clamp01(Mathf.SmoothStep(0f, 1f, -d / 0.02f + 0.5f));
                        px[y * W + f * 32 + x] = c;
                    }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true, false) { name = "Wildlife_BirdSheet", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels(px); tex.Apply(true);
            return tex;
        }
    }
}
