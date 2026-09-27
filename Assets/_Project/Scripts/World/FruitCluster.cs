using UnityEngine;
using PrimalFrontier.Audio;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.VFX;

namespace PrimalFrontier.World
{
    /// <summary>
    /// A bunch of fruit high on a Climbable tree. Picked from the trunk (Harvest_Fruit): the fruit meshes (children)
    /// disappear, the items go into the pack, leaves and a pickup sound play. Grows back after regrowHours of game
    /// time (sleeping counts). Place it by hand where the fruit hangs; the reach point is this transform.
    /// </summary>
    public class FruitCluster : MonoBehaviour
    {
        public ItemDefinition item;
        [Min(1)] public int amount = 3;
        public float regrowHours = 30f;
        public double EmptyUntil { get; private set; } = -1;
        public bool Ripe => EmptyUntil < 0;
        Renderer[] _fruit;

        void Awake() { _fruit = GetComponentsInChildren<Renderer>(true); }

        void Update()
        {
            if (!Ripe && GameClock.Now >= EmptyUntil) Regrow();
        }

        /// <summary>pick the fruit into the inventory; false if nothing to pick or the pack is full</summary>
        public bool Harvest(InventorySystem inv)
        {
            if (!Ripe || item == null || inv == null) return false;
            int added = inv.Add(item, amount);
            if (added <= 0) { Player.PlayerInteraction.Notify("Your pack is full."); return false; }
            EmptyUntil = GameClock.Now + GameClock.Hours(regrowHours);
            foreach (var r in _fruit) if (r) r.enabled = false;
            VfxPool.Instance.Play(VfxId.Leaves, transform.position, Vector3.up);
            SfxPlayer.Instance.Play(SfxId.Pickup, transform.position, 0.8f);
            SfxPlayer.Instance.Play(SfxId.LeafRustle, transform.position, 0.7f);
            GameEvents.Raise(GameEventType.FruitHarvested, item.id, added, transform.position);
            return true;
        }

        public void Regrow()
        {
            EmptyUntil = -1;
            if (_fruit == null) _fruit = GetComponentsInChildren<Renderer>(true);
            foreach (var r in _fruit) if (r) r.enabled = true;
        }

#if UNITY_EDITOR
        void OnDrawGizmos() { Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f); Gizmos.DrawWireSphere(transform.position, 0.25f); }
#endif
    }
}
