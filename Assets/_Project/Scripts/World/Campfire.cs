using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Building;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.Survival;
using PrimalFrontier.VFX;

namespace PrimalFrontier.World
{
    /// <summary>
    /// Placed campfire, the one cooking / boiling station.
    /// Fuel is item data: any item with fuelSeconds &gt; 0 burns (the legacy fuel item, wood, burns
    /// SurvivalConfig.legacyFuelSeconds when its own value is 0), up to SurvivalConfig.maxFuelSeconds; rain on an
    /// uncovered fire burns it faster. Lighting takes one fuel from the pack.
    /// Cooking slots (SurvivalConfig.cookingSlots): raw food goes Raw (fire out, paused) / Cooking -> Ready -> Burned.
    /// Cook time = raw.cookSeconds; a ready item burns after cooked.burnSeconds, or raw.cookSeconds x
    /// burnAfterCookMultiplier; the burnt result is cooked.burntResult or SurvivalConfig.burntFood (none = lost).
    /// A water container that needs boiling is moved off the pack onto a slot (never a reference into the inventory),
    /// boils for WaterRules.BoilSeconds, then WaterRules.CompleteBoil; it is taken back like food.
    /// E priority: take ready / burnt > cook the held raw food > boil the held container > cook raw food from the pack >
    /// add fuel; a cold fire: take ready / burnt > light > take back raw items. Hold E puts the fire out.
    /// Food sits on the stones as a small copy of its world model (reused per slot), ready food steams, burnt food is
    /// darkened. Slots are saved through ISaveableStructure (lit / fuel stay in StructureData).
    /// </summary>
    public class Campfire : Interactable, ISaveableStructure
    {
        public enum CookState { Empty, Raw, Cooking, Ready, Burned }

        /// <summary>one place on the fire</summary>
        public class CookSlot
        {
            /// <summary>raw food (Raw / Cooking), cooked (Ready), burnt (Burned), or the container being boiled</summary>
            public ItemDefinition item;
            /// <summary>the container stack owned by the fire while it boils / waits (null for food)</summary>
            public ItemStack water;
            public CookState state;
            /// <summary>seconds spent in the current state while the fire burned</summary>
            public float t;
            /// <summary>seconds until the next state (cook, boil, burn); 0 = never</summary>
            public float duration;
            public bool IsEmpty => state == CookState.Empty;
            public bool IsWater => water != null;
            public void Clear() { item = null; water = null; state = CookState.Empty; t = 0f; duration = 0f; }
        }

        public const int MaxSlots = 6;
        public static readonly List<Campfire> All = new List<Campfire>();
        public CampfireFx fx;
        [Tooltip("legacy fuel (wood): burns SurvivalConfig.legacyFuelSeconds when its fuelSeconds is 0. Any item with fuelSeconds > 0 burns too")]
        public ItemDefinition fuelItem;
        public float heatRadius = 5f;
        public float heat = 16f;               // deg C at the fire
        [Tooltip("fuel burns this much faster while rain falls on the fire (no shelter over it)")] public float rainBurnMultiplier = 2f;
        [Header("Food on the fire (visuals)")]
        [Tooltip("distance of the food from the centre (on the stones), m")] public float foodRadius = 0.42f;
        public float foodHeight = 0.12f;
        [Tooltip("largest size of a food model on the fire, m")] public float foodSize = 0.2f;
        [Tooltip("colour of burnt food that has no model of its own")] public Color burntTint = new Color(0.2f, 0.15f, 0.12f, 1f);

