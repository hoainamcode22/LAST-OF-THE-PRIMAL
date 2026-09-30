using System;
using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.Survival;
using PrimalFrontier.UI;
using PrimalFrontier.World;

namespace PrimalFrontier.Story
{
    /// <summary>
    /// Chapter one teaching, folded into play: the old 25 tutorial steps are now lessons. A lesson is learned when the
    /// player does the thing (any order: drink, fill a cup, light a fire...). Its short tip (keys + a Vietnamese gloss, shown by
    /// ContextHints) appears only when it becomes relevant (thirsty, holding raw meat, evening without a shelter...), never
    /// twice, at most one tip per <see cref="minTipGapSeconds"/>, so the lessons spread over the first day instead of a
    /// checklist in the first minutes. The objective line belongs to MissionSystem now; this class keeps the old public
    /// members (Index = first lesson not learned, CurrentId, Restore by id / old index) so older saves and tools still work.
    /// Saved as the "lessons" section (older saves: the step id / index).
    /// </summary>
    public class TutorialManager : MonoBehaviour, ISaveSection
    {
        public class Step
        {
            public string id, objective, hint;
            public Func<GameEvent, bool> onEvent;      // learned when true
            public Func<bool> check;                   // polled: learned when true
            public Func<string> progress;              // legacy "(2/4)"
            public Action onBegin;
            /// <summary>tip is relevant now (null = no tip for this lesson)</summary>
            public Func<bool> tipWhen;
            public string tip;
        }

        [Serializable] public class TipText { public string id; [TextArea(1, 3)] public string text; }

        public static TutorialManager Instance { get; private set; }
        public readonly List<Step> steps = new List<Step>();
        [Tooltip("tip text per lesson id (edit freely; an empty text uses the built-in one, '-' switches the tip off)")]
        public List<TipText> tips = new List<TipText>();
        [Tooltip("real seconds between two first-day tips")] [Min(5)] public float minTipGapSeconds = 45f;
        [Tooltip("seconds of play before the first tip")] [Min(0)] public float firstTipDelay = 4f;
        public float observeSeconds = 4f;
        [Tooltip("'Shelter before night' is learned from this hour on (or at night) while the player is near a shelter")] public float eveningHour = 17f;
        [Tooltip("metres from a shelter / tent that count as 'at the shelter'")] public float shelterRange = 6f;

        /// <summary>survival milestone 1 lesson ids (tests, tools)</summary>
        public const string FillWaterStep = "fill_water", BoilWaterStep = "boil_water", DrinkCleanStep = "drink_clean",
                            TentStep = "tent", ShelterNightStep = "shelter_night", SleepStep = "sleep";
        /// <summary>the step order before milestone 1: saves without a step id store an index into this list</summary>
        public static readonly string[] LegacyOrder = { "look", "walk", "search", "wood", "stone", "open_craft", "craft_axe", "fiber", "water", "food",
                                                        "campfire", "cook", "spear", "explore", "tracks", "observe", "return", "shelter", "night" };
        const string TipPrefix = "ch1_";

        readonly HashSet<string> _learned = new HashSet<string>(StringComparer.Ordinal);
        bool _started;
        public int Index { get; private set; } = -1;
        public bool Running => _started && !Completed && Index >= 0 && Index < steps.Count;
        public bool Completed { get; private set; }
        public Step Current => Running ? steps[Index] : null;
        /// <summary>id of the first lesson not learned yet (null when idle or all learned)</summary>
        public string CurrentId => Running ? steps[Index].id : null;
        public event Action<Step> StepStarted;
        public event Action Finished;
        public bool IsLearned(string id) => id != null && _learned.Contains(id);
        public int IndexOf(string id) { if (string.IsNullOrEmpty(id)) return -1; for (int i = 0; i < steps.Count; i++) if (steps[i].id == id) return i; return -1; }

