using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Core;
using PrimalFrontier.Player;

namespace PrimalFrontier.World
{
    /// <summary>
    /// Cover: keeps rain and sun off inside its area, adds its own warmth (deg C) there, rest here to save and set the
    /// respawn point. Lean-to / leaf shelter / tent: a round cover (coverRadius) on the placed object. A built enclosure
    /// (roof with walls, BuildingEnclosure) uses a box cover (coverBox, local x / z half sizes) on the roof's Enclosure
    /// child and counts any bedroll placed inside as its bed. Everything that asks "is p under a roof" (SurvivalEnvironment,
    /// WeatherManager.RainingAt, Campfire.Sheltered, PlayerSignature) goes through Covers / WarmthAt.
    /// </summary>
    public class Shelter : Interactable
    {
        public static readonly List<Shelter> All = new List<Shelter>();
        public float coverRadius = 2.3f;
        [Tooltip("box cover instead of the radius: local half sizes x / z (0 = use the radius)")] public Vector3 coverBox;
        [Tooltip("roof height above the shelter's origin: below it counts as covered")] public float coverHeight = 2.5f;
        [Tooltip("how far below the origin still counts (a floor slightly under the enclosure origin)")] public float coverBelow = 0f;
        [Tooltip("deg C added while the player is covered (lean-to 4, tent 6, hut up to 7)")] public float warmth = 4f;
        [Tooltip("where the player stands after resting / respawning: this far along forward")] public float restDistance = 1.8f;
        [Tooltip("a Bedroll anywhere inside the cover counts as this shelter's bed (built enclosures)")] public bool bedInsideCounts;
        [Tooltip("second prompt line (enclosures: wall count)")] public string note;
        [HideInInspector] public int enclosureWalls;
        public override float Radius => 1.2f;
        public static System.Action<Shelter> Rested;          // GameManager: save + respawn point
        /// <summary>runs before every static query: the building enclosures re-evaluate themselves when pieces changed</summary>
        public static System.Action Validate = Building.BuildingEnclosure.RefreshIfDirty;

        /// <summary>a Bedroll sits on the same object (tent), or inside a built enclosure</summary>
        public bool HasBed
        {
            get
            {
                if (!_bedChecked) { _bed = GetComponent<Bedroll>(); _bedChecked = true; }
                if (_bed) return true;
                if (!bedInsideCounts) return false;
                var beds = Bedroll.All;
                for (int i = 0; i < beds.Count; i++) { var b = beds[i]; if (b && CoversPoint(b.transform.position + Vector3.up * 0.3f)) return true; }
                return false;
            }
        }
        Bedroll _bed; bool _bedChecked;
        /// <summary>a Bedroll was added / enabled on this object</summary>
        public void RefreshBed() { _bedChecked = false; }
        /// <summary>where resting / respawning puts the player</summary>
        public Vector3 RestPoint => transform.position + transform.forward * restDistance;
        public bool IsEnclosure => bedInsideCounts;

        protected override void OnEnable() { base.OnEnable(); if (!All.Contains(this)) All.Add(this); _bedChecked = false; }
        protected override void OnDisable() { base.OnDisable(); All.Remove(this); }

        /// <summary>is p under this shelter's roof</summary>
        public bool CoversPoint(Vector3 p)
        {
            if (coverBox.x > 0f || coverBox.z > 0f)
            {
                var l = transform.InverseTransformPoint(p);
                return Mathf.Abs(l.x) <= coverBox.x && Mathf.Abs(l.z) <= coverBox.z && l.y < coverHeight && l.y >= -coverBelow;
            }
            var d = p - transform.position; d.y = 0;
            return d.sqrMagnitude <= coverRadius * coverRadius && p.y < transform.position.y + coverHeight && p.y >= transform.position.y - coverBelow;
        }

        public static bool Covers(Vector3 p)
        {
            Validate?.Invoke();
            for (int i = 0; i < All.Count; i++) { var s = All[i]; if (s && s.CoversPoint(p)) return true; }
            return false;
        }

        /// <summary>warmth of the warmest shelter covering p (0 when in the open)</summary>
        public static float WarmthAt(Vector3 p)
        {
            Validate?.Invoke();
            float w = 0f;
            for (int i = 0; i < All.Count; i++) { var s = All[i]; if (s && s.warmth > w && s.CoversPoint(p)) w = s.warmth; }
            return w;
        }

        /// <summary>closest shelter within range of p (null = none)</summary>
        public static Shelter Nearest(Vector3 p, float range, bool withBedOnly = false)
        {
            Validate?.Invoke();
            Shelter best = null; float bestD = range * range;
            for (int i = 0; i < All.Count; i++)
            {
                var s = All[i]; if (!s || (withBedOnly && !s.HasBed)) continue;
                float d = (s.transform.position - p).sqrMagnitude;
                if (d <= bestD) { bestD = d; best = s; }
            }
            return best;
        }

        public override string GetPrompt(PlayerInteraction p, out string sub)
        {
            sub = string.IsNullOrEmpty(note) ? "Saves the game and sets your respawn point" : note + ". Saves the game and sets your respawn point";
            return "Rest in shelter";
        }

        public override void Interact(PlayerInteraction p)
        {
            p.DoOneShot(PlayerActions.Interact, "OnInteract", () =>
            {
                Rested?.Invoke(this);
                PlayerInteraction.Notify("You rest for a moment. Game saved.");
            }, transform.position, 0.8f, this);
        }
    }
}
