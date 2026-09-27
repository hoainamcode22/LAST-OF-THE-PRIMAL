using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using PrimalFrontier.Story;

namespace PrimalFrontier.UI
{
    /// <summary>
    /// The survival journal (J): leather cover, two aged paper pages. Left page lists what has been discovered in the
    /// chosen section (SURVIVAL, CREATURES, CRAFTING, WORLD) with an undiscovered count; right page shows the sketch
    /// and the handwritten note. Pages slide in when an entry changes.
    /// </summary>
    public class JournalUI : MonoBehaviour, IBakeableUI
    {
        Canvas _canvas; RectTransform _list, _page; CanvasGroup _pageGroup;
        Text _count, _title, _text; Image _sketch;
        readonly List<Button> _tabs = new List<Button>();
        JournalCategory _cat = JournalCategory.Survival; string _entry;
        float _anim;

        void Awake() { Build(); _canvas.gameObject.SetActive(false); }
        public void BakeLayout() { Build(); _canvas.gameObject.SetActive(false); }
        void Start() { if (UIManager.Instance) UIManager.Instance.Changed += OnScreen; var j = JournalSystem.Instance; if (j) j.Unlocked += e => { if (_canvas.gameObject.activeSelf) Refresh(); }; }

        void Build()
        {
            UIFactory.BeginBuild();
            try { BuildLayout(); } finally { UIFactory.EndBuild(); }
        }

        void BuildLayout()
        {
            _canvas = UIFactory.Canvas("[Journal]", 31, transform);
            var root = _canvas.transform;
            UIFactory.Fill(root, "Dim", null, new Color(0, 0, 0, 0.6f), true);
            var book = UIFactory.Image(root, "Cover", UIStyle.Leather, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500, 900), true).rectTransform;
            var left = UIFactory.Image(book, "LeftPage", UIStyle.Paper, Color.white, new Vector2(0, 0), new Vector2(0.5f, 1), new Vector2(0, 0.5f), Vector2.zero, Vector2.zero).rectTransform;
            if (UIFactory.Fresh(left)) { left.offsetMin = new Vector2(46, 40); left.offsetMax = new Vector2(-6, -40); }
            var right = UIFactory.Image(book, "RightPage", UIStyle.Paper, Color.white, new Vector2(0.5f, 0), new Vector2(1, 1), new Vector2(1, 0.5f), Vector2.zero, Vector2.zero).rectTransform;
            if (UIFactory.Fresh(right)) { right.offsetMin = new Vector2(6, 40); right.offsetMax = new Vector2(-46, -40); }
            UIFactory.Image(book, "Spine", null, new Color(0.12f, 0.07f, 0.04f, 0.9f), new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(12, -60));

            UIFactory.Label(left, "Heading", "SURVIVAL JOURNAL", 34, UIStyle.TextDark, TextAnchor.UpperCenter, UIStyle.Head, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -28), new Vector2(0, 44), false);
            string[] names = { "SURVIVAL", "CREATURES", "CRAFTING", "WORLD" };
            for (int i = 0; i < 4; i++)
            {
                int k = i;
                var b = UIFactory.Button(left, "Tab" + i, names[i], () => { _cat = (JournalCategory)k; _entry = null; Refresh(); }, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(34 + i * 162, -86), new Vector2(154, 44), 18);
                _tabs.Add(b);
            }
            _list = UIFactory.Rect(left, "List", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, Vector2.zero);
            if (UIFactory.Fresh(_list)) { _list.offsetMin = new Vector2(40, 70); _list.offsetMax = new Vector2(-40, -150); }
            _count = UIFactory.Label(left, "Count", "", 18, new Color(0.45f, 0.3f, 0.18f), TextAnchor.LowerCenter, UIStyle.Hand, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(0, 30), false);

