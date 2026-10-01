using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// ENV-B (EB_) Phase 2, zone 4 GIANT FERN FOREST on the old deep_forest (168, 8, 20), r 70 + 30 m blend (core Zones "fern").
    /// Bridge command <c>PrimalZonesBuilder.FernForest</c> ("dry" = survey only, "clearing=x,z" moves the landmark clearing):
    ///   terrain (from the EB backup baseline, OwnWeight blended): damp forest floor / moss splat, worn trail lines, a grass and
    ///   moss clearing; dense understory details (giant fern, fern, bush) with open lanes on the trails and in the clearing;
    ///   own terrain trees (tree ferns, cycads, a few tall araucarias for the canopy, key "EB_fern"); placed giant fern clumps,
    ///   magnolia shrubs, fallen trunks, root plates, moss rocks and vine curtains on tree fern trunks under
    ///   World/Environment/Forest/GiantFernForest; trail waypoints (Trails/Trail_*/WP_nn) for AI; FX anchors for WORLD;
    ///   the landmark placeholder LM_GiantFernForest (ART's LM_GiantTree is put under it once the prefab exists).
    /// Shared EB helpers (trails, scatter, prefab bounds, splat mix) live here too; <c>FernFoothillsCheck</c> is read-only.
    /// </summary>
    public static partial class PrimalZonesBuilder
    {
        // ================================================================== EB shared helpers
        public sealed class EBTrail
        {
            public string name, note; public float halfWidth; public Vector3[] pts;
            public float Dist(Vector3 p) { float best = 1e9f; for (int i = 0; i + 1 < pts.Length; i++) best = Mathf.Min(best, SegDist(p, pts[i], pts[i + 1])); return best; }
            public float Length { get { float l = 0f; for (int i = 0; i + 1 < pts.Length; i++) l += Vector3.Distance(pts[i], pts[i + 1]); return l; } }
        }

        static float EBRamp(float s, float a, float b) => Smooth01((s - a) / Mathf.Max(1e-4f, b - a));
        static float EBFlat(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return Mathf.Sqrt(dx * dx + dz * dz); }
        static int EBLayer(string name) { var ls = TD.terrainLayers; for (int i = 0; i < ls.Length; i++) if (ls[i] && ls[i].name == name) return i; return -1; }

        /// <summary>t = t * (1 - k) + mix * k; mix as (layer, weight) pairs, layers &lt; 0 ignored</summary>
        static void EBMix(float[] t, float k, params float[] pairs)
        {
            k = Mathf.Clamp01(k); if (k <= 0f) return;
            float sum = 0f; for (int i = 0; i + 1 < pairs.Length; i += 2) if ((int)pairs[i] >= 0 && (int)pairs[i] < t.Length) sum += pairs[i + 1];
            if (sum <= 1e-4f) return;
            for (int l = 0; l < t.Length; l++) t[l] *= 1f - k;
            for (int i = 0; i + 1 < pairs.Length; i += 2) { int l = (int)pairs[i]; if (l >= 0 && l < t.Length) t[l] += pairs[i + 1] / sum * k; }
        }

        /// <summary>stochastic rounding, deterministic per position</summary>
        static int EBRound(float v, Vector3 w, int salt)
        {
            if (v <= 0f) return 0; int i = (int)v;
            if (Rand01(Mathf.RoundToInt(w.x * 8f) + salt * 131, Mathf.RoundToInt(w.z * 8f) - salt * 17) < v - i) i++;
            return Mathf.Clamp(i, 0, 255);
        }

        static Vector3 EBXZ(Dictionary<string, string> a, string key, Vector2 def)
        {
            if (a.TryGetValue(key, out var s))
            {
                var c = s.Split(',');
                if (c.Length >= 2 && float.TryParse(c[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && float.TryParse(c[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)) return new Vector3(x, 0f, z);
                W($"bad {key}={s}, default used");
            }
            return new Vector3(def.x, 0f, def.y);
        }

        static readonly Dictionary<GameObject, Bounds> _ebBounds = new Dictionary<GameObject, Bounds>();
        /// <summary>LOD0 mesh bounds of a prefab in its root space (scale 1)</summary>
        static Bounds EBPrefabBounds(GameObject pf)
        {
            if (!pf) return new Bounds(Vector3.up * 0.5f, Vector3.one);
            if (_ebBounds.TryGetValue(pf, out var b)) return b;
            bool has = false; b = new Bounds();
            var lg = pf.GetComponentInChildren<LODGroup>(true);
            IEnumerable<Renderer> rs = lg && lg.GetLODs().Length > 0 ? lg.GetLODs()[0].renderers.Where(r => r) : pf.GetComponentsInChildren<Renderer>(true);
            var inv = pf.transform.worldToLocalMatrix;
            foreach (var r in rs)
            {
                var mf = r.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue;
                var mb = mf.sharedMesh.bounds; var m = inv * r.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    var w = m.MultiplyPoint3x4(c);
                    if (!has) { b = new Bounds(w, Vector3.zero); has = true; } else b.Encapsulate(w);
                }
            }
            if (!has) b = new Bounds(Vector3.up * 0.5f, Vector3.one);
            _ebBounds[pf] = b; return b;
        }

        static long EBPosKey(Vector3 p) => ((long)Mathf.RoundToInt(p.x * 10f) << 32) ^ (uint)Mathf.RoundToInt(p.z * 10f);
        /// <summary>every live terrain tree except this agent's own recorded ones (4 m grid)</summary>
        static Dictionary<long, List<Vector3>> EBTreeGrid(params string[] ownKeys)
        {
            var own = new HashSet<long>();
            foreach (var k in ownKeys) foreach (var p in OwnTreePositions(k)) own.Add(EBPosKey(p));
            var g = new Dictionary<long, List<Vector3>>();
            foreach (var t in TD.treeInstances) { if (t.widthScale < 0.001f) continue; var p = TreeWorld(t); if (own.Contains(EBPosKey(p))) continue; Add(g, p, 4f); }
            return g;
        }
        static int EBCount(Dictionary<long, List<Vector3>> g, Vector3 p, float r, float cell = 4f)
        {
            int n = 0, cx = Mathf.FloorToInt(p.x / cell), cz = Mathf.FloorToInt(p.z / cell), k = Mathf.CeilToInt(r / cell);
            for (int dx = -k; dx <= k; dx++) for (int dz = -k; dz <= k; dz++)
                if (g.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out var l))
                    foreach (var q in l) { float ex = q.x - p.x, ez = q.z - p.z; if (ex * ex + ez * ez < r * r) n++; }
            return n;
        }

        public delegate float EBDensity(Vector3 p);
        /// <summary>clearance test at p for a footprint radius r</summary>
        public delegate bool EBOk(Vector3 p, float r);

        /// <summary>
        /// random scatter in an area; long = check both ends of the long axis (fallen trunks). Returns the placed objects.
        /// </summary>
        static List<GameObject> EBScatter(Transform parent, string[] names, int want, float spacing, EBDensity density, EBOk ok, float sMin, float sMax,
            float sink, bool align, int seed, Rect area, Dictionary<long, List<Vector3>> placed, bool isLong = false, bool colliderProp = false, Transform ignoreRoot = null)
        {
            var res = new List<GameObject>();
            var pfs = names.Select(n => Prefab(n)).Where(p => p).ToArray(); if (pfs.Length == 0 || want <= 0) return res;
            int tries = 0;
            while (res.Count < want && tries < want * 300)
            {
                tries++;
                var p = new Vector3(area.xMin + Rand01(seed, tries * 2) * area.width, 0f, area.yMin + Rand01(seed + 1, tries * 2 + 1) * area.height);
                float d = density(p); if (d <= 0f || Rand01(seed + 2, tries) > d) continue;
                if (Near(placed, p, spacing)) continue;
                var pf = pfs[(res.Count + tries) % pfs.Length];
                float s = Mathf.Lerp(sMin, sMax, Rand01(seed + 4, tries)), yaw = Rand01(seed + 3, tries) * 360f;
                var bb = EBPrefabBounds(pf);
                if (isLong)
                {
                    bool alongX = bb.extents.x >= bb.extents.z;
                    var axis = Quaternion.Euler(0f, yaw, 0f) * (alongX ? Vector3.right : Vector3.forward);
                    float half = (alongX ? bb.extents.x : bb.extents.z) * s, thick = (alongX ? bb.extents.z : bb.extents.x) * s;
                    var ctr = p + Quaternion.Euler(0f, yaw, 0f) * new Vector3(bb.center.x, 0f, bb.center.z) * s;
                    var e0 = ctr + axis * half; var e1 = ctr - axis * half;
                    if (!ok(ctr, thick) || !ok(e0, thick) || !ok(e1, thick) || !ok((ctr + e0) * 0.5f, thick) || !ok((ctr + e1) * 0.5f, thick)) continue;
                    if (Mathf.Abs(GroundY(e0) - GroundY(e1)) > half * 0.5f) continue;             // too steep along the trunk
                    if (colliderProp && (SolidAt(e0, thick + 0.3f, ignoreRoot) || SolidAt(e1, thick + 0.3f, ignoreRoot) || SolidAt(ctr, thick + 0.3f, ignoreRoot))) continue;
                }
                else
                {
                    float rad = Mathf.Max(bb.extents.x, bb.extents.z) * s;
                    if (!ok(p, rad)) continue;
                    if (colliderProp && SolidAt(p, Mathf.Min(rad, 2.5f) + 0.3f, ignoreRoot)) continue;
                }
                var go = Place(parent, pf, p, yaw, s, align, sink);
                if (go) { res.Add(go); Add(placed, p); }
            }
            return res;
        }

        /// <summary>best-scoring random spot (score &lt;= 0 rejected), at least minSep from the taken ones</summary>
        static bool EBFindSpot(Func<Vector3, float> score, Rect area, int seed, int tries, List<Vector3> taken, float minSep, out Vector3 best)
        {
            best = Vector3.zero; float bs = 0f;
            for (int i = 0; i < tries; i++)
            {
                var p = new Vector3(area.xMin + Rand01(seed, i * 2) * area.width, 0f, area.yMin + Rand01(seed + 1, i * 2 + 1) * area.height);
                if (taken.Any(q => EBFlat(q, p) < minSep)) continue;
                float s = score(p); if (s > bs) { bs = s; best = Ground(p); }
            }
            if (bs <= 0f) return false;
            taken.Add(best); return true;
        }

        static Transform EBTrailMarkers(Transform parent, IList<EBTrail> trails)
        {
            var root = Group2(parent, "Trails");
            foreach (var t in trails)
            {
                var g = Group2(root, t.name);
                for (int i = 0; i < t.pts.Length; i++)
                {
                    var next = i + 1 < t.pts.Length ? t.pts[i + 1] : t.pts[i] + (t.pts[i] - t.pts[Mathf.Max(0, i - 1)]);
                    var dir = next - t.pts[i]; dir.y = 0f;
                    Marker(g, $"WP_{i:00}", t.pts[i], dir.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(dir.normalized, Vector3.up) : Quaternion.identity);
                }
            }
            return root;
        }
        static Transform Group2(Transform parent, string name) { var t = parent.Find(name); if (!t) { t = new GameObject(name).transform; t.SetParent(parent, false); } return t; }

        static string EBModelCounts(Transform root)
        {
            var d = new SortedDictionary<string, int>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)) continue;
                var src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(t.gameObject); string n = src ? src.name : "?";
                d.TryGetValue(n, out var v); d[n] = v + 1;
            }
            return string.Join(", ", d.Select(kv => kv.Key + " " + kv.Value));
        }

        static long[] EBDetailTotals()
        {
            int res = TD.detailResolution, n = TD.detailPrototypes.Length; var tot = new long[n];
            for (int l = 0; l < n; l++) foreach (var v in TD.GetDetailLayer(0, 0, res, res, l)) tot[l] += v;
            return tot;
        }

        // ================================================================== Zone 4: Giant Fern Forest
        static readonly Vector2 FernClearingDefault = new Vector2(160f, 24f);
        const float FernClearingR = 11f;
        static Vector3 _fernClearing;
        static List<EBTrail> _fernTrails;

        /// <summary>points on the clearing ring (radius R - 3) that take a trail round the landmark instead of through it</summary>
        static IEnumerable<Vector3> FernRing(Vector3 c, Vector3 from, Vector3 to, bool end)
        {
            float r = FernClearingR - 3f;
            float aIn = Mathf.Atan2(from.x - c.x, from.z - c.z);
            yield return Ground(c + new Vector3(Mathf.Sin(aIn), 0f, Mathf.Cos(aIn)) * r);
            if (end) yield break;
            float aOut = Mathf.Atan2(to.x - c.x, to.z - c.z), d = Mathf.DeltaAngle(aIn * Mathf.Rad2Deg, aOut * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            for (int k = 1; k <= 2; k++) { float a = aIn + d * k / 3f; yield return Ground(c + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * r); }
            yield return Ground(c + new Vector3(Mathf.Sin(aOut), 0f, Mathf.Cos(aOut)) * r);
        }

        static List<EBTrail> FernTrails(Vector3 c)
        {
            Vector3 P(float x, float z) => Ground(new Vector3(x, 0f, z));
            EBTrail MkT(string name, float hw, string note, Vector3[] before, Vector3[] after)
            {
                var pts = new List<Vector3>(before);
                pts.AddRange(FernRing(c, before[before.Length - 1], after.Length > 0 ? after[0] : c, after.Length == 0));
                pts.AddRange(after);
                return new EBTrail { name = name, halfWidth = hw, note = note, pts = pts.ToArray() };
            }
            return new List<EBTrail>
            {
                MkT("Trail_A_RiverApproach", 1.5f, "main lane west -> east: river side (96, 6) -> clearing -> coast side (210, 8); player / herd width",
                    new[] { P(96, 6), P(108, 10), P(120, 9), P(132, 15), P(144, 20) }, new[] { P(174, 27), P(186, 22), P(198, 14), P(210, 8) }),
                MkT("Trail_B_GameTrail", 1.2f, "game trail south -> north: from the old camp side (130, -46) -> clearing -> (185, 82)",
                    new[] { P(130, -46), P(138, -32), P(147, -16), P(154, -2) }, new[] { P(164, 38), P(170, 52), P(178, 66), P(185, 82) }),
                MkT("Trail_C_StalkerPath", 0.9f, "narrow winding path through the thickest understory (stealth / ambush): (100, 24) -> clearing",
                    new[] { P(100, 24), P(108, 34), P(118, 44), P(130, 51), P(142, 52), P(152, 44) }, new Vector3[0]),
            };
        }
        /// <summary>metres beyond the nearest trail edge (negative on a trail)</summary>
        static float FernTrailClear(Vector3 p) { float best = 1e9f; foreach (var t in _fernTrails) best = Mathf.Min(best, t.Dist(p) - t.halfWidth); return best; }
        /// <summary>1 on the trail centre, 0 from hw + 1 m</summary>
        static float FernTrailFactor(Vector3 p)
        {
            float best = 0f;
            foreach (var t in _fernTrails) { float d = t.Dist(p); best = Mathf.Max(best, 1f - Smooth01((d - 0.5f * t.halfWidth) / (0.5f * t.halfWidth + 1f))); }
            return best;
        }

        [PrimalBridgeCommand]
        public static string FernForest(string arg)
        {
            var a = Args(arg); bool dry = a.ContainsKey("dry");
            Begin("EB", "FernForest " + arg);
            if (!OpenIsland(out var scene, out bool wasDirty)) return End();
            if (!FindTerrain() || LoadFeat() == null) { W("no terrain or features"); return End(); }
            var z = ZoneById("fern"); if (z == null) { W("zone fern missing in the core"); return End(); }
            _fernClearing = Ground(EBXZ(a, "clearing", FernClearingDefault));
            _fernTrails = FernTrails(_fernClearing);
            int blockers = BuildBlockers(scene);
            var others = EBTreeGrid("EB_fern");
            L($"zone {z.id}: centre {V(z.centre)} r {F(z.radius)} blend {F(z.blend)}; clearing {V(_fernClearing)} r {F(FernClearingR)}; blockers {blockers}");
            if (dry) { FernSurvey(z, others); return End(); }

            var bk = BackupTerrain("EB"); if (!bk) return End();
            var area = z.Bounds;
            var camp = Feat.props != null ? P3(Feat.props.oldCamp) : new Vector3(122f, 11f, -66f);

            // ---- splat
            int iSand = EBLayer("TL_Sand"), iGrass = EBLayer("TL_Grass"), iFF = EBLayer("TL_ForestFloor"), iMud = EBLayer("TL_Mud"), iSW = EBLayer("TL_SandWet"),
                iMoss = EBLayer("TL_Moss"), iDark = EBLayer("TL_DarkSoil");
            int nSplat = EditSplat(z, bk, (w, ow, b, t) =>
            {
                float sandy = (iSand >= 0 ? b[iSand] : 0f) + (iSW >= 0 ? b[iSW] : 0f);
                if (w.y < 2.2f || sandy > 0.55f || Slope(w) > 34f) return;
                float n = Noise(w.x, w.z, 13f, 41), coast = Smooth01((w.y - 2.2f) / 2f);
                EBMix(t, 0.8f * coast, iFF, 0.55f, iMoss, 0.12f + 0.25f * n, iDark, 0.13f, iGrass, 0.2f * (1f - n));
                float tf = FernTrailFactor(w);
                if (tf > 0f) EBMix(t, 0.6f * tf, iDark, 0.45f, iMud, 0.2f, iFF, 0.35f);
                float cf = 1f - Smooth01((EBFlat(w, _fernClearing) - FernClearingR + 3f) / 6f);
                if (cf > 0f) EBMix(t, 0.75f * cf, iGrass, 0.45f, iMoss, 0.35f, iFF, 0.2f);
            });
            L($"splat: {nSplat} texels (forest floor + moss, worn trails, clearing grass / moss)");

            // ---- details
            int dFern = DetailLayer("DET_ENV_Fern_01"), dGrass = DetailLayer("DET_ENV_Grass_01"), dBush = DetailLayer("DET_ENV_Bush_01"), dGF = DetailLayer("DET_PC_GiantFern");
            var layers = new[] { dFern, dGrass, dBush, dGF }.Where(i => i >= 0).ToArray();
            int kF = Array.IndexOf(layers, dFern), kG = Array.IndexOf(layers, dGrass), kB = Array.IndexOf(layers, dBush), kGF = Array.IndexOf(layers, dGF);
            int nDet = EditDetails(z, bk, layers, (w, ow, b, t) =>
            {
                if (w.y < 2.4f || Slope(w) > 34f || WaterDist(w) < 1.5f) return;
                float coast = Smooth01((w.y - 2.4f) / 2f);
                float n = Noise(w.x, w.z, 9f, 51), thick = Smooth01((n - 0.28f) / 0.45f), n2 = Noise(w.x, w.z, 23f, 52);
                var v = new float[t.Length]; for (int i = 0; i < v.Length; i++) v[i] = b[i];
                if (kGF >= 0) v[kGF] = Mathf.Max(b[kGF], Mathf.Lerp(b[kGF], 0.5f + 1.5f * thick, coast));
                if (kF >= 0) v[kF] = Mathf.Max(b[kF], Mathf.Lerp(b[kF], 1.5f + 1.2f * (1f - thick), coast));
                if (kB >= 0) v[kB] = Mathf.Max(b[kB], Mathf.Lerp(b[kB], thick * n2 * 1.4f, coast));
                if (kG >= 0) v[kG] = b[kG] * Mathf.Lerp(1f, 0.35f, coast);
                float tc = FernTrailClear(w);
                if (tc < 1.5f) { float k = tc < 0f ? 0.06f : Mathf.Lerp(0.3f, 1f, tc / 1.5f); for (int i = 0; i < v.Length; i++) v[i] *= k; }
                float cd = EBFlat(w, _fernClearing) - FernClearingR;
                if (cd < 3f)
                {
                    float k = cd < 0f ? 0f : cd / 3f;
                    if (kGF >= 0) v[kGF] *= k; if (kB >= 0) v[kB] *= k; if (kF >= 0) v[kF] *= Mathf.Lerp(0.3f, 1f, k);
                    if (kG >= 0) v[kG] = Mathf.Max(v[kG], 1.5f * (1f - k));
                }
                for (int i = 0; i < v.Length; i++) t[i] = EBRound(v[i], w, 60 + i);
            }, out long dBefore, out long dAfter);
            L($"details: {nDet} cells, layers [{string.Join(", ", layers.Select(l => TD.detailPrototypes[l].prototype ? TD.detailPrototypes[l].prototype.name : "?"))}] {dBefore} -> {dAfter} instances in the zone rect");

            // ---- own terrain trees (key EB_fern)
            int[] pFern = new[] { "ENV_PC_TreeFern_A", "ENV_PC_TreeFern_B", "ENV_PC_TreeFern_C" }.Select(n => TreeProto(n)).Where(i => i >= 0).ToArray();
            int[] pCyc = new[] { "ENV_PC_Cycad_A", "ENV_PC_Cycad_B", "ENV_PC_Cycad_C" }.Select(n => TreeProto(n)).Where(i => i >= 0).ToArray();
            int pAra = TreeProto("PFB_ENV_Tree_01");
            var mine = new List<TreeInstance>(); var myG = new Dictionary<long, List<Vector3>>();
            int nF = 0, nC = 0, nA = 0; const float st = 3.4f;
            var araPos = new List<Vector3>();
            for (float gz = area.yMin; gz < area.yMax; gz += st)
                for (float gx = area.xMin; gx < area.xMax; gx += st)
                {
                    int ix = Mathf.RoundToInt(gx * 10f), iz = Mathf.RoundToInt(gz * 10f);
                    var p = new Vector3(gx + Rand01(ix, iz * 3 + 1) * st, 0f, gz + Rand01(ix * 7 + 3, iz) * st);
                    float ow = OwnWeight(z, p); if (ow <= 0.02f) continue;
                    p.y = GroundY(p); if (p.y < 2.8f || Slope(p) > 28f || WaterDist(p) < 3f) continue;
                    float n = Noise(p.x, p.z, 17f, 61), r = Rand01(ix + 911, iz + 17);
                    float pA = pAra >= 0 ? ow * ow * 0.028f : 0f, pF = pFern.Length > 0 ? ow * ow * (0.2f + 0.2f * n) : 0f, pC = pCyc.Length > 0 ? ow * (1f - ow) * 0.12f + 0.01f * ow : 0f;
                    int kind = r < pA ? 0 : r < pA + pF ? 1 : r < pA + pF + pC ? 2 : -1; if (kind < 0) continue;
                    float clear = kind == 0 ? 2.6f : 1.6f, sep = kind == 0 ? 5f : 2.8f;
                    if (FernTrailClear(p) < clear || EBFlat(p, _fernClearing) < FernClearingR + (kind == 0 ? 4f : 1.5f)) continue;
                    if (IsBlocked(p, 1.5f) || Near(others, p, kind == 0 ? 5f : 3f, 4f) || Near(myG, p, sep, 4f) || RouteDist(p) < 5f || EBFlat(p, camp) < 18f) continue;
                    float yaw = Rand01(ix + 5, iz + 9) * Mathf.PI * 2f, s;
                    int proto;
                    if (kind == 0) { proto = pAra; s = 0.95f + Rand01(ix, iz + 31) * 0.3f; nA++; araPos.Add(p); }
                    else if (kind == 1) { proto = pFern[(ix / 34 + iz / 34 + (int)(r * 97f)) % pFern.Length]; s = 0.85f + Rand01(ix + 2, iz) * 0.55f; nF++; }
                    else { proto = pCyc[(int)(r * 131f) % pCyc.Length]; s = 0.8f + Rand01(ix, iz + 2) * 0.4f; nC++; }
                    mine.Add(MakeTree(proto, p, yaw, s, s * (0.92f + Rand01(iz, ix) * 0.16f))); Add(myG, p, 4f);
                }
            SetOwnTrees("EB_fern", mine);
            L($"own terrain trees: {nF} tree ferns, {nC} cycads, {nA} tall araucarias (canopy)");

            // ---- placed dressing
            var root = ZoneRoot(z); ClearGroup(root);
            var under = Group2(root, "Understory"); var dead = Group2(root, "Deadwood"); var vines = Group2(root, "Vines");
            var placed = new Dictionary<long, List<Vector3>>();
            var allTrees = new Dictionary<long, List<Vector3>>(); foreach (var kv in others) foreach (var q in kv.Value) Add(allTrees, q, 4f); foreach (var kv in myG) foreach (var q in kv.Value) Add(allTrees, q, 4f);
            bool Base(Vector3 p, float r, float trailExtra)
            {
                if (OwnWeight(z, p) <= 0.05f) return false;
                float y = GroundY(p); if (y < 2.8f || WaterDist(p) < 2f) return false;
                if (FernTrailClear(p) < r * 0.6f + trailExtra) return false;
                if (EBFlat(p, _fernClearing) < FernClearingR + r * 0.5f) return false;
                if (IsBlocked(p, r * 0.5f + 0.4f) || RouteDist(p) < 4f || EBFlat(p, camp) < 16f) return false;
                return true;
            }
            var gf = EBScatter(under, new[] { "ENV_PC_GiantFern_A", "ENV_PC_GiantFern_B" }, 220, 3.2f,
                p => { float ow = OwnWeight(z, p); float th = Smooth01((Noise(p.x, p.z, 9f, 51) - 0.28f) / 0.45f); return ow * (0.35f + 0.65f * th); },
                (p, r) => Base(p, r, 0.4f) && Slope(p) < 32f && !Near(allTrees, p, 1.1f, 4f), 1.15f, 1.85f, 0.05f, false, 4101, area, placed);
            var mag = EBScatter(under, new[] { "ENV_PC_Magnolia_A", "ENV_PC_Magnolia_B" }, 70, 7f,
                p => OwnWeight(z, p) * 0.55f, (p, r) => Base(p, r, 0.6f) && Slope(p) < 24f && !Near(allTrees, p, 1.4f, 4f), 0.9f, 1.35f, 0.08f, false, 4102, area, placed);
            var trunks = EBScatter(dead, new[] { "ENV_PC_FallenTrunk_A", "ENV_PC_FallenTrunk_B" }, 12, 16f,
                p => OwnWeight(z, p) > 0.4f ? 0.7f : 0f, (p, r) => Base(p, r, 1.2f) && Slope(p) < 14f && !Near(allTrees, p, 1.2f, 4f), 0.9f, 1.2f, 0.12f, true, 4103, area, placed, isLong: true, colliderProp: true, ignoreRoot: root);
            var rocks = EBScatter(dead, new[] { "ENV_PC_MossRock_A", "ENV_PC_MossRock_B", "ENV_PC_MossRock_C" }, 16, 9f,
                p => OwnWeight(z, p) * 0.45f, (p, r) => Base(p, r, 1f) && Slope(p) < 26f && !Near(allTrees, p, 1.2f, 4f), 0.6f, 1.3f, 0.3f, true, 4104, area, placed, colliderProp: true, ignoreRoot: root);
            // root plates at the new araucarias
            int nRoots = 0; var rootPf = new[] { Prefab("ENV_PC_Roots_A"), Prefab("ENV_PC_Roots_B") }.Where(p => p).ToArray();
            for (int i = 0; i < araPos.Count && nRoots < 10 && rootPf.Length > 0; i++)
            {
                var p = araPos[i]; if (Rand01(i, 4105) > 0.45f || FernTrailClear(p) < 3f || IsBlocked(p, 2.6f) || Slope(p) > 20f) continue;
                var go = Place(dead, rootPf[nRoots % rootPf.Length], p, Rand01(i, 4106) * 360f, 0.8f + Rand01(i, 4107) * 0.3f, false, 0.15f);
                if (go) nRoots++;
            }
            // vine curtains on tree fern trunks (own trees)
            int nVines = 0; var vinePf = new[] { Prefab("ENV_PC_VineCurtain_A"), Prefab("ENV_PC_VineCurtain_B") }.Where(p => p).ToArray();
            var fernSet = new HashSet<int>(pFern);
            for (int i = 0; i < mine.Count && nVines < 40 && vinePf.Length > 0; i++)
            {
                var t = mine[i]; if (!fernSet.Contains(t.prototypeIndex) || Rand01(i, 4108) > 0.16f) continue;
                var tp = TreeWorld(t); if (FernTrailClear(tp) < 2.5f || OwnWeight(z, tp) < 0.5f) continue;
                var tpf = TD.treePrototypes[t.prototypeIndex].prefab; float h = EBPrefabBounds(tpf).max.y * t.heightScale; if (h < 2.5f) continue;
                var vpf = vinePf[nVines % vinePf.Length]; var vb = EBPrefabBounds(vpf);
                float top = h * 0.8f, s = Mathf.Clamp(top * 0.85f / Mathf.Max(0.5f, vb.size.y), 0.5f, 1.3f);
                float ang = Rand01(i, 4109) * 360f; var dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
                var pos = tp + dir * (0.28f * t.widthScale + Mathf.Max(0f, vb.max.z) * s); pos.y = GroundY(tp) + top - vb.max.y * s;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(vpf, vines);
                go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-dir, Vector3.up)); go.transform.localScale = Vector3.one * s;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic);
                nVines++;
            }
            L($"placed: {gf.Count} giant fern clumps, {mag.Count} magnolia shrubs, {trunks.Count} fallen trunks, {nRoots} root plates, {rocks.Count} moss rocks, {nVines} vine curtains on tree fern trunks");

            // ---- trails, landmark, FX anchors
            EBTrailMarkers(root, _fernTrails);
            foreach (var t in _fernTrails) L($"trail {t.name} (half width {F(t.halfWidth)} m, {F(t.Length)} m, {t.pts.Length} WP): {t.note}; " + string.Join(" ", t.pts.Select(V)));
            var lm = Marker(root, "LM_GiantFernForest", _fernClearing, Quaternion.identity);
            var art = Prefab("LM_GiantTree", false);
            if (art) { var go = (GameObject)PrefabUtility.InstantiatePrefab(art, lm); go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; L($"landmark: ART LM_GiantTree placed at {V(_fernClearing)}"); }
            else L($"landmark: placeholder LM_GiantFernForest at {V(_fernClearing)} (LM_GiantTree not delivered yet)");
            var fx = Group2(root, "FX_Anchors");
            void Fx(string name, Vector3 p, float up) { var g = Ground(p); Marker(fx, name, g + Vector3.up * up, Quaternion.identity); L($"  FX anchor {name} {V(g + Vector3.up * up)}"); }
            Fx("FX_GroundMist_01", new Vector3(116f, 0f, 40f), 0.3f); Fx("FX_GroundMist_02", new Vector3(134f, 0f, -8f), 0.3f);
            Fx("FX_GroundMist_03", new Vector3(184f, 0f, 48f), 0.3f); Fx("FX_GroundMist_04", new Vector3(194f, 0f, -18f), 0.3f);
            Fx("FX_Insects_01", _fernClearing + new Vector3(-7f, 0f, 5f), 1.2f); Fx("FX_Insects_02", _fernClearing + new Vector3(8f, 0f, -6f), 1.2f);
            if (trunks.Count > 0) Fx("FX_Insects_03", trunks[0].transform.position, 1f);
            Fx("FX_LightShaft_01", _fernClearing, 14f);
            Fx("FX_Drip_01", new Vector3(146f, 0f, 46f), 3f);

            Ter.Flush(); EditorUtility.SetDirty(TD); AssetDatabase.SaveAssets();
            SaveIsland(scene, wasDirty);
            return End();
        }

        static void FernSurvey(Zone z, Dictionary<long, List<Vector3>> trees)
        {
            foreach (var n in new[] { "ENV_PC_GiantFern_A", "ENV_PC_GiantFern_B", "ENV_PC_Magnolia_A", "ENV_PC_Magnolia_B", "ENV_PC_FallenTrunk_A", "ENV_PC_FallenTrunk_B",
                "ENV_PC_Roots_A", "ENV_PC_VineCurtain_A", "ENV_PC_VineCurtain_B", "ENV_PC_TreeFern_A", "ENV_PC_Cycad_A", "PFB_ENV_Tree_01", "PROP_PC_BrokenTree_A", "PROP_PC_BrokenTree_B",
                "ENV_PC_Basalt_A", "ENV_PC_BasaltBoulder", "DET_ENV_Grass_01", "LM_GiantTree", "LM_BlackRidge" })
            {
                var pf = Prefab(n, false); if (!pf) { L($"  prefab {n}: missing"); continue; }
                var b = EBPrefabBounds(pf); L($"  prefab {n}: bounds min {V(b.min)} max {V(b.max)}, colliders {pf.GetComponentsInChildren<Collider>(true).Length}, LODGroup {(pf.GetComponentInChildren<LODGroup>(true) ? "yes" : "no")}");
            }
            int inZone = 0; foreach (var kv in trees) foreach (var p in kv.Value) if (z.Weight(p) > 0f) inZone++;
            L($"  other terrain trees in the zone + blend: {inZone}");
            var cands = new List<(Vector3 p, int n, float s)>();
            for (float dx = -24f; dx <= 24f; dx += 4f) for (float dz = -24f; dz <= 24f; dz += 4f)
                {
                    var p = Ground(new Vector3(FernClearingDefault.x + dx, 0f, FernClearingDefault.y + dz)); float s = 0f;
                    for (int k = 0; k < 8; k++) s = Mathf.Max(s, Slope(p + Quaternion.Euler(0, k * 45f, 0) * Vector3.forward * 6f));
                    if (IsBlocked(p, FernClearingR)) continue;
                    cands.Add((p, EBCount(trees, p, FernClearingR), s));
                }
            foreach (var c in cands.OrderBy(c => c.n * 10 + c.s).Take(8)) L($"  clearing candidate {V(c.p)}: {c.n} trees within {F(FernClearingR)} m, max slope {F(c.s)}");
            foreach (var t in _fernTrails)
            {
                float maxS = 0f, minW = 999f; int blocked = 0, solid = 0, treesIn = 0, samples = 0;
                for (int i = 0; i + 1 < t.pts.Length; i++)
                {
                    float len = EBFlat(t.pts[i], t.pts[i + 1]);
                    for (float d = 0f; d < len; d += 2f)
                    {
                        var p = Ground(Vector3.Lerp(t.pts[i], t.pts[i + 1], d / len)); samples++;
                        maxS = Mathf.Max(maxS, Slope(p)); minW = Mathf.Min(minW, WaterDist(p));
                        if (IsBlocked(p, 0f)) blocked++; if (SolidAt(p, t.halfWidth)) solid++; treesIn += EBCount(trees, p, t.halfWidth + 0.5f);
                    }
                }
                L($"  trail {t.name}: {F(t.Length)} m, {samples} samples, max slope {F(maxS)}, min water {F(minW)}, blocked {blocked}, colliders {solid}, tree hits {treesIn}");
            }
        }

        // ================================================================== check (read only, both EB zones)
        [PrimalBridgeCommand]
        public static string FernFoothillsCheck(string arg)
        {
            Begin("EB", "FernFoothillsCheck " + arg);
            if (!OpenIsland(out var scene, out _)) return End();
            if (!FindTerrain() || LoadFeat() == null) { W("no terrain or features"); return End(); }
            int problems = 0;
            var fern = ZoneById("fern"); var foot = ZoneById("foothills");
            _fernClearing = Ground(new Vector3(FernClearingDefault.x, 0f, FernClearingDefault.y));
            var lmT = SceneRootsFind(fern.GroupPath + "/LM_GiantFernForest"); if (lmT) _fernClearing = lmT.position;
            _fernTrails = FernTrails(_fernClearing);
            FootLoad();
            foreach (var (z, key) in new[] { (fern, "EB_fern"), (foot, "EB_foothills") })
            {
                var root = SceneRootsFind(z.GroupPath);
                if (!root) { W($"{z.GroupPath} missing: run the builder first"); problems++; continue; }
                problems += CheckZone(z, new List<Transform> { root }, key, scene);
                L($"  [{z.id}] models: {EBModelCounts(root)}");
                var fx = root.Find("FX_Anchors");
                if (fx) foreach (Transform c in fx) L($"  [{z.id}] FX anchor {c.name} {V(c.position)}");
                foreach (Transform c in root) if (c.name.StartsWith("LM_")) L($"  [{z.id}] landmark {c.name} {V(c.position)} ({(c.childCount > 0 ? "art: " + c.GetChild(0).name : "placeholder, no art yet")})");
                // detail totals inside the zone core
                int res = TD.detailResolution; var sb = new StringBuilder();
                for (int l = 0; l < TD.detailPrototypes.Length; l++)
                {
                    var m = TD.GetDetailLayer(0, 0, res, res, l); long n = 0;
                    var b = z.Bounds;
                    int x0 = Mathf.Clamp((int)((b.xMin - TPos.x) / TD.size.x * res), 0, res - 1), x1 = Mathf.Clamp((int)((b.xMax - TPos.x) / TD.size.x * res), 0, res - 1);
                    int z0 = Mathf.Clamp((int)((b.yMin - TPos.z) / TD.size.z * res), 0, res - 1), z1 = Mathf.Clamp((int)((b.yMax - TPos.z) / TD.size.z * res), 0, res - 1);
                    for (int iz = z0; iz <= z1; iz++) for (int ix = x0; ix <= x1; ix++)
                        {
                            var w = new Vector3(TPos.x + (ix + 0.5f) / res * TD.size.x, 0f, TPos.z + (iz + 0.5f) / res * TD.size.z);
                            if (OwnWeight(z, w) > 0f) n += m[iz, ix];
                        }
                    var pr = TD.detailPrototypes[l].prototype; sb.Append($"{(pr ? pr.name : "?")} {n}; ");
                }
                L($"  [{z.id}] detail instances (own weight > 0): {sb}");
            }
            // trail corridors (fern forest)
            var fernRoot = SceneRootsFind(fern.GroupPath);
            var own = OwnTreePositions("EB_fern"); var ownG = new Dictionary<long, List<Vector3>>(); foreach (var p in own) Add(ownG, p, 4f);
            var otherG = EBTreeGrid("EB_fern");
            foreach (var t in _fernTrails)
            {
                int samples = 0, mineSolid = 0, otherSolid = 0, ownTrees = 0, otherTrees = 0; float maxS = 0f; var names = new HashSet<string>();
                for (int i = 0; i + 1 < t.pts.Length; i++)
                {
                    float len = EBFlat(t.pts[i], t.pts[i + 1]);
                    for (float d = 0f; d < len; d += 1f)
                    {
                        var p = Ground(Vector3.Lerp(t.pts[i], t.pts[i + 1], d / len)); if (fern.Weight(p) <= 0f) continue; samples++;
                        maxS = Mathf.Max(maxS, Slope(p));
                        var c = new Vector3(p.x, p.y + t.halfWidth * 0.5f + 0.2f, p.z);
                        foreach (var col in Physics.OverlapSphere(c, t.halfWidth * 0.8f, ~0, QueryTriggerInteraction.Ignore))
                        {
                            if (col is TerrainCollider) continue;
                            if (fernRoot && col.transform.IsChildOf(fernRoot)) mineSolid++; else { otherSolid++; if (names.Count < 6) names.Add(PathOf(col.transform)); }
                        }
                        ownTrees += EBCount(ownG, p, t.halfWidth); otherTrees += EBCount(otherG, p, t.halfWidth);
                    }
                }
                if (mineSolid + ownTrees > 0) problems++;
                L($"  trail {t.name}: {samples} samples (1 m) in the zone, max slope {F(maxS)}; own colliders {mineSolid}, own trees {ownTrees} (must be 0); other colliders {otherSolid}, other agents' / v1 trees {otherTrees}{(names.Count > 0 ? " [" + string.Join("; ", names) + "]" : "")}");
                L($"    WP: {string.Join(" ", t.pts.Select(V))}");
            }
            // canyon path (foothills)
            var footRoot = SceneRootsFind(foot.GroupPath);
            if (_footCanyon != null && footRoot)
            {
                int samples = 0, hits = 0, ownTrees = 0; var footOwn = new Dictionary<long, List<Vector3>>(); foreach (var p in OwnTreePositions("EB_foothills")) Add(footOwn, p, 4f);
                for (int i = 0; i + 1 < _footCanyon.Count; i++)
                {
                    var p = _footCanyon[i]; if (p.z > -100f) continue; samples++;
                    foreach (var col in Physics.OverlapSphere(Ground(p) + Vector3.up * 1.2f, 2f, ~0, QueryTriggerInteraction.Ignore)) if (col.transform.IsChildOf(footRoot)) hits++;
                    ownTrees += EBCount(footOwn, p, 3f);
                }
                if (hits + ownTrees > 0) problems++;
                L($"  canyon path (z < -100): {samples} samples, own colliders within 2 m {hits}, own trees within 3 m {ownTrees} (must be 0)");
                int nearLava = 0; foreach (Transform c in footRoot.GetComponentsInChildren<Transform>(true)) if (c != footRoot && PrefabUtility.IsOutermostPrefabInstanceRoot(c.gameObject) && FootLavaDist(c.position) < 4f) nearLava++;
                L($"  own objects within 4 m of the lava channel / vent: {nearLava}");
                if (nearLava > 0) problems++;
            }
            L($"EB check problems: {problems}");
            return End();
        }

        static Transform SceneRootsFind(string path) => PrimalFrontier.Core.SceneRoots.Find(path, false);
    }
}
