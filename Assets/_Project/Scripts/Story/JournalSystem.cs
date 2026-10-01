using System;
using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Survival;
using PrimalFrontier.World;

namespace PrimalFrontier.Story
{
    /// <summary>
    /// Journal sections. The numbers are stored in the scene's page list: the old four keep theirs (World = 3 is now
    /// Locations), the new ones are appended. The book shows them in <see cref="JournalSystem.Sections"/> order.
    /// </summary>
    public enum JournalCategory { Survival = 0, Creatures = 1, Crafting = 2, Locations = 3, Resources = 4, Discoveries = 5 }

    /// <summary>
    /// The survivor's journal, learned by experience: pages unlock from game events (drinking fresh water, entering a place,
    /// picking up a new material, examining a strange prop, seeing a creature). Six sections: SURVIVAL, CRAFTING, CREATURES,
    /// RESOURCES, LOCATIONS, DISCOVERIES. Creature pages are written by observation (<see cref="CreatureJournal"/>: habitat,
    /// diet, habits, threat fill in as they are seen). The scene's page list keeps hand edits; built-in pages missing from it
    /// are added at start, old pages move to the new sections (the wreck to LOCATIONS, prints and the log to DISCOVERIES,
    /// fruit to RESOURCES) and keep their ids, so saves (which store page ids) stay valid. Examined props with an id the
    /// journal does not know get a page from the prop itself. Creature knowledge is saved as the "creatures" section.
    /// </summary>
    public class JournalSystem : MonoBehaviour, ISaveSection
    {
        [Serializable]
        public class Entry
        {
            public string id, title; public JournalCategory category; [TextArea] public string text; public string sketch;
            public GameEventType trigger; public string triggerId;     // null triggerId = any; '|' alternatives, '*' prefix
            [Tooltip("creature page: the species id whose observations fill the page")] public string creatureId;
        }

        public static readonly JournalCategory[] Sections = { JournalCategory.Survival, JournalCategory.Crafting, JournalCategory.Creatures, JournalCategory.Resources, JournalCategory.Locations, JournalCategory.Discoveries };
        public static string SectionName(JournalCategory c)
        {
            switch (c)
            {
                case JournalCategory.Survival: return "SURVIVAL"; case JournalCategory.Crafting: return "CRAFTING"; case JournalCategory.Creatures: return "CREATURES";
                case JournalCategory.Resources: return "RESOURCES"; case JournalCategory.Locations: return "LOCATIONS"; case JournalCategory.Discoveries: return "DISCOVERIES";
            }
            return c.ToString().ToUpperInvariant();
        }

        public static JournalSystem Instance { get; private set; }
        [Tooltip("Every journal page: title, text, sketch and what unlocks it. Edit the texts freely. Empty list = built-in pages.")]
        public List<Entry> entries = new List<Entry>();
        readonly HashSet<string> _unlocked = new HashSet<string>(StringComparer.Ordinal);
        readonly List<string> _order = new List<string>();
        readonly HashSet<string> _pending = new HashSet<string>(StringComparer.Ordinal);     // saved ids whose page appears later (props)
        readonly Dictionary<string, Entry> _byId = new Dictionary<string, Entry>(StringComparer.Ordinal);
        readonly HashSet<string> _sceneDiscoveries = new HashSet<string>(StringComparer.Ordinal);
        public IReadOnlyList<string> UnlockedInOrder => _order;
        public event Action<Entry> Unlocked;
        /// <summary>a creature page got new notes (species id)</summary>
        public event Action<Entry> Updated;
        public bool HasUnread { get; set; }
        public CreatureJournal Creatures { get; } = new CreatureJournal();
        float _noteAt;

        void Awake()
        {
            Instance = this;
            if (entries.Count == 0) DefaultEntries();
            AddSurvivalPages();
            AddBuiltIns();
            Migrate();
            Reindex();
            Creatures.Learned += OnCreatureLearned;
        }
        void Start() { ScanExaminables(); }
        void OnDestroy() { if (Instance == this) Instance = null; Creatures.Learned -= OnCreatureLearned; Creatures.Unhook(); }
        void OnEnable() { GameEvents.Raised -= OnEvent; GameEvents.Raised += OnEvent; SaveSystem.RegisterSection(this); }
        void OnDisable() { GameEvents.Raised -= OnEvent; SaveSystem.UnregisterSection(this); }

        void Update()
        {
            var gm = GameManager.Instance;
            if (gm && gm.State != GameState.Playing) return;
            Creatures.Tick(Time.time);
        }

