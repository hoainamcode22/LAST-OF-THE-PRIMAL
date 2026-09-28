using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PrimalFrontier.Building;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.Survival;
using PrimalFrontier.World;
using Object = UnityEngine.Object;

namespace PrimalFrontier.Tests
{
    /// <summary>
    /// Milestone 1 as ONE play-through on the island, through the same calls the input uses (Interact, crafting queue,
    /// placement spawn, consumables): gather -> craft axe + cup -> campfire lit with wood -> cook meat until ready -> eat ->
    /// sea water -> boil -> drink clean -> rain collector in the rain -> tent -> sleep through the night -> save / load.
    /// Material top-ups are given where gathering would only cost minutes (each is commented); time is sped up while
    /// waiting on timers (cooking, boiling, rain).
    /// </summary>
    public class SurvivalM1LoopTest
    {
        static ItemDatabase Db => ItemDatabase.Instance;
        GameManager _gm;

        IEnumerator LoadIsland()
        {
            TestScenes.UseTestSaves(); GameManager.ForceShowTitle = false; GameManager.ForcePlayIntro = false;
            PlayerInputReader.Simulate = true;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/Island_VerticalSlice.unity", new LoadSceneParameters(LoadSceneMode.Single));
#endif
            float t = 0; while ((_gm = GameManager.Instance) == null || _gm.State != GameState.Playing) { t += Time.unscaledDeltaTime; if (t > 20f) break; yield return null; }
            Assert.IsNotNull(_gm, "GameManager"); Assert.AreEqual(GameState.Playing, _gm.State, "game started");
            yield return new WaitForSeconds(1f);
        }

        [TearDown] public void Cleanup() { GameManager.ForceShowTitle = null; GameManager.ForcePlayIntro = null; PlayerInputReader.Simulate = false; Time.timeScale = 1f; }
        [UnityTearDown] public IEnumerator Unload() { yield return TestScenes.ClearIfGameplayLeft(); }

        static void Log(string m) { Debug.Log("[M1 loop] " + m); TestContext.WriteLine(m); }

        static IEnumerator Until(Func<bool> done, float realSeconds, float timeScale = 1f)
        {
            Time.timeScale = timeScale; float t = 0f;
            while (!done() && t < realSeconds) { t += Time.unscaledDeltaTime; yield return null; }
            Time.timeScale = 1f;
        }

        static IEnumerator Act(PlayerInteraction pi, Action start, Func<bool> done, float timeout = 10f)
        {
            float t = 0f;
            while (!done() && t < timeout) { if (!pi.InAction) start(); yield return null; t += Time.unscaledDeltaTime; }
        }

        IEnumerator WalkTo(PlayerMotor m, Vector3 target, float standOff = 1.3f)
        {
            Vector3 from = m.transform.position; Vector3 d = from - target; d.y = 0f;
            Vector3 stand = target + (d.sqrMagnitude > 0.01f ? d.normalized : Vector3.back) * standOff;
            var terrain = Terrain.activeTerrain; if (terrain) stand.y = terrain.SampleHeight(stand) + terrain.transform.position.y;
            m.Warp(stand + Vector3.up * 0.3f, Quaternion.LookRotation(Flat(target - stand)));
            yield return new WaitForSeconds(0.6f);
        }
        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-4f ? v : Vector3.forward; }

        GameObject Place(string itemId, Vector3 near, Vector3 offset)
        {
            var item = Db.Item(itemId); Assert.IsNotNull(item, itemId);
            Vector3 p = near + offset; var terrain = Terrain.activeTerrain; if (terrain) p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
            return BuildSystem.Spawn(item, p, Quaternion.LookRotation(Flat(near - p)), null);
        }

        /// <summary>put the first matching stack in the hand (moved onto the hotbar when it landed in the bag); its slot</summary>
        static int Hold(InventorySystem inv, Func<ItemStack, bool> match)
        {
            int i = Array.FindIndex(inv.Slots, s => s != null && !s.IsEmpty && match(s));
            Assert.GreaterOrEqual(i, 0, "item to hold is in the pack");
            if (i >= inv.hotbarSize) { int to = inv.hotbarSize - 1; Assert.IsTrue(inv.Move(i, inv, to), "moved onto the hotbar"); i = to; }
            inv.SetActiveSlot(i);
            Assert.AreSame(inv.Get(i), inv.ActiveStack, "held");
            return i;
        }
        static int Hold(InventorySystem inv, string id) => Hold(inv, s => s.item && s.item.id == id);

