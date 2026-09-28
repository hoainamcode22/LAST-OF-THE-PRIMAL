using UnityEngine;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.Survival;

namespace PrimalFrontier.Combat.Weapons
{
    /// <summary>where a melee attack is: winding up, hitbox live, recovering. None = no attack running.</summary>
    public enum AttackPhase { None, Startup, Active, Recovery }

    /// <summary>the attack / aim buttons for one frame, as PlayerCombat routes them</summary>
    public struct WeaponInput
    {
        public bool pressed, held, aiming;
        public WeaponInput(bool pressed, bool held, bool aiming) { this.pressed = pressed; this.held = held; this.aiming = aiming; }
    }

    /// <summary>
    /// A weapon in the hand, created by the WeaponController from the active item's WeaponData. Plain C# (no
    /// MonoBehaviour): the controller ticks it and forwards the animation events.
    /// </summary>
    public interface IWeapon
    {
        WeaponData Data { get; }
        ItemDefinition Item { get; }
        /// <summary>an attack / draw is running (the weapon owns the body)</summary>
        bool Busy { get; }
        void Equip(WeaponContext ctx);
        void Unequip();
        /// <summary>buttons for this frame (called from PlayerCombat.Update, before Tick)</summary>
        void HandleInput(WeaponInput input);
        void OnAimChanged(bool aiming);
        /// <summary>the held model was (re)created by PlayerEquipment (null = no model)</summary>
        void OnModelChanged(GameObject model);
        /// <summary>cancel the running attack / draw (dodge, slot change, death)</summary>
        void Cancel();
        /// <summary>forget a press in progress without attacking (input gated: menus, build mode); a running swing goes on</summary>
        void ResetInput();
        void Tick(float dt);
        /// <summary>after the Animator: bone-following visuals</summary>
        void LateTick();
        void OnAnimEvent(string name, string param);
    }

    /// <summary>everything a weapon needs from the player, gathered once by the WeaponController</summary>
    public class WeaponContext
    {
        public GameObject owner;
        public Transform root;
        public WeaponController controller;
        public WeaponAnimatorBridge anim;
        public PlayerAnimationDriver driver;
        public PlayerMotor motor;
        public PlayerSurvival survival;
        public InventorySystem inventory;
        public PlayerEquipment equipment;
        public PlayerHierarchy hierarchy;
        /// <summary>aim / arrow raycasts: everything except the Player and Ignore Raycast layers</summary>
        public int hitMask;
        /// <summary>melee hitboxes: everything except the Player layer (what the old ResolveMelee used)</summary>
        public int meleeMask;
        ThirdPersonCamera _cam;
        public ThirdPersonCamera Camera
        {
            get { if (!_cam && UnityEngine.Camera.main) _cam = UnityEngine.Camera.main.GetComponent<ThirdPersonCamera>(); return _cam; }
        }
    }
}
