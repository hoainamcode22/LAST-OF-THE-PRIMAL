using System;
using System.Collections.Generic;
using UnityEngine;

namespace PrimalFrontier.AI
{
    /// <summary>
    /// The island's creatures. Two ways to use it:
    /// 1. Placed (default after Primal Frontier > Scene > Bake Everything Into Scene): every dinosaur under this object in
    ///    the Hierarchy is a real scene object. Move, rotate, duplicate (Ctrl+D), delete or disable them by hand and save;
    ///    their Inspector values (DinosaurController / AmbientCreature) are used. A new game or a loaded save puts every
    ///    placed creature back where it stands in the editor, alive.
    /// 2. Random: when nothing is placed, the entries below spawn each species around its habitat.
    /// </summary>
    public class DinosaurSpawner : MonoBehaviour
    {
        [Serializable] public class Entry { public DinosaurDefinition def; public Vector3 center; public float radius = 30f; public int count = 1; public float altitude = 25f; }
        [Tooltip("random spawns, only used when no creature is placed under this object")]
        public List<Entry> entries = new List<Entry>();
        public bool spawnOnStart = true;
        [Header("Population (placed creatures)")]
        [Tooltip("in-game hours after death before a placed creature returns (0 = never); only once its body is gone")] [Min(0)] public float respawnHours = 48f;
        [Tooltip("never respawn a creature closer than this to the player (m)")] public float respawnMinDistance = 120f;
        float _nextRespawnCheck;
        readonly List<GameObject> _spawned = new List<GameObject>();
        public IReadOnlyList<GameObject> Spawned => _spawned;

        class Slot { public GameObject template; public Transform parent; public Vector3 pos; public Quaternion rot; public string name; }
        readonly List<Slot> _slots = new List<Slot>();
        /// <summary>true when the creatures come from the scene (placed by hand) rather than from the entries</summary>
        public bool UsesPlacedCreatures => _slots.Count > 0;
        public int PlacedCount => _slots.Count;

        void Awake()
        {
            // a scene the wildlife builder never ran on: lay the PC-phase herds / territories out now (same plan, runtime clones)
            if (WildlifePlan.RuntimeFallback && WildlifePlan.HasPlanGroups(transform) && !GetComponentInChildren<HerdGroup>(true))
            {
                var summary = WildlifePlan.Apply(transform, new WildlifePlan.Context());
                Debug.Log("[Wildlife] runtime layout (run PrimalWildlifeBuilder to bake it into the scene):\n" + summary);
            }
            // keep an untouched copy of every placed creature so a new game can restore it (the original may be killed)
            var placed = new List<GameObject>();
            foreach (var c in GetComponentsInChildren<DinosaurController>(false)) placed.Add(c.gameObject);
            foreach (var a in GetComponentsInChildren<AmbientCreature>(false)) if (!placed.Contains(a.gameObject)) placed.Add(a.gameObject);
            if (placed.Count == 0) return;
            var holder = new GameObject("[Templates]") { hideFlags = HideFlags.HideInHierarchy };
            holder.SetActive(false); holder.transform.SetParent(transform, false);
            foreach (var go in placed)
            {
                var t = Instantiate(go, holder.transform); t.name = go.name;      // inactive parent: no Awake / Start on the copy
                _slots.Add(new Slot { template = t, parent = go.transform.parent, pos = go.transform.position, rot = go.transform.rotation, name = go.name });
                _spawned.Add(go);
            }
        }

        void Start()
        {
            if (spawnOnStart && !UsesPlacedCreatures) SpawnAll();      // placed creatures are already there
            Story.TutorialManager.CreatureExists = () => DinosaurController.All.Count > 0;
        }

