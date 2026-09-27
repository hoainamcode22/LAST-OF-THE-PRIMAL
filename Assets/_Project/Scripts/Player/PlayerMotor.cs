using UnityEngine;

namespace PrimalFrontier.Player
{
    /// <summary>Anything that can pay for sprinting (the survival stats implement this).</summary>
    public interface IStaminaSource
    {
        bool CanSprint { get; }
        void DrainSprint(float dt);
        float MoveSpeedMultiplier { get; }   // hunger / thirst / injury penalties
    }

    /// <summary>
    /// Grounded third-person movement on a CharacterController: camera-relative input, acceleration / deceleration,
    /// gravity, jump, crouch, slope handling and ground probing. Speeds match the authored animation speeds.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMotor : MonoBehaviour
    {
        [Header("Speeds (m/s) - equal to the clip speeds so the feet do not slide")]
        public float walkSpeed = 1.35f;
        public float runSpeed = 3.8f;
        public float sprintSpeed = 6.2f;
        public float crouchSpeed = 0.95f;
        [Header("Feel")]
        public float acceleration = 10f;
        public float deceleration = 14f;
        [Range(0, 1)] public float airControl = 0.25f;
        public float turnSpeed = 600f;           // deg/s towards the move direction
        public float gravity = -22f;
        public float jumpHeight = 0.95f;
        public float coyoteTime = 0.12f;
        public float jumpBuffer = 0.12f;
        [Header("Ground")]
        public LayerMask groundMask = ~0;
        public float maxStepHeight = 0.42f;
        public float groundProbe = 0.25f;
        public float slideSpeed = 5f;
        [Header("Crouch")]
        public float standHeight = 1.8f;
        public float crouchHeight = 1.15f;

        public CharacterController Controller { get; private set; }
        public Transform CameraTransform { get; set; }
        public IStaminaSource Stamina { get; set; }
        /// <summary>false while a full-body action (eat, craft, gather...) or a cutscene owns the character</summary>
        public bool CanMove { get; set; } = true;
        public bool AimMode { get; set; }

        public bool IsGrounded { get; private set; } = true;
        public bool IsCrouching { get; private set; }
        public bool IsSprinting { get; private set; }
        public float VerticalVelocity => _vy;
        public float PlanarSpeed => new Vector2(_vel.x, _vel.z).magnitude;
        public float TurnRate { get; private set; }                // deg/s, + = left
        public Vector3 GroundNormal { get; private set; } = Vector3.up;
        public string GroundTag { get; private set; } = "";
        public event System.Action Jumped;
        public event System.Action<float> Landed;                  // impact speed (m/s)

        Vector3 _vel; float _vy; float _lastGrounded; float _jumpQueued = -1f; float _prevYaw;
        PlayerInputReader _in;

        void Awake()
        {
            Controller = GetComponent<CharacterController>();
            Controller.height = standHeight; Controller.center = new Vector3(0, standHeight / 2f, 0);
            if (Controller.radius <= 0.01f) Controller.radius = 0.3f;
            Controller.slopeLimit = 45f; Controller.stepOffset = 0.35f; Controller.skinWidth = 0.03f;
            _prevYaw = transform.eulerAngles.y;
        }

        void Start()
        {
            _in = PlayerInputReader.Instance;
            if (CameraTransform == null && Camera.main) CameraTransform = Camera.main.transform;
        }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);        // a loading hitch must not integrate a huge fall
            if (dt <= 0f) return;
            if (_in == null) _in = PlayerInputReader.Instance;
            Vector2 mv = _in != null && CanMove ? _in.Move : Vector2.zero;

            ProbeGround();
            // crouch toggle (stand up only if there is room)
            if (_in != null && CanMove && _in.CrouchPressed) SetCrouch(!IsCrouching);

