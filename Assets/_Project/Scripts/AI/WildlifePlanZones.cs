using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PrimalFrontier.AI
{
    /// <summary>
    /// Phase 2 (AI): the wildlife of the six world zones, as data. ZoneGroups adds creatures of the existing species where
    /// a zone needs them (cloned from the island's own animals: never a new species); Routines gives single creatures a
    /// scheduled outing (WildlifeRoutine): patrols, visits, wading, scavenging. Places are AI anchors of the zone builders
    /// (WildlifeZones.TryAnchor: AI_Anchors children, Markers/Zones ids, "trail:&lt;group&gt;" = the WP_nn children of a trail
    /// object, "home" = the creature's own home), each with a fallback position. Legs between stops are walked along a ground
    /// path found on the terrain (FindPath: slope, water, rocks and trunks, no-go areas, heat), so a visitor goes round
    /// cliffs and along the river instead of in a straight line. Applied with the rest of the plan (Apply), idempotent.
    /// </summary>
    public static partial class WildlifePlan
    {
        public struct ZoneGroupRow
        {
            public string zone, group, species, from, stem, label, at;
            public Vector3 fallback; public int count; public float homeRadius;
            public bool herd, flyer; public float altitude;
        }
        public struct StopRow { public string at; public Vector3 fallback; public float stay; public RoutineStop act; }
        public struct RoutineRow
        {
            public string id, zone, group, species; public int member; public RoutineKind kind;
            public float from, to; public int every, offset; public float chance; public bool loop; public float speed; public int visits;
            public string follow; public StopRow[] stops; public string note;
        }

        static StopRow S(string at, float x, float z, float stay = 0f, RoutineStop act = RoutineStop.Walk) => new StopRow { at = at, fallback = new Vector3(x, 0f, z), stay = stay, act = act };
        static readonly StopRow Home = new StopRow { at = "home" };

        public static readonly ZoneGroupRow[] ZoneGroups =
        {
            new ZoneGroupRow { zone = "wetland", group = "Wetland_Parasaurolophus", species = "parasaurolophus", from = "Parasaurolophus", stem = "Parasaurolophus_Wetland", label = "reed trio", count = 3, herd = true,
                at = "wetland_herbivores", fallback = new Vector3(132f, 0f, 150f) },
            new ZoneGroupRow { zone = "bone", group = "BoneValley_Velociraptor", species = "velociraptor", from = "Velociraptor", stem = "Velociraptor_BoneValley", label = "carcass raptor", count = 1, homeRadius = 20f,
                at = "bone_raptor_den", fallback = new Vector3(-72f, 0f, -104f) },
            new ZoneGroupRow { zone = "bone", group = "BoneValley_Pteranodon", species = "pteranodon", from = "Pteranodon", stem = "Pteranodon_BoneValley", label = "scavenging flyers", count = 2, flyer = true, homeRadius = 26f, altitude = 28f,
                at = "BV_CircleCentre", fallback = new Vector3(-114f, 0f, -52f) },
            new ZoneGroupRow { zone = "foothills", group = "Foothills_Pteranodon", species = "pteranodon", from = "Pteranodon", stem = "Pteranodon_Foothills", label = "high flyer", count = 1, flyer = true, homeRadius = 48f, altitude = 44f,
                at = "foothills_sky", fallback = new Vector3(-130f, 0f, -168f) },
            new ZoneGroupRow { zone = "foothills", group = "Foothills_Ankylosaurus", species = "ankylosaurus", from = "Ankylosaurus", stem = "Ankylosaurus_Foothills", label = "low grazer", count = 1, homeRadius = 16f,
                at = "foothills_grazing", fallback = new Vector3(-140f, 0f, -110f) },
        };

        public static readonly RoutineRow[] Routines =
        {
            new RoutineRow { id = "carno_valley_bone_patrol", zone = "valley+bone", group = "Carnotaurus", species = "carnotaurus", member = 0, kind = RoutineKind.Patrol,
                from = 13.5f, to = 17.5f, every = 1, chance = 0.7f, speed = 1f, note = "afternoon: watches the herds from the valley's west edge, then walks BONE's route through Bone Valley and home",
                stops = new[] { S("valley_edge_watch", -48f, -46f, 45f, RoutineStop.Observe), S("seq:BV_PredRoute_@2", -108f, -56f, 20f, RoutineStop.Feed), Home } },
            new RoutineRow { id = "spino_wetland_visit", zone = "wetland", group = "Spinosaurus", species = "spinosaurus", member = 0, kind = RoutineKind.Visit,
                from = 11.5f, to = 15f, every = 2, offset = 0, chance = 0.8f, speed = 1f, note = "every 2nd day (from day 2), midday: down the river to the lagoon, wades and fishes at three spots, walks back",
                stops = new[] { S("wetland_wade_1", 120f, 176f, 70f, RoutineStop.Wade), S("wetland_wade_2", 112f, 186f, 60f, RoutineStop.Wade), S("wetland_wade_3", 126f, 165f, 60f, RoutineStop.Wade), Home } },
            new RoutineRow { id = "raptor_fern_stalk", zone = "fern", group = "Velociraptor", species = "velociraptor", member = 0, kind = RoutineKind.Patrol,
                from = 15.5f, to = 22f, every = 1, chance = 1f, loop = true, speed = 0.85f, note = "afternoon to night: the pair creeps along ENV-B's stalker path through the thickest ferns and back",
                stops = new[] { S("trail:Trail_C_StalkerPath", 100f, 24f), S("fern_ambush", 142f, 52f, 25f, RoutineStop.Observe), S("trail-back:Trail_C_StalkerPath", 100f, 24f) } },
            new RoutineRow { id = "raptor_fern_pair", zone = "fern", group = "Velociraptor", species = "velociraptor", member = 1, kind = RoutineKind.Patrol,
                from = 15.5f, to = 22f, every = 1, chance = 1f, speed = 0.9f, follow = "raptor_fern_stalk", note = "the second of the pair, a few metres behind the first", stops = new StopRow[0] },
            new RoutineRow { id = "raptor_wetland_dusk", zone = "wetland", group = "Velociraptor", species = "velociraptor", member = 2, kind = RoutineKind.Patrol,
                from = 17.5f, to = 20.5f, every = 1, chance = 0.6f, speed = 0.95f, note = "dusk: a lone raptor slips out to the wetland's inland shore, watches, and goes back",
                stops = new[] { S("wetland_raptor_watch_1", 140f, 152f, 30f, RoutineStop.Observe), S("wetland_raptor_watch_2", 132f, 192f, 25f, RoutineStop.Observe), Home } },
            new RoutineRow { id = "raptor_bone_feed", zone = "bone", group = "BoneValley_Velociraptor", species = "velociraptor", member = 0, kind = RoutineKind.Visit,
                from = 15f, to = 18.5f, every = 1, chance = 0.6f, speed = 1f, note = "late afternoon, some days: feeds at the fresh carcass, then back to its den",
                stops = new[] { S("BV_Scavenge_fresh_1", -108f, -56f, 50f, RoutineStop.Feed), Home } },
            new RoutineRow { id = "ptera_bone_scavenge_a", zone = "bone", group = "BoneValley_Pteranodon", species = "pteranodon", member = 0, kind = RoutineKind.Scavenge,
                from = 8f, to = 17.5f, every = 1, chance = 1f, visits = 3, note = "by day: circles over the carcasses, lands by one now and then",
                stops = new[] { S("seq:BV_Scavenge_", -108f, -56f), S("seq:BV_Perch_", -119f, -40f) } },
            new RoutineRow { id = "ptera_bone_scavenge_b", zone = "bone", group = "BoneValley_Pteranodon", species = "pteranodon", member = 1, kind = RoutineKind.Scavenge,
                from = 9f, to = 18f, every = 1, chance = 0.85f, visits = 2,
                stops = new[] { S("seq:BV_Scavenge_", -119f, -40f), S("seq:BV_Perch_", -108f, -56f) } },
        };

        /// <summary>the zone each placed creature belongs to (checks): by group name, else nearest zone circle</summary>
        public static string ZoneOfGroup(string group)
        {
            foreach (var z in ZoneGroups) if (z.group == group) return z.zone;
            switch (group)
            {
                case "Parasaurolophus": case "Triceratops": return "valley";
                case "Velociraptor": return "fern";
                case "Rift Tyrant": return "bone";
                default: return null;
            }
        }

        // ------------------------------------------------------------------ apply
        static string ApplyZones(Transform root, Context ctx)
        {
            var sb = new StringBuilder();
            WildlifeZones.Invalidate();
            foreach (var z in ZoneGroups)
            {
                var g = root.Find(z.group);
                if (!g)
                {
                    g = new GameObject(z.group).transform; g.SetParent(root, false);
                    ctx.created(g.gameObject);
                }
                Vector3 want = WildlifeZones.Anchor(z.at, z.fallback);
                if (z.flyer) { sb.AppendLine(FlyerGroup(root, g, z, want, ctx)); continue; }
                var members = Members(g, z.species);
                if (members.Count == 0)
                {
                    var src = FirstOf(root, z.from, z.species);
                    if (!src) { sb.AppendLine($"zone {z.zone} {z.group}: no {z.species} to copy"); continue; }
                    var go = ctx.clone(src.gameObject, g); if (!go) continue;
                    go.name = z.stem + "_0";
                    var d0 = go.GetComponent<DinosaurController>(); if (!d0) continue;
                    var rt = go.GetComponent<WildlifeRoutine>(); if (rt) ctx.remove(rt);        // a copied patrol is not this animal's
                    members.Add(d0);
                }
                float body = members[0].def.bodyRadius;
                Vector3 centre = Ground(want, 30f, body, members[0].def);
                int added = Fill(g, members, z.count, ctx);
                var rnd = new System.Random(Seed(z.group));
                if (z.herd)
                {
                    float rad = (WildlifeConfig.Instance.grazeSpread + WildlifeConfig.Instance.grazePerMember * members.Count) * 0.6f;
                    LayOut(g, members, centre, rad, 2.4f * body + 1f, rnd, ctx, 0f);
                    var herd = g.GetComponent<HerdGroup>(); if (!herd) herd = g.gameObject.AddComponent<HerdGroup>();
                    herd.species = members[0].def; herd.label = z.label; herd.spread = 0f; herd.home = new HerdPlaces { graze = centre };
                    ctx.dirty(herd);
                    foreach (var m in members) { m.home = Vector3.zero; m.homeRadius = rad * 2f; ctx.dirty(m); }
                }
                else
                {
                    LayOut(g, members, centre, members.Count > 1 ? 5f + members.Count : 0.5f, 2.2f * body + 1.5f, rnd, ctx, 0f);
                    foreach (var m in members) { m.home = centre; m.homeRadius = z.homeRadius > 0f ? z.homeRadius : 20f; ctx.dirty(m); }
                }
                sb.AppendLine($"zone {z.zone}: {z.group} ({z.label}, {z.species}) {members.Count} ({added} new) at {Fmt(centre)}{(WildlifeZones.TryAnchor(z.at, out _) ? $" (anchor {z.at})" : $" (fallback, anchor '{z.at}' not found)")}");
            }
            foreach (var r in Routines) sb.AppendLine(ApplyRoutine(root, r, ctx));
            return sb.ToString();
        }

        static DinosaurController FirstOf(Transform root, string group, string species)
        {
            var g = root.Find(group);
            if (g) { var l = Members(g, species); if (l.Count > 0) return l[0]; }
            foreach (var d in root.GetComponentsInChildren<DinosaurController>(true)) if (d.def && d.def.id == species) return d;
            return null;
        }

        /// <summary>ambient flyers of a zone: circling over it (cloned from the island's own flyers)</summary>
        static string FlyerGroup(Transform root, Transform g, ZoneGroupRow z, Vector3 want, Context ctx)
        {
            var list = new List<AmbientCreature>();
            foreach (Transform c in g) { var a = c.GetComponent<AmbientCreature>(); if (a && a.def && a.def.id == z.species) list.Add(a); }
            list.Sort((a, b) => Index(a.name).CompareTo(Index(b.name)));
            AmbientCreature src = list.Count > 0 ? list[0] : null;
            if (!src)
            {
                var sg = root.Find(z.from);
                if (sg) foreach (Transform c in sg) { var a = c.GetComponent<AmbientCreature>(); if (a && a.def && a.def.id == z.species) { src = a; break; } }
                if (!src) foreach (var a in root.GetComponentsInChildren<AmbientCreature>(true)) if (a.def && a.def.id == z.species) { src = a; break; }
            }
            if (!src) return $"zone {z.zone} {z.group}: no {z.species} to copy";
            int added = 0, next = 0;
            foreach (var a in list) next = Mathf.Max(next, Index(a.name) + 1);
            while (list.Count < z.count)
            {
                var go = ctx.clone(src.gameObject, g); if (!go) break;
                go.name = z.stem + "_" + next++;
                var a = go.GetComponent<AmbientCreature>(); if (!a) break;
                var rt = go.GetComponent<WildlifeRoutine>(); if (rt) ctx.remove(rt);
                list.Add(a); added++;
            }
            var t = Terrain.activeTerrain;
            Vector3 c0 = want; if (t) c0.y = t.SampleHeight(c0) + t.transform.position.y;
            g.position = c0; ctx.dirty(g);
            var rnd = new System.Random(Seed(z.group));
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                // each circles the zone on its own ring (a little apart in radius and height)
                a.center = c0 + new Vector3(((float)rnd.NextDouble() - 0.5f) * 10f, 0f, ((float)rnd.NextDouble() - 0.5f) * 10f);
                a.radius = z.homeRadius * (0.8f + 0.35f * i); a.altitude = z.altitude + 5f * i; a.swimmer = false;
                float ang = (float)rnd.NextDouble() * Mathf.PI * 2f;
                a.transform.SetPositionAndRotation(a.center + new Vector3(Mathf.Cos(ang) * a.radius, a.altitude, Mathf.Sin(ang) * a.radius), Quaternion.Euler(0f, ang * Mathf.Rad2Deg + 90f, 0f));
                ctx.dirty(a); ctx.dirty(a.transform);
            }
            return $"zone {z.zone}: {z.group} ({z.label}, {z.species}) {list.Count} ({added} new) circling {Fmt(c0)} r {z.homeRadius} at {z.altitude} m{(WildlifeZones.TryAnchor(z.at, out _) ? $" (anchor {z.at})" : $" (fallback, anchor '{z.at}' not found)")}";
        }

        static string ApplyRoutine(Transform root, RoutineRow r, Context ctx)
        {
            var g = root.Find(r.group);
            if (!g) return $"routine {r.id}: no group '{r.group}'";
            GameObject who = null; DinosaurController dc = null; AmbientCreature ac = null;
            if (r.kind == RoutineKind.Scavenge)
            {
                var list = new List<AmbientCreature>();
                foreach (Transform c in g) { var a = c.GetComponent<AmbientCreature>(); if (a && a.def && a.def.id == r.species) list.Add(a); }
                list.Sort((a, b) => Index(a.name).CompareTo(Index(b.name)));
                if (r.member < list.Count) { ac = list[r.member]; who = ac.gameObject; }
            }
            else
            {
                var list = Members(g, r.species);
                if (r.member < list.Count) { dc = list[r.member]; who = dc.gameObject; }
            }
            if (!who) return $"routine {r.id}: {r.group} has no member {r.member}";
            var rt = who.GetComponent<WildlifeRoutine>(); if (!rt) rt = who.AddComponent<WildlifeRoutine>();
            rt.id = r.id; rt.kind = r.kind; rt.hours = new Vector2(r.from, r.to); rt.everyDays = Mathf.Max(1, r.every); rt.dayOffset = r.offset;
            rt.chance = r.chance; rt.loop = r.loop; rt.speedMul = r.speed > 0f ? r.speed : 1f; rt.maxVisits = Mathf.Max(1, r.visits);
            rt.leaderId = r.follow ?? "";
            var pts = new List<Vector3>(); var stay = new List<float>(); var acts = new List<RoutineStop>();
            var sb = new StringBuilder();
            int anchored = 0, fell = 0, pathLegs = 0, pathPts = 0; float length = 0f;
            Vector3 home = dc ? (dc.home != Vector3.zero ? dc.home : dc.transform.position) : who.transform.position;
            Vector3 prev = who.transform.position;
            float body = dc && dc.def ? dc.def.bodyRadius : 0.5f;
            if (r.stops != null)
                foreach (var s in r.stops)
                {
                    // a trail: every WP_nn of the trail object (forwards, or backwards for "trail-back:")
                    if (s.at != null && (s.at.StartsWith("trail:") || s.at.StartsWith("trail-back:")))
                    {
                        bool back = s.at.StartsWith("trail-back:");
                        var wps = TrailPoints(s.at.Substring(s.at.IndexOf(':') + 1));
                        if (wps.Count == 0) { wps.Add(Ground(s.fallback, 12f, body, dc ? dc.def : null)); fell++; } else anchored++;
                        if (back) wps.Reverse();
                        for (int i = 0; i < wps.Count; i++)
                        {
                            if (dc) length += Leg(prev, wps[i], dc, pts, stay, acts, ref pathLegs, ref pathPts, false);
                            pts.Add(wps[i]); stay.Add(i == wps.Count - 1 ? s.stay : 0f); acts.Add(i == wps.Count - 1 ? s.act : RoutineStop.Walk);
                            prev = wps[i];
                        }
                        continue;
                    }
                    // a sequence of anchors: every anchor whose name starts with the prefix, in name order ("@k": the stop's stay / action at the k-th)
                    if (s.at != null && s.at.StartsWith("seq:"))
                    {
                        string pre = s.at.Substring(4); int at = -1; int k0 = pre.IndexOf('@');
                        if (k0 >= 0) { int.TryParse(pre.Substring(k0 + 1), out at); pre = pre.Substring(0, k0); }
                        var seq = new List<KeyValuePair<string, Transform>>();
                        foreach (var kv in WildlifeZones.Anchors) if (kv.Value && kv.Key.StartsWith(pre)) seq.Add(kv);
                        seq.Sort((x, y) => string.CompareOrdinal(x.Key, y.Key));
                        var sp = new List<Vector3>(); foreach (var kv in seq) sp.Add(kv.Value.position);
                        if (sp.Count == 0) { sp.Add(s.fallback); fell++; at = 0; } else anchored++;
                        for (int i = 0; i < sp.Count; i++)
                        {
                            Vector3 q = sp[i];
                            if (r.kind != RoutineKind.Scavenge) q = Ground(q, 10f, body, dc ? dc.def : null);
                            else if (sp.Count == 1 && fell > 0) { var t = Terrain.activeTerrain; if (t) q.y = t.SampleHeight(q) + t.transform.position.y; }
                            bool here = at < 0 ? i == sp.Count - 1 && r.kind != RoutineKind.Scavenge : i == at;
                            if (dc) length += Leg(prev, q, dc, pts, stay, acts, ref pathLegs, ref pathPts, false);
                            pts.Add(q); stay.Add(here ? s.stay : 0f); acts.Add(here ? s.act : RoutineStop.Walk);
                            prev = q;
                        }
                        continue;
                    }
                    Vector3 p;
                    if (s.at == "home") { p = home; anchored++; }
                    else if (WildlifeZones.TryAnchor(s.at, out p)) anchored++;
                    else { p = s.fallback; fell++; }
                    if (r.kind != RoutineKind.Scavenge && s.act != RoutineStop.Wade) p = Ground(p, 16f, body, dc ? dc.def : null);
                    else { var t = Terrain.activeTerrain; if (t) p.y = t.SampleHeight(p) + t.transform.position.y; }
                    if (dc) length += Leg(prev, p, dc, pts, stay, acts, ref pathLegs, ref pathPts, s.act == RoutineStop.Wade);
                    pts.Add(p); stay.Add(s.stay); acts.Add(s.act);
                    prev = p;
                }
            rt.points = pts.ToArray(); rt.stay = stay.ToArray(); rt.actions = acts.ToArray();
            ctx.dirty(rt);
            sb.Append($"routine {r.id} ({r.kind}, {r.zone}) on {who.name}: {r.from:0.#}-{r.to:0.#} h, every {rt.everyDays} day(s){(r.offset != 0 ? $" +{r.offset}" : "")}, chance {r.chance:0.##}{(r.loop ? ", loops" : "")}{(string.IsNullOrEmpty(r.follow) ? "" : $", follows {r.follow}")}; ");
            sb.Append($"{pts.Count} points ({anchored} anchored, {fell} fallback{(pathLegs > 0 ? $", {pathLegs} legs routed on the ground, +{pathPts} path points" : "")}), ~{length:0} m");
            for (int i = 0; i < pts.Count; i++) if (stay[i] > 0f || acts[i] != RoutineStop.Walk) sb.Append($"; stop {Fmt(pts[i])} {acts[i]} {stay[i]:0} s");
            return sb.ToString();
        }

        static List<Vector3> TrailPoints(string name)
        {
            var list = new List<Vector3>();
            Transform best = null;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (t.name == name && t.childCount > 0) { best = t; break; }
            if (!best) return list;
            var kids = new List<Transform>(); foreach (Transform c in best) if (c.name.StartsWith("WP_")) kids.Add(c);
            kids.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            foreach (var c in kids) list.Add(c.position);
            return list;
        }

        /// <summary>the ground path from a to b (inner points only) added before the stop; returns the leg length</summary>
        static float Leg(Vector3 a, Vector3 b, DinosaurController dc, List<Vector3> pts, List<float> stay, List<RoutineStop> acts, ref int legs, ref int added, bool wadeEnd)
        {
            float straight = Vector3.Distance(new Vector3(a.x, 0f, a.z), new Vector3(b.x, 0f, b.z));
            if (straight < 20f) return straight;
            var path = new List<Vector3>();
            if (!FindPath(a, b, dc.def, wadeEnd ? b : (Vector3?)null, path)) return straight;
            legs++;
            float len = 0f; Vector3 q = a;
            // inner points only (every ~25 m), the end point is the stop itself
            for (int i = 1; i < path.Count - 1; i++) { pts.Add(path[i]); stay.Add(0f); acts.Add(RoutineStop.Walk); added++; len += Vector3.Distance(q, path[i]); q = path[i]; }
            return len + Vector3.Distance(q, b);
        }

        // ------------------------------------------------------------------ ground paths (A* on a 4 m terrain grid)
        const float PathCell = 4f;

        /// <summary>
        /// a walkable ground path from a to b for this species (A* over 4 m cells: slope under 26 degrees, dry ground or, within
        /// 30 m of wadeTo, water up to 0.9 m deep; no rocks / trunks / props, no no-go area or heat ring), string-pulled and
        /// split into points at most 25 m apart. False = no path (the caller walks straight).
        /// </summary>
        public static bool FindPath(Vector3 a, Vector3 b, DinosaurDefinition def, Vector3? wadeTo, List<Vector3> result)
        {
            result.Clear();
            var t = Terrain.activeTerrain; if (!t || !t.terrainData) return false;
            if (_water == null) BuildWater();
            var tp = t.transform.position; var sz = t.terrainData.size;
            int nx = Mathf.CeilToInt(sz.x / PathCell), nz = Mathf.CeilToInt(sz.z / PathCell);
            float body = def ? Mathf.Clamp(def.bodyRadius * 0.6f, 0.4f, 1.1f) : 0.6f;
            var state = new sbyte[nx * nz];        // 0 unknown, 1 open, 2 blocked
            Vector3 Cell(int x, int z) => new Vector3(tp.x + (x + 0.5f) * PathCell, 0f, tp.z + (z + 0.5f) * PathCell);
            bool Open(int x, int z, out float cost)
            {
                cost = 1f;
                if (x < 1 || z < 1 || x >= nx - 1 || z >= nz - 1) return false;
                int i = z * nx + x;
                var p = Cell(x, z); float u = (p.x - tp.x) / sz.x, v = (p.z - tp.z) / sz.z;
                float y = t.SampleHeight(p) + tp.y; p.y = y;
                float slope = t.terrainData.GetSteepness(u, v);
                bool wet = _water.TryGetValue(new Vector2Int(Mathf.FloorToInt(p.x / 3f), Mathf.FloorToInt(p.z / 3f)), out float surf) && surf > y - 0.2f;
                bool nearWade = wadeTo.HasValue && Flat(p - wadeTo.Value) < 30f;
                cost = 1f + slope / 26f * 1.5f + (wet ? 2.5f : 0f);
                if (state[i] == 1) return true;
                if (state[i] == 2) return false;
                bool ok = slope < 26f && (y >= 0.8f || nearWade) && (!wet || (nearWade && surf - y < 0.9f) || surf - y < 0.35f);
                if (ok && def && WildlifeZones.Forbidden(def, p)) ok = false;
                if (ok)
                {
                    int n = Physics.OverlapSphereNonAlloc(p + Vector3.up * (body + 0.9f), body, _hits, ~0, QueryTriggerInteraction.Ignore);
                    for (int k = 0; k < n; k++) { var c = _hits[k]; if (!c || c.GetComponentInParent<DinosaurController>() || c.GetComponentInParent<AmbientCreature>()) continue; ok = false; break; }
                }
                state[i] = (sbyte)(ok ? 1 : 2);
                return ok;
            }
            int ax = Mathf.Clamp(Mathf.FloorToInt((a.x - tp.x) / PathCell), 0, nx - 1), az = Mathf.Clamp(Mathf.FloorToInt((a.z - tp.z) / PathCell), 0, nz - 1);
            int bx = Mathf.Clamp(Mathf.FloorToInt((b.x - tp.x) / PathCell), 0, nx - 1), bz = Mathf.Clamp(Mathf.FloorToInt((b.z - tp.z) / PathCell), 0, nz - 1);
            // start / goal on a blocked cell (a creature standing by a rock, a stop in reeds): the nearest open cell nearby
            if (!NearestOpen(ref ax, ref az, Open) || !NearestOpen(ref bx, ref bz, Open)) return false;
            var g = new float[nx * nz]; var prevOf = new int[nx * nz]; var closed = new bool[nx * nz];
            for (int i = 0; i < g.Length; i++) { g[i] = float.MaxValue; prevOf[i] = -1; }
            var open = new List<(float f, int i)>();
            int start = az * nx + ax, goal = bz * nx + bx;
            g[start] = 0f; open.Add((0f, start));
            int iterations = 0;
            while (open.Count > 0 && iterations++ < 60000)
            {
                int bi = 0; for (int k = 1; k < open.Count; k++) if (open[k].f < open[bi].f) bi = k;
                int cur = open[bi].i; open[bi] = open[open.Count - 1]; open.RemoveAt(open.Count - 1);
                if (closed[cur]) continue;
                closed[cur] = true;
                if (cur == goal) break;
                int cx = cur % nx, cz = cur / nx;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int x = cx + dx, z = cz + dz; if (x < 0 || z < 0 || x >= nx || z >= nz) continue;
                        int ni = z * nx + x; if (closed[ni] || !Open(x, z, out float c)) continue;
                        if (dx != 0 && dz != 0 && (!Open(cx + dx, cz, out _) || !Open(cx, cz + dz, out _))) continue;      // no corner cutting
                        float step = (dx != 0 && dz != 0 ? 1.4142f : 1f) * c;
                        float ng = g[cur] + step;
                        if (ng < g[ni]) { g[ni] = ng; prevOf[ni] = cur; float h = Mathf.Sqrt((x - bx) * (x - bx) + (z - bz) * (z - bz)); open.Add((ng + h, ni)); }
                    }
            }
            if (prevOf[goal] < 0 && goal != start) return false;
            var cells = new List<int>(); for (int c = goal; c >= 0; c = prevOf[c]) { cells.Add(c); if (c == start) break; }
            cells.Reverse();
            // string pulling: keep a cell only when the straight line from the last kept one crosses a blocked cell
            var keep = new List<Vector3> { a };
            int last = 0;
            for (int k = 2; k < cells.Count; k++)
                if (!Straight(cells[last], cells[k], nx, Open)) { last = k - 1; keep.Add(Snap3(Cell(cells[last] % nx, cells[last] / nx), t)); }
            keep.Add(b);
            // at most 25 m between points
            result.Add(keep[0]);
            for (int k = 1; k < keep.Count; k++)
            {
                Vector3 p0 = result[result.Count - 1], p1 = keep[k]; float d = Flat(p1 - p0);
                int parts = Mathf.CeilToInt(d / 25f);
                for (int s = 1; s < parts; s++) result.Add(Snap3(Vector3.Lerp(p0, p1, s / (float)parts), t));
                result.Add(p1);
            }
            return true;
        }

        static readonly Collider[] _hits = new Collider[16];
        delegate bool OpenFn(int x, int z, out float cost);

        static bool NearestOpen(ref int x, ref int z, OpenFn open)
        {
            if (open(x, z, out _)) return true;
            for (int r = 1; r <= 4; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                        if ((Mathf.Abs(dx) == r || Mathf.Abs(dz) == r) && open(x + dx, z + dz, out _)) { x += dx; z += dz; return true; }
            return false;
        }

        static bool Straight(int c0, int c1, int nx, OpenFn open)
        {
            int x0 = c0 % nx, z0 = c0 / nx, x1 = c1 % nx, z1 = c1 / nx;
            int steps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(z1 - z0)) * 2;
            for (int s = 1; s < steps; s++)
            {
                float k = s / (float)steps;
                int x = Mathf.RoundToInt(Mathf.Lerp(x0, x1, k)), z = Mathf.RoundToInt(Mathf.Lerp(z0, z1, k));
                if (!open(x, z, out _)) return false;
            }
            return true;
        }

        static Vector3 Snap3(Vector3 p, Terrain t) { p.y = t.SampleHeight(p) + t.transform.position.y; return p; }
        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

        /// <summary>checks: is there a ground path between a and b for this species (reachability)</summary>
        public static bool Reachable(Vector3 a, Vector3 b, DinosaurDefinition def, out int points)
        {
            var l = new List<Vector3>(); bool ok = FindPath(a, b, def, null, l); points = l.Count; return ok;
        }
    }
}
