using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PrimalFrontier.World;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// Resource pass (Phase 3.5, RES agent). Bridge commands:
    ///   Audit        read-only map of the island: which rocks / logs / plants are gameplay nodes and which are decoration,
    ///                nodes near the spawn, fresh water distance (writes Documentation/Resources/scene_audit.txt).
    /// </summary>
    public static partial class PrimalResourceBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Island_VerticalSlice.unity";
        public const string DocDir = "Documentation/Resources";

        // ------------------------------------------------------------------ audit
        [PrimalBridgeCommand]
        public static string Audit(string arg)
        {
            var sb = new StringBuilder();
            var scene = EditorSceneManager.GetActiveScene();
            sb.AppendLine($"scene {scene.path} dirty {scene.isDirty}");
            foreach (var r in scene.GetRootGameObjects()) sb.AppendLine($"root {r.name} children {r.transform.childCount} active {r.activeSelf}");
            var world = GameObject.Find("World");
            if (world)
                foreach (Transform g in world.transform)
                {
                    var kinds = new Dictionary<string, List<Transform>>();
                    foreach (Transform c in g) { var k = Kind(c.name); if (!kinds.TryGetValue(k, out var l)) kinds[k] = l = new List<Transform>(); l.Add(c); }
                    sb.AppendLine($"World/{g.name}: {g.childCount} children");
                    foreach (var kv in kinds.OrderBy(k => k.Key))
                    {
                        int nodes = kv.Value.Count(t => t.GetComponent<ResourceNode>());
                        var b = kv.Value.Select(t => Bounds(t.gameObject)).ToList();
                        var avg = new Vector3(b.Average(x => x.size.x), b.Average(x => x.size.y), b.Average(x => x.size.z));
                        var t0 = kv.Value[0];
                        int cols = kv.Value.Count(t => t.GetComponentInChildren<Collider>());
                        var mr = t0.GetComponentInChildren<Renderer>();
                        string mat = mr && mr.sharedMaterial ? $"{mr.sharedMaterial.name}/{(mr.sharedMaterial.shader ? mr.sharedMaterial.shader.name : "?")}/bc{mr.sharedMaterial.HasProperty("_BaseColor")}" : "-";
                        string pf = PrefabUtility.GetCorrespondingObjectFromSource(t0.gameObject) is GameObject src ? AssetDatabase.GetAssetPath(src) : "no prefab";
                        sb.AppendLine($"   {kv.Key}: {kv.Value.Count} (nodes {nodes}, colliders {cols}) size {avg.x:F2}x{avg.y:F2}x{avg.z:F2} mat {mat} prefab {pf} scale {t0.localScale.x:F2}");
                    }
                }
            // markers
            var markers = PrimalFrontier.Core.SceneRoots.LegacyObject("Markers");
            if (markers) foreach (var t in markers.GetComponentsInChildren<Transform>()) if (t != markers.transform && t.childCount == 0) sb.AppendLine($"marker {t.parent.name}/{t.name} {V(t.position)}");
            // spawn + nodes near it
            var spawnT = GameObject.Find("ZONE_PlayerSpawn"); Vector3 spawn = spawnT ? spawnT.transform.position : Vector3.zero;
            sb.AppendLine($"spawn {V(spawn)}");
            var nodesAll = Object.FindObjectsByType<ResourceNode>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            sb.AppendLine($"ResourceNodes total {nodesAll.Length}");
            foreach (var g in nodesAll.GroupBy(n => (n.yieldItem ? n.yieldItem.id : "?") + " / " + n.displayName + " / req " + n.requiredTool))
                sb.AppendLine($"   {g.Key}: {g.Count()} within 20 m {g.Count(n => D(n, spawn) < 20)}, 40 m {g.Count(n => D(n, spawn) < 40)}, 60 m {g.Count(n => D(n, spawn) < 60)}, 100 m {g.Count(n => D(n, spawn) < 100)}");
            foreach (var n in nodesAll.Where(n => D(n, spawn) < 60).OrderBy(n => D(n, spawn))) sb.AppendLine($"   near spawn {D(n, spawn):F1} m: {PathOf(n.transform)} ({n.displayName}, charges {n.charges}x{n.yieldPerHit})");
            var pk = Object.FindObjectsByType<WorldPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            sb.AppendLine($"WorldPickups {pk.Length}: " + string.Join(", ", pk.Select(p => $"{(p.item ? p.item.id : "?")} {D(p, spawn):F0}m")));
            foreach (var w in Object.FindObjectsByType<WaterSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var mf = w.surface ? w.surface : w.GetComponentInChildren<MeshFilter>();
                var rb = w.GetComponentInChildren<Renderer>();
                float d = rb ? Vector3.Distance(Flat(rb.bounds.ClosestPoint(spawn)), Flat(spawn)) : -1f;
                sb.AppendLine($"water {w.name} '{w.displayName}' fresh {w.fresh} bounds {(rb ? rb.bounds.ToString() : "-")} closest to spawn {d:F1} m");
            }
            foreach (var c in Object.FindObjectsByType<Climbable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                sb.AppendLine($"climbable {PathOf(c.transform)} {V(c.transform.position)} fruit {c.GetComponentsInChildren<FruitCluster>(true).Length} dist {Vector3.Distance(c.transform.position, spawn):F0}");
            var bushes = Object.FindObjectsByType<BushInteraction>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            sb.AppendLine($"bushes {bushes.Length}, berry nodes {bushes.Count(b => b.GetComponent<ResourceNode>())}, within 60 m of spawn {bushes.Count(b => D(b, spawn) < 60)} (berries {bushes.Count(b => b.GetComponent<ResourceNode>() && D(b, spawn) < 60)})");
            var bb = bushes.FirstOrDefault(b => b.GetComponent<ResourceNode>());
            if (bb) foreach (var r in bb.GetComponentsInChildren<Renderer>(true)) sb.AppendLine($"   berry bush part {PathOf(r.transform)} mats {string.Join(",", r.sharedMaterials.Select(m => m ? m.name + "/" + m.shader.name : "-"))} col {bb.GetComponent<Collider>()?.GetType().Name}");
            // terrain
            var terrain = Terrain.activeTerrain;
            if (terrain)
            {
                var td = terrain.terrainData;
                sb.AppendLine($"terrain {terrain.name} pos {V(terrain.transform.position)} size {V(td.size)} layers {string.Join(",", td.terrainLayers.Select(l => l ? l.name : "-"))} alphamap {td.alphamapResolution}");
                sb.AppendLine($"trees {td.treeInstanceCount} prototypes {string.Join(",", td.treePrototypes.Select(p => p.prefab ? p.prefab.name : "-"))}");
                int treesNear = 0; foreach (var ti in td.treeInstances) { var p = Vector3.Scale(ti.position, td.size) + terrain.transform.position; if (Vector3.Distance(Flat(p), Flat(spawn)) < 60) treesNear++; }
                sb.AppendLine($"trees within 60 m of spawn {treesNear}");
                sb.AppendLine($"details {string.Join(",", td.detailPrototypes.Select(p => p.prototype ? p.prototype.name : p.prototypeTexture ? p.prototypeTexture.name : "-"))}");
            }
            var ocean = PrimalFrontier.Core.SceneRoots.LegacyObject("Water");
            if (ocean) foreach (Transform t in ocean.transform) sb.AppendLine($"water obj {t.name} y {t.position.y:F2}");
            // decoration that looks gatherable
            var sus = new Regex("Rock|Stone|Pebble|Log|Branch|Wood|Drift|Fern|Grass|Reed|Stick|Twig", RegexOptions.IgnoreCase);
            var deco = new Dictionary<string, int>();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (r.GetComponentInParent<ResourceNode>() || r.GetComponentInParent<WorldPickup>()) continue;
                var root = PrefabUtility.GetOutermostPrefabInstanceRoot(r.gameObject); var t = root ? root.transform : r.transform;
                if (!sus.IsMatch(t.name)) continue;
                string k = (t.parent ? t.parent.name + "/" : "") + Kind(t.name);
                deco[k] = deco.TryGetValue(k, out var c) ? c + 1 : 1;
            }
            sb.AppendLine("decoration that looks gatherable (no node):");
            foreach (var kv in deco.OrderByDescending(k => k.Value)) sb.AppendLine($"   {kv.Key}: {kv.Value} renderers");
            Directory.CreateDirectory(DocDir);
            File.WriteAllText(System.IO.Path.Combine(DocDir, "scene_audit" + (string.IsNullOrEmpty(arg) ? "" : "_" + arg) + ".txt"), sb.ToString());
            return sb.ToString();
        }

        /// <summary>arg: comma-separated prefab / model asset paths: bounds, renderers, materials, colliders, LODs</summary>
        [PrimalBridgeCommand]
        public static string ProbeAssets(string arg)
        {
            var sb = new StringBuilder();
            foreach (var path in arg.Split(','))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path.Trim());
                if (!go) { sb.AppendLine("missing " + path); continue; }
                var inst = (GameObject)Object.Instantiate(go); inst.transform.position = Vector3.zero;
                var b = Bounds(inst);
                sb.AppendLine($"{path}: bounds {b.size.x:F2}x{b.size.y:F2}x{b.size.z:F2} center {V(b.center)} lod {inst.GetComponentInChildren<LODGroup>() != null}");
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    var mf = r.GetComponent<MeshFilter>();
                    sb.AppendLine($"   {PathOf(r.transform)} {r.GetType().Name} mesh {(mf && mf.sharedMesh ? mf.sharedMesh.name + " sub" + mf.sharedMesh.subMeshCount + " v" + mf.sharedMesh.vertexCount : "-")} mats {string.Join(",", r.sharedMaterials.Select(m => m ? m.name + "/" + m.shader.name : "-"))}");
                }
                foreach (var c in inst.GetComponentsInChildren<Collider>(true)) sb.AppendLine($"   collider {PathOf(c.transform)} {c.GetType().Name} trigger {c.isTrigger} layer {LayerMask.LayerToName(c.gameObject.layer)}");
                foreach (var m in inst.GetComponentsInChildren<MonoBehaviour>(true)) sb.AppendLine($"   component {PathOf(m.transform)} {m.GetType().Name}");
                Object.DestroyImmediate(inst);
            }
            return sb.ToString();
        }

        static string Kind(string name) => Regex.Replace(name, @"(\s*\(\d+\))|(_\d+)$|(\.\d+)$", "");
        static float D(Component c, Vector3 p) { var a = c.transform.position; a.y = 0; p.y = 0; return Vector3.Distance(a, p); }
        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
        static string V(Vector3 v) => $"({v.x:F1},{v.y:F1},{v.z:F1})";
        static string PathOf(Transform t) { var s = t.name; while (t.parent) { t = t.parent; s = t.name + "/" + s; } return s; }
        public static Bounds Bounds(GameObject g)
        {
            var rs = g.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) return new Bounds(g.transform.position, Vector3.one * 0.3f);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }
    }
}
