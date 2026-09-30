using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.Story;
using PrimalFrontier.Survival;
using PrimalFrontier.World;
using Object = UnityEngine.Object;

namespace PrimalFrontier.Tests
{
    /// <summary>
    /// Survival milestone 1 (agent S1): need tiers from SurvivalConfig (tier-free at the start stats, slower regen / lower max
    /// stamina when low, health drain when critical), eating / sleeping rules, environment (night blend, shelter warmth,
    /// tent bed), water fill types (pond = unboiled, sea with a container = salt, no mixing), drinking clean vs salt,
    /// Empty, and the save round trip of water type + sickness + tutorial step id.
    /// </summary>
    public class SurvivalNeedsTests
    {
        static SurvivalConfig Cfg => SurvivalConfig.Instance;
        static ItemDatabase Db => ItemDatabase.Instance;

        static PlayerSurvival MakeSurvivor(out PlayerHealth hp, out GameObject go)
        {
            go = new GameObject("TestSurvivor");
            go.transform.position = new Vector3(5000f, 0f, 5000f);             // away from any shelter / fire / zone
            var sv = go.AddComponent<PlayerSurvival>();                         // PlayerHealth comes with it (RequireComponent)
            hp = go.GetComponent<PlayerHealth>();
            return sv;
        }

        static ItemDefinition MakeContainer(int charges)
        {
            var it = ScriptableObject.CreateInstance<ItemDefinition>();
            it.id = "test_gourd"; it.displayName = "Test Gourd"; it.waterCharges = charges; it.maxStack = 1;
            return it;
        }

        // ------------------------------------------------------------------ needs tiers
        [Test] public void Start_Stats_Come_From_Config_And_Are_Tier_Free()
        {
            var sv = MakeSurvivor(out _, out var go);
            try
            {
                Assert.AreEqual(Cfg.startHunger, sv.Hunger, 1e-4f, "start hunger from SurvivalConfig");
                Assert.AreEqual(Cfg.startThirst, sv.Thirst, 1e-4f, "start thirst from SurvivalConfig");
                Assert.AreEqual(-1, sv.HungerTier, "no hunger tier at the start");
                Assert.AreEqual(-1, sv.ThirstTier, "no thirst tier at the start");
                Assert.IsNull(sv.HungerTierLabel); Assert.IsNull(sv.ThirstTierLabel);
                Assert.AreEqual(1f, sv.MoveSpeedMultiplier, 1e-6f, "PlayerControllerTests: speed multiplier 1 at the start stats");
                Assert.AreEqual(1f, sv.NeedStaminaRegenMultiplier, 1e-6f);
                Assert.AreEqual(sv.maxStamina, sv.MaxStaminaNow, 1e-4f);
                Assert.AreEqual(0f, sv.NeedHealthDrain, 1e-6f);
                sv.SetStats(0f, 0f, 0f, 37f, 0f); sv.ResetToStart();
                Assert.AreEqual(Cfg.startHunger, sv.Hunger, 1e-4f, "new game resets to the config start");
                Assert.AreEqual(Cfg.startThirst, sv.Thirst, 1e-4f);
                Assert.AreEqual(1f, sv.MoveSpeedMultiplier, 1e-6f);
            }
            finally { Object.Destroy(go); }
        }

