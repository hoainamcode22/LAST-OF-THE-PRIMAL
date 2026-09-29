using UnityEngine;
using PrimalFrontier.Audio;

namespace PrimalFrontier.VFX
{
    /// <summary>
    /// Presentation of a campfire: looping flames / smoke / embers, one flickering point light (the only realtime light
    /// the fire adds), crackle loop, ignition / extinguish bursts, cooking smoke while food cooks and a steam wisp over
    /// every slot whose food (or boiled water) is ready. Gameplay calls SetLit / SetCooking / SetReady / BurnPuff.
    /// Effects come from VfxPool (no Instantiate after warm-up); steam only shows while the fire burns.
    /// Phase 3: SetIntensity scales flames, embers, smoke, light and crackle with the fire's strength (fuel, lighting,
    /// rain); SetBoiling plays BoilBubbles bursts (about once a second) and the seamless WaterBoil loop on its own
    /// AudioSource while water heats; Doused = the hiss and the grey puff when rain puts the fire out.
    /// </summary>
    public class CampfireFx : MonoBehaviour
    {
        public ParticleSystem flames, smoke, embers;
        public Light fireLight;
        public AudioSource crackle;
        public float lightIntensity = 2.2f, lightRange = 7f;
        public bool startLit;
        [Tooltip("size of the steam wisp over ready food")] public float readySteamScale = 0.7f;
        [Tooltip("seconds between BoilBubbles bursts while water heats")] public float boilBurstEvery = 1f;
        public bool IsLit { get; private set; }
        /// <summary>current strength 0..1 (SetIntensity)</summary>
        public float Intensity { get; private set; } = 1f;
        public bool IsBoiling => _boilAt;
        PooledEffect _cook; float _seed; float _level;
        // base values of the particle systems (strength scales them)
        float _flameRate = -1f, _flameSize, _emberRate, _smokeRate;
        Transform _boilAt; float _nextBubble; AudioSource _boilSrc; static AudioClip _boilClip; static bool _boilClipLooked;

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
            if (!on) { SetCooking(false); SetBoiling(null); }
            if (_steamWanted != null) for (int i = 0; i < MaxSteam; i++) ApplySteam(i);
        }

        void CacheBase()
        {
            if (_flameRate >= 0f) return;
            _flameRate = flames ? flames.emission.rateOverTimeMultiplier : 0f; _flameSize = flames ? flames.main.startSizeMultiplier : 1f;
            _emberRate = embers ? embers.emission.rateOverTimeMultiplier : 0f; _smokeRate = smoke ? smoke.emission.rateOverTimeMultiplier : 0f;
        }

        /// <summary>fire strength 0..1: smaller, sparser flames and a dimmer light when low on fuel, lighting or rained on</summary>
        public void SetIntensity(float k)
        {
            k = Mathf.Clamp01(k);
            Intensity = k;
            CacheBase();
            float f = Mathf.Lerp(0.25f, 1f, k);
            if (flames) { var em = flames.emission; em.rateOverTimeMultiplier = _flameRate * f; var m = flames.main; m.startSizeMultiplier = _flameSize * Mathf.Lerp(0.55f, 1f, k); }
            if (embers) { var em = embers.emission; em.rateOverTimeMultiplier = _emberRate * Mathf.Lerp(0.2f, 1f, k); }
            // a weak or rained-on fire smokes about as much: keep most of it
            if (smoke) { var em = smoke.emission; em.rateOverTimeMultiplier = _smokeRate * Mathf.Lerp(0.7f, 1f, k); }
        }

        /// <summary>water heating on the slot at <paramref name="at"/> (null = nothing boils): bubbles + the boil loop</summary>
        public void SetBoiling(Transform at)
        {
            if (!Application.isPlaying) return;
            bool on = at && IsLit;
            _boilAt = on ? at : null;
            if (on)
            {
                if (!_boilSrc)
                {
                    _boilSrc = gameObject.AddComponent<AudioSource>();
                    _boilSrc.loop = true; _boilSrc.playOnAwake = false; _boilSrc.spatialBlend = 1f; _boilSrc.rolloffMode = AudioRolloffMode.Linear;
                    _boilSrc.minDistance = 1f; _boilSrc.maxDistance = 12f; _boilSrc.dopplerLevel = 0f;
                }
                var clip = BoilClip();
                if (clip && (!_boilSrc.isPlaying || _boilSrc.clip != clip))
                {
                    _boilSrc.clip = clip; _boilSrc.volume = 0.55f * PrimalFrontier.Core.AudioBus.Sfx; _boilSrc.Play();
                }
                if (_nextBubble <= Time.time) _nextBubble = Time.time;             // first burst right away
            }
            else if (_boilSrc && _boilSrc.isPlaying) _boilSrc.Stop();
        }

        /// <summary>the WaterBoil clip from the SFX library (loops seamlessly); null until the clip exists</summary>
        static AudioClip BoilClip()
        {
            if (_boilClip || _boilClipLooked) return _boilClip;
            _boilClipLooked = true;
            var lib = Resources.Load<SfxLibrary>("SfxLibrary");
            if (lib != null) foreach (var e in lib.entries) if (e.id == SfxId.WaterBoil && e.clips != null && e.clips.Length > 0) { _boilClip = e.clips[0]; break; }
            return _boilClip;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() { _boilClip = null; _boilClipLooked = false; }

        /// <summary>rain put the fire out: hiss + grey puff (the fire goes out without the normal extinguish burst)</summary>
        public void Doused()
        {
            if (!Application.isPlaying) return;
            VfxPool.Instance.Play(VfxId.FireExtinguish, transform.position + Vector3.up * 0.2f, Vector3.up, null, 1.2f);
            SfxPlayer.Instance.Play(SfxId.FireHiss, transform.position, 1f);
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
            float want = IsLit ? Mathf.Lerp(0.3f, 1f, Intensity) : 0f;
            _level = Mathf.MoveTowards(_level, want, Time.deltaTime * (IsLit ? 1.5f : 0.7f));
            if (fireLight)
            {
                float n = Mathf.PerlinNoise(_seed, Time.time * 6f) * 0.6f + Mathf.PerlinNoise(_seed + 3f, Time.time * 17f) * 0.4f;
                fireLight.intensity = _level * lightIntensity * (0.75f + 0.5f * n);
                fireLight.range = lightRange * Mathf.Lerp(0.6f, 1f, _level) * (0.9f + 0.15f * n);
                fireLight.enabled = _level > 0.01f;
            }
            if (crackle) crackle.volume = 0.6f * _level;
            if (_boilAt && Time.time >= _nextBubble && Application.isPlaying)
            {
                _nextBubble = Time.time + Mathf.Max(0.3f, boilBurstEvery);
                VfxPool.Instance.Play(VfxId.BoilBubbles, _boilAt.position + Vector3.up * 0.06f, Vector3.up, null, 0.5f);
            }
        }
    }
}
