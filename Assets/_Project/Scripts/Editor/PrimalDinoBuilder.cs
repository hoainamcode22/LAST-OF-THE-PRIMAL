using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PrimalFrontier.AI;
using PrimalFrontier.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// After PrimalCharacterBuilder imported the dinosaurs: DinosaurDefinition assets (stats / senses / behaviour),
    /// and a DinosaurSpawner in Island_VerticalSlice placing each species in its habitat (Blender markers).
    /// </summary>
    public static class PrimalDinoBuilder
    {
        const string DataDir = "Assets/_Project/Data/Dinosaurs";
        const string ScenePath = "Assets/_Project/Scenes/Island_VerticalSlice.unity";

        struct Row
        {
            public string id, name, habitat; public Temperament t; public float hp, turn, accel, radius, sight, observe, personal, aggro, territory, flee, retreat, range, dmg, heavy, cd, bleed;
            public int gmin, gmax, meat, hide, bone, count; public float spawnRadius, altitude; public bool heavySteps;
        }

        static readonly Row[] Rows =
        {
            new Row { id = "Triceratops", name = "Triceratops", habitat = "HAB_Triceratops", t = Temperament.Defensive, hp = 600, turn = 45, accel = 2.2f, radius = 1.5f, sight = 40, observe = 32, personal = 11, aggro = 0, territory = 45, flee = 50, retreat = 0.3f, range = 4f, dmg = 30, heavy = 55, cd = 2.6f, count = 2, spawnRadius = 30, meat = 5, hide = 2, bone = 3, heavySteps = true },
            new Row { id = "Parasaurolophus", name = "Parasaurolophus", habitat = "HAB_Parasaurolophus", t = Temperament.Passive, hp = 280, turn = 70, accel = 3f, radius = 1.1f, sight = 55, observe = 40, personal = 16, flee = 70, retreat = 1f, range = 3f, dmg = 12, heavy = 20, cd = 3f, count = 3, spawnRadius = 30, meat = 4, hide = 2, bone = 2, heavySteps = true },
            new Row { id = "Ankylosaurus", name = "Ankylosaurus", habitat = "HAB_Ankylosaurus", t = Temperament.Defensive, hp = 750, turn = 40, accel = 1.5f, radius = 1.3f, sight = 30, observe = 22, personal = 8, territory = 30, flee = 30, retreat = 0.15f, range = 4.5f, dmg = 35, heavy = 60, cd = 3f, count = 1, spawnRadius = 25, meat = 4, hide = 3, bone = 2, heavySteps = true },
            new Row { id = "Velociraptor", name = "Velociraptor", habitat = "HAB_Velociraptor", t = Temperament.Predator, hp = 80, turn = 240, accel = 12f, radius = 0.4f, sight = 45, observe = 0, personal = 0, aggro = 28, territory = 60, flee = 60, retreat = 0.3f, range = 1.8f, dmg = 12, heavy = 18, cd = 1.1f, bleed = 4f, count = 2, spawnRadius = 25, meat = 1, hide = 1, bone = 1, heavySteps = false },
            new Row { id = "Carnotaurus", name = "Carnotaurus", habitat = "HAB_Carnotaurus", t = Temperament.Territorial, hp = 500, turn = 70, accel = 4f, radius = 1.2f, sight = 50, aggro = 35, territory = 55, flee = 60, retreat = 0.15f, range = 4.5f, dmg = 38, heavy = 60, cd = 2f, bleed = 5f, count = 1, spawnRadius = 30, meat = 5, hide = 2, bone = 3, heavySteps = true },
            new Row { id = "Spinosaurus", name = "Spinosaurus", habitat = "RESAREA_StreamStone", t = Temperament.Territorial, hp = 850, turn = 45, accel = 2.5f, radius = 1.6f, sight = 40, aggro = 26, territory = 30, flee = 60, retreat = 0.1f, range = 6f, dmg = 45, heavy = 70, cd = 2.4f, bleed = 5f, count = 1, spawnRadius = 20, meat = 7, hide = 3, bone = 4, heavySteps = true },
            new Row { id = "Apex", name = "Rift Tyrant", habitat = "HAB_ApexPredator", t = Temperament.Territorial, hp = 1300, turn = 40, accel = 2.5f, radius = 1.8f, sight = 55, aggro = 40, territory = 60, flee = 60, retreat = 0.05f, range = 6.5f, dmg = 60, heavy = 90, cd = 2.6f, bleed = 6f, count = 1, spawnRadius = 40, meat = 9, hide = 4, bone = 5, heavySteps = true },
            new Row { id = "Pteranodon", name = "Pteranodon", habitat = "ZONE_Shipwreck", t = Temperament.AmbientFlyer, hp = 40, turn = 90, accel = 5, radius = 0.6f, count = 3, spawnRadius = 70, altitude = 28, meat = 1, hide = 1, bone = 1 },
            new Row { id = "Mosasaurus", name = "Mosasaurus", habitat = "OCEAN", t = Temperament.AmbientSwimmer, hp = 900, turn = 30, accel = 2, radius = 1.4f, count = 1, spawnRadius = 60, meat = 8, hide = 3, bone = 4 },
        };

        [Serializable] class MarkerFile { public Marker[] markers; }
        [Serializable] class Marker { public string name; public string group; public float x, y, z, r, yaw; }

        [MenuItem("Primal Frontier/Dinosaurs/Build Definitions + Spawner")]
        public static void Build()
        {
            Directory.CreateDirectory(DataDir); AssetDatabase.Refresh();
            var defs = new List<(DinosaurDefinition def, Row row)>();
            foreach (var r in Rows)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrimalCharacterBuilder.DinoRoot}/{r.id}/Prefab/DINO_{r.id}.prefab");
                if (prefab == null) { Debug.LogWarning("[PrimalDinoBuilder] prefab missing for " + r.id); continue; }
                string p = $"{DataDir}/DINO_{r.id}.asset";
                var d = AssetDatabase.LoadAssetAtPath<DinosaurDefinition>(p);
                if (d == null) { d = ScriptableObject.CreateInstance<DinosaurDefinition>(); AssetDatabase.CreateAsset(d, p); }
                d.id = r.id.ToLowerInvariant(); d.displayName = r.name; d.prefab = prefab; d.temperament = r.t; d.maxHealth = r.hp;
                var meta = AssetDatabase.LoadAssetAtPath<TextAsset>($"{PrimalCharacterBuilder.DinoRoot}/{r.id}/Animations/DINO_{r.id}_anim.json");
                float walk = 1.6f, run = 5f;
                if (meta != null)
                {
                    var m = JsonUtility.FromJson<PrimalCharacterBuilder.AnimMeta>(meta.text);
                    var w = m.clips.FirstOrDefault(c => c.name == "Walk"); var rn = m.clips.FirstOrDefault(c => c.name == "Run");
                    if (w != null && w.speed > 0.01f) walk = w.speed; if (rn != null && rn.speed > 0.01f) run = rn.speed;
                }
                if (r.t == Temperament.AmbientFlyer) { walk = 0.8f; run = 11f; }
                if (r.t == Temperament.AmbientSwimmer) { walk = 4f; run = 9f; }
                d.walkSpeed = walk; d.runSpeed = run; d.turnSpeed = r.turn; d.acceleration = r.accel; d.bodyRadius = r.radius;
                d.sightRange = r.sight; d.hearingRange = r.sight * 0.6f; d.observeDistance = r.observe; d.personalSpace = r.personal; d.aggroRange = r.aggro;
                d.territoryRadius = r.territory; d.fleeDistance = r.flee; d.retreatHealth = r.retreat;
                d.attackRange = r.range; d.attackDamage = r.dmg; d.heavyDamage = r.heavy; d.attackCooldown = r.cd; d.bleedSeconds = r.bleed;
                AudioClip[] Clips(string kind) => Enumerable.Range(1, 3).Select(i => AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/_Project/Audio/SFX/Dinosaurs/SFX_{r.id}_{kind}_{i:00}.wav")).Where(c => c != null).ToArray();
                d.calls = Clips("Call"); d.roars = Clips("Roar"); d.hurts = Clips("Hurt"); d.deaths = Clips("Death");
                d.groupMin = 1; d.groupMax = r.count; d.meat = r.meat; d.hide = r.hide; d.bone = r.bone; d.heavyFootsteps = r.heavySteps; d.journalId = d.id;
                EditorUtility.SetDirty(d); defs.Add((d, r));
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[PrimalDinoBuilder] definitions: {defs.Count}");

            // ---- spawner in the island scene
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var terrain = UnityEngine.Object.FindFirstObjectByType<Terrain>();
            var markers = new Dictionary<string, Marker>();
            var mj = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_Project/Data/World/markers.json");
            if (mj != null) foreach (var m in JsonUtility.FromJson<MarkerFile>(mj.text).markers) markers[m.name] = m;
            Vector3 MP(string n) { if (!markers.TryGetValue(n, out var m)) return Vector3.zero; var p = BlenderSpace.ToUnityPosition(m.x, m.y, m.z); if (terrain) p.y = terrain.SampleHeight(p) + terrain.transform.position.y; return p; }
            var old = GameObject.Find("[Dinosaurs]"); if (old) UnityEngine.Object.DestroyImmediate(old);
            var go = new GameObject("[Dinosaurs]"); var gp = GameObject.Find("[Gameplay]"); if (gp) go.transform.SetParent(gp.transform);
            var sp = go.AddComponent<DinosaurSpawner>();
            foreach (var (d, r) in defs)
            {
                Vector3 c;
                if (r.habitat == "OCEAN")
                {
                    var w = MP("ZONE_Shipwreck"); var dir = new Vector3(w.x, 0, w.z).normalized; c = w + dir * 110f; c.y = 0f;
                    // walk further out until the sea floor is deep enough
                    for (int i = 0; i < 20 && terrain && terrain.SampleHeight(c) + terrain.transform.position.y > -6f; i++) c += dir * 15f;
                }
                else c = MP(r.habitat);
                if (c == Vector3.zero && r.habitat != "OCEAN") { Debug.LogWarning("[PrimalDinoBuilder] marker missing " + r.habitat); continue; }
                sp.entries.Add(new DinosaurSpawner.Entry { def = d, center = c, radius = r.spawnRadius, count = r.count, altitude = r.altitude });
            }
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Debug.Log($"[PrimalDinoBuilder] spawner entries: {sp.entries.Count} -> {ScenePath}");
        }
    }
}
