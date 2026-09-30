using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PrimalFrontier.Audio;
using PrimalFrontier.Player;
using PrimalFrontier.Survival;
using PrimalFrontier.VFX;
using PrimalFrontier.World;

namespace PrimalFrontier.Tests
{
    /// <summary>
    /// Climbable kinds on greybox rocks (targeted: nothing else can check them): a ledge is pulled over in one move and the
    /// player stands on top; a rock face is climbed with W and pulled over at the top. No teleport (largest per-frame move),
    /// motor / controller / interaction restored.
    /// </summary>
    public class ClimbTests
    {
        const string PlayerPrefab = "Assets/_Project/Prefabs/Player/PFB_Player.prefab";
        GameObject _course, _player, _cam, _input, _rocks;
        PlayerMotor _motor; PlayerInteraction _pi; PlayerSurvival _sv;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return TestScenes.ClearIfGameplayLeft();
            PlayerInputReader.Simulate = true; ResetSim();
            _course = TestEnvironment.Build(null);
            _input = new GameObject("Input"); _input.AddComponent<PlayerInputReader>();
            _rocks = new GameObject("TestRocks");
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            Assert.IsNotNull(prefab, "PFB_Player.prefab missing");
            _player = Object.Instantiate(prefab, new Vector3(0, 0.05f, 0), Quaternion.identity);
#endif
            _motor = _player.GetComponent<PlayerMotor>(); _pi = _player.GetComponent<PlayerInteraction>(); _sv = _player.GetComponent<PlayerSurvival>();
            _cam = new GameObject("Cam"); _cam.tag = "MainCamera"; _cam.AddComponent<Camera>(); var tpc = _cam.AddComponent<ThirdPersonCamera>(); tpc.target = _player.transform; tpc.ignoreMask = 1 << _player.layer;
            _motor.CameraTransform = _cam.transform;
            VfxPool.Instance.ToString(); SfxPlayer.Instance.ToString();
            yield return new WaitForSeconds(0.5f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerInputReader.Simulate = false; ResetSim();
            foreach (var g in new[] { _player, _cam, _input, _course, _rocks }) if (g) Object.Destroy(g);
            yield return null;
        }

        static void ResetSim() { var s = PlayerInputReader.Sim; s.Move = s.Look = Vector2.zero; s.Sprint = s.Walk = s.Aim = s.Jump = s.Crouch = s.Interact = s.Attack = s.AttackHold = s.Heavy = s.Dodge = false; }

        /// <summary>a box rock whose front face is at z = frontZ (facing -Z), top at height, centred on x</summary>
        Climbable Rock(ClimbKind kind, float x, float frontZ, float height, float width = 3f)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = "Rock_" + kind; g.transform.SetParent(_rocks.transform, true);
            g.transform.position = new Vector3(x, height * 0.5f, frontZ + 1f); g.transform.localScale = new Vector3(width, height, 2f);
            var c = Climbable.CreateOn(g, kind, new Vector3(x, 0f, frontZ), new Vector3(x, height, frontZ), null, width * 0.6f);
            return c;
        }

