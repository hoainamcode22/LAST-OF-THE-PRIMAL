using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PrimalFrontier.Animation;
using PrimalFrontier.Player;

namespace PrimalFrontier.Tests
{
    /// <summary>Phase 3 player checks: locomotion speeds, jump, crouch, slopes, actions, attacks, hurt / death / respawn,
    /// wake-up, camera collision and facial blinking. Any error / exception logged during a test fails it.</summary>
    public class PlayerControllerTests
    {
        GameObject _course, _player, _cam, _input;
        PlayerMotor _motor; PlayerAnimationDriver _drv; Animator _anim; ThirdPersonCamera _tpc;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            PlayerInputReader.Simulate = true; ResetSim();
            _course = TestEnvironment.Build(null);
            _input = new GameObject("Input"); _input.AddComponent<PlayerInputReader>();
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player/PFB_Player.prefab");
            Assert.IsNotNull(prefab, "PFB_Player.prefab missing (run PrimalPlayerSetup)");
            _player = Object.Instantiate(prefab, new Vector3(0, 0.05f, 0), Quaternion.identity);
#endif
            _motor = _player.GetComponent<PlayerMotor>(); _drv = _player.GetComponent<PlayerAnimationDriver>(); _anim = _drv.animator;
            _cam = new GameObject("Cam"); _cam.tag = "MainCamera"; _cam.AddComponent<Camera>();
            _tpc = _cam.AddComponent<ThirdPersonCamera>(); _tpc.target = _player.transform; _tpc.ignoreMask = 1 << _player.layer;
            _motor.CameraTransform = _cam.transform;
            PrimalFrontier.VFX.VfxPool.Instance.ToString(); PrimalFrontier.Audio.SfxPlayer.Instance.ToString();   // warm up (no first-use hitch)
            yield return new WaitForSeconds(0.4f);
            // spawn drop / first-frame Fall->Land must be over before a test starts (slow machines)
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 4f && !(_anim.GetCurrentAnimatorStateInfo(0).IsName("Locomotion") && !_anim.IsInTransition(0))) yield return null;
            _tpc.Yaw = 0f; _tpc.Pitch = 12f;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerInputReader.Simulate = false; ResetSim();
            foreach (var g in new[] { _player, _cam, _input, _course }) if (g) Object.Destroy(g);
            yield return null;
        }

        static void ResetSim() { var s = PlayerInputReader.Sim; s.Move = s.Look = Vector2.zero; s.Sprint = s.Walk = s.Aim = s.Jump = s.Crouch = s.Interact = s.Attack = false; }
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

        IEnumerator MoveAndMeasure(float seconds, System.Action<float, float> check)
        {
            Vector3 p0 = _player.transform.position; yield return new WaitForSeconds(seconds);
            Vector3 d = _player.transform.position - p0; d.y = 0;
            check(d.magnitude, _motor.PlanarSpeed);
        }

        [UnityTest] public IEnumerator Run_ReachesRunSpeed_AndLocomotionState()
        {
            PlayerInputReader.Sim.Move = new Vector2(0, 1);
            yield return new WaitForSeconds(1.0f);
            yield return MoveAndMeasure(1.0f, (dist, spd) =>
            {
                Assert.AreEqual(_motor.runSpeed, spd, 0.25f, "run speed");
                Assert.AreEqual(_motor.runSpeed, dist, 0.35f, "distance per second");
            });
            Assert.IsTrue(_motor.IsGrounded, "grounded while running");
            Assert.IsTrue(InState("Locomotion"));
            Assert.AreEqual(_motor.runSpeed, _anim.GetFloat(AnimParams.Speed), 0.3f, "animator Speed");
            Assert.Greater(_player.transform.position.z, 5f, "moved camera-forward (+Z)");
            Assert.Greater(Vector3.Dot(_player.transform.forward, Vector3.forward), 0.95f, "faces the move direction");
        }

        [UnityTest] public IEnumerator Sprint_And_Walk_Speeds()
        {
            PlayerInputReader.Sim.Move = new Vector2(0, 1); PlayerInputReader.Sim.Sprint = true;
            yield return new WaitForSeconds(1.5f);
            Assert.AreEqual(_motor.sprintSpeed, _motor.PlanarSpeed, 0.3f, "sprint speed");
            PlayerInputReader.Sim.Sprint = false; PlayerInputReader.Sim.Walk = true;
            yield return new WaitForSeconds(1.2f);
            Assert.AreEqual(_motor.walkSpeed, _motor.PlanarSpeed, 0.2f, "walk speed");
            PlayerInputReader.Sim.Move = Vector2.zero; PlayerInputReader.Sim.Walk = false;
            yield return new WaitForSeconds(0.8f);
            Assert.Less(_motor.PlanarSpeed, 0.05f, "stops (deceleration)");
        }

        [UnityTest] public IEnumerator Jump_LeavesGround_AndLands()
        {
            float y0 = _player.transform.position.y;
            PlayerInputReader.Sim.Jump = true;
            yield return new WaitForSeconds(0.2f);
            Assert.IsFalse(_motor.IsGrounded, "airborne");
            Assert.Greater(_player.transform.position.y, y0 + 0.3f, "went up");
            Assert.IsTrue(InState("Jump") || InState("Fall"), "jump/fall state " + Diag());
            yield return new WaitForSeconds(1.2f);
            Assert.IsTrue(_motor.IsGrounded, "landed");
            Assert.AreEqual(y0, _player.transform.position.y, 0.08f, "back on the ground");
        }

        [UnityTest] public IEnumerator Crouch_ChangesHeight_Speed_State()
        {
            PlayerInputReader.Sim.Crouch = true; yield return null; yield return null;
            Assert.IsTrue(_motor.IsCrouching);
            Assert.AreEqual(_motor.crouchHeight, _motor.Controller.height, 0.01f);
            PlayerInputReader.Sim.Move = new Vector2(0, 1);
            yield return new WaitForSeconds(1.2f);
            Assert.LessOrEqual(_motor.PlanarSpeed, _motor.crouchSpeed + 0.05f);
            Assert.IsTrue(InState("Crouch"));
            PlayerInputReader.Sim.Move = Vector2.zero; PlayerInputReader.Sim.Crouch = true; yield return null; yield return null;
            Assert.IsFalse(_motor.IsCrouching, "stands up again");
        }

        [UnityTest] public IEnumerator Slopes_WalkableRamp_And_SteepSlope()
        {
            _motor.Warp(new Vector3(3f, 0.05f, 0), Quaternion.Euler(0, 90, 0));
            _tpc.Yaw = 90f; yield return null;
            PlayerInputReader.Sim.Move = new Vector2(0, 1);
            yield return new WaitForSeconds(3.0f);
            Assert.Greater(_player.transform.position.y, 1.0f, "climbs the 20 deg ramp");
            PlayerInputReader.Sim.Move = Vector2.zero;
            _motor.Warp(new Vector3(-4f, 0.05f, 0), Quaternion.Euler(0, -90, 0));
            _tpc.Yaw = -90f; yield return null;
            PlayerInputReader.Sim.Move = new Vector2(0, 1);
            yield return new WaitForSeconds(3.0f);
            Assert.Less(_player.transform.position.y, 0.8f, "cannot walk up a 55 deg slope");
        }

        [UnityTest] public IEnumerator OneShotAction_Eat_LocksMovement_ThenReturns()
        {
            _drv.PlayAction(PlayerActions.Eat);
            yield return WaitState("Eat", 0.5f);
            yield return null;
            Assert.IsFalse(_motor.CanMove, "cannot move while eating");
            yield return new WaitForSeconds(2.4f);
            Assert.IsTrue(InState("Locomotion"), "back to locomotion after the one-shot");
            Assert.IsTrue(_motor.CanMove);
            Assert.AreEqual(PlayerActions.None, _anim.GetInteger(AnimParams.Action), "Action parameter cleared");
        }

        [UnityTest] public IEnumerator LoopAction_GatherWood_UntilStopped()
        {
            _drv.PlayAction(PlayerActions.GatherWood);
            yield return WaitState("Gather_Wood", 0.5f);
            yield return new WaitForSeconds(3.2f);
            Assert.IsTrue(InState("Gather_Wood"), "keeps gathering");
            _drv.StopAction();
            yield return WaitState("Locomotion", 0.8f);
        }

        IEnumerator WaitSettled(string n, float timeout)
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < timeout && !(_anim.GetCurrentAnimatorStateInfo(0).IsName(n) && !_anim.IsInTransition(0))) yield return null;
            Assert.IsTrue(_anim.GetCurrentAnimatorStateInfo(0).IsName(n) && !_anim.IsInTransition(0), $"not settled in {n} {Diag()}");
        }

        [UnityTest] public IEnumerator Attacks_Light_And_Heavy()
        {
            _drv.Attack(PlayerActions.AttackSpear);
            yield return WaitState("Attack_Spear", 0.4f);
            yield return WaitSettled("Locomotion", 2.0f);
            _drv.Attack(PlayerActions.AttackSpearHeavy);
            yield return WaitState("Attack_Spear_Heavy", 0.4f);
            yield return WaitSettled("Locomotion", 2.6f);
        }

        [UnityTest] public IEnumerator Attack_Buffered_During_Previous_Swing()
        {
            _drv.Attack(PlayerActions.AttackSpear);
            yield return WaitState("Attack_Spear", 0.4f);
            yield return new WaitForSeconds(0.3f);
            _drv.Attack(PlayerActions.AttackSpearHeavy);                      // pressed mid-swing -> queued
            yield return WaitState("Attack_Spear_Heavy", 1.4f);
        }

        [UnityTest] public IEnumerator Hurt_Death_Respawn()
        {
            _drv.Hurt(false);
            yield return WaitState("Hurt", 0.3f);
            yield return WaitState("Locomotion", 1.5f);
            _drv.Hurt(true);
            yield return WaitState("Hurt_Heavy", 0.3f);
            yield return WaitState("Locomotion", 2.2f);
            _drv.Die();
            yield return WaitState("Death", 0.5f);
            yield return new WaitForSeconds(3f);
            Assert.IsTrue(InState("Death"), "stays dead");
            Assert.IsFalse(_motor.CanMove);
            _drv.Respawn();
            yield return WaitState("Get_Up", 0.6f);
            yield return WaitState("Locomotion", 3.5f);
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(_motor.CanMove);
        }

        [UnityTest] public IEnumerator WakeUp_FromUnconscious()
        {
            _drv.SetUnconscious();
            yield return new WaitForSeconds(1f);
            Assert.IsTrue(InState("Unconscious"));
            Assert.IsFalse(_motor.CanMove, "no control while unconscious");
            _drv.PlayAction(PlayerActions.WakeUp);
            yield return WaitState("Wake_Up", 0.4f);
            yield return WaitState("Locomotion", 8.5f);
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(_motor.CanMove, "control returns after waking up");
        }

        [UnityTest] public IEnumerator Camera_DoesNotClipThroughWall()
        {
            _motor.Warp(new Vector3(0, 0.05f, -10.8f), Quaternion.identity);   // wall at z = -12 (0.25 thick)
            _tpc.Yaw = 0f; _tpc.Pitch = 10f; _tpc.SnapBehindTarget();
            yield return new WaitForSeconds(0.6f);
            Assert.Greater(_cam.transform.position.z, -11.75f + 0.05f, "camera stays in front of the wall");
        }

        [UnityTest] public IEnumerator Stairs_StepUp()
        {
            _motor.Warp(new Vector3(15f, 0.05f, 6f), Quaternion.identity);
            _tpc.Yaw = 0f; yield return null;
            PlayerInputReader.Sim.Move = new Vector2(0, 1); PlayerInputReader.Sim.Walk = true;
            yield return new WaitForSeconds(3.2f);
            Assert.Greater(_player.transform.position.y, 1.0f, "walks up 0.2 m steps " + Diag());
        }

        [UnityTest] public IEnumerator Face_Blinks()
        {
            var face = _player.GetComponent<PlayerFacial>().face;
            Assert.IsNotNull(face, "LOD0 face renderer");
            int idx = face.sharedMesh.GetBlendShapeIndex("Blink");
            Assert.GreaterOrEqual(idx, 0, "Blink blendshape imported");
            float max = 0; float t = 0;
            while (t < 7f) { max = Mathf.Max(max, face.GetBlendShapeWeight(idx)); t += Time.deltaTime; yield return null; }
            Assert.Greater(max, 60f, "eyes blink");
        }
    }
}
