using System.Collections.Generic;
using UnityEditor.Animations;
using UnityEngine;

namespace PrimalFrontier.EditorTools
{
    public static partial class PrimalCharacterBuilder
    {
        // Implemented in phase B (Triceratops) - reusable dinosaur Animator + colliders.
        static AnimatorController BuildDinoController(Spec spec, List<AnimationClip> clips, AnimMeta meta) { F("dinosaur controller not implemented yet"); return null; }
        static void AddDinoColliders(GameObject go, Bounds b) { }
        static void TestDinoColliders(GameObject go, Bounds b) { }
    }
}
