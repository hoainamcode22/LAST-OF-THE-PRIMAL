using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Core;
using PrimalFrontier.Survival;
using PrimalFrontier.World;

namespace PrimalFrontier.Player
{
    /// <summary>
    /// Climbing state: approach a Climbable, press E -> Climb_Start (the body is carried onto the trunk), hang
    /// (Climb_Idle), W / S climb up and down (Climb_Up / Climb_Down, the body follows the trunk at the clip speed),
    /// E picks fruit in reach (Harvest_Fruit), Space / C lets go (Climb_End, the body is set down beside the trunk).
    /// Hands are placed on the trunk surface by PlayerIK. Getting hurt or running out of stamina makes you fall off.
    /// The character controller and the motor are off while climbing; everything is restored on the way down.
    /// </summary>
    [RequireComponent(typeof(PlayerMotor))]
    public class PlayerClimb : MonoBehaviour
    {
        public enum Phase { None, Starting, Hanging, Harvesting, Ending }
        public float attachTime = 0.75f, detachTime = 0.9f;
        [Tooltip("distance from the trunk surface to the player's pivot")] public float bodyOffset = 0.3f;
        public float startHeight = 0.35f;
        [Tooltip("hands above the feet (for reaching fruit)")] public float reachHeight = 2.0f;
        public float harvestReach = 1.25f;

        public Phase State { get; private set; }
        public bool IsClimbing => State != Phase.None;
        public Climbable Current { get; private set; }
        /// <summary>feet height above the climb start</summary>
        public float Height => _h;

        PlayerMotor _motor; CharacterController _cc; PlayerAnimationDriver _drv; PlayerInteraction _pi; PlayerHealth _hp; PlayerSurvival _sv;
        Animator _a; CharacterAnimationEvents _ev; PlayerIK _ik;
        float _h, _t; Vector3 _from, _to, _away; Quaternion _fromR, _toR; FruitCluster _harvest; bool _harvested; string _anim;

        void Awake()
        {
            _motor = GetComponent<PlayerMotor>(); _cc = GetComponent<CharacterController>(); _drv = GetComponent<PlayerAnimationDriver>();
            _pi = GetComponent<PlayerInteraction>(); _hp = GetComponent<PlayerHealth>(); _sv = GetComponent<PlayerSurvival>();
            _a = _drv ? _drv.animator : GetComponentInChildren<Animator>();
            _ev = GetComponentInChildren<CharacterAnimationEvents>();
        }
        void OnEnable() { if (_ev) _ev.AnimationEventRaised += OnAnimEvent; if (_hp) _hp.Damaged += OnDamaged; }
        void OnDisable() { if (_ev) _ev.AnimationEventRaised -= OnAnimEvent; if (_hp) _hp.Damaged -= OnDamaged; }

        public bool Begin(Climbable c)
        {
            if (IsClimbing || c == null || (_drv && (_drv.IsBusy || _drv.IsDead))) return false;
            Current = c; State = Phase.Starting; _t = 0f; _h = startHeight;
            if (!_ik && _a) _ik = _a.GetComponent<PlayerIK>();
            _motor.Stop(); _motor.enabled = false; if (_cc) _cc.enabled = false;
            if (_pi) { _pi.Suspended = true; _pi.ExternalPrompt = ClimbPrompt; }
            _away = transform.position - c.Bottom; _away.y = 0f;
            _away = _away.sqrMagnitude > 0.01f ? _away.normalized : -transform.forward;
            _from = transform.position; _fromR = transform.rotation;
            Pose(_h, out _to, out _toR);
            Play("Climb_Start", 0.12f);
            if (_ik) _ik.Climb = c;
            GameEvents.Raise(GameEventType.ClimbStarted, c.name, 1, c.Bottom);
            return true;
        }

        /// <summary>let go: drop = true falls straight down (hurt, out of stamina), false climbs down the last bit</summary>
        public void End(bool drop = false)
        {
            if (!IsClimbing || State == Phase.Ending) return;
            State = Phase.Ending; _t = 0f;
            _from = transform.position; _fromR = transform.rotation;
            Vector3 land = Current.Bottom + _away * (Current.trunkRadius + bodyOffset + (drop ? 0.2f : 0.45f));
            if (Physics.Raycast(land + Vector3.up * 3f, Vector3.down, out var hit, 8f, ~(1 << gameObject.layer), QueryTriggerInteraction.Ignore)) land.y = hit.point.y;
            _to = land; _toR = Quaternion.LookRotation(-_away, Vector3.up);
            Play("Climb_End", 0.12f);
        }

        void Finish()
        {
            if (_cc) _cc.enabled = true;
            _motor.enabled = true; _motor.Warp(_to, _toR);
            if (_pi) { _pi.Suspended = false; _pi.ExternalPrompt = null; }
            if (_ik) { _ik.Climb = null; _ik.ReachTarget = null; }
            State = Phase.None; Current = null; _harvest = null; _anim = null;
        }

