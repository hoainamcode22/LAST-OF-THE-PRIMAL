using UnityEngine;
using PrimalFrontier.Audio;

namespace PrimalFrontier.VFX
{
    /// <summary>
    /// Presentation of a campfire: looping flames / smoke / embers, one flickering point light (the only realtime light
    /// the fire adds), crackle loop, ignition / extinguish bursts, cooking smoke while food cooks and a steam wisp over
    /// every slot whose food (or boiled water) is ready. Gameplay calls SetLit / SetCooking / SetReady / BurnPuff.
    /// Effects come from VfxPool (no Instantiate after warm-up); steam only shows while the fire burns.
    /// </summary>
    public class CampfireFx : MonoBehaviour
    {
        public ParticleSystem flames, smoke, embers;
        public Light fireLight;
        public AudioSource crackle;
        public float lightIntensity = 2.2f, lightRange = 7f;
        public bool startLit;
        [Tooltip("size of the steam wisp over ready food")] public float readySteamScale = 0.7f;
        public bool IsLit { get; private set; }
        PooledEffect _cook; float _seed; float _level;

        const int MaxSteam = 8;
        PooledEffect[] _steam; Transform[] _steamAt; bool[] _steamWanted;   // per cooking slot (allocated on first use)

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
            Loop(flames, on); Loop(smoke, on); Loop(embers, on);
            if (crackle) { if (on) { if (!crackle.isPlaying) crackle.Play(); } else crackle.Stop(); }
            if (withBurst && Application.isPlaying)
            {
                VfxPool.Instance.Play(on ? VfxId.FireIgnite : VfxId.FireExtinguish, transform.position + Vector3.up * 0.15f, Vector3.up);
                SfxPlayer.Instance.Play(on ? SfxId.FireIgnite : SfxId.FireExtinguish, transform.position);
            }
            if (!on) SetCooking(false);
            if (_steamWanted != null) for (int i = 0; i < MaxSteam; i++) ApplySteam(i);
        }

        static void Loop(ParticleSystem ps, bool on)
        {
            if (!ps) return;
            if (on) ps.Play(true); else ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        /// <summary>cooking smoke + sizzle while something cooks (only while lit)</summary>
        public void SetCooking(bool on)
        {
            if (!Application.isPlaying) return;
            if (on && _cook == null && IsLit) { _cook = VfxPool.Instance.Play(VfxId.CookSmoke, transform.position + Vector3.up * 0.55f, Quaternion.identity, transform); SfxPlayer.Instance.Play(SfxId.Sizzle, transform.position, 0.8f); }
            else if (!on && _cook != null) { _cook.StopEmitting(); _cook = null; }
        }

        /// <summary>steam over one slot (ready food, boiled water); shown only while the fire burns</summary>
        public void SetReady(int slot, Transform at, bool on)
        {
            if (slot < 0 || slot >= MaxSteam) return;
            if (_steamWanted == null)
            {
                if (!on) return;
                _steam = new PooledEffect[MaxSteam]; _steamAt = new Transform[MaxSteam]; _steamWanted = new bool[MaxSteam];
            }
            _steamWanted[slot] = on; _steamAt[slot] = at;
            ApplySteam(slot);
        }

        void ApplySteam(int i)
        {
            bool show = _steamWanted[i] && IsLit && _steamAt[i] && Application.isPlaying;
            var e = _steam[i];
            if (show && e == null) _steam[i] = VfxPool.Instance.Play(VfxId.Steam, _steamAt[i].position + Vector3.up * 0.05f, Vector3.up, _steamAt[i], readySteamScale);
            else if (!show && e != null) { e.StopEmitting(); _steam[i] = null; }
        }

        /// <summary>food burned: a grey puff and a hiss at the slot</summary>
        public void BurnPuff(Vector3 at)
        {
            if (!Application.isPlaying) return;
            VfxPool.Instance.Play(VfxId.FireExtinguish, at, Vector3.up, null, 0.35f);
            SfxPlayer.Instance.Play(SfxId.Sizzle, at, 0.5f);
        }

        /// <summary>water boiled / food ready: a short sizzle</summary>
        public void ReadyCue(Vector3 at)
        {
            if (!Application.isPlaying) return;
            SfxPlayer.Instance.Play(SfxId.Sizzle, at, 0.6f);
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
