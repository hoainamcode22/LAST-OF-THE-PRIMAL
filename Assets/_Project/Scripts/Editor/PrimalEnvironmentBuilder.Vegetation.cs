using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using PrimalFrontier.World;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// Prehistoric vegetation (directive 2, 54): tree ferns and cycads as terrain trees (appended after the v1 trees, so
    /// TreeHarvest indices and saves stay valid), horsetails / reeds / giant ferns as instanced terrain details, the grass
    /// detail thinned into a fern prairie and the generic bush detail halved, and placed prefabs under World/Environment
    /// (fallen trunks, big roots at old araucarias, moss rocks, magnolia-like shrubs, riverside giant ferns and horsetail
    /// clumps, hanging moss / climbing-fern curtains on the canyon and waterfall walls, basalt on the volcanic ridge).
    /// Deterministic and idempotent: ENV's own trees / details / objects are rebuilt each run from the v1 backup and the
    /// terrain masks; interactive bushes, thickets, resource nodes and all gameplay objects are only avoided, never moved.
    /// Run after TerrainPass (TerrainPass resets the details to the v1 backup).
    /// </summary>
    public static partial class PrimalEnvironmentBuilder
    {
        static readonly string[] TreeProtoNames = { "ENV_PC_TreeFern_A", "ENV_PC_TreeFern_B", "ENV_PC_TreeFern_C", "ENV_PC_Cycad_A", "ENV_PC_Cycad_B", "ENV_PC_Cycad_C" };

        // ------------------------------------------------------------------ context shared by the placement steps
        class Ctx
        {
            public TerrainData td; public Vector3 tp, size; public float[,,] alpha; public int ar;
            public float[][,] veg, veg2;
            public List<Vector3> water = new List<Vector3>(); public Dictionary<long, List<Vector3>> waterGrid = new Dictionary<long, List<Vector3>>();
            public Dictionary<long, List<Vector3>> block = new Dictionary<long, List<Vector3>>();
            public Dictionary<long, List<Vector3>> placed = new Dictionary<long, List<Vector3>>();
            public List<Vector3> route = new List<Vector3>();
            public Dictionary<string, EnvLocation> loc = new Dictionary<string, EnvLocation>();
        }
        static long GKey(float x, float z, float cell) => ((long)Mathf.FloorToInt(x / cell) << 32) ^ (uint)Mathf.FloorToInt(z / cell);
        static void GAdd(Dictionary<long, List<Vector3>> g, Vector3 p, float cell) { long k = GKey(p.x, p.z, cell); if (!g.TryGetValue(k, out var l)) g[k] = l = new List<Vector3>(); l.Add(p); }
        static bool GNear(Dictionary<long, List<Vector3>> g, Vector3 p, float r, float cell)
        {
            int cx = Mathf.FloorToInt(p.x / cell), cz = Mathf.FloorToInt(p.z / cell), n = Mathf.CeilToInt(r / cell);
            for (int dx = -n; dx <= n; dx++) for (int dz = -n; dz <= n; dz++)
                if (g.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out var l))
                    foreach (var q in l) { float ex = q.x - p.x, ez = q.z - p.z; if (ex * ex + ez * ez < r * r) return true; }
            return false;
        }

        static Ctx _c;

        static float Layer(Ctx c, Vector3 p, int layer)
        {
            if (c.alpha == null || layer >= c.alpha.GetLength(2)) return 0f;
            int x = Mathf.Clamp(Mathf.RoundToInt((p.x - c.tp.x) / c.size.x * (c.ar - 1)), 0, c.ar - 1), z = Mathf.Clamp(Mathf.RoundToInt((p.z - c.tp.z) / c.size.z * (c.ar - 1)), 0, c.ar - 1);
            return c.alpha[z, x, layer];
        }
        static float Forest(Ctx c, Vector3 p) => Mathf.Clamp01(Layer(c, p, 2) + Layer(c, p, 6) * 0.8f);
        static float WaterDist(Ctx c, Vector3 p)
        {
            float best = 200f; int cx = Mathf.FloorToInt(p.x / 20f), cz = Mathf.FloorToInt(p.z / 20f);
            for (int dx = -3; dx <= 3; dx++) for (int dz = -3; dz <= 3; dz++)
                if (c.waterGrid.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out var l))
                    foreach (var q in l) { float d = new Vector2(q.x - p.x, q.z - p.z).magnitude - q.y; if (d < best) best = d; }
            return Mathf.Max(0f, best);
        }
        static float RouteDist(Ctx c, Vector3 p)
        {
            float best = 1e9f;
            for (int i = 0; i + 1 < c.route.Count; i++)
            {
                Vector2 a = new Vector2(c.route[i].x, c.route[i].z), b = new Vector2(c.route[i + 1].x, c.route[i + 1].z), q = new Vector2(p.x, p.z);
                var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                best = Mathf.Min(best, (a + ab * t - q).magnitude);
            }
            return best;
        }
        static float LocFactor(Ctx c, string id, Vector3 p) { if (!c.loc.TryGetValue(id, out var l)) return 0f; var d = p - l.transform.position; d.y = 0; return Mathf.Clamp01(1f - d.magnitude / l.radius); }

        static Ctx BuildCtx(UnityEngine.SceneManagement.Scene scene)
        {
            var c = new Ctx { td = _terrain.terrainData, tp = _terrain.transform.position };
            c.size = c.td.size; c.ar = c.td.alphamapResolution;
            c.alpha = c.td.GetAlphamaps(0, 0, c.ar, c.ar);
            c.veg = ReadMask(TerrainSrc + "/ENV_Island_VegMask_v2.png"); c.veg2 = ReadMask(TerrainSrc + "/ENV_Island_VegMask2_v2.png");
            var f = _features;
            // water as (x, radius, z) samples, 20 m grid
            void Line(float[] pts, float[] hw, float fixedHw)
            {
                var P = Pts(pts);
                for (int i = 0; i < P.Count; i++) c.water.Add(new Vector3(P[i].x, hw != null && i < hw.Length ? hw[i] : fixedHw, P[i].z));
            }
            Line(f.water.riverA.points, f.water.riverA.halfWidths, 3f); Line(f.water.riverB.points, f.water.riverB.halfWidths, 3f); Line(f.water.brook.points, null, 1f);
            var pool = V3(f.water.waterfall.pool); c.water.Add(new Vector3(pool.x, f.water.waterfall.poolRadius, pool.z));
            var pond = V3(f.water.pond.center); c.water.Add(new Vector3(pond.x, 17f, pond.z));
            foreach (var b in f.water.wetland.blobs) { var bc = V3(b.center); c.water.Add(new Vector3(bc.x, b.radius * 0.7f, bc.z)); }
            foreach (var w in c.water) GAdd(c.waterGrid, w, 20f);
            c.route = Pts(f.migration);
            var zones = Root(scene, "Markers").Find("Zones");
            if (zones) foreach (var el in zones.GetComponentsInChildren<EnvLocation>()) c.loc[el.id] = el;
            // blockers: everything gameplay placed (never covered or moved by ENV decoration)
            void Block(Transform t, float r) { if (t) foreach (Transform ch in t) GAdd(c.block, new Vector3(ch.position.x, r, ch.position.z), 4f); }
            foreach (var n in ResourceNodeTransforms(scene)) GAdd(c.block, new Vector3(n.position.x, 2.2f, n.position.z), 4f);
            var world = Root(scene, "World");
            foreach (var g in new[] { "Vegetation/InteractiveBushes", "Rocks", "Resources", "Props", "Shipwreck" }) Block(PrimalFrontier.Core.SceneRoots.Legacy("World/" + g), 2.5f);
            var thickets = PrimalFrontier.Core.SceneRoots.Legacy("World/Vegetation/Thickets"); if (thickets) foreach (Transform th in thickets) Block(th, 2.5f);
            foreach (var k in PrimalFrontier.Core.SceneRoots.LegacyChildren("[Gameplay]"))   // HIER: the old [Gameplay] children and grandchildren, wherever they are now
            {
                if (!k.gameObject.activeInHierarchy) continue;
                GAdd(c.block, new Vector3(k.position.x, 5f, k.position.z), 4f);
                foreach (Transform t in k) if (t.gameObject.activeInHierarchy) GAdd(c.block, new Vector3(t.position.x, 5f, t.position.z), 4f);
            }
            foreach (var z in new[] { ("ZONE_Camp", 17f), ("ZONE_PlayerSpawn", 20f), ("ZONE_Shipwreck", 24f), ("ZONE_Cave", 14f) })
            { var zt = GameObject.Find(z.Item1); if (zt) GAdd(c.block, new Vector3(zt.transform.position.x, z.Item2, zt.transform.position.z), 4f); }
            return c;
        }

        static IEnumerable<Transform> ResourceNodeTransforms(UnityEngine.SceneManagement.Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var n in root.GetComponentsInChildren<ResourceNode>(true)) yield return n.transform;
        }

        /// <summary>blocker radius is stored in y</summary>
        static bool Blocked(Ctx c, Vector3 p, float extra)
        {
            int cx = Mathf.FloorToInt(p.x / 4f), cz = Mathf.FloorToInt(p.z / 4f);
            for (int dx = -6; dx <= 6; dx++) for (int dz = -6; dz <= 6; dz++)
                if (c.block.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out var l))
                    foreach (var q in l) { float r = q.y + extra; float ex = q.x - p.x, ez = q.z - p.z; if (ex * ex + ez * ez < r * r) return true; }
            return false;
        }

        // ------------------------------------------------------------------ Vegetation
        [PrimalBridgeCommand]
        public static string Vegetation(string arg)
        {
            Begin("Vegetation " + arg);
            if (!IslandOpen(out var scene, out bool wasDirty)) return End("Vegetation");
            if (!FindTerrain() || LoadFeatures() == null) { W("no terrain or features"); return End("Vegetation"); }
            EnvMaterials();
            // ---- prefabs
            var P = new Dictionary<string, GameObject>();
            void Pf(string n, float[] lods, ColKind col, bool low = false, float cr = 0.3f, float ch = 3f, bool shadows = true)
            {
                var sw = low ? new Dictionary<string, string> { { "M_Env_Foliage", "M_Env_FoliageLow" } } : null;
                var p = MakePrefab(n, lods, col, sw, shadows, cr, ch); if (p) P[n] = p;
            }
            foreach (var n in new[] { "ENV_PC_TreeFern_A", "ENV_PC_TreeFern_B", "ENV_PC_TreeFern_C" }) Pf(n, new[] { 0.28f, 0.09f, 0.012f }, ColKind.Capsule, cr: 0.28f, ch: 4f);
            foreach (var n in new[] { "ENV_PC_Cycad_A", "ENV_PC_Cycad_B", "ENV_PC_Cycad_C" }) Pf(n, new[] { 0.3f, 0.1f, 0.015f }, ColKind.Capsule, cr: 0.42f, ch: 1.4f);
            foreach (var n in new[] { "ENV_PC_GiantFern_A", "ENV_PC_GiantFern_B", "ENV_PC_HorsetailClump" }) Pf(n, new[] { 0.2f, 0.035f }, ColKind.None, true, shadows: false);
            foreach (var n in new[] { "ENV_PC_Magnolia_A", "ENV_PC_Magnolia_B" }) Pf(n, new[] { 0.3f, 0.1f, 0.02f }, ColKind.None);
            foreach (var n in new[] { "ENV_PC_VineCurtain_A", "ENV_PC_VineCurtain_B" }) Pf(n, new[] { 0.2f, 0.03f }, ColKind.None, true, shadows: false);
            foreach (var n in new[] { "ENV_PC_Roots_A", "ENV_PC_Roots_B", "ENV_PC_FallenTrunk_A", "ENV_PC_FallenTrunk_B" }) Pf(n, new[] { 0.3f, 0.09f, 0.015f }, ColKind.Mesh);
            foreach (var n in new[] { "ENV_PC_MossRock_A", "ENV_PC_MossRock_B", "ENV_PC_MossRock_C", "ENV_PC_Basalt_A", "ENV_PC_Basalt_B", "ENV_PC_BasaltBoulder" }) Pf(n, new[] { 0.3f, 0.1f, 0.012f }, ColKind.Mesh);
            var dets = new Dictionary<string, GameObject>();
            foreach (var n in new[] { "DET_PC_Horsetail", "DET_PC_Reeds", "DET_PC_GiantFern" }) { var d = MakeDetailPrefab(n, "M_Env_FoliageLow"); if (d) dets[n] = d; }
            L($"prefabs: {P.Count} (Prefabs/Environment/PC), detail prefabs: {dets.Count}");
            if (P.Count == 0) { W("no ENV models imported (Art/Environment/Models missing?): nothing placed"); AssetDatabase.SaveAssets(); return End("Vegetation"); }
            _c = BuildCtx(scene);
            var c = _c; var td = c.td;

            // ---- terrain trees: ENV prototypes appended, ENV instances rebuilt after the v1 ones
            var src = Or(AssetDatabase.LoadAssetAtPath<TerrainData>(BackupPath), td);
            int v1 = src.treeInstanceCount;
            var protos = td.treePrototypes.ToList();
            var idx = new Dictionary<string, int>();
            foreach (var n in TreeProtoNames)
            {
                if (!P.TryGetValue(n, out var pf)) continue;
                int i = protos.FindIndex(p => p.prefab == pf);
                if (i < 0) { protos.Add(new TreePrototype { prefab = pf, bendFactor = 0f }); i = protos.Count - 1; }
                idx[n] = i;
            }
            td.treePrototypes = protos.ToArray();
            var mine = new HashSet<int>(idx.Values);
            var keep = td.treeInstances.Take(v1).ToList();
            // v1 trees swapped in place to the new models (same index, position and scale: TreeHarvest and saves stay valid).
            // Decided from the v1 backup's prototype, so re-runs give the same result; arg "noconvert" puts the v1 models back.
            int toFern = 0, toCycad = 0; bool convert = !arg.Contains("noconvert");
            if (src != td)
            {
                var srcProtos = src.treePrototypes; var srcTrees = src.treeInstances;
                string SrcName(int pi) => pi >= 0 && pi < srcProtos.Length && srcProtos[pi].prefab ? srcProtos[pi].prefab.name : "";
                var fernsN = new[] { "ENV_PC_TreeFern_A", "ENV_PC_TreeFern_B", "ENV_PC_TreeFern_C" }.Where(idx.ContainsKey).Select(n => idx[n]).ToArray();
                var cycN = new[] { "ENV_PC_Cycad_A", "ENV_PC_Cycad_B", "ENV_PC_Cycad_C" }.Where(idx.ContainsKey).Select(n => idx[n]).ToArray();
                for (int i = 0; i < keep.Count && i < srcTrees.Length; i++)
                {
                    string was = SrcName(srcTrees[i].prototypeIndex);
                    var t = keep[i]; var p = Vector3.Scale(t.position, c.size) + c.tp;
                    float r = Rand01(i, 4242);
                    int to = -1;
                    if (!convert) { }
                    else if (was.EndsWith("Tree_02") && fernsN.Length > 0) to = fernsN[i % fernsN.Length];                 // old tree fern -> new
                    else if (was.EndsWith("Tree_04") && cycN.Length > 0) to = cycN[i % cycN.Length];                  // old cycad -> new
                    else if (was.EndsWith("Tree_03"))                                                                   // broadleaf: the modern-looking one
                    {
                        float wetK = Mathf.Clamp01(1f - WaterDist(c, p) / 40f), deepK = LocFactor(c, "deep_forest", p);
                        if (fernsN.Length > 0 && r < 0.3f + 0.35f * Mathf.Max(wetK, deepK)) to = fernsN[i % fernsN.Length];
                        else if (cycN.Length > 0 && r > 0.82f) to = cycN[i % cycN.Length];
                    }
                    if (to < 0)
                    {
                        if (mine.Contains(t.prototypeIndex)) { t.prototypeIndex = srcTrees[i].prototypeIndex; t.heightScale = srcTrees[i].heightScale; t.widthScale = srcTrees[i].widthScale; keep[i] = t; }
                        continue;
                    }
                    if (fernsN.Contains(to)) { toFern++; t.heightScale = Mathf.Clamp(t.heightScale, 0.8f, 1.3f); t.widthScale = Mathf.Clamp(t.widthScale, 0.8f, 1.3f); }
                    else { toCycad++; t.heightScale = Mathf.Clamp(t.heightScale, 0.75f, 1.3f); t.widthScale = Mathf.Clamp(t.widthScale, 0.75f, 1.3f); }
                    t.prototypeIndex = to; keep[i] = t;
                }
            }
            var extra = td.treeInstances.Skip(v1).Where(t => !mine.Contains(t.prototypeIndex)).ToList();       // other agents' appended trees stay
            var trees = new List<TreeInstance>(keep); trees.AddRange(extra);
            var treeGrid = new Dictionary<long, List<Vector3>>();
            foreach (var t in trees) GAdd(treeGrid, Vector3.Scale(t.position, c.size) + c.tp, 5f);
            int nf = 0, ncy = 0;
            var ferns = idx.Where(kv => kv.Key.Contains("TreeFern")).Select(kv => kv.Value).ToArray();
            var cycads = idx.Where(kv => kv.Key.Contains("Cycad")).Select(kv => kv.Value).ToArray();
            const float step = 3f;
            int gx = Mathf.FloorToInt(c.size.x / step), gz = Mathf.FloorToInt(c.size.z / step);
            for (int iz = 0; iz < gz; iz++)
                for (int ix = 0; ix < gx; ix++)
                {
                    float jx = Rand01(ix * 73 + 1, iz * 31 + 2), jz = Rand01(ix * 19 + 3, iz * 97 + 4);
                    var p = new Vector3(c.tp.x + (ix + jx) * step, 0, c.tp.z + (iz + jz) * step);
                    float ok = SampleMap(c.veg[0], p); if (ok < 0.6f) continue;
                    float y = GroundY(p); if (y < 1.4f) continue;
                    float slope = Slope(p); if (slope > 30f) continue;
                    float fo = Forest(c, p), dw = WaterDist(c, p), deep = LocFactor(c, "deep_forest", p);
                    float wetness = Mathf.Clamp01(1f - dw / 45f);
                    float meadowCore = LocFactor(c, "meadow", p);
                    float pFern = fo * (0.012f + 0.09f * wetness + 0.06f * deep) * (1f - meadowCore * 1.5f);
                    float edge = 4f * fo * (1f - fo);
                    float sand = Layer(c, p, 0);
                    float pCy = 0.03f * edge + 0.006f * Layer(c, p, 1) * (1f - meadowCore * 1.6f) + 0.02f * sand * (y > 2.2f ? 1 : 0) + 0.03f * SampleMap(c.veg2[2], p);
                    float r = Rand01(ix * 7 + 11, iz * 13 + 5);
                    int proto = -1; float scale = 1f;
                    if (ferns.Length > 0 && r < pFern) { proto = ferns[(ix + iz) % ferns.Length]; scale = 0.8f + Rand01(ix, iz * 3) * 0.5f; }
                    else if (cycads.Length > 0 && r < pFern + pCy) { proto = cycads[(ix * 3 + iz) % cycads.Length]; scale = 0.75f + Rand01(ix * 5, iz) * 0.6f; }
                    if (proto < 0) continue;
                    if (RouteDist(c, p) < 5f || GNear(treeGrid, p, 2.8f, 5f) || Blocked(c, p, 1.5f)) continue;
                    trees.Add(new TreeInstance
                    {
                        prototypeIndex = proto, position = new Vector3((p.x - c.tp.x) / c.size.x, (y - c.tp.y) / c.size.y, (p.z - c.tp.z) / c.size.z),
                        rotation = Rand01(ix * 3 + 7, iz * 5 + 1) * Mathf.PI * 2f, widthScale = scale, heightScale = scale * (0.9f + Rand01(iz, ix) * 0.2f),
                        color = Color.white, lightmapColor = Color.white
                    });
                    GAdd(treeGrid, p, 5f);
                    if (ferns.Contains(proto)) nf++; else ncy++;
                }
            td.SetTreeInstances(trees.ToArray(), true);
            L($"terrain trees: {v1} v1 kept in order ({toFern} swapped to the new tree ferns, {toCycad} to the new cycads: old tree ferns, old cycads, part of the broadleaf) + {extra.Count} other appended + ENV {nf} tree ferns and {ncy} cycads (prototypes {string.Join(", ", idx.Select(kv => kv.Key + "=" + kv.Value))})");

            // ---- terrain details
            Details(src, td, c, dets);

            // ---- placed prefabs
            var forest = EnvGroup(scene, "Forest"); ClearChildren(forest);
            var rocks = EnvGroup(scene, "Rocks"); ClearChildren(rocks);
            var waterEdge = EnvGroup(scene, "WaterEdge"); ClearChildren(waterEdge);
            var volc = EnvGroup(scene, "Volcano/Basalt"); ClearChildren(volc);
            c.placed.Clear();
            int Scatter(Transform parent, string[] names, int want, float spacing, System.Func<Vector3, float> density, float sink, bool alignToGround, float scaleMin, float scaleMax, int seed, float routeClear = 0f, float blockExtra = 1f, bool yawFromSlope = false)
            {
                int n = 0, tries = 0;
                var avail = names.Where(P.ContainsKey).ToArray(); if (avail.Length == 0) return 0;
                while (n < want && tries < want * 400)
                {
                    tries++;
                    var p = new Vector3(c.tp.x + Rand01(seed, tries * 2) * c.size.x, 0, c.tp.z + Rand01(seed + 1, tries * 2 + 1) * c.size.z);
                    float dns = density(p); if (dns <= 0f || Rand01(seed + 2, tries) > dns) continue;
                    if (GNear(c.placed, p, spacing, 8f) || Blocked(c, p, blockExtra) || GNear(treeGrid, p, 1.6f, 5f)) continue;
                    if (routeClear > 0 && RouteDist(c, p) < routeClear) continue;
                    var pf = avail[n % avail.Length];
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(P[pf], parent);
                    float yaw = Rand01(seed + 3, tries) * 360f;
                    var nrm = GroundNormal(p);
                    var rot = Quaternion.Euler(0, yaw, 0);
                    if (alignToGround) rot = Quaternion.FromToRotation(Vector3.up, nrm) * rot;
                    if (yawFromSlope) { var down = Vector3.ProjectOnPlane(-Vector3.up, nrm); if (down.sqrMagnitude > 1e-4f) rot = Quaternion.FromToRotation(Vector3.up, nrm) * Quaternion.LookRotation(Vector3.Cross(down.normalized, Vector3.up), Vector3.up); }
                    float s = Mathf.Lerp(scaleMin, scaleMax, Rand01(seed + 4, tries));
                    go.transform.SetPositionAndRotation(new Vector3(p.x, GroundY(p) - sink * s, p.z), rot);
                    go.transform.localScale = Vector3.one * s;
                    GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic);
                    GAdd(c.placed, p, 8f); n++;
                }
                return n;
            }
            var pool = V3(_features.water.waterfall.pool);
            int trunks = Scatter(forest, new[] { "ENV_PC_FallenTrunk_A", "ENV_PC_FallenTrunk_B" }, 30, 22f, p => Forest(c, p) > 0.55f && Slope(p) < 16f && SampleMap(c.veg[0], p) > 0.5f ? 0.6f : 0f, 0.12f, true, 0.85f, 1.15f, 501, routeClear: 7f, blockExtra: 4f, yawFromSlope: true);
            int mrock = Scatter(rocks, new[] { "ENV_PC_MossRock_A", "ENV_PC_MossRock_B", "ENV_PC_MossRock_C" }, 80, 9f, p => { float f = Forest(c, p), dw = WaterDist(c, p), sl = Slope(p); return SampleMap(c.veg2[3], p) > 0.3f ? 0f : f * (dw < 25f ? 0.8f : 0.15f) + (sl > 10f && sl < 30f ? 0.2f : 0f); }, 0.3f, true, 0.6f, 1.5f, 502, blockExtra: 2f);
            int prock = Scatter(rocks, new[] { "ENV_PC_MossRock_A", "ENV_PC_MossRock_B", "ENV_PC_MossRock_C" }, 18, 5f, p => { var d = p - pool; d.y = 0; return d.magnitude < 34f && SampleMap(c.veg2[3], p) < 0.3f ? 1f : 0f; }, 0.35f, true, 0.7f, 1.6f, 503, blockExtra: 1f);
            int roots = PlaceRoots(forest, P, c, src);
            int gfern = Scatter(waterEdge, new[] { "ENV_PC_GiantFern_A", "ENV_PC_GiantFern_B" }, 55, 5f, p => { float dw = WaterDist(c, p); return dw > 1.2f && dw < 7f && Slope(p) < 30f ? 0.7f : 0f; }, 0.05f, false, 0.8f, 1.3f, 504, blockExtra: 0.5f);
            int hclump = Scatter(waterEdge, new[] { "ENV_PC_HorsetailClump" }, 70, 3.5f, p => { float dw = WaterDist(c, p), wm = SampleMap(c.veg2[1], p); return (dw > 0.6f && dw < 4.5f) || (wm > 0.15f && wm < 0.7f && SampleMap(c.veg2[3], p) < 0.5f) ? 0.8f : 0f; }, 0.05f, false, 0.8f, 1.3f, 505, blockExtra: 0.3f);
            int mag = Scatter(forest, new[] { "ENV_PC_Magnolia_A", "ENV_PC_Magnolia_B" }, 100, 7f, p => { float f = Forest(c, p); return f > 0.2f && f < 0.65f && Slope(p) < 22f && GroundY(p) > 2f ? 0.5f : 0f; }, 0.08f, false, 0.8f, 1.25f, 506, blockExtra: 1f);
            // Phase 1: denser, bigger dark basalt on the volcanic ridge (was 38 groups at 0.7-1.4)
            int basaltN = Scatter(volc, new[] { "ENV_PC_Basalt_A", "ENV_PC_Basalt_B", "ENV_PC_BasaltBoulder" }, 85, 6f, p => SampleMap(c.veg2[2], p) > 0.35f ? 0.95f : 0f, 0.25f, false, 0.8f, 1.9f, 507, blockExtra: 3.5f);
            int vines = PlaceCurtains(forest, P, c);
            L($"placed: {trunks} fallen trunks, {roots} root plates at old araucarias, {mrock} moss rocks (+{prock} round the waterfall pool), {gfern} riverside giant ferns, {hclump} horsetail clumps, {mag} magnolia shrubs, {basaltN} basalt groups, {vines} hanging moss / vine curtains");
            L(PlaceWaterfallDressing(scene, P, c, treeGrid));
            _terrain.Flush(); EditorUtility.SetDirty(td);
            AssetDatabase.SaveAssets();
            SaveScene(scene, wasDirty);
            return End("Vegetation");
        }

        static void Details(TerrainData src, TerrainData td, Ctx c, Dictionary<string, GameObject> dets)
        {
            int res = td.detailResolution;
            var protos = td.detailPrototypes.ToList();
            int Proto(string n, float minS, float maxS)
            {
                if (!dets.TryGetValue(n, out var pf)) return -1;
                int i = protos.FindIndex(p => p.prototype == pf);
                var dp = i >= 0 ? protos[i] : new DetailPrototype();
                dp.prototype = pf; dp.usePrototypeMesh = true; dp.renderMode = DetailRenderMode.VertexLit; dp.useInstancing = true;
                dp.minWidth = minS; dp.maxWidth = maxS; dp.minHeight = minS; dp.maxHeight = maxS; dp.noiseSpread = 0.35f; dp.alignToGround = 0.3f; dp.positionJitter = 1f;
                dp.healthyColor = Color.white; dp.dryColor = new Color(0.92f, 0.9f, 0.8f);
                if (i < 0) { protos.Add(dp); i = protos.Count - 1; } else protos[i] = dp;
                return i;
            }
            int iH = Proto("DET_PC_Horsetail", 0.75f, 1.25f), iR = Proto("DET_PC_Reeds", 0.8f, 1.3f), iG = Proto("DET_PC_GiantFern", 0.7f, 1.15f);
            td.detailPrototypes = protos.ToArray();
            var size = td.size; var tp = _terrain.transform.position;
            var keep = c.veg[1];
            // the original three layers, rebuilt from the v1 backup: grass mostly becomes low ferns (a fern prairie), bushes halved
            int n0 = Mathf.Min(3, src.detailPrototypes.Length);
            int[][,] orig = new int[n0][,];
            for (int l = 0; l < n0; l++) orig[l] = src.GetDetailLayer(0, 0, res, res, l);
            string N(int l) => td.detailPrototypes[l].prototype ? td.detailPrototypes[l].prototype.name : "";
            int fernL = -1, grassL = -1, bushL = -1;
            for (int l = 0; l < n0; l++) { var nm = N(l); if (nm.Contains("Fern")) fernL = l; else if (nm.Contains("Grass")) grassL = l; else if (nm.Contains("Bush")) bushL = l; }
            var outL = new Dictionary<int, int[,]>();
            foreach (int l in new[] { fernL, grassL, bushL }) if (l >= 0) outL[l] = new int[res, res];
            int[,] hL = iH >= 0 ? new int[res, res] : null, rL = iR >= 0 ? new int[res, res] : null, gL = iG >= 0 ? new int[res, res] : null;
            long cnt0 = 0, cnt1 = 0, cH = 0, cR = 0, cG = 0;
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    var w = new Vector3((x + 0.5f) / res * size.x + tp.x, 0f, (z + 0.5f) / res * size.z + tp.z);
                    float k = SampleMap(keep, w);
                    float rnd = Rand01(x * 31 + 7, z * 17 + 3);
                    int Round(float v) { int i = (int)v; if (rnd < v - i) i++; return i; }
                    int g = grassL >= 0 ? orig[grassL][z, x] : 0, fe = fernL >= 0 ? orig[fernL][z, x] : 0, bu = bushL >= 0 ? orig[bushL][z, x] : 0;
                    cnt0 += g + fe + bu;
                    if (grassL >= 0) outL[grassL][z, x] = Round(g * k * 0.35f);
                    if (fernL >= 0) outL[fernL][z, x] = Round((fe + g * 0.22f) * k);
                    if (bushL >= 0) outL[bushL][z, x] = Round(bu * k * 0.5f);
                    if (hL != null) { hL[z, x] = Round(SampleMap(c.veg[2], w) * 2.6f); cH += hL[z, x]; }
                    if (rL != null) { rL[z, x] = Round(SampleMap(c.veg[3], w) * 3.2f); cR += rL[z, x]; }
                    if (gL != null) { gL[z, x] = Round(SampleMap(c.veg2[0], w) * Forest(c, w) * 0.55f); cG += gL[z, x]; }
                }
            foreach (var kv in outL) { td.SetDetailLayer(0, 0, kv.Key, kv.Value); foreach (var v in kv.Value) cnt1 += v; }
            if (hL != null) td.SetDetailLayer(0, 0, iH, hL);
            if (rL != null) td.SetDetailLayer(0, 0, iR, rL);
            if (gL != null) td.SetDetailLayer(0, 0, iG, gL);
            L($"details: grass / fern / bush {cnt0} -> {cnt1} (grass x0.35 with part of it as low ferns, bushes x0.5); new: horsetail {cH}, reeds {cR}, giant fern {cG}");
        }

        static int PlaceRoots(Transform parent, Dictionary<string, GameObject> P, Ctx c, TerrainData src)
        {
            var names = new[] { "ENV_PC_Roots_A", "ENV_PC_Roots_B" }.Where(P.ContainsKey).ToArray(); if (names.Length == 0) return 0;
            var trees = c.td.treeInstances; int n = 0; var used = new Dictionary<long, List<Vector3>>();
            for (int i = 0; i < src.treeInstanceCount && n < 45; i++)
            {
                var t = trees[i]; if (t.prototypeIndex != 0) continue;                     // the tall araucarias
                var p = Vector3.Scale(t.position, c.size) + c.tp;
                if (Forest(c, p) < 0.5f || Rand01(i, 9) > 0.18f || GNear(used, p, 14f, 8f) || Slope(p) > 20f) continue;
                if (Blocked(c, p, 2.6f) || RouteDist(c, p) < 6f) continue;           // root plates are wide: keep gameplay nodes clear
                var go = (GameObject)PrefabUtility.InstantiatePrefab(P[names[n % names.Length]], parent);
                float s = Mathf.Lerp(0.75f, 1.2f, Rand01(i, 10)) * t.widthScale;
                go.transform.SetPositionAndRotation(new Vector3(p.x, GroundY(p) - 0.15f, p.z), Quaternion.Euler(0, Rand01(i, 11) * 360f, 0));
                go.transform.localScale = new Vector3(s, Mathf.Min(1.2f, s), s);
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic);
                GAdd(used, p, 8f); n++;
            }
            return n;
        }

        /// <summary>
        /// Phase 1: the falls and the pool (World/Environment/WaterfallDressing, rebuilt each run): moss / climbing-fern curtains
        /// on the rock walls either side of the sheet (ray cast against the terrain and the RockFace / cliff colliders), wet
        /// mossy boulders on the back half of the pool edge (M_Env_WetRockMossy), giant ferns and horsetails on the ledges and
        /// banks. Never in the water, in front of the sheet, on the downstream (approach) side for rocks, or on gameplay objects.
        /// </summary>
        static string PlaceWaterfallDressing(UnityEngine.SceneManagement.Scene scene, Dictionary<string, GameObject> P, Ctx c, Dictionary<long, List<Vector3>> treeGrid)
        {
            var root = EnvGroup(scene, "WaterfallDressing"); ClearChildren(root);
            var fw = _features.water.waterfall;
            var lip = V3(fw.lip); var pool = V3(fw.pool); float pr = fw.poolRadius; float drop = lip.y - pool.y;
            var fwd = pool - lip; fwd.y = 0f; fwd.Normalize(); var side = new Vector3(-fwd.z, 0f, fwd.x);
            Material wetMat = null; if (_mats != null) _mats.TryGetValue("M_Env_WetRockMossy", out wetMat);
            var world = Root(scene, "World");
            var rockRoots = new[] { "World/Cliffs", "World/Rocks", "World/Environment/Water/Waterfall/RockFace", "World/Environment/Rocks" }.Select(p => PrimalFrontier.Core.SceneRoots.Legacy(p)).ToArray();
            bool IsGround(Collider col) => col is TerrainCollider || rockRoots.Any(r => r && col.transform.IsChildOf(r));
            var used = new Dictionary<long, List<Vector3>>();
            Physics.SyncTransforms();
            int ferns = 0, rocks = 0, curtains = 0, clumps = 0;
            bool InFall(Vector3 p)
            {
                var q = p - lip; q.y = 0f; float along = Vector3.Dot(q, fwd), across = Mathf.Abs(Vector3.Dot(q, side));
                return along > -1.5f && along < pr + 2.5f && across < 3f;
            }
            bool Down(Vector3 xz, out RaycastHit hit)
            {
                var o = new Vector3(xz.x, lip.y + 25f, xz.z);
                foreach (var h in Physics.RaycastAll(o, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore).OrderBy(e => e.distance))
                    if (IsGround(h.collider)) { hit = h; return true; }
                    else if (!h.collider.isTrigger) break;                                   // something else is there: skip
                hit = default; return false;
            }
            GameObject Inst(string pf, Vector3 at, Quaternion rot, float s)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(P[pf], root);
                go.transform.SetPositionAndRotation(at, rot); go.transform.localScale = Vector3.one * s;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic);
                return go;
            }
            // ---- curtains on the walls either side of the sheet
            var cur = new[] { "ENV_PC_VineCurtain_A", "ENV_PC_VineCurtain_B" }.Where(P.ContainsKey).ToArray();
            if (cur.Length > 0)
                for (int k = 0; k < 23; k++)
                {
                    float ang = -99f + k * 9f; if (Mathf.Abs(ang) < 14f) continue;                   // the fall itself stays clear
                    foreach (float hf in new[] { 0.95f, 0.75f, 0.5f })
                    {
                        var dir = Quaternion.Euler(0f, ang, 0f) * (-fwd);
                        var o = new Vector3(pool.x, pool.y + drop * hf, pool.z) + dir * 1.5f;
                        if (!Physics.Raycast(o, dir, out var hit, 30f, ~0, QueryTriggerInteraction.Ignore) || !IsGround(hit.collider)) continue;
                        if (Vector3.Angle(hit.normal, Vector3.up) < 38f || GNear(used, hit.point, 2.6f, 4f) || InFall(hit.point)) continue;
                        var up = Vector3.ProjectOnPlane(Vector3.up, hit.normal).normalized;
                        Inst(cur[(k + (hf > 0.7f ? 1 : 0)) % cur.Length], hit.point + hit.normal * 0.12f, Quaternion.LookRotation(-hit.normal, up), 0.9f + Rand01(k, (int)(hf * 100f)) * 0.5f);
                        GAdd(used, hit.point, 4f); curtains++;
                    }
                }
            // ---- wet mossy boulders on the back half of the pool edge (the downstream side stays open to reach the water)
            var mr = new[] { "ENV_PC_MossRock_A", "ENV_PC_MossRock_B", "ENV_PC_MossRock_C" }.Where(P.ContainsKey).ToArray();
            int rjFall = 0, rjWater = 0, rjBlock = 0, rjNear = 0, rjGround = 0;
            if (mr.Length > 0)
                for (int k = 0; k < 80 && rocks < 12; k++)
                {
                    float ang = (Rand01(k, 41) - 0.5f) * 200f;
                    var dir = Quaternion.Euler(0f, ang, 0f) * (-fwd);
                    var p = pool + dir * (pr + 0.3f + Rand01(k, 42) * 3.2f);
                    if (InFall(p)) { rjFall++; continue; }
                    if (WaterDist(c, p) < 0.2f) { rjWater++; continue; }
                    if (Blocked(c, p, 0.3f)) { rjBlock++; continue; }
                    if (GNear(used, p, 2.0f, 4f)) { rjNear++; continue; }
                    if (!Down(p, out var hit) || hit.point.y < pool.y + 0.05f) { rjGround++; continue; }
                    float s = 0.45f + Rand01(k, 43) * 0.6f;
                    var go = Inst(mr[k % mr.Length], hit.point + Vector3.down * 0.3f * s, Quaternion.FromToRotation(Vector3.up, hit.normal) * Quaternion.Euler(0f, Rand01(k, 44) * 360f, 0f), s);
                    if (wetMat) foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true)) { var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) ms[i] = wetMat; r.sharedMaterials = ms; }
                    GAdd(used, p, 4f); rocks++;
                }
            // ---- giant ferns + horsetails on the banks and ledges round the pool and up the walls
            var gf = new[] { "ENV_PC_GiantFern_A", "ENV_PC_GiantFern_B" }.Where(P.ContainsKey).ToArray();
            bool hasH = P.ContainsKey("ENV_PC_HorsetailClump");
            for (int k = 0; k < 260 && (ferns < 34 || clumps < 12); k++)
            {
                float ang = Rand01(k, 51) * 360f, rr = pr + 0.8f + Rand01(k, 52) * 13f;
                var p = pool + Quaternion.Euler(0f, ang, 0f) * Vector3.forward * rr;
                if (InFall(p) || WaterDist(c, p) < 0.5f || Blocked(c, p, 0.3f) || GNear(used, p, 1.8f, 4f) || GNear(treeGrid, p, 1.2f, 5f)) continue;
                if (!Down(p, out var hit) || hit.point.y < pool.y + 0.1f || Vector3.Angle(hit.normal, Vector3.up) > 40f) continue;
                bool wetBank = WaterDist(c, p) < 3.5f;
                if (hasH && wetBank && clumps < 12 && Rand01(k, 53) < 0.55f)
                { Inst("ENV_PC_HorsetailClump", hit.point + Vector3.down * 0.05f, Quaternion.Euler(0f, Rand01(k, 54) * 360f, 0f), 0.8f + Rand01(k, 55) * 0.4f); clumps++; }
                else if (gf.Length > 0 && ferns < 34)
                { Inst(gf[k % gf.Length], hit.point + Vector3.down * 0.05f, Quaternion.Euler(0f, Rand01(k, 56) * 360f, 0f), 0.75f + Rand01(k, 57) * 0.55f); ferns++; }
                else continue;
                GAdd(used, p, 4f);
            }
            return $"waterfall dressing (World/Environment/WaterfallDressing): {curtains} moss / fern curtains on the walls, {rocks} wet mossy boulders (M_Env_WetRockMossy {(wetMat ? "on" : "missing")}) on the back of the pool (rejected: fall {rjFall}, water {rjWater}, gameplay {rjBlock}, spacing {rjNear}, no rock / ground {rjGround}), {ferns} giant ferns, {clumps} horsetail clumps; pool r {F(pr)} m, drop {F(drop)} m, the sheet, pool water and downstream bank left clear";
        }

        /// <summary>hanging moss / climbing-fern curtains on the rims of the canyon walls and the waterfall amphitheatre</summary>
        static int PlaceCurtains(Transform parent, Dictionary<string, GameObject> P, Ctx c)
        {
            var names = new[] { "ENV_PC_VineCurtain_A", "ENV_PC_VineCurtain_B" }.Where(P.ContainsKey).ToArray(); if (names.Length == 0) return 0;
            int n = 0;
            void Put(Vector3 rim, Vector3 outward, int seed)
            {
                outward.y = 0; outward.Normalize();
                // lie along the wall face just below the rim (the walls are steep, not vertical: a curtain hanging
                // straight down from the rim would sit inside the rock). Model: hangs along its -y, strands on its -z side.
                var face = rim + outward * 0.6f;
                var wn = GroundNormal(face);
                if (Vector3.Angle(wn, Vector3.up) < 40f) return;                              // no real wall here
                var up = Vector3.ProjectOnPlane(Vector3.up, wn).normalized;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(P[names[seed % names.Length]], parent);
                go.transform.SetPositionAndRotation(Ground(face) + wn * 0.12f, Quaternion.LookRotation(-wn, up));
                go.transform.localScale = Vector3.one * (0.8f + Rand01(seed, 3) * 0.5f);
                n++;
            }
            bool FindRim(Vector3 centre, Vector3 dir, float floorY, out Vector3 rim)
            {
                rim = centre;
                for (float d = 1f; d < 14f; d += 0.5f)
                {
                    var q = centre + dir * d; float y = GroundY(q);
                    if (y - floorY > 7f)
                    {
                        // walk on until the slope flattens: that is the rim
                        for (float e = d; e < d + 10f; e += 0.5f) { var r = centre + dir * e; if (Slope(r) < 35f) { rim = new Vector3(r.x, GroundY(r), r.z); return true; } }
                        return false;
                    }
                }
                return false;
            }
            var can = Pts(_features.canyon != null ? _features.canyon.points : null);
            for (int i = 3; i + 2 < can.Count; i += 4)
            {
                var a = can[i]; var t = (can[i + 1] - can[i - 1]); t.y = 0; t.Normalize();
                var side = new Vector3(-t.z, 0, t.x);
                foreach (var s in new[] { 1f, -1f })
                    if (FindRim(new Vector3(a.x, 0, a.z), side * s, GroundY(a), out var rim)) Put(rim, -side * s, i * 2 + (s > 0 ? 1 : 0));
            }
            // the waterfall amphitheatre: behind and beside the fall
            var pool = V3(_features.water.waterfall.pool); var lip = V3(_features.water.waterfall.lip);
            var fwd = pool - lip; fwd.y = 0; fwd.Normalize();
            for (int k = 0; k < 7; k++)
            {
                float ang = -75f + k * 25f; if (Mathf.Abs(ang) < 12f) continue;           // leave the fall itself clear
                var dir = Quaternion.Euler(0, ang, 0) * (-fwd);
                if (FindRim(new Vector3(pool.x, 0, pool.z), dir, pool.y, out var rim)) Put(rim, -dir, 900 + k);
            }
            return n;
        }
    }
}
