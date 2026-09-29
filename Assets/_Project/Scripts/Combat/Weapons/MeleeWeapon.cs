using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.VFX;

namespace PrimalFrontier.Combat.Weapons
{
    /// <summary>
    /// Spear / knife / sword / tools / torch. Tap = next step of the light chain (WeaponData.attacks), hold = heavy.
    /// A press while an attack runs is queued as the next step and keeps its own parameters (the old buffer overwrote
    /// the pending hit). Each attack goes Startup -> Active -> Recovery: the clip's OnAttackActive / OnAttackEnd events
    /// drive the hitbox; clips without them use the profile's normalized window of the attack state, so the hitbox is
    /// never live for the whole clip. The legacy OnAttackHit still lands the old sphere hit if the blade missed.
    /// Stamina is spent when the attack really starts; wear goes to the stack that attacked.
    /// </summary>
    public class MeleeWeapon : WeaponBase
    {
        const float QueueLifetime = 1.2f;       // like the old driver buffer: slow frames must not drop a press
        const float EnterTimeout = 0.3f;        // no Attack state after this: the controller has no clip, run on time
        const float FallbackClip = 0.9f;        // seconds of a time-driven attack (x 1 / attackSpeed)

        struct Pending { public bool valid, heavy, chain; public float time; }

        AttackPhase _phase;
        AttackProfile _profile; bool _heavy; int _nextStep; float _lastLightStart = -10f;
        Pending _queued;
        float _pressT = -1f; bool _heavyFired;
        float _startT; int _stateHash; bool _entered, _timeDriven;
        bool _usesEvents; int _activeFrame = -1; bool _legacyDone; bool _sawActive;
        ItemStack _stack; bool _worn;
        WeaponHitbox _blade, _sphere, _hitbox; WeaponTrail _trail;
        float _equipUntil = -1f;

        public MeleeWeapon(WeaponData data, ItemDefinition item) : base(data, item) { }

        public AttackPhase Phase => _phase;
        public override bool Busy => _phase != AttackPhase.None;
        /// <summary>PlayerActions id of the running attack (0 = none)</summary>
        public int CurrentAction => _phase != AttackPhase.None && _profile != null ? _profile.action : PlayerActions.None;
        public bool CurrentIsHeavy => _phase != AttackPhase.None && _heavy;
        public bool HasQueued => _queued.valid;
        public WeaponHitbox Hitbox => _hitbox ? _hitbox : (_blade ? _blade : _sphere);

        // ------------------------------------------------------------------ equip
        public override void Equip(WeaponContext ctx)
        {
            base.Equip(ctx);
            var origin = ctx.hierarchy ? ctx.hierarchy.AttackOrigin : null;
            if (origin)
            {
                _sphere = origin.gameObject.GetOrAdd<WeaponHitbox>();
                _sphere.SetupSphere(Data, ctx.root, ctx.meleeMask);
                _sphere.Hit += OnHit;
            }
            if (Data.equipAction != PlayerActions.None && !ctx.anim.IsBusy && ctx.anim.CurrentAction == PlayerActions.None)
            {
                ctx.anim.Play(Data.equipAction); _equipUntil = Time.time + Mathf.Max(0.1f, Data.equipTime);
            }
        }

        public override void Unequip()
        {
            Cancel();
            if (_equipUntil > 0f && Ctx.anim.CurrentAction == Data.equipAction) Ctx.anim.Stop();
            _equipUntil = -1f;
            if (_sphere) { _sphere.SetActive(false); _sphere.Hit -= OnHit; }
            DetachBlade();
            _sphere = null;
            base.Unequip();
        }