            // desired planar velocity (camera relative)
            Vector3 fwd = CameraTransform ? Vector3.ProjectOnPlane(CameraTransform.forward, Vector3.up).normalized : transform.forward;
            if (fwd.sqrMagnitude < 0.01f) fwd = transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 dir = fwd * mv.y + right * mv.x;
            float mag = Mathf.Clamp01(dir.magnitude); if (mag > 0.001f) dir /= dir.magnitude;
            bool wantSprint = _in != null && _in.Sprint && !IsCrouching && !AimMode && mv.y > 0.1f;
            IsSprinting = wantSprint && mag > 0.5f && (Stamina == null || Stamina.CanSprint) && IsGrounded;
            float top = IsCrouching ? crouchSpeed : AimMode ? walkSpeed : (_in != null && _in.Walk) ? walkSpeed : IsSprinting ? sprintSpeed : runSpeed;
            // a gentle stick push walks; keyboard (magnitude 1) uses the full speed of the current mode
            float speed = mag < 0.55f && !IsCrouching ? Mathf.Min(top, walkSpeed) * mag / 0.55f : top * (IsCrouching ? mag : 1f);
            if (Stamina != null) speed *= Stamina.MoveSpeedMultiplier;
            Vector3 target = dir * speed;
            if (IsSprinting && Stamina != null) Stamina.DrainSprint(dt);

            float rate = target.sqrMagnitude > _vel.sqrMagnitude ? acceleration : deceleration;
            if (!IsGrounded) rate *= airControl;
            Vector3 planar = new Vector3(_vel.x, 0, _vel.z);
            planar = Vector3.MoveTowards(planar, target, rate * dt);

            // slopes: slide down surfaces steeper than the controller allows
            float slope = Vector3.Angle(GroundNormal, Vector3.up);
            if (IsGrounded && slope > Controller.slopeLimit + 2f && slope < 80f)       // < 80: ignore step edges / walls
            {
                Vector3 down = Vector3.ProjectOnPlane(Vector3.down, GroundNormal).normalized;
                planar += down * slideSpeed * dt * 10f; planar = Vector3.ClampMagnitude(planar, Mathf.Max(top, slideSpeed));
            }
            _vel = new Vector3(planar.x, 0, planar.z);

            // gravity / jump
            if (_in != null && CanMove && _in.JumpPressed) _jumpQueued = Time.time;
            bool canJump = Time.time - _lastGrounded <= coyoteTime && !IsCrouching;
            if (_jumpQueued > 0 && Time.time - _jumpQueued <= jumpBuffer && canJump && _vy <= 0.1f)
            {
                _vy = Mathf.Sqrt(2f * -gravity * jumpHeight); _jumpQueued = -1f; _lastGrounded = -10f; IsGrounded = false;
                Jumped?.Invoke();
            }
            else if (IsGrounded && _vy < 0f) _vy = -3f;              // keeps the controller glued on slopes / steps
            else _vy += gravity * dt;

            // move along the ground plane when grounded (no bouncing down slopes)
            Vector3 motion = _vel;
            // follow walkable ground only; a step riser / wall normal must not cancel the forward motion (lets stepOffset work)
            if (IsGrounded && _vy <= 0f && Vector3.Angle(GroundNormal, Vector3.up) <= Controller.slopeLimit) motion = Vector3.ProjectOnPlane(_vel, GroundNormal);
            motion.y += _vy;
            float vyBefore = _vy;
            Vector3 p0 = transform.position;
            var flags = Controller.Move(motion * dt);
            // blocked while walking into something low (step edge rides the round capsule bottom, so the flag can be Below)
            if (IsGrounded && _vy <= 0f && planar.sqrMagnitude > 0.01f)
            {
                Vector3 moved = transform.position - p0; moved.y = 0f;
                float wanted = planar.magnitude * dt;
                if ((flags & CollisionFlags.Sides) != 0 || Vector3.Dot(moved, planar.normalized) < wanted * 0.5f) TryStepUp(planar.normalized);
            }
            if ((flags & CollisionFlags.Above) != 0 && _vy > 0) _vy = 0f;
            bool wasGrounded = IsGrounded;
            ProbeGround();
            if (!wasGrounded && IsGrounded) Landed?.Invoke(-vyBefore);

