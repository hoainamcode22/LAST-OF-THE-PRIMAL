using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PrimalFrontier.Combat;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.World;

namespace PrimalFrontier.Tests
{
    /// <summary>The resource pass on the island: start area, trail to water, every node near the spawn, tools vs hands, strikes, fruit, notes, respawn, save.</summary>
    public class ResourceIslandTests
    {
        static ItemDatabase Db => ItemDatabase.Instance;
        GameManager _gm; double _clock;

        IEnumerator LoadIsland()
        {
            TestScenes.UseTestSaves(); GameManager.ForceShowTitle = false; GameManager.ForcePlayIntro = false; PlayerInputReader.Simulate = true;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/Island_VerticalSlice.unity", new LoadSceneParameters(LoadSceneMode.Single));
#endif
            float t = 0; while ((_gm = GameManager.Instance) == null || _gm.State != GameState.Playing) { t += Time.unscaledDeltaTime; if (t > 20f) break; yield return null; }
            Assert.IsNotNull(_gm, "GameManager"); Assert.AreEqual(GameState.Playing, _gm.State, "game started");
            _clock = GameClock.Now;
            yield return new WaitForSeconds(1f);
            _gm.Player.GetComponent<PlayerHealth>().InvulnerableUntil = Time.time + 600f;     // creatures must not interrupt
        }
        [TearDown] public void Cleanup() { GameManager.ForceShowTitle = null; GameManager.ForcePlayIntro = null; PlayerInputReader.Simulate = false; PlayerInputReader.Sim.Attack = false; Time.timeScale = 1f; }
        [UnityTearDown] public IEnumerator Unload() { yield return TestScenes.Clear(); }

        Vector3 Spawn => _gm.spawnPoint ? _gm.spawnPoint.position : _gm.Player.transform.position;
        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
        static float Ground(Vector3 p) { var t = Terrain.activeTerrain; return t ? t.SampleHeight(p) + t.transform.position.y : p.y; }
        static List<ResourceNode> NodesWithin(Vector3 c, float r) => Interactable.Active.OfType<ResourceNode>().Where(n => n && Flat(n.transform.position - c).magnitude < r).ToList();

        /// <summary>stand next to a node (closest surface ~0.6 m), facing it, and wait for the scan to pick it</summary>
        IEnumerator StandAt(ResourceNode n, PlayerInteraction pi, System.Action<bool> targeted, float gap = 0.6f)
        {
            var motor = pi.GetComponent<PlayerMotor>();
            var f = n.FocusPoint; var away = Flat(Spawn - f); away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.back;
            var stand = f + away * (n.Radius + gap); stand.y = Ground(stand) + 0.05f;
            motor.Warp(stand, Quaternion.LookRotation(Flat(f - stand).normalized));
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 1.5f && !(pi.Target == n && pi.Prompt != null)) yield return null;
            targeted(pi.Target == n && pi.Prompt != null);
        }

        [UnityTest, Timeout(120000)] public IEnumerator Start_Area_Is_Stocked_And_Leads_To_Water()
        {
            yield return LoadIsland();
            var near = NodesWithin(Spawn, 40f);
            var report = new System.Text.StringBuilder();
            foreach (var cat in new[] { ResourceCategory.Stone, ResourceCategory.Wood, ResourceCategory.Fiber, ResourceCategory.Food })
            {
                var c = near.Where(n => n.Def.category == cat).ToList();
                report.Append($"{cat} {c.Count} nodes / {c.Sum(n => n.charges)} units; ");
                Assert.GreaterOrEqual(c.Count, cat == ResourceCategory.Food ? 2 : 3, $"{cat} within 40 m of the spawn ({report})");
            }
            // a visible chain of resources from the beach to fresh water: no gap over 30 m
            var fresh = WaterSource.All.Where(w => w && w.fresh).ToList(); Assert.Greater(fresh.Count, 0);
            var all = Interactable.Active.OfType<ResourceNode>().Where(n => n).Select(n => Flat(n.transform.position)).ToList();
            var start = Flat(Spawn); float bestWater = float.MaxValue; Vector3 goal = start;
            foreach (var w in fresh)
            {
                var mf = w.surface ? w.surface : w.GetComponentInChildren<MeshFilter>(); if (!mf || !mf.sharedMesh) continue;
                foreach (var v in mf.sharedMesh.vertices) { var q = Flat(mf.transform.TransformPoint(v)); float d = Vector3.Distance(q, start); if (d < bestWater) { bestWater = d; goal = q; } }
            }
            Assert.Less(bestWater, 1000f, "found the fresh water surface");
            Assert.Greater(bestWater, 35f, "the water is not at the spawn (the chain test means something)");
            // BFS over nodes (links <= 30 m) from the spawn
            var reached = new List<Vector3> { start }; var open = new Queue<Vector3>(); open.Enqueue(start); var used = new HashSet<int>();
            bool arrived = false;
            while (open.Count > 0 && !arrived)
            {
                var p = open.Dequeue();
                if (Vector3.Distance(p, goal) < 35f) { arrived = true; break; }
                for (int i = 0; i < all.Count; i++) if (!used.Contains(i) && Vector3.Distance(all[i], p) <= 30f) { used.Add(i); open.Enqueue(all[i]); }
            }
            report.Append($"nearest fresh water {bestWater:F0} m from the spawn; chain reaches it {arrived}");
            Debug.Log("[Resources] " + report);
            Assert.IsTrue(arrived, "resources lead from the beach to fresh water (links of 30 m or less): " + report);
        }