        void Reindex() { _byId.Clear(); foreach (var e in entries) if (e != null && !string.IsNullOrEmpty(e.id) && !_byId.ContainsKey(e.id)) _byId[e.id] = e; }

        public bool IsUnlocked(string id) => id != null && _unlocked.Contains(id);
        public Entry Get(string id) => id != null && _byId.TryGetValue(id, out var e) ? e : null;
        public Entry CreaturePage(string species) { foreach (var e in entries) if (e != null && e.creatureId == species) return e; return null; }

        // ------------------------------------------------------------------ events
        void OnEvent(GameEvent e)
        {
            if (e.type == GameEventType.GameLoaded) { ScanExaminables(); return; }
            if (StoryIds.EventIs(e.type, "HerdSighted")) Creatures.OnHerdEvent(e.id, e.position);
            for (int i = 0; i < entries.Count; i++)
            {
                var en = entries[i];
                if (en == null || _unlocked.Contains(en.id) || !Triggers(en, e)) continue;
                Unlock(en.id);
            }
        }

        static bool Triggers(Entry en, GameEvent e)
        {
            if (en.trigger != e.type)
            {
                // a prop page listens to both prop events (ENV may mark prints as FootprintFound), by id only
                bool pageIsProp = en.trigger == GameEventType.Discovery || en.trigger == GameEventType.FootprintFound;
                bool eventIsProp = e.type == GameEventType.Discovery || e.type == GameEventType.FootprintFound;
                if (!pageIsProp || !eventIsProp || string.IsNullOrEmpty(en.triggerId)) return false;
            }
            if (string.IsNullOrEmpty(en.triggerId)) return true;
            switch (e.type)
            {
                case GameEventType.ZoneEntered: return StoryIds.LocationMatches(e.id, en.triggerId);
                case GameEventType.Discovery: case GameEventType.FootprintFound:
                    return StoryIds.Matches(StoryIds.Discovery(e.id), StoryIds.Discovery(en.triggerId)) || StoryIds.Matches(e.id, en.triggerId);
                default: return StoryIds.Matches(e.id, en.triggerId);
            }
        }

        public void Unlock(string id, bool notify = true)
        {
            var en = Get(id); if (en == null) { if (!string.IsNullOrEmpty(id)) _pending.Add(id); return; }
            if (!_unlocked.Add(id)) return;
            _order.Add(id); HasUnread = true;
            if (notify) { Unlocked?.Invoke(en); GameEvents.Raise(GameEventType.JournalUnlocked, id); }
        }

        public void SetUnlocked(IEnumerable<string> ids)
        {
            _unlocked.Clear(); _order.Clear(); _pending.Clear();
            foreach (var i in ids)
            {
                if (string.IsNullOrEmpty(i)) continue;
                if (Get(i) != null) { if (_unlocked.Add(i)) _order.Add(i); }
                else _pending.Add(i);
            }
        }

        /// <summary>new game: no pages, no creature notes</summary>
        public void ResetAll() { SetUnlocked(Array.Empty<string>()); Creatures.Clear(); HasUnread = false; }

        public int CountUnlocked(JournalCategory c) { int n = 0; foreach (var id in _order) { var e = Get(id); if (e != null && e.category == c) n++; } return n; }
        /// <summary>species with a page the player has opened by seeing them</summary>
        public int CreaturesKnown { get { int n = 0; foreach (var id in _order) { var e = Get(id); if (e != null && e.category == JournalCategory.Creatures && !string.IsNullOrEmpty(e.creatureId)) n++; } return n; } }

        /// <summary>pages of a section that can be found on this island (places that exist, props that are placed, creatures that live here) + those unlocked</summary>
        public int CountFindable(JournalCategory c)
        {
            int n = 0;
            foreach (var e in entries) if (e != null && e.category == c && (IsUnlocked(e.id) || Findable(e))) n++;
            return n;
        }
        bool Findable(Entry e)
        {
            switch (e.category)
            {
                case JournalCategory.Locations:
                    if (e.trigger != GameEventType.ZoneEntered || string.IsNullOrEmpty(e.triggerId)) return true;
                    var ms = MissionSystem.Instance; if (!ms) return true;
                    foreach (var id in StoryIds.Split(e.triggerId)) if (ms.TryLocation(id, out _)) return true;
                    return false;
                case JournalCategory.Discoveries:
                    if (e.trigger != GameEventType.Discovery && e.trigger != GameEventType.FootprintFound) return true;
                    if (string.IsNullOrEmpty(e.triggerId)) return true;
                    foreach (var id in StoryIds.Split(e.triggerId)) if (_sceneDiscoveries.Contains(StoryIds.Discovery(id))) return true;
                    return false;
                case JournalCategory.Creatures:
                    if (string.IsNullOrEmpty(e.creatureId)) return true;
                    foreach (var d in AI.DinosaurController.All) if (d && d.def && d.def.id == e.creatureId) return true;
                    foreach (var a in AI.AmbientCreature.All) if (a && a.def && a.def.id == e.creatureId) return true;
                    return false;
            }
            return true;
        }

