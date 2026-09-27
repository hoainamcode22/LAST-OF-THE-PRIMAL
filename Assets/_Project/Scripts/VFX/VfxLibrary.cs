using System.Collections.Generic;
using UnityEngine;

namespace PrimalFrontier.VFX
{
    /// <summary>id -> prefab table, loaded from Resources/VfxLibrary (built by PrimalVfxBuilder)</summary>
    [CreateAssetMenu(menuName = "Primal Frontier/VFX Library")]
    public class VfxLibrary : ScriptableObject
    {
        [System.Serializable] public struct Entry { public VfxId id; public GameObject prefab; public int prewarm; }
        public List<Entry> entries = new List<Entry>();
    }

}
