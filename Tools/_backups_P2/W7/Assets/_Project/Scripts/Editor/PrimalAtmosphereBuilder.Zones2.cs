using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PrimalFrontier.Core;
using PrimalFrontier.Story;
using PrimalFrontier.UI;
using PrimalFrontier.VFX;
using PrimalFrontier.World;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// Phase 2 (World Exploration), WORLD agent: bridge commands <c>PrimalAtmosphereBuilder.Zones2</c> (idempotent, saves the
    /// scene) and <c>Zones2Check</c> (read-only). Zones2 makes, for the six environments:
    /// 1. ZoneManager Region zones (circle / capsule, blend band, toast name, climate) and Landmark zones for each LM_&lt;Zone&gt;
    ///    marker the zone agents placed (found by sight or approach) and the cave's hidden chamber;
    /// 2. the story side: journal pages (SyncBuiltIns), survivor lines, minimap place list;
    /// 3. zone atmosphere: pooled particle prefabs (Prefabs/World/ZoneFx), a ZoneFxManager with its library and zone haze, and
    ///    ZoneFxAnchor objects from the zone agents' FX_ anchors (fallback: placed from env_features_v2.json / the zone shape);
    /// 4. zone sound: AmbienceManager zone beds and calls, 3D emitters (flies at carcasses, the cave stream / pool), cave
    ///    reverb (AudioReverbZone Cave), and a mild heat band in the foothills (warm ring only: no damage).
    /// Everything generated lives under World/Environment/Weather/Zones2 (cleared and rebuilt on each run); nothing owned by
    /// another agent is touched (their FX_ / LM_ objects are only read). Log: Documentation/Phase2/Logs/W_zones2.txt.
    /// </summary>
    public static partial class PrimalAtmosphereBuilder
    {
        const string Z2Root = SceneRoots.Weather + "/Zones2";
        const string Z2PrefabDir = "Assets/_Project/Prefabs/World/ZoneFx", Z2MatDir = "Assets/_Project/Art/World/ZoneFx";
        const string Z2Log = "Documentation/Phase2/Logs/W_zones2.txt";
        const string SfxDir = "Assets/_Project/Audio/SFX";

        class Region
        {
            public string id, name, group; public Vector3 centre, end; public float radius, blend;
            public float day, night, humidity; public bool indoor;
            public string landmark; public float lmReach, lmSight;
        }

        static Region R(string id, string name, string group, Vector3 c, float r, float blend, string lm, float reach, float sight, Vector3? end = null,
                        float day = 0f, float night = 0f, float humidity = 0f, bool indoor = false)
            => new Region { id = id, name = name, group = group, centre = c, end = end ?? c, radius = r, blend = blend, landmark = lm, lmReach = reach, lmSight = sight,
                            day = day, night = night, humidity = humidity, indoor = indoor };

        /// <summary>the six zones (Documentation/Phase2/PHASE2_OWNERSHIP.md); the cave's circle comes from CAVE's geometry when it exists</summary>
        static readonly Region[] Z2Regions =
        {
            R("migration_valley", "Migration Valley", SceneRoots.Forest + "/MigrationValley", new Vector3(0f, 9f, -35f), 95f, 30f, "lm_rock_ridge", 16f, 110f),
            R("prehistoric_wetland", "Prehistoric Wetland", SceneRoots.Wetlands + "/PrehistoricWetland", new Vector3(102f, 0.4f, 172f), 45f, 25f, "lm_fallen_tree", 12f, 50f, null, -1.5f, -0.5f, 0.9f),
            R("bone_valley", "Bone Valley", SceneRoots.Forest + "/BoneValley", new Vector3(-105f, 21f, -76f), 50f, 25f, "lm_fossil_skeleton", 14f, 60f),
            R("giant_fern_forest", "Giant Fern Forest", SceneRoots.Forest + "/GiantFernForest", new Vector3(168f, 8f, 20f), 70f, 30f, "lm_giant_tree", 14f, 80f, null, -2f, 1f, 0.8f),
            R("volcanic_foothills", "Volcanic Foothills", SceneRoots.Volcano + "/VolcanicFoothills", new Vector3(-136f, 23f, -125f), 40f, 30f, "lm_black_ridge", 18f, 110f, new Vector3(-122f, 44f, -216f)),
            R("deep_water_cave", "Deep Water Cave", SceneRoots.Caves + "/DeepWaterCave", new Vector3(-18f, 18f, -131f), 22f, 10f, "lm_underground_pool", 10f, 28f, null, 0f, 0f, 0.85f, true),
        };
        static string ZoneGroupName(Region r) => r.group.Substring(r.group.LastIndexOf('/') + 1);

        static StringBuilder _z2;
        static int _z2Warn;
        static void Z(string s) { _z2.AppendLine(s); Debug.Log("[PrimalAtmosphere.Zones2] " + s); }
        static void ZW(string s) { _z2Warn++; _z2.AppendLine("WARNING: " + s); Debug.LogWarning("[PrimalAtmosphere.Zones2] " + s); }
        static string V3(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "({0:0.#}, {1:0.#}, {2:0.#})", v.x, v.y, v.z);

        // ================================================================== Zones2
        [PrimalBridgeCommand]
        public static string Zones2(string arg)
        {
            _z2 = new StringBuilder(); _z2Warn = 0;
            Z($"# PrimalAtmosphereBuilder.Zones2 {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            var log = new StringBuilder();
            if (!OpenScene(log)) { Z(log.ToString()); return Finish(); }
            var scene = EditorSceneManager.GetActiveScene();
            var zm = UnityEngine.Object.FindFirstObjectByType<ZoneManager>();
            if (!zm) { ZW("no ZoneManager in the scene: nothing done"); return Finish(); }
            var terrain = Terrain.activeTerrain;

            // ---- 1. zones
            Z("## Zones");
            var cave = CaveShape(terrain);
            var keep = new HashSet<string>();
            foreach (var r in Z2Regions)
            {
                var seg = (r.end - r.centre) * 0.5f; var mid = r.centre + seg;
                float radius = r.radius; bool band = false; float minY = -1000f, maxY = 1000f;
                if (r.id == "deep_water_cave" && cave.found) { mid = cave.centre; radius = cave.radius; band = true; minY = cave.minY; maxY = cave.maxY; seg = Vector3.zero; }
                var z = zm.Register(r.id, r.name, mid, radius, r.indoor, r.day);
                z.kind = ZoneManager.ZoneKind.Region; z.segment = new Vector3(seg.x, 0f, seg.z); z.blend = r.blend; z.announce = true;
                z.separateNight = r.day != 0f || r.night != 0f; z.nightTemperatureOffset = r.night; z.humidity = r.humidity;
                z.stableAir = r.indoor; z.stableAirTemperature = 15f; z.stableAirWeight = 0.8f;
                z.heightBand = band; z.minY = minY; z.maxY = maxY; z.sightRange = 0f;
                keep.Add(r.id);
                Z($"- {r.id} \"{r.name}\": centre {V3(mid)}{(seg.sqrMagnitude > 1f ? $", capsule +/- {V3(seg)}" : "")}, r {radius:0.#} + blend {r.blend:0}"
                  + (band ? $", height band {minY:0.#}..{maxY:0.#}" : "") + (r.indoor ? ", indoor, stable air 15 C" : "")
                  + (r.humidity > 0f ? $", humidity {r.humidity:0.##}" : "") + (r.day != 0f || r.night != 0f ? $", day {r.day:+0.#;-0.#} / night {r.night:+0.#;-0.#} C" : ""));
                if (r.id == "deep_water_cave" && !cave.found) ZW("deep_water_cave: no cave geometry under " + r.group + " yet: default circle at the cave mouth (re-run after CAVE)");
            }
            // landmarks (LM_<Zone> placed by the zone agents) and the hidden chamber
            foreach (var r in Z2Regions)
            {
                var lm = FindInScene("LM_" + ZoneGroupName(r));
                if (!lm) { ZW($"{r.landmark}: LM_{ZoneGroupName(r)} not in the scene yet ({r.group}): landmark page waits (re-run Zones2)"); continue; }
                var (pos, h, size) = LandmarkShape(lm);
                var z = zm.Register(r.landmark, StoryTexts.Landmark(r.landmark)?.title ?? r.landmark, pos, r.lmReach);
                z.kind = ZoneManager.ZoneKind.Landmark; z.segment = Vector3.zero; z.blend = 0f; z.announce = false; z.humidity = 0f; z.separateNight = false;
                z.sightRange = r.lmSight; z.sightHeight = h; z.sightSize = size;
                bool under = r.id == "deep_water_cave";
                z.heightBand = under; z.minY = pos.y - 8f; z.maxY = pos.y + 12f;
                keep.Add(r.landmark);
                Z($"- landmark {r.landmark} at {V3(pos)} ({SceneRoots.PathOf(lm)}): found within {r.lmReach:0} m or seen from {r.lmSight:0} m (look point +{h:0.#} m, size {size:0.#} m)");
            }
            var chamber = FindHiddenChamber();
            if (chamber)
            {
                var z = zm.Register("cave_hidden_chamber", StoryTexts.Landmark("cave_hidden_chamber")?.title ?? "The Hidden Chamber", chamber.position, 4.5f);
                z.kind = ZoneManager.ZoneKind.Landmark; z.segment = Vector3.zero; z.blend = 0f; z.announce = false; z.sightRange = 0f;
                z.heightBand = true; z.minY = chamber.position.y - 4f; z.maxY = chamber.position.y + 6f;
                keep.Add("cave_hidden_chamber");
                Z($"- hidden chamber at {V3(chamber.position)} ({SceneRoots.PathOf(chamber)}): found within 4.5 m (height band)");
            }
            else ZW("cave_hidden_chamber: no *HiddenChamber* object under " + SceneRoots.Caves + "/DeepWaterCave yet (CAVE): page waits");
            // my zones from an earlier run that no longer have a source
            int dropped = zm.zones.RemoveAll(q => q != null && q.kind != ZoneManager.ZoneKind.Place && !keep.Contains(q.id) &&
                                                   (Array.IndexOf(StoryIds.Regions, q.id) >= 0 || Array.IndexOf(StoryIds.Landmarks, q.id) >= 0));
            EditorUtility.SetDirty(zm);
            Z($"- ZoneManager: {zm.zones.Count(q => q.kind == ZoneManager.ZoneKind.Region)} regions, {zm.zones.Count(q => q.kind == ZoneManager.ZoneKind.Landmark)} landmarks, {zm.zones.Count} zones in all" + (dropped > 0 ? $", {dropped} stale removed" : ""));

            // ---- 2. story
            Z("## Story");
            StorySide();

            // ---- 3. + 4. generated scene content
            var root = SceneRoots.Find(Z2Root, true);
            int cleared = 0;
            for (int i = root.childCount - 1; i >= 0; i--) { UnityEngine.Object.DestroyImmediate(root.GetChild(i).gameObject); cleared++; }
            Z($"## Scene content ({Z2Root}: {cleared} old children cleared)");
            var mats = Z2Materials();
            var lib = Z2Prefabs(mats);
            var mgr = root.GetComponent<ZoneFxManager>() ?? root.gameObject.AddComponent<ZoneFxManager>();
            mgr.library = lib; mgr.haze = Z2Haze();
            EditorUtility.SetDirty(mgr);
            Z($"- ZoneFxManager: {lib.Count} pooled kinds ({lib.Sum(q => q.pool)} systems at play start), {mgr.haze.Count} zone hazes");
            Z2Import();
            int anchors = 0, emit = 0, rev = 0, hz = 0;
            foreach (var r in Z2Regions)
            {
                var zone = zm.Find(r.id);
                var g = new GameObject(ZoneGroupName(r)).transform; g.SetParent(root, false);
                var list = new List<(ZoneFxKind k, Vector3 p, float rad, float h, string src)>();
                AgentAnchors(r, list);
                FallbackAnchors(r, zone, terrain, cave, list);
                var counts = new Dictionary<ZoneFxKind, int>();
                foreach (var fx in list)
                {
                    counts.TryGetValue(fx.k, out int n); counts[fx.k] = n + 1;
                    var go = new GameObject($"FX_{fx.k}_{n}"); go.transform.SetParent(g, false); go.transform.position = fx.p;
                    var an = go.AddComponent<ZoneFxAnchor>(); an.kind = fx.k; an.radius = fx.rad; an.height = fx.h;
                    an.strength = zone != null ? Mathf.Clamp(Mathf.Max(0.35f, zone.Weight(fx.p)), 0f, 1f) : 1f;
                    anchors++;
                }
                Z($"- {ZoneGroupName(r)} FX: " + (counts.Count == 0 ? "none" : string.Join(", ", counts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value}")))
                  + $" (from agent anchors: {list.Count(q => q.src == "agent")})");
                foreach (var fx in list.Where(q => q.src == "agent")) Z($"  - agent anchor -> {fx.k} at {V3(fx.p)}");
                emit += Z2Emitters(r, g, list, cave);
                rev += Z2Reverb(r, g, zone, cave);
                hz += Z2Heat(r, g, terrain);
            }
            Z($"- totals: {anchors} FX anchors, {emit} emitters, {rev} reverb zones, {hz} heat band zones");
            Z2Audio();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Z("scene saved");
            Z("## Check");
            Z2CheckInto();
            return Finish();
        }

        static string Finish()
        {
            Z($"finished, {_z2Warn} warning(s)");
            Directory.CreateDirectory(Path.GetDirectoryName(Z2Log));
            File.AppendAllText(Z2Log, _z2 + "\n");
            return _z2.ToString();
        }

        [PrimalBridgeCommand]
        public static string Zones2Check(string arg)
        {
            _z2 = new StringBuilder(); _z2Warn = 0;
            Z($"# PrimalAtmosphereBuilder.Zones2Check {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Z2CheckInto();
            Z($"finished, {_z2Warn} problem(s)");
            return _z2.ToString();
        }

        // ------------------------------------------------------------------ scene lookups (read only)
        static Transform FindInScene(string name)
        {
            foreach (var r in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var t in r.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        static (Vector3 pos, float h, float size) LandmarkShape(Transform lm)
        {
            var rs = lm.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return (lm.position, 3f, 4f);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            var pos = new Vector3(b.center.x, lm.position.y, b.center.z);
            float h = Mathf.Clamp(b.center.y - pos.y, 1f, 40f);
            float size = Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.y, b.extents.z) * 0.9f, 3f, 30f);
            return (pos, h, size);
        }

        struct CaveInfo { public bool found; public Vector3 centre; public float radius, minY, maxY; public Transform group; }

        /// <summary>the cave region from CAVE's geometry (renderers under Caves/DeepWaterCave), else not found</summary>
        static CaveInfo CaveShape(Terrain terrain)
        {
            var g = SceneRoots.Find(SceneRoots.Caves + "/DeepWaterCave");
            var info = new CaveInfo { group = g };
            if (!g) return info;
            var rs = g.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).ToArray();
            if (rs.Length == 0) return info;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            info.found = true; info.centre = new Vector3(b.center.x, b.min.y, b.center.z);
            info.radius = Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.z) + 2f, 8f, 80f);
            info.minY = b.min.y - 3f; info.maxY = b.max.y + 0.5f;
            return info;
        }

        static Transform FindHiddenChamber()
        {
            var g = SceneRoots.Find(SceneRoots.Caves + "/DeepWaterCave");
            if (!g) return null;
            foreach (var t in g.GetComponentsInChildren<Transform>(true))
            {
                var n = t.name.ToLowerInvariant().Replace("_", "");
                if (n.Contains("hiddenchamber") && (n.StartsWith("fx") || n.StartsWith("lm") || n.StartsWith("mk") || n.StartsWith("marker") || t.childCount == 0 || t.GetComponent<Renderer>() == null)) return t;
            }
            foreach (var t in g.GetComponentsInChildren<Transform>(true)) if (t.name.ToLowerInvariant().Replace("_", "").Contains("hiddenchamber")) return t;
            return null;
        }

        // ------------------------------------------------------------------ story
        static void StorySide()
        {
            var j = UnityEngine.Object.FindFirstObjectByType<JournalSystem>(FindObjectsInactive.Include);
            if (j)
            {
                int before = j.entries.Count; j.SyncBuiltIns(); EditorUtility.SetDirty(j);
                Z($"- journal: {j.entries.Count} pages ({j.entries.Count - before} added)");
            }
            else ZW("no JournalSystem in the scene (STORY builder): pages come from the built-in list at run time");
            var v = UnityEngine.Object.FindFirstObjectByType<ProtagonistVoice>(FindObjectsInactive.Include);
            if (v)
            {
                int n = 0;
                foreach (var l in ProtagonistVoice.Defaults()) if (!v.lines.Exists(x => x != null && x.id == l.id)) { v.lines.Add(l); n++; }
                EditorUtility.SetDirty(v);
                Z($"- survivor lines: {v.lines.Count} ({n} added)");
            }
            else ZW("no ProtagonistVoice in the scene: Bone Valley's line needs it");
            var mm = UnityEngine.Object.FindFirstObjectByType<Minimap>(FindObjectsInactive.Include);
            if (mm)
            {
                var ids = StoryIds.Split(mm.markedPlaces).ToList(); int n = 0;
                foreach (var id in StoryIds.Landmarks) if (!ids.Contains(id)) { ids.Add(id); n++; }
                mm.markedPlaces = string.Join("|", ids); EditorUtility.SetDirty(mm);
                Z($"- minimap: {ids.Count} marked places ({n} added); region names are map labels once entered");
            }
            else ZW("no Minimap in the scene");
        }

        // ------------------------------------------------------------------ materials / prefabs
        static Dictionary<string, Material> Z2Materials()
        {
            var d = new Dictionary<string, Material>();
            var soft = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/VFX/Materials/M_VFX_Soft.mat");
            d["soft"] = soft;
            d["add"] = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/VFX/Materials/M_VFX_Additive.mat");
            d["drop"] = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/VFX/Materials/M_VFX_Drop.mat");
            d["heat"] = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/VFX/Materials/M_VFX_HeatShimmer.mat");
            // fog: the soft particle material with camera fading, so a fog puff never fills the screen when walked through
            Material fog = null;
            if (soft)
            {
                Directory.CreateDirectory(Z2MatDir);
                string path = Z2MatDir + "/M_Z2_Fog.mat";
                fog = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!fog) { fog = new Material(soft) { name = "M_Z2_Fog" }; AssetDatabase.CreateAsset(fog, path); Z("- created " + path); }
                else if (fog.shader != soft.shader) fog.shader = soft.shader;
                fog.CopyPropertiesFromMaterial(soft);
                fog.SetFloat("_CameraFadingEnabled", 1f); fog.SetFloat("_CameraNearFadeDistance", 1.5f); fog.SetFloat("_CameraFarFadeDistance", 7f);
                fog.SetVector("_CameraFadeParams", new Vector4(1.5f, 1f / (7f - 1.5f), 0f, 0f));
                fog.EnableKeyword("_FADING_ON");
                EditorUtility.SetDirty(fog);
            }
            d["fog"] = fog ? fog : soft;
            foreach (var kv in d) if (!kv.Value) ZW($"material '{kv.Key}' missing (VFX/Materials)");
            return d;
        }

        static List<ZoneFxManager.Entry> Z2Prefabs(Dictionary<string, Material> m)
        {
            Directory.CreateDirectory(Z2PrefabDir);
            var lib = new List<ZoneFxManager.Entry>();
            void Add(ZoneFxKind k, string mat, int pool, float range, float refR, bool dim, Action<ParticleSystem, ParticleSystemRenderer> cfg)
            {
                var go = new GameObject("PFX_Z2_" + k);
                try
                {
                    var ps = go.AddComponent<ParticleSystem>(); var r = go.GetComponent<ParticleSystemRenderer>();
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    var mn = ps.main; mn.loop = true; mn.duration = 10f; mn.playOnAwake = false; mn.simulationSpace = ParticleSystemSimulationSpace.World;
                    mn.scalingMode = ParticleSystemScalingMode.Hierarchy; mn.startSpeed = 0f; mn.gravityModifier = 0f;
                    var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(refR * 2f, 2f, refR * 2f);
                    var em = ps.emission; em.enabled = true;
                    r.sharedMaterial = m.TryGetValue(mat, out var mm) ? mm : null; r.renderMode = ParticleSystemRenderMode.Billboard;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                    r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                    r.maxParticleSize = 2f;
                    cfg(ps, r);
                    string path = $"{Z2PrefabDir}/PFX_Z2_{k}.prefab";
                    var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
                    lib.Add(new ZoneFxManager.Entry { kind = k, prefab = prefab, pool = pool, range = range, refRadius = refR, dimAtNight = dim });
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
            }
            Add(ZoneFxKind.GroundFog, "fog", 5, 110f, 12f, true, (ps, r) =>
            {
                PMain(ps, 16f, 24f, 7f, 12f, new Color(0.82f, 0.85f, 0.86f, 0.13f), 40); Rate(ps, 1.6f); Fade(ps, 0.25f, 0.7f); Spin(ps, 6f);
                Vel(ps, 0.25f, 0.02f, 0.1f); SizeOver(ps, 0.8f, 1.2f); r.sortingFudge = 20f; r.maxParticleSize = 3f;
            });
            Add(ZoneFxKind.WaterMist, "fog", 5, 90f, 8f, true, (ps, r) =>
            {
                PMain(ps, 10f, 16f, 3.5f, 6.5f, new Color(0.86f, 0.9f, 0.92f, 0.1f), 30); Rate(ps, 1.8f); Fade(ps, 0.3f, 0.65f); Spin(ps, 8f);
                Vel(ps, 0.12f, 0.08f, 0.05f); SizeOver(ps, 0.7f, 1.3f); r.sortingFudge = 18f; r.maxParticleSize = 3f;
            });
            Add(ZoneFxKind.Insects, "soft", 5, 45f, 2.5f, false, (ps, r) =>
            {
                PMain(ps, 3f, 6f, 0.03f, 0.055f, new Color(0.08f, 0.08f, 0.06f, 0.9f), 60); Rate(ps, 12f); Fade(ps, 0.1f, 0.85f);
                Noise(ps, 1.2f, 1.4f); r.minParticleSize = 0.0008f;
            });
            Add(ZoneFxKind.Dragonflies, "soft", 3, 45f, 8f, false, (ps, r) =>
            {
                PMain(ps, 4f, 8f, 0.07f, 0.11f, new Color(0.16f, 0.3f, 0.34f, 0.95f), 8); Rate(ps, 1.2f); Fade(ps, 0.1f, 0.85f);
                Noise(ps, 2.4f, 0.35f); r.renderMode = ParticleSystemRenderMode.Stretch; r.lengthScale = 1.6f; r.velocityScale = 0.02f;
            });
            Add(ZoneFxKind.Fireflies, "add", 3, 60f, 7f, false, (ps, r) =>
            {
                PMain(ps, 4f, 7f, 0.05f, 0.08f, new Color(0.72f, 0.88f, 0.42f, 0.55f), 16); Rate(ps, 1.6f); Pulse(ps);
                Noise(ps, 0.45f, 0.4f); r.minParticleSize = 0.001f;
            });
            Add(ZoneFxKind.DustMotes, "add", 6, 45f, 2.5f, false, (ps, r) =>
            {
                PMain(ps, 8f, 12f, 0.02f, 0.035f, new Color(1f, 0.93f, 0.76f, 0.32f), 70); Rate(ps, 6f); Fade(ps, 0.25f, 0.7f);
                Noise(ps, 0.12f, 0.25f); Vel(ps, 0f, 0.02f, 0f); r.minParticleSize = 0.0008f;
            });
            Add(ZoneFxKind.Haze, "fog", 4, 120f, 18f, true, (ps, r) =>
            {
                PMain(ps, 22f, 30f, 10f, 16f, new Color(0.5f, 0.58f, 0.46f, 0.07f), 22); Rate(ps, 0.8f); Fade(ps, 0.3f, 0.65f); Spin(ps, 3f);
                Vel(ps, 0.06f, 0.01f, 0.04f); r.sortingFudge = 24f; r.maxParticleSize = 3f;
            });
            Add(ZoneFxKind.Flies, "soft", 4, 35f, 1.2f, false, (ps, r) =>
            {
                PMain(ps, 2f, 4f, 0.02f, 0.032f, new Color(0.05f, 0.05f, 0.05f, 1f), 40); Rate(ps, 14f); Fade(ps, 0.05f, 0.9f);
                Noise(ps, 3f, 2.2f); r.minParticleSize = 0.0008f;
            });
            Add(ZoneFxKind.WindDust, "soft", 3, 80f, 14f, true, (ps, r) =>
            {
                PMain(ps, 4f, 7f, 1f, 2.6f, new Color(0.62f, 0.55f, 0.44f, 0.11f), 30); Rate(ps, 3f); Fade(ps, 0.2f, 0.7f); Spin(ps, 30f);
                Vel(ps, 2f, 0.15f, 1.5f); SizeOver(ps, 0.6f, 1.5f); mnGravity(ps, -0.01f); r.sortingFudge = 10f;
            });
            Add(ZoneFxKind.SmokeWisp, "soft", 6, 90f, 0.8f, true, (ps, r) =>
            {
                PMain(ps, 6f, 10f, 0.7f, 1.4f, new Color(0.52f, 0.49f, 0.46f, 0.2f), 30); Rate(ps, 2.2f); Fade(ps, 0.15f, 0.6f); Spin(ps, 20f);
                Vel(ps, 0.2f, 0.9f, 0.1f); SizeOver(ps, 1f, 3.2f); Noise(ps, 0.3f, 0.3f); r.sortingFudge = 8f;
            });
            Add(ZoneFxKind.AshFall, "soft", 3, 70f, 25f, true, (ps, r) =>
            {
                PMain(ps, 9f, 13f, 0.035f, 0.07f, new Color(0.22f, 0.21f, 0.2f, 0.85f), 160); Rate(ps, 12f); Fade(ps, 0.1f, 0.85f);
                mnGravity(ps, 0.018f); Noise(ps, 0.35f, 0.3f); r.minParticleSize = 0.0008f;
            });
            Add(ZoneFxKind.HeatShimmer, "heat", 3, 90f, 8f, false, (ps, r) =>
            {
                PMain(ps, 6f, 9f, 5f, 8f, new Color(1f, 1f, 1f, 0.55f), 6); Rate(ps, 0.6f); Fade(ps, 0.3f, 0.6f);
                var mn = ps.main; mn.startSize3D = true; mn.startSizeX = new ParticleSystem.MinMaxCurve(5f, 9f); mn.startSizeY = new ParticleSystem.MinMaxCurve(4f, 7f); mn.startSizeZ = 1f;
                r.renderMode = ParticleSystemRenderMode.VerticalBillboard; r.sortingFudge = 12f; r.maxParticleSize = 3f;
                r.SetActiveVertexStreams(new List<ParticleSystemVertexStream> { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV, ParticleSystemVertexStream.StableRandomX });
            });
            Add(ZoneFxKind.CaveDrips, "drop", 4, 30f, 3f, false, (ps, r) =>
            {
                PMain(ps, 0.9f, 1.2f, 0.025f, 0.04f, new Color(0.72f, 0.82f, 0.9f, 0.8f), 14); Rate(ps, 2.5f);
                mnGravity(ps, 1f); r.renderMode = ParticleSystemRenderMode.Stretch; r.lengthScale = 1f; r.velocityScale = 0.035f; r.minParticleSize = 0.0008f;
            });
            Add(ZoneFxKind.CaveMist, "fog", 3, 40f, 6f, true, (ps, r) =>
            {
                PMain(ps, 12f, 18f, 2.5f, 5f, new Color(0.55f, 0.6f, 0.66f, 0.08f), 16); Rate(ps, 0.9f); Fade(ps, 0.3f, 0.65f); Spin(ps, 4f);
                Vel(ps, 0.05f, 0.03f, 0.03f); r.sortingFudge = 16f; r.maxParticleSize = 2.5f;
            });
            AssetDatabase.SaveAssets();
            Z($"- prefabs: {lib.Count} in {Z2PrefabDir} (" + string.Join(", ", lib.Select(e => $"{e.kind} x{e.pool}")) + ")");
            return lib;
        }

        // particle helpers (editor only)
        static void PMain(ParticleSystem ps, float life0, float life1, float size0, float size1, Color c, int max)
        {
            var m = ps.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(life0, life1); m.startSize = new ParticleSystem.MinMaxCurve(size0, size1);
            m.startColor = c; m.maxParticles = max; m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        }
        static void mnGravity(ParticleSystem ps, float g) { var m = ps.main; m.gravityModifier = g; }
        static void Rate(ParticleSystem ps, float r) { var e = ps.emission; e.rateOverTime = r; }
        static void Fade(ParticleSystem ps, float inAt, float outAt)
        {
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, inAt), new GradientAlphaKey(1f, outAt), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }
        static void Pulse(ParticleSystem ps)
        {
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.1f, 0.35f), new GradientAlphaKey(1f, 0.55f), new GradientAlphaKey(0.05f, 0.75f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }
        static void Spin(ParticleSystem ps, float degPerSec)
        {
            var rot = ps.rotationOverLifetime; rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-degPerSec * Mathf.Deg2Rad, degPerSec * Mathf.Deg2Rad);
        }
        static void Vel(ParticleSystem ps, float x, float y, float z)
        {
            var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
            v.x = new ParticleSystem.MinMaxCurve(x); v.y = new ParticleSystem.MinMaxCurve(y); v.z = new ParticleSystem.MinMaxCurve(z);
        }
        static void SizeOver(ParticleSystem ps, float a, float b)
        {
            var s = ps.sizeOverLifetime; s.enabled = true;
            s.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, a, 1f, b));
        }
        static void Noise(ParticleSystem ps, float strength, float freq)
        {
            var n = ps.noise; n.enabled = true; n.strength = strength; n.frequency = freq; n.scrollSpeed = 0.35f; n.damping = true;
            n.quality = ParticleSystemNoiseQuality.Low; n.octaveCount = 1;
        }

        static List<ZoneFxManager.Haze> Z2Haze() => new List<ZoneFxManager.Haze>
        {
            new ZoneFxManager.Haze { zone = "prehistoric_wetland", color = new Color(0.64f, 0.68f, 0.7f), day = 0.06f, dawn = 0.34f, night = 0.22f },
            new ZoneFxManager.Haze { zone = "giant_fern_forest", color = new Color(0.26f, 0.31f, 0.24f), day = 0.16f, dawn = 0.26f, night = 0.14f },
            new ZoneFxManager.Haze { zone = "bone_valley", color = new Color(0.56f, 0.5f, 0.42f), day = 0.05f, dawn = 0.05f, night = 0.02f },
            new ZoneFxManager.Haze { zone = "volcanic_foothills", color = new Color(0.42f, 0.39f, 0.36f), day = 0.08f, dawn = 0.1f, night = 0.08f },
            new ZoneFxManager.Haze { zone = "deep_water_cave", color = new Color(0.03f, 0.035f, 0.045f), day = 0.22f, dawn = 0.22f, night = 0.22f, indoor = true },
        };

        // ------------------------------------------------------------------ anchors
        /// <summary>FX_ objects the zone agent placed under its zone group (only read), kind from the name</summary>
        static void AgentAnchors(Region r, List<(ZoneFxKind, Vector3, float, float, string)> list)
        {
            var g = SceneRoots.Find(r.group);
            if (!g) return;
            foreach (var t in g.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("FX_", StringComparison.OrdinalIgnoreCase)) continue;
                var k = KindFromName(t.name, r.id);
                if (k == null) continue;
                float s = Mathf.Max(t.lossyScale.x, t.lossyScale.z);
                float rad = s > 1.5f ? s : DefaultRadius(k.Value);
                float h = DefaultHeight(k.Value);
                var p = t.position + Vector3.up * AnchorLift(k.Value);
                list.Add((k.Value, p, rad, h, "agent"));
            }
        }

        static ZoneFxKind? KindFromName(string name, string zone)
        {
            var n = name.ToLowerInvariant();
            bool cave = zone == "deep_water_cave";
            if (n.Contains("drip")) return ZoneFxKind.CaveDrips;
            if (n.Contains("firefl")) return ZoneFxKind.Fireflies;
            if (n.Contains("dragon")) return ZoneFxKind.Dragonflies;
            if (n.Contains("fly") || n.Contains("flies") || n.Contains("carcass") || n.Contains("carrion")) return ZoneFxKind.Flies;
            if (n.Contains("insect") || n.Contains("swarm") || n.Contains("reed") || n.Contains("gnat")) return ZoneFxKind.Insects;
            if (n.Contains("mote") || n.Contains("shaft") || n.Contains("sunbeam") || n.Contains("lightray") || n.Contains("light_ray")) return ZoneFxKind.DustMotes;
            if (n.Contains("ash")) return ZoneFxKind.AshFall;
            if (n.Contains("heat") || n.Contains("shimmer")) return ZoneFxKind.HeatShimmer;
            if (n.Contains("smoke") || n.Contains("vent") || n.Contains("crack") || n.Contains("fumarole") || n.Contains("steam")) return ZoneFxKind.SmokeWisp;
            if (n.Contains("dust") || n.Contains("wind")) return ZoneFxKind.WindDust;
            if (n.Contains("haze") || n.Contains("humid")) return ZoneFxKind.Haze;
            if (n.Contains("mist")) return cave ? ZoneFxKind.CaveMist : ZoneFxKind.WaterMist;
            if (n.Contains("fog")) return cave ? ZoneFxKind.CaveMist : ZoneFxKind.GroundFog;
            if (cave && (n.Contains("pool") || n.Contains("water"))) return ZoneFxKind.CaveMist;
            return null;
        }
        static float DefaultRadius(ZoneFxKind k)
        {
            switch (k)
            {
                case ZoneFxKind.GroundFog: return 12f; case ZoneFxKind.WaterMist: return 7f; case ZoneFxKind.Insects: return 2.5f; case ZoneFxKind.Dragonflies: return 8f;
                case ZoneFxKind.Fireflies: return 7f; case ZoneFxKind.DustMotes: return 2.5f; case ZoneFxKind.Haze: return 18f; case ZoneFxKind.Flies: return 1.2f;
                case ZoneFxKind.WindDust: return 14f; case ZoneFxKind.SmokeWisp: return 0.8f; case ZoneFxKind.AshFall: return 25f; case ZoneFxKind.HeatShimmer: return 8f;
                case ZoneFxKind.CaveDrips: return 3f; default: return 6f;
            }
        }
        static float DefaultHeight(ZoneFxKind k)
        {
            switch (k)
            {
                case ZoneFxKind.GroundFog: return 2.5f; case ZoneFxKind.WaterMist: return 1f; case ZoneFxKind.Insects: return 2f; case ZoneFxKind.Dragonflies: return 1.5f;
                case ZoneFxKind.Fireflies: return 2f; case ZoneFxKind.DustMotes: return 8f; case ZoneFxKind.Haze: return 5f; case ZoneFxKind.Flies: return 1f;
                case ZoneFxKind.WindDust: return 1.5f; case ZoneFxKind.SmokeWisp: return 0.4f; case ZoneFxKind.AshFall: return 3f; case ZoneFxKind.HeatShimmer: return 1f;
                case ZoneFxKind.CaveDrips: return 0.2f; default: return 1f;
            }
        }
        /// <summary>metres above an anchor point (usually on the ground) where the volume is centred</summary>
        static float AnchorLift(ZoneFxKind k)
        {
            switch (k)
            {
                case ZoneFxKind.GroundFog: return 1.2f; case ZoneFxKind.WaterMist: return 0.5f; case ZoneFxKind.Insects: return 1.3f; case ZoneFxKind.Dragonflies: return 1f;
                case ZoneFxKind.Fireflies: return 1f; case ZoneFxKind.DustMotes: return 4f; case ZoneFxKind.Haze: return 3f; case ZoneFxKind.Flies: return 0.7f;
                case ZoneFxKind.WindDust: return 0.8f; case ZoneFxKind.SmokeWisp: return 0.3f; case ZoneFxKind.AshFall: return 16f; case ZoneFxKind.HeatShimmer: return 2.5f;
                case ZoneFxKind.CaveDrips: return 0f; default: return 0.4f;
            }
        }

        static Vector3 G(Terrain t, float x, float z, float lift) { var p = new Vector3(x, 0f, z); p.y = Ground(t, p) + lift; return p; }
        static float H01(int a, int b) { unchecked { uint h = (uint)a * 374761393u + (uint)b * 668265263u; h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16; return (h & 0xFFFFFF) / 16777216f; } }

        /// <summary>anchors for the kinds a zone needs that no agent anchor covers</summary>
        static void FallbackAnchors(Region r, ZoneManager.Zone zone, Terrain t, CaveInfo cave, List<(ZoneFxKind k, Vector3 p, float rad, float h, string src)> list)
        {
            bool Has(ZoneFxKind kind) => list.Any(q => q.k == kind);
            void Add(ZoneFxKind kind, Vector3 pos, float rad = -1f, float h = -1f) => list.Add((kind, pos, rad > 0f ? rad : DefaultRadius(kind), h > 0f ? h : DefaultHeight(kind), "builder"));
            int seed = r.id.GetHashCode() & 0xffff;
            var feat = PrimalZonesBuilder.Feat ?? PrimalZonesBuilder.LoadFeat();
            switch (r.id)
            {
                case "prehistoric_wetland":
                {
                    var wet = feat?.water?.wetland;
                    float lvl = wet != null ? wet.level : 0.85f;
                    var blobs = wet?.blobs != null && wet.blobs.Length > 0 ? wet.blobs : new[] { new PrimalZonesBuilder.FBlob { center = new[] { r.centre.x, lvl, r.centre.z }, radius = 30f } };
                    bool fog = Has(ZoneFxKind.GroundFog), mist = Has(ZoneFxKind.WaterMist), bugs = Has(ZoneFxKind.Insects), dragon = Has(ZoneFxKind.Dragonflies), glow = Has(ZoneFxKind.Fireflies);
                    for (int i = 0; i < blobs.Length; i++)
                    {
                        var b = blobs[i]; var c = new Vector3(b.center[0], lvl, b.center[2]);
                        if (!fog) Add(ZoneFxKind.GroundFog, c + Vector3.up * AnchorLift(ZoneFxKind.GroundFog), b.radius * 0.8f);
                        if (!mist) Add(ZoneFxKind.WaterMist, c + Vector3.up * AnchorLift(ZoneFxKind.WaterMist), b.radius * 0.5f);
                        if (!dragon && i < 3) Add(ZoneFxKind.Dragonflies, c + Vector3.up * AnchorLift(ZoneFxKind.Dragonflies), Mathf.Min(10f, b.radius * 0.45f));
                        // insects / fireflies over the reeds at the edge of each pool
                        for (int e = 0; e < 2; e++)
                        {
                            float ang = (H01(seed + i, e) + e * 0.5f) * Mathf.PI * 2f;
                            var edge = c + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * b.radius * 0.92f;
                            edge.y = Mathf.Max(lvl, Ground(t, edge));
                            if (!bugs) Add(ZoneFxKind.Insects, edge + Vector3.up * AnchorLift(ZoneFxKind.Insects));
                            if (!glow && e == 0 && i < 3) Add(ZoneFxKind.Fireflies, edge + Vector3.up * AnchorLift(ZoneFxKind.Fireflies));
                        }
                    }
                    break;
                }
                case "giant_fern_forest":
                    if (!Has(ZoneFxKind.DustMotes))
                        for (int i = 0; i < 8; i++)
                        {
                            float ang = H01(seed, i) * Mathf.PI * 2f, d = Mathf.Sqrt(H01(seed + 1, i)) * r.radius * 0.75f;
                            var pos = G(t, r.centre.x + Mathf.Cos(ang) * d, r.centre.z + Mathf.Sin(ang) * d, AnchorLift(ZoneFxKind.DustMotes));
                            if (feat != null && PrimalZonesBuilder.WaterDist(pos) < 4f) continue;
                            Add(ZoneFxKind.DustMotes, pos);
                        }
                    if (!Has(ZoneFxKind.Haze))
                        for (int i = 0; i < 4; i++)
                        {
                            float ang = (i + H01(seed + 2, i) * 0.6f) / 4f * Mathf.PI * 2f, d = r.radius * 0.45f;
                            Add(ZoneFxKind.Haze, G(t, r.centre.x + Mathf.Cos(ang) * d, r.centre.z + Mathf.Sin(ang) * d, AnchorLift(ZoneFxKind.Haze)));
                        }
                    break;
                case "bone_valley":
                    if (!Has(ZoneFxKind.Flies))
                    {
                        var pts = new List<Vector3>();
                        if (feat?.props?.skeleton != null && feat.props.skeleton.Length >= 3) pts.Add(new Vector3(feat.props.skeleton[0], feat.props.skeleton[1], feat.props.skeleton[2]));
                        foreach (var go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                        {
                            if (pts.Count >= 6) break;
                            foreach (var tr in go.GetComponentsInChildren<Transform>(false))
                            {
                                var n = tr.name.ToLowerInvariant();
                                if (!(n.Contains("carcass") || n.Contains("skeleton") || n.Contains("ribcage") || n.Contains("kill") || n.Contains("bones") || n.Contains("skull"))) continue;
                                if (n.StartsWith("fx_") || n.StartsWith("lm_")) continue;
                                if (zone == null || !zone.Contains(tr.position)) continue;
                                var at = tr.position;
                                if (pts.Any(q => (q - at).sqrMagnitude < 64f)) continue;
                                pts.Add(at);
                                if (pts.Count >= 6) break;
                            }
                        }
                        foreach (var pt in pts) { var q = pt; q.y = Mathf.Max(pt.y, Ground(t, pt)); Add(ZoneFxKind.Flies, q + Vector3.up * AnchorLift(ZoneFxKind.Flies)); }
                    }
                    if (!Has(ZoneFxKind.WindDust))
                        for (int i = 0; i < 3; i++)
                        {
                            float ang = (i / 3f + H01(seed, i) * 0.2f) * Mathf.PI * 2f, d = r.radius * 0.5f;
                            Add(ZoneFxKind.WindDust, G(t, r.centre.x + Mathf.Cos(ang) * d, r.centre.z + Mathf.Sin(ang) * d, AnchorLift(ZoneFxKind.WindDust)));
                        }
                    break;
                case "volcanic_foothills":
                {
                    Vector3 At(float u, float side) { var pos = Vector3.Lerp(r.centre, r.end, u); var dir = r.end - r.centre; dir.y = 0f; var nrm = new Vector3(-dir.z, 0f, dir.x).normalized; return pos + nrm * side; }
                    if (!Has(ZoneFxKind.SmokeWisp))
                        for (int i = 0; i < 6; i++)
                        {
                            var pos = At(0.4f + i * 0.1f, (H01(seed, i) - 0.5f) * 30f);
                            Add(ZoneFxKind.SmokeWisp, G(t, pos.x, pos.z, AnchorLift(ZoneFxKind.SmokeWisp)));
                        }
                    if (!Has(ZoneFxKind.AshFall))
                        foreach (var u in new[] { 0.55f, 0.75f, 0.95f }) { var pos = At(u, 0f); Add(ZoneFxKind.AshFall, G(t, pos.x, pos.z, AnchorLift(ZoneFxKind.AshFall))); }
                    if (!Has(ZoneFxKind.HeatShimmer))
                        foreach (var u in new[] { 0.72f, 0.86f, 1f }) { var pos = At(u, (H01(seed + 3, (int)(u * 10f)) - 0.5f) * 16f); Add(ZoneFxKind.HeatShimmer, G(t, pos.x, pos.z, AnchorLift(ZoneFxKind.HeatShimmer))); }
                    break;
                }
                case "deep_water_cave":
                {
                    if (!cave.found) break;                                       // underground: no guessing before CAVE's geometry exists
                    var c = cave.centre;
                    var pool = FindInScene("LM_DeepWaterCave");
                    if (!Has(ZoneFxKind.CaveDrips))
                        for (int i = 0; i < 4; i++)
                        {
                            float ang = (i / 4f + H01(seed, i) * 0.2f) * Mathf.PI * 2f, d = cave.radius * 0.4f;
                            Add(ZoneFxKind.CaveDrips, new Vector3(c.x + Mathf.Cos(ang) * d, Mathf.Min(cave.maxY - 1.5f, c.y + 4f), c.z + Mathf.Sin(ang) * d));
                        }
                    if (!Has(ZoneFxKind.CaveMist) && pool) Add(ZoneFxKind.CaveMist, pool.position + Vector3.up * 0.4f);
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ sound
        static void Z2Import()
        {
            int missing = 0;
            Import($"{AmbDir}/AMB_Flies_Loop.wav", true, AudioClipLoadType.CompressedInMemory, 0.55f, ref missing);
            if (missing > 0) ZW("AMB_Flies_Loop.wav missing (Tools/Audio/ambience_synth_p2.py): no fly sound at the carcasses");
        }

        static AudioClip Sfx(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>($"{SfxDir}/{name}.wav") ?? AssetDatabase.LoadAssetAtPath<AudioClip>($"{SfxDir}/Dinosaurs/{name}.wav");
        static AudioClip OneShotClip(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>($"{OneShotDir}/{name}.wav");

        static int Z2Emitters(Region r, Transform g, List<(ZoneFxKind k, Vector3 p, float rad, float h, string src)> list, CaveInfo cave)
        {
            int n = 0;
            if (r.id == "bone_valley")
            {
                var flies = Clip("Flies");
                foreach (var fx in list.Where(q => q.k == ZoneFxKind.Flies).Take(5))
                {
                    var e = Emitter(g, $"EMIT_Flies_{n}", fx.p, AmbienceEmitter.Kind.Flies);
                    e.clip = flies; e.volume = 0.32f; e.nightVolume = 0.32f; e.nightScale = 0.15f; e.minDistance = 1.5f; e.maxDistance = 16f; e.spread = 60f; e.rainMask = 0.6f; e.hushable = false;
                    n++;
                }
            }
            if (r.id == "deep_water_cave" && cave.found)
            {
                // the cave stream / pool: agent anchors named *stream* / *water* / *pool*, else the landmark pool
                var pts = new List<(Vector3 p, bool stream)>();
                if (cave.group)
                    foreach (var t in cave.group.GetComponentsInChildren<Transform>(true))
                    {
                        var nm = t.name.ToLowerInvariant();
                        if (!(nm.StartsWith("fx_") || nm.StartsWith("mk_") || nm.StartsWith("snd_") || nm.StartsWith("amb_"))) continue;
                        if (nm.Contains("stream") || nm.Contains("trickle") || nm.Contains("flow")) pts.Add((t.position, true));
                        else if (nm.Contains("pool")) pts.Add((t.position, false));
                    }
                var lm = FindInScene("LM_DeepWaterCave");
                if (!pts.Any(q => !q.stream) && lm) pts.Add((lm.position, false));
                foreach (var (p, stream) in pts.Take(4))
                {
                    var e = Emitter(g, stream ? $"EMIT_CaveStream_{n}" : $"EMIT_CavePool_{n}", p + Vector3.up * 0.3f, stream ? AmbienceEmitter.Kind.Stream : AmbienceEmitter.Kind.Custom);
                    if (stream) { e.volume = 0.4f; e.nightVolume = 0.4f; e.maxDistance = 26f; } else { e.volume = 0.28f; e.nightVolume = 0.28f; e.maxDistance = 22f; }
                    e.rainMask = 0f;
                    n++;
                }
            }
            if (n > 0) Z($"  - {ZoneGroupName(r)}: {n} emitters");
            return n;
        }

        static int Z2Reverb(Region r, Transform g, ZoneManager.Zone zone, CaveInfo cave)
        {
            if (r.id != "deep_water_cave" || zone == null) return 0;
            int n = 0;
            void Add(string name, Vector3 p, float min, float max)
            {
                var go = new GameObject(name); go.transform.SetParent(g, false); go.transform.position = p;
                var rz = go.AddComponent<AudioReverbZone>(); rz.reverbPreset = AudioReverbPreset.Cave; rz.minDistance = min; rz.maxDistance = Mathf.Max(min + 2f, max);
                Z($"  - {name} (Cave preset) at {V3(p)}, {min:0}..{rz.maxDistance:0} m");
                n++;
            }
            var c = zone.center + Vector3.up * 2f;
            Add("REV_DeepWaterCave", c, Mathf.Max(3f, zone.radius * 0.6f), zone.radius * 1.05f);
            var lm = FindInScene("LM_DeepWaterCave");
            if (lm && (lm.position - zone.center).magnitude > zone.radius * 0.5f) Add("REV_DeepWaterCave_Pool", lm.position + Vector3.up * 2f, 6f, 16f);
            var ch = FindHiddenChamber();
            if (ch) Add("REV_DeepWaterCave_Chamber", ch.position + Vector3.up * 1.5f, 2.5f, 7f);
            if (!cave.found) ZW("deep_water_cave: reverb at the default circle until CAVE's geometry exists");
            return n;
        }

        /// <summary>a mild heat band in the foothills: warm ring only (air +2.5..+4 C and the warm line), never hot or dangerous</summary>
        static int Z2Heat(Region r, Transform g, Terrain t)
        {
            if (r.id != "volcanic_foothills") return 0;
            int n = 0;
            foreach (var (u, heat) in new[] { (0.35f, 2.5f), (0.6f, 4f) })
            {
                var p = Vector3.Lerp(r.centre, r.end, u); p.y = Ground(t, p);
                var go = new GameObject($"HZ_foothills_band_{n}"); go.transform.SetParent(g, false); go.transform.position = p;
                var h = go.AddComponent<HazardZone>(); h.hazardId = "foothills";
                h.warmRadius = 34f; h.hotRadius = 0f; h.dangerRadius = 0f; h.minHeight = -20f; h.maxHeight = 45f;
                h.warmHeat = heat; h.hotHeat = heat; h.coreHeat = heat; h.smoke = 0.05f; h.smokeRadius = 30f; h.smokeDrift = 8f;
                Z($"  - HZ_foothills_band_{n} at {V3(p)}: warm ring 34 m, up to +{heat:0.#} C, no hot / dangerous ring");
                n++;
            }
            return n;
        }

        static void Z2Audio()
        {
            var a = UnityEngine.Object.FindFirstObjectByType<AmbienceManager>();
            if (!a) { ZW("no AmbienceManager in the scene: no zone beds / calls"); return; }
            AmbienceManager.ZoneBed B(string zone, string day, string night, float v, float nv, bool hush, float rain, float indoor = 0.3f)
                => new AmbienceManager.ZoneBed { zone = zone, day = Clip(day), night = night != null ? Clip(night) : null, volume = v, nightVolume = nv, hushable = hush, rainMask = rain, indoor = indoor };
            a.zoneBeds = new List<AmbienceManager.ZoneBed>
            {
                B("migration_valley", "WindStrong", null, 0.12f, 0.1f, false, 0.3f),
                B("prehistoric_wetland", "WetlandDay", "WetlandNight", 0.2f, 0.26f, true, 0.5f),
                B("bone_valley", "WindStrong", null, 0.17f, 0.15f, false, 0.3f),
                B("giant_fern_forest", "InsectsDay", "NightInsects", 0.22f, 0.24f, true, 0.6f),
                B("volcanic_foothills", "WindStrong", null, 0.2f, 0.2f, false, 0.3f),
                B("deep_water_cave", "CaveRoom", null, 0.16f, 0.16f, false, 0f, 1f),
            };
            a.zoneCalls = new List<AmbienceManager.ZoneCall>
            {
                new AmbienceManager.ZoneCall { zone = "migration_valley", clips = new[] { Sfx("SFX_Parasaurolophus_Call_01"), Sfx("SFX_Parasaurolophus_Call_02"), Sfx("SFX_Triceratops_Call_01"), Sfx("SFX_Triceratops_Call_02") },
                    every = new Vector2(25f, 60f), volume = 0.45f, pitch = new Vector2(0.85f, 1f), distance = new Vector2(70f, 110f), height = new Vector2(0f, 6f), cutoff = 1400f, dayChance = 1f, nightChance = 0.35f },
                new AmbienceManager.ZoneCall { zone = "bone_valley", clips = new[] { Sfx("SFX_Pteranodon_Call_01"), Sfx("SFX_Pteranodon_Call_02") },
                    every = new Vector2(16f, 40f), volume = 0.3f, pitch = new Vector2(1.1f, 1.3f), distance = new Vector2(35f, 70f), height = new Vector2(15f, 35f), cutoff = 4500f, dayChance = 1f, nightChance = 0.1f },
                new AmbienceManager.ZoneCall { zone = "giant_fern_forest", clips = new[] { Sfx("SFX_ShipCreak_1"), Sfx("SFX_ShipCreak_2") },
                    every = new Vector2(12f, 35f), volume = 0.28f, pitch = new Vector2(0.5f, 0.7f), distance = new Vector2(10f, 30f), height = new Vector2(4f, 14f), cutoff = 2600f, dayChance = 1f, nightChance = 1f },
                new AmbienceManager.ZoneCall { zone = "volcanic_foothills", clips = new[] { OneShotClip("AMB_ThunderFar_1"), OneShotClip("AMB_ThunderFar_2"), OneShotClip("AMB_ThunderFar_3") },
                    every = new Vector2(30f, 80f), volume = 0.5f, pitch = new Vector2(0.4f, 0.55f), distance = new Vector2(90f, 140f), height = new Vector2(-10f, 0f), cutoff = 380f, dayChance = 1f, nightChance = 1f },
            };
            EditorUtility.SetDirty(a);
            int beds = a.zoneBeds.Count(b => b.day), clips = a.zoneCalls.Sum(c => c.clips.Count(x => x)), want = a.zoneCalls.Sum(c => c.clips.Length);
            Z($"- AmbienceManager: {beds}/{a.zoneBeds.Count} zone beds with a clip, zone calls {a.zoneCalls.Count} with {clips}/{want} clips");
        }

        // ------------------------------------------------------------------ check
        static void Z2CheckInto()
        {
            var zm = UnityEngine.Object.FindFirstObjectByType<ZoneManager>();
            if (!zm) { ZW("check: no ZoneManager"); return; }
            int regions = 0;
            foreach (var id in StoryIds.Regions)
            {
                var z = zm.Find(id);
                if (z == null) { ZW($"check: zone {id} missing"); continue; }
                regions++;
                if (!z.announce || string.IsNullOrEmpty(z.displayName)) ZW($"check: zone {id} has no toast name");
                if (StoryTexts.Location(id) == null) ZW($"check: no location text for {id}");
            }
            int lms = StoryIds.Landmarks.Count(id => zm.Find(id) != null);
            Z($"- zones: {regions}/6 regions, {lms}/{StoryIds.Landmarks.Length} landmarks registered" + (lms < StoryIds.Landmarks.Length ? " (missing: " + string.Join(", ", StoryIds.Landmarks.Where(id => zm.Find(id) == null)) + ")" : ""));
            var j = UnityEngine.Object.FindFirstObjectByType<JournalSystem>(FindObjectsInactive.Include);
            if (j)
            {
                var pages = StoryIds.Regions.Select(id => StoryTexts.Location(id)?.page).Concat(StoryTexts.Landmarks.Select(m => m.page)).Append(StoryTexts.BoneValleyLesson.page).Where(p => p != null).ToList();
                var miss = pages.Where(p => !j.entries.Exists(e => e != null && e.id == p)).ToList();
                Z($"- journal: {pages.Count - miss.Count}/{pages.Count} Phase 2 pages in the scene list" + (miss.Count > 0 ? " (missing: " + string.Join(", ", miss) + ")" : ""));
                if (miss.Count > 0) _z2Warn++;
            }
            var v = UnityEngine.Object.FindFirstObjectByType<ProtagonistVoice>(FindObjectsInactive.Include);
            if (v) { bool bv = v.lines.Exists(l => l != null && l.id == "bone_valley"); Z($"- survivor line \"Something hunts here.\": {(bv ? "yes" : "MISSING")}"); if (!bv) _z2Warn++; }
            var root = SceneRoots.Find(Z2Root);
            var mgr = root ? root.GetComponent<ZoneFxManager>() : null;
            if (!mgr) { ZW("check: no ZoneFxManager at " + Z2Root); return; }
            var kinds = new HashSet<ZoneFxKind>();
            foreach (var e in mgr.library)
            {
                if (e == null || !e.prefab) { ZW("check: library entry without a prefab"); continue; }
                if (e.pool < 1) ZW($"check: {e.kind} pool {e.pool}");
                var ps = e.prefab.GetComponent<ParticleSystem>(); var r = e.prefab.GetComponent<ParticleSystemRenderer>();
                if (!ps) ZW($"check: {e.prefab.name} has no ParticleSystem");
                if (!r || !r.sharedMaterial) ZW($"check: {e.prefab.name} has no material");
                kinds.Add(e.kind);
            }
            var anchors = root.GetComponentsInChildren<ZoneFxAnchor>(true);
            var unreg = anchors.Where(q => !kinds.Contains(q.kind)).Select(q => q.kind).Distinct().ToList();
            if (unreg.Count > 0) ZW("check: anchors of kinds without a pooled prefab: " + string.Join(", ", unreg));
            Z($"- pooled effects: {mgr.library.Count} kinds registered ({mgr.library.Sum(e => e.pool)} systems), {anchors.Length} anchors: " + string.Join(", ", anchors.GroupBy(q => q.kind).OrderBy(q => q.Key).Select(q => $"{q.Key} {q.Count()}")));
            var emit = root.GetComponentsInChildren<AmbienceEmitter>(true);
            int noClip = emit.Count(e => !e.clip);
            Z($"- emitters: {emit.Length} ({noClip} without a clip), reverb zones {root.GetComponentsInChildren<AudioReverbZone>(true).Length}, heat band zones {root.GetComponentsInChildren<HazardZone>(true).Length}");
            if (noClip > 0) _z2Warn++;
            var a2 = UnityEngine.Object.FindFirstObjectByType<AmbienceManager>();
            if (a2)
            {
                int badBeds = a2.zoneBeds.Count(b => b == null || !b.day || zm.Find(b.zone) == null);
                int badCalls = a2.zoneCalls.Count(c => c == null || c.clips == null || c.clips.Any(x => !x) || zm.Find(c.zone) == null);
                Z($"- ambience: {a2.zoneBeds.Count} zone beds ({badBeds} broken), {a2.zoneCalls.Count} zone calls ({badCalls} broken)");
                _z2Warn += badBeds + badCalls;
            }
            // missing references anywhere under the Zones2 root
            int missingRefs = 0;
            foreach (var c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null) { missingRefs++; continue; }
                var so = new SerializedObject(c); var p = so.GetIterator();
                while (p.NextVisible(true)) if (p.propertyType == SerializedPropertyType.ObjectReference && p.objectReferenceValue == null && p.objectReferenceInstanceIDValue != 0) missingRefs++;
            }
            Z($"- missing references under {Z2Root}: {missingRefs}");
            _z2Warn += missingRefs;
        }
    }
}
