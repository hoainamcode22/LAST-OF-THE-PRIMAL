using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using PrimalFrontier.Core;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// ENV-A (EA_), Phase 2 zone 1 MIGRATION VALLEY (meadow + herbivore_valley + migration route + lookout knoll), centre
    /// (0, 9, -35), r 95 + 30 m blend. Bridge commands (idempotent, each saves the scene):
    ///   Valley "[assets|terrain|props]" (default all three)
    ///     assets   DET_Z_TallGrass (copy of DET_ENV_Grass_01 on M_Z_TallGrass_Wind = PF/Foliage Wind) as a terrain detail prototype
    ///     terrain  from the EA baseline (TD_Island_before_EA): splat (meadow grass, trampled herd trail, muddy wallows and ford
    ///              banks, dark soil patches), details (tall grass in clumps, short grass between, ferns + giant ferns at the
    ///              forest edges, horsetail / reeds on the river banks; route trampled, wallows and water kept bare) and
    ///              sparse own terrain trees (araucaria / cycad clusters at the edges, tree ferns + cycads along the river),
    ///              never on the route, the wallows, the knoll view corridors or gameplay objects
    ///     props    World/Environment/Forest/MigrationValley/{Rocks, Props, Landmark, FX_Anchors}: boulders, edge giant ferns,
    ///              fallen trunks, riverside horsetail clumps, LM_MigrationValley (+ ART's LM_RockRidge once delivered), FX anchors
    ///   ValleyCheck    read-only: counts, missing refs, covered nodes, route / wallows / knoll view.
    /// Heights are not changed.
    /// </summary>
    public static partial class PrimalZonesBuilder
    {
        const string EA = "EA";
        // splat layer order of TD_Island (Survey): Sand, Grass, ForestFloor, Rock, Mud, SandWet, Moss, DarkSoil, Ash
        const int LSand = 0, LGrass = 1, LForest = 2, LRock = 3, LMud = 4, LSandWet = 5, LMoss = 6, LDark = 7;
        static readonly Vector3 KnollTop = new Vector3(-40f, 25.4f, -92f), Ford = new Vector3(84.6f, 10.6f, -97.5f),
            FallsPool = new Vector3(93.5f, 19.4f, -160f), MeadowC = new Vector3(-2f, 9.2f, -28f);
        static readonly Vector3 ValleyLandmark = new Vector3(-72f, 0f, -8f);

        [PrimalBridgeCommand]
        public static string Valley(string arg)
        {
            Begin(EA, "Valley " + arg);
            var z = ZoneById("valley");
            if (!OpenIsland(out var scene, out bool wasDirty)) return End();
            if (!FindTerrain() || LoadFeat() == null) { W("no terrain or features"); return End(); }
            bool all = !(arg.Contains("assets") || arg.Contains("terrain") || arg.Contains("props"));
            if (all || arg.Contains("assets") || arg.Contains("terrain")) EA_TallGrassProto();
            if (all || arg.Contains("terrain"))
            {
                var bk = BackupTerrain(EA); if (!bk) return End();
                BuildBlockers(scene); EA_RidgePose(z);
                EA_ValleySplat(z, bk);
                EA_ValleyDetails(z, bk);
                EA_ClearKnollView(z, true);                    // moved trees back first: every run starts from the same trees
                EA_ValleyTrees(z);
                EA_ClearKnollView(z, false);
                Ter.Flush(); EditorUtility.SetDirty(TD); AssetDatabase.SaveAssets();
            }
            if (all || arg.Contains("props")) { BuildBlockers(scene); EA_RidgePose(z); EA_ValleyProps(z); }
            SaveIsland(scene, wasDirty);
            return End();
        }

        // ------------------------------------------------------------------ assets
        /// <summary>tall wind grass: DET_Z_TallGrass (DET_ENV_Grass_01 mesh) on its own PF/Foliage Wind material; returns the detail layer</summary>
        static int EA_TallGrassProto()
        {
            var pf = EA_WindDetail("DET_Z_TallGrass", "DET_ENV_Grass_01", "M_Z_TallGrass_Wind", 1.7f, 0.22f, 0.035f, new Color(1f, 0.96f, 0.82f), 0.2f);
            if (!pf) return -1;
            int i = AddDetailProto(pf, 1.5f, 2.2f, 1.7f, 2.6f, 0.3f, 0.15f, new Color(0.93f, 0.96f, 0.78f), new Color(0.97f, 0.86f, 0.56f));
            L($"tall grass: detail layer {i} = {pf.name} (w 1.5-2.2, h 1.7-2.6 x 0.7 m mesh = about 1.2-1.8 m tall), PF/Foliage Wind");
            return i;
        }

        /// <summary>a detail prefab in Prefabs/Environment/Zones copied from an existing one, with its own PF/Foliage Wind material (Art/Environment/Materials/Zones)</summary>
        static GameObject EA_WindDetail(string name, string source, string matName, float windHeight, float sway, float flutter, Color tint, float variation)
        {
            var src = Prefab(source); if (!src) return null;
            EnsureFolder(PrefabZones); EnsureFolder(ZoneMatDir);
            var srcR = src.GetComponentInChildren<MeshRenderer>(); var srcM = srcR ? srcR.sharedMaterial : null;
            var sh = Shader.Find("PF/Foliage Wind");
            if (!srcM || !sh) { W($"{name}: source material or PF/Foliage Wind missing"); return null; }
            string mp = $"{ZoneMatDir}/{matName}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (!m) { m = new Material(srcM) { name = matName }; AssetDatabase.CreateAsset(m, mp); }
            bool clip = srcM.IsKeywordEnabled("_ALPHATEST_ON") || (srcM.HasProperty("_AlphaClip") && srcM.GetFloat("_AlphaClip") > 0.5f);
            var tex = srcM.HasProperty("_BaseMap") ? srcM.GetTexture("_BaseMap") : srcM.mainTexture;
            var col = srcM.HasProperty("_BaseColor") ? srcM.GetColor("_BaseColor") : Color.white;
            float cutoff = srcM.HasProperty("_Cutoff") ? srcM.GetFloat("_Cutoff") : 0.5f, cull = srcM.HasProperty("_Cull") ? srcM.GetFloat("_Cull") : 0f;
            m.shader = sh;
            m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", col * tint); m.SetFloat("_Cutoff", cutoff); m.SetFloat("_Cull", cull);
            m.SetFloat("_AlphaClip", clip ? 1f : 0f); if (clip) m.EnableKeyword("_ALPHATEST_ON"); else m.DisableKeyword("_ALPHATEST_ON");
            m.SetFloat("_WindHeight", windHeight); m.SetFloat("_WindSway", sway); m.SetFloat("_Flutter", flutter);
            m.SetFloat("_Translucency", 0.45f); m.SetFloat("_ColorVariation", variation); m.SetColor("_VariationTint", new Color(1.1f, 0.98f, 0.66f, 1f));
            m.enableInstancing = true; m.renderQueue = clip ? 2450 : -1;
            EditorUtility.SetDirty(m);
            string pp = $"{PrefabZones}/{name}.prefab";
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(pp)) AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(src), pp);
            var root = PrefabUtility.LoadPrefabContents(pp);
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true)) { var ms = r.sharedMaterials; for (int k = 0; k < ms.Length; k++) ms[k] = m; r.sharedMaterials = ms; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
            PrefabUtility.SaveAsPrefabAsset(root, pp); PrefabUtility.UnloadPrefabContents(root);
            AssetDatabase.SaveAssets();
            _pfCache.Remove(name);
            return AssetDatabase.LoadAssetAtPath<GameObject>(pp);
        }

        // ------------------------------------------------------------------ shared valley masks
        static Vector3[] _wallows;
        static Vector3[] Wallows() => _wallows != null && _wallows.Length > 0 ? _wallows : (_wallows = Pts(Feat.props.wallows).ToArray());
        static float WallowDist(Vector3 p) { float b = 999f; foreach (var w in Wallows()) b = Mathf.Min(b, new Vector2(w.x - p.x, w.z - p.z).magnitude); return b; }
        /// <summary>would something of this height at p cut a knoll view line (knoll top -> ford / waterfall pool / meadow centre)?</summary>
        static bool EA_BlocksKnollView(Vector3 p, float height, float halfWidth)
        {
            var eye = KnollTop + Vector3.up * 1.7f;
            foreach (var tgt in new[] { Ford + Vector3.up * 1.5f, FallsPool + Vector3.up * 6f, MeadowC + Vector3.up * 1.5f })
            {
                if (SegDist(p, eye, tgt) > halfWidth) continue;
                var a = new Vector2(eye.x, eye.z); var ab = new Vector2(tgt.x, tgt.z) - a;
                float t = Mathf.Clamp01(Vector2.Dot(new Vector2(p.x, p.z) - a, ab) / ab.sqrMagnitude);
                if (t < 0.04f) continue;                                          // the knoll top itself
                float lineY = Mathf.Lerp(eye.y, tgt.y, t);
                if (GroundY(p) + height > lineY - 1f) return true;
            }
            return false;
        }
        static int Mod(int a, int n) => ((a % n) + n) % n;
        static float EA_ForestK(SplatSampler s, Vector3 p) => Mathf.Clamp01(s.Get(p, LForest) + s.Get(p, LMoss) * 0.8f);

        // ------------------------------------------------------------------ splat
        static void EA_ValleySplat(Zone z, TerrainData bk)
        {
            var ford = P3(Feat.water.ford);
            int n = EditSplat(z, bk, (p, ow, b, t) =>
            {
                float slope = Slope(p); if (slope > 32f) return;                      // cliffs / rock faces stay as they are
                float forest = Mathf.Clamp01(b[LForest] + b[LMoss] * 0.8f);
                float rd = RouteDist(p), wd = WallowDist(p), dw = WaterDist(p);
                float flat = 1f - Smooth01((slope - 14f) / 14f);
                // 1) meadow: the open valley floor becomes grassland (forest floor pulled toward grass where it is thin)
                float open = flat * (1f - Smooth01((forest - 0.35f) / 0.45f));
                float moveF = Mathf.Min(t[LForest], t[LForest] * 0.55f * open); t[LForest] -= moveF; t[LGrass] += moveF;
                // 2) dark soil patches in the grass (grazed / dry ground), value noise 9 m
                float patch = Smooth01((Noise(p.x, p.z, 9f, 11) - 0.62f) / 0.2f) * flat;
                float moveG = t[LGrass] * 0.35f * patch; t[LGrass] -= moveG; t[LDark] += moveG;
                // 3) the herd trail along the migration route: trampled dark soil with mud in the hollows
                float trail = 1f - Smooth01((rd - 1.5f) / 3.5f);
                if (trail > 0f)
                {
                    float wob = Noise(p.x, p.z, 4f, 12);
                    float k = trail * (0.55f + 0.35f * wob);
                    for (int l = 0; l < t.Length; l++) t[l] *= 1f - k;
                    t[LDark] += k * 0.62f; t[LMud] += k * 0.28f; t[LGrass] += k * 0.10f;
                }
                // 4) wallows: mud (existing hollows), a darker muddy ring round them
                float wal = 1f - Smooth01((wd - 5f) / 6f);
                if (wal > 0f) { for (int l = 0; l < t.Length; l++) t[l] *= 1f - wal * 0.9f; t[LMud] += wal * 0.75f; t[LDark] += wal * 0.15f; }
                // 5) river banks and the ford: wet sand and mud
                float bank = 1f - Smooth01((dw - 1.5f) / 4.5f);
                float fordK = 1f - Smooth01((new Vector2(p.x - ford.x, p.z - ford.z).magnitude - 6f) / 10f);
                float wet = Mathf.Max(bank * 0.7f, fordK * 0.9f);
                if (wet > 0f) { for (int l = 0; l < t.Length; l++) t[l] *= 1f - wet * 0.8f; t[LSandWet] += wet * 0.45f; t[LMud] += wet * 0.35f; }
            });
            L($"valley splat: {n} texels written (lerp by OwnWeight from the EA baseline)");
        }

        // ------------------------------------------------------------------ details
        static void EA_ValleyDetails(Zone z, TerrainData bk)
        {
            int lFern = DetailLayer("DET_ENV_Fern_01"), lGrass = DetailLayer("DET_ENV_Grass_01"), lH = DetailLayer("DET_PC_Horsetail"),
                lR = DetailLayer("DET_PC_Reeds"), lGF = DetailLayer("DET_PC_GiantFern"), lTall = DetailLayer("DET_Z_TallGrass");
            var layers = new[] { lFern, lGrass, lH, lR, lGF, lTall };
            if (layers.Any(l => l < 0)) { W("valley details: a detail layer is missing: " + string.Join(", ", layers)); return; }
            var s = new SplatSampler(z, bk);
            int n = EditDetails(z, bk, layers, (p, ow, b, t) =>
            {
                float slope = Slope(p), dw = WaterDist(p), rd = RouteDist(p), wd = WallowDist(p);
                float forest = EA_ForestK(s, p), rock = s.Get(p, LRock);
                if (dw <= 0.05f) { t[0] = t[1] = t[4] = t[5] = 0; t[2] = 0; return; }      // in the river: nothing sticks through the water
                float flat = 1f - Smooth01((slope - 18f) / 14f);
                float open = flat * (1f - Smooth01((forest - 0.3f) / 0.45f)) * (1f - rock);
                float clump = Smooth01((Noise(p.x, p.z, 14f, 21) - 0.35f) / 0.4f) * 0.8f + Noise(p.x, p.z, 5f, 22) * 0.2f;
                float trampled = Smooth01((rd - 1.5f) / 3.5f);                       // 0 on the route, 1 away from it
                float bare = Smooth01((wd - 6f) / 4f);                                  // 0 in the wallows
                float knoll = Smooth01((new Vector2(p.x - KnollTop.x, p.z - KnollTop.z).magnitude - 10f) / 8f);   // the knoll top keeps short grass
                float wetBank = 1f - Smooth01((dw - 1f) / 4f);
                // tall grass in clumps on the open floor (thin on the trail, none in the wallows or on the banks)
                t[5] = Mathf.RoundToInt(open * (0.5f + 2.7f * clump) * (0.15f + 0.85f * trampled) * bare * knoll * (1f - wetBank));
                // short grass between the clumps
                t[1] = Mathf.Max(b[1], Mathf.RoundToInt(open * (1.2f + 1.6f * (1f - clump)) * (0.4f + 0.6f * trampled) * bare));
                // ferns and giant ferns at the forest edges and in the outer blend band (forest -> meadow)
                float edge = 4f * forest * (1f - forest), band = Mathf.Clamp01(1f - Mathf.Abs(ow - 0.4f) / 0.35f);
                float fe = Mathf.Max(edge, band * 0.8f) * (slope < 32f ? 1f : 0f) * bare;
                t[0] = Mathf.Max(b[0], Mathf.RoundToInt(fe * 2.4f * (0.6f + 0.4f * Noise(p.x, p.z, 7f, 23))));
                t[4] = Mathf.Max(b[4], Mathf.RoundToInt(edge * 0.45f * Noise(p.x, p.z, 9f, 24) * 1.6f));
                // river banks: horsetail, reeds right at the edge
                t[2] = Mathf.Max(b[2], Mathf.RoundToInt(wetBank * 2.2f * (0.5f + 0.5f * Noise(p.x, p.z, 6f, 25))));
                t[3] = Mathf.Max(b[3], Mathf.RoundToInt((1f - Smooth01((dw - 0.3f) / 1.7f)) * 2.4f));
            }, out long before, out long after);
            L($"valley details: {n} texels, layers fern {lFern} grass {lGrass} horsetail {lH} reeds {lR} giantfern {lGF} tallgrass {lTall}; instances in the rect {before} -> {after}");
        }

        // ------------------------------------------------------------------ trees
        static void EA_ValleyTrees(Zone z)
        {
            string key = EA + "_valley";
            int arau = TreeProto("PFB_ENV_Tree_01");
            var ferns = new[] { "ENV_PC_TreeFern_A", "ENV_PC_TreeFern_B", "ENV_PC_TreeFern_C" }.Select(nm => TreeProto(nm)).Where(i => i >= 0).ToArray();
            var cycads = new[] { "ENV_PC_Cycad_A", "ENV_PC_Cycad_B", "ENV_PC_Cycad_C" }.Select(nm => TreeProto(nm)).Where(i => i >= 0).ToArray();
            if (arau < 0 || ferns.Length == 0 || cycads.Length == 0) { W("valley trees: prototypes missing"); return; }
            var grid = TreeGrid(OwnTreeSlots(key));
            var s = new SplatSampler(z);
            var mine = new List<TreeInstance>(); var mineGrid = new Dictionary<long, List<Vector3>>();
            int rjRoute = 0, rjView = 0, rjBlock = 0, rjNear = 0;
            const float step = 6f; var b = z.Bounds;
            for (float gz = b.yMin; gz < b.yMax && mine.Count < 120; gz += step)
                for (float gx = b.xMin; gx < b.xMax && mine.Count < 120; gx += step)
                {
                    int ix = Mathf.RoundToInt(gx), iz = Mathf.RoundToInt(gz);
                    var p = new Vector3(gx + Rand01(ix, iz * 3 + 1) * step, 0f, gz + Rand01(ix * 5 + 2, iz) * step);
                    float ow = OwnWeight(z, p); if (ow <= 0.05f) continue;
                    p.y = GroundY(p);
                    float slope = Slope(p), dw = WaterDist(p); if (slope > 26f || dw < 2f || p.y < 1.5f) continue;
                    float forest = EA_ForestK(s, p);
                    float cluster = Smooth01((Noise(p.x, p.z, 26f, 31) - 0.45f) / 0.3f);
                    float bandK = Mathf.Clamp01(1f - Mathf.Abs(ow - 0.35f) / 0.35f);           // the outer ring of the valley
                    float riverK = dw < 14f ? 1f - Mathf.Clamp01((dw - 3f) / 11f) : 0f;
                    float pEdge = 0.42f * cluster * Mathf.Max(bandK, 4f * forest * (1f - forest));
                    float pRiver = 0.3f * riverK * (0.4f + 0.6f * cluster);
                    float pLone = cluster > 0.85f ? 0.02f : 0f;
                    float pr = pEdge + pRiver + pLone, r = Rand01(ix * 7 + 3, iz * 11 + 5);
                    if (r >= pr) continue;
                    if (RouteDist(p) < 10f || WallowDist(p) < 12f || EA_InRidge(p, 6f)) { rjRoute++; continue; }
                    if (EA_BlocksKnollView(p, 14f, 16f)) { rjView++; continue; }
                    if (IsBlocked(p, 1.8f)) { rjBlock++; continue; }
                    if (Near(grid, p, 5f, 5f) || Near(mineGrid, p, 5.5f, 5f)) { rjNear++; continue; }
                    int proto; float sc;
                    float r2 = Rand01(ix * 13 + 1, iz * 17 + 9);
                    if (r < pRiver) { proto = r2 < 0.62f ? ferns[Mod(ix + iz, ferns.Length)] : cycads[Mod(ix * 3 + iz, cycads.Length)]; sc = 0.85f + Rand01(ix, iz * 7) * 0.45f; }
                    else if (r2 < 0.5f) { proto = arau; sc = 0.8f + Rand01(ix, iz * 7) * 0.45f; }
                    else if (r2 < 0.82f) { proto = cycads[Mod(ix * 3 + iz, cycads.Length)]; sc = 0.8f + Rand01(ix, iz * 7) * 0.55f; }
                    else { proto = ferns[Mod(ix + iz, ferns.Length)]; sc = 0.85f + Rand01(ix, iz * 7) * 0.4f; }
                    mine.Add(MakeTree(proto, p, Rand01(ix * 3, iz * 5) * Mathf.PI * 2f, sc, sc * (0.92f + Rand01(iz, ix) * 0.16f)));
                    Add(mineGrid, p, 5f);
                }
            L($"valley trees: {mine.Count} own terrain trees (araucaria {mine.Count(t => t.prototypeIndex == arau)}, cycad {mine.Count(t => cycads.Contains(t.prototypeIndex))}, tree fern {mine.Count(t => ferns.Contains(t.prototypeIndex))}); rejected route / wallow / landmark {rjRoute}, knoll view {rjView}, gameplay {rjBlock}, spacing {rjNear}");
            SetOwnTrees(key, mine);
        }

        // ------------------------------------------------------------------ ridge pose (ART LM_RockRidge: 57 x 15.7 x 20.7 m, long axis local X, pivot base centre)
        static Vector3 _ridgePos; static float _ridgeYaw, _ridgeRange; static bool _ridgeOk;
        const float RidgeHalfX = 28.5f, RidgeHalfZ = 10.4f, RidgeH = 15.7f;
        /// <summary>yaw (+-40 deg round "facing the valley centre") with the flattest footprint that keeps the route, gameplay objects and the knoll view clear; y = lowest footprint ground (the 2 m skirt covers the rest)</summary>
        static void EA_RidgePose(Zone z)
        {
            var p0 = Ground(ValleyLandmark); var face = z.centre - p0; face.y = 0f;
            float baseYaw = Quaternion.LookRotation(face.normalized, Vector3.up).eulerAngles.y;
            _ridgePos = p0; _ridgeYaw = baseYaw; _ridgeRange = 99f; _ridgeOk = false; float bestScore = 1e9f;
            for (float dy = -40f; dy <= 40f; dy += 5f)
            {
                var rot = Quaternion.Euler(0f, baseYaw + dy, 0f); float mn = 1e9f, mx = -1e9f; bool bad = false;
                for (float x = -RidgeHalfX; x <= RidgeHalfX + 0.1f; x += 3f)
                    for (float zz = -RidgeHalfZ; zz <= RidgeHalfZ + 0.1f; zz += 3.5f)
                    {
                        var q = p0 + rot * new Vector3(x, 0f, zz); float y = GroundY(q); mn = Mathf.Min(mn, y); mx = Mathf.Max(mx, y);
                        if (IsBlocked(q, 0.8f) || RouteDist(q) < 6f || WaterDist(q) < 1f || EA_BlocksKnollView(q, RidgeH, 3f)) bad = true;
                    }
                float score = (mx - mn) + Mathf.Abs(dy) * 0.02f + (bad ? 1000f : 0f);
                if (score < bestScore) { bestScore = score; _ridgeYaw = baseYaw + dy; _ridgeRange = mx - mn; _ridgePos = new Vector3(p0.x, mn, p0.z); _ridgeOk = !bad; }
            }
            if (!_ridgeOk) W("ridge: no yaw keeps the whole footprint clear of the route / gameplay / knoll view");
            if (_ridgeRange > 2f) W($"ridge: ground range {F(_ridgeRange)} m under the footprint is more than the 2 m skirt: the downhill edge may show a gap");
        }
        static bool EA_InRidge(Vector3 p, float margin)
        {
            var l = Quaternion.Euler(0f, -_ridgeYaw, 0f) * (p - _ridgePos);
            return Mathf.Abs(l.x) < RidgeHalfX + margin && Mathf.Abs(l.z) < RidgeHalfZ + margin;
        }

        // ------------------------------------------------------------------ knoll view: older trees that cut a view line are moved aside
        static readonly Dictionary<int, float> _protoH = new Dictionary<int, float>();
        /// <summary>height of a tree prototype's prefab (tallest mesh, metres at scale 1)</summary>
        static float EA_ProtoHeight(int proto)
        {
            if (_protoH.TryGetValue(proto, out var h)) return h;
            h = 10f; var pf = proto >= 0 && proto < TD.treePrototypes.Length ? TD.treePrototypes[proto].prefab : null;
            if (pf) { float m = 0f; foreach (var mf in pf.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh) m = Mathf.Max(m, mf.sharedMesh.bounds.max.y * mf.transform.lossyScale.y); if (m > 0.5f) h = m; }
            _protoH[proto] = h; return h;
        }
        [System.Serializable] class EAMoved { public int i; public Vector3 from, to; }
        [System.Serializable] class EAMovedList { public List<EAMoved> items = new List<EAMoved>(); }
        /// <summary>
        /// Terrain trees (not EA's own) in the valley whose crown would cut a knoll view line (knoll top -> ford / waterfall pool /
        /// meadow) are moved sideways to the nearest free spot (same index, prototype and scale: TreeHarvest and saves stay valid).
        /// Recorded in Tools/_zones/EA_valley_moved.json; a re-run first puts them back, so the result is the same.
        /// </summary>
        static void EA_ClearKnollView(Zone z, bool restoreOnly)
        {
            string path = System.IO.Path.Combine(TreeRecordDir, EA + "_valley_moved.json");
            var rec = System.IO.File.Exists(path) ? JsonUtility.FromJson<EAMovedList>(System.IO.File.ReadAllText(path)) : new EAMovedList();
            var all = TD.treeInstances; int restored = 0;
            foreach (var m in rec.items) if (m.i < all.Length && (all[m.i].position - m.to).sqrMagnitude < 1e-8f) { all[m.i].position = m.from; restored++; }
            if (restoreOnly) { TD.SetTreeInstances(all, true); L($"knoll view: {restored} moved trees put back"); return; }
            var own = OwnTreeSlots(EA + "_valley");
            var grid = new Dictionary<long, List<Vector3>>(); for (int i = 0; i < all.Length; i++) Add(grid, TreeWorld(all[i]), 5f);
            var outRec = new EAMovedList(); int stuck = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (own.Contains(i)) continue;
                var p = TreeWorld(all[i]); if (OwnWeight(z, p) <= 0f) continue;
                float h = EA_ProtoHeight(all[i].prototypeIndex) * all[i].heightScale;
                if (!EA_BlocksKnollView(p, h, 7f)) continue;
                bool done = false;
                for (float d = 8f; d <= 34f && !done; d += 2f)
                    for (int a = 0; a < 8 && !done; a++)
                    {
                        var q = p + Quaternion.Euler(0f, a * 45f + d * 7f, 0f) * Vector3.forward * d; q.y = GroundY(q);
                        if (OwnWeight(z, q) <= 0f || Slope(q) > 25f || WaterDist(q) < 2f || RouteDist(q) < 8f || WallowDist(q) < 10f) continue;
                        if (EA_BlocksKnollView(q, h, 9f) || IsBlocked(q, 1.5f) || Near(grid, q, 4f, 5f)) continue;
                        var from = all[i].position;
                        all[i].position = new Vector3((q.x - TPos.x) / TD.size.x, (q.y - TPos.y) / TD.size.y, (q.z - TPos.z) / TD.size.z);
                        Add(grid, q, 5f); outRec.items.Add(new EAMoved { i = i, from = from, to = all[i].position }); done = true;
                    }
                if (!done) stuck++;
            }
            TD.SetTreeInstances(all, true);
            System.IO.Directory.CreateDirectory(TreeRecordDir); System.IO.File.WriteAllText(path, JsonUtility.ToJson(outRec, true));
            L($"knoll view: {restored} trees put back from the last run, {outRec.items.Count} trees that cut a view line (knoll -> ford / waterfall / meadow) moved aside (same index), {stuck} without a free spot");
        }

        // ------------------------------------------------------------------ props, landmark, FX anchors
        static void EA_ValleyProps(Zone z)
        {
            var root = ZoneRoot(z);
            var rocks = Group(z, "Rocks"); ClearGroup(rocks);
            var props = Group(z, "Props"); ClearGroup(props);
            var lmG = Group(z, "Landmark");
            var fx = Group(z, "FX_Anchors");
            var s = new SplatSampler(z);
            var trees = TreeGrid(null);
            var placed = new Dictionary<long, List<Vector3>>();
            Physics.SyncTransforms();
            int Scatter(Transform parent, string[] names, int want, float spacing, System.Func<Vector3, float, float> density, float sink, bool align, float sMin, float sMax, int seed,
                float routeClear, float blockExtra, bool solidTest, float viewH)
            {
                var pfs = names.Select(nm => Prefab(nm, false)).Where(pf => pf).ToArray(); if (pfs.Length == 0) { W("valley: no prefab of " + string.Join("/", names)); return 0; }
                int n = 0, tries = 0; var bb = z.Bounds;
                while (n < want && tries < want * 500)
                {
                    tries++;
                    var p = new Vector3(Mathf.Lerp(bb.xMin, bb.xMax, Rand01(seed, tries * 2)), 0f, Mathf.Lerp(bb.yMin, bb.yMax, Rand01(seed + 1, tries * 2 + 1)));
                    float ow = OwnWeight(z, p); if (ow <= 0.05f) continue;
                    p.y = GroundY(p);
                    float d = density(p, ow); if (d <= 0f || Rand01(seed + 2, tries) > d) continue;
                    if (Near(placed, p, spacing) || IsBlocked(p, blockExtra) || Near(trees, p, 2.2f, 5f)) continue;
                    if (RouteDist(p) < routeClear || WallowDist(p) < 8f || EA_InRidge(p, 2.5f)) continue;
                    if (viewH > 0f && EA_BlocksKnollView(p, viewH, 6f)) continue;
                    if (solidTest && SolidAt(p, 1.2f, root)) continue;
                    float sc = Mathf.Lerp(sMin, sMax, Rand01(seed + 4, tries));
                    var go = Place(parent, pfs[n % pfs.Length], p, Rand01(seed + 3, tries) * 360f, sc, align, sink);
                    if (go.GetComponentInChildren<Collider>() && FootprintBlocked(go, 0.6f)) { UnityEngine.Object.DestroyImmediate(go); continue; }
                    Add(placed, p); n++;
                }
                return n;
            }
            // boulders: scattered on the open floor in loose groups (noise), bigger on the rising edges
            int nRock = Scatter(rocks, new[] { "ENV_PC_MossRock_A", "ENV_PC_MossRock_B", "ENV_PC_MossRock_C" }, 28, 14f,
                (p, ow) => Slope(p) < 24f && WaterDist(p) > 2f ? 0.15f + 0.85f * Smooth01((Noise(p.x, p.z, 30f, 41) - 0.55f) / 0.25f) : 0f, 0.35f, true, 0.7f, 2.0f, 4101, 6f, 2f, true, 2.5f);
            // giant ferns at the forest edges (no collider)
            int nFern = Scatter(props, new[] { "ENV_PC_GiantFern_A", "ENV_PC_GiantFern_B" }, 26, 7f,
                (p, ow) => { float f = EA_ForestK(s, p); return Slope(p) < 30f && (4f * f * (1f - f) > 0.5f || (ow < 0.7f && ow > 0.15f)) ? 0.7f : 0f; }, 0.05f, false, 0.8f, 1.3f, 4102, 4f, 0.5f, false, 0f);
            // fallen trunks in the edge band (the forest side), aligned
            int nTrunk = Scatter(props, new[] { "ENV_PC_FallenTrunk_A", "ENV_PC_FallenTrunk_B" }, 6, 30f,
                (p, ow) => ow > 0.1f && ow < 0.85f && Slope(p) < 16f && EA_ForestK(s, p) > 0.12f ? 0.8f : 0f, 0.12f, true, 0.85f, 1.15f, 4103, 8f, 3f, true, 2f);
            // horsetail clumps on the river banks in the valley
            int nH = Scatter(props, new[] { "ENV_PC_HorsetailClump" }, 14, 5f,
                (p, ow) => { float dw = WaterDist(p); return dw > 0.6f && dw < 4f ? 0.8f : 0f; }, 0.05f, false, 0.8f, 1.25f, 4104, 3f, 0.3f, false, 0f);
            L($"valley props: {nRock} boulders (Rocks), {nFern} giant ferns, {nTrunk} fallen trunks, {nH} horsetail clumps (Props)");

            // landmark: the rock ridge on the rising west edge (pose from EA_RidgePose: long side along the edge, +Z / moss side to the valley)
            var lmPos = _ridgePos;
            var face = Quaternion.Euler(0f, _ridgeYaw, 0f) * Vector3.forward;
            var lm = Marker(lmG, "LM_MigrationValley", lmPos, Quaternion.Euler(0f, _ridgeYaw, 0f));
            ClearGroup(lm);
            var ridge = Prefab("LM_RockRidge", false);
            if (ridge) { var go = (GameObject)PrefabUtility.InstantiatePrefab(ridge, lm); go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic); L("landmark: LM_RockRidge (ART) placed under LM_MigrationValley"); }
            else L("landmark: LM_RockRidge not delivered yet: empty LM_MigrationValley kept");
            L($"landmark LM_MigrationValley at {V(lmPos)}, yaw {F(_ridgeYaw)} (valley side {V(face)}), footprint 57 x 21 m: ground range {F(_ridgeRange)} m (buried skirt 2 m), footprint clear of route / gameplay / knoll view: {_ridgeOk}; route {F(RouteDist(lmPos))} m from the centre");

            // FX anchors for WORLD (empty transforms): meadow pollen, wallow insects, ford mist, dusty trail
            ClearGroup(fx);
            var anchors = new List<(string, Vector3)> { ("FXA_Valley_Pollen_Meadow", Ground(MeadowC) + Vector3.up * 1.5f), ("FXA_Valley_Mist_Ford", Ground(P3(Feat.water.ford)) + Vector3.up * 0.8f),
                ("FXA_Valley_Dust_Trail", Ground(new Vector3(18f, 0, -72f)) + Vector3.up * 1f), ("FXA_Valley_Pollen_Knoll", KnollTop + Vector3.up * 2f) };
            var wl = Wallows(); for (int i = 0; i < wl.Length; i++) anchors.Add(($"FXA_Valley_Insects_Wallow_{i}", Ground(wl[i]) + Vector3.up * 0.8f));
            foreach (var (n, p) in anchors) Marker(fx, n, p, Quaternion.identity);
            L("valley FX anchors: " + string.Join(", ", anchors.Select(a => $"{a.Item1} {V(a.Item2)}")));
        }

        // ------------------------------------------------------------------ check
        [PrimalBridgeCommand]
        public static string ValleyCheck(string arg)
        {
            Begin(EA, "ValleyCheck " + arg);
            var z = ZoneById("valley");
            if (!OpenIsland(out var scene, out _)) return End();
            if (!FindTerrain() || LoadFeat() == null) { W("no terrain or features"); return End(); }
            var root = ZoneRoot(z);
            int problems = CheckZone(z, new[] { root }, EA + "_valley", scene);
            // route, wallows, knoll view
            int onRoute = 0; foreach (Transform g in root) foreach (Transform c in g) if (PrefabUtility.IsOutermostPrefabInstanceRoot(c.gameObject) && RouteDist(c.position) < 3f) onRoute++;
            var own = OwnTreePositions(EA + "_valley");
            int treeRoute = own.Count(p => RouteDist(p) < 8f), treeView = own.Count(p => EA_BlocksKnollView(p, 14f, 16f));
            int oldView = 0; var slots = OwnTreeSlots(EA + "_valley"); var all = TD.treeInstances;
            for (int i = 0; i < all.Length; i++) { if (slots.Contains(i)) continue; var p = TreeWorld(all[i]); if (z.Weight(p) > 0f && EA_BlocksKnollView(p, EA_ProtoHeight(all[i].prototypeIndex) * all[i].heightScale, 7f)) oldView++; }
            L($"  route: zone props within 3 m {onRoute}, own trees within 8 m {treeRoute}; knoll view lines (ford, waterfall, meadow): own trees in the way {treeView}, other trees that cut a line {oldView} (outside the zone weight they are not moved)");
            var s = new SplatSampler(z);
            L("  wallows (mud weight at the centre): " + string.Join(", ", Wallows().Select(w => $"{V(Ground(w))} mud {F(s.Get(w, LMud))}")));
            int lTall = DetailLayer("DET_Z_TallGrass"); var dp = lTall >= 0 ? TD.detailPrototypes[lTall] : null;
            L($"  tall grass layer {lTall}: {(dp != null && dp.prototype ? dp.prototype.name + " / " + dp.prototype.GetComponentInChildren<MeshRenderer>().sharedMaterial.shader.name : "missing")}");
            L($"  terrain draw settings (scene): detail distance {F(Ter.detailObjectDistance)} m, density {F(Ter.detailObjectDensity)}, tree distance {F(Ter.treeDistance)} m, billboard {F(Ter.treeBillboardDistance)} m; runtime presets (Core/TerrainQuality) override these per quality level");
            var lm = root.Find("Landmark/LM_MigrationValley");
            L($"  landmark: {(lm ? V(lm.position) + (lm.childCount > 0 ? " with " + lm.GetChild(0).name : " (empty, waiting for ART)") : "MISSING")}");
            L($"VALLEY CHECK {(problems == 0 && onRoute == 0 && treeRoute == 0 && treeView == 0 ? "OK" : "PROBLEMS")}: {problems} problem(s)");
            return End();
        }
    }
}
