using System;
using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.World;

namespace PrimalFrontier.AI
{
    /// <summary>
    /// The island's wildlife layout for the PC phase, in one table: per species behaviour / weight / track values, the herds
    /// (3-12, one of them the migration herd on the valley route) and the predator territories tied to their prey (small
    /// hunters at a forest edge, a medium one on the river / meadow edge, a large one on open ground, the apex in its rare
    /// territory), all placed by ENV location ids (Markers/Zones) and the route (Markers/Migration). PrimalWildlifeBuilder
    /// applies it to [Gameplay]/[Dinosaurs] in the editor and saves the scene; when a scene was never rebuilt the
    /// DinosaurSpawner applies the same plan at runtime (clones instead of prefab instances), so the game plays the same.
    /// Placement snaps to the terrain and keeps out of water, off steep ground and out of rocks. Deterministic (fixed seeds).
    /// </summary>
    public static partial class WildlifePlan
    {
        // ------------------------------------------------------------------ species values
        public struct SpeciesRow
        {
            public string id; public float night, aggression, investigation;
            public float weight, sway, lean, tail, pitch, shake, pivot;
            public FootShape shape; public float print, stride, gauge, dung, scratch;
        }

        public static readonly SpeciesRow[] Species =
        {
            new SpeciesRow { id = "triceratops", night = 0.4f, aggression = 0.6f, investigation = 0.3f, weight = 0.85f, sway = 2.2f, lean = 1.5f, tail = 9f, pitch = 1.2f, shake = 0.022f, pivot = 0.6f, shape = FootShape.Ceratopsian, print = 0.6f, stride = 2.2f, gauge = 1.3f, dung = 0.5f },
            new SpeciesRow { id = "parasaurolophus", night = 0.7f, aggression = 0.1f, investigation = 0.45f, weight = 0.62f, sway = 1.6f, lean = 2.6f, tail = 14f, pitch = 1f, shake = 0.012f, pivot = 0.7f, shape = FootShape.Hadrosaur, print = 0.5f, stride = 2.3f, gauge = 0.9f, dung = 0.38f },
            new SpeciesRow { id = "ankylosaurus", night = 0.2f, aggression = 0.7f, investigation = 0.2f, weight = 0.9f, sway = 2.6f, lean = 1f, tail = 8f, pitch = 1.4f, shake = 0.02f, pivot = 0.55f, shape = FootShape.Ankylosaur, print = 0.48f, stride = 1.6f, gauge = 1.4f, dung = 0.42f },
            new SpeciesRow { id = "velociraptor", night = 0f, aggression = 0.75f, investigation = 0.9f, weight = 0.08f, sway = 0.6f, lean = 7f, tail = 24f, pitch = 0.3f, shake = 0f, pivot = 1f, shape = FootShape.Dromaeosaur, print = 0.17f, stride = 1.2f, gauge = 0.22f, dung = 0.1f, scratch = 0.9f },
            new SpeciesRow { id = "carnotaurus", night = 0.2f, aggression = 0.8f, investigation = 0.6f, weight = 0.55f, sway = 1.2f, lean = 4.5f, tail = 18f, pitch = 0.8f, shake = 0.012f, pivot = 0.75f, shape = FootShape.Theropod, print = 0.52f, stride = 2.6f, gauge = 0.7f, dung = 0.25f, scratch = 2.2f },
            new SpeciesRow { id = "spinosaurus", night = 0.3f, aggression = 0.6f, investigation = 0.5f, weight = 0.82f, sway = 1.9f, lean = 2.5f, tail = 12f, pitch = 1.2f, shake = 0.02f, pivot = 0.6f, shape = FootShape.Theropod, print = 0.72f, stride = 3f, gauge = 1f, dung = 0.35f, scratch = 2.6f },
            new SpeciesRow { id = "apex", night = 0.1f, aggression = 0.9f, investigation = 0.7f, weight = 1f, sway = 2.4f, lean = 2f, tail = 12f, pitch = 1.5f, shake = 0.032f, pivot = 0.55f, shape = FootShape.Theropod, print = 0.95f, stride = 3.6f, gauge = 1.1f, dung = 0.45f, scratch = 3.2f },
        };

        public static bool TryRow(string id, out SpeciesRow row)
        {
            foreach (var r in Species) if (r.id == id) { row = r; return true; }
            row = default; return false;
        }

