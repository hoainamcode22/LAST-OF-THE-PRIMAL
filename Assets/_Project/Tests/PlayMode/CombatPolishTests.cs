using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PrimalFrontier.Animation;
using PrimalFrontier.Audio;
using PrimalFrontier.Combat.Weapons;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.Survival;
using PrimalFrontier.VFX;

namespace PrimalFrontier.Tests
{
    /// <summary>
    /// The light hit reaction as the player's Animator shows it: controllers with the HurtLight trigger play the additive
    /// Hurt_Additive state on the HitReaction layer (HealthState untouched); older controllers pulse HealthState 1 (base Hurt).
    /// </summary>
    public static class HitReactionProbe
    {
        public static bool HasHurtLight(Animator a)
        {
            if (!a || !a.runtimeAnimatorController) return false;
            foreach (var p in a.parameters) if (p.nameHash == AnimParams.HurtLight && p.type == AnimatorControllerParameterType.Trigger) return true;
            return false;
        }

        public static bool InState(Animator a, int layer, string n)
        {
            var s = a.GetCurrentAnimatorStateInfo(layer); var nx = a.GetNextAnimatorStateInfo(layer);
            return s.IsName(n) || (a.IsInTransition(layer) && nx.IsName(n));
        }

        /// <summary>after Hurt(false) on a controller with HurtLight: the flinch plays and ends, HealthState stays 0, the base layer never enters Hurt</summary>
        public static IEnumerator ExpectAdditiveLightHurt(Animator a, float timeout = 0.4f)
        {
            int layer = a.GetLayerIndex("HitReaction");
            Assert.GreaterOrEqual(layer, 0, "the controller has HurtLight but no HitReaction layer");
            float t0 = Time.realtimeSinceStartup; int frames = 0; bool seen = false;
            while ((Time.realtimeSinceStartup - t0 < timeout || frames < 10) && !seen)
            {
                Assert.AreEqual(PlayerHealthStates.Normal, a.GetInteger(AnimParams.HealthState), "a light hit does not set HealthState");
                Assert.IsFalse(InState(a, 0, "Hurt"), "the base layer keeps its locomotion");
                seen = InState(a, layer, "Hurt_Additive"); frames++;
                yield return null;
            }
            Assert.IsTrue(seen, $"Hurt_Additive not reached in {timeout}s");
            t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 2f && InState(a, layer, "Hurt_Additive"))
            {
                Assert.IsFalse(InState(a, 0, "Hurt"), "the base layer keeps its locomotion");
                yield return null;
            }
            Assert.IsFalse(InState(a, layer, "Hurt_Additive"), "the flinch ends");
        }
    }

    /// <summary>Polish: sword block (frontal reduction, rear hits, guard break, no attack behind the guard) and carried weapons (back / hip, draw).</summary>
    public class CombatPolishTests
    {
        const string PlayerPrefab = "Assets/_Project/Prefabs/Player/PFB_Player.prefab";
        GameObject _course, _player, _cam, _input;
        PlayerMotor _motor; PlayerAnimationDriver _drv; PlayerHealth _hp; PlayerCombat _combat; PlayerEquipment _eq; InventorySystem _inv; PlayerSurvival _sv; Animator _anim;
        readonly List<Object> _created = new List<Object>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return TestScenes.ClearIfGameplayLeft();
            PlayerInputReader.Simulate = true; ResetSim();
            _course = TestEnvironment.Build(null);
            _input = new GameObject("Input"); _input.AddComponent<PlayerInputReader>();
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            Assert.IsNotNull(prefab, "PFB_Player.prefab missing (run PrimalPlayerSetup)");
            _player = Object.Instantiate(prefab, new Vector3(0, 0.05f, 0), Quaternion.identity);
#endif
            _motor = _player.GetComponent<PlayerMotor>(); _drv = _player.GetComponent<PlayerAnimationDriver>(); _hp = _player.GetComponent<PlayerHealth>(); _anim = _drv.animator;
            _combat = _player.GetComponent<PlayerCombat>(); _eq = _player.GetComponent<PlayerEquipment>(); _inv = _player.GetComponent<InventorySystem>(); _sv = _player.GetComponent<PlayerSurvival>();
            _cam = new GameObject("Cam"); _cam.tag = "MainCamera"; _cam.AddComponent<Camera>(); var tpc = _cam.AddComponent<ThirdPersonCamera>(); tpc.target = _player.transform; tpc.ignoreMask = 1 << _player.layer;
            _motor.CameraTransform = _cam.transform;
            VfxPool.Instance.ToString(); SfxPlayer.Instance.ToString();
            yield return new WaitForSeconds(0.3f);
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 4f && !(_anim.GetCurrentAnimatorStateInfo(0).IsName("Locomotion") && !_anim.IsInTransition(0))) yield return null;
            _inv.Clear();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerInputReader.Simulate = false; ResetSim();
            foreach (var g in new[] { _player, _cam, _input, _course }) if (g) Object.Destroy(g);
            foreach (var o in _created) if (o) Object.Destroy(o);
            _created.Clear();
            yield return null;
        }

        static void ResetSim() { var s = PlayerInputReader.Sim; s.Move = s.Look = Vector2.zero; s.Sprint = s.Walk = s.Aim = s.Jump = s.Crouch = s.Interact = s.Attack = s.AttackHold = s.Dodge = false; }

        /// <summary>a runtime weapon item (no assets): melee chain of one sword swing unless ranged</summary>
        ItemDefinition Weapon(string id, WeaponKind kind, CarrySocket carry, GameObject model = null, bool throwable = false, int equipAction = 0)
        {
            var wd = ScriptableObject.CreateInstance<WeaponData>();
            wd.name = "WPN_" + id; wd.kind = kind; wd.throwable = throwable; wd.carrySocket = carry; wd.equipAction = equipAction;
            wd.attacks = new[] { new AttackProfile(PlayerActions.SwordAttack1) }; wd.heavyAttack = new AttackProfile();
            var it = ScriptableObject.CreateInstance<ItemDefinition>();
            it.name = "ITEM_" + id; it.id = id; it.displayName = id; it.maxStack = 1; it.category = ItemCategory.Weapon; it.weapon = kind;
            it.weaponData = wd; it.handPrefab = model;
            _created.Add(wd); _created.Add(it);
            return it;
        }

        /// <summary>a stand-in tool model on the pipeline convention: origin at the grip, handle +Z, working end towards -Z</summary>
        GameObject Model(float length)
        {
            var root = new GameObject("TestTool_" + length.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
            root.transform.position = new Vector3(0f, -50f, 0f);
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(0.04f, 0.06f, length);
            body.transform.localPosition = new Vector3(0f, 0f, 0.1f - length * 0.5f);
            _created.Add(root);
            return root;
        }

        static IEnumerator WaitFor(System.Func<bool> cond, float timeout, string what)
        {
            float t0 = Time.realtimeSinceStartup; int frames = 0;
            while ((Time.realtimeSinceStartup - t0 < timeout || frames < 10) && !cond()) { frames++; yield return null; }
            Assert.IsTrue(cond(), what + $" (not within {timeout}s)");
        }

        bool BaseIn(string n) => HitReactionProbe.InState(_anim, 0, n);

        IEnumerator HoldGuard(ItemDefinition weapon)
        {
            _inv.SetSlot(0, new ItemStack(weapon, 1)); _inv.SetActiveSlot(0);
            yield return null;
            PlayerInputReader.Sim.Aim = true;
            yield return WaitFor(() => _combat.IsBlocking, 1f, "guard up");
        }

        // ------------------------------------------------------------------ block
        [Test] public void CanBlock_Only_Melee_Without_Throw()
        {
            var sword = Weapon("t_sword", WeaponKind.Sword, CarrySocket.Back);
            var spear = Weapon("t_spear", WeaponKind.Spear, CarrySocket.Back, null, true);
            var bow = Weapon("t_bow", WeaponKind.Bow, CarrySocket.Back); bow.weaponData.ranged = true;
            Assert.IsTrue(PlayerCombat.CanBlockWith(sword.weaponData), "sword blocks");
            Assert.IsFalse(PlayerCombat.CanBlockWith(spear.weaponData), "spear aims / throws");
            Assert.IsFalse(PlayerCombat.CanBlockWith(bow.weaponData), "bow aims");
            Assert.IsFalse(PlayerCombat.CanBlockWith(null));
        }

        [UnityTest] public IEnumerator Block_Reduces_Frontal_Damage_Not_Rear()
        {
            var sword = Weapon("t_sword", WeaponKind.Sword, CarrySocket.None);
            yield return HoldGuard(sword);
            Assert.AreEqual(PlayerActions.SwordBlock, _drv.CurrentAction, "Sword_Block on the upper body");
            Assert.IsTrue(_motor.AimMode, "walk speed / facing the camera while blocking");
            var p = _player.transform;
            float h0 = _hp.Health, st0 = _sv ? _sv.Stamina : 0f;
            _hp.TakeDamage(20f, p.position + p.forward * 2f + Vector3.up, false);
            Assert.AreEqual(h0 - 20f * (1f - _combat.blockReduction), _hp.Health, 0.01f, "frontal hit reduced");
            Assert.IsTrue(_hp.LastHitBlocked, "the guard held");
            if (_sv) Assert.Less(_sv.Stamina, st0, "a blocked hit costs stamina");
            Assert.IsTrue(_combat.IsBlocking, "the guard holds while stamina lasts");
            // just outside the cone, then straight from behind: full damage
            float h1 = _hp.Health;
            Vector3 side = Quaternion.AngleAxis(_combat.blockArc * 0.5f + 10f, Vector3.up) * p.forward;
            _hp.TakeDamage(10f, p.position + side * 2f + Vector3.up, false);
            Assert.AreEqual(h1 - 10f, _hp.Health, 0.01f, "hit outside the guarded cone lands in full");
            float h2 = _hp.Health;
            _hp.TakeDamage(20f, p.position - p.forward * 2f + Vector3.up, false);
            Assert.AreEqual(h2 - 20f, _hp.Health, 0.01f, "rear hit lands in full");
            Assert.IsFalse(_hp.LastHitBlocked);
            // no attack behind the guard
            PlayerInputReader.Sim.Attack = true;
            yield return null; yield return null;
            Assert.IsFalse(_combat.Weapons.IsAttacking, "cannot attack while blocking");
            Assert.IsTrue(_combat.IsBlocking);
            // release: the guard drops and the walk-speed mode ends
            PlayerInputReader.Sim.Aim = false;
            yield return WaitFor(() => !_combat.IsBlocking, 0.5f, "guard down on release");
            Assert.IsFalse(_motor.AimMode, "back to free movement");
            Assert.AreNotEqual(PlayerActions.SwordBlock, _drv.CurrentAction, "Sword_Block stopped");
        }

        [UnityTest] public IEnumerator Block_Breaks_When_Stamina_Is_Empty()
        {
            Assert.IsNotNull(_sv, "PlayerSurvival on the player");
            var sword = Weapon("t_sword", WeaponKind.Sword, CarrySocket.None);
            yield return HoldGuard(sword);
            _sv.SetStats(_sv.Hunger, _sv.Thirst, 0f, _sv.BodyTemperature, _sv.Wetness);
            var p = _player.transform; float h0 = _hp.Health;
            _hp.TakeDamage(20f, p.position + p.forward * 2f + Vector3.up, false);
            Assert.AreEqual(h0 - 20f, _hp.Health, 0.01f, "no stamina to pay: the hit lands in full");
            Assert.IsFalse(_hp.LastHitBlocked, "the guard broke");
            Assert.IsFalse(_combat.IsBlocking, "guard down");
            Assert.Greater(_combat.GuardBrokenUntil, Time.time, "guard-break lockout");
            yield return WaitFor(() => BaseIn("Hurt_Heavy"), 0.5f, "stagger (Hurt_Heavy)");
            Assert.IsFalse(_combat.IsBlocking, "no guard during the lockout while the button is still held");
        }

        // ------------------------------------------------------------------ carried weapons
        [UnityTest] public IEnumerator Carried_Weapons_Back_And_Hip_Follow_The_Hotbar()
        {
            var sword = Weapon("t_sword", WeaponKind.Sword, CarrySocket.Back, Model(0.9f));
            var spear = Weapon("t_spear", WeaponKind.Spear, CarrySocket.Back, Model(1.8f), true);
            var knife = Weapon("t_knife", WeaponKind.Knife, CarrySocket.Hip, Model(0.3f));
            _inv.SetSlot(0, new ItemStack(sword, 1)); _inv.SetSlot(1, new ItemStack(knife, 1)); _inv.SetSlot(2, new ItemStack(spear, 1));
            _inv.SetActiveSlot(3);                                                   // empty hand
            yield return null; yield return null;
            var hier = _player.GetComponent<PlayerHierarchy>();
            Assert.AreEqual(sword, _eq.CarriedBackItem, "nothing held yet: the first back weapon in the hotbar");
            Assert.AreEqual(knife, _eq.CarriedHipItem, "hip tool");
            var swordCopy = _eq.CarriedBackObject; var knifeCopy = _eq.CarriedHipObject;
            Assert.IsTrue(swordCopy.activeInHierarchy && knifeCopy.activeInHierarchy, "carried copies visible");
            Assert.AreEqual(hier.BackWeaponSocket, swordCopy.transform.parent, "on BackWeaponSocket");
            Assert.AreEqual(hier.HipToolSocket, knifeCopy.transform.parent, "on HipToolSocket");
            Assert.AreEqual(0, swordCopy.GetComponentsInChildren<Collider>(true).Length, "colliders stripped");
            // the handle (+Z) runs up to the right shoulder on the back and points up at the hip
            Assert.Greater(Vector3.Dot(swordCopy.transform.forward, Vector3.up), 0.5f, "back: handle up");
            Assert.Greater(Vector3.Dot(swordCopy.transform.forward, _player.transform.right), 0.2f, "back: handle towards the right shoulder");
            Assert.Greater(Vector3.Dot(knifeCopy.transform.forward, Vector3.up), 0.7f, "hip: handle up");

            _inv.SetActiveSlot(0);                                                   // sword in the hand
            yield return null; yield return null;
            Assert.IsFalse(swordCopy.activeSelf, "no second sword on the back");
            Assert.IsTrue(_eq.HeldObject && _eq.HeldObject.activeInHierarchy, "sword in the hand");
            Assert.AreEqual(spear, _eq.CarriedBackItem, "the other back weapon rides on the back");
            var spearCopy = _eq.CarriedBackObject;

            _inv.SetActiveSlot(2);                                                   // spear in the hand
            yield return null; yield return null;
            Assert.AreEqual(sword, _eq.CarriedBackItem, "the most recently held back weapon");
            Assert.AreSame(swordCopy, _eq.CarriedBackObject, "cached instance reused");
            Assert.IsFalse(spearCopy.activeSelf, "no second spear on the back");

            _inv.SetActiveSlot(1);                                                   // knife in the hand
            yield return null; yield return null;
            Assert.AreEqual(spear, _eq.CarriedBackItem, "the most recently held back weapon");
            Assert.AreSame(spearCopy, _eq.CarriedBackObject, "cached instance reused");
            Assert.IsNull(_eq.CarriedHipItem, "the knife is in the hand");
            Assert.IsFalse(knifeCopy.activeSelf, "no second knife at the hip");
        }

        [UnityTest] public IEnumerator Sword_Draw_Moves_From_Back_To_Hand_Without_Duplicate()
        {
            var sword = Weapon("t_sword_draw", WeaponKind.Sword, CarrySocket.Back, Model(0.9f), false, PlayerActions.SwordEquip);
            sword.weaponData.equipTime = 0.5f;
            _inv.SetSlot(0, new ItemStack(sword, 1)); _inv.SetActiveSlot(1);
            yield return null; yield return null;
            Assert.AreEqual(sword, _eq.CarriedBackItem, "on the back while not held");
            var backCopy = _eq.CarriedBackObject;
            _inv.SetActiveSlot(0);
            Assert.IsTrue(_eq.HeldOnCarrySocket, "drawing: stays on the back until the grab frame");
            Assert.IsFalse(_eq.HeldObject.activeSelf, "the hand copy waits for OnEquip draw");
            float t0 = Time.realtimeSinceStartup; bool inHand = false;
            while (Time.realtimeSinceStartup - t0 < 1.5f && !inHand)
            {
                yield return null;
                bool hand = _eq.HeldObject && _eq.HeldObject.activeInHierarchy, back = backCopy && backCopy.activeInHierarchy;
                Assert.IsFalse(hand && back, "hand and back copies never visible together");
                inHand = hand && !_eq.HeldOnCarrySocket;
            }
            Assert.IsTrue(inHand, "the sword reaches the hand (OnEquip draw, or the equip-time fallback)");
            Assert.IsFalse(backCopy.activeInHierarchy, "back copy hidden");
            Assert.IsNull(_eq.CarriedBackItem, "nothing else to carry on the back");
        }
    }

    /// <summary>
    /// PlayerAnimationDriver.Hurt(false) against a controller built in code (independent of the PlayerAnimator asset):
    /// with the HurtLight trigger the HitReaction layer flinches and HealthState stays 0 (the base keeps its locomotion,
    /// the motor keeps moving); without it the old HealthState 1 pulse still plays the base Hurt state.
    /// </summary>
    public class HitReactionTests
    {
        const string PlayerPrefab = "Assets/_Project/Prefabs/Player/PFB_Player.prefab";
        GameObject _course, _player, _cam, _input;
        PlayerMotor _motor; PlayerAnimationDriver _drv; Animator _anim;
        Object _controller;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerInputReader.Simulate = false; PlayerInputReader.Sim.Move = Vector2.zero;
            foreach (var g in new[] { _player, _cam, _input, _course }) if (g) Object.Destroy(g);
            if (_controller) Object.Destroy(_controller);
            yield return null;
        }

        IEnumerator Spawn(bool withHurtLight)
        {
            yield return TestScenes.ClearIfGameplayLeft();
            PlayerInputReader.Simulate = true; PlayerInputReader.Sim.Move = Vector2.zero;
            _course = TestEnvironment.Build(null);
            _input = new GameObject("Input"); _input.AddComponent<PlayerInputReader>();
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            Assert.IsNotNull(prefab, "PFB_Player.prefab missing (run PrimalPlayerSetup)");
            _player = Object.Instantiate(prefab, new Vector3(0, 0.05f, 0), Quaternion.identity);
            _drv = _player.GetComponent<PlayerAnimationDriver>(); _motor = _player.GetComponent<PlayerMotor>(); _anim = _drv.animator;
            var ac = BuildController(_anim, withHurtLight);
            if (ac == null) Assert.Ignore("the player controller has no Hurt clip to build the test controller from");
            _controller = ac;
            _anim.runtimeAnimatorController = ac;              // same frame as Instantiate: the driver caches this controller's parameters
#else
            Assert.Ignore("needs the editor (AnimatorController built in code)");
#endif
            _cam = new GameObject("Cam"); _cam.tag = "MainCamera"; _cam.AddComponent<Camera>(); var tpc = _cam.AddComponent<ThirdPersonCamera>(); tpc.target = _player.transform; tpc.ignoreMask = 1 << _player.layer;
            tpc.Yaw = 0f;                                      // moving camera-forward = +Z, the open lane of the test course
            _motor.CameraTransform = _cam.transform;
            VfxPool.Instance.ToString(); SfxPlayer.Instance.ToString();
            yield return new WaitForSeconds(0.6f);
        }

