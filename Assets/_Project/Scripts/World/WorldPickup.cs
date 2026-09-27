using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;

namespace PrimalFrontier.World
{
    /// <summary>An item lying in the world. Scene pickups are saved as "taken"; dropped ones are saved with their position.</summary>
    public class WorldPickup : Interactable
    {
        public ItemDefinition item;
        [Min(1)] public int count = 1;
        [System.NonSerialized] public ItemStack uniqueStack;       // tools with durability / filled containers
        public bool Dynamic { get; private set; }                   // spawned at runtime (dropped, spilled, thrown)
        public static readonly List<WorldPickup> Dropped = new List<WorldPickup>();
        public static System.Action<WorldPickup> Taken;             // scene pickups -> save system

        public override float Range => 1.8f;
        public override int Priority => 1;

        public override string GetPrompt(PlayerInteraction p, out string sub)
        {
            sub = null;
            if (item == null) return null;
            int n = uniqueStack != null ? uniqueStack.count : count;
            if (p.Inventory.SpaceFor(item) <= 0) sub = p.Inventory.IsOverweight ? "Too heavy" : "Inventory full";
            return "Pick up " + item.displayName + (n > 1 ? " x" + n : "");
        }
        public override bool CanInteract(PlayerInteraction p) => item != null && p.Inventory.SpaceFor(item) > 0;

        public override void Interact(PlayerInteraction p)
        {
            p.DoOneShot(PlayerActions.Pickup, "OnPickup", () => Collect(p), FocusPoint, 0.8f, this);
        }

        public void Collect(PlayerInteraction p)
        {
            if (this == null || !gameObject.activeSelf || item == null) return;
            if (uniqueStack != null)
            {
                if (!p.Inventory.AddStack(uniqueStack)) { PlayerInteraction.Notify("No room."); return; }
                count = 0;
            }
            else
            {
                int left = p.Inventory.Add(item, count);
                if (left == count) { PlayerInteraction.Notify(p.Inventory.IsOverweight ? "Too heavy." : "Inventory full."); return; }
                count = left;
            }
            if (count > 0) return;                                   // partially taken, the rest stays
            if (Dynamic) { Dropped.Remove(this); Destroy(gameObject); }
            else { Taken?.Invoke(this); gameObject.SetActive(false); }
        }

        // ------------------------------------------------------------------ spawning
        public static WorldPickup Drop(ItemDefinition item, int count, Vector3 pos)
        {
            if (item == null || count <= 0) return null;
            var pk = Spawn(item, pos); pk.count = count; return pk;
        }

        public static WorldPickup DropStack(ItemStack stack, Vector3 pos)
        {
            if (stack == null || stack.IsEmpty) return null;
            var pk = Spawn(stack.item, pos);
            pk.count = stack.count;
            if (stack.item.maxStack == 1 || stack.water > 0 || (stack.item.HasDurability && stack.durability < stack.item.maxDurability)) pk.uniqueStack = stack.Clone();
            return pk;
        }

        static WorldPickup Spawn(ItemDefinition item, Vector3 pos)
        {
            GameObject go;
            if (item.worldPrefab) go = Instantiate(item.worldPrefab);
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.transform.localScale = Vector3.one * 0.2f;
                Destroy(go.GetComponent<Collider>());
            }
            go.name = "Pickup_" + item.id;
            // settle on the ground below
            if (Physics.Raycast(pos + Vector3.up * 0.5f, Vector3.down, out var hit, 6f, ~LayerMask.GetMask("Player"), QueryTriggerInteraction.Ignore)) pos = hit.point + Vector3.up * 0.02f;
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Quaternion.Euler(item.worldEuler));
            foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false;   // pickups never block the player
            // rest the lowest point of the model on the ground
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0) { var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); go.transform.position += Vector3.up * (pos.y - b.min.y + 0.005f); }
            var pk = go.GetOrAdd<WorldPickup>();
            pk.item = item; pk.Dynamic = true; pk.RefreshBounds();
            Dropped.Add(pk);
            return pk;
        }

        public static void ClearDropped()
        {
            foreach (var d in Dropped) if (d) Destroy(d.gameObject);
            Dropped.Clear();
        }
    }
}