        [UnityTest, Timeout(300000)] public IEnumerator Every_Node_Near_The_Spawn_Prompts_And_Gives_Items()
        {
            yield return LoadIsland();
            var p = _gm.Player; var pi = p.GetComponent<PlayerInteraction>(); var inv = p.GetComponent<InventorySystem>();
            var nodes = NodesWithin(Spawn, 40f).Where(n => !n.IsEmpty).OrderBy(n => Flat(n.transform.position - Spawn).magnitude).ToList();
            Assert.GreaterOrEqual(nodes.Count, 12, "nodes around the spawn");
            var allowed = new[] { "Gather Stone", "Gather Wood", "Gather Fiber", "Harvest" };
            int targeted = 0, lit = 0; var misses = new List<string>(); var report = new System.Text.StringBuilder();
            var hl = Object.FindFirstObjectByType<InteractionHighlight>(); Assert.IsNotNull(hl, "the interaction highlight runs");
            foreach (var n in nodes)
            {
                inv.Clear(); inv.SetActiveSlot(0);
                bool tg = false; yield return StandAt(n, pi, b => tg = b);
                if (tg) targeted++; else misses.Add($"{n.name} (target {(pi.Target ? pi.Target.name : "none")})");
                if (tg) { yield return null; if (hl && hl.Current == n && n.Renderers.Any(r => r && r.enabled && r.HasPropertyBlock())) lit++; }
                string prompt = n.GetPrompt(pi, out var sub);
                Assert.IsNotNull(prompt, n.name + " has a prompt");
                CollectionAssert.Contains(allowed, prompt, n.name + " prompt");
                Assert.IsTrue(n.CanInteract(pi), $"{n.name}: '{prompt}' / '{sub}' can be used by hand");
                var item = n.Def.item; int before = inv.Count(item), rem0 = n.Remaining, drops = WorldPickup.Dropped.Count;
                int tries = 0;
                while (inv.Count(item) == before && tries < 4 && !n.IsEmpty) { n.Hit(pi); tries++; }
                int gained = inv.Count(item) - before;
                Assert.Greater(gained, 0, $"{n.name} gives {item.id} by hand ({tries} actions)");
                Assert.AreEqual(rem0 - n.Remaining, gained, $"{n.name}: what left the node is what arrived (no duplicates)");
                Assert.AreEqual(drops, WorldPickup.Dropped.Count, "nothing spilled with room in the pack");
                report.Append($"{n.Def.id} '{prompt}' +{gained} in {tries}; ");
                n.Regrow();
            }
            Debug.Log($"[Resources] {nodes.Count} nodes near the spawn, targeted by the scan {targeted}, highlighted {lit}: {report} misses: {string.Join(", ", misses)}");
            Assert.AreEqual(targeted, lit, "the targeted node (and only that one) glows");
            Assert.LessOrEqual(Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Count(r => r.HasPropertyBlock() && r.GetComponentInParent<ResourceNode>()), 8, "no highlight left on other nodes");
            Assert.GreaterOrEqual(targeted, Mathf.CeilToInt(nodes.Count * 0.85f), "the scan picks the node the player faces: " + string.Join(", ", misses));
        }

