using System.Collections.Generic;
using UnityEngine;

namespace PrimalFrontier.Building
{
    /// <summary>
    /// Stand-in meshes for the building pieces, made with the Mesh API (no ProBuilder): the builder uses them for the
    /// prefabs until the Blender FBX (Tools/BlenderPipeline/build_pieces.py -> Art/Models/Building/BLD_*.fbx) exist, and
    /// tests can build without any asset. Same grid as the definitions (3 m cells, 2.4 m walls, 0.35 m platform), same
    /// three materials in the same order: 0 wood (poles, logs), 1 atlas (wattle, thatch, fronds), 2 stone.
    /// Atlas regions (T_Building, u0 v0 u1 v1): wattle 0 .5 1 1, thatch 0 .25 1 .5, frond 0 0 .5 .25, hide .5 0 .75 .125.
    /// </summary>
    public static class PieceMeshes
    {
        public const int SubWood = 0, SubAtlas = 1, SubStone = 2;
        public static readonly string[] MaterialNames = { "M_BuildWood", "M_BuildAtlas", "M_BuildStone" };
        static readonly Rect Wattle = new Rect(0f, 0.5f, 1f, 0.5f), Thatch = new Rect(0f, 0.25f, 1f, 0.25f), Frond = new Rect(0f, 0f, 0.5f, 0.25f), Hide = new Rect(0.5f, 0f, 0.25f, 0.125f);

        const float Cell = StructureDefinition.Cell, H = StructureDefinition.WallHeight, Top = StructureDefinition.FoundationTop;

        public class Part
        {
            readonly List<Vector3> _v = new List<Vector3>(); readonly List<Vector3> _n = new List<Vector3>(); readonly List<Vector2> _uv = new List<Vector2>();
            readonly List<int>[] _tri = { new List<int>(), new List<int>(), new List<int>() };

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, int sub)
            {
                var n = Vector3.Cross(b - a, c - a).normalized;
                int i = _v.Count;
                _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
                for (int k = 0; k < 4; k++) _n.Add(n);
                _uv.Add(ua); _uv.Add(ub); _uv.Add(uc); _uv.Add(ud);
                var t = _tri[sub]; t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
            }

            static Vector2 R(Rect r, float u, float v) => new Vector2(r.x + r.width * Mathf.Repeat(u, 1f), r.y + r.height * Mathf.Clamp01(v));

