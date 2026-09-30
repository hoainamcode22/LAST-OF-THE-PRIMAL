using UnityEngine;
using PrimalFrontier.Core;
using PrimalFrontier.Player;

namespace PrimalFrontier.AI
{
    /// <summary>awareness of the player, from low to high (AI/PERCEPTION_DESIGN.md 6.3)</summary>
    public enum AwarenessLevel { Unaware, Suspicious, Investigating, Alerted, Engaged }
    public enum SenseKind : byte { None, Sight, Noise, Motion, Scent, Herd, Touch, Damage }

    /// <summary>
    /// Senses of one creature (owned by its DinosaurController, ticked from its timed Think, never per frame):
    /// sight (effective range from the player's light / posture / motion / cover, FOV, one budgeted linecast), hearing
    /// (new noises in the Stimuli ring), motion flashes (shaking bushes), scent (puffs, every Nth tick). Builds one
    /// awareness meter (threat = the player) with hysteresis and decay, one interest meter (food scent), a memory of
    /// the last known position and a look point. Noise and scent give a position, not an identity: without sight the
    /// meter stops below Engaged. Allocation free.
    /// </summary>
    public sealed class DinoSenses
    {
        public float Awareness { get; private set; }
        public float Interest { get; private set; }
        public AwarenessLevel Level { get; private set; }
        public bool SeesPlayer { get; private set; }
        public float LastSeenTime { get; private set; } = -99f;
        public bool HasMemory { get; private set; }
        public Vector3 LastKnownPos { get; private set; }
        public float LastKnownTime { get; private set; } = -99f;
        public SenseKind LastSense { get; private set; }
        public bool HasLookPoint { get; private set; }
        public Vector3 LookPoint { get; private set; }
        public bool HasInterest => Interest > 0.001f && _hasInterestPos;
        public Vector3 InterestPos { get; private set; }
        /// <summary>herbivores: blood / meat / smoke smelled here (walk away from it)</summary>
        public bool HasAversion => Stimuli.Now - _aversionTime < 8f;
        public Vector3 AversionPos { get; private set; }
        /// <summary>last computed effective sight range (m), for tests / gizmos</summary>
        public float EffectiveSight { get; private set; }
        /// <summary>following the player by smell (Engaged and within scentTrackRange)</summary>
        public bool ScentTracking { get; private set; }
        /// <summary>sight / hearing multipliers set by the owner (sleep)</summary>
        public float SightMul = 1f, HearingMul = 1f;

        int _lastSeq = -1, _tick; float _lastStimulus = -99f, _aversionTime = -99f;
        bool _hasInterestPos; Vector3 _cooldownPos; float _cooldownUntil = -99f;

        // ------------------------------------------------------------------ frame budget / counters
        static int _losFrame = -1, _losCount, _tickFrame = -1, _tickCount;
        public static int LosThisFrame => _losFrame == Time.frameCount ? _losCount : 0;
        public static int TicksThisFrame => _tickFrame == Time.frameCount ? _tickCount : 0;
        public static int LosTotal { get; private set; }
        /// <summary>milliseconds spent in Tick this frame (all creatures)</summary>
        public static float TickMsThisFrame => _tickFrame == Time.frameCount ? (float)(_tickTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency) : 0f;
        static long _tickTicks;
        /// <summary>profiling totals since the last ResetStats: sensing time (ms), the worst frame (ms), most linecasts in one frame</summary>
        public static double TotalTickMs { get; private set; }
        public static float MaxFrameTickMs { get; private set; }
        public static int MaxLosInAFrame { get; private set; }
        public static void ResetStats() { TotalTickMs = 0; MaxFrameTickMs = 0f; MaxLosInAFrame = 0; }
        /// <summary>tests: wind (world xz direction, strength) and rain 0..1 instead of the WeatherManager</summary>
        public static bool WeatherOverride; public static Vector2 WindDirOverride = new Vector2(0.8f, 0.6f); public static float WindStrengthOverride = 0.35f, RainOverride;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _losFrame = -1; _losCount = 0; _tickFrame = -1; _tickCount = 0; LosTotal = 0; WeatherOverride = false; RainOverride = 0f; WindStrengthOverride = 0.35f; WindDirOverride = new Vector2(0.8f, 0.6f); }

