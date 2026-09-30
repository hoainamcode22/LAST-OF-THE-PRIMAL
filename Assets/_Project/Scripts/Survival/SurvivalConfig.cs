using System;
using UnityEngine;
using PrimalFrontier.Items;

namespace PrimalFrontier.Survival
{
    /// <summary>
    /// Every survival tuning number in one asset (Resources/SurvivalConfig, made by PrimalSurvivalBuilder; a default
    /// instance is used when the asset is missing so nothing breaks). Code reads SurvivalConfig.Instance; no literals in
    /// gameplay classes. Designed for "pressure, not accounting": effects grow gradually as a need falls.
    /// </summary>
    [CreateAssetMenu(menuName = "Primal Frontier/Survival Config", fileName = "SurvivalConfig")]
    public class SurvivalConfig : ScriptableObject
    {
        /// <summary>one step of a need: applies while the need is below <see cref="below"/> (tiers sorted high to low)</summary>
        [Serializable]
        public struct NeedTier
        {
            [Tooltip("the tier applies while the need (0..100) is below this")] public float below;
            [Tooltip("stamina regeneration multiplier")] public float staminaRegen;
            [Tooltip("maximum stamina multiplier")] public float maxStamina;
            [Tooltip("move speed multiplier")] public float moveSpeed;
            [Tooltip("health lost per second")] public float healthDrain;
            [Tooltip("HUD word")] public string label;
            public NeedTier(float below, float regen, float max, float speed, float drain, string label)
            { this.below = below; staminaRegen = regen; maxStamina = max; moveSpeed = speed; healthDrain = drain; this.label = label; }
        }

        /// <summary>what drinking / boiling one kind of water does</summary>
        [Serializable]
        public struct WaterProfile
        {
            public WaterType type;
            [Tooltip("thirst per charge (negative = makes it worse)")] public float thirst;
            public float stamina;
            [Range(0, 1)] public float sickChance;
            public float sickSeconds;
            [Tooltip("seconds on a lit campfire to purify one container (0 = cannot / need not be boiled). Salt water never becomes drinkable, whatever this says (WaterRules)")] public float boilSeconds;
            public WaterType boilResult;
            [Tooltip("charges lost while boiling (salt boils down)")] public int boilChargeLoss;
            [Tooltip("HUD / inventory word")] public string label;
            public Color color;
            [Tooltip("GameEvents id when drunk / filled (journal, tutorial)")] public string eventId;
        }

        [Header("Needs: drain per real minute")]
        public float hungerPerMinute = 2.2f;          // ~45 min full -> empty
        public float thirstPerMinute = 3.2f;          // ~31 min
        [Header("Needs: start / respawn")]
        public float startHunger = 85f, startThirst = 70f;
        public float respawnNeedFloor = 40f;
        [Tooltip("health (0..1 of max), stamina and body temperature after a respawn")] public float respawnHealth = 0.6f, respawnStamina = 60f, respawnBodyTemp = 36.5f;
        [Tooltip("Stomach_Sick seconds when food with a sicknessChance (raw meat) makes you ill")] public float foodSickSeconds = 25f;
        [Header("Needs: tiers (high -> low). Start stats (85 / 70) must stay tier-free")]
        public NeedTier[] hungerTiers =
        {
            new NeedTier(60f, 0.85f, 1f, 1f, 0f, "Peckish"),
            new NeedTier(30f, 0.65f, 0.8f, 1f, 0f, "Hungry"),
            new NeedTier(10f, 0.5f, 0.65f, 0.92f, 0.12f, "Starving"),
            new NeedTier(0.01f, 0.4f, 0.6f, 0.9f, 0.25f, "Starving!"),
        };
        public NeedTier[] thirstTiers =
        {
            new NeedTier(60f, 0.85f, 1f, 1f, 0f, "Thirsty"),
            new NeedTier(30f, 0.6f, 0.8f, 1f, 0f, "Parched"),
            new NeedTier(10f, 0.45f, 0.65f, 0.92f, 0.15f, "Dehydrating"),
            new NeedTier(0.01f, 0.35f, 0.6f, 0.9f, 0.35f, "Dehydrated!"),
        };

