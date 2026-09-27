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

        public ItemStack(ItemDefinition item, int count)
        {
            this.item = item; this.count = count;
            durability = item != null ? item.maxDurability : 0f;
            water = 0;
        }
        public bool IsEmpty => item == null || count <= 0;
        public float Weight => IsEmpty ? 0f : item.weight * count;
        public ItemStack Clone() => new ItemStack(item, count) { durability = durability, water = water };
        public bool CanMergeWith(ItemStack o) => o != null && !IsEmpty && !o.IsEmpty && o.item == item && item.maxStack > 1;
    }
}
