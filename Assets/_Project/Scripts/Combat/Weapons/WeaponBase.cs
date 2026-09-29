using UnityEngine;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;

namespace PrimalFrontier.Combat.Weapons
{
    /// <summary>
    /// Shared weapon plumbing: stamina checks, durability wear on the stack that actually attacked (found again by
    /// reference when the hit lands, so switching slots mid-swing never wears the wrong item), camera shake.
    /// </summary>
    public abstract class WeaponBase : IWeapon
    {
        public WeaponData Data { get; }
        public ItemDefinition Item { get; }
        public abstract bool Busy { get; }
        protected WeaponContext Ctx { get; private set; }
        protected GameObject Model { get; private set; }

        protected WeaponBase(WeaponData data, ItemDefinition item) { Data = data; Item = item; }

        public virtual void Equip(WeaponContext ctx) { Ctx = ctx; }
        public virtual void Unequip() { Cancel(); Model = null; }
        public virtual void OnModelChanged(GameObject model) { Model = model; }
        public abstract void HandleInput(WeaponInput input);
        public virtual void OnAimChanged(bool aiming) { }
        public abstract void Cancel();
        public virtual void ResetInput() { }
        public virtual void Tick(float dt) { }
        public virtual void LateTick() { }
        public virtual void OnAnimEvent(string name, string param) { }

        // ------------------------------------------------------------------ stamina
        /// <summary>the same rule as before: refused below half the cost ("too tired"), otherwise spent</summary>
        protected bool TrySpendStamina(float cost, string tiredMessage)
        {
            var sv = Ctx.survival;
            if (sv == null || cost <= 0f) return true;
            if (sv.Stamina < cost * 0.5f) { if (tiredMessage != null) PlayerInteraction.Notify(tiredMessage); return false; }
            sv.UseStamina(cost);
            return true;
        }

        protected bool HasStaminaFor(float cost) => Ctx.survival == null || Ctx.survival.Stamina >= cost * 0.5f;

        // ------------------------------------------------------------------ durability
        /// <summary>the stack in the active slot right now (capture it when the attack starts)</summary>
        protected ItemStack ActiveStack => Ctx.inventory ? Ctx.inventory.ActiveStack : null;

        /// <summary>wears the given stack wherever it is now; breaks it at 0 (notifies "X broke!")</summary>
        protected void Wear(ItemStack stack, float amount)
        {
            var inv = Ctx.inventory;
            if (inv == null || stack == null || stack.IsEmpty || !stack.item.HasDurability || amount <= 0f) return;
            if (ReferenceEquals(inv.ActiveStack, stack))
            {
                if (inv.WearActive(amount)) Broke(stack.item.displayName);
                return;
            }
            int slot = SlotOf(inv, stack);
            if (slot < 0) return;                                           // dropped / stored since the swing started
            stack.durability -= amount;
            if (stack.durability > 0f) { inv.ForceNotify(); return; }
            string id = stack.item.id, name = stack.item.displayName;
            inv.SetSlot(slot, null);
            if (inv.raiseGameEvents) GameEvents.Raise(GameEventType.ItemRemoved, id, 1, inv.transform.position);
            Broke(name);
        }

        /// <summary>a weapon / tool broke on a hit: the note, the snap sound and a little dust at the hand</summary>
        void Broke(string name)
        {
            PlayerInteraction.Notify(name + " broke!");
            Vector3 at = Ctx.hierarchy && Ctx.hierarchy.RightHandWeaponSocket ? Ctx.hierarchy.RightHandWeaponSocket.position : Ctx.root.position + Vector3.up * 1.1f;
            Audio.SfxPlayer.Instance.Play(Audio.SfxId.ToolBreak, at, 0.9f);
            VFX.VfxPool.Instance.Play(VFX.VfxId.DustImpact, at, Vector3.up, null, 0.5f);
        }

        static int SlotOf(InventorySystem inv, ItemStack stack)
        {
            var slots = inv.Slots;
            if (slots == null) return -1;
            for (int i = 0; i < slots.Length; i++) if (ReferenceEquals(slots[i], stack)) return i;
            return -1;
        }

        // ------------------------------------------------------------------ feedback
        protected void Shake(float amplitude, float duration) { var cam = Ctx.Camera; if (cam) cam.AddShake(amplitude, duration); }
        protected void MarkCombat() { if (Ctx.controller) Ctx.controller.LastCombatTime = Time.time; }
    }
}
