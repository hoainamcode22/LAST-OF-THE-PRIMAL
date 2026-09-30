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
    /// Phase 3 (same rules, hand edits win):
    /// - Resources/StatusEffects/SE_&lt;id&gt; for every built-in status effect (StatusEffectDefinition.BuiltIn values); icons
    ///   from Resources/UI/icon_status_&lt;id&gt;.png.
    /// - Items raw_fish (cooks into cooked_fish), cooked_fish, burnt_fish, edible_plant with models / icons from Art (borrowed
    ///   visuals until they exist); spoilHours on the foods that have none; the bandage becomes a treatment (cures bleeding,
    ///   +15 health slowly, no longer "eaten").
    /// - Logs the live recipe list (count, required ones present).
    /// PC phase (same rules):
    /// - "Plant Fiber" spelling on every item / recipe text in the database ("fibre" -> "fiber"; ids unchanged).
    /// - Dishes at the fire: meat_skewer (raw_meat + berries + wood -> raw skewer, cooked on the fire slots) and leaf_fish
    ///   (raw_fish + edible_plant -> raw bundle, cooked on the slots); fruit_mash stays as it is.
    /// - Rare item wreck_scraps (Shipwreck Scraps, placed by RES at the wreck remains); it also burns (60 s). Its recipes
    ///   are made by WreckRecipes (phase 1b below).
    /// Models / icons for the new items come from Tools/S_art/s_dishes_props.py (Blender, next session); until the FBX / PNG
    /// exist the items borrow the meat / fish / rope visuals (logged "pending").
    /// Phase 1 (SURV wave 1, same rules; also its own bridge command Phase1):
    /// - Salt water is never drinkable: the SurvivalConfig salt profile loses its boil result (was CleanWater) and boil time.
    /// - Hand models for the water containers (water_container, leather_waterskin, leaf_cup): PFB_Hand_&lt;id&gt; wraps the item's
    ///   own world model (Art/Props/Items/ITEM_WaterContainer.fbx, Art/Models/Props/ITEM_LeafCup.fbx), centred on the grip and
    ///   scaled to a hand size; a primitive gourd with M_Gourd when no model exists. Set only where handPrefab is empty.
    /// - Charcoal: item charcoal (Resource, fuel 150 s, icon ICON_Charcoal.png), PFB_Charcoal (primitive lumps, M_Charcoal) as
    ///   its world model, SurvivalConfig.charcoal.
    /// - Recipe categories: campfire, torch -> Fire; storage -> Storage (only from their old default category; hand edits win).
    /// Phase 1b (SURV wave 2a; also its own bridge command Phase1b, which only adds these recipes):
    /// - Shipwreck salvage (RES items wreck_scraps, wreck_nails, sailcloth): cloth_bandage (1 sailcloth + 1 fiber -> bandage,
    ///   Survival), scrap_rope (2 wreck_scraps -> rope, Resources), nailed_crate (4 wood + 4 wreck_nails -> storage, Storage;
    ///   a cheaper alternative to the lashed storage recipe). Learned when one of the materials is picked up.
    /// Safe to run again. NEVER run PrimalGameplayBuilder after this (it overwrites item data and drops other items).
    /// Bridge: PrimalSurvivalBuilder.Build (arg "" or "rebuild"); PrimalSurvivalBuilder.Phase1 (arg "" or "rebuild");
    /// PrimalSurvivalBuilder.Inspect (content report); PrimalSurvivalBuilder.Phase1Check (missing refs, counts).
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
                StatusEffectAssets();
                Phase3Items();
                PcPhaseItems();
                Phase1Content(cfg, rebuild);
                FixSpelling();
                CheckIcons();
                RecipeReport();
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
                "A broad leaf folded into a cup and tied with fiber. Holds one drink. Fill it at water; boil sea or pond water at a fire before drinking.",
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
            // CraftingSystem learns a hidden recipe when any of its ingredients is added (wood, fiber or hide here)
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

        // ------------------------------------------------------------------ phase 3
        const string EffectsDir = "Assets/_Project/Resources/StatusEffects";

        /// <summary>one asset per built-in status effect (created with the design values when missing; kept otherwise)</summary>
        static void StatusEffectAssets()
        {
            Directory.CreateDirectory(EffectsDir);
            foreach (var id in StatusEffectDefinition.BuiltInIds)
            {
                string p = $"{EffectsDir}/SE_{id}.asset";
                var d = AssetDatabase.LoadAssetAtPath<StatusEffectDefinition>(p);
                if (!d)
                {
                    d = StatusEffectDefinition.BuiltIn(id); d.hideFlags = HideFlags.None;
                    AssetDatabase.CreateAsset(d, p); L($"status effect {id} created ({p})");
                }
                else L($"status effect {id} kept");
                if (!d.icon)
                {
                    var icon = AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/_Project/Resources/UI/icon_{d.iconName}.png");
                    if (icon) { d.icon = icon; EditorUtility.SetDirty(d); L($"  {id}: icon icon_{d.iconName}.png"); }
                    else L($"  {id}: icon pending (Resources/UI/icon_{d.iconName}.png; the HUD loads it by name meanwhile)");
                }
            }
            StatusEffectDefinition.ClearCache();
        }

        static void Phase3Items()
        {
            var rawMeat = Find("raw_meat"); var cookedMeat = Find("cooked_meat"); var burntMeat = Find("burnt_meat");
            var burntFish = Item("burnt_fish", "Burnt Fish", ItemCategory.Food, 0.25f, 10,
                "Fish left on the fire too long. Charred and dry, but still a little food.",
                Look("burnt_meat", "cooked_meat"), "ICON_BurntFish", "ITEM_BurntFish", i => { i.hunger = 4f; i.sicknessChance = 0.1f; i.spoilHours = 72f; });
            var cookedFish = Item("cooked_fish", "Cooked Fish", ItemCategory.Food, 0.3f, 10,
                "Flaky white fish roasted over the coals. Light, safe and good for you.",
                Look("cooked_meat"), "ICON_CookedFish", "ITEM_CookedFish",
                i => { i.hunger = 26f; i.thirst = 4f; i.health = 5f; i.isHot = true; i.spoilHours = 36f; });
            var rawFish = Item("raw_fish", "Raw Fish", ItemCategory.Food, 0.4f, 10,
                "A silver fish from the stream. Cook it on a campfire; raw it may make you sick, and it goes off fast.",
                Look("raw_meat"), "ICON_RawFish", "ITEM_RawFish",
                i => { i.hunger = 10f; i.thirst = 2f; i.sicknessChance = 0.25f; i.cookSeconds = 10f; i.spoilHours = 12f; });
            Item("edible_plant", "Wild Greens", ItemCategory.Food, 0.1f, 20,
                "Tender leaves and shoots from the forest edge. A little food, a little water, safe to eat raw.",
                Look("berries", "fiber"), "ICON_EdiblePlant", "ITEM_EdiblePlant",
                i => { i.hunger = 6f; i.thirst = 4f; i.stamina = 3f; i.spoilHours = 30f; });
            // links (only when empty: hand edits win)
            if (rawFish && cookedFish && !rawFish.cookedResult) { rawFish.cookedResult = cookedFish; EditorUtility.SetDirty(rawFish); L("raw_fish cooks into cooked_fish"); }
            if (cookedFish && burntFish && !cookedFish.burntResult) { cookedFish.burntResult = burntFish; EditorUtility.SetDirty(cookedFish); L("cooked_fish burns into burnt_fish"); }
            // spoilage on existing foods that have none (in-game hours)
            void Spoil(string id, float hours)
            {
                var it = Find(id); if (!it) { L($"{id}: missing (no spoil time set)"); return; }
                if (it.spoilHours > 0f) { L($"{id}: spoilHours kept ({F(it.spoilHours)} h)"); return; }
                it.spoilHours = hours; EditorUtility.SetDirty(it); L($"{id}: spoilHours {F(hours)} h");
            }
            Spoil("raw_meat", 24f); Spoil("cooked_meat", 48f); Spoil("burnt_meat", 72f); Spoil("berries", 36f); Spoil("fruit", 48f); Spoil("fruit_mash", 24f);
            // the bandage is a treatment, not food: cures bleeding, health back slowly (it went through Eat and never stopped bleeding).
            // Its own roll model / icon replace the borrowed fiber ones (Item keeps everything else as it is)
            if (Find("bandage")) Item("bandage", "Bandage", ItemCategory.Survival, 0.1f, 10, null, Look("fiber", "rope"), "ICON_Bandage", "ITEM_Bandage");
            var bandage = Find("bandage");
            if (bandage)
            {
                bool dirty = false;
                if (bandage.cures == null || bandage.cures.Length == 0) { bandage.cures = new[] { StatusEffectIds.Bleeding }; dirty = true; L("bandage: cures bleeding"); }
                if (bandage.health > 0f && bandage.healOverTime <= 0f) { bandage.healOverTime = 15f; bandage.health = 0f; dirty = true; L("bandage: +15 health over time (was +25 at once through Eat)"); }
                if (bandage.description != null && bandage.description.Contains("(+25 health)"))
                { bandage.description = "Soft fiber pads bound with a strip of hide. Stops bleeding and helps the wound close (+15 health, slowly)."; dirty = true; }
                if (dirty) EditorUtility.SetDirty(bandage); else L("bandage: treatment data kept");
            }
            else W("bandage item missing (PrimalCraftingBuilder makes it)");
        }

        // ------------------------------------------------------------------ PC phase
        static void PcPhaseItems()
        {
            var burntMeat = Find("burnt_meat"); var burntFish = Find("burnt_fish");
            // meat and berry skewer: assembled at the crafting menu, cooked on the fire like any raw food
            var skewer = Item("meat_skewer", "Meat and Berry Skewer", ItemCategory.Food, 0.5f, 10,
                "Chunks of meat and berries roasted on a stick over the coals. Filling, sweet and hot.",
                Look("cooked_meat"), "ICON_MeatSkewer", "ITEM_MeatSkewer",
                i => { i.hunger = 48f; i.thirst = 10f; i.health = 8f; i.stamina = 10f; i.isHot = true; i.spoilHours = 48f; });
            var skewerRaw = Item("meat_skewer_raw", "Raw Meat Skewer", ItemCategory.Food, 0.5f, 10,
                "Raw meat and berries on a stick. Roast it on a campfire; raw it may make you sick.",
                Look("raw_meat"), "ICON_MeatSkewerRaw", "ITEM_MeatSkewerRaw",
                i => { i.hunger = 14f; i.thirst = 2f; i.sicknessChance = 0.35f; i.cookSeconds = 16f; i.spoilHours = 20f; });
            // fish wrapped in edible leaves, steamed in the coals
            var leafFish = Item("leaf_fish", "Leaf-Wrapped Fish", ItemCategory.Food, 0.45f, 10,
                "Fish steamed inside a wrap of greens on the coals. Soft, safe and good for you.",
                Look("cooked_fish"), "ICON_LeafFish", "ITEM_LeafFish",
                i => { i.hunger = 36f; i.thirst = 10f; i.health = 8f; i.stamina = 8f; i.isHot = true; i.spoilHours = 40f; });
            var leafFishRaw = Item("leaf_fish_raw", "Fish in Leaves (raw)", ItemCategory.Food, 0.45f, 10,
                "A raw fish wrapped in greens, ready for the coals. Cook it on a campfire.",
                Look("raw_fish"), "ICON_LeafFishRaw", "ITEM_LeafFishRaw",
                i => { i.hunger = 14f; i.thirst = 5f; i.sicknessChance = 0.25f; i.cookSeconds = 12f; i.spoilHours = 12f; });
            // shipwreck scraps: a rare pickup at the wreck remains (RES places it), cloth and cord in one
            Item("wreck_scraps", "Shipwreck Scraps", ItemCategory.Resource, 0.3f, 20,
                "Torn sailcloth, tarred cord and splinters washed up from the wreck. Cloth for dressings, cord for rope; the tar burns.",
                Look("rope", "fiber"), "ICON_WreckScraps", "ITEM_WreckScraps",
                i => { i.fuelSeconds = 60f; });
            // links (only when empty: hand edits win)
            if (skewerRaw && skewer && !skewerRaw.cookedResult) { skewerRaw.cookedResult = skewer; EditorUtility.SetDirty(skewerRaw); L("meat_skewer_raw cooks into meat_skewer"); }
            if (skewer && burntMeat && !skewer.burntResult) { skewer.burntResult = burntMeat; EditorUtility.SetDirty(skewer); L("meat_skewer burns into burnt_meat"); }
            if (leafFishRaw && leafFish && !leafFishRaw.cookedResult) { leafFishRaw.cookedResult = leafFish; EditorUtility.SetDirty(leafFishRaw); L("leaf_fish_raw cooks into leaf_fish"); }
            if (leafFish && burntFish && !leafFish.burntResult) { leafFish.burntResult = burntFish; EditorUtility.SetDirty(leafFish); L("leaf_fish burns into burnt_fish"); }
            // recipes (learned when one of the materials is picked up)
            Recipe("meat_skewer", RecipeCategory.Food, "meat_skewer_raw", 1, 4f, false, "Meat and berries on a stick, then the fire.", ("raw_meat", 1), ("berries", 4), ("wood", 1));
            Recipe("leaf_fish", RecipeCategory.Food, "leaf_fish_raw", 1, 3f, false, "Wrap a fish in greens, then the coals.", ("raw_fish", 1), ("edible_plant", 2));
            // cloth_bandage / scrap_rope: made by WreckRecipes (phase 1b) now that RES's sailcloth / wreck_nails exist
            if (_db.recipes.Count > MaxRecipes) W($"{_db.recipes.Count} recipes: over the cap of {MaxRecipes} (SurvivalLoopTests)");
        }

        /// <summary>"Plant Fiber" everywhere in the item / recipe texts (the owner's spelling); ids are never touched</summary>
        static void FixSpelling()
        {
            int n = 0;
            string Fix(string t) => t == null ? null : t.Replace("Fibre", "Fiber").Replace("fibre", "fiber");
            foreach (var it in _db.items)
            {
                if (!it) continue;
                string dn = Fix(it.displayName), de = Fix(it.description);
                if (dn != it.displayName || de != it.description) { it.displayName = dn; it.description = de; EditorUtility.SetDirty(it); n++; L($"  spelling: item {it.id} -> \"{dn}\""); }
            }
            foreach (var r in _db.recipes)
            {
                if (!r) continue;
                string h = Fix(r.hint);
                if (h != r.hint) { r.hint = h; EditorUtility.SetDirty(r); n++; L($"  spelling: recipe {r.id} hint"); }
            }
            L(n == 0 ? "spelling: every item / recipe text already says Fiber" : $"spelling: {n} item / recipe text(s) now say Fiber");
        }

        /// <summary>the live recipe list and the directive's required recipes (19): which are present</summary>
        static void RecipeReport()
        {
            var ids = _db.recipes.Where(r => r).Select(r => r.id).ToList();
            L($"recipes ({ids.Count}): {string.Join(", ", ids)}");
            var outputs = new HashSet<string>(_db.recipes.Where(r => r && r.output).Select(r => r.output.id));
            var want = new (string what, string[] any)[]
            {
                ("stone axe", new[] { "stone_axe" }), ("stone pick", new[] { "stone_pick" }), ("stone knife", new[] { "flint_knife", "stone_knife" }),
                ("stone spear (also thrown)", new[] { "stone_spear" }), ("primitive bow", new[] { "bow" }), ("arrow", new[] { "arrow" }),
                ("campfire", new[] { "campfire" }), ("torch", new[] { "torch" }), ("water container", new[] { "water_container", "leaf_cup", "leather_waterskin" }),
                ("bedroll", new[] { "bedroll" }), ("storage", new[] { "storage" }), ("bandage", new[] { "bandage" }),
            };
            foreach (var (what, any) in want)
                L($"  required {what}: " + (any.Any(outputs.Contains) ? "present (" + string.Join("/", any.Where(outputs.Contains)) + ")" : "MISSING"));
            L("  cooked meat / cooked fish: campfire slots (raw_meat -> " + (Find("raw_meat") && Find("raw_meat").cookedResult ? Find("raw_meat").cookedResult.id : "none") +
              ", raw_fish -> " + (Find("raw_fish") && Find("raw_fish").cookedResult ? Find("raw_fish").cookedResult.id : "none") + ")");
            L("  dishes at the fire: " + string.Join(", ", new[] { "meat_skewer", "leaf_fish", "fruit_mash" }.Where(id => _db.recipes.Any(r => r && r.id == id))) +
              "; rare items: bone (" + _db.recipes.Count(r => r && r.ingredients.Any(i => i.item && i.item.id == "bone")) + " recipes), hide (" +
              _db.recipes.Count(r => r && r.ingredients.Any(i => i.item && i.item.id == "hide")) + "), wreck_scraps (" +
              _db.recipes.Count(r => r && r.ingredients.Any(i => i.item && i.item.id == "wreck_scraps")) + ")");
            int building = _db.recipes.Count(r => r && r.category == RecipeCategory.Structures);
            L($"  total {_db.recipes.Count} recipes ({building} building) of the 25-40 the directive allows" + (_db.recipes.Count < 25 || _db.recipes.Count > 40 ? " : OUT OF RANGE" : ""));
        }

        /// <summary>bridge: what the phase 3 content looks like in the project (no changes)</summary>
        [PrimalBridgeCommand]
        public static string Inspect(string arg)
        {
            var sb = new StringBuilder();
            var db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(DbPath);
            if (db)
            {
                sb.AppendLine($"items {db.items.Count}, recipes {db.recipes.Count}");
                foreach (var id in new[] { "raw_fish", "cooked_fish", "burnt_fish", "edible_plant", "bandage", "raw_meat", "cooked_meat", "berries", "fruit", "meat_skewer_raw", "meat_skewer", "leaf_fish_raw", "leaf_fish", "wreck_scraps", "fiber" })
                {
                    var it = db.Item(id);
                    sb.AppendLine(it ? $"  {id}: hunger {F(it.hunger)} thirst {F(it.thirst)} health {F(it.health)} heal {F(it.healOverTime)} spoil {F(it.spoilHours)} h cures [{(it.cures != null ? string.Join(",", it.cures) : "")}] icon {(it.icon ? it.icon.name : "-")} model {(it.worldPrefab ? it.worldPrefab.name : "-")} cooks-> {(it.cookedResult ? it.cookedResult.id : "-")}" : $"  {id}: MISSING");
                }
            }
            foreach (var id in StatusEffectDefinition.BuiltInIds)
            {
                var d = AssetDatabase.LoadAssetAtPath<StatusEffectDefinition>($"{EffectsDir}/SE_{id}.asset");
                sb.AppendLine(d ? $"  SE_{id}: {d.displayName} {F(d.defaultSeconds)} s, hp/s {F(d.healthPerSecond)}, icon {(d.icon ? d.icon.name : "-")}" : $"  SE_{id}: MISSING");
            }
            var rc = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/PFB_RainCollector.prefab");
            if (rc)
            {
                var c = rc.GetComponent<RainCollector>(); var w = c ? c.waterSurface : null;
                var mf = w ? w.GetComponent<MeshFilter>() : null; var mr = w ? w.GetComponent<MeshRenderer>() : null;
                if (w && mf && mf.sharedMesh)
                {
                    var m = mf.sharedMesh; var n = m.normals.Length > 0 ? w.TransformDirection(m.normals[0]) : Vector3.zero;
                    sb.AppendLine($"rain collector water: mesh {m.name} bounds {m.bounds.center}/{m.bounds.size}, local pos {w.localPosition} rot {w.localEulerAngles} scale {w.localScale}, " +
                                  $"world normal {n}, material {(mr && mr.sharedMaterial ? mr.sharedMaterial.name + " (" + mr.sharedMaterial.shader.name + ")" : "none")}, heights empty {F(c.emptySurfaceHeight)} full {F(c.fullSurfaceHeight)}");
                }
                else sb.AppendLine("rain collector water: no mesh");
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ phase 1 (SURV wave 1)
        /// <summary>bridge: only the phase 1 content (salt rule, container hand models, charcoal, recipe categories); arg "rebuild" rebuilds the made prefabs</summary>
        [PrimalBridgeCommand]
        public static string Phase1(string arg)
        {
            Log.Clear();
            bool rebuild = !string.IsNullOrEmpty(arg) && arg.IndexOf("rebuild", StringComparison.OrdinalIgnoreCase) >= 0;
            foreach (var d in new[] { ItemsDir, RecipesDir, PrefabDir, MatDir, "Assets/_Project/Resources" }) Directory.CreateDirectory(d);
            AssetDatabase.Refresh();
            _db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(DbPath);
            if (!_db) { W("no ItemDatabase at " + DbPath); return Log.ToString(); }
            int items0 = _db.items.Count;
            try { Phase1Content(EnsureConfig(), rebuild); }
            finally
            {
                EditorUtility.SetDirty(_db);
                AssetDatabase.SaveAssets();
                SurvivalConfig.Override(null);
            }
            L($"Database: {_db.items.Count} items ({Signed(_db.items.Count - items0)}), {_db.recipes.Count} recipes");
            return Log.ToString();
        }

        static void Phase1Content(SurvivalConfig cfg, bool rebuild)
        {
            SaltNeverDrinkable(cfg);
            WaterHandModels(rebuild);
            CharcoalContent(cfg, rebuild);
            RecipeCategories();
            WreckRecipes();
        }

        /// <summary>bridge: only the phase 1b salvage recipes (cloth_bandage, scrap_rope, nailed_crate); creates missing
        /// assets and database entries, never changes existing items or recipes</summary>
        [PrimalBridgeCommand]
        public static string Phase1b(string arg)
        {
            Log.Clear();
            Directory.CreateDirectory(RecipesDir);
            _db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(DbPath);
            if (!_db) { W("no ItemDatabase at " + DbPath); return Log.ToString(); }
            int items0 = _db.items.Count, recipes0 = _db.recipes.Count;
            try { WreckRecipes(); }
            finally { EditorUtility.SetDirty(_db); AssetDatabase.SaveAssets(); }
            L($"Database: {_db.items.Count} items ({Signed(_db.items.Count - items0)}), {_db.recipes.Count} recipes ({Signed(_db.recipes.Count - recipes0)}, cap {MaxRecipes})");
            var outputs = _db.recipes.Where(r => r).GroupBy(r => r.category).OrderBy(g => (int)g.Key).Select(g => $"{g.Key} {g.Count()}");
            L("recipe tabs: " + string.Join(", ", outputs));
            foreach (var id in new[] { "wreck_scraps", "wreck_nails", "sailcloth" })
                L($"  {id} used by: " + string.Join(", ", _db.recipes.Where(r => r && r.ingredients != null && r.ingredients.Any(i => i.item && i.item.id == id)).Select(r => r.id)));
            return Log.ToString();
        }

        /// <summary>uses for RES's shipwreck salvage; Recipe() keeps existing assets (hand edits win)</summary>
        static void WreckRecipes()
        {
            Recipe("cloth_bandage", RecipeCategory.Survival, "bandage", 1, 2f, false,
                "A strip of clean sailcloth tied with fiber makes a dressing without hide.", ("sailcloth", 1), ("fiber", 1));
            Recipe("scrap_rope", RecipeCategory.Resources, "rope", 1, 2f, false,
                "Tarred cord from the wreck, picked out of the scraps and retwisted.", ("wreck_scraps", 2));
            Recipe("nailed_crate", RecipeCategory.Storage, "storage", 1, 6f, false,
                "Iron nails from the wreck hold a crate together without lashing.", ("wood", 4), ("wreck_nails", 4));
            if (_db.recipes.Count > MaxRecipes) W($"{_db.recipes.Count} recipes: over the cap of {MaxRecipes} (SurvivalLoopTests)");
        }

        /// <summary>the ocean is never drinkable: the salt profile of an existing config asset loses its boil result</summary>
        static void SaltNeverDrinkable(SurvivalConfig cfg)
        {
            if (!cfg || cfg.water == null) return;
            for (int i = 0; i < cfg.water.Length; i++)
            {
                var w = cfg.water[i];
                if (w.type != WaterType.SaltWater) continue;
                if (w.boilSeconds <= 0f && w.boilResult == WaterType.SaltWater && w.boilChargeLoss == 0) { L("salt water: already not boilable (kept)"); return; }
                L($"salt water: boilSeconds {F(w.boilSeconds)} -> 0, boilResult {w.boilResult} -> SaltWater, boilChargeLoss {w.boilChargeLoss} -> 0 (boiling does not remove salt)");
                w.boilSeconds = 0f; w.boilResult = WaterType.SaltWater; w.boilChargeLoss = 0;
                cfg.water[i] = w; EditorUtility.SetDirty(cfg);
                return;
            }
            L("salt water: no profile in the config asset (code default is not boilable)");
        }

        static Material LitMaterial(string name, Color color, float smoothness)
        {
            string p = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m) return m;
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (!sh) { W($"URP Lit shader not found: {name} not made"); return null; }
            m = new Material(sh);
            m.SetColor("_BaseColor", color); m.SetFloat("_Smoothness", smoothness); m.SetFloat("_Metallic", 0f); m.enableInstancing = true;
            AssetDatabase.CreateAsset(m, p); L($"material {name} created ({p})");
            return m;
        }

        static GameObject Part(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Vector3 euler, Material mat)
        {
            var g = GameObject.CreatePrimitive(type);
            UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());
            g.name = name; g.transform.SetParent(parent, false);
            g.transform.localPosition = pos; g.transform.localScale = scale; g.transform.localRotation = Quaternion.Euler(euler);
            var mr = g.GetComponent<MeshRenderer>(); if (mat) mr.sharedMaterial = mat;
            return g;
        }

        /// <summary>an original gourd from primitives (body, neck, wooden stopper, fibre cord), 0.26 m tall, origin at its middle</summary>
        static void PrimitiveGourd(Transform root)
        {
            var skin = LitMaterial("M_Gourd", new Color(0.62f, 0.47f, 0.25f), 0.35f);
            var wood = LitMaterial("M_GourdStopper", new Color(0.33f, 0.22f, 0.13f), 0.15f);
            var cord = LitMaterial("M_GourdCord", new Color(0.55f, 0.5f, 0.34f), 0.05f);
            Part(root, PrimitiveType.Sphere, "Body", new Vector3(0f, -0.045f, 0f), new Vector3(0.16f, 0.15f, 0.16f), Vector3.zero, skin);
            Part(root, PrimitiveType.Sphere, "Shoulder", new Vector3(0f, 0.045f, 0f), new Vector3(0.1f, 0.1f, 0.1f), Vector3.zero, skin);
            Part(root, PrimitiveType.Cylinder, "Neck", new Vector3(0f, 0.09f, 0f), new Vector3(0.045f, 0.03f, 0.045f), Vector3.zero, skin);
            Part(root, PrimitiveType.Cylinder, "Stopper", new Vector3(0f, 0.122f, 0f), new Vector3(0.034f, 0.014f, 0.034f), Vector3.zero, wood);
            Part(root, PrimitiveType.Cylinder, "Cord", new Vector3(0f, 0.08f, 0f), new Vector3(0.056f, 0.004f, 0.056f), Vector3.zero, cord);
        }

        /// <summary>PFB_Hand_&lt;id&gt;: the model centred on the grip (origin = middle of its bounds) and scaled to <paramref name="size"/> m</summary>
        static GameObject HandPrefab(string id, GameObject model, float size, bool rebuild)
        {
            string path = $"{PrefabDir}/PFB_Hand_{id}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing && !rebuild) { L($"prefab PFB_Hand_{id} kept"); return existing; }
            var root = new GameObject("PFB_Hand_" + id);
            try
            {
                var holder = new GameObject(model ? ModelName : "Gourd"); holder.transform.SetParent(root.transform, false);
                if (model)
                {
                    var vis = (GameObject)PrefabUtility.InstantiatePrefab(model, holder.transform);
                    vis.transform.localPosition = Vector3.zero; vis.transform.localRotation = Quaternion.identity;
                    foreach (var c in vis.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
                }
                else PrimitiveGourd(holder.transform);
                var b = RendererBounds(holder, Vector3.zero, Vector3.one * size);
                float m = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                float k = m > 1e-4f ? size / m : 1f;
                holder.transform.localScale = Vector3.one * k;
                holder.transform.localPosition = -b.center * k;
                var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
                L($"prefab PFB_Hand_{id} {(existing ? "rebuilt" : "created")}: {(model ? model.name : "primitive gourd")}, {F(b.size.x * k)} x {F(b.size.y * k)} x {F(b.size.z * k)} m");
                return saved;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        /// <summary>water containers get a model in the hand (PlayerEquipment shows handPrefab in the right hand socket)</summary>
        static void WaterHandModels(bool rebuild)
        {
            var gourdModel = OldProp("ITEM_WaterContainer");
            foreach (var (id, size, fallback) in new (string, float, GameObject)[] { ("water_container", 0.26f, gourdModel), ("leather_waterskin", 0.3f, gourdModel), ("leaf_cup", 0.14f, LoadModel("ITEM_LeafCup")) })
            {
                var it = Find(id);
                if (!it) { W($"{id}: item missing (no hand model)"); continue; }
                string own = $"{PrefabDir}/PFB_Hand_{id}.prefab";
                if (it.handPrefab && AssetDatabase.GetAssetPath(it.handPrefab) != own) { L($"{id}: handPrefab kept ({it.handPrefab.name}, set by hand)"); continue; }
                var model = it.worldPrefab ? it.worldPrefab : fallback;
                var hp = HandPrefab(id, model, size, rebuild);
                if (!hp) continue;
                if (it.handPrefab != hp) { it.handPrefab = hp; it.gripPosition = Vector3.zero; it.gripEuler = Vector3.zero; EditorUtility.SetDirty(it); L($"{id}: handPrefab = {hp.name}"); }
            }
        }

        /// <summary>PFB_Charcoal: a few dark angular lumps (original, primitives), about 0.16 m across, resting on y = 0</summary>
        static GameObject CharcoalPrefab(bool rebuild)
        {
            string path = $"{PrefabDir}/PFB_Charcoal.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing && !rebuild) { L("prefab PFB_Charcoal kept"); return existing; }
            var mat = LitMaterial("M_Charcoal", new Color(0.075f, 0.072f, 0.07f), 0.22f);
            var ash = LitMaterial("M_CharcoalAsh", new Color(0.36f, 0.34f, 0.32f), 0.05f);
            var root = new GameObject("PFB_Charcoal");
            try
            {
                Part(root.transform, PrimitiveType.Cube, "Lump0", new Vector3(0f, 0.026f, 0f), new Vector3(0.09f, 0.05f, 0.06f), new Vector3(8f, 25f, 12f), mat);
                Part(root.transform, PrimitiveType.Cube, "Lump1", new Vector3(0.055f, 0.02f, 0.03f), new Vector3(0.06f, 0.038f, 0.045f), new Vector3(-10f, 70f, 6f), mat);
                Part(root.transform, PrimitiveType.Cube, "Lump2", new Vector3(-0.045f, 0.018f, 0.035f), new Vector3(0.05f, 0.034f, 0.05f), new Vector3(14f, -35f, -9f), mat);
                Part(root.transform, PrimitiveType.Cube, "Lump3", new Vector3(-0.02f, 0.016f, -0.05f), new Vector3(0.045f, 0.03f, 0.035f), new Vector3(-6f, 110f, 15f), mat);
                Part(root.transform, PrimitiveType.Sphere, "Ash", new Vector3(0.005f, 0.003f, 0f), new Vector3(0.17f, 0.008f, 0.15f), Vector3.zero, ash);
                var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
                L($"prefab PFB_Charcoal {(existing ? "rebuilt" : "created")} ({path})");
                return saved;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static void CharcoalContent(SurvivalConfig cfg, bool rebuild)
        {
            var stone = Find("stone");
            var it = Item("charcoal", "Charcoal", ItemCategory.Resource, 0.15f, 20,
                "Black, brittle lumps left in the ashes of a fire that burned a long time. Light and dry, it burns hot and steady.",
                Look("stone", "wood"), "ICON_Charcoal", null, i => { i.fuelSeconds = 150f; });
            if (!it) return;
            var pf = CharcoalPrefab(rebuild);
            if (pf && (!it.worldPrefab || (stone && it.worldPrefab == stone.worldPrefab)))
            { it.worldPrefab = pf; it.worldEuler = Vector3.zero; EditorUtility.SetDirty(it); L("charcoal: world model PFB_Charcoal"); }
            if (it.fuelSeconds <= 0f) { it.fuelSeconds = 150f; EditorUtility.SetDirty(it); }
            if (cfg && !cfg.charcoal) { cfg.charcoal = it; EditorUtility.SetDirty(cfg); L("SurvivalConfig.charcoal = charcoal"); }
            else if (cfg) L($"SurvivalConfig.charcoal kept ({cfg.charcoal.id})");
            if (cfg) L($"campfire charcoal: 1 after {F(cfg.charcoalAfterBurnSeconds)} s of burning, 2 after {F(cfg.charcoalAfterBurnSeconds * 2f)} s (max {cfg.charcoalMax})");
        }

        /// <summary>campfire + torch -> Fire, storage -> Storage (only from their old default: hand edits win); structures stay Building</summary>
        static void RecipeCategories()
        {
            void Cat(string id, RecipeCategory want, params RecipeCategory[] oldDefaults)
            {
                var rc = _db.recipes.FirstOrDefault(r => r && r.id == id) ?? AssetDatabase.LoadAssetAtPath<RecipeDefinition>($"{RecipesDir}/RCP_{id}.asset");
                if (!rc) { W($"recipe {id} missing (category not set)"); return; }
                if (rc.category == want) { L($"recipe {id}: category {want} (kept)"); return; }
                if (Array.IndexOf(oldDefaults, rc.category) < 0) { L($"recipe {id}: category {rc.category} kept (not a default, set by hand)"); return; }
                L($"recipe {id}: category {rc.category} -> {want}");
                rc.category = want; EditorUtility.SetDirty(rc);
            }
            Cat("campfire", RecipeCategory.Fire, RecipeCategory.Structures, RecipeCategory.Survival);
            Cat("torch", RecipeCategory.Fire, RecipeCategory.Survival, RecipeCategory.Tools);
            Cat("storage", RecipeCategory.Storage, RecipeCategory.Structures, RecipeCategory.Survival);
            var counts = _db.recipes.Where(r => r).GroupBy(r => r.category).OrderBy(g => (int)g.Key).Select(g => $"{g.Key} {g.Count()}");
            L("recipe tabs: " + string.Join(", ", counts));
        }

        /// <summary>bridge: phase 1 content check (no changes): missing references and counts</summary>
        [PrimalBridgeCommand]
        public static string Phase1Check(string arg)
        {
            var sb = new StringBuilder();
            var db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(DbPath);
            if (!db) return "no ItemDatabase";
            int nullItems = db.items.Count(i => !i), noIcon = db.items.Count(i => i && !i.icon), nullRecipes = db.recipes.Count(r => !r);
            int badRecipes = db.recipes.Count(r => r && (!r.output || r.ingredients == null || r.ingredients.Any(g => !g.item)));
            sb.AppendLine($"items {db.items.Count} (null {nullItems}, no icon {noIcon}), recipes {db.recipes.Count} (null {nullRecipes}, missing output / ingredient {badRecipes})");
            int missingScripts = 0, nullMats = 0;
            void CheckPrefab(GameObject g, string label)
            {
                if (!g) { sb.AppendLine($"  {label}: MISSING"); return; }
                int ms = 0, nm = 0;
                foreach (var t in g.GetComponentsInChildren<Transform>(true)) ms += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                foreach (var r in g.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) if (!m) nm++;
                int rn = g.GetComponentsInChildren<Renderer>(true).Length;
                missingScripts += ms; nullMats += nm;
                sb.AppendLine($"  {label}: {g.name}, renderers {rn}, missing scripts {ms}, empty material slots {nm}");
            }
            foreach (var id in new[] { "water_container", "leather_waterskin", "leaf_cup" })
            {
                var it = db.Item(id);
                if (!it) { sb.AppendLine($"  {id}: MISSING"); continue; }
                CheckPrefab(it.handPrefab, id + " handPrefab");
            }
            foreach (var id in new[] { "torch", "stone_axe", "water_container", "leaf_cup" })
            {
                var it = db.Item(id); if (!it || !it.handPrefab) continue;
                var b = RendererBounds(it.handPrefab, Vector3.zero, Vector3.zero);
                var inst = (GameObject)UnityEngine.Object.Instantiate(it.handPrefab);
                try { b = RendererBounds(inst, Vector3.zero, Vector3.zero); } finally { UnityEngine.Object.DestroyImmediate(inst); }
                sb.AppendLine($"  {id} hand bounds centre {b.center.ToString("F3")} size {b.size.ToString("F3")}, grip {it.gripPosition} / {it.gripEuler}");
            }
            var ch = db.Item("charcoal");
            if (ch) { sb.AppendLine($"  charcoal: {ch.displayName}, category {ch.category}, fuel {F(ch.fuelSeconds)} s, icon {(ch.icon ? ch.icon.name : "NONE")}"); CheckPrefab(ch.worldPrefab, "charcoal worldPrefab"); }
            else sb.AppendLine("  charcoal: MISSING");
            var cfg = AssetDatabase.LoadAssetAtPath<SurvivalConfig>(ConfigPath);
            if (cfg)
            {
                var salt = cfg.Water(WaterType.SaltWater);
                sb.AppendLine($"  config: salt boil {F(salt.boilSeconds)} s -> {salt.boilResult}, hot cools after {F(cfg.hotWaterCoolSeconds)} s, heat {F(cfg.heatWaterSeconds)} s, charcoal ref {(cfg.charcoal ? cfg.charcoal.id : "NONE")}, charcoal after {F(cfg.charcoalAfterBurnSeconds)} s");
            }
            foreach (var id in new[] { "campfire", "torch", "storage", "shelter", "tent", "bedroll" })
            {
                var rc = db.recipes.FirstOrDefault(r => r && r.id == id);
                sb.AppendLine(rc ? $"  recipe {id}: {rc.category}" : $"  recipe {id}: not in the database");
            }
            foreach (var id in new[] { "cloth_bandage", "scrap_rope", "nailed_crate" })
            {
                var rc = db.recipes.FirstOrDefault(r => r && r.id == id);
                sb.AppendLine(rc ? $"  recipe {id}: {rc.category}, {string.Join(" + ", rc.ingredients.Select(g => g.count + " " + (g.item ? g.item.id : "MISSING")))} -> {rc.outputCount} {(rc.output ? rc.output.id : "MISSING")}" : $"  recipe {id}: not in the database");
            }
            sb.AppendLine("  tabs: " + string.Join(", ", db.recipes.Where(r => r).GroupBy(r => r.category).OrderBy(g => (int)g.Key).Select(g => $"{g.Key} {g.Count()}")));
            sb.AppendLine($"missing refs total: null items {nullItems}, null recipes {nullRecipes}, bad recipes {badRecipes}, no icon {noIcon}, missing scripts {missingScripts}, empty material slots {nullMats}");
            return sb.ToString();
        }

        static void CheckIcons()
        {
            int missing = 0;
            foreach (var it in _db.items) if (it && !it.icon) { missing++; W($"item {it.id} has no icon"); }
            if (missing == 0) L("every database item has an icon");
        }
    }
}
