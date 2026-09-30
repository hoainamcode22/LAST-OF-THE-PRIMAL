using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PrimalFrontier.AI;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;

namespace PrimalFrontier.Tests
{
    /// <summary>
    /// Wildlife layer on the island (PC phase, targeted): herds are loose groups of 3-12 with their own pace, predators hold
    /// their territories, a herd walks to water at noon and drinks, the migration walks the valley route and is seen from
    /// the lookout (MigrationStarted / MigrationSeen), creatures leave tracks and examining one raises TracksFound.
    /// Run only these when checking the wildlife: PrimalTestRunner.RunPlayMode "WildlifeTests".
    /// </summary>
    public class WildlifeTests
    {
        GameManager _gm;
        readonly List<GameEvent> _events = new List<GameEvent>();
        void OnEvent(GameEvent e) => _events.Add(e);

        IEnumerator LoadIsland()
        {
            TestScenes.UseTestSaves(); GameManager.ForceShowTitle = false; GameManager.ForcePlayIntro = false; PlayerInputReader.Simulate = true;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/Island_VerticalSlice.unity", new LoadSceneParameters(LoadSceneMode.Single));
#endif
            float t = 0; while ((_gm = GameManager.Instance) == null || _gm.State != GameState.Playing) { t += Time.unscaledDeltaTime; if (t > 20f) break; yield return null; }
            yield return new WaitForSeconds(1.5f);
            Assert.IsNotNull(_gm, "island loaded");
            _gm.Player.GetComponent<PlayerHealth>().InvulnerableUntil = Time.time + 9999f;
            _events.Clear(); GameEvents.Raised -= OnEvent; GameEvents.Raised += OnEvent;
        }

        [TearDown] public void Cleanup()
        {
            GameEvents.Raised -= OnEvent;
            GameManager.ForceShowTitle = null; GameManager.ForcePlayIntro = null; PlayerInputReader.Simulate = false; Time.timeScale = 1f;
        }
        [UnityTearDown] public IEnumerator Unload() { yield return TestScenes.Clear(); }

        static Vector3 Ground(Vector3 p) { var t = Terrain.activeTerrain; if (t) p.y = t.SampleHeight(p) + t.transform.position.y; return p; }
        static void SetHour(float h) { var tm = TimeManager.Instance; tm.Set(tm.day, h); tm.paused = true; }
        void Warp(Vector3 p, Vector3 face) { var f = face - p; f.y = 0; _gm.Player.GetComponent<PlayerMotor>().Warp(Ground(p) + Vector3.up * 0.05f, Quaternion.LookRotation(f.sqrMagnitude > 0.01f ? f : Vector3.forward)); }

