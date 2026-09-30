namespace PrimalFrontier.Items
{
    public enum ItemCategory { Resource, Food, Tool, Weapon, Ammo, Survival, Structure }
    /// <summary>What a held tool is good at. A tool may have several (Hand Stone chops and breaks, poorly).</summary>
    [System.Flags] public enum ToolKind { None = 0, Chop = 1, Mine = 2, Cut = 4, Hammer = 8, Light = 16 }
    public enum WeaponKind { None, Spear, Bow, Knife, Sword }     // appended only: stored as ints in the item assets (Sword = 4)
    public enum CraftStation { None, Campfire }
    public enum RecipeCategory { Tools, Weapons, Survival, Food, Structures, Water, Resources }   // appended only (stored as ints); Structures shows as BUILDING
    /// <summary>what is inside a water container (saved as int: append only). Rules and numbers: Survival/WaterRules + SurvivalConfig</summary>
    public enum WaterType { None, SaltWater, DirtyWater, CleanWater }
}
