using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Core;
using PrimalFrontier.Player;

namespace PrimalFrontier.World
{
    /// <summary>
    /// Sleep through the night (18:00-05:00) and wake at dawn; sets the respawn point. Any number of beds may exist
    /// (bedrolls, tents: a tent is a Shelter with a Bedroll on the same object; a bedroll inside a built hut is that
    /// enclosure's bed). When sleeping is possible the bed wins the interaction target over its own shelter ("Rest in
    /// shelter" by day, "Sleep" at night).
    /// </summary>
    public class Bedroll : Interactable
    {
        public static readonly List<Bedroll> All = new List<Bedroll>();
        public float sleepFromHour = 18f, sleepUntilHour = 6f;
        public override float Radius => 0.8f;
        /// <summary>GameManager handles the fade, the time skip and the save</summary>
        public static System.Action<Bedroll, PlayerInteraction> SleepRequested;
        /// <summary>current hour (TimeManager)</summary>
        public static System.Func<float> Hour = () => 12f;

        public bool CanSleepNow { get { float h = Hour(); return h >= sleepFromHour || h < sleepUntilHour - 1f; } }
        /// <summary>above a shelter on the same object at night, below it by day</summary>
        public override int Priority => CanSleepNow ? 1 : -1;
        /// <summary>under a roof (own shelter, tent or a built enclosure)</summary>
        public bool Sheltered => Shelter.Covers(transform.position + Vector3.up * 0.3f);

        protected override void OnEnable() { base.OnEnable(); if (!All.Contains(this)) All.Add(this); var s = GetComponent<Shelter>(); if (s) s.RefreshBed(); }
        protected override void OnDisable() { base.OnDisable(); All.Remove(this); }

        public override string GetPrompt(PlayerInteraction p, out string sub)
        {
            sub = CanSleepNow ? (Sheltered ? "Sleep until dawn, under a roof (saves the game)" : "Sleep until dawn in the open (saves the game)") : "You can only sleep after sunset";
            return "Sleep";
        }
        public override bool CanInteract(PlayerInteraction p) => CanSleepNow;
        public override void Interact(PlayerInteraction p)
        {
            if (!CanSleepNow) return;
            SleepRequested?.Invoke(this, p);
        }
    }
}