        [UnityTest, Timeout(180000)] public IEnumerator Tools_Beat_Hands_On_Stone_Wood_And_Trees()
        {
            yield return LoadIsland();
            var p = _gm.Player; var pi = p.GetComponent<PlayerInteraction>(); var inv = p.GetComponent<InventorySystem>(); var motor = p.GetComponent<PlayerMotor>();
            var stone = Db.Item("stone"); var wood = Db.Item("wood"); var report = new System.Text.StringBuilder();
            ResourceNode Nearest(string def) => Interactable.Active.OfType<ResourceNode>().Where(n => n && n.Def.id == def && !n.IsEmpty).OrderBy(n => Flat(n.transform.position - Spawn).magnitude).FirstOrDefault();
            bool tg = false;
            // stone: large rock, hands 1 per 3 actions, pick 3-5 per action
            var rock = Nearest("stone_large"); Assert.IsNotNull(rock, "a large rock");
            yield return StandAt(rock, pi, b => tg = b);
            inv.Clear();
            for (int i = 0; i < 3; i++) rock.Hit(pi);
            Assert.AreEqual(1, inv.Count(stone), "three hand actions on a large rock: 1 stone");
            inv.Add(Db.Item("stone_pick"), 1); inv.SetActiveSlot(0);
            Assert.AreEqual("stone_pick", inv.ActiveItem ? inv.ActiveItem.id : null);
            StringAssert.Contains("Gather Stone", rock.GetPrompt(pi, out _));
            int b0 = inv.Count(stone); rock.Hit(pi); int pickGain = inv.Count(stone) - b0;
            Assert.That(pickGain, Is.InRange(3, 5), "the pick: 3-5 stone per action");
            report.Append($"large rock: hands 1 per 3, pick {pickGain}; ");
            // a branch by hand, then fibre by hand and with a knife
            var br = Nearest("wood_branch"); Assert.IsNotNull(br, "a fallen branch");
            yield return StandAt(br, pi, b => tg = b); inv.Clear(); inv.SetActiveSlot(0);
            br.Hit(pi); Assert.AreEqual(1, inv.Count(wood), "a branch by hand: 1 wood per action");
            var fib = Nearest("fiber_plant") ?? Nearest("fiber_grass"); Assert.IsNotNull(fib, "a fibre node");
            yield return StandAt(fib, pi, b => tg = b); inv.Clear();
            fib.Hit(pi); int fh = inv.Count(Db.Item("fiber")); Assert.That(fh, Is.InRange(1, 2), "fibre by hand 1-2");
            report.Append($"branch by hand 1, fibre by hand {fh}; ");
            // a terrain tree: hands only strip twigs (tiny, capped, never fells), the axe chops 2-3 per hit and fells it
            var th = Object.FindFirstObjectByType<TreeHarvest>(); Assert.IsNotNull(th, "trees");
            var td = Terrain.activeTerrain.terrainData; var tp = Terrain.activeTerrain.transform.position;
            var tree = td.treeInstances.Select(t => Vector3.Scale(t.position, td.size) + tp).OrderBy(t => Flat(t - Spawn).magnitude).First();
            var stand = tree + Flat(Spawn - tree).normalized * 1.3f; stand.y = Ground(stand) + 0.05f;
            motor.Warp(stand, Quaternion.LookRotation(Flat(tree - stand).normalized));
            float t0 = Time.realtimeSinceStartup; while (th.Current < 0 && Time.realtimeSinceStartup - t0 < 2f) yield return null;
            Assert.GreaterOrEqual(th.Current, 0, "at a tree"); int idx = th.Current;
            inv.Clear(); inv.SetActiveSlot(0);
            Assert.AreEqual("Gather Wood", th.GetPrompt(pi, out var tsub), "bare hands at a tree");
            for (int i = 0; i < 12; i++) th.HandHit(pi, idx);
            int twigs = inv.Count(wood);
            Assert.That(twigs, Is.InRange(1, 2), "hands: a couple of dry twigs, no more");
            Assert.IsFalse(th.Felled.ContainsKey(idx), "fists never fell a tree");
            bool felledEvent = false; System.Action<GameEvent> on = e => { if (e.type == GameEventType.TreeFelled) felledEvent = true; };
            GameEvents.Raised += on;
            inv.Clear(); inv.Add(Db.Item("stone_axe"), 1); inv.SetActiveSlot(0);
            Assert.AreEqual("Chop Tree", th.GetPrompt(pi, out _));
            int hits = 0; bool go = true; var per = new List<int>();
            while (go && hits < 12) { int w0 = inv.Count(wood); th.Hit(pi, idx); per.Add(inv.Count(wood) - w0); hits++; go = !th.Felled.ContainsKey(idx); }   // Hit returns false after each strike unless the key is held
            GameEvents.Raised -= on;
            Assert.IsTrue(th.Felled.ContainsKey(idx), "the axe fells it");
            Assert.IsTrue(felledEvent, "TreeFelled raised (AI hears it)");
            Assert.IsTrue(per.All(x => x >= 2 && x <= 3), "axe: 2-3 wood per hit: " + string.Join(",", per));
            report.Append($"tree: hands {twigs} twigs in 12 actions ('{tsub}'), axe {string.Join("+", per)} in {hits} hits then felled");
            Debug.Log("[Resources] " + report);
            th.RestoreAll();
        }

