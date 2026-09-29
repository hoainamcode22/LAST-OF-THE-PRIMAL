using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using PrimalFrontier.Animation;
using PrimalFrontier.Building;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.Story;
using PrimalFrontier.Survival;
using PrimalFrontier.World;
using Object = UnityEngine.Object;

namespace PrimalFrontier.Tests
{
    /// <summary>
    /// Survival phase 3 on the island (real player, real HUD, real save file in the test folder): the bandage through the
    /// normal use path (Bandage_Use action, not Eat), HUD status icons and "+ Hydration", and a save / load that keeps
    /// status effects, food ages, a burned-out fire and any ISaveSection, while a broken section is skipped.
    /// Optional captures (Library/PrimalBridge/capture_s3.txt = "on") write Documentation/Screenshots/Survival/phase3_*.png.
    /// </summary>
    public class SurvivalPhase3IslandTests
    {
        static ItemDatabase Db => ItemDatabase.Instance;
        GameManager _gm;
        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        static string CaptureFlag => Path.Combine(Root, "Library/PrimalBridge/capture_s3.txt");
        static string Out => Path.Combine(Root, "Documentation/Screenshots/Survival");

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

        [TearDown] public void Cleanup() { GameManager.ForceShowTitle = null; GameManager.ForcePlayIntro = null; PlayerInputReader.Simulate = false; Time.timeScale = 1f; PlayerHealth.InjuryRoll = null; }
        [UnityTearDown] public IEnumerator Unload() { yield return TestScenes.ClearIfGameplayLeft(); }

        static int Hold(InventorySystem inv, ItemDefinition item)
        {
            int i = Array.FindIndex(inv.Slots, s => s != null && !s.IsEmpty && s.item == item);
            Assert.GreaterOrEqual(i, 0, item.id + " in the pack");
            if (i >= inv.hotbarSize) { Assert.IsTrue(inv.Move(i, inv, 0)); i = 0; }
            inv.SetActiveSlot(i);
            return i;
        }

        static Image StatusIcon(int i)
        {
            var hud = Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(r => r.name == "StatusIcons");
            if (!hud) return null;
            var bg = hud.Find("Status" + i); return bg ? bg.Find("Icon")?.GetComponent<Image>() : null;
        }

        static bool NoteContains(string text) =>
            Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Any(t => t && t.isActiveAndEnabled && t.text != null && t.text.Contains(text) && t.transform.parent && t.transform.parent.name == "Note");