        public bool IsLit { get; private set; }
        public float Fuel { get; private set; }
        public float MaxFuel => Mathf.Max(1f, C.maxFuelSeconds);
        /// <summary>places on the fire (SurvivalConfig.cookingSlots)</summary>
        public int SlotCount => Mathf.Clamp(C.cookingSlots, 1, MaxSlots);
        /// <summary>food items on the fire (raw, cooking, ready or burnt; boiling water does not count)</summary>
        public int CookingCount { get { int n = 0; for (int i = 0; i < MaxSlots; i++) if (!_slots[i].IsEmpty && !_slots[i].IsWater) n++; return n; } }
        public int OccupiedCount { get { int n = 0; for (int i = 0; i < MaxSlots; i++) if (!_slots[i].IsEmpty) n++; return n; } }
        /// <summary>a container is heating on the fire</summary>
        public bool Boiling { get { for (int i = 0; i < MaxSlots; i++) { var s = _slots[i]; if (s.IsWater && (s.state == CookState.Raw || s.state == CookState.Cooking)) return true; } return false; } }
        public CookState StateOf(int i) => i >= 0 && i < MaxSlots ? _slots[i].state : CookState.Empty;
        public ItemDefinition ItemOn(int i) => i >= 0 && i < MaxSlots ? _slots[i].item : null;
        /// <summary>the container on slot i (owned by the fire until taken back), null for food</summary>
        public ItemStack WaterOn(int i) => i >= 0 && i < MaxSlots ? _slots[i].water : null;
        /// <summary>0..1 of the current timer (cooking / boiling / time left before burning)</summary>
        public float ProgressOf(int i) { if (i < 0 || i >= MaxSlots) return 0f; var s = _slots[i]; return s.duration > 0f ? Mathf.Clamp01(s.t / s.duration) : 0f; }
        /// <summary>the model shown on slot i (null = nothing / no world model)</summary>
        public GameObject VisualOn(int i) => i >= 0 && i < MaxSlots ? _shown[i] : null;
        public override float Radius => 0.5f;

        static SurvivalConfig C => SurvivalConfig.Instance;
        readonly CookSlot[] _slots = NewSlots();
        static CookSlot[] NewSlots() { var a = new CookSlot[MaxSlots]; for (int i = 0; i < a.Length; i++) a[i] = new CookSlot(); return a; }
        float _rainCheck; bool _wet; PlacedStructure _placed;

        protected override void OnEnable() { base.OnEnable(); if (!All.Contains(this)) All.Add(this); }
        protected override void OnDisable() { base.OnDisable(); All.Remove(this); }
        void Awake() { if (!fx) fx = GetComponentInChildren<CampfireFx>(); }

        public static bool LitNear(Vector3 p, float r)
        {
            foreach (var c in All) if (c && c.IsLit && (c.transform.position - p).sqrMagnitude <= r * r) return true;
            return false;
        }
        public static float HeatAt(Vector3 p)
        {
            float h = 0f;
            foreach (var c in All)
            {
                if (!c || !c.IsLit) continue;
                float d = Vector3.Distance(c.transform.position, p);
                if (d < c.heatRadius) h = Mathf.Max(h, c.heat * (1f - d / c.heatRadius));
            }
            return h;
        }

        // ================================================================== fuel
        /// <summary>seconds of fire one unit of this item gives (0 = not a fuel)</summary>
        public float FuelSecondsOf(ItemDefinition item)
        {
            if (!item) return 0f;
            if (item.fuelSeconds > 0f) return item.fuelSeconds;
            return item == fuelItem || item == C.wood ? Mathf.Max(0f, C.legacyFuelSeconds) : 0f;
        }

        /// <summary>the fuel to use: the held one, else the campfire's own fuel item, else any fuel in the pack</summary>
        public ItemDefinition FindFuel(InventorySystem inv)
        {
            if (inv == null || inv.Slots == null) return null;
            var a = inv.ActiveItem; if (a && FuelSecondsOf(a) > 0f) return a;
            if (fuelItem && FuelSecondsOf(fuelItem) > 0f && inv.Has(fuelItem)) return fuelItem;
            var w = C.wood; if (w && w != fuelItem && FuelSecondsOf(w) > 0f && inv.Has(w)) return w;
            var slots = inv.Slots;
            for (int i = 0; i < slots.Length; i++) { var s = slots[i]; if (!s.IsEmptyOrNull() && FuelSecondsOf(s.item) > 0f) return s.item; }
            return null;
        }

