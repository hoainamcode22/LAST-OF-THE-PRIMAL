using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PrimalFrontier.AI;
using PrimalFrontier.Combat;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.Survival;
using PrimalFrontier.World;

namespace PrimalFrontier.Tests
{
    /// <summary>
    /// Perception and wildlife on the island (AI/PERCEPTION_DESIGN.md 12): stealth in a bush at night, detection when
    /// moving in view, noise -> investigation, gameplay events -> noises, torch cost, shaking bushes, last known position
    /// and search, herd alerts, cooking scent with the wind (and upwind nothing), fire fear per species, felled trees
    /// open the ground, bare-hand scaling / knockback, creature save / restore, daily life (sleep, drink), AI tiers and
    /// the frame budget with zero allocation. Time is paused where the hour matters; the player is made invulnerable.
    /// </summary>
    public class PerceptionIslandTests
    {
        GameManager _gm;
        PerceptionConfig C => PerceptionConfig.Instance;

        IEnumerator LoadIsland()
        {
            TestScenes.UseTestSaves(); GameManager.ForceShowTitle = false; GameManager.ForcePlayIntro = false; PlayerInputReader.Simulate = true;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/Island_VerticalSlice.unity", new LoadSceneParameters(LoadSceneMode.Single));
#endif
            float t = 0; while ((_gm = GameManager.Instance) == null || _gm.State != GameState.Playing) { t += Time.unscaledDeltaTime; if (t > 20f) break; yield return null; }
            yield return new WaitForSeconds(1f);
            Assert.IsNotNull(_gm, "island loaded");
            _gm.Player.GetComponent<PlayerHealth>().InvulnerableUntil = Time.time + 9999f;
            _gm.Player.GetComponent<InventorySystem>().Clear();
        }

        [TearDown] public void Cleanup()
        {
            GameManager.ForceShowTitle = null; GameManager.ForcePlayIntro = null; PlayerInputReader.Simulate = false; Time.timeScale = 1f;
            PlayerInputReader.Sim.Move = Vector2.zero; PlayerInputReader.Sim.Sprint = false;
            DinoSenses.WeatherOverride = false; Stimuli.Clock = null;
        }
        [UnityTearDown] public IEnumerator Unload() { yield return TestScenes.Clear(); }

        // ------------------------------------------------------------------ helpers
        static Vector3 Ground(Vector3 p) { var t = Terrain.activeTerrain; if (t) p.y = t.SampleHeight(p) + t.transform.position.y; return p; }
        static void SetHour(float h) { var tm = TimeManager.Instance; tm.Set(tm.day, h); tm.paused = true; }
        PlayerMotor Motor => _gm.Player.GetComponent<PlayerMotor>();
        void Warp(Vector3 p, Vector3 face) { var f = face - p; f.y = 0; Motor.Warp(Ground(p) + Vector3.up * 0.05f, Quaternion.LookRotation(f.sqrMagnitude > 0.01f ? f : Vector3.forward)); }

        /// <summary>put a creature at p facing 'look', home there, calm</summary>
        static void Place(DinosaurController d, Vector3 p, Vector3 look)
        {
            p = Ground(p); d.home = p; var f = look - p; f.y = 0;
            d.transform.SetPositionAndRotation(p, Quaternion.LookRotation(f.sqrMagnitude > 0.01f ? f : Vector3.forward));
            d.Senses.ForgetAll(); d.ForceState(DinoState.Idle);
        }

        static bool Clear(Vector3 a, Vector3 b) => !Physics.Linecast(a + Vector3.up * 1.4f, b + Vector3.up * 1.4f, ~LayerMask.GetMask("Player"), QueryTriggerInteraction.Ignore);

        /// <summary>a spot 'dist' m from 'center' with open line of sight and gentle ground; open = no trees / bushes around it (no cover)</summary>
        static Vector3 OpenSpot(Vector3 center, float dist, bool open = false)
        {
            center = Ground(center);
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < 36; i++)
                {
                    var p = Ground(center + Quaternion.Euler(0, i * 10f, 0) * Vector3.forward * dist);
                    if (Mathf.Abs(p.y - center.y) > dist * 0.25f || p.y < 1f || !Clear(center, p)) continue;
                    if (open && pass == 0 && (CoverMap.TreesNear(p, 6f, out _) > 0 || CoverMap.BushesNear(p, 2f) > 0)) continue;
                    return p;
                }
            return Ground(center + Vector3.forward * dist);
        }

        /// <summary>a raptor home spot in the open (for tests that need the player seen clearly)</summary>
        static Vector3 OpenHome(DinosaurController d)
        {
            var h = Ground(d.home);
            for (int i = 0; i < 40; i++)
            {
                var p = Ground(h + Quaternion.Euler(0, i * 37f, 0) * Vector3.forward * (i * 1.5f));
                if (p.y > 1f && CoverMap.TreesNear(p, 8f, out _) == 0 && CoverMap.BushesNear(p, 3f) == 0) return p;
            }
            return h;
        }

        static DinosaurController Dino(string id) => DinosaurController.All.First(d => d && d.def && d.def.id == id && d.IsAlive);

