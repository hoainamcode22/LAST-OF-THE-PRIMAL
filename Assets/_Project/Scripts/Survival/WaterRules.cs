using UnityEngine;
using PrimalFrontier.Core;
using PrimalFrontier.Items;

namespace PrimalFrontier.Survival
{
    /// <summary>
    /// The only place the water rules live (fill, empty, drink, boil), keyed by WaterType with the numbers from
    /// SurvivalConfig.water. Sources: pond / stream = DirtyWater, sea = SaltWater, rain collector = CleanWater, boiling on
    /// a lit campfire turns dirty water clean. Salt water is never drinkable: boiling it is refused (<see cref="SaltBoilMessage"/>),
    /// whatever the config says. No allocations.
    /// Phase 1: temperature on the stack (ItemStack.hotUntil). Water from a source is Cold; boiling (or heating clean water,
    /// SurvivalConfig.heatWaterSeconds) makes it Hot for hotWaterCoolSeconds of game clock, then it is Cold again. Hot clean
    /// water warms the body when it is cold, raining or night (<see cref="ApplyDrink"/>); otherwise it is a normal drink.
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

        // ------------------------------------------------------------------ temperature (phase 1)
        /// <summary>the water in this container is Hot (boiled / heated, not cooled yet)</summary>
        public static bool IsHot(ItemStack s) => s != null && s.water > 0 && s.hotUntil > GameClock.Now;
        /// <summary>game-clock seconds until the water is Cold (0 = cold / empty)</summary>
        public static float HotSecondsLeft(ItemStack s) => IsHot(s) ? (float)(s.hotUntil - GameClock.Now) : 0f;
        /// <summary>just boiled / heated: Hot for SurvivalConfig.hotWaterCoolSeconds</summary>
        public static void MakeHot(ItemStack s) { if (IsContainer(s) && s.water > 0) s.hotUntil = GameClock.Now + Mathf.Max(1f, C.hotWaterCoolSeconds); }
        public static void MakeCold(ItemStack s) { if (s != null) s.hotUntil = 0.0; }
        /// <summary>save / load: Hot for this many more seconds (0, NaN or no water = Cold)</summary>
        public static void RestoreHot(ItemStack s, float secondsLeft)
        {
            if (s == null) return;
            s.hotUntil = s.water > 0 && secondsLeft > 0f && !float.IsNaN(secondsLeft) && !float.IsInfinity(secondsLeft)
                ? GameClock.Now + Mathf.Min(secondsLeft, Mathf.Max(1f, C.hotWaterCoolSeconds)) : 0.0;
        }
        public static string TempWord(ItemStack s) => IsHot(s) ? "Hot" : "Cold";
        static readonly string[] _names = new string[16], _nameLabels = new string[16];
        /// <summary>"Clean Water (Hot)", "Salt Water (Cold)", "Empty" (cached per type and temperature)</summary>
        public static string NameOf(ItemStack s)
        {
            var t = TypeOf(s);
            if (t == WaterType.None) return "Empty";
            int k = ((int)t & 7) * 2 + (IsHot(s) ? 1 : 0);
            string label = Label(t);
            if (_names[k] == null || !ReferenceEquals(_nameLabels[k], label))
            {
                var ti = System.Globalization.CultureInfo.InvariantCulture.TextInfo;
                _names[k] = ti.ToTitleCase(label ?? "water") + ((k & 1) == 1 ? " (Hot)" : " (Cold)");
                _nameLabels[k] = label;
            }
            return _names[k];
        }
        /// <summary>slot / label colour: hot water glows warm, otherwise the water type colour</summary>
        public static Color ColorOf(ItemStack s) => IsHot(s) ? C.hotWaterColor : Color(TypeOf(s));

