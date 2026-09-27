using UnityEngine;
using UnityEngine.UI;

namespace PrimalFrontier.UI
{
    /// <summary>
    /// Readable, non-intrusive hit feedback: a soft red edge vignette (never covers the centre) and a short directional
    /// arc pointing at the attacker. Built in code on its own overlay canvas.
    /// </summary>
    public class DamageOverlay : MonoBehaviour
    {
        static DamageOverlay _inst;
        public static DamageOverlay Instance
        {
            get
            {
                if (_inst == null) { var go = new GameObject("[DamageOverlay]"); _inst = go.AddComponent<DamageOverlay>(); _inst.Build(); }
                return _inst;
            }
        }

        Image _vignette; RectTransform _arcPivot; Image _arc; Image _heal;
        float _vA, _arcA, _healA; float _arcAngle;

        void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 50;
            gameObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _vignette = MakeImage("Vignette", VignetteSprite(), transform); Stretch(_vignette.rectTransform);
            _vignette.color = new Color(0.55f, 0f, 0f, 0f); _vignette.raycastTarget = false;
            _heal = MakeImage("Heal", VignetteSprite(), transform); Stretch(_heal.rectTransform);
            _heal.color = new Color(0.45f, 0.75f, 0.35f, 0f); _heal.raycastTarget = false;
            var piv = new GameObject("ArcPivot", typeof(RectTransform)); _arcPivot = (RectTransform)piv.transform; _arcPivot.SetParent(transform, false);
            _arcPivot.anchorMin = _arcPivot.anchorMax = new Vector2(0.5f, 0.5f); _arcPivot.sizeDelta = Vector2.zero;
            _arc = MakeImage("Arc", ArcSprite(), _arcPivot); _arc.rectTransform.sizeDelta = new Vector2(220, 60); _arc.rectTransform.anchoredPosition = new Vector2(0, 190);
            _arc.color = new Color(0.75f, 0.08f, 0.05f, 0f); _arc.raycastTarget = false;
        }

        static Image MakeImage(string n, Sprite s, Transform parent)
        {
            var g = new GameObject(n, typeof(RectTransform), typeof(Image)); g.transform.SetParent(parent, false);
            var i = g.GetComponent<Image>(); i.sprite = s; return i;
        }
        static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

        static Sprite VignetteSprite()
        {
            const int N = 128; var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2 - 1, v = (y + 0.5f) / N * 2 - 1;
                    float r = Mathf.Sqrt(u * u * 0.8f + v * v); float a = Mathf.Clamp01((r - 0.72f) / 0.5f); t.SetPixel(x, y, new Color(1, 1, 1, a * a));
                }
            t.Apply(); return Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f));
        }

        static Sprite ArcSprite()
        {
            const int W = 128, H = 32; var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W * 2 - 1, v = (y + 0.5f) / H;
                    float curve = 0.55f - 0.45f * u * u; float d = Mathf.Abs(v - curve);
                    float a = Mathf.Clamp01(1 - d / 0.18f) * Mathf.Clamp01(1 - Mathf.Abs(u) * 1.05f) * 1.2f; t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(a)));
                }
            t.Apply(); return Sprite.Create(t, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f));
        }

        /// <summary>worldDir = direction from the player to the attacker (y ignored)</summary>
        public void ShowHit(Vector3 worldDir, bool heavy)
        {
            _vA = Mathf.Max(_vA, heavy ? 0.55f : 0.32f);
            var cam = Camera.main;
            if (cam && worldDir.sqrMagnitude > 0.01f)
            {
                var f = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up); var d = Vector3.ProjectOnPlane(worldDir, Vector3.up);
                _arcAngle = -Vector3.SignedAngle(f, d, Vector3.up); _arcA = 1f;
            }
        }

        public void ShowHeal() { _healA = 0.25f; }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _vA = Mathf.MoveTowards(_vA, 0f, dt * 0.9f); _arcA = Mathf.MoveTowards(_arcA, 0f, dt * 1.1f); _healA = Mathf.MoveTowards(_healA, 0f, dt * 0.5f);
            var c = _vignette.color; c.a = _vA; _vignette.color = c;
            var h = _heal.color; h.a = _healA; _heal.color = h;
            var a = _arc.color; a.a = Mathf.Clamp01(_arcA * 1.2f) * 0.85f; _arc.color = a;
            _arcPivot.localRotation = Quaternion.Euler(0, 0, _arcAngle);
        }
    }
}
