using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Audio;
using PrimalFrontier.Combat;
using PrimalFrontier.Combat.Weapons;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Survival;
using PrimalFrontier.VFX;

namespace PrimalFrontier.Player
{
    /// <summary>
    /// Primary / aim buttons with the active item. Items with WeaponData (and every bow) fight through the
    /// WeaponController (MeleeWeapon: light chain / heavy on hold / hitbox windows; RangedWeapon: aim, draw, release); so do
    /// the bare hands (empty hand, or an item with no weapon role: WeaponData WPN_bare_hands, punch 1-2-3 / heavy on hold,
    /// HitInfo.unarmed, see Documentation/BARE_HAND_COMBAT.md);
    /// this component keeps the gating, food / water / placeables, the spear throw (aim + attack), the dodge (V / pad
    /// north / touch button: short hop back with a brief invulnerable window), the block (hold the aim button with a melee
    /// weapon that has no throw: Sword_Block on the upper body, walk speed, no attacks until released, frontal hits reduced
    /// through PlayerHealth.IncomingHitFilter for stamina; out of stamina the guard breaks), and the legacy melee path for items
    /// without WeaponData: spear thrust (tap), combo stab (Spear_Attack_2), heavy thrust (hold), knife / tool slash
    /// (Knife_Attack), damage applied once on the clip's hit event.
    /// </summary>
    public class PlayerCombat : MonoBehaviour
    {
        public float heavyHoldTime = 0.35f;
        public float spearReach = 2.1f, heavyReach = 2.4f;
        public float throwSpeed = 22f, arrowSpeed = 38f;
        public float bowFullDraw = 0.9f;
        [Header("Combo / knife")]
        [Tooltip("a second tap this soon after the first thrust starts chains the overhead stab")] public float comboWindow = 1.0f;
        public float knifeReach = 1.35f;
        [Header("Dodge")]
        public float dodgeSpeed = 5.2f, dodgeTime = 0.32f, dodgeStamina = 14f, dodgeCooldown = 0.55f;
        [Tooltip("seconds after the start of the dodge during which hits miss")] public Vector2 dodgeInvulnerable = new Vector2(0.04f, 0.34f);
        [Header("Block (hold the aim button with a sword / knife / tool)")]
        [Tooltip("share of a blocked hit's damage the guard takes away")] [Range(0f, 1f)] public float blockReduction = 0.7f;
        [Tooltip("full angle of the guarded cone in front of the body (deg); 110 = 55 each side")] [Range(0f, 360f)] public float blockArc = 110f;
        [Tooltip("stamina per point of incoming damage (before the reduction) for a blocked hit")] public float blockStaminaPerDamage = 0.8f;
        [Tooltip("least stamina a blocked hit costs")] public float blockStaminaMin = 4f;
        [Tooltip("after the guard breaks (stamina could not pay for a hit), no block for this long (s)")] public float guardBreakLockout = 1.2f;

        PlayerInputReader _in; PlayerAnimationDriver _drv; PlayerMotor _motor; PlayerInteraction _pi; PlayerSurvival _sv;
        InventorySystem _inv; PlayerEquipment _eq; ThirdPersonCamera _cam; CharacterAnimationEvents _ev;
        float _pressT = -1f; bool _heavyFired;
        int _pendingAttack; ItemDefinition _attackItem; int _pendingAnim;
        float _lastThrust = -10f; int _comboStep; float _nextDodge;
        PlayerHealth _hp; WeaponController _wc;
        public bool Aiming { get; private set; }
        /// <summary>guarding with the melee weapon in hand (Sword_Block pose, walk speed, frontal hits reduced)</summary>
        public bool IsBlocking { get; private set; }
        /// <summary>Time.time until which the guard cannot come up again (it just broke)</summary>
        public float GuardBrokenUntil => _guardBrokenUntil;
        float _guardBrokenUntil = -99f;
        PlayerHealth.HitFilter _hitFilter;
        /// <summary>fighting right now (struck or was hit in the last few seconds): the camera tightens a little</summary>
        public bool InCombat => Time.time - Mathf.Max(_lastCombat, _wc ? _wc.LastCombatTime : -99f) < 4f;
        float _lastCombat = -99f;
        /// <summary>0..1 bow draw of the weapon in hand</summary>
        public float DrawProgress => _wc ? _wc.DrawProgress : 0f;
        /// <summary>the weapon owner (data-driven weapons and the bow)</summary>
        public WeaponController Weapons => _wc;
        /// <summary>Build mode takes over the buttons (set by BuildSystem)</summary>
        public System.Func<bool> PrimaryBlocked = () => false;

