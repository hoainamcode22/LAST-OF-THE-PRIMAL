using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Core;
using PrimalFrontier.Survival;
using PrimalFrontier.World;

namespace PrimalFrontier.Player
{
    /// <summary>
    /// Climbing state for every Climbable kind:
    /// - Tree: approach, press E -> Climb_Start (the body is carried onto the trunk), hang (Climb_Idle), W / S climb up and
    ///   down (Climb_Up / Climb_Down, the body follows the trunk at the clip speed), E picks fruit in reach (Harvest_Fruit),
    ///   Space / C lets go (Climb_End, the body is set down beside the trunk).
    /// - Ledge: press E in front of it -> one continuous pull-over: reach up to the lip (hands on the edge), pull the body
    ///   up the face, swing the legs over and stand on top. No teleport: the body follows a path every frame.
    /// - RockFace: like a trunk but on a flat face (hands and feet on the face plane); at the top, W pulls over onto the
    ///   top like a ledge; at the bottom, S steps off.
    /// Hands (and feet) are placed by PlayerIK (trunk surface, face plane, or the lip holds during a pull-over). Getting
    /// hurt or running out of stamina makes you fall off. The character controller and the motor are off while climbing;
    /// everything is restored on the way down / on top.
    /// </summary>
    [RequireComponent(typeof(PlayerMotor))]
    public class PlayerClimb : MonoBehaviour
    {
        public enum Phase { None, Starting, Hanging, Harvesting, Ending, Mantling }
        public float attachTime = 0.75f, detachTime = 0.9f;
        [Tooltip("distance from the trunk surface to the player's pivot")] public float bodyOffset = 0.3f;
        [Tooltip("distance from a rock face to the player's pivot while on it")] public float faceOffset = 0.34f;
        public float startHeight = 0.35f;
        [Tooltip("hands above the feet (for reaching fruit / a lip)")] public float reachHeight = 2.0f;
        public float harvestReach = 1.25f;
        [Header("Pull-over (ledge, top of a rock face)")]
        [Tooltip("reach up to the lip (s)")] public float mantleReach = 0.35f;
        [Tooltip("pull the body up: base time + per metre of height (s)")] public Vector2 mantlePull = new Vector2(0.35f, 0.3f);
        [Tooltip("legs over the edge and stand up (s)")] public float mantleOver = 0.6f;
        [Tooltip("half the distance between the hands on the lip (m)")] public float lipHandSpread = 0.22f;

        public Phase State { get; private set; }
        public bool IsClimbing => State != Phase.None;
        public Climbable Current { get; private set; }
        /// <summary>feet height above the climb start</summary>
        public float Height => _h;
        /// <summary>largest single-frame move of the body during the last climb (m): a teleport check for tests</summary>
        public float MaxStep { get; private set; }

        PlayerMotor _motor; CharacterController _cc; PlayerAnimationDriver _drv; PlayerInteraction _pi; PlayerHealth _hp; PlayerSurvival _sv;
        Animator _a; CharacterAnimationEvents _ev; PlayerIK _ik;
        float _h, _t; Vector3 _from, _to, _away; Quaternion _fromR, _toR; FruitCluster _harvest; bool _harvested; string _anim;
        // pull-over path: p0 start -> p1 hands on the lip -> p2 hips at the lip -> p3 standing on top
        Vector3 _m0, _m1, _m2, _m3; float _tReach, _tPull, _tOver; Vector3 _last;

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
            if (!_a && _drv) _a = _drv.animator;
            Current = c; _t = 0f; _h = startHeight; MaxStep = 0f; _last = transform.position;
            if (!_ik && _a) _ik = _a.GetComponent<PlayerIK>();
            _motor.Stop(); _motor.enabled = false; if (_cc) _cc.enabled = false;
            if (_pi) { _pi.Suspended = true; _pi.ExternalPrompt = ClimbPrompt; }
            if (c.IsFace) _away = -c.Forward;
            else { _away = transform.position - c.Bottom; _away.y = 0f; _away = _away.sqrMagnitude > 0.01f ? _away.normalized : -transform.forward; }
            _from = transform.position; _fromR = transform.rotation;
            if (_ik) _ik.Climb = c;
            GameEvents.Raise(GameEventType.ClimbStarted, c.name, 1, c.Bottom);
            if (c.kind == ClimbKind.Ledge) { StartMantle(true); return true; }
            State = Phase.Starting;
            Pose(_h, out _to, out _toR);
            Play("Climb_Start", 0.12f);
            Audio.SfxPlayer.Instance.Play(Audio.SfxId.ClimbGrab, c.AxisAt(_h + 1.6f), 0.8f);
            return true;
        }

