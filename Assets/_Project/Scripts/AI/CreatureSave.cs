using System;
using System.Collections.Generic;
using UnityEngine;

namespace PrimalFrontier.AI
{
    /// <summary>one creature of the spawner in the save file</summary>
    [Serializable]
    public class CreatureRecord
    {
        public int i; public string id; public string name;
        public bool dead; public float hp; public Vector3 pos; public float yaw; public double diedAt;
        public bool carcass; public int meat, hide, bone; public double expireAt; public bool gone;
        /// <summary>predators: hunger 0..1 (appended in Phase 1; older saves read 0)</summary>
        public float hunger;
    }

    [Serializable]
    public class CreatureSaveData { public int version = 1; public List<CreatureRecord> list = new List<CreatureRecord>(); }

    /// <summary>
    /// Creature state for the save file (a named JSON section): alive / dead, health, where it stands, and what is left
    /// on a carcass. A load respawns the spawner's creatures first (GameManager.ResetWorld), then this puts each one
    /// back: killed animals stay dead (their bodies keep the butchering progress), wounded ones keep their health.
    /// Matching is by spawn order + species id (+ name when the order changed). A missing or broken section is skipped.
    /// </summary>
    public static class CreatureSave
    {
        public const string Key = "creatures";

        public static string Capture(DinosaurSpawner sp)
        {
            if (!sp) return null;
            var d = new CreatureSaveData();
            var list = sp.Spawned;
            for (int i = 0; i < list.Count; i++)
            {
                var go = list[i]; if (!go) continue;
                var r = new CreatureRecord { i = i, name = go.name };
                var dc = go.GetComponent<DinosaurController>();
                var ac = dc ? null : go.GetComponent<AmbientCreature>();
                if (dc)
                {
                    if (!dc.def) continue;
                    var s = dc.Capture();
                    r.id = dc.def.id; Fill(r, s);
                }
                else if (ac)
                {
                    r.id = ac.def ? ac.def.id : go.name;
                    r.dead = !ac.IsAlive; r.pos = go.transform.position; r.yaw = go.transform.eulerAngles.y; r.diedAt = ac.DiedAt;
                    var c = go.GetComponent<World.Carcass>();
                    if (r.dead && c) { r.carcass = true; r.meat = c.meat; r.hide = c.hide; r.bone = c.bone; r.expireAt = c.ExpireAt; r.gone = c.Sinking || !go.activeSelf; }
                    else if (r.dead) r.gone = !go.activeSelf;
                    if (!r.dead) continue;                              // ambient flyers / swimmers: only their death matters
                }
                else continue;
                d.list.Add(r);
            }
            return JsonUtility.ToJson(d);
        }

        static void Fill(CreatureRecord r, DinosaurController.SavedState s)
        {
            r.dead = s.dead; r.hp = s.health; r.pos = s.pos; r.yaw = s.yaw; r.diedAt = s.diedAt;
            r.carcass = s.carcass; r.meat = s.meat; r.hide = s.hide; r.bone = s.bone; r.expireAt = s.expireAt; r.gone = s.gone; r.hunger = s.hunger;
        }

        static DinosaurController.SavedState ToState(CreatureRecord r) => new DinosaurController.SavedState
        {
            dead = r.dead, health = r.hp, pos = r.pos, yaw = r.yaw, diedAt = r.diedAt,
            carcass = r.carcass, meat = r.meat, hide = r.hide, bone = r.bone, expireAt = r.expireAt, gone = r.gone, hunger = r.hunger,
        };

        /// <summary>apply a captured section to the (freshly respawned) creatures; returns how many were restored</summary>
        public static int Restore(DinosaurSpawner sp, string json)
        {
            if (!sp || string.IsNullOrEmpty(json)) return 0;
            CreatureSaveData d;
            try { d = JsonUtility.FromJson<CreatureSaveData>(json); }
            catch (Exception e) { Debug.LogWarning("[CreatureSave] unreadable section: " + e.Message); return 0; }
            if (d == null || d.list == null) return 0;
            var list = sp.Spawned; int n = 0;
            var used = new bool[list.Count];
            foreach (var r in d.list)
            {
                if (r == null) continue;
                int k = Find(list, used, r);
                if (k < 0) continue;
                used[k] = true;
                var go = list[k];
                try
                {
                    var dc = go.GetComponent<DinosaurController>();
                    if (dc) { dc.RestoreSaved(ToState(r)); n++; continue; }
                    var ac = go.GetComponent<AmbientCreature>();
                    if (ac && r.dead) { ac.RestoreDead(ToState(r)); n++; }
                }
                catch (Exception e) { Debug.LogWarning("[CreatureSave] could not restore " + r.name + ": " + e.Message); }
            }
            return n;
        }

        static int Find(IReadOnlyList<GameObject> list, bool[] used, CreatureRecord r)
        {
            if (r.i >= 0 && r.i < list.Count && !used[r.i] && Matches(list[r.i], r, true)) return r.i;
            for (int k = 0; k < list.Count; k++) if (!used[k] && Matches(list[k], r, true)) return k;
            for (int k = 0; k < list.Count; k++) if (!used[k] && Matches(list[k], r, false)) return k;     // renamed in the scene: same species
            return -1;
        }

        static bool Matches(GameObject go, CreatureRecord r, bool byName)
        {
            if (!go) return false;
            var dc = go.GetComponent<DinosaurController>();
            string id = dc && dc.def ? dc.def.id : null;
            if (id == null) { var ac = go.GetComponent<AmbientCreature>(); id = ac && ac.def ? ac.def.id : go.name; }
            if (id != r.id) return false;
            return !byName || string.IsNullOrEmpty(r.name) || go.name == r.name;
        }
    }

    /// <summary>
    /// The save section for creatures (SURV's ISaveSection), registered by GameManager.Awake. SaveSystem.Apply restores it
    /// after GameManager.ResetWorld has respawned every creature, so the load order is respawn, then this.
    /// </summary>
    public sealed class CreatureSaveSection : Core.ISaveSection
    {
        readonly DinosaurSpawner _spawner;
        public CreatureSaveSection(DinosaurSpawner spawner) { _spawner = spawner; }
        public string SectionKey => CreatureSave.Key;
        public string CaptureSection() => CreatureSave.Capture(_spawner);
        public void RestoreSection(string json) => CreatureSave.Restore(_spawner, json);
    }
}
