using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using PrimalFrontier.Items;
using PrimalFrontier.UI;

namespace PrimalFrontier.Building
{
    /// <summary>
    /// The small build menu (B): one card per known piece with its icon, name and cost as have / need (green when the pack
    /// holds enough, red otherwise), click or 1..9 to pick, B / Esc / right click to close. While a piece is being placed a
    /// short strip at the bottom shows the piece and its cost. Built in code (UIFactory) on its own canvas under [Build].
    /// </summary>
    public class BuildMenuUI : MonoBehaviour
    {
        public static BuildMenuUI Instance { get; private set; }
        BuildSystem _bs; Canvas _canvas; RectTransform _panel, _row, _strip; Text _title, _stripText, _hint;
        readonly List<Card> _cards = new List<Card>();
        readonly List<StructureDefinition> _shown = new List<StructureDefinition>();
        bool _visible;

        class Card { public RectTransform root; public Image back, icon; public Text name, cost, key; public Button button; public StructureDefinition def; }

        public bool Visible => _visible;
        public IReadOnlyList<StructureDefinition> Shown => _shown;

        public static BuildMenuUI Ensure(BuildSystem bs)
        {
            var ui = Instance ? Instance : bs.GetComponentInChildren<BuildMenuUI>(true);
            if (!ui) { var go = new GameObject("[BuildMenu]"); go.transform.SetParent(bs.transform, false); ui = go.AddComponent<BuildMenuUI>(); }
            ui._bs = bs; ui.BuildLayout();
            return ui;
        }

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void BuildLayout()
        {
            if (_canvas) return;
            UIFactory.BeginBuild();
            try
            {
                _canvas = UIFactory.Canvas("[Build]", 33, transform);
                var root = _canvas.transform;
                _panel = UIFactory.Rect(root, "Panel", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 120), new Vector2(1180, 250));
                UIFactory.Fill(_panel, "Back", UIStyle.PanelDark, new Color(1, 1, 1, 0.96f), true);
                _title = UIFactory.Label(_panel, "Title", "BUILD", 30, UIStyle.Accent, TextAnchor.MiddleLeft, UIStyle.Head, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -8), new Vector2(-48, 40));
                _hint = UIFactory.Label(_panel, "Hint", "click a piece or press its number   B / Esc closes", 18, UIStyle.TextDim, TextAnchor.MiddleRight, UIStyle.Body, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -8), new Vector2(-48, 40));
                _row = UIFactory.Rect(_panel, "Row", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(-40, -70));
                _strip = UIFactory.Rect(root, "Strip", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 150), new Vector2(720, 44));
                UIFactory.Fill(_strip, "Back", UIStyle.PanelDark, new Color(1, 1, 1, 0.8f));
                _stripText = UIFactory.Label(_strip, "Text", "", 20, UIStyle.Text, TextAnchor.MiddleCenter, UIStyle.Body);
            }
            finally { UIFactory.EndBuild(); }
            _panel.gameObject.SetActive(false); _strip.gameObject.SetActive(false);
        }

        public void Show()
        {
            BuildLayout();
            _visible = true;
            _panel.gameObject.SetActive(true); _strip.gameObject.SetActive(false);
            Refresh();
            if (_bs && _bs.Inventory) { _bs.Inventory.Changed -= Refresh; _bs.Inventory.Changed += Refresh; }
        }

        public void Hide()
        {
            _visible = false;
            if (_panel) _panel.gameObject.SetActive(false);
            if (_bs && _bs.Inventory) _bs.Inventory.Changed -= Refresh;
        }

        /// <summary>rebuild the cards from the known pieces and the pack</summary>
        public void Refresh()
        {
            if (!_bs || !_row) return;
            _shown.Clear(); _shown.AddRange(_bs.Available());
            int n = _shown.Count;
            float w = Mathf.Min(210f, (_row.rect.width > 10f ? _row.rect.width : 1140f) / Mathf.Max(1, n)), gap = 10f;
            float x0 = -(n * w + (n - 1) * gap) * 0.5f + w * 0.5f;
            for (int i = 0; i < n; i++)
            {
                var c = i < _cards.Count ? _cards[i] : MakeCard(i);
                c.def = _shown[i];
                c.root.gameObject.SetActive(true);
                c.root.anchoredPosition = new Vector2(x0 + i * (w + gap), 0); c.root.sizeDelta = new Vector2(w, 0);
                c.name.text = c.def.Name;
                c.key.text = (i + 1).ToString();
                var it = c.def.item;
                c.icon.sprite = it ? it.icon : null; c.icon.enabled = c.icon.sprite;
                bool ok = true; var sb = new System.Text.StringBuilder();
                foreach (var ing in c.def.Cost)
                {
                    if (!ing.item) continue;
                    int have = _bs.Inventory ? _bs.Inventory.Count(ing.item) : 0;
                    bool enough = have >= ing.count; ok &= enough;
                    sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(enough ? UIStyle.Good : UIStyle.Bad)).Append('>')
                      .Append(ing.item.displayName).Append("  ").Append(have).Append(" / ").Append(ing.count).Append("</color>\n");
                }
                c.cost.text = sb.ToString().TrimEnd();
                c.back.color = ok ? new Color(1, 1, 1, 1f) : new Color(0.75f, 0.6f, 0.55f, 1f);
            }
            for (int i = n; i < _cards.Count; i++) _cards[i].root.gameObject.SetActive(false);
            _title.text = n == 0 ? "BUILD: no pieces known yet" : "BUILD";
        }

        Card MakeCard(int i)
        {
            var c = new Card();
            UIFactory.BeginBuild();
            try
            {
                c.root = UIFactory.Rect(_row, "Card" + i, new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200, 0));
                c.back = UIFactory.Fill(c.root, "Back", UIStyle.Slot, Color.white, true);
                c.button = c.back.gameObject.GetComponent<Button>();
                if (!c.button)
                {
                    c.button = c.back.gameObject.AddComponent<Button>();
                    var cb = c.button.colors; cb.highlightedColor = new Color(1f, 0.95f, 0.85f); cb.pressedColor = new Color(0.8f, 0.7f, 0.55f); c.button.colors = cb;
                }
                int idx = i;
                if (Application.isPlaying) c.button.onClick.AddListener(() => Pick(idx));
                c.icon = UIFactory.Image(c.root, "Icon", null, Color.white, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -8), new Vector2(72, 72));
                c.icon.preserveAspect = true;
                c.key = UIFactory.Label(c.root, "Key", "1", 20, UIStyle.Accent, TextAnchor.UpperLeft, UIStyle.Head, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(8, -6), new Vector2(30, 26));
                c.name = UIFactory.Label(c.root, "Name", "", 21, UIStyle.Text, TextAnchor.MiddleCenter, UIStyle.Head, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -84), new Vector2(-8, 28));
                c.cost = UIFactory.Label(c.root, "Cost", "", 17, UIStyle.Text, TextAnchor.UpperCenter, UIStyle.Body, new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -112), new Vector2(-10, -116));
            }
            finally { UIFactory.EndBuild(); }
            _cards.Add(c);
            return c;
        }

        public void Pick(int index)
        {
            if (!_bs || index < 0 || index >= _shown.Count) return;
            _bs.Select(_shown[index]);
        }

        void Update()
        {
            if (!_bs) return;
            if (_visible)
            {
                var kb = Keyboard.current;
                if (kb != null && !Player.PlayerInputReader.Simulate)
                {
                    for (int i = 0; i < Mathf.Min(9, _shown.Count); i++)
                        if (kb[Key.Digit1 + i].wasPressedThisFrame) { Pick(i); return; }
                }
                return;
            }
            // placement strip
            bool placing = _bs.Placing && _bs.Structure;
            if (_strip.gameObject.activeSelf != placing) _strip.gameObject.SetActive(placing);
            if (!placing) return;
            var def = _bs.Structure; var sb = new System.Text.StringBuilder(def.Name);
            foreach (var ing in def.Cost)
            {
                if (!ing.item) continue;
                int have = _bs.Inventory ? _bs.Inventory.Count(ing.item) : 0;
                sb.Append("   <color=#").Append(ColorUtility.ToHtmlStringRGB(have >= ing.count ? UIStyle.Good : UIStyle.Bad)).Append('>').Append(ing.item.displayName).Append(' ').Append(have).Append('/').Append(ing.count).Append("</color>");
            }
            if (_bs.Snapped) sb.Append("   <color=#").Append(ColorUtility.ToHtmlStringRGB(UIStyle.TextDim)).Append(">snapped</color>");
            sb.Append("   <color=#").Append(ColorUtility.ToHtmlStringRGB(UIStyle.TextDim)).Append(">[B] pieces</color>");
            _stripText.text = sb.ToString();
        }
    }
}