        [Test] public void Low_Needs_Slow_Regen_And_Lower_Max_Stamina()
        {
            var sv = MakeSurvivor(out _, out var go);
            try
            {
                sv.SetStats(25f, 80f, 100f, 37f, 0f);
                Assert.IsTrue(SurvivalConfig.TierFor(Cfg.hungerTiers, 25f, out var ht, out int hi), "hunger 25 is in a tier");
                Assert.AreEqual(hi, sv.HungerTier);
                Assert.AreEqual(ht.label, sv.HungerTierLabel, "HUD word");
                Assert.AreEqual(Mathf.Min(1f, ht.staminaRegen), sv.NeedStaminaRegenMultiplier, 1e-4f);
                Assert.Less(sv.NeedStaminaRegenMultiplier, 1f, "stamina regen reduced at hunger 25");
                Assert.Less(sv.MaxStaminaNow, sv.maxStamina, "max stamina reduced at hunger 25");
                Assert.LessOrEqual(sv.Stamina, sv.MaxStaminaNow + 1e-3f, "stamina clamped to the lower max");

                sv.SetStats(80f, 25f, 100f, 37f, 0f);
                Assert.IsTrue(SurvivalConfig.TierFor(Cfg.thirstTiers, 25f, out var tt, out _));
                Assert.AreEqual(tt.label, sv.ThirstTierLabel);
                Assert.Less(sv.NeedStaminaRegenMultiplier, 1f, "thirst 25 slows regen too");
                Assert.Less(sv.MaxStaminaNow, sv.maxStamina);

                // both low: the worse multiplier of each wins, drains add
                sv.SetStats(25f, 25f, 100f, 37f, 0f);
                Assert.AreEqual(Mathf.Min(ht.staminaRegen, tt.staminaRegen), sv.NeedStaminaRegenMultiplier, 1e-4f);
                Assert.AreEqual(sv.maxStamina * Mathf.Min(ht.maxStamina, tt.maxStamina), sv.MaxStaminaNow, 1e-3f);
                Assert.AreEqual(Mathf.Max(0f, ht.healthDrain) + Mathf.Max(0f, tt.healthDrain), sv.NeedHealthDrain, 1e-4f);
            }
            finally { Object.Destroy(go); }
        }

        [UnityTest] public IEnumerator Health_Drains_Only_When_Needs_Are_Critical()
        {
            var sv = MakeSurvivor(out var hp, out var go);
            sv.SetStats(50f, 80f, 100f, 37f, 0f);
            Assert.AreEqual(0f, sv.NeedHealthDrain, 1e-6f, "no drain at hunger 50");
            sv.SetStats(5f, 80f, 100f, 37f, 0f);
            Assert.Greater(sv.NeedHealthDrain, 0f, "hunger below 10 drains health");
            float before = hp.Health;
            yield return new WaitForSeconds(1f);
            Assert.Less(hp.Health, before, "health lost while starving");
            sv.SetStats(80f, 5f, 100f, 37f, 0f);
            Assert.Greater(sv.NeedHealthDrain, 0f, "thirst below 10 drains health");
            Object.Destroy(go);
        }

        [Test] public void Tier_Crossing_Warns_Downward_Only_And_Raises_Event()
        {
            var sv = MakeSurvivor(out _, out var go);
            int warnings = 0, events = 0;
            sv.Warning += m => warnings++;
            Action<GameEvent> h = e => { if (e.type == GameEventType.NeedTierChanged && e.id == PlayerSurvival.HungerEventId) events++; };
            GameEvents.Raised += h;
            try
            {
                float top = Cfg.hungerTiers[0].below;
                sv.SetStats(top + 1f, 80f, 100f, 37f, 0f);
                Assert.AreEqual(0, warnings, "setting stats is silent");
                sv.Consume(-2f, 0f, 0f, 0f);
                Assert.AreEqual(1, warnings, "warning on the way down");
                Assert.AreEqual(1, events, "NeedTierChanged raised");
                Assert.AreEqual(Cfg.hungerTiers[0].label, sv.HungerTierLabel);
                sv.Consume(5f, 0f, 0f, 0f);
                Assert.AreEqual(1, warnings, "no warning on the way up");
                Assert.AreEqual(2, events, "tier change up is still an event");
                Assert.IsNull(sv.HungerTierLabel);
            }
            finally { GameEvents.Raised -= h; Object.Destroy(go); }
        }

