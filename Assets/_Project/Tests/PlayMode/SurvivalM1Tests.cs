using System;
using System.Collections;
using System.Collections.Generic;
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
    /// Survival milestone 1 (agent S2): campfire fuel from item data, cooking slots raw -> ready -> burned, taking food,
    /// boiling salt water refused (the container stays salt in the pack), rain collector (only in rain, fills a
    /// container), campfire slots / collector water through ISaveableStructure and the real save file, and the content
    /// made by PrimalSurvivalBuilder. Short timings come from runtime item copies and a test SurvivalConfig.
    /// </summary>
    public class SurvivalM1Tests
    {
        static ItemDatabase Db => ItemDatabase.Instance;
        SurvivalConfig _cfg;
        readonly List<Object> _made = new List<Object>();
        int _collected;

        [UnitySetUp] public IEnumerator ClearScene() { yield return TestScenes.ClearIfGameplayLeft(); }

        [SetUp] public void SetUp()
        {
            _cfg = ScriptableObject.CreateInstance<SurvivalConfig>(); _cfg.name = "SurvivalConfig (test)";
            if (Db != null) { _cfg.wood = Db.Item("wood"); _cfg.rawMeat = Db.Item("raw_meat"); _cfg.cookedMeat = Db.Item("cooked_meat"); }
            _made.Add(_cfg);
            SurvivalConfig.Override(_cfg);
            _collected = 0;
        }

        [TearDown] public void TearDown()
        {
            GameEvents.Raised -= CountCollected;
            SurvivalConfig.Override(null);
            foreach (var o in _made) if (o) Object.Destroy(o);
            _made.Clear();
            GameManager.ForceShowTitle = null; GameManager.ForcePlayIntro = null; PlayerInputReader.Simulate = false; Time.timeScale = 1f;
        }

        [UnityTearDown] public IEnumerator Unload() { yield return TestScenes.ClearIfGameplayLeft(); }

        // ------------------------------------------------------------------ helpers
        T Keep<T>(T o) where T : Object { _made.Add(o); return o; }

        ItemDefinition MakeItem(string id, Action<ItemDefinition> cfg = null)
        {
            var it = Keep(ScriptableObject.CreateInstance<ItemDefinition>());
            it.id = id; it.name = id; it.displayName = id; it.maxStack = 10; it.weight = 0.1f;
            cfg?.Invoke(it);
            return it;
        }

        InventorySystem NewPack()
        {
            var go = Keep(new GameObject("TestPack"));
            var inv = go.AddComponent<InventorySystem>(); inv.raiseGameEvents = false; inv.maxWeight = 0f; inv.EnsureSlots();
            return inv;
        }

        /// <summary>the real campfire prefab when the database has it (visuals, fx), else a bare component</summary>
        Campfire NewFire(Vector3 pos)
        {
            var item = Db != null ? Db.Item("campfire") : null;
            GameObject go = item && item.placePrefab ? BuildSystem.Spawn(item, pos, Quaternion.identity, null) : new GameObject("TestFire", typeof(Campfire));
            go.transform.position = pos;
            Keep(go);
            return go.GetComponent<Campfire>();
        }

        RainCollector NewCollector(Vector3 pos)
        {
            var item = Db != null ? Db.Item("rain_collector") : null;
            GameObject go = item && item.placePrefab && item.placePrefab.GetComponent<RainCollector>() ? BuildSystem.Spawn(item, pos, Quaternion.identity, null)
                                                                                                 : new GameObject("TestCollector", typeof(RainCollector));
            go.transform.position = pos;
            Keep(go);
            return go.GetComponent<RainCollector>();
        }

        void SetBoil(WaterType t, float seconds)
        {
            for (int i = 0; i < _cfg.water.Length; i++) if (_cfg.water[i].type == t) _cfg.water[i].boilSeconds = seconds;
        }

        static IEnumerator Until(Func<bool> done, float timeout)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!done() && Time.realtimeSinceStartup - t0 < timeout) yield return null;
        }

        void CountCollected(GameEvent e) { if (e.type == GameEventType.WaterCollected) _collected += e.amount; }

        // ------------------------------------------------------------------ fire
        [Test] public void Fuel_Comes_From_Item_Data()
        {
            var fire = NewFire(new Vector3(0, 0, 0)); var inv = NewPack();
            fire.fuelItem = null;
            var rock = MakeItem("t_rock");                                    // fuelSeconds 0: not a fuel
            var kindling = MakeItem("t_kindling", i => i.fuelSeconds = 30f);
            inv.Add(rock, 1);
            Assert.IsFalse(fire.TryLight(inv), "no fuel, no fire");
            Assert.IsFalse(fire.IsLit);
            inv.Add(kindling, 4);
            Assert.IsTrue(fire.TryLight(inv), "any item with fuelSeconds lights it");
            Assert.IsTrue(fire.IsLit); Assert.AreEqual(30f, fire.Fuel, 0.01f); Assert.AreEqual(3, inv.Count(kindling));
            Assert.IsTrue(fire.TryAddFuel(inv, kindling)); Assert.AreEqual(60f, fire.Fuel, 0.01f);
            // the campfire's own (legacy) fuel item without a value burns SurvivalConfig.legacyFuelSeconds
            var legacy = MakeItem("t_legacy"); fire.fuelItem = legacy; _cfg.legacyFuelSeconds = 50f; inv.Add(legacy, 1);
            Assert.AreEqual(50f, fire.FuelSecondsOf(legacy), 0.01f);
            Assert.IsTrue(fire.TryAddFuel(inv, legacy)); Assert.AreEqual(110f, fire.Fuel, 0.01f);
            // the maximum comes from the config; a full fire takes nothing
            _cfg.maxFuelSeconds = 120f;
            Assert.IsTrue(fire.TryAddFuel(inv, kindling)); Assert.AreEqual(120f, fire.Fuel, 0.01f);
            int left = inv.Count(kindling);
            Assert.IsFalse(fire.TryAddFuel(inv, kindling), "full");
            Assert.AreEqual(left, inv.Count(kindling), "nothing used on a full fire");
            Assert.AreEqual(0f, fire.FuelSecondsOf(rock));
        }

        [UnityTest] public IEnumerator Cooking_Raw_Ready_Burned_And_Take()
        {
            var look = Db != null ? Db.Item("raw_meat") : null;
            var burnt = MakeItem("t_burnt", i => { i.category = ItemCategory.Food; i.hunger = 4f; if (look) i.worldPrefab = look.worldPrefab; });
            var cooked = MakeItem("t_cooked", i => { i.category = ItemCategory.Food; i.hunger = 35f; i.burnSeconds = 0.6f; if (look) i.worldPrefab = look.worldPrefab; });
            var raw = MakeItem("t_raw", i => { i.category = ItemCategory.Food; i.hunger = 10f; i.cookSeconds = 0.4f; i.cookedResult = cooked; if (look) i.worldPrefab = look.worldPrefab; });
            var fuel = MakeItem("t_fuel", i => i.fuelSeconds = 200f);
            _cfg.burntFood = burnt;
            var fire = NewFire(new Vector3(3, 0, 0)); var inv = NewPack();
            inv.Add(fuel, 2); inv.Add(raw, 3);
            Assert.IsFalse(fire.TryCook(inv, raw), "a cold fire does not cook");
            Assert.IsTrue(fire.TryLight(inv));
            Assert.IsTrue(fire.TryCook(inv, raw));
            Assert.AreEqual(1, fire.CookingCount); Assert.AreEqual(2, inv.Count(raw), "one piece left the pack");
            Assert.AreEqual(Campfire.CookState.Cooking, fire.StateOf(0));
            if (raw.worldPrefab) Assert.IsNotNull(fire.VisualOn(0), "the food is shown on the fire");
            Assert.IsFalse(fire.TryTake(inv), "nothing to take while it cooks");
            // a cold fire pauses the timer (raw stays raw)
            fire.SetLit(false);
            Assert.AreEqual(Campfire.CookState.Raw, fire.StateOf(0));
            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(Campfire.CookState.Raw, fire.StateOf(0), "paused while the fire is out");
            fire.SetLit(true);
            Assert.AreEqual(Campfire.CookState.Cooking, fire.StateOf(0));
            yield return Until(() => fire.StateOf(0) == Campfire.CookState.Ready, 3f);
            Assert.AreEqual(Campfire.CookState.Ready, fire.StateOf(0), "cooked");
            Assert.AreEqual(cooked, fire.ItemOn(0));
            Assert.IsTrue(fire.TryTake(inv), "take the cooked piece");
            Assert.AreEqual(1, inv.Count(cooked)); Assert.AreEqual(0, fire.CookingCount);
            // the next piece stays on until it burns
            Assert.IsTrue(fire.TryCook(inv, raw));
            yield return Until(() => fire.StateOf(0) == Campfire.CookState.Burned, 4f);
            Assert.AreEqual(Campfire.CookState.Burned, fire.StateOf(0), "left too long: burned");
            Assert.AreEqual(burnt, fire.ItemOn(0));
            Assert.IsTrue(fire.TryTake(inv)); Assert.AreEqual(1, inv.Count(burnt));
            // no burnt result at all: the food is lost
            _cfg.burntFood = null;
            Assert.IsTrue(fire.TryCook(inv, raw));
            yield return Until(() => fire.CookingCount == 0, 4f);
            Assert.AreEqual(0, fire.CookingCount, "burned to ash");
            Assert.AreEqual(1, inv.Count(burnt), "nothing new in the pack");
        }

        [Test] public void Slots_Follow_Config_And_Raw_Food_Comes_Back_From_A_Cold_Fire()
        {
            var cooked = MakeItem("t_cooked2", i => i.hunger = 20f);
            var raw = MakeItem("t_raw2", i => { i.hunger = 5f; i.cookSeconds = 30f; i.cookedResult = cooked; });
            var fuel = MakeItem("t_fuel2", i => i.fuelSeconds = 100f);
            _cfg.cookingSlots = 2;
            var fire = NewFire(new Vector3(6, 0, 0)); var inv = NewPack();
            inv.Add(fuel, 1); inv.Add(raw, 4);
            Assert.IsTrue(fire.TryLight(inv));
            Assert.IsTrue(fire.TryCook(inv, raw)); Assert.IsTrue(fire.TryCook(inv, raw));
            Assert.IsFalse(fire.TryCook(inv, raw), "only cookingSlots places");
            Assert.AreEqual(2, fire.CookingCount);
            Assert.IsFalse(fire.TryTake(inv), "a burning fire keeps raw food until it is cooked");
            fire.SetLit(false);
            Assert.IsTrue(fire.TryTake(inv), "the fire is out: raw food can be taken back");
            Assert.AreEqual(1, fire.CookingCount); Assert.AreEqual(3, inv.Count(raw));
        }

        [Test] public void Boiling_Salt_Water_Is_Refused_And_Stays_Salt()
        {
            SetBoil(WaterType.SaltWater, 0.4f);                  // even a config that would boil it: the code rule wins
            var gourd = MakeItem("t_gourd", i => { i.category = ItemCategory.Survival; i.maxStack = 1; i.waterCharges = 3; });
            var fuel = MakeItem("t_fuel3", i => i.fuelSeconds = 100f);
            var fire = NewFire(new Vector3(9, 0, 0)); var inv = NewPack();
            var st = new ItemStack(gourd, 1);
            Assert.AreEqual(3, WaterRules.Fill(st, WaterType.SaltWater));
            inv.SetSlot(0, st); inv.Add(fuel, 1);
            Assert.IsFalse(WaterRules.CanBoil(st), "salt water is never boiled");
            Assert.IsTrue(fire.TryLight(inv));
            Assert.IsFalse(fire.TryBoil(inv, 0), "a lit fire refuses salt water (boiling does not remove salt)");
            Assert.AreSame(st, inv.Get(0), "the container stays in the pack");
            Assert.IsFalse(fire.Boiling); Assert.IsNull(fire.WaterOn(0), "nothing was put on the fire");
            Assert.AreEqual(WaterType.SaltWater, WaterRules.TypeOf(st), "still salt water");
            Assert.AreEqual(3, st.water, "no charge lost");
        }

        // ------------------------------------------------------------------ rain collector
        [UnityTest] public IEnumerator RainCollector_Fills_Only_In_Rain_And_Fills_A_Container()
        {
            var wm = Keep(new GameObject("TestWeather")).AddComponent<WeatherManager>(); wm.allowRandom = false;
            wm.SetWeather(WeatherState.Clear, -1f, true);
            _cfg.collectorCapacity = 4;
            _cfg.collectorChargesPerRainHour = 3f * GameClock.SecondsPerHour;          // 3 charges per real second at normal rain
            var rc = NewCollector(new Vector3(20, 0, 0));
            GameEvents.Raised += CountCollected;
            yield return new WaitForSeconds(1.4f);
            Assert.AreEqual(0, rc.Charges, "dry weather: no water");
            wm.SetWeather(WeatherState.Rain, -1f, true);
            Assert.IsTrue(wm.RainingAt(rc.transform.position + Vector3.up), "rain falls on the collector");
            yield return Until(() => rc.Charges >= 2, 4f);
            Assert.GreaterOrEqual(rc.Charges, 2, "collects in the rain");
            Assert.Greater(_collected, 0, "WaterCollected raised");
            yield return Until(() => rc.IsFull, 4f);
            Assert.AreEqual(4, rc.Charges, "capped at collectorCapacity");
            wm.SetWeather(WeatherState.Clear, -1f, true);
            var cup = MakeItem("t_cup", i => { i.maxStack = 1; i.waterCharges = 3; });
            var salty = new ItemStack(cup, 1); WaterRules.Fill(salty, WaterType.SaltWater, 1);
            Assert.IsFalse(rc.TryFill(salty), "no mixing with salt water");
            var st = new ItemStack(cup, 1);
            Assert.IsTrue(rc.TryFill(st));
            Assert.AreEqual(3, st.water); Assert.AreEqual(WaterType.CleanWater, WaterRules.TypeOf(st), "rain water is clean");
            Assert.AreEqual(1, rc.Charges, "3 of 4 charges poured");
            // save state round trip
            string json = rc.CaptureState();
            var copy = NewCollector(new Vector3(24, 0, 0));
            copy.RestoreState(json);
            Assert.AreEqual(rc.Water, copy.Water, 0.001f, "collector water saved");
        }

        // ------------------------------------------------------------------ save
        [UnityTest] public IEnumerator Campfire_Slots_Survive_Save_State_Round_Trip()
        {
            Assert.IsNotNull(Db, "Resources/ItemDatabase");
            var raw = Db.Item("raw_meat"); var gourd = Db.Item("water_container"); var wood = Db.Item("wood");
            Assert.IsNotNull(raw); Assert.IsNotNull(gourd); Assert.IsNotNull(wood);
            var fire = NewFire(new Vector3(30, 0, 0)); var inv = NewPack();
            inv.Add(wood, 2); inv.Add(raw, 1);
            var st = new ItemStack(gourd, 1); WaterRules.Fill(st, WaterType.DirtyWater, 2);
            int free = Array.FindIndex(inv.Slots, s => s == null); inv.SetSlot(free, st);
            Assert.IsTrue(fire.TryLight(inv, wood));
            Assert.IsTrue(fire.TryCook(inv, raw));
            Assert.IsTrue(fire.TryBoil(inv, free));
            yield return new WaitForSeconds(0.3f);
            string json = fire.CaptureState();
            Assert.IsFalse(string.IsNullOrEmpty(json), "slots captured");
            bool lit = fire.IsLit; float fuel = fire.Fuel; float progress = fire.ProgressOf(0);
            Assert.Greater(progress, 0f, "the meat was cooking");
            fire.SetLit(false);                                                // freeze the original for the comparison
            // SaveSystem order: Restore(lit, fuel) then RestoreState; the other order must give the same fire
            var a = NewFire(new Vector3(33, 0, 0)); a.Restore(lit, fuel); a.RestoreState(json);
            var b = NewFire(new Vector3(36, 0, 0)); b.RestoreState(json); b.Restore(lit, fuel);
            foreach (var f in new[] { a, b })
            {
                Assert.IsTrue(f.IsLit);
                Assert.AreEqual(1, f.CookingCount, "meat back on the fire");
                Assert.AreEqual(raw, f.ItemOn(0)); Assert.AreEqual(Campfire.CookState.Cooking, f.StateOf(0));
                Assert.AreEqual(progress, f.ProgressOf(0), 0.1f, "cooking progress kept");
                Assert.IsTrue(f.Boiling, "container back on the fire");
                var w = f.WaterOn(1); Assert.IsNotNull(w);
                Assert.AreEqual(gourd, w.item); Assert.AreEqual(2, w.water); Assert.AreEqual(WaterType.DirtyWater, w.waterType);
            }
            var cold = NewFire(new Vector3(39, 0, 0)); cold.Restore(false, 0f); cold.RestoreState(json);
            Assert.AreEqual(Campfire.CookState.Raw, cold.StateOf(0), "a cold fire keeps the food paused");
            Assert.IsNull(NewFire(new Vector3(42, 0, 0)).CaptureState(), "an empty fire saves nothing extra");
        }

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

        [UnityTest] public IEnumerator Island_Save_Load_Keeps_Fire_Slots_And_Collector_Water()
        {
            SurvivalConfig.Override(null);                                    // the island runs on the project config
            yield return LoadIsland();
            var rcItem = Db.Item("rain_collector");
            Assert.IsNotNull(rcItem, "rain_collector item (run PrimalSurvivalBuilder.Build)");
            Assert.IsNotNull(rcItem.placePrefab, "rain_collector prefab");
            var wm = WeatherManager.Instance; if (wm) { wm.allowRandom = false; wm.SetWeather(WeatherState.Clear, -1f, true); }
            var p = _gm.Player; var inv = p.GetComponent<InventorySystem>();
            var fire = BuildSystem.Spawn(Db.Item("campfire"), p.transform.position + p.transform.forward * 2f, Quaternion.identity, null).GetComponent<Campfire>();
            var rc = BuildSystem.Spawn(rcItem, p.transform.position + p.transform.right * 3f, Quaternion.identity, null).GetComponent<RainCollector>();
            Assert.IsNotNull(fire); Assert.IsNotNull(rc);
            var raw = Db.Item("raw_meat");
            inv.Add(Db.Item("wood"), 2); inv.Add(raw, 1);
            Assert.IsTrue(fire.TryLight(inv)); Assert.IsTrue(fire.TryCook(inv, raw));
            rc.SetWater(3f);
            yield return null;
            Assert.IsTrue(_gm.SaveGame(), "save: " + SaveSystem.LastError);
            _gm.LoadGame(); yield return new WaitForSeconds(0.5f);
            var fire2 = PlacedStructure.All.Select(s => s ? s.GetComponent<Campfire>() : null).FirstOrDefault(c => c);
            var rc2 = PlacedStructure.All.Select(s => s ? s.GetComponent<RainCollector>() : null).FirstOrDefault(c => c);
            Assert.IsNotNull(fire2, "campfire restored"); Assert.IsNotNull(rc2, "rain collector restored");
            Assert.IsTrue(fire2.IsLit, "fire still lit");
            Assert.AreEqual(1, fire2.CookingCount, "meat still on the fire");
            Assert.AreEqual(raw, fire2.ItemOn(0));
            Assert.AreEqual(3, rc2.Charges, "collector water restored");
            SaveSystem.Delete();
        }

        // ------------------------------------------------------------------ content (PrimalSurvivalBuilder)
        [Test] public void Survival_Content_Is_Built()
        {
            Assert.IsNotNull(Db, "Resources/ItemDatabase");
            var cfg = Resources.Load<SurvivalConfig>("SurvivalConfig");
            Assert.IsNotNull(cfg, "Resources/SurvivalConfig (run PrimalSurvivalBuilder.Build)");
            Assert.IsNotNull(cfg.wood, "config wood"); Assert.IsNotNull(cfg.rawMeat, "config raw meat");
            Assert.IsNotNull(cfg.cookedMeat, "config cooked meat"); Assert.IsNotNull(cfg.burntFood, "config burnt food");
            foreach (var id in new[] { "leaf_cup", "burnt_meat", "rain_collector", "tent" })
            {
                var it = Db.Item(id); Assert.IsNotNull(it, "item " + id); Assert.IsNotNull(it.icon, "icon " + id);
            }
            Assert.AreEqual(1, Db.Item("leaf_cup").waterCharges, "leaf cup holds one drink");
            Assert.Greater(Db.Item("burnt_meat").hunger, 0f);
            Assert.Greater(Db.Item("wood").fuelSeconds, 0f, "wood is fuel"); Assert.Greater(Db.Item("fiber").fuelSeconds, 0f, "fiber is fuel");
            var tent = Db.Item("tent").placePrefab; Assert.IsNotNull(tent, "tent prefab");
            var sh = tent.GetComponentInChildren<Shelter>(); Assert.IsNotNull(sh, "tent shelter"); Assert.AreEqual(2.8f, sh.coverRadius, 0.01f); Assert.AreEqual(6f, sh.warmth, 0.01f);
            Assert.IsNotNull(tent.GetComponentInChildren<Bedroll>(), "tent bed");
            var rc = Db.Item("rain_collector").placePrefab; Assert.IsNotNull(rc, "rain collector prefab");
            Assert.IsNotNull(rc.GetComponent<RainCollector>()); Assert.IsNotNull(rc.GetComponent<Collider>());
            Assert.IsNull(Db.Recipe("cooked_meat"), "one cooking path: meat is cooked on the fire, not crafted");
            foreach (var id in new[] { "leaf_cup", "rain_collector", "tent" }) Assert.IsNotNull(Db.Recipe(id), "recipe " + id);
            Assert.IsTrue(Db.Recipe("leaf_cup").knownAtStart, "leaf cup known at start");
            Assert.LessOrEqual(Db.recipes.Count, 40);
            // the tent covers the player inside it
            var go = Keep(BuildSystem.Spawn(Db.Item("tent"), new Vector3(50, 0, 50), Quaternion.identity, null));
            Assert.IsTrue(Shelter.Covers(go.transform.position + Vector3.up * 0.5f), "inside the tent is covered");
        }
    }
}
