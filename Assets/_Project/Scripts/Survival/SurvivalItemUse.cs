using System;
using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Audio;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.VFX;

namespace PrimalFrontier.Survival
{
    /// <summary>
    /// Survival's item uses, registered in PlayerInteraction.UseHandlers (attack button, touch USE, inventory Use):
    /// - treatment items (ItemDefinition.cures / healOverTime, e.g. the bandage): the Bandage_Use action, BandageWrap sound,
    ///   the cured effects removed (bleeding stops), health back slowly (Recovering), a faint Heal effect. Not food: no
    ///   eat clip, no Ate event (the tutorial "food" step does not complete).
    /// - food that spoils: eaten from the exact stack (its spoilage stage counts), same Eat action as other food.
    /// Food that never spoils keeps PlayerInteraction.Eat.
    /// </summary>
    public static class SurvivalItemUse
    {
        public const string BandageId = "bandage";
        static bool _registered;
        static readonly Func<ItemDefinition, bool> HandlesMedicalFn = HandlesMedical, HandlesFoodFn = HandlesFood;
        static readonly Func<PlayerInteraction, ItemStack, bool> UseMedicalFn = UseMedical, UseFoodFn = UseFood;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() => _registered = false;

        /// <summary>adds the handlers once per play session (PlayerSurvival.Awake calls it)</summary>
        public static void EnsureRegistered()
        {
            var list = PlayerInteraction.UseHandlers;
            if (_registered)
            {
                for (int i = 0; i < list.Count; i++) if (list[i].handles == HandlesMedicalFn) return;     // still there
            }
            list.Add(new PlayerInteraction.ItemUseHandler { handles = HandlesMedicalFn, use = UseMedicalFn });
            list.Add(new PlayerInteraction.ItemUseHandler { handles = HandlesFoodFn, use = UseFoodFn });
            _registered = true;
        }

        // ------------------------------------------------------------------ rules
        /// <summary>a bandage or another treatment (the legacy bandage asset without cures counts too)</summary>
        public static bool HandlesMedical(ItemDefinition item) => item && (item.IsMedical || item.id == BandageId);
        public static bool HandlesFood(ItemDefinition item) => item && item.IsFood && item.Spoils && !HandlesMedical(item);

        /// <summary>does this item treat the effect (the bandage treats bleeding even before its asset lists it)</summary>
        public static bool Cures(ItemDefinition item, string effectId)
        {
            if (!item || string.IsNullOrEmpty(effectId)) return false;
            if (item.cures != null) for (int i = 0; i < item.cures.Length; i++) if (item.cures[i] == effectId) return true;
            return item.id == BandageId && effectId == StatusEffectIds.Bleeding && (item.cures == null || item.cures.Length == 0);
        }

        /// <summary>health a treatment gives back over time (legacy bandage: its instant health becomes gradual)</summary>
        public static float HealOf(ItemDefinition item) => item ? item.healOverTime + Mathf.Max(0f, item.health) : 0f;

        /// <summary>null when the treatment would help now, else why not</summary>
        public static string WhyNot(PlayerHealth hp, ItemDefinition item)
        {
            if (!hp || !item) return "Nothing to treat.";
            if (hp.IsDead) return "Nothing to treat.";
            var fx = hp.Effects;
            if (fx) foreach (var a in fx.ActiveEffects) if (a.def && Cures(item, a.def.id)) return null;
            if (HealOf(item) > 0f && hp.Health < hp.maxHealth - 1f) return null;
            return "You have no wound to dress.";
        }

        // ------------------------------------------------------------------ uses
        static bool UseMedical(PlayerInteraction p, ItemStack st) => p && st != null && p.Inventory ? UseFromSlot(p, p.Inventory.ActiveSlot) : false;
        static bool UseFood(PlayerInteraction p, ItemStack st) => p && st != null && p.Inventory ? EatFromSlot(p, p.Inventory.ActiveSlot) : false;

