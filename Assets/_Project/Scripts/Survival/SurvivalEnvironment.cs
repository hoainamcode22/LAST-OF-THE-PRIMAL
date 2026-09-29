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
    /// player exists; the PlayerSurvival static hooks then point here. Allocation free per query.
    /// Phase 3: water the feet stand in (sea below OceanShore.seaLevel, pond / stream surfaces of WaterSource), sun on the
    /// player (daylight x clear sky, 0 in the shade: one raycast towards the sun, PlayerSurvival asks twice a second) and
    /// roof cover (shelter, tent, cave zone).
    /// </summary>
    public static class SurvivalEnvironment
    {
        /// <summary>air temperature when no TimeManager exists (tests, empty scenes)</summary>
        public const float FallbackAir = 24f;

        static TimeManager _time; static WeatherManager _weather; static ZoneManager _zones;
        static PlayerEquipment _torch; static OceanShore _ocean; static int _mask = Physics.DefaultRaycastLayers;
        static System.Func<Vector3, float> _air, _heat, _depth, _sun; static System.Func<Vector3, bool> _rain, _roof;

        /// <summary>point the PlayerSurvival hooks at this world (null systems fall back to neutral values)</summary>
        public static void Hook(TimeManager time, WeatherManager weather, ZoneManager zones, GameObject player)
        {
            _time = time; _weather = weather; _zones = zones;
            BindPlayer(player);
            if (_air == null) { _air = AirAt; _rain = RainAt; _heat = HeatAt; _depth = WaterDepthAt; _sun = SunAt; _roof = ShelteredAt; }      // one delegate each, created once
            PlayerSurvival.AirTemperature = _air;
            PlayerSurvival.RainingAt = _rain;
            PlayerSurvival.HeatAt = _heat;
            PlayerSurvival.WaterDepthAt = _depth;
            PlayerSurvival.SunAt = _sun;
            PlayerSurvival.ShelteredAt = _roof;
            _ocean = Object.FindFirstObjectByType<OceanShore>();
        }

        /// <summary>the player whose torch warms (cached component, no per-frame GetComponent)</summary>
        public static void BindPlayer(GameObject player)
        {
            _torch = player ? player.GetComponent<PlayerEquipment>() : null;
            // the sun ray ignores the player's own colliders and triggers
            _mask = Physics.DefaultRaycastLayers & ~(player ? 1 << player.layer : 0);
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

        /// <summary>depth of water the feet at p stand in (sea / pond / stream), m; 0 on dry land</summary>
        public static float WaterDepthAt(Vector3 p)
        {
            float d = 0f;
            if (_ocean && p.y < _ocean.seaLevel - 0.02f)
            {
                var t = _ocean.terrain ? _ocean.terrain : Terrain.activeTerrain;
                if (!t || t.SampleHeight(p) + t.transform.position.y < _ocean.seaLevel) d = _ocean.seaLevel - p.y;
            }
            var all = WaterSource.All;
            for (int i = 0; i < all.Count; i++)
            {
                var w = all[i]; if (!w) continue;
                if (w.Closest(p, out var q) > 0.9f) continue;
                if (p.y < q.y + 0.03f) d = Mathf.Max(d, Mathf.Max(0.1f, q.y - p.y));
            }
            return d;
        }

        /// <summary>under a roof: a shelter / tent covers p, or a cave (indoor zone)</summary>
        public static bool ShelteredAt(Vector3 p) => Shelter.Covers(p + Vector3.up * 0.5f) || (_zones && _zones.IsIndoor(p));

        /// <summary>sun on the player at p: daylight x clear sky, 0 at night, under a roof or in the shade (a ray towards the sun)</summary>
        public static float SunAt(Vector3 p)
        {
            if (!_time || !_time.sun) return 0f;
            float day = _time.Daylight01; if (day <= 0.02f) return 0f;
            var toSun = -_time.sun.transform.forward; if (toSun.y < 0.05f) return 0f;
            float clear = 1f - (_weather ? _weather.Overcast : 0f) * 0.85f;
            if (clear <= 0.05f) return 0f;
            if (Physics.Raycast(p + Vector3.up * 1.4f, toSun, 80f, _mask, QueryTriggerInteraction.Ignore)) return 0f;
            return day * clear;
        }

        public static float HeatAt(Vector3 p)
        {
            float h = Campfire.HeatAt(p) + Shelter.WarmthAt(p);
            if (_torch && _torch.TorchLit) h += SurvivalConfig.Instance.torchWarmth;
            return h;
        }
    }
}
