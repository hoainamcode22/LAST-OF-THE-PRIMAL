using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using PrimalFrontier.Core;
using PrimalFrontier.World;
using Object = UnityEngine.Object;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// CAVE (CV_), Phase 2 zone 6: the Deep Water Cave under the cliff arc, World/Environment/Caves/DeepWaterCave.
    /// The existing grotto (PFB_ENV_Cave_Entrance, ENV) ends in a closed back wall at z -145.4, so the new cave has its own
    /// mouths: the main one on the west wall of the waterfall plunge pool (the underground stream leaves the cave there and
    /// runs into the pool) and a narrow back door at the foot of the cliff slope 19 m east of the old grotto. Inside: a stream passage
    /// (with a side alcove pool), the domed chamber with the large still pool (the landmark, faint daylight through a
    /// ceiling cleft), a west passage up to the back door, and a hidden chamber behind a small water curtain and a low
    /// crawl (an old nest of bones).
    /// The rock shell is generated: the cave air is a signed distance field (passages, rooms, pool basins, stream grooves,
    /// value noise), polygonised with surface nets at 0.4 m, clipped where it leaves the terrain, split into 20 m chunks
    /// with MeshColliders. ART's cave kit (Prefabs/Environment/Phase2/*CAVE_*) dresses it when delivered. The terrain gets
    /// holes only at the two mouths (TerrainData backed up once to Art/Terrain/_Backup/TD_Island_before_CV.asset; the mouth
    /// regions are restored from it before every build, so a re-run gives the same holes).
    /// Commands: Build (idempotent: clears and rebuilds only DeepWaterCave + its own generated assets and hole regions),
    /// Check (read only: route walk, holes, slope, headroom, void leaks, cover), SaveScene, Survey / Near (read only).
    /// </summary>
    public static class PrimalCaveBuilder
    {
        const string RootPath = SceneRoots.Caves + "/DeepWaterCave";
        const string CaveDir = "Assets/_Project/Art/Environment/Cave", GenDir = CaveDir + "/Generated", MatDir = CaveDir + "/Materials";
        const string VarDir = MatDir + "/Variants";
        const string BackupPath = "Assets/_Project/Art/Terrain/_Backup/TD_Island_before_CV.asset";
        const string KitDir = "Assets/_Project/Prefabs/Environment/Phase2";
        const float Cell = 0.4f, HoleInset = 0.25f, Lip = 0.5f, ChunkSize = 20f;

        static readonly StringBuilder Log = new StringBuilder();
        static int _warn;
        static void L(string s) { Log.AppendLine(s); }
        static void W(string s) { _warn++; Log.AppendLine("WARNING: " + s); }
        static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        static string V(Vector3 v) => $"({F(v.x)}, {F(v.y)}, {F(v.z)})";

        // ================================================================== route (Unity world coordinates)
        /// <summary>passage node: floor centre (y absolute, or relative to the live terrain when terr), half width, height</summary>
        struct N
        {
            public float x, y, z, w, h; public bool terr;
            public N(float x, float y, float z, float w, float h, bool terr = false) { this.x = x; this.y = y; this.z = z; this.w = w; this.h = h; this.terr = terr; }
        }
        /// <summary>stream passage: waterfall mouth (outside, mouth) -> chamber east edge</summary>
        static readonly N[] PassA =
        {
            new N(92.1f, 0.03f, -156.4f, 2.0f, 3.2f, true),
            new N(90.0f, 0.03f, -157.4f, 2.1f, 3.3f, true),
            new N(85.0f, 19.72f, -159.6f, 2.3f, 3.4f),
            new N(79.2f, 19.92f, -161.6f, 2.5f, 3.6f),
            new N(73.2f, 20.12f, -161.3f, 2.2f, 3.2f),
            new N(67.0f, 20.34f, -165.4f, 2.6f, 3.8f),
            new N(61.4f, 20.54f, -170.2f, 2.3f, 3.4f),
            new N(55.0f, 20.74f, -171.8f, 2.5f, 3.8f),
            new N(49.6f, 20.88f, -173.0f, 3.0f, 4.6f),
        };
        /// <summary>west passage: chamber west edge -> back door in the cliff slope (mouth, outside)</summary>
        static readonly N[] PassB =
        {
            new N(30.4f, 20.92f, -173.4f, 2.6f, 4.2f),
            new N(26.5f, 21.20f, -169.2f, 2.2f, 3.3f),
            new N(22.0f, 21.50f, -165.0f, 2.1f, 3.1f),
            new N(17.5f, 21.80f, -160.0f, 2.3f, 3.3f),
            new N(14.8f, 22.00f, -154.0f, 2.0f, 3.0f),
            new N(13.2f, 22.15f, -148.0f, 1.9f, 2.9f),
            new N(12.4f, 0.03f, -141.4f, 1.9f, 2.9f, true),
            new N(12.15f, 0.03f, -139.2f, 1.8f, 2.8f, true),
        };
        /// <summary>hidden branch: chamber south wall (behind the curtain) -> low crawl -> hidden chamber</summary>
        static readonly N[] PassH =
        {
            new N(41.6f, 20.92f, -180.6f, 1.5f, 2.6f),
            new N(42.2f, 20.97f, -184.4f, 0.95f, 1.3f),
            new N(43.1f, 21.05f, -187.3f, 1.3f, 2.4f),
            new N(44.2f, 21.20f, -190.2f, 1.8f, 3.0f),
        };
        struct R { public string name; public Vector3 c; public float rx, rz, h; public R(string n, Vector3 c, float rx, float rz, float h) { name = n; this.c = c; this.rx = rx; this.rz = rz; this.h = h; } }
        static readonly R Chamber = new R("Chamber", new Vector3(40.0f, 20.90f, -173.6f), 10.5f, 9.6f, 10.8f);
        static readonly R Alcove = new R("Alcove", new Vector3(67.8f, 20.34f, -162.3f), 2.7f, 2.4f, 3.1f);
        static readonly R Hidden = new R("HiddenChamber", new Vector3(44.8f, 21.25f, -192.4f), 4.4f, 3.8f, 4.3f);
        /// <summary>pool basins: centre at the water surface, radii (x, depth, z)</summary>
        static readonly Vector3 PoolC = new Vector3(39.2f, 20.62f, -175.2f), PoolR = new Vector3(5.8f, 1.7f, 4.3f);
        static readonly Vector3 AlcPoolC = new Vector3(68.0f, 20.06f, -161.7f), AlcPoolR = new Vector3(1.5f, 0.75f, 1.2f);
        /// <summary>the ceiling cleft over the pool (daylight shaft)</summary>
        static readonly Vector3 CleftC = new Vector3(37.2f, 31.6f, -174.4f), CleftR = new Vector3(0.45f, 2.3f, 1.7f);
        /// <summary>stream 1 after the mouth: over the rock into the plunge pool (y from the live terrain)</summary>
        static readonly Vector2[] StreamOut = { new Vector2(91.3f, -157.2f), new Vector2(92.3f, -156.0f), new Vector2(93.0f, -154.8f), new Vector2(93.2f, -153.7f) };
        const float PlungePoolLevel = 18.45f;                                  // the river surface just below the plunge pool
        /// <summary>runnel from the curtain foot to the pool</summary>
        static readonly Vector2[] Runnel = { new Vector2(41.7f, -181.9f), new Vector2(41.1f, -180.6f), new Vector2(40.5f, -179.4f), new Vector2(40.2f, -178.7f) };
        static Vector3 MouthE, MouthW;                                           // mouth points (after the live terrain fix-up)

        // ================================================================== signed distance field of the cave air
        class Prim
        {
            public int kind;                     // 0 passage segment, 1 room, 2 basin / cleft (plain union), 3 groove segment
            public Vector3 a, b; public float wa, wb, ha, hb;                       // segment
            public Vector3 c, r;                                                   // room / basin: centre, radii
            public float noise = 1f; public bool hole = true;
            public Vector3 min, max;
        }
        static List<Prim> _prims = new List<Prim>();
        static float _smoothK = 0.7f;

        static float EllipseD(float lat, float dy, float w, float hh)
        {
            float ax = lat / w, ay = dy / hh; float k0 = Mathf.Sqrt(ax * ax + ay * ay);
            float bx = lat / (w * w), by = dy / (hh * hh); float k1 = Mathf.Sqrt(bx * bx + by * by);
            return k1 < 1e-5f ? -Mathf.Min(w, hh) : k0 * (k0 - 1f) / k1;
        }
        static float EllipsoidD(Vector3 p, Vector3 c, Vector3 r)
        {
            var q = p - c; float ax = q.x / r.x, ay = q.y / r.y, az = q.z / r.z; float k0 = Mathf.Sqrt(ax * ax + ay * ay + az * az);
            float bx = q.x / (r.x * r.x), by = q.y / (r.y * r.y), bz = q.z / (r.z * r.z); float k1 = Mathf.Sqrt(bx * bx + by * by + bz * bz);
            return k1 < 1e-5f ? -Mathf.Min(r.x, Mathf.Min(r.y, r.z)) : k0 * (k0 - 1f) / k1;
        }
        /// <summary>smooth intersection: rounds the floor / wall crease (a sharp crease meshes into lit stair-step teeth)</summary>
        static float SMax(float a, float b, float k) => -SMin(-a, -b, k);
        static float SMin(float a, float b, float k) { float h = Mathf.Max(k - Mathf.Abs(a - b), 0f) / k; return Mathf.Min(a, b) - h * h * k * 0.25f; }

        /// <summary>segment frame at p: t along a->b (xz projection), lateral distance, floor y, width, height</summary>
        static void SegAt(Prim s, Vector3 p, out float t, out float lat, out float fy, out float w, out float h)
        {
            float abx = s.b.x - s.a.x, abz = s.b.z - s.a.z, l2 = abx * abx + abz * abz;
            t = l2 < 1e-6f ? 0f : Mathf.Clamp01(((p.x - s.a.x) * abx + (p.z - s.a.z) * abz) / l2);
            float qx = s.a.x + abx * t - p.x, qz = s.a.z + abz * t - p.z;
            lat = Mathf.Sqrt(qx * qx + qz * qz);
            fy = Mathf.Lerp(s.a.y, s.b.y, t); w = Mathf.Lerp(s.wa, s.wb, t); h = Mathf.Lerp(s.ha, s.hb, t);
        }

        static float PrimD(Prim s, Vector3 p, out float floorY, bool noFloor = false)
        {
            switch (s.kind)
            {
                case 0:
                {
                    SegAt(s, p, out _, out float lat, out float fy, out float w, out float h);
                    floorY = fy;
                    float de = EllipseD(lat, p.y - (fy + 0.42f * h), w, 0.6f * h);
                    return noFloor ? de : SMax(de, fy - p.y, 0.3f);
                }
                case 1:
                {
                    floorY = s.c.y;
                    float de = EllipsoidD(p, new Vector3(s.c.x, s.c.y + 0.2f * s.r.y, s.c.z), new Vector3(s.r.x, 0.8f * s.r.y, s.r.z));
                    return noFloor ? de : SMax(de, s.c.y - p.y, 0.3f);
                }
                case 3:
                {
                    SegAt(s, p, out _, out float lat, out float fy, out float w, out float h);
                    floorY = fy;
                    return EllipseD(lat, p.y - fy, w, Mathf.Max(0.02f, h));
                }
                default:
                    floorY = s.c.y;
                    return EllipsoidD(p, s.c, s.r);
            }
        }

        /// <summary>the cave air field: negative = air, positive = rock. forHoles: without grooves / basins and without the floor cut
        /// (terrain just under the cave floor may go: the floor covers it; ApplyHoles keeps anything 0.3 m under the floor)</summary>
        static float Field(Vector3 p, bool forHoles = false)
        {
            float d = 50f, dPlain = 50f, best = 50f, fy = p.y, ns = 1f;
            foreach (var s in _prims)
            {
                if (p.x < s.min.x || p.y < s.min.y || p.z < s.min.z || p.x > s.max.x || p.y > s.max.y || p.z > s.max.z) continue;
                if (forHoles && !s.hole) continue;
                float di = PrimD(s, p, out float f, forHoles);
                if (s.kind == 0 || s.kind == 1) d = SMin(d, di, _smoothK);
                else if (s.kind == 2) d = SMin(d, di, 0.35f);                    // pool basins / cleft: a rounded rim (no stair-step teeth at the waterline)
                else dPlain = Mathf.Min(dPlain, di);
                if (di < best) { best = di; fy = f; ns = s.noise; }
            }
            d = Mathf.Min(d, dPlain);
            if (d > 1.6f || d < -1.6f) return d;
            float above = p.y - fy;
            float amp = ns * Mathf.Lerp(0.03f, 0.42f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 1.3f, above)));
            return d + amp * (Fbm(p) * 2f - 1f);
        }

        static float Hash(int x, int y, int z)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + z * 1274126177;
                h = (h ^ (h >> 13)) * 1274126177; h ^= h >> 16;
                return (h & 0xffffff) / 16777215f;
            }
        }
        static float VNoise(Vector3 p)
        {
            int x0 = Mathf.FloorToInt(p.x), y0 = Mathf.FloorToInt(p.y), z0 = Mathf.FloorToInt(p.z);
            float fx = p.x - x0, fy = p.y - y0, fz = p.z - z0;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy); fz = fz * fz * (3f - 2f * fz);
            float c00 = Mathf.Lerp(Hash(x0, y0, z0), Hash(x0 + 1, y0, z0), fx), c10 = Mathf.Lerp(Hash(x0, y0 + 1, z0), Hash(x0 + 1, y0 + 1, z0), fx);
            float c01 = Mathf.Lerp(Hash(x0, y0, z0 + 1), Hash(x0 + 1, y0, z0 + 1), fx), c11 = Mathf.Lerp(Hash(x0, y0 + 1, z0 + 1), Hash(x0 + 1, y0 + 1, z0 + 1), fx);
            return Mathf.Lerp(Mathf.Lerp(c00, c10, fy), Mathf.Lerp(c01, c11, fy), fz);
        }
        /// <summary>0..1: broad lumps (2.8 m) + finer knobs (1.1 m), slightly stretched vertically (flowstone)</summary>
        static float Fbm(Vector3 p)
        {
            var q = new Vector3(p.x * 0.36f, p.y * 0.3f, p.z * 0.36f);
            return VNoise(q) * 0.66f + VNoise(q * 2.55f + new Vector3(17.3f, 3.1f, 41.7f)) * 0.34f;
        }
        /// <summary>mouth zone (horizontal radius around each mouth): the shell is a solid rock band (inner face, outer skin
        /// ShellT out, top cap just above the terrain) so the terrain hole edges sit inside rock and nothing shows through</summary>
        const float PortalR = 7f, ShellT = 1.25f;                            // band > cell diagonal: a cell touching the air is always inside the band (no terrain teeth)
        static bool InPortal(Vector3 p)
        {
            float dx = p.x - MouthE.x, dz = p.z - MouthE.z; if (dx * dx + dz * dz < PortalR * PortalR) return true;
            dx = p.x - MouthW.x; dz = p.z - MouthW.z; return dx * dx + dz * dz < PortalR * PortalR;
        }
        /// <summary>cap of the rock band: ground in front of a mouth (terrain at the cave floor): 0.1 m over the terrain (the
        /// a low rock apron, 0.12 m, that covers the hole edge on the ground); cliff: 0.45 m over the terrain (a rock lip round the opening)</summary>
        static float CapY(float x, float z)
        {
            float ty = TY(x, z), fl = FloorAt(x, z);
            bool ground = !float.IsNaN(fl) && ty < fl + 0.12f;
            // the lip grows with the terrain's height over the cave floor: a full 0.3 m rim on the cliff, thin on gentle ground
            if (!ground) return float.IsNaN(fl) ? ty + 0.3f : ty + Mathf.Lerp(0.06f, 0.3f, Mathf.InverseLerp(fl + 0.12f, fl + 1.4f, ty));
            // on the ground: a thin rock apron right at the mouth (covers the hole edge at the cliff foot), elsewhere under the turf
            float dm = Mathf.Min(new Vector2(x - MouthE.x, z - MouthE.z).magnitude, new Vector2(x - MouthW.x, z - MouthW.z).magnitude);
            return ty + Mathf.Lerp(0.06f, -0.06f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.6f, 2.8f, dm)));     // tapers: no edge to trip on
        }
        /// <summary>the meshed field: the cave air field, inside the mouth zones the rock band (negative = not rock)</summary>
        static float FieldA(Vector3 p)
        {
            float f = Field(p);
            if (!InPortal(p)) return f;
            return Mathf.Min(ShellT * 0.5f - Mathf.Abs(f - ShellT * 0.5f), CapY(p.x, p.z) - p.y);
        }
        static Vector3 FieldNormal(Vector3 p)
        {
            const float e = 0.12f;
            var g = new Vector3(FieldA(p + Vector3.right * e) - FieldA(p - Vector3.right * e), FieldA(p + Vector3.up * e) - FieldA(p - Vector3.up * e), FieldA(p + Vector3.forward * e) - FieldA(p - Vector3.forward * e));
            return g.sqrMagnitude < 1e-10f ? Vector3.up : (-g).normalized;
        }

        static Terrain _t;
        static float TY(float x, float z) => _t ? _t.SampleHeight(new Vector3(x, 0f, z)) + _t.transform.position.y : 0f;

        /// <summary>resolves terrain-relative nodes (mouth floors never above the next node inside, so the stream runs out) and builds the primitives</summary>
        static N[] Resolve(N[] src, bool mouthFirst, out Vector3 mouth)
        {
            var n = src.ToArray(); mouth = Vector3.zero;
            // mouth-first lists: [outside, mouth, inner...]; mouth-last lists: [..., inner, mouth, outside]
            int iMouth = mouthFirst ? 1 : n.Length - 2, iOut = mouthFirst ? 0 : n.Length - 1, iInner = mouthFirst ? 2 : n.Length - 3;
            if (n[iMouth].terr)
            {
                float y = TY(n[iMouth].x, n[iMouth].z) + n[iMouth].y;
                if (mouthFirst && y > n[iInner].y - 0.05f) { W($"mouth floor {F(y)} above the inner floor {F(n[iInner].y)}: clamped (stream must run out)"); y = n[iInner].y - 0.05f; }
                n[iMouth].y = y; n[iMouth].terr = false;
            }
            if (n[iOut].terr) { n[iOut].y = TY(n[iOut].x, n[iOut].z) + n[iOut].y; n[iOut].terr = false; }
            mouth = new Vector3(n[iMouth].x, n[iMouth].y, n[iMouth].z);
            return n;
        }

        static void AddPassage(N[] n, float noise)
        {
            for (int i = 0; i + 1 < n.Length; i++)
            {
                var s = new Prim { kind = 0, a = new Vector3(n[i].x, n[i].y, n[i].z), b = new Vector3(n[i + 1].x, n[i + 1].y, n[i + 1].z), wa = n[i].w, wb = n[i + 1].w, ha = n[i].h, hb = n[i + 1].h, noise = noise };
                float wm = Mathf.Max(s.wa, s.wb) + 2f, hm = Mathf.Max(s.ha, s.hb) * 1.1f + 2f;
                s.min = Vector3.Min(s.a, s.b) - new Vector3(wm, 2f, wm); s.max = Vector3.Max(s.a, s.b) + new Vector3(wm, hm, wm);
                _prims.Add(s);
            }
        }
        static void AddRoom(R r, float noise)
        {
            var s = new Prim { kind = 1, c = r.c, r = new Vector3(r.rx, r.h, r.rz), noise = noise };
            s.min = r.c - new Vector3(r.rx + 2f, 2f, r.rz + 2f); s.max = r.c + new Vector3(r.rx + 2f, r.h + 2f, r.rz + 2f);
            _prims.Add(s);
        }
        static void AddEllipsoid(Vector3 c, Vector3 r, float noise, bool hole)
        {
            var s = new Prim { kind = 2, c = c, r = r, noise = noise, hole = hole };
            s.min = c - r - Vector3.one * 2f; s.max = c + r + Vector3.one * 2f; _prims.Add(s);
        }
        /// <summary>stream groove along world points (x, floor y, z) with per-point depth</summary>
        static void AddGroove(List<Vector3> pts, List<float> depth, float halfWidth)
        {
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                var s = new Prim { kind = 3, a = pts[i], b = pts[i + 1], wa = halfWidth, wb = halfWidth, ha = depth[i], hb = depth[i + 1], noise = 0.08f, hole = false };
                s.min = Vector3.Min(s.a, s.b) - Vector3.one * 2f; s.max = Vector3.Max(s.a, s.b) + Vector3.one * 2f;
                _prims.Add(s);
            }
        }

        /// <summary>floor height of the cave at (x, z): the passage / room that holds the point best</summary>
        static float FloorAt(float x, float z)
        {
            float best = float.MaxValue, fy = float.NaN;
            foreach (var s in _prims)
            {
                if (s.kind != 0 && s.kind != 1) continue;
                float f0 = s.kind == 0 ? Mathf.Lerp(s.a.y, s.b.y, 0.5f) : s.c.y;
                var p = new Vector3(x, f0 + 1.2f, z);
                if (s.kind == 0) { SegAt(s, p, out _, out _, out float f1, out _, out _); p.y = f1 + 1.2f; }
                float d = PrimD(s, p, out float f);
                if (d < best) { best = d; fy = f; }
            }
            return fy;
        }

        // ================================================================== route set-up
        static N[] _a, _b, _h;
        static List<Vector3> _stream = new List<Vector3>(), _runnel = new List<Vector3>();       // water surface points
        static List<float> _streamHalf = new List<float>();
        static int _streamInside;                                                                // points of _stream inside the cave

        static bool Setup()
        {
            _prims.Clear(); _stream.Clear(); _runnel.Clear(); _streamHalf.Clear();
            _t = Terrain.activeTerrain;
            if (!_t) { W("no active terrain"); return false; }
            _a = Resolve(PassA, true, out MouthE);
            _b = Resolve(PassB, false, out MouthW);
            _h = PassH.ToArray();
            AddPassage(_a, 1f); AddPassage(_b, 1f); AddPassage(_h, 0.3f);
            AddRoom(Chamber, 1.25f); AddRoom(Alcove, 0.8f); AddRoom(Hidden, 0.9f);
            AddEllipsoid(PoolC, PoolR, 0.25f, false);
            AddEllipsoid(AlcPoolC, AlcPoolR, 0.2f, false);
            AddEllipsoid(CleftC, CleftR, 0.5f, false);

            // ---- stream 1: pool outlet -> along the right (south) side of passage A -> mouth -> over the rock into the plunge pool
            var line = new List<Vector3> { new Vector3(44.7f, 0f, -175.3f), new Vector3(47.2f, 0f, -174.5f) };
            for (int i = _a.Length - 1; i >= 1; i--)
            {
                // flow runs from node i to i-1 (east); offset 0.9 m to the flow's right, shrinking to 0.5 m at the mouth
                var p = new Vector3(_a[i].x, 0f, _a[i].z);
                var q = new Vector3(_a[Mathf.Max(0, i - 1)].x, 0f, _a[Mathf.Max(0, i - 1)].z);
                var r0 = new Vector3(_a[Mathf.Min(_a.Length - 1, i + 1)].x, 0f, _a[Mathf.Min(_a.Length - 1, i + 1)].z);
                var dir = (q - r0).normalized; var right = new Vector3(dir.z, 0f, -dir.x);
                if (i == _a.Length - 1) continue;                                    // the chamber edge node: the two outlet points cover it
                line.Add(p + right * (i == 1 ? 0.5f : 0.9f));
            }
            // resample to 1 m, floor from the cave, groove depth tapering to the mouth
            var pts = Resample(line, 1.0f);
            var gp = new List<Vector3>(); var gd = new List<float>();
            float total = 0f; for (int i = 1; i < pts.Count; i++) total += Vector3.Distance(pts[i], pts[i - 1]);
            float run = 0f, lastY = float.MaxValue;
            for (int i = 0; i < pts.Count; i++)
            {
                if (i > 0) run += Vector3.Distance(pts[i], pts[i - 1]);
                float fy = FloorAt(pts[i].x, pts[i].z);
                if (float.IsNaN(fy)) fy = lastY;
                float left = total - run;
                float depth = Mathf.Lerp(0.07f, 0.32f, Mathf.InverseLerp(0.5f, 7f, left));
                gp.Add(new Vector3(pts[i].x, fy, pts[i].z)); gd.Add(depth);
                float sy = fy + 0.02f - depth * 0.5f;
                if (i == 0) sy = Mathf.Min(sy, PoolC.y - 0.01f);
                sy = Mathf.Min(sy, lastY - 0.004f); lastY = sy;                       // strictly downhill
                _stream.Add(new Vector3(pts[i].x, sy, pts[i].z)); _streamHalf.Add(Mathf.Lerp(0.3f, 0.52f, Mathf.InverseLerp(0.5f, 5f, left)));
            }
            _streamInside = _stream.Count;
            AddGroove(gp, gd, 0.62f);
            // outside: a thin sheet over the rock into the plunge pool
            var outPts = new List<Vector3> { new Vector3(_stream[_stream.Count - 1].x, 0f, _stream[_stream.Count - 1].z) };
            foreach (var o in StreamOut) outPts.Add(new Vector3(o.x, 0f, o.y));
            var outR = Resample(outPts, 0.7f);
            for (int i = 1; i < outR.Count; i++)
            {
                float y = Mathf.Max(TY(outR[i].x, outR[i].z) + 0.045f, PlungePoolLevel + 0.02f);
                y = Mathf.Min(y, lastY - 0.01f); lastY = y;
                _stream.Add(new Vector3(outR[i].x, y, outR[i].z)); _streamHalf.Add(0.3f);
            }
            // ---- runnel: curtain foot -> pool
            var rl = Resample(Runnel.Select(v => new Vector3(v.x, 0f, v.y)).ToList(), 0.6f);
            var rp = new List<Vector3>(); var rd = new List<float>(); lastY = float.MaxValue;
            for (int i = 0; i < rl.Count; i++)
            {
                float fy = FloorAt(rl[i].x, rl[i].z); if (float.IsNaN(fy)) fy = Chamber.c.y;
                float depth = i == rl.Count - 1 ? 0.3f : 0.14f;
                rp.Add(new Vector3(rl[i].x, fy, rl[i].z)); rd.Add(depth);
                float sy = Mathf.Min(fy + 0.02f - 0.07f, lastY - 0.004f); if (i == rl.Count - 1) sy = Mathf.Min(sy, PoolC.y + 0.01f); lastY = sy;
                _runnel.Add(new Vector3(rl[i].x, sy, rl[i].z));
            }
            AddGroove(rp, rd, 0.32f);
            L($"route: passage A {_a.Length} nodes (mouth {V(MouthE)}), passage B {_b.Length} nodes (mouth {V(MouthW)}), hidden branch {_h.Length} nodes, 3 rooms, 3 basins / cleft, {_prims.Count} primitives");
            L($"stream: {_stream.Count} points ({_streamInside} inside), surface {F(_stream[0].y)} -> {F(_stream[_stream.Count - 1].y)} m; runnel {_runnel.Count} points");
            return true;
        }

        static List<Vector3> Resample(List<Vector3> src, float step)
        {
            var o = new List<Vector3> { src[0] };
            for (int i = 1; i < src.Count; i++)
            {
                float d = Vector3.Distance(src[i - 1], src[i]); int n = Mathf.Max(1, Mathf.RoundToInt(d / step));
                for (int k = 1; k <= n; k++) o.Add(Vector3.Lerp(src[i - 1], src[i], k / (float)n));
            }
            return o;
        }

        static float PathLength(N[] n) { float s = 0f; for (int i = 1; i < n.Length; i++) s += Vector3.Distance(new Vector3(n[i].x, n[i].y, n[i].z), new Vector3(n[i - 1].x, n[i - 1].y, n[i - 1].z)); return s; }

        // ================================================================== surface nets
        class Quad { public Vector3 a, b, c, d; public int ia, ib, ic, id; }
        static float[] _f; static int _nx, _ny, _nz; static Vector3 _o;
        static int FI(int i, int j, int k) => (k * _ny + j) * _nx + i;

        static void SampleField()
        {
            var mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue); var mx = -mn;
            foreach (var s in _prims) { mn = Vector3.Min(mn, s.min); mx = Vector3.Max(mx, s.max); }
            _o = new Vector3(Mathf.Floor(mn.x), Mathf.Floor(mn.y), Mathf.Floor(mn.z));
            _nx = Mathf.CeilToInt((mx.x - _o.x) / Cell) + 2; _ny = Mathf.CeilToInt((mx.y - _o.y) / Cell) + 2; _nz = Mathf.CeilToInt((mx.z - _o.z) / Cell) + 2;
            _f = new float[_nx * _ny * _nz];
            int air = 0;
            for (int k = 0; k < _nz; k++)
                for (int j = 0; j < _ny; j++)
                    for (int i = 0; i < _nx; i++)
                    {
                        float v = (i == 0 || j == 0 || k == 0 || i == _nx - 1 || j == _ny - 1 || k == _nz - 1) ? 1f : FieldA(_o + new Vector3(i, j, k) * Cell);
                        _f[FI(i, j, k)] = v; if (v < 0f) air++;
                    }
            L($"field: {_nx} x {_ny} x {_nz} samples at {F(Cell)} m from {V(_o)}, air {F(air * Cell * Cell * Cell)} m3");
        }

        static readonly int[,] Edges = { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };

        /// <summary>naive surface nets: one vertex per sign-changing cell, one quad per sign-changing sample edge, facing the air</summary>
        static List<Quad> Polygonise(out List<Vector3> verts)
        {
            verts = new List<Vector3>();
            int cx = _nx - 1, cy = _ny - 1, cz = _nz - 1;
            var cellV = new int[cx * cy * cz];
            var corner = new float[8];
            for (int k = 0; k < cz; k++)
                for (int j = 0; j < cy; j++)
                    for (int i = 0; i < cx; i++)
                    {
                        int neg = 0;
                        for (int c = 0; c < 8; c++) { corner[c] = _f[FI(i + (c & 1), j + ((c >> 1) & 1), k + ((c >> 2) & 1))]; if (corner[c] < 0f) neg++; }
                        int ci = (k * cy + j) * cx + i;
                        if (neg == 0 || neg == 8) { cellV[ci] = -1; continue; }
                        Vector3 sum = Vector3.zero; int n = 0;
                        for (int e = 0; e < 12; e++)
                        {
                            int c0 = Edges[e, 0], c1 = Edges[e, 1]; float f0 = corner[c0], f1 = corner[c1];
                            if ((f0 < 0f) == (f1 < 0f)) continue;
                            float t = f0 / (f0 - f1);
                            var p0 = new Vector3(c0 & 1, (c0 >> 1) & 1, (c0 >> 2) & 1); var p1 = new Vector3(c1 & 1, (c1 >> 1) & 1, (c1 >> 2) & 1);
                            sum += Vector3.Lerp(p0, p1, t); n++;
                        }
                        cellV[ci] = verts.Count;
                        verts.Add(_o + (new Vector3(i, j, k) + sum / n) * Cell);
                    }
            int CV(int i, int j, int k) => (i < 0 || j < 0 || k < 0 || i >= cx || j >= cy || k >= cz) ? -1 : cellV[(k * cy + j) * cx + i];
            var quads = new List<Quad>();
            var vv = verts;
            void Emit(int a, int b, int c, int d, Vector3 want)
            {
                if (a < 0 || b < 0 || c < 0 || d < 0) return;
                var q = new Quad { ia = a, ib = b, ic = c, id = d, a = vv[a], b = vv[b], c = vv[c], d = vv[d] };
                var nrm = Vector3.Cross(q.b - q.a, q.c - q.a) + Vector3.Cross(q.c - q.a, q.d - q.a);
                if (Vector3.Dot(nrm, want) < 0f) { (q.ib, q.id) = (q.id, q.ib); (q.b, q.d) = (q.d, q.b); }
                quads.Add(q);
            }
            for (int k = 1; k < cz; k++)
                for (int j = 1; j < cy; j++)
                    for (int i = 0; i < cx; i++)
                    {
                        // x edge (i,j,k)-(i+1,j,k): cells around it differ in j, k
                        float f0 = _f[FI(i, j, k)], f1 = _f[FI(i + 1, j, k)];
                        if ((f0 < 0f) != (f1 < 0f)) Emit(CV(i, j - 1, k - 1), CV(i, j, k - 1), CV(i, j, k), CV(i, j - 1, k), f0 < 0f ? Vector3.left : Vector3.right);
                    }
            for (int k = 1; k < cz; k++)
                for (int j = 0; j < cy; j++)
                    for (int i = 1; i < cx; i++)
                    {
                        float f0 = _f[FI(i, j, k)], f1 = _f[FI(i, j + 1, k)];
                        if ((f0 < 0f) != (f1 < 0f)) Emit(CV(i - 1, j, k - 1), CV(i, j, k - 1), CV(i, j, k), CV(i - 1, j, k), f0 < 0f ? Vector3.down : Vector3.up);
                    }
            for (int k = 0; k < cz; k++)
                for (int j = 1; j < cy; j++)
                    for (int i = 1; i < cx; i++)
                    {
                        float f0 = _f[FI(i, j, k)], f1 = _f[FI(i, j, k + 1)];
                        if ((f0 < 0f) != (f1 < 0f)) Emit(CV(i - 1, j - 1, k), CV(i, j - 1, k), CV(i, j, k), CV(i - 1, j, k), f0 < 0f ? Vector3.back : Vector3.forward);
                    }
            return quads;
        }

        /// <summary>highest air sample y in the grid column under (x, z)</summary>
        static float AirTop(float x, float z)
        {
            int i = Mathf.Clamp(Mathf.RoundToInt((x - _o.x) / Cell), 0, _nx - 1), k = Mathf.Clamp(Mathf.RoundToInt((z - _o.z) / Cell), 0, _nz - 1);
            for (int j = _ny - 1; j >= 0; j--) if (_f[FI(i, j, k)] < 0f) return _o.y + j * Cell;
            return float.NegativeInfinity;
        }

        static float MouthDist(Vector3 p) => Mathf.Min(Vector3.Distance(p, MouthE), Vector3.Distance(p, MouthW));
        /// <summary>ambient bands by distance from the nearest mouth: occlusion of the sky ambient (1 = full daylight ambient)</summary>
        static readonly float[] BandDist = { 4f, 9f, 16f, 24f, float.MaxValue };
        static readonly float[] BandOcc = { 0.62f, 0.42f, 0.28f, 0.19f, 0.12f };
        static int Band(Vector3 p) { float d = MouthDist(p); for (int i = 0; i < BandDist.Length; i++) if (d < BandDist[i]) return i; return BandDist.Length - 1; }

        // ================================================================== assets
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int s = path.LastIndexOf('/'); EnsureFolder(path.Substring(0, s));
            AssetDatabase.CreateFolder(path.Substring(0, s), path.Substring(s + 1));
        }
        static T SaveAsset<T>(T obj, string path) where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing && obj is Mesh src && existing is Mesh dst)
            {
                // meshes are rewritten through the Mesh API: CopySerialized updated the asset (and the collider) but the
                // renderer kept drawing the old GPU buffers in this editor session
                dst.Clear(); dst.indexFormat = src.indexFormat;
                dst.SetVertices(src.vertices); if (src.normals.Length > 0) dst.SetNormals(src.normals);
                if (src.tangents.Length > 0) dst.SetTangents(src.tangents); if (src.uv.Length > 0) dst.SetUVs(0, src.uv);
                if (src.colors.Length > 0) dst.SetColors(src.colors);
                dst.subMeshCount = src.subMeshCount;
                for (int i = 0; i < src.subMeshCount; i++) dst.SetTriangles(src.GetTriangles(i), i);
                dst.RecalculateBounds(); dst.UploadMeshData(false);
                Object.DestroyImmediate(src); EditorUtility.SetDirty(dst); return existing;
            }
            if (existing) { EditorUtility.CopySerialized(obj, existing); Object.DestroyImmediate(obj); EditorUtility.SetDirty(existing); return existing; }
            AssetDatabase.CreateAsset(obj, path); return obj;
        }
        static Texture2D _ao;
        static Texture2D AoTexture()
        {
            // constant low value in G: with _OcclusionStrength it scales the sky ambient + reflections only (not the torch)
            var px = new Color32[64]; for (int i = 0; i < 64; i++) px[i] = new Color32(20, 20, 20, 255);
            return DataTex(GenDir + "/T_CV_CaveAmbient.asset", 8, 8, px, TextureWrapMode.Repeat);
        }
        /// <summary>linear RGBA32 data texture asset, updated in place on a re-run (same pattern as PrimalWaterBuilder)</summary>
        static Texture2D DataTex(string path, int w, int h, Color32[] px, TextureWrapMode wrap)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            bool created = false;
            if (!tex) { tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true); created = true; }
            else if (tex.width != w || tex.height != h || tex.format != TextureFormat.RGBA32 || tex.mipmapCount != 1) tex.Reinitialize(w, h, TextureFormat.RGBA32, false);
            tex.name = Path.GetFileNameWithoutExtension(path);
            tex.wrapMode = wrap; tex.filterMode = FilterMode.Bilinear; tex.anisoLevel = 0;
            tex.SetPixels32(px); tex.Apply(false, false);
            if (created) AssetDatabase.CreateAsset(tex, path); else EditorUtility.SetDirty(tex);
            return tex;
        }
        static float OccStrength(float occ) => Mathf.Clamp01((1f - occ) / (1f - 20f / 255f));

        static Material RockMat(string name, int band, bool floor)
        {
            string path = $"{MatDir}/{name}.mat";
            var sh = Shader.Find("PF/Wet Surface");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(sh) { name = name }; AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != sh) m.shader = sh;
            var d = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/Textures/T_Rock_D.png");
            var nm = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/Textures/T_Rock_N.png");
            var mk = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/Textures/T_Rock_M.png");
            if (d) m.SetTexture("_BaseMap", d); if (nm) m.SetTexture("_BumpMap", nm); if (mk) m.SetTexture("_MetallicGlossMap", mk);
            m.SetTexture("_OcclusionMap", _ao); m.SetFloat("_OcclusionStrength", OccStrength(BandOcc[band]));
            m.SetColor("_BaseColor", floor ? new Color(0.5f, 0.48f, 0.45f) : new Color(0.6f, 0.58f, 0.55f));
            m.SetFloat("_Smoothness", band <= 1 ? 0.1f : floor ? 0.35f : 0.25f); m.SetFloat("_BumpScale", floor ? 0.8f : 1.1f);
            m.SetFloat("_WetResponse", 0f);                                   // rain never reaches it
            float wet = band <= 1 ? (floor ? 0.08f : 0.05f) : (floor ? 0.78f : 0.38f);
            m.SetFloat("_BaseWetness", wet); m.SetFloat("_Porosity", 0.55f); m.SetFloat("_WetSmoothness", band <= 1 ? 0.5f : 0.8f);
            m.SetFloat("_MossAmount", 0f);                                    // no _MossMap on these: moss would render as flat white m.SetFloat("_ColorVariation", 0f);
            m.SetFloat("_Cull", 2f); m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }
        static readonly Dictionary<(Material, int), Material> _variants = new Dictionary<(Material, int), Material>();
        /// <summary>a cave copy of a prop material: its sky ambient scaled down like the cave rock at that band</summary>
        static Material CaveVariant(Material src, int band)
        {
            if (!src || !src.HasProperty("_OcclusionMap")) return src;
            if (_variants.TryGetValue((src, band), out var v)) return v;
            string path = $"{VarDir}/{src.name}_CV{band}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(src) { name = src.name + "_CV" + band }; AssetDatabase.CreateAsset(m, path); }
            else { m.shader = src.shader; m.CopyPropertiesFromMaterial(src); }
            m.SetTexture("_OcclusionMap", _ao); m.SetFloat("_OcclusionStrength", OccStrength(BandOcc[band]));
            if (m.HasProperty("_WetResponse")) m.SetFloat("_WetResponse", 0f);
            if (m.HasProperty("_BaseWetness")) m.SetFloat("_BaseWetness", Mathf.Max(m.GetFloat("_BaseWetness"), 0.3f));
            EditorUtility.SetDirty(m);
            _variants[(src, band)] = m;
            return m;
        }
        static void Cavify(GameObject go, int band)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = CaveVariant(mats[i], band);
                r.sharedMaterials = mats;
            }
        }

        // ================================================================== shell meshes
        static int _shellTris, _shellChunks, _shellVerts, _flipped, _strayLog;
        static void BuildShell(Transform root, List<Quad> quads, List<Vector3> verts)
        {
            var wall = new Material[BandOcc.Length]; var floor = new Material[BandOcc.Length];
            for (int b = 0; b < BandOcc.Length; b++) { wall[b] = RockMat($"M_CV_Rock_B{b}", b, false); floor[b] = RockMat($"M_CV_RockFloor_B{b}", b, true); }
            // vertex normals from the field (smooth, identical across chunk seams)
            var vn = new Vector3[verts.Count]; var needN = new bool[verts.Count];
            var keep = new List<Quad>(); int dropped = 0;
            foreach (var q in quads)
            {
                var cen = (q.a + q.b + q.c + q.d) * 0.25f;
                if (InPortal(cen))
                {
                    // mouth zone: the rock band (inner face, outer skin, cap); nothing above the cap or deep in the rock
                    // (where the band field meets the plain field at the zone edge)
                    if (cen.y > CapY(cen.x, cen.z) + 0.12f || Field(cen) > ShellT + 0.3f) { dropped++; continue; }
                    if (_strayLog < 12)
                    {
                        float best = float.MaxValue;
                        foreach (var nn in _a.Concat(_b)) best = Mathf.Min(best, new Vector2(nn.x - cen.x, nn.z - cen.z).magnitude);
                        if (best > 4.5f) { _strayLog++; L($"  stray portal quad at {V(cen)}: node dist {F(best)}, field {F(Field(cen))}, fieldA {F(FieldA(cen))}, terrain {F(TY(cen.x, cen.z))}, cap {F(CapY(cen.x, cen.z))}, floor {F(FloorAt(cen.x, cen.z))}"); }
                    }
                    keep.Add(q); needN[q.ia] = needN[q.ib] = needN[q.ic] = needN[q.id] = true; continue;
                }
                // outside the mouth zones only the cave wall itself (field ~0); anything else is the seam with the band field at the zone edge
                if (Field(cen) > 0.6f) { dropped++; continue; }
                float ty = TY(cen.x, cen.z);
                bool outside = ty < AirTop(cen.x, cen.z) - 0.3f;                 // open ground in front of a mouth: only the floor + walls below the ground
                bool isFloor = (Vector3.Cross(q.b - q.a, q.c - q.a) + Vector3.Cross(q.c - q.a, q.d - q.a)).normalized.y > 0.55f;
                if (cen.y > ty + (outside ? (isFloor ? 0.3f : -0.05f) : Lip)) { dropped++; continue; }
                keep.Add(q); needN[q.ia] = needN[q.ib] = needN[q.ic] = needN[q.id] = true;
            }
            for (int i = 0; i < verts.Count; i++) if (needN[i]) vn[i] = FieldNormal(verts[i]);
            // chunks
            var groups = new Dictionary<Vector2Int, List<Quad>>();
            foreach (var q in keep)
            {
                var cen = (q.a + q.b + q.c + q.d) * 0.25f;
                var key = new Vector2Int(Mathf.FloorToInt(cen.x / ChunkSize), Mathf.FloorToInt(cen.z / ChunkSize));
                if (!groups.TryGetValue(key, out var l)) groups[key] = l = new List<Quad>();
                l.Add(q);
            }
            var shellRoot = Child(root, "Shell");
            _shellTris = 0; _shellChunks = 0; _shellVerts = 0; _flipped = 0; _strayLog = 0;
            foreach (var kv in groups.OrderBy(k => k.Key.x).ThenBy(k => k.Key.y))
            {
                var map = new Dictionary<int, int>();
                var pv = new List<Vector3>(); var pn = new List<Vector3>(); var puv = new List<Vector2>();
                var subs = new Dictionary<int, List<int>>();                       // material slot (band * 2 + floor) -> indices
                var origin = new Vector3((kv.Key.x + 0.5f) * ChunkSize, 0f, (kv.Key.y + 0.5f) * ChunkSize);
                int Vi(int idx)
                {
                    if (map.TryGetValue(idx, out int o)) return o;
                    o = pv.Count; map[idx] = o;
                    var p = verts[idx]; var n = vn[idx];
                    pv.Add(p - origin); pn.Add(n);
                    // box-projected UV (2.5 m per tile): floor / ceiling from above, walls from the side they face
                    float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
                    puv.Add(ay > 0.62f ? new Vector2(p.x, p.z) / 2.5f : ax > az ? new Vector2(p.z, p.y) / 2.5f : new Vector2(p.x, p.y) / 2.5f);
                    return o;
                }
                foreach (var q in kv.Value)
                {
                    var cen = (q.a + q.b + q.c + q.d) * 0.25f;
                    var fn = Vector3.Cross(q.b - q.a, q.c - q.a) + Vector3.Cross(q.c - q.a, q.d - q.a);
                    bool isFloor = fn.normalized.y > 0.55f;
                    int slot = Band(cen) * 2 + (isFloor ? 1 : 0);
                    if (!subs.TryGetValue(slot, out var li)) subs[slot] = li = new List<int>();
                    int a = Vi(q.ia), b = Vi(q.ib), c = Vi(q.ic), d = Vi(q.id);
                    // a surface-nets quad is often not planar (floor / wall junctions, the band's cap edge): pick the diagonal whose
                    // two triangles both face the air, and flip any triangle that still faces the rock (else it is culled: a see-through tooth)
                    var want = vn[q.ia] + vn[q.ib] + vn[q.ic] + vn[q.id];
                    float Tri(Vector3 p0, Vector3 p1, Vector3 p2) => Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), want);
                    float s1 = Mathf.Min(Tri(q.a, q.b, q.c), Tri(q.a, q.c, q.d)), s2 = Mathf.Min(Tri(q.a, q.b, q.d), Tri(q.b, q.c, q.d));
                    var tri = s1 >= s2 ? new[] { (a, b, c, q.a, q.b, q.c), (a, c, d, q.a, q.c, q.d) } : new[] { (a, b, d, q.a, q.b, q.d), (b, c, d, q.b, q.c, q.d) };
                    foreach (var (i0, i1, i2, p0, p1, p2) in tri)
                    {
                        if (Tri(p0, p1, p2) < 0f) { li.AddRange(new[] { i0, i2, i1 }); _flipped++; }
                        else li.AddRange(new[] { i0, i1, i2 });
                    }
                }
                var mesh = new Mesh { name = $"ME_CV_Shell_{kv.Key.x}_{kv.Key.y}", indexFormat = pv.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(pv); mesh.SetNormals(pn); mesh.SetUVs(0, puv);
                var slots = subs.Keys.OrderBy(k => k).ToList();
                mesh.subMeshCount = slots.Count;
                for (int s = 0; s < slots.Count; s++) { mesh.SetTriangles(subs[slots[s]], s); _shellTris += subs[slots[s]].Count / 3; }
                mesh.RecalculateBounds(); mesh.RecalculateTangents();
                mesh = SaveAsset(mesh, $"{GenDir}/{mesh.name}.asset");
                var go = new GameObject($"Shell_{kv.Key.x}_{kv.Key.y}");
                go.transform.SetParent(shellRoot, false); go.transform.position = origin;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = slots.Select(k => (k & 1) == 1 ? floor[k >> 1] : wall[k >> 1]).ToArray();
                mr.shadowCastingMode = ShadowCastingMode.TwoSided; mr.receiveShadows = true;
                mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic);
                _shellChunks++; _shellVerts += pv.Count;
            }
            L($"shell: {keep.Count} quads kept ({dropped} above the terrain dropped), {_shellChunks} chunks, {_shellVerts} verts, {_shellTris} tris ({_flipped} twisted-quad triangles re-wound), {BandOcc.Length * 2} rock materials (ambient bands {string.Join("/", BandOcc.Select(F))})");
        }

        static Transform Child(Transform parent, string name)
        {
            var t = parent.Find(name);
            if (!t) { t = new GameObject(name).transform; t.SetParent(parent, false); }
            return t;
        }
        static GameObject Empty(Transform parent, string name, Vector3 pos, Vector3 scale = default)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.position = pos;
            if (scale != default) go.transform.localScale = scale;
            return go;
        }

        // ================================================================== terrain holes (mouths only)
        static int _holeCells;
        static bool ApplyHoles()
        {
            var td = _t.terrainData; string tdPath = AssetDatabase.GetAssetPath(td);
            if (!AssetDatabase.LoadAssetAtPath<TerrainData>(BackupPath))
            {
                EnsureFolder(BackupPath.Substring(0, BackupPath.LastIndexOf('/')));
                if (!AssetDatabase.CopyAsset(tdPath, BackupPath)) { W("terrain backup failed: no holes cut"); return false; }
                L($"terrain backup: {tdPath} -> {BackupPath}");
            }
            var bak = AssetDatabase.LoadAssetAtPath<TerrainData>(BackupPath);
            // compressed hole textures are drawn in 4 x 4 cell blocks (2.5 m): the rendered holes grow past the cut cells and the
            // void shows through. Uncompressed: the drawn holes match the cut cells exactly (1024 x 1024 R8, 1 MB)
            if (td.enableHolesTextureCompression) { td.enableHolesTextureCompression = false; L("terrain: holes texture compression off (exact hole edges)"); }
            int res = td.holesResolution; var size = td.size; var tp = _t.transform.position;
            float cx = size.x / res, cz = size.z / res;
            _holeCells = 0;
            foreach (var m in new[] { MouthE, MouthW })
            {
                int x0 = Mathf.Clamp(Mathf.FloorToInt((m.x - 10f - tp.x) / cx), 0, res - 1), x1 = Mathf.Clamp(Mathf.CeilToInt((m.x + 10f - tp.x) / cx), 0, res - 1);
                int z0 = Mathf.Clamp(Mathf.FloorToInt((m.z - 10f - tp.z) / cz), 0, res - 1), z1 = Mathf.Clamp(Mathf.CeilToInt((m.z + 10f - tp.z) / cz), 0, res - 1);
                int w = x1 - x0 + 1, h = z1 - z0 + 1;
                var holes = (bak && bak.holesResolution == res ? bak : td).GetHoles(x0, z0, w, h);       // [z, x], true = ground
                int cut = 0;
                for (int j = 0; j < h; j++)
                    for (int i = 0; i < w; i++)
                    {
                        bool inside = true, anyAir = false, anyHigh = false;
                        for (int c = 0; c < 4 && inside; c++)
                        {
                            int hx = x0 + i + (c & 1), hz = z0 + j + (c >> 1);
                            var p = new Vector3(tp.x + hx * cx, tp.y + td.GetHeight(hx, hz), tp.z + hz * cz);
                            // inside the rock band's outer skin (the band covers what is under the hole)
                            float fc = Field(p, true), fl = FloorAt(p.x, p.z);
                            bool ground = !float.IsNaN(fl) && p.y < fl + 0.12f;            // the ground in front of a mouth stays (it covers the floor)
                            // a corner beyond the band is fine on the cliff (the band's outer skin is behind it), not on the ground
                            if (!InPortal(p) || (fc > ShellT - 0.15f && (ground || fc > ShellT + 1.5f))) inside = false;
                            else { if (!ground) anyHigh = true; if (fc < -0.02f) anyAir = true; }
                        }
                        if (inside && anyAir && anyHigh) { holes[j, i] = false; cut++; }
                    }
                td.SetHoles(x0, z0, holes);
                _holeCells += cut;
                L($"terrain holes at the mouth {V(m)}: region {w} x {h} cells restored from the backup, {cut} cells cut ({F(cut * cx * cz)} m2)");
            }
            // push the hole mask to the GPU texture now (the drawn holes otherwise kept an older mask in edit mode)
            td.SyncTexture(TerrainData.HolesTextureName);
            _t.Flush();
            EditorUtility.SetDirty(td);
            return true;
        }

        // ================================================================== water
        static Texture2D SaveFlow(string name, int w, int h, Color32[] px) => DataTex($"{GenDir}/{name}.asset", w, h, px, TextureWrapMode.Clamp);
        static byte B01(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
        const float BankRange = 3f, DepthRange = 2f;

        static Material WaterMat(string name, bool calm, Texture2D flow, Vector4 rect)
        {
            string path = $"{MatDir}/{name}.mat";
            var src = AssetDatabase.LoadAssetAtPath<Material>(calm ? "Assets/_Project/Art/Water/Materials/M_Water_Pond.mat" : "Assets/_Project/Art/Water/Materials/M_Water_Stream.mat")
                   ?? AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Water/Materials/M_Water_River.mat");
            var sh = Shader.Find("PF/Water Flow");
            if (!sh) { W("PF/Water Flow shader missing"); return src; }
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = src ? new Material(src) : new Material(sh); m.name = name; AssetDatabase.CreateAsset(m, path); }
            else if (src) { m.shader = src.shader; m.CopyPropertiesFromMaterial(src); }
            m.shader = sh;
            m.SetTexture("_FlowMap", flow); m.SetVector("_FlowMapRect", rect);
            m.SetFloat("_BankRange", BankRange); m.SetFloat("_DepthRange", DepthRange);
            m.SetFloat("_RainStrength", 0f); m.SetFloat("_WindDrift", calm ? 0.01f : 0.02f);
            m.SetFloat("_SpecStrength", 0.35f); m.SetFloat("_ReflectionStrength", 0.85f);
            if (calm) { m.SetFloat("_FlowSpeed", 0.25f); m.SetFloat("_WaveStrength", 0.02f); m.SetFloat("_RippleStrength", 0.03f); m.SetFloat("_BankFoam", 0f); m.SetFloat("_RapidsFoam", 0f); m.SetFloat("_ShallowFoam", 0f); m.SetFloat("_Smoothness", 0.97f); }
            else { m.SetFloat("_FlowSpeed", 1.0f); m.SetFloat("_WaveStrength", 0.03f); m.SetFloat("_BankFoam", 0.3f); m.SetFloat("_ShallowFoam", 0.12f); m.SetFloat("_RapidsFoam", 0.25f); }
            m.SetFloat("_CausticsStrength", 0.15f);
            m.renderQueue = -1; m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static void WaterRenderer(GameObject go, Mesh mesh, Material mat)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = true;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        /// <summary>flowing ribbon along centre points (surface y) with half widths; flow map baked from the centre line</summary>
        static GameObject Ribbon(Transform parent, string name, List<Vector3> c, List<float> hw, float waterDepthInside, int insideCount)
        {
            int across = 5; var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            for (int i = 0; i < c.Count; i++)
            {
                var tg = c[Mathf.Min(i + 1, c.Count - 1)] - c[Mathf.Max(i - 1, 0)]; tg.y = 0f; tg.Normalize();
                var side = new Vector3(-tg.z, 0f, tg.x);
                for (int k = 0; k < across; k++)
                {
                    float u = k / (float)(across - 1);
                    var p = c[i] + side * Mathf.Lerp(-hw[i] - 0.12f, hw[i] + 0.12f, u);
                    if (i >= insideCount)                                                            // outside: drape over the rock (or the cave floor under a hole)
                        p.y = Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var gh, 4f, ~0, QueryTriggerInteraction.Ignore) ? gh.point.y + 0.03f : TY(p.x, p.z) + 0.03f;
                    verts.Add(p); uvs.Add(new Vector2(u, i));
                }
                if (i > 0) { int b0 = (i - 1) * across, b1 = i * across; for (int k = 0; k < across - 1; k++) tris.AddRange(new[] { b0 + k, b1 + k, b0 + k + 1, b0 + k + 1, b1 + k, b1 + k + 1 }); }
            }
            float up = 0f; for (int t = 0; t + 2 < tris.Count; t += 3) up += Vector3.Cross(verts[tris[t + 1]] - verts[tris[t]], verts[tris[t + 2]] - verts[tris[t]]).y;
            if (up < 0f) for (int t = 0; t + 2 < tris.Count; t += 3) { int k = tris[t + 1]; tris[t + 1] = tris[t + 2]; tris[t + 2] = k; }
            var mesh = new Mesh { name = "ME_CV_" + name }; mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            mesh = SaveAsset(mesh, $"{GenDir}/ME_CV_{name}.asset");
            // flow map
            const float tx = 0.2f;
            float minX = verts.Min(v => v.x) - 0.5f, maxX = verts.Max(v => v.x) + 0.5f, minZ = verts.Min(v => v.z) - 0.5f, maxZ = verts.Max(v => v.z) + 0.5f;
            int w = Mathf.Clamp(Mathf.CeilToInt((maxX - minX) / tx), 4, 1024), h = Mathf.Clamp(Mathf.CeilToInt((maxZ - minZ) / tx), 4, 1024);
            float sx = (maxX - minX) / w, sz = (maxZ - minZ) / h;
            var px = new Color32[w * h]; int wet = 0;
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    float x = minX + (i + 0.5f) * sx, z = minZ + (j + 0.5f) * sz;
                    float best = float.MaxValue; int bs = 0; float bt = 0f;
                    for (int s = 0; s + 1 < c.Count; s++)
                    {
                        float ax = c[s].x, az = c[s].z, bx = c[s + 1].x - ax, bz = c[s + 1].z - az, l2 = bx * bx + bz * bz;
                        float t = l2 < 1e-6f ? 0f : Mathf.Clamp01(((x - ax) * bx + (z - az) * bz) / l2);
                        float dx = ax + bx * t - x, dz = az + bz * t - z, d = dx * dx + dz * dz;
                        if (d < best) { best = d; bs = s; bt = t; }
                    }
                    float lat = Mathf.Sqrt(best), half = Mathf.Lerp(hw[bs], hw[bs + 1], bt);
                    if (lat > half + 0.15f) { px[j * w + i] = new Color32(128, 128, 0, 0); continue; }
                    var d3 = c[bs + 1] - c[bs]; float len = new Vector2(d3.x, d3.z).magnitude;
                    var dir = len > 1e-4f ? new Vector2(d3.x, d3.z) / len : Vector2.zero;
                    float slope = len > 1e-4f ? Mathf.Max(0f, -d3.y / len) : 0f;
                    float speed = Mathf.Clamp01(0.42f + slope * 5f) * Mathf.Lerp(0.6f, 1f, Mathf.Clamp01((half - lat) / 0.3f));
                    bool inside = bs + 1 < insideCount;
                    float depth = inside ? waterDepthInside * Mathf.Clamp01((half - lat + 0.1f) / 0.35f) : 0.035f;
                    px[j * w + i] = new Color32(B01(dir.x * speed * 0.5f + 0.5f), B01(dir.y * speed * 0.5f + 0.5f), B01(Mathf.Max(0f, half - lat) / BankRange), B01(depth / DepthRange));
                    wet++;
                }
            var tex = SaveFlow("T_CV_Flow_" + name, w, h, px);
            var mat = WaterMat("M_CV_Water_" + name, false, tex, new Vector4(minX, minZ, 1f / (w * sx), 1f / (h * sz)));
            var go = new GameObject("Water_" + name); go.transform.SetParent(parent, false);
            WaterRenderer(go, mesh, mat);
            L($"water {name}: ribbon {c.Count} points, {verts.Count} verts, flow map {w} x {h} ({wet} wet texels)");
            return go;
        }

        /// <summary>still pool: an ellipse disc at the surface, tucked under the rim, calm flow map (drift towards the outlet)</summary>
        static GameObject Pool(Transform parent, string name, Vector3 c, Vector3 r, Vector3 outlet)
        {
            float ex = r.x * 0.96f, ez = r.z * 0.96f; int seg = 48, rings = 4;
            var verts = new List<Vector3> { c }; var tris = new List<int>();
            for (int ri = 1; ri <= rings; ri++) for (int s = 0; s < seg; s++) { float a = s / (float)seg * Mathf.PI * 2f, f = ri / (float)rings; verts.Add(c + new Vector3(Mathf.Cos(a) * ex * f, 0f, Mathf.Sin(a) * ez * f)); }
            for (int s = 0; s < seg; s++) tris.AddRange(new[] { 0, 1 + (s + 1) % seg, 1 + s });
            for (int ri = 1; ri < rings; ri++) { int a0 = 1 + (ri - 1) * seg, b0 = 1 + ri * seg; for (int s = 0; s < seg; s++) { int s1 = (s + 1) % seg; tris.AddRange(new[] { a0 + s, a0 + s1, b0 + s, a0 + s1, b0 + s1, b0 + s }); } }
            var mesh = new Mesh { name = "ME_CV_" + name }; mesh.SetVertices(verts); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            if (mesh.normals.Length > 0 && mesh.normals[0].y < 0f) { for (int t = 0; t + 2 < tris.Count; t += 3) { int k = tris[t + 1]; tris[t + 1] = tris[t + 2]; tris[t + 2] = k; } mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); }
            mesh = SaveAsset(mesh, $"{GenDir}/ME_CV_{name}.asset");
            const float tx = 0.2f;
            float minX = c.x - ex - 0.5f, minZ = c.z - ez - 0.5f; int w = Mathf.CeilToInt((ex * 2f + 1f) / tx), h = Mathf.CeilToInt((ez * 2f + 1f) / tx);
            var px = new Color32[w * h];
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    float x = minX + (i + 0.5f) * tx, z = minZ + (j + 0.5f) * tx;
                    float k = Mathf.Sqrt(Mathf.Pow((x - c.x) / r.x, 2f) + Mathf.Pow((z - c.z) / r.z, 2f));
                    if (k > 1.06f) { px[j * w + i] = new Color32(128, 128, 0, 0); continue; }
                    var to = new Vector2(outlet.x - x, outlet.z - z); float dist = to.magnitude;
                    var fl = dist > 0.01f ? to / dist * (0.25f * Mathf.Exp(-dist / 3.5f)) : Vector2.zero;
                    float bank = Mathf.Max(0f, (1f - k) * Mathf.Min(r.x, r.z));
                    float depth = Mathf.Max(0f, r.y * (1f - k * k));
                    px[j * w + i] = new Color32(B01(fl.x * 0.5f + 0.5f), B01(fl.y * 0.5f + 0.5f), B01(bank / BankRange), B01(Mathf.Min(depth, DepthRange) / DepthRange));
                }
            var tex = SaveFlow("T_CV_Flow_" + name, w, h, px);
            var mat = WaterMat("M_CV_Water_" + name, true, tex, new Vector4(minX, minZ, 1f / (w * tx), 1f / (h * tx)));
            var go = new GameObject("Water_" + name); go.transform.SetParent(parent, false);
            WaterRenderer(go, mesh, mat);
            L($"water {name}: pool {F(r.x * 2f)} x {F(r.z * 2f)} m at {F(c.y)} m, depth {F(r.y)} m, flow map {w} x {h}");
            return go;
        }

        /// <summary>drinking spots: a WaterSource on a renderer-less MeshFilter (PrimalWaterBuilder only bakes WaterSources that have a renderer)</summary>
        static void Drink(Transform parent, string name, string display, List<Vector3> pts, string saveId)
        {
            if (pts.Count == 0) return;
            var cen = Vector3.zero; foreach (var p in pts) cen += p; cen /= pts.Count;
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.position = cen;
            var mesh = new Mesh { name = "ME_CV_" + name }; mesh.SetVertices(pts.Select(p => p - cen).ToList()); mesh.RecalculateBounds();
            mesh = SaveAsset(mesh, $"{GenDir}/ME_CV_{name}.asset");
            var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = mesh;
            var ws = go.AddComponent<WaterSource>(); ws.displayName = display; ws.fresh = true; ws.clean = false; ws.surface = mf; ws.SaveId = saveId;
        }

        // ================================================================== lights, probes
        static int _lights;
        static Light AddLight(Transform parent, string name, Vector3 pos, LightType type, Color col, float intensity, float range, bool daylight, Vector3 lookAt = default)
        {
            var go = Empty(parent, name, pos);
            var l = go.AddComponent<Light>(); l.type = type; l.color = col; l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.Auto; l.bounceIntensity = 0f;
            if (type == LightType.Spot) { l.spotAngle = 38f; l.innerSpotAngle = 12f; go.transform.rotation = Quaternion.LookRotation((lookAt - pos).normalized, Vector3.forward); }
            if (daylight) { var d = go.AddComponent<CaveDaylightLight>(); d.dayIntensity = intensity; d.nightIntensity = 0f; }
            _lights++;
            return l;
        }
        static Cubemap DarkCube()
        {
            string path = GenDir + "/CUBE_CV_CaveDark.cubemap";
            var c = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
            if (c) return c;
            c = new Cubemap(16, TextureFormat.RGBA32, false) { name = "CUBE_CV_CaveDark" };
            var px = Enumerable.Repeat(new Color(0.016f, 0.018f, 0.02f, 1f), 256).ToArray();
            foreach (CubemapFace f in new[] { CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY, CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ }) c.SetPixels(px, f);
            c.Apply(false);
            AssetDatabase.CreateAsset(c, path);
            return c;
        }
        static void Probe(Transform parent, string name, Vector3 min, Vector3 max, Cubemap cube)
        {
            var t = parent.Find(name); var go = t ? t.gameObject : Empty(parent, name, (min + max) * 0.5f);
            go.transform.position = (min + max) * 0.5f;
            var p = go.GetComponent<ReflectionProbe>(); if (!p) p = go.AddComponent<ReflectionProbe>();
            p.mode = ReflectionProbeMode.Custom; p.customBakedTexture = cube; p.size = max - min; p.center = Vector3.zero;
            p.importance = 10; p.blendDistance = 1.5f; p.boxProjection = false; p.intensity = 1f;
        }

        // ================================================================== props, kit, anchors
        static GameObject Prefab(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);
        static bool FloorHit(Vector3 p, out RaycastHit hit) => Physics.Raycast(p + Vector3.up * 1.4f, Vector3.down, out hit, 3.5f, ~0, QueryTriggerInteraction.Ignore);
        static int _props;
        static GameObject Put(Transform parent, GameObject pf, Vector3 pos, Quaternion rot, float scale, string name, bool cave = true)
        {
            if (!pf) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(pf, parent); go.name = name;
            go.transform.SetPositionAndRotation(pos, rot); go.transform.localScale = Vector3.one * scale;
            if (cave) Cavify(go, Band(pos));
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic);
            _props++;
            return go;
        }
        static float Rnd(int i, int k) => Hash(i * 7 + 3, k * 13 + 1, 91);

        /// <summary>points along a passage centre line every 'step' m (floor y)</summary>
        static List<(Vector3 p, Vector3 dir, float w)> Walk(N[] n, float step)
        {
            var o = new List<(Vector3, Vector3, float)>();
            for (int i = 0; i + 1 < n.Length; i++)
            {
                var a = new Vector3(n[i].x, n[i].y, n[i].z); var b = new Vector3(n[i + 1].x, n[i + 1].y, n[i + 1].z);
                var d = b - a; d.y = 0f; float len = d.magnitude; if (len < 1e-3f) continue; d /= len;
                int k0 = i == 0 ? 0 : 1; int cnt = Mathf.Max(1, Mathf.RoundToInt(len / step));
                for (int k = k0; k <= cnt; k++) { float t = k / (float)cnt; o.Add((Vector3.Lerp(a, b, t), d, Mathf.Lerp(n[i].w, n[i + 1].w, t))); }
            }
            return o;
        }

        static void Dress(Transform root)
        {
            var props = Child(root, "Props");
            var rocks = new[] { "Small_01", "Small_02", "Medium_01", "Medium_02", "Medium_03" }.Select(s => Prefab($"Assets/_Project/Prefabs/Environment/PFB_ENV_Rock_{s}.prefab")).Where(p => p).ToArray();
            var big = new[] { "Large_01", "Large_02" }.Select(s => Prefab($"Assets/_Project/Prefabs/Environment/PFB_ENV_Rock_{s}.prefab")).Where(p => p).ToArray();
            var bones = Prefab("Assets/_Project/Prefabs/Environment/PC/PROP_PC_Bones.prefab");
            Physics.SyncTransforms();
            // rubble at the wall bases (passage A: only on the walkway side, the stream runs on the right)
            int ri = 0;
            foreach (var (pts, name, sideMask) in new[] { (Walk(_a, 6.5f), "A", 1), (Walk(_b, 6f), "B", 3) })
                for (int i = 1; i < pts.Count - 1; i++)
                {
                    var (p, dir, w) = pts[i];
                    if (MouthDist(p) < 4f) continue;
                    int side = (sideMask == 3 ? (i % 2 == 0 ? 1 : -1) : 1);                 // +1 = left (flow of A runs east, left = north)
                    var left = new Vector3(-dir.z, 0f, dir.x) * side;
                    if (name == "A") left = -left;                                          // A is listed mouth -> chamber: its left is the flow's right; flip to the walkway side
                    if (!Physics.Raycast(p + Vector3.up * 0.5f, left, out var wh, w * 2.5f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    var at = wh.point - left * 0.25f; if (!FloorHit(at, out var fh)) continue;
                    if (fh.normal.y < 0.7f || Mathf.Abs(fh.point.y - p.y) > 0.6f) continue;      // only on the real floor (no ledge, no floating rock)
                    var pf = rocks[(int)(Rnd(ri, 1) * rocks.Length) % rocks.Length];
                    Put(props, pf, fh.point + Vector3.down * 0.12f, Quaternion.Euler(Rnd(ri, 2) * 20f, Rnd(ri, 3) * 360f, Rnd(ri, 4) * 20f), 0.55f + Rnd(ri, 5) * 0.4f, $"Rubble_{name}_{ri}");
                    ri++;
                }
            // framing boulders outside both mouths (daylight: no cave variant)
            int fi = 0;
            foreach (var (m, n) in new[] { (MouthE, _a[0]), (MouthW, _b[_b.Length - 1]) })
            {
                var outDir = new Vector3(n.x - m.x, 0f, n.z - m.z).normalized; var side = new Vector3(-outDir.z, 0f, outDir.x);
                foreach (float s in new[] { -1f, 1f })
                {
                    var p = m + side * s * 2.9f + outDir * 0.4f; p.y = TY(p.x, p.z) - 0.55f;
                    if (big.Length > 0) Put(props, big[fi % big.Length], p, Quaternion.Euler(0f, Rnd(fi, 7) * 360f, 0f), 0.5f + Rnd(fi, 8) * 0.2f, $"MouthRock_{fi}", false);
                    fi++;
                }
            }
            // bones: a few along the stream passage, on the chamber walkway, and the old nest in the hidden chamber
            int bi = 0;
            var bonePts = new List<Vector3> { new Vector3(53.2f, 0f, -170.6f), new Vector3(33.2f, 0f, -167.2f), new Vector3(46.8f, 0f, -179.8f), new Vector3(20.2f, 0f, -163.0f) };
            foreach (var bp in bonePts)
                if (FloorHit(new Vector3(bp.x, FloorAt(bp.x, bp.z), bp.z), out var h)) Put(props, bones, h.point, Quaternion.Euler(0f, Rnd(bi, 9) * 360f, 0f), 0.8f + Rnd(bi, 10) * 0.3f, $"Bones_{bi++}");
            var nest = Child(root, "HiddenNest");
            var nc = new Vector3(Hidden.c.x + 0.6f, Hidden.c.y, Hidden.c.z - 0.4f);
            nest.position = nc;                                                         // before the children (they are placed in world space)
            for (int i = 0; i < 9; i++)
            {
                float a = i / 9f * Mathf.PI * 2f; var p = nc + new Vector3(Mathf.Cos(a) * 1.25f, 0f, Mathf.Sin(a) * 1.05f);
                if (FloorHit(p, out var h) && rocks.Length > 0) Put(nest, rocks[i % 2], h.point + Vector3.down * 0.08f, Quaternion.Euler(Rnd(i, 11) * 30f, Rnd(i, 12) * 360f, 0f), 0.42f + Rnd(i, 13) * 0.2f, $"NestStone_{i}");
            }
            for (int i = 0; i < 3; i++)
            {
                var p = nc + new Vector3((Rnd(i, 14) - 0.5f) * 1.2f, 0f, (Rnd(i, 15) - 0.5f) * 1.0f);
                if (FloorHit(p, out var h)) Put(nest, bones, h.point, Quaternion.Euler(0f, Rnd(i, 16) * 360f, 0f), 0.75f + Rnd(i, 17) * 0.35f, $"NestBones_{i}");
            }
            var ex = nest.gameObject.AddComponent<Examinable>(); ex.discoveryId = "cave_hidden_nest"; ex.SaveId = "cave_hidden_nest"; ex.range = 2.6f;
            L($"props: {_props} (rubble {ri}, mouth rocks {fi}, bones {bi}, nest ring 9 + 3 bones), Examinable cave_hidden_nest at {V(nc)}");
        }

        static int _kit, _kitRejected;
        /// <summary>ART cave kit, only the pieces that fit the generated shell: stalactites hung by raycast on flat enough
        /// ceilings (attach point top centre, hang 3.4 m x scale) and rubble set on the floor at the wall bases (base centre),
        /// each checked flush (all probes on the surface) and clear of the walk lines, the pools and the stream. The modular
        /// walls, tunnels, the dome and the pool rim (circular, radius 8.4 m) do not match the generated rock and are skipped.</summary>
        static void PlaceKit(Transform root)
        {
            GameObject sta = null, rub = null; var skipped = new List<string>();
            if (AssetDatabase.IsValidFolder(KitDir))
                foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { KitDir }))
                {
                    var p = AssetDatabase.GUIDToAssetPath(g); var n = Path.GetFileNameWithoutExtension(p);
                    if (n.Contains("CAVE_Stalactites")) sta = Prefab(p);
                    else if (n.Contains("CAVE_Rubble")) rub = Prefab(p);
                    else if (n.Contains("CAVE_")) skipped.Add(n);
                }
            if (!sta && !rub) { L($"ART cave kit: not delivered yet ({KitDir}/*CAVE_*): shell only"); return; }
            var kit = Child(root, "Kit");
            Physics.SyncTransforms();
            var route = new List<Vector3>();
            foreach (var w in Walk(_a.Skip(1).ToArray(), 1f).Concat(Walk(_b.Take(_b.Length - 1).ToArray(), 1f)).Concat(Walk(_h, 1f))) route.Add(w.p);
            for (int k = 0; k < 48; k++) { float a = k / 48f * Mathf.PI * 2f; route.Add(PoolC + new Vector3(Mathf.Cos(a) * (PoolR.x + 1.8f), 0f, Mathf.Sin(a) * (PoolR.z + 1.8f))); }
            float RouteDist(Vector3 q) { float best = float.MaxValue; foreach (var r in route) best = Mathf.Min(best, new Vector2(r.x - q.x, r.z - q.z).magnitude); return best; }
            bool OverWater(Vector3 q, float m)
            {
                float kx = (q.x - PoolC.x) / (PoolR.x + m), kz = (q.z - PoolC.z) / (PoolR.z + m);
                float ax = (q.x - AlcPoolC.x) / (AlcPoolR.x + m), az = (q.z - AlcPoolC.z) / (AlcPoolR.z + m);
                return kx * kx + kz * kz < 1f || ax * ax + az * az < 1f || NearLine(q, _stream, 0.75f + m) || NearLine(q, _runnel, 0.5f + m);
            }
            int ns = 0, nr = 0, rjN = 0, rjF = 0, rjH = 0;
            // ---- stalactites: chamber ring + the taller passage spots
            if (sta)
            {
                var spots = new List<Vector3>();
                for (int k = 0; k < 12; k++) { float a = (k + 0.5f) / 12f * Mathf.PI * 2f; float f = k % 2 == 0 ? 0.45f : 0.7f; spots.Add(new Vector3(Chamber.c.x + Mathf.Cos(a) * Chamber.rx * f, Chamber.c.y, Chamber.c.z + Mathf.Sin(a) * Chamber.rz * f)); }
                foreach (var w in Walk(_a.Skip(2).ToArray(), 5f).Concat(Walk(_b.Take(_b.Length - 2).ToArray(), 5f))) spots.Add(w.p);
                int i = 0;
                foreach (var sp in spots)
                {
                    if (ns >= 10) break;
                    i++;
                    if (MouthDist(sp) < 8f) continue;
                    float s = 0.75f + Rnd(i, 31) * 0.25f, hang = 3.4f * s;
                    var top0 = new Vector3(sp.x, sp.y + 1.2f, sp.z);
                    if (!FloorHit(top0, out var fh)) { _kitRejected++; continue; }
                    if (!Physics.Raycast(fh.point + Vector3.up * 0.3f, Vector3.up, out var ch, 14f, ~0, QueryTriggerInteraction.Ignore) || ch.normal.y > -0.4f) { _kitRejected++; rjN++; continue; }
                    // flush: the ceiling under the cluster's footprint within 0.45 m of the attach point
                    bool flush = true;
                    foreach (var o in new[] { new Vector3(1.0f, 0f, 0f), new Vector3(-1.0f, 0f, 0f), new Vector3(0f, 0f, 0.9f), new Vector3(0f, 0f, -0.9f) })
                        if (!Physics.Raycast(fh.point + o * s + Vector3.up * 0.3f, Vector3.up, out var c2, 14f, ~0, QueryTriggerInteraction.Ignore) || c2.point.y < ch.point.y - 0.7f || c2.point.y > ch.point.y + 0.7f) { flush = false; break; }
                    float tip = ch.point.y + 0.3f - hang;
                    bool overPool = OverWater(fh.point, 0f) && !NearLine(fh.point, _stream, 1.2f);
                    if (!flush) { _kitRejected++; rjF++; continue; }
                    if (overPool ? tip < PoolC.y + 0.6f : tip < fh.point.y + 2.4f) { _kitRejected++; rjH++; continue; }
                    Put(kit, sta, ch.point + Vector3.up * 0.3f, Quaternion.Euler(0f, Rnd(i, 32) * 360f, 0f), s, $"Stalactites_{ns++}"); _kit++;
                }
            }
            // ---- rubble: at the chamber wall and the passage walls, on the floor, off the walk lines and the water
            if (rub)
            {
                var cand = new List<(Vector3 from, Vector3 dir)>();
                for (int k = 0; k < 14; k++) { float a = k / 14f * Mathf.PI * 2f; cand.Add((Chamber.c + Vector3.up * 0.6f, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)))); }
                int wi = 0;
                foreach (var w in Walk(_a.Skip(2).ToArray(), 7f).Concat(Walk(_b.Take(_b.Length - 2).ToArray(), 7f)))
                { var side = new Vector3(-w.dir.z, 0f, w.dir.x) * ((wi++ & 1) == 0 ? 1f : -1f); cand.Add((w.p + Vector3.up * 0.6f, side)); }
                int i = 0;
                foreach (var (src0, dir) in cand)
                {
                    if (nr >= 12) break;
                    i++;
                    if (MouthDist(src0) < 7f) continue;
                    if (!Physics.Raycast(src0, dir, out var wh, 16f, ~0, QueryTriggerInteraction.Ignore)) { _kitRejected++; continue; }
                    float s = 0.45f + Rnd(i, 33) * 0.15f, halfX = 2.15f * s, halfZ = 1.7f * s;
                    var at = wh.point - dir * (halfZ + 0.15f);
                    if (!FloorHit(at, out var fh) || Vector3.Angle(fh.normal, Vector3.up) > 22f) { _kitRejected++; continue; }
                    var rot = Quaternion.FromToRotation(Vector3.up, fh.normal) * Quaternion.LookRotation(-dir);
                    bool ok = !OverWater(fh.point, halfX) && RouteDist(fh.point) > halfX + 0.9f;
                    var right = rot * Vector3.right; var fwd = rot * Vector3.forward;
                    foreach (var o in new[] { right * halfX * 0.8f + fwd * halfZ * 0.8f, right * halfX * 0.8f - fwd * halfZ * 0.8f, -right * halfX * 0.8f + fwd * halfZ * 0.8f, -right * halfX * 0.8f - fwd * halfZ * 0.8f })
                    {
                        if (!ok) break;
                        // every footprint corner on the floor within -0.3 .. +0.35 m of the base (no overhang, not sunk into a slope)
                        if (!FloorHit(fh.point + o, out var c2) || c2.point.y < fh.point.y - 0.3f || c2.point.y > fh.point.y + 0.35f) ok = false;
                    }
                    if (!ok) { _kitRejected++; continue; }
                    Put(kit, rub, fh.point - fh.normal * 0.08f, rot * Quaternion.Euler(0f, (Rnd(i, 34) - 0.5f) * 40f, 0f), s, $"Rubble_{nr++}"); _kit++;
                }
            }
            L($"ART cave kit: {ns} stalactite clusters + {nr} rubble piles placed (flush + clear of the route), {_kitRejected} spots rejected (stalactites: ceiling too steep {rjN}, not flush {rjF}, too low {rjH}); not used (do not match the generated rock): {string.Join(", ", skipped)}");
        }

        static void Anchors(Transform root)
        {
            float FY(float x, float z) { float f = FloorAt(x, z); return float.IsNaN(f) ? Chamber.c.y : f; }
            Vector3 P(float x, float z, float up = 0f) => new Vector3(x, FY(x, z) + up, z);
            var ai = Child(root, "AI_Anchors");
            Empty(ai, "AI_SmallCreature_0_lizards", P(57.5f, -170.4f));
            Empty(ai, "AI_SmallCreature_1_crickets", P(34.6f, -167.0f));
            Empty(ai, "AI_SmallCreature_2_lizards", P(21.2f, -164.6f));
            Empty(ai, "AI_SmallCreature_3_scavenger", P(47.2f, -193.4f));
            Empty(ai, "AI_SmallCreature_4_crickets", P(76.0f, -160.9f));
            for (int i = 0; i < 3; i++) Empty(ai, $"AI_Fish_Pool_{i}", new Vector3(PoolC.x - 2.5f + i * 2.5f, PoolC.y - 0.6f, PoolC.z + (i - 1) * 1.2f));
            Empty(ai, "AI_Fish_AlcovePool_0", new Vector3(AlcPoolC.x, AlcPoolC.y - 0.35f, AlcPoolC.z));
            var res = Child(root, "RES_Anchors");
            Empty(res, "RES_Stone_0", P(81.6f, -159.4f)); Empty(res, "RES_Stone_1", P(25.6f, -171.8f)); Empty(res, "RES_Stone_2", P(15.6f, -156.2f));
            Empty(res, "RES_Flint_0", P(63.4f, -168.2f)); Empty(res, "RES_Flint_1", P(19.4f, -161.8f));
            Empty(res, "RES_Clay_0", P(69.8f, -162.2f)); Empty(res, "RES_Clay_1", P(46.2f, -176.4f));
            Empty(res, "RES_Bones_0", P(53.8f, -171.4f)); Empty(res, "RES_Bones_1", P(32.4f, -168.8f));
            Empty(res, "RES_RareCluster_Hidden_0_bones", P(Hidden.c.x - 2.3f, Hidden.c.z + 0.8f));
            Empty(res, "RES_RareCluster_Hidden_1_flint", P(Hidden.c.x + 2.4f, Hidden.c.z - 1.2f));
            Empty(res, "RES_RareCluster_Hidden_2_bones", P(Hidden.c.x - 0.8f, Hidden.c.z - 2.4f));
            var fx = Child(root, "FX_Anchors");
            Empty(fx, "FX_Drip_0", P(71.5f, -163.2f, 3.0f)); Empty(fx, "FX_Drip_1", P(62.0f, -169.4f, 3.0f)); Empty(fx, "FX_Drip_2", P(36.0f, -176.0f, 8.5f));
            Empty(fx, "FX_Drip_3", P(43.5f, -171.0f, 8.0f)); Empty(fx, "FX_Drip_4", P(23.6f, -166.8f, 2.8f)); Empty(fx, "FX_Drip_5", P(44.6f, -192.0f, 3.6f));
            for (int i = 0; i < 4; i++) { int k = Mathf.Clamp(i * (_streamInside - 1) / 3, 0, _stream.Count - 1); Empty(fx, $"FX_Stream_{i}", _stream[k]); }
            Empty(fx, "FX_Curtain", new Vector3(41.7f, Chamber.c.y + 1.2f, -182.2f));
            Empty(fx, "FX_WaterfallOutside", MouthE + Vector3.up * 1.5f);
            Empty(fx, "FX_LightShaftDust", new Vector3(CleftC.x, Chamber.c.y + 4.5f, CleftC.z));
            // echo zones: box bounds as the anchor's scale (centre = position)
            Empty(fx, "FX_EchoZone_Chamber", new Vector3(Chamber.c.x, Chamber.c.y + Chamber.h * 0.5f, Chamber.c.z), new Vector3(Chamber.rx * 2f, Chamber.h, Chamber.rz * 2f));
            Empty(fx, "FX_EchoZone_StreamPassage", new Vector3(69.5f, 21.8f, -165.5f), new Vector3(37f, 5f, 16f));
            Empty(fx, "FX_EchoZone_WestPassage", new Vector3(19.0f, 23.5f, -157.0f), new Vector3(22f, 6f, 36f));
            Empty(fx, "FX_EchoZone_Hidden", new Vector3(Hidden.c.x, Hidden.c.y + 2f, Hidden.c.z + 1f), new Vector3(10f, 4.5f, 13f));
            Empty(root, "LM_DeepWaterCave", new Vector3(PoolC.x, PoolC.y, PoolC.z));
            L($"anchors: AI {ai.childCount}, RES {res.childCount}, FX {fx.childCount}; LM_DeepWaterCave at {V(PoolC)}");
        }

        static void Curtain(Transform root)
        {
            Physics.SyncTransforms();
            var h0 = new Vector3(_h[0].x, _h[0].y, _h[0].z); var h1 = new Vector3(_h[1].x, _h[1].y, _h[1].z);
            var dir = h1 - h0; dir.y = 0f; dir.Normalize();
            // the wall above the crawl opening: first hit going in at 2.6 m above the floor
            var from = h0 + Vector3.up * 2.6f - dir * 0.5f;
            if (!Physics.Raycast(from, dir, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore)) { W("curtain: no wall found above the crawl"); return; }
            var top = hit.point - dir * 0.18f; top.y = h0.y + 2.7f;
            var side = new Vector3(-dir.z, 0f, dir.x);
            float drop = top.y - h0.y + 0.05f; int rows = 8, cols = 4;
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var cols4 = new List<Color>(); var tris = new List<int>();
            float len = 0f; Vector3 prev = top;
            for (int i = 0; i <= rows; i++)
            {
                float v = i / (float)rows; var c = top - dir * (0.25f * Mathf.Sin(v * Mathf.PI * 0.5f)) + Vector3.down * (drop * v);
                if (i > 0) len += Vector3.Distance(c, prev); prev = c;
                float hw = Mathf.Lerp(0.75f, 0.95f, v);
                for (int k = 0; k <= cols; k++) { float u = k / (float)cols; verts.Add(c + side * Mathf.Lerp(-hw, hw, u)); uvs.Add(new Vector2(u, len)); cols4.Add(new Color(1f, 1f, 1f, v)); }
                if (i > 0) { int b0 = (i - 1) * (cols + 1), b1 = i * (cols + 1); for (int k = 0; k < cols; k++) tris.AddRange(new[] { b0 + k, b0 + k + 1, b1 + k, b0 + k + 1, b1 + k + 1, b1 + k }); }
            }
            var mesh = new Mesh { name = "ME_CV_Curtain" }; mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetColors(cols4); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            mesh = SaveAsset(mesh, GenDir + "/ME_CV_Curtain.asset");
            var src = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Environment/Materials/M_Env_Rivulet.mat");
            string mp = MatDir + "/M_CV_Curtain.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (!m && src) { m = new Material(src) { name = "M_CV_Curtain" }; AssetDatabase.CreateAsset(m, mp); }
            if (m)
            {
                if (src) { m.shader = src.shader; m.CopyPropertiesFromMaterial(src); }
                if (m.HasProperty("_WaterColor")) m.SetColor("_WaterColor", new Color(0.3f, 0.36f, 0.37f, 1f));
                if (m.HasProperty("_FoamColor")) m.SetColor("_FoamColor", new Color(0.5f, 0.54f, 0.55f, 1f));
                if (m.HasProperty("_Opacity")) m.SetFloat("_Opacity", 0.55f);
                if (m.HasProperty("_Speed")) m.SetFloat("_Speed", 0.7f);
                EditorUtility.SetDirty(m);
            }
            var go = new GameObject("WaterCurtain"); go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off;
            L($"water curtain: {F(drop)} m x {F(1.9f)} m over the crawl opening at {V(top)} (wall hit {F(hit.distance)} m in)");
        }

        // ================================================================== commands
        static bool SceneOk(out UnityEngine.SceneManagement.Scene scene)
        {
            scene = EditorSceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode) { W("Play mode: nothing done"); return false; }
            if (!scene.path.EndsWith("Island_VerticalSlice.unity")) { W("the island scene is not open: " + scene.path); return false; }
            return true;
        }

        /// <summary>builds the Deep Water Cave (idempotent). args: "noholes" (keep the terrain as it is)</summary>
        [PrimalBridgeCommand]
        public static string Build(string arg)
        {
            Log.Clear(); _warn = 0; _lights = 0; _props = 0; _kit = 0; _kitRejected = 0; _strayLog = 0; _variants.Clear();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            if (!SceneOk(out var scene) || !Setup()) return Log.ToString();
            EnsureFolder(GenDir); EnsureFolder(MatDir); EnsureFolder(VarDir);
            _ao = AoTexture();
            var root = SceneRoots.Find(RootPath, true);
            // reflection probes are kept and updated in place: destroying them in edit mode leaves URP's probe atlas with a dead
            // probe (NullReference in ReflectionProbeManager on every render until the scene is reloaded)
            for (int i = root.childCount - 1; i >= 0; i--) if (root.GetChild(i).name != "ReflectionProbes") Object.DestroyImmediate(root.GetChild(i).gameObject);
            root.position = Vector3.zero; root.rotation = Quaternion.identity; root.localScale = Vector3.one;

            SampleField();
            var quads = Polygonise(out var verts);
            L($"surface nets: {verts.Count} cell vertices, {quads.Count} quads ({F((float)clock.Elapsed.TotalSeconds)} s)");
            BuildShell(root, quads, verts);
            _f = null;
            Physics.SyncTransforms();

            if (arg != null && arg.Contains("noholes")) L("terrain holes: skipped (noholes)");
            else ApplyHoles();

            // ---- water
            var water = Child(root, "Water");
            Ribbon(water, "Stream", _stream, _streamHalf, 0.17f, _streamInside);
            Ribbon(water, "Runnel", _runnel, _runnel.Select(_ => 0.2f).ToList(), 0.08f, _runnel.Count);
            Pool(water, "UndergroundPool", PoolC, PoolR, _stream[0]);
            Pool(water, "AlcovePool", AlcPoolC, AlcPoolR, AlcPoolC + Vector3.forward * 5f);
            var drink = Child(water, "Drink");
            int seg = 0; var cur = new List<Vector3>(); float run = 0f;
            for (int i = 0; i < _stream.Count; i++)
            {
                if (i > 0) run += Vector3.Distance(_stream[i], _stream[i - 1]);
                cur.Add(_stream[i]);
                if (run >= 11f || i == _stream.Count - 1) { Drink(drink, $"Stream_{seg}", "Cave stream", cur, $"water_cave_stream_{seg}"); seg++; cur = new List<Vector3> { _stream[i] }; run = 0f; }
            }
            var ring = new List<Vector3>(); for (int i = 0; i < 24; i++) { float a = i / 24f * Mathf.PI * 2f; ring.Add(PoolC + new Vector3(Mathf.Cos(a) * PoolR.x * 0.92f, 0f, Mathf.Sin(a) * PoolR.z * 0.92f)); }
            Drink(drink, "UndergroundPool", "Underground pool", ring, "water_cave_pool");
            var ring2 = new List<Vector3>(); for (int i = 0; i < 10; i++) { float a = i / 10f * Mathf.PI * 2f; ring2.Add(AlcPoolC + new Vector3(Mathf.Cos(a) * AlcPoolR.x * 0.9f, 0f, Mathf.Sin(a) * AlcPoolR.z * 0.9f)); }
            Drink(drink, "AlcovePool", "Cave pool", ring2, "water_cave_alcove_pool");
            L($"drinking: {seg} stream sections + 2 pools (WaterSource, fresh, unboiled)");
            var pex = Empty(root, "PoolLookout", new Vector3(PoolC.x - 0.4f, Chamber.c.y, PoolC.z + PoolR.z + 1.2f)).AddComponent<Examinable>();
            pex.discoveryId = "cave_deep_pool"; pex.SaveId = "cave_deep_pool"; pex.range = 3.2f;
            Curtain(root);

            // ---- light: dark but readable (sky ambient scaled per band in the materials, custom dark reflections, few lights, no shadows)
            var lights = Child(root, "Lighting");
            var inE = new Vector3(_a[2].x, _a[2].y, _a[2].z) + Vector3.up * 2.3f;
            var inW = new Vector3(_b[_b.Length - 3].x, _b[_b.Length - 3].y, _b[_b.Length - 3].z) + Vector3.up * 2.1f;
            AddLight(lights, "L_EastMouthDaylight", inE, LightType.Point, new Color(0.95f, 0.93f, 0.86f), 3.2f, 9f, true);
            AddLight(lights, "L_WestMouthDaylight", inW, LightType.Point, new Color(0.95f, 0.93f, 0.86f), 2.4f, 7f, true);
            AddLight(lights, "L_PoolShaft", new Vector3(CleftC.x, Chamber.c.y + Chamber.h + 0.6f, CleftC.z), LightType.Spot, new Color(0.84f, 0.9f, 1f), 42f, 15f, true, new Vector3(CleftC.x + 0.5f, PoolC.y, CleftC.z - 0.4f));
            AddLight(lights, "L_ChamberFill", Chamber.c + Vector3.up * 5.5f, LightType.Point, new Color(0.62f, 0.68f, 0.75f), 3.4f, 15f, false);
            var cube = DarkCube();
            var probes = Child(root, "ReflectionProbes");
            Probe(probes, "Probe_StreamPassage", new Vector3(52f, 18f, -174f), new Vector3(86.5f, 25.5f, -156f), cube);
            Probe(probes, "Probe_Chamber", new Vector3(29f, 18f, -184f), new Vector3(51f, 32.5f, -163f), cube);
            Probe(probes, "Probe_WestPassage", new Vector3(9f, 19.5f, -175f), new Vector3(31f, 27f, -145.5f), cube);
            Probe(probes, "Probe_Hidden", new Vector3(38f, 19f, -198f), new Vector3(50f, 26f, -181f), cube);
            L($"lighting: {_lights} realtime lights (no shadows; 3 follow daylight), 4 custom reflection probes (dark cubemap), sky ambient x {string.Join("/", BandOcc.Select(F))} by distance from the mouths");

            Dress(root);
            PlaceKit(root);
            Anchors(root);

            float la = PathLength(_a), lb = PathLength(_b), lh = PathLength(_h) + Hidden.rz;
            L($"walkable length: stream passage {F(la)} m, chamber {F(Chamber.rx * 2f)} m across, west passage {F(lb)} m, hidden branch {F(lh)} m: total {F(la + lb + lh + Chamber.rx * 2f)} m (branches: west passage, hidden chamber)");
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            L($"built in {F((float)clock.Elapsed.TotalSeconds)} s, {_warn} warnings; scene marked dirty (run PrimalCaveBuilder.SaveScene)");
            return Log.ToString();
        }

        [PrimalBridgeCommand]
        public static string SaveScene()
        {
            var scene = EditorSceneManager.GetActiveScene();
            AssetDatabase.SaveAssets();
            bool ok = EditorSceneManager.SaveScene(scene);
            return (ok ? "saved " : "SAVE FAILED ") + scene.path;
        }

        /// <summary>read only: walks the route (floor holes, too steep floor, headroom under 2.2 m, player capsule), void leaks, rock cover</summary>
        [PrimalBridgeCommand]
        public static string Check(string arg)
        {
            Log.Clear(); _warn = 0;
            if (!SceneOk(out _) || !Setup()) return Log.ToString();
            var root = SceneRoots.Find(RootPath);
            if (!root) { W("DeepWaterCave not built"); return Log.ToString(); }
            Physics.SyncTransforms();
            int chunks = root.Find("Shell") ? root.Find("Shell").childCount : 0;
            int cols = root.GetComponentsInChildren<MeshCollider>(true).Length, lights = root.GetComponentsInChildren<Light>(true).Length;
            int tris = 0, kitTris = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh || !mf.GetComponent<MeshRenderer>() || mf.name.Contains("_LOD1") || mf.name.Contains("_LOD2") || mf.name.Contains("_LOD3")) continue;
                int t = (int)(mf.sharedMesh.GetIndexCount(0) / 3); for (int sm = 1; sm < mf.sharedMesh.subMeshCount; sm++) t += (int)(mf.sharedMesh.GetIndexCount(sm) / 3);
                tris += t; if (mf.transform.IsChildOf(root.Find("Kit") ? root.Find("Kit") : root) && root.Find("Kit")) kitTris += t;
            }
            int ws = root.GetComponentsInChildren<WaterSource>(true).Length, exm = root.GetComponentsInChildren<Examinable>(true).Length;
            int missing = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) if (!m || !m.shader || m.shader.name.Contains("Error")) missing++;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true)) if (!mf.sharedMesh) missing++;
            L($"DeepWaterCave: {chunks} shell chunks, {cols} mesh colliders, {tris} rendered tris at LOD0 (kit {kitTris}, kit pieces {Count(root, "Kit")}), {lights} lights, {ws} WaterSources, {exm} Examinables, missing refs {missing}");
            L($"anchors: AI {Count(root, "AI_Anchors")}, RES {Count(root, "RES_Anchors")}, FX {Count(root, "FX_Anchors")}");

            // ---- the walk
            var lines = new List<(string name, List<(Vector3 p, Vector3 dir, float w)> pts)>
            {
                ("stream passage", Walk(new[] { Beyond(_a[0], _a[1]) }.Concat(_a).ToArray(), 0.5f)),
                ("west passage", Walk(_b.Concat(new[] { Beyond(_b[_b.Length - 1], _b[_b.Length - 2]) }).ToArray(), 0.5f)),
                ("hidden branch", Walk(_h, 0.5f)),
            };
            var ringPts = new List<(Vector3, Vector3, float)>();
            for (int i = 0; i < 64; i++) { float a = i / 64f * Mathf.PI * 2f; var p = PoolC + new Vector3(Mathf.Cos(a) * (PoolR.x + 1.8f), 0f, Mathf.Sin(a) * (PoolR.z + 1.8f)); p.y = Chamber.c.y; ringPts.Add((p, new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)), 1.2f)); }
            lines.Add(("pool walkway", ringPts));
            var hid = new List<(Vector3, Vector3, float)>(); var h3 = new Vector3(_h[3].x, _h[3].y, _h[3].z);
            for (int i = 0; i <= 12; i++) { var p = Vector3.Lerp(h3, Hidden.c + new Vector3(0f, 0f, -Hidden.rz * 0.6f), i / 12f); hid.Add((p, Vector3.back, 1.4f)); }
            lines.Add(("hidden chamber", hid));
            var h1 = new Vector3(_h[1].x, 0f, _h[1].z);
            int total = 0, holes = 0, steep = 0, low = 0, blocked = 0, steps = 0; float crawlMin = float.MaxValue, minHead = float.MaxValue;
            foreach (var (name, pts) in lines)
            {
                int lh = 0, ls = 0, ll = 0, lb = 0, lst = 0; float prevY = float.NaN; Vector3 prevP = Vector3.zero;
                foreach (var (p, dir, w) in pts)
                {
                    var side = new Vector3(-dir.z, 0f, dir.x);
                    bool crawl = new Vector2(p.x - h1.x, p.z - h1.z).magnitude < 2.2f && name == "hidden branch";
                    foreach (float o in new[] { 0f, -0.35f, 0.35f })
                    {
                        var q = p + side * (o * w); total++;
                        bool nearWater = NearLine(q, _stream, 0.95f) || NearLine(q, _runnel, 0.6f);
                        if (!Physics.Raycast(q + Vector3.up * 1.6f, Vector3.down, out var hit, 3.6f, ~0, QueryTriggerInteraction.Ignore) || hit.point.y < p.y - 0.6f)
                        { lh++; if (lh <= 3) L($"  HOLE {name} at {V(q)}"); continue; }
                        if (!nearWater && Vector3.Angle(hit.normal, Vector3.up) > 40f) { ls++; if (ls <= 3) L($"  STEEP {name} at {V(hit.point)} {F(Vector3.Angle(hit.normal, Vector3.up))} deg ({hit.collider.name})"); }
                        float head = Physics.Raycast(hit.point + Vector3.up * 0.05f, Vector3.up, out var upHit, 12f, ~0, QueryTriggerInteraction.Ignore) ? upHit.distance + 0.05f : 99f;
                        if (crawl) crawlMin = Mathf.Min(crawlMin, head);
                        else if (o == 0f || !nearWater)
                        {
                            minHead = Mathf.Min(minHead, head);
                            if (head < 2.2f && o == 0f) { ll++; if (ll <= 3) L($"  LOW {name} at {V(hit.point)}: {F(head)} m"); }
                        }
                        if (o == 0f)
                        {
                            if (!float.IsNaN(prevY) && !nearWater && name != "pool walkway" && (q - prevP).magnitude < 0.8f && Mathf.Abs(hit.point.y - prevY) > Mathf.Max(0.32f, new Vector2(q.x - prevP.x, q.z - prevP.z).magnitude * 0.9f))
                            { lst++; if (lst <= 3) L($"  STEP {name} at {V(hit.point)}: {F(hit.point.y - prevY)} m ({hit.collider.name})"); }
                            prevY = hit.point.y; prevP = q;
                            float top = crawl ? 1.1f : 1.75f;
                            if (Physics.CheckCapsule(hit.point + Vector3.up * 0.62f, hit.point + Vector3.up * Mathf.Max(0.62f, top - 0.3f), 0.3f, ~0, QueryTriggerInteraction.Ignore))
                            { lb++; if (lb <= 3) L($"  BLOCKED {name} at {V(hit.point)}"); }
                        }
                    }
                }
                L($"walk {name}: {pts.Count} points x 3, holes {lh}, too steep {ls}, headroom < 2.2 m {ll}, capsule blocked {lb}, steps (> 0.32 m and steeper than 42 deg) {lst}");
                holes += lh; steep += ls; low += ll; blocked += lb; steps += lst;
            }
            L($"walk total: {total} probes, holes {holes}, too steep {steep}, low {low}, blocked {blocked}, steps {steps}; lowest headroom outside the crawl {F(minHead)} m; crawl clearance {F(crawlMin)} m (crouch height 1.15, stand 1.8)");
            if (crawlMin < 1.25f) W($"crawl too low ({F(crawlMin)} m)");

            // ---- void leaks: rays that leave the cave (away from the mouths) and hit nothing
            int rays = 0, leaks = 0, longSight = 0;
            var dirs = new List<Vector3>();
            for (int a = 0; a < 12; a++) for (int e = -1; e <= 2; e++) dirs.Add(Quaternion.Euler(-e * 30f, a * 30f, 0f) * Vector3.forward);
            dirs.Add(Vector3.up); dirs.Add(Vector3.down);
            foreach (var (name, pts) in lines)
                for (int i = 0; i < pts.Count; i += 6)
                {
                    var o = pts[i].p + Vector3.up * 1.2f;
                    if (MouthDist(o) < 9f) continue;
                    foreach (var d in dirs)
                    {
                        rays++;
                        if (Physics.RaycastAll(o, d, 40f, ~0, QueryTriggerInteraction.Ignore).Any(h => !(h.collider is TerrainCollider))) continue;
                        // no hit in 40 m: fine when the ray is still in the cave air (a long passage) or left through a mouth
                        var end = o + d * 40f;
                        if (Field(end) < 0f || SegDist(MouthE, o, end) < 4f || SegDist(MouthW, o, end) < 4f) { longSight++; continue; }
                        leaks++; if (leaks <= 4) L($"  LEAK from {V(o)} towards {V(d)}");
                    }
                }
            L($"void check: {rays} rays from the route (> 9 m from the mouths), {leaks} escaped into the void ({longSight} ran 40 m along the cave or out of a mouth)");

            // ---- rock cover over the ceiling and the sun: terrain above the cave
            int thin = 0; float minCover = float.MaxValue;
            foreach (var (name, pts) in lines)
                foreach (var (p, _, _) in pts)
                {
                    if (MouthDist(p) < 7f) continue;
                    if (!Physics.Raycast(p + Vector3.up * 1f, Vector3.up, out var c, 15f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    float cover = TY(p.x, p.z) - c.point.y; minCover = Mathf.Min(minCover, cover);
                    if (cover < 1f) thin++;
                }
            L($"rock cover (terrain above the ceiling, > 7 m from the mouths): min {F(minCover)} m, {thin} points under 1 m");

            // ---- stream runs downhill, terrain holes
            int up = 0; for (int i = 1; i < _stream.Count; i++) if (_stream[i].y > _stream[i - 1].y + 1e-4f) up++;
            L($"stream: {_stream.Count} points, {F(_stream[0].y)} -> {F(_stream[_stream.Count - 1].y)} m, uphill steps {up}; pool {F(PoolC.y)} m, plunge pool {F(PlungePoolLevel)} m");
            var td = _t.terrainData; int res = td.holesResolution; var tp = _t.transform.position; float cx = td.size.x / res, cz = td.size.z / res; int holeCells = 0;
            foreach (var m in new[] { MouthE, MouthW })
            {
                int x0 = Mathf.Clamp(Mathf.FloorToInt((m.x - 10f - tp.x) / cx), 0, res - 1), z0 = Mathf.Clamp(Mathf.FloorToInt((m.z - 10f - tp.z) / cz), 0, res - 1);
                int w = Mathf.Min(res - x0, Mathf.CeilToInt(20f / cx) + 1), h = Mathf.Min(res - z0, Mathf.CeilToInt(20f / cz) + 1);
                var g = td.GetHoles(x0, z0, w, h); int n = 0; foreach (var b in g) if (!b) n++;
                holeCells += n; L($"terrain holes around the mouth {V(m)}: {n} cells");
            }
            bool ok = steps == 0 && holes == 0 && steep == 0 && low == 0 && blocked == 0 && leaks == 0 && missing == 0 && up == 0 && crawlMin >= 1.25f;
            L(ok ? "CAVE CHECK OK" : "CAVE CHECK: issues above");
            return Log.ToString();
        }

        static float SegDist(Vector3 p, Vector3 a, Vector3 b) { var ab = b - a; float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude)); return Vector3.Distance(p, a + ab * t); }

        /// <summary>edit-mode captures (1600x900) into Documentation/Screenshots/Phase2/Cave: arg "tag" (views with and without a torch light)</summary>
        [PrimalBridgeCommand]
        public static string Capture(string arg)
        {
            Log.Clear(); _warn = 0;
            if (!SceneOk(out var scene) || !Setup()) return Log.ToString();
            string tag = string.IsNullOrEmpty(arg) ? "CV" : arg.Split(';')[0]; bool wasClean = !scene.isDirty;
            if (arg != null && arg.Contains("reload"))
            {
                // a clean reload of the saved scene resets URP's probe state after edit-mode rebuilds
                if (scene.isDirty) { W("reload skipped: the scene has unsaved changes"); }
                else { scene = EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Single); Setup(); L("scene reloaded from disk"); }
            }
            const string dir = "Documentation/Screenshots/Phase2/Cave"; Directory.CreateDirectory(dir);
            var views = new List<(string n, Vector3 eye, Vector3 at, bool torch)>
            {
                ("east_mouth_outside", new Vector3(96.5f, 21.2f, -155.2f), MouthE + Vector3.up * 1.2f, false),
                ("stream_passage", new Vector3(_a[2].x, _a[2].y + 1.6f, _a[2].z), new Vector3(_a[4].x, _a[4].y + 1.2f, _a[4].z), false),
                ("stream_passage_torch", new Vector3(_a[4].x, _a[4].y + 1.6f, _a[4].z), new Vector3(_a[6].x, _a[6].y + 1.0f, _a[6].z), true),
                ("chamber_pool", new Vector3(49.0f, Chamber.c.y + 2.2f, -172.0f), PoolC + Vector3.up * 0.5f, false),
                ("chamber_pool_torch", new Vector3(46.5f, Chamber.c.y + 1.7f, -170.5f), PoolC, true),
                ("curtain", new Vector3(40.5f, Chamber.c.y + 1.6f, -177.5f), new Vector3(42f, Chamber.c.y + 1.2f, -183f), true),
                ("hidden_nest_torch", new Vector3(_h[3].x, _h[3].y + 1.5f, _h[3].z), Hidden.c + Vector3.up * 0.3f, true),
                ("west_passage_torch", new Vector3(_b[1].x, _b[1].y + 1.6f, _b[1].z), new Vector3(_b[3].x, _b[3].y + 1.2f, _b[3].z), true),
                ("west_mouth_outside", new Vector3(10.5f, 23.5f, -133.5f), MouthW + Vector3.up * 1.2f, false),
            };
            var camGo = new GameObject("__CaveCaptureCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>(); var main = Camera.main; if (main) cam.CopyFrom(main);
            cam.enabled = false; cam.nearClipPlane = 0.05f; cam.farClipPlane = 1500f; cam.fieldOfView = 62f;
            var urp = camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(); urp.renderPostProcessing = true;
            // URP's probe atlas throws on a reflection probe without a texture (any probe in the scene): switched off while capturing
            foreach (var rp in Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                L($"probe {SceneRoots.PathOf(rp.transform)} active {rp.isActiveAndEnabled} mode {rp.mode} texture {(rp.texture ? rp.texture.name : "none")} size {V(rp.size)}");
            bool noProbes = tag.Contains("noprobes");
            var noTex = Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None).Where(rp => rp.enabled && (!rp.texture || noProbes)).ToList();
            foreach (var rp in noTex) { L($"capture: reflection probe without texture switched off meanwhile: {SceneRoots.PathOf(rp.transform)} (mode {rp.mode})"); rp.enabled = false; }
            var hidden = new List<GameObject>();
            foreach (var part in (arg ?? "").Split(';').Where(x => x.StartsWith("hide=")))
                foreach (var n in part.Substring(5).Split('+'))
                {
                    var t = n.StartsWith("/") ? SceneRoots.Find(n.Substring(1)) : SceneRoots.Find(RootPath + "/" + n);       // "/World/..." = any scene path (restored after)
                    if (t && t.gameObject.activeSelf) { t.gameObject.SetActive(false); hidden.Add(t.gameObject); L("capture: hidden " + n); }
                }
            if (arg != null && arg.Contains("only="))
            {
                var only = arg.Split(';').First(x => x.StartsWith("only=")).Substring(5).Split('+');
                views = views.Where(v => only.Contains(v.n)).ToList();
            }
            var torchGo = new GameObject("__CaveTorch") { hideFlags = HideFlags.HideAndDontSave };
            var torch = torchGo.AddComponent<Light>(); torch.type = LightType.Point; torch.color = new Color(1f, 0.68f, 0.38f); torch.intensity = 3.5f; torch.range = 11f; torch.shadows = LightShadows.Soft;
            try
            {
                foreach (var v in views)
                {
                    cam.transform.SetPositionAndRotation(v.eye, Quaternion.LookRotation((v.at - v.eye).normalized));
                    torchGo.SetActive(v.torch); torchGo.transform.position = v.eye + cam.transform.right * 0.4f - Vector3.up * 0.35f;
                    const int w = 1600, h = 900;
                    var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                    cam.targetTexture = rt; cam.Render(); cam.Render();
                    var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                    RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(); RenderTexture.active = null; cam.targetTexture = null;
                    string path = $"{dir}/{tag}_{v.n}.png"; File.WriteAllBytes(path, tex.EncodeToPNG());
                    Object.DestroyImmediate(tex); Object.DestroyImmediate(rt);
                    L($"wrote {path}");
                }
            }
            finally
            {
                Object.DestroyImmediate(camGo); Object.DestroyImmediate(torchGo);
                foreach (var rp in noTex) if (rp) rp.enabled = true;
                foreach (var g in hidden) if (g) g.SetActive(true);
                if (wasClean && scene.isDirty) EditorSceneManager.SaveScene(scene);
            }
            return Log.ToString();
        }

        /// <summary>a node 1.2 m further out than 'outer' (seen from 'inner'), on the terrain: the approach to a mouth</summary>
        static N Beyond(N outer, N inner)
        {
            var d = new Vector3(outer.x - inner.x, 0f, outer.z - inner.z).normalized * 1.2f;
            float x = outer.x + d.x, z = outer.z + d.z;
            return new N(x, TY(x, z), z, outer.w, outer.h);
        }

        /// <summary>debug (read only): "x,z": every hit straight down, the terrain, the hole state, the cave fields there</summary>
        [PrimalBridgeCommand]
        public static string Probe(string arg)
        {
            Log.Clear(); if (!Setup()) return Log.ToString();
            Physics.SyncTransforms();
            foreach (var spec in (arg ?? "").Split(';'))
            {
                var s = spec.Split(','); if (s.Length < 2) continue;
                float x = float.Parse(s[0], CultureInfo.InvariantCulture), z = float.Parse(s[1], CultureInfo.InvariantCulture);
                float ty = TY(x, z), fl = FloorAt(x, z);
                var td = _t.terrainData; var tp = _t.transform.position; int res = td.holesResolution;
                int hx = Mathf.Clamp(Mathf.FloorToInt((x - tp.x) / (td.size.x / res)), 0, res - 1), hz = Mathf.Clamp(Mathf.FloorToInt((z - tp.z) / (td.size.z / res)), 0, res - 1);
                bool solid = td.GetHoles(hx, hz, 1, 1)[0, 0];
                L($"({F(x)}, {F(z)}): terrain {F(ty)} {(solid ? "solid" : "HOLE")}, floor {F(fl)}, cap {F(CapY(x, z))}, field at terrain {F(Field(new Vector3(x, ty, z)))}, portal {InPortal(new Vector3(x, ty, z))}");
                var hits = Physics.RaycastAll(new Vector3(x, ty + 6f, z), Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance);
                foreach (var h in hits) L($"   hit {F(h.point.y)} n {V(h.normal)} {h.collider.name}");
                var sb = new StringBuilder("   fieldA by height:"); for (float y = ty - 1.5f; y <= ty + 1.6f; y += 0.25f) sb.Append($" {F(y)}:{F(FieldA(new Vector3(x, y, z)))}"); L(sb.ToString());
            }
            return Log.ToString();
        }

        /// <summary>debug (read only): "x,y,z": a dense fan of rays from an eye point; rays that hit nothing within 60 m are
        /// gaps in the shell; prints where each such ray crosses the cave wall (field 0)</summary>
        [PrimalBridgeCommand]
        public static string Gaps(string arg)
        {
            Log.Clear(); if (!Setup()) return Log.ToString();
            Physics.SyncTransforms();
            var s = (arg ?? "").Split(',');
            var eye = new Vector3(float.Parse(s[0], CultureInfo.InvariantCulture), float.Parse(s[1], CultureInfo.InvariantCulture), float.Parse(s[2], CultureInfo.InvariantCulture));
            int rays = 0, esc = 0;
            float step = s.Length > 3 ? float.Parse(s[3], CultureInfo.InvariantCulture) : 2f;
            for (float a = 0; a < 360; a += step)
                for (float e = -40; e <= 60; e += step)
                {
                    rays++;
                    var d = Quaternion.Euler(-e, a, 0f) * Vector3.forward;
                    // the terrain collider answers rays from below too: only hits on anything else count as the cave wall
                    var root0 = SceneRoots.Find(RootPath);
                    if (Physics.RaycastAll(eye, d, 60f, ~0, QueryTriggerInteraction.Ignore).Any(h => root0 && h.collider.transform.IsChildOf(root0))) continue;
                    esc++;
                    if (esc > 25) continue;
                    Vector3 cross = Vector3.zero; for (float t = 0.2f; t < 40f; t += 0.1f) { var q = eye + d * t; if (FieldA(q) > 0f) { cross = q; break; } }
                    L($"  escape az {F(a)} el {F(e)}: crosses the wall at {V(cross)} (chunk {Mathf.FloorToInt(cross.x / ChunkSize)}_{Mathf.FloorToInt(cross.z / ChunkSize)}, field {F(Field(cross))}, ty {F(TY(cross.x, cross.z))})");
                }
            L($"gaps from {V(eye)}: {rays} rays, {esc} hit nothing within 60 m");
            return Log.ToString();
        }

        static int Count(Transform root, string name) { var t = root.Find(name); return t ? t.childCount : 0; }
        static bool NearLine(Vector3 p, List<Vector3> line, float r)
        {
            for (int i = 0; i + 1 < line.Count; i++)
            {
                float ax = line[i].x, az = line[i].z, bx = line[i + 1].x - ax, bz = line[i + 1].z - az, l2 = bx * bx + bz * bz;
                float t = l2 < 1e-6f ? 0f : Mathf.Clamp01(((p.x - ax) * bx + (p.z - az) * bz) / l2);
                float dx = ax + bx * t - p.x, dz = az + bz * t - p.z;
                if (dx * dx + dz * dz < r * r) return true;
            }
            return false;
        }

        [PrimalBridgeCommand]
        public static string Survey(string arg)
        {
            Log.Clear();
            var t = Terrain.activeTerrain;
            if (t)
            {
                var td = t.terrainData;
                L($"terrain {t.name} pos {V(t.transform.position)} size {V(td.size)} hm {td.heightmapResolution} holes {td.holesResolution} enableHolesTextureCompression {td.enableHolesTextureCompression} asset {AssetDatabase.GetAssetPath(td)}");
                L($"terrain castShadows {t.shadowCastingMode} drawInstanced {t.drawInstanced}");
            }
            foreach (var n in new[] { SceneRoots.Caves, SceneRoots.Waterfalls })
            {
                var r = SceneRoots.Find(n);
                if (!r) { L(n + ": missing"); continue; }
                L($"{n}:");
                foreach (var c in r.GetComponentsInChildren<Transform>(true))
                {
                    if (c == r) continue;
                    int depth = 0; var p = c; while (p && p != r) { depth++; p = p.parent; }
                    if (depth > 3) continue;
                    var comps = string.Join(",", c.GetComponents<Component>().Where(x => x && !(x is Transform)).Select(x => x.GetType().Name));
                    var rend = c.GetComponent<Renderer>();
                    L($"{new string(' ', depth * 2)}{c.name} pos {V(c.position)} rot {V(c.eulerAngles)} scl {V(c.lossyScale)} [{comps}]" + (rend ? $" bounds {V(rend.bounds.min)}..{V(rend.bounds.max)} mat {(rend.sharedMaterial ? rend.sharedMaterial.name + "/" + rend.sharedMaterial.shader.name : "none")}" : ""));
                }
            }
            foreach (var go in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(x => x.name.Contains("Cave")))
            {
                var mf = go.GetComponent<MeshFilter>();
                L($"named *Cave*: {SceneRoots.PathOf(go)} active {go.gameObject.activeInHierarchy} pos {V(go.position)} fwd {V(go.forward)}" + (mf && mf.sharedMesh ? $" mesh {mf.sharedMesh.name} local bounds {V(mf.sharedMesh.bounds.center)} ext {V(mf.sharedMesh.bounds.extents)} v {mf.sharedMesh.vertexCount}" : ""));
            }
            Physics.SyncTransforms();
            // rays from the cave floor
            var cave = new Vector3(-18f, 18.4f, -131f);
            if (arg != null && arg.StartsWith("at="))
            {
                var s = arg.Substring(3).Split(',');
                cave = new Vector3(float.Parse(s[0], CultureInfo.InvariantCulture), float.Parse(s[1], CultureInfo.InvariantCulture), float.Parse(s[2], CultureInfo.InvariantCulture));
            }
            foreach (var y in new[] { 1.0f, 2.2f })
            {
                var sb = new StringBuilder($"rays from {V(cave + Vector3.up * y)}: ");
                for (int a = 0; a < 360; a += 15)
                {
                    var d = Quaternion.Euler(0, a, 0) * Vector3.forward;
                    sb.Append(Physics.Raycast(cave + Vector3.up * y, d, out var h, 60f, ~0, QueryTriggerInteraction.Ignore) ? $"{a}:{F(h.distance)}({h.collider.name}) " : $"{a}:- ");
                }
                L(sb.ToString());
            }
            // grid: floor + ceiling along a line from the cave into -z
            for (float dz = 0; dz <= 24; dz += 2)
                for (float dx = -6; dx <= 6; dx += 3)
                {
                    var p = cave + new Vector3(dx, 0f, -dz);
                    string down = Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out var hd, 10f, ~0, QueryTriggerInteraction.Ignore) ? $"{F(hd.point.y)}({hd.collider.name})" : "-";
                    string up = Physics.Raycast(p + Vector3.up * 1.5f, Vector3.up, out var hu, 40f, ~0, QueryTriggerInteraction.Ignore) ? $"{F(hu.point.y)}({hu.collider.name})" : "-";
                    L($"  grid dx {dx} dz -{dz}: floor {down} ceil {up} terrain {(t ? F(t.SampleHeight(p) + t.transform.position.y) : "-")}");
                }
            var player = Object.FindObjectsByType<CharacterController>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
            if (player) L($"player CC height {F(player.height)} radius {F(player.radius)} slope {F(player.slopeLimit)} step {F(player.stepOffset)} at {V(player.transform.position)}");
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(x => x.type == LightType.Directional))
                L($"dir light {l.name} shadows {l.shadows} intensity {F(l.intensity)}");
            L($"ambient {RenderSettings.ambientMode} fog {RenderSettings.fog} {RenderSettings.fogMode} density {RenderSettings.fogDensity}");
            L($"shadow distance {F(QualitySettings.shadowDistance)}");
            foreach (var rz in Object.FindObjectsByType<AudioReverbZone>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                L($"reverb {SceneRoots.PathOf(rz.transform)} {V(rz.transform.position)} min {F(rz.minDistance)} max {F(rz.maxDistance)}");
            foreach (var g in AssetDatabase.FindAssets("t:Material", new[] { "Assets/_Project/Art" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g); var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (m && m.shader && (m.shader.name.Contains("Wet") || m.name.Contains("Rock") || m.name.Contains("Cave"))) L($"mat {p} {m.shader.name}");
            }
            foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project/Prefabs" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (p.Contains("Rock") || p.Contains("Cave") || p.Contains("Bone") || p.Contains("Skeleton") || p.Contains("Nest") || p.Contains("Cliff") || p.Contains("Phase2")) L($"prefab {p}");
            }
            return Log.ToString();
        }

        /// <summary>read only: every renderer / collider / interactable within r m (xz) of "x,z,r"</summary>
        [PrimalBridgeCommand]
        public static string Near(string arg)
        {
            Log.Clear();
            var s = (arg ?? "").Split(',');
            if (s.Length < 3) return "arg x,z,r";
            float x = float.Parse(s[0], CultureInfo.InvariantCulture), z = float.Parse(s[1], CultureInfo.InvariantCulture), r = float.Parse(s[2], CultureInfo.InvariantCulture);
            var c = new Vector3(x, 0f, z);
            var seen = new HashSet<Transform>();
            foreach (var col in Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (col is TerrainCollider) continue;
                var b = col.bounds; var q = b.ClosestPoint(new Vector3(x, b.center.y, z)); q.y = 0f;
                if ((q - c).magnitude > r) continue;
                seen.Add(col.transform);
                L($"COL {SceneRoots.PathOf(col.transform)} {col.GetType().Name} trig {col.isTrigger} bounds {V(b.min)}..{V(b.max)}");
            }
            foreach (var rd in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (rd is ParticleSystemRenderer) continue;
                var b = rd.bounds; var q = b.ClosestPoint(new Vector3(x, b.center.y, z)); q.y = 0f;
                if ((q - c).magnitude > r || seen.Contains(rd.transform)) continue;
                if (rd.name.EndsWith("_LOD1") || rd.name.EndsWith("_LOD2")) continue;
                L($"REN {SceneRoots.PathOf(rd.transform)} bounds {V(b.min)}..{V(b.max)}");
            }
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!mb || mb.GetType().Namespace == null || !mb.GetType().Namespace.StartsWith("PrimalFrontier")) continue;
                var p = mb.transform.position; if (new Vector2(p.x - x, p.z - z).magnitude > r) continue;
                L($"MB {mb.GetType().Name} {SceneRoots.PathOf(mb.transform)} {V(p)}");
            }
            var t = Terrain.activeTerrain;
            if (t) for (float dz = -r; dz <= r; dz += 2f) { var sb = new StringBuilder($"h z {F(z + dz)}: "); for (float dx = -r; dx <= r; dx += 2f) sb.Append(F(t.SampleHeight(new Vector3(x + dx, 0, z + dz)) + t.transform.position.y) + " "); L(sb.ToString()); }
            return Log.ToString();
        }
    }
}
