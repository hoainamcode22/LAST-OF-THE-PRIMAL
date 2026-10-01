using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PrimalFrontier.Core;
using PrimalFrontier.World;
using Object = UnityEngine.Object;

namespace PrimalFrontier.EditorTools
{
    /// <summary>R3 (RES, Phase 2): read-only access to the zone builders' corridors (route, wallows, knoll view, fern trails,
    /// predator path, canyon path, lava) so resource nodes keep them clear. Nothing here changes the zones.</summary>
    public static partial class PrimalZonesBuilder
    {
        internal static void R3Init()
        {
            FindTerrain(); LoadFeat(); BVLoadPath(); FootLoad();
            _wallows = null;
            _fernClearing = Ground(new Vector3(FernClearingDefault.x, 0f, FernClearingDefault.y));
            var lmT = SceneRootsFind(ZoneById("fern").GroupPath + "/LM_GiantFernForest"); if (lmT) _fernClearing = lmT.position;
            _fernTrails = FernTrails(_fernClearing);
        }

        /// <summary>null when p (footprint r) keeps every zone corridor clear, else what it would block</summary>
        internal static string R3Keepout(Vector3 p, float r)
        {
            if (RouteDist(p) < 4f + r) return "migration route";
            if (Feat != null && Feat.props != null && Feat.props.wallows != null && WallowDist(p) < 6f + r) return "wallow";
            if (Feat != null && EA_BlocksKnollView(p, 1.5f, 2f + r)) return "knoll view";
            if (_fernTrails != null && FernTrailClear(p) < r + 0.8f) return "fern trail";
            if (_fernTrails != null && EBFlat(p, _fernClearing) < FernClearingR + r) return "fern clearing";
            if (_bvPath != null && _bvPath.Count > 2 && BVPathDist(p) < 2.5f + r) return "predator path";
            if (_footCanyon != null && _footCanyon.Count > 1 && FootCanyonDist(p) < 2.5f + r) return "canyon path";
            if (FootLavaDist(p) < 5f + r) return "lava";
            return null;
        }
        internal static float R3FootBand(Vector3 p) => FootS(p);
        internal static float R3Weight(string zone, Vector3 p) { var z = ZoneById(zone); return z != null ? z.Weight(p) : 0f; }
        internal static Transform R3Root(string zone) { var z = ZoneById(zone); return z != null ? SceneRootsFind(z.GroupPath) : null; }
        internal static List<EBTrail> R3FernTrails => _fernTrails;
        internal static string[] R3TreeKeys => new[] { "EA_valley", "EA_wetland", BV_TreeKey, "EB_fern", "EB_foothills" };
        internal static IEnumerable<string> R3ZoneIds => Zones.Select(z => z.id);
    }

    /// <summary>
    /// Phase 2 resource pass (RES R3, 2026-10-01). Bridge: PrimalResourceBuilder.Phase2 ("" = apply and save, "dry" = report).
    /// Idempotent: [Resources]/Phase2 is rebuilt, covered nodes are moved (a moved node is not covered any more, so a re-run
    /// moves nothing). Log Documentation/Phase2/Logs/R3_phase2_build.txt.
    ///  1. covered nodes: resource nodes (stones, bushes, plants, converted ENV rocks) inside a zone collider (the zone checks'
    ///     test), inside the mesh footprint of a landmark (LM_FossilSkeleton, LM_BlackRidge, LM_RockRidge, LM_AncientFallenTree),
    ///     within 2 m of a Bone Valley prop or 1.2 m of a terrain tree: moved to the nearest free dry spot that keeps every corridor clear;
    ///  2. new nodes in the zones (natural clusters): cave RES_Anchors, fish (lagoon east shallows, cave pools), reeds at the
    ///     wetland reed beds, bone piles in Bone Valley, basalt / stone on the foothills' black rock band, fibre + branches along
    ///     the fern forest trails;
    ///  3. checks: ground, water, covered, corridors (all 0).
    /// </summary>
    public static partial class PrimalResourceBuilder
    {
        const string P2Log = "Documentation/Phase2/Logs/R3_phase2_build.txt";
        static readonly string[] P2LandmarkNames = { "LM_FossilSkeleton", "LM_BlackRidge", "LM_RockRidge", "LM_AncientFallenTree" };
        /// <summary>a landmark's mesh bounds in its own frame; mesh = the bounds are the footprint (the fossil: open ribs, the owner's rule),
        /// otherwise a node is covered only where the landmark's colliders are above it (ridges, the fallen tree)</summary>
        struct P2Obb { public string name; public Transform t; public Bounds lb; public bool mesh; public Collider[] cols; }
        static List<P2Obb> _p2Lms = new List<P2Obb>();
        static List<Transform> _p2ZoneRoots = new List<Transform>();
        static List<Vector3> _p2BoneProps = new List<Vector3>();
        static Transform _p2Cave; static Vector3 _p2Spawn;
        static readonly Dictionary<Vector2Int, float> _p2CaveWater = new Dictionary<Vector2Int, float>();   // 0.5 m cells -> surface y
        static readonly Dictionary<string, int> _p2Count = new Dictionary<string, int>();
        static readonly List<GameObject> _p2New = new List<GameObject>();
        static readonly List<Transform> _p2Moved = new List<Transform>();

