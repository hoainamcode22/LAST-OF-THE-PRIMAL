using System;
using UnityEngine;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;

namespace PrimalFrontier.Survival
{
    /// <summary>
    /// Hunger, thirst, stamina, body temperature, wetness and food sickness of the player. The needs numbers come from
    /// SurvivalConfig: hunger / thirst drain per real minute and their effects grow in tiers (medium: slower stamina
    /// regeneration, low: lower maximum stamina, critical: health loss). Hunger and thirst combine by taking the worse
    /// multiplier of the two and adding their health drains. Eating (ConsumeItem), sleeping (ApplySleep) and respawning
    /// (ApplyRespawn) rules live here, not in the player controllers. Pays for sprinting (IStaminaSource) and hurts /
    /// heals through PlayerHealth. No allocations per frame.
    /// </summary>
    [RequireComponent(typeof(PlayerHealth))]
    public class PlayerSurvival : MonoBehaviour, IStaminaSource
    {
        [Header("Hunger / thirst (per real minute; taken from SurvivalConfig at Awake)")]
        public float hungerPerMinute = 2.2f;
        public float thirstPerMinute = 3.2f;
        public float sprintMultiplier = 1.8f;
        [Tooltip("thirst drains hotAirThirstMultiplier times faster while the air around the player is above hotAir deg C")] public float hotAir = 28f, hotAirThirstMultiplier = 1.3f;
        [Header("Stamina")]
        public float maxStamina = 100f;
        public float sprintCostPerSecond = 16f;
        public float regenPerSecond = 14f;
        public float regenDelay = 0.9f;
        public float jumpCost = 8f;
        [Tooltip("stamina regeneration multiplier while cold")] public float coldStaminaRegen = 0.6f;
        [Tooltip("move speed multiplier while carrying too much / while freezing")] public float overweightSpeed = 0.72f, freezingSpeed = 0.9f;
        [Header("Temperature (deg C)")]
        public float normalBody = 37f;
        public float coldBody = 35.2f;
        public float freezingBody = 34.2f;
        public float bodyResponse = 0.02f;         // how fast the body follows the environment
        [Header("Damage / heal (HP per second; hunger / thirst damage comes from the SurvivalConfig tiers)")]
        public float freezeDamage = 0.2f;
        public float sickDamage = 0.3f;
        [Tooltip("while sick (Stomach_Sick): thirst drains faster, stamina comes back slower")] public float sickThirstMultiplier = 1.6f, sickStaminaRegen = 0.5f;
        public float regenHealth = 0.12f;
        [Tooltip("health regenerates only while hunger and thirst are both above this")] public float regenNeedAbove = 55f;

        public float Hunger { get; private set; }                // 100 = full (start value: SurvivalConfig.startHunger)
        public float Thirst { get; private set; }
        public float Stamina { get; private set; }
        public float BodyTemperature { get; private set; }
        public float Wetness { get; private set; }               // 0..1
        public float EnvironmentTemperature { get; private set; } = 24f;
        public float SickSeconds { get; private set; }
        public bool IsCold => BodyTemperature < coldBody;
        public bool IsFreezing => BodyTemperature < freezingBody;
        public bool IsStarving => Hunger <= 0.01f;
        public bool IsDehydrated => Thirst <= 0.01f;
        public bool IsSick => SickSeconds > 0f;
        /// <summary>maximum stamina after the hunger / thirst tiers</summary>
        public float MaxStaminaNow => maxStamina * _maxMul;
        /// <summary>stamina regeneration multiplier of the hunger / thirst tiers (1 = fine; cold and sickness apply on top)</summary>
        public float NeedStaminaRegenMultiplier => _regenMul;
        /// <summary>health lost per second from hunger + thirst tiers</summary>
        public float NeedHealthDrain => _needDrain;
        /// <summary>index into SurvivalConfig.hungerTiers / thirstTiers, -1 = fine</summary>
        public int HungerTier => _hTier;
        public int ThirstTier => _tTier;
        /// <summary>HUD word of the current tier ("Hungry", "Thirsty"...), null when fine. Cached config strings, no allocation.</summary>
        public string HungerTierLabel { get; private set; }
        public string ThirstTierLabel { get; private set; }
        /// <summary>carried weight too high: follows the player's InventorySystem (event driven)</summary>
        public bool Overweight { get; set; }
        /// <summary>survival stats paused (intro, menus that stop time, tests)</summary>
        public bool Paused { get; set; }

