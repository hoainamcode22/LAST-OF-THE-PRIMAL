using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.Story;
using PrimalFrontier.Survival;

namespace PrimalFrontier.UI
{
    /// <summary>
    /// In-game HUD: vitals (top left), compass + day/time (top right), hotbar 1-8, interaction prompt, short
    /// notifications, crafting progress, crosshair when aiming / building, subtitles, chapter banner (GameManager /
    /// intro: "DAY N"), fade and lightning flash. Minimal and dark so the island stays the star.
    /// PC phase: the old objective box and the "DAY ONE SURVIVED" banner are gone (STORY's ObjectiveUI shows the current
    /// mission; the compass marker still reads TutorialManager.CurrentTarget = the mission marker). A hazard warning line
    /// (GameEventType.HazardWarning: amount 1 warm, 2 hot, 3 dangerous, 0 clears) sits above the prompt.
    /// Hunger / thirst show their SurvivalConfig tier word ("Thirsty") inside the bar; the hotbar water count takes the
    /// water type colour. Texts are rebuilt only when their value changes (no per-frame string allocations).
    /// Phase 3: a row of status icons under the vitals (PlayerStatusEffects: bleeding, food poisoning, wet, cold, leg / arm
    /// injury, recovering; rebuilt only when the effects change), a thin wetness bar under the temperature, "+30 Hydration"
    /// notes, containers in ml, spoiling food tinted in the hotbar, one note for several recipes learned together, tool
    /// break feedback (ToolBreak sound + a puff) and no repeat of the same note within a second.
    /// </summary>
    public class HUDManager : MonoBehaviour, IBakeableUI
    {
        public static HUDManager Instance { get; private set; }

        GameObject _player; PlayerHealth _hp; PlayerSurvival _sv; InventorySystem _inv; PlayerInteraction _pi; CraftingSystem _craft; PlayerCombat _combat;
        PlayerStatusEffects _fx; int _cFxVersion = -1; RectTransform _statusRow; readonly List<Image> _statusIcons = new List<Image>(); readonly List<string> _statusIds = new List<string>();
        Image _wetBar; Image _wetIcon; int _cWet = -1;
        readonly List<string> _learnedNames = new List<string>(); Sprite _learnedIcon; float _learnedFlushAt = -1f;
        string _lastNote; float _lastNoteT;
        const int MaxStatusIcons = 8;
        Canvas _canvas, _top; CanvasGroup _hudGroup;
        Image _hpBar, _hungerBar, _thirstBar, _staminaBar, _tempBar; Text _hpVal, _hungerVal, _thirstVal, _staminaVal, _tempVal, _status;
        Text _hungerTier, _thirstTier;
        // last values written into the texts (rebuild on change only)
        int _cHp = int.MinValue, _cHunger = int.MinValue, _cThirst = int.MinValue, _cStamina = int.MinValue, _cTemp = int.MinValue, _cStatus = -1, _cClock = -1, _cDist = int.MinValue;
        int _cHungerTier = int.MinValue, _cThirstTier = int.MinValue; string _cHungerLabel, _cThirstLabel; bool _hungerDrain, _thirstDrain;
        Text _hazard; CanvasGroup _hazardGroup; int _hazardLevel; float _hazardPulse;
        string _cPrompt, _cPromptSub, _cHold; bool _cPromptSet;
        readonly string[] _statusTexts = new string[32];
        RectTransform _compassStrip; Text _clock; Image _marker; Text _markerDist;
        readonly List<Image> _slotBg = new List<Image>(); readonly List<Image> _slotIcon = new List<Image>(); readonly List<Text> _slotCount = new List<Text>(); readonly List<Image> _slotDur = new List<Image>();
        Text _hotbarName; float _hotbarNameT;
        Text _prompt, _promptSub, _promptKey; Image _promptKeyBg; Image _hold; CanvasGroup _promptGroup;
        RectTransform _notes; readonly List<(CanvasGroup g, float t)> _noteItems = new List<(CanvasGroup, float)>();
        Text _subtitle; CanvasGroup _subGroup; float _subUntil;
        Text _bannerTitle, _bannerSub; CanvasGroup _bannerGroup; float _bannerUntil;
        Image _fade, _flash; float _fadeFrom, _fadeTo, _fadeDur, _fadeT; float _flashT;
        Image _cross; Image _craftIcon, _craftBar; CanvasGroup _craftGroup;
        bool _hudVisible = true;
        readonly Dictionary<string, (int n, float t, Text txt, CanvasGroup g)> _stacked = new Dictionary<string, (int, float, Text, CanvasGroup)>();

        void Awake()
        {
            Instance = this; Build();
            // texts the game fills in while playing (the saved scene may hold sample text for layout work)
            foreach (var t in new[] { _prompt, _promptSub, _hotbarName, _subtitle, _bannerTitle, _bannerSub, _markerDist, _status, _hungerTier, _thirstTier, _hazard }) if (t) t.text = "";
        }

        /// <summary>editor baker: write the HUD into the scene, with sample text so the hidden parts can be laid out</summary>
        public void BakeLayout()
        {
            Build();
            foreach (var g in new[] { _promptGroup, _craftGroup, _subGroup, _bannerGroup, _hazardGroup }) if (g) g.alpha = 1f;
            void Sample(Text t, string v) { if (t && string.IsNullOrEmpty(t.text)) t.text = v; }
            Sample(_hazard, "The ground is warm here");
            Sample(_prompt, "Pick up Driftwood"); Sample(_promptSub, "Hold E: gather"); Sample(_hotbarName, "Stone Axe");
            Sample(_subtitle, "What was that?"); Sample(_bannerTitle, "DAY 1"); Sample(_bannerSub, "Dawn"); Sample(_markerDist, "120 m");
            Sample(_hungerTier, "Peckish"); Sample(_thirstTier, "Thirsty");
        }
        void OnDestroy()
        {
            if (Instance == this) Instance = null; PlayerInteraction.Message -= Notify; GameEvents.Raised -= OnEvent;
            InventorySystem.ToolBroke -= OnToolBroke;
            if (_fx) _fx.Message -= Notify;
            if (_sv) { _sv.NeedRestored -= OnNeedRestored; _sv.FoodStageChanged -= OnFoodStage; }
        }

