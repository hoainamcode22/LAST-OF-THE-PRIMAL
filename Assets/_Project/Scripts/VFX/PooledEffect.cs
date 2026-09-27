using UnityEngine;

namespace PrimalFrontier.VFX
{
    /// <summary>Lives on every effect prefab; the root particle system calls back when all children have finished.</summary>
    public class PooledEffect : MonoBehaviour
    {
        public VfxId Id { get; set; }
        public VfxPool Pool { get; set; }
        [Tooltip("optional safety timeout (s); 0 = rely on the particle system")] public float maxLifetime = 0f;
        ParticleSystem _ps; float _t; bool _running;
        public bool Running => _running;

        void Awake()
        {
            _ps = GetComponent<ParticleSystem>();
            if (_ps) { var m = _ps.main; m.stopAction = ParticleSystemStopAction.Callback; m.playOnAwake = false; }
        }

        public void Begin()
        {
            _t = 0f; _running = true; _deadline = -1f;
            if (_ps) { _ps.Clear(true); _ps.Play(true); }
            var light = GetComponentInChildren<Light>(true); if (light) light.enabled = true;
        }

        /// <summary>stop emitting (looping effects like steam / bleeding); particles fade out, then the effect returns</summary>
        public void StopEmitting()
        {
            if (_ps) _ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            _deadline = _t + 3f;                                  // safety: return even if the callback never comes
        }
        float _deadline = -1f;

        void Update()
        {
            if (!_running) return;
            _t += Time.deltaTime;
            if (maxLifetime > 0f && _t > maxLifetime) Finish();
            else if (_deadline > 0f && _t > _deadline) Finish();
            else if (_ps && !_ps.main.loop && _t > _ps.main.duration + 0.5f && !_ps.IsAlive(true)) Finish();   // missed callback
        }

        void OnParticleSystemStopped() { Finish(); }

        // attached effects (steam on a hand, bleeding on a chest) die with their parent: keep the pool count right
        void OnDestroy() { if (_running && Pool) { _running = false; Pool.Forget(this); } }

        void Finish()
        {
            if (!_running) return;
            _running = false;
            if (Pool) Pool.Return(this); else gameObject.SetActive(false);
        }
    }
}