        /// <summary>treat with the item in this inventory slot (bandage): true when the action started</summary>
        public static bool UseFromSlot(PlayerInteraction p, int slot)
        {
            var inv = p ? p.Inventory : null; var st = inv ? inv.Get(slot) : null;
            if (st == null || !HandlesMedical(st.item)) return false;
            var item = st.item; var hp = p.Health;
            string why = WhyNot(hp, item);
            if (why != null) { PlayerInteraction.Notify(why); return false; }
            bool started = p.DoOneShot(PlayerActions.BandageUse, "OnUseItem", () => Treat(p, slot, item), null, 1.6f);
            if (started) SfxPlayer.Instance.Play(SfxId.BandageWrap, p.transform.position + Vector3.up * 1f, 0.9f);
            return started;
        }

        /// <summary>the treatment itself (on the animation event): one item used, cures, slow heal, faint heal effect</summary>
        public static bool Treat(PlayerInteraction p, int slot, ItemDefinition item) => p && Treat(p.Health, p.Survival, p.Inventory, slot, item, p.transform);

        /// <summary>the treatment on any body (tests, other callers): one item from the slot (else the pack), cures, slow heal</summary>
        public static bool Treat(PlayerHealth hp, PlayerSurvival sv, InventorySystem inv, int slot, ItemDefinition item, Transform at)
        {
            if (!inv || !item) return false;
            var s = inv.Get(slot);
            if (s != null && s.item == item) inv.TakeFromSlot(slot, 1);
            else if (!inv.Remove(item, 1)) return false;
            var fx = hp ? hp.Effects : null;
            if (fx)
            {
                for (int i = fx.ActiveEffects.Count - 1; i >= 0; i--)
                {
                    if (i >= fx.ActiveEffects.Count) continue;
                    var a = fx.ActiveEffects[i];
                    if (!a.def || !Cures(item, a.def.id)) continue;
                    if (a.def.id == StatusEffectIds.Bleeding) hp.StopBleeding(); else fx.Remove(a.def.id, true);
                }
            }
            float heal = HealOf(item);
            if (heal > 0f && sv) sv.HealOverTime(heal);
            Vector3 pos = at ? at.position : Vector3.zero;
            if (Application.isPlaying && at) VfxPool.Instance.Play(VfxId.Heal, pos + Vector3.up * 1.05f + at.forward * 0.15f, Vector3.up, at, 0.6f);
            GameEvents.Raise(GameEventType.ItemUsed, item.id, 1, pos);
            return true;
        }

        /// <summary>eat one from this exact slot (its spoilage stage counts): true when the action started</summary>
        public static bool EatFromSlot(PlayerInteraction p, int slot)
        {
            var inv = p ? p.Inventory : null; var st = inv ? inv.Get(slot) : null;
            if (st == null || !st.item.IsFood) return false;
            var item = st.item;
            if (p.Feedback) p.Feedback.HotFood = item.isHot;
            return p.DoOneShot(PlayerActions.Eat, "OnEat", () =>
            {
                var s = inv.Get(slot);
                FoodStage stage;
                if (s != null && s.item == item) { stage = Spoilage.Stage(s); inv.TakeFromSlot(slot, 1); }
                else
                {
                    var any = FirstStack(inv, item); if (any == null) return;
                    stage = Spoilage.Stage(any);
                    if (!inv.Remove(item, 1)) return;
                }
                if (p.Survival) p.Survival.ConsumeItem(item, stage);
            }, null, 0.9f);
        }

        static ItemStack FirstStack(InventorySystem inv, ItemDefinition item)
        {
            var slots = inv.Slots; if (slots == null) return null;
            for (int i = slots.Length - 1; i >= 0; i--) { var s = slots[i]; if (s != null && !s.IsEmpty && s.item == item) return s; }   // Remove takes from the end
            return null;
        }
    }
}
