using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PrimalFrontier.Building;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.World;
using Object = UnityEngine.Object;

namespace PrimalFrontier.Tests
{
    /// <summary>
    /// Review screenshots from the running game (not a check): a camp with the fire cooking, the rain collector and the tent,
    /// the carried weapons on the back / hip, and the key hints next to the fire. Skipped unless
    /// Library/PrimalBridge/capture_survival.txt says "on", so the normal suite does not write images.
    /// Output: Documentation/Screenshots/Survival/.
    /// </summary>
    public class SurvivalShowcase
    {
        static ItemDatabase Db => ItemDatabase.Instance;
        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        static string Flag => Path.Combine(Root, "Library/PrimalBridge/capture_survival.txt");
        static string Out => Path.Combine(Root, "Documentation/Screenshots/Survival");
        GameManager _gm;

        [TearDown] public void Cleanup() { GameManager.ForceShowTitle = null; GameManager.ForcePlayIntro = null; PlayerInputReader.Simulate = false; Time.timeScale = 1f; }
        [UnityTearDown] public IEnumerator Unload() { yield return TestScenes.ClearIfGameplayLeft(); }

        static void Shot(Camera cam, string file, int w = 1600, int h = 900)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            cam.targetTexture = rt; cam.Render(); cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = prev; cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(Out, file), tex.EncodeToPNG());
            Object.Destroy(tex); rt.Release(); Object.Destroy(rt);
        }

        /// <summary>game camera image WITH the HUD: overlay canvases are drawn by this camera for one render (no WaitForEndOfFrame,
        /// which never comes while the Game view is not painting, e.g. the editor in the background)</summary>
        static void ShotWithUi(Camera cam, string file)
        {
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay && c.isActiveAndEnabled).ToArray();
            foreach (var c in canvases) { c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = cam.nearClipPlane + 0.05f; }
            Canvas.ForceUpdateCanvases();
            try { Shot(cam, file, Mathf.Max(640, Screen.width), Mathf.Max(360, Screen.height)); }
            finally { foreach (var c in canvases) { c.renderMode = RenderMode.ScreenSpaceOverlay; c.worldCamera = null; } }
        }

        static Vector3 Ground(Vector3 p) { var t = Terrain.activeTerrain; if (t) p.y = t.SampleHeight(p) + t.transform.position.y; return p; }
        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.forward; }
        static GameObject Place(string id, Vector3 p, Vector3 face) { p = Ground(p); return BuildSystem.Spawn(Db.Item(id), p, Quaternion.LookRotation(Flat(face - p)), null); }
        static void Hold(InventorySystem inv, string id)
        {
            int i = Array.FindIndex(inv.Slots, s => s != null && !s.IsEmpty && s.item && s.item.id == id);
            if (i >= inv.hotbarSize) { inv.Move(i, inv, 0); i = 0; }
            inv.SetActiveSlot(i);
        }

        [UnityTest, Timeout(150000)] public IEnumerator Capture_Camp_Holster_Hints()
        {
            if (!File.Exists(Flag) || File.ReadAllText(Flag).Trim() != "on") Assert.Ignore("capture only: write \"on\" into Library/PrimalBridge/capture_survival.txt to run it");
            Directory.CreateDirectory(Out);
            TestScenes.UseTestSaves(); GameManager.ForceShowTitle = false; GameManager.ForcePlayIntro = false; PlayerInputReader.Simulate = true;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/Island_VerticalSlice.unity", new LoadSceneParameters(LoadSceneMode.Single));
#endif
            float w = 0; while ((_gm = GameManager.Instance) == null || _gm.State != GameState.Playing) { w += Time.unscaledDeltaTime; if (w > 20f) break; yield return null; }
            Assert.AreEqual(GameState.Playing, _gm.State);
            yield return new WaitForSeconds(1f);

            var p = _gm.Player; var inv = p.GetComponent<InventorySystem>(); var motor = p.GetComponent<PlayerMotor>();
            var tm = TimeManager.Instance; tm.Set(1, 10.5f);
            Vector3 fwd = Flat(p.transform.forward), right = Vector3.Cross(Vector3.up, fwd);
            Vector3 c = Ground(p.transform.position + fwd * 5f);

            // camp: fire (one piece ready, one cooking), collector with water, tent facing the fire
            foreach (var (id, n) in new[] { ("wood", 6), ("raw_meat", 2), ("flint_sword", 1), ("flint_knife", 1), ("stone_axe", 1) }) inv.Add(Db.Item(id), n, true);
            var fire = Place("campfire", c, c - fwd).GetComponent<Campfire>();
            Assert.IsTrue(fire.TryLight(inv), "lit");
            Assert.IsTrue(fire.TryCook(inv, Db.Item("raw_meat")), "cooking 1");
            Time.timeScale = 8f; float t = 0f;
            while (!Enumerable.Range(0, fire.SlotCount).Any(i => fire.StateOf(i) == Campfire.CookState.Ready) && t < 10f) { t += Time.unscaledDeltaTime; yield return null; }
            Time.timeScale = 1f;
            Assert.IsTrue(fire.TryCook(inv, Db.Item("raw_meat")), "cooking 2");
            var col = Place("rain_collector", c - right * 2.4f + fwd * 0.8f, c).GetComponent<RainCollector>(); col.SetWater(4f);
            Place("tent", c + right * 3.4f + fwd * 1.8f, c);

            // survivor at the fire, axe in hand: sword on the back, knife at the hip
            Hold(inv, "stone_axe");
            Vector3 stand = Ground(c - fwd * 1.5f + right * 0.5f);
            motor.Warp(stand + Vector3.up * 0.1f, Quaternion.LookRotation(Flat(c - stand)));
            yield return new WaitForSeconds(1.2f);

            var cam = Camera.main; Assert.IsNotNull(cam);
            var tpc = cam.GetComponentInParent<ThirdPersonCamera>() ?? Object.FindFirstObjectByType<ThirdPersonCamera>();
            if (tpc) tpc.enabled = false;
            Vector3 look = c + Vector3.up * 0.7f + right * 0.4f;
            cam.transform.position = Ground(c - fwd * 7.5f - right * 2.2f) + Vector3.up * 2.8f; cam.transform.LookAt(look);
            yield return null; Shot(cam, "camp_day.png");

            tm.Set(1, 19.3f); yield return new WaitForSeconds(0.6f);
            Shot(cam, "camp_dusk.png");

            tm.Set(1, 10.5f); yield return new WaitForSeconds(0.4f);
            cam.transform.position = c - fwd * 1.1f - right * 1.3f + Vector3.up * 1.25f; cam.transform.LookAt(c + Vector3.up * 0.1f);
            yield return null; Shot(cam, "fire_cooking.png");
            Vector3 cp = col.transform.position;
            cam.transform.position = cp + (c - cp).normalized * 1.6f + Vector3.up * 1.7f; cam.transform.LookAt(cp + Vector3.up * 0.55f);
            yield return null; Shot(cam, "rain_collector.png");
            var tent = PlacedStructure.All.First(x => x.itemId == "tent").transform;
            cam.transform.position = Ground(tent.position + tent.forward * 4.2f + tent.right * 1.6f) + Vector3.up * 1.6f; cam.transform.LookAt(tent.position + Vector3.up * 0.8f);
            yield return null; Shot(cam, "tent_front.png");
            var back = p.transform; Vector3 behind = back.position - back.forward * 1.9f + back.right * 0.5f + Vector3.up * 1.55f;
            cam.transform.position = behind; cam.transform.LookAt(back.position + Vector3.up * 1.15f);
            yield return null; Shot(cam, "holster_back_hip.png");
            var eq = p.GetComponent<PlayerEquipment>();
            Debug.Log($"[showcase] carried back {(eq.CarriedBackItem ? eq.CarriedBackItem.id : "-")}, hip {(eq.CarriedHipItem ? eq.CarriedHipItem.id : "-")}");
            Assert.IsNotNull(eq.CarriedBackObject, "a weapon on the back");

            // key hints: the game camera again, raw meat in hand at the lit fire
            inv.Add(Db.Item("raw_meat"), 1, true); Hold(inv, "raw_meat");
            if (tpc) { tpc.enabled = true; tpc.Yaw = p.transform.eulerAngles.y + 25f; tpc.SnapBehindTarget(); }
            yield return new WaitForSeconds(1.5f);
            ShotWithUi(cam, "hints_fire.png");
            Debug.Log("[showcase] wrote " + string.Join(", ", Directory.GetFiles(Out, "*.png").Select(Path.GetFileName)));
        }
    }
}
