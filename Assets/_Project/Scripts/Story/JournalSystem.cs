using System;
using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Core;

namespace PrimalFrontier.Story
{
    public enum JournalCategory { Survival, Creatures, Crafting, World }

    /// <summary>
    /// The handmade survival journal: entries are written when the player discovers something (drinks fresh water,
    /// finds the footprints, lights a fire...). Knowledge comes from discovery, never from a menu of everything.
    /// </summary>
    public class JournalSystem : MonoBehaviour
    {
        [Serializable]
        public class Entry
        {
            public string id, title; public JournalCategory category; [TextArea] public string text; public string sketch;
            public GameEventType trigger; public string triggerId;     // null triggerId = any
        }

        public static JournalSystem Instance { get; private set; }
        [Tooltip("Every journal page: title, text, sketch and what unlocks it. Edit the texts freely. Empty list = built-in pages.")]
        public List<Entry> entries = new List<Entry>();
        readonly HashSet<string> _unlocked = new HashSet<string>();
        readonly List<string> _order = new List<string>();
        public IReadOnlyList<string> UnlockedInOrder => _order;
        public event Action<Entry> Unlocked;
        public bool HasUnread { get; set; }

        void Awake() { Instance = this; if (entries.Count == 0) DefaultEntries(); }
        void OnDestroy() { if (Instance == this) Instance = null; }
        void OnEnable() { GameEvents.Raised += OnEvent; }
        void OnDisable() { GameEvents.Raised -= OnEvent; }

        public bool IsUnlocked(string id) => _unlocked.Contains(id);
        public Entry Get(string id) => entries.Find(e => e.id == id);

        void OnEvent(GameEvent e)
        {
            foreach (var en in entries)
                if (en.trigger == e.type && (string.IsNullOrEmpty(en.triggerId) || en.triggerId == e.id)) Unlock(en.id);
        }

        public void Unlock(string id, bool notify = true)
        {
            var en = Get(id); if (en == null || !_unlocked.Add(id)) return;
            _order.Add(id); HasUnread = true;
            if (notify) { Unlocked?.Invoke(en); GameEvents.Raise(GameEventType.JournalUnlocked, id); }
        }

        public void SetUnlocked(IEnumerable<string> ids) { _unlocked.Clear(); _order.Clear(); foreach (var i in ids) if (Get(i) != null && _unlocked.Add(i)) _order.Add(i); }

        void Add(string id, JournalCategory c, string title, GameEventType t, string tid, string sketch, string text) =>
            entries.Add(new Entry { id = id, category = c, title = title, trigger = t, triggerId = tid, sketch = sketch, text = text });

        /// <summary>replace the pages with the built-in ones (Inspector: right click the component)</summary>
        [ContextMenu("Reset pages to the built-in text")]
        public void ResetToDefaults() { entries.Clear(); DefaultEntries(); }