            _page = UIFactory.Rect(right, "Page", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _pageGroup = UIFactory.Group(_page.gameObject);
            _title = UIFactory.Label(_page, "Title", "", 36, UIStyle.TextDark, TextAnchor.UpperLeft, UIStyle.Head, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(48, -34), new Vector2(-96, 48), false);
            _sketch = UIFactory.Image(_page, "Sketch", null, new Color(1, 1, 1, 0.92f), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -96), new Vector2(360, 300));
            _sketch.preserveAspect = true;
            _text = UIFactory.Label(_page, "Text", "", 24, UIStyle.TextDark, TextAnchor.UpperLeft, UIStyle.Hand, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(52, -414), new Vector2(-104, 380), false);
            UIFactory.Button(book, "Close", "X", () => UIManager.Instance?.Close(), new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-8, -8), new Vector2(48, 48), 24);
            UIFactory.Label(book, "Keys", "J / Esc to close", 16, new Color(0.85f, 0.78f, 0.65f), TextAnchor.LowerRight, UIStyle.Body, new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-50, 8), new Vector2(300, 26));
        }

        void OnScreen(UIScreen from, UIScreen to)
        {
            bool open = to == UIScreen.Journal;
            _canvas.gameObject.SetActive(open);
            if (open)
            {
                var j = JournalSystem.Instance;
                if (j && j.HasUnread && j.UnlockedInOrder.Count > 0) { var last = j.Get(j.UnlockedInOrder[j.UnlockedInOrder.Count - 1]); _cat = last.category; _entry = last.id; }
                if (j) j.HasUnread = false;
                Refresh();
            }
        }

        void Refresh()
        {
            var j = JournalSystem.Instance; if (j == null) return;
            for (int i = 0; i < _tabs.Count; i++) _tabs[i].GetComponent<Image>().color = (int)_cat == i ? Color.white : new Color(0.62f, 0.56f, 0.5f);
            foreach (Transform c in _list) Destroy(c.gameObject);
            var all = j.entries.Where(e => e.category == _cat).ToList();
            var found = all.Where(e => j.IsUnlocked(e.id)).ToList();
            if (_entry == null || !found.Any(e => e.id == _entry)) _entry = found.Count > 0 ? found[0].id : null;
            float y = 0;
            foreach (var e in found)
            {
                var en = e;
                var b = UIFactory.Button(_list, "E_" + e.id, e.title, () => { _entry = en.id; _anim = 0f; Refresh(); }, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -y), new Vector2(0, 46), 22);
                var img = b.GetComponent<Image>(); img.color = e.id == _entry ? new Color(1f, 0.95f, 0.85f) : new Color(1, 1, 1, 0.0f);
                var t = b.GetComponentInChildren<Text>(); t.color = UIStyle.TextDark; t.font = UIStyle.Hand; t.alignment = TextAnchor.MiddleLeft; t.GetComponent<Shadow>().enabled = false;
                y += 50;
            }
            int missing = all.Count - found.Count;
            _count.text = found.Count == 0 ? "Nothing written here yet." : missing > 0 ? $"{found.Count} of {all.Count} discovered" : "Every page filled";
            var cur = _entry != null ? j.Get(_entry) : null;
            _title.text = cur != null ? cur.title : "";
            _text.text = cur != null ? cur.text : "The pages are empty. Explore, gather, survive, and the journal will fill itself.";
            var sk = cur != null ? Resources.Load<Sprite>("UI/Sketches/sketch_" + cur.sketch) : null;
            if (sk == null && cur != null) { var tex = Resources.Load<Texture2D>("UI/Sketches/sketch_" + cur.sketch); if (tex) sk = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f)); }
            _sketch.sprite = sk; _sketch.enabled = sk != null;
        }

        void Update()
        {
            if (!_canvas.gameObject.activeSelf) return;
            _anim = Mathf.MoveTowards(_anim, 1f, Time.unscaledDeltaTime * 3.5f);
            _pageGroup.alpha = _anim; _page.anchoredPosition = new Vector2((1f - _anim) * 40f, 0);
        }
    }
}
