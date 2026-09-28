using System;
using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.Survival;
using PrimalFrontier.World;

namespace PrimalFrontier.Story
{
    /// <summary>
    /// Day Zero tutorial: steps that teach by doing (look, walk, search, gather, craft, water, food, fire, cook, then the
    /// survival milestone 1 steps: fill a container, boil it, drink clean water, build a tent, shelter before night,
    /// sleep; then spear, explore, tracks, observe, predator warning, return, shelter, survive the night). Each step
    /// completes from game events or a simple check, shows an objective + hint, and can be skipped as a whole in the
    /// settings. The save stores the current step by id (older saves: an index into <see cref="LegacyOrder"/>).
    /// </summary>
    public class TutorialManager : MonoBehaviour
    {
        public class Step
        {
            public string id, objective, hint;
            public Func<GameEvent, bool> onEvent;      // completes when true
            public Func<bool> check;                   // polled
            public Func<string> progress;              // "(2/4)"
            public Action onBegin;
        }

        [Serializable] public class StepText { public string id; public string objective; [TextArea(1, 3)] public string hint; }

        public static TutorialManager Instance { get; private set; }
        public readonly List<Step> steps = new List<Step>();
        [Tooltip("Objective / hint shown for each step. Edit the text freely (the step logic stays in code); an empty field uses the built-in text.")]
        public List<StepText> texts = new List<StepText>();
        public int Index { get; private set; } = -1;
        public bool Running => Index >= 0 && Index < steps.Count;
        public bool Completed { get; private set; }
        public Step Current => Running ? steps[Index] : null;
        public event Action<Step> StepStarted;
        public event Action Finished;
        /// <summary>fallback when no creature is present yet: seconds spent inside the herbivore habitat</summary>
        public float observeSeconds = 4f;
        [Tooltip("'Shelter before night' completes from this hour on (or at night) while the player is near a shelter")] public float eveningHour = 17f;
        [Tooltip("metres from a shelter / tent that count as 'at the shelter'")] public float shelterRange = 6f;

        /// <summary>survival milestone 1 step ids (compass targets, tests)</summary>
        public const string FillWaterStep = "fill_water", BoilWaterStep = "boil_water", DrinkCleanStep = "drink_clean",
                            TentStep = "tent", ShelterNightStep = "shelter_night", SleepStep = "sleep";
        /// <summary>the step order before milestone 1: saves without a step id store an index into this list</summary>
        public static readonly string[] LegacyOrder = { "look", "walk", "search", "wood", "stone", "open_craft", "craft_axe", "fiber", "water", "food",
                                                        "campfire", "cook", "spear", "explore", "tracks", "observe", "return", "shelter", "night" };
        /// <summary>id of the current step (null when idle or finished)</summary>
        public string CurrentId => Running ? steps[Index].id : null;
        public int IndexOf(string id) { if (string.IsNullOrEmpty(id)) return -1; for (int i = 0; i < steps.Count; i++) if (steps[i].id == id) return i; return -1; }

        GameObject _player; InventorySystem _inv; ThirdPersonCamera _cam;
        float _yawAcc; float _lastYaw; float _stepTime; float _habitatTime;
        public Func<Vector3, bool> NearCamp = p => false;
        /// <summary>where the current objective is (compass marker); null = no marker</summary>
        public Func<string, Vector3?> TargetFor = id => null;
        public Vector3? CurrentTarget => Running ? TargetFor(steps[Index].id) : null;
        public Func<Vector3, bool> InHerbivoreHabitat = p => false;
        public Action PredatorWarningCue;

        void Awake() { Instance = this; Build(); }
        void OnDestroy() { if (Instance == this) Instance = null; }
        void OnEnable() { GameEvents.Raised += OnEvent; }
        void OnDisable() { GameEvents.Raised -= OnEvent; }

        public void Bind(GameObject player)
        {
            _player = player; _inv = player ? player.GetComponent<InventorySystem>() : null;
            _cam = Camera.main ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
            if (_cam) _lastYaw = _cam.Yaw;
        }

        int Count(string id) { var db = ItemDatabase.Instance; var it = db ? db.Item(id) : null; return _inv && it ? _inv.Count(it) : 0; }
        static bool Is(GameEvent e, GameEventType t, string id = null) => e.type == t && (id == null || e.id == id);

