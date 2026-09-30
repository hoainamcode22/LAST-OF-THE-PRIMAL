using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// Phase 2 art kit (agent ART / A2): landmarks, Deep Water Cave kit and Phase 2 props made in Blender
    /// (E:\Model game khủng long\scripts\phase2\*.py). The Blender side writes Art/Environment/Models/Phase2/P2_Manifest.json
    /// next to the FBX files; this builder is data driven from it and idempotent:
    ///   Build [names]  import settings (models + textures), URP Lit materials (Art/Environment/Materials/Phase2),
    ///                  prefabs (Prefabs/Environment/Phase2) with LODGroup, MeshCollider from the *_COL mesh, static flags.
    ///                  Optional arg: comma separated asset names (empty = all in the manifest).
    ///   Check          missing references (prefab, meshes, materials, textures, collider) and triangles per LOD.
    /// Scene content is not touched (zone agents place the prefabs).
    /// </summary>
    public static class PrimalPhase2ArtBuilder
    {
        const string ModelDir = "Assets/_Project/Art/Environment/Models/Phase2";
        const string TexDir = "Assets/_Project/Art/Environment/Textures/Phase2";
        const string MatDir = "Assets/_Project/Art/Environment/Materials/Phase2";
        const string PrefabDir = "Assets/_Project/Prefabs/Environment/Phase2";
        const string ManifestPath = ModelDir + "/P2_Manifest.json";
        const string LogPath = "Documentation/Phase2/Art/a2_build.txt";

        [Serializable] class MatDef { public string name, tex, kind, detailNormal; public bool cutout, doubleSided; public float tiling = 1f, detailTiling = 1f, smoothScale = 1f; public float[] tint; }
        [Serializable] class AssetDef { public string name, kind, zone, fbx, col, material, pivot, notes; public string[] lods, materials; public int[] tris; public int colTris; public float[] lodHeights; public float sizeX, sizeY, sizeZ, minZ, maxZ; }
        [Serializable] class Manifest { public int version; public MatDef[] materials; public AssetDef[] assets; }

        static readonly StringBuilder Log = new StringBuilder();
        static int _warn;
        static void L(string s) { Log.AppendLine(s); Debug.Log("[PrimalP2Art] " + s); }
        static void W(string s) { _warn++; Log.AppendLine("WARNING: " + s); Debug.LogWarning("[PrimalP2Art] " + s); }

        static Manifest Load()
        {
            if (!File.Exists(ManifestPath)) throw new Exception("manifest missing: " + ManifestPath);
            var m = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            m.materials = m.materials ?? new MatDef[0]; m.assets = m.assets ?? new AssetDef[0];
            return m;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // ------------------------------------------------------------------ textures
        static Texture2D Tex(string file, bool normal, bool linear, bool cutout, int maxSize)
        {
            string path = $"{TexDir}/{file}.png";
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!ti) { W("missing texture " + path); return null; }
            bool dirty = false;
            void Set<T>(T now, T want, Action<T> apply) { if (!EqualityComparer<T>.Default.Equals(now, want)) { apply(want); dirty = true; } }
            Set(ti.textureType, normal ? TextureImporterType.NormalMap : TextureImporterType.Default, v => ti.textureType = v);
            Set(ti.sRGBTexture, !linear && !normal, v => ti.sRGBTexture = v);
            Set(ti.mipmapEnabled, true, v => ti.mipmapEnabled = v);
            Set(ti.maxTextureSize, maxSize, v => ti.maxTextureSize = v);
            Set(ti.anisoLevel, 4, v => ti.anisoLevel = v);
            Set(ti.textureCompression, TextureImporterCompression.CompressedHQ, v => ti.textureCompression = v);
            Set(ti.alphaSource, (linear && !normal) || cutout ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None, v => ti.alphaSource = v);
            Set(ti.alphaIsTransparency, cutout, v => ti.alphaIsTransparency = v);
            Set(ti.mipMapsPreserveCoverage, cutout, v => ti.mipMapsPreserveCoverage = v);
            if (cutout) Set(ti.alphaTestReferenceValue, 0.5f, v => ti.alphaTestReferenceValue = v);
            Set(ti.wrapMode, TextureWrapMode.Repeat, v => ti.wrapMode = v);
            if (dirty) ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static int TexSize(string file)
        {
            string p = $"{TexDir}/{file}.png";
            var ti = AssetImporter.GetAtPath(p) as TextureImporter;
            if (!ti) return 2048;
            ti.GetSourceTextureWidthAndHeight(out int w, out int h);
            return Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(w, h)), 256, 2048);
        }

        // ------------------------------------------------------------------ materials (URP Lit)
        static Material MakeMaterial(MatDef d)
        {
            EnsureFolder(MatDir);
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            string p = $"{MatDir}/{d.name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(sh) { name = d.name }; AssetDatabase.CreateAsset(m, p); }
            else if (m.shader != sh) m.shader = sh;
            var alb = Tex(d.tex + "_D", false, false, d.cutout, TexSize(d.tex + "_D"));
            var nrm = Tex(d.tex + "_N", true, true, false, TexSize(d.tex + "_N"));
            var msk = Tex(d.tex + "_M", false, true, false, TexSize(d.tex + "_M"));
            m.SetTexture("_BaseMap", alb);
            var tint = d.tint != null && d.tint.Length >= 3 ? new Color(d.tint[0], d.tint[1], d.tint[2], 1f) : Color.white;
            m.SetColor("_BaseColor", tint);
            m.SetTextureScale("_BaseMap", Vector2.one * Mathf.Max(0.01f, d.tiling));
            m.SetTexture("_BumpMap", nrm); m.SetFloat("_BumpScale", 1f);
            if (nrm) m.EnableKeyword("_NORMALMAP"); else m.DisableKeyword("_NORMALMAP");
            m.SetFloat("_WorkflowMode", 1f); m.SetFloat("_Metallic", 0f);
            m.SetTexture("_MetallicGlossMap", msk);
            if (msk) m.EnableKeyword("_METALLICSPECGLOSSMAP"); else m.DisableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_SmoothnessTextureChannel", 0f); m.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            m.SetFloat("_Smoothness", Mathf.Clamp01(d.smoothScale));
            m.SetTexture("_OcclusionMap", msk); m.SetFloat("_OcclusionStrength", 1f);
            if (msk) m.EnableKeyword("_OCCLUSIONMAP"); else m.DisableKeyword("_OCCLUSIONMAP");
            // detail normal (tileable, close-up breakup on the large baked landmarks)
            Texture2D det = string.IsNullOrEmpty(d.detailNormal) ? null : Tex(d.detailNormal, true, true, false, 1024);
            m.SetTexture("_DetailNormalMap", det);
            if (det)
            {
                m.EnableKeyword("_DETAIL_MULX2"); m.SetFloat("_DetailNormalMapScale", 0.6f); m.SetFloat("_DetailAlbedoMapScale", 1f);
                m.SetTextureScale("_DetailAlbedoMap", Vector2.one * Mathf.Max(0.01f, d.detailTiling));
            }
            else m.DisableKeyword("_DETAIL_MULX2");
            if (d.cutout)
            {
                m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.5f); m.EnableKeyword("_ALPHATEST_ON");
                m.renderQueue = (int)RenderQueue.AlphaTest; m.SetOverrideTag("RenderType", "TransparentCutout");
            }
            else
            {
                m.SetFloat("_AlphaClip", 0f); m.DisableKeyword("_ALPHATEST_ON");
                m.renderQueue = -1; m.SetOverrideTag("RenderType", "Opaque");
            }
            m.SetFloat("_Cull", d.doubleSided ? 0f : 2f); m.doubleSidedGI = d.doubleSided;
            m.SetFloat("_Surface", 0f); m.SetFloat("_ReceiveShadows", 1f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ models
        static GameObject ImportModel(AssetDef a, Dictionary<string, Material> mats)
        {
            string path = $"{ModelDir}/{a.fbx}";
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (!mi) { W("missing model " + path); return null; }
            bool dirty = false;
            void Set<T>(T now, T want, Action<T> apply) { if (!EqualityComparer<T>.Default.Equals(now, want)) { apply(want); dirty = true; } }
            Set(mi.globalScale, 1f, v => mi.globalScale = v);
            Set(mi.useFileScale, true, v => mi.useFileScale = v);
            Set(mi.importAnimation, false, v => mi.importAnimation = v);
            Set(mi.animationType, ModelImporterAnimationType.None, v => mi.animationType = v);
            Set(mi.importCameras, false, v => mi.importCameras = v);
            Set(mi.importLights, false, v => mi.importLights = v);
            Set(mi.importVisibility, false, v => mi.importVisibility = v);
            Set(mi.importBlendShapes, false, v => mi.importBlendShapes = v);
            Set(mi.importNormals, ModelImporterNormals.Import, v => mi.importNormals = v);
            Set(mi.importTangents, ModelImporterTangents.CalculateMikk, v => mi.importTangents = v);
            Set(mi.materialImportMode, ModelImporterMaterialImportMode.ImportStandard, v => mi.materialImportMode = v);
            Set(mi.isReadable, false, v => mi.isReadable = v);
            Set(mi.meshCompression, ModelImporterMeshCompression.Off, v => mi.meshCompression = v);
            Set(mi.addCollider, false, v => mi.addCollider = v);
            Set(mi.generateSecondaryUV, false, v => mi.generateSecondaryUV = v);
            var existing = mi.GetExternalObjectMap();
            foreach (var kv in mats)
            {
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key);
                if (!existing.TryGetValue(id, out var cur) || cur != kv.Value) { mi.AddRemap(id, kv.Value); dirty = true; }
            }
            if (dirty) mi.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var r = FindDeep(c, name); if (r) return r; }
            return null;
        }

        static GameObject MakePrefab(AssetDef a, GameObject model, Dictionary<string, Material> mats)
        {
            EnsureFolder(PrefabDir);
            var root = new GameObject(a.name);
            try
            {
                var lodRenderers = new List<Renderer>();
                bool prop = a.kind == "prop";
                for (int i = 0; i < a.lods.Length; i++)
                {
                    var src = FindDeep(model.transform, a.lods[i]);
                    var smf = src ? src.GetComponent<MeshFilter>() : null;
                    var smr = src ? src.GetComponent<MeshRenderer>() : null;
                    if (!smf || !smf.sharedMesh) { W($"{a.name}: missing mesh {a.lods[i]}"); continue; }
                    var go = new GameObject(a.lods[i]);
                    go.transform.SetParent(root.transform, false);
                    go.transform.localPosition = src.localPosition; go.transform.localRotation = src.localRotation; go.transform.localScale = src.localScale;
                    go.AddComponent<MeshFilter>().sharedMesh = smf.sharedMesh;
                    var r = go.AddComponent<MeshRenderer>();
                    var ms = smr ? smr.sharedMaterials : new Material[0];
                    for (int k = 0; k < ms.Length; k++) if (ms[k] && mats.TryGetValue(ms[k].name, out var mm)) ms[k] = mm;
                    if (ms.Length == 0 && mats.TryGetValue(a.material ?? "", out var def)) ms = new[] { def };
                    r.sharedMaterials = ms;
                    r.shadowCastingMode = prop && i == a.lods.Length - 1 && a.lods.Length > 1 ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    r.receiveShadows = true;
                    lodRenderers.Add(r);
                }
                if (lodRenderers.Count > 1)
                {
                    var lg = root.AddComponent<LODGroup>();
                    var lods = new LOD[lodRenderers.Count];
                    for (int i = 0; i < lods.Length; i++)
                    {
                        float h = a.lodHeights != null && i < a.lodHeights.Length ? a.lodHeights[i] : Mathf.Pow(0.3f, i + 1);
                        if (i > 0) h = Mathf.Min(h, lods[i - 1].screenRelativeTransitionHeight * 0.9f);
                        lods[i] = new LOD(h, new[] { lodRenderers[i] });
                    }
                    lg.SetLODs(lods); lg.fadeMode = LODFadeMode.None; lg.RecalculateBounds();
                }
                if (!string.IsNullOrEmpty(a.col))
                {
                    var c = FindDeep(model.transform, a.col);
                    var cmf = c ? c.GetComponent<MeshFilter>() : null;
                    if (cmf && cmf.sharedMesh) root.AddComponent<MeshCollider>().sharedMesh = cmf.sharedMesh;
                    else W($"{a.name}: missing collider mesh {a.col}");
                }
                var flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
                if (a.kind == "landmark" || a.kind == "cave") flags |= StaticEditorFlags.OccluderStatic;
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
                return PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{a.name}.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static int Tris(Mesh m)
        {
            if (!m) return 0;
            int t = 0; for (int s = 0; s < m.subMeshCount; s++) t += (int)m.GetIndexCount(s) / 3; return t;
        }

        // ------------------------------------------------------------------ commands
        [PrimalBridgeCommand]
        public static string Build(string arg)
        {
            Log.Clear(); _warn = 0;
            L($"Build {arg} started {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            AssetDatabase.Refresh();
            var man = Load();
            var only = string.IsNullOrEmpty(arg) ? null : new HashSet<string>(arg.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0));
            var mats = new Dictionary<string, Material>();
            var needMats = new HashSet<string>(man.assets.Where(a => only == null || only.Contains(a.name)).SelectMany(a => a.materials ?? new string[0]));
            foreach (var d in man.materials)
            {
                if (!needMats.Contains(d.name)) { var ex = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{d.name}.mat"); if (ex) mats[d.name] = ex; continue; }
                mats[d.name] = MakeMaterial(d); L($"material {d.name} ({d.tex})");
            }
            AssetDatabase.SaveAssets();
            int n = 0;
            foreach (var a in man.assets)
            {
                if (only != null && !only.Contains(a.name)) continue;
                var model = ImportModel(a, mats);
                if (!model) continue;
                var p = MakePrefab(a, model, mats);
                if (p) { n++; L($"prefab {PrefabDir}/{a.name}.prefab  zone={a.zone}  size {a.sizeX}x{a.sizeY}x{a.sizeZ} m  tris {string.Join("/", a.tris)}  col {a.colTris}"); }
            }
            AssetDatabase.SaveAssets();
            L($"Build finished: {n} prefab(s), {_warn} warning(s)");
            try { Directory.CreateDirectory(Path.GetDirectoryName(LogPath)); File.AppendAllText(LogPath, Log + "\n"); } catch (Exception e) { Debug.LogWarning(e.Message); }
            return Log.ToString();
        }

        [PrimalBridgeCommand]
        public static string Check(string arg)
        {
            var sb = new StringBuilder(); int missing = 0;
            void Miss(string s) { missing++; sb.AppendLine("MISSING " + s); }
            var man = Load();
            foreach (var a in man.assets)
            {
                if (!string.IsNullOrEmpty(arg) && !arg.Split(',').Contains(a.name)) continue;
                string pp = $"{PrefabDir}/{a.name}.prefab";
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>(pp);
                if (!pf) { Miss(pp); continue; }
                var tris = new List<int>();
                var lg = pf.GetComponent<LODGroup>();
                var rends = lg ? lg.GetLODs().SelectMany(l => l.renderers).ToArray() : pf.GetComponentsInChildren<Renderer>(true);
                if (lg && lg.lodCount != a.lods.Length) Miss($"{a.name}: LODGroup has {lg.lodCount} LODs, manifest {a.lods.Length}");
                Bounds b = new Bounds(); bool first = true;
                foreach (var r in rends)
                {
                    if (!r) { Miss($"{a.name}: null renderer in LODGroup"); continue; }
                    var mf = r.GetComponent<MeshFilter>();
                    if (!mf || !mf.sharedMesh) { Miss($"{a.name}/{r.name}: mesh"); continue; }
                    tris.Add(Tris(mf.sharedMesh));
                    if (first) { b = mf.sharedMesh.bounds; first = false; }
                    foreach (var m in r.sharedMaterials)
                    {
                        if (!m) { Miss($"{a.name}/{r.name}: material slot"); continue; }
                        if (!m.GetTexture("_BaseMap")) Miss($"{a.name}: {m.name} _BaseMap");
                        if (!m.GetTexture("_BumpMap")) Miss($"{a.name}: {m.name} _BumpMap");
                        if (!m.GetTexture("_MetallicGlossMap")) Miss($"{a.name}: {m.name} _MetallicGlossMap");
                        if (m.shader == null || m.shader.name != "Universal Render Pipeline/Lit") Miss($"{a.name}: {m.name} shader");
                    }
                }
                var mc = pf.GetComponent<MeshCollider>();
                if (!string.IsNullOrEmpty(a.col) && (!mc || !mc.sharedMesh)) Miss($"{a.name}: MeshCollider {a.col}");
                sb.AppendLine($"{a.name}: tris/LOD {string.Join("/", tris)}, col {(mc && mc.sharedMesh ? Tris(mc.sharedMesh) : 0)}, " +
                              $"LOD0 bounds centre ({b.center.x:0.#}, {b.center.y:0.#}, {b.center.z:0.#}) size ({b.size.x:0.#}, {b.size.y:0.#}, {b.size.z:0.#}) m, static {GameObjectUtility.GetStaticEditorFlags(pf)}");
            }
            sb.Insert(0, $"Phase2 art check: {man.assets.Length} asset(s), {missing} missing ref(s)\n");
            return sb.ToString();
        }
    }
}
