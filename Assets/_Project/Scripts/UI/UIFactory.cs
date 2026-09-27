using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PrimalFrontier.UI
{
    /// <summary>
    /// Small helpers to build uGUI in code, with find-or-create: the screens' Build() code describes the layout and
    /// its defaults, but the result is saved in the scene ([UI] in Island_VerticalSlice, made by
    /// Primal Frontier > Scene > Bake Everything Into Scene) so it can be moved, resized, recoloured and re-texted by hand.
    /// Between BeginBuild() and EndBuild() every call first looks for an unclaimed child with the same name under the
    /// same parent and returns it untouched; only missing pieces are created with the code defaults. Rule for editing
    /// by hand: move / resize / restyle freely, add decorations freely, but do not rename the objects the code uses.
    /// </summary>
    public static class UIFactory
    {
        // ------------------------------------------------------------------ find-or-create
        static int _depth;
        static readonly HashSet<Transform> _claimed = new HashSet<Transform>();
        static readonly HashSet<Transform> _fresh = new HashSet<Transform>();

        /// <summary>inside a screen's Build(): calls reuse the objects already in the scene</summary>
        public static bool Building => _depth > 0;
        public static void BeginBuild() { if (_depth++ == 0) { _claimed.Clear(); _fresh.Clear(); } }
        public static void EndBuild() { if (--_depth <= 0) { _depth = 0; _claimed.Clear(); _fresh.Clear(); } }

        /// <summary>true when the element was created just now (not found in the scene), so code may set its layout.
        /// Always true outside Build() (dynamic content such as notifications or recipe tiles).</summary>
        public static bool Fresh(Component c) => c != null && (!Building || _fresh.Contains(c.transform));

        static RectTransform Find(Transform parent, string name)
        {
            if (_depth == 0 || parent == null) return null;
            for (int i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                if (c.name == name && !_claimed.Contains(c) && c is RectTransform rt) { _claimed.Add(c); return rt; }
            }
            return null;
        }

        static void Created(Transform parent, Transform t)
        {
            if (_depth == 0) return;
            _fresh.Add(t);
            if (parent != null)
            {
                // a piece deleted by hand comes back in the code's order (right after the last piece already found),
                // so it is not drawn on top of everything else
                int last = -1;
                for (int i = 0; i < parent.childCount; i++) { var c = parent.GetChild(i); if (c != t && _claimed.Contains(c)) last = i; }
                if (last + 1 < parent.childCount) t.SetSiblingIndex(last + 1);
            }
            _claimed.Add(t);
        }

        // ------------------------------------------------------------------ elements
        public static Canvas Canvas(string name, int order, Transform parent = null)
        {
            var rt = Find(parent, name);
            if (rt == null)
            {
                var go = new GameObject(name, typeof(RectTransform)); go.layer = 5; rt = (RectTransform)go.transform;
                if (parent) rt.SetParent(parent, false);
                Created(parent, rt);
            }
            var c = rt.GetComponent<Canvas>();
            if (!c) { c = rt.gameObject.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = order; }
            if (!rt.GetComponent<CanvasScaler>())
            {
                var sc = rt.gameObject.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
            }
            if (!rt.GetComponent<GraphicRaycaster>()) rt.gameObject.AddComponent<GraphicRaycaster>();
            return c;
        }

        public static RectTransform Rect(Transform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var ex = Find(parent, name); if (ex) return ex;
            var go = new GameObject(name, typeof(RectTransform)); go.layer = 5;
            var rt = (RectTransform)go.transform; rt.SetParent(parent, false);
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot; rt.anchoredPosition = pos; rt.sizeDelta = size;
            Created(parent, rt);
            return rt;
        }
        public static RectTransform Stretch(Transform parent, string name, float inset = 0f)
        {
            var rt = Rect(parent, name, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            if (Fresh(rt)) { rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset); }
            return rt;
        }

        public static Image Image(Transform parent, string name, Sprite sprite, Color color, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size, bool raycast = false)
        {
            var rt = Rect(parent, name, aMin, aMax, pivot, pos, size);
            var img = rt.GetComponent<Image>();
            if (img)
            {
                if (!img.sprite && sprite) img.sprite = sprite;      // a texture that could not be saved with the scene
                return img;
            }
            img = rt.gameObject.AddComponent<Image>(); img.sprite = sprite; img.color = color; img.raycastTarget = raycast;
            if (sprite && sprite.border.sqrMagnitude > 0) img.type = UnityEngine.UI.Image.Type.Sliced;
            return img;
        }
        public static Image Fill(Transform parent, string name, Sprite sprite, Color color, bool raycast = false)
        {
            var img = Image(parent, name, sprite, color, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, raycast);
            return img;
        }

        public static Text Label(Transform parent, string name, string text, int size, Color color, TextAnchor align, Font font = null,
                                 Vector2? aMin = null, Vector2? aMax = null, Vector2? pivot = null, Vector2? pos = null, Vector2? sz = null, bool shadow = true)
        {
            var rt = Rect(parent, name, aMin ?? Vector2.zero, aMax ?? Vector2.one, pivot ?? new Vector2(0.5f, 0.5f), pos ?? Vector2.zero, sz ?? Vector2.zero);
            var t = rt.GetComponent<Text>();
            if (t) { if (!t.font) t.font = font ? font : UIStyle.Body; return t; }
            t = rt.gameObject.AddComponent<Text>();
            t.font = font ? font : UIStyle.Body; t.text = text; t.fontSize = size; t.color = color; t.alignment = align; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow; t.supportRichText = true;
            if (shadow) { var s = rt.gameObject.AddComponent<Shadow>(); s.effectColor = new Color(0, 0, 0, 0.65f); s.effectDistance = new Vector2(1.5f, -1.5f); }
            return t;
        }

        public static Button Button(Transform parent, string name, string label, UnityAction onClick, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size, int fontSize = 24)
        {
            var img = Image(parent, name, UIStyle.Button, Color.white, aMin, aMax, pivot, pos, size, true);
            var b = img.GetComponent<Button>();
            if (!b)
            {
                b = img.gameObject.AddComponent<Button>();
                var cb = b.colors; cb.normalColor = new Color(0.85f, 0.8f, 0.72f); cb.highlightedColor = Color.white; cb.pressedColor = new Color(0.7f, 0.6f, 0.45f);
                cb.disabledColor = new Color(0.45f, 0.42f, 0.38f, 0.8f); cb.selectedColor = new Color(0.95f, 0.9f, 0.8f); b.colors = cb;
            }
            if (Application.isPlaying)
            {
                if (onClick != null) b.onClick.AddListener(onClick);
                b.onClick.AddListener(() => Audio.SfxPlayer.Instance.Play2D(Audio.SfxId.UiClick, 0.6f));
            }
            Label(img.transform, "Label", label, fontSize, UIStyle.Text, TextAnchor.MiddleCenter, UIStyle.Head);
            return b;
        }

        public static void SetLabel(Button b, string text) { var t = b.GetComponentInChildren<Text>(); if (t) t.text = text; }

        public static Slider Slider(Transform parent, string name, float min, float max, float value, UnityAction<float> onChange, Vector2 pos, Vector2 size)
        {
            var root = Rect(parent, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
            var bg = Fill(root, "Background", UIStyle.BarBack, new Color(1, 1, 1, 0.9f));
            var fillArea = Stretch(root, "Fill Area", 4f);
            var fill = Fill(fillArea, "Fill", UIStyle.BarFill, UIStyle.Accent);
            var handleArea = Stretch(root, "Handle Area", 0f);
            var handle = Image(handleArea, "Handle", UIStyle.Slot, Color.white, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22, 8), true);
            var s = root.GetComponent<Slider>();
            if (!s)
            {
                s = root.gameObject.AddComponent<Slider>();
                s.fillRect = fill.rectTransform; s.handleRect = handle.rectTransform; s.targetGraphic = handle; s.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
                bg.raycastTarget = true;
            }
            s.minValue = min; s.maxValue = max; s.value = value;
            if (onChange != null && Application.isPlaying) s.onValueChanged.AddListener(onChange);
            return s;
        }

        /// <summary>fill bar (value 0..1) made of a back sprite and a horizontally filled image</summary>
        public static Image Bar(Transform parent, string name, Color color, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var back = Image(parent, name, UIStyle.BarBack, new Color(1, 1, 1, 0.85f), aMin, aMax, pivot, pos, size);
            var fillRt = Stretch(back.transform, "Fill", 3f);
            var f = fillRt.GetComponent<Image>();
            if (f) { if (!f.sprite) f.sprite = UIStyle.BarFill; return f; }
            f = fillRt.gameObject.AddComponent<Image>(); f.sprite = UIStyle.BarFill; f.color = color; f.raycastTarget = false;
            f.type = UnityEngine.UI.Image.Type.Filled; f.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal; f.fillAmount = 1f;
            return f;
        }

        public static void AddHover(GameObject go, Action enter, Action exit)
        {
            if (!Application.isPlaying) return;
            var et = go.GetOrAdd<EventTrigger>();
            var e1 = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter }; e1.callback.AddListener(_ => enter?.Invoke()); et.triggers.Add(e1);
            var e2 = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit }; e2.callback.AddListener(_ => exit?.Invoke()); et.triggers.Add(e2);
        }

        public static CanvasGroup Group(GameObject go) { var g = go.GetOrAdd<CanvasGroup>(); return g; }
    }

    /// <summary>a UI screen whose layout can be written into the scene by the editor baker (Build() without playing)</summary>
    public interface IBakeableUI { void BakeLayout(); }
}
