using UnityEngine;

namespace PrimalFrontier.AI
{
    /// <summary>
    /// Small signs of life on top of the animation clips, for every creature:
    /// - the head turns toward what the animal is watching (the player while it observes, investigates, chases or
    ///   attacks), clamped to a natural range and blended in / out;
    /// - the attack tell: while DinosaurController winds up (Telegraph 0..1) the head is drawn back / up, so the strike
    ///   is readable and the player can dodge;
    /// - eyes (Eye_L / Eye_R under the Head bone) blink every few seconds, glance toward the target, and stay half
    ///   closed after death.
    /// Works after the Animator (LateUpdate), never accumulates when the Animator is culled, and skips far animals.
    /// </summary>
    public class DinoLife : MonoBehaviour
    {
        [Range(0, 80)] public float maxYaw = 45f;
        [Range(0, 60)] public float maxPitch = 22f;
        [Range(0, 1)] public float lookWeight = 0.75f;
        [Tooltip("degrees the head is drawn back during the attack wind-up")] public float tellPitch = 14f;
        public Vector2 blinkEvery = new Vector2(2.5f, 7f);
        [Range(0, 45)] public float eyeGlance = 22f;
        public float activeDistance = 70f;

        DinosaurController _d; Animator _anim; Transform _head, _model, _eyeL, _eyeR;
        Vector3 _fwdLocal; float _w, _tell, _nextBlink, _blinkT = -1f;
        Quaternion _pre, _lastSet; bool _haveLast;
        Quaternion _eyeRestL, _eyeRestR; Vector3 _eyeScaleL, _eyeScaleR;

        void Start()
        {
            _d = GetComponent<DinosaurController>();
            _anim = GetComponentInChildren<Animator>();
            _model = _anim ? _anim.transform : transform;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Head" && !_head) _head = t;
                else if (t.name == "Eye_L") _eyeL = t;
                else if (t.name == "Eye_R") _eyeR = t;
            }
            if (_head) _fwdLocal = _head.InverseTransformDirection(_model.forward);
            if (_eyeL) { _eyeRestL = _eyeL.localRotation; _eyeScaleL = _eyeL.localScale; }
            if (_eyeR) { _eyeRestR = _eyeR.localRotation; _eyeScaleR = _eyeR.localScale; }
            _nextBlink = Time.time + Random.Range(blinkEvery.x, blinkEvery.y);
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam && (cam.transform.position - transform.position).sqrMagnitude > activeDistance * activeDistance) return;
            float dt = Time.deltaTime;
            bool alive = !_d || _d.IsAlive;
            Transform target = _d && alive ? _d.LookTarget : null;
            _w = Mathf.MoveTowards(_w, target ? lookWeight : 0f, dt * 1.6f);
            _tell = Mathf.MoveTowards(_tell, _d && _d.Telegraph > 0f ? 1f : 0f, dt * 7f);
            Head(target);
            Eyes(target, alive, dt);
        }

        void Head(Transform target)
        {
            if (!_head) return;
            // animation did not write the head this frame (culled / disabled): undo last frame's offset first
            if (_haveLast && _head.localRotation == _lastSet) _head.localRotation = _pre;
            _haveLast = false;
            if (_w <= 0.001f && _tell <= 0.001f) return;
            if (_anim && !_anim.enabled) return;
            _pre = _head.localRotation;
            Quaternion delta = Quaternion.identity;
            if (_w > 0.001f && target)
            {
                Vector3 to = target.position + Vector3.up * 1.5f - _head.position;
                Vector3 l = _model.InverseTransformDirection(to.normalized);
                float yaw = Mathf.Clamp(Mathf.Atan2(l.x, l.z) * Mathf.Rad2Deg, -maxYaw, maxYaw);
                float pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(l.y, -1f, 1f)) * Mathf.Rad2Deg, -maxPitch, maxPitch);
                Vector3 want = _model.TransformDirection(Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward);
                Vector3 cur = _head.TransformDirection(_fwdLocal);
                delta = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(cur, want), _w);
            }
            if (_tell > 0.001f) delta = Quaternion.AngleAxis(-tellPitch * _tell * (_d && _d.HeavyAttack ? 1.4f : 1f), _model.right) * delta;
            _head.rotation = delta * _head.rotation;
            _lastSet = _head.localRotation; _haveLast = true;
        }

        void Eyes(Transform target, bool alive, float dt)
        {
            if (!_eyeL && !_eyeR) return;
            float open = 1f;
            if (!alive) open = 0.3f;
            else
            {
                if (_blinkT < 0f && Time.time > _nextBlink) { _blinkT = 0f; _nextBlink = Time.time + Random.Range(blinkEvery.x, blinkEvery.y); }
                if (_blinkT >= 0f)
                {
                    _blinkT += dt; float t = _blinkT / 0.16f;
                    open = t < 0.5f ? Mathf.Lerp(1f, 0.1f, t * 2f) : Mathf.Lerp(0.1f, 1f, (t - 0.5f) * 2f);
                    if (t >= 1f) { _blinkT = -1f; open = 1f; }
                }
            }
            Eye(_eyeL, _eyeRestL, _eyeScaleL, target, open);
            Eye(_eyeR, _eyeRestR, _eyeScaleR, target, open);
        }

        void Eye(Transform e, Quaternion rest, Vector3 scale, Transform target, float open)
        {
            if (!e) return;
            e.localScale = new Vector3(scale.x, scale.y * open, scale.z);
            Quaternion restW = (e.parent ? e.parent.rotation : Quaternion.identity) * rest;
            if (target && _w > 0.001f)
            {
                Vector3 to = target.position + Vector3.up * 1.5f - e.position;
                var look = Quaternion.RotateTowards(restW, Quaternion.LookRotation(to, _model.up), eyeGlance);
                e.rotation = Quaternion.Slerp(restW, look, _w);
            }
            else e.localRotation = rest;
        }
    }
}
