using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine;
using PrimalFrontier.AI;
using PrimalFrontier.Core;
using PrimalFrontier.Player;
using PrimalFrontier.VFX;
using PrimalFrontier.World;
using Object = UnityEngine.Object;

namespace PrimalFrontier.Tests
{
    /// <summary>
    /// Perception without a scene (AI/PERCEPTION_DESIGN.md 12): the Stimuli rings, scent puff maths, visibility and
    /// hearing formulas, the awareness meter (rise, hold, decay, hysteresis, noise ceiling), motion look points, fire fear
    /// radius (night / rain / fuel), bare-hand scaling, and zero allocation in the sensing path. A controlled clock
    /// (Stimuli.Clock) and a test player signature (PlayerSignature.SetTest) make every step deterministic.
    /// </summary>
    public class PerceptionTests
    {
        float _t;
        PerceptionConfig _cfg;
        readonly System.Collections.Generic.List<Object> _made = new System.Collections.Generic.List<Object>();

        [SetUp]
        public void Setup()
        {
            _t = 1000f; Stimuli.Clock = () => _t; Stimuli.Clear();
            PlayerSignature.ClearTest();
            DinoSenses.WeatherOverride = true; DinoSenses.WindDirOverride = new Vector2(1f, 0f); DinoSenses.WindStrengthOverride = 1f; DinoSenses.RainOverride = 0f;
            _cfg = ScriptableObject.CreateInstance<PerceptionConfig>(); _made.Add(_cfg); PerceptionConfig.Override(_cfg);
        }

        [TearDown]
        public void Teardown()
        {
            foreach (var o in _made) if (o) Object.DestroyImmediate(o);
            _made.Clear();
            Stimuli.Clock = null; Stimuli.Clear(); PlayerSignature.ClearTest(); DinoSenses.WeatherOverride = false; PerceptionConfig.Override(null);
        }

        DinosaurDefinition Def(Temperament t = Temperament.Predator)
        {
            var d = ScriptableObject.CreateInstance<DinosaurDefinition>(); _made.Add(d);
            d.id = "test_" + t; d.temperament = t; d.sightRange = 45f; d.hearingRange = 27f; d.fov = 220f; d.bodyRadius = 0.4f;
            d.sightGain = 1.1f; d.hearingGain = 1.3f; d.smellSensitivity = 1.4f; d.nightVision = 0.45f; d.awarenessDecay = 0.1f; d.memorySeconds = 20f;
            d.meatDrive = t == Temperament.Predator ? 1f : 0f;
            return d;
        }

        DinosaurController Dino(DinosaurDefinition def, Vector3 pos)
        {
            var go = new GameObject("TestDino_" + def.id); _made.Add(go);
            go.transform.SetPositionAndRotation(pos, Quaternion.identity);       // facing +z
            var dc = go.AddComponent<DinosaurController>(); dc.def = def;
            return dc;
        }

        static PlayerSignature.Snapshot Snap(Vector3 pos, float ambient = 1f, float local = 0f, float beacon = 1f, float posture = 1f, float motion = 1f, float cover = 0f)
        {
            var s = PlayerSignature.Snapshot.Neutral;
            s.valid = true; s.alive = true; s.position = pos; s.aimPoint = pos + Vector3.up * 1.35f;
            s.ambient = ambient; s.local = local; s.beacon = beacon; s.posture = posture; s.motion = motion; s.cover = cover;
            return s;
        }

