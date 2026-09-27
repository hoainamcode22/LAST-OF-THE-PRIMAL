using UnityEngine;
using PrimalFrontier.Audio;

namespace PrimalFrontier.VFX
{
    /// <summary>
    /// Presentation of a campfire: looping flames / smoke / embers, one flickering point light (the only realtime light
    /// the fire adds), crackle loop, ignition / extinguish bursts and cooking smoke. Gameplay calls SetLit / SetCooking.
    /// </summary>
    public class CampfireFx : MonoBehaviour
    {
        public ParticleSystem flames, smoke, embers;
        public Light fireLight;
        public AudioSource crackle;
        public float lightIntensity = 2.2f, lightRange = 7f;
        public bool startLit;
        public bool IsLit { get; private set; }
        PooledEffect _cook; float _seed; float _level;

        void Awake()
        {
            _seed = Random.value * 100f;
            if (fireLight) { fireLight.shadows = LightShadows.None; fireLight.renderMode = LightRenderMode.Auto; }
            SetLit(startLit, false);
        }

        public void SetLit(bool on, bool withBurst = true)
        {
            if (IsLit == on && _level == (on ? 1f : 0f)) return;
            IsLit = on;
            foreach (var ps in new[] { flames, smoke, embers })
            {
                if (!ps) continue;
                if (on) ps.Play(true); else ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            if (crackle) { if (on) { if (!crackle.isPlaying) crackle.Play(); } else crackle.Stop(); }
            if (withBurst && Application.isPlaying)
            {
                VfxPool.Instance.Play(on ? VfxId.FireIgnite : VfxId.FireExtinguish, transform.position + Vector3.up * 0.15f, Vector3.up);
                SfxPlayer.Instance.Play(on ? SfxId.FireIgnite : SfxId.FireExtinguish, transform.position);
            }
            if (!on) SetCooking(false);
        }

        public void SetCooking(bool on)
        {
            if (on && _cook == null && IsLit) { _cook = VfxPool.Instance.Play(VfxId.CookSmoke, transform.position + Vector3.up * 0.55f, Quaternion.identity, transform); SfxPlayer.Instance.Play(SfxId.Sizzle, transform.position, 0.8f); }
            else if (!on && _cook != null) { _cook.StopEmitting(); _cook = null; }
        }

        void Update()
        {
            _level = Mathf.MoveTowards(_level, IsLit ? 1f : 0f, Time.deltaTime * (IsLit ? 1.5f : 0.7f));
            if (fireLight)
            {
                float n = Mathf.PerlinNoise(_seed, Time.time * 6f) * 0.6f + Mathf.PerlinNoise(_seed + 3f, Time.time * 17f) * 0.4f;
                fireLight.intensity = _level * lightIntensity * (0.75f + 0.5f * n);
                fireLight.range = lightRange * (0.9f + 0.15f * n);
                fireLight.enabled = _level > 0.01f;
            }
            if (crackle) crackle.volume = 0.6f * _level;
        }
    }
}
