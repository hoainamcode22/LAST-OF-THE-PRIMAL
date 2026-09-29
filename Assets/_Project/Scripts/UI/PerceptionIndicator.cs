using UnityEngine;
using UnityEngine.UI;
using PrimalFrontier.AI;
using PrimalFrontier.Core;
using PrimalFrontier.Player;

namespace PrimalFrontier.UI
{
    /// <summary>
    /// Subtle stealth feedback (perception): an eye next to the vitals whose tone shows how visible the player is
    /// (hidden / low / visible / exposed, from PlayerSignature for a reference observer), a ring that pulses with the
    /// player's noises, and up to three pooled edge arcs pointing at creatures that are suspicious or worse (white ->
    /// amber -> red). Settings > Stealth hint: AUTO shows it while crouched, near a predator (60 m) or when something
    /// noticed the player; ALWAYS; OFF. Also queues the perception onboarding tips through ContextHints (bilingual,
    /// once each). Own canvas "[Perception]" (order 11). State sampled 10 times a second; per frame only fades.
    /// </summary>
    public class PerceptionIndicator : MonoBehaviour, IBakeableUI
    {
        public const string TipStealth = "stealth", TipNoise = "noise", TipTorch = "torch_stealth", TipBush = "bush", TipScent = "scent", TipWind = "wind";
        public const int MaxMarkers = 3;
        [Tooltip("predators this close count as near (AUTO mode, tips)")] public float nearPredator = 60f;
        public float markerRadius = 190f;

        Canvas _canvas; CanvasGroup _group; RectTransform _root, _markers; Image _eye, _pupil, _ring; CanvasGroup _ringGroup;
        readonly Image[] _marker = new Image[MaxMarkers]; readonly float[] _markerAngle = new float[MaxMarkers], _markerAlpha = new float[MaxMarkers];
        readonly Color[] _markerColor = new Color[MaxMarkers];
        float _next, _show, _vis, _ringT; float _lastNoiseTime = -99f; int _visTier = -1;
        static Sprite _eyeSprite, _ringSprite, _arcSprite;
        static readonly Color Hidden = new Color(0.55f, 0.6f, 0.55f, 0.45f), Low = new Color(0.75f, 0.78f, 0.7f, 0.8f), Seen = new Color(0.93f, 0.89f, 0.8f, 1f), Exposed = new Color(0.95f, 0.62f, 0.28f, 1f);
        string _accent;

        /// <summary>0 hidden, 1 low, 2 visible, 3 exposed (tests)</summary>
        public int VisibilityTier => _visTier;
        public bool Shown => _show > 0.5f;
        public int ActiveMarkers { get; private set; }

        void OnEnable() { GameEvents.Raised -= OnEvent; GameEvents.Raised += OnEvent; }
        void OnDisable() { GameEvents.Raised -= OnEvent; }

        void Start() { if (!_canvas) Build(); }

        public void BakeLayout() { Build(); if (_group) _group.alpha = 1f; }

        void Build()
        {
            UIFactory.BeginBuild();
            try { BuildLayout(); } finally { UIFactory.EndBuild(); }
        }