#if UNITY_EDITOR
        static AnimationClip FindClip(RuntimeAnimatorController rac, string name)
        {
            if (!rac) return null;
            foreach (var c in rac.animationClips) if (c && c.name == name) return c;
            return null;
        }

        /// <summary>the asset's parameters (so the driver never writes a missing one), a Locomotion + Hurt base layer, and an additive HitReaction layer</summary>
        static UnityEditor.Animations.AnimatorController BuildController(Animator a, bool withHurtLight)
        {
            var src = a.runtimeAnimatorController;
            var hurt = FindClip(src, "Hurt"); if (!hurt) return null;
            var idle = FindClip(src, "Idle");
            var ac = new UnityEditor.Animations.AnimatorController { name = "TestPlayerAnimator" };
            ac.AddLayer("Base Layer");
            foreach (var p in a.parameters)
            {
                if (p.nameHash == AnimParams.HurtLight) continue;
                ac.AddParameter(new AnimatorControllerParameter { name = p.name, type = p.type, defaultBool = p.defaultBool, defaultFloat = p.defaultFloat, defaultInt = p.defaultInt });
            }
            if (withHurtLight) ac.AddParameter("HurtLight", AnimatorControllerParameterType.Trigger);
            var sm = ac.layers[0].stateMachine;
            var loco = sm.AddState("Locomotion"); loco.motion = idle; sm.defaultState = loco;
            var hs = sm.AddState("Hurt"); hs.motion = hurt; hs.tag = "Hurt";
            var toHurt = sm.AddAnyStateTransition(hs); toHurt.duration = 0.05f; toHurt.canTransitionToSelf = false;
            toHurt.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Equals, PlayerHealthStates.HurtLight, "HealthState");
            var back = hs.AddTransition(loco); back.hasExitTime = true; back.exitTime = 0.85f; back.duration = 0.2f;
            if (withHurtLight)
            {
                ac.AddLayer("HitReaction");
                var layers = ac.layers; layers[1].defaultWeight = 1f; layers[1].blendingMode = UnityEditor.Animations.AnimatorLayerBlendingMode.Additive; ac.layers = layers;
                var hsm = ac.layers[1].stateMachine;
                var empty = hsm.AddState("Empty"); hsm.defaultState = empty;
                var add = hsm.AddState("Hurt_Additive"); add.motion = hurt;
                var tin = hsm.AddAnyStateTransition(add); tin.duration = 0.05f; tin.hasExitTime = false; tin.canTransitionToSelf = true;
                tin.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0, "HurtLight");
                var tout = add.AddTransition(empty); tout.hasExitTime = true; tout.exitTime = 0.85f; tout.duration = 0.15f;
            }
            return ac;
        }
