using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using PrimalFrontier.Core;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// ENV (PC phase): the island's environment pass, deeper not bigger. Idempotent bridge commands, each logs to
    /// Documentation/PCPhase/Env/env_build.txt and saves the scene (never when the open scene had other unsaved changes):
    ///   Survey          read-only report (terrain, layers, roots, counts, height check against the v1 heightmap)
    ///   Capture tag     edit-mode camera renders (no play mode, no WaitForEndOfFrame) to Documentation/Screenshots/PCPhase/Env/tag_view.png
    ///   TerrainPass     backs up TD_Island once (Art/Terrain/_Backup), applies the v2 heights, 9 layers, splat, moves trees out of
    ///                   water / rock / open ground, thins details, re-snaps every object under the ENV roots (World except
    ///                   World/Resources, Water, Markers/Zones, Markers/Water), reports what other roots must re-run
    ///   Markers         Markers/Zones/&lt;id&gt; (EnvLocation) + Markers/Migration/Route_00..NN from env_features_v2.json
    ///   Water, Vegetation, Story, Volcano, Wet: see the partial files.
    ///   All             Terrain, Markers, Water, Vegetation, Story, Volcano, Wet in that order.
    /// Source data (tools/env/terrain_v2.py + terrain_post.py, numpy): Art/Environment/Terrain/*.
    /// </summary>
    public static partial class PrimalEnvironmentBuilder
    {
        const string ScenePath = "Assets/_Project/Scenes/Island_VerticalSlice.unity";
        const string EnvRoot = "Assets/_Project/Art/Environment";
        const string TerrainSrc = EnvRoot + "/Terrain";
        const string TexDir = EnvRoot + "/Textures";
        const string MatDir = EnvRoot + "/Materials";
        const string MeshDir = EnvRoot + "/Generated";
        const string ModelDir = EnvRoot + "/Models";
        const string PrefabDir = "Assets/_Project/Prefabs/Environment/PC";
        const string OldTerrainDir = "Assets/_Project/Art/Terrain";
        const string BackupDir = OldTerrainDir + "/_Backup";
        const string BackupPath = BackupDir + "/TD_Island_before_env_v2.asset";
        const string FeaturesPath = TerrainSrc + "/env_features_v2.json";
        const string LogDir = "Documentation/PCPhase/Env";
        const string ShotDir = "Documentation/Screenshots/PCPhase/Env";
        static readonly StringBuilder Log = new StringBuilder();
        static int _warnings;

        static void L(string s) { Log.AppendLine(s); Debug.Log("[PrimalEnv] " + s); }
        static void W(string s) { _warnings++; Log.AppendLine("WARNING: " + s); Debug.LogWarning("[PrimalEnv] " + s); }
        static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        static string V(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "({0:0.#}, {1:0.#}, {2:0.#})", v.x, v.y, v.z);

        static void Begin(string what)
        {
            Log.Clear(); _warnings = 0; _forceSave = what.Contains("force");
            L($"{what} started {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }

        static string End(string what)
        {
            L($"{what} finished, {_warnings} warning(s)");
            try
            {
                Directory.CreateDirectory(LogDir);
                File.AppendAllText(Path.Combine(LogDir, "env_build.txt"), Log + "\n");
            }
            catch (Exception e) { Debug.LogWarning("[PrimalEnv] log write failed: " + e.Message); }
            return Log.ToString();
        }

        static Dictionary<string, string> Args(string arg)
        {
            var d = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(arg)) return d;
            foreach (var part in arg.Split(';'))
            {
                var p = part.Trim(); if (p.Length == 0) continue;
                int eq = p.IndexOf('=');
                if (eq < 0) d[p] = ""; else d[p.Substring(0, eq).Trim()] = p.Substring(eq + 1).Trim();
            }
            return d;
        }

        // ------------------------------------------------------------------ scene / assets
        static bool IslandOpen(out Scene scene, out bool wasDirty)
        {
            scene = EditorSceneManager.GetActiveScene(); wasDirty = scene.isDirty;
            if (EditorApplication.isPlayingOrWillChangePlaymode) { L("stop Play mode first"); return false; }
            if (scene.path == ScenePath) return true;
            if (scene.isDirty) { L($"the open scene ({scene.name}) has unsaved changes: save it or open {ScenePath}, then run again"); return false; }
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single); wasDirty = false;
            return true;
        }

        /// <summary>set by a command's "force" arg: save even when the scene was dirty before (only after a failed ENV run)</summary>
        static bool _forceSave;
        static void SaveScene(Scene scene, bool wasDirty)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            if (wasDirty && _forceSave) { L("the scene had unsaved changes before this command (force): saving anyway"); wasDirty = false; }
            if (wasDirty) { W("the scene had other unsaved changes before this command: NOT saved, save it by hand"); return; }
            EditorSceneManager.SaveScene(scene);
            L("scene saved: " + scene.path);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/'), leaf = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        /// <summary>an old root ("World", "Markers", "Water"): where it is now in the active scene (HIER 2026-09-30, see SceneRoots)</summary>
        static Transform Root(Scene scene, string name, bool create = true) => PrimalFrontier.Core.SceneRoots.Legacy(name, create);

        /// <summary>find-or-create a child path under a transform ("Environment/Forest")</summary>
        static Transform Child(Transform parent, string path)
        {
            var t = parent;
            foreach (var part in path.Split('/'))
            {
                var c = t.Find(part);
                if (!c) { c = new GameObject(part).transform; c.SetParent(t, false); }
                t = c;
            }
            return t;
        }

        /// <summary>World/Environment/&lt;group&gt;</summary>
        static Transform EnvGroup(Scene scene, string group) => PrimalFrontier.Core.SceneRoots.Legacy("World/Environment/" + group, true);   // mapped by HIER

        /// <summary>existing component or a new one (Unity's fake-null objects break the ?? operator in the editor)</summary>
        static T Comp<T>(GameObject go) where T : Component { var c = go.GetComponent<T>(); return c ? c : go.AddComponent<T>(); }
        static T Or<T>(T a, T b) where T : UnityEngine.Object => a ? a : b;

        static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(t.GetChild(i).gameObject);
        }

        static Texture2D ImportTexture(string path, bool normal, bool linear, int maxSize = 1024)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!ti) { W("missing texture " + path); return null; }
            bool dirty = false;
            void Set<T>(T now, T want, Action<T> apply) { if (!EqualityComparer<T>.Default.Equals(now, want)) { apply(want); dirty = true; } }
            Set(ti.textureType, normal ? TextureImporterType.NormalMap : TextureImporterType.Default, v => ti.textureType = v);
            Set(ti.sRGBTexture, !linear && !normal, v => ti.sRGBTexture = v);
            Set(ti.wrapMode, TextureWrapMode.Repeat, v => ti.wrapMode = v);
            Set(ti.mipmapEnabled, true, v => ti.mipmapEnabled = v);
            Set(ti.anisoLevel, 4, v => ti.anisoLevel = v);
            Set(ti.maxTextureSize, maxSize, v => ti.maxTextureSize = v);
            Set(ti.textureCompression, TextureImporterCompression.CompressedHQ, v => ti.textureCompression = v);
            if (dirty) ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------------ features (tools/env/terrain_post.py)
        [Serializable] public class FSpring { public float[] center; public float radius; }
        [Serializable] public class FLine { public float[] points; public float halfWidth; public float[] halfWidths; public int[] wetlandSpan; }
        [Serializable] public class FFall { public float[] lip, pool; public float poolRadius, drop; }
        [Serializable] public class FBlob { public float[] center; public float radius; }
        [Serializable] public class FWet { public float level; public FBlob[] blobs; }
        [Serializable] public class FPond { public float[] center; public float level; }
        [Serializable] public class FWater { public FSpring spring; public FLine brook, riverA, riverB; public FFall waterfall; public float[] ford; public FWet wetland; public FPond pond; }
        [Serializable] public class FLava { public float[] points; public float[] vent; }
        [Serializable] public class FLoc { public string id; public float[] pos; public float radius; public string what; }
        [Serializable] public class FProps { public float[] oldCamp, fossilBed, nest, skeleton, knollTop, spring, wallows; }
        [Serializable] public class Features { public int version; public string[] layers; public FWater water; public FLava lava; public FLine canyon; public float[] predatorPath; public FLoc[] locations; public float[] migration; public FProps props; }

        static Features _features;
        static Features LoadFeatures()
        {
            var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(FeaturesPath);
            if (!ta) { W("missing " + FeaturesPath); return null; }
            _features = JsonUtility.FromJson<Features>(ta.text);
            return _features;
        }
        static Vector3 V3(float[] a, int i = 0) => a == null || a.Length < i * 3 + 3 ? Vector3.zero : new Vector3(a[i * 3], a[i * 3 + 1], a[i * 3 + 2]);
        static List<Vector3> Pts(float[] a) { var l = new List<Vector3>(); if (a != null) for (int i = 0; i + 2 < a.Length; i += 3) l.Add(new Vector3(a[i], a[i + 1], a[i + 2])); return l; }

        // ------------------------------------------------------------------ terrain helpers
        static Terrain _terrain;
        static Terrain FindTerrain()
        {
            var go = GameObject.Find("ENV_Island_Terrain");
            _terrain = go ? go.GetComponent<Terrain>() : Terrain.activeTerrain;
            return _terrain;
        }
        static float GroundY(Vector3 p) => _terrain ? _terrain.SampleHeight(p) + _terrain.transform.position.y : p.y;
        static Vector3 Ground(Vector3 p) { p.y = GroundY(p); return p; }
        static Vector3 GroundNormal(Vector3 p)
        {
            if (!_terrain) return Vector3.up;
            var td = _terrain.terrainData; var lp = p - _terrain.transform.position;
            return td.GetInterpolatedNormal(lp.x / td.size.x, lp.z / td.size.z);
        }
        static float Slope(Vector3 p) => Vector3.Angle(GroundNormal(p), Vector3.up);

        /// <summary>a PNG next to the terrain data read byte-exact (import settings do not matter): [z, x] per channel, row 0 = Unity z 0</summary>
        static float[][,] ReadMask(string path)
        {
            if (!File.Exists(path)) { W("missing " + path); return null; }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(path), false)) { W("cannot decode " + path); return null; }
            int w = tex.width, h = tex.height; var px = tex.GetPixels32();
            var ch = new float[4][,];
            for (int c = 0; c < 4; c++) ch[c] = new float[h, w];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var p = px[y * w + x]; int z = h - 1 - y;
                    ch[0][z, x] = p.r / 255f; ch[1][z, x] = p.g / 255f; ch[2][z, x] = p.b / 255f; ch[3][z, x] = p.a / 255f;
                }
            UnityEngine.Object.DestroyImmediate(tex);
            return ch;
        }

        /// <summary>bilinear sample of a [z, x] map covering the terrain at world (x, z)</summary>
        static float SampleMap(float[,] m, Vector3 world)
        {
            if (m == null || !_terrain) return 0f;
            var tp = _terrain.transform.position; var sz = _terrain.terrainData.size;
            int h = m.GetLength(0), w = m.GetLength(1);
            float fx = Mathf.Clamp01((world.x - tp.x) / sz.x) * (w - 1), fz = Mathf.Clamp01((world.z - tp.z) / sz.z) * (h - 1);
            int x0 = Mathf.Min((int)fx, w - 2), z0 = Mathf.Min((int)fz, h - 2); float tx = fx - x0, tz = fz - z0;
            return Mathf.Lerp(Mathf.Lerp(m[z0, x0], m[z0, x0 + 1], tx), Mathf.Lerp(m[z0 + 1, x0], m[z0 + 1, x0 + 1], tx), tz);
        }

        static float SampleHeights(float[,] h01, Vector3 world)
        {
            var sz = _terrain.terrainData.size;
            return SampleMap(h01, world) * sz.y + _terrain.transform.position.y;
        }

        static uint Hash(int a, int b = 0) { unchecked { uint h = (uint)a * 374761393u + (uint)b * 668265263u; h = (h ^ (h >> 13)) * 1274126177u; return h ^ (h >> 16); } }
        static float Rand01(int a, int b = 0) => (Hash(a, b) & 0xFFFFFF) / 16777216f;

        // ------------------------------------------------------------------ Survey
        [PrimalBridgeCommand]
        public static string Survey(string arg)
        {
            Begin("Survey");
            if (!IslandOpen(out var scene, out _)) return End("Survey");
            var t = FindTerrain();
            if (!t) { W("no terrain"); return End("Survey"); }
            var td = t.terrainData;
            L($"terrain {t.name}: pos {V(t.transform.position)} size {V(td.size)} heightmap {td.heightmapResolution} alphamap {td.alphamapResolution} ({td.alphamapLayers} layers, {td.alphamapTextureCount} textures) detail {td.detailResolution} ({td.detailPrototypes.Length} prototypes) trees {td.treeInstanceCount} ({td.treePrototypes.Length} prototypes) material {(t.materialTemplate ? t.materialTemplate.shader.name : "none")} drawInstanced {t.drawInstanced} pixelError {t.heightmapPixelError}");
            L("layers: " + string.Join(", ", td.terrainLayers.Select(l => l ? $"{l.name} (tile {F(l.tileSize.x)} m)" : "null")));
            L("tree prototypes: " + string.Join(", ", td.treePrototypes.Select(p => p.prefab ? p.prefab.name : "null")));
            L("detail prototypes: " + string.Join(", ", td.detailPrototypes.Select(p => (p.prototype ? p.prototype.name : p.prototypeTexture ? p.prototypeTexture.name : "null"))));
            var h = td.GetHeights(0, 0, td.heightmapResolution, td.heightmapResolution);
            float mn = float.MaxValue, mx = float.MinValue; double sum = 0;
            foreach (var v in h) { mn = Mathf.Min(mn, v); mx = Mathf.Max(mx, v); sum += v; }
            L($"heights {F(mn * td.size.y + t.transform.position.y)} .. {F(mx * td.size.y + t.transform.position.y)} m, mean {F((float)(sum / h.Length) * td.size.y + t.transform.position.y)}");
            foreach (var (name, file) in new[] { ("v1", OldTerrainDir + "/ENV_Island_Height_1025.bytes"), ("v2", TerrainSrc + "/ENV_Island_Height_v2.bytes") })
            {
                if (!File.Exists(file)) { L($"{name} heightmap file missing ({file})"); continue; }
                var b = File.ReadAllBytes(file); int n = td.heightmapResolution; if (b.Length != n * n * 2) { L($"{name}: size mismatch"); continue; }
                double err = 0; float emax = 0;
                for (int z = 0; z < n; z += 4) for (int x = 0; x < n; x += 4) { int i = (z * n + x) * 2; float e = Mathf.Abs((b[i] | (b[i + 1] << 8)) / 65535f - h[z, x]) * td.size.y; err += e; emax = Mathf.Max(emax, e); }
                L($"terrain vs {name} file: mean |dh| {F((float)(err / ((n / 4 + 1) * (n / 4 + 1))))} m, max {F(emax)} m");
            }
            foreach (var root in scene.GetRootGameObjects())
            {
                int n = root.GetComponentsInChildren<Transform>(true).Length;
                var kids = string.Join(", ", root.transform.Cast<Transform>().Take(14).Select(c => $"{c.name}:{c.childCount}"));
                L($"root {root.name} ({n} transforms) {kids}");
            }
            L($"backup {(File.Exists(BackupPath) ? "exists" : "not made yet")}: {BackupPath}");
            return End("Survey");
        }

        // ------------------------------------------------------------------ Capture
        struct View { public string name; public Vector3 eye, target; public float fov; public bool eyeAbs, targetAbs; }

        /// <summary>views in Unity coordinates; eye / target heights above the ground unless marked absolute</summary>
        static readonly View[] Views =
        {
            new View { name = "overview", eye = new Vector3(0, 190, 360), target = new Vector3(0, 5, -40), fov = 55, eyeAbs = true, targetAbs = true },
            new View { name = "beach", eye = new Vector3(-16, 1.7f, 206), target = new Vector3(40, 3f, 150), fov = 62 },
            new View { name = "waterfall", eye = new Vector3(78, 3.5f, -126), target = new Vector3(94, 25f, -163), fov = 60, targetAbs = true },
            new View { name = "river_valley", eye = new Vector3(58, 9f, -18), target = new Vector3(86, 14f, -112), fov = 60, targetAbs = true },
            new View { name = "meadow_from_view", eye = new Vector3(-40, 1.8f, -92), target = new Vector3(50, 9f, -70), fov = 70, targetAbs = true },
            new View { name = "canyon", eye = new Vector3(-133, 1.8f, -92), target = new Vector3(-141, 27f, -150), fov = 65, targetAbs = true },
            new View { name = "wetland", eye = new Vector3(62, 9f, 205), target = new Vector3(104, 0.5f, 168), fov = 65, targetAbs = true },
            new View { name = "forest_floor", eye = new Vector3(168, 1.7f, 26), target = new Vector3(150, 2f, -2), fov = 70 },
            new View { name = "old_camp", eye = new Vector3(114, 2.4f, -58), target = new Vector3(122, 11.8f, -66), fov = 60, targetAbs = true },
            new View { name = "volcano_ash", eye = new Vector3(-152, 4f, -196), target = new Vector3(-108, 45f, -228), fov = 65, targetAbs = true },
            // Phase 1 close-ups (not in the before set)
            new View { name = "waterfall_close", eye = new Vector3(93f, 23.5f, -145f), target = new Vector3(94f, 25f, -167f), fov = 62, eyeAbs = true, targetAbs = true },
            new View { name = "volcano_close", eye = new Vector3(-84f, 54f, -212f), target = new Vector3(-101f, 45f, -231f), fov = 60, eyeAbs = true, targetAbs = true },
        };

        [MenuItem("Primal Frontier/Environment/Capture (before)", priority = 60)] static void MenuCapture() => EditorUtility.DisplayDialog("Primal Frontier", Capture("before"), "OK");

        /// <summary>arg "tag[;only=a+b][;hour=15][;focus=Name@dist@height+Name2]" -> Documentation/Screenshots/PCPhase/Env/tag_view.png
        /// (1600x900). focus (Phase 1): a view of each named scene object from its +z side (dist m out, height m up, defaults 4.5 / 1.4).</summary>
        [PrimalBridgeCommand]
        public static string Capture(string arg)
        {
            Begin("Capture " + arg);
            var a = Args(arg);
            string tag = a.Keys.FirstOrDefault(k => !k.Contains("=") && k != "only" && k != "hour" && k != "sun" && k != "focus") ?? "shot";
            var only = a.TryGetValue("only", out var o) ? new HashSet<string>(o.Split('+')) : null;
            var scene = EditorSceneManager.GetActiveScene(); bool wasClean = !scene.isDirty;
            if (!FindTerrain()) { W("no terrain"); return End("Capture"); }
            Directory.CreateDirectory(ShotDir);
            var camGo = new GameObject("__EnvCaptureCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            var main = Camera.main; if (main) cam.CopyFrom(main);
            cam.enabled = false; cam.nearClipPlane = 0.1f; cam.farClipPlane = 2500f;
            var urp = camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            urp.renderPostProcessing = true; urp.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var sun = RenderSettings.sun; Quaternion sunRot = sun ? sun.transform.rotation : Quaternion.identity;
            try
            {
                if (sun) sun.transform.rotation = Quaternion.Euler(42f, -128f, 0f);                    // same light for before / after
                var views = new List<View>(Views);
                if (a.TryGetValue("focus", out var fo))
                {
                    if (only == null) only = new HashSet<string>();
                    foreach (var spec in fo.Split('+'))
                    {
                        var parts = spec.Split('@'); var go = GameObject.Find(parts[0]);
                        if (!go) { W("focus: no object " + parts[0]); continue; }
                        float dist = parts.Length > 1 ? float.Parse(parts[1], CultureInfo.InvariantCulture) : 4.5f, hgt = parts.Length > 2 ? float.Parse(parts[2], CultureInfo.InvariantCulture) : 1.4f;
                        var fwd = go.transform.forward; fwd.y = 0f; if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward; fwd.Normalize();
                        var eye = go.transform.position + fwd * dist; eye.y = Mathf.Max(GroundY(eye), go.transform.position.y) + hgt;
                        views.Add(new View { name = "focus_" + go.name, eye = eye, target = go.transform.position + Vector3.up * 0.8f, fov = 55, eyeAbs = true, targetAbs = true });
                        only.Add("focus_" + go.name);
                    }
                }
                foreach (var v in views)
                {
                    if (only != null && !only.Contains(v.name)) continue;
                    var eye = v.eye; if (!v.eyeAbs) eye.y = GroundY(eye) + v.eye.y;
                    var tgt = v.target; if (!v.targetAbs) tgt.y = GroundY(tgt) + v.target.y;
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation((tgt - eye).normalized));
                    cam.fieldOfView = v.fov;
                    const int w = 1600, h = 900;
                    var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                    cam.targetTexture = rt; cam.Render(); cam.Render();
                    var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                    RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(); RenderTexture.active = null;
                    cam.targetTexture = null;
                    string path = $"{ShotDir}/{tag}_{v.name}.png"; File.WriteAllBytes(path, tex.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(tex); UnityEngine.Object.DestroyImmediate(rt);
                    L($"wrote {path} eye {V(eye)}");
                }
            }
            finally
            {
                if (sun) sun.transform.rotation = sunRot;
                UnityEngine.Object.DestroyImmediate(camGo);
                if (wasClean && scene.isDirty) EditorSceneManager.SaveScene(scene);
            }
            return End("Capture");
        }

        // ------------------------------------------------------------------ All
        [MenuItem("Primal Frontier/Environment/Build All (terrain, markers, water, vegetation, story, volcano, wet)", priority = 61)]
        static void MenuAll() { if (EditorUtility.DisplayDialog("Primal Frontier", "Run the whole environment pass on the island scene? (terrain backup is kept)", "Run", "Cancel")) EditorUtility.DisplayDialog("Primal Frontier", All(""), "OK"); }

        [PrimalBridgeCommand]
        public static string All(string arg)
        {
            var sb = new StringBuilder();
            sb.AppendLine(TerrainPass(arg)); sb.AppendLine(Markers(arg)); sb.AppendLine(Water(arg));
            sb.AppendLine(Vegetation(arg)); sb.AppendLine(Story(arg)); sb.AppendLine(Volcano(arg)); sb.AppendLine(Wet(arg));
            return sb.ToString();
        }
    }
}
