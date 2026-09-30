using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using PrimalFrontier.Items;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// QA (agent Q, Phase 1 final verification). Read-only: loads assets, changes nothing, saves nothing.
    /// Prefabs: every prefab under Assets/_Project, Assets/Art and Assets/Resources: missing scripts, null mesh on
    /// MeshFilter / SkinnedMeshRenderer / MeshCollider, null or broken-shader material slots, missing object references,
    /// nested prefab instances whose asset is missing. Items: ItemDatabase null entries, recipes with a missing output /
    /// ingredient or pointing at an item that is not in the database, duplicate ids. Animators: every AnimatorController,
    /// states (sub state machines included) without motion and blend trees with empty children.
    /// Details go to Library/PrimalBridge/Q_*.txt.
    /// </summary>
    public static class PrimalQaCheck
    {
        const string OutDir = "Library/PrimalBridge";
        static readonly string[] Roots = { "Assets/_Project", "Assets/Art", "Assets/Resources" };

        static bool SkipProp(string p) => p == "m_Script" || p == "m_GameObject" || p.StartsWith("m_PrefabInstance") || p.StartsWith("m_CorrespondingSourceObject") || p.StartsWith("m_PrefabAsset");

        [PrimalBridgeCommand]
        public static string Prefabs(string arg)
        {
            var sb = new StringBuilder(); var offenders = new List<string>();
            var guids = AssetDatabase.FindAssets("t:Prefab", Roots.Where(AssetDatabase.IsValidFolder).ToArray());
            int prefabs = 0, loadFail = 0, missScripts = 0, nullMesh = 0, nullMat = 0, badShader = 0, missRefs = 0, missNested = 0, bad = 0;
            foreach (var g in guids.Distinct())
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!go) { loadFail++; offenders.Add($"LOAD FAILED {path}"); continue; }
                prefabs++;
                int ms = 0, nm = 0, nt = 0, bs = 0, mr = 0, mn = 0; var lines = new List<string>();
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                {
                    string tp = t == go.transform ? go.name : go.name + "/" + AnimationUtility.CalculateTransformPath(t, go.transform);
                    int k = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                    if (k > 0) { ms += k; lines.Add($"    missing script x{k}: {tp}"); }
                    if (PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject) && PrefabUtility.IsPrefabAssetMissing(t.gameObject)) { mn++; lines.Add($"    nested prefab asset missing: {tp}"); }
                    var mf = t.GetComponent<MeshFilter>(); if (mf && !mf.sharedMesh) { nm++; lines.Add($"    MeshFilter without mesh: {tp}"); }
                    var smr = t.GetComponent<SkinnedMeshRenderer>(); if (smr && !smr.sharedMesh) { nm++; lines.Add($"    SkinnedMeshRenderer without mesh: {tp}"); }
                    var mc = t.GetComponent<MeshCollider>(); if (mc && !mc.sharedMesh) { nm++; lines.Add($"    MeshCollider without mesh: {tp}"); }
                    foreach (var r in t.GetComponents<Renderer>())
                    {
                        Material[] mats;
                        if (r is ParticleSystemRenderer psr) { if (psr.renderMode == ParticleSystemRenderMode.None) continue; mats = new[] { psr.sharedMaterial }; }
                        else mats = r.sharedMaterials;
                        for (int i = 0; i < mats.Length; i++)
                        {
                            var m = mats[i];
                            if (!m) { nt++; lines.Add($"    {r.GetType().Name} material slot {i} empty: {tp}"); }
                            else if (!m.shader || m.shader.name == "Hidden/InternalErrorShader" || ShaderUtil.ShaderHasError(m.shader)) { bs++; lines.Add($"    {r.GetType().Name} material {m.name} broken shader: {tp}"); }
                        }
                    }
                    foreach (var c in t.GetComponents<Component>())
                    {
                        if (!c || c is Transform) continue;
                        var it = new SerializedObject(c).GetIterator();
                        while (it.Next(true))
                        {
                            if (it.propertyType != SerializedPropertyType.ObjectReference || SkipProp(it.propertyPath)) continue;
                            if (it.objectReferenceValue == null && it.objectReferenceInstanceIDValue != 0) { mr++; lines.Add($"    missing ref {c.GetType().Name}.{it.propertyPath}: {tp}"); }
                        }
                    }
                }
                missScripts += ms; nullMesh += nm; nullMat += nt; badShader += bs; missRefs += mr; missNested += mn;
                if (lines.Count > 0) { bad++; offenders.Add($"{path}: missing scripts {ms}, null meshes {nm}, empty material slots {nt}, broken shaders {bs}, missing refs {mr}, missing nested {mn}"); offenders.AddRange(lines.Take(30)); if (lines.Count > 30) offenders.Add($"    ... {lines.Count - 30} more"); }
            }
            string head = $"prefabs {prefabs} (load failed {loadFail}) under {string.Join(", ", Roots)}: with problems {bad}; missing scripts {missScripts}, null meshes {nullMesh}, empty material slots {nullMat}, broken shaders {badShader}, missing object refs {missRefs}, missing nested prefab assets {missNested}";
            sb.AppendLine(head); foreach (var o in offenders) sb.AppendLine(o);
            File.WriteAllText(Path.Combine(OutDir, "Q_prefabs.txt"), sb.ToString());
            return head + (offenders.Count > 0 ? "\n" + string.Join("\n", offenders.Take(80)) : "");
        }

        [PrimalBridgeCommand]
        public static string Items(string arg)
        {
            var sb = new StringBuilder();
            var db = Resources.Load<ItemDatabase>("ItemDatabase");
            if (!db) return "no Resources/ItemDatabase";
            var set = new HashSet<ItemDefinition>(db.items.Where(i => i));
            int nullItems = db.items.Count(i => !i), nullRecipes = db.recipes.Count(r => !r), noOutput = 0, nullIng = 0, notInDb = 0, badCount = 0;
            var dupItems = db.items.Where(i => i).GroupBy(i => i.id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            var dupRecipes = db.recipes.Where(r => r).GroupBy(r => r.id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            foreach (var r in db.recipes.Where(r => r))
            {
                if (!r.output) { noOutput++; sb.AppendLine($"  recipe {r.id}: no output"); }
                else if (!set.Contains(r.output)) { notInDb++; sb.AppendLine($"  recipe {r.id}: output {r.output.id} not in the database"); }
                if (r.ingredients == null || r.ingredients.Length == 0) { nullIng++; sb.AppendLine($"  recipe {r.id}: no ingredients"); continue; }
                foreach (var g in r.ingredients)
                {
                    if (!g.item) { nullIng++; sb.AppendLine($"  recipe {r.id}: null ingredient"); }
                    else if (!set.Contains(g.item)) { notInDb++; sb.AppendLine($"  recipe {r.id}: ingredient {g.item.id} not in the database"); }
                    if (g.count < 1) { badCount++; sb.AppendLine($"  recipe {r.id}: ingredient count {g.count}"); }
                }
            }
            int missRefs = 0;
            foreach (var o in db.items.Where(i => i).Cast<Object>().Concat(db.recipes.Where(r => r)))
            {
                var it = new SerializedObject(o).GetIterator();
                while (it.Next(true))
                    if (it.propertyType == SerializedPropertyType.ObjectReference && !SkipProp(it.propertyPath) && it.objectReferenceValue == null && it.objectReferenceInstanceIDValue != 0)
                    { missRefs++; sb.AppendLine($"  missing ref {o.name}.{it.propertyPath}"); }
            }
            int noWorld = db.items.Count(i => i && !i.worldPrefab), noIcon = db.items.Count(i => i && !i.icon);
            string head = $"ItemDatabase: items {db.items.Count} (null {nullItems}, duplicate ids {dupItems.Count}{(dupItems.Count > 0 ? ": " + string.Join(",", dupItems) : "")}, no icon {noIcon}, no worldPrefab {noWorld}), recipes {db.recipes.Count} (null {nullRecipes}, duplicate ids {dupRecipes.Count}{(dupRecipes.Count > 0 ? ": " + string.Join(",", dupRecipes) : "")}, no output {noOutput}, null / no ingredients {nullIng}, items not in the database {notInDb}, bad counts {badCount}), missing object refs on items / recipes {missRefs}";
            var res = head + "\n" + sb;
            File.WriteAllText(Path.Combine(OutDir, "Q_items.txt"), res);
            return res;
        }

        [PrimalBridgeCommand]
        public static string Animators(string arg)
        {
            var sb = new StringBuilder(); int ctrls = 0, states = 0, noMotion = 0, emptyChildren = 0;
            foreach (var g in AssetDatabase.FindAssets("t:AnimatorController", Roots.Where(AssetDatabase.IsValidFolder).ToArray()).Distinct())
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (!ac) { sb.AppendLine($"LOAD FAILED {path}"); continue; }
                ctrls++; int s = 0, nm = 0, ec = 0; var lines = new List<string>();
                void Walk(AnimatorStateMachine sm, string layer)
                {
                    foreach (var cs in sm.states)
                    {
                        s++; var st = cs.state;
                        if (!st.motion) { nm++; lines.Add($"    no motion: {layer}/{st.name}"); }
                        else if (st.motion is BlendTree bt) foreach (var ch in bt.children) if (!ch.motion) { ec++; lines.Add($"    blend tree child empty: {layer}/{st.name}"); }
                    }
                    foreach (var sub in sm.stateMachines) Walk(sub.stateMachine, layer + "/" + sub.stateMachine.name);
                }
                foreach (var l in ac.layers) if (l.stateMachine) Walk(l.stateMachine, l.name);
                states += s; noMotion += nm; emptyChildren += ec;
                sb.AppendLine($"{path}: layers {ac.layers.Length}, params {ac.parameters.Length}, states {s}, without motion {nm}, empty blend children {ec}, clips {ac.animationClips.Length}");
                foreach (var x in lines) sb.AppendLine(x);
            }
            var res = $"animator controllers {ctrls}: states {states}, without motion {noMotion}, empty blend-tree children {emptyChildren}\n" + sb;
            File.WriteAllText(Path.Combine(OutDir, "Q_animators.txt"), res);
            return res;
        }
    }
}
