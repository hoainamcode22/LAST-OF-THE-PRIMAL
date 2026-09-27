using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Core;
using PrimalFrontier.Player;

namespace PrimalFrontier.World
{
    /// <summary>Sleep through the night (18:00-05:00) and wake at dawn; sets the respawn point.</summary>
    public class Bedroll : Interactable
    {
        public float sleepFromHour = 18f, sleepUntilHour = 6f;
        public override float Radius => 0.8f;
        /// <summary>GameManager handles the fade, the time skip and the save</summary>
        public static System.Action<Bedroll, PlayerInteraction> SleepRequested;
        /// <summary>current hour (TimeManager)</summary>
        public static System.Func<float> Hour = () => 12f;

        public bool CanSleepNow { get { float h = Hour(); return h >= sleepFromHour || h < sleepUntilHour - 1f; } }

        public override string GetPrompt(PlayerInteraction p, out string sub)
        {
            sub = CanSleepNow ? "Sleep until dawn (saves the game)" : "You can only sleep after sunset";
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