        /// <summary>let go: drop = true falls straight down (hurt, out of stamina), false climbs down the last bit</summary>
        public void End(bool drop = false)
        {
            if (!IsClimbing || State == Phase.Ending) return;
            State = Phase.Ending; _t = 0f;
            _from = transform.position; _fromR = transform.rotation;
            float off = Current.IsFace ? faceOffset : Current.trunkRadius + bodyOffset;
            Vector3 land = Current.Bottom + _away * (off + (drop ? 0.2f : 0.45f));
            if (Physics.Raycast(land + Vector3.up * 3f, Vector3.down, out var hit, 8f, ~(1 << gameObject.layer), QueryTriggerInteraction.Ignore)) land.y = hit.point.y;
            _to = land; _toR = Quaternion.LookRotation(-_away, Vector3.up);
            if (_ik) _ik.ClearHolds();
            Play("Climb_End", 0.12f);
        }

        void Finish()
        {
            if (_cc) _cc.enabled = true;
            _motor.enabled = true; _motor.Warp(_to, _toR);
            if (_pi) { _pi.Suspended = false; _pi.ExternalPrompt = null; }
            if (_ik) { _ik.Climb = null; _ik.ReachTarget = null; _ik.ClearHolds(); }
            State = Phase.None; Current = null; _harvest = null; _anim = null;
        }

        void Pose(float h, out Vector3 pos, out Quaternion rot)
        {
            Vector3 axis = Current.AxisAt(h);
            if (Current.IsFace)
            {
                pos = axis + Current.FaceNormal * faceOffset; pos.y = Current.Bottom.y + h;
                rot = Quaternion.LookRotation(Current.Forward, Vector3.up);
                return;
            }
            pos = axis + _away * (Current.trunkRadius + bodyOffset); pos.y = Current.Bottom.y + h;
            rot = Quaternion.LookRotation(-_away, Vector3.up);
        }

        // ------------------------------------------------------------------ pull-over
        /// <summary>fromGround: a ledge climbed from standing; else the top of a rock face from hanging</summary>
        void StartMantle(bool fromGround)
        {
            var c = Current;
            if (_sv && c.mantleStamina > 0f && !_sv.UseStamina(c.mantleStamina))
            {
                PlayerInteraction.Notify("Too tired to pull yourself up.");
                if (fromGround) { _to = transform.position; _toR = transform.rotation; Finish(); } else End(false);
                return;
            }
            State = Phase.Mantling; _t = 0f;
            Vector3 fwd = c.Forward, lip = c.Top, exit = c.Exit;
            _m0 = transform.position; _fromR = transform.rotation;
            // hands on the lip: the body hangs reachHeight below it, close to the face (never below the ground it stands on)
            Vector3 face = lip - fwd * faceOffset;
            _m1 = new Vector3(face.x, Mathf.Max(_m0.y, lip.y - reachHeight * 0.95f), face.z);
            // hips at the lip: body up the face, a little back so the knees clear the edge
            _m2 = new Vector3(face.x, lip.y - 0.55f, face.z) - fwd * 0.05f;
            _m3 = exit;
            _tReach = fromGround ? mantleReach : 0.12f;
            _tPull = mantlePull.x + mantlePull.y * (lip.y - _m0.y);
            _tOver = mantleOver;
            _toR = Quaternion.LookRotation(fwd, Vector3.up);
            if (_ik) _ik.SetHolds(lip - c.Right * lipHandSpread + Vector3.up * 0.03f, lip + c.Right * lipHandSpread + Vector3.up * 0.03f);
            Play(fromGround ? "Climb_Start" : "Climb_Up", 0.12f);
            _gripAt = Time.time + _tReach * 0.9f; _gripPoint = lip;                     // the palms reach the edge: slap + a little grit
        }