        /// <summary>write the wildlife values into a definition (force = the builder; the runtime only fills untuned ones)</summary>
        public static bool ApplySpecies(DinosaurDefinition d, bool force)
        {
            if (!d || (!force && d.wildlifeTuned) || !TryRow(d.id, out var r)) return false;
            d.behaviour = new BehaviourProfile { nightFear = r.night, aggression = r.aggression, investigation = r.investigation };
            d.locomotion = new LocomotionProfile { weight = r.weight, swayDegrees = r.sway, leanDegrees = r.lean, tailBalance = r.tail, accelPitch = r.pitch, stepShake = r.shake, pivotRate = r.pivot };
            d.tracks = new TrackProfile { shape = r.shape, printSize = r.print, stride = r.stride, gauge = r.gauge, droppingsSize = r.dung, scratchHeight = r.scratch };
            d.wildlifeTuned = true;
            return true;
        }

        // ------------------------------------------------------------------ herds and territories
        public struct HerdRow { public string species, group, label; public int count; public string at, towards; public Vector3 offset; public float edge; public bool migrates; }
        public struct TerritoryRow { public string species, group, habitat, role; public int count; public string at, towards; public Vector3 offset; public float edge, homeRadius; }

        public static readonly HerdRow[] Herds =
        {
            // the migration herd: home side = the meadow gathering (Route_00), far side = the resting hollow (Route_13)
            new HerdRow { species = "parasaurolophus", group = "Parasaurolophus", label = "crested herd", count = 9, migrates = true },
            new HerdRow { species = "triceratops", group = "Triceratops", label = "horned herd", count = 5, at = "herbivore_valley", offset = new Vector3(12f, 0f, -22f) },
            new HerdRow { species = "ankylosaurus", group = "Ankylosaurus", label = "armoured group", count = 3, at = "deep_forest", towards = "old_camp", edge = 0.95f },
        };

        public static readonly TerritoryRow[] Territories =
        {
            new TerritoryRow { species = "velociraptor", group = "Velociraptor", habitat = "HAB_Velociraptor", role = "small: forest edge by the valley", count = 3, at = "deep_forest", towards = "herbivore_valley", edge = 0.92f, homeRadius = 38f },
            new TerritoryRow { species = "carnotaurus", group = "Carnotaurus", habitat = "HAB_Carnotaurus", role = "medium: river / meadow edge", count = 1, at = "river", offset = new Vector3(-26f, 0f, 35f), homeRadius = 40f },
            new TerritoryRow { species = "spinosaurus", group = "Spinosaurus", habitat = null, role = "large: open grassland terrace", count = 1, at = "route:11", offset = new Vector3(-6f, 0f, 32f), homeRadius = 32f },
            // Phase 2: home moved from the kill site (-113, -64) to the canyon mouth side of Bone Valley, ~45 m from the fresh carcass
            // (the player's first visit reads the evidence first; the tyrant can still wander in)
            new TerritoryRow { species = "apex", group = "Rift Tyrant", habitat = "HAB_ApexPredator", role = "apex: the hunting ground", count = 1, at = "predator_territory", offset = new Vector3(-23f, 0f, -22f), homeRadius = 30f },
        };
        static readonly Dictionary<string, string> HerdHabitat = new Dictionary<string, string> { { "parasaurolophus", "HAB_Parasaurolophus" }, { "triceratops", "HAB_Triceratops" }, { "ankylosaurus", "HAB_Ankylosaurus" } };

