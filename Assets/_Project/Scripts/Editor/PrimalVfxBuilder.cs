using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using PrimalFrontier.VFX;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// Generates the primitive-survival VFX set in code: procedural particle textures, 4 shared URP particle materials,
    /// one prefab per VfxId and Resources/VfxLibrary.asset. Small particle counts (mobile), world simulation, no fantasy glow.
    /// batch: -executeMethod PrimalFrontier.EditorTools.PrimalVfxBuilder.BuildFromCommandLine
    /// </summary>
    public static class PrimalVfxBuilder
    {
        const string Root = "Assets/_Project/VFX";
        static Material _soft, _add, _drop, _chip, _leaf;

        public static void BuildFromCommandLine()
        {
            int code = 0;
            try { Build(); }
            catch (Exception e) { Debug.LogError("[PrimalVfxBuilder] " + e); code = 1; }
            EditorApplication.Exit(code);
        }

        [MenuItem("Primal Frontier/VFX/Build VFX Library")]
        public static void Build()
        {
            foreach (var d in new[] { Root, Root + "/Textures", Root + "/Materials", Root + "/Prefabs", "Assets/_Project/Resources" }) Directory.CreateDirectory(d);
            var tSoft = Tex("T_VFX_Soft", 64, (u, v) => { float r = Mathf.Clamp01(1 - Len(u, v)); return new Color(1, 1, 1, r * r); });
            var tPuff = Tex("T_VFX_Puff", 128, (u, v) =>
            {
                float r = Len(u, v); float n = Mathf.PerlinNoise(u * 4 + 11, v * 4 + 7) * 0.6f + Mathf.PerlinNoise(u * 9 + 3, v * 9 + 1) * 0.4f;
                float a = Mathf.Clamp01((1 - r) * 1.6f) * Mathf.Clamp01(n * 1.6f - 0.25f); return new Color(1, 1, 1, a);
            });
            var tDrop = Tex("T_VFX_Drop", 32, (u, v) => { float r = Len(u * 1.0f, v * 0.8f); float a = Mathf.Clamp01((1 - r) * 3f); return new Color(1, 1, 1, a); });
            var tChip = Tex("T_VFX_Chip", 32, (u, v) =>
            {
                float a = Mathf.Abs(u) + Mathf.Abs(v * 1.4f) < 0.8f + 0.15f * Mathf.PerlinNoise(u * 6, v * 6) ? 1f : 0f; float sh = 0.75f + 0.25f * u; return new Color(sh, sh, sh, a);
            });
            var tLeaf = Tex("T_VFX_Leaf", 32, (u, v) =>
            {
                float w = 0.45f * (1 - v * v); float a = Mathf.Abs(u) < w && v > -0.95f && v < 0.95f ? 1f : 0f; float vein = Mathf.Abs(u) < 0.05f ? 0.8f : 1f; return new Color(vein, vein, vein, a);
            });
            _soft = Mat("M_VFX_Soft", tPuff, false); _add = Mat("M_VFX_Additive", tSoft, true); _drop = Mat("M_VFX_Drop", tDrop, false);
            _chip = Mat("M_VFX_Chip", tChip, false); _leaf = Mat("M_VFX_Leaf", tLeaf, false);

            var lib = ScriptableObject.CreateInstance<VfxLibrary>();
            void Add(VfxId id, int prewarm, Action<GameObject> build)
            {
                var go = new GameObject("VFX_" + id);
                try
                {
                    build(go);
                    go.AddComponent<PooledEffect>();
                    var path = $"{Root}/Prefabs/VFX_{id}.prefab";
                    var p = PrefabUtility.SaveAsPrefabAsset(go, path);
                    lib.entries.Add(new VfxLibrary.Entry { id = id, prefab = p, prewarm = prewarm });
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
            }
            var blood = new Color(0.36f, 0.02f, 0.015f, 1f); var blood2 = new Color(0.5f, 0.05f, 0.03f, 1f);
            var sand = new Color(0.80f, 0.72f, 0.58f, 0.55f); var dirt = new Color(0.46f, 0.38f, 0.30f, 0.55f); var rock = new Color(0.6f, 0.6f, 0.58f, 0.45f);
            var wood = new Color(0.66f, 0.49f, 0.30f, 1f); var stone = new Color(0.55f, 0.55f, 0.53f, 1f); var water = new Color(0.8f, 0.9f, 1f, 0.8f);

            Add(VfxId.HitLight, 3, g => { Drops(g, "Drops", blood, blood2, 9, 1.2f, 2.6f, 0.012f, 0.03f, 0.45f); Puff(g, "Mist", new Color(0.45f, 0.05f, 0.04f, 0.35f), 2, 0.08f, 0.2f, 0.3f, 0.2f); });
            Add(VfxId.HitHeavy, 2, g => { Drops(g, "Drops", blood, blood2, 20, 1.8f, 4.0f, 0.015f, 0.045f, 0.6f); Puff(g, "Mist", new Color(0.45f, 0.05f, 0.04f, 0.4f), 4, 0.12f, 0.3f, 0.4f, 0.3f); Puff(g, "Dust", dirt, 3, 0.15f, 0.35f, 0.7f, 0.5f); });
            Add(VfxId.Bleed, 2, g =>
            {
                var ps = Emitter(g, "Drip", _drop, 4f, true); var m = ps.main;
                m.startLifetime = R(0.4f, 0.7f); m.startSpeed = R(0.05f, 0.3f); m.startSize = R(0.008f, 0.018f); m.gravityModifier = 1f; m.startColor = new ParticleSystem.MinMaxGradient(blood, blood2);
                var e = ps.emission; e.rateOverTime = 5f; Shape(ps, ParticleSystemShapeType.Sphere, 0, 0.05f); Stretch(ps, 0.04f);
            });
            Add(VfxId.FootSand, 6, g => Puff(g, "Dust", sand, 6, 0.08f, 0.2f, 0.8f, 0.55f, 0.6f));
            Add(VfxId.FootDirt, 6, g => Puff(g, "Dust", dirt, 5, 0.07f, 0.18f, 0.7f, 0.5f, 0.5f));
            Add(VfxId.FootMud, 4, g => { Drops(g, "Splash", new Color(0.25f, 0.2f, 0.14f, 1), new Color(0.32f, 0.26f, 0.18f, 1), 7, 0.6f, 1.4f, 0.012f, 0.025f, 0.4f, 35f); Puff(g, "Dust", new Color(0.3f, 0.25f, 0.18f, 0.35f), 2, 0.06f, 0.12f, 0.4f, 0.3f); });
            Add(VfxId.FootRock, 4, g => Puff(g, "Dust", rock, 3, 0.05f, 0.1f, 0.5f, 0.35f, 0.4f));
            Add(VfxId.LandDust, 2, g => Puff(g, "Dust", sand, 12, 0.15f, 0.35f, 1.1f, 1.2f, 0.8f));
            Add(VfxId.WoodChips, 4, g => { Chips(g, "Chips", _chip, wood, new Color(0.5f, 0.36f, 0.22f, 1), 10, 1.8f, 3.6f, 0.02f, 0.05f, 0.9f); Puff(g, "Dust", new Color(0.62f, 0.52f, 0.4f, 0.4f), 3, 0.08f, 0.18f, 0.6f, 0.4f); });
            Add(VfxId.StoneChips, 4, g => { Chips(g, "Chips", _chip, stone, new Color(0.42f, 0.42f, 0.4f, 1), 8, 2.0f, 4.0f, 0.015f, 0.04f, 0.8f); Puff(g, "Dust", rock, 4, 0.08f, 0.2f, 0.7f, 0.5f); Sparks(g, 4, 0.12f); });
            Add(VfxId.Leaves, 4, g =>
            {
                var ps = Chips(g, "Leaves", _leaf, new Color(0.35f, 0.5f, 0.2f, 1), new Color(0.5f, 0.55f, 0.25f, 1), 8, 0.6f, 1.6f, 0.03f, 0.06f, 1.8f);
                var m = ps.main; m.gravityModifier = 0.12f; var n = ps.noise; n.enabled = true; n.strength = 0.6f; n.frequency = 1.5f;
            });
            Add(VfxId.CraftDust, 3, g => { Puff(g, "Dust", new Color(0.6f, 0.52f, 0.42f, 0.4f), 5, 0.06f, 0.14f, 0.7f, 0.35f); Chips(g, "Bits", _chip, wood, stone, 4, 0.5f, 1.2f, 0.01f, 0.025f, 0.6f); });
            Add(VfxId.CraftSparks, 3, g => Sparks(g, 7, 0.18f));
            Add(VfxId.FoodCrumbs, 3, g => Chips(g, "Crumbs", _chip, new Color(0.55f, 0.32f, 0.2f, 1), new Color(0.72f, 0.5f, 0.3f, 1), 6, 0.4f, 1.1f, 0.006f, 0.014f, 0.5f));
            Add(VfxId.Steam, 3, g =>
            {
                var ps = Emitter(g, "Steam", _soft, 2.5f, true); var m = ps.main;
                m.startLifetime = R(1.2f, 1.8f); m.startSpeed = R(0.15f, 0.35f); m.startSize = R(0.05f, 0.1f); m.startColor = new Color(1, 1, 1, 0.22f); m.gravityModifier = -0.05f;
                var e = ps.emission; e.rateOverTime = 6f; Shape(ps, ParticleSystemShapeType.Cone, 12f, 0.04f); Grow(ps, 3.5f); Fade(ps); RandRot(ps);
            });
            Add(VfxId.WaterSplash, 3, g => { Drops(g, "Drops", water, new Color(0.7f, 0.82f, 0.95f, 0.7f), 14, 1.2f, 2.8f, 0.012f, 0.03f, 0.55f, 30f); Puff(g, "Spray", new Color(0.9f, 0.95f, 1f, 0.3f), 4, 0.1f, 0.25f, 0.5f, 0.6f); });
            Add(VfxId.WaterDrops, 3, g => Drops(g, "Drips", water, water, 6, 0.1f, 0.4f, 0.008f, 0.016f, 0.5f, 10f));
            Add(VfxId.FireIgnite, 2, g =>
            {
                Sparks(g, 18, 0.5f);
                var ps = Emitter(g, "Flash", _add, 0.4f, false); var m = ps.main; m.startLifetime = 0.25f; m.startSpeed = 0f; m.startSize = R(0.3f, 0.5f); m.startColor = new Color(1f, 0.6f, 0.2f, 0.6f);
                Burst(ps, 2); Shape(ps, ParticleSystemShapeType.Sphere, 0, 0.05f); Fade(ps);
            });
            Add(VfxId.FireExtinguish, 2, g =>
            {
                var ps = Emitter(g, "Steam", _soft, 0.6f, false); var m = ps.main;
                m.startLifetime = R(1.5f, 2.5f); m.startSpeed = R(0.4f, 1.0f); m.startSize = R(0.2f, 0.4f); m.startColor = new Color(0.85f, 0.85f, 0.85f, 0.4f); m.gravityModifier = -0.1f;
                Burst(ps, 14); Shape(ps, ParticleSystemShapeType.Cone, 25f, 0.25f); Grow(ps, 3f); Fade(ps); RandRot(ps);
            });
            Add(VfxId.CookSmoke, 2, g =>
            {
                var ps = Emitter(g, "Smoke", _soft, 3f, true); var m = ps.main;
                m.startLifetime = R(1.5f, 2.2f); m.startSpeed = R(0.2f, 0.4f); m.startSize = R(0.08f, 0.14f); m.startColor = new Color(0.7f, 0.68f, 0.64f, 0.3f); m.gravityModifier = -0.04f;
                var e = ps.emission; e.rateOverTime = 5f; Shape(ps, ParticleSystemShapeType.Cone, 10f, 0.06f); Grow(ps, 4f); Fade(ps); RandRot(ps);
            });
            Add(VfxId.SpearImpact, 3, g => { Puff(g, "Dust", dirt, 5, 0.08f, 0.18f, 0.7f, 0.6f); Chips(g, "Bits", _chip, new Color(0.4f, 0.34f, 0.28f, 1), stone, 6, 1.5f, 3f, 0.01f, 0.03f, 0.6f); });
            Add(VfxId.ArrowImpact, 3, g => { Puff(g, "Dust", dirt, 4, 0.05f, 0.12f, 0.5f, 0.4f); Chips(g, "Splinters", _chip, wood, wood, 4, 1f, 2.2f, 0.008f, 0.02f, 0.5f); });
            Add(VfxId.DinoFootDust, 4, g => Puff(g, "Dust", dirt, 8, 0.3f, 0.7f, 1.4f, 1.2f, 0.9f));
            Add(VfxId.DinoImpactDust, 2, g => { Puff(g, "Dust", dirt, 14, 0.4f, 1.0f, 1.8f, 2.4f, 1.2f); Chips(g, "Debris", _chip, stone, new Color(0.35f, 0.3f, 0.25f, 1), 10, 2f, 4.5f, 0.03f, 0.07f, 1f); });

            BuildCampfire();
            const string libPath = "Assets/_Project/Resources/VfxLibrary.asset";
            AssetDatabase.DeleteAsset(libPath); AssetDatabase.CreateAsset(lib, libPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[PrimalVfxBuilder] {lib.entries.Count} effects -> {libPath}");
        }

        /// <summary>persistent (not pooled) campfire effect: flames, smoke, embers, flickering light, crackle loop</summary>
        static void BuildCampfire()
        {
            var root = new GameObject("VFX_CampfireLoop");
            try
            {
                var fx = root.AddComponent<CampfireFx>();
                var holder = new GameObject("Flames"); holder.transform.SetParent(root.transform, false);
                var fl = holder.AddComponent<ParticleSystem>(); var m = fl.main;
                m.loop = true; m.duration = 1f; m.playOnAwake = false; m.simulationSpace = ParticleSystemSimulationSpace.World; m.maxParticles = 40;
                m.startLifetime = R(0.45f, 0.8f); m.startSpeed = R(0.5f, 0.9f); m.startSize = R(0.18f, 0.34f); m.gravityModifier = -0.15f;
                m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.72f, 0.3f, 0.9f), new Color(1f, 0.5f, 0.15f, 0.9f));
                var e = fl.emission; e.rateOverTime = 22f; Shape(fl, ParticleSystemShapeType.Cone, 8f, 0.14f);
                var col = fl.colorOverLifetime; col.enabled = true; var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 0.5f), 0), new GradientColorKey(new Color(1f, 0.45f, 0.1f), 0.5f), new GradientColorKey(new Color(0.6f, 0.15f, 0.05f), 1) },
                          new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.9f, 0.12f), new GradientAlphaKey(0.5f, 0.6f), new GradientAlphaKey(0, 1) });
                col.color = g; Shrink(fl); RandRot(fl);
                var n = fl.noise; n.enabled = true; n.strength = 0.25f; n.frequency = 2.5f; n.scrollSpeed = 1.5f;
                var r = holder.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = _add; r.shadowCastingMode = ShadowCastingMode.Off; r.maxParticleSize = 0.6f;
                var smk = new GameObject("Smoke"); smk.transform.SetParent(root.transform, false); smk.transform.localPosition = new Vector3(0, 0.45f, 0);
                var sp = smk.AddComponent<ParticleSystem>(); m = sp.main;
                m.loop = true; m.duration = 1f; m.playOnAwake = false; m.simulationSpace = ParticleSystemSimulationSpace.World; m.maxParticles = 30;
                m.startLifetime = R(3f, 4.5f); m.startSpeed = R(0.35f, 0.6f); m.startSize = R(0.25f, 0.4f); m.startColor = new Color(0.35f, 0.33f, 0.31f, 0.28f); m.gravityModifier = -0.03f;
                e = sp.emission; e.rateOverTime = 4f; Shape(sp, ParticleSystemShapeType.Cone, 10f, 0.1f); Grow(sp, 4f); Fade(sp, 0.2f); RandRot(sp);
                var vel = sp.velocityOverLifetime; vel.enabled = true; vel.x = new ParticleSystem.MinMaxCurve(0.15f); vel.space = ParticleSystemSimulationSpace.World;
                vel.y = new ParticleSystem.MinMaxCurve(0f); vel.z = new ParticleSystem.MinMaxCurve(0f);
                r = smk.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = _soft; r.shadowCastingMode = ShadowCastingMode.Off; r.maxParticleSize = 1.5f;
                var emb = new GameObject("Embers"); emb.transform.SetParent(root.transform, false); emb.transform.localPosition = new Vector3(0, 0.2f, 0);
                var ep = emb.AddComponent<ParticleSystem>(); m = ep.main;
                m.loop = true; m.duration = 1f; m.playOnAwake = false; m.simulationSpace = ParticleSystemSimulationSpace.World; m.maxParticles = 25;
                m.startLifetime = R(1f, 2f); m.startSpeed = R(0.8f, 1.8f); m.startSize = R(0.008f, 0.018f); m.startColor = new Color(1f, 0.55f, 0.2f, 1f); m.gravityModifier = -0.05f;
                e = ep.emission; e.rateOverTime = 6f; Shape(ep, ParticleSystemShapeType.Cone, 20f, 0.12f); Fade(ep, 0.05f);
                n = ep.noise; n.enabled = true; n.strength = 0.6f; n.frequency = 1.2f;
                r = emb.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = _add; r.shadowCastingMode = ShadowCastingMode.Off;
                var lg = new GameObject("FireLight"); lg.transform.SetParent(root.transform, false); lg.transform.localPosition = new Vector3(0, 0.5f, 0);
                var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.62f, 0.3f); l.range = 7f; l.intensity = 0f; l.shadows = LightShadows.None;
                var au = root.AddComponent<AudioSource>(); au.loop = true; au.playOnAwake = false; au.spatialBlend = 1f; au.maxDistance = 18f; au.rolloffMode = AudioRolloffMode.Linear;
                au.clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/Ambience/AMB_Fire_Loop.wav");
                fx.flames = fl; fx.smoke = sp; fx.embers = ep; fx.fireLight = l; fx.crackle = au;
                PrefabUtility.SaveAsPrefabAsset(root, $"{Root}/Prefabs/VFX_CampfireLoop.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        // ---------------------------------------------------------------- emitters
        static ParticleSystem.MinMaxCurve R(float a, float b) => new ParticleSystem.MinMaxCurve(a, b);
        static float Len(float u, float v) => Mathf.Sqrt(u * u + v * v);

        static ParticleSystem Emitter(GameObject parent, string name, Material mat, float duration, bool loop)
        {
            GameObject go;
            if (parent.GetComponent<ParticleSystem>() == null) go = parent;          // first emitter lives on the root (drives stopAction)
            else { go = new GameObject(name); go.transform.SetParent(parent.transform, false); }
            var ps = go.AddComponent<ParticleSystem>();
            var m = ps.main; m.duration = duration; m.loop = loop; m.playOnAwake = false; m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.maxParticles = 64; m.scalingMode = ParticleSystemScalingMode.Hierarchy;
            m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;   // tiny systems: never freeze off screen (they must finish to return to the pool)
            var e = ps.emission; e.rateOverTime = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.sortingFudge = 0; r.minParticleSize = 0f; r.maxParticleSize = 0.5f;
            return ps;
        }
        static void Burst(ParticleSystem ps, int n) { var e = ps.emission; e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)n) }); }
        static void Shape(ParticleSystem ps, ParticleSystemShapeType t, float angle, float radius)
        { var s = ps.shape; s.enabled = true; s.shapeType = t; s.angle = angle; s.radius = radius; s.rotation = Vector3.zero; }
        static void Stretch(ParticleSystem ps, float velScale)
        { var r = ps.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = velScale; r.lengthScale = 1.2f; }
        static void Fade(ParticleSystem ps, float peak = 0.15f)
        {
            var c = ps.colorOverLifetime; c.enabled = true; var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, peak), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0, 1) });
            c.color = g;
        }
        static void Grow(ParticleSystem ps, float to)
        { var s = ps.sizeOverLifetime; s.enabled = true; s.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 1f / to, 1, 1f)); }
        static void Shrink(ParticleSystem ps)
        { var s = ps.sizeOverLifetime; s.enabled = true; s.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.2f)); }
        static void RandRot(ParticleSystem ps) { var m = ps.main; m.startRotation = R(0, Mathf.PI * 2); var r = ps.rotationOverLifetime; r.enabled = true; r.z = R(-0.8f, 0.8f); }

        static ParticleSystem Puff(GameObject g, string name, Color col, int n, float s0, float s1, float life, float speed, float grow = 2.5f)
        {
            var ps = Emitter(g, name, _soft, life + 0.2f, false); var m = ps.main;
            m.startLifetime = R(life * 0.7f, life); m.startSpeed = R(speed * 0.4f, speed); m.startSize = R(s0, s1); m.startColor = col; m.gravityModifier = -0.02f;
            var lv = ps.limitVelocityOverLifetime; lv.enabled = true; lv.limit = 0.1f; lv.dampen = 0.15f;
            Burst(ps, n); Shape(ps, ParticleSystemShapeType.Hemisphere, 0, 0.05f); Grow(ps, grow); Fade(ps); RandRot(ps);
            return ps;
        }
        static ParticleSystem Drops(GameObject g, string name, Color a, Color b, int n, float v0, float v1, float s0, float s1, float life, float cone = 40f)
        {
            var ps = Emitter(g, name, _drop, life + 0.2f, false); var m = ps.main;
            m.startLifetime = R(life * 0.6f, life); m.startSpeed = R(v0, v1); m.startSize = R(s0, s1); m.startColor = new ParticleSystem.MinMaxGradient(a, b); m.gravityModifier = 1.2f;
            Burst(ps, n); Shape(ps, ParticleSystemShapeType.Cone, cone, 0.02f); Stretch(ps, 0.035f); Shrink(ps);
            return ps;
        }
        static ParticleSystem Chips(GameObject g, string name, Material mat, Color a, Color b, int n, float v0, float v1, float s0, float s1, float life)
        {
            var ps = Emitter(g, name, mat, life + 0.2f, false); var m = ps.main;
            m.startLifetime = R(life * 0.6f, life); m.startSpeed = R(v0, v1); m.startSize = R(s0, s1); m.startColor = new ParticleSystem.MinMaxGradient(a, b); m.gravityModifier = 1f;
            Burst(ps, n); Shape(ps, ParticleSystemShapeType.Cone, 35f, 0.02f); RandRot(ps);
            var rot = ps.rotationOverLifetime; rot.z = R(-8f, 8f);
            var c = ps.collision; c.enabled = true; c.type = ParticleSystemCollisionType.World; c.mode = ParticleSystemCollisionMode.Collision3D; c.dampen = 0.6f; c.bounce = 0.2f;
            c.quality = ParticleSystemCollisionQuality.Low; c.lifetimeLoss = 0.2f; c.maxCollisionShapes = 4;
            return ps;
        }
        static ParticleSystem Sparks(GameObject g, int n, float life)
        {
            var ps = Emitter(g, "Sparks", _add, life + 0.2f, false); var m = ps.main;
            m.startLifetime = R(life * 0.5f, life); m.startSpeed = R(1.5f, 3.5f); m.startSize = R(0.008f, 0.016f); m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.35f), new Color(1f, 0.5f, 0.15f)); m.gravityModifier = 0.8f;
            Burst(ps, n); Shape(ps, ParticleSystemShapeType.Cone, 45f, 0.01f); Stretch(ps, 0.02f); Fade(ps, 0.05f);
            return ps;
        }

        // ---------------------------------------------------------------- textures / materials
        static Texture2D Tex(string name, int size, Func<float, float, Color> f)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                    t.SetPixel(x, y, f((x + 0.5f) / size * 2 - 1, (y + 0.5f) / size * 2 - 1));
            t.Apply();
            string path = $"{Root}/Textures/{name}.png";
            File.WriteAllBytes(path, t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp; ti.mipmapEnabled = true; ti.textureCompression = TextureImporterCompression.Compressed;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material Mat(string name, Texture2D tex, bool additive)
        {
            string path = $"{Root}/Materials/{name}.mat";
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZWrite", 0f); m.SetFloat("_Cull", 0f);
            m.SetOverrideTag("RenderType", "Transparent"); m.renderQueue = (int)RenderQueue.Transparent;
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON"); m.DisableKeyword("_ALPHAMODULATE_ON");
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
