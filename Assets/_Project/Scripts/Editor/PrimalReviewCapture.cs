using System.Globalization;
using System.Linq;
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

        /// <summary>"shot:FromObject:ToObject:eyeHeight:targetHeight[:fov[:hour]]" ; entries separated by ';' (hour sets the time of day for the shot)</summary>
        [PrimalBridgeCommand]
        public static string LookFrom(string arg)
        {
            var sb = new StringBuilder(); var tmp = new System.Collections.Generic.List<GameObject>();
            var entries = new System.Collections.Generic.List<string>();
            var tm = Object.FindFirstObjectByType<Core.TimeManager>(); float oldHour = tm ? tm.hour : 0f;
            // particle effects do not run in edit mode: advance the landmark ones so the shot shows them
            var fx = Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(ps => ps.GetComponentInParent<World.VolcanoLandmark>()).ToArray();
            foreach (var ps in fx) ps.Simulate(45f, false, true, false);
            try
            {
                foreach (var e in (arg ?? "").Split(';'))
                {
                    var p = e.Split(':'); if (p.Length < 5) continue;
                    var a = FindTarget(p[1].Trim()); var b = FindTarget(p[2].Trim());
                    if (!a || !b) { sb.AppendLine($"{p[0]}: missing {p[1]} or {p[2]}"); continue; }
                    float eh = F(p[3]), th = F(p[4]), fov = p.Length > 5 ? F(p[5]) : 55f;
                    if (tm && p.Length > 6) { tm.hour = F(p[6]); tm.Apply(); }
                    // Shots() looks from target+offset back at target: put a temporary target and compute distance / yaw
                    Vector3 from = a.position + Vector3.up * eh, to = b.position + Vector3.up * th;
                    var go = new GameObject("_look_" + p[0]) { hideFlags = HideFlags.HideAndDontSave }; go.transform.position = to; tmp.Add(go); _faceTargets[go.name] = go.transform;
                    Vector3 d = from - to; float dist = new Vector2(d.x, d.z).magnitude;
                    float yaw = Mathf.Atan2(-d.x, -d.z) * Mathf.Rad2Deg;
                    sb.Append(Shots(System.FormattableString.Invariant($"{p[0]}:{go.name}:{dist}:{d.y}:{yaw}:0:{fov}")));
                }
            }
            finally
            {
                foreach (var g in tmp) if (g) Object.DestroyImmediate(g);
                _faceTargets.Clear();
                if (tm) { tm.hour = oldHour; tm.Apply(); }
                foreach (var ps in fx) if (ps) ps.Clear(false);
            }
            return sb.ToString();
        }

        /// <summary>scene root objects with child counts (for placing new content in the right group)</summary>
        [PrimalBridgeCommand]
        public static string Roots(string arg)
        {
            var sb = new StringBuilder();
            foreach (var g in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                sb.Append($"{g.name} ({g.transform.childCount}): ");
                for (int i = 0; i < Mathf.Min(g.transform.childCount, 14); i++) sb.Append(g.transform.GetChild(i).name + ", ");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>close-up of every placed creature's face (head bone, 3/4 front), for eye / face review</summary>
        [PrimalBridgeCommand]
        public static string DinoFaces(string arg)
        {
            var sb = new StringBuilder();
            var entries = new System.Collections.Generic.List<string>();
            foreach (var d in Object.FindObjectsByType<AI.DinosaurController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                Transform head = null;
                foreach (var t in d.GetComponentsInChildren<Transform>())
                {
                    string n = t.name.ToLower();
                    if (n.Contains("head") && !n.Contains("end") && !n.Contains("_end")) { head = t; break; }
                }
                if (!head) { sb.AppendLine($"{d.name}: no head bone"); continue; }
                float size = d.def ? Mathf.Max(0.5f, d.def.bodyRadius) : 1f;
                var go = new GameObject("_face_" + d.name) { hideFlags = HideFlags.HideAndDontSave };
                go.transform.position = head.position;
                _faceTargets[go.name] = go.transform;
                entries.Add(System.FormattableString.Invariant($"face_{d.name}:{go.name}:{size * 1.6f}:{size * 0.25f}:{d.transform.eulerAngles.y + 180f - 35f}:0:40"));
            }
            try { sb.Append(Shots(string.Join(";", entries))); }
            finally { foreach (var t in _faceTargets.Values) if (t) Object.DestroyImmediate(t.gameObject); _faceTargets.Clear(); }
            return sb.ToString();
        }
        /// <summary>like DinoFaces but on fresh prefab instances (default pose) placed high above the island</summary>
        [PrimalBridgeCommand]
        public static string PrefabFaces(string arg)
        {
            var sb = new StringBuilder(); var made = new System.Collections.Generic.List<GameObject>();
            var entries = new System.Collections.Generic.List<string>();
            int i = 0;
            foreach (var guid in AssetDatabase.FindAssets("DINO_ t:Prefab", new[] { "Assets/Art/Characters/Dinosaurs" }))
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (!pf || (!string.IsNullOrEmpty(arg) && !pf.name.Contains(arg))) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(pf); go.hideFlags = HideFlags.HideAndDontSave;
                go.transform.position = new Vector3(i * 40f, 400f, 0f); i++;
                foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>()) mb.enabled = false;
                made.Add(go);
                var head = go.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Head");
                if (!head) continue;
                var tgt = new GameObject("_pface_" + pf.name) { hideFlags = HideFlags.HideAndDontSave }; var eyeL = go.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Eye_L");
                tgt.transform.position = eyeL ? eyeL.position : head.position + go.transform.forward * 0.3f;
                _faceTargets[tgt.name] = tgt.transform; made.Add(tgt);
                var d = go.GetComponent<AI.DinosaurController>(); float size = d && d.def ? Mathf.Max(0.35f, d.def.bodyRadius) : 1f;
                entries.Add(System.FormattableString.Invariant($"pface_{pf.name}:{tgt.name}:{size * 1.1f}:{size * 0.1f}:{180f - 55f}:0:35"));
            }
            try { sb.Append(Shots(string.Join(";", entries))); }
            finally { foreach (var g in made) if (g) Object.DestroyImmediate(g); _faceTargets.Clear(); }
            return sb.ToString();
        }

        static readonly System.Collections.Generic.Dictionary<string, Transform> _faceTargets = new System.Collections.Generic.Dictionary<string, Transform>();

        static Transform FindTarget(string name)
        {
            if (_faceTargets.TryGetValue(name, out var t) && t) return t;
            var g = GameObject.Find(name); return g ? g.transform : null;
        }

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
            bool asyncWas = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;   // compile shader variants now, not a blank frame
            try
            {
                foreach (var entry in (arg ?? "").Split(';'))
                {
                    var p = entry.Split(':'); if (p.Length < 5) continue;
                    var target = FindTarget(p[1].Trim());
                    if (!target) { sb.AppendLine($"{p[0]}: '{p[1]}' not found"); continue; }
                    float dist = F(p[2]), h = F(p[3]), yaw = F(p[4]), look = p.Length > 5 ? F(p[5]) : 1.5f;
                    Vector3 basePos = target.position;
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
                ShaderUtil.allowAsyncCompilation = asyncWas;
                cam.targetTexture = null; RenderTexture.active = null;
                Object.DestroyImmediate(go); Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
            }
            return sb.ToString();
        }
    }
}
