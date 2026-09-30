using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PrimalFrontier.Core;
using PrimalFrontier.Survival;
using PrimalFrontier.World;
using PrimalFrontier.Player;

namespace PrimalFrontier.Tests
{
    /// <summary>
    /// WORLD (PC phase), no scene: the weather schedule keeps storms rare, short and apart; day phases and their event;
    /// wet surfaces stay wet after the rain before drying; the volcano heat rings warn, heat the air gradually and apply
    /// the heat effects only near the lava (never instantly lethal).
    /// </summary>
    public class WorldAtmosphereTests
    {
        readonly List<Object> _made = new List<Object>();
        double _clock; Transform _playerWas;
        T Keep<T>(T o) where T : Object { _made.Add(o); return o; }

        [UnitySetUp] public IEnumerator ClearScene() { yield return TestScenes.ClearIfGameplayLeft(); }
        [SetUp] public void SetUp() { _clock = GameClock.Now; _playerWas = PlayerLocator.Player; }
        [TearDown] public void TearDown()
        {
            foreach (var o in _made) if (o) Object.Destroy(o);
            _made.Clear(); GameClock.Now = _clock; PlayerLocator.Player = _playerWas; WeatherConfig.Override(null);
        }

        [Test] public void Storms_Are_Rare_Short_And_Never_Back_To_Back()
        {
            var c = ScriptableObject.CreateInstance<WeatherConfig>(); _made.Add(c);
            var rnd = new System.Random(11);
            var p = new WeatherPlanner(Rules(c), () => (float)rnd.NextDouble());
            double lastEnd = -1, minGap = double.MaxValue, stormStart = 0, longest = 0; int storms = 0; var prev = p.State;
            for (double h = 0; h < 24 * 200; h += 1.0 / 90.0)
            {
                p.Tick(h, true);
                if (p.State == prev) continue;
                if (p.State == WeatherState.Storm) { storms++; stormStart = h; if (lastEnd >= 0) minGap = System.Math.Min(minGap, h - lastEnd); }
                if (prev == WeatherState.Storm) { lastEnd = h; longest = System.Math.Max(longest, h - stormStart); }
                prev = p.State;
            }
            Debug.Log($"[WorldAtmosphereTests] 200 days: {storms} storms, longest {longest:F2} h, closest {minGap:F1} h apart");
            Assert.Greater(storms, 5, "storms happen");
            Assert.Less(storms, 200 / 2, "storms are rare (fewer than one every two days)");
            Assert.LessOrEqual(longest, c.stormHours.y + 0.02, "storms are short");
            Assert.GreaterOrEqual(minGap, c.minHoursBetweenStorms - 0.02, "never back to back");
        }

        static WeatherPlanner.Rules Rules(WeatherConfig c) => new WeatherPlanner.Rules
        {
            cloudChancePerHour = c.cloudChancePerHour, rainChancePerHour = c.rainChancePerHour, lightRainShare = c.lightRainShare, heavyRainShare = c.heavyRainShare,
            heavyRainBuild = c.heavyRainBuild, stormChance = c.stormChance, firstStormAfterHours = c.firstStormAfterHours, minHoursBetweenStorms = c.minHoursBetweenStorms,
            cloudMin = c.cloudHours.x, cloudMax = c.cloudHours.y, afterRainMin = c.afterRainCloudHours.x, afterRainMax = c.afterRainCloudHours.y, rainMin = c.rainHours.x, rainMax = c.rainHours.y,
            heavyMin = c.heavyRainHours.x, heavyMax = c.heavyRainHours.y, leadMin = c.stormLeadHours.x, leadMax = c.stormLeadHours.y, stormMin = c.stormHours.x, stormMax = c.stormHours.y,
            tailMin = c.stormTailHours.x, tailMax = c.stormTailHours.y, lightRain = c.lightRain, normalRain = c.normalRain, heavyRain = c.heavyRain, stormRain = c.stormRain,
        };

        [UnityTest, Timeout(30000)] public IEnumerator Day_Phases_Raise_Events_And_Light_Stays_Natural()
        {
            var lightGo = Keep(new GameObject("TestSun")); var sun = lightGo.AddComponent<Light>(); sun.type = LightType.Directional;
            var go = Keep(new GameObject("TestTime")); var tm = go.AddComponent<TimeManager>(); tm.sun = sun; tm.secondsPerHour = 90f;
            yield return null;
            tm.paused = true;
            Assert.AreEqual(TimeManager.Phase.Night, tm.PhaseAt(3f)); Assert.AreEqual(TimeManager.Phase.Dawn, tm.PhaseAt(6f));
            Assert.AreEqual(TimeManager.Phase.Morning, tm.PhaseAt(9f)); Assert.AreEqual(TimeManager.Phase.Noon, tm.PhaseAt(12.5f));
            Assert.AreEqual(TimeManager.Phase.Afternoon, tm.PhaseAt(16f)); Assert.AreEqual(TimeManager.Phase.Dusk, tm.PhaseAt(19f));
            Assert.AreEqual(TimeManager.Phase.Night, tm.PhaseAt(20.5f));
            var seen = new List<string>();
            System.Action<GameEvent> h = e => { if (e.type == GameEventType.DayPhaseChanged) seen.Add(e.id); };
            GameEvents.Raised += h;
            try { tm.Set(1, 17.5f); tm.SkipHours(1f); tm.SkipHours(2f); }
            finally { GameEvents.Raised -= h; }
            CollectionAssert.AreEqual(new[] { "Dusk", "Night" }, seen, "DayPhaseChanged at 18:00 and 20:00");
            // sunset: the direct light is golden, the sky ambient stays cooler (not all orange); night keeps a readable floor
            tm.Set(1, 19.1f);
            Assert.Greater(sun.color.r, sun.color.b + 0.3f, "golden sunset light");
            var amb = RenderSettings.ambientSkyColor; Assert.GreaterOrEqual(amb.b, amb.r, "sunset ambient is not orange");
            tm.Set(1, 23f);
            Assert.Greater(sun.intensity, 0.1f, "moonlight at night"); Assert.Greater(sun.color.b, sun.color.r, "cool night light");
            Assert.Greater(RenderSettings.ambientSkyColor.b, 0.15f, "night ambient floor");
            tm.Set(1, 15f);
            Assert.Greater(sun.intensity, 1.3f, "strong afternoon sun");
        }

