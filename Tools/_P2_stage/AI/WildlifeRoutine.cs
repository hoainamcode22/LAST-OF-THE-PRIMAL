using System;
using UnityEngine;
using PrimalFrontier.Core;
using PrimalFrontier.VFX;
using PrimalFrontier.World;

namespace PrimalFrontier.AI
{
    public enum RoutineKind { Patrol, Visit, Scavenge }
    public enum RoutineStop { Walk, Feed, Observe, Wade }

    /// <summary>
    /// Phase 2 (AI): a scheduled outing of one creature, laid out by WildlifePlan.Routines (PrimalWildlifeBuilder bakes the
    /// points from the zone agents' AI anchors). Patrol / Visit (land creatures): inside its game-hour window, on its days
    /// and with its chance per day, the creature walks its points in order (a pair member follows its leader instead),
    /// does what each stop says (feed with the head down, watch the herds, wade and fish in the shallows) and walks home
    /// again; loop = the patrol goes round again while the window lasts. It only moves the creature while it is calm and
    /// free (DinosaurController.RoutineReady): a hunt, the player, a fire or a fright always come first, and the routine
    /// picks up again afterwards (or goes home when it is stuck). Scavenge (ambient flyers): by day now and then the flyer
    /// glides down next to a body at one of its points (a Carcass there, else the anchor itself), stays a while and takes
    /// off again, never with the player close. Nothing here is saved: after a load the day's schedule starts over from the
    /// clock and the creature is back at home (the spawner rebuilds it from its template).
    /// </summary>
    [DisallowMultipleComponent]
    public class WildlifeRoutine : MonoBehaviour
    {
        public string id = "routine";
        public RoutineKind kind = RoutineKind.Patrol;
        [Tooltip("world positions of the stops, in order (the builder resolves the AI anchors into these)")] public Vector3[] points = new Vector3[0];
        [Tooltip("seconds at each stop (missing = 0: walk on)")] public float[] stay = new float[0];
        [Tooltip("what it does at each stop (missing = Walk)")] public RoutineStop[] actions = new RoutineStop[0];
        [Header("When")]
        [Tooltip("game hours of the outing (from, to; wraps past midnight). Patrol: it goes home when the window ends; Visit: it only has to start inside it")] public Vector2 hours = new Vector2(17.5f, 20.5f);
        [Tooltip("on every Nth game day only (day + dayOffset divisible by N)")] [Min(1)] public int everyDays = 1;
        public int dayOffset;
        [Tooltip("chance per eligible day (fixed per day and routine: no re-rolls)")] [Range(0, 1)] public float chance = 1f;
        [Tooltip("patrol: go round again while the window lasts")] public bool loop;
        [Tooltip("scavenging flyer: landings per window at most")] [Min(1)] public int maxVisits = 3;
        [Header("How")]
        [Tooltip("x walk speed on the legs")] [Range(0.4f, 1.6f)] public float speedMul = 1f;
        [Tooltip("wander radius around a stop while it stays")] [Min(1)] public float stopRoam = 5f;
        [Tooltip("Wade stops: shallow water within this radius counts as ground while it stays")] [Min(1)] public float wadeRadius = 14f;
        [Tooltip("pair: walk behind the creature whose routine has this id instead of the own points")] public string leaderId;
        [Tooltip("place behind the leader (right, up, forward in the leader's frame)")] public Vector3 followOffset = new Vector3(2.5f, 0f, -5f);

        public enum Phase { Waiting, Out, Staying, Back }
        public Phase State { get; private set; } = Phase.Waiting;
        /// <summary>index of the stop it is going to / staying at</summary>
        public int Stop { get; private set; }
        /// <summary>game day of the last outing (-1 = none yet)</summary>
        public int LastDay { get; private set; } = -1;
        public int Outings { get; private set; }
        /// <summary>a routine started (true) or ended (false): tests, journal</summary>
        public static event Action<WildlifeRoutine, bool> Changed;
        public static readonly System.Collections.Generic.List<WildlifeRoutine> All = new System.Collections.Generic.List<WildlifeRoutine>();