        public event Action<string> Warning;      // "Thirsty: find water soon." etc. (HUD)
        /// <summary>GameEvents ids of NeedTierChanged (amount = tier index + 1, 0 = fine)</summary>
        public const string HungerEventId = "hunger", ThirstEventId = "thirst";

        PlayerHealth _hp; InventorySystem _inv; SurvivalConfig _cfg;
        float _lastStaminaUse = -10f; int _warnMask;
        int _hTier = int.MinValue, _tTier = int.MinValue;
        float _regenMul = 1f, _maxMul = 1f, _speedMul = 1f, _needDrain;
        string[] _hWarn = new string[0], _tWarn = new string[0];
        bool _sprinting;

        SurvivalConfig Config { get { if (!_cfg) ApplyConfig(); return _cfg; } }

        void Awake()
        {
            _hp = GetComponent<PlayerHealth>();
            ApplyConfig();
            var c = _cfg;
            Hunger = Mathf.Clamp(c.startHunger, 0f, 100f); Thirst = Mathf.Clamp(c.startThirst, 0f, 100f);
            BodyTemperature = normalBody;
            RefreshTiers(false);
            Stamina = MaxStaminaNow;
        }
        void Start()
        {
            var m = GetComponent<PlayerMotor>(); if (m) { m.Stamina = this; m.Jumped += OnJumped; }
            BindInventory();
        }
        void OnEnable() { BindInventory(); }
        void OnDisable() { if (_inv) _inv.Changed -= OnInventoryChanged; }
        void OnDestroy() { var m = GetComponent<PlayerMotor>(); if (m) m.Jumped -= OnJumped; }
        void OnJumped() => UseStamina(jumpCost);

        void BindInventory()
        {
            if (!_inv) _inv = GetComponent<InventorySystem>();
            if (!_inv) return;
            _inv.Changed -= OnInventoryChanged; _inv.Changed += OnInventoryChanged;
            OnInventoryChanged();
        }
        void OnInventoryChanged() { if (_inv) Overweight = _inv.IsOverweight; }

        /// <summary>(re)read SurvivalConfig.Instance: drains and the tier warning texts (allocates, only when the config changes)</summary>
        void ApplyConfig()
        {
            _cfg = SurvivalConfig.Instance;
            hungerPerMinute = _cfg.hungerPerMinute; thirstPerMinute = _cfg.thirstPerMinute;
            _hWarn = BuildWarnings(_cfg.hungerTiers, "find something to eat soon.", "you are losing health. Eat something now.");
            _tWarn = BuildWarnings(_cfg.thirstTiers, "find water soon, and boil it.", "you are losing health. Drink clean water now.");
            _hTier = _tTier = int.MinValue;           // recompute multipliers against the new tiers
            RefreshTiers(false);
        }

        static string[] BuildWarnings(SurvivalConfig.NeedTier[] tiers, string hint, string drainHint)
        {
            if (tiers == null) return new string[0];
            var w = new string[tiers.Length];
            for (int i = 0; i < tiers.Length; i++)
            {
                string l = string.IsNullOrEmpty(tiers[i].label) ? "Needs" : tiers[i].label.TrimEnd('!', '.');
                w[i] = l + ": " + (tiers[i].healthDrain > 0f ? drainHint : hint);
            }
            return w;
        }

        // ---------------------------------------------------------------- IStaminaSource
        public bool CanSprint => !Overweight && Stamina > (Time.time - _lastStaminaUse < 0.2f ? 0.5f : 12f);
        public void DrainSprint(float dt) { UseStamina(sprintCostPerSecond * dt); _sprinting = true; }
        /// <summary>1 at the start stats; tiers, overweight and freezing slow the player</summary>
        public float MoveSpeedMultiplier => (Overweight ? overweightSpeed : 1f) * _speedMul * (IsFreezing ? freezingSpeed : 1f);

        public bool UseStamina(float amount)
        {
            if (amount <= 0f) return true;
            bool had = Stamina >= amount * 0.5f;
            Stamina = Mathf.Max(0f, Stamina - amount); _lastStaminaUse = Time.time;
            return had;
        }

