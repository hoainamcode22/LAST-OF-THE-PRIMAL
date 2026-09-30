using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Story;
using static PrimalFrontier.Story.MissionCondition;
using Object = UnityEngine.Object;

namespace PrimalFrontier.Tests
{
    /// <summary>
    /// Story (agent STORY), no island: id rules (legacy zone names, prop ids with a number), mission rules (start after
    /// requirements, events with ids / counts / distinct ids, locations visited, chapter end, a rule on an event this build
    /// lacks never starts, a start trigger), the missions save section round trip, the death share (item in hand kept,
    /// half of each stack, every other single item), and the journal (old pages move to the new sections, events unlock
    /// location / prop / resource pages, a creature page is written from observations).
    /// </summary>
    public class StoryTests
    {
        readonly List<Object> _made = new List<Object>();

        [UnitySetUp] public IEnumerator ClearScene() { yield return TestScenes.ClearIfGameplayLeft(); }
        [TearDown] public void Cleanup() { foreach (var o in _made) if (o) Object.DestroyImmediate(o); _made.Clear(); }

        T Make<T>(string name) where T : Component { var go = new GameObject(name); _made.Add(go); return go.AddComponent<T>(); }
        static void Raise(GameEventType t, string id, int amount = 1) => GameEvents.Raise(t, id, amount);

        [Test] public void Ids_Normalise_Locations_Discoveries_And_Patterns()
        {
            Assert.AreEqual("beach", StoryIds.Location("ZONE_StartBeach"));
            Assert.AreEqual("shipwreck", StoryIds.Location("ZONE_Shipwreck"));
            Assert.AreEqual("deep_forest", StoryIds.Location("DeepForest"));
            Assert.AreEqual("waterfall", StoryIds.Location("waterfall"));
            Assert.AreEqual("hab_triceratops", StoryIds.Location("HAB_Triceratops"));
            Assert.AreEqual("claw_marks", StoryIds.Discovery("claw_marks_2"));
            Assert.AreEqual("claw_marks", StoryIds.Discovery("Claw_Marks"));
            Assert.AreEqual("claw_marks", StoryIds.Discovery("env_claw_marks"), "ENV's prop ids");
            Assert.AreEqual("old_firepit", StoryIds.Discovery("env_old_camp_firering"));
            Assert.AreEqual("strange_markings", StoryIds.Discovery("env_markings_ridge"));
            Assert.IsNotNull(StoryTexts.Discovery("env_giant_skeleton"), "every ENV prop id has a text");
            Assert.IsTrue(StoryIds.Matches("claw_marks", "footprint*|claw*"));
            Assert.IsFalse(StoryIds.Matches("bones", "footprint*|claw*"));
            Assert.IsTrue(StoryIds.Matches("herd:triceratops", "herd:*"));
            Assert.IsTrue(StoryIds.LocationMatches("ZONE_Cave", "cave|nest"));
            Assert.IsTrue(StoryIds.Events("Drank|NoSuchEventName").Count == 1, "an unknown event name is ignored");
        }

        static MissionDefinition Def(string id, int chapter, int order, bool main, params MissionCondition[] complete)
        {
            var m = ScriptableObject.CreateInstance<MissionDefinition>();
            m.id = id; m.chapter = chapter; m.order = order; m.main = main; m.title = id; m.objective = "do " + id; m.complete = complete.ToList();
            return m;
        }

        List<MissionDefinition> TestMissions()
        {
            var a = Def("a", 1, 10, true, Ev("Drank", "dirty_water|clean_water"));
            var b = Def("b", 1, 20, true, Ev("Discovery|FootprintFound", "footprint*|claw_marks*", 2, true)); b.requires.Add("a"); b.endsChapter = true;
            var c = Def("c", 2, 30, true, Loc("waterfall")); c.requires.Add("b");
            var d = Def("d", 1, 40, false, Ev("NoSuchEventName")); d.requires.Add("a");
            var e = Def("e", 1, 50, false, Ev("ItemUsed", "bandage")); e.startWhen.Add(Ev("StatusApplied", "bleeding"));
            var l = new List<MissionDefinition> { a, b, c, d, e };
            _made.AddRange(l);
            return l;
        }