        public void Bind(GameObject player)
        {
            _player = player;
            _cHp = _cHunger = _cThirst = _cStamina = _cTemp = _cDist = _cHungerTier = _cThirstTier = int.MinValue; _cStatus = _cClock = -1;
            _cHungerLabel = _cThirstLabel = null; _cPromptSet = false;
            SetHazard(0);
            _hp = player.GetComponent<PlayerHealth>(); _sv = player.GetComponent<PlayerSurvival>(); _inv = player.GetComponent<InventorySystem>();
            _pi = player.GetComponent<PlayerInteraction>(); _craft = player.GetComponent<CraftingSystem>(); _combat = player.GetComponent<PlayerCombat>();
            if (_inv) { _inv.Changed -= RefreshHotbar; _inv.Changed += RefreshHotbar; _inv.ActiveSlotChanged -= OnActive; _inv.ActiveSlotChanged += OnActive; }
            if (_sv) { _sv.Warning -= Notify; _sv.Warning += Notify; }
            PlayerInteraction.Message -= Notify; PlayerInteraction.Message += Notify;
            GameEvents.Raised -= OnEvent; GameEvents.Raised += OnEvent;
            var j = JournalSystem.Instance; if (j) { j.Unlocked -= OnJournal; j.Unlocked += OnJournal; }
            if (_craft) { _craft.Learned -= OnLearned; _craft.Learned += OnLearned; }
            if (_fx) { _fx.Message -= Notify; }
            _fx = player.GetComponent<PlayerStatusEffects>(); _cFxVersion = -1;
            if (_fx) { _fx.Message -= Notify; _fx.Message += Notify; }
            if (_sv)
            {
                _sv.NeedRestored -= OnNeedRestored; _sv.NeedRestored += OnNeedRestored;
                _sv.FoodStageChanged -= OnFoodStage; _sv.FoodStageChanged += OnFoodStage;
            }
            InventorySystem.ToolBroke -= OnToolBroke; InventorySystem.ToolBroke += OnToolBroke;
            RefreshHotbar();
        }

        // ================================================================== build
        void Build()
        {
            UIFactory.BeginBuild();
            try { BuildLayout(); } finally { UIFactory.EndBuild(); }
        }

