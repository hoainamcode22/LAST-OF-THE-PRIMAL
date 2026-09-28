using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using PrimalFrontier.Core;

namespace PrimalFrontier.Items
{
    /// <summary>
    /// Player crafting: known recipes, requirement checks (materials, station, then the recipe's extra requirements:
    /// tool in the pack, station nearby, unlock day), and a small queue. Ingredients are taken when a job is queued and
    /// refunded if it is cancelled, so items can never be duplicated or lost; the save file keeps the queue (already paid).
    /// A job only progresses while the hands are free (standing still, no other action); the Craft loop plays meanwhile.
    /// </summary>
    public class CraftingSystem : MonoBehaviour
    {
        public const int MaxQueue = 5;
        public InventorySystem inventory;
        public float stationRadius = 4f;
        [Tooltip("set by the player: true when standing still and not busy")] public Func<bool> HandsFree = () => true;

        public class Job { public RecipeDefinition recipe; public float progress; }
        readonly List<Job> _queue = new List<Job>();
        readonly HashSet<string> _known = new HashSet<string>();
        public IReadOnlyList<Job> Queue => _queue;
        public IEnumerable<string> KnownIds => _known;
        public bool IsCrafting => _queue.Count > 0;
        public bool IsWorking { get; private set; }            // progressing this frame (drives the Craft animation)
        public event Action QueueChanged;
        public event Action<RecipeDefinition> Crafted, Learned;

        void Awake() { if (!inventory) inventory = GetComponent<InventorySystem>(); }

        void OnEnable() { GameEvents.Raised += OnGameEvent; }
        void OnDisable() { GameEvents.Raised -= OnGameEvent; }

        public void InitKnown(ItemDatabase db)
        {
            _known.Clear();
            if (db == null) return;
            foreach (var r in db.recipes) if (r && r.knownAtStart) _known.Add(r.id);
        }

        public bool IsKnown(RecipeDefinition r) => r != null && _known.Contains(r.id);
        public void Learn(RecipeDefinition r, bool notify = true)
        {
            if (r == null || !_known.Add(r.id)) return;
            if (notify) { Learned?.Invoke(r); GameEvents.Raise(GameEventType.JournalUnlocked, "recipe_" + r.id); }
        }
        public void SetKnown(IEnumerable<string> ids) { _known.Clear(); foreach (var i in ids) _known.Add(i); }

        // discovering a material teaches the recipes that need it (knownAtStart = false)
        void OnGameEvent(GameEvent e)
        {
            if (e.type != GameEventType.ItemAdded) return;
            var db = ItemDatabase.Instance; if (db == null) return;
            foreach (var r in db.recipes)
            {
                if (r == null || r.knownAtStart || IsKnown(r)) continue;
                foreach (var ing in r.ingredients) if (ing.item && ing.item.id == e.id) { Learn(r); break; }
            }
        }

        public bool StationAvailable(CraftStation st) => st == CraftStation.None || (st == CraftStation.Campfire && World.Campfire.LitNear(transform.position, stationRadius));

        /// <summary>the recipe's station and every requirement station are near (the queue pauses otherwise)</summary>
        public bool StationsAvailable(RecipeDefinition r)
        {
            if (r == null || !StationAvailable(r.station)) return false;
            var reqs = r.requirements; if (reqs == null) return true;
            for (int i = 0; i < reqs.Length; i++) if (!StationAvailable(reqs[i].nearStation)) return false;
            return true;
        }

        /// <summary>some item in the pack (any slot) has one of the given tool flags; None = always true</summary>
        public bool HasToolInPack(ToolKind anyOf)
        {
            if (anyOf == ToolKind.None) return true;
            var slots = inventory ? inventory.Slots : null; if (slots == null) return false;
            for (int i = 0; i < slots.Length; i++) { var s = slots[i]; if (!s.IsEmptyOrNull() && (s.item.tool & anyOf) != 0) return true; }
            return false;
        }

        /// <summary>null if every extra requirement of the recipe is met (tool in the pack, station near, unlock day), otherwise the reason</summary>
        public string CheckRequirements(RecipeDefinition r)
        {
            var reqs = r != null ? r.requirements : null; if (reqs == null) return null;
            for (int i = 0; i < reqs.Length; i++)
            {
                var q = reqs[i];
                if (!HasToolInPack(q.tool)) return ToolReason(q.tool);
                if (!StationAvailable(q.nearStation)) return StationReason(q.nearStation);
                if (q.minDay > 0 && CurrentDay < q.minDay) return "Not yet known";
            }
            return null;
        }

        /// <summary>game day for minDay requirements (no clock, e.g. tests: no day limit)</summary>
        static int CurrentDay { get { var tm = TimeManager.Instance; return tm ? tm.day : int.MaxValue; } }

        public static string StationLabel(CraftStation st) => st == CraftStation.Campfire ? "a lit campfire" : st == CraftStation.None ? "" : "a crafting station";
        public static string StationReason(CraftStation st) => st == CraftStation.Campfire ? "Needs a lit campfire nearby" : "Needs " + StationLabel(st) + " nearby";

        /// <summary>"a knife", "a hammer or a knife" (single kinds named like ResourceNode prompts)</summary>
        public static string ToolLabel(ToolKind k)
        {
            if (_toolLabels.TryGetValue(k, out var s)) return s;
            var sb = new StringBuilder();
            for (int bit = 1; bit > 0 && bit <= (int)k; bit <<= 1)
            {
                var f = (ToolKind)bit; if ((k & f) == 0) continue;
                if (sb.Length > 0) sb.Append(" or ");
                sb.Append(f == ToolKind.Light ? "a torch" : World.ResourceNode.ToolName(f));
            }
            s = sb.Length > 0 ? sb.ToString() : "a tool";
            _toolLabels[k] = s; return s;
        }
        public static string ToolReason(ToolKind k)
        {
            if (_toolReasons.TryGetValue(k, out var s)) return s;
            s = "Needs " + ToolLabel(k) + " in your pack"; _toolReasons[k] = s; return s;
        }
        static readonly Dictionary<ToolKind, string> _toolLabels = new Dictionary<ToolKind, string>(), _toolReasons = new Dictionary<ToolKind, string>();

