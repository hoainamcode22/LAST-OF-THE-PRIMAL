using System;

namespace PrimalFrontier.Items
{
    /// <summary>A slot's content. Durability (tools) and water charges (containers) make a stack unique (maxStack 1).</summary>
    [Serializable]
    public class ItemStack
    {
        public ItemDefinition item;
        public int count;
        public float durability;
        public int water;
        /// <summary>what the water charges are (salt / dirty / clean); None when empty</summary>
        public WaterType waterType;
        /// <summary>GameClock time the food in this stack was made (count-weighted average when stacks merge). Only items with
        /// ItemDefinition.spoilHours &gt; 0 use it (Survival/Spoilage); saved as an age so it survives the clock being restored.</summary>
        public double madeAt;
        /// <summary>GameClock time until which the water in this container is Hot (just boiled / heated); at or before now = Cold.
        /// Rules and the cooling time: Survival/WaterRules + SurvivalConfig.hotWaterCoolSeconds. Saved as seconds left (old saves: cold).</summary>
        public double hotUntil;
        /// <summary>compatibility view of <see cref="waterType"/>: pond / stream water that was not boiled</summary>
        public bool dirty
        {
            get => water > 0 && waterType == WaterType.DirtyWater;
            set { if (value) waterType = WaterType.DirtyWater; else if (waterType == WaterType.DirtyWater) waterType = water > 0 ? WaterType.CleanWater : WaterType.None; }
        }

        public ItemStack(ItemDefinition item, int count)
        {
            this.item = item; this.count = count;
            durability = item != null ? item.maxDurability : 0f;
            water = 0;
            madeAt = Core.GameClock.Now;
        }
        /// <summary>seconds of game time since the food was made (0 or more)</summary>
        public float AgeSeconds => (float)Math.Max(0.0, Core.GameClock.Now - madeAt);
        /// <summary>call before adding <paramref name="n"/> units made at <paramref name="otherMadeAt"/> to this stack: the stack's age becomes the count-weighted average</summary>
        public void MergeAge(double otherMadeAt, int n)
        {
            if (n <= 0 || item == null || item.spoilHours <= 0f) return;
            int total = Math.Max(0, count) + n;
            madeAt = total > 0 ? (madeAt * Math.Max(0, count) + otherMadeAt * n) / total : otherMadeAt;
        }
        public bool IsEmpty => item == null || count <= 0;
        public float Weight => IsEmpty ? 0f : item.weight * count;
        public ItemStack Clone() => new ItemStack(item, count) { durability = durability, water = water, waterType = waterType, madeAt = madeAt, hotUntil = hotUntil };
        public bool CanMergeWith(ItemStack o) => o != null && !IsEmpty && !o.IsEmpty && o.item == item && item.maxStack > 1;
    }
}
