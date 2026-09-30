using System.Collections.Generic;
using UnityEngine;
using static PrimalFrontier.Story.MissionCondition;

namespace PrimalFrontier.Story
{
    /// <summary>
    /// The built-in chapters and missions (directive 57-60). Used when neither the scene list on MissionSystem nor
    /// Resources/Story/Missions holds any; PrimalStoryBuilder writes these as editable assets. Goals are things to do in
    /// the world ("Find fresh water before sunset"), never "collect 10 stones". The final mystery stays open.
    /// </summary>
    public static class MissionCatalog
    {
        public static readonly string[] ChapterNames = { "THE SHORE", "THE RIVER", "THE TRACKS", "THE WILDERNESS" };
        public static readonly string[] ChapterNumbers = { "CHAPTER ONE", "CHAPTER TWO", "CHAPTER THREE", "CHAPTER FOUR" };
        public static string ChapterName(int chapter) => chapter >= 1 && chapter <= ChapterNames.Length ? ChapterNames[chapter - 1] : "";
        public static string ChapterNumber(int chapter) => chapter >= 1 && chapter <= ChapterNumbers.Length ? ChapterNumbers[chapter - 1] : "";

        /// <summary>weapon item ids that count as "armed"</summary>
        public const string Weapons = "stone_spear|bow|hunting_bow|flint_sword|flint_knife|butcher_knife";

