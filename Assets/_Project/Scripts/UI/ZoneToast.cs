using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using PrimalFrontier.Core;
using PrimalFrontier.World;

namespace PrimalFrontier.UI
{
    /// <summary>
    /// The place name that fades in when the player walks into one of the Phase 2 environments (ZoneManager zones with
    /// announce on: Migration Valley, Prehistoric Wetland, Bone Valley, Giant Fern Forest, Volcanic Foothills, Deep Water
    /// Cave). The name in the heading font, a hand-written "a new place" under it on the first visit, a thin amber rule.
    /// Fades in over 0.6 s, stays 3.2 s, fades out. Coming back to a zone shows it again only after <see cref="repeatSeconds"/>.
    /// Only while playing, never over a menu; follows the HUD's own fade. Own canvas "[ZoneToast]", made on demand by
    /// ZoneManager at play start (nothing to place).
    /// </summary>
    public class ZoneToast : MonoBehaviour
    {
        [Tooltip("seconds the name stays at full strength")] public float holdSeconds = 3.2f;
        public float fadeIn = 0.6f, fadeOut = 1.2f;
        [Tooltip("a zone entered again shows its name only after this many real seconds")] public float repeatSeconds = 120f;

        public static ZoneToast Instance { get; private set; }
        CanvasGroup _group; Text _title, _sub; float _t0 = -99f; bool _showing;
        readonly Dictionary<string, float> _last = new Dictionary<string, float>();

        public static void Ensure()
        {
            if (Instance || !Application.isPlaying) return;
            var parent = HUDManager.Instance ? HUDManager.Instance.transform.parent : null;
            var go = new GameObject("[ZoneToast]");
            if (parent) go.transform.SetParent(parent, false);
            go.AddComponent<ZoneToast>();
        }

        void Awake()
        {
            if (Instance && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            UIFactory.BeginBuild();
            try
            {
                var canvas = UIFactory.Canvas("Canvas", 12, transform);
                var cg = UIFactory.Group(canvas.gameObject); cg.interactable = false; cg.blocksRaycasts = false;
                var root = UIFactory.Rect(canvas.transform, "Toast", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(900f, 110f));
                _group = UIFactory.Group(root.gameObject); _group.alpha = 0f;
                _title = UIFactory.Label(root, "Name", "", 40, UIStyle.Text, TextAnchor.UpperCenter, UIStyle.Head, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 50));
                UIFactory.Image(root, "Rule", UIStyle.BarFill, new Color(UIStyle.Accent.r, UIStyle.Accent.g, UIStyle.Accent.b, 0.75f), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -56), new Vector2(120, 2));
                _sub = UIFactory.Label(root, "Sub", "", 22, UIStyle.TextDim, TextAnchor.UpperCenter, UIStyle.Hand, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -64), new Vector2(0, 30));
            }
            finally { UIFactory.EndBuild(); }
        }

        void OnEnable() { GameEvents.Raised -= OnEvent; GameEvents.Raised += OnEvent; }
        void OnDisable() { GameEvents.Raised -= OnEvent; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void OnEvent(GameEvent e)
        {
            if (e.type != GameEventType.ZoneEntered || string.IsNullOrEmpty(e.id)) return;
            var zm = ZoneManager.Instance; var z = zm ? zm.Find(e.id) : null;
            if (z == null || !z.announce || string.IsNullOrEmpty(z.displayName)) return;
            var gm = GameManager.Instance;
            if (gm && gm.State != GameState.Playing) return;
            float now = Time.unscaledTime;
            bool first = e.amount > 0;
            if (!first && _last.TryGetValue(z.id, out var at) && now - at < repeatSeconds) return;
            _last[z.id] = now;
            Show(z.displayName, first ? "a new place" : "");
        }

        public void Show(string title, string sub)
        {
            if (!_title) return;
            _title.text = title.ToUpperInvariant(); _sub.text = sub ?? "";
            _t0 = Time.unscaledTime; _showing = true;
        }

        void Update()
        {
            if (!_showing || !_group) return;
            float t = Time.unscaledTime - _t0;
            float a = t < fadeIn ? t / Mathf.Max(0.01f, fadeIn) : t < fadeIn + holdSeconds ? 1f : 1f - (t - fadeIn - holdSeconds) / Mathf.Max(0.01f, fadeOut);
            if (a <= 0f && t > fadeIn) { _showing = false; a = 0f; }
            bool menu = UIManager.Instance && UIManager.Instance.Current != UIScreen.None;
            float hud = HUDManager.Instance ? HUDManager.Instance.HudAlpha : 1f;
            _group.alpha = Mathf.Clamp01(a) * (menu ? 0f : 1f) * hud;
        }
    }
}
