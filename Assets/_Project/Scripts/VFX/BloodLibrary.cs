using UnityEngine;

namespace PrimalFrontier.VFX
{
    /// <summary>materials for blood splats / pools (built by PrimalVfxBuilder into Resources/BloodLibrary)</summary>
    [CreateAssetMenu(menuName = "Primal Frontier/Blood Library")]
    public class BloodLibrary : ScriptableObject
    {
        public Material[] splats;
        public Material pool;
    }
}