        // ---------------------------------------------------------------- tiers
        /// <summary>the tier each need is in; multipliers combine as the worse of the two, health drains add</summary>
        void RefreshTiers(bool notify)
        {
            var c = _cfg ? _cfg : Config;
            SurvivalConfig.TierFor(c.hungerTiers, Hunger, out var ht, out int hi);
            SurvivalConfig.TierFor(c.thirstTiers, Thirst, out var tt, out int ti);
            if (hi == _hTier && ti == _tTier) return;
            int oldH = _hTier, oldT = _tTier;
            _hTier = hi; _tTier = ti;
            _regenMul = Mathf.Min(hi >= 0 ? ht.staminaRegen : 1f, ti >= 0 ? tt.staminaRegen : 1f);
            _maxMul = Mathf.Min(hi >= 0 ? ht.maxStamina : 1f, ti >= 0 ? tt.maxStamina : 1f);
            _speedMul = Mathf.Min(hi >= 0 ? ht.moveSpeed : 1f, ti >= 0 ? tt.moveSpeed : 1f);
            _needDrain = (hi >= 0 ? Mathf.Max(0f, ht.healthDrain) : 0f) + (ti >= 0 ? Mathf.Max(0f, tt.healthDrain) : 0f);
            HungerTierLabel = hi >= 0 && !string.IsNullOrEmpty(ht.label) ? ht.label : null;
            ThirstTierLabel = ti >= 0 && !string.IsNullOrEmpty(tt.label) ? tt.label : null;
            if (!notify) return;
            if (hi != oldH) TierChanged(HungerEventId, hi, oldH, _hWarn);
            if (ti != oldT) TierChanged(ThirstEventId, ti, oldT, _tWarn);
        }

        void TierChanged(string need, int index, int old, string[] warnings)
        {
            GameEvents.Raise(GameEventType.NeedTierChanged, need, index + 1, transform.position);
            if (index > old && index >= 0 && index < warnings.Length) Warning?.Invoke(warnings[index]);     // only when it gets worse
        }

        // ---------------------------------------------------------------- consumption
        public void Consume(float hunger, float thirst, float health, float stamina)
        {
            Hunger = Mathf.Clamp(Hunger + hunger, 0f, 100f);
            Thirst = Mathf.Clamp(Thirst + thirst, 0f, 100f);
            RefreshTiers(true);
            Stamina = Mathf.Clamp(Stamina + stamina, 0f, MaxStaminaNow);
            if (health > 0f) _hp.Heal(health); else if (health < 0f) _hp.ApplyRaw(-health);
        }

        /// <summary>eat one unit of this item: its food values, and the sickness roll of item.sicknessChance (raw meat)</summary>
        public bool ConsumeItem(ItemDefinition item)
        {
            if (item == null || !_hp || _hp.IsDead) return false;
            Consume(item.hunger, item.thirst, item.health, item.stamina);
            if (item.sicknessChance > 0f && UnityEngine.Random.value < item.sicknessChance)
                MakeSick(Config.foodSickSeconds, "Your stomach turns. The " + (item.displayName ?? "food").ToLowerInvariant() + " was a risk.");
            GameEvents.Raise(GameEventType.Ate, item.id, 1, transform.position);
            return true;
        }

        /// <summary>Stomach_Sick: health drains a little, thirst faster, stamina recovers slower, for the given time</summary>
        public void MakeSick(float seconds, string reason = "Your stomach turns. Raw meat was a risk.")
        {
            SickSeconds = Mathf.Max(SickSeconds, seconds); Warning?.Invoke(reason);
            GameEvents.Raise(GameEventType.GotSick, "stomach", Mathf.RoundToInt(seconds), transform.position);
        }
        /// <summary>save / load: put the remaining sickness back without a warning</summary>
        public void RestoreSickness(float seconds) { SickSeconds = Mathf.Max(0f, seconds); }

        public void SetStats(float hunger, float thirst, float stamina, float body, float wet)
        {
            Hunger = Mathf.Clamp(hunger, 0, 100); Thirst = Mathf.Clamp(thirst, 0, 100);
            BodyTemperature = Mathf.Clamp(body, 30f, 40f); Wetness = Mathf.Clamp01(wet); _warnMask = 0;
            RefreshTiers(false);
            Stamina = Mathf.Clamp(stamina, 0, MaxStaminaNow);
        }

