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
    /// SPLIT (the way to use wind now): WindApply switched the SHARED materials (M_Bark / M_Foliage ...), so fallen logs,
    /// ferns, grass details, resource nodes and berry plants swayed too. WindSplit makes dedicated copies
    /// (Art/Materials/&lt;name&gt;_Wind.mat for the trees; &lt;name&gt;_BushWind.mat for a material the bushes share with the trees,
    /// with bush-height wind numbers) with the same textures, colours, cutoff, keywords and wind numbers, assigns them only
    /// to the tree prefabs (terrain tree prototypes + PFB_ENV_Tree*) and the bush prefabs (PFB_ENV_Bush*: renderer
    /// overrides in the prefab; a prototype that is a model file gets an importer remap for that model only), refreshes
    /// the terrain tree prototypes and puts the shared materials back on their original shader from wind_originals.json.
    /// Idempotent (a second run changes nothing); everything is recorded in _Backup/wind_split.json. WindRevert undoes the
    /// split first (prefab overrides / remaps back, copies deleted) and then restores the shared materials as before, so
    /// it still returns to the state before any wind.
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
        [MenuItem("Primal Frontier/Tools/Shaders: Wind Split (only trees + bushes sway)")]
        static void MenuWindSplit()
        {
            if (!EditorUtility.DisplayDialog("Primal Frontier", "Give the trees and bushes their own wind materials (M_*_Wind) and put the shared materials back on their original shader? 'Shaders: Wind Revert' undoes it.", "Split", "Cancel")) return;
            EditorUtility.DisplayDialog("Primal Frontier", WindSplit(""), "OK");
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
            UndoWindSplit();
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

        // ------------------------------------------------------------------ wind: split (dedicated copies for trees / bushes)
        const string SplitJson = Backup + "/wind_split.json", CopyTag = "PF_WindCopyOf";
        [System.Serializable] class SplitCopy { public string source, copy, group; }
        [System.Serializable] class SplitEdit { public string prefab, renderer; public int slot; public string from, to; public bool hadOverride; }
        [System.Serializable] class SplitRemap { public string model, name, from, to; }
        [System.Serializable]
        class SplitRecord
        {
            public List<SplitCopy> copies = new List<SplitCopy>();
            public List<SplitEdit> edits = new List<SplitEdit>();
            public List<SplitRemap> remaps = new List<SplitRemap>();
            public List<string> restored = new List<string>();
            public bool Empty => copies.Count == 0 && edits.Count == 0 && remaps.Count == 0;
        }

        /// <summary>"path" for a .mat asset, "path#name" for a material inside a model file</summary>
        static string MatKey(Material m)
        {
            string p = AssetDatabase.GetAssetPath(m);
            return string.IsNullOrEmpty(p) || AssetDatabase.IsMainAsset(m) ? p : p + "#" + m.name;
        }

        static Material LoadMat(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            int i = key.IndexOf('#');
            if (i < 0) return AssetDatabase.LoadAssetAtPath<Material>(key);
            string path = key.Substring(0, i), name = key.Substring(i + 1);
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().FirstOrDefault(x => x.name == name);
        }

        static bool IsWindCopy(Material m) => m && !string.IsNullOrEmpty(m.GetTag(CopyTag, false, ""));

        static SplitRecord LoadSplit()
        {
            try { if (File.Exists(SplitJson)) return JsonUtility.FromJson<SplitRecord>(File.ReadAllText(SplitJson)) ?? new SplitRecord(); }
            catch (System.Exception e) { L("could not read " + SplitJson + ": " + e.Message); }
            return new SplitRecord();
        }

        static void SaveSplit(SplitRecord r)
        {
            Directory.CreateDirectory(Backup);
            File.WriteAllText(SplitJson, JsonUtility.ToJson(r, true));
            AssetDatabase.ImportAsset(SplitJson);
        }

        static string RelPath(Transform root, Transform t)
        {
            var parts = new List<string>();
            for (var x = t; x && x != root; x = x.parent) parts.Insert(0, x.name);
            return string.Join("/", parts);
        }

        /// <summary>
        /// Trees and bushes get their own wind materials; the shared ones go back to their original shader.
        /// Idempotent. Undo: WindRevert.
        /// </summary>
        [PrimalBridgeCommand]
        public static string WindSplit(string arg)
        {
            Log.Clear();
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "stop Play mode first";
            if (!Ok(WindShader, out var sh)) return Log.ToString();
            if (!IslandOpen(out var scene, out bool wasDirty)) return Log.ToString();
            var trees = TreePrefabs(); var bushes = PrefabsNamed("PFB_ENV_Bush");
            if (trees.Count + bushes.Count == 0) { L("no tree prototype / PFB_ENV_Tree / PFB_ENV_Bush prefab: nothing done"); return Log.ToString(); }
            L($"trees: {string.Join(", ", trees.Select(t => t.name))}; bushes: {string.Join(", ", bushes.Select(b => b.name))}");
            var treeUses = new Dictionary<Material, WindUse>(); var bushUses = new Dictionary<Material, WindUse>();
            foreach (var t in trees) Collect(t, false, treeUses);
            foreach (var b in bushes) Collect(b, true, bushUses);
            var rec = LoadWindRecords(); var split = LoadSplit();
            int created = 0, kept = 0, skipped = 0;
            // 1) the copies, per group ("tree" / "bush"): source key -> copy
            var copyFor = new Dictionary<string, Dictionary<Material, Material>> { ["tree"] = new Dictionary<Material, Material>(), ["bush"] = new Dictionary<Material, Material>() };
            void MakeCopies(Dictionary<Material, WindUse> uses, string group)
            {
                foreach (var kv in uses)
                {
                    var m = kv.Key;
                    if (IsWindCopy(m)) continue;                                    // already split
                    if (!m.shader || (m.shader != sh && !WindSourceShaders.Contains(m.shader.name))) { L($"{m.name}: skipped (shader {(m.shader ? m.shader.name : "none")})"); skipped++; continue; }
                    string key = MatKey(m);
                    if (string.IsNullOrEmpty(key)) { L($"{m.name}: skipped (not an asset)"); skipped++; continue; }
                    bool shared = group == "bush" && treeUses.ContainsKey(m);      // same material on trees and bushes: bush-height numbers
                    var copy = FindSplitCopy(split, key, group);
                    if (copy && copy.shader == sh) { kept++; copyFor[group][m] = copy; continue; }
                    string baseName = m.name + (shared ? "_BushWind" : "_Wind"), path = $"{Mats}/{baseName}.mat";
                    for (int n = 2; AssetDatabase.LoadAssetAtPath<Material>(path) && AssetDatabase.LoadAssetAtPath<Material>(path).GetTag(CopyTag, false, "") != key; n++) path = $"{Mats}/{baseName}_{n}.mat";
                    copy = AssetDatabase.LoadAssetAtPath<Material>(path);
                    bool fromWind = m.shader == sh;
                    if (!copy) { copy = new Material(m) { name = Path.GetFileNameWithoutExtension(path) }; AssetDatabase.CreateAsset(copy, path); }
                    else { copy.shader = m.shader; copy.CopyPropertiesFromMaterial(m); copy.shaderKeywords = m.shaderKeywords; copy.renderQueue = m.renderQueue; }
                    // a switched source already carries its wind numbers (kept as they are); a Lit source (or the bush copy) gets them from the heights
                    if (!fromWind || shared) SwitchToWind(copy, sh, kv.Value);
                    copy.SetOverrideTag(CopyTag, key);
                    copy.enableInstancing = true;
                    EditorUtility.SetDirty(copy);
                    split.copies.RemoveAll(c => c.source == key && c.group == group);
                    split.copies.Add(new SplitCopy { source = key, copy = path, group = group });
                    copyFor[group][m] = copy; created++;
                    L($"{copy.name}: copy of {m.name} ({(fromWind ? "wind numbers kept" : "switched from " + m.shader.name)}{(shared ? ", bush height" : "")}), height {F(kv.Value.height)} m, sway {F(copy.GetFloat("_WindSway"))} m, flutter {F(copy.GetFloat("_Flutter"))} m, for {string.Join(", ", kv.Value.prefabs)}");
                }
            }
            MakeCopies(treeUses, "tree"); MakeCopies(bushUses, "bush");
            SaveSplit(split);                                                       // before any prefab changes: the way back exists
            // 2) assign: prefab renderer overrides, or an importer remap when a prototype is a model file
            int edits = 0, remaps = 0;
            foreach (var (list, group) in new[] { (trees, "tree"), (bushes, "bush") })
                foreach (var go in list)
                {
                    string path = AssetDatabase.GetAssetPath(go);
                    if (string.IsNullOrEmpty(path)) continue;
                    if (AssetImporter.GetAtPath(path) is ModelImporter mi) remaps += RemapModel(mi, path, go, copyFor[group], split);
                    else edits += AssignInPrefab(path, copyFor[group], split);
                }
            SaveSplit(split);
            // 3) the shared materials back on their original shader (only the ones now replaced by a copy)
            int restored = 0;
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var m in copyFor["tree"].Keys.Concat(copyFor["bush"].Keys).Distinct())
            {
                if (!m || m.shader != sh) continue;
                string path = AssetDatabase.GetAssetPath(m);
                if (!path.EndsWith(".mat", System.StringComparison.OrdinalIgnoreCase)) continue;
                var r = rec.items.FirstOrDefault(x => x.path == path);
                var orig = (r != null ? FindShader(r.shader) : null) ?? FindShader(m.GetTag(WindTag, false, "")) ?? lit;
                RestoreMaterial(m, orig, r != null ? r.keywords : m.shaderKeywords, r != null ? r.queue : m.renderQueue);
                if (!split.restored.Contains(path)) split.restored.Add(path);
                restored++;
            }
            SaveSplit(split);
            RefreshTreePrototypes();
            AssetDatabase.SaveAssets();
            if (created + edits + remaps + restored == 0) L($"nothing to change: already split ({kept} copies in use){(skipped > 0 ? $", {skipped} material(s) skipped" : "")}");
            else
            {
                L($"split: {created} copies made, {kept} kept, {edits} renderer slot(s) switched in prefabs, {remaps} model remap(s), {restored} shared material(s) back on their original shader");
                ReportOtherUsers(split.restored, trees.Concat(bushes));
                L("the shared materials above no longer sway; terrain detail meshes (DET_*) stay still. Undo: PrimalShaderBuilder.WindRevert");
            }
            if (wasDirty) L("the scene had unsaved changes: nothing in it was changed by the split");
            return Log.ToString();
        }

        static Material FindSplitCopy(SplitRecord split, string key, string group)
        {
            var e = split.copies.FirstOrDefault(c => c.source == key && c.group == group);
            var m = e != null ? AssetDatabase.LoadAssetAtPath<Material>(e.copy) : null;
            return m && m.GetTag(CopyTag, false, "") == key ? m : null;
        }

        /// <summary>renderer slots of one prefab that hold a source material get its copy (a prefab override); returns the slots changed</summary>
        static int AssignInPrefab(string path, Dictionary<Material, Material> copies, SplitRecord split)
        {
            if (copies.Count == 0) return 0;
            var root = PrefabUtility.LoadPrefabContents(path);
            int n = 0;
            try
            {
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mats = r.sharedMaterials; bool changed = false;
                    bool had = false;
                    if (PrefabUtility.IsPartOfPrefabInstance(r)) { var prop = new SerializedObject(r).FindProperty("m_Materials"); had = prop != null && prop.prefabOverride; }
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (!mats[i] || !copies.TryGetValue(mats[i], out var copy)) continue;
                        split.edits.Add(new SplitEdit { prefab = path, renderer = RelPath(root.transform, r.transform), slot = i, from = MatKey(mats[i]), to = AssetDatabase.GetAssetPath(copy), hadOverride = had });
                        mats[i] = copy; changed = true; n++;
                    }
                    if (changed) r.sharedMaterials = mats;
                }
                if (n > 0) { PrefabUtility.SaveAsPrefabAsset(root, path); L($"{Path.GetFileNameWithoutExtension(path)}: {n} renderer slot(s) on the wind copies"); }
            }
            catch (System.Exception e) { L($"{path}: FAILED ({e.Message}); its shared materials may lose the wind"); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return n;
        }

        /// <summary>a model file used directly as a tree prototype: remap its materials to the copies (this model only)</summary>
        static int RemapModel(ModelImporter mi, string path, GameObject model, Dictionary<Material, Material> copies, SplitRecord split)
        {
            var map = mi.GetExternalObjectMap();
            int n = 0;
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (!m || !copies.TryGetValue(m, out var copy)) continue;
                    // the identifier: the remap entry that points at this material, else the embedded material's own name
                    var id = map.FirstOrDefault(kv => kv.Key.type == typeof(Material) && kv.Value == m).Key;
                    string name = id.name ?? m.name;
                    if (split.remaps.Any(x => x.model == path && x.name == name)) continue;
                    var sid = new AssetImporter.SourceAssetIdentifier(typeof(Material), name);
                    split.remaps.Add(new SplitRemap { model = path, name = name, from = map.TryGetValue(sid, out var prev) && prev ? AssetDatabase.GetAssetPath(prev) : "", to = AssetDatabase.GetAssetPath(copy) });
                    mi.AddRemap(sid, copy); n++;
                }
            if (n > 0) { mi.SaveAndReimport(); L($"{Path.GetFileName(path)}: {n} material remap(s) to the wind copies (this model only)"); }
            return n;
        }

        static void RefreshTreePrototypes()
        {
            var t = Terrain.activeTerrain ? Terrain.activeTerrain : Object.FindFirstObjectByType<Terrain>();
            if (!t || !t.terrainData) return;
            t.terrainData.RefreshPrototypes();
            t.Flush();
            EditorUtility.SetDirty(t.terrainData);
            L("terrain tree prototypes refreshed");
        }

        /// <summary>WindRevert, first step: prefab overrides and model remaps back, the copies deleted, the record removed</summary>
        static void UndoWindSplit()
        {
            if (!File.Exists(SplitJson)) return;
            var split = LoadSplit();
            var copyToSource = new Dictionary<string, string>();
            foreach (var c in split.copies) if (!string.IsNullOrEmpty(c.copy)) copyToSource[c.copy] = c.source;
            int slots = 0;
            foreach (var group in split.edits.GroupBy(e => e.prefab))
            {
                if (!File.Exists(group.Key)) { L("missing " + group.Key); continue; }
                var root = PrefabUtility.LoadPrefabContents(group.Key);
                try
                {
                    var noOverride = new HashSet<string>(group.Where(e => !e.hadOverride).Select(e => e.renderer));
                    int n = 0;
                    foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        var mats = r.sharedMaterials; bool changed = false;
                        for (int i = 0; i < mats.Length; i++)
                        {
                            if (!mats[i]) continue;
                            string cp = AssetDatabase.GetAssetPath(mats[i]);
                            if (!copyToSource.TryGetValue(cp, out var src)) continue;
                            var orig = LoadMat(src);
                            if (!orig) { L($"{group.Key}: source {src} missing, slot left on {Path.GetFileName(cp)}"); continue; }
                            mats[i] = orig; changed = true; n++;
                        }
                        if (!changed) continue;
                        r.sharedMaterials = mats;
                        // no override before the split: revert it so the prefab follows its model again
                        if (noOverride.Contains(RelPath(root.transform, r.transform)) && PrefabUtility.IsPartOfPrefabInstance(r))
                        {
                            var prop = new SerializedObject(r).FindProperty("m_Materials");
                            if (prop != null && prop.prefabOverride) PrefabUtility.RevertPropertyOverride(prop, InteractionMode.AutomatedAction);
                        }
                    }
                    if (n > 0) { PrefabUtility.SaveAsPrefabAsset(root, group.Key); slots += n; }
                }
                catch (System.Exception e) { L($"{group.Key}: revert FAILED ({e.Message})"); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            foreach (var model in split.remaps.GroupBy(x => x.model))
            {
                if (!(AssetImporter.GetAtPath(model.Key) is ModelImporter mi)) { L("missing " + model.Key); continue; }
                foreach (var x in model)
                {
                    var sid = new AssetImporter.SourceAssetIdentifier(typeof(Material), x.name);
                    var prev = string.IsNullOrEmpty(x.from) ? null : AssetDatabase.LoadAssetAtPath<Material>(x.from);
                    if (prev) mi.AddRemap(sid, prev); else mi.RemoveRemap(sid);
                }
                mi.SaveAndReimport();
                L($"{Path.GetFileName(model.Key)}: material remaps restored");
            }
            int deleted = 0;
            foreach (var c in split.copies) if (!string.IsNullOrEmpty(c.copy) && AssetDatabase.LoadAssetAtPath<Material>(c.copy)) { AssetDatabase.DeleteAsset(c.copy); deleted++; }
            RefreshTreePrototypes();
            AssetDatabase.DeleteAsset(SplitJson);
            AssetDatabase.SaveAssets();
            L($"wind split undone: {slots} prefab slot(s) back on the shared materials, {deleted} copies deleted");
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
