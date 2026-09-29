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
    /// outline and never touches item data (unlike PrimalGameplayBuilder):
    ///  1. data: definitions, tools, database, node prefabs (BuildData);
    ///  2. converts what already looks like a resource: stone piles, driftwood, fibre plants, berry plants / bushes (+ a
    ///     visible crop on the bushes), the medium / large rocks (large rock / boulder), the small decorative rocks (now
    ///     Stone nodes) and the fallen logs (deadwood). Cliffs and the cave stay scenery;
    ///  3. rebuilds the [Resources] root: a dense start area around the beach spawn (stone, wood, fibre, food within 40 m),
    ///     a trail of small clusters from the beach past the camp to the pond (the way to fresh water), fallen fruit under
    ///     each fruit tree, and one small cluster per ~34 m cell elsewhere, picked by biome (beach, meadow, forest edge,
    ///     forest, rocky, water edge), with empty ground between clusters.
    /// </summary>
    public static partial class PrimalResourceBuilder
    {
        enum Biome { Beach, Meadow, ForestEdge, Forest, Rocky, WaterEdge }
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
            PlaceNew(db);
            WireSystems(db);
            Report();
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
            _water.Clear();
            foreach (var ws in Object.FindObjectsByType<WaterSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var mf = ws.surface ? ws.surface : ws.GetComponentInChildren<MeshFilter>(); if (!mf || !mf.sharedMesh) continue;
                RasterWater(mf);
            }
            Physics.SyncTransforms();
            L($"terrain {_td.size} at {_tp}, layers {string.Join(",", _layers)}, trees {_td.treeInstanceCount}, fresh water cells {_water.Count}");
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

        static Biome BiomeAt(Vector3 p)
        {
            float h = Ground(p);
            if (WaterDistance(p, 7f) < 7f) return Biome.WaterEdge;
            string layer = Splat(p);
            var rocky = Marker("ZONE_Rocky");
            if (layer.Contains("Rock") || Steep(p) > 24f || (rocky != Vector3.zero && Flat(p - rocky).magnitude < 45f)) return Biome.Rocky;
            if (layer.Contains("Sand") && h < 6f) return Biome.Beach;
            int trees = TreesWithin(p, 12f);
            if (trees >= 6 || layer.Contains("Forest")) return Biome.Forest;
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
            var rg = world.transform.Find("Resources"); if (rg) foreach (Transform t in rg) res.Add(t);
            var cave = GameObject.Find("[Gameplay]/Cave"); if (cave) foreach (Transform t in cave.transform) res.Add(t);
            foreach (var t in res)
            {
                var n = t.GetComponent<ResourceNode>(); if (!n) continue;
                string def = t.name.Contains("RES_Stone") ? "stone_small" : t.name.Contains("RES_Wood") ? "wood_driftwood" : t.name.Contains("RES_Fiber") ? "fiber_plant" : t.name.Contains("RES_BerryPlant") ? "food_berry_bush" : null;
                var d = def != null ? db.Get(def) : null; if (!d) continue;
                Assign(n, d, $"res_{def}_{Hash(t.name + t.position):x}", true); Count(def);
            }
            // rocks: small (decorative until now) -> Stone, medium -> Large rock, large -> Boulder
            var rocks = world.transform.Find("Rocks");
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
                    Count(def);
                }
            }
            // fallen logs -> deadwood
            var veg = world.transform.Find("Vegetation");
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
            [Biome.Meadow] = new[] { ("Resource_Fiber_LongGrass", 4f), ("Resource_Fiber_Plant", 2f), ("Resource_Stone_Small", 2f), ("Resource_Food_BerryBush", 1f), ("Resource_Wood_Branch", 1f), ("Resource_Food_EdiblePlant", 1f) },
            [Biome.ForestEdge] = new[] { ("Resource_Fiber_Plant", 3f), ("Resource_Wood_Branch", 3f), ("Resource_Stone_Small", 2f), ("Resource_Food_BerryBush", 2f), ("Resource_Fiber_Fern", 1f), ("Resource_Fiber_SmallBush", 1f), ("Resource_Food_EdiblePlant", 1.5f) },
            [Biome.Forest] = new[] { ("Resource_Fiber_Fern", 3f), ("Resource_Wood_Branch", 3f), ("Resource_Wood_SmallLog", 2f), ("Resource_Stone_Medium", 1f), ("Resource_Fiber_SmallBush", 2f), ("Resource_Food_FallenFruit", 0.7f) },
            [Biome.Rocky] = new[] { ("Resource_Stone_Small", 4f), ("Resource_Stone_Medium", 3f), ("Resource_Fiber_LongGrass", 1f) },
            [Biome.WaterEdge] = new[] { ("Resource_Fiber_LongGrass", 3f), ("Resource_Stone_Small", 2f), ("Resource_Fiber_Plant", 1f), ("Resource_Food_EdiblePlant", 2f) },
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
            node.SaveId = id; node.charges = def.RollAmount(Hash(id));
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

            // --- the trail to fresh water: beach -> camp -> fibre meadow -> pond edge, a small cluster every ~22 m
            var trail = new GameObject("TrailToWater").transform; trail.SetParent(_root);
            var pond = Marker("WATER_Pond"); if (pond == Vector3.zero) pond = Marker("ZONE_Pond");
            var pts = new List<Vector3> { spawn };
            foreach (var m in new[] { "ZONE_Camp", "RESAREA_Fiber" }) { var q = Marker(m); if (q != Vector3.zero) pts.Add(q); }
            if (pond != Vector3.zero) { var last = pts[pts.Count - 1]; pts.Add(pond + Flat(last - pond).normalized * 27f); }
            float along = 0f, next = 40f; int side = 1, trailClusters = 0;
            for (int s = 0; s + 1 < pts.Count; s++)
            {
                var a = pts[s]; var b = pts[s + 1]; float len = Flat(b - a).magnitude; var dir = Flat(b - a).normalized; var perp = Vector3.Cross(Vector3.up, dir);
                while (next <= along + len)
                {
                    var p = a + dir * (next - along) + perp * side * (4f + (float)rng.NextDouble() * 3f); side = -side; next += 22f;
                    var biome = BiomeAt(p); int n = 2 + rng.Next(2); int got = 0;
                    var grp = new GameObject($"Trail_{trailClusters:00}_{biome}").transform; grp.SetParent(trail);
                    // always something useful on the way: one fibre / stone / food node, then the biome's mix
                    string first = trailClusters % 3 == 0 ? "Resource_Stone_Small" : trailClusters % 3 == 1 ? "Resource_Fiber_Plant" : "Resource_Food_BerryBush";
                    if (Place(first, p, 0.5f, 3f, rng, grp, "trail")) got++;
                    for (int i = 1; i < n; i++) if (Place(Pick(Recipes[biome], rng), p, 0.8f, 4f, rng, grp, "trail")) got++;
                    if (got == 0) Object.DestroyImmediate(grp.gameObject); else { usedCenters.Add(p); trailClusters++; }
                }
                along += len;
            }
            L($"trail to fresh water: {pts.Count} waypoints ({string.Join(" -> ", pts.Select(V))}), {trailClusters} clusters, {(_placedBy.TryGetValue("trail", out var tn) ? tn : 0)} nodes");

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
            int banks = 0, fish = 0; var fishPrefab = NodePrefab("Resource_Fish_Shoal");
            for (int i = 0; i < picks.Count; i++)
            {
                var (p, land) = picks[i];
                var bank = p + land * 3.5f;
                var grp = new GameObject($"Bank_{banks:00}").transform; grp.SetParent(waterRoot);
                int got = 0, n = 2 + rng.Next(2);
                for (int k = 0; k < n; k++) if (Place(Pick(Recipes[Biome.WaterEdge], rng), bank, k == 0 ? 0f : 0.8f, 3.5f, rng, grp, "water edge")) got++;
                if (got == 0) Object.DestroyImmediate(grp.gameObject); else { banks++; usedCenters.Add(bank); }
                if (fishPrefab && (i % 2 == 0 || fish < 3)) { for (float d = 2f; d <= 5f; d += 1f) if (PlaceFish(fishPrefab, p - land * d, rng, waterRoot)) { fish++; break; } }
            }
            L($"water edges: {picks.Count} shore spots, {banks} bank clusters ({(_placedBy.TryGetValue("water edge", out var we) ? we : 0)} nodes), {fish} fish shoals{(fishPrefab ? "" : " (no raw_fish item / model yet)")}");

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
        }
    }
}