        [Test] public void Mission_Rules_Start_Complete_Chapter_And_Save_Round_Trip()
        {
            var ms = Make<MissionSystem>("TestMissions");
            ms.SetDefinitions(TestMissions());
            ms.chapterCardDelay = 0f;
            ms.Begin();
            Assert.AreEqual(1, ms.Chapter);
            Assert.IsTrue(ms.IsActive("a")); Assert.IsFalse(ms.IsActive("b"), "b waits for a");
            Assert.AreEqual("a", ms.Focused.def.id);
            Raise(GameEventType.Drank, "salt_water"); ms.EvaluateNow();
            Assert.IsTrue(ms.IsActive("a"), "sea water does not count");
            Raise(GameEventType.Drank, "dirty_water"); ms.EvaluateNow();
            Assert.IsTrue(ms.IsDone("a")); Assert.IsTrue(ms.IsActive("b")); Assert.AreEqual("b", ms.Focused.def.id);
            Assert.IsFalse(ms.IsActive("d"), "a mission waiting only for an event this build lacks never starts");
            Assert.IsFalse(ms.IsActive("e"), "e waits for its start trigger");
            Raise(GameEventType.StatusApplied, "bleeding"); ms.EvaluateNow();
            Assert.IsTrue(ms.IsActive("e"));
            Assert.AreEqual("b", ms.Focused.def.id, "the main mission stays the objective");
            Raise(GameEventType.Discovery, "claw_marks"); Raise(GameEventType.Discovery, "claw_marks_2"); ms.EvaluateNow();
            Assert.IsTrue(ms.IsActive("b"), "the same sign twice counts once");
            Assert.AreEqual(1, ms.Get("b").counts[0]);
            string json = ms.CaptureSection();
            Raise(GameEventType.FootprintFound, "footprint"); ms.EvaluateNow();
            Assert.IsTrue(ms.IsDone("b")); Assert.AreEqual(2, ms.Chapter, "b ends chapter one");
            Assert.IsTrue(ms.IsActive("c"));
            Raise(GameEventType.ZoneEntered, "ZONE_Waterfall"); ms.EvaluateNow();
            Assert.IsTrue(ms.IsDone("c"), "a legacy zone name counts as the location");
            Raise(GameEventType.ItemUsed, "bandage"); ms.EvaluateNow();
            Assert.IsTrue(ms.IsDone("e"));

            // the save taken before the prints: restores chapter one with b half done
            var ms2 = Make<MissionSystem>("TestMissions2");
            ms2.SetDefinitions(TestMissions());
            ms2.RestoreSection(json);
            Assert.AreEqual(1, ms2.Chapter);
            Assert.IsTrue(ms2.IsDone("a")); Assert.IsTrue(ms2.IsActive("b")); Assert.IsTrue(ms2.IsActive("e"));
            Assert.AreEqual(1, ms2.Get("b").counts[0]);
            Assert.IsTrue(ms2.Get("b").seen[0].Contains("claw_marks"), "distinct ids restored");
        }

        [Test] public void Built_In_Catalog_Has_The_Twelve_Main_Missions_In_Four_Chapters()
        {
            var all = MissionCatalog.Build(); _made.AddRange(all);
            var ids = new[] { "wake", "find_water", "make_fire", "find_food", "build_shelter", "first_night", "follow_river", "find_waterfall", "observe_herd", "investigate_tracks", "old_camp", "volcanic_ridge" };
            foreach (var id in ids) Assert.IsTrue(all.Any(m => m.id == id && m.main), "main mission " + id);
            Assert.AreEqual(4, all.Where(m => m.main).Select(m => m.chapter).Distinct().Count());
            Assert.AreEqual(all.Count, all.Select(m => m.id).Distinct().Count(), "unique ids");
            foreach (var m in all) foreach (var r in m.requires) Assert.IsTrue(all.Any(x => x.id == r), m.id + " requires a known mission " + r);
            Assert.AreEqual("Find fresh water before sunset", all.First(m => m.id == "find_water").objective);
            Assert.AreEqual("Follow the river to its source", all.First(m => m.id == "follow_river").objective);
        }

