using System;
using System.Collections.Generic;
using UnityEngine;

namespace PrimalFrontier.Core
{
    /// <summary>
    /// Compact, versioned save file content. Only game state, never the scene hierarchy. New fields are added with a
    /// default that means "not in this save" so older files load (JsonUtility leaves missing fields at their default).
    /// v4 (survival milestone 1): SlotData / DropData.waterType (WaterType int; v2/v3 migrate from dirty), StructureData.state
    /// (ISaveableStructure), player sickSeconds (older saves: 0 = not sick), tutorialStepId (step id; older saves: empty,
    /// the index is mapped through the pre-M1 step order).
    /// </summary>
    [Serializable]
    public class SaveData
    {
        public const int CurrentVersion = 4;          // 2: water containers remember unboiled water (older saves load as clean); 3: crafting queue
        public int version = CurrentVersion;
        public string savedAt;
        public float playSeconds;
        // time / weather
        public int day; public float hour; public double clock; public string weather; public float weatherIntensity;
        // player
        public Vector3 playerPos; public float playerYaw;
        public float health, hunger, thirst, stamina, bodyTemp, wetness;
        public float sickSeconds;                                          // v4: Stomach_Sick time left (older saves: 0)
        public bool hasRespawn; public Vector3 respawnPos;
        public List<SlotData> inventory = new List<SlotData>();
        public int activeSlot;
        public List<string> knownRecipes = new List<string>();
        public List<CraftJobData> craftQueue = new List<CraftJobData>();   // v3 (older saves: empty); ingredients were paid at enqueue
        // story
        public bool introDone; public int tutorialStep; public bool tutorialDone;
        public string tutorialStepId;                                      // v4: current step by id (wins over the index; older saves: empty)
        public List<string> journal = new List<string>();
        public List<string> zonesVisited = new List<string>();
        public List<string> tipsSeen = new List<string>();         // onboarding tips already shown (older saves: none)
        // world
        public List<NodeData> nodes = new List<NodeData>();
        public List<string> takenPickups = new List<string>();
        public List<string> openedLoot = new List<string>();
        public List<string> examined = new List<string>();
        public List<TreeData> felledTrees = new List<TreeData>();
        public List<StructureData> structures = new List<StructureData>();
        public List<DropData> dropped = new List<DropData>();
    }

    [Serializable] public class SlotData { public int slot; public string item; public int count; public float durability; public int water; public bool dirty; public int waterType; }
    [Serializable] public class CraftJobData { public string recipe; public float progress; }
    [Serializable] public class NodeData { public string id; public int remaining; public double emptyUntil; }
    [Serializable] public class TreeData { public int index; public double regrowAt; }
    [Serializable] public class DropData { public string item; public int count; public float durability; public int water; public bool dirty; public int waterType; public Vector3 pos; }
    [Serializable]
    public class StructureData
    {
        public string item; public string uid; public Vector3 pos; public float yaw;
        public bool lit; public float fuel;                       // campfire
        public List<SlotData> contents = new List<SlotData>();    // storage
        public string state;                                      // ISaveableStructure (campfire slots, rain collector...)
    }
}
