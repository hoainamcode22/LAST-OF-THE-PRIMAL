using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PrimalFrontier.Animation;
using PrimalFrontier.Audio;
using PrimalFrontier.Player;
using PrimalFrontier.VFX;

namespace PrimalFrontier.Tests
{
    /// <summary>Phases 4-6: damage feedback (hit reaction, blood, bleed, screen), death / revive, VFX pooling, action VFX, footsteps, audio library.</summary>
    public class PlayerFeedbackTests
    {
        GameObject _course, _player, _cam, _input;
        PlayerMotor _motor; PlayerAnimationDriver _drv; PlayerHealth _hp; Animator _anim;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return TestScenes.ClearIfGameplayLeft();
            PlayerInputReader.Simulate = true; var s = PlayerInputReader.Sim; s.Move = Vector2.zero; s.Sprint = s.Walk = s.Jump = s.Crouch = false;
            _course = TestEnvironment.Build(null);
            _input = new GameObject("Input"); _input.AddComponent<PlayerInputReader>();
#if UNITY_EDITOR
            _player = Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player/PFB_Player.prefab"), new Vector3(0, 0.05f, 0), Quaternion.identity);
#endif
            _motor = _player.GetComponent<PlayerMotor>(); _drv = _player.GetComponent<PlayerAnimationDriver>(); _hp = _player.GetComponent<PlayerHealth>(); _anim = _drv.animator;
            _cam = new GameObject("Cam"); _cam.tag = "MainCamera"; _cam.AddComponent<Camera>(); var tpc = _cam.AddComponent<ThirdPersonCamera>(); tpc.target = _player.transform; tpc.ignoreMask = 1 << _player.layer;
            _motor.CameraTransform = _cam.transform;
            VfxPool.Instance.ToString(); SfxPlayer.Instance.ToString();
            yield return new WaitForSeconds(0.3f);
            // settle: spawn landing finished and its dust returned to the pool
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 4f && (!(_anim.GetCurrentAnimatorStateInfo(0).IsName("Locomotion") && !_anim.IsInTransition(0)) || VfxPool.Instance.ActiveCount > 0)) yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerInputReader.Simulate = false; PlayerInputReader.Sim.Move = Vector2.zero;
            foreach (var g in new[] { _player, _cam, _input, _course }) if (g) Object.Destroy(g);
            yield return null;
        }

        bool InState(string n) { var a = _anim.GetCurrentAnimatorStateInfo(0); var b = _anim.GetNextAnimatorStateInfo(0); return a.IsName(n) || (_anim.IsInTransition(0) && b.IsName(n)); }
        static readonly string[] Known = { "Locomotion", "Crouch", "TurnInPlace", "Jump", "Fall", "Land", "Pickup", "Gather_Wood", "Gather_Stone", "Gather_Plant", "Interact", "Craft", "Eat", "Drink",
            "Build", "Use_Item", "Sleep", "Wake_Up", "Get_Up", "Unconscious", "Attack_Spear", "Attack_Spear_Heavy", "Throw_Spear", "Hurt", "Hurt_Heavy", "Death" };
        string Name(AnimatorStateInfo s) { foreach (var k in Known) if (s.IsName(k)) return k; return "?"; }
        string Diag() => $"[cur {Name(_anim.GetCurrentAnimatorStateInfo(0))} next {(_anim.IsInTransition(0) ? Name(_anim.GetNextAnimatorStateInfo(0)) : "-")} Action={_anim.GetInteger(AnimParams.Action)} IsAtk={_anim.GetBool(AnimParams.IsAttacking)} Grounded={_anim.GetBool(AnimParams.IsGrounded)} vy={_anim.GetFloat(AnimParams.VerticalVelocity):F1} pos={_player.transform.position}]";
        /// frame-rate independent wait (a loading hitch cannot eat the timeout)
        IEnumerator WaitState(string n, float timeout)
        {
            float t0 = Time.realtimeSinceStartup; int frames = 0;
            while ((Time.realtimeSinceStartup - t0 < timeout || frames < 10) && !InState(n)) { frames++; yield return null; }
            Assert.IsTrue(InState(n), $"state {n} not reached in {timeout}s {Diag()}");
        }

        static IEnumerator WaitPoolIdle(float timeout)
        {
            float t = 0f;
            while (t < timeout && VfxPool.Instance.ActiveCount > 0) { t += Time.deltaTime; yield return null; }
        }

        [Test] public void Libraries_Loaded()
        {
            Assert.IsNotNull(Resources.Load<VfxLibrary>("VfxLibrary"), "VfxLibrary");
            var sfx = Resources.Load<SfxLibrary>("SfxLibrary"); Assert.IsNotNull(sfx, "SfxLibrary");
            Assert.GreaterOrEqual(sfx.entries.Count, 30, "sfx ids");
        }

        [UnityTest] public IEnumerator LightHit_ReducesHealth_PlaysReaction_Blood_Overlay()
        {
            yield return new WaitForSeconds(0.1f);
            int before = VfxPool.Instance.ActiveCount;      // 0 after the settle in SetUp
            _hp.TakeDamage(12f, _player.transform.position + _player.transform.forward * 2f, false);
            Assert.AreEqual(88f, _hp.Health, 0.01f);
            // the hit spark / blood is short-lived: check it before waiting for the whole flinch to play out
            Assert.Greater(VfxPool.Instance.ActiveCount, before, "blood effect spawned");
            if (HitReactionProbe.HasHurtLight(_anim)) yield return HitReactionProbe.ExpectAdditiveLightHurt(_anim, 0.4f);   // HitReaction layer
            else yield return WaitState("Hurt", 0.4f);                                                                       // controller built before it
            Assert.IsNotNull(Object.FindFirstObjectByType<PrimalFrontier.UI.DamageOverlay>(), "screen feedback");
            yield return new WaitForSeconds(1f);
            yield return WaitPoolIdle(4f);
            Assert.AreEqual(before, VfxPool.Instance.ActiveCount, "effects returned to the pool");
        }

        [UnityTest] public IEnumerator HeavyHit_Bleeds_ThenStops()
        {
            _hp.TakeDamage(25f, _player.transform.position - _player.transform.right * 2f, true, 2.5f);
            yield return WaitState("Hurt_Heavy", 0.4f);
            Assert.IsTrue(_hp.IsBleeding);
            float h0 = _hp.Health; yield return new WaitForSeconds(1.0f);
            Assert.Less(_hp.Health, h0, "bleeding drains health");
            yield return new WaitForSeconds(3f);
            Assert.IsFalse(_hp.IsBleeding, "bleeding stops");
            yield return WaitPoolIdle(5f);                          // last drops fade out (random lifetimes)
            Assert.AreEqual(0, VfxPool.Instance.ActiveCount, "bleed effect returned; active: " + VfxPool.Instance.ActiveNames());
        }

        [UnityTest] public IEnumerator LethalDamage_Death_ThenRevive()
        {
            _hp.TakeDamage(500f, _player.transform.position + Vector3.forward, true);
            Assert.IsTrue(_hp.IsDead);
            yield return WaitState("Death", 0.5f);
            yield return new WaitForSeconds(2.6f);
            Assert.IsTrue(InState("Death"));
            _hp.Revive(0.5f);
            Assert.AreEqual(50f, _hp.Health, 0.01f);
            yield return WaitState("Get_Up", 0.6f);
            yield return WaitState("Locomotion", 3.5f);
        }

        [UnityTest] public IEnumerator VfxPool_Reuses_NoGrowthOnSecondRound()
        {
            var pool = VfxPool.Instance;
            foreach (VfxId id in System.Enum.GetValues(typeof(VfxId))) if (id != VfxId.None && id != VfxId.Bleed && id != VfxId.Steam && id != VfxId.CookSmoke) pool.Play(id, Vector3.up, Vector3.up);
            yield return new WaitForSeconds(3f);
            yield return WaitPoolIdle(6f);
            Assert.AreEqual(0, pool.ActiveCount, "all one-shots finished; active: " + pool.ActiveNames());
            int created = pool.CreatedCount;
            foreach (VfxId id in System.Enum.GetValues(typeof(VfxId))) if (id != VfxId.None && id != VfxId.Bleed && id != VfxId.Steam && id != VfxId.CookSmoke) pool.Play(id, Vector3.up, Vector3.up);
            yield return null;
            Assert.AreEqual(created, pool.CreatedCount, "second round reused pooled instances");
            yield return new WaitForSeconds(4f);
        }

        [UnityTest] public IEnumerator Eat_Hot_Food_Spawns_Crumbs_And_Steam()
        {
            _player.GetComponent<PlayerFeedback>().HotFood = true;
            _drv.PlayAction(PlayerActions.Eat);
            yield return WaitState("Eat", 0.5f);
            int peak = 0; float t = 0;
            while (t < 1.8f) { peak = Mathf.Max(peak, VfxPool.Instance.ActiveCount); t += Time.deltaTime; yield return null; }
            Assert.GreaterOrEqual(peak, 2, "crumbs + steam");
        }

        [UnityTest] public IEnumerator Gather_And_Drink_And_Craft_Events_Spawn_Effects()
        {
            foreach (var a in new[] { PlayerActions.GatherWood, PlayerActions.GatherStone, PlayerActions.GatherPlant, PlayerActions.Craft })
            {
                _drv.PlayAction(a);
                int peak = 0; float t = 0;
                while (t < 2.6f) { peak = Mathf.Max(peak, VfxPool.Instance.ActiveCount); t += Time.deltaTime; yield return null; }
                Assert.Greater(peak, 0, "effects for action " + a);
                _drv.StopAction(); yield return new WaitForSeconds(0.5f);
            }
            _drv.PlayAction(PlayerActions.Drink);
            int p2 = 0; float t2 = 0;
            while (t2 < 2.8f) { p2 = Mathf.Max(p2, VfxPool.Instance.ActiveCount); t2 += Time.deltaTime; yield return null; }
            Assert.Greater(p2, 0, "drink splash");
        }

        [UnityTest] public IEnumerator Running_Plays_Footstep_Effects()
        {
            PlayerInputReader.Sim.Move = new Vector2(0, 1); PlayerInputReader.Sim.Sprint = true;
            int peak = 0; float t = 0;
            while (t < 2f) { peak = Mathf.Max(peak, VfxPool.Instance.ActiveCount); t += Time.deltaTime; yield return null; }
            Assert.Greater(peak, 0, "footstep dust while sprinting");
        }
    }
}