        void Awake()
        {
            _drv = GetComponent<PlayerAnimationDriver>(); _motor = GetComponent<PlayerMotor>(); _pi = GetComponent<PlayerInteraction>();
            _sv = GetComponent<PlayerSurvival>(); _inv = GetComponent<InventorySystem>(); _eq = GetComponent<PlayerEquipment>();
            _ev = GetComponentInChildren<CharacterAnimationEvents>(); _hp = GetComponent<PlayerHealth>();
            _wc = gameObject.GetOrAdd<WeaponController>();
            _wc.legacyArrowSpeed = arrowSpeed; _wc.legacyDrawTime = bowFullDraw;
            _hitFilter = FilterHit;
        }
        void OnEnable()
        {
            if (_ev) _ev.AnimationEventRaised += OnAnimEvent;
            if (_hp) _hp.IncomingHitFilter = _hitFilter;
        }
        void OnDisable()
        {
            if (_ev) _ev.AnimationEventRaised -= OnAnimEvent;
            if (_hp && _hp.IncomingHitFilter == _hitFilter) _hp.IncomingHitFilter = null;
            EndBlock();
        }
        void Start() { _in = PlayerInputReader.Instance; if (Camera.main) _cam = Camera.main.GetComponent<ThirdPersonCamera>(); }

        void Update()
        {
            if (_in == null) _in = PlayerInputReader.Instance;
            if (_in == null || _drv == null || _drv.IsDead || (_pi && (_pi.Suspended || _pi.InAction)) || PrimaryBlocked()) { SetAim(false); EndBlock(); _pressT = -1f; if (_wc) _wc.ResetInput(); return; }
            var item = _inv ? _inv.ActiveItem : null;
            var weapon = item ? item.weapon : WeaponKind.None;
            if (_in.DodgePressed) TryDodge();

            // data-driven weapons, every bow and the bare hands (empty hand or an item with no weapon role): the
            // WeaponController (the spear throw stays here); items the attack button uses (food, water, camp items, items a
            // use handler claims) go to the use path below
            var data = _wc ? _wc.ResolveData(item) : null;
            bool usable = item && (item.IsFood || item.IsWaterContainer || item.IsPlaceable || PlayerInteraction.HasUseHandler(item));
            if (data != null && !usable)
            {
                bool canThrow = data.throwable;
                SetAim(_in.Aim && (canThrow || data.IsRanged));
                UpdateBlock(_in.BlockHeld && CanBlockWith(data));
                if (IsBlocking) { _wc.ResetInput(); _pressT = -1f; return; }      // no attacks behind the guard: release to strike
                if (canThrow && Aiming && _in.AttackPressed) { _wc.ResetInput(); Throw(item); _pressT = -1f; return; }
                _wc.HandleInput(_in.AttackPressed, _in.AttackHeld, Aiming, _in.HeavyPressed);
                return;
            }
            EndBlock();

            // legacy path (items without WeaponData): aiming = spear throw
            bool wantAim = _in.Aim && weapon == WeaponKind.Spear;
            SetAim(wantAim);

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
            if (item.IsFood || item.IsWaterContainer || PlayerInteraction.HasUseHandler(item)) { _pi.UseActiveConsumable(); return; }
            if (item.IsPlaceable) { Building.BuildSystem.Instance?.Begin(item); return; }
            if (item.damage > 0f) Melee(item, false);                     // knife / stone tools slash
        }