        [PrimalBridgeCommand]
        public static string Phase2(string arg)
        {
            bool dry = !string.IsNullOrEmpty(arg) && arg.Contains("dry");
            _log = new StringBuilder(); _p2Count.Clear(); _p2New.Clear(); _p2Moved.Clear();
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (scene.isDirty) return "active scene has unsaved changes and is not the island: open the island first";
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            L($"PrimalResourceBuilder.Phase2 {DateTime.Now:yyyy-MM-dd HH:mm} arg '{arg}'{(dry ? " (dry run: no change)" : "")}");
            var db = dry ? AssetDatabase.LoadAssetAtPath<ResourceDatabase>(DbPath) : BuildDataInto(new StringBuilder());
            if (!dry) L($"data: {db.resources.Count} definitions (+ fiber_reeds, stone_basalt), node prefabs rebuilt");
            if (!Setup()) return _log.ToString();
            _seaY = SeaLevel();
            PrimalZonesBuilder.R3Init();
            var rootGo = GameObject.Find("[Resources]"); if (!rootGo) { L("ERROR no [Resources]"); return _log.ToString(); }
            _root = rootGo.transform;
            var spawnT = GameObject.Find("ZONE_PlayerSpawn"); _p2Spawn = spawnT ? spawnT.transform.position : Vector3.zero;
            _p2ZoneRoots = PrimalZonesBuilder.R3ZoneIds.Select(PrimalZonesBuilder.R3Root).Where(t => t).ToList();
            _p2Cave = PrimalZonesBuilder.R3Root("cave");
            var bv = PrimalZonesBuilder.R3Root("bone");
            _p2BoneProps = bv ? bv.GetComponentsInChildren<Renderer>().Select(r => r.transform.position).ToList() : new List<Vector3>();
            P2Landmarks();
            P2CaveWaterRaster();
            L($"zone roots {_p2ZoneRoots.Count} ({string.Join(", ", _p2ZoneRoots.Select(t => t.name))}), landmark footprints {_p2Lms.Count}, Bone Valley props {_p2BoneProps.Count}, cave water cells {_p2CaveWater.Count}, spawn {V(_p2Spawn)}");
            // the new group first goes away, so the covered test and the spots see only what stays
            var old = _root.Find("Phase2"); if (old && !dry) { Object.DestroyImmediate(old.gameObject); Physics.SyncTransforms(); }
            P2FixCovered(dry);
            if (!dry)
            {
                var g = new GameObject("Phase2").transform; g.SetParent(_root, false);
                var rng = new System.Random(Seed + 2002);
                P2CaveNodes(g, rng); Physics.SyncTransforms();
                P2Fish(g, rng); Physics.SyncTransforms();
                P2Reeds(g, rng); Physics.SyncTransforms();
                P2Bones(g, rng); Physics.SyncTransforms();
                P2Foothills(g, rng); Physics.SyncTransforms();
                P2Fern(g, rng); Physics.SyncTransforms();
                L("new nodes per zone: " + string.Join(", ", _p2Count.Select(k => $"{k.Key} {k.Value}")) + $" (total {_p2New.Count})");
            }
            P2Verify();
            if (!dry) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); L("scene saved " + scene.path); }
            Directory.CreateDirectory(Path.GetDirectoryName(P2Log));
            File.WriteAllText(dry ? P2Log.Replace(".txt", "_dry.txt") : P2Log, _log.ToString());
            return _log.ToString();
        }

        // ------------------------------------------------------------------ landmark footprints (mesh bounds in the landmark's frame)
        static void P2Landmarks()
        {
            _p2Lms.Clear();
            foreach (var zr in _p2ZoneRoots)
                foreach (var t in zr.GetComponentsInChildren<Transform>(false))
                {
                    if (!P2LandmarkNames.Contains(t.name)) continue;
                    var mfs = t.GetComponentsInChildren<MeshFilter>(false).Where(m => m.sharedMesh && m.GetComponent<Renderer>()).ToArray();
                    if (mfs.Length == 0) continue;
                    bool any = false; var lb = new Bounds();
                    foreach (var mf in mfs)
                    {
                        // LOD0 only when the object has an LODGroup (lower LODs have the same footprint anyway)
                        var mb = mf.sharedMesh.bounds;
                        for (int i = 0; i < 8; i++)
                        {
                            var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                            var lp = t.InverseTransformPoint(mf.transform.TransformPoint(c));
                            if (!any) { lb = new Bounds(lp, Vector3.zero); any = true; } else lb.Encapsulate(lp);
                        }
                    }
                    var cols = t.GetComponentsInChildren<Collider>(false).Where(c => c.enabled && !c.isTrigger).ToArray();
                    _p2Lms.Add(new P2Obb { name = t.name, t = t, lb = lb, mesh = t.name == "LM_FossilSkeleton" || cols.Length == 0, cols = cols });
                    var s = t.lossyScale;
                    L($"landmark {PathOf(t)} at {V(t.position)} yaw {t.eulerAngles.y:F0}: footprint {lb.size.x * Mathf.Abs(s.x):F1} x {lb.size.z * Mathf.Abs(s.z):F1} m, height {lb.size.y * Mathf.Abs(s.y):F1} m");
                }
        }

        static bool P2InLandmark(Vector3 p, float margin, out string which)
        {
            foreach (var o in _p2Lms)
            {
                if (!o.t) continue;
                var lp = o.t.InverseTransformPoint(p); var s = o.t.lossyScale;
                float mx = margin / Mathf.Max(0.01f, Mathf.Abs(s.x)), mz = margin / Mathf.Max(0.01f, Mathf.Abs(s.z)), my = 2f / Mathf.Max(0.01f, Mathf.Abs(s.y));
                if (!(lp.x > o.lb.min.x - mx && lp.x < o.lb.max.x + mx && lp.z > o.lb.min.z - mz && lp.z < o.lb.max.z + mz && lp.y < o.lb.max.y + my && lp.y > o.lb.min.y - my * 3f)) continue;
                if (o.mesh) { which = o.name; return true; }
                // collider landmarks: covered where one of its colliders is above the spot (centre and 4 points at the margin)
                foreach (var d in new[] { Vector3.zero, Vector3.right * margin, Vector3.left * margin, Vector3.forward * margin, Vector3.back * margin })
                {
                    var from = p + d + Vector3.up * 40f; float len = 40f - 0.1f;
                    foreach (var c in o.cols) if (c && c.Raycast(new Ray(from, Vector3.down), out _, len)) { which = o.name; return true; }
                }
            }
            which = null; return false;
        }

        static bool P2InZoneRoot(Transform t) { foreach (var r in _p2ZoneRoots) if (t.IsChildOf(r)) return true; return false; }

        /// <summary>the zone checks' covered test: a solid collider of a zone group in a 0.55 m sphere 0.5 m above the node</summary>
        static Collider P2ZoneCollider(Vector3 p, Transform self, float r = 0.55f)
        {
            bool underground = p.y < Ground(p) - 2f;                          // in the cave: the shell is the floor, not a cover
            foreach (var c in Physics.OverlapSphere(p + Vector3.up * 0.5f, r, ~0, QueryTriggerInteraction.Ignore))
                if (!(c is TerrainCollider) && P2InZoneRoot(c.transform) && (!self || !c.transform.IsChildOf(self)) && !(underground && c.name.StartsWith("Shell_"))) return c;
            return null;
        }

        static bool P2NearBoneProp(Vector3 p, float d) => PrimalZonesBuilder.R3Weight("bone", p) > 0f && _p2BoneProps.Any(q => Flat(q - p).magnitude < d);
        static bool P2InCave(Transform t) => _p2Cave && t.IsChildOf(_p2Cave) || (t.parent && t.parent.name == "DeepWaterCave");

        /// <summary>why this node counts as covered (null = it is fine)</summary>
        static string P2CoveredWhy(ResourceNode n)
        {
            var p = n.transform.position;
            var c = P2ZoneCollider(p, n.transform); if (c) return "zone collider " + c.name;
            if (P2InLandmark(p, 0.3f, out var lm)) return "inside the footprint of " + lm;
            if (P2NearBoneProp(p, 2f)) return "within 2 m of a Bone Valley prop";
            // a solid node standing in a fern forest trail (the trail check's sphere: 0.8 x the half width round the centre line)
            var trails = PrimalZonesBuilder.R3FernTrails;
            if (trails != null && PrimalZonesBuilder.R3Weight("fern", p) > 0f)
            {
                float r = 0f;
                foreach (var sc in n.GetComponentsInChildren<Collider>()) if (sc.enabled && !sc.isTrigger) r = Mathf.Max(r, Mathf.Max(sc.bounds.extents.x, sc.bounds.extents.z));
                if (r > 0f) foreach (var t in trails) if (t.Dist(p) < t.halfWidth * 0.8f + r) return "solid node on " + t.name;
            }
            return null;
        }

        static bool P2InAnyZone(Vector3 p) => PrimalZonesBuilder.R3ZoneIds.Any(z => PrimalZonesBuilder.R3Weight(z, p) > 0f);

        // ------------------------------------------------------------------ 1. covered nodes
        static void P2FixCovered(bool dry)
        {
            var all = Object.FindObjectsByType<ResourceNode>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(n => n.enabled && !P2InZoneRoot(n.transform) && (P2InAnyZone(n.transform.position) || _p2Lms.Any(o => o.t && Flat(o.t.position - n.transform.position).magnitude < 40f)))
                .OrderBy(n => PathOf(n.transform)).ToList();
            int covered = 0, moved = 0, stuck = 0;
            foreach (var n in all)
            {
                if (n.definition && n.definition.category == ResourceCategory.Fish) continue;
                var why = P2CoveredWhy(n); if (why == null) continue;
                covered++;
                var t = n.transform; var p0 = t.position;
                // the object to move: a bush's node sits on the bush root, a converted rock on the rock itself
                var cols = t.GetComponentsInChildren<Collider>(true).Where(c => c.enabled).ToArray();
                var rs = t.GetComponentsInChildren<Renderer>().Where(r => !(r is ParticleSystemRenderer)).ToArray();
                float r = 0.4f; if (rs.Length > 0) { var b = rs[0].bounds; foreach (var x in rs) b.Encapsulate(x.bounds); r = Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.z), 0.35f, 4f); }
                float dy = Mathf.Clamp(p0.y - Ground(p0), -r * 0.5f, 0.2f);
                foreach (var c in cols) c.enabled = false;                       // the search must not see the node itself
                Physics.SyncTransforms();
                bool ok = P2FindSpot(p0, r, 40f, out var q, out var tries);
                foreach (var c in cols) c.enabled = true;
                if (!ok) { stuck++; L($"   NO FREE SPOT within 40 m: {PathOf(t)} {V(p0)} ({why})"); continue; }
                q.y = Ground(q) + dy;
                L($"   {(dry ? "would move" : "moved")} {PathOf(t)} {V(p0)} -> {V(q)} ({Flat(q - p0).magnitude:F1} m, r {r:F1}): {why}");
                if (!dry) { t.position = q; P1Dirty(t); Physics.SyncTransforms(); _p2Moved.Add(t); }
                moved++;
            }
            L($"covered nodes near the zones: {all.Count} checked, {covered} covered, {(dry ? "would move" : "moved")} {moved}, no spot {stuck}");
            // interactables that are not resource nodes inside the fossil / ridges: reported, not moved (other agents' objects)
            int others = 0;
            foreach (var it in Object.FindObjectsByType<Interactable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (it is ResourceNode || P2InZoneRoot(it.transform)) continue;
                if (P2InLandmark(it.transform.position, 0.3f, out var lm)) { others++; L($"   other interactable inside {lm}: {PathOf(it.transform)} {V(it.transform.position)} (not moved: not a resource node)"); }
            }
            L($"other interactables inside a landmark footprint: {others}");
        }

        /// <summary>the nearest spot (rings 1.5 m apart) where a footprint r is free and keeps every corridor clear</summary>
        static bool P2FindSpot(Vector3 p, float r, float maxD, out Vector3 q, out int tries)
        {
            tries = 0;
            for (float d = 1.5f; d <= maxD; d += 1.5f)
            {
                int steps = Mathf.Clamp(Mathf.RoundToInt(d * 4f), 12, 96);
                float best = float.MaxValue; Vector3 pick = Vector3.zero; bool any = false;
                for (int k = 0; k < steps; k++)
                {
                    float a = (k + 0.5f * (d % 3f)) / steps * Mathf.PI * 2f; var c = p + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                    tries++;
                    if (P2SpotWhy(c, r, 2f, 0f) != null) continue;
                    float score = Mathf.Abs(Ground(c) - Ground(p)) + Steep(c) * 0.05f;
                    if (score < best) { best = score; pick = c; any = true; }
                }
                if (any) { q = pick; return true; }
            }
            q = p; return false;
        }

        /// <summary>why a node of footprint r cannot stand at q (null = it can)</summary>
        static string P2SpotWhy(Vector3 q, float r, float minWater, float maxWater)
        {
            var u = q - _tp;
            if (u.x < 4 || u.z < 4 || u.x > _td.size.x - 4 || u.z > _td.size.z - 4) return "edge";
            float h = Ground(q); q.y = h; if (h < _seaY + 0.5f) return "sea";
            if (InWater(q)) return "water";
            float wd = WaterDistance(q, Mathf.Max(minWater, maxWater) + 1f);
            if (wd < minWater) return "water edge";
            if (maxWater > 0f && wd > maxWater) return "far from water";
            if (Steep(q) > 30f) return "steep";
            var k = Cell8(q); float tr = Mathf.Max(1.4f, r * 0.7f);
            for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
                    if (_treeGrid.TryGetValue(new Vector2Int(k.x + dx, k.y + dz), out var l)) foreach (var t in l) { var d = t - q; d.y = 0; if (d.sqrMagnitude < tr * tr) return "tree"; }
            if (Flat(q - _p2Spawn).magnitude < 25f) return "spawn";
            var camp = Marker("ZONE_Camp"); if (camp != Vector3.zero && Flat(q - camp).magnitude < 7f) return "camp";
            if (InLoc("old_camp", q, 0.6f) || InLoc("nest", q, 0.5f)) return "story spot";
            var ko = PrimalZonesBuilder.R3Keepout(q, r); if (ko != null) return ko;
            if (P2InLandmark(q, Mathf.Max(1f, r * 0.5f), out var lm)) return "landmark";
            if (P2ZoneCollider(q, null, Mathf.Max(0.55f, r * 0.8f))) return "zone collider";
            var cs = new Vector3(q.x, h + r + 0.15f, q.z);
            foreach (var c in Physics.OverlapSphere(cs, r + 0.1f, ~0, QueryTriggerInteraction.Collide)) if (!(c is TerrainCollider) && c.enabled) return "occupied";
            if (P2NearBoneProp(q, 2.2f + r * 0.3f)) return "bone prop";
            return null;
        }

        // ------------------------------------------------------------------ placing
        static GameObject P2Spawn(string prefabName, Vector3 pos, Vector3 normal, float align, System.Random rng, Transform parent, string zone, string id, float scale = -1f)
        {
            var pf = NodePrefab(Variant(prefabName, rng)); if (!pf) { L($"WARNING prefab {prefabName} missing"); return null; }
            var node0 = pf.GetComponent<ResourceNode>(); var def = node0 ? node0.definition : null; if (!def) return null;
            if (scale <= 0f) scale = 0.85f + (float)rng.NextDouble() * 0.3f;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(pf, parent);
            go.transform.SetPositionAndRotation(pos, Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, normal, align)) * Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
            go.transform.localScale = Vector3.one * scale;
            var node = go.GetComponent<ResourceNode>();
            node.SaveId = id; node.charges = def.RollAmount(Hash(id)); node.radius = node0.radius * scale;
            PrefabUtility.RecordPrefabInstancePropertyModifications(node); PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
            go.name = $"{pf.name}_{id.Replace("rn_p2_", "")}";
            _p2New.Add(go); _p2Count[zone] = _p2Count.TryGetValue(zone, out var c) ? c + 1 : 1;
            Physics.SyncTransforms();
            return go;
        }

        /// <summary>a node on the terrain near 'near' (minR..maxR), the spot passing P2SpotWhy and the extra test</summary>
        static GameObject P2Try(string prefab, Vector3 near, float minR, float maxR, System.Random rng, Transform parent, string zone, string id, Func<Vector3, bool> extra = null, float minWater = 2f, float maxWater = 0f)
        {
            var pf = NodePrefab(prefab); var n0 = pf ? pf.GetComponent<ResourceNode>() : null; if (!n0 || !n0.definition) return null;
            float r = Mathf.Max(0.35f, n0.radius);
            for (int a = 0; a < 24; a++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f, d = minR + (float)rng.NextDouble() * (maxR - minR);
                var q = near + new Vector3(Mathf.Cos(ang) * d, 0f, Mathf.Sin(ang) * d);
                if (P2SpotWhy(q, r, minWater, maxWater) != null || (extra != null && !extra(q))) continue;
                var def = n0.definition;
                q.y = Ground(q) - (def.category == ResourceCategory.Stone ? 0.04f : 0.02f);
                return P2Spawn(prefab, q, Normal(q), def.category == ResourceCategory.Fiber ? 0.3f : 0.7f, rng, parent, zone, id);
            }
            return null;
        }

        static Vector3 P2G(Vector3 p) { p.y = Ground(p); return p; }
        static Transform P2Group(Transform g, string name) { var t = g.Find(name); if (!t) { t = new GameObject(name).transform; t.SetParent(g, false); } return t; }

        static Transform P2FindByName(Transform under, string name) => under ? under.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name) : null;

        // ------------------------------------------------------------------ 2a. cave
        static void P2CaveWaterRaster()
        {
            _p2CaveWater.Clear();
            if (!_p2Cave) return;
            foreach (var mf in _p2Cave.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh || !(mf.name.StartsWith("Water") || mf.GetComponent<WaterSource>() || mf.GetComponentInParent<WaterSource>())) continue;
                var m = mf.sharedMesh; var v = m.vertices; var tr = m.triangles; var w = mf.transform.localToWorldMatrix;
                var wv = v.Select(x => w.MultiplyPoint3x4(x)).ToArray();
                for (int i = 0; i + 2 < tr.Length; i += 3)
                {
                    Vector3 a = wv[tr[i]], b = wv[tr[i + 1]], c = wv[tr[i + 2]];
                    int x0 = Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) * 2f), x1 = Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) * 2f);
                    int z0 = Mathf.FloorToInt(Mathf.Min(a.z, Mathf.Min(b.z, c.z)) * 2f), z1 = Mathf.CeilToInt(Mathf.Max(a.z, Mathf.Max(b.z, c.z)) * 2f);
                    for (int x = x0; x <= x1; x++)
                        for (int z = z0; z <= z1; z++)
                        {
                            var pp = new Vector2((x + 0.5f) * 0.5f, (z + 0.5f) * 0.5f);
                            if (!Bary(pp, new Vector2(a.x, a.z), new Vector2(b.x, b.z), new Vector2(c.x, c.z), out var uu, out var vv, out var ww)) continue;
                            float y = a.y * uu + b.y * vv + c.y * ww; var key = new Vector2Int(x, z);
                            if (!_p2CaveWater.TryGetValue(key, out var y0) || y > y0) _p2CaveWater[key] = y;
                        }
                }
            }
        }
        static Vector2Int P2CaveCell(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x * 2f), Mathf.FloorToInt(p.z * 2f));
        static bool P2CaveOverWater(Vector3 floor, int ring)
        {
            var c = P2CaveCell(floor);
            for (int dx = -ring; dx <= ring; dx++) for (int dz = -ring; dz <= ring; dz++)
                    if (_p2CaveWater.TryGetValue(new Vector2Int(c.x + dx, c.y + dz), out var y) && y > floor.y - 0.05f) return true;
            return false;
        }

        // cave walk lines (PrimalCaveBuilder PassA / PassB / PassH centre lines, x, y, z, half width)
        static readonly Vector4[][] P2CaveLines =
        {
            new[] { new Vector4(90.0f, 19.45f, -157.4f, 2.1f), new Vector4(85.0f, 19.72f, -159.6f, 2.3f), new Vector4(79.2f, 19.92f, -161.6f, 2.5f), new Vector4(73.2f, 20.12f, -161.3f, 2.2f),
                    new Vector4(67.0f, 20.34f, -165.4f, 2.6f), new Vector4(61.4f, 20.54f, -170.2f, 2.3f), new Vector4(55.0f, 20.74f, -171.8f, 2.5f), new Vector4(49.6f, 20.88f, -173.0f, 3.0f) },
            new[] { new Vector4(30.4f, 20.92f, -173.4f, 2.6f), new Vector4(26.5f, 21.2f, -169.2f, 2.2f), new Vector4(22.0f, 21.5f, -165.0f, 2.1f), new Vector4(17.5f, 21.8f, -160.0f, 2.3f),
                    new Vector4(14.8f, 22.0f, -154.0f, 2.0f), new Vector4(13.2f, 22.15f, -148.0f, 1.9f), new Vector4(12.4f, 22.49f, -141.4f, 1.9f) },
            new[] { new Vector4(41.6f, 20.92f, -180.6f, 1.5f), new Vector4(42.2f, 20.97f, -184.4f, 0.95f), new Vector4(43.1f, 21.05f, -187.3f, 1.3f), new Vector4(44.2f, 21.2f, -190.2f, 1.8f) },
            // the pool walkway round the chamber: chamber east edge -> north rim -> west edge
            new[] { new Vector4(49.6f, 20.88f, -173.0f, 1.6f), new Vector4(44f, 20.9f, -169.5f, 1.6f), new Vector4(36f, 20.9f, -169.5f, 1.6f), new Vector4(30.4f, 20.92f, -173.4f, 1.6f) },
        };
        static float P2CaveLineDist(Vector3 p)
        {
            float best = 1e9f;
            foreach (var line in P2CaveLines)
                for (int i = 0; i + 1 < line.Length; i++)
                {
                    var a = new Vector3(line[i].x, 0, line[i].z); var b = new Vector3(line[i + 1].x, 0, line[i + 1].z);
                    best = Mathf.Min(best, PrimalZonesBuilder.SegDist(p, a, b));
                }
            return best;
        }

        static bool P2CaveFloor(Vector3 p, out RaycastHit hit)
        {
            hit = default;
            if (!Physics.Raycast(p + Vector3.up * 1.2f, Vector3.down, out hit, 3.5f, ~0, QueryTriggerInteraction.Ignore)) return false;
            return !(hit.collider is TerrainCollider) && _p2Cave && hit.collider.transform.IsChildOf(_p2Cave);
        }

        static string P2CaveSpotWhy(Vector3 p, float r, float minLine, out RaycastHit fh)
        {
            if (!P2CaveFloor(p, out fh)) return "no cave floor";
            if (Vector3.Angle(fh.normal, Vector3.up) > 28f) return "steep";
            if (P2CaveOverWater(fh.point, 1)) return "water";
            if (P2CaveLineDist(fh.point) < minLine) return "walk line";
            if (Physics.Raycast(fh.point + Vector3.up * 0.1f, Vector3.up, out var up, 1.6f, ~0, QueryTriggerInteraction.Ignore)) return "headroom";
            if (P2ZoneCollider(fh.point, null, Mathf.Max(0.55f, r))) return "cave prop";
            var self = fh.collider;
            foreach (var c in Physics.OverlapSphere(fh.point + Vector3.up * (r + 0.1f), r, ~0, QueryTriggerInteraction.Collide)) if (c != self && !(c is TerrainCollider) && !c.transform.IsChildOf(self.transform.parent ? self.transform.parent : self.transform)) return "occupied";
            return null;
        }

        static void P2CaveNodes(Transform g, System.Random rng)
        {
            if (!_p2Cave) { L("cave: DeepWaterCave group missing"); return; }
            var anchors = P2FindByName(_p2Cave, "RES_Anchors");
            if (!anchors) { L("cave: RES_Anchors missing"); return; }
            var grp = P2Group(g, "DeepWaterCave");
            int idx = 0, missed = 0;
            foreach (Transform a in anchors)
            {
                string n = a.name; string pf; int count;
                if (n.StartsWith("RES_Stone")) { pf = "Resource_Stone_Small"; count = 2; }
                else if (n.StartsWith("RES_Flint")) { pf = "Resource_Stone_Small"; count = 1; }               // no flint item in the project: loose stone
                else if (n.StartsWith("RES_Clay")) { pf = "Resource_Stone_Small"; count = 1; }                // no clay item: pebbles on the bank
                else if (n.Contains("bones") || n.StartsWith("RES_Bones")) { pf = "Resource_Rare_Bones"; count = 1; }
                else if (n.Contains("flint")) { pf = "Resource_Stone_Medium"; count = 1; }                    // hidden chamber: a bigger stone
                else { L($"cave: anchor {n} unknown, skipped"); continue; }
                var pf0 = NodePrefab(pf); float r = pf0 ? Mathf.Max(0.3f, pf0.GetComponent<ResourceNode>().radius) : 0.35f;
                float d0 = P2CaveLineDist(a.position);
                const float minLine = 1.1f;                                        // off the passage centre (CAVE's walk probes), towards the wall
                for (int i = 0; i < count; i++)
                {
                    bool done = false; string lastWhy = "";
                    for (int t = 0; t < 30 && !done; t++)
                    {
                        Vector3 p = a.position;
                        if (i > 0 || t > 0) { float ang = (float)rng.NextDouble() * Mathf.PI * 2f, d = 0.5f + (float)rng.NextDouble() * (t < 15 ? 1.2f : 2.2f); p += new Vector3(Mathf.Cos(ang) * d, 0f, Mathf.Sin(ang) * d); }
                        var why = P2CaveSpotWhy(p, r, minLine, out var fh);
                        if (why != null) { lastWhy = why; continue; }
                        var def = pf0.GetComponent<ResourceNode>().definition;
                        var go = P2Spawn(pf, fh.point - Vector3.up * (def.category == ResourceCategory.Stone ? 0.04f : 0.02f), fh.normal, 0.7f, rng, grp, "cave", $"rn_p2_cave_{idx++:00}");
                        if (go) { done = true; L($"   cave {n}: {go.name} at {V(go.transform.position)} (walk line {P2CaveLineDist(go.transform.position):F1} m)"); }
                    }
                    if (!done) { missed++; L($"   cave {n} #{i}: no spot ({lastWhy})"); }
                }
            }
            L($"cave: {_p2Count.GetValueOrDefault("cave")} nodes at {anchors.childCount} RES anchors, {missed} slots without a spot");
        }

        // ------------------------------------------------------------------ 2b. fish
        static void P2Fish(Transform g, System.Random rng)
        {
            var grp = P2Group(g, "Fish"); int idx = 0;
            var existing = Object.FindObjectsByType<ResourceNode>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(n => n.definition && n.definition.category == ResourceCategory.Fish).Select(n => n.transform.position).ToList();
            // cave pools: AI_Fish_Pool_0..2, AI_Fish_AlcovePool_0 (0.6 m under the surface)
            if (_p2Cave)
                foreach (var a in _p2Cave.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("AI_Fish_")).OrderBy(t => t.name))
                {
                    float surface = a.position.y + 0.6f;
                    if (_p2CaveWater.TryGetValue(P2CaveCell(a.position), out var wy)) surface = wy;
                    Vector3 bed = a.position + Vector3.down * 0.5f; float depth = 0.55f;
                    if (Physics.Raycast(a.position + Vector3.up * 0.2f, Vector3.down, out var h, 3f, ~0, QueryTriggerInteraction.Ignore) && h.collider.transform.IsChildOf(_p2Cave)) { bed = h.point; depth = surface - bed.y; }
                    var go = P2Spawn("Resource_Fish_Shoal", bed + Vector3.up * 0.05f, Vector3.up, 0f, rng, grp, "cave fish", $"rn_p2_fish_cave_{idx++:00}", 1f);
                    if (go) { var n = go.GetComponent<ResourceNode>(); n.cueHeight = Mathf.Max(0.1f, depth - 0.05f); PrefabUtility.RecordPrefabInstancePropertyModifications(n); existing.Add(go.transform.position); L($"   cave fish {a.name}: {go.name} at {V(go.transform.position)} depth {depth:F2} m"); }
                }
            // lagoon east shallows: 0.2-0.8 m deep, east half, away from the beach and the river mouth
            var lagoon = new List<Vector2Int>();
            foreach (var kv in _water)
            {
                var p = new Vector3(kv.Key.x + 0.5f, 0f, kv.Key.y + 0.5f);
                if (p.x < 108f || p.z > 186f || p.z < 148f || PrimalZonesBuilder.R3Weight("wetland", p) < 0.5f) continue;
                float depth = kv.Value - Ground(p); if (depth < 0.2f || depth > 0.8f) continue;
                bool inner = true; for (int dx = -1; dx <= 1 && inner; dx++) for (int dz = -1; dz <= 1; dz++) if (!_water.ContainsKey(new Vector2Int(kv.Key.x + dx, kv.Key.y + dz))) { inner = false; break; }
                if (inner && !P2InLandmark(p, 2f, out _)) lagoon.Add(kv.Key);
            }
            int lag = 0;
            foreach (var c in lagoon.OrderBy(k => Hash($"{k.x},{k.y}")))
            {
                if (lag >= 3) break;
                var p = new Vector3(c.x + 0.5f, 0f, c.y + 0.5f);
                if (existing.Any(e => Flat(e - p).magnitude < 10f)) continue;
                float depth = _water[c] - Ground(p);
                var go = P2Spawn("Resource_Fish_Shoal", new Vector3(p.x, Ground(p) + 0.05f, p.z), Vector3.up, 0f, rng, grp, "wetland fish", $"rn_p2_fish_lagoon_{lag:00}", 1f);
                if (!go) continue;
                var n = go.GetComponent<ResourceNode>(); n.cueHeight = depth - 0.05f; PrefabUtility.RecordPrefabInstancePropertyModifications(n);
                existing.Add(go.transform.position); lag++;
                L($"   lagoon fish: {go.name} at {V(go.transform.position)} depth {depth:F2} m");
            }
            L($"fish: cave {_p2Count.GetValueOrDefault("cave fish")}, lagoon {lag} (from {lagoon.Count} shallow cells)");
        }

        // ------------------------------------------------------------------ 2c. wetland reeds
        static void P2Reeds(Transform g, System.Random rng)
        {
            var wet = PrimalZonesBuilder.R3Root("wetland"); var grp = P2Group(g, "PrehistoricWetland"); int idx = 0;
            var beds = wet ? wet.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("FXA_Wetland_Insects_Reeds")).OrderBy(t => t.name).ToList() : new List<Transform>();
            if (beds.Count == 0) L("wetland: no reed bed anchors (FXA_Wetland_Insects_Reeds_*)");
            foreach (var b in beds)
            {
                int got = 0;
                for (int i = 0; i < 3; i++)
                    if (P2Try(i == 2 ? "Resource_Fiber_Plant" : "Resource_Fiber_Reeds", b.position, 1.5f, 9f, rng, grp, "wetland", $"rn_p2_wet_{idx++:00}", null, 0.5f, 3.5f)) got++;
                L($"   reed bed {b.name} {V(b.position)}: {got} nodes at the water edge");
            }
        }

        // ------------------------------------------------------------------ 2d. bone valley
        static void P2Bones(Transform g, System.Random rng)
        {
            var bv = PrimalZonesBuilder.R3Root("bone"); var grp = P2Group(g, "BoneValley"); int idx = 0;
            var targets = new List<(string name, int n)> { ("BV_Carcass_Rotting", 2), ("BV_Carcass_Old", 2), ("BV_Carcass_Fresh", 1) };
            foreach (var (name, n) in targets)
            {
                var t = P2FindByName(bv, name); if (!t) { L($"   bone valley: {name} missing"); continue; }
                int got = 0;
                for (int i = 0; i < n; i++)
                    if (P2Try("Resource_Rare_Bones", t.position, 3f, 9f, rng, grp, "bone valley", $"rn_p2_bone_{idx++:00}", q => PrimalZonesBuilder.R3Weight("bone", q) >= 0.5f)) got++;
                L($"   bone piles near {name} {V(t.position)}: {got}");
            }
        }

        // ------------------------------------------------------------------ 2e. foothills: the black rock band
        static void P2Foothills(Transform g, System.Random rng)
        {
            var grp = P2Group(g, "VolcanicFoothills"); int idx = 0, clusters = 0;
            var centres = new List<Vector3>();
            var nodes = Object.FindObjectsByType<ResourceNode>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Select(n => n.transform.position).ToList();
            var cand = new List<Vector3>();
            for (float x = -190f; x <= -80f; x += 5f) for (float z = -250f; z <= -100f; z += 5f) cand.Add(new Vector3(x, 0f, z));
            foreach (var c0 in cand.OrderBy(c => Hash($"{c.x:F0},{c.z:F0}")))
            {
                if (clusters >= 6) break;
                var c = P2G(c0);
                if (PrimalZonesBuilder.R3Weight("foothills", c) < 0.6f) continue;
                float s = PrimalZonesBuilder.R3FootBand(c); if (s < 0.48f || s > 0.70f) continue;
                if (Steep(c) > 26f || centres.Any(o => Flat(o - c).magnitude < 16f) || nodes.Any(o => Flat(o - c).magnitude < 5f)) continue;
                if (P2SpotWhy(c, 1.2f, 2f, 0f) != null) continue;
                Func<Vector3, bool> band = q => { float b = PrimalZonesBuilder.R3FootBand(q); return b >= 0.45f && b <= 0.74f; };
                int got = 0;
                foreach (var pf in new[] { "Resource_Stone_Basalt", "Resource_Stone_Basalt", clusters % 2 == 0 ? "Resource_Stone_Small" : "Resource_Stone_Medium" })
                    if (P2Try(pf, c, got == 0 ? 0f : 0.9f, 3.2f, rng, grp, "foothills", $"rn_p2_foot_{idx++:00}", band)) got++;
                if (got == 0) continue;
                centres.Add(c); clusters++;
                L($"   foothills cluster at {V(c)} (band {s:F2}): {got} nodes");
            }
            L($"foothills: {clusters} clusters on the black rock band");
        }

        // ------------------------------------------------------------------ 2f. fern forest: trail edges
        static void P2Fern(Transform g, System.Random rng)
        {
            var grp = P2Group(g, "GiantFernForest"); int idx = 0, clusters = 0;
            var trails = PrimalZonesBuilder.R3FernTrails; if (trails == null) { L("fern: no trails"); return; }
            foreach (var t in trails)
            {
                float along = 0f, next = 10f; int side = 1;
                for (int i = 0; i + 1 < t.pts.Length && clusters < 10; i++)
                {
                    var a = t.pts[i]; var b = t.pts[i + 1]; float len = Flat(b - a).magnitude; if (len < 0.5f) { continue; }
                    var dir = Flat(b - a).normalized; var perp = Vector3.Cross(Vector3.up, dir);
                    while (next <= along + len && clusters < 10)
                    {
                        var c = a + dir * (next - along) + perp * side * (t.halfWidth + 2.6f); side = -side; next += 24f;
                        c = P2G(c);
                        if (PrimalZonesBuilder.R3Weight("fern", c) < 0.6f || P2SpotWhy(c, 0.8f, 2f, 0f) != null) continue;
                        int got = 0;
                        if (P2Try(clusters % 2 == 0 ? "Resource_Fiber_Fern" : "Resource_Fiber_Plant", c, 0f, 1.2f, rng, grp, "fern forest", $"rn_p2_fern_{idx++:00}")) got++;
                        if (P2Try("Resource_Wood_Branch", c, 1.0f, 3f, rng, grp, "fern forest", $"rn_p2_fern_{idx++:00}")) got++;
                        if (got > 0) { clusters++; L($"   fern {t.name} cluster at {V(c)} ({PrimalZonesBuilder.R3Keepout(c, 0f) ?? "clear"}): {got} nodes"); }
                    }
                    along += len;
                }
            }
            L($"fern forest: {clusters} clusters along the trail edges");
        }

        // ------------------------------------------------------------------ 3. checks
        static void P2Verify()
        {
            int ground = 0, water = 0, cover = 0, corridor = 0; var bad = new List<string>();
            foreach (var go in _p2New.Concat(_p2Moved.Select(t => t ? t.gameObject : null)).Where(x => x))
            {
                var n = go.GetComponent<ResourceNode>(); var p = go.transform.position;
                bool fish = n && n.definition && n.definition.category == ResourceCategory.Fish;
                bool cave = _p2Cave && Flat(p - new Vector3(40f, 0f, -170f)).magnitude < 60f && p.y < Ground(p) - 2f;
                if (cave)
                {
                    if (!Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out var h, 2.5f, ~0, QueryTriggerInteraction.Ignore) || Mathf.Abs(h.point.y - p.y) > 0.35f) { ground++; bad.Add($"cave floor gap {go.name}"); }
                    if (!fish && P2CaveOverWater(p, 0)) { water++; bad.Add($"cave water {go.name}"); }
                    if (!fish && P2CaveLineDist(p) < 1.0f) { corridor++; bad.Add($"walk line {go.name} {P2CaveLineDist(p):F2} m"); }
                }
                else
                {
                    if (!fish && Mathf.Abs(p.y - Ground(p)) > 0.6f) { ground++; bad.Add($"off ground {go.name} {p.y - Ground(p):F2}"); }
                    if (!fish && P1Wet(p)) { water++; bad.Add($"in water {go.name} {V(p)}"); }
                    if (fish && !InWater(p)) { water++; bad.Add($"fish not in water {go.name}"); }
                    var ko = fish ? null : PrimalZonesBuilder.R3Keepout(p, 0f); if (ko != null) { corridor++; bad.Add($"{ko}: {go.name}"); }
                }
                if (n && !fish) { var why = P2CoveredWhy(n); if (why != null) { cover++; bad.Add($"covered {go.name}: {why}"); } }
            }
            L($"CHECK new + moved nodes ({_p2New.Count} + {_p2Moved.Count}): off the ground {ground}, in water {water}, covered {cover}, on a corridor (route / trails / paths / walk lines) {corridor}");
            foreach (var b in bad.Take(30)) L("   " + b);
            // every node near the zones: covered (zone collider, landmark footprint, bone prop) after the pass
            int left = 0;
            foreach (var n in Object.FindObjectsByType<ResourceNode>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!n.enabled || P2InZoneRoot(n.transform) || (n.definition && n.definition.category == ResourceCategory.Fish)) continue;
                if (!P2InAnyZone(n.transform.position) && !_p2Lms.Any(o => o.t && Flat(o.t.position - n.transform.position).magnitude < 40f)) continue;
                var why = P2CoveredWhy(n); if (why != null) { left++; if (left <= 12) L($"   still covered: {PathOf(n.transform)} {V(n.transform.position)}: {why}"); }
            }
            L($"CHECK all nodes near the zones still covered: {left}");
            foreach (var o in _p2Lms.Where(o => o.name == "LM_FossilSkeleton"))
            {
                int inside = Object.FindObjectsByType<Interactable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Count(it => !P2InZoneRoot(it.transform) && P2InLandmark(it.transform.position, 0f, out var w) && w == o.name);
                L($"CHECK LM_FossilSkeleton mesh footprint: interactables inside {inside}");
            }
        }
    }
}