            // facing
            Vector3 face = AimMode ? fwd : new Vector3(_vel.x, 0, _vel.z);
            if (face.sqrMagnitude > 0.04f || AimMode)
            {
                var want = Quaternion.LookRotation(face.sqrMagnitude > 1e-4f ? face.normalized : transform.forward, Vector3.up);
                float ts = IsSprinting ? turnSpeed * 0.75f : turnSpeed;
                transform.rotation = Quaternion.RotateTowards(transform.rotation, want, ts * dt);
            }
            float yaw = transform.eulerAngles.y;
            TurnRate = Mathf.Lerp(TurnRate, -Mathf.DeltaAngle(_prevYaw, yaw) / dt, 1f - Mathf.Exp(-10f * dt));
            _prevYaw = yaw;
        }

        void ProbeGround()
        {
            var c = Controller;
            Vector3 origin = transform.position + Vector3.up * (c.radius + 0.05f);
            if (Physics.SphereCast(origin, c.radius * 0.95f, Vector3.down, out var hit, 0.05f + groundProbe, groundMask, QueryTriggerInteraction.Ignore))
            {
                GroundNormal = hit.normal;
                // a sphere touching an edge reports the edge normal; ask the collider for the real face under the contact
                if (Vector3.Angle(hit.normal, Vector3.up) > c.slopeLimit)
                {
                    Vector3 away = hit.point - transform.position; away.y = 0f;          // step onto the face beyond the edge
                    Vector3 from = hit.point + Vector3.up * 0.1f + (away.sqrMagnitude > 1e-6f ? away.normalized * 0.02f : Vector3.zero);
                    if (hit.collider.Raycast(new Ray(from, Vector3.down), out var face, 0.3f)) GroundNormal = face.normal;
                }
                GroundTag = hit.collider.sharedMaterial ? hit.collider.sharedMaterial.name : hit.collider.tag;
                bool close = hit.distance <= 0.05f + (_vy > 0.5f ? 0.02f : groundProbe * 0.6f);
                IsGrounded = (c.isGrounded || close) && _vy <= 0.5f;
            }
            else { IsGrounded = c.isGrounded && _vy <= 0.5f; GroundNormal = Vector3.up; }
            if (IsGrounded) _lastGrounded = Time.time;
        }

        /// <summary>explicit step assist: the capsule's round bottom treats small ledges as steep slopes</summary>
        void TryStepUp(Vector3 dir)
        {
            var c = Controller;
            Vector3 probe = transform.position + dir * (c.radius + 0.08f) + Vector3.up * (maxStepHeight + 0.05f);
            if (!Physics.Raycast(probe, Vector3.down, out var hit, maxStepHeight + 0.05f, groundMask, QueryTriggerInteraction.Ignore)) return;
            float h = hit.point.y - transform.position.y;
            if (h < 0.02f || h > maxStepHeight || Vector3.Angle(hit.normal, Vector3.up) > c.slopeLimit) return;
            // room for the capsule on top of the step?
            Vector3 top = transform.position + Vector3.up * (h + 0.03f);
            int mask = groundMask & ~(1 << gameObject.layer);        // not our own capsule
            if (Physics.CheckCapsule(top + Vector3.up * (c.radius + 0.02f), top + Vector3.up * (c.height - c.radius), c.radius * 0.9f, mask, QueryTriggerInteraction.Ignore)) return;
            c.Move(Vector3.up * (h + 0.03f));
            c.Move(dir * 0.06f);
        }

        public bool SetCrouch(bool on)
        {
            if (on == IsCrouching) return true;
            if (!on)
            {
                var c = Controller;
                Vector3 bottom = transform.position + Vector3.up * c.radius;
                if (Physics.SphereCast(bottom, c.radius * 0.9f, Vector3.up, out _, standHeight - c.radius * 2f, groundMask, QueryTriggerInteraction.Ignore))
                    return false;                                         // no room to stand
            }
            IsCrouching = on;
            float h = on ? crouchHeight : standHeight;
            Controller.height = h; Controller.center = new Vector3(0, h / 2f, 0);
            return true;
        }

        /// <summary>Teleport (spawn, load, cutscene) without the controller fighting it.</summary>
        public void Warp(Vector3 pos, Quaternion rot)
        {
            Controller.enabled = false; transform.SetPositionAndRotation(pos, rot); Controller.enabled = true;
            _vel = Vector3.zero; _vy = 0f; _prevYaw = rot.eulerAngles.y;
        }

        public void AddImpulse(Vector3 v) { _vel += new Vector3(v.x, 0, v.z); _vy += v.y; }
        public void Stop() { _vel = Vector3.zero; }
    }
}