        public static List<MissionDefinition> Build()
        {
            var l = new List<MissionDefinition>();
            MissionDefinition M(string id, int chapter, int order, bool main, string title, string objective, params MissionCondition[] complete)
            {
                var m = ScriptableObject.CreateInstance<MissionDefinition>();
                m.name = "MIS_" + id; m.id = id; m.chapter = chapter; m.order = order; m.main = main; m.title = title; m.objective = objective;
                m.complete = new List<MissionCondition>(complete);
                l.Add(m); return m;
            }

            // ================================================================ 1 THE SHORE (day 0 / 1)
            var wake = M("wake", 1, 10, true, "Wake", "Search the wreck for anything useful",
                Ev("LootOpened"), Ev("ItemAdded"), Loc("shipwreck"));
            wake.clue = "The wreck lies along the shore.";

            var water = M("find_water", 1, 20, true, "Find Water", "Find fresh water before sunset",
                Ev("Drank|WaterFilled", "dirty_water|clean_water"), Chk("near_fresh_water", radius: 3f));
            water.requires.Add("wake"); water.lateObjective = "Find fresh water";
            water.clue = "The sea is no use. Streams run down from the high ground.";
            water.deadline = MissionDeadline.Sunset; water.lateLine = "Night is coming, and I still have no water.";
            water.marker = MissionMarker.NearestFreshWater; water.markerAfterHours = 4f;

            var fire = M("make_fire", 1, 30, true, "Make Fire", "Build a fire and get it burning before dark", Ev("FireLit"));
            fire.requires.Add("wake"); fire.lateObjective = "Get a fire burning";
            fire.deadline = MissionDeadline.Nightfall;

            var food = M("find_food", 1, 40, true, "Find Food", "Find something to eat", Ev("Ate"));
            food.requires.Add("wake");
            food.clue = "Berry bushes grow where the forest meets the sand.";

            var shelter = M("build_shelter", 1, 50, true, "Build Shelter", "Build a shelter before nightfall", Chk("shelter_exists"));
            shelter.requires.Add("wake"); shelter.lateObjective = "Build a shelter";
            shelter.startWhen.Add(Hours(13f, 24f)); shelter.startWhen.Add(Done("make_fire"));
            shelter.deadline = MissionDeadline.Nightfall;

            var night = M("first_night", 1, 60, true, "Survive the First Night", "Survive until dawn", Ev("DayStarted"), Ev("Slept"));
            night.requires.Add("wake"); night.endsChapter = true;
            night.startWhen.Add(Chk("dusk"));
            night.clue = "Stay near the fire. Keep it fed.";
            night.marker = MissionMarker.Camp; night.startCue = "predator_roar";
            night.doneLine = "I made it through the night.";

            var boil = M("opt_boil", 1, 45, false, "Clean Water", "Boil water at the fire to make it safe", Ev("WaterBoiled"));
            boil.requires.Add("find_water"); boil.requires.Add("make_fire");

            var bandage = M("opt_bandage", 1, 46, false, "Tend the Wound", "Stop the bleeding with a bandage", Ev("ItemUsed", "bandage"));
            bandage.requires.Add("wake"); bandage.startWhen.Add(Ev("StatusApplied", "bleeding"));

            var torch = M("opt_torch", 1, 62, false, "Light in the Dark", "Carry a torch for the night", Chk("has_item", "torch"));
            torch.requires.Add("make_fire"); torch.startWhen.Add(Chk("dusk"));

            var fuel = M("opt_firewood", 1, 63, false, "Keep the Fire Fed", "Gather dry wood and keep the fire burning", Ev("FuelAdded", null, 2));
            fuel.requires.Add("make_fire"); fuel.startWhen.Add(Chk("dusk"));
            fuel.clue = "Rain and wind eat the fire. Keep wood close.";

            var spear = M("opt_weapon", 1, 65, false, "A Weapon", "Make a weapon before going inland", Ev("ItemCrafted", Weapons), Chk("has_item", Weapons));
            spear.requires.Add("wake");
            spear.startWhen.Add(Ev("CreatureSighted")); spear.startWhen.Add(Ev("PredatorWarning")); spear.startWhen.Add(Done("first_night"));

            // ================================================================ 2 THE RIVER (day 2)
            var river = M("follow_river", 2, 70, true, "Follow the River", "Follow the river to its source",
                Near("waterfall", 110f), Loc("waterfall"));
            river.requires.Add("first_night");
            river.clue = "The stream came down from somewhere in the hills. Follow it upstream.";
            river.doneLine = "Falling water. Close now.";

            var falls = M("find_waterfall", 2, 80, true, "Find the Waterfall", "Find where the river falls", Loc("waterfall"));
            falls.requires.Add("follow_river");

            var herd = M("observe_herd", 2, 90, true, "Observe the Herd", "Watch a herd from a safe distance",
                Ev("HerdSighted"), Ev("CreatureObserved", "herd:*"), Ev("MigrationSeen"));
            herd.requires.Add("find_waterfall"); herd.endsChapter = true;
            herd.clue = "Wide trails through the ferns lead down to the valley.";

            var migration = M("opt_migration", 2, 95, false, "The Migration", "Watch the great herd cross the valley from high ground", Ev("MigrationSeen"));
            migration.requires.Add("find_waterfall");
            migration.clue = "From the high rocks above the valley you would see the whole herd move.";

            var source = M("opt_source", 2, 96, false, "The Source", "Climb above the waterfall to where the river begins", Ev("Discovery", "spring*"), Loc("ridge"));
            source.requires.Add("find_waterfall");
            source.clue = "The water falls from somewhere higher still.";

            var hunt = M("opt_hunt", 2, 100, false, "The Hunt", "Hunt an animal for meat and hide", Ev("CarcassButchered"));
            hunt.requires.Add("first_night");

            var fish = M("opt_fish", 2, 105, false, "River Fish", "Catch a fish in the shallows", Ev("ItemAdded", "raw_fish"));
            fish.requires.Add("follow_river");

            var back = M("opt_return", 2, 110, false, "Back Before Dark", "Return to camp before darkness", Chk("at_camp", radius: 15f));
            back.requires.Add("first_night"); back.startMode = MatchMode.All;
            back.startWhen.Add(Chk("far_from_camp", radius: 150f)); back.startWhen.Add(Hours(16.5f, 20f));
            back.deadline = MissionDeadline.Nightfall; back.lateObjective = "Get back to camp";
            back.marker = MissionMarker.Camp;

            // ================================================================ 3 THE TRACKS (day 3)
            var tracks = M("investigate_tracks", 3, 120, true, "Investigate Predator Tracks", "Find out what made the tracks",
                Ev("TracksFound"),
                Ev("FootprintFound|Discovery", "footprint*|claw_marks*|kill_site*|blood_trail*|broken_trees*|bones*|giant_skeleton*|theropod_trail*", 2, true),
                Loc("predator_territory"));
            tracks.requires.Add("observe_herd");
            tracks.clue = "The prints lead away from the river, towards the broken trees.";

            var camp = M("old_camp", 3, 130, true, "Discover the Old Camp", "Look for signs of other people",
                Loc("old_camp"), Ev("Discovery", "old_shelter*|old_firepit*|old_tools*|old_camp_tally*"));
            camp.requires.Add("investigate_tracks"); camp.endsChapter = true;
            camp.clue = "Cut marks on a branch, a knot in old cord. Someone else was here.";

            var prep = M("opt_prepare", 3, 125, false, "Prepare the Journey", "Pack food, water and a weapon before going deeper", Chk("prepared"));
            prep.requires.Add("observe_herd");

            var notes = M("opt_field_notes", 3, 140, false, "Field Notes", "Study five different creatures", Chk("creatures_known", count: 5));
            notes.requires.Add("find_waterfall");

            // ================================================================ 4 THE WILDERNESS
            var ridge = M("volcanic_ridge", 4, 150, true, "Reach the Volcanic Ridge", "Climb to the smoking ridge", Loc("volcano"));
            ridge.requires.Add("old_camp");
            ridge.clue = "The markings point inland, towards the smoke.";
            ridge.doneLine = "The ground is warm. The mountain is awake.";

            var beyond = M("deeper", 4, 160, true, "Deeper Still", "Follow the markings deeper into the island",
                Ev("Discovery", "strange_markings*|cave_markings*", 2, true));
            beyond.requires.Add("volcanic_ridge"); beyond.endsChapter = true;
            beyond.doneLine = "They knew this island better than I do.";

            var cave = M("opt_cave", 4, 155, false, "The Cave", "Find where the cold air comes from", Loc("cave"));
            cave.requires.Add("old_camp");
            var fossils = M("opt_fossils", 4, 156, false, "Stone Bones", "Find the bones that turned to stone", Loc("fossil_bed"), Ev("Discovery", "fossil_bed*"));
            fossils.requires.Add("old_camp");
            var nest = M("opt_nest", 4, 157, false, "The Nest", "Find where the hunters raise their young", Loc("nest"), Ev("Discovery", "nest*"));
            nest.requires.Add("old_camp");
            var signs = M("opt_discoveries", 4, 158, false, "Strange Signs", "Record eight discoveries in the journal", Chk("discoveries", count: 8));
            signs.requires.Add("investigate_tracks");
            return l;
        }
    }
}