        [Test] public void Eating_Sleeping_And_Overweight_Follow_The_Rules()
        {
            var sv = MakeSurvivor(out var hp, out var go);
            var meat = ScriptableObject.CreateInstance<ItemDefinition>();
            meat.id = "test_raw_food"; meat.displayName = "Test Meat"; meat.hunger = 10f; meat.sicknessChance = 1f;
            bool ate = false;
            Action<GameEvent> h = e => { if (e.type == GameEventType.Ate && e.id == "test_raw_food") ate = true; };
            GameEvents.Raised += h;
            try
            {
                // eating: food values + raw-food sickness for the config duration
                sv.SetStats(50f, 80f, 100f, 37f, 0f);
                Assert.IsTrue(sv.ConsumeItem(meat));
                Assert.AreEqual(60f, sv.Hunger, 1e-3f);
                Assert.IsTrue(ate, "Ate event");
                Assert.AreEqual(Cfg.foodSickSeconds, sv.SickSeconds, 1e-3f, "sick for SurvivalConfig.foodSickSeconds");
                sv.RestoreSickness(12.5f); Assert.AreEqual(12.5f, sv.SickSeconds, 1e-4f, "sickness restorable (save)");
                sv.RestoreSickness(0f);

                // sleeping: config costs per hour, floor, heal, full stamina
                sv.SetStats(50f, 70f, 10f, 37f, 0f); hp.ApplyRaw(30f);
                float hours = 10f, hpBefore = hp.Health;
                sv.ApplySleep(hours);
                Assert.AreEqual(Mathf.Max(Cfg.sleepNeedFloor, 50f - hours * Cfg.sleepHungerPerHour), sv.Hunger, 1e-3f, "sleep hunger cost");
                Assert.AreEqual(Mathf.Max(Cfg.sleepNeedFloor, 70f - hours * Cfg.sleepThirstPerHour), sv.Thirst, 1e-3f, "sleep thirst cost");
                Assert.AreEqual(Mathf.Min(hp.maxHealth, hpBefore + hours * Cfg.sleepHealthPerHour), hp.Health, 1e-3f, "sleep heals");
                Assert.AreEqual(sv.MaxStaminaNow, sv.Stamina, 1e-3f, "sleep refills stamina");
                float low = Mathf.Max(0f, Cfg.sleepNeedFloor - 2f);
                sv.SetStats(low, low, 10f, 37f, 0f); sv.ApplySleep(8f);
                Assert.AreEqual(low, sv.Hunger, 1e-4f, "below the floor: sleeping neither lowers nor raises it");
                Assert.AreEqual(low, sv.Thirst, 1e-4f);
            }
            finally { GameEvents.Raised -= h; Object.Destroy(meat); Object.Destroy(go); }

            // overweight follows the inventory (event driven)
            var go2 = new GameObject("TestPack"); go2.transform.position = new Vector3(5000f, 0f, 5000f);
            var inv = go2.AddComponent<InventorySystem>(); inv.raiseGameEvents = false; inv.maxWeight = 5f; inv.EnsureSlots();
            var sv2 = go2.AddComponent<PlayerSurvival>();
            var rock = ScriptableObject.CreateInstance<ItemDefinition>(); rock.id = "test_rock"; rock.weight = 4f; rock.maxStack = 10;
            try
            {
                Assert.IsFalse(sv2.Overweight);
                inv.Add(rock, 3, true);
                Assert.IsTrue(sv2.Overweight, "PlayerSurvival.Overweight follows InventorySystem.IsOverweight");
                Assert.Less(sv2.MoveSpeedMultiplier, 1f, "overburdened is slower");
                inv.Clear();
                Assert.IsFalse(sv2.Overweight);
            }
            finally { Object.Destroy(rock); Object.Destroy(go2); }
        }

        // ------------------------------------------------------------------ environment
        [Test] public void Night_Air_Blends_Over_Dusk_And_Dawn()
        {
            Assert.AreEqual(0f, SurvivalEnvironment.NightBlend(12f, 6f, 19.5f, 2f), 1e-4f, "noon");
            Assert.AreEqual(1f, SurvivalEnvironment.NightBlend(0f, 6f, 19.5f, 2f), 1e-4f, "midnight");
            Assert.AreEqual(1f, SurvivalEnvironment.NightBlend(4.5f, 6f, 19.5f, 2f), 1e-4f, "before dawn");
            Assert.AreEqual(0.5f, SurvivalEnvironment.NightBlend(6f, 6f, 19.5f, 2f), 1e-4f, "sunrise: half way");
            Assert.AreEqual(0.5f, SurvivalEnvironment.NightBlend(19.5f, 6f, 19.5f, 2f), 1e-4f, "sunset: half way");
            Assert.AreEqual(0f, SurvivalEnvironment.NightBlend(18f, 6f, 19.5f, 2f), 1e-4f, "late afternoon");
            Assert.AreEqual(1f, SurvivalEnvironment.NightBlend(21f, 6f, 19.5f, 2f), 1e-4f, "night");
            float a = SurvivalEnvironment.NightBlend(19f, 6f, 19.5f, 2f), b = SurvivalEnvironment.NightBlend(20f, 6f, 19.5f, 2f);
            Assert.IsTrue(a > 0f && a < b && b < 1f, "gradual over dusk");
        }