        [UnityTest, Timeout(150000)] public IEnumerator Bandage_Use_Path_Stops_Bleeding_Shows_Icons_And_Hydration()
        {
            yield return LoadIsland();
            var p = _gm.Player; var pi = p.GetComponent<PlayerInteraction>(); var inv = p.GetComponent<InventorySystem>();
            var hp = p.GetComponent<PlayerHealth>(); var sv = p.GetComponent<PlayerSurvival>(); var fx = p.GetComponent<PlayerStatusEffects>();
            var drv = p.GetComponent<PlayerAnimationDriver>();
            Assert.IsNotNull(fx, "the player has PlayerStatusEffects");
            Assert.AreSame(fx, PlayerStatusEffects.Player, "the tagged player registers");
            Assert.IsTrue(SaveSystem.Sections.Any(s => s.SectionKey == PlayerStatusEffects.SaveKey), "status effects are a save section");
            PlayerHealth.InjuryRoll = () => 0.99f;
            // a deep wound (strong hit): bleeding, HUD icon
            hp.TakeDamage(SurvivalConfig.Instance.deepWoundDamage + 2f, p.transform.position + p.transform.forward * 2f + Vector3.up, false, 0f);
            Assert.IsTrue(hp.IsBleeding, "deep wound bleeds");
            yield return null; yield return null;
            var icon0 = StatusIcon(0);
            Assert.IsNotNull(icon0, "HUD status icon row exists");
            Assert.IsTrue(icon0.transform.parent.gameObject.activeSelf, "a status icon shows while bleeding");
            Assert.IsNotNull(icon0.sprite); StringAssert.Contains("bleeding", icon0.sprite.name, "the bleeding icon");
            // the bandage through the normal use path (attack button / touch USE / inventory Use all call UseActiveConsumable)
            var bandage = Db.Item("bandage"); Assert.IsNotNull(bandage, "bandage item");
            Assert.IsTrue(PlayerInteraction.HasUseHandler(bandage), "survival registered a use for the bandage");
            Assert.IsFalse(bandage.IsFood, "the bandage is not food (no Eat path)");
            yield return new WaitForSeconds(1.2f);                        // the hurt reaction is over
            inv.Add(bandage, 2, true); Hold(inv, bandage);
            string stepBefore = TutorialManager.Instance ? TutorialManager.Instance.CurrentId : null;
            bool ate = false, used = false; Action<GameEvent> h = e => { if (e.type == GameEventType.Ate) ate = true; if (e.type == GameEventType.ItemUsed && e.id == "bandage") used = true; };
            GameEvents.Raised += h;
            int sawAction = -1;
            try
            {
                float t = 0f;
                while (hp.IsBleeding && t < 8f)
                {
                    if (!pi.InAction && !drv.IsBusy) pi.UseActiveConsumable();
                    if (pi.InAction) sawAction = pi.CurrentActionId;
                    t += Time.unscaledDeltaTime; yield return null;
                }
            }
            finally { GameEvents.Raised -= h; }
            Assert.IsFalse(hp.IsBleeding, "the bandage stopped the bleeding");
            Assert.AreEqual(PlayerActions.BandageUse, sawAction, "played the Bandage_Use action (not Eat)");
            Assert.IsTrue(used, "ItemUsed raised"); Assert.IsFalse(ate, "no Ate event");
            Assert.AreEqual(1, inv.Count(bandage), "one bandage used");
            Assert.AreEqual(stepBefore, TutorialManager.Instance ? TutorialManager.Instance.CurrentId : null, "the tutorial step did not move on");
            Assert.IsTrue(fx.Has(StatusEffectIds.Recovering), "health comes back slowly");
            yield return null; yield return null;
            Assert.IsFalse(icon0.transform.parent.gameObject.activeSelf && icon0.sprite && icon0.sprite.name.Contains("bleeding"), "the bleeding icon went away");
            // not bleeding any more and healthy: no use of a bandage
            hp.SetHealth(hp.maxHealth); fx.Remove(StatusEffectIds.Recovering);
            Assert.IsNotNull(SurvivalItemUse.WhyNot(hp, bandage), "nothing to treat");
            // drinking shows "+N Hydration"
            var cup = Db.Item("leaf_cup"); inv.Add(cup, 1, true); int cs = Hold(inv, cup);
            WaterRules.Fill(inv.Get(cs), WaterType.CleanWater);
            sv.SetStats(sv.Hunger, 30f, sv.Stamina, sv.BodyTemperature, 0f);
            float th = sv.Thirst, tt = 0f;
            while (sv.Thirst <= th + 1f && tt < 6f) { if (!pi.InAction && !drv.IsBusy) pi.UseActiveConsumable(); tt += Time.unscaledDeltaTime; yield return null; }
            Assert.Greater(sv.Thirst, th + 1f, "drank");
            yield return null;
            Assert.IsTrue(NoteContains("Hydration"), "a \"+N Hydration\" note");
            Assert.AreEqual("0ml", HUDManagerCount(inv.Get(cs)), "the cup shows its amount in ml");
            // wet: the wetness bar + wet icon
            sv.SetStats(sv.Hunger, sv.Thirst, sv.Stamina, sv.BodyTemperature, 0.9f);
            yield return new WaitForSeconds(0.2f);
            Assert.IsTrue(fx.Has(StatusEffectIds.Wet), "wet status from the wetness");
            var bar = Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(r => r.name == "WetnessBar");
            Assert.IsNotNull(bar, "wetness bar"); Assert.IsTrue(bar.gameObject.activeInHierarchy, "the wetness bar shows while wet");
            if (CaptureOn) { yield return null; CaptureHud(Camera.main, "phase3_hud_status.png"); }
        }

        static string HUDManagerCount(ItemStack s) => UI.HUDManager.SlotCountText(s);

        class IslandSection : ISaveSection
        {
            public string data = "{\"berries\":7}"; public string restored;
            public string SectionKey => "t_island_section";
            public string CaptureSection() => data;
            public void RestoreSection(string json) { restored = json; }
        }

