using System;
using UnityEngine;
using PrimalFrontier.Core;

namespace PrimalFrontier.Items
{
    /// <summary>
    /// Slot inventory used by the player (hotbar = first <see cref="hotbarSize"/> slots) and by storage boxes.
    /// Stacks, carry weight, move / swap / split / quick-transfer. Never duplicates or loses items: every operation
    /// either completes or leaves the slots untouched.
    /// </summary>
    public class InventorySystem : MonoBehaviour
    {
        [Min(1)] public int slotCount = 32;
        [Min(0)] public int hotbarSize = 8;
        [Tooltip("kg; 0 = no limit")] public float maxWeight = 45f;
        [Tooltip("raise ItemAdded / ItemRemoved game events (player only)")] public bool raiseGameEvents = true;

        [NonSerialized] public ItemStack[] Slots;
        public event Action Changed;
        public event Action<int> ActiveSlotChanged;

        public int ActiveSlot { get; private set; }
        public ItemStack ActiveStack => hotbarSize > 0 && !Slots[ActiveSlot].IsEmptyOrNull() ? Slots[ActiveSlot] : null;
        public ItemDefinition ActiveItem => ActiveStack?.item;
        public float Weight { get; private set; }
        public bool IsOverweight => maxWeight > 0f && Weight > maxWeight;
        public int BagStart => hotbarSize;

        void Awake() { EnsureSlots(); }

        public void EnsureSlots()
        {
            if (Slots != null && Slots.Length == slotCount) return;
            var old = Slots; Slots = new ItemStack[slotCount];
            if (old != null) Array.Copy(old, Slots, Mathf.Min(old.Length, slotCount));
        }

        void Notify() { RecalcWeight(); Changed?.Invoke(); }
        void RecalcWeight() { float w = 0; foreach (var s in Slots) if (!s.IsEmptyOrNull()) w += s.Weight; Weight = w; }

        public ItemStack Get(int i) => i >= 0 && i < Slots.Length && !Slots[i].IsEmptyOrNull() ? Slots[i] : null;

        public int Count(ItemDefinition item)
        {
            if (item == null) return 0; int n = 0;
            foreach (var s in Slots) if (!s.IsEmptyOrNull() && s.item == item) n += s.count;
            return n;
        }
        public bool Has(ItemDefinition item, int count = 1) => Count(item) >= count;

        /// <summary>how many of item fit (slots and weight)</summary>
        public int SpaceFor(ItemDefinition item, bool ignoreWeight = false)
        {
            if (item == null) return 0;
            long space = 0;
            foreach (var s in Slots)
            {
                if (s.IsEmptyOrNull()) space += item.maxStack;
                else if (s.item == item && item.maxStack > 1) space += Mathf.Max(0, item.maxStack - s.count);
            }
            if (!ignoreWeight && maxWeight > 0f && item.weight > 0f)
                space = Math.Min(space, (long)Mathf.Floor((maxWeight - Weight) / item.weight + 1e-4f));
            return (int)Math.Max(0, Math.Min(space, int.MaxValue));
        }

        bool PrefersHotbar(ItemDefinition item) =>
            hotbarSize > 0 && (item.category == ItemCategory.Tool || item.category == ItemCategory.Weapon || item.category == ItemCategory.Food ||
                               item.category == ItemCategory.Structure || item.IsWaterContainer);

