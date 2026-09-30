using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Core;
using PrimalFrontier.Player;
using PrimalFrontier.World;

namespace PrimalFrontier.AI
{
    /// <summary>
    /// Phase 2 (AI): the six zones as the wildlife sees them. (1) No-go areas: heat hazard rings (HazardZone, from
    /// WildlifeConfig.heatAvoidLevel for destinations and placement, heatHardLevel while moving) and the configured areas
    /// (WildlifeConfig.noGoAreas: the cave, the start beach for predators, ...). Destinations, wander points, routine stops,
    /// perches and placement skip them; a creature walking into one turns away (a hard one blocks everything but fleeing).
    /// (2) Zone visibility (PerceptionConfig.visibilityZones): inside dense vegetation (the Giant Fern Forest) creatures see
    /// the player and each other over shorter distances, and a crouched player in dense plants (terrain details, trees,
    /// bushes) is harder still to spot. (3) AI anchors: named empty objects the zone builders put under an object called
    /// "AI_Anchors" (their reports list them), with Markers/Zones ids and fallback positions behind them.
    /// Queries allocate nothing after the first use.
    /// </summary>
    public static class WildlifeZones
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Invalidate(); }

        /// <summary>scene changed (builder, tests): look the anchors up again</summary>
        public static void Invalidate()
        {
            _anchors = null; _areaCentres = null; _visCentres = null; _grids = null; _playerW = null; _playerDense = null; _nextPlayer = -1f;
        }

        // ------------------------------------------------------------------ anchors
        static Dictionary<string, Transform> _anchors;

        static void BuildAnchors()
        {
            _anchors = new Dictionary<string, Transform>();
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!t || t.name != "AI_Anchors") continue;
                foreach (var c in t.GetComponentsInChildren<Transform>(true))
                    if (c != t && !_anchors.ContainsKey(c.name)) _anchors[c.name] = c;
            }
        }

        /// <summary>all anchors found (name -> transform), for the builder / checks</summary>
        public static IReadOnlyDictionary<string, Transform> Anchors { get { if (_anchors == null) BuildAnchors(); return _anchors; } }

        /// <summary>
        /// a named place: "spawn" (the player's start), an AI anchor (child of an AI_Anchors object), a Markers/Zones id or
        /// "route:NN" (WildlifePlan.Resolve). False = none of them exists (the caller uses its fallback).
        /// </summary>
        public static bool TryAnchor(string name, out Vector3 p)
        {
            p = default;
            if (string.IsNullOrEmpty(name)) return false;
            if (name == "spawn") { if (HuntDirector.SafeStart(out p)) return true; return false; }
            if (_anchors == null) BuildAnchors();
            if (_anchors.TryGetValue(name, out var t) && t) { p = t.position; return true; }
            return WildlifePlan.Resolve(name, out p, out _);
        }

        public static Vector3 Anchor(string name, Vector3 fallback) => TryAnchor(name, out var p) ? p : fallback;

        // ------------------------------------------------------------------ no-go areas
        static Vector3[] _areaCentres;

        static Vector3 AreaCentre(WildlifeConfig w, int i)
        {
            if (_areaCentres == null || _areaCentres.Length != w.noGoAreas.Count)
            {
                _areaCentres = new Vector3[w.noGoAreas.Count];
                for (int k = 0; k < _areaCentres.Length; k++) { var a = w.noGoAreas[k]; _areaCentres[k] = a == null ? Vector3.zero : Anchor(a.anchor, a.center); }
            }
            return _areaCentres[i];
        }

        static bool Applies(WildlifeConfig.NoGoArea a, DinosaurDefinition def, bool flyer)
        {
            if (a == null || a.radius <= 0f) return false;
            if (flyer) return a.flyers;
            if (!def) return true;
            if (a.predatorsOnly && !def.IsPredator) return false;
            return def.bodyRadius >= a.minBody;
        }

        /// <summary>the heat hazard ring at p (0 safe .. 3 dangerous, HazardZone.Level)</summary>
        public static int HeatLevel(Vector3 p)
        {
            int best = 0; var all = HazardZone.All;
            for (int i = 0; i < all.Count; i++) { var h = all[i]; if (!h) continue; int l = (int)h.LevelAt(p); if (l > best) best = l; }
            return best;
        }

        /// <summary>
        /// p is off limits for this creature. moving = the creature is on its way (only the heatHardLevel ring and the
        /// areas count then, and hardOnly keeps just the hard ones: a chase). flyer = an ambient flyer's perch.
        /// </summary>
        public static bool Forbidden(DinosaurDefinition def, Vector3 p, bool moving = false, bool hardOnly = false, bool flyer = false)
        {
            var w = WildlifeConfig.Instance;
            int heat = HeatLevel(p);
            if (heat > 0 && heat >= (hardOnly ? w.heatChaseLevel : moving ? w.heatHardLevel : w.heatAvoidLevel)) return true;
            var areas = w.noGoAreas; if (areas == null) return false;
            for (int i = 0; i < areas.Count; i++)
            {
                var a = areas[i];
                if (!Applies(a, def, flyer) || (hardOnly && !a.hard)) continue;
                Vector3 c = AreaCentre(w, i); float dx = p.x - c.x, dz = p.z - c.z;
                if (dx * dx + dz * dz < a.radius * a.radius) return true;
            }
            return false;
        }

        /// <summary>name of the no-go area / heat ring p is in for this creature (checks / logs), null = none</summary>
        public static string ForbiddenWhy(DinosaurDefinition def, Vector3 p, bool flyer = false)
        {
            var w = WildlifeConfig.Instance;
            int heat = HeatLevel(p);
            if (heat > 0 && heat >= w.heatAvoidLevel) return "heat ring " + (HazardZone.Level)heat;
            var areas = w.noGoAreas; if (areas == null) return null;
            for (int i = 0; i < areas.Count; i++)
            {
                var a = areas[i]; if (!Applies(a, def, flyer)) continue;
                Vector3 c = AreaCentre(w, i); float dx = p.x - c.x, dz = p.z - c.z;
                if (dx * dx + dz * dz < a.radius * a.radius) return "no-go " + a.id;
            }
            return null;
        }

        // ------------------------------------------------------------------ zone visibility
        static Vector3[] _visCentres;
        static float[] _playerW; static bool[] _playerDense; static float _nextPlayer = -1f;

        static List<PerceptionConfig.VisibilityZone> VisZones => PerceptionConfig.Instance.visibilityZones;

        static Vector3 VisCentre(List<PerceptionConfig.VisibilityZone> zones, int i)
        {
            if (_visCentres == null || _visCentres.Length != zones.Count)
            {
                _visCentres = new Vector3[zones.Count];
                for (int k = 0; k < zones.Count; k++) { var z = zones[k]; _visCentres[k] = z == null ? Vector3.zero : Anchor(z.anchor, z.center); }
            }
            return _visCentres[i];
        }

        /// <summary>1 inside the zone's radius, smooth to 0 at radius + blend</summary>
        public static float Weight(List<PerceptionConfig.VisibilityZone> zones, int i, Vector3 p)
        {
            var z = zones[i]; if (z == null || z.radius <= 0f) return 0f;
            Vector3 c = VisCentre(zones, i); float dx = p.x - c.x, dz = p.z - c.z, d = Mathf.Sqrt(dx * dx + dz * dz);
            if (d <= z.radius) return 1f;
            if (d >= z.radius + z.blend || z.blend <= 0f) return 0f;
            float t = (d - z.radius) / z.blend; return 1f - t * t * (3f - 2f * t);
        }

        /// <summary>the player's zone weights and "in dense plants" flags, refreshed 4 times a second</summary>
        static void UpdatePlayer(in PlayerSignature.Snapshot s, List<PerceptionConfig.VisibilityZone> zones)
        {
            if (_playerW == null || _playerW.Length != zones.Count) { _playerW = new float[zones.Count]; _playerDense = new bool[zones.Count]; _nextPlayer = -1f; }
            float now = Time.time;
            if (now < _nextPlayer) return;
            _nextPlayer = now + 0.25f;
            for (int i = 0; i < zones.Count; i++)
            {
                _playerW[i] = s.valid ? Weight(zones, i, s.position) : 0f;
                _playerDense[i] = _playerW[i] > 0f && Density(zones, i, s.position, true) >= zones[i].denseThreshold;
            }
        }

        /// <summary>x sight range of a creature at 'creature' looking for the player (1 = no zone)</summary>
        public static float PlayerSightMul(Vector3 creature, in PlayerSignature.Snapshot s)
        {
            var zones = VisZones; if (zones == null || zones.Count == 0 || !s.valid) return 1f;
            UpdatePlayer(s, zones);
            float mul = 1f;
            for (int i = 0; i < zones.Count; i++)
            {
                var z = zones[i]; if (z == null) continue;
                float wp = _playerW[i], w = Mathf.Max(wp, Weight(zones, i, creature));
                if (w <= 0f) continue;
                float m = Mathf.Lerp(1f, z.playerSightMul, w);
                if (s.crouching && _playerDense[i]) m *= 1f - z.denseCrouchCover * wp;
                if (m < mul) mul = m;
            }
            return mul;
        }

        /// <summary>the player is crouched in dense plants inside a visibility zone right now (HUD / tests)</summary>
        public static bool PlayerHiddenInDensePlants
        {
            get
            {
                var zones = VisZones; var s = PlayerSignature.Read;
                if (zones == null || zones.Count == 0 || !s.valid || !s.crouching) return false;
                UpdatePlayer(s, zones);
                for (int i = 0; i < zones.Count; i++) if (_playerDense[i] && _playerW[i] > 0.5f) return true;
                return false;
            }
        }

        /// <summary>x the distances at which two creatures notice each other (a herd and a hunter; 1 = no zone)</summary>
        public static float CreatureSightMul(Vector3 a, Vector3 b)
        {
            var zones = VisZones; if (zones == null || zones.Count == 0) return 1f;
            float mul = 1f;
            for (int i = 0; i < zones.Count; i++)
            {
                var z = zones[i]; if (z == null) continue;
                float w = Mathf.Max(Weight(zones, i, a), Weight(zones, i, b));
                if (w <= 0f) continue;
                float m = Mathf.Lerp(1f, z.creatureSightMul, w);
                if (m < mul) mul = m;
            }
            return mul;
        }

        // ---- dense plants: terrain details (a grid read once per zone), standing trees, bushes
        sealed class Grid { public float x0, z0, cell; public int nx, nz; public float[] v; }
        static Grid[] _grids;

        /// <summary>plant density 0..1 at p inside zone i (terrain details against the zone's own 90th percentile, trees, bushes)</summary>
        public static float Density(List<PerceptionConfig.VisibilityZone> zones, int i, Vector3 p, bool player)
        {
            if (player && BushInteraction.PlayerBush) return 1f;
            float d = 0f;
            var g = GridFor(zones, i);
            if (g != null && g.v != null)
            {
                int x = Mathf.FloorToInt((p.x - g.x0) / g.cell), z = Mathf.FloorToInt((p.z - g.z0) / g.cell);
                if (x >= 0 && z >= 0 && x < g.nx && z < g.nz) d = g.v[z * g.nx + x];
            }
            int trees = CoverMap.TreesNear(p, 3.5f, out _);
            d = Mathf.Max(d, Mathf.Clamp01(trees * 0.3f));
            if (CoverMap.BushesNear(p, 0.5f) > 0) d = Mathf.Max(d, 0.8f);
            return d;
        }

        static Grid GridFor(List<PerceptionConfig.VisibilityZone> zones, int i)
        {
            if (_grids == null || _grids.Length != zones.Count) _grids = new Grid[zones.Count];
            if (_grids[i] != null) return _grids[i];
            var g = new Grid(); _grids[i] = g;
            var t = Terrain.activeTerrain; var z = zones[i];
            if (!t || !t.terrainData || z == null) return g;
            var td = t.terrainData; int res = td.detailResolution, layers = td.detailPrototypes.Length;
            if (res <= 0 || layers == 0) return g;
            Vector3 tp = t.transform.position, sz = td.size, c = VisCentre(zones, i);
            float r = z.radius + z.blend;
            int x0 = Mathf.Clamp(Mathf.FloorToInt((c.x - r - tp.x) / sz.x * res), 0, res - 1), x1 = Mathf.Clamp(Mathf.CeilToInt((c.x + r - tp.x) / sz.x * res), 0, res - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((c.z - r - tp.z) / sz.z * res), 0, res - 1), z1 = Mathf.Clamp(Mathf.CeilToInt((c.z + r - tp.z) / sz.z * res), 0, res - 1);
            int w = x1 - x0 + 1, h = z1 - z0 + 1; if (w <= 0 || h <= 0) return g;
            var sum = new float[w * h];
            for (int l = 0; l < layers; l++)
            {
                var a = td.GetDetailLayer(x0, z0, w, h, l);
                for (int zz = 0; zz < h; zz++) for (int xx = 0; xx < w; xx++) sum[zz * w + xx] += a[zz, xx];
            }
            // normalise against the zone's own 90th percentile of the planted texels (instance count or coverage mode alike)
            var planted = new List<float>(); foreach (var v in sum) if (v > 0f) planted.Add(v);
            float p90 = 1f;
            if (planted.Count > 0) { planted.Sort(); p90 = Mathf.Max(1f, planted[Mathf.Clamp(Mathf.RoundToInt(planted.Count * 0.9f), 0, planted.Count - 1)]); }
            for (int k = 0; k < sum.Length; k++) sum[k] = Mathf.Clamp01(sum[k] / p90);
            g.cell = sz.x / res; g.x0 = tp.x + x0 * g.cell; g.z0 = tp.z + z0 * (sz.z / res); g.nx = w; g.nz = h; g.v = sum;
            return g;
        }

        /// <summary>checks: dense share of zone i (fraction of its core that counts as dense for a crouched player)</summary>
        public static float DenseShare(int i, int samples = 400)
        {
            var zones = VisZones; if (zones == null || i < 0 || i >= zones.Count || zones[i] == null) return 0f;
            var z = zones[i]; Vector3 c = VisCentre(zones, i); int n = 0, dense = 0;
            int side = Mathf.Max(2, Mathf.RoundToInt(Mathf.Sqrt(samples)));
            for (int a = 0; a < side; a++)
                for (int b = 0; b < side; b++)
                {
                    var p = c + new Vector3((a + 0.5f) / side * 2f - 1f, 0f, (b + 0.5f) / side * 2f - 1f) * z.radius;
                    if ((p - c).sqrMagnitude > z.radius * z.radius) continue;
                    n++; if (Density(zones, i, p, false) >= z.denseThreshold) dense++;
                }
            return n > 0 ? dense / (float)n : 0f;
        }
    }
}
