using UnityEngine;

namespace PrimalFrontier.AI
{
    /// <summary>
    /// Materials the wildlife layer draws with (tracking signs, droppings, bones, bird flocks). PrimalWildlifeBuilder saves
    /// one in Resources/WildlifeArt with baked textures (so the shaders ship in a build); without it TrackArt makes the
    /// same materials at runtime.
    /// </summary>
    public class WildlifeArt : ScriptableObject
    {
        public Material decals, dung, dungOld, bones, birds;
    }
}
