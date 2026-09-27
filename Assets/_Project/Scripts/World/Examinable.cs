using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Core;
using PrimalFrontier.Player;

namespace PrimalFrontier.World
{
    /// <summary>Something to look at closely: giant footprints, the captain's log, a view from the ridge. Unlocks a journal entry.</summary>
    public class Examinable : Interactable
    {
        public string displayName = "Something";
        public string verb = "Examine";
        public string discoveryId;
        public GameEventType eventType = GameEventType.Discovery;
        [TextArea(2, 5)] public string thought;
        public bool once = true;
        public float range = 2.4f;
        public bool Examined { get; private set; }
        public override float Range => range;
        public override float Radius => 0.6f;
        public override int Priority => 2;

        public override string GetPrompt(PlayerInteraction p, out string sub) { sub = null; return Examined && once ? null : verb + " " + displayName; }

        public override void Interact(PlayerInteraction p)
        {
            p.DoOneShot(PlayerActions.Interact, "OnInteract", () => Examine(), FocusPoint, 0.7f, this);
        }

        public void Examine()
        {
            if (Examined && once) return;
            Examined = true;
            if (!string.IsNullOrEmpty(thought)) PlayerInteraction.Notify(thought);
            GameEvents.Raise(eventType, discoveryId, 1, transform.position);
        }
        public void Restore(bool examined) => Examined = examined;
    }
}
