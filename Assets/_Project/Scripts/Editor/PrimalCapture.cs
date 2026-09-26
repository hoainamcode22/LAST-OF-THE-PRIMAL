using System.IO;
using System.Linq;
using PrimalFrontier.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// Renders review screenshots of the island scene to Documentation/Screenshots (works in batchmode with a GPU).
    /// Viewpoints are given in Blender coordinates (same as the level layout) and converted with BlenderSpace.
    /// </summary>
    public static class PrimalCapture
    {
        const string ScenePath = "Assets/_Project/Scenes/Island_VerticalSlice.unity";

        struct View { public string name; public Vector3 eyeB; public Vector3 targetB; public float fov; public bool groundRelative; }

        static readonly View[] Views =
        {
            new View { name = "01_spawn_to_wreck", eyeB = new Vector3(16, -206, 1.7f), targetB = new Vector3(46, -226, 1.5f), fov = 60, groundRelative = true },
            new View { name = "02_wreck_close", eyeB = new Vector3(36, -210, 2.2f), targetB = new Vector3(46, -224, 1.0f), fov = 60, groundRelative = true },
            new View { name = "03_beach_to_island", eyeB = new Vector3(10, -232, 14f), targetB = new Vector3(-10, -120, 8f), fov = 60, groundRelative = true },
            new View { name = "04_camp_forest_edge", eyeB = new Vector3(-18, -190, 1.7f), targetB = new Vector3(-60, -140, 6f), fov = 60, groundRelative = true },
            new View { name = "05_pond", eyeB = new Vector3(-20, -88, 2.0f), targetB = new Vector3(-40, -60, 0f), fov = 60, groundRelative = true },
            new View { name = "06_meadow_to_cliff", eyeB = new Vector3(5, 0, 2.0f), targetB = new Vector3(18, 140, 25f), fov = 55, groundRelative = true },
            new View { name = "07_cave_mouth", eyeB = new Vector3(24, 105, 2.0f), targetB = new Vector3(18, 134, 20f), fov = 60, groundRelative = true },
            new View { name = "08_forest_interior", eyeB = new Vector3(-140, -40, 1.7f), targetB = new Vector3(-120, 0, 4f), fov = 65, groundRelative = true },
            new View { name = "09_aerial", eyeB = new Vector3(0, -520, 260f), targetB = new Vector3(0, -20, 0f), fov = 50, groundRelative = false },
        };

        [MenuItem("Primal Frontier/Capture Review Screenshots")]
        public static void CaptureMenu() => Capture();

        public static void CaptureFromCommandLine()
        {
            int code = 0;
            try { Capture(); } catch (System.Exception e) { Debug.LogError("[PrimalCapture] " + e); code = 1; }
            EditorApplication.Exit(code);
        }

        public static void Capture()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var terrain = Terrain.activeTerrain;
            if (terrain != null)
            {
                var td = terrain.terrainData;
                foreach (var (label, bx, by) in new[] { ("meadow", 0f, 20f), ("forest", -140f, -40f), ("beach", 0f, -205f) })
                {
                    var u = BlenderSpace.ToUnityPosition(bx, by, 0) - terrain.transform.position;
                    int ax = Mathf.Clamp(Mathf.RoundToInt(u.x / td.size.x * (td.alphamapWidth - 1)), 0, td.alphamapWidth - 1);
                    int az = Mathf.Clamp(Mathf.RoundToInt(u.z / td.size.z * (td.alphamapHeight - 1)), 0, td.alphamapHeight - 1);
                    var w = td.GetAlphamaps(ax, az, 1, 1);
                    var sb = new System.Text.StringBuilder();
                    for (int l = 0; l < td.alphamapLayers; l++) sb.Append($"{td.terrainLayers[l].name}={w[0, 0, l]:F2} ");
                    Debug.Log($"[PrimalCapture] VERIFY splat {label}: {sb}");
                }
                int total = 0;
                for (int l = 0; l < td.detailPrototypes.Length; l++)
                    total += td.GetDetailLayer(0, 0, td.detailWidth, td.detailHeight, l).Cast<int>().Sum();
                Debug.Log($"[PrimalCapture] VERIFY trees={td.treeInstanceCount} detail instances={total} alphamapTextures={td.alphamapTextureCount}");
            }
            var cam = Camera.main;
            if (cam == null) { Debug.LogError("[PrimalCapture] no main camera"); return; }
            Directory.CreateDirectory("Documentation/Screenshots");
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            foreach (var v in Views)
            {
                var eye = BlenderSpace.ToUnityPosition(v.eyeB.x, v.eyeB.y, 0);
                var tgt = BlenderSpace.ToUnityPosition(v.targetB.x, v.targetB.y, 0);
                if (v.groundRelative && terrain != null)
                {
                    eye.y = terrain.SampleHeight(eye) + terrain.transform.position.y + v.eyeB.z;
                    tgt.y = terrain.SampleHeight(tgt) + terrain.transform.position.y + v.targetB.z;
                }
                else { eye.y = v.eyeB.z; tgt.y = v.targetB.z; }
                cam.transform.position = eye;
                cam.transform.rotation = Quaternion.LookRotation((tgt - eye).normalized);
                cam.fieldOfView = v.fov;
                cam.targetTexture = rt;
                cam.Render();
                cam.Render();   // second pass lets temporal effects / shadow cascades settle
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                cam.targetTexture = null;
                File.WriteAllBytes($"Documentation/Screenshots/{v.name}.png", tex.EncodeToPNG());
                Debug.Log($"[PrimalCapture] {v.name} eye={eye}");
            }
            Object.DestroyImmediate(rt);
        }
    }
}
