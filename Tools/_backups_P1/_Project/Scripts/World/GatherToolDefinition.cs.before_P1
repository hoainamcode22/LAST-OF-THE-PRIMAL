using System;
using UnityEngine;
using PrimalFrontier.Items;

namespace PrimalFrontier.World
{
    /// <summary>units one action gives on one resource category (inclusive range when both ends are whole numbers)</summary>
    [Serializable]
    public struct ToolEfficiency
    {
        public ResourceCategory category;
        [Min(0)] public float min;
        [Min(0)] public float max;
        [Tooltip("action speed (1 = normal): divides the gather time")] [Min(0.1f)] public float speed;
        public ToolEfficiency(ResourceCategory c, float min, float max, float speed = 1f) { category = c; this.min = min; this.max = max; this.speed = speed; }
    }

    /// <summary>
    /// Gathering side of a tool (the item itself, its durability pool and hand model stay on ItemDefinition): tool kinds,
    /// efficiency per resource category and durability cost per action. Bare hands are one of these (item empty).
    /// Created by PrimalResourceBuilder (Data/Resources/TOOL_*.asset); a tool item without one gets numbers derived from
    /// its ToolKind flags and toolPower.
    /// </summary>
    [CreateAssetMenu(menuName = "Primal Frontier/Gather Tool Definition", fileName = "TOOL_New")]
    public class GatherToolDefinition : ScriptableObject
    {
        public string id = "hands";
        public string displayName = "Bare hands";
        [Tooltip("the inventory item (empty = bare hands)")] public ItemDefinition item;
        [Tooltip("what it counts as for a resource's bestTool / requiredTool (normally the item's ToolKind flags)")] public ToolKind toolKind;
        public ToolEfficiency[] efficiency = new ToolEfficiency[0];
        [Tooltip("durability cost per gather action, x the resource's toolWear (the pool is the item's maxDurability)")] [Min(0)] public float wearPerAction = 1f;
        [Tooltip("tier for later tools (0 = stone age)")] public int level;

        public bool TryGet(ResourceCategory c, out ToolEfficiency e)
        {
            if (efficiency != null)
                for (int i = 0; i < efficiency.Length; i++) if (efficiency[i].category == c) { e = efficiency[i]; if (e.speed <= 0f) e.speed = 1f; return true; }
            e = default; return false;
        }
    }
}
