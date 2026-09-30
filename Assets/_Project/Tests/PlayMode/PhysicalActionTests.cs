using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PrimalFrontier.Animation;
using PrimalFrontier.Audio;
using PrimalFrontier.Player;
using PrimalFrontier.Survival;
using PrimalFrontier.VFX;
using PrimalFrontier.World;

namespace PrimalFrontier.Tests
{
    /// <summary>
    /// Physical drinking (targeted: only the running body can show it): at a water source the player kneels facing the
    /// water, the hands go down to the surface, then up to the mouth, the drink lands on that moment, thirst improves.
    /// </summary>
    public class PhysicalActionTests
    {
        const string PlayerPrefab = "Assets/_Project/Prefabs/Player/PFB_Player.prefab";
        GameObject _course, _player, _cam, _input, _water;
        PlayerInteraction _pi; PlayerSurvival _sv; Animator _anim; CharacterAnimationEvents _ev;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return TestScenes.ClearIfGameplayLeft();
            PlayerInputReader.Simulate = true;
            _course = TestEnvironment.Build(null);
            _input = new GameObject("Input"); _input.AddComponent<PlayerInputReader>();
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            Assert.IsNotNull(prefab, "PFB_Player.prefab missing");
            _player = Object.Instantiate(prefab, new Vector3(0, 0.05f, 0), Quaternion.identity);
#endif
            _pi = _player.GetComponent<PlayerInteraction>(); _sv = _player.GetComponent<PlayerSurvival>();
            _anim = _player.GetComponent<PlayerAnimationDriver>().animator; _ev = _player.GetComponentInChildren<CharacterAnimationEvents>();
            _anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;     // the test camera may not render the body: IK must still run
            _cam = new GameObject("Cam"); _cam.tag = "MainCamera"; _cam.AddComponent<Camera>(); var tpc = _cam.AddComponent<ThirdPersonCamera>(); tpc.target = _player.transform; tpc.ignoreMask = 1 << _player.layer;
            _player.GetComponent<PlayerMotor>().CameraTransform = _cam.transform;
            PlayerLocator.Player = _player.transform;
            VfxPool.Instance.ToString(); SfxPlayer.Instance.ToString();
            yield return new WaitForSeconds(0.5f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerInputReader.Simulate = false;
            if (PlayerLocator.Player == (_player ? _player.transform : null)) PlayerLocator.Player = null;
            foreach (var g in new[] { _player, _cam, _input, _course, _water }) if (g) Object.Destroy(g);
            yield return null;
        }

        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

        [UnityTest, Timeout(120000)] public IEnumerator Kneel_Drink_At_Water_Hands_To_Surface_Then_Mouth()
        {
            // a shallow fresh pool 1.1-4.1 m ahead, its surface just above the course floor
            _water = GameObject.CreatePrimitive(PrimitiveType.Plane); _water.name = "TestPool";
            Object.Destroy(_water.GetComponent<Collider>());
            _water.transform.position = new Vector3(0f, 0.03f, 2.6f); _water.transform.localScale = new Vector3(0.3f, 1f, 0.3f);
            var ws = _water.AddComponent<WaterSource>(); ws.displayName = "Test pool"; ws.fresh = true; ws.clean = true;
            ws.surface = _water.GetComponent<MeshFilter>();
            ws.enabled = false; ws.enabled = true;                                           // re-cache the surface points
            _player.transform.rotation = Quaternion.LookRotation(Vector3.right);             // facing away a bit: the body turns to the water
            yield return new WaitForSeconds(0.3f);
            float thirst0 = _sv ? _sv.Thirst : 0f;
            if (_sv) _sv.SetStats(_sv.Hunger, 40f, _sv.Stamina, _sv.BodyTemperature, _sv.Wetness);
            thirst0 = _sv ? _sv.Thirst : 0f;
            int drinks = 0; float drinkAt = -1f;
            System.Action<string, string> onEv = (fn, p) => { if (fn == "OnDrink") { drinks++; drinkAt = Time.time; } };
            _ev.AnimationEventRaised += onEv;
            try
            {
                ws.Interact(_pi);
                float t0 = Time.time;
                bool kneel = false; float bestGoal = 99f, bestMouth = 99f, lowest = 99f, nearest = 99f; Vector3 wp = ws.FocusPoint; float maxW = 0f, maxDrop = 0f;
                var ik = _anim.GetComponent<PlayerIK>();
                Transform lh = _anim.GetBoneTransform(HumanBodyBones.LeftHand), rh = _anim.GetBoneTransform(HumanBodyBones.RightHand), head = _anim.GetBoneTransform(HumanBodyBones.Head);
                float start = (lh.position.y + rh.position.y) * 0.5f;
                while (Time.time - t0 < 3.5f && (_pi.InAction || Time.time - t0 < 0.5f))
                {
                    yield return null;
                    float t = Time.time - t0;
                    maxW = Mathf.Max(maxW, ik.ActionHandWeight); maxDrop = Mathf.Max(maxDrop, ik.ActionDrop);
                    kneel |= _anim.GetCurrentAnimatorStateInfo(0).IsName("Drink_Kneel") || _anim.GetNextAnimatorStateInfo(0).IsName("Drink_Kneel");
                    Vector3 hands = (lh.position + rh.position) * 0.5f;
                    if (t > 0.7f && t < 1.0f) { bestGoal = Mathf.Min(bestGoal, (hands - ik.ActionHandGoal).magnitude); nearest = Mathf.Min(nearest, (hands - wp).magnitude); lowest = Mathf.Min(lowest, hands.y - wp.y); }
                    Vector3 mouth = head.position + _player.transform.forward * 0.1f - Vector3.up * 0.05f;
                    if (t > 1.2f && t < 1.65f) bestMouth = Mathf.Min(bestMouth, (hands - mouth).magnitude);
                }
                Vector3 toWater = wp - _player.transform.position; toWater.y = 0f;
                float facing = Vector3.Angle(_player.transform.forward, toWater);
                Debug.Log($"[Drink] hand weight max {maxW:F2}, kneel state {kneel}, hands: to the IK goal {bestGoal:F3} m, to the water point {nearest:F3} m (water {toWater.magnitude:F2} m ahead), lowest {lowest:F3} m above the surface (from {start - wp.y:F2} m), body sank {maxDrop:F2} m, edge now {Flat(wp - _player.transform.position):F2} m, to the mouth {bestMouth:F3} m, OnDrink x{drinks} at {(drinkAt > 0 ? drinkAt - t0 : -1):F2} s, facing the water {facing:F1} deg, thirst {thirst0:F1} -> {(_sv ? _sv.Thirst : 0f):F1}");
                Assert.IsTrue(kneel, "Drink_Kneel played (kneel at the water)");
                Assert.Less(facing, 20f, "facing the water");
                Assert.Greater(maxW, 0.9f, "the program drives the hands");
                Assert.Less(bestGoal, 0.1f, "the hands get to the scoop point the body can reach");
                Assert.Less(lowest, start - wp.y - 0.3f, "the hands go well down toward the water");
                Assert.Less(bestMouth, 0.18f, "the hands come up to the mouth");
                Assert.Less(Flat(wp - _player.transform.position), 0.75f, "stepped up to the edge");
                Assert.AreEqual(1, drinks, "one drink event");
                if (_sv) Assert.Greater(_sv.Thirst, thirst0, "thirst improves");
                Assert.IsFalse(_pi.InAction, "the action ends");
            }
            finally { _ev.AnimationEventRaised -= onEv; }
        }
    }
}