        void BuildLayout()
        {
            _canvas = UIFactory.Canvas("[HUD]", 10, transform);
            _hudGroup = UIFactory.Group(_canvas.gameObject); _hudGroup.interactable = false; _hudGroup.blocksRaycasts = false;
            var root = _canvas.transform;

            // ---- vitals (top left)
            var vit = UIFactory.Image(root, "Vitals", UIStyle.PanelDark, new Color(1, 1, 1, 0.85f), new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -24), new Vector2(330, 196));
            float y = -20;
            void Row(string icon, Color c, out Image bar, out Text val)
            {
                UIFactory.Image(vit.transform, icon, UIStyle.Icon(icon), c, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(18, y - 12), new Vector2(26, 26));
                bar = UIFactory.Bar(vit.transform, icon + "Bar", c, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(54, y - 12), new Vector2(200, 16));
                val = UIFactory.Label(vit.transform, icon + "Val", "100", 18, UIStyle.Text, TextAnchor.MiddleRight, UIStyle.Body, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(258, y - 12), new Vector2(56, 24));
                y -= 32;
            }
            Row("health", UIStyle.Health, out _hpBar, out _hpVal);
            Row("hunger", UIStyle.Hunger, out _hungerBar, out _hungerVal);
            Row("thirst", UIStyle.Thirst, out _thirstBar, out _thirstVal);
            Row("stamina", UIStyle.Stamina, out _staminaBar, out _staminaVal);
            Row("temperature", UIStyle.Warm, out _tempBar, out _tempVal);
            // tier words over the empty (right) end of the hunger / thirst bars
            Text Tier(string name, float rowY) => UIFactory.Label(vit.transform, name, "", 14, UIStyle.Text, TextAnchor.MiddleRight, UIStyle.Body, new Vector2(0, 1), new Vector2(0, 1), new Vector2(1, 0.5f), new Vector2(250, rowY - 12), new Vector2(160, 20));
            _hungerTier = Tier("hungerTier", -20 - 32);
            _thirstTier = Tier("thirstTier", -20 - 64);
            _status = UIFactory.Label(vit.transform, "Status", "", 16, UIStyle.Accent, TextAnchor.UpperLeft, UIStyle.Body, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(18, -26), new Vector2(-30, 24));
            // wetness: a thin bar under the temperature row (shown while wet)
            float wetY = -20 - 4 * 32 - 12 - 18;
            _wetIcon = UIFactory.Image(vit.transform, "WetnessIcon", UIStyle.Icon("status_wet"), new Color(0.55f, 0.78f, 1f, 0.95f), new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(34, wetY), new Vector2(16, 16));
            _wetBar = UIFactory.Bar(vit.transform, "WetnessBar", new Color(0.45f, 0.7f, 0.95f), new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(54, wetY), new Vector2(200, 10));
            // status effect icons (PC layout): one row under the vitals panel, left aligned with it, below the status words line
            // (panel 24 + 196 px, words line 26 px, 8 px gap); 8 icons x 40 px = 320 px fit the 330 px panel width. The top row
            // stays clear for the perception indicator ("Presence", x 352-448) and the compass on the right.
            const float StatusRowY = -24 - 196 - 26 - 8;
            _statusRow = UIFactory.Rect(root, "StatusIcons", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, StatusRowY), new Vector2(MaxStatusIcons * 40, 38));
            _statusIcons.Clear();
            for (int i = 0; i < MaxStatusIcons; i++)
            {
                var bg = UIFactory.Image(_statusRow, "Status" + i, UIStyle.Slot, new Color(1, 1, 1, 0.85f), new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(i * 40, 0), new Vector2(36, 36));
                var ic = UIFactory.Image(bg.transform, "Icon", null, Color.white, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                if (UIFactory.Fresh(ic)) { ic.rectTransform.offsetMin = new Vector2(4, 4); ic.rectTransform.offsetMax = new Vector2(-4, -4); }
                ic.preserveAspect = true; ic.raycastTarget = false;
                bg.gameObject.SetActive(false);
                _statusIcons.Add(ic);
            }

            // ---- compass + clock (top right)
            var comp = UIFactory.Image(root, "Compass", UIStyle.PanelDark, new Color(1, 1, 1, 0.8f), new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -24), new Vector2(360, 44));
            var mask = UIFactory.Stretch(comp.transform, "Mask", 6f); mask.gameObject.GetOrAdd<RectMask2D>();
            _compassStrip = UIFactory.Rect(mask, "Strip", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1440 * 2, 40));
            string[] labels = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            for (int rep = -1; rep <= 1; rep++)
                for (int k = 0; k < 24; k++)
                {
                    float x = (rep * 360 + k * 15) * 4f;
                    bool major = k % 3 == 0;
                    if (major) UIFactory.Label(_compassStrip, "L", labels[k / 3], k % 6 == 0 ? 22 : 16, k == 0 ? UIStyle.Accent : UIStyle.Text, TextAnchor.MiddleCenter, UIStyle.Head, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, 0), new Vector2(60, 36));
                    else UIFactory.Image(_compassStrip, "T", null, new Color(1, 1, 1, 0.35f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, 0), new Vector2(2, 10));
                }
            UIFactory.Image(comp.transform, "Needle", null, UIStyle.Accent, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 2), new Vector2(3, 8));
            _marker = UIFactory.Image(mask, "Objective", null, UIStyle.Accent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(12, 12));
            if (UIFactory.Fresh(_marker)) _marker.rectTransform.localRotation = Quaternion.Euler(0, 0, 45f);
            _markerDist = UIFactory.Label(root, "ObjectiveDist", "", 16, UIStyle.Accent, TextAnchor.UpperCenter, UIStyle.Body, new Vector2(1, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(-204, -70), new Vector2(120, 22));
            _clock = UIFactory.Label(root, "Clock", "Day 1  09:00", 18, UIStyle.TextDim, TextAnchor.UpperRight, UIStyle.Body, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-28, -74), new Vector2(300, 26));

            // ---- the old objective box: STORY's ObjectiveUI replaced it. A saved scene may still hold it: switched off, not rebuilt
            var oldBox = root.Find("Objective"); if (oldBox && !oldBox.GetComponent<Image>()) oldBox.gameObject.SetActive(false);

            // ---- hotbar (bottom centre)
            var hb = UIFactory.Rect(root, "Hotbar", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 22), new Vector2(8 * 76 + 16, 92));
            UIFactory.Image(hb, "Bg", UIStyle.PanelDark, new Color(1, 1, 1, 0.75f), Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            for (int i = 0; i < 8; i++)
            {
                var bg = UIFactory.Image(hb, "Slot" + i, UIStyle.Slot, Color.white, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12 + i * 76, 0), new Vector2(70, 70));
                var icon = UIFactory.Image(bg.transform, "Icon", null, Color.white, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                if (UIFactory.Fresh(icon)) { icon.rectTransform.offsetMin = new Vector2(8, 8); icon.rectTransform.offsetMax = new Vector2(-8, -8); }
                icon.preserveAspect = true; icon.enabled = false;
                UIFactory.Label(bg.transform, "Key", (i + 1).ToString(), 14, UIStyle.TextDim, TextAnchor.UpperLeft, UIStyle.Body, Vector2.zero, Vector2.one, new Vector2(0, 1), new Vector2(5, -2), Vector2.zero);
                var cnt = UIFactory.Label(bg.transform, "Count", "", 16, UIStyle.Text, TextAnchor.LowerRight, UIStyle.Body, Vector2.zero, Vector2.one, new Vector2(1, 0), new Vector2(-5, 3), Vector2.zero);
                if (UIFactory.Fresh(cnt)) { cnt.rectTransform.offsetMin = new Vector2(0, 2); cnt.rectTransform.offsetMax = new Vector2(-6, 0); }
                var dur = UIFactory.Image(bg.transform, "Dur", UIStyle.BarFill, UIStyle.Good, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(8, 5), new Vector2(-16, 4));
                dur.type = Image.Type.Filled; dur.fillMethod = Image.FillMethod.Horizontal; dur.enabled = false;
                _slotBg.Add(bg); _slotIcon.Add(icon); _slotCount.Add(cnt); _slotDur.Add(dur);
            }
            _hotbarName = UIFactory.Label(root, "HotbarName", "", 20, UIStyle.Text, TextAnchor.MiddleCenter, UIStyle.Head, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 118), new Vector2(500, 28));

            // ---- prompt
            var pr = UIFactory.Rect(root, "Prompt", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 190), new Vector2(620, 80));
            _promptGroup = UIFactory.Group(pr.gameObject);
            _promptKeyBg = UIFactory.Image(pr, "KeyBg", UIStyle.Slot, Color.white, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(1, 1), new Vector2(-8, 0), new Vector2(40, 40));
            _promptKey = UIFactory.Label(_promptKeyBg.transform, "Key", "E", 22, UIStyle.Accent, TextAnchor.MiddleCenter, UIStyle.Head);
            _prompt = UIFactory.Label(pr, "Text", "", 24, UIStyle.Text, TextAnchor.MiddleLeft, UIStyle.Body, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, 1), new Vector2(4, 0), new Vector2(560, 40));
            _promptSub = UIFactory.Label(pr, "Sub", "", 17, UIStyle.TextDim, TextAnchor.UpperCenter, UIStyle.Body, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 8), new Vector2(0, 26));
            _hold = UIFactory.Image(pr, "Hold", UIStyle.BarFill, UIStyle.Accent, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -44), new Vector2(200, 4));
            _hold.type = Image.Type.Filled; _hold.fillMethod = Image.FillMethod.Horizontal; _hold.fillAmount = 0f;
            _promptGroup.alpha = 0f;

            // ---- hazard warning (centre, above the prompt): volcano heat / lava, from GameEventType.HazardWarning
            var hz = UIFactory.Rect(root, "Hazard", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 286), new Vector2(620, 36));
            _hazardGroup = UIFactory.Group(hz.gameObject);
            UIFactory.Image(hz, "Bg", UIStyle.PanelDark, new Color(1, 1, 1, 0.65f), Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _hazard = UIFactory.Label(hz, "Text", "", 19, UIStyle.Accent, TextAnchor.MiddleCenter, UIStyle.Body, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _hazardGroup.alpha = 0f;

            // ---- notifications (left, above hotbar level)
            _notes = UIFactory.Rect(root, "Notes", new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(28, 140), new Vector2(520, 300));

            // ---- craft progress (right of the hotbar)
            var cp = UIFactory.Rect(root, "Craft", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 0), new Vector2(8 * 38 + 24, 30), new Vector2(200, 60));
            _craftGroup = UIFactory.Group(cp.gameObject);
            UIFactory.Image(cp, "Bg", UIStyle.PanelDark, new Color(1, 1, 1, 0.75f), Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _craftIcon = UIFactory.Image(cp, "Icon", null, Color.white, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(44, 44)); _craftIcon.preserveAspect = true;
            _craftBar = UIFactory.Bar(cp, "Bar", UIStyle.Accent, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(60, 0), new Vector2(128, 12));
            _craftGroup.alpha = 0f;

            // ---- crosshair
            _cross = UIFactory.Image(root, "Crosshair", UIStyle.Icon("crosshair"), new Color(1, 1, 1, 0.85f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(28, 28));
            _cross.enabled = false;

            // ---- top canvas: subtitle, banner, fade, flash (above menus)
            _top = UIFactory.Canvas("[HUD Top]", 100, transform);
            var tg = UIFactory.Group(_top.gameObject); tg.interactable = false; tg.blocksRaycasts = false;
            _fade = UIFactory.Fill(_top.transform, "Fade", null, Color.black); _fade.color = new Color(0, 0, 0, 0);
            _flash = UIFactory.Fill(_top.transform, "Flash", null, new Color(0.85f, 0.9f, 1f, 0));
            var sub = UIFactory.Rect(_top.transform, "Subtitle", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 250), new Vector2(1200, 60));
            _subGroup = UIFactory.Group(sub.gameObject);
            _subtitle = UIFactory.Label(sub, "Text", "", 34, UIStyle.Text, TextAnchor.MiddleCenter, UIStyle.Hand);
            _subGroup.alpha = 0f;
            var ban = UIFactory.Rect(_top.transform, "Banner", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 160), new Vector2(1000, 160));
            _bannerGroup = UIFactory.Group(ban.gameObject);
            UIFactory.Image(ban, "Line", UIStyle.BarFill, new Color(UIStyle.Accent.r, UIStyle.Accent.g, UIStyle.Accent.b, 0.8f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -8), new Vector2(420, 2));
            _bannerTitle = UIFactory.Label(ban, "Title", "", 72, UIStyle.Text, TextAnchor.LowerCenter, UIStyle.Head, new Vector2(0, 0.5f), new Vector2(1, 1), new Vector2(0.5f, 0), new Vector2(0, 0), Vector2.zero);
            _bannerSub = UIFactory.Label(ban, "Sub", "", 28, UIStyle.Accent, TextAnchor.UpperCenter, UIStyle.Head, new Vector2(0, 0), new Vector2(1, 0.5f), new Vector2(0.5f, 1), new Vector2(0, -18), Vector2.zero);
            _bannerGroup.alpha = 0f;
        }

        // ================================================================== API
        public void SetHudVisible(bool on) { _hudVisible = on; }
        public float HudAlpha => _hudGroup ? _hudGroup.alpha : 1f;
        public void Fade(float to, float seconds) { _fadeFrom = _fade.color.a; _fadeTo = to; _fadeDur = Mathf.Max(0.0001f, seconds); _fadeT = 0f; if (seconds <= 0f) SetFade(to); }
        void SetFade(float a) { var c = _fade.color; c.a = a; _fade.color = c; }
        public float FadeAlpha => _fade.color.a;
        public void Flash(float strength) { _flashT = strength; }
        public void ShowSubtitle(string text, float seconds, bool italic) { _subtitle.text = text; _subtitle.font = italic ? UIStyle.Hand : UIStyle.Body; _subUntil = Time.unscaledTime + seconds; }
        public void ShowBanner(string title, string sub, float seconds)
        {
            _bannerTitle.text = title; _bannerSub.text = sub; _bannerUntil = Time.unscaledTime + seconds;
            Audio.SfxPlayer.Instance.Play2D(Audio.SfxId.UiObjective, 0.8f);
        }

        /// <summary>a short note; the same text again within a second is dropped (several systems may report one thing)</summary>
        public void Notify(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return;
            if (msg == _lastNote && Time.unscaledTime - _lastNoteT < 1f) return;
            _lastNote = msg; _lastNoteT = Time.unscaledTime;
            AddNote(msg, null, null);
        }

        void OnNeedRestored(string need, float amount)
        {
            int n = Mathf.RoundToInt(amount); if (n <= 0) return;
            bool thirst = need == PlayerSurvival.ThirstEventId;
            string key = thirst ? "restore_thirst" : "restore_hunger";
            int total = n + (_stacked.TryGetValue(key, out var st) && st.g && Time.unscaledTime - st.t < 3f ? AddedTotal(key) : 0);
            _added[key] = total;
            AddNote("+" + NumText(total) + (thirst ? "  Hydration" : "  Food"), UIStyle.Icon(thirst ? "thirst" : "hunger"), key);
        }

        void OnFoodStage(ItemDefinition item, FoodStage stage)
        {
            if (!item) return;
            AddNote(stage == FoodStage.Spoiled ? item.displayName + " has spoiled." : item.displayName + " is getting old. Eat or cook it soon.", item.icon, "spoil_" + item.id);
            GameEvents.Raise(GameEventType.FoodSpoiled, item.id, (int)stage);
        }

        void OnToolBroke(ItemDefinition item, InventorySystem inv)
        {
            if (!item || !inv || inv != _inv) return;
            var at = inv.transform.position + Vector3.up * 1.1f + inv.transform.forward * 0.3f;
            Audio.SfxPlayer.Instance.Play(Audio.SfxId.ToolBreak, at, 0.9f);
            VFX.VfxPool.Instance.Play(VFX.VfxId.DustImpact, at, Vector3.up, null, 0.5f);
            Notify(item.displayName + " broke!");
        }

        void AddNote(string msg, Sprite icon, string stackKey)
        {
            if (string.IsNullOrEmpty(msg)) return;
            if (stackKey != null && _stacked.TryGetValue(stackKey, out var s) && s.g && Time.unscaledTime - s.t < 3f)
            {
                int n = s.n + 1; _stacked[stackKey] = (n, Time.unscaledTime, s.txt, s.g);
                s.txt.text = msg; for (int i = 0; i < _noteItems.Count; i++) if (_noteItems[i].g == s.g) _noteItems[i] = (s.g, Time.unscaledTime);
                return;
            }
            var rt = UIFactory.Rect(_notes, "Note", new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(520, 34));
            var g = UIFactory.Group(rt.gameObject);
            UIFactory.Image(rt, "Bg", UIStyle.PanelDark, new Color(1, 1, 1, 0.6f), Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            float x = 12;
            if (icon) { var im = UIFactory.Image(rt, "Icon", icon, Color.white, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(28, 28)); im.preserveAspect = true; x = 42; }
            var t = UIFactory.Label(rt, "Text", msg, 19, UIStyle.Text, TextAnchor.MiddleLeft, UIStyle.Body, Vector2.zero, Vector2.one, new Vector2(0, 0.5f), Vector2.zero, Vector2.zero);
            t.rectTransform.offsetMin = new Vector2(x, 0); t.rectTransform.offsetMax = new Vector2(-8, 0);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            float w = Mathf.Min(900f, t.preferredWidth + x + 20f); rt.sizeDelta = new Vector2(w, 34);
            _noteItems.Insert(0, (g, Time.unscaledTime));
            if (stackKey != null) _stacked[stackKey] = (1, Time.unscaledTime, t, g);
            while (_noteItems.Count > 6) { var last = _noteItems[_noteItems.Count - 1]; _noteItems.RemoveAt(_noteItems.Count - 1); if (last.g) Destroy(last.g.gameObject); }
        }

        // ================================================================== events
        void OnEvent(GameEvent e)
        {
            var db = ItemDatabase.Instance;
            switch (e.type)
            {
                case GameEventType.ItemAdded:
                {
                    var it = db ? db.Item(e.id) : null; if (it == null) break;
                    string key = "add_" + e.id;
                    int total = e.amount + (_stacked.TryGetValue(key, out var s) && s.g && Time.unscaledTime - s.t < 3f ? AddedTotal(key) : 0);
                    _added[key] = total;
                    AddNote($"+{total}  {it.displayName}", it.icon, key);
                    break;
                }
                case GameEventType.ItemCrafted:
                {
                    var it = db ? db.Item(e.id) : null; if (it != null) AddNote("Crafted: " + it.displayName, it.icon, null);
                    Audio.SfxPlayer.Instance.Play2D(Audio.SfxId.UiRecipe, 0.7f);
                    break;
                }
                case GameEventType.GameSaved: AddNote("Game saved.", null, "saved"); break;
                case GameEventType.HazardWarning: SetHazard(e.amount, e.id); break;
            }
        }

        /// <summary>the hazard line: 0 clears it; 1 warm, 2 hot, 3 dangerous (WORLD's HazardZoneMonitor raises it on each change)</summary>
        public void SetHazard(int level, string id = null)
        {
            level = Mathf.Clamp(level, 0, 3);
            if (level == _hazardLevel && level == 0) return;
            _hazardLevel = level;
            if (!_hazard) return;
            bool lava = id == "lava";
            _hazard.text = level switch
            {
                1 => lava ? "The ground is warm near the lava" : "The ground is warm here",
                2 => lava ? "Hot air rises from the lava. Do not stay" : "The heat is rising. Do not stay long",
                3 => lava ? "Get away from the lava!" : "Dangerous heat! Get away from here",
                _ => "",
            };
            _hazard.color = level >= 3 ? UIStyle.Bad : level == 2 ? new Color(1f, 0.66f, 0.26f) : UIStyle.Accent;
            _hazardPulse = 1f;
        }
        readonly Dictionary<string, int> _added = new Dictionary<string, int>();
        int AddedTotal(string key) => _added.TryGetValue(key, out var n) ? n : 0;

        void OnJournal(JournalSystem.Entry e) { AddNote("Journal: " + e.title + "   [J]", UIStyle.Icon("journal"), null); }
        /// <summary>recipes learned in the same moment (one pickup can teach several) become one note, posted a moment later</summary>
        void OnLearned(RecipeDefinition r)
        {
            if (!r) return;
            _learnedNames.Add(r.output ? r.output.displayName : r.id);
            if (_learnedNames.Count == 1) _learnedIcon = r.output ? r.output.icon : null;
            if (_learnedFlushAt < 0f) _learnedFlushAt = Time.unscaledTime + 0.25f;
        }

        void FlushLearned()
        {
            _learnedFlushAt = -1f;
            if (_learnedNames.Count == 0) return;
            string msg = _learnedNames.Count == 1 ? "New recipe: " + _learnedNames[0] : "New recipes: " + string.Join(", ", _learnedNames);
            AddNote(msg, _learnedIcon, null);
            _learnedNames.Clear(); _learnedIcon = null;
        }
        void OnActive(int i)
        {
            RefreshHotbar(); var it = _inv ? _inv.ActiveItem : null; var st = _inv ? _inv.ActiveStack : null;
            _hotbarName.text = !it ? "" : it.IsWaterContainer && st != null && st.water > 0 ? it.displayName + ": " + WaterRules.NameOf(st) : it.displayName;     // "Gourd: Clean Water (Hot)"
            _hotbarNameT = Time.unscaledTime;
        }

        void RefreshHotbar()
        {
            if (_inv == null || _inv.Slots == null) return;
            for (int i = 0; i < 8 && i < _inv.Slots.Length; i++)
            {
                var s = _inv.Get(i);
                _slotBg[i].sprite = i == _inv.ActiveSlot ? UIStyle.SlotActive : UIStyle.Slot;
                _slotIcon[i].enabled = s != null && s.item.icon; if (s != null) _slotIcon[i].sprite = s.item.icon;
                _slotCount[i].text = SlotCountText(s);
                _slotCount[i].color = WaterCountColor(s);
                if (s != null) _slotIcon[i].color = Spoilage.Tint(Spoilage.Stage(s));
                bool dur = s != null && s.item.HasDurability;
                _slotDur[i].enabled = dur; if (dur) { float k = Mathf.Clamp01(s.durability / s.item.maxDurability); _slotDur[i].fillAmount = k; _slotDur[i].color = Color.Lerp(UIStyle.Bad, UIStyle.Good, k); }
            }
        }

        // ================================================================== per frame
        void Update()
        {
            float udt = Time.unscaledDeltaTime;
            // fade / flash
            if (_fadeT < _fadeDur) { _fadeT += udt; SetFade(Mathf.Lerp(_fadeFrom, _fadeTo, Mathf.SmoothStep(0, 1, _fadeT / _fadeDur))); }
            _flashT = Mathf.MoveTowards(_flashT, 0f, udt * 1.6f); var fc = _flash.color; fc.a = _flashT; _flash.color = fc;
            _subGroup.alpha = Mathf.MoveTowards(_subGroup.alpha, Time.unscaledTime < _subUntil ? 1f : 0f, udt * 2.5f);
            _bannerGroup.alpha = Mathf.MoveTowards(_bannerGroup.alpha, Time.unscaledTime < _bannerUntil ? 1f : 0f, udt * 1.2f);

            bool menuOpen = UIManager.Instance && UIManager.Instance.Current != UIScreen.None;
            _hudGroup.alpha = Mathf.MoveTowards(_hudGroup.alpha, _hudVisible && !(UIManager.Instance && (UIManager.Instance.Current == UIScreen.Title || UIManager.Instance.Current == UIScreen.Pause)) ? 1f : 0f, udt * 4f);

            // notes fade out after 4 s and stack upwards
            for (int i = _noteItems.Count - 1; i >= 0; i--)
            {
                var (g, t) = _noteItems[i];
                if (!g) { _noteItems.RemoveAt(i); continue; }
                float age = Time.unscaledTime - t;
                g.alpha = Mathf.Clamp01(Mathf.Min(age * 6f, (5f - age)));
                var rt = (RectTransform)g.transform; rt.anchoredPosition = Vector2.Lerp(rt.anchoredPosition, new Vector2(0, i * 38f), udt * 12f);
                if (age > 5f) { Destroy(g.gameObject); _noteItems.RemoveAt(i); }
            }
            _hotbarName.color = new Color(1, 1, 1, Mathf.Clamp01(2.2f - (Time.unscaledTime - _hotbarNameT))) * UIStyle.Text;
            if (_learnedFlushAt >= 0f && Time.unscaledTime >= _learnedFlushAt) FlushLearned();
            // hazard line (pulses briefly on a change, stays while the level is above 0)
            if (_hazardGroup)
            {
                bool hz = _hazardLevel > 0 && !menuOpen;
                _hazardPulse = Mathf.MoveTowards(_hazardPulse, 0f, udt * 1.5f);
                float want = hz ? (_hazardLevel >= 3 ? 0.75f + 0.25f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3f)) : 1f) : 0f;
                _hazardGroup.alpha = Mathf.MoveTowards(_hazardGroup.alpha, want, udt * 3f);
                if (_hazard) _hazard.transform.localScale = Vector3.one * (1f + 0.08f * _hazardPulse);
            }

            if (_player == null) return;
            // vitals (texts only when the shown value changes)
            if (_hp) { _hpBar.fillAmount = _hp.Normalized; SetInt(_hpVal, Mathf.CeilToInt(_hp.Health), ref _cHp); }
            if (_sv)
            {
                _hungerBar.fillAmount = _sv.Hunger / 100f; SetInt(_hungerVal, Mathf.CeilToInt(_sv.Hunger), ref _cHunger);
                _thirstBar.fillAmount = _sv.Thirst / 100f; SetInt(_thirstVal, Mathf.CeilToInt(_sv.Thirst), ref _cThirst);
                _staminaBar.fillAmount = _sv.Stamina / _sv.maxStamina; SetInt(_staminaVal, Mathf.CeilToInt(_sv.Stamina), ref _cStamina);
                float bt = _sv.BodyTemperature; _tempBar.fillAmount = Mathf.InverseLerp(33f, 38.5f, bt);
                _tempBar.color = bt < _sv.coldBody ? UIStyle.Cold : bt > 37.6f ? UIStyle.Bad : UIStyle.Warm;
                int bt10 = Mathf.RoundToInt(bt * 10f);
                if (bt10 != _cTemp) { _cTemp = bt10; _tempVal.text = (bt10 / 10f).ToString("0.0", CultureInfo.InvariantCulture) + "°"; }
                var cfg = SurvivalConfig.Instance;
                SetTier(_hungerTier, _sv.HungerTier, _sv.HungerTierLabel, cfg.hungerTiers, ref _cHungerTier, ref _cHungerLabel, ref _hungerDrain);
                SetTier(_thirstTier, _sv.ThirstTier, _sv.ThirstTierLabel, cfg.thirstTiers, ref _cThirstTier, ref _cThirstLabel, ref _thirstDrain);
                Pulse(_hungerBar, _hungerDrain); Pulse(_thirstBar, _thirstDrain); Pulse(_hpBar, _hp && _hp.Normalized < 0.25f); Pulse(_tempBar, _sv.IsCold);
                if (!_fx) _fx = _player.GetComponent<PlayerStatusEffects>();
                int ver = (_fx ? _fx.Version : 0) * 2 + (_sv.Overweight ? 1 : 0);
                if (ver != _cFxVersion) { _cFxVersion = ver; RefreshStatus(); }
                PulseStatus();
                int wet = Mathf.RoundToInt(_sv.Wetness * 50f);
                if (wet != _cWet)
                {
                    _cWet = wet;
                    bool wetShown = wet > 0;
                    if (_wetBar) { _wetBar.fillAmount = _sv.Wetness; var back = _wetBar.transform.parent; if (back && back.gameObject.activeSelf != wetShown) back.gameObject.SetActive(wetShown); }
                    if (_wetIcon && _wetIcon.enabled != wetShown) _wetIcon.enabled = wetShown;
                }
            }
            // compass
            var cam = Camera.main;
            if (cam) _compassStrip.anchoredPosition = new Vector2(-Mathf.Repeat(cam.transform.eulerAngles.y, 360f) * 4f, 0);
            // objective marker on the compass
            var target = TutorialManager.Instance ? TutorialManager.Instance.CurrentTarget : null;
            bool mk = target.HasValue && cam && _player;
            if (mk)
            {
                Vector3 d = target.Value - _player.transform.position; float dist = new Vector2(d.x, d.z).magnitude;
                float bearing = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; float rel = Mathf.DeltaAngle(cam.transform.eulerAngles.y, bearing);
                rel = Mathf.Clamp(rel, -42f, 42f);
                _marker.rectTransform.anchoredPosition = new Vector2(rel * 4f, 0);
                int di = dist > 8f ? Mathf.RoundToInt(dist) : -1;
                if (di != _cDist) { _cDist = di; _markerDist.text = di < 0 ? "" : NumText(di) + " m"; }
                // under the marker, wherever the compass has been placed
                _markerDist.rectTransform.position = _marker.rectTransform.position + new Vector3(0f, -34f * (_canvas ? _canvas.scaleFactor : 1f), 0f);
            }
            _marker.enabled = mk; _markerDist.enabled = mk;
            var tm = TimeManager.Instance;
            if (tm)
            {
                int h = Mathf.FloorToInt(tm.hour), m = Mathf.FloorToInt((tm.hour - h) * 60f), key = tm.day * 1440 + h * 60 + m;
                if (key != _cClock) { _cClock = key; _clock.text = string.Format(CultureInfo.InvariantCulture, "Day {0}   {1:00}:{2:00}", tm.day, h, m); }
            }
            // prompt
            bool show = _pi && !menuOpen && (_pi.Prompt != null || _pi.HoldLabel != null);
            if (show)
            {
                if (!_cPromptSet || !ReferenceEquals(_pi.Prompt, _cPrompt) || !ReferenceEquals(_pi.PromptSub, _cPromptSub) || !ReferenceEquals(_pi.HoldLabel, _cHold))
                {
                    _cPromptSet = true; _cPrompt = _pi.Prompt; _cPromptSub = _pi.PromptSub; _cHold = _pi.HoldLabel;
                    _prompt.text = _pi.Prompt ?? _pi.HoldLabel;
                    string sub = _pi.PromptSub; if (_pi.HoldLabel != null && _pi.Prompt != null) sub = (sub != null ? sub + "   " : "") + "Hold E: " + _pi.HoldLabel;
                    _promptSub.text = sub ?? "";
                    float w = Mathf.Min(560f, _prompt.preferredWidth); var p = _prompt.rectTransform; p.anchoredPosition = new Vector2(-w * 0.5f + 20f, 0);
                    _promptKeyBg.rectTransform.anchoredPosition = new Vector2(-w * 0.5f + 12f, 0);
                }
                _prompt.color = _pi.PromptEnabled || _pi.Prompt == null ? UIStyle.Text : UIStyle.TextDim;
                _promptKey.color = _pi.PromptEnabled || _pi.Prompt == null ? UIStyle.Accent : UIStyle.TextDim;
                _hold.fillAmount = _pi.HoldProgress;
            }
            _promptGroup.alpha = Mathf.MoveTowards(_promptGroup.alpha, show ? 1f : 0f, udt * 8f);
            // crafting
            bool crafting = _craft && _craft.IsCrafting;
            if (crafting) { var j = _craft.Queue[0]; _craftIcon.sprite = j.recipe.output ? j.recipe.output.icon : null; _craftBar.fillAmount = _craft.CurrentProgress01; }
            _craftGroup.alpha = Mathf.MoveTowards(_craftGroup.alpha, crafting && !menuOpen ? 1f : 0f, udt * 4f);
            // crosshair
            bool build = Building.BuildSystem.Instance && Building.BuildSystem.Instance.Active;
            _cross.enabled = !menuOpen && ((_combat && _combat.Aiming) || build);
            if (build) _cross.color = Building.BuildSystem.Instance.Valid ? UIStyle.Good : UIStyle.Bad; else _cross.color = new Color(1, 1, 1, 0.85f);
        }

        public static readonly Color DirtyWater = new Color(0.78f, 0.6f, 0.36f);

        /// <summary>hotbar / slot count colour: the water type colour for a container with water, else the text colour</summary>
        public static Color WaterCountColor(ItemStack s)
        {
            var t = s != null && s.item != null && s.item.IsWaterContainer ? WaterRules.TypeOf(s) : WaterType.None;
            return t == WaterType.None ? UIStyle.Text : WaterRules.ColorOf(s);        // hot water: the warm colour
        }

        static readonly string[] _nums = BuildNums(301);
        static string[] BuildNums(int n) { var a = new string[n]; for (int i = 0; i < n; i++) a[i] = i.ToString(CultureInfo.InvariantCulture); return a; }
        /// <summary>cached number strings (no allocation for 0..300)</summary>
        public static string NumText(int n) => n >= 0 && n < _nums.Length ? _nums[n] : n.ToString(CultureInfo.InvariantCulture);
        static void SetInt(Text t, int v, ref int cache) { if (v == cache || !t) return; cache = v; t.text = NumText(v); }

        static void SetTier(Text t, int index, string label, SurvivalConfig.NeedTier[] tiers, ref int cacheIndex, ref string cacheLabel, ref bool drain)
        {
            if (index == cacheIndex && ReferenceEquals(label, cacheLabel)) return;
            cacheIndex = index; cacheLabel = label;
            drain = index >= 0 && tiers != null && index < tiers.Length && tiers[index].healthDrain > 0f;
            if (t) { t.text = label ?? ""; t.color = drain ? UIStyle.Bad : UIStyle.Text; }
        }

        static readonly System.Collections.Generic.Dictionary<int, string> _hotText = new System.Collections.Generic.Dictionary<int, string>();
        /// <summary>"HOT" over "500ml" (cached per amount)</summary>
        static string HotText(int charges)
        {
            if (!_hotText.TryGetValue(charges, out var t)) _hotText[charges] = t = "HOT\n" + WaterRules.MlShort(charges);
            return t;
        }

        /// <summary>"500ml" for a container ("HOT" above it while the water is hot), the count for a stack, "" for one item</summary>
        public static string SlotCountText(ItemStack s)
        {
            if (s == null) return "";
            if (s.item.IsWaterContainer) return WaterRules.IsHot(s) ? HotText(s.water) : WaterRules.MlShort(s.water);
            return s.count > 1 ? NumText(s.count) : "";
        }

        /// <summary>status icons + words from the player's effects (only when they changed)</summary>
        void RefreshStatus()
        {
            _statusIds.Clear();
            var sb = new System.Text.StringBuilder(64);
            int n = 0;
            if (_fx)
            {
                // HUD order: lower hudOrder first (small list: a simple selection pass)
                var list = _fx.ActiveEffects; int count = list.Count;
                var used = new bool[count];
                for (int k = 0; k < count; k++)
                {
                    int best = -1;
                    for (int i = 0; i < count; i++) if (!used[i] && list[i].def && list[i].def.showOnHud && (best < 0 || list[i].def.hudOrder < list[best].def.hudOrder)) best = i;
                    if (best < 0) break;
                    used[best] = true;
                    var d = list[best].def;
                    if (n < _statusIcons.Count)
                    {
                        var ic = _statusIcons[n];
                        ic.sprite = d.icon ? d.icon : UIStyle.Icon(string.IsNullOrEmpty(d.iconName) ? "status_" + d.id : d.iconName);
                        ic.color = d.color;
                        ic.transform.parent.gameObject.SetActive(true);
                        _statusIds.Add(d.id);
                        n++;
                    }
                    sb.Append(d.displayName).Append("  ");
                }
            }
            for (int i = n; i < _statusIcons.Count; i++) _statusIcons[i].transform.parent.gameObject.SetActive(false);
            if (_sv && _sv.Overweight) sb.Append("Overburdened  ");
            _status.text = sb.ToString();
        }

        /// <summary>the bleeding icon pulses</summary>
        void PulseStatus()
        {
            for (int i = 0; i < _statusIds.Count && i < _statusIcons.Count; i++)
            {
                if (_statusIds[i] != StatusEffectIds.Bleeding) continue;
                var ic = _statusIcons[i]; var c = ic.color; c.a = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4f)); ic.color = c;
            }
        }

        /// <summary>status line for a flag mask (bleeding 1, sick 2, wet 4, cold 8, overburdened 16), built once per mask</summary>
        string StatusText(int mask)
        {
            if (mask <= 0) return "";
            var cached = _statusTexts[mask & 31]; if (cached != null) return cached;
            var st = new System.Text.StringBuilder();
            if ((mask & 1) != 0) st.Append("Bleeding  ");
            if ((mask & 2) != 0) st.Append("Stomach sick  ");
            if ((mask & 4) != 0) st.Append("Wet  ");
            if ((mask & 8) != 0) st.Append("Cold  ");
            if ((mask & 16) != 0) st.Append("Overburdened  ");
            return _statusTexts[mask & 31] = st.ToString();
        }
        static void Pulse(Image bar, bool on)
        {
            if (!bar) return;
            var c = bar.color; float a = on ? 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4f)) : 1f; c.a = a; bar.color = c;
        }
    }
}
