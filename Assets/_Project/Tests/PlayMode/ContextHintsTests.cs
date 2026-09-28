using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.UI;

namespace PrimalFrontier.Tests
{
    /// <summary>Contextual key hints: the lines follow the held item / situation, use the real key names, stay within
    /// four lines, hide with the menus; onboarding tips are shown once and saved.</summary>
    public class ContextHintsTests
    {
        static bool Has(List<string> lines, string s) => lines.Any(l => l.Contains(s));
        static List<string> Lines(HintContext c) { var l = new List<string>(); ContextHints.Compose(c, l); return l; }

        [Test] public void Key_Names_Come_From_The_Bindings()
        {
            Assert.AreEqual("E", KeyNames.Path("<Keyboard>/e"));
            Assert.AreEqual("LMB", KeyNames.Path("<Mouse>/leftButton"));
            Assert.AreEqual("Shift", KeyNames.Path("<Keyboard>/leftShift"));
            // the controls table (checked against PlayerInputReader by ControlsGuideTests) is the source
            Assert.AreEqual(KeyNames.Path(KeyNames.Paths("Attack")[0]), KeyNames.Of("Attack"));
            Assert.AreEqual(KeyNames.Path(KeyNames.Paths("Interact")[0]), KeyNames.Of("Interact"));
            Assert.IsNotNull(KeyNames.Of("Hotbar8"), "hotbar keys listed one per action");
        }

        [Test] public void Compose_Follows_The_Situation()
        {
            string atk = "[" + KeyNames.Of("Attack") + "]", aim = KeyNames.Of("Aim");
            var sword = Lines(new HintContext { held = HeldKind.Melee, heavy = true });
            Assert.IsTrue(Has(sword, "Slash") && Has(sword, "Heavy") && Has(sword, "Block") && Has(sword, atk), string.Join(" | ", sword));
            Assert.IsTrue(Has(sword, "Hold " + aim), "block = hold the aim button");
            Assert.IsFalse(Has(Lines(new HintContext { held = HeldKind.Melee, heavy = false }), "Heavy"), "a knife without a heavy attack shows none");
            Assert.IsTrue(Has(Lines(new HintContext { held = HeldKind.Spear, heavy = true }), "Throw"));
            Assert.IsTrue(Has(Lines(new HintContext { held = HeldKind.Bow }), "Draw"));
            Assert.IsTrue(Has(Lines(new HintContext { held = HeldKind.Bow, noAmmo = true }), "No arrows"));
            Assert.IsTrue(Has(Lines(new HintContext { held = HeldKind.Food }), "Eat"));
            Assert.IsTrue(Has(Lines(new HintContext { held = HeldKind.Placeable }), "Place mode"));
            Assert.IsTrue(Has(Lines(new HintContext { mode = HintMode.Building }), "[" + KeyNames.Of("Rotate") + "]"));
            Assert.IsTrue(Has(Lines(new HintContext { mode = HintMode.Climbing }), "[" + KeyNames.Of("Jump") + "]"));
            Assert.IsTrue(Has(Lines(new HintContext { mode = HintMode.Inventory }), "Double-click"));
            var busy = Lines(new HintContext { held = HeldKind.Spear, heavy = true, crouching = true, lowStamina = true, night = NightTip.NoTorch });
            Assert.LessOrEqual(busy.Count, ContextHints.MaxLines, "never more than four lines");
            Assert.IsTrue(Has(busy, "Sneaking") && Has(busy, "Tired"), "situation lines win over the third base line: " + string.Join(" | ", busy));
        }

        [Test] public void Tips_Seen_Round_Trip()
        {
            OnboardingTips.Clear();
            OnboardingTips.MarkSeen(OnboardingTips.Wood); OnboardingTips.MarkSeen(OnboardingTips.Night);
            var saved = OnboardingTips.SeenList();
            OnboardingTips.Clear(); Assert.IsFalse(OnboardingTips.IsSeen(OnboardingTips.Wood));
            OnboardingTips.SetSeen(saved);
            Assert.IsTrue(OnboardingTips.IsSeen(OnboardingTips.Wood) && OnboardingTips.IsSeen(OnboardingTips.Night));
            OnboardingTips.Clear();
        }

