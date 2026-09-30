using UnityEngine;
using PrimalFrontier.Core;

namespace PrimalFrontier.AI
{
    /// <summary>
    /// Island-wide rules for predators hunting herbivores (Phase 1, AI). A hunt is rare: at most one at a time on the whole
    /// island, a cooldown after it ends (WildlifeConfig.huntCooldownMinutes after a kill, huntFailCooldownMinutes after a
    /// miss, huntFirstDelayMinutes after a new game or a load), never on a herd at or below huntMinHerd (so a herd is never
    /// wiped out), never on the migration herd while it is on its route, and by day never inside the player's safe start
    /// area (huntSafeStartRadius around GameManager.spawnPoint). The hunt itself (stalk a straggler, charge, strike, eat,
    /// rest) is run by the hunter's DinosaurController; this class only allows it, picks the prey and keeps count.
    /// Transient: nothing is saved; a respawn or load simply ends any hunt (the clock going back restarts the delay).
    /// </summary>
    public static class HuntDirector
    {
        static DinosaurController _hunter, _prey;
        static double _nextAllowed = double.NaN, _lastClock = double.NaN;
        public static DinosaurController Hunter => Active ? _hunter : null;
        public static DinosaurController Prey => Active ? _prey : null;
        /// <summary>a hunt is running (hunter and prey alive, the hunter still after that prey)</summary>
        public static bool Active => _hunter && _prey && _hunter.IsAlive && _prey.IsAlive && _hunter.HuntTarget == _prey;
        /// <summary>counts since the domain loaded (tests / report)</summary>
        public static int Started { get; private set; }
        public static int Kills { get; private set; }
        public static int Failed { get; private set; }
        /// <summary>game clock seconds until a new hunt may start (0 = now)</summary>
        public static double CooldownLeft { get { Tick(); return double.IsNaN(_nextAllowed) ? 0 : System.Math.Max(0, _nextAllowed - GameClock.Now); } }

        static WildlifeConfig W => WildlifeConfig.Instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _hunter = _prey = null; _nextAllowed = _lastClock = double.NaN; Started = Kills = Failed = 0; }

        /// <summary>tests: no hunt running, cooldown over (delaySeconds of game clock from now)</summary>
        public static void ResetForTests(double delaySeconds = 0)
        {
            _hunter = _prey = null; _lastClock = GameClock.Now; _nextAllowed = GameClock.Now + delaySeconds;
        }

        static void Tick()
        {
            double now = GameClock.Now;
            // new game / load: the clock jumped back, or it is the first look: hunts wait a while
            if (double.IsNaN(_nextAllowed) || double.IsNaN(_lastClock) || now < _lastClock - 1.0)
            {
                _hunter = _prey = null;
                _nextAllowed = now + W.huntFirstDelayMinutes * 60.0;
            }
            _lastClock = now;
            // the hunter or the prey went away (respawned, killed by the player, gave up without telling)
            if (!ReferenceEquals(_hunter, null) && !Active)
            {
                _hunter = _prey = null; Failed++;
                _nextAllowed = System.Math.Max(_nextAllowed, now + W.huntFailCooldownMinutes * 60.0);
            }
        }

        /// <summary>no hunt running and the cooldown is over</summary>
        public static bool CanStart()
        {
            Tick();
            return ReferenceEquals(_hunter, null) && GameClock.Now >= _nextAllowed;
        }

        public static bool Begin(DinosaurController hunter, DinosaurController prey)
        {
            if (!hunter || !prey || !CanStart()) return false;
            _hunter = hunter; _prey = prey; Started++;
            GameEvents.Raise(GameEventType.PredatorHunt, hunter.def ? hunter.def.id : hunter.name, 1, prey.transform.position);
            return true;
        }

        /// <summary>the hunter says the hunt is over (kill = the prey was brought down)</summary>
        public static void End(DinosaurController hunter, bool kill, Vector3 at)
        {
            if (ReferenceEquals(_hunter, null) || _hunter != hunter) return;
            _hunter = _prey = null;
            var w = W;
            double cd = (kill ? w.huntCooldownMinutes : w.huntFailCooldownMinutes) * 60.0 * Random.Range(0.8f, 1.25f);
            _nextAllowed = GameClock.Now + cd; _lastClock = GameClock.Now;
            if (kill) Kills++; else Failed++;
            GameEvents.Raise(GameEventType.PredatorHunt, hunter && hunter.def ? hunter.def.id : "predator", kill ? 2 : 3, at);
        }

