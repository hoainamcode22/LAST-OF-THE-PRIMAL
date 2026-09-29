using UnityEngine;
using PrimalFrontier.Items;

namespace PrimalFrontier.World
{
    /// <summary>how one gather action on a resource works with what the player holds</summary>
    public struct GatherPlan
    {
        /// <summary>false: this cannot be worked with what is in hand (prompt greyed)</summary>
        public bool allowed;
        /// <summary>bare hands (nothing fitting held)</summary>
        public bool byHand;
        public GatherToolDefinition tool;
        /// <summary>the held item doing the work (wears), null by hand</summary>
        public ItemDefinition toolItem;
        /// <summary>units per action</summary>
        public float min, max;
        public float speed;
        /// <summary>true when the hands work at the low rate because the required tool is missing</summary>
        public bool slowByHand;
    }

    /// <summary>
    /// The gathering rules in one place (tool choice, efficiency, whole units from fractional work). Used by ResourceNode,
    /// TreeHarvest and the bare-hand strikes, so hands / pick / axe / knife behave the same everywhere.
    /// </summary>
    public static class GatheringSystem
    {
        public static GatherPlan Plan(ResourceDefinition def, ItemDefinition held)
        {
            var plan = new GatherPlan { speed = 1f };
            if (def == null) return plan;
            var db = ResourceDatabase.Instance;
            var tool = held && held.tool != ToolKind.None ? db.ToolFor(held) : null;
            ToolKind kinds = tool ? tool.toolKind | held.tool : ToolKind.None;
            if (tool && def.bestTool != ToolKind.None && (kinds & def.bestTool) != 0 && tool.TryGet(def.category, out var e))
            {
                plan.allowed = true; plan.tool = tool; plan.toolItem = held;
                plan.min = e.min * def.toolFactor; plan.max = e.max * def.toolFactor; plan.speed = e.speed;
                return plan;
            }
            var hands = db.Hands;
            if (!hands.TryGet(def.category, out var h)) h = new ToolEfficiency(def.category, 1, 1);
            bool lacks = def.requiredTool != ToolKind.None && (kinds & def.requiredTool) == 0;
            plan.byHand = true; plan.tool = hands; plan.slowByHand = lacks;
            plan.min = h.min * def.handsFactor; plan.max = h.max * def.handsFactor; plan.speed = h.speed;
            plan.allowed = def.handsFactor > 0f;
            return plan;
        }

        /// <summary>
        /// whole units from one action of work: a whole-number range rolls inclusive (pick 3-5 gives 3, 4 or 5), a fractional one
        /// adds to progress and pays out once it reaches 1 (0.34 per action = one stone every third action)
        /// </summary>
        public static int Roll(ref float progress, float min, float max)
        {
            if (max < min) { var t = min; min = max; max = t; }
            if (max <= 0f) return 0;
            float w;
            if (Whole(min) && Whole(max) && min >= 1f) w = Random.Range(Mathf.RoundToInt(min), Mathf.RoundToInt(max) + 1);
            else w = max > min ? Random.Range(min, max) : min;
            return Pay(ref progress, w);
        }

        /// <summary>adds work, returns the whole units it completes</summary>
        public static int Pay(ref float progress, float work)
        {
            progress += Mathf.Max(0f, work);
            int n = Mathf.FloorToInt(progress + 1e-4f);
            progress = Mathf.Max(0f, progress - n);
            return n;
        }

        static bool Whole(float v) => Mathf.Abs(v - Mathf.Round(v)) < 1e-4f;

        /// <summary>"a pick", "an axe" ... (same words as the old node prompts)</summary>
        public static string ToolName(ToolKind k) => k switch
        {
            ToolKind.Chop => "an axe", ToolKind.Mine => "a pick", ToolKind.Cut => "a knife", ToolKind.Hammer => "a hammer",
            ToolKind.Chop | ToolKind.Mine => "a stone tool", _ => "a tool"
        };
    }
}