        public override void OnModelChanged(GameObject model)
        {
            if (_phase != AttackPhase.None) Cancel();
            DetachBlade();
            base.OnModelChanged(model);
            if (!model || Data.unarmed) return;                                     // fists: the held log / stone is not a blade
            var hb = model.GetOrAdd<WeaponHitbox>();
            if (!hb.SetupBlade(Data, Ctx.root, Ctx.meleeMask)) { Object.Destroy(hb); return; }   // no mesh: AttackOrigin sphere
            _blade = hb; _blade.Hit += OnHit;
            if (Data.trail) _trail = WeaponTrail.Attach(model.transform, hb.localTip, Data.trailColor, Data.trailMaterial);
        }

        void DetachBlade()
        {
            if (_blade) { _blade.SetActive(false); _blade.Hit -= OnHit; }
            if (_trail) _trail.Emit(false);
            _blade = null; _trail = null; _hitbox = null;
        }

        // ------------------------------------------------------------------ input
        public override void HandleInput(WeaponInput input)
        {
            if (input.heavyPressed && Data.HasHeavy) { _pressT = -1f; _heavyFired = true; Request(true); return; }   // HEAVY button (touch)
            if (input.pressed)
            {
                _pressT = Time.time; _heavyFired = false;
                if (!Data.HasHeavy) { Request(false); _pressT = -1f; return; }     // no heavy: strike on press
            }
            if (_pressT > 0f && !_heavyFired && input.held && Time.time - _pressT >= Data.heavyHoldTime) { _heavyFired = true; Request(true); }
            if (_pressT > 0f && !input.held) { if (!_heavyFired) Request(false); _pressT = -1f; }
        }

        void Request(bool heavy)
        {
            if (_phase != AttackPhase.None || Ctx.anim.IsBusy)
            {
                // one step waits; it keeps its own kind (light / heavy) and continues the chain
                if (!_queued.valid) { _queued = new Pending { valid = true, heavy = heavy, chain = _phase != AttackPhase.None, time = Time.time }; if (Logging) Log(heavy ? "queued heavy" : "queued light"); }
                return;
            }
            StartAttack(heavy, false);
        }

        public override void ResetInput() { _pressT = -1f; }

        public override void Cancel()
        {
            if (_phase != AttackPhase.None) SetPhase(AttackPhase.None);
            _queued.valid = false; _pressT = -1f;
        }

        // ------------------------------------------------------------------ attack
        void StartAttack(bool heavy, bool chain)
        {
            AttackProfile p; int step = 0;
            if (heavy && Data.HasHeavy) p = Data.heavyAttack;
            else
            {
                heavy = false;
                var chainArr = Data.attacks;
                if (chainArr == null || chainArr.Length == 0) return;
                bool cont = _nextStep > 0 && _nextStep < chainArr.Length && (chain || Time.time - _lastLightStart < Data.comboWindow);
                step = cont ? _nextStep : 0;
                p = chainArr[step];
                if (p == null || !p.IsValid) return;
            }
            float cost = Data.staminaCost * p.staminaMultiplier * (heavy ? Data.heavyStaminaMultiplier : 1f);
            if (!TrySpendStamina(cost, "Too tired to strike.")) return;

            if (heavy) _nextStep = 0;
            else { _nextStep = step + 1 >= Data.attacks.Length ? 0 : step + 1; _lastLightStart = Time.time; }
            _profile = p; _heavy = heavy;
            _startT = Time.time; _stateHash = 0; _entered = false; _timeDriven = false;
            _usesEvents = false; _activeFrame = -1; _legacyDone = false;
            _stack = ActiveStack; _worn = false;
            if (_equipUntil > 0f) _equipUntil = -1f;                                // the attack takes over the body

            float dmg = (heavy ? Data.heavyDamage : Data.damage) * p.damageMultiplier;
            bool strong = heavy || p.damageMultiplier > 1.1f;                       // combo finishers stagger like the heavy
            bool down = p.action == PlayerActions.SpearAttack2 || p.action == PlayerActions.SwordAttack3;
            _hitbox = _blade ? _blade : _sphere;
            if (_hitbox) _hitbox.BeginSwing(dmg, strong, down, heavy ? Data.HeavyReach : Data.reach, heavy ? Data.heavyKnockback : Data.knockback * p.damageMultiplier);
            _sawActive = false;
            if (Data.effortSfx != Audio.SfxId.None && (heavy || p.damageMultiplier > 1.3f)) Audio.SfxPlayer.Instance.Play(Data.effortSfx, Ctx.root.position + Vector3.up * 1.5f, 0.7f);
            Ctx.anim.Play(p.action);                                                // our queue decides, not the driver buffer
            MarkCombat();
            SetPhase(AttackPhase.Startup);
            if (Logging) Log($"start {(heavy ? "heavy" : "light " + (step + 1))} action {p.action} dmg {dmg:F1} stamina {cost:F1} hitbox {(_hitbox == null ? "none" : _hitbox.HasBlade ? "blade" : "sphere")}");
        }

