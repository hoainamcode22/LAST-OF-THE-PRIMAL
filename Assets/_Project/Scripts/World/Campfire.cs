using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.VFX;

namespace PrimalFrontier.World
{
    /// <summary>
    /// Placed campfire: light (1 wood), add fuel, cook raw food (the cooked piece appears on the stones), warmth and
    /// light, hold to extinguish. Rain burns fuel twice as fast unless a shelter covers it.
    /// </summary>
    public class Campfire : Interactable
    {
        public static readonly List<Campfire> All = new List<Campfire>();
        public CampfireFx fx;
        public ItemDefinition fuelItem;
        public float secondsPerFuel = 240f;
        public float maxFuelSeconds = 1200f;
        public float heatRadius = 5f;
        public float heat = 16f;               // deg C at the fire

        public bool IsLit { get; private set; }
        public float Fuel { get; private set; }
        class Cooking { public ItemDefinition raw; public float t; }
        readonly List<Cooking> _cooking = new List<Cooking>();
        public int CookingCount => _cooking.Count;
        public override float Radius => 0.5f;

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

        void Update()
        {
            UpdateBoil();
            if (!IsLit) return;
            bool wet = Survival.PlayerSurvival.RainingAt(transform.position + Vector3.up * 0.5f);
            Fuel -= Time.deltaTime * (wet ? 2f : 1f);
            for (int i = _cooking.Count - 1; i >= 0; i--)
            {
                var c = _cooking[i]; c.t += Time.deltaTime;
                if (c.t < c.raw.cookSeconds) continue;
                _cooking.RemoveAt(i);
                var pos = transform.position + Quaternion.Euler(0, i * 90f + 30f, 0) * Vector3.forward * 0.45f + Vector3.up * 0.15f;
                WorldPickup.Drop(c.raw.cookedResult, 1, pos);
                GameEvents.Raise(GameEventType.FoodCooked, c.raw.cookedResult.id, 1, transform.position);
                PlayerInteraction.Notify(c.raw.cookedResult.displayName + " is ready.");
            }
            if (fx) fx.SetCooking(_cooking.Count > 0);
            if (Fuel <= 0f) { Fuel = 0f; SetLit(false); PlayerInteraction.Notify("The fire has burned out."); }
        }

        public void SetLit(bool on, bool burst = true)
        {
            if (IsLit == on) return;
            IsLit = on;
            if (fx) fx.SetLit(on, burst);
            GameEvents.Raise(on ? GameEventType.FireLit : GameEventType.FireOut, "campfire", 1, transform.position);
        }

        [Header("Boiling water")]
        public float boilSeconds = 8f;
        ItemStack _boiling; float _boilT;
        public bool Boiling => _boiling != null;

        /// <summary>a container with unboiled water (the active one first)</summary>
        ItemStack FindDirtyWater(PlayerInteraction p)
        {
            var a = p.ActiveStack; if (a != null && a.item.IsWaterContainer && a.dirty && a.water > 0) return a;
            foreach (var s in p.Inventory.Slots) if (s != null && !s.IsEmpty && s.item.IsWaterContainer && s.dirty && s.water > 0) return s;
            return null;
        }

        void UpdateBoil()
        {
            if (_boiling == null) return;
            if (!IsLit) { _boiling = null; PlayerInteraction.Notify("The fire went out before the water boiled."); return; }
            _boilT += Time.deltaTime;
            if (_boilT < boilSeconds) return;
            _boiling.dirty = false; _boiling = null;
            var inv = PlayerLocator.Player ? PlayerLocator.Player.GetComponent<InventorySystem>() : null;
            inv?.ForceNotify();
            VFX.VfxPool.Instance.Play(VFX.VfxId.Steam, transform.position + Vector3.up * 0.4f, Vector3.up);
            Audio.SfxPlayer.Instance.Play(Audio.SfxId.Sizzle, transform.position, 0.6f);
            PlayerInteraction.Notify("The water has boiled. It is safe to drink.");
            GameEvents.Raise(GameEventType.WaterBoiled, "water", 1, transform.position);
        }

