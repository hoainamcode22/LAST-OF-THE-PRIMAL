using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
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
    /// Survival phase 3 (agent SURV), no island: status effects (bleeding from hits, deep wounds, bandage, limb injuries),
    /// dirty-water poisoning with a fixed seed, food spoilage stages and ages, fish cooking, campfire states, rain
    /// putting out an open fire but not a sheltered one, save backup / corrupted fallback / partly invalid data, and the
    /// ISaveSection registry round trip. A fresh SurvivalConfig (code defaults) keeps the numbers predictable.
    /// </summary>
    public class SurvivalPhase3Tests
    {
        static ItemDatabase Db => ItemDatabase.Instance;
        SurvivalConfig _cfg;
        readonly List<Object> _made = new List<Object>();
        double _clock; string _folder;

        [UnitySetUp] public IEnumerator ClearScene() { yield return TestScenes.ClearIfGameplayLeft(); }

        [SetUp] public void SetUp()
        {
            _cfg = ScriptableObject.CreateInstance<SurvivalConfig>(); _cfg.name = "SurvivalConfig (phase 3 test)";
            if (Db != null) { _cfg.wood = Db.Item("wood"); _cfg.rawMeat = Db.Item("raw_meat"); _cfg.cookedMeat = Db.Item("cooked_meat"); _cfg.burntFood = Db.Item("burnt_meat"); }
            _made.Add(_cfg);
            SurvivalConfig.Override(_cfg);
            _clock = GameClock.Now;
            PlayerHealth.InjuryRoll = () => 0.99f;             // no limb injury unless a test asks for one
        }

        [TearDown] public void TearDown()
        {
            SurvivalConfig.Override(null);
            foreach (var o in _made) if (o) Object.Destroy(o);
            _made.Clear();
            GameClock.Now = _clock;
            PlayerHealth.InjuryRoll = null;
            Time.timeScale = 1f;
            if (_folder != null) { try { Directory.Delete(_folder, true); } catch { } SaveSystem.FolderOverride = null; _folder = null; }
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

        PlayerSurvival Survivor(out PlayerHealth hp, out PlayerStatusEffects fx, out InventorySystem inv)
        {
            var go = Keep(new GameObject("TestSurvivor"));
            go.transform.position = new Vector3(5000f, 0f, 5000f);
            inv = go.AddComponent<InventorySystem>(); inv.raiseGameEvents = false; inv.maxWeight = 0f; inv.EnsureSlots();
            var sv = go.AddComponent<PlayerSurvival>();
            hp = go.GetComponent<PlayerHealth>(); fx = go.GetComponent<PlayerStatusEffects>();
            Assert.IsNotNull(fx, "PlayerSurvival adds PlayerStatusEffects");
            return sv;
        }

        Campfire NewFire(Vector3 pos, bool prefab = false)
        {
            var item = prefab && Db != null ? Db.Item("campfire") : null;
            GameObject go = item && item.placePrefab ? BuildSystem.Spawn(item, pos, Quaternion.identity, null) : new GameObject("TestFire", typeof(Campfire));
            go.transform.position = pos;
            Keep(go);
            return go.GetComponent<Campfire>();
        }

        InventorySystem NewPack()
        {
            var go = Keep(new GameObject("TestPack"));
            var inv = go.AddComponent<InventorySystem>(); inv.raiseGameEvents = false; inv.maxWeight = 0f; inv.EnsureSlots();
            return inv;
        }

        static IEnumerator Until(Func<bool> done, float timeout)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!done() && Time.realtimeSinceStartup - t0 < timeout) yield return null;
        }

        // ------------------------------------------------------------------ bleeding + bandage
        [UnityTest, Timeout(60000)] public IEnumerator Bleeding_From_Hits_Deep_Wound_And_Bandage_Stops_It()
        {
            var sv = Survivor(out var hp, out var fx, out var inv);
            yield return null;
            int applied = 0, ended = 0; bool bleedEvent = false;
            Action<GameEvent> h = e => { if (e.id == StatusEffectIds.Bleeding) { if (e.type == GameEventType.StatusApplied) applied++; if (e.type == GameEventType.StatusEnded) ended++; } };
            hp.BleedingChanged += on => bleedEvent |= on;
            GameEvents.Raised += h;
            try
            {
                // a light cut (the attacker's bleedSeconds): bleeds that long, drains health, stops by itself
                hp.TakeDamage(8f, hp.transform.position + Vector3.forward * 2f, false, 3f);
                Assert.IsTrue(hp.IsBleeding, "a cut bleeds"); Assert.IsTrue(bleedEvent, "BleedingChanged(true) for the feedback");
                Assert.AreEqual(3f, fx.RemainingOf(StatusEffectIds.Bleeding), 0.05f, "light cut: the attacker's seconds");
                Assert.IsTrue(fx.BlocksRegen, "no natural regeneration while bleeding");
                float h0 = hp.Health; yield return new WaitForSeconds(1f);
                Assert.Less(hp.Health, h0 - 0.3f, "bleeding drains health");
                // a second cut while bleeding opens a deep wound (needs a bandage)
                hp.TakeDamage(8f, hp.transform.position + Vector3.forward * 2f, false, 3f);
                Assert.GreaterOrEqual(fx.RemainingOf(StatusEffectIds.Bleeding), _cfg.deepWoundSeconds - 1.5f, "repeat cut: deep wound");
                // one strong hit alone is a deep wound too
                fx.Remove(StatusEffectIds.Bleeding);
                hp.TakeDamage(_cfg.deepWoundDamage + 1f, hp.transform.position + Vector3.forward * 2f, false, 0f);
                Assert.IsTrue(hp.IsBleeding, "a hit of deepWoundDamage bleeds without attacker bleed seconds");
                Assert.GreaterOrEqual(fx.RemainingOf(StatusEffectIds.Bleeding), _cfg.deepWoundSeconds - 0.5f);
                // the bandage: cures bleeding, heals slowly (Recovering), used up, not eaten
                var bandage = MakeItem("t_bandage", i => { i.category = ItemCategory.Survival; i.cures = new[] { StatusEffectIds.Bleeding }; i.healOverTime = 10f; });
                Assert.IsTrue(SurvivalItemUse.HandlesMedical(bandage)); Assert.IsFalse(bandage.IsFood, "a bandage is not food");
                Assert.IsFalse(SurvivalItemUse.HandlesFood(bandage));
                inv.Add(bandage, 2);
                Assert.IsNull(SurvivalItemUse.WhyNot(hp, bandage), "useful while bleeding");
                bool ate = false; Action<GameEvent> eh = e => { if (e.type == GameEventType.Ate) ate = true; };
                GameEvents.Raised += eh;
                Assert.IsTrue(SurvivalItemUse.Treat(hp, sv, inv, 0, bandage, hp.transform));
                GameEvents.Raised -= eh;
                Assert.IsFalse(hp.IsBleeding, "the bandage stops the bleeding");
                Assert.IsFalse(ate, "no Ate event: the tutorial food step does not complete");
                Assert.AreEqual(1, inv.Count(bandage), "one bandage used");
                Assert.IsTrue(fx.Has(StatusEffectIds.Recovering), "health comes back slowly");
                float before = hp.Health; yield return new WaitForSeconds(1f);
                Assert.Greater(hp.Health, before, "recovering heals over time");
                Assert.Less(hp.Health, before + 10f, "never all at once");
                // the legacy bandage asset (no cures listed) still treats bleeding
                var legacy = MakeItem("bandage", i => { i.health = 25f; });
                Assert.IsTrue(SurvivalItemUse.HandlesMedical(legacy) && SurvivalItemUse.Cures(legacy, StatusEffectIds.Bleeding));
                Assert.GreaterOrEqual(applied, 1, "StatusApplied raised");
                Assert.GreaterOrEqual(ended, 1, "StatusEnded raised");
            }
            finally { GameEvents.Raised -= h; }
        }

        [Test] public void Heavy_Hits_Injure_A_Limb_Leg_Slows_Arm_Weakens()
        {
            var sv = Survivor(out var hp, out var fx, out _);
            hp.maxHealth = 1000f; hp.SetHealth(1000f);                 // many hits in a row
            Assert.AreEqual(1f, sv.MoveSpeedMultiplier, 1e-4f);
            float dmg = _cfg.injuryMinDamage;
            PlayerHealth.InjuryRoll = () => _cfg.injuryChance * _cfg.legInjuryShare * 0.5f;      // low roll: the leg
            hp.TakeDamage(dmg, hp.transform.position + Vector3.forward, true, 0f);
            Assert.IsTrue(fx.Has(StatusEffectIds.LegInjury), "leg injury from a heavy hit");
            Assert.Less(sv.MoveSpeedMultiplier, 0.9f, "leg injury: slower");
            Assert.Greater(fx.SprintCostMultiplier, 1f, "leg injury: running costs more");
            PlayerHealth.InjuryRoll = () => _cfg.injuryChance * 0.9f;                               // the arm part of the chance
            hp.TakeDamage(dmg, hp.transform.position + Vector3.forward, true, 0f);
            Assert.IsTrue(fx.Has(StatusEffectIds.ArmInjury), "arm injury");
            Assert.Less(fx.AttackMultiplier, 0.8f, "arm injury: weaker blows (combat reads AttackMultiplier)");
            fx.Clear();
            PlayerHealth.InjuryRoll = () => 0.99f;
            hp.TakeDamage(dmg, hp.transform.position + Vector3.forward, true, 0f);
            Assert.IsFalse(fx.Has(StatusEffectIds.LegInjury) || fx.Has(StatusEffectIds.ArmInjury), "a roll above the chance: no injury");
            PlayerHealth.InjuryRoll = () => 0f;
            hp.TakeDamage(dmg - 1f, hp.transform.position + Vector3.forward, true, 0f);
            Assert.IsFalse(fx.Has(StatusEffectIds.LegInjury) || fx.Has(StatusEffectIds.ArmInjury), "below injuryMinDamage: no injury");
            hp.TakeDamage(dmg, hp.transform.position + Vector3.forward, false, 0f);
            Assert.IsFalse(fx.Has(StatusEffectIds.LegInjury) || fx.Has(StatusEffectIds.ArmInjury), "not a heavy hit: no injury");
            hp.InvulnerableUntil = Time.time + 5f;
            hp.TakeDamage(dmg, hp.transform.position + Vector3.forward, true, 5f);
            Assert.AreEqual(0, fx.Count, "a dodged hit neither bleeds nor injures");
            // sleeping lets it heal; a hard landing hurts the leg
            hp.InvulnerableUntil = 0f;
            fx.Apply(StatusEffectIds.LegInjury, 1f, 120f);
            sv.ApplySleep(8f);
            Assert.IsFalse(fx.Has(StatusEffectIds.LegInjury), "a night's sleep heals it");
            hp.Landed(_cfg.fallInjurySpeed + 1f);
            Assert.IsTrue(fx.Has(StatusEffectIds.LegInjury), "hard landing: leg injury");
            hp.Revive(1f);
            Assert.AreEqual(0, fx.Count, "revive / respawn clears the effects");
        }

        // ------------------------------------------------------------------ water poisoning
        [UnityTest, Timeout(60000)] public IEnumerator Dirty_Water_Poisoning_Follows_Config_With_A_Fixed_Seed()
        {
            var sv = Survivor(out var hp, out var fx, out _);
            yield return null;
            int Drinks(int seed, int n)
            {
                UnityEngine.Random.InitState(seed); int sick = 0;
                for (int i = 0; i < n; i++)
                {
                    fx.Remove(StatusEffectIds.Sickness, false, false);
                    sv.SetStats(50f, 20f, 100f, 37f, 0f);
                    WaterRules.ApplyDrink(sv, WaterType.DirtyWater);
                    if (sv.IsSick) sick++;
                }
                return sick;
            }
            float chance = _cfg.Water(WaterType.DirtyWater).sickChance;
            int a = Drinks(4242, 200), b = Drinks(4242, 200);
            Debug.Log($"[Phase3] dirty water: {a} / 200 drinks made the survivor sick (sickChance {chance:P0}, seed 4242)");
            Assert.AreEqual(a, b, "the same seed gives the same result");
            Assert.That(a, Is.InRange(Mathf.FloorToInt(200 * chance * 0.5f), Mathf.CeilToInt(200 * chance * 1.6f)), "about sickChance of the drinks");
            // configurable: 0 never, 1 always
            for (int i = 0; i < _cfg.water.Length; i++) if (_cfg.water[i].type == WaterType.DirtyWater) _cfg.water[i].sickChance = 0f;
            Assert.AreEqual(0, Drinks(7, 50), "sickChance 0: never sick");
            for (int i = 0; i < _cfg.water.Length; i++) if (_cfg.water[i].type == WaterType.DirtyWater) { _cfg.water[i].sickChance = 1f; _cfg.water[i].sickSeconds = 40f; }
            Assert.AreEqual(20, Drinks(7, 20), "sickChance 1: always sick");
            Assert.AreEqual(40f, sv.SickSeconds, 0.05f, "for the configured seconds");
            // what food poisoning does: thirstier, less stamina, slow health loss, no regeneration
            Assert.Greater(fx.ThirstMultiplier, 1f); Assert.Less(fx.StaminaRegenMultiplier, 1f); Assert.Less(sv.MaxStaminaNow, sv.maxStamina);
            Assert.Less(fx.HealthPerSecond, 0f); Assert.IsTrue(fx.BlocksRegen);
            // clean water never makes you sick; drinking reports "+N Hydration"
            float restored = 0f; sv.NeedRestored += (n, v) => { if (n == PlayerSurvival.ThirstEventId) restored += v; };
            fx.Remove(StatusEffectIds.Sickness, false, false); sv.SetStats(50f, 20f, 100f, 37f, 0f);
            WaterRules.ApplyDrink(sv, WaterType.CleanWater);
            Assert.IsFalse(sv.IsSick); Assert.AreEqual(_cfg.Water(WaterType.CleanWater).thirst, restored, 0.01f, "+ Hydration feedback amount");
            Assert.Less(sv.Thirst, 100f, "never an instant 100 %");
            // ml shown for containers
            Assert.AreEqual(250 * 3, WaterRules.Ml(3)); Assert.AreEqual("500ml", WaterRules.MlShort(2));
        }

        // ------------------------------------------------------------------ spoilage
        [Test] public void Spoilage_Stages_Nutrition_Sickness_And_Stack_Ages()
        {
            var sv = Survivor(out var hp, out var fx, out var inv);
            GameClock.SecondsPerHour = 90f;
            var berry = MakeItem("t_berry", i => { i.category = ItemCategory.Food; i.hunger = 20f; i.spoilHours = 1f; });  // 90 s of game time
            GameClock.Now = 1000.0;
            inv.Add(berry, 4);
            var st = inv.Slots.First(s => s != null && s.item == berry);
            Assert.AreEqual(FoodStage.Fresh, Spoilage.Stage(st), "fresh when picked");
            GameClock.Now += 90 * (_cfg.agingAt + 0.05f);
            Assert.AreEqual(FoodStage.Aging, Spoilage.Stage(st), "aging past agingAt of the spoil time");
            GameClock.Now += 90;
            Assert.AreEqual(FoodStage.Spoiled, Spoilage.Stage(st), "spoiled after the spoil time");
            // nutrition by stage
            sv.SetStats(20f, 80f, 100f, 37f, 0f);
            _cfg.spoiledSickChance = 0f;
            sv.ConsumeItem(berry, FoodStage.Spoiled);
            Assert.AreEqual(20f + 20f * _cfg.spoiledNutrition, sv.Hunger, 0.01f, "spoiled food: poor nutrition");
            sv.SetStats(20f, 80f, 100f, 37f, 0f);
            sv.ConsumeItem(berry, FoodStage.Aging);
            Assert.AreEqual(20f + 20f * _cfg.agingNutrition, sv.Hunger, 0.01f, "aging food: a little less");
            _cfg.spoiledSickChance = 1f;
            sv.ConsumeItem(berry, st);
            Assert.IsTrue(sv.IsSick, "spoiled food: sickness chance (1 in this test)");
            Assert.AreEqual(_cfg.spoiledSickSeconds, sv.SickSeconds, 0.05f);
            // fresh food joining an old stack: count-weighted age (no per-item timers)
            double oldMade = st.madeAt;
            inv.Add(berry, 4);                                       // made now
            Assert.AreEqual((oldMade * 4 + GameClock.Now * 4) / 8, st.madeAt, 1e-3, "merged stack: average age");
            Assert.AreEqual(8, st.count);
            // RemoveOne gives the age (cooking keeps it), moves keep it
            Assert.IsTrue(inv.RemoveOne(berry, -1, out double made));
            Assert.AreEqual(st.madeAt, made, 1e-3);
            var copy = st.Clone(); Assert.AreEqual(st.madeAt, copy.madeAt, 1e-6, "clones keep the age");
            // cooked food keeps the part of the spoil time already used
            var cooked = MakeItem("t_cooked", i => { i.spoilHours = 4f; });
            GameClock.Now = 5000.0;
            double cm = Spoilage.CookedMadeAt(berry, 5000.0 - 45.0, cooked);                // raw half gone
            Assert.AreEqual(5000.0 - 0.5 * 4 * 90, cm, 0.01, "cooked result starts half through its own spoil time");
            // food that never spoils stays fresh
            var stone = MakeItem("t_stone"); var ss = new ItemStack(stone, 1) { madeAt = -1e6 };
            Assert.AreEqual(FoodStage.Fresh, Spoilage.Stage(ss));
        }

        [UnityTest, Timeout(60000)] public IEnumerator Spoilage_Check_Refreshes_The_Pack_Once_Per_Stage()
        {
            var sv = Survivor(out _, out _, out var inv);
            GameClock.SecondsPerHour = 90f;
            _cfg.spoilCheckSeconds = 0.5f;
            var meat = MakeItem("t_meat", i => { i.category = ItemCategory.Food; i.hunger = 10f; i.spoilHours = 1f; });
            inv.Add(meat, 2);
            int stages = 0, notified = 0; sv.FoodStageChanged += (it, stg) => stages++;
            inv.Changed += () => notified++;
            yield return new WaitForSeconds(0.7f);
            sv.CheckSpoilage(); int n0 = notified;
            GameClock.Now += 90 * 0.6;                               // to aging
            sv.CheckSpoilage();
            Assert.AreEqual(1, stages, "one stage change reported");
            Assert.AreEqual(n0 + 1, notified, "the pack refreshed once");
            sv.CheckSpoilage(); sv.CheckSpoilage();
            Assert.AreEqual(1, stages, "no repeat while nothing changed");
            Assert.AreEqual(n0 + 1, notified);
        }

        // ------------------------------------------------------------------ fish
        [UnityTest, Timeout(60000)] public IEnumerator Fish_Cooks_On_The_Fire_And_Keeps_Its_Age()
        {
            if (Db != null)
            {
                var rf = Db.Item("raw_fish"); var cf = Db.Item("cooked_fish");
                Assert.IsNotNull(rf, "raw_fish item (PrimalSurvivalBuilder)"); Assert.IsNotNull(cf, "cooked_fish item");
                Assert.AreSame(cf, rf.cookedResult, "raw fish cooks into cooked fish");
                Assert.IsNotNull(Db.Item("edible_plant"), "edible_plant item");
                Assert.IsNotNull(rf.icon, "raw_fish icon"); Assert.IsNotNull(cf.icon, "cooked_fish icon");
                Assert.Greater(rf.spoilHours, 0f, "raw fish spoils");
            }
            GameClock.SecondsPerHour = 90f;
            var cooked = MakeItem("t_cooked_fish", i => { i.category = ItemCategory.Food; i.hunger = 26f; i.spoilHours = 2f; i.burnSeconds = 30f; });
            var raw = MakeItem("t_raw_fish", i => { i.category = ItemCategory.Food; i.hunger = 10f; i.cookSeconds = 0.3f; i.cookedResult = cooked; i.spoilHours = 1f; });
            var fuel = MakeItem("t_fuel", i => i.fuelSeconds = 300f);
            var fire = NewFire(new Vector3(200, 0, 0)); var inv = NewPack();
            inv.Add(fuel, 1); Assert.IsTrue(fire.TryLight(inv));
            GameClock.Now = 10000.0;
            inv.Add(raw, 1, false, 10000.0 - 90 * 0.6);             // caught a while ago: aging
            Assert.IsTrue(fire.TryCook(inv, raw), "fish on the fire");
            yield return Until(() => fire.StateOf(0) == Campfire.CookState.Ready, 5f);
            Assert.AreEqual(Campfire.CookState.Ready, fire.StateOf(0), "cooked");
            Assert.AreSame(cooked, fire.ItemOn(0));
            Assert.IsTrue(fire.TryTake(inv));
            var st = inv.Slots.First(s => s != null && s.item == cooked);
            Assert.AreEqual(FoodStage.Aging, Spoilage.Stage(st), "cooked fish keeps the used-up part of its time");
        }

        // ------------------------------------------------------------------ fire states + rain
        [UnityTest, Timeout(60000)] public IEnumerator Fire_States_Lighting_Burning_LowFuel_Extinguished()
        {
            _cfg.lightingSeconds = 0.4f; _cfg.lowFuelSeconds = 6f; _cfg.fullIntensityFuelSeconds = 20f;
            var fire = NewFire(new Vector3(300, 0, 0)); var inv = NewPack();
            var fuel = MakeItem("t_fuel", i => i.fuelSeconds = 10f);
            Assert.AreEqual(Campfire.FireState.Unlit, fire.State, "a new fire pit is unlit");
            Assert.AreEqual(0f, fire.Intensity01);
            var states = new List<Campfire.FireState>(); fire.StateChanged += (a, b) => states.Add(b);
            inv.Add(fuel, 2); Assert.IsTrue(fire.TryLight(inv));
            Assert.AreEqual(Campfire.FireState.Lighting, fire.State, "Lighting first");
            Assert.IsTrue(fire.IsLit, "lit while lighting (cooking / warmth start)");
            float lighting = fire.Intensity01;
            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(Campfire.FireState.Burning, fire.State, "then Burning");
            Assert.Greater(fire.Intensity01, lighting, "the flames grew");
            float full = fire.Intensity01; float heatFull = Campfire.HeatAt(fire.transform.position + Vector3.right);
            _cfg.fuelBurnRate = 4f;
            yield return Until(() => fire.State == Campfire.FireState.LowFuel, 5f);
            Assert.AreEqual(Campfire.FireState.LowFuel, fire.State, "LowFuel below lowFuelSeconds");
            Assert.Less(fire.Intensity01, full, "weaker as fuel drops");
            Assert.Less(Campfire.HeatAt(fire.transform.position + Vector3.right), heatFull, "less heat");
            Assert.Less(fire.CookRate, 1f, "a weak fire cooks slower");
            Assert.IsTrue(fire.TryAddFuel(inv, fuel)); _cfg.fuelBurnRate = 0f;
            yield return null; yield return null;
            Assert.AreEqual(Campfire.FireState.Burning, fire.State, "fuel added: burning again");
            _cfg.fuelBurnRate = 10f;
            yield return Until(() => !fire.IsLit, 5f);
            Assert.AreEqual(Campfire.FireState.Extinguished, fire.State, "burned out: extinguished");
            Assert.AreEqual(0f, fire.Intensity01);
            CollectionAssert.IsSubsetOf(new[] { Campfire.FireState.Lighting, Campfire.FireState.Burning, Campfire.FireState.LowFuel, Campfire.FireState.Extinguished }, states);
            // the state is saved (a burned-out fire comes back extinguished, not as a new pit)
            string json = fire.CaptureState(); Assert.IsNotNull(json);
            var b2 = NewFire(new Vector3(310, 0, 0)); b2.Restore(false, 0f); b2.RestoreState(json);
            Assert.AreEqual(Campfire.FireState.Extinguished, b2.State);
        }

        [UnityTest, Timeout(60000)] public IEnumerator Heavy_Rain_Puts_Out_An_Open_Fire_Not_A_Sheltered_One()
        {
            _cfg.heavyRainExtinguishSeconds = 0.8f; _cfg.lightingSeconds = 0f;
            var wmGo = Keep(new GameObject("TestWeather")); var wm = wmGo.AddComponent<WeatherManager>(); wm.allowRandom = false;
            var inv = NewPack(); var fuel = MakeItem("t_fuel", i => i.fuelSeconds = 300f); inv.Add(fuel, 4);
            // light rain: weaker, burns faster, not put out
            wm.SetWeather(WeatherState.Rain, -1f, true);
            var open = NewFire(new Vector3(400, 0, 0), true);
            Assert.IsTrue(open.TryLight(inv));
            yield return new WaitForSeconds(1.5f);
            Assert.IsTrue(open.IsLit, "normal rain does not put the fire out");
            Assert.IsTrue(open.RainedOn, "rain falls on it");
            Assert.AreEqual(open.FuelIntensity01 * _cfg.rainFireIntensity, open.Intensity01, 1e-3f, "light rain lowers the intensity");
            // heavy rain (storm): the open fire goes out with a hiss, the sheltered one keeps burning
            var covered = NewFire(new Vector3(420, 0, 0), true);
            var sh = Keep(new GameObject("TestShelter")); sh.transform.position = covered.transform.position; sh.AddComponent<Shelter>();
            Assert.IsTrue(covered.TryLight(inv));
            bool doused = false; Action<GameEvent> h = e => { if (e.type == GameEventType.FireDoused) doused = true; };
            GameEvents.Raised += h;
            try
            {
                wm.SetWeather(WeatherState.Storm, -1f, true);
                yield return Until(() => !open.IsLit, 4f);
                Assert.IsFalse(open.IsLit, "the storm put out the open fire");
                Assert.AreEqual(Campfire.FireState.Extinguished, open.State);
                Assert.IsTrue(doused, "FireDoused raised (FireHiss played by CampfireFx)");
                Assert.IsTrue(covered.IsLit, "a sheltered fire is protected");
                Assert.IsTrue(covered.Sheltered); Assert.IsFalse(covered.RainedOn);
                Assert.AreEqual(covered.FuelIntensity01, covered.Intensity01, 1e-4f, "no rain penalty under the roof");
            }
            finally { GameEvents.Raised -= h; }
        }

        [UnityTest, Timeout(60000)] public IEnumerator Boiling_Plays_Bubbles_And_Loop_While_Water_Heats()
        {
            var fire = NewFire(new Vector3(500, 0, 0), true); var inv = NewPack();
            if (!fire.fx) Assert.Ignore("campfire prefab without CampfireFx in this project");
            for (int i = 0; i < _cfg.water.Length; i++) if (_cfg.water[i].type == WaterType.DirtyWater) _cfg.water[i].boilSeconds = 1.2f;
            var fuel = MakeItem("t_fuel", i => i.fuelSeconds = 200f); var cup = MakeItem("t_cup", i => { i.maxStack = 1; i.waterCharges = 2; });
            inv.Add(fuel, 1); Assert.IsTrue(fire.TryLight(inv));
            inv.Add(cup, 1); var st = inv.Slots.First(s => s != null && s.item == cup); WaterRules.Fill(st, WaterType.DirtyWater);
            int slot = Array.IndexOf(inv.Slots, st);
            Assert.IsTrue(fire.TryBoil(inv, slot));
            yield return null;
            Assert.IsTrue(fire.fx.IsBoiling, "bubbles + boil loop while the water heats");
            yield return Until(() => fire.StateOf(0) == Campfire.CookState.Ready, 5f);
            yield return null;
            Assert.IsFalse(fire.fx.IsBoiling, "stops once boiled");
        }

        // ------------------------------------------------------------------ wetness / sun / tool break
        [UnityTest, Timeout(60000)] public IEnumerator Water_Wets_Roof_And_Fire_Dry_Sun_Warms()
        {
            var depth = PlayerSurvival.WaterDepthAt; var sun = PlayerSurvival.SunAt; var roof = PlayerSurvival.ShelteredAt; var heat = PlayerSurvival.HeatAt; var rain = PlayerSurvival.RainingAt; var air = PlayerSurvival.AirTemperature;
            try
            {
                var sv = Survivor(out _, out var fx, out _);
                PlayerSurvival.RainingAt = q => false; PlayerSurvival.HeatAt = q => 0f; PlayerSurvival.AirTemperature = q => 22f;
                PlayerSurvival.SunAt = q => 0f; PlayerSurvival.ShelteredAt = q => false;
                PlayerSurvival.WaterDepthAt = q => 0.6f;                               // wading
                yield return new WaitForSeconds(1.5f);
                Assert.Greater(sv.WaterDepth, 0.5f); Assert.Greater(sv.Wetness, 0.2f, "standing in water soaks you quickly");
                Assert.Less(sv.EnvironmentTemperature, 22f - 1f, "water chills");
                yield return new WaitForSeconds(1f);
                Assert.IsTrue(fx.Has(StatusEffectIds.Wet), "Wet status (HUD icon)");
                // out of the water: open-air drying vs under a roof by a fire
                PlayerSurvival.WaterDepthAt = q => 0f;
                sv.SetStats(80f, 80f, 100f, 37f, 0.8f); yield return new WaitForSeconds(0.6f);
                float w0 = sv.Wetness; yield return new WaitForSeconds(1f); float openDry = w0 - sv.Wetness;
                PlayerSurvival.ShelteredAt = q => true; PlayerSurvival.HeatAt = q => 10f;
                sv.SetStats(80f, 80f, 100f, 37f, 0.8f); yield return new WaitForSeconds(0.6f);
                w0 = sv.Wetness; yield return new WaitForSeconds(1f); float campDry = w0 - sv.Wetness;
                Assert.Greater(openDry, 0f, "dries in the open");
                Assert.Greater(campDry, openDry * 3f, "dries much faster under a roof by a fire");
                Assert.IsTrue(sv.Sheltered, "roof seen");
                // sun: warmer in full sun than in the shade; a roof blocks it
                PlayerSurvival.ShelteredAt = q => false; PlayerSurvival.HeatAt = q => 0f;
                sv.SetStats(80f, 80f, 100f, 37f, 0f);
                PlayerSurvival.SunAt = q => 0f; yield return new WaitForSeconds(0.6f); float shade = sv.EnvironmentTemperature;
                PlayerSurvival.SunAt = q => 1f; yield return new WaitForSeconds(0.6f); float sunny = sv.EnvironmentTemperature;
                Assert.AreEqual(_cfg.sunWarmth, sunny - shade, 0.3f, "full sun adds sunWarmth");
                PlayerSurvival.ShelteredAt = q => true; yield return new WaitForSeconds(0.6f);
                Assert.AreEqual(0f, sv.SunExposure, "the roof gives shade");
            }
            finally { PlayerSurvival.WaterDepthAt = depth; PlayerSurvival.SunAt = sun; PlayerSurvival.ShelteredAt = roof; PlayerSurvival.HeatAt = heat; PlayerSurvival.RainingAt = rain; PlayerSurvival.AirTemperature = air; }
        }

        [Test] public void Tool_Break_In_The_Wear_Path_Is_Reported_Once()
        {
            var inv = NewPack(); inv.raiseGameEvents = true;
            var axe = MakeItem("t_axe", i => { i.maxStack = 1; i.maxDurability = 3f; i.tool = ToolKind.Chop; i.category = ItemCategory.Tool; });
            inv.Add(axe, 1); inv.SetActiveSlot(0);
            int broke = 0, events = 0; ItemDefinition what = null;
            Action<ItemDefinition, InventorySystem> onBroke = (it, i) => { broke++; what = it; };
            Action<GameEvent> h = e => { if (e.type == GameEventType.ToolBroken) events++; };
            InventorySystem.ToolBroke += onBroke; GameEvents.Raised += h;
            try
            {
                Assert.IsFalse(inv.WearActive(2f), "worn, not broken");
                Assert.AreEqual(0, broke);
                Assert.IsTrue(inv.WearActive(2f), "broke");
                Assert.AreEqual(1, broke, "ToolBroke once (HUD: ToolBreak sound, puff, note)"); Assert.AreSame(axe, what);
                Assert.AreEqual(1, events, "ToolBroken game event");
                Assert.IsNull(inv.Get(0), "the broken tool is gone");
                Assert.IsFalse(inv.WearActive(2f), "nothing left to wear");
                Assert.AreEqual(1, broke);
            }
            finally { InventorySystem.ToolBroke -= onBroke; GameEvents.Raised -= h; }
        }

        // ------------------------------------------------------------------ save
        [Test] public void Save_Keeps_A_Backup_And_Falls_Back_When_Damaged()
        {
            _folder = Path.Combine(Application.temporaryCachePath, "PF_S3Saves_" + UnityEngine.Random.Range(0, 99999));
            SaveSystem.FolderOverride = _folder;
            var a = new SaveData { day = 2, hour = 8f, health = 70f, hunger = 60f };
            var b = new SaveData { day = 3, hour = 9f, health = 55f, hunger = 40f };
            Assert.IsTrue(SaveSystem.WriteSafe(a)); Assert.IsFalse(File.Exists(SaveSystem.BackupPathFor(0)), "no backup before a second save");
            Assert.IsTrue(SaveSystem.WriteSafe(b));
            Assert.IsTrue(File.Exists(SaveSystem.BackupPathFor(0)), "the previous save is kept as the backup");
            Assert.IsFalse(File.Exists(SaveSystem.PathFor(0) + ".tmp"), "no temp file left");
            var r = SaveSystem.Read(); Assert.AreEqual(3, r.day); Assert.IsFalse(SaveSystem.LoadedFromBackup);
            // the main file gets damaged: the backup loads, nothing throws
            File.WriteAllText(SaveSystem.PathFor(0), "{ \"version\": 5, \"day\": ");
            r = SaveSystem.Read();
            Assert.IsNotNull(r, "fell back to the backup"); Assert.AreEqual(2, r.day); Assert.IsTrue(SaveSystem.LoadedFromBackup);
            StringAssert.Contains("backup", SaveSystem.LastError);
            // a damaged main save is never copied over the good backup
            Assert.IsTrue(SaveSystem.WriteSafe(b));
            Assert.AreEqual(2, SaveSystem.Parse(File.ReadAllText(SaveSystem.BackupPathFor(0)), out _).day, "the good backup survived");
            // both damaged: null with a reason, no exception
            File.WriteAllText(SaveSystem.PathFor(0), "garbage"); File.WriteAllText(SaveSystem.BackupPathFor(0), "");
            Assert.IsNull(SaveSystem.Read()); Assert.IsNotEmpty(SaveSystem.LastError);
            // newer version refused, no version refused
            Assert.IsNull(SaveSystem.Parse("{\"version\": 999}", out var why)); StringAssert.Contains("newer", why);
            Assert.IsNull(SaveSystem.Parse("{\"day\": 3}", out _), "not a save file (no version key)");
            // partly invalid data is repaired, not a crash
            var d = SaveSystem.Parse("{\"version\":5,\"day\":0,\"hour\":30,\"inventory\":[null,{\"slot\":0,\"item\":\"\",\"count\":1},{\"slot\":1,\"item\":\"wood\",\"count\":2}],\"structures\":[null,{\"item\":\"campfire\"}],\"sections\":[null,{\"key\":\"\"},{\"key\":\"x\",\"json\":\"{}\"}]}", out why);
            Assert.IsNotNull(d, why);
            Assert.AreEqual(1, d.day, "day at least 1"); Assert.Less(d.hour, 24f);
            Assert.AreEqual(1, d.inventory.Count, "null / id-less slots dropped"); Assert.AreEqual(1, d.structures.Count); Assert.AreEqual(1, d.sections.Count);
            SaveSystem.Delete();
            Assert.IsFalse(SaveSystem.Exists(), "Delete removes the save and its backup");
        }

        class TestSection : ISaveSection
        {
            public string key; public string json; public string restored; public bool throwOnCapture, throwOnRestore; public int restores;
            public string SectionKey => key;
            public string CaptureSection() { if (throwOnCapture) throw new InvalidOperationException("capture boom"); return json; }
            public void RestoreSection(string j) { restores++; if (throwOnRestore) throw new InvalidOperationException("restore boom"); restored = j; }
        }

        [Test] public void ISaveSection_Round_Trip_Missing_And_Broken_Sections_Are_Skipped()
        {
            _folder = Path.Combine(Application.temporaryCachePath, "PF_S3Sections_" + UnityEngine.Random.Range(0, 99999));
            SaveSystem.FolderOverride = _folder;
            var good = new TestSection { key = "t_good", json = "{\"n\":3,\"s\":\"hi \\\"there\\\"\"}" };
            var badCapture = new TestSection { key = "t_bad_capture", json = "{}", throwOnCapture = true };
            var badRestore = new TestSection { key = "t_bad_restore", json = "{\"x\":1}", throwOnRestore = true };
            var nothing = new TestSection { key = "t_nothing", json = null };
            var all = new[] { good, badCapture, badRestore, nothing };
            try
            {
                foreach (var s in all) SaveSystem.RegisterSection(s);
                var d = new SaveData();
                SaveSystem.CaptureSections(d);
                var keys = d.sections.Select(x => x.key).ToList();
                Assert.IsTrue(keys.Contains("t_good") && keys.Contains("t_bad_restore"), "captured: " + string.Join(",", keys));
                Assert.IsFalse(keys.Contains("t_bad_capture") || keys.Contains("t_nothing"), "a throwing capture and a null capture are left out");
                // through the file
                Assert.IsTrue(SaveSystem.WriteSafe(d));
                var back = SaveSystem.Read(); Assert.IsNotNull(back);
                back.sections.Add(new SectionData { key = "t_unregistered", json = "{}" });   // a system that is gone: ignored
                var late = new TestSection { key = "t_missing" }; SaveSystem.RegisterSection(late);
                int n = SaveSystem.RestoreSections(back);
                Assert.AreEqual(good.json, good.restored, "the JSON comes back exactly (quotes included)");
                Assert.GreaterOrEqual(n, 1, "the good section counted as restored");
                Assert.AreEqual(1, badRestore.restores, "a throwing restore was tried and skipped, the rest still loaded");
                Assert.AreEqual(0, late.restores, "a section missing from the file is not restored");
                Assert.IsTrue(SaveSystem.TryGetLoadedSection("t_unregistered", out _), "late registrants can still find their blob");
                // same key again: the newer registration replaces the old one
                var again = new TestSection { key = "t_good", json = "{}" }; SaveSystem.RegisterSection(again);
                Assert.AreEqual(1, SaveSystem.Sections.Count(s => s.SectionKey == "t_good"));
                SaveSystem.UnregisterSection(again); SaveSystem.UnregisterSection(late);
            }
            finally { foreach (var s in all) SaveSystem.UnregisterSection(s); }
        }

        [Test] public void Status_Effects_Save_Section_Round_Trip()
        {
            var sv = Survivor(out var hp, out var fx, out _);
            fx.Apply(StatusEffectIds.Bleeding, 1f, 42f, false);
            fx.Apply(StatusEffectIds.LegInjury, 1f, 100f, false);
            fx.Apply(StatusEffectIds.Wet, 1f, 0f, false);               // derived: not saved
            string json = fx.CaptureSection();
            fx.Clear();
            fx.RestoreSection(json);
            Assert.AreEqual(42f, fx.RemainingOf(StatusEffectIds.Bleeding), 0.01f);
            Assert.AreEqual(100f, fx.RemainingOf(StatusEffectIds.LegInjury), 0.01f);
            Assert.IsFalse(fx.Has(StatusEffectIds.Wet), "wet / cold follow the stats, they are not saved");
            fx.RestoreSection("{\"effects\":[{\"id\":\"no_such_effect\",\"remaining\":5},{\"id\":\"sickness\",\"remaining\":\"NaN\"}]}");
            Assert.IsFalse(fx.Has("no_such_effect"), "unknown ids skipped");
            Assert.AreEqual(PlayerStatusEffects.SaveKey, fx.SectionKey);
            fx.RestoreSection("{broken");                               // unreadable: skipped with a warning, no exception
        }
    }
}
