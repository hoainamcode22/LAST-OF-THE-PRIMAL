using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.Survival;

namespace PrimalFrontier.UI
{
    /// <summary>
    /// Short key names ("E", "LMB", "Shift") for the real bindings, read from <see cref="ControlsGuide"/> (its rows are
    /// checked against PlayerInputReader by ControlsGuideTests), so a hint never shows a key that differs from the binding.
    /// </summary>
    public static class KeyNames
    {
        static Dictionary<string, string[]> _byAction;
        static readonly Dictionary<string, string> _cache = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>short name of the index-th keyboard / mouse binding of an InputAction ("Attack" -> "LMB"); null when the table has no such action</summary>
        public static string Of(string action, int index = 0)
        {
            var p = Paths(action);
            if (p == null || p.Length == 0) return null;
            return Path(p[Mathf.Clamp(index, 0, p.Length - 1)]);
        }

        /// <summary>keyboard / mouse binding paths of an action as the controls table lists them (null = not listed)</summary>
        public static string[] Paths(string action)
        {
            if (string.IsNullOrEmpty(action)) return null;
            if (_byAction == null) Build();
            return _byAction.TryGetValue(action, out var p) ? p : null;
        }

        static void Build()
        {
            _byAction = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var r in ControlsGuide.Rows)
            {
                if (r.IsHeader || r.actions == null || r.paths == null || r.paths.Length == 0) continue;
                if (r.actions.Length == 1) { if (!_byAction.ContainsKey(r.actions[0])) _byAction[r.actions[0]] = r.paths; continue; }
                // one row for several actions (Hotbar1..8): one path per action, in order
                if (r.paths.Length == r.actions.Length)
                    for (int i = 0; i < r.actions.Length; i++) if (!_byAction.ContainsKey(r.actions[i])) _byAction[r.actions[i]] = new[] { r.paths[i] };
            }
        }