        GameObject _player; InventorySystem _inv; ThirdPersonCamera _cam; PlayerInteraction _pi; PlayerSurvival _sv; PlayerHealth _hp;
        float _yawAcc, _lastYaw, _habitatTime, _nextPoll, _nextTip, _playTime, _lastTipAt = -999f;
        Vector3 _startPos; bool _hasStart;
        readonly HashSet<GameEventType> _seenEvents = new HashSet<GameEventType>();
        public Func<Vector3, bool> NearCamp = p => false;
        public Func<Vector3, bool> InHerbivoreHabitat = p => false;
        /// <summary>the current objective's direction: the focused mission's marker (compass / map)</summary>
        public Vector3? CurrentTarget => MissionSystem.Instance ? MissionSystem.Instance.MarkerTarget : null;
        /// <summary>set by the dinosaur system once creatures exist in the world</summary>
        public static Func<bool> CreatureExists = () => false;

        void Awake() { Instance = this; Build(); }
        void OnDestroy() { if (Instance == this) Instance = null; }
        void OnEnable() { GameEvents.Raised -= OnEvent; GameEvents.Raised += OnEvent; SaveSystem.RegisterSection(this); }
        void OnDisable() { GameEvents.Raised -= OnEvent; SaveSystem.UnregisterSection(this); }

        public void Bind(GameObject player)
        {
            _player = player;
            _inv = player ? player.GetComponent<InventorySystem>() : null; _pi = player ? player.GetComponent<PlayerInteraction>() : null;
            _sv = player ? player.GetComponent<PlayerSurvival>() : null; _hp = player ? player.GetComponent<PlayerHealth>() : null;
            _cam = Camera.main ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
            if (_cam) _lastYaw = _cam.Yaw;
        }

        // ------------------------------------------------------------------ lessons
        int Count(string id) { var db = ItemDatabase.Instance; var it = db ? db.Item(id) : null; return _inv && it ? _inv.Count(it) : 0; }
        static bool Is(GameEvent e, GameEventType t, string id = null) => e.type == t && (id == null || e.id == id);
        static string K(string action, string fallback) => Accent("[" + (KeyNames.Of(action) ?? fallback) + "]");
        static string Accent(string s) => "<color=#" + ColorUtility.ToHtmlStringRGB(UIStyle.Accent) + ">" + s + "</color>";
        static float Hour => TimeManager.Instance ? TimeManager.Instance.hour : 9f;
        static int Day => TimeManager.Instance ? TimeManager.Instance.day : 1;
        bool Seen(GameEventType t) => _seenEvents.Contains(t);

