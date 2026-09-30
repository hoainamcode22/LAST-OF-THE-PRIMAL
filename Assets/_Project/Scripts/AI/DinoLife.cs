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
    ///   closed after death;
    /// - weight (directive 44, per species DinosaurDefinition.locomotion): the torso rolls from side to side in step with
    ///   the walk clip, leans into turns and pitches when speeding up or braking; the tail swings out against a turn and
    ///   lifts at a run, lagging behind the body. Heavy animals sway slowly and deeply, small ones barely.
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

        [Tooltip("scales the procedural body sway / lean / tail (0 = off)")] [Range(0, 2)] public float weightMotion = 1f;

        DinosaurController _d; Animator _anim; Transform _head, _model, _eyeL, _eyeR;
        Transform _spine; readonly System.Collections.Generic.List<Transform> _tail = new System.Collections.Generic.List<Transform>(12);
        Quaternion[] _tailPre, _tailSet; Quaternion _spinePre, _spineSet; bool _bodyHaveLast;
        float _roll, _lean, _pitchK, _tailYaw, _tailLift;
        static readonly int LocoHash = Animator.StringToHash("Locomotion");
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
                else if (t.name == "Spine_01" && !_spine) _spine = t;
                else if (t.name.StartsWith("Tail_")) _tail.Add(t);
            }
            _tail.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            _tailPre = new Quaternion[_tail.Count]; _tailSet = new Quaternion[_tail.Count];
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
            // no player to watch: look at the last stimulus (a noise, a shaking bush, a smell)
            bool has = target;
            if (target) _lookAt = target.position + Vector3.up * 1.5f;
            else if (_d && alive && _d.TryGetLookPoint(out var lp)) { _lookAt = lp + Vector3.up * 0.8f; has = true; }
            _w = Mathf.MoveTowards(_w, has ? lookWeight : 0f, dt * 1.6f);
            _tell = Mathf.MoveTowards(_tell, _d && _d.Telegraph > 0f ? 1f : 0f, dt * 7f);
            Weight(dt, alive);
            Head(has);
            Eyes(has, alive, dt);
        }

        Vector3 _lookAt;

        /// <summary>body sway, lean, acceleration pitch and tail balance on top of the clip (restored first when the clip did not write the bones)</summary>
        void Weight(float dt, bool alive)
        {
            if (!_d || !_d.def || (!_spine && _tail.Count == 0)) return;
            if (_bodyHaveLast)
            {
                if (_spine && _spine.localRotation == _spineSet) _spine.localRotation = _spinePre;
                for (int i = 0; i < _tail.Count; i++) if (_tail[i] && _tail[i].localRotation == _tailSet[i]) _tail[i].localRotation = _tailPre[i];
            }
            _bodyHaveLast = false;
            if (!alive || weightMotion <= 0f || (_anim && !_anim.enabled)) { _roll = _lean = _pitchK = _tailYaw = _tailLift = 0f; return; }
            var def = _d.def; var lp = def.locomotion;
            float speed = _d.CurrentSpeed, walk = Mathf.Max(0.1f, def.walkSpeed), run = Mathf.Max(walk + 0.1f, def.runSpeed);
            float mv = Mathf.Clamp01(speed / walk);
            // gait phase from the locomotion clip, so the sway stays in step with the feet
            float phase;
            var st = _anim ? _anim.GetCurrentAnimatorStateInfo(0) : default;
            if (_anim && st.shortNameHash == LocoHash) phase = st.normalizedTime;
            else phase = Time.time * speed / Mathf.Max(0.5f, def.tracks.stride);
            float runK = Mathf.Clamp01((speed - walk) / (run - walk));
            float sway = Mathf.Sin(phase * Mathf.PI * 2f) * lp.swayDegrees * mv * Mathf.Lerp(1f, 0.55f, runK);
            float lean = Mathf.Clamp(_d.YawRate / 90f * lp.leanDegrees * Mathf.Clamp01(speed / (walk * 2f)), -lp.leanDegrees, lp.leanDegrees);
            float pitch = Mathf.Clamp(_d.Accel / Mathf.Max(0.5f, def.acceleration), -1f, 1f) * lp.accelPitch;
            float tail = Mathf.Clamp(_d.YawRate / 100f * lp.tailBalance, -lp.tailBalance * 1.5f, lp.tailBalance * 1.5f) - sway * 1.2f;
            float lift = runK * 7f * (1f - def.Weight01 * 0.5f);
            // heavy animals follow slowly: the sway is deep and late, the tail lags behind the turn
            float heavy = def.Weight01;
            _roll = Mathf.Lerp(_roll, sway, Mathf.Clamp01(dt * Mathf.Lerp(14f, 7f, heavy)));
            _lean = Mathf.Lerp(_lean, lean, Mathf.Clamp01(dt * Mathf.Lerp(6f, 2.5f, heavy)));
            _pitchK = Mathf.Lerp(_pitchK, pitch, Mathf.Clamp01(dt * 3f));
            _tailYaw = Mathf.Lerp(_tailYaw, tail, Mathf.Clamp01(dt * Mathf.Lerp(5f, 2.2f, heavy)));
            _tailLift = Mathf.Lerp(_tailLift, lift, Mathf.Clamp01(dt * 2f));
            float k = weightMotion;
            Vector3 fwd = _model.forward, up = _model.up, right = _model.right;
            if (_spine)
            {
                _spinePre = _spine.localRotation;
                _spine.rotation = Quaternion.AngleAxis((_roll + _lean) * k, fwd) * Quaternion.AngleAxis(_pitchK * k, right) * _spine.rotation;
                _spineSet = _spine.localRotation;
            }
            int n = _tail.Count;
            for (int i = 0; i < n; i++)
            {
                var b = _tail[i]; if (!b) continue;
                _tailPre[i] = b.localRotation;
                float share = 1f / n;
                b.rotation = Quaternion.AngleAxis(_tailYaw * share * k, up) * Quaternion.AngleAxis(_tailLift * share * k, right) * b.rotation;
                _tailSet[i] = b.localRotation;
            }
            _bodyHaveLast = true;
        }

        void Head(bool target)
        {
            if (!_head) return;
            // animation did not write the head this frame (culled / disabled): undo last frame's offset first
            if (_haveLast && _head.localRotation == _lastSet) _head.localRotation = _pre;
            _haveLast = false;
            if (_w <= 0.001f && _tell <= 0.001f) return;
            if (_anim && !_anim.enabled) return;
            _pre = _head.localRotation;
            Quaternion delta = Quaternion.identity;
            if (_w > 0.001f)                  // fades out on the last look point when the target goes
            {
                Vector3 to = _lookAt - _head.position;
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

        void Eyes(bool target, bool alive, float dt)
        {
            if (!_eyeL && !_eyeR) return;
            float open = 1f;
            if (!alive) open = 0.3f;
            else if (_d && _d.Sleeping) open = 0.12f;           // asleep: eyes closed
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

        void Eye(Transform e, Quaternion rest, Vector3 scale, bool target, float open)
        {
            if (!e) return;
            e.localScale = new Vector3(scale.x, scale.y * open, scale.z);
            Quaternion restW = (e.parent ? e.parent.rotation : Quaternion.identity) * rest;
            if (_w > 0.001f)
            {
                Vector3 to = _lookAt - e.position;
                var look = Quaternion.RotateTowards(restW, Quaternion.LookRotation(to, _model.up), eyeGlance);
                e.rotation = Quaternion.Slerp(restW, look, _w);
            }
            else e.localRotation = rest;
        }
    }
}
