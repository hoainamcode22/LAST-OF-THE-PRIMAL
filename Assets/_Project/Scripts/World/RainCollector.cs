using System;
using System.Globalization;
using System.Text;
using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Building;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.Survival;

namespace PrimalFrontier.World
{
    /// <summary>
    /// Placed rain collector (hide funnel over a basin). Once a second it checks WeatherManager.RainingAt at the basin
    /// (a shelter / tent over it or a cave keeps it dry) and gains SurvivalConfig.collectorChargesPerRainHour per in-game
    /// hour of rain, scaled by rain intensity (normal rain 0.75 = x1, storm more), up to collectorCapacity. The water is
    /// always CleanWater. E: fill the held container (WaterRules.CanFill: empty or already clean) from the whole charges,
    /// or drink one charge. The child "Water" (surface) rises with the amount. Saved through ISaveableStructure.
    /// </summary>
    public class RainCollector : Interactable, ISaveableStructure
    {
        [Tooltip("water surface inside the basin; authored at the full level (found by the name \"Water\" when empty)")]
        public Transform waterSurface;
        [Tooltip("how far below its authored (full) height the surface sits when the basin is almost empty, m")] public float emptyDepth = 0.2f;
        [Tooltip("the surface is this much narrower when almost empty (a basin that widens upward)")] [Range(0.3f, 1f)] public float emptyWidth = 0.8f;
        [Tooltip("height of the funnel opening above the pivot, m (the rain check point)")] public float basinHeight = 0.8f;

        /// <summary>rain intensity that gives the configured rate (WeatherManager: rain 0.75, storm 1)</summary>
        public const float ReferenceIntensity = 0.75f;
        const float TickSeconds = 1f;
        const WaterType Kind = WaterType.CleanWater;

        /// <summary>stored water in charges (fractional while it fills)</summary>
        public float Water { get; private set; }
        /// <summary>whole drinks ready to take</summary>
        public int Charges => Mathf.FloorToInt(Water + 1e-4f);
        public int Capacity => Mathf.Max(1, C.collectorCapacity);
        public bool IsFull => Water >= Capacity - 1e-4f;
        public override float Radius => 0.5f;

        static SurvivalConfig C => SurvivalConfig.Instance;
        float _tick; Vector3 _fullPos, _fullScale; bool _surfaceCached;

        void Awake()
        {
            if (!waterSurface) waterSurface = FindDeep(transform, "Water");
            UpdateSurface();
        }