        /// <summary>the page text: creature pages are written from observations</summary>
        public string PageText(Entry e)
        {
            if (e == null) return "";
            if (!string.IsNullOrEmpty(e.creatureId)) return Creatures.Compose(e.creatureId, e.text);
            return e.text;
        }

        void OnCreatureLearned(string species, CreatureSeen what)
        {
            var page = CreaturePage(species);
            if (page == null) { page = AddCreaturePage(species); if (page == null) return; }
            if (!IsUnlocked(page.id)) { Unlock(page.id); return; }            // the unlock note says enough
            HasUnread = true;
            Updated?.Invoke(page);
            if (what == CreatureSeen.Resting || Time.unscaledTime - _noteAt < 20f) return;
            _noteAt = Time.unscaledTime;
            var hud = UI.HUDManager.Instance; if (hud) hud.Notify("Journal: " + page.title + ", new notes   [J]");
        }

        Entry AddCreaturePage(string species)
        {
            var t = StoryTexts.Creature(species);
            var k = Creatures.Get(species);
            string title = t != null ? t.title : k != null && !string.IsNullOrEmpty(k.displayName) ? k.displayName : species;
            var en = new Entry { id = species, category = JournalCategory.Creatures, title = title, trigger = GameEventType.CreatureSighted, triggerId = species,
                                 sketch = t != null ? t.sketch : "claw", text = t != null ? t.appearance : "", creatureId = species };
            if (Get(en.id) != null) en.id = "creature_" + species;
            entries.Add(en); _byId[en.id] = en;
            return en;
        }

        // ------------------------------------------------------------------ props
        /// <summary>every examinable prop in the scene has a page (unknown ids get one from the prop's own name and thought)</summary>
        public void ScanExaminables()
        {
            _sceneDiscoveries.Clear();
            foreach (var ex in FindObjectsByType<Examinable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!ex || string.IsNullOrEmpty(ex.discoveryId)) continue;
                _sceneDiscoveries.Add(StoryIds.Discovery(ex.discoveryId));
                EnsureDiscoveryPage(ex);
            }
            if (_pending.Count > 0)
                foreach (var id in new List<string>(_pending)) if (Get(id) != null) { _pending.Remove(id); if (_unlocked.Add(id)) _order.Add(id); }
        }

        public Entry EnsureDiscoveryPage(Examinable ex)
        {
            string did = StoryIds.Discovery(ex.discoveryId);
            foreach (var e in entries)
                if (e != null && (e.trigger == GameEventType.Discovery || e.trigger == GameEventType.FootprintFound) && !string.IsNullOrEmpty(e.triggerId) && StoryIds.Discovery(e.triggerId) == did) return e;
            var t = StoryTexts.Discovery(did);
            var en = new Entry
            {
                id = t != null ? t.page : "disc_" + did, category = JournalCategory.Discoveries, trigger = GameEventType.Discovery, triggerId = did,
                title = t != null ? t.title : Title(ex.displayName), sketch = t != null ? t.sketch : "rocks",
                text = t != null ? t.text : (!string.IsNullOrEmpty(ex.thought) ? ex.thought : "I stopped and looked at it for a long time. I do not know what to make of it."),
            };
            if (Get(en.id) != null) return Get(en.id);
            entries.Add(en); _byId[en.id] = en;
            return en;
        }
        static string Title(string s) => string.IsNullOrEmpty(s) ? "Something Strange" : char.ToUpperInvariant(s[0]) + s.Substring(1);

        // ------------------------------------------------------------------ pages
        void Add(string id, JournalCategory c, string title, GameEventType t, string tid, string sketch, string text, string creature = null)
        {
            if (Get(id) != null || entries.Exists(e => e != null && e.id == id)) return;
            var en = new Entry { id = id, category = c, title = title, trigger = t, triggerId = tid, sketch = sketch, text = text, creatureId = creature };
            entries.Add(en); _byId[id] = en;
        }

