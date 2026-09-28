using System;
using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.Survival;

namespace PrimalFrontier.Combat.Weapons
{
    /// <summary>
    /// Owns the weapon in the player's hand. It follows the active hotbar item: an item with WeaponData becomes a
    /// MeleeWeapon or RangedWeapon (a bow without data gets the legacy bow numbers), anything else means no weapon.
    /// PlayerCombat keeps food / placeables / the spear throw and hands the attack and aim buttons to it; the animation
    /// events reach the weapon through the WeaponAnimatorBridge. Added by PlayerCombat when missing.
    /// </summary>
    [DisallowMultipleComponent]
    public class WeaponController : MonoBehaviour
    {
        [Tooltip("log attack starts, phases and hits to the console (hitbox window checks)")] public bool logAttacks;
        [Tooltip("CombatMode stays on this long after the last attack / hit (s)")] public float combatModeHold = 3f;
        [Header("Legacy bow (bow items without WeaponData)")]
        [Tooltip("set from PlayerCombat.arrowSpeed")] public float legacyArrowSpeed = 38f;
        [Tooltip("set from PlayerCombat.bowFullDraw")] public float legacyDrawTime = 0.9f;

        public IWeapon Current { get; private set; }
        public WeaponData CurrentData => Current?.Data;
        public ItemDefinition CurrentItem => _item;
        public bool HasWeapon => Current != null;
        /// <summary>a melee attack is between its start and the end of its recovery</summary>
        public bool IsAttacking => Current is MeleeWeapon m && m.Phase != AttackPhase.None;
        public AttackPhase CurrentPhase => Current is MeleeWeapon m ? m.Phase : AttackPhase.None;
        /// <summary>PlayerActions id of the running melee attack (0 = none)</summary>
        public int CurrentAttackAction => Current is MeleeWeapon m ? m.CurrentAction : PlayerActions.None;
        public bool IsDrawing => Current is RangedWeapon r && r.IsDrawing;
        /// <summary>0..1 bow draw (0 when not drawing)</summary>
        public float DrawProgress => Current is RangedWeapon r ? r.DrawProgress : 0f;
        public bool Aiming { get; private set; }
        /// <summary>weapon out and fighting / aiming (what the CombatMode parameter shows)</summary>
        public bool CombatMode { get; private set; }
        public float LastCombatTime { get; set; } = -99f;
        public WeaponAnimatorBridge Bridge => _bridge;
        public PlayerHierarchy Hierarchy => _ctx != null ? _ctx.hierarchy : null;

        public event Action<IWeapon> WeaponChanged;
        public event Action<AttackPhase> PhaseChanged;
        public event Action<IDamageable, HitInfo> TargetHit;

        WeaponContext _ctx; WeaponAnimatorBridge _bridge;
        InventorySystem _inv; PlayerEquipment _eq;
        ItemDefinition _item; WeaponData _data; GameObject _model;
        int _syncFrame = -1;

        void Awake()
        {
            _inv = GetComponent<InventorySystem>(); _eq = GetComponent<PlayerEquipment>();
            var drv = GetComponent<PlayerAnimationDriver>();
            _ctx = new WeaponContext
            {
                owner = gameObject, root = transform, controller = this, driver = drv,
                motor = GetComponent<PlayerMotor>(), survival = GetComponent<PlayerSurvival>(), inventory = _inv, equipment = _eq,
                hierarchy = gameObject.GetOrAdd<PlayerHierarchy>(),
                hitMask = ~LayerMask.GetMask("Player", "Ignore Raycast"),
                meleeMask = ~LayerMask.GetMask("Player"),
            };
            if (gameObject.layer != 0) { _ctx.hitMask &= ~(1 << gameObject.layer); _ctx.meleeMask &= ~(1 << gameObject.layer); }
        }

        void OnEnable()
        {
            _bridge = new WeaponAnimatorBridge(_ctx.driver, GetComponentInChildren<CharacterAnimationEvents>(), OnAnimEvent);
            _ctx.anim = _bridge;
        }

        void OnDisable()
        {
            Select(null, null);
            _bridge?.Dispose(); _bridge = null;
        }

