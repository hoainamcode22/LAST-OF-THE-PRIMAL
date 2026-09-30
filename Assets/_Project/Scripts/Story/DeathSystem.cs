using System;
using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.Survival;
using PrimalFrontier.World;

namespace PrimalFrontier.Story
{
    /// <summary>
    /// Death rules (directive 65): a share of what the player carries is left where they died, as one recoverable bundle
    /// (a hide-wrapped pack on the ground, marked on the map); the tool / weapon in hand (and optionally every tool and
    /// weapon) stays with the player; the journal, recipes, the world and every structure stay as they are. GameManager
    /// runs the flow (respawn at the last rested shelter / bed, else the beach, then a save). Bundles are saved as the
    /// "death" section.
    /// </summary>
    public class DeathSystem : MonoBehaviour, ISaveSection
    {
        public static DeathSystem Instance { get; private set; }
        [Tooltip("share of every carried stack left in the bundle (0.5 = half, rounded up)")] [Range(0, 1)] public float dropShare = 0.5f;
        [Tooltip("the item in hand stays with the player")] public bool keepEquipped = true;
        [Tooltip("every tool and weapon stays with the player")] public bool keepToolsAndWeapons;
        [Tooltip("in-game hours a bundle stays before the island takes it (0 = until picked up)")] [Min(0)] public float bundleHours;
        [Tooltip("bundles kept at once; a new death past this removes the oldest")] [Min(1)] public int maxBundles = 3;

        /// <summary>what the last death left behind (death screen text)</summary>
        public int LastStacks { get; private set; }
        public int LastItems { get; private set; }

        void Awake() { Instance = this; }
        void OnEnable() { SaveSystem.RegisterSection(this); }
        void OnDisable() { SaveSystem.UnregisterSection(this); }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (bundleHours <= 0f || DeathBundle.All.Count == 0) return;
            for (int i = DeathBundle.All.Count - 1; i >= 0; i--)
            {
                var b = DeathBundle.All[i];
                if (b && GameClock.Now - b.droppedAt > GameClock.Hours(bundleHours)) Destroy(b.gameObject);
            }
        }

        /// <summary>takes the share out of the pack and leaves it at the player's feet (null when nothing was left)</summary>
        public DeathBundle OnPlayerDied(GameObject player)
        {
            LastStacks = LastItems = 0;
            var inv = player ? player.GetComponent<InventorySystem>() : null; if (!inv) return null;
            var taken = TakeShare(inv, dropShare, keepEquipped, keepToolsAndWeapons);
            if (taken.Count == 0) return null;
            LastStacks = taken.Count; foreach (var s in taken) LastItems += s.count;
            while (DeathBundle.All.Count >= maxBundles) { var old = DeathBundle.All[0]; DeathBundle.All.RemoveAt(0); if (old) Destroy(old.gameObject); }
            return DeathBundle.Spawn(player.transform.position, taken, GameClock.Now);
        }

        /// <summary>
        /// the share of each stack (rounded up; single items: every other one, deterministic). Kept: the item in hand when
        /// keepEquipped, tools and weapons when keepTools. Water containers go whole (water inside).
        /// </summary>
        public static List<ItemStack> TakeShare(InventorySystem inv, float share, bool keepEquipped, bool keepTools)
        {
            var taken = new List<ItemStack>();
            if (inv == null || inv.Slots == null || share <= 0f) return taken;
            float acc = 0f;
            for (int i = 0; i < inv.Slots.Length; i++)
            {
                var s = inv.Slots[i]; if (s.IsEmptyOrNull()) continue;
                if (keepEquipped && i == inv.ActiveSlot && i < inv.hotbarSize) continue;
                var it = s.item;
                if (keepTools && (it.tool != ToolKind.None || it.weapon != WeaponKind.None || it.category == ItemCategory.Tool || it.category == ItemCategory.Weapon)) continue;
                if (s.count > 1 && it.maxStack > 1 && s.water == 0)
                {
                    int n = Mathf.Min(s.count, Mathf.CeilToInt(s.count * share));
                    if (n <= 0) continue;
                    var part = s.Clone(); part.count = n;
                    s.count -= n; if (s.count <= 0) inv.Slots[i] = null;
                    taken.Add(part);
                }
                else
                {
                    acc += share;
                    if (acc < 1f - 1e-4f) continue;
                    acc -= 1f;
                    taken.Add(s.Clone()); inv.Slots[i] = null;
                }
            }
            if (taken.Count > 0) inv.ForceNotify();
            return taken;
        }

        public void ResetAll() { DeathBundle.ClearAll(); LastStacks = LastItems = 0; }

        // ------------------------------------------------------------------ save
        [Serializable] class Saved { public List<SavedBundle> bundles = new List<SavedBundle>(); }
        [Serializable] class SavedBundle { public Vector3 pos; public double at; public List<SlotData> items = new List<SlotData>(); }

        public string SectionKey => "death";
        public string CaptureSection()
        {
            if (DeathBundle.All.Count == 0) return null;
            var d = new Saved();
            foreach (var b in DeathBundle.All)
            {
                if (!b) continue;
                var sb = new SavedBundle { pos = b.transform.position, at = b.droppedAt };
                foreach (var s in b.stacks)
                    if (!s.IsEmptyOrNull())
                        sb.items.Add(new SlotData { item = s.item.id, count = s.count, durability = s.durability, water = s.water, waterType = (int)s.waterType, dirty = s.waterType == WaterType.DirtyWater, age = Spoilage.Spoils(s.item) ? s.AgeSeconds : 0f });
                if (sb.items.Count > 0) d.bundles.Add(sb);
            }
            return d.bundles.Count > 0 ? JsonUtility.ToJson(d) : null;
        }

        public void RestoreSection(string json)
        {
            DeathBundle.ClearAll();
            var d = JsonUtility.FromJson<Saved>(json); var db = ItemDatabase.Instance;
            if (d == null || d.bundles == null || db == null) return;
            foreach (var sb in d.bundles)
            {
                if (sb == null || sb.items == null) continue;
                var list = new List<ItemStack>();
                foreach (var sl in sb.items)
                {
                    var it = sl != null ? db.Item(sl.item) : null; if (it == null || sl.count <= 0) continue;
                    var st = new ItemStack(it, Mathf.Min(sl.count, Mathf.Max(1, it.maxStack))) { durability = it.HasDurability ? Mathf.Clamp(sl.durability, 0.01f, it.maxDurability) : sl.durability };
                    st.water = Mathf.Clamp(sl.water, 0, it.waterCharges);
                    st.waterType = st.water > 0 && Enum.IsDefined(typeof(WaterType), sl.waterType) ? (WaterType)sl.waterType : WaterType.None;
                    st.madeAt = GameClock.Now - Mathf.Max(0f, sl.age);
                    list.Add(st);
                }
                if (list.Count > 0) DeathBundle.Spawn(sb.pos, list, sb.at, false);
            }
        }
    }
}