        public override void Tick(float dt)
        {
            if (_equipUntil > 0f && Time.time >= _equipUntil)
            {
                _equipUntil = -1f;
                if (Ctx.anim.CurrentAction == Data.equipAction) Ctx.anim.Stop();
            }
            if (_phase != AttackPhase.None) UpdatePhase();
            // the queued step starts as soon as the body is free (same frame the previous one finished)
            if (_phase != AttackPhase.None || !_queued.valid) return;
            if (Time.time - _queued.time > QueueLifetime) { _queued.valid = false; return; }
            if (Ctx.anim.IsBusy || Ctx.anim.IsDead) return;
            var q = _queued; _queued.valid = false;
            StartAttack(q.heavy, q.chain);
        }

        void UpdatePhase()
        {
            float t = Time.time - _startT;
            if (!_entered)
            {
                if (Ctx.anim.TryGetEnteringAttack(out int h)) { _stateHash = h; _entered = true; }
                else if (t > EnterTimeout)
                {
                    _timeDriven = true; _entered = true; _startT = Time.time; t = 0f;          // the window runs from now
                    if (Logging) Log("no Attack state for action " + _profile.action + ": time-driven window");
                }
                else return;                                                        // still waiting for the clip to start
            }
            float n;
            if (_timeDriven) n = t / (FallbackClip / Mathf.Max(0.1f, Data.attackSpeed));
            else if (!Ctx.anim.TryGetStateTime(_stateHash, out n)) { Finish(); return; }     // the clip is gone
            if (_timeDriven && n >= 1f) { Finish(); return; }

            if (!_usesEvents)
            {
                if (_phase == AttackPhase.Startup && n >= _profile.activeStart) SetPhase(AttackPhase.Active);
                if (_phase == AttackPhase.Active && n >= _profile.activeEnd) SetPhase(AttackPhase.Recovery);
            }
            else if (_phase == AttackPhase.Active && n >= Mathf.Max(_profile.activeEnd, 0.9f)) SetPhase(AttackPhase.Recovery);   // OnAttackEnd missing
            // control comes back when the body blends out of the attack (same moment the old buffer fired)
            if (_phase == AttackPhase.Recovery && !_timeDriven && !Ctx.anim.IsBusy) Finish();
        }

        void Finish()
        {
            if (_phase == AttackPhase.Active) SetPhase(AttackPhase.Recovery);
            SetPhase(AttackPhase.None);
            if (_queued.valid) _queued.time = Time.time;                            // a press queued in a long swing waits from its end
        }