        [Test] public void Shelter_Warmth_Uses_Its_Own_Value_And_Tent_Bed_Wins_At_Night()
        {
            var oldHour = Bedroll.Hour;
            var lean = new GameObject("TestLeanTo"); lean.transform.position = new Vector3(6000f, 0f, 6000f);
            var s1 = lean.AddComponent<Shelter>(); s1.warmth = 3f;
            var tent = new GameObject("TestTent"); tent.transform.position = new Vector3(6000f, 0f, 6000f);
            var s2 = tent.AddComponent<Shelter>(); s2.warmth = 6f;
            var bed = tent.AddComponent<Bedroll>();
            try
            {
                var inside = new Vector3(6000.5f, 0.5f, 6000f);
                Assert.AreEqual(6f, Shelter.WarmthAt(inside), 1e-4f, "the warmest covering shelter's own warmth");
                Assert.AreEqual(0f, Shelter.WarmthAt(new Vector3(6010f, 0f, 6000f)), 1e-4f, "open ground");
                Assert.GreaterOrEqual(SurvivalEnvironment.HeatAt(inside), 6f, "environment heat includes the shelter warmth");
                Assert.IsTrue(s2.HasBed, "tent = shelter + bedroll"); Assert.IsFalse(s1.HasBed);
                Bedroll.Hour = () => 20f;
                Assert.Greater(bed.Priority, s2.Priority, "at night the tent offers Sleep");
                Bedroll.Hour = () => 12f;
                Assert.Less(bed.Priority, s2.Priority, "by day the tent offers Rest");
                Assert.AreEqual(s2, Shelter.Nearest(inside, 5f, true), "nearest shelter with a bed");
            }
            finally { Bedroll.Hour = oldHour; Object.Destroy(lean); Object.Destroy(tent); }
        }

        // ------------------------------------------------------------------ water rules (drinking)
        [Test] public void Drinking_Clean_Helps_Salt_Hurts()
        {
            var sv = MakeSurvivor(out _, out var go);
            var gourd = MakeContainer(3);
            bool salty = false;
            Action<GameEvent> h = e => { if (e.type == GameEventType.TriedSaltWater) salty = true; };
            GameEvents.Raised += h;
            try
            {
                var st = new ItemStack(gourd, 1);
                Assert.AreEqual(3, WaterRules.Fill(st, WaterType.CleanWater));
                sv.SetStats(80f, 40f, 100f, 37f, 0f);
                Assert.IsTrue(WaterRules.Drink(sv, st));
                Assert.AreEqual(40f + Cfg.Water(WaterType.CleanWater).thirst, sv.Thirst, 1e-3f, "clean water per charge");
                Assert.AreEqual(2, st.water);
                Assert.IsFalse(WaterRules.CanFill(st, WaterType.SaltWater), "no mixing: clean + salt");
                WaterRules.Empty(st);
                Assert.AreEqual(WaterType.None, WaterRules.TypeOf(st));
                Assert.AreEqual(2, WaterRules.Fill(st, WaterType.SaltWater, 2));
                sv.SetStats(80f, 50f, 100f, 37f, 0f);
                Assert.IsTrue(WaterRules.Drink(sv, st));
                Assert.Less(sv.Thirst, 50f, "salt water makes thirst worse");
                Assert.IsTrue(salty, "TriedSaltWater raised");
            }
            finally { GameEvents.Raised -= h; Object.Destroy(gourd); Object.Destroy(go); }
        }