        /// <summary>new game: start stats from SurvivalConfig, full stamina, normal body, dry, not sick</summary>
        public void ResetToStart()
        {
            var c = Config;
            SickSeconds = 0f;
            SetStats(c.startHunger, c.startThirst, maxStamina, normalBody, 0f);
            OnInventoryChanged();
        }

        /// <summary>after death: needs lifted to the config floor, config stamina / body temperature, dry, not sick (health: GameManager revives)</summary>
        public void ApplyRespawn()
        {
            var c = Config;
            SickSeconds = 0f;
            SetStats(Mathf.Max(Hunger, c.respawnNeedFloor), Mathf.Max(Thirst, c.respawnNeedFloor), c.respawnStamina, c.respawnBodyTemp, 0f);
        }

        /// <summary>
        /// Slept this many in-game hours: hunger / thirst fall by the config cost per hour (never below the sleep floor,
        /// never raised when already below it), health recovers per hour, stamina refills, sickness wears off with the
        /// skipped time.
        /// </summary>
        public void ApplySleep(float hours)
        {
            if (hours <= 0f || !_hp || _hp.IsDead) return;
            var c = Config;
            float floor = c.sleepNeedFloor;
            if (Hunger > floor) Hunger = Mathf.Max(floor, Hunger - hours * Mathf.Max(0f, c.sleepHungerPerHour));
            if (Thirst > floor) Thirst = Mathf.Max(floor, Thirst - hours * Mathf.Max(0f, c.sleepThirstPerHour));
            if (c.sleepHealthPerHour > 0f) _hp.Heal(hours * c.sleepHealthPerHour);
            SickSeconds = Mathf.Max(0f, SickSeconds - hours * GameClock.SecondsPerHour);
            RefreshTiers(true);
            Stamina = MaxStaminaNow;
        }

        // ---------------------------------------------------------------- environment (SurvivalEnvironment sets these)
        /// <summary>air temperature around the player, deg C</summary>
        public static Func<Vector3, float> AirTemperature = p => 24f;
        /// <summary>is it raining on this spot (not under a roof)</summary>
        public static Func<Vector3, bool> RainingAt = p => false;
        /// <summary>extra warmth at this spot (fires, shelter, torch), deg C</summary>
        public static Func<Vector3, float> HeatAt = p => 0f;

        void Update()
        {
            if (!_hp || _hp.IsDead || Paused) { _sprinting = false; return; }
            if (!ReferenceEquals(_cfg, SurvivalConfig.Instance)) ApplyConfig();      // tests / tools swapped the config
            float dt = Time.deltaTime; float m = dt / 60f;
            float k = _sprinting ? sprintMultiplier : 1f;
            Hunger = Mathf.Max(0f, Hunger - hungerPerMinute * m * k);
            Thirst = Mathf.Max(0f, Thirst - thirstPerMinute * m * k * (EnvironmentTemperature > hotAir ? hotAirThirstMultiplier : 1f) * (IsSick ? sickThirstMultiplier : 1f));
            RefreshTiers(true);

            // stamina
            float maxNow = MaxStaminaNow;
            if (Time.time - _lastStaminaUse > regenDelay) Stamina = Mathf.Min(maxNow, Stamina + regenPerSecond * _regenMul * (IsCold ? coldStaminaRegen : 1f) * (IsSick ? sickStaminaRegen : 1f) * dt);
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
            float dmg = _needDrain;
            if (IsFreezing) dmg += freezeDamage;
            if (SickSeconds > 0f) { SickSeconds = Mathf.Max(0f, SickSeconds - dt); dmg += sickDamage; }
            if (dmg > 0f) _hp.ApplyRaw(dmg * dt);
            else if (Hunger > regenNeedAbove && Thirst > regenNeedAbove && !IsCold && !_hp.IsBleeding) _hp.Heal(regenHealth * dt);

            // hunger / thirst warnings come from the tier crossings; cold once per crossing
            Warn(4, IsCold, "You are getting cold. Find fire or shelter.");
            _sprinting = false;
        }

        void Warn(int bit, bool on, string msg)
        {
            if (on && (_warnMask & bit) == 0) { _warnMask |= bit; Warning?.Invoke(msg); }
            else if (!on) _warnMask &= ~bit;
        }
    }
}