        IEnumerator StandAndClimb(Climbable c, float x, float frontZ)
        {
            _motor.Warp(new Vector3(x, 0.05f, frontZ - 0.75f), Quaternion.LookRotation(Vector3.forward));
            if (_sv) _sv.SetStats(_sv.Hunger, _sv.Thirst, _sv.maxStamina, _sv.BodyTemperature, _sv.Wetness);
            float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < 2f && !(_pi.Target == c && _pi.PromptEnabled && _motor.IsGrounded)) yield return null;
            Assert.AreSame(c, _pi.Target, $"the scan targets the {c.kind} (target {(_pi.Target ? _pi.Target.name : "none")}, prompt '{_pi.Prompt}')");
            Assert.IsTrue(_pi.PromptEnabled, "prompt enabled: " + _pi.Prompt);
            PlayerInputReader.Sim.Interact = true;
        }

        [UnityTest, Timeout(120000)] public IEnumerator Ledge_Pull_Over_Stands_On_Top()
        {
            var c = Rock(ClimbKind.Ledge, 0f, 5.5f, 1.5f);
            yield return new WaitForFixedUpdate();
            Assert.IsNull(c.Validate(), "valid ledge");
            yield return StandAndClimb(c, 0f, 5.5f);
            var climb = _player.GetComponent<PlayerClimb>();
            float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < 1f && !climb.IsClimbing) yield return null;
            Assert.IsTrue(climb.IsClimbing, "the pull-over starts");
            Assert.AreEqual(PlayerClimb.Phase.Mantling, climb.State, "a ledge goes straight to the pull-over");
            t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < 5f && climb.IsClimbing) yield return null;
            Assert.IsFalse(climb.IsClimbing, "the pull-over ends");
            var p = _player.transform.position;
            Assert.AreEqual(1.5f, p.y, 0.12f, "standing on the ledge top");
            Assert.Greater(p.z, 5.6f, "past the lip");
            Assert.Less(climb.MaxStep, 0.3f, "no teleport (largest single-frame move)");
            Assert.IsTrue(_motor.enabled && _player.GetComponent<CharacterController>().enabled && !_pi.Suspended, "motor, controller and interaction back");
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(1.5f, _player.transform.position.y, 0.12f, "stays on top (no fall-through)");
            Debug.Log($"[Climb] ledge 1.5 m: on top at {p}, largest step {climb.MaxStep:F3} m");
        }

        [UnityTest, Timeout(120000)] public IEnumerator RockFace_Climb_Up_And_Over()
        {
            var c = Rock(ClimbKind.RockFace, 8f, 5.5f, 3.2f);
            yield return new WaitForFixedUpdate();
            Assert.IsNull(c.Validate(), "valid rock face");
            yield return StandAndClimb(c, 8f, 5.5f);
            var climb = _player.GetComponent<PlayerClimb>();
            float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < 2f && climb.State != PlayerClimb.Phase.Hanging) yield return null;
            Assert.AreEqual(PlayerClimb.Phase.Hanging, climb.State, "on the face");
            float dz = Vector3.Dot(_player.transform.position - c.Bottom, -c.FaceNormal);
            Assert.Less(Mathf.Abs(dz + climb.faceOffset), 0.1f, "held at faceOffset from the face");
            PlayerInputReader.Sim.Move = new Vector2(0f, 1f);
            t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < 10f && climb.IsClimbing) yield return null;
            PlayerInputReader.Sim.Move = Vector2.zero;
            Assert.IsFalse(climb.IsClimbing, $"up and over (state {climb.State}, height {climb.Height:F2})");
            var p = _player.transform.position;
            Assert.AreEqual(3.2f, p.y, 0.12f, "standing on top of the rock");
            Assert.Less(climb.MaxStep, 0.3f, "no teleport");
            Debug.Log($"[Climb] rock face 3.2 m: on top at {p}, largest step {climb.MaxStep:F3} m");
        }

        [UnityTest, Timeout(120000)] public IEnumerator RockFace_Climb_Down_Steps_Off()
        {
            var c = Rock(ClimbKind.RockFace, -8f, 5.5f, 3.2f);
            yield return StandAndClimb(c, -8f, 5.5f);
            var climb = _player.GetComponent<PlayerClimb>();
            float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < 2f && climb.State != PlayerClimb.Phase.Hanging) yield return null;
            PlayerInputReader.Sim.Move = new Vector2(0f, -1f);
            t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < 4f && climb.IsClimbing) yield return null;
            PlayerInputReader.Sim.Move = Vector2.zero;
            Assert.IsFalse(climb.IsClimbing, "stepped off at the bottom");
            var p = _player.transform.position;
            Assert.AreEqual(0f, p.y, 0.12f, "back on the ground");
            Assert.Less(p.z, 5.5f - 0.3f, "in front of the face");
        }
    }
}
