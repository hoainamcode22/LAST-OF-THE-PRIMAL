using System;
using UnityEngine;

namespace PrimalFrontier.Core
{
    public enum GameEventType
    {
        None,
        // player
        Looked, Moved, Sprinted, Jumped, Crouched,
        // items
        ItemAdded, ItemRemoved, ItemCrafted, ItemEquipped, ItemDropped,
        Ate, Drank, WaterFilled, TriedSaltWater,
        // world
        ResourceGathered, LootOpened, ZoneEntered, Discovery, FootprintFound,
        StructurePlaced, FireLit, FireOut, FoodCooked,
        // menus
        MenuOpened,
        // time / story
        Slept, NightStarted, DayStarted, DayCompleted, WeatherChanged,
        CreatureSighted, PredatorWarning, TutorialStep, ObjectiveChanged, JournalUnlocked,
        // combat
        CreatureHit, CreatureKilled, PlayerDied, PlayerRespawned,
        GameSaved, GameLoaded,
        // phase 2 (appended: stored as ints in journal pages)
        PlayerDodged, ClimbStarted, FruitHarvested, WaterBoiled, GotSick, VolcanoRumble,
    }

    /// <summary>One gameplay fact ("added 3 wood", "entered cave", "lit a fire"). Tutorial, journal, audio and UI listen.</summary>
    public struct GameEvent
    {
        public GameEventType type;
        public string id;          // item / zone / recipe / creature id
        public int amount;
        public Vector3 position;
        public override string ToString() => $"{type}({id} x{amount})";
    }

    /// <summary>Static event hub so systems never hold references to each other just to notify.</summary>
    public static class GameEvents
    {
        public static event Action<GameEvent> Raised;
        public static int RaisedCount { get; private set; }

        public static void Raise(GameEventType type, string id = null, int amount = 1, Vector3 position = default)
        {
            RaisedCount++;
            var e = new GameEvent { type = type, id = id, amount = amount, position = position };
            var h = Raised;
            if (h == null) return;
            foreach (Action<GameEvent> d in h.GetInvocationList())
            {
                try { d(e); } catch (Exception ex) { Debug.LogException(ex); }    // one bad listener must not break the others
            }
        }

        /// <summary>tests / new game: drop every listener that belonged to a destroyed scene</summary>
        public static void ClearAll() { Raised = null; RaisedCount = 0; }
    }
}
