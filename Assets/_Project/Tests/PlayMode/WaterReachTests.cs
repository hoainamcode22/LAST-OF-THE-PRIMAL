using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.Survival;
using PrimalFrontier.World;

namespace PrimalFrontier.Tests
{
    /// <summary>
    /// The pond, the stream and the sea must be reachable through the REAL interaction scan (what E targets), not only by
    /// calling Interact directly: their transforms sit at the map origin, far from the water, and the scan used to cull them.
    /// </summary>
    public class WaterReachTests
    {
        GameManager _gm;
        static ItemDatabase Db => ItemDatabase.Instance;

        IEnumerator LoadIsland()
        {
            TestScenes.UseTestSaves(); GameManager.ForceShowTitle = false; GameManager.ForcePlayIntro = false; PlayerInputReader.Simulate = true;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/Island_VerticalSlice.unity", new LoadSceneParameters(LoadSceneMode.Single));
#endif
            float t = 0; while ((_gm = GameManager.Instance) == null || _gm.State != GameState.Playing) { t += Time.unscaledDeltaTime; if (t > 20f) break; yield return null; }
            Assert.AreEqual(GameState.Playing, _gm.State, "game started");
            yield return new WaitForSeconds(1f);
        }
        [TearDown] public void Cleanup() { GameManager.ForceShowTitle = null; GameManager.ForcePlayIntro = null; PlayerInputReader.Simulate = false; Time.timeScale = 1f; }
        [UnityTearDown] public IEnumerator Unload() { yield return TestScenes.ClearIfGameplayLeft(); }

        static float Ground(Vector3 p) { var t = Terrain.activeTerrain; return t.SampleHeight(p) + t.transform.position.y; }

        static bool NothingElseNear(Vector3 p, float r)
        {
            foreach (var it in Interactable.Active)
                if (it && !it.LargeArea && (it.transform.position - p).sqrMagnitude < r * r) return false;
            return true;
        }

        /// <summary>a dry or ankle-deep spot 0.6-1.6 m from the water of this source, with no other interactable around</summary>
        static bool FindBank(WaterSource ws, out Vector3 stand, out Vector3 water)
        {
            stand = water = default;
            var r = ws.surface ? ws.surface.GetComponent<Renderer>() : null; if (!r) return false;
            var b = r.bounds;
            for (float x = b.min.x - 2f; x <= b.max.x + 2f; x += 1.5f)
                for (float z = b.min.z - 2f; z <= b.max.z + 2f; z += 1.5f)
                {
                    var p = new Vector3(x, 0f, z);
                    float d = ws.Closest(p, out var w);
                    if (d < 0.6f || d > 1.6f) continue;
                    float g = Ground(p);
                    if (g < w.y - 0.4f || g > w.y + 1.0f) continue;
                    p.y = g;
                    if (!NothingElseNear(p, 3.5f)) continue;
                    bool otherWater = false;                                   // not where the stream runs into the pond
                    foreach (var o in WaterSource.All) if (o != ws && o.Closest(p, out _) < 4f) { otherWater = true; break; }
                    if (otherWater) continue;
                    stand = p; water = w; return true;
                }
            return false;
        }

        [UnityTest, Timeout(120000)] public IEnumerator Pond_And_Stream_Are_Targeted_By_The_Scan_And_Fill()
        {
            yield return LoadIsland();
            var p = _gm.Player; var pi = p.GetComponent<PlayerInteraction>(); var motor = p.GetComponent<PlayerMotor>(); var inv = p.GetComponent<InventorySystem>();
            foreach (var name in new[] { "Pond water", "Stream water" })
            {
                var ws = Object.FindObjectsByType<WaterSource>(FindObjectsSortMode.None).FirstOrDefault(w => w.displayName == name);
                Assert.IsNotNull(ws, name);
                Assert.Greater((ws.transform.position - ws.surface.GetComponent<Renderer>().bounds.center).magnitude, 9.2f,
                    name + ": transform far from the water (the case the scan used to cull)");
                Assert.IsTrue(FindBank(ws, out var stand, out var water), name + ": a bank to stand on");
                float wt = 0f; while (pi.InAction && wt < 5f) { wt += Time.unscaledDeltaTime; yield return null; }   // previous fill finished
                var face = water - stand; face.y = 0f;
                motor.Warp(stand + Vector3.up * 0.3f, Quaternion.LookRotation(face.sqrMagnitude > 1e-4f ? face : Vector3.forward));
                yield return new WaitForSeconds(0.8f);
                var me = p.transform.position; var fp = ws.FocusPoint; var hd = fp - me; float dyy = hd.y; hd.y = 0f;
                string dbg = $"stand {stand}, water {water}, player {me}, focus {fp}, horiz {hd.magnitude:F2} m, dy {dyy:F2}, facing {Vector3.Angle(p.transform.forward, hd):F0} deg";
                Debug.Log("[WaterReach] " + name + ": " + dbg);
                Assert.AreSame(ws, pi.Target, $"{name}: E targets the water at the bank (target: {(pi.Target ? pi.Target.name : "none")}); {dbg}");
                Assert.IsTrue(pi.PromptEnabled, name + ": prompt shown");

                // fill a cup through the targeted interactable, like pressing E
                inv.Clear(); inv.Add(Db.Item("leaf_cup"), 1, true); inv.SetActiveSlot(0);
                yield return new WaitForSeconds(0.2f);
                var cup = inv.Get(0);
                float t = 0f; while (cup.water == 0 && t < 6f) { if (!pi.InAction && pi.Target) pi.Target.Interact(pi); t += Time.unscaledDeltaTime; yield return null; }
                Assert.Greater(cup.water, 0, name + ": the cup filled");
                Assert.AreEqual(WaterType.DirtyWater, WaterRules.TypeOf(cup), name + ": pond / stream water is dirty");
            }
        }

        [UnityTest, Timeout(120000)] public IEnumerator Sea_Is_Targeted_By_The_Scan_At_The_Shore()
        {
            yield return LoadIsland();
            var p = _gm.Player; var pi = p.GetComponent<PlayerInteraction>(); var motor = p.GetComponent<PlayerMotor>();
            var ocean = Object.FindFirstObjectByType<OceanShore>(); Assert.IsNotNull(ocean);
            Vector3 spawn = p.transform.position; bool found = false; Vector3 spot = default;
            for (float r = 5f; r <= 200f && !found; r += 3f)
                for (int a = 0; a < 36 && !found; a++)
                {
                    var q = spawn + Quaternion.Euler(0, a * 10f, 0) * Vector3.forward * r; float h = Ground(q);
                    if (h > -0.03f || h < -0.25f) continue;
                    q.y = h; if (!NothingElseNear(q, 4f)) continue;
                    spot = q; found = true;
                }
            Assert.IsTrue(found, "a wading spot at the shore");
            motor.Warp(spot + Vector3.up * 0.3f, p.transform.rotation);
            yield return new WaitForSeconds(0.8f);
            Assert.IsTrue(ocean.PlayerAtShore, "at the shore");
            Assert.AreSame(ocean, pi.Target, "E targets the sea at the shore (target: " + (pi.Target ? pi.Target.name : "none") + ")");
        }
    }
}