        void Build()
        {
            steps.Clear();
            void S(string id, string obj, string hint, Func<GameEvent, bool> ev = null, Func<bool> chk = null, Func<string> prog = null, Action begin = null) =>
                steps.Add(new Step { id = id, objective = obj, hint = hint, onEvent = ev, check = chk, progress = prog, onBegin = begin });

            S("look", "Look around", "Move the mouse to look.", chk: () => _yawAcc > 100f);
            S("walk", "Walk to the wreck", "W A S D to move. Hold Shift to run faster.", e => Is(e, GameEventType.ZoneEntered, "ZONE_Shipwreck"));
            S("search", "Search the wreck for supplies", "Walk up to a crate and press E.", e => Is(e, GameEventType.LootOpened));
            S("wood", "Collect wood", "Driftwood lies along the beach. Press E next to it.", chk: () => Count("wood") >= 4, prog: () => $"({Mathf.Min(4, Count("wood"))}/4)");
            S("stone", "Collect stones", "Loose stones gather where the rocks meet the sand.", chk: () => Count("stone") >= 3, prog: () => $"({Mathf.Min(3, Count("stone"))}/3)");
            S("open_craft", "Open the crafting menu", "Press Q (or Tab, then the Crafting tab).", e => Is(e, GameEventType.MenuOpened, "crafting"));
            S("craft_axe", "Craft a Stone Axe", "Stone, wood and the ship's rope. Select it and press Craft.", e => Is(e, GameEventType.ItemCrafted, "stone_axe"), chk: () => Count("stone_axe") > 0);
            S("fiber", "Gather plant fibre", "Tall fibrous plants grow behind the beach. Fibre makes cord.", chk: () => Count("fiber") >= 5, prog: () => $"({Mathf.Min(5, Count("fiber"))}/5)");
            S("water", "Find fresh water", "The sea will not help you. Look for a stream or a pond inland.", e => Is(e, GameEventType.Drank));
            S("food", "Find something to eat", "Berry bushes grow along the forest edge.", e => Is(e, GameEventType.Ate));
            S("campfire", "Build a campfire", "Craft it, choose it on the hotbar (1-8) and place it with left click.", e => Is(e, GameEventType.StructurePlaced, "campfire"));
            S("cook", "Light the fire and cook meat", "Press E at the campfire with wood, then with raw meat.", e => Is(e, GameEventType.FoodCooked));
            // survival milestone 1: water -> boil -> clean water -> tent -> shelter before night -> sleep
            S(FillWaterStep, "Fill a container with water", "Hold a leaf cup or a gourd and press E at the pond, the stream or the sea.",
              e => Is(e, GameEventType.WaterFilled), chk: () => CarriesWater(WaterType.None));
            S(BoilWaterStep, "Boil water at the fire", "Press E at a lit campfire while holding the filled container. Pond and sea water come out clean.",
              e => Is(e, GameEventType.WaterBoiled), chk: () => CarriesWater(WaterType.CleanWater));
            S(DrinkCleanStep, "Drink clean water", "Choose the container on the hotbar and left click to drink.",
              e => Is(e, GameEventType.Drank, SurvivalConfig.Instance.Water(WaterType.CleanWater).eventId));
            S(TentStep, "Build a tent", "Craft a tent, choose it on the hotbar and place it near your fire.", chk: TentExists);
            S(ShelterNightStep, "Shelter before night", "Evening is coming and the night air is cold. Stay by your tent and fire.",
              chk: () => IsEvening() && _player && Shelter.Nearest(_player.transform.position, shelterRange) != null);
            S(SleepStep, "Sleep in the tent", "After sunset, press E at the tent to sleep until dawn.", e => Is(e, GameEventType.Slept));
            S("spear", "Craft a Stone Spear", "A spear keeps danger at a distance.", e => Is(e, GameEventType.ItemCrafted, "stone_spear"), chk: () => Count("stone_spear") > 0);
            S("explore", "Explore the forest inland", "Follow the stream uphill towards the meadow.", e => Is(e, GameEventType.ZoneEntered, "ZONE_Meadow"));
            S("tracks", "Examine the strange tracks", "Something left deep prints in the mud near the meadow.", e => Is(e, GameEventType.FootprintFound));
            S("observe", "Observe the grazing giant", "Keep your distance. Watch, do not provoke.", e => Is(e, GameEventType.CreatureSighted, "triceratops"),
              chk: () => _habitatTime >= observeSeconds && !CreatureExists());
            S("return", "Something is nearby. Return to camp", "Head back to your fire on the beach.", chk: () => _player && NearCamp(_player.transform.position),
              begin: () => { GameEvents.Raise(GameEventType.PredatorWarning, "roar"); PredatorWarningCue?.Invoke(); });
            S("shelter", "Build a shelter before nightfall", "Craft a Basic Shelter and place it near the fire.",
              e => Is(e, GameEventType.StructurePlaced, "shelter") || (e.type == GameEventType.StructurePlaced && PlacesShelter(e.id)), chk: TentExists);
            S("night", "Survive the night", "Stay warm by the fire. A bedroll lets you sleep until dawn.", e => Is(e, GameEventType.DayStarted));
            // text edited in the Inspector wins
            foreach (var t in texts)
            {
                if (t == null || string.IsNullOrEmpty(t.id)) continue;
                var st = steps.Find(x => x.id == t.id); if (st == null) continue;
                if (!string.IsNullOrEmpty(t.objective)) st.objective = t.objective;
                if (!string.IsNullOrEmpty(t.hint)) st.hint = t.hint;
            }
        }

