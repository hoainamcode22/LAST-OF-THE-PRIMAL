using System;
using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;

namespace PrimalFrontier.Story
{
    /// <summary>
    /// Day Zero tutorial: 19 steps that teach by doing (look, walk, search, gather, craft, water, food, fire, spear,
    /// explore, tracks, observe, predator warning, return, shelter, survive the night). Each step completes from game
    /// events or a simple check, shows an objective + hint, and can be skipped as a whole in the settings.
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

        public static TutorialManager Instance { get; private set; }
        public readonly List<Step> steps = new List<Step>();
        public int Index { get; private set; } = -1;
        public bool Running => Index >= 0 && Index < steps.Count;
        public bool Completed { get; private set; }
        public Step Current => Running ? steps[Index] : null;
        public event Action<Step> StepStarted;
        public event Action Finished;
        /// <summary>fallback when no creature is present yet: seconds spent inside the herbivore habitat</summary>
        public float observeSeconds = 4f;

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
            S("spear", "Craft a Stone Spear", "A spear keeps danger at a distance.", e => Is(e, GameEventType.ItemCrafted, "stone_spear"), chk: () => Count("stone_spear") > 0);
            S("explore", "Explore the forest inland", "Follow the stream uphill towards the meadow.", e => Is(e, GameEventType.ZoneEntered, "ZONE_Meadow"));
            S("tracks", "Examine the strange tracks", "Something left deep prints in the mud near the meadow.", e => Is(e, GameEventType.FootprintFound));
            S("observe", "Observe the grazing giant", "Keep your distance. Watch, do not provoke.", e => Is(e, GameEventType.CreatureSighted, "triceratops"),
              chk: () => _habitatTime >= observeSeconds && !CreatureExists());
            S("return", "Something is nearby. Return to camp", "Head back to your fire on the beach.", chk: () => _player && NearCamp(_player.transform.position),
              begin: () => { GameEvents.Raise(GameEventType.PredatorWarning, "roar"); PredatorWarningCue?.Invoke(); });
            S("shelter", "Build a shelter before nightfall", "Craft a Basic Shelter and place it near the fire.", e => Is(e, GameEventType.StructurePlaced, "shelter"));
            S("night", "Survive the night", "Stay warm by the fire. A bedroll lets you sleep until dawn.", e => Is(e, GameEventType.DayStarted));
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
        public void Restore(int index, bool completed)
        {
            if (completed) { Index = steps.Count; Completed = true; return; }
            Index = Mathf.Clamp(index, 0, steps.Count);
            if (Running) StepStarted?.Invoke(steps[Index]);
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