        // LOCATIONS.md values, used when a marker is missing
        static readonly Dictionary<string, Vector4> KnownLocations = new Dictionary<string, Vector4>
        {
            { "beach", new Vector4(0f, 1.8f, 205f, 70f) }, { "forest", new Vector4(-40f, 10.7f, 125f, 60f) }, { "deep_forest", new Vector4(168f, 8.2f, 20f, 60f) },
            { "river", new Vector4(66f, 10.5f, -45f, 55f) }, { "waterfall", new Vector4(93.5f, 19.4f, -160f, 24f) }, { "meadow", new Vector4(-2f, 9.2f, -28f, 70f) },
            { "canyon", new Vector4(-136f, 23.4f, -125f, 36f) }, { "wetland", new Vector4(102f, 0.4f, 172f, 38f) }, { "ridge", new Vector4(108f, 47.8f, -208f, 45f) },
            { "predator_territory", new Vector4(-105f, 21.6f, -76f, 45f) }, { "herbivore_valley", new Vector4(6f, 9.2f, -40f, 85f) }, { "old_camp", new Vector4(122f, 11.4f, -66f, 12f) },
            { "nest", new Vector4(-152f, 13.2f, -28f, 14f) }, { "migration_view", new Vector4(-40f, 25.4f, -92f, 10f) },
        };
        static readonly Vector3[] KnownRoute =
        {
            new Vector3(-14f, 8.9f, 12f), new Vector3(-8f, 9.1f, -18f), new Vector3(2f, 9.3f, -48f), new Vector3(18f, 9.9f, -72f), new Vector3(46f, 11.1f, -88f),
            new Vector3(70f, 12.2f, -95f), new Vector3(84.6f, 10.6f, -97.5f), new Vector3(100f, 13.7f, -110f), new Vector3(112f, 14.7f, -122f), new Vector3(124f, 16.9f, -132f),
            new Vector3(137f, 21.1f, -142f), new Vector3(156f, 23f, -150f), new Vector3(172f, 23.3f, -154f), new Vector3(184f, 24.7f, -160f),
        };

        /// <summary>how the plan changes the scene: clone = duplicate a placed creature under a parent (prefab instance in the editor)</summary>
        public class Context
        {
            public Func<GameObject, Transform, GameObject> clone = (src, parent) => UnityEngine.Object.Instantiate(src, parent);
            public Action<string> log = s => Debug.Log("[Wildlife] " + s);
            public Action<UnityEngine.Object> dirty = o => { };
            /// <summary>Phase 2: a new group object was made (editor: undo) / a copied component must go</summary>
            public Action<GameObject> created = go => { };
            public Action<Component> remove = c => { if (c) UnityEngine.Object.Destroy(c); };
            public bool editor;
        }

        static Transform MarkersRoot()
        {
            return PrimalFrontier.Core.SceneRoots.Legacy("Markers");                     // Managers/Markers since HIER 2026-09-30
        }

        /// <summary>a location id (Markers/Zones/&lt;id&gt;), or "route:NN" for a migration route point</summary>
        public static bool Resolve(string id, out Vector3 pos, out float radius)
        {
            pos = Vector3.zero; radius = 20f;
            if (string.IsNullOrEmpty(id)) return false;
            if (id.StartsWith("route:"))
            {
                var r = Route(); int i = int.Parse(id.Substring(6));
                if (i < 0 || i >= r.Length) return false;
                pos = r[i]; radius = 12f; return true;
            }
            var m = MarkersRoot();
            var zones = m ? m.Find("Zones") : null;
            var z = zones ? zones.Find(id) : null;
            if (z) { pos = z.position; var el = z.GetComponent<EnvLocation>(); if (el) radius = el.radius; return true; }
            if (KnownLocations.TryGetValue(id, out var k)) { pos = new Vector3(k.x, k.y, k.z); radius = k.w; return true; }
            return false;
        }

        public static Vector3[] Route()
        {
            var m = MarkersRoot(); var mig = m ? m.Find("Migration") : null;
            if (mig)
            {
                var list = new List<Transform>();
                foreach (Transform c in mig) if (c.name.StartsWith("Route_")) list.Add(c);
                if (list.Count >= 2) { list.Sort((a, b) => string.CompareOrdinal(a.name, b.name)); var p = new Vector3[list.Count]; for (int i = 0; i < p.Length; i++) p[i] = list[i].position; return p; }
            }
            return (Vector3[])KnownRoute.Clone();
        }

        static Vector3 Place(string at, string towards, float edge, Vector3 offset)
        {
            Resolve(at, out var p, out float r);
            if (!string.IsNullOrEmpty(towards) && Resolve(towards, out var q, out _))
            {
                Vector3 d = q - p; d.y = 0f;
                if (d.sqrMagnitude > 1f) p += d.normalized * r * (edge > 0f ? edge : 0.9f);
            }
            return p + offset;
        }

        // ------------------------------------------------------------------ ground
        static Dictionary<Vector2Int, float> _water;

