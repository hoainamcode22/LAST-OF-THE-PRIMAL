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
            [Tooltip("seconds on a lit campfire to purify one container (0 = cannot / need not be boiled)")] public float boilSeconds;
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
            new WaterProfile { type = WaterType.SaltWater, thirst = -6f, stamina = 0f, sickChance = 0f, sickSeconds = 0f, boilSeconds = 20f, boilResult = WaterType.CleanWater, boilChargeLoss = 1, label = "salt water", color = new Color(0.55f, 0.78f, 0.92f), eventId = "salt_water" },
            new WaterProfile { type = WaterType.DirtyWater, thirst = 25f, stamina = 3f, sickChance = 0.2f, sickSeconds = 60f, boilSeconds = 8f, boilResult = WaterType.CleanWater, boilChargeLoss = 0, label = "unboiled water", color = new Color(0.72f, 0.6f, 0.38f), eventId = "dirty_water" },
            new WaterProfile { type = WaterType.CleanWater, thirst = 30f, stamina = 5f, sickChance = 0f, sickSeconds = 0f, boilSeconds = 0f, boilResult = WaterType.CleanWater, boilChargeLoss = 0, label = "clean water", color = new Color(0.45f, 0.75f, 1f), eventId = "clean_water" },
        };
        [Tooltip("drinking straight from the pond / stream (hands): thirst gained, sickness uses the dirty water profile")] public float handDrinkThirst = 28f;

        [Header("Fire")]
        [Tooltip("burn seconds of a fuel item whose fuelSeconds is 0 but is the campfire's own fuel item (legacy wood)")] public float legacyFuelSeconds = 240f;
        public float maxFuelSeconds = 1200f;
        [Tooltip("food / water slots on one campfire")] [Range(1, 6)] public int cookingSlots = 4;
        [Tooltip("a cooked result burns after cookSeconds x this when the item has no burnSeconds")] public float burnAfterCookMultiplier = 1.5f;
        [Tooltip("food that burns turns into this (burnt_meat); empty = the food is lost")] public ItemDefinition burntFood;

        [Header("Rain collector")]
        public int collectorCapacity = 6;
        [Tooltip("charges collected per in-game hour of rain (at rain intensity 0.75)")] public float collectorChargesPerRainHour = 2f;

        [Header("Temperature")]
        [Tooltip("air is this much colder at night (blended over dusk / dawn), on top of TimeManager")] public float nightAirOffset = -4f;
        [Tooltip("hours over which the night offset fades in around sunset and out around sunrise")] public float nightBlendHours = 2f;
        public float torchWarmth = 2f;
        [Tooltip("air cools by altitudeCooling deg C per metre above altitudeCoolingStart")] public float altitudeCoolingStart = 30f, altitudeCooling = 0.06f;

        [Header("Sleep (per in-game hour asleep)")]
        public float sleepHungerPerHour = 2.2f, sleepThirstPerHour = 2.8f, sleepHealthPerHour = 4f;
        public float sleepNeedFloor = 5f;

        [Header("Item references (no literal ids in code)")]
        public ItemDefinition wood;
        public ItemDefinition rawMeat;
        public ItemDefinition cookedMeat;

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
