using System.Collections;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using Debug = UnityEngine.Debug;

namespace PrimalFrontier.Tests
{
    /// <summary>
    /// Phase 3 P2: the first-gather hitch. Picking up a material the first time teaches the recipes that use it; each learned
    /// recipe used to rebuild every crafting tile (24-43 ms each) even with the inventory closed. Measures the whole
    /// synchronous chain (InventorySystem.Add -> ItemAdded -> CraftingSystem learns -> HUD / inventory / journal listeners)
    /// on the island with the inventory closed.
    /// </summary>
    public class SurvivalHitchTests
    {
        static ItemDatabase Db => ItemDatabase.Instance;
        GameManager _gm;

        IEnumerator LoadIsland()
        {
            TestScenes.UseTestSaves(); GameManager.ForceShowTitle = false; GameManager.ForcePlayIntro = false;
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

        /// <summary>materials whose first pickup teaches at least one recipe, most recipes first</summary>
        static ItemDefinition[] Teachers(CraftingSystem cr)
        {
            return Db.recipes.Where(r => r && !r.knownAtStart && !cr.IsKnown(r))
                     .SelectMany(r => r.ingredients.Where(i => i.item).Select(i => i.item)).Distinct()
                     .OrderByDescending(it => Db.recipes.Count(r => r && !r.knownAtStart && !cr.IsKnown(r) && r.ingredients.Any(i => i.item == it)))
                     .ToArray();
        }

        [UnityTest, Timeout(180000)] public IEnumerator First_Gather_Learn_Chain_Is_Cheap_With_The_Inventory_Closed()
        {
            yield return LoadIsland();
            var p = _gm.Player; var inv = p.GetComponent<InventorySystem>(); var cr = p.GetComponent<CraftingSystem>();
            Assert.IsTrue(UI.UIManager.Instance == null || UI.UIManager.Instance.Current == UI.UIScreen.None, "inventory closed");
            // warm up the listeners (JIT, first note) with an item that teaches nothing new
            var known = Db.items.FirstOrDefault(i => i && !Db.recipes.Any(r => r && !r.knownAtStart && r.ingredients.Any(g => g.item == i)));
            if (known) { inv.Add(known, 1); yield return null; inv.Remove(known, 1); yield return null; }
            // an ordinary frame for comparison (editor frames are slow; the pickup frame should look like them)
            var idle = new System.Collections.Generic.List<float>();
            for (int i = 0; i < 12; i++) { float f0 = Time.realtimeSinceStartup; yield return null; idle.Add((Time.realtimeSinceStartup - f0) * 1000f); }
            idle.Sort(); float idleMs = idle[idle.Count / 2];
            var teachers = Teachers(cr);
            Assert.Greater(teachers.Length, 0, "some material still teaches recipes on a new game");
            float worst = 0f; int samples = 0; var sb = new System.Text.StringBuilder();
            foreach (var it in teachers.Take(3))
            {
                int before = cr.KnownIds.Count();
                var sw = Stopwatch.StartNew();
                inv.Add(it, 1);
                float ms = (float)sw.Elapsed.TotalMilliseconds;
                int learned = cr.KnownIds.Count() - before;
                // the next frame (notes are laid out, HUD refreshes)
                float t0 = Time.realtimeSinceStartup; yield return null; float frame = (Time.realtimeSinceStartup - t0) * 1000f;
                sb.Append($"{it.id}: {ms:F1} ms for {learned} recipe(s), next frame {frame:F1} ms; ");
                if (learned > 0) { worst = Mathf.Max(worst, ms); samples++; }
                yield return new WaitForSeconds(0.3f);
            }
            sb.Append($"idle frame (median of 12) {idleMs:F1} ms; ");
            var ui = UI.InventoryUI.Instance;
            if (ui) sb.Append($"recipe tiles built while closed: {ui.TileBuilds}; ");
            Debug.Log("[Hitch] first pickups: " + sb);
            TestContext.WriteLine("[Hitch] first pickups: " + sb);
            if (ui)
            {
                Assert.AreEqual(0, ui.TileBuilds, "no recipe tile rebuild while the inventory is closed");
                // opening the crafting tab builds the tiles once (and only then)
                UI.UIManager.Instance.Open(UI.UIScreen.Inventory); ui.ShowTab(1); yield return null;
                Debug.Log($"[Hitch] crafting tab first open: {ui.LastTileBuildMs:F1} ms for the tiles ({ui.TileBuilds} build)");
                TestContext.WriteLine($"[Hitch] crafting tab first open: {ui.LastTileBuildMs:F1} ms");
                Assert.AreEqual(1, ui.TileBuilds, "built once when shown");
                inv.Add(teachers.Length > 3 ? teachers[3] : teachers[0], 1); yield return null;
                Assert.AreEqual(1, ui.TileBuilds, "learning while open updates the tiles in place");
                UI.UIManager.Instance.Close();
            }
            Assert.Greater(samples, 0, "measured at least one pickup that taught recipes");
            Assert.Less(worst, 12f, "the learn chain stays small with the inventory closed: " + sb);
        }
    }
}
