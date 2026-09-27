using System.Collections.Generic;
using UnityEngine;

namespace PrimalFrontier.Building
{
    /// <summary>Marks a player-built object for the save file (which item placed it, unique id).</summary>
    public class PlacedStructure : MonoBehaviour
    {
        public string itemId;
        public string uid;
        public static readonly List<PlacedStructure> All = new List<PlacedStructure>();
        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }
        public static void DestroyAll()
        {
            foreach (var s in All.ToArray()) if (s) Destroy(s.gameObject);
            All.Clear();
        }
    }
}
