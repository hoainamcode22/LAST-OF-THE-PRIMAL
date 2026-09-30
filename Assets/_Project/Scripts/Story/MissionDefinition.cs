using System;
using System.Collections.Generic;
using UnityEngine;

namespace PrimalFrontier.Story
{
    public enum MissionDeadline { None, Sunset, Nightfall, Hour }
    public enum MissionMarker { None, Location, NearestFreshWater, Camp, Shelter }
    public enum ConditionKind { Event, Location, NearLocation, Check, Hour, MissionDone }
    public enum MatchMode { Any, All }

    /// <summary>
    /// One rule of a mission. Event: GameEventType names ("Drank|WaterFilled", names another system has not added yet are
    /// ignored) with ids ("dirty_water|clean_water", '*' = prefix); count = how many times (distinct = different ids only).
    /// Location: the player has entered one of the location ids (also counts places visited before the mission began).
    /// NearLocation: within radius metres of a location marker. Check: a named rule (MissionSystem.Check: shelter_exists,
    /// near_fresh_water, fire_lit, at_camp, far_from_camp, prepared, has_item, creatures_known, discoveries, night, dusk, day).
    /// Hour: the clock is between hourFrom and hourTo (wraps past midnight). MissionDone: missions (ids) are done.
    /// </summary>
    [Serializable]
    public class MissionCondition
    {
        public ConditionKind kind;
        [Tooltip("Event: GameEventType names, '|' between alternatives")] public string events;
        [Tooltip("ids to match ('|' alternatives, '*' at the end = prefix); empty = any. Location / MissionDone: the ids")] public string ids;
        [Tooltip("Check: rule name")] public string check;
        [Min(1)] public int count = 1;
        [Tooltip("Event: count different ids only")] public bool distinct;
        [Tooltip("NearLocation: metres; Check at_camp / far_from_camp / near_fresh_water: metres (0 = rule default)")] public float radius;
        [Range(0, 24)] public float hourFrom, hourTo;

        public static MissionCondition Ev(string events, string ids = null, int count = 1, bool distinct = false) =>
            new MissionCondition { kind = ConditionKind.Event, events = events, ids = ids, count = count, distinct = distinct };
        public static MissionCondition Loc(string ids) => new MissionCondition { kind = ConditionKind.Location, ids = ids };
        public static MissionCondition Near(string id, float radius) => new MissionCondition { kind = ConditionKind.NearLocation, ids = id, radius = radius };
        public static MissionCondition Chk(string check, string ids = null, int count = 1, float radius = 0f) =>
            new MissionCondition { kind = ConditionKind.Check, check = check, ids = ids, count = count, radius = radius };
        public static MissionCondition Hours(float from, float to) => new MissionCondition { kind = ConditionKind.Hour, hourFrom = from, hourTo = to };
        public static MissionCondition Done(string ids) => new MissionCondition { kind = ConditionKind.MissionDone, ids = ids };
    }

    /// <summary>
    /// A mission (main or optional), as data. Starts when every mission in <see cref="requires"/> is done, its chapter has
    /// begun and (if any) its start rules match; completes when its complete rules match. A soft deadline ("before sunset")
    /// never fails the mission: past it the objective switches to lateObjective and the survivor says lateLine. The marker
    /// shows on the compass / map only for missions that truly need a direction (after markerAfterHours in-game hours);
    /// exploration missions give a text clue instead. Built-in missions live in MissionCatalog; PrimalStoryBuilder writes
    /// them as assets in Resources/Story/Missions (edit those; the scene list on MissionSystem wins when filled).
    /// </summary>
    [CreateAssetMenu(menuName = "Primal Frontier/Mission", fileName = "MIS_New")]
    public class MissionDefinition : ScriptableObject
    {
        public string id = "mission";
        [Range(1, 4)] public int chapter = 1;
        [Tooltip("order inside the game: the lowest active main mission is the current objective")] public int order;
        public bool main = true;
        [Tooltip("completing it ends its chapter (the next chapter begins; unfinished missions of the chapter stay open)")] public bool endsChapter;
        public string title = "Mission";
        [TextArea(1, 2)] public string objective = "";
        [TextArea(1, 2)] [Tooltip("objective after the soft deadline passed (empty = unchanged)")] public string lateObjective = "";
        [TextArea(1, 3)] [Tooltip("text clue shown under the objective when it first appears (exploration missions)")] public string clue = "";

        [Header("Start")]
        public List<string> requires = new List<string>();
        public MatchMode startMode = MatchMode.Any;
        [Tooltip("empty = starts as soon as the requirements are done")] public List<MissionCondition> startWhen = new List<MissionCondition>();

        [Header("Complete")]
        public MatchMode completeMode = MatchMode.Any;
        public List<MissionCondition> complete = new List<MissionCondition>();

        [Header("Soft deadline")]
        public MissionDeadline deadline;
        [Range(0, 24)] public float deadlineHour = 18f;

        [Header("Direction")]
        public MissionMarker marker;
        [Tooltip("Location marker: location id")] public string markerId;
        [Tooltip("in-game hours after the start before the marker appears (0 = at once)")] [Min(0)] public float markerAfterHours;

        [Header("Survivor lines (optional)")]
        public string startLine;
        public string doneLine;
        public string lateLine;
        [Tooltip("journal page unlocked when done (optional)")] public string journalPage;
        [Tooltip("story moment played when the mission starts: predator_roar (a distant roar, the forest goes quiet)")] public string startCue;

        public MissionDefinition Clone() { var m = Instantiate(this); m.name = name; return m; }
    }
}
