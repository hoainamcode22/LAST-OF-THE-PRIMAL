using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// Review screenshots of scene objects without touching the scene: a temporary hidden camera (settings copied
    /// from the main camera) looks at named objects and writes Documentation/Screenshots/Review/&lt;shot&gt;.png.
    /// Bridge arg: "shot:ObjectName:distance:height:yaw[:lookHeight[:fov]]" entries separated by ';'.
    /// Nothing is saved or moved in the scene.
    /// </summary>
    public static class PrimalReviewCapture
    {
        const string ScenePath = "Assets/_Project/Scenes/Island_VerticalSlice.unity";
        const int W = 1280, H = 720;
        static float F(string s) => float.Parse(s.Trim(), CultureInfo.InvariantCulture);

        [PrimalBridgeCommand]
        public static string Shots(string arg)
        {
            var sb = new StringBuilder();
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (scene.isDirty) return "active scene has unsaved changes; open Island_VerticalSlice first";
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            var main = Camera.main;
            var go = new GameObject("_ReviewCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            if (main) cam.CopyFrom(main);
            cam.enabled = false; cam.nearClipPlane = 0.1f; cam.farClipPlane = 1500f;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            Directory.CreateDirectory("Documentation/Screenshots/Review");
            var terrain = Terrain.activeTerrain;
            try
            {
                foreach (var entry in (arg ?? "").Split(';'))
                {
                    var p = entry.Split(':'); if (p.Length < 5) continue;
                    var target = GameObject.Find(p[1].Trim());
                    if (!target) { sb.AppendLine($"{p[0]}: '{p[1]}' not found"); continue; }
                    float dist = F(p[2]), h = F(p[3]), yaw = F(p[4]), look = p.Length > 5 ? F(p[5]) : 1.5f;
                    Vector3 basePos = target.transform.position;
                    Vector3 c = basePos + Vector3.up * look;
                    Vector3 eye = basePos + Quaternion.Euler(0, yaw, 0) * Vector3.back * dist + Vector3.up * h;
                    if (terrain) eye.y = Mathf.Max(eye.y, terrain.SampleHeight(eye) + terrain.transform.position.y + 0.6f);
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation((c - eye).normalized));
                    cam.fieldOfView = p.Length > 6 ? F(p[6]) : 55f;
                    cam.targetTexture = rt; cam.Render(); cam.Render();
                    var prev = RenderTexture.active; RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
                    RenderTexture.active = prev; cam.targetTexture = null;
                    File.WriteAllBytes($"Documentation/Screenshots/Review/{p[0].Trim()}.png", tex.EncodeToPNG());
                    sb.AppendLine($"{p[0]}: {p[1]} at {basePos}, eye {eye}");
                }
            }
            finally
            {
                cam.targetTexture = null; RenderTexture.active = null;
                Object.DestroyImmediate(go); Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
            }
            return sb.ToString();
        }
    }
}
