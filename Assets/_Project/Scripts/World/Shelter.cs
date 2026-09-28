using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Core;
using PrimalFrontier.Player;

namespace PrimalFrontier.World
{
    /// <summary>
    /// Lean-to / tent cover: keeps rain off inside coverRadius, adds its own warmth (deg C) there, rest here to save and
    /// set the respawn point. A tent is the same component with a Bedroll on the same object (sleep through the night).
    /// </summary>
    public class Shelter : Interactable
    {
        public static readonly List<Shelter> All = new List<Shelter>();
        public float coverRadius = 2.3f;
        [Tooltip("roof height above the shelter's origin: below it counts as covered")] public float coverHeight = 2.5f;
        [Tooltip("deg C added while the player is covered (lean-to 4, tent 6)")] public float warmth = 4f;
        public override float Radius => 1.2f;
        public static System.Action<Shelter> Rested;          // GameManager: save + respawn point
        /// <summary>a Bedroll sits on the same object (tent): sleeping happens there</summary>
        public bool HasBed { get { if (!_bedChecked) { _bed = GetComponent<Bedroll>(); _bedChecked = true; } return _bed; } }
        Bedroll _bed; bool _bedChecked;
        /// <summary>a Bedroll was added / enabled on this object</summary>
        public void RefreshBed() { _bedChecked = false; }

        protected override void OnEnable() { base.OnEnable(); if (!All.Contains(this)) All.Add(this); _bedChecked = false; }
        protected override void OnDisable() { base.OnDisable(); All.Remove(this); }

        /// <summary>is p under this shelter's roof</summary>
        public bool CoversPoint(Vector3 p)
        {
            var d = p - transform.position; d.y = 0;
            return d.sqrMagnitude <= coverRadius * coverRadius && p.y < transform.position.y + coverHeight;
        }

        public static bool Covers(Vector3 p)
        {
            for (int i = 0; i < All.Count; i++) { var s = All[i]; if (s && s.CoversPoint(p)) return true; }
            return false;
        }

        /// <summary>warmth of the warmest shelter covering p (0 when in the open)</summary>
        public static float WarmthAt(Vector3 p)
        {
            float w = 0f;
            for (int i = 0; i < All.Count; i++) { var s = All[i]; if (s && s.warmth > w && s.CoversPoint(p)) w = s.warmth; }
            return w;
        }

        /// <summary>closest shelter within range of p (null = none)</summary>
        public static Shelter Nearest(Vector3 p, float range, bool withBedOnly = false)
        {
            Shelter best = null; float bestD = range * range;
            for (int i = 0; i < All.Count; i++)
            {
                var s = All[i]; if (!s || (withBedOnly && !s.HasBed)) continue;
                float d = (s.transform.position - p).sqrMagnitude;
                if (d <= bestD) { bestD = d; best = s; }
            }
            return best;
        }

        public override string GetPrompt(PlayerInteraction p, out string sub) { sub = "Saves the game and sets your respawn point"; return "Rest in shelter"; }

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
