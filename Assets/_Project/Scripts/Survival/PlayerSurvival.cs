using System;
using UnityEngine;
using PrimalFrontier.Core;
using PrimalFrontier.Player;

namespace PrimalFrontier.Survival
{
    /// <summary>
    /// Hunger, thirst, stamina, body temperature, wetness and food sickness. Pays for sprinting (IStaminaSource) and
    /// hurts / heals through PlayerHealth. Tuned for "survival feeling, not accounting": slow hunger, faster thirst,
    /// real danger only when a stat is empty.
    /// </summary>
    [RequireComponent(typeof(PlayerHealth))]
    public class PlayerSurvival : MonoBehaviour, IStaminaSource
    {
        [Header("Hunger / thirst (per real minute)")]
        public float hungerPerMinute = 2.4f;       // ~40 min from full to empty
        public float thirstPerMinute = 3.8f;       // ~26 min
        public float sprintMultiplier = 1.8f;
        [Header("Stamina")]
        public float maxStamina = 100f;
        public float sprintCostPerSecond = 16f;
        public float regenPerSecond = 14f;
        public float regenDelay = 0.9f;
        public float jumpCost = 8f;
        [Header("Temperature (deg C)")]
        public float normalBody = 37f;
        public float coldBody = 35.2f;
        public float freezingBody = 34.2f;
        public float bodyResponse = 0.02f;         // how fast the body follows the environment
        [Header("Damage / heal (HP per second)")]
        public float starveDamage = 0.25f;
        public float dehydrateDamage = 0.35f;
        public float freezeDamage = 0.2f;
        public float sickDamage = 0.3f;
        [Tooltip("while sick (Stomach_Sick): thirst drains faster, stamina comes back slower")] public float sickThirstMultiplier = 1.6f, sickStaminaRegen = 0.5f;
        public float regenHealth = 0.12f;

        public float Hunger { get; private set; } = 85f;       // 100 = full
        public float Thirst { get; private set; } = 70f;
        public float Stamina { get; private set; } = 100f;
        public float BodyTemperature { get; private set; } = 37f;
        public float Wetness { get; private set; }               // 0..1
        public float EnvironmentTemperature { get; private set; } = 24f;
        public float SickSeconds { get; private set; }
        public bool IsCold => BodyTemperature < coldBody;
        public bool IsFreezing => BodyTemperature < freezingBody;
        public bool IsStarving => Hunger <= 0.01f;
        public bool IsDehydrated => Thirst <= 0.01f;
        public float MaxStaminaNow => maxStamina * Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(Mathf.Min(Hunger, Thirst) / 25f));
        /// <summary>external: carried weight too high (set by the player each frame)</summary>
        public bool Overweight { get; set; }
        /// <summary>survival stats paused (intro, menus that stop time, tests)</summary>
        public bool Paused { get; set; }

        public event Action<string> Warning;      // "You are thirsty." etc. (HUD)
        PlayerHealth _hp; float _lastStaminaUse = -10f; int _warnMask;

        void Awake() { _hp = GetComponent<PlayerHealth>(); }
        void Start()
        {
            var m = GetComponent<PlayerMotor>(); if (m) { m.Stamina = this; m.Jumped += OnJumped; }
        }
        void OnDestroy() { var m = GetComponent<PlayerMotor>(); if (m) m.Jumped -= OnJumped; }
        void OnJumped() => UseStamina(jumpCost);

        // ---------------------------------------------------------------- IStaminaSource
        public bool CanSprint => !Overweight && Stamina > (Time.time - _lastStaminaUse < 0.2f ? 0.5f : 12f);
        public void DrainSprint(float dt) { UseStamina(sprintCostPerSecond * dt); _sprinting = true; }
        public float MoveSpeedMultiplier => (Overweight ? 0.72f : 1f) * (Hunger < 12f || Thirst < 12f ? 0.9f : 1f) * (IsFreezing ? 0.9f : 1f);
        bool _sprinting;

        public bool UseStamina(float amount)
        {
            if (amount <= 0f) return true;
            bool had = Stamina >= amount * 0.5f;
            Stamina = Mathf.Max(0f, Stamina - amount); _lastStaminaUse = Time.time;
            return had;
        }

