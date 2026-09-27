using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;

namespace PrimalFrontier.World
{
    /// <summary>
    /// Fresh water (pond, stream) you can drink from and fill containers at. The water surface mesh defines where:
    /// the closest vertex to the player is the drinking spot. The ocean is handled by <see cref="OceanShore"/>.
    /// </summary>
    public class WaterSource : Interactable
    {
        public string displayName = "Fresh water";
        public bool fresh = true;
        public float thirstPerDrink = 28f;
        public MeshFilter surface;
        Vector3[] _pts; Bounds _worldBounds; Vector3 _focus;
        public static readonly List<WaterSource> All = new List<WaterSource>();

        public override float Range => 2.0f;
        public override float Radius => 0f;
        public override Vector3 FocusPoint => _focus;
        public override int Priority => -1;

        protected override void OnEnable() { base.OnEnable(); if (!All.Contains(this)) All.Add(this); Cache(); }
        protected override void OnDisable() { base.OnDisable(); All.Remove(this); }

        void Cache()
        {
            if (!surface) surface = GetComponentInChildren<MeshFilter>();
            if (!surface || !surface.sharedMesh) { _pts = new Vector3[0]; return; }
            var m = surface.sharedMesh; var v = m.vertices; var t = surface.transform;
            // keep ~1 point per 0.8 m so the lookup stays cheap
            var pts = new List<Vector3>(); var seen = new HashSet<Vector3Int>();
            foreach (var lv in v)
            {
                var w = t.TransformPoint(lv); var key = new Vector3Int(Mathf.RoundToInt(w.x / 0.8f), 0, Mathf.RoundToInt(w.z / 0.8f));
                if (seen.Add(key)) pts.Add(w);
            }
            _pts = pts.ToArray();
            var r = surface.GetComponent<Renderer>(); _worldBounds = r ? r.bounds : new Bounds(t.position, Vector3.one * 10f);
            _worldBounds.Expand(6f);
            _focus = _pts.Length > 0 ? _pts[0] : transform.position;
        }

        /// <summary>closest water point to pos (and distance); updates the focus used by the interaction scan</summary>
        public float Closest(Vector3 pos, out Vector3 point)
        {
            point = _focus;
            if (_pts == null || _pts.Length == 0 || !_worldBounds.Contains(new Vector3(pos.x, _worldBounds.center.y, pos.z))) return float.MaxValue;
            float best = float.MaxValue;
            foreach (var p in _pts) { float d = (p.x - pos.x) * (p.x - pos.x) + (p.z - pos.z) * (p.z - pos.z); if (d < best) { best = d; point = p; } }
            return Mathf.Sqrt(best);
        }

        void Update()
        {
            var pl = PlayerLocator.Position;
            if (pl.HasValue) Closest(pl.Value, out _focus);
        }

        public static bool IsNearFresh(Vector3 pos, float range = 2.5f)
        {
            foreach (var w in All) if (w.fresh && w.Closest(pos, out _) <= range) return true;
            return false;
        }

        public override string GetPrompt(PlayerInteraction p, out string sub)
        {
            sub = null;
            var st = p.ActiveStack;
            if (st != null && st.item.IsWaterContainer && fresh)
            {
                if (st.water < st.item.waterCharges) { sub = $"{st.water}/{st.item.waterCharges} drinks"; return "Fill " + st.item.displayName; }
            }
            if (!fresh) sub = "Salt water. Drinking it will only make you thirstier.";
            return (fresh ? "Drink " : "Drink ") + displayName.ToLowerInvariant();
        }

        public override void Interact(PlayerInteraction p)
        {
            var st = p.ActiveStack;
            int slot = p.Inventory.ActiveSlot;
            if (fresh && st != null && st.item.IsWaterContainer && st.water < st.item.waterCharges)
            {
                p.DoOneShot(PlayerActions.Drink, "OnDrink", () =>
                {
                    var s = p.Inventory.Get(slot); if (s == null || !s.item.IsWaterContainer) return;
                    s.water = s.item.waterCharges; p.Inventory.ForceNotify();
                    PlayerInteraction.Notify(s.item.displayName + " filled.");
                    GameEvents.Raise(GameEventType.WaterFilled, s.item.id, 1, transform.position);
                }, FocusPoint, 2.0f, this);
                return;
            }
            p.DoOneShot(PlayerActions.Drink, "OnDrink", () => Drink(p), FocusPoint, 2.0f, this);
        }

        public void Drink(PlayerInteraction p)
        {
            if (fresh)
            {
                p.Survival.Consume(0f, thirstPerDrink, 0f, 4f);
                GameEvents.Raise(GameEventType.Drank, "fresh_water", 1, transform.position);
            }
            else
            {
                p.Survival.Consume(0f, -6f, 0f, 0f);
                PlayerInteraction.Notify("Salt water. It burns your throat and makes the thirst worse.");
                GameEvents.Raise(GameEventType.TriedSaltWater, "ocean", 1, transform.position);
            }
        }
    }

    /// <summary>cheap global access to the player's position for world scripts (set by GameManager / the player)</summary>
    public static class PlayerLocator
    {
        public static Transform Player;
        public static Vector3? Position => Player ? Player.position : (Vector3?)null;
    }
}
