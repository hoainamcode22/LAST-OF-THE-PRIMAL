using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Audio;
using PrimalFrontier.Combat;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Survival;
using PrimalFrontier.VFX;

namespace PrimalFrontier.Player
{
    /// <summary>
    /// Primary / aim buttons with the active item: spear thrust (tap), heavy thrust (hold), throw (aim + attack),
    /// bow (aim, hold to draw, release to shoot), melee with stone tools, eat / drink / place otherwise.
    /// Damage is applied on the clip's hit event; stamina, timing and distance matter.
    /// </summary>
    public class PlayerCombat : MonoBehaviour
    {
        public float heavyHoldTime = 0.35f;
        public float spearReach = 2.1f, heavyReach = 2.4f;
        public float throwSpeed = 22f, arrowSpeed = 38f;
        public float bowFullDraw = 0.9f;

        PlayerInputReader _in; PlayerAnimationDriver _drv; PlayerMotor _motor; PlayerInteraction _pi; PlayerSurvival _sv;
        InventorySystem _inv; PlayerEquipment _eq; ThirdPersonCamera _cam; CharacterAnimationEvents _ev;
        float _pressT = -1f; bool _heavyFired; float _drawStart = -1f; bool _aimingBow;
        int _pendingAttack; ItemDefinition _attackItem;
        public bool Aiming { get; private set; }
        public float DrawProgress => _drawStart < 0 ? 0f : Mathf.Clamp01((Time.time - _drawStart) / bowFullDraw);
        /// <summary>Build mode takes over the buttons (set by BuildSystem)</summary>
        public System.Func<bool> PrimaryBlocked = () => false;

        void Awake()
        {
            _drv = GetComponent<PlayerAnimationDriver>(); _motor = GetComponent<PlayerMotor>(); _pi = GetComponent<PlayerInteraction>();
            _sv = GetComponent<PlayerSurvival>(); _inv = GetComponent<InventorySystem>(); _eq = GetComponent<PlayerEquipment>();
            _ev = GetComponentInChildren<CharacterAnimationEvents>();
        }
        void OnEnable() { if (_ev) _ev.AnimationEventRaised += OnAnimEvent; }
        void OnDisable() { if (_ev) _ev.AnimationEventRaised -= OnAnimEvent; }
        void Start() { _in = PlayerInputReader.Instance; if (Camera.main) _cam = Camera.main.GetComponent<ThirdPersonCamera>(); }

        void Update()
        {
            if (_in == null) _in = PlayerInputReader.Instance;
            if (_in == null || _drv == null || _drv.IsDead || (_pi && (_pi.Suspended || _pi.InAction)) || PrimaryBlocked()) { SetAim(false); _pressT = -1f; return; }
            var item = _inv ? _inv.ActiveItem : null;
            var weapon = item ? item.weapon : WeaponKind.None;

            // aiming: spear (to throw) and bow
            bool wantAim = _in.Aim && (weapon == WeaponKind.Spear || weapon == WeaponKind.Bow);
            SetAim(wantAim);
            if (weapon == WeaponKind.Bow) { UpdateBow(item); return; }

            if (_in.AttackPressed) { _pressT = Time.time; _heavyFired = false; }
            if (weapon == WeaponKind.Spear)
            {
                if (Aiming && _in.AttackPressed) { Throw(item); _pressT = -1f; return; }
                if (_pressT > 0 && !_heavyFired && _in.AttackHeld && Time.time - _pressT >= heavyHoldTime) { _heavyFired = true; Melee(item, true); }
                if (_pressT > 0 && !_in.AttackHeld) { if (!_heavyFired) Melee(item, false); _pressT = -1f; }
                return;
            }
            if (!_in.AttackPressed) return;
            _pressT = -1f;
            if (item == null) return;
            if (item.IsFood || item.IsWaterContainer) { _pi.UseActiveConsumable(); return; }
            if (item.IsPlaceable) { Building.BuildSystem.Instance?.Begin(item); return; }
            if (item.damage > 0f) Melee(item, false);                     // stone tools strike (weak)
        }

        void SetAim(bool on)
        {
            if (Aiming == on) return;
            Aiming = on;
            if (_motor) _motor.AimMode = on;
            if (_cam) _cam.Aiming = on;
            var item = _inv ? _inv.ActiveItem : null;
            if (on && item && item.weapon == WeaponKind.Bow) { _drv.PlayAction(PlayerActions.BowAim); _aimingBow = true; }
            else if (!on && _aimingBow) { _drv.StopAction(); _aimingBow = false; _drawStart = -1f; }
        }

        // ------------------------------------------------------------------ melee
        void Melee(ItemDefinition item, bool heavy)
        {
            if (_drv.IsBusy) { _drv.Attack(heavy ? PlayerActions.AttackSpearHeavy : PlayerActions.AttackSpear); _pendingAttack = heavy ? 2 : 1; _attackItem = item; return; }
            float cost = item.staminaCost * (heavy ? 1.8f : 1f);
            if (_sv && _sv.Stamina < cost * 0.5f) { PlayerInteraction.Notify("Too tired to strike."); return; }
            _sv?.UseStamina(cost);
            _pendingAttack = heavy ? 2 : 1; _attackItem = item;
            _drv.Attack(heavy ? PlayerActions.AttackSpearHeavy : PlayerActions.AttackSpear);
        }