        /// <summary>light a cold fire with one fuel from the pack (the given one, else <see cref="FindFuel"/>)</summary>
        public bool TryLight(InventorySystem inv, ItemDefinition fuel = null)
        {
            if (IsLit || inv == null) return false;
            if (!fuel || !inv.Has(fuel) || FuelSecondsOf(fuel) <= 0f) fuel = FindFuel(inv);
            float fs = FuelSecondsOf(fuel);
            if (fs <= 0f || !inv.Remove(fuel, 1)) return false;
            Fuel = Mathf.Min(MaxFuel, Fuel + fs);
            SetLit(true);
            return true;
        }

        /// <summary>one more fuel on a burning fire that is not full</summary>
        public bool TryAddFuel(InventorySystem inv, ItemDefinition fuel = null)
        {
            if (!IsLit || inv == null || Fuel >= MaxFuel - 1f) return false;
            if (!fuel || !inv.Has(fuel) || FuelSecondsOf(fuel) <= 0f) fuel = FindFuel(inv);
            float fs = FuelSecondsOf(fuel);
            if (fs <= 0f || !inv.Remove(fuel, 1)) return false;
            Fuel = Mathf.Min(MaxFuel, Fuel + fs);
            GameEvents.Raise(GameEventType.FuelAdded, fuel.id, 1, transform.position);
            InvalidatePrompt();
            return true;
        }

        // ================================================================== slots
        static bool IsCookable(ItemDefinition it) => it && it.cookedResult && it.cookedResult != it;

        int FreeSlot() { int n = SlotCount; for (int i = 0; i < n; i++) if (_slots[i].IsEmpty) return i; return -1; }
        public bool HasFreeSlot => FreeSlot() >= 0;

        int FirstDone() { for (int i = 0; i < MaxSlots; i++) { var st = _slots[i].state; if (st == CookState.Ready || st == CookState.Burned) return i; } return -1; }
        int FirstRaw() { for (int i = 0; i < MaxSlots; i++) if (_slots[i].state == CookState.Raw) return i; return -1; }
        /// <summary>what E takes: ready / burnt first; raw items only from a cold fire</summary>
        int FirstTakeable() { int i = FirstDone(); return i >= 0 || IsLit ? i : FirstRaw(); }

        static ItemDefinition FindRaw(InventorySystem inv)
        {
            var slots = inv.Slots; if (slots == null) return null;
            for (int i = 0; i < slots.Length; i++) { var s = slots[i]; if (!s.IsEmptyOrNull() && IsCookable(s.item)) return s.item; }
            return null;
        }

        /// <summary>put one raw food from the pack on a free slot of the burning fire</summary>
        public bool TryCook(InventorySystem inv, ItemDefinition raw)
        {
            if (!IsLit || inv == null || !IsCookable(raw)) return false;
            int i = FreeSlot(); if (i < 0) return false;
            if (!inv.Remove(raw, 1)) return false;
            var s = _slots[i];
            s.Clear(); s.item = raw; s.state = CookState.Cooking; s.duration = Mathf.Max(0.1f, raw.cookSeconds);
            SlotChanged(i);
            return true;
        }

        /// <summary>move the container in inventory slot <paramref name="invSlot"/> onto the fire to boil (only water that needs it)</summary>
        public bool TryBoil(InventorySystem inv, int invSlot)
        {
            if (!IsLit || inv == null) return false;
            var st = inv.Get(invSlot);
            if (!WaterRules.IsContainer(st) || !WaterRules.NeedsBoiling(st)) return false;
            int i = FreeSlot(); if (i < 0) return false;
            float secs = WaterRules.BoilSeconds(st);
            var taken = inv.TakeFromSlot(invSlot, 1);            // a new stack: the fire owns it until it is taken back
            if (taken == null) return false;
            var s = _slots[i];
            s.Clear(); s.item = taken.item; s.water = taken; s.state = CookState.Cooking; s.duration = Mathf.Max(0.1f, secs);
            PlayerInteraction.Notify("The water is heating over the fire.");
            SlotChanged(i);
            return true;
        }