        /// <summary>adds up to count; returns how many did NOT fit (0 = all added)</summary>
        public int Add(ItemDefinition item, int count, bool ignoreWeight = false)
        {
            if (item == null || count <= 0) return count;
            int fit = Mathf.Min(count, SpaceFor(item, ignoreWeight));
            int left = fit;
            // 1) top up existing stacks
            if (item.maxStack > 1)
                for (int i = 0; i < Slots.Length && left > 0; i++)
                {
                    var s = Slots[i];
                    if (s.IsEmptyOrNull() || s.item != item || s.count >= item.maxStack) continue;
                    int k = Mathf.Min(left, item.maxStack - s.count); s.count += k; left -= k;
                }
            // 2) empty slots (tools / food try the hotbar first, resources the bag)
            bool hot = PrefersHotbar(item);
            for (int pass = 0; pass < 2 && left > 0; pass++)
            {
                bool hotPass = hot ? pass == 0 : pass == 1;
                int a = hotPass ? 0 : hotbarSize, b = hotPass ? hotbarSize : Slots.Length;
                for (int i = a; i < b && left > 0; i++)
                {
                    if (!Slots[i].IsEmptyOrNull()) continue;
                    int k = Mathf.Min(left, item.maxStack);
                    Slots[i] = new ItemStack(item, k); left -= k;
                }
            }
            int added = fit - left;
            if (added > 0)
            {
                Notify();
                if (raiseGameEvents) GameEvents.Raise(GameEventType.ItemAdded, item.id, added, transform.position);
                if (hotbarSize > 0) ActiveSlotChanged?.Invoke(ActiveSlot);
            }
            return count - added;
        }

        /// <summary>adds a unique stack (keeps durability / water); false if no room</summary>
        public bool AddStack(ItemStack stack, bool ignoreWeight = false)
        {
            if (stack == null || stack.IsEmpty) return false;
            if (stack.item.maxStack > 1 && Mathf.Approximately(stack.durability, stack.item.maxDurability) && stack.water == 0)
            {
                if (SpaceFor(stack.item, ignoreWeight) < stack.count) return false;     // all or nothing: no duplication
                return Add(stack.item, stack.count, ignoreWeight) == 0;
            }
            if (!ignoreWeight && maxWeight > 0f && Weight + stack.Weight > maxWeight) return false;
            bool hot = PrefersHotbar(stack.item);
            for (int pass = 0; pass < 2; pass++)
            {
                bool hotPass = hot ? pass == 0 : pass == 1;
                int a = hotPass ? 0 : hotbarSize, b = hotPass ? hotbarSize : Slots.Length;
                for (int i = a; i < b; i++)
                    if (Slots[i].IsEmptyOrNull())
                    {
                        Slots[i] = stack.Clone(); Notify();
                        if (raiseGameEvents) GameEvents.Raise(GameEventType.ItemAdded, stack.item.id, stack.count, transform.position);
                        return true;
                    }
            }
            return false;
        }

        /// <summary>removes count items (bag first, hotbar last); false and no change if not enough</summary>
        public bool Remove(ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return count <= 0;
            if (Count(item) < count) return false;
            int left = count;
            for (int i = Slots.Length - 1; i >= 0 && left > 0; i--)
            {
                var s = Slots[i];
                if (s.IsEmptyOrNull() || s.item != item) continue;
                int k = Mathf.Min(left, s.count); s.count -= k; left -= k;
                if (s.count <= 0) Slots[i] = null;
            }
            Notify();
            if (raiseGameEvents) GameEvents.Raise(GameEventType.ItemRemoved, item.id, count, transform.position);
            return true;
        }

        /// <summary>takes count out of one slot and returns them as a new stack (null if empty)</summary>
        public ItemStack TakeFromSlot(int i, int count)
        {
            var s = Get(i); if (s == null || count <= 0) return null;
            count = Mathf.Min(count, s.count);
            var taken = s.Clone(); taken.count = count;
            s.count -= count; if (s.count <= 0) Slots[i] = null;
            Notify();
            if (raiseGameEvents) GameEvents.Raise(GameEventType.ItemRemoved, taken.item.id, count, transform.position);
            return taken;
        }