        [UnityTest, Timeout(90000)]
        public IEnumerator Herds_Are_Loose_Groups_With_Their_Own_Pace_And_Predators_Hold_Territories()
        {
            yield return LoadIsland();
            SetHour(9f);
            yield return new WaitForSeconds(3.5f);                    // herds scan their members
            var herds = HerdGroup.All.Where(h => h && h.species).ToList();
            Assert.GreaterOrEqual(herds.Count, 3, "three herds on the island");
            foreach (var h in herds)
            {
                int n = h.AliveCount;
                Assert.That(n, Is.InRange(3, 12), $"{h.label}: 3-12 members");
                var paces = h.Members.Where(m => m).Select(m => Mathf.Round(m.HerdPace * 1000f)).Distinct().Count();
                Assert.Greater(paces, 1, $"{h.label}: members walk at their own pace");
                float closest = float.MaxValue;
                for (int i = 0; i < h.Members.Count; i++)
                    for (int j = i + 1; j < h.Members.Count; j++)
                        closest = Mathf.Min(closest, Vector3.Distance(h.Members[i].transform.position, h.Members[j].transform.position));
                Assert.Greater(closest, 1f, $"{h.label}: no two members stacked");
                Debug.Log($"[Wildlife] {h.label}: {n} members, closest pair {closest:F1} m, activity {h.Activity}, side {h.Side}");
            }
            Assert.IsNotNull(Object.FindFirstObjectByType<MigrationDirector>(), "the migration herd has its director");
            foreach (var row in WildlifePlan.Territories)
            {
                var ds = DinosaurController.All.Where(d => d && d.def && d.def.id == row.species).ToList();
                Assert.GreaterOrEqual(ds.Count, row.count, $"{row.species}: {row.count} in its territory");
                foreach (var d in ds) Assert.AreEqual(row.homeRadius, d.homeRadius, 0.01f, $"{d.name}: territory radius");
                Debug.Log($"[Wildlife] {row.species} ({row.role}): home {ds[0].home}");
            }
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator Herd_Walks_To_Water_At_Noon_And_Drinks()
        {
            yield return LoadIsland();
            var herd = HerdGroup.All.FirstOrDefault(h => h && h.species && !h.away.Valid && h.WaterPoint != Vector3.zero);
            Assert.IsNotNull(herd, "a herd with fresh water in reach");
            Warp(herd.Centroid + Vector3.right * 110f, herd.Centroid);
            SetHour(11.3f);                                            // noon: time to drink
            Time.timeScale = 4f;
            float t0 = Time.realtimeSinceStartup; bool drinkTime = false, drinking = false;
            while (Time.realtimeSinceStartup - t0 < 90f && !drinking)
            {
                drinkTime |= herd.Activity == HerdActivity.Drink;
                drinking = herd.Members.Any(m => m && m.State == DinoState.Drink);
                yield return null;
            }
            Time.timeScale = 1f;
            Debug.Log($"[Wildlife] {herd.label} at noon: activity {herd.Activity}, drinking {drinking}, {Vector3.Distance(herd.Centroid, herd.WaterPoint):F0} m from the water");
            Assert.IsTrue(drinkTime, $"the herd goes to water at noon (activity {herd.Activity})");
            Assert.IsTrue(drinking, "members drink at the water");
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator Migration_Walks_The_Route_And_Is_Seen_From_The_Lookout()
        {
            yield return LoadIsland();
            // the schedule: one migration every second morning, the herd on the far side after an odd count
            var tm = TimeManager.Instance;
            Assert.AreEqual(0, MigrationDirector.CompletedBy(1, 9f, tm)); Assert.AreEqual(0, MigrationDirector.CompletedBy(2, 8f, tm)); Assert.AreEqual(1, MigrationDirector.CompletedBy(2, 12f, tm));
            Assert.AreEqual(1, MigrationDirector.SideAfter(1)); Assert.AreEqual(0, MigrationDirector.SideAfter(2));
            var dir = Object.FindFirstObjectByType<MigrationDirector>();
            Assert.IsNotNull(dir, "migration director"); dir.scheduled = false;
            var herd = dir.herd; Assert.IsNotNull(herd, "migration herd");
            SetHour(8f);
            if (herd.Side != 0) { herd.Relocate(0); yield return new WaitForSeconds(1f); }
            Assert.IsTrue(WildlifePlan.Resolve("migration_view", out var view, out _), "the lookout");
            Warp(view, herd.Centroid);
            yield return new WaitForSeconds(0.5f);
            dir.Begin(1);
            Assert.IsTrue(herd.OnRoute, "the herd sets off along the route");
            Assert.IsTrue(_events.Any(e => e.type == GameEventType.MigrationStarted && e.id == herd.species.id && e.amount == 1), "MigrationStarted (species id, outward)");
            var cam = Camera.main ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
            if (cam) cam.InputEnabled = false;
            Time.timeScale = 3f;
            float t0 = Time.realtimeSinceStartup; bool seen = false;
            while (Time.realtimeSinceStartup - t0 < 80f && !seen)
            {
                if (cam) { var to = herd.Centroid - view; cam.Yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg; cam.Pitch = 6f; }
                seen = _events.Any(e => e.type == GameEventType.MigrationSeen);
                yield return null;
            }
            Time.timeScale = 1f;
            if (cam) cam.InputEnabled = true;
            Debug.Log($"[Wildlife] migration: route point {herd.RouteIndex}/{herd.RouteLength}, seen {seen} (watched {dir.WatchedSeconds:F1} s), dust {dir.DustPuffs}, calls {dir.Calls}, birds {BirdFlush.Bursts}, herd {Vector3.Distance(view, herd.Centroid):F0} m from the lookout");
            Assert.Greater(herd.RouteIndex, 1, "the herd moves along the route");
            Assert.IsTrue(seen, "MigrationSeen from the lookout");
            Assert.Greater(dir.DustPuffs, 0, "dust rises from the moving herd");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Creatures_Leave_Tracks_And_Examining_Them_Raises_TracksFound()
        {
            yield return LoadIsland();
            SetHour(9f);
            var para = DinosaurController.All.First(d => d && d.IsAlive && d.def && d.def.id == "parasaurolophus");
            Warp(para.transform.position + para.transform.right * 25f, para.transform.position);
            yield return new WaitForSeconds(0.5f);
            int before = TrackSigns.Instance.CountOf(TrackKind.Footprint);
            para.ForceState(DinoState.Wander);
            yield return new WaitForSeconds(8f);
            int after = TrackSigns.Instance.CountOf(TrackKind.Footprint);
            Debug.Log($"[Wildlife] tracks: footprints {before} -> {after}, all signs {TrackSigns.Instance.Count}, drawn {TrackSigns.Instance.Visible}");
            Assert.Greater(after, before, "a walking animal leaves footprints");
            // a fresh print right in front of the player: the prompt, then the thought and TracksFound
            var p = _gm.Player.transform.position + _gm.Player.transform.forward * 1.2f;
            TrackSigns.Step(para.def, p, _gm.Player.transform.forward, true);
            yield return new WaitForSeconds(0.5f);
            int sign = TrackSigns.FindExaminable(_gm.Player.transform.position, 3f);
            Assert.GreaterOrEqual(sign, 0, "a sign to examine next to the player");
            _events.Clear();
            string line = TrackSigns.Examine(sign);
            Debug.Log($"[Wildlife] examined {TrackSigns.KindOf(sign)}: \"{line}\"");
            Assert.IsFalse(string.IsNullOrEmpty(line), "a short thought");
            Assert.IsTrue(_events.Any(e => e.type == GameEventType.TracksFound && !string.IsNullOrEmpty(e.id)), "TracksFound with the species id");
            Assert.IsFalse(TrackSigns.IsExaminable(sign), "read signs are not offered again");
        }
    }
}
