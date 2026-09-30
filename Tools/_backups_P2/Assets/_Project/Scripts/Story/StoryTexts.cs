using System.Collections.Generic;

namespace PrimalFrontier.Story
{
    /// <summary>
    /// Built-in story texts: location pages (by location id), discovery pages and the survivor's thought when examining a
    /// prop (by discovery id), resource pages (by item id) and creature facts (by species id). First person, short, sober.
    /// The journal copies these into its page list once (JournalSystem); edits made in the scene win over this table.
    /// </summary>
    public static class StoryTexts
    {
        public class LocationText { public string id, page, title, place, sketch, text; }
        public class DiscoveryText { public string id, page, title, sketch, thought, text; }
        public class ResourceText { public string page, title, items, sketch, text; }
        public class CreatureText { public string id, title, sketch, appearance, diet; }

        // ------------------------------------------------------------------ locations
        /// <summary>page = the journal page id (legacy pages keep their old ids so saves stay valid)</summary>
        public static readonly LocationText[] Locations =
        {
            L("beach", "beach", "The Beach", "the beach", "beach", "Pale sand, driftwood and the broken hull. Behind the beach the forest climbs towards dark hills. Everything I have starts here."),
            L("shipwreck", "wreck", "The Wreck", "the wreck", "wreck", "The storm broke our ship on the reef. I found no one else. The hold is split open and the sea has taken almost everything. What is left, I must use well."),
            L("forest", "loc_forest", "The Forest", "the forest", "meadow", "Tree ferns and cycads taller than a house, and under them a green dusk. Every step cracks something. Every sound stops when I stop."),
            L("deep_forest", "loc_deep_forest", "The Deep Forest", "the deep forest", "meadow", "Here the canopy closes. Moss on everything, roots like walls, air that does not move. I lose the sun in here. I must mark my way."),
            L("river", "loc_river", "The River", "the river", "water", "Brown water, fast in the middle, slow at the edges. Prints of every size in the mud. Everything on this island drinks here."),
            L("waterfall", "loc_waterfall", "The Waterfall", "the waterfall", "water", "The river falls from the ridge in one white sheet. Mist, moss, cold spray, wet black rock around the pool. I could sit here for hours. I should not."),
            L("meadow", "meadow", "The Meadow", "the meadow", "meadow", "An open meadow in the middle of the island. The grass is flattened in wide paths, as if something huge walks here every day."),
            L("canyon", "loc_canyon", "The Canyon", "the canyon", "rocks", "Walls of red stone on both sides and one way through. Sound carries strangely here. I would not want to meet anything in this place."),
            L("wetland", "loc_wetland", "The Wetland", "the wetland", "water", "Reeds, black water, mud that pulls at my feet. Insects in clouds. Something large moves under the surface and never shows itself."),
            L("cave", "cave", "The Cave", "the cave", "cave", "A cave mouth in the cliffs. Cold air breathes out of it. Shelter from rain, but I do not know what else sleeps inside."),
            L("ridge", "loc_ridge", "The Ridge", "the ridge", "rocks", "Bare rock and wind. From here the island opens up: forest, river, the smoke in the distance. It is bigger than I believed."),
            L("volcano", "loc_volcano", "The Volcanic Ridge", "the volcanic ridge", "rocks", "Black stone, warm under my hands. Ash in the air, cracks glowing where the ground is thin. The mountain breathes. It is not asleep."),
            L("predator_territory", "loc_predator_territory", "Hunting Ground", "the hunting ground", "claw", "Bones picked clean, trunks scored with claws, a smell I will not forget. Nothing grazes here. I should not be here either."),
            L("herbivore_valley", "loc_herbivore_valley", "The Grazing Valley", "the grazing valley", "meadow", "A wide valley of ferns and horsetails, cropped low. Paths trodden flat, dung, the low calls of the herds. Life, everywhere."),
            L("old_camp", "loc_old_camp", "The Old Camp", "the old camp", "shelter", "A ring of blackened stones. Poles lashed with cord, rotted through. Someone lived here, long enough to build. Where did they go?"),
            L("fossil_bed", "loc_fossil_bed", "The Stone Bones", "the fossil bed", "rocks", "Bones turned to stone, lying in the rock as if they fell asleep there. Some have the shapes of the animals alive here. Some do not."),
            L("nest", "loc_nest", "The Nest", "the nest", "claw", "A mound of earth and rotting leaves, warm to the touch, broken shells at the edge. I left quickly. Whatever built this will come back."),
            L("migration_view", "loc_migration_view", "The Lookout", "the lookout", "rocks", "High rocks over the valley. From here I can see the paths the herds follow far below, like lines drawn on the land."),
            L("pond", "pond", "The Pond", "the pond", "water", "A still pond in a hollow of the forest, fed by the stream. Animals come here to drink. I am not the only one who knows this place."),
            L("rocky", "rocky", "The Rocky Hills", "the rocky hills", "rocks", "Broken stone and cliffs. Good rock for tools. From up here the island looks bigger than I thought."),
        };