        static bool TakeLos(int max)
        {
            int f = Time.frameCount;
            if (f != _losFrame) { _losFrame = f; _losCount = 0; }
            if (_losCount >= max) return false;
            _losCount++; LosTotal++; if (_losCount > MaxLosInAFrame) MaxLosInAFrame = _losCount; return true;
        }
        static void CountTick()
        {
            int f = Time.frameCount;
            if (f != _tickFrame)
            {
                float last = (float)(_tickTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                if (last > MaxFrameTickMs) MaxFrameTickMs = last;
                _tickFrame = f; _tickCount = 0; _tickTicks = 0;
            }
            _tickCount++;
        }

        /// <summary>current wind (world xz, normalized), strength and rain 0..1</summary>
        public static void Weather(out Vector2 windDir, out float windStrength, out float rain)
        {
            if (WeatherOverride) { windDir = WindDirOverride.sqrMagnitude > 1e-4f ? WindDirOverride.normalized : Vector2.right; windStrength = WindStrengthOverride; rain = RainOverride; return; }
            var w = WeatherManager.Instance;
            if (w) { windDir = w.WindDirection; windStrength = w.WindStrength; rain = w.Intensity; return; }
            windDir = new Vector2(0.8f, 0.6f); windStrength = 0.35f; rain = 0f;
        }

        /// <summary>the light a species sees the player by: night vision lifts the ambient part, never the torch</summary>
        public static float SpeciesLight(in PlayerSignature.Snapshot s, float nightVision) =>
            Mathf.Max(Mathf.Lerp(s.ambient, 1f, Mathf.Clamp01(nightVision)), s.local) * s.beacon * s.caveMul;

        /// <summary>visibility of the player to a species (0..1.5)</summary>
        public static float Visibility(in PlayerSignature.Snapshot s, float nightVision) =>
            Mathf.Clamp(SpeciesLight(s, nightVision) * s.posture * s.motion * (1f - Mathf.Clamp01(s.cover)) * s.exposure, 0f, 1.5f);

        /// <summary>effective sight range (m) of a species for this signature</summary>
        public static float SightRange(DinosaurDefinition def, in PlayerSignature.Snapshot s, PerceptionConfig c, float mul = 1f)
        {
            float alert = def.alertSensitivity > 0f ? def.alertSensitivity : 1f;
            float full = def.sightRange * alert;
            return Mathf.Min(full * Visibility(s, def.nightVision), full * c.rangeCap) * mul;
        }

        /// <summary>hearing radius (m) of a species for a noise of this loudness</summary>
        public static float HearingRadius(DinosaurDefinition def, float loudness, float rain, PerceptionConfig c, float mul = 1f)
        {
            float alert = def.alertSensitivity > 0f ? def.alertSensitivity : 1f;
            return def.hearingRange * alert * loudness * (1f - Mathf.Clamp01(c.rainMask * rain)) * mul;
        }

        public static AwarenessLevel LevelFor(float a, AwarenessLevel current, PerceptionConfig c)
        {
            // rise at the threshold, drop only hysteresis below it
            float h = c.hysteresis;
            AwarenessLevel up = a >= 0.999f ? AwarenessLevel.Engaged : a >= c.alerted ? AwarenessLevel.Alerted : a >= c.investigating ? AwarenessLevel.Investigating : a >= c.suspicious ? AwarenessLevel.Suspicious : AwarenessLevel.Unaware;
            if (up >= current) return up;
            float keep = current == AwarenessLevel.Engaged ? 1f - h : current == AwarenessLevel.Alerted ? c.alerted - h : current == AwarenessLevel.Investigating ? c.investigating - h : c.suspicious - h;
            if (a >= keep) return current;
            return up;
        }

        // ------------------------------------------------------------------ inputs from the owner
        /// <summary>hit by the player (or anything from 'from'): fully aware, knows where it came from</summary>
        public void OnDamaged(Vector3 from)
        {
            float now = Stimuli.Now;
            Awareness = 1f; Level = AwarenessLevel.Engaged; _lastStimulus = now;
            Remember(from, now, SenseKind.Damage); LastSeenTime = now;
        }

        /// <summary>a herd member / pack mate shares what it knows</summary>
        public void Share(float level, Vector3 pos, PerceptionConfig c)
        {
            float now = Stimuli.Now;
            if (level > Awareness) Awareness = Mathf.Clamp01(level);
            _lastStimulus = now; Remember(pos, now, SenseKind.Herd);
            Level = LevelFor(Awareness, Level, c);
        }

        /// <summary>the owner gave up on the food it was after (reached it, or scared away): ignore that spot for a while</summary>
        public void DropInterest(PerceptionConfig c)
        {
            _cooldownPos = InterestPos; _cooldownUntil = Stimuli.Now + c.interestCooldown;
            Interest = 0f; _hasInterestPos = false;
        }

        public void ForgetAll()
        {
            Awareness = 0f; Interest = 0f; Level = AwarenessLevel.Unaware; SeesPlayer = false; HasMemory = false; HasLookPoint = false;
            _hasInterestPos = false; ScentTracking = false; _lastSeq = Stimuli.TransientHead; LastSeenTime = -99f; _aversionTime = -99f;
        }

        void Remember(Vector3 p, float now, SenseKind k)
        {
            LastKnownPos = p; LastKnownTime = now; HasMemory = true; LastSense = k;
            LookPoint = p; HasLookPoint = true;
        }

        // ------------------------------------------------------------------ tick
        /// <summary>
        /// One sensing tick. tier: 0 near, 1 medium (sight + hearing + motion + scent), 2 far (loud noises + scent),
        /// 3 very far (nothing). dt = seconds since the previous tick.
        /// </summary>
        public void Tick(DinosaurController self, float dt, int tier, PerceptionConfig c)
        {
            var def = self.def; if (!def) return;
            CountTick();
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try { TickInner(self, dt, tier, c); }
            finally { long dt0 = System.Diagnostics.Stopwatch.GetTimestamp() - t0; _tickTicks += dt0; TotalTickMs += dt0 * 1000.0 / System.Diagnostics.Stopwatch.Frequency; }
        }

        void TickInner(DinosaurController self, float dt, int tier, PerceptionConfig c)
        {
            var def = self.def;
            float now = Stimuli.Now;
            _tick++;
            if (tier >= 3) { _lastSeq = Stimuli.TransientHead; Decay(def, dt, now, c); return; }
            Vector3 me = self.transform.position;
            bool herbivore = def.temperament == Temperament.Passive || def.temperament == Temperament.Defensive;
            bool stimulus = false;
            Weather(out var windDir, out var windStrength, out var rain);
            var snap = PlayerSignature.Read;

            // ---- sight
            bool saw = false;
            if (tier <= 1 && snap.valid && snap.alive)
            {
                Vector3 to = snap.aimPoint - me; to.y = 0f; float d = to.magnitude;
                float r = SightRange(def, snap, c, SightMul);
                EffectiveSight = r;
                float vis = Visibility(snap, def.nightVision);
                bool touch = d < def.bodyRadius * c.touchBodyMul && vis > c.touchVisibility;
                if (d <= r || touch)
                {
                    Vector3 fwd = self.transform.forward; fwd.y = 0f;
                    bool inFov = d < def.bodyRadius * 3f || Vector3.Angle(fwd, to) <= def.fov * 0.5f;
                    if (inFov)
                    {
                        if (TakeLos(c.maxLosPerFrame))
                        {
                            Vector3 eye = me + Vector3.up * Mathf.Max(1f, def.bodyRadius * 1.4f);
                            saw = d < def.bodyRadius * 4f || !Blocked(eye, snap.aimPoint, self.transform);
                        }
                        else saw = SeesPlayer;                           // over budget: keep last tick's answer
                    }
                }
                if (saw)
                {
                    if (touch) Awareness = 1f;
                    else
                    {
                        float k = r > 0.01f ? Mathf.Clamp01(1f - d / r) : 1f;
                        Awareness = Mathf.Min(1f, Awareness + c.sightRate * def.sightGain * Mathf.Lerp(c.edgeMul, c.closeMul, k) * dt);
                    }
                    LastSeenTime = now; Remember(snap.position, now, SenseKind.Sight); stimulus = true;
                }
            }
            SeesPlayer = saw;

            // ---- hearing + motion (new transient entries only)
            int head = Stimuli.TransientHead;
            int from = Mathf.Max(_lastSeq + 1, Stimuli.OldestTransient);
            for (int seq = from; seq <= head; seq++)
            {
                if (!Stimuli.TryGetTransient(seq, now, out var s) || s.source == StimulusSource.Creature) continue;
                if (s.kind == StimulusKind.Noise)
                {
                    if (tier == 2 && s.strength < 2f) continue;           // far: only very loud noises
                    float r = HearingRadius(def, s.strength, rain, c, HearingMul);
                    float dx = s.pos.x - me.x, dz = s.pos.z - me.z, d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d >= r) continue;
                    float k = 1f - d / r;
                    float w = s.tag == (byte)NoiseTag.Footstep ? c.pulseWeight : 1f;
                    float ceiling = k >= 1f - c.loudCloseFraction ? 0.99f : c.alerted - 0.01f;
                    float bump = def.hearingGain * Mathf.Lerp(c.noiseBumpMin, c.noiseBumpMax, k) * w;
                    if (Awareness < ceiling) Awareness = Mathf.Min(ceiling, Awareness + bump);
                    if (!saw)
                    {
                        float err = c.noiseLocError * (1f - k);
                        Vector2 e = err > 0.01f ? Random.insideUnitCircle * err : Vector2.zero;
                        Remember(new Vector3(s.pos.x + e.x, s.pos.y, s.pos.z + e.y), now, SenseKind.Noise);
                    }
                    stimulus = true;
                }
                else if (s.kind == StimulusKind.Motion && tier <= 1 && !saw)
                {
                    float r = def.sightRange * SpeciesLight(snap.valid ? snap : PlayerSignature.Snapshot.Neutral, def.nightVision) * c.motionRangeMul * s.strength * SightMul;
                    Vector3 to = s.pos - me; to.y = 0f; float d = to.magnitude;
                    if (d >= r) continue;
                    Vector3 fwd = self.transform.forward; fwd.y = 0f;
                    if (Vector3.Angle(fwd, to) > def.fov * 0.5f || !TakeLos(c.maxLosPerFrame)) continue;
                    Vector3 eye = me + Vector3.up * Mathf.Max(1f, def.bodyRadius * 1.4f);
                    // stop just short of the moving thing: the bush's own leaves / collider must not hide it from view
                    Vector3 tgt = s.pos + Vector3.up * 0.5f, dv = tgt - eye; float len = dv.magnitude;
                    if (len > 1.5f) tgt = eye + dv * ((len - 1.2f) / len);
                    if (Blocked(eye, tgt, self.transform)) continue;
                    float k = 1f - d / r, ceiling = c.alerted - 0.01f;
                    if (Awareness < ceiling) Awareness = Mathf.Min(ceiling, Awareness + def.sightGain * Mathf.Lerp(c.motionBumpMin, c.motionBumpMax, k));
                    Remember(s.pos, now, SenseKind.Motion); stimulus = true;
                }
            }
            _lastSeq = head;

            // ---- scent (every Nth tick)
            ScentTracking = false;
            int nth = Mathf.Max(1, c.scentEveryNthTick);
            bool smelledFood = false;
            if (_tick % nth == 0 && def.smellSensitivity > 0f)
            {
                float sdt = dt * nth; var model = c.Puff;
                float bestThreat = 0f, bestFood = 0f, bestAvoid = 0f; Vector3 threatAt = default, foodAt = default, avoidAt = default; float threatAge = 0f, foodAge = 0f;
                int n = Stimuli.ScentSlotsUsed;
                for (int i = 0; i < n; i++)
                {
                    if (!Stimuli.ScentLive(i, now, model.maxAge)) continue;
                    ref readonly var s = ref Stimuli.ScentSlot(i);
                    if (!Stimuli.PuffAt(s, now, windDir, windStrength, rain, model, out var center, out var radius, out var strength)) continue;
                    float conc = Stimuli.Concentration(me, center, radius, strength) * def.smellSensitivity;
                    if (conc < c.smellThreshold) continue;
                    var kind = (ScentKind)s.tag;
                    bool playerMade = s.source == StimulusSource.Player;
                    if (kind == ScentKind.Player || (kind == ScentKind.Blood && playerMade)) { if (conc > bestThreat) { bestThreat = conc; threatAt = s.pos; threatAge = now - s.time; } }
                    if (kind == ScentKind.Meat || kind == ScentKind.Blood) { if (conc > bestFood) { bestFood = conc; foodAt = s.pos; foodAge = now - s.time; } }
                    if (herbivore && kind != ScentKind.Player && conc > bestAvoid) { bestAvoid = conc; avoidAt = s.pos; }
                }
                if (bestThreat > 0f)
                {
                    float ceiling = herbivore ? c.investigating - 0.01f : c.alerted - 0.01f;
                    bool track = !herbivore && Level == AwarenessLevel.Engaged && def.scentTrackRange > 0f && snap.valid &&
                                 (snap.position - me).sqrMagnitude <= def.scentTrackRange * def.scentTrackRange;
                    if (track) { ScentTracking = true; Awareness = 1f; if (!saw) Remember(snap.position, now, SenseKind.Scent); }
                    else
                    {
                        if (Awareness < ceiling) Awareness = Mathf.Min(ceiling, Awareness + c.smellRate * bestThreat * sdt);
                        if (!saw) Remember(Blur(threatAt, threatAge, c), now, SenseKind.Scent);
                    }
                    stimulus = true;
                }
                if (bestFood > 0f && !herbivore && def.meatDrive > 0f)
                {
                    Vector3 at = Blur(foodAt, foodAge, c);
                    bool cooling = now < _cooldownUntil && (at - _cooldownPos).sqrMagnitude < 15f * 15f;
                    if (!cooling)
                    {
                        Interest = Mathf.Min(1f, Interest + c.interestRate * bestFood * def.meatDrive * sdt);
                        InterestPos = at; _hasInterestPos = true; smelledFood = true;
                    }
                }
                if (bestAvoid > 0f) { AversionPos = avoidAt; _aversionTime = now; }
                if (!smelledFood) Interest = Mathf.Max(0f, Interest - c.interestDecay * sdt);
                if (Interest <= 0f) _hasInterestPos = false;
            }

            if (stimulus) _lastStimulus = now;
            Decay(def, dt, now, c);
        }

