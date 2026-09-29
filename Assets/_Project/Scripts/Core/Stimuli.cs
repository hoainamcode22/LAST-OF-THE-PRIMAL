using System;
using UnityEngine;

namespace PrimalFrontier.Core
{
    public enum StimulusKind : byte { Noise, Motion, Scent }
    /// <summary>who made it: creatures ignore other creatures' stimuli (except herd alarms, handled by the AI)</summary>
    public enum StimulusSource : byte { Player, Creature, World, Distraction }
    public enum NoiseTag : byte { Footstep, Rustle, Chop, Mine, Gather, TreeFall, Build, Combat, Impact, Landing, Voice, Fire }
    public enum ScentKind : byte { Meat, Blood, Smoke, Player }

    /// <summary>one noise, motion flash or scent puff (stored by value in a ring)</summary>
    public struct Stimulus
    {
        /// <summary>noise / motion: where; scent: where the puff was released</summary>
        public Vector3 pos;
        /// <summary>clock time at emission (<see cref="Stimuli.Now"/>)</summary>
        public float time;
        /// <summary>noise: loudness as a multiple of the listener's hearing range; motion: 0..1.5; scent: puff strength</summary>
        public float strength;
        /// <summary>monotonic number of the entry in its ring</summary>
        public int seq;
        public StimulusKind kind;
        public StimulusSource source;
        /// <summary><see cref="NoiseTag"/> for noise / motion, <see cref="ScentKind"/> for scent</summary>
        public byte tag;
    }

    /// <summary>how a scent puff drifts, spreads and fades (from PerceptionConfig)</summary>
    public struct PuffModel
    {
        public float drift;        // m/s per unit of wind strength
        public float radius0;      // m at release
        public float spread;       // m/s radius growth
        public float tau;          // s, strength e-folding time
        public float rainCut;      // 0..1 strength lost at full rain
        public float maxAge;       // s
    }

    /// <summary>
    /// The perception hub (static, like <see cref="GameEvents"/>): gameplay writes noises, motion flashes and scent puffs;
    /// creatures read them on their own timed sensing ticks. Two fixed rings of structs: transient (noise + motion, short
    /// life) and scent (puffs, long life), so a burst of footsteps never evicts smells. Writing is O(1) and allocation
    /// free; the oldest entry is overwritten. Readers keep the last sequence number they saw. A puff is only its release
    /// record: where it has drifted and how strong it still is are computed when read (<see cref="PuffAt"/>).
    /// </summary>
    public static class Stimuli
    {
        public const int TransientCapacity = 128, ScentCapacity = 256;
        /// <summary>seconds a noise / motion stays readable (at least one far sensing tick)</summary>
        public static float TransientLife = 1.5f;

        static readonly Stimulus[] _transient = new Stimulus[TransientCapacity];
        static readonly Stimulus[] _scent = new Stimulus[ScentCapacity];
        static int _tNext, _sNext;                 // sequence number of the next entry
        static int _tValidFrom, _sValidFrom;       // Clear: entries below these sequence numbers are gone
        static int _frame = -1, _emitted;

        /// <summary>tests: replaces Time.time (null = Time.time)</summary>
        public static Func<float> Clock;
        public static float Now => Clock != null ? Clock() : Time.time;
        /// <summary>newest transient sequence number (-1 = none yet)</summary>
        public static int TransientHead => _tNext - 1;
        /// <summary>scent puffs written since start (ring slots in use = min(this, capacity))</summary>
        public static int ScentWritten => _sNext;
        /// <summary>entries written this frame (profiling)</summary>
        public static int EmittedThisFrame => _frame == Time.frameCount ? _emitted : 0;
        /// <summary>the last player-made noise (HUD ring): loudness and time</summary>
        public static float LastPlayerNoise { get; private set; }
        public static float LastPlayerNoiseTime { get; private set; } = -99f;
        /// <summary>the AI hooks loud noises (ambient flyers startle); (position, loudness)</summary>
        public static Action<Vector3, float> LoudNoise;
        /// <summary>loudness at or above which <see cref="LoudNoise"/> is raised</summary>
        public static float LoudNoiseThreshold = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Clock = null; _tNext = 0; _sNext = 0; _tValidFrom = 0; _sValidFrom = 0; LastPlayerNoise = 0f; LastPlayerNoiseTime = -99f; LoudNoise = null; TransientLife = 1.5f; LoudNoiseThreshold = 1f; }

