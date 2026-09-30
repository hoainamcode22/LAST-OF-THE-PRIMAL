using UnityEngine;
using UnityEngine.UI;
using PrimalFrontier.Core;
using PrimalFrontier.Story;

namespace PrimalFrontier.UI
{
    /// <summary>
    /// The current objective, small, under the minimap: the chapter name (THE SHORE) and one line ("Find fresh water before
    /// sunset"), plus a text clue the first time an exploration objective appears. It shows when the objective changes, when
    /// a soft deadline comes close, after a menu closes, and fades out when nothing changes. A finished objective shows
    /// "done" for a moment first. Hidden on the title, in the intro, on the death screen and in menus. Replaces the HUD's
    /// older objective box (hidden at start). Own canvas "[Objective]"; layout lives in the scene once baked.
    /// </summary>
    public class ObjectiveUI : MonoBehaviour, IBakeableUI
    {
        [Tooltip("seconds the objective stays after it changes")] public float showSeconds = 9f;
        [Tooltip("seconds the clue line stays (first appearance only)")] public float clueSeconds = 12f;
        [Tooltip("seconds a completed objective shows as done")] public float doneSeconds = 2.8f;

        Canvas _canvas; CanvasGroup _group, _clueGroup; RectTransform _root; Text _chapter, _line, _clue; Image _rule;
        int _cVersion = -1; string _cMission, _cText; int _cChapter = -1; bool _cUrgent;
        float _showUntil, _clueUntil, _doneUntil; string _doneText; bool _first = true;
        bool _hookedHud;

        void Awake() { Build(); if (_line) _line.text = ""; if (_chapter) _chapter.text = ""; if (_clue) _clue.text = ""; if (_group) _group.alpha = 0f; }
        public void BakeLayout()
        {
            Build();
            if (_chapter && string.IsNullOrEmpty(_chapter.text)) _chapter.text = "THE SHORE";
            if (_line && string.IsNullOrEmpty(_line.text)) _line.text = "Find fresh water before sunset";
            if (_clue && string.IsNullOrEmpty(_clue.text)) _clue.text = "The sea is no use. Streams run down from the high ground.";
            if (_group) _group.alpha = 1f; if (_clueGroup) _clueGroup.alpha = 1f;
        }

        void Start()
        {
            if (UIManager.Instance) UIManager.Instance.Changed += OnScreen;
            GameEvents.Raised += OnEvent;
            HideOldObjectiveBox();
        }
        void OnDestroy() { if (UIManager.Instance) UIManager.Instance.Changed -= OnScreen; GameEvents.Raised -= OnEvent; }

        void Build()
        {
            UIFactory.BeginBuild();
            try
            {
                _canvas = UIFactory.Canvas("[Objective]", 11, transform);
                var cg = UIFactory.Group(_canvas.gameObject); cg.interactable = false; cg.blocksRaycasts = false;
                _root = UIFactory.Rect(_canvas.transform, "Objective", new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-26, -268), new Vector2(460, 110));
                _group = UIFactory.Group(_root.gameObject);
                _chapter = UIFactory.Label(_root, "Chapter", "", 16, UIStyle.Accent, TextAnchor.UpperRight, UIStyle.Head, new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(0, 0), new Vector2(0, 22));
                _rule = UIFactory.Image(_root, "Rule", UIStyle.BarFill, new Color(UIStyle.Accent.r, UIStyle.Accent.g, UIStyle.Accent.b, 0.7f), new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(0, -24), new Vector2(64, 2));
                _line = UIFactory.Label(_root, "Line", "", 22, UIStyle.Text, TextAnchor.UpperRight, UIStyle.Body, new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(0, -30), new Vector2(0, 30));
                var clueRt = UIFactory.Rect(_root, "ClueBox", new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(0, -62), new Vector2(0, 44));
                _clueGroup = UIFactory.Group(clueRt.gameObject);
                _clue = UIFactory.Label(clueRt, "Clue", "", 17, UIStyle.TextDim, TextAnchor.UpperRight, UIStyle.Hand);
            }
            finally { UIFactory.EndBuild(); }
        }

