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
        readonly List<GameObject> _spawned = new List<GameObject>();
        public IReadOnlyList<GameObject> Spawned => _spawned;

        class Slot { public GameObject template; public Transform parent; public Vector3 pos; public Quaternion rot; public string name; }
        readonly List<Slot> _slots = new List<Slot>();
        /// <summary>true when the creatures come from the scene (placed by hand) rather than from the entries</summary>
        public bool UsesPlacedCreatures => _slots.Count > 0;
        public int PlacedCount => _slots.Count;

        void Awake()
        {
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

        public void SpawnAll()
        {
            Clear();
            DinosaurController.ResetSightings();
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
