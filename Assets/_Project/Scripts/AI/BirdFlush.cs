using UnityEngine;
using PrimalFrontier.Audio;
using PrimalFrontier.VFX;

namespace PrimalFrontier.AI
{
    /// <summary>
    /// Small birds bursting out of the canopy (a migrating herd pushing past, a heavy animal crashing through): one pooled
    /// particle system of two-frame flapping silhouettes that rise, scatter and fade. Cheap enough to read from the lookout
    /// at 150 m; made on first use.
    /// </summary>
    public class BirdFlush : MonoBehaviour
    {
        static BirdFlush _inst;
        ParticleSystem _ps;
        public static int Bursts { get; private set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() { _inst = null; Bursts = 0; }

        static BirdFlush Instance
        {
            get
            {
                if (!_inst && Application.isPlaying) { var go = new GameObject("[BirdFlush]"); _inst = go.AddComponent<BirdFlush>(); _inst.Init(); }
                return _inst;
            }
        }

        void Init()
        {
            _ps = gameObject.AddComponent<ParticleSystem>();
            var main = _ps.main;
            main.playOnAwake = false;
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (!_ps.isPlaying) main.duration = 1f;
            main.loop = true;                          // kept playing with emission off: Emit() adds the flocks
            main.startLifetime = new ParticleSystem.MinMaxCurve(3.2f, 5f);
            main.startSpeed = 0f; main.startSize = new ParticleSystem.MinMaxCurve(0.45f, 0.7f);
            main.gravityModifier = -0.04f; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 160; main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.startColor = Color.white;
            var em = _ps.emission; em.enabled = false;
            var sh = _ps.shape; sh.enabled = false;
            var noise = _ps.noise; noise.enabled = true; noise.strength = 1.1f; noise.frequency = 0.45f; noise.scrollSpeed = 0.3f; noise.damping = true;
            var drag = _ps.limitVelocityOverLifetime; drag.enabled = true; drag.dampen = 0.04f; drag.limit = 7f;
            var col = _ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.06f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var tsa = _ps.textureSheetAnimation; tsa.enabled = true; tsa.numTilesX = 2; tsa.numTilesY = 1;
            tsa.animation = ParticleSystemAnimationType.WholeSheet; tsa.cycleCount = 14;
            tsa.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
            var r = GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard; r.sharedMaterial = TrackArt.BirdMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            r.maxParticleSize = 0.2f; r.minParticleSize = 0.003f;          // still a few pixels from the lookout
            _ps.Play();
        }

        /// <summary>a flock of 'count' birds leaves the canopy at 'at', flying off roughly along 'away'</summary>
        public static void Burst(Vector3 at, Vector3 away, int count = 9)
        {
            var b = Instance; if (!b || !b._ps) return;
            away.y = 0f; if (away.sqrMagnitude < 1e-4f) away = Random.onUnitSphere; away.y = 0f; away.Normalize();
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                ep.position = at + Random.insideUnitSphere * 1.8f;
                var v = away * Random.Range(3.5f, 7f) + Vector3.up * Random.Range(2.2f, 4.2f) + Random.insideUnitSphere * 1.6f;
                ep.velocity = v;
                ep.startLifetime = Random.Range(3.2f, 5f);
                ep.startSize = Random.Range(0.45f, 0.7f);
                b._ps.Emit(ep, 1);
            }
            Bursts++;
            // the canopy shakes as they go (leaves) and a rustle nearby
            VfxPool.Instance.Play(VfxId.Leaves, at, Vector3.up, null, 1.4f);
            SfxPlayer.Instance.Play(SfxId.LeafRustle, at, 0.55f);
            float r2 = 70f * 70f;
            foreach (var a in AmbientCreature.All) if (a && a.IsAlive && (a.transform.position - at).sqrMagnitude < r2) a.Startle(at);
        }
    }
}
