using UnityEngine;
using UnityEngine.UI;
using PrimalFrontier.Core;

namespace PrimalFrontier.UI
{
    /// <summary>Settings block shared by the title and pause menus: volumes, mouse, quality preset, resolution, fullscreen, VSync,
    /// blood, touch controls, key hints (ContextHints).</summary>
    public static class SettingsPanel
    {
        public static RectTransform Build(Transform parent, System.Action onBack)
        {
            var p = UIFactory.Image(parent, "Settings", UIStyle.Leather, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820, 860), true).rectTransform;
            UIFactory.Label(p, "Title", "SETTINGS", 40, UIStyle.Text, TextAnchor.UpperCenter, UIStyle.Head, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(0, 50));
            float y = 290;
            void SliderRow(string label, float min, float max, float v, System.Action<float> set)
            {
                UIFactory.Label(p, label, label, 22, UIStyle.Text, TextAnchor.MiddleLeft, UIStyle.Body, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 0.5f), new Vector2(-330, y), new Vector2(260, 36));
                UIFactory.Slider(p, label + "Slider", min, max, v, x => set(x), new Vector2(110, y), new Vector2(380, 22));
                y -= 56;
            }
            SliderRow("Master volume", 0, 1, GameSettings.Master, v => GameSettings.Master = v);
            SliderRow("Music volume", 0, 1, GameSettings.Music, v => GameSettings.Music = v);
            SliderRow("Effects volume", 0, 1, GameSettings.Sfx, v => GameSettings.Sfx = v);
            SliderRow("Ambience volume", 0, 1, GameSettings.Ambience, v => GameSettings.Ambience = v);
            SliderRow("Mouse sensitivity", 0.03f, 0.4f, GameSettings.MouseSensitivity, v => GameSettings.MouseSensitivity = v);
            Button Toggle(string label, System.Func<string> text, System.Action click)
            {
                UIFactory.Label(p, label, label, 22, UIStyle.Text, TextAnchor.MiddleLeft, UIStyle.Body, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 0.5f), new Vector2(-330, y), new Vector2(260, 36));
                Button b = null;
                b = UIFactory.Button(p, label + "Btn", text(), () => { click(); UIFactory.SetLabel(b, text()); }, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(110, y), new Vector2(380, 44), 20);
                UIFactory.SetLabel(b, text());      // the saved scene holds the value from when it was baked
                y -= 56; return b;
            }
            Toggle("Invert mouse Y", () => GameSettings.InvertY ? "ON" : "OFF", () => GameSettings.InvertY = !GameSettings.InvertY);
            Toggle("Quality", () => GameSettings.QualityNames[Mathf.Clamp(GameSettings.Quality, 0, 3)].ToUpperInvariant(), () => GameSettings.Quality = (GameSettings.Quality + 1) % 4);
            Toggle("Resolution", () => $"{UnityEngine.Screen.width} x {UnityEngine.Screen.height}", () => GameSettings.CycleResolution());
            Toggle("Fullscreen", () => GameSettings.Fullscreen ? "ON" : "OFF", () => GameSettings.Fullscreen = !GameSettings.Fullscreen);
            Toggle("VSync", () => GameSettings.VSync ? "ON" : "OFF", () => GameSettings.VSync = !GameSettings.VSync);
            Toggle("Blood effects", () => GameSettings.BloodNames[(int)GameSettings.Blood], () => GameSettings.Blood = (BloodLevel)(((int)GameSettings.Blood + 1) % 3));
            Toggle("Touch controls", () => GameSettings.TouchNames[GameSettings.TouchControls], () => GameSettings.TouchControls = (GameSettings.TouchControls + 1) % 3);
            // added after the scene bake: a new row below the others grows the panel once (a baked panel keeps its size)
            var hints = Toggle("Key hints (Gợi ý phím)", () => GameSettings.Hints ? "ON" : "OFF", () => GameSettings.Hints = !GameSettings.Hints);
            if (UIFactory.Fresh(hints)) p.sizeDelta += new Vector2(0f, 120f);
            UIFactory.Button(p, "Back", "BACK", () => onBack?.Invoke(), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(260, 54), 24);
            return p;
        }
    }
}