        // ------------------------------------------------------------------ discoveries (Examinable.discoveryId)
        public static readonly DiscoveryText[] Discoveries =
        {
            D("footprint", "footprint", "Giant Footprints", "footprint", "Three toes... each longer than my arm. Whatever made these is huge. And the tracks are fresh.",
              "Three toes, each longer than my forearm, pressed deep into the mud. Whatever made these weighs more than any ox. The tracks are fresh."),
            D("herd_tracks", "disc_herd_tracks", "Herd Tracks", "footprint", "Round prints, dozens of them. A herd passed here.",
              "Wide round prints pressed into the mud, one over another. Dozens of animals moving together, towards water. Where there are grazers, there are hunters."),
            D("claw_marks", "disc_claw_marks", "Claw Marks", "claw", "Deep grooves in the bark... higher than my head.",
              "Four grooves torn through the bark, higher than I can reach. Sap still runs from them. Something is telling the others this place is taken."),
            D("bones", "disc_bones", "Old Bones", "claw", "Bones. Something big died here.",
              "A ribcage like the frame of a boat, bleached by the sun, gnawed at the ends. Whatever killed it did not need to hurry."),
            D("kill_site", "disc_kill_site", "A Kill", "claw", "Blood on the ferns. Not long ago.",
              "Trampled ferns, dark blood, a drag mark into the trees. Flies everywhere. Something fed here, and it may still be close."),
            D("blood_trail", "disc_blood_trail", "Blood Trail", "claw", "Blood... a trail of it.",
              "Drops of blood on the leaves, then more. A wounded animal went this way. So did whatever wounded it."),
            D("broken_trees", "disc_broken_trees", "Broken Trees", "claw", "These trees were snapped like twigs.",
              "Trunks as thick as my waist, snapped at shoulder height. No storm did this. Something pushed through without slowing down."),
            D("nest", "disc_nest", "Eggs", "claw", "Eggs. Still warm.",
              "Eggs half buried in warm leaves, each bigger than my head. I did not touch them. I have seen what guards them."),
            D("fossil_bed", "disc_fossil_bed", "A Stone Skull", "rocks", "Bones... turned to stone.",
              "A skull in the rock, longer than my arm, the teeth still sharp. It has lain here longer than anyone could count. It looks like the hunters by the river."),
            D("old_shelter", "disc_old_shelter", "Collapsed Shelter", "shelter", "Someone built this. Not recently.",
              "Poles set in a frame and lashed with twisted cord, the roof long gone. The knots are good knots. A person made this, a person who knew what they were doing."),
            D("old_firepit", "disc_old_firepit", "Cold Fire Pit", "fire", "A fire pit. The ash is old.",
              "Stones in a ring, the ash packed hard by the rain, charred bones in it. Somebody cooked here, many times."),
            D("old_tools", "disc_old_tools", "A Worked Stone", "axe", "A stone tool... shaped by a hand.",
              "A chopper of dark stone, knapped to an edge and worn smooth where a hand held it for years. Better work than mine. I left it where it lay. It did not feel like mine to take."),
            D("old_camp_tally", "disc_old_camp_tally", "Tally Marks", "rocks", "Marks in groups... counted. Days?",
              "Scratches on the boulder in groups of five, row after row. Someone counted something here. Days, I think. So many days."),
            D("strange_markings", "disc_strange_markings", "Raked Stone", "claw", "Claw marks. Cut into the rock itself.",
              "Four deep grooves raked across the boulder by the spring, the stone inside them still pale, chips of it in the moss below. Each groove is wider than my finger. Something big sharpens its claws here, or marks this water as its own."),
            D("cave_markings", "disc_cave_markings", "Marks by the Cave", "cave", "Knife marks... and a scrap of sail.",
              "Scratches cut with a blade into the rock beside the cave mouth, in rows of five, the last row unfinished, and an arrow pointing inside. A rag of sailcloth is pinned under a stone. Another castaway sheltered here. I hope they found a way off."),
            D("old_wreck", "disc_old_wreck", "Older Timbers", "wreck", "Timber. Not from our ship.",
              "Ship's timbers half sunk in the sand, grey and worn smooth, the nails rusted to nothing. This wreck is far older than ours. We were not the first."),
            D("shipwreck_remains", "disc_shipwreck_remains", "Wreckage", "wreck", "More of the ship...",
              "Planks, a torn sail, a broken cask washed further along the shore. I looked for names and faces. Only the sea."),
            D("giant_skeleton", "disc_giant_skeleton", "A Giant's Bones", "claw", "Bones... it must have been enormous.",
              "A skeleton as long as a boat, ribs standing out of the ferns like a broken fence. The bones are cracked open. Whatever did that still walks here."),
            D("theropod_trail", "disc_theropod_trail", "The Hunter's Trail", "footprint", "The same three toes... a whole line of them.",
              "A line of prints, three toes each, pressed deep and far apart. It was walking, not running. It walks this path often."),
            D("lava_channel", "disc_lava_channel", "Burning Ground", "rocks", "The rock... is melting.",
              "A crack in the black rock, glowing orange, slow as honey. The air shakes above it. The ground here is alive, and it is hot enough to kill."),
            D("view_knoll", "disc_view_knoll", "The View", "meadow", "I can see the whole valley from here.",
              "From the knoll the valley opens: the meadow, the ford, the white thread of the waterfall. The herds cross down there, on paths older than any of them."),
            D("spring", "disc_spring", "The Spring", "water", "Clean water, straight out of the rock.",
              "The river starts here, a cold spring welling out of the rock. Water this clear needs no fire. Something big drinks here too: its claw marks are on the boulder beside it."),
            D("captains_log", "captains_log", "Captain's Log", "log", "The captain's log. Most pages are ruined...",
              "Water-stained pages from the captain's chest: 'Day 41. Compass spinning. Charts useless. The men speak of an island that is not on any map. Storm rising from the south.' The rest is unreadable."),
        };

