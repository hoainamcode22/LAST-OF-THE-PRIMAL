using UnityEngine;
using PrimalFrontier.Animation;

namespace PrimalFrontier.Player
{
    /// <summary>
    /// Face on the LOD0 mesh: natural blinking plus expressions picked from what the body is doing
    /// (pain when hit, effort while working / attacking, shout on heavy attacks, eyes closed asleep / dead / unconscious).
    /// Blendshapes (from Blender): Blink, Blink_L, Blink_R, Squint, Angry, Shout, Pain, Smile, Effort, JawOpen.
    /// </summary>
    public class PlayerFacial : MonoBehaviour
    {
        public SkinnedMeshRenderer face;
        public Animator animator;
        public float blendSpeed = 8f;

        int _blink, _angry, _shout, _pain, _effort, _squint, _jaw;
        float _nextBlink, _blinkT = -1f;
        float _wBlink, _wAngry, _wShout, _wPain, _wEffort, _wSquint, _wJaw;
        float _painUntil;

        void Awake()
        {
            if (!animator) animator = GetComponentInChildren<Animator>();
            if (!face)
                foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (r.sharedMesh && r.sharedMesh.blendShapeCount > 0) { face = r; break; }
            if (face)
            {
                var m = face.sharedMesh;
                _blink = m.GetBlendShapeIndex("Blink"); _angry = m.GetBlendShapeIndex("Angry"); _shout = m.GetBlendShapeIndex("Shout");
                _pain = m.GetBlendShapeIndex("Pain"); _effort = m.GetBlendShapeIndex("Effort"); _squint = m.GetBlendShapeIndex("Squint"); _jaw = m.GetBlendShapeIndex("JawOpen");
            }
            _nextBlink = Time.time + Random.Range(1.5f, 4f);
        }

        public void OnHit(bool heavy) { _painUntil = Time.time + (heavy ? 1.4f : 0.7f); }

        void LateUpdate()
        {
            if (!face || !face.isVisible) return;
            float dt = Time.deltaTime;
            float tBlink = 0, tAngry = 0, tShout = 0, tPain = 0, tEffort = 0, tSquint = 0.15f, tJaw = 0;
            bool eyesClosed = false;
            if (animator)
            {
                var st = animator.GetCurrentAnimatorStateInfo(0);
                if (st.IsName("Unconscious") || st.IsName("Sleep") || st.IsTag("Dead")) eyesClosed = true;
                else if (st.IsName("Wake_Up")) { float k = Mathf.Clamp01(st.normalizedTime * 180f / 45f); tBlink = 1f - k; tPain = 0.5f * (1f - Mathf.Abs(st.normalizedTime - 0.45f) * 2.2f); }
                else if (st.IsName("Attack_Spear_Heavy") || st.IsName("Throw_Spear")) { tShout = 0.8f; tAngry = 0.4f; }
                else if (st.IsTag("Attack")) { tEffort = 0.8f; tAngry = 0.5f; }
                else if (st.IsName("Gather_Wood") || st.IsName("Gather_Stone") || st.IsName("Build")) tEffort = 0.6f;
                else if (st.IsName("Eat")) tJaw = 0.25f + 0.25f * Mathf.Sin(Time.time * 14f);
                else if (st.IsName("Sprint") || animator.GetFloat(AnimParams.Speed) > 5f) tEffort = 0.35f;
                if (st.IsTag("Hurt")) _painUntil = Mathf.Max(_painUntil, Time.time + 0.3f);
            }
            if (Time.time < _painUntil) { tPain = 1f; tEffort = 0f; tShout = 0f; }
            // blinking
            if (!eyesClosed)
            {
                if (_blinkT < 0f && Time.time >= _nextBlink) _blinkT = 0f;
                if (_blinkT >= 0f)
                {
                    _blinkT += dt / 0.14f;
                    tBlink = Mathf.Max(tBlink, Mathf.Sin(Mathf.Clamp01(_blinkT) * Mathf.PI));
                    if (_blinkT >= 1f) { _blinkT = -1f; _nextBlink = Time.time + Random.Range(2f, 6f); if (Random.value < 0.15f) _nextBlink = Time.time + 0.25f; }
                }
            }
            else tBlink = 1f;
            float k2 = 1f - Mathf.Exp(-blendSpeed * dt);
            _wBlink = _blinkT >= 0f ? tBlink : Mathf.Lerp(_wBlink, tBlink, k2 * 2f);
            _wAngry = Mathf.Lerp(_wAngry, tAngry, k2); _wShout = Mathf.Lerp(_wShout, tShout, k2); _wPain = Mathf.Lerp(_wPain, tPain, k2 * 1.5f);
            _wEffort = Mathf.Lerp(_wEffort, tEffort, k2); _wSquint = Mathf.Lerp(_wSquint, tSquint, k2); _wJaw = Mathf.Lerp(_wJaw, tJaw, k2 * 2f);
            Set(_blink, _wBlink); Set(_angry, _wAngry); Set(_shout, _wShout); Set(_pain, _wPain); Set(_effort, _wEffort); Set(_squint, _wSquint); Set(_jaw, _wJaw);
        }

        void Set(int i, float w) { if (i >= 0) face.SetBlendShapeWeight(i, Mathf.Clamp01(w) * 100f); }
    }
}
