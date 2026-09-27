using UnityEngine;
using PrimalFrontier.Items;
using PrimalFrontier.Player;

namespace PrimalFrontier.World
{
    /// <summary>Woven storage basket: its own inventory, opened next to the player's in the inventory screen.</summary>
    [RequireComponent(typeof(InventorySystem))]
    public class StorageBox : Interactable
    {
        public InventorySystem Inventory { get; private set; }
        public static System.Action<StorageBox> Opened;       // UI
        public override float Radius => 0.5f;

        void Awake()
        {
            Inventory = GetComponent<InventorySystem>();
            Inventory.hotbarSize = 0; Inventory.maxWeight = 0f; Inventory.raiseGameEvents = false;
            Inventory.EnsureSlots();
        }

        public override string GetPrompt(PlayerInteraction p, out string sub) { sub = null; return "Open storage"; }
        public override void Interact(PlayerInteraction p) => Opened?.Invoke(this);
    }
}
