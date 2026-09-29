using UnityEngine;
using PrimalFrontier.Core;
using PrimalFrontier.Items;

namespace PrimalFrontier.Survival
{
    /// <summary>
    /// The only place the water rules live (fill, empty, drink, boil), keyed by WaterType with the numbers from
    /// SurvivalConfig.water. Sources: pond / stream = DirtyWater, sea = SaltWater, rain collector = CleanWater, boiling on
    /// a lit campfire turns salt / dirty water clean (salt boils down by a charge). No allocations.
    /// </summary>
    public static class WaterRules
    {
        static SurvivalConfig C => SurvivalConfig.Instance;

        /// <summary>what is in the container (old saves: charges without a type count as clean, the dirty flag as dirty)</summary>
        public static WaterType TypeOf(ItemStack s)
        {
            if (s == null || s.water <= 0) return WaterType.None;
            return s.waterType == WaterType.None ? WaterType.CleanWater : s.waterType;
        }

        public static bool IsContainer(ItemStack s) => s != null && !s.IsEmpty && s.item.IsWaterContainer;
        public static int Capacity(ItemStack s) => IsContainer(s) ? s.item.waterCharges : 0;
        public static bool IsFull(ItemStack s) => IsContainer(s) && s.water >= s.item.waterCharges;
        public static string Label(WaterType t) => t == WaterType.None ? "empty" : C.Water(t).label;

        // ------------------------------------------------------------------ amounts in ml (charges stay the rule unit)
        /// <summary>millilitres of this many charges (SurvivalConfig.mlPerCharge each)</summary>
        public static int Ml(int charges) => Mathf.Max(0, charges) * Mathf.Max(1, C.mlPerCharge);
        static readonly System.Collections.Generic.Dictionary<int, string> _mlShort = new System.Collections.Generic.Dictionary<int, string>(), _mlLong = new System.Collections.Generic.Dictionary<int, string>();
        /// <summary>"500ml" (slot count text; cached, no allocation after the first use of an amount)</summary>
        public static string MlShort(int charges)
        {
            int ml = Ml(charges);
            if (!_mlShort.TryGetValue(ml, out var t)) _mlShort[ml] = t = ml.ToString(System.Globalization.CultureInfo.InvariantCulture) + "ml";
            return t;
        }
        /// <summary>"500 / 750 ml"</summary>
        public static string MlOf(ItemStack s)
        {
            if (!IsContainer(s)) return "";
            int key = s.water * 1000 + s.item.waterCharges;
            if (!_mlLong.TryGetValue(key, out var t))
                _mlLong[key] = t = Ml(s.water).ToString(System.Globalization.CultureInfo.InvariantCulture) + " / " + Ml(s.item.waterCharges).ToString(System.Globalization.CultureInfo.InvariantCulture) + " ml";
            return t;
        }
        public static Color Color(WaterType t) => t == WaterType.None ? UnityEngine.Color.white : C.Water(t).color;

        /// <summary>water mixes only with the same kind (empty first to change it)</summary>
        public static bool CanFill(ItemStack s, WaterType t)
        {
            if (!IsContainer(s) || t == WaterType.None || IsFull(s)) return false;
            var cur = TypeOf(s);
            return cur == WaterType.None || cur == t;
        }

        /// <summary>adds up to <paramref name="charges"/> of this water; returns the charges added</summary>
        public static int Fill(ItemStack s, WaterType t, int charges = int.MaxValue)
        {
            if (!CanFill(s, t)) return 0;
            int add = Mathf.Min(charges, s.item.waterCharges - s.water);
            if (add <= 0) return 0;
            s.water += add; s.waterType = t;
            GameEvents.Raise(GameEventType.WaterFilled, C.Water(t).eventId, add);
            FillSound();
            return add;
        }

        /// <summary>the WaterFill sound at the player (fills happen in the player's hands)</summary>
        static void FillSound()
        {
            if (!Application.isPlaying) return;
            var p = World.PlayerLocator.Position;
            Audio.SfxPlayer.Instance.Play(Audio.SfxId.WaterFill, p.HasValue ? p.Value + Vector3.up * 0.6f : Vector3.zero, 0.8f, p.HasValue ? 1f : 0f);
        }

        public static void Empty(ItemStack s)
        {
            if (!IsContainer(s)) return;
            s.water = 0; s.waterType = WaterType.None;
        }

        /// <summary>one charge from a container; false when empty. Effects as <see cref="ApplyDrink"/>.</summary>
        public static bool Drink(PlayerSurvival who, ItemStack s)
        {
            if (!who || !IsContainer(s) || s.water <= 0) return false;
            var t = TypeOf(s);
            s.water--;
            if (s.water <= 0) { s.water = 0; s.waterType = WaterType.None; }
            ApplyDrink(who, t);
            return true;
        }

        /// <summary>thirst / stamina / sickness roll of one drink of this water (also used by sources and collectors)</summary>
        public static void ApplyDrink(PlayerSurvival who, WaterType t, float thirstOverride = float.NaN)
        {
            if (!who || t == WaterType.None) return;
            var p = C.Water(t);
            who.Consume(0f, float.IsNaN(thirstOverride) ? p.thirst : thirstOverride, 0f, p.stamina);
            if (p.sickChance > 0f && Random.value < p.sickChance) who.MakeSick(p.sickSeconds, "Your stomach turns. That water was not boiled.");
            if (t == WaterType.SaltWater) GameEvents.Raise(GameEventType.TriedSaltWater, p.eventId, 1, who.transform.position);
            else GameEvents.Raise(GameEventType.Drank, p.eventId, 1, who.transform.position);
        }

        // ------------------------------------------------------------------ boiling
        public static bool NeedsBoiling(ItemStack s) { var t = TypeOf(s); return t != WaterType.None && C.Water(t).boilSeconds > 0f && C.Water(t).boilResult != t; }
        public static float BoilSeconds(ItemStack s) => NeedsBoiling(s) ? C.Water(TypeOf(s)).boilSeconds : 0f;

        /// <summary>finish boiling: the water becomes its boil result, salt loses charges (at least one stays)</summary>
        public static void CompleteBoil(ItemStack s)
        {
            if (!NeedsBoiling(s)) return;
            var p = C.Water(TypeOf(s));
            s.water = Mathf.Max(1, s.water - p.boilChargeLoss);
            s.waterType = p.boilResult;
            GameEvents.Raise(GameEventType.WaterBoiled, C.Water(p.boilResult).eventId, s.water);
        }
    }
}
