using UnityEngine;
using PrimalFrontier.Audio;
using PrimalFrontier.Core;

namespace PrimalFrontier.World
{
    /// <summary>
    /// The smoking mountain across the sea (scenery, not reachable). Drives its effects: the smoke plume drifts with the
    /// weather wind, the crater glow and embers show at dusk and night, and every few minutes the mountain rumbles
    /// (a low distant boom, a faint camera tremor and a darker puff of smoke). All particle systems are children
    /// placed by the builder; move / retune them in the scene.
    /// </summary>
    public class VolcanoLandmark : MonoBehaviour
    {
        public ParticleSystem smoke, embers, glow;
        [Tooltip("small halos along the lava flow (night)")] public ParticleSystem[] flowGlows = new ParticleSystem[0];
        public Renderer lava;
        [Tooltip("real seconds between rumbles")] public Vector2 rumbleEvery = new Vector2(240f, 540f);
        [Range(0, 1)] public float rumbleVolume = 0.4f;
        public Color lavaEmission = new Color(4f, 1.1f, 0.15f);
        public float embersNight = 10f, embersDay = 1.5f;

        float _nextRumble; MaterialPropertyBlock _mpb; ParticleSystem.Particle[] _glowP;
        static readonly int WindId = Shader.PropertyToID("_PF_Wind"), EmissionId = Shader.PropertyToID("_EmissionColor");

        void Start()
        {
            _nextRumble = Time.time + Random.Range(90f, 200f);
            _mpb = new MaterialPropertyBlock();
        }

        void Update()
        {
            var tm = TimeManager.Instance;
            float day = tm ? tm.Daylight01 : 1f;                 // 0 night .. 1 full day
            float night = 1f - Mathf.Clamp01(day * 1.4f);
            // smoke drifts with the wind (stronger up high)
            if (smoke)
            {
                Vector4 w = Shader.GetGlobalVector(WindId);
                float s = (w.z > 0f ? w.z : 0.4f) * 7f;
                var v = smoke.velocityOverLifetime;
                v.x = new ParticleSystem.MinMaxCurve(w.x * s * 0.6f, w.x * s * 1.3f);
                v.z = new ParticleSystem.MinMaxCurve(w.y * s * 0.6f, w.y * s * 1.3f);
            }
            if (embers) { var em = embers.emission; em.rateOverTime = Mathf.Lerp(embersDay, embersNight, night); }
            // long-lived halo particles over the crater and along the flow: faint by day, strong at night
            Halo(glow, Mathf.Lerp(0.05f, 0.38f, night));
            foreach (var g in flowGlows) Halo(g, Mathf.Lerp(0.0f, 0.2f, night));
            if (lava)
            {
                lava.GetPropertyBlock(_mpb);
                float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 0.7f) * Mathf.Sin(Time.time * 1.9f);
                _mpb.SetColor(EmissionId, lavaEmission * pulse * Mathf.Lerp(0.6f, 1.3f, night));
                lava.SetPropertyBlock(_mpb);
            }
            if (Time.time >= _nextRumble) Rumble();
        }

        void Halo(ParticleSystem ps, float alpha)
        {
            if (!ps) return;
            if (_glowP == null) _glowP = new ParticleSystem.Particle[1];
            if (ps.GetParticles(_glowP) > 0)
            {
                var c = (Color)_glowP[0].startColor; c.a = alpha;
                _glowP[0].startColor = c; ps.SetParticles(_glowP, 1);
            }
        }

        public void Rumble()
        {
            _nextRumble = Time.time + Random.Range(rumbleEvery.x, rumbleEvery.y);
            var a = SfxPlayer.Instance.Play(SfxId.Thunder, Camera.main ? Camera.main.transform.position : transform.position, rumbleVolume, 0f);
            if (a) a.pitch = 0.5f;
            var cam = Camera.main ? Camera.main.GetComponent<Player.ThirdPersonCamera>() : null;
            if (cam) cam.AddShake(0.012f, 1.6f);
            if (smoke)
            {
                var p = new ParticleSystem.EmitParams { startColor = new Color(0.12f, 0.11f, 0.11f, 0.7f), startSize = 150f, startLifetime = 50f };
                smoke.Emit(p, 3);
            }
            if (embers) embers.Emit(25);
            GameEvents.Raise(GameEventType.VolcanoRumble, "volcano", 1, transform.position);
        }
    }
}
