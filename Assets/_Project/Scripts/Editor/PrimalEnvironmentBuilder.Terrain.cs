using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PrimalFrontier.World;

namespace PrimalFrontier.EditorTools
{
    public static partial class PrimalEnvironmentBuilder
    {
        // v2 splat order (tools/env/terrain_post.py LAYERS); the first six are the original layers
        static readonly string[] LayerNames = { "Sand", "Grass", "ForestFloor", "Rock", "Mud", "SandWet", "Moss", "DarkSoil", "Ash" };
        static readonly float[] LayerTile = { 4f, 6f, 5f, 8f, 4f, 4f, 3.5f, 4.5f, 6f };

        [MenuItem("Primal Frontier/Environment/Terrain Pass (v2 heights, layers, trees, re-snap)", priority = 62)]
        static void MenuTerrain() { if (EditorUtility.DisplayDialog("Primal Frontier", "Apply the v2 terrain? TD_Island is backed up first (Art/Terrain/_Backup).", "Apply", "Cancel")) EditorUtility.DisplayDialog("Primal Frontier", TerrainPass(""), "OK"); }

        /// <summary>arg: "" (apply) | "dry" (report what would move, change nothing)</summary>
        [PrimalBridgeCommand]
        public static string TerrainPass(string arg)
        {
            Begin("TerrainPass " + arg);
            var a = Args(arg); bool dry = a.ContainsKey("dry");
            if (!IslandOpen(out var scene, out bool wasDirty)) return End("TerrainPass");
            var t = FindTerrain(); if (!t) { W("no terrain ENV_Island_Terrain: nothing changed"); return End("TerrainPass"); }
            var td = t.terrainData; int n = td.heightmapResolution;
            if (n != 1025 || Mathf.Abs(td.size.x - 640f) > 0.1f || Mathf.Abs(td.size.y - 80f) > 0.1f) { W($"unexpected terrain {n} {V(td.size)}: nothing changed"); return End("TerrainPass"); }
            string hFile = TerrainSrc + "/ENV_Island_Height_v2.bytes";
            if (!File.Exists(hFile) || new FileInfo(hFile).Length != n * n * 2) { W("missing or wrong-sized " + hFile + ": nothing changed"); return End("TerrainPass"); }
            var splat = new float[3][][,];
            for (int i = 0; i < 3; i++) { splat[i] = ReadMask($"{TerrainSrc}/ENV_Island_Splat_v2_{i}.png"); if (splat[i] == null) return End("TerrainPass"); }
            var veg = ReadMask(TerrainSrc + "/ENV_Island_VegMask_v2.png"); var veg2 = ReadMask(TerrainSrc + "/ENV_Island_VegMask2_v2.png");
            if (veg == null || veg2 == null) return End("TerrainPass");
            float[,] treeOk = veg[0], detKeep = veg[1], wetMask = veg2[3];

            // ---- backup (once): the v1 terrain stays the source for trees and details, so re-runs give the same result
            EnsureFolder(BackupDir);
            if (!File.Exists(BackupPath))
            {
                if (dry) L("dry: would back up " + AssetDatabase.GetAssetPath(td) + " -> " + BackupPath);
                else if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(td), BackupPath)) { W("backup failed: nothing changed"); return End("TerrainPass"); }
                else L("backup: " + AssetDatabase.GetAssetPath(td) + " -> " + BackupPath);
            }
            else L("backup exists: " + BackupPath);
            var src = Or(AssetDatabase.LoadAssetAtPath<TerrainData>(BackupPath), td);