        /// <summary>replace the pages with the built-in ones (Inspector: right click the component)</summary>
        [ContextMenu("Reset pages to the built-in text")]
        public void ResetToDefaults() { entries.Clear(); _byId.Clear(); DefaultEntries(); AddSurvivalPages(); AddBuiltIns(); Migrate(); Reindex(); }

        /// <summary>add the built-in pages the list does not have yet and move old pages to the new sections (builder / Inspector)</summary>
        [ContextMenu("Add missing built-in pages")]
        public void SyncBuiltIns() { Reindex(); AddSurvivalPages(); AddBuiltIns(); Migrate(); Reindex(); }

        /// <summary>survival milestone 1 pages; trigger ids come from the SurvivalConfig water profiles</summary>
        void AddSurvivalPages()
        {
            var cfg = SurvivalConfig.Instance;
            string salt = cfg.Water(WaterType.SaltWater).eventId, clean = cfg.Water(WaterType.CleanWater).eventId, dirty = cfg.Water(WaterType.DirtyWater).eventId;
            foreach (var en in entries)
                if (en != null && en.trigger == GameEventType.Drank && en.triggerId == LegacyFreshWaterId && !string.IsNullOrEmpty(dirty)) en.triggerId = dirty;
            Add("sea_water_carried", JournalCategory.Survival, "Carrying the Sea", GameEventType.WaterFilled, salt, "water",
                "I filled a container at the shore. Sea water is no good to drink, and boiling it does not help: the water goes, the salt stays. The sea will never be drinking water. I need a stream, a spring or the rain.");
            Add("clean_water", JournalCategory.Survival, "Clean Water", GameEventType.Drank, clean, "water",
                "Boiled water, or rain caught before it touches the ground. It tastes flat, but my stomach keeps it down. This is the water to carry.");
        }
        const string LegacyFreshWaterId = "fresh_water";

        /// <summary>resources, locations, discoveries, the new survival / crafting pages and the creatures added in the PC phase</summary>
        void AddBuiltIns()
        {
            foreach (var l in StoryTexts.Locations) Add(l.page, JournalCategory.Locations, l.title, GameEventType.ZoneEntered, l.id, l.sketch, l.text);
            foreach (var d in StoryTexts.Discoveries)
                Add(d.page, JournalCategory.Discoveries, d.title, d.id == "footprint" ? GameEventType.FootprintFound : GameEventType.Discovery, d.id, d.sketch, d.text);
            // Phase 2: landmarks / hidden spots (found by seeing or reaching them) and Bone Valley's lesson
            foreach (var m in StoryTexts.Landmarks) Add(m.page, JournalCategory.Discoveries, m.title, GameEventType.ZoneEntered, m.id, m.sketch, m.text);
            var lesson = StoryTexts.BoneValleyLesson;
            Add(lesson.page, JournalCategory.Survival, lesson.title, GameEventType.ZoneEntered, lesson.id, lesson.sketch, lesson.text);
            foreach (var r in StoryTexts.Resources) Add(r.page, JournalCategory.Resources, r.title, GameEventType.ItemAdded, r.items, r.sketch, r.text);
            foreach (var c in StoryTexts.Creatures) Add(c.id, JournalCategory.Creatures, c.title, GameEventType.CreatureSighted, c.id, c.sketch, c.appearance, c.id);
            Add("wounds", JournalCategory.Survival, "Wounds", GameEventType.StatusApplied, "bleeding|leg_injury|arm_injury", "claw",
                "A wound that will not close by itself. Fibre wrapped tight stops the bleeding; rest, food and water do the rest. Out here a small cut can kill.");
            Add("rain", JournalCategory.Survival, "Rain", GameEventType.WeatherChanged, "Rain|Storm", "water",
                "Rain soaks through everything. Wet means cold, and cold means slow. A roof, a fire, and wood kept dry.");
            Add("spoiled_food", JournalCategory.Survival, "Food Turns", GameEventType.FoodSpoiled, null, "meat",
                "Meat goes bad here in a day, fruit in two. The smell tells me before my stomach does. Eat the oldest first.");
            Add("second_chance", JournalCategory.Survival, "Waking Again", GameEventType.PlayerRespawned, null, "shelter",
                "I woke where I had last rested, aching everywhere. Half of what I carried is still lying where I fell. I must go back for it, carefully.");
            Add("torch", JournalCategory.Crafting, "Torch", GameEventType.ItemCrafted, "torch", "fire",
                "A branch wrapped with fibre and resin. It burns down, but while it lasts the dark is less dark, and some hunters keep their distance.");
            Add("bandage", JournalCategory.Crafting, "Bandages", GameEventType.ItemCrafted, "bandage", "rope",
                "Fibre beaten soft and rolled tight. Wrapped hard over a wound, it stops the bleeding.");
            Add("containers", JournalCategory.Crafting, "Carrying Water", GameEventType.ItemCrafted, "leaf_cup|water_container|leather_waterskin", "water",
                "Something to carry water in means I can leave the stream. Boil it at the fire before I trust it.");
            Add("knife", JournalCategory.Crafting, "The Knife", GameEventType.ItemCrafted, "flint_knife|butcher_knife", "axe",
                "A flake of flint with a bound grip. It cuts fibre, hide and meat. The most useful thing I own.");
        }