        // ---------------------------------------------------------------- consumption
        public void Consume(float hunger, float thirst, float health, float stamina)
        {
            Hunger = Mathf.Clamp(Hunger + hunger, 0f, 100f);
            Thirst = Mathf.Clamp(Thirst + thirst, 0f, 100f);
            Stamina = Mathf.Clamp(Stamina + stamina, 0f, MaxStaminaNow);
            if (health > 0f) _hp.Heal(health); else if (health < 0f) _hp.ApplyRaw(-health);
            if (hunger > 0f) _warnMask &= ~1; if (thirst > 0f) _warnMask &= ~2;
        }
        /// <summary>Stomach_Sick: health drains a little, thirst faster, stamina recovers slower, for the given time</summary>
        public void MakeSick(float seconds, string reason = "Your stomach turns. Raw meat was a risk.")
        {
            SickSeconds = Mathf.Max(SickSeconds, seconds); Warning?.Invoke(reason);
            GameEvents.Raise(GameEventType.GotSick, "stomach", Mathf.RoundToInt(seconds), transform.position);
        }
        public bool IsSick => SickSeconds > 0f;
        public void Warm(float seconds) { BodyTemperature = Mathf.Min(normalBody, BodyTemperature + seconds * 0.05f); }

        public void SetStats(float hunger, float thirst, float stamina, float body, float wet)
        {
            Hunger = Mathf.Clamp(hunger, 0, 100); Thirst = Mathf.Clamp(thirst, 0, 100); Stamina = Mathf.Clamp(stamina, 0, maxStamina);
            BodyTemperature = Mathf.Clamp(body, 30f, 40f); Wetness = Mathf.Clamp01(wet); _warnMask = 0;
        }

        // ---------------------------------------------------------------- environment (from time / weather / fire / shelter)
        /// <summary>air temperature around the player, deg C</summary>
        public static Func<Vector3, float> AirTemperature = p => 24f;
        /// <summary>is it raining on this spot (not under a roof)</summary>
        public static Func<Vector3, bool> RainingAt = p => false;
        /// <summary>extra warmth at this spot (fires, shelter), deg C</summary>
        public static Func<Vector3, float> HeatAt = p => 0f;

        void Update()
        {
            if (_hp.IsDead || Paused) { _sprinting = false; return; }
            float dt = Time.deltaTime; float m = dt / 60f;
            float k = _sprinting ? sprintMultiplier : 1f;
            Hunger = Mathf.Max(0f, Hunger - hungerPerMinute * m * k);
            Thirst = Mathf.Max(0f, Thirst - thirstPerMinute * m * k * (EnvironmentTemperature > 28f ? 1.3f : 1f) * (IsSick ? sickThirstMultiplier : 1f));

            // stamina
            float maxNow = MaxStaminaNow;
            if (Time.time - _lastStaminaUse > regenDelay) Stamina = Mathf.Min(maxNow, Stamina + regenPerSecond * (IsCold ? 0.6f : 1f) * (IsSick ? sickStaminaRegen : 1f) * dt);
            else if (Stamina > maxNow) Stamina = maxNow;

            // wetness + temperature
            Vector3 p = transform.position;
            bool rain = RainingAt(p);
            float heat = HeatAt(p);
            Wetness = Mathf.Clamp01(Wetness + (rain ? 0.04f : -(0.01f + heat * 0.004f)) * dt);
            EnvironmentTemperature = AirTemperature(p) + heat - Wetness * 5f + (_sprinting ? 2f : 0f);
            // body drifts towards a target: comfortable air keeps 37, cold air pulls it down, heat pulls it back up
            float target = EnvironmentTemperature >= 18f ? normalBody : Mathf.Lerp(33.5f, normalBody, (EnvironmentTemperature - 4f) / 14f);
            BodyTemperature = Mathf.MoveTowards(BodyTemperature, target, bodyResponse * dt * (target > BodyTemperature ? 3f : 1f));

            // damage / heal
            float dmg = 0f;
            if (IsStarving) dmg += starveDamage;
            if (IsDehydrated) dmg += dehydrateDamage;
            if (IsFreezing) dmg += freezeDamage;
            if (SickSeconds > 0f) { SickSeconds -= dt; dmg += sickDamage; }
            if (dmg > 0f) _hp.ApplyRaw(dmg * dt);
            else if (Hunger > 55f && Thirst > 55f && !IsCold && !_hp.IsBleeding) _hp.Heal(regenHealth * dt);

            // warnings once per threshold crossing
            Warn(1, Hunger < 20f, "You are hungry.");
            Warn(2, Thirst < 20f, "You are thirsty. Find fresh water.");
            Warn(4, IsCold, "You are getting cold. Find fire or shelter.");
            Warn(8, IsStarving, "Starving!");
            Warn(16, IsDehydrated, "Dehydrated!");
            _sprinting = false;
        }

        void Warn(int bit, bool on, string msg)
        {
            if (on && (_warnMask & bit) == 0) { _warnMask |= bit; Warning?.Invoke(msg); }
            else if (!on) _warnMask &= ~bit;
        }
    }
}