        static void BuildWater()
        {
            _water = new Dictionary<Vector2Int, float>();
            foreach (var w in UnityEngine.Object.FindObjectsByType<WaterSource>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var mf = w.surface ? w.surface : w.GetComponentInChildren<MeshFilter>();
                if (!mf || !mf.sharedMesh) continue;
                var t = mf.transform; var v = mf.sharedMesh.vertices;
                foreach (var lv in v)
                {
                    var q = t.TransformPoint(lv);
                    int cx = Mathf.FloorToInt(q.x / 3f), cz = Mathf.FloorToInt(q.z / 3f);
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            var k = new Vector2Int(cx + dx, cz + dz);
                            if (!_water.TryGetValue(k, out float h) || q.y > h) _water[k] = q.y;
                        }
                }
            }
        }

        /// <summary>dry, walkable, free ground (not in water, under 24 degrees, no rock or trunk in the way)</summary>
        public static bool GoodGround(Vector3 p, float body, out Vector3 at)
        {
            at = p;
            var t = Terrain.activeTerrain; if (!t || !t.terrainData) return false;
            var tp = t.transform.position; var sz = t.terrainData.size;
            float u = (p.x - tp.x) / sz.x, v = (p.z - tp.z) / sz.z;
            if (u < 0.02f || v < 0.02f || u > 0.98f || v > 0.98f) return false;
            float y = t.SampleHeight(p) + tp.y; at.y = y;
            if (y < 1f || t.terrainData.GetSteepness(u, v) > 24f) return false;
            if (_water == null) BuildWater();
            if (_water.TryGetValue(new Vector2Int(Mathf.FloorToInt(p.x / 3f), Mathf.FloorToInt(p.z / 3f)), out float surf) && surf > y - 0.4f) return false;      // in or right at the water
            float r = Mathf.Clamp(body, 0.4f, 1.3f);
            foreach (var c in Physics.OverlapSphere(at + Vector3.up * (r + 0.9f), r, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!c || c.GetComponentInParent<DinosaurController>() || c.GetComponentInParent<AmbientCreature>()) continue;
                return false;                 // a rock, a trunk (terrain tree colliders), a prop
            }
            return true;
        }

        /// <summary>the nearest good ground to 'desired' within 'search' m (rings of 3 m); with a species also outside its no-go areas and heat rings</summary>
        public static Vector3 Ground(Vector3 desired, float search, float body, DinosaurDefinition def = null)
        {
            bool Good(Vector3 cand, out Vector3 found) => GoodGround(cand, body, out found) && (!def || !WildlifeZones.Forbidden(def, found));
            if (Good(desired, out var at)) return at;
            for (float r = 3f; r <= search; r += 3f)
            {
                int n = Mathf.Max(8, Mathf.RoundToInt(r * 1.2f));
                for (int i = 0; i < n; i++)
                {
                    float a = (i + r * 0.37f) / n * Mathf.PI * 2f;
                    if (Good(desired + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r, out at)) return at;
                }
            }
            var t = Terrain.activeTerrain;
            if (t) desired.y = t.SampleHeight(desired) + t.transform.position.y;
            return desired;
        }

