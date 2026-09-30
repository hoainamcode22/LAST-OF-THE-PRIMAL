using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PrimalFrontier.World;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// Review captures of the resource pass (bridge: PrimalResourceBuilder.Capture "before" / "after"): edit-mode camera
    /// renders (Camera.Render, no play mode, no WaitForEndOfFrame) to Documentation/Screenshots/Resources/:
    /// island map from above with a coloured dot per node (grey stone, orange wood, green fibre, red food, blue fish, violet rare), the start
    /// beach from above with and without dots, and the player's eye view at the spawn. The dots are temporary objects,
    /// removed after the render; a scene that was saved before stays saved.
    /// </summary>
    public static partial class PrimalResourceBuilder
    {
        const string ShotDir = "Documentation/Screenshots/Resources";

        [PrimalBridgeCommand]
        public static string Capture(string arg)
        {
            string tag = string.IsNullOrEmpty(arg) ? "shot" : arg.Trim();
            var scene = EditorSceneManager.GetActiveScene();
            bool wasClean = !scene.isDirty;
            var log = new StringBuilder();
            var terrain = Terrain.activeTerrain; if (!terrain) return "no terrain";
            var td = terrain.terrainData; var tp = terrain.transform.position;
            var spawnT = GameObject.Find("ZONE_PlayerSpawn"); Vector3 spawn = spawnT ? spawnT.transform.position : Vector3.zero;
            Directory.CreateDirectory(ShotDir);
            var camGo = new GameObject("__ResCaptureCam") { hideFlags = HideFlags.DontSave };
            var cam = camGo.AddComponent<Camera>();
            var main = Camera.main; if (main) { cam.CopyFrom(main); }
            cam.enabled = false; cam.farClipPlane = 3000f; cam.nearClipPlane = 0.1f;
            var dots = new List<GameObject>();
            var mats = new Dictionary<ResourceCategory, Material>();
            Material Mat(ResourceCategory c)
            {
                if (mats.TryGetValue(c, out var m)) return m;
                var sh = Shader.Find("Universal Render Pipeline/Unlit"); m = new Material(sh) { hideFlags = HideFlags.DontSave };
                var col = c switch { ResourceCategory.Stone => new Color(0.85f, 0.85f, 0.9f), ResourceCategory.Wood => new Color(1f, 0.55f, 0.1f), ResourceCategory.Fiber => new Color(0.2f, 1f, 0.25f), ResourceCategory.Fish => new Color(0.2f, 0.6f, 1f), ResourceCategory.Rare => new Color(0.85f, 0.4f, 1f), _ => new Color(1f, 0.15f, 0.2f) };
                m.SetColor("_BaseColor", col); mats[c] = m; return m;
            }
            void Dots(float size, float lift, Vector3 center, float radius)
            {
                foreach (var n in Object.FindObjectsByType<ResourceNode>(FindObjectsSortMode.None))
                {
                    var p = n.transform.position; if (radius > 0 && (p - center).sqrMagnitude > radius * radius) continue;
                    var cat = n.definition ? n.definition.category : ResourceNode.CategoryOf(n.yieldItem);
                    var d = GameObject.CreatePrimitive(PrimitiveType.Sphere); d.hideFlags = HideFlags.DontSave;
                    Object.DestroyImmediate(d.GetComponent<Collider>());
                    float s = size * (n.definition && n.definition.size == ResourceSize.Huge ? 0.6f : 1f);
                    d.transform.position = Bounds(n.gameObject).max.y * Vector3.up + new Vector3(p.x, lift, p.z); d.transform.localScale = Vector3.one * s;
                    var r = d.GetComponent<MeshRenderer>(); r.sharedMaterial = Mat(cat); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    dots.Add(d);
                }
            }
            void Clear() { foreach (var d in dots) if (d) Object.DestroyImmediate(d); dots.Clear(); }
            void Shot(string name, int w, int h)
            {
                var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                cam.targetTexture = rt; cam.Render(); cam.Render();
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(); RenderTexture.active = null;
                cam.targetTexture = null;
                string path = $"{ShotDir}/{tag}_{name}.png"; File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex); Object.DestroyImmediate(rt);
                log.AppendLine("wrote " + path);
            }
            bool fog = RenderSettings.fog;
            try
            {
                // 1. island map from above with dots (no fog: it washes the map out from 600 m)
                RenderSettings.fog = false;
                Dots(3.2f, 2f, Vector3.zero, 0f);
                cam.orthographic = true; cam.orthographicSize = td.size.z * 0.5f;
                cam.transform.SetPositionAndRotation(tp + new Vector3(td.size.x * 0.5f, 600f, td.size.z * 0.5f), Quaternion.Euler(90f, 0f, 0f));
                Shot("island_map", 1400, 1400);
                Clear();
                RenderSettings.fog = fog;
                // 2. the start beach from above, dots and plain
                cam.orthographic = false; cam.fieldOfView = 50f;
                var toInland = Vector3.back;                                    // the island interior lies to -z of the beach spawn
                var eye = spawn - toInland * 28f + Vector3.up * 34f;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation((spawn + toInland * 22f) - eye));
                Shot("start_area", 1600, 900);
                Dots(0.7f, 0.6f, spawn, 90f);
                Shot("start_area_dots", 1600, 900);
                Clear();
                // 3. the player's eye at the spawn, looking inland along the trail
                var camp = GameObject.Find("ZONE_Camp"); var look = camp ? camp.transform.position : spawn + toInland * 30f;
                var e2 = spawn + Vector3.up * 1.7f; var dir = (look - spawn); dir.y = 0f;
                cam.fieldOfView = 60f; cam.transform.SetPositionAndRotation(e2, Quaternion.LookRotation(dir.normalized + Vector3.down * 0.12f));
                Shot("player_view", 1600, 900);
            }
            finally
            {
                RenderSettings.fog = fog;
                Clear();
                foreach (var m in mats.Values) Object.DestroyImmediate(m);
                Object.DestroyImmediate(camGo);
                if (wasClean && scene.isDirty) EditorSceneManager.SaveScene(scene);
            }
            return log.ToString();
        }
    }
}
