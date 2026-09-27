using System;
using System.Collections.Generic;
using UnityEngine;

namespace PrimalFrontier.Core
{
    /// <summary>Compact, versioned save file content. Only game state, never the scene hierarchy.</summary>
    [Serializable]
    public class SaveData
    {
        public const int CurrentVersion = 2;          // 2: water containers remember unboiled water (older saves load as clean)
        public int version = CurrentVersion;
        public string savedAt;
        public float playSeconds;
        // time / weather
        public int day; public float hour; public double clock; public string weather; public float weatherIntensity;
        // player
        public Vector3 playerPos; public float playerYaw;
        public float health, hunger, thirst, stamina, bodyTemp, wetness;
        public bool hasRespawn; public Vector3 respawnPos;
        public List<SlotData> inventory = new List<SlotData>();
        public int activeSlot;
        public List<string> knownRecipes = new List<string>();
        // story
        public bool introDone; public int tutorialStep; public bool tutorialDone;
        public List<string> journal = new List<string>();
        public List<string> zonesVisited = new List<string>();
        // world
        public List<NodeData> nodes = new List<NodeData>();
        public List<string> takenPickups = new List<string>();
        public List<string> openedLoot = new List<string>();
        public List<string> examined = new List<string>();
        public List<TreeData> felledTrees = new List<TreeData>();
        public List<StructureData> structures = new List<StructureData>();
        public List<DropData> dropped = new List<DropData>();
    }

    [Serializable] public class SlotData { public int slot; public string item; public int count; public float durability; public int water; public bool dirty; }
    [Serializable] public class NodeData { public string id; public int remaining; public double emptyUntil; }
    [Serializable] public class TreeData { public int index; public double regrowAt; }
    [Serializable] public class DropData { public string item; public int count; public float durability; public int water; public bool dirty; public Vector3 pos; }
    [Serializable]
    public class StructureData
    {
        public string item; public string uid; public Vector3 pos; public float yaw;
        public bool lit; public float fuel;                       // campfire
        public List<SlotData> contents = new List<SlotData>();    // storage
    }
}
