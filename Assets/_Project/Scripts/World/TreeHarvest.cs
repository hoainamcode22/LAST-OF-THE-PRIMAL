using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Audio;
using PrimalFrontier.Combat;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.VFX;

namespace PrimalFrontier.World
{
    /// <summary>
    /// Chopping the terrain's trees (they are TreeInstances, not GameObjects): this one interactable moves to the
    /// nearest standing tree. With an axe (or a hand stone, any Chop tool) each hit gives wood by the tool's efficiency
    /// (ResourceDefinition "wood_tree", GatheringSystem); after hitsPerTree hits the tree falls (hidden, TreeFelled event)
    /// and regrows days later. Bare hands cannot chop: they strip a few dry twigs (tiny yield, a hint, no felling), by
    /// Interact or by punching (IDamageable on a trigger proxy that follows the current tree).
    /// The object itself is moved onto the current tree: PlayerInteraction pre-filters interactables by their
    /// transform distance, so a system object left near the world origin was only choppable near the origin.
    /// </summary>
    public class TreeHarvest : Interactable, IDamageable
    {
        public Terrain terrain;
        public ItemDefinition wood;
        public ItemDefinition fiber;
        [Range(0, 1)] public float fiberChance = 0.25f;
        public int hitsPerTree = 6;
        public float regrowHours = 72f;
        public float toolWear = 1f;
        [Tooltip("the tree's data (tools, hand rate, feedback). Empty = ResourceDatabase 'wood_tree', else built-in values")]
        public ResourceDefinition definition;
        [Tooltip("dry twigs bare hands can strip from one tree before it regrows")] public int handTwigsPerTree = 2;

        const float Cell = 8f;
        readonly Dictionary<Vector2Int, List<int>> _grid = new Dictionary<Vector2Int, List<int>>();
        TreeInstance[] _trees; Vector3[] _pos;
        readonly Dictionary<int, int> _hits = new Dictionary<int, int>();
        readonly Dictionary<int, double> _felled = new Dictionary<int, double>();     // index -> regrow time
        readonly Dictionary<int, int> _handTaken = new Dictionary<int, int>();
        float _toolProgress, _handProgress;
        ResourceDefinition _def;
        Transform _proxy;
        int _current = -1, _placedAt = -1;
        string[] _hitsLeft;                            // "N hits left" texts, built once (no per-frame strings)
        public int Current => _current;
        public IReadOnlyDictionary<int, double> Felled => _felled;
        /// <summary>chop hits taken per standing tree (saved by ResourceSaveSection)</summary>
        public IReadOnlyDictionary<int, int> Hits => _hits;
        public ResourceDefinition Def => _def ? _def : (_def = definition ? definition : (ResourceDatabase.Instance.Get("wood_tree") ?? BuiltIn()));
        /// <summary>IDamageable: punches land while the player stands at a tree</summary>
        public bool IsAlive => _current >= 0;
        public override float Range => 1.6f;
        public override float Radius => 0.45f;
        public override Vector3 FocusPoint => _current >= 0 ? _pos[_current] + Vector3.up * 1.0f : transform.position;

        void Start() { Build(); MakeProxy(); }

        ResourceDefinition BuiltIn()
        {
            var d = ScriptableObject.CreateInstance<ResourceDefinition>(); d.hideFlags = HideFlags.DontSave;
            d.id = "wood_tree"; d.displayName = "Tree"; d.category = ResourceCategory.Wood; d.size = ResourceSize.Huge; d.prompt = "Chop Tree";
            d.item = wood; d.bestTool = d.requiredTool = ToolKind.Chop; d.handsFactor = 0.25f; d.toolFactor = 1f;
            d.handAction = PlayerActions.GatherBranch; d.toolAction = PlayerActions.GatherWood; d.respawnHours = regrowHours;
            d.handHint = "Bare hands only strip dry twigs. An axe fells the tree."; d.depletedLook = DepletedLook.Mined;
            d.FeedbackDefaults(); d.hitVfx2 = VfxId.Leaves;
            return d;
        }

