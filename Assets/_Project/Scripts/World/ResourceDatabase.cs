using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Items;

namespace PrimalFrontier.World
{
    /// <summary>
    /// Resources/ResourceDatabase: every ResourceDefinition and GatherToolDefinition (PrimalResourceBuilder keeps it
    /// up to date). Tool lookup by item; a tool item with no definition gets one derived from its ToolKind flags and
    /// toolPower, so a new tool from the crafting data still gathers sensibly.
    /// </summary>
    [CreateAssetMenu(menuName = "Primal Frontier/Resource Database", fileName = "ResourceDatabase")]
    public class ResourceDatabase : ScriptableObject
    {
        public List<ResourceDefinition> resources = new List<ResourceDefinition>();
        [Tooltip("bare hands (the default when nothing fitting is held)")] public GatherToolDefinition hands;
        public List<GatherToolDefinition> tools = new List<GatherToolDefinition>();
        [Tooltip("green crop material for berries / fruit that are growing back")] public Material unripeMaterial;

        static ResourceDatabase _inst;
        public static ResourceDatabase Instance
        {
            get
            {
                if (_inst == null)
                {
                    _inst = Resources.Load<ResourceDatabase>("ResourceDatabase");
                    if (_inst == null) { _inst = CreateInstance<ResourceDatabase>(); _inst.name = "ResourceDatabase (default)"; _inst.hideFlags = HideFlags.DontSave; }
                }
                return _inst;
            }
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() { _inst = null; }

        readonly Dictionary<ItemDefinition, GatherToolDefinition> _byItem = new Dictionary<ItemDefinition, GatherToolDefinition>();
        GatherToolDefinition _defaultHands;
        void OnEnable() { _byItem.Clear(); _defaultHands = null; }

        public ResourceDefinition Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var r in resources) if (r && r.id == id) return r;
            return null;
        }

        public GatherToolDefinition Hands => hands ? hands : _defaultHands ??= DefaultHands();

        /// <summary>the gathering data of a held item; null when it is no tool at all (the hands work then)</summary>
        public GatherToolDefinition ToolFor(ItemDefinition item)
        {
            if (item == null) return null;
            if (_byItem.TryGetValue(item, out var t) && t) return t;
            t = null;
            foreach (var d in tools) if (d && (d.item == item || (d.item == null && d.id == item.id))) { t = d; break; }
            if (t == null && item.tool != ToolKind.None) t = Derived(item);
            if (t != null) _byItem[item] = t;
            return t;
        }

        /// <summary>bare hands: 1 per action on stone and wood, 1-2 on plants and food</summary>
        public static GatherToolDefinition DefaultHands()
        {
            var h = CreateInstance<GatherToolDefinition>(); h.hideFlags = HideFlags.DontSave; h.id = "hands"; h.displayName = "Bare hands"; h.wearPerAction = 0f;
            h.efficiency = new[]
            {
                new ToolEfficiency(ResourceCategory.Stone, 1, 1), new ToolEfficiency(ResourceCategory.Wood, 1, 1),
                new ToolEfficiency(ResourceCategory.Fiber, 1, 2), new ToolEfficiency(ResourceCategory.Food, 1, 2),
                new ToolEfficiency(ResourceCategory.Fish, 1, 1), new ToolEfficiency(ResourceCategory.Rare, 1, 1),
            };
            return h;
        }

        /// <summary>numbers for a tool item without a definition: Mine 3-5 stone, Chop 2-3 wood, Cut 2-3 fibre / food, Hammer 2-3 stone (x toolPower)</summary>
        public static GatherToolDefinition Derived(ItemDefinition item)
        {
            var d = CreateInstance<GatherToolDefinition>(); d.hideFlags = HideFlags.DontSave;
            d.id = item.id; d.displayName = item.displayName; d.item = item; d.toolKind = item.tool; d.wearPerAction = 1f;
            float p = Mathf.Max(0.1f, item.toolPower);
            var list = new List<ToolEfficiency>();
            if ((item.tool & ToolKind.Mine) != 0) list.Add(new ToolEfficiency(ResourceCategory.Stone, 3 * p, 5 * p));
            else if ((item.tool & ToolKind.Hammer) != 0) list.Add(new ToolEfficiency(ResourceCategory.Stone, 2 * p, 3 * p));
            if ((item.tool & ToolKind.Chop) != 0) list.Add(new ToolEfficiency(ResourceCategory.Wood, 2 * p, 3 * p));
            if ((item.tool & ToolKind.Cut) != 0) { list.Add(new ToolEfficiency(ResourceCategory.Fiber, 2 * p, 3 * p)); list.Add(new ToolEfficiency(ResourceCategory.Food, 2 * p, 3 * p)); }
            d.efficiency = list.ToArray();
            return d;
        }
    }
}
