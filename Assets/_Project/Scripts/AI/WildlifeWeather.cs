using UnityEngine;
using PrimalFrontier.Core;

namespace PrimalFrontier.AI
{
    /// <summary>
    /// The weather as wildlife feels it (Phase 1, AI). WeatherManager is read at a low rate (WildlifeConfig.weatherCheckSeconds,
    /// once for all creatures, never per creature per frame) and turned into shared factors:
    /// - rain: herbivores graze closer to tree cover, rest more, herds bunch; heavy rain or a storm: herds go to the trees
    ///   and wait it out (HerdGroup), loners shelter too;
    /// - storm: flyers land (AmbientCreature), predators lie low and do not hunt, heavy rain makes them patrol less;
    /// - every creature calls less in rain; the senses lose hearing and smell (PerceptionConfig.rainMask, rainSmellLoss,
    ///   stormHearingLoss; rain on the scent puffs is PerceptionConfig.rainScentCut).
    /// Readers call Refresh() (a float compare) and then read the cached values.
    /// </summary>
    public static class WildlifeWeather
    {
        /// <summary>rain 0..1 (WeatherManager.Intensity)</summary>
        public static float Rain { get; private set; }
        /// <summary>0..1 how stormy (StormK; at least 0.5 while the state is Storm)</summary>
        public static float Storm { get; private set; }
        /// <summary>rain above WildlifeConfig.rainMinIntensity, 0..1</summary>
        public static float RainK { get; private set; }
        public static bool IsStorm { get; private set; }
        public static bool HeavyRain { get; private set; }
        /// <summary>it rains enough to matter to animals (light rain and up)</summary>
        public static bool Wet { get; private set; }
        /// <summary>herbivores go to tree cover and rest there (rain at WildlifeConfig.shelterIntensity and up, or a storm)</summary>
        public static bool Shelter { get; private set; }
        /// <summary>flyers land and wait (storm, or rain at WildlifeConfig.flyerLandRain and up)</summary>
        public static bool FlyersGrounded { get; private set; }
        /// <summary>x call rate (1 dry .. stormCallMul)</summary>
        public static float CallMul { get; private set; } = 1f;
        /// <summary>x chance to start a hunt (0 in a storm)</summary>
        public static float HuntMul { get; private set; } = 1f;
        /// <summary>x predator roaming radius, idle patience and prey watching</summary>
        public static float PatrolMul { get; private set; } = 1f;
        /// <summary>x herbivore roaming radius (loners)</summary>
        public static float HerbRoamMul { get; private set; } = 1f;
        /// <summary>x herd spread while grazing / resting (bunching)</summary>
        public static float HerdSpreadMul { get; private set; } = 1f;
        /// <summary>0..1 extra chance an idle creature lies down instead of moving on</summary>
        public static float RestBias { get; private set; }
        /// <summary>times the values were sampled (tests / profiling)</summary>
        public static int Samples { get; private set; }

        static float _next = -1f;
        static bool _override; static float _oRain, _oStorm;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _next = -1f; _override = false; _oRain = _oStorm = 0f; Samples = 0;
            Rain = Storm = RainK = RestBias = 0f; IsStorm = HeavyRain = Wet = Shelter = FlyersGrounded = false;
            CallMul = HuntMul = PatrolMul = HerbRoamMul = HerdSpreadMul = 1f;
        }

        /// <summary>tests / story: use this weather (rain 0..1, storm 0..1: 0.5 and up counts as a storm) until ClearOverride</summary>
        public static void SetOverride(float rain, float storm) { _override = true; _oRain = Mathf.Clamp01(rain); _oStorm = Mathf.Clamp01(storm); _next = -1f; Refresh(); }
        public static void ClearOverride() { _override = false; _next = -1f; Refresh(); }

        /// <summary>re-sample when the interval is over (cheap to call from every creature)</summary>
        public static void Refresh()
        {
            float now = Time.time;
            if (_next >= 0f && now < _next && now > _next - 120f) return;
            _next = now + Mathf.Max(0.2f, WildlifeConfig.Instance.weatherCheckSeconds);
            Samples++;
            if (_override) { Apply(_oRain, _oStorm, _oStorm >= 0.5f); return; }
            if (DinoSenses.WeatherOverride) { Apply(DinoSenses.RainOverride, 0f, false); return; }          // perception tests drive the rain
            var wm = WeatherManager.Instance;
            if (!wm) { Apply(0f, 0f, false); return; }
            bool storm = wm.IsStorm;
            Apply(wm.Intensity, storm ? Mathf.Max(0.5f, wm.StormK) : wm.StormK, storm);
        }

        static void Apply(float rain, float storm, bool isStorm)
        {
            var w = WildlifeConfig.Instance;
            Rain = Mathf.Clamp01(rain); Storm = Mathf.Clamp01(storm); IsStorm = isStorm || Storm >= 0.5f;
            RainK = Mathf.InverseLerp(w.rainMinIntensity, 1f, Rain);
            Wet = Rain >= w.rainMinIntensity;
            HeavyRain = Rain >= w.heavyRainIntensity;
            Shelter = IsStorm || Rain >= w.shelterIntensity;
            FlyersGrounded = IsStorm || Rain >= w.flyerLandRain;
            CallMul = IsStorm ? w.stormCallMul : Mathf.Lerp(1f, w.rainCallMul, RainK);
            HuntMul = IsStorm ? 0f : HeavyRain ? w.heavyRainHuntMul : Mathf.Lerp(1f, w.rainHuntMul, RainK);
            PatrolMul = IsStorm ? w.stormPatrolMul : HeavyRain ? w.heavyRainPatrolMul : Mathf.Lerp(1f, w.rainPatrolMul, RainK);
            HerbRoamMul = IsStorm ? w.stormRoamMul : Mathf.Lerp(1f, w.rainRoamMul, RainK);
            HerdSpreadMul = IsStorm ? w.stormSpread : Mathf.Lerp(1f, w.rainSpread, RainK);
            RestBias = IsStorm ? w.stormRestBias : RainK * w.rainRestBias;
        }
    }
}