        // ------------------------------------------------------------------ write
        public static void Noise(Vector3 p, float loudness, NoiseTag tag, StimulusSource src)
        {
            if (loudness <= 0f) return;
            WriteTransient(p, loudness, StimulusKind.Noise, src, (byte)tag);
            if (src == StimulusSource.Player) { LastPlayerNoise = loudness; LastPlayerNoiseTime = Now; }
            if (loudness >= LoudNoiseThreshold) LoudNoise?.Invoke(p, loudness);
        }

        /// <summary>something visibly moved there (a shaking bush): creatures with line of sight look at it</summary>
        public static void Motion(Vector3 p, float strength, StimulusSource src, NoiseTag tag = NoiseTag.Rustle)
        {
            if (strength <= 0f) return;
            WriteTransient(p, strength, StimulusKind.Motion, src, (byte)tag);
        }

        public static void Scent(Vector3 origin, float strength, ScentKind kind, StimulusSource src)
        {
            if (strength <= 0f) return;
            int i = _sNext % ScentCapacity;
            _scent[i] = new Stimulus { pos = origin, time = Now, strength = strength, seq = _sNext, kind = StimulusKind.Scent, source = src, tag = (byte)kind };
            _sNext++; Count();
        }

        static void WriteTransient(Vector3 p, float s, StimulusKind k, StimulusSource src, byte tag)
        {
            int i = _tNext % TransientCapacity;
            _transient[i] = new Stimulus { pos = p, time = Now, strength = s, seq = _tNext, kind = k, source = src, tag = tag };
            _tNext++; Count();
        }

        static void Count()
        {
            int f = Time.frameCount;
            if (f != _frame) { _frame = f; _emitted = 0; }
            _emitted++;
        }

        /// <summary>forget everything (new game, load, after sleeping). Sequence numbers keep counting so readers stay in step.</summary>
        public static void Clear() { _tValidFrom = _tNext; _sValidFrom = _sNext; LastPlayerNoise = 0f; LastPlayerNoiseTime = -99f; }

        // ------------------------------------------------------------------ read
        /// <summary>the transient entry with this sequence number, if it is still in the ring and not expired</summary>
        public static bool TryGetTransient(int seq, float now, out Stimulus s)
        {
            s = default;
            if (seq < _tValidFrom || seq >= _tNext || seq < _tNext - TransientCapacity) return false;
            s = _transient[seq % TransientCapacity];
            return s.seq == seq && now - s.time <= TransientLife && now >= s.time - 0.001f;
        }

        /// <summary>first sequence number still in the ring (readers that fell behind jump here)</summary>
        public static int OldestTransient => Mathf.Max(_tValidFrom, _tNext - TransientCapacity);

        /// <summary>slot i (0..ScentCapacity-1) of the scent ring; check <see cref="ScentLive"/></summary>
        public static ref readonly Stimulus ScentSlot(int i) => ref _scent[i];
        /// <summary>is scent slot i a puff that can still be smelled</summary>
        public static bool ScentLive(int i, float now, float maxAge)
        {
            if (i >= _sNext) return false;
            ref readonly var s = ref _scent[i];
            return s.seq >= _sValidFrom && now - s.time <= maxAge && now >= s.time - 0.001f && s.strength > 0f;
        }
        public static int ScentSlotsUsed => Mathf.Min(_sNext, ScentCapacity);

        // ------------------------------------------------------------------ pure maths (DinoSenses, tests)
        /// <summary>
        /// Where a puff is now and how strong it is: centre = origin + wind x drift x strength x age (downwind = the way the
        /// wind blows, as _PF_Wind), radius = r0 + spread x age, strength = s0 x exp(-age / tau) x (1 - rainCut x rain).
        /// windDir is the world xz direction.
        /// </summary>
        public static bool PuffAt(in Stimulus s, float now, Vector2 windDir, float windStrength, float rain, in PuffModel m,
                                  out Vector3 center, out float radius, out float strength)
        {
            float age = Mathf.Max(0f, now - s.time);
            float drift = m.drift * Mathf.Max(0f, windStrength) * age;
            center = new Vector3(s.pos.x + windDir.x * drift, s.pos.y, s.pos.z + windDir.y * drift);
            radius = m.radius0 + m.spread * age;
            strength = age > m.maxAge ? 0f : s.strength * Mathf.Exp(-age / Mathf.Max(0.01f, m.tau)) * (1f - Mathf.Clamp01(m.rainCut * rain));
            return strength > 0f;
        }

        /// <summary>concentration at x of a puff (quadratic fall-off, horizontal distance)</summary>
        public static float Concentration(Vector3 x, Vector3 center, float radius, float strength)
        {
            float dx = x.x - center.x, dz = x.z - center.z;
            float q = (dx * dx + dz * dz) / Mathf.Max(0.01f, radius * radius);
            return q >= 1f ? 0f : strength * (1f - q);
        }
    }
}
