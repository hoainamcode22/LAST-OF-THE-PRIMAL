using UnityEngine;

namespace PrimalFrontier.Core
{
    /// <summary>terrain cost per quality preset (Low / Medium / High / Ultra): mesh error, grass distance / density, tree distances</summary>
    public static class TerrainQuality
    {
        static readonly float[] PixelError = { 10f, 7f, 5f, 3f };
        static readonly float[] DetailDist = { 35f, 55f, 80f, 110f };
        static readonly float[] DetailDensity = { 0.4f, 0.6f, 0.85f, 1f };
        static readonly float[] TreeDist = { 450f, 700f, 1000f, 1500f };
        static readonly float[] Billboard = { 60f, 90f, 130f, 180f };
        static readonly float[] BaseMap = { 250f, 400f, 700f, 1000f };

        public static void Apply(int q)
        {
            q = Mathf.Clamp(q, 0, 3);
            foreach (var t in Terrain.activeTerrains)
            {
                if (!t) continue;
                t.heightmapPixelError = PixelError[q]; t.detailObjectDistance = DetailDist[q]; t.detailObjectDensity = DetailDensity[q];
                t.treeDistance = TreeDist[q]; t.treeBillboardDistance = Billboard[q]; t.basemapDistance = BaseMap[q];
                t.treeCrossFadeLength = q == 0 ? 0f : 5f;
                t.drawInstanced = true;
            }
        }
    }
}
