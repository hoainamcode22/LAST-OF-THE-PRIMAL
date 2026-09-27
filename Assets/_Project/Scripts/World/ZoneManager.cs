using System;
using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Core;

namespace PrimalFrontier.World
{
    /// <summary>
    /// Named areas of the island (beach, wreck, pond, meadow, cave, habitats) as simple circles from the Blender
    /// markers. Raises ZoneEntered when the player walks in; the cave also counts as "indoors" (cold, no rain).
    /// </summary>
    public class ZoneManager : MonoBehaviour
    {
        [Serializable] public class Zone { public string id; public string displayName; public Vector3 center; public float radius = 20f; public bool indoor; public float temperatureOffset; }
        public List<Zone> zones = new List<Zone>();
        public static ZoneManager Instance { get; private set; }
        public Zone Current { get; private set; }
        readonly HashSet<string> _inside = new HashSet<string>();
        readonly HashSet<string> _visited = new HashSet<string>();
        public IEnumerable<string> Visited => _visited;
        float _next;

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (Time.time < _next) return; _next = Time.time + 0.4f;
            var pp = PlayerLocator.Position; if (!pp.HasValue) return;
            var p = pp.Value; Zone best = null; float bestR = float.MaxValue;
            foreach (var z in zones)
            {
                var d = p - z.center; d.y = 0;
                bool inside = d.sqrMagnitude <= z.radius * z.radius;
                if (inside && z.radius < bestR) { best = z; bestR = z.radius; }
                if (inside && _inside.Add(z.id))
                {
                    bool first = _visited.Add(z.id);
                    GameEvents.Raise(GameEventType.ZoneEntered, z.id, first ? 1 : 0, p);
                }
                else if (!inside) _inside.Remove(z.id);
            }
            Current = best;
        }

        public Zone Find(string id) => zones.Find(z => z.id == id);
        public bool IsIndoor(Vector3 p)
        {
            foreach (var z in zones) { if (!z.indoor) continue; var d = p - z.center; d.y = 0; if (d.sqrMagnitude <= z.radius * z.radius) return true; }
            return false;
        }
        public float TemperatureOffset(Vector3 p)
        {
            float o = 0f;
            foreach (var z in zones) { if (z.temperatureOffset == 0f) continue; var d = p - z.center; d.y = 0; if (d.sqrMagnitude <= z.radius * z.radius) o += z.temperatureOffset; }
            return o;
        }
        public void SetVisited(IEnumerable<string> ids) { _visited.Clear(); foreach (var i in ids) _visited.Add(i); _inside.Clear(); }
    }
}
