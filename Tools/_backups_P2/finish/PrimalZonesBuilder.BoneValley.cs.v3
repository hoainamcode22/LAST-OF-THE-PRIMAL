using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using PrimalFrontier.Core;
using PrimalFrontier.World;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// BONE (Phase 2): Zone 3, Bone / Carcass Valley, on predator_territory (-105, 21, -76), r 50 (+ 25 m blend), the kill
    /// site basin, the massif's north slope and the canyon mouth (z &gt; -100; the canyon above is ENV-B's). A natural death
    /// area that says "something hunts here": darker, trampled ground; sparse ferns and grass; old bones scattered over the
    /// basin; a bone line and a skull across the predator path at the entrance with fresh large prints branching off it;
    /// drag marks and blood to a fresh, half-eaten carcass (a real Carcass: butcher it with a knife, ZoneCarcass fills it at
    /// start); a rotting carcass days old; an old bone pile at the canyon mouth; prints leading out along the predator path
    /// to the canyon; claw-marked snags and snapped trees; the landmark (LM_FossilSkeleton from ART, until then the empty
    /// LM_BoneValley) half-buried in the slope above the path. AI_Anchors for AI: scavenge points, a predator visit route,
    /// pteranodon perches and a circling centre (each an EnvLocation: id, radius, note).
    /// Everything lives under World/Environment/Forest/BoneValley and is rebuilt on each run (the ENV Storytelling kill
    /// site, skeleton, blood trail and theropod trail are ENV's and are not touched).
    /// Bridge commands:
    ///   BoneValley "[terrain][;props][;rebake][;force]"   (no step named = terrain + props); terrain edits start from the
    ///                                                   BV backup (Art/Terrain/_Backup/TD_Island_before_BV.asset)
    ///   BoneValleyCheck ""                               read only: counts, missing refs, covered nodes, carcass / anchor state
    /// </summary>
    public static partial class PrimalZonesBuilder
    {
        const string BV_Gen = "Assets/_Project/Art/Environment/Generated/BoneValley";
        const string BV_TexDir = "Assets/_Project/Art/Environment/Textures/Zones";
        const string BV_DinoDir = "Assets/Art/Characters/Dinosaurs";
        const string BV_TreeKey = "BV_bone";
        static readonly string[] BV_Subgroups = { "Carcasses", "Bones", "Tracks", "Trees", "Entrance", "Landmark", "AI_Anchors" };

        [Serializable] class BVFeat { public float[] predatorPath; }
        static List<Vector3> _bvPath;
        static float[] _bvArc;

        static bool BVLoadPath()
        {
            _bvPath = new List<Vector3>();
            if (!File.Exists(FeaturesPath)) { W("missing " + FeaturesPath); return false; }
            var f = JsonUtility.FromJson<BVFeat>(File.ReadAllText(FeaturesPath));
            _bvPath = Pts(f != null ? f.predatorPath : null);
            _bvArc = new float[_bvPath.Count];
            for (int i = 1; i < _bvPath.Count; i++) _bvArc[i] = _bvArc[i - 1] + BVFlat(_bvPath[i] - _bvPath[i - 1]).magnitude;
            if (_bvPath.Count < 3) { W("predatorPath missing in the features file"); return false; }
            return true;
        }
        static Vector3 BVFlat(Vector3 v) { v.y = 0f; return v; }
        static float BVPathLen => _bvArc != null && _bvArc.Length > 0 ? _bvArc[_bvArc.Length - 1] : 0f;
        /// <summary>point and heading on the predator path at arc length s (metres from its meadow end)</summary>
        static Vector3 BVAt(float s, out Vector3 dir)
        {
            dir = Vector3.forward;
            for (int i = 0; i + 1 < _bvPath.Count; i++)
            {
                float seg = _bvArc[i + 1] - _bvArc[i];
                if (s <= _bvArc[i + 1] || i + 2 == _bvPath.Count)
                {
                    dir = BVFlat(_bvPath[i + 1] - _bvPath[i]).normalized;
                    return Ground(Vector3.Lerp(_bvPath[i], _bvPath[i + 1], Mathf.Clamp01((s - _bvArc[i]) / Mathf.Max(0.01f, seg))));
                }
            }
            return _bvPath[0];
        }
        /// <summary>arc length of the path point nearest to p, and the xz distance to it</summary>
        static float BVArcOf(Vector3 p, out float dist)
        {
            dist = 1e9f; float best = 0f; var q = new Vector2(p.x, p.z);
            for (int i = 0; i + 1 < _bvPath.Count; i++)
            {
                Vector2 a = new Vector2(_bvPath[i].x, _bvPath[i].z), b = new Vector2(_bvPath[i + 1].x, _bvPath[i + 1].z); var ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude)); float d = (a + ab * t - q).magnitude;
                if (d < dist) { dist = d; best = _bvArc[i] + t * ab.magnitude; }
            }
            return best;
        }
        static float BVPathDist(Vector3 p) { BVArcOf(p, out float d); return d; }

        // ------------------------------------------------------------------ plan (deterministic spots)
        struct BVFeature { public Vector3 p; public float r, dark, clear; }
        class BVPlan
        {
            public Vector3 entrance, entranceDir, fresh, rot, old, landmark, dragFrom;
            public float entranceArc, freshYaw, rotYaw, landmarkYaw;
            public float lmScale = 1f, lmHx, lmHz, lmPitch, lmRoll, lmResid; public bool lmBlocked; public Quaternion lmRot = Quaternion.identity; public Vector3 lmFwd = Vector3.forward, lmRight = Vector3.right;
            /// <summary>inside the landmark's footprint (+ margin metres)</summary>
            public bool InLandmark(Vector3 p, float margin)
            {
                if (lmHx <= 0f) return false; var d = p - landmark;
                return Mathf.Abs(Vector3.Dot(d, lmRight)) < lmHx + margin && Mathf.Abs(Vector3.Dot(d, lmFwd)) < lmHz + margin;
            }
            public readonly List<BVFeature> feats = new List<BVFeature>();
        }
        static BVPlan _bvPlan;

        static bool BVOk(Zone z, Vector3 p, float extra, float minOwn = 0.5f)
            => p.z > -100f && OwnWeight(z, p) >= minOwn && WaterDist(p) > 3f && !IsBlocked(p, extra);

        /// <summary>best spot within rMax of pref by score (NegativeInfinity = not allowed); false if none</summary>
        static bool BVFind(Vector3 pref, float rMax, Func<Vector3, float> score, out Vector3 best)
        {
            best = Ground(pref); float bs = float.NegativeInfinity;
            for (float r = 0f; r <= rMax; r += 1f)
            {
                int n = r < 0.5f ? 1 : Mathf.Max(8, Mathf.RoundToInt(r * 4f));
                for (int k = 0; k < n; k++)
                {
                    float a = k * Mathf.PI * 2f / n + r * 0.37f;
                    var p = Ground(pref + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * r);
                    float s = score(p); if (float.IsNegativeInfinity(s)) continue;
                    s -= r * 0.35f;
                    if (s > bs) { bs = s; best = p; }
                }
            }
            return !float.IsNegativeInfinity(bs);
        }

        struct BVBlock { public Vector3 p; public float r; public string name; public bool node; }
        static List<BVBlock> BVBlockers(Scene scene, Zone z)
        {
            var l = new List<BVBlock>();
            void B(Vector3 p, float r, string n, bool node) { if (BVFlat(p - z.centre).magnitude < z.radius + 40f) l.Add(new BVBlock { p = p, r = r, name = n, node = node }); }
            var mine = ZoneRoot(z);
            foreach (var root in scene.GetRootGameObjects())
                foreach (var it in root.GetComponentsInChildren<Interactable>(false))
                    if (!it.transform.IsChildOf(mine)) B(it.transform.position, it is ResourceNode ? 2.2f : 1.5f, PathOf(it.transform), it is ResourceNode);
            foreach (var path in new[] { SceneRoots.Interactables + "/Storytelling", SceneRoots.Interactables + "/GiantFootprints" })
            { var t = SceneRoots.Find(path); if (t) foreach (var r in t.GetComponentsInChildren<Renderer>(false)) B(r.bounds.center, Mathf.Clamp(r.bounds.extents.magnitude * 0.6f, 1f, 4f), PathOf(r.transform), false); }
            // solid props of other groups (rocks, logs, thickets) under the footprint
            foreach (var col in UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                if (col is TerrainCollider || col.isTrigger || !col.enabled || col.transform.IsChildOf(mine)) continue;
                var b = col.bounds; if (b.size.y < 0.4f) continue;
                B(b.center, Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.z), 0.5f, 5f), PathOf(col.transform), false);
            }
            return l;
        }

        /// <summary>
        /// LM_FossilSkeleton (ART: 40.7 x 23.5 m footprint, 7.5 m high, pivot base centre at ground, head +X, mound bank on -Z,
        /// exposed side +Z): the best spot in the zone where the whole footprint is clear of the predator path (2.5 m), every
        /// interactable / resource node / ENV story prop / other solid prop (exact rectangle test), the canyon cliff and the
        /// neighbours' cores, and the ground fits a plane. Scale 1 down to 0.7 (ART: 0.8-1.25 is fine); the exposed side
        /// should face the basin. The model is tilted to 80 % of the fitted plane and sunk by the fit error.
        /// </summary>
        static void BVLandmarkSpot(Zone z, BVPlan P, Scene scene)
        {
            const float HX = 20.33f, HZ = 11.73f;
            var blk = BVBlockers(scene, z);
            var pathPts = new List<Vector3>(); for (float s0 = 0f; s0 <= BVPathLen; s0 += 1f) pathPts.Add(BVAt(s0, out _));
            float best = float.NegativeInfinity; int tried = 0, clear = 0;
            int bestBlocked = int.MaxValue; string bestBlockedInfo = ""; Vector3 bestBlockedAt = Vector3.zero; float bestBlockedScale = 0f, bestBlockedYaw = 0f;
            foreach (float s in new[] { 1f, 0.9f, 0.8f, 0.75f, 0.7f })
            {
                float hx = HX * s, hz = HZ * s;
                for (float cx = -134f; cx <= -84f; cx += 2f)
                    for (float cz = -100f; cz <= -20f; cz += 2f)
                    {
                        var c = new Vector3(cx, 0f, cz); if (BVFlat(c - z.centre).magnitude > z.radius) continue;
                        for (int yi = 0; yi < 24; yi++)
                        {
                            float yaw = yi * 15f; var rot = Quaternion.Euler(0f, yaw, 0f); var fw = rot * Vector3.forward; var rt = rot * Vector3.right;
                            bool ok = true;
                            double su = 0, sv = 0, sa = 0, sb = 0, sc = 0; int n = 0; var hs = new List<Vector3>(45);
                            for (int iu = -4; iu <= 4 && ok; iu++)
                                for (int iv = -2; iv <= 2; iv++)
                                {
                                    float u = iu / 4f * hx, v = iv / 2f * hz; var p = c + rt * u + fw * v;
                                    if (p.z < -100f || p.x < -131f || OwnWeight(z, p) < 0.5f || WaterDist(p) < 3f) { ok = false; break; }
                                    float y = GroundY(p); hs.Add(new Vector3(u, y, v)); su += u * u; sv += v * v; sa += u * y; sb += v * y; sc += y; n++;
                                }
                            if (!ok) continue;
                            foreach (var q in pathPts) if (BVInRect(q, c, rt, fw, hx, hz, 2.5f)) { ok = false; break; }
                            if (!ok) continue;
                            tried++;
                            int blocked = 0; string info = "";
                            foreach (var b in blk) if (BVInRect(b.p, c, rt, fw, hx, hz, b.r * 0.7f + 0.5f)) { blocked++; if (info.Length < 400) info += (b.node ? "[node] " : "") + b.name + "; "; }
                            if (blocked > 0)
                            {
                                if (blocked < bestBlocked || (blocked == bestBlocked && s > bestBlockedScale)) { bestBlocked = blocked; bestBlockedInfo = info; bestBlockedAt = c; bestBlockedScale = s; bestBlockedYaw = yaw; }
                                continue;
                            }
                            clear++;
                            float A = (float)(sa / su), B = (float)(sb / sv), C = (float)(sc / n), resid = 0f;
                            foreach (var h in hs) resid = Mathf.Max(resid, Mathf.Abs(h.y - (A * h.x + B * h.z + C)));
                            float pitch = Mathf.Atan(-B) * Mathf.Rad2Deg, roll = Mathf.Atan(A) * Mathf.Rad2Deg;
                            var toBasin = BVFlat(new Vector3(-112f, 0f, -50f) - c); float vis = toBasin.magnitude;
                            float facing = vis > 1f ? Vector3.Dot(fw, toBasin / vis) : 0f;
                            float score = facing * 2f - resid * 1.2f - Mathf.Abs(roll) * 0.15f - vis * 0.06f + s * 8f - Mathf.Max(0f, Mathf.Abs(pitch) - 18f) * 0.2f;
                            if (score <= best) continue;
                            best = score; P.lmScale = s; P.lmHx = hx; P.lmHz = hz; P.landmarkYaw = yaw; P.lmPitch = pitch; P.lmRoll = roll; P.lmResid = resid;
                            P.lmFwd = fw; P.lmRight = rt; P.landmark = new Vector3(cx, C, cz);
                            var nWorld = rot * new Vector3(-A, 1f, -B).normalized;
                            P.lmRot = Quaternion.FromToRotation(Vector3.up, Vector3.Lerp(Vector3.up, nWorld, 0.8f).normalized) * rot;
                        }
                    }
            }
            L($"landmark search: {blk.Count} blockers near the zone, {tried} footprints clear of the path and inside the zone, {clear} also clear of every blocker, best score {F(best)}");
            if (bestBlocked < int.MaxValue) L($"  fewest blockers of the others: {bestBlocked} at {V(bestBlockedAt)} scale {F(bestBlockedScale)} yaw {F(bestBlockedYaw)}: {bestBlockedInfo}");
            if (float.IsNegativeInfinity(best))
            {
                W("landmark: no clear footprint found: only the marker LM_BoneValley is placed (at the fewest-blockers spot), the prefab is NOT placed");
                P.lmBlocked = true;
                var at = bestBlocked < int.MaxValue ? bestBlockedAt : new Vector3(-120f, 0f, -32f);
                P.landmark = Ground(at); P.lmScale = bestBlocked < int.MaxValue ? bestBlockedScale : 0.7f; P.lmHx = HX * P.lmScale; P.lmHz = HZ * P.lmScale; P.landmarkYaw = bestBlockedYaw;
                P.lmRot = Quaternion.Euler(0f, P.landmarkYaw, 0f); P.lmFwd = P.lmRot * Vector3.forward; P.lmRight = P.lmRot * Vector3.right;
            }
        }
        static bool BVInRect(Vector3 q, Vector3 c, Vector3 rt, Vector3 fw, float hx, float hz, float m)
        {
            float dx = q.x - c.x, dz = q.z - c.z;
            return Mathf.Abs(dx * rt.x + dz * rt.z) < hx + m && Mathf.Abs(dx * fw.x + dz * fw.z) < hz + m;
        }

        static BVPlan BVMakePlan(Zone z, Scene scene)
        {
            var P = new BVPlan();
            BVLandmarkSpot(z, P, scene);
            // entrance: where the predator path comes over the low ridge from the meadow into the basin
            P.entranceArc = BVArcOf(new Vector3(-99f, 0f, -62f), out _);
            P.entrance = BVAt(P.entranceArc, out P.entranceDir);
            // fresh carcass: on the flat basin floor between the path and the old kill site
            int rs = 0, rp = 0, rb = 0;
            Func<Vector3, float, float, float, float> flat = (p, maxS, minPath, extra) =>
            {
                if (Slope(p) > maxS) { rs++; return float.NegativeInfinity; }
                if (BVPathDist(p) < minPath) { rp++; return float.NegativeInfinity; }
                if (!BVOk(z, p, extra) || P.InLandmark(p, extra)) { rb++; return float.NegativeInfinity; }
                return -Slope(p) * 0.15f;
            };
            if (!BVFind(new Vector3(-108f, 0f, -56f), 14f, p => flat(p, 16f, 4f, 2.5f), out P.fresh))
                W("fresh carcass: no free flat spot near (-108, -56): using the preferred point");
            P.dragFrom = BVAt(BVArcOf(P.fresh, out _), out _);
            P.freshYaw = Quaternion.LookRotation(BVFlat(P.fresh - P.dragFrom).normalized + Vector3.right * 0.35f).eulerAngles.y;
            // rotting carcass: the west basin floor below the canyon mouth
            if (!BVFind(new Vector3(-123f, 0f, -45f), 14f, p => flat(p, 16f, 5f, 3f), out P.rot))
                W("rotting carcass: no free flat spot near (-123, -45): using the preferred point");
            L($"plan rejections (fresh + rotting search): slope {rs}, path {rp}, blocked / zone {rb}");
            P.rotYaw = 205f;
            // old bone pile: the canyon mouth floor (z > -100)
            if (!BVFind(new Vector3(-136f, 0f, -72f), 8f, p => Slope(p) > 18f || !BVOk(z, p, 3f) || P.InLandmark(p, 3f) ? float.NegativeInfinity : -Slope(p) * 0.1f, out P.old))
                W("old bones: no free spot near (-136, -72): using the preferred point");
            // terrain features: (position, radius, darkness, detail clearing)
            void Fe(Vector3 p, float r, float dark, float clear) => P.feats.Add(new BVFeature { p = p, r = r, dark = dark, clear = clear });
            Fe(P.fresh, 7f, 0.55f, 1f); Fe(P.rot, 7.5f, 0.6f, 1f); Fe(P.old, 5f, 0.35f, 0.8f); Fe(P.landmark, Mathf.Max(P.lmHx, 8f), 0.4f, 0.85f); Fe(P.entrance, 4.5f, 0.3f, 0.7f);
            var skel = Feat != null && Feat.props != null ? P3(Feat.props.skeleton) : Vector3.zero;
            if (skel != Vector3.zero) Fe(skel, 7f, 0.45f, 0.7f);
            for (int i = 0; i <= 4; i++) { var q = Vector3.Lerp(P.dragFrom, P.fresh, i / 4f); Fe(q, 2.2f, 0.4f, 1f); }
            _bvPlan = P;
            return P;
        }

        // ------------------------------------------------------------------ bridge command
        [PrimalBridgeCommand]
        public static string BoneValley(string arg)
        {
            var a = Args(arg);
            bool all = !a.ContainsKey("terrain") && !a.ContainsKey("props");
            Begin("BV", "BoneValley " + arg);
            if (!OpenIsland(out var scene, out bool wasDirty)) return End();
            if (!FindTerrain()) { W("no terrain"); return End(); }
            LoadFeat(); if (!BVLoadPath()) return End();
            var z = ZoneById("bone");
            var root = ZoneRoot(z);
            foreach (var g in BV_Subgroups) ClearGroup(Group(z, g));
            Physics.SyncTransforms();
            L($"blockers: {BuildBlockers(scene)}");
            var P = BVMakePlan(z, scene);
            L($"plan: entrance {V(P.entrance)} (path arc {F(P.entranceArc)} of {F(BVPathLen)}), fresh {V(P.fresh)}, rotting {V(P.rot)}, old {V(P.old)}, landmark {V(P.landmark)} scale {F(P.lmScale)} yaw {F(P.landmarkYaw)} pitch {F(P.lmPitch)} roll {F(P.lmRoll)} ground fit {F(P.lmResid)} m, footprint {F(P.lmHx * 2f)} x {F(P.lmHz * 2f)} m");
            if (all || a.ContainsKey("terrain")) BVTerrain(z, P);
            if (all || a.ContainsKey("props")) BVProps(z, P, root, a.ContainsKey("rebake"));
            AssetDatabase.SaveAssets();
            SaveIsland(scene, wasDirty);
            return End();
        }

        // ------------------------------------------------------------------ terrain: darker soil, trampled path, sparse plants
        static int BVLayer(string name) { var ls = TD.terrainLayers; for (int i = 0; i < ls.Length; i++) if (ls[i] && ls[i].name == name) return i; return -1; }

        static void BVTerrain(Zone z, BVPlan P)
        {
            var bk = BackupTerrain("BV"); if (!bk) { W("no terrain backup: terrain step skipped"); return; }
            int dark = BVLayer("TL_DarkSoil"), mud = BVLayer("TL_Mud"), rock = BVLayer("TL_Rock");
            if (dark < 0 || mud < 0) { W("TL_DarkSoil / TL_Mud layer missing: splat skipped"); }
            else
            {
                int n = EditSplat(z, bk, (w, ow, b, t) =>
                {
                    float rk = rock >= 0 ? b[rock] : 0f;
                    float k = 0.28f + 0.3f * Noise(w.x, w.z, 9f, 11);
                    foreach (var f in P.feats) { float d = BVFlat(w - f.p).magnitude; if (d < f.r) k += f.dark * (1f - Smooth01(d / f.r)); }
                    float pd = BVPathDist(w), m = pd < 3f ? 0.5f * (1f - pd / 3f) : 0f;
                    k = Mathf.Clamp01(k) * (1f - rk * 0.85f); m = Mathf.Min(m * (1f - rk), 1f - k);
                    for (int l = 0; l < t.Length; l++) if (l != rock) t[l] = b[l] * (1f - k - m);
                    t[dark] += k; t[mud] += m;
                });
                L($"splat: {n} texels (TL_DarkSoil through the zone core, darker at the carcasses / landmark / kill site, TL_Mud along the path; rock kept)");
            }
            var layers = new[] { "DET_ENV_Fern_01", "DET_ENV_Grass_01", "DET_ENV_Bush_01", "DET_PC_GiantFern" }.Select(DetailLayer).Where(i => i >= 0).ToArray();
            if (layers.Length == 0) { W("no detail layers found"); return; }
            int nd = EditDetails(z, bk, layers, (w, ow, b, t) =>
            {
                float keep = 0.4f + 0.3f * Noise(w.x, w.z, 7f, 23);
                if (Noise(w.x, w.z, 11f, 31) > 0.7f) keep *= 0.25f;                 // trampled patches
                float pd = BVPathDist(w); if (pd < 3f) keep *= Mathf.Lerp(0.08f, 1f, pd / 3f);
                foreach (var f in P.feats) { float d = BVFlat(w - f.p).magnitude; if (d < f.r) keep *= Mathf.Lerp(1f - f.clear, 1f, Smooth01(d / f.r)); }
                for (int k = 0; k < t.Length; k++) t[k] = Mathf.RoundToInt(b[k] * keep);
            }, out long before, out long after);
            L($"details: {nd} cells on {layers.Length} layers ({string.Join(", ", layers)}), {before} -> {after} instances in the zone rect");
        }

        // ------------------------------------------------------------------ assets
        static Mesh BVQuad()
        {
            var q = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/_Project/Art/Environment/Generated/ME_Env_DecalQuad.asset");
            if (q) return q;
            string p = BV_Gen + "/ME_BV_Quad.asset"; q = AssetDatabase.LoadAssetAtPath<Mesh>(p); if (q) return q;
            q = new Mesh { name = "ME_BV_Quad" };
            q.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f) };
            q.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            q.triangles = new[] { 0, 2, 1, 0, 3, 2 }; q.RecalculateNormals(); q.RecalculateTangents(); q.RecalculateBounds();
            EnsureFolder(BV_Gen); AssetDatabase.CreateAsset(q, p); return q;
        }

        static Material BVMat(string name, Material from, Action<Material> tune)
        {
            EnsureFolder(ZoneMatDir);
            string p = $"{ZoneMatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(from) { name = name }; AssetDatabase.CreateAsset(m, p); }
            else { if (m.shader != from.shader) m.shader = from.shader; m.CopyPropertiesFromMaterial(from); }
            m.enableInstancing = true; tune(m); EditorUtility.SetDirty(m);
            return m;
        }
        static void BVTint(Material m, Color c, float smooth)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
        }

        /// <summary>drag furrow texture (made once): a smeared dark band with three claw / tail grooves along it, soft cut-out edges</summary>
        static Material BVDragMaterial()
        {
            EnsureFolder(BV_TexDir);
            string tp = BV_TexDir + "/T_BV_DragMark_D.png";
            if (!File.Exists(tp))
            {
                const int w = 128, h = 256; var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                        float n = Noise(x, y * 0.35f, 6f, 5), n2 = Noise(x, y, 2.5f, 9);
                        float band = 1f - Smooth01((Mathf.Abs(u - 0.5f) - 0.2f + (n - 0.5f) * 0.14f) / 0.14f);
                        float ends = Smooth01(Mathf.Min(v, 1f - v) / 0.12f);
                        float a = band * ends * (0.62f + 0.5f * n2);
                        float groove = 0f;
                        foreach (var gu in new[] { 0.36f, 0.49f, 0.63f }) groove = Mathf.Max(groove, 1f - Smooth01(Mathf.Abs(u - gu - (Noise(0, y, 30f, 3) - 0.5f) * 0.04f) / 0.022f));
                        a = Mathf.Max(a, groove * ends * 0.95f);
                        var c = Color.Lerp(new Color(0.23f, 0.16f, 0.1f), new Color(0.07f, 0.045f, 0.03f), Mathf.Max(groove, n2 * 0.5f));
                        px[y * w + x] = new Color(c.r, c.g, c.b, Mathf.Clamp01(a));
                    }
                tex.SetPixels32(px); tex.Apply();
                File.WriteAllBytes(tp, tex.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(tp);
                L("made " + tp);
            }
            var ti = AssetImporter.GetAtPath(tp) as TextureImporter;
            if (ti && (!ti.alphaIsTransparency || ti.wrapMode != TextureWrapMode.Clamp || !ti.mipMapsPreserveCoverage))
            { ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp; ti.mipMapsPreserveCoverage = true; ti.alphaTestReferenceValue = 0.4f; ti.SaveAndReimport(); }
            var t2 = AssetDatabase.LoadAssetAtPath<Texture2D>(tp);
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            EnsureFolder(ZoneMatDir);
            string mp = ZoneMatDir + "/M_BV_DragMark.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (!m) { m = new Material(lit) { name = "M_BV_DragMark" }; AssetDatabase.CreateAsset(m, mp); }
            m.SetTexture("_BaseMap", t2); m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.4f); m.EnableKeyword("_ALPHATEST_ON"); m.SetFloat("_Smoothness", 0.18f);
            m.renderQueue = (int)RenderQueue.AlphaTest; m.SetOverrideTag("RenderType", "TransparentCutout"); m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>the creature's Death pose, last frame, baked into one static mesh (Generated/BoneValley/ME_BV_Dead_&lt;species&gt;), pivot at the
        /// bottom centre; made once (arg rebake makes it again). mats = the model's materials per sub-mesh.</summary>
        static Mesh BVDeadMesh(string species, bool rebake, out Material[] mats)
        {
            mats = null;
            string fbx = $"{BV_DinoDir}/{species}/Model/DINO_{species}.fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (!model) { W("missing model " + fbx); return null; }
            var mlist = new List<Material>();
            foreach (var s in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (s.sharedMesh) for (int i = 0; i < s.sharedMesh.subMeshCount; i++) mlist.Add(i < s.sharedMaterials.Length ? s.sharedMaterials[i] : s.sharedMaterial);
            mats = mlist.ToArray();
            EnsureFolder(BV_Gen);
            string mp = $"{BV_Gen}/ME_BV_Dead_{species}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(mp);
            if (existing && !rebake) return existing;
            var clip = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().FirstOrDefault(c => c.name == "Death");
            var inst = (GameObject)UnityEngine.Object.Instantiate(model);
            inst.hideFlags = HideFlags.HideAndDontSave;
            var temp = new List<Mesh>();
            try
            {
                inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); inst.transform.localScale = Vector3.one;
                if (clip) clip.SampleAnimation(inst, clip.length); else W(species + ": no Death clip, the bind pose is baked");
                var ci = new List<CombineInstance>();
                foreach (var s in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!s.sharedMesh) continue;
                    var baked = new Mesh(); s.BakeMesh(baked, true); temp.Add(baked);
                    var m = Matrix4x4.TRS(s.transform.position, s.transform.rotation, Vector3.one);
                    for (int i = 0; i < baked.subMeshCount; i++) ci.Add(new CombineInstance { mesh = baked, subMeshIndex = i, transform = m });
                }
                var mesh = new Mesh { name = "ME_BV_Dead_" + species, indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(ci.ToArray(), false, true);
                mesh.RecalculateBounds();
                var b = mesh.bounds; var off = new Vector3(-b.center.x, -b.min.y, -b.center.z);
                var vs = mesh.vertices; for (int i = 0; i < vs.Length; i++) vs[i] += off; mesh.vertices = vs;
                mesh.RecalculateBounds();
                if (mesh.vertexCount < 65000) mesh.indexFormat = IndexFormat.UInt16;
                if (existing) { EditorUtility.CopySerialized(mesh, existing); UnityEngine.Object.DestroyImmediate(mesh); mesh = existing; EditorUtility.SetDirty(mesh); }
                else AssetDatabase.CreateAsset(mesh, mp);
                L($"baked {species} death pose ({(clip ? "Death @ " + F(clip.length) + " s" : "bind pose")}): {mesh.vertexCount} verts, {mats.Length} sub-meshes, size {V(mesh.bounds.size)} -> {mp}");
                return mesh;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(inst);
                foreach (var t in temp) UnityEngine.Object.DestroyImmediate(t);
            }
        }

        // ------------------------------------------------------------------ small placers
        static GameObject BVMeshObj(Transform parent, string name, Mesh mesh, Material[] mats, Vector3 pos, Quaternion rot, bool shadows = true)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterials = mats; r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }
        static GameObject BVDecal(Transform parent, string name, Mesh quad, Material m, Vector3 at, float sx, float sz, float yaw)
        {
            var g = Ground(at); var n = Normal(g);
            var go = BVMeshObj(parent, name, quad, new[] { m }, g + n * 0.03f, Quaternion.FromToRotation(Vector3.up, n) * Quaternion.Euler(0f, yaw, 0f), false);
            go.transform.localScale = new Vector3(sx, 1f, sz);
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            return go;
        }
        static Examinable BVExam(GameObject go, string id, float range, string verb = "Examine")
        {
            if (!go) return null;
            var e = go.GetComponent<Examinable>(); if (!e) e = go.AddComponent<Examinable>();
            e.discoveryId = id; e.SaveId = id; e.displayName = "Something"; e.verb = verb; e.range = range; e.eventType = GameEventType.Discovery; e.once = true;
            return e;
        }
        static EnvLocation BVAnchor(Transform parent, string name, Vector3 pos, Vector3 fwd, float radius, string note)
        {
            fwd = BVFlat(fwd); var t = Marker(parent, name, pos, fwd.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(fwd.normalized) : Quaternion.identity);
            var el = t.GetComponent<EnvLocation>(); if (!el) el = t.gameObject.AddComponent<EnvLocation>();
            el.id = name.ToLowerInvariant(); el.radius = radius; el.note = note;
            return el;
        }
        /// <summary>first prefab of the list that exists (ART's Phase 2 kit first, then the PC kit)</summary>
        static GameObject BVPrefab(params string[] names) { foreach (var n in names) { var p = Prefab(n, false); if (p) return p; } W("none of these prefabs found: " + string.Join(", ", names)); return null; }

        // ------------------------------------------------------------------ props
        static void BVProps(Zone z, BVPlan P, Transform root, bool rebake)
        {
            var gCar = Group(z, "Carcasses"); var gBones = Group(z, "Bones"); var gTracks = Group(z, "Tracks"); var gTrees = Group(z, "Trees");
            var gEnt = Group(z, "Entrance"); var gLm = Group(z, "Landmark"); var gAi = Group(z, "AI_Anchors");
            var quad = BVQuad();
            var blood = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Environment/Materials/M_Env_BloodDried.mat");
            var drag = BVDragMaterial();
            var mud = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/M_FootprintMud.mat");
            var fresh = mud ? BVMat("M_BV_FootprintFresh", mud, m => BVTint(m, new Color(0.62f, 0.55f, 0.5f), 0.5f)) : null;
            var fpRoot = SceneRoots.Find(SceneRoots.Interactables + "/GiantFootprints"); var fp = fpRoot ? fpRoot.Find("Footprint_0") : null;
            var fpMf = fp ? fp.GetComponent<MeshFilter>() : null; var fpMesh = fpMf ? fpMf.sharedMesh : null;
            if (!fpMesh || !mud) W("no GiantFootprints/Footprint_0 mesh or M_FootprintMud: prints skipped");
            var bonesPf = BVPrefab("PROP_PC_Bones");
            var scatterPf = Prefab("PROP_P2_BoneScatter_A", false) ?? bonesPf; var scatterB = Prefab("PROP_P2_BoneScatter_B", false) ?? scatterPf;
            var ribPf = Prefab("PROP_P2_Ribcage", false); var skullPf = Prefab("PROP_P2_Skull_Large", false);
            L($"ART kit: BoneScatter_A/B {(Prefab("PROP_P2_BoneScatter_A", false) ? "yes" : "no (PROP_PC_Bones)")}, Ribcage {(ribPf ? "yes" : "no")}, Skull_Large {(skullPf ? "yes" : "no")}, LM_FossilSkeleton {(HasPrefab("LM_FossilSkeleton") ? "yes" : "no (marker)")}");
            int nBones = 0, nPrints = 0, nDecals = 0, nEx = 0, nTrees = 0, nSnags = 0;
            GameObject Bone(Transform parent, GameObject pf, Vector3 p, float yaw, float s, string name, float sink = 0.04f)
            { var go = Place(parent, pf, p, yaw, s, true, sink); if (go) { go.name = name; nBones++; } return go; }
            GameObject Print(Transform parent, string name, Vector3 p, Vector3 dir, bool left, Material m, float scale = 1.15f)
            {
                if (!fpMesh || !m) return null;
                dir = BVFlat(dir); if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward; dir.Normalize();
                var side = new Vector3(-dir.z, 0f, dir.x) * (left ? 0.8f : -0.8f);
                var g = Ground(p + side); var n = Normal(g);
                var go = BVMeshObj(parent, name, fpMesh, new[] { m }, g + n * 0.02f, Quaternion.FromToRotation(Vector3.up, n) * Quaternion.LookRotation(dir), false);
                go.transform.localScale = Vector3.one * scale; nPrints++;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
                return go;
            }

            // ---- 1. fresh, half-eaten carcass (knife: meat, hide, bone)
            var paraMesh = BVDeadMesh("Parasaurolophus", rebake, out var paraMats);
            GameObject freshGo = null;
            if (paraMesh)
            {
                var n = Normal(P.fresh);
                freshGo = BVMeshObj(gCar, "BV_Carcass_Fresh", paraMesh, paraMats, P.fresh + Vector3.down * 0.12f, Quaternion.FromToRotation(Vector3.up, Vector3.Lerp(Vector3.up, n, 0.8f)) * Quaternion.Euler(0f, P.freshYaw, 0f));
                var b = paraMesh.bounds; bool alongZ = b.size.z >= b.size.x;
                var cap = freshGo.AddComponent<CapsuleCollider>(); cap.direction = alongZ ? 2 : 0; cap.center = b.center;
                cap.radius = Mathf.Clamp(Mathf.Min(b.extents.y, alongZ ? b.extents.x : b.extents.z), 0.3f, 1.3f); cap.height = Mathf.Max(cap.radius * 2f, (alongZ ? b.size.z : b.size.x) * 0.9f);
                var car = freshGo.AddComponent<Carcass>(); car.displayName = "half-eaten carcass"; car.creatureId = "parasaurolophus"; car.meat = 3; car.hide = 2; car.bone = 2; car.expireHours = 100000f; car.body = freshGo; car.SaveId = "bv_carcass_fresh";
                var zc = freshGo.AddComponent<ZoneCarcass>(); zc.stage = ZoneCarcass.Stage.Fresh; zc.id = "bv_carcass_fresh"; zc.creatureId = "parasaurolophus"; zc.displayName = "half-eaten carcass";
                zc.meat = 3; zc.hide = 2; zc.bone = 2; zc.expireHours = 100000f; zc.radius = Mathf.Max(b.extents.x, b.extents.z);
                if (blood) for (int k = 0; k < 6; k++)
                {
                    float an = k * 1.05f + Rand01(k, 41) * 0.6f, r = zc.radius * (0.3f + Rand01(k, 42) * 0.55f);
                    BVDecal(gCar, $"FreshBlood_{k}", quad, blood, P.fresh + new Vector3(Mathf.Sin(an), 0, Mathf.Cos(an)) * r, 0.8f + Rand01(k, 43) * 0.9f, 0.7f + Rand01(k, 44) * 0.8f, Rand01(k, 45) * 360f); nDecals++;
                }
            }
            // drag marks + blood from the path to the body
            var dragDir = BVFlat(P.fresh - P.dragFrom); float dragLen = dragDir.magnitude; dragDir = dragLen > 0.01f ? dragDir / dragLen : Vector3.forward;
            GameObject firstDrag = null; int segs = Mathf.Max(1, Mathf.CeilToInt((dragLen - 1.5f) / 2f));
            for (int i = 0; i < segs; i++)
            {
                var c = P.dragFrom + dragDir * (0.8f + (i + 0.5f) * (dragLen - 1.5f) / segs) + new Vector3(-dragDir.z, 0, dragDir.x) * ((Rand01(i, 51) - 0.5f) * 0.4f);
                var go = BVDecal(gTracks, $"DragMark_{i}", quad, drag, c, 1.15f, 2.5f, Quaternion.LookRotation(dragDir).eulerAngles.y + (Rand01(i, 52) - 0.5f) * 8f); nDecals++;
                if (!firstDrag) firstDrag = go;
                if (blood && i % 2 == 0) { BVDecal(gTracks, $"DragBlood_{i}", quad, blood, c + new Vector3(-dragDir.z, 0, dragDir.x) * 0.7f, 0.45f, 0.6f, Rand01(i, 53) * 360f); nDecals++; }
            }
            if (firstDrag && BVExam(firstDrag, "bv_drag_marks", 3f)) nEx++;

            // ---- 2. rotting carcass, days old (sunk, dark, bones showing)
            var trikeMesh = BVDeadMesh("Triceratops", rebake, out var trikeMats);
            if (trikeMesh)
            {
                var rotMats = trikeMats.Select((m, i) => m ? BVMat($"M_BV_Rot_Triceratops_{i}", m, x => BVTint(x, new Color(0.4f, 0.34f, 0.29f), 0.42f)) : m).ToArray();
                var b = trikeMesh.bounds; var n = Normal(P.rot);
                var rotGo = BVMeshObj(gCar, "BV_Carcass_Rotting", trikeMesh, rotMats, P.rot + Vector3.down * b.size.y * 0.3f, Quaternion.FromToRotation(Vector3.up, Vector3.Lerp(Vector3.up, n, 0.8f)) * Quaternion.Euler(0f, P.rotYaw, 0f));
                GameObjectUtility.SetStaticEditorFlags(rotGo, StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.BatchingStatic);
                bool alongZ = b.size.z >= b.size.x;
                var cap = rotGo.AddComponent<CapsuleCollider>(); cap.direction = alongZ ? 2 : 0; cap.center = b.center + Vector3.up * b.size.y * 0.1f;
                cap.radius = Mathf.Clamp(Mathf.Min(b.extents.y, alongZ ? b.extents.x : b.extents.z) * 0.8f, 0.3f, 1.4f); cap.height = Mathf.Max(cap.radius * 2f, (alongZ ? b.size.z : b.size.x) * 0.85f);
                var zc = rotGo.AddComponent<ZoneCarcass>(); zc.stage = ZoneCarcass.Stage.Rotting; zc.id = "bv_carcass_rotting"; zc.creatureId = "triceratops"; zc.displayName = "rotting carcass"; zc.meat = zc.hide = zc.bone = 0; zc.radius = Mathf.Max(b.extents.x, b.extents.z);
                if (BVExam(rotGo, "bv_rotting_carcass", 5f)) nEx++;
                if (bonesPf) for (int k = 0; k < 3; k++) { float an = P.rotYaw * Mathf.Deg2Rad + k * 2.1f; Bone(gCar, bonesPf, P.rot + new Vector3(Mathf.Sin(an), 0, Mathf.Cos(an)) * (zc.radius * 0.8f + 0.8f), Rand01(k, 61) * 360f, 0.9f + Rand01(k, 62) * 0.2f, $"RotBones_{k}"); }
                if (blood) for (int k = 0; k < 4; k++) { float an = k * 1.6f; BVDecal(gCar, $"RotStain_{k}", quad, blood, P.rot + new Vector3(Mathf.Sin(an), 0, Mathf.Cos(an)) * zc.radius * 0.6f, 1.6f, 1.3f, Rand01(k, 63) * 360f); nDecals++; }
            }

            // ---- 3. old bone pile at the canyon mouth
            var oldGo = new GameObject("BV_Carcass_Old"); oldGo.transform.SetParent(gCar, false); oldGo.transform.position = P.old;
            {
                var zc = oldGo.AddComponent<ZoneCarcass>(); zc.stage = ZoneCarcass.Stage.Old; zc.id = "bv_carcass_old"; zc.creatureId = ""; zc.displayName = "old bones"; zc.meat = zc.hide = zc.bone = 0; zc.radius = 2.5f;
                GameObject first = null;
                if (ribPf) first = Bone(oldGo.transform, ribPf, P.old, 70f, 1f, "Ribcage", 0.25f);
                for (int k = 0; k < 4; k++) { float an = k * 1.7f + 0.4f; var go = Bone(oldGo.transform, k % 2 == 0 ? scatterPf : bonesPf, P.old + new Vector3(Mathf.Sin(an), 0, Mathf.Cos(an)) * (1.2f + k * 0.5f), Rand01(k, 71) * 360f, 0.85f + Rand01(k, 72) * 0.3f, $"OldBones_{k}"); if (!first) first = go; }
                if (skullPf) Bone(oldGo.transform, skullPf, P.old + new Vector3(1.8f, 0, -1.2f), 140f, 0.9f, "Skull", 0.2f);
                if (first && BVExam(first, "bv_old_bones", 3.5f)) nEx++;
            }

            // ---- 4. entrance: a bone line across the path, a skull at its side, fresh prints branching off to the carcass
            var eDir = P.entranceDir; var eSide = new Vector3(-eDir.z, 0f, eDir.x);
            if (bonesPf)
            {
                var lineAt = P.entrance + eDir * 1.5f;
                for (int k = -2; k <= 2; k++) Bone(gEnt, k % 2 == 0 ? scatterPf : bonesPf, lineAt + eSide * (k * 1.5f) + eDir * ((Rand01(k + 5, 81) - 0.5f) * 0.8f), Quaternion.LookRotation(eSide).eulerAngles.y + (Rand01(k + 5, 82) - 0.5f) * 40f, 0.8f + Rand01(k + 5, 83) * 0.25f, $"BoneLine_{k + 2}");
            }
            var skullAt = P.entrance + eSide * 2.6f + eDir * 0.6f;
            var skull = skullPf ? Bone(gEnt, skullPf, skullAt, Quaternion.LookRotation(-eDir).eulerAngles.y, 1f, "Skull_Entrance", 0.15f)
                                : Bone(gEnt, bonesPf, skullAt, Quaternion.LookRotation(-eDir).eulerAngles.y, 1.3f, "Skull_Entrance_Placeholder", 0.04f);
            if (skull && BVExam(skull, "bv_bone_line", 3.5f)) nEx++;
            GameObject firstFresh = null;
            {
                // prints from the entrance down to the fresh carcass (a big hunter, hours ago)
                var from = P.entrance + eDir * 3f; var to = P.fresh; var d = BVFlat(to - from); float len = d.magnitude; d = len > 0.01f ? d / len : eDir;
                int k = 0;
                for (float s = 0f; s < len - 3f && k < 14; s += 2.8f, k++)
                {
                    var bend = new Vector3(-d.z, 0, d.x) * Mathf.Sin(s / Mathf.Max(1f, len) * Mathf.PI) * 2.5f;
                    var go = Print(gTracks, $"FreshPrint_{k}", from + d * s + bend, d, k % 2 == 0, fresh);
                    if (!firstFresh) firstFresh = go;
                }
                if (firstFresh && BVExam(firstFresh, "bv_fresh_tracks", 3.5f)) nEx++;
                // prints out: from the carcass back to the path, then along it to the canyon mouth
                float outArc = BVArcOf(new Vector3(-122f, 0f, -64f), out _);
                var join = BVAt(outArc, out _);
                var d2 = BVFlat(join - P.fresh); float l2 = d2.magnitude; d2 = l2 > 0.01f ? d2 / l2 : Vector3.forward; int j = 0;
                for (float s = 3f; s < l2; s += 2.8f, j++) Print(gTracks, $"OutPrint_{j}", P.fresh + d2 * s, d2, j % 2 == 0, fresh);
                for (float s = outArc + 1.4f; s < BVPathLen - 2f; s += 2.8f, j++) { var p = BVAt(s, out var dd); Print(gTracks, $"OutPrint_{j}", p, dd, j % 2 == 0, mud); }
            }

            // ---- 5. old bones over the basin (more toward the carcasses and the kill site)
            {
                var hubs = new[] { P.fresh, P.rot, P.landmark + P.lmFwd * (P.lmHz + 4f), P.old, P.entrance };
                int placed = 0;
                for (int k = 0; k < 90 && placed < 14; k++)
                {
                    var hub = hubs[k % hubs.Length];
                    float an = Rand01(k, 91) * Mathf.PI * 2f, r = 6f + Rand01(k, 92) * 14f;
                    var p = Ground(hub + new Vector3(Mathf.Sin(an), 0, Mathf.Cos(an)) * r);
                    if (Slope(p) > 28f || BVPathDist(p) < 1.5f || !BVOk(z, p, 0.6f, 0.6f)) continue;
                    if (P.feats.Any(f => BVFlat(f.p - p).magnitude < 3.5f) || P.InLandmark(p, 1f)) continue;
                    Bone(gBones, placed % 3 == 0 ? scatterPf : placed % 3 == 1 ? scatterB : bonesPf, p, Rand01(k, 93) * 360f, 0.7f + Rand01(k, 94) * 0.45f, $"Bones_{placed}"); placed++;
                }
            }

            // ---- 6. claw-marked snags beside the path (gouges face it) and snapped trees
            var snagPf = BVPrefab("PROP_PC_ClawSnag");
            var snagSpots = new List<Vector3>();
            foreach (var s0 in new[] { P.entranceArc + 6f, BVArcOf(P.landmark, out _), BVPathLen - 10f })
            {
                var pp = BVAt(s0, out var dd); var side = new Vector3(-dd.z, 0, dd.x) * (snagSpots.Count % 2 == 0 ? 1f : -1f);
                if (!BVFind(pp + side * 5f, 3f, p => BVPathDist(p) < 3.5f || Slope(p) > 30f || !BVOk(z, p, 1.2f, 0.5f) || P.InLandmark(p, 2f) ? float.NegativeInfinity : 0f, out var sp)) continue;
                var toPath = BVFlat(sp - pp); var go = Place(gTrees, snagPf, sp, toPath.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(toPath.normalized).eulerAngles.y : 0f, 0.95f + Rand01(nSnags, 101) * 0.15f, false, 0.3f);
                if (go) { go.name = $"ClawSnag_{nSnags}"; snagSpots.Add(sp); nSnags++; }
            }
            var btA = BVPrefab("PROP_PC_BrokenTree_A"); var btB = BVPrefab("PROP_PC_BrokenTree_B");
            var treePrefs = new[] { new Vector3(-104f, 0, -50f), new Vector3(-126f, 0, -58f), new Vector3(-100f, 0, -70f), new Vector3(-121f, 0, -72f), new Vector3(-131f, 0, -80f) };
            foreach (var tp in treePrefs)
            {
                if (!BVFind(tp, 5f, p => BVPathDist(p) < 3.5f || Slope(p) > 32f || !BVOk(z, p, 1.6f, 0.5f) || P.InLandmark(p, 3f) || P.feats.Any(f => BVFlat(f.p - p).magnitude < f.r * 0.8f) ? float.NegativeInfinity : 0f, out var sp)) continue;
                var go = Place(gTrees, nTrees % 2 == 0 ? btA : btB, sp, Rand01(nTrees, 111) * 360f, 0.9f + Rand01(nTrees, 112) * 0.3f, false, 0.2f);
                if (go) { go.name = $"BrokenTree_{nTrees}"; nTrees++; }
            }

            // ---- 7. landmark
            Transform lmT;
            var lmPf = Prefab("LM_FossilSkeleton", false);
            var lmPos = P.landmark + Vector3.down * (0.35f + P.lmResid * 0.6f) * P.lmScale;
            if (lmPf && !P.lmBlocked)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(lmPf, gLm); go.name = "LM_FossilSkeleton";
                go.transform.SetPositionAndRotation(lmPos, P.lmRot); go.transform.localScale = Vector3.one * P.lmScale;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.BatchingStatic);
                if (BVExam(go, "bv_fossil_skeleton", 9f)) nEx++;
                lmT = go.transform;
            }
            else lmT = Marker(gLm, "LM_BoneValley", lmPos, P.lmRot);
            L($"landmark: {(lmPf && !P.lmBlocked ? "LM_FossilSkeleton placed" : lmPf ? "marker LM_BoneValley (no clear footprint for the prefab)" : "marker LM_BoneValley (ART prefab not delivered)")} at {V(lmT.position)} scale {F(P.lmScale)} yaw {F(P.landmarkYaw)} (exposed side faces {V(P.lmFwd)}, bank behind)");

            // ---- 8. AI anchors
            var gSc = Group(z, "AI_Anchors/Scavenge"); var gRt = Group(z, "AI_Anchors/PredatorRoute"); var gPe = Group(z, "AI_Anchors/Perch");
            int nSc = 0;
            void Scav(string key, Vector3 c, float r, int count)
            {
                for (int k = 0; k < count; k++)
                {
                    float an = k * Mathf.PI * 2f / count + 0.5f; var p = Ground(c + new Vector3(Mathf.Sin(an), 0, Mathf.Cos(an)) * r);
                    BVAnchor(gSc, $"BV_Scavenge_{key}_{k}", p, c - p, 1.5f, $"scavenge point at the {key} carcass (faces it)"); nSc++;
                }
            }
            if (freshGo) Scav("fresh", P.fresh, freshGo.GetComponent<ZoneCarcass>().radius * 0.7f + 2f, 4);
            Scav("rotting", P.rot, 4.5f, 3); Scav("old", P.old, 3f, 2);
            var skelP = Feat != null && Feat.props != null ? Ground(P3(Feat.props.skeleton)) : Vector3.zero;
            if (skelP != Vector3.zero) Scav("killsite", skelP, 5f, 2);
            var route = new List<(string, Vector3)> { ("entrance", P.entrance - P.entranceDir * 6f), ("bone_line", P.entrance + P.entranceDir * 3f), ("fresh", P.fresh + BVFlat(P.dragFrom - P.fresh).normalized * 3f),
                ("killsite", skelP != Vector3.zero ? skelP + new Vector3(4f, 0, -3f) : P.fresh), ("rotting", P.rot + Vector3.right * 5f), ("path_west", BVAt(BVArcOf(new Vector3(-122f, 0, -64f), out _), out _)),
                ("landmark_front", Ground(P.landmark + P.lmFwd * (P.lmHz + 3f))), ("canyon_mouth", P.old + Vector3.right * 4f), ("exit_canyon", BVAt(BVPathLen, out _)) };
            for (int i = 0; i < route.Count; i++)
            {
                var nxt = route[Mathf.Min(i + 1, route.Count - 1)].Item2; var fwd = i + 1 < route.Count ? nxt - route[i].Item2 : route[i].Item2 - route[i - 1].Item2;
                BVAnchor(gRt, $"BV_PredRoute_{i:00}", Ground(route[i].Item2), fwd, 3f, "predator visit route: " + route[i].Item1);
            }
            // perches: snapped-tree / snag tops, then high rim points over the basin
            int nPe = 0;
            foreach (Transform t in gTrees)
            {
                var rs = t.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) continue;
                var bb = rs[0].bounds; foreach (var r in rs) bb.Encapsulate(r.bounds);
                if (bb.size.y < 3f) continue;
                BVAnchor(gPe, $"BV_Perch_{nPe++}", new Vector3(t.position.x, bb.max.y, t.position.z), P.fresh - t.position, 1f, $"pteranodon perch: top of {t.name} ({F(bb.max.y - GroundY(t.position))} m)");
                if (nPe >= 4) break;
            }
            var rim = new List<Vector3>();
            for (float x = -135f; x <= -95f; x += 4f)
                for (float zz = -98f; zz <= -62f; zz += 4f)
                {
                    var p = Ground(new Vector3(x, 0, zz)); if (OwnWeight(z, p) < 0.5f || Slope(p) > 22f || p.y < P.fresh.y + 8f) continue;
                    rim.Add(p);
                }
            foreach (var p in rim.OrderByDescending(q => q.y - BVFlat(q - P.fresh).magnitude * 0.12f))
            {
                if (nPe >= 7) break;
                if (gPe.Cast<Transform>().Any(t => BVFlat(t.position - p).magnitude < 14f)) continue;
                BVAnchor(gPe, $"BV_Perch_{nPe++}", p, P.fresh - p, 1.5f, $"pteranodon perch: rim over the basin, {F(p.y - P.fresh.y)} m above the fresh carcass");
            }
            var cc = (P.fresh + P.rot + skelP) / 3f; cc = Ground(cc) + Vector3.up * 28f;
            BVAnchor(gAi, "BV_CircleCentre", cc, Vector3.forward, 26f, "pteranodons circle here over the carcasses (radius = circle), 28 m above the basin floor");
            L($"props: {nBones} bone props, {nPrints} prints, {nDecals} decals (drag marks, blood), {nSnags} claw snags, {nTrees} broken trees, {nEx} Examinables; carcasses: fresh {(freshGo ? "yes" : "NO")}, rotting {(trikeMesh ? "yes" : "NO")}, old yes");
            L($"AI anchors: {nSc} scavenge, {route.Count} predator route, {nPe} perches, 1 circle centre");
            foreach (var zc in gCar.GetComponentsInChildren<ZoneCarcass>()) L($"  carcass {zc.id} ({zc.stage}, {zc.creatureId}) at {V(zc.transform.position)} r {F(zc.radius)}{(zc.GetComponent<Carcass>() ? " + Carcass (butcher: meat " + zc.meat + ", hide " + zc.hide + ", bone " + zc.bone + ")" : "")}");
            foreach (var e in root.GetComponentsInChildren<Examinable>()) L($"  examinable {e.discoveryId} at {V(e.transform.position)}");
        }

        // ------------------------------------------------------------------ check (read only)
        [PrimalBridgeCommand]
        public static string BoneValleyCheck(string arg)
        {
            Begin("BV", "BoneValleyCheck " + arg);
            if (!OpenIsland(out var scene, out _)) return End();
            if (!FindTerrain()) { W("no terrain"); return End(); }
            LoadFeat(); BVLoadPath();
            var z = ZoneById("bone"); var root = ZoneRoot(z);
            int problems = CheckZone(z, new[] { root }, BV_TreeKey, scene);
            var cars = root.GetComponentsInChildren<ZoneCarcass>(true);
            L($"ZoneCarcass: {cars.Length} ({string.Join(", ", cars.Select(q => q.id + " " + q.stage))}); with Carcass: {cars.Count(q => q.GetComponent<Carcass>() != null)}");
            foreach (var c in cars.Where(q => q.GetComponent<Carcass>() != null))
            {
                var car = c.GetComponent<Carcass>(); var mf = c.GetComponent<MeshFilter>(); var col = c.GetComponent<CapsuleCollider>();
                L($"  {c.id}: mesh {(mf && mf.sharedMesh ? mf.sharedMesh.name + " " + mf.sharedMesh.vertexCount + " verts" : "MISSING")}, collider {(col ? "capsule r " + F(col.radius) + " h " + F(col.height) : "none")}, SaveId {car.SaveId}, loot preset {c.meat}/{c.hide}/{c.bone}, ground dy {F(c.transform.position.y - GroundY(c.transform.position))}");
                if (!mf || !mf.sharedMesh) problems++;
            }
            var anchors = root.Find("AI_Anchors"); var els = anchors ? anchors.GetComponentsInChildren<EnvLocation>() : new EnvLocation[0];
            L($"AI anchors: {els.Length} (scavenge {els.Count(e => e.name.StartsWith("BV_Scavenge"))}, route {els.Count(e => e.name.StartsWith("BV_PredRoute"))}, perch {els.Count(e => e.name.StartsWith("BV_Perch"))}, circle {els.Count(e => e.name == "BV_CircleCentre")})");
            var exs = root.GetComponentsInChildren<Examinable>(true);
            L($"Examinables: {exs.Length} ({string.Join(", ", exs.Select(e => e.discoveryId))}); duplicate SaveIds: {exs.GroupBy(e => e.SaveId).Count(g => g.Count() > 1)}");
            var lm = root.Find("Landmark"); L($"landmark: {(lm && lm.childCount > 0 ? string.Join(", ", lm.Cast<Transform>().Select(t => t.name + " " + V(t.position))) : "NONE")}");
            // resource nodes within 2 m of a BONE prop (visual crowding, not only colliders)
            int crowd = 0; var props = root.GetComponentsInChildren<Renderer>().Select(r => r.transform.position).ToList();
            foreach (var n in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ResourceNode>(false)))
                if (z.Weight(n.transform.position) > 0f && props.Any(p => BVFlat(p - n.transform.position).magnitude < 2f)) { crowd++; L($"  resource node within 2 m of a BONE prop: {PathOf(n.transform)} {V(n.transform.position)}"); }
            L($"resource nodes within 2 m of a BONE prop: {crowd}");
            L($"renderers {root.GetComponentsInChildren<Renderer>(true).Length}, colliders {root.GetComponentsInChildren<Collider>(true).Count(c => !c.isTrigger)} solid / {root.GetComponentsInChildren<Collider>(true).Count(c => c.isTrigger)} trigger, lights {root.GetComponentsInChildren<Light>(true).Length}");
            L($"problems: {problems}");
            return End();
        }
    }
}