        /// <summary>where the player starts (safe by day), if known</summary>
        public static bool SafeStart(out Vector3 p)
        {
            var gm = GameManager.Instance;
            if (gm && gm.spawnPoint) { p = gm.spawnPoint.position; return true; }
            p = default; return false;
        }

        static bool IsDay { get { var tm = TimeManager.Instance; return !tm || !tm.IsNight; } }

        /// <summary>p is inside the player's safe start area and it is day (no hunting there)</summary>
        public static bool InSafeArea(Vector3 p)
        {
            if (!IsDay || !SafeStart(out var s)) return false;
            float r = W.huntSafeStartRadius; p.y = 0f; s.y = 0f;
            return (p - s).sqrMagnitude < r * r;
        }

        /// <summary>how many of the prey's group are alive (its herd, or its species for a loner)</summary>
        public static int GroupAlive(DinosaurController d)
        {
            if (!d) return 0;
            if (d.Herd) return d.Herd.AliveCount;
            int n = 0; var all = DinosaurController.All;
            for (int i = 0; i < all.Count; i++) { var o = all[i]; if (o && o.def == d.def && o.IsAlive) n++; }
            return n;
        }

        /// <summary>the prey may be hunted at all (species, group size, route, safe area)</summary>
        public static bool Allowed(in HuntProfile hp, DinosaurController d)
        {
            if (!d || !d.def || !d.IsAlive || !d.def.IsHerbivore || !hp.PreysOn(d.def.id)) return false;
            var w = W;
            if (d.Herd) { if (d.Herd.OnRoute || d.Herd.AliveCount <= w.huntMinHerd) return false; }
            else if (GroupAlive(d) <= w.huntLonerMin) return false;
            return !InSafeArea(d.transform.position);
        }

        /// <summary>kill chance multiplier for this prey: a herd one above its minimum is hard to take from</summary>
        public static float KillMul(DinosaurController d)
        {
            var w = W; int alive = GroupAlive(d), min = d && d.Herd ? w.huntMinHerd : w.huntLonerMin;
            return alive <= min + 1 ? w.huntNearMinKillMul : 1f;
        }

        /// <summary>
        /// the best prey for this hunter: an allowed herbivore within huntSearchRadius of it, preferring stragglers (far from
        /// their herd's centre, or alone), wounded ones and close ones. Null = nothing worth it.
        /// </summary>
        public static DinosaurController PickPrey(DinosaurController hunter, in HuntProfile hp)
        {
            if (!hunter) return null;
            var w = W; float r = w.huntSearchRadius, r2 = r * r;
            Vector3 hp0 = hunter.transform.position;
            DinosaurController best = null; float bestS = float.MinValue;
            var all = DinosaurController.All;
            for (int i = 0; i < all.Count; i++)
            {
                var d = all[i];
                if (!d || d == hunter || d.State == DinoState.Flee || d.Hunted) continue;
                Vector3 v = d.transform.position - hp0; v.y = 0f; float q2 = v.sqrMagnitude;
                if (q2 > r2 || !Allowed(hp, d)) continue;
                float vis = WildlifeZones.CreatureSightMul(hp0, d.transform.position);          // Phase 2: dense vegetation hides prey too
                if (vis < 0.999f && q2 > r2 * vis * vis) continue;
                float straggle;
                if (d.Herd) { Vector3 o = d.transform.position - d.Herd.Centroid; o.y = 0f; straggle = o.magnitude / Mathf.Max(3f, d.Herd.Spread); }
                else straggle = 2f;
                float s = straggle * 10f - Mathf.Sqrt(q2) * 0.08f + (1f - d.Health / Mathf.Max(1f, d.def.maxHealth)) * 6f + Random.value * 2f;
                if (s > bestS) { bestS = s; best = d; }
            }
            return best;
        }
    }
}
