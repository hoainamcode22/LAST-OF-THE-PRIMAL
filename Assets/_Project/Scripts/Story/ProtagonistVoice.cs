using System;
using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Audio;
using PrimalFrontier.Core;
using PrimalFrontier.Player;
using PrimalFrontier.Survival;
using PrimalFrontier.UI;
using PrimalFrontier.World;

namespace PrimalFrontier.Story
{
    /// <summary>
    /// The survivor's few spoken thoughts, as subtitles (hand font): "That's fresh water.", "I need shelter.", "What made
    /// those tracks?", "That sound...". Each line has a trigger (GameEvents names + ids, or a watched situation by its id)
    /// and is said once per game unless repeatHours is set; at least <see cref="minGapSeconds"/> between two lines so the
    /// survivor is never chatty. Story lines (missions, examining a prop) are "important": they wait for a short gap instead
    /// of being dropped. Fear lines come with a breath. Only while playing (never in the intro, menus, sleep or death).
    /// Saved as the "voice" section (lines already said).
    /// </summary>
    public class ProtagonistVoice : MonoBehaviour, ISaveSection
    {
        [Serializable]
        public class Line
        {
            public string id;
            [TextArea(1, 2)] public string text;
            [Tooltip("GameEventType names that trigger it ('|'); empty = a watched situation named by the id (fresh_water, dark, need_shelter, hungry, thirsty, cold)")]
            public string events;
            [Tooltip("event ids ('|', '*' = prefix); empty = any")] public string ids;
            [Tooltip("0 = once per game; > 0 = may come again after this many in-game hours")] [Min(0)] public float repeatHours;
            [Tooltip("a sharp breath with the line")] public bool fear;
        }

        public static ProtagonistVoice Instance { get; private set; }
        [Tooltip("every line (empty = the built-in ones). Edit the texts freely.")] public List<Line> lines = new List<Line>();
        [Tooltip("real seconds between two ordinary lines")] [Min(0)] public float minGapSeconds = 25f;
        [Tooltip("real seconds between an important line and the one before")] [Min(0)] public float importantGapSeconds = 4f;
        [Tooltip("seconds a subtitle stays")] [Min(1)] public float seconds = 3.4f;
        [Tooltip("the situation lines stop after this day (dark, cold...)")] public int situationDays = 3;

        readonly Dictionary<string, double> _said = new Dictionary<string, double>(StringComparer.Ordinal);
        float _lastAt = -999f, _nextPoll;
        string _pendingKey, _pendingText; bool _pendingFear; float _pendingUntil;
        PlayerSurvival _sv; Transform _svOwner;

        void Awake() { Instance = this; if (lines.Count == 0) lines = Defaults(); }
        void OnEnable() { GameEvents.Raised -= OnEvent; GameEvents.Raised += OnEvent; SaveSystem.RegisterSection(this); }
        void OnDisable() { GameEvents.Raised -= OnEvent; SaveSystem.UnregisterSection(this); }
        void OnDestroy() { if (Instance == this) Instance = null; }