        void BuildLayout()
        {
            MakeSprites();
            _accent = "#" + ColorUtility.ToHtmlStringRGB(UIStyle.Accent);
            _canvas = UIFactory.Canvas("[Perception]", 11, transform);
            _group = UIFactory.Group(_canvas.gameObject); _group.interactable = false; _group.blocksRaycasts = false; _group.alpha = 0f;
            var gr = _canvas.GetComponent<GraphicRaycaster>(); if (gr) gr.enabled = false;
            var tl = new Vector2(0, 1);
            _root = UIFactory.Rect(_canvas.transform, "Presence", tl, tl, new Vector2(0.5f, 0.5f), new Vector2(400, -70), new Vector2(96, 96));
            _ring = UIFactory.Image(_root, "NoiseRing", _ringSprite, new Color(1f, 1f, 1f, 0.6f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(64, 64));
            _ringGroup = UIFactory.Group(_ring.gameObject); _ringGroup.alpha = 0f;
            _eye = UIFactory.Image(_root, "Eye", _eyeSprite, Seen, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56, 32));
            _pupil = UIFactory.Image(_eye.transform, "Pupil", _ringSprite, new Color(0.1f, 0.08f, 0.06f, 0.9f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(16, 16));
            var mid = new Vector2(0.5f, 0.5f);
            _markers = UIFactory.Rect(_canvas.transform, "Threats", mid, mid, mid, Vector2.zero, new Vector2(10, 10));
            for (int i = 0; i < MaxMarkers; i++)
            {
                _marker[i] = UIFactory.Image(_markers, "Threat" + i, _arcSprite, new Color(1, 1, 1, 0), mid, mid, mid, Vector2.zero, new Vector2(84, 28));
                _marker[i].raycastTarget = false;
            }
        }

        /// <summary>procedural shapes (no texture assets): almond eye, soft ring, arc</summary>
        static void MakeSprites()
        {
            if (_eyeSprite && _ringSprite && _arcSprite) return;
            _eyeSprite = Shape(64, 36, (x, y) => { float u = x * 2f - 1f, v = y * 2f - 1f; float lid = 1f - u * u; return Mathf.Clamp01((lid * 0.95f - Mathf.Abs(v)) * 12f); });
            _ringSprite = Shape(64, 64, (x, y) => { float u = x * 2f - 1f, v = y * 2f - 1f, r = Mathf.Sqrt(u * u + v * v); return Mathf.Clamp01((1f - Mathf.Abs(r - 0.82f) / 0.12f)) * (r < 1f ? 1f : 0f); });
            _arcSprite = Shape(96, 32, (x, y) => { float u = x * 2f - 1f, v = y; float curve = 0.55f + 0.35f * (1f - u * u); float d = Mathf.Abs(v - curve); return Mathf.Clamp01(1f - d / 0.14f) * Mathf.Clamp01((1f - Mathf.Abs(u)) * 4f); });
            if (_ringSprite) _ringSprite.name = "perception_ring";
        }

        static Sprite Shape(int w, int h, System.Func<float, float, float> alpha)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha((x + 0.5f) / w, (y + 0.5f) / h)) * 255f));
            tex.SetPixels32(px); tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        }

        // ------------------------------------------------------------------ tick
        void Update()
        {
            if (!_canvas) return;
            float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            if (now >= _next) { _next = now + 0.1f; Sample(); }
            _group.alpha = Mathf.MoveTowards(_group.alpha, _show, dt * 3f);
            // noise ring: grows and fades after each player noise
            float t = Stimuli.Now - Stimuli.LastPlayerNoiseTime;
            if (Stimuli.LastPlayerNoiseTime != _lastNoiseTime) { _lastNoiseTime = Stimuli.LastPlayerNoiseTime; _ringT = Mathf.Clamp01(Stimuli.LastPlayerNoise / 1.5f); }
            float k = Mathf.Clamp01(1f - t / 0.7f);
            _ringGroup.alpha = k * Mathf.Lerp(0.25f, 0.9f, _ringT);
            float size = Mathf.Lerp(40f, 96f, _ringT) * Mathf.Lerp(1.15f, 0.85f, k);
            _ring.rectTransform.sizeDelta = new Vector2(size, size);
            for (int i = 0; i < MaxMarkers; i++)
            {
                var m = _marker[i]; if (!m) continue;
                var c = _markerColor[i]; c.a = Mathf.MoveTowards(m.color.a, _markerAlpha[i], dt * 3f); m.color = c;
                float a = _markerAngle[i] * Mathf.Deg2Rad;
                m.rectTransform.anchoredPosition = new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * markerRadius;
                m.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -_markerAngle[i]);
            }
        }

        void Sample()
        {
            var sig = PlayerSignature.Instance;
            var mode = GameSettings.StealthHud;
            var gm = GameManager.Instance;
            bool playing = sig && (!gm || gm.State == GameState.Playing);
            if (!playing || mode == 2) { _show = 0f; ClearMarkers(); return; }
            var snap = PlayerSignature.Current;
            // visibility tier
            float v = sig.Visibility;
            _visTier = v < 0.25f ? 0 : v < 0.5f ? 1 : v < 0.9f ? 2 : 3;
            _eye.color = _visTier == 0 ? Hidden : _visTier == 1 ? Low : _visTier == 2 ? Seen : Exposed;
            _pupil.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(8f, 18f, Mathf.Clamp01(v));
            // threats (nearest suspicious-or-worse creatures) and the nearest predator
            var cam = Camera.main;
            float camYaw = cam ? cam.transform.eulerAngles.y : 0f;
            int n = 0; float nearestPred = float.MaxValue; bool noticed = false; bool smelled = false;
            var all = DinosaurController.All;
            for (int i = 0; i < MaxMarkers; i++) _markerAlpha[i] = 0f;
            float[] bestD = _bestD; for (int i = 0; i < MaxMarkers; i++) bestD[i] = float.MaxValue;
            for (int i = 0; i < all.Count; i++)
            {
                var d = all[i]; if (!d || !d.IsAlive || !d.def) continue;
                Vector3 to = d.transform.position - snap.position; to.y = 0f; float dist = to.magnitude;
                bool pred = d.def.temperament == Temperament.Predator || d.def.temperament == Temperament.Territorial;
                if (pred && dist < nearestPred) nearestPred = dist;
                var lvl = d.Senses.Level;
                if (lvl < AwarenessLevel.Suspicious || dist > 90f) continue;
                noticed = true;
                if (d.Senses.LastSense == SenseKind.Scent) smelled = true;
                // keep the MaxMarkers nearest
                int slot = -1; for (int k = 0; k < MaxMarkers; k++) if (dist < bestD[k] && (slot < 0 || bestD[k] > bestD[slot])) slot = k;
                if (slot < 0) continue;
                bestD[slot] = dist;
                float ang = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg - camYaw;
                _markerAngle[slot] = Mathf.Repeat(ang + 180f, 360f) - 180f;
                _markerColor[slot] = lvl >= AwarenessLevel.Engaged ? UIStyle.Bad : lvl >= AwarenessLevel.Investigating ? UIStyle.Accent : UIStyle.Text;
                _markerAlpha[slot] = lvl >= AwarenessLevel.Engaged ? 0.9f : lvl >= AwarenessLevel.Investigating ? 0.75f : 0.5f;
            }
            n = 0; for (int k = 0; k < MaxMarkers; k++) if (_markerAlpha[k] > 0f) n++;
            ActiveMarkers = n;
            bool auto = snap.crouching || nearestPred < nearPredator || noticed;
            _show = mode == 1 || auto ? 1f : 0f;
            Tips(snap, nearestPred, noticed, smelled);
        }

        readonly float[] _bestD = new float[MaxMarkers];

        void ClearMarkers() { for (int i = 0; i < MaxMarkers; i++) _markerAlpha[i] = 0f; ActiveMarkers = 0; }

        // ------------------------------------------------------------------ tips (ContextHints, once each)
        string Key(string action, string fallback) => "<color=" + _accent + ">[" + (KeyNames.Of(action) ?? fallback) + "]</color>";

        void Tip(string id, string text)
        {
            var h = ContextHints.Instance;
            if (h && h.WantsTip(id)) h.QueueTip(id, text);
        }

        void Tips(in PlayerSignature.Snapshot snap, float nearestPred, bool noticed, bool smelled)
        {
            var h = ContextHints.Instance; if (!h) return;
            bool loud = Stimuli.LastPlayerNoise >= 1.2f && Stimuli.Now - Stimuli.LastPlayerNoiseTime < 1f;
            if (loud && nearestPred < 80f && !OnboardingTips.IsSeen(TipNoise))
                Tip(TipNoise, "Chopping and mining are loud: predators come to check (Chặt cây, đào đá rất ồn: thú săn mồi sẽ tìm đến)");
            var tm = TimeManager.Instance;
            if (snap.torch && tm && tm.NightFactor > 0.5f && nearestPred < 80f && !OnboardingTips.IsSeen(TipTorch))
                Tip(TipTorch, "A torch lights the way and shows you: put it away to hide (Đuốc soi đường nhưng làm bạn lộ diện: cất đuốc để ẩn nấp)");
            if (noticed && snap.inBush && snap.moving && !OnboardingTips.IsSeen(TipBush))
                Tip(TipBush, "Bushes hide you when still, pushing through shakes them (Bụi cây che bạn khi đứng yên, lao qua sẽ làm lá rung)");
            if (smelled && !OnboardingTips.IsSeen(TipWind))
                Tip(TipWind, "They smelled you: approach from downwind (Chúng đánh hơi thấy bạn: hãy tiếp cận từ cuối gió)");
        }

        void OnEvent(GameEvent e)
        {
            if (!_canvas || _accent == null) return;
            if (e.type == GameEventType.PlayerNoticed && !OnboardingTips.IsSeen(TipStealth))
                Tip(TipStealth, "Something noticed you: " + Key("Crouch", "C") + " crouch and keep still to lose it (Có con vật để ý bạn: ngồi xuống và đứng yên)");
            else if (e.type == GameEventType.ScentInvestigated && !OnboardingTips.IsSeen(TipScent))
                Tip(TipScent, "Cooking meat smells and the wind carries it: cook under a shelter, keep the fire burning (Mùi thịt nướng bay theo gió: nấu trong lều, giữ lửa cháy)");
        }
    }
}
