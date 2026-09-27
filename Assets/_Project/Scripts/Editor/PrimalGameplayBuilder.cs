using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PrimalFrontier.Building;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.VFX;
using PrimalFrontier.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Animation = PrimalFrontier.Animation;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// Phase 7-12 builder: item / recipe ScriptableObjects, prop materials, camp prefabs, UI texture import, player
    /// gameplay components, and the gameplay layer of Island_VerticalSlice (resource nodes, fresh water, wreck loot,
    /// captain's log, footprints, cave stones, zones, GameManager). Re-runnable: assets are updated in place (GUIDs
    /// stay), scene components are replaced. Batch: -executeMethod PrimalFrontier.EditorTools.PrimalGameplayBuilder.BuildFromCommandLine
    /// </summary>
    public static class PrimalGameplayBuilder
    {
        const string ItemsDir = "Assets/_Project/Data/Items", RecipesDir = "Assets/_Project/Data/Recipes", DbPath = "Assets/_Project/Resources/ItemDatabase.asset";
        const string Props = "Assets/Art/Props", Icons = "Assets/_Project/Art/Icons", PrefabDir = "Assets/_Project/Prefabs/Gameplay", MatDir = "Assets/_Project/Art/Materials";
        const string ScenePath = "Assets/_Project/Scenes/Island_VerticalSlice.unity";
        static readonly StringBuilder Report = new StringBuilder(); static int _errors;
        static void Log(string s) { Report.AppendLine(s); Debug.Log("[PrimalGameplayBuilder] " + s); }
        static void Err(string s) { _errors++; Report.AppendLine("ERROR: " + s); Debug.LogError("[PrimalGameplayBuilder] " + s); }

        [MenuItem("Primal Frontier/Advanced (overwrites hand edits)/Regenerate Gameplay (items, recipes, prefabs, [Gameplay])", priority = 101)]
        public static void BuildMenu() { if (PrimalSceneBaker.ConfirmRegenerate("Items, recipes, gameplay prefabs and the [Gameplay] object (with the dinosaurs placed in it)")) Build(); }

        public static void BuildFromCommandLine()
        {
            int code = 0;
            try { Build(); if (_errors > 0) code = 2; }
            catch (Exception e) { Debug.LogError("[PrimalGameplayBuilder] FAILED: " + e); code = 1; }
            EditorApplication.Exit(code);
        }

        public static void Build()
        {
            Report.Clear(); _errors = 0;
            Log("Build " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            foreach (var d in new[] { ItemsDir, RecipesDir, PrefabDir, MatDir, "Assets/_Project/Resources" }) Directory.CreateDirectory(d);
            AssetDatabase.Refresh();
            ConfigureTextures();
            var mats = BuildMaterials();
            RemapProps(mats["M_Tools"]);
            var items = BuildItems();
            var prefabs = BuildPlaceables(items, mats);
            items["campfire"].placePrefab = prefabs["campfire"]; items["shelter"].placePrefab = prefabs["shelter"];
            items["storage"].placePrefab = prefabs["storage"]; items["bedroll"].placePrefab = prefabs["bedroll"];
            foreach (var it in items.Values) EditorUtility.SetDirty(it);
            var recipes = BuildRecipes(items);
            BuildDatabase(items, recipes);
            AssetDatabase.SaveAssets();
            PrimalPlayerSetup.BuildGameplayPrefab();
            SetupScene(items, mats);
            AssetDatabase.SaveAssets();
            Log($"Done with {_errors} error(s)");
            Directory.CreateDirectory("Documentation");
            File.WriteAllText("Documentation/GameplayBuildReport.md", "# Gameplay Build Report (auto-generated)\n\n```\n" + Report + "```\n");
        }

        // ================================================================== textures
        static void ConfigureTextures()
        {
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/_Project/Resources/UI" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid); var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                if (ti == null) continue;
                bool dirty = ti.mipmapEnabled || ti.textureCompression != TextureImporterCompression.Uncompressed || ti.wrapMode != TextureWrapMode.Clamp || !ti.alphaIsTransparency;
                if (!dirty) continue;
                ti.textureType = TextureImporterType.Default; ti.mipmapEnabled = false; ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.wrapMode = TextureWrapMode.Clamp; ti.alphaIsTransparency = true; ti.npotScale = TextureImporterNPOTScale.None; ti.sRGBTexture = true;
                ti.SaveAndReimport(); n++;
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Icons }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid); var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                if (ti == null || ti.textureType == TextureImporterType.Sprite) continue;
                ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single; ti.mipmapEnabled = false; ti.alphaIsTransparency = true;
                ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.SaveAndReimport(); n++;
            }
            void Tex(string p, bool normal, bool linear)
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(p); if (ti == null) { Err("texture missing " + p); return; }
                var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (ti.textureType == type && ti.sRGBTexture == !linear) return;
                ti.textureType = type; ti.sRGBTexture = !linear && !normal; ti.SaveAndReimport(); n++;
            }
            Tex(Props + "/Textures/T_Tools_N.png", true, true); Tex(Props + "/Textures/T_Tools_M.png", false, true); Tex(Props + "/Textures/T_Tools_D.png", false, false);
            Log($"Texture import settings updated: {n}");
        }

        // ================================================================== materials
        static Material Mat(string name, Shader sh)
        {
            string p = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, p); }
            else if (m.shader != sh) m.shader = sh;
            return m;
        }

        static Dictionary<string, Material> BuildMaterials()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit"); var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var particles = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var r = new Dictionary<string, Material>();
            var tools = Mat("M_Tools", lit);
            tools.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Props + "/Textures/T_Tools_D.png"));
            tools.SetColor("_BaseColor", Color.white);
            var n = AssetDatabase.LoadAssetAtPath<Texture2D>(Props + "/Textures/T_Tools_N.png"); if (n) { tools.SetTexture("_BumpMap", n); tools.EnableKeyword("_NORMALMAP"); }
            var m = AssetDatabase.LoadAssetAtPath<Texture2D>(Props + "/Textures/T_Tools_M.png");
            if (m) { tools.SetTexture("_MetallicGlossMap", m); tools.EnableKeyword("_METALLICSPECGLOSSMAP"); tools.SetFloat("_Smoothness", 1f); tools.SetTexture("_OcclusionMap", m); tools.EnableKeyword("_OCCLUSIONMAP"); tools.SetFloat("_OcclusionStrength", 0.8f); }
            tools.enableInstancing = true; EditorUtility.SetDirty(tools); r["M_Tools"] = tools;

            Material Ghost(string name, Color c)
            {
                var g = Mat(name, unlit);
                g.SetFloat("_Surface", 1f); g.SetFloat("_Blend", 0f); g.SetOverrideTag("RenderType", "Transparent");
                g.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); g.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); g.SetFloat("_ZWrite", 0f);
                g.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); g.renderQueue = (int)RenderQueue.Transparent; g.SetColor("_BaseColor", c);
                EditorUtility.SetDirty(g); return g;
            }
            r["M_GhostValid"] = Ghost("M_GhostValid", new Color(0.45f, 0.95f, 0.5f, 0.38f));
            r["M_GhostInvalid"] = Ghost("M_GhostInvalid", new Color(1f, 0.3f, 0.25f, 0.38f));

            var rain = Mat("M_Rain", particles ? particles : unlit);
            rain.SetFloat("_Surface", 1f); rain.SetFloat("_Blend", 0f); rain.SetOverrideTag("RenderType", "Transparent");
            rain.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); rain.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); rain.SetFloat("_ZWrite", 0f);
            rain.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); rain.renderQueue = (int)RenderQueue.Transparent;
            rain.SetColor("_BaseColor", new Color(0.78f, 0.82f, 0.88f, 0.42f));
            var streak = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Resources/UI/ui_bar_fill.png");
            if (streak) rain.SetTexture("_BaseMap", streak);
            EditorUtility.SetDirty(rain); r["M_Rain"] = rain;

            var mud = Mat("M_FootprintMud", lit); mud.SetColor("_BaseColor", new Color(0.13f, 0.1f, 0.07f)); mud.SetFloat("_Smoothness", 0.62f); EditorUtility.SetDirty(mud); r["M_FootprintMud"] = mud;
            var leather = Mat("M_LogBook", lit); leather.SetColor("_BaseColor", new Color(0.25f, 0.14f, 0.07f)); leather.SetFloat("_Smoothness", 0.35f); EditorUtility.SetDirty(leather); r["M_LogBook"] = leather;
            Log("Materials: " + string.Join(", ", r.Keys));
            return r;
        }

        static void RemapProps(Material tools)
        {
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Props }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid); var imp = AssetImporter.GetAtPath(p) as ModelImporter; if (imp == null) continue;
                bool changed = false;
                var existing = imp.GetExternalObjectMap();
                foreach (var mat in AssetDatabase.LoadAllAssetsAtPath(p).OfType<Material>())
                {
                    var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), mat.name);
                    if (existing.TryGetValue(id, out var o) && o == tools) continue;
                    imp.AddRemap(id, tools); changed = true;
                }
                if (imp.importAnimation || imp.importCameras || imp.importLights) { imp.importAnimation = false; imp.importCameras = false; imp.importLights = false; changed = true; }
                if (changed) { imp.SaveAndReimport(); n++; }
            }
            Log($"Prop models remapped to M_Tools: {n}");
        }

        // ================================================================== items
        static GameObject Model(string file)
        {
            foreach (var sub in new[] { "Items", "Tools", "Camp" })
            {
                var g = AssetDatabase.LoadAssetAtPath<GameObject>($"{Props}/{sub}/{file}.fbx");
                if (g) return g;
            }
            Err("model missing: " + file); return null;
        }
        static Sprite Icon(string name)
        {
            var s = AssetDatabase.LoadAssetAtPath<Sprite>($"{Icons}/ICON_{name}.png");
            if (s == null) Err("icon missing: " + name);
            return s;
        }

        static ItemDefinition Item(Dictionary<string, ItemDefinition> all, string id, string name, ItemCategory cat, string model, string icon, float weight, int stack, string desc, Action<ItemDefinition> cfg = null)
        {
            string p = $"{ItemsDir}/ITEM_{id}.asset";
            var it = AssetDatabase.LoadAssetAtPath<ItemDefinition>(p);
            if (it == null) { it = ScriptableObject.CreateInstance<ItemDefinition>(); AssetDatabase.CreateAsset(it, p); }
            it.id = id; it.displayName = name; it.category = cat; it.weight = weight; it.maxStack = stack; it.description = desc;
            it.icon = Icon(icon);
            it.worldPrefab = model != null ? Model(model) : null;
            // reset optional stats so re-runs never keep stale values
            it.handPrefab = null; it.hunger = it.thirst = it.health = it.stamina = 0; it.sicknessChance = 0; it.isHot = false; it.cookedResult = null;
            it.tool = ToolKind.None; it.toolPower = 1f; it.weapon = WeaponKind.None; it.damage = it.heavyDamage = 0; it.maxDurability = 0; it.ammo = null;
            it.waterCharges = 0; it.lightRange = 0; it.placeRadius = 1f; it.worldEuler = Vector3.zero; it.gripEuler = Vector3.zero; it.gripPosition = Vector3.zero;
            cfg?.Invoke(it);
            // long thin models lie down on the ground
            if (it.worldPrefab)
            {
                var rs = it.worldPrefab.GetComponentsInChildren<MeshFilter>(); var b = new Bounds();
                bool first = true; foreach (var mf in rs) { if (!mf.sharedMesh) continue; var mb = mf.sharedMesh.bounds; if (first) { b = mb; first = false; } else b.Encapsulate(mb); }
                if (!first && b.size.y > Mathf.Max(b.size.x, b.size.z) * 1.4f) it.worldEuler = new Vector3(90f, 0, 0);
            }
            EditorUtility.SetDirty(it);
            all[id] = it; return it;
        }

        static Dictionary<string, ItemDefinition> BuildItems()
        {
            var a = new Dictionary<string, ItemDefinition>();
            // resources
            Item(a, "wood", "Wood", ItemCategory.Resource, "ITEM_Wood", "Wood", 1.0f, 20, "Driftwood and dry branches. Fuel for fire, shafts for tools, frames for shelter.");
            Item(a, "stone", "Stone", ItemCategory.Resource, "ITEM_Stone", "Stone", 1.2f, 20, "Hard, fine-grained stone. Knapped for edges, stacked for fire rings.");
            Item(a, "fiber", "Plant Fibre", ItemCategory.Resource, "ITEM_Fiber", "Fiber", 0.1f, 40, "Tough stems stripped into fibre. Twisted into cord, woven into bedding.");
            Item(a, "rope", "Cord", ItemCategory.Resource, "ITEM_Rope", "Rope", 0.2f, 20, "Twisted cord. Binds stone to wood.");
            Item(a, "hide", "Hide", ItemCategory.Resource, "ITEM_Hide", "Hide", 1.0f, 10, "An animal skin. Waterproof when worked.");
            Item(a, "bone", "Bone", ItemCategory.Resource, "ITEM_Bone", "Bone", 0.4f, 20, "Strong and light. A good handle for a blade.");
            // food
            Item(a, "berries", "Berries", ItemCategory.Food, "ITEM_Berries", "Berries", 0.05f, 30, "Small dark berries. A little food and a little water.", i => { i.hunger = 7f; i.thirst = 3f; });
            Item(a, "cooked_meat", "Cooked Meat", ItemCategory.Food, "ITEM_CookedMeat", "CookedMeat", 0.4f, 10, "Seared over the coals. Filling and safe.", i => { i.hunger = 35f; i.health = 6f; i.isHot = true; });
            Item(a, "raw_meat", "Raw Meat", ItemCategory.Food, "ITEM_RawMeat", "RawMeat", 0.5f, 10, "Raw flesh. Cook it on a campfire; eaten raw it may make you sick.",
                i => { i.hunger = 10f; i.sicknessChance = 0.35f; i.cookedResult = a["cooked_meat"]; i.cookSeconds = 14f; });
            // tools
            Item(a, "hand_stone", "Hand Stone", ItemCategory.Tool, "TOOL_HandStone", "HandStone", 0.8f, 1, "A fist-sized stone knapped to a rough edge. Chops and breaks, badly.",
                i => { i.handPrefab = i.worldPrefab; i.tool = ToolKind.Chop | ToolKind.Mine; i.toolPower = 0.5f; i.damage = 6f; i.maxDurability = 40f; i.staminaCost = 8f; });
            Item(a, "flint_knife", "Flint Knife", ItemCategory.Tool, "TOOL_FlintKnife", "FlintKnife", 0.4f, 1, "A flake of flint bound to a bone handle. Cuts fibre and hide cleanly.",
                i => { i.handPrefab = i.worldPrefab; i.tool = ToolKind.Cut; i.damage = 10f; i.maxDurability = 80f; i.staminaCost = 8f; });
            Item(a, "stone_hammer", "Stone Hammer", ItemCategory.Tool, "TOOL_StoneHammer", "StoneHammer", 2.0f, 1, "A heavy stone lashed to a haft. Breaks rock, drives stakes.",
                i => { i.handPrefab = i.worldPrefab; i.tool = ToolKind.Hammer | ToolKind.Mine; i.toolPower = 0.8f; i.damage = 12f; i.maxDurability = 90f; i.staminaCost = 12f; });
            Item(a, "stone_axe", "Stone Axe", ItemCategory.Tool, "TOOL_StoneAxe", "StoneAxe", 2.0f, 1, "A ground stone head bound to a wooden haft. Fells trees.",
                i => { i.handPrefab = i.worldPrefab; i.tool = ToolKind.Chop; i.toolPower = 1f; i.damage = 14f; i.maxDurability = 120f; i.staminaCost = 11f; });
            Item(a, "stone_pick", "Stone Pick", ItemCategory.Tool, "TOOL_StonePick", "StonePick", 2.5f, 1, "A pointed stone head. Breaks stone from big rocks.",
                i => { i.handPrefab = i.worldPrefab; i.tool = ToolKind.Mine; i.toolPower = 1f; i.damage = 12f; i.maxDurability = 120f; i.staminaCost = 12f; });
            Item(a, "torch", "Torch", ItemCategory.Survival, "TOOL_Torch", "Torch", 0.8f, 5, "Resin and fibre wrapped on a stick. Light in the dark, a little warmth.",
                i => { i.handPrefab = i.worldPrefab; i.tool = ToolKind.Light; i.lightRange = 9f; i.damage = 4f; });
            // weapons
            Item(a, "arrow", "Arrow", ItemCategory.Ammo, "WEAPON_Arrow", "Arrow", 0.05f, 30, "A straight shaft with a stone tip.");
            Item(a, "stone_spear", "Stone Spear", ItemCategory.Weapon, "WEAPON_StoneSpear", "StoneSpear", 2.0f, 1, "Tap to thrust, hold for a heavy thrust, aim (right mouse) and attack to throw.",
                i => { i.handPrefab = i.worldPrefab; i.weapon = WeaponKind.Spear; i.damage = 22f; i.heavyDamage = 38f; i.staminaCost = 12f; i.maxDurability = 100f; i.tool = ToolKind.None; });
            Item(a, "bow", "Primitive Bow", ItemCategory.Weapon, "WEAPON_Bow", "Bow", 1.2f, 1, "Aim with the right mouse button, hold attack to draw, release to shoot. Needs arrows.",
                i => { i.handPrefab = i.worldPrefab; i.weapon = WeaponKind.Bow; i.damage = 30f; i.staminaCost = 6f; i.maxDurability = 120f; i.ammo = a["arrow"]; });
            // survival
            Item(a, "water_container", "Water Gourd", ItemCategory.Survival, "ITEM_WaterContainer", "WaterContainer", 1.0f, 1, "A dried gourd with a stopper. Fill it at fresh water, drink from it anywhere.",
                i => { i.waterCharges = 3; });
            // structures (placeables get their prefab after BuildPlaceables)
            Item(a, "campfire", "Campfire", ItemCategory.Structure, "ITEM_Wood", "Campfire", 6f, 3, "A ring of stones and a stack of wood. Place it, then light it with wood.", i => i.placeRadius = 0.7f);
            Item(a, "shelter", "Basic Shelter", ItemCategory.Structure, "ITEM_Wood", "Shelter", 10f, 1, "A lean-to of branches and leaves. Keeps off rain; rest here to save.", i => i.placeRadius = 1.5f);
            Item(a, "storage", "Storage Basket", ItemCategory.Structure, "ITEM_Wood", "Storage", 5f, 2, "A woven basket with a lid. Twenty slots of storage.", i => i.placeRadius = 0.6f);
            Item(a, "bedroll", "Simple Bedroll", ItemCategory.Structure, "ITEM_Fiber", "Bedroll", 3f, 1, "Woven grass bedding. Sleep through the night and wake here.", i => i.placeRadius = 1.0f);
            Log($"Items: {a.Count}");
            return a;
        }

        static Dictionary<string, RecipeDefinition> BuildRecipes(Dictionary<string, ItemDefinition> it)
        {
            var r = new Dictionary<string, RecipeDefinition>();
            void R(string id, RecipeCategory cat, string output, int count, float secs, CraftStation st, bool known, string hint, params (string item, int n)[] ing)
            {
                string p = $"{RecipesDir}/RCP_{id}.asset";
                var rc = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(p);
                if (rc == null) { rc = ScriptableObject.CreateInstance<RecipeDefinition>(); AssetDatabase.CreateAsset(rc, p); }
                rc.id = id; rc.category = cat; rc.output = it[output]; rc.outputCount = count; rc.craftSeconds = secs; rc.station = st; rc.knownAtStart = known; rc.hint = hint;
                rc.ingredients = ing.Select(x => new Ingredient(it[x.item], x.n)).ToArray();
                EditorUtility.SetDirty(rc); r[id] = rc;
            }
            R("rope", RecipeCategory.Survival, "rope", 1, 2f, CraftStation.None, true, null, ("fiber", 3));
            R("hand_stone", RecipeCategory.Tools, "hand_stone", 1, 2f, CraftStation.None, true, null, ("stone", 2));
            R("stone_axe", RecipeCategory.Tools, "stone_axe", 1, 4f, CraftStation.None, true, null, ("stone", 2), ("wood", 1), ("rope", 1));
            R("stone_pick", RecipeCategory.Tools, "stone_pick", 1, 4f, CraftStation.None, true, null, ("stone", 2), ("wood", 2), ("rope", 1));
            R("stone_hammer", RecipeCategory.Tools, "stone_hammer", 1, 4f, CraftStation.None, true, null, ("stone", 3), ("wood", 1), ("rope", 1));
            R("flint_knife", RecipeCategory.Tools, "flint_knife", 1, 3f, CraftStation.None, false, "Needs a bone for the handle.", ("stone", 1), ("bone", 1), ("fiber", 1));
            R("stone_spear", RecipeCategory.Weapons, "stone_spear", 1, 5f, CraftStation.None, true, null, ("wood", 3), ("stone", 1), ("rope", 1));
            R("bow", RecipeCategory.Weapons, "bow", 1, 6f, CraftStation.None, true, null, ("wood", 3), ("rope", 2));
            R("arrows", RecipeCategory.Weapons, "arrow", 5, 3f, CraftStation.None, true, null, ("wood", 2), ("stone", 1), ("fiber", 1));
            R("torch", RecipeCategory.Survival, "torch", 1, 2f, CraftStation.None, true, null, ("wood", 1), ("fiber", 2));
            R("water_container", RecipeCategory.Water, "water_container", 1, 4f, CraftStation.None, false, "Needs a hide to seal it.", ("hide", 1), ("rope", 1));
            R("cooked_meat", RecipeCategory.Food, "cooked_meat", 1, 10f, CraftStation.Campfire, true, null, ("raw_meat", 1));
            R("campfire", RecipeCategory.Structures, "campfire", 1, 3f, CraftStation.None, true, null, ("wood", 4), ("stone", 5));
            R("shelter", RecipeCategory.Structures, "shelter", 1, 6f, CraftStation.None, true, null, ("wood", 8), ("fiber", 6), ("rope", 2));
            R("storage", RecipeCategory.Structures, "storage", 1, 5f, CraftStation.None, true, null, ("wood", 6), ("fiber", 4), ("rope", 1));
            R("bedroll", RecipeCategory.Structures, "bedroll", 1, 5f, CraftStation.None, true, null, ("fiber", 10), ("rope", 2));
            Log($"Recipes: {r.Count}");
            return r;
        }

        static void BuildDatabase(Dictionary<string, ItemDefinition> items, Dictionary<string, RecipeDefinition> recipes)
        {
            var db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(DbPath);
            if (db == null) { db = ScriptableObject.CreateInstance<ItemDatabase>(); AssetDatabase.CreateAsset(db, DbPath); }
            db.items = items.Values.ToList(); db.recipes = recipes.Values.ToList();
            EditorUtility.SetDirty(db);
            Log($"Database: {db.items.Count} items, {db.recipes.Count} recipes -> {DbPath}");
        }

        // ================================================================== placeables
        static Dictionary<string, GameObject> BuildPlaceables(Dictionary<string, ItemDefinition> items, Dictionary<string, Material> mats)
        {
            var r = new Dictionary<string, GameObject>();
            GameObject Make(string key, string model, Action<GameObject, GameObject> cfg)
            {
                var root = new GameObject("PFB_" + char.ToUpper(key[0]) + key.Substring(1));
                try
                {
                    var m = Model(model);
                    GameObject vis = null;
                    if (m) { vis = (GameObject)PrefabUtility.InstantiatePrefab(m, root.transform); vis.name = "Model"; vis.transform.localPosition = Vector3.zero; }
                    cfg(root, vis);
                    var saved = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{root.name}.prefab");
                    r[key] = saved; return saved;
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            Bounds B(GameObject g)
            {
                var rs = g ? g.GetComponentsInChildren<Renderer>() : new Renderer[0];
                if (rs.Length == 0) return new Bounds(Vector3.up * 0.3f, Vector3.one * 0.6f);
                var b = rs[0].bounds; foreach (var x in rs) b.Encapsulate(x.bounds); return b;
            }
            var fire = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/VFX/Prefabs/VFX_CampfireLoop.prefab");
            if (!fire) Err("VFX_CampfireLoop prefab missing");
            Make("campfire", "PROP_Campfire", (root, vis) =>
            {
                var b = B(vis);
                var bc = root.AddComponent<BoxCollider>(); bc.center = new Vector3(0, Mathf.Min(0.2f, b.size.y * 0.5f), 0); bc.size = new Vector3(b.size.x * 0.9f, Mathf.Min(0.4f, b.size.y), b.size.z * 0.9f);
                var cf = root.AddComponent<Campfire>(); cf.fuelItem = items["wood"];
                if (fire) { var f = (GameObject)PrefabUtility.InstantiatePrefab(fire, root.transform); f.transform.localPosition = new Vector3(0, 0.08f, 0); cf.fx = f.GetComponent<CampfireFx>(); if (cf.fx) cf.fx.startLit = false; }
            });
            Make("shelter", "PROP_Shelter", (root, vis) =>
            {
                if (vis) foreach (var mf in vis.GetComponentsInChildren<MeshFilter>()) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                root.AddComponent<Shelter>();
            });
            Make("storage", "PROP_Storage", (root, vis) =>
            {
                var b = B(vis); var bc = root.AddComponent<BoxCollider>(); bc.center = new Vector3(0, b.size.y * 0.5f, 0); bc.size = b.size;
                var inv = root.AddComponent<InventorySystem>(); inv.slotCount = 20; inv.hotbarSize = 0; inv.maxWeight = 0f; inv.raiseGameEvents = false;
                root.AddComponent<StorageBox>();
            });
            Make("bedroll", "PROP_Bedroll", (root, vis) => { root.AddComponent<Bedroll>(); });
            Log("Placeable prefabs: " + string.Join(", ", r.Keys));
            return r;
        }

        // ================================================================== scene
        [Serializable] class MarkerFile { public Marker[] markers; }
        [Serializable] class Marker { public string name; public string group; public float x, y, z, r, yaw; }

        static Terrain _terrain;
        static Vector3 Ground(Vector3 p) { if (_terrain) p.y = _terrain.SampleHeight(p) + _terrain.transform.position.y; return p; }
        static Vector3 GroundNormal(Vector3 p)
        {
            if (!_terrain) return Vector3.up;
            var td = _terrain.terrainData; var lp = p - _terrain.transform.position;
            return td.GetInterpolatedNormal(lp.x / td.size.x, lp.z / td.size.z);
        }

        static void SetupScene(Dictionary<string, ItemDefinition> items, Dictionary<string, Material> mats)
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            _terrain = UnityEngine.Object.FindFirstObjectByType<Terrain>();
            // remove what a previous run added
            var old = GameObject.Find("[Gameplay]"); if (old) UnityEngine.Object.DestroyImmediate(old);
            var gp = new GameObject("[Gameplay]").transform;

            // ---- markers
            var markers = new Dictionary<string, Marker>();
            var mj = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_Project/Data/World/markers.json");
            if (mj != null) foreach (var m in JsonUtility.FromJson<MarkerFile>(mj.text).markers) markers[m.name] = m;
            Vector3 MP(string n) => markers.TryGetValue(n, out var m) ? Ground(BlenderSpace.ToUnityPosition(m.x, m.y, m.z)) : Vector3.zero;
            float MR(string n, float def) => markers.TryGetValue(n, out var m) && m.r > 0.01f ? m.r : def;

            // ---- resource nodes
            var world = GameObject.Find("World");
            int nWood = 0, nStone = 0, nFiber = 0, nBerry = 0, nRock = 0;
            if (!world) Err("World root missing in scene");
            else
            {
                foreach (Transform t in world.GetComponentsInChildren<Transform>(true))
                {
                    string n = t.name;
                    if (t.parent == null || !(t.parent.name == "Resources" || t.parent.name == "Rocks")) continue;
                    ResourceNode node = null;
                    if (n.Contains("RES_Wood")) { node = Node(t, "Driftwood", "Gather", items["wood"], 1, 4, ToolKind.None, ToolKind.Chop, 20f, true, $"res_wood_{nWood++}"); node.handAction = Animation.PlayerActions.GatherPlant; node.toolAction = Animation.PlayerActions.GatherWood; }
                    else if (n.Contains("RES_Stone")) { node = Node(t, "Loose stones", "Gather", items["stone"], 1, 4, ToolKind.None, ToolKind.Mine, 24f, true, $"res_stone_{nStone++}"); node.handAction = Animation.PlayerActions.GatherPlant; node.toolAction = Animation.PlayerActions.GatherStone; }
                    else if (n.Contains("RES_Fiber")) { node = Node(t, "Fibrous plant", "Gather", items["fiber"], 2, 3, ToolKind.None, ToolKind.Cut, 16f, true, $"res_fiber_{nFiber++}"); node.handAction = node.toolAction = Animation.PlayerActions.GatherPlant; }
                    else if (n.Contains("RES_BerryPlant")) { node = Node(t, "Berry bush", "Pick", items["berries"], 2, 3, ToolKind.None, ToolKind.None, 20f, false, $"res_berry_{nBerry++}"); node.handAction = node.toolAction = Animation.PlayerActions.GatherPlant; }
                    else if (n.Contains("ENV_Rock_Medium") || n.Contains("ENV_Rock_Large"))
                    {
                        node = Node(t, "Rock", "Break", items["stone"], 2, 5, ToolKind.Mine, ToolKind.None, 30f, false, $"rock_{nRock++}");
                        node.handAction = node.toolAction = Animation.PlayerActions.GatherStone;
                        var b = RendBounds(t.gameObject); node.radius = Mathf.Max(0.4f, Mathf.Min(b.extents.x, b.extents.z) * 0.9f);
                    }
                }
            }
            Log($"Resource nodes: wood {nWood}, stone {nStone}, fiber {nFiber}, berries {nBerry}, rocks {nRock}");
            if (nWood + nStone + nFiber + nBerry == 0) Err("no RES_ nodes found");

            // ---- fresh water
            int nw = 0;
            foreach (var wn in new[] { "ENV_Pond_Water", "ENV_Stream_Water" })
            {
                var w = GameObject.Find(wn); if (!w) { Err("water missing " + wn); continue; }
                foreach (var c in w.GetComponents<WaterSource>()) UnityEngine.Object.DestroyImmediate(c);
                var ws = w.AddComponent<WaterSource>(); ws.fresh = true; ws.displayName = wn.Contains("Pond") ? "Pond water" : "Stream water"; ws.surface = w.GetComponentInChildren<MeshFilter>();
                ws.SaveId = "water_" + wn; nw++;
            }
            Log($"Fresh water sources: {nw}");

            // ---- wreck loot
            var loot = new List<(string name, (string, int)[] items, string text)>
            {
                ("Ship's crate", new[] { ("rope", 2), ("raw_meat", 2) }, "Frayed ship's rope and two slabs of salted meat. Better than nothing."),
                ("Ship's crate", new[] { ("water_container", 1), ("berries", 4) }, "A stoppered gourd flask and a handful of dried berries."),
                ("Ship's crate", new (string, int)[0], "Empty. The sea took everything."),
                ("Barrel", new[] { ("bone", 2) }, "Salt and fish bones. The bones could make a handle."),
                ("Barrel", new (string, int)[0], "Sea water and rot."),
                ("Barrel", new[] { ("fiber", 4) }, "Oakum fibre for caulking. It will twist into cord."),
                ("Broken crate", new[] { ("wood", 3) }, "Splintered planks. Good firewood."),
                ("Broken crate", new[] { ("stone", 2) }, "Ballast stones rolled out of the hold."),
            };
            var props = new List<Transform>();
            if (world) foreach (Transform t in world.GetComponentsInChildren<Transform>(true)) if (t.parent && t.parent.name == "Props" && (t.name.Contains("Crate") || t.name.Contains("Barrel"))) props.Add(t);
            props = props.OrderBy(t => t.name.Contains("Broken") ? 2 : t.name.Contains("Barrel") ? 1 : 0).ThenBy(t => t.position.x).ToList();
            int crate = 0, barrel = 0, broken = 0, placedLoot = 0;
            foreach (var t in props)
            {
                int idx = t.name.Contains("Broken") ? 6 + broken++ : t.name.Contains("Barrel") ? 3 + barrel++ : crate++;
                if (idx >= loot.Count) continue;
                foreach (var c in t.GetComponents<LootContainer>()) UnityEngine.Object.DestroyImmediate(c);
                var l = t.gameObject.AddComponent<LootContainer>(); var e = loot[idx];
                l.displayName = e.name; l.foundText = e.text; l.SaveId = "loot_" + idx;
                l.contents = e.items.Select(x => new LootContainer.Entry { item = items[x.Item1], count = x.Item2 }).ToArray();
                placedLoot++;
            }
            Log($"Wreck loot containers: {placedLoot} (crates {crate}, barrels {barrel}, broken {broken})");

            // ---- captain's log (next to the first crate)
            Vector3 wreck = MP("ZONE_Shipwreck"), spawn = MP("ZONE_PlayerSpawn");
            Vector3 logPos = props.Count > 0 ? Ground(props[0].position + (spawn - props[0].position).normalized * 1.3f) : Ground(wreck + Vector3.right * 3f);
            var log = GameObject.CreatePrimitive(PrimitiveType.Cube); log.name = "CaptainsLog"; log.transform.SetParent(gp);
            log.transform.position = logPos + Vector3.up * 0.03f; log.transform.localScale = new Vector3(0.22f, 0.05f, 0.3f); log.transform.rotation = Quaternion.Euler(0, 37f, 4f);
            UnityEngine.Object.DestroyImmediate(log.GetComponent<Collider>());
            log.GetComponent<MeshRenderer>().sharedMaterial = mats["M_LogBook"];
            var ex = log.AddComponent<Examinable>(); ex.displayName = "water-stained book"; ex.verb = "Read"; ex.discoveryId = "captains_log"; ex.SaveId = "captains_log";
            ex.thought = "The captain's log. Most pages are ruined... (Journal updated)";

            // ---- beach pickups near the spawn
            int np = 0;
            Vector3 toWreck = (wreck - spawn); toWreck.y = 0; toWreck = toWreck.sqrMagnitude > 0.01f ? toWreck.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, toWreck);
            foreach (var (id, off) in new[] { ("wood", toWreck * 6f + side * 3f), ("stone", toWreck * 9f - side * 2.5f), ("wood", toWreck * 12f + side * 1f), ("stone", -side * 5f + toWreck * 2f) })
            { Pickup(gp, items[id], 1, Ground(spawn + off), "beach_" + np++); }
            Log($"Beach pickups: {np}");

            // ---- cave: stones + bones
            Vector3 cave = MP("ZONE_Cave");
            var stonePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Resources/PFB_RES_Stone_01.prefab");
            int nc = 0;
            if (cave != Vector3.zero && stonePrefab)
            {
                var caveRoot = new GameObject("Cave").transform; caveRoot.SetParent(gp);
                for (int i = 0; i < 3; i++)
                {
                    var p = Ground(cave + Quaternion.Euler(0, i * 120f + 20f, 0) * Vector3.forward * 4.5f);
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(stonePrefab, caveRoot); go.transform.position = p; go.transform.rotation = Quaternion.Euler(0, i * 77f, 0);
                    var nd = Node(go.transform, "Loose stones", "Gather", items["stone"], 1, 4, ToolKind.None, ToolKind.Mine, 30f, true, $"cave_stone_{i}");
                    nd.handAction = Animation.PlayerActions.GatherPlant; nd.toolAction = Animation.PlayerActions.GatherStone; nc++;
                }
                Pickup(caveRoot, items["bone"], 2, Ground(cave + Vector3.left * 2f), "cave_bones");
            }
            else Err("cave marker or stone prefab missing");
            Log($"Cave stone nodes: {nc}");

            // ---- giant footprints between the pond and the meadow
            Vector3 pond = MP("ZONE_Pond"), meadow = MP("ZONE_Meadow"), trike = MP("HAB_Triceratops");
            var printMesh = FootprintMesh();
            var fpRoot = new GameObject("GiantFootprints").transform; fpRoot.SetParent(gp);
            Vector3 dir = meadow - pond; dir.y = 0; float len = dir.magnitude; dir = len > 0.1f ? dir / len : Vector3.forward;
            Vector3 across = Vector3.Cross(Vector3.up, dir);
            Vector3 start = pond + dir * Mathf.Min(len * 0.5f, 45f) - across * 10f;     // trail crosses the path diagonally
            Vector3 walk = (across * 0.8f + dir * 0.6f).normalized;
            Vector3 mid = start;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = start + walk * (i * 2.6f) + Vector3.Cross(Vector3.up, walk) * (i % 2 == 0 ? 0.7f : -0.7f);
                p = Ground(p);
                var go = new GameObject("Footprint_" + i); go.transform.SetParent(fpRoot);
                var n = GroundNormal(p);
                go.transform.position = p + n * 0.02f;
                go.transform.rotation = Quaternion.FromToRotation(Vector3.up, n) * Quaternion.LookRotation(walk);
                go.AddComponent<MeshFilter>().sharedMesh = printMesh; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mats["M_FootprintMud"]; mr.shadowCastingMode = ShadowCastingMode.Off;
                if (i == 4) mid = p;
            }
            var fpEx = new GameObject("FootprintClue"); fpEx.transform.SetParent(fpRoot); fpEx.transform.position = mid + Vector3.up * 0.3f;
            var fe = fpEx.AddComponent<Examinable>(); fe.displayName = "strange tracks"; fe.discoveryId = "footprint"; fe.eventType = GameEventType.FootprintFound; fe.range = 3.5f; fe.SaveId = "footprints";
            fe.thought = "Three toes... each longer than my arm. Whatever made these is huge. And the tracks are fresh.";
            Log($"Footprints: 8 at {mid}");

            // ---- zones
            var zm = new GameObject("[Zones]").AddComponent<ZoneManager>(); zm.transform.SetParent(gp);
            void Z(string id, string name, float def, bool indoor = false, float temp = 0f) { var p = MP(id); if (p == Vector3.zero && id != "ZONE_Cave") { Err("marker missing " + id); return; } zm.zones.Add(new ZoneManager.Zone { id = id, displayName = name, center = p, radius = MR(id, def), indoor = indoor, temperatureOffset = temp }); }
            Z("ZONE_StartBeach", "The Beach", 40f); Z("ZONE_Shipwreck", "The Wreck", 18f); Z("ZONE_Camp", "Camp", 14f); Z("ZONE_Pond", "The Pond", 20f);
            Z("ZONE_Meadow", "The Meadow", 70f); Z("ZONE_Rocky", "Rocky Hills", 45f, false, -2f); Z("ZONE_Cave", "The Cave", 12f, true, -4f);
            foreach (var h in new[] { "HAB_Triceratops", "HAB_Parasaurolophus", "HAB_Ankylosaurus", "HAB_Velociraptor", "HAB_Carnotaurus", "HAB_ApexPredator" }) Z(h, h.Substring(4), 40f);
            Log($"Zones: {zm.zones.Count}");

            // ---- game manager
            var gmGo = new GameObject("[Game]"); gmGo.transform.SetParent(gp);
            var gm = gmGo.AddComponent<GameManager>();
            gm.playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrimalPlayerSetup.GameplayPrefab);
            var spawnT = GameObject.Find("ZONE_PlayerSpawn"); gm.spawnPoint = spawnT ? spawnT.transform : null;
            if (spawnT) { var sp = spawnT.transform.position; spawnT.transform.position = Ground(sp) + Vector3.up * 0.05f; var f = wreck - sp; f.y = 0; if (f.sqrMagnitude > 0.01f) spawnT.transform.rotation = Quaternion.LookRotation(f.normalized); }
            var wreckT = GameObject.Find("ZONE_Shipwreck"); gm.titleCameraFocus = wreckT ? wreckT.transform : null;
            gm.database = AssetDatabase.LoadAssetAtPath<ItemDatabase>(DbPath);
            gm.torchFlamePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/VFX/Prefabs/VFX_CampfireLoop.prefab");
            gm.ghostValid = mats["M_GhostValid"]; gm.ghostInvalid = mats["M_GhostInvalid"]; gm.rainMaterial = mats["M_Rain"];
            AudioClip A(string n) { var c = AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/_Project/Audio/Ambience/AMB_{n}_Loop.wav"); if (!c) Err("ambience missing " + n); return c; }
            gm.ambOcean = A("Ocean"); gm.ambForest = A("Forest"); gm.ambNight = A("Night"); gm.ambWind = A("Wind"); gm.ambRain = A("Rain"); gm.ambStorm = A("Storm");
            gm.herbivoreHabitat = trike; gm.herbivoreHabitatRadius = 25f; gm.campArea = MP("ZONE_Camp"); gm.campRadius = 30f;
            gm.targetSteps = new List<string> { "walk", "search", "water", "explore", "tracks", "observe", "return" };
            Vector3 meadowEdge = meadow - dir * Mathf.Min(MR("ZONE_Meadow", 70f) - 5f, len * 0.6f);
            gm.targetPositions = new List<Vector3> { wreck, wreck, pond, Ground(meadowEdge), mid, trike, MP("ZONE_Camp") };
            // the scene's own camera stays (GameManager adds the third-person rig at runtime)
            Log($"GameManager: spawn {(gm.spawnPoint ? gm.spawnPoint.position.ToString() : "MISSING")}, prefab {(gm.playerPrefab ? "ok" : "MISSING")}, db {(gm.database ? "ok" : "MISSING")}");
            if (!gm.playerPrefab || !gm.database || !gm.spawnPoint) Err("GameManager references missing");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Log("Scene saved " + ScenePath);
        }

        static ResourceNode Node(Transform t, string name, string verb, ItemDefinition yield, int perHit, int charges, ToolKind req, ToolKind faster, float regrow, bool hide, string id)
        {
            foreach (var c in t.GetComponents<ResourceNode>()) UnityEngine.Object.DestroyImmediate(c);
            var n = t.gameObject.AddComponent<ResourceNode>();
            n.displayName = name; n.verb = verb; n.yieldItem = yield; n.yieldPerHit = perHit; n.charges = charges; n.requiredTool = req; n.fasterTool = faster;
            n.regrowHours = regrow; n.hideWhenEmpty = hide; n.SaveId = id;
            return n;
        }

        static WorldPickup Pickup(Transform parent, ItemDefinition item, int count, Vector3 pos, string id)
        {
            GameObject go = item.worldPrefab ? (GameObject)PrefabUtility.InstantiatePrefab(item.worldPrefab, parent) : GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(parent); go.name = "Pickup_" + item.id;
            go.transform.rotation = Quaternion.Euler(0, (id.GetHashCode() & 255) * 1.4f, 0) * Quaternion.Euler(item.worldEuler);
            go.transform.position = pos;
            var b = RendBounds(go); go.transform.position += Vector3.up * (pos.y - b.min.y + 0.005f);
            foreach (var c in go.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(c);
            var pk = go.AddComponent<WorldPickup>(); pk.item = item; pk.count = count; pk.SaveId = "pickup_" + id;
            return pk;
        }

        static Bounds RendBounds(GameObject g)
        {
            var rs = g.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) return new Bounds(g.transform.position, Vector3.one * 0.3f);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        /// <summary>three-toed print (about 0.9 m long), a shallow dish so it reads as pressed into the mud</summary>
        static Mesh FootprintMesh()
        {
            const string path = "Assets/_Project/Art/Models/Generated/MESH_GiantFootprint.asset";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool create = mesh == null; if (create) mesh = new Mesh { name = "MESH_GiantFootprint" };
            var verts = new List<Vector3>(); var tris = new List<int>();
            void Blob(Vector2 c, float rx, float rz, float ang)
            {
                int n = 18; int ci = verts.Count; verts.Add(new Vector3(c.x, -0.015f, c.y));
                float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
                for (int i = 0; i < n; i++)
                {
                    float a = i / (float)n * Mathf.PI * 2f; float x = Mathf.Cos(a) * rx, z = Mathf.Sin(a) * rz;
                    verts.Add(new Vector3(c.x + x * ca - z * sa, 0f, c.y + x * sa + z * ca));
                }
                for (int i = 0; i < n; i++) { tris.Add(ci); tris.Add(ci + 1 + (i + 1) % n); tris.Add(ci + 1 + i); }
            }
            Blob(new Vector2(0, -0.12f), 0.2f, 0.18f, 0f);
            Blob(new Vector2(0, 0.25f), 0.07f, 0.28f, 0f);
            Blob(new Vector2(-0.2f, 0.17f), 0.065f, 0.24f, -0.45f);
            Blob(new Vector2(0.2f, 0.17f), 0.065f, 0.24f, 0.45f);
            mesh.Clear(); mesh.SetVertices(verts); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var nrm = new Vector3[verts.Count]; for (int i = 0; i < nrm.Length; i++) nrm[i] = Vector3.up; mesh.normals = nrm;
            if (create) AssetDatabase.CreateAsset(mesh, path); else EditorUtility.SetDirty(mesh);
            return mesh;
        }
    }
}