        DinosaurController _dc; AmbientCreature _ac; WildlifeRoutine _leader;
        /// <summary>the routine this one follows (pair), found by leaderId; null = none / not here</summary>
        WildlifeRoutine leader
        {
            get
            {
                if (string.IsNullOrEmpty(leaderId)) return null;
                if (_leader && _leader.isActiveAndEnabled) return _leader;
                _leader = null;
                foreach (var r in All) if (r && r != this && r.id == leaderId && r.isActiveAndEnabled) { _leader = r; break; }
                return _leader;
            }
        }
        /// <summary>a pair member: follows another routine</summary>
        public bool Follower => !string.IsNullOrEmpty(leaderId);
        Vector3 _home0; float _radius0; bool _haveHome;
        float _next, _stayUntil, _bestDist, _progressAt, _nextVisit = -1f; int _visits, _visitDay = -1; bool _acted;
        Carcass _atBody;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); if (State != Phase.Waiting) Finish(false); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { All.Clear(); Changed = null; }

        void Start()
        {
            _dc = GetComponent<DinosaurController>(); _ac = _dc ? null : GetComponent<AmbientCreature>();
            _next = Time.time + UnityEngine.Random.Range(0.5f, 2f);
        }

        void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + WildlifeConfig.Instance.routineTick;
            if (_ac) { ScavengeTick(); return; }
            if (!_dc || !_dc.def) return;
            if (!_dc.IsAlive) { if (State != Phase.Waiting) Finish(false); return; }
            LandTick();
        }

        // ------------------------------------------------------------------ schedule
        static int Day { get { var tm = TimeManager.Instance; return tm ? tm.day : 1; } }
        static float Hour { get { var tm = TimeManager.Instance; return tm ? tm.hour : 12f; } }

        public bool InWindow(float h)
        {
            float a = hours.x, b = hours.y;
            return a <= b ? h >= a && h < b : h >= a || h < b;
        }

        /// <summary>does the routine go out on this game day (days, fixed chance roll)</summary>
        public bool DayEligible(int day)
        {
            if (((day + dayOffset) % Mathf.Max(1, everyDays) + Mathf.Max(1, everyDays)) % Mathf.Max(1, everyDays) != 0) return false;
            if (chance >= 1f) return true;
            uint h = 2166136261u; foreach (char c in id) h = (h ^ c) * 16777619u;
            h = (h ^ (uint)day) * 16777619u; h ^= h >> 13; h *= 1274126177u; h ^= h >> 16;
            return (h & 0xFFFF) / 65536f < chance;
        }

        /// <summary>tests / tools: start now, whatever the clock says</summary>
        public void ForceStart() { if (_dc && State == Phase.Waiting) Begin(Day); }

        // ------------------------------------------------------------------ land creatures
        float Reach => WildlifeConfig.Instance.routineReach + (_dc && _dc.def ? _dc.def.bodyRadius : 0f);
        int Count => Follower ? 1 : points != null ? points.Length : 0;
        Vector3 PointAt(int i) => points[Mathf.Clamp(i, 0, points.Length - 1)];
        float StayAt(int i) => stay != null && i < stay.Length ? stay[i] : 0f;
        RoutineStop ActionAt(int i) => actions != null && i < actions.Length ? actions[i] : RoutineStop.Walk;

        void LandTick()
        {
            int day = Day; float h = Hour;
            switch (State)
            {
                case Phase.Waiting:
                    if (Count == 0 || LastDay == day || !InWindow(h) || !DayEligible(day)) return;
                    if (Follower && (!leader || leader.State == Phase.Waiting)) return;       // a follower goes when its leader goes
                    if (!_dc.RoutineReady) return;
                    Begin(day);
                    return;
                case Phase.Out:
                {
                    if (kind == RoutineKind.Patrol && !InWindow(h) && !Follower) { GoBack(); return; }      // a visit, once started, runs its course
                    var ld = leader;
                    if (Follower && (!ld || ld.State == Phase.Waiting || ld.State == Phase.Back)) { GoBack(); return; }
                    Vector3 target = Target();
                    float d = Flat(target - transform.position);
                    if (!Follower && d <= Reach) { Arrive(); return; }
                    if (Follower && d <= Reach) { _progressAt = Time.time; return; }                // keeps its place behind the leader
                    if (!Follower && ActionAt(Stop) == RoutineStop.Wade) _dc.SetWade(target, wadeRadius + 8f, 20f);     // may step into the shallows to get there
                    SetHome(target, _dc.def.bodyRadius + 2f);
                    _dc.RoutineWalk(target, speedMul);
                    Progress(d);
                    return;
                }
                case Phase.Staying:
                {
                    Vector3 p = PointAt(Stop); var act = ActionAt(Stop);
                    if (!_acted && _dc.RoutineReady)
                    {
                        _acted = true;
                        if (act == RoutineStop.Feed || act == RoutineStop.Wade) _dc.RoutineAct(true, Mathf.Min(StayAt(Stop), 40f), p);
                        else if (act == RoutineStop.Observe) _dc.RoutineAct(false, Mathf.Min(StayAt(Stop), 30f), LookPoint(p));
                    }
                    else if (_acted && act == RoutineStop.Wade && _dc.State == DinoState.Eat && UnityEngine.Random.value < 0.25f)
                    {
                        // fishing in the shallows: a strike at the water now and then
                        var head = transform.position + transform.forward * (_dc.def.bodyRadius * 1.6f);
                        if (TrackSigns.IsUnderWater(head)) VfxPool.Instance.Play(VfxId.WaterSplash, head, Vector3.up, null, _dc.def.bodyRadius * 0.8f);
                    }
                    else if (_acted && _dc.RoutineReady && _dc.State == DinoState.Idle && UnityEngine.Random.value < 0.3f) _acted = false;     // feed / look again
                    if (Time.time >= _stayUntil || (kind == RoutineKind.Patrol && !InWindow(h))) Next();
                    return;
                }
                case Phase.Back:
                {
                    float d = Flat(_home0 - transform.position);
                    if (d <= Reach + _radius0 * 0.5f) { Finish(true); return; }
                    SetHome(_home0, _radius0);
                    _dc.RoutineWalk(_home0, speedMul);
                    if (Progress(d)) Finish(false);             // stuck on the way home: its own Return state walks it back
                    return;
                }
            }
        }

        Vector3 Target()
        {
            var ld = leader;
            if (!ld) return points != null && points.Length > 0 ? PointAt(Stop) : transform.position;
            var lt = ld.transform; var o = followOffset;
            Vector3 f = lt.forward; f.y = 0f; if (f.sqrMagnitude < 0.01f) f = Vector3.forward; f.Normalize();
            Vector3 r = Vector3.Cross(Vector3.up, f);
            return lt.position + r * o.x + f * o.z;
        }

        /// <summary>the herd nearest to p (a watching predator looks at it), else a point a little beyond p</summary>
        static Vector3 LookPoint(Vector3 p)
        {
            HerdGroup best = null; float bd = 160f * 160f;
            foreach (var hg in HerdGroup.All) { if (!hg || hg.AliveCount == 0) continue; float q = (hg.Centroid - p).sqrMagnitude; if (q < bd) { bd = q; best = hg; } }
            return best ? best.Centroid + Vector3.up : p + Vector3.forward * 20f;
        }

        void Begin(int day)
        {
            if (!_haveHome) { _home0 = _dc.home != Vector3.zero ? _dc.home : transform.position; _radius0 = _dc.homeRadius; _haveHome = true; }
            LastDay = day; Outings++; Stop = 0; State = Phase.Out; ResetProgress();
            Changed?.Invoke(this, true);
        }

        void Arrive()
        {
            float s = StayAt(Stop);
            if (s <= 0f) { Next(); return; }
            var p = PointAt(Stop);
            State = Phase.Staying; _stayUntil = Time.time + s * UnityEngine.Random.Range(0.8f, 1.2f); _acted = false;
            SetHome(p, stopRoam);
            if (ActionAt(Stop) == RoutineStop.Wade) _dc.SetWade(p, wadeRadius, s * 1.3f + 10f);
            if (ActionAt(Stop) == RoutineStop.Feed) { _atBody = Carcass.NearestBody(p, 12f); if (_atBody) _atBody.ScavengerArrived(); }
        }

        void Next()
        {
            if (_atBody) { _atBody.ScavengerLeft(); _atBody = null; }
            Stop++;
            if (Stop >= Count)
            {
                if (loop && InWindow(Hour)) { Stop = 0; State = Phase.Out; ResetProgress(); return; }
                GoBack(); return;
            }
            State = Phase.Out; ResetProgress();
        }

        void GoBack()
        {
            if (_atBody) { _atBody.ScavengerLeft(); _atBody = null; }
            State = Phase.Back; ResetProgress();
        }

        void Finish(bool arrived)
        {
            if (_atBody) { _atBody.ScavengerLeft(); _atBody = null; }
            if (_dc && _haveHome) { _dc.home = _home0; _dc.homeRadius = _radius0; }
            var was = State; State = Phase.Waiting;
            if (was != Phase.Waiting) Changed?.Invoke(this, false);
        }

        void SetHome(Vector3 p, float r) { if (_dc) { _dc.home = p; _dc.homeRadius = Mathf.Max(2f, r); } }

        void ResetProgress() { _bestDist = float.MaxValue; _progressAt = Time.time; }

        /// <summary>true when it made no progress towards the current point for routineStuckSeconds (skips the point)</summary>
        bool Progress(float d)
        {
            if (d < _bestDist - 1f) { _bestDist = d; _progressAt = Time.time; return false; }
            if (Time.time - _progressAt < WildlifeConfig.Instance.routineStuckSeconds) return false;
            if (State == Phase.Out && !Follower) { Next(); return false; }
            return true;
        }

        // ------------------------------------------------------------------ scavenging flyers
        void ScavengeTick()
        {
            if (!_ac.IsAlive || points == null || points.Length == 0) return;
            var w = WildlifeConfig.Instance; int day = Day;
            var pp = PlayerLocator.Position;
            if (_ac.Visiting)
            {
                // the player walks up: it takes off
                if (pp.HasValue && Flat(pp.Value - transform.position) < w.scavengeShyDistance) _ac.Startle(pp.Value);
                return;
            }
            if (_atBody) { _atBody.ScavengerLeft(); _atBody = null; }
            if (State != Phase.Waiting) { State = Phase.Waiting; Changed?.Invoke(this, false); }
            if (_visitDay != day) { _visitDay = day; _visits = 0; }
            if (!InWindow(Hour) || !DayEligible(day) || _visits >= maxVisits || _ac.Landed) return;
            if (_nextVisit < 0f) { _nextVisit = Time.time + UnityEngine.Random.Range(w.scavengeEvery.x, w.scavengeEvery.y) * 0.5f; return; }
            if (Time.time < _nextVisit) return;
            _nextVisit = Time.time + UnityEngine.Random.Range(w.scavengeEvery.x, w.scavengeEvery.y);
            int i = UnityEngine.Random.Range(0, points.Length);
            Vector3 at = points[i];
            var ter = Terrain.activeTerrain;
            bool perch = ter && at.y > ter.SampleHeight(at) + ter.transform.position.y + 1.5f;         // a snag top / rock rim anchor: land right on it
            var body = perch ? null : Carcass.NearestBody(at, 14f);
            Vector3 spot = at;
            if (!perch)
            {
                // a ground point: by the body when one lies there, a few metres from it
                Vector3 c = body ? body.transform.position : at;
                var off = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(2.5f, 4.5f);
                spot = body ? c + new Vector3(off.x, 0f, off.y) : at;
                if (!HerdGroup.Walkable(spot)) return;
            }
            if (pp.HasValue && Flat(pp.Value - spot) < w.scavengeShyDistance * 1.5f) return;
            float s = UnityEngine.Random.Range(w.scavengeStay.x, w.scavengeStay.y) * (perch ? 1.5f : 1f);
            if (!_ac.VisitGround(spot, s, perch)) return;
            _visits++; LastDay = day; Outings++; Stop = i; State = Phase.Staying;
            if (body) { _atBody = body; body.ScavengerArrived(); }
            Changed?.Invoke(this, true);
        }

        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (points == null) return;
            Gizmos.color = kind == RoutineKind.Scavenge ? new Color(0.9f, 0.8f, 0.3f) : new Color(1f, 0.5f, 0.1f);
            for (int i = 0; i < points.Length; i++)
            {
                Gizmos.DrawWireSphere(points[i], 1.5f);
                if (i > 0) Gizmos.DrawLine(points[i - 1], points[i]);
            }
        }
#endif
    }
}
