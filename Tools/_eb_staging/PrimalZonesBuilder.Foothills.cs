using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using PrimalFrontier.World;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// ENV-B (EB_) Phase 2, zone 5 VOLCANIC FOOTHILLS: the upper canyon (z &lt; -100; BONE owns the mouth) up to the volcanic ridge
    /// (-122, 44, -216), core zone "foothills" (capsule r 40 + 30 m blend). Bridge command <c>PrimalZonesBuilder.Foothills</c>
    /// ("dry" = survey only, "ridge=x,z" moves the landmark spot). A band value s (0 at z -108, 1 near z -203, a little
    /// height and noise) drives the transition forest -> dry vegetation -> black rock -> ash -> the Phase 1 volcanic ground:
    ///   splat (from the EB backup, OwnWeight blended, faded out at the canyon mouth): dry grass / dusty soil, basalt Rock +
    ///   DarkSoil, Ash + DarkSoil; the Phase 1 ash is never reduced (max) and the ground round the vent is left as it was;
    ///   details: green layers thinned upward, a yellow-brown dry grass layer (DET_EB_DryGrass, recoloured copy of the grass);
    ///   own terrain trees (cycads in the dry band, a few tree ferns low down, key "EB_foothills"); placed dead / broken trees,
    ///   dry fallen trunks and basalt groups under World/Environment/Volcano/VolcanicFoothills; FX_Anchors for WORLD; the
    ///   landmark placeholder LM_VolcanicFoothills (ART's LM_BlackRidge goes under it once the prefab exists).
    /// Never touches World/Environment/Volcano/{Lava, Basalt, Hazards, Landmarks} or the offshore volcano.
    /// </summary>
    public static partial class PrimalZonesBuilder
    {
        [Serializable] class EBLine { public float[] points; }
        [Serializable] class EBLava { public float[] points; public float[] vent; }
        [Serializable] class EBFeat { public EBLine canyon; public EBLava lava; }
        static List<Vector3> _footCanyon, _footLava; static Vector3 _footVent = new Vector3(-100f, 44.7f, -232f);
        static readonly Vector2 FootRidgeDefault = new Vector2(-150f, -198f);
        static Vector3 _footRidge;
        static Quaternion _footRidgeRot = Quaternion.identity;
        static Bounds _footRidgeB = new Bounds(Vector3.zero, new Vector3(20f, 10f, 20f));
        static Vector3 FootExit => _footCanyon != null && _footCanyon.Count > 0 ? _footCanyon[_footCanyon.Count - 1] : new Vector3(-162f, 42.5f, -210f);
        /// <summary>local -Z (ART: the steep face) towards the canyon exit, plus a yaw offset</summary>
        static Quaternion FootRidgeRotAt(Vector3 c, float yawOff)
        {
            var away = c - FootExit; away.y = 0f;
            var q = away.sqrMagnitude > 1e-3f ? Quaternion.LookRotation(away.normalized, Vector3.up) : Quaternion.identity;
            return Quaternion.Euler(0f, yawOff, 0f) * q;
        }
        /// <summary>is p inside the landmark footprint (local bounds rectangle + margin)?</summary>
        static bool FootInRidge(Vector3 p, float margin) => FootInRect(p, _footRidge, _footRidgeRot, margin);
        static bool FootInRect(Vector3 p, Vector3 c, Quaternion rot, float margin)
        {
            var q = Quaternion.Inverse(rot) * new Vector3(p.x - c.x, 0f, p.z - c.z); var b = _footRidgeB;
            return q.x > b.min.x - margin && q.x < b.max.x + margin && q.z > b.min.z - margin && q.z < b.max.z + margin;
        }
        /// <summary>
        /// with ART's LM_BlackRidge: a temporary instance is tried at poses round the requested spot (closest first, -6..6 m,
        /// yaw -30..30 deg, steep face towards the canyon exit first, then turned round); the first pose whose real colliders
        /// touch no canyon path probe, no lava probe and no gameplay object (resource node 0.55 m probe, others 0.4 m) wins
        /// </summary>
        static bool FootFitRidge(Zone z, Vector3 want, UnityEngine.SceneManagement.Scene scene)
        {
            var art = Prefab("LM_BlackRidge", false); if (!art) return false;
            _footRidgeB = EBPrefabBounds(art);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(art);
            var mine = new HashSet<Collider>(go.GetComponentsInChildren<Collider>(true));
            float reach = Mathf.Max(_footRidgeB.extents.x, _footRidgeB.extents.z) + 12f;
            var probes = new List<(Vector3 c, float r)>();
            for (int i = 0; i + 1 < _footCanyon.Count; i++)
                for (float f = 0f; f < 1f; f += 0.25f) { var q = Vector3.Lerp(_footCanyon[i], _footCanyon[i + 1], f); if (q.z <= -100f && EBFlat(q, want) < reach) probes.Add((Ground(q) + Vector3.up * 1.2f, 2.6f)); }
            int nCanyon = probes.Count;
            for (int i = 0; i + 1 < _footLava.Count; i++) { var q = _footLava[i]; if (EBFlat(q, want) < reach) probes.Add((Ground(q) + Vector3.up * 0.5f, 3f)); }
            int nLava = probes.Count - nCanyon;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var it in root.GetComponentsInChildren<Interactable>(false))
                {
                    var q = it.transform.position; if (EBFlat(q, want) >= reach) continue;
                    probes.Add((q + Vector3.up * 0.5f, it is ResourceNode ? 0.55f : 0.4f));
                }
            var poses = new List<(Vector3 c, float yaw, float cost)>();
            for (float dx = -6f; dx <= 6.01f; dx += 1f) for (float dz = -6f; dz <= 6.01f; dz += 1f)
                    foreach (float flip in new[] { 0f, 180f })
                        for (float yo = -30f; yo <= 30.01f; yo += 5f)
                            poses.Add((new Vector3(want.x + dx, 0f, want.z + dz), flip + yo, Mathf.Sqrt(dx * dx + dz * dz) + 0.1f * Mathf.Abs(yo) + (flip > 0f ? 4f : 0f)));
            poses.Sort((x, y) => x.cost.CompareTo(y.cost));
            bool found = false; int tried = 0, bestHits = int.MaxValue; Vector3 bc = Ground(want); float by = 0f;
            foreach (var ps in poses)
            {
                tried++;
                var c = Ground(ps.c); var rot = FootRidgeRotAt(c, ps.yaw);
                go.transform.SetPositionAndRotation(c, rot); Physics.SyncTransforms();
                int hits = 0;
                foreach (var pr in probes) { foreach (var col in Physics.OverlapSphere(pr.c, pr.r, ~0, QueryTriggerInteraction.Ignore)) if (mine.Contains(col)) { hits++; break; } if (hits > bestHits) break; }
                if (hits < bestHits) { bestHits = hits; bc = c; by = ps.yaw; }
                if (hits == 0) { found = true; break; }
            }
            UnityEngine.Object.DestroyImmediate(go); Physics.SyncTransforms();
            _footRidge = bc; _footRidgeRot = FootRidgeRotAt(bc, by);
            L($"landmark fit (real LM_BlackRidge colliders): {tried} poses tried, probes canyon {nCanyon} / lava {nLava} / gameplay {probes.Count - nCanyon - nLava}; spot {V(bc)} yaw offset {F(by)} ({F(EBFlat(bc, want))} m from {V(Ground(want))}), touching probes {bestHits}");
            if (!found) W($"LM_BlackRidge still touches {bestHits} probe(s) at the best pose");
            return true;
        }

        /// <summary>
        /// the landmark spot: near the requested spot, the whole footprint (+3 m) on open ground of the upper zone, at least
        /// 5 m from the canyon path, 7 m from the lava, no gameplay object, no more than 5 m of relief under it.
        /// </summary>
        static void FootPlaceRidge(Zone z, Vector3 want, bool fixedSpot)
        {
            var art = Prefab("LM_BlackRidge", false);
            if (art) _footRidgeB = EBPrefabBounds(art);
            float bestS = -1e9f; Vector3 best = Ground(want); float bestYaw = 0f; string info = "";
            var b = _footRidgeB;
            var offs = Enumerable.Range(0, 18).Select(i => i <= 9 ? i * 10f : (i - 18) * 10f).ToArray();   // 0..90, -80..-10
            var why = new Dictionary<string, int>(); void Why(string w) { why.TryGetValue(w, out var v); why[w] = v + 1; }
            const float mg = 1.5f;
            int bestBlk = 0;
            for (float dx = -39f; dx <= 39f; dx += 3f) for (float dz = -39f; dz <= 39f; dz += 3f)
                {
                    if (fixedSpot && (dx != 0f || dz != 0f)) continue;
                    var c = Ground(new Vector3(want.x + dx, 0f, want.z + dz));
                    if (OwnWeight(z, c) < 0.5f || FootMouth(c) < 1f) continue;
                    foreach (var yo in offs)
                    {
                        var rot = FootRidgeRotAt(c, yo); bool ok = true; float lo = 1e9f, hi = -1e9f; int blk = 0;
                        for (float lx = b.min.x - mg; lx <= b.max.x + mg + 0.01f && ok; lx += 3f)
                            for (float lz = b.min.z - mg; lz <= b.max.z + mg + 0.01f && ok; lz += 3f)
                            {
                                var q = c + rot * new Vector3(lx, 0f, lz); q.y = GroundY(q);
                                string w = FootCanyonDist(q) < 4.5f ? "canyon" : FootLavaDist(q) < 6f ? "lava" : FootMouth(q) < 0.95f ? "mouth" : q.y < 12f ? "low / sea cliff" : null;
                                if (w != null) { Why(w); ok = false; break; }
                                if (IsBlocked(q, 0f)) blk++;
                                lo = Mathf.Min(lo, q.y); hi = Mathf.Max(hi, q.y);
                            }
                        if (!ok) continue;
                        if (hi - lo > 8f) { Why("relief"); continue; }
                        float score = -60f * blk - EBFlat(c, want) - 2f * (hi - lo) - 0.15f * Mathf.Abs(yo);
                        if (score > bestS) { bestS = score; best = c; bestYaw = yo; bestBlk = blk; info = $"relief {F(hi - lo)} m, {F(EBFlat(c, FootExit))} m from the canyon exit, yaw offset {F(yo)}, {blk} footprint samples near a gameplay object"; }
                    }
                }
            L("landmark footprint rejections: " + string.Join(", ", why.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + " " + kv.Value)));
            if (bestS <= -1e8f) { W($"no free landmark footprint near {V(want)}: kept the requested spot"); _footRidge = Ground(want); _footRidgeRot = FootRidgeRotAt(_footRidge, 0f); return; }
            _footRidge = best; _footRidgeRot = FootRidgeRotAt(best, bestYaw);
            if (bestBlk > 0)
            {
                var near = new List<string>();
                foreach (var l in Blockers.Values) foreach (var q in l) if (FootInRect(q, best, _footRidgeRot, q.y) && near.Count < 8) near.Add(V(q));
                W($"landmark footprint is near {bestBlk} gameplay sample(s): blockers {string.Join(" ", near)}");
            }
            L($"landmark spot {V(best)} (footprint {F(b.size.x)} x {F(b.size.z)} m, {info})");
        }

        static void FootLoad()
        {
            _footCanyon = new List<Vector3>(); _footLava = new List<Vector3>();
            try
            {
                var f = JsonUtility.FromJson<EBFeat>(File.ReadAllText(FeaturesPath));
                if (f.canyon != null) _footCanyon = Pts(f.canyon.points);
                if (f.lava != null) { _footLava = Pts(f.lava.points); if (f.lava.vent != null && f.lava.vent.Length >= 3) _footVent = P3(f.lava.vent); }
            }
            catch (Exception e) { W("features (canyon / lava) not read: " + e.Message); }
        }
        static float FootPolyDist(List<Vector3> l, Vector3 p)
        {
            if (l == null || l.Count == 0) return 999f; if (l.Count == 1) return EBFlat(l[0], p);
            float best = 1e9f; for (int i = 0; i + 1 < l.Count; i++) best = Mathf.Min(best, SegDist(p, l[i], l[i + 1]));
            return best;
        }
        static float FootCanyonDist(Vector3 p) => FootPolyDist(_footCanyon, p);
        /// <summary>xz metres to the lava channel or the vent pool edge (about 6 m round the vent)</summary>
        static float FootLavaDist(Vector3 p) => Mathf.Min(FootPolyDist(_footLava, p), EBFlat(p, _footVent) - 6f);
        /// <summary>0 at the canyon mouth (z &gt; -100, BONE), 1 from z -112</summary>
        static float FootMouth(Vector3 p) => Smooth01((-p.z - 100f) / 12f);
        /// <summary>band value: 0 forest, ~0.2-0.48 dry, ~0.48-0.7 black rock, ~0.7-0.86 ash, above: the volcanic ground</summary>
        static float FootS(Vector3 p)
        {
            float s = (-p.z - 108f) / 95f + Mathf.Max(0f, (p.y - 40f) / 16f) * 0.25f + (Noise(p.x, p.z, 21f, 71) - 0.5f) * 0.12f;
            return Mathf.Clamp01(s);
        }

        /// <summary>DET_EB_DryGrass: the grass detail mesh with a recoloured (straw / yellow-brown) copy of its texture</summary>
        static GameObject FootDryGrassPrefab()
        {
            string prefabPath = PrefabZones + "/DET_EB_DryGrass.prefab", matPath = ZoneMatDir + "/M_EB_DryGrass.mat", texPath = ZoneMatDir + "/T_EB_DryGrass.png";
            var src = Prefab("DET_ENV_Grass_01"); if (!src) return null;
            var srcR = src.GetComponentInChildren<MeshRenderer>(true); var srcF = src.GetComponentInChildren<MeshFilter>(true);
            if (!srcR || !srcF || !srcF.sharedMesh || !srcR.sharedMaterial) { W("DET_ENV_Grass_01 has no mesh / material: no dry grass"); return null; }
            EnsureFolder(ZoneMatDir); EnsureFolder(PrefabZones);
            var srcMat = srcR.sharedMaterial;
            string texProp = srcMat.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
            var srcTex = srcMat.GetTexture(texProp) as Texture2D;
            Texture2D dryTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (!dryTex && srcTex)
            {
                int w = Mathf.Min(srcTex.width, 1024), h = Mathf.Min(srcTex.height, 1024);
                var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var prev = RenderTexture.active;
                Graphics.Blit(srcTex, rt); RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
                RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                var px = tex.GetPixels();
                var straw = new Color(0.78f, 0.64f, 0.38f); var brown = new Color(0.52f, 0.4f, 0.24f);
                for (int i = 0; i < px.Length; i++)
                {
                    var c = px[i]; float lum = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
                    float k = Mathf.Clamp01(lum * 2.2f);
                    var o = Color.Lerp(brown, straw, k) * (0.75f + lum * 0.9f); o.a = c.a; px[i] = o;
                }
                tex.SetPixels(px); tex.Apply();
                File.WriteAllBytes(texPath, tex.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(texPath);
                var ti = AssetImporter.GetAtPath(texPath) as TextureImporter;
                if (ti) { ti.alphaIsTransparency = true; ti.sRGBTexture = true; ti.mipmapEnabled = true; ti.maxTextureSize = 1024; ti.SaveAndReimport(); }
                dryTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                L($"dry grass texture: {texPath} ({w} x {h}, recoloured from {srcTex.name})");
            }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!mat) { mat = new Material(srcMat) { name = "M_EB_DryGrass" }; AssetDatabase.CreateAsset(mat, matPath); }
            else { mat.shader = srcMat.shader; mat.CopyPropertiesFromMaterial(srcMat); }
            if (dryTex) { mat.SetTexture(texProp, dryTex); if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", dryTex); }
            else { var tint = new Color(1.25f, 1.0f, 0.5f, 1f); if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint); if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint); W("no readable grass texture: dry grass is only tinted"); }
            mat.enableInstancing = true; EditorUtility.SetDirty(mat);
            var go = new GameObject("DET_EB_DryGrass");
            go.AddComponent<MeshFilter>().sharedMesh = srcF.sharedMesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off;
            var pf = PrefabUtility.SaveAsPrefabAsset(go, prefabPath); UnityEngine.Object.DestroyImmediate(go);
            _pfCache.Remove("DET_EB_DryGrass");
            return pf;
        }

        [PrimalBridgeCommand]
        public static string Foothills(string arg)
        {
            var a = Args(arg); bool dry = a.ContainsKey("dry");
            Begin("EB", "Foothills " + arg);
            if (!OpenIsland(out var scene, out bool wasDirty)) return End();
            if (!FindTerrain() || LoadFeat() == null) { W("no terrain or features"); return End(); }
            var z = ZoneById("foothills"); if (z == null) { W("zone foothills missing in the core"); return End(); }
            FootLoad();
            int blockers = BuildBlockers(scene);
            var wantRidge = EBXZ(a, "ridge", FootRidgeDefault);
            if (!FootFitRidge(z, wantRidge, scene)) FootPlaceRidge(z, wantRidge, a.ContainsKey("ridge"));
            var others = EBTreeGrid("EB_foothills");
            L($"zone {z.id}: {V(z.centre)} -> {V(z.end)} r {F(z.radius)} blend {F(z.blend)}; canyon {_footCanyon.Count} pts, lava {_footLava.Count} pts, vent {V(_footVent)}; ridge {V(_footRidge)} s {F(FootS(_footRidge))}; blockers {blockers}");
            if (dry) { FootSurvey(z, others); return End(); }

            var bk = BackupTerrain("EB"); if (!bk) return End();
            var area = z.Bounds;

            // ---- splat
            int iSand = EBLayer("TL_Sand"), iGrass = EBLayer("TL_Grass"), iFF = EBLayer("TL_ForestFloor"), iRock = EBLayer("TL_Rock"),
                iDark = EBLayer("TL_DarkSoil"), iAsh = EBLayer("TL_Ash");
            int nSplat = EditSplat(z, bk, (w, ow, b, t) =>
            {
                float m = FootMouth(w); if (m <= 0f || w.y < 2.5f) return;
                if (FootLavaDist(w) < 3f) return;                                           // the Phase 1 lava ground stays
                float s = FootS(w), dryK = EBRamp(s, 0.14f, 0.3f), blk = EBRamp(s, 0.44f, 0.56f), ash = EBRamp(s, 0.66f, 0.8f), vol = EBRamp(s, 0.86f, 0.95f);
                float n = Noise(w.x, w.z, 11f, 73);
                var T = (float[])t.Clone();
                EBMix(T, 0.75f * dryK * (1f - blk), iGrass, 0.34f, iDark, 0.28f, iSand, 0.16f + 0.08f * n, iRock, 0.14f, iFF, 0.08f);
                EBMix(T, 0.85f * blk * (1f - ash), iRock, 0.42f + 0.1f * n, iDark, 0.46f - 0.1f * n, iAsh, 0.12f);
                EBMix(T, 0.9f * ash, iAsh, 0.58f + 0.12f * n, iDark, 0.3f - 0.12f * n, iRock, 0.12f);
                if (vol > 0f) for (int l = 0; l < T.Length; l++) T[l] = Mathf.Lerp(T[l], b[l], vol * 0.5f);
                float sl = Slope(w);
                if (sl > 36f) EBMix(T, 0.6f * EBRamp(sl, 36f, 48f), iRock, 0.75f, iDark, 0.25f);   // walls stay rock (darker higher up)
                float cd = FootCanyonDist(w);
                if (cd < 3f) for (int l = 0; l < T.Length; l++) T[l] = Mathf.Lerp(T[l], b[l], 0.5f * (1f - cd / 3f));   // the canyon floor path keeps its look
                if (iAsh >= 0) T[iAsh] = Mathf.Max(T[iAsh], b[iAsh]);                        // Phase 1 ash never reduced
                for (int l = 0; l < T.Length; l++) t[l] = Mathf.Lerp(b[l], T[l], m);
            });
            L($"splat: {nSplat} texels (dry soil -> basalt rock + dark soil -> ash; Phase 1 ash kept, vent / lava ground untouched)");

            // ---- details (+ the dry grass prototype; the other layers are verified unchanged by the prototype edit)
            var before = EBDetailTotals(); int res = TD.detailResolution;
            var saved = new int[before.Length][,]; for (int l = 0; l < before.Length; l++) saved[l] = TD.GetDetailLayer(0, 0, res, res, l);
            var dryPf = FootDryGrassPrefab();
            int dDry = dryPf ? AddDetailProto(dryPf, 0.8f, 1.25f, 0.7f, 1.2f, 0.4f, 0.3f, Color.white, Color.white) : -1;
            var afterProto = EBDetailTotals(); int restored = 0;
            for (int l = 0; l < before.Length && l < afterProto.Length; l++) if (afterProto[l] != before[l]) { TD.SetDetailLayer(0, 0, l, saved[l]); restored++; }
            if (restored > 0) W($"adding the dry grass prototype changed {restored} detail layers: restored from the snapshot");
            L($"dry grass detail: {(dDry >= 0 ? "layer " + dDry : "not available")}");
            int dFern = DetailLayer("DET_ENV_Fern_01"), dGrass = DetailLayer("DET_ENV_Grass_01"), dBush = DetailLayer("DET_ENV_Bush_01"), dH = DetailLayer("DET_PC_Horsetail"),
                dR = DetailLayer("DET_PC_Reeds"), dGF = DetailLayer("DET_PC_GiantFern");
            var layers = new[] { dFern, dGrass, dBush, dH, dR, dGF, dDry }.Where(i => i >= 0).Distinct().ToArray();
            int kFe = Array.IndexOf(layers, dFern), kGr = Array.IndexOf(layers, dGrass), kDry = dDry >= 0 ? Array.IndexOf(layers, dDry) : -1;
            int nDet = EditDetails(z, bk, layers, (w, ow, b, t) =>
            {
                float m = FootMouth(w); if (m <= 0f || w.y < 3f) return;
                float s = FootS(w), dryK = EBRamp(s, 0.14f, 0.3f), blk = EBRamp(s, 0.44f, 0.56f), ash = EBRamp(s, 0.66f, 0.8f);
                float green = (1f - 0.8f * dryK) * (1f - 0.9f * blk) * (1f - ash);
                float n = Noise(w.x, w.z, 7f, 75), n2 = Noise(w.x, w.z, 19f, 76);
                var v = new float[t.Length];
                for (int i = 0; i < v.Length; i++) v[i] = i == kDry ? b[i] : b[i] * green;
                if (kDry >= 0)
                {
                    float grassBase = (kGr >= 0 ? b[kGr] : 0f) + (kFe >= 0 ? b[kFe] * 0.3f : 0f);
                    float drySparse = dryK * (1f - blk) * (grassBase * 0.7f + 0.5f + 0.9f * n) + blk * (1f - ash) * Smooth01((n2 - 0.45f) / 0.3f) * 0.8f;
                    v[kDry] = Mathf.Max(b[kDry], drySparse * (1f - 0.5f * EBRamp(Slope(w), 25f, 38f)));
                }
                float cd = FootCanyonDist(w); if (cd < 3.5f) for (int i = 0; i < v.Length; i++) v[i] *= 0.4f;
                if (FootLavaDist(w) < 4f) for (int i = 0; i < v.Length; i++) v[i] = 0f;
                for (int i = 0; i < v.Length; i++) t[i] = EBRound(Mathf.Lerp(b[i], v[i], m), w, 80 + i);
            }, out long dBefore, out long dAfter);
            L($"details: {nDet} cells, layers [{string.Join(", ", layers.Select(l => TD.detailPrototypes[l].prototype ? TD.detailPrototypes[l].prototype.name : "?"))}] {dBefore} -> {dAfter} instances in the zone rect");

            // ---- own terrain trees (key EB_foothills)
            int[] pFern = new[] { "ENV_PC_TreeFern_A", "ENV_PC_TreeFern_B", "ENV_PC_TreeFern_C" }.Select(n => TreeProto(n)).Where(i => i >= 0).ToArray();
            int[] pCyc = new[] { "ENV_PC_Cycad_A", "ENV_PC_Cycad_B", "ENV_PC_Cycad_C" }.Select(n => TreeProto(n)).Where(i => i >= 0).ToArray();
            var mine = new List<TreeInstance>(); var myG = new Dictionary<long, List<Vector3>>(); int nF = 0, nC = 0; const float st = 4f;
            for (float gz = area.yMin; gz < area.yMax; gz += st)
                for (float gx = area.xMin; gx < area.xMax; gx += st)
                {
                    int ix = Mathf.RoundToInt(gx * 10f), iz = Mathf.RoundToInt(gz * 10f);
                    var p = new Vector3(gx + Rand01(ix + 3, iz * 5 + 1) * st, 0f, gz + Rand01(ix * 11 + 3, iz + 7) * st);
                    float ow = OwnWeight(z, p) * FootMouth(p); if (ow <= 0.05f) continue;
                    p.y = GroundY(p); if (p.y < 3f || Slope(p) > 28f || WaterDist(p) < 3f) continue;
                    float s = FootS(p), dryK = EBRamp(s, 0.14f, 0.3f), blk = EBRamp(s, 0.44f, 0.6f), r = Rand01(ix + 57, iz + 91);
                    float pC = pCyc.Length > 0 ? ow * dryK * (1f - blk) * 0.07f : 0f, pF = pFern.Length > 0 ? ow * (1f - dryK) * 0.025f : 0f;
                    int kind = r < pC ? 1 : r < pC + pF ? 0 : -1; if (kind < 0) continue;
                    if (FootCanyonDist(p) < 4f || FootLavaDist(p) < 8f || FootInRidge(p, 4f) || IsBlocked(p, 1.5f) || Near(others, p, 3.5f, 4f) || Near(myG, p, 3.5f, 4f)) continue;
                    float yaw = Rand01(ix + 1, iz + 13) * Mathf.PI * 2f, sc;
                    int proto;
                    if (kind == 1) { proto = pCyc[(int)(r * 173f) % pCyc.Length]; sc = 0.75f + Rand01(ix, iz + 5) * 0.45f; nC++; }
                    else { proto = pFern[(int)(r * 211f) % pFern.Length]; sc = 0.8f + Rand01(ix + 4, iz) * 0.35f; nF++; }
                    mine.Add(MakeTree(proto, p, yaw, sc, sc * (0.92f + Rand01(iz, ix + 3) * 0.16f))); Add(myG, p, 4f);
                }
            SetOwnTrees("EB_foothills", mine);
            L($"own terrain trees: {nC} cycads (dry band), {nF} tree ferns (lower transition)");

            // ---- placed: dead trees, dry trunks, basalt
            var root = ZoneRoot(z); ClearGroup(root);
            var deadG = Group2(root, "DeadTrees"); var rockG = Group2(root, "BlackRock");
            var placed = new Dictionary<long, List<Vector3>>();
            var allTrees = new Dictionary<long, List<Vector3>>(); foreach (var kv in others) foreach (var q in kv.Value) Add(allTrees, q, 4f); foreach (var kv in myG) foreach (var q in kv.Value) Add(allTrees, q, 4f);
            bool Base(Vector3 p, float r, float pathExtra)
            {
                if (OwnWeight(z, p) <= 0.1f || FootMouth(p) < 0.9f) return false;
                if (GroundY(p) < 3f || WaterDist(p) < 2f) return false;
                if (FootCanyonDist(p) < 3.5f + r * 0.6f + pathExtra || FootLavaDist(p) < 5f + r) return false;
                if (FootInRidge(p, 3f + r * 0.5f) || IsBlocked(p, r * 0.5f + 1f)) return false;
                return true;
            }
            float DryK(Vector3 p) { var g = Ground(p); float s = FootS(g); return EBRamp(s, 0.16f, 0.3f) * (1f - EBRamp(s, 0.62f, 0.74f)); }
            float BlkK(Vector3 p) { var g = Ground(p); float s = FootS(g); return EBRamp(s, 0.44f, 0.56f) * (1f - 0.6f * EBRamp(s, 0.86f, 0.95f)) * 0.9f + EBRamp(s, 0.66f, 0.8f) * 0.25f; }
            var rejD = new Dictionary<string, int>(); var rejF = new Dictionary<string, int>();
            var deadT = EBScatter(deadG, new[] { "PROP_PC_BrokenTree_A", "PROP_PC_BrokenTree_B" }, 24, 9f,
                p => OwnWeight(z, p) * DryK(p) * 0.85f, (p, r) => Base(p, r, 0.5f) && Slope(p) < 26f && !Near(allTrees, p, 2f, 4f), 0.85f, 1.25f, 0.1f, false, 5101, area, placed, colliderProp: true, ignoreRoot: root, rej: rejD);
            var dryTr = EBScatter(deadG, new[] { "ENV_PC_FallenTrunk_A", "ENV_PC_FallenTrunk_B", "PFB_ENV_FallenLog_01" }, 6, 14f,
                p => OwnWeight(z, p) * Mathf.Max(DryK(p), 0.5f * BlkK(p)) * 0.9f, (p, r) => Base(p, r, 1f) && Slope(p) < 18f && !Near(allTrees, p, 1.2f, 4f), 0.75f, 1.05f, 0.12f, true, 5102, area, new Dictionary<long, List<Vector3>>(), isLong: true, colliderProp: true, ignoreRoot: root, rej: rejF);
            L($"dead tree rejections: {EBRej(rejD)}; dry trunk rejections: {EBRej(rejF)}");
            var basalt = EBScatter(rockG, new[] { "ENV_PC_Basalt_A", "ENV_PC_Basalt_B", "ENV_PC_BasaltBoulder" }, 90, 5.5f,
                p => { float cl = Smooth01((Noise(p.x, p.z, 18f, 81) - 0.35f) / 0.3f); return OwnWeight(z, p) * BlkK(p) * (0.25f + 0.75f * cl); },
                (p, r) => Base(p, r, 0.5f) && Slope(p) < 40f, 0.8f, 2.0f, 0.25f, true, 5103, area, placed, colliderProp: true, ignoreRoot: root);
            L($"placed: {deadT.Count} dead / broken trees, {dryTr.Count} dry fallen trunks, {basalt.Count} basalt groups; models {EBModelCounts(root)}");

            // ---- landmark + FX anchors
            var lm = Marker(root, "LM_VolcanicFoothills", _footRidge, _footRidgeRot);
            var art = Prefab("LM_BlackRidge", false);
            if (art) { var go = (GameObject)PrefabUtility.InstantiatePrefab(art, lm); go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; L($"landmark: ART LM_BlackRidge placed at {V(_footRidge)}"); }
            else L($"landmark: placeholder LM_VolcanicFoothills at {V(_footRidge)}, facing the canyon exit (LM_BlackRidge not delivered yet)");
            // the canyon path stays free of own colliders (same probes as the check, a little larger)
            var probes = new List<(Vector3 c, float r)>();
            for (int i = 0; i + 1 < _footCanyon.Count; i++)
                for (float f = 0f; f < 1f; f += 0.25f) { var q = Vector3.Lerp(_footCanyon[i], _footCanyon[i + 1], f); if (q.z <= -100f) probes.Add((Ground(q) + Vector3.up * 1.2f, 2.6f)); }
            var lmHits = new List<string>();
            int cleared = EBClearCorridor(root, lm, probes, lmHits);
            L($"canyon path: {cleared} own props with a collider within 2.6 m removed{(lmHits.Count > 0 ? "; landmark touches the path at " + string.Join(" ", lmHits) : "")}");
            if (lmHits.Count > 0) W("the landmark collider touches the canyon path");
            L($"final own props: {EBModelCounts(root)}");
            var fx = Group2(root, "FX_Anchors"); var taken = new List<Vector3> { _footRidge };
            int nFx = 0;
            void Fx(string name, Func<Vector3, float> score, int seed, float minSep, float up)
            {
                Func<Vector3, float> sc = p => { if (OwnWeight(z, p) < 0.4f || FootMouth(p) < 0.95f) return 0f; var g = Ground(p); if (g.y < 4f || IsBlocked(g, 1f)) return 0f; return score(g); };
                if (EBFindSpot(sc, area, seed, 1500, taken, minSep, out var spot) || EBFindSpot(sc, area, seed + 1, 3000, taken, minSep * 0.6f, out spot))
                { Marker(fx, name, spot + Vector3.up * up, Quaternion.identity); nFx++; L($"  FX anchor {name} {V(spot + Vector3.up * up)} s {F(FootS(spot))}"); }
                else W($"no spot for FX anchor {name}");
            }
            float Band(Vector3 g, float c, float half) => Mathf.Max(0f, 1f - Mathf.Abs(FootS(g) - c) / half);
            for (int i = 1; i <= 4; i++) Fx($"FX_AshFall_{i:00}", g => Slope(g) < 25f && FootLavaDist(g) > 10f ? Band(g, 0.82f, 0.12f) : 0f, 5200 + i * 7, 25f, 8f);
            for (int i = 1; i <= 3; i++) Fx($"FX_Fumarole_{i:00}", g => Slope(g) < 18f && FootCanyonDist(g) > 8f && FootLavaDist(g) > 12f ? Band(g, 0.62f, 0.12f) : 0f, 5300 + i * 7, 20f, 0.1f);
            for (int i = 1; i <= 2; i++) Fx($"FX_HeatShimmer_{i:00}", g => { float ld = FootLavaDist(g); return Slope(g) < 15f && ld > 6f && ld < 20f && FootS(g) > 0.85f ? 1f - ld / 25f : 0f; }, 5400 + i * 7, 20f, 0.5f);
            for (int i = 1; i <= 2; i++) Fx($"FX_DustDevil_{i:00}", g => Slope(g) < 12f && FootCanyonDist(g) > 6f && !Near(allTrees, g, 5f, 4f) ? Band(g, 0.36f, 0.16f) : 0f, 5500 + i * 7, 22f, 0.2f);
            Marker(fx, "FX_SmokeDrift_01", _footRidge + Vector3.up * 10f, Quaternion.identity); nFx++; L($"  FX anchor FX_SmokeDrift_01 {V(_footRidge + Vector3.up * 10f)} (over the landmark)");
            L($"FX anchors: {nFx} (WORLD attaches smoke / ash / heat / dust; hazard zones HZ_volcano / HZ_vent unchanged)");

            Ter.Flush(); EditorUtility.SetDirty(TD); AssetDatabase.SaveAssets();
            SaveIsland(scene, wasDirty);
            return End();
        }

        static void FootSurvey(Zone z, Dictionary<long, List<Vector3>> trees)
        {
            // band profile along the capsule axis and the canyon
            for (int i = 0; i < _footCanyon.Count; i += 8)
            {
                var p = Ground(_footCanyon[i]);
                L($"  canyon {i}: {V(p)} s {F(FootS(p))} mouth {F(FootMouth(p))} own {F(OwnWeight(z, p))} slope {F(Slope(p))}");
            }
            int inZone = 0; foreach (var kv in trees) foreach (var p in kv.Value) if (OwnWeight(z, p) * FootMouth(p) > 0f) inZone++;
            L($"  other terrain trees in the zone (z < -100): {inZone}");
            var cands = new List<(Vector3 p, float score)>();
            var b = z.Bounds;
            for (float x = b.xMin; x < b.xMax; x += 6f) for (float zz = b.yMin; zz < b.yMax; zz += 6f)
                {
                    var p = Ground(new Vector3(x, 0f, zz)); if (OwnWeight(z, p) < 0.8f || FootMouth(p) < 1f) continue;
                    float s = FootS(p); if (s < 0.5f || s > 0.95f) continue;
                    if (FootCanyonDist(p) < 12f || FootLavaDist(p) < 15f || Slope(p) > 20f || IsBlocked(p, 8f)) continue;
                    cands.Add((p, p.y + 20f * Mathf.Max(0f, 0.2f - Mathf.Abs(s - 0.7f))));
                }
            foreach (var c in cands.OrderByDescending(c => c.score).Take(10)) L($"  ridge candidate {V(c.p)} s {F(FootS(c.p))} canyon {F(FootCanyonDist(c.p))} lava {F(FootLavaDist(c.p))} slope {F(Slope(c.p))}");
            var vol = PrimalFrontier.Core.SceneRoots.Find(PrimalFrontier.Core.SceneRoots.Volcano, false);
            if (vol) foreach (Transform c in vol) L($"  Volcano/{c.name}: {c.GetComponentsInChildren<Transform>(true).Length - 1} objects");
        }
    }
}