        static Transform FindDeep(Transform t, string name)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (c.name == name) return c;
                var d = FindDeep(c, name); if (d) return d;
            }
            return null;
        }

        void Update()
        {
            _tick += Time.deltaTime;
            if (_tick < TickSeconds) return;
            float seconds = _tick; _tick = 0f;
            try { Collect(seconds); }
            catch (Exception e) { Debug.LogException(e, this); }
        }

        /// <summary>one collection step over <paramref name="seconds"/> of real time (only while it rains on the basin)</summary>
        public void Collect(float seconds)
        {
            if (IsFull || seconds <= 0f) return;
            var wm = WeatherManager.Instance;
            if (!wm || !wm.RainingAt(transform.position + Vector3.up * basinHeight)) return;
            float perHour = Mathf.Max(0f, C.collectorChargesPerRainHour) * Mathf.Clamp(wm.Intensity / ReferenceIntensity, 0f, 2f);
            float add = perHour * seconds / Mathf.Max(1f, GameClock.SecondsPerHour);
            if (add <= 0f) return;
            int before = Charges;
            Water = Mathf.Min(Capacity, Water + add);
            int gained = Charges - before;
            if (gained > 0) { GameEvents.Raise(GameEventType.WaterCollected, C.Water(Kind).eventId, gained, transform.position); InvalidatePrompt(); }
            UpdateSurface();
        }

        /// <summary>tools / tests: set the stored water (charges, clamped to the capacity)</summary>
        public void SetWater(float charges)
        {
            Water = Mathf.Clamp(charges, 0f, Capacity);
            UpdateSurface(); InvalidatePrompt();
        }

        /// <summary>pour whole charges into a container (empty or already clean water); false when nothing moved</summary>
        public bool TryFill(ItemStack container)
        {
            if (Charges < 1 || !WaterRules.CanFill(container, Kind)) return false;
            int added = WaterRules.Fill(container, Kind, Charges);
            if (added <= 0) return false;
            Water = Mathf.Max(0f, Water - added);
            UpdateSurface(); InvalidatePrompt();
            return true;
        }

        /// <summary>drink one charge straight from the basin</summary>
        public bool TryDrink(PlayerSurvival who)
        {
            if (Charges < 1 || !who) return false;
            Water = Mathf.Max(0f, Water - 1f);
            WaterRules.ApplyDrink(who, Kind);
            UpdateSurface(); InvalidatePrompt();
            return true;
        }

        void UpdateSurface()
        {
            if (!waterSurface) return;
            if (!_surfaceCached) { _fullPos = waterSurface.localPosition; _fullScale = waterSurface.localScale; _surfaceCached = true; }
            float f = Mathf.Clamp01(Water / Capacity);
            bool show = f > 0.01f;
            if (waterSurface.gameObject.activeSelf != show) waterSurface.gameObject.SetActive(show);
            if (!show) return;
            waterSurface.localPosition = _fullPos - Vector3.up * (emptyDepth * (1f - f));
            float w = Mathf.Lerp(emptyWidth, 1f, f);
            waterSurface.localScale = new Vector3(_fullScale.x * w, _fullScale.y, _fullScale.z * w);
        }

        // ------------------------------------------------------------------ interaction
        enum Act { Empty, Fill, Drink }

        Act Decide(PlayerInteraction p)
        {
            if (Charges < 1) return Act.Empty;
            var st = p ? p.ActiveStack : null;
            if (WaterRules.IsContainer(st) && WaterRules.CanFill(st, Kind)) return Act.Fill;
            return Act.Drink;
        }

        public override bool CanInteract(PlayerInteraction p)
        {
            var a = Decide(p);
            return a == Act.Fill || (a == Act.Drink && p && p.Survival);
        }

        public override void Interact(PlayerInteraction p)
        {
            if (!p) return;
            var act = Decide(p);
            if (act == Act.Fill)
            {
                int slot = p.Inventory.ActiveSlot; var inv = p.Inventory;
                p.DoOneShot(PlayerActions.Interact, "OnInteract", () =>
                {
                    var s = inv.Get(slot);
                    if (TryFill(s)) { inv.ForceNotify(); PlayerInteraction.Notify(s.item.displayName + " filled with clean rain water."); }
                }, FocusPoint, 1.2f, this);
            }
            else if (act == Act.Drink)
            {
                p.DoOneShot(PlayerActions.Drink, "OnDrink", () => TryDrink(p.Survival), FocusPoint, 2.0f, this);
            }
        }

        // prompt cached: rebuilt only when the amount / action / held item changes
        Act _pAct = (Act)(-1); int _pCharges = -1, _pCap = -1; ItemDefinition _pItem; WaterType _pHeld = (WaterType)(-1); bool _pFull;
        string _pText, _pSub;
        void InvalidatePrompt() { _pCharges = -1; }

        public override string GetPrompt(PlayerInteraction p, out string sub)
        {
            var act = Decide(p);
            var st = p ? p.ActiveStack : null;
            var held = st != null && WaterRules.IsContainer(st) ? st.item : null;
            var heldType = held ? WaterRules.TypeOf(st) : WaterType.None;
            bool heldFull = held && WaterRules.IsFull(st);
            int ch = Charges, cap = Capacity;
            if (act != _pAct || ch != _pCharges || cap != _pCap || held != _pItem || heldType != _pHeld || heldFull != _pFull)
            {
                _pAct = act; _pCharges = ch; _pCap = cap; _pItem = held; _pHeld = heldType; _pFull = heldFull;
                var amount = new StringBuilder(48).Append("Rain collector: ").Append(ch.ToString(CultureInfo.InvariantCulture)).Append('/')
                    .Append(cap.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(WaterRules.Label(Kind)).ToString();
                switch (act)
                {
                    case Act.Fill: _pText = "Fill " + held.displayName; _pSub = amount; break;
                    case Act.Drink:
                        _pText = "Drink " + WaterRules.Label(Kind);
                        _pSub = held == null ? amount
                              : heldFull ? amount + "   (" + held.displayName + " is full)"
                              : amount + "   (" + held.displayName + " holds " + WaterRules.Label(heldType) + ": empty it first)";
                        break;
                    default: _pText = amount; _pSub = "Collects clean water while it rains"; break;
                }
            }
            sub = _pSub;
            return _pText;
        }

        // ------------------------------------------------------------------ save
        [Serializable] class State { public float water; }
        public string CaptureState() => Water > 0f ? JsonUtility.ToJson(new State { water = Water }) : null;
        public void RestoreState(string state)
        {
            if (string.IsNullOrEmpty(state)) return;
            try { var s = JsonUtility.FromJson<State>(state); if (s != null) SetWater(s.water); }
            catch (Exception e) { Debug.LogWarning("[RainCollector] unreadable state: " + e.Message, this); }
        }
    }
}