        /// <summary>the data this item fights with: its WeaponData, the legacy bow numbers for a bow without data, else null</summary>
        public WeaponData ResolveData(ItemDefinition item)
        {
            if (!item) return null;
            if (item.weaponData) return item.weaponData;
            if (item.weapon == WeaponKind.Bow) return WeaponData.LegacyBow(item, legacyArrowSpeed, legacyDrawTime);
            return null;
        }

        /// <summary>true when the active item fights through this controller</summary>
        public bool Handles(ItemDefinition item) => ResolveData(item) != null;

        // ------------------------------------------------------------------ weapon selection
        /// <summary>follow the active item (once per frame; PlayerCombat calls it before routing the buttons)</summary>
        public void Sync()
        {
            if (_syncFrame == Time.frameCount) return;
            _syncFrame = Time.frameCount;
            var item = _inv ? _inv.ActiveItem : null;
            var data = ResolveData(item);
            if (item != _item || data != _data) Select(item, data);
            if (Current == null) return;
            var model = _eq && _eq.HeldItem == _item ? _eq.HeldObject : null;
            if (model != _model) { _model = model; Current.OnModelChanged(model); }
        }

        void Select(ItemDefinition item, WeaponData data)
        {
            if (Current != null)
            {
                Current.Unequip();
                if (logAttacks) Log("unequip " + (Current.Item ? Current.Item.id : "?"));
            }
            Current = null; _item = item; _data = data; _model = null;
            if (data != null && _bridge != null)
            {
                Current = data.IsRanged ? new RangedWeapon(data, item) : (IWeapon)new MeleeWeapon(data, item);
                Current.Equip(_ctx);
                if (Aiming) Current.OnAimChanged(true);
                _model = _eq && _eq.HeldItem == item ? _eq.HeldObject : null;
                Current.OnModelChanged(_model);
                if (logAttacks) Log($"equip {item.id} ({(data.IsRanged ? "ranged" : "melee")}, {data.name})");
            }
            _bridge?.SetWeapon(data);
            WeaponChanged?.Invoke(Current);
        }

        // ------------------------------------------------------------------ input (from PlayerCombat)
        /// <summary>attack buttons for this frame; aiming is PlayerCombat.Aiming</summary>
        public void HandleInput(bool pressed, bool held, bool aiming)
        {
            Sync();
            Current?.HandleInput(new WeaponInput(pressed, held, aiming));
        }

        /// <summary>aim toggled (PlayerCombat.SetAim): the bow raises / lowers</summary>
        public void SetAim(bool on)
        {
            if (Aiming == on) return;
            Aiming = on;
            Sync();
            Current?.OnAimChanged(on);
        }

        /// <summary>stop the running attack / draw and forget queued presses (dodge, death)</summary>
        public void CancelAttack() => Current?.Cancel();

        /// <summary>drop a press in progress without attacking (input gated by menus, build mode, interactions)</summary>
        public void ResetInput() => Current?.ResetInput();

        // ------------------------------------------------------------------ loop
        void Update()
        {
            Sync();
            if (Current != null)
            {
                if (_bridge.IsDead) Current.Cancel();
                else Current.Tick(Time.deltaTime);
            }
            bool fighting = Current != null && (Aiming || Current.Busy || Time.time - LastCombatTime < combatModeHold);
            CombatMode = fighting;
            _bridge.SetWeapon(_data);          // no-op unless it changed (or the Animator was not ready yet)
            _bridge.SetCombatMode(fighting);
        }

        void LateUpdate() { Current?.LateTick(); }

        void OnAnimEvent(string name, string param) { Current?.OnAnimEvent(name, param); }

        // ------------------------------------------------------------------ called by the weapons
        internal void RaisePhase(AttackPhase p) => PhaseChanged?.Invoke(p);
        internal void RaiseHit(IDamageable target, HitInfo hit) { LastCombatTime = Time.time; TargetHit?.Invoke(target, hit); }
        internal void Log(string s) => Debug.Log("[Weapon] " + s, this);
    }
}