        // ------------------------------------------------------------------ tutorial ids
        [Test] public void Tutorial_Restores_By_Id_And_Maps_Old_Indexes()
        {
            var go = new GameObject("TestTutorial"); var tut = go.AddComponent<TutorialManager>();
            try
            {
                int cook = tut.IndexOf("cook");
                Assert.GreaterOrEqual(cook, 0);
                Assert.AreEqual(cook + 1, tut.IndexOf(TutorialManager.FillWaterStep), "milestone 1 steps follow the cook step");
                foreach (var id in new[] { TutorialManager.FillWaterStep, TutorialManager.BoilWaterStep, TutorialManager.DrinkCleanStep, TutorialManager.TentStep, TutorialManager.ShelterNightStep, TutorialManager.SleepStep })
                    Assert.GreaterOrEqual(tut.IndexOf(id), 0, "step " + id);
                for (int i = 0; i < TutorialManager.LegacyOrder.Length; i++)
                    Assert.GreaterOrEqual(tut.IndexOf(TutorialManager.LegacyOrder[i]), 0, "old step still exists: " + TutorialManager.LegacyOrder[i]);
                tut.Restore(0, false, TutorialManager.BoilWaterStep);
                Assert.AreEqual(TutorialManager.BoilWaterStep, tut.CurrentId, "restored by id");
                tut.Restore(12, false, null);
                Assert.AreEqual("spear", tut.CurrentId, "an old save's index maps through the old order");
                tut.Restore(3, false, "no_such_step");
                Assert.AreEqual("wood", tut.CurrentId, "unknown id falls back to the index");
            }
            finally { Object.Destroy(go); }
        }

        // ------------------------------------------------------------------ island: fill / drink / empty / save
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

        /// <summary>run an interaction (retries while the player is still busy) until done() or the timeout</summary>
        static IEnumerator Act(PlayerInteraction pi, Action start, Func<bool> done, float timeout = 8f)
        {
            float t = 0f;
            while (!done() && t < timeout)
            {
                if (!pi.InAction) start();
                yield return null; t += Time.deltaTime;
            }
        }

        static ItemStack GiveContainer(InventorySystem inv, out ItemDefinition gourd)
        {
            gourd = Db.Item("water_container");
            Assert.IsNotNull(gourd, "water_container item"); Assert.Greater(gourd.waterCharges, 1, "a container with room for more than one drink");
            inv.SetSlot(0, new ItemStack(gourd, 1)); inv.SetActiveSlot(0);
            return inv.Get(0);
        }

