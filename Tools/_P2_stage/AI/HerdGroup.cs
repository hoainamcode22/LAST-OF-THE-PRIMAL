using System;
using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Core;
using PrimalFrontier.World;

namespace PrimalFrontier.AI
{
    public enum HerdActivity { Graze, Travel, Drink, Rest, Sleep, Migrate }

    /// <summary>where a herd spends its day on one side of its range (zero = derived from the grazing place)</summary>
    [Serializable]
    public struct HerdPlaces
    {
        [Tooltip("morning grazing")] public Vector3 graze;
        [Tooltip("late afternoon grazing (zero = near the morning place)")] public Vector3 graze2;
        [Tooltip("midday rest in the heat, near shade (zero = found near the grazing place)")] public Vector3 rest;
        [Tooltip("safer ground for the night: open, away from cover and hunters (zero = found)")] public Vector3 night;
        public bool Valid => graze != Vector3.zero;
    }

    /// <summary>
    /// A herd of 3-12 herbivores (directive 31-36, 45). The members are the DinosaurControllers under this object; the herd
    /// only moves a shared centre (the anchor) through its day by world time (TimeManager phases): morning grazing, a walk
    /// to water around noon, drinking, a rest in the heat, grazing again, safer open ground at dusk, sleep at night. Each
    /// member keeps its own loose place around the anchor (re-picked now and then), its own walking speed and idle timing,
    /// so there is never a formation. Alarms spread through the herd with a delay by distance; a predator close to the
    /// herd is watched, walked away from, or fled. The migration herd also follows a route (MigrationDirector) and lives
    /// on two sides of the valley. Far herds tick slowly (their members are in the far AI tiers anyway).
    /// </summary>
    public class HerdGroup : MonoBehaviour
    {
        public DinosaurDefinition species;
        [Tooltip("name used in logs / the journal")] public string label = "herd";
        public HerdPlaces home;
        [Tooltip("the far side of its range (migration herd only)")] public HerdPlaces away;
        [Tooltip("grazing spread radius (0 = from the config and the member count)")] public float spread;

        public static readonly List<HerdGroup> All = new List<HerdGroup>();
        public readonly List<DinosaurController> Members = new List<DinosaurController>(16);
        public HerdActivity Activity { get; private set; } = HerdActivity.Graze;
        public Vector3 Anchor { get; private set; }
        public Vector3 Heading { get; private set; } = Vector3.forward;
        public Vector3 Centroid { get; private set; }
        /// <summary>0 = home side, 1 = away side (migration herd)</summary>
        public int Side { get; private set; }
        public bool AnchorMoving { get; private set; }
        public float AnchorSpeed { get; private set; }
        public bool OnRoute => _route != null;
        public int RouteIndex => _routeIdx;
        public int RouteLength => _route != null ? _route.Length : 0;
        public bool RoutePaused => _route != null && Time.time < _pauseUntil;
        /// <summary>drink session number: a member that drank in this session is done</summary>
        public int DrinkSession { get; private set; }
        public Vector3 WaterPoint => _water;
        public HerdPlaces Places => Side == 1 && away.Valid ? away : home;
        public int AliveCount { get { int n = 0; foreach (var m in Members) if (m && m.IsAlive) n++; return n; } }
        /// <summary>the herd is on edge (a hunter or the player close): members keep looking up</summary>
        public bool Wary => Stimuli.Now < _waryUntil;

        public float Spread
        {
            get
            {
                float s = spread > 0f ? spread : WildlifeConfig.Instance.grazeSpread + WildlifeConfig.Instance.grazePerMember * Mathf.Max(3, Members.Count);
                var tm = TimeManager.Instance;
                if (Activity == HerdActivity.Sleep || (tm && tm.NightFactor > 0.6f))
                    s *= WildlifeConfig.Instance.nightSpread * (1f - (species ? species.NightFear : 0.5f) * 0.4f);
                if (_route == null) s *= WildlifeWeather.HerdSpreadMul;              // rain / storm: the herd bunches up
                return s;
            }
        }
        public Vector2 ColumnSize { get { var c = WildlifeConfig.Instance; int n = Mathf.Max(3, Members.Count); return new Vector2(c.travelSpread.x + 0.5f * n, c.travelSpread.y + 3.5f * n); } }

