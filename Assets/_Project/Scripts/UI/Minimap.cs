using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using PrimalFrontier.AI;
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
    /// Small map in the top-right corner (mobile HUD rule): the baked island picture scrolled around the player
    /// (north up), the player arrow, and markers for what the player knows: camp fires and shelters, water, the wreck,
    /// discovered places (cave...), known resource areas, and creatures only while they are spotted (close and in
    /// view). No second camera renders at run time. Tap it (or press M) for the whole island.
    /// Layout lives in the scene ([UI]/[HUD]/Minimap); markers are pooled.
    /// </summary>
    public class Minimap : MonoBehaviour, IBakeableUI
    {
        public MinimapData data;
        [Tooltip("metres across the small map")] public float viewSize = 140f;
        public float spotRange = 45f;
        public Color campColor = new Color(1f, 0.62f, 0.25f), waterColor = new Color(0.35f, 0.7f, 1f), placeColor = new Color(0.95f, 0.9f, 0.75f),
                     resourceColor = new Color(0.6f, 0.85f, 0.45f), dangerColor = new Color(0.95f, 0.25f, 0.2f), creatureColor = new Color(0.95f, 0.85f, 0.35f);

        RectTransform _root, _mapRect, _markers, _arrow; RawImage _map; Text _north, _label; CanvasGroup _group;
        readonly List<Image> _pool = new List<Image>(); int _used;
        bool _big; float _bigK;
        Vector2 _smallSize = new Vector2(230, 230), _smallPos = new Vector2(-24, -24);
        readonly Dictionary<string, Transform> _places = new Dictionary<string, Transform>();
        public bool Big => _big;

        void Awake()
        {
            if (!data) data = Resources.Load<MinimapData>("MinimapData");
            Build();
            foreach (var n in new[] { "ZONE_Shipwreck", "ZONE_Cave", "ZONE_Pond", "ZONE_Meadow", "RESAREA_Driftwood", "RESAREA_Stone", "RESAREA_Fiber", "RESAREA_Berries", "RESAREA_StreamStone" })
            { var g = GameObject.Find(n); if (g) _places[n] = g.transform; }
        }

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
                // the HUD canvas ignores touches; the map is tappable and follows the HUD fade itself
                _group = UIFactory.Group(frame.gameObject); _group.ignoreParentGroups = true; _group.interactable = true; _group.blocksRaycasts = true;
                var mask = UIFactory.Stretch(_root, "Mask", 8f); mask.gameObject.GetOrAdd<RectMask2D>();
                _mapRect = UIFactory.Stretch(mask, "Map", 0f);
                _map = _mapRect.gameObject.GetOrAdd<RawImage>(); _map.raycastTarget = false;
                if (data && data.map) _map.texture = data.map;
                _markers = UIFactory.Stretch(mask, "Markers", 0f);
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

        public void Toggle() { _big = !_big; Audio.SfxPlayer.Instance.Play2D(Audio.SfxId.UiClick, 0.5f); }

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
            m.gameObject.SetActive(true); m.color = c; m.rectTransform.sizeDelta = new Vector2(size, size);
            m.rectTransform.localRotation = diamond ? Quaternion.Euler(0, 0, 45f) : Quaternion.identity;
            return m;
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
            // big: centred, most of the screen height
            var canvasRt = (RectTransform)_root.parent;
            float bigSide = Mathf.Min(canvasRt.rect.height * 0.8f, canvasRt.rect.width * 0.9f);
            _root.anchorMin = _root.anchorMax = Vector2.Lerp(new Vector2(1, 1), new Vector2(0.5f, 0.5f), k);
            _root.pivot = Vector2.Lerp(new Vector2(1, 1), new Vector2(0.5f, 0.5f), k);
            _root.anchoredPosition = Vector2.Lerp(_smallPos, Vector2.zero, k);
            _root.sizeDelta = Vector2.Lerp(_smallSize, new Vector2(bigSide, bigSide), k);
            _label.text = _big ? "Tap / M to close" : "";

            var player = PlayerLocator.Player;
            if (!player || !data || !data.map) { _map.enabled = false; return; }
            _map.enabled = true;
            float view = Mathf.Lerp(viewSize, data.worldSize, k);
            Vector2 p = new Vector2(player.position.x, player.position.z);
            Vector2 centre = Vector2.Lerp(p, data.worldMin + Vector2.one * data.worldSize * 0.5f, k);
            Vector2 uv0 = (centre - data.worldMin) / data.worldSize - Vector2.one * (view / data.worldSize) * 0.5f;
            _map.uvRect = new Rect(uv0, Vector2.one * (view / data.worldSize));
            float side = _mapRect.rect.width;
            Vector2 ToUi(Vector3 w) => (new Vector2(w.x, w.z) - centre) / view * side;
            bool Inside(Vector2 ui) => Mathf.Abs(ui.x) < side * 0.5f && Mathf.Abs(ui.y) < side * 0.5f;

            _arrow.anchoredPosition = ToUi(player.position);
            _arrow.localRotation = Quaternion.Euler(0, 0, -player.eulerAngles.y + 45f);     // crosshair icon drawn as a diamond pointer

            _used = 0;
            float ms = Mathf.Lerp(9f, 14f, k);
            foreach (var c in Campfire.All) if (c) Put(c.transform.position, campColor, ms + 2, true);
            foreach (var s in Shelter.All) if (s) Put(s.transform.position, campColor, ms, false);
            foreach (var w in WaterSource.All) if (w && w.fresh) Put(w.FocusPoint, waterColor, ms, false);
            var zm = ZoneManager.Instance;
            var visited = zm ? new HashSet<string>(zm.Visited) : new HashSet<string>();
            foreach (var kv in _places)
            {
                if (!kv.Value) continue;
                bool known = kv.Key == "ZONE_Shipwreck" || visited.Contains(kv.Key) || (kv.Key.StartsWith("RESAREA") && (kv.Value.position - player.position).sqrMagnitude < 50f * 50f);
                if (!known) continue;
                Put(kv.Value.position, kv.Key.StartsWith("RESAREA") ? resourceColor : kv.Key == "ZONE_Pond" ? waterColor : placeColor, ms, kv.Key.StartsWith("ZONE"));
            }
            // creatures: only while spotted (close and roughly in front of the camera)
            var cam = Camera.main;
            foreach (var d in DinosaurController.All)
            {
                if (!d || !d.IsAlive) continue;
                Vector3 v = d.transform.position - player.position;
                if (v.sqrMagnitude > spotRange * spotRange) continue;
                if (cam && Vector3.Dot(cam.transform.forward, v.normalized) < 0.2f && v.sqrMagnitude > 12f * 12f) continue;
                bool danger = d.def && (d.def.temperament == Temperament.Predator || d.def.temperament == Temperament.Territorial);
                Put(d.transform.position, danger ? dangerColor : creatureColor, ms - 1, false);
            }
            // tutorial objective
            var tut = Story.TutorialManager.Instance;
            var target = tut ? tut.CurrentTarget : null;
            if (target.HasValue) PutClamped(target.Value, UIStyle.Accent, ms + 3);
            for (int i = _used; i < _pool.Count; i++) if (_pool[i].gameObject.activeSelf) _pool[i].gameObject.SetActive(false);

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
    }
}
