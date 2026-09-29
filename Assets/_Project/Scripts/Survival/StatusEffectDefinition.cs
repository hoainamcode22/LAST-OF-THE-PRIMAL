using System;
using System.Collections.Generic;
using UnityEngine;

namespace PrimalFrontier.Survival
{
    /// <summary>ids of the built-in status effects (assets Resources/StatusEffects/SE_&lt;id&gt;; saved by id, never rename)</summary>
    public static class StatusEffectIds
    {
        public const string Bleeding = "bleeding", LegInjury = "leg_injury", ArmInjury = "arm_injury", Sickness = "sickness",
                            Wet = "wet", Cold = "cold", Recovering = "recovering";
    }

    /// <summary>
    /// One kind of status effect on the player (bleeding, leg / arm injury, food poisoning, wet, cold, recovering): how long
    /// it lasts, how a second application stacks, what it does while active (health per second, speed / stamina / thirst /
    /// attack multipliers, blocks natural regeneration) and the HUD icon. Assets live in Resources/StatusEffects (made by
    /// PrimalSurvivalBuilder with the values below); a missing asset falls back to a built-in default with the same values,
    /// so nothing breaks. Multipliers are the value at severity 1; severity s scales the difference from 1 (1 + (m - 1) x s).
    /// </summary>
    [CreateAssetMenu(menuName = "Primal Frontier/Status Effect", fileName = "SE_New")]
    public class StatusEffectDefinition : ScriptableObject
    {
        public enum Stacking
        {
            /// <summary>keep the longer remaining time and the higher severity</summary>
            Refresh,
            /// <summary>add the new seconds to the remaining time (up to maxSeconds)</summary>
            Extend,
            /// <summary>add the severities (up to maxSeverity), keep the longer time</summary>
            AddSeverity,
        }

        [Header("Identity")]
        public string id = "effect";
        public string displayName = "Effect";
        [TextArea(1, 3)] public string description;
        [Tooltip("HUD icon; empty = Resources/UI/icon_<iconName>.png")] public Sprite icon;
        public string iconName;
        public Color color = Color.white;
        public bool showOnHud = true;
        [Tooltip("lower comes first in the HUD row")] public int hudOrder;
        [Tooltip("kept in the save file (derived states such as wet / cold are not)")] public bool saved = true;

        [Header("Duration")]
        [Tooltip("seconds when applied without a time (0 = until removed)")] [Min(0)] public float defaultSeconds = 30f;
        [Tooltip("cap for Extend / Refresh (0 = no cap)")] [Min(0)] public float maxSeconds;
        [Min(0.01f)] public float maxSeverity = 1f;
        public Stacking stacking = Stacking.Refresh;

        [Header("While active (at severity 1)")]
        [Tooltip("health per second (negative drains, positive heals)")] public float healthPerSecond;
        public float moveSpeedMultiplier = 1f;
        public float sprintCostMultiplier = 1f;
        public float staminaRegenMultiplier = 1f;
        public float maxStaminaMultiplier = 1f;
        public float thirstMultiplier = 1f;
        public float hungerMultiplier = 1f;
        [Tooltip("damage of the player's attacks (combat reads PlayerStatusEffects.AttackMultiplier)")] public float attackMultiplier = 1f;
        [Tooltip("gather speed / yield (resource systems read PlayerStatusEffects.GatherMultiplier)")] public float gatherMultiplier = 1f;
        [Tooltip("no natural health regeneration while active")] public bool blocksHealthRegen;

        [Header("Messages (empty = none)")]
        public string appliedMessage;
        public string curedMessage;
        public string expiredMessage;

        /// <summary>multiplier m at severity s: 1 + (m - 1) x s, never below 0.05</summary>
        public static float Scaled(float m, float s) => Mathf.Max(0.05f, 1f + (m - 1f) * Mathf.Max(0f, s));

        // ------------------------------------------------------------------ registry
        static Dictionary<string, StatusEffectDefinition> _all;