        // ------------------------------------------------------------------ salt (phase 1: the ocean is never drinkable)
        public const string SaltBoilMessage = "Boiling does not remove salt. Sea water is never safe to drink: fill up at a pond, the stream or a rain collector.";
        public static bool IsSalt(ItemStack s) => TypeOf(s) == WaterType.SaltWater;
        /// <summary>boiling this kind of water makes it safe (dirty water); never salt water</summary>
        public static bool CanPurify(WaterType t)
        {
            if (t == WaterType.None || t == WaterType.SaltWater) return false;
            var p = C.Water(t);
            return p.boilSeconds > 0f && p.boilResult != t && p.boilResult != WaterType.SaltWater && p.boilResult != WaterType.None;
        }
        /// <summary>" (boil it before drinking)", " (not drinkable: boiling does not remove salt)" or "" for fill prompts</summary>
        public static string FillAdvice(WaterType t) => t == WaterType.SaltWater ? " (not drinkable: boiling does not remove salt)" : CanPurify(t) ? " (boil it before drinking)" : "";

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
            MakeCold(s);                                   // water from a source is Cold (and cools what was in it)
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
            s.water = 0; s.waterType = WaterType.None; s.hotUntil = 0.0;
        }

        /// <summary>one charge from a container; false when empty. Effects as <see cref="ApplyDrink"/>.</summary>
        public static bool Drink(PlayerSurvival who, ItemStack s)
        {
            if (!who || !IsContainer(s) || s.water <= 0) return false;
            var t = TypeOf(s);
            bool hot = IsHot(s);
            s.water--;
            if (s.water <= 0) { s.water = 0; s.waterType = WaterType.None; s.hotUntil = 0.0; }
            ApplyDrink(who, t, float.NaN, hot);
            return true;
        }

        /// <summary>
        /// thirst / stamina / sickness roll of one drink of this water (also used by sources and collectors). Salt water keeps
        /// its thirst penalty. <paramref name="hot"/> clean water also warms the body when it is cold, raining or night
        /// (<see cref="WarmsNow"/>); otherwise it is a normal drink.
        /// </summary>
        public static void ApplyDrink(PlayerSurvival who, WaterType t, float thirstOverride = float.NaN, bool hot = false)
        {
            if (!who || t == WaterType.None) return;
            var p = C.Water(t);
            who.Consume(0f, float.IsNaN(thirstOverride) ? p.thirst : thirstOverride, 0f, p.stamina);
            if (hot && t == WaterType.CleanWater && WarmsNow(who))
            {
                who.WarmFromDrink(C.hotDrinkBodyWarmth, C.hotDrinkWarmth, C.hotDrinkWarmSeconds);
                if (Application.isPlaying) PrimalFrontier.Player.PlayerInteraction.Notify("The hot water warms you through.");
            }
            if (p.sickChance > 0f && Random.value < p.sickChance) who.MakeSick(p.sickSeconds, "Your stomach turns. That water was not boiled.");
            if (t == WaterType.SaltWater) GameEvents.Raise(GameEventType.TriedSaltWater, p.eventId, 1, who.transform.position);
            else GameEvents.Raise(GameEventType.Drank, p.eventId, 1, who.transform.position);
        }

        /// <summary>a hot drink warms now: the body is cold, the felt air is cold (SurvivalConfig.hotDrinkColdAir), it rains on the player, or it is night</summary>
        public static bool WarmsNow(PlayerSurvival who)
        {
            if (!who) return false;
            if (who.IsCold || who.EnvironmentTemperature < C.hotDrinkColdAir) return true;
            if (PlayerSurvival.RainingAt != null && PlayerSurvival.RainingAt(who.transform.position)) return true;
            var tm = TimeManager.Instance;
            return tm && tm.IsNight;
        }

        // ------------------------------------------------------------------ boiling
        /// <summary>needs boiling to be safe (dirty water). Salt water never: boiling does not remove salt</summary>
        public static bool NeedsBoiling(ItemStack s) => CanPurify(TypeOf(s));
        /// <summary>clean Cold water can be heated on the fire (a hot drink), SurvivalConfig.heatWaterSeconds</summary>
        public static bool CanHeat(ItemStack s) => TypeOf(s) == WaterType.CleanWater && !IsHot(s) && C.heatWaterSeconds > 0f;
        /// <summary>what a lit campfire accepts: water to purify or clean water to heat (never salt water)</summary>
        public static bool CanBoil(ItemStack s) => IsContainer(s) && (NeedsBoiling(s) || CanHeat(s));
        public static float BoilSeconds(ItemStack s) => NeedsBoiling(s) ? C.Water(TypeOf(s)).boilSeconds : CanHeat(s) ? C.heatWaterSeconds : 0f;

        /// <summary>finish boiling: dirty water becomes its boil result (charges lost as configured, at least one stays), clean
        /// water is heated; both are Hot afterwards. Salt water is left as it is</summary>
        public static void CompleteBoil(ItemStack s)
        {
            if (NeedsBoiling(s))
            {
                var p = C.Water(TypeOf(s));
                s.water = Mathf.Max(1, s.water - p.boilChargeLoss);
                s.waterType = p.boilResult;
                MakeHot(s);
                GameEvents.Raise(GameEventType.WaterBoiled, C.Water(p.boilResult).eventId, s.water);
            }
            else if (TypeOf(s) == WaterType.CleanWater) MakeHot(s);
        }
    }
}
