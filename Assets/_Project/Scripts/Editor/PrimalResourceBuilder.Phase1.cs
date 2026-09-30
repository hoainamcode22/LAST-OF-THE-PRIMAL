using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using PrimalFrontier.Animation;
using PrimalFrontier.Audio;
using PrimalFrontier.Items;
using PrimalFrontier.VFX;
using PrimalFrontier.World;
using Object = UnityEngine.Object;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// Phase 1 resource pass (RES, 2026-09-30). Bridge: PrimalResourceBuilder.Phase1 ("" = apply and save the scene,
    /// "dry" = report only). Idempotent. Logs to Documentation/Phase1/R_phase1_build.txt.
    ///  1. shipwreck items wreck_scraps (SURV's definition, created here when missing), wreck_nails, sailcloth: generated
    ///     original models (planks, nails, canvas), rendered icons, listed in the ItemDatabase;
    ///  2. data (BuildData): tool gating (trees and logs need an axe, large rocks and boulders a pick, hands 0; the hand stone
    ///     is a crude tool that never fells or breaks them), salvage definitions + node prefabs;
    ///  3. scene: resource nodes of [Resources] and World/Resources snapped to the current ground, the ones in water moved to
    ///     the nearest dry spot; salvage nodes around World/Shipwreck ([Resources]/Shipwreck); hand resources within 60 m of the
    ///     spawn counted and topped up with natural clusters ([Resources]/StartArea/P1_*); overlap report of the two node sets.
    /// Save ids of new nodes are fixed per slot (rn_wreck_*, rn_p1start_*), so a re-run keeps saves valid.
    /// </summary>
    public static partial class PrimalResourceBuilder
    {
        const string P1Log = "Documentation/Phase1/R_phase1_build.txt";
        const string P1ItemsDir = "Assets/_Project/Data/Items";
        const string P1ItemPrefabDir = "Assets/_Project/Prefabs/Resources/Items";
        const string P1ModelPrefabDir = "Assets/_Project/Prefabs/Resources/Models";
        const string P1IconDir = "Assets/_Project/Art/Icons";
        const string ItemDbPath = "Assets/_Project/Resources/ItemDatabase.asset";
        const string WreckPlanksProp = "Assets/_Project/Prefabs/Environment/PC/PROP_PC_WreckPlanks.prefab";
        const string ShipDebrisProp = "Assets/_Project/Prefabs/Props/PFB_PROP_Ship_Debris.prefab";

        // ------------------------------------------------------------------ bridge
        [PrimalBridgeCommand]
        public static string Phase1(string arg)
        {
            bool dry = !string.IsNullOrEmpty(arg) && arg.Contains("dry");
            _p1ForceIcons = !string.IsNullOrEmpty(arg) && arg.Contains("icons");
            _log = new StringBuilder();
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (scene.isDirty) return "active scene has unsaved changes and is not the island: open the island first";
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            L($"PrimalResourceBuilder.Phase1 {DateTime.Now:yyyy-MM-dd HH:mm} arg '{arg}'{(dry ? " (dry run: no asset or scene change)" : "")}");
            ResourceDatabase db = AssetDatabase.LoadAssetAtPath<ResourceDatabase>(DbPath);
            if (!dry)
            {
                P1Items();
                db = BuildDataInto(_log);
                P1RecipeCheck();
            }
            if (!Setup()) return _log.ToString();
            var root = GameObject.Find("[Resources]");
            if (!root) { L("ERROR no [Resources] root in the scene"); return _log.ToString(); }
            _root = root.transform;
            P1Water(dry);
            if (!dry) { P1Shipwreck(db); Physics.SyncTransforms(); }
            P1StartArea(dry);
            P1Separate(dry);
            P1Overlap();
            Verify();
            if (!dry)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                L("scene saved " + scene.path);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(P1Log));
            File.WriteAllText(dry ? P1Log.Replace(".txt", "_dry.txt") : P1Log, _log.ToString());
            return _log.ToString();
        }

        /// <summary>the full Build calls this after PlaceNew so a rebuilt [Resources] keeps the wreck salvage and the start top-up</summary>
        static void P1AfterPlaceNew(ResourceDatabase db)
        {
            _seaY = SeaLevel();
            P1Shipwreck(db); Physics.SyncTransforms(); P1StartArea(false);
        }

        // ------------------------------------------------------------------ 1. items
        struct P1ItemSpec { public string id, name, desc, icon, prefab; public float weight, fuel; public int stack; }
        static readonly P1ItemSpec[] P1ItemSpecs =
        {
            // wreck_scraps: SURV's definition (PrimalSurvivalBuilder.PcPhaseItems), kept verbatim so both builders agree
            new P1ItemSpec { id = "wreck_scraps", name = "Shipwreck Scraps", weight = 0.3f, stack = 20, fuel = 60f, icon = "ICON_WreckScraps", prefab = "PFB_ITEM_WreckScraps",
                desc = "Torn sailcloth, tarred cord and splinters washed up from the wreck. Cloth for dressings, cord for rope; the tar burns." },
            new P1ItemSpec { id = "wreck_nails", name = "Iron Nails", weight = 0.05f, stack = 50, fuel = 0f, icon = "ICON_WreckNails", prefab = "PFB_ITEM_WreckNails",
                desc = "Bent iron nails pried out of the ship's timbers. The only metal on the island: worth keeping for building." },
            new P1ItemSpec { id = "sailcloth", name = "Sailcloth", weight = 0.4f, stack = 10, fuel = 30f, icon = "ICON_Sailcloth", prefab = "PFB_ITEM_Sailcloth",
                desc = "A torn strip of heavy canvas from the ship's sail. It sheds rain and wind, and it burns." },
        };

        static bool _p1ForceIcons;
        static void P1Items()
        {
            bool newDir = false;
            foreach (var d in new[] { P1ItemsDir, P1ItemPrefabDir, P1ModelPrefabDir, P1IconDir, GenDir }) if (!AssetDatabase.IsValidFolder(d)) { Directory.CreateDirectory(d); newDir = true; }
            if (newDir) AssetDatabase.Refresh();
            var idb = AssetDatabase.LoadAssetAtPath<ItemDatabase>(ItemDbPath);
            if (!idb) { L("WARNING no ItemDatabase at " + ItemDbPath + ": items are created but not listed"); }
            var rope = Item("rope"); var fiber = Item("fiber");
            // models first (the icons are rendered from them)
            var models = new Dictionary<string, GameObject>
            {
                ["wreck_scraps"] = P1ModelPrefab($"{P1ItemPrefabDir}/PFB_ITEM_WreckScraps.prefab", "MESH_ItemWreckScraps", P1MeshScraps, "M_ShipPlanks", "M_SailCloth", "M_Rope"),
                ["wreck_nails"] = P1ModelPrefab($"{P1ItemPrefabDir}/PFB_ITEM_WreckNails.prefab", "MESH_ItemWreckNails", P1MeshNails, "M_IronHoop", "M_ShipPlanks"),
                ["sailcloth"] = P1ModelPrefab($"{P1ItemPrefabDir}/PFB_ITEM_Sailcloth.prefab", "MESH_ItemSailcloth", P1MeshCloth, "M_SailCloth"),
            };
            P1ModelPrefab($"{P1ModelPrefabDir}/MDL_NailedTimber.prefab", "MESH_NailedTimber", P1MeshNailedTimber, "M_ShipBeam", "M_ShipPlanks", "M_IronHoop");
            P1ModelPrefab($"{P1ModelPrefabDir}/MDL_TornSail.prefab", "MESH_TornSail", P1MeshTornSail, "M_ShipBeam", "M_SailCloth");
            foreach (var s in P1ItemSpecs)
            {
                string path = $"{P1ItemsDir}/ITEM_{s.id}.asset";
                var it = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                bool created = !it;
                if (created)
                {
                    it = ScriptableObject.CreateInstance<ItemDefinition>();
                    it.id = s.id; it.displayName = s.name; it.description = s.desc; it.category = ItemCategory.Resource;
                    it.weight = s.weight; it.maxStack = s.stack; it.fuelSeconds = s.fuel;
                    AssetDatabase.CreateAsset(it, path);
                    L($"item {s.id} created ({s.name}, {s.weight} kg, stack {s.stack}, fuel {s.fuel} s)");
                }
                else L($"item {s.id} kept (exists: {it.displayName})");
                // world model: ours unless a real (non-borrowed) one is set
                var model = models[s.id];
                bool borrowed = !it.worldPrefab || (rope && it.worldPrefab == rope.worldPrefab) || (fiber && it.worldPrefab == fiber.worldPrefab);
                if (model && borrowed && it.worldPrefab != model) { it.worldPrefab = model; it.worldEuler = Vector3.zero; L($"  {s.id}: world model {AssetDatabase.GetAssetPath(model)}"); }
                // icon: rendered from the model once (kept afterwards: a hand-made icon with the same name wins)
                string iconPath = $"{P1IconDir}/{s.icon}.png";
                if ((!File.Exists(iconPath) || _p1ForceIcons) && model) { P1RenderIcon(model, iconPath); P1RenderIcon(model, iconPath); }   // twice: the first render may miss a material whose shader was still compiling
                var icon = P1ImportIcon(iconPath);
                bool iconBorrowed = !it.icon || (rope && it.icon == rope.icon) || (fiber && it.icon == fiber.icon);
                if (icon && iconBorrowed && it.icon != icon) { it.icon = icon; L($"  {s.id}: icon {iconPath}"); }
                if (!it.icon) L($"WARNING {s.id} has no icon");
                EditorUtility.SetDirty(it);
                if (idb)
                {
                    if (idb.items == null) idb.items = new List<ItemDefinition>();
                    if (!idb.items.Contains(it))
                    {
                        if (idb.items.Any(x => x && x.id == s.id)) L($"WARNING ItemDatabase already lists another item with id {s.id}: kept that one");
                        else { idb.items.Add(it); EditorUtility.SetDirty(idb); L($"  + ItemDatabase item {s.id}"); }
                    }
                }
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>the recipes that use the shipwreck items (SURV's data): present and resolving?</summary>
        static void P1RecipeCheck()
        {
            var idb = AssetDatabase.LoadAssetAtPath<ItemDatabase>(ItemDbPath);
            var ids = new[] { "wreck_scraps", "wreck_nails", "sailcloth" };
            int n = 0;
            if (idb && idb.recipes != null)
                foreach (var r in idb.recipes.Where(x => x && x.ingredients != null))
                    foreach (var ing in r.ingredients)
                        if (ing.item && ids.Contains(ing.item.id)) { n++; L($"recipe {r.id}: uses {ing.count} {ing.item.id} (resolves) -> {(r.output ? r.output.id : "?")}"); }
            foreach (var want in new[] { "cloth_bandage", "scrap_rope" })
            {
                var a = AssetDatabase.LoadAssetAtPath<RecipeDefinition>($"Assets/_Project/Data/Recipes/RCP_{want}.asset");
                L(a ? $"recipe asset RCP_{want}: present, ingredients {string.Join(" + ", a.ingredients.Select(i => (i.item ? i.item.id : "MISSING") + " x" + i.count))}"
                    : $"recipe asset RCP_{want}: not created yet (SURV's PrimalSurvivalBuilder.Build creates it now that ITEM_wreck_scraps exists)");
            }
            L($"recipes in the ItemDatabase using shipwreck items: {n}");
        }

        // ------------------------------------------------------------------ generated models (original, low poly)
        class MeshB
        {
            public readonly List<Vector3> v = new List<Vector3>(); public readonly List<Vector3> n = new List<Vector3>(); public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<List<int>> sub = new List<List<int>>();
            public List<int> S(int i) { while (sub.Count <= i) sub.Add(new List<int>()); return sub[i]; }
            /// <summary>a box (flat faces, UVs in metres x 2)</summary>
            public void Box(int s, Vector3 c, Vector3 size, Quaternion r)
            {
                var h = size * 0.5f; var t = S(s);
                Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                foreach (var d in dirs)
                {
                    var u = Mathf.Abs(d.y) > 0.5f ? Vector3.right : Vector3.up; var w = Vector3.Cross(d, u);
                    float du = Vector3.Scale(u, h).magnitude, dw = Vector3.Scale(w, h).magnitude, dd = Vector3.Scale(d, h).magnitude;
                    int b = v.Count;
                    for (int k = 0; k < 4; k++)
                    {
                        float a = (k == 0 || k == 3) ? -1 : 1, e = (k < 2) ? -1 : 1;
                        var p = d * dd + u * du * a + w * dw * e;
                        v.Add(c + r * p); n.Add(r * d); uv.Add(new Vector2((a * du + du) * 2f, (e * dw + dw) * 2f));
                    }
                    t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3);
                }
            }
            /// <summary>a cloth sheet (grid, height from f), both sides; skip(i, j) drops a cell (torn edge)</summary>
            public void Sheet(int s, Vector3 c, float sx, float sz, int nx, int nz, Func<float, float, float> f, Quaternion r, Func<int, int, bool> skip = null)
            {
                var t = S(s);
                for (int side = 0; side < 2; side++)
                {
                    int b = v.Count;
                    for (int j = 0; j <= nz; j++)
                        for (int i = 0; i <= nx; i++)
                        {
                            float x = (i / (float)nx - 0.5f) * sx, z = (j / (float)nz - 0.5f) * sz, y = f(i / (float)nx, j / (float)nz);
                            v.Add(c + r * new Vector3(x, y + (side == 0 ? 0.002f : 0f), z)); n.Add(r * (side == 0 ? Vector3.up : Vector3.down)); uv.Add(new Vector2(x * 2f, z * 2f));
                        }
                    for (int j = 0; j < nz; j++)
                        for (int i = 0; i < nx; i++)
                        {
                            if (skip != null && skip(i, j)) continue;
                            int a = b + j * (nx + 1) + i, a2 = a + nx + 1;
                            if (side == 0) { t.Add(a); t.Add(a2); t.Add(a + 1); t.Add(a + 1); t.Add(a2); t.Add(a2 + 1); }
                            else { t.Add(a); t.Add(a + 1); t.Add(a2); t.Add(a + 1); t.Add(a2 + 1); t.Add(a2); }
                        }
                }
            }
            /// <summary>a squashed low-poly ball (cloth wad, rock)</summary>
            public void Ball(int s, Vector3 c, Vector3 radii, int seed)
            {
                var t = S(s); var rng = new System.Random(seed); int b = v.Count; const int lat = 5, lon = 8;
                for (int i = 0; i <= lat; i++)
                    for (int j = 0; j <= lon; j++)
                    {
                        float th = Mathf.PI * i / lat, ph = 2 * Mathf.PI * j / lon; float k = 0.85f + (float)rng.NextDouble() * 0.3f;
                        if (j == lon) k = 1f;
                        var d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                        v.Add(c + Vector3.Scale(d, radii) * (i == 0 || i == lat ? 1f : k)); n.Add(d); uv.Add(new Vector2(j / (float)lon, i / (float)lat));
                    }
                for (int i = 0; i < lat; i++)
                    for (int j = 0; j < lon; j++) { int a = b + i * (lon + 1) + j, a2 = a + lon + 1; t.Add(a); t.Add(a + 1); t.Add(a2); t.Add(a + 1); t.Add(a2 + 1); t.Add(a2); }
            }
            public void Into(Mesh m)
            {
                m.Clear(); m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.subMeshCount = sub.Count;
                for (int i = 0; i < sub.Count; i++) m.SetTriangles(sub[i], i);
                m.RecalculateBounds(); m.RecalculateTangents();
            }
        }

        static Quaternion Y(float deg) => Quaternion.Euler(0f, deg, 0f);

        static void P1MeshScraps(MeshB b)
        {
            // three splintered planks, crossed, a wad of sailcloth and a turn of tarred cord
            b.Box(0, new Vector3(0f, 0.013f, 0f), new Vector3(0.44f, 0.026f, 0.09f), Y(8f));
            b.Box(0, new Vector3(0.21f, 0.013f, 0.03f), new Vector3(0.06f, 0.02f, 0.05f), Y(35f));             // splinter
            b.Box(0, new Vector3(0.02f, 0.038f, 0.01f), new Vector3(0.36f, 0.024f, 0.08f), Y(-28f) * Quaternion.Euler(0, 0, 3f));
            b.Box(0, new Vector3(-0.05f, 0.012f, -0.09f), new Vector3(0.28f, 0.022f, 0.07f), Y(64f));
            b.Ball(1, new Vector3(-0.12f, 0.05f, 0.06f), new Vector3(0.1f, 0.045f, 0.08f), 11);
            b.Box(2, new Vector3(0.03f, 0.052f, 0.0f), new Vector3(0.018f, 0.014f, 0.16f), Y(-28f));
            b.Box(2, new Vector3(0.1f, 0.05f, 0.03f), new Vector3(0.016f, 0.012f, 0.13f), Y(-10f));
        }

        static void P1MeshNails(MeshB b)
        {
            // a splinter of plank with a handful of bent iron nails
            b.Box(1, new Vector3(0f, 0.01f, 0f), new Vector3(0.2f, 0.02f, 0.06f), Y(12f));
            var rng = new System.Random(7);
            for (int i = 0; i < 7; i++)
            {
                var c = new Vector3((float)rng.NextDouble() * 0.2f - 0.1f, 0.028f, (float)rng.NextDouble() * 0.16f - 0.08f);
                var r = Y((float)rng.NextDouble() * 360f);
                bool bent = i % 3 == 0;
                b.Box(0, c + r * new Vector3(0f, 0f, -0.04f), new Vector3(0.008f, 0.008f, bent ? 0.05f : 0.09f), r);
                if (bent) { var r2 = r * Quaternion.Euler(0, 38f, 0); b.Box(0, c + r * new Vector3(0f, 0f, -0.015f) + r2 * new Vector3(0, 0, 0.022f), new Vector3(0.008f, 0.008f, 0.045f), r2); }
                b.Box(0, c + r * new Vector3(0f, 0f, -0.088f), new Vector3(0.022f, 0.022f, 0.005f), r);         // head
            }
        }

        static void P1MeshCloth(MeshB b)
        {
            // a folded, crumpled strip of canvas
            b.Sheet(0, Vector3.zero, 0.5f, 0.34f, 10, 7, (x, z) => 0.012f + 0.018f * Mathf.Sin(x * 17f) * Mathf.Cos(z * 5f) + 0.035f * Mathf.Exp(-Mathf.Pow((x - 0.62f) * 7f, 2f)), Y(0f));
            b.Sheet(0, new Vector3(-0.02f, 0.03f, 0.02f), 0.36f, 0.28f, 8, 6, (x, z) => 0.012f * Mathf.Sin(x * 11f + z * 4f), Y(18f), (i, j) => i == 7 && j % 2 == 0);
        }

        static void P1MeshNailedTimber(MeshB b)
        {
            // a heavy ship beam with a plank nailed across it, nail heads showing and two bent nails sticking up
            b.Box(0, new Vector3(0f, 0.1f, 0f), new Vector3(1.3f, 0.2f, 0.18f), Y(0f));
            b.Box(1, new Vector3(0.12f, 0.215f, 0.05f), new Vector3(0.95f, 0.03f, 0.14f), Y(24f));
            b.Box(1, new Vector3(-0.35f, 0.02f, 0.28f), new Vector3(0.55f, 0.03f, 0.12f), Y(-12f));
            foreach (var x in new[] { -0.3f, 0.05f, 0.42f }) b.Box(2, new Vector3(x, 0.235f, Mathf.Tan(24f * Mathf.Deg2Rad) * -(x - 0.12f) + 0.05f), new Vector3(0.028f, 0.012f, 0.028f), Y(0f));
            b.Box(2, new Vector3(-0.52f, 0.25f, 0f), new Vector3(0.012f, 0.1f, 0.012f), Quaternion.Euler(0, 0, 25f));
            b.Box(2, new Vector3(0.58f, 0.24f, -0.03f), new Vector3(0.012f, 0.08f, 0.012f), Quaternion.Euler(18f, 0, -10f));
        }

        static void P1MeshTornSail(MeshB b)
        {
            // a broken spar on the sand with a torn sheet of sail draped over it
            b.Box(0, new Vector3(0f, 0.05f, 0f), new Vector3(1.7f, 0.09f, 0.09f), Y(0f) * Quaternion.Euler(0, 0, 2f));
            b.Box(0, new Vector3(0.93f, 0.04f, 0.05f), new Vector3(0.22f, 0.07f, 0.06f), Y(28f));              // splintered end
            b.Sheet(1, new Vector3(-0.1f, 0f, 0.02f), 1.5f, 1.15f, 14, 11, (x, z) => 0.105f * Mathf.Exp(-Mathf.Pow((z - 0.5f) * 5.5f, 2f)) + 0.02f * Mathf.Sin(x * 13f) + 0.012f,
                    Y(0f), (i, j) => (j == 10 && (i % 3 != 1)) || (i == 13 && j > 6) || (j == 0 && i > 9));
        }

        static GameObject P1ModelPrefab(string path, string meshName, Action<MeshB> build, params string[] mats)
        {
            string meshPath = $"{GenDir}/{meshName}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath); bool create = !mesh;
            if (create) mesh = new Mesh { name = meshName };
            var mb = new MeshB(); build(mb); mb.Into(mesh);
            if (create) AssetDatabase.CreateAsset(mesh, meshPath); else EditorUtility.SetDirty(mesh);
            var go = new GameObject(Path.GetFileNameWithoutExtension(path));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var list = new List<Material>();
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{mats[Mathf.Min(i, mats.Length - 1)]}.mat");
                if (!m) { L($"WARNING material {mats[Mathf.Min(i, mats.Length - 1)]} missing for {meshName}"); m = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat"); }
                list.Add(m);
            }
            mr.sharedMaterials = list.ToArray();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            var saved = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            L($"model {path}: {mesh.vertexCount} verts, {mesh.triangles.Length / 3} tris, {mesh.subMeshCount} materials ({string.Join(", ", mats)}), size {mesh.bounds.size.x:F2}x{mesh.bounds.size.y:F2}x{mesh.bounds.size.z:F2}");
            return saved;
        }

        static Sprite P1ImportIcon(string path)
        {
            if (!File.Exists(path)) return null;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti && (ti.textureType != TextureImporterType.Sprite || ti.mipmapEnabled || !ti.alphaIsTransparency))
            {
                ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single; ti.mipmapEnabled = false; ti.alphaIsTransparency = true;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>the model on a clear background from above at an angle (same look as the other item icons)</summary>
        static bool P1RenderIcon(GameObject prefab, string path)
        {
            const int S = 256;
            GameObject inst = null, camGo = null, lightGo = null; RenderTexture rt = null; Texture2D tex = null;
            bool fog = RenderSettings.fog;
            try
            {
                var at = new Vector3(0, 900f, 0);
                inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab); inst.transform.position = at; inst.hideFlags = HideFlags.HideAndDontSave;
                var b = Bounds(inst); if (b.size.sqrMagnitude < 1e-6f) b = new Bounds(at, Vector3.one * 0.2f);
                camGo = new GameObject("_IconCam") { hideFlags = HideFlags.HideAndDontSave };
                var cam = camGo.AddComponent<Camera>(); cam.enabled = false; cam.orthographic = true;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0); cam.allowHDR = false; cam.allowMSAA = true;
                var extra = camGo.AddComponent<UniversalAdditionalCameraData>(); extra.renderPostProcessing = false; extra.renderShadows = false; extra.antialiasing = AntialiasingMode.None;
                var dir = Quaternion.Euler(42f, -30f, 0) * Vector3.forward;
                cam.transform.position = b.center - dir * (b.size.magnitude * 2f + 1f); cam.transform.rotation = Quaternion.LookRotation(dir);
                float ext = 0f;
                for (int i = 0; i < 8; i++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var lv = cam.transform.InverseTransformPoint(c); ext = Mathf.Max(ext, Mathf.Abs(lv.x), Mathf.Abs(lv.y));
                }
                cam.orthographicSize = ext * 1.15f + 0.01f; cam.nearClipPlane = 0.01f; cam.farClipPlane = b.size.magnitude * 6f + 5f;
                lightGo = new GameObject("_IconSun") { hideFlags = HideFlags.HideAndDontSave };
                var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.6f; light.color = new Color(1f, 0.95f, 0.85f);
                lightGo.transform.rotation = Quaternion.Euler(48f, -25f, 0);
                RenderSettings.fog = false;
                rt = new RenderTexture(S, S, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
                tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
                bool asyncWas = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
                try { cam.targetTexture = rt; cam.Render(); cam.Render(); }
                finally { ShaderUtil.allowAsyncCompilation = asyncWas; }
                var prev = RenderTexture.active; RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, S, S), 0, 0); tex.Apply();
                RenderTexture.active = prev; cam.targetTexture = null;
                var px = tex.GetPixels32(); int opaque = px.Count(p => p.a > 8);
                if (opaque < 50) { L($"WARNING icon {Path.GetFileName(path)}: render came out empty"); return false; }
                File.WriteAllBytes(path, tex.EncodeToPNG());
                L($"icon {path} rendered ({opaque * 100 / px.Length} % covered)");
                return true;
            }
            catch (Exception e) { L($"WARNING icon {Path.GetFileName(path)}: render failed ({e.Message})"); return false; }
            finally
            {
                RenderSettings.fog = fog;
                if (inst) Object.DestroyImmediate(inst);
                if (camGo) Object.DestroyImmediate(camGo);
                if (lightGo) Object.DestroyImmediate(lightGo);
                if (rt) Object.DestroyImmediate(rt);
                if (tex) Object.DestroyImmediate(tex);
            }
        }

        // ------------------------------------------------------------------ 2. data hooks (called by BuildDataInto / BuildNodePrefabs)
        /// <summary>salvage bonus items and feedback (wood splinters for planks and timber, a soft rustle for canvas)</summary>
        static void SalvageData(ResourceDefinition a)
        {
            switch (a.id)
            {
                case "rare_wreck_scraps": a.bonusItem = Item("wreck_nails"); a.bonusChance = 0.25f; a.hitVfx = VfxId.WoodChips; a.hitVfx2 = VfxId.HitDust; a.handSfx = a.toolSfx = SfxId.BranchSnap; break;
                case "salvage_planks": a.bonusItem = Item("wreck_nails"); a.bonusChance = 0.3f; a.hitVfx = VfxId.WoodChips; a.hitVfx2 = VfxId.HitDust; a.handSfx = a.toolSfx = SfxId.BranchSnap; a.depleteSfx = SfxId.WoodBreak; break;
                case "salvage_nails": a.bonusItem = Item("wreck_scraps"); a.bonusChance = 0.35f; a.hitVfx = VfxId.WoodChips; a.hitVfx2 = VfxId.HitDust; a.handSfx = a.toolSfx = SfxId.WoodChop; a.depleteSfx = SfxId.WoodBreak; break;
                case "salvage_sail": a.bonusItem = Item("wreck_scraps"); a.bonusChance = 0.3f; a.hitVfx = VfxId.HitDust; a.hitVfx2 = VfxId.None; a.handSfx = a.toolSfx = SfxId.LeafRustle; break;
            }
        }

        static IEnumerable<NodeKind> SalvageKinds(StringBuilder log)
        {
            var list = new List<NodeKind>();
            var scrapsModel = $"{P1ItemPrefabDir}/PFB_ITEM_WreckScraps.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(scrapsModel)) list.Add(new NodeKind { prefab = "Resource_Rare_WreckScraps", def = "rare_wreck_scraps", model = scrapsModel, scale = 1f, col = Col.Trigger, lod = true, copies = 2 });
            string planks = AssetDatabase.LoadAssetAtPath<GameObject>(WreckPlanksProp) ? WreckPlanksProp : ShipDebrisProp;
            list.Add(new NodeKind { prefab = "Resource_Salvage_Planks", def = "salvage_planks", model = planks, scale = 1f, col = Col.Trigger, lod = true, fit = 1.5f });
            list.Add(new NodeKind { prefab = "Resource_Salvage_Nails", def = "salvage_nails", model = $"{P1ModelPrefabDir}/MDL_NailedTimber.prefab", scale = 1f, col = Col.Trigger, lod = true });
            list.Add(new NodeKind { prefab = "Resource_Salvage_Sail", def = "salvage_sail", model = $"{P1ModelPrefabDir}/MDL_TornSail.prefab", scale = 1f, col = Col.Trigger, lod = true });
            return list;
        }

        // ------------------------------------------------------------------ 3a. water and ground
        static float _seaY;
        static float SeaLevel()
        {
            foreach (var n in new[] { "Water/ENV_Ocean", "Water/PF_Ocean" }) { var g = GameObject.Find(n); if (g && g.activeInHierarchy) return g.transform.position.y; }
            return 0f;
        }
        static bool P1Wet(Vector3 p) => InWater(p) || Ground(p) < _seaY + 0.45f;

        static List<ResourceNode> P1Set(string which)
        {
            if (which == "[Resources]") { var r = GameObject.Find("[Resources]"); return r ? r.GetComponentsInChildren<ResourceNode>(true).ToList() : new List<ResourceNode>(); }
            var t = PrimalFrontier.Core.SceneRoots.Legacy("World/Resources");
            return t ? t.GetComponentsInChildren<ResourceNode>(true).ToList() : new List<ResourceNode>();
        }

        static void P1Water(bool dry)
        {
            _seaY = SeaLevel();
            L($"sea level {_seaY:F2} m; fresh water cells {_water.Count}");
            foreach (var set in new[] { "[Resources]", "World/Resources" })
            {
                var nodes = P1Set(set);
                int wet = 0, moved = 0, left = 0, snapped = 0, fish = 0; float worstOff = 0f; var lines = new List<string>();
                foreach (var n in nodes)
                {
                    if (!n.gameObject.activeInHierarchy) continue;
                    var t = n.transform; var p = t.position;
                    bool isFish = n.definition && n.definition.category == ResourceCategory.Fish;
                    if (isFish) { fish++; P1Fish(n, dry, lines); continue; }
                    float sink = n.definition && n.definition.category == ResourceCategory.Stone ? 0.04f : 0.02f;
                    if (P1Wet(p) || WaterDistance(p, 1.5f) < 1.0f)          // in the water or right at its edge
                    {
                        wet++;
                        if (P1FindDry(p, Mathf.Max(0.35f, n.radius), out var q))
                        {
                            q.y = Ground(q) - sink;
                            lines.Add($"{(dry ? "would move" : "moved")} {t.name} {V(p)} -> {V(q)} ({Flat(q - p).magnitude:F1} m)");
                            if (!dry) { t.position = q; P1Dirty(t); Physics.SyncTransforms(); }
                            moved++;
                        }
                        else { left++; lines.Add($"NO DRY SPOT within 24 m: {t.name} {V(p)} (left as is)"); }
                        continue;
                    }
                    float g = Ground(p) - sink, off = p.y - g;
                    if (Mathf.Abs(off) > 0.05f)
                    {
                        snapped++; if (Mathf.Abs(off) > Mathf.Abs(worstOff)) worstOff = off;
                        if (!dry) { t.position = new Vector3(p.x, g, p.z); P1Dirty(t); }
                    }
                }
                L($"{set}: {nodes.Count} nodes ({fish} fish shoals skipped): in water {wet}, {(dry ? "would move" : "moved")} to dry ground {moved}, no dry spot {left}; " +
                  $"{(dry ? "would snap" : "snapped")} to the ground {snapped} (worst {worstOff:F2} m)");
                foreach (var l in lines.Take(60)) L("   " + l);
            }
            // converted scenery (ENV's rocks, fallen logs) standing in water: the node is switched off, the rock stays (scenery)
            int sceneryOff = 0; var offl = new List<string>();
            var world = GameObject.Find("World");
            foreach (var g in new[] { "Rocks", "Vegetation" })
            {
                var gt = PrimalFrontier.Core.SceneRoots.Legacy("World/" + g); if (!gt) continue;
                foreach (var n in gt.GetComponentsInChildren<ResourceNode>(false))
                {
                    if (!n.enabled || !P1Wet(n.transform.position)) continue;
                    sceneryOff++; offl.Add($"{g}/{n.name} {V(n.transform.position)}");
                    if (!dry) { n.enabled = false; EditorUtility.SetDirty(n); if (PrefabUtility.IsPartOfPrefabInstance(n)) PrefabUtility.RecordPrefabInstancePropertyModifications(n); }
                }
            }
            L($"World/Rocks + World/Vegetation nodes standing in water: {sceneryOff} {(dry ? "would be" : "")} switched off (the model stays as scenery){(offl.Count > 0 ? ": " + string.Join("; ", offl.Take(12)) : "")}");
            Physics.SyncTransforms();
        }

        /// <summary>a fish shoal lies on the bed of shallow water (15-150 cm): snapped there, or moved to the nearest such spot within 20 m, else switched off</summary>
        static void P1Fish(ResourceNode n, bool dry, List<string> lines)
        {
            var t = n.transform; var p = t.position;
            bool Ok(Vector3 q, out float depth)
            {
                depth = 0f; var c = Cell1(q); if (!_water.TryGetValue(c, out var wy)) return false;
                for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++) if (!_water.ContainsKey(new Vector2Int(c.x + dx, c.y + dz))) return false;
                depth = wy - Ground(q); return depth >= 0.15f && depth <= 1.5f;
            }
            Vector3 best = p; float bd = 0f; bool found = Ok(p, out bd);
            for (float d = 1f; d <= 20f && !found; d += 1f)
                for (int k = 0; k < 24 && !found; k++)
                {
                    float a = k / 24f * Mathf.PI * 2f; var q = p + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                    if (Ok(q, out bd)) { best = q; found = true; }
                }
            if (!found) { lines.Add($"fish {t.name} {V(p)}: no shallow water within 20 m, {(dry ? "would switch" : "switched")} off"); if (!dry) { t.gameObject.SetActive(false); P1Dirty(t); } return; }
            var to = new Vector3(best.x, Ground(best) + 0.05f, best.z);
            if ((to - p).sqrMagnitude < 0.01f) return;
            lines.Add($"fish {t.name} {V(p)} -> {V(to)} (depth {bd:F2} m)");
            if (!dry) { t.position = to; n.cueHeight = bd - 0.05f; EditorUtility.SetDirty(n); P1Dirty(t); if (PrefabUtility.IsPartOfPrefabInstance(n)) PrefabUtility.RecordPrefabInstancePropertyModifications(n); }
        }

        /// <summary>two nodes of one set within 0.3 m of each other: the second moves to the nearest free dry spot</summary>
        static void P1Separate(bool dry)
        {
            int moved = 0;
            foreach (var set in new[] { "[Resources]", "World/Resources" })
            {
                var s = P1Set(set).Where(n => n.gameObject.activeInHierarchy).ToList();
                for (int i = 0; i < s.Count; i++)
                    for (int j = i + 1; j < s.Count; j++)
                    {
                        if (Flat(s[i].transform.position - s[j].transform.position).magnitude >= 0.3f) continue;
                        var t = s[j].transform;
                        if (P1FindDry(t.position, Mathf.Max(0.35f, s[j].radius), out var q))
                        {
                            L($"   separated {t.name} from {s[i].name}: {V(t.position)} -> {V(q)}");
                            if (!dry) { q.y = Ground(q) - 0.02f; t.position = q; P1Dirty(t); Physics.SyncTransforms(); }
                            moved++;
                        }
                    }
            }
            L($"nodes on top of each other (< 0.3 m) separated: {moved}");
        }

        static void P1Dirty(Transform t)
        {
            EditorUtility.SetDirty(t);
            if (PrefabUtility.IsPartOfPrefabInstance(t)) PrefabUtility.RecordPrefabInstancePropertyModifications(t);
        }

        /// <summary>the nearest dry, free spot (rings 1.5 m apart, up to 24 m)</summary>
        static bool P1FindDry(Vector3 p, float r, out Vector3 q)
        {
            for (float d = 1.5f; d <= 24f; d += 1.5f)
            {
                int steps = Mathf.Clamp(Mathf.RoundToInt(d * 4f), 12, 64);
                float best = float.MaxValue; Vector3 pick = Vector3.zero; bool any = false;
                for (int k = 0; k < steps; k++)
                {
                    float a = k / (float)steps * Mathf.PI * 2f; var c = p + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                    if (P1Wet(c) || WaterDistance(c, 2.5f) < 2.5f || !Free(c, r)) continue;
                    float score = Mathf.Abs(Ground(c) - Ground(p));             // the gentlest nearby ground
                    if (score < best) { best = score; pick = c; any = true; }
                }
                if (any) { q = pick; return true; }
            }
            q = p; return false;
        }

        // ------------------------------------------------------------------ 3b. shipwreck salvage
        static void P1Shipwreck(ResourceDatabase db)
        {
            var wreck = PrimalFrontier.Core.SceneRoots.Legacy("World/Shipwreck");
            if (!wreck) { L("WARNING World/Shipwreck missing: no salvage placed"); return; }
            var rs = wreck.GetComponentsInChildren<Renderer>().Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
            if (rs.Length == 0) { L("WARNING World/Shipwreck has no renderers"); return; }
            var hull = rs[0].bounds; foreach (var r in rs) hull.Encapsulate(r.bounds);
            var old = _root.Find("Shipwreck"); if (old) Object.DestroyImmediate(old.gameObject);
            Physics.SyncTransforms();
            var grp = new GameObject("Shipwreck").transform; grp.SetParent(_root);
            var c = hull.center; float r0 = Mathf.Max(hull.extents.x, hull.extents.z);
            L($"shipwreck {wreck.childCount} parts, bounds {V(hull.min)}..{V(hull.max)}, centre {V(c)}, radius {r0:F1} m, ground {Ground(c):F2}");
            var rng = new System.Random(Seed + 77);
            var want = new (string prefab, int n)[] { ("Resource_Salvage_Planks", 3), ("Resource_Salvage_Nails", 2), ("Resource_Salvage_Sail", 2), ("Resource_Rare_WreckScraps", 3) };
            int placed = 0; var got = new List<string>();
            foreach (var (prefab, n) in want)
            {
                if (!NodePrefab(prefab)) { L($"WARNING {prefab} prefab missing: skipped"); continue; }
                int ok = 0;
                for (int i = 0; i < n; i++)
                {
                    GameObject go = null;
                    for (int ring = 0; ring < 4 && !go; ring++)
                        go = P1PlaceDry(prefab, c, r0 * 0.55f + 0.8f + ring * 2f, r0 * 0.55f + 4.5f + ring * 3f, rng, grp);
                    if (!go) { L($"   no dry free spot around the wreck for {prefab} #{i}"); continue; }
                    var node = go.GetComponent<ResourceNode>(); var def = node.definition;
                    string id = $"rn_wreck_{def.id}_{i}";
                    node.SaveId = id; node.charges = def.RollAmount(Hash(id)); go.name = $"{prefab}_{i:00}";
                    PrefabUtility.RecordPrefabInstancePropertyModifications(node);
                    ok++; placed++;
                    got.Add($"{go.name} {V(go.transform.position)} ({Flat(go.transform.position - c).magnitude:F1} m, {node.charges} {def.item.id})");
                }
                L($"   {prefab}: {ok} / {n}");
            }
            L($"shipwreck salvage: {placed} nodes under [Resources]/Shipwreck");
            foreach (var g in got) L("   " + g);
        }

        /// <summary>Place() on dry ground only (no sea, no fresh water, 2.5 m off any water edge)</summary>
        static GameObject P1PlaceDry(string prefab, Vector3 near, float minR, float maxR, System.Random rng, Transform parent)
        {
            for (int t = 0; t < 6; t++)
            {
                var go = Place(prefab, near, minR, maxR, rng, parent, "p1");
                if (!go) continue;
                var p = go.transform.position;
                if (!P1Wet(p) && WaterDistance(p, 2.5f) >= 2.5f) return go;
                Object.DestroyImmediate(go); Physics.SyncTransforms();
            }
            return null;
        }

        // ------------------------------------------------------------------ 3c. start area: hand resources within 60 m of the spawn
        struct P1Count { public int nodes, units; }
        static Dictionary<string, P1Count> P1HandCount(Vector3 spawn, float radius)
        {
            var res = new Dictionary<string, P1Count>();
            foreach (var n in Object.FindObjectsByType<ResourceNode>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!n.enabled || !n.definition || n.definition.handsFactor <= 0f || !n.definition.item) continue;
                if (Flat(n.transform.position - spawn).magnitude > radius) continue;
                string k = n.definition.item.id;
                var c = res.TryGetValue(k, out var v) ? v : default; c.nodes++; c.units += n.charges; res[k] = c;
            }
            return res;
        }

        static readonly (string item, int units)[] P1StartNeed = { ("stone", 20), ("wood", 16), ("fiber", 18), ("bone", 1) };

        static void P1StartArea(bool dry)
        {
            var spawnT = GameObject.Find("ZONE_PlayerSpawn"); Vector3 spawn = spawnT ? spawnT.transform.position : Vector3.zero;
            var start = _root.Find("StartArea"); if (!start) { start = new GameObject("StartArea").transform; start.SetParent(_root); }
            if (!dry) foreach (var t in start.Cast<Transform>().Where(x => x.name.StartsWith("P1_")).ToList()) Object.DestroyImmediate(t.gameObject);
            Physics.SyncTransforms();
            var before = P1HandCount(spawn, 60f);
            // what the first tools cost (the live recipes): stone axe, stone pick, flint knife, plus a campfire
            var idb = AssetDatabase.LoadAssetAtPath<ItemDatabase>(ItemDbPath);
            if (idb) foreach (var rid in new[] { "stone_axe", "stone_pick", "flint_knife", "campfire", "rope" })
                {
                    var r = idb.recipes.FirstOrDefault(x => x && x.id == rid);
                    if (r) L($"recipe {rid}: {string.Join(" + ", r.ingredients.Select(i => i.count + " " + (i.item ? i.item.id : "?")))}");
                }
            L($"hand-gatherable within 60 m of the spawn {V(spawn)} (before top-up): " + string.Join(", ", before.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value.nodes} nodes / {k.Value.units} units")));
            var wreck = PrimalFrontier.Core.SceneRoots.LegacyObject("World/Shipwreck"); Vector3 wc = wreck ? wreck.transform.position : spawn;
            var rng = new System.Random(Seed + 91);
            int cluster = 0, added = 0;
            foreach (var (item, need) in P1StartNeed)
            {
                int have = before.TryGetValue(item, out var v) ? v.units : 0;
                if (have >= need) { L($"   {item}: {have} units, need {need}: enough"); continue; }
                L($"   {item}: {have} units, need {need}: {(dry ? "would add" : "adding")} clusters");
                if (dry) continue;
                int guard = 0;
                while (have < need && guard++ < 8)
                {
                    // alternate: the beach between the spawn and the wreck, then the forest edge behind the beach
                    bool beach = cluster % 2 == 0;
                    if (!P1ClusterCentre(spawn, wc, beach, rng, out var cc)) { L($"   no free centre for a {(beach ? "beach" : "forest edge")} cluster"); cluster++; continue; }
                    var biome = BiomeAt(cc);
                    string[] mix = item switch
                    {
                        "stone" => beach ? new[] { "Resource_Stone_Small", "Resource_Stone_Small", "Resource_Wood_Driftwood" } : new[] { "Resource_Stone_Small", "Resource_Stone_Medium", "Resource_Fiber_Plant" },
                        "wood" => beach ? new[] { "Resource_Wood_Driftwood", "Resource_Wood_Branch", "Resource_Stone_Small" } : new[] { "Resource_Wood_Branch", "Resource_Wood_Branch", "Resource_Fiber_Fern" },
                        "fiber" => beach ? new[] { "Resource_Fiber_LongGrass", "Resource_Fiber_LongGrass", "Resource_Wood_Branch" } : new[] { "Resource_Fiber_Plant", "Resource_Fiber_LongGrass", "Resource_Wood_Branch" },
                        _ => new[] { "Resource_Rare_Bones" },
                    };
                    var grp = new GameObject($"P1_{cluster:00}_{item}_{biome}").transform; grp.SetParent(start);
                    int made = 0;
                    foreach (var pf in mix)
                    {
                        var go = P1PlaceDry(pf, cc, made == 0 ? 0f : 0.9f, 3.5f, rng, grp);
                        if (!go) continue;
                        var node = go.GetComponent<ResourceNode>(); var def = node.definition;
                        string id = $"rn_p1start_{cluster:00}_{made}";
                        node.SaveId = id; node.charges = def.RollAmount(Hash(id)); go.name = $"{pf}_P1_{cluster:00}_{made}";
                        PrefabUtility.RecordPrefabInstancePropertyModifications(node);
                        if (def.item && def.item.id == item) have += node.charges;
                        made++; added++;
                    }
                    L($"   + {grp.name} at {V(cc)} ({Flat(cc - spawn).magnitude:F0} m from the spawn): {made} nodes");
                    if (made == 0) Object.DestroyImmediate(grp.gameObject);
                    cluster++;
                }
            }
            if (!dry)
            {
                Physics.SyncTransforms();
                var after = P1HandCount(spawn, 60f);
                L($"hand-gatherable within 60 m after the top-up ({added} nodes added): " + string.Join(", ", after.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value.nodes} nodes / {k.Value.units} units")));
            }
            var gated = Object.FindObjectsByType<ResourceNode>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(n => n.enabled && n.definition && n.definition.handsFactor <= 0f && Flat(n.transform.position - spawn).magnitude <= 60f).GroupBy(n => n.definition.id);
            L("tool-gated nodes within 60 m (hands 0): " + string.Join(", ", gated.Select(g => $"{g.Key} {g.Count()}")));
        }

        /// <summary>a cluster centre: on the beach between the spawn and the wreck, or at the forest edge 20-55 m from the spawn</summary>
        static bool P1ClusterCentre(Vector3 spawn, Vector3 wreck, bool beach, System.Random rng, out Vector3 c)
        {
            for (int i = 0; i < 400; i++)
            {
                Vector3 p;
                if (beach) { float t = 0.2f + (float)rng.NextDouble() * 0.6f; p = Vector3.Lerp(spawn, wreck, t) + new Vector3((float)rng.NextDouble() - 0.5f, 0, (float)rng.NextDouble() - 0.5f) * 18f; }
                else { float a = (float)rng.NextDouble() * Mathf.PI * 2f, d = 20f + (float)rng.NextDouble() * 35f; p = spawn + new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d); }
                if (Flat(p - spawn).magnitude > 56f || Flat(p - spawn).magnitude < 6f) continue;
                if (P1Wet(p) || WaterDistance(p, 3f) < 3f || !Free(p, 1.2f)) continue;
                var b = BiomeAt(p);
                if (beach ? b != Biome.Beach : (b != Biome.ForestEdge && b != Biome.Forest && b != Biome.Meadow)) continue;
                // natural spacing: not on top of another node group
                bool crowded = Object.FindObjectsByType<ResourceNode>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Count(n => Flat(n.transform.position - p).magnitude < 4f) > 1;
                if (crowded) continue;
                c = p; return true;
            }
            c = Vector3.zero; return false;
        }

        // ------------------------------------------------------------------ 3d. overlap of the two node sets
        static void P1Overlap()
        {
            var a = P1Set("[Resources]"); var b = P1Set("World/Resources");
            L($"node sets: [Resources] {a.Count} ({a.Count(n => n.gameObject.activeInHierarchy && n.enabled)} active), World/Resources {b.Count} ({b.Count(n => n.gameObject.activeInHierarchy && n.enabled)} active)");
            int near05 = 0, near15 = 0, same05 = 0; var lines = new List<string>();
            foreach (var n in b)
            {
                float best = float.MaxValue; ResourceNode bn = null;
                foreach (var m in a) { float d = Flat(m.transform.position - n.transform.position).magnitude; if (d < best) { best = d; bn = m; } }
                if (best < 1.5f) near15++;
                if (best < 0.5f)
                {
                    near05++;
                    bool same = bn && bn.definition && n.definition && bn.definition.item == n.definition.item;
                    if (same) same05++;
                    lines.Add($"{PathOf(n.transform)} ~ {PathOf(bn.transform)} {best:F2} m{(same ? " (same item: doubled)" : "")}");
                }
            }
            L($"World/Resources vs [Resources]: {near15} within 1.5 m, {near05} within 0.5 m ({same05} same item at the same spot)");
            foreach (var l in lines.Take(20)) L("   " + l);
            foreach (var (name, set) in new[] { ("[Resources]", a), ("World/Resources", b) })
            {
                int dup = 0; var dl = new List<string>();
                for (int i = 0; i < set.Count; i++)
                    for (int j = i + 1; j < set.Count; j++)
                        if (Flat(set[i].transform.position - set[j].transform.position).magnitude < 0.3f) { dup++; if (dl.Count < 10) dl.Add($"{set[i].name} ~ {set[j].name}"); }
                L($"{name}: {dup} pairs within 0.3 m of each other" + (dl.Count > 0 ? ": " + string.Join("; ", dl) : ""));
            }
            // World/Resources by kind (the older hand-placed set)
            L("World/Resources kinds: " + string.Join(", ", b.GroupBy(n => n.definition ? n.definition.id : "legacy").Select(g => $"{g.Key} {g.Count()}")));
        }

        // ------------------------------------------------------------------ review captures (edit mode, no play mode)
        /// <summary>bridge: eye-height views of the wreck salvage and the start area, plus the new item models, to Documentation/Screenshots/Phase1/R_*.png</summary>
        [PrimalBridgeCommand]
        public static string Phase1Capture(string arg)
        {
            var sb = new StringBuilder(); const string dir = "Documentation/Screenshots/Phase1";
            Directory.CreateDirectory(dir);
            var camGo = new GameObject("__R_P1Cam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>(); var main = Camera.main; if (main) cam.CopyFrom(main);
            cam.enabled = false; cam.nearClipPlane = 0.05f; cam.farClipPlane = 1500f; cam.fieldOfView = 55f;
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            void Shot(string name, Vector3 eye, Vector3 at)
            {
                cam.transform.position = eye; cam.transform.rotation = Quaternion.LookRotation(at - eye);
                bool asyncWas = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
                try { cam.targetTexture = rt; cam.Render(); cam.Render(); } finally { ShaderUtil.allowAsyncCompilation = asyncWas; }
                var prev = RenderTexture.active; RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply(); RenderTexture.active = prev; cam.targetTexture = null;
                File.WriteAllBytes($"{dir}/{name}.png", tex.EncodeToPNG()); sb.AppendLine($"wrote {dir}/{name}.png eye {V(eye)} at {V(at)}");
            }
            try
            {
                var t = Terrain.activeTerrain;
                float G(Vector3 p) => t ? t.SampleHeight(p) + t.transform.position.y : p.y;
                var wreckGrp = GameObject.Find("[Resources]/Shipwreck");
                if (wreckGrp && wreckGrp.transform.childCount > 0)
                {
                    var nodes = wreckGrp.transform.Cast<Transform>().ToList();
                    var c = nodes.Aggregate(Vector3.zero, (a, n) => a + n.position) / nodes.Count;
                    var hull = PrimalFrontier.Core.SceneRoots.LegacyObject("World/Shipwreck"); var hc = hull ? hull.transform.position : c + Vector3.forward * 10f;
                    var away = Flat(c - hc).normalized; if (away.sqrMagnitude < 0.1f) away = Vector3.back;
                    var eye = c + away * 7f; eye.y = G(eye) + 1.7f;
                    Shot("R_wreck_salvage", eye, c + Vector3.up * 0.2f);
                    foreach (var n in nodes.Where(n => n.name.EndsWith("_00")))
                    {
                        var e2 = n.position + away * 2.2f + Vector3.Cross(Vector3.up, away) * 0.8f; e2.y = G(e2) + 1.4f;
                        Shot("R_node_" + n.name.Replace("Resource_", "").Replace("_00", ""), e2, n.position + Vector3.up * 0.15f);
                    }
                }
                var spawnT = GameObject.Find("ZONE_PlayerSpawn");
                if (spawnT) { var s = spawnT.transform.position; var e = s + new Vector3(0, 0, 0); e.y = G(e) + 1.7f; Shot("R_spawn_view", e, s + (PrimalFrontier.Core.SceneRoots.LegacyObject("World/Shipwreck") ? Flat(PrimalFrontier.Core.SceneRoots.LegacyObject("World/Shipwreck").transform.position - s).normalized * 12f : Vector3.forward * 12f)); }
                // the three item models side by side, on a patch of beach near the spawn (temporary, removed)
                if (spawnT)
                {
                    var s = spawnT.transform.position + Vector3.right * 3f; var tmp = new List<GameObject>(); int i = 0;
                    foreach (var id in new[] { "PFB_ITEM_WreckScraps", "PFB_ITEM_WreckNails", "PFB_ITEM_Sailcloth" })
                    {
                        var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"{P1ItemPrefabDir}/{id}.prefab"); if (!pf) continue;
                        var g = (GameObject)PrefabUtility.InstantiatePrefab(pf); g.hideFlags = HideFlags.HideAndDontSave;
                        var p = s + Vector3.right * (i++ * 0.6f); p.y = G(p) + 0.01f; g.transform.position = p; tmp.Add(g);
                    }
                    var mid = s + Vector3.right * 0.6f; mid.y = G(mid);
                    var e = mid + new Vector3(0f, 0.9f, -1.1f);
                    Shot("R_items_on_ground", e, mid + Vector3.up * 0.05f);
                    foreach (var g in tmp) Object.DestroyImmediate(g);
                }
            }
            finally { Object.DestroyImmediate(camGo); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); }
            return sb.ToString();
        }
    }
}
