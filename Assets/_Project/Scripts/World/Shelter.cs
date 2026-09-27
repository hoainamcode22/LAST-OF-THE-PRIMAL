using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Core;
using PrimalFrontier.Player;

namespace PrimalFrontier.World
{
    /// <summary>Lean-to shelter: keeps rain off, a little warmer, rest here to save and set the respawn point.</summary>
    public class Shelter : Interactable
    {
        public static readonly List<Shelter> All = new List<Shelter>();
        public float coverRadius = 2.3f;
        public float warmth = 4f;
        public override float Radius => 1.2f;
        public static System.Action<Shelter> Rested;          // GameManager: save + respawn point

        protected override void OnEnable() { base.OnEnable(); if (!All.Contains(this)) All.Add(this); }
        protected override void OnDisable() { base.OnDisable(); All.Remove(this); }

        public static bool Covers(Vector3 p)
        {
            foreach (var s in All)
            {
                if (!s) continue;
                var d = p - s.transform.position; d.y = 0;
                if (d.sqrMagnitude <= s.coverRadius * s.coverRadius && p.y < s.transform.position.y + 2.5f) return true;
            }
            return false;
        }
        public static float WarmthAt(Vector3 p) => Covers(p) ? 4f : 0f;

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
