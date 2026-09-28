using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.Survival;
using PrimalFrontier.World;

namespace PrimalFrontier.UI
{
    /// <summary>
    /// Tab / Q window: INVENTORY (character preview, bag grid, hotbar, item info, weight, Use / Equip / Drop / Split,
    /// storage side panel) and CRAFTING (categories, recipes, requirements with have / need, craft time, station,
    /// queue with cancel). Drag & drop between slots, shift-click to transfer, drag outside to drop on the ground.
    /// Water containers show their water type (label + colour) and get EMPTY in place of SPLIT (pours the water out).
    /// </summary>
    public class InventoryUI : MonoBehaviour, IBakeableUI
    {
        public static InventoryUI Instance { get; private set; }
        public int Tab { get; private set; }

        GameObject _player; InventorySystem _inv; CraftingSystem _craft; PlayerInteraction _pi; PlayerSurvival _sv; PlayerHealth _hp;
        Canvas _canvas; RectTransform _win, _invTab, _craftTab, _storagePanel, _detailPanel; Button _tabInv, _tabCraft;
        readonly List<SlotView> _slots = new List<SlotView>(); readonly List<SlotView> _storageSlots = new List<SlotView>();
        InventorySystem _storage; SlotView _selected;
        Text _statText, _weightText; Image _weightBar;
        Image _dIcon; Text _dName, _dCat, _dDesc, _dStats; Button _bUse, _bEquip, _bDrop, _bSplit, _bEmpty;
        RectTransform _tooltip; Text _tipText;
        RawImage _preview; Camera _previewCam; RenderTexture _rt;
        // crafting
        RecipeCategory? _cat; readonly List<RecipeCategory?> _catOf = new List<RecipeCategory?>(); readonly List<(RecipeDefinition r, Image bg, Image icon, Text name)> _tiles = new List<(RecipeDefinition, Image, Image, Text)>();
        RectTransform _tileRoot; RecipeDefinition _recipe;
        Image _cIcon; Text _cName, _cDesc, _cReq, _cInfo, _cReason; Button _bCraft, _bCraft5;
        readonly List<Image> _queueIcons = new List<Image>(); readonly List<Image> _queueBars = new List<Image>();
        readonly List<Button> _catButtons = new List<Button>();

        void Awake() { Instance = this; Build(); _canvas.gameObject.SetActive(false); }
        public void BakeLayout() { Build(); _canvas.gameObject.SetActive(false); }
        void OnDestroy() { if (Instance == this) Instance = null; if (_rt) _rt.Release(); SlotView.Dropped -= OnSlotDropped; }

        public void Bind(GameObject player)
        {
            _player = player; _inv = player.GetComponent<InventorySystem>(); _craft = player.GetComponent<CraftingSystem>();
            _pi = player.GetComponent<PlayerInteraction>(); _sv = player.GetComponent<PlayerSurvival>(); _hp = player.GetComponent<PlayerHealth>();
            foreach (var s in _slots) s.inventory = _inv;
            _inv.Changed -= RefreshAll; _inv.Changed += RefreshAll;
            _craft.QueueChanged -= RefreshCraft; _craft.QueueChanged += RefreshCraft;
            _craft.Learned -= OnLearned; _craft.Learned += OnLearned;
            StorageBox.Opened -= OpenStorage; StorageBox.Opened += OpenStorage;
            if (UIManager.Instance) { UIManager.Instance.Changed -= OnScreen; UIManager.Instance.Changed += OnScreen; }
            BuildPreviewCamera();
            BuildTiles();
        }

        void OnLearned(RecipeDefinition r) => BuildTiles();

        // ================================================================== build
        void Build()
        {
            UIFactory.BeginBuild();
            try { BuildLayout(); } finally { UIFactory.EndBuild(); }
        }

