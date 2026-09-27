using System.Collections.Generic;
using UnityEngine;

namespace PrimalFrontier.Audio
{
    /// <summary>id -> clip variants (built by PrimalAudioBuilder from the synthesized WAVs), loaded from Resources/SfxLibrary</summary>
    [CreateAssetMenu(menuName = "Primal Frontier/SFX Library")]
    public class SfxLibrary : ScriptableObject
    {
        [System.Serializable] public struct Entry { public SfxId id; public AudioClip[] clips; [Range(0, 1)] public float volume; public float pitchJitter; public float maxDistance; }
        public List<Entry> entries = new List<Entry>();
    }

}
