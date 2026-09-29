using UnityEngine;
using PrimalFrontier.World;

namespace PrimalFrontier.AI
{
    /// <summary>
    /// Cover around a point: terrain trees (standing vs felled, read from TreeHarvest.Felled) in a flat 8 m cell grid
    /// built once from the terrain's tree instances, and walk-through bushes (BushInteraction.All). Felling trees lowers
    /// the cover automatically and a patch of stumps counts as a clearing. Queries allocate nothing.
    /// </summary>
    public static class CoverMap
    {
        const float Cell = 8f;
        static Vector3[] _pos; static int[] _start, _items;
        static int _nx, _nz; static Vector3 _origin; static Terrain _terrain; static TreeHarvest _harvest; static bool _built; static float _nextHarvestLook;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _pos = null; _start = _items = null; _terrain = null; _harvest = null; _built = false; }

        public static int TreeCount { get { EnsureBuilt(); return _pos != null ? _pos.Length : 0; } }

        /// <summary>tests / scene changes: rebuild on the next query</summary>
        public static void Invalidate() { _built = false; }

        static void EnsureBuilt()
        {
            var t = Terrain.activeTerrain;
            if (_built && t == _terrain)
            {
                // TreeHarvest may appear after the first query (scene start order): look again now and then
                if (!_harvest && t && Time.unscaledTime >= _nextHarvestLook) { _nextHarvestLook = Time.unscaledTime + 5f; _harvest = Object.FindFirstObjectByType<TreeHarvest>(); }
                return;
            }
            _built = true; _terrain = t; _harvest = t ? Object.FindFirstObjectByType<TreeHarvest>() : null; _nextHarvestLook = Time.unscaledTime + 5f;
            _pos = null; _start = _items = null;
            if (!t || !t.terrainData) return;
            var td = t.terrainData; var trees = td.treeInstances;             // a copy, once
            _origin = t.transform.position;
            _nx = Mathf.Max(1, Mathf.CeilToInt(td.size.x / Cell)); _nz = Mathf.Max(1, Mathf.CeilToInt(td.size.z / Cell));
            _pos = new Vector3[trees.Length];
            var cellOf = new int[trees.Length];
            _start = new int[_nx * _nz + 1];
            for (int i = 0; i < trees.Length; i++)
            {
                var p = Vector3.Scale(trees[i].position, td.size) + _origin; _pos[i] = p;
                int c = CellIndex(p); cellOf[i] = c; if (c >= 0) _start[c + 1]++;
            }
            for (int c = 0; c < _nx * _nz; c++) _start[c + 1] += _start[c];
            _items = new int[trees.Length];
            var fill = new int[_nx * _nz];
            for (int i = 0; i < trees.Length; i++) { int c = cellOf[i]; if (c < 0) continue; _items[_start[c] + fill[c]++] = i; }
        }

        static int CellIndex(Vector3 p)
        {
            int x = Mathf.FloorToInt((p.x - _origin.x) / Cell), z = Mathf.FloorToInt((p.z - _origin.z) / Cell);
            if (x < 0 || z < 0 || x >= _nx || z >= _nz) return -1;
            return z * _nx + x;
        }

        /// <summary>standing trees within r of p (horizontal); felled = felled trees in the same radius</summary>
        public static int TreesNear(Vector3 p, float r, out int felled)
        {
            felled = 0; EnsureBuilt();
            if (_pos == null || _pos.Length == 0) return 0;
            int standing = 0; float r2 = r * r;
            int x0 = Mathf.FloorToInt((p.x - r - _origin.x) / Cell), x1 = Mathf.FloorToInt((p.x + r - _origin.x) / Cell);
            int z0 = Mathf.FloorToInt((p.z - r - _origin.z) / Cell), z1 = Mathf.FloorToInt((p.z + r - _origin.z) / Cell);
            var cut = _harvest ? _harvest.Felled : null;
            for (int z = Mathf.Max(0, z0); z <= Mathf.Min(_nz - 1, z1); z++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(_nx - 1, x1); x++)
                {
                    int c = z * _nx + x;
                    for (int k = _start[c]; k < _start[c + 1]; k++)
                    {
                        int i = _items[k]; var q = _pos[i];
                        float dx = q.x - p.x, dz = q.z - p.z; if (dx * dx + dz * dz > r2) continue;
                        if (cut != null && cut.ContainsKey(i)) felled++; else standing++;
                    }
                }
            return standing;
        }

        /// <summary>indices of the standing trees within r of p, nearest first (tests / tools; fills 'into')</summary>
        public static void StandingTrees(Vector3 p, float r, System.Collections.Generic.List<int> into)
        {
            into.Clear(); EnsureBuilt(); if (_pos == null) return;
            var cut = _harvest ? _harvest.Felled : null; float r2 = r * r;
            for (int i = 0; i < _pos.Length; i++) { var q = _pos[i]; float dx = q.x - p.x, dz = q.z - p.z; if (dx * dx + dz * dz <= r2 && (cut == null || !cut.ContainsKey(i))) into.Add(i); }
            into.Sort((a, b) => (_pos[a] - p).sqrMagnitude.CompareTo((_pos[b] - p).sqrMagnitude));
        }

        public static Vector3 TreePosition(int i) { EnsureBuilt(); return _pos != null && i >= 0 && i < _pos.Length ? _pos[i] : Vector3.zero; }

        /// <summary>bushes whose edge is within 'edge' m of p (the one the player is inside counts too)</summary>
        public static int BushesNear(Vector3 p, float edge)
        {
            var all = BushInteraction.All; int n = 0;
            for (int i = 0; i < all.Count; i++)
            {
                var b = all[i]; if (!b) continue;
                Vector3 d = b.transform.position - p; d.y = 0f;
                float lim = b.Radius + edge;
                if (d.sqrMagnitude <= lim * lim) n++;
            }
            return n;
        }
    }
}