        /// <summary>a water container in the pack holds water (of this kind; None = any kind)</summary>
        bool CarriesWater(WaterType kind)
        {
            if (!_inv || _inv.Slots == null) return false;
            for (int i = 0; i < _inv.Slots.Length; i++)
            {
                var s = _inv.Slots[i]; if (!WaterRules.IsContainer(s) || s.water <= 0) continue;
                if (kind == WaterType.None || WaterRules.TypeOf(s) == kind) return true;
            }
            return false;
        }
        /// <summary>a tent (a shelter with a bed on it) stands somewhere</summary>
        static bool TentExists()
        {
            var all = Shelter.All;
            for (int i = 0; i < all.Count; i++) if (all[i] && all[i].HasBed) return true;
            return false;
        }
        /// <summary>the placed item is a shelter of any kind (lean-to, tent): read from its prefab, no item ids</summary>
        static bool PlacesShelter(string itemId)
        {
            var db = ItemDatabase.Instance; var it = db ? db.Item(itemId) : null;
            return it && it.placePrefab && it.placePrefab.GetComponentInChildren<Shelter>(true);
        }
        bool IsEvening()
        {
            var tm = TimeManager.Instance; if (!tm) return false;
            return tm.hour >= eveningHour || tm.IsNight;
        }

        /// <summary>fill the text list with every step (keeps what was already edited). Inspector: right click the component.</summary>
        [ContextMenu("Fill step texts")]
        public void SyncTexts()
        {
            Build();
            foreach (var st in steps)
                if (!texts.Exists(t => t != null && t.id == st.id)) texts.Add(new StepText { id = st.id, objective = st.objective, hint = st.hint });
        }

        /// <summary>set by the dinosaur system once creatures exist in the world</summary>
        public static Func<bool> CreatureExists = () => false;

        public void Begin(int index = 0)
        {
            Completed = false; Index = Mathf.Clamp(index, 0, steps.Count); _stepTime = 0f; _habitatTime = 0f;
            if (_cam) _lastYaw = _cam.Yaw; _yawAcc = 0f;
            if (Running) { steps[Index].onBegin?.Invoke(); StepStarted?.Invoke(steps[Index]); GameEvents.Raise(GameEventType.ObjectiveChanged, steps[Index].id, Index); }
            else Finish();
        }

        public void Skip() { Index = steps.Count; Finish(); }
        /// <summary>new game / title: idle, nothing shown until Begin()</summary>
        public void ResetIdle() { Index = -1; Completed = false; _stepTime = 0f; _habitatTime = 0f; _yawAcc = 0f; }
        public void Restore(int index, bool completed) => Restore(index, completed, null);
        /// <summary>load: the step id wins; saves without one map their index through the pre-milestone-1 order</summary>
        public void Restore(int index, bool completed, string stepId)
        {
            if (completed) { Index = steps.Count; Completed = true; return; }
            int i = IndexOf(stepId);
            if (i < 0) i = LegacyIndex(index);
            Index = Mathf.Clamp(i, 0, steps.Count); _stepTime = 0f;
            if (Running) StepStarted?.Invoke(steps[Index]);
        }

        int LegacyIndex(int index)
        {
            if (index < 0) return 0;
            if (index >= LegacyOrder.Length) return steps.Count;
            int i = IndexOf(LegacyOrder[index]);
            return i >= 0 ? i : Mathf.Min(index, steps.Count);
        }

        void OnEvent(GameEvent e)
        {
            if (!Running) return;
            var s = steps[Index];
            if (s.onEvent != null && s.onEvent(e)) Advance();
        }

        void Update()
        {
            if (!Running) return;
            _stepTime += Time.deltaTime;
            if (_cam) { _yawAcc += Mathf.Abs(Mathf.DeltaAngle(_lastYaw, _cam.Yaw)); _lastYaw = _cam.Yaw; }
            if (_player && InHerbivoreHabitat(_player.transform.position)) _habitatTime += Time.deltaTime;
            var s = steps[Index];
            if (s.check != null && _stepTime > 0.3f && s.check()) Advance();
        }

        void Advance()
        {
            var done = steps[Index];
            GameEvents.Raise(GameEventType.TutorialStep, done.id, Index);
            Index++; _stepTime = 0f;
            if (Index >= steps.Count) { Finish(); return; }
            var s = steps[Index];
            s.onBegin?.Invoke();
            StepStarted?.Invoke(s);
            GameEvents.Raise(GameEventType.ObjectiveChanged, s.id, Index);
        }

        void Finish()
        {
            if (Completed) return;
            Completed = true;
            Finished?.Invoke();
            GameEvents.Raise(GameEventType.DayCompleted, "day_one", 1);
        }

        public string ObjectiveText()
        {
            var s = Current; if (s == null) return null;
            return s.objective + (s.progress != null ? " " + s.progress() : "");
        }
    }
}
