using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PrimalFrontier.Core;
using PrimalFrontier.World;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// Phase 2 zone builders: one partial class, one file per zone owner (ENV-A: this core + .Valley + .Wetland; ENV-B:
    /// .FernForest + .Foothills; BONE: .BoneValley). This file is the SHARED CORE (ENV-A). Keep its API stable; add helpers in
    /// your own partial file (prefix them with your zone name) instead of editing this one. Nothing here runs on its own
    /// except the read-only bridge command <c>Survey</c>.
    ///
    /// CORE API (all static, call from your partial file):
    ///   Session    Begin(prefix, what) / L(msg) / W(warning) / End() -> log text, appended to Documentation/Phase2/Logs/&lt;prefix&gt;_build.txt
    ///              Args("a=1;b") -> dictionary; F(float), V(Vector3) invariant formatting
    ///   Scene      OpenIsland(out scene, out wasDirty) (false = cannot run), SaveIsland(scene, wasDirty) (never saves a scene that
    ///              had other unsaved changes unless Args has "force")
    ///   Zones      Zones[] (the six Phase 2 zones), ZoneById("valley"), zone.Weight(p) (1 inside radius, smoothstep to 0 at
    ///              radius + blend; capsule zones use the segment centre -> end), OwnWeight(zone, p) = Weight faded out where
    ///              another zone's core starts (so blend bands never overwrite a neighbour), zone.Core(p)
    ///              Smooth01(t), Noise(x, z, scale, seed) 0..1 value noise, Rand01(a, b) hash
    ///   Features   Feat (Art/Environment/Terrain/env_features_v2.json: water lines, ford, wetland blobs, migration route, props,
    ///              locations), LoadFeat(), WaterDist(p) (xz metres to the nearest river / brook / pool / pond / lagoon edge,
    ///              0 in the water), RouteDist(p) (xz metres to the migration route), Route (points), P3(float[], i)
    ///   Terrain    FindTerrain() -> Ter / TD, GroundY(p), Ground(p), Normal(p), Slope(p) (degrees)
    ///              BackupTerrain(prefix) -> TerrainData backup asset Art/Terrain/_Backup/TD_Island_before_&lt;prefix&gt;.asset
    ///              (made once per prefix, never overwritten; it is also the BASELINE every re-run starts from, which makes the
    ///              edits idempotent)
    ///              EditSplat(zone, backup, fn) / EditDetails(zone, backup, layers, fn) / EditHeights(zone, backup, fn):
    ///              per texel with OwnWeight &gt; 0: result = lerp(baseline, fn(baseline), OwnWeight); texels outside keep their
    ///              current value. Returns the number of texels written.
    ///              DetailLayer(name) / AddDetailProto(prefab, ...) / TreeProto(prefabName, add) / TreeProtoIndex
    ///              OwnTreeSlots(key), TreeGrid(except) (5 m grid of the other terrain trees), SplatSampler(zone[, baseline]).Get(p, layer)
    ///              SetOwnTrees(key, trees): the agent's own terrain trees (recorded in Tools/_zones/&lt;key&gt;_trees.json): recorded
    ///              slots are overwritten in place, extras appended, surplus removed only from the tail (v1 / other agents'
    ///              tree indices never move, TreeHarvest saves stay valid)
    ///   Groups     Group(zone, sub) -> World/Environment/&lt;slot&gt;/&lt;ZoneGroup&gt;/&lt;sub&gt; (made when missing), ClearGroup(t)
    ///              Marker(parent, name, pos, rot) -> an empty transform (landmark placeholder, FX anchor)
    ///   Prefabs    Prefab(name): Prefabs/Environment/{Phase2, Zones, PC, root} by file name (null + warning when missing)
    ///   Placement  Blockers (built by BuildBlockers(scene)): every active Interactable (resource node 2.2 m, others 1.5 m),
    ///              ZONE_* markers, story props, footprints, player; IsBlocked(p, extra); Spacing grid: Near(grid, p, r) / Add(grid, p)
    ///              SolidAt(p, r, ignoreRoot): a non-terrain collider there; FootprintBlocked(go, extra): a placed object's bounds reach a blocker; Place(parent, prefab, pos, yaw, scale, align, sink)
    ///   Check      CheckZone(zone, roots, prefix): per model counts, missing prefab / mesh / material / script / LOD refs,
    ///              resource nodes covered by a collider under the roots or by the agent's terrain trees, nodes near the spawn
    /// Units: Unity world metres. Everything is deterministic (hash based), so a re-run gives the same scene.
    /// </summary>
    public static partial class PrimalZonesBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Island_VerticalSlice.unity";
        public const string TerrainBackupDir = "Assets/_Project/Art/Terrain/_Backup";
        public const string FeaturesPath = "Assets/_Project/Art/Environment/Terrain/env_features_v2.json";
        public const string PrefabP2 = "Assets/_Project/Prefabs/Environment/Phase2", PrefabZones = "Assets/_Project/Prefabs/Environment/Zones",
            PrefabPC = "Assets/_Project/Prefabs/Environment/PC", PrefabEnv = "Assets/_Project/Prefabs/Environment";
        public const string ZoneMatDir = "Assets/_Project/Art/Environment/Materials/Zones";
        public const string LogDir = "Documentation/Phase2/Logs", TreeRecordDir = "Tools/_zones";

        // ================================================================== session / log
        static readonly StringBuilder Log = new StringBuilder();
        static int _warn; static string _prefix = "Z"; static bool _force;
        public static int Warnings => _warn;
        public static void L(string s) { Log.AppendLine(s); Debug.Log($"[PrimalZones] {s}"); }
        public static void W(string s) { _warn++; Log.AppendLine("WARNING: " + s); Debug.LogWarning($"[PrimalZones] {s}"); }
        public static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        public static string V(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "({0:0.#}, {1:0.#}, {2:0.#})", v.x, v.y, v.z);
        public static void Begin(string prefix, string what)
        {
            Log.Clear(); _warn = 0; _prefix = prefix; _force = what != null && what.Contains("force");
            L($"{prefix} {what} started {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }
        public static string End()
        {
            L($"finished, {_warn} warning(s)");
            try { Directory.CreateDirectory(LogDir); File.AppendAllText(Path.Combine(LogDir, _prefix + "_build.txt"), Log + "\n"); }
            catch (Exception e) { Debug.LogWarning("[PrimalZones] log write failed: " + e.Message); }
            return Log.ToString();
        }
        public static Dictionary<string, string> Args(string arg)
        {
            var d = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(arg)) return d;
            foreach (var part in arg.Split(';'))
            {
                var p = part.Trim(); if (p.Length == 0) continue;
                int eq = p.IndexOf('=');
                if (eq < 0) d[p] = ""; else d[p.Substring(0, eq).Trim()] = p.Substring(eq + 1).Trim();
            }
            return d;
        }

        // ================================================================== scene
        public static bool OpenIsland(out Scene scene, out bool wasDirty)
        {
            scene = EditorSceneManager.GetActiveScene(); wasDirty = scene.isDirty;
            if (EditorApplication.isPlayingOrWillChangePlaymode) { W("stop Play mode first"); return false; }
            if (scene.path == ScenePath) return true;
            if (scene.isDirty) { W($"the open scene ({scene.name}) has unsaved changes: save it or open {ScenePath}"); return false; }
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single); wasDirty = false;
            return true;
        }
        public static void SaveIsland(Scene scene, bool wasDirty)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            if (wasDirty && !_force) { W("the scene had other unsaved changes before this command: NOT saved (arg force saves anyway)"); return; }
            EditorSceneManager.SaveScene(scene);
            L("scene saved: " + scene.path);
        }
        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/'), leaf = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
        public static string PathOf(Transform t) => SceneRoots.PathOf(t);

        // ================================================================== zones
        public sealed class Zone
        {
            public string id, name, owner, slot, landmark;
            public Vector3 centre, end;       // end == centre: a circle; otherwise a capsule along centre -> end
            public float radius, blend;
            public string GroupPath => slot + "/" + name;
            public float Dist(Vector3 p)
            {
                Vector2 a = new Vector2(centre.x, centre.z), b = new Vector2(end.x, end.z), q = new Vector2(p.x, p.z);
                var ab = b - a; float t = ab.sqrMagnitude < 1e-4f ? 0f : Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude);
                return (a + ab * t - q).magnitude;
            }
            /// <summary>1 inside the radius, smoothstep to 0 at radius + blend</summary>
            public float Weight(Vector3 p) { float d = Dist(p); return d <= radius ? 1f : d >= radius + blend ? 0f : 1f - Smooth01((d - radius) / Mathf.Max(0.01f, blend)); }
            /// <summary>1 inside the radius, 0 at radius + blend / 2 (the part a neighbour must never overwrite)</summary>
            public float Core(Vector3 p) { float d = Dist(p), e = Mathf.Max(0.01f, blend * 0.5f); return d <= radius ? 1f : d >= radius + e ? 0f : 1f - Smooth01((d - radius) / e); }
            public Rect Bounds => Rect.MinMaxRect(Mathf.Min(centre.x, end.x) - radius - blend, Mathf.Min(centre.z, end.z) - radius - blend,
                Mathf.Max(centre.x, end.x) + radius + blend, Mathf.Max(centre.z, end.z) + radius + blend);
        }

        /// <summary>Phase 2 zones (PHASE2_OWNERSHIP.md). Blend bands 20-40 m.</summary>
        static Zone Mk(string id, string name, string owner, string slot, Vector3 c, float r, float blend, Vector3? end = null)
            => new Zone { id = id, name = name, owner = owner, slot = slot, landmark = "LM_" + name, centre = c, end = end ?? c, radius = r, blend = blend };
        public static readonly Zone[] Zones =
        {
            Mk("valley", "MigrationValley", "EA", SceneRoots.Forest, new Vector3(0f, 9f, -35f), 95f, 30f),
            Mk("wetland", "PrehistoricWetland", "EA", SceneRoots.Wetlands, new Vector3(102f, 0.4f, 172f), 45f, 25f),
            Mk("bone", "BoneValley", "BV", SceneRoots.Forest, new Vector3(-105f, 21f, -76f), 50f, 25f),
            Mk("fern", "GiantFernForest", "EB", SceneRoots.Forest, new Vector3(168f, 8f, 20f), 70f, 30f),
            Mk("foothills", "VolcanicFoothills", "EB", SceneRoots.Volcano, new Vector3(-136f, 23f, -125f), 40f, 30f, new Vector3(-122f, 44f, -216f)),
            Mk("cave", "DeepWaterCave", "CV", SceneRoots.Caves, new Vector3(-18f, 18f, -131f), 15f, 10f),
        };
        public static Zone ZoneById(string id) => Zones.FirstOrDefault(z => z.id == id);

        /// <summary>the zone's weight, faded out where another zone's core begins (never write into a neighbour's core)</summary>
        public static float OwnWeight(Zone z, Vector3 p)
        {
            float w = z.Weight(p); if (w <= 0f) return 0f;
            float other = 0f;
            foreach (var o in Zones) if (o != z && o.id != "cave") other = Mathf.Max(other, o.Core(p));
            return w * (1f - other);
        }

        public static float Smooth01(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
        public static uint Hash(int a, int b = 0) { unchecked { uint h = (uint)a * 374761393u + (uint)b * 668265263u; h = (h ^ (h >> 13)) * 1274126177u; return h ^ (h >> 16); } }
        public static float Rand01(int a, int b = 0) => (Hash(a, b) & 0xFFFFFF) / 16777216f;
        /// <summary>smooth value noise 0..1 (feature size = scale metres)</summary>
        public static float Noise(float x, float z, float scale, int seed)
        {
            float fx = x / scale, fz = z / scale; int ix = Mathf.FloorToInt(fx), iz = Mathf.FloorToInt(fz);
            float tx = Smooth01(fx - ix), tz = Smooth01(fz - iz);
            float a = Rand01(ix + seed * 7919, iz), b = Rand01(ix + 1 + seed * 7919, iz), c = Rand01(ix + seed * 7919, iz + 1), d = Rand01(ix + 1 + seed * 7919, iz + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
        }

        // ================================================================== features (env_features_v2.json)
        [Serializable] public class FLine { public float[] points; public float[] halfWidths; public float halfWidth; }
        [Serializable] public class FFall { public float[] lip; public float[] pool; public float poolRadius; public float drop; }
        [Serializable] public class FBlob { public float[] center; public float radius; }
        [Serializable] public class FWet { public float level; public FBlob[] blobs; }
        [Serializable] public class FPond { public float[] center; public float level; }
        [Serializable] public class FWater { public FLine riverA, riverB, brook; public FFall waterfall; public float[] ford; public FWet wetland; public FPond pond; }
        [Serializable] public class FProps { public float[] knollTop, wallows, skeleton, nest, oldCamp, spring, fossilBed; }
        [Serializable] public class FLoc { public string id; public float[] pos; public float radius; public string what; }
        [Serializable] public class Features { public FWater water; public float[] migration; public FProps props; public FLoc[] locations; }
        public static Features Feat;
        static List<Vector3> _route; static List<Vector4> _water;   // water samples: (x, halfwidth, z, 0)
        public static List<Vector3> Route => _route;
        public static Vector3 P3(float[] a, int i = 0) => a == null || a.Length < i * 3 + 3 ? Vector3.zero : new Vector3(a[i * 3], a[i * 3 + 1], a[i * 3 + 2]);
        public static List<Vector3> Pts(float[] a) { var l = new List<Vector3>(); if (a != null) for (int i = 0; i + 2 < a.Length; i += 3) l.Add(new Vector3(a[i], a[i + 1], a[i + 2])); return l; }
        public static Features LoadFeat()
        {
            if (!File.Exists(FeaturesPath)) { W("missing " + FeaturesPath); return Feat = null; }
            Feat = JsonUtility.FromJson<Features>(File.ReadAllText(FeaturesPath));
            _route = Pts(Feat.migration);
            _water = new List<Vector4>();
            void Line(FLine ln, float fixedHw)
            {
                if (ln == null) return; var P = Pts(ln.points);
                for (int i = 0; i < P.Count; i++) _water.Add(new Vector4(P[i].x, ln.halfWidths != null && i < ln.halfWidths.Length ? ln.halfWidths[i] : (ln.halfWidth > 0 ? ln.halfWidth : fixedHw), P[i].z, 0));
            }
            Line(Feat.water.riverA, 3f); Line(Feat.water.riverB, 3f); Line(Feat.water.brook, 1f);
            var pool = P3(Feat.water.waterfall.pool); _water.Add(new Vector4(pool.x, Feat.water.waterfall.poolRadius, pool.z, 0));
            var pond = P3(Feat.water.pond.center); _water.Add(new Vector4(pond.x, 17f, pond.z, 0));
            if (Feat.water.wetland != null && Feat.water.wetland.blobs != null)
                foreach (var b in Feat.water.wetland.blobs) { var c = P3(b.center); _water.Add(new Vector4(c.x, b.radius * 0.7f, c.z, 0)); }
            return Feat;
        }
        /// <summary>xz metres from p to the nearest water edge of the feature lines (0 inside); lagoon: also where the terrain is under the level</summary>
        public static float WaterDist(Vector3 p)
        {
            if (_water == null) return 999f;
            float best = 999f;
            foreach (var w in _water) { float dx = w.x - p.x, dz = w.z - p.z; float d = Mathf.Sqrt(dx * dx + dz * dz) - w.y; if (d < best) best = d; }
            return Mathf.Max(0f, best);
        }
        public static float RouteDist(Vector3 p)
        {
            if (_route == null || _route.Count < 2) return 999f;
            float best = 1e9f; var q = new Vector2(p.x, p.z);
            for (int i = 0; i + 1 < _route.Count; i++)
            {
                Vector2 a = new Vector2(_route[i].x, _route[i].z), b = new Vector2(_route[i + 1].x, _route[i + 1].z);
                var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                best = Mathf.Min(best, (a + ab * t - q).magnitude);
            }
            return best;
        }
        /// <summary>xz distance from p to the segment a-b</summary>
        public static float SegDist(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector2 A = new Vector2(a.x, a.z), B = new Vector2(b.x, b.z), q = new Vector2(p.x, p.z); var ab = B - A;
            float t = ab.sqrMagnitude < 1e-4f ? 0f : Mathf.Clamp01(Vector2.Dot(q - A, ab) / ab.sqrMagnitude);
            return (A + ab * t - q).magnitude;
        }

        // ================================================================== terrain
        public static Terrain Ter; public static TerrainData TD;
        public static Terrain FindTerrain()
        {
            var go = GameObject.Find("ENV_Island_Terrain");
            Ter = go ? go.GetComponent<Terrain>() : Terrain.activeTerrain;
            TD = Ter ? Ter.terrainData : null;
            return Ter;
        }
        public static Vector3 TPos => Ter ? Ter.transform.position : Vector3.zero;
        public static float GroundY(Vector3 p) => Ter ? Ter.SampleHeight(p) + Ter.transform.position.y : p.y;
        public static Vector3 Ground(Vector3 p) { p.y = GroundY(p); return p; }
        public static Vector3 Normal(Vector3 p)
        {
            if (!Ter) return Vector3.up; var lp = p - TPos;
            return TD.GetInterpolatedNormal(lp.x / TD.size.x, lp.z / TD.size.z);
        }
        public static float Slope(Vector3 p) => Vector3.Angle(Normal(p), Vector3.up);

        /// <summary>the prefix's terrain backup (made once, never overwritten): the baseline of every re-run</summary>
        public static TerrainData BackupTerrain(string prefix)
        {
            if (!TD) return null;
            string p = $"{TerrainBackupDir}/TD_Island_before_{prefix}.asset";
            var bk = AssetDatabase.LoadAssetAtPath<TerrainData>(p);
            if (bk) { L($"terrain baseline: {p} (existing backup, kept)"); return bk; }
            EnsureFolder(TerrainBackupDir);
            string src = AssetDatabase.GetAssetPath(TD);
            if (string.IsNullOrEmpty(src) || !AssetDatabase.CopyAsset(src, p)) { W($"terrain backup failed ({src} -> {p}): nothing changed"); return null; }
            AssetDatabase.ImportAsset(p);
            bk = AssetDatabase.LoadAssetAtPath<TerrainData>(p);
            L($"terrain backup made: {src} -> {p}");
            return bk;
        }

        public struct TexRect { public int x0, z0, w, h; }
        static TexRect RectFor(Zone z, int res, float sizeX, float sizeZ)
        {
            var b = z.Bounds; var tp = TPos;
            int x0 = Mathf.Clamp(Mathf.FloorToInt((b.xMin - tp.x) / sizeX * res), 0, res - 1), x1 = Mathf.Clamp(Mathf.CeilToInt((b.xMax - tp.x) / sizeX * res), 0, res);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((b.yMin - tp.z) / sizeZ * res), 0, res - 1), z1 = Mathf.Clamp(Mathf.CeilToInt((b.yMax - tp.z) / sizeZ * res), 0, res);
            return new TexRect { x0 = x0, z0 = z0, w = Mathf.Max(1, x1 - x0), h = Mathf.Max(1, z1 - z0) };
        }

        public delegate void SplatFn(Vector3 world, float ownW, float[] baseline, float[] target);
        /// <summary>splat: target starts as a copy of the baseline weights (layers of the current terrain), fn edits it, result = lerp by OwnWeight, normalised</summary>
        public static int EditSplat(Zone z, TerrainData baseline, SplatFn fn)
        {
            int res = TD.alphamapResolution, nl = TD.alphamapLayers;
            var r = RectFor(z, res, TD.size.x, TD.size.z);
            var cur = TD.GetAlphamaps(r.x0, r.z0, r.w, r.h);
            float[,,] bas = null;
            if (baseline && baseline.alphamapResolution == res && baseline.alphamapLayers == nl) bas = baseline.GetAlphamaps(r.x0, r.z0, r.w, r.h);
            else W("splat baseline missing or different layout: the current splat is the baseline (re-runs are not idempotent)");
            var b = new float[nl]; var t = new float[nl]; int n = 0; var tp = TPos;
            for (int iz = 0; iz < r.h; iz++)
                for (int ix = 0; ix < r.w; ix++)
                {
                    var w = new Vector3(tp.x + (r.x0 + ix + 0.5f) / res * TD.size.x, 0f, tp.z + (r.z0 + iz + 0.5f) / res * TD.size.z);
                    float ow = OwnWeight(z, w); if (ow <= 0f) continue;
                    w.y = GroundY(w);
                    for (int l = 0; l < nl; l++) { b[l] = bas != null ? bas[iz, ix, l] : cur[iz, ix, l]; t[l] = b[l]; }
                    fn(w, ow, b, t);
                    float s = 0f;
                    for (int l = 0; l < nl; l++) { float v = Mathf.Max(0f, Mathf.Lerp(b[l], t[l], ow)); cur[iz, ix, l] = v; s += v; }
                    if (s > 1e-5f) for (int l = 0; l < nl; l++) cur[iz, ix, l] /= s;
                    n++;
                }
            TD.SetAlphamaps(r.x0, r.z0, cur);
            return n;
        }

        public delegate void DetailFn(Vector3 world, float ownW, int[] baseline, int[] target);
        /// <summary>details on the given layers: target starts as the baseline counts (0 for layers the backup does not have), result = round(lerp by OwnWeight)</summary>
        public static int EditDetails(Zone z, TerrainData baseline, int[] layers, DetailFn fn, out long before, out long after)
        {
            before = after = 0;
            int res = TD.detailResolution; var r = RectFor(z, res, TD.size.x, TD.size.z);
            int nl = layers.Length;
            var cur = new int[nl][,]; var bas = new int[nl][,];
            bool baseOk = baseline && baseline.detailResolution == res;
            if (!baseOk) W("detail baseline missing or different resolution: the current details are the baseline");
            for (int k = 0; k < nl; k++)
            {
                cur[k] = TD.GetDetailLayer(r.x0, r.z0, r.w, r.h, layers[k]);
                bas[k] = baseOk && layers[k] < baseline.detailPrototypes.Length ? baseline.GetDetailLayer(r.x0, r.z0, r.w, r.h, layers[k]) : (baseOk ? new int[r.h, r.w] : (int[,])cur[k].Clone());
            }
            var b = new int[nl]; var t = new int[nl]; int n = 0; var tp = TPos;
            for (int iz = 0; iz < r.h; iz++)
                for (int ix = 0; ix < r.w; ix++)
                {
                    var w = new Vector3(tp.x + (r.x0 + ix + 0.5f) / res * TD.size.x, 0f, tp.z + (r.z0 + iz + 0.5f) / res * TD.size.z);
                    float ow = OwnWeight(z, w); if (ow <= 0f) continue;
                    w.y = GroundY(w);
                    for (int k = 0; k < nl; k++) { b[k] = bas[k][iz, ix]; t[k] = b[k]; before += cur[k][iz, ix]; }
                    fn(w, ow, b, t);
                    float rnd = Rand01(r.x0 + ix + 7777, r.z0 + iz);
                    for (int k = 0; k < nl; k++)
                    {
                        float v = Mathf.Lerp(b[k], t[k], ow); int iv = (int)v; if (rnd < v - iv) iv++;
                        cur[k][iz, ix] = Mathf.Clamp(iv, 0, 255); after += cur[k][iz, ix];
                    }
                    n++;
                }
            for (int k = 0; k < nl; k++) TD.SetDetailLayer(r.x0, r.z0, layers[k], cur[k]);
            return n;
        }

        public delegate float HeightFn(Vector3 world, float ownW, float baselineY);
        /// <summary>heights (world y): result = lerp(baseline, fn, OwnWeight). Re-snap your objects afterwards.</summary>
        public static int EditHeights(Zone z, TerrainData baseline, HeightFn fn)
        {
            int res = TD.heightmapResolution; var r = RectFor(z, res - 1, TD.size.x, TD.size.z);
            r.w = Mathf.Min(r.w + 1, res - r.x0); r.h = Mathf.Min(r.h + 1, res - r.z0);
            var cur = TD.GetHeights(r.x0, r.z0, r.w, r.h);
            var bas = baseline && baseline.heightmapResolution == res ? baseline.GetHeights(r.x0, r.z0, r.w, r.h) : (float[,])cur.Clone();
            int n = 0; var tp = TPos; float sy = TD.size.y;
            for (int iz = 0; iz < r.h; iz++)
                for (int ix = 0; ix < r.w; ix++)
                {
                    var w = new Vector3(tp.x + (float)(r.x0 + ix) / (res - 1) * TD.size.x, 0f, tp.z + (float)(r.z0 + iz) / (res - 1) * TD.size.z);
                    float ow = OwnWeight(z, w); if (ow <= 0f) continue;
                    float by = bas[iz, ix] * sy + tp.y;
                    float y = Mathf.Lerp(by, fn(w, ow, by), ow);
                    cur[iz, ix] = Mathf.Clamp01((y - tp.y) / sy); n++;
                }
            TD.SetHeights(r.x0, r.z0, cur);
            return n;
        }

        /// <summary>index of the first detail prototype whose prefab (or texture) has this name, -1 if none</summary>
        public static int DetailLayer(string name)
        {
            var d = TD.detailPrototypes;
            for (int i = 0; i < d.Length; i++) { var o = d[i].usePrototypeMesh ? (UnityEngine.Object)d[i].prototype : d[i].prototypeTexture; if (o && o.name == name) return i; }
            return -1;
        }
        /// <summary>find (by prefab) or append a mesh detail prototype, then apply the settings; returns its index</summary>
        public static int AddDetailProto(GameObject prefab, float minW, float maxW, float minH, float maxH, float noise = 0.35f, float align = 0.2f, Color? healthy = null, Color? dry = null)
        {
            if (!prefab) return -1;
            var protos = TD.detailPrototypes.ToList();
            int i = protos.FindIndex(p => p.usePrototypeMesh && p.prototype == prefab);
            var dp = i >= 0 ? protos[i] : new DetailPrototype();
            dp.prototype = prefab; dp.usePrototypeMesh = true; dp.renderMode = DetailRenderMode.VertexLit; dp.useInstancing = true;
            dp.minWidth = minW; dp.maxWidth = maxW; dp.minHeight = minH; dp.maxHeight = maxH; dp.noiseSpread = noise; dp.alignToGround = align; dp.positionJitter = 1f;
            dp.healthyColor = healthy ?? Color.white; dp.dryColor = dry ?? new Color(0.9f, 0.86f, 0.7f);
            if (i < 0) { protos.Add(dp); i = protos.Count - 1; } else protos[i] = dp;
            TD.detailPrototypes = protos.ToArray();
            return i;
        }
        /// <summary>index of the tree prototype with this prefab name (add = append the prefab found by <see cref="Prefab"/>), -1 if none</summary>
        public static int TreeProto(string prefabName, bool add = false)
        {
            var protos = TD.treePrototypes;
            for (int i = 0; i < protos.Length; i++) if (protos[i].prefab && protos[i].prefab.name == prefabName) return i;
            if (!add) return -1;
            var pf = Prefab(prefabName); if (!pf) return -1;
            var l = protos.ToList(); l.Add(new TreePrototype { prefab = pf, bendFactor = 0f }); TD.treePrototypes = l.ToArray();
            return l.Count - 1;
        }
        public static Vector3 TreeWorld(TreeInstance t) => Vector3.Scale(t.position, TD.size) + TPos;
        public static TreeInstance MakeTree(int proto, Vector3 world, float yawRad, float width, float height)
        {
            var tp = TPos; var s = TD.size;
            return new TreeInstance
            {
                prototypeIndex = proto, position = new Vector3((world.x - tp.x) / s.x, (GroundY(world) - tp.y) / s.y, (world.z - tp.z) / s.z),
                rotation = yawRad, widthScale = width, heightScale = height, color = Color.white, lightmapColor = Color.white
            };
        }

        [Serializable] class TreeSlot { public int i, proto; public float x, y, z; }
        [Serializable] class TreeRecord { public List<TreeSlot> slots = new List<TreeSlot>(); }
        /// <summary>the agent's own terrain trees for this key (e.g. "EA_valley"): recorded slots overwritten in place, extras appended, surplus removed only from the tail</summary>
        public static int SetOwnTrees(string key, List<TreeInstance> mine)
        {
            string path = Path.Combine(TreeRecordDir, key + "_trees.json");
            var rec = File.Exists(path) ? JsonUtility.FromJson<TreeRecord>(File.ReadAllText(path)) : new TreeRecord();
            var all = TD.treeInstances.ToList();
            var valid = new List<int>();
            foreach (var s in rec.slots)
                if (s.i >= 0 && s.i < all.Count && all[s.i].prototypeIndex == s.proto && (all[s.i].position - new Vector3(s.x, s.y, s.z)).sqrMagnitude < 1e-8f) valid.Add(s.i);
            if (valid.Count < rec.slots.Count) W($"{key}: {rec.slots.Count - valid.Count} recorded tree slots no longer match (changed by someone else): left alone");
            valid.Sort();
            int k = 0; var used = new List<int>();
            for (; k < mine.Count && k < valid.Count; k++) { all[valid[k]] = mine[k]; used.Add(valid[k]); }
            var surplus = valid.Skip(k).ToList();
            // surplus slots: remove from the tail only (indices of everyone else stay put); others become a copy of a new tree
            surplus.Sort(); int removed = 0;
            for (int j = surplus.Count - 1; j >= 0; j--) if (surplus[j] == all.Count - 1) { all.RemoveAt(all.Count - 1); removed++; surplus.RemoveAt(j); } else break;
            foreach (var s in surplus) { var t = all[s]; t.widthScale = t.heightScale = 0.0001f; all[s] = t; used.Add(s); }   // parked tiny (cannot move others)
            for (; k < mine.Count; k++) { all.Add(mine[k]); used.Add(all.Count - 1); }
            TD.SetTreeInstances(all.ToArray(), true);
            var outRec = new TreeRecord();
            var fresh = TD.treeInstances;
            foreach (var i in used.Distinct().OrderBy(x => x)) if (i < fresh.Length) outRec.slots.Add(new TreeSlot { i = i, proto = fresh[i].prototypeIndex, x = fresh[i].position.x, y = fresh[i].position.y, z = fresh[i].position.z });
            Directory.CreateDirectory(TreeRecordDir); File.WriteAllText(path, JsonUtility.ToJson(outRec, true));
            L($"{key}: {mine.Count} own terrain trees ({Mathf.Min(mine.Count, valid.Count)} in recorded slots, {Mathf.Max(0, mine.Count - valid.Count)} appended, {removed} surplus removed from the tail, {surplus.Count} surplus parked), total terrain trees {fresh.Length}");
            return mine.Count;
        }
        /// <summary>indices of the agent's recorded tree slots (to leave them out of "existing trees" spacing tests)</summary>
        public static HashSet<int> OwnTreeSlots(string key)
        {
            var h = new HashSet<int>(); string path = Path.Combine(TreeRecordDir, key + "_trees.json");
            if (File.Exists(path)) foreach (var s in JsonUtility.FromJson<TreeRecord>(File.ReadAllText(path)).slots) h.Add(s.i);
            return h;
        }
        /// <summary>every terrain tree except the given slots, on a 5 m grid (spacing tests)</summary>
        public static Dictionary<long, List<Vector3>> TreeGrid(HashSet<int> except)
        {
            var g = new Dictionary<long, List<Vector3>>(); var all = TD.treeInstances;
            for (int i = 0; i < all.Length; i++) if (except == null || !except.Contains(i)) Add(g, TreeWorld(all[i]), 5f);
            return g;
        }
        /// <summary>splat weights of a zone rect read once (current terrain, or a baseline TerrainData)</summary>
        public sealed class SplatSampler
        {
            readonly float[,,] a; readonly TexRect r; readonly int res, nl;
            public SplatSampler(Zone z, TerrainData src = null)
            {
                src = src ? src : TD; res = src.alphamapResolution; nl = src.alphamapLayers;
                r = RectFor(z, res, src.size.x, src.size.z); a = src.GetAlphamaps(r.x0, r.z0, r.w, r.h);
            }
            public float Get(Vector3 p, int layer)
            {
                if (layer < 0 || layer >= nl) return 0f; var tp = TPos;
                int x = Mathf.FloorToInt((p.x - tp.x) / TD.size.x * res) - r.x0, z = Mathf.FloorToInt((p.z - tp.z) / TD.size.z * res) - r.z0;
                if (x < 0 || z < 0 || x >= r.w || z >= r.h) return 0f;
                return a[z, x, layer];
            }
        }
        /// <summary>world positions of the agent's recorded trees (for checks)</summary>
        public static List<Vector3> OwnTreePositions(string key)
        {
            var l = new List<Vector3>(); string path = Path.Combine(TreeRecordDir, key + "_trees.json");
            if (!File.Exists(path) || !TD) return l;
            var rec = JsonUtility.FromJson<TreeRecord>(File.ReadAllText(path)); var all = TD.treeInstances;
            foreach (var s in rec.slots) if (s.i < all.Length && all[s.i].widthScale > 0.001f) l.Add(TreeWorld(all[s.i]));
            return l;
        }

        // ================================================================== groups / prefabs
        public static Transform ZoneRoot(Zone z) => SceneRoots.Find(z.GroupPath, true);
        public static Transform Group(Zone z, string sub) => string.IsNullOrEmpty(sub) ? ZoneRoot(z) : SceneRoots.Find(z.GroupPath + "/" + sub, true);
        public static void ClearGroup(Transform t) { if (t) for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(t.GetChild(i).gameObject); }
        public static Transform Marker(Transform parent, string name, Vector3 pos, Quaternion rot)
        {
            var t = parent.Find(name); if (!t) { t = new GameObject(name).transform; t.SetParent(parent, false); }
            t.SetPositionAndRotation(pos, rot); return t;
        }
        static readonly Dictionary<string, GameObject> _pfCache = new Dictionary<string, GameObject>();
        public static GameObject Prefab(string name, bool warn = true)
        {
            if (_pfCache.TryGetValue(name, out var c) && c) return c;
            foreach (var dir in new[] { PrefabP2, PrefabZones, PrefabPC, PrefabEnv })
            {
                var g = AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}/{name}.prefab");
                if (g) { _pfCache[name] = g; return g; }
            }
            if (warn) W("prefab not found: " + name);
            return null;
        }
        public static bool HasPrefab(string name) => Prefab(name, false) != null;

        // ================================================================== placement
        /// <summary>blocker positions, radius in y, on a 4 m grid</summary>
        public static readonly Dictionary<long, List<Vector3>> Blockers = new Dictionary<long, List<Vector3>>();
        static long Key(float x, float z, float cell) => ((long)Mathf.FloorToInt(x / cell) << 32) ^ (uint)Mathf.FloorToInt(z / cell);
        public static void Add(Dictionary<long, List<Vector3>> g, Vector3 p, float cell = 8f) { long k = Key(p.x, p.z, cell); if (!g.TryGetValue(k, out var l)) g[k] = l = new List<Vector3>(); l.Add(p); }
        public static bool Near(Dictionary<long, List<Vector3>> g, Vector3 p, float r, float cell = 8f)
        {
            int cx = Mathf.FloorToInt(p.x / cell), cz = Mathf.FloorToInt(p.z / cell), n = Mathf.CeilToInt(r / cell);
            for (int dx = -n; dx <= n; dx++) for (int dz = -n; dz <= n; dz++)
                if (g.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out var l))
                    foreach (var q in l) { float ex = q.x - p.x, ez = q.z - p.z; if (ex * ex + ez * ez < r * r) return true; }
            return false;
        }
        /// <summary>every active gameplay object: interactables (resource nodes 2.2 m), ZONE_* markers, story props, footprints, player</summary>
        public static int BuildBlockers(Scene scene)
        {
            Blockers.Clear(); int n = 0;
            void B(Vector3 p, float r) { Add(Blockers, new Vector3(p.x, r, p.z), 4f); n++; }
            foreach (var root in scene.GetRootGameObjects())
                foreach (var it in root.GetComponentsInChildren<Interactable>(false)) B(it.transform.position, it is ResourceNode ? 2.2f : 1.5f);
            foreach (var z in new[] { ("ZONE_Camp", 17f), ("ZONE_PlayerSpawn", 20f), ("ZONE_Shipwreck", 24f), ("ZONE_Cave", 14f) })
            { var zt = GameObject.Find(z.Item1); if (zt) B(zt.transform.position, z.Item2); }
            foreach (var path in new[] { SceneRoots.Interactables + "/Storytelling", SceneRoots.Interactables + "/GiantFootprints", SceneRoots.Interactables + "/Climbables", SceneRoots.Structures })
            { var t = SceneRoots.Find(path); if (t) foreach (var r in t.GetComponentsInChildren<Renderer>(false)) B(r.bounds.center, Mathf.Clamp(r.bounds.extents.magnitude * 0.6f, 1f, 4f)); }
            var pl = SceneRoots.Find(SceneRoots.Player); if (pl) foreach (Transform c in pl) { B(c.position, 6f); break; }
            return n;
        }
        public static bool IsBlocked(Vector3 p, float extra)
        {
            int cx = Mathf.FloorToInt(p.x / 4f), cz = Mathf.FloorToInt(p.z / 4f);
            for (int dx = -7; dx <= 7; dx++) for (int dz = -7; dz <= 7; dz++)
                if (Blockers.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out var l))
                    foreach (var q in l) { float r = q.y + extra, ex = q.x - p.x, ez = q.z - p.z; if (ex * ex + ez * ez < r * r) return true; }
            return false;
        }
        /// <summary>a non-terrain, non-trigger collider within r of p (ground level), not under ignoreRoot</summary>
        public static bool SolidAt(Vector3 p, float r, Transform ignoreRoot = null)
        {
            var c = new Vector3(p.x, GroundY(p) + r * 0.5f + 0.2f, p.z);
            foreach (var col in Physics.OverlapSphere(c, r, ~0, QueryTriggerInteraction.Ignore))
                if (!(col is TerrainCollider) && (!ignoreRoot || !col.transform.IsChildOf(ignoreRoot))) return true;
            return false;
        }
        /// <summary>the xz bounds (renderers + colliders, grown by extra) of a placed object touch a blocker circle: long props (trunks) must not reach a node</summary>
        public static bool FootprintBlocked(GameObject go, float extra)
        {
            Bounds b = default; bool has = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>()) { if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds); }
            if (!has) { Physics.SyncTransforms(); foreach (var c in go.GetComponentsInChildren<Collider>()) { if (!has) { b = c.bounds; has = true; } else b.Encapsulate(c.bounds); } }   // collider bounds are only valid after a sync
            if (!has) return false;
            float x0 = b.min.x - extra, x1 = b.max.x + extra, z0 = b.min.z - extra, z1 = b.max.z + extra;
            int cx0 = Mathf.FloorToInt(x0 / 4f) - 6, cx1 = Mathf.FloorToInt(x1 / 4f) + 6, cz0 = Mathf.FloorToInt(z0 / 4f) - 6, cz1 = Mathf.FloorToInt(z1 / 4f) + 6;
            for (int cx = cx0; cx <= cx1; cx++) for (int cz = cz0; cz <= cz1; cz++)
                if (Blockers.TryGetValue(((long)cx << 32) ^ (uint)cz, out var l))
                    foreach (var q in l)
                    {
                        float px = Mathf.Clamp(q.x, x0, x1) - q.x, pz = Mathf.Clamp(q.z, z0, z1) - q.z;
                        if (px * px + pz * pz < q.y * q.y) return true;
                    }
            return false;
        }
        public static GameObject Place(Transform parent, GameObject prefab, Vector3 pos, float yawDeg, float scale, bool alignToGround = false, float sink = 0f, StaticEditorFlags flags = StaticEditorFlags.OccludeeStatic)
        {
            if (!prefab) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            var rot = Quaternion.Euler(0f, yawDeg, 0f);
            if (alignToGround) rot = Quaternion.FromToRotation(Vector3.up, Normal(pos)) * rot;
            go.transform.SetPositionAndRotation(new Vector3(pos.x, GroundY(pos) - sink * scale, pos.z), rot);
            go.transform.localScale = Vector3.one * scale;
            GameObjectUtility.SetStaticEditorFlags(go, flags);
            return go;
        }

        // ================================================================== check
        /// <summary>read-only zone check; returns the number of problems (covered nodes + missing refs)</summary>
        public static int CheckZone(Zone z, IList<Transform> roots, string treeKey, Scene scene)
        {
            roots = roots.Where(r => r).ToList();
            var count = new SortedDictionary<string, int>(); int objs = 0;
            int missPrefab = 0, missMesh = 0, missMat = 0, badShader = 0, missScript = 0, missLod = 0, missCol = 0;
            var first = new List<string>();
            void Miss(ref int k, Transform t, string what) { k++; if (first.Count < 12) first.Add(what + ": " + PathOf(t)); }
            foreach (var root in roots)
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    objs++; var go = t.gameObject;
                    if (PrefabUtility.IsOutermostPrefabInstanceRoot(go))
                    {
                        var src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(go);
                        string n = src ? src.name : "(missing prefab)"; count.TryGetValue(n, out var v); count[n] = v + 1;
                    }
                    if (PrefabUtility.IsPartOfPrefabInstance(go) && PrefabUtility.GetPrefabInstanceStatus(go) == PrefabInstanceStatus.MissingAsset) Miss(ref missPrefab, t, "missing prefab");
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go) > 0) Miss(ref missScript, t, "missing script");
                    var mf = go.GetComponent<MeshFilter>(); if (mf && !mf.sharedMesh) Miss(ref missMesh, t, "missing mesh");
                    var mc = go.GetComponent<MeshCollider>(); if (mc && !mc.sharedMesh) Miss(ref missCol, t, "collider without mesh");
                    foreach (var r in go.GetComponents<Renderer>())
                    {
                        if (r is ParticleSystemRenderer) continue;
                        foreach (var m in r.sharedMaterials)
                        { if (!m) { Miss(ref missMat, t, "missing material"); continue; } if (!m.shader || m.shader.name == "Hidden/InternalErrorShader" || ShaderUtil.ShaderHasError(m.shader)) Miss(ref badShader, t, "broken shader " + m.name); }
                    }
                    var lg = go.GetComponent<LODGroup>(); if (lg) foreach (var lod in lg.GetLODs()) foreach (var r in lod.renderers) if (!r) Miss(ref missLod, t, "LOD renderer missing");
                }
            L($"[{z.id}] objects under {string.Join(", ", roots.Select(r => PathOf(r)))}: {objs}");
            foreach (var kv in count) L($"  {kv.Key}: {kv.Value}");
            var trees = OwnTreePositions(treeKey);
            var perProto = new SortedDictionary<string, int>();
            if (TD) { var all = TD.treeInstances; string path = Path.Combine(TreeRecordDir, treeKey + "_trees.json");
                if (File.Exists(path)) foreach (var s in JsonUtility.FromJson<TreeRecord>(File.ReadAllText(path)).slots)
                    if (s.i < all.Length && all[s.i].widthScale > 0.001f) { var pf = TD.treePrototypes[all[s.i].prototypeIndex].prefab; string n = pf ? pf.name : "?"; perProto.TryGetValue(n, out var v); perProto[n] = v + 1; } }
            L($"  own terrain trees: {trees.Count} ({string.Join(", ", perProto.Select(kv => kv.Key + " " + kv.Value))})");
            int protoMiss = TD ? TD.treePrototypes.Count(p => !p.prefab) + TD.detailPrototypes.Count(p => p.usePrototypeMesh ? !p.prototype : !p.prototypeTexture) : 0;
            L($"  missing refs: prefab {missPrefab}, mesh {missMesh}, material {missMat}, broken shader {badShader}, script {missScript}, LOD renderer {missLod}, collider mesh {missCol}; terrain prototypes without asset {protoMiss}");
            foreach (var s in first) L("    " + s);
            // resource nodes
            Physics.SyncTransforms();
            var tg = new Dictionary<long, List<Vector3>>(); foreach (var p in trees) Add(tg, p, 4f);
            int nodes = 0, covered = 0, byTree = 0; var bad = new List<string>();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var n in root.GetComponentsInChildren<ResourceNode>(false))
                {
                    var p = n.transform.position; if (z.Weight(p) <= 0f) continue;
                    nodes++;
                    var hits = Physics.OverlapSphere(p + Vector3.up * 0.5f, 0.55f, ~0, QueryTriggerInteraction.Ignore)
                        .Where(col => roots.Any(r => col.transform.IsChildOf(r)) && !col.transform.IsChildOf(n.transform)).ToArray();
                    if (hits.Length > 0) { covered++; if (bad.Count < 12) bad.Add($"covered by {hits[0].name}: {PathOf(n.transform)} {V(p)}"); }
                    else if (Near(tg, p, 1.2f, 4f)) { byTree++; if (bad.Count < 12) bad.Add($"own terrain tree within 1.2 m: {PathOf(n.transform)} {V(p)}"); }
                }
            L($"  resource nodes in the zone + blend: {nodes}; covered by a zone collider {covered}, own terrain tree within 1.2 m {byTree}");
            foreach (var s in bad) L("    " + s);
            // gameplay interactables inside a zone collider
            int intCovered = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var it in root.GetComponentsInChildren<Interactable>(false))
                {
                    if (it is ResourceNode || z.Weight(it.transform.position) <= 0f) continue;
                    var hits = Physics.OverlapSphere(it.transform.position + Vector3.up * 0.4f, 0.4f, ~0, QueryTriggerInteraction.Ignore).Where(col => roots.Any(r => col.transform.IsChildOf(r)) && !col.transform.IsChildOf(it.transform)).ToArray();
                    if (hits.Length > 0) { intCovered++; L($"    interactable covered by {hits[0].name}: {PathOf(it.transform)}"); }
                }
            L($"  other interactables covered: {intCovered}");
            var spawn = GameObject.Find("ZONE_PlayerSpawn");
            if (spawn) { var sp = spawn.transform.position; int near = 0; foreach (var r in roots) foreach (Transform c in r.GetComponentsInChildren<Transform>(true)) if (c != r && (new Vector2(c.position.x - sp.x, c.position.z - sp.z)).magnitude < 20f) near++; L($"  objects within 20 m of the player spawn {V(sp)}: {near}"); }
            return covered + byTree + intCovered + missPrefab + missMesh + missMat + badShader + missScript + missLod + missCol;
        }

        // ================================================================== Survey (read only)
        [PrimalBridgeCommand]
        public static string Survey(string arg)
        {
            var a = Args(arg); var z = ZoneById(a.TryGetValue("zone", out var id) ? id : "valley") ?? Zones[0];
            Begin("EA", "Survey " + arg);
            if (!OpenIsland(out var scene, out _)) return End();
            if (!FindTerrain()) { W("no terrain"); return End(); }
            LoadFeat();
            L($"terrain {TD.name} size {V(TD.size)} at {V(TPos)}; heightmap {TD.heightmapResolution}, alphamap {TD.alphamapResolution} x {TD.alphamapLayers}, detail {TD.detailResolution} ({TD.detailResolutionPerPatch}/patch)");
            L($"  distances: detail {F(Ter.detailObjectDistance)} density {F(Ter.detailObjectDensity)}, tree {F(Ter.treeDistance)}, billboard {F(Ter.treeBillboardDistance)}, crossfade {F(Ter.treeCrossFadeLength)}, basemap {F(Ter.basemapDistance)}; wind grass speed {F(TD.wavingGrassSpeed)} strength {F(TD.wavingGrassStrength)} amount {F(TD.wavingGrassAmount)}");
            L("  layers: " + string.Join(", ", TD.terrainLayers.Select((l, i) => $"{i} {(l ? l.name : "null")}")));
            var r = RectFor(z, TD.detailResolution, TD.size.x, TD.size.z);
            var dps = TD.detailPrototypes;
            for (int l = 0; l < dps.Length; l++)
            {
                var d = dps[l]; long inZone = 0; var m = TD.GetDetailLayer(r.x0, r.z0, r.w, r.h, l);
                foreach (var v in m) inZone += v;
                string mesh = "", mat = "";
                if (d.usePrototypeMesh && d.prototype) { var mf = d.prototype.GetComponentInChildren<MeshFilter>(); var mr = d.prototype.GetComponentInChildren<MeshRenderer>(); if (mf && mf.sharedMesh) mesh = $" mesh {mf.sharedMesh.name} bounds {V(mf.sharedMesh.bounds.size)}"; if (mr && mr.sharedMaterial) mat = $" mat {mr.sharedMaterial.name} / {mr.sharedMaterial.shader.name}"; }
                L($"  detail {l}: {(d.usePrototypeMesh ? (d.prototype ? d.prototype.name : "null") : (d.prototypeTexture ? d.prototypeTexture.name + " (texture)" : "null"))} {d.renderMode} inst {d.useInstancing} w {F(d.minWidth)}-{F(d.maxWidth)} h {F(d.minHeight)}-{F(d.maxHeight)} {mesh}{mat}; in {z.id} rect {inZone}");
            }
            var tps = TD.treePrototypes; var cntAll = new int[tps.Length]; var cntZ = new int[tps.Length];
            foreach (var t in TD.treeInstances) { if (t.prototypeIndex < 0 || t.prototypeIndex >= tps.Length) continue; cntAll[t.prototypeIndex]++; if (z.Weight(TreeWorld(t)) > 0f) cntZ[t.prototypeIndex]++; }
            for (int i = 0; i < tps.Length; i++) L($"  tree {i}: {(tps[i].prefab ? tps[i].prefab.name : "null")} all {cntAll[i]}, in {z.id} {cntZ[i]}");
            L($"  tree instances total {TD.treeInstanceCount}");
            // splat share in the zone core
            var al = TD.GetAlphamaps(0, 0, TD.alphamapResolution, TD.alphamapResolution); int ar = TD.alphamapResolution; var share = new float[TD.alphamapLayers]; int cells = 0;
            for (int iz = 0; iz < ar; iz += 2) for (int ix = 0; ix < ar; ix += 2)
                {
                    var w = new Vector3(TPos.x + (ix + 0.5f) / ar * TD.size.x, 0, TPos.z + (iz + 0.5f) / ar * TD.size.z); if (z.Weight(w) < 1f) continue;
                    cells++; for (int l = 0; l < share.Length; l++) share[l] += al[iz, ix, l];
                }
            L($"  splat share in the {z.id} core: " + string.Join(", ", share.Select((s, i) => $"{(TD.terrainLayers[i] ? TD.terrainLayers[i].name : "?")} {F(100f * s / Mathf.Max(1, cells))}%")));
            // ring samples + grid
            var sb = new StringBuilder();
            for (float rr = 0.25f; rr <= 1.31f; rr += 0.25f)
            {
                sb.Clear(); sb.Append($"  ring {F(rr)}R:");
                for (int k = 0; k < 16; k++)
                {
                    float ang = k * 22.5f * Mathf.Deg2Rad; var p = z.centre + new Vector3(Mathf.Sin(ang), 0, Mathf.Cos(ang)) * z.radius * rr;
                    sb.Append($" [{k * 22.5f:0}] y{GroundY(p):0.0} s{Slope(p):0} w{WaterDist(p):0}");
                }
                L(sb.ToString());
            }
            // objects in the zone by group
            var grp = new SortedDictionary<string, int>();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(false))
                {
                    if (!PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject) && !t.GetComponent<Interactable>() && !t.GetComponent<EnvLocation>()) continue;
                    if (z.Weight(t.position) <= 0f) continue;
                    string g = t.parent ? PathOf(t.parent) : "(root)"; string key = g + " :: " + System.Text.RegularExpressions.Regex.Replace(t.name, @"[\s_]*(\(\d+\)|\d+)$", "");
                    grp.TryGetValue(key, out var v); grp[key] = v + 1;
                }
            foreach (var kv in grp) L($"  {kv.Key}: {kv.Value}");
            BuildBlockers(scene); L($"  blockers: {Blockers.Values.Sum(l => l.Count)}");
            if (a.ContainsKey("points"))
                foreach (var ps in a["points"].Split('|'))
                {
                    var c = ps.Split(','); if (c.Length < 2) continue;
                    var p = new Vector3(float.Parse(c[0], CultureInfo.InvariantCulture), 0, float.Parse(c[1], CultureInfo.InvariantCulture));
                    L($"  point {V(Ground(p))}: slope {F(Slope(p))}, water {F(WaterDist(p))}, route {F(RouteDist(p))}, blocked {IsBlocked(p, 0f)}, solid {SolidAt(p, 1.5f)}, own {F(OwnWeight(z, p))}");
                }
            return End();
        }
    }
}