        void Build()
        {
            steps.Clear();
            Step S(string id, string obj, string hint, Func<GameEvent, bool> ev = null, Func<bool> chk = null, Func<bool> tipWhen = null, string tip = null)
            { var s = new Step { id = id, objective = obj, hint = hint, onEvent = ev, check = chk, tipWhen = tipWhen, tip = tip }; steps.Add(s); return s; }
            string E = K("Interact", "E"), LMB = K("Attack", "LMB"), RMB = K("Aim", "RMB"), C = K("Crouch", "C"), Q = K("Craft", "Q"), J = K("Journal", "J");

            S("look", "Look around", "Move the mouse to look.", chk: () => _yawAcc > 100f);
            S("walk", "Walk to the wreck", "W A S D to move, Shift to run.", e => Is(e, GameEventType.ZoneEntered) && StoryIds.Location(e.id) == "shipwreck", chk: () => Moved(25f),
              tipWhen: () => _playTime > firstTipDelay, tip: "Mouse to look around, " + Accent("[W A S D]") + " to walk, " + K("Sprint", "Shift") + " to run (Nhìn: chuột, đi: WASD, chạy: Shift)");
            S("search", "Search the wreck", "Walk up to a crate and press E.", e => Is(e, GameEventType.LootOpened),
              tipWhen: () => _pi && _pi.Prompt != null, tip: E + " searches crates and picks things up (E: lục soát, nhặt đồ)");
            S("wood", "Collect wood", "Driftwood lies along the beach.", e => Is(e, GameEventType.ItemAdded, "wood"), chk: () => Count("wood") > 0,
              tipWhen: () => IsLearned("search") || _playTime > 60f, tip: "Hold " + E + " to keep gathering: driftwood, stones, plants (Giữ E để thu thập liên tục)");
            S("stone", "Collect stones", "Loose stones gather where the rocks meet the sand.", e => Is(e, GameEventType.ItemAdded, "stone"), chk: () => Count("stone") > 0);
            S("open_craft", "Open the crafting menu", "Press Q.", e => Is(e, GameEventType.MenuOpened, "crafting"),
              tipWhen: () => Count("wood") > 0 && Count("stone") > 0 && !OnboardingTips.IsSeen(OnboardingTips.Wood), tip: Q + " opens crafting: stone, wood and cord make a stone axe (Q: chế tạo rìu đá)");
            S("craft_axe", "Craft a Stone Axe", "Stone, wood and rope.", e => Is(e, GameEventType.ItemCrafted, "stone_axe"), chk: () => Count("stone_axe") > 0);
            S("fiber", "Gather plant fibre", "Fibrous plants grow behind the beach.", e => Is(e, GameEventType.ItemAdded, "fiber"), chk: () => Count("fiber") > 0,
              tipWhen: () => Hour >= 10.5f || Day > 1, tip: "Tall fibrous plants give fibre; twisted, it becomes cord (Cây có sợi cho sợi, bện thành dây)");
            S("water", "Find fresh water", "Look for a stream or a pond inland.", e => Is(e, GameEventType.Drank) && e.id != SaltId,
              tipWhen: () => (_sv && _sv.Thirst < 70f) || Hour >= 11.5f || NearFreshWater(8f), tip: E + " at a stream or pond drinks with your hands. Never the sea (Uống ở suối / ao, không uống nước biển)");
            S("food", "Find something to eat", "Berry bushes grow along the forest edge.", e => Is(e, GameEventType.Ate),
              tipWhen: () => (_sv && _sv.Hunger < 70f) || Hour >= 12f || HasFood(), tip: "Hold food and press " + LMB + " to eat; berries grow at the forest edge (Cầm đồ ăn, nhấn chuột trái để ăn)");
            S("campfire", "Build a campfire", "Craft it and place it.", e => Is(e, GameEventType.StructurePlaced, "campfire"), chk: () => Campfire.All.Count > 0,
              tipWhen: () => Count("campfire") > 0 || Hour >= 13.5f || Day > 1, tip: "Craft a campfire, pick it on the hotbar " + K("Hotbar1", "1") + "-" + K("Hotbar8", "8") + " and place it with " + LMB + " (Đặt lửa trại: chọn trên thanh, nhấn chuột trái)");
            S("cook", "Cook meat", "Raw meat on a lit fire.", e => Is(e, GameEventType.FoodCooked),
              tipWhen: () => Count("raw_meat") > 0 || Count("raw_fish") > 0, tip: "Raw meat is a gamble: " + E + " at a lit fire cooks it (Thịt sống dễ gây bệnh: nấu ở lửa)");
            // survival milestone 1: water -> boil -> clean water -> tent -> shelter before night -> sleep
            S(FillWaterStep, "Fill a container with water", "Hold a leaf cup or a gourd and press E at water.", e => Is(e, GameEventType.WaterFilled), chk: () => CarriesWater(WaterType.None),
              tipWhen: () => HasEmptyContainer(), tip: "Hold a leaf cup or gourd and press " + E + " at water to fill it (Cầm cốc lá / bầu, nhấn E ở chỗ nước)");
            S(BoilWaterStep, "Boil water at the fire", "Hold the container and press E at a lit campfire.", e => Is(e, GameEventType.WaterBoiled), chk: () => CarriesWater(WaterType.CleanWater),
              tipWhen: () => (CarriesWater(WaterType.DirtyWater) || CarriesWater(WaterType.SaltWater)) && _player && Campfire.LitNear(_player.transform.position, 15f),
              tip: "Hold the filled container and press " + E + " at a lit fire to boil it safe (Đun sôi nước ở lửa cho sạch)");
            S(DrinkCleanStep, "Drink clean water", "Choose the container and left click.", e => Is(e, GameEventType.Drank, CleanId),
              tipWhen: () => CarriesWater(WaterType.CleanWater) && _sv && _sv.Thirst < 85f, tip: "Choose the container on the hotbar and press " + LMB + " to drink (Chọn bình, nhấn chuột trái để uống)");
            S(TentStep, "Build a tent", "Craft a tent and place it near your fire.", chk: TentExists,
              tipWhen: () => (Hour >= 15.5f || Day > 1) && Shelter.All.Count == 0, tip: "Craft a shelter or a tent and place it near your fire before dark (Dựng lều gần lửa trước khi trời tối)");
            S(ShelterNightStep, "Shelter before night", "Stay by your tent and fire.", chk: () => IsEvening() && _player && Shelter.Nearest(_player.transform.position, shelterRange) != null);
            S(SleepStep, "Sleep in the tent", "After sunset, press E at the tent.", e => Is(e, GameEventType.Slept),
              tipWhen: () => IsEvening() && _player && Shelter.Nearest(_player.transform.position, 10f, true) != null, tip: "After sunset, " + E + " at the tent sleeps until dawn (Sau hoàng hôn, nhấn E ở lều để ngủ)");
            S("spear", "Craft a Stone Spear", "A spear keeps danger at a distance.", e => Is(e, GameEventType.ItemCrafted, "stone_spear"), chk: () => Count("stone_spear") > 0,
              tipWhen: () => Seen(GameEventType.PredatorWarning) || Day > 1, tip: "A stone spear keeps danger at arm's length: craft one before going inland (Làm giáo đá trước khi vào sâu)");
            S("explore", "Explore inland", "Follow the stream uphill.", e => Is(e, GameEventType.ZoneEntered) && StoryIds.LocationMatches(e.id, "meadow|river|forest|pond"));
            S("tracks", "Examine the tracks", "Deep prints in the mud.", e => Is(e, GameEventType.FootprintFound) || StoryIds.EventIs(e.type, "TracksFound"));
            S("observe", "Observe a grazer", "Keep your distance.", e => Is(e, GameEventType.CreatureSighted),
              chk: () => _habitatTime >= observeSeconds && !CreatureExists(),
              tipWhen: () => Seen(GameEventType.CreatureSighted) && !OnboardingTips.IsSeen(OnboardingTips.Dinosaur), tip: C + " crouch and move slowly: grazers ignore a quiet watcher (C: cúi người, đi chậm)");
            S("return", "Rest at camp", "Rest at a shelter.", e => Is(e, GameEventType.Slept),
              tipWhen: () => _player && Shelter.Nearest(_player.transform.position, 10f) != null, tip: E + " at a shelter rests: the game saves and you wake there after a bad fall (Nghỉ ở lều: lưu game, hồi sinh tại đó)");
            S("shelter", "Build a shelter", "Place it near the fire.", e => e.type == GameEventType.StructurePlaced && PlacesShelter(e.id), chk: () => Shelter.All.Count > 0);
            S("night", "Survive the night", "Stay warm by the fire.", e => Is(e, GameEventType.DayStarted));
            // appended lessons (not in the old order)
            S("journal", "Read the journal", "Press J.", e => Is(e, GameEventType.MenuOpened, "journal"),
              tipWhen: () => JournalSystem.Instance && JournalSystem.Instance.UnlockedInOrder.Count >= 2, tip: J + " opens your journal: what you learn is written there (J: nhật ký)");
            S("map", "Open the map", "Press M.", chk: () => _mapOpened,
              tipWhen: () => ZoneManager.Instance && CountVisited() >= 3, tip: Accent("[M]") + " opens the map. It shows only the places you have been (M: bản đồ)");
            S("bandage", "Dress a wound", "Use a bandage.", e => Is(e, GameEventType.ItemUsed, "bandage"),
              tipWhen: () => _hp && _hp.IsBleeding, tip: "Bleeding: craft a bandage from fibre, hold it and press " + LMB + " (Chảy máu: làm băng từ sợi, nhấn chuột trái để dùng)");
            S("stealth", "Stay unseen", "Crouch in cover.", e => Is(e, GameEventType.Crouched),
              tipWhen: () => Seen(GameEventType.PlayerNoticed), tip: "Something noticed you. " + C + " crouch in the ferns and keep still (Bị phát hiện: cúi người trong bụi, đứng yên)");
            // tip text edited in the Inspector wins ('-' = no tip)
            foreach (var t in tips)
            {
                if (t == null || string.IsNullOrEmpty(t.id) || string.IsNullOrEmpty(t.text)) continue;
                var st = steps.Find(x => x.id == t.id); if (st == null) continue;
                st.tip = t.text == "-" ? null : t.text;
            }
        }