        float _gripAt = -1f; Vector3 _gripPoint;
        void UpdateMantle(float dt)
        {
            _t += dt;
            if (_gripAt > 0f && Time.time >= _gripAt)
            {
                _gripAt = -1f;
                Audio.SfxPlayer.Instance.Play(Audio.SfxId.ClimbGrab, _gripPoint, 0.9f);
                VFX.VfxPool.Instance.Play(VFX.VfxId.RockDust, _gripPoint, Vector3.down, null, 0.8f);
            }
            Vector3 p; Quaternion r = Quaternion.Slerp(_fromR, _toR, Mathf.Clamp01(_t / Mathf.Max(0.05f, _tReach)));
            if (_t < _tReach)
            {
                float k = Mathf.SmoothStep(0f, 1f, _t / _tReach);
                p = Vector3.Lerp(_m0, _m1, k);
            }
            else if (_t < _tReach + _tPull)
            {
                float k = Mathf.SmoothStep(0f, 1f, (_t - _tReach) / _tPull);
                p = Vector3.Lerp(_m1, _m2, k); Play("Climb_Up", 0.15f);
            }
            else
            {
                float k = Mathf.Clamp01((_t - _tReach - _tPull) / _tOver);
                // over the edge: up first, then forward onto the top (an arc, the knees clear the lip)
                float up = Mathf.Sin(Mathf.Min(1f, k * 1.6f) * Mathf.PI * 0.5f), fw = Mathf.SmoothStep(0f, 1f, k);
                p = new Vector3(Mathf.Lerp(_m2.x, _m3.x, fw), Mathf.Lerp(_m2.y, _m3.y, up), Mathf.Lerp(_m2.z, _m3.z, fw));
                if (_ik) _ik.ClimbFeetFree = true;                                       // the legs come over the edge: no feet on the face
                if (k > 0.45f && _ik) _ik.ClearHolds();                                  // the hands leave the edge as the body stands
                Play("Climb_End", 0.2f);
                if (k >= 1f) { transform.SetPositionAndRotation(p, r); _to = _m3; _toR = r; Finish(); return; }
            }
            transform.SetPositionAndRotation(p, r);
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
                    float top = Mathf.Max(startHeight, Current.IsFace ? Current.Length - reachHeight * 0.95f : Current.Length);
                    if (v > 0.25f && _h < top)
                    {
                        if (_sv && !_sv.UseStamina(Current.staminaPerSecond * dt)) { PlayerInteraction.Notify("Your arms give out."); End(true); break; }
                        _h = Mathf.Min(top, _h + Current.climbSpeed * v * dt); Play("Climb_Up", 0.2f);
                    }
                    else if (v > 0.25f && Current.kind == ClimbKind.RockFace) { StartMantle(false); break; }   // hands on the lip: over the top
                    else if (v < -0.25f)
                    {
                        if (_h <= startHeight + 0.01f) { End(false); break; }
                        _h = Mathf.Max(startHeight, _h + Current.descendSpeed * v * dt); Play("Climb_Down", 0.2f);
                    }
                    else Play("Climb_Idle", 0.25f);
                    Pose(_h, out var p, out var r);
                    transform.SetPositionAndRotation(Vector3.Lerp(transform.position, p, 1f - Mathf.Exp(-14f * dt)), Quaternion.Slerp(transform.rotation, r, 1f - Mathf.Exp(-10f * dt)));
                    if (input && (input.JumpPressed || input.CrouchPressed || input.DodgePressed)) { End(true); break; }
                    if (input && input.InteractPressed && !Current.IsFace)
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
                case Phase.Mantling:
                    UpdateMantle(dt);
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
            if (IsClimbing) { MaxStep = Mathf.Max(MaxStep, (transform.position - _last).magnitude); _last = transform.position; }
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
            if (State == Phase.Mantling) return ("Climbing up", null);
            if (State == Phase.Hanging && Current && Current.kind == ClimbKind.RockFace)
                return ("Climbing", _h >= Current.Length - reachHeight * 0.95f - 0.02f ? "W pull over the top   S climb down   Space let go" : "W / S climb   Space let go");
            if (State == Phase.Hanging && FruitInReach()) return ("Pick the fruit", "W / S climb   Space let go");
            if (State == Phase.Hanging) return ("Climbing", Current && Current.Fruits.Length > 0 ? "Climb up to the fruit   Space let go" : "W / S climb   Space let go");
            return (null, null);
        }

        void OnAnimEvent(string fn, string param)
        {
            if (fn == "OnClimbStep" && (State == Phase.Hanging || State == Phase.Mantling) && Current)
            {
                // hand / foot contacts on the way up and down: grit on rock, a softer grip on bark
                Vector3 at = transform.position + Vector3.up * 0.3f + transform.forward * 0.3f;
                if (Current.IsFace) { Audio.SfxPlayer.Instance.Play(Audio.SfxId.ClimbScrape, at, 0.8f); if (Random.value < 0.4f) VFX.VfxPool.Instance.Play(VFX.VfxId.RockDust, at, Vector3.down, null, 0.5f); }
                else Audio.SfxPlayer.Instance.Play(Audio.SfxId.ClimbGrab, at + Vector3.up * 1.4f, 0.45f);
            }
            if (fn == "OnHarvest" && State == Phase.Harvesting && _harvest && !_harvested) { _harvested = _harvest.Harvest(_pi ? _pi.Inventory : null) || true; }
        }

        void OnDamaged(float amount, Vector3 from, bool heavy) { if (IsClimbing && State != Phase.Ending && State != Phase.Mantling) End(true); }

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
