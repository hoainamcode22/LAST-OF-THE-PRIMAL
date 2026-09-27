using UnityEngine;

namespace PrimalFrontier.VFX
{
    public enum Surface { Dirt, Sand, Grass, Rock, Mud, Wood, Water }

    /// <summary>What is under a point: terrain layer (dominant splat) or collider material / name keywords.</summary>
    public static class SurfaceDetector
    {
        static readonly RaycastHit[] Hits = new RaycastHit[4];

        public static Surface At(Vector3 pos, int mask = ~0)
        {
            int n = Physics.RaycastNonAlloc(pos + Vector3.up * 0.4f, Vector3.down, Hits, 1.2f, mask, QueryTriggerInteraction.Collide);
            Surface best = Surface.Dirt; float bestD = float.MaxValue; bool water = false;
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i];
                if (h.collider.isTrigger)
                {
                    if (h.collider.CompareTag("Water") || h.collider.name.Contains("Water")) water = true;
                    continue;
                }
                if (h.distance < bestD) { bestD = h.distance; best = Classify(h); }
            }
            return water ? Surface.Water : best;
        }

        public static Surface Classify(RaycastHit h)
        {
            if (h.collider is TerrainCollider tc && tc.terrainData != null) return FromTerrain(tc.GetComponent<Terrain>(), h.point);
            string k = (h.collider.sharedMaterial ? h.collider.sharedMaterial.name : "") + " " + h.collider.name;
            return FromName(k);
        }

        public static Surface FromName(string k)
        {
            k = k.ToLowerInvariant();
            if (k.Contains("sand") || k.Contains("beach")) return Surface.Sand;
            if (k.Contains("mud")) return Surface.Mud;
            if (k.Contains("rock") || k.Contains("stone") || k.Contains("cliff") || k.Contains("cave") || k.Contains("boulder")) return Surface.Rock;
            if (k.Contains("wood") || k.Contains("plank") || k.Contains("wreck") || k.Contains("hull") || k.Contains("log") || k.Contains("deck")) return Surface.Wood;
            if (k.Contains("grass") || k.Contains("moss") || k.Contains("fern")) return Surface.Grass;
            if (k.Contains("water")) return Surface.Water;
            return Surface.Dirt;
        }

        static Surface FromTerrain(Terrain t, Vector3 world)
        {
            if (t == null) return Surface.Dirt;
            var d = t.terrainData; if (d.alphamapLayers == 0) return Surface.Dirt;
            Vector3 p = world - t.transform.position;
            int x = Mathf.Clamp((int)(p.x / d.size.x * d.alphamapWidth), 0, d.alphamapWidth - 1);
            int z = Mathf.Clamp((int)(p.z / d.size.z * d.alphamapHeight), 0, d.alphamapHeight - 1);
            var a = d.GetAlphamaps(x, z, 1, 1);
            int best = 0; for (int i = 1; i < d.alphamapLayers; i++) if (a[0, 0, i] > a[0, 0, best]) best = i;
            var layer = d.terrainLayers != null && best < d.terrainLayers.Length ? d.terrainLayers[best] : null;
            return layer ? FromName(layer.name + " " + (layer.diffuseTexture ? layer.diffuseTexture.name : "")) : Surface.Dirt;
        }
    }
}