        void OnAnimEvent(string fn, string param)
        {
            if (fn == "OnAttackHit" && _pendingAttack != 0) { ResolveMelee(_attackItem, _pendingAttack == 2); _pendingAttack = 0; }
        }

        void ResolveMelee(ItemDefinition item, bool heavy)
        {
            if (item == null) return;
            float reach = heavy ? heavyReach : spearReach;
            if (item.weapon != WeaponKind.Spear) reach = 1.5f;
            Vector3 origin = transform.position + Vector3.up * 1.1f;
            Vector3 fwd = transform.forward;
            var hits = Physics.OverlapSphere(origin + fwd * reach * 0.6f, reach * 0.55f, ~LayerMask.GetMask("Player"), QueryTriggerInteraction.Collide);
            IDamageable best = null; Collider bestC = null; float bestD = float.MaxValue;
            foreach (var c in hits)
            {
                var d = c.GetComponentInParent<IDamageable>(); if (d == null || !d.IsAlive) continue;
                float dist = Vector3.Distance(origin, c.ClosestPoint(origin));
                if (dist < bestD) { bestD = dist; best = d; bestC = c; }
            }
            if (best == null) return;
            var zone = bestC.GetComponent<HitZone>();
            float mult = zone ? zone.DamageMultiplier : 1f;
            Vector3 pt = bestC.ClosestPoint(origin + fwd * 0.5f);
            best.TakeHit(new HitInfo { damage = (heavy ? item.heavyDamage : item.damage) * mult, point = pt, direction = fwd, attacker = gameObject, weapon = item.weapon, heavy = heavy, zoneMultiplier = mult });
            VfxPool.Instance.Play(VfxId.SpearImpact, pt, -fwd);
            SfxPlayer.Instance.Play(SfxId.SpearImpact, pt);
            if (_cam) _cam.AddShake(heavy ? 0.06f : 0.03f, 0.12f);
            if (item.HasDurability && _inv.WearActive(heavy ? 2f : 1f)) PlayerInteraction.Notify(item.displayName + " broke!");
            GameEvents.Raise(GameEventType.CreatureHit, (best as Component) ? ((Component)best).name : "creature", 1, pt);
        }

        // ------------------------------------------------------------------ throw
        void Throw(ItemDefinition item)
        {
            if (_drv.IsBusy) return;
            if (_sv && !_sv.UseStamina(item.staminaCost * 1.5f)) { PlayerInteraction.Notify("Too tired to throw."); return; }
            int slot = _inv.ActiveSlot;
            _pi.DoOneShot(PlayerActions.ThrowSpear, "OnThrowRelease", () =>
            {
                var stack = _inv.TakeFromSlot(slot, 1); if (stack == null) return;
                Vector3 dir = AimDirection();
                Vector3 from = transform.position + Vector3.up * 1.6f + transform.right * 0.2f + dir * 0.6f;
                Projectile.Launch(stack, from, dir * throwSpeed + Vector3.up * 1.2f,
                    new HitInfo { damage = item.heavyDamage * 1.2f, attacker = gameObject, weapon = WeaponKind.Spear, heavy = true }, gameObject, 1f, false);
            }, null, 1.0f);
        }

        Vector3 AimDirection()
        {
            var cam = Camera.main;
            if (!cam) return transform.forward;
            var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f));
            Vector3 target = Physics.Raycast(ray, out var h, 120f, ~LayerMask.GetMask("Player"), QueryTriggerInteraction.Ignore) ? h.point : ray.origin + ray.direction * 60f;
            return (target - (transform.position + Vector3.up * 1.6f)).normalized;
        }

        // ------------------------------------------------------------------ bow
        void UpdateBow(ItemDefinition bow)
        {
            if (!Aiming)
            {
                if (_in.AttackPressed) PlayerInteraction.Notify("Hold right mouse button to aim the bow.");
                return;
            }
            var ammo = bow.ammo;
            if (_in.AttackPressed)
            {
                if (ammo && !_inv.Has(ammo)) { PlayerInteraction.Notify("No arrows."); return; }
                _drawStart = Time.time; _drv.PlayAction(PlayerActions.BowDraw);
            }
            else if (_drawStart > 0 && !_in.AttackHeld)
            {
                float k = DrawProgress; _drawStart = -1f;
                _drv.PlayAction(PlayerActions.BowRelease);
                if (k < 0.25f) { _drv.PlayAction(PlayerActions.BowAim); return; }          // let go too early: no shot
                if (ammo && !_inv.Remove(ammo, 1)) return;
                _sv?.UseStamina(bow.staminaCost);
                Vector3 dir = AimDirection();
                Vector3 from = transform.position + Vector3.up * 1.55f + dir * 0.7f;
                var stack = new ItemStack(ammo, 1);
                Projectile.Launch(stack, from, dir * arrowSpeed * Mathf.Lerp(0.5f, 1f, k),
                    new HitInfo { damage = bow.damage * Mathf.Lerp(0.4f, 1f, k), attacker = gameObject, weapon = WeaponKind.Bow }, gameObject, 0.6f, true);
                if (bow.HasDurability && _inv.WearActive(1f)) PlayerInteraction.Notify(bow.displayName + " broke!");
                Invoke(nameof(BackToAim), 0.45f);
            }
        }
        void BackToAim() { if (Aiming && _inv.ActiveItem && _inv.ActiveItem.weapon == WeaponKind.Bow) _drv.PlayAction(PlayerActions.BowAim); }
    }
}