        /// <summary>take the first ready / burnt item (from a cold fire also raw items) into the pack</summary>
        public bool TryTake(InventorySystem inv)
        {
            int i = FirstTakeable();
            if (i < 0 || inv == null) return false;
            var s = _slots[i];
            if (s.IsWater)
            {
                if (!inv.AddStack(s.water)) { PlayerInteraction.Notify(NoRoom(inv, s.item)); return false; }
            }
            else
            {
                if (!s.item || inv.Add(s.item, 1) > 0) { PlayerInteraction.Notify(NoRoom(inv, s.item)); return false; }
                GameEvents.Raise(GameEventType.FoodTaken, s.item.id, 1, transform.position);
            }
            s.Clear();
            SlotChanged(i);
            return true;
        }

        static string NoRoom(InventorySystem inv, ItemDefinition it) =>
            it && inv.SpaceFor(it, true) > 0 ? "Too heavy to carry it. Leave it on the fire for now." : "No room in your pack.";

        void Update()
        {
            try { Tick(Time.deltaTime); }
            catch (Exception e) { Debug.LogException(e, this); }
        }

        void Tick(float dt)
        {
            if (!IsLit) return;
            _rainCheck -= dt;
            if (_rainCheck <= 0f) { _rainCheck = 1f; var wm = WeatherManager.Instance; _wet = wm && wm.RainingAt(transform.position + Vector3.up * 0.5f); }
            Fuel -= dt * (_wet ? Mathf.Max(1f, rainBurnMultiplier) : 1f);
            for (int i = 0; i < MaxSlots; i++)
            {
                var s = _slots[i];
                switch (s.state)
                {
                    case CookState.Raw:                      // relit without SetLit (restore): resume
                        s.state = CookState.Cooking; SlotChanged(i);
                        break;
                    case CookState.Cooking:
                        s.t += dt;
                        if (s.t >= s.duration) FinishCooking(i);
                        break;
                    case CookState.Ready:
                        if (s.IsWater || s.duration <= 0f) break;
                        s.t += dt;
                        if (s.t >= s.duration) Burn(i);
                        break;
                }
            }
            if (Fuel <= 0f) { Fuel = 0f; SetLit(false); PlayerInteraction.Notify("The fire has burned out."); }
        }

        void FinishCooking(int i)
        {
            var s = _slots[i];
            if (s.IsWater)
            {
                WaterRules.CompleteBoil(s.water);          // raises WaterBoiled
                s.state = CookState.Ready; s.t = 0f; s.duration = 0f;
                if (fx) fx.ReadyCue(transform.position);
                PlayerInteraction.Notify("The water has boiled. Take it off the fire.");
            }
            else
            {
                var raw = s.item; var cooked = raw ? raw.cookedResult : null;
                if (cooked)
                {
                    s.item = cooked;
                    s.duration = cooked.burnSeconds > 0f ? cooked.burnSeconds : Mathf.Max(0f, raw.cookSeconds * C.burnAfterCookMultiplier);
                    GameEvents.Raise(GameEventType.FoodCooked, cooked.id, 1, transform.position);
                    PlayerInteraction.Notify(cooked.displayName + " is ready. Take it before it burns.");
                }
                else s.duration = 0f;                        // item data changed since it went on the fire: just done
                s.state = CookState.Ready; s.t = 0f;
                if (fx) fx.ReadyCue(transform.position);
            }
            SlotChanged(i);
        }

        void Burn(int i)
        {
            var s = _slots[i];
            var cooked = s.item;
            var burnt = cooked ? (cooked.burntResult ? cooked.burntResult : C.burntFood) : null;
            GameEvents.Raise(GameEventType.FoodBurned, cooked ? cooked.id : null, 1, transform.position);
            if (fx) fx.BurnPuff(Anchor(i).position);
            if (!burnt)
            {
                PlayerInteraction.Notify((cooked ? cooked.displayName : "The food") + " burned to ash.");
                s.Clear();
            }
            else
            {
                PlayerInteraction.Notify((cooked ? cooked.displayName : "The food") + " burned.");
                s.item = burnt; s.state = CookState.Burned; s.t = 0f; s.duration = 0f;
            }
            SlotChanged(i);
        }