        // ------------------------------------------------------------------ resources (first time an item is picked up)
        public static readonly ResourceText[] Resources =
        {
            R("res_wood", "Wood", "wood", "axe", "Driftwood on the beach, dead branches in the forest. Dry wood burns, green wood smokes. I need it for almost everything."),
            R("res_stone", "Stone", "stone", "rocks", "Rounded stones on the shore, sharper ones up in the rocks. Struck together, the right ones break with an edge."),
            R("res_fiber", "Plant Fibre", "fiber", "rope", "Long tough leaves from the fibrous plants behind the beach. Dried and twisted, they become cord."),
            R("res_berries", "Berries", "berries", "meadow", "Small dark berries from the bushes at the forest edge. Sweet, with a little water in them. The animals leave some for me."),
            R("res_greens", "Wild Greens", "edible_plant", "meadow", "Soft-leaved plants that grow near water. Bitter, but they fill the stomach and do not make me sick."),
            R("res_meat", "Meat", "raw_meat", "meat", "Dark, heavy meat. Cooked, it keeps me strong for a long time. Raw, it is a gamble, and the smell of it carries."),
            R("res_fish", "Fish", "raw_fish", "water", "The shallows hold fish as long as my arm. Quick to cook, quick to spoil."),
            R("res_hide", "Hide", "hide", "shelter", "Thick scaled skin. Scraped and dried, it makes straps, bags, a bed off the cold ground."),
            R("res_bone", "Bone", "bone", "claw", "Hard and light. Sharpened, it makes points. There is no shortage of bones on this island."),
            R("res_scraps", "Ship Scraps", "scrap|ship_scrap|ship_scraps|wreck_scrap|shipwreck_scrap|shipwreck_scraps", "wreck", "Nails, a strip of canvas, a length of old rope from the wreck. There will not be more. I use them carefully."),
        };

