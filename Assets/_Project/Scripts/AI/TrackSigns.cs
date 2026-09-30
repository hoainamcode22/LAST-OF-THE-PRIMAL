using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using PrimalFrontier.Core;
using PrimalFrontier.World;

namespace PrimalFrontier.AI
{
    public enum TrackKind : byte { Footprint, Flattened, Scratch, Droppings, Blood, Kill }

    /// <summary>
    /// Tracking signs (directive 41): footprints along the paths creatures really walk, flattened plants behind a moving herd,
    /// claw scratches on trunks in predator territories, droppings where herds graze, blood drops from wounded animals and
    /// old kill sites. "This creature was here recently": signs fade over game hours (faster in the rain) and the player can
    /// examine them (TrackSignSpot) for a short thought, which raises TracksFound (and the matching DISCOVERIES page).
    /// One ring buffer, no GameObject per sign: everything near the camera is drawn with Graphics.RenderMeshInstanced from
    /// one decal atlas (four fade steps) and two shared meshes. Nothing allocates after warm-up except examining.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class TrackSigns : MonoBehaviour
    {
        struct Sign
        {
            public Matrix4x4 m, m2;
            public Vector3 pos;
            public double born; public float life;          // wear hours at birth, life in wear hours
            public TrackKind kind; public byte cell; public short species;
            public bool used, examined, permanent, mirror;
        }

        static TrackSigns _inst;
        /// <summary>the scene's sign store (made on first use while playing)</summary>
        public static TrackSigns Instance
        {
            get
            {
                if (!_inst && Application.isPlaying) { var go = new GameObject("[TrackSigns]"); _inst = go.AddComponent<TrackSigns>(); }
                return _inst;
            }
        }
        public static bool Exists => _inst;

        static readonly List<DinosaurDefinition> Species = new List<DinosaurDefinition>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() { _inst = null; Species.Clear(); _water = null; }

        Sign[] _signs; int _next, _count;
        readonly Dictionary<int, double> _readUntil = new Dictionary<int, double>();          // (species, kind) -> wear hour until which it is not offered again
        double _wear; double _lastClock = -1;
        bool _seeded; float _seedAt;
        TrackSignSpot _spot;
        readonly List<Matrix4x4>[] _decal = new List<Matrix4x4>[TrackArt.CellCount * 8];      // (cell, mirrored, fade step)
        readonly List<Matrix4x4> _dungNew = new List<Matrix4x4>(64), _dungOld = new List<Matrix4x4>(64), _bones = new List<Matrix4x4>(16);
        float _nextRebuild; Camera _cam;
        public int Count => _count;
        /// <summary>signs drawn this frame (debug / tests)</summary>
        public int Visible { get; private set; }

        void Awake()
        {
            if (_inst && _inst != this) { Destroy(this); return; }
            _inst = this;
            var c = WildlifeConfig.Instance;
            _signs = new Sign[Mathf.Clamp(c.capacity, 64, 4096)];
            for (int i = 0; i < _decal.Length; i++) _decal[i] = new List<Matrix4x4>(32);
            _seedAt = Time.time + 1.5f;
            _spot = new GameObject("TrackSignSpot").AddComponent<TrackSignSpot>();
            _spot.transform.SetParent(transform, false);
        }
        void OnDestroy() { if (_inst == this) _inst = null; }

        // ------------------------------------------------------------------ adding signs
        static short SpeciesIndex(DinosaurDefinition d)
        {
            if (!d) return -1;
            int i = Species.IndexOf(d); if (i >= 0) return (short)i;
            Species.Add(d); return (short)(Species.Count - 1);
        }
        public static DinosaurDefinition SpeciesOf(int sign) { var t = _inst; if (!t || sign < 0 || sign >= t._signs.Length) return null; int s = t._signs[sign].species; return s >= 0 && s < Species.Count ? Species[s] : null; }

        int Alloc()
        {
            int n = _signs.Length;
            for (int k = 0; k < n; k++)
            {
                int i = (_next + k) % n;
                if (!_signs[i].used) { _next = (i + 1) % n; _count++; return i; }
            }
            // full: reuse the oldest sign that is not a permanent landmark
            int best = -1; double born = double.MaxValue;
            for (int i = 0; i < n; i++) if (!_signs[i].permanent && _signs[i].born < born) { born = _signs[i].born; best = i; }
            if (best < 0) best = _next;
            _next = (best + 1) % n;
            return best;
        }

        int Add(TrackKind kind, DinosaurDefinition def, Vector3 pos, Matrix4x4 m, byte cell, float lifeHours, double ageHours, bool permanent, bool mirror = false)
        {
            if (_signs == null) return -1;
            int i = Alloc();
            _signs[i] = new Sign { m = m, m2 = Matrix4x4.zero, pos = pos, born = _wear - ageHours, life = lifeHours, kind = kind, cell = cell, species = SpeciesIndex(def), used = true, permanent = permanent, mirror = mirror };
            _nextRebuild = 0f;
            return i;
        }

        static float FreshLife => WildlifeConfig.Instance.freshHours + WildlifeConfig.Instance.fadeHours;

        /// <summary>one footprint: bodyPos = the body centre on the ground, fwd = its heading, left / right foot</summary>
        public static void Step(DinosaurDefinition def, Vector3 bodyPos, Vector3 fwd, bool left, double ageHours = 0)
        {
            if (!def || def.tracks.printSize <= 0f) return;
            var t = Instance; if (!t) return;
            var tp = def.tracks;
            fwd.y = 0f; if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward; fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 p = bodyPos + right * (left ? -tp.gauge : tp.gauge) * 0.5f;
            if (!Ground(p, out var at, out var n)) return;
            float yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg + (left ? -7f : 7f) + Random.Range(-4f, 4f);
            float s = tp.printSize * Random.Range(0.94f, 1.06f);
            var rot = Quaternion.FromToRotation(Vector3.up, n) * Quaternion.Euler(0f, yaw, 0f);
            var m = Matrix4x4.TRS(at + n * 0.035f, rot, new Vector3(s, 1f, s));
            t.Add(TrackKind.Footprint, def, at, m, (byte)CellFor(tp.shape), FreshLife, ageHours, false, left);       // the left print is the mirror image
        }

        static TrackArt.Cell CellFor(FootShape s) => s switch
        {
            FootShape.Dromaeosaur => TrackArt.Cell.Dromaeosaur, FootShape.Hadrosaur => TrackArt.Cell.Hadrosaur,
            FootShape.Ceratopsian => TrackArt.Cell.Ceratopsian, FootShape.Ankylosaur => TrackArt.Cell.Ankylosaur, _ => TrackArt.Cell.Theropod,
        };

        /// <summary>plants pressed flat where a heavy animal pushed through (herds on the move)</summary>
        public static void Flatten(DinosaurDefinition def, Vector3 pos, float yawDeg, float size, double ageHours = 0)
        {
            var t = Instance; if (!t || !Ground(pos, out var at, out var n)) return;
            var rot = Quaternion.FromToRotation(Vector3.up, n) * Quaternion.Euler(0f, yawDeg + Random.Range(-12f, 12f), 0f);
            var m = Matrix4x4.TRS(at + n * 0.04f, rot, new Vector3(size * 0.8f, 1f, size));
            t.Add(TrackKind.Flattened, def, at, m, (byte)TrackArt.Cell.Flattened, FreshLife * 1.6f, ageHours, false);
        }

        /// <summary>drops of blood from a wounded animal (off with the blood setting)</summary>
        public static void Blood(DinosaurDefinition def, Vector3 pos, double ageHours = 0)
        {
            if (GameSettings.Blood == BloodLevel.Off) return;
            var t = Instance; if (!t || !Ground(pos, out var at, out var n)) return;
            float s = Random.Range(0.35f, 0.7f);
            var m = Matrix4x4.TRS(at + n * 0.03f, Quaternion.FromToRotation(Vector3.up, n) * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), new Vector3(s, 1f, s));
            t.Add(TrackKind.Blood, def, at, m, (byte)TrackArt.Cell.Blood, FreshLife * 1.4f, ageHours, false);
        }