        [Header("Water")]
        public WaterProfile[] water =
        {
            new WaterProfile { type = WaterType.SaltWater, thirst = -6f, stamina = 0f, sickChance = 0f, sickSeconds = 0f, boilSeconds = 0f, boilResult = WaterType.SaltWater, boilChargeLoss = 0, label = "salt water", color = new Color(0.55f, 0.78f, 0.92f), eventId = "salt_water" },
            new WaterProfile { type = WaterType.DirtyWater, thirst = 25f, stamina = 3f, sickChance = 0.2f, sickSeconds = 60f, boilSeconds = 8f, boilResult = WaterType.CleanWater, boilChargeLoss = 0, label = "unboiled water", color = new Color(0.72f, 0.6f, 0.38f), eventId = "dirty_water" },
            new WaterProfile { type = WaterType.CleanWater, thirst = 30f, stamina = 5f, sickChance = 0f, sickSeconds = 0f, boilSeconds = 0f, boilResult = WaterType.CleanWater, boilChargeLoss = 0, label = "clean water", color = new Color(0.45f, 0.75f, 1f), eventId = "clean_water" },
        };
        [Tooltip("drinking straight from the pond / stream (hands): thirst gained, sickness uses the dirty water profile")] public float handDrinkThirst = 28f;
        [Tooltip("millilitres in one charge (one drink); containers show their amount in ml")] [Min(1)] public int mlPerCharge = 250;

        [Header("Water temperature (phase 1): Cold from a source / after cooling, Hot just boiled")]
        [Tooltip("game-clock seconds boiled / heated water stays Hot before it is Cold again (sleep counts)")] [Min(1)] public float hotWaterCoolSeconds = 240f;
        [Tooltip("seconds on a lit campfire to heat clean Cold water (it needs no purifying); 0 = clean water cannot be reheated")] [Min(0)] public float heatWaterSeconds = 6f;
        [Tooltip("hot clean water in the cold / rain / night: body temperature raised at once by this, deg C (never above normal)")] public float hotDrinkBodyWarmth = 0.8f;
        [Tooltip("hot clean water in the cold / rain / night: the body feels this much warmer, deg C, for hotDrinkWarmSeconds")] public float hotDrinkWarmth = 6f;
        [Tooltip("seconds the warmth of a hot drink lasts")] [Min(0)] public float hotDrinkWarmSeconds = 120f;
        [Tooltip("felt air below this counts as cold for a hot drink, deg C (the body starts to cool below 18)")] public float hotDrinkColdAir = 18f;
        [Tooltip("slot / label colour of hot water")] public Color hotWaterColor = new Color(1f, 0.62f, 0.35f);

