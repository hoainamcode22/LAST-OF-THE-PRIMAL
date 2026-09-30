using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using PrimalFrontier.AI;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// Phase 2 (AI): wildlife of the six world zones. The layout itself is part of Build (WildlifePlan.Apply -> ApplyZones:
    /// zone groups, routines, no-go placement); this file adds the read-only checks.
    /// Bridge: PrimalWildlifeBuilder.Phase2Check "" | "navmesh" (also bakes a temporary NavMesh in memory for
    /// NavMesh.SamplePosition / CalculatePath; nothing is saved) | "survey" (extra: anchors, LOD, heat rings, flyers).
    /// </summary>
    public static partial class PrimalWildlifeBuilder
    {
        [PrimalBridgeCommand]
        public static string Phase2Check(string arg)
        {
            Log.Clear(); arg ??= "";
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "stop Play mode first";
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != Scene) return "open " + Scene + " first (active: " + scene.path + ")";
            var spawner = Object.FindFirstObjectByType<DinosaurSpawner>(FindObjectsInactive.Include);
            if (!spawner) return "no DinosaurSpawner";
            WildlifeZones.Invalidate();
            if (arg.Contains("survey")) Survey(spawner);
            ZoneChecks(spawner, arg.Contains("navmesh"));
            return Log.ToString();
        }

        // ------------------------------------------------------------------ survey (read-only facts for the report)
        static void Survey(DinosaurSpawner spawner)
        {
            L("== survey");
            var anchors = WildlifeZones.Anchors;
            L($"AI anchors (children of AI_Anchors objects): {anchors.Count}" + (anchors.Count > 0 ? ": " + string.Join(", ", anchors.Select(kv => kv.Key + " " + V(kv.Value.position))) : ""));
            foreach (var name in new[] { "Trail_A_RiverApproach", "Trail_B_GameTrail", "Trail_C_StalkerPath" })
            {
                var t = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).FirstOrDefault(x => x.name == name && x.childCount > 0);
                L($"trail {name}: {(t ? t.childCount + " WP from " + V(t.GetChild(0).position) + " to " + V(t.GetChild(t.childCount - 1).position) : "not found")}");
            }
            foreach (var h in Object.FindObjectsByType<PrimalFrontier.World.HazardZone>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                L($"heat ring {h.hazardId} {Path(h.transform)} at {V(h.transform.position)}: warm {h.warmRadius} hot {h.hotRadius} danger {h.dangerRadius}");
            var cam = Camera.main; float fov = cam ? cam.fieldOfView : 60f;
            L($"camera {(cam ? cam.name : "none")} fov {fov:0.#} far clip {(cam ? cam.farClipPlane : 0f):0}, QualitySettings.lodBias {QualitySettings.lodBias:0.##}");
            var pc = PerceptionConfig.Instance;
            var seen = new HashSet<string>();
            foreach (var d in spawner.GetComponentsInChildren<DinosaurController>(true))
            {
                if (!d.def || !seen.Add(d.def.id)) continue;
                var lg = d.GetComponentInChildren<LODGroup>(); var an = d.GetComponentInChildren<Animator>();
                var smr = d.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                float before = DinosaurController.CullDistance(lg, fov);
                float want = d.def.IsHerbivore ? pc.herdCullDistance : pc.creatureCullDistance;
                L($"species {d.def.id}: LODGroup {(lg ? $"{lg.lodCount} LODs, size {lg.size:0.0} m, last threshold {lg.GetLODs().Last().screenRelativeTransitionHeight:0.###}, culled at {before:0} m -> runtime {Mathf.Max(before, want):0} m" : "none")}; " +
                  $"Animator {(an ? an.cullingMode.ToString() : "none")} (runtime: CullUpdateTransforms, far tier stepped by hand); skinned renderers {smr.Length}, updateWhenOffscreen {smr.Count(s => s.updateWhenOffscreen)}");
            }
            foreach (var a in spawner.GetComponentsInChildren<AmbientCreature>(true))
                L($"flyer / swimmer {Path(a.transform)} ({(a.def ? a.def.id : "?")}): centre {V(a.center)} r {a.radius:0} alt {a.altitude:0}{(a.GetComponent<WildlifeRoutine>() ? " + routine " + a.GetComponent<WildlifeRoutine>().id : "")}");
            L($"NavMesh in the scene: {NavMesh.CalculateTriangulation().vertices.Length} vertices (the AI walks the terrain kinematically; see the ground path grid)");
        }

        // ------------------------------------------------------------------ zone checks
        static void ZoneChecks(DinosaurSpawner spawner, bool bakeNavMesh)
        {
            L("== Phase 2 zone checks");
            var land = spawner.GetComponentsInChildren<DinosaurController>(true).Where(d => d.def).ToList();
            var fly = spawner.GetComponentsInChildren<AmbientCreature>(true).Where(a => a.def).ToList();
            var routines = spawner.GetComponentsInChildren<WildlifeRoutine>(true).ToList();
            int bad = 0;
            // counts per zone (creatures standing / circling inside the zone circle + blend band, and routines with stops inside)
            foreach (var z in PrimalZonesBuilder.Zones)
            {
                var inside = land.Where(d => z.Weight(d.transform.position) > 0f).ToList();
                var air = fly.Where(a => !a.swimmer && z.Weight(a.center) > 0f).ToList();
                var visits = routines.Where(r => r.points != null && r.points.Any(p => z.Dist(p) <= z.radius)).ToList();
                string sp = string.Join(", ", inside.GroupBy(d => d.def.id).Select(gr => gr.Key + " " + gr.Count()));
                L($"zone {z.id} ({z.name}, r {z.radius} + {z.blend}): living here {inside.Count}{(inside.Count > 0 ? " [" + sp + "]" : "")}; flyers {air.Count}; " +
                  $"visitors {visits.Count}{(visits.Count > 0 ? " [" + string.Join(", ", visits.Select(r => r.id + " (" + r.name + ")")) + "]" : "")}");
            }
            // forbidden areas: nobody placed inside, no routine stop inside, no large creature in the cave
            int forb = 0;
            foreach (var d in land)
            {
                string why = WildlifeZones.ForbiddenWhy(d.def, d.transform.position);
                if (why != null) { L($"CHECK {d.name} at {V(d.transform.position)}: inside {why}"); forb++; }
            }
            foreach (var r in routines)
            {
                var dc = r.GetComponent<DinosaurController>();
                if (!dc || r.points == null) continue;
                for (int i = 0; i < r.points.Length; i++)
                {
                    string why = WildlifeZones.ForbiddenWhy(dc.def, r.points[i]);
                    if (why != null) { L($"CHECK routine {r.id} point {i} {V(r.points[i])}: inside {why}"); forb++; }
                }
            }
            var cave = PrimalZonesBuilder.ZoneById("cave");
            int inCave = cave != null ? land.Count(d => cave.Dist(d.transform.position) < cave.radius) : 0;
            if (inCave > 0) L($"CHECK {inCave} land creature(s) inside the Deep Water Cave zone");
            L($"forbidden areas: creatures / routine stops inside = {forb}; land creatures in the cave = {inCave}");
            bad += forb + inCave;
            // reachability on the ground grid: every creature to each of its routine points in turn, herds to their places
            int legs = 0, unreachable = 0;
            foreach (var r in routines)
            {
                var dc = r.GetComponent<DinosaurController>();
                if (!dc || r.points == null || r.points.Length == 0) continue;
                Vector3 prev = dc.transform.position;
                foreach (var p in r.points)
                {
                    legs++;
                    if (Flat(p - prev) > 3f && !WildlifePlan.Reachable(prev, p, dc.def, out _)) { unreachable++; L($"CHECK routine {r.id}: no ground path {V(prev)} -> {V(p)}"); }
                    prev = p;
                }
            }
            L($"ground path grid: {legs} routine legs, unreachable {unreachable}");
            bad += unreachable;
            // NavMesh.SamplePosition of every spawn point and routine stop (the scene has no NavMesh unless 'navmesh' bakes a temporary one)
            NavMeshData tmp = null; NavMeshDataInstance inst = default;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (bakeNavMesh) tmp = TempNavMesh(out inst);
            bool any = NavMesh.CalculateTriangulation().vertices.Length > 0;
            int onMesh = 0, off = 0, paths = 0, pathOk = 0;
            if (any)
            {
                foreach (var d in land)
                {
                    if (NavMesh.SamplePosition(d.transform.position, out var hit, 3f, NavMesh.AllAreas)) onMesh++; else { off++; L($"navmesh: {d.name} {V(d.transform.position)} not on the mesh (3 m)"); }
                }
                foreach (var r in routines)
                {
                    var dc = r.GetComponent<DinosaurController>(); if (!dc || r.points == null) continue;
                    Vector3 prev = dc.transform.position;
                    foreach (var p in r.points)
                    {
                        bool wade = r.actions != null && System.Array.IndexOf(r.points, p) < r.actions.Length && r.actions[System.Array.IndexOf(r.points, p)] == RoutineStop.Wade;
                        if (NavMesh.SamplePosition(p, out var h1, 4f, NavMesh.AllAreas) && NavMesh.SamplePosition(prev, out var h0, 4f, NavMesh.AllAreas))
                        {
                            paths++; var np = new NavMeshPath();
                            if (NavMesh.CalculatePath(h0.position, h1.position, NavMesh.AllAreas, np) && np.status == NavMeshPathStatus.PathComplete) pathOk++;
                            else L($"navmesh: routine {r.id} {V(prev)} -> {V(p)}: {np.status}");
                        }
                        else if (!wade) L($"navmesh: routine {r.id} point {V(p)} not on the mesh (4 m)");
                        prev = p;
                    }
                }
                L($"NavMesh.SamplePosition: creatures on the mesh {onMesh}/{land.Count}; routine legs with a complete NavMesh path {pathOk}/{paths}{(bakeNavMesh ? $" (temporary bake {sw.ElapsedMilliseconds} ms, removed again)" : "")}");
            }
            else L("NavMesh.SamplePosition: no NavMesh in the scene (creatures move kinematically on the terrain; run Phase2Check \"navmesh\" for a temporary bake)");
            if (tmp) { NavMesh.RemoveNavMeshData(inst); Object.DestroyImmediate(tmp); }
            // zone visibility (fern forest)
            var zones = PerceptionConfig.Instance.visibilityZones;
            for (int i = 0; zones != null && i < zones.Count; i++)
            {
                var vz = zones[i];
                L($"visibility zone {vz.id}: centre {V(WildlifeZones.Anchor(vz.anchor, vz.center))}{(WildlifeZones.TryAnchor(vz.anchor, out _) ? " (anchor " + vz.anchor + ")" : " (fallback)")}, r {vz.radius} + {vz.blend}, " +
                  $"player sight x{vz.playerSightMul}, creature sight x{vz.creatureSightMul}, crouched in dense plants x{1f - vz.denseCrouchCover:0.##} more; dense share of the core {WildlifeZones.DenseShare(i):P0}");
            }
            L(bad == 0 ? "zone checks: OK" : $"zone checks: {bad} problem(s)");
        }

        /// <summary>a NavMesh of the terrain and static colliders, in memory only (agent: 1 m radius, 3 m high, 30 degrees, 0.6 m step)</summary>
        static NavMeshData TempNavMesh(out NavMeshDataInstance inst)
        {
            inst = default;
            var t = Terrain.activeTerrain; if (!t) return null;
            var b = t.terrainData.bounds; b.center += t.transform.position; b.Expand(new Vector3(0f, 40f, 0f));
            var sources = new List<NavMeshBuildSource>();
            UnityEngine.AI.NavMeshBuilder.CollectSources(b, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
            var s = NavMesh.GetSettingsByID(0);
            s.agentRadius = 1f; s.agentHeight = 3f; s.agentSlope = 30f; s.agentClimb = 0.6f;
            var data = UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(s, sources, b, Vector3.zero, Quaternion.identity);
            if (!data) return null;
            inst = NavMesh.AddNavMeshData(data);
            L($"temporary NavMesh: {sources.Count} sources, {NavMesh.CalculateTriangulation().vertices.Length} vertices");
            return data;
        }

        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }
        static string V(Vector3 v) => $"({v.x:0.#}, {v.y:0.#}, {v.z:0.#})";
    }
}