        void Pose(float h, out Vector3 pos, out Quaternion rot)
        {
            Vector3 axis = Current.AxisAt(h);
            pos = axis + _away * (Current.trunkRadius + bodyOffset); pos.y = Current.Bottom.y + h;
            rot = Quaternion.LookRotation(-_away, Vector3.up);
        }

        void Update()
        {
            if (!IsClimbing) return;
            if (Current == null || (_hp && _hp.IsDead)) { if (Current == null) { _to = transform.position; _toR = transform.rotation; } Finish(); return; }
            float dt = Time.deltaTime;
            var input = PlayerInputReader.Instance;
            switch (State)
            {
                case Phase.Starting:
                {
                    _t += dt / attachTime; float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t));
                    transform.SetPositionAndRotation(Vector3.Lerp(_from, _to, k), Quaternion.Slerp(_fromR, _toR, k));
                    if (_t >= 1f) { State = Phase.Hanging; Play("Climb_Idle", 0.2f); }
                    break;
                }
                case Phase.Hanging:
                {
                    float v = input ? input.Move.y : 0f;
                    float top = Mathf.Max(startHeight, Current.Length);
                    if (v > 0.25f && _h < top)
                    {
                        if (_sv && !_sv.UseStamina(Current.staminaPerSecond * dt)) { PlayerInteraction.Notify("Your arms give out."); End(true); break; }
                        _h = Mathf.Min(top, _h + Current.climbSpeed * v * dt); Play("Climb_Up", 0.2f);
                    }
                    else if (v < -0.25f)
                    {
                        if (_h <= startHeight + 0.01f) { End(false); break; }
                        _h = Mathf.Max(startHeight, _h + Current.descendSpeed * v * dt); Play("Climb_Down", 0.2f);
                    }
                    else Play("Climb_Idle", 0.25f);
                    Pose(_h, out var p, out var r);
                    transform.SetPositionAndRotation(Vector3.Lerp(transform.position, p, 1f - Mathf.Exp(-14f * dt)), Quaternion.Slerp(transform.rotation, r, 1f - Mathf.Exp(-10f * dt)));
                    if (input && (input.JumpPressed || input.CrouchPressed || input.DodgePressed)) { End(true); break; }
                    if (input && input.InteractPressed)
                    {
                        var f = FruitInReach();
                        if (f) { _harvest = f; _harvested = false; _t = 0f; State = Phase.Harvesting; Play("Harvest_Fruit", 0.15f); if (_ik) _ik.ReachTarget = f.transform; }
                    }
                    break;
                }
                case Phase.Harvesting:
                    _t += dt;
                    if (_t > 1.5f) { if (!_harvested && _harvest) _harvest.Harvest(_pi ? _pi.Inventory : null); _harvest = null; if (_ik) _ik.ReachTarget = null; State = Phase.Hanging; Play("Climb_Idle", 0.25f); }
                    break;
                case Phase.Ending:
                {
                    _t += dt / detachTime;
                    float k = Mathf.Clamp01(_t);
                    // fall with gravity feel for the first half, then settle
                    Vector3 p = Vector3.Lerp(_from, _to, k * k); p.x = Mathf.Lerp(_from.x, _to.x, Mathf.SmoothStep(0, 1, k)); p.z = Mathf.Lerp(_from.z, _to.z, Mathf.SmoothStep(0, 1, k));
                    transform.SetPositionAndRotation(p, Quaternion.Slerp(_fromR, _toR, k));
                    if (_t >= 1f) Finish();
                    break;
                }
            }
        }

        FruitCluster FruitInReach()
        {
            Vector3 hands = transform.position + Vector3.up * reachHeight;
            FruitCluster best = null; float bd = harvestReach;
            foreach (var f in Current.Fruits)
            {
                if (!f.Ripe) continue;
                float d = Vector3.Distance(hands, f.transform.position);
                if (d < bd) { bd = d; best = f; }
            }
            return best;
        }

        (string, string) ClimbPrompt()
        {
            if (State == Phase.Hanging && FruitInReach()) return ("Pick the fruit", "W / S climb   Space let go");
            if (State == Phase.Hanging) return ("Climbing", Current && Current.Fruits.Length > 0 ? "Climb up to the fruit   Space let go" : "W / S climb   Space let go");
            return (null, null);
        }

        void OnAnimEvent(string fn, string param)
        {
            if (fn == "OnHarvest" && State == Phase.Harvesting && _harvest && !_harvested) { _harvested = _harvest.Harvest(_pi ? _pi.Inventory : null) || true; }
        }

        void OnDamaged(float amount, Vector3 from, bool heavy) { if (IsClimbing && State != Phase.Ending) End(true); }

        void Play(string state, float fade)
        {
            if (_anim == state || _a == null) return;
            int h = Animator.StringToHash(state);
            if (!_a.HasState(0, h)) return;                       // controller not rebuilt yet: keep going without the clip
            _a.CrossFadeInFixedTime(h, fade, 0);
            _anim = state;
        }
    }
}