        static string SaltId => SurvivalConfig.Instance.Water(WaterType.SaltWater).eventId;
        static string CleanId => SurvivalConfig.Instance.Water(WaterType.CleanWater).eventId;
        bool _mapOpened;
        bool Moved(float m) => _player && _hasStart && (_player.transform.position - _startPos).sqrMagnitude > m * m;
        bool NearFreshWater(float r) => _player && WaterSource.IsNearFresh(_player.transform.position, r);
        int CountVisited() { int n = 0; foreach (var _ in ZoneManager.Instance.Visited) n++; return n; }

        bool HasFood()
        {
            if (!_inv || _inv.Slots == null) return false;
            for (int i = 0; i < _inv.Slots.Length; i++) { var s = _inv.Slots[i]; if (!s.IsEmptyOrNull() && s.item.IsFood && !s.item.IsWaterContainer && !s.item.IsMedical) return true; }
            return false;
        }
        bool HasEmptyContainer()
        {
            if (!_inv || _inv.Slots == null) return false;
            for (int i = 0; i < _inv.Slots.Length; i++) { var s = _inv.Slots[i]; if (WaterRules.IsContainer(s) && s.water <= 0) return true; }
            return false;
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
        static bool TentExists()
        {
            var all = Shelter.All;
            for (int i = 0; i < all.Count; i++) if (all[i] && all[i].HasBed) return true;
            return false;
        }
        static bool PlacesShelter(string itemId)
        {
            var db = ItemDatabase.Instance; var it = db ? db.Item(itemId) : null;
            return it && it.placePrefab && it.placePrefab.GetComponentInChildren<Shelter>(true);
        }
        bool IsEvening() { var tm = TimeManager.Instance; return tm && (tm.hour >= eveningHour || tm.IsNight); }

        /// <summary>fill the tip text list with every lesson (keeps texts already edited). Inspector: right click the component.</summary>
        [ContextMenu("Fill tip texts")]
        public void SyncTexts()
        {
            Build();
            foreach (var st in steps)
                if (st.tip != null && !tips.Exists(t => t != null && t.id == st.id)) tips.Add(new TipText { id = st.id, text = st.tip });
        }

        // ------------------------------------------------------------------ flow
        /// <summary>new game after the intro: lessons from the start (index &gt; 0 = the old steps before it count as learned)</summary>
        public void Begin(int index = 0)
        {
            _learned.Clear(); _seenEvents.Clear(); _mapOpened = false; _announced = false;
            for (int i = 0; i < index && i < steps.Count; i++) _learned.Add(steps[i].id);
            _started = true; Completed = false; _yawAcc = 0f; _habitatTime = 0f; _playTime = 0f; _lastTipAt = -999f;
            if (_cam) _lastYaw = _cam.Yaw;
            if (_player) { _startPos = _player.transform.position; _hasStart = true; }
            RefreshIndex();
            if (Running) { StepStarted?.Invoke(steps[Index]); GameEvents.Raise(GameEventType.ObjectiveChanged, steps[Index].id, Index); }
        }

        /// <summary>no more tips this game (every lesson counts as learned)</summary>
        public void Skip() { foreach (var s in steps) _learned.Add(s.id); RefreshIndex(); if (!_announced) { _announced = true; Finished?.Invoke(); } }
        bool _announced;
        /// <summary>new game / title: idle, nothing shown until Begin()</summary>
        public void ResetIdle() { _learned.Clear(); _seenEvents.Clear(); _started = false; Completed = false; Index = -1; _yawAcc = 0f; _habitatTime = 0f; _playTime = 0f; _hasStart = false; }
        public void Restore(int index, bool completed) => Restore(index, completed, null);
        /// <summary>load (old saves): the step id wins; the lessons before it count as learned. The "lessons" section, when present, replaces this afterwards.</summary>
        public void Restore(int index, bool completed, string stepId)
        {
            _started = true; _learned.Clear();
            if (completed) { foreach (var s in steps) _learned.Add(s.id); Completed = true; Index = steps.Count; return; }
            Completed = false;
            int i = IndexOf(stepId);
            if (i < 0) i = LegacyIndex(index);
            for (int k = 0; k < i && k < steps.Count; k++) _learned.Add(steps[k].id);
            RefreshIndex();
        }

        int LegacyIndex(int index)
        {
            if (index < 0) return 0;
            if (index >= LegacyOrder.Length) return steps.Count;
            int i = IndexOf(LegacyOrder[index]);
            return i >= 0 ? i : Mathf.Min(index, steps.Count);
        }

        void RefreshIndex()
        {
            int i = 0; while (i < steps.Count && _learned.Contains(steps[i].id)) i++;
            Index = i;
            if (_started && i >= steps.Count) Completed = true;       // quietly: the chapter card belongs to MissionSystem
        }

        void Learn(Step s)
        {
            if (!_learned.Add(s.id)) return;
            GameEvents.Raise(GameEventType.TutorialStep, s.id, IndexOf(s.id));
            RefreshIndex();
        }

        void OnEvent(GameEvent e)
        {
            _seenEvents.Add(e.type);
            if (!_started) return;
            for (int i = 0; i < steps.Count; i++)
            {
                var s = steps[i];
                if (s.onEvent == null || _learned.Contains(s.id)) continue;
                bool ok; try { ok = s.onEvent(e); } catch (Exception ex) { Debug.LogException(ex); ok = false; }
                if (ok) Learn(s);
            }
        }

        /// <summary>called by GameManager when the player rested at a shelter (the "return" lesson)</summary>
        public void NotifyRested() { var s = steps.Find(x => x.id == "return"); if (s != null && _started) Learn(s); }
        /// <summary>called by the minimap when the big map opens</summary>
        public void NotifyMapOpened() { _mapOpened = true; }

        void Update()
        {
            if (!_started) return;
            float dt = Time.deltaTime;
            _playTime += dt;
            if (_cam) { _yawAcc += Mathf.Abs(Mathf.DeltaAngle(_lastYaw, _cam.Yaw)); _lastYaw = _cam.Yaw; }
            if (!_hasStart && _player) { _startPos = _player.transform.position; _hasStart = true; }
            if (_player && InHerbivoreHabitat(_player.transform.position)) _habitatTime += dt;
            if (Time.time >= _nextPoll)
            {
                _nextPoll = Time.time + 0.5f;
                for (int i = 0; i < steps.Count; i++)
                {
                    var s = steps[i];
                    if (s.check == null || _learned.Contains(s.id)) continue;
                    bool ok; try { ok = s.check(); } catch (Exception ex) { Debug.LogException(ex); ok = false; }
                    if (ok) Learn(s);
                }
            }
            if (Time.unscaledTime >= _nextTip) { _nextTip = Time.unscaledTime + 1f; TryTip(); }
        }

        /// <summary>one relevant tip, when the last one is long enough ago and nothing else is on screen</summary>
        void TryTip()
        {
            var gm = GameManager.Instance; if (gm && gm.State != GameState.Playing) return;
            var hints = ContextHints.Instance; if (!hints || hints.CurrentTip != null || hints.QueuedTips > 0) return;
            if (UIManager.Instance && UIManager.Instance.Current != UIScreen.None) return;
            if (Time.unscaledTime - _lastTipAt < minTipGapSeconds) return;
            for (int i = 0; i < steps.Count; i++)
            {
                var s = steps[i];
                if (s.tip == null || s.tipWhen == null || _learned.Contains(s.id) || OnboardingTips.IsSeen(TipPrefix + s.id)) continue;
                bool want; try { want = s.tipWhen(); } catch { want = false; }
                if (!want) continue;
                if (hints.QueueTip(TipPrefix + s.id, s.tip)) { _lastTipAt = Time.unscaledTime; return; }
            }
        }

        /// <summary>legacy: text of the first lesson not learned (the HUD objective line now comes from MissionSystem)</summary>
        public string ObjectiveText()
        {
            var s = Current; if (s == null) return null;
            return s.objective + (s.progress != null ? " " + s.progress() : "");
        }

        // ------------------------------------------------------------------ save
        [Serializable] class Saved { public List<string> learned = new List<string>(); public bool done; }
        public string SectionKey => "lessons";
        public string CaptureSection() { if (!_started) return null; var d = new Saved { done = Completed }; d.learned.AddRange(_learned); return JsonUtility.ToJson(d); }
        public void RestoreSection(string json)
        {
            var d = JsonUtility.FromJson<Saved>(json); if (d == null) return;
            _started = true; _learned.Clear(); Completed = false;
            if (d.learned != null) foreach (var id in d.learned) if (!string.IsNullOrEmpty(id)) _learned.Add(id);
            RefreshIndex();
            if (d.done) Completed = true;
        }
    }
}