        public void SetLit(bool on, bool burst = true)
        {
            if (IsLit == on) return;
            IsLit = on;
            if (fx) fx.SetLit(on, burst);
            NormalizeSlots();
            GameEvents.Raise(on ? GameEventType.FireLit : GameEventType.FireOut, EventId, 1, transform.position);
            InvalidatePrompt();
        }

        string EventId { get { if (!_placed) _placed = GetComponent<PlacedStructure>(); return _placed ? _placed.itemId : null; } }

        /// <summary>Raw (paused) while the fire is out, Cooking while it burns; refreshes visuals and effects</summary>
        void NormalizeSlots()
        {
            for (int i = 0; i < MaxSlots; i++)
            {
                var s = _slots[i];
                if (IsLit && s.state == CookState.Raw) s.state = CookState.Cooking;
                else if (!IsLit && s.state == CookState.Cooking) s.state = CookState.Raw;
            }
            RefreshAll();
        }

        // ================================================================== interaction
        enum Act { None, Take, CookHeld, BoilHeld, CookPack, AddFuel, Light, NeedFuel, Warm }

        Act Decide(InventorySystem inv, out int index, out ItemDefinition item)
        {
            index = -1; item = null;
            int done = FirstDone();
            if (done >= 0) { index = done; item = _slots[done].item; return Act.Take; }
            bool hasInv = inv != null && inv.Slots != null;
            if (!IsLit)
            {
                item = hasInv ? FindFuel(inv) : null;
                if (item) return Act.Light;
                int raw = FirstRaw();
                if (raw >= 0) { index = raw; item = _slots[raw].item; return Act.Take; }
                return Act.NeedFuel;
            }
            if (!hasInv) return Act.Warm;
            if (FreeSlot() >= 0)
            {
                var st = inv.ActiveStack;
                if (st != null && IsCookable(st.item)) { item = st.item; index = inv.ActiveSlot; return Act.CookHeld; }
                if (st != null && WaterRules.IsContainer(st) && WaterRules.NeedsBoiling(st)) { item = st.item; index = inv.ActiveSlot; return Act.BoilHeld; }
                item = FindRaw(inv);
                if (item) return Act.CookPack;
            }
            if (Fuel < MaxFuel - 1f) { item = FindFuel(inv); if (item) return Act.AddFuel; }
            item = null;
            return Act.Warm;
        }

        public override bool CanInteract(PlayerInteraction p)
        {
            var a = Decide(p ? p.Inventory : null, out _, out _);
            return a != Act.None && a != Act.NeedFuel && a != Act.Warm;
        }

        public override void Interact(PlayerInteraction p)
        {
            if (!p) return;
            var inv = p.Inventory;
            var act = Decide(inv, out int idx, out var item);
            Action run;
            switch (act)
            {
                case Act.Take: run = () => TryTake(inv); break;
                case Act.CookHeld: case Act.CookPack: run = () => TryCook(inv, item); break;
                case Act.BoilHeld: run = () => { var st = inv.Get(idx); if (st != null && st.item == item) TryBoil(inv, idx); }; break;
                case Act.AddFuel: run = () => TryAddFuel(inv, item); break;
                case Act.Light: run = () => TryLight(inv, item); break;
                default: return;
            }
            p.DoOneShot(PlayerActions.Interact, "OnInteract", run, transform.position, 0.8f, this);
        }

        public override string HoldPrompt(PlayerInteraction p) => IsLit ? "Hold to put out" : null;
        public override void HoldInteract(PlayerInteraction p)
        {
            if (!IsLit || !p) return;
            p.DoOneShot(PlayerActions.Interact, "OnInteract", () => { SetLit(false); Fuel = 0f; }, transform.position, 0.8f, this);
        }

        // ------------------------------------------------------------------ prompt (cached: rebuilt only when what it shows changes)
        Act _pAct = (Act)(-1); UnityEngine.Object _pItem; int _pN1 = int.MinValue, _pN2 = int.MinValue;
        string _pText, _pSub;
        static readonly StringBuilder Sb = new StringBuilder(96);
        void InvalidatePrompt() { _pAct = (Act)(-1); }
        int FireSeconds => Mathf.CeilToInt(Mathf.Max(0f, Fuel));