        // ------------------------------------------------------------------ dodge
        public bool TryDodge()
        {
            if (Time.time < _nextDodge || _drv.IsDead || (_motor && !_motor.IsGrounded)) return false;
            if (_drv.IsBusy && !_drv.IsAttackingState) return false;                   // not out of eating / crafting
            if (_sv && _sv.Stamina < dodgeStamina * 0.6f) { PlayerInteraction.Notify("Too tired to dodge."); return false; }
            _sv?.UseStamina(dodgeStamina);
            _nextDodge = Time.time + dodgeCooldown;
            Vector3 dir = -transform.forward;                                          // hop back; sideways with a stick push
            var mv = _in.Move;
            if (mv.sqrMagnitude > 0.2f && _motor && _motor.CameraTransform)
            {
                Vector3 f = Vector3.ProjectOnPlane(_motor.CameraTransform.forward, Vector3.up).normalized, r = Vector3.Cross(Vector3.up, f);
                Vector3 want = (f * mv.y + r * mv.x).normalized;
                if (Vector3.Dot(want, transform.forward) < 0.5f) dir = want;           // pushing forward still hops back (keeps facing the threat)
            }
            _pendingAttack = 0;
            if (_wc) _wc.CancelAttack();
            _drv.PlayAction(PlayerActions.Dodge);
            _motor?.Burst(dir * dodgeSpeed, dodgeTime);
            if (_hp) _hp.InvulnerableUntil = Time.time + dodgeInvulnerable.y;
            VfxPool.Instance.Play(VfxId.LandDust, transform.position, Vector3.up);
            SfxPlayer.Instance.Play(SfxId.FootDirt, transform.position, 0.9f);
            GameEvents.Raise(GameEventType.PlayerDodged, "dodge", 1, transform.position);
            return true;
        }

        void SetAim(bool on)
        {
            if (Aiming == on) return;
            Aiming = on;
            ApplyMoveMode();
            if (_cam) _cam.Aiming = on;
            if (_wc) _wc.SetAim(on);                              // the bow raises / lowers (RangedWeapon)
        }

        /// <summary>aiming and blocking both walk and face the camera (strafe locomotion); sprint is off</summary>
        void ApplyMoveMode() { if (_motor) _motor.AimMode = Aiming || IsBlocking; }

        // ------------------------------------------------------------------ block
        /// <summary>melee weapons without a throw block (sword, knife, axe, pick ...); the spear aims / throws, the bow aims</summary>
        public static bool CanBlockWith(WeaponData data) => data != null && !data.unarmed && !data.IsRanged && !data.throwable && data.attacks != null && data.attacks.Length > 0;

        void UpdateBlock(bool want)
        {
            if (IsBlocking)
            {
                // something else took the arms (dodge, an action, the equip clip of a new weapon): the guard is down
                if (_drv.CurrentAction != PlayerActions.SwordBlock) { IsBlocking = false; ApplyMoveMode(); }
                else if (!want) EndBlock();
                else { if (_wc) _wc.LastCombatTime = Time.time; return; }        // keeps CombatMode (Sword_Idle after the guard)
            }
            if (!want || Time.time < _guardBrokenUntil || !CanStartBlock()) return;
            _drv.PlayAction(PlayerActions.SwordBlock);                                        // upper body, loops until StopAction
            if (_drv.CurrentAction != PlayerActions.SwordBlock) return;                       // no Animator: no guard
            IsBlocking = true;
            _pressT = -1f;
            if (_wc) { _wc.CancelAttack(); _wc.ResetInput(); _wc.LastCombatTime = Time.time; }   // nothing running here: drops a queued press
            ApplyMoveMode();
        }