            // ---- heights
            var oldH = td.GetHeights(0, 0, n, n);
            var bytes = File.ReadAllBytes(hFile);
            var newH = new float[n, n];
            double sum = 0; float mx = 0; int moved = 0;
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    int i = (z * n + x) * 2; float v = (bytes[i] | (bytes[i + 1] << 8)) / 65535f; newH[z, x] = v;
                    float d = Mathf.Abs(v - oldH[z, x]) * td.size.y; sum += d; mx = Mathf.Max(mx, d); if (d > 0.3f) moved++;
                }
            L($"heights: mean change {F((float)(sum / (n * n)))} m, max {F(mx)} m, {moved} texels moved more than 0.3 m ({F(moved * 0.39f)} m2)");
            if (dry) { ResnapReport(scene, oldH, newH, wetMask, true); return End("TerrainPass"); }
            td.SetHeights(0, 0, newH);

            // ---- layers (the six originals + Moss, DarkSoil, Ash)
            var layers = new TerrainLayer[LayerNames.Length];
            for (int i = 0; i < LayerNames.Length; i++)
            {
                string nm = LayerNames[i];
                var existing = Or(td.terrainLayers.FirstOrDefault(l => l && l.name == "TL_" + nm), AssetDatabase.LoadAssetAtPath<TerrainLayer>($"{OldTerrainDir}/TL_{nm}.terrainlayer"));
                if (existing) { layers[i] = existing; continue; }
                EnsureFolder(TerrainSrc);
                string p = $"{TerrainSrc}/TL_{nm}.terrainlayer";
                var tl = AssetDatabase.LoadAssetAtPath<TerrainLayer>(p);
                if (!tl) { tl = new TerrainLayer(); AssetDatabase.CreateAsset(tl, p); }
                tl.diffuseTexture = ImportTexture($"{TexDir}/T_{nm}_D.png", false, false);
                tl.normalMapTexture = ImportTexture($"{TexDir}/T_{nm}_N.png", true, true);
                tl.maskMapTexture = ImportTexture($"{TexDir}/T_{nm}_M.png", false, true);
                tl.tileSize = Vector2.one * LayerTile[i]; tl.normalScale = 1f;
                tl.smoothness = 0f; tl.metallic = 0f;
                EditorUtility.SetDirty(tl);
                layers[i] = tl;
                L($"layer TL_{nm}: {(tl.diffuseTexture ? "textures ok" : "TEXTURES MISSING")}, tile {F(LayerTile[i])} m");
            }
            if (layers.Any(l => !l || !l.diffuseTexture)) W("a terrain layer has no albedo texture: check Art/Environment/Textures");
            td.terrainLayers = layers;

            // ---- splat (1025 PNG -> 1024 alphamap, same rounding as the original PrimalWorldBuilder)
            int ar = td.alphamapResolution;
            var alpha = new float[ar, ar, LayerNames.Length];
            int sres = splat[0][0].GetLength(0);
            for (int z = 0; z < ar; z++)
                for (int x = 0; x < ar; x++)
                {
                    int sx = Mathf.RoundToInt(x / (float)(ar - 1) * (sres - 1)), sz = Mathf.RoundToInt(z / (float)(ar - 1) * (sres - 1));
                    float s = 0f;
                    for (int l = 0; l < LayerNames.Length; l++) { float v = splat[l / 4][l % 4][sz, sx]; alpha[z, x, l] = v; s += v; }
                    if (s < 1e-4f) { alpha[z, x, 1] = 1f; s = 1f; }
                    for (int l = 0; l < LayerNames.Length; l++) alpha[z, x, l] /= s;
                }
            td.SetAlphamaps(0, 0, alpha);
            L($"splat: {ar}x{ar}, {LayerNames.Length} layers, {td.alphamapTextureCount} alphamap textures");

            // ---- trees (from the v1 backup, same order and count: TreeHarvest / saves use the index)
            RelocateTrees(src, td, treeOk);
            // ---- details (from the v1 backup, thinned where water / rock / ash now are)
            ThinDetails(src, td, detKeep);
            t.Flush();
            EditorUtility.SetDirty(td);

            // ---- re-snap the objects ENV owns, report the rest
            ResnapReport(scene, oldH, newH, wetMask, false);
            AssetDatabase.SaveAssets();
            SaveScene(scene, wasDirty);
            return End("TerrainPass");
        }

        static void RelocateTrees(TerrainData src, TerrainData td, float[,] treeOk)
        {
            var trees = src.treeInstances;
            if (td.treeInstanceCount > trees.Length)
            {
                // prototypes / instances appended by the vegetation step stay (they come after the v1 ones)
                var extra = td.treeInstances.Skip(trees.Length).ToArray();
                trees = trees.Concat(extra).ToArray();
            }
            var size = td.size; var tp = _terrain.transform.position;
            Vector3 World(TreeInstance ti) => new Vector3(ti.position.x * size.x + tp.x, 0f, ti.position.z * size.z + tp.z);
            var grid = new Dictionary<long, List<Vector2>>();
            long Key(float x, float z) => ((long)Mathf.FloorToInt(x / 2.5f) << 32) ^ (uint)Mathf.FloorToInt(z / 2.5f);
            bool Crowded(Vector3 p, float r)
            {
                int cx = Mathf.FloorToInt(p.x / 2.5f), cz = Mathf.FloorToInt(p.z / 2.5f);
                for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
                    if (grid.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out var l))
                        foreach (var q in l) if ((q - new Vector2(p.x, p.z)).sqrMagnitude < r * r) return true;
                return false;
            }
            void Add(Vector3 p) { long k = Key(p.x, p.z); if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<Vector2>(); l.Add(new Vector2(p.x, p.z)); }
            var bad = new List<int>();
            for (int i = 0; i < trees.Length; i++) { var p = World(trees[i]); if (SampleMap(treeOk, p) >= 0.5f) Add(p); else bad.Add(i); }
            int movedN = 0, far = 0;
            float[] rings = { 3f, 4.5f, 6f, 8f, 10.5f, 13f, 16f, 20f, 25f, 31f, 38f, 46f };
            foreach (int i in bad)
            {
                var p = World(trees[i]); bool done = false;
                foreach (var r in rings)
                {
                    float a0 = Rand01(i, 7) * Mathf.PI * 2f;
                    for (int k = 0; k < 12 && !done; k++)
                    {
                        float ang = a0 + k * Mathf.PI * 2f / 12f;
                        var q = p + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
                        if (q.x < tp.x + 2 || q.z < tp.z + 2 || q.x > tp.x + size.x - 2 || q.z > tp.z + size.z - 2) continue;
                        if (SampleMap(treeOk, q) < 0.65f || Crowded(q, 2.2f) || GroundY(q) < 0.6f) continue;
                        var ti = trees[i]; ti.position = new Vector3((q.x - tp.x) / size.x, ti.position.y, (q.z - tp.z) / size.z); trees[i] = ti;
                        Add(q); done = true; movedN++; if (r > 20f) far++;
                    }
                    if (done) break;
                }
                if (!done) W($"tree {i} at {V(p)} found no free ground within 46 m: left in place");
            }
            td.SetTreeInstances(trees, true);
            L($"trees: {trees.Length} kept in order ({src.treeInstanceCount} from the v1 backup), {movedN} moved out of water / rock / open ground ({far} more than 20 m), all snapped to the new heights");
        }

        static void ThinDetails(TerrainData src, TerrainData td, float[,] keep)
        {
            int layersN = Mathf.Min(src.detailPrototypes.Length, td.detailPrototypes.Length);
            if (src.detailResolution != td.detailResolution) { W("detail resolution differs from the backup: details left as they are"); return; }
            int res = td.detailResolution; var size = td.size; var tp = _terrain.transform.position;
            for (int l = 0; l < layersN; l++)
            {
                var map = src.GetDetailLayer(0, 0, res, res, l);
                long before = 0, after = 0;
                for (int z = 0; z < res; z++)
                    for (int x = 0; x < res; x++)
                    {
                        int c = map[z, x]; if (c == 0) continue; before += c;
                        var w = new Vector3((x + 0.5f) / res * size.x + tp.x, 0f, (z + 0.5f) / res * size.z + tp.z);
                        float k = SampleMap(keep, w) * c;
                        int nc = (int)k; if (Rand01(x * 7919 + l, z) < k - nc) nc++;
                        map[z, x] = nc; after += nc;
                    }
                td.SetDetailLayer(0, 0, l, map);
                L($"details layer {l} ({(td.detailPrototypes[l].prototype ? td.detailPrototypes[l].prototype.name : "texture")}): {before} -> {after} instances");
            }
        }

        // ------------------------------------------------------------------ re-snap + report
        // HIER 2026-09-30: "Environment" (ENV's groups) listed group by group, since World/Environment now holds the whole layout
        static readonly string[] OwnedWorldGroups = { "Rocks", "Cliffs", "Shipwreck", "Props", "Vegetation", "Environment/Water", "Environment/Forest", "Environment/Rocks",
                                                      "Environment/WaterEdge", "Environment/Volcano/Basalt", "Environment/Volcano/Lava", "Environment/Storytelling", "Environment/WaterfallDressing" };

        static void ResnapReport(Scene scene, float[,] oldH, float[,] newH, float[,] wet, bool dry)
        {
            var world = Root(scene, "World", false); var markers = Root(scene, "Markers", false);
            var placed = new List<Transform>();
            if (world)
                foreach (var g in OwnedWorldGroups)
                {
                    var gt = PrimalFrontier.Core.SceneRoots.Legacy("World/" + g); if (!gt) continue;
                    foreach (var tr in gt.GetComponentsInChildren<Transform>(true))
                        if (tr != gt && IsPlacedObject(tr, gt)) placed.Add(tr);
                }
            int snapped = 0, relocated = 0; var skipped = new List<string>();
            foreach (var tr in placed)
            {
                var p = tr.position;
                float o = SampleHeights(oldH, p), nh = SampleHeights(newH, p), d = nh - o;
                bool inWater = SampleMap(wet, p) >= 0.5f;
                bool fixedPiece = IsUnder(tr, world, "Cliffs") || IsUnder(tr, world, "Shipwreck");
                if (inWater && !fixedPiece && Mathf.Abs(d) > 0.05f)
                {
                    if (FindDry(p, wet, newH, out var q))
                    {
                        float off = p.y - o;
                        if (!dry) tr.position = new Vector3(q.x, SampleHeights(newH, q) + off, q.z);
                        relocated++; continue;
                    }
                    skipped.Add(PathOf(tr));
                }
                if (Mathf.Abs(d) > 0.005f) { if (!dry) tr.position = new Vector3(p.x, p.y + d, p.z); snapped++; }
            }
            L($"{(dry ? "dry: would re-snap" : "re-snapped")} {snapped} ENV objects (World/{string.Join(", World/", OwnedWorldGroups)}), {relocated} moved out of the new water" + (skipped.Count > 0 ? $", {skipped.Count} left in water: {string.Join("; ", skipped.Take(8))}" : ""));
            // markers ENV owns
            int mk = 0;
            if (markers)
                foreach (var g in new[] { "Zones", "Water", "Migration" })
                {
                    var gt = markers.Find(g); if (!gt) continue;
                    foreach (Transform m in gt)
                    {
                        float d = SampleHeights(newH, m.position) - SampleHeights(oldH, m.position);
                        if (Mathf.Abs(d) > 0.005f) { if (!dry) m.position += Vector3.up * d; mk++; }
                    }
                }
            L($"markers re-snapped (Markers/Zones, Water, Migration): {mk}");
            // others: count what changed under them, they re-run their own builders
            var report = new List<string>();
            void Check(string label, IEnumerable<Transform> items)
            {
                int n = 0, water = 0; float worst = 0; string ex = null;
                foreach (var tr in items)
                {
                    var p = tr.position; float d = SampleHeights(newH, p) - SampleHeights(oldH, p); bool w = SampleMap(wet, p) >= 0.5f;
                    if (Mathf.Abs(d) > 0.25f || w) { n++; if (w) water++; if (Mathf.Abs(d) > Mathf.Abs(worst)) { worst = d; ex = tr.name; } }
                }
                report.Add($"{label}: {n} off by more than 0.25 m ({water} now in water){(ex != null ? $", worst {ex} {F(worst)} m" : "")}");
            }
            var res = PrimalFrontier.Core.SceneRoots.LegacyObject("[Resources]");
            if (res) Check("[Resources] (RES)", res.GetComponentsInChildren<ResourceNode>(true).Select(c => c.transform));
            var wres = PrimalFrontier.Core.SceneRoots.Legacy("World/Resources"); if (wres) Check("World/Resources (RES)", wres.Cast<Transform>());
            var gpKids = PrimalFrontier.Core.SceneRoots.LegacyChildren("[Gameplay]");   // HIER: the old [Gameplay] children, wherever they are now
            if (gpKids.Count > 0)
            {
                foreach (var c in gpKids)
                    Check($"[Gameplay]/{c.name}", c.childCount > 0 ? c.Cast<Transform>() : new[] { c });
            }
            if (markers) foreach (var g in new[] { "Habitats", "ResourceAreas" }) { var gt = markers.Find(g); if (gt) Check($"Markers/{g}", gt.Cast<Transform>()); }
            L("OTHER ROOTS (their owners re-run): " + string.Join(" | ", report));
        }

        static bool IsUnder(Transform t, Transform world, string group) { if (!world) return false; var g = PrimalFrontier.Core.SceneRoots.Legacy("World/" + group); return g && t.IsChildOf(g); }

        /// <summary>the objects a builder placed: outermost prefab instances, or plain leaves with a renderer / collider</summary>
        static bool IsPlacedObject(Transform tr, Transform groupRoot)
        {
            if (PrefabUtility.IsOutermostPrefabInstanceRoot(tr.gameObject)) return !HasPlacedAncestor(tr, groupRoot);
            if (PrefabUtility.IsPartOfPrefabInstance(tr.gameObject)) return false;
            bool leafLike = tr.GetComponent<Renderer>() || tr.GetComponent<Collider>() || tr.GetComponent<LODGroup>() || tr.GetComponent<ParticleSystem>();
            return leafLike && !HasPlacedAncestor(tr, groupRoot);
        }
        static bool HasPlacedAncestor(Transform tr, Transform groupRoot)
        {
            for (var p = tr.parent; p && p != groupRoot; p = p.parent)
            {
                if (PrefabUtility.IsOutermostPrefabInstanceRoot(p.gameObject)) return true;
                if (!PrefabUtility.IsPartOfPrefabInstance(p.gameObject) && (p.GetComponent<Renderer>() || p.GetComponent<LODGroup>())) return true;
            }
            return false;
        }

        static bool FindDry(Vector3 p, float[,] wet, float[,] newH, out Vector3 q)
        {
            float[] rings = { 2f, 3.5f, 5f, 7f, 9.5f, 12f, 15f, 19f };
            int seed = Mathf.RoundToInt(p.x * 13 + p.z * 7);
            foreach (var r in rings)
                for (int k = 0; k < 12; k++)
                {
                    float ang = Rand01(seed, k) * 0.3f + k * Mathf.PI / 6f;
                    q = p + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
                    if (SampleMap(wet, q) < 0.2f && Slope(q) < 35f) return true;
                }
            q = p; return false;
        }

        static string PathOf(Transform t) { var s = t.name; for (var p = t.parent; p; p = p.parent) s = p.name + "/" + s; return s; }

        // ------------------------------------------------------------------ Markers
        [PrimalBridgeCommand]
        public static string Markers(string arg)
        {
            Begin("Markers");
            if (!IslandOpen(out var scene, out bool wasDirty)) return End("Markers");
            if (!FindTerrain()) { W("no terrain"); return End("Markers"); }
            var f = LoadFeatures(); if (f == null) return End("Markers");
            var markers = Root(scene, "Markers");
            var zones = Child(markers, "Zones");
            int nz = 0;
            foreach (var loc in f.locations)
            {
                var tr = zones.Find(loc.id); if (!tr) { tr = new GameObject(loc.id).transform; tr.SetParent(zones, false); }
                var p = V3(loc.pos); tr.position = Ground(p); tr.rotation = Quaternion.identity;
                var el = Comp<EnvLocation>(tr.gameObject);
                el.id = loc.id; el.radius = loc.radius; el.note = loc.what;
                nz++;
            }
            var mig = Child(markers, "Migration");
            var pts = Pts(f.migration);
            for (int i = 0; i < pts.Count; i++)
            {
                string nm = $"Route_{i:00}";
                var tr = mig.Find(nm); if (!tr) { tr = new GameObject(nm).transform; tr.SetParent(mig, false); }
                tr.position = Ground(pts[i]);
                var next = pts[Mathf.Min(i + 1, pts.Count - 1)] - pts[Mathf.Max(i - 1, 0)]; next.y = 0f;
                tr.rotation = next.sqrMagnitude > 0.01f ? Quaternion.LookRotation(next.normalized) : Quaternion.identity;
            }
            for (int i = mig.childCount - 1; i >= 0; i--) { var c = mig.GetChild(i); if (c.name.StartsWith("Route_") && int.TryParse(c.name.Substring(6), out int k) && k >= pts.Count) UnityEngine.Object.DestroyImmediate(c.gameObject); }
            L($"Markers/Zones: {nz} locations (EnvLocation id / radius / note); Markers/Migration: Route_00..Route_{pts.Count - 1:00}");
            float maxSlope = 0f; string where = "";
            for (int i = 0; i + 1 < pts.Count; i++)
                for (int s = 0; s <= 20; s++)
                {
                    var p = Vector3.Lerp(pts[i], pts[i + 1], s / 20f); float sl = Slope(p);
                    if (sl > maxSlope) { maxSlope = sl; where = $"Route_{i:00}-{i + 1:00}"; }
                }
            L($"migration route: steepest ground {F(maxSlope)} deg ({where})");
            SaveScene(scene, wasDirty);
            return End("Markers");
        }
    }
}
