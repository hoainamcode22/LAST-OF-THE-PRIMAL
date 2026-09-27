using UnityEngine;

namespace PrimalFrontier
{
    public static class ComponentExt
    {
        /// <summary>GetComponent or AddComponent. Never use "GetComponent() ?? AddComponent()": Unity's fake-null breaks ??.</summary>
        public static T GetOrAdd<T>(this GameObject go) where T : Component { var c = go.GetComponent<T>(); return c ? c : go.AddComponent<T>(); }
    }
}