        [Header("Fire")]
        [Tooltip("burn seconds of a fuel item whose fuelSeconds is 0 but is the campfire's own fuel item (legacy wood)")] public float legacyFuelSeconds = 240f;
        public float maxFuelSeconds = 1200f;
        [Tooltip("food / water slots on one campfire")] [Range(1, 6)] public int cookingSlots = 4;
        [Tooltip("a cooked result burns after cookSeconds x this when the item has no burnSeconds")] public float burnAfterCookMultiplier = 1.5f;
        [Tooltip("food that burns turns into this (burnt_meat); empty = the food is lost")] public ItemDefinition burntFood;
        [Tooltip("multiplier on every fire's fuel use (1 = one fuel second per second)")] public float fuelBurnRate = 1f;
        [Tooltip("seconds of the Lighting state after lighting: the flames grow from small to full")] public float lightingSeconds = 3f;
        [Tooltip("the fire is LowFuel (smaller flames, a warning) below this many seconds of fuel")] public float lowFuelSeconds = 60f;
        [Tooltip("fuel seconds that give a full-strength fire; less fuel = weaker flames, light and heat")] public float fullIntensityFuelSeconds = 180f;
        [Tooltip("weakest a burning fire gets from low fuel (0..1)")] [Range(0, 1)] public float minFireIntensity = 0.3f;
        [Tooltip("cooking / boiling speed at the weakest fire (1 = no slowdown)")] [Range(0.1f, 1)] public float lowFireCookRate = 0.6f;
        [Tooltip("fire strength in normal rain on an uncovered fire (x intensity)")] [Range(0, 1)] public float rainFireIntensity = 0.7f;
        [Tooltip("fuel use in normal rain on an uncovered fire (x)")] public float rainFuelMultiplier = 1.5f;
        [Tooltip("weather intensity from which rain counts as heavy (WeatherManager: rain 0.75, storm 1)")] [Range(0, 1)] public float heavyRainIntensity = 0.9f;
        [Tooltip("fuel use in heavy rain on an uncovered fire (x)")] public float heavyRainFuelMultiplier = 2f;
        [Tooltip("fire strength in heavy rain on an uncovered fire (x intensity)")] [Range(0, 1)] public float heavyRainFireIntensity = 0.5f;
        [Tooltip("seconds of heavy rain that put out an uncovered fire (x0.5 when the fuel is low); 0 = never")] public float heavyRainExtinguishSeconds = 45f;
        [Tooltip("a fire that burns out after burning this many seconds leaves 1 charcoal (2 after twice as long); 0 = never")] [Min(0)] public float charcoalAfterBurnSeconds = 300f;
        [Tooltip("most charcoal one burnt-out fire leaves")] [Range(0, 5)] public int charcoalMax = 2;

        [Header("Rain collector")]
        public int collectorCapacity = 6;
        [Tooltip("charges collected per in-game hour of rain (at rain intensity 0.75)")] public float collectorChargesPerRainHour = 2f;

        [Header("Temperature")]
        [Tooltip("air is this much colder at night (blended over dusk / dawn), on top of TimeManager")] public float nightAirOffset = -4f;
        [Tooltip("hours over which the night offset fades in around sunset and out around sunrise")] public float nightBlendHours = 2f;
        public float torchWarmth = 2f;
        [Tooltip("air cools by altitudeCooling deg C per metre above altitudeCoolingStart")] public float altitudeCoolingStart = 30f, altitudeCooling = 0.06f;

        [Header("Wetness / sun (per second)")]
        [Tooltip("wetness gained in the rain")] public float rainWetPerSecond = 0.04f;
        [Tooltip("wetness gained while standing in water (sea, pond, stream), at 0.8 m depth")] public float waterWetPerSecond = 0.3f;
        [Tooltip("air felt this much colder while standing in water, deg C")] public float waterChill = 3f;
        [Tooltip("drying in the open")] public float dryPerSecond = 0.01f;
        [Tooltip("extra drying per deg C of fire / shelter warmth")] public float heatDryPerDegree = 0.004f;
        [Tooltip("extra drying under a roof")] public float shelterDryPerSecond = 0.01f;
        [Tooltip("extra drying in full sun")] public float sunDryPerSecond = 0.006f;
        [Tooltip("warmer in full sun than in the shade, deg C (clear midday; clouds and evening lower it)")] public float sunWarmth = 2.5f;
        [Tooltip("the Wet status shows above this wetness (0..1)")] [Range(0, 1)] public float wetStatusAbove = 0.3f;
        [Tooltip("drying x (1 - this x humidity): wetland / waterfall air (humidity 0.95, WORLD's zones) dries you about half as fast")] [Range(0, 1)] public float humidityDryingPenalty = 0.5f;

        [Header("Injuries (PlayerHealth damage path)")]
        [Tooltip("one hit of this much damage opens a deep wound")] public float deepWoundDamage = 30f;
        [Tooltip("a deep wound bleeds this long unless bandaged, s")] public float deepWoundSeconds = 75f;
        [Tooltip("a cut while already bleeding becomes a deep wound")] public bool repeatCutDeepens = true;
        [Tooltip("heavy hits from this damage may injure a leg or an arm")] public float injuryMinDamage = 25f;
        [Range(0, 1)] public float injuryChance = 0.6f;
        [Tooltip("share of limb injuries that hit the leg (the rest: the arm)")] [Range(0, 1)] public float legInjuryShare = 0.5f;
        [Tooltip("seconds a limb injury lasts (sleep and rest shorten it)")] public float injurySeconds = 300f;
        [Tooltip("landing faster than this (m/s) hurts the leg; 0 = never")] public float fallInjurySpeed = 13f;

