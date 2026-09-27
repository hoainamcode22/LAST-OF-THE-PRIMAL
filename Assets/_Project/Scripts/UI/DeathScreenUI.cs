using UnityEngine;
using UnityEngine.UI;
using PrimalFrontier.Core;

namespace PrimalFrontier.UI
{
    /// <summary>"You did not survive." with the cause, respawn at camp (shelter / bedroll) or on the beach, or load.</summary>
    public class DeathScreenUI : MonoBehaviour, IBakeableUI
    {
        Canvas _canvas; Text _cause; CanvasGroup _group; float _shownAt;

        void Awake() { Build(); _canvas.gameObject.SetActive(false); }
        public void BakeLayout() { Build(); _canvas.gameObject.SetActive(false); }
        void Start() { if (UIManager.Instance) UIManager.Instance.Changed += (a, b) => { _canvas.gameObject.SetActive(b == UIScreen.Death); _shownAt = Time.unscaledTime; }; }
        public void SetCause(string c) => _cause.text = c;

        void Build()
        {
            UIFactory.BeginBuild();
            try { BuildLayout(); } finally { UIFactory.EndBuild(); }
        }

        void BuildLayout()
        {
            _canvas = UIFactory.Canvas("[Death]", 50, transform);
            _group = UIFactory.Group(_canvas.gameObject);
            UIFactory.Fill(_canvas.transform, "Dim", UIStyle.Vignette, new Color(0.05f, 0, 0, 0.85f), true);
            UIFactory.Label(_canvas.transform, "Title", "You did not survive.", 72, UIStyle.Text, TextAnchor.MiddleCenter, UIStyle.Hand, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 170), new Vector2(1400, 100));
            _cause = UIFactory.Label(_canvas.transform, "Cause", "", 26, UIStyle.TextDim, TextAnchor.MiddleCenter, UIStyle.Body, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 90), new Vector2(1200, 40));
            UIFactory.Button(_canvas.transform, "Respawn", "WAKE UP AT CAMP", () => GameManager.Instance?.Respawn(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(420, 62), 26);
            UIFactory.Button(_canvas.transform, "Load", "LOAD LAST SAVE", () => { if (SaveSystem.Exists()) GameManager.Instance?.LoadGame(); }, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -100), new Vector2(420, 62), 26);
        }

        void Update() { if (_canvas.gameObject.activeSelf) _group.alpha = Mathf.Clamp01((Time.unscaledTime - _shownAt) / 1.5f); }
    }
}
