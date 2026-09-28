using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using PrimalFrontier.Items;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// Crafting upgrade content (Documentation/Upgrade/CRAFTING_PLAN.md), added to what exists: never replaces or removes
    /// anything. New items (sharpened flint, sinew cord, bandage, fruit mash, butcher knife, bone arrow, hunting bow,
    /// leather waterskin) reuse the icon / world / hand model of a similar item until they get their own. New recipes carry
    /// their tool / station requirements; the flint sword recipe waits for the weapons builder's item. The campfire recipes
    /// that cook one raw item take the campfire cook time of that item (cooked meat: 14 s). Safe to run again: existing
    /// items and recipes are kept as they are (hand edits win), only empty visuals are filled.
    /// </summary>
    public static class PrimalCraftingBuilder
    {
        const string ItemsDir = "Assets/_Project/Data/Items", RecipesDir = "Assets/_Project/Data/Recipes", DbPath = "Assets/_Project/Resources/ItemDatabase.asset";
        static readonly StringBuilder Log = new StringBuilder();
        static void L(string s) { Log.AppendLine(s); Debug.Log("[PrimalCrafting] " + s); }
        static void W(string s) { Log.AppendLine("WARNING: " + s); Debug.LogWarning("[PrimalCrafting] " + s); }
        static ItemDatabase _db;

        [MenuItem("Primal Frontier/Tools/Build Crafting Recipes")]
        static void Menu() => EditorUtility.DisplayDialog("Primal Frontier", Build(""), "OK");

        /// <summary>bridge / menu entry; arg is not used</summary>
        [PrimalBridgeCommand]
        public static string Build(string arg)
        {
            Log.Clear();
            Directory.CreateDirectory(ItemsDir); Directory.CreateDirectory(RecipesDir);
            AssetDatabase.Refresh();
            _db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(DbPath);
            if (!_db) { W("no ItemDatabase at " + DbPath + " (build the gameplay data first)"); return Log.ToString(); }
            if (_db.items == null) _db.items = new List<ItemDefinition>();
            if (_db.recipes == null) _db.recipes = new List<RecipeDefinition>();
            int items0 = _db.items.Count, recipes0 = _db.recipes.Count;
            try
            {
                BuildItems();
                BuildRecipes();
                SyncCookTimes();
            }
            finally
            {
                EditorUtility.SetDirty(_db);
                AssetDatabase.SaveAssets();
            }
            L($"Database: {_db.items.Count} items (+{_db.items.Count - items0}), {_db.recipes.Count} recipes (+{_db.recipes.Count - recipes0})");
            return Log.ToString();
        }

        // ------------------------------------------------------------------ lookup
        static ItemDefinition Find(string id)
        {
            foreach (var i in _db.items) if (i && i.id == id) return i;
            return AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{ItemsDir}/ITEM_{id}.asset");
        }

        /// <summary>first of the given items that has an icon (else the first that exists): whose visuals a new item borrows</summary>
        static ItemDefinition Look(params string[] ids)
        {
            ItemDefinition any = null;
            foreach (var id in ids) { var i = Find(id); if (!i) continue; if (i.icon) return i; if (!any) any = i; }
            return any;
        }

        static void AddItem(ItemDefinition it)
        {
            if (!it || _db.items.Contains(it)) return;
            if (_db.items.Any(x => x && x.id == it.id)) { W($"database already has another item with id {it.id}: kept that one"); return; }
            _db.items.Add(it); L("  + database item " + it.id);
        }

        static void AddRecipe(RecipeDefinition rc)
        {
            if (!rc || _db.recipes.Contains(rc)) return;
            if (_db.recipes.Any(x => x && x.id == rc.id)) { W($"database already has another recipe with id {rc.id}: kept that one"); return; }
            _db.recipes.Add(rc); L("  + database recipe " + rc.id);
        }

        // ------------------------------------------------------------------ items
        static ItemDefinition Item(string id, string name, ItemCategory cat, float weight, int stack, string desc, ItemDefinition look, Action<ItemDefinition> cfg = null)
        {
            string p = $"{ItemsDir}/ITEM_{id}.asset";
            var it = AssetDatabase.LoadAssetAtPath<ItemDefinition>(p);
            if (!it)
            {
                it = ScriptableObject.CreateInstance<ItemDefinition>();
                it.id = id; it.displayName = name; it.category = cat; it.weight = weight; it.maxStack = stack; it.description = desc;
                if (look)
                {
                    it.icon = look.icon; it.worldPrefab = look.worldPrefab; it.worldEuler = look.worldEuler;
                    it.handPrefab = look.handPrefab; it.gripPosition = look.gripPosition; it.gripEuler = look.gripEuler;
                }
                cfg?.Invoke(it);
                AssetDatabase.CreateAsset(it, p);
                L($"item {id} created" + (look ? $" (visuals of {look.id})" : ""));
            }
            else if (look)
            {
                // kept as it is (hand edits win); only empty visuals are filled
                bool filled = false;
                if (!it.icon && look.icon) { it.icon = look.icon; filled = true; }
                if (!it.worldPrefab && look.worldPrefab) { it.worldPrefab = look.worldPrefab; it.worldEuler = look.worldEuler; filled = true; }
                if (!it.handPrefab && look.handPrefab) { it.handPrefab = look.handPrefab; it.gripPosition = look.gripPosition; it.gripEuler = look.gripEuler; filled = true; }
                if (filled) { EditorUtility.SetDirty(it); L($"item {id}: empty visuals filled from {look.id}"); }
            }
            if (!look) W($"item {id}: no similar item found to borrow visuals from");
            if (!it.icon) W($"item {id} has no icon");
            AddItem(it);
            return it;
        }

        static void BuildItems()
        {
            var arrow = Find("arrow");
            Item("sharpened_flint", "Sharpened Flint", ItemCategory.Resource, 0.3f, 20,
                "A stone knapped to a keen edge on both sides. The blade of better tools and weapons.", Look("stone", "hand_stone"));
            Item("sinew_cord", "Sinew Cord", ItemCategory.Resource, 0.1f, 20,
                "Dried tendon cut from a hide and twisted into a strong, springy cord. Strings a bow, stitches leather.", Look("rope", "fiber"));
            Item("bandage", "Bandage", ItemCategory.Survival, 0.1f, 10,
                "Soft fibre pads bound with a strip of hide. Use it to dress a wound (+25 health).", Look("fiber", "rope"),
                i => { i.health = 25f; i.hunger = 0f; });
            Item("fruit_mash", "Fruit Mash", ItemCategory.Food, 0.3f, 10,
                "Wild fruit and berries stewed soft over the fire. Sweet, filling and wet.", Look("berries", "fruit", "cooked_meat"),
                i => { i.hunger = 30f; i.thirst = 12f; i.isHot = true; });
            Item("butcher_knife", "Butcher Knife", ItemCategory.Tool, 0.5f, 1,
                "A broad flint blade set in a bone grip. Cuts hide and meat faster than a small knife.", Look("flint_knife"),
                i => { i.tool = ToolKind.Cut; i.toolPower = 1.2f; i.damage = 12f; i.maxDurability = 120f; i.staminaCost = 8f; i.weapon = WeaponKind.None; });
            var boneArrow = Item("bone_arrow", "Bone Arrow", ItemCategory.Ammo, 0.06f, 30,
                "A straight shaft with a ground bone point. Hits harder than a stone tip.", Look("arrow"),
                i => { if (arrow) { i.damage = arrow.damage * 1.2f; i.heavyDamage = arrow.heavyDamage * 1.2f; } });
            if (arrow && arrow.damage <= 0f && boneArrow && boneArrow.damage <= 0f)
                L("bone_arrow: the arrow item has no damage of its own (the bow sets it), so bone_arrow damage stays 0 until arrows get a value (+20 %)");
            Item("hunting_bow", "Hunting Bow", ItemCategory.Weapon, 1.3f, 1,
                "A longer bow strung with sinew. Draws heavier and shoots harder than the primitive bow. Aim with the right mouse button, hold attack to draw, release to shoot.",
                Look("bow"), i => { i.weapon = WeaponKind.Bow; i.damage = 38f; i.maxDurability = 160f; i.staminaCost = 7f; i.ammo = arrow; });
            Item("leather_waterskin", "Leather Waterskin", ItemCategory.Survival, 0.8f, 1,
                "A hide bag sewn tight with sinew. Holds more water than a gourd. Fill it at fresh water, drink from it anywhere.", Look("water_container"),
                i => { i.waterCharges = 5; });
            if (!arrow) W("arrow item missing: hunting_bow has no ammo and bone_arrow has no damage");
        }

        // ------------------------------------------------------------------ recipes
        static CraftingRequirement[] Needs(ToolKind tool) => new[] { new CraftingRequirement(tool) };

        static RecipeDefinition Recipe(string id, RecipeCategory cat, string output, int count, float secs, CraftStation st, bool known, string hint,
                                       CraftingRequirement[] reqs, params (string item, int n)[] ing)
        {
            string p = $"{RecipesDir}/RCP_{id}.asset";
            var rc = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(p);
            if (rc) { AddRecipe(rc); return rc; }                        // kept as it is (hand edits win)
            var outItem = Find(output);
            if (!outItem) { W($"recipe {id} skipped: output item {output} missing"); return null; }
            var list = new List<Ingredient>();
            foreach (var (item, n) in ing)
            {
                var it = Find(item);
                if (!it) { W($"recipe {id} skipped: ingredient {item} missing" + (item == "fruit" ? " (run Add Phase 2 Content first)" : "")); return null; }
                list.Add(new Ingredient(it, n));
            }
            rc = ScriptableObject.CreateInstance<RecipeDefinition>();
            rc.id = id; rc.category = cat; rc.output = outItem; rc.outputCount = count; rc.craftSeconds = secs; rc.station = st; rc.knownAtStart = known; rc.hint = hint;
            rc.ingredients = list.ToArray();
            rc.requirements = reqs ?? Array.Empty<CraftingRequirement>();
            rc.extraResults = Array.Empty<CraftingResult>();
            AssetDatabase.CreateAsset(rc, p);
            L($"recipe {id} created ({cat}, {string.Join(" + ", list.Select(x => x.count + " " + x.item.id))} -> {count} {output}" +
              (st != CraftStation.None ? ", at a " + st : "") + (reqs != null && reqs.Length > 0 ? ", needs " + string.Join(", ", reqs.Select(q => q.tool.ToString())) : "") + ")");
            AddRecipe(rc);
            return rc;
        }

        static void BuildRecipes()
        {
            var none = CraftStation.None;
            Recipe("sharpened_flint", RecipeCategory.Resources, "sharpened_flint", 1, 2f, none, true, null, null, ("stone", 2));
            Recipe("sinew_cord", RecipeCategory.Resources, "sinew_cord", 1, 3f, none, true, null, Needs(ToolKind.Cut), ("hide", 1));
            if (AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{ItemsDir}/ITEM_flint_sword.asset"))
            {
                AddItem(AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{ItemsDir}/ITEM_flint_sword.asset"));
                Recipe("flint_sword", RecipeCategory.Weapons, "flint_sword", 1, 8f, none, false, "Needs sharpened flint for the blade.", Needs(ToolKind.Hammer | ToolKind.Cut),
                       ("wood", 3), ("sharpened_flint", 3), ("rope", 2));
            }
            else L("flint_sword item pending, run Build Weapons first");
            Recipe("bone_arrows", RecipeCategory.Weapons, "bone_arrow", 5, 3f, none, true, null, Needs(ToolKind.Cut), ("wood", 2), ("bone", 1), ("fiber", 1));
            Recipe("hunting_bow", RecipeCategory.Weapons, "hunting_bow", 1, 8f, none, false, "Needs sinew cord for the string.", Needs(ToolKind.Cut), ("wood", 4), ("sinew_cord", 2));
            Recipe("butcher_knife", RecipeCategory.Tools, "butcher_knife", 1, 4f, none, true, null, null, ("sharpened_flint", 1), ("bone", 1), ("rope", 1));
            Recipe("leather_waterskin", RecipeCategory.Water, "leather_waterskin", 1, 6f, none, true, null, Needs(ToolKind.Cut), ("hide", 2), ("sinew_cord", 1));
            Recipe("fruit_mash", RecipeCategory.Food, "fruit_mash", 1, 12f, CraftStation.Campfire, true, null, null, ("fruit", 2), ("berries", 3));
            Recipe("bandage", RecipeCategory.Survival, "bandage", 1, 3f, none, true, null, null, ("fiber", 4), ("hide", 1));
        }

        /// <summary>a campfire recipe that cooks one raw item takes the same time as cooking it on the fire (cooked meat 10 s -> 14 s)</summary>
        static void SyncCookTimes()
        {
            int n = 0;
            foreach (var rc in _db.recipes)
            {
                if (!rc || rc.station != CraftStation.Campfire || rc.ingredients == null || rc.ingredients.Length != 1) continue;
                var raw = rc.ingredients[0].item;
                if (!raw || !raw.cookedResult || raw.cookedResult != rc.output || raw.cookSeconds <= 0f) continue;
                if (Mathf.Approximately(rc.craftSeconds, raw.cookSeconds)) continue;
                L($"recipe {rc.id}: {rc.craftSeconds:0.#} s -> {raw.cookSeconds:0.#} s (campfire cook time of {raw.id})");
                rc.craftSeconds = raw.cookSeconds; EditorUtility.SetDirty(rc); n++;
            }
            // cooked meat even when the raw meat item lost its cookedResult link: the campfire cooks meat in 14 s
            var meat = AssetDatabase.LoadAssetAtPath<RecipeDefinition>($"{RecipesDir}/RCP_cooked_meat.asset");
            var rawMeat = Find("raw_meat");
            float fire = rawMeat && rawMeat.cookSeconds > 0f ? rawMeat.cookSeconds : 14f;
            if (meat && !Mathf.Approximately(meat.craftSeconds, fire))
            {
                L($"recipe {meat.id}: {meat.craftSeconds:0.#} s -> {fire:0.#} s (campfire cook time)");
                meat.craftSeconds = fire; EditorUtility.SetDirty(meat); n++;
            }
            if (n == 0) L("campfire recipe times already match the cook times");
        }
    }
}