        // ------------------------------------------------------------------ island
        GameManager _gm; bool? _hintsBefore;
        IEnumerator LoadIsland()
        {
            TestScenes.UseTestSaves(); GameManager.ForceShowTitle = false; GameManager.ForcePlayIntro = false; PlayerInputReader.Simulate = true;
            if (_hintsBefore == null) _hintsBefore = GameSettings.Hints;
            GameSettings.Hints = true;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/Island_VerticalSlice.unity", new LoadSceneParameters(LoadSceneMode.Single));
#endif
            float t = 0; while ((_gm = GameManager.Instance) == null || _gm.State != GameState.Playing) { t += Time.unscaledDeltaTime; if (t > 20f) break; yield return null; }
            Assert.IsNotNull(_gm, "GameManager"); Assert.AreEqual(GameState.Playing, _gm.State, "game started");
            yield return new WaitForSeconds(1f);
        }
        [TearDown] public void Cleanup() { GameManager.ForceShowTitle = null; GameManager.ForcePlayIntro = null; PlayerInputReader.Simulate = false; Time.timeScale = 1f; if (_hintsBefore.HasValue) GameSettings.Hints = _hintsBefore.Value; _hintsBefore = null; }
        [UnityTearDown] public IEnumerator Unload() { yield return TestScenes.ClearIfGameplayLeft(); }

        [UnityTest] public IEnumerator Sword_In_Hand_Shows_The_Weapon_Keys_And_Menus_Hide_Them()
        {
            yield return LoadIsland();
            var hints = ContextHints.Instance;
            Assert.IsNotNull(hints, "GameManager adds ContextHints to [UI]");
            var db = ItemDatabase.Instance;
            var weapon = db.Item("flint_sword") ?? db.Item("flint_knife");
            Assert.IsNotNull(weapon, "flint_sword / flint_knife item");
            var inv = _gm.Player.GetComponent<InventorySystem>();
            Assert.AreEqual(0, inv.Add(weapon, 1));
            int slot = System.Array.FindIndex(inv.Slots, s => s != null && s.item == weapon);
            Assert.That(slot, Is.InRange(0, inv.hotbarSize - 1), "weapons go to the hotbar");
            inv.SetActiveSlot(slot);
            yield return new WaitForSecondsRealtime(0.4f);
            var lines = hints.Lines.ToList();
            Assert.AreEqual(HeldKind.Melee, hints.Context.held, weapon.id);
            Assert.IsTrue(Has(lines, "Slash") && Has(lines, "Block") && Has(lines, "Dodge"), string.Join(" | ", lines));
            bool heavy = weapon.weaponData ? weapon.weaponData.HasHeavy : false;
            Assert.AreEqual(heavy, Has(lines, "Heavy"), "heavy line only when the weapon has a heavy attack");
            var touch = Object.FindFirstObjectByType<MobileHUD>();
            bool touchUi = touch && touch.Visible;
            if (!touchUi) Assert.IsTrue(hints.Showing, "hints on screen in play");
            Assert.IsFalse(hints.WantsTip(OnboardingTips.Weapon), "first weapon tip queued or shown");
            // menus: the pause menu hides them, the inventory shows its own keys
            UIManager.Instance.Open(UIScreen.Pause);
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.IsFalse(hints.Showing, "hidden while paused");
            UIManager.Instance.Close();
            UIManager.Instance.Open(UIScreen.Inventory);
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.AreEqual(HintMode.Inventory, hints.Context.mode);
            Assert.IsTrue(Has(hints.Lines.ToList(), "Double-click"));
            UIManager.Instance.Close();
            yield return new WaitForSecondsRealtime(0.3f);
            // the tips shown are saved with the game
            float t = 0; while (!OnboardingTips.IsSeen(OnboardingTips.Weapon) && t < 12f && !touchUi) { t += Time.unscaledDeltaTime; yield return null; }
            if (!touchUi) Assert.IsTrue(SaveSystem.Capture(_gm).tipsSeen.Contains(OnboardingTips.Weapon), "tips seen go into the save");
        }
    }
}