        public static List<Line> Defaults()
        {
            var l = new List<Line>();
            void A(string id, string text, string events = null, string ids = null, float repeat = 0f, bool fear = false) =>
                l.Add(new Line { id = id, text = text, events = events, ids = ids, repeatHours = repeat, fear = fear });
            // watched situations (no event)
            A("fresh_water", "That's fresh water.");
            A("need_shelter", "I need shelter.");
            A("dark", "It's getting dark.", repeat: 20f);
            A("hungry", "I need to eat something.");
            A("thirsty", "My throat is burning.");
            A("cold", "So cold...");
            // events
            A("wreck", "Anyone...? Anyone at all?", "ZoneEntered", "shipwreck");
            A("tracks", "What made those tracks?", "FootprintFound|TracksFound", fear: true);
            A("sound", "That sound...", "PredatorWarning", fear: true);
            A("first_rain", "Rain. I need to stay dry.", "WeatherChanged", "Rain|Storm");
            A("first_injury", "I'm hurt. I need to be careful.", "StatusApplied", "bleeding|leg_injury|arm_injury", fear: true);
            A("first_predator", "Don't move. Don't breathe.", "CreatureSighted", "velociraptor|carnotaurus|spinosaurus|apex", fear: true);
            A("first_dinosaur", "What... is that?", "CreatureSighted", fear: true);
            A("first_fire", "Warmth. Finally.", "FireLit");
            A("first_night", "Stay close to the fire.", "NightStarted");
            A("salt", "Salt... I can't drink this.", "TriedSaltWater");
            A("sick", "My stomach...", "GotSick");
            A("waterfall", "So this is where the river begins.", "ZoneEntered", "waterfall");
            A("herd", "So many of them...", "HerdSighted|CreatureObserved", "herd:*");
            A("migration", "The ground is shaking...", "MigrationStarted");
            A("old_camp", "Someone else was here.", "ZoneEntered|Discovery", "old_camp|old_shelter*|old_firepit*|old_tools*|old_camp_tally*");
            A("cave", "Cold air... from deep inside.", "ZoneEntered", "cave");
            A("volcano", "The ground is warm here.", "ZoneEntered|HazardWarning", "volcano|lava*");
            A("respawn", "Still breathing. Barely.", "PlayerRespawned");
            A("belongings", "My things. Still here.", "BelongingsRecovered");
            return l;
        }

        // ------------------------------------------------------------------ API
        /// <summary>say a line by its id (situations); false when it is not due or the survivor spoke too recently</summary>
        public static bool Say(string id) { var v = Instance; return v && v.TrySay(id, false); }
        /// <summary>say a story text (mission, examined prop); important ones wait for a short gap instead of being dropped</summary>
        public static void SayText(string key, string text, bool important, bool force = false)
        {
            var v = Instance;
            if (!v) { if (force && !string.IsNullOrEmpty(text)) PlayerInteraction.Notify(text); return; }
            v.Speak(key, text, important, false, force);
        }
        public bool WasSaid(string id) => id != null && _said.ContainsKey(id);
        public void ResetAll() { _said.Clear(); _pendingKey = null; _lastAt = -999f; }

        bool CanSpeakNow()
        {
            var gm = GameManager.Instance;
            if (gm && (gm.State != GameState.Playing || !gm.IntroDone)) return false;
            return !UIManager.Instance || UIManager.Instance.Current == UIScreen.None;
        }

        Line Find(string id) { for (int i = 0; i < lines.Count; i++) if (lines[i] != null && lines[i].id == id) return lines[i]; return null; }

        bool Due(Line l)
        {
            if (!_said.TryGetValue(l.id, out var at)) return true;
            return l.repeatHours > 0f && GameClock.Now - at >= GameClock.Hours(l.repeatHours);
        }

        bool TrySay(string id, bool important)
        {
            var l = Find(id); if (l == null || string.IsNullOrEmpty(l.text) || !Due(l)) return false;
            return Speak(l.id, l.text, important, l.fear, false);
        }

        bool Speak(string key, string text, bool important, bool fear, bool force)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (!force && !CanSpeakNow())
            {
                if (important) { _pendingKey = key; _pendingText = text; _pendingFear = fear; _pendingUntil = Time.unscaledTime + 30f; }
                return false;
            }
            float since = Time.unscaledTime - _lastAt;
            if (!force && since < (important ? importantGapSeconds : minGapSeconds))
            {
                if (important) { _pendingKey = key; _pendingText = text; _pendingFear = fear; _pendingUntil = Time.unscaledTime + 20f; }
                return false;
            }
            var hud = HUDManager.Instance; if (!hud) return false;
            hud.ShowSubtitle(text, Mathf.Max(seconds, 1.2f + text.Length * 0.055f), true);
            if (fear) SfxPlayer.Instance.Play2D(SfxId.BreathIn, 0.55f);
            _lastAt = Time.unscaledTime;
            if (!string.IsNullOrEmpty(key)) _said[key] = GameClock.Now;
            if (key == _pendingKey) _pendingKey = null;
            return true;
        }

