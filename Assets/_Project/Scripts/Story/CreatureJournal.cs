using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using PrimalFrontier.AI;
using PrimalFrontier.Core;
using PrimalFrontier.Player;
using PrimalFrontier.World;

namespace PrimalFrontier.Story
{
    /// <summary>what the survivor has seen a species do (journal creature pages are written from this)</summary>
    [Flags]
    public enum CreatureSeen
    {
        None = 0, Sighted = 1, Eating = 2, Drinking = 4, Sleeping = 8, Herd = 16, Hunting = 32, Fleeing = 64, FireWary = 128,
        Attacked = 256, Calm = 512, Resting = 1024, Defending = 2048, HerdWatched = 4096,
    }

    /// <summary>
    /// Fills creature pages by observation: a few creatures per tick (round robin), close enough, in front of the camera and
    /// with a clear line of sight (at most two linecasts per tick), are checked for what they are doing: eating (diet),
    /// drinking, sleeping or resting, fleeing, hunting / charging, keeping away from a fire, moving in a herd; where they
    /// are (habitat = the location they stand in); being attacked by one reveals its threat, and watching a calm grazer
    /// long enough does too. Watching a herd for a few seconds raises CreatureObserved "herd:species" (mission 9).
    /// No allocation per tick once every species has an entry.
    /// </summary>
    public class CreatureJournal
    {
        public class Knowledge
        {
            public string id; public CreatureSeen seen; public readonly List<string> habitats = new List<string>(4);
            public int herdMax; public float watchSeconds, herdWatch; public DinosaurController.FireMode fire; public Temperament temperament; public string displayName;
            public bool Has(CreatureSeen f) => (seen & f) != 0;
        }

        public float tickSeconds = 0.4f, observeRange = 55f, largeObserveRange = 90f, ambientRange = 130f, viewDot = 0.6f;
        public float herdRadius = 25f, herdWatchSeconds = 6f, calmWatchSeconds = 45f;
        public int maxPerTick = 6, maxLinecastsPerTick = 2;

        readonly Dictionary<string, Knowledge> _known = new Dictionary<string, Knowledge>(StringComparer.Ordinal);
        public IEnumerable<Knowledge> All => _known.Values;
        /// <summary>a species got a new note (id)</summary>
        public event Action<string, CreatureSeen> Learned;
        int _cursor, _ambientCursor; float _next;
        PlayerHealth _hp; Transform _hpOwner;
        static int _mask = -1;

        public Knowledge Get(string id) => id != null && _known.TryGetValue(id, out var k) ? k : null;
        public void Clear() { _known.Clear(); _cursor = _ambientCursor = 0; }

        Knowledge For(DinosaurDefinition def)
        {
            if (!def || string.IsNullOrEmpty(def.id)) return null;
            if (!_known.TryGetValue(def.id, out var k)) { k = new Knowledge { id = def.id }; _known[def.id] = k; }
            k.temperament = def.temperament; k.displayName = def.displayName;
            return k;
        }

        void Add(Knowledge k, CreatureSeen f, Vector3 at)
        {
            if (k == null || (k.seen & f) == f) return;
            k.seen |= f;
            Learned?.Invoke(k.id, f);
            GameEvents.Raise(GameEventType.CreatureObserved, Tag(f) + ":" + k.id, 1, at);
        }
        static string Tag(CreatureSeen f)
        {
            switch (f)
            {
                case CreatureSeen.Sighted: return "sighted"; case CreatureSeen.Eating: return "eating"; case CreatureSeen.Drinking: return "drinking";
                case CreatureSeen.Sleeping: return "sleeping"; case CreatureSeen.Herd: return "group"; case CreatureSeen.Hunting: return "hunting";
                case CreatureSeen.Fleeing: return "fleeing"; case CreatureSeen.FireWary: return "fire"; case CreatureSeen.Attacked: return "attacked";
                case CreatureSeen.Calm: return "calm"; case CreatureSeen.Resting: return "resting"; case CreatureSeen.Defending: return "defending";
                case CreatureSeen.HerdWatched: return "herd";
            }
            return f.ToString().ToLowerInvariant();
        }

