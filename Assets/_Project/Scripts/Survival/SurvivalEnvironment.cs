using UnityEngine;
using PrimalFrontier.Core;
using PrimalFrontier.Player;
using PrimalFrontier.World;

namespace PrimalFrontier.Survival
{
    /// <summary>
    /// What the world does to the player's body: air temperature (TimeManager curve + SurvivalConfig night offset blended
    /// over dusk / dawn + zone offset - altitude), rain on this spot (WeatherManager, roofs and caves keep it off) and
    /// extra warmth (lit campfires, the covering shelter's own warmth, a lit torch). GameManager calls Hook once the
    /// player exists; the three PlayerSurvival static hooks then point here. Allocation free per query.
    /// </summary>
    public static class SurvivalEnvironment
    {
        /// <summary>air temperature when no TimeManager exists (tests, empty scenes)</summary>
        public const float FallbackAir = 24f;

        static TimeManager _time; static WeatherManager _weather; static ZoneManager _zones;
        static PlayerEquipment _torch;
        static System.Func<Vector3, float> _air, _heat; static System.Func<Vector3, bool> _rain;

        /// <summary>point the PlayerSurvival hooks at this world (null systems fall back to neutral values)</summary>
        public static void Hook(TimeManager time, WeatherManager weather, ZoneManager zones, GameObject player)
        {
            _time = time; _weather = weather; _zones = zones;
            BindPlayer(player);
            if (_air == null) { _air = AirAt; _rain = RainAt; _heat = HeatAt; }      // one delegate each, created once
            PlayerSurvival.AirTemperature = _air;
            PlayerSurvival.RainingAt = _rain;
            PlayerSurvival.HeatAt = _heat;
        }

        /// <summary>the player whose torch warms (cached component, no per-frame GetComponent)</summary>
        public static void BindPlayer(GameObject player)
        {
            _torch = player ? player.GetComponent<PlayerEquipment>() : null;
        }

        /// <summary>0 by day, 1 at night; smooth over nightBlendHours centred on sunset (in) and sunrise (out)</summary>
        public static float NightBlend(float hour, float sunrise, float sunset, float blendHours)
        {
            float half = Mathf.Max(0.01f, blendHours) * 0.5f;
            float t = hour >= (sunrise + sunset) * 0.5f
                ? Mathf.InverseLerp(sunset - half, sunset + half, hour)            // afternoon -> midnight: dusk ramp
                : 1f - Mathf.InverseLerp(sunrise - half, sunrise + half, hour);    // midnight -> noon: dawn ramp
            return t * t * (3f - 2f * t);
        }

        /// <summary>current night blend of the scene's TimeManager (0 without one)</summary>
        public static float NightNow()
        {
            if (!_time) return 0f;
            return NightBlend(_time.hour, _time.sunriseHour, _time.sunsetHour, SurvivalConfig.Instance.nightBlendHours);
        }

        public static float AirAt(Vector3 p)
        {
            var c = SurvivalConfig.Instance;
            float air = _time ? _time.AirTemperature() + c.nightAirOffset * NightNow() : FallbackAir;
            if (_zones) air += _zones.TemperatureOffset(p);
            return air - Mathf.Max(0f, p.y - c.altitudeCoolingStart) * c.altitudeCooling;
        }

        public static bool RainAt(Vector3 p) => _weather && _weather.RainingAt(p);

        public static float HeatAt(Vector3 p)
        {
            float h = Campfire.HeatAt(p) + Shelter.WarmthAt(p);
            if (_torch && _torch.TorchLit) h += SurvivalConfig.Instance.torchWarmth;
            return h;
        }
    }
}