        [UnityTest, Timeout(180000)] public IEnumerator Punch_Fruit_Notes_And_Respawn()
        {
            yield return LoadIsland();
            var p = _gm.Player; var pi = p.GetComponent<PlayerInteraction>(); var inv = p.GetComponent<InventorySystem>(); var motor = p.GetComponent<PlayerMotor>();
            var stone = Db.Item("stone"); var report = new System.Text.StringBuilder();
            // 1. a real bare-hand punch on small stones gathers one
            var small = Interactable.Active.OfType<ResourceNode>().Where(n => n && n.Def.id == "stone_small" && !n.IsEmpty && n.Remaining >= 2)
                .OrderBy(n => Flat(n.transform.position - Spawn).magnitude).First();
            inv.Clear(); inv.SetActiveSlot(0);
            var wc = p.GetComponent<PlayerCombat>().Weapons; IDamageable hitT = null; wc.TargetHit += (t, h) => { if (ReferenceEquals(t, small)) hitT = t; };
            var sv = p.GetComponent<Survival.PlayerSurvival>();
            bool punched = false;
            for (int attempt = 0; attempt < 3 && !punched; attempt++)
            {
                var f = small.FocusPoint; var away = Flat(Spawn - f).normalized; if (away.sqrMagnitude < 0.01f) away = Vector3.back;
                var stand = f + away * (small.Radius + 0.35f + attempt * 0.15f); stand.y = Ground(stand) + 0.05f;
                motor.Warp(stand, Quaternion.LookRotation(Flat(f - stand).normalized));
                yield return new WaitForSeconds(0.4f);
                if (sv) sv.SetStats(sv.Hunger, sv.Thirst, sv.maxStamina, sv.BodyTemperature, sv.Wetness);
                PlayerInputReader.Sim.Attack = true;
                float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < 2.5f && inv.Count(stone) == 0) yield return null;
                punched = inv.Count(stone) > 0;
                yield return new WaitForSeconds(0.8f);
            }
            report.Append($"punch on small stones: +{inv.Count(stone)} (hitbox reported the node {hitT != null}); ");
            Assert.IsTrue(punched, "a bare-hand punch on small stones gives a stone");
            // 2. the pickup feed merges quick gains into one '+N Stone' line
            var med = Interactable.Active.OfType<ResourceNode>().Where(n => n && n.Def.id == "stone_medium" && n.Remaining >= 4).OrderBy(n => Flat(n.transform.position - Spawn).magnitude).First();
            bool tg = false; yield return StandAt(med, pi, b => tg = b);
            yield return new WaitForSeconds(3.3f);                              // older stone notes stop merging
            inv.Clear(); inv.SetActiveSlot(0);
            for (int i = 0; i < 3; i++) { med.Hit(pi); yield return new WaitForSeconds(0.25f); }
            yield return null;
            var notes = Object.FindObjectsByType<UnityEngine.UI.Text>(FindObjectsSortMode.None).Where(t => t.transform.parent && t.transform.parent.name == "Note" && t.text.Contains(stone.displayName) && t.text.StartsWith("+")).ToList();
            report.Append($"notes: {string.Join(" | ", notes.Select(t => t.text))}; ");
            Assert.AreEqual(1, notes.Count(t => t.text.Contains("+3")), "one merged '+3 Stone' note, not a wall of '+1'");
            // 3. fruit on a tree: all of it with room, none with a full pack
            var fc = Object.FindObjectsByType<FruitCluster>(FindObjectsSortMode.None).FirstOrDefault(x => x.Ripe); Assert.IsNotNull(fc, "a ripe fruit cluster");
            var fruit = fc.item; inv.Clear();
            var full = Db.Item("stone"); for (int i = 0; i < inv.Slots.Length; i++) inv.Add(full, full.maxStack, true);
            int f0 = inv.Count(fruit);
            Assert.IsFalse(fc.Harvest(inv), "full pack: nothing"); Assert.IsTrue(fc.Ripe, "fruit stays"); Assert.AreEqual(f0, inv.Count(fruit));
            inv.Clear();
            Assert.IsTrue(fc.Harvest(inv)); Assert.AreEqual(fc.amount, inv.Count(fruit), "all of the bunch"); Assert.IsFalse(fc.Ripe, "bunch empty");
            Assert.IsFalse(fc.Harvest(inv), "no second harvest"); Assert.AreEqual(fc.amount, inv.Count(fruit));
            report.Append($"fruit +{fc.amount}, then empty; ");
            // 4. respawn: never instant, not in front of the player, back after its time
            var gone = Interactable.Active.OfType<ResourceNode>().Where(n => n && n.Def.depletedLook == DepletedLook.Hide && !n.IsEmpty && n.Def.category == ResourceCategory.Fiber)
                .OrderBy(n => Flat(n.transform.position - Spawn).magnitude).First();
            yield return StandAt(gone, pi, b => tg = b); inv.Clear();
            for (int i = 0; i < 10 && !gone.IsEmpty; i++) gone.Hit(pi);
            Assert.IsTrue(gone.IsEmpty, "picked clean");
            Assert.IsFalse(gone.GetComponentsInChildren<Renderer>().Any(r => r.enabled), "gone (with a puff)");
            ResourceManager.Instance.Tick(); Assert.IsTrue(gone.IsEmpty, "never instant");
            GameClock.Now = gone.EmptyUntil + 1;
            ResourceManager.Instance.Tick(); Assert.IsTrue(gone.IsEmpty, "due, but the player is right there looking at it");
            var awayPos = gone.transform.position + Flat(Spawn - gone.transform.position).normalized * 45f; awayPos.y = Ground(awayPos) + 0.1f;
            motor.Warp(awayPos, Quaternion.identity); yield return null; yield return null;
            ResourceManager.Instance.Tick();
            Assert.IsFalse(gone.IsEmpty, "grown back while the player was away");
            report.Append($"respawn {gone.Def.id} after {gone.Def.respawnHours} h");
            Debug.Log("[Resources] " + report);
            GameClock.Now = _clock;
        }

        [UnityTest, Timeout(180000)] public IEnumerator Save_Load_Keeps_Resource_State_And_The_Layer_Is_Cheap()
        {
            yield return LoadIsland();
            SaveSystem.Delete();
            var p = _gm.Player; var pi = p.GetComponent<PlayerInteraction>(); var inv = p.GetComponent<InventorySystem>();
            int count = Interactable.Active.OfType<ResourceNode>().Count();
            Assert.Greater(count, 800, "nodes on the island");
            Assert.AreEqual(0, Interactable.Active.OfType<ResourceNode>().Count(n => !n.definition), "every node has a ResourceDefinition");
            yield return new WaitForSeconds(2f);
            float ms = ResourceManager.AverageTickMs;
            Debug.Log($"[Resources] {count} nodes, ResourceManager average {ms:F4} ms per frame");
            Assert.Less(ms, 0.25f, "the whole resource layer costs well under a quarter millisecond");
            // a picked fruit bunch, a used node and a chopped tree survive save / load
            var fc = Object.FindObjectsByType<FruitCluster>(FindObjectsSortMode.None).First(x => x.Ripe); inv.Clear();
            Assert.IsTrue(fc.Harvest(inv)); double until = fc.EmptyUntil;
            var node = Interactable.Active.OfType<ResourceNode>().Where(n => n && n.Def.id == "stone_medium" && n.Remaining >= 3).OrderBy(n => Flat(n.transform.position - Spawn).magnitude).First();
            bool tg = false; yield return StandAt(node, pi, b => tg = b); inv.Clear();
            node.Hit(pi); int rem = node.Remaining; Assert.Less(rem, node.charges);
            Assert.IsTrue(_gm.SaveGame(), "save: " + SaveSystem.LastError);
            _gm.LoadGame(); yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(rem, node.Remaining, "the used node kept its amount");
            bool sections = typeof(SaveSystem).GetMethod("RegisterSection") != null;
            if (sections)
            {
                Assert.IsFalse(fc.Ripe, "the picked bunch is still empty after loading (save section 'resources')");
                Assert.AreEqual(until, fc.EmptyUntil, 1.0, "same regrow time");
            }
            else Debug.Log("[Resources] fruit save waits for SURV's save sections (SaveSystem.RegisterSection not in this build)");
            SaveSystem.Delete();
        }
    }
}
