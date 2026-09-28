using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.Story;
using PrimalFrontier.Survival;
using PrimalFrontier.World;

namespace PrimalFrontier.Tests
{
    /// <summary>Phase 7-12: item data, inventory rules, crafting, the island scene boot, gathering, water, fire, save/load.</summary>
    public class SurvivalLoopTests
    {
        static ItemDatabase Db => ItemDatabase.Instance;

        [Test] public void Database_Has_Items_And_15_To_40_Recipes()
        {
            Assert.IsNotNull(Db, "Resources/ItemDatabase");
            Assert.GreaterOrEqual(Db.items.Count, 15);
            Assert.That(Db.recipes.Count, Is.InRange(15, 40));                  // upgrade: resources, sword, bow, bandage...
            foreach (var id in new[] { "stone_axe", "stone_pick", "flint_knife", "stone_spear", "torch", "campfire", "water_container", "shelter", "storage", "bedroll", "bow", "arrows" })
                Assert.IsNotNull(Db.Recipe(id), "recipe " + id);
            foreach (var it in Db.items) { Assert.IsNotNull(it.icon, "icon " + it.id); }
            foreach (var id in new[] { "campfire", "shelter", "storage", "bedroll" }) Assert.IsNotNull(Db.Item(id).placePrefab, "prefab " + id);
        }

        [Test] public void Inventory_Stacks_Weight_NoDuplication()
        {
            var go = new GameObject("inv"); var inv = go.AddComponent<InventorySystem>(); inv.raiseGameEvents = false; inv.maxWeight = 10f; inv.EnsureSlots();
            var wood = Db.Item("wood"); var axe = Db.Item("stone_axe");
            int left = inv.Add(wood, 25);                                     // 1 kg each, 10 kg limit
            Assert.AreEqual(15, left, "weight limit"); Assert.AreEqual(10, inv.Count(wood));
            Assert.IsTrue(inv.Remove(wood, 4)); Assert.AreEqual(6, inv.Count(wood));
            Assert.IsFalse(inv.Remove(wood, 7)); Assert.AreEqual(6, inv.Count(wood), "failed remove changes nothing");
            Assert.AreEqual(0, inv.Add(axe, 1)); Assert.AreEqual(axe, inv.Get(0)?.item, "tools go to the hotbar");
            int total = inv.Count(wood); int from = System.Array.FindIndex(inv.Slots, s => s != null && s.item == wood);
            inv.Split(from); Assert.AreEqual(total, inv.Count(wood), "split keeps the total");
            Object.Destroy(go);
        }

        [Test] public void Crafting_Consumes_Then_Produces_And_Cancel_Refunds()
        {
            var go = new GameObject("crafter"); var inv = go.AddComponent<InventorySystem>(); inv.raiseGameEvents = false; inv.EnsureSlots();
            var cr = go.AddComponent<CraftingSystem>(); cr.inventory = inv; cr.InitKnown(Db);
            var axe = Db.Recipe("stone_axe");
            Assert.IsNotNull(cr.Check(axe), "missing materials reported");
            inv.Add(Db.Item("stone"), 4); inv.Add(Db.Item("wood"), 2); inv.Add(Db.Item("rope"), 2);
            Assert.IsNull(cr.Check(axe));
            Assert.IsTrue(cr.Enqueue(axe)); Assert.AreEqual(2, inv.Count(Db.Item("stone")), "ingredients taken when queued");
            Assert.IsTrue(cr.Enqueue(axe)); cr.Cancel(1); Assert.AreEqual(2, inv.Count(Db.Item("stone")), "cancel refunds");
            cr.CompleteFirstNow(); Assert.AreEqual(1, inv.Count(Db.Item("stone_axe")));
            Object.Destroy(go);
        }

        // ------------------------------------------------------------------ island scene
        GameManager _gm;
        IEnumerator LoadIsland()
        {
            GameManager.ForceShowTitle = false; GameManager.ForcePlayIntro = false;
            PlayerInputReader.Simulate = true;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/Island_VerticalSlice.unity", new LoadSceneParameters(LoadSceneMode.Single));
#endif
            float t = 0; while ((_gm = GameManager.Instance) == null || _gm.State != GameState.Playing) { t += Time.unscaledDeltaTime; if (t > 20f) break; yield return null; }
            Assert.IsNotNull(_gm, "GameManager"); Assert.AreEqual(GameState.Playing, _gm.State, "game started");
            yield return new WaitForSeconds(1f);
        }

        [TearDown] public void Cleanup() { GameManager.ForceShowTitle = null; GameManager.ForcePlayIntro = null; PlayerInputReader.Simulate = false; Time.timeScale = 1f; }
        [UnityTearDown] public IEnumerator Unload() { yield return TestScenes.ClearIfGameplayLeft(); }

        [UnityTest] public IEnumerator Island_Boots_Player_Tutorial_Nodes()
        {
            yield return LoadIsland();
            var p = _gm.Player; Assert.IsNotNull(p);
            Assert.IsTrue(p.GetComponent<CharacterController>().isGrounded || p.GetComponent<PlayerMotor>().IsGrounded, "player stands on the beach " + p.transform.position);
            Assert.IsTrue(TutorialManager.Instance.Running && TutorialManager.Instance.Index == 0, "tutorial at step 1");
            Assert.Greater(Interactable.Active.OfType<ResourceNode>().Count(), 30, "resource nodes");
            Assert.GreaterOrEqual(Interactable.Active.OfType<LootContainer>().Count(), 6, "wreck loot");
            Assert.GreaterOrEqual(WaterSource.All.Count(w => w.fresh), 1, "fresh water");
            Assert.Greater(ZoneManager.Instance.zones.Count, 8);
        }