        /// <summary>the definition for an id: the Resources/StatusEffects asset, else the built-in default (null for an unknown id)</summary>
        public static StatusEffectDefinition Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_all == null) Load();
            if (_all.TryGetValue(id, out var d) && d) return d;
            d = BuiltIn(id);
            if (d) _all[id] = d;
            return d;
        }

        /// <summary>every known definition (assets + built-ins)</summary>
        public static IEnumerable<StatusEffectDefinition> All()
        {
            foreach (var id in BuiltInIds) Get(id);
            return _all.Values;
        }

        /// <summary>tools / tests: forget the loaded definitions (next Get reloads the assets)</summary>
        public static void ClearCache() { _all = null; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() => _all = null;

        static void Load()
        {
            _all = new Dictionary<string, StatusEffectDefinition>();
            foreach (var d in Resources.LoadAll<StatusEffectDefinition>("StatusEffects"))
                if (d && !string.IsNullOrEmpty(d.id) && !_all.ContainsKey(d.id)) _all[d.id] = d;
        }

        public static readonly string[] BuiltInIds =
        {
            StatusEffectIds.Bleeding, StatusEffectIds.LegInjury, StatusEffectIds.ArmInjury, StatusEffectIds.Sickness,
            StatusEffectIds.Wet, StatusEffectIds.Cold, StatusEffectIds.Recovering,
        };

        /// <summary>a fresh definition with the design values for a built-in id (the builder writes these into the assets)</summary>
        public static StatusEffectDefinition BuiltIn(string id)
        {
            if (Array.IndexOf(BuiltInIds, id) < 0) return null;
            var d = CreateInstance<StatusEffectDefinition>();
            d.id = id; d.name = "SE_" + id; d.iconName = "status_" + id;
            switch (id)
            {
                case StatusEffectIds.Bleeding:
                    d.displayName = "Bleeding"; d.color = new Color(0.86f, 0.2f, 0.16f); d.hudOrder = 0;
                    d.description = "Loses health until the wound closes or is bandaged.";
                    d.defaultSeconds = 20f; d.maxSeconds = 180f; d.stacking = Stacking.Refresh;
                    d.healthPerSecond = -0.8f; d.blocksHealthRegen = true;
                    d.appliedMessage = "You are bleeding. A bandage will stop it.";
                    d.curedMessage = "The bandage stops the bleeding.";
                    d.expiredMessage = "The bleeding has stopped.";
                    break;
                case StatusEffectIds.LegInjury:
                    d.displayName = "Leg injury"; d.color = new Color(0.9f, 0.55f, 0.25f); d.hudOrder = 1;
                    d.description = "Slower, and running tires you faster. Heals with time, rest and sleep.";
                    d.defaultSeconds = 300f; d.maxSeconds = 600f; d.stacking = Stacking.Extend;
                    d.moveSpeedMultiplier = 0.82f; d.sprintCostMultiplier = 1.5f;
                    d.appliedMessage = "Your leg is hurt. You move slower until it heals. Rest or sleep.";
                    d.expiredMessage = "Your leg feels better.";
                    break;
                case StatusEffectIds.ArmInjury:
                    d.displayName = "Arm injury"; d.color = new Color(0.9f, 0.55f, 0.25f); d.hudOrder = 2;
                    d.description = "Weaker attacks and slower work. Heals with time, rest and sleep.";
                    d.defaultSeconds = 300f; d.maxSeconds = 600f; d.stacking = Stacking.Extend;
                    d.attackMultiplier = 0.7f; d.gatherMultiplier = 0.8f;
                    d.appliedMessage = "Your arm is hurt. Your blows are weaker until it heals.";
                    d.expiredMessage = "Your arm feels better.";
                    break;
                case StatusEffectIds.Sickness:
                    d.displayName = "Food poisoning"; d.color = new Color(0.55f, 0.72f, 0.25f); d.hudOrder = 3;
                    d.description = "Stomach sick: thirstier, tires quicker, slowly loses health. Passes with time.";
                    d.defaultSeconds = 60f; d.maxSeconds = 300f; d.stacking = Stacking.Refresh;
                    d.healthPerSecond = -0.3f; d.thirstMultiplier = 1.6f; d.staminaRegenMultiplier = 0.5f; d.maxStaminaMultiplier = 0.85f;
                    d.blocksHealthRegen = true;
                    d.expiredMessage = "Your stomach has settled.";
                    break;
                case StatusEffectIds.Wet:
                    d.displayName = "Wet"; d.color = new Color(0.45f, 0.7f, 0.95f); d.hudOrder = 5;
                    d.description = "Wet clothes chill you. Dry off by a fire or under a roof.";
                    d.defaultSeconds = 0f; d.saved = false;
                    break;
                case StatusEffectIds.Cold:
                    d.displayName = "Cold"; d.color = new Color(0.6f, 0.82f, 1f); d.hudOrder = 4;
                    d.description = "Your body is cooling. Find fire, shelter or dry clothes.";
                    d.defaultSeconds = 0f; d.saved = false;
                    break;
                case StatusEffectIds.Recovering:
                    d.displayName = "Recovering"; d.color = new Color(0.55f, 0.78f, 0.42f); d.hudOrder = 6;
                    d.description = "Good food or a dressed wound: health comes back slowly.";
                    d.defaultSeconds = 20f; d.maxSeconds = 240f; d.stacking = Stacking.Extend;
                    d.healthPerSecond = 0.25f;
                    break;
            }
            d.hideFlags = HideFlags.DontSave;
            return d;
        }
    }
}
