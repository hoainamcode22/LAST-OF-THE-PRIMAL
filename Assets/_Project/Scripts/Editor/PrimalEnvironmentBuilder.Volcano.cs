using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using PrimalFrontier.Core;
using PrimalFrontier.World;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// Volcanic ridge visuals (directive 49), inside the island's volcano zone at the top of the canyon: the small lava
    /// channel carved by the terrain pass (PF/Lava ribbon, hot at the spatter vent, crusted at the cooled front), a lava
    /// pool in the vent pit, glowing cracks across the ash field (M_Env_LavaCrack), a thin vent smoke, two warm unshadowed
    /// point lights, a small heat shimmer over the vent (M_VFX_HeatShimmer when it exists) and an Examinable
    /// (env_lava_channel). The ash field and basalt come from the terrain layers and the Vegetation step (Volcano/Basalt).
    /// Phase 1: embers over the vent and the channel. Wave 2a (owner decision): the offshore PFB_ENV_Volcano is THE volcano
    /// (it erupts, PrimalVolcanoBuilder.Eruption) and stays on; the VolcanoLandmark copy on Lava/VentSystem is kept but
    /// switched off (SetActive false, eruptions off), so there is one volcano behaviour.
    /// Rebuilt each run under World/Environment/Volcano/Lava (Basalt is left alone).
    /// </summary>
    public static partial class PrimalEnvironmentBuilder
    {
        [PrimalBridgeCommand]
        public static string Volcano(string arg)
        {
            Begin("Volcano " + arg);
            if (!IslandOpen(out var scene, out bool wasDirty)) return End("Volcano");
            if (!FindTerrain() || LoadFeatures() == null || _features.lava?.points == null) { W("no terrain / features / lava line"); return End("Volcano"); }
            var M = EnvMaterials();
            var root = EnvGroup(scene, "Volcano"); var lava = Child(root, "Lava"); ClearChildren(lava);
            // ash ground: TL_Ash only covers the volcano zone; tint it dark grey (terrain layer remap, texture untouched)
            var ashLayer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(TerrainSrc + "/TL_Ash.terrainlayer");
            if (ashLayer) { ashLayer.diffuseRemapMin = Vector4.zero; ashLayer.diffuseRemapMax = new Vector4(0.78f, 0.76f, 0.75f, 1f); EditorUtility.SetDirty(ashLayer); L("TL_Ash tinted a little darker (diffuse remap max 0.78 / 0.76 / 0.75)"); }
            else W("TL_Ash.terrainlayer missing");
            var line = Pts(_features.lava.points); var vent = V3(_features.lava.vent);

            // ---- channel: surface a little above the carved floor, flat across, edges under the banks
            var surf = new List<Vector3>();
            foreach (var p in line) surf.Add(new Vector3(p.x, GroundY(p) + 0.22f, p.z));
            for (int pass = 0; pass < 2; pass++) for (int i = 1; i + 1 < surf.Count; i++) { var q = surf[i]; q.y = (surf[i - 1].y + q.y * 2f + surf[i + 1].y) * 0.25f; surf[i] = q; }
            var ch = LavaRibbon("ME_Env_LavaChannel", surf, 0.8f, 1.25f);
            var chGo = MeshGo(lava, "LavaChannel", ch, M["M_Env_Lava"], false);
            // ---- vent pool
            var ventG = Ground(vent);
            var poolGo = MeshGo(lava, "VentPool", LavaDisc("ME_Env_LavaVentPool", ventG + Vector3.up * 0.3f, 1.9f), M["M_Env_Lava"], false);
            // ---- cracks across the ash field
            var veg2 = ReadMask(TerrainSrc + "/ENV_Island_VegMask2_v2.png");
            // ---- ash ground: TL_Ash painted into the splat inside the volcanic mask (max, then the other layers renormalised;
            //      idempotent, and TerrainPass resets the splat, so run Volcano after it)
            var tdv = _terrain.terrainData; int ashIdx = System.Array.FindIndex(tdv.terrainLayers, tl => tl && tl.name == "TL_Ash");
            if (ashIdx >= 0 && veg2 != null)
            {
                int ar = tdv.alphamapResolution; var al = tdv.GetAlphamaps(0, 0, ar, ar); int nl = al.GetLength(2), painted = 0;
                var tpv = _terrain.transform.position; var szv = tdv.size;
                for (int z = 0; z < ar; z++)
                    for (int x = 0; x < ar; x++)
                    {
                        var w = new Vector3(tpv.x + x / (float)(ar - 1) * szv.x, 0f, tpv.z + z / (float)(ar - 1) * szv.z);
                        float mk = SampleMap(veg2[2], w); if (mk < 0.25f) continue;
                        float want = Mathf.Clamp01((mk - 0.25f) / 0.4f) * 0.85f, cur = al[z, x, ashIdx]; if (cur >= want - 0.01f) continue;
                        float rest = 1f - cur, sc = rest > 1e-4f ? (1f - want) / rest : 0f;
                        for (int l = 0; l < nl; l++) al[z, x, l] = l == ashIdx ? want : al[z, x, l] * sc;
                        painted++;
                    }
                tdv.SetAlphamaps(0, 0, al); EditorUtility.SetDirty(tdv);
                L($"ash ground: TL_Ash (layer {ashIdx}) painted on {painted} splat texels inside the volcanic mask (up to 85 %)");
            }
            else W("ash ground: no TL_Ash layer or no volcanic mask");
            int nc = 0;
            var cracks = Child(lava, "Cracks");
            for (int k = 0; k < 60 && nc < 11; k++)
            {
                float a = Rand01(k, 71) * 360f, r = 6f + Rand01(k, 72) * 26f;
                var s0 = vent + Quaternion.Euler(0, a, 0) * Vector3.forward * r;
                if (veg2 != null && SampleMap(veg2[2], s0) < 0.5f) continue;
                if (Slope(s0) > 24f) continue;
                if (DistToLine(line, s0) < 3.5f) continue;
                var pts = new List<Vector3>(); var dir = Quaternion.Euler(0, Rand01(k, 73) * 360f, 0) * Vector3.forward; var p = s0;
                int len = 7 + (int)(Rand01(k, 74) * 9f);
                for (int i = 0; i < len; i++) { pts.Add(p); dir = Quaternion.Euler(0, (Rand01(k, 80 + i) - 0.5f) * 50f, 0) * dir; p += dir * 0.9f; }
                var m = CrackMesh($"ME_Env_LavaCrack_{nc:00}", pts, 0.1f + Rand01(k, 75) * 0.16f);
                MeshGo(cracks, $"Crack_{nc:00}", m, M["M_Env_LavaCrack"], false);
                nc++;
            }
            // ---- smoke, embers, light, shimmer
            var smoke = VentSmoke(lava, ventG + Vector3.up * 0.8f);
            var embers = VentEmbers(lava, "VentEmbers", ventG + Vector3.up * 0.5f, 1.4f, 4f);
            if (surf.Count > 6) VentEmbers(lava, "ChannelEmbers", surf[surf.Count / 3] + Vector3.up * 0.2f, 1.0f, 1.2f);
            WarmLight(lava, "VentGlow", ventG + Vector3.up * 1.6f, 9f, 2.4f);
            if (surf.Count > 6) WarmLight(lava, "ChannelGlow", surf[surf.Count / 2] + Vector3.up * 1.2f, 7f, 1.3f);
            var haze = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/VFX/Materials/M_VFX_HeatShimmer.mat");
            if (haze)
            {
                VentShimmer(lava, ventG + Vector3.up * 3.5f, haze);
                if (surf.Count > 6) VentShimmer(lava, surf[surf.Count / 2] + Vector3.up * 2.2f, haze, "ChannelShimmer", 0.6f);
            }
            else L("no M_VFX_HeatShimmer yet (PrimalVolcanoBuilder.Atmosphere creates it): no shimmer over the vent");
            // ---- the island vent's behaviour (VolcanoLandmark, tuned small)
            var ventGo = new GameObject("VentSystem"); ventGo.transform.SetParent(lava, false); ventGo.transform.position = ventG;
            var vv = ventGo.AddComponent<VolcanoLandmark>();
            vv.smoke = smoke; vv.embers = embers; vv.glow = null; vv.lava = null; vv.flowGlows = new ParticleSystem[0];
            vv.embersDay = 1.5f; vv.embersNight = 6f; vv.smokeWind = 1.2f; vv.rumblePuffSize = 5f; vv.rumbleVolume = 0.35f;
            vv.ashFall = true; vv.ashMaterial = Or(VfxMat("M_VFX_Chip"), VfxMat("M_VFX_Soft"));
            vv.ashFullDistance = 30f; vv.ashStartDistance = 130f; vv.ashRate = 22f; vv.ashMaxParticles = 160;
            vv.heatHaze = false; vv.hazeMaterial = haze; vv.hazeSize = new Vector2(6f, 9f);
            vv.eruptions = false; ventGo.SetActive(false);                 // wave 2a: the offshore volcano is the volcano
            // ---- examine spot on the bank, 40 % along
            if (surf.Count > 4)
            {
                int i = (int)(surf.Count * 0.4f); var t = surf[i + 1] - surf[i - 1]; t.y = 0; t.Normalize(); var side = new Vector3(-t.z, 0, t.x);
                var go = new GameObject("LavaExamine"); go.transform.SetParent(lava, false); go.transform.position = Ground(surf[i] + side * 2.6f) + Vector3.up * 0.4f;
                var e = go.AddComponent<Examinable>(); e.discoveryId = "env_lava_channel"; e.SaveId = "env_lava_channel"; e.verb = "Look at"; e.range = 4f; e.eventType = GameEventType.Discovery; e.once = true;
            }
            L($"lava channel {surf.Count} pts ({ch.vertexCount} verts), vent pool, {nc} cracks, vent smoke, embers (vent + channel), 2 point lights (no shadows), shimmer {(haze ? "vent + channel" : "off")}, Examinable env_lava_channel");
            L($"VentSystem VolcanoLandmark (switched off) at {V(ventG)}: smoke drift x{F(vv.smokeWind)}, embers {F(vv.embersDay)}/{F(vv.embersNight)} per s (day / night), rumble puff {F(vv.rumblePuffSize)} m, ash {F(vv.ashFullDistance)}..{F(vv.ashStartDistance)} m ({(vv.ashMaterial ? vv.ashMaterial.name : "smoke material")})");
            // ---- one volcano: the offshore landmark stays on (wave 2a)
            bool wantOffshore = true;
            foreach (var other in Object.FindObjectsByType<VolcanoLandmark>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (other == vv || other.transform.IsChildOf(root)) continue;
                var go = other.gameObject;
                if (go.activeSelf != wantOffshore) { go.SetActive(wantOffshore); EditorUtility.SetDirty(go); }
                L($"offshore volcano {PathOf(go.transform)} at {V(go.transform.position)}: {(wantOffshore ? "ON" : "switched off (SetActive false, kept)")}");
            }
            AssetDatabase.SaveAssets();
            SaveScene(scene, wasDirty);
            return End("Volcano");
        }

        static float DistToLine(List<Vector3> l, Vector3 p)
        {
            float best = 1e9f; var q = new Vector2(p.x, p.z);
            foreach (var a in l) best = Mathf.Min(best, (new Vector2(a.x, a.z) - q).sqrMagnitude);
            return Mathf.Sqrt(best);
        }

        static GameObject MeshGo(Transform parent, string name, Mesh m, Material mat, bool shadows)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic);
            return go;
        }

        /// <summary>flat-across ribbon, uv in metres (x across, y along), vertex R = heat (1 at the start, 0 at the front)</summary>
        static Mesh LavaRibbon(string name, List<Vector3> pts, float hw0, float hw1)
        {
            float total = 0f; for (int i = 1; i < pts.Count; i++) total += Vector3.Distance(pts[i], pts[i - 1]);
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var col = new List<Color>(); var tr = new List<int>();
            const int across = 5; float along = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                if (i > 0) along += Vector3.Distance(pts[i], pts[i - 1]);
                float f = total > 0 ? along / total : 0f;
                var t = pts[Mathf.Min(i + 1, pts.Count - 1)] - pts[Mathf.Max(i - 1, 0)]; t.y = 0; t.Normalize(); var side = new Vector3(-t.z, 0, t.x);
                float hw = Mathf.Lerp(hw0, hw1, f); float heat = 1f - Mathf.SmoothStep(0.72f, 1f, f) - f * 0.25f;
                for (int k = 0; k < across; k++)
                {
                    float u = k / (float)(across - 1);
                    v.Add(pts[i] + side * Mathf.Lerp(-hw, hw, u)); uv.Add(new Vector2((u - 0.5f) * hw * 2f, along));
                    col.Add(new Color(Mathf.Clamp01(heat * (1f - Mathf.Abs(u - 0.5f) * 0.5f)), 0, 0, 1));
                }
                // Phase 1 fix: clockwise seen from above (Unity front face); the old order faced down and was culled
                if (i > 0) { int b0 = (i - 1) * across, b1 = i * across; for (int k = 0; k < across - 1; k++) tr.AddRange(new[] { b0 + k, b0 + k + 1, b1 + k, b0 + k + 1, b1 + k + 1, b1 + k }); }
            }
            FaceUp(v, tr);
            var m = new Mesh(); m.SetVertices(v); m.SetUVs(0, uv); m.SetColors(col); m.SetTriangles(tr, 0); m.RecalculateNormals();
            return SaveMesh(name, m);
        }

        static Mesh LavaDisc(string name, Vector3 c, float r)
        {
            c.y = Mathf.Max(c.y, GroundY(c) + 0.06f);                       // Phase 1: the pool drapes on the ground (it sat under the vent mound)
            var v = new List<Vector3> { c }; var uv = new List<Vector2> { Vector2.zero }; var col = new List<Color> { Color.red }; var tr = new List<int>();
            const int seg = 20;
            for (int s = 0; s < seg; s++)
            {
                float a = s / (float)seg * Mathf.PI * 2f; var o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r;
                var q = c + o; q.y = Mathf.Max(c.y - 0.25f, GroundY(q) + 0.06f);
                v.Add(q); uv.Add(new Vector2(o.x, o.z)); col.Add(new Color(0.75f, 0, 0, 1));
                tr.AddRange(new[] { 0, 1 + (s + 1) % seg, 1 + s });
            }
            FaceUp(v, tr);
            var m = new Mesh(); m.SetVertices(v); m.SetUVs(0, uv); m.SetColors(col); m.SetTriangles(tr, 0); m.RecalculateNormals();
            return SaveMesh(name, m);
        }

        /// <summary>thin strip that follows the ground per vertex; tapers and cools at both ends</summary>
        static Mesh CrackMesh(string name, List<Vector3> pts, float hw)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var col = new List<Color>(); var tr = new List<int>();
            float along = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                if (i > 0) along += Vector3.Distance(pts[i], pts[i - 1]);
                float f = i / (float)(pts.Count - 1); float taper = Mathf.Sin(f * Mathf.PI) * 0.8f + 0.2f;
                var t = pts[Mathf.Min(i + 1, pts.Count - 1)] - pts[Mathf.Max(i - 1, 0)]; t.y = 0; t.Normalize(); var side = new Vector3(-t.z, 0, t.x);
                for (int k = 0; k < 3; k++)
                {
                    float u = k / 2f; var p = pts[i] + side * Mathf.Lerp(-hw, hw, u) * taper;
                    p.y = GroundY(p) + 0.035f;
                    v.Add(p); uv.Add(new Vector2((u - 0.5f) * hw * 2f, along)); col.Add(new Color(taper * (k == 1 ? 1f : 0.55f), 0, 0, 1));
                }
                if (i > 0) { int b0 = (i - 1) * 3, b1 = i * 3; for (int k = 0; k < 2; k++) tr.AddRange(new[] { b0 + k, b0 + k + 1, b1 + k, b0 + k + 1, b1 + k + 1, b1 + k }); }
            }
            FaceUp(v, tr);
            var m = new Mesh(); m.SetVertices(v); m.SetUVs(0, uv); m.SetColors(col); m.SetTriangles(tr, 0); m.RecalculateNormals();
            return SaveMesh(name, m);
        }

        static void WarmLight(Transform parent, string name, Vector3 at, float range, float intensity)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.position = at;
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.45f, 0.16f); l.range = range; l.intensity = intensity;
            l.shadows = LightShadows.None; l.lightmapBakeType = LightmapBakeType.Realtime; l.renderMode = LightRenderMode.Auto;
        }

        static ParticleSystem VentSmoke(Transform parent, Vector3 at)
        {
            var go = new GameObject("VentSmoke"); go.transform.SetParent(parent, false); go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.loop = true; main.playOnAwake = true; main.prewarm = true; main.maxParticles = 18;
            main.startLifetime = new ParticleSystem.MinMaxCurve(9f, 14f); main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            main.startSize = new ParticleSystem.MinMaxCurve(2.5f, 4.5f); main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.42f, 0.4f, 0.38f, 0.22f), new Color(0.55f, 0.52f, 0.5f, 0.14f));
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.gravityModifier = -0.015f;
            main.cullingMode = ParticleSystemCullingMode.PauseAndCatchup;
            var em = ps.emission; em.rateOverTime = 1.5f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 1.2f; go.transform.rotation = Quaternion.Euler(-90f, 0, 0);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            // all three axes as two constants: VolcanoLandmark rewrites x / z with the wind in the same mode
            vel.x = new ParticleSystem.MinMaxCurve(0.3f, 0.4f); vel.y = new ParticleSystem.MinMaxCurve(0.25f, 0.35f); vel.z = new ParticleSystem.MinMaxCurve(0.1f, 0.2f);
            var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 2.6f));
            var colm = ps.colorOverLifetime; colm.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.8f, 0.7f), 0f), new GradientColorKey(Color.white, 0.2f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            colm.color = g;
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = VfxMat("M_VFX_Soft"); r.shadowCastingMode = ShadowCastingMode.Off; r.sortingFudge = 3f; r.maxParticleSize = 1.2f;
            return ps;
        }

        /// <summary>small glowing sparks rising from the lava (additive, no shadows); rate per second</summary>
        static ParticleSystem VentEmbers(Transform parent, string name, Vector3 at, float radius, float rate)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.SetPositionAndRotation(at, Quaternion.Euler(-90f, 0, 0));
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.loop = true; main.playOnAwake = true; main.prewarm = true; main.maxParticles = 60;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3.6f); main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.6f, 0.2f, 1f), new Color(1f, 0.32f, 0.06f, 1f));
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.gravityModifier = 0.06f;
            main.cullingMode = ParticleSystemCullingMode.PauseAndCatchup;
            var em = ps.emission; em.rateOverTime = rate;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 18f; sh.radius = radius;
            var nz = ps.noise; nz.enabled = true; nz.strength = 0.6f; nz.frequency = 0.8f; nz.quality = ParticleSystemNoiseQuality.Low;
            var colm = ps.colorOverLifetime; colm.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.75f, 0.35f), 0f), new GradientColorKey(new Color(0.8f, 0.15f, 0.02f), 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.6f), new GradientAlphaKey(0f, 1f) });
            colm.color = g;
            var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = Or(VfxMat("M_VFX_AdditiveNoFog"), VfxMat("M_VFX_Additive"));
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.maxParticleSize = 0.05f;
            return ps;
        }

        static void VentShimmer(Transform parent, Vector3 at, Material haze, string name = "VentShimmer", float scale = 1f)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.loop = true; main.playOnAwake = true; main.prewarm = true; main.maxParticles = 4; main.startSize3D = true;
            main.startLifetime = 8f; main.startSpeed = 0f;
            main.startSizeX = new ParticleSystem.MinMaxCurve(3.5f * scale, 5f * scale); main.startSizeY = new ParticleSystem.MinMaxCurve(5f * scale, 7f * scale); main.startSizeZ = 1f;
            main.startColor = new Color(1f, 1f, 1f, 0.55f); main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode = ParticleSystemCullingMode.Pause;
            var em = ps.emission; em.rateOverTime = 0.4f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 1.2f;
            var colm = ps.colorOverLifetime; colm.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            colm.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = haze; r.renderMode = ParticleSystemRenderMode.VerticalBillboard;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.sortingFudge = 10f;
            r.SetActiveVertexStreams(new List<ParticleSystemVertexStream> { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV, ParticleSystemVertexStream.StableRandomX });
        }
    }
}
