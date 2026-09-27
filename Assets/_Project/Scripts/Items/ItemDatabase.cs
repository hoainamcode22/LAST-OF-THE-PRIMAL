using System.Collections.Generic;
using UnityEngine;

namespace PrimalFrontier.Items
{
    /// <summary>All items and recipes (Resources/ItemDatabase). Lookups by id for save files.</summary>
    [CreateAssetMenu(menuName = "Primal Frontier/Item Database", fileName = "ItemDatabase")]
    public class ItemDatabase : ScriptableObject
    {
        public List<ItemDefinition> items = new List<ItemDefinition>();
        public List<RecipeDefinition> recipes = new List<RecipeDefinition>();

        Dictionary<string, ItemDefinition> _items; Dictionary<string, RecipeDefinition> _recipes;
        static ItemDatabase _inst;
        public static ItemDatabase Instance
        {
            get
            {
                if (_inst == null) _inst = Resources.Load<ItemDatabase>("ItemDatabase");
                return _inst;
            }
        }

        void Index()
        {
            _items = new Dictionary<string, ItemDefinition>(); _recipes = new Dictionary<string, RecipeDefinition>();
            foreach (var i in items) if (i) _items[i.id] = i;
            foreach (var r in recipes) if (r) _recipes[r.id] = r;
        }
        public ItemDefinition Item(string id) { if (_items == null) Index(); return id != null && _items.TryGetValue(id, out var i) ? i : null; }
        public RecipeDefinition Recipe(string id) { if (_recipes == null) Index(); return id != null && _recipes.TryGetValue(id, out var r) ? r : null; }
        void OnEnable() { _items = null; _recipes = null; }
    }
}
