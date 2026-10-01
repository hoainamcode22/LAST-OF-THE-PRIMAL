using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using PrimalFrontier.Core;
using PrimalFrontier.Story;
using PrimalFrontier.World;

namespace PrimalFrontier.UI
{
    /// <summary>top-down picture of the island (baked once in the editor) and the world area it covers</summary>
    [CreateAssetMenu(menuName = "Primal Frontier/Minimap Data", fileName = "MinimapData")]
    public class MinimapData : ScriptableObject
    {
        public Texture2D map;
        public Vector2 worldMin;          // x, z of the bottom-left corner
        public float worldSize = 640f;    // metres covered by the square picture
    }

    /// <summary>
    /// Small map in the top-right corner, discovery only: the baked island picture is hidden under a dark fog except where
    /// the player has walked (a small reveal texture, soft edges, updated a few times a second only while moving); markers
    /// only for what the survivor knows: camp (placed fires and shelters), fresh water once seen, the belongings left at a
    /// death, the places visited (wreck, cave, waterfall, volcano, old camp...; big landmarks once seen from nearby), and the
    /// current objective's direction when a mission needs one. No creatures, no resources, nothing unknown. Tap it (or M)
    /// for the whole island with place names. No run-time camera, no per-frame allocation. The reveal and the water seen are
    /// saved as the "map" section.
    /// </summary>
    public class Minimap : MonoBehaviour, IBakeableUI, ISaveSection
    {
        public MinimapData data;
        [Tooltip("metres across the small map")] public float viewSize = 140f;
        [Tooltip("metres revealed around the player")] public float revealRadius = 42f;
        [Tooltip("texels across the reveal (the whole map area)")] public int revealResolution = 128;
        [Tooltip("places marked once visited ('|')")] public string markedPlaces = "shipwreck|cave|volcano|waterfall|old_camp|fossil_bed|nest|migration_view|pond|canyon|ridge|wetland|lm_rock_ridge|lm_fallen_tree|lm_fossil_skeleton|lm_giant_tree|lm_black_ridge|lm_underground_pool|cave_hidden_chamber";
        [Tooltip("big landmarks marked once the player has been this close (m)")] public float landmarkSeeRadius = 120f;
        [Tooltip("landmarks known from afar ('|')")] public string landmarks = "shipwreck|volcano|waterfall";
        [Tooltip("metres to a fresh water source that count as having seen it")] public float waterSeeRange = 22f;
        public Color campColor = new Color(1f, 0.62f, 0.25f), waterColor = new Color(0.35f, 0.7f, 1f), placeColor = new Color(0.95f, 0.9f, 0.75f),
                     belongingsColor = new Color(0.9f, 0.45f, 0.35f), fogColor = new Color(0.1f, 0.085f, 0.07f, 1f);

        RectTransform _root, _mapRect, _markers, _labels, _arrow; RawImage _map, _fog; Text _north, _label; CanvasGroup _group;
        readonly List<Image> _pool = new List<Image>(); int _used;
        readonly List<Text> _labelPool = new List<Text>(); int _labelsUsed;
        bool _big; float _bigK;
        Vector2 _smallSize = new Vector2(230, 230), _smallPos = new Vector2(-24, -24);
        public bool Big => _big;

        // discovery state
        Texture2D _fogTex; Color32[] _fogPx; bool _fogDirty; float _nextReveal; Vector3 _lastReveal = new Vector3(1e9f, 0, 0);
        readonly List<Vector3> _water = new List<Vector3>();
        readonly HashSet<string> _known = new HashSet<string>(StringComparer.Ordinal);
        string[] _markedIds, _landmarkIds;
        float _nextWaterLook;

        void Awake()
        {
            if (!data) data = Resources.Load<MinimapData>("MinimapData");
            _markedIds = StoryIds.Split(markedPlaces); _landmarkIds = StoryIds.Split(landmarks);
            Build();
            if (Application.isPlaying) MakeFog();
        }
        void OnEnable() { GameEvents.Raised -= OnEvent; GameEvents.Raised += OnEvent; if (Application.isPlaying) SaveSystem.RegisterSection(this); }
        void OnDisable() { GameEvents.Raised -= OnEvent; SaveSystem.UnregisterSection(this); }
        void OnDestroy() { if (_fogTex) Destroy(_fogTex); }