        void DefaultEntries()
        {
            // SURVIVAL
            Add("wreck", JournalCategory.Survival, "The Wreck", GameEventType.ZoneEntered, "ZONE_Shipwreck", "wreck",
                "The storm broke our ship on the reef. I found no one else. The hold is split open and the sea has taken almost everything. What is left, I must use well.");
            Add("fresh_water", JournalCategory.Survival, "Fresh Water", GameEventType.Drank, "fresh_water", "water",
                "A stream runs down from the hills into a quiet pond. The water is cold and sweet. I must stay close to it, and carry some when I can.");
            Add("salt_water", JournalCategory.Survival, "The Sea Is Not Water", GameEventType.TriedSaltWater, null, "water",
                "I drank from the sea like a fool. The salt burned and the thirst came back worse. Only fresh water will keep me alive.");
            Add("fire", JournalCategory.Survival, "Fire", GameEventType.FireLit, null, "fire",
                "Fire. Warmth, light, cooked food, and a mark of safety in the dark. Rain eats the fuel quickly; keep wood close.");
            Add("cooking", JournalCategory.Survival, "Cooked Food", GameEventType.FoodCooked, null, "meat",
                "Meat cooked over the coals fills me more and sits better. Raw meat is a gamble with my stomach.");
            Add("night", JournalCategory.Survival, "The First Night", GameEventType.NightStarted, null, "night",
                "Darkness falls fast here. The air turns cold, and the forest is full of sounds I cannot name. Stay near the fire.");
            Add("shelter", JournalCategory.Survival, "Shelter", GameEventType.StructurePlaced, "shelter", "shelter",
                "Branches and leaves, lashed together. Not much, but it keeps off the rain and lets me rest.");
            // CRAFTING
            Add("stone_tools", JournalCategory.Crafting, "Stone Tools", GameEventType.ItemCrafted, "stone_axe", "axe",
                "Strike stone on stone until an edge appears, then bind it to a haft with cord. My grandfather's stories were true: a sharp stone is a second hand.");
            Add("cordage", JournalCategory.Crafting, "Cordage", GameEventType.ItemCrafted, "rope", "rope",
                "Plant fibre, twisted and doubled back on itself, becomes cord. Cord holds everything together.");
            Add("spear", JournalCategory.Crafting, "The Spear", GameEventType.ItemCrafted, "stone_spear", "spear",
                "A long shaft and a knapped point. Thrust to keep distance, or throw it if I must. Always pick it up again.");
            Add("bow", JournalCategory.Crafting, "Bow and Arrows", GameEventType.ItemCrafted, "bow", "bow",
                "A springy stave and a cord. Draw fully before letting go, or the arrow flies weak.");
            // WORLD
            Add("beach", JournalCategory.World, "The Beach", GameEventType.ZoneEntered, "ZONE_StartBeach", "beach",
                "White sand, driftwood, and the wreck. Behind the beach the forest rises towards dark hills.");
            Add("pond", JournalCategory.World, "The Pond", GameEventType.ZoneEntered, "ZONE_Pond", "water",
                "A still pond in a hollow of the forest, fed by the stream. Animals come here to drink. I am not the only one who knows this place.");
            Add("meadow", JournalCategory.World, "The Meadow", GameEventType.ZoneEntered, "ZONE_Meadow", "meadow",
                "An open meadow in the middle of the island. The grass is flattened in wide paths, as if something huge walks here every day.");
            Add("cave", JournalCategory.World, "The Cave", GameEventType.ZoneEntered, "ZONE_Cave", "cave",
                "A cave mouth in the cliffs. Cold air breathes out of it. Shelter from rain, but I do not know what else sleeps inside.");
            Add("rocky", JournalCategory.World, "The Rocky Hills", GameEventType.ZoneEntered, "ZONE_Rocky", "rocks",
                "Broken stone and cliffs. Good rock for tools. From up here the island looks bigger than I thought.");
            Add("footprint", JournalCategory.World, "Giant Footprints", GameEventType.FootprintFound, null, "footprint",
                "Three toes, each longer than my forearm, pressed deep into the mud. Whatever made these weighs more than any ox. The tracks are fresh.");
            Add("captains_log", JournalCategory.World, "Captain's Log", GameEventType.Discovery, "captains_log", "log",
                "Water-stained pages from the captain's chest: 'Day 41. Compass spinning. Charts useless. The men speak of an island that is not on any map. Storm rising from the south.' The rest is unreadable.");
            // CREATURES
            Add("triceratops", JournalCategory.Creatures, "Three-Horned Grazer", GameEventType.CreatureSighted, "triceratops", "trike",
                "A beast the size of a hut, with three horns and a great bony frill. It grazes calmly and watches me with one eye. I think it will leave me alone if I leave it alone.");
            Add("parasaurolophus", JournalCategory.Creatures, "Crested Callers", GameEventType.CreatureSighted, "parasaurolophus", "trike",
                "They move in small groups and call to each other with long, hollow notes through the crests on their heads. At the first sign of danger they run.");
            Add("ankylosaurus", JournalCategory.Creatures, "Armoured Tank", GameEventType.CreatureSighted, "ankylosaurus", "rocks",
                "Low, wide and covered in bony plates, with a heavy club at the end of its tail. Slow, but I would not want to stand behind it.");
            Add("velociraptor", JournalCategory.Creatures, "Small Hunters", GameEventType.CreatureSighted, "velociraptor", "claw",
                "No taller than my waist, feathered, fast, and never alone. A hooked claw on each foot. They watch before they strike.");
            Add("carnotaurus", JournalCategory.Creatures, "Horned Runner", GameEventType.CreatureSighted, "carnotaurus", "claw",
                "A tall hunter with two short horns above its eyes and tiny arms. It guards its ground and runs like the wind. Stay out of its sight.");
            Add("spinosaurus", JournalCategory.Creatures, "Sail-Back", GameEventType.CreatureSighted, "spinosaurus", "water",
                "A long-snouted giant with a great sail on its back, hunting along the stream. The water is its territory.");
            Add("apex", JournalCategory.Creatures, "The Rift Tyrant", GameEventType.CreatureSighted, "apex", "claw",
                "I heard it long before I saw it. A head as long as a man, jaws full of teeth like knives. Everything on this island fears it. Everything.");
            Add("predator_sign", JournalCategory.Creatures, "Something Hunts Here", GameEventType.PredatorWarning, null, "claw",
                "A roar rolled across the hills and the forest went silent. The grazers lifted their heads. Something on this island hunts, and it is not me.");
        }
    }
}