            /// <summary>axis box, uvs planar per face in metres (tiles the given region)</summary>
            public void Box(Vector3 c, Vector3 s, Quaternion q, int sub, Rect region, float metresPerTile = 1f)
            {
                Vector3 h = s * 0.5f;
                Vector3 P(float x, float y, float z) => c + q * new Vector3(x * h.x, y * h.y, z * h.z);
                void Face(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, float w, float hh)
                {
                    float uw = w / metresPerTile, vh = hh / metresPerTile;
                    Quad(a, b, cc, d, R(region, 0, 0), R(region, uw, 0), R(region, uw, vh), R(region, 0, vh), sub);
                }
                Face(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), s.x, s.y);        // +z
                Face(P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), s.x, s.y);    // -z
                Face(P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), s.z, s.y);        // +x
                Face(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), s.z, s.y);    // -x
                Face(P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1), s.x, s.z);        // +y
                Face(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), s.x, s.z);    // -y
            }

            /// <summary>a pole between two points (bark wraps around u, tiles along v in metres)</summary>
            public void Pole(Vector3 a, Vector3 b, float r, int segs, int sub, Rect region)
            {
                var d = (b - a); float L = d.magnitude; if (L < 1e-4f) return; d /= L;
                var side = Mathf.Abs(d.y) < 0.95f ? Vector3.Cross(d, Vector3.up).normalized : Vector3.right;
                var up = Vector3.Cross(side, d);
                int i0 = _v.Count;
                for (int k = 0; k <= segs; k++)
                {
                    float ang = k / (float)segs * Mathf.PI * 2f; var off = (side * Mathf.Cos(ang) + up * Mathf.Sin(ang)) * r;
                    _v.Add(a + off); _n.Add(off.normalized); _uv.Add(R(region, k / (float)segs, 0f));
                    _v.Add(b + off); _n.Add(off.normalized); _uv.Add(R(region, k / (float)segs, L / 0.6f));
                }
                var t = _tri[sub];
                for (int k = 0; k < segs; k++)
                {
                    int p = i0 + k * 2;
                    t.Add(p); t.Add(p + 1); t.Add(p + 3); t.Add(p); t.Add(p + 3); t.Add(p + 2);
                }
                // end caps (flat)
                Cap(a, -d, r, segs, sub, region); Cap(b, d, r, segs, sub, region);
            }

            void Cap(Vector3 c, Vector3 n, float r, int segs, int sub, Rect region)
            {
                var side = Mathf.Abs(n.y) < 0.95f ? Vector3.Cross(n, Vector3.up).normalized : Vector3.right; var up = Vector3.Cross(side, n);
                int i0 = _v.Count; _v.Add(c); _n.Add(n); _uv.Add(R(region, 0.5f, 0.5f));
                for (int k = 0; k <= segs; k++)
                {
                    float ang = k / (float)segs * Mathf.PI * 2f; var off = (side * Mathf.Cos(ang) + up * Mathf.Sin(ang)) * r;
                    _v.Add(c + off); _n.Add(n); _uv.Add(R(region, 0.5f + 0.4f * Mathf.Cos(ang), 0.5f + 0.4f * Mathf.Sin(ang)));
                }
                var t = _tri[sub];
                for (int k = 0; k < segs; k++)
                {
                    // winding so the cap faces along n
                    if (Vector3.Dot(Vector3.Cross(_v[i0 + 1 + k] - c, _v[i0 + 2 + k] - c), n) > 0) { t.Add(i0); t.Add(i0 + 1 + k); t.Add(i0 + 2 + k); }
                    else { t.Add(i0); t.Add(i0 + 2 + k); t.Add(i0 + 1 + k); }
                }
            }

            /// <summary>a thick quad (a b c d counter-clockwise seen from its front), extruded back by t; uvs tile the region along ab / ad in metres</summary>
            public void Slab(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t, int sub, Rect region, float uMetres = 1f, float vMetres = 1f, bool fringe = false)
            {
                var n = Vector3.Cross(b - a, d - a).normalized; var back = -n * t;
                float w = (b - a).magnitude, h = (d - a).magnitude;
                float uw = w / uMetres, vh = fringe ? 1f : h / vMetres;
                Quad(a, b, c, d, R(region, 0, 0), R(region, uw, 0), R(region, uw, vh), R(region, 0, vh), sub);
                Quad(a + back, d + back, c + back, b + back, R(region, 0, 0), R(region, 0, vh), R(region, uw, vh), R(region, uw, 0), sub);
                // sides
                Quad(a, a + back, b + back, b, R(region, 0, 0), R(region, 0, 0.05f), R(region, uw, 0.05f), R(region, uw, 0), sub);
                Quad(c, c + back, d + back, d, R(region, 0, 0.95f), R(region, 0, 1f), R(region, uw, 1f), R(region, uw, 0.95f), sub);
                Quad(b, b + back, c + back, c, R(region, uw, 0), R(region, uw, 0.05f), R(region, uw, vh), R(region, uw, vh), sub);
                Quad(d, d + back, a + back, a, R(region, 0, vh), R(region, 0, vh), R(region, 0, 0.05f), R(region, 0, 0), sub);
            }

            /// <summary>a thin triangle (gable end), both faces</summary>
            public void Tri(Vector3 a, Vector3 b, Vector3 c, int sub, Rect region)
            {
                var n = Vector3.Cross(b - a, c - a).normalized;
                int i = _v.Count;
                _v.Add(a); _v.Add(b); _v.Add(c); for (int k = 0; k < 3; k++) _n.Add(n);
                _uv.Add(R(region, 0, 0)); _uv.Add(R(region, 1, 0)); _uv.Add(R(region, 0.5f, 1));
                var t = _tri[sub]; t.Add(i); t.Add(i + 1); t.Add(i + 2);
                i = _v.Count;
                _v.Add(a); _v.Add(c); _v.Add(b); for (int k = 0; k < 3; k++) _n.Add(-n);
                _uv.Add(R(region, 0, 0)); _uv.Add(R(region, 0.5f, 1)); _uv.Add(R(region, 1, 0));
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(_v); m.SetNormals(_n); m.SetUVs(0, _uv);
                m.subMeshCount = 3;
                for (int s = 0; s < 3; s++) m.SetTriangles(_tri[s], s, true);
                m.RecalculateBounds(); m.RecalculateTangents();
                return m;
            }
        }

        // ------------------------------------------------------------------ pieces
        /// <summary>the piece mesh (3 submeshes) for a category and LOD (0 detailed, 1 simple)</summary>
        public static Mesh Piece(StructureCategory cat, int lod)
        {
            switch (cat)
            {
                case StructureCategory.Foundation: return Foundation(lod);
                case StructureCategory.Wall: return Wall(lod, false);
                case StructureCategory.Doorway: return Wall(lod, true);
                case StructureCategory.Roof: return Roof(lod);
                default: return LeanTo(lod);
            }
        }

        static Mesh Foundation(int lod)
        {
            var p = new Part(); float c = Cell * 0.5f;
            if (lod == 0)
            {
                // two bearer logs along z on stones, nine deck logs along x on top
                foreach (float x in new[] { -1.05f, 1.05f })
                    p.Pole(new Vector3(x, 0.14f, -c + 0.05f), new Vector3(x, 0.14f, c - 0.05f), 0.11f, 8, SubWood, Wattle);
                for (int i = 0; i < 9; i++)
                {
                    float z = -c + 0.18f + i * (Cell - 0.36f) / 8f;
                    p.Pole(new Vector3(-c + 0.02f, Top - 0.09f, z), new Vector3(c - 0.02f, Top - 0.09f, z), 0.09f, 7, SubWood, Wattle);
                }
                foreach (var s in new[] { new Vector3(-1.05f, -0.05f, -1.15f), new Vector3(1.05f, -0.05f, -1.15f), new Vector3(-1.05f, -0.05f, 1.15f), new Vector3(1.05f, -0.05f, 1.15f), new Vector3(-1.05f, -0.05f, 0f), new Vector3(1.05f, -0.05f, 0f) })
                    p.Box(s, new Vector3(0.36f, 0.2f, 0.3f), Quaternion.Euler(0, 15f, 0), SubStone, Hide, 0.6f);
            }
            else
            {
                p.Box(new Vector3(0, Top - 0.13f, 0), new Vector3(Cell, 0.26f, Cell), Quaternion.identity, SubWood, Wattle, 0.6f);
                p.Box(new Vector3(0, 0.0f, 0), new Vector3(Cell - 0.7f, 0.2f, Cell - 0.7f), Quaternion.identity, SubStone, Hide, 0.6f);
            }
            return p.ToMesh("BLD_Foundation_LOD" + lod);
        }

        static Mesh Wall(int lod, bool doorway)
        {
            var p = new Part(); float c = Cell * 0.5f;
            if (lod == 0)
            {
                foreach (float x in new[] { -c + 0.1f, -0.5f, 0.5f, c - 0.1f })
                {
                    if (doorway && Mathf.Abs(x) < 0.6f) continue;
                    p.Pole(new Vector3(x, 0f, 0f), new Vector3(x, H, 0f), 0.06f, 7, SubWood, Wattle);
                }
                foreach (float y in new[] { 0.35f, 1.35f, H - 0.15f })
                    p.Pole(new Vector3(-c, y, 0.07f), new Vector3(c, y, 0.07f), 0.045f, 6, SubWood, Wattle);
                if (doorway)
                {
                    // door posts and lintel, two woven side panels
                    foreach (float x in new[] { -0.55f, 0.55f }) p.Pole(new Vector3(x, 0f, 0f), new Vector3(x, 2.0f, 0f), 0.07f, 7, SubWood, Wattle);
                    p.Pole(new Vector3(-0.62f, 2.0f, 0f), new Vector3(0.62f, 2.0f, 0f), 0.06f, 7, SubWood, Wattle);
                    p.Slab(new Vector3(-c + 0.15f, 0.08f, -0.02f), new Vector3(-0.58f, 0.08f, -0.02f), new Vector3(-0.58f, H - 0.08f, -0.02f), new Vector3(-c + 0.15f, H - 0.08f, -0.02f), 0.05f, SubAtlas, Wattle, 2.2f, 2.0f);
                    p.Slab(new Vector3(0.58f, 0.08f, -0.02f), new Vector3(c - 0.15f, 0.08f, -0.02f), new Vector3(c - 0.15f, H - 0.08f, -0.02f), new Vector3(0.58f, H - 0.08f, -0.02f), 0.05f, SubAtlas, Wattle, 2.2f, 2.0f);
                    p.Slab(new Vector3(-0.58f, 2.06f, -0.02f), new Vector3(0.58f, 2.06f, -0.02f), new Vector3(0.58f, H - 0.08f, -0.02f), new Vector3(-0.58f, H - 0.08f, -0.02f), 0.05f, SubAtlas, Wattle, 2.2f, 2.0f);
                }
                else p.Slab(new Vector3(-c + 0.15f, 0.08f, -0.02f), new Vector3(c - 0.15f, 0.08f, -0.02f), new Vector3(c - 0.15f, H - 0.08f, -0.02f), new Vector3(-c + 0.15f, H - 0.08f, -0.02f), 0.05f, SubAtlas, Wattle, 2.2f, 2.0f);
            }
            else
            {
                if (doorway)
                {
                    p.Box(new Vector3(-(c + 0.55f) * 0.5f, H * 0.5f, 0), new Vector3(c - 0.55f, H, 0.12f), Quaternion.identity, SubAtlas, Wattle, 2.2f);
                    p.Box(new Vector3((c + 0.55f) * 0.5f, H * 0.5f, 0), new Vector3(c - 0.55f, H, 0.12f), Quaternion.identity, SubAtlas, Wattle, 2.2f);
                    p.Box(new Vector3(0, (2.0f + H) * 0.5f, 0), new Vector3(1.2f, H - 2.0f, 0.12f), Quaternion.identity, SubAtlas, Wattle, 2.2f);
                }
                else p.Box(new Vector3(0, H * 0.5f, 0), new Vector3(Cell, H, 0.12f), Quaternion.identity, SubAtlas, Wattle, 2.2f);
            }
            return p.ToMesh((doorway ? "BLD_Doorway_LOD" : "BLD_Wall_LOD") + lod);
        }

        /// <summary>the door leaf: hinge at the origin, leaf toward +x, 0.9 x 1.85 m</summary>
        public static Mesh Door(int lod)
        {
            var p = new Part();
            if (lod == 0)
            {
                foreach (float x in new[] { 0.06f, 0.86f }) p.Pole(new Vector3(x, 0.05f, 0f), new Vector3(x, 1.9f, 0f), 0.045f, 6, SubWood, Wattle);
                foreach (float y in new[] { 0.3f, 1.0f, 1.7f }) p.Pole(new Vector3(0.02f, y, 0.05f), new Vector3(0.9f, y, 0.05f), 0.035f, 6, SubWood, Wattle);
                p.Slab(new Vector3(0.08f, 0.1f, -0.02f), new Vector3(0.84f, 0.1f, -0.02f), new Vector3(0.84f, 1.85f, -0.02f), new Vector3(0.08f, 1.85f, -0.02f), 0.04f, SubAtlas, Wattle, 2.2f, 2.0f);
            }
            else p.Box(new Vector3(0.46f, 0.98f, 0f), new Vector3(0.88f, 1.86f, 0.08f), Quaternion.identity, SubAtlas, Wattle, 2.2f);
            return p.ToMesh("BLD_Door_LOD" + lod);
        }

        static Mesh Roof(int lod)
        {
            var p = new Part(); float c = Cell * 0.5f, ridge = 1.15f, eave = c + 0.2f, low = -0.08f;
            // two thatch slopes (front faces outward / upward), ridge along x
            var fl = new Vector3(-c - 0.15f, low, eave); var fr = new Vector3(c + 0.15f, low, eave);
            var rl = new Vector3(-c - 0.15f, ridge, 0f); var rr = new Vector3(c + 0.15f, ridge, 0f);
            var bl = new Vector3(-c - 0.15f, low, -eave); var br = new Vector3(c + 0.15f, low, -eave);
            p.Slab(fl, fr, rr, rl, 0.14f, SubAtlas, Thatch, 2.4f, 0.6f, true);
            p.Slab(br, bl, rl, rr, 0.14f, SubAtlas, Thatch, 2.4f, 0.6f, true);
            if (lod == 0)
            {
                p.Pole(new Vector3(-c - 0.2f, ridge + 0.04f, 0f), new Vector3(c + 0.2f, ridge + 0.04f, 0f), 0.08f, 7, SubWood, Wattle);
                // rafters under each slope
                for (int i = 0; i < 4; i++)
                {
                    float x = -c + 0.3f + i * (Cell - 0.6f) / 3f;
                    p.Pole(new Vector3(x, low - 0.1f, eave - 0.1f), new Vector3(x, ridge - 0.12f, 0f), 0.045f, 6, SubWood, Wattle);
                    p.Pole(new Vector3(x, low - 0.1f, -eave + 0.1f), new Vector3(x, ridge - 0.12f, 0f), 0.045f, 6, SubWood, Wattle);
                }
                // gable ends woven shut
                p.Tri(new Vector3(-c, low + 0.05f, c), new Vector3(-c, low + 0.05f, -c), new Vector3(-c, ridge - 0.05f, 0f), SubAtlas, Wattle);
                p.Tri(new Vector3(c, low + 0.05f, -c), new Vector3(c, low + 0.05f, c), new Vector3(c, ridge - 0.05f, 0f), SubAtlas, Wattle);
            }
            else
            {
                p.Tri(new Vector3(-c, low + 0.05f, c), new Vector3(-c, low + 0.05f, -c), new Vector3(-c, ridge - 0.05f, 0f), SubAtlas, Wattle);
                p.Tri(new Vector3(c, low + 0.05f, -c), new Vector3(c, low + 0.05f, c), new Vector3(c, ridge - 0.05f, 0f), SubAtlas, Wattle);
            }
            return p.ToMesh("BLD_Roof_LOD" + lod);
        }

        static Mesh LeanTo(int lod)
        {
            var p = new Part(); float w = 1.4f, top = 1.75f, front = 0.55f, back = -1.25f;
            // sloped frond roof from the ground at the back up to the ridge pole at the front
            var a = new Vector3(-w, 0.02f, back); var b = new Vector3(w, 0.02f, back); var cc = new Vector3(w, top, front); var d = new Vector3(-w, top, front);
            p.Slab(a, b, cc, d, 0.12f, SubAtlas, Frond, 1.6f, 0.8f);
            p.Pole(new Vector3(-w - 0.05f, top, front), new Vector3(w + 0.05f, top, front), 0.07f, 7, SubWood, Wattle);
            foreach (float x in new[] { -w + 0.1f, w - 0.1f }) p.Pole(new Vector3(x, 0f, front + 0.05f), new Vector3(x, top, front), 0.07f, 7, SubWood, Wattle);
            if (lod == 0)
            {
                for (int i = 0; i < 6; i++)
                {
                    float x = -w + 0.15f + i * (2f * w - 0.3f) / 5f;
                    p.Pole(new Vector3(x, 0.03f, back - 0.05f), new Vector3(x, top - 0.08f, front - 0.06f), 0.04f, 6, SubWood, Wattle);
                }
                // low woven sides
                p.Tri(new Vector3(-w, 0.03f, back + 0.1f), new Vector3(-w, 0.03f, front - 0.1f), new Vector3(-w, top - 0.2f, front - 0.1f), SubAtlas, Frond);
                p.Tri(new Vector3(w, 0.03f, front - 0.1f), new Vector3(w, 0.03f, back + 0.1f), new Vector3(w, top - 0.2f, front - 0.1f), SubAtlas, Frond);
            }
            return p.ToMesh("BLD_LeanTo_LOD" + lod);
        }

        /// <summary>a piece object with MeshFilter / MeshRenderer children <name>_LOD0 / _LOD1 and a LODGroup (tests, builder)</summary>
        public static GameObject Build(StructureCategory cat, string name, Material[] mats)
        {
            var root = new GameObject(name);
            var lods = new LOD[2];
            for (int l = 0; l < 2; l++)
            {
                var go = new GameObject(name + "_LOD" + l); go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = Piece(cat, l);
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = mats ?? new Material[3];
                lods[l] = new LOD(l == 0 ? 0.35f : 0.05f, new Renderer[] { mr });
            }
            var lg = root.AddComponent<LODGroup>(); lg.SetLODs(lods); lg.RecalculateBounds();
            return root;
        }
    }
}
