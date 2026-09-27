using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using PrimalFrontier.AI;
using PrimalFrontier.UI;

namespace PrimalFrontier.Tests
{
    /// <summary>
    /// Hand edits in the scene win over the code defaults: UI pieces found by name keep their layout and style, missing
    /// ones come back, and dinosaurs placed by hand are restored where they stand on a new game.
    /// </summary>
    public class SceneAuthoringTests
    {
        [UnityTearDown] public IEnumerator Unload() { yield return TestScenes.Clear(); }

        [UnityTest] public IEnumerator UI_Edited_In_The_Scene_Keeps_Its_Layout_And_Missing_Pieces_Come_Back()
        {
            yield return TestScenes.Clear();
            // what a designer left in the scene: the HUD canvas with a moved, recoloured vitals panel and no hotbar
            var root = new GameObject("[UI]"); root.SetActive(false);
            var canvas = UIFactory.Canvas("[HUD]", 10, root.transform);
            var vit = UIFactory.Image(canvas.transform, "Vitals", UIStyle.PanelDark, Color.white, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -24), new Vector2(330, 196));
            vit.rectTransform.anchoredPosition = new Vector2(300, -200); vit.color = Color.green;
            var extra = UIFactory.Rect(canvas.transform, "MyDecoration", Vector2.zero, Vector2.one, Vector2.one * 0.5f, Vector2.zero, Vector2.zero);
            var hud = root.AddComponent<HUDManager>();
            root.SetActive(true);                                     // Awake: Build() finds what is there
            yield return null;

            var hudCanvases = root.GetComponentsInChildren<Canvas>(true).Where(c => c.name == "[HUD]").ToList();
            Assert.AreEqual(1, hudCanvases.Count, "the HUD canvas is reused, not duplicated");
            var vitals = hudCanvases[0].transform.Cast<Transform>().Where(t => t.name == "Vitals").ToList();
            Assert.AreEqual(1, vitals.Count, "one Vitals panel");
            Assert.AreEqual(new Vector2(300, -200), ((RectTransform)vitals[0]).anchoredPosition, "moved by hand: position kept");
            Assert.AreEqual(Color.green, vitals[0].GetComponent<Image>().color, "recoloured by hand: colour kept");
            Assert.IsNotNull(hudCanvases[0].transform.Find("Hotbar"), "missing piece rebuilt from the code defaults");
            Assert.IsTrue(extra != null && extra.parent == hudCanvases[0].transform, "a decoration added by hand stays");
            Assert.IsNotNull(vitals[0].Find("healthBar"), "the code found its pieces inside the reused panel");
            Object.Destroy(root);
        }

        [UnityTest] public IEnumerator Placed_Dinosaurs_Are_Restored_Where_They_Stand()
        {
            yield return TestScenes.Clear();
            var def = Resources.FindObjectsOfTypeAll<DinosaurDefinition>().FirstOrDefault(d => d.prefab && d.temperament == Temperament.Passive);
#if UNITY_EDITOR
            if (def == null) def = UnityEditor.AssetDatabase.LoadAssetAtPath<DinosaurDefinition>("Assets/_Project/Data/Dinosaurs/DINO_Parasaurolophus.asset");
#endif
            Assert.IsNotNull(def, "a dinosaur definition with a prefab");
            var spGo = new GameObject("[Dinosaurs]"); spGo.SetActive(false);
            var sp = spGo.AddComponent<DinosaurSpawner>();
            var placed = Object.Instantiate(def.prefab, new Vector3(12f, 0f, -7f), Quaternion.Euler(0, 90, 0), spGo.transform);
            placed.name = "MyPara";
            DinosaurSpawner.Configure(placed, def, Vector3.zero, 20f, 0f, true);
            spGo.SetActive(true);
            yield return null;
            Assert.IsTrue(sp.UsesPlacedCreatures, "placed mode");
            Assert.AreEqual(1, sp.PlacedCount);
            placed.transform.position = new Vector3(50f, 0f, 50f);    // wandered off during play
            sp.SpawnAll();                                             // new game
            yield return null;
            var live = spGo.GetComponentsInChildren<DinosaurController>().Where(d => d.gameObject.activeInHierarchy).ToList();
            Assert.AreEqual(1, live.Count, "one creature after a new game (the old one is gone, no duplicates)");
            Assert.AreEqual("MyPara", live[0].name);
            Assert.Less(Vector2.Distance(new Vector2(live[0].transform.position.x, live[0].transform.position.z), new Vector2(12f, -7f)), 0.01f, "back where it was placed");
            Object.Destroy(spGo);
        }
    }
}