        /// <summary>the guard comes up only when the body is free: no swing, no full-body action, no upper-body clip (equip, bow)</summary>
        bool CanStartBlock()
        {
            if (_drv.IsBusy || _drv.CurrentAction != PlayerActions.None) return false;
            if (_wc && _wc.IsAttacking) return false;
            return !_motor || _motor.IsGrounded;
        }

        void EndBlock()
        {
            if (!IsBlocking) return;
            IsBlocking = false;
            if (_drv && _drv.CurrentAction == PlayerActions.SwordBlock) _drv.StopAction();
            ApplyMoveMode();
        }

        /// <summary>
        /// PlayerHealth.IncomingHitFilter: a hit from inside the guarded cone loses blockReduction of its damage for stamina
        /// (blockStaminaPerDamage x damage, at least blockStaminaMin). When the stamina cannot pay, the guard breaks: only
        /// the paid share is reduced, the hit staggers (heavy -> Hurt_Heavy) and the guard stays down for guardBreakLockout.
        /// A heavy hit from outside the cone knocks the guard down too. Returns true when the guard held.
        /// </summary>
        bool FilterHit(ref float amount, Vector3 source, ref bool heavy, ref float bleedSeconds)
        {
            if (!IsBlocking || amount <= 0f) return false;
            Vector3 to = source - transform.position; to.y = 0f;
            bool front = to.sqrMagnitude > 1e-4f && Vector3.Angle(transform.forward, to) <= blockArc * 0.5f;
            if (!front)
            {
                if (heavy) EndBlock();                                   // full-body stagger: the arms must not stay in the guard
                return false;
            }
            Vector3 dir = to.normalized;
            float cost = Mathf.Max(blockStaminaMin, amount * blockStaminaPerDamage);
            float paid = 1f;
            if (_sv)
            {
                float have = _sv.Stamina;
                if (have >= cost) _sv.UseStamina(cost);
                else { paid = cost > 0f ? Mathf.Clamp01(have / cost) : 1f; _sv.UseStamina(have); }
            }
            amount *= 1f - blockReduction * paid;
            bleedSeconds *= 1f - paid;
            _lastCombat = Time.time;
            if (_wc) _wc.LastCombatTime = Time.time;
            Vector3 at = GuardPoint(dir);
            if (paid < 1f)
            {
                // guard break: the rest of the hit lands, short stagger, the guard stays down for a moment
                heavy = true;
                EndBlock();
                _guardBrokenUntil = Time.time + guardBreakLockout;
                VfxPool.Instance.Play(VfxId.HitDust, at, dir);
                SfxPlayer.Instance.Play(SfxId.WoodBreak, at, 0.9f);
                PlayerInteraction.Notify("Guard broken!");
                return false;
            }
            heavy = false;                                               // a held guard never staggers
            VfxPool.Instance.Play(VfxId.HitDust, at, dir);
            VfxPool.Instance.Play(VfxId.CraftSparks, at, dir, null, 0.6f);
            SfxPlayer.Instance.Play(SfxId.HitHeavy, at, 0.8f);
            if (_cam) _cam.AddShake(0.04f, 0.12f);
            return true;
        }

        /// <summary>where the blow meets the guard: in front of the chest toward the attacker</summary>
        Vector3 GuardPoint(Vector3 dir)
        {
            var h = _wc ? _wc.Hierarchy : null;
            Vector3 o = h && h.AttackOrigin ? h.AttackOrigin.position : transform.position + Vector3.up * 1.1f;
            return o + dir * 0.35f + Vector3.up * 0.1f;
        }

