using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PrimalFrontier.World;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// The island resource pass (bridge: PrimalResourceBuilder.Build). Idempotent, deterministic (fixed seed), logs every
    /// step to Documentation/Resources/resource_build.txt and saves the scene. It never changes the terrain or the island
    /// outline and never touches item data (unlike PrimalGameplayBuilder). Positions come from the scene at build time:
    /// ENV's location markers (Markers/Zones/&lt;id&gt;, EnvLocation), the water surfaces (WaterSource meshes), the terrain
    /// and its trees, the old ZONE_ markers as a fallback; nothing is hard-coded.
    ///  1. data: definitions, tools, database, node prefabs (BuildData);
    ///  2. converts what already looks like a resource: stone piles, driftwood, fibre plants, berry plants / bushes (+ a
    ///     visible crop on the bushes), the medium / large rocks (large rock / boulder), the small decorative rocks (now
    ///     Stone nodes) and the fallen logs (deadwood). Cliffs and the cave stay scenery;
    ///  3. re-snaps the nodes that are not rebuilt (World/Resources, the cave stones, Markers/ResourceAreas) to the current
    ///     ground and moves the ones that ended up in water to the nearest dry spot;
    ///  4. rebuilds the [Resources] root: a dense start area around the beach spawn, trails from the beach to the pond and
    ///     to the river mouth, fallen fruit under each fruit tree, bank clusters + fish shoals along every fresh water body
    ///     (river, waterfall pool, lagoon, pond), clusters inside the new biomes (meadow, canyon, ridge, wetland, waterfall)
    ///     and one small cluster per ~34 m cell elsewhere, picked by biome, with empty ground between clusters; rare nodes
    ///     (bones and a hide at the kill site / predator territory, shipwreck scraps along the shore);
    ///  5. verifies: every node on the ground (none buried or floating), none in water unless it is a fish shoal.
    /// </summary>
    public static partial class PrimalResourceBuilder
    {
        enum Biome { Beach, Meadow, ForestEdge, Forest, Rocky, WaterEdge, RiverBank, WaterfallPool, Wetland, Canyon, Ridge }
        /// <summary>ENV's location markers by id (Markers/Zones/&lt;id&gt;), read at build time</summary>
        static readonly Dictionary<string, (Vector3 p, float r)> _loc = new Dictionary<string, (Vector3, float)>();
        const int Seed = 35012;

        static Terrain _t; static TerrainData _td; static Vector3 _tp;
        static readonly Dictionary<Vector2Int, List<Vector3>> _treeGrid = new Dictionary<Vector2Int, List<Vector3>>();
        static readonly Dictionary<Vector2Int, float> _water = new Dictionary<Vector2Int, float>();   // 1 m cells of visible fresh water -> surface y
        static string[] _layers;
        static StringBuilder _log;
        static int _placed; static readonly Dictionary<string, int> _placedBy = new Dictionary<string, int>();

        [PrimalBridgeCommand]
        public static string Build(string arg)
        {
            _log = new StringBuilder();
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (scene.isDirty) return "active scene has unsaved changes and is not the island: open the island first";
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            L($"PrimalResourceBuilder.Build {System.DateTime.Now:yyyy-MM-dd HH:mm} arg '{arg}'");
            var db = BuildDataInto(_log);
            if (!Setup()) return _log.ToString();
            ConvertExisting(db);
            ResnapExisting();
            PlaceNew(db);
            P1AfterPlaceNew(db);
            WireSystems(db);
            Report();
            Verify();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            L("scene saved " + scene.path);
            Directory.CreateDirectory(DocDir);
            File.WriteAllText(System.IO.Path.Combine(DocDir, "resource_build.txt"), _log.ToString());
            return _log.ToString();
        }

        static void L(string s) { _log.AppendLine(s); }

        static bool Setup()
        {
            _t = Terrain.activeTerrain; if (!_t) { L("ERROR no terrain"); return false; }
            _td = _t.terrainData; _tp = _t.transform.position;
            _layers = _td.terrainLayers.Select(l => l ? l.name : "").ToArray();
            _treeGrid.Clear();
            foreach (var ti in _td.treeInstances)
            {
                var p = Vector3.Scale(ti.position, _td.size) + _tp; var k = Cell8(p);
                if (!_treeGrid.TryGetValue(k, out var l)) _treeGrid[k] = l = new List<Vector3>(); l.Add(p);
            }
            _water.Clear(); var bodies = new List<string>();
            foreach (var ws in Object.FindObjectsByType<WaterSource>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!ws.enabled || !ws.gameObject.activeInHierarchy) continue;                     // the old stream is switched off: not water any more
                var mf = ws.surface ? ws.surface : ws.GetComponentInChildren<MeshFilter>(); if (!mf || !mf.sharedMesh) continue;
                int before = _water.Count; RasterWater(mf); bodies.Add($"{ws.name} {_water.Count - before}");
            }
            _loc.Clear();
            foreach (var e in Object.FindObjectsByType<EnvLocation>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (!string.IsNullOrEmpty(e.id)) _loc[e.id] = (e.transform.position, Mathf.Max(5f, e.radius));
            // fallbacks for a scene without ENV's markers (old ZONE_ empties)
            void Fallback(string id, string zone, float r) { if (!_loc.ContainsKey(id)) { var m = Marker(zone); if (m != Vector3.zero) _loc[id] = (m, r); } }
            Fallback("beach", "ZONE_StartBeach", 40f); Fallback("shipwreck", "ZONE_Shipwreck", 18f); Fallback("meadow", "ZONE_Meadow", 70f); Fallback("cave", "ZONE_Cave", 12f);
            Physics.SyncTransforms();
            L($"terrain {_td.size} at {_tp}, layers {string.Join(",", _layers)}, trees {_td.treeInstanceCount}");
            L($"fresh water cells {_water.Count} from {bodies.Count} active bodies: {string.Join(", ", bodies)}");
            L($"locations ({_loc.Count}): {string.Join(", ", _loc.OrderBy(k => k.Key).Select(k => $"{k.Key} {V(k.Value.p)} r{k.Value.r:F0}"))}");
            foreach (var need in new[] { "beach", "river", "waterfall", "meadow", "canyon", "wetland", "ridge", "predator_territory" }) if (!_loc.ContainsKey(need)) L($"WARNING location '{need}' missing: its clusters are skipped");
            return true;
        }

        static Vector2Int Cell8(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / 8f), Mathf.FloorToInt(p.z / 8f));
        static Vector2Int Cell1(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.z));
        public static float Ground(Vector3 p) => _t.SampleHeight(p) + _tp.y;

        static void RasterWater(MeshFilter mf)
        {
            var m = mf.sharedMesh; var v = m.vertices; var tr = m.triangles; var w = mf.transform;
            var wv = v.Select(x => w.TransformPoint(x)).ToArray();
            for (int i = 0; i < tr.Length; i += 3)
            {
                Vector3 a = wv[tr[i]], b = wv[tr[i + 1]], c = wv[tr[i + 2]];
                int x0 = Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))), x1 = Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)));
                int z0 = Mathf.FloorToInt(Mathf.Min(a.z, Mathf.Min(b.z, c.z))), z1 = Mathf.CeilToInt(Mathf.Max(a.z, Mathf.Max(b.z, c.z)));
                for (int x = x0; x <= x1; x++)
                    for (int z = z0; z <= z1; z++)
                    {
                        var p = new Vector2(x + 0.5f, z + 0.5f);
                        if (!Bary(p, new Vector2(a.x, a.z), new Vector2(b.x, b.z), new Vector2(c.x, c.z), out var u, out var vv, out var ww)) continue;
                        float y = a.y * u + b.y * vv + c.y * ww;
                        var q = new Vector3(p.x, 0, p.y);
                        if (Ground(q) < y + 0.02f) _water[new Vector2Int(x, z)] = y;            // visible water only (not under the terrain)
                    }
            }
        }

        static bool Bary(Vector2 p, Vector2 a, Vector2 b, Vector2 c, out float u, out float v, out float w)
        {
            var v0 = b - a; var v1 = c - a; var v2 = p - a;
            float d = v0.x * v1.y - v1.x * v0.y; u = v = w = 0;
            if (Mathf.Abs(d) < 1e-8f) return false;
            v = (v2.x * v1.y - v1.x * v2.y) / d; w = (v0.x * v2.y - v2.x * v0.y) / d; u = 1 - v - w;
            return u >= -1e-4f && v >= -1e-4f && w >= -1e-4f;
        }

        static bool InWater(Vector3 p) => _water.ContainsKey(Cell1(p));
        static float WaterDistance(Vector3 p, float max = 9f)
        {
            float best = max * max; var k = Cell1(p); int r = Mathf.CeilToInt(max);
            for (int dx = -r; dx <= r; dx += 1)
                for (int dz = -r; dz <= r; dz += 1)
                {
                    if (!_water.ContainsKey(new Vector2Int(k.x + dx, k.y + dz))) continue;
                    float d = dx * dx + dz * dz; if (d < best) best = d;
                }
            return Mathf.Sqrt(best);
        }

        static int TreesWithin(Vector3 p, float r)
        {
            int n = 0; var k = Cell8(p); int c = Mathf.CeilToInt(r / 8f); float r2 = r * r;
            for (int dx = -c; dx <= c; dx++)
                for (int dz = -c; dz <= c; dz++)
                    if (_treeGrid.TryGetValue(new Vector2Int(k.x + dx, k.y + dz), out var l))
                        foreach (var t in l) { var d = t - p; d.y = 0; if (d.sqrMagnitude < r2) n++; }
            return n;
        }

        static string Splat(Vector3 p)
        {
            var u = p - _tp;
            int x = Mathf.Clamp(Mathf.RoundToInt(u.x / _td.size.x * (_td.alphamapWidth - 1)), 0, _td.alphamapWidth - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt(u.z / _td.size.z * (_td.alphamapHeight - 1)), 0, _td.alphamapHeight - 1);
            var a = _td.GetAlphamaps(x, z, 1, 1); int best = 0;
            for (int i = 1; i < _td.alphamapLayers; i++) if (a[0, 0, i] > a[0, 0, best]) best = i;
            return best < _layers.Length ? _layers[best] : "";
        }

        static float Steep(Vector3 p) { var u = p - _tp; return _td.GetSteepness(u.x / _td.size.x, u.z / _td.size.z); }
        static Vector3 Normal(Vector3 p) { var u = p - _tp; return _td.GetInterpolatedNormal(u.x / _td.size.x, u.z / _td.size.z); }

        static Vector3 Marker(string name) { var g = GameObject.Find(name); return g ? g.transform.position : Vector3.zero; }
        static bool Loc(string id, out Vector3 p, out float r) { if (_loc.TryGetValue(id, out var v)) { p = v.p; r = v.r; return true; } p = Vector3.zero; r = 0f; return false; }
        static bool InLoc(string id, Vector3 p, float scale = 1f) => _loc.TryGetValue(id, out var v) && Flat(p - v.p).magnitude <= v.r * scale;
        /// <summary>the storytelling prop with this discovery id (ENV's Story step), or the location fallback</summary>
        static bool Prop(string discoveryId, string fallbackLoc, out Vector3 p)
        {
            foreach (var e in Object.FindObjectsByType<Examinable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (e.discoveryId == discoveryId) { p = e.transform.position; return true; }
            if (fallbackLoc != null && Loc(fallbackLoc, out p, out _)) return true;
            p = Vector3.zero; return false;
        }

        /// <summary>biome of a spot: ENV's locations first (wetland, canyon, ridge, volcano, waterfall, river, meadow, beach), then water distance, splat layer and tree density</summary>
        static Biome BiomeAt(Vector3 p)
        {
            float h = Ground(p); float wd = WaterDistance(p, 9f);
            if (InLoc("wetland", p, 1.1f)) return Biome.Wetland;
            if (InLoc("waterfall", p, 1.2f)) return Biome.WaterfallPool;
            if (InLoc("canyon", p, 1.1f) || InLoc("fossil_bed", p)) return Biome.Canyon;
            if (InLoc("ridge", p) || InLoc("volcano", p, 0.9f)) return Biome.Ridge;
            if (wd < 7f) return InLoc("river", p, 1.3f) ? Biome.RiverBank : Biome.WaterEdge;
            string layer = Splat(p);
            if (layer.Contains("Mud")) return Biome.WaterEdge;
            var rocky = Marker("ZONE_Rocky");
            if (layer.Contains("Rock") || layer.Contains("Ash") || Steep(p) > 24f || (rocky != Vector3.zero && Flat(p - rocky).magnitude < 45f)) return Biome.Rocky;
            if (layer.Contains("Sand") && h < 6f) return Biome.Beach;
            int trees = TreesWithin(p, 12f);
            if (trees >= 6 || layer.Contains("Forest") || layer.Contains("Moss") || InLoc("deep_forest", p, 0.8f)) return Biome.Forest;
            if (trees >= 1) return Biome.ForestEdge;
            return Biome.Meadow;
        }

        static readonly Collider[] _ov = new Collider[16];
        /// <summary>a spot a node of footprint r can go: land above the sea, no fresh water, not too steep, off tree trunks and other colliders</summary>
        static bool Free(Vector3 p, float r)
        {
            var u = p - _tp;
            if (u.x < 4 || u.z < 4 || u.x > _td.size.x - 4 || u.z > _td.size.z - 4) return false;
            float h = Ground(p); if (h < 0.45f) return false;
            if (InWater(p) || WaterDistance(p, 1.5f) < 1.5f) return false;
            if (Steep(p) > 30f) return false;
            var k = Cell8(p);
            for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
                    if (_treeGrid.TryGetValue(new Vector2Int(k.x + dx, k.y + dz), out var l)) foreach (var t in l) { var d = t - p; d.y = 0; if (d.sqrMagnitude < 1.4f * 1.4f) return false; }
            var camp = Marker("ZONE_Camp"); if (camp != Vector3.zero && Flat(p - camp).magnitude < 7f) return false;   // keep the camp site clear for building
            if (InLoc("old_camp", p, 0.6f) || InLoc("nest", p, 0.5f)) return false;                                        // ENV's story spots stay readable
            var c = new Vector3(p.x, h + r + 0.15f, p.z);
            int n = Physics.OverlapSphereNonAlloc(c, r + 0.1f, _ov, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++) if (_ov[i] && !(_ov[i] is TerrainCollider)) return false;
            return true;
        }

        static int Hash(string s) { unchecked { int h = 23; foreach (var ch in s) h = h * 31 + ch; return h & 0x7fffffff; } }

        static string RawSaveId(Component c)
        {
            var so = new SerializedObject(c); var sp = so.FindProperty("saveId"); return sp != null ? sp.stringValue : null;
        }

        // ------------------------------------------------------------------ 2. convert what already looks like a resource
        static void Assign(ResourceNode n, ResourceDefinition def, string idIfEmpty, bool radiusFromBounds)
        {
            n.definition = def;
            string id = RawSaveId(n); if (string.IsNullOrEmpty(id)) { n.SaveId = idIfEmpty; id = idIfEmpty; }
            n.charges = def.RollAmount(Hash(id));
            if (radiusFromBounds) { var b = Bounds(n.gameObject); n.radius = Mathf.Max(def.radius * 0.6f, Mathf.Min(b.extents.x, b.extents.z) * 0.9f); }
            n.displayName = def.displayName; n.verb = def.prompt; n.yieldItem = def.item; n.yieldPerHit = 1;
            n.requiredTool = def.requiredTool; n.fasterTool = def.bestTool; n.handAction = def.handAction; n.toolAction = def.toolAction;
            n.regrowHours = def.respawnHours; n.hideWhenEmpty = def.depletedLook == DepletedLook.Hide;
            EditorUtility.SetDirty(n);
            if (PrefabUtility.IsPartOfPrefabInstance(n)) PrefabUtility.RecordPrefabInstancePropertyModifications(n);
        }

        static void ConvertExisting(ResourceDatabase db)
        {
            var counts = new Dictionary<string, int>();
            void Count(string k) => counts[k] = counts.TryGetValue(k, out var c) ? c + 1 : 1;
            var world = GameObject.Find("World");
            if (!world) { L("ERROR World root missing"); return; }
            // World/Resources (+ the cave stones in [Gameplay])
            var res = new List<Transform>();
            var rg = PrimalFrontier.Core.SceneRoots.Legacy("World/Resources"); if (rg) foreach (Transform t in rg) res.Add(t);
            var cave = PrimalFrontier.Core.SceneRoots.LegacyObject("[Gameplay]/Cave"); if (cave) foreach (Transform t in cave.transform) res.Add(t);
            foreach (var t in res)
            {
                var n = t.GetComponent<ResourceNode>(); if (!n) continue;
                string def = t.name.Contains("RES_Stone") ? "stone_small" : t.name.Contains("RES_Wood") ? "wood_driftwood" : t.name.Contains("RES_Fiber") ? "fiber_plant" : t.name.Contains("RES_BerryPlant") ? "food_berry_bush" : null;
                var d = def != null ? db.Get(def) : null; if (!d) continue;
                Assign(n, d, $"res_{def}_{Hash(t.name + t.position):x}", true); Count(def);
            }
            // rocks: small (decorative until now) -> Stone, medium -> Large rock, large -> Boulder. ENV owns and re-snaps the rocks; a rock
            // standing in deep water (river bed, lagoon) keeps its node switched off: scenery, not a prompt under water
            var rocks = PrimalFrontier.Core.SceneRoots.Legacy("World/Rocks");
            int underwater = 0;
            if (rocks)
            {
                var list = rocks.Cast<Transform>().OrderBy(t => t.position.x).ThenBy(t => t.position.z).ToList();
                int small = 0;
                foreach (var t in list)
                {
                    string def = t.name.Contains("Rock_Small") ? "stone_medium" : t.name.Contains("Rock_Medium") ? "stone_large" : t.name.Contains("Rock_Large") ? "stone_boulder" : null;
                    var d = def != null ? db.Get(def) : null; if (!d) continue;
                    var n = t.GetComponent<ResourceNode>();
                    if (!n) n = t.gameObject.AddComponent<ResourceNode>();
                    Assign(n, d, def == "stone_medium" ? $"rock_small_{small}" : $"rock_{Hash(t.name + t.position):x}", true);
                    if (def == "stone_medium") small++;
                    bool deep = _water.TryGetValue(Cell1(t.position), out var wy) && wy - Ground(t.position) > 0.4f;
                    if (n.enabled == deep) { n.enabled = !deep; EditorUtility.SetDirty(n); if (PrefabUtility.IsPartOfPrefabInstance(n)) PrefabUtility.RecordPrefabInstancePropertyModifications(n); }
                    if (deep) underwater++;
                    Count(def);
                }
            }
            if (underwater > 0) L($"rocks standing in deep water: {underwater} (node switched off, scenery)");
            // fallen logs -> deadwood
            var veg = PrimalFrontier.Core.SceneRoots.Legacy("World/Vegetation");
            if (veg)
            {
                int i = 0;
                foreach (var t in veg.Cast<Transform>().Where(t => t.name.Contains("FallenLog")).OrderBy(t => t.position.x).ThenBy(t => t.position.z))
                {
                    var n = t.GetComponent<ResourceNode>(); if (!n) n = t.gameObject.AddComponent<ResourceNode>();
                    Assign(n, db.Get("wood_deadwood"), $"deadwood_{i++}", true); Count("wood_deadwood");
                }
            }
            // berry bushes (walk-through bushes with the Berries variant): definition + a visible crop on the shaking Visual
            var berryMesh = BerryMesh(_log); var berryMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Berry.mat");
            int crops = 0;
            foreach (var bi in Object.FindObjectsByType<BushInteraction>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var n = bi.GetComponent<ResourceNode>(); if (!n || bi.variant != BushVariant.Berries) continue;
                Assign(n, db.Get("food_berry_bush"), $"bush_berry_{Hash(bi.name + bi.transform.position):x}", false); Count("food_berry_bush (bush)");
                if (berryMesh && berryMat && AddCrop(bi, berryMesh, berryMat)) crops++;
            }
            L($"converted: {string.Join(", ", counts.OrderBy(k => k.Key).Select(k => k.Key + " " + k.Value))}; berry crops added {crops}");
        }

        static bool AddCrop(BushInteraction bi, Mesh mesh, Material mat)
        {
            var vis = bi.visual ? bi.visual : bi.transform.Find("Visual"); if (!vis) return false;
            var old = vis.Find("Berries"); if (old) Object.DestroyImmediate(old.gameObject);
            var rs = vis.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) return false;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            var holder = new GameObject("Berries").transform; holder.SetParent(vis, false);
            var rng = new System.Random(Hash(bi.name));
            for (int i = 0; i < 5; i++)
            {
                float a = (i * 72f + (float)rng.NextDouble() * 30f) * Mathf.Deg2Rad, el = (18f + (float)rng.NextDouble() * 32f) * Mathf.Deg2Rad;
                var p = b.center + new Vector3(Mathf.Cos(el) * Mathf.Cos(a) * b.extents.x * 0.8f, Mathf.Sin(el) * b.extents.y * 0.75f - b.extents.y * 0.1f, Mathf.Cos(el) * Mathf.Sin(a) * b.extents.z * 0.8f);
                var go = new GameObject("Berries_" + i); go.transform.SetParent(holder, true);
                go.transform.position = p; go.transform.rotation = Quaternion.Euler((float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f, 0f);
                go.transform.localScale = Vector3.one * (1.3f + (float)rng.NextDouble() * 0.5f) / Mathf.Max(0.01f, vis.lossyScale.x);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return true;
        }

        // ------------------------------------------------------------------ 3. new clusters
        static readonly Dictionary<Biome, (string prefab, float w)[]> Recipes = new Dictionary<Biome, (string, float)[]>
        {
            [Biome.Beach] = new[] { ("Resource_Wood_Driftwood", 3f), ("Resource_Stone_Small", 3f), ("Resource_Wood_Branch", 1f), ("Resource_Food_FallenFruit", 1f), ("Resource_Fiber_LongGrass", 1.5f) },
            [Biome.Meadow] = new[] { ("Resource_Fiber_LongGrass", 4f), ("Resource_Fiber_Plant", 2.5f), ("Resource_Food_BerryBush", 2f), ("Resource_Food_EdiblePlant", 1.2f), ("Resource_Stone_Small", 0.8f), ("Resource_Wood_Branch", 0.8f) },
            [Biome.ForestEdge] = new[] { ("Resource_Fiber_Plant", 3f), ("Resource_Wood_Branch", 3f), ("Resource_Stone_Small", 2f), ("Resource_Food_BerryBush", 2f), ("Resource_Fiber_Fern", 1f), ("Resource_Fiber_SmallBush", 1f), ("Resource_Food_EdiblePlant", 1.5f) },
            [Biome.Forest] = new[] { ("Resource_Fiber_Fern", 3f), ("Resource_Wood_Branch", 3f), ("Resource_Wood_SmallLog", 2f), ("Resource_Stone_Medium", 1f), ("Resource_Fiber_SmallBush", 2f), ("Resource_Food_FallenFruit", 0.7f) },
            [Biome.Rocky] = new[] { ("Resource_Stone_Small", 4f), ("Resource_Stone_Medium", 3f), ("Resource_Fiber_LongGrass", 1f) },
            [Biome.WaterEdge] = new[] { ("Resource_Fiber_LongGrass", 3f), ("Resource_Stone_Small", 2f), ("Resource_Fiber_Plant", 1f), ("Resource_Food_EdiblePlant", 2f) },
            // the new terrain (directive 23-25): river banks stones + reeds + fibre + food; the pool stones, ferns and moss-side fibre; the
            // wetland reeds, fibre and edible plants; the canyon much stone and little food; the ridge stone and deadwood
            [Biome.RiverBank] = new[] { ("Resource_Stone_Small", 3f), ("Resource_Stone_Medium", 1.5f), ("Resource_Fiber_LongGrass", 3f), ("Resource_Fiber_Plant", 1.5f), ("Resource_Food_EdiblePlant", 1.5f), ("Resource_Wood_Driftwood", 1f) },
            [Biome.WaterfallPool] = new[] { ("Resource_Stone_Small", 3f), ("Resource_Stone_Medium", 2f), ("Resource_Fiber_Fern", 2f), ("Resource_Fiber_Plant", 1f), ("Resource_Food_EdiblePlant", 1f) },
            [Biome.Wetland] = new[] { ("Resource_Fiber_LongGrass", 4f), ("Resource_Fiber_Plant", 2f), ("Resource_Food_EdiblePlant", 3f), ("Resource_Stone_Small", 1f), ("Resource_Wood_Driftwood", 0.6f) },
            [Biome.Canyon] = new[] { ("Resource_Stone_Small", 4f), ("Resource_Stone_Medium", 4f), ("Resource_Fiber_Fern", 0.5f), ("Resource_Food_EdiblePlant", 0.3f) },
            [Biome.Ridge] = new[] { ("Resource_Stone_Small", 4f), ("Resource_Stone_Medium", 3f), ("Resource_Wood_SmallLog", 1.5f), ("Resource_Wood_Branch", 1f), ("Resource_Fiber_LongGrass", 0.5f) },
        };

        static string Variant(string prefab, System.Random rng)
        {
            if (prefab == "Resource_Wood_Branch" && rng.NextDouble() < 0.45) return "Resource_Wood_Branch_02";
            if (prefab == "Resource_Stone_Medium" && rng.NextDouble() < 0.5) return "Resource_Stone_Medium_02";
            return prefab;
        }

        static string Pick((string prefab, float w)[] recipe, System.Random rng)
        {
            var avail = recipe.Where(r => NodePrefab(r.prefab)).ToArray(); if (avail.Length == 0) return null;
            float total = avail.Sum(r => r.w), x = (float)rng.NextDouble() * total;
            foreach (var r in avail) { x -= r.w; if (x <= 0f) return r.prefab; }
            return avail[avail.Length - 1].prefab;
        }

        static Transform _root;

        static GameObject Place(string prefabName, Vector3 near, float minR, float maxR, System.Random rng, Transform parent, string tag)
        {
            var prefab = NodePrefab(Variant(prefabName, rng)); if (!prefab) return null;
            var node0 = prefab.GetComponent<ResourceNode>(); var def = node0 ? node0.definition : null; if (!def) return null;
            float scale = 0.85f + (float)rng.NextDouble() * 0.35f;
            float r = Mathf.Max(0.35f, node0.radius * scale);
            for (int attempt = 0; attempt < 14; attempt++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, d = minR + (float)rng.NextDouble() * (maxR - minR);
                var p = near + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                if (!Free(p, r)) continue;
                p.y = Ground(p) - (def.category == ResourceCategory.Stone ? 0.04f : 0.02f);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                var n = Vector3.Slerp(Vector3.up, Normal(p), def.category == ResourceCategory.Fiber ? 0.3f : 0.7f);
                go.transform.SetPositionAndRotation(p, Quaternion.FromToRotation(Vector3.up, n) * Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
                go.transform.localScale = Vector3.one * scale;
                var node = go.GetComponent<ResourceNode>();
                string id = $"rn_{def.id}_{_placed:0000}";
                node.SaveId = id; node.charges = def.RollAmount(Hash(id)); node.radius = node0.radius * scale;
                PrefabUtility.RecordPrefabInstancePropertyModifications(node);
                PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
                go.name = $"{prefab.name}_{_placed:0000}";
                Physics.SyncTransforms();
                _placed++; _placedBy[tag] = _placedBy.TryGetValue(tag, out var c) ? c + 1 : 1;
                return go;
            }
            return null;
        }

        /// <summary>a fish shoal on the bed of shallow fresh water (20-120 cm deep, 2 m of water around it): reached by wading in</summary>
        static bool PlaceFish(GameObject prefab, Vector3 at, System.Random rng, Transform parent)
        {
            var c = Cell1(at); if (!_water.TryGetValue(c, out var wy)) return false;
            for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++) if (!_water.ContainsKey(new Vector2Int(c.x + dx, c.y + dz))) return false;
            float g = Ground(at), depth = wy - g; if (depth < 0.15f || depth > 1.5f) return false;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.SetPositionAndRotation(new Vector3(at.x, g + 0.05f, at.z), Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
            var node = go.GetComponent<ResourceNode>(); var def = node.definition;
            string id = $"rn_{def.id}_{_placed:0000}";
            node.SaveId = id; node.charges = def.RollAmount(Hash(id)); node.cueHeight = depth - 0.05f;       // the ripple cue plays on the surface
            PrefabUtility.RecordPrefabInstancePropertyModifications(node); PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
            go.name = $"{prefab.name}_{_placed:0000}";
            _placed++; _placedBy["fish"] = _placedBy.TryGetValue("fish", out var n) ? n + 1 : 1;
            return true;
        }

        static void PlaceNew(ResourceDatabase db)
        {
            var old = GameObject.Find("[Resources]"); if (old) Object.DestroyImmediate(old);
            Physics.SyncTransforms();
            _root = new GameObject("[Resources]").transform;
            var rp = PrimalFrontier.Core.SceneRoots.LegacyParent("[Resources]", true); if (rp) _root.SetParent(rp, false);   // World/Gameplay/Resources (HIER)
            _placed = 0; _placedBy.Clear();
            var rng = new System.Random(Seed);
            var spawnT = GameObject.Find("ZONE_PlayerSpawn"); Vector3 spawn = spawnT ? spawnT.transform.position : Vector3.zero;
            var usedCenters = new List<Vector3>();

            // --- start area: stone, wood, fibre and food within 40 m of the spawn, in five small groups
            var start = new GameObject("StartArea").transform; start.SetParent(_root);
            // the first thing in reach: loose stones a few steps from where the survivor wakes, on their own (no pickup or node within 3.5 m)
            var others = Object.FindObjectsByType<Interactable>(FindObjectsInactive.Include, FindObjectsSortMode.None).Select(x => x.transform.position).ToList();
            GameObject firstStone = null;
            for (int i = 0; i < 80 && !firstStone; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, d = 4.5f + (float)rng.NextDouble() * 2.5f;
                var p = spawn + new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d);
                if (Ground(p) < 0.8f || others.Any(o => Flat(o - p).magnitude < 3.5f)) continue;
                firstStone = Place("Resource_Stone_Small", p, 0f, 0.01f, rng, start, "start area");
            }
            L(firstStone ? $"first stones {Flat(firstStone.transform.position - spawn).magnitude:F1} m from the spawn" : "WARNING no free spot for the first stones near the spawn");
            var centers = new List<Vector3>();
            for (int i = 0; i < 600 && centers.Count < 6; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, d = 13.5f + (float)rng.NextDouble() * 24f;
                var p = spawn + new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d);
                if (Ground(p) < 0.9f || !Free(p, 1.2f)) continue;
                if (centers.Any(c => Flat(c - p).magnitude < 11f)) continue;
                centers.Add(p);
            }
            var quota = new List<string>();
            void Q(string p, int n) { for (int i = 0; i < n; i++) quota.Add(p); }
            Q("Resource_Stone_Small", 4); Q("Resource_Wood_Branch", 4); Q("Resource_Wood_Driftwood", 1); Q("Resource_Fiber_LongGrass", 3); Q("Resource_Fiber_Plant", 2);
            Q("Resource_Food_FallenFruit", 2); Q("Resource_Food_BerryBush", 1); Q("Resource_Stone_Medium", 1);
            int qi = 0;
            foreach (var item in quota)
            {
                if (centers.Count == 0) break;
                var c = centers[qi++ % centers.Count];
                if (!Place(item, c, 0.8f, 3.8f, rng, start, "start area")) Place(item, spawn, 8f, 40f, rng, start, "start area");
            }
            usedCenters.AddRange(centers);
            L($"start area: {centers.Count} groups around the spawn {spawn}, {(_placedBy.TryGetValue("start area", out var sa) ? sa : 0)} nodes");

            // --- trails to fresh water: beach -> camp -> fibre meadow -> pond edge, and beach -> the river mouth / lagoon; a small cluster every ~22 m
            var trail = new GameObject("TrailToWater").transform; trail.SetParent(_root);
            var pond = Marker("WATER_Pond"); if (pond == Vector3.zero) pond = Marker("ZONE_Pond");
            var pts = new List<Vector3> { spawn };
            foreach (var m in new[] { "ZONE_Camp", "RESAREA_Fiber" }) { var q = Marker(m); if (q != Vector3.zero) pts.Add(q); }
            if (pond != Vector3.zero) { var last = pts[pts.Count - 1]; pts.Add(pond + Flat(last - pond).normalized * 27f); }
            int trailClusters = Trail(pts, "Pond", trail, rng, usedCenters, 40f);
            var trails = new List<string> { $"pond: {pts.Count} waypoints ({string.Join(" -> ", pts.Select(V))})" };
            if (Loc("wetland", out var wet, out var wetR))
            {
                // the river mouth crosses the +x end of the start beach: the nearest fresh water for a survivor who follows the shore
                var mouth = wet + Flat(spawn - wet).normalized * (wetR + 6f);
                var pts2 = new List<Vector3> { spawn, Vector3.Lerp(spawn, mouth, 0.5f) + Vector3.Cross(Vector3.up, Flat(mouth - spawn).normalized) * -6f, mouth };
                trailClusters += Trail(pts2, "Mouth", trail, rng, usedCenters, 30f);
                trails.Add($"river mouth: {string.Join(" -> ", pts2.Select(V))}");
            }
            L($"trails to fresh water: {string.Join("; ", trails)}; {trailClusters} clusters, {(_placedBy.TryGetValue("trail", out var tn) ? tn : 0)} nodes");

            // --- fallen fruit under each fruit tree (food without climbing)
            var fruitRoot = new GameObject("FruitTreeDrops").transform; fruitRoot.SetParent(_root);
            foreach (var cl in Object.FindObjectsByType<Climbable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (cl.GetComponentsInChildren<FruitCluster>(true).Length > 0) Place("Resource_Food_FallenFruit", cl.transform.position, 2.2f, 3.6f, rng, fruitRoot, "fruit drops");

            // --- fresh-water banks: reeds, stones, edible plants on the bank, fish in the shallows (every ~24 m of shore)
            var waterRoot = new GameObject("WaterEdge").transform; waterRoot.SetParent(_root);
            var shore = _water.Keys.Where(c => !_water.ContainsKey(c + Vector2Int.right) || !_water.ContainsKey(c + Vector2Int.left) || !_water.ContainsKey(c + Vector2Int.up) || !_water.ContainsKey(c + Vector2Int.down))
                              .OrderBy(c => c.x).ThenBy(c => c.y).ToList();
            var picks = new List<(Vector3 p, Vector3 land)>();   // shore spots ~20 m apart
            foreach (var c in shore.OrderBy(_ => rng.Next()))
            {
                var p = new Vector3(c.x + 0.5f, 0f, c.y + 0.5f);
                if (picks.Any(q => Flat(q.p - p).magnitude < 20f)) continue;
                var land = Vector3.zero;
                for (int dx = -2; dx <= 2; dx++) for (int dz = -2; dz <= 2; dz++) if (!_water.ContainsKey(new Vector2Int(c.x + dx, c.y + dz))) land += new Vector3(dx, 0, dz);
                if (land.sqrMagnitude < 0.01f) continue;
                picks.Add((p, land.normalized));
            }
            int banks = 0, fish = 0; var fishPrefab = NodePrefab("Resource_Fish_Shoal"); var bankBiomes = new Dictionary<Biome, int>();
            for (int i = 0; i < picks.Count; i++)
            {
                var (p, land) = picks[i];
                var bank = p + land * 3.5f;
                var biome = BiomeAt(bank);
                if (biome != Biome.RiverBank && biome != Biome.Wetland && biome != Biome.WaterfallPool) biome = Biome.WaterEdge;
                var grp = new GameObject($"Bank_{banks:00}_{biome}").transform; grp.SetParent(waterRoot);
                int got = 0, n = biome == Biome.Wetland ? 3 + rng.Next(2) : 2 + rng.Next(2);
                for (int k = 0; k < n; k++) if (Place(Pick(Recipes[biome], rng), bank, k == 0 ? 0f : 0.8f, 3.5f, rng, grp, "water edge")) got++;
                if (got == 0) Object.DestroyImmediate(grp.gameObject); else { banks++; usedCenters.Add(bank); bankBiomes[biome] = bankBiomes.TryGetValue(biome, out var bb) ? bb + 1 : 1; }
                // fish: every second bank, every bank in the wetland and at the pool, at least three on the island
                bool wantFish = i % 2 == 0 || fish < 3 || biome == Biome.Wetland || biome == Biome.WaterfallPool;
                if (fishPrefab && wantFish) { for (float d = 2f; d <= 6f; d += 1f) if (PlaceFish(fishPrefab, p - land * d, rng, waterRoot)) { fish++; break; } }
            }
            L($"water edges: {picks.Count} shore spots, {banks} bank clusters ({string.Join(", ", bankBiomes.Select(k => k.Key + " " + k.Value))}; {(_placedBy.TryGetValue("water edge", out var we) ? we : 0)} nodes), {fish} fish shoals{(fishPrefab ? "" : " (no raw_fish item / model yet)")}");

            // --- the new biomes (ENV terrain v2): clusters inside each location, denser than the island grid
            var zones = new GameObject("Biomes").transform; zones.SetParent(_root);
            foreach (var (id, count, minN, maxN) in new[] { ("meadow", 12, 2, 4), ("herbivore_valley", 4, 2, 3), ("canyon", 9, 3, 5), ("ridge", 7, 3, 4), ("wetland", 7, 3, 4), ("waterfall", 4, 2, 3), ("river", 6, 2, 4), ("volcano", 3, 2, 3) })
            {
                if (!Loc(id, out var c0, out var r0)) continue;
                var zroot = new GameObject("Zone_" + id).transform; zroot.SetParent(zones);
                int made = 0, nodes0 = _placed;
                for (int attempt = 0; attempt < count * 14 && made < count; attempt++)
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f, d = (float)Mathf.Sqrt((float)rng.NextDouble()) * r0 * 0.92f;
                    var p = c0 + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                    if (Ground(p) < 0.8f || usedCenters.Any(u => Flat(u - p).magnitude < 14f) || !Free(p, 1.0f)) continue;
                    var biome = BiomeAt(p);
                    var grp = new GameObject($"{id}_{made:00}_{biome}").transform; grp.SetParent(zroot);
                    int n = minN + rng.Next(maxN - minN + 1), got = 0;
                    for (int i = 0; i < n; i++) if (Place(Pick(Recipes[biome], rng), p, i == 0 ? 0f : 1.2f, 4.5f, rng, grp, "biomes")) got++;
                    if (got == 0) { Object.DestroyImmediate(grp.gameObject); continue; }
                    made++; usedCenters.Add(p);
                }
                L($"biome {id}: {made} clusters, {_placed - nodes0} nodes (r {r0:F0} m at {V(c0)})");
            }

            // --- rare nodes: bones + one hide at the kill site and in the predator territory, shipwreck scraps along the shore
            PlaceRare(rng, usedCenters);

            // --- the rest of the island: one small cluster per ~34 m cell, by biome, empty ground between
            var island = new GameObject("Island").transform; island.SetParent(_root);
            const float S = 34f; int clusters = 0; var biomeCount = new Dictionary<Biome, int>();
            for (float gx = _tp.x + S * 0.5f; gx < _tp.x + _td.size.x; gx += S)
                for (float gz = _tp.z + S * 0.5f; gz < _tp.z + _td.size.z; gz += S)
                {
                    var p = new Vector3(gx + ((float)rng.NextDouble() - 0.5f) * S * 0.5f, 0f, gz + ((float)rng.NextDouble() - 0.5f) * S * 0.5f);
                    if (Ground(p) < 0.8f) continue;
                    if (Flat(p - spawn).magnitude < 45f || usedCenters.Any(c => Flat(c - p).magnitude < 16f)) continue;
                    if (!Free(p, 1.0f)) continue;
                    var biome = BiomeAt(p);
                    int n = biome == Biome.Rocky ? 3 + rng.Next(2) : 2 + rng.Next(2);
                    var grp = new GameObject($"Cluster_{clusters:000}_{biome}").transform; grp.SetParent(island);
                    int got = 0;
                    for (int i = 0; i < n; i++) if (Place(Pick(Recipes[biome], rng), p, i == 0 ? 0f : 1.2f, 4.5f, rng, grp, "island")) got++;
                    if (got == 0) { Object.DestroyImmediate(grp.gameObject); continue; }
                    clusters++; biomeCount[biome] = biomeCount.TryGetValue(biome, out var bc) ? bc + 1 : 1;
                }
            L($"island clusters: {clusters} ({string.Join(", ", biomeCount.Select(k => k.Key + " " + k.Value))}), {(_placedBy.TryGetValue("island", out var inn) ? inn : 0)} nodes");
            L($"new nodes total {_placed} under [Resources]");
        }

        /// <summary>small clusters every ~22 m along a polyline, alternating sides; the first node is always something useful (stone / fibre / food)</summary>
        static int Trail(List<Vector3> pts, string name, Transform parent, System.Random rng, List<Vector3> usedCenters, float firstAt)
        {
            float along = 0f, next = firstAt; int side = 1, made = 0;
            for (int s = 0; s + 1 < pts.Count; s++)
            {
                var a = pts[s]; var b = pts[s + 1]; float len = Flat(b - a).magnitude; if (len < 0.5f) continue;
                var dir = Flat(b - a).normalized; var perp = Vector3.Cross(Vector3.up, dir);
                while (next <= along + len)
                {
                    var p = a + dir * (next - along) + perp * side * (4f + (float)rng.NextDouble() * 3f); side = -side; next += 22f;
                    if (Ground(p) < 0.8f) continue;
                    var biome = BiomeAt(p); int n = 2 + rng.Next(2); int got = 0;
                    var grp = new GameObject($"Trail{name}_{made:00}_{biome}").transform; grp.SetParent(parent);
                    string first = made % 3 == 0 ? "Resource_Stone_Small" : made % 3 == 1 ? "Resource_Fiber_Plant" : "Resource_Food_BerryBush";
                    if (Place(first, p, 0.5f, 3f, rng, grp, "trail")) got++;
                    for (int i = 1; i < n; i++) if (Place(Pick(Recipes[biome], rng), p, 0.8f, 4f, rng, grp, "trail")) got++;
                    if (got == 0) Object.DestroyImmediate(grp.gameObject); else { usedCenters.Add(p); made++; }
                }
                along += len;
            }
            return made;
        }

        /// <summary>rare nodes (directive 23-25). Item ids come from SURV: a missing item or model skips that kind with a log line.</summary>
        static void PlaceRare(System.Random rng, List<Vector3> usedCenters)
        {
            var root = new GameObject("Rare").transform; root.SetParent(_root);
            int n0 = _placed;
            // bones: 4 piles within 5-16 m of the skeleton, 4 more in the predator territory, 1 along the theropod trail; one torn hide at the kill site
            var bones = NodePrefab("Resource_Rare_Bones"); var hide = NodePrefab("Resource_Rare_Hide");
            bool kill = Prop("env_giant_skeleton", "predator_territory", out var skel);
            if (!bones) L("rare bones: skipped (no 'bone' item model and no PROP_PC_Bones prefab yet: run ENV's Story step, then this builder again)");
            else if (!kill) L("rare bones: skipped (no kill site and no 'predator_territory' location)");
            else
            {
                int b = 0;
                for (int i = 0; i < 4; i++) if (Place("Resource_Rare_Bones", skel, 5f, 16f, rng, root, "rare bones")) b++;
                if (Loc("predator_territory", out var pt, out var pr)) for (int i = 0; i < 4; i++) if (Place("Resource_Rare_Bones", pt, 8f, pr * 0.9f, rng, root, "rare bones")) b++;
                if (Prop("env_theropod_trail", null, out var trail)) if (Place("Resource_Rare_Bones", trail, 3f, 8f, rng, root, "rare bones")) b++;
                L($"rare bones: {b} piles around the kill site {V(skel)} and the predator territory");
            }
            if (!hide) L("rare hide: skipped (item 'hide' missing or without a world model)");
            else if (kill) L(Place("Resource_Rare_Hide", skel, 3f, 9f, rng, root, "rare hide") ? "rare hide: 1 at the kill site" : "rare hide: no free spot at the kill site");
            // shipwreck scraps: at the wreck remains along the shore (ENV's WreckRemains pieces), else spread along the dry sand of the beach; 2 at the wreck itself
            var scraps = NodePrefab("Resource_Rare_WreckScraps");
            if (!scraps) L("rare wreck scraps: skipped (SURV's 'wreck_scraps' item has no world model yet and PROP_PC_WreckPlanks is not built: run SURV's builder S5 / ENV's Story, then this builder again)");
            else
            {
                int sc = 0; var spots = new List<Vector3>();
                var remains = PrimalFrontier.Core.SceneRoots.LegacyObject("World/Environment/Storytelling/WreckRemains");
                if (remains) foreach (Transform t in remains.transform) spots.Add(t.position);
                if (spots.Count == 0 && Loc("beach", out var bc, out var br))
                    for (int i = 0; i < 8; i++) { float a = (float)rng.NextDouble() * Mathf.PI * 2f; spots.Add(bc + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * br * (0.5f + 0.45f * (float)rng.NextDouble())); }
                spots = spots.Where(q => Ground(q) > 0.6f && Ground(q) < 6f && Splat(q).Contains("Sand")).OrderBy(_ => rng.Next()).Take(6).ToList();
                foreach (var q in spots) if (Place("Resource_Rare_WreckScraps", q, 1.5f, 3.5f, rng, root, "rare scraps")) sc++;
                if (Loc("shipwreck", out var wreck, out var wr)) for (int i = 0; i < 2; i++) if (Place("Resource_Rare_WreckScraps", wreck, 7f, Mathf.Max(9f, wr * 0.6f), rng, root, "rare scraps")) sc++;
                L($"rare wreck scraps: {sc} piles ({(remains ? "at ENV's wreck remains" : "along the beach sand")} + the wreck)");
            }
            L($"rare nodes: {_placed - n0}");
        }

        /// <summary>
        /// nodes that are not rebuilt (World/Resources, the cave stones, Markers/ResourceAreas) sit on the ground of the current terrain;
        /// one that ended up in water (or below the sea) moves to the nearest dry spot within 10 m, else it is switched off (logged)
        /// </summary>
        static void ResnapExisting()
        {
            int snapped = 0, moved = 0, off = 0, marks = 0; var log = new List<string>();
            var world = GameObject.Find("World");
            var list = new List<Transform>();
            var rg = PrimalFrontier.Core.SceneRoots.Legacy("World/Resources"); if (rg) foreach (Transform t in rg) list.Add(t);
            var cave = PrimalFrontier.Core.SceneRoots.LegacyObject("[Gameplay]/Cave"); if (cave) foreach (Transform t in cave.transform) list.Add(t);
            foreach (var t in list)
            {
                var n = t.GetComponent<ResourceNode>(); if (!n) continue;
                var p = t.position; float g = Ground(p);
                bool wet = InWater(p) || g < 0.45f;
                if (wet)
                {
                    bool found = false;
                    for (float r = 2f; r <= 10f && !found; r += 2f)
                        for (int k = 0; k < 12 && !found; k++)
                        {
                            float a = k / 12f * Mathf.PI * 2f; var q = p + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                            if (!Free(q, Mathf.Max(0.3f, n.radius))) continue;
                            q.y = Ground(q) - 0.02f; t.position = q; found = true; moved++; log.Add($"moved {t.name} {V(p)} -> {V(q)}");
                        }
                    if (!found) { t.gameObject.SetActive(false); off++; log.Add($"OFF {t.name} {V(p)} (in water, no dry spot within 10 m)"); }
                }
                else if (Mathf.Abs(p.y - g) > 0.03f) { t.position = new Vector3(p.x, g - 0.02f, p.z); snapped++; }
                EditorUtility.SetDirty(t);
                if (PrefabUtility.IsPartOfPrefabInstance(t)) PrefabUtility.RecordPrefabInstancePropertyModifications(t);
            }
            var areas = GameObject.Find("Markers/ResourceAreas");
            if (areas) foreach (Transform m in areas.transform) { var p = m.position; float g = Ground(p); if (Mathf.Abs(p.y - g) > 0.03f) { m.position = new Vector3(p.x, g, p.z); marks++; EditorUtility.SetDirty(m); } }
            Physics.SyncTransforms();
            L($"re-snap of kept nodes: {list.Count} checked, {snapped} snapped to the ground, {moved} moved out of water, {off} switched off, {marks} RESAREA markers snapped");
            foreach (var l in log.Take(30)) L("   " + l);
        }

        /// <summary>after the build: no node buried or floating (|y - ground| over 0.6 m), none in water unless it is a fish shoal</summary>
        static void Verify()
        {
            var all = Object.FindObjectsByType<ResourceNode>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(n => n.enabled).ToArray();
            int offGround = 0, inWater = 0, belowSea = 0; var bad = new List<string>();
            foreach (var n in all)
            {
                if (!n.enabled) continue;
                var p = n.transform.position; float g = Ground(p);
                bool fish = n.definition && n.definition.category == ResourceCategory.Fish;
                if (Mathf.Abs(p.y - g) > 0.6f) { offGround++; if (bad.Count < 40) bad.Add($"off ground {n.name} y {p.y:F2} ground {g:F2}"); }
                if (!fish && InWater(p)) { inWater++; if (bad.Count < 40) bad.Add($"in water {n.name} {V(p)}"); }
                if (!fish && g < 0.3f) { belowSea++; if (bad.Count < 40) bad.Add($"below sea {n.name} {V(p)}"); }
            }
            L($"VERIFY {all.Length} active nodes: off the ground {offGround}, in fresh water (not fish) {inWater}, below sea level {belowSea}");
            foreach (var b in bad) L("   " + b);
        }

        static void WireSystems(ResourceDatabase db)
        {
            var th = Object.FindFirstObjectByType<TreeHarvest>();
            if (th) { th.definition = db.Get("wood_tree"); EditorUtility.SetDirty(th); L($"TreeHarvest '{th.name}': definition wood_tree"); }
            else L("TreeHarvest not in the scene (GameManager creates it at runtime and loads wood_tree from the database)");
            int f = 0;
            foreach (var cl in Object.FindObjectsByType<Climbable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                int i = 0;
                foreach (var fc in cl.GetComponentsInChildren<FruitCluster>(true)) { fc.SaveId = $"fruit_{cl.name}_{i++}"; EditorUtility.SetDirty(fc); if (PrefabUtility.IsPartOfPrefabInstance(fc)) PrefabUtility.RecordPrefabInstancePropertyModifications(fc); f++; }
            }
            L($"fruit clusters with save ids: {f}");
        }

        static void Report()
        {
            var spawnT = GameObject.Find("ZONE_PlayerSpawn"); Vector3 spawn = spawnT ? spawnT.transform.position : Vector3.zero;
            var all = Object.FindObjectsByType<ResourceNode>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            L($"ResourceNodes in the scene: {all.Length} ({all.Count(n => !n.definition)} without a definition)");
            foreach (var g in all.GroupBy(n => n.definition ? n.definition.id : "legacy " + n.displayName).OrderBy(g => g.Key))
                L($"   {g.Key}: {g.Count()} (within 40 m of the spawn {g.Count(n => Flat(n.transform.position - spawn).magnitude < 40f)}, 80 m {g.Count(n => Flat(n.transform.position - spawn).magnitude < 80f)})");
            foreach (var cat in new[] { ResourceCategory.Stone, ResourceCategory.Wood, ResourceCategory.Fiber, ResourceCategory.Food })
            {
                var near = all.Where(n => n.definition && n.definition.category == cat && Flat(n.transform.position - spawn).magnitude < 40f).ToList();
                L($"start area (40 m) {cat}: {near.Count} nodes, {near.Sum(n => n.charges)} units");
            }
            foreach (var id in new[] { "meadow", "canyon", "ridge", "wetland", "waterfall", "river", "predator_territory", "beach" })
            {
                if (!Loc(id, out var c, out var r)) continue;
                var inside = all.Where(n => n.definition && Flat(n.transform.position - c).magnitude <= r).ToList();
                L($"location {id} (r {r:F0}): {inside.Count} nodes: {string.Join(", ", inside.GroupBy(n => n.definition.category).OrderBy(g => g.Key).Select(g => g.Key + " " + g.Count()))}");
            }
        }
    }
}