        [UnityTest, Timeout(30000)] public IEnumerator Surfaces_Stay_Wet_After_Rain_Then_Dry()
        {
            var c = ScriptableObject.CreateInstance<WeatherConfig>(); _made.Add(c);
            c.soakSeconds = 1f; c.dryDelaySeconds = 1.2f; c.drySecondsClear = 2f; c.drySecondsOvercast = 2f; c.rainRampSeconds = 0.2f; c.cloudRampSeconds = 0.2f;
            var go = Keep(new GameObject("TestWeather")); var wm = go.AddComponent<WeatherManager>(); wm.config = c; wm.allowRandom = false;
            wm.SetWeather(WeatherState.Rain, -1f, false);
            float t = 0f; while (wm.Wetness < 0.95f && t < 5f) { t += Time.deltaTime; yield return null; }
            Assert.Greater(wm.Wetness, 0.95f, "soaked in the rain");
            Assert.AreEqual(wm.Wetness, Shader.GetGlobalFloat("_PF_Wetness"), 1e-4f, "_PF_Wetness drives the shaders");
            wm.SetWeather(WeatherState.Clear, -1f, false);
            yield return new WaitForSeconds(0.8f);
            Assert.Greater(wm.Wetness, 0.95f, "still wet just after the rain (drying lag)");
            yield return new WaitForSeconds(2.2f);
            Assert.Less(wm.Wetness, 0.8f, "drying after the lag");
        }

        [UnityTest, Timeout(30000)] public IEnumerator Volcano_Heat_Warns_Gradually_And_Hurts_Only_Near_The_Lava()
        {
            var zGo = Keep(new GameObject("TestVolcano")); zGo.transform.position = new Vector3(6000f, 0f, 6000f);
            var z = zGo.AddComponent<HazardZone>(); z.hazardId = "volcano"; z.warmRadius = 60f; z.hotRadius = 30f; z.dangerRadius = 12f; z.smoke = 0f;
            var pGo = Keep(new GameObject("TestPlayer")); pGo.tag = "Player"; pGo.transform.position = zGo.transform.position + new Vector3(100f, 0f, 0f);
            var hp = pGo.AddComponent<PlayerHealth>(); var fx = pGo.GetComponent<PlayerStatusEffects>();
            Assert.IsNotNull(fx); PlayerLocator.Player = pGo.transform;
            var levels = new List<int>();
            System.Action<GameEvent> h = e => { if (e.type == GameEventType.HazardWarning) levels.Add(e.amount); };
            GameEvents.Raised += h;
            try
            {
                float prevHeat = -1f;
                foreach (var d in new[] { 100f, 50f, 25f, 8f })
                {
                    pGo.transform.position = zGo.transform.position + new Vector3(d, 0f, 0f);
                    float heat = HazardZone.TotalHeatAt(pGo.transform.position);
                    Assert.Greater(heat, prevHeat, "the air gets hotter towards the centre"); prevHeat = heat;
                    yield return new WaitForSeconds(0.35f);
                }
                CollectionAssert.AreEqual(new[] { 1, 2, 3 }, levels, "warm -> hot -> dangerous");
                Assert.IsTrue(fx.Has(HazardZoneMonitor.SevereHeatId), "scorching heat near the lava");
                Assert.IsFalse(fx.Has(HazardZoneMonitor.HeatId), "one heat effect at a time");
                Assert.Less(fx.StaminaRegenMultiplier, 0.6f, "stamina recovers slowly");
                float h0 = hp.Health; yield return new WaitForSeconds(1f);
                float lost = h0 - hp.Health;
                Assert.Greater(lost, 0.05f, "mild heat damage in the inner ring"); Assert.Less(lost, 1f, "never instantly lethal");
                pGo.transform.position = zGo.transform.position + new Vector3(25f, 0f, 0f);
                yield return new WaitForSeconds(0.35f);
                Assert.IsTrue(fx.Has(HazardZoneMonitor.HeatId) && !fx.Has(HazardZoneMonitor.SevereHeatId), "hot ring: heat without damage");
                pGo.transform.position = zGo.transform.position + new Vector3(200f, 0f, 0f);
                yield return new WaitForSeconds(0.35f);
                Assert.IsFalse(fx.Has(HazardZoneMonitor.HeatId) || fx.Has(HazardZoneMonitor.SevereHeatId), "effects end away from the volcano");
                Assert.AreEqual(0, levels[levels.Count - 1], "safe again");
            }
            finally
            {
                GameEvents.Raised -= h;
                if (HazardZoneMonitor.Instance) Object.Destroy(HazardZoneMonitor.Instance.gameObject);
            }
        }
    }
}
