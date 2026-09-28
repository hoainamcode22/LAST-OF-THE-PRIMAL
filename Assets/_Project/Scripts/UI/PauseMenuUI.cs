using UnityEngine;
using UnityEngine.UI;
using PrimalFrontier.Core;

namespace PrimalFrontier.UI
{
    /// <summary>
    /// Esc: resume, save, load, settings, quit to title, quit game, controls. Time is stopped while open.
    /// F1 in play opens the pause screen straight on the controls table (ControlsPanel); F1 / Esc / BACK then return
    /// to the game. F1 does nothing on the title / death screens, during the intro or while another menu is open.
    /// </summary>
    public class PauseMenuUI : MonoBehaviour, IBakeableUI
    {
        Canvas _canvas; RectTransform _main, _settings, _controls; Text _info;
        bool _controlsOnly;                              // opened with F1: closing the table resumes the game

        void Awake() { Build(); _canvas.gameObject.SetActive(false); }
        public void BakeLayout() { Build(); _settings.gameObject.SetActive(false); if (_controls) _controls.gameObject.SetActive(false); _canvas.gameObject.SetActive(false); }
        void Start() { if (UIManager.Instance) { UIManager.Instance.Changed += OnScreen; OnScreen(UIScreen.None, UIManager.Instance.Current); } }
        void OnDestroy() { if (UIManager.Instance) UIManager.Instance.Changed -= OnScreen; }
        void OnScreen(UIScreen from, UIScreen to) { _canvas.gameObject.SetActive(to == UIScreen.Pause); if (to == UIScreen.Pause) ShowMain(); }

        void Build()
        {
            UIFactory.BeginBuild();
            try { BuildLayout(); } finally { UIFactory.EndBuild(); }
        }

        void BuildLayout()
        {
            _canvas = UIFactory.Canvas("[Pause]", 40, transform);
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
            // added after the scene bake: a new button below the others grows the panel once (a baked panel keeps its size)
            var controls = UIFactory.Button(_main, "CONTROLS", "CONTROLS / PHÍM  (F1)", () => ShowControls(false), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, y), new Vector2(360, 58), 22);
            if (UIFactory.Fresh(controls)) _main.sizeDelta += new Vector2(0f, 100f);
            _info = UIFactory.Label(_main, "Info", "", 18, UIStyle.TextDim, TextAnchor.LowerCenter, UIStyle.Body, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 24), new Vector2(-40, 30));
            _settings = SettingsPanel.Build(_canvas.transform, ShowMain);
            _controls = ControlsPanel.Build(_canvas.transform, ControlsBack);
            _controls.gameObject.SetActive(false);
        }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null || !kb.f1Key.wasPressedThisFrame || _canvas == null) return;
            var ui = UIManager.Instance; if (ui == null) return;
            if (ui.Current == UIScreen.None)
            {
                if (!ui.allowGameplayMenus || (ui.BlockPause != null && ui.BlockPause())) return;
                ui.Open(UIScreen.Pause);                     // OnScreen shows the main block first
                ShowControls(true);
            }
            else if (ui.Current == UIScreen.Pause)
            {
                if (_controls && _controls.gameObject.activeSelf) ControlsBack();
                else ShowControls(false);
            }
        }

        void ShowMain() { _main.gameObject.SetActive(true); _settings.gameObject.SetActive(false); if (_controls) _controls.gameObject.SetActive(false); _controlsOnly = false; _info.text = ""; }
        void ShowSettings() { _main.gameObject.SetActive(false); _settings.gameObject.SetActive(true); if (_controls) _controls.gameObject.SetActive(false); }
        void ShowControls(bool fromF1)
        {
            if (!_controls) return;
            _main.gameObject.SetActive(false); _settings.gameObject.SetActive(false); _controls.gameObject.SetActive(true);
            _controlsOnly = fromF1;
        }
        void ControlsBack() { if (_controlsOnly) UIManager.Instance?.Close(); else ShowMain(); }
    }
}
