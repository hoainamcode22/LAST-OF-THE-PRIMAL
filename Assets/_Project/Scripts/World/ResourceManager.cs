using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Core;
using PrimalFrontier.Player;

namespace PrimalFrontier.World
{
    /// <summary>
    /// Runs every resource node from one place, so no node has an Update: respawn timers (checked twice a second),
    /// the regrowing look of emptied rocks / bushes near the player (distance activation, 80 m), fruit regrowth, the hit
    /// wobble of the few nodes that were just struck, a 16 m cell grid for "nodes near here" queries, and the
    /// interaction highlight. Hidden nodes never pop back in right in front of the player: a due respawn waits until the
    /// player is 18 m away or looking elsewhere. Created on demand in play mode ([ResourceManager]).
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public partial class ResourceManager : MonoBehaviour
    {
        static ResourceManager _inst;
        public static ResourceManager Instance { get { Ensure(); return _inst; } }

        /// <summary>gather once per press, keep going while the interact key / GATHER button is held (directive 44)</summary>
        public static bool HoldToRepeat = true;

        [Tooltip("emptied nodes within this range show their regrowing look (m)")] public float activeRadius = 80f;
        [Tooltip("a hidden node does not reappear while the player is closer than this and facing it (m)")] public float noPopRadius = 18f;
        [Tooltip("s between respawn checks")] public float tickInterval = 0.5f;

        static readonly List<ResourceNode> _nodes = new List<ResourceNode>(1024);
        static readonly List<ResourceNode> _depleted = new List<ResourceNode>(64);
        static readonly List<ResourceNode> _animating = new List<ResourceNode>(8);
        static readonly List<FruitCluster> _fruit = new List<FruitCluster>(16);
        static readonly Dictionary<Vector2Int, List<ResourceNode>> _grid = new Dictionary<Vector2Int, List<ResourceNode>>();
        const float Cell = 16f;

        public static IReadOnlyList<ResourceNode> Nodes => _nodes;
        public static IReadOnlyList<ResourceNode> DepletedNodes => _depleted;
        public static IReadOnlyList<FruitCluster> Fruit => _fruit;
        /// <summary>average cost of the manager's Update (ms, editor timing): the whole resource layer per frame</summary>
        public static float AverageTickMs { get; private set; }
        public static Material UnripeMaterial => ResourceDatabase.Instance ? ResourceDatabase.Instance.unripeMaterial : null;

        float _nextTick; readonly System.Diagnostics.Stopwatch _sw = new System.Diagnostics.Stopwatch();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _inst = null; _nodes.Clear(); _depleted.Clear(); _animating.Clear(); _fruit.Clear(); _grid.Clear(); HoldToRepeat = true; AverageTickMs = 0f; }

        public static void Ensure()
        {
            if (_inst != null || !Application.isPlaying) return;
            _inst = FindFirstObjectByType<ResourceManager>();
            if (_inst == null) _inst = new GameObject("[ResourceManager]").AddComponent<ResourceManager>();
        }

        void Awake()
        {
            if (_inst != null && _inst != this) { Destroy(this); return; }
            _inst = this;
            if (!GetComponent<InteractionHighlight>()) gameObject.AddComponent<InteractionHighlight>();
        }
        void OnDestroy() { if (_inst == this) _inst = null; }

        // save section (ResourceSaveSection.cs, needs SURV's ISaveSection): a partial hook, so this file builds without it
        partial void SaveHook(bool on);
        void OnEnable() => SaveHook(true);
        void OnDisable() => SaveHook(false);

        // ------------------------------------------------------------------ registry (nodes call these)
        public static void Register(ResourceNode n)
        {
            if (!n || !Application.isPlaying) return;
            Ensure();
            if (_nodes.Contains(n)) return;
            _nodes.Add(n);
            var k = Key(n.transform.position);
            if (!_grid.TryGetValue(k, out var l)) _grid[k] = l = new List<ResourceNode>();
            l.Add(n);
            if (n.IsEmpty && !_depleted.Contains(n)) _depleted.Add(n);
        }

        public static void Unregister(ResourceNode n)
        {
            _nodes.Remove(n); _depleted.Remove(n); _animating.Remove(n);
            if (n && _grid.TryGetValue(Key(n.transform.position), out var l)) l.Remove(n);
            else foreach (var kv in _grid) if (kv.Value.Remove(n)) break;
        }

