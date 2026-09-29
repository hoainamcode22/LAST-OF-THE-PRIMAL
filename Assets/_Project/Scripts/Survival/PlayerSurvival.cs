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
    /// Phase 3: sickness is the "sickness" status effect (PlayerStatusEffects; SickSeconds / MakeSick keep their meaning), status
    /// multipliers (injuries, sickness) apply to speed, sprint cost, stamina and thirst; food health comes back slowly
    /// (Recovering) and spoiled / aging food is worth less (Spoilage); wetness also rises in water and dries faster under a
    /// roof or in the sun; the sun warms a little outside the shade. Wet / Cold are shown as status flags.
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
        [Tooltip("legacy, unused (see the 'sickness' status effect)")] public float sickDamage = 0.3f;
        [Tooltip("legacy, unused: sickness numbers now come from the 'sickness' StatusEffectDefinition")] public float sickThirstMultiplier = 1.6f, sickStaminaRegen = 0.5f;
        public float regenHealth = 0.12f;
        [Tooltip("health regenerates only while hunger and thirst are both above this")] public float regenNeedAbove = 55f;

        public float Hunger { get; private set; }                // 100 = full (start value: SurvivalConfig.startHunger)
        public float Thirst { get; private set; }
        public float Stamina { get; private set; }
        public float BodyTemperature { get; private set; }
        public float Wetness { get; private set; }               // 0..1
        public float EnvironmentTemperature { get; private set; } = 24f;
        /// <summary>food poisoning time left (the "sickness" status effect)</summary>
        public float SickSeconds { get { var fx = Effects; if (!fx) return 0f; float r = fx.RemainingOf(StatusEffectIds.Sickness); return float.IsInfinity(r) ? 0f : r; } }
        /// <summary>standing in water (sea, pond, stream) this deep at the last check, m (0 = dry land)</summary>
        public float WaterDepth { get; private set; }
        /// <summary>0 = shade / night, 1 = full midday sun (last check)</summary>
        public float SunExposure { get; private set; }
        /// <summary>under a roof (shelter, tent, cave) at the last check</summary>
        public bool Sheltered { get; private set; }
        public bool IsCold => BodyTemperature < coldBody;
        public bool IsFreezing => BodyTemperature < freezingBody;
        public bool IsStarving => Hunger <= 0.01f;
        public bool IsDehydrated => Thirst <= 0.01f;
        public bool IsSick { get { var fx = Effects; return fx && fx.Has(StatusEffectIds.Sickness); } }
        public bool IsWet => Wetness > Config.wetStatusAbove;
        /// <summary>maximum stamina after the hunger / thirst tiers and status effects</summary>
        public float MaxStaminaNow => maxStamina * _maxMul * (_fx ? _fx.MaxStaminaMultiplier : 1f);
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
        /// <summary>a need went up: (HungerEventId / ThirstEventId, amount) for the "+30 Hydration" feedback</summary>
        public event Action<string, float> NeedRestored;
        /// <summary>a stack in the pack reached a new spoilage stage (item, stage)</summary>
        public event Action<ItemDefinition, FoodStage> FoodStageChanged;
        /// <summary>GameEvents ids of NeedTierChanged (amount = tier index + 1, 0 = fine)</summary>
        public const string HungerEventId = "hunger", ThirstEventId = "thirst";

        PlayerHealth _hp; InventorySystem _inv; SurvivalConfig _cfg; PlayerStatusEffects _fx;
        float _envTick, _spoilTick;
        float _lastStaminaUse = -10f; int _warnMask;
        int _hTier = int.MinValue, _tTier = int.MinValue;
        float _regenMul = 1f, _maxMul = 1f, _speedMul = 1f, _needDrain;
        string[] _hWarn = new string[0], _tWarn = new string[0];
        bool _sprinting;

        SurvivalConfig Config { get { if (!_cfg) ApplyConfig(); return _cfg; } }

        /// <summary>the status effects on this body (added when missing)</summary>
        public PlayerStatusEffects Effects
        {
            get
            {
                if (!_fx) { _fx = GetComponent<PlayerStatusEffects>(); if (!_fx && this) _fx = gameObject.AddComponent<PlayerStatusEffects>(); }
                return _fx;
            }
        }

        void Awake()
        {
            _hp = GetComponent<PlayerHealth>();
            _ = Effects;
            SurvivalItemUse.EnsureRegistered();
            ApplyConfig();
            var c = _cfg;
            Hunger = Mathf.Clamp(c.startHunger, 0f, 100f); Thirst = Mathf.Clamp(c.startThirst, 0f, 100f);
            BodyTemperature = normalBody;
            RefreshTiers(false);
            Stamina = MaxStaminaNow;
        }
        void Start()
        {
            var m = GetComponent<PlayerMotor>(); if (m) { m.Stamina = this; m.Jumped += OnJumped; m.Landed += OnLanded; }
            BindInventory();
        }
        void OnEnable() { BindInventory(); }
        void OnDisable() { if (_inv) _inv.Changed -= OnInventoryChanged; }
        void OnDestroy() { var m = GetComponent<PlayerMotor>(); if (m) { m.Jumped -= OnJumped; m.Landed -= OnLanded; } }
        void OnJumped() => UseStamina(jumpCost);
        void OnLanded(float impact) { if (_hp && !Paused) _hp.Landed(impact); }

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
        public void DrainSprint(float dt) { UseStamina(sprintCostPerSecond * (_fx ? _fx.SprintCostMultiplier : 1f) * dt); _sprinting = true; }
        /// <summary>1 at the start stats; tiers, overweight, freezing and status effects (leg injury) slow the player</summary>
        public float MoveSpeedMultiplier => (Overweight ? overweightSpeed : 1f) * _speedMul * (IsFreezing ? freezingSpeed : 1f) * (_fx ? _fx.MoveSpeedMultiplier : 1f);

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
            float h0 = Hunger, t0 = Thirst;
            Hunger = Mathf.Clamp(Hunger + hunger, 0f, 100f);
            Thirst = Mathf.Clamp(Thirst + thirst, 0f, 100f);
            RefreshTiers(true);
            Stamina = Mathf.Clamp(Stamina + stamina, 0f, MaxStaminaNow);
            if (health > 0f) _hp.Heal(health); else if (health < 0f) _hp.ApplyRaw(-health);
            if (Hunger - h0 > 0.5f) NeedRestored?.Invoke(HungerEventId, Hunger - h0);
            if (Thirst - t0 > 0.5f) NeedRestored?.Invoke(ThirstEventId, Thirst - t0);
        }

        /// <summary>eat one unit of this item (fresh): its food values, and the sickness roll of item.sicknessChance (raw meat)</summary>
        public bool ConsumeItem(ItemDefinition item) => ConsumeItem(item, FoodStage.Fresh);

        /// <summary>eat one unit from this stack (call before taking it out of the pack): the stack's spoilage stage counts</summary>
        public bool ConsumeItem(ItemDefinition item, ItemStack from) => ConsumeItem(item, from != null && from.item == item ? Spoilage.Stage(from) : FoodStage.Fresh);

        /// <summary>
        /// eat one unit at this spoilage stage: hunger / thirst / stamina x Spoilage.Nutrition, the item's health comes back
        /// slowly (Recovering), and the sickness roll (item.sicknessChance, higher when aging / spoiled)
        /// </summary>
        public bool ConsumeItem(ItemDefinition item, FoodStage stage)
        {
            if (item == null || !_hp || _hp.IsDead) return false;
            float k = Spoilage.Nutrition(stage);
            Consume(item.hunger * k, item.thirst * k, 0f, item.stamina * k);
            HealOverTime((item.health + item.healOverTime) * k);
            float chance = Spoilage.SickChance(item, stage);
            if (chance > 0f && UnityEngine.Random.value < chance)
            {
                string n = (item.displayName ?? "food").ToLowerInvariant();
                if (stage == FoodStage.Spoiled) MakeSick(Config.spoiledSickSeconds, "Your stomach turns. The " + n + " had spoiled.");
                else MakeSick(Config.foodSickSeconds, "Your stomach turns. The " + n + " was a risk.");
            }
            else if (stage == FoodStage.Spoiled) Warning?.Invoke("That " + (item.displayName ?? "food").ToLowerInvariant() + " had gone off. Not much good in it.");
            GameEvents.Raise(GameEventType.Ate, item.id, 1, transform.position);
            return true;
        }

        /// <summary>health back over time through the Recovering status (never all at once)</summary>
        public void HealOverTime(float amount)
        {
            if (amount <= 0f || !_hp || _hp.IsDead) return;
            var def = StatusEffectDefinition.Get(StatusEffectIds.Recovering);
            var fx = Effects;
            if (!def || !fx || def.healthPerSecond <= 0f) { _hp.Heal(amount); return; }
            fx.Apply(def, 1f, amount / def.healthPerSecond, false);
        }

        /// <summary>food poisoning (the "sickness" status): health drains a little, thirst faster, stamina lower, for the given time</summary>
        public void MakeSick(float seconds, string reason = "Your stomach turns. Raw meat was a risk.")
        {
            var fx = Effects; if (fx) fx.Apply(StatusEffectIds.Sickness, 1f, Mathf.Max(0.01f, seconds), false);
            Warning?.Invoke(reason);
            GameEvents.Raise(GameEventType.GotSick, "stomach", Mathf.RoundToInt(seconds), transform.position);
        }
        /// <summary>save / load: put the remaining sickness back without a warning (0 = not sick)</summary>
        public void RestoreSickness(float seconds)
        {
            var fx = Effects; if (!fx) return;
            fx.Remove(StatusEffectIds.Sickness, false, false);
            if (seconds > 0f) fx.Apply(StatusEffectIds.Sickness, 1f, seconds, false);
        }

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
            if (Effects) _fx.Clear();
            SetStats(c.startHunger, c.startThirst, maxStamina, normalBody, 0f);
            OnInventoryChanged();
        }

        /// <summary>after death: needs lifted to the config floor, config stamina / body temperature, dry, not sick (health: GameManager revives)</summary>
        public void ApplyRespawn()
        {
            var c = Config;
            if (Effects) _fx.Clear();
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
            // the night passes for the status effects too (sickness, injuries, wounds wear off) without their health effect
            var fx = Effects; if (fx) fx.Advance(hours * GameClock.SecondsPerHour);
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
        /// <summary>depth of water (sea, pond, stream) the feet at this spot stand in, m (0 = dry)</summary>
        public static Func<Vector3, float> WaterDepthAt = p => 0f;
        /// <summary>sun on this spot: 0 = shade / night / overcast .. 1 = full midday sun (checked every second or so)</summary>
        public static Func<Vector3, float> SunAt = p => 0f;
        /// <summary>under a roof (shelter, tent, cave)</summary>
        public static Func<Vector3, bool> ShelteredAt = p => false;
        /// <summary>seconds between the water / sun / roof checks</summary>
        public const float EnvironmentCheckSeconds = 0.5f;

        void Update()
        {
            if (!_hp || _hp.IsDead || Paused) { _sprinting = false; return; }
            if (!ReferenceEquals(_cfg, SurvivalConfig.Instance)) ApplyConfig();      // tests / tools swapped the config
            float dt = Time.deltaTime; float m = dt / 60f;
            float k = _sprinting ? sprintMultiplier : 1f;
            var fx = _fx;
            Hunger = Mathf.Max(0f, Hunger - hungerPerMinute * m * k * (fx ? fx.HungerMultiplier : 1f));
            Thirst = Mathf.Max(0f, Thirst - thirstPerMinute * m * k * (EnvironmentTemperature > hotAir ? hotAirThirstMultiplier : 1f) * (fx ? fx.ThirstMultiplier : 1f));
            RefreshTiers(true);

            // stamina
            float maxNow = MaxStaminaNow;
            if (Time.time - _lastStaminaUse > regenDelay) Stamina = Mathf.Min(maxNow, Stamina + regenPerSecond * _regenMul * (IsCold ? coldStaminaRegen : 1f) * (fx ? fx.StaminaRegenMultiplier : 1f) * dt);
            else if (Stamina > maxNow) Stamina = maxNow;

            // water / sun / roof: a few times a second (a raycast for the shade)
            Vector3 p = transform.position;
            var c = _cfg;
            _envTick -= dt;
            if (_envTick <= 0f)
            {
                _envTick = EnvironmentCheckSeconds;
                WaterDepth = Mathf.Max(0f, WaterDepthAt(p));
                Sheltered = ShelteredAt(p);
                SunExposure = Sheltered ? 0f : Mathf.Clamp01(SunAt(p));
            }

            // wetness + temperature
            bool rain = RainingAt(p);
            float heat = HeatAt(p);
            float wetIn = (rain ? c.rainWetPerSecond : 0f) + (WaterDepth > 0.05f ? c.waterWetPerSecond * Mathf.Clamp(WaterDepth / 0.8f, 0.25f, 1.5f) : 0f);
            float dry = c.dryPerSecond + heat * c.heatDryPerDegree + (Sheltered ? c.shelterDryPerSecond : 0f) + SunExposure * c.sunDryPerSecond;
            Wetness = Mathf.Clamp01(Wetness + (wetIn > 0f ? wetIn : -dry) * dt);
            EnvironmentTemperature = AirTemperature(p) + heat - Wetness * 5f + (_sprinting ? 2f : 0f) + SunExposure * c.sunWarmth - (WaterDepth > 0.05f ? c.waterChill : 0f);
            // body drifts towards a target: comfortable air keeps 37, cold air pulls it down, heat pulls it back up
            float target = EnvironmentTemperature >= 18f ? normalBody : Mathf.Lerp(33.5f, normalBody, (EnvironmentTemperature - 4f) / 14f);
            BodyTemperature = Mathf.MoveTowards(BodyTemperature, target, bodyResponse * dt * (target > BodyTemperature ? 3f : 1f));

            // damage / heal (status effects drain / heal on their own: PlayerStatusEffects)
            float dmg = _needDrain;
            if (IsFreezing) dmg += freezeDamage;
            bool resting = Sheltered || heat > 2f;
            if (dmg > 0f) _hp.ApplyRaw(dmg * dt);
            else if (Hunger > regenNeedAbove && Thirst > regenNeedAbove && !IsCold && !(fx && fx.BlocksRegen) && !_hp.IsBleeding)
                _hp.Heal(regenHealth * (resting ? c.restRegenMultiplier : 1f) * dt);
            // limb injuries heal faster while resting under a roof / by the fire
            if (resting && fx && fx.Count > 0 && c.restInjuryHealMultiplier > 1f)
            {
                float extra = dt * (c.restInjuryHealMultiplier - 1f);
                fx.Shorten(StatusEffectIds.LegInjury, extra); fx.Shorten(StatusEffectIds.ArmInjury, extra);
            }

            // derived status flags (HUD icons; nothing else reads them as rules)
            if (fx) { fx.SetFlag(StatusEffectIds.Wet, Wetness > c.wetStatusAbove); fx.SetFlag(StatusEffectIds.Cold, IsCold); }

            // spoilage: a slow look at the pack (stages are worked out from the time, nothing ticks per item)
            _spoilTick -= dt;
            if (_spoilTick <= 0f) { _spoilTick = Mathf.Max(0.5f, c.spoilCheckSeconds); CheckSpoilage(); }

            // hunger / thirst warnings come from the tier crossings; cold once per crossing
            Warn(4, IsCold, "You are getting cold. Find fire or shelter.");
            _sprinting = false;
        }

        // ---------------------------------------------------------------- spoilage
        ItemDefinition[] _spoilItem; FoodStage[] _spoilStage;

        /// <summary>
        /// the pack is looked at every SurvivalConfig.spoilCheckSeconds (stages come from the time, nothing ticks per item):
        /// when a stack's stage moved on, the slots refresh once and FoodStageChanged tells the HUD
        /// </summary>
        public void CheckSpoilage()
        {
            if (!_inv || _inv.Slots == null) return;
            var slots = _inv.Slots;
            if (_spoilItem == null || _spoilItem.Length != slots.Length) { _spoilItem = new ItemDefinition[slots.Length]; _spoilStage = new FoodStage[slots.Length]; }
            bool changed = false;
            for (int i = 0; i < slots.Length; i++)
            {
                var st = slots[i];
                var item = st != null && !st.IsEmpty && Spoilage.Spoils(st.item) ? st.item : null;
                var stage = item ? Spoilage.Stage(st) : FoodStage.Fresh;
                if (item && item == _spoilItem[i] && stage != _spoilStage[i])
                {
                    changed = true;
                    if (stage > _spoilStage[i]) FoodStageChanged?.Invoke(item, stage);
                }
                _spoilItem[i] = item; _spoilStage[i] = stage;
            }
            if (changed) _inv.ForceNotify();              // slot tints / labels
        }

        void Warn(int bit, bool on, string msg)
        {
            if (on && (_warnMask & bit) == 0) { _warnMask |= bit; Warning?.Invoke(msg); }
            else if (!on) _warnMask &= ~bit;
        }
    }
}
