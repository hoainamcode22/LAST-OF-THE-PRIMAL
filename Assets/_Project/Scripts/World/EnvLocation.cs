using UnityEngine;

namespace PrimalFrontier.World
{
    /// <summary>
    /// A named place of the island (ENV's location markers under Markers/Zones/&lt;id&gt;): id, radius and a short note.
    /// Data only (no Update): ZoneManager, missions, the journal, AI or audio can read it. Ids and positions are listed in
    /// Documentation/PCPhase/LOCATIONS.md. Placed by PrimalEnvironmentBuilder.Markers.
    /// </summary>
    public class EnvLocation : MonoBehaviour
    {
        public string id;
        public float radius = 20f;
        [TextArea(1, 3)] public string note;

        public bool Contains(Vector3 p)
        {
            var d = p - transform.position; d.y = 0f;
            return d.sqrMagnitude <= radius * radius;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.9f, 0.5f, 0.6f);
            const int n = 48; var c = transform.position; var prev = c + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f; var p = c + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                Gizmos.DrawLine(prev, p); prev = p;
            }
        }
    }
}