        public static void Register(FruitCluster f) { if (f && Application.isPlaying && !_fruit.Contains(f)) { Ensure(); _fruit.Add(f); } }
        public static void Unregister(FruitCluster f) => _fruit.Remove(f);

        internal static void Depleted(ResourceNode n) { if (n && !_depleted.Contains(n)) _depleted.Add(n); }
        internal static void Restored(ResourceNode n) { _depleted.Remove(n); }
        internal static void Animate(ResourceNode n) { if (n && !_animating.Contains(n)) _animating.Add(n); }

        /// <summary>after a gather hit: go on only while the interact key / button is held (HoldToRepeat)</summary>
        public static bool KeepGathering()
        {
            if (!HoldToRepeat) return true;
            var ir = PlayerInputReader.Instance;
            return ir == null || ir.InteractHeld;
        }

        static Vector2Int Key(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell));

        /// <summary>registered nodes within r (m) of p, flat distance; into is cleared first</summary>
        public static void NodesNear(Vector3 p, float r, List<ResourceNode> into)
        {
            into.Clear();
            var k = Key(p); int c = Mathf.CeilToInt(r / Cell); float r2 = r * r;
            for (int dx = -c; dx <= c; dx++)
                for (int dz = -c; dz <= c; dz++)
                {
                    if (!_grid.TryGetValue(new Vector2Int(k.x + dx, k.y + dz), out var l)) continue;
                    foreach (var n in l) { if (!n) continue; var d = n.transform.position - p; d.y = 0f; if (d.sqrMagnitude <= r2) into.Add(n); }
                }
        }

        // ------------------------------------------------------------------ per frame
        void Update()
        {
            _sw.Restart();
            float dt = Time.deltaTime;
            for (int i = _animating.Count - 1; i >= 0; i--)
            {
                var n = _animating[i];
                if (!n || !n.StepWobble(dt)) _animating.RemoveAt(i);
            }
            if (Time.time >= _nextTick) { _nextTick = Time.time + tickInterval; Tick(); }
            _sw.Stop();
            AverageTickMs = Mathf.Lerp(AverageTickMs, (float)_sw.Elapsed.TotalMilliseconds, 0.05f);
        }

        static readonly List<ResourceNode> _due = new List<ResourceNode>(16);

        /// <summary>respawns and regrowing looks (also called by tests after moving the clock)</summary>
        public void Tick()
        {
            double now = GameClock.Now;
            var pp = PlayerLocator.Position;
            var cam = Camera.main ? Camera.main.transform : null;
            float act2 = activeRadius * activeRadius, pop2 = noPopRadius * noPopRadius;
            _due.Clear();
            for (int i = 0; i < _depleted.Count; i++)
            {
                var n = _depleted[i];
                if (!n) continue;
                if (!n.IsEmpty) { _due.Add(n); continue; }
                double dur = GameClock.Hours(n.Def.respawnHours);
                if (n.EmptyUntil < 0 || now >= n.EmptyUntil)
                {
                    if (n.Def.depletedLook == DepletedLook.Hide && pp.HasValue && InView(n.transform.position, pp.Value, cam, pop2)) continue;   // not in front of the player
                    _due.Add(n); continue;
                }
                if (n.Def.depletedLook == DepletedLook.Hide) continue;
                if (pp.HasValue) { var d = n.transform.position - pp.Value; d.y = 0f; if (d.sqrMagnitude > act2) continue; }   // distance activation: far looks catch up later
                double start = n.EmptyUntil - dur;
                n.SetRegrow((float)((now - start - dur * 0.5) / (dur * 0.5)));
            }
            foreach (var n in _due) { if (n.IsEmpty) n.Regrow(); else _depleted.Remove(n); }
            for (int i = _fruit.Count - 1; i >= 0; i--) { var f = _fruit[i]; if (!f) { _fruit.RemoveAt(i); continue; } f.Tick(now); }
        }

        static bool InView(Vector3 at, Vector3 player, Transform cam, float r2)
        {
            var d = at - player; d.y = 0f;
            if (d.sqrMagnitude > r2) return false;
            if (!cam) return true;
            var v = at - cam.position; return Vector3.Dot(cam.forward, v) > 0f;
        }
    }
}