        [UnityTest, Timeout(180000)] public IEnumerator Save_Load_Keeps_Status_Food_Age_Fire_State_And_Sections()
        {
            yield return LoadIsland();
            SaveSystem.Delete();
            var p = _gm.Player; var inv = p.GetComponent<InventorySystem>(); var hp = p.GetComponent<PlayerHealth>(); var fx = p.GetComponent<PlayerStatusEffects>();
            var sec = new IslandSection(); SaveSystem.RegisterSection(sec);
            try
            {
                // state to keep
                fx.Apply(StatusEffectIds.Bleeding, 1f, 50f, false);
                fx.Apply(StatusEffectIds.LegInjury, 1f, 200f, false);
                var meat = Db.Item("raw_meat"); Assert.Greater(meat.spoilHours, 0f, "raw meat spoils");
                double spoil = meat.spoilHours * GameClock.SecondsPerHour;
                inv.Add(meat, 2, true, GameClock.Now - spoil * 0.6);          // an aging stack
                var fire = BuildSystem.Spawn(Db.Item("campfire"), p.transform.position + p.transform.forward * 3f, Quaternion.identity, null).GetComponent<Campfire>();
                fire.Restore(true, 100f); fire.SetLit(false);                       // lit, then put out: Extinguished
                Assert.AreEqual(Campfire.FireState.Extinguished, fire.State);
                Vector3 firePos = fire.transform.position;                      // the load replaces the object
                Assert.IsTrue(_gm.SaveGame(), "saved: " + SaveSystem.LastError);
                Assert.IsTrue(_gm.SaveGame(), "saved twice (backup kept)");
                Assert.IsTrue(File.Exists(SaveSystem.BackupPathFor(0)), "backup of the previous save");
                // a broken section in the file (another system's bad data) must not stop the load
                string path = SaveSystem.PathFor(0);
                var d = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
                Assert.IsTrue(d.sections.Any(s => s.key == PlayerStatusEffects.SaveKey), "status section in the file");
                Assert.IsTrue(d.sections.Any(s => s.key == sec.SectionKey), "test section in the file");
                d.sections.Add(new SectionData { key = PlayerStatusEffects.SaveKey + "_x", json = "{not json" });   // a section nobody registered
                File.WriteAllText(path, JsonUtility.ToJson(d, true));
                // change everything, then load
                fx.Clear(); inv.Clear(); sec.data = "{\"berries\":0}";
                _gm.LoadGame();
                yield return new WaitForSeconds(0.5f);
                Assert.AreEqual(GameState.Playing, _gm.State, "loaded");
                fx = p.GetComponent<PlayerStatusEffects>();
                Assert.IsTrue(fx.Has(StatusEffectIds.Bleeding), "bleeding came back");
                Assert.Greater(fx.RemainingOf(StatusEffectIds.Bleeding), 30f);
                Assert.IsTrue(fx.Has(StatusEffectIds.LegInjury), "leg injury came back");
                var st = inv.Slots.FirstOrDefault(s => s != null && s.item == meat);
                Assert.IsNotNull(st, "meat back in the pack");
                Assert.AreEqual(FoodStage.Aging, Spoilage.Stage(st), "the meat is still aging (age saved)");
                var fire2 = Campfire.All.FirstOrDefault(c => c && (c.transform.position - firePos).sqrMagnitude < 1f);
                Assert.IsNotNull(fire2, "fire restored");
                Assert.AreEqual(Campfire.FireState.Extinguished, fire2.State, "a put-out fire stays extinguished");
                Assert.AreEqual("{\"berries\":7}", sec.restored, "ISaveSection restored through the save file");
                // the main save damaged: Continue uses the backup
                File.WriteAllText(path, "{ damaged");
                var r = SaveSystem.Read();
                Assert.IsNotNull(r, "backup read"); Assert.IsTrue(SaveSystem.LoadedFromBackup);
            }
            finally { SaveSystem.UnregisterSection(sec); SaveSystem.Delete(); }
        }

        // ------------------------------------------------------------------ captures (optional)
        static bool CaptureOn => File.Exists(CaptureFlag) && File.ReadAllText(CaptureFlag).Trim() == "on";