        // ------------------------------------------------------------------ vision / hearing
        [UnityTest, Timeout(120000)]
        public IEnumerator Crouched_Still_In_Bush_At_Night_Is_Not_Detected()
        {
            yield return LoadIsland();
            SetHour(23f);
            var raptor = Dino("velociraptor");
            var bush = BushInteraction.All.Where(b => b && b.gameObject.activeInHierarchy && b.transform.position.y > 1.5f).OrderBy(b => (b.transform.position - raptor.home).sqrMagnitude).First();
            Vector3 bp = Ground(bush.transform.position);
            Motor.SetCrouch(true);
            Warp(bp, bp + Vector3.forward);
            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(bush, BushInteraction.PlayerBush, "the player stands in the bush");
            Vector3 rp = OpenSpot(bp, 12f);
            Vector3 dir = (rp - bp); dir.y = 0; dir.Normalize();
            DinoSenses.WeatherOverride = true; DinoSenses.WindDirOverride = new Vector2(-dir.x, -dir.z); DinoSenses.WindStrengthOverride = 0.35f;   // smell blows away from the raptor
            Place(raptor, rp, bp);
            float maxA = 0f; var t0 = Time.time;
            while (Time.time - t0 < 8f) { maxA = Mathf.Max(maxA, raptor.Senses.Awareness); raptor.home = rp; yield return null; }
            var snap = PlayerSignature.Current;
            Debug.Log($"[Perception] bush at night: cover {snap.cover:F2}, visibility to a raptor {DinoSenses.Visibility(snap, raptor.def.nightVision):F3}, sight {raptor.Senses.EffectiveSight:F1} m, max awareness {maxA:F2}");
            Assert.GreaterOrEqual(snap.cover, C.bushCrouchedCover - 0.01f, "crouched bush cover");
            Assert.Less(maxA, C.suspicious, $"a raptor 12 m away does not notice a crouched, still player in a bush at night (awareness {maxA:F2})");
            // control: stand up and step out into the open, still at night
            Motor.SetCrouch(false);
            Warp(bp + dir * 6f, rp);
            Place(raptor, rp, bp);                                          // it may have wandered off meanwhile
            t0 = Time.time; bool noticed = false;
            while (Time.time - t0 < 6f && !noticed) { noticed = raptor.Senses.Level >= AwarenessLevel.Suspicious; raptor.home = rp; yield return null; }
            Assert.IsTrue(noticed, $"standing in the open 6 m away is noticed (sight {raptor.Senses.EffectiveSight:F1} m)");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Still_At_25m_Unseen_Sprinting_Seen_Quickly()
        {
            yield return LoadIsland();
            SetHour(9.5f);
            var raptor = Dino("velociraptor");
            Vector3 rp = OpenHome(raptor);
            Vector3 pp = OpenSpot(rp, 25f, true);
            Place(raptor, rp, pp);
            Warp(pp, rp);
            float t0 = Time.time, maxStill = 0f;
            while (Time.time - t0 < 3f) { maxStill = Mathf.Max(maxStill, raptor.Senses.Awareness); raptor.home = rp; yield return null; }
            Debug.Log($"[Perception] still at 25 m: sight {raptor.Senses.EffectiveSight:F1} m, awareness {maxStill:F2}");
            Assert.Less(raptor.Senses.EffectiveSight, 25f, "standing still, the effective sight is under 25 m");
            Assert.Less(maxStill, C.suspicious, "not noticed standing still at 25 m");
            // sprint in view (along the clear line of sight toward it)
            var cam = Camera.main.GetComponent<ThirdPersonCamera>();
            var to = rp - pp; to.y = 0f;
            cam.Yaw = Quaternion.LookRotation(to).eulerAngles.y;
            PlayerInputReader.Sim.Move = new Vector2(0f, 1f); PlayerInputReader.Sim.Sprint = true;
            t0 = Time.time; float engagedAt = -1f, distAt = -1f;
            while (Time.time - t0 < 4f && engagedAt < 0f) { if (raptor.Senses.Level == AwarenessLevel.Engaged) { engagedAt = Time.time - t0; distAt = Vector3.Distance(raptor.transform.position, _gm.Player.transform.position); } yield return null; }
            PlayerInputReader.Sim.Move = Vector2.zero; PlayerInputReader.Sim.Sprint = false;
            Debug.Log($"[Perception] sprinting: engaged after {engagedAt:F2} s at {distAt:F1} m, sight {raptor.Senses.EffectiveSight:F1} m, speed {Motor.MeasuredPlanarSpeed:F1}");
            // footstep loudness uses the surface of the last footstep (RES: PlayerFeedback.LastFootSurface)
            yield return new WaitForSeconds(0.6f);
            var fb = _gm.Player.GetComponent<PlayerFeedback>(); Assert.IsNotNull(fb);
            Assert.AreEqual((int)fb.LastFootSurface, PlayerSignature.Current.surface, "the signature reads the footstep surface");
            Assert.Greater(engagedAt, 0f, $"sprinting in view is detected (sight {raptor.Senses.EffectiveSight:F1} m, awareness {raptor.Senses.Awareness:F2})");
            Assert.Less(engagedAt, 3f, "within 3 s");
            Assert.Greater(distAt, 10f, "long before it reaches the raptor");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Chopping_Noise_Draws_Predator_To_Investigate()
        {
            yield return LoadIsland();
            SetHour(9.5f);
            var carno = Dino("carnotaurus");
            Vector3 cp = OpenSpot(carno.home, 1f);
            Warp(cp + Vector3.forward * 80f, cp);                                   // far away, not the target
            Place(carno, cp, cp + Vector3.forward * 10f);
            Vector3 noise = Ground(cp - Vector3.forward * 22f);                    // behind it: not seen
            yield return new WaitForSeconds(0.5f);
            for (int i = 0; i < 3; i++) { Stimuli.Noise(noise, C.chopLoudness, NoiseTag.Chop, StimulusSource.Player); yield return new WaitForSeconds(1.2f); }
            float t0 = Time.time; bool investigating = false;
            while (Time.time - t0 < 4f && !investigating) { investigating = carno.State == DinoState.Investigate && Vector3.Distance(carno.Destination, noise) < C.noiseLocError + C.searchRadius; yield return null; }
            Debug.Log($"[Perception] chop noise: carnotaurus {carno.State}, awareness {carno.Senses.Awareness:F2}, destination {Vector3.Distance(carno.Destination, noise):F1} m from the noise");
            Assert.IsTrue(investigating, $"the carnotaurus investigates the chopping (state {carno.State}, awareness {carno.Senses.Awareness:F2})");
            float d0 = Vector3.Distance(carno.transform.position, noise);
            yield return new WaitForSeconds(4f);
            Assert.Less(Vector3.Distance(carno.transform.position, noise), d0 - 2f, "and walks toward it");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Gameplay_Events_Become_Noises_And_A_Falling_Tree_Startles_Flyers()
        {
            yield return LoadIsland();
            var p = _gm.Player.transform.position;
            GameEvents.Raise(GameEventType.CreatureHit, "test", 1, p + Vector3.forward);
            Assert.AreEqual(C.combatLoudness, Stimuli.LastPlayerNoise, 1e-4f, "a hit is heard (combat loudness)");
            GameEvents.Raise(GameEventType.StructurePlaced, "campfire", 1, p);
            Assert.AreEqual(C.buildLoudness, Stimuli.LastPlayerNoise, 1e-4f, "building is heard");
            GameEvents.Raise(GameEventType.ResourceGathered, "fiber", 1, p + Vector3.right * 60f);        // away from any tree next to the player
            Assert.AreEqual(C.handGatherLoudness, Stimuli.LastPlayerNoise, 1e-4f, "gathering by hand is quiet");
            // a tree: chop it (event at the tree), then fell it -> a tree-fall noise
            var th = Object.FindFirstObjectByType<TreeHarvest>(); Assert.IsNotNull(th, "trees");
            var near = new List<int>(); CoverMap.StandingTrees(p, 200f, near); Assert.Greater(near.Count, 0, "a standing tree");
            var tp = CoverMap.TreePosition(near[0]);
            Warp(tp + (p - tp).normalized * 1.4f, tp);
            yield return new WaitForSeconds(0.3f);
            Assert.GreaterOrEqual(th.Current, 0, "next to a tree");
            GameEvents.Raise(GameEventType.ResourceGathered, "wood", 2, th.FocusPoint - Vector3.up);
            th.Fell(th.Current, true);
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(C.treeFallLoudness, Stimuli.LastPlayerNoise, 1e-4f, "a falling tree is the loudest noise");
            var amb = Object.FindObjectsByType<AmbientCreature>(FindObjectsSortMode.None).FirstOrDefault(a => a.IsAlive);
            Assert.IsNotNull(amb, "an ambient creature");
            Stimuli.Noise(amb.transform.position, C.treeFallLoudness, NoiseTag.TreeFall, StimulusSource.Player);
            Assert.IsTrue(amb.Startled, "flyers / swimmers startle at a loud noise");
            th.RestoreAll();
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Torch_At_Night_Extends_Detection()
        {
            yield return LoadIsland();
            SetHour(23f);
            var raptor = Dino("velociraptor");
            yield return new WaitForSeconds(0.3f);
            float dark = DinoSenses.SightRange(raptor.def, PlayerSignature.Current, C);
            var inv = _gm.Player.GetComponent<InventorySystem>(); var torch = ItemDatabase.Instance.Item("torch"); Assert.IsNotNull(torch, "torch item");
            inv.SetSlot(0, new ItemStack(torch, 1)); int slot = 0; inv.SetActiveSlot(0);
            var eq = _gm.Player.GetComponent<PlayerEquipment>();
            float t0 = Time.time; while (Time.time - t0 < 3f && !eq.TorchLit) yield return null;
            Assert.IsTrue(eq.TorchLit, $"the torch is lit in the hand (held {(eq.HeldItem ? eq.HeldItem.id : "none")}, object {(eq.HeldObject ? eq.HeldObject.name : "none")}, flame prefab {(eq.torchFlamePrefab ? eq.torchFlamePrefab.name : "none")}, lightRange {torch.lightRange}, hand prefab {(torch.handPrefab ? torch.handPrefab.name : "none")}, active slot {inv.ActiveSlot}/{slot})");
            yield return new WaitForSeconds(0.3f);
            var snap = PlayerSignature.Current;
            float lit = DinoSenses.SightRange(raptor.def, snap, C);
            Debug.Log($"[Perception] torch at night: raptor sight {dark:F1} m dark -> {lit:F1} m with the torch (beacon {snap.beacon:F2})");
            Assert.IsTrue(snap.torch);
            Assert.Greater(lit, dark * 2f, $"a torch makes the player visible from much farther ({dark:F1} -> {lit:F1} m)");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Running_Into_A_Bush_Shakes_It_And_Draws_A_Look()
        {
            yield return LoadIsland();
            SetHour(9.5f);
            // 1) the real bush: running into it gives off a visible shake (motion) and a rustle (noise)
            var bush = BushInteraction.All.Where(b => b && b.gameObject.activeInHierarchy && b.transform.position.y > 1.5f)
                .OrderBy(b => (b.transform.position - _gm.Player.transform.position).sqrMagnitude).First();
            Vector3 bp = Ground(bush.transform.position);
            Vector3 start = OpenSpot(bp, 6f);
            Warp(start, bp);
            var cam = Camera.main.GetComponent<ThirdPersonCamera>(); var toBush = bp - start; toBush.y = 0;
            cam.Yaw = Quaternion.LookRotation(toBush).eulerAngles.y;
            yield return new WaitForSeconds(0.3f);
            int head0 = Stimuli.TransientHead;
            PlayerInputReader.Sim.Move = new Vector2(0f, 1f);
            float t0 = Time.time; while (Time.time - t0 < 4f && BushInteraction.PlayerBush != bush) yield return null;
            yield return new WaitForSeconds(0.3f);
            PlayerInputReader.Sim.Move = Vector2.zero;
            Assert.AreEqual(bush, BushInteraction.PlayerBush, "ran into the bush");
            float motion = 0f, rustle = 0f;
            for (int seq = Mathf.Max(head0 + 1, Stimuli.OldestTransient); seq <= Stimuli.TransientHead; seq++)
                if (Stimuli.TryGetTransient(seq, Stimuli.Now, out var s) && s.source == StimulusSource.Player && Vector3.Distance(Ground(s.pos), bp) < 2.5f)
                { if (s.kind == StimulusKind.Motion) motion = Mathf.Max(motion, s.strength); else if (s.tag == (byte)NoiseTag.Rustle) rustle = Mathf.Max(rustle, s.strength); }
            Debug.Log($"[Perception] bush run-in: motion {motion:F2}, rustle noise {rustle:F2}");
            Assert.Greater(motion, 0.5f, "a running push shakes the bush visibly");
            Assert.Greater(rustle, 0f, "and rustles");
            // 2) a raptor 15 m away that cannot see the player notices a shake of that size and looks at the bush
            var raptor = Dino("velociraptor");
            Vector3 rp = OpenSpot(bp, 15f);
            Warp(bp + (bp - rp).normalized * 70f, bp);
            Place(raptor, rp, bp);
            yield return new WaitForSeconds(0.6f);
            Stimuli.Motion(bp + Vector3.up * 0.6f, motion, StimulusSource.Player);
            t0 = Time.time; bool looked = false; Vector3 lp = default;
            while (Time.time - t0 < 2f && !looked) { raptor.home = rp; if (raptor.TryGetLookPoint(out lp) && Vector3.Distance(Ground(lp), bp) < 1.5f) looked = true; yield return null; }
            Vector3 eye = raptor.transform.position + Vector3.up * Mathf.Max(1f, raptor.def.bodyRadius * 1.4f);
            bool los = !Physics.Linecast(eye, bp + Vector3.up * 1.1f, ~LayerMask.GetMask("Player"), QueryTriggerInteraction.Ignore);
            Physics.Linecast(eye, bp + Vector3.up * 1.1f, out var blockHit, ~LayerMask.GetMask("Player"), QueryTriggerInteraction.Ignore);
            Assert.IsTrue(looked, $"the raptor looks at the shaking bush (state {raptor.State}, tier {raptor.Tier}, dist {Vector3.Distance(raptor.transform.position, bp):F1} m, awareness {raptor.Senses.Awareness:F2}, sense {raptor.Senses.LastSense}, los {los} {(blockHit.collider ? blockHit.collider.name : "")}, light {PlayerSignature.Current.ambient:F2})");
            Assert.AreEqual(SenseKind.Motion, raptor.Senses.LastSense, "because it saw the bush move");
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator Lost_Target_Goes_To_Last_Known_Position_Then_Gives_Up()
        {
            yield return LoadIsland();
            SetHour(9.5f);
            var raptor = Dino("velociraptor");
            Vector3 rp = OpenSpot(raptor.home, 1f), pp = OpenSpot(rp, 12f);
            Place(raptor, rp, pp); Warp(pp, rp);
            float t0 = Time.time; while (Time.time - t0 < 12f && raptor.State != DinoState.Chase && raptor.State != DinoState.Attack) yield return null;
            Assert.IsTrue(raptor.State == DinoState.Chase || raptor.State == DinoState.Attack, $"the raptor hunts the player (state {raptor.State})");
            Vector3 last = _gm.Player.transform.position;
            Vector3 away = rp - (pp - rp).normalized * 80f;                // far behind the raptor
            Warp(away, rp);
            t0 = Time.time; bool toLast = false;
            while (Time.time - t0 < 4f && !toLast) { toLast = raptor.State == DinoState.Investigate && Vector3.Distance(raptor.Destination, last) < C.searchRadius + 2f; yield return null; }
            Debug.Log($"[Perception] lost target: {raptor.State}, destination {Vector3.Distance(raptor.Destination, last):F1} m from the last known position, {Vector3.Distance(raptor.Destination, _gm.Player.transform.position):F1} m from the player");
            Assert.IsTrue(toLast, $"it goes to the last known position (state {raptor.State}, dest {Vector3.Distance(raptor.Destination, last):F1} m)");
            Assert.Greater(Vector3.Distance(raptor.Destination, _gm.Player.transform.position), 40f, "not to where the player really is");
            Time.timeScale = 3f;
            t0 = Time.realtimeSinceStartup; bool gaveUp = false;
            while (Time.realtimeSinceStartup - t0 < 45f && !gaveUp) { gaveUp = raptor.State != DinoState.Investigate && raptor.State != DinoState.Chase && raptor.Senses.Level <= AwarenessLevel.Suspicious; yield return null; }
            Time.timeScale = 1f;
            Assert.IsTrue(gaveUp, $"the search ends (state {raptor.State}, level {raptor.Senses.Level})");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Herd_Shares_An_Alert()
        {
            yield return LoadIsland();
            SetHour(9.5f);
            var herd = DinosaurController.All.Where(d => d && d.IsAlive && d.def && d.def.id == "parasaurolophus").ToList();
            Assert.GreaterOrEqual(herd.Count, 2, "a herd");
            var p0 = herd[0];
            var mates = herd.Skip(1).Where(d => Vector3.Distance(d.transform.position, p0.transform.position) < p0.def.herdShareRadius).ToList();
            Assert.Greater(mates.Count, 0, "herd mates in range");
            Warp(p0.transform.position + Vector3.right * 90f, p0.transform.position);
            yield return new WaitForSeconds(0.5f);
            foreach (var m in mates) m.Senses.ForgetAll();
            p0.Senses.Share(0.85f, p0.transform.position + Vector3.right * 20f, C);
            yield return new WaitForSeconds(1f);
            float best = mates.Max(m => m.Senses.Awareness);
            Debug.Log($"[Perception] herd alert: {mates.Count} mates, best awareness {best:F2}");
            Assert.GreaterOrEqual(best, C.herdShareLevel - 0.02f, "herd mates share the alert");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Missed_Projectile_Landing_Lures_An_Investigation()
        {
            yield return LoadIsland();
            SetHour(9.5f);
            var carno = Dino("carnotaurus");
            Vector3 cp = OpenSpot(carno.home, 1f);
            Warp(cp + Vector3.forward * 80f, cp);
            Place(carno, cp, cp + Vector3.forward * 10f);
            Vector3 landed = Ground(cp - Vector3.forward * 18f);            // behind it, far from the player
            yield return new WaitForSeconds(0.5f);
            int head0 = Stimuli.TransientHead;
            for (int i = 0; i < 2; i++) { GameEvents.Raise(GameEventType.ProjectileLanded, "stone_spear", 2, landed); yield return new WaitForSeconds(1f); }
            bool distraction = false;
            for (int seq = Mathf.Max(head0 + 1, Stimuli.OldestTransient); seq <= Stimuli.TransientHead; seq++)
                if (Stimuli.TryGetTransient(seq, Stimuli.Now, out var s) && s.source == StimulusSource.Distraction && Vector3.Distance(s.pos, landed) < 0.5f) distraction = true;
            Assert.IsTrue(distraction, "the landing is a distraction noise at the landing spot");
            float t0 = Time.time; bool toLanding = false;
            while (Time.time - t0 < 4f && !toLanding) { toLanding = carno.State == DinoState.Investigate && Vector3.Distance(carno.Destination, landed) < C.noiseLocError + 2f; yield return null; }
            Debug.Log($"[Perception] projectile landing: carnotaurus {carno.State}, awareness {carno.Senses.Awareness:F2}, destination {Vector3.Distance(carno.Destination, landed):F1} m from the spear, {Vector3.Distance(carno.Destination, _gm.Player.transform.position):F0} m from the player");
            Assert.IsTrue(toLanding, $"it investigates the landing spot (state {carno.State}, awareness {carno.Senses.Awareness:F2})");
            Assert.Greater(Vector3.Distance(carno.Destination, _gm.Player.transform.position), 50f, "not the player");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Bleeding_Status_Leaves_A_Blood_Trail()
        {
            yield return LoadIsland();
            var fx = PlayerStatusEffects.Player; Assert.IsNotNull(fx, "PlayerStatusEffects on the player");
            int w0 = Stimuli.ScentWritten;
            fx.Apply(StatusEffectIds.Bleeding, 1f, 30f, false);
            Assert.IsTrue(fx.Has(StatusEffectIds.Bleeding));
            yield return new WaitForSeconds(C.bleedScentInterval * 2f + 0.3f);
            int blood = CountBlood(w0, _gm.Player.transform.position, 3f);
            fx.Remove(StatusEffectIds.Bleeding, false, false);
            Debug.Log($"[Perception] bleeding: {blood} blood puffs in {C.bleedScentInterval * 2f + 0.3f:F1} s");
            Assert.GreaterOrEqual(blood, 1, "bleeding (PlayerStatusEffects) leaves blood scent");
            int w1 = Stimuli.ScentWritten;
            yield return new WaitForSeconds(C.bleedScentInterval * 2f);
            int after = CountBlood(w1, _gm.Player.transform.position, 1000f);
            Assert.AreEqual(0, after, "no blood once the bleeding stopped");
        }

        /// <summary>player blood puffs written since sequence 'from' within r of p</summary>
        static int CountBlood(int from, Vector3 p, float r)
        {
            int n = 0;
            for (int i = 0; i < Stimuli.ScentSlotsUsed; i++)
            {
                var s = Stimuli.ScentSlot(i);
                if (s.seq >= from && s.tag == (byte)ScentKind.Blood && s.source == StimulusSource.Player && Vector3.Distance(s.pos, p) < r) n++;
            }
            return n;
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator Save_And_Load_Keep_Killed_And_Wounded_Creatures()
        {
            yield return LoadIsland();
            Assert.IsTrue(SaveSystem.Sections.Any(s => s is CreatureSaveSection), "the creature section is registered");
            var raptor = Dino("velociraptor"); string rname = raptor.name;
            var tri = Dino("triceratops"); string tname = tri.name;
            raptor.TakeHit(new HitInfo { damage = 500f, point = raptor.transform.position + Vector3.up, direction = Vector3.forward, attacker = _gm.Player });
            yield return new WaitForSeconds(0.3f);
            var carcass = raptor.GetComponent<Carcass>(); Assert.IsNotNull(carcass);
            var pi = _gm.Player.GetComponent<PlayerInteraction>();
            for (int i = 0; i < carcass.handCutsPerItem; i++) carcass.Hit(pi);
            int meatLeft = carcass.meat; Vector3 bodyAt = raptor.transform.position;
            tri.TakeHit(new HitInfo { damage = 150f, point = tri.transform.position + Vector3.up, direction = Vector3.forward, attacker = _gm.Player });
            float triHp = tri.Health;
            Assert.IsTrue(_gm.SaveGame(), "saved");
            // everything changes after the save: the island respawns alive and healthy
            var sp = Object.FindFirstObjectByType<DinosaurSpawner>(); sp.SpawnAll();
            yield return null; yield return null;
            Assert.IsTrue(sp.Spawned.First(g => g && g.name == rname).GetComponent<DinosaurController>().IsAlive, "respawned alive before the load");
            _gm.LoadGame();
            yield return null; yield return null; yield return null;
            var r2 = sp.Spawned.First(g => g && g.name == rname).GetComponent<DinosaurController>();
            var t2 = sp.Spawned.First(g => g && g.name == tname).GetComponent<DinosaurController>();
            var c2 = r2.GetComponent<Carcass>();
            Debug.Log($"[Perception] save / load: raptor dead {!r2.IsAlive}, carcass meat {(c2 ? c2.meat : -1)} (saved {meatLeft}), {Vector3.Distance(r2.transform.position, bodyAt):F2} m from where it fell; triceratops {t2.Health:F0} HP (saved {triHp:F0})");
            Assert.IsFalse(r2.IsAlive, "the killed raptor is dead after the load");
            Assert.IsNotNull(c2, "its carcass"); Assert.AreEqual(meatLeft, c2.meat, "with what was left on it");
            Assert.Less(Vector3.Distance(r2.transform.position, bodyAt), 1f, "where it fell");
            Assert.AreEqual(triHp, t2.Health, 0.5f, "the wounded triceratops keeps its health");
        }

        // ------------------------------------------------------------------ scent + fire
        Campfire MakeFire(Vector3 at, float fuel)
        {
            var item = ItemDatabase.Instance.Item("campfire"); Assert.IsNotNull(item, "campfire item"); Assert.IsNotNull(item.placePrefab, "campfire prefab");
            var go = Object.Instantiate(item.placePrefab, Ground(at), Quaternion.identity);
            var fire = go.GetComponentInChildren<Campfire>(); Assert.IsNotNull(fire, "Campfire component");
            fire.Restore(true, fuel);
            return fire;
        }

        void Cook(Campfire fire)
        {
            var inv = _gm.Player.GetComponent<InventorySystem>(); var raw = ItemDatabase.Instance.Item("raw_meat"); Assert.IsNotNull(raw, "raw meat");
            inv.Add(raw, 3);
            Assert.IsTrue(fire.TryCook(inv, raw), "meat on the fire");
            fire.TryCook(inv, raw);                                        // a second piece when the fire has room
        }

        IEnumerator ScentRun(bool downwind, System.Action<DinosaurController, Campfire, bool, float, float> check)
        {
            yield return LoadIsland();
            SetHour(9.5f);
            var raptor = Dino("velociraptor");
            Vector3 rp = OpenSpot(raptor.home, 1f);
            Vector3 w = new Vector3(0.8f, 0f, 0.6f).normalized;
            DinoSenses.WeatherOverride = true; DinoSenses.WindDirOverride = new Vector2(w.x, w.z); DinoSenses.WindStrengthOverride = 1f;
            Vector3 fp = downwind ? rp - w * 30f : rp + w * 30f;            // downwind: the smell drifts from the fire to the raptor
            Warp(rp - Vector3.Cross(Vector3.up, w) * 110f, rp);           // far to the side
            Place(raptor, rp, fp);
            var fire = MakeFire(fp, 900f); Cook(fire);
            bool heard = false; void OnEv(GameEvent e) { if (e.type == GameEventType.ScentInvestigated && e.id == raptor.def.id) heard = true; }
            GameEvents.Raised += OnEv;
            Time.timeScale = 3f;
            float t0 = Time.realtimeSinceStartup, minDist = float.MaxValue;
            while (Time.realtimeSinceStartup - t0 < 40f) { minDist = Mathf.Min(minDist, Vector3.Distance(raptor.transform.position, fire.transform.position)); if (downwind && heard && minDist < 12f) break; yield return null; }
            Time.timeScale = 1f;
            GameEvents.Raised -= OnEv;
            check(raptor, fire, heard, minDist, FireSense.FearRadius(raptor.def.fireFear, fire, FireSense.Night));
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator Cooking_Meat_Downwind_Draws_A_Raptor_To_The_Fire_Edge()
        {
            yield return ScentRun(true, (raptor, fire, heard, minDist, r) =>
            {
                Debug.Log($"[Perception] cooking downwind: raptor followed the smell {heard}, closest {minDist:F1} m (fear radius {r:F1} m), fire mode {raptor.FireBehaviour}");
                Assert.IsTrue(heard, $"the raptor follows the cooking smell (interest {raptor.Senses.Interest:F2}, state {raptor.State})");
                Assert.Less(minDist, 14f, "it comes to the fire");
                Assert.Greater(minDist, r - 1f, $"but stays outside the fear radius ({minDist:F1} vs {r:F1} m)");
            });
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator Cooking_Meat_Upwind_Is_Not_Smelled()
        {
            yield return ScentRun(false, (raptor, fire, heard, minDist, r) =>
            {
                Debug.Log($"[Perception] cooking upwind: interest {raptor.Senses.Interest:F2}, closest {minDist:F1} m");
                Assert.IsFalse(heard, "no scent investigation from upwind");
                Assert.AreEqual(0f, raptor.Senses.Interest, 0.01f, "no interest");
            });
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator Fire_Keeps_A_Raptor_Circling_Outside_Until_It_Goes_Out()
        {
            yield return LoadIsland();
            SetHour(9.5f);
            var raptor = Dino("velociraptor");
            Vector3 rp = OpenHome(raptor), pp = OpenSpot(rp, 11f, true);
            var fire = MakeFire(pp + (pp - rp).normalized * 1f, 900f);
            Warp(pp, rp); Place(raptor, rp, pp);
            float r = FireSense.FearRadius(raptor.def.fireFear, fire, FireSense.Night);
            float t0 = Time.time, minFire = float.MaxValue; bool circled = false;
            while (Time.time - t0 < 12f) { minFire = Mathf.Min(minFire, Vector3.Distance(Ground(raptor.transform.position), Ground(fire.transform.position))); circled |= raptor.FireBehaviour != DinosaurController.FireMode.None; yield return null; }
            Debug.Log($"[Perception] fire fear: raptor {raptor.FireBehaviour} / {raptor.State}, closest to the fire {minFire:F1} m (radius {r:F1} m), awareness {raptor.Senses.Awareness:F2}, player cover {PlayerSignature.Current.cover:F2}, raptor sight {raptor.Senses.EffectiveSight:F1} m");
            Assert.IsTrue(circled, $"the raptor holds off at the fire (state {raptor.State}, awareness {raptor.Senses.Awareness:F2})");
            Assert.Greater(minFire, r - 0.5f, $"it stays outside the fear radius ({minFire:F1} vs {r:F1} m)");
            fire.SetLit(false);
            t0 = Time.time; float minPlayer = float.MaxValue;
            while (Time.time - t0 < 10f && minPlayer > 3.5f) { minPlayer = Mathf.Min(minPlayer, Vector3.Distance(raptor.transform.position, _gm.Player.transform.position)); yield return null; }
            Assert.LessOrEqual(minPlayer, 3.5f, $"with the fire out it closes in ({minPlayer:F1} m)");
        }

        // ------------------------------------------------------------------ cover
        [UnityTest, Timeout(120000)]
        public IEnumerator Felling_Trees_Opens_The_Ground()
        {
            yield return LoadIsland();
            var th = Object.FindFirstObjectByType<TreeHarvest>(); Assert.IsNotNull(th);
            Vector3 spot = default; bool found = false;
            // the densest stand (at least clearingFelled trees) with no more than one bush nearby (thicket below tree cover)
            int bestN = 0;
            for (int i = 0; i < CoverMap.TreeCount; i++)
            {
                var p = CoverMap.TreePosition(i) + Vector3.right * 1.2f; if (p.y < 1.5f) continue;
                int n = CoverMap.TreesNear(p, C.treeRadius, out _);
                if (n > bestN && n >= C.clearingFelled && CoverMap.BushesNear(p, C.bushEdge) <= 1) { bestN = n; spot = p; found = true; }
            }
            Assert.IsTrue(found, $"a stand of trees ({CoverMap.TreeCount} trees, densest {bestN} within {C.treeRadius} m)");
            Warp(spot, spot + Vector3.forward);
            yield return new WaitForSeconds(0.6f);
            var sig = PlayerSignature.Instance;
            int before = sig.TreesAround; float cover0 = PlayerSignature.Current.cover;
            var near = new List<int>(); CoverMap.StandingTrees(spot, C.treeRadius, near);
            int cut = Mathf.Min(6, near.Count);
            for (int k = 0; k < cut; k++) th.Fell(near[k], false);
            yield return new WaitForSeconds(0.6f);
            var snap = PlayerSignature.Current;
            Debug.Log($"[Perception] felling: trees {before} -> {sig.TreesAround}, cover {cover0:F2} -> {snap.cover:F2}, felled around {sig.FelledAround}, exposure {snap.exposure:F2}");
            Assert.AreEqual(before - cut, sig.TreesAround, "felled trees stop counting as cover");
            if (!snap.inBush) Assert.Less(snap.cover, cover0, "less cover");
            Assert.GreaterOrEqual(sig.FelledAround, C.clearingFelled, "a clearing");
            if (!snap.inBush) Assert.AreEqual(C.clearingExposure, snap.exposure, 1e-4f, "the clearing exposes the player");
            th.RestoreAll();
        }

        // ------------------------------------------------------------------ bare hands, save, life, tiers, budget
        [UnityTest, Timeout(120000)]
        public IEnumerator Bare_Hands_Knock_Small_Creatures_Back_Large_Barely_Notice()
        {
            yield return LoadIsland();
            var small = Dino("velociraptor"); var large = Dino("ankylosaurus");
            var player = _gm.Player;
            float h0 = small.Health;
            small.TakeHit(new HitInfo { damage = 4f, unarmed = true, knockback = 2.5f, attacker = player, point = small.transform.position + Vector3.up, direction = (small.transform.position - player.transform.position).normalized });
            Assert.AreEqual(h0 - 4f, small.Health, 1e-3f, "a small creature takes the full punch");
            Assert.IsTrue(small.IsKnockedBack, "and is pushed back"); Assert.Greater(small.PushSpeed, 1f);
            float l0 = large.Health;
            large.TakeHit(new HitInfo { damage = 4f, unarmed = true, knockback = 2.5f, attacker = player, point = large.transform.position + Vector3.up, direction = Vector3.forward });
            float lost = l0 - large.Health;
            Debug.Log($"[Perception] bare hands: raptor -4.0 (knocked back {small.PushSpeed:F2} m/s), ankylosaurus -{lost:F2} ({lost / large.def.maxHealth * 100f:F3} %)");
            Assert.Less(lost, 1f, "a large creature barely notices"); Assert.Greater(lost, 0f);
            Assert.IsFalse(large.IsKnockedBack, "and is not moved");
            yield return null;
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Creature_Save_Restores_Dead_Bodies_Carcass_And_Health()
        {
            yield return LoadIsland();
            var sp = Object.FindFirstObjectByType<DinosaurSpawner>(); Assert.IsNotNull(sp, "spawner");
            var raptor = Dino("velociraptor"); string rname = raptor.name;
            var tri = Dino("triceratops"); string tname = tri.name;
            raptor.TakeHit(new HitInfo { damage = 500f, point = raptor.transform.position + Vector3.up, direction = Vector3.forward, attacker = _gm.Player });
            Assert.IsFalse(raptor.IsAlive);
            yield return new WaitForSeconds(0.3f);
            var carcass = raptor.GetComponent<Carcass>(); Assert.IsNotNull(carcass, "carcass");
            var pi = _gm.Player.GetComponent<PlayerInteraction>();
            for (int i = 0; i < carcass.handCutsPerItem; i++) carcass.Hit(pi);
            int meatLeft = carcass.meat; Vector3 bodyAt = raptor.transform.position;
            tri.TakeHit(new HitInfo { damage = 100f, point = tri.transform.position + Vector3.up, direction = Vector3.forward, attacker = _gm.Player });
            float triHp = tri.Health;
            string json = CreatureSave.Capture(sp);
            StringAssert.Contains(rname, json);
            // load flow: respawn everything, then restore the section in the same frame
            sp.SpawnAll();
            int n = CreatureSave.Restore(sp, json);
            yield return null; yield return null;
            var r2 = sp.Spawned.First(g => g && g.name == rname).GetComponent<DinosaurController>();
            var t2 = sp.Spawned.First(g => g && g.name == tname).GetComponent<DinosaurController>();
            var c2 = r2.GetComponent<Carcass>();
            Debug.Log($"[Perception] creature save: {n} restored, raptor dead {!r2.IsAlive} meat {(c2 ? c2.meat : -1)} (was {meatLeft}), triceratops {t2.Health:F0} (was {triHp:F0}), json {json.Length} chars");
            Assert.Greater(n, 5, "creatures restored");
            Assert.IsFalse(r2.IsAlive, "the killed raptor stays dead after load");
            Assert.IsNotNull(c2, "its carcass is back"); Assert.AreEqual(meatLeft, c2.meat, "with the butchering progress");
            Assert.Less(Vector3.Distance(r2.transform.position, bodyAt), 1f, "where it fell");
            Assert.AreEqual(triHp, t2.Health, 0.5f, "a wounded creature keeps its health");
            Assert.IsFalse(DinosaurController.All.Contains(r2), "a body is not a live creature");
            Assert.AreEqual(0, CreatureSave.Restore(sp, "{broken"), "a broken section is skipped");
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator Herbivores_Sleep_At_Night_And_Go_To_Water()
        {
            yield return LoadIsland();
            Assert.Greater(DrinkSpots.Count, 0, "fresh water points");
            var herb = DinosaurController.All.Where(d => d && d.IsAlive && d.def && d.def.activity == ActivityCycle.Diurnal && d.def.drinkEveryHours > 0f)
                .Select(d => { bool ok = DrinkSpots.Nearest(d.home, d.def.drinkSearchRadius, out var s); return (d, ok, s); }).Where(x => x.ok)
                .OrderBy(x => Vector3.Distance(x.d.home, x.s)).FirstOrDefault();
            Assert.IsNotNull(herb.d, "a herbivore with water in reach");
            var d = herb.d;
            d.LeaveHerd();                  // a herd drinks and sleeps together (HerdGroup); this one lives alone for the test
            Warp(d.transform.position + Vector3.right * 100f, d.transform.position);
            SetHour(23.5f);
            float t0 = Time.time; bool slept = false;
            while (Time.time - t0 < 25f && !slept) { slept = DinosaurController.All.Any(x => x && x.def && x.def.activity == ActivityCycle.Diurnal && x.Sleeping); yield return null; }
            Assert.IsTrue(slept, "diurnal herbivores sleep at night");
            SetHour(10f);
            yield return new WaitForSeconds(0.6f);
            d.Senses.ForgetAll(); d.ForceState(DinoState.Idle); d.ThirstNow();
            t0 = Time.time; bool going = false;
            while (Time.time - t0 < 6f && !going) { going = Vector3.Distance(d.Destination, herb.s) < 0.5f; yield return null; }
            Assert.IsTrue(going, $"a thirsty {d.def.id} heads for the water (state {d.State})");
            Time.timeScale = 4f;
            t0 = Time.realtimeSinceStartup; bool drank = false;
            while (Time.realtimeSinceStartup - t0 < 60f && !drank) { drank = d.State == DinoState.Drink; yield return null; }
            Time.timeScale = 1f;
            Debug.Log($"[Perception] life: slept yes, {d.def.id} drank {drank} ({Vector3.Distance(d.transform.position, herb.s):F1} m from the water point)");
            Assert.IsTrue(drank, $"it drinks at the water (state {d.State}, {Vector3.Distance(d.transform.position, herb.s):F1} m away)");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator AI_Tiers_Near_Medium_Far_Very_Far()
        {
            yield return LoadIsland();
            var tri = Dino("triceratops");
            var anim = tri.GetComponentInChildren<Animator>();
            float[] dist = { C.nearDistance * 0.5f, (C.nearDistance + C.mediumDistance) * 0.5f, (C.mediumDistance + C.farDistance) * 0.5f, C.farDistance + 60f };
            var seen = new List<string>();
            // a direction that stays on the island (above the sea) at the far distance
            var terrain = Terrain.activeTerrain; var b = terrain.terrainData.bounds; b.center += terrain.transform.position;
            Vector3 way = Vector3.right;
            for (int a = 0; a < 16; a++)
            {
                var w = Quaternion.Euler(0, a * 22.5f, 0) * Vector3.forward; var q = Ground(tri.transform.position + w * dist[3]);
                if (b.Contains(new Vector3(q.x, b.center.y, q.z)) && q.y > 1.5f) { way = w; break; }
            }
            for (int k = 0; k < 4; k++)
            {
                Vector3 p = tri.transform.position + way * dist[k];
                Warp(p, tri.transform.position);
                yield return new WaitForSeconds(1.2f);
                float real = Vector3.Distance(tri.transform.position, _gm.Player.transform.position);
                int expect = real < C.nearDistance ? 0 : real < C.mediumDistance ? 1 : real < C.farDistance ? 2 : 3;
                seen.Add($"{real:F0} m -> tier {tri.Tier} (animator {(anim ? anim.enabled.ToString() : "none")})");
                Assert.AreEqual(expect, tri.Tier, $"tier at {real:F0} m");
                if (anim) Assert.AreEqual(expect < 2, anim.enabled, "Animator on near and medium (far: stepped by hand at a lower rate, very far: off)");
            }
            Debug.Log("[Perception] tiers: " + string.Join("; ", seen));
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Killed_Creature_Returns_After_Its_Respawn_Time_When_Far()
        {
            yield return LoadIsland();
            var sp = Object.FindFirstObjectByType<DinosaurSpawner>(); Assert.IsNotNull(sp); Assert.IsTrue(sp.UsesPlacedCreatures, "placed creatures");
            var raptor = Dino("velociraptor"); string name = raptor.name;
            int index = System.Linq.Enumerable.ToList(sp.Spawned).IndexOf(raptor.gameObject);
            raptor.TakeHit(new HitInfo { damage = 500f, point = raptor.transform.position + Vector3.up, direction = Vector3.forward, attacker = _gm.Player });
            yield return new WaitForSeconds(0.2f);
            var c = raptor.GetComponent<Carcass>(); Assert.IsNotNull(c);
            c.RestoreLeft(0, 0, 0, 0);                                      // butchered: the body sinks
            float t0 = Time.time; while (Time.time - t0 < c.sinkSeconds + 2f && raptor.gameObject.activeSelf) yield return null;
            Assert.IsFalse(raptor.gameObject.activeSelf, "the body sank");
            float hours0 = sp.respawnHours;
            try
            {
                sp.respawnHours = 0.5f;
                var slotPos = raptor.transform.position;
                Vector3 far = OpenSpot(slotPos, sp.respawnMinDistance + 30f);
                Warp(far, slotPos);
                GameClock.Now += GameClock.Hours(1f);
                t0 = Time.time; GameObject back = null;
                while (Time.time - t0 < 8f && back == null) { var g = sp.Spawned[index]; if (g && g != raptor.gameObject && g.activeSelf && g.GetComponent<DinosaurController>().IsAlive) back = g; yield return null; }
                Assert.IsNotNull(back, "the raptor came back after its respawn time");
                Assert.AreEqual(name, back.name);
                Assert.IsTrue(DinosaurController.All.Contains(back.GetComponent<DinosaurController>()), "alive again");
            }
            finally { sp.respawnHours = hours0; }
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Stealth_Indicator_Auto_Shows_When_Crouched_And_Marks_Threats()
        {
            yield return LoadIsland();
            SetHour(9.5f);
            int mode = GameSettings.StealthHud;
            try
            {
                GameSettings.StealthHud = 0;                                                 // AUTO (the default)
                var ind = Object.FindFirstObjectByType<UI.PerceptionIndicator>(); Assert.IsNotNull(ind, "indicator on the UI");
                // far from predators, standing: hidden
                Vector3 quiet = _gm.Player.transform.position;
                Motor.SetCrouch(false);
                yield return new WaitForSeconds(0.5f);
                bool nearPred = DinosaurController.All.Any(d => d && d.def && (d.def.temperament == Temperament.Predator || d.def.temperament == Temperament.Territorial) && Vector3.Distance(d.transform.position, quiet) < ind.nearPredator);
                bool noticed = DinosaurController.All.Any(d => d && d.Senses.Level >= AwarenessLevel.Suspicious);
                if (!nearPred && !noticed) Assert.IsFalse(ind.Shown, "AUTO: hidden while standing, away from predators");
                Motor.SetCrouch(true);
                yield return new WaitForSeconds(0.4f);
                Assert.IsTrue(ind.Shown, "AUTO: shown while crouched");
                Assert.GreaterOrEqual(ind.VisibilityTier, 0);
                // a raptor that noticed the player gets an edge marker
                var raptor = Dino("velociraptor");
                Place(raptor, _gm.Player.transform.position + Vector3.forward * 30f, _gm.Player.transform.position);
                raptor.Senses.Share(0.6f, _gm.Player.transform.position, C);
                yield return new WaitForSeconds(0.3f);
                Assert.Greater(ind.ActiveMarkers, 0, "a threat marker points at the creature");
                GameSettings.StealthHud = 2;
                yield return new WaitForSeconds(0.3f);
                Assert.IsFalse(ind.Shown, "OFF hides it");
                Motor.SetCrouch(false);
            }
            finally { GameSettings.StealthHud = mode; }
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator Perception_Frame_Budget_And_Zero_Allocation()
        {
            yield return LoadIsland();
            SetHour(9.5f);
            // some activity: a fire with food, a carcass, noises
            // stand in view of the densest group of land creatures (worst case: full senses, sight checks running)
            Vector3 p = _gm.Player.transform.position; int most = -1; DinosaurController anchor = null;
            foreach (var d in DinosaurController.All)
            {
                if (!d) continue; int n = DinosaurController.All.Count(o => o && (o.transform.position - d.transform.position).sqrMagnitude < 50f * 50f);
                if (n > most) { most = n; anchor = d; }
            }
            p = OpenSpot(anchor.transform.position, 14f, true);
            Warp(p, anchor.transform.position);
            yield return new WaitForSeconds(1f);
            p = _gm.Player.transform.position;
            var fire = MakeFire(p + Vector3.forward * 4f, 900f); Cook(fire);
            int frames = 0;
            DinoSenses.ResetStats(); int ticks0 = 0;
            long los0 = DinoSenses.LosTotal;
            while (frames < 300)
            {
                if (frames % 20 == 0) Stimuli.Noise(p + Random.insideUnitSphere * 10f, 1.2f, NoiseTag.Chop, StimulusSource.Player);
                yield return null; frames++; ticks0 += DinoSenses.TicksThisFrame;
            }
            float avg = (float)(DinoSenses.TotalTickMs / frames), maxMs = DinoSenses.MaxFrameTickMs; int maxLos = DinoSenses.MaxLosInAFrame;
            long losCount = DinoSenses.LosTotal - los0;
            // allocation: the player signature and every creature's senses, many rounds per frame on a moving clock
            var sig = PlayerSignature.Instance; Assert.IsNotNull(sig);
            var dinos = DinosaurController.All.Where(d => d && d.IsAlive).ToList();
            foreach (var d in dinos) d.enabled = false;                    // their own Update must not run on the test clock (state changes raise events)
            float fake = Time.time + 1000f; Stimuli.Clock = () => fake;
            void Round() { fake += 0.1f; sig.Tick(); foreach (var d in dinos) d.Senses.Tick(d, 0.2f, Mathf.Min(d.Tier, 2), C); }
            for (int i = 0; i < 200; i++) Round();
            double work = 0, idle = 0, probe = 0, sigOnly = 0, sensesOnly = 0;
            yield return PerceptionTests.AllocPerFrame(() => { for (int i = 0; i < 400; i++) Round(); }, 12, (w, id, pr) => { work = w; idle = id; probe = pr; });
            yield return PerceptionTests.AllocPerFrame(() => { for (int i = 0; i < 400; i++) { fake += 0.1f; sig.Tick(); } }, 8, (w, id, pr) => { sigOnly = w - id; });
            yield return PerceptionTests.AllocPerFrame(() => { for (int i = 0; i < 400; i++) { fake += 0.1f; foreach (var d in dinos) d.Senses.Tick(d, 0.2f, Mathf.Min(d.Tier, 2), C); } }, 8, (w, id, pr) => { sensesOnly = w - id; });
            Debug.Log($"[Perception] allocation split: signature only {sigOnly:F0} B/frame, senses only {sensesOnly:F0} B/frame (400 rounds)");
            Stimuli.Clock = null; Stimuli.Clear();
            foreach (var d in dinos) if (d) { d.enabled = true; d.Senses.ForgetAll(); }
            Debug.Log($"[Perception] budget over {frames} frames: sensing avg {avg:F3} ms / worst frame {maxMs:F3} ms, {losCount} linecasts (max {maxLos} in a frame), {dinos.Count} creatures near {most}; allocation {work:F0} B/frame with 400 rounds x {dinos.Count + 1} ticks vs {idle:F0} B/frame idle (probe {probe:F0})");
            Assert.LessOrEqual(maxLos, C.maxLosPerFrame, "line-of-sight budget");
            Assert.Less(avg, 0.25f, "sensing under 0.25 ms per frame on average");
            Assert.Greater(losCount, 0, "the creatures really looked (sight checks ran)");
            Assert.Greater(probe - idle, 30000.0, "the per-frame allocation counter works");
            Assert.Less(work - idle, 4096.0, $"perception allocates nothing in steady state ({work - idle:F0} B per frame over {400 * (dinos.Count + 1)} ticks)");
        }
    }
}