        public bool HasIngredients(RecipeDefinition r, int times = 1)
        {
            foreach (var ing in r.ingredients) if (inventory.Count(ing.item) < ing.count * times) return false;
            return true;
        }

        /// <summary>null if craftable, otherwise a short reason for the UI</summary>
        public string Check(RecipeDefinition r)
        {
            if (r == null || r.output == null) return "Invalid recipe";
            if (!IsKnown(r)) return "Unknown recipe";
            if (_queue.Count >= MaxQueue) return "Queue is full";
            if (!StationAvailable(r.station)) return StationReason(r.station);
            if (!HasIngredients(r))
            {
                var sb = new StringBuilder("Missing: ");
                bool first = true;
                foreach (var ing in r.ingredients)
                {
                    int have = inventory.Count(ing.item);
                    if (have >= ing.count) continue;
                    if (!first) sb.Append(", "); first = false;
                    sb.Append(ing.count - have).Append(' ').Append(ing.item ? ing.item.displayName : "?");
                }
                return sb.ToString();
            }
            return CheckRequirements(r);
        }

        public int MaxCraftable(RecipeDefinition r)
        {
            int n = int.MaxValue;
            foreach (var ing in r.ingredients) n = Mathf.Min(n, inventory.Count(ing.item) / Mathf.Max(1, ing.count));
            return n == int.MaxValue ? 0 : n;
        }

        public bool Enqueue(RecipeDefinition r)
        {
            if (Check(r) != null) return false;
            foreach (var ing in r.ingredients) inventory.Remove(ing.item, ing.count);
            _queue.Add(new Job { recipe = r });
            QueueChanged?.Invoke();
            return true;
        }

        public void Cancel(int index)
        {
            if (index < 0 || index >= _queue.Count) return;
            var j = _queue[index]; _queue.RemoveAt(index);
            foreach (var ing in j.recipe.ingredients)
            {
                int left = inventory.Add(ing.item, ing.count, true);
                if (left > 0) World.WorldPickup.Drop(ing.item, left, transform.position + transform.forward * 0.6f + Vector3.up * 0.3f);
            }
            QueueChanged?.Invoke();
        }

        public void CancelAll() { for (int i = _queue.Count - 1; i >= 0; i--) Cancel(i); }

        void Update()
        {
            IsWorking = false;
            if (_queue.Count == 0) return;
            var j = _queue[0];
            if (!StationsAvailable(j.recipe)) return;                                               // walked away from the fire
            if (!HandsFree()) return;
            IsWorking = true;
            j.progress += Time.deltaTime;
            if (j.progress < j.recipe.craftSeconds) return;
            _queue.RemoveAt(0);
            Complete(j.recipe);
            QueueChanged?.Invoke();
        }

        /// <summary>instant completion (tests / debug)</summary>
        public void CompleteFirstNow() { if (_queue.Count == 0) return; var j = _queue[0]; _queue.RemoveAt(0); Complete(j.recipe); QueueChanged?.Invoke(); }

        void Complete(RecipeDefinition r)
        {
            Give(r.output, r.outputCount);
            var extra = r.extraResults;
            if (extra != null) for (int i = 0; i < extra.Length; i++) Give(extra[i].item, extra[i].count);
            Crafted?.Invoke(r);
            GameEvents.Raise(GameEventType.ItemCrafted, r.output.id, r.outputCount, transform.position);
        }

        void Give(ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return;
            int left = inventory.Add(item, count, true);                       // crafted items may exceed the weight limit
            if (left > 0) World.WorldPickup.Drop(item, left, transform.position + transform.forward * 0.6f + Vector3.up * 0.3f);
        }

        // ------------------------------------------------------------------ save / load
        /// <summary>the queue in order as (recipe id, progress seconds) for the save file</summary>
        public List<(string recipeId, float progress)> GetQueueForSave()
        {
            var l = new List<(string, float)>(_queue.Count);
            foreach (var j in _queue) if (j.recipe) l.Add((j.recipe.id, j.progress));
            return l;
        }

        /// <summary>
        /// load: replaces the queue with jobs that were already paid for (no ingredients are taken, nothing is refunded).
        /// Unknown recipe ids are skipped. Returns how many jobs were restored.
        /// </summary>
        public int RestoreQueue(IEnumerable<(string recipeId, float progress)> jobs, ItemDatabase db)
        {
            _queue.Clear(); IsWorking = false;
            if (jobs != null && db != null)
                foreach (var (id, progress) in jobs)
                {
                    if (_queue.Count >= MaxQueue) break;
                    var r = db.Recipe(id); if (r == null || r.output == null) continue;
                    _queue.Add(new Job { recipe = r, progress = Mathf.Clamp(progress, 0f, r.craftSeconds) });
                }
            QueueChanged?.Invoke();
            return _queue.Count;
        }

        /// <summary>empties the queue WITHOUT refunding (the inventory is being replaced, e.g. by a load)</summary>
        public void ClearQueue() { if (_queue.Count == 0) return; _queue.Clear(); IsWorking = false; QueueChanged?.Invoke(); }

        public float CurrentProgress01 => _queue.Count == 0 ? 0f : Mathf.Clamp01(_queue[0].progress / _queue[0].recipe.craftSeconds);
    }
}
