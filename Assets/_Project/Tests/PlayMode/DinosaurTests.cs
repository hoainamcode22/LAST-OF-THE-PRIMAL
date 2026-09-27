using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PrimalFrontier.AI;
using PrimalFrontier.Combat;
using PrimalFrontier.Core;
using PrimalFrontier.Player;

namespace PrimalFrontier.Tests
{
    /// <summary>Phase 13-15: dinosaurs spawn in their habitats, move on the terrain, herbivores react, predators hunt, damage both ways.</summary>
    public class DinosaurTests
    {
        GameManager _gm;
        IEnumerator LoadIsland()
        {
            GameManager.ForceShowTitle = false; GameManager.ForcePlayIntro = false; PlayerInputReader.Simulate = true;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/Island_VerticalSlice.unity", new LoadSceneParameters(LoadSceneMode.Single));
#endif
            float t = 0; while ((_gm = GameManager.Instance) == null || _gm.State != GameState.Playing) { t += Time.unscaledDeltaTime; if (t > 20f) break; yield return null; }
            yield return new WaitForSeconds(1f);
        }
        [TearDown] public void Cleanup() { GameManager.ForceShowTitle = null; GameManager.ForcePlayIntro = null; PlayerInputReader.Simulate = false; Time.timeScale = 1f; }
        [UnityTearDown] public IEnumerator Unload() { yield return TestScenes.Clear(); }

        [UnityTest] public IEnumerator Dinosaurs_Spawn_And_Walk_On_Terrain()
        {
            yield return LoadIsland();
            var dinos = DinosaurController.All.ToList();
            Assert.GreaterOrEqual(dinos.Count, 7, "land dinosaurs spawned: " + string.Join(",", dinos.Select(d => d.def ? d.def.id : "?")));
            Assert.GreaterOrEqual(Object.FindObjectsByType<AmbientCreature>(FindObjectsSortMode.None).Length, 2, "flyers / swimmer");
            var tri = dinos.First(d => d.def.id == "triceratops");
            // bring the player close enough for full-rate AI and force a walk
            var p = _gm.Player.GetComponent<PlayerMotor>(); p.Warp(tri.transform.position + tri.transform.right * 45f + Vector3.up * 2f, Quaternion.identity);
            yield return new WaitForSeconds(0.5f);
            Vector3 a = tri.transform.position; tri.ForceState(DinoState.Wander);
            yield return new WaitForSeconds(6f);
            var terrain = Terrain.activeTerrain;
            float ground = terrain.SampleHeight(tri.transform.position) + terrain.transform.position.y;
            Assert.AreEqual(ground, tri.transform.position.y, 0.2f, "stays on the ground");
            Assert.Greater((tri.transform.position - a).magnitude, 0.5f, $"triceratops moved (state {tri.State})");
            var anim = tri.GetComponent<Animator>(); Assert.IsNotNull(anim.runtimeAnimatorController);
        }

        [UnityTest] public IEnumerator Predator_Chases_And_Bites_Player_Player_Can_Kill_It()
        {
            yield return LoadIsland();
            var raptor = DinosaurController.All.First(d => d.def.id == "velociraptor");
            var player = _gm.Player; var hp = player.GetComponent<PlayerHealth>();
            // place the player in open ground 14 m in front of the raptor
            var pos = raptor.transform.position + raptor.transform.forward * 14f; var t = Terrain.activeTerrain; pos.y = t.SampleHeight(pos) + t.transform.position.y + 0.2f;
            player.GetComponent<PlayerMotor>().Warp(pos, Quaternion.LookRotation(-raptor.transform.forward));
            float h0 = hp.Health; float time = 0;
            while (time < 20f && hp.Health >= h0) { time += Time.deltaTime; yield return null; }
            Assert.Less(hp.Health, h0, $"raptor attacked within 20 s (state {raptor.State}, dist {Vector3.Distance(raptor.transform.position, player.transform.position):F1})");
            hp.Revive(1f);
            raptor.TakeHit(new HitInfo { damage = 500f, point = raptor.transform.position + Vector3.up, direction = Vector3.forward, attacker = player });
            Assert.IsFalse(raptor.IsAlive, "raptor dies");
            yield return new WaitForSeconds(2f);
            Assert.GreaterOrEqual(World.WorldPickup.Dropped.Count, 1, "loot dropped");
        }

        [UnityTest] public IEnumerator Herbivores_Flee_From_Predator_Warning()
        {
            yield return LoadIsland();
            var para = DinosaurController.All.First(d => d.def.id == "parasaurolophus");
            _gm.Player.GetComponent<PlayerMotor>().Warp(para.transform.position + Vector3.right * 60f + Vector3.up * 2f, Quaternion.identity);
            yield return new WaitForSeconds(0.5f);
            GameEvents.Raise(GameEventType.PredatorWarning, "test");
            yield return null;
            Assert.AreEqual(DinoState.Flee, para.State, "herd flees on the predator warning");
        }
    }
}