        [Header("Healing")]
        [Tooltip("health regeneration x this while under a roof or by a lit fire")] public float restRegenMultiplier = 1.75f;
        [Tooltip("all healing (natural regeneration, Recovering) x this at hunger 0, x1 at hunger 100 (linear): an empty stomach heals slowly")] [Range(0, 1)] public float healAtEmptyHunger = 0.25f;
        [Tooltip("all healing x this at thirst 0, x1 at 100")] [Range(0, 1)] public float healAtEmptyThirst = 0.5f;
        [Tooltip("limb injuries heal this many times faster while resting under a roof or by a fire")] public float restInjuryHealMultiplier = 2f;

        [Header("Food spoilage (ItemDefinition.spoilHours; stages by the part of that time gone)")]
        [Tooltip("food is Aging from this part of its spoil time (0..1)")] [Range(0, 1)] public float agingAt = 0.5f;
        [Tooltip("food value of aging food (x hunger / thirst / health)")] [Range(0, 1)] public float agingNutrition = 0.8f;
        [Tooltip("food value of spoiled food")] [Range(0, 1)] public float spoiledNutrition = 0.35f;
        [Tooltip("extra sickness chance of aging food")] [Range(0, 1)] public float agingSickChance = 0.05f;
        [Tooltip("sickness chance of spoiled food (at least)")] [Range(0, 1)] public float spoiledSickChance = 0.5f;
        [Tooltip("food poisoning seconds from spoiled food")] public float spoiledSickSeconds = 60f;
        [Tooltip("seconds between spoilage checks of the pack (no per-item update)")] public float spoilCheckSeconds = 5f;

        [Header("Sleep (per in-game hour asleep)")]
        public float sleepHungerPerHour = 2.2f, sleepThirstPerHour = 2.8f, sleepHealthPerHour = 4f;
        public float sleepNeedFloor = 5f;

        [Header("Item references (no literal ids in code)")]
        public ItemDefinition wood;
        public ItemDefinition rawMeat;
        public ItemDefinition cookedMeat;
        [Tooltip("left by a campfire that burned out (phase 1); empty = looked up by id 'charcoal'")] public ItemDefinition charcoal;

        // ------------------------------------------------------------------ access
        static SurvivalConfig _instance;
        /// <summary>Resources/SurvivalConfig, or a default instance with the values above</summary>
        public static SurvivalConfig Instance
        {
            get
            {
                if (_instance) return _instance;
                _instance = Resources.Load<SurvivalConfig>("SurvivalConfig");
                if (!_instance) { _instance = CreateInstance<SurvivalConfig>(); _instance.name = "SurvivalConfig (default)"; _instance.hideFlags = HideFlags.DontSave; }
                return _instance;
            }
        }
        /// <summary>tests / tools: force a specific config (null = reload from Resources)</summary>
        public static void Override(SurvivalConfig c) { _instance = c; }

        public WaterProfile Water(WaterType t)
        {
            if (water != null) for (int i = 0; i < water.Length; i++) if (water[i].type == t) return water[i];
            return new WaterProfile { type = t, label = "water", color = Color.white, boilResult = t };
        }

        /// <summary>the lowest tier the need has fallen into; false = no tier (fine)</summary>
        public static bool TierFor(NeedTier[] tiers, float value, out NeedTier tier, out int index)
        {
            tier = default; index = -1;
            if (tiers == null) return false;
            for (int i = 0; i < tiers.Length; i++) if (value < tiers[i].below) { tier = tiers[i]; index = i; }
            return index >= 0;
        }
    }
}
