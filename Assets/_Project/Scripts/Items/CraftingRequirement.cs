using System;
using UnityEngine;

namespace PrimalFrontier.Items
{
    /// <summary>
    /// Extra condition on a recipe, checked after the materials (<see cref="CraftingSystem.Check"/>). Every field is
    /// optional: None / 0 means no condition. All requirements of a recipe must pass.
    /// </summary>
    [Serializable]
    public struct CraftingRequirement
    {
        [Tooltip("a tool that must be in the pack (not used up). Several flags = any one of them (Hammer | Cut: a hammer or a knife)")]
        public ToolKind tool;
        [Tooltip("a station that must be near while queueing and while the job runs")]
        public CraftStation nearStation;
        [Tooltip("the recipe stays locked before this day (0 = always)")] [Min(0)]
        public int minDay;

        public CraftingRequirement(ToolKind tool, CraftStation nearStation = CraftStation.None, int minDay = 0)
        { this.tool = tool; this.nearStation = nearStation; this.minDay = minDay; }
    }

    /// <summary>Extra output of a recipe beyond its main one (for example butchering scraps). Optional.</summary>
    [Serializable]
    public struct CraftingResult
    {
        public ItemDefinition item;
        [Min(1)] public int count;

        public CraftingResult(ItemDefinition item, int count) { this.item = item; this.count = count; }
    }
}
