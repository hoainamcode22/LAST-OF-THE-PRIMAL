using UnityEngine;
using UnityEngine.UI;
using PrimalFrontier.Core;

namespace PrimalFrontier.UI
{
    /// <summary>Esc: resume, save, load, settings, quit to title, quit game. Time is stopped while open.</summary>
    public class PauseMenuUI : MonoBehaviour
    {
        Canvas _canvas; RectTransform _main, _settings; Text _info;

        void Awake() { Build(); _canvas.gameObject.SetActive(false); }
        void Start() { if (UIManager.Instance) UIManager.Instance.Changed += (a, b) => { _canvas.gameObject.SetActive(b == UIScreen.Pause); if (b == UIScreen.Pause) ShowMain(); }; }

        void Build()
        {
            _canvas = UIFactory.Canvas("[Pause]", 40); _canvas.transform.SetParent(transform, false);
            UIFactory.Fill(_canvas.transform, "Dim", UIStyle.Vignette, new Color(0, 0, 0, 0.75f), true);
            _main = UIFactory.Image(_canvas.transform, "Main", UIStyle.Leather, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520, 640), true).rectTransform;
            UIFactory.Label(_main, "Title", "PAUSED", 44, UIStyle.Text, TextAnchor.UpperCenter, UIStyle.Head, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -34), new Vector2(0, 56));
            float y = 150;
            void B(string t, UnityEngine.Events.UnityAction a) { UIFactory.Button(_main, t, t, a, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, y), new Vector2(360, 58), 26); y -= 72; }
            B("RESUME", () => UIManager.Instance?.Close());
            B("SAVE GAME", () => { bool ok = GameManager.Instance && GameManager.Instance.SaveGame(); _info.text = ok ? "Game saved." : "Save failed: " + SaveSystem.LastError; });
            B("LOAD GAME", () => { if (!SaveSystem.Exists()) { _info.text = "No save yet."; return; } UIManager.Instance?.Close(); GameManager.Instance?.LoadGame(); });
            B("SETTINGS", ShowSettings);
            B("QUIT TO TITLE", () => GameManager.Instance?.QuitToTitle());
            B("QUIT GAME", () => { Application.Quit();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
            });
            _info = UIFactory.Label(_main, "Info", "", 18, UIStyle.TextDim, TextAnchor.LowerCenter, UIStyle.Body, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 24), new Vector2(-40, 30));
            _settings = SettingsPanel.Build(_canvas.transform, ShowMain);
        }

        void ShowMain() { _main.gameObject.SetActive(true); _settings.gameObject.SetActive(false); _info.text = ""; }
        void ShowSettings() { _main.gameObject.SetActive(false); _settings.gameObject.SetActive(true); }
    }
}
