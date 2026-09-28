using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PrimalFrontier.Items;
using PrimalFrontier.World;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// Interactive bushes (additive, never touches other objects): builds PFB_ENV_BushInteractive (root: BushInteraction +
    /// trigger sphere + kinematic Rigidbody on the Ignore Raycast layer, child "Visual" with the mesh) from the Blender bush
    /// models (Art/Models/Vegetation/ENV_Bush_01..03.fbx) or, until they exist, the terrain detail bush prefab (DET_*Bush*),
    /// and places 150 of them under World/Vegetation/InteractiveBushes: on dry land (no beach, pond or stream), slope up
    /// to 25 degrees, clear of colliders and tree trunks, 4 m apart, a quarter near the day-one places (camp, meadow, pond,
    /// berry / fibre areas, cave, wreck), the rest biased to forest edges and paths. Deterministic (fixed seed).
    /// Variants 60 % plain, 20 % berries (ResourceNode with the RES_BerryPlant data), 12 % hidden item, 8 % animal flush.
    /// Idempotent: an existing holder with bushes is kept and reported; arg "rebuild" deletes only its children and
    /// places them again (and rebuilds the prefabs).
    /// </summary>
    public static class PrimalBushBuilder
    {
        const string Scene = "Assets/_Project/Scenes/Island_VerticalSlice.unity";
        const string ModelDir = "Assets/_Project/Art/Models/Vegetation";
        const string EnvPrefabDir = "Assets/_Project/Prefabs/Environment";
        const string PrefabPath = EnvPrefabDir + "/PFB_ENV_BushInteractive.prefab";
        const string ItemsDir = "Assets/_Project/Data/Items", DbPath = "Assets/_Project/Resources/ItemDatabase.asset";
        const string HolderName = "InteractiveBushes";
        const int Target = 150, Seed = 20260928;
        const float MinSpacing = 4f, MaxSlope = 25f, NearRadius = 30f, MaxHeight = 40f;
        const float VisualHeight = 1.3f;                   // wanted bush height (m) when a mesh has to be scaled up
        static readonly string[] MarkerNames = { "ZONE_Camp", "ZONE_Meadow", "ZONE_Pond", "RESAREA_Berries", "RESAREA_Fiber", "ZONE_Cave", "ZONE_Shipwreck" };
        static readonly string[] WaterNames = { "ENV_Pond_Water", "ENV_Stream_Water" };
        static readonly StringBuilder Log = new StringBuilder();
        static void L(string s) { Log.AppendLine(s); Debug.Log("[PrimalBush] " + s); }

        [MenuItem("Primal Frontier/Tools/Place Interactive Bushes")]
        static void Menu() => EditorUtility.DisplayDialog("Primal Frontier", Build(""), "OK");

        [MenuItem("Primal Frontier/Tools/Place Interactive Bushes (re-place)")]
        static void MenuRebuild()
        {
            if (EditorUtility.DisplayDialog("Primal Frontier", "Delete the bushes under World/Vegetation/" + HolderName + " and place them again? Nothing else is changed.", "Re-place", "Cancel"))
                EditorUtility.DisplayDialog("Primal Frontier", Build("rebuild"), "OK");
        }

        [PrimalBridgeCommand]
        public static string Build(string arg)
        {
            Log.Clear();
            bool rebuild = !string.IsNullOrEmpty(arg) && arg.Trim().Equals("rebuild", StringComparison.OrdinalIgnoreCase);
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != Scene)
            {
                if (scene.isDirty) { L("the open scene has unsaved changes: save it (or open " + Scene + ") and run again"); return Log.ToString(); }
                if (!File.Exists(Scene)) { L("scene missing: " + Scene); return Log.ToString(); }
                scene = EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            }
            var world = GameObject.Find("World");
            if (!world) { L("no 'World' root in the scene (build the island first): nothing placed"); return Log.ToString(); }
            var terrain = Terrain.activeTerrain ? Terrain.activeTerrain : UnityEngine.Object.FindFirstObjectByType<Terrain>();
            if (!terrain) { L("no terrain in the scene: nothing placed"); return Log.ToString(); }

            // bush models use the shared vegetation materials (also when the bushes are already placed)
            for (int i = 1; i <= 3; i++) { string fp = $"{ModelDir}/ENV_Bush_{i:00}.fbx"; if (File.Exists(fp)) RemapToProjectMaterials(fp, L); }
            // holder: World/Vegetation/InteractiveBushes (World/InteractiveBushes when there is no Vegetation group)
            var veg = world.transform.Find("Vegetation");
            Transform holder = veg ? veg.Find(HolderName) : null;
            if (!holder) holder = world.transform.Find(HolderName);
            if (holder && holder.childCount > 0 && !rebuild)
            {
                L($"kept {holder.childCount} bushes under {PathOf(holder)} (arg \"rebuild\" places them again)");
                Report(holder);
                return Log.ToString();
            }

            // visuals
            var sources = FindVisuals(out bool detail);
            if (sources.Count == 0)
            {
                L($"no bush mesh: neither {ModelDir}/ENV_Bush_01..03.fbx nor a DET_*Bush* prefab in {EnvPrefabDir}. Nothing placed.");
                return Log.ToString();
            }
            L(detail ? $"visual: terrain detail prefab {sources[0].name} (scaled up) until ENV_Bush_01..03.fbx are exported"
                     : $"visual: {string.Join(", ", sources.Select(s => s.name))}");
            var prefabs = new List<GameObject>();
            for (int k = 0; k < sources.Count; k++)
            {
                string path = k == 0 ? PrefabPath : $"{EnvPrefabDir}/PFB_ENV_BushInteractive_{k + 1:00}.prefab";
                var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (existing && !rebuild && SameSource(existing, sources[k])) { prefabs.Add(existing); L("prefab kept " + path); continue; }
                var p = MakePrefab(sources[k], detail, path);
                if (p) prefabs.Add(p);
            }
            if (prefabs.Count == 0) { L("prefab creation failed: nothing placed"); return Log.ToString(); }

            if (!holder) { holder = new GameObject(HolderName).transform; holder.SetParent(veg ? veg : world.transform, false); L("created " + PathOf(holder)); }
            if (rebuild && holder.childCount > 0)
            {
                int n = holder.childCount;
                for (int i = n - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(holder.GetChild(i).gameObject);
                L($"rebuild: removed {n} old bushes");
            }

            var spots = FindSpots(terrain);
            Place(holder, prefabs, spots);
            Report(holder);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            L("scene saved " + scene.path);
            return Log.ToString();
        }

        // ------------------------------------------------------------------ visuals + prefab
        /// <summary>materials embedded in a pipeline FBX are remapped by name onto the project's shared .mat assets
        /// (Art/Materials/M_*.mat), like the other vegetation / tool models, so shaders, alpha clip and wind apply</summary>
        public static int RemapToProjectMaterials(string fbx, System.Action<string> log = null)
        {
            var mi = AssetImporter.GetAtPath(fbx) as ModelImporter;
            if (mi == null) return 0;
            var map = mi.GetExternalObjectMap();
            int n = 0;
            foreach (var m in AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Material>())
            {
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name);
                if (map.ContainsKey(id)) continue;
                var target = AssetDatabase.LoadAssetAtPath<Material>($"Assets/_Project/Art/Materials/{m.name}.mat");
                if (!target) continue;
                mi.AddRemap(id, target); n++;
            }
            if (n > 0) { mi.SaveAndReimport(); log?.Invoke($"{System.IO.Path.GetFileName(fbx)}: {n} material(s) remapped to Art/Materials"); }
            return n;
        }

        static List<GameObject> FindVisuals(out bool detail)
        {
            detail = false;
            var list = new List<GameObject>();
            for (int i = 1; i <= 3; i++)
            {
                var m = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/ENV_Bush_{i:00}.fbx");
                if (m) list.Add(m);
            }
            if (list.Count > 0) return list;
            if (!AssetDatabase.IsValidFolder(EnvPrefabDir)) return list;
            // fallback: the single-mesh terrain detail bush PrimalWorldBuilder makes (DET_ENV_Bush_01)
            var det = AssetDatabase.FindAssets("DET_ t:Prefab", new[] { EnvPrefabDir }).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => { var f = Path.GetFileNameWithoutExtension(p); return f.StartsWith("DET_") && f.IndexOf("Bush", StringComparison.OrdinalIgnoreCase) >= 0; })
                .OrderBy(p => p, StringComparer.Ordinal).FirstOrDefault();
            var dp = det != null ? AssetDatabase.LoadAssetAtPath<GameObject>(det) : null;
            if (dp && dp.GetComponentInChildren<Renderer>(true)) { list.Add(dp); detail = true; }
            return list;
        }

        static bool SameSource(GameObject prefab, GameObject source)
        {
            var bi = prefab.GetComponent<BushInteraction>();
            var vis = bi && bi.visual ? bi.visual : prefab.transform.Find("Visual");
            return vis && vis.childCount > 0 && vis.GetChild(0).name == source.name && prefab.GetComponent<SphereCollider>() && prefab.GetComponent<Rigidbody>();
        }

        static GameObject MakePrefab(GameObject source, bool detail, string path)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(path));
            try
            {
                int ignore = LayerMask.NameToLayer("Ignore Raycast");            // projectiles / build placement skip this layer
                if (ignore >= 0) root.layer = ignore;
                var vis = new GameObject("Visual").transform; vis.SetParent(root.transform, false);
                var m = (GameObject)PrefabUtility.InstantiatePrefab(source, vis);
                m.name = source.name;
                foreach (var c in m.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);   // walk-through
                foreach (var t in m.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);   // it moves: never static-batched
                foreach (var r in m.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; r.receiveShadows = true; }

                // size: the detail mesh is small (x2..3); a proper model keeps its size unless it is tiny
                var b = RendBounds(m);
                float h = b.size.y;
                float f = detail ? (h > 0.01f ? Mathf.Clamp(VisualHeight / h, 2f, 3f) : 2.5f)
                                 : (h > 0.01f && h < 0.7f ? Mathf.Clamp(VisualHeight / h, 1f, 3f) : 1f);
                vis.localScale = Vector3.one * f;
                // rest the lowest point on the ground so the visual pivots at its base
                b = RendBounds(m);
                if (Mathf.Abs(b.min.y) > 0.02f) m.transform.localPosition += Vector3.up * (-b.min.y / f);
                b = RendBounds(m);

                // cull small far bushes (terrain detail bushes carry the density further out)
                if (!m.GetComponentInChildren<LODGroup>(true))
                {
                    var lg = vis.gameObject.AddComponent<LODGroup>();
                    lg.SetLODs(new[] { new LOD(0.015f, m.GetComponentsInChildren<Renderer>(true)) });
                    lg.RecalculateBounds();
                }

                var sc = root.AddComponent<SphereCollider>();
                sc.isTrigger = true;
                sc.radius = Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.z) * 0.85f, 0.6f, 1.3f);
                sc.center = new Vector3(0f, Mathf.Clamp(b.size.y * 0.45f, 0.4f, 0.9f), 0f);
                var rb = root.AddComponent<Rigidbody>();
                rb.isKinematic = true; rb.useGravity = false;
                var bi = root.AddComponent<BushInteraction>();
                bi.visual = vis;

                Directory.CreateDirectory(EnvPrefabDir);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                L($"prefab {path}: visual {source.name} x{f:0.##} (height {b.size.y:0.00} m), trigger r {sc.radius:0.00} m");
                return prefab;
            }
            catch (Exception e) { L("prefab " + path + " FAILED: " + e.Message); return null; }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static Bounds RendBounds(GameObject g)
        {
            var rs = g.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(g.transform.position, Vector3.zero);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        // ------------------------------------------------------------------ placement
        struct Spot { public Vector3 p, n; public bool near; }
        enum Why { Edge, Height, Slope, Water, Beach, Spacing, Tree, Blocked, Covered, Bias, Count }

        class Ctx
        {
            public Terrain terrain; public TerrainData td; public Vector3 tp, size;
            public List<Bounds> water = new List<Bounds>();
            public Dictionary<long, List<Vector2>> trees = new Dictionary<long, List<Vector2>>();
            public int[] layerKind;                        // 0 other, 1 sand, 2 forest floor, 3 path (mud / dirt)
            public List<Spot> spots = new List<Spot>();
            public int[] why = new int[(int)Why.Count];
            public readonly Collider[] buf = new Collider[16];
        }

        const float TreeCell = 8f;
        static long Cell(float x, float z) => ((long)Mathf.FloorToInt(x / TreeCell) << 32) ^ (uint)Mathf.FloorToInt(z / TreeCell);

        static List<Spot> FindSpots(Terrain terrain)
        {
            Physics.SyncTransforms();
            var c = new Ctx { terrain = terrain, td = terrain.terrainData, tp = terrain.transform.position };
            c.size = c.td.size;
            foreach (var n in WaterNames)
            {
                var w = GameObject.Find(n); if (!w) continue;
                foreach (var r in w.GetComponentsInChildren<Renderer>()) c.water.Add(r.bounds);
            }
            foreach (var ti in c.td.treeInstances)
            {
                var wp = c.tp + Vector3.Scale(ti.position, c.size);
                long k = Cell(wp.x, wp.z);
                if (!c.trees.TryGetValue(k, out var l)) c.trees[k] = l = new List<Vector2>();
                l.Add(new Vector2(wp.x, wp.z));
            }
            var layers = c.td.terrainLayers;
            c.layerKind = new int[layers.Length];
            for (int i = 0; i < layers.Length; i++)
            {
                string nm = ((layers[i] ? layers[i].name : "") + " " + (layers[i] && layers[i].diffuseTexture ? layers[i].diffuseTexture.name : "")).ToLowerInvariant();
                c.layerKind[i] = nm.Contains("sand") || nm.Contains("beach") ? 1 : nm.Contains("forest") ? 2 : nm.Contains("mud") || nm.Contains("path") || nm.Contains("dirt") ? 3 : 0;
            }
            var rng = new System.Random(Seed);

            // a quarter within 30 m of the places the player passes on day one
            var markers = new List<Vector3>(); var missing = new List<string>();
            foreach (var n in MarkerNames) { var g = GameObject.Find(n); if (g) markers.Add(g.transform.position); else missing.Add(n); }
            if (missing.Count > 0) L("markers not found: " + string.Join(", ", missing));
            int nearTarget = Mathf.RoundToInt(Target * 0.25f), tries = 0;
            for (int i = 0; markers.Count > 0 && c.spots.Count < nearTarget && tries < nearTarget * 120; i++, tries++)
            {
                var m = markers[i % markers.Count];
                double a = rng.NextDouble() * Math.PI * 2.0, d = 4.0 + (NearRadius - 4.0) * Math.Sqrt(rng.NextDouble());
                var p = new Vector3(m.x + (float)(Math.Cos(a) * d), 0f, m.z + (float)(Math.Sin(a) * d));
                if (TryGround(c, p, 1.2f, out var s)) { s.near = true; c.spots.Add(s); }
            }
            int near = c.spots.Count;
            // the rest over the land (1.5 .. 40 m high), preferring forest edges and paths
            tries = 0;
            while (c.spots.Count < Target && tries < 60000)
            {
                tries++;
                var p = new Vector3(c.tp.x + 2f + (float)rng.NextDouble() * (c.size.x - 4f), 0f, c.tp.z + 2f + (float)rng.NextDouble() * (c.size.z - 4f));
                if (!TryGround(c, p, 1.5f, out var s)) continue;
                if (rng.NextDouble() > EdgeBias(c, s.p)) { c.why[(int)Why.Bias]++; continue; }
                c.spots.Add(s);
            }
            L($"spots: {c.spots.Count} / {Target} ({near} near markers, {c.spots.Count - near} over the land); rejected: " +
              string.Join(", ", Enumerable.Range(0, (int)Why.Count).Where(i => c.why[i] > 0).Select(i => $"{(Why)i} {c.why[i]}")));
            if (c.spots.Count < Target) L($"WARNING: only {c.spots.Count} dry, flat, free spots found");
            return c.spots;
        }

        static bool TryGround(Ctx c, Vector3 p, float minHeight, out Spot s)
        {
            s = default;
            float u = (p.x - c.tp.x) / c.size.x, v = (p.z - c.tp.z) / c.size.z;
            if (u < 0.005f || u > 0.995f || v < 0.005f || v > 0.995f) { c.why[(int)Why.Edge]++; return false; }
            float y = c.terrain.SampleHeight(p) + c.tp.y;
            if (y < minHeight || y > MaxHeight) { c.why[(int)Why.Height]++; return false; }
            var n = c.td.GetInterpolatedNormal(u, v);
            if (Vector3.Angle(n, Vector3.up) > MaxSlope) { c.why[(int)Why.Slope]++; return false; }
            var g = new Vector3(p.x, y, p.z);
            if (IsWet(c, g)) { c.why[(int)Why.Water]++; return false; }
            if (LayerWeight(c, u, v, 1) > 0.5f) { c.why[(int)Why.Beach]++; return false; }
            foreach (var o in c.spots)
            {
                float dx = o.p.x - g.x, dz = o.p.z - g.z;
                if (dx * dx + dz * dz < MinSpacing * MinSpacing) { c.why[(int)Why.Spacing]++; return false; }
            }
            if (TreesWithin(c, g, 1.6f, 1) > 0) { c.why[(int)Why.Tree]++; return false; }
            // clear of rocks, props, logs, resource nodes: the spec probe (1.2 m up, r 0.6) + the base (r 1.2, terrain ignored)
            if (Physics.CheckSphere(g + Vector3.up * 1.2f, 0.6f, ~0, QueryTriggerInteraction.Ignore)) { c.why[(int)Why.Blocked]++; return false; }
            int hits = Physics.OverlapSphereNonAlloc(g + Vector3.up * 0.6f, 1.2f, c.buf, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits; i++) if (!(c.buf[i] is TerrainCollider)) { c.why[(int)Why.Blocked]++; return false; }
            // open ground: the first solid thing from above is the terrain (not under the cave roof, a rock arch or the wreck)
            if (Physics.Raycast(g + Vector3.up * 30f, Vector3.down, out var hit, 31f, ~0, QueryTriggerInteraction.Ignore) && !(hit.collider is TerrainCollider))
            { c.why[(int)Why.Covered]++; return false; }
            s = new Spot { p = g, n = n };
            return true;
        }

        /// <summary>PrimalPhase2Builder.IsWet: inside (or within 5 m of) the pond / stream surface and less than 1 m above it</summary>
        static bool IsWet(Ctx c, Vector3 p)
        {
            foreach (var w in c.water)
            {
                var b = w; b.Expand(new Vector3(5f, 0f, 5f));
                if (p.x > b.min.x && p.x < b.max.x && p.z > b.min.z && p.z < b.max.z && p.y < b.max.y + 1.0f) return true;
            }
            return false;
        }

        static float LayerWeight(Ctx c, float u, float v, int kind)
        {
            if (c.layerKind.Length == 0 || c.td.alphamapWidth <= 0) return 0f;
            int x = Mathf.Clamp(Mathf.RoundToInt(u * (c.td.alphamapWidth - 1)), 0, c.td.alphamapWidth - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt(v * (c.td.alphamapHeight - 1)), 0, c.td.alphamapHeight - 1);
            var a = c.td.GetAlphamaps(x, z, 1, 1);
            float w = 0f;
            for (int l = 0; l < c.layerKind.Length && l < a.GetLength(2); l++) if (c.layerKind[l] == kind) w += a[0, 0, l];
            return w;
        }

        static int TreesWithin(Ctx c, Vector3 p, float r, int cap)
        {
            int n = 0; float r2 = r * r;
            int x0 = Mathf.FloorToInt((p.x - r) / TreeCell), x1 = Mathf.FloorToInt((p.x + r) / TreeCell);
            int z0 = Mathf.FloorToInt((p.z - r) / TreeCell), z1 = Mathf.FloorToInt((p.z + r) / TreeCell);
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    if (!c.trees.TryGetValue(((long)x << 32) ^ (uint)z, out var l)) continue;
                    foreach (var t in l) { float dx = t.x - p.x, dz = t.y - p.z; if (dx * dx + dz * dz < r2 && ++n >= cap) return n; }
                }
            return n;
        }

        /// <summary>acceptance chance: forest edges and paths high, open grass low, deep forest medium</summary>
        static float EdgeBias(Ctx c, Vector3 p)
        {
            int t = TreesWithin(c, p, 10f, 9);
            float s = t == 0 ? 0.3f : t <= 5 ? 1f : 0.55f;
            float u = (p.x - c.tp.x) / c.size.x, v = (p.z - c.tp.z) / c.size.z;
            float forest = LayerWeight(c, u, v, 2);
            if (forest > 0.2f && forest < 0.8f) s = Mathf.Max(s, 0.9f);
            if (LayerWeight(c, u, v, 3) > 0.3f) s = Mathf.Max(s, 0.85f);
            return s;
        }

        static void Place(Transform holder, List<GameObject> prefabs, List<Spot> spots)
        {
            var rng = new System.Random(Seed + 1);
            int n = spots.Count;
            int nb = Mathf.RoundToInt(n * 0.20f), nh = Mathf.RoundToInt(n * 0.12f), nf = Mathf.RoundToInt(n * 0.08f);
            var variants = new List<BushVariant>(n);
            for (int i = 0; i < n; i++) variants.Add(i < nb ? BushVariant.Berries : i < nb + nh ? BushVariant.HiddenItem : i < nb + nh + nf ? BushVariant.AnimalFlush : BushVariant.Plain);
            for (int i = n - 1; i > 0; i--) { int j = rng.Next(i + 1); (variants[i], variants[j]) = (variants[j], variants[i]); }

            var berries = Item("berries");
            if (!berries) L("item 'berries' missing: berry bushes become plain");
            var loot = new List<BushLoot>();
            void Loot(string id, int min, int max, float w) { var it = Item(id); if (it) loot.Add(new BushLoot { item = it, min = min, max = max, weight = w }); else L("hidden item '" + id + "' missing"); }
            Loot("fiber", 2, 4, 4f); Loot("stone", 1, 2, 3f); Loot("berries", 2, 4, 3f); Loot("bone", 1, 1, 1.5f);
            if (loot.Count == 0) L("no hidden item found: hidden-item bushes become plain");

            for (int i = 0; i < n; i++)
            {
                var sp = spots[i]; var v = variants[i];
                if (v == BushVariant.Berries && !berries) v = BushVariant.Plain;
                if (v == BushVariant.HiddenItem && loot.Count == 0) v = BushVariant.Plain;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[rng.Next(prefabs.Count)], holder);
                go.name = $"Bush_{i + 1:000}" + (v != BushVariant.Plain ? "_" + v : "");
                float yaw = (float)(rng.NextDouble() * 360.0);
                var lean = Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, sp.n, 0.5f));   // half-follow the slope: roots stay in the ground
                go.transform.SetPositionAndRotation(sp.p - Vector3.up * 0.05f, lean * Quaternion.Euler(0f, yaw, 0f));
                float scale = Mathf.Lerp(0.8f, 1.3f, (float)rng.NextDouble());
                go.transform.localScale = Vector3.one * scale;
                var bi = go.GetComponent<BushInteraction>();
                bi.variant = v;
                if (v == BushVariant.HiddenItem) bi.hiddenLoot = loot.ToArray();
                PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(bi);
                if (v == BushVariant.Berries)
                {
                    // same data as the RES_BerryPlant nodes (PrimalGameplayBuilder), reachable from the bush edge
                    var node = go.AddComponent<ResourceNode>();
                    node.displayName = "Berry bush"; node.verb = "Pick"; node.yieldItem = berries; node.yieldPerHit = 2; node.charges = 3;
                    node.requiredTool = ToolKind.None; node.fasterTool = ToolKind.None; node.regrowHours = 20f; node.hideWhenEmpty = false;
                    node.handAction = node.toolAction = Animation.PlayerActions.GatherPlant;
                    var sc = go.GetComponent<SphereCollider>();
                    node.radius = (sc ? sc.radius : 0.8f) * scale * 0.75f;
                    node.emptyScale = 0.9f;                  // a picked bush barely shrinks (piles use 0.7)
                    node.SaveId = $"bush_berry_{i + 1:000}";
                }
            }
            L($"placed {n} bushes");
        }

        static ItemDefinition Item(string id)
        {
            var it = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{ItemsDir}/ITEM_{id}.asset");
            if (it) return it;
            var db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(DbPath);
            return db ? db.items.FirstOrDefault(x => x && x.id == id) : null;
        }

        static void Report(Transform holder)
        {
            var counts = new int[4]; int berryNodes = 0, lootless = 0;
            foreach (Transform t in holder)
            {
                var bi = t.GetComponent<BushInteraction>(); if (!bi) continue;
                int k = Mathf.Clamp((int)bi.variant, 0, 3); counts[k]++;
                if (bi.variant == BushVariant.Berries && t.GetComponent<ResourceNode>()) berryNodes++;
                if (bi.variant == BushVariant.HiddenItem && (bi.hiddenLoot == null || bi.hiddenLoot.Length == 0)) lootless++;
            }
            L($"variants: Plain {counts[0]}, Berries {counts[1]} ({berryNodes} with ResourceNode), HiddenItem {counts[2]}{(lootless > 0 ? $" ({lootless} without loot)" : "")}, AnimalFlush {counts[3]}");
        }

        static string PathOf(Transform t) { var s = t.name; while (t.parent) { t = t.parent; s = t.name + "/" + s; } return s; }
    }
}