        [UnityTest] public IEnumerator Gather_Craft_Fire_Cook_Save_Load()
        {
            yield return LoadIsland();
            var p = _gm.Player; var pi = p.GetComponent<PlayerInteraction>(); var inv = p.GetComponent<InventorySystem>(); var motor = p.GetComponent<PlayerMotor>();
            // gather from the nearest loose-stone node
            var node = Interactable.Active.OfType<ResourceNode>().Where(n => n.yieldItem && n.yieldItem.id == "stone" && n.requiredTool == ToolKind.None).OrderBy(n => (n.transform.position - p.transform.position).sqrMagnitude).First();
            var to = node.transform.position - node.transform.forward * 0f; Vector3 stand = to + (p.transform.position - to).normalized * 1.2f;
            motor.Warp(stand + Vector3.up * 0.3f, Quaternion.LookRotation(to - stand)); yield return new WaitForSeconds(0.6f);
            int before = inv.Count(Db.Item("stone"));
            node.Interact(pi);
            float t = 0; while (pi.InAction && t < 12f) { t += Time.deltaTime; yield return null; }
            Assert.Greater(inv.Count(Db.Item("stone")), before, "gathered stone");
            // campfire: give materials, craft, place directly, light, cook
            inv.Add(Db.Item("wood"), 6); inv.Add(Db.Item("stone"), 5); inv.Add(Db.Item("raw_meat"), 1);
            var cr = p.GetComponent<CraftingSystem>(); Assert.IsTrue(cr.Enqueue(Db.Recipe("campfire"))); cr.CompleteFirstNow();
            Assert.AreEqual(1, inv.Count(Db.Item("campfire")));
            var fireGo = Building.BuildSystem.Spawn(Db.Item("campfire"), p.transform.position + p.transform.forward * 1.6f, Quaternion.identity, null);
            inv.Remove(Db.Item("campfire"), 1);
            var fire = fireGo.GetComponent<Campfire>(); Assert.IsNotNull(fire);
            yield return new WaitForSeconds(0.3f);
            fire.Interact(pi); t = 0; while (!fire.IsLit && t < 5f) { t += Time.deltaTime; yield return null; }
            Assert.IsTrue(fire.IsLit, "fire lit");
            yield return new WaitForSeconds(1.2f);
            fire.Interact(pi); t = 0; while (fire.CookingCount == 0 && t < 5f) { t += Time.deltaTime; yield return null; }
            Assert.AreEqual(1, fire.CookingCount, "meat on the fire");
            Assert.Greater(PlayerSurvival.HeatAt(p.transform.position), 0f, "fire warms");
            // save / load round trip
            int woodBefore = inv.Count(Db.Item("wood"));
            Assert.IsTrue(_gm.SaveGame(), "save: " + SaveSystem.LastError);
            inv.Remove(Db.Item("wood"), woodBefore);
            _gm.LoadGame(); yield return new WaitForSeconds(0.5f);
            inv = _gm.Player.GetComponent<InventorySystem>();
            Assert.AreEqual(woodBefore, inv.Count(Db.Item("wood")), "inventory restored");
            Assert.AreEqual(1, Building.PlacedStructure.All.Count, "campfire restored");
            Assert.IsTrue(Building.PlacedStructure.All[0].GetComponent<Campfire>().IsLit, "fire state restored");
            SaveSystem.Delete();
        }

        [UnityTest] public IEnumerator Ocean_Is_Not_Drinkable_Fresh_Water_Is()
        {
            yield return LoadIsland();
            var p = _gm.Player; var pi = p.GetComponent<PlayerInteraction>(); var sv = p.GetComponent<PlayerSurvival>();
            var fresh = WaterSource.All.First(w => w.fresh);
            sv.SetStats(80, 40, 100, 37, 0);
            fresh.Drink(pi); Assert.Greater(sv.Thirst, 60f, "fresh water helps");
            var ocean = Object.FindFirstObjectByType<OceanShore>(); Assert.IsNotNull(ocean);
            float th = sv.Thirst; bool salty = false;
            GameEvents.Raised += e => { if (e.type == GameEventType.TriedSaltWater) salty = true; };
            // stand at the shoreline in front of the spawn (the beach runs into the sea)
            var spawn = _gm.spawnPoint.position; var terrain = Terrain.activeTerrain; float ty = terrain.transform.position.y;
            Vector3 shore = spawn; float best = float.MaxValue, lowest = float.MaxValue;
            // nearest shallow water (sea bed 2..30 cm under the surface): the player wades in and faces the sea
            for (float x = -150f; x <= 150f; x += 1.5f)
                for (float z = -150f; z <= 150f; z += 1.5f)
                {
                    var q = spawn + new Vector3(x, 0, z); float h = terrain.SampleHeight(q) + ty; lowest = Mathf.Min(lowest, h);
                    if (h > -0.02f || h < -0.3f) continue; float d2 = x * x + z * z;
                    if (d2 < best) { best = d2; shore = new Vector3(q.x, h, q.z); }
                }
            Assert.Less(best, float.MaxValue, $"found shallow sea near the spawn (lowest terrain {lowest:F2} m)");
            p.GetComponent<PlayerMotor>().Warp(shore + Vector3.up * 0.3f, p.transform.rotation);
            yield return new WaitForSeconds(0.8f);
            Assert.IsTrue(ocean.PlayerAtShore, $"sea offered at {p.transform.position} (sea bed {shore.y:F2})");
            ocean.Interact(pi); float t = 0; while (!salty && t < 4f) { t += Time.deltaTime; yield return null; }
            Assert.IsTrue(salty, "salt water event"); Assert.Less(sv.Thirst, th, "sea water made thirst worse");
        }
    }
}