#endif

        [UnityTest] public IEnumerator LightHurt_With_HurtLight_Leaves_HealthState_And_Movement()
        {
            yield return Spawn(true);
            Assert.IsTrue(HitReactionProbe.HasHurtLight(_anim), "test controller has HurtLight");
            PlayerInputReader.Sim.Move = new Vector2(0f, 1f);                 // moving when the hit lands
            yield return new WaitForSeconds(0.4f);
            _drv.Hurt(false);
            Assert.AreEqual(PlayerHealthStates.Normal, _anim.GetInteger(AnimParams.HealthState), "HealthState not set");
            for (int i = 0; i < 20; i++)
            {
                yield return null;
                Assert.IsTrue(_motor.CanMove, "the motor keeps moving");
                Assert.IsFalse(_drv.IsBusy, "a light hit does not own the body");
                Assert.Greater(_motor.PlanarSpeed, 0.5f, "still walking / running");
            }
            PlayerInputReader.Sim.Move = Vector2.zero;
            _drv.Hurt(false);
            yield return HitReactionProbe.ExpectAdditiveLightHurt(_anim);
        }

        [UnityTest] public IEnumerator LightHurt_Without_HurtLight_Falls_Back_To_HealthState()
        {
            yield return Spawn(false);
            Assert.IsFalse(HitReactionProbe.HasHurtLight(_anim), "test controller without HurtLight");
            _drv.Hurt(false);
            Assert.AreEqual(PlayerHealthStates.HurtLight, _anim.GetInteger(AnimParams.HealthState), "old controllers: HealthState 1 pulse");
            float t0 = Time.realtimeSinceStartup; int frames = 0;
            while ((Time.realtimeSinceStartup - t0 < 0.4f || frames < 10) && !HitReactionProbe.InState(_anim, 0, "Hurt")) { frames++; yield return null; }
            Assert.IsTrue(HitReactionProbe.InState(_anim, 0, "Hurt"), "base Hurt state");
        }
    }
}