        // ------------------------------------------------------------------ Stimuli
        [Test]
        public void Stimuli_Ring_Overwrites_Oldest_And_Keeps_Sequence()
        {
            int first = Stimuli.TransientHead + 1;
            for (int i = 0; i < 300; i++) Stimuli.Noise(new Vector3(i, 0, 0), 1f, NoiseTag.Footstep, StimulusSource.Player);
            int head = Stimuli.TransientHead;
            Assert.AreEqual(first + 299, head, "sequence counts every write");
            Assert.AreEqual(head - Stimuli.TransientCapacity + 1, Stimuli.OldestTransient, "oldest still in the ring");
            Assert.IsTrue(Stimuli.TryGetTransient(head, _t, out var s) && s.seq == head && Mathf.Approximately(s.pos.x, 299f), "newest readable");
            Assert.IsTrue(Stimuli.TryGetTransient(head - Stimuli.TransientCapacity + 1, _t, out _), "oldest kept entry readable");
            Assert.IsFalse(Stimuli.TryGetTransient(head - Stimuli.TransientCapacity, _t, out _), "overwritten entry gone");
            _t += Stimuli.TransientLife + 0.01f;
            Assert.IsFalse(Stimuli.TryGetTransient(head, _t, out _), "expired after its life");
            Stimuli.Noise(Vector3.zero, 1f, NoiseTag.Chop, StimulusSource.Player);
            Stimuli.Clear();
            Assert.IsFalse(Stimuli.TryGetTransient(Stimuli.TransientHead, _t, out _), "Clear hides older entries");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator Emit_Read_And_Sense_Allocate_Nothing()
        {
            var dc = Dino(Def(), Vector3.zero);
            PlayerSignature.SetTest(Snap(new Vector3(0, 0, 12f)));
            var c = _cfg;
            for (int i = 0; i < 300; i++) Stimuli.Scent(new Vector3(i * 0.1f - 15f, 0, 3f), 0.8f, ScentKind.Meat, StimulusSource.World);
            void Round(int i)
            {
                _t += 0.05f;
                Stimuli.Noise(new Vector3(0, 0, 8f), 0.6f, NoiseTag.Footstep, StimulusSource.Player);
                if ((i & 7) == 0) Stimuli.Scent(new Vector3(-10f, 0, 2f), 1f, ScentKind.Meat, StimulusSource.World);
                if ((i & 15) == 0) Stimuli.Motion(new Vector3(2f, 0.6f, 9f), 0.8f, StimulusSource.Player);
                dc.Senses.Tick(dc, 0.2f, 0, c);
            }
            for (int i = 0; i < 300; i++) Round(i);                  // warm up (static class init, JIT)
            yield return AllocPerFrame(() => { for (int i = 0; i < 500; i++) Round(i); }, 10, (work, idle, probe) =>
            {
                Debug.Log($"[Perception] no-scene allocation: {work:F0} B/frame with 500 sensing rounds, {idle:F0} B/frame idle, probe {probe:F0} B/frame");
                Assert.Greater(probe - idle, 30000.0, "the per-frame allocation counter works (64 KB probe)");
                Assert.Less(work - idle, 2048.0, $"sensing allocates nothing ({work - idle:F0} B per frame over 500 rounds)");
            });
        }

        /// <summary>
        /// "GC Allocated In Frame" with the work vs without it, frames interleaved (work, idle, work, idle ...) and compared
        /// by median so a spike from another system in one frame does not count; plus frames with a known 64 KB allocation
        /// (proof the counter works). Mono has no per-thread byte counter, so allocation is measured per frame.
        /// </summary>
        public static IEnumerator AllocPerFrame(System.Action work, int frames, System.Action<double, double, double> result)
        {
            using (var rec = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame", frames * 6))
            {
                yield return null; yield return null;
                var busy = new long[frames]; var idle = new long[frames]; long probe = 0; object keep = null;
                for (int f = 0; f < frames; f++)
                {
                    work(); yield return null; busy[f] = rec.LastValue;
                    yield return null; idle[f] = rec.LastValue;
                }
                for (int f = 0; f < 3; f++) { keep = new byte[65536]; yield return null; probe += rec.LastValue; }
                GC.KeepAlive(keep);
                System.Array.Sort(busy); System.Array.Sort(idle);
                result(busy[frames / 2], idle[frames / 2], probe / 3.0);
            }
        }

        // ------------------------------------------------------------------ maths
        [Test]
        public void Scent_Puff_Drifts_Downwind_Decays_And_Rain_Shortens_It()
        {
            var m = _cfg.Puff;
            var s = new Stimulus { pos = new Vector3(5, 0, 5), time = 0f, strength = 1f, kind = StimulusKind.Scent };
            Stimuli.PuffAt(s, 0f, new Vector2(1, 0), 1f, 0f, m, out var c0, out var r0, out var k0);
            Assert.AreEqual(5f, c0.x, 1e-4f); Assert.AreEqual(m.radius0, r0, 1e-4f); Assert.AreEqual(1f, k0, 1e-4f);
            Stimuli.PuffAt(s, 10f, new Vector2(1, 0), 1f, 0f, m, out var c1, out var r1, out var k1);
            Assert.AreEqual(5f + m.drift * 10f, c1.x, 1e-3f, "drifts downwind (the way the wind blows)");
            Assert.AreEqual(5f, c1.z, 1e-3f);
            Assert.AreEqual(m.radius0 + m.spread * 10f, r1, 1e-3f, "spreads");
            Assert.AreEqual(Mathf.Exp(-10f / m.tau), k1, 1e-4f, "fades");
            Stimuli.PuffAt(s, m.tau * Mathf.Log(2f), new Vector2(1, 0), 1f, 0f, m, out _, out _, out var half);
            Assert.AreEqual(0.5f, half, 1e-3f, "half strength after tau ln 2");
            Stimuli.PuffAt(s, 10f, new Vector2(1, 0), 1f, 1f, m, out _, out _, out var wet);
            Assert.AreEqual(k1 * (1f - m.rainCut), wet, 1e-4f, "rain cuts the strength");
            Assert.IsFalse(Stimuli.PuffAt(s, m.maxAge + 1f, new Vector2(1, 0), 1f, 0f, m, out _, out _, out _), "gone after its max age");
            Assert.AreEqual(0f, Stimuli.Concentration(new Vector3(100, 0, 0), c1, r1, k1));
            Assert.AreEqual(k1, Stimuli.Concentration(c1, c1, r1, k1), 1e-4f, "full at the centre");
        }

        [Test]
        public void Scent_Is_Smelled_Downwind_Not_Upwind()
        {
            // wind blows +x at strength 1 (1.4 m/s): a meat puff 20 m upwind (-x) reaches the predator in ~14 s
            var down = Dino(Def(), new Vector3(0, 0, 0));
            var up = Dino(Def(), new Vector3(0, 0, 200f));
            Stimuli.Scent(new Vector3(-20f, 0.5f, 0f), 1f, ScentKind.Meat, StimulusSource.World);
            Stimuli.Scent(new Vector3(20f, 0.5f, 200f), 1f, ScentKind.Meat, StimulusSource.World);        // downwind of 'up': drifts away
            for (int i = 0; i < 100; i++) { _t += 0.2f; down.Senses.Tick(down, 0.2f, 0, _cfg); up.Senses.Tick(up, 0.2f, 0, _cfg); }
            Assert.Greater(down.Senses.Interest, _cfg.interestThreshold, "predator downwind smells the meat");
            Assert.IsTrue(down.Senses.HasInterest);
            Assert.Less(Vector3.Distance(down.Senses.InterestPos, new Vector3(-20f, 0.5f, 0f)), _cfg.scentLocCap + 0.1f, "it knows roughly where the smell came from");
            Assert.AreEqual(0f, up.Senses.Interest, 1e-4f, "predator upwind smells nothing");
        }

        [Test]
        public void Visibility_Factors_Order()
        {
            float stand = DinoSenses.Visibility(Snap(Vector3.zero, motion: _cfg.walkMotion), 0.2f);
            float crouch = DinoSenses.Visibility(Snap(Vector3.zero, posture: _cfg.crouchPosture, motion: _cfg.crouchWalkMotion), 0.2f);
            float still = DinoSenses.Visibility(Snap(Vector3.zero, motion: _cfg.stillMotion), 0.2f);
            float sprint = DinoSenses.Visibility(Snap(Vector3.zero, motion: _cfg.sprintMotion), 0.2f);
            Assert.Less(crouch, stand, "crouching hides"); Assert.Less(still, sprint, "stillness hides");
            float night = DinoSenses.Visibility(Snap(Vector3.zero, ambient: _cfg.nightAmbient, motion: _cfg.walkMotion), 0.2f);
            float torch = DinoSenses.Visibility(Snap(Vector3.zero, ambient: _cfg.nightAmbient, local: _cfg.torchLight, beacon: _cfg.torchNightBeacon, motion: _cfg.walkMotion, cover: 0.4f * _cfg.torchCoverMul), 0.2f);
            Assert.Less(night, stand, "darkness hides"); Assert.Greater(torch, night * 2f, $"a torch at night shows you (torch {torch:F2}, dark {night:F2})");
            float bush = DinoSenses.Visibility(Snap(Vector3.zero, posture: _cfg.crouchPosture, motion: _cfg.stillMotion, cover: _cfg.bushCrouchedCover), 0.2f);
            Assert.Less(bush, 0.1f, "crouched still in a bush is nearly invisible");
            // night vision lifts the dark, never the torch
            float nv0 = DinoSenses.Visibility(Snap(Vector3.zero, ambient: 0.25f, local: 1f), 0f), nv1 = DinoSenses.Visibility(Snap(Vector3.zero, ambient: 0.25f, local: 1f), 1f);
            Assert.AreEqual(nv0, nv1, 1e-5f);
            Assert.Greater(DinoSenses.Visibility(Snap(Vector3.zero, ambient: 0.25f), 0.45f), DinoSenses.Visibility(Snap(Vector3.zero, ambient: 0.25f), 0.1f), "night vision helps in the dark");
            Assert.LessOrEqual(DinoSenses.Visibility(Snap(Vector3.zero, local: 5f, beacon: 5f, motion: 5f), 1f), 1.5f, "clamped");
        }

        [Test]
        public void Hearing_Radius_By_Surface_And_Rain()
        {
            var d = Def();
            float rock = DinoSenses.HearingRadius(d, _cfg.walkStep * _cfg.SurfaceLoudness((int)Surface.Rock), 0f, _cfg);
            float grass = DinoSenses.HearingRadius(d, _cfg.walkStep * _cfg.SurfaceLoudness((int)Surface.Grass), 0f, _cfg);
            float storm = DinoSenses.HearingRadius(d, _cfg.walkStep * _cfg.SurfaceLoudness((int)Surface.Rock), 1f, _cfg);
            Assert.Greater(rock, grass, "rock is louder than grass");
            Assert.AreEqual(rock * (1f - _cfg.rainMask), storm, 1e-4f, "rain masks sound");
            Assert.AreEqual(d.hearingRange, DinoSenses.HearingRadius(d, 1f, 0f, _cfg), 1e-4f, "loudness 1 = a sprint step = hearingRange");
            Assert.Greater(DinoSenses.HearingRadius(d, _cfg.treeFallLoudness, 0f, _cfg), 60f, "a falling tree carries far");
        }

        // ------------------------------------------------------------------ awareness
        [Test]
        public void Awareness_Rises_With_Sight_Holds_Then_Decays_With_Hysteresis()
        {
            var dc = Dino(Def(), Vector3.zero);
            PlayerSignature.SetTest(Snap(new Vector3(0, 0, 10f), motion: _cfg.stillMotion));      // standing still 10 m ahead, daylight
            float engagedAt = -1f;
            for (int i = 0; i < 20 && engagedAt < 0f; i++) { _t += 0.2f; dc.Senses.Tick(dc, 0.2f, 0, _cfg); if (dc.Senses.Level == AwarenessLevel.Engaged) engagedAt = (i + 1) * 0.2f; }
            Assert.IsTrue(dc.Senses.SeesPlayer, "sees the player");
            Assert.Greater(engagedAt, 0f, "reaches Engaged"); Assert.LessOrEqual(engagedAt, 1.6f, $"quickly at 10 m (took {engagedAt:F1} s)");
            Assert.AreEqual(22.5f, dc.Senses.EffectiveSight, 0.01f, "45 m x 0.5 (still)");
            // the player vanishes: awareness holds decayHold, then falls at awarenessDecay
            PlayerSignature.SetTest(Snap(new Vector3(0, 0, 200f), motion: _cfg.stillMotion));
            for (int i = 0; i < 10; i++) { _t += 0.2f; dc.Senses.Tick(dc, 0.2f, 0, _cfg); }
            Assert.AreEqual(1f, dc.Senses.Awareness, 1e-4f, "held during the decay hold");
            Assert.IsTrue(dc.Senses.HasMemory, "remembers the last known position");
            Assert.Less(Vector3.Distance(dc.Senses.LastKnownPos, new Vector3(0, 0, 10f)), 0.01f, "at the last place it saw the player");
            for (int i = 0; i < 10; i++) { _t += 0.2f; dc.Senses.Tick(dc, 0.2f, 0, _cfg); }
            Assert.Less(dc.Senses.Awareness, 1f, "decays after the hold");
            // hysteresis
            Assert.AreEqual(AwarenessLevel.Investigating, DinoSenses.LevelFor(0.45f, AwarenessLevel.Investigating, _cfg), "stays until hysteresis below");
            Assert.AreEqual(AwarenessLevel.Suspicious, DinoSenses.LevelFor(0.39f, AwarenessLevel.Investigating, _cfg));
            Assert.AreEqual(AwarenessLevel.Engaged, DinoSenses.LevelFor(0.95f, AwarenessLevel.Engaged, _cfg));
            Assert.AreEqual(AwarenessLevel.Alerted, DinoSenses.LevelFor(0.85f, AwarenessLevel.Engaged, _cfg));
            Assert.AreEqual(AwarenessLevel.Alerted, DinoSenses.LevelFor(0.8f, AwarenessLevel.Unaware, _cfg), "rises at the threshold");
            // memory expires
            _t += 30f; dc.Senses.Tick(dc, 0.2f, 0, _cfg);
            Assert.IsFalse(dc.Senses.HasMemory, "memory expires after memorySeconds");
        }

        [Test]
        public void Noise_Alone_Never_Engages_And_Gives_A_Position()
        {
            var dc = Dino(Def(), Vector3.zero);
            PlayerSignature.SetTest(Snap(new Vector3(0, 0, -300f)));          // far behind, unseen
            for (int i = 0; i < 20; i++) { Stimuli.Noise(new Vector3(0, 0, -20f), 1f, NoiseTag.Chop, StimulusSource.Player); _t += 0.2f; dc.Senses.Tick(dc, 0.2f, 0, _cfg); }
            Assert.Less(dc.Senses.Awareness, _cfg.alerted, "a distant noise caps below Alerted");
            Assert.GreaterOrEqual(dc.Senses.Level, AwarenessLevel.Investigating, "but makes it investigate");
            Assert.IsTrue(dc.Senses.HasMemory);
            Assert.Less(Vector3.Distance(dc.Senses.LastKnownPos, new Vector3(0, 0, -20f)), _cfg.noiseLocError + 0.01f, "position with an error");
            for (int i = 0; i < 20; i++) { Stimuli.Noise(new Vector3(0, 0, -3f), 1.2f, NoiseTag.Chop, StimulusSource.Player); _t += 0.2f; dc.Senses.Tick(dc, 0.2f, 0, _cfg); }
            Assert.Less(dc.Senses.Awareness, 1f, "noise never reaches Engaged");
            Assert.AreEqual(AwarenessLevel.Alerted, dc.Senses.Level, "a loud noise close by can alert");
            // creature noises are ignored; a sleeping listener hears less
            var dc2 = Dino(Def(), new Vector3(500, 0, 0));
            for (int i = 0; i < 10; i++) { Stimuli.Noise(new Vector3(500, 0, -5f), 2f, NoiseTag.Voice, StimulusSource.Creature); _t += 0.2f; dc2.Senses.Tick(dc2, 0.2f, 0, _cfg); }
            Assert.AreEqual(0f, dc2.Senses.Awareness, 1e-4f, "other creatures' noises are ignored");
        }

        [Test]
        public void Motion_Flash_Makes_It_Look_There()
        {
            var dc = Dino(Def(), Vector3.zero);
            PlayerSignature.SetTest(Snap(new Vector3(0, 0, -300f)));
            var bush = new Vector3(3f, 0.6f, 12f);
            Stimuli.Motion(bush, 1f, StimulusSource.Player);
            _t += 0.1f; dc.Senses.Tick(dc, 0.2f, 0, _cfg);
            Assert.GreaterOrEqual(dc.Senses.Level, AwarenessLevel.Suspicious, $"a shaking bush in view is noticed ({dc.Senses.Awareness:F2})");
            Assert.IsTrue(dc.Senses.HasLookPoint);
            Assert.Less(Vector3.Distance(dc.Senses.LookPoint, bush), 0.01f, "looks at the bush");
            var behind = Dino(Def(), new Vector3(0, 0, 400f));
            behind.transform.rotation = Quaternion.Euler(0, 180f, 0);   // facing -z: a flash at +z is behind it
            Stimuli.Motion(new Vector3(0, 0.6f, 430f), 1f, StimulusSource.Player);
            _t += 0.1f; behind.Senses.Tick(behind, 0.2f, 0, _cfg);
            Assert.Less(behind.Senses.Awareness, _cfg.suspicious, "a flash outside the field of view is not seen");
        }

        // ------------------------------------------------------------------ fire fear, bare hands
        [UnityTest, Timeout(60000)]
        public IEnumerator Fire_Fear_Radius_Follows_Night_Rain_And_Fuel()
        {
            // SURV's Campfire read API: FuelIntensity01 (fuel + lighting ramp), State, Sheltered, RainedOn (checked once a second)
            var fireGo = new GameObject("TestFire"); _made.Add(fireGo);
            var fire = fireGo.AddComponent<Campfire>();
            fire.Restore(true, 300f);
            Assert.IsTrue(fire.IsLit);
            Assert.AreEqual(1f, FireSense.Intensity01(fire), 1e-4f, "a well-fed fire is at full strength");
            var f = new FireFearProfile { predatorFear = 0.85f, fearRadiusDay = 7f, fearRadiusNight = 10f, rainMultiplier = 0.6f, fuelMultiplier = 0.6f };
            Assert.AreEqual(7f, FireSense.FearRadius(f, fire, 0f), 1e-4f, "day radius");
            Assert.AreEqual(10f, FireSense.FearRadius(f, fire, 1f), 1e-4f, "night radius");
            fire.Restore(true, 60f);                                     // low fuel
            float low = FireSense.Intensity01(fire);
            Assert.AreEqual(0.53f, low, 0.01f, "Campfire.FuelIntensity01 at 60 s of fuel (SURV: 0.53)");
            Assert.AreEqual(7f * Mathf.Lerp(1f, low, 0.6f), FireSense.FearRadius(f, fire, 0f), 1e-3f, "a weak fire scares less");
            var ignore = f; ignore.ignoreBelowIntensity = 0.6f;
            Assert.AreEqual(0f, FireSense.FearRadius(ignore, fire, 0f), "the apex ignores a weak fire");
            var none = f; none.predatorFear = 0f;
            Assert.AreEqual(0f, FireSense.FearRadius(none, fire, 1f), "no fear, no radius");
            fire.Restore(true, 300f);
            var wmGo = new GameObject("TestWeather"); _made.Add(wmGo);
            var wm = wmGo.AddComponent<WeatherManager>(); wm.allowRandom = false;
            wm.SetWeather(WeatherState.Rain, -1f, true);
            yield return new WaitForSeconds(1.3f);                       // the fire checks rain / roof once a second
            Assert.IsTrue(FireSense.RainedOn(fire), "rain falls on the open fire (Campfire.RainedOn)");
            Assert.AreEqual(7f * 0.6f, FireSense.FearRadius(f, fire, 0f), 0.05f, "rain shrinks the radius");
            var shelterGo = new GameObject("TestShelter"); _made.Add(shelterGo);
            shelterGo.transform.position = fireGo.transform.position;
            shelterGo.AddComponent<Shelter>();
            yield return new WaitForSeconds(1.3f);
            Assert.IsTrue(FireSense.Sheltered(fire), "a shelter covers the fire (Campfire.Sheltered)");
            Assert.IsFalse(FireSense.RainedOn(fire), "no rain under the roof");
            Assert.AreEqual(7f, FireSense.FearRadius(f, fire, 0f), 0.05f, "a sheltered fire keeps its radius in the rain");
            Assert.Less(_cfg.shelteredScentMul, 1f, "a sheltered fire smells less");
            fire.SetLit(false, false);
            Assert.AreEqual(0f, FireSense.FearRadius(f, fire, 1f), "an unlit fire does not scare");
        }

        [Test]
        public void Unarmed_Hits_Scale_By_Body_Size()
        {
            var small = Def(); small.bodyRadius = 0.4f;
            var large = Def(); large.bodyRadius = 1.3f;
            var fixedScale = Def(); fixedScale.bodyRadius = 1.3f; fixedScale.unarmedDamageScale = 0.05f;
            Assert.AreEqual(1f, DinosaurController.UnarmedScale(small), 1e-4f, "small creatures take the full punch");
            Assert.AreEqual((0.5f / 1.3f) * (0.5f / 1.3f), DinosaurController.UnarmedScale(large), 1e-4f, "large ones barely");
            Assert.AreEqual(0.05f, DinosaurController.UnarmedScale(fixedScale), 1e-4f, "per-species override");
        }
    }
}
