using System;
using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Core;
using PrimalFrontier.Player;

namespace PrimalFrontier.Survival
{
    /// <summary>
    /// The player's status effects (bleeding, leg / arm injury, food poisoning, wet, cold, recovering), data from
    /// <see cref="StatusEffectDefinition"/>. Apply / Has / Remove by id or definition; timed effects count down here (one
    /// small list, no allocation per frame), health per second goes through PlayerHealth (drain = ApplyRaw, heal = Heal),
    /// and the combined multipliers are recomputed only when the list changes. PlayerSurvival reads the multipliers
    /// (speed, sprint cost, stamina, thirst, hunger, regeneration block); combat reads <see cref="AttackMultiplier"/>,
    /// gathering <see cref="GatherMultiplier"/>; AI reads Has(Bleeding). Sleeping advances the timers without their
    /// health effect (<see cref="Advance"/>). The player's effects are a save section ("status").
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerStatusEffects : MonoBehaviour, ISaveSection
    {
        public sealed class Active
        {
            public StatusEffectDefinition def;
            public float severity = 1f;
            /// <summary>seconds left; &lt;= 0 with <see cref="untilRemoved"/> = stays until removed</summary>
            public float remaining;
            public bool untilRemoved;
            public string Id => def ? def.id : null;
        }

        public const string SaveKey = "status";
        readonly List<Active> _active = new List<Active>(8);
        public IReadOnlyList<Active> ActiveEffects => _active;

        /// <summary>the list changed (added, removed, severity): HUD icons, multipliers</summary>
        public event Action Changed;
        /// <summary>(definition, true = applied / false = removed)</summary>
        public event Action<StatusEffectDefinition, bool> EffectChanged;
        /// <summary>a message worth showing ("You are bleeding..."): the HUD listens</summary>
        public event Action<string> Message;
        /// <summary>send a status message through <see cref="Message"/> (wound rules, treatments)</summary>
        public void Say(string msg) { if (!string.IsNullOrEmpty(msg)) Message?.Invoke(msg); }

        // combined multipliers (recomputed on change)
        public float MoveSpeedMultiplier { get; private set; } = 1f;
        public float SprintCostMultiplier { get; private set; } = 1f;
        public float StaminaRegenMultiplier { get; private set; } = 1f;
        public float MaxStaminaMultiplier { get; private set; } = 1f;
        public float ThirstMultiplier { get; private set; } = 1f;
        public float HungerMultiplier { get; private set; } = 1f;
        /// <summary>damage multiplier for the player's attacks (arm injury): read by combat</summary>
        public float AttackMultiplier { get; private set; } = 1f;
        /// <summary>gather speed / yield multiplier (arm injury): read by resource nodes</summary>
        public float GatherMultiplier { get; private set; } = 1f;
        /// <summary>some active effect stops natural health regeneration (bleeding, sickness)</summary>
        public bool BlocksRegen { get; private set; }
        /// <summary>net health per second of all effects (negative = losing health)</summary>
        public float HealthPerSecond { get; private set; }
        /// <summary>x on the healing effects (Recovering): PlayerSurvival lowers it while hungry / thirsty (drains are not scaled)</summary>
        public float HealMultiplier { get; set; } = 1f;
        /// <summary>number of effects shown on the HUD</summary>
        public int Count => _active.Count;
        /// <summary>increments on every change (cheap HUD dirty check)</summary>
        public int Version { get; private set; }

        PlayerHealth _hp;
        bool _registered;

        /// <summary>the player's component (tagged Player), null when none</summary>
        public static PlayerStatusEffects Player { get; private set; }

        void Awake() { _hp = GetComponent<PlayerHealth>(); }
        void OnEnable()
        {
            if (CompareTag("Player")) { Player = this; SaveSystem.RegisterSection(this); _registered = true; }
        }
        void OnDisable()
        {
            if (_registered) { SaveSystem.UnregisterSection(this); _registered = false; }
            if (Player == this) Player = null;
        }

        // ------------------------------------------------------------------ queries
        public Active Find(string id)
        {
            for (int i = 0; i < _active.Count; i++) if (_active[i].def && _active[i].def.id == id) return _active[i];
            return null;
        }
        public bool Has(string id) => Find(id) != null;
        public bool Has(StatusEffectDefinition def) => def && Find(def.id) != null;
        public float SeverityOf(string id) { var a = Find(id); return a != null ? a.severity : 0f; }
        /// <summary>seconds left (0 = not active; +infinity = until removed)</summary>
        public float RemainingOf(string id) { var a = Find(id); return a == null ? 0f : a.untilRemoved ? float.PositiveInfinity : Mathf.Max(0f, a.remaining); }

        // ------------------------------------------------------------------ apply / remove
        /// <summary>apply by id (seconds &lt; 0 = the definition's default; 0 on a definition with defaultSeconds 0 = until removed)</summary>
        public Active Apply(string id, float severity = 1f, float seconds = -1f, bool notify = true) => Apply(StatusEffectDefinition.Get(id), severity, seconds, notify);

        public Active Apply(StatusEffectDefinition def, float severity = 1f, float seconds = -1f, bool notify = true)
        {
            if (!def || (_hp && _hp.IsDead)) return null;
            if (seconds < 0f) seconds = def.defaultSeconds;
            bool untilRemoved = seconds <= 0f;
            float cap = def.maxSeconds > 0f ? def.maxSeconds : float.MaxValue;
            severity = Mathf.Clamp(severity, 0.01f, def.maxSeverity);
            var a = Find(def.id);
            bool added = a == null;
            if (added)
            {
                a = new Active { def = def, severity = severity, remaining = Mathf.Min(seconds, cap), untilRemoved = untilRemoved };
                _active.Add(a);
            }
            else
            {
                if (untilRemoved) a.untilRemoved = true;
                switch (def.stacking)
                {
                    case StatusEffectDefinition.Stacking.Extend: a.remaining = Mathf.Min(cap, Mathf.Max(0f, a.remaining) + seconds); break;
                    case StatusEffectDefinition.Stacking.AddSeverity: a.severity = Mathf.Min(def.maxSeverity, a.severity + severity); a.remaining = Mathf.Min(cap, Mathf.Max(a.remaining, seconds)); break;
                    default: a.remaining = Mathf.Min(cap, Mathf.Max(a.remaining, seconds)); break;
                }
                if (def.stacking != StatusEffectDefinition.Stacking.AddSeverity) a.severity = Mathf.Max(a.severity, severity);
            }
            Recompute();
            if (added)
            {
                Fire(def, true);
                if (notify && !string.IsNullOrEmpty(def.appliedMessage)) Message?.Invoke(def.appliedMessage);
            }
            Changed?.Invoke();
            return a;
        }

        /// <summary>remove an effect; cured = by treatment (bandage...): shows the cured message</summary>
        public bool Remove(string id, bool cured = false, bool notify = true)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                var a = _active[i];
                if (!a.def || a.def.id != id) continue;
                _active.RemoveAt(i);
                Recompute();
                Fire(a.def, false);
                if (notify) { var m = cured ? a.def.curedMessage : null; if (!string.IsNullOrEmpty(m)) Message?.Invoke(m); }
                Changed?.Invoke();
                return true;
            }
            return false;
        }

        /// <summary>derived on / off states (wet, cold): applied until removed, silently</summary>
        public void SetFlag(string id, bool on)
        {
            bool has = Has(id);
            if (on && !has) Apply(id, 1f, 0f, false);
            else if (!on && has) Remove(id, false, false);
        }

        /// <summary>shorten a timed effect (rest, a splint...); removes it when it runs out</summary>
        public void Shorten(string id, float seconds)
        {
            var a = Find(id); if (a == null || a.untilRemoved || seconds <= 0f) return;
            a.remaining -= seconds;
            if (a.remaining <= 0f) Expire(a);
        }

        /// <summary>new game / respawn: everything off, silently</summary>
        public void Clear()
        {
            if (_active.Count == 0) return;
            var old = _active.ToArray();
            _active.Clear();
            Recompute();
            foreach (var a in old) if (a.def) Fire(a.def, false);
            Changed?.Invoke();
        }

        /// <summary>time passed without the effects acting (sleep): timers run down, no health change</summary>
        public void Advance(float seconds)
        {
            if (seconds <= 0f) return;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var a = _active[i];
                if (a.untilRemoved) continue;
                a.remaining -= seconds;
                if (a.remaining <= 0f) Expire(a);
            }
        }

        void Expire(Active a)
        {
            int i = _active.IndexOf(a); if (i < 0) return;
            _active.RemoveAt(i);
            Recompute();
            Fire(a.def, false);
            if (a.def && !string.IsNullOrEmpty(a.def.expiredMessage)) Message?.Invoke(a.def.expiredMessage);
            Changed?.Invoke();
        }

        void Recompute()
        {
            float move = 1f, sprint = 1f, regen = 1f, maxSt = 1f, thirst = 1f, hunger = 1f, atk = 1f, gather = 1f, hps = 0f; bool block = false;
            for (int i = 0; i < _active.Count; i++)
            {
                var a = _active[i]; var d = a.def; if (!d) continue;
                float s = a.severity;
                move *= StatusEffectDefinition.Scaled(d.moveSpeedMultiplier, s);
                sprint *= StatusEffectDefinition.Scaled(d.sprintCostMultiplier, s);
                regen *= StatusEffectDefinition.Scaled(d.staminaRegenMultiplier, s);
                maxSt *= StatusEffectDefinition.Scaled(d.maxStaminaMultiplier, s);
                thirst *= StatusEffectDefinition.Scaled(d.thirstMultiplier, s);
                hunger *= StatusEffectDefinition.Scaled(d.hungerMultiplier, s);
                atk *= StatusEffectDefinition.Scaled(d.attackMultiplier, s);
                gather *= StatusEffectDefinition.Scaled(d.gatherMultiplier, s);
                hps += d.healthPerSecond * s;
                block |= d.blocksHealthRegen;
            }
            MoveSpeedMultiplier = move; SprintCostMultiplier = sprint; StaminaRegenMultiplier = regen; MaxStaminaMultiplier = maxSt;
            ThirstMultiplier = thirst; HungerMultiplier = hunger; AttackMultiplier = atk; GatherMultiplier = gather;
            HealthPerSecond = hps; BlocksRegen = block;
            Version++;
        }

        void Fire(StatusEffectDefinition def, bool on)
        {
            if (!def) return;
            EffectChanged?.Invoke(def, on);
            GameEvents.Raise(on ? GameEventType.StatusApplied : GameEventType.StatusEnded, def.id, 1, transform.position);
        }

        // ------------------------------------------------------------------ tick
        void Update()
        {
            if (_active.Count == 0) return;
            if (!_hp) _hp = GetComponent<PlayerHealth>();
            if (_hp && _hp.IsDead) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            // health: one call per frame for the net drain / heal
            if (_hp)
            {
                float drain = 0f, heal = 0f;
                for (int i = 0; i < _active.Count; i++)
                {
                    var a = _active[i]; if (!a.def) continue;
                    float v = a.def.healthPerSecond * a.severity;
                    if (v < 0f) drain -= v; else heal += v;
                }
                if (drain > 0f) _hp.ApplyRaw(drain * dt);
                if (heal > 0f && !_hp.IsDead && _hp.Health < _hp.maxHealth) _hp.Heal(heal * Mathf.Clamp(HealMultiplier, 0f, 4f) * dt);
                if (_hp.IsDead) return;
            }
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (i >= _active.Count) continue;
                var a = _active[i];
                if (a.untilRemoved) continue;
                a.remaining -= dt;
                if (a.remaining <= 0f) Expire(a);
            }
        }

        // ------------------------------------------------------------------ save section
        [Serializable] class EffectSave { public string id; public float severity; public float remaining; public bool untilRemoved; }
        [Serializable] class SectionSave { public List<EffectSave> effects = new List<EffectSave>(); }

        public string SectionKey => SaveKey;

        public string CaptureSection()
        {
            var s = new SectionSave();
            foreach (var a in _active)
                if (a.def && a.def.saved) s.effects.Add(new EffectSave { id = a.def.id, severity = a.severity, remaining = Mathf.Max(0f, a.remaining), untilRemoved = a.untilRemoved });
            return JsonUtility.ToJson(s);
        }

        /// <summary>replaces the saved effects (derived ones such as wet / cold follow the stats on the next frame); unknown ids and bad numbers are skipped</summary>
        public void RestoreSection(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            SectionSave s;
            try { s = JsonUtility.FromJson<SectionSave>(json); }
            catch (Exception e) { Debug.LogWarning("[PlayerStatusEffects] unreadable status section skipped: " + e.Message, this); return; }
            if (s == null || s.effects == null) return;
            for (int i = _active.Count - 1; i >= 0; i--) if (_active[i].def && _active[i].def.saved) _active.RemoveAt(i);
            foreach (var e in s.effects)
            {
                if (e == null) continue;
                var def = StatusEffectDefinition.Get(e.id);
                if (!def || !def.saved) continue;
                float sev = float.IsNaN(e.severity) || e.severity <= 0f ? 1f : e.severity;
                float rem = float.IsNaN(e.remaining) || float.IsInfinity(e.remaining) ? def.defaultSeconds : e.remaining;
                if (!e.untilRemoved && rem <= 0f) continue;
                if (Find(def.id) != null) continue;
                _active.Add(new Active { def = def, severity = Mathf.Clamp(sev, 0.01f, def.maxSeverity), remaining = rem, untilRemoved = e.untilRemoved });
            }
            Recompute();
            foreach (var a in _active) Fire(a.def, true);
            Changed?.Invoke();
        }
    }
}
