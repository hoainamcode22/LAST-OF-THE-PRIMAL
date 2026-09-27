using System;
using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;

namespace PrimalFrontier.World
{
    /// <summary>Wreck crate / barrel: searched once, gives its contents (the rest spills on the ground if full).</summary>
    public class LootContainer : Interactable
    {
        [Serializable] public struct Entry { public ItemDefinition item; public int count; }
        public string displayName = "Crate";
        public Entry[] contents = Array.Empty<Entry>();
        [Tooltip("journal / tutorial id raised when searched")] public string discoveryId;
        [TextArea(1, 3)] public string foundText;
        public bool Opened { get; private set; }
        public override float Radius => 0.5f;

        public override string GetPrompt(PlayerInteraction p, out string sub)
        {
            sub = null;
            if (Opened) return null;
            return "Search " + displayName;
        }

        public override void Interact(PlayerInteraction p)
        {
            if (Opened) return;
            p.DoOneShot(PlayerActions.Interact, "OnInteract", () => Open(p), FocusPoint, 0.8f, this);
        }

        public void Open(PlayerInteraction p)
        {
            if (Opened) return;
            Opened = true;
            int total = 0;
            foreach (var e in contents) if (e.item) { p.GiveOrDrop(e.item, e.count); total += e.count; }
            PlayerInteraction.Notify(!string.IsNullOrEmpty(foundText) ? foundText : total > 0 ? "You found something useful." : "Empty. The sea took everything.");
            GameEvents.Raise(GameEventType.LootOpened, string.IsNullOrEmpty(discoveryId) ? SaveId : discoveryId, total, transform.position);
        }

        public void Restore(bool opened) { Opened = opened; }
    }
}
