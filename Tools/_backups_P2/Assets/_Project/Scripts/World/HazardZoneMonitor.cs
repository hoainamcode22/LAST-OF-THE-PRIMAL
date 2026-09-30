using UnityEngine;
using PrimalFrontier.Core;
using PrimalFrontier.Survival;

namespace PrimalFrontier.World
{
    /// <summary>
    /// Turns the HazardZone ring the player stands in into gameplay, four times a second: GameEventType.HazardWarning
    /// (id hazard id, amount level) on every level change with hysteresis (a ring is left only 6 % past its edge), a short
    /// line the first time each level is reached on an approach ("The ground is warm here." / "The air burns..."), the
    /// status effects "heat" (hot ring: stamina regeneration x0.7, sprinting x1.25, thirst x1.5) and "heat_severe"
    /// (dangerous ring: stamina regeneration x0.45, sprinting x1.5, stamina capped at 75 %, thirst x1.8, -0.3 health / s,
    /// no natural regeneration), and the local smoke at the camera (HazardZone.LocalSmoke, eased). Created on demand by the
    /// first enabled HazardZone in Play mode; nothing to place.
    /// </summary>
    public class HazardZoneMonitor : MonoBehaviour
    {
        public const string HeatId = "heat", SevereHeatId = "heat_severe";
        public static HazardZoneMonitor Instance { get; private set; }
        public HazardZone.Level Level { get; private set; }
        public HazardZone CurrentZone { get; private set; }
        [Tooltip("a ring is left only this far (x radius) past its edge")] [Range(0f, 0.3f)] public float hysteresis = 0.06f;
        float _next; float _smokeTarget; HazardZone.Level _warned;
        static StatusEffectDefinition _heat, _severe;

        public static void Ensure()
        {
            if (Instance || !Application.isPlaying) return;
            var go = new GameObject("[HazardMonitor]");
            go.AddComponent<HazardZoneMonitor>();
        }

        void Awake() { if (Instance && Instance != this) { Destroy(gameObject); return; } Instance = this; }
        void OnDestroy() { if (Instance == this) { Instance = null; HazardZone.LocalSmoke = 0f; } }

        void Update()
        {
            HazardZone.LocalSmoke = Mathf.MoveTowards(HazardZone.LocalSmoke, _smokeTarget, Time.deltaTime * 0.25f);
            if (Time.time < _next) return;
            _next = Time.time + 0.25f;
            var cam = Camera.main;
            var pp = PlayerLocator.Position;
            Vector3 eye = cam ? cam.transform.position : pp ?? Vector3.zero;
            _smokeTarget = HazardZone.All.Count > 0 && !(ZoneManager.Instance && ZoneManager.Instance.IsIndoor(eye)) ? HazardZone.TotalSmokeAt(eye) : 0f;
            if (!pp.HasValue) return;
            var p = pp.Value;
            var up = HazardZone.MaxLevelAt(p, out var zUp);
            var keep = HazardZone.MaxLevelAt(p, out var zKeep, hysteresis);
            var lvl = up > Level ? up : keep < Level ? keep : Level;
            var zone = up >= lvl ? zUp : zKeep;
            if (zone) CurrentZone = zone;
            if (lvl != Level) SetLevel(lvl, p);
            else
            {
                // keep the effects in step with the ring (a load / respawn clears every effect)
                var fx = PlayerStatusEffects.Player;
                if (fx) { SetEffect(fx, Heat, Level == HazardZone.Level.Hot, false); SetEffect(fx, Severe, Level == HazardZone.Level.Dangerous, false); }
            }
        }

        void SetLevel(HazardZone.Level lvl, Vector3 p)
        {
            var old = Level; Level = lvl;
            string id = CurrentZone ? CurrentZone.hazardId : "volcano";
            GameEvents.Raise(GameEventType.HazardWarning, id, (int)lvl, p);
            var fx = PlayerStatusEffects.Player;
            if (fx)
            {
                SetEffect(fx, Heat, lvl == HazardZone.Level.Hot, lvl > old);
                SetEffect(fx, Severe, lvl == HazardZone.Level.Dangerous, lvl > old);
                // one line per new, higher level on an approach (the effects bring their own text for hot / dangerous)
                if (lvl == HazardZone.Level.Warm && old == HazardZone.Level.Safe && _warned < HazardZone.Level.Warm) fx.Say("The ground is warm here. The air smells of sulphur.");
            }
            if (lvl > _warned) _warned = lvl;
            if (lvl == HazardZone.Level.Safe) _warned = HazardZone.Level.Safe;
        }

        static void SetEffect(PlayerStatusEffects fx, StatusEffectDefinition def, bool on, bool notify)
        {
            if (!def) return;
            bool has = fx.Has(def.id);
            if (on && !has) fx.Apply(def, 1f, 0f, notify);
            else if (!on && has) fx.Remove(def.id, false, false);
        }

        /// <summary>the "heat" definition (Resources/StatusEffects/SE_heat, else the built-in values)</summary>
        public static StatusEffectDefinition Heat => _heat ? _heat : _heat = StatusEffectDefinition.Get(HeatId) ?? BuiltIn(HeatId);
        /// <summary>the "heat_severe" definition (Resources/StatusEffects/SE_heat_severe, else the built-in values)</summary>
        public static StatusEffectDefinition Severe => _severe ? _severe : _severe = StatusEffectDefinition.Get(SevereHeatId) ?? BuiltIn(SevereHeatId);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() { _heat = null; _severe = null; }

        /// <summary>design values of the two heat effects (PrimalAtmosphereBuilder writes them into the assets)</summary>
        public static StatusEffectDefinition BuiltIn(string id)
        {
            var d = ScriptableObject.CreateInstance<StatusEffectDefinition>();
            d.id = id; d.name = "SE_" + id; d.iconName = "status_" + id; d.saved = false; d.defaultSeconds = 0f;
            if (id == SevereHeatId)
            {
                d.displayName = "Scorching heat"; d.color = new Color(1f, 0.36f, 0.16f); d.hudOrder = 3;
                d.description = "The air burns. You tire fast and your skin blisters. Get away from the lava.";
                d.staminaRegenMultiplier = 0.45f; d.sprintCostMultiplier = 1.5f; d.maxStaminaMultiplier = 0.75f; d.thirstMultiplier = 1.8f;
                d.healthPerSecond = -0.3f; d.blocksHealthRegen = true;
                d.appliedMessage = "The heat is unbearable. Get away from the lava.";
            }
            else
            {
                d.displayName = "Heat"; d.color = new Color(1f, 0.66f, 0.26f); d.hudOrder = 4;
                d.description = "Hot air from the volcano: you tire faster and get thirsty quickly.";
                d.staminaRegenMultiplier = 0.7f; d.sprintCostMultiplier = 1.25f; d.thirstMultiplier = 1.5f;
                d.appliedMessage = "The air is hot and hard to breathe. Rest will not come easily here.";
            }
            if (!Application.isEditor || Application.isPlaying) d.hideFlags = HideFlags.DontSave;
            return d;
        }
    }
}
