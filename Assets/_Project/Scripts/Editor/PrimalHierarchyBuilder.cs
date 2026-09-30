using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PrimalFrontier.Core;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// Scene hierarchy organiser (agent HIER, Phase 1 wave 2b). Read-only commands: Dump ("levels"), Audit ("before" /
    /// "after": missing scripts + missing object references + world transform snapshot), FindProbe (Unity path lookup
    /// semantics). Apply ("dry" = report only) moves the existing containers into the owner's layout.
    /// </summary>
    public static class PrimalHierarchyBuilder
    {
        const string OutDir = "Library/PrimalBridge";

        // ------------------------------------------------------------------ dump
        [PrimalBridgeCommand]
        public static string Dump(string arg)
        {
            // arg "levels" or "path:levels" (path walked from a root, inactive included)
            string start = null; if (!string.IsNullOrEmpty(arg) && arg.Contains(":")) { start = arg.Substring(0, arg.LastIndexOf(':')); arg = arg.Substring(arg.LastIndexOf(':') + 1); }
            int levels = int.TryParse(arg, out var l) ? l : 2;
            var sb = new StringBuilder();
            var scene = EditorSceneManager.GetActiveScene();
            sb.AppendLine($"scene {scene.path} dirty={scene.isDirty} roots {scene.rootCount}");
            if (start != null)
            {
                var parts = start.Split('/'); var r0 = scene.GetRootGameObjects().FirstOrDefault(g => g.name == parts[0]);
                var t = r0 ? r0.transform : null; for (int i = 1; t && i < parts.Length; i++) t = t.Find(parts[i]);
                if (t) DumpRec(sb, t, 0, levels); else sb.AppendLine("not found " + start);
            }
            else foreach (var r in scene.GetRootGameObjects()) DumpRec(sb, r.transform, 0, levels);
            File.WriteAllText(Path.Combine(OutDir, "H_dump.txt"), sb.ToString());
            return sb.ToString();
        }

        static void DumpRec(StringBuilder sb, Transform t, int depth, int levels)
        {
            int desc = t.GetComponentsInChildren<Transform>(true).Length - 1;
            var comps = t.GetComponents<Component>().Where(c => !(c is Transform)).Select(c => c ? c.GetType().Name : "MISSING");
            bool pfb = PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject);
            sb.AppendLine($"{new string(' ', depth * 2)}{t.name}{(t.gameObject.activeSelf ? "" : " (off)")}{(pfb ? " [prefab]" : "")} [{desc}] {{{string.Join(",", comps)}}}");
            if (depth + 1 > levels) return;
            int shown = 0;
            foreach (Transform c in t)
            {
                if (shown >= 25) { sb.AppendLine($"{new string(' ', (depth + 1) * 2)}... {t.childCount - shown} more"); break; }
                DumpRec(sb, c, depth + 1, levels); shown++;
            }
        }

        /// <summary>every MonoBehaviour outside the UI whose type looks like a system (for choosing WorldSystems / Managers slots)</summary>
        [PrimalBridgeCommand]
        public static string Systems()
        {
            var sb = new StringBuilder();
            var scene = EditorSceneManager.GetActiveScene();
            var seen = new HashSet<string>();
            foreach (var r in scene.GetRootGameObjects())
                foreach (var m in r.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (!m) continue; var n = m.GetType().Name;
                    if (!System.Text.RegularExpressions.Regex.IsMatch(n, "Manager|System|Director|Pool|Player$|Spawner|Save|Clock|Planner|Controller|Journal|Tutorial|Intro|Mission|Ambience|Sfx|Decal|Sky|Weather|Perception|Stimuli|Wildlife|Migration|Herd|Zone|Camera")) continue;
                    var p = PathOf(m.transform) + " : " + n; if (seen.Add(p)) sb.AppendLine(p);
                }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ probe of Unity path semantics
        [PrimalBridgeCommand]
        public static string FindProbe()
        {
            var sb = new StringBuilder();
            foreach (var q in new[] { "Environment/Water", "/Environment/Water", "World/Environment/Water", "/World/Environment/Water", "Rocks", "World/Rocks", "/Rocks", "Environment", "Water", "Zones", "Markers/Zones", "Resources" })
            {
                var g = GameObject.Find(q);
                sb.AppendLine($"GameObject.Find(\"{q}\") -> {(g ? PathOf(g.transform) : "null")}");
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ audit
        [PrimalBridgeCommand]
        public static string Audit(string arg)
        {
            string tag = string.IsNullOrEmpty(arg) ? "now" : arg;
            var scene = EditorSceneManager.GetActiveScene();
            var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
            int missingScripts = 0, missingRefs = 0, comps = 0;
            var refLines = new StringBuilder(); var pos = new StringBuilder();
            foreach (var t in all)
            {
                missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                foreach (var c in t.GetComponents<Component>())
                {
                    if (!c) continue; comps++;
                    var so = new SerializedObject(c); var it = so.GetIterator();
                    while (it.Next(true))
                    {
                        if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                        if (it.objectReferenceValue == null && it.objectReferenceInstanceIDValue != 0)
                        { missingRefs++; if (missingRefs <= 200) refLines.AppendLine($"{PathOf(t)} : {c.GetType().Name}.{it.propertyPath}"); }
                    }
                }
                var gid = GlobalObjectId.GetGlobalObjectIdSlow(t.gameObject).ToString();
                var p = t.position; var q = t.rotation;
                pos.AppendLine($"{gid}\t{p.x:F4}\t{p.y:F4}\t{p.z:F4}\t{q.x:F4}\t{q.y:F4}\t{q.z:F4}\t{q.w:F4}\t{PathOf(t)}");
            }
            File.WriteAllText(Path.Combine(OutDir, $"H_pos_{tag}.txt"), pos.ToString());
            File.WriteAllText(Path.Combine(OutDir, $"H_refs_{tag}.txt"), refLines.ToString());
            return $"audit {tag}: objects {all.Count}, components {comps}, missing scripts {missingScripts}, missing object refs {missingRefs}";
        }

        // ------------------------------------------------------------------ verify (read-only)
        /// <summary>resolves every hierarchy lookup the code makes (old paths through SceneRoots, names through GameObject.Find),
        /// lists where the systems are, the rock twins, and compares H_pos_before / H_pos_after (Audit) when both exist</summary>
        [PrimalBridgeCommand]
        public static string Verify(string arg)
        {
            var sb = new StringBuilder(); int ok = 0, bad = 0;
            var scene = EditorSceneManager.GetActiveScene();
            sb.AppendLine($"scene {scene.path} dirty={scene.isDirty}; roots: {string.Join(", ", scene.GetRootGameObjects().Select(r => r.name))}");
            void R(string label, Transform t, bool expected = true)
            {
                bool found = t; if (found == expected) ok++; else bad++;
                sb.AppendLine($"{(found == expected ? "ok " : "BAD")} {label} -> {(found ? PathOf(t) : "not found")}");
            }
            string[] legacy =
            {
                "Markers", "Markers/Zones", "Markers/Migration", "Markers/Habitats", "Markers/ResourceAreas",
                "[Atmosphere]", "[Atmosphere]/Emitters", "[Atmosphere]/Hazards", "[Atmosphere]/Reverb", "[Atmosphere]/Sky",
                "Water", "Water/ENV_Ocean", "Water/PF_Ocean", "Water/PF_OceanShore", "Water/ENV_River_Water", "Water/ENV_LowerRiver_Water", "Water/ENV_RiverMouth_Water",
                "Water/ENV_SpringStream_Water", "Water/ENV_Stream_Water", "Water/ENV_Wetland_Water", "Water/ENV_Pond_Water", "Water/ENV_Spring_Water", "Water/ENV_WaterfallPool_Water",
                "World", "World/Vegetation", "World/Vegetation/InteractiveBushes", "World/Vegetation/Thickets", "World/Cliffs", "World/Rocks", "World/Resources",
                "World/Props", "World/Shipwreck", "World/Landmarks", "World/Landmarks/Volcano",
                "World/Environment/Water", "World/Environment/Water/Waterfall", "World/Environment/Water/Waterfall/RockFace", "World/Environment/Forest",
                "World/Environment/Rocks", "World/Environment/WaterEdge", "World/Environment/Volcano", "World/Environment/Volcano/Basalt", "World/Environment/Volcano/Lava",
                "World/Environment/Storytelling", "World/Environment/Storytelling/WreckRemains", "World/Environment/WaterfallDressing",
                "[Gameplay]", "[Gameplay]/[Dinosaurs]", "[Gameplay]/FruitTrees", "[Gameplay]/Climbables", "[Gameplay]/Cave", "[Gameplay]/CaptainsLog", "[Gameplay]/GiantFootprints",
                "[Gameplay]/[Game]", "[Gameplay]/[Zones]", "[Systems]", "[Systems]/Time", "[Systems]/Weather", "[Systems]/NightSky", "[Systems]/Input", "[Systems]/EventSystem",
                "[Resources]", "[Resources]/Shipwreck", "[Resources]/StartArea", "[UI]", "[UI]/[HUD]", "Player", "Main Camera", "ENV_Island_Terrain", "Sun", "Global Volume",
            };
            sb.AppendLine("-- old paths through SceneRoots.Legacy (builders + runtime)");
            foreach (var p in legacy) R($"Legacy(\"{p}\")", SceneRoots.Legacy(p));
            R("LegacyParent(\"[Systems]/NightSky\")", SceneRoots.LegacyParent("[Systems]/NightSky"));
            R("LegacyParent(\"[Resources]\")", SceneRoots.LegacyParent("[Resources]"));
            R("LegacyParent(\"[Gameplay]/[Dinosaurs]\")", SceneRoots.LegacyParent("[Gameplay]/[Dinosaurs]"));
            sb.AppendLine("-- name lookups left as GameObject.Find (active objects, anywhere)");
            foreach (var n in new[] { "World", "[Dinosaurs]", "[Resources]", "[Resources]/Shipwreck", "[UI]", "[UI]/[HUD]", "GiantFootprints", "Landmarks", "ENV_Island_Terrain", "ENV_Ocean",
                                      "ENV_Pond_Water", "Water/ENV_Ocean", "Water/PF_Ocean", "Markers/ResourceAreas", "ZONE_PlayerSpawn", "ZONE_Shipwreck", "ZONE_Camp", "ZONE_Meadow", "ZONE_Cave", "WaterfallSheet" })
            { var g = GameObject.Find(n); R($"GameObject.Find(\"{n}\")", g ? g.transform : null); }
            sb.AppendLine("-- old roots gone (expected not found as roots)");
            foreach (var n in new[] { "[Systems]", "[Gameplay]", "[Atmosphere]" }) R($"root {n}", SceneRoots.Find(n), false);
            sb.AppendLine("-- layout slots");
            foreach (var p in SceneRoots.Slots) R($"slot {p}", SceneRoots.Find(p));
            sb.AppendLine("-- runtime");
            var route = PrimalFrontier.AI.WildlifePlan.Route(); sb.AppendLine($"WildlifePlan.Route(): {route.Length} points"); if (route.Length >= 2) ok++; else bad++;
            bool mz = PrimalFrontier.AI.WildlifePlan.Resolve("meadow", out var mp, out _); sb.AppendLine($"WildlifePlan.Resolve(meadow): {mz} {mp}");
            var rr = PrimalFrontier.AI.MigrationDirector.ReadRoute(); sb.AppendLine($"MigrationDirector.ReadRoute(): {rr.Length} points"); if (rr.Length >= 2) ok++; else bad++;
            var kids = SceneRoots.LegacyChildren("[Gameplay]"); sb.AppendLine($"LegacyChildren([Gameplay]): {kids.Count}: {string.Join(", ", kids.Select(k => k.name))}");
            sb.AppendLine("-- systems");
            var want = new HashSet<string> { "TimeManager", "WeatherManager", "AmbienceManager", "JournalSystem", "TutorialManager", "IntroSequence", "BuildSystem", "OceanShore", "TreeHarvest",
                "PlayerInputReader", "VfxPool", "SfxPlayer", "BloodDecals", "EventSystem", "NightSky", "ZoneManager", "GameManager", "DinosaurSpawner", "UIManager", "ThirdPersonCamera", "CloudVeil", "WaterGlobals", "PlayerMotor" };
            foreach (var m in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true)))
                if (m && want.Remove(m.GetType().Name)) sb.AppendLine($"{m.GetType().Name}: {PathOf(m.transform)}");
            if (want.Count > 0) sb.AppendLine("NOT FOUND: " + string.Join(", ", want));
            var light = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(l => l.type == LightType.Directional).Select(l => PathOf(l.transform));
            sb.AppendLine($"directional lights: {string.Join(", ", light)}; RenderSettings.sun {(RenderSettings.sun ? PathOf(RenderSettings.sun.transform) : "none")}; Camera.main {(Camera.main ? PathOf(Camera.main.transform) : "none")}");
            sb.AppendLine("-- rocks named PFB_ENV_Rock_Large_03");
            foreach (var t in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Where(t => t.name.StartsWith("PFB_ENV_Rock_Large_03")))
            {
                string sid = "";
                foreach (var c in t.GetComponents<MonoBehaviour>()) { if (!c) continue; var p = new SerializedObject(c).FindProperty("saveId"); if (p != null) sid += $" {c.GetType().Name}.saveId='{p.stringValue}'"; }
                sb.AppendLine($"{PathOf(t)} at {t.position}{sid}");
            }
            // world pose comparison
            string fb = Path.Combine(OutDir, "H_pos_before.txt"), fa = Path.Combine(OutDir, "H_pos_after.txt");
            if (File.Exists(fb) && File.Exists(fa))
            {
                var b = File.ReadAllLines(fb).Select(l => l.Split('\t')).ToDictionary(x => x[0], x => x);
                var a = File.ReadAllLines(fa).Select(l => l.Split('\t')).ToDictionary(x => x[0], x => x);
                int common = 0, diff = 0; float maxd = 0; var samples = new List<string>();
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                float F(string s) => float.Parse(s, ci);
                foreach (var kv in b)
                {
                    if (!a.TryGetValue(kv.Key, out var y)) continue; common++;
                    var x = kv.Value; float d = 0; for (int i = 1; i <= 7; i++) d = Mathf.Max(d, Mathf.Abs(F(x[i]) - F(y[i])));
                    if (d > 0.0011f) diff++; maxd = Mathf.Max(maxd, d);
                    if (common % Mathf.Max(1, b.Count / 50) == 0 && samples.Count < 50) samples.Add($"  {x[8]} -> {y[8]} : ({x[1]}, {x[2]}, {x[3]}) -> ({y[1]}, {y[2]}, {y[3]})");
                }
                sb.AppendLine($"-- world poses: before {b.Count}, after {a.Count}, same object in both {common}, changed {diff}, max component delta {maxd:F4}; 50 samples:");
                foreach (var s in samples) sb.AppendLine(s);
            }
            sb.AppendLine($"lookups ok {ok}, bad {bad}");
            var res = sb.ToString(); File.WriteAllText(Path.Combine(OutDir, $"H_verify_{(string.IsNullOrEmpty(arg) ? "now" : arg)}.txt"), res);
            return res;
        }

        /// <summary>prefab instance roots by status (connected / missing asset) and stray prefab children</summary>
        [PrimalBridgeCommand]
        public static string PrefabCheck()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToList();
            var roots = all.Where(g => PrefabUtility.IsOutermostPrefabInstanceRoot(g)).ToList();
            var byStatus = roots.GroupBy(g => PrefabUtility.GetPrefabInstanceStatus(g)).Select(g => $"{g.Key} {g.Count()}");
            int missingAsset = roots.Count(g => PrefabUtility.IsPrefabAssetMissing(g));
            return $"prefab instance roots {roots.Count}: {string.Join(", ", byStatus)}; missing prefab asset {missingAsset}";
        }

        // ------------------------------------------------------------------ apply
        /// <summary>
        /// Moves the existing containers into the owner's layout (<see cref="SceneRoots"/>): world positions kept
        /// (SetParent worldPositionStays), prefab instances moved as a whole (never unpacked), object names kept (only the
        /// ENV forest group becomes Forest/ForestDressing), [Systems] / [Gameplay] / [Atmosphere] emptied into their slots and
        /// removed when empty and unreferenced. Idempotent. arg "dry" = report only. Does NOT save: run SaveScene after the check.
        /// </summary>
        [PrimalBridgeCommand]
        public static string Apply(string arg)
        {
            bool dry = arg == "dry";
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "stop Play mode first";
            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.path.EndsWith("Island_VerticalSlice.unity")) return "open Island_VerticalSlice first: " + scene.path;
            if (scene.isDirty && !dry && arg != "force") return "the scene has unsaved changes: save or revert them first (nothing changed)";
            var sb = new StringBuilder(); int moved = 0, created = 0, removed = 0, skipped = 0;
            sb.AppendLine(dry ? "DRY RUN (nothing changed)" : "APPLY");
            foreach (var p in new[] { "World", "World/Environment", "Water", "Markers", "[Resources]", "[Systems]", "[Gameplay]", "[Atmosphere]", "[UI]", "World/Environment/Water", "World/Environment/Volcano" })
            {
                var t = SceneRoots.Find(p);
                if (t) sb.AppendLine($"container {p}: pos {t.position} rot {t.rotation.eulerAngles} scale {t.lossyScale}{(IsIdentity(t) ? "" : "  NOT IDENTITY")}");
            }
            var before = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToDictionary(t => t.GetInstanceID(), t => (t.position, t.rotation));

            var entries = SceneRoots.Moves.Select((m, i) => (m.from, m.to, i, depth: m.from.Count(ch => ch == '/'))).ToList();
            bool IsSlot(string p) => System.Array.IndexOf(SceneRoots.Slots, p) >= 0;
            string Leaf(string p) => p.Substring(p.LastIndexOf('/') + 1);
            string ParentPath(string p) { int s = p.LastIndexOf('/'); return s < 0 ? null : p.Substring(0, s); }

            var done = new HashSet<Transform>();
            void MoveTo(Transform t, string parentPath, string label)
            {
                if (!done.Add(t)) return;
                var go = t.gameObject;
                if (PrefabUtility.IsPartOfPrefabInstance(go) && PrefabUtility.GetOutermostPrefabInstanceRoot(go) != go)
                { sb.AppendLine($"  SKIP {label}: inside a prefab instance"); skipped++; return; }
                var parent = parentPath == null ? null : SceneRoots.Find(parentPath, false);
                if (parent && parent.Cast<Transform>().Any(c => c != t && c.name == t.name) && !t.name.StartsWith("Pickup_"))
                { sb.AppendLine($"  CONFLICT {label}: {parentPath} already has a {t.name}, not moved"); skipped++; return; }
                sb.AppendLine($"  move {label} -> {(parentPath ?? "(root)")}/{t.name}");
                moved++;
                if (dry) return;
                if (!parent && parentPath != null) { parent = SceneRoots.Find(parentPath, true); }
                t.SetParent(parent, true);
            }

            // 1. a group that becomes a child of a new slot with its own old path (ENV's Forest -> Forest/ForestDressing): rename first
            foreach (var e in entries.Where(e => e.to.StartsWith(e.from + "/")))
            {
                var t = SceneRoots.Find(e.from);
                if (!t || IsSlot(e.from) && SceneRoots.Find(e.to)) continue;
                if (t.GetComponents<Component>().Length > 1 || t.Cast<Transform>().Any(c => c.name == Leaf(e.to))) continue;
                sb.AppendLine($"  rename {e.from} -> {Leaf(e.to)}, then into the new slot {e.from}");
                moved++;
                if (dry) continue;
                t.name = Leaf(e.to);
                var slot = SceneRoots.Find(e.from, true); slot.SetSiblingIndex(t.GetSiblingIndex());
                t.SetParent(slot, true);
            }
            // 2. plain moves, deepest old path first (children leave a moved container before it moves)
            foreach (var e in entries.Where(e => !e.to.StartsWith(e.from + "/") && Leaf(e.from) == Leaf(e.to)).OrderByDescending(e => e.depth).ThenBy(e => e.i))
                foreach (var t in FindAll(scene, e.from))
                    MoveTo(t, ParentPath(e.to), e.from);
            // 3. old containers that are dissolved into a slot: their remaining children go into the slot
            var dissolved = entries.Where(e => Leaf(e.from) != Leaf(e.to) && !e.to.StartsWith(e.from + "/")).ToList();
            foreach (var e in dissolved)
            {
                var c = SceneRoots.Find(e.from); if (!c) continue;
                foreach (var ch in c.Cast<Transform>().ToList()) MoveTo(ch, e.to, e.from + "/" + ch.name);
            }
            // 4. empty slots
            foreach (var p in SceneRoots.Slots)
                if (!SceneRoots.Find(p)) { created++; sb.AppendLine($"  new slot {p}"); if (!dry) SceneRoots.Find(p, true); }
            // 5. remove the emptied old containers (only when empty, no component, nothing points at them)
            if (!dry)
            {
                var refs = ReferencedIds(scene);
                foreach (var e in dissolved)
                {
                    var c = SceneRoots.Find(e.from); if (!c) continue;
                    if (c.childCount > 0 || c.GetComponents<Component>().Length > 1 || refs.Contains(c.GetInstanceID()) || refs.Contains(c.gameObject.GetInstanceID()))
                    { sb.AppendLine($"  KEPT {e.from}: {c.childCount} children, {c.GetComponents<Component>().Length - 1} components, referenced {refs.Contains(c.GetInstanceID()) || refs.Contains(c.gameObject.GetInstanceID())}"); continue; }
                    sb.AppendLine($"  removed empty container {e.from}"); removed++;
                    Object.DestroyImmediate(c.gameObject);
                }
            }
            else foreach (var e in dissolved) if (SceneRoots.Find(e.from)) sb.AppendLine($"  would remove {e.from} once empty");
            // 6. order: roots, then the slots in the owner's order
            if (!dry)
            {
                for (int i = 0; i < SceneRoots.Roots.Length; i++) { var r = SceneRoots.Find(SceneRoots.Roots[i]); if (r) r.SetSiblingIndex(i); }
                foreach (var grp in SceneRoots.Slots.Where(p => p.Contains("/")).GroupBy(ParentPath))
                {
                    int k = 0;
                    foreach (var p in grp) { var t = SceneRoots.Find(p); if (t) t.SetSiblingIndex(k++); }
                }
            }
            var otherRoots = scene.GetRootGameObjects().Where(r => System.Array.IndexOf(SceneRoots.Roots, r.name) < 0).Select(r => r.name).ToList();
            sb.AppendLine($"roots now: {string.Join(", ", scene.GetRootGameObjects().Select(r => r.name))}");
            if (otherRoots.Count > 0) sb.AppendLine("other roots (not in the layout): " + string.Join(", ", otherRoots));
            // 7. world poses unchanged?
            float dp = 0f, dr = 0f; int changed = 0;
            foreach (var t in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)))
                if (before.TryGetValue(t.GetInstanceID(), out var b))
                {
                    float a = Vector3.Distance(b.position, t.position), q = Quaternion.Angle(b.rotation, t.rotation);
                    dp = Mathf.Max(dp, a); dr = Mathf.Max(dr, q); if (a > 1e-3f || q > 0.01f) changed++;
                }
            sb.AppendLine($"moved {moved}, new slots {created}, removed containers {removed}, skipped {skipped}; world poses: {changed} changed (max {dp:F5} m, {dr:F4} deg)");
            if (!dry) EditorSceneManager.MarkSceneDirty(scene);
            var res = sb.ToString(); File.WriteAllText(Path.Combine(OutDir, dry ? "H_apply_dry.txt" : "H_apply.txt"), res);
            return res;
        }

        static bool IsIdentity(Transform t) => t.position.sqrMagnitude < 1e-8f && Quaternion.Angle(t.rotation, Quaternion.identity) < 0.001f && (t.lossyScale - Vector3.one).sqrMagnitude < 1e-8f;

        /// <summary>every object at this exact path (duplicate names included), inactive too</summary>
        static List<Transform> FindAll(UnityEngine.SceneManagement.Scene scene, string path)
        {
            var parts = path.Split('/');
            var cur = scene.GetRootGameObjects().Where(r => r.name == parts[0]).Select(r => r.transform).ToList();
            for (int i = 1; i < parts.Length; i++) cur = cur.SelectMany(t => t.Cast<Transform>().Where(c => c.name == parts[i])).ToList();
            return cur;
        }

        /// <summary>instance ids of every object some serialized field in the scene points at</summary>
        static HashSet<int> ReferencedIds(UnityEngine.SceneManagement.Scene scene)
        {
            var ids = new HashSet<int>();
            foreach (var c in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Component>(true)))
            {
                if (!c || c is Transform) continue;
                var it = new SerializedObject(c).GetIterator();
                while (it.Next(true)) if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue) ids.Add(it.objectReferenceValue.GetInstanceID());
            }
            return ids;
        }

        public static string PathOf(Transform t) { var s = t.name; while (t.parent) { t = t.parent; s = t.name + "/" + s; } return s; }
    }
}
