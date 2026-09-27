using System;
using UnityEngine;

namespace PrimalFrontier.Items
{
    [Serializable]
    public struct Ingredient
    {
        public ItemDefinition item;
        [Min(1)] public int count;
        public Ingredient(ItemDefinition i, int c) { item = i; count = c; }
    }

    /// <summary>What goes in, what comes out, how long it takes and where it can be made.</summary>
    [CreateAssetMenu(menuName = "Primal Frontier/Recipe Definition", fileName = "RCP_New")]
    public class RecipeDefinition : ScriptableObject
    {
        public string id = "recipe";
        public RecipeCategory category;
        public ItemDefinition output;
        [Min(1)] public int outputCount = 1;
        public Ingredient[] ingredients = Array.Empty<Ingredient>();
        [Min(0.1f)] public float craftSeconds = 3f;
        public CraftStation station;
        [Tooltip("known from the start; otherwise unlocked by picking up one of its ingredients / a journal discovery")]
        public bool knownAtStart = true;
        [TextArea(1, 3)] public string hint;
    }
}