        /// <summary>material top-up standing in for gathering that would only cost more minutes (see the comments at each call)</summary>
        static void Give(InventorySystem inv, string id, int n) { var it = Db.Item(id); Assert.IsNotNull(it, id); Assert.AreEqual(0, inv.Add(it, n), "room for " + n + " " + id); }

        static void Craft(CraftingSystem cr, InventorySystem inv, string recipe, string product)
        {
            var r = Db.Recipe(recipe); Assert.IsNotNull(r, "recipe " + recipe);
            int before = inv.Count(Db.Item(product));
            Assert.IsNull(cr.Check(r), $"can craft {recipe}: {cr.Check(r)}");
            Assert.IsTrue(cr.Enqueue(r), "queued " + recipe);
            cr.CompleteFirstNow();
            Assert.Greater(inv.Count(Db.Item(product)), before, "crafted " + product);
        }

        [UnityTest] public IEnumerator Milestone1_Spawn_To_Night_And_Save()
        {
            yield return LoadIsland();
            StringAssert.Contains("PF_TestSaves", SaveSystem.Folder, "test saves go to a temp folder, never the player's slots");
            SaveSystem.Delete();
            var p = _gm.Player; var pi = p.GetComponent<PlayerInteraction>(); var inv = p.GetComponent<InventorySystem>();
            var motor = p.GetComponent<PlayerMotor>(); var sv = p.GetComponent<PlayerSurvival>(); var cr = p.GetComponent<CraftingSystem>();
            var cfg = SurvivalConfig.Instance;
            Assert.IsTrue(cfg && cfg.name != "SurvivalConfig (default)", "SurvivalConfig asset in Resources");

            // 1. start with (almost) nothing
            Assert.AreEqual(0, inv.Slots.Count(s => s != null && !s.IsEmpty), "the survivor starts with an empty pack");
            Assert.AreEqual(1f, sv.MoveSpeedMultiplier, 1e-4f, "no need penalty at the start");

            // 2. gather for real: one stone node and one fibre / wood node by hand
            foreach (var want in new[] { "stone", "fiber" })
            {
                var node = Interactable.Active.OfType<ResourceNode>().Where(n => n.yieldItem && n.yieldItem.id == want && n.requiredTool == ToolKind.None && n.CanInteract(pi))
                    .OrderBy(n => (n.transform.position - p.transform.position).sqrMagnitude).FirstOrDefault();
                Assert.IsNotNull(node, "a hand-gatherable " + want + " node");
                yield return WalkTo(motor, node.transform.position);
                int before = inv.Count(Db.Item(want));
                yield return Act(pi, () => node.Interact(pi), () => inv.Count(Db.Item(want)) > before, 15f);
                Assert.Greater(inv.Count(Db.Item(want)), before, "gathered " + want);
                Log($"gathered {want} by hand from {node.displayName}: {inv.Count(Db.Item(want))}");
                pi.StopAction(); yield return new WaitForSeconds(0.3f);
            }

            // 3. rope -> stone axe, and a cup (top-up: what more node hits / a branch would give)
            Give(inv, "stone", 2); Give(inv, "fiber", 6); Give(inv, "wood", 1);
            Craft(cr, inv, "rope", "rope");
            Craft(cr, inv, "stone_axe", "stone_axe");
            Craft(cr, inv, "leaf_cup", "leaf_cup");

            // 4. campfire: craft, place, light with one wood (top-up: wood / stone for the fire)
            Give(inv, "wood", 8); Give(inv, "stone", 5);
            Craft(cr, inv, "campfire", "campfire");
            var fireGo = Place("campfire", p.transform.position, p.transform.forward * 1.8f); inv.Remove(Db.Item("campfire"), 1);
            var fire = fireGo.GetComponent<Campfire>(); Assert.IsNotNull(fire, "campfire placed");
            yield return new WaitForSeconds(0.3f);
            int woodBefore = inv.Count(Db.Item("wood"));
            yield return Act(pi, () => fire.Interact(pi), () => fire.IsLit);
            Assert.IsTrue(fire.IsLit, "fire lit");
            Assert.AreEqual(woodBefore - 1, inv.Count(Db.Item("wood")), "lighting burns one wood");
            Assert.Greater(fire.Fuel, 0f, "fuel from the wood's fuelSeconds");
            Assert.Greater(PlayerSurvival.HeatAt(p.transform.position), 0f, "the fire warms");
            Log($"fire lit with 1 wood: fuel {fire.Fuel:F0} s, heat at the player {PlayerSurvival.HeatAt(p.transform.position):F1} C");

            // 5. cook raw meat: raw -> cooking -> ready, take it, eat it
            Give(inv, "raw_meat", 1);                      // top-up: meat from the wreck / a hunt (milestone 2)
            Hold(inv, "raw_meat");
            yield return Act(pi, () => fire.Interact(pi), () => fire.CookingCount == 1);
            Assert.AreEqual(1, fire.CookingCount, "meat on the fire");
            yield return Until(() => Enumerable.Range(0, fire.SlotCount).Any(i => fire.StateOf(i) == Campfire.CookState.Ready), 20f, 8f);
            Assert.IsTrue(Enumerable.Range(0, fire.SlotCount).Any(i => fire.StateOf(i) == Campfire.CookState.Ready), "cooked meat is ready");
            int cookedBefore = inv.Count(Db.Item("cooked_meat"));
            yield return Act(pi, () => fire.Interact(pi), () => inv.Count(Db.Item("cooked_meat")) > cookedBefore);
            Assert.AreEqual(cookedBefore + 1, inv.Count(Db.Item("cooked_meat")), "took the cooked meat");
            sv.SetStats(40f, sv.Thirst, sv.Stamina, sv.BodyTemperature, 0f);
            Hold(inv, "cooked_meat");
            yield return Act(pi, () => pi.UseActiveConsumable(), () => sv.Hunger > 60f);
            Assert.Greater(sv.Hunger, 60f, "eating cooked meat fills hunger");
            Log($"meat cooked, taken and eaten: hunger 40 -> {sv.Hunger:F0}");

            // 6. water: fill the cup at the sea (salt), boil it at the fire (clean), drink
            int cupSlot = Hold(inv, "leaf_cup"); var cup = inv.Get(cupSlot);
            var ocean = Object.FindFirstObjectByType<OceanShore>(); Assert.IsNotNull(ocean);
            var terrain = Terrain.activeTerrain; float ty = terrain.transform.position.y; Vector3 spawn = p.transform.position, shore = spawn; float best = float.MaxValue;
            for (float x = -160f; x <= 160f; x += 1.5f)
                for (float z = -160f; z <= 160f; z += 1.5f)
                {
                    var q = spawn + new Vector3(x, 0, z); float h = terrain.SampleHeight(q) + ty;
                    if (h > -0.02f || h < -0.3f) continue; float d2 = x * x + z * z;
                    if (d2 < best) { best = d2; shore = new Vector3(q.x, h, q.z); }
                }
            Vector3 camp = fireGo.transform.position;
            motor.Warp(shore + Vector3.up * 0.3f, p.transform.rotation); yield return new WaitForSeconds(0.8f);
            Assert.IsTrue(ocean.PlayerAtShore, "at the shore");
            yield return Act(pi, () => ocean.Interact(pi), () => cup.water > 0);
            Assert.AreEqual(WaterType.SaltWater, WaterRules.TypeOf(cup), "sea water fills as salt water");
            yield return WalkTo(motor, camp, 1.4f);
            cupSlot = Hold(inv, s => s == cup);
            yield return Act(pi, () => fire.Interact(pi), () => fire.Boiling);
            Assert.IsTrue(fire.Boiling, "cup boiling on the fire");
            yield return Until(() => !fire.Boiling, 30f, 8f);
            yield return Act(pi, () => fire.Interact(pi), () => inv.Slots.Any(s => s != null && s.item == Db.Item("leaf_cup")));
            cupSlot = Hold(inv, "leaf_cup"); cup = inv.Get(cupSlot);
            Assert.AreEqual(WaterType.CleanWater, WaterRules.TypeOf(cup), "boiled into clean water");
            sv.SetStats(sv.Hunger, 30f, sv.Stamina, sv.BodyTemperature, 0f);
            yield return Act(pi, () => pi.UseActiveConsumable(), () => cup.water == 0);
            Assert.Greater(sv.Thirst, 30f + cfg.Water(WaterType.CleanWater).thirst - 1f, "clean water quenches thirst");
            Log($"sea water -> boiled -> clean, drunk: thirst 30 -> {sv.Thirst:F0}");

            // 7. rain collector: craft (top-up: hide from a carcass, milestone 2), place, let it rain, fill the cup
            Give(inv, "wood", 4); Give(inv, "fiber", 6); Give(inv, "hide", 1);
            Craft(cr, inv, "rain_collector", "rain_collector");
            var colGo = Place("rain_collector", camp, new Vector3(3.5f, 0f, 1.5f)); inv.Remove(Db.Item("rain_collector"), 1);
            var col = colGo.GetComponent<RainCollector>(); Assert.IsNotNull(col, "collector placed");
            var wm = WeatherManager.Instance; wm.SetWeather(WeatherState.Clear, -1f, true);
            yield return Until(() => false, 1.5f, 8f);
            Assert.AreEqual(0, col.Charges, "no water without rain");
            wm.SetWeather(WeatherState.Rain, 3f, true);
            yield return Until(() => col.Charges >= 1, 25f, 10f);
            Assert.GreaterOrEqual(col.Charges, 1, "the collector fills in the rain"); int rained = col.Charges;
            yield return WalkTo(motor, colGo.transform.position, 1.1f);
            cupSlot = Hold(inv, "leaf_cup"); cup = inv.Get(cupSlot);
            yield return Act(pi, () => col.Interact(pi), () => cup.water > 0);
            Assert.AreEqual(WaterType.CleanWater, WaterRules.TypeOf(cup), "rain water is clean");
            Log($"rain collector: 0 charges in clear weather, {rained} after rain, {col.Charges} left after filling the cup (clean)");
            wm.SetWeather(WeatherState.Clear, -1f, true);

            // 8. tent before night (top-up: materials), sleep through the night
            Give(inv, "wood", 6); Give(inv, "fiber", 8); Give(inv, "hide", 2);
            Craft(cr, inv, "tent", "tent");
            var tentGo = Place("tent", camp, new Vector3(-4f, 0f, 2f)); inv.Remove(Db.Item("tent"), 1);
            Assert.IsNotNull(tentGo.GetComponent<Shelter>(), "tent gives cover"); var bed = tentGo.GetComponentInChildren<Bedroll>(); Assert.IsNotNull(bed, "tent has a bed");
            var tm = TimeManager.Instance; int day0 = tm.day; tm.Set(day0, 18.6f);
            yield return WalkTo(motor, tentGo.transform.position, 0.9f);
            Assert.IsTrue(bed.CanSleepNow, "evening: can sleep");
            float h0 = sv.Hunger;
            bed.Interact(pi);
            yield return Until(() => _gm.State == GameState.Sleeping, 3f);
            yield return Until(() => _gm.State == GameState.Playing && tm.day > day0, 20f);
            Assert.AreEqual(GameState.Playing, _gm.State, "woke up");
            Assert.Greater(tm.day, day0, "survived the night: a new day");
            Assert.Less(Mathf.Abs(tm.hour - 6f), 1.5f, "woke at dawn, hour " + tm.hour);
            Assert.Less(sv.Hunger, h0, "the night costs food");
            Log($"slept in the tent: day {day0} -> {tm.day}, woke {tm.hour:F1} h, hunger {h0:F0} -> {sv.Hunger:F0}");

            // 9. save / load keeps the camp
            Assert.IsTrue(_gm.SaveGame(), "save: " + SaveSystem.LastError);
            int placed = PlacedStructure.All.Count;
            _gm.LoadGame(); yield return new WaitForSeconds(0.8f);
            Assert.AreEqual(placed, PlacedStructure.All.Count, "structures restored");
            Assert.IsTrue(PlacedStructure.All.Any(s => s.GetComponent<RainCollector>()), "collector restored");
            Assert.IsTrue(PlacedStructure.All.Any(s => s.GetComponent<Shelter>() && s.GetComponentInChildren<Bedroll>()), "tent restored");
            var inv2 = _gm.Player.GetComponent<InventorySystem>();
            var cup2 = inv2.Slots.FirstOrDefault(s => s != null && s.item == Db.Item("leaf_cup"));
            Assert.IsNotNull(cup2, "cup restored");
            Assert.AreEqual(WaterType.CleanWater, WaterRules.TypeOf(cup2), "cup water type restored");
            Log($"save / load: {placed} structures back (fire, collector, tent), cup still clean water");
            SaveSystem.Delete();
        }
    }
}