        public override string GetPrompt(PlayerInteraction p, out string sub)
        {
            var inv = p ? p.Inventory : null;
            var act = Decide(inv, out int idx, out var item);
            int n1 = 0, n2 = 0;
            switch (act)
            {
                case Act.Take:
                {
                    var s = _slots[idx]; n1 = (int)s.state;
                    if (s.IsWater) n2 = (int)WaterRules.TypeOf(s.water) * 1000 + s.water.water;
                    break;
                }
                case Act.CookHeld: case Act.CookPack: n1 = Mathf.CeilToInt(item.cookSeconds); n2 = OccupiedCount; break;
                case Act.BoilHeld: { var st = inv.Get(idx); n1 = (int)WaterRules.TypeOf(st); n2 = Mathf.CeilToInt(WaterRules.BoilSeconds(st)); break; }
                case Act.AddFuel: n1 = FireSeconds; n2 = Mathf.CeilToInt(FuelSecondsOf(item)); break;
                case Act.Light: n1 = Mathf.CeilToInt(FuelSecondsOf(item)); break;
                case Act.Warm: n1 = FireSeconds; n2 = Fuel >= MaxFuel - 1f ? 1 : 0; break;
            }
            if (act != _pAct || item != _pItem || n1 != _pN1 || n2 != _pN2)
            {
                _pAct = act; _pItem = item; _pN1 = n1; _pN2 = n2;
                BuildPrompt(act, idx, item, n1, n2);
            }
            sub = _pSub;
            return _pText;
        }

        void BuildPrompt(Act act, int idx, ItemDefinition item, int n1, int n2)
        {
            var sb = Sb; sb.Length = 0; _pSub = null;
            string name = item ? item.displayName : null;
            switch (act)
            {
                case Act.Take:
                {
                    var s = _slots[idx];
                    sb.Append("Take ").Append(name);
                    if (s.IsWater)
                    {
                        var t = WaterRules.TypeOf(s.water);
                        sb.Append(" (").Append(WaterRules.Label(t)).Append(')');
                        _pSub = s.state == CookState.Ready ? "Boiled. Safe to drink" : "Not boiled yet";
                    }
                    else _pSub = s.state == CookState.Burned ? "Burnt" : s.state == CookState.Ready ? "Cooked. Take it before it burns" : "Not cooked yet";
                    break;
                }
                case Act.CookHeld: case Act.CookPack:
                    sb.Append("Cook ").Append(name).Append(" (").Append(Int(n1)).Append(" s)");
                    _pSub = "Leave it too long and it burns";
                    break;
                case Act.BoilHeld:
                {
                    var t = (WaterType)n1;
                    sb.Append("Boil ").Append(WaterRules.Label(t)).Append(" (").Append(Int(n2)).Append(" s)");
                    int loss = C.Water(t).boilChargeLoss;
                    _pSub = loss > 0 ? (loss == 1 ? "It boils down: 1 drink is lost" : "It boils down: " + Int(loss) + " drinks are lost") : "Makes it safe to drink";
                    break;
                }
                case Act.AddFuel:
                    sb.Append("Add ").Append(name).Append(" (fire ");
                    Clock(sb, n1); sb.Append(')');
                    var s2 = new StringBuilder(24).Append('+'); Clock(s2, n2); _pSub = s2.Append(" of fire").ToString();
                    break;
                case Act.Light:
                {
                    sb.Append("Light the fire");
                    var s2b = new StringBuilder(32).Append("Uses 1 ").Append(name).Append(" (");
                    Clock(s2b, n1); _pSub = s2b.Append(')').ToString();
                    break;
                }
                case Act.NeedFuel:
                {
                    sb.Append("Light the fire");
                    var f = fuelItem ? fuelItem : C.wood;
                    _pSub = f ? "Needs 1 " + f.displayName + " or other fuel" : "Needs fuel";
                    break;
                }
                case Act.Warm:
                {
                    sb.Append("Warm up");
                    var s2c = new StringBuilder(40).Append("Fire ");
                    Clock(s2c, n1); _pSub = s2c.Append(n2 == 1 ? "   (fuel full)" : "   (no fuel in your pack)").ToString();
                    break;
                }
                default: _pText = null; return;
            }
            _pText = sb.ToString();
        }

