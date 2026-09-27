using System.Collections.Generic;
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
    /// In-game HUD: vitals (top left), compass + day/time (top right), objective, hotbar 1-8, interaction prompt,
    /// short notifications, crafting progress, crosshair when aiming / building, subtitles, chapter banner,
    /// fade and lightning flash. Minimal and dark so the island stays the star.
    /// </summary>
    public class HUDManager : MonoBehaviour, IBakeableUI
    {
        public static HUDManager Instance { get; private set; }

        GameObject _player; PlayerHealth _hp; PlayerSurvival _sv; InventorySystem _inv; PlayerInteraction _pi; CraftingSystem _craft; PlayerCombat _combat;
        Canvas _canvas, _top; CanvasGroup _hudGroup;
        Image _hpBar, _hungerBar, _thirstBar, _staminaBar, _tempBar; Text _hpVal, _hungerVal, _thirstVal, _staminaVal, _tempVal, _status;
        RectTransform _compassStrip; Text _clock; Image _marker; Text _markerDist;
        Text _objTitle, _objText, _objHint; CanvasGroup _objGroup;
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
            foreach (var t in new[] { _objText, _objHint, _prompt, _promptSub, _hotbarName, _subtitle, _bannerTitle, _bannerSub, _markerDist, _status }) if (t) t.text = "";
        }

        /// <summary>editor baker: write the HUD into the scene, with sample text so the hidden parts can be laid out</summary>
        public void BakeLayout()
        {
            Build();
            foreach (var g in new[] { _objGroup, _promptGroup, _craftGroup, _subGroup, _bannerGroup }) if (g) g.alpha = 1f;
            void Sample(Text t, string v) { if (t && string.IsNullOrEmpty(t.text)) t.text = v; }
            Sample(_objText, "Collect wood (2/4)"); Sample(_objHint, "Driftwood lies along the beach. Press E next to it.");
            Sample(_prompt, "Pick up Driftwood"); Sample(_promptSub, "Hold E: gather"); Sample(_hotbarName, "Stone Axe");
            Sample(_subtitle, "What was that?"); Sample(_bannerTitle, "DAY 1"); Sample(_bannerSub, "Dawn"); Sample(_markerDist, "120 m");
        }
        void OnDestroy() { if (Instance == this) Instance = null; PlayerInteraction.Message -= Notify; GameEvents.Raised -= OnEvent; }

        public void Bind(GameObject player)
        {
            _player = player;
            _hp = player.GetComponent<PlayerHealth>(); _sv = player.GetComponent<PlayerSurvival>(); _inv = player.GetComponent<InventorySystem>();
            _pi = player.GetComponent<PlayerInteraction>(); _craft = player.GetComponent<CraftingSystem>(); _combat = player.GetComponent<PlayerCombat>();
            if (_inv) { _inv.Changed -= RefreshHotbar; _inv.Changed += RefreshHotbar; _inv.ActiveSlotChanged -= OnActive; _inv.ActiveSlotChanged += OnActive; }
            if (_sv) { _sv.Warning -= Notify; _sv.Warning += Notify; }
            PlayerInteraction.Message -= Notify; PlayerInteraction.Message += Notify;
            GameEvents.Raised -= OnEvent; GameEvents.Raised += OnEvent;
            var tut = TutorialManager.Instance; if (tut) { tut.StepStarted -= OnStep; tut.StepStarted += OnStep; tut.Finished -= OnTutorialDone; tut.Finished += OnTutorialDone; }
            var j = JournalSystem.Instance; if (j) { j.Unlocked -= OnJournal; j.Unlocked += OnJournal; }
            if (_craft) { _craft.Learned -= OnLearned; _craft.Learned += OnLearned; }
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
            _status = UIFactory.Label(vit.transform, "Status", "", 16, UIStyle.Accent, TextAnchor.UpperLeft, UIStyle.Body, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(18, -26), new Vector2(-30, 24));

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

            // ---- objective (right)
            var obj = UIFactory.Rect(root, "Objective", new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -110), new Vector2(380, 120));
            _objGroup = UIFactory.Group(obj.gameObject);
            UIFactory.Image(obj, "Bg", UIStyle.PanelDark, new Color(1, 1, 1, 0.7f), Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _objTitle = UIFactory.Label(obj, "Title", "OBJECTIVE", 16, UIStyle.Accent, TextAnchor.UpperLeft, UIStyle.Head, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(18, -12), new Vector2(-36, 22));
            _objText = UIFactory.Label(obj, "Text", "", 22, UIStyle.Text, TextAnchor.UpperLeft, UIStyle.Body, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(18, -36), new Vector2(-36, 30));
            _objHint = UIFactory.Label(obj, "Hint", "", 16, UIStyle.TextDim, TextAnchor.UpperLeft, UIStyle.Body, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(18, -70), new Vector2(-36, 44));
            _objGroup.alpha = 0f;

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

        public void Notify(string msg) => AddNote(msg, null, null);

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
            }
        }
        readonly Dictionary<string, int> _added = new Dictionary<string, int>();
        int AddedTotal(string key) => _added.TryGetValue(key, out var n) ? n : 0;

        void OnStep(TutorialManager.Step s)
        {
            _objGroup.alpha = 1f; _objFlash = 1f;
            Audio.SfxPlayer.Instance.Play2D(Audio.SfxId.UiObjective, 0.6f);
        }
        float _objFlash;
        void OnTutorialDone() { ShowBanner("DAY ONE SURVIVED", "The island is larger than you thought", 6f); }
        void OnJournal(JournalSystem.Entry e) { AddNote("Journal: " + e.title + "   [J]", UIStyle.Icon("journal"), null); }
        void OnLearned(RecipeDefinition r) { AddNote("New recipe: " + (r.output ? r.output.displayName : r.id), r.output ? r.output.icon : null, null); }
        void OnActive(int i) { RefreshHotbar(); var it = _inv ? _inv.ActiveItem : null; _hotbarName.text = it ? it.displayName : ""; _hotbarNameT = Time.unscaledTime; }

        void RefreshHotbar()
        {
            if (_inv == null || _inv.Slots == null) return;
            for (int i = 0; i < 8 && i < _inv.Slots.Length; i++)
            {
                var s = _inv.Get(i);
                _slotBg[i].sprite = i == _inv.ActiveSlot ? UIStyle.SlotActive : UIStyle.Slot;
                _slotIcon[i].enabled = s != null && s.item.icon; if (s != null) _slotIcon[i].sprite = s.item.icon;
                _slotCount[i].text = s == null ? "" : s.item.IsWaterContainer ? $"{s.water}/{s.item.waterCharges}" : s.count > 1 ? s.count.ToString() : "";
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

            if (_player == null) return;
            // vitals
            if (_hp) { _hpBar.fillAmount = _hp.Normalized; _hpVal.text = Mathf.CeilToInt(_hp.Health).ToString(); }
            if (_sv)
            {
                _hungerBar.fillAmount = _sv.Hunger / 100f; _hungerVal.text = Mathf.CeilToInt(_sv.Hunger).ToString();
                _thirstBar.fillAmount = _sv.Thirst / 100f; _thirstVal.text = Mathf.CeilToInt(_sv.Thirst).ToString();
                _staminaBar.fillAmount = _sv.Stamina / _sv.maxStamina; _staminaVal.text = Mathf.CeilToInt(_sv.Stamina).ToString();
                float bt = _sv.BodyTemperature; _tempBar.fillAmount = Mathf.InverseLerp(33f, 38.5f, bt);
                _tempBar.color = bt < _sv.coldBody ? UIStyle.Cold : bt > 37.6f ? UIStyle.Bad : UIStyle.Warm;
                _tempVal.text = bt.ToString("0.0") + "°";
                Pulse(_hungerBar, _sv.Hunger < 15f); Pulse(_thirstBar, _sv.Thirst < 15f); Pulse(_hpBar, _hp && _hp.Normalized < 0.25f); Pulse(_tempBar, _sv.IsCold);
                var st = new System.Text.StringBuilder();
                if (_hp && _hp.IsBleeding) st.Append("Bleeding  ");
                if (_sv.SickSeconds > 0f) st.Append("Sick  ");
                if (_sv.Wetness > 0.3f) st.Append("Wet  ");
                if (_sv.IsCold) st.Append("Cold  ");
                if (_sv.Overweight) st.Append("Overburdened  ");
                _status.text = st.ToString();
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
                _markerDist.text = dist > 8f ? Mathf.RoundToInt(dist) + " m" : "";
                _markerDist.rectTransform.anchoredPosition = new Vector2(-204 + rel * 4f, -70);
            }
            _marker.enabled = mk; _markerDist.enabled = mk;
            var tm = TimeManager.Instance;
            if (tm) { int h = Mathf.FloorToInt(tm.hour), m = Mathf.FloorToInt((tm.hour - h) * 60f); _clock.text = $"Day {tm.day}   {h:00}:{m:00}"; }
            // objective
            var tut = TutorialManager.Instance;
            if (tut && tut.Running) { _objText.text = tut.ObjectiveText(); _objHint.text = tut.Current.hint; _objGroup.alpha = Mathf.MoveTowards(_objGroup.alpha, 1f, udt * 3f); }
            else _objGroup.alpha = Mathf.MoveTowards(_objGroup.alpha, 0f, udt * 1f);
            _objFlash = Mathf.MoveTowards(_objFlash, 0f, udt * 0.8f); _objTitle.color = Color.Lerp(UIStyle.Accent, Color.white, _objFlash);
            // prompt
            bool show = _pi && !menuOpen && (_pi.Prompt != null || _pi.HoldLabel != null);
            if (show)
            {
                _prompt.text = _pi.Prompt ?? _pi.HoldLabel;
                string sub = _pi.PromptSub; if (_pi.HoldLabel != null && _pi.Prompt != null) sub = (sub != null ? sub + "   " : "") + "Hold E: " + _pi.HoldLabel;
                _promptSub.text = sub ?? "";
                _prompt.color = _pi.PromptEnabled || _pi.Prompt == null ? UIStyle.Text : UIStyle.TextDim;
                _promptKey.color = _pi.PromptEnabled || _pi.Prompt == null ? UIStyle.Accent : UIStyle.TextDim;
                _hold.fillAmount = _pi.HoldProgress;
                float w = Mathf.Min(560f, _prompt.preferredWidth); var p = _prompt.rectTransform; p.anchoredPosition = new Vector2(-w * 0.5f + 20f, 0);
                _promptKeyBg.rectTransform.anchoredPosition = new Vector2(-w * 0.5f + 12f, 0);
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

        static void Pulse(Image bar, bool on)
        {
            if (!bar) return;
            var c = bar.color; float a = on ? 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4f)) : 1f; c.a = a; bar.color = c;
        }
    }
}
