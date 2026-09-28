using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// Tree / bush wind with the lean PF/Foliage Wind shader, and the night sky (PF/Night Sky).
    /// Safe order: WindTest (copies ONE tree's materials into Art/Materials/_WindTest and puts ONE extra tree,
    /// "PF_WindTest_Tree", next to the player spawn; originals, prefabs and terrain untouched) -> look at it ->
    /// WindApply "confirm" (switches the shader of the tree prototype and bush materials in place, textures / colours /
    /// cutoff / cull kept; bark bends with the crown, only leaves flutter).
    /// ROLLBACK: bridge PrimalShaderBuilder.WindRevert (menu Tools/Shaders: Wind Revert) puts every switched material back
    /// on the shader, keywords and render queue recorded in Art/Materials/_Backup/wind_originals.json (written before
    /// anything is switched; each material also carries the tag PF_WindOriginalShader; URP Lit if both are lost) and
    /// deletes the test tree and the _WindTest copies. WindRevert "test" removes only the test tree. By hand: select the
    /// material, set its shader to Universal Render Pipeline/Lit (texture, colour, cutoff, cull are kept) and tick Alpha
    /// Clipping again on leaves.
    /// Night sky: NightSky adds [Systems]/NightSky (NightSky component + M_NightSky); NightSky "remove" deletes it.
    /// </summary>
    public static partial class PrimalShaderBuilder
    {
        const string WindShader = "PF/Foliage Wind", NightShader = "PF/Night Sky";
        const string WindTag = "PF_WindOriginalShader", WindTestName = "PF_WindTest_Tree";
        const string WindJson = Backup + "/wind_originals.json", WindTestDir = Mats + "/_WindTest";
        const string IslandScene = "Assets/_Project/Scenes/Island_VerticalSlice.unity", PrefabRoot = "Assets/_Project/Prefabs";
        static readonly string[] WindSourceShaders = { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit" };
        static readonly string[] LeafWords = { "leaf", "leaves", "foliage", "frond", "palm", "needle", "canopy", "crown", "bush", "fern" };

        [System.Serializable] class WindRecord { public string path, shader; public int queue; public string[] keywords; }
        [System.Serializable] class WindRecords { public List<WindRecord> items = new List<WindRecord>(); }
        struct WindUse { public bool leaf; public float height, pivot; public List<string> prefabs; }

        [MenuItem("Primal Frontier/Tools/Shaders: Wind Test (one tree)")] static void MenuWindTest() => EditorUtility.DisplayDialog("Primal Frontier", WindTest(""), "OK");
        [MenuItem("Primal Frontier/Tools/Shaders: Wind Apply (trees + bushes)")]
        static void MenuWindApply()
        {
            if (!EditorUtility.DisplayDialog("Primal Frontier", "Switch the tree and bush materials to PF/Foliage Wind? Save the scene first. 'Shaders: Wind Revert' puts the recorded shaders back.", "Switch", "Cancel")) return;
            EditorUtility.DisplayDialog("Primal Frontier", WindApply("confirm"), "OK");
        }
        [MenuItem("Primal Frontier/Tools/Shaders: Wind Revert")] static void MenuWindRevert() => EditorUtility.DisplayDialog("Primal Frontier", WindRevert(""), "OK");
        [MenuItem("Primal Frontier/Tools/Shaders: Night Sky (stars)")] static void MenuNightSky() => EditorUtility.DisplayDialog("Primal Frontier", NightSky(""), "OK");

        static string F(float v) => v.ToString("0.00", CultureInfo.InvariantCulture);
        static string V(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "({0:0.0}, {1:0.0}, {2:0.0})", v.x, v.y, v.z);
        static Shader FindShader(string name) => string.IsNullOrEmpty(name) ? null : Shader.Find(name);

        // ------------------------------------------------------------------ wind: test one tree
        /// <summary>arg: tree prototype index (default 0)</summary>
        [PrimalBridgeCommand]
        public static string WindTest(string arg)
        {
            Log.Clear();
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "stop Play mode first";
            if (!Ok(WindShader, out var sh)) return Log.ToString();
            if (!IslandOpen(out var scene, out bool wasDirty)) return Log.ToString();
            var trees = TreePrefabs();
            if (trees.Count == 0) { L("no tree prototype on the terrain and no PFB_ENV_Tree prefab: nothing done"); return Log.ToString(); }
            int idx = 0;
            if (!string.IsNullOrEmpty(arg) && !int.TryParse(arg.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out idx)) idx = 0;
            idx = Mathf.Clamp(idx, 0, trees.Count - 1);
            var prefab = trees[idx];
            var uses = new Dictionary<Material, WindUse>();
            Collect(prefab, false, uses);
            if (uses.Count == 0) { L(prefab.name + ": no mesh materials: nothing done"); return Log.ToString(); }
            RemoveWindTest(false);                                        // one test tree at a time
            if (!AssetDatabase.IsValidFolder(WindTestDir)) AssetDatabase.CreateFolder(Mats, "_WindTest");
            if (!uses.Values.Any(u => u.leaf)) L(prefab.name + ": no leaf material recognised (alpha clip, or leaf / foliage / palm in the name): it bends, nothing flutters");
            var map = new Dictionary<Material, Material>();
            foreach (var kv in uses)
            {
                var src = kv.Key;
                var copy = new Material(src) { name = src.name + "_WindTest" };
                AssetDatabase.CreateAsset(copy, $"{WindTestDir}/{copy.name}.mat");
                SwitchToWind(copy, sh, kv.Value);
                map[src] = copy;
                L($"{copy.name}: copy of {src.name} ({src.shader.name}), {(kv.Value.leaf ? "leaf" : "bark")}, height {F(kv.Value.height)} m, sway {F(copy.GetFloat("_WindSway"))} m, flutter {F(copy.GetFloat("_Flutter"))} m");
                if (kv.Value.pivot > 0.5f) L($"  WARNING {src.name}: a mesh pivot sits {F(kv.Value.pivot)} m above the prefab root: its parts may bend apart");
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            go.name = WindTestName;
            go.transform.SetPositionAndRotation(WindTestPosition(), Quaternion.identity);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var arr = r.sharedMaterials; bool changed = false;
                for (int i = 0; i < arr.Length; i++) if (arr[i] && map.TryGetValue(arr[i], out var c)) { arr[i] = c; changed = true; }
                if (changed) r.sharedMaterials = arr;                     // scene instance override only
            }
            Undo.RegisterCreatedObjectUndo(go, "Wind test tree");
            Selection.activeGameObject = go;
            AssetDatabase.SaveAssets();
            SaveScene(scene, wasDirty);
            L($"test tree '{WindTestName}' ({prefab.name}, prototype {idx} of {trees.Count}) at {V(go.transform.position)}; originals untouched");
            L("see it move: Play mode (weather wind), or Scene view with Always Refresh on. Remove: WindRevert \"test\". Switch all: WindApply \"confirm\"");
            return Log.ToString();
        }

        // ------------------------------------------------------------------ wind: switch all tree / bush materials
        [PrimalBridgeCommand]
        public static string WindApply(string arg)
        {
            Log.Clear();
            if (arg != "confirm") return "switches the tree and bush materials to PF/Foliage Wind: call with arg \"confirm\" (try WindTest first; WindRevert undoes it)";
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "stop Play mode first";
            if (!Ok(WindShader, out var sh)) return Log.ToString();
            var trees = TreePrefabs(); var bushes = PrefabsNamed("PFB_ENV_Bush");
            var targets = new Dictionary<Material, WindUse>();
            foreach (var t in trees) Collect(t, false, targets);
            foreach (var b in bushes) Collect(b, true, targets);
            L($"trees: {string.Join(", ", trees.Select(t => t.name))}; bushes: {string.Join(", ", bushes.Select(b => b.name))}");
            var rec = LoadWindRecords();
            var todo = new List<KeyValuePair<Material, WindUse>>();
            foreach (var kv in targets)
            {
                var m = kv.Key; string path = AssetDatabase.GetAssetPath(m);
                if (m.shader == sh) { L($"{m.name}: already {WindShader}"); continue; }
                if (!path.EndsWith(".mat", System.StringComparison.OrdinalIgnoreCase)) { L($"{m.name}: skipped (inside {path}, not a .mat asset)"); continue; }
                if (!m.shader || !WindSourceShaders.Contains(m.shader.name)) { L($"{m.name}: skipped (shader {(m.shader ? m.shader.name : "none")})"); continue; }
                if (!rec.items.Any(r => r.path == path)) rec.items.Add(new WindRecord { path = path, shader = m.shader.name, queue = m.renderQueue, keywords = m.shaderKeywords });
                todo.Add(kv);
            }
            if (todo.Count == 0) { L("nothing to switch"); return Log.ToString(); }
            SaveWindRecords(rec);                                          // before any switch: a crash half way still has the way back
            foreach (var kv in todo)
            {
                var m = kv.Key;
                m.SetOverrideTag(WindTag, m.shader.name);
                SwitchToWind(m, sh, kv.Value);
                L($"{m.name}: {WindShader} ({(kv.Value.leaf ? "leaf" : "bark")}, height {F(kv.Value.height)} m, sway {F(m.GetFloat("_WindSway"))} m, flutter {F(m.GetFloat("_Flutter"))} m) used by {string.Join(", ", kv.Value.prefabs)}");
                if (kv.Value.pivot > 0.5f) L($"  WARNING {m.name}: a mesh pivot sits {F(kv.Value.pivot)} m above its prefab root: its parts may bend apart");
            }
            AssetDatabase.SaveAssets();
            ReportOtherUsers(todo.Select(kv => AssetDatabase.GetAssetPath(kv.Key)), trees.Concat(bushes));
            L($"{todo.Count} material(s) switched, originals recorded in {WindJson}. Rollback: PrimalShaderBuilder.WindRevert");
            return Log.ToString();
        }

        // ------------------------------------------------------------------ wind: rollback
        /// <summary>arg "test": only remove the test tree and its copies</summary>
        [PrimalBridgeCommand]
        public static string WindRevert(string arg)
        {
            Log.Clear();
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "stop Play mode first";
            RemoveWindTest(true);
            if (arg == "test") return Log.ToString();
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var rec = LoadWindRecords();
            var done = new HashSet<string>(); int n = 0;
            foreach (var r in rec.items)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(r.path);
                if (!m) { L("missing " + r.path); continue; }
                done.Add(r.path);
                if (!m.shader || m.shader.name != WindShader) { L($"{m.name}: not on {WindShader} ({(m.shader ? m.shader.name : "none")}), left alone"); continue; }
                var orig = FindShader(r.shader) ?? FindShader(m.GetTag(WindTag, false, "")) ?? lit;
                RestoreMaterial(m, orig, r.keywords, r.queue); n++;
            }
            // switched without a record (json lost): the tag, else URP Lit
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/_Project" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (done.Contains(p) || p.StartsWith(WindTestDir)) continue;
                var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (!m || !m.shader || m.shader.name != WindShader) continue;
                var orig = FindShader(m.GetTag(WindTag, false, "")) ?? lit;
                L($"{m.name}: no record, using {(orig ? orig.name : "none")}");
                if (orig) { RestoreMaterial(m, orig, m.shaderKeywords, m.renderQueue); n++; }
            }
            AssetDatabase.SaveAssets();
            if (File.Exists(WindJson)) AssetDatabase.DeleteAsset(WindJson);
            L($"{n} material(s) restored");
            return Log.ToString();
        }

        static void RestoreMaterial(Material m, Shader sh, string[] keywords, int queue)
        {
            m.shader = sh;
            m.shaderKeywords = keywords ?? new string[0];
            m.renderQueue = queue;
            m.SetOverrideTag(WindTag, "");
            EditorUtility.SetDirty(m);
            L($"{m.name}: back to {sh.name}");
        }

        // ------------------------------------------------------------------ wind helpers
        static void SwitchToWind(Material m, Shader sh, WindUse u)
        {
            bool clip = AlphaClipped(m);
            float cull = m.HasProperty("_Cull") ? m.GetFloat("_Cull") : 2f;
            int queue = m.renderQueue;
            m.shader = sh;                                                 // same property names: texture / colour / cutoff stay
            m.SetFloat("_AlphaClip", clip ? 1f : 0f);
            if (clip) m.EnableKeyword("_ALPHATEST_ON"); else m.DisableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", cull);
            m.renderQueue = queue;
            float h = Mathf.Max(1f, u.height);
            m.SetFloat("_WindHeight", h);
            m.SetFloat("_WindSway", Mathf.Clamp(h * 0.028f, 0.04f, 0.3f));
            m.SetFloat("_Flutter", u.leaf ? Mathf.Clamp(h * 0.005f, 0.01f, 0.045f) : 0f);
            m.SetFloat("_Translucency", u.leaf ? 0.35f : 0f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
        }

        static bool AlphaClipped(Material m) => (m.HasProperty("_AlphaClip") && m.GetFloat("_AlphaClip") > 0.5f) || m.IsKeywordEnabled("_ALPHATEST_ON");

        static bool IsLeaf(Material m)
        {
            string n = m.name.ToLowerInvariant();
            if (n.Contains("bark") || n.Contains("trunk") || n.Contains("wood")) return AlphaClipped(m);
            return AlphaClipped(m) || LeafWords.Any(n.Contains);
        }

        /// <summary>materials of a prefab's mesh renderers: leaf or bark, top height above each mesh's own pivot</summary>
        static void Collect(GameObject prefab, bool bush, Dictionary<Material, WindUse> into)
        {
            if (!prefab) return;
            var root = prefab.transform;
            foreach (var r in prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                float pivot = root.InverseTransformPoint(r.transform.position).y, top = 0f;
                var mf = r.GetComponent<MeshFilter>();
                if (mf && mf.sharedMesh)
                {
                    var b = mf.sharedMesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                        top = Mathf.Max(top, root.InverseTransformPoint(r.transform.TransformPoint(corner)).y - pivot);
                    }
                }
                foreach (var m in r.sharedMaterials)
                {
                    if (!m) continue;
                    into.TryGetValue(m, out var u);
                    if (u.prefabs == null) u.prefabs = new List<string>();
                    if (!u.prefabs.Contains(prefab.name)) u.prefabs.Add(prefab.name);
                    u.leaf |= bush || IsLeaf(m);
                    u.height = Mathf.Max(u.height, top);
                    u.pivot = Mathf.Max(u.pivot, pivot);
                    into[m] = u;
                }
            }
        }

        /// <summary>the terrain's tree prototypes (in order) plus any other PFB_ENV_Tree prefab</summary>
        static List<GameObject> TreePrefabs()
        {
            var list = new List<GameObject>();
            var t = Terrain.activeTerrain ? Terrain.activeTerrain : Object.FindFirstObjectByType<Terrain>();
            if (t && t.terrainData)
                foreach (var p in t.terrainData.treePrototypes)
                    if (p != null && p.prefab && !list.Contains(p.prefab)) list.Add(p.prefab);
            foreach (var pf in PrefabsNamed("PFB_ENV_Tree")) if (!list.Contains(pf)) list.Add(pf);
            return list;
        }

        static List<GameObject> PrefabsNamed(string prefix)
        {
            var list = new List<GameObject>();
            if (!AssetDatabase.IsValidFolder(PrefabRoot)) return list;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (!Path.GetFileNameWithoutExtension(p).StartsWith(prefix, System.StringComparison.Ordinal)) continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (go && !list.Contains(go)) list.Add(go);
            }
            return list;
        }

        /// <summary>other prefabs (props, terrain detail meshes) that share a switched material</summary>
        static void ReportOtherUsers(IEnumerable<string> matPaths, IEnumerable<GameObject> known)
        {
            var set = new HashSet<string>(matPaths);
            var knownPaths = new HashSet<string>(known.Select(g => AssetDatabase.GetAssetPath(g)));
            var users = new SortedSet<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (knownPaths.Contains(p)) continue;
                if (AssetDatabase.GetDependencies(p, true).Any(set.Contains)) users.Add(Path.GetFileNameWithoutExtension(p));
            }
            if (users.Count > 0) L("also using these materials (they sway as well, check them): " + string.Join(", ", users));
        }

        static WindRecords LoadWindRecords()
        {
            try { if (File.Exists(WindJson)) return JsonUtility.FromJson<WindRecords>(File.ReadAllText(WindJson)) ?? new WindRecords(); }
            catch (System.Exception e) { L("could not read " + WindJson + ": " + e.Message); }
            return new WindRecords();
        }

        static void SaveWindRecords(WindRecords r)
        {
            Directory.CreateDirectory(Backup);
            File.WriteAllText(WindJson, JsonUtility.ToJson(r, true));
            AssetDatabase.ImportAsset(WindJson);
        }

        static Vector3 WindTestPosition()
        {
            Vector3 p;
            var spawn = GameObject.Find("ZONE_PlayerSpawn");
            if (spawn) p = spawn.transform.position + spawn.transform.forward * 8f + spawn.transform.right * 3f;
            else if (SceneView.lastActiveSceneView != null) p = SceneView.lastActiveSceneView.pivot;
            else p = Vector3.zero;
            var t = Terrain.activeTerrain;
            if (t) p.y = t.SampleHeight(p) + t.transform.position.y;
            return p;
        }

        /// <summary>deletes every PF_WindTest_Tree in the open scenes and the _WindTest material copies</summary>
        static void RemoveWindTest(bool save)
        {
            int n = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (!s.isLoaded) continue;
                bool wasDirty = s.isDirty; int before = n;
                foreach (var root in s.GetRootGameObjects())
                    foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(x => x.name == WindTestName).ToArray())
                        if (t) { Object.DestroyImmediate(t.gameObject); n++; }
                if (save && n > before) SaveScene(s, wasDirty);
            }
            if (AssetDatabase.IsValidFolder(WindTestDir)) { AssetDatabase.DeleteAsset(WindTestDir); L("deleted " + WindTestDir); }
            if (n > 0) L($"removed {n} wind test tree(s)");
        }

        // ------------------------------------------------------------------ scene helpers
        /// <summary>the island scene open and active (opened only when the current scene has no unsaved changes)</summary>
        static bool IslandOpen(out Scene scene, out bool wasDirty)
        {
            scene = EditorSceneManager.GetActiveScene(); wasDirty = scene.isDirty;
            if (scene.path == IslandScene) return true;
            if (scene.isDirty) { L($"the open scene ({scene.name}) has unsaved changes: save it or open {IslandScene}, then run again"); return false; }
            if (!File.Exists(IslandScene)) { L("scene missing: " + IslandScene); return false; }
            scene = EditorSceneManager.OpenScene(IslandScene, OpenSceneMode.Single); wasDirty = false;
            return true;
        }

        /// <summary>saves the scene unless it already had someone's unsaved changes (then it stays dirty for them)</summary>
        static void SaveScene(Scene scene, bool wasDirty)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            if (wasDirty) { L("the scene had other unsaved changes: NOT saved, save it yourself"); return; }
            if (string.IsNullOrEmpty(scene.path)) return;
            EditorSceneManager.SaveScene(scene);
            L("scene saved: " + scene.path);
        }

        // ------------------------------------------------------------------ night sky
        /// <summary>adds / updates [Systems]/NightSky with M_NightSky; arg "remove" deletes it</summary>
        [PrimalBridgeCommand]
        public static string NightSky(string arg)
        {
            Log.Clear();
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "stop Play mode first";
            if (!IslandOpen(out var scene, out bool wasDirty)) return Log.ToString();
            var existing = Object.FindFirstObjectByType<PrimalFrontier.VFX.NightSky>(FindObjectsInactive.Include);
            if (arg == "remove")
            {
                if (!existing) { L("no NightSky in the scene"); return Log.ToString(); }
                var go = existing.gameObject;
                if (go.GetComponents<Component>().Length <= 2 && go.transform.childCount == 0) Object.DestroyImmediate(go); else Object.DestroyImmediate(existing);
                L("NightSky removed (stars off)");
                SaveScene(scene, wasDirty);
                return Log.ToString();
            }
            if (!Ok(NightShader, out var sh)) return Log.ToString();
            string mp = $"{Mats}/M_NightSky.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (!m) { m = new Material(sh) { name = "M_NightSky" }; AssetDatabase.CreateAsset(m, mp); L("material " + mp); }
            else if (m.shader != sh) m.shader = sh;
            EditorUtility.SetDirty(m);
            if (!existing)
            {
                var go = new GameObject("NightSky");
                var systems = GameObject.Find("[Systems]");
                if (systems) go.transform.SetParent(systems.transform, false);
                existing = go.AddComponent<PrimalFrontier.VFX.NightSky>();
                Undo.RegisterCreatedObjectUndo(go, "Night sky");
                L("added " + (systems ? "[Systems]/" : "") + "NightSky");
            }
            existing.material = m;
            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssets();
            SaveScene(scene, wasDirty);
            L("stars fade in after sunset (_PF_NightFactor from TimeManager); by day the dome renderer is off. Remove: NightSky \"remove\"");
            return Log.ToString();
        }
    }
}
