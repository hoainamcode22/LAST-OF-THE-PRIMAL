using System;
using System.Collections.Generic;
using UnityEngine;

namespace PrimalFrontier.AI
{
    /// <summary>Spawns each species in its habitat when the island loads (and again for a new game). Few animals, placed with intent.</summary>
    public class DinosaurSpawner : MonoBehaviour
    {
        [Serializable] public class Entry { public DinosaurDefinition def; public Vector3 center; public float radius = 30f; public int count = 1; public float altitude = 25f; }
        public List<Entry> entries = new List<Entry>();
        public bool spawnOnStart = true;
        readonly List<GameObject> _spawned = new List<GameObject>();
        public IReadOnlyList<GameObject> Spawned => _spawned;

        void Start()
        {
            if (spawnOnStart) SpawnAll();
            Story.TutorialManager.CreatureExists = () => DinosaurController.All.Count > 0;
        }

        public void SpawnAll()
        {
            Clear();
            DinosaurController.ResetSightings();
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
                    if (amb)
                    {
                        var dc = go.GetComponent<DinosaurController>(); if (dc) Destroy(dc);
                        var a = go.GetOrAdd<AmbientCreature>(); a.def = e.def; a.center = e.center; a.radius = e.radius; a.altitude = e.altitude; a.swimmer = e.def.temperament == Temperament.AmbientSwimmer;
                    }
                    else
                    {
                        var c = go.GetOrAdd<DinosaurController>(); c.def = e.def; c.home = e.center; c.homeRadius = e.radius;
                    }
                    _spawned.Add(go);
                }
            }
        }

        public void Clear()
        {
            foreach (var g in _spawned) if (g) Destroy(g);
            _spawned.Clear();
        }
    }
}