        /// <summary>drag & drop inside this inventory or into another one: merge, else swap</summary>
        public bool Move(int from, InventorySystem dst, int to)
        {
            if (dst == null) dst = this;
            var a = Get(from); if (a == null) return false;
            if (to < 0 || to >= dst.Slots.Length) return false;
            if (dst == this && from == to) return false;
            var b = dst.Get(to);
            if (dst != this && dst.maxWeight > 0f)
            {
                float incoming = a.Weight - (b != null && !b.CanMergeWith(a) ? b.Weight : 0f);
                if (dst.Weight + incoming > dst.maxWeight + 1e-3f) return false;
            }
            if (b != null && b.CanMergeWith(a))
            {
                int k = Mathf.Min(a.count, b.item.maxStack - b.count);
                if (k <= 0) return false;
                b.count += k; a.count -= k; if (a.count <= 0) Slots[from] = null;
            }
            else
            {
                if (dst != this && b != null && maxWeight > 0f && Weight - a.Weight + b.Weight > maxWeight + 1e-3f) return false;
                dst.Slots[to] = a; Slots[from] = b;
            }
            Notify(); if (dst != this) dst.Notify();
            if (hotbarSize > 0) ActiveSlotChanged?.Invoke(ActiveSlot);
            if (dst != this && dst.hotbarSize > 0) dst.ActiveSlotChanged?.Invoke(dst.ActiveSlot);
            return true;
        }

        /// <summary>shift-click: send the whole slot to another inventory (merging first)</summary>
        public bool QuickTransfer(int from, InventorySystem dst)
        {
            var a = Get(from); if (a == null || dst == null || dst == this) return false;
            int n = a.count;
            if (a.item.maxStack > 1 && a.water == 0)
            {
                int left = dst.Add(a.item, n);
                int moved = n - left; if (moved <= 0) return false;
                a.count -= moved; if (a.count <= 0) Slots[from] = null;
                Notify(); return true;
            }
            if (!dst.AddStack(a)) return false;
            Slots[from] = null; Notify(); return true;
        }

        public bool Split(int i)
        {
            var s = Get(i); if (s == null || s.count < 2) return false;
            int empty = -1;
            for (int k = hotbarSize; k < Slots.Length && empty < 0; k++) if (Slots[k].IsEmptyOrNull()) empty = k;
            for (int k = 0; k < hotbarSize && empty < 0; k++) if (Slots[k].IsEmptyOrNull()) empty = k;
            if (empty < 0) return false;
            int half = s.count / 2; s.count -= half;
            var n = s.Clone(); n.count = half; Slots[empty] = n;
            Notify(); return true;
        }

        public void SetActiveSlot(int i)
        {
            if (hotbarSize <= 0) return;
            i = ((i % hotbarSize) + hotbarSize) % hotbarSize;
            if (i == ActiveSlot) return;
            ActiveSlot = i; ActiveSlotChanged?.Invoke(i);
            if (raiseGameEvents && ActiveItem != null) GameEvents.Raise(GameEventType.ItemEquipped, ActiveItem.id, 1, transform.position);
        }

        /// <summary>uses durability of the active tool; breaks it at 0. Returns true if it broke.</summary>
        public bool WearActive(float amount)
        {
            var s = ActiveStack; if (s == null || !s.item.HasDurability) return false;
            s.durability -= amount;
            if (s.durability <= 0f)
            {
                var id = s.item.id; Slots[ActiveSlot] = null; Notify();
                if (raiseGameEvents) GameEvents.Raise(GameEventType.ItemRemoved, id, 1, transform.position);
                ActiveSlotChanged?.Invoke(ActiveSlot);
                return true;
            }
            Changed?.Invoke();
            return false;
        }

        public void Clear() { for (int i = 0; i < Slots.Length; i++) Slots[i] = null; Notify(); ActiveSlotChanged?.Invoke(ActiveSlot); }

        /// <summary>used by the save system: put a stack straight into a slot</summary>
        public void SetSlot(int i, ItemStack s) { if (i >= 0 && i < Slots.Length) { Slots[i] = s; Notify(); } }
        public void ForceNotify() => Notify();
    }

    public static class ItemStackExt
    {
        public static bool IsEmptyOrNull(this ItemStack s) => s == null || s.IsEmpty;
    }
}