        // ------------------------------------------------------------------ tick
        public void Tick(float now)
        {
            if (now < _next) return;
            _next = now + tickSeconds;
            var player = PlayerLocator.Player; var cam = Camera.main;
            if (!player || !cam) return;
            HookHealth(player);
            if (_mask == -1) _mask = ~LayerMask.GetMask("Player", "UI", "Ignore Raycast");
            Vector3 eye = cam.transform.position, fwd = cam.transform.forward, pp = player.position;
            int casts = 0;
            var all = DinosaurController.All;
            int n = Mathf.Min(maxPerTick, all.Count);
            for (int k = 0; k < n; k++)
            {
                if (all.Count == 0) break;
                _cursor = (_cursor + 1) % all.Count;
                var d = all[_cursor];
                if (!d || !d.def || !d.IsAlive) continue;
                float range = d.def.bodyRadius > 1.5f ? largeObserveRange : observeRange;
                Vector3 c = d.transform.position + Vector3.up * Mathf.Clamp(d.def.bodyRadius, 0.4f, 2.5f);
                Vector3 v = c - eye; float dist = v.magnitude;
                if ((d.transform.position - pp).sqrMagnitude > range * range || dist < 0.1f) continue;
                if (Vector3.Dot(fwd, v / dist) < viewDot && dist > 10f) continue;
                if (casts >= maxLinecastsPerTick) break;
                casts++;
                if (Physics.Linecast(eye, c, out var hit, _mask, QueryTriggerInteraction.Ignore) && hit.collider && hit.collider.GetComponentInParent<DinosaurController>() != d) continue;
                Observe(d);
            }
            var amb = AmbientCreature.All;
            for (int k = 0; k < 2 && amb.Count > 0; k++)
            {
                _ambientCursor = (_ambientCursor + 1) % amb.Count;
                var a = amb[_ambientCursor]; if (!a || !a.def || !a.IsAlive) continue;
                Vector3 v = a.transform.position - eye; float dist = v.magnitude;
                if (dist > ambientRange || dist < 0.1f || Vector3.Dot(fwd, v / dist) < viewDot) continue;
                var kn = For(a.def); Add(kn, CreatureSeen.Sighted, a.transform.position);
            }
        }

        void Observe(DinosaurController d)
        {
            var k = For(d.def); if (k == null) return;
            var at = d.transform.position;
            Add(k, CreatureSeen.Sighted, at);
            k.watchSeconds += tickSeconds;
            AddHabitat(k, at);
            bool herbivore = d.def.temperament == Temperament.Passive || d.def.temperament == Temperament.Defensive;
            switch (d.State)
            {
                case DinoState.Eat: Add(k, CreatureSeen.Eating, at); break;
                case DinoState.Drink: Add(k, CreatureSeen.Drinking, at); break;
                case DinoState.Rest: Add(k, d.Sleeping ? CreatureSeen.Sleeping : CreatureSeen.Resting, at); break;
                case DinoState.Flee: Add(k, CreatureSeen.Fleeing, at); break;
                case DinoState.Chase: case DinoState.Attack: Add(k, herbivore ? CreatureSeen.Defending : CreatureSeen.Hunting, at); break;
            }
            if (d.FireBehaviour != DinosaurController.FireMode.None) { k.fire = d.FireBehaviour; Add(k, CreatureSeen.FireWary, at); }
            // herd: others of the same species close by
            int group = 1;
            var all = DinosaurController.All;
            float hr = d.def.herdShareRadius > 0f ? Mathf.Clamp(d.def.herdShareRadius, herdRadius, 45f) : herdRadius, r2 = hr * hr;   // loose herds spread wider
            for (int i = 0; i < all.Count; i++) { var o = all[i]; if (o && o != d && o.def == d.def && o.IsAlive && (o.transform.position - at).sqrMagnitude < r2) group++; }
            if (group >= 3)
            {
                if (group > k.herdMax) k.herdMax = group;
                Add(k, CreatureSeen.Herd, at);
                k.herdWatch += tickSeconds;
                if (k.herdWatch >= herdWatchSeconds) Add(k, CreatureSeen.HerdWatched, at);
            }
            if (herbivore && k.watchSeconds >= calmWatchSeconds && !k.Has(CreatureSeen.Attacked)) Add(k, CreatureSeen.Calm, at);
        }

