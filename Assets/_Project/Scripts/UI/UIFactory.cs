using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PrimalFrontier.UI
{
    /// <summary>Small helpers to build uGUI in code (no prefabs to break).</summary>
    public static class UIFactory
    {
        public static Canvas Canvas(string name, int order)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var c = go.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = order;
            var sc = go.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return c;
        }

        public static RectTransform Rect(Transform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.layer = 5;
            var rt = (RectTransform)go.transform; rt.SetParent(parent, false);
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot; rt.anchoredPosition = pos; rt.sizeDelta = size;
            return rt;
        }
        public static RectTransform Stretch(Transform parent, string name, float inset = 0f)
        {
            var rt = Rect(parent, name, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset); return rt;
        }

        public static Image Image(Transform parent, string name, Sprite sprite, Color color, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size, bool raycast = false)
        {
            var rt = Rect(parent, name, aMin, aMax, pivot, pos, size);
            var img = rt.gameObject.AddComponent<Image>(); img.sprite = sprite; img.color = color; img.raycastTarget = raycast;
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
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font ? font : UIStyle.Body; t.text = text; t.fontSize = size; t.color = color; t.alignment = align; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow; t.supportRichText = true;
            if (shadow) { var s = rt.gameObject.AddComponent<Shadow>(); s.effectColor = new Color(0, 0, 0, 0.65f); s.effectDistance = new Vector2(1.5f, -1.5f); }
            return t;
        }

        public static Button Button(Transform parent, string name, string label, UnityAction onClick, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size, int fontSize = 24)
        {
            var img = Image(parent, name, UIStyle.Button, Color.white, aMin, aMax, pivot, pos, size, true);
            var b = img.gameObject.AddComponent<Button>();
            var cb = b.colors; cb.normalColor = new Color(0.85f, 0.8f, 0.72f); cb.highlightedColor = Color.white; cb.pressedColor = new Color(0.7f, 0.6f, 0.45f);
            cb.disabledColor = new Color(0.45f, 0.42f, 0.38f, 0.8f); cb.selectedColor = new Color(0.95f, 0.9f, 0.8f); b.colors = cb;
            if (onClick != null) b.onClick.AddListener(onClick);
            b.onClick.AddListener(() => Audio.SfxPlayer.Instance.Play2D(Audio.SfxId.UiClick, 0.6f));
            var t = Label(img.transform, "Label", label, fontSize, UIStyle.Text, TextAnchor.MiddleCenter, UIStyle.Head);
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
            var s = root.gameObject.AddComponent<Slider>();
            s.fillRect = fill.rectTransform; s.handleRect = handle.rectTransform; s.targetGraphic = handle; s.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            s.minValue = min; s.maxValue = max; s.value = value;
            bg.raycastTarget = true;
            if (onChange != null) s.onValueChanged.AddListener(onChange);
            return s;
        }

        /// <summary>fill bar (value 0..1) made of a back sprite and a horizontally filled image</summary>
        public static Image Bar(Transform parent, string name, Color color, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var back = Image(parent, name, UIStyle.BarBack, new Color(1, 1, 1, 0.85f), aMin, aMax, pivot, pos, size);
            var fillRt = Stretch(back.transform, "Fill", 3f);
            var f = fillRt.gameObject.AddComponent<Image>(); f.sprite = UIStyle.BarFill; f.color = color; f.raycastTarget = false;
            f.type = UnityEngine.UI.Image.Type.Filled; f.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal; f.fillAmount = 1f;
            return f;
        }

        public static void AddHover(GameObject go, Action enter, Action exit)
        {
            var et = go.GetOrAdd<EventTrigger>();
            var e1 = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter }; e1.callback.AddListener(_ => enter?.Invoke()); et.triggers.Add(e1);
            var e2 = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit }; e2.callback.AddListener(_ => exit?.Invoke()); et.triggers.Add(e2);
        }

        public static CanvasGroup Group(GameObject go) { var g = go.GetOrAdd<CanvasGroup>(); return g; }
    }
}