        // ------------------------------------------------------------------ creatures (appearance on sighting, diet once seen eating)
        public static readonly CreatureText[] Creatures =
        {
            C("triceratops", "Three-Horned Grazer", "trike", "A beast the size of a hut, with three horns and a great bony frill. It watches me with one eye and goes on grazing.", "Crops ferns and low cycads with its beak, hour after hour."),
            C("parasaurolophus", "Crested Callers", "trike", "Tall grazers with a long hollow crest. They call to each other with deep notes that carry across the valley.", "Browses leaves and horsetails, often close to water."),
            C("ankylosaurus", "Armoured Tank", "rocks", "Low, wide and covered in bony plates, with a heavy club at the end of its tail. Slow, but I would not want to stand behind it.", "Grubs up low plants and roots."),
            C("velociraptor", "Small Hunters", "claw", "No taller than my waist, feathered, fast, and never alone. A hooked claw on each foot.", "Meat. They feed together, fast and quarrelling."),
            C("carnotaurus", "Horned Runner", "claw", "A tall hunter with two short horns above its eyes and tiny arms. It runs like the wind.", "Meat. It takes the grazers at the edge of the herds."),
            C("spinosaurus", "Sail-Back", "water", "A long-snouted giant with a great sail on its back. It stands in the river for hours.", "Fish, mostly, snapped from the shallows. It would not refuse other meat."),
            C("apex", "The Rift Tyrant", "claw", "A head as long as a man, jaws full of teeth like knives. The forest goes quiet when it walks.", "Anything it can catch."),
            C("pteranodon", "Leather Wings", "rocks", "Huge winged shapes circling over the cliffs, skin stretched on long fingers, a crest like an axe blade.", "Fish, taken from the sea."),
            C("mosasaurus", "Sea Serpent", "water", "A long dark back rolled out beyond the reef, longer than our ship. I will not swim out there.", "Whatever swims."),
        };

        static LocationText L(string id, string page, string title, string place, string sketch, string text) => new LocationText { id = id, page = page, title = title, place = place, sketch = sketch, text = text };
        static DiscoveryText D(string id, string page, string title, string sketch, string thought, string text) => new DiscoveryText { id = id, page = page, title = title, sketch = sketch, thought = thought, text = text };
        static ResourceText R(string page, string title, string items, string sketch, string text) => new ResourceText { page = page, title = title, items = items, sketch = sketch, text = text };
        static CreatureText C(string id, string title, string sketch, string appearance, string diet) => new CreatureText { id = id, title = title, sketch = sketch, appearance = appearance, diet = diet };

        static Dictionary<string, LocationText> _loc; static Dictionary<string, DiscoveryText> _disc; static Dictionary<string, CreatureText> _cre;
        public static LocationText Location(string id)
        {
            if (_loc == null) { _loc = new Dictionary<string, LocationText>(); foreach (var l in Locations) _loc[l.id] = l; }
            return id != null && _loc.TryGetValue(id, out var t) ? t : null;
        }
        public static DiscoveryText Discovery(string id)
        {
            if (_disc == null) { _disc = new Dictionary<string, DiscoveryText>(); foreach (var d in Discoveries) _disc[d.id] = d; }
            return id != null && _disc.TryGetValue(StoryIds.Discovery(id), out var t) ? t : null;
        }
        public static CreatureText Creature(string id)
        {
            if (_cre == null) { _cre = new Dictionary<string, CreatureText>(); foreach (var c in Creatures) _cre[c.id] = c; }
            return id != null && _cre.TryGetValue(id, out var t) ? t : null;
        }
    }
}
