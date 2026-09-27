namespace PrimalFrontier.Items
{
    public enum ItemCategory { Resource, Food, Tool, Weapon, Ammo, Survival, Structure }
    /// <summary>What a held tool is good at. A tool may have several (Hand Stone chops and breaks, poorly).</summary>
    [System.Flags] public enum ToolKind { None = 0, Chop = 1, Mine = 2, Cut = 4, Hammer = 8, Light = 16 }
    public enum WeaponKind { None, Spear, Bow, Knife }            // appended only: stored as ints in the item assets
    public enum CraftStation { None, Campfire }
    public enum RecipeCategory { Tools, Weapons, Survival, Food, Structures, Water }   // appended only (stored as ints); Structures shows as BUILDING
}