        // ------------------------------------------------------------------ apply
        /// <summary>
        /// Lay the plan out under [Dinosaurs] (the DinosaurSpawner root). Idempotent: existing members are kept and moved,
        /// only the missing ones are cloned from them; the same seeds give the same positions every run. Returns a summary.
        /// </summary>
        public static string Apply(Transform root, Context ctx)
        {
            if (!root) return "no [Dinosaurs] root";
            _water = null; Physics.SyncTransforms();
            var sb = new System.Text.StringBuilder();
            // species values (the builder writes them, the runtime only fills untuned definitions)
            var defs = new HashSet<DinosaurDefinition>();
            foreach (var d in root.GetComponentsInChildren<DinosaurController>(true)) if (d.def) defs.Add(d.def);
            foreach (var d in defs) if (ApplySpecies(d, ctx.editor)) { ctx.dirty(d); sb.AppendLine($"species {d.id}: night {d.NightFear} aggression {d.Aggression} investigation {d.Investigation} fire {d.FireFear} weight {d.Weight01:F2} print {d.tracks.shape} {d.tracks.printSize} m"); }

            var route = Route();
            Resolve("migration_view", out var view, out _);
            foreach (var h in Herds)
            {
                var g = root.Find(h.group);
                if (!g) { sb.AppendLine($"herd {h.species}: no group '{h.group}' under {root.name}"); continue; }
                var members = Members(g, h.species);
                if (members.Count == 0) { sb.AppendLine($"herd {h.species}: no placed creature to copy"); continue; }
                Vector3 centre = h.migrates ? route[0] : Place(h.at, h.towards, h.edge, Vector3.zero) + h.offset;
                float body = members[0].def.bodyRadius;
                centre = Ground(centre, 24f, body, members[0].def);
                int added = Fill(g, members, h.count, ctx);
                var rnd = new System.Random(Seed(h.group));
                float rad = (WildlifeConfig.Instance.grazeSpread + WildlifeConfig.Instance.grazePerMember * members.Count) * 0.6f;
                LayOut(g, members, centre, rad, 2.4f * body + 1f, rnd, ctx, 0f);
                var herd = g.GetComponent<HerdGroup>(); if (!herd) herd = g.gameObject.AddComponent<HerdGroup>();
                herd.species = members[0].def; herd.label = h.label; herd.spread = 0f;
                if (h.migrates)
                {
                    Resolve("meadow", out var meadow, out _);
                    herd.home = new HerdPlaces { graze = route[0], graze2 = Ground(meadow, 20f, body) };
                    herd.away = new HerdPlaces { graze = Ground(route[route.Length - 1], 16f, body), graze2 = Ground(route[Mathf.Max(0, route.Length - 2)], 16f, body) };
                    var mig = g.GetComponent<MigrationDirector>(); if (!mig) mig = g.gameObject.AddComponent<MigrationDirector>();
                    mig.herd = herd; mig.route = route; mig.viewpoint = view;
                    ctx.dirty(mig);
                }
                else herd.home = new HerdPlaces { graze = centre };
                ctx.dirty(herd);
                foreach (var m in members) { m.home = Vector3.zero; m.homeRadius = rad * 2f; ctx.dirty(m); }
                sb.AppendLine($"herd {h.label} ({h.species}): {members.Count} ({added} new) at {Fmt(centre)}{(h.migrates ? $", migrates over {route.Length} route points" : "")}");
                if (ctx.editor && HerdHabitat.TryGetValue(h.species, out var hab)) MoveHabitat(hab, centre, ctx, sb);
            }
            foreach (var tr in Territories)
            {
                var g = root.Find(tr.group);
                if (!g) { sb.AppendLine($"territory {tr.species}: no group '{tr.group}'"); continue; }
                var members = Members(g, tr.species);
                if (members.Count == 0) { sb.AppendLine($"territory {tr.species}: no placed creature to copy"); continue; }
                float body = members[0].def.bodyRadius;
                Vector3 centre = Ground(Place(tr.at, tr.towards, tr.edge, Vector3.zero) + tr.offset, 30f, body, members[0].def);
                int added = Fill(g, members, tr.count, ctx);
                var rnd = new System.Random(Seed(tr.group));
                LayOut(g, members, centre, members.Count > 1 ? 5f + members.Count : 0.5f, 2.2f * body + 1.5f, rnd, ctx, 0f);
                foreach (var m in members) { m.home = centre; m.homeRadius = tr.homeRadius; ctx.dirty(m); }
                sb.AppendLine($"territory {tr.species} ({tr.role}): {members.Count} ({added} new) home {Fmt(centre)} r {tr.homeRadius}");
                if (ctx.editor && !string.IsNullOrEmpty(tr.habitat)) MoveHabitat(tr.habitat, centre, ctx, sb);
            }
            // Phase 2: the zones' own groups and the routines (WildlifePlanZones)
            foreach (var line in ApplyZones(root, ctx).Split('\n')) if (!string.IsNullOrWhiteSpace(line)) sb.AppendLine(line.TrimEnd());
            // every other land creature: back on the ground, out of the water (terrain v2.1) and out of no-go areas / heat rings
            foreach (var d in root.GetComponentsInChildren<DinosaurController>(true))
            {
                if (!d.def || InPlan(d)) continue;
                var p = Ground(d.transform.position, 30f, d.def.bodyRadius, d.def);
                if ((p - d.transform.position).sqrMagnitude > 0.01f) { d.transform.position = p; ctx.dirty(d.transform); sb.AppendLine($"re-snapped {d.name} to {Fmt(p)}"); }
            }
            return sb.ToString();
        }

