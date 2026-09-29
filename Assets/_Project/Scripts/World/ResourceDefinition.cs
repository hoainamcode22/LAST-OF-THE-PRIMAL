using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Audio;
using PrimalFrontier.Items;
using PrimalFrontier.VFX;

namespace PrimalFrontier.World
{
    /// <summary>what a node gives; picks the tool efficiency row (stored as ints in the assets: append only)</summary>
    public enum ResourceCategory { Stone, Wood, Fiber, Food, Fish }
    /// <summary>node size class (Small stones 2-4, Medium 4-8, Large 8-15; Huge = the big boulders)</summary>
    public enum ResourceSize { Small, Medium, Large, Huge }
    /// <summary>how an emptied node looks until it is back: Hide (piles, plants: gone with a puff), Rubble (shrinks and sinks),
    /// Stripped (bushes: stay, smaller, no berries), Mined (big boulders: stay, settle a little)</summary>
    public enum DepletedLook { Hide, Rubble, Stripped, Mined }

    /// <summary>
    /// One kind of gatherable node (Resource_Stone_Small, Resource_Wood_Branch ...). Every ResourceNode points at one;
    /// yields, tools, respawn and feedback are tuned here, never on the node. Created / updated by
    /// PrimalResourceBuilder (Data/Resources/RES_*.asset, listed in Resources/ResourceDatabase).
    /// </summary>
    [CreateAssetMenu(menuName = "Primal Frontier/Resource Definition", fileName = "RES_New")]
    public class ResourceDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string id = "resource";
        [Tooltip("name on the prompt's second line (Small stones, Fallen branch ...)")] public string displayName = "Resource";
        public ResourceCategory category;
        public ResourceSize size;
        [Tooltip("first prompt line: Gather Stone / Gather Wood / Gather Fiber / Harvest")] public string prompt = "Gather";

        [Header("Yield")]
        public ItemDefinition item;
        [Tooltip("units in a full node: the builder rolls each node's amount in this range (inclusive)")] public Vector2Int yieldRange = new Vector2Int(2, 4);
        public ItemDefinition bonusItem;
        [Range(0, 1)] public float bonusChance;

        [Header("Gathering")]
        [Tooltip("tool kind whose efficiency applies (Mine = pick on stone, Chop = axe on wood, Cut = knife on fibre / food)")] public ToolKind bestTool;
        [Tooltip("without this tool kind the hands only get the low handsFactor rate and the hint (None = hands are fine)")] public ToolKind requiredTool;
        [Tooltip("bare-hand units per action x the hands efficiency: 1 = one stone per action, 0.34 = one every third action, 0 = hands cannot")]
        [Min(0)] public float handsFactor = 1f;
        [Tooltip("multiplies a fitting tool's efficiency")] [Min(0)] public float toolFactor = 1f;
        [Tooltip("s per action when the clip has no gather event (the tool's speed divides it)")] [Min(0.2f)] public float gatherSeconds = 1.25f;
        [Tooltip("PlayerActions id by hand / with the fitting tool")] public int handAction = PlayerActions.GatherPlant;
        public int toolAction = PlayerActions.GatherPlant;
        [Tooltip("durability a tool loses per action (x the tool's wearPerAction)")] [Min(0)] public float toolWear = 1f;
        [Tooltip("second prompt line while working it by hand without the required tool")] public string handHint;

        [Header("Bare-hand strikes (punches, IDamageable)")]
        public bool punchable = true;
        [Tooltip("punch damage per unit, before handsFactor: a 4-damage jab on small stones = 1 stone")] [Min(0.1f)] public float damagePerUnit = 4f;

        [Header("Respawn and looks")]
        [Tooltip("in-game hours until an emptied node is back (never instant)")] [Min(0.5f)] public float respawnHours = 24f;
        public DepletedLook depletedLook = DepletedLook.Hide;
        [Tooltip("scale when nearly used up (damaged state)")] [Range(0.3f, 1f)] public float damagedScale = 0.75f;
        [Tooltip("scale of an emptied Rubble / Stripped / Mined node")] [Range(0.2f, 1f)] public float depletedScale = 0.45f;
        [Tooltip("the node sinks this share of its height as it is used up")] [Range(0f, 0.5f)] public float sink = 0.15f;
        [Tooltip("interaction footprint radius (m) when the node sets none")] [Min(0.1f)] public float radius = 0.4f;

        [Header("Feedback (pooled, PlayerFeedback plays them)")]
        public VfxId hitVfx = VfxId.StoneChips;
        public VfxId hitVfx2 = VfxId.DustImpact;
        public SfxId handSfx = SfxId.StoneGatherHand;
        public SfxId toolSfx = SfxId.StoneHit;
        public SfxId depleteSfx = SfxId.None;

        /// <summary>default particles / sounds for the category (stone fragments + dust, wood chips + bark, fibre / fruit leaves)</summary>
        public void FeedbackDefaults()
        {
            switch (category)
            {
                case ResourceCategory.Stone: hitVfx = VfxId.StoneChips; hitVfx2 = VfxId.DustImpact; handSfx = SfxId.StoneGatherHand; toolSfx = SfxId.StoneHit; break;
                case ResourceCategory.Wood: hitVfx = VfxId.WoodChips; hitVfx2 = VfxId.HitDust; handSfx = SfxId.BranchSnap; toolSfx = SfxId.WoodChop; break;
                case ResourceCategory.Fish: hitVfx = VfxId.WaterSplash; hitVfx2 = VfxId.WaterDrops; handSfx = toolSfx = SfxId.WaterSplash; break;
                default: hitVfx = VfxId.Leaves; hitVfx2 = VfxId.None; handSfx = toolSfx = SfxId.LeafRustle; break;
            }
        }

        /// <summary>the node's full amount for a stable seed (same node, same amount on every run)</summary>
        public int RollAmount(int seed)
        {
            int lo = Mathf.Max(1, Mathf.Min(yieldRange.x, yieldRange.y)), hi = Mathf.Max(lo, Mathf.Max(yieldRange.x, yieldRange.y));
            uint h = (uint)seed * 2654435761u; h ^= h >> 13;
            return lo + (int)(h % (uint)(hi - lo + 1));
        }
    }
}