        void AddHabitat(Knowledge k, Vector3 at)
        {
            if (k.habitats.Count >= 4) return;
            var zm = ZoneManager.Instance; if (!zm) return;
            string best = null; float bestR = float.MaxValue;
            foreach (var z in zm.zones)
            {
                if (z == null || string.IsNullOrEmpty(z.id)) continue;
                var loc = StoryIds.Location(z.id);
                if (loc.StartsWith("hab_", StringComparison.Ordinal) || loc == "camp") continue;       // creature homes are not places
                var d = at - z.center; d.y = 0f;
                if (d.sqrMagnitude <= z.radius * z.radius && z.radius < bestR) { bestR = z.radius; best = loc; }
            }
            if (best != null && !k.habitats.Contains(best)) k.habitats.Add(best);
        }

        void HookHealth(Transform player)
        {
            if (player == _hpOwner) return;
            if (_hp) _hp.Damaged -= OnDamaged;
            _hpOwner = player; _hp = player.GetComponent<PlayerHealth>();
            if (_hp) _hp.Damaged += OnDamaged;
        }
        public void Unhook() { if (_hp) _hp.Damaged -= OnDamaged; _hp = null; _hpOwner = null; }

        /// <summary>a creature hurt the player: the one attacking closest to where the hit came from</summary>
        void OnDamaged(float amount, Vector3 source, bool heavy)
        {
            DinosaurController best = null; float bestD = 8f * 8f;
            foreach (var d in DinosaurController.All)
            {
                if (!d || !d.def || !d.IsAlive || (d.State != DinoState.Attack && d.State != DinoState.Chase)) continue;
                float s = (d.transform.position - source).sqrMagnitude; if (s < bestD) { bestD = s; best = d; }
            }
            if (!best) return;
            var k = For(best.def);
            Add(k, CreatureSeen.Sighted, best.transform.position);
            Add(k, CreatureSeen.Attacked, best.transform.position);
        }

        /// <summary>AI's events (HerdSighted with a species id): a herd seen by the wildlife system counts as watched</summary>
        public void OnHerdEvent(string speciesId, Vector3 at)
        {
            if (string.IsNullOrEmpty(speciesId)) return;
            foreach (var d in DinosaurController.All)
                if (d && d.def && d.def.id == speciesId) { var k = For(d.def); Add(k, CreatureSeen.Herd, at); Add(k, CreatureSeen.HerdWatched, at); return; }
        }

        // ------------------------------------------------------------------ page text
        static readonly StringBuilder _sb = new StringBuilder(512);

        /// <summary>the page of a species: appearance, then what has been seen of its habitat, diet, habits and threat</summary>
        public string Compose(string id, string appearance)
        {
            var k = Get(id); var t = StoryTexts.Creature(id);
            _sb.Clear();
            _sb.Append(string.IsNullOrEmpty(appearance) ? (t != null ? t.appearance : "") : appearance);
            _sb.Append("\n\n<b>Seen at:</b> ");
            if (k != null && k.habitats.Count > 0)
                for (int i = 0; i < k.habitats.Count; i++) { if (i > 0) _sb.Append(i == k.habitats.Count - 1 ? " and " : ", "); _sb.Append(StoryIds.PlaceName(k.habitats[i])); }
            else _sb.Append("not noted yet");
            _sb.Append(".\n<b>Diet:</b> ");
            if (k != null && k.Has(CreatureSeen.Eating)) _sb.Append(t != null ? t.diet : Herbivore(k) ? "Plants." : "Meat.");
            else _sb.Append("I have not seen it eat.");
            if (k != null)
            {
                int n0 = _sb.Length;
                _sb.Append("\n<b>Habits:</b>");
                if (k.Has(CreatureSeen.Herd)) _sb.Append(k.herdMax >= 3 ? " Moves in groups of up to " + k.herdMax + "." : " Moves in groups.");
                if (k.Has(CreatureSeen.Drinking)) _sb.Append(" Comes to the water to drink.");
                if (k.Has(CreatureSeen.Sleeping)) _sb.Append(" Sleeps with its head down, still as stone.");
                else if (k.Has(CreatureSeen.Resting)) _sb.Append(" Rests for long hours.");
                if (k.Has(CreatureSeen.Fleeing)) _sb.Append(" Runs at the first sign of danger.");
                if (k.Has(CreatureSeen.Defending)) _sb.Append(" Turns and charges when pressed.");
                if (k.Has(CreatureSeen.Hunting)) _sb.Append(" I watched it hunt.");
                if (k.Has(CreatureSeen.FireWary)) _sb.Append(FireText(k.fire));
                if (_sb.Length == n0 + "\n<b>Habits:</b>".Length) _sb.Length = n0;
            }
            _sb.Append("\n<b>Threat:</b> ").Append(Threat(k));
            return _sb.ToString();
        }