        // day plan
        Vector3 _target, _water; HerdActivity _arrive; bool _hasWater; int _drankDay = -1; float _drinkStart;
        Vector3 _shift; float _shiftUntil, _waryUntil = -99f;
        float _nextTick, _nextScan, _nextThreat, _nextSight, _lastTick; int _sightedDay = int.MinValue;
        Vector3 _lastFlatten;
        // route
        Vector3[] _route; int _routeIdx, _pauseIdx = -1; float _pauseSeconds, _pauseUntil = -1f; bool _paused; Action _routeDone;
        bool _started;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); foreach (var m in Members) if (m && m.Herd == this) m.Herd = null; }

        void Start()
        {
            Scan();
            Centroid = ComputeCentroid(out _);
            Anchor = Members.Count > 0 ? Centroid : transform.position;
            Side = away.Valid && (Centroid - away.graze).sqrMagnitude < (Centroid - home.graze).sqrMagnitude ? 1 : 0;
            ResolvePlaces();
            _lastFlatten = Anchor; _lastTick = Time.time;
            _started = true;
        }

        // ------------------------------------------------------------------ members
        readonly List<DinosaurController> _scan = new List<DinosaurController>(16);
        public void Scan()
        {
            int kept = 0, before = Members.Count;
            for (int i = Members.Count - 1; i >= 0; i--)
            {
                var m = Members[i];
                if (!m || !m.IsAlive || m.Loner || !m.isActiveAndEnabled || !m.transform.IsChildOf(transform)) { if (m && m.Herd == this) m.Herd = null; Members.RemoveAt(i); }
            }
            kept = Members.Count;
            int added = 0;
            GetComponentsInChildren(false, _scan);                      // no allocation after the first scan
            foreach (var d in _scan)
            {
                if (!d || !d.IsAlive || d.Loner || (species && d.def != species) || Members.Contains(d)) continue;
                if (!species) species = d.def;
                Members.Add(d); d.Herd = this; AssignSlot(d, true); added++;
            }
            // every member replaced at once (new game, a loaded save re-made the creatures): start the herd again where they stand
            if (_started && before > 0 && kept == 0 && added > 0) Reinit();
        }

        void Reinit()
        {
            _route = null; _paused = false; _pauseUntil = -1f; _routeDone = null; _shiftUntil = -1f; _drankDay = -1;
            Centroid = ComputeCentroid(out _); Anchor = Centroid; _lastFlatten = Anchor;
            Side = away.Valid && (Centroid - away.graze).sqrMagnitude < (Centroid - home.graze).sqrMagnitude ? 1 : 0;
            Activity = HerdActivity.Graze; _target = Anchor;
            ResolvePlaces();
        }

        /// <summary>a loose place in the herd (unit disc, kept apart from the others), an own walking pace and idle timing</summary>
        internal void AssignSlot(DinosaurController d, bool fresh)
        {
            var c = WildlifeConfig.Instance;
            Vector2 best = UnityEngine.Random.insideUnitCircle; float bestD = -1f;
            for (int k = 0; k < 14; k++)
            {
                var cand = UnityEngine.Random.insideUnitCircle;
                float near = 9f;
                foreach (var o in Members) if (o && o != d) near = Mathf.Min(near, (o.HerdSlot - cand).sqrMagnitude);
                if (near > bestD) { bestD = near; best = cand; }
            }
            d.HerdSlot = best;
            if (fresh) { d.HerdSpeedMul = UnityEngine.Random.Range(c.speedSpread.x, c.speedSpread.y); d.HerdIdleMul = UnityEngine.Random.Range(c.idleSpread.x, c.idleSpread.y); }
            d.HerdReslotAt = Time.time + UnityEngine.Random.Range(c.reslotSeconds.x, c.reslotSeconds.y);
        }

        Vector3 ComputeCentroid(out int n)
        {
            Vector3 s = Vector3.zero; n = 0;
            foreach (var m in Members) { if (!m || !m.IsAlive || m.State == DinoState.Flee) continue; s += m.transform.position; n++; }
            if (n == 0) foreach (var m in Members) { if (!m || !m.IsAlive) continue; s += m.transform.position; n++; }
            return n > 0 ? s / n : (Members.Count > 0 && Members[0] ? Members[0].transform.position : transform.position);
        }

        /// <summary>where this member belongs right now (its loose place in the grazing circle or the travelling column)</summary>
        public Vector3 SlotPosition(DinosaurController d)
        {
            Vector2 s = d.HerdSlot;
            if (Activity == HerdActivity.Travel || Activity == HerdActivity.Migrate)
            {
                var size = ColumnSize; Vector3 f = Heading, r = Vector3.Cross(Vector3.up, f);
                return Snap(Anchor + r * (s.x * size.x * 0.5f) + f * (s.y * size.y * 0.5f));
            }
            float sp = Spread;
            return Snap(Anchor + new Vector3(s.x, 0f, s.y) * sp);
        }

        /// <summary>a grazing spot near its own place</summary>
        public Vector3 GrazePoint(DinosaurController d)
        {
            var q = SlotPosition(d); var r = UnityEngine.Random.insideUnitCircle * 3.5f;
            return Snap(q + new Vector3(r.x, 0f, r.y));
        }

        /// <summary>the walking speed that keeps this member with the herd (stragglers trot, the front ones dawdle)</summary>
        public float SpeedFor(DinosaurController d, Vector3 slot)
        {
            var c = WildlifeConfig.Instance; float walk = d.def.walkSpeed * d.HerdSpeedMul;
            Vector3 off = d.transform.position - slot; off.y = 0f;
            float along = Vector3.Dot(off, Heading), dist = off.magnitude;
            float sp = Activity == HerdActivity.Travel || Activity == HerdActivity.Migrate ? Mathf.Max(ColumnSize.x, ColumnSize.y) * 0.5f : Spread;
            if (dist > sp * c.catchUp && along < 0f) return walk * c.catchUpSpeed;
            if (AnchorMoving && along > 3f) return Mathf.Min(walk, AnchorSpeed * 0.7f);
            if (AnchorMoving) return Mathf.Clamp(AnchorSpeed * 1.08f + dist * 0.05f, walk * 0.5f, walk * 1.25f);
            return walk;
        }

        /// <summary>a drinking place on the bank for this member (spread along the shore)</summary>
        public bool DrinkSpotFor(DinosaurController d, out Vector3 spot)
        {
            spot = _water;
            if (!_hasWater) return false;
            Vector3 from = Anchor + new Vector3(d.HerdSlot.x, 0f, d.HerdSlot.y) * Spread * 0.8f;
            if (DrinkSpots.Nearest(from, 45f, out var p)) spot = p;
            return true;
        }

        static Vector3 Snap(Vector3 p)
        {
            var t = Terrain.activeTerrain; if (!t) return p;
            p.y = t.SampleHeight(p) + t.transform.position.y; return p;
        }

        // ------------------------------------------------------------------ tick
        void Update()
        {
            if (!_started) return;
            float now = Time.time;
            if (now >= _nextScan) { _nextScan = now + 3f; Scan(); }
            if (Members.Count == 0) return;
            // near herds think twice a second, far ones every 1.5 s (their members are in the slow AI tiers)
            var pp = PlayerLocator.Position;
            float pd = pp.HasValue ? Flat(pp.Value - Centroid) : 999f;
            if (now < _nextTick) { MoveAnchor(now - _lastTick); _lastTick = now; return; }
            _nextTick = now + (pd < 200f ? 0.5f : 1.5f);
            MoveAnchor(now - _lastTick); _lastTick = now;
            Centroid = ComputeCentroid(out _);
            foreach (var m in Members) if (m && Time.time >= m.HerdReslotAt) AssignSlot(m, false);
            if (_route != null) RouteTick(now); else Plan();
            if (now >= _nextThreat) { _nextThreat = now + 1f; Threats(); }
            if (pp.HasValue && pd < WildlifeConfig.Instance.sightedRange + Spread && now >= _nextSight) { _nextSight = now + 0.5f; CheckSighted(); }
            TrampleTrail();
        }

        void MoveAnchor(float dt)
        {
            if (dt <= 0f) return;
            Vector3 goal = _route != null ? _route[Mathf.Clamp(_routeIdx, 0, _route.Length - 1)] : _target + CurrentShift;
            Vector3 d = goal - Anchor; d.y = 0f; float dist = d.magnitude;
            bool travel = (_route != null && !RoutePaused) || (_route == null && Activity == HerdActivity.Travel);
            if (!travel || dist < 0.3f) { AnchorMoving = false; AnchorSpeed = 0f; return; }
            var c = WildlifeConfig.Instance;
            float slowest = float.MaxValue;
            foreach (var m in Members) if (m && m.IsAlive && m.def) slowest = Mathf.Min(slowest, m.def.walkSpeed * m.HerdSpeedMul);
            if (slowest == float.MaxValue) slowest = species ? species.walkSpeed : 1.5f;
            float speed = slowest * c.travelSpeed * (_route != null ? c.migrationSpeed : 1f);
            // wait for the herd: the centre never runs away from the animals
            Vector3 lag = Anchor - Centroid; lag.y = 0f;
            float half = Mathf.Max(ColumnSize.x, ColumnSize.y) * 0.5f;
            if (Vector3.Dot(lag, d / Mathf.Max(0.01f, dist)) > half * c.anchorWait) speed = 0f;
            int fleeing = 0; foreach (var m in Members) if (m && m.State == DinoState.Flee) fleeing++;
            if (fleeing * 2 > Members.Count) speed = 0f;
            AnchorSpeed = speed; AnchorMoving = speed > 0.01f;
            if (!AnchorMoving) return;
            Heading = Vector3.Slerp(Heading, d / Mathf.Max(0.01f, dist), Mathf.Clamp01(dt * 1.2f)).normalized;
            Anchor = Snap(Anchor + d / dist * Mathf.Min(dist, speed * dt));
        }

        Vector3 CurrentShift => Stimuli.Now < _shiftUntil ? _shift : Vector3.zero;

        // ------------------------------------------------------------------ the herd day
        void ResolvePlaces()
        {
            var p = Places;
            if (!p.Valid) { p.graze = Anchor; }
            if (p.graze2 == Vector3.zero) p.graze2 = p.graze + new Vector3(12f, 0f, -9f);
            if (p.rest == Vector3.zero) p.rest = ShadeNear(p.graze);
            if (p.night == Vector3.zero) p.night = SaferGround(p.graze);
            if (Side == 1 && away.Valid) away = p; else home = p;
            _hasWater = DrinkSpots.Nearest(p.graze, WildlifeConfig.Instance.waterSearch, out _water);
            if (_hasWater)
            {
                // stand on the bank, a little back from the water towards the grazing place
                Vector3 back = p.graze - _water; back.y = 0f;
                if (back.sqrMagnitude > 1f) _waterStand = Snap(_water + back.normalized * Mathf.Min(8f, back.magnitude * 0.5f)); else _waterStand = _water;
            }
        }
        Vector3 _waterStand;

        /// <summary>a spot within 70 m of p with trees around (shade for the midday rest), not in the trees themselves</summary>
        internal static Vector3 ShadeNear(Vector3 p)
        {
            Vector3 best = p; int bestN = -1;
            for (int i = 0; i < 16; i++)
            {
                float a = i * 2.39996f, r = 12f + (i % 4) * 16f;
                var q = p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                if (!Walkable(q)) continue;
                int n = CoverMap.TreesNear(q, 14f, out _), inside = CoverMap.TreesNear(q, 4f, out _);
                if (inside > 0) continue;
                if (n > bestN) { bestN = n; best = q; }
            }
            return Snap(best);
        }

        /// <summary>open ground near p, away from cover and from the predators' homes</summary>
        static Vector3 SaferGround(Vector3 p)
        {
            Vector3 best = p; float bestS = float.MaxValue;
            for (int i = 0; i < 16; i++)
            {
                float a = i * 2.39996f, r = i == 0 ? 0f : 8f + (i % 4) * 12f;
                var q = p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                if (!Walkable(q)) continue;
                float s = CoverMap.TreesNear(q, 22f, out _) * 2f;
                foreach (var d in DinosaurController.All)
                    if (d && d.def && d.def.IsPredator) s += Mathf.Max(0f, 90f - Flat(d.home - q)) * 0.15f;
                if (s < bestS) { bestS = s; best = q; }
            }
            return Snap(best);
        }

        internal static bool Walkable(Vector3 q)
        {
            var t = Terrain.activeTerrain; if (!t || !t.terrainData) return true;
            var tp = t.transform.position; var sz = t.terrainData.size;
            float u = (q.x - tp.x) / sz.x, v = (q.z - tp.z) / sz.z;
            if (u < 0.02f || v < 0.02f || u > 0.98f || v > 0.98f) return false;
            if (t.SampleHeight(q) + tp.y < 0.8f) return false;
            return t.terrainData.GetSteepness(u, v) < 26f;
        }

        void Plan()
        {
            var tm = TimeManager.Instance;
            var c = WildlifeConfig.Instance;
            var p = Places;
            TimeManager.Phase phase = tm ? tm.CurrentPhase : TimeManager.Phase.Morning;
            float hour = tm ? tm.hour : 9f; int day = tm ? tm.day : 1;
            Vector3 place; HerdActivity arrive;
            switch (phase)
            {
                case TimeManager.Phase.Dawn: place = p.night; arrive = HerdActivity.Graze; break;
                case TimeManager.Phase.Morning: place = p.graze; arrive = HerdActivity.Graze; break;
                case TimeManager.Phase.Noon:
                    if (_hasWater && _drankDay != day) { place = _waterStand; arrive = HerdActivity.Drink; }
                    else { place = p.rest; arrive = HerdActivity.Rest; }
                    break;
                case TimeManager.Phase.Afternoon:
                    if (_hasWater && _drankDay != day && hour < (tm ? tm.afternoonStart : 14f) + 0.5f) { place = _waterStand; arrive = HerdActivity.Drink; }
                    else if (hour < (tm ? tm.afternoonStart : 14f) + c.afternoonRestHours) { place = p.rest; arrive = HerdActivity.Rest; }
                    else { place = p.graze2; arrive = HerdActivity.Graze; }
                    break;
                case TimeManager.Phase.Dusk: place = p.night; arrive = HerdActivity.Graze; break;
                default: place = p.night; arrive = HerdActivity.Sleep; break;
            }
            WildlifeWeather.Refresh();
            if (WildlifeWeather.Shelter)
            {
                // heavy rain / storm: the herd goes to its trees (the midday rest place) and waits it out together, lying down at night
                place = p.rest; arrive = arrive == HerdActivity.Sleep ? HerdActivity.Sleep : HerdActivity.Rest;
            }
            else if (WildlifeWeather.Wet && arrive == HerdActivity.Graze) place = Vector3.Lerp(place, p.rest, c.rainGrazeTowardCover);     // rain: grazes nearer cover
            SetPlan(place, arrive, day, c);
        }

        void SetPlan(Vector3 place, HerdActivity arrive, int day, WildlifeConfig c)
        {
            _target = place; _arrive = arrive;
            Vector3 goal = place + CurrentShift;
            float d = Flat(goal - Anchor);
            if (d > 3f) { if (Activity != HerdActivity.Travel) Activity = HerdActivity.Travel; return; }
            if (Activity != arrive)
            {
                Activity = arrive;
                if (arrive == HerdActivity.Drink) { DrinkSession++; _drinkStart = Time.time; }
            }
            if (Activity == HerdActivity.Drink)
            {
                // done when every member drank (or it took too long)
                int left = 0; foreach (var m in Members) if (m && m.IsAlive && m.HerdDrankSession != DrinkSession) left++;
                if (left == 0 || Time.time - _drinkStart > 110f) _drankDay = day;
            }
        }

        // ------------------------------------------------------------------ alarms
        /// <summary>a member bolted: the others follow, each after a delay by its distance (the alarm runs through the herd)</summary>
        public void Alarm(DinosaurController source, Vector3 from)
        {
            var c = WildlifeConfig.Instance;
            Vector3 at = source ? source.transform.position : Centroid;
            foreach (var m in Members)
            {
                if (!m || m == source || !m.IsAlive || m.State == DinoState.Flee) continue;
                float delay = Flat(m.transform.position - at) / Mathf.Max(1f, c.alarmSpeed) + UnityEngine.Random.Range(c.alarmDelay.x, c.alarmDelay.y);
                m.FleeLater(from, delay);
            }
            Disturb(from, 2f);
        }

        /// <summary>something bothers the herd (the player too close, a hunter nearby): the herd drifts away from it together</summary>
        public void Disturb(Vector3 from, float strength = 1f)
        {
            Vector3 away = Centroid - from; away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = -Heading;
            float dist = Mathf.Lerp(16f, 30f, Mathf.Clamp01(strength - 0.5f));
            Vector3 s = away.normalized * dist;
            var goal = _target + s;
            if (!Walkable(goal)) { s = Quaternion.Euler(0f, 60f, 0f) * s; if (!Walkable(_target + s)) s = Quaternion.Euler(0f, -120f, 0f) * s; }
            _shift = Stimuli.Now < _shiftUntil ? Vector3.ClampMagnitude(_shift + s * 0.6f, 45f) : s;
            _shiftUntil = Stimuli.Now + 60f + 40f * strength;
            _waryUntil = Stimuli.Now + 20f;
        }

        void Threats()
        {
            var c = WildlifeConfig.Instance;
            DinosaurController best = null; float bd = float.MaxValue;
            foreach (var d in DinosaurController.All)
            {
                if (!d || !d.def || !d.IsAlive || !d.def.IsPredator || d.Sleeping) continue;
                float q = Flat(d.transform.position - Centroid);
                if (q < bd) { bd = q; best = d; }
            }
            if (!best) return;
            float edge = Mathf.Max(0f, bd - Spread * 0.5f);         // distance to the herd's edge
            Vector3 pos = best.transform.position;
            // Phase 2: in dense vegetation (zone visibility) a hunter is noticed later
            float vis = WildlifeZones.CreatureSightMul(pos, Centroid);
            float watch = c.predatorWatch * vis, avoid = c.predatorAvoid * vis, flee = c.predatorFlee * Mathf.Max(0.6f, vis);
            bool rushing = best.CurrentSpeed > best.def.walkSpeed * 1.5f && edge < watch && Vector3.Dot(Centroid - pos, best.transform.forward) > 0f;
            if (edge < flee || rushing)
            {
                DinosaurController first = null; float fd = float.MaxValue;
                foreach (var m in Members) { if (!m || !m.IsAlive) continue; float q = Flat(m.transform.position - pos); if (q < fd) { fd = q; first = m; } }
                if (first && first.State != DinoState.Flee) { first.FleeFrom(pos, true); }
                return;
            }
            if (edge < watch)
            {
                _waryUntil = Stimuli.Now + 6f;
                foreach (var m in Members) if (m && m.IsAlive) m.WatchThreat(pos, 2.5f);
                if (edge < avoid && _route == null) Disturb(pos, 1.2f);
            }
        }

        // ------------------------------------------------------------------ sighting
        void CheckSighted()
        {
            var tm = TimeManager.Instance; int day = tm ? tm.day : 1;
            if (_sightedDay == day) return;
            var cam = Camera.main; if (!cam || !species) return;
            var c = WildlifeConfig.Instance;
            Vector3 cp = cam.transform.position, cf = cam.transform.forward;
            int inView = 0;
            foreach (var m in Members)
            {
                if (!m || !m.IsAlive) continue;
                Vector3 to = m.transform.position + Vector3.up * m.def.bodyRadius - cp; float d = to.magnitude;
                if (d > c.sightedRange || d < 0.5f) continue;
                if (Vector3.Dot(cf, to / d) < 0.8f) continue;              // within about 37 degrees of the view centre
                inView++;
            }
            int need = Mathf.Min(c.sightedMembers, Mathf.Max(1, AliveCount));
            if (inView < need) return;
            Vector3 target = Centroid + Vector3.up * 2f;
            if (Physics.Linecast(cp, target, out var hit, TerrainMask, QueryTriggerInteraction.Ignore)) return;         // a hill in the way
            _sightedDay = day;
            GameEvents.Raise(GameEventType.HerdSighted, species.id, inView, Centroid);
        }
        static int _terrainMask = -1;
        internal static int TerrainMask { get { if (_terrainMask == -1) { int l = LayerMask.NameToLayer("Terrain"); _terrainMask = l >= 0 ? (1 << l) | 1 : 1; } return _terrainMask; } }

        // ------------------------------------------------------------------ signs
        void TrampleTrail()
        {
            if (!AnchorMoving || Members.Count < 3 || !species) return;
            var c = WildlifeConfig.Instance;
            if (Flat(Anchor - _lastFlatten) < c.flattenEvery) return;
            _lastFlatten = Anchor;
            Vector3 r = Vector3.Cross(Vector3.up, Heading);
            float yaw = Mathf.Atan2(Heading.x, Heading.z) * Mathf.Rad2Deg;
            float w = ColumnSize.x * 0.4f;
            TrackSigns.Flatten(species, Centroid + r * UnityEngine.Random.Range(-w, w), yaw, UnityEngine.Random.Range(1.8f, 2.8f));
            TrackSigns.Flatten(species, Centroid + r * UnityEngine.Random.Range(-w, w) - Heading * 4f, yaw, UnityEngine.Random.Range(1.6f, 2.4f));
        }

        /// <summary>old dung spots for the start of the game (graze, second graze, rest, night places)</summary>
        public Vector3 SeedPoint(int k)
        {
            var p = Places;
            switch (k % 4) { case 0: return p.graze; case 1: return p.graze2 != Vector3.zero ? p.graze2 : p.graze; case 2: return p.rest != Vector3.zero ? p.rest : p.graze; default: return p.night != Vector3.zero ? p.night : p.graze; }
        }

        // ------------------------------------------------------------------ route (migration)
        /// <summary>walk the route (points in walking order); pause at pauseIndex for pauseSeconds (drinking at the ford)</summary>
        public void BeginRoute(Vector3[] points, int pauseIndex, float pauseSeconds, Action done)
        {
            if (points == null || points.Length < 2) { done?.Invoke(); return; }
            _route = points; _pauseIdx = pauseIndex; _pauseSeconds = pauseSeconds; _paused = false; _pauseUntil = -1f; _routeDone = done;
            // start at the route point nearest the herd, heading for the one after it
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < points.Length; i++) { float q = Flat(points[i] - Anchor); if (q < bd) { bd = q; best = i; } }
            _routeIdx = Mathf.Min(best + (bd < 12f ? 1 : 0), points.Length - 1);
            Activity = HerdActivity.Migrate; _shiftUntil = -1f;
            Vector3 f = points[_routeIdx] - Anchor; f.y = 0f; if (f.sqrMagnitude > 0.01f) Heading = f.normalized;
        }

        void RouteTick(float now)
        {
            if (_paused && now >= _pauseUntil) { _paused = false; Activity = HerdActivity.Migrate; _routeIdx++; }
            if (_paused) return;
            if (_routeIdx >= _route.Length) { EndRoute(); return; }
            if (Flat(_route[_routeIdx] - Anchor) < 2.5f)
            {
                if (_routeIdx == _pauseIdx && _pauseSeconds > 0f)
                {
                    _paused = true; _pauseUntil = now + _pauseSeconds; Activity = HerdActivity.Drink; DrinkSession++; _drinkStart = now;
                    return;
                }
                _routeIdx++;
                if (_routeIdx >= _route.Length) { EndRoute(); return; }
            }
            if (Activity != HerdActivity.Migrate) Activity = HerdActivity.Migrate;
        }

        void EndRoute()
        {
            _route = null; _paused = false; _pauseUntil = -1f;
            if (away.Valid) Side = 1 - Side;
            ResolvePlaces();
            Activity = HerdActivity.Graze; _target = Anchor;
            var d = _routeDone; _routeDone = null; d?.Invoke();
        }

        /// <summary>the migration was missed (slept through, loaded later): the herd is put on the other side at once</summary>
        public void Relocate(int side)
        {
            if (!away.Valid) return;
            Side = Mathf.Clamp(side, 0, 1); _route = null;
            ResolvePlaces();
            Anchor = Places.graze; Centroid = Anchor; Activity = HerdActivity.Graze; _target = Anchor; _shiftUntil = -1f;
            foreach (var m in Members) if (m && m.IsAlive) m.TeleportTo(SlotPosition(m), UnityEngine.Random.Range(0f, 360f));
        }

        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            var p = home;
            Gizmos.color = new Color(0.4f, 1f, 0.5f, 0.9f);
            if (p.graze != Vector3.zero) DinosaurSpawner.DrawCircle(p.graze, 12f);
            Gizmos.color = new Color(1f, 0.9f, 0.3f, 0.9f); if (p.rest != Vector3.zero) DinosaurSpawner.DrawCircle(p.rest, 6f);
            Gizmos.color = new Color(0.4f, 0.6f, 1f, 0.9f); if (p.night != Vector3.zero) DinosaurSpawner.DrawCircle(p.night, 8f);
            if (away.Valid) { Gizmos.color = new Color(0.4f, 1f, 0.5f, 0.5f); DinosaurSpawner.DrawCircle(away.graze, 12f); }
            if (Application.isPlaying) { Gizmos.color = Color.white; Gizmos.DrawWireSphere(Anchor + Vector3.up, 1f); Gizmos.DrawLine(Anchor + Vector3.up, Anchor + Vector3.up + Heading * 6f); }
        }
#endif
    }
}