        [Test] public void Death_Share_Keeps_The_Item_In_Hand_And_Leaves_Half()
        {
            ItemDefinition Item(string id, int stack, ItemCategory cat = ItemCategory.Resource)
            { var it = ScriptableObject.CreateInstance<ItemDefinition>(); it.id = id; it.displayName = id; it.maxStack = stack; it.category = cat; it.weight = 0.1f; _made.Add(it); return it; }
            var inv = Make<InventorySystem>("TestInv"); inv.slotCount = 16; inv.hotbarSize = 4; inv.maxWeight = 0f; inv.raiseGameEvents = false; inv.EnsureSlots();
            var spear = Item("spear", 1, ItemCategory.Weapon); var wood = Item("wood", 20); var stone = Item("stone", 20); var cupA = Item("cup", 1); var cupB = Item("cup2", 1);
            inv.Slots[0] = new ItemStack(spear, 1); inv.Slots[5] = new ItemStack(wood, 9); inv.Slots[6] = new ItemStack(stone, 1);
            inv.Slots[7] = new ItemStack(cupA, 1); inv.Slots[8] = new ItemStack(cupB, 1);
            inv.SetActiveSlot(0);
            var taken = DeathSystem.TakeShare(inv, 0.5f, true, false);
            Assert.IsNotNull(inv.Get(0), "the spear in hand stays");
            Assert.AreEqual(4, inv.Get(5).count, "wood: 9 -> 5 left in the bundle (rounded up), 4 kept");
            Assert.AreEqual(5, taken.First(s => s.item == wood).count);
            int singlesTaken = taken.Count(s => s.item == stone || s.item == cupA || s.item == cupB);
            Assert.AreEqual(1, singlesTaken, "every other single item: 1 of 3");
        }

        [Test] public void Journal_Moves_Old_Pages_Unlocks_New_Sections_And_Writes_Creature_Pages()
        {
            var go = new GameObject("TestJournal"); _made.Add(go); go.SetActive(false);
            var j = go.AddComponent<JournalSystem>();
            j.entries.Add(new JournalSystem.Entry { id = "wreck", title = "The Wreck", category = JournalCategory.Survival, trigger = GameEventType.ZoneEntered, triggerId = "ZONE_Shipwreck" });
            j.entries.Add(new JournalSystem.Entry { id = "beach", title = "The Beach", category = (JournalCategory)3, trigger = GameEventType.ZoneEntered, triggerId = "ZONE_StartBeach" });
            j.entries.Add(new JournalSystem.Entry { id = "footprint", title = "Giant Footprints", category = (JournalCategory)3, trigger = GameEventType.FootprintFound });
            j.entries.Add(new JournalSystem.Entry { id = "fruit", title = "Fruit", category = JournalCategory.Survival, trigger = GameEventType.FruitHarvested });
            j.entries.Add(new JournalSystem.Entry { id = "triceratops", title = "Three-Horned Grazer", category = JournalCategory.Creatures, trigger = GameEventType.CreatureSighted, triggerId = "triceratops",
                text = "A beast the size of a hut, with three horns and a great bony frill. It grazes calmly and watches me with one eye. I think it will leave me alone if I leave it alone." });
            go.SetActive(true);
            Assert.AreEqual(JournalCategory.Locations, j.Get("wreck").category);
            Assert.AreEqual(JournalCategory.Locations, j.Get("beach").category);
            Assert.AreEqual(JournalCategory.Discoveries, j.Get("footprint").category);
            Assert.AreEqual(JournalCategory.Resources, j.Get("fruit").category);
            Assert.AreEqual("triceratops", j.Get("triceratops").creatureId);
            Assert.IsFalse(j.Get("triceratops").text.Contains("leave me alone"), "the old text told behaviour before it was seen");
            foreach (var c in JournalSystem.Sections) Assert.Greater(j.entries.Count(e => e.category == c), 0, "pages in " + c);

            Raise(GameEventType.ZoneEntered, "beach");
            Raise(GameEventType.Discovery, "claw_marks_3");
            Raise(GameEventType.ItemAdded, "wood", 2);
            Raise(GameEventType.Discovery, "captains_log");
            Assert.IsTrue(j.IsUnlocked("beach"), "LOCATIONS id matches the legacy zone name");
            Assert.IsTrue(j.IsUnlocked("disc_claw_marks"), "a numbered prop id opens its page");
            Assert.IsTrue(j.IsUnlocked("res_wood"));
            Assert.IsFalse(j.IsUnlocked("footprint"), "another prop's event does not open the prints page");
            string page = j.PageText(j.Get("triceratops"));
            StringAssert.Contains("Diet:", page); StringAssert.Contains("I have not seen it eat", page); StringAssert.Contains("Threat:", page);
        }
    }
}