        /// <summary>a trigger on Ignore Raycast that follows the current tree, so bare-hand strikes reach the terrain tree</summary>
        void MakeProxy()
        {
            if (_proxy) return;
            var go = new GameObject("HitProxy"); go.layer = 2;
            go.transform.SetParent(transform, false);
            var c = go.AddComponent<CapsuleCollider>(); c.isTrigger = true; c.radius = 0.45f; c.height = 3f; c.center = new Vector3(0f, 1.5f, 0f);
            _proxy = go.transform; go.SetActive(false);
        }

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
            if (_current >= 0 && _current != _placedAt) { _placedAt = _current; transform.position = _pos[_current]; }
            if (_proxy && _proxy.gameObject.activeSelf != (_current >= 0)) _proxy.gameObject.SetActive(_current >= 0);
        }

        string HitsLeft(int n)
        {
            if (_hitsLeft == null || _hitsLeft.Length != hitsPerTree + 1)
            {
                _hitsLeft = new string[Mathf.Max(1, hitsPerTree + 1)];
                for (int i = 0; i < _hitsLeft.Length; i++) _hitsLeft[i] = i == 1 ? "1 hit left" : $"{i} hits left";
            }
            return _hitsLeft[Mathf.Clamp(n, 0, _hitsLeft.Length - 1)];
        }

        bool Chops(PlayerInteraction p, out GatherPlan plan)
        {
            plan = GatheringSystem.Plan(Def, p ? p.ActiveItem : null);
            return !plan.byHand;
        }
        int HandLeft(int tree) => handTwigsPerTree - (_handTaken.TryGetValue(tree, out var n) ? n : 0);

        public override string GetPrompt(PlayerInteraction p, out string sub)
        {
            sub = null;
            if (_current < 0) return null;
            if (!Chops(p, out _))
            {
                sub = HandLeft(_current) > 0 ? Def.handHint : "No dry twigs left here. An axe fells the tree.";
                return "Gather Wood";
            }
            int h = _hits.TryGetValue(_current, out var v) ? v : 0;
            sub = HitsLeft(hitsPerTree - h);
            return Def.prompt;
        }
        public override bool CanInteract(PlayerInteraction p) => _current >= 0 && (Chops(p, out _) || HandLeft(_current) > 0);

        public override void Interact(PlayerInteraction p)
        {
            if (!CanInteract(p)) return;
            int tree = _current; var d = Def;
            var fb = p.Feedback;
            bool chop = Chops(p, out var plan);
            System.Func<bool> hit = chop ? () => Hit(p, tree) : () => HandHit(p, tree);
            if (p.DoLoop(chop ? d.toolAction : d.handAction, "OnGatherHit", hit, _pos[tree] + Vector3.up * 1.0f, d.gatherSeconds / Mathf.Max(0.1f, plan.speed), this,
                         () => { if (fb) fb.NodeDrivesGatherFx = false; }) && fb)
                fb.NodeDrivesGatherFx = true;
        }

        /// <summary>one axe / hand-stone hit (animation event); false = stop</summary>
        public bool Hit(PlayerInteraction p, int tree)
        {
            if (tree < 0 || _felled.ContainsKey(tree) || !Chops(p, out var plan)) return false;
            var tool = plan.toolItem; var d = Def;
            if (p.Inventory.SpaceFor(wood) <= 0) { PlayerInteraction.Notify(p.Inventory.IsOverweight ? "Too heavy to carry more." : "Inventory full."); return false; }
            int n = Mathf.Max(1, GatheringSystem.Roll(ref _toolProgress, plan.min, plan.max));
            int got = p.GiveOrDrop(wood, n);
            if (fiber && Random.value < fiberChance) p.GiveOrDrop(fiber, 1);
            if (tool && tool.HasDurability) p.Inventory.WearActive(toolWear * (plan.tool ? plan.tool.wearPerAction : 1f));   // break feedback: InventorySystem.ToolBroke (HUD)
            Vector3 at = _pos[tree] + Vector3.up * 1.0f;
            PlayerFeedback.GatherHit(d, at, (p.transform.position + Vector3.up - at).normalized, false, n);
            PlayerFeedback.GatherCollected(p.transform.position + Vector3.up);
            GameEvents.Raise(GameEventType.ResourceGathered, wood.id, got, _pos[tree]);
            int h = (_hits.TryGetValue(tree, out var v) ? v : 0) + 1; _hits[tree] = h;
            if (h >= hitsPerTree) { Fell(tree, true); return false; }
            return Chops(p, out _) && ResourceManager.KeepGathering();
        }