        /// <summary>a dung pile behind a grazing animal</summary>
        public static void Droppings(DinosaurDefinition def, Vector3 pos, double ageHours = 0, float lifeHours = 72f)
        {
            if (!def || def.tracks.droppingsSize <= 0f) return;
            var t = Instance; if (!t || !Ground(pos, out var at, out var n)) return;
            float s = def.tracks.droppingsSize * Random.Range(0.85f, 1.15f);
            var rot = Quaternion.FromToRotation(Vector3.up, n) * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            var m = Matrix4x4.TRS(at - n * 0.03f, rot, new Vector3(s, s * Random.Range(0.8f, 1.1f), s));
            t.Add(TrackKind.Droppings, def, at, m, 0, lifeHours, ageHours, false);
        }

        /// <summary>claw grooves on a trunk: point = on the bark, outward = away from the trunk centre (horizontal)</summary>
        public static void Scratch(DinosaurDefinition def, Vector3 point, Vector3 outward, float size, bool permanent = true)
        {
            var t = Instance; if (!t) return;
            outward.y = 0f; if (outward.sqrMagnitude < 1e-4f) return; outward.Normalize();
            var rot = Quaternion.LookRotation(Vector3.up, outward);             // quad +Z (texture up) = world up, quad normal = outward
            bool small = def && def.bodyRadius < 0.8f;
            var m = Matrix4x4.TRS(point + outward * 0.03f, rot, new Vector3(size * 0.62f, 1f, size));
            t.Add(TrackKind.Scratch, def, point, m, (byte)(small ? TrackArt.Cell.ScratchSmall : TrackArt.Cell.Scratch), permanent ? 1e6f : FreshLife * 20f, 0, permanent);
        }

