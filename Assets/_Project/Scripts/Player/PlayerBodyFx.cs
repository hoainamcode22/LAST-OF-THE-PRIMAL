using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Audio;
using PrimalFrontier.VFX;

namespace PrimalFrontier.Player
{
    /// <summary>
    /// Small body life on top of the clips (PC phase, directive 8-10 / 19 / 55), all procedural and cheap:
    /// - chewing after a bite: two quiet Chew sounds and a small head nod for a couple of seconds (the Eat clip brings the
    ///   hand to the mouth, PlayerFacial opens the jaw while the clip plays);
    /// - water dripping off the chin / hands a moment after a drink;
    /// - while bleeding, a drop falls to the ground now and then (more often when moving);
    /// - idle breathing: the chest rises and falls a little while standing still.
    /// Added by PlayerAnimationDriver. Presentation only.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerBodyFx : MonoBehaviour
    {
        [Header("Chewing")]
        [Tooltip("seconds of chewing after a bite")] public float chewSeconds = 2.0f;
        [Tooltip("head nod amplitude (deg) and rate (Hz)")] public float chewNod = 2.2f, chewRate = 2.3f;
        [Header("Breathing (standing still)")]
        [Tooltip("chest pitch amplitude (deg) and breaths per minute")] public float breathDeg = 0.9f, breathsPerMinute = 15f;
        [Header("Head stabilisation (walking / running)")]
        [Tooltip("share of the clip's quick head wobble removed while moving (the gaze stays steady, slow head motion is kept)")] [Range(0f, 1f)] public float headSteady = 0.5f;
        [Tooltip("time constant of the kept (slow) head motion, s")] public float headSteadyTime = 0.22f;
        [Header("Bleeding")]
        [Tooltip("seconds between blood drops standing / moving")] public Vector2 dripEvery = new Vector2(1.6f, 0.7f);

        Animator _a; PlayerAnimationDriver _drv; PlayerHealth _hp; PlayerMotor _motor; CharacterAnimationEvents _ev; PlayerClimb _climb;
        Quaternion _headRel = Quaternion.identity; bool _hasHeadRel; float _steadyW;
        Transform _head, _chest, _hips; SkinnedMeshRenderer _skin;
        float _chewUntil = -1f, _nextChew, _drinkDrip = -1f, _nextDrip, _breathW, _chewW;
        int _chewSounds;

        void Awake()
        {
            _drv = GetComponent<PlayerAnimationDriver>(); _hp = GetComponent<PlayerHealth>(); _motor = GetComponent<PlayerMotor>();
            _a = _drv ? _drv.animator : GetComponentInChildren<Animator>();
            _ev = GetComponentInChildren<CharacterAnimationEvents>();
        }
        void OnEnable() { if (_ev) _ev.AnimationEventRaised += OnEvent; }
        void OnDisable() { if (_ev) _ev.AnimationEventRaised -= OnEvent; }

        void OnEvent(string fn, string param)
        {
            if (fn == "OnEat") { _chewUntil = Time.time + chewSeconds; _nextChew = Time.time + 0.35f; _chewSounds = 0; }
            else if (fn == "OnDrink" && param != "fill") _drinkDrip = Time.time + 0.25f;
        }

        void Update()
        {
            if (_hp && _hp.IsDead) return;
            if (!Bones()) return;
            float now = Time.time;
            // chew sounds: two, spaced (the bite itself already made its sound)
            if (now < _chewUntil && now >= _nextChew && _chewSounds < 2)
            {
                SfxPlayer.Instance.Play(SfxId.Chew, _head.position + transform.forward * 0.08f, 0.8f);
                _chewSounds++; _nextChew = now + 0.75f;
            }
            if (_drinkDrip > 0f && now >= _drinkDrip)
            {
                _drinkDrip = -1f;
                VfxPool.Instance.Play(VfxId.DrinkDrips, _head.position + transform.forward * 0.1f - Vector3.up * 0.12f, Vector3.down, null, 1f);
            }
            if (_hp && _hp.IsBleeding && now >= _nextDrip)
            {
                bool moving = _motor && _motor.PlanarSpeed > 0.5f;
                _nextDrip = now + Random.Range(0.8f, 1.2f) * (moving ? dripEvery.y : dripEvery.x);
                Vector3 at = (_hips ? _hips.position : transform.position + Vector3.up) + transform.right * Random.Range(-0.12f, 0.12f) + transform.forward * 0.08f;
                VfxPool.Instance.Play(VfxId.BloodDrip, at, Vector3.down);
            }
        }

        void LateUpdate()
        {
            if (!Bones() || (_hp && _hp.IsDead)) return;
            // the Animator culls transform updates while the body is off screen (CullUpdateTransforms): adding to a pose it did
            // not write this frame would accumulate, so only touch the bones while the body is visible
            if (!_skin) foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>()) if (!_skin || r.bones.Length > _skin.bones.Length) _skin = r;
            if (!_skin || !_skin.isVisible) return;
            float dt = Time.deltaTime;
            // head stabilisation: the head's rotation relative to the body root is low-passed and part of the quick wobble
            // (step bob, arm swing carried up the spine) is taken out; turns and the look IK are slow and pass through
            if (!_climb) _climb = GetComponent<PlayerClimb>();
            float speed = _motor ? _motor.PlanarSpeed : 0f;
            bool steady = headSteady > 0f && speed > 0.6f && !(_drv && (_drv.IsBusy || _drv.IsDead)) && !(_climb && _climb.IsClimbing);
            _steadyW = Mathf.MoveTowards(_steadyW, steady ? headSteady * Mathf.InverseLerp(0.6f, 1.6f, speed) : 0f, dt * 2f);
            Quaternion root = transform.rotation, rel = Quaternion.Inverse(root) * _head.rotation;
            if (!_hasHeadRel || dt <= 0f) { _headRel = rel; _hasHeadRel = true; }
            else _headRel = Quaternion.Slerp(_headRel, rel, 1f - Mathf.Exp(-dt / Mathf.Max(0.02f, headSteadyTime)));
            if (_steadyW > 0.001f) _head.rotation = root * Quaternion.Slerp(rel, _headRel, _steadyW);
            // chewing nod (small; the look IK already placed the head this frame)
            _chewW = Mathf.MoveTowards(_chewW, Time.time < _chewUntil ? 1f : 0f, dt * 3f);
            if (_chewW > 0.001f) _head.localRotation *= Quaternion.Euler(Mathf.Sin(Time.time * chewRate * Mathf.PI * 2f) * chewNod * _chewW, 0f, 0f);
            // breathing while standing still
            bool still = _drv && _drv.IsIdleStill;
            _breathW = Mathf.MoveTowards(_breathW, still ? 1f : 0f, dt * 0.8f);
            if (_breathW > 0.001f && _chest)
            {
                float b = Mathf.Sin(Time.time * breathsPerMinute / 60f * Mathf.PI * 2f);
                _chest.localRotation *= Quaternion.Euler(-b * breathDeg * _breathW, 0f, 0f);
                _head.localRotation *= Quaternion.Euler(b * breathDeg * 0.6f * _breathW, 0f, 0f);   // the head stays level
            }
        }

        bool Bones()
        {
            if (!_a || !_a.isHuman) return false;
            if (!_head) _head = _a.GetBoneTransform(HumanBodyBones.Head);
            if (!_chest) _chest = _a.GetBoneTransform(HumanBodyBones.UpperChest) ?? _a.GetBoneTransform(HumanBodyBones.Chest);
            if (!_hips) _hips = _a.GetBoneTransform(HumanBodyBones.Hips);
            return _head;
        }
    }
}
