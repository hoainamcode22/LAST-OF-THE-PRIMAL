using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Audio;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.VFX;

namespace PrimalFrontier.World
{
    /// <summary>
    /// Chopping the terrain's trees (they are TreeInstances, not GameObjects): this one interactable moves to the
    /// nearest standing tree. Each hit gives wood; after enough hits the tree falls (hidden) and regrows days later.
    /// </summary>
    public class TreeHarvest : Interactable
    {
        public Terrain terrain;
        public ItemDefinition wood;
        public ItemDefinition fiber;
        [Range(0, 1)] public float fiberChance = 0.25f;
        public int hitsPerTree = 6;
        public float regrowHours = 72f;
        public float toolWear = 1f;

        const float Cell = 8f;
        readonly Dictionary<Vector2Int, List<int>> _grid = new Dictionary<Vector2Int, List<int>>();
        TreeInstance[] _trees; Vector3[] _pos;
        readonly Dictionary<int, int> _hits = new Dictionary<int, int>();
        readonly Dictionary<int, double> _felled = new Dictionary<int, double>();     // index -> regrow time
        int _current = -1;
        public int Current => _current;
        public IReadOnlyDictionary<int, double> Felled => _felled;
        public override float Range => 1.6f;
        public override float Radius => 0.45f;
        public override Vector3 FocusPoint => _current >= 0 ? _pos[_current] + Vector3.up * 1.0f : transform.position;

        void Start() { Build(); }

        public void Build()
        {
            if (!terrain) terrain = Terrain.activeTerrain;
            if (!terrain) return;
#if UNITY_EDITOR
            // in the editor TerrainData is the asset on disk: work on a copy so play mode never edits the forest
            if (Application.isPlaying && UnityEditor.AssetDatabase.Contains(terrain.terrainData))
            {
                var copy = Instantiate(terrain.terrainData); terrain.terrainData = copy;
                var tc = terrain.GetComponent<TerrainCollider>(); if (tc) tc.terrainData = copy;
            }
#endif
            var td = terrain.terrainData; _trees = td.treeInstances; _pos = new Vector3[_trees.Length];
            _grid.Clear();
            for (int i = 0; i < _trees.Length; i++)
            {
                _pos[i] = Vector3.Scale(_trees[i].position, td.size) + terrain.transform.position;
                var k = Key(_pos[i]);
                if (!_grid.TryGetValue(k, out var l)) _grid[k] = l = new List<int>();
                l.Add(i);
            }
        }

        static Vector2Int Key(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell));

        void Update()
        {
            if (_trees == null) return;
            // regrow
            if (_felled.Count > 0)
            {
                List<int> back = null;
                foreach (var kv in _felled) if (GameClock.Now >= kv.Value) (back ??= new List<int>()).Add(kv.Key);
                if (back != null) foreach (var i in back) Regrow(i);
            }
            _current = -1;
            var pp = PlayerLocator.Position; if (!pp.HasValue) return;
            var p = pp.Value; var k = Key(p); float best = 3.2f * 3.2f;
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (!_grid.TryGetValue(new Vector2Int(k.x + dx, k.y + dz), out var l)) continue;
                    foreach (var i in l)
                    {
                        if (_felled.ContainsKey(i)) continue;
                        var d = _pos[i] - p; d.y = 0; float s = d.sqrMagnitude;
                        if (s < best) { best = s; _current = i; }
                    }
                }
        }

        public override string GetPrompt(PlayerInteraction p, out string sub)
        {
            sub = null;
            if (_current < 0) return null;
            if (!p.HasTool(ToolKind.Chop, out _)) { sub = "Needs an axe or a hand stone in hand"; return "Chop tree"; }
            int h = _hits.TryGetValue(_current, out var v) ? v : 0;
            sub = $"{hitsPerTree - h} hits left";
            return "Chop tree";
        }
        public override bool CanInteract(PlayerInteraction p) => _current >= 0 && p.HasTool(ToolKind.Chop, out _);

        public override void Interact(PlayerInteraction p)
        {
            if (!CanInteract(p)) return;
            int tree = _current;
            p.DoLoop(PlayerActions.GatherWood, "OnGatherHit", () => Hit(p, tree), _pos[tree] + Vector3.up * 1.0f, 1.25f, this);
        }

        public bool Hit(PlayerInteraction p, int tree)
        {
            if (tree < 0 || _felled.ContainsKey(tree) || !p.HasTool(ToolKind.Chop, out var tool)) return false;
            int n = Mathf.Max(1, Mathf.RoundToInt(2f * tool.toolPower));
            if (p.Inventory.SpaceFor(wood) <= 0) { PlayerInteraction.Notify(p.Inventory.IsOverweight ? "Too heavy to carry more." : "Inventory full."); return false; }
            int got = p.GiveOrDrop(wood, n);
            if (fiber && Random.value < fiberChance) p.GiveOrDrop(fiber, 1);
            if (tool.HasDurability && p.Inventory.WearActive(toolWear)) { PlayerInteraction.Notify(tool.displayName + " broke!"); }
            GameEvents.Raise(GameEventType.ResourceGathered, wood.id, got, _pos[tree]);
            int h = (_hits.TryGetValue(tree, out var v) ? v : 0) + 1; _hits[tree] = h;
            if (h >= hitsPerTree) { Fell(tree, true); return false; }
            return p.HasTool(ToolKind.Chop, out _);
        }

        public void Fell(int i, bool fx)
        {
            if (_trees == null || i < 0 || i >= _trees.Length) return;
            _felled[i] = GameClock.Now + GameClock.Hours(regrowHours); _hits.Remove(i);
            var t = _trees[i]; t.heightScale = 0f; t.widthScale = 0f;
            terrain.terrainData.SetTreeInstance(i, t);
            if (fx)
            {
                VfxPool.Instance.Play(VfxId.Leaves, _pos[i] + Vector3.up * 2.5f, Vector3.up, null, 2f);
                VfxPool.Instance.Play(VfxId.WoodChips, _pos[i] + Vector3.up * 0.8f, Vector3.up, null, 1.5f);
                SfxPlayer.Instance.Play(SfxId.WoodBreak, _pos[i], 1f);
                WorldPickup.Drop(wood, 3, _pos[i] + Vector3.right * 1.2f);
                PlayerInteraction.Notify("Timber! The tree falls.");
            }
        }

        void Regrow(int i)
        {
            _felled.Remove(i);
            terrain.terrainData.SetTreeInstance(i, _trees[i]);
        }

        /// <summary>restore the original forest (exit play mode / new game) - TerrainData is an asset!</summary>
        public void RestoreAll()
        {
            if (_trees == null || !terrain) return;
            foreach (var i in new List<int>(_felled.Keys)) Regrow(i);
            _hits.Clear();
        }
        void OnDestroy() { RestoreAll(); }

        public void Restore(Dictionary<int, double> felled)
        {
            RestoreAll();
            foreach (var kv in felled) { Fell(kv.Key, false); _felled[kv.Key] = kv.Value; }
        }
    }
}
