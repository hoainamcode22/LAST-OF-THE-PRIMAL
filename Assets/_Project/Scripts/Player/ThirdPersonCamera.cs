using UnityEngine;

namespace PrimalFrontier.Player
{
    /// <summary>
    /// Survival third-person camera: orbit with mouse / stick, smooth follow, collision pull-in (fast in, slow out),
    /// over-the-shoulder framing when aiming, scroll zoom. Put it on the camera; assign the player as target.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class ThirdPersonCamera : MonoBehaviour
    {
        public Transform target;
        public Vector3 pivotOffset = new Vector3(0f, 1.62f, 0f);
        [Header("Exploration")]
        public float distance = 3.4f;
        public float minDistance = 1.4f, maxDistance = 5.5f;
        public float shoulder = 0.35f;
        public float fov = 60f;
        [Header("Aim / combat")]
        public float aimDistance = 2.0f;
        public float aimShoulder = 0.6f;
        public float aimFov = 50f;
        [Header("Limits / feel")]
        public float minPitch = -35f, maxPitch = 70f;
        public float followSmooth = 0.06f;
        public float zoomSpeed = 0.004f;
        public float collisionRadius = 0.22f;
        public LayerMask collisionMask = ~0;
        [Tooltip("layers ignored by the collision probe (player, dinos, triggers...)")]
        public LayerMask ignoreMask;
        public float returnSpeed = 3f;           // m/s the camera eases back out after a collision

        public float Yaw { get; set; }
        public float Pitch { get; set; } = 12f;
        public bool Aiming { get; set; }
        public bool InputEnabled { get; set; } = true;

        Camera _cam; Vector3 _pivotVel; Vector3 _pivot; float _curDist; float _blend;
        [Header("Situations (extra distance m, pitch bias deg)")]
        public Vector2 climbOffset = new Vector2(0.8f, -8f);
        public Vector2 buildOffset = new Vector2(1.2f, 10f);
        public Vector2 combatOffset = new Vector2(-0.35f, 0f);
        /// <summary>manual extra offset (cinematics), added to the automatic ones</summary>
        public Vector2 ModeOffset { get; set; }
        Vector2 _mode; PlayerClimb _climb; PlayerCombat _combat;
        float _shakeAmp, _shakeTime, _shakeDur;

        /// <summary>short, damped positional shake (hits, heavy footsteps nearby)</summary>
        public void AddShake(float amplitude, float duration)
        {
            _shakeAmp = Mathf.Max(_shakeAmp * (_shakeTime / Mathf.Max(0.01f, _shakeDur)), amplitude); _shakeDur = duration; _shakeTime = duration;
        }
        PlayerInputReader _in;

        void Start()
        {
            _cam = GetComponent<Camera>();
            _in = PlayerInputReader.Instance;
            if (target) { _pivot = target.position + pivotOffset; Yaw = target.eulerAngles.y; }
            _curDist = distance;
            LockCursor(true);
        }

        public static void LockCursor(bool on)
        {
            Cursor.lockState = on ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !on;
        }

        public void SnapBehindTarget()
        {
            if (!target) return;
            Yaw = target.eulerAngles.y; Pitch = 12f; _pivot = target.position + pivotOffset; _pivotVel = Vector3.zero;
        }

        void LateUpdate()
        {
            if (!target) return;
            if (_in == null) _in = PlayerInputReader.Instance;
            float dt = Time.deltaTime;
            if (InputEnabled && _in != null)
            {
                Yaw += _in.Look.x; Pitch = Mathf.Clamp(Pitch - _in.Look.y, minPitch, maxPitch);
                if (!Aiming) distance = Mathf.Clamp(distance - _in.Zoom * zoomSpeed * 60f * 0.02f, minDistance, maxDistance);
            }
            _blend = Mathf.MoveTowards(_blend, Aiming ? 1f : 0f, dt * 5f);
            float b = _blend * _blend * (3f - 2f * _blend);
            // situation offsets ease in and out (never snap): climbing looks up the trunk, building pulls back, combat tightens
            if (!_climb) _climb = target.GetComponent<PlayerClimb>();
            if (!_combat) _combat = target.GetComponent<PlayerCombat>();
            Vector2 want = ModeOffset;
            if (_climb && _climb.IsClimbing) want += climbOffset;
            else if (Building.BuildSystem.Instance && Building.BuildSystem.Instance.Active) want += buildOffset;
            else if (_combat && _combat.InCombat) want += combatOffset;
            _mode = Vector2.Lerp(_mode, want, 1f - Mathf.Exp(-3.5f * dt));
            float wantDist = Mathf.Lerp(Mathf.Max(minDistance * 0.7f, distance + _mode.x), aimDistance, b);
            float sh = Mathf.Lerp(shoulder, aimShoulder, b);
            if (_cam) _cam.fieldOfView = Mathf.Lerp(fov, aimFov, b);

            Vector3 goal = target.position + pivotOffset;
            _pivot = Vector3.SmoothDamp(_pivot, goal, ref _pivotVel, followSmooth);
            if ((goal - _pivot).sqrMagnitude > 25f) _pivot = goal;                   // teleports

            var rot = Quaternion.Euler(Mathf.Clamp(Pitch + _mode.y * (1f - b), minPitch, maxPitch), Yaw, 0f);
            Vector3 shoulderPos = _pivot + rot * new Vector3(sh, 0f, 0f);
            // keep the shoulder offset itself out of walls
            var mask = collisionMask & ~ignoreMask;
            if (Physics.SphereCast(_pivot, collisionRadius * 0.8f, (shoulderPos - _pivot).normalized, out var sh0, sh, mask, QueryTriggerInteraction.Ignore))
                shoulderPos = _pivot + (shoulderPos - _pivot).normalized * Mathf.Max(0f, sh0.distance - 0.05f);
            Vector3 back = rot * Vector3.back;
            float allowed = wantDist;
            if (Physics.SphereCast(shoulderPos, collisionRadius, back, out var hit, wantDist, mask, QueryTriggerInteraction.Ignore))
                allowed = Mathf.Max(0.3f, hit.distance - 0.05f);
            _curDist = allowed < _curDist ? allowed : Mathf.MoveTowards(_curDist, allowed, returnSpeed * dt);
            Vector3 shake = Vector3.zero;
            if (_shakeTime > 0f)
            {
                _shakeTime -= dt; float k = Mathf.Clamp01(_shakeTime / _shakeDur) * _shakeAmp; float t = Time.time * 28f;
                shake = rot * new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, t) - 0.5f, 0f) * 2f * k;
            }
            transform.SetPositionAndRotation(shoulderPos + back * _curDist + shake, rot);
        }
    }
}
