using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PrimalFrontier.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// Builds Assets/_Project/Scenes/Island_VerticalSlice.unity from the Blender export:
    /// materials (materials.json) -> model material remap -> prefabs -> Unity Terrain (heightmap + splat + trees + details)
    /// -> placed props (placement.json) -> water, light, sky, fog, markers. Re-runnable; overwrites generated assets.
    /// Menu: Primal Frontier/Build Island Scene. Batch: -executeMethod PrimalFrontier.EditorTools.PrimalWorldBuilder.BuildFromCommandLine
    /// </summary>
    public static class PrimalWorldBuilder
    {
        const string Root = "Assets/_Project";
        const string ModelsDir = Root + "/Art/Models";
        const string TexDir = Root + "/Art/Textures";
        const string MatDir = Root + "/Art/Materials";
        const string TerrainDir = Root + "/Art/Terrain";
        const string DataDir = Root + "/Data/World";
        const string PrefabDir = Root + "/Prefabs";
        const string ScenePath = Root + "/Scenes/Island_VerticalSlice.unity";

        const int HeightRes = 1025;
        const float TerrainSize = 640f;
        const float HeightMin = -10f, HeightMax = 70f;

        static readonly StringBuilder Report = new StringBuilder();
        static int _errors;

        // ------------------------------------------------------------------ JSON DTOs
        [Serializable] class PlacementFile { public int version; public int count; public PlacementItem[] items; }
        [Serializable] class PlacementItem { public string asset; public string kind; public float[] m; }
        [Serializable] class MaterialFile { public MaterialEntry[] materials; }
        [Serializable] class MaterialEntry { public string name; public string texture; public float[] tint; public bool alphaClip; public bool doubleSided; public float[] color; public float smoothness; public float metallic; public float tiling; }
        [Serializable] class MarkerFile { public Marker[] markers; }
        [Serializable] class Marker { public string name; public string group; public float x, y, z, r, yaw; }
        [Serializable] class LayoutMeta { public float ext; public int res; public float pond_water_level; public float cave_floor; }

        [MenuItem("Primal Frontier/Build Island Scene")]
        public static void BuildMenu() => Build();

        public static void BuildFromCommandLine()
        {
            int code = 0;
            try { Build(); if (_errors > 0) code = 2; }
            catch (Exception e) { Debug.LogError("[PrimalWorldBuilder] FAILED: " + e); code = 1; }
            EditorApplication.Exit(code);
        }

        static void Log(string s) { Report.AppendLine(s); Debug.Log("[PrimalWorldBuilder] " + s); }
        static void Err(string s) { _errors++; Report.AppendLine("ERROR: " + s); Debug.LogError("[PrimalWorldBuilder] " + s); }

        public static void Build()
        {
            Report.Clear(); _errors = 0;
            Log("Build started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            AssetDatabase.Refresh();
            EnsureFolders();
            var mats = BuildMaterials();
            RemapModelMaterials();
            var prefabs = BuildPrefabs(mats);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var terrain = BuildTerrain(prefabs);
            PlaceObjects(prefabs, terrain);
            BuildWater(mats);
            BuildLightingAndAtmosphere();
            BuildMarkersAndCamera(terrain);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuild();
            AssetDatabase.SaveAssets();
            Log($"Build finished with {_errors} error(s). Scene: {ScenePath}");
            Directory.CreateDirectory("Documentation");
            File.WriteAllText("Documentation/WorldBuildReport.md", "# World Build Report (auto-generated)\n\n```\n" + Report + "```\n");
        }

        static void EnsureFolders()
        {
            foreach (var p in new[] { MatDir, TerrainDir, PrefabDir + "/Environment", PrefabDir + "/Props", PrefabDir + "/Resources", PrefabDir + "/Shipwreck", Root + "/Scenes" })
                Directory.CreateDirectory(p);
            AssetDatabase.Refresh();
        }

        // ------------------------------------------------------------------ materials
        static Shader LitShader => Shader.Find("Universal Render Pipeline/Lit");

        static Dictionary<string, Material> BuildMaterials()
        {
            var result = new Dictionary<string, Material>();
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(DataDir + "/materials.json");
            if (json == null) { Err("materials.json missing"); return result; }
            var file = JsonUtility.FromJson<MaterialFile>(json.text);
            foreach (var e in file.materials)
            {
                var mat = LoadOrCreateMaterial(e.name);
                if (!string.IsNullOrEmpty(e.texture))
                {
                    var d = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/T_{e.texture}_D.png");
                    var n = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/T_{e.texture}_N.png");
                    var m = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/T_{e.texture}_M.png");
                    if (d == null) Err($"{e.name}: albedo T_{e.texture}_D missing");
                    mat.SetTexture("_BaseMap", d);
                    mat.SetColor("_BaseColor", new Color(e.tint[0], e.tint[1], e.tint[2], 1f).gamma);
                    mat.SetTextureScale("_BaseMap", Vector2.one * Mathf.Max(0.01f, e.tiling));
                    if (n != null) { mat.SetTexture("_BumpMap", n); mat.SetFloat("_BumpScale", 1f); mat.EnableKeyword("_NORMALMAP"); }
                    if (m != null)
                    {
                        mat.SetTexture("_MetallicGlossMap", m); mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                        mat.SetFloat("_Smoothness", 1f);
                        mat.SetTexture("_OcclusionMap", m); mat.SetFloat("_OcclusionStrength", 0.8f); mat.EnableKeyword("_OCCLUSIONMAP");
                    }
                    else { mat.SetFloat("_Smoothness", 0.25f); }
                    mat.SetFloat("_Metallic", 0f);
                }
                else
                {
                    mat.SetColor("_BaseColor", new Color(e.color[0], e.color[1], e.color[2], 1f).gamma);
                    mat.SetFloat("_Smoothness", e.smoothness);
                    mat.SetFloat("_Metallic", e.metallic);
                }
                if (e.alphaClip)
                {
                    mat.SetFloat("_AlphaClip", 1f); mat.SetFloat("_Cutoff", 0.5f); mat.EnableKeyword("_ALPHATEST_ON");
                    mat.renderQueue = (int)RenderQueue.AlphaTest;
                    mat.SetOverrideTag("RenderType", "TransparentCutout");
                }
                if (e.doubleSided) { mat.SetFloat("_Cull", 0f); mat.doubleSidedGI = true; }
                mat.enableInstancing = true;
                EditorUtility.SetDirty(mat);
                result[e.name] = mat;
            }
            // procedural extras (not from Blender)
            result["M_Ocean"] = WaterMaterial("M_Ocean", new Color(0.05f, 0.22f, 0.28f, 0.82f), 0.93f);
            result["M_FreshWater"] = WaterMaterial("M_FreshWater", new Color(0.07f, 0.17f, 0.13f, 0.72f), 0.95f);
            AssetDatabase.SaveAssets();
            Log($"Materials: {result.Count} ({string.Join(", ", result.Keys)})");
            return result;
        }

        static Material LoadOrCreateMaterial(string name)
        {
            string path = $"{MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(LitShader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != LitShader) mat.shader = LitShader;
            return mat;
        }

        static Material WaterMaterial(string name, Color c, float smooth)
        {
            var mat = LoadOrCreateMaterial(name);
            mat.SetFloat("_Surface", 1f); mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetColor("_BaseColor", c);
            mat.SetFloat("_Smoothness", smooth);
            mat.SetFloat("_Metallic", 0f);
            var n = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/T_Sand_N.png");
            if (n != null) { mat.SetTexture("_BumpMap", n); mat.SetFloat("_BumpScale", 0.15f); mat.EnableKeyword("_NORMALMAP"); mat.SetTextureScale("_BaseMap", new Vector2(0.5f, 0.5f)); }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static IEnumerable<string> ModelPaths() =>
            AssetDatabase.FindAssets("t:Model", new[] { ModelsDir }).Select(AssetDatabase.GUIDToAssetPath);

        static void RemapModelMaterials()
        {
            int n = 0;
            foreach (var path in ModelPaths())
            {
                var mi = AssetImporter.GetAtPath(path) as ModelImporter;
                if (mi == null) continue;
                mi.SearchAndRemapMaterials(ModelImporterMaterialName.BasedOnMaterialName, ModelImporterMaterialSearch.Everywhere);
                mi.SaveAndReimport();
                n++;
            }
            Log($"Remapped materials on {n} models");
        }

        // ------------------------------------------------------------------ prefabs
        enum ColliderKind { None, Mesh, Box, Capsule, TreeCapsule }

        static string Category(string asset)
        {
            if (asset.StartsWith("ENV_Tree") || asset.StartsWith("ENV_Fern") || asset.StartsWith("ENV_Bush") || asset.StartsWith("ENV_Grass") || asset.StartsWith("ENV_FallenLog")) return "Environment";
            if (asset.StartsWith("ENV_")) return "Environment";
            if (asset.StartsWith("PROP_Ship_Crate") || asset.StartsWith("PROP_Ship_Barrel") || asset.StartsWith("PROP_Ship_Debris")) return "Props";
            if (asset.StartsWith("PROP_Ship_")) return "Shipwreck";
            if (asset.StartsWith("RES_")) return "Resources";
            return "Environment";
        }

        static ColliderKind ColliderFor(string asset)
        {
            if (asset.StartsWith("ENV_Tree")) return ColliderKind.TreeCapsule;
            if (asset is "PROP_Ship_Sail" or "PROP_Ship_Rope" || asset.StartsWith("ENV_Fern") || asset.StartsWith("ENV_Grass") || asset.StartsWith("ENV_Bush")) return ColliderKind.None;
            if (asset.StartsWith("PROP_Ship_Crate") || asset.StartsWith("PROP_Ship_Debris") || asset.StartsWith("RES_")) return ColliderKind.Box;
            if (asset.StartsWith("PROP_Ship_Barrel")) return ColliderKind.Capsule;
            return ColliderKind.Mesh;
        }

        static Dictionary<string, GameObject> BuildPrefabs(Dictionary<string, Material> mats)
        {
            var result = new Dictionary<string, GameObject>();
            foreach (var path in ModelPaths())
            {
                string asset = Path.GetFileNameWithoutExtension(path);
                if (path.Contains("/Water/")) continue;
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null) { Err("cannot load model " + path); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
                go.name = asset;
                AddCollider(go, ColliderFor(asset));
                string dir = $"{PrefabDir}/{Category(asset)}";
                var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{dir}/PFB_{asset}.prefab");
                UnityEngine.Object.DestroyImmediate(go);
                result[asset] = prefab;
                // detail-only single mesh prefab (LOD0 mesh at the root) for terrain details
                if (asset is "ENV_Fern_01" or "ENV_Grass_01" or "ENV_Bush_01")
                    result["DET_" + asset] = BuildDetailPrefab(model, asset);
            }
            // report a few orientation checks
            foreach (var key in new[] { "ENV_Tree_01", "PROP_Ship_Hull", "ENV_Cave_Entrance" })
            {
                if (!result.TryGetValue(key, out var p)) { Err("prefab missing: " + key); continue; }
                var b = Bounds(p);
                Log($"Orientation check {key}: root rot={p.transform.localRotation.eulerAngles} bounds size={b.size} center={b.center}");
            }
            Log($"Prefabs: {result.Count}");
            return result;
        }

        static GameObject BuildDetailPrefab(GameObject model, string asset)
        {
            var mf = model.GetComponentsInChildren<MeshFilter>(true).OrderBy(f => f.name.EndsWith("_LOD0") ? 0 : 1).First();
            var mr = mf.GetComponent<MeshRenderer>();
            var go = new GameObject("DET_" + asset);
            go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mr.sharedMaterials;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/Environment/DET_{asset}.prefab");
            UnityEngine.Object.DestroyImmediate(go);
            return prefab;
        }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds();
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static void AddCollider(GameObject go, ColliderKind kind)
        {
            switch (kind)
            {
                case ColliderKind.Mesh:
                    foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (mf.name.EndsWith("_LOD1")) continue;
                        var mc = mf.gameObject.AddComponent<MeshCollider>();
                        mc.sharedMesh = mf.sharedMesh;
                    }
                    break;
                case ColliderKind.Box:
                {
                    var b = Bounds(go);
                    var bc = go.AddComponent<BoxCollider>();
                    bc.center = go.transform.InverseTransformPoint(b.center); bc.size = b.size;
                    break;
                }
                case ColliderKind.Capsule:
                {
                    var b = Bounds(go);
                    var cc = go.AddComponent<CapsuleCollider>();
                    cc.center = go.transform.InverseTransformPoint(b.center); cc.height = b.size.y; cc.radius = Mathf.Max(b.size.x, b.size.z) * 0.5f;
                    break;
                }
                case ColliderKind.TreeCapsule:
                {
                    var cc = go.AddComponent<CapsuleCollider>();
                    cc.center = new Vector3(0, 3f, 0); cc.height = 6f; cc.radius = 0.45f;
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ terrain
        static readonly (string tex, float tile)[] Layers =
        {
            ("Sand", 4f), ("Grass", 6f), ("ForestFloor", 5f), ("Rock", 8f), ("Mud", 4f), ("SandWet", 4f)
        };

        static Terrain BuildTerrain(Dictionary<string, GameObject> prefabs)
        {
            // Create the asset FIRST so alphamap textures are persisted as sub-assets (otherwise splat weights are lost on reload).
            AssetDatabase.DeleteAsset(TerrainDir + "/TD_Island.asset");
            var td = new TerrainData { heightmapResolution = HeightRes };
            td.size = new Vector3(TerrainSize, HeightMax - HeightMin, TerrainSize);
            AssetDatabase.CreateAsset(td, TerrainDir + "/TD_Island.asset");
            // heights (.r16 as .bytes, little-endian, rows = Unity z)
            var raw = AssetDatabase.LoadAssetAtPath<TextAsset>(TerrainDir + "/ENV_Island_Height_1025.bytes");
            if (raw == null) throw new Exception("heightmap missing");
            var bytes = raw.bytes;
            if (bytes.Length != HeightRes * HeightRes * 2) throw new Exception($"heightmap size {bytes.Length} != {HeightRes * HeightRes * 2}");
            var h = new float[HeightRes, HeightRes];
            for (int z = 0; z < HeightRes; z++)
                for (int x = 0; x < HeightRes; x++)
                {
                    int i = (z * HeightRes + x) * 2;
                    h[z, x] = (bytes[i] | (bytes[i + 1] << 8)) / 65535f;
                }
            td.SetHeights(0, 0, h);

            // layers
            var tls = new TerrainLayer[Layers.Length];
            for (int i = 0; i < Layers.Length; i++)
            {
                string p = $"{TerrainDir}/TL_{Layers[i].tex}.terrainlayer";
                var tl = AssetDatabase.LoadAssetAtPath<TerrainLayer>(p);
                if (tl == null) { tl = new TerrainLayer(); AssetDatabase.CreateAsset(tl, p); }
                tl.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/T_{Layers[i].tex}_D.png");
                tl.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/T_{Layers[i].tex}_N.png");
                tl.maskMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/T_{Layers[i].tex}_M.png");
                tl.tileSize = Vector2.one * Layers[i].tile;
                tl.normalScale = 1f;
                EditorUtility.SetDirty(tl);
                tls[i] = tl;
            }
            td.terrainLayers = tls;

            // splat maps (read PNG bytes exactly; PNG row 0 = Unity z 0, GetPixel y is bottom-up)
            const int aRes = 1024;
            td.alphamapResolution = aRes;
            td.baseMapResolution = 1024;
            var s0 = LoadPng(TerrainDir + "/ENV_Island_Splat0.png");
            var s1 = LoadPng(TerrainDir + "/ENV_Island_Splat1.png");
            var alpha = new float[aRes, aRes, Layers.Length];
            for (int z = 0; z < aRes; z++)
                for (int x = 0; x < aRes; x++)
                {
                    int sx = Mathf.RoundToInt(x / (float)(aRes - 1) * (s0.width - 1));
                    int sz = Mathf.RoundToInt(z / (float)(aRes - 1) * (s0.height - 1));
                    Color a = s0.GetPixel(sx, s0.height - 1 - sz);
                    Color b = s1.GetPixel(sx, s1.height - 1 - sz);
                    float[] w = { a.r, a.g, a.b, a.a, b.r, b.g };
                    float sum = w.Sum();
                    if (sum < 1e-4f) { w[1] = 1f; sum = 1f; }
                    for (int l = 0; l < w.Length; l++) alpha[z, x, l] = w[l] / sum;
                }
            td.SetAlphamaps(0, 0, alpha);

            // trees
            var protoNames = new[] { "ENV_Tree_01", "ENV_Tree_02", "ENV_Tree_03", "ENV_Tree_04" };
            var protos = new List<TreePrototype>();
            foreach (var n in protoNames)
            {
                if (!prefabs.TryGetValue(n, out var pf)) { Err("tree prefab missing " + n); continue; }
                protos.Add(new TreePrototype { prefab = pf, bendFactor = 0f });
            }
            td.treePrototypes = protos.ToArray();

            // details
            td.SetDetailResolution(512, 32);
            var detailNames = new[] { ("DET_ENV_Fern_01", "ENV_Detail_Fern.png", 1.0f, 0.8f, 1.3f), ("DET_ENV_Grass_01", "ENV_Detail_Grass.png", 2.0f, 0.7f, 1.2f), ("DET_ENV_Bush_01", "ENV_Detail_Bush.png", 0.35f, 0.8f, 1.2f) };
            var dprotos = new List<DetailPrototype>();
            foreach (var (pfName, _, _, minS, maxS) in detailNames)
            {
                prefabs.TryGetValue(pfName, out var pf);
                if (pf == null) { Err("detail prefab missing " + pfName); continue; }
                dprotos.Add(new DetailPrototype
                {
                    prototype = pf, usePrototypeMesh = true, renderMode = DetailRenderMode.VertexLit, useInstancing = true,
                    minWidth = minS, maxWidth = maxS, minHeight = minS, maxHeight = maxS, noiseSpread = 0.3f,
                    healthyColor = Color.white, dryColor = new Color(0.9f, 0.88f, 0.78f), alignToGround = 0.6f, positionJitter = 1f
                });
            }
            td.detailPrototypes = dprotos.ToArray();
            var rng = new System.Random(42);
            for (int l = 0; l < detailNames.Length && l < dprotos.Count; l++)
            {
                var map = LoadPng(TerrainDir + "/" + detailNames[l].Item2);
                var layer = new int[512, 512];
                float k = detailNames[l].Item3;
                long total = 0;
                for (int z = 0; z < 512; z++)
                    for (int x = 0; x < 512; x++)
                    {
                        int sx = x * (map.width - 1) / 511, sz = z * (map.height - 1) / 511;
                        float d = map.GetPixel(sx, map.height - 1 - sz).r * k;
                        int c = (int)d; if (rng.NextDouble() < d - c) c++;
                        layer[z, x] = c; total += c;
                    }
                td.SetDetailLayer(0, 0, l, layer);
                Log($"Detail layer {detailNames[l].Item1}: {total} instances");
            }

            EditorUtility.SetDirty(td);
            AssetDatabase.SaveAssets();
            Log($"TerrainData sub-assets: alphamap textures {td.alphamapTextureCount}");
            var go = Terrain.CreateTerrainGameObject(td);
            go.name = "ENV_Island_Terrain";
            go.transform.position = new Vector3(-TerrainSize / 2f, HeightMin, -TerrainSize / 2f);
            var t = go.GetComponent<Terrain>();
            t.drawInstanced = true;
            t.heightmapPixelError = 4f;
            t.basemapDistance = 300f;
            t.treeDistance = 450f;
            t.treeBillboardDistance = 450f;
            t.treeMaximumFullLODCount = 200;
            t.detailObjectDistance = 90f;
            t.detailObjectDensity = 1f;
            t.shadowCastingMode = ShadowCastingMode.On;
            var terrainShader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (terrainShader != null)
            {
                var tm = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/M_Terrain.mat");
                if (tm == null) { tm = new Material(terrainShader); AssetDatabase.CreateAsset(tm, MatDir + "/M_Terrain.mat"); }
                tm.EnableKeyword("_MASKMAP"); tm.EnableKeyword("_NORMALMAP");
                t.materialTemplate = tm;
            }
            else Err("URP Terrain/Lit shader not found");
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            Log($"Terrain: {HeightRes}^2 heights, size {td.size}, layers {tls.Length}, pos {go.transform.position}");
            return t;
        }

        static Texture2D LoadPng(string assetPath)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(assetPath), false)) throw new Exception("cannot decode " + assetPath);
            return tex;
        }

        // ------------------------------------------------------------------ placement
        static float[][] Rows(float[] m) => new[] { new[] { m[0], m[1], m[2], m[3] }, new[] { m[4], m[5], m[6], m[7] }, new[] { m[8], m[9], m[10], m[11] }, new[] { m[12], m[13], m[14], m[15] } };

        static string GroupFor(string asset)
        {
            if (asset.StartsWith("ENV_Rock")) return "Rocks";
            if (asset.StartsWith("ENV_Cliff") || asset.StartsWith("ENV_Cave")) return "Cliffs";
            if (asset.StartsWith("ENV_FallenLog")) return "Vegetation";
            return Category(asset);
        }

        static void PlaceObjects(Dictionary<string, GameObject> prefabs, Terrain terrain)
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(DataDir + "/placement.json");
            if (json == null) { Err("placement.json missing"); return; }
            var file = JsonUtility.FromJson<PlacementFile>(json.text);
            var world = new GameObject("World").transform;
            var groups = new Dictionary<string, Transform>();
            var trees = new List<TreeInstance>();
            var protoIndex = new Dictionary<string, int> { { "ENV_Tree_01", 0 }, { "ENV_Tree_02", 1 }, { "ENV_Tree_03", 2 }, { "ENV_Tree_04", 3 } };
            var tpos = terrain.transform.position; var tsize = terrain.terrainData.size;
            double errSum = 0; float errMax = 0; int errN = 0; int placed = 0;
            foreach (var it in file.items)
            {
                var rows = Rows(it.m);
                BlenderSpace.Decompose(rows, out var pos, out var rot, out var scl);
                if (it.kind == "tree")
                {
                    if (!protoIndex.TryGetValue(it.asset, out int pi)) { Err("unknown tree " + it.asset); continue; }
                    // verification: Blender placed trees at terrain height - 0.15
                    float th = terrain.SampleHeight(pos) + tpos.y;
                    float e = Mathf.Abs(th - (pos.y + 0.15f)); errSum += e; errMax = Mathf.Max(errMax, e); errN++;
                    trees.Add(new TreeInstance
                    {
                        prototypeIndex = pi,
                        position = new Vector3((pos.x - tpos.x) / tsize.x, (pos.y - tpos.y) / tsize.y, (pos.z - tpos.z) / tsize.z),
                        rotation = BlenderSpace.YawRadians(rows), widthScale = scl.x, heightScale = scl.y,
                        color = Color.white, lightmapColor = Color.white
                    });
                    continue;
                }
                if (!prefabs.TryGetValue(it.asset, out var pf)) { Err("prefab missing for placement " + it.asset); continue; }
                string g = GroupFor(it.asset);
                if (!groups.TryGetValue(g, out var parent)) { parent = new GameObject(g).transform; parent.SetParent(world); groups[g] = parent; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(pf, parent);
                go.transform.SetPositionAndRotation(pos, rot);
                go.transform.localScale = scl;
                var flags = StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI;
                if (g is "Rocks" or "Cliffs" or "Shipwreck") flags |= StaticEditorFlags.OccluderStatic;
                if (g != "Resources") GameObjectUtility.SetStaticEditorFlags(go, flags);
                placed++;
            }
            terrain.terrainData.SetTreeInstances(trees.ToArray(), true);
            terrain.Flush();
            Log($"Placed {placed} objects in {groups.Count} groups ({string.Join(", ", groups.Select(kv => kv.Key + ":" + kv.Value.childCount))}); trees {trees.Count}");
            if (errN > 0)
            {
                float mean = (float)(errSum / errN);
                Log($"VERIFY terrain/placement mapping: mean |dh| {mean:F3} m, max {errMax:F3} m over {errN} trees");
                if (mean > 0.35f) Err("terrain vs placement height mismatch: axis mapping or heightmap orientation is wrong");
            }
            // wreck sanity: hull must sit near the shoreline height
            var hull = groups.TryGetValue("Shipwreck", out var sw) ? sw.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name.StartsWith("PFB_PROP_Ship_Hull") || t.name.StartsWith("PROP_Ship_Hull")) : null;
            if (hull != null) Log($"Hull at {hull.position}, terrain there {terrain.SampleHeight(hull.position) + tpos.y:F2}");
        }

        // ------------------------------------------------------------------ water, light, markers
        static void BuildWater(Dictionary<string, Material> mats)
        {
            var parent = new GameObject("Water").transform;
            var ocean = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ocean.name = "ENV_Ocean"; ocean.transform.SetParent(parent);
            ocean.transform.position = new Vector3(0, 0f, 0); ocean.transform.localScale = new Vector3(200, 1, 200);
            UnityEngine.Object.DestroyImmediate(ocean.GetComponent<Collider>());
            ocean.GetComponent<MeshRenderer>().sharedMaterial = mats["M_Ocean"];
            ocean.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            foreach (var n in new[] { "ENV_Pond_Water", "ENV_Stream_Water" })
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelsDir}/Water/{n}.fbx");
                if (model == null) { Err("water model missing " + n); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
                go.name = n;
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>()) { r.sharedMaterial = mats["M_FreshWater"]; r.shadowCastingMode = ShadowCastingMode.Off; }
            }
            Log("Water: ocean plane (y=0, 2 km), pond + stream meshes");
        }

        static void BuildLightingAndAtmosphere()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.93f, 0.82f);
            sun.intensity = 1.6f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(38f, -135f, 0f);
            RenderSettings.sun = sun;
            var sky = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/M_Sky.mat");
            if (sky == null) { sky = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(sky, MatDir + "/M_Sky.mat"); }
            sky.SetFloat("_SunSize", 0.035f); sky.SetFloat("_AtmosphereThickness", 0.95f);
            sky.SetColor("_SkyTint", new Color(0.52f, 0.58f, 0.66f)); sky.SetColor("_GroundColor", new Color(0.33f, 0.31f, 0.28f));
            sky.SetFloat("_Exposure", 1.15f);
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0016f;
            RenderSettings.fogColor = new Color(0.68f, 0.74f, 0.78f);
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/SampleSceneProfile.asset");
            if (profile != null)
            {
                var vol = new GameObject("Global Volume").AddComponent<Volume>();
                vol.isGlobal = true; vol.sharedProfile = profile;
            }
            Log("Lighting: directional sun, procedural sky, exp^2 fog, global volume");
        }

        static void BuildMarkersAndCamera(Terrain terrain)
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(DataDir + "/markers.json");
            var root = new GameObject("Markers").transform;
            Vector3 spawn = Vector3.zero, wreck = Vector3.zero;
            if (json != null)
            {
                var file = JsonUtility.FromJson<MarkerFile>(json.text);
                var groups = new Dictionary<string, Transform>();
                foreach (var m in file.markers)
                {
                    if (!groups.TryGetValue(m.group, out var g)) { g = new GameObject(m.group).transform; g.SetParent(root); groups[m.group] = g; }
                    var go = new GameObject(m.name);
                    go.transform.SetParent(g);
                    go.transform.position = BlenderSpace.ToUnityPosition(m.x, m.y, m.z);
                    go.transform.rotation = Quaternion.Euler(0, -m.yaw, 0);
                    if (m.name == "ZONE_PlayerSpawn") spawn = go.transform.position;
                    if (m.name == "ZONE_Shipwreck") wreck = go.transform.position;
                }
                Log($"Markers: {file.markers.Length}");
            }
            else Err("markers.json missing");
            var cam = new GameObject("Main Camera");
            cam.tag = "MainCamera";
            var c = cam.AddComponent<Camera>();
            c.nearClipPlane = 0.1f; c.farClipPlane = 1500f;
            cam.AddComponent<AudioListener>();
            var eye = spawn + Vector3.up * 1.7f;
            cam.transform.position = eye;
            var look = wreck + Vector3.up * 1.5f - eye;
            if (look.sqrMagnitude > 0.01f) cam.transform.rotation = Quaternion.LookRotation(look.normalized);
            Log($"Camera at spawn {eye}, looking at wreck {wreck}");
        }

        static void AddSceneToBuild()
        {
            var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