        static string Int(int v) => v.ToString(CultureInfo.InvariantCulture);
        static void Clock(StringBuilder sb, int seconds)
        {
            if (seconds < 0) seconds = 0;
            int m = seconds / 60, s = seconds % 60;
            sb.Append(Int(m)).Append(':');
            if (s < 10) sb.Append('0');
            sb.Append(Int(s));
        }

        // ================================================================== visuals (food on the stones)
        struct Visual { public int slot; public ItemDefinition item; public bool tint; public GameObject go; }
        readonly List<Visual> _visuals = new List<Visual>(8);
        readonly GameObject[] _shown = new GameObject[MaxSlots];
        Transform[] _anchors; int _anchorLayout = -1;
        static MaterialPropertyBlock _mpb;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), ColorId = Shader.PropertyToID("_Color");

        Transform Anchor(int i)
        {
            int n = SlotCount;
            if (_anchors == null) _anchors = new Transform[MaxSlots];
            if (_anchorLayout != n || !_anchors[i])
            {
                _anchorLayout = n;
                for (int k = 0; k < MaxSlots; k++)
                {
                    if (!_anchors[k]) { var go = new GameObject("FoodSlot" + k); go.transform.SetParent(transform, false); _anchors[k] = go.transform; }
                    float a = (k * 360f / n + 20f) * Mathf.Deg2Rad;
                    var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                    _anchors[k].localPosition = dir * (k < n ? foodRadius : foodRadius * 1.35f) + Vector3.up * foodHeight;
                    _anchors[k].localRotation = Quaternion.LookRotation(-dir, Vector3.up);
                }
            }
            return _anchors[i];
        }

        void SlotChanged(int i)
        {
            RefreshVisual(i);
            RefreshFx();
            InvalidatePrompt();
        }

        void RefreshAll()
        {
            for (int i = 0; i < MaxSlots; i++) RefreshVisual(i);
            RefreshFx();
            InvalidatePrompt();
        }

        void RefreshFx()
        {
            if (!fx) return;
            bool cooking = false;
            for (int i = 0; i < MaxSlots; i++)
            {
                var s = _slots[i];
                if (s.state == CookState.Cooking && !s.IsWater) cooking = true;
                bool ready = s.state == CookState.Ready;
                if (ready) fx.SetReady(i, Anchor(i), true); else fx.SetReady(i, null, false);
            }
            fx.SetCooking(cooking && IsLit);
        }

        void RefreshVisual(int i)
        {
            var s = _slots[i];
            GameObject want = null;
            if (!s.IsEmpty && s.item && s.item.worldPrefab)
            {
                bool tint = s.state == CookState.Burned && SharesModel(s.item);
                want = VisualFor(i, s.item, tint);
            }
            if (_shown[i] == want) return;
            if (_shown[i]) _shown[i].SetActive(false);
            _shown[i] = want;
            if (want) want.SetActive(true);
        }

        /// <summary>burnt food that borrows another item's model (no model of its own yet) gets darkened</summary>
        static bool SharesModel(ItemDefinition item)
        {
            var db = ItemDatabase.Instance;
            if (db == null || db.items == null) return true;
            foreach (var it in db.items) if (it && it != item && it.worldPrefab == item.worldPrefab) return true;
            return false;
        }

