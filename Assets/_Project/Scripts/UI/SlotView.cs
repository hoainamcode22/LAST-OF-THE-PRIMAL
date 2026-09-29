using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using PrimalFrontier.Items;

namespace PrimalFrontier.UI
{
    /// <summary>One inventory slot in the UI: icon, count (water in ml), durability, spoilage tint; click, double click, shift-click, drag & drop.</summary>
    public class SlotView : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public InventorySystem inventory; public int index;
        public Image bg, icon, dur; public Text count, key;
        public Action<SlotView> Clicked, DoubleClicked, ShiftClicked, RightClicked, Hovered, Unhovered;
        public static Action<SlotView, SlotView> Dropped;      // from, to
        static SlotView _dragFrom; static Image _dragIcon;
        float _lastClick;
        public bool Selected { get; set; }
        public bool Highlight { get; set; }

        public ItemStack Stack => inventory ? inventory.Get(index) : null;

        public static SlotView Create(Transform parent, InventorySystem inv, int index, Vector2 pos, float size, string keyLabel = null)
        {
            var bgImg = UIFactory.Image(parent, "Slot" + index, UIStyle.Slot, Color.white, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), pos, new Vector2(size, size), true);
            var v = bgImg.gameObject.GetOrAdd<SlotView>();
            v.inventory = inv; v.index = index; v.bg = bgImg;
            v.icon = UIFactory.Image(bgImg.transform, "Icon", null, Color.white, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            if (UIFactory.Fresh(v.icon)) { v.icon.rectTransform.offsetMin = new Vector2(7, 7); v.icon.rectTransform.offsetMax = new Vector2(-7, -7); }
            v.icon.preserveAspect = true;
            v.count = UIFactory.Label(bgImg.transform, "Count", "", 16, UIStyle.Text, TextAnchor.LowerRight, UIStyle.Body);
            if (UIFactory.Fresh(v.count)) { v.count.rectTransform.offsetMin = new Vector2(0, 3); v.count.rectTransform.offsetMax = new Vector2(-6, 0); }
            v.dur = UIFactory.Image(bgImg.transform, "Dur", UIStyle.BarFill, UIStyle.Good, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(7, 4), new Vector2(-14, 4));
            v.dur.type = Image.Type.Filled; v.dur.fillMethod = Image.FillMethod.Horizontal;
            if (keyLabel != null) v.key = UIFactory.Label(bgImg.transform, "Key", keyLabel, 13, UIStyle.TextDim, TextAnchor.UpperLeft, UIStyle.Body, Vector2.zero, Vector2.one, new Vector2(0, 1), new Vector2(5, -2), Vector2.zero);
            v.Refresh();
            return v;
        }

        public void Refresh()
        {
            var s = Stack;
            icon.enabled = s != null && s.item.icon; if (s != null) icon.sprite = s.item.icon;
            count.text = HUDManager.SlotCountText(s);
            count.color = HUDManager.WaterCountColor(s);
            icon.color = s != null ? Survival.Spoilage.Tint(Survival.Spoilage.Stage(s)) : Color.white;     // aging / spoiled food is tinted
            bool d = s != null && s.item.HasDurability; dur.enabled = d;
            if (d) { float k = Mathf.Clamp01(s.durability / s.item.maxDurability); dur.fillAmount = k; dur.color = Color.Lerp(UIStyle.Bad, UIStyle.Good, k); }
            bg.sprite = Selected || Highlight ? UIStyle.SlotActive : UIStyle.Slot;
            bg.color = Selected ? new Color(1f, 0.92f, 0.75f) : Color.white;
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Right) { RightClicked?.Invoke(this); return; }
            if (e.button != PointerEventData.InputButton.Left) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.shiftKey.isPressed) { ShiftClicked?.Invoke(this); return; }
            if (Time.unscaledTime - _lastClick < 0.3f) { DoubleClicked?.Invoke(this); _lastClick = 0; return; }
            _lastClick = Time.unscaledTime;
            Clicked?.Invoke(this);
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (Stack == null) return;
            _dragFrom = this;
            var canvas = GetComponentInParent<Canvas>().rootCanvas;
            _dragIcon = UIFactory.Image(canvas.transform, "DragIcon", icon.sprite, new Color(1, 1, 1, 0.85f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(64, 64));
            _dragIcon.preserveAspect = true; _dragIcon.raycastTarget = false;
            OnDrag(e);
        }
        public void OnDrag(PointerEventData e)
        {
            if (_dragIcon == null) return;
            var canvasRt = (RectTransform)_dragIcon.canvas.transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, e.position, null, out var lp);
            _dragIcon.rectTransform.anchoredPosition = lp;
        }
        public void OnEndDrag(PointerEventData e) { if (_dragIcon) Destroy(_dragIcon.gameObject); _dragIcon = null; _dragFrom = null; }
        public void OnDrop(PointerEventData e) { if (_dragFrom != null && _dragFrom != this) Dropped?.Invoke(_dragFrom, this); }
        public void OnPointerEnter(PointerEventData e) => Hovered?.Invoke(this);
        public void OnPointerExit(PointerEventData e) => Unhovered?.Invoke(this);
        public static bool Dragging => _dragFrom != null;
        public static SlotView DragSource => _dragFrom;
    }
}
