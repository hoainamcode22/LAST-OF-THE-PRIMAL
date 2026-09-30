using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PrimalFrontier.Core
{
    /// <summary>
    /// The owner's scene layout (agent HIER, Phase 1 wave 2b, 2026-09-30) and the map from the old container paths to it:
    /// World / {Environment / {Terrain, Ocean, Rivers, Waterfalls, Forest, Caves, Wetlands, Volcano, Weather},
    /// Gameplay / {Player, Wildlife, Resources, Structures, WaterSources, Traps, Interactables},
    /// WorldSystems / {Time, Weather, Wildlife, Perception, Save}}, Lighting, VFX, Audio, Managers.
    /// Moved objects kept their own names ([Dinosaurs], [Resources], Markers, Rocks, Vegetation, ...), so code written for
    /// the old roots ([Systems], [Gameplay], [Resources], [Atmosphere], [UI], Markers, Water, World/Rocks, ...) resolves them
    /// with <see cref="Legacy"/>: the old path is mapped to the new one (longest old prefix wins, the rest of the path is kept),
    /// and a scene still in the old layout is found by the old path. Lookups include inactive objects.
    /// </summary>
    public static class SceneRoots
    {
        public const string World = "World", Environment = "World/Environment", Gameplay = "World/Gameplay", WorldSystems = "World/WorldSystems";
        public const string Terrain = Environment + "/Terrain", Ocean = Environment + "/Ocean", Rivers = Environment + "/Rivers",
            Waterfalls = Environment + "/Waterfalls", Forest = Environment + "/Forest", Caves = Environment + "/Caves",
            Wetlands = Environment + "/Wetlands", Volcano = Environment + "/Volcano", Weather = Environment + "/Weather";
        public const string Player = Gameplay + "/Player", Wildlife = Gameplay + "/Wildlife", Resources = Gameplay + "/Resources",
            Structures = Gameplay + "/Structures", WaterSources = Gameplay + "/WaterSources", Traps = Gameplay + "/Traps",
            Interactables = Gameplay + "/Interactables";
        public const string TimeSystem = WorldSystems + "/Time", WeatherSystem = WorldSystems + "/Weather", WildlifeSystem = WorldSystems + "/Wildlife",
            PerceptionSystem = WorldSystems + "/Perception", SaveSystemSlot = WorldSystems + "/Save";
        public const string Lighting = "Lighting", Vfx = "VFX", Audio = "Audio", Managers = "Managers";
        /// <summary>moved roots that code looks up often</summary>
        public const string Markers = Managers + "/Markers", Water = Ocean + "/Water", UiRoot = Managers + "/[UI]";

        /// <summary>the scene roots, in hierarchy order</summary>
        public static readonly string[] Roots = { World, Lighting, Vfx, Audio, Managers };
        /// <summary>every container of the layout (parents before children)</summary>
        public static readonly string[] Slots =
        {
            World, Environment, Terrain, Ocean, Rivers, Waterfalls, Forest, Caves, Wetlands, Volcano, Weather,
            Gameplay, Player, Wildlife, Resources, Structures, WaterSources, Traps, Interactables,
            WorldSystems, TimeSystem, WeatherSystem, WildlifeSystem, PerceptionSystem, SaveSystemSlot,
            Lighting, Vfx, Audio, Managers,
        };

        /// <summary>old path (before 2026-09-30) -> new path</summary>
        public static readonly (string from, string to)[] Moves =
        {
            ("[Systems]", Managers),
            ("[Systems]/Time", TimeSystem), ("[Systems]/Weather", WeatherSystem),
            ("[Systems]/Ambience", Audio + "/Ambience"), ("[Systems]/SfxPlayer", Audio + "/SfxPlayer"),
            ("[Systems]/VfxPool", Vfx + "/VfxPool"), ("[Systems]/BloodDecals", Vfx + "/BloodDecals"), ("[Systems]/NightSky", Vfx + "/NightSky"),
            ("[Gameplay]", Gameplay),
            ("[Gameplay]/[Dinosaurs]", Wildlife + "/[Dinosaurs]"),
            ("[Gameplay]/FruitTrees", Interactables + "/FruitTrees"), ("[Gameplay]/Climbables", Interactables + "/Climbables"),
            ("[Gameplay]/CaptainsLog", Interactables + "/CaptainsLog"), ("[Gameplay]/GiantFootprints", Interactables + "/GiantFootprints"),
            ("[Gameplay]/Pickup_wood", Interactables + "/Pickup_wood"), ("[Gameplay]/Pickup_stone", Interactables + "/Pickup_stone"),
            ("[Gameplay]/Cave", Caves + "/Cave"),
            ("[Gameplay]/[Game]", Managers + "/[Game]"), ("[Gameplay]/[Zones]", Managers + "/[Zones]"),
            ("[Resources]", Resources + "/[Resources]"),
            ("[Atmosphere]", Weather),
            ("[Atmosphere]/Emitters", Audio + "/Emitters"), ("[Atmosphere]/Reverb", Audio + "/Reverb"),
            ("[Atmosphere]/Hazards", Volcano + "/Hazards"), ("[Atmosphere]/Sky", Weather + "/Sky"),
            ("[UI]", UiRoot),
            ("Player", Player),
            ("Main Camera", Managers + "/Main Camera"),
            ("Markers", Markers),
            ("ENV_Island_Terrain", Terrain + "/ENV_Island_Terrain"),
            ("Sun", Lighting + "/Sun"), ("Global Volume", Lighting + "/Global Volume"),
            ("Water", Water),
            ("Water/ENV_River_Water", Rivers + "/ENV_River_Water"), ("Water/ENV_LowerRiver_Water", Rivers + "/ENV_LowerRiver_Water"),
            ("Water/ENV_RiverMouth_Water", Rivers + "/ENV_RiverMouth_Water"), ("Water/ENV_SpringStream_Water", Rivers + "/ENV_SpringStream_Water"),
            ("Water/ENV_Stream_Water", Rivers + "/ENV_Stream_Water"),
            ("Water/ENV_Wetland_Water", Wetlands + "/ENV_Wetland_Water"),
            ("Water/ENV_Pond_Water", WaterSources + "/ENV_Pond_Water"), ("Water/ENV_Spring_Water", WaterSources + "/ENV_Spring_Water"),
            ("Water/ENV_WaterfallPool_Water", WaterSources + "/ENV_WaterfallPool_Water"),
            ("World/Rocks", Terrain + "/Rocks"), ("World/Cliffs", Terrain + "/Cliffs"),
            ("World/Shipwreck", Structures + "/Shipwreck"), ("World/Props", Interactables + "/Props"),
            ("World/Resources", Resources + "/Resources"),
            ("World/Vegetation", Forest + "/Vegetation"),
            ("World/Landmarks", Volcano + "/Landmarks"),
            ("World/Environment/Water", Waterfalls + "/Water"),
            ("World/Environment/WaterfallDressing", Waterfalls + "/WaterfallDressing"),
            ("World/Environment/Forest", Forest + "/ForestDressing"),
            ("World/Environment/Rocks", Forest + "/Rocks"),
            ("World/Environment/WaterEdge", Wetlands + "/WaterEdge"),
            ("World/Environment/Storytelling", Interactables + "/Storytelling"),
        };

        /// <summary>the new path of an old one (unchanged when nothing moved)</summary>
        public static string Map(string legacyPath)
        {
            if (string.IsNullOrEmpty(legacyPath)) return legacyPath;
            string best = null, to = null;
            foreach (var (f, t) in Moves)
                if ((legacyPath == f || legacyPath.StartsWith(f + "/", StringComparison.Ordinal)) && (best == null || f.Length > best.Length)) { best = f; to = t; }
            return best == null ? legacyPath : to + legacyPath.Substring(best.Length);
        }

        /// <summary>the object at this exact path from a scene root (inactive included); create = make the missing containers</summary>
        public static Transform Find(string path, bool create = false)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var scene = SceneManager.GetActiveScene();
            var parts = path.Split('/');
            Transform t = null;
            if (scene.IsValid()) foreach (var r in scene.GetRootGameObjects()) if (r.name == parts[0]) { t = r.transform; break; }
            if (!t)
            {
                if (!create) return null;
                t = new GameObject(parts[0]).transform;
            }
            for (int i = 1; i < parts.Length; i++)
            {
                var c = t.Find(parts[i]);
                if (!c)
                {
                    if (!create) return null;
                    c = new GameObject(parts[i]).transform; c.SetParent(t, false);
                }
                t = c;
            }
            return t;
        }

        /// <summary>an old path: its new place, else the old place (a scene not yet reorganised), else (create) the new place made</summary>
        public static Transform Legacy(string legacyPath, bool create = false)
        {
            var t = Find(Map(legacyPath));
            if (t) return t;
            // old layout: the old path itself (never a container of the new layout, e.g. World/Environment/Forest now holds more)
            if (Map(legacyPath) != legacyPath && (Array.IndexOf(Slots, legacyPath) < 0 || !IsNewLayout)) { t = Find(legacyPath); if (t) return t; }
            return create ? Find(Map(legacyPath), true) : null;
        }

        /// <summary>the scene already has the owner's layout (World/Gameplay or Managers exists)</summary>
        public static bool IsNewLayout => Find(Gameplay) || Find(Managers);

        /// <summary>the GameObject of <see cref="Legacy"/></summary>
        public static GameObject LegacyObject(string legacyPath, bool create = false) { var t = Legacy(legacyPath, create); return t ? t.gameObject : null; }

        /// <summary>the parent an object at this old path belongs under now (made when create)</summary>
        public static Transform LegacyParent(string legacyPath, bool create = false)
        {
            string p = Map(legacyPath); int s = p.LastIndexOf('/');
            if (s < 0) return null;                                              // a root
            var parent = Find(p.Substring(0, s), create);
            if (parent) return parent;
            int s0 = legacyPath.LastIndexOf('/');
            return s0 < 0 ? null : Find(legacyPath.Substring(0, s0));
        }

        /// <summary>every object that was a direct child of this old container, wherever it is now (old layout: its children)</summary>
        public static List<Transform> LegacyChildren(string legacyParent)
        {
            var list = new List<Transform>();
            var old = Find(legacyParent);
            if (old && Map(legacyParent) != legacyParent) foreach (Transform c in old) list.Add(c);
            foreach (var (f, t) in Moves)
            {
                if (!f.StartsWith(legacyParent + "/", StringComparison.Ordinal) || f.IndexOf('/', legacyParent.Length + 1) >= 0) continue;
                int s = t.LastIndexOf('/'); var parent = s > 0 ? Find(t.Substring(0, s)) : null; if (!parent) continue;
                string leaf = t.Substring(s + 1);
                foreach (Transform c in parent) if (c.name == leaf && !list.Contains(c)) list.Add(c);
            }
            var now = Find(Map(legacyParent));
            if (now && now != old) foreach (Transform c in now) if (Array.IndexOf(Slots, PathOf(c)) < 0 && !list.Contains(c)) list.Add(c);
            return list;
        }

        /// <summary>"World/Gameplay/Player" style path of a transform</summary>
        public static string PathOf(Transform t)
        {
            if (!t) return "";
            var s = t.name; while (t.parent) { t = t.parent; s = t.name + "/" + s; }
            return s;
        }
    }
}
