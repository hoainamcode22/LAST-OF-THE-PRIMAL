using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PrimalFrontier.AI;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// Perception data (AI/PERCEPTION_DESIGN.md 11): creates Resources/PerceptionConfig when missing and writes ONLY the
    /// appended perception / fire fear / daily life fields of each DINO_*.asset from the table below (every other field
    /// belongs to PrimalDinoBuilder and is never touched). Idempotent. Arg "keep" leaves assets that already have
    /// hand-tuned perception values alone (detected by a non-default fireFear radius).
    /// </summary>
    public static class PrimalPerceptionBuilder
    {
        const string DataDir = "Assets/_Project/Data/Dinosaurs", ConfigPath = "Assets/_Project/Resources/PerceptionConfig.asset";

        struct Row
        {
            public string id; public float night, sight, hear, smell, decay, memory, herd, meat, track, drinkHours; public bool alarm; public ActivityCycle act;
            public FireFearProfile fire;
        }

        static FireFearProfile F(float fear, float day, float night, float rain, float fuel, FireResponse resp, float patience, float ignoreBelow, bool provoked) =>
            new FireFearProfile { predatorFear = fear, fearRadiusDay = day, fearRadiusNight = night, rainMultiplier = rain, fuelMultiplier = fuel, response = resp, patienceSeconds = patience, ignoreBelowIntensity = ignoreBelow, ignoreWhenProvoked = provoked };

        /// <summary>starting values (design 11.2): small predators fear fire most, the apex hesitates and ignores weak fires</summary>
        static readonly Row[] Rows =
        {
            new Row { id = "triceratops", night = 0.10f, sight = 0.8f, hear = 0.8f, smell = 0.5f, decay = 0.10f, memory = 8, herd = 30, alarm = true, meat = 0, track = 0, act = ActivityCycle.Diurnal, drinkHours = 10,
                      fire = F(0.7f, 6, 9, 0.7f, 0.5f, FireResponse.Avoid, 12, 0, true) },
            new Row { id = "parasaurolophus", night = 0.15f, sight = 1.2f, hear = 1.4f, smell = 0.9f, decay = 0.06f, memory = 12, herd = 40, alarm = true, meat = 0, track = 0, act = ActivityCycle.Diurnal, drinkHours = 8,
                      fire = F(0.9f, 8, 12, 0.7f, 0.5f, FireResponse.Leave, 8, 0, false) },
            new Row { id = "ankylosaurus", night = 0.10f, sight = 0.6f, hear = 0.7f, smell = 0.6f, decay = 0.12f, memory = 6, herd = 0, alarm = false, meat = 0, track = 0, act = ActivityCycle.Diurnal, drinkHours = 12,
                      fire = F(0.5f, 5, 7, 0.7f, 0.5f, FireResponse.Avoid, 12, 0, true) },
            new Row { id = "velociraptor", night = 0.45f, sight = 1.1f, hear = 1.3f, smell = 1.4f, decay = 0.05f, memory = 20, herd = 45, alarm = false, meat = 1f, track = 15, act = ActivityCycle.Nocturnal, drinkHours = 12,
                      fire = F(0.85f, 7, 10, 0.6f, 0.6f, FireResponse.Circle, 25, 0, true) },
            new Row { id = "carnotaurus", night = 0.30f, sight = 1.0f, hear = 1.0f, smell = 1.0f, decay = 0.07f, memory = 15, herd = 0, alarm = false, meat = 0.7f, track = 8, act = ActivityCycle.Cathemeral, drinkHours = 14,
                      fire = F(0.6f, 5, 8, 0.6f, 0.6f, FireResponse.Wait, 18, 0, true) },
            new Row { id = "spinosaurus", night = 0.25f, sight = 0.9f, hear = 0.9f, smell = 0.8f, decay = 0.08f, memory = 12, herd = 0, alarm = false, meat = 0.5f, track = 6, act = ActivityCycle.Cathemeral, drinkHours = 6,
                      fire = F(0.6f, 5, 8, 0.6f, 0.5f, FireResponse.Observe, 15, 0, true) },
            new Row { id = "apex", night = 0.30f, sight = 0.9f, hear = 1.1f, smell = 1.3f, decay = 0.04f, memory = 30, herd = 0, alarm = false, meat = 0.9f, track = 12, act = ActivityCycle.Cathemeral, drinkHours = 16,
                      fire = F(0.3f, 4, 6, 0.5f, 0.8f, FireResponse.Wait, 10, 0.4f, true) },
        };

        [PrimalBridgeCommand]
        public static string Build(string arg)
        {
            bool keep = arg == "keep";
            var log = new StringBuilder();
            Directory.CreateDirectory("Assets/_Project/Resources");
            var cfg = AssetDatabase.LoadAssetAtPath<PerceptionConfig>(ConfigPath);
            if (!cfg) { cfg = ScriptableObject.CreateInstance<PerceptionConfig>(); AssetDatabase.CreateAsset(cfg, ConfigPath); log.AppendLine("created " + ConfigPath); }
            else if (arg == "reset-config")
            {
                // the asset takes the code defaults again (same file, same GUID)
                var fresh = ScriptableObject.CreateInstance<PerceptionConfig>();
                EditorUtility.CopySerialized(fresh, cfg); Object.DestroyImmediate(fresh); cfg.name = "PerceptionConfig";
                EditorUtility.SetDirty(cfg); log.AppendLine("config reset to code defaults: " + ConfigPath);
            }
            else log.AppendLine("config exists (kept): " + ConfigPath);
            var byId = new Dictionary<string, Row>(); foreach (var r in Rows) byId[r.id] = r;
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:DinosaurDefinition", new[] { DataDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var d = AssetDatabase.LoadAssetAtPath<DinosaurDefinition>(path); if (!d) continue;
                if (d.temperament == Temperament.AmbientFlyer || d.temperament == Temperament.AmbientSwimmer)
                {
                    d.fireFear = F(0f, 0, 0, 1, 0, FireResponse.Avoid, 0, 0, false); d.drinkEveryHours = 0f;
                    EditorUtility.SetDirty(d); log.AppendLine($"{d.id}: ambient (no fire fear, no drinking)"); n++; continue;
                }
                if (!byId.TryGetValue(d.id, out var row)) { log.AppendLine($"{d.id}: not in the table, defaults kept"); continue; }
                if (keep && d.fireFear.fearRadiusDay != FireFearProfile.Default.fearRadiusDay) { log.AppendLine($"{d.id}: hand-tuned, kept"); continue; }
                d.nightVision = row.night; d.sightGain = row.sight; d.hearingGain = row.hear; d.smellSensitivity = row.smell;
                d.awarenessDecay = row.decay; d.memorySeconds = row.memory; d.herdShareRadius = row.herd; d.alarmCall = row.alarm;
                d.meatDrive = row.meat; d.scentTrackRange = row.track; d.activity = row.act; d.drinkEveryHours = row.drinkHours;
                d.fireFear = row.fire; d.unarmedDamageScale = -1f;
                EditorUtility.SetDirty(d); n++;
                log.AppendLine($"{d.id}: night {row.night} sight x{row.sight} hear x{row.hear} smell x{row.smell} fire {row.fire.predatorFear} ({row.fire.fearRadiusDay}/{row.fire.fearRadiusNight} m, {row.fire.response}) {row.act} unarmed x{DinosaurController.UnarmedScale(d):F3}");
            }
            AssetDatabase.SaveAssets();
            log.Insert(0, $"[PrimalPerceptionBuilder] {n} definitions updated\n");
            Debug.Log(log.ToString());
            return log.ToString();
        }
    }
}