        GameObject VisualFor(int i, ItemDefinition item, bool tint)
        {
            for (int k = 0; k < _visuals.Count; k++)
            {
                var v = _visuals[k];
                if (v.slot == i && v.item == item && v.tint == tint && v.go) return v.go;
            }
            // first time this item sits on this slot: one small copy of its world model, kept and reused
            var anchor = Anchor(i);
            var go = Instantiate(item.worldPrefab, anchor);
            go.name = item.worldPrefab.name;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) { c.enabled = false; Destroy(c); }
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) Destroy(rb);
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) { mb.enabled = false; Destroy(mb); }
            var t = go.transform;
            t.localPosition = Vector3.zero; t.localRotation = Quaternion.Euler(item.worldEuler); t.localScale = Vector3.one;
            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length > 0)
            {
                var b = rs[0].bounds; for (int r = 1; r < rs.Length; r++) b.Encapsulate(rs[r].bounds);
                float m = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                float k = m > 1e-4f ? Mathf.Clamp(foodSize / m, 0.02f, 4f) : 1f;
                Vector3 off = b.center - t.position;
                t.localScale = Vector3.one * k;
                t.position = anchor.position - off * k + Vector3.up * (b.extents.y * k);
                for (int r = 0; r < rs.Length; r++)
                {
                    rs[r].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    if (!tint) continue;
                    if (_mpb == null) _mpb = new MaterialPropertyBlock();
                    rs[r].GetPropertyBlock(_mpb);
                    _mpb.SetColor(BaseColorId, burntTint); _mpb.SetColor(ColorId, burntTint);
                    rs[r].SetPropertyBlock(_mpb);
                }
            }
            go.SetActive(false);
            _visuals.Add(new Visual { slot = i, item = item, tint = tint, go = go });
            return go;
        }

        // ================================================================== save
        [Serializable] class SlotSave { public int index; public string item; public int state; public float t; public float duration; public bool container; public int water; public int waterType; public float durability; }
        [Serializable] class SlotsSave { public List<SlotSave> slots = new List<SlotSave>(); }

        /// <summary>SaveSystem: lit / fuel are stored in StructureData; this keeps the slots (empty fire = null)</summary>
        public string CaptureState()
        {
            SlotsSave save = null;
            for (int i = 0; i < MaxSlots; i++)
            {
                var s = _slots[i];
                if (s.IsEmpty || !s.item) continue;
                if (save == null) save = new SlotsSave();
                save.slots.Add(new SlotSave
                {
                    index = i, item = s.item.id, state = (int)s.state, t = s.t, duration = s.duration,
                    container = s.IsWater, water = s.IsWater ? s.water.water : 0, waterType = s.IsWater ? (int)s.water.waterType : 0,
                    durability = s.IsWater ? s.water.durability : 0f,
                });
            }
            return save == null ? null : JsonUtility.ToJson(save);
        }

        /// <summary>rebuilds the slots; works before or after <see cref="Restore"/> (states follow the fire)</summary>
        public void RestoreState(string state)
        {
            if (string.IsNullOrEmpty(state)) return;
            SlotsSave save;
            try { save = JsonUtility.FromJson<SlotsSave>(state); }
            catch (Exception e) { Debug.LogWarning("[Campfire] unreadable slot state: " + e.Message, this); return; }
            if (save == null || save.slots == null) return;
            var db = ItemDatabase.Instance;
            foreach (var d in save.slots)
            {
                if (d == null || d.index < 0 || d.index >= MaxSlots) continue;
                var item = db != null ? db.Item(d.item) : null;
                if (!item) { Debug.LogWarning("[Campfire] unknown item on the fire: " + d.item, this); continue; }
                var s = _slots[d.index];
                s.Clear();
                s.state = (CookState)Mathf.Clamp(d.state, 0, (int)CookState.Burned);
                if (s.state == CookState.Empty) continue;
                s.item = item; s.t = Mathf.Max(0f, d.t); s.duration = Mathf.Max(0f, d.duration);
                if (d.container) s.water = new ItemStack(item, 1) { durability = d.durability, water = Mathf.Max(0, d.water), waterType = (WaterType)d.waterType };
            }
            NormalizeSlots();
        }

        /// <summary>SaveSystem: lit / fuel. Never clears the slots (RestoreState may already have filled them).</summary>
        public void Restore(bool lit, float fuel)
        {
            Fuel = Mathf.Max(0f, fuel);
            if (lit && Fuel > 0f) SetLit(true, false);
            else { IsLit = false; if (fx) fx.SetLit(false, false); }
            NormalizeSlots();
        }
    }
}