        /// <summary>an old kill: a dark stain and picked bones (and the killer's prints around it)</summary>
        public static void Kill(DinosaurDefinition killer, Vector3 pos, float size, bool permanent = true)
        {
            var t = Instance; if (!t || !Ground(pos, out var at, out var n)) return;
            var up = Quaternion.FromToRotation(Vector3.up, n);
            var m = Matrix4x4.TRS(at + n * 0.03f, up * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), new Vector3(size, 1f, size));
            int i = t.Add(TrackKind.Kill, killer, at, m, (byte)TrackArt.Cell.Stain, permanent ? 1e6f : 400f, 0, permanent);
            if (i >= 0) t._signs[i].m2 = Matrix4x4.TRS(at + n * 0.02f, up * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), Vector3.one * size * 0.55f);
        }

        // ------------------------------------------------------------------ ground
        /// <summary>fresh water surface height per 3 m cell (a point lower than it is under water: no prints there)</summary>
        static Dictionary<Vector2Int, float> _water; static int _waterSources = -1;

        static bool Ground(Vector3 p, out Vector3 at, out Vector3 n)
        {
            at = p; n = Vector3.up;
            var t = Terrain.activeTerrain; if (!t || !t.terrainData) return false;
            var tp = t.transform.position; var size = t.terrainData.size;
            float u = (p.x - tp.x) / size.x, v = (p.z - tp.z) / size.z;
            if (u < 0f || v < 0f || u > 1f || v > 1f) return false;
            at.y = t.SampleHeight(p) + tp.y;
            if (at.y < 0.55f) return false;                                     // the sea
            n = t.terrainData.GetInterpolatedNormal(u, v);
            if (n.y < 0.72f) return false;                                      // steeper than about 44 degrees: nothing stays
            EnsureWater();
            if (_water.TryGetValue(new Vector2Int(Mathf.FloorToInt(p.x / 3f), Mathf.FloorToInt(p.z / 3f)), out float surf) && surf > at.y + 0.12f) return false;
            return true;
        }

        /// <summary>is the ground at p under a fresh water surface (the ford, a river) or the sea</summary>
        public static bool IsUnderWater(Vector3 p)
        {
            var t = Terrain.activeTerrain; if (!t) return false;
            float y = t.SampleHeight(p) + t.transform.position.y;
            if (y < 0.55f) return true;
            EnsureWater();
            return _water.TryGetValue(new Vector2Int(Mathf.FloorToInt(p.x / 3f), Mathf.FloorToInt(p.z / 3f)), out float surf) && surf > y + 0.08f;
        }

        static void EnsureWater()
        {
            var all = WaterSource.All;
            if (_water != null && all.Count == _waterSources) return;
            _waterSources = all.Count; _water = new Dictionary<Vector2Int, float>();
            foreach (var w in all)
            {
                if (!w) continue;
                var mf = w.surface ? w.surface : w.GetComponentInChildren<MeshFilter>();
                if (!mf || !mf.sharedMesh) continue;
                var tr = mf.transform; var verts = mf.sharedMesh.vertices;
                for (int i = 0; i < verts.Length; i++)
                {
                    var q = tr.TransformPoint(verts[i]);
                    int cx = Mathf.FloorToInt(q.x / 3f), cz = Mathf.FloorToInt(q.z / 3f);
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            var k = new Vector2Int(cx + dx, cz + dz);
                            if (!_water.TryGetValue(k, out float h) || q.y > h) _water[k] = q.y;
                        }
                }
            }
        }

        // ------------------------------------------------------------------ update / draw
        void Update()
        {
            var c = WildlifeConfig.Instance;
            // wear clock: game hours, faster in the rain
            double now = GameClock.Now;
            if (_lastClock < 0) _lastClock = now;
            double dh = (now - _lastClock) / Mathf.Max(1f, GameClock.SecondsPerHour); _lastClock = now;
            if (dh < 0) dh = 0;
            DinoSenses.Weather(out _, out _, out float rain);
            _wear += dh * (1f + c.rainWear * Mathf.Clamp01(rain));

            if (!_seeded && Time.time >= _seedAt && DinosaurController.All.Count > 0) { _seeded = true; Seed(); }

            if (!_cam) _cam = Camera.main;
            if (!_cam) return;
            if (Time.time >= _nextRebuild) { _nextRebuild = Time.time + 0.2f; Rebuild(c); }
            Draw(c);
        }

        void Rebuild(WildlifeConfig c)
        {
            for (int i = 0; i < _decal.Length; i++) _decal[i].Clear();
            _dungNew.Clear(); _dungOld.Clear(); _bones.Clear();
            Vector3 cp = _cam.transform.position; float r2 = c.drawDistance * c.drawDistance;
            float fresh = c.freshHours, fade = Mathf.Max(0.1f, c.fadeHours);
            int vis = 0;
            for (int i = 0; i < _signs.Length; i++)
            {
                ref var s = ref _signs[i];
                if (!s.used) continue;
                double age = _wear - s.born;
                if (age > s.life) { s.used = false; _count--; continue; }
                if ((s.pos - cp).sqrMagnitude > r2) continue;
                if (s.kind == TrackKind.Blood && GameSettings.Blood == BloodLevel.Off) continue;
                vis++;
                if (s.kind == TrackKind.Droppings) { (age < 8.0 ? _dungNew : _dungOld).Add(s.m); continue; }
                int bucket = s.permanent ? 1 : Bucket(age, s.life, fresh, fade);
                var list = _decal[(s.cell * 2 + (s.mirror ? 1 : 0)) * 4 + bucket]; if (list.Count < 1000) list.Add(s.m);
                if (s.kind == TrackKind.Kill && _bones.Count < 200) _bones.Add(s.m2);
            }
            Visible = vis;
        }

        /// <summary>fade step from age: fully fresh, then three fainter steps over the rest of its life</summary>
        static int Bucket(double age, float life, float fresh, float fade)
        {
            float freshPart = Mathf.Min(fresh, life * 0.3f);
            if (age <= freshPart) return 0;
            double k = (age - freshPart) / Mathf.Max(0.01f, life - freshPart);
            return k < 0.33 ? 1 : k < 0.66 ? 2 : 3;
        }

        void Draw(WildlifeConfig c)
        {
            var bounds = new Bounds(_cam.transform.position, Vector3.one * (c.drawDistance * 2.2f + 10f));
            for (int cell = 0; cell < TrackArt.CellCount; cell++)
                for (int mir = 0; mir < 2; mir++)
                    for (int b = 0; b < 4; b++)
                    {
                        var list = _decal[(cell * 2 + mir) * 4 + b]; if (list.Count == 0) continue;
                        var rp = new RenderParams(TrackArt.Decal(b)) { worldBounds = bounds, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true, lightProbeUsage = LightProbeUsage.Off };
                        Graphics.RenderMeshInstanced(rp, TrackArt.Quad((TrackArt.Cell)cell, mir == 1), 0, list);
                    }
            if (_dungNew.Count > 0) Graphics.RenderMeshInstanced(new RenderParams(TrackArt.DungMaterial(false)) { worldBounds = bounds, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true }, TrackArt.Dung, 0, _dungNew);
            if (_dungOld.Count > 0) Graphics.RenderMeshInstanced(new RenderParams(TrackArt.DungMaterial(true)) { worldBounds = bounds, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true }, TrackArt.Dung, 0, _dungOld);
            if (_bones.Count > 0) Graphics.RenderMeshInstanced(new RenderParams(TrackArt.BonesMaterial) { worldBounds = bounds, shadowCastingMode = ShadowCastingMode.On, receiveShadows = true }, TrackArt.Bones, 0, _bones);
        }

        /// <summary>new game: every sign goes, the old landmarks are placed again</summary>
        public void ClearAll()
        {
            for (int i = 0; i < _signs.Length; i++) _signs[i].used = false;
            _readUntil.Clear();
            _count = 0; _seeded = false; _seedAt = Time.time + 1.5f; _nextRebuild = 0f;
        }

        public int CountOf(TrackKind k) { int n = 0; for (int i = 0; i < _signs.Length; i++) if (_signs[i].used && _signs[i].kind == k) n++; return n; }

        // ------------------------------------------------------------------ examining
        /// <summary>the nearest sign the player has not read yet within range of p (horizontal; trunk marks up to 3.5 m above); -1 = none</summary>
        public static int FindExaminable(Vector3 p, float range)
        {
            var t = _inst; if (!t || t._signs == null) return -1;
            int best = -1; float bq = range * range;
            for (int i = 0; i < t._signs.Length; i++)
            {
                ref var s = ref t._signs[i];
                if (!s.used || s.examined) continue;
                if (s.kind == TrackKind.Blood && GameSettings.Blood == BloodLevel.Off) continue;
                float dy = s.pos.y - p.y; if (dy < -1.6f || dy > 3.6f) continue;
                if (t._readUntil.TryGetValue(s.species * 8 + (int)s.kind, out double until) && t._wear < until) continue;      // read these today already
                float dx = s.pos.x - p.x, dz = s.pos.z - p.z, q = dx * dx + dz * dz;
                if (q < bq) { bq = q; best = i; }
            }
            return best;
        }
        public static Vector3 PositionOf(int i) { var t = _inst; return t && i >= 0 && i < t._signs.Length ? t._signs[i].pos : Vector3.zero; }
        public static TrackKind KindOf(int i) { var t = _inst; return t && i >= 0 && i < t._signs.Length ? t._signs[i].kind : TrackKind.Footprint; }
        public static bool IsExaminable(int i) { var t = _inst; return t && i >= 0 && i < t._signs.Length && t._signs[i].used && !t._signs[i].examined; }

        static readonly string[] Prompts = { "Examine the tracks", "Examine the trampled plants", "Examine the claw marks", "Examine the droppings", "Examine the blood", "Examine the bones" };
        public static string PromptFor(int i) => Prompts[(int)KindOf(i)];

        /// <summary>game hours since the sign was left (wear hours: rain ages it faster)</summary>
        public static float AgeHours(int i) { var t = _inst; return t && i >= 0 && i < t._signs.Length ? (float)(t._wear - t._signs[i].born) : 0f; }

        /// <summary>read a sign: a short thought, TracksFound (+ the matching DISCOVERIES page); the trail around it counts as read</summary>
        public static string Examine(int i)
        {
            var t = _inst; if (!t || !IsExaminable(i)) return null;
            ref var s = ref t._signs[i];
            var def = SpeciesOf(i);
            float age = AgeHours(i);
            int same = 0;
            float r2 = WildlifeConfig.Instance.examinedRadius; r2 *= r2;
            for (int k = 0; k < t._signs.Length; k++)
            {
                ref var o = ref t._signs[k];
                if (!o.used || o.species != s.species || (o.pos - s.pos).sqrMagnitude > r2) continue;
                if (o.kind == s.kind) same++;
                if (o.kind == s.kind || (s.kind == TrackKind.Footprint && o.kind == TrackKind.Flattened) || (s.kind == TrackKind.Flattened && o.kind == TrackKind.Footprint)) o.examined = true;
            }
            t._readUntil[s.species * 8 + (int)s.kind] = t._wear + WildlifeConfig.Instance.readForHours;      // no prompts for the same kind of sign for a while
            string line = Thought(s.kind, def, age, same);
            bool predator = def && def.IsPredator;
            string id = def ? def.id : "unknown";
            Story.ProtagonistVoice.SayText("track:" + s.kind + ":" + id, line, true, true);
            GameEvents.Raise(GameEventType.TracksFound, id, predator ? 2 : 1, s.pos);
            string disc = DiscoveryFor(s.kind, def, same);
            if (disc != null) GameEvents.Raise(GameEventType.Discovery, disc, 1, s.pos);
            return line;
        }

        static string DiscoveryFor(TrackKind k, DinosaurDefinition d, int same)
        {
            bool pred = d && d.IsPredator;
            switch (k)
            {
                case TrackKind.Footprint: return pred ? (d.tracks.printSize >= 0.45f ? "footprint" : null) : "herd_tracks";
                case TrackKind.Flattened: return pred ? null : "herd_tracks";
                case TrackKind.Scratch: return pred && d.bodyRadius >= 0.8f ? "claw_marks" : null;
                case TrackKind.Blood: return "blood_trail";
                case TrackKind.Kill: return "kill_site";
                default: return null;
            }
        }

        static readonly StringBuilder Sb = new StringBuilder(256);

        /// <summary>the survivor's reading of a sign (never names a species the journal does not know yet)</summary>
        public static string Thought(TrackKind k, DinosaurDefinition d, float ageHours, int sameNearby)
        {
            var c = WildlifeConfig.Instance;
            bool pred = d && d.IsPredator; float size = d ? d.tracks.printSize : 0.4f;
            Sb.Clear();
            switch (k)
            {
                case TrackKind.Footprint:
                    if (d && !pred && sameNearby >= 8) Sb.Append(d.tracks.shape == FootShape.Hadrosaur ? "Blunt three-toed prints, dozens of them over each other. A herd passed here." : "Round prints, dozens of them, pressed deep. A herd passed here.");
                    else switch (d ? d.tracks.shape : FootShape.Theropod)
                    {
                        case FootShape.Dromaeosaur: Sb.Append(sameNearby >= 6 ? "Small prints, two toes each, the third held off the ground. Several of them, moving together. Quick hunters." : "A small print, only two toes pressed in, the third held up. Something quick."); break;
                        case FootShape.Hadrosaur: Sb.Append("Three blunt, rounded toes. A plant-eater, walking, in no hurry."); break;
                        case FootShape.Ceratopsian: Sb.Append("Wide round prints with short toes, a smaller one in front of each. Something very heavy, on four legs."); break;
                        case FootShape.Ankylosaur: Sb.Append("Broad, flat prints close together, dragged a little. Something low and heavy."); break;
                        default: Sb.Append(size >= 0.8f ? "Three toes, each as long as my arm, sharp at the tips. A huge hunter walked here." : size >= 0.45f ? "Three-toed prints, deep, a long stride between them. A hunter, and not a small one." : "Three thin toes, sharp. A hunter."); break;
                    }
                    break;
                case TrackKind.Flattened: Sb.Append("The plants are pressed flat in a wide path, stems snapped. Many heavy animals pushed through here."); break;
                case TrackKind.Scratch:
                    Sb.Append(d && d.bodyRadius < 0.8f ? "Thin claw marks low on the trunk, many of them, crossing. Small hunters, and more than one."
                              : d && d.tracks.scratchHeight >= 2.6f ? "Deep grooves torn through the bark, higher than my head. Something is marking this place as its own."
                              : "Claw grooves in the bark at chest height, sap still in them. A hunter marks its ground here."); break;
                case TrackKind.Droppings:
                    Sb.Append(pred ? "Dung with splinters of bone in it. A meat-eater." : "Dung, full of chewed plants. Grazers feed near here.");
                    Sb.Append(ageHours < 6f ? " Still soft." : ageHours < 30f ? " A day old, maybe." : " Dry. Days old."); break;
                case TrackKind.Blood: Sb.Append(pred ? "Blood in drops on the ground. One of the hunters is hurt." : "Drops of blood, then more. Something wounded went this way."); break;
                case TrackKind.Kill: Sb.Append("Bones picked clean, and the ground stained dark around them. Something ate well here, and took its time."); break;
            }
            if (k == TrackKind.Footprint || k == TrackKind.Flattened || k == TrackKind.Blood)
                Sb.Append(ageHours < c.freshHours ? " Fresh." : ageHours < c.freshHours + c.fadeHours * 0.5f ? " A few hours old." : " Old, the edges already soft.");
            if (d && (k == TrackKind.Footprint || k == TrackKind.Droppings) && KnownToJournal(d)) Sb.Append(' ').Append(d.displayName).Append(", I think.");
            return Sb.ToString();
        }

        static bool KnownToJournal(DinosaurDefinition d)
        {
            var j = Story.JournalSystem.Instance; if (!j || !d) return false;
            var page = j.CreaturePage(d.id);
            return page != null && j.IsUnlocked(page.id);
        }

        // ------------------------------------------------------------------ the island's old signs
        /// <summary>
        /// Signs that were there before the player came, placed once per session from a fixed seed (same places every
        /// time): old trails around every creature's home, dung where the herds graze, claw marks on trunks in the
        /// predator territories and a picked kill in the big hunters' ground.
        /// </summary>
        void Seed()
        {
            var c = WildlifeConfig.Instance;
            var rnd = new System.Random(20260929);
            float fresh = FreshLife;
            var herdsDone = new HashSet<HerdGroup>();
            foreach (var d in DinosaurController.All)
            {
                if (!d || !d.def) continue;
                var def = d.def; Vector3 home = d.home != Vector3.zero ? d.home : d.transform.position;
                // old trails: two or three short walks from the home area
                if (def.tracks.printSize > 0f)
                {
                    int left = c.seedPrintsPerCreature;
                    while (left > 0)
                    {
                        float a = (float)rnd.NextDouble() * Mathf.PI * 2f, r = (float)rnd.NextDouble() * Mathf.Max(6f, d.homeRadius * 0.7f);
                        Vector3 p = home + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                        float h = (float)rnd.NextDouble() * Mathf.PI * 2f; double age = fresh * (0.15 + rnd.NextDouble() * 0.6);
                        int steps = Mathf.Min(left, 5 + rnd.Next(4));
                        for (int s = 0; s < steps; s++)
                        {
                            h += ((float)rnd.NextDouble() - 0.5f) * 0.5f;
                            var fwd = new Vector3(Mathf.Sin(h), 0f, Mathf.Cos(h));
                            Step(def, p, fwd, s % 2 == 0, age);
                            p += fwd * def.tracks.stride * 0.5f;
                        }
                        left -= steps;
                    }
                }
                // dung in each herd's grazing places
                if (d.Herd && herdsDone.Add(d.Herd) && def.tracks.droppingsSize > 0f)
                    for (int k = 0; k < c.droppingsPerHerd; k++)
                    {
                        Vector3 g = d.Herd.SeedPoint(k);
                        var off = new Vector3((float)rnd.NextDouble() - 0.5f, 0f, (float)rnd.NextDouble() - 0.5f) * 14f;
                        Droppings(def, g + off, 10 + rnd.NextDouble() * 50, 200f);
                    }
                // claw marks on trunks in a predator's territory, and one old kill for the big ones
                if (def.IsPredator && def.tracks.scratchHeight > 0f && !_scratched.Contains(def))
                {
                    _scratched.Add(def);
                    float terr = Mathf.Max(20f, def.territoryRadius > 0f ? Mathf.Min(def.territoryRadius, d.homeRadius + 15f) : d.homeRadius);
                    int placed = 0;
                    for (int n = 0; n < 40 && placed < c.scratchesPerTerritory; n++)
                    {
                        int tree = CoverMap.StandingTreeAt(home, terr, n * 3 + rnd.Next(3));
                        if (tree < 0) break;
                        if (TryTrunk(CoverMap.TreePosition(tree), def.tracks.scratchHeight * (0.85f + (float)rnd.NextDouble() * 0.3f), (float)rnd.NextDouble() * 360f, out var pt, out var outw))
                        { Scratch(def, pt, outw, def.bodyRadius < 0.8f ? 0.55f : 0.5f + def.bodyRadius * 0.35f); placed++; }
                    }
                    if (def.Weight01 > 0.45f)
                    {
                        float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
                        Vector3 kp = home + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * d.homeRadius * 0.5f;
                        Kill(def, kp, 2.8f);
                        for (int s = 0; s < 8; s++) { float h = a + s * 0.8f; Step(def, kp + new Vector3(Mathf.Sin(h), 0, Mathf.Cos(h)) * 2.4f, new Vector3(Mathf.Cos(h), 0, -Mathf.Sin(h)), s % 2 == 0, fresh * 0.5); }
                    }
                }
            }
            _scratched.Clear();
            var mig = MigrationDirector.Instance;
            if (mig) mig.SeedOldTrail();
        }
        static readonly HashSet<DinosaurDefinition> _scratched = new HashSet<DinosaurDefinition>();

        static readonly RaycastHit[] _hits = new RaycastHit[8];
        /// <summary>a point on the bark of the terrain tree at 'basePos', 'height' above its foot, facing 'yawDeg'</summary>
        public static bool TryTrunk(Vector3 basePos, float height, float yawDeg, out Vector3 point, out Vector3 outward)
        {
            point = basePos; outward = Vector3.forward;
            var dir = Quaternion.Euler(0f, yawDeg, 0f) * Vector3.forward;
            Vector3 from = basePos + Vector3.up * height + dir * 2.5f;
            int n = Physics.RaycastNonAlloc(from, -dir, _hits, 2.6f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; bool ok = false;
            for (int i = 0; i < n; i++)
            {
                var h = _hits[i];
                if (h.distance < 0.6f || h.distance > best) continue;                // the hit must be close to the trunk axis (0.1 .. 1.9 m)
                best = h.distance; point = h.point; outward = h.normal; ok = true;
            }
            outward.y = 0f;
            if (!ok || outward.sqrMagnitude < 0.1f) return false;
            outward.Normalize();
            return true;
        }
    }
}
