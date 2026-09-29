using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.World;

namespace PrimalFrontier.AI
{
    /// <summary>
    /// Where wildlife can drink: points on the fresh water surfaces (WaterSource meshes: pond, stream), sampled about
    /// every 3 m once and cached (rebuilt when the set of water sources changes). The nearest point to a home is the
    /// bank side facing it. Only the build allocates.
    /// </summary>
    public static class DrinkSpots
    {
        static readonly List<Vector3> _pts = new List<Vector3>();
        static int _sources = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _pts.Clear(); _sources = -1; }

        public static int Count { get { Ensure(); return _pts.Count; } }

        static void Ensure()
        {
            var all = WaterSource.All;
            if (all.Count == _sources) return;
            _sources = all.Count; _pts.Clear();
            var seen = new HashSet<Vector2Int>();
            foreach (var w in all)
            {
                if (!w || !w.fresh) continue;
                var mf = w.surface ? w.surface : w.GetComponentInChildren<MeshFilter>();
                if (!mf || !mf.sharedMesh) { if (seen.Add(Key(w.transform.position))) _pts.Add(w.transform.position); continue; }
                var t = mf.transform; var v = mf.sharedMesh.vertices;
                for (int i = 0; i < v.Length; i++) { var p = t.TransformPoint(v[i]); if (seen.Add(Key(p))) _pts.Add(p); }
            }
        }

        static Vector2Int Key(Vector3 p) => new Vector2Int(Mathf.RoundToInt(p.x / 3f), Mathf.RoundToInt(p.z / 3f));

        /// <summary>the fresh water point nearest to 'from' within 'radius' (horizontal)</summary>
        public static bool Nearest(Vector3 from, float radius, out Vector3 spot)
        {
            Ensure(); spot = from; float best = radius * radius; bool any = false;
            for (int i = 0; i < _pts.Count; i++)
            {
                var p = _pts[i]; float dx = p.x - from.x, dz = p.z - from.z, q = dx * dx + dz * dz;
                if (q < best) { best = q; spot = p; any = true; }
            }
            return any;
        }
    }
}