        /// <summary>"&lt;Keyboard&gt;/leftShift" -> "Shift", "&lt;Mouse&gt;/leftButton" -> "LMB"; unknown controls use the Input System's readable name</summary>
        public static string Path(string path)
        {
            if (string.IsNullOrEmpty(path)) return "?";
            if (_cache.TryGetValue(path, out var s)) return s;
            int slash = path.IndexOf('/');
            string ctl = slash >= 0 ? path.Substring(slash + 1) : path;
            if (path.StartsWith("<Mouse>", StringComparison.Ordinal))
                s = ctl == "leftButton" ? "LMB" : ctl == "rightButton" ? "RMB" : ctl == "middleButton" ? "MMB" : ctl.StartsWith("scroll", StringComparison.Ordinal) ? "Wheel" : ctl == "delta" ? "Mouse" : null;
            else
                switch (ctl)
                {
                    case "leftShift": case "rightShift": s = "Shift"; break;
                    case "leftCtrl": case "rightCtrl": s = "Ctrl"; break;
                    case "leftAlt": case "rightAlt": s = "Alt"; break;
                    case "space": s = "Space"; break;
                    case "tab": s = "Tab"; break;
                    case "escape": s = "Esc"; break;
                    case "enter": s = "Enter"; break;
                    case "backspace": s = "Backspace"; break;
                    case "equals": s = "="; break;
                    case "minus": s = "-"; break;
                    case "numpadPlus": s = "Num +"; break;
                    case "numpadMinus": s = "Num -"; break;
                    case "upArrow": s = "Up"; break;
                    case "downArrow": s = "Down"; break;
                    case "leftArrow": s = "Left"; break;
                    case "rightArrow": s = "Right"; break;
                    default: s = ctl.Length == 1 ? ctl.ToUpperInvariant() : null; break;
                }
            if (s == null)
            {
                try { s = InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice); }
                catch (Exception) { s = ctl; }
            }
            _cache[path] = s;
            return s;
        }
    }

    /// <summary>
    /// One-time onboarding tips already shown in this game. Saved with the game (SaveData.tipsSeen); a new game starts
    /// with none seen (GameManager.ResetWorld -> Clear).
    /// </summary>
    public static class OnboardingTips
    {
        public const string Wood = "wood", Night = "night", Weapon = "weapon", Dinosaur = "dinosaur", Hunger = "hunger", Thirst = "thirst";
        static readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>raised by Clear / SetSeen (the HUD drops its queue)</summary>
        public static event Action Reset;

        public static bool IsSeen(string id) => id != null && _seen.Contains(id);
        public static void MarkSeen(string id) { if (!string.IsNullOrEmpty(id)) _seen.Add(id); }
        public static void Clear() { _seen.Clear(); Reset?.Invoke(); }
        public static void SetSeen(IEnumerable<string> ids)
        {
            _seen.Clear();
            if (ids != null) foreach (var id in ids) if (!string.IsNullOrEmpty(id)) _seen.Add(id);
            Reset?.Invoke();
        }
        /// <summary>sorted copy for the save file</summary>
        public static List<string> SeenList() { var l = new List<string>(_seen); l.Sort(StringComparer.Ordinal); return l; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _seen.Clear(); Reset = null; }
    }

    /// <summary>what the player holds, as far as the key hints care</summary>
    public enum HeldKind { Empty, Tool, Torch, Melee, Spear, Bow, Food, Drink, Placeable, Other }
    /// <summary>which set of keys matters: normal play (by the held item), the inventory window, build placement, climbing</summary>
    public enum HintMode { Play, Inventory, Building, Climbing }
    /// <summary>night tip: none, hold the torch in hotbar slot torchSlot, torch only in the bag, no torch at all</summary>
    public enum NightTip { None, HotbarTorch, BagTorch, NoTorch }

    /// <summary>the player's situation the hints are written from (filled by ContextHints, or by a test)</summary>
    public struct HintContext
    {
        public HintMode mode;
        public HeldKind held;
        public ToolKind tool;           // gather verb for tools
        public bool heavy;              // held weapon has a heavy attack (hold the attack button)
        public bool aiming;             // spear / bow aim held
        public bool noAmmo;             // bow without arrows
        public bool emptyWater;         // water container without water
        public bool crouching, lowStamina;
        public NightTip night; public int torchSlot;
        public bool climbFruit;         // the tree being climbed carries fruit
        public string buildProblem;     // BuildSystem.InvalidReason (null = the spot is fine)

        public static bool Same(in HintContext a, in HintContext b) =>
            a.mode == b.mode && a.held == b.held && a.tool == b.tool && a.heavy == b.heavy && a.aiming == b.aiming && a.noAmmo == b.noAmmo &&
            a.emptyWater == b.emptyWater && a.crouching == b.crouching && a.lowStamina == b.lowStamina && a.night == b.night &&
            a.torchSlot == b.torchSlot && a.climbFruit == b.climbFruit && ReferenceEquals(a.buildProblem, b.buildProblem);
    }

    /// <summary>
    /// Contextual key hints (bottom right, 1-4 small lines such as "[LMB] Slash (Chém)   [Hold LMB] Heavy (Đòn mạnh)"):
    /// the keys that matter right now, from the player's state: empty hands, tool, torch, sword / knife (attack, heavy,
    /// hold RMB block), spear (thrust, heavy, aim + throw), bow (aim, draw, release), food / water, camp item (place mode),
    /// build placement, climbing, the inventory window, plus crouch (stealth), low stamina (sprint) and night (torch) lines.
    /// Key names come from <see cref="KeyNames"/> (the real bindings). Above the hints, one-line onboarding tips are shown
    /// once each (first wood, night, weapon, dinosaur, hunger, thirst; <see cref="OnboardingTips"/>, saved with the game).
    /// Hidden on the title, intro, pause / journal / death screens, while dead or sleeping, with the touch controls and
    /// when Settings > Key hints is OFF (GameSettings.Hints). Own canvas "[Hints]" (order 32: above the inventory, below
    /// the pause menu); layout lives in the scene ([UI]/[Hints]) once baked, the container can be moved by hand (the
    /// boxes size themselves to the text). The state is sampled 10 times a second; texts change only when it changes.
    /// </summary>
    public class ContextHints : MonoBehaviour, IBakeableUI
    {
        public static ContextHints Instance { get; private set; }
        [Tooltip("seconds each onboarding tip stays")] public float tipSeconds = 8f;
        [Tooltip("the night torch line shows this long after dark falls")] public float nightTipSeconds = 45f;
        [Tooltip("widest hint box (px, reference 1920 x 1080)")] public float maxWidth = 560f;
        [Tooltip("widest tip box (px); longer tips wrap")] public float maxTipWidth = 600f;
        public const int MaxLines = 4;

        Canvas _canvas; CanvasGroup _group; RectTransform _root, _panel, _tip; Text _text, _tipText; CanvasGroup _tipGroup;
        readonly List<string> _lines = new List<string>(MaxLines + 4);
        readonly StringBuilder _sb = new StringBuilder(256);
        HintContext _ctx; bool _hasCtx; bool _visible; float _nextSample; bool _hintsOn = true; float _nextSettings;
        GameObject _player; InventorySystem _inv; PlayerCombat _combat; PlayerMotor _motor; PlayerSurvival _sv; PlayerHealth _hp; PlayerClimb _climb;
        bool _wasNight; float _nightSince = -999f; bool _loggedError;
        MobileHUD _touch; bool _touchLooked;
        readonly Queue<(string id, string text)> _tipQueue = new Queue<(string, string)>();
        string _tipId; float _tipLeft;

        /// <summary>the hint lines for the current state (rich text), also while hidden</summary>
        public IReadOnlyList<string> Lines => _lines;
        /// <summary>the hints are on screen (or fading in)</summary>
        public bool Showing => _visible && _lines.Count > 0;
        public HintContext Context => _ctx;
        /// <summary>id of the onboarding tip on screen (null = none)</summary>
        public string CurrentTip => _tipId;
        public int QueuedTips => _tipQueue.Count;

        void Awake()
        {
            Instance = this;
            Build();
            _text.text = ""; _tipText.text = "";
            _group.alpha = 0f; _tipGroup.alpha = 0f;
        }

        void OnEnable() { GameEvents.Raised -= OnEvent; GameEvents.Raised += OnEvent; OnboardingTips.Reset -= OnTipsReset; OnboardingTips.Reset += OnTipsReset; }
        void OnDisable() { GameEvents.Raised -= OnEvent; OnboardingTips.Reset -= OnTipsReset; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>editor baker: write the layout into the scene with sample text so it can be placed by hand</summary>
        public void BakeLayout()
        {
            Build();
            var c = new HintContext { mode = HintMode.Play, held = HeldKind.Melee, heavy = true };
            Compose(c, _lines); SetText();
            _tipText.text = TipLine("Wood + stone make tools: press " + Key(Keys.craft) + " to craft (Gỗ + đá làm công cụ)");
            SizeTip();
            _group.alpha = 1f; _tipGroup.alpha = 1f;
            _lines.Clear();
        }

        // ================================================================== build
        void Build()
        {
            UIFactory.BeginBuild();
            try { BuildLayout(); } finally { UIFactory.EndBuild(); }
        }

        void BuildLayout()
        {
            _canvas = UIFactory.Canvas("[Hints]", 32, transform);
            _group = UIFactory.Group(_canvas.gameObject); _group.interactable = false; _group.blocksRaycasts = false;
            var gr = _canvas.GetComponent<GraphicRaycaster>(); if (gr) gr.enabled = false;           // never takes clicks
            var br = new Vector2(1, 0);
            _root = UIFactory.Rect(_canvas.transform, "ContextHints", br, br, br, new Vector2(-24, 24), new Vector2(maxWidth, 240));
            _panel = UIFactory.Image(_root, "Panel", UIStyle.PanelDark, new Color(1, 1, 1, 0.62f), br, br, br, Vector2.zero, new Vector2(360, 96)).rectTransform;
            _text = UIFactory.Label(_panel, "Text", "", 16, UIStyle.Text, TextAnchor.LowerRight, UIStyle.Body);
            if (UIFactory.Fresh(_text)) { _text.rectTransform.offsetMin = new Vector2(14, 8); _text.rectTransform.offsetMax = new Vector2(-14, -8); _text.lineSpacing = 1.1f; }
            _text.supportRichText = true; _text.horizontalOverflow = HorizontalWrapMode.Wrap; _text.verticalOverflow = VerticalWrapMode.Overflow;
            _tip = UIFactory.Image(_root, "Tip", UIStyle.PanelDark, new Color(1, 1, 1, 0.75f), br, br, br, new Vector2(0, 104), new Vector2(420, 40)).rectTransform;
            _tipGroup = UIFactory.Group(_tip.gameObject);
            _tipText = UIFactory.Label(_tip, "Text", "", 17, UIStyle.Text, TextAnchor.MiddleRight, UIStyle.Body);
            if (UIFactory.Fresh(_tipText)) { _tipText.rectTransform.offsetMin = new Vector2(14, 6); _tipText.rectTransform.offsetMax = new Vector2(-14, -6); }
            _tipText.supportRichText = true; _tipText.horizontalOverflow = HorizontalWrapMode.Wrap; _tipText.verticalOverflow = VerticalWrapMode.Overflow;
        }

        // ================================================================== per frame
        void Update()
        {
            try { Tick(); }
            catch (Exception e)
            {
                if (!_loggedError) { _loggedError = true; Debug.LogException(e); }
                if (_group) _group.alpha = 0f;
            }
        }

        void Tick()
        {
            float now = Time.unscaledTime, udt = Time.unscaledDeltaTime;
            if (now >= _nextSettings) { _nextSettings = now + 0.5f; _hintsOn = GameSettings.Hints; }
            if (now >= _nextSample)
            {
                _nextSample = now + 0.1f;
                ResolvePlayer();
                _visible = ComputeVisible(out bool inventory);
                var c = Sample(inventory);
                if (!_hasCtx || !HintContext.Same(c, _ctx)) { _ctx = c; _hasCtx = true; Compose(_ctx, _lines); SetText(); }
            }
            _group.alpha = Mathf.MoveTowards(_group.alpha, Showing ? 1f : 0f, udt * 6f);
            UpdateTips(udt);
        }

        void ResolvePlayer()
        {
            var gm = GameManager.Instance;
            var p = gm && gm.Player ? gm.Player : (World.PlayerLocator.Player ? World.PlayerLocator.Player.gameObject : null);
            if (p != _player)
            {
                _player = p;
                _inv = p ? p.GetComponent<InventorySystem>() : null; _combat = p ? p.GetComponent<PlayerCombat>() : null;
                _motor = p ? p.GetComponent<PlayerMotor>() : null; _sv = p ? p.GetComponent<PlayerSurvival>() : null;
                _hp = p ? p.GetComponent<PlayerHealth>() : null; _climb = p ? p.GetComponent<PlayerClimb>() : null;
            }
            if (_player && !_climb) _player.TryGetComponent(out _climb);          // added at run time by Climbable
        }

        /// <summary>follows the F1 controls rule: nothing on the title / death screens, during the intro or with another menu open</summary>
        bool ComputeVisible(out bool inventory)
        {
            inventory = false;
            if (!_hintsOn || !_player) return false;
            var ui = UIManager.Instance;
            if (ui)
            {
                if (ui.Current == UIScreen.Inventory) inventory = true;
                else if (ui.Current != UIScreen.None) return false;
                if (!ui.allowGameplayMenus) return false;                          // title, intro
            }
            var gm = GameManager.Instance;
            if (gm && gm.State != GameState.Playing) return false;                 // intro, dead, sleeping, title
            if (_hp && _hp.IsDead) return false;
            var hud = HUDManager.Instance;
            if (!inventory && hud && hud.HudAlpha < 0.5f) return false;           // HUD hidden (cutscene, fade)
            if (!_touchLooked) { _touchLooked = true; _touch = GetComponent<MobileHUD>(); if (!_touch) _touch = FindFirstObjectByType<MobileHUD>(); }
            if (_touch && _touch.Visible) return false;                            // the touch buttons own that corner
            return true;
        }

        HintContext Sample(bool inventory)
        {
            var c = new HintContext();
            var bs = Building.BuildSystem.Instance;
            if (inventory) c.mode = HintMode.Inventory;
            else if (bs && bs.Active) { c.mode = HintMode.Building; c.buildProblem = bs.Valid ? null : bs.InvalidReason; }
            else if (_climb && _climb.IsClimbing) { c.mode = HintMode.Climbing; c.climbFruit = _climb.Current && _climb.Current.Fruits.Length > 0; }
            var st = _inv ? _inv.ActiveStack : null;
            var item = st != null ? st.item : null;
            c.held = Classify(item);
            if (item)
            {
                c.tool = item.tool;
                var wd = item.weaponData;
                c.heavy = wd ? wd.HasHeavy : item.weapon == WeaponKind.Spear;
                if (c.held == HeldKind.Bow)
                {
                    var ammo = item.ammo ? item.ammo : wd ? wd.ammo : null;
                    c.noAmmo = ammo && _inv.Count(ammo) == 0;
                }
                if (c.held == HeldKind.Drink) c.emptyWater = st.water <= 0;
            }
            c.aiming = _combat && _combat.Aiming;
            c.crouching = _motor && _motor.IsCrouching;
            if (_sv && _sv.maxStamina > 0f)
            {
                float k = _sv.Stamina / _sv.maxStamina;
                c.lowStamina = _ctx.lowStamina ? k < 0.35f : k < 0.2f;             // hysteresis: no flicker
            }
            var tm = TimeManager.Instance;
            bool night = tm && tm.IsNight;
            if (night && !_wasNight) _nightSince = Time.unscaledTime;
            _wasNight = night;
            if (night && c.held != HeldKind.Torch && Time.unscaledTime - _nightSince < nightTipSeconds && _inv && _inv.Slots != null)
            {
                c.night = NightTip.NoTorch;
                for (int i = 0; i < _inv.Slots.Length; i++)
                {
                    var s = _inv.Get(i); if (s == null || s.item.lightRange <= 0f) continue;
                    if (i < _inv.hotbarSize) { c.night = NightTip.HotbarTorch; c.torchSlot = i; break; }
                    c.night = NightTip.BagTorch;
                }
            }
            return c;
        }

        /// <summary>what kind of thing the item in hand is (null = empty hands)</summary>
        public static HeldKind Classify(ItemDefinition item)
        {
            if (!item) return HeldKind.Empty;
            if (item.IsPlaceable) return HeldKind.Placeable;
            if (item.IsWaterContainer) return HeldKind.Drink;
            if (item.IsFood) return HeldKind.Food;
            var wd = item.weaponData;
            if (item.weapon == WeaponKind.Bow || (wd && wd.IsRanged)) return HeldKind.Bow;
            if (item.weapon == WeaponKind.Spear || (wd && wd.throwable)) return HeldKind.Spear;
            if (item.weapon == WeaponKind.Sword || item.weapon == WeaponKind.Knife) return HeldKind.Melee;
            if (item.lightRange > 0f) return HeldKind.Torch;
            if (item.tool != ToolKind.None) return HeldKind.Tool;
            if (item.damage > 0f || wd) return HeldKind.Melee;
            return HeldKind.Other;
        }

        // ================================================================== text
        /// <summary>key names, read once from the real bindings</summary>
        static class Keys
        {
            public static readonly string attack = KeyNames.Of("Attack") ?? "LMB";
            public static readonly string aim = KeyNames.Of("Aim") ?? "RMB";
            // the lead's binding for the block is "hold the aim button"; a dedicated Block action wins when the table lists one
            public static readonly string block = KeyNames.Of("Block") ?? KeyNames.Of("Aim") ?? "RMB";
            public static readonly string interact = KeyNames.Of("Interact") ?? "E";
            public static readonly string jump = KeyNames.Of("Jump") ?? "Space";
            public static readonly string crouch = KeyNames.Of("Crouch") ?? "C";
            public static readonly string dodge = KeyNames.Of("Dodge") ?? "V";
            public static readonly string sprint = KeyNames.Of("Sprint") ?? "Shift";
            public static readonly string rotate = KeyNames.Of("Rotate") ?? "R";
            public static readonly string scroll = KeyNames.Of("Scroll") ?? "Wheel";
            public static readonly string inventory = KeyNames.Of("Inventory") ?? "Tab";
            public static readonly string craft = KeyNames.Of("Craft") ?? "Q";
            public static readonly string pause = KeyNames.Of("Pause") ?? "Esc";
            public static readonly string drop = KeyNames.Of("Drop") ?? "G";
            public static readonly string up = KeyNames.Of("Move", 0) ?? "W";
            public static readonly string down = KeyNames.Of("Move", 1) ?? "S";
            public static readonly string hotbar = (KeyNames.Of("Hotbar1") ?? "1") + "-" + (KeyNames.Of("Hotbar8") ?? "8");
            // hard-wired mouse buttons (BuildSystem / PlayerInputReader.CancelPressed: right mouse; SlotView: left / right click)
            public static readonly string lmb = KeyNames.Path("<Mouse>/leftButton"), rmb = KeyNames.Path("<Mouse>/rightButton");
            public static readonly string accent = "#" + ColorUtility.ToHtmlStringRGB(UIStyle.Accent), dim = "#" + ColorUtility.ToHtmlStringRGB(UIStyle.TextDim);
            public static readonly string[] slot = { "1", "2", "3", "4", "5", "6", "7", "8" };
            static Keys()
            {
                for (int i = 0; i < slot.Length; i++) slot[i] = KeyNames.Of("Hotbar" + (i + 1)) ?? slot[i];
            }
        }

        static string Key(string k) => "<color=" + Keys.accent + ">[" + k + "]</color>";
        static string H(string key, string text) => Key(key) + " " + text;
        static string Pair(string a, string b) => a + "    " + b;
        static string Dim(string text) => "<color=" + Keys.dim + ">" + text + "</color>";
        static readonly List<string> _base = new List<string>(6), _mods = new List<string>(4);

        /// <summary>the hint lines (rich text) for a situation: at most <see cref="MaxLines"/>, the most useful first</summary>
        public static void Compose(in HintContext c, List<string> into)
        {
            into.Clear(); _base.Clear(); _mods.Clear();
            switch (c.mode)
            {
                case HintMode.Inventory:
                    _base.Add(H("Drag", "Move an item (Di chuyển đồ)"));
                    _base.Add(H("Double-click", "Use / equip (Dùng / trang bị)"));
                    _base.Add(Pair(H(Keys.rmb, "Use (Dùng)"), H("Shift+" + Keys.lmb, "Storage (Rương)")));
                    _base.Add(Key(Keys.inventory) + " / " + H(Keys.pause, "Close (Đóng)"));
                    break;
                case HintMode.Building:
                    _base.Add(H(Keys.attack, "Build here (Xây ở đây)"));
                    if (c.buildProblem != null) _base.Add(Dim(c.buildProblem));
                    _base.Add(Key(Keys.rotate) + " / " + H(Keys.scroll, "Rotate (Xoay)"));
                    _base.Add(Key(Keys.rmb) + " / " + H(Keys.pause, "Cancel (Hủy)"));
                    break;
                case HintMode.Climbing:
                    _base.Add(Key(Keys.up) + " / " + H(Keys.down, "Climb up / down (Leo lên / xuống)"));
                    if (c.climbFruit) _base.Add(H(Keys.interact, "Pick fruit in reach (Hái quả)"));
                    _base.Add(H(Keys.jump, "Let go (Buông)"));
                    break;
                default: HeldLines(c); break;
            }
            if (c.mode != HintMode.Inventory)
            {
                if (c.crouching && c.mode == HintMode.Play) _mods.Add(Pair(H(Keys.crouch, "Stand (Đứng)"), Dim("Sneaking: harder to spot (Khó bị phát hiện)")));
                if (c.lowStamina && c.mode != HintMode.Climbing) _mods.Add(Dim("Tired: stop sprinting ") + Key(Keys.sprint) + Dim(" (Mệt: ngừng chạy)"));
                if (c.night != NightTip.None && c.mode == HintMode.Play)
                    _mods.Add(c.night == NightTip.HotbarTorch ? H(Keys.slot[Mathf.Clamp(c.torchSlot, 0, 7)], "Hold the torch, it is night (Trời tối: cầm đuốc)")
                            : c.night == NightTip.BagTorch ? Dim("Night: put the torch on the hotbar (Trời tối: để đuốc lên ô nhanh)")
                            : Dim("Night: stay by a fire, craft a torch ") + Key(Keys.craft) + Dim(" (Trời tối: làm đuốc)"));
            }
            // keep the first two base lines, then the situation lines, then the rest while there is room
            int keepBase = Mathf.Min(_base.Count, Mathf.Max(2, MaxLines - _mods.Count));
            for (int i = 0; i < keepBase && into.Count < MaxLines; i++) into.Add(_base[i]);
            for (int i = 0; i < _mods.Count && into.Count < MaxLines; i++) into.Add(_mods[i]);
        }

        static void HeldLines(in HintContext c)
        {
            switch (c.held)
            {
                case HeldKind.Empty:
                case HeldKind.Other:
                    _base.Add(H(Keys.interact, "Interact / pick up (Tương tác / nhặt)"));
                    _base.Add(H(Keys.hotbar, "Hold an item (Cầm đồ)"));
                    _base.Add(Pair(H(Keys.inventory, "Bag (Túi đồ)"), H(Keys.craft, "Craft (Chế tạo)")));
                    break;
                case HeldKind.Tool:
                    _base.Add(H(Keys.interact, GatherVerb(c.tool)));
                    _base.Add(Pair(H(Keys.attack, "Strike (Đánh)"), H(Keys.dodge, "Dodge (Né)")));
                    _base.Add(H("Hold " + Keys.block, "Block (Đỡ đòn)"));
                    break;
                case HeldKind.Torch:
                    _base.Add(H(Keys.attack, "Swing (Vung đuốc)"));
                    _base.Add(H(Keys.hotbar, "Switch item (Đổi đồ)"));
                    break;
                case HeldKind.Melee:
                    _base.Add(c.heavy ? Pair(H(Keys.attack, "Slash (Chém)"), H("Hold " + Keys.attack, "Heavy (Đòn mạnh)")) : H(Keys.attack, "Slash (Chém)"));
                    _base.Add(Pair(H("Hold " + Keys.block, "Block (Đỡ đòn)"), H(Keys.dodge, "Dodge (Né)")));
                    if (c.tool != ToolKind.None) _base.Add(H(Keys.interact, GatherVerb(c.tool)));
                    break;
                case HeldKind.Spear:
                    if (c.aiming)
                    {
                        _base.Add(H(Keys.attack, "Throw (Ném)"));
                        _base.Add(H("Release " + Keys.aim, "Stop aiming (Thôi ngắm)"));
                    }
                    else
                    {
                        _base.Add(c.heavy ? Pair(H(Keys.attack, "Thrust (Đâm)"), H("Hold " + Keys.attack, "Heavy (Đòn mạnh)")) : H(Keys.attack, "Thrust (Đâm)"));
                        _base.Add(Pair(H("Hold " + Keys.aim, "Aim (Ngắm)"), "+ " + H(Keys.attack, "Throw (Ném)")));
                        _base.Add(H(Keys.dodge, "Dodge (Né)"));
                    }
                    break;
                case HeldKind.Bow:
                    if (!c.aiming) _base.Add(H("Hold " + Keys.aim, "Aim (Ngắm)"));
                    _base.Add(Pair(H("Hold " + Keys.attack, "Draw (Kéo dây)"), H("Release", "Shoot (Bắn)")));
                    if (c.noAmmo) _base.Add(Dim("No arrows: craft some ") + Key(Keys.craft) + Dim(" (Hết tên)"));
                    break;
                case HeldKind.Food:
                    _base.Add(H(Keys.attack, "Eat (Ăn)"));
                    _base.Add(H(Keys.drop, "Drop (Vứt)"));
                    break;
                case HeldKind.Drink:
                    _base.Add(c.emptyWater ? H(Keys.interact, "Fill it at fresh water (Lấy nước ngọt)") : H(Keys.attack, "Drink (Uống)"));
                    break;
                case HeldKind.Placeable:
                    _base.Add(H(Keys.attack, "Place mode (Chế độ đặt)"));
                    _base.Add(Pair(H(Keys.rotate, "Rotate (Xoay)"), H(Keys.rmb, "Cancel (Hủy)")));
                    break;
            }
        }

        static string GatherVerb(ToolKind t)
        {
            t &= ~ToolKind.Light;
            if (t == ToolKind.Chop) return "Chop wood (Chặt gỗ)";
            if (t == ToolKind.Mine) return "Mine stone (Đào đá)";
            if (t == ToolKind.Cut) return "Cut fibre / hide (Cắt sợi / da)";
            if (t == ToolKind.Hammer) return "Break rocks (Đập đá)";
            return "Gather (Thu thập)";
        }

        void SetText()
        {
            _sb.Clear();
            for (int i = 0; i < _lines.Count; i++) { if (i > 0) _sb.Append('\n'); _sb.Append(_lines[i]); }
            _text.text = _sb.ToString();
            if (_lines.Count == 0) { PlaceTip(); return; }
            // box follows the text: width of the longest line (capped), then the wrapped height
            var ti = _text.rectTransform; float padX = -ti.offsetMax.x + ti.offsetMin.x, padY = -ti.offsetMax.y + ti.offsetMin.y;
            float w = Mathf.Min(maxWidth, _text.preferredWidth + padX + 2f);
            _panel.sizeDelta = new Vector2(w, _panel.sizeDelta.y);
            float h = _text.preferredHeight + padY;
            _panel.sizeDelta = new Vector2(w, Mathf.Max(28f, h));
            PlaceTip();
        }

        void PlaceTip()
        {
            float top = _lines.Count > 0 ? _panel.anchoredPosition.y + _panel.sizeDelta.y + 8f : _panel.anchoredPosition.y;
            _tip.anchoredPosition = new Vector2(_panel.anchoredPosition.x, top);
        }

        void SizeTip()
        {
            var ti = _tipText.rectTransform; float padX = -ti.offsetMax.x + ti.offsetMin.x, padY = -ti.offsetMax.y + ti.offsetMin.y;
            float w = Mathf.Min(maxTipWidth, _tipText.preferredWidth + padX + 2f);
            _tip.sizeDelta = new Vector2(w, _tip.sizeDelta.y);
            _tip.sizeDelta = new Vector2(w, Mathf.Max(32f, _tipText.preferredHeight + padY));
            PlaceTip();
        }

        // ================================================================== onboarding tips
        static string TipLine(string text) => "<color=" + Keys.accent + ">TIP (Mẹo)</color>  " + text;

        void OnTipsReset() { _tipQueue.Clear(); _tipId = null; _tipLeft = 0f; if (_tipText) _tipText.text = ""; if (_tipGroup) _tipGroup.alpha = 0f; }

        /// <summary>true when the tip would be queued: hints on, not shown in this game yet, not queued or showing now</summary>
        public bool WantsTip(string id)
        {
            if (!_hintsOn || string.IsNullOrEmpty(id) || OnboardingTips.IsSeen(id) || id == _tipId || _tipQueue.Count >= 6) return false;
            foreach (var q in _tipQueue) if (q.id == id) return false;
            return true;
        }

        /// <summary>queue an onboarding tip unless it was already shown in this game (or is queued / showing now)</summary>
        public bool QueueTip(string id, string text)
        {
            if (!WantsTip(id)) return false;
            _tipQueue.Enqueue((id, text));
            return true;
        }

        void UpdateTips(float udt)
        {
            bool canShow = _visible && _ctx.mode != HintMode.Inventory;
            if (_tipId == null && canShow && _tipQueue.Count > 0)
            {
                var (id, text) = _tipQueue.Dequeue();
                if (!OnboardingTips.IsSeen(id))
                {
                    _tipId = id; _tipLeft = Mathf.Max(2f, tipSeconds);
                    OnboardingTips.MarkSeen(id);
                    _tipText.text = TipLine(text);
                    SizeTip();
                    Audio.SfxPlayer.Instance.Play2D(Audio.SfxId.UiClick, 0.35f);
                }
            }
            if (_tipId != null && canShow) { _tipLeft -= udt; if (_tipLeft <= 0f) _tipId = null; }
            float want = _tipId != null && canShow && _tipLeft > 0.4f ? 1f : 0f;
            _tipGroup.alpha = Mathf.MoveTowards(_tipGroup.alpha, want, udt * (want > 0f ? 4f : 2.5f));
        }

        void OnEvent(GameEvent e)
        {
            if (!_hintsOn) return;
            switch (e.type)
            {
                case GameEventType.ItemAdded:
                    if (e.id == "wood") { if (WantsTip(OnboardingTips.Wood)) QueueTip(OnboardingTips.Wood, "Wood + stone make tools: press " + Key(Keys.craft) + " to craft (Gỗ + đá làm công cụ: nhấn " + Keys.craft + ")"); }
                    else WeaponTip(e.id);
                    break;
                case GameEventType.ItemEquipped: WeaponTip(e.id); break;
                case GameEventType.NightStarted:
                    if (WantsTip(OnboardingTips.Night)) QueueTip(OnboardingTips.Night, "Night is cold and dark: stay by a fire or hold a torch (Đêm lạnh: ở gần lửa hoặc cầm đuốc)");
                    break;
                case GameEventType.CreatureSighted:
                    if (WantsTip(OnboardingTips.Dinosaur)) QueueTip(OnboardingTips.Dinosaur, "Dinosaur! Keep your distance, " + Key(Keys.crouch) + " crouch to stay unseen (Khủng long: giữ khoảng cách, ngồi để ẩn)");
                    break;
            }
        }

        void WeaponTip(string itemId)
        {
            if (!WantsTip(OnboardingTips.Weapon)) return;
            var db = ItemDatabase.Instance; var it = db && itemId != null ? db.Item(itemId) : null;
            switch (Classify(it))
            {
                case HeldKind.Melee:
                    QueueTip(OnboardingTips.Weapon, "Weapon: " + Key(Keys.attack) + " attack, hold for heavy, " + Key("Hold " + Keys.block) + " block, " + Key(Keys.dodge) + " dodge (Vũ khí: đánh, đỡ, né)");
                    break;
                case HeldKind.Spear:
                    QueueTip(OnboardingTips.Weapon, "Spear: " + Key(Keys.attack) + " thrust, hold " + Key(Keys.aim) + " and press " + Key(Keys.attack) + " to throw (Giáo: đâm hoặc ngắm + ném)");
                    break;
                case HeldKind.Bow:
                    QueueTip(OnboardingTips.Weapon, "Bow: hold " + Key(Keys.aim) + " to aim, hold " + Key(Keys.attack) + " to draw, release to shoot (Cung: ngắm, kéo, thả)");
                    break;
            }
        }

        // hunger / thirst: the first warning of the game (polled with the state, see LateUpdate)
        float _nextVitals;
        void LateUpdate()
        {
            if (!_hintsOn || !_sv || Time.unscaledTime < _nextVitals) return;
            _nextVitals = Time.unscaledTime + 1f;
            if (_sv.Paused) return;
            if (_sv.Hunger < 20f && WantsTip(OnboardingTips.Hunger))
                QueueTip(OnboardingTips.Hunger, "Hungry: hold food and press " + Key(Keys.attack) + " to eat, berries grow on bushes (Đói: cầm đồ ăn và nhấn để ăn)");
            if (_sv.Thirst < 20f && WantsTip(OnboardingTips.Thirst))
                QueueTip(OnboardingTips.Thirst, "Thirsty: press " + Key(Keys.interact) + " at a pond or stream, never the sea (Khát: uống ở ao / suối, không uống nước biển)");
        }
    }
}
