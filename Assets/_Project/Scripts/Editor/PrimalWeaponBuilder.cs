using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Audio;
using PrimalFrontier.Combat.Weapons;
using PrimalFrontier.Items;
using PrimalFrontier.VFX;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// Weapon data (Documentation/Upgrade/COMBAT_PLAN.md), added to what exists: one WeaponData asset per melee / ranged
    /// item under Assets/_Project/Data/Weapons (numbers copied from the item and the reach values PlayerCombat used),
    /// linked from ITEM_&lt;id&gt;.weaponData, plus the new Flint Sword item (model WEAPON_FlintSword.fbx when it exists)
    /// added to the ItemDatabase. Safe to run again: existing weapon data, links and items are kept as they are (hand
    /// edits win); only missing assets, empty links and empty references are filled.
    /// </summary>
    public static class PrimalWeaponBuilder
    {
        const string ItemsDir = "Assets/_Project/Data/Items", WeaponsDir = "Assets/_Project/Data/Weapons", DbPath = "Assets/_Project/Resources/ItemDatabase.asset";
        const string SwordFbx = "Assets/_Project/Art/Models/Weapons/WEAPON_FlintSword.fbx";
        const string SwordIcon = "Assets/_Project/Art/Icons/ICON_FlintSword.png";
        const string TrailMat = "Assets/_Project/VFX/Materials/M_VFX_Additive.mat";
        static readonly StringBuilder Log = new StringBuilder();
        static void L(string s) { Log.AppendLine(s); Debug.Log("[PrimalWeapons] " + s); }
        static void W(string s) { Log.AppendLine("WARNING: " + s); Debug.LogWarning("[PrimalWeapons] " + s); }

        [MenuItem("Primal Frontier/Tools/Build Weapons")]
        static void Menu() => EditorUtility.DisplayDialog("Primal Frontier", Build(""), "OK");

        /// <summary>bridge / menu entry; arg is not used</summary>
        [PrimalBridgeCommand]
        public static string Build(string arg)
        {
            Log.Clear();
            Directory.CreateDirectory(WeaponsDir);
            AssetDatabase.Refresh();
            var db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(DbPath);
            if (!db) W("no ItemDatabase at " + DbPath + " (the sword item is created but not listed)");
            var trailMat = AssetDatabase.LoadAssetAtPath<Material>(TrailMat);
            if (!trailMat) W("trail material missing: " + TrailMat + " (trails use a default sprite material)");
            try
            {
                SwordItem(db);
                foreach (var s in Specs()) Weapon(s, trailMat);
            }
            finally { AssetDatabase.SaveAssets(); }
            if (Log.Length == 0) L("nothing to do: weapon data, links and the sword item already exist");
            return Log.ToString();
        }

        // ------------------------------------------------------------------ specs
        class Spec
        {
            public string item;                    // ITEM_<item>.asset
            public string asset;                   // WPN_<asset>.asset
            public Action<WeaponData, ItemDefinition> fill;
        }

        static float Or(float v, float fallback) => v > 0f ? v : fallback;

        /// <summary>the numbers the old code used: item damage / heavy / stamina, PlayerCombat reach (spear 2.1 / 2.4, others 1.35)</summary>
        static IEnumerable<Spec> Specs()
        {
            yield return new Spec
            {
                item = "stone_spear", asset = "stone_spear", fill = (d, it) =>
                {
                    d.kind = WeaponKind.Spear;
                    d.damage = Or(it ? it.damage : 0, 22f); d.heavyDamage = Or(it ? it.heavyDamage : 0, 38f); d.staminaCost = Or(it ? it.staminaCost : 0, 12f);
                    d.reach = 2.1f; d.heavyReach = 2.4f; d.hitRadius = 0.1f; d.comboWindow = 1.0f;
                    d.attacks = new[] { new AttackProfile(PlayerActions.AttackSpear, 1f, 0.28f, 0.5f), new AttackProfile(PlayerActions.SpearAttack2, 1.25f, 0.3f, 0.52f, 1.0f, 0.15f) };
                    d.heavyAttack = new AttackProfile(PlayerActions.AttackSpearHeavy, 1f, 0.35f, 0.6f, 2.4f, 0.2f);
                    d.throwable = true; d.throwSpeed = 22f; d.throwDamageMultiplier = 1.2f;
                    d.hitSfx = SfxId.HitFlesh; d.swingSfx = SfxId.SpearWhoosh; d.hitVfxHard = VfxId.HitDust;
                    d.carrySocket = CarrySocket.Back;
                    d.offHandGrip = OffHand(it, 0.42f);                   // left hand forward on the shaft (matches the clips)
                }
            };
            yield return new Spec
            {
                item = "flint_knife", asset = "flint_knife", fill = (d, it) =>
                {
                    d.kind = WeaponKind.Knife;
                    d.damage = Or(it ? it.damage : 0, 10f); d.heavyDamage = Or(it ? it.heavyDamage : 0, 14f); d.staminaCost = Or(it ? it.staminaCost : 0, 8f);
                    d.reach = 1.35f; d.hitRadius = 0.07f; d.comboWindow = 0.8f;
                    d.attacks = new[] { new AttackProfile(PlayerActions.KnifeAttack, 1f, 0.3f, 0.55f, 0.8f, 0.12f) };
                    d.heavyAttack = new AttackProfile();                   // strikes on press, as before
                    d.hitSfx = SfxId.HitFlesh; d.swingSfx = SfxId.SpearWhoosh;
                    d.carrySocket = CarrySocket.Hip;
                }
            };
            yield return Tool("stone_axe", 14f, 11f, 0.1f, SfxId.HitFlesh, CarrySocket.Hip);
            yield return Tool("stone_pick", 12f, 12f, 0.1f, SfxId.HitHeavy, CarrySocket.Hip);
            yield return Tool("stone_hammer", 12f, 12f, 0.12f, SfxId.HitHeavy, CarrySocket.Hip);
            yield return Tool("hand_stone", 6f, 8f, 0.08f, SfxId.HitHeavy, CarrySocket.None);
            yield return Tool("torch", 4f, 12f, 0.09f, SfxId.HitHeavy, CarrySocket.None);
            yield return new Spec
            {
                item = "bow", asset = "bow", fill = (d, it) =>
                {
                    d.kind = WeaponKind.Bow; d.ranged = true;
                    d.damage = Or(it ? it.damage : 0, 30f); d.heavyDamage = d.damage; d.staminaCost = Or(it ? it.staminaCost : 0, 6f);
                    d.durabilityCost = 1f;
                    d.drawTime = 0.9f; d.projectileSpeedMin = 14f; d.projectileSpeedMax = 42f; d.minDraw = 0.25f; d.recoverChance = 0.6f;
                    d.ammo = it ? it.ammo : null;
                    d.attacks = new AttackProfile[0]; d.heavyAttack = new AttackProfile();
                    // the pipeline bow bulges towards -Y with the handle 0.11 m off the string: turn it so the belly faces the
                    // target (knuckles, +Y of the grip) and bring the handle into the palm
                    d.gripEuler = new Vector3(0f, 0f, 180f); d.gripPosition = new Vector3(0f, -0.11f, 0f);
                    d.hitSfx = SfxId.ArrowImpact; d.swingSfx = SfxId.BowRelease;
                    d.carrySocket = CarrySocket.Back;
                }
            };
            yield return new Spec
            {
                item = "flint_sword", asset = "flint_sword", fill = (d, it) =>
                {
                    d.kind = WeaponKind.Sword;
                    d.damage = Or(it ? it.damage : 0, 26f); d.heavyDamage = Or(it ? it.heavyDamage : 0, 44f); d.staminaCost = Or(it ? it.staminaCost : 0, 13f);
                    d.reach = 1.6f; d.heavyReach = 1.8f; d.hitRadius = 0.1f; d.comboWindow = 1.0f;
                    d.attacks = new[]
                    {
                        new AttackProfile(PlayerActions.SwordAttack1, 1f, 0.3f, 0.5f, 1.2f, 0.15f),       // diagonal down-right
                        new AttackProfile(PlayerActions.SwordAttack2, 1.1f, 0.28f, 0.48f, 1.0f, 0.15f),   // horizontal back-hand
                        new AttackProfile(PlayerActions.SwordAttack3, 1.35f, 0.35f, 0.55f, 1.6f, 0.18f, 1.2f), // overhead finisher
                    };
                    d.heavyAttack = new AttackProfile(PlayerActions.SwordHeavy, 1f, 0.45f, 0.65f, 2.2f, 0.2f);
                    d.hitSfx = SfxId.HitFlesh; d.swingSfx = SfxId.SpearWhoosh; d.hitVfxHard = VfxId.HitDust;
                    d.trail = true; d.trailColor = new Color(1f, 0.92f, 0.78f, 0.55f);
                    d.carrySocket = CarrySocket.Back;
                    d.equipAction = PlayerActions.SwordEquip; d.equipTime = 0.5f;
                }
            };
        }

        /// <summary>stone tools and the torch: the one-hand slash (Knife_Attack), strike on press, reach 1.35 as before</summary>
        static Spec Tool(string id, float damage, float stamina, float radius, SfxId hit, CarrySocket carry) => new Spec
        {
            item = id, asset = id, fill = (d, it) =>
            {
                d.kind = it ? it.weapon : WeaponKind.None;
                d.damage = Or(it ? it.damage : 0, damage); d.heavyDamage = Or(it ? it.heavyDamage : 0, d.damage * 1.5f); d.staminaCost = Or(it ? it.staminaCost : 0, stamina);
                d.reach = 1.35f; d.hitRadius = radius; d.comboWindow = 0.8f;
                d.attacks = new[] { new AttackProfile(PlayerActions.KnifeAttack, 1f, 0.3f, 0.55f) };
                d.heavyAttack = new AttackProfile();
                d.hitSfx = hit; d.swingSfx = SfxId.SpearWhoosh; d.carrySocket = carry;
            }
        };

        /// <summary>off-hand point on the model's +Y axis, a little up the shaft (clamped to the model length)</summary>
        static Vector3 OffHand(ItemDefinition it, float want)
        {
            // along the shaft towards the point: the pipeline's tools run along local Z with the point at -Z
            var model = it ? (it.handPrefab ? it.handPrefab : it.worldPrefab) : null;
            if (model && WeaponHitbox.MeasureLocalBounds(model.transform, out var b))
            {
                if (b.size.z >= b.size.y)
                {
                    float far = Mathf.Abs(b.min.z) >= Mathf.Abs(b.max.z) ? b.min.z : b.max.z;
                    return new Vector3(b.center.x, b.center.y, Mathf.Sign(far) * Mathf.Min(want, Mathf.Abs(far) * 0.5f));
                }
                if (b.max.y > 0.1f) return new Vector3(b.center.x, Mathf.Min(want, b.max.y * 0.35f), b.center.z);
            }
            return new Vector3(0f, 0f, -want);
        }

        // ------------------------------------------------------------------ assets
        static void Weapon(Spec s, Material trailMat)
        {
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{ItemsDir}/ITEM_{s.item}.asset");
            string path = $"{WeaponsDir}/WPN_{s.asset}.asset";
            var d = AssetDatabase.LoadAssetAtPath<WeaponData>(path);
            if (!d)
            {
                d = ScriptableObject.CreateInstance<WeaponData>();
                s.fill(d, item);
                if (d.trail) d.trailMaterial = trailMat;
                AssetDatabase.CreateAsset(d, path);
                L($"weapon data WPN_{s.asset} created ({(d.IsRanged ? "ranged" : d.attacks.Length + " step chain" + (d.HasHeavy ? " + heavy" : ""))})");
            }
            else
            {
                // kept as it is (hand edits win); only empty references are filled
                bool changed = false;
                if (d.trail && !d.trailMaterial && trailMat) { d.trailMaterial = trailMat; changed = true; }
                if (d.IsRanged && !d.ammo && item && item.ammo) { d.ammo = item.ammo; changed = true; }
                if (changed) { EditorUtility.SetDirty(d); L($"weapon data WPN_{s.asset}: empty references filled"); }
            }
            if (!item) { W($"ITEM_{s.item}.asset not found: WPN_{s.asset} is not linked"); return; }
            if (!item.weaponData) { item.weaponData = d; EditorUtility.SetDirty(item); L($"ITEM_{s.item}.weaponData -> WPN_{s.asset}"); }
            else if (item.weaponData != d) L($"ITEM_{s.item} keeps its weapon data {item.weaponData.name}");
        }

        /// <summary>the Flint Sword item (created once; model and icon filled when they appear)</summary>
        static void SwordItem(ItemDatabase db)
        {
            string p = $"{ItemsDir}/ITEM_flint_sword.asset";
            var it = AssetDatabase.LoadAssetAtPath<ItemDefinition>(p);
            if (File.Exists(SwordFbx)) PrimalBushBuilder.RemapToProjectMaterials(SwordFbx, L);     // M_Tools like the other tools
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(SwordFbx);
            if (!it)
            {
                it = ScriptableObject.CreateInstance<ItemDefinition>();
                it.id = "flint_sword"; it.displayName = "Flint Sword"; it.category = ItemCategory.Weapon; it.maxStack = 1; it.weight = 2.2f;
                it.description = "An original prehistoric design: a hardwood blade edged with flint teeth, the grip wrapped in rawhide. " +
                                 "Tap to slash (three strikes chain), hold for a heavy overhead blow.";
                it.weapon = WeaponKind.Sword; it.tool = ToolKind.None;
                it.damage = 26f; it.heavyDamage = 44f; it.staminaCost = 13f; it.maxDurability = 140f;
                AssetDatabase.CreateAsset(it, p);
                L("item flint_sword created");
            }
            bool changed = false;
            if (model)
            {
                if (!it.handPrefab) { it.handPrefab = model; changed = true; }
                if (!it.worldPrefab)
                {
                    it.worldPrefab = model; changed = true;
                    // lies on the ground like the other pipeline tools (handle along local Z): same world rotation as the knife
                    var knifeItem = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{ItemsDir}/ITEM_flint_knife.asset");
                    if (knifeItem) it.worldEuler = knifeItem.worldEuler;
                }
                if (changed) L("flint_sword model: " + SwordFbx);
            }
            else if (!it.handPrefab) L("flint_sword model pending (" + SwordFbx + " not found)");
            if (!it.icon)
            {
                var ti = AssetImporter.GetAtPath(SwordIcon) as TextureImporter;
                if (ti != null && ti.textureType != TextureImporterType.Sprite)
                { ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single; ti.mipmapEnabled = false; ti.alphaIsTransparency = true; ti.SaveAndReimport(); }
                var icon = AssetDatabase.LoadAssetAtPath<Sprite>(SwordIcon);
                if (!icon) { var knife = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{ItemsDir}/ITEM_flint_knife.asset"); icon = knife ? knife.icon : null; if (icon) L("flint_sword icon: borrowed from the flint knife until ICON_FlintSword.png exists"); }
                if (icon) { it.icon = icon; changed = true; }
            }
            if (changed) EditorUtility.SetDirty(it);
            if (db)
            {
                if (db.items == null) db.items = new List<ItemDefinition>();
                if (!db.items.Contains(it))
                {
                    if (db.items.Any(x => x && x.id == it.id)) W("ItemDatabase already has another item with id flint_sword: kept that one");
                    else { db.items.Add(it); EditorUtility.SetDirty(db); L("flint_sword added to ItemDatabase"); }
                }
            }
        }
    }
}
