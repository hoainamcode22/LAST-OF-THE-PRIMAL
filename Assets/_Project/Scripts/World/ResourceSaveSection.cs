using System;
using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Core;

namespace PrimalFrontier.World
{
    /// <summary>
    /// Save section "resources" (SURV's ISaveSection): fruit clusters that were picked (fruit left + regrow time) and the
    /// chop hits on standing trees. Resource nodes and felled trees stay in SaveData as before. Registered by
    /// ResourceManager; a missing or broken section leaves the reset state (all fruit ripe, no hits).
    /// </summary>
    public class ResourceSaveSection : ISaveSection
    {
        public const string Key = "resources";
        public string SectionKey => Key;

        [Serializable] public class FruitState { public string id; public int left; public double emptyUntil; }
        [Serializable] public class TreeState { public int index; public int hits; }
        [Serializable] public class Data { public int version = 1; public List<FruitState> fruit = new List<FruitState>(); public List<TreeState> trees = new List<TreeState>(); }

        public string CaptureSection()
        {
            var d = new Data();
            foreach (var f in UnityEngine.Object.FindObjectsByType<FruitCluster>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (f && (!f.Ripe || f.Left < f.amount)) d.fruit.Add(new FruitState { id = f.SaveId, left = f.Left, emptyUntil = f.EmptyUntil });
            var th = UnityEngine.Object.FindFirstObjectByType<TreeHarvest>();
            if (th) foreach (var kv in th.Hits) d.trees.Add(new TreeState { index = kv.Key, hits = kv.Value });
            return JsonUtility.ToJson(d);
        }

        public void RestoreSection(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            var d = JsonUtility.FromJson<Data>(json); if (d == null) return;
            var byId = new Dictionary<string, FruitState>();
            if (d.fruit != null) foreach (var f in d.fruit) if (f != null && !string.IsNullOrEmpty(f.id)) byId[f.id] = f;
            foreach (var f in UnityEngine.Object.FindObjectsByType<FruitCluster>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!f) continue;
                if (byId.TryGetValue(f.SaveId, out var s)) f.Restore(s.left, s.emptyUntil); else f.Regrow();
            }
            var th = UnityEngine.Object.FindFirstObjectByType<TreeHarvest>();
            if (th && d.trees != null)
            {
                var hits = new List<KeyValuePair<int, int>>();
                foreach (var t in d.trees) if (t != null) hits.Add(new KeyValuePair<int, int>(t.index, t.hits));
                th.RestoreHits(hits);
            }
        }
    }

    public partial class ResourceManager
    {
        readonly ResourceSaveSection _save = new ResourceSaveSection();
        partial void SaveHook(bool on) { if (on) SaveSystem.RegisterSection(_save); else SaveSystem.UnregisterSection(_save); }
    }
}