        static void Shot(Camera cam, string file, int w, int h)
        {
            Directory.CreateDirectory(Out);
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            cam.targetTexture = rt; cam.Render(); cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = prev; cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(Out, file), tex.EncodeToPNG());
            Object.Destroy(tex); rt.Release(); Object.Destroy(rt);
        }

        /// <summary>the game camera with the HUD drawn by it for one render (no WaitForEndOfFrame)</summary>
        static void CaptureHud(Camera cam, string file)
        {
            if (!cam) return;
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay && c.isActiveAndEnabled).ToArray();
            foreach (var c in canvases) { c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = cam.nearClipPlane + 0.05f; }
            Canvas.ForceUpdateCanvases();
            try { Shot(cam, file, 1600, 900); }
            finally { foreach (var c in canvases) { c.renderMode = RenderMode.ScreenSpaceOverlay; c.worldCamera = null; } }
        }

        static Vector3 Ground(Vector3 p) { var t = Terrain.activeTerrain; if (t) p.y = t.SampleHeight(p) + t.transform.position.y; return p; }

        [UnityTest, Timeout(150000)] public IEnumerator Capture_Collector_Fire_And_Fish()
        {
            if (!CaptureOn) Assert.Ignore("capture only: write \"on\" into Library/PrimalBridge/capture_s3.txt to run it");
            yield return LoadIsland();
            var p = _gm.Player; var tm = TimeManager.Instance; if (tm) tm.Set(tm.day, 11f);
            var basePos = Ground(p.transform.position + p.transform.forward * 4f);
            var cam = new GameObject("S3_CaptureCam").AddComponent<Camera>(); cam.fieldOfView = 40f; cam.nearClipPlane = 0.05f;
            try
            {
                // rain collector at 2/6 and 5/6 (the water surface must show: QA known issue 3)
                var a = BuildSystem.Spawn(Db.Item("rain_collector"), basePos, Quaternion.identity, null).GetComponent<RainCollector>();
                var b = BuildSystem.Spawn(Db.Item("rain_collector"), Ground(basePos + p.transform.right * 1.6f), Quaternion.identity, null).GetComponent<RainCollector>();
                a.SetWater(2f); b.SetWater(5f);
                Debug.Log($"[S3 capture] collector surface heights: 2/6 {a.SurfaceHeight:F3} m, 5/6 {b.SurfaceHeight:F3} m (basin floor 0.03, rim 0.24)");
                var mid = (a.transform.position + b.transform.position) * 0.5f;
                cam.transform.position = mid + Vector3.up * 1.9f - p.transform.forward * 1.2f; cam.transform.LookAt(mid + Vector3.up * 0.15f);
                yield return null;
                Shot(cam, "phase3_rain_collector_2of6_5of6.png", 1400, 800);
                Object.Destroy(a.gameObject); Object.Destroy(b.gameObject);
                // fires: burning with a fish cooking vs low on fuel
                var f1 = BuildSystem.Spawn(Db.Item("campfire"), basePos, Quaternion.identity, null).GetComponent<Campfire>();
                var f2 = BuildSystem.Spawn(Db.Item("campfire"), Ground(basePos + p.transform.right * 2.2f), Quaternion.identity, null).GetComponent<Campfire>();
                f1.Restore(true, 900f); f2.Restore(true, 20f);
                var inv = p.GetComponent<InventorySystem>();
                inv.Add(Db.Item("raw_fish"), 1, true); inv.Add(Db.Item("raw_meat"), 1, true);
                f1.TryCook(inv, Db.Item("raw_fish")); f1.TryCook(inv, Db.Item("raw_meat"));
                yield return new WaitForSeconds(1.5f);
                var m2 = (f1.transform.position + f2.transform.position) * 0.5f;
                cam.transform.position = m2 + Vector3.up * 1.6f - p.transform.forward * 2.6f; cam.transform.LookAt(m2 + Vector3.up * 0.2f);
                if (tm) tm.Set(tm.day, 20.5f);
                yield return new WaitForSeconds(0.5f);
                Debug.Log($"[S3 capture] fires: full {f1.State} intensity {f1.Intensity01:F2}, low {f2.State} intensity {f2.Intensity01:F2}");
                Shot(cam, "phase3_fire_full_vs_low_fish.png", 1400, 800);
            }
            finally { Object.Destroy(cam.gameObject); }
        }
    }
}
