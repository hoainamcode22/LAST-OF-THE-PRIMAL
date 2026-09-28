using UnityEngine;

namespace PrimalFrontier.Items
{
    /// <summary>One item type (resource, food, tool, weapon, placeable). Created by the gameplay builder; edit the numbers here.</summary>
    [CreateAssetMenu(menuName = "Primal Frontier/Item Definition", fileName = "ITEM_New")]
    public class ItemDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string id = "item";
        public string displayName = "Item";
        [TextArea(2, 4)] public string description;
        public Sprite icon;
        public ItemCategory category;
        [Min(1)] public int maxStack = 20;
        [Min(0)] public float weight = 0.5f;          // kg per unit

        [Header("World / hand")]
        [Tooltip("model dropped in the world (pickup)")] public GameObject worldPrefab;
        [Tooltip("rotation of the world model so it lies naturally on the ground")] public Vector3 worldEuler;
        [Tooltip("model held in the right hand when on the active hotbar slot")] public GameObject handPrefab;
        public Vector3 gripPosition;                   // extra offset in the grip frame (m)
        public Vector3 gripEuler;                      // extra rotation in the grip frame (deg)

        [Header("Food / drink")]
        public float hunger;                           // + restores
        public float thirst;
        public float health;
        public float stamina;
        [Tooltip("chance to get sick (lose health over time) when eaten")] [Range(0, 1)] public float sicknessChance;
        public bool isHot;                             // cooked food steams
        [Tooltip("cooking this item on a campfire gives")] public ItemDefinition cookedResult;
        public float cookSeconds = 12f;

        [Header("Tool / weapon")]
        public ToolKind tool;
        [Tooltip("gather yield multiplier / speed for its tool kinds")] public float toolPower = 1f;
        public WeaponKind weapon;
        public float damage;
        public float heavyDamage;
        public float staminaCost = 12f;
        public float maxDurability;                    // 0 = unbreakable
        public ItemDefinition ammo;                    // bow -> arrow

        [Header("Water container")]
        [Min(0)] public int waterCharges;              // capacity in drinks (0 = not a container)

        [Header("Placeable")]
        public GameObject placePrefab;                 // campfire / shelter / storage / bedroll
        public float placeRadius = 1f;                 // footprint for overlap tests

        [Header("Light")]
        public float lightRange;                       // torch

        [Header("Fire / cooking")]
        [Tooltip("seconds of campfire burn one unit adds (0 = not a fuel)")] [Min(0)] public float fuelSeconds;
        [Tooltip("seconds a cooked result may stay on the fire before it burns (0 = SurvivalConfig default from cookSeconds)")] [Min(0)] public float burnSeconds;
        [Tooltip("what this cooked item becomes when it stays on the fire too long (empty = SurvivalConfig burnt food)")] public ItemDefinition burntResult;

        [Header("Weapon data")]
        [Tooltip("combat numbers, attack chain, grip and feedback (PrimalWeaponBuilder). Empty = the legacy PlayerCombat path")]
        public Combat.Weapons.WeaponData weaponData;

        public bool IsFood => hunger > 0f || thirst > 0f || health > 0f;
        public bool IsPlaceable => placePrefab != null;
        public bool IsWaterContainer => waterCharges > 0;
        public bool HasDurability => maxDurability > 0f;
    }
}