        static bool Herbivore(Knowledge k) => k.temperament == Temperament.Passive || k.temperament == Temperament.Defensive;

        static string FireText(DinosaurController.FireMode m)
        {
            switch (m)
            {
                case DinosaurController.FireMode.Avoid: return " Keeps well away from fire.";
                case DinosaurController.FireMode.Circle: return " Circles a fire at the edge of the light.";
                case DinosaurController.FireMode.Wait: return " Waits outside the firelight. Patient.";
                case DinosaurController.FireMode.Observe: return " Stands and watches the fire.";
            }
            return " Wary of fire.";
        }

        static string Threat(Knowledge k)
        {
            if (k == null) return "unknown.";
            bool known = k.Has(CreatureSeen.Attacked) || k.Has(CreatureSeen.Hunting) || k.Has(CreatureSeen.Defending) || k.Has(CreatureSeen.Calm);
            if (k.id == "apex" && (known || k.Has(CreatureSeen.Sighted))) return "everything on this island fears it. So do I.";
            if (!known)
            {
                if (k.temperament == Temperament.AmbientFlyer) return "none that I have seen.";
                if (k.temperament == Temperament.AmbientSwimmer) return "stay out of deep water.";
                return "unknown. I have not seen it angry.";
            }
            switch (k.temperament)
            {
                case Temperament.Passive: return k.Has(CreatureSeen.Attacked) ? "it struck me when I came too close." : "low. It ignores me if I keep my distance.";
                case Temperament.Defensive: return k.Has(CreatureSeen.Attacked) || k.Has(CreatureSeen.Defending) ? "it charges when crowded. Keep well back." : "low if left alone. Dangerous when crowded.";
                case Temperament.Territorial: return "high inside its ground. Leave the moment it notices me.";
                case Temperament.Predator: return "deadly. They hunt together.";
            }
            return "unknown.";
        }

        // ------------------------------------------------------------------ save
        [Serializable] public class SavedCreature { public string id; public int seen; public string[] habitats; public int herdMax; public float watch, herdWatch; public int fire, temper; }
        [Serializable] class Saved { public List<SavedCreature> creatures = new List<SavedCreature>(); }

        public string Capture()
        {
            if (_known.Count == 0) return null;
            var d = new Saved();
            foreach (var k in _known.Values) d.creatures.Add(new SavedCreature { id = k.id, seen = (int)k.seen, habitats = k.habitats.ToArray(), herdMax = k.herdMax, watch = k.watchSeconds, herdWatch = k.herdWatch, fire = (int)k.fire, temper = (int)k.temperament });
            return JsonUtility.ToJson(d);
        }

        public void Restore(string json)
        {
            var d = JsonUtility.FromJson<Saved>(json); if (d == null || d.creatures == null) return;
            _known.Clear();
            foreach (var c in d.creatures)
            {
                if (c == null || string.IsNullOrEmpty(c.id)) continue;
                var k = new Knowledge { id = c.id, seen = (CreatureSeen)c.seen, herdMax = c.herdMax, watchSeconds = c.watch, herdWatch = c.herdWatch, fire = (DinosaurController.FireMode)c.fire, temperament = (Temperament)c.temper };
                if (c.habitats != null) foreach (var h in c.habitats) if (!string.IsNullOrEmpty(h) && k.habitats.Count < 4) k.habitats.Add(h);
                _known[c.id] = k;
            }
        }
    }
}
