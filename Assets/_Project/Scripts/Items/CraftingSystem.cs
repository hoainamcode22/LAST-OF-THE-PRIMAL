using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using PrimalFrontier.Core;

namespace PrimalFrontier.Items
{
    /// <summary>
    /// Player crafting: known recipes, requirement checks (materials + station), and a small queue. Ingredients are
    /// taken when a job is queued and refunded if it is cancelled, so items can never be duplicated or lost.
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
            if (!StationAvailable(r.station)) return "Needs a lit campfire nearby";
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
            return null;
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
            if (j.recipe.station != CraftStation.None && !StationAvailable(j.recipe.station)) return;   // walked away from the fire
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
            int left = inventory.Add(r.output, r.outputCount, true);          // crafted items may exceed the weight limit
            if (left > 0) World.WorldPickup.Drop(r.output, left, transform.position + transform.forward * 0.6f + Vector3.up * 0.3f);
            Crafted?.Invoke(r);
            GameEvents.Raise(GameEventType.ItemCrafted, r.output.id, r.outputCount, transform.position);
        }

        public float CurrentProgress01 => _queue.Count == 0 ? 0f : Mathf.Clamp01(_queue[0].progress / _queue[0].recipe.craftSeconds);
    }
}