        public void BakeLayout() => Build();

        void Build()
        {
            UIFactory.BeginBuild();
            try
            {
                var hud = UIFactory.Canvas("[HUD]", 10, transform).transform;
                var frame = UIFactory.Image(hud, "Minimap", UIStyle.PanelDark, new Color(1, 1, 1, 0.9f), new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), _smallPos, _smallSize, true);
                _root = frame.rectTransform;
                _smallSize = _root.sizeDelta; _smallPos = _root.anchoredPosition;      // the size / place set in the scene
                _group = UIFactory.Group(frame.gameObject); _group.ignoreParentGroups = true; _group.interactable = true; _group.blocksRaycasts = true;
                var mask = UIFactory.Stretch(_root, "Mask", 8f); mask.gameObject.GetOrAdd<RectMask2D>();
                _mapRect = UIFactory.Stretch(mask, "Map", 0f);
                _map = _mapRect.gameObject.GetOrAdd<RawImage>(); _map.raycastTarget = false;
                if (data && data.map) _map.texture = data.map;
                var fogRt = UIFactory.Stretch(mask, "Fog", 0f);
                if (UIFactory.Fresh(fogRt)) fogRt.SetSiblingIndex(_mapRect.GetSiblingIndex() + 1);
                _fog = fogRt.gameObject.GetOrAdd<RawImage>(); _fog.raycastTarget = false; _fog.color = fogColor;
                _markers = UIFactory.Stretch(mask, "Markers", 0f);
                _labels = UIFactory.Stretch(mask, "Labels", 0f);
                var ar = UIFactory.Image(mask, "Player", UIStyle.Icon("crosshair"), Color.white, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18, 18));
                _arrow = ar.rectTransform;
                _north = UIFactory.Label(_root, "North", "N", 18, UIStyle.Accent, TextAnchor.UpperCenter, UIStyle.Head, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -4), new Vector2(40, 24));
                _label = UIFactory.Label(_root, "Hint", "", 14, UIStyle.TextDim, TextAnchor.LowerCenter, UIStyle.Body, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 4), new Vector2(0, 20));
                if (!Application.isPlaying) return;
                var btn = frame.gameObject.GetOrAdd<Button>(); btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(Toggle);
            }
            finally { UIFactory.EndBuild(); }
        }

        public void Toggle()
        {
            _big = !_big; Audio.SfxPlayer.Instance.Play2D(Audio.SfxId.UiClick, 0.5f);
            if (_big && TutorialManager.Instance) TutorialManager.Instance.NotifyMapOpened();
        }

        // ------------------------------------------------------------------ discovery
        void MakeFog()
        {
            int n = Mathf.Clamp(revealResolution, 32, 512);
            if (_fogTex && _fogTex.width == n) return;
            _fogTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "MinimapFog" };
            _fogPx = new Color32[n * n];
            ClearFog();
            if (_fog) _fog.texture = _fogTex;
        }
        void ClearFog()
        {
            if (_fogPx == null) return;
            var c = new Color32(255, 255, 255, 255);
            for (int i = 0; i < _fogPx.Length; i++) _fogPx[i] = c;
            _fogDirty = true;
        }

        /// <summary>new game: nothing known</summary>
        public void ResetDiscovery()
        {
            ClearFog(); _water.Clear(); _known.Clear(); _lastReveal = new Vector3(1e9f, 0, 0);
            if (_fogTex) { _fogTex.SetPixels32(_fogPx); _fogTex.Apply(false); _fogDirty = false; }
        }

        /// <summary>share of the island area revealed (0..1)</summary>
        public float Revealed01
        {
            get { if (_fogPx == null) return 0f; int n = 0; for (int i = 0; i < _fogPx.Length; i++) if (_fogPx[i].a < 128) n++; return n / (float)_fogPx.Length; }
        }

        void Reveal(Vector3 w, float radius)
        {
            if (_fogPx == null || !data || data.worldSize <= 0f) return;
            int n = _fogTex.width;
            float tx = (w.x - data.worldMin.x) / data.worldSize * n, ty = (w.z - data.worldMin.y) / data.worldSize * n;
            float r = radius / data.worldSize * n, inner = r * 0.55f;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(tx - r)), x1 = Mathf.Min(n - 1, Mathf.CeilToInt(tx + r));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(ty - r)), y1 = Mathf.Min(n - 1, Mathf.CeilToInt(ty + r));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x + 0.5f - tx, dy = y + 0.5f - ty, d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d >= r) continue;
                    float k = d <= inner ? 0f : Mathf.SmoothStep(0f, 1f, (d - inner) / (r - inner));
                    byte a = (byte)Mathf.RoundToInt(k * 255f);
                    int i = y * n + x;
                    if (a < _fogPx[i].a) { _fogPx[i].a = a; _fogDirty = true; }
                }
        }

        void OnEvent(GameEvent e)
        {
            if (e.type == GameEventType.ZoneEntered && !string.IsNullOrEmpty(e.id)) _known.Add(StoryIds.Location(e.id));
            else if (e.type == GameEventType.GameLoaded) SyncKnown();
        }
        void SyncKnown() { var zm = ZoneManager.Instance; if (zm) foreach (var v in zm.Visited) if (!string.IsNullOrEmpty(v)) _known.Add(StoryIds.Location(v)); }

        void UpdateDiscovery(Vector3 p)
        {
            if (Time.unscaledTime >= _nextReveal)
            {
                _nextReveal = Time.unscaledTime + 0.35f;
                if ((p - _lastReveal).sqrMagnitude > 9f) { _lastReveal = p; Reveal(p, revealRadius); }
                // big landmarks: known once the player was close enough to see them
                var ms = MissionSystem.Instance;
                if (ms) foreach (var id in _landmarkIds) if (!_known.Contains(id) && ms.TryLocation(id, out var at) && Flat(at - p) < landmarkSeeRadius) _known.Add(id);
            }
            if (Time.unscaledTime >= _nextWaterLook)
            {
                _nextWaterLook = Time.unscaledTime + 1f;
                foreach (var w in WaterSource.All)
                {
                    if (!w || !w.fresh || !MissionSystem.WaterPoint(w, p, out var at) || Flat(at - p) > waterSeeRange) continue;
                    bool near = false; for (int i = 0; i < _water.Count; i++) if (Flat(_water[i] - at) < 40f) { near = true; break; }
                    if (!near && _water.Count < 48) _water.Add(at);
                }
            }
            if (_fogDirty && _fogTex) { _fogDirty = false; _fogTex.SetPixels32(_fogPx); _fogTex.Apply(false); }
        }
        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

        // ------------------------------------------------------------------ drawing
        Image Marker(Color c, float size, bool diamond = false)
        {
            Image m;
            if (_used < _pool.Count) m = _pool[_used];
            else
            {
                m = UIFactory.Image(_markers, "M", null, c, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
                _pool.Add(m);
            }
            _used++;
            if (!m.gameObject.activeSelf) m.gameObject.SetActive(true);
            m.color = c; m.rectTransform.sizeDelta = new Vector2(size, size);
            m.rectTransform.localRotation = diamond ? Quaternion.Euler(0, 0, 45f) : Quaternion.identity;
            return m;
        }

        readonly List<Vector2> _labelAt = new List<Vector2>();
        bool Crowded(Vector2 ui)
        {
            for (int i = 0; i < _labelAt.Count; i++) { var d = _labelAt[i] - ui; if (Mathf.Abs(d.x) < 110f && Mathf.Abs(d.y) < 20f) return true; }
            return false;
        }

        Text Label(string text, Vector2 at)
        {
            Text t;
            if (_labelsUsed < _labelPool.Count) t = _labelPool[_labelsUsed];
            else
            {
                t = UIFactory.Label(_labels, "L", "", 15, UIStyle.Text, TextAnchor.LowerCenter, UIStyle.Hand, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(220, 22));
                _labelPool.Add(t);
            }
            _labelsUsed++;
            if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            if (!ReferenceEquals(t.text, text)) t.text = text;
            t.rectTransform.anchoredPosition = at + new Vector2(0f, 9f);
            return t;
        }

        void Update()
        {
            if (_root == null) return;
            if (_group && HUDManager.Instance) _group.alpha = Mathf.Max(HUDManager.Instance.HudAlpha, _bigK);
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.mKey.wasPressedThisFrame && (UIManager.Instance == null || UIManager.Instance.Current == UIScreen.None)) Toggle();
            float dt = Time.unscaledDeltaTime;
            _bigK = Mathf.MoveTowards(_bigK, _big ? 1f : 0f, dt * 4f);
            float k = _bigK * _bigK * (3f - 2f * _bigK);
            var canvasRt = (RectTransform)_root.parent;
            float bigSide = Mathf.Min(canvasRt.rect.height * 0.8f, canvasRt.rect.width * 0.9f);
            _root.anchorMin = _root.anchorMax = Vector2.Lerp(new Vector2(1, 1), new Vector2(0.5f, 0.5f), k);
            _root.pivot = Vector2.Lerp(new Vector2(1, 1), new Vector2(0.5f, 0.5f), k);
            _root.anchoredPosition = Vector2.Lerp(_smallPos, Vector2.zero, k);
            _root.sizeDelta = Vector2.Lerp(_smallSize, new Vector2(bigSide, bigSide), k);
            string hint = _big ? "Tap / M to close" : "";
            if (!ReferenceEquals(_label.text, hint)) _label.text = hint;

            var player = PlayerLocator.Player;
            if (!player || !data || !data.map) { _map.enabled = false; if (_fog) _fog.enabled = false; return; }
            _map.enabled = true;
            if (_fog) { _fog.enabled = _fogTex != null; if (_fog.texture != _fogTex) _fog.texture = _fogTex; }
            var gm = GameManager.Instance;
            if (gm == null || gm.State == GameState.Playing || gm.State == GameState.Sleeping) UpdateDiscovery(player.position);

            float view = Mathf.Lerp(viewSize, data.worldSize, k);
            Vector2 p = new Vector2(player.position.x, player.position.z);
            Vector2 centre = Vector2.Lerp(p, data.worldMin + Vector2.one * data.worldSize * 0.5f, k);
            Vector2 uv0 = (centre - data.worldMin) / data.worldSize - Vector2.one * (view / data.worldSize) * 0.5f;
            var uv = new Rect(uv0, Vector2.one * (view / data.worldSize));
            _map.uvRect = uv; if (_fog) _fog.uvRect = uv;
            float side = _mapRect.rect.width;
            Vector2 ToUi(Vector3 w) => (new Vector2(w.x, w.z) - centre) / view * side;
            bool Inside(Vector2 ui) => Mathf.Abs(ui.x) < side * 0.5f && Mathf.Abs(ui.y) < side * 0.5f;

            _arrow.anchoredPosition = ToUi(player.position);
            _arrow.localRotation = Quaternion.Euler(0, 0, -player.eulerAngles.y + 45f);     // crosshair icon drawn as a diamond pointer

            _used = 0; _labelsUsed = 0;
            float ms = Mathf.Lerp(9f, 14f, k);
            foreach (var c in Campfire.All) if (c) Put(c.transform.position, campColor, ms + 2, true);
            for (int i = 0; i < Shelter.All.Count; i++) { var s = Shelter.All[i]; if (s) Put(s.transform.position, campColor, ms, false); }
            for (int i = 0; i < _water.Count; i++) Put(_water[i], waterColor, ms - 1, false);
            for (int i = 0; i < DeathBundle.All.Count; i++) { var b = DeathBundle.All[i]; if (b) Put(b.transform.position, belongingsColor, ms + 1, true); }
            var mis = MissionSystem.Instance;
            if (mis)
            {
                // the Phase 2 environments first: an older place at the same spot (wetland, deep forest...) does not print over their name
                _labelAt.Clear();
                for (int pass = 0; pass < 2; pass++)
                    foreach (var id in _known)
                    {
                        bool region = Array.IndexOf(StoryIds.Regions, id) >= 0;
                        if (region != (pass == 0)) continue;
                        if (!mis.TryLocation(id, out var at)) continue;
                        bool point = Array.IndexOf(_markedIds, id) >= 0;
                        var ui = ToUi(at);
                        if (point && Inside(ui)) Marker(placeColor, ms, true).rectTransform.anchoredPosition = ui;
                        if (k > 0.6f && Inside(ui))
                        {
                            var t = StoryTexts.Location(id);
                            string title = t != null ? t.title : StoryTexts.Landmark(id)?.title;
                            if (title == null || Crowded(ui)) continue;
                            _labelAt.Add(ui);
                            Label(title, ui);
                        }
                    }
                var target = mis.MarkerTarget;
                if (target.HasValue) PutClamped(target.Value, UIStyle.Accent, ms + 3);
            }
            for (int i = _used; i < _pool.Count; i++) if (_pool[i].gameObject.activeSelf) _pool[i].gameObject.SetActive(false);
            for (int i = _labelsUsed; i < _labelPool.Count; i++) if (_labelPool[i].gameObject.activeSelf) _labelPool[i].gameObject.SetActive(false);

            void Put(Vector3 w, Color c, float size, bool diamond)
            {
                var ui = ToUi(w); if (!Inside(ui)) return;
                Marker(c, size, diamond).rectTransform.anchoredPosition = ui;
            }
            void PutClamped(Vector3 w, Color c, float size)
            {
                var ui = ToUi(w); float lim = side * 0.5f - 8f;
                if (!Inside(ui)) { float s = lim / Mathf.Max(Mathf.Abs(ui.x), Mathf.Abs(ui.y)); ui *= s; }
                Marker(c, size, true).rectTransform.anchoredPosition = ui;
            }
        }

        // ------------------------------------------------------------------ save
        [Serializable] class Saved { public int res; public string fog; public List<Vector3> water = new List<Vector3>(); public List<string> known = new List<string>(); }
        public string SectionKey => "map";
        public string CaptureSection()
        {
            if (_fogPx == null) return null;
            var d = new Saved { res = _fogTex.width, fog = Rle(_fogPx) };
            d.water.AddRange(_water); d.known.AddRange(_known);
            return JsonUtility.ToJson(d);
        }
        public void RestoreSection(string json)
        {
            var d = JsonUtility.FromJson<Saved>(json); if (d == null) return;
            MakeFog(); ClearFog();
            if (d.res == _fogTex.width && !string.IsNullOrEmpty(d.fog)) UnRle(d.fog, _fogPx);
            _water.Clear(); if (d.water != null) _water.AddRange(d.water);
            _known.Clear(); if (d.known != null) foreach (var id in d.known) if (!string.IsNullOrEmpty(id)) _known.Add(id);
            _fogDirty = true; _lastReveal = new Vector3(1e9f, 0, 0);
        }

        /// <summary>fog alpha as (run, value) byte pairs, base64</summary>
        static string Rle(Color32[] px)
        {
            var b = new List<byte>(512);
            int i = 0;
            while (i < px.Length)
            {
                byte v = px[i].a; int run = 1;
                while (i + run < px.Length && run < 255 && px[i + run].a == v) run++;
                b.Add((byte)run); b.Add(v); i += run;
            }
            return Convert.ToBase64String(b.ToArray());
        }
        static void UnRle(string s, Color32[] px)
        {
            byte[] b; try { b = Convert.FromBase64String(s); } catch { return; }
            int o = 0;
            for (int i = 0; i + 1 < b.Length && o < px.Length; i += 2)
                for (int r = 0; r < b[i] && o < px.Length; r++) px[o++].a = b[i + 1];
        }
    }
}