        void SetPhase(AttackPhase p)
        {
            if (p == _phase) return;
            var was = _phase; _phase = p;
            var hb = _hitbox;
            if (p == AttackPhase.Active)
            {
                _activeFrame = Time.frameCount;
                if (hb) hb.SetActive(true);
                if (_trail && hb == _blade) _trail.Emit(true);
                if (_profile != null && _profile.lunge > 0f && Ctx.motor) Ctx.motor.Burst(Ctx.root.forward * _profile.lunge, Mathf.Max(0.05f, _profile.lungeTime));
                // clips with OnAttackStart play the swing there; clips without it (knife, placeholders) and time-driven attacks here
                if ((_timeDriven || !_usesEvents) && Data.swingSfx != Audio.SfxId.None) Audio.SfxPlayer.Instance.Play(Data.swingSfx, Ctx.root.position + Vector3.up * 1.2f, 0.8f);
            }
            else if (was == AttackPhase.Active)
            {
                // Active opened and closed inside one frame (hitch, or both events on one frame): sweep once now
                if (hb && p == AttackPhase.Recovery && _activeFrame == Time.frameCount) hb.SweepNow();
                if (hb) hb.SetActive(false);
                if (_trail) _trail.Emit(false);
            }
            if (p == AttackPhase.None) { if (hb) hb.SetActive(false); if (_trail) _trail.Emit(false); _hitbox = null; }
            if (Ctx.controller) Ctx.controller.RaisePhase(p);
            if (Logging) Log("phase " + p);
        }

        // ------------------------------------------------------------------ events
        public override void OnAnimEvent(string name, string param)
        {
            if (_phase == AttackPhase.None || Time.time <= _startT) return;        // nothing running / an older clip's event
            switch (name)
            {
                case "OnAttackStart":
                    _usesEvents = true;
                    if (Data.swingSfx != Audio.SfxId.None) Audio.SfxPlayer.Instance.Play(Data.swingSfx, Ctx.root.position + Vector3.up * 1.2f, 0.8f);
                    break;
                case "OnAttackActive":
                    _usesEvents = true; _sawActive = true;
                    if (_phase == AttackPhase.Startup) SetPhase(AttackPhase.Active);
                    break;
                case "OnAttackEnd":
                    _usesEvents = true;
                    if (_phase == AttackPhase.Startup || _phase == AttackPhase.Active) SetPhase(AttackPhase.Recovery);
                    break;
                case "OnAttackHit":
                    if (!_usesEvents) { if (!_legacyDone) LegacyHit(); }
                    else if (!_sawActive && _phase == AttackPhase.Startup)
                    {
                        // clip with OnAttackStart / OnAttackHit / OnAttackEnd but no OnAttackActive: the contact frame opens the
                        // window (swept at once, live until OnAttackEnd), so the hit still comes from the animation event
                        SetPhase(AttackPhase.Active);
                        if (_hitbox) _hitbox.SweepNow();
                    }
                    break;
            }
        }

        /// <summary>old clips: the contact frame lands a hit if the blade window has not hit anything yet</summary>
        void LegacyHit()
        {
            _legacyDone = true;
            var hb = _hitbox; if (!hb || hb.HitCount > 0) return;
            if (hb.HasBlade) hb.SweepNow();
            if (hb.HitCount > 0) return;
            var origin = Ctx.hierarchy && Ctx.hierarchy.AttackOrigin ? Ctx.hierarchy.AttackOrigin.position : Ctx.root.position + Vector3.up * 1.1f;
            hb.SweepSphereFrom(origin, _heavy ? Data.HeavyReach : Data.reach);
            if (Logging) Log("legacy OnAttackHit -> " + hb.HitCount + " hit(s)");
        }

        void OnHit(IDamageable target, HitInfo hit, Collider c)
        {
            MarkCombat();
            Shake(_heavy ? 0.06f : 0.03f, 0.12f);
            if (!_worn && !Data.unarmed) { _worn = true; Wear(_stack, Data.durabilityCost * (_heavy ? 2f : 1f)); }  // once per swing (fists never wear the held item)
            var comp = target as Component;
            GameEvents.Raise(GameEventType.CreatureHit, comp ? comp.name : "creature", 1, hit.point);
            if (Ctx.controller) Ctx.controller.RaiseHit(target, hit);
            if (Logging) Log($"hit {(comp ? comp.name : "?")} dmg {hit.damage:F1} zone x{hit.zoneMultiplier:F2}");
        }

        bool Logging => Ctx != null && Ctx.controller && Ctx.controller.logAttacks;
        void Log(string s) => Ctx.controller.Log(s);
    }
}