        // ------------------------------------------------------------------ melee
        /// <summary>which clip a strike uses: spear thrust / combo stab / heavy lunge, or the one-hand slash for knives and tools</summary>
        int AttackAnim(ItemDefinition item, bool heavy)
        {
            if (item.weapon != WeaponKind.Spear) return PlayerActions.KnifeAttack;
            if (heavy) { _comboStep = 0; return PlayerActions.AttackSpearHeavy; }
            bool chain = _comboStep == 1 && Time.time - _lastThrust < comboWindow;
            _comboStep = chain ? 2 : 1; _lastThrust = Time.time;
            return chain ? PlayerActions.SpearAttack2 : PlayerActions.AttackSpear;
        }

        void Melee(ItemDefinition item, bool heavy)
        {
            float cost = item.staminaCost * (heavy ? 1.8f : 1f);
            if (_sv && _sv.Stamina < cost * 0.5f) { PlayerInteraction.Notify("Too tired to strike."); return; }
            int anim = AttackAnim(item, heavy);
            _sv?.UseStamina(cost);
            _pendingAttack = heavy ? 2 : 1; _attackItem = item; _pendingAnim = anim; _lastCombat = Time.time;
            _drv.Attack(anim);                                   // buffered by the driver while the previous swing finishes
        }

        void OnAnimEvent(string fn, string param)
        {
            if (fn == "OnAttackHit" && _pendingAttack != 0) { ResolveMelee(_attackItem, _pendingAttack == 2); _pendingAttack = 0; }
        }

        void ResolveMelee(ItemDefinition item, bool heavy)
        {
            if (item == null) return;
            float reach = heavy ? heavyReach : spearReach;
            if (item.weapon != WeaponKind.Spear) reach = knifeReach;
            bool combo = _pendingAnim == PlayerActions.SpearAttack2;
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
            float dmg = (heavy ? item.heavyDamage : item.damage * (combo ? 1.25f : 1f)) * mult * WeaponHitbox.AttackerMultiplier(transform);   // arm injury
            best.TakeHit(new HitInfo { damage = dmg, point = pt, direction = combo ? Vector3.down * 0.5f + fwd : fwd, attacker = gameObject, weapon = item.weapon, heavy = heavy || combo, zoneMultiplier = mult });
            VfxPool.Instance.Play(VfxId.SpearImpact, pt, -fwd);
            SfxPlayer.Instance.Play(SfxId.SpearImpact, pt);
            if (_cam) _cam.AddShake(heavy ? 0.06f : 0.03f, 0.12f);
            if (item.HasDurability) _inv.WearActive(heavy ? 2f : 1f);      // a break: InventorySystem.ToolBroke (sound, puff, note)
            GameEvents.Raise(GameEventType.CreatureHit, (best as Component) ? ((Component)best).name : "creature", 1, pt);
        }

        // ------------------------------------------------------------------ throw
        /// <summary>throw the item in hand (spear): WeaponData throw numbers when it has them, else the legacy ones</summary>
        void Throw(ItemDefinition item)
        {
            if (_drv.IsBusy) return;
            var data = item.weaponData;
            float cost = (data ? data.staminaCost : item.staminaCost) * 1.5f;
            if (_sv && !_sv.UseStamina(cost)) { PlayerInteraction.Notify("Too tired to throw."); return; }
            float speed = data && data.throwable ? data.throwSpeed : throwSpeed;
            float damage = data ? data.heavyDamage * data.throwDamageMultiplier : item.heavyDamage * 1.2f;
            var kind = data ? data.kind : WeaponKind.Spear;
            int slot = _inv.ActiveSlot;
            _pi.DoOneShot(PlayerActions.ThrowSpear, "OnThrowRelease", () =>
            {
                var stack = _inv.TakeFromSlot(slot, 1); if (stack == null) return;
                Vector3 dir = AimDirection();
                Vector3 from = transform.position + Vector3.up * 1.6f + transform.right * 0.2f + dir * 0.6f;
                Projectile.Launch(stack, from, dir * speed + Vector3.up * 1.2f,
                    new HitInfo { damage = damage, attacker = gameObject, weapon = kind, heavy = true }, gameObject, 1f, false);
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
    }
}
