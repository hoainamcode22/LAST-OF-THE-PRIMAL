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
using PrimalFrontier.VFX;

namespace PrimalFrontier.Tests
{
    /// <summary>Phase 13-15: dinosaurs spawn in their habitats, move on the terrain, herbivores react, predators hunt, damage both ways.</summary>
    public class DinosaurTests
    {
        GameManager _gm;
        IEnumerator LoadIsland()
        {
            TestScenes.UseTestSaves(); GameManager.ForceShowTitle = false; GameManager.ForcePlayIntro = false; PlayerInputReader.Simulate = true;
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
            if (raptor.def.sprayLoot) { Assert.GreaterOrEqual(World.WorldPickup.Dropped.Count, 1, "loot dropped"); yield break; }
            // loot stays on the body as a carcass; cutting it gives the items
            var carcass = raptor.GetComponent<World.Carcass>();
            Assert.IsNotNull(carcass, "the body is left as a carcass to butcher");
            Assert.Greater(carcass.Remaining, 0, "carcass holds the loot");
            var meat = Items.ItemDatabase.Instance.Item("raw_meat");
            var inv = player.GetComponent<Items.InventorySystem>(); int before = inv.Count(meat);
            var pi = player.GetComponent<PlayerInteraction>();
            for (int i = 0; i < carcass.handCutsPerItem; i++) carcass.Hit(pi);
            Assert.AreEqual(before + 1, inv.Count(meat), "butchering gives meat");
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

        [UnityTest] public IEnumerator Hits_Spray_Blood_Death_Leaves_A_Pool_And_Blood_Can_Be_Turned_Off()
        {
            var saved = GameSettings.Blood;
            try
            {
                yield return LoadIsland();
                GameSettings.Blood = BloodLevel.Normal;
                var tri = DinosaurController.All.First(d => d.def.id == "triceratops");
                var player = _gm.Player;
                player.GetComponent<PlayerMotor>().Warp(tri.transform.position + tri.transform.right * 30f + Vector3.up * 2f, Quaternion.identity);
                yield return new WaitForSeconds(0.3f);
                BloodDecals.Instance.ClearAll();
                int pools = BloodDecals.Instance.PoolCount;
                var side = tri.transform.position + Vector3.up * tri.def.bodyRadius + tri.transform.right * tri.def.bodyRadius;
                tri.TakeHit(new HitInfo { damage = 10f, point = side, direction = -tri.transform.right, attacker = player, heavy = true });
                yield return null;
                string fx = VfxPool.Instance.ActiveNames();
                StringAssert.Contains("BloodSpray", fx, "spray on hit: " + fx);
                Assert.Greater(BloodDecals.Instance.ActiveCount, 0, "blood on the ground");
                StringAssert.Contains("Bleed(attached)", fx, "wound keeps bleeding on the body: " + fx);
                tri.TakeHit(new HitInfo { damage = 5000f, point = side, direction = -tri.transform.right, attacker = player });
                yield return new WaitForSeconds(0.2f);
                Assert.AreEqual(DinoState.Dead, tri.State);
                Assert.AreEqual(pools + 1, BloodDecals.Instance.PoolCount, "blood pool under the body");
                // blood off: dust instead, no ground blood
                GameSettings.Blood = BloodLevel.Off;
                yield return null;
                Assert.AreEqual(0, BloodDecals.Instance.ActiveCount, "decals cleared when blood is switched off");
                var para = DinosaurController.All.First(d => d.def.id == "parasaurolophus" && d.State != DinoState.Dead);
                var p2 = para.transform.position + Vector3.up * para.def.bodyRadius;
                player.GetComponent<PlayerMotor>().Warp(para.transform.position + para.transform.right * 30f + Vector3.up * 2f, Quaternion.identity);
                yield return new WaitForSeconds(0.3f);
                para.TakeHit(new HitInfo { damage = 5f, point = p2, direction = Vector3.forward, attacker = player });
                yield return null;
                fx = VfxPool.Instance.ActiveNames();
                StringAssert.Contains("HitDust", fx, "neutral impact when blood is off: " + fx);
                Assert.AreEqual(0, BloodDecals.Instance.ActiveCount, "no ground blood when off");
            }
            finally { GameSettings.Blood = saved; }
        }
    }
}
