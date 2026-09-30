using System;

namespace PrimalFrontier.Core
{
    /// <summary>
    /// The weather schedule without any engine code (WeatherManager drives it; tools can simulate it): which state comes
    /// next, for how long, how heavy a rain is, and when a storm may happen. Times in in-game hours on the game clock.
    /// Storms: only from a rain that starts, with <see cref="Rules.stormChance"/>, never before
    /// <see cref="NextStormAllowed"/> (new game: firstStormAfterHours; after any storm: its end + minHoursBetweenStorms),
    /// so two storms never follow each other. A storm is a heavy rain lead-in, the storm, then a normal rain tail.
    /// </summary>
    public sealed class WeatherPlanner
    {
        public enum RainKind { None, Light, Normal, Heavy, StormLead, Storm, StormTail }

        /// <summary>plain copy of the WeatherConfig schedule numbers (x = min, y = max of a range)</summary>
        public struct Rules
        {
            public float cloudChancePerHour, rainChancePerHour, lightRainShare, heavyRainShare, heavyRainBuild, stormChance;
            public float firstStormAfterHours, minHoursBetweenStorms;
            public float cloudMin, cloudMax, afterRainMin, afterRainMax, rainMin, rainMax, heavyMin, heavyMax;
            public float leadMin, leadMax, stormMin, stormMax, tailMin, tailMax;
            public float lightRain, normalRain, heavyRain, stormRain;
        }

        public Rules rules;
        public WeatherState State { get; private set; } = WeatherState.Clear;
        public RainKind Kind { get; private set; } = RainKind.None;
        /// <summary>when the current state started / ends (hours; Until &lt; 0 = until changed)</summary>
        public double Start { get; private set; }
        public double Until { get; private set; } = -1;
        public double NextStormAllowed { get; set; }
        /// <summary>storms that started (random or set), for tools</summary>
        public int StormCount { get; private set; }
        readonly Func<float> _rnd;
        double _nextRoll = -1, _last = -1;

        public WeatherPlanner(Rules r, Func<float> rnd) { rules = r; _rnd = rnd ?? (() => 0.5f); NextStormAllowed = r.firstStormAfterHours; }

        float R(float a, float b) => a + (b - a) * _rnd();

        /// <summary>
        /// advance to 'now' (hours). Ends a timed state, and once per hour (allowRandom) rolls clouds / rain / storms.
        /// A clock that went back (new game) resets the storm cooldown. Returns true when the state or rain kind changed.
        /// </summary>
        public bool Tick(double now, bool allowRandom)
        {
            bool changed = false;
            CheckClock(now);
            if (Until >= 0 && now >= Until) { EndCurrent(now); changed = true; }
            if (_nextRoll < 0) _nextRoll = now + 1;
            if (allowRandom && now >= _nextRoll)
            {
                _nextRoll = now + 1;
                if (State == WeatherState.Clear && _rnd() < rules.cloudChancePerHour) { Go(WeatherState.Cloudy, RainKind.None, now, R(rules.cloudMin, rules.cloudMax)); changed = true; }
                else if (State == WeatherState.Cloudy && _rnd() < rules.rainChancePerHour) { StartRain(now); changed = true; }
            }
            else if (!allowRandom) _nextRoll = now + 1;
            return changed;
        }

        /// <summary>the game clock went back (new game): storms wait firstStormAfterHours again, a timed state keeps its time left</summary>
        void CheckClock(double now)
        {
            if (_last >= 0 && now < _last - 0.5)
            {
                NextStormAllowed = now + rules.firstStormAfterHours; _nextRoll = now + 1;
                Until = Until >= 0 ? now + Math.Max(0.1, Until - _last) : -1; Start = now;
            }
            _last = now;
        }

        void StartRain(double now)
        {
            if (now >= NextStormAllowed && _rnd() < rules.stormChance) { Go(WeatherState.Rain, RainKind.StormLead, now, R(rules.leadMin, rules.leadMax)); return; }
            float k = _rnd();
            if (k < rules.lightRainShare) Go(WeatherState.Rain, RainKind.Light, now, R(rules.rainMin, rules.rainMax));
            else if (k < rules.lightRainShare + rules.heavyRainShare) Go(WeatherState.Rain, RainKind.Heavy, now, R(rules.heavyMin, rules.heavyMax));
            else Go(WeatherState.Rain, RainKind.Normal, now, R(rules.rainMin, rules.rainMax));
        }

        void EndCurrent(double now)
        {
            switch (Kind)
            {
                case RainKind.StormLead: Go(WeatherState.Storm, RainKind.Storm, now, R(rules.stormMin, rules.stormMax)); return;
                case RainKind.Storm: Go(WeatherState.Rain, RainKind.StormTail, now, R(rules.tailMin, rules.tailMax)); return;
            }
            if (State == WeatherState.Rain || State == WeatherState.Storm) Go(WeatherState.Cloudy, RainKind.None, now, R(rules.afterRainMin, rules.afterRainMax));
            else Go(WeatherState.Clear, RainKind.None, now, -1);
        }

        void Go(WeatherState s, RainKind k, double now, double hours)
        {
            if (State == WeatherState.Storm && s != WeatherState.Storm) NextStormAllowed = Math.Max(NextStormAllowed, now + rules.minHoursBetweenStorms);
            if (s == WeatherState.Storm && State != WeatherState.Storm) StormCount++;
            State = s; Kind = k; Start = now; Until = hours > 0 ? now + hours : -1;
        }

        /// <summary>set a state by hand (story, tests, load): rain = normal rain, storm = full storm; hours &lt;= 0 = until changed</summary>
        public void Set(WeatherState s, double now, double hours)
        {
            CheckClock(now);
            var k = s == WeatherState.Rain ? RainKind.Normal : s == WeatherState.Storm ? RainKind.Storm : RainKind.None;
            Go(s, k, now, hours);
        }

        /// <summary>restore an exact plan (save section)</summary>
        public void Restore(WeatherState s, RainKind k, double now, double hoursLeft, double hoursDone, double stormAllowedIn)
        {
            State = s; Kind = k; Start = now - Math.Max(0, hoursDone); Until = hoursLeft > 0 ? now + hoursLeft : -1;
            NextStormAllowed = now + Math.Max(0, stormAllowedIn); _last = now; _nextRoll = now + 1;
        }

        /// <summary>0..1 of the current timed state (0 when open-ended)</summary>
        public float Progress(double now) => Until > Start ? (float)Math.Min(1, Math.Max(0, (now - Start) / (Until - Start))) : 0f;

        /// <summary>rain intensity the sky is heading to (before the natural swell)</summary>
        public float TargetRain(double now)
        {
            switch (Kind)
            {
                case RainKind.Light: return rules.lightRain;
                case RainKind.Normal: case RainKind.StormTail: return rules.normalRain;
                case RainKind.Heavy:
                    {
                        // starts as a normal rain, turns heavy after heavyRainBuild of its time: only a long heavy rain douses an open fire
                        float p = Progress(now), b = rules.heavyRainBuild;
                        float k = b <= 0f ? 1f : Clamp01((p - b) / 0.1f);
                        return rules.normalRain + (rules.heavyRain - rules.normalRain) * k * k * (3f - 2f * k);
                    }
                case RainKind.StormLead: return rules.heavyRain;
                case RainKind.Storm: return rules.stormRain;
            }
            return State == WeatherState.Rain ? rules.normalRain : State == WeatherState.Storm ? rules.stormRain : 0f;
        }

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