        // ------------------------------------------------------------------ triggers
        void OnEvent(GameEvent e)
        {
            if (!CanSpeakNow()) return;
            if (e.type == GameEventType.ZoneEntered && e.amount == 0) return;          // only the first visit of a place
            for (int i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                if (l == null || string.IsNullOrEmpty(l.events) || !StoryIds.EventIs(e.type, l.events) || !Due(l)) continue;
                if (!IdMatches(l, e)) continue;
                if (Speak(l.id, l.text, false, l.fear, false)) return;
                // a first-of-its-kind line that could not be said now (too soon after another) is kept for the next occasion
            }
        }

        static bool IdMatches(Line l, GameEvent e)
        {
            if (string.IsNullOrEmpty(l.ids)) return true;
            // "behaviour:species" patterns only filter CreatureObserved; the same line's other events (HerdSighted) match any id
            if (e.type != GameEventType.CreatureObserved && l.ids.IndexOf(':') >= 0) return true;
            if (e.type == HazardEvent && e.amount <= 0) return false;                  // hazard level 0 = safe again
            switch (e.type)
            {
                case GameEventType.ZoneEntered: return StoryIds.LocationMatches(e.id, l.ids);
                case GameEventType.Discovery: case GameEventType.FootprintFound: return StoryIds.Matches(StoryIds.Discovery(e.id), l.ids);
                default: return StoryIds.Matches(e.id, l.ids);
            }
        }

        static readonly GameEventType HazardEvent = StoryIds.TryEvent("HazardWarning", out var t) ? t : GameEventType.None;

        void Update()
        {
            if (_pendingKey != null && Time.unscaledTime > _pendingUntil) _pendingKey = null;
            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + 0.5f;
            if (!CanSpeakNow()) return;
            if (_pendingKey != null) { Speak(_pendingKey, _pendingText, true, _pendingFear, false); return; }
            if (Time.unscaledTime - _lastAt < minGapSeconds) return;
            var player = PlayerLocator.Player; if (!player) return;
            var tm = TimeManager.Instance; int day = tm ? tm.day : 1;
            if (player != _svOwner) { _svOwner = player; _sv = player.GetComponent<PlayerSurvival>(); }
            // fresh water in view, close
            if (Due("fresh_water") && FreshWaterInView(player.position, 14f)) { TrySay("fresh_water", false); return; }
            if (day > situationDays) return;
            if (tm)
            {
                if (tm.hour >= tm.sunsetHour - 0.6f && tm.hour < tm.sunsetHour + 1f && TrySay("dark", false)) return;
                if (day == 1 && tm.hour >= 16f && Shelter.All.Count == 0 && TrySay("need_shelter", false)) return;
            }
            if (_sv)
            {
                if (_sv.Thirst < 30f && TrySay("thirsty", false)) return;
                if (_sv.Hunger < 30f && TrySay("hungry", false)) return;
                if (_sv.IsCold && TrySay("cold", false)) return;
            }
        }
        bool Due(string id) { var l = Find(id); return l != null && Due(l); }

        static bool FreshWaterInView(Vector3 p, float range)
        {
            var cam = Camera.main;
            foreach (var w in WaterSource.All)
            {
                if (!w || !w.fresh) continue;
                if (!MissionSystem.WaterPoint(w, p, out var at)) continue;
                var d = at - p; d.y = 0f; if (d.sqrMagnitude > range * range) continue;
                if (cam && d.sqrMagnitude > 9f && Vector3.Dot(cam.transform.forward, (at - cam.transform.position).normalized) < 0.35f) continue;
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ save
        [Serializable] class Saved { public List<string> ids = new List<string>(); public List<double> at = new List<double>(); }
        public string SectionKey => "voice";
        public string CaptureSection()
        {
            if (_said.Count == 0) return null;
            var d = new Saved(); foreach (var kv in _said) { d.ids.Add(kv.Key); d.at.Add(kv.Value); }
            return JsonUtility.ToJson(d);
        }
        public void RestoreSection(string json)
        {
            var d = JsonUtility.FromJson<Saved>(json); if (d == null || d.ids == null) return;
            _said.Clear();
            for (int i = 0; i < d.ids.Count; i++) if (!string.IsNullOrEmpty(d.ids[i])) _said[d.ids[i]] = d.at != null && i < d.at.Count ? d.at[i] : 0;
        }
    }
}