        static bool InPlan(DinosaurController d)
        {
            var p = d.transform.parent; if (!p) return false;
            foreach (var h in Herds) if (p.name == h.group) return true;
            foreach (var t in Territories) if (p.name == t.group) return true;
            foreach (var z in ZoneGroups) if (p.name == z.group) return true;
            return false;
        }

        static List<DinosaurController> Members(Transform g, string species)
        {
            var list = new List<DinosaurController>();
            foreach (Transform c in g) { var d = c.GetComponent<DinosaurController>(); if (d && d.def && d.def.id == species) list.Add(d); }
            list.Sort((a, b) => Index(a.name).CompareTo(Index(b.name)));
            return list;
        }

        static int Index(string n) { int u = n.LastIndexOf('_'); return u >= 0 && int.TryParse(n.Substring(u + 1), out int i) ? i : 999; }

        /// <summary>clone the first member until the group has 'count' (never removes any)</summary>
        static int Fill(Transform g, List<DinosaurController> members, int count, Context ctx)
        {
            int added = 0, next = 0;
            foreach (var m in members) next = Mathf.Max(next, Index(m.name) + 1);
            string stem = members[0].name.Contains("_") ? members[0].name.Substring(0, members[0].name.LastIndexOf('_')) : members[0].name;
            while (members.Count < count)
            {
                var go = ctx.clone(members[0].gameObject, g);
                if (!go) break;
                go.name = stem + "_" + next++;
                var d = go.GetComponent<DinosaurController>(); if (!d) break;
                members.Add(d); added++;
            }
            return added;
        }

        /// <summary>members spread loosely around the centre (kept apart, facing roughly the same way, each on good ground)</summary>
        static void LayOut(Transform g, List<DinosaurController> members, Vector3 centre, float radius, float spacing, System.Random rnd, Context ctx, float yawBase)
        {
            g.position = centre; ctx.dirty(g);
            var placed = new List<Vector3>();
            float heading = (float)rnd.NextDouble() * 360f;
            foreach (var m in members)
            {
                Vector3 best = centre; float bestD = -1f;
                for (int k = 0; k < 24; k++)
                {
                    float a = (float)rnd.NextDouble() * Mathf.PI * 2f, r = Mathf.Sqrt((float)rnd.NextDouble()) * radius;
                    var p = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                    float near = 999f; foreach (var q in placed) near = Mathf.Min(near, Vector2.Distance(new Vector2(p.x, p.z), new Vector2(q.x, q.z)));
                    if (near >= spacing) { best = p; bestD = near; break; }
                    if (near > bestD) { bestD = near; best = p; }
                }
                var at = Ground(best, 10f, m.def.bodyRadius, m.def);
                placed.Add(at);
                m.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, heading + ((float)rnd.NextDouble() - 0.5f) * 80f, 0f));
                ctx.dirty(m.transform);
            }
        }

        static void MoveHabitat(string name, Vector3 at, Context ctx, System.Text.StringBuilder sb)
        {
            var m = MarkersRoot(); var h = m ? m.Find("Habitats") : null; var t = h ? h.Find(name) : null;
            if (!t) return;
            if ((t.position - at).sqrMagnitude < 0.01f) return;
            sb.AppendLine($"Markers/Habitats/{name}: {Fmt(t.position)} -> {Fmt(at)}");
            t.position = at; ctx.dirty(t);
        }

        /// <summary>stable seed from a name (string.GetHashCode is not stable across runtimes)</summary>
        static int Seed(string s) { int h = 17; foreach (char c in s) h = unchecked(h * 31 + c); return h & 0x7fffffff; }

        /// <summary>does this [Dinosaurs] root hold the plan's species groups (the island), so the plan can be laid out</summary>
        public static bool HasPlanGroups(Transform root)
        {
            if (!root) return false;
            foreach (var h in Herds) if (root.Find(h.group)) return true;
            return false;
        }

        /// <summary>tests / tools: off = a scene that was never rebuilt keeps its old creatures at runtime</summary>
        public static bool RuntimeFallback = true;

        static string Fmt(Vector3 v) => $"({v.x:F1}, {v.y:F1}, {v.z:F1})";
    }
}
