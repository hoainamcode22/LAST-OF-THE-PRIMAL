using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using PrimalFrontier.Core;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// ENV-A (EA_), Phase 2 zone 2 PREHISTORIC WETLAND (lagoon + river mouth), centre (102, 0.4, 172), r 45 + 25 m blend.
    /// Lagoon water ENV_Wetland_Water (0.85 m) is reused as it is (the flats round it already sit at 0.4-1.2 m, so no extra
    /// pools are dug; heights are not changed). Bridge commands (idempotent, each saves the scene):
    ///   Wetland "[terrain|props]" (default both)
    ///     terrain  from the EA baseline: splat (mud / wet sand flats and lagoon bed, dark soil + moss on the rim, the beach sand
    ///              kept), details (reed beds at the water line, horsetail on the flats, ferns / giant ferns on the rim, grass
    ///              thinned on the wet ground, nothing in water deeper than 0.5 m), own terrain trees (tree ferns + a few cycads
    ///              on the drier rim)
    ///     props    World/Environment/Wetlands/PrehistoricWetland/{Props, Landmark, FX_Anchors}: dead snags (ART's
    ///              PROP_P2_DeadSnag_A/B when delivered, else PROP_PC_BrokenTree_A/B), root plates, fallen trunks (some half in the
    ///              water), horsetail clumps, giant ferns; LM_PrehistoricWetland (+ ART's LM_AncientFallenTree once delivered)
    ///              on the shore, pointing into the lagoon; FX_Anchors for WORLD (fog, insects, mist)
    ///   WetlandCheck   read-only: counts, missing refs, covered nodes, what stands in deep water, landmark.
    /// </summary>
    public static partial class PrimalZonesBuilder
    {
        const float LagoonLevel = 0.85f;

        [PrimalBridgeCommand]
        public static string Wetland(string arg)
        {
            Begin(EA, "Wetland " + arg);
            var z = ZoneById("wetland");
            if (!OpenIsland(out var scene, out bool wasDirty)) return End();
            if (!FindTerrain() || LoadFeat() == null) { W("no terrain or features"); return End(); }
            bool all = !(arg.Contains("terrain") || arg.Contains("props"));
            if (all || arg.Contains("terrain"))
            {
                var bk = BackupTerrain(EA); if (!bk) return End();
                BuildBlockers(scene); _ftPos = EA_WetLandmarkSpot(z, out _ftDir);
                EA_WetSplat(z, bk);
                EA_WetDetails(z, bk);
                EA_WetTrees(z);
                Ter.Flush(); EditorUtility.SetDirty(TD); AssetDatabase.SaveAssets();
            }
            if (all || arg.Contains("props")) { BuildBlockers(scene); _ftPos = EA_WetLandmarkSpot(z, out _ftDir); EA_WetProps(z); }
            SaveIsland(scene, wasDirty);
            return End();
        }

        /// <summary>water depth under the lagoon level (negative = ground above it)</summary>
        static float WetDepth(Vector3 p) => LagoonLevel - GroundY(p);
        /// <summary>the start-beach side: sand that must stay sand (the river mouth crosses the beach)</summary>
        static float BeachK(float sandBase, Vector3 p) => Mathf.Clamp01(sandBase * 1.4f - 0.2f) * Smooth01((p.z - 186f) / 12f);

        // ------------------------------------------------------------------ splat
        static void EA_WetSplat(Zone z, TerrainData bk)
        {
            int n = EditSplat(z, bk, (p, ow, b, t) =>
            {
                float slope = Slope(p); if (slope > 32f) return;
                float depth = WetDepth(p), beach = BeachK(b[LSand], p), keep = 1f - beach;
                float n1 = Noise(p.x, p.z, 7f, 51), n2 = Noise(p.x, p.z, 16f, 52);
                // lagoon bed and flats: mud with wet sand, most on the ground within 0.9 m of the water level
                float flats = 1f - Smooth01((-depth - 0.2f) / 0.9f);                 // 1 in the water and up to 0.2 m above it, 0 by 1.1 m above
                float k = flats * keep * (0.8f + 0.2f * n1);
                if (k > 0f)
                {
                    for (int l = 0; l < t.Length; l++) t[l] *= 1f - k;
                    float mud = Mathf.Lerp(0.45f, 0.75f, n2);
                    t[LMud] += k * mud; t[LSandWet] += k * (1f - mud) * 0.85f; t[LDark] += k * (1f - mud) * 0.15f;
                }
                // the damp rim (1.1 - 4 m): dark soil and moss mixed into the forest floor / grass
                float rim = Smooth01((-depth - 0.6f) / 0.8f) * (1f - Smooth01((-depth - 3.5f) / 2.5f)) * keep;
                if (rim > 0f)
                {
                    float mv = rim * 0.45f; float fromG = t[LGrass] * mv, fromS = t[LSand] * mv * 0.6f;
                    t[LGrass] -= fromG; t[LSand] -= fromS;
                    t[LDark] += (fromG + fromS) * (0.55f + 0.2f * n1); t[LMoss] += (fromG + fromS) * (0.25f - 0.1f * n1); t[LMud] += (fromG + fromS) * 0.2f;
                }
            });
            L($"wetland splat: {n} texels written");
        }

        // ------------------------------------------------------------------ details
        static void EA_WetDetails(Zone z, TerrainData bk)
        {
            int lFern = DetailLayer("DET_ENV_Fern_01"), lGrass = DetailLayer("DET_ENV_Grass_01"), lH = DetailLayer("DET_PC_Horsetail"),
                lR = DetailLayer("DET_PC_Reeds"), lGF = DetailLayer("DET_PC_GiantFern"), lBush = DetailLayer("DET_ENV_Bush_01");
            var layers = new[] { lFern, lGrass, lH, lR, lGF, lBush };
            if (layers.Any(l => l < 0)) { W("wetland details: a detail layer is missing: " + string.Join(", ", layers)); return; }
            var s = new SplatSampler(z, bk);
            int n = EditDetails(z, bk, layers, (p, ow, b, t) =>
            {
                float depth = WetDepth(p), slope = Slope(p), beach = BeachK(s.Get(p, LSand), p), keep = 1f - beach;
                float n1 = Noise(p.x, p.z, 8f, 61), n2 = Noise(p.x, p.z, 4f, 62);
                if (depth > 0.5f) { for (int k = 0; k < t.Length; k++) t[k] = 0; return; }                     // open water: nothing sticks out
                float dry = -depth;                                                                           // height above the water
                // reeds: dense beds from 0.5 m deep to 0.4 m above the water line, in clumps
                float reedK = (1f - Smooth01((depth - 0.1f) / 0.4f)) * (1f - Smooth01((dry - 0.1f) / 0.5f));
                t[3] = Mathf.Max(Mathf.RoundToInt(b[3] * 0.5f), Mathf.RoundToInt(reedK * keep * (1f + 3.2f * Smooth01((n1 - 0.3f) / 0.4f))));
                // horsetail on the flats just above the water
                float hK = Smooth01((dry + 0.05f) / 0.3f) * (1f - Smooth01((dry - 1.4f) / 0.9f));
                t[2] = Mathf.Max(Mathf.RoundToInt(b[2] * 0.5f), Mathf.RoundToInt(hK * keep * (0.6f + 2.2f * n2)));
                // the rim: ferns and giant ferns (forest -> wetland transition), grass thinned on the wet ground
                float rim = Smooth01((dry - 1.2f) / 1.2f) * (slope < 32f ? 1f : 0f);
                t[0] = Mathf.Max(b[0], Mathf.RoundToInt(rim * keep * 2.2f * (0.5f + 0.5f * n1)));
                t[4] = Mathf.Max(b[4], Mathf.RoundToInt(rim * keep * 0.9f * Smooth01((n2 - 0.4f) / 0.4f)));
                float wetG = 1f - Smooth01((dry - 0.8f) / 1.5f);
                t[1] = Mathf.RoundToInt(b[1] * (1f - 0.85f * wetG * keep));
                t[5] = Mathf.RoundToInt(b[5] * (1f - wetG * keep));
            }, out long before, out long after);
            L($"wetland details: {n} texels, layers fern {lFern} grass {lGrass} horsetail {lH} reeds {lR} giantfern {lGF} bush {lBush}; instances in the rect {before} -> {after}");
        }

        // ------------------------------------------------------------------ trees
        static void EA_WetTrees(Zone z)
        {
            string key = EA + "_wetland";
            var ferns = new[] { "ENV_PC_TreeFern_A", "ENV_PC_TreeFern_B", "ENV_PC_TreeFern_C" }.Select(nm => TreeProto(nm)).Where(i => i >= 0).ToArray();
            var cycads = new[] { "ENV_PC_Cycad_A", "ENV_PC_Cycad_B", "ENV_PC_Cycad_C" }.Select(nm => TreeProto(nm)).Where(i => i >= 0).ToArray();
            if (ferns.Length == 0 || cycads.Length == 0) { W("wetland trees: prototypes missing"); return; }
            var grid = TreeGrid(OwnTreeSlots(key));
            var s = new SplatSampler(z);
            var mine = new List<TreeInstance>(); var mineGrid = new Dictionary<long, List<Vector3>>();
            int rj = 0; const float step = 5f; var b = z.Bounds;
            for (float gz = b.yMin; gz < b.yMax && mine.Count < 40; gz += step)
                for (float gx = b.xMin; gx < b.xMax && mine.Count < 40; gx += step)
                {
                    int ix = Mathf.RoundToInt(gx), iz = Mathf.RoundToInt(gz);
                    var p = new Vector3(gx + Rand01(ix + 3, iz * 3 + 7) * step, 0f, gz + Rand01(ix * 5 + 9, iz) * step);
                    float ow = OwnWeight(z, p); if (ow <= 0.1f) continue;
                    p.y = GroundY(p); float dry = p.y - LagoonLevel, slope = Slope(p);
                    if (dry < 0.9f || dry > 6f || slope > 24f || BeachK(s.Get(p, LSand), p) > 0.3f) continue;
                    float pr = 0.32f * Smooth01((Noise(p.x, p.z, 18f, 71) - 0.35f) / 0.35f) * (1f - Smooth01((dry - 3.5f) / 2.5f));
                    float r = Rand01(ix * 7 + 1, iz * 13 + 3); if (r >= pr) continue;
                    if (IsBlocked(p, 1.8f) || Near(grid, p, 4.5f, 5f) || Near(mineGrid, p, 5f, 5f) || WaterDist(p) < 1.5f || EA_InFallenTree(p, 5f)) { rj++; continue; }
                    bool cy = Rand01(ix, iz * 29) < 0.25f;
                    int proto = cy ? cycads[Mod(ix + iz, cycads.Length)] : ferns[Mod(ix * 3 + iz, ferns.Length)];
                    float sc = 0.85f + Rand01(ix * 11, iz) * 0.45f;
                    mine.Add(MakeTree(proto, p, Rand01(ix * 3, iz * 5) * Mathf.PI * 2f, sc, sc * (0.92f + Rand01(iz, ix) * 0.16f)));
                    Add(mineGrid, p, 5f);
                }
            L($"wetland trees: {mine.Count} own terrain trees (tree fern {mine.Count(t => ferns.Contains(t.prototypeIndex))}, cycad {mine.Count(t => cycads.Contains(t.prototypeIndex))}), rejected {rj}");
            SetOwnTrees(key, mine);
        }

        // ------------------------------------------------------------------ props
        static void EA_WetProps(Zone z)
        {
            var root = ZoneRoot(z);
            var props = Group(z, "Props"); ClearGroup(props);
            var lmG = Group(z, "Landmark"); var fx = Group(z, "FX_Anchors");
            var trees = TreeGrid(null);
            var placed = new Dictionary<long, List<Vector3>>();
            Physics.SyncTransforms();
            var lmPos = _ftPos; var lmDir = _ftDir;
            int Scatter(string[] names, int want, float spacing, System.Func<Vector3, float, float> density, float sink, bool align, float sMin, float sMax, int seed, float blockExtra, bool solidTest)
            {
                var pfs = names.Select(nm => Prefab(nm, false)).Where(pf => pf).ToArray(); if (pfs.Length == 0) { W("wetland: no prefab of " + string.Join("/", names)); return 0; }
                int n = 0, tries = 0; var bb = z.Bounds;
                while (n < want && tries < want * 500)
                {
                    tries++;
                    var p = new Vector3(Mathf.Lerp(bb.xMin, bb.xMax, Rand01(seed, tries * 2)), 0f, Mathf.Lerp(bb.yMin, bb.yMax, Rand01(seed + 1, tries * 2 + 1)));
                    float ow = OwnWeight(z, p); if (ow <= 0.1f) continue;
                    p.y = GroundY(p);
                    float d = density(p, ow); if (d <= 0f || Rand01(seed + 2, tries) > d) continue;
                    if (Near(placed, p, spacing) || IsBlocked(p, blockExtra) || Near(trees, p, 2f, 5f)) continue;
                    if (EA_InFallenTree(p, 2.5f)) continue;                                          // the landmark lies alone
                    if (solidTest && SolidAt(p, 1.2f, root)) continue;
                    float sc = Mathf.Lerp(sMin, sMax, Rand01(seed + 4, tries));
                    var go = Place(props, pfs[n % pfs.Length], p, Rand01(seed + 3, tries) * 360f, sc, align, sink);
                    if (go.GetComponentInChildren<Collider>() && FootprintBlocked(go, 0.6f)) { UnityEngine.Object.DestroyImmediate(go); continue; }
                    Add(placed, p); n++;
                }
                return n;
            }
            string[] snags = HasPrefab("PROP_P2_DeadSnag_A") ? new[] { "PROP_P2_DeadSnag_A", "PROP_P2_DeadSnag_B" } : new[] { "PROP_PC_BrokenTree_A", "PROP_PC_BrokenTree_B" };
            float sandOk(Vector3 p) => BeachK(0.8f, p) > 0.5f ? 0f : 1f;
            // dead snags: on the flats and standing in the shallow water (drowned trees)
            int nSnag = Scatter(snags, 11, 11f, (p, ow) => { float dep = WetDepth(p); return dep < 0.35f && dep > -1.6f && Slope(p) < 16f ? 0.8f * sandOk(p) : 0f; }, 0.25f, false, 0.85f, 1.3f, 5101, 1.5f, true);
            // root plates on the damp rim
            int nRoot = Scatter(new[] { "ENV_PC_Roots_A", "ENV_PC_Roots_B" }, 8, 10f, (p, ow) => { float dry = -WetDepth(p); return dry > 0.2f && dry < 2.5f && Slope(p) < 16f ? 0.7f * sandOk(p) : 0f; }, 0.15f, false, 0.8f, 1.2f, 5102, 2.6f, true);
            // fallen trunks: flats and half in the water
            int nTrunk = Scatter(new[] { "ENV_PC_FallenTrunk_A", "ENV_PC_FallenTrunk_B" }, 7, 11f, (p, ow) => { float dep = WetDepth(p); return dep < 0.3f && dep > -1.8f && Slope(p) < 16f ? 0.8f * sandOk(p) : 0f; }, 0.2f, true, 0.85f, 1.2f, 5103, 3f, true);
            // horsetail clumps at the water line, giant ferns on the rim
            int nH = Scatter(new[] { "ENV_PC_HorsetailClump" }, 16, 5f, (p, ow) => { float dep = WetDepth(p); return dep < 0.15f && dep > -0.9f ? 0.8f * sandOk(p) : 0f; }, 0.05f, false, 0.8f, 1.3f, 5104, 0.3f, false);
            int nGF = Scatter(new[] { "ENV_PC_GiantFern_A", "ENV_PC_GiantFern_B" }, 12, 6f, (p, ow) => { float dry = -WetDepth(p); return dry > 1.2f && dry < 5f && Slope(p) < 28f ? 0.7f * sandOk(p) : 0f; }, 0.05f, false, 0.8f, 1.3f, 5105, 0.5f, false);
            L($"wetland props: {nSnag} dead snags ({snags[0]}/..), {nRoot} root plates, {nTrunk} fallen trunks, {nH} horsetail clumps, {nGF} giant ferns");

            // landmark: the ancient fallen tree, root end on the shore, trunk reaching into the lagoon
            var lm = Marker(lmG, "LM_PrehistoricWetland", lmPos, Quaternion.LookRotation(lmDir, Vector3.up));
            ClearGroup(lm);
            var tree = Prefab("LM_AncientFallenTree", false);
            // ART: trunk along local X (root plate at -X, crown at +X), pivot base centre, y 0 = the water line: turned so +X runs along the marker's +Z,
            // centred 17.5 m out from the root end, pivot at the lagoon level
            if (tree) { var go = (GameObject)PrefabUtility.InstantiatePrefab(tree, lm); go.transform.localPosition = new Vector3(0f, LagoonLevel - lmPos.y, FtCentre); go.transform.localRotation = Quaternion.Euler(0f, -90f, 0f); GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic); L("landmark: LM_AncientFallenTree (ART) placed under LM_PrehistoricWetland"); }
            else L("landmark: LM_AncientFallenTree not delivered yet: empty LM_PrehistoricWetland kept (its +z points from the root end into the water)");
            var tip = lmPos + lmDir * (FtCentre + 19f);
            L($"landmark LM_PrehistoricWetland (root end) at {V(lmPos)}, trunk direction {V(lmDir)}, tree pivot {V(new Vector3(lmPos.x, LagoonLevel, lmPos.z) + lmDir * FtCentre)} at the water line, crown end {V(Ground(tip))} (lagoon bed {F(GroundY(tip))}, water {F(LagoonLevel)})");

            // FX anchors for WORLD: lagoon fog, reed-bed insects, river-mouth mist, landmark
            ClearGroup(fx);
            var anchors = new List<(string, Vector3)>();
            var blobs = Feat.water.wetland.blobs;
            anchors.Add(("FXA_Wetland_Fog_Lagoon", new Vector3(100f, LagoonLevel + 0.6f, 168f)));
            if (blobs != null && blobs.Length > 2) { var w = P3(blobs[2].center); anchors.Add(("FXA_Wetland_Fog_West", new Vector3(w.x, LagoonLevel + 0.6f, w.z))); }
            if (blobs != null && blobs.Length > 1) { var e = P3(blobs[1].center); anchors.Add(("FXA_Wetland_Fog_East", new Vector3(e.x, LagoonLevel + 0.6f, e.z))); }
            anchors.Add(("FXA_Wetland_Mist_RiverMouth", Ground(new Vector3(106f, 0f, 200f)) + Vector3.up * 0.8f));
            anchors.Add(("FXA_Wetland_Insects_Landmark", lmPos + lmDir * 6f + Vector3.up * 1.5f));
            // reed beds: the three densest reed spots, at least 20 m apart
            int lR = DetailLayer("DET_PC_Reeds");
            if (lR >= 0)
            {
                var cand = new List<(float, Vector3)>();
                for (float gz = z.centre.z - z.radius; gz <= z.centre.z + z.radius; gz += 6f)
                    for (float gx = z.centre.x - z.radius; gx <= z.centre.x + z.radius; gx += 6f)
                    {
                        var p = new Vector3(gx, 0f, gz); if (z.Weight(p) < 1f) continue;
                        var tp = TPos; int res = TD.detailResolution;
                        int dx = Mathf.Clamp(Mathf.FloorToInt((gx - tp.x) / TD.size.x * res) - 2, 0, res - 5), dz = Mathf.Clamp(Mathf.FloorToInt((gz - tp.z) / TD.size.z * res) - 2, 0, res - 5);
                        int sum = 0; foreach (var v in TD.GetDetailLayer(dx, dz, 5, 5, lR)) sum += v;
                        cand.Add((sum, p));
                    }
                int k = 0;
                foreach (var c in cand.OrderByDescending(q => q.Item1))
                {
                    if (anchors.Any(a => a.Item1.StartsWith("FXA_Wetland_Insects_Reeds") && new Vector2(a.Item2.x - c.Item2.x, a.Item2.z - c.Item2.z).magnitude < 20f)) continue;
                    anchors.Add(($"FXA_Wetland_Insects_Reeds_{k}", new Vector3(c.Item2.x, Mathf.Max(LagoonLevel, GroundY(c.Item2)) + 1.2f, c.Item2.z)));
                    if (++k >= 3) break;
                }
            }
            foreach (var (n, p) in anchors) Marker(fx, n, p, Quaternion.identity);
            L("wetland FX anchors: " + string.Join(", ", anchors.Select(a => $"{a.Item1} {V(a.Item2)}")));
        }

        // ------------------------------------------------------------------ fallen tree pose (ART LM_AncientFallenTree: 38.3 x 8 x 10.7 m, long axis local X)
        static Vector3 _ftPos, _ftDir = Vector3.forward;
        const float FtCentre = 17.5f, FtHalfLen = 19.2f, FtHalfW = 5.4f;
        /// <summary>inside the tree's footprint (root end _ftPos, along _ftDir)</summary>
        static bool EA_InFallenTree(Vector3 p, float margin)
        {
            var d = p - _ftPos; d.y = 0f; float along = Vector3.Dot(d, _ftDir) - FtCentre, across = Mathf.Abs(Vector3.Dot(d, new Vector3(_ftDir.z, 0f, -_ftDir.x)));
            return Mathf.Abs(along) < FtHalfLen + margin && across < FtHalfW + margin;
        }
        /// <summary>the shore spot for the fallen tree: dry root end (0.25-1.4 m above the water), the crown half under water, the whole footprint
        /// clear of gameplay objects and inside the zone; nearest to the beach approach from the spawn</summary>
        static Vector3 EA_WetLandmarkSpot(Zone z, out Vector3 dir)
        {
            var c = new Vector3(100f, 0f, 168f); var view = new Vector3(78f, 0f, 200f);   // lagoon centre, the beach approach from the spawn
            Vector3 best = Ground(c + new Vector3(-20f, 0f, 20f)); float bestS = -1e9f; dir = (c - best); dir.y = 0f; dir.Normalize();
            int tried = 0, rjBlock = 0, rjWet = 0;
            for (int a = 0; a < 360; a += 4)
            {
                var d = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                for (float r = 14f; r < 44f; r += 1f)
                {
                    var p = c + d * r; float y = GroundY(p);
                    if (y < LagoonLevel + 0.25f || y > LagoonLevel + 1.4f || Slope(p) > 12f) continue;
                    tried++;
                    var inward = -d;
                    int wet = 0, samples = 0;
                    float yaw = Quaternion.LookRotation(inward, Vector3.up).eulerAngles.y;             // local +Z = inward
                    bool bad = EA_RectBlocked(p + inward * FtCentre, yaw, 2.2f, FtHalfLen, 0.6f)      // trunk (about 4 m thick)
                            || EA_RectBlocked(p + inward * 0.5f, yaw, FtHalfW, 3f, 0.6f)               // root plate
                            || z.Weight(p + inward * (FtCentre + FtHalfLen)) < 0.6f || z.Weight(p - inward * 2f) < 0.6f;
                    for (float s = FtCentre; s <= FtCentre + FtHalfLen; s += 3f) { samples++; if (GroundY(p + inward * s) < LagoonLevel - 0.1f) wet++; }
                    if (bad) { rjBlock++; continue; }
                    if (wet * 2 < samples || SolidAt(p, 2f, ZoneRoot(z))) { rjWet++; continue; }
                    float score = -new Vector2(p.x - view.x, p.z - view.z).magnitude + wet * 2f;
                    if (score > bestS) { bestS = score; best = Ground(p); dir = inward; }
                    break;                                                                     // first dry shore along this ray
                }
            }
            L($"fallen tree spot: {tried} shore candidates, {rjBlock} footprint on gameplay objects / outside the zone core, {rjWet} crown not in the water; chosen {V(best)} dir {V(dir)}{(bestS < -1e8f ? " (FALLBACK, no clear spot)" : "")}");
            if (bestS < -1e8f) W("fallen tree: no shore spot with a clear footprint");
            return best;
        }

        // ------------------------------------------------------------------ check
        [PrimalBridgeCommand]
        public static string WetlandCheck(string arg)
        {
            Begin(EA, "WetlandCheck " + arg);
            var z = ZoneById("wetland");
            if (!OpenIsland(out var scene, out _)) return End();
            if (!FindTerrain() || LoadFeat() == null) { W("no terrain or features"); return End(); }
            var root = ZoneRoot(z);
            int problems = CheckZone(z, new[] { root }, EA + "_wetland", scene);
            int deep = 0; var props = root.Find("Props");
            if (props) foreach (Transform c in props) if (WetDepth(c.position) > 0.6f) { deep++; L($"    in deep water: {c.name} {V(c.position)}"); }
            L($"  props standing in water deeper than 0.6 m: {deep}");
            var water = SceneRoots.Find(SceneRoots.Wetlands + "/ENV_Wetland_Water");
            L($"  lagoon water ENV_Wetland_Water: {(water ? (water.gameObject.activeInHierarchy ? "on" : "OFF") + " at " + V(water.position) : "MISSING")}");
            var lm = root.Find("Landmark/LM_PrehistoricWetland");
            L($"  landmark: {(lm ? V(lm.position) + (lm.childCount > 0 ? " with " + lm.GetChild(0).name : " (empty, waiting for ART)") : "MISSING")}");
            var fx = root.Find("FX_Anchors"); L($"  FX anchors: {(fx ? fx.childCount : 0)}{(fx ? ": " + string.Join(", ", fx.Cast<Transform>().Select(t => t.name + " " + V(t.position))) : "")}");
            L($"WETLAND CHECK {(problems == 0 && deep == 0 ? "OK" : "PROBLEMS")}: {problems} problem(s)");
            return End();
        }
    }
}
