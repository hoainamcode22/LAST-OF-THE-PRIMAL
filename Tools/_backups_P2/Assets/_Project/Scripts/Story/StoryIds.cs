using System;
using System.Collections.Generic;
using System.Text;
using PrimalFrontier.Core;

namespace PrimalFrontier.Story
{
    /// <summary>
    /// Id rules shared by missions, journal, minimap and voice: location ids (LOCATIONS.md ids such as "beach" and the legacy
    /// zone names such as "ZONE_StartBeach" map to one id), discovery ids ("claw_marks_2" = "claw_marks"), '|' patterns with
    /// a '*' prefix wildcard, and GameEventType names resolved at run time (so data can name events another agent appends later).
    /// </summary>
    public static class StoryIds
    {
        /// <summary>the location ids of the PC phase (PC_ROUND_OWNERSHIP interfaces)</summary>
        public static readonly string[] Locations =
        {
            "beach", "shipwreck", "forest", "deep_forest", "river", "waterfall", "meadow", "canyon", "wetland", "cave", "ridge",
            "volcano", "predator_territory", "herbivore_valley", "old_camp", "fossil_bed", "nest", "migration_view",
        };

        static readonly Dictionary<string, string> LegacyLocations = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "start_beach", "beach" }, { "startbeach", "beach" }, { "wreck", "shipwreck" }, { "ship_wreck", "shipwreck" },
            { "oldcamp", "old_camp" }, { "fossils", "fossil_bed" }, { "deepforest", "deep_forest" }, { "herbivore_territory", "herbivore_valley" },
        };
        static readonly Dictionary<string, string> _locCache = new Dictionary<string, string>(StringComparer.Ordinal);
        static readonly Dictionary<string, string> _discCache = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>"ZONE_StartBeach" / "LOC_DeepForest" / "beach" -> "beach" / "deep_forest" (null stays null)</summary>
        public static string Location(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            if (_locCache.TryGetValue(raw, out var c)) return c;
            string s = raw.Trim();
            foreach (var pre in new[] { "ZONE_", "Zone_", "zone_", "LOC_", "Loc_", "loc_", "LOCATION_" })
                if (s.StartsWith(pre, StringComparison.Ordinal)) { s = s.Substring(pre.Length); break; }
            s = Snake(s);
            if (LegacyLocations.TryGetValue(s, out var mapped)) s = mapped;
            _locCache[raw] = s;
            return s;
        }

        /// <summary>"Claw_Marks_2" -> "claw_marks"; ids are compared lower case without a trailing number</summary>
        public static string Discovery(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            if (_discCache.TryGetValue(raw, out var c)) return c;
            string s = Snake(raw.Trim());
            int k = s.Length;
            while (k > 0 && char.IsDigit(s[k - 1])) k--;
            if (k < s.Length && k > 1 && (s[k - 1] == '_' || s[k - 1] == '-')) s = s.Substring(0, k - 1);
            if (s.StartsWith("env_", StringComparison.Ordinal)) s = s.Substring(4);          // ENV's prop ids (LOCATIONS.md)
            if (DiscoveryAliases.TryGetValue(s, out var alias)) s = alias;
            _discCache[raw] = s;
            return s;
        }

        /// <summary>ENV's storytelling prop ids (LOCATIONS.md, without "env_") that share a page with a story id</summary>
        static readonly Dictionary<string, string> DiscoveryAliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "scattered_bones", "bones" }, { "nest_eggs", "nest" }, { "old_camp_firering", "old_firepit" }, { "old_camp_fire_ring", "old_firepit" },
            { "old_camp_shelter", "old_shelter" }, { "old_camp_tool", "old_tools" }, { "markings_ridge", "strange_markings" },
            { "markings_cave", "cave_markings" }, { "wreck_remains", "shipwreck_remains" }, { "giant_footprints", "footprint" },
        };

        /// <summary>CamelCase / spaces / dashes -> snake_case, lower case</summary>
        public static string Snake(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length + 4);
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == ' ' || ch == '-') { if (sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_'); continue; }
                if (char.IsUpper(ch))
                {
                    bool prevLower = i > 0 && (char.IsLower(s[i - 1]) || char.IsDigit(s[i - 1]));
                    bool nextLower = i + 1 < s.Length && char.IsLower(s[i + 1]) && i > 0 && char.IsUpper(s[i - 1]);
                    if ((prevLower || nextLower) && sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
                    sb.Append(char.ToLowerInvariant(ch));
                }
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        /// <summary>
        /// value against "a|b|pre*" (case-insensitive); empty pattern or "*" = anything. A null value only matches an empty pattern.
        /// </summary>
        public static bool Matches(string value, string patterns)
        {
            if (string.IsNullOrEmpty(patterns) || patterns == "*") return true;
            if (string.IsNullOrEmpty(value)) return false;
            int start = 0;
            while (start <= patterns.Length)
            {
                int bar = patterns.IndexOf('|', start); if (bar < 0) bar = patterns.Length;
                int len = bar - start;
                if (len > 0)
                {
                    bool prefix = patterns[bar - 1] == '*';
                    int plen = prefix ? len - 1 : len;
                    if (prefix ? value.Length >= plen && string.Compare(value, 0, patterns, start, plen, StringComparison.OrdinalIgnoreCase) == 0
                               : value.Length == plen && string.Compare(value, 0, patterns, start, plen, StringComparison.OrdinalIgnoreCase) == 0) return true;
                }
                start = bar + 1;
            }
            return false;
        }

        /// <summary>location id against a pattern list of location ids (both sides normalised)</summary>
        public static bool LocationMatches(string zoneId, string patterns)
        {
            if (string.IsNullOrEmpty(patterns) || patterns == "*") return true;
            var loc = Location(zoneId);
            foreach (var p in Split(patterns)) if (p.EndsWith("*") ? loc.StartsWith(Location(p.TrimEnd('*')), StringComparison.Ordinal) : Location(p) == loc) return true;
            return false;
        }

        static readonly Dictionary<string, string[]> _split = new Dictionary<string, string[]>(StringComparer.Ordinal);
        /// <summary>"a|b" -> [a, b] (cached, no empty parts)</summary>
        public static string[] Split(string patterns)
        {
            if (string.IsNullOrEmpty(patterns)) return Array.Empty<string>();
            if (_split.TryGetValue(patterns, out var a)) return a;
            var parts = patterns.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
            _split[patterns] = parts;
            return parts;
        }

        // ------------------------------------------------------------------ event names
        static readonly Dictionary<string, HashSet<GameEventType>> _events = new Dictionary<string, HashSet<GameEventType>>(StringComparer.Ordinal);

        /// <summary>
        /// GameEventType names "Drank|WaterFilled" -> the set of values that exist in this build (a name another agent has not
        /// appended yet is ignored, so data can already name it)
        /// </summary>
        public static HashSet<GameEventType> Events(string names)
        {
            names ??= "";
            if (_events.TryGetValue(names, out var set)) return set;
            set = new HashSet<GameEventType>();
            foreach (var n in Split(names)) if (Enum.TryParse(n, true, out GameEventType t) && Enum.IsDefined(typeof(GameEventType), t)) set.Add(t);
            _events[names] = set;
            return set;
        }
        public static bool EventIs(GameEventType t, string names) => Events(names).Contains(t);
        /// <summary>the value of an event name, when this build has it</summary>
        public static bool TryEvent(string name, out GameEventType t) => Enum.TryParse(name, true, out t) && Enum.IsDefined(typeof(GameEventType), t);

        /// <summary>readable place name for texts: "deep_forest" -> "the deep forest"</summary>
        public static string PlaceName(string id)
        {
            var t = StoryTexts.Location(Location(id));
            if (t != null && !string.IsNullOrEmpty(t.place)) return t.place;
            return string.IsNullOrEmpty(id) ? "" : "the " + Location(id).Replace('_', ' ');
        }
    }
}