        ItemDefinition FindCookable(PlayerInteraction p)
        {
            var a = p.ActiveItem; if (a && a.cookedResult) return a;
            foreach (var s in p.Inventory.Slots) if (!s.IsEmptyOrNull() && s.item.cookedResult) return s.item;
            return null;
        }

        public override string GetPrompt(PlayerInteraction p, out string sub)
        {
            sub = null;
            bool hasFuel = fuelItem && p.Inventory.Has(fuelItem);
            if (!IsLit)
            {
                if (!hasFuel) { sub = "Needs 1 " + (fuelItem ? fuelItem.displayName : "fuel"); }
                return "Light fire";
            }
            var water = FindDirtyWater(p);
            if (water != null && !Boiling && p.ActiveStack == water) { sub = "Makes it safe to drink"; return "Boil water (" + water.item.displayName + ")"; }
            var cook = FindCookable(p);
            if (cook != null && _cooking.Count < 4) { sub = $"Fuel {Mathf.CeilToInt(Fuel / 60f)} min"; return "Cook " + cook.displayName; }
            if (water != null && !Boiling) { sub = "Makes it safe to drink"; return "Boil water (" + water.item.displayName + ")"; }
            if (Boiling) { sub = "Boiling..."; }
            else sub = null;
            sub = (sub != null ? sub + "   " : "") + $"Fuel {Mathf.CeilToInt(Fuel / 60f)} min" + (hasFuel ? "" : "  (no wood)");
            return hasFuel ? "Add fuel" : "Warm up";
        }

        public override bool CanInteract(PlayerInteraction p)
        {
            bool hasFuel = fuelItem && p.Inventory.Has(fuelItem);
            if (!IsLit) return hasFuel;
            return FindCookable(p) != null || hasFuel || (!Boiling && FindDirtyWater(p) != null);
        }

        public override string HoldPrompt(PlayerInteraction p) => IsLit ? "Hold to put out" : null;
        public override void HoldInteract(PlayerInteraction p)
        {
            if (!IsLit) return;
            p.DoOneShot(PlayerActions.Interact, "OnInteract", () => { SetLit(false); Fuel = 0f; }, transform.position, 0.8f, this);
        }

        public override void Interact(PlayerInteraction p)
        {
            if (!CanInteract(p)) return;
            if (!IsLit)
            {
                p.DoOneShot(PlayerActions.Interact, "OnInteract", () =>
                {
                    if (!p.Inventory.Remove(fuelItem, 1)) return;
                    Fuel = Mathf.Min(maxFuelSeconds, Fuel + secondsPerFuel); SetLit(true);
                }, transform.position, 0.8f, this);
                return;
            }
            var water = Boiling ? null : FindDirtyWater(p);
            var cook = FindCookable(p);
            if (water != null && (p.ActiveStack == water || cook == null || _cooking.Count >= 4))
            {
                p.DoOneShot(PlayerActions.Interact, "OnInteract", () =>
                {
                    if (!IsLit || water.water <= 0) return;
                    _boiling = water; _boilT = 0f;
                    PlayerInteraction.Notify("The water is heating over the fire.");
                }, transform.position, 0.8f, this);
                return;
            }
            if (cook != null && _cooking.Count < 4)
            {
                p.DoOneShot(PlayerActions.Interact, "OnInteract", () =>
                {
                    if (!p.Inventory.Remove(cook, 1)) return;
                    _cooking.Add(new Cooking { raw = cook });
                }, transform.position, 0.8f, this);
                return;
            }
            p.DoOneShot(PlayerActions.Interact, "OnInteract", () =>
            {
                if (!p.Inventory.Remove(fuelItem, 1)) return;
                Fuel = Mathf.Min(maxFuelSeconds, Fuel + secondsPerFuel);
            }, transform.position, 0.8f, this);
        }

        public void Restore(bool lit, float fuel)
        {
            Fuel = fuel; _cooking.Clear();
            if (lit && fuel > 0f) SetLit(true, false); else { IsLit = false; if (fx) fx.SetLit(false, false); }
        }
    }
}