        /// <summary>bare hands at a tree: dry twigs now and then (handsFactor), never felling it</summary>
        public bool HandHit(PlayerInteraction p, int tree)
        {
            if (tree < 0 || _felled.ContainsKey(tree) || HandLeft(tree) <= 0) return false;
            if (p.Inventory.SpaceFor(wood) <= 0) { PlayerInteraction.Notify(p.Inventory.IsOverweight ? "Too heavy to carry more." : "Inventory full."); return false; }
            var d = Def; var plan = GatheringSystem.Plan(d, p.ActiveItem);
            int n = Mathf.Min(HandLeft(tree), GatheringSystem.Roll(ref _handProgress, plan.min, plan.max));
            HandGive(p, tree, n, _pos[tree] + Vector3.up * 1.3f, (p.transform.position + Vector3.up - _pos[tree]).normalized);
            return HandLeft(tree) > 0 && ResourceManager.KeepGathering();
        }

        void HandGive(PlayerInteraction p, int tree, int n, Vector3 at, Vector3 normal)
        {
            var d = Def; int got = 0;
            if (n > 0)
            {
                if (p) { got = p.GiveOrDrop(wood, n); PlayerFeedback.GatherCollected(p.transform.position + Vector3.up); }
                else { WorldPickup.Drop(wood, n, at); got = n; }
                _handTaken[tree] = (_handTaken.TryGetValue(tree, out var t) ? t : 0) + n;
            }
            else PlayerFeedback.GatherHint(d.handHint);
            PlayerFeedback.GatherHit(d, at, normal, true, n);
            GameEvents.Raise(GameEventType.ResourceGathered, wood.id, got, _pos[tree]);
        }

        /// <summary>a bare-hand strike on the current tree: leaves, a hint, a twig once in a while (no chopping with fists)</summary>
        public void TakeHit(HitInfo hit)
        {
            int tree = _current;
            if (tree < 0 || !hit.unarmed || _felled.ContainsKey(tree)) return;
            var pi = hit.attacker ? hit.attacker.GetComponentInParent<PlayerInteraction>() : null;
            if (HandLeft(tree) <= 0) { PlayerFeedback.GatherHint("No dry twigs left here. An axe fells the tree."); PlayerFeedback.GatherHit(Def, hit.point, -hit.direction, true, 0); return; }
            var d = Def;
            int n = Mathf.Min(HandLeft(tree), GatheringSystem.Pay(ref _handProgress, hit.damage / d.damagePerUnit * d.handsFactor));
            if (pi && n > 0 && pi.Inventory.SpaceFor(wood) <= 0) { PlayerInteraction.Notify("Inventory full."); n = 0; }
            HandGive(pi, tree, n, hit.point, -hit.direction);
        }

        /// <summary>save / load of the chop hits on standing trees</summary>
        public void RestoreHits(IEnumerable<KeyValuePair<int, int>> hits)
        {
            _hits.Clear();
            if (hits != null) foreach (var kv in hits) if (kv.Value > 0 && kv.Value < hitsPerTree && !_felled.ContainsKey(kv.Key)) _hits[kv.Key] = kv.Value;
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
                GameEvents.Raise(GameEventType.TreeFelled, wood ? wood.id : "wood", 1, _pos[i]);
            }
        }

        void Regrow(int i)
        {
            _felled.Remove(i); _handTaken.Remove(i);
            terrain.terrainData.SetTreeInstance(i, _trees[i]);
        }

        /// <summary>restore the original forest (exit play mode / new game) - TerrainData is an asset!</summary>
        public void RestoreAll()
        {
            if (_trees == null || !terrain) return;
            foreach (var i in new List<int>(_felled.Keys)) Regrow(i);
            _hits.Clear(); _handTaken.Clear(); _toolProgress = _handProgress = 0f;
        }
        void OnDestroy() { RestoreAll(); }

        public void Restore(Dictionary<int, double> felled)
        {
            RestoreAll();
            foreach (var kv in felled) { Fell(kv.Key, false); _felled[kv.Key] = kv.Value; }
        }
    }
}
