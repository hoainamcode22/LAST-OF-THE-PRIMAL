using UnityEngine;
using PrimalFrontier.Core;
using PrimalFrontier.Items;

namespace PrimalFrontier.Survival
{
    public enum FoodStage { Fresh, Aging, Spoiled }

    /// <summary>
    /// Food spoilage rules, the only place they live. A stack of food that spoils (ItemDefinition.spoilHours &gt; 0)
    /// carries the game time it was made (ItemStack.madeAt, count-weighted when stacks merge); its stage is worked out
    /// from that when it matters (eating, the inventory, a slow pack check in PlayerSurvival): nothing ticks per item.
    /// Fresh -> Aging (SurvivalConfig.agingAt of the spoil time) -> Spoiled (all of it). Aging food is worth a little less;
    /// spoiled food is worth much less and often makes you sick. Cooking keeps the part of the time already gone.
    /// </summary>
    public static class Spoilage
    {
        static SurvivalConfig C => SurvivalConfig.Instance;

        public static bool Spoils(ItemDefinition it) => it != null && it.spoilHours > 0f;
        /// <summary>game seconds from made to spoiled (0 = never)</summary>
        public static double SpoilSeconds(ItemDefinition it) => Spoils(it) ? it.spoilHours * (double)Mathf.Max(1f, GameClock.SecondsPerHour) : 0.0;

        /// <summary>part of the spoil time gone (0 fresh .. 1+ spoiled); 0 for food that never spoils</summary>
        public static float Fraction(ItemStack s)
        {
            if (s == null || !Spoils(s.item)) return 0f;
            double t = SpoilSeconds(s.item);
            return t > 0 ? (float)(System.Math.Max(0.0, GameClock.Now - s.madeAt) / t) : 0f;
        }

        public static FoodStage StageOf(float fraction) => fraction >= 1f ? FoodStage.Spoiled : fraction >= C.agingAt ? FoodStage.Aging : FoodStage.Fresh;
        public static FoodStage Stage(ItemStack s) => s == null || !Spoils(s.item) ? FoodStage.Fresh : StageOf(Fraction(s));

        /// <summary>x hunger / thirst / health / stamina of the food at this stage</summary>
        public static float Nutrition(FoodStage st) => st == FoodStage.Spoiled ? C.spoiledNutrition : st == FoodStage.Aging ? C.agingNutrition : 1f;

        /// <summary>chance of food poisoning from eating one of this item at this stage</summary>
        public static float SickChance(ItemDefinition it, FoodStage st)
        {
            float b = it != null ? it.sicknessChance : 0f;
            if (st == FoodStage.Spoiled) return Mathf.Max(b, C.spoiledSickChance);
            if (st == FoodStage.Aging) return Mathf.Clamp01(b + C.agingSickChance);
            return b;
        }

        /// <summary>"", "aging", "spoiled" (UI)</summary>
        public static string Label(FoodStage st) => st == FoodStage.Spoiled ? "spoiled" : st == FoodStage.Aging ? "aging" : "";
        /// <summary>slot tint of a stage (white = fresh)</summary>
        public static Color Tint(FoodStage st) => st == FoodStage.Spoiled ? new Color(0.62f, 0.66f, 0.42f) : st == FoodStage.Aging ? new Color(0.92f, 0.86f, 0.7f) : Color.white;

        /// <summary>the made-at time a cooked result gets so it keeps the part of its spoil time the raw food had used</summary>
        public static double CookedMadeAt(ItemDefinition raw, double rawMadeAt, ItemDefinition cooked)
        {
            double now = GameClock.Now;
            if (!Spoils(raw) || !Spoils(cooked)) return now;
            double f = System.Math.Max(0.0, now - rawMadeAt) / System.Math.Max(1.0, SpoilSeconds(raw));
            return now - f * SpoilSeconds(cooked);
        }

        /// <summary>game seconds until the stack reaches its next stage (float.MaxValue for none)</summary>
        public static float SecondsToNextStage(ItemStack s)
        {
            if (s == null || !Spoils(s.item)) return float.MaxValue;
            double t = SpoilSeconds(s.item), age = System.Math.Max(0.0, GameClock.Now - s.madeAt);
            double next = age < t * C.agingAt ? t * C.agingAt : age < t ? t : double.MaxValue;
            return next == double.MaxValue ? float.MaxValue : (float)(next - age);
        }
    }
}
