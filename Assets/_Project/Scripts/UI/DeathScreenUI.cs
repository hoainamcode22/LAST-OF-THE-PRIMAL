using UnityEngine;
using UnityEngine.UI;
using PrimalFrontier.Core;

namespace PrimalFrontier.UI
{
    /// <summary>
    /// Short death screen: "You did not survive.", the cause, what was left behind and where you wake, one button. The death
    /// counts (GameManager saves after the respawn), so there is no "load last save" here (the pause menu still loads).
    /// </summary>
    public class DeathScreenUI : MonoBehaviour, IBakeableUI
    {
        Canvas _canvas; Text _cause, _lost, _wake; CanvasGroup _group; float _shownAt;

        void Awake() { Build(); _canvas.gameObject.SetActive(false); }
        public void BakeLayout() { Build(); _canvas.gameObject.SetActive(false); }
        void Start() { if (UIManager.Instance) { UIManager.Instance.Changed += OnScreen; OnScreen(UIScreen.None, UIManager.Instance.Current); } }
        void OnDestroy() { if (UIManager.Instance) UIManager.Instance.Changed -= OnScreen; }
        void OnScreen(UIScreen from, UIScreen to) { _canvas.gameObject.SetActive(to == UIScreen.Death); _shownAt = Time.unscaledTime; }
        public void SetCause(string c) => _cause.text = c;
        /// <summary>what stayed at the death spot, and where the survivor wakes</summary>
        public void SetDetails(string lost, string wake) { _lost.text = lost ?? ""; _wake.text = wake ?? ""; }

        void Build()
        {
            UIFactory.BeginBuild();
            try { BuildLayout(); } finally { UIFactory.EndBuild(); }
        }

        void BuildLayout()
        {
            _canvas = UIFactory.Canvas("[Death]", 50, transform);
            _group = UIFactory.Group(_canvas.gameObject);
            var root = _canvas.transform;
            UIFactory.Fill(root, "Dim", UIStyle.Vignette, new Color(0.05f, 0, 0, 0.85f), true);
            UIFactory.Label(root, "Title", "You did not survive.", 72, UIStyle.Text, TextAnchor.MiddleCenter, UIStyle.Hand, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 170), new Vector2(1400, 100));
            _cause = UIFactory.Label(root, "Cause", "", 26, UIStyle.TextDim, TextAnchor.MiddleCenter, UIStyle.Body, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 90), new Vector2(1200, 40));
            _lost = UIFactory.Label(root, "Lost", "", 22, UIStyle.TextDim, TextAnchor.MiddleCenter, UIStyle.Hand, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 48), new Vector2(1200, 34));
            _wake = UIFactory.Label(root, "Wake", "", 22, UIStyle.TextDim, TextAnchor.MiddleCenter, UIStyle.Hand, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 14), new Vector2(1200, 34));
            var wakeBtn = UIFactory.Button(root, "Respawn", "WAKE UP", () => GameManager.Instance?.Respawn(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -60), new Vector2(420, 62), 26);
            UIFactory.SetLabel(wakeBtn, "WAKE UP");
            var load = root.Find("Load"); if (load) load.gameObject.SetActive(false);           // baked by the old layout
            if (_cause) _cause.text = ""; if (_lost) _lost.text = ""; if (_wake) _wake.text = "";
        }

        void Update() { if (_canvas.gameObject.activeSelf) _group.alpha = Mathf.Clamp01((Time.unscaledTime - _shownAt) / 1.5f); }
    }
}
