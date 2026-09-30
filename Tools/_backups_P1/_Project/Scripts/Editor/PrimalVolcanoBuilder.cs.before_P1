using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using PrimalFrontier.Core;
using PrimalFrontier.World;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// The distant volcano landmark (Tools/BlenderPipeline pf_volcano.py -> ENV_Volcano.fbx + Data/World/volcano.json):
    /// materials, prefab with its smoke plume / embers / crater glow and the VolcanoLandmark script, placed across the
    /// sea from the start beach under [World]/Landmarks (or a new Landmarks root). Scenery only: no collider, the
    /// playable island is unchanged. Safe to run again: an existing placed volcano is kept (arg "prefab" only rebuilds
    /// the prefab).
    /// Atmosphere: ash flakes (M_VFX_Chip) and the heat shimmer (PF/Heat Shimmer, M_VFX_HeatShimmer) on the placed
    /// VolcanoLandmark, which builds both effects at run time; arg "off" switches them off again.
    /// </summary>
    public static class PrimalVolcanoBuilder
    {
        const string Fbx = "Assets/_Project/Art/Models/Environment/ENV_Volcano.fbx";
        const string PrefabPath = "Assets/_Project/Prefabs/Environment/PFB_ENV_Volcano.prefab";
        const string Mats = "Assets/_Project/Art/Materials", Tex = "Assets/_Project/Art/Textures";
        const string Scene = "Assets/_Project/Scenes/Island_VerticalSlice.unity";
        public static readonly Vector3 Place = new Vector3(-360f, -2f, 640f);
        static readonly StringBuilder Log = new StringBuilder();
        static void L(string s) { Log.AppendLine(s); Debug.Log("[PrimalVolcano] " + s); }

        [MenuItem("Primal Frontier/Scene/Add Volcano Landmark", priority = 21)]
        static void Menu() => EditorUtility.DisplayDialog("Primal Frontier", Build(""), "OK");

        [PrimalBridgeCommand]
        public static string Build(string arg)
        {
            Log.Clear();
            AssetDatabase.Refresh();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            var metaTa = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_Project/Data/World/volcano.json");
            if (!model || !metaTa) return "MISSING ENV_Volcano.fbx or Data/World/volcano.json (run pf_volcano.build_all in Blender)";
            var meta = JsonUtility.FromJson<Meta>(metaTa.text);
            var rock = LitMat("M_VolcanoRock", "T_Rock", new Color(0.36f, 0.33f, 0.31f), 0.12f);
            var ash = LitMat("M_VolcanoAsh", "T_Mud", new Color(0.46f, 0.44f, 0.42f), 0.08f);
            var lava = LavaMat();
            L(ShaderCheck("PF/Landmark Lit") + ShaderCheck("PF/Particles Additive No Fog"));
            var prefab = BuildPrefab(model, meta, rock, ash, lava);
            if (arg != "prefab") PlaceInScene(prefab);
            AssetDatabase.SaveAssets();
            return Log.ToString();
        }

        [System.Serializable] class Meta { public float height, radius, crater_radius; public float[] crater_floor, rim, flow_top, flow_mid; }
        static Vector3 U(float[] b) => BlenderSpace.ToUnityPosition(b[0], b[1], b[2]);

        /// <summary>"shader: ok" or its compile errors (used to fall back to URP Lit if a custom shader fails)</summary>
        public static string ShaderCheck(string name)
        {
            var sh = Shader.Find(name);
            if (!sh) return $"{name}: NOT FOUND\n";
            var msgs = ShaderUtil.GetShaderMessages(sh);
            var errs = msgs.Where(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).ToArray();
            return errs.Length == 0 && !ShaderUtil.ShaderHasError(sh) ? $"{name}: ok\n" : $"{name}: {errs.Length} errors: " + string.Join(" | ", errs.Take(4).Select(e => e.message + " (line " + e.line + ")")) + "\n";
        }
        static Shader Landmark()
        {
            var sh = Shader.Find("PF/Landmark Lit");
            return sh && !ShaderUtil.ShaderHasError(sh) ? sh : Shader.Find("Universal Render Pipeline/Lit");
        }

        static Material LitMat(string name, string tex, Color tint, float smooth)
        {
            string p = $"{Mats}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(Landmark()) { name = name, enableInstancing = true }; AssetDatabase.CreateAsset(m, p); L("material " + name); }
            m.shader = Landmark();
            m.SetColor("_TopColor", new Color(1.35f, 1.3f, 1.25f)); m.SetFloat("_TopHeight", 150f); m.SetFloat("_TopBlend", 70f);
            m.SetFloat("_GullyDark", 0.5f); m.SetFloat("_FogStrength", 0.38f); m.SetFloat("_Wrap", 0.1f);
            var d = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Tex}/{tex}_D.png"); var n = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Tex}/{tex}_N.png");
            if (d) m.SetTexture("_BaseMap", d);
            if (n) { m.SetTexture("_BumpMap", n); m.EnableKeyword("_NORMALMAP"); m.SetFloat("_BumpScale", 1.2f); }
            m.SetColor("_BaseColor", tint); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material LavaMat()
        {
            string p = $"{Mats}/M_VolcanoLava.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(Landmark()) { name = "M_VolcanoLava" }; AssetDatabase.CreateAsset(m, p); L("material M_VolcanoLava"); }
            m.shader = Landmark(); m.SetFloat("_FogStrength", 0.3f); m.SetFloat("_GullyDark", 0f); m.SetColor("_TopColor", Color.white);
            m.SetColor("_BaseColor", new Color(0.25f, 0.06f, 0.02f)); m.SetFloat("_Smoothness", 0.35f);
            m.EnableKeyword("_EMISSION"); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.SetColor("_EmissionColor", new Color(4f, 1.1f, 0.15f));
            var n = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Tex}/T_Rock_N.png"); if (n) { m.SetTexture("_BumpMap", n); m.EnableKeyword("_NORMALMAP"); }
            EditorUtility.SetDirty(m);
            return m;
        }

        static GameObject BuildPrefab(GameObject model, Meta meta, Material rock, Material ash, Material lava)
        {
            var root = new GameObject("PFB_ENV_Volcano");
            try
            {
                var m = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform); m.name = "Model";
                foreach (var r in m.GetComponentsInChildren<Renderer>(true))
                {
                    var arr = r.sharedMaterials;
                    for (int i = 0; i < arr.Length; i++)
                    {
                        string n = arr[i] ? arr[i].name : "";
                        arr[i] = n.Contains("Lava") ? lava : n.Contains("Ash") ? ash : rock;
                    }
                    r.sharedMaterials = arr;
                    bool isLava = r.name.Contains("Lava");
                    r.shadowCastingMode = isLava ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    r.receiveShadows = !isLava;
                }
                var lg = m.GetComponentInChildren<LODGroup>();
                if (lg)
                {
                    var lods = lg.GetLODs();
                    if (lods.Length >= 2) { lods[0].screenRelativeTransitionHeight = 0.2f; lods[1].screenRelativeTransitionHeight = 0.004f; lg.SetLODs(lods); }
                }
                Vector3 rim = U(meta.rim), floor = U(meta.crater_floor);
                var vl = root.AddComponent<VolcanoLandmark>();
                vl.smoke = Smoke(root.transform, rim + Vector3.up * 6f, meta.crater_radius);
                vl.embers = Embers(root.transform, floor + Vector3.up * 4f, meta.crater_radius);
                vl.glow = Glow(root.transform, floor + Vector3.up * 30f, "Crater_Glow", 150f, new Color(1f, 0.42f, 0.12f, 0.3f));
                Vector3 ft = U(meta.flow_top), fm = U(meta.flow_mid);
                var flows = new System.Collections.Generic.List<ParticleSystem>();
                for (int i = 0; i < 9; i++)
                {
                    // overlapping soft halos a little in front of the slope so they read as one glowing streak
                    float k = i / 8f; Vector3 fp = Vector3.Lerp(ft, fm + (fm - ft) * 0.35f, k);
                    Vector3 outward = new Vector3(fp.x, 0f, fp.z).normalized;
                    fp += outward * 22f + Vector3.up * 8f;
                    flows.Add(Glow(root.transform, fp, "Flow_Glow_" + i, Mathf.Lerp(48f, 26f, k), new Color(1f, 0.36f, 0.08f, 0.14f)));
                }
                vl.flowGlows = flows.ToArray();
                vl.lava = m.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name.Contains("Lava"));
                vl.ashMaterial = AshMat(); vl.hazeMaterial = HazeMat();
                Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
                var p = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                L("prefab " + PrefabPath);
                return p;
            }
            finally { Object.DestroyImmediate(root); }
        }

        static Material Vfx(string name) => AssetDatabase.LoadAssetAtPath<Material>($"Assets/_Project/VFX/Materials/{name}.mat");

        /// <summary>additive glow that is not hidden by the sea fog at night (falls back to the normal additive VFX material)</summary>
        static Material GlowMat()
        {
            var baseMat = Vfx("M_VFX_Additive");
            var sh = Shader.Find("PF/Particles Additive No Fog");
            if (!sh || ShaderUtil.ShaderHasError(sh)) return baseMat;
            string p = "Assets/_Project/VFX/Materials/M_VFX_AdditiveNoFog.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(sh) { name = "M_VFX_AdditiveNoFog", enableInstancing = true }; AssetDatabase.CreateAsset(m, p); L("material M_VFX_AdditiveNoFog"); }
            m.shader = sh;
            if (baseMat) m.SetTexture("_BaseMap", baseMat.GetTexture("_BaseMap"));
            m.SetColor("_BaseColor", Color.white); m.SetFloat("_Boost", 1.6f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static ParticleSystem NewPs(Transform parent, string name, Vector3 pos, Material mat)
        {
            var g = new GameObject(name); g.transform.SetParent(parent, false); g.transform.localPosition = pos;
            g.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);         // emit upwards
            var ps = g.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var r = g.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.sortingFudge = 10f;
            var main = ps.main; main.playOnAwake = true; main.loop = true; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate; main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            return ps;
        }

        static ParticleSystem Smoke(Transform parent, Vector3 pos, float craterR)
        {
            var ps = NewPs(parent, "Smoke_Plume", pos, Vfx("M_VFX_Soft"));
            var m = ps.main; m.duration = 20f; m.startLifetime = new ParticleSystem.MinMaxCurve(38f, 55f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(7f, 11f); m.startSize = new ParticleSystem.MinMaxCurve(60f, 105f);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f); m.maxParticles = 60; m.prewarm = true;
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.34f, 0.32f, 0.31f, 0.55f), new Color(0.5f, 0.48f, 0.46f, 0.5f));
            var e = ps.emission; e.rateOverTime = 1.1f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 12f; sh.radius = craterR * 0.45f;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 2.8f));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(new Color(0.8f, 0.78f, 0.76f), 0f), new GradientColorKey(Color.white, 1f) },
                                             new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
            v.x = new ParticleSystem.MinMaxCurve(2f, 5f); v.y = new ParticleSystem.MinMaxCurve(0f, 0f); v.z = new ParticleSystem.MinMaxCurve(1f, 3f);
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 9f; lim.dampen = 0.05f;
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            ps.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.Billboard;
            return ps;
        }

        static ParticleSystem Embers(Transform parent, Vector3 pos, float craterR)
        {
            var ps = NewPs(parent, "Embers", pos, GlowMat());
            var m = ps.main; m.duration = 5f; m.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 5f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(22f, 40f); m.startSize = new ParticleSystem.MinMaxCurve(2.5f, 6f); m.gravityModifier = 0.9f; m.maxParticles = 80;
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.55f, 0.15f, 1f), new Color(1f, 0.3f, 0.05f, 1f));
            var e = ps.emission; e.rateOverTime = 4f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 25f; sh.radius = craterR * 0.3f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.3f, 0.1f), 1f) },
                                             new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            return ps;
        }

        static ParticleSystem Glow(Transform parent, Vector3 pos, string name, float size, Color color)
        {
            var ps = NewPs(parent, name, pos, GlowMat());
            var m = ps.main; m.duration = 10f; m.startLifetime = 9999f; m.startSpeed = 0f; m.startSize = size; m.maxParticles = 1; m.prewarm = false;
            m.startColor = color;
            var e = ps.emission; e.rateOverTime = 0f; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            var sh = ps.shape; sh.enabled = false;
            return ps;
        }

        /// <summary>ash flake + heat shimmer materials on the placed volcano; arg "off" turns both effects off</summary>
        [PrimalBridgeCommand]
        public static string Atmosphere(string arg)
        {
            Log.Clear();
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "stop Play mode first";
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != Scene)
            {
                if (scene.isDirty) return "the open scene has unsaved changes: save it or open " + Scene + ", then run again";
                scene = EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            }
            bool wasDirty = scene.isDirty;
            var vl = Object.FindFirstObjectByType<VolcanoLandmark>();
            if (!vl) return "no VolcanoLandmark in the scene (run PrimalVolcanoBuilder.Build first)";
            bool off = arg == "off";
            vl.ashFall = !off; vl.heatHaze = !off;
            if (!off)
            {
                vl.ashMaterial = AshMat(); vl.hazeMaterial = HazeMat();
                L(vl.ashMaterial ? "ash fall: " + vl.ashMaterial.name : "ash fall: no M_VFX_Chip / M_VFX_Soft, uses the smoke plume's material");
                L(vl.hazeMaterial ? "heat shimmer: " + vl.hazeMaterial.name : "heat shimmer: PF/Heat Shimmer missing or not compiling, stays off");
            }
            else L("ash fall and heat shimmer off (VolcanoLandmark.ashFall / heatHaze)");
            EditorUtility.SetDirty(vl);
            if (PrefabUtility.IsPartOfPrefabInstance(vl)) PrefabUtility.RecordPrefabInstancePropertyModifications(vl);
            EditorSceneManager.MarkSceneDirty(scene);
            if (wasDirty) L("the scene had other unsaved changes: NOT saved, save it yourself");
            else { EditorSceneManager.SaveScene(scene); L("scene saved"); }
            AssetDatabase.SaveAssets();
            return Log.ToString();
        }

        static Material AshMat()
        {
            var m = Vfx("M_VFX_Chip");
            return m ? m : Vfx("M_VFX_Soft");
        }

        /// <summary>M_VFX_HeatShimmer on PF/Heat Shimmer, or null when the shader is missing / has errors (no shimmer then)</summary>
        static Material HazeMat()
        {
            var sh = Shader.Find("PF/Heat Shimmer");
            if (!sh || ShaderUtil.ShaderHasError(sh)) { L(ShaderCheck("PF/Heat Shimmer").TrimEnd()); return null; }
            const string dir = "Assets/_Project/VFX/Materials", p = dir + "/M_VFX_HeatShimmer.mat";
            if (!AssetDatabase.IsValidFolder(dir)) { Directory.CreateDirectory(dir); AssetDatabase.Refresh(); }
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(sh) { name = "M_VFX_HeatShimmer" }; AssetDatabase.CreateAsset(m, p); L("material M_VFX_HeatShimmer"); }
            m.shader = sh;
            EditorUtility.SetDirty(m);
            return m;
        }

        static void PlaceInScene(GameObject prefab)
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != Scene) scene = EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            var existing = Object.FindFirstObjectByType<VolcanoLandmark>();
            if (existing)
            {
                // the first version sat further out: move it only if nobody moved it since
                if ((existing.transform.position - new Vector3(-420f, -2f, 700f)).sqrMagnitude < 1f)
                { existing.transform.position = Place; EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); L($"volcano moved to {Place}"); }
                else L($"volcano kept at {existing.transform.position} ({existing.name})");
                return;
            }
            var world = GameObject.Find("World") ?? GameObject.Find("[World]");
            Transform holder = world ? world.transform.Find("Landmarks") : null;
            if (!holder) { var h = GameObject.Find("Landmarks"); holder = h ? h.transform : new GameObject("Landmarks").transform; if (world && !h) holder.SetParent(world.transform, false); }
            var v = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder);
            v.name = "Volcano"; v.transform.position = Place; v.transform.rotation = Quaternion.identity;
            v.isStatic = false;
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            L($"volcano placed at {Place} under {holder.name} (about {new Vector2(Place.x, Place.z).magnitude:0} m from the island centre)");
        }
    }
}