        /// <summary>
        /// A killed placed creature comes back at its editor spot after respawnHours of game time, once its body has
        /// sunk and the player is far away (a living island, not an emptying one). Checked every few seconds.
        /// </summary>
        void Update()
        {
            if (respawnHours <= 0f || !UsesPlacedCreatures || Time.time < _nextRespawnCheck) return;
            _nextRespawnCheck = Time.time + 5f;
            var pp = World.PlayerLocator.Position;
            double now = Core.GameClock.Now, wait = Core.GameClock.Hours(respawnHours);
            for (int i = 0; i < _spawned.Count && i < _slots.Count; i++)
            {
                var go = _spawned[i]; double died = -1; bool bodyGone = !go || !go.activeSelf;
                if (go)
                {
                    var dc = go.GetComponent<DinosaurController>(); var ac = dc ? null : go.GetComponent<AmbientCreature>();
                    if (dc) { if (dc.IsAlive) continue; died = dc.DiedAt; }
                    else if (ac) { if (ac.IsAlive) continue; died = ac.DiedAt; }
                    else continue;
                    var c = go.GetComponent<World.Carcass>(); if (c && !c.Sinking && go.activeSelf) continue;     // the body is still there
                }
                if (!bodyGone || died < 0 || now - died < wait) continue;
                var s = _slots[i];
                if (pp.HasValue && (s.pos - pp.Value).sqrMagnitude < respawnMinDistance * respawnMinDistance) continue;
                if (go) Destroy(go);
                var n = Instantiate(s.template, s.pos, s.rot, s.parent ? s.parent : transform); n.name = s.name; n.SetActive(true);
                _spawned[i] = n;
            }
        }

        public void SpawnAll()
        {
            Clear();
            DinosaurController.ResetSightings();
            if (TrackSigns.Exists) TrackSigns.Instance.ClearAll();          // new game / load: fresh tracks (the old landmarks come back)
            if (UsesPlacedCreatures)
            {
                foreach (var s in _slots)
                {
                    var go = Instantiate(s.template, s.pos, s.rot, s.parent ? s.parent : transform);
                    go.name = s.name; _spawned.Add(go);
                }
                return;
            }
            var t = Terrain.activeTerrain;
            foreach (var e in entries)
            {
                if (e.def == null || e.def.prefab == null) continue;
                int n = Mathf.Max(1, e.count);
                for (int i = 0; i < n; i++)
                {
                    Vector3 p = e.center + Quaternion.Euler(0, i * 137f + UnityEngine.Random.Range(0f, 40f), 0) * Vector3.forward * (i == 0 ? 0f : UnityEngine.Random.Range(4f, Mathf.Max(5f, e.radius * 0.4f)));
                    bool amb = e.def.temperament == Temperament.AmbientFlyer || e.def.temperament == Temperament.AmbientSwimmer;
                    if (t && !amb) p.y = t.SampleHeight(p) + t.transform.position.y;
                    var go = Instantiate(e.def.prefab, p, Quaternion.Euler(0, UnityEngine.Random.Range(0f, 360f), 0), transform);
                    go.name = e.def.displayName + "_" + i;
                    Configure(go, e.def, e.center, e.radius, e.altitude, true);
                    _spawned.Add(go);
                }
            }
        }

        /// <summary>brain for a creature instance (also used by the editor baker): land AI or an ambient flyer / swimmer</summary>
        public static void Configure(GameObject go, DinosaurDefinition def, Vector3 center, float radius, float altitude, bool playing)
        {
            bool amb = def.temperament == Temperament.AmbientFlyer || def.temperament == Temperament.AmbientSwimmer;
            if (amb)
            {
                var dc = go.GetComponent<DinosaurController>(); if (dc) { if (playing) Destroy(dc); else DestroyImmediate(dc); }
                var a = go.GetOrAdd<AmbientCreature>(); a.def = def; a.center = center; a.radius = radius; a.altitude = altitude; a.swimmer = def.temperament == Temperament.AmbientSwimmer;
            }
            else
            {
                var c = go.GetOrAdd<DinosaurController>(); c.def = def; c.home = center; c.homeRadius = radius;
            }
        }

        public void Clear()
        {
            foreach (var g in _spawned) if (g) Destroy(g);
            _spawned.Clear();
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (UsesPlacedCreatures || transform.childCount > 0) return;
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.8f);
            foreach (var e in entries) DrawCircle(e.center, e.radius);
        }
        internal static void DrawCircle(Vector3 c, float r)
        {
            Vector3 prev = c + new Vector3(r, 0, 0);
            for (int i = 1; i <= 48; i++) { float a = i / 48f * Mathf.PI * 2f; var p = c + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r); Gizmos.DrawLine(prev, p); prev = p; }
        }
#endif
    }
}
