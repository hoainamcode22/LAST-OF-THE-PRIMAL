using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using PrimalFrontier.Items;
using PrimalFrontier.Survival;
using PrimalFrontier.World;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// Survival Milestone 1 content (Documentation/Survival), added to what exists: never replaces what a person edited.
    /// - Resources/SurvivalConfig asset (created when missing; empty item refs filled: wood, raw_meat, cooked_meat,
    ///   burntFood = burnt_meat).
    /// - Fire data on existing items when still 0: wood fuelSeconds 240, fiber 25 (rope stays 0: not a fuel;
    ///   cooked_meat burnSeconds stays 0 = SurvivalConfig multiplier).
    /// - New items leaf_cup, burnt_meat, rain_collector, tent; their models / icons come from the lead's Blender
    ///   exports (Art/Models/Props, Art/Icons) and replace borrowed visuals as soon as they exist (logged "pending" until).
    /// - Prefabs PFB_Tent (Shelter cover 2.8 m, warmth 6 C, Bedroll, colliders with an open front at +Z) and
    ///   PFB_RainCollector (RainCollector + collider + "Water" surface). A prefab built with a borrowed model is rebuilt
    ///   when the real model arrives; otherwise kept (arg "rebuild" forces it).
    /// - Recipes leaf_cup (start), rain_collector (learned with its materials), tent (start); RCP_cooked_meat is taken
    ///   out of the ItemDatabase recipe list (asset kept) so meat is cooked only on the campfire. Max 40 recipes.
    /// Safe to run again. NEVER run PrimalGameplayBuilder after this (it overwrites item data and drops other items).
    /// Bridge: PrimalSurvivalBuilder.Build (arg "" or "rebuild").
    /// </summary>
    public static class PrimalSurvivalBuilder
    {
        const string ItemsDir = "Assets/_Project/Data/Items", RecipesDir = "Assets/_Project/Data/Recipes", DbPath = "Assets/_Project/Resources/ItemDatabase.asset";
        const string ConfigPath = "Assets/_Project/Resources/SurvivalConfig.asset";
        const string PrefabDir = "Assets/_Project/Prefabs/Gameplay", ModelDir = "Assets/_Project/Art/Models/Props", IconDir = "Assets/_Project/Art/Icons";
        const string MatDir = "Assets/_Project/Art/Materials", OldProps = "Assets/Art/Props";
        const string PendingName = "Model_Pending", ModelName = "Model";
        const int MaxRecipes = 40;

        static readonly StringBuilder Log = new StringBuilder();
        static void L(string s) { Log.AppendLine(s); Debug.Log("[PrimalSurvival] " + s); }
        static void W(string s) { Log.AppendLine("WARNING: " + s); Debug.LogWarning("[PrimalSurvival] " + s); }
        static ItemDatabase _db;
        static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        static string Signed(int v) => (v > 0 ? "+" : "") + v.ToString(CultureInfo.InvariantCulture);

        [MenuItem("Primal Frontier/Tools/Build Survival Content (M1)")]
        static void Menu() => EditorUtility.DisplayDialog("Primal Frontier", Build(""), "OK");

        /// <summary>bridge / menu entry; arg "rebuild" rebuilds PFB_Tent / PFB_RainCollector even when they exist</summary>
        [PrimalBridgeCommand]
        public static string Build(string arg)
        {
            Log.Clear();
            bool rebuild = !string.IsNullOrEmpty(arg) && arg.IndexOf("rebuild", StringComparison.OrdinalIgnoreCase) >= 0;
            foreach (var d in new[] { ItemsDir, RecipesDir, PrefabDir, MatDir, "Assets/_Project/Resources" }) Directory.CreateDirectory(d);
            AssetDatabase.Refresh();
            _db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(DbPath);
            if (!_db) { W("no ItemDatabase at " + DbPath + " (build the gameplay data first)"); return Log.ToString(); }
            if (_db.items == null) _db.items = new List<ItemDefinition>();
            if (_db.recipes == null) _db.recipes = new List<RecipeDefinition>();
            int items0 = _db.items.Count, recipes0 = _db.recipes.Count;
            try
            {
                var cfg = EnsureConfig();
                FireData();
                RemapModels();
                var items = BuildItems();
                BuildPrefabs(items, rebuild);
                RetireCookedMeatRecipe();
                BuildRecipes();
                FillConfigRefs(cfg);
                CheckIcons();
            }
            finally
            {
                EditorUtility.SetDirty(_db);
                AssetDatabase.SaveAssets();
                SurvivalConfig.Override(null);           // next access loads the asset (not a default made before it existed)
            }
            L($"Database: {_db.items.Count} items ({Signed(_db.items.Count - items0)}), {_db.recipes.Count} recipes ({Signed(_db.recipes.Count - recipes0)})");
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
            if (_db.recipes.Count >= MaxRecipes) { W($"recipe {rc.id} not added: the database already has {_db.recipes.Count} recipes (cap {MaxRecipes})"); return; }
            _db.recipes.Add(rc); L("  + database recipe " + rc.id);
        }

        static GameObject LoadModel(string file) => AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/{file}.fbx");

        /// <summary>an existing camp prop from the first art pass (Assets/Art/Props/{Camp,Items,Tools})</summary>
        static GameObject OldProp(string file)
        {
            foreach (var sub in new[] { "Camp", "Items", "Tools" })
            {
                var g = AssetDatabase.LoadAssetAtPath<GameObject>($"{OldProps}/{sub}/{file}.fbx");
                if (g) return g;
            }
            return null;
        }

        /// <summary>the lead's icon as a Sprite (import settings fixed like the other builders); null while it does not exist</summary>
        static Sprite LoadIcon(string file)
        {
            string p = $"{IconDir}/{file}.png";
            var ti = AssetImporter.GetAtPath(p) as TextureImporter;
            if (ti == null) return null;
            if (ti.textureType != TextureImporterType.Sprite || ti.spriteImportMode != SpriteImportMode.Single)
            {
                ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single; ti.mipmapEnabled = false; ti.alphaIsTransparency = true;
                ti.SaveAndReimport(); L($"icon {file}.png imported as a Sprite");
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(p);
        }

        // ------------------------------------------------------------------ config + fire data
        static SurvivalConfig EnsureConfig()
        {
            var cfg = AssetDatabase.LoadAssetAtPath<SurvivalConfig>(ConfigPath);
            if (cfg) { L("SurvivalConfig kept (" + ConfigPath + ")"); return cfg; }
            cfg = ScriptableObject.CreateInstance<SurvivalConfig>();
            AssetDatabase.CreateAsset(cfg, ConfigPath);
            L("SurvivalConfig created with the default numbers (" + ConfigPath + ")");
            return cfg;
        }

        static void FillConfigRefs(SurvivalConfig cfg)
        {
            bool dirty = false;
            void Ref(ref ItemDefinition field, string label, string id)
            {
                if (field) { L($"SurvivalConfig.{label} kept ({field.id})"); return; }
                var it = Find(id);
                if (!it) { W($"SurvivalConfig.{label}: item {id} missing"); return; }
                field = it; dirty = true; L($"SurvivalConfig.{label} = {id}");
            }
            Ref(ref cfg.wood, "wood", "wood");
            Ref(ref cfg.rawMeat, "rawMeat", "raw_meat");
            Ref(ref cfg.cookedMeat, "cookedMeat", "cooked_meat");
            Ref(ref cfg.burntFood, "burntFood", "burnt_meat");
            if (dirty) EditorUtility.SetDirty(cfg);
        }

        static void FireData()
        {
            void Fuel(string id, float secs)
            {
                var it = Find(id);
                if (!it) { W($"item {id} missing: no fuel value set"); return; }
                if (it.fuelSeconds > 0f) { L($"{id}: fuelSeconds kept ({F(it.fuelSeconds)} s)"); return; }
                it.fuelSeconds = secs; EditorUtility.SetDirty(it); L($"{id}: fuelSeconds {F(secs)} s");
            }
            Fuel("wood", 240f);
            Fuel("fiber", 25f);
            var rope = Find("rope"); if (rope) L($"rope: fuelSeconds {F(rope.fuelSeconds)} (not a fuel, left as it is)");
            var cooked = Find("cooked_meat");
            if (cooked) L("cooked_meat: burnSeconds " + (cooked.burnSeconds > 0f ? F(cooked.burnSeconds) + " s (kept)" : "0 = cook time x SurvivalConfig.burnAfterCookMultiplier"));
        }

        /// <summary>the lead's FBX materials onto Art/Materials by name (PrimalBushBuilder rule)</summary>
        static void RemapModels()
        {
            foreach (var f in new[] { "PROP_Tent", "PROP_RainCollector", "ITEM_LeafCup", "ITEM_BurntMeat" })
            {
                string p = $"{ModelDir}/{f}.fbx";
                if (!File.Exists(p)) { L($"model {f}.fbx pending ({ModelDir})"); continue; }
                int n = PrimalBushBuilder.RemapToProjectMaterials(p, L);
                if (n == 0) L($"{f}.fbx: materials already mapped (or no matching Art/Materials)");
            }
        }

        // ------------------------------------------------------------------ items
        /// <summary>
        /// Creates the item when missing (cfg runs only then: hand edits win). The own icon / model (lead's export) replaces
        /// a missing or borrowed one; until it exists the visuals of <paramref name="look"/> are borrowed.
        /// </summary>
        static ItemDefinition Item(string id, string name, ItemCategory cat, float weight, int stack, string desc, ItemDefinition look,
                                   string iconFile, string modelFile, Action<ItemDefinition> cfg = null)
        {
            string p = $"{ItemsDir}/ITEM_{id}.asset";
            var it = AssetDatabase.LoadAssetAtPath<ItemDefinition>(p);
            bool created = false;
            if (!it)
            {
                it = ScriptableObject.CreateInstance<ItemDefinition>();
                it.id = id; it.displayName = name; it.category = cat; it.weight = weight; it.maxStack = stack; it.description = desc;
                cfg?.Invoke(it);
                AssetDatabase.CreateAsset(it, p);
                created = true;
                L($"item {id} created");
            }
            else L($"item {id} kept");
            bool dirty = false;
            // icon
            var icon = LoadIcon(iconFile);
            if (icon)
            {
                if (it.icon != icon && (!it.icon || (look && it.icon == look.icon))) { it.icon = icon; dirty = true; L($"  {id}: icon {iconFile}.png"); }
            }
            else
            {
                if (!it.icon && look && look.icon) { it.icon = look.icon; dirty = true; }
                L($"  {id}: icon pending ({IconDir}/{iconFile}.png)" + (look ? $", using the {look.id} icon" : ""));
            }
            // world model (dropped / on the fire)
            var model = modelFile != null ? LoadModel(modelFile) : null;
            if (model)
            {
                if (it.worldPrefab != model && (!it.worldPrefab || (look && it.worldPrefab == look.worldPrefab)))
                { it.worldPrefab = model; it.worldEuler = Vector3.zero; dirty = true; L($"  {id}: model {modelFile}.fbx"); }
            }
            else
            {
                if (!it.worldPrefab && look && look.worldPrefab) { it.worldPrefab = look.worldPrefab; it.worldEuler = look.worldEuler; dirty = true; }
                if (modelFile != null) L($"  {id}: model pending ({ModelDir}/{modelFile}.fbx)" + (look ? $", using the {look.id} model" : ""));
            }
            if (dirty || created) EditorUtility.SetDirty(it);
            if (!it.icon) W($"item {id} has no icon (SurvivalLoopTests: every item needs one)");
            AddItem(it);
            return it;
        }

        static Dictionary<string, ItemDefinition> BuildItems()
        {
            var r = new Dictionary<string, ItemDefinition>();
            var cooked = Find("cooked_meat");
            r["leaf_cup"] = Item("leaf_cup", "Leaf Cup", ItemCategory.Survival, 0.1f, 1,
                "A broad leaf folded into a cup and tied with fibre. Holds one drink. Fill it at water; boil sea or pond water at a fire before drinking.",
                Look("water_container", "leather_waterskin"), "ICON_LeafCup", "ITEM_LeafCup", i => { i.waterCharges = 1; });
            r["burnt_meat"] = Item("burnt_meat", "Burnt Meat", ItemCategory.Food, cooked ? cooked.weight : 0.3f, cooked ? cooked.maxStack : 10,
                "Meat left on the fire too long. Charred and bitter, but still food. Take cooked food off the fire in time.",
                Look("cooked_meat"), "ICON_BurntMeat", "ITEM_BurntMeat", i => { i.hunger = 4f; i.sicknessChance = 0.1f; i.isHot = false; });
            r["rain_collector"] = Item("rain_collector", "Rain Collector", ItemCategory.Structure, 5f, 1,
                "A hide funnel over a wooden frame and basin. Collects clean rain water while it rains. Fill a container or drink from it.",
                Look("storage", "water_container"), "ICON_RainCollector", null, i => { i.placeRadius = 0.6f; });
            r["tent"] = Item("tent", "Hide Tent", ItemCategory.Structure, 9f, 1,
                "Hides stretched over a wooden frame. Keeps off the rain, holds warmth and has a bed: sleep through the night here.",
                Look("shelter", "bedroll"), "ICON_Tent", null, i => { i.placeRadius = 1.4f; });
            return r;
        }

        // ------------------------------------------------------------------ prefabs
        static Bounds RendererBounds(GameObject g, Vector3 fallbackCenter, Vector3 fallbackSize)
        {
            var rs = g ? g.GetComponentsInChildren<Renderer>() : new Renderer[0];
            if (rs.Length == 0) return new Bounds(fallbackCenter, fallbackSize);
            var b = rs[0].bounds; foreach (var x in rs) b.Encapsulate(x.bounds); return b;
        }

        /// <summary>
        /// Builds / keeps one placeable prefab. Model = the lead's FBX, else a borrowed one (child named Model_Pending, so the
        /// next run rebuilds the prefab once the FBX exists). cfg adds the components.
        /// </summary>
        static GameObject Prefab(string name, string modelFile, GameObject borrowed, bool rebuild, Vector3 fallbackSize, Action<GameObject, GameObject, Bounds> cfg)
        {
            string path = $"{PrefabDir}/{name}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var real = LoadModel(modelFile);
            if (existing && !rebuild)
            {
                bool pending = existing.transform.Find(PendingName) != null;
                if (!pending) { L($"prefab {name} kept"); return existing; }
                if (!real) { L($"prefab {name} kept (model pending: {ModelDir}/{modelFile}.fbx)"); return existing; }
                L($"prefab {name}: {modelFile}.fbx arrived, rebuilding with it");
            }
            var model = real ? real : borrowed;
            var root = new GameObject(name);
            try
            {
                GameObject vis;
                if (model)
                {
                    vis = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                    vis.transform.localPosition = Vector3.zero; vis.transform.localRotation = Quaternion.identity;
                }
                else { vis = new GameObject(); vis.transform.SetParent(root.transform, false); }
                vis.name = real ? ModelName : PendingName;
                var b = RendererBounds(vis, new Vector3(0, fallbackSize.y * 0.5f, 0), fallbackSize);
                cfg(root, vis, b);
                var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
                L($"prefab {name} {(existing ? "rebuilt" : "created")} ({path}), model " + (real ? modelFile + ".fbx" : model ? $"{model.name} (borrowed, {modelFile}.fbx pending)" : $"none ({modelFile}.fbx pending)") +
                  $", size {F(b.size.x)} x {F(b.size.y)} x {F(b.size.z)} m");
                return saved;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static void Box(GameObject root, Vector3 center, Vector3 size)
        {
            var bc = root.AddComponent<BoxCollider>(); bc.center = center; bc.size = size;
        }

        static void BuildPrefabs(Dictionary<string, ItemDefinition> items, bool rebuild)
        {
            // tent: A-frame along Z, open front at +Z. Colliders: back wall and the two low sides; the middle stays open so
            // the player can step in (Shelter covers 2.8 m around it anyway: rain cover, warmth, the Bedroll inside)
            var tent = Prefab("PFB_Tent", "PROP_Tent", OldProp("PROP_Shelter"), rebuild, new Vector3(2.2f, 1.6f, 2.6f), (root, vis, b) =>
            {
                float w = Mathf.Max(1.2f, b.size.x), h = Mathf.Max(0.9f, b.size.y), d = Mathf.Max(1.4f, b.size.z), th = 0.08f;
                Vector3 c = b.center; float wallH = h * 0.55f;
                Box(root, new Vector3(c.x - w * 0.4f, wallH * 0.5f, c.z), new Vector3(th, wallH, d * 0.95f));
                Box(root, new Vector3(c.x + w * 0.4f, wallH * 0.5f, c.z), new Vector3(th, wallH, d * 0.95f));
                Box(root, new Vector3(c.x, h * 0.5f, c.z - d * 0.5f + th * 0.5f), new Vector3(w * 0.9f, h, th));
                var sh = root.AddComponent<Shelter>(); sh.coverRadius = 2.8f; sh.warmth = 6f;
                var bed = new GameObject("Bedroll"); bed.transform.SetParent(root.transform, false);
                bed.transform.localPosition = new Vector3(c.x, 0.05f, c.z - d * 0.15f);
                bed.AddComponent<Bedroll>();
            });
            // rain collector: one box collider around the model, RainCollector, a "Water" surface that rises with the amount
            var rain = Prefab("PFB_RainCollector", "PROP_RainCollector", OldProp("PROP_Storage"), rebuild, new Vector3(0.9f, 1f, 0.9f), (root, vis, b) =>
            {
                Box(root, b.center, new Vector3(Mathf.Max(0.3f, b.size.x), Mathf.Max(0.3f, b.size.y), Mathf.Max(0.3f, b.size.z)));
                var rc = root.AddComponent<RainCollector>();
                rc.basinHeight = Mathf.Max(0.3f, b.max.y);
                var water = FindDeep(vis.transform, "Water");
                if (water) L("  PFB_RainCollector: water surface = the model's \"Water\" object");
                else
                {
                    var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    UnityEngine.Object.DestroyImmediate(disc.GetComponent<Collider>());
                    disc.name = "Water"; disc.transform.SetParent(root.transform, false);
                    disc.transform.localPosition = new Vector3(b.center.x, Mathf.Max(0.1f, b.max.y - 0.06f), b.center.z);
                    disc.transform.localScale = new Vector3(Mathf.Max(0.2f, b.size.x * 0.7f), 0.01f, Mathf.Max(0.2f, b.size.z * 0.7f));
                    var mr = disc.GetComponent<MeshRenderer>(); mr.sharedMaterial = WaterMaterial(); mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    water = disc.transform;
                    L("  PFB_RainCollector: water surface = generated disc (the model has no \"Water\" object)");
                }
                rc.waterSurface = water;
            });
            Place(items, "tent", tent);
            Place(items, "rain_collector", rain);
        }

        static void Place(Dictionary<string, ItemDefinition> items, string id, GameObject prefab)
        {
            if (!items.TryGetValue(id, out var it) || !it || !prefab) return;
            if (it.placePrefab == prefab) return;
            if (it.placePrefab) { L($"{id}: placePrefab kept ({it.placePrefab.name}, set by hand)"); return; }
            it.placePrefab = prefab; EditorUtility.SetDirty(it); L($"{id}: placePrefab = {prefab.name}");
        }

        static Transform FindDeep(Transform t, string name)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (c.name == name) return c;
                var d = FindDeep(c, name); if (d) return d;
            }
            return null;
        }

        static Material WaterMaterial()
        {
            string p = $"{MatDir}/M_RainWater.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m) return m;
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (!sh) { W("URP Lit shader not found: water disc uses the default material"); return null; }
            m = new Material(sh);
            m.SetColor("_BaseColor", new Color(0.3f, 0.47f, 0.58f, 1f)); m.SetFloat("_Smoothness", 0.92f); m.enableInstancing = true;
            AssetDatabase.CreateAsset(m, p); L("material M_RainWater created (" + p + ")");
            return m;
        }

        // ------------------------------------------------------------------ recipes
        static RecipeDefinition Recipe(string id, RecipeCategory cat, string output, int count, float secs, bool known, string hint, params (string item, int n)[] ing)
        {
            string p = $"{RecipesDir}/RCP_{id}.asset";
            var rc = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(p);
            if (rc) { L($"recipe {id} kept"); AddRecipe(rc); return rc; }          // hand edits win
            var outItem = Find(output);
            if (!outItem) { W($"recipe {id} skipped: output item {output} missing"); return null; }
            var list = new List<Ingredient>();
            foreach (var (item, n) in ing)
            {
                var it = Find(item);
                if (!it) { W($"recipe {id} skipped: ingredient {item} missing"); return null; }
                list.Add(new Ingredient(it, n));
            }
            rc = ScriptableObject.CreateInstance<RecipeDefinition>();
            rc.id = id; rc.category = cat; rc.output = outItem; rc.outputCount = count; rc.craftSeconds = secs; rc.station = CraftStation.None;
            rc.knownAtStart = known; rc.hint = hint; rc.ingredients = list.ToArray();
            rc.requirements = Array.Empty<CraftingRequirement>(); rc.extraResults = Array.Empty<CraftingResult>();
            AssetDatabase.CreateAsset(rc, p);
            L($"recipe {id} created ({cat}, {string.Join(" + ", list.Select(x => x.count + " " + x.item.id))} -> {count} {output}, {(known ? "known at start" : "learned when one of its materials is picked up")})");
            AddRecipe(rc);
            return rc;
        }

        static void BuildRecipes()
        {
            Recipe("leaf_cup", RecipeCategory.Water, "leaf_cup", 1, 2f, true, null, ("fiber", 3));
            // CraftingSystem learns a hidden recipe when any of its ingredients is added (wood, fibre or hide here)
            Recipe("rain_collector", RecipeCategory.Water, "rain_collector", 1, 8f, false, "A hide makes the funnel.", ("wood", 4), ("fiber", 6), ("hide", 1));
            Recipe("tent", RecipeCategory.Structures, "tent", 1, 10f, true, null, ("wood", 6), ("fiber", 8), ("hide", 2));
            if (_db.recipes.Count > MaxRecipes) W($"{_db.recipes.Count} recipes: over the cap of {MaxRecipes} (SurvivalLoopTests)");
        }

        /// <summary>one cooking path: meat is cooked on the campfire slots, not crafted (asset kept, only the list entry goes)</summary>
        static void RetireCookedMeatRecipe()
        {
            int n = _db.recipes.RemoveAll(r => r && r.id == "cooked_meat");
            if (n > 0) L($"recipe cooked_meat retired: removed from the ItemDatabase recipe list (asset {RecipesDir}/RCP_cooked_meat.asset kept); meat is cooked on the campfire slots");
            else L("recipe cooked_meat already retired (not in the ItemDatabase)");
        }

        static void CheckIcons()
        {
            int missing = 0;
            foreach (var it in _db.items) if (it && !it.icon) { missing++; W($"item {it.id} has no icon"); }
            if (missing == 0) L("every database item has an icon");
        }
    }
}
