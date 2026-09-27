using UnityEngine;
using UnityEngine.UI;
using PrimalFrontier.Core;

namespace PrimalFrontier.UI
{
    /// <summary>Title: PRIMAL FRONTIER over a slow view of the wreck. Continue (if a save exists), New Game, Settings, Quit.</summary>
    public class TitleScreenUI : MonoBehaviour, IBakeableUI
    {
        Canvas _canvas; RectTransform _main, _settings; Button _continue; Text _saveInfo;

        void Awake() { Build(); _canvas.gameObject.SetActive(false); }
        public void BakeLayout() { Build(); _settings.gameObject.SetActive(false); _canvas.gameObject.SetActive(false); }
        void Start() { if (UIManager.Instance) UIManager.Instance.Changed += (a, b) => { _canvas.gameObject.SetActive(b == UIScreen.Title); if (b == UIScreen.Title) ShowMain(); }; }

        void Build()
        {
            UIFactory.BeginBuild();
            try { BuildLayout(); } finally { UIFactory.EndBuild(); }
        }

        void BuildLayout()
        {
            _canvas = UIFactory.Canvas("[Title]", 45, transform);
            UIFactory.Fill(_canvas.transform, "Vignette", UIStyle.Vignette, new Color(0, 0, 0, 0.8f));
            _main = UIFactory.Stretch(_canvas.transform, "Main");
            UIFactory.Label(_main, "Title", "PRIMAL FRONTIER", 104, UIStyle.Text, TextAnchor.MiddleLeft, UIStyle.Head, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(140, 260), new Vector2(1200, 140));
            UIFactory.Label(_main, "Sub", "Day Zero", 34, UIStyle.Accent, TextAnchor.MiddleLeft, UIStyle.Hand, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(150, 175), new Vector2(800, 50));
            float y = 40;
            Button B(string t, UnityEngine.Events.UnityAction a) { var b = UIFactory.Button(_main, t, t, a, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(150, y), new Vector2(360, 60), 28); y -= 76; return b; }
            _continue = B("CONTINUE", () => GameManager.Instance?.ContinueGame());
            B("NEW GAME", () => GameManager.Instance?.NewGame());
            B("SETTINGS", () => { _main.gameObject.SetActive(false); _settings.gameObject.SetActive(true); });
            B("QUIT", () => { Application.Quit();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
            });
            _saveInfo = UIFactory.Label(_main, "SaveInfo", "", 18, UIStyle.TextDim, TextAnchor.MiddleLeft, UIStyle.Body, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(530, 40), new Vector2(600, 30));
            UIFactory.Label(_main, "Credit", "Vertical slice. Original assets. Fonts: Alegreya (SIL OFL).", 15, UIStyle.TextDim, TextAnchor.LowerRight, UIStyle.Body, new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-30, 20), new Vector2(900, 26));
            _settings = SettingsPanel.Build(_canvas.transform, ShowMain);
        }

        void ShowMain()
        {
            _main.gameObject.SetActive(true); _settings.gameObject.SetActive(false);
            bool has = SaveSystem.Exists();
            _continue.gameObject.SetActive(has);
            var d = has ? SaveSystem.Read() : null;
            _saveInfo.text = d != null ? $"Day {d.day}, saved {d.savedAt}" : "";
        }
    }
}