        void BuildLayout()
        {
            _canvas = UIFactory.Canvas("[Inventory]", 30, transform);
            var root = _canvas.transform;
            var dim = UIFactory.Fill(root, "Dim", null, new Color(0, 0, 0, 0.55f), true);
            dim.gameObject.GetOrAdd<DropZone>().Dropped = OnDropOutside;
            _win = UIFactory.Image(root, "Window", UIStyle.Leather, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1560, 860), true).rectTransform;
            var header = UIFactory.Image(_win, "Header", UIStyle.Wood, Color.white, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -14), new Vector2(-40, 64));
            _tabInv = UIFactory.Button(header.transform, "TabInv", "INVENTORY", () => ShowTab(0), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(240, 48), 26);
            _tabCraft = UIFactory.Button(header.transform, "TabCraft", "CRAFTING", () => ShowTab(1), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(272, 0), new Vector2(240, 48), 26);
            UIFactory.Label(header.transform, "Keys", "Tab inventory   Q crafting   J journal   Esc close", 17, UIStyle.TextDim, TextAnchor.MiddleRight, UIStyle.Body, Vector2.zero, Vector2.one, new Vector2(1, 0.5f), new Vector2(-80, 0), new Vector2(-560, 0));
            UIFactory.Button(header.transform, "Close", "X", () => UIManager.Instance?.Close(), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-12, 0), new Vector2(48, 48), 26);

            _invTab = UIFactory.Rect(_win, "InventoryTab", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            if (UIFactory.Fresh(_invTab)) { _invTab.offsetMin = new Vector2(30, 30); _invTab.offsetMax = new Vector2(-30, -92); }
            _craftTab = UIFactory.Rect(_win, "CraftingTab", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            if (UIFactory.Fresh(_craftTab)) { _craftTab.offsetMin = new Vector2(30, 30); _craftTab.offsetMax = new Vector2(-30, -92); }
            BuildInventoryTab(); BuildCraftTab();

            _tooltip = UIFactory.Image(root, "Tooltip", UIStyle.PanelDark, new Color(1, 1, 1, 0.95f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 1), Vector2.zero, new Vector2(320, 120)).rectTransform;
            _tipText = UIFactory.Label(_tooltip, "Text", "", 17, UIStyle.Text, TextAnchor.UpperLeft, UIStyle.Body);
            if (UIFactory.Fresh(_tipText)) { _tipText.rectTransform.offsetMin = new Vector2(14, 10); _tipText.rectTransform.offsetMax = new Vector2(-14, -10); }
            _tooltip.gameObject.SetActive(false);
            if (Application.isPlaying) SlotView.Dropped += OnSlotDropped;
        }

        void BuildInventoryTab()
        {
            // left: preview + status
            var left = UIFactory.Image(_invTab, "Left", UIStyle.PanelDark, new Color(1, 1, 1, 0.8f), new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), Vector2.zero, new Vector2(360, 0)).rectTransform;
            _preview = UIFactory.Rect(left, "Preview", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -12), new Vector2(-24, 400)).gameObject.GetOrAdd<RawImage>();
            _preview.raycastTarget = false;
            _statText = UIFactory.Label(left, "Stats", "", 18, UIStyle.Text, TextAnchor.UpperLeft, UIStyle.Body, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(20, 70), new Vector2(-40, 220));
            _weightText = UIFactory.Label(left, "Weight", "", 18, UIStyle.Text, TextAnchor.LowerLeft, UIStyle.Body, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(20, 36), new Vector2(-40, 26));
            _weightBar = UIFactory.Bar(left, "WeightBar", UIStyle.Accent, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(20, 18), new Vector2(-40, 12));

            // centre: bag + hotbar
            var mid = UIFactory.Rect(_invTab, "Middle", new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(380, 0), new Vector2(560, 0));
            UIFactory.Label(mid, "BagTitle", "PACK", 22, UIStyle.Accent, TextAnchor.UpperLeft, UIStyle.Head, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(4, 0), new Vector2(0, 30));
            const float S = 86f;
            for (int i = 0; i < 24; i++)
            {
                int r = i / 6, c = i % 6;
                var v = SlotView.Create(mid, null, 8 + i, new Vector2(c * (S + 6), -40 - r * (S + 6)), S);
                Wire(v); _slots.Add(v);
            }
            UIFactory.Label(mid, "HotTitle", "HOTBAR  (1-8)", 22, UIStyle.Accent, TextAnchor.UpperLeft, UIStyle.Head, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(4, -430), new Vector2(0, 30));
            for (int i = 0; i < 8; i++)
            {
                var v = SlotView.Create(mid, null, i, new Vector2(i * 68, -468), 62, (i + 1).ToString());
                Wire(v); _slots.Add(v);
            }
            UIFactory.Label(mid, "Help", "Double click: use / equip    Shift+click: move    Drag outside: drop", 16, UIStyle.TextDim, TextAnchor.UpperLeft, UIStyle.Body, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(4, -548), new Vector2(0, 24));

            // right: details (paper) / storage
            _detailPanel = UIFactory.Image(_invTab, "Detail", UIStyle.Paper, Color.white, new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), Vector2.zero, new Vector2(520, 0)).rectTransform;
            _dIcon = UIFactory.Image(_detailPanel, "Icon", null, Color.white, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, -34), new Vector2(128, 128)); _dIcon.preserveAspect = true;
            _dName = UIFactory.Label(_detailPanel, "Name", "", 34, UIStyle.TextDark, TextAnchor.UpperLeft, UIStyle.Head, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(178, -40), new Vector2(-200, 80), false);
            _dCat = UIFactory.Label(_detailPanel, "Cat", "", 18, new Color(0.45f, 0.3f, 0.18f), TextAnchor.UpperLeft, UIStyle.Body, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(178, -124), new Vector2(-200, 26), false);
            _dDesc = UIFactory.Label(_detailPanel, "Desc", "", 20, UIStyle.TextDark, TextAnchor.UpperLeft, UIStyle.Hand, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(34, -186), new Vector2(-68, 140), false);
            _dStats = UIFactory.Label(_detailPanel, "Stats", "", 19, UIStyle.TextDark, TextAnchor.UpperLeft, UIStyle.Body, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(34, -340), new Vector2(-68, 200), false);
            _bUse = UIFactory.Button(_detailPanel, "Use", "USE", OnUse, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(30, 30), new Vector2(110, 48), 20);
            _bEquip = UIFactory.Button(_detailPanel, "Equip", "EQUIP", OnEquip, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(148, 30), new Vector2(110, 48), 20);
            _bSplit = UIFactory.Button(_detailPanel, "Split", "SPLIT", OnSplit, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(266, 30), new Vector2(110, 48), 20);
            _bDrop = UIFactory.Button(_detailPanel, "Drop", "DROP", OnDrop, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(384, 30), new Vector2(110, 48), 20);
            // containers cannot be split (one per slot): EMPTY takes SPLIT's place for them
            _bEmpty = UIFactory.Button(_detailPanel, "Empty", "EMPTY", OnEmpty, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(266, 30), new Vector2(110, 48), 20);
            _bEmpty.gameObject.SetActive(false);

            _storagePanel = UIFactory.Image(_invTab, "Storage", UIStyle.PanelDark, new Color(1, 1, 1, 0.85f), new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), Vector2.zero, new Vector2(520, 0)).rectTransform;
            UIFactory.Label(_storagePanel, "Title", "STORAGE", 22, UIStyle.Accent, TextAnchor.UpperLeft, UIStyle.Head, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(24, -16), new Vector2(0, 30));
            for (int i = 0; i < 20; i++)
            {
                int r = i / 5, c = i % 5;
                var v = SlotView.Create(_storagePanel, null, i, new Vector2(24 + c * 94, -60 - r * 94), 88);
                Wire(v); _storageSlots.Add(v);
            }
            UIFactory.Label(_storagePanel, "Help", "Shift+click moves between pack and storage", 16, UIStyle.TextDim, TextAnchor.UpperLeft, UIStyle.Body, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(24, 20), new Vector2(-48, 24));
            _storagePanel.gameObject.SetActive(false);
        }

        void BuildCraftTab()
        {
            var left = UIFactory.Image(_craftTab, "Categories", UIStyle.PanelDark, new Color(1, 1, 1, 0.8f), new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), Vector2.zero, new Vector2(230, 0)).rectTransform;
            // tab order and names are game data (the buttons themselves can be moved / restyled in the scene);
            // new tabs are appended so the scene objects Cat0..CatN keep their category
            string[] names = { "ALL", "TOOLS", "WEAPONS", "FOOD", "WATER", "BUILDING", "SURVIVAL", "RESOURCES" };
            RecipeCategory?[] cats = { null, RecipeCategory.Tools, RecipeCategory.Weapons, RecipeCategory.Food, RecipeCategory.Water, RecipeCategory.Structures, RecipeCategory.Survival, RecipeCategory.Resources };
            for (int i = 0; i < names.Length; i++)
            {
                var cat = cats[i];
                var b = UIFactory.Button(left, "Cat" + i, names[i], () => { _cat = cat; BuildTiles(); }, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -18 - i * 62), new Vector2(200, 52), 20);
                var t = b.GetComponentInChildren<Text>(); if (t) t.text = names[i];
                _catButtons.Add(b); _catOf.Add(cat);
            }
            var mid = UIFactory.Image(_craftTab, "Recipes", UIStyle.PanelDark, new Color(1, 1, 1, 0.65f), new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(246, 0), new Vector2(700, 0)).rectTransform;
            var scroll = UIFactory.Stretch(mid, "Scroll", 14f);
            var sr = scroll.GetComponent<ScrollRect>(); if (!sr) { sr = scroll.gameObject.AddComponent<ScrollRect>(); sr.horizontal = false; sr.scrollSensitivity = 30f; }
            var vp = UIFactory.Stretch(scroll, "Viewport"); vp.gameObject.GetOrAdd<RectMask2D>();
            if (!vp.GetComponent<Image>()) { var vpImg = vp.gameObject.AddComponent<Image>(); vpImg.color = new Color(0, 0, 0, 0.01f); }
            _tileRoot = UIFactory.Rect(vp, "Content", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 600));
            sr.viewport = vp; sr.content = _tileRoot;

            var right = UIFactory.Image(_craftTab, "Detail", UIStyle.Paper, Color.white, new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), Vector2.zero, new Vector2(520, 0)).rectTransform;
            _cIcon = UIFactory.Image(right, "Icon", null, Color.white, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, -30), new Vector2(116, 116)); _cIcon.preserveAspect = true;
            _cName = UIFactory.Label(right, "Name", "", 32, UIStyle.TextDark, TextAnchor.UpperLeft, UIStyle.Head, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(166, -36), new Vector2(-190, 80), false);
            _cInfo = UIFactory.Label(right, "Info", "", 18, new Color(0.45f, 0.3f, 0.18f), TextAnchor.UpperLeft, UIStyle.Body, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(166, -118), new Vector2(-190, 30), false);
            _cDesc = UIFactory.Label(right, "Desc", "", 19, UIStyle.TextDark, TextAnchor.UpperLeft, UIStyle.Hand, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(34, -166), new Vector2(-68, 110), false);
            UIFactory.Label(right, "ReqTitle", "REQUIRES", 18, new Color(0.45f, 0.3f, 0.18f), TextAnchor.UpperLeft, UIStyle.Head, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(34, -284), new Vector2(-68, 26), false);
            _cReq = UIFactory.Label(right, "Req", "", 21, UIStyle.TextDark, TextAnchor.UpperLeft, UIStyle.Body, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(34, -314), new Vector2(-68, 150), false);
            _cReason = UIFactory.Label(right, "Reason", "", 18, new Color(0.62f, 0.16f, 0.1f), TextAnchor.UpperLeft, UIStyle.Body, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(34, 196), new Vector2(-68, 50), false);
            _bCraft = UIFactory.Button(right, "Craft", "CRAFT", () => Craft(1), new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(34, 130), new Vector2(210, 56), 24);
            _bCraft5 = UIFactory.Button(right, "Craft5", "CRAFT x5", () => Craft(5), new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(256, 130), new Vector2(210, 56), 24);
            UIFactory.Label(right, "QTitle", "QUEUE  (click to cancel)", 16, new Color(0.45f, 0.3f, 0.18f), TextAnchor.UpperLeft, UIStyle.Body, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(34, 96), new Vector2(-68, 22), false);
            for (int i = 0; i < CraftingSystem.MaxQueue; i++)
            {
                int k = i;
                var q = UIFactory.Image(right, "Q" + i, UIStyle.Slot, Color.white, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(34 + i * 72, 22), new Vector2(66, 66), true);
                var ic = UIFactory.Image(q.transform, "Icon", null, Color.white, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero); if (UIFactory.Fresh(ic)) { ic.rectTransform.offsetMin = new Vector2(8, 8); ic.rectTransform.offsetMax = new Vector2(-8, -8); } ic.preserveAspect = true;
                var bar = UIFactory.Image(q.transform, "Bar", UIStyle.BarFill, UIStyle.Accent, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(6, 4), new Vector2(-12, 5)); bar.type = Image.Type.Filled; bar.fillMethod = Image.FillMethod.Horizontal;
                var btn = q.gameObject.GetOrAdd<Button>(); if (Application.isPlaying) btn.onClick.AddListener(() => { _craft?.Cancel(k); });
                _queueIcons.Add(ic); _queueBars.Add(bar);
            }
        }

        void Wire(SlotView v)
        {
            v.Clicked = s => Select(s);
            v.DoubleClicked = s => { Select(s); if (s.inventory == _inv) { var st = s.Stack; if (st != null && (st.item.IsFood || st.item.IsWaterContainer)) OnUse(); else OnEquip(); } else Transfer(s); };
            v.ShiftClicked = s => Transfer(s);
            v.RightClicked = s => { Select(s); if (s.inventory == _inv) OnUse(); };
            v.Hovered = s => ShowTip(s.Stack, s.transform as RectTransform);
            v.Unhovered = s => _tooltip.gameObject.SetActive(false);
        }

        // ================================================================== open / close
        void OnScreen(UIScreen from, UIScreen to)
        {
            bool open = to == UIScreen.Inventory;
            _canvas.gameObject.SetActive(open);
            if (_previewCam) _previewCam.enabled = open;
            if (!open) { _storage = null; _storagePanel.gameObject.SetActive(false); _detailPanel.gameObject.SetActive(true); _tooltip.gameObject.SetActive(false); }
            else RefreshAll();
        }

        public void ShowTab(int t)
        {
            Tab = t;
            _invTab.gameObject.SetActive(t == 0); _craftTab.gameObject.SetActive(t == 1);
            _tabInv.GetComponent<Image>().color = t == 0 ? Color.white : new Color(0.6f, 0.55f, 0.5f);
            _tabCraft.GetComponent<Image>().color = t == 1 ? Color.white : new Color(0.6f, 0.55f, 0.5f);
            if (t == 1) { GameEvents.Raise(GameEventType.MenuOpened, "crafting"); BuildTiles(); }
            RefreshAll();
        }

        void OpenStorage(StorageBox box)
        {
            _storage = box.Inventory;
            foreach (var s in _storageSlots) s.inventory = _storage;
            _storage.Changed -= RefreshAll; _storage.Changed += RefreshAll;
            UIManager.Instance?.Open(UIScreen.Inventory);
            ShowTab(0);
            _storagePanel.gameObject.SetActive(true); _detailPanel.gameObject.SetActive(false);
            RefreshAll();
        }

        void Update()
        {
            if (!_canvas.gameObject.activeSelf) return;
            if (_storage != null && _player && _storage.transform && Vector3.Distance(_storage.transform.position, _player.transform.position) > 4f) UIManager.Instance?.Close();
            if (_tooltip.gameObject.activeSelf)
            {
                var m = UnityEngine.InputSystem.Mouse.current; if (m != null)
                {
                    RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)_canvas.transform, m.position.ReadValue(), null, out var lp);
                    _tooltip.anchoredPosition = lp + new Vector2(22, -22);
                }
            }
            if (Tab == 1) UpdateQueue();
            UpdateStats();
        }

        // ================================================================== refresh
        void RefreshAll()
        {
            if (_inv == null) return;
            foreach (var s in _slots) { s.Selected = s == _selected; s.Refresh(); }
            foreach (var s in _storageSlots) { s.Selected = s == _selected; if (s.inventory) s.Refresh(); }
            RefreshDetail();
            if (Tab == 1) RefreshCraft();
        }

        void UpdateStats()
        {
            if (_sv == null) return;
            _statText.text = $"<b>SURVIVOR</b>\nHealth   {Mathf.CeilToInt(_hp.Health)} / {Mathf.CeilToInt(_hp.maxHealth)}\nHunger   {Mathf.CeilToInt(_sv.Hunger)}{TierSuffix(_sv.HungerTierLabel)}\nThirst   {Mathf.CeilToInt(_sv.Thirst)}{TierSuffix(_sv.ThirstTierLabel)}\n" +
                             $"Stamina  {Mathf.CeilToInt(_sv.Stamina)}\nBody     {_sv.BodyTemperature:0.0} °C\nAir      {_sv.EnvironmentTemperature:0} °C";
            _weightText.text = $"Weight  {_inv.Weight:0.0} / {_inv.maxWeight:0} kg";
            _weightBar.fillAmount = _inv.maxWeight > 0 ? Mathf.Clamp01(_inv.Weight / _inv.maxWeight) : 0f;
            _weightBar.color = _inv.IsOverweight ? UIStyle.Bad : UIStyle.Accent;
        }

        static string TierSuffix(string label) => label == null ? "" : "   (" + label + ")";

        void Select(SlotView s) { _selected = s.Stack != null ? s : null; RefreshAll(); }

        void RefreshDetail()
        {
            var st = _selected ? _selected.Stack : null;
            bool has = st != null && _selected.inventory == _inv;
            _dIcon.enabled = st != null; if (st != null) _dIcon.sprite = st.item.icon;
            _dName.text = st != null ? st.item.displayName : "Select an item";
            _dCat.text = st != null ? st.item.category.ToString().ToUpperInvariant() + $"   {st.item.weight:0.##} kg each" : "";
            _dDesc.text = st != null ? st.item.description : "Items you carry appear here. Your pack has a weight limit.";
            _dStats.text = st != null ? Stats(st, true) : "";
            _bUse.interactable = has && (st.item.IsFood || st.item.IsWaterContainer);
            _bEquip.interactable = has && _selected.index >= _inv.hotbarSize;
            _bSplit.interactable = has && st.count > 1;
            bool container = has && st.item.IsWaterContainer;
            _bSplit.gameObject.SetActive(!container); _bEmpty.gameObject.SetActive(container);
            _bEmpty.interactable = container && st.water > 0;
            _bDrop.interactable = has;
        }

        static string Stats(ItemStack st, bool onPaper = false)
        {
            var i = st.item; var sb = new StringBuilder();
            if (i.hunger > 0) sb.Append($"Hunger  +{i.hunger:0}\n");
            if (i.thirst != 0) sb.Append($"Thirst  {(i.thirst > 0 ? "+" : "")}{i.thirst:0}\n");
            if (i.health > 0) sb.Append($"Health  +{i.health:0}\n");
            if (i.sicknessChance > 0) sb.Append($"Risk of sickness  {i.sicknessChance * 100:0}%\n");
            if (i.cookedResult) sb.Append("Can be cooked on a campfire\n");
            if (i.IsWaterContainer) sb.Append("Water  ").Append(WaterText(st, onPaper)).Append(WaterAdvice(st)).Append('\n');
            if (i.damage > 0) sb.Append($"Damage  {i.damage:0}" + (i.heavyDamage > 0 ? $"  (heavy {i.heavyDamage:0})" : "") + "\n");
            if (i.tool != ToolKind.None) sb.Append("Tool  " + i.tool.ToString().Replace(",", " /") + "\n");
            if (i.HasDurability) sb.Append($"Durability  {st.durability:0} / {i.maxDurability:0}\n");
            if (i.IsPlaceable) sb.Append("Select on the hotbar and left click to place\n");
            if (i.lightRange > 0) sb.Append("Gives light and a little warmth\n");
            return sb.ToString();
        }

        void ShowTip(ItemStack st, RectTransform at)
        {
            if (st == null || SlotView.Dragging) { _tooltip.gameObject.SetActive(false); return; }
            _tipText.text = st.item.IsWaterContainer
                ? "<b>" + st.item.displayName + "</b>: " + WaterText(st, false) + "\n<color=#bda985>" + st.item.category + "</color>\n" + Stats(st)
                : $"<b>{st.item.displayName}</b>\n<color=#bda985>{st.item.category}</color>\n" + Stats(st);
            _tooltip.sizeDelta = new Vector2(330, Mathf.Max(80, _tipText.preferredHeight + 24));
            _tooltip.gameObject.SetActive(true);
        }

        /// <summary>"2/3 clean water" with the water type colour (darker on the paper panel)</summary>
        static string WaterText(ItemStack st, bool onPaper)
        {
            var t = WaterRules.TypeOf(st);
            string n = st.water.ToString(CultureInfo.InvariantCulture) + "/" + st.item.waterCharges.ToString(CultureInfo.InvariantCulture);
            if (t == WaterType.None) return n + " (empty)";
            var c = WaterRules.Color(t); if (onPaper) c = Color.Lerp(c, Color.black, 0.45f);
            return n + " <color=#" + ColorUtility.ToHtmlStringRGB(c) + ">" + WaterRules.Label(t) + "</color>";
        }
        static string WaterAdvice(ItemStack st)
        {
            var t = WaterRules.TypeOf(st);
            if (t == WaterType.None) return "";
            if (t == WaterType.SaltWater) return "  (makes thirst worse: boil it at a campfire)";
            return WaterRules.NeedsBoiling(st) ? "  (boil it at a campfire)" : "  (safe to drink)";
        }

        // ================================================================== actions
        void OnEmpty()
        {
            if (!_selected || _selected.inventory != _inv || _pi == null) return;
            _pi.EmptyContainer(_selected.index);
            RefreshAll();
        }

        void OnUse()
        {
            var st = _selected ? _selected.Stack : null; if (st == null || _selected.inventory != _inv) return;
            if (st.item.IsFood) { UIManager.Instance?.Close(); _pi.Eat(st.item); }
            else if (st.item.IsWaterContainer)
            {
                if (_selected.index < _inv.hotbarSize) _inv.SetActiveSlot(_selected.index);
                else if (!MoveToHotbar(_selected.index)) return;
                UIManager.Instance?.Close(); _pi.UseActiveConsumable();
            }
        }

        bool MoveToHotbar(int from)
        {
            int target = -1;
            for (int i = 0; i < _inv.hotbarSize; i++) if (_inv.Get(i) == null) { target = i; break; }
            if (target < 0) target = _inv.ActiveSlot;
            if (!_inv.Move(from, _inv, target)) return false;
            _inv.SetActiveSlot(target); return true;
        }

        void OnEquip()
        {
            if (!_selected || _selected.inventory != _inv || _selected.Stack == null) return;
            if (_selected.index < _inv.hotbarSize) { _inv.SetActiveSlot(_selected.index); return; }
            MoveToHotbar(_selected.index); _selected = null; RefreshAll();
        }

        void OnSplit() { if (_selected && _selected.inventory == _inv) _inv.Split(_selected.index); }

        void OnDrop()
        {
            if (!_selected || _selected.inventory != _inv) return;
            DropFrom(_inv, _selected.index); _selected = null; RefreshAll();
        }

        void DropFrom(InventorySystem inv, int index)
        {
            var st = inv.Get(index); if (st == null || !_player) return;
            var taken = inv.TakeFromSlot(index, st.count);
            WorldPickup.DropStack(taken, _player.transform.position + _player.transform.forward * 0.8f + Vector3.up * 0.4f);
            GameEvents.Raise(GameEventType.ItemDropped, taken.item.id, taken.count, _player.transform.position);
        }

        void Transfer(SlotView s)
        {
            if (s.Stack == null) return;
            if (_storage != null) { if (s.inventory == _inv) _inv.QuickTransfer(s.index, _storage); else _storage.QuickTransfer(s.index, _inv); return; }
            // no storage: hotbar <-> pack
            if (s.index < _inv.hotbarSize) { for (int i = _inv.hotbarSize; i < _inv.Slots.Length; i++) if (_inv.Get(i) == null) { _inv.Move(s.index, _inv, i); break; } }
            else MoveToHotbar(s.index);
        }

        void OnSlotDropped(SlotView from, SlotView to)
        {
            if (!_canvas.gameObject.activeSelf || from.inventory == null || to.inventory == null) return;
            if (!from.inventory.Move(from.index, to.inventory, to.index)) PlayerInteraction.Notify(to.inventory.IsOverweight || (to.inventory.maxWeight > 0) ? "Too heavy." : "Can't move that there.");
            _selected = null; RefreshAll();
        }

        void OnDropOutside()
        {
            var src = SlotView.DragSource; if (src == null || src.inventory == null) return;
            DropFrom(src.inventory, src.index); _selected = null; RefreshAll();
        }

        // ================================================================== crafting
        void BuildTiles()
        {
            if (_craft == null || _tileRoot == null) return;
            foreach (Transform c in _tileRoot) Destroy(c.gameObject);
            _tiles.Clear();
            var db = ItemDatabase.Instance; if (db == null) return;
            int n = 0; const float W = 214, H = 150;
            foreach (var r in db.recipes)
            {
                if (r == null || r.output == null) continue;
                if (_cat.HasValue && r.category != _cat.Value) continue;
                bool known = _craft.IsKnown(r);
                int col = n % 3, row = n / 3; n++;
                var bg = UIFactory.Image(_tileRoot, "Tile_" + r.id, UIStyle.Slot, Color.white, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(8 + col * (W + 8), -8 - row * (H + 8)), new Vector2(W, H), true);
                var icon = UIFactory.Image(bg.transform, "Icon", known ? r.output.icon : UIStyle.Icon("unknown"), known ? Color.white : new Color(0.3f, 0.28f, 0.25f), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -10), new Vector2(92, 92));
                icon.preserveAspect = true;
                var name = UIFactory.Label(bg.transform, "Name", known ? r.output.displayName : "? ? ?", 18, UIStyle.Text, TextAnchor.LowerCenter, UIStyle.Body, Vector2.zero, Vector2.one, new Vector2(0.5f, 0), new Vector2(0, 8), Vector2.zero);
                var btn = bg.gameObject.AddComponent<Button>(); var rr = r;
                btn.onClick.AddListener(() => { _recipe = rr; RefreshCraft(); });
                _tiles.Add((r, bg, icon, name));
            }
            _tileRoot.sizeDelta = new Vector2(0, Mathf.Ceil(n / 3f) * (H + 8) + 16);
            for (int i = 0; i < _catButtons.Count; i++) _catButtons[i].GetComponent<Image>().color = i < _catOf.Count && _catOf[i] == _cat ? Color.white : new Color(0.6f, 0.55f, 0.5f);
            if (_recipe == null && _tiles.Count > 0) _recipe = _tiles[0].r;
            RefreshCraft();
        }

        void RefreshCraft()
        {
            if (_craft == null) return;
            foreach (var t in _tiles)
            {
                bool known = _craft.IsKnown(t.r); bool can = known && _craft.Check(t.r) == null;
                bool blocked = known && !can && _craft.CheckRequirements(t.r) != null;          // missing tool / station / day: dimmer
                t.bg.sprite = t.r == _recipe ? UIStyle.SlotActive : UIStyle.Slot;
                t.name.color = can ? UIStyle.Text : known ? UIStyle.TextDim : new Color(0.4f, 0.38f, 0.35f);
                t.icon.color = known ? (can ? Color.white : blocked ? new Color(0.45f, 0.43f, 0.4f) : new Color(0.65f, 0.62f, 0.58f)) : new Color(0.3f, 0.28f, 0.25f);
            }
            var r = _recipe;
            bool k = r != null && _craft.IsKnown(r);
            _cIcon.enabled = r != null; if (r != null) _cIcon.sprite = k ? r.output.icon : UIStyle.Icon("unknown");
            _cName.text = r == null ? "" : k ? r.output.displayName + (r.outputCount > 1 ? $"  x{r.outputCount}" : "") : "Unknown recipe";
            _cInfo.text = r == null ? "" : $"{r.category.ToString().ToUpperInvariant()}   {r.craftSeconds:0.#} s" + (r.station == CraftStation.Campfire ? "   needs a lit campfire" : "");
            _cDesc.text = r == null ? "" : k ? r.output.description : (string.IsNullOrEmpty(r.hint) ? "Find one of its materials to learn it." : r.hint);
            var sb = new StringBuilder();
            if (r != null && k)
                foreach (var ing in r.ingredients)
                {
                    int have = _inv.Count(ing.item);
                    string col = have >= ing.count ? "#3d5a22" : "#9a2a1a";
                    sb.Append($"<color={col}>{ing.item.displayName}   {have} / {ing.count}</color>\n");
                }
            if (r != null && k && r.requirements != null)
                foreach (var q in r.requirements)
                {
                    if (q.tool != ToolKind.None) sb.Append($"<color={(_craft.HasToolInPack(q.tool) ? "#3d5a22" : "#9a2a1a")}>Tool   {CraftingSystem.ToolLabel(q.tool)}</color>\n");
                    if (q.nearStation != CraftStation.None) sb.Append($"<color={(_craft.StationAvailable(q.nearStation) ? "#3d5a22" : "#9a2a1a")}>Near   {CraftingSystem.StationLabel(q.nearStation)}</color>\n");
                }
            _cReq.text = sb.ToString();
            string why = r != null ? _craft.Check(r) : null;
            string need = why != null && k ? _craft.CheckRequirements(r) : null;          // also name a missing tool while materials are short
            _cReason.text = why == null ? "" : need != null && need != why ? why + "\n" + need : why;
            _bCraft.interactable = r != null && why == null;
            _bCraft5.interactable = r != null && why == null && _craft.MaxCraftable(r) >= 2;
            UpdateQueue();
        }

        void Craft(int times)
        {
            if (_recipe == null) return;
            int made = 0;
            for (int i = 0; i < times; i++) { if (!_craft.Enqueue(_recipe)) break; made++; }
            if (made == 0) PlayerInteraction.Notify(_craft.Check(_recipe) ?? "Can't craft that.");
            RefreshCraft();
        }

        void UpdateQueue()
        {
            if (_craft == null) return;
            for (int i = 0; i < _queueIcons.Count; i++)
            {
                bool has = i < _craft.Queue.Count;
                _queueIcons[i].enabled = has; if (has) _queueIcons[i].sprite = _craft.Queue[i].recipe.output.icon;
                _queueBars[i].enabled = has && i == 0; if (has && i == 0) _queueBars[i].fillAmount = _craft.CurrentProgress01;
            }
        }

        // ================================================================== preview
        void BuildPreviewCamera()
        {
            if (_previewCam || !_player) return;
            _rt = new RenderTexture(512, 768, 16, RenderTextureFormat.ARGB32) { name = "RT_CharacterPreview" };
            var go = new GameObject("PreviewCamera"); go.transform.SetParent(_player.transform, false);
            go.transform.localPosition = new Vector3(0.0f, 1.05f, 2.6f); go.transform.localRotation = Quaternion.Euler(4f, 180f, 0f);
            _previewCam = go.AddComponent<Camera>();
            _previewCam.clearFlags = CameraClearFlags.SolidColor; _previewCam.backgroundColor = new Color(0.08f, 0.07f, 0.06f, 1f);
            _previewCam.cullingMask = 1 << _player.layer; _previewCam.fieldOfView = 32f; _previewCam.nearClipPlane = 0.3f; _previewCam.farClipPlane = 6f;
            _previewCam.targetTexture = _rt; _previewCam.enabled = false;
            _preview.texture = _rt;
        }
    }
}