        [UnityTest] public IEnumerator Water_Fill_Types_Drink_And_Empty()
        {
            yield return LoadIsland();
            var p = _gm.Player; var pi = p.GetComponent<PlayerInteraction>(); var inv = p.GetComponent<InventorySystem>(); var sv = p.GetComponent<PlayerSurvival>();
            var st = GiveContainer(inv, out var gourd);

            // pond / stream -> unboiled (dirty) water
            var pond = WaterSource.All.First(w => w.fresh && !w.clean);
            Assert.AreEqual(WaterType.DirtyWater, pond.SourceType);
            yield return Act(pi, () => pond.Interact(pi), () => st.water > 0);
            Assert.AreEqual(WaterType.DirtyWater, WaterRules.TypeOf(st), "pond water is unboiled");
            Assert.AreEqual(gourd.waterCharges, st.water, "filled up");

            // Empty (inventory button)
            string emptiedId = null;
            Action<GameEvent> h = e => { if (e.type == GameEventType.WaterEmptied) emptiedId = e.id; };
            GameEvents.Raised += h;
            try
            {
                Assert.IsTrue(pi.EmptyContainer(0), "emptied");
                Assert.AreEqual(0, st.water); Assert.AreEqual(WaterType.None, WaterRules.TypeOf(st));
                Assert.AreEqual(Cfg.Water(WaterType.DirtyWater).eventId, emptiedId, "WaterEmptied names the water that was poured out");
                Assert.IsFalse(pi.EmptyContainer(0), "nothing left to pour");
            }
            finally { GameEvents.Raised -= h; }

            // sea with a container in hand -> salt water
            var ocean = Object.FindFirstObjectByType<OceanShore>(); Assert.IsNotNull(ocean);
            var spawn = _gm.spawnPoint.position; var terrain = Terrain.activeTerrain; float ty = terrain.transform.position.y;
            Vector3 shore = spawn; float best = float.MaxValue;
            for (float x = -150f; x <= 150f; x += 1.5f)
                for (float z = -150f; z <= 150f; z += 1.5f)
                {
                    var q = spawn + new Vector3(x, 0, z); float hgt = terrain.SampleHeight(q) + ty;
                    if (hgt > -0.02f || hgt < -0.3f) continue; float d2 = x * x + z * z;
                    if (d2 < best) { best = d2; shore = new Vector3(q.x, hgt, q.z); }
                }
            Assert.Less(best, float.MaxValue, "found shallow sea near the spawn");
            p.GetComponent<PlayerMotor>().Warp(shore + Vector3.up * 0.3f, p.transform.rotation);
            yield return new WaitForSeconds(0.8f);
            Assert.IsTrue(ocean.PlayerAtShore, "at the shore");
            Assert.AreEqual("Fill with salt water", ocean.GetPrompt(pi, out _), "a container in hand fills at the sea");
            yield return Act(pi, () => ocean.Interact(pi), () => st.water > 0);
            Assert.AreEqual(WaterType.SaltWater, WaterRules.TypeOf(st), "sea water is salt water");

            // drink salt from the container: allowed but harmful, with a warning
            string msg = ""; Action<string> mh = m => msg += m + "\n"; PlayerInteraction.Message += mh;
            try
            {
                sv.SetStats(80f, 50f, 100f, 37f, 0f);
                int before = st.water;
                yield return Act(pi, () => pi.UseActiveConsumable(), () => st.water < before);
                Assert.AreEqual(before - 1, st.water, "one drink");
                Assert.Less(sv.Thirst, 50f, "salt water makes thirst worse");
                StringAssert.Contains("Boil it first", msg, "salt water warning");
            }
            finally { PlayerInteraction.Message -= mh; }

            // no mixing: salt inside, the pond cannot top it up
            Assert.IsFalse(WaterRules.CanFill(st, WaterType.DirtyWater), "salt + unboiled do not mix");
            Assert.IsTrue(WaterRules.CanFill(st, WaterType.SaltWater), "same kind tops up");
            var prompt = pond.GetPrompt(pi, out var sub);
            Assert.IsFalse(prompt.StartsWith("Fill"), "the pond offers drinking, not filling: " + prompt);
            StringAssert.Contains("Empty", sub ?? "", "tells the player to empty it first");

            // clean water from the container helps
            WaterRules.Empty(st); WaterRules.Fill(st, WaterType.CleanWater, 1); inv.ForceNotify();
            sv.SetStats(80f, 40f, 100f, 37f, 0f);
            yield return Act(pi, () => pi.UseActiveConsumable(), () => st.water == 0);
            Assert.Greater(sv.Thirst, 40f + Cfg.Water(WaterType.CleanWater).thirst - 1f, "clean water quenches thirst");
        }

        [UnityTest] public IEnumerator Save_Load_Keeps_Water_Type_Sickness_And_Tutorial_Step()
        {
            yield return LoadIsland();
            var p = _gm.Player; var inv = p.GetComponent<InventorySystem>(); var sv = p.GetComponent<PlayerSurvival>(); var tut = TutorialManager.Instance;
            var st = GiveContainer(inv, out _);
            WaterRules.Fill(st, WaterType.SaltWater, 2); inv.ForceNotify();
            sv.MakeSick(50f);
            tut.Restore(0, false, TutorialManager.BoilWaterStep);
            Assert.AreEqual(TutorialManager.BoilWaterStep, tut.CurrentId);
            Assert.IsTrue(_gm.SaveGame(), "save: " + SaveSystem.LastError);

            inv.Clear(); sv.RestoreSickness(0f); tut.Begin(0);
            _gm.LoadGame(); yield return new WaitForSeconds(0.5f);

            inv = _gm.Player.GetComponent<InventorySystem>(); sv = _gm.Player.GetComponent<PlayerSurvival>();
            var back = inv.Get(0);
            Assert.IsNotNull(back, "container restored");
            Assert.AreEqual(WaterType.SaltWater, WaterRules.TypeOf(back), "water type restored");
            Assert.AreEqual(2, back.water, "charges restored");
            Assert.Greater(sv.SickSeconds, 40f, "sickness restored");
            Assert.AreEqual(TutorialManager.BoilWaterStep, TutorialManager.Instance.CurrentId, "tutorial step restored by id");
            SaveSystem.Delete();
        }
    }
}