        static Vector3 Blur(Vector3 origin, float age, PerceptionConfig c)
        {
            float err = Mathf.Min(c.scentLocCap, age * c.scentLocPerSecond);
            if (err < 0.05f) return origin;
            var e = Random.insideUnitCircle * err;
            return new Vector3(origin.x + e.x, origin.y, origin.z + e.y);
        }

        void Decay(DinosaurDefinition def, float dt, float now, PerceptionConfig c)
        {
            if (now - _lastStimulus > c.decayHold) Awareness = Mathf.Max(0f, Awareness - def.awarenessDecay * dt);
            if (HasMemory && now - LastKnownTime > def.memorySeconds) { HasMemory = false; HasLookPoint = false; }
            Level = LevelFor(Awareness, Level, c);
        }

        static class Masks { internal static readonly int NotPlayer = ~LayerMask.GetMask("Player"); }
        static readonly RaycastHit[] LosHits = new RaycastHit[16];

        /// <summary>is the line from a to b blocked by anything except the creature's own colliders (its head hit zones) and triggers</summary>
        static bool Blocked(Vector3 a, Vector3 b, Transform self)
        {
            Vector3 d = b - a; float len = d.magnitude; if (len < 0.01f) return false;
            int n = Physics.RaycastNonAlloc(a, d / len, LosHits, len, Masks.NotPlayer, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) { var col = LosHits[i].collider; if (col && !col.transform.IsChildOf(self)) return true; }
            return false;
        }
    }
}