        /// <summary>old pages into the new sections; creature pages get their species; old default creature texts become the observation-free ones</summary>
        void Migrate()
        {
            foreach (var e in entries)
            {
                if (e == null) continue;
                if (e.id == "footprint" && string.IsNullOrEmpty(e.triggerId)) e.triggerId = "footprint";
                if (e.id == "wreck") e.category = JournalCategory.Locations;
                else if (e.id == "fruit") e.category = JournalCategory.Resources;
                else if (e.id == "predator_sign") e.category = JournalCategory.Discoveries;
                else if (e.category == JournalCategory.Locations && e.trigger != GameEventType.ZoneEntered) e.category = JournalCategory.Discoveries;
                if (e.category == JournalCategory.Creatures && e.trigger == GameEventType.CreatureSighted && string.IsNullOrEmpty(e.creatureId) && !string.IsNullOrEmpty(e.triggerId))
                    e.creatureId = e.triggerId;
                if (!string.IsNullOrEmpty(e.creatureId) && LegacyCreatureTexts.TryGetValue(e.id, out var old) && e.text == old)
                {
                    var t = StoryTexts.Creature(e.creatureId); if (t != null) e.text = t.appearance;
                }
            }
        }

        /// <summary>creature texts of the pre-PC journal: they told behaviour before it was seen, so they are replaced when unedited</summary>
        static readonly Dictionary<string, string> LegacyCreatureTexts = new Dictionary<string, string>
        {
            { "triceratops", "A beast the size of a hut, with three horns and a great bony frill. It grazes calmly and watches me with one eye. I think it will leave me alone if I leave it alone." },
            { "parasaurolophus", "They move in small groups and call to each other with long, hollow notes through the crests on their heads. At the first sign of danger they run." },
            { "ankylosaurus", "Low, wide and covered in bony plates, with a heavy club at the end of its tail. Slow, but I would not want to stand behind it." },
            { "velociraptor", "No taller than my waist, feathered, fast, and never alone. A hooked claw on each foot. They watch before they strike." },
            { "carnotaurus", "A tall hunter with two short horns above its eyes and tiny arms. It guards its ground and runs like the wind. Stay out of its sight." },
            { "spinosaurus", "A long-snouted giant with a great sail on its back, hunting along the stream. The water is its territory." },
            { "apex", "I heard it long before I saw it. A head as long as a man, jaws full of teeth like knives. Everything on this island fears it. Everything." },
        };

        void DefaultEntries()
        {
            // SURVIVAL
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
                "Strike stone on stone until an edge appears, then bind it to a haft with cord. A sharp stone is a second hand.");
            Add("cordage", JournalCategory.Crafting, "Cordage", GameEventType.ItemCrafted, "rope", "rope",
                "Plant fibre, twisted and doubled back on itself, becomes cord. Cord holds everything together.");
            Add("spear", JournalCategory.Crafting, "The Spear", GameEventType.ItemCrafted, "stone_spear", "spear",
                "A long shaft and a knapped point. Thrust to keep distance, or throw it if I must. Always pick it up again.");
            Add("bow", JournalCategory.Crafting, "Bow and Arrows", GameEventType.ItemCrafted, "bow", "bow",
                "A springy stave and a cord. Draw fully before letting go, or the arrow flies weak.");
            // DISCOVERIES (moved from the old WORLD section)
            Add("predator_sign", JournalCategory.Discoveries, "Something Hunts Here", GameEventType.PredatorWarning, null, "claw",
                "A roar rolled across the hills and the forest went silent. The grazers lifted their heads. Something on this island hunts, and it is not me.");
            // locations, discoveries, resources and creatures: AddBuiltIns
        }

        // ------------------------------------------------------------------ save (creature observations; page ids are in SaveData.journal)
        public string SectionKey => "creatures";
        public string CaptureSection() => Creatures.Capture();
        public void RestoreSection(string json) => Creatures.Restore(json);
    }
}
