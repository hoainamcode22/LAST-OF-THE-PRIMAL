using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PrimalFrontier.UI;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// PC first (owner, 2026-09-30, agent U): removes the on-screen mobile / touch controls from the open scene:
    /// the MobileHUD component(s), the [Touch] canvas under [UI] and the "Touch controls" row (label + button) of the
    /// Settings panels under [Pause] / [Title]. The rows below the removed one move up one row. Idempotent (a second run
    /// finds nothing). Ends with the scene saved. Bridge: PrimalPcCleanup.RemoveTouch ("dry" = count only).
    /// </summary>
    public static class PrimalPcCleanup
    {
        const float RowStep = 56f;

        [PrimalBridgeCommand]
        public static string RemoveTouch(string arg)
        {
            bool dry = arg == "dry";
            var sb = new StringBuilder();
            var scene = EditorSceneManager.GetActiveScene();
            if (EditorApplication.isPlaying) return "refused: Play mode";
            sb.AppendLine($"scene {scene.path}{(dry ? " (dry run)" : "")}");

            // 1. [Touch] canvases (the MobileHUD layout) and any stray touch buttons / zones
            var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
            var touchRoots = all.Where(t => t && t.name == "[Touch]").ToList();
            int touchNodes = touchRoots.Sum(t => t.GetComponentsInChildren<Transform>(true).Length);
            foreach (var t in touchRoots) sb.AppendLine($"- [Touch] canvas {PathOf(t)} ({t.GetComponentsInChildren<Transform>(true).Length} objects)");
            if (!dry) foreach (var t in touchRoots) Object.DestroyImmediate(t.gameObject);
            var stray = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true)).Where(m => m is TouchButton || m is TouchZone).ToList();
            sb.AppendLine($"- stray TouchButton / TouchZone components outside [Touch]: {stray.Count}");
            if (!dry) foreach (var m in stray) Object.DestroyImmediate(m);

            // 2. MobileHUD components
            var huds = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MobileHUD>(true)).ToList();
            foreach (var h in huds) sb.AppendLine($"- MobileHUD on {PathOf(h.transform)}");
            if (!dry) foreach (var h in huds) Object.DestroyImmediate(h);

            // 3. Settings "Touch controls" rows (label "Touch controls" + button "Touch controlsBtn") in every Settings panel
            int rows = 0, moved = 0;
            var panels = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<RectTransform>(true)).Where(r => r.name == "Settings").ToList();
            foreach (var p in panels)
            {
                var label = p.Find("Touch controls") as RectTransform; var btn = p.Find("Touch controlsBtn") as RectTransform;
                if (!label && !btn) continue;
                rows++;
                float y = (label ? label : btn).anchoredPosition.y;
                sb.AppendLine($"- Settings row 'Touch controls' in {PathOf(p)} at y {y:F0}");
                if (dry) continue;
                if (label) Object.DestroyImmediate(label.gameObject);
                if (btn) Object.DestroyImmediate(btn.gameObject);
                // the rows below (centre-anchored labels, sliders, buttons) move up one row: no gap
                foreach (Transform ct in p)
                {
                    var c = ct as RectTransform;
                    if (!c || c.anchorMin != new Vector2(0.5f, 0.5f) || c.anchorMax != new Vector2(0.5f, 0.5f)) continue;
                    if (c.anchoredPosition.y < y - 1f) { c.anchoredPosition += new Vector2(0f, RowStep); moved++; EditorUtility.SetDirty(c); }
                }
                EditorUtility.SetDirty(p);
            }
            sb.AppendLine($"settings panels {panels.Count}, touch rows removed {rows}, rows moved up {moved}");
            sb.AppendLine($"removed: [Touch] canvases {touchRoots.Count} ({touchNodes} objects), MobileHUD components {huds.Count}, stray touch components {stray.Count}");

            if (!dry && (touchRoots.Count + huds.Count + rows + stray.Count) > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                bool ok = EditorSceneManager.SaveScene(scene);
                sb.AppendLine(ok ? "scene saved" : "SAVE FAILED");
            }
            else sb.AppendLine(dry ? "dry run: nothing changed" : "nothing to remove (already clean)");
            return sb.ToString();
        }

        static string PathOf(Transform t) { string s = t.name; while (t.parent) { t = t.parent; s = t.name + "/" + s; } return s; }
    }
}