        /// <summary>the HUD's older objective box would repeat this line: hidden (the compass marker stays)</summary>
        void HideOldObjectiveBox()
        {
            if (_hookedHud) return;
            var hud = HUDManager.Instance; if (!hud) return;
            _hookedHud = true;
            var canvas = hud.transform.Find("[HUD]");
            var box = canvas ? canvas.Find("Objective") : null;
            if (box) box.gameObject.SetActive(false);
        }

        void OnScreen(UIScreen from, UIScreen to)
        {
            if (to == UIScreen.None && (from == UIScreen.Journal || from == UIScreen.Inventory || from == UIScreen.Pause)) Show(4f);
        }

        void OnEvent(GameEvent e)
        {
            if (e.type != GameEventType.MissionCompleted) return;
            var ms = MissionSystem.Instance; var s = ms ? ms.Get(e.id) : null;
            // only the objective the player was following gets a "done" moment (side goals finish quietly)
            if (s != null && (s.def.main || s.def.id == _cMission))
            {
                _doneText = s.def.title + "   <color=#" + ColorUtility.ToHtmlStringRGB(UIStyle.Good) + ">done</color>";
                _doneUntil = Time.unscaledTime + doneSeconds;
            }
        }

        void Show(float seconds) { _showUntil = Mathf.Max(_showUntil, Time.unscaledTime + seconds); }

        void Update()
        {
            if (!_hookedHud) HideOldObjectiveBox();
            var ms = MissionSystem.Instance;
            var gm = GameManager.Instance;
            bool playing = gm == null || gm.State == GameState.Playing;
            bool menu = UIManager.Instance && UIManager.Instance.Current != UIScreen.None;
            float hudA = HUDManager.Instance ? HUDManager.Instance.HudAlpha : 1f;
            float t = Time.unscaledTime, udt = Time.unscaledDeltaTime;

            if (ms && ms.Version != _cVersion)
            {
                _cVersion = ms.Version;
                var f = ms.Focused;
                string id = f != null ? f.def.id : null;
                string text = ms.ObjectiveText(f);
                bool urgent = ms.FocusedUrgent;
                if (ms.Chapter != _cChapter) { _cChapter = ms.Chapter; _chapter.text = ms.ChapterName; Show(showSeconds); }
                if (id != _cMission)
                {
                    bool had = _cMission != null;
                    _cMission = id;
                    _clue.text = f != null && !string.IsNullOrEmpty(f.def.clue) ? f.def.clue : "";
                    _clueUntil = _clue.text.Length > 0 ? t + clueSeconds : 0f;
                    if (f != null)
                    {
                        Show(showSeconds + (_doneUntil > t ? doneSeconds : 0f));
                        if (!_first || had) Audio.SfxPlayer.Instance.Play2D(Audio.SfxId.UiObjective, 0.45f);
                        _first = false;
                    }
                }
                if (text != _cText) { if (_cText != null && id == _cMission) Show(showSeconds * 0.6f); _cText = text; }
                if (urgent && !_cUrgent) Show(showSeconds);
                _cUrgent = urgent;
            }

            // the line: a finished objective first, then the current one
            bool done = _doneUntil > t && !string.IsNullOrEmpty(_doneText);
            string want = done ? _doneText : _cText ?? "";
            if (!ReferenceEquals(_line.text, want) && _line.text != want) _line.text = want;
            _line.color = done ? UIStyle.TextDim : _cUrgent ? Color.Lerp(UIStyle.Text, UIStyle.Accent, 0.55f + 0.25f * Mathf.Sin(t * 3f)) : UIStyle.Text;

            bool has = ms && ms.Begun && (done || !string.IsNullOrEmpty(_cText));
            bool visible = has && playing && !menu && hudA > 0.5f && (t < _showUntil || done);
            _group.alpha = Mathf.MoveTowards(_group.alpha, visible ? 1f : 0f, udt * (visible ? 2.5f : 0.6f));
            bool clue = visible && !done && t < _clueUntil;
            _clueGroup.alpha = Mathf.MoveTowards(_clueGroup.alpha, clue ? 1f : 0f, udt * (clue ? 2f : 0.5f));
            if (_rule) _rule.enabled = !string.IsNullOrEmpty(_chapter.text);
        }
    }
}
