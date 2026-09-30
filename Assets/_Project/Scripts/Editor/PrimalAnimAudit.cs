using System.Collections.Generic;
using System.Linq;
using System.Text;
using PrimalFrontier.Animation;
using UnityEditor;
using UnityEngine;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// DIAGNOSTIC (animation audit, 2026-09-28): review renders of the player's arms, read-only (nothing saved but PNGs).
    /// Shows what the humanoid avatar does to the arm skin: the skin bind pose as authored vs the same pose after a
    /// humanoid round trip (same joints, rolls redistributed by the avatar), and clip frames just before / after the left
    /// upper-arm roll flip the probe measured, with and without the TwistBoneDriver.
    /// Bridge: PrimalAnimAudit.RenderArmTwist "spec;spec" where spec = bind | bind_rt | Clip@seconds (empty = default set).
    /// Output: Documentation/Screenshots/Review/armtwist_*.png
    /// </summary>
    public static class PrimalAnimAudit
    {
        const string PlayerModel = "Assets/Art/Characters/Player/Model/PLAYER_Survivor.fbx";
        const string PlayerPrefab = "Assets/Art/Characters/Player/Prefab/PFB_Player_Survivor.prefab";

        [PrimalBridgeCommand]
        public static string RenderArmTwist(string arg)
        {
            var sb = new StringBuilder();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            if (!prefab) return "player prefab missing";
            var clips = AssetDatabase.LoadAllAssetsAtPath(PlayerModel).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__")).ToDictionary(c => c.name, c => c);
            string dir = "Documentation/Screenshots/Review"; System.IO.Directory.CreateDirectory(dir);
            string spec = string.IsNullOrEmpty(arg) ? "bind;bind_rt;Idle@0;Run@0.155;Run@0.18;Run@0.485;Run@0.51;Walk@0.25;Walk@0.27" : arg;
            var pru = new PreviewRenderUtility();
            bool started = !AnimationMode.InAnimationMode(); if (started) AnimationMode.StartAnimationMode();
            try
            {
                pru.camera.fieldOfView = 26f; pru.camera.nearClipPlane = 0.05f; pru.camera.farClipPlane = 50f;
                pru.camera.clearFlags = CameraClearFlags.SolidColor; pru.camera.backgroundColor = new Color(0.42f, 0.44f, 0.47f);
                pru.lights[0].intensity = 1.2f; pru.lights[0].transform.rotation = Quaternion.Euler(40f, -30f, 0f);
                pru.lights[1].intensity = 0.6f; pru.lights[1].transform.rotation = Quaternion.Euler(20f, 150f, 0f);
                pru.ambientColor = new Color(0.35f, 0.35f, 0.38f);
                pru.BeginStaticPreview(new Rect(0, 0, 64, 64)); pru.camera.Render(); Object.DestroyImmediate(pru.EndStaticPreview());   // warm-up
                foreach (var e in spec.Split(';'))
                {
                    var go = (GameObject)Object.Instantiate(prefab);
                    foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;
                    pru.AddSingleGO(go);
                    var anim = go.GetComponentInChildren<Animator>();
                    var tbd = anim.GetComponent<TwistBoneDriver>();
                    string label = e.Replace("@", "_");
                    if (e == "tpose")
                    {
                        // the avatar's T-pose as stored in the importer (humanDescription.skeleton), applied to the model
                        var mi = AssetImporter.GetAtPath(PlayerModel) as ModelImporter;
                        var byName = anim.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                        if (mi) foreach (var sk in mi.humanDescription.skeleton) if (byName.TryGetValue(sk.name, out var t) && t != anim.transform) { t.localPosition = sk.position; t.localRotation = sk.rotation; }
                    }
                    else if (e == "bind" || e == "bind_rt")
                    {
                        SetBindPose(anim);
                        if (e == "bind_rt")
                        {
                            var h = new HumanPoseHandler(anim.avatar, anim.transform); var hp = new HumanPose();
                            h.GetHumanPose(ref hp); h.SetHumanPose(ref hp); h.Dispose();
                        }
                    }
                    else
                    {
                        var cp = e.Split('@');
                        if (cp.Length < 2 || !clips.TryGetValue(cp[0], out var clip)) { sb.AppendLine(e + ": bad spec"); Object.DestroyImmediate(go); continue; }
                        float t = float.Parse(cp[1], System.Globalization.CultureInfo.InvariantCulture);
                        AnimationMode.BeginSampling(); AnimationMode.SampleAnimationClip(anim.gameObject, clip, t); AnimationMode.EndSampling();
                        label = cp[0] + "_" + Mathf.RoundToInt(t * 1000f).ToString("0000") + "ms";
                    }
                    for (int pass = 0; pass < (tbd ? 2 : 1); pass++)
                    {
                        if (pass == 1)
                        {
                            // what the game adds: TwistBoneDriver on the pose (its Awake / LateUpdate do not run in edit mode)
                            var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                            typeof(TwistBoneDriver).GetMethod("Awake", bf)?.Invoke(tbd, null);          // binds the bones + rest from the bind poses
                            typeof(TwistBoneDriver).GetMethod("LateUpdate", bf)?.Invoke(tbd, null);
                            sb.AppendLine(System.FormattableString.Invariant($"{label}: twist driver L {tbd.LeftTwist:F1} R {tbd.RightTwist:F1}"));
                        }
                        var hips = anim.GetBoneTransform(HumanBodyBones.Chest) ?? anim.GetBoneTransform(HumanBodyBones.Hips);
                        Vector3 c = hips.position;
                        var elL = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm).position; var elR = anim.GetBoneTransform(HumanBodyBones.RightLowerArm).position;
                        foreach (var (tag, yaw, pitch, dist, tgt) in new[] { ("back", 0f, 6f, 2.6f, c), ("left", 90f, 4f, 2.2f, c + go.transform.rotation * (Vector3.left * 0.25f)), ("front", 180f, 6f, 2.6f, c),
                                                                              ("closeL_front", 150f, 10f, 1.7f, elL), ("closeL_back", 30f, 10f, 1.7f, elL), ("closeL_out", 90f, 5f, 1.7f, elL),
                                                                              ("closeR_front", -150f, 10f, 1.7f, elR), ("closeR_out", -90f, 5f, 1.7f, elR) })
                        {
                            var rot = go.transform.rotation * Quaternion.Euler(pitch, yaw, 0f);
                            Vector3 target = tgt;
                            pru.camera.transform.rotation = rot;
                            pru.camera.transform.position = target - rot * Vector3.forward * dist;
                            pru.BeginStaticPreview(new Rect(0, 0, 420, 480));
                            pru.camera.Render();
                            var tex = pru.EndStaticPreview();
                            var file = $"{dir}/armtwist_{label}_{(pass == 0 ? "nodriver" : "driver")}_{tag}.png";
                            System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
                            Object.DestroyImmediate(tex);
                            sb.AppendLine(file);
                        }
                    }
                    Object.DestroyImmediate(go);
                }
            }
            finally { if (started) AnimationMode.StopAnimationMode(); pru.Cleanup(); }
            return sb.ToString();
        }

        /// <summary>open scenes and whether they have unsaved changes (check before a builder that opens a new scene)</summary>
        [PrimalBridgeCommand]
        public static string SceneState()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < UnityEditor.SceneManagement.EditorSceneManager.sceneCount; i++)
            {
                var sc = UnityEditor.SceneManagement.EditorSceneManager.GetSceneAt(i);
                sb.AppendLine($"{sc.path} loaded={sc.isLoaded} dirty={sc.isDirty}");
            }
            return sb.ToString();
        }

        /// <summary>re-open a scene after a builder left an empty one (refuses when the open scene has unsaved changes)</summary>
        [PrimalBridgeCommand]
        public static string OpenScene(string path)
        {
            var cur = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (cur.isDirty && !string.IsNullOrEmpty(cur.path)) return $"refused: {cur.path} has unsaved changes";
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(string.IsNullOrEmpty(path) ? "Assets/_Project/Scenes/Island_VerticalSlice.unity" : path, UnityEditor.SceneManagement.OpenSceneMode.Single);
            return "opened " + UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path;
        }

        /// <summary>state of the player's prefab / controller / avatar assets (after a builder run)</summary>
        [PrimalBridgeCommand]
        public static string ControllerCheck()
        {
            var sb = new StringBuilder();
            const string cp = "Assets/Art/Characters/Player/Animations/PlayerAnimator.controller";
            var ac = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(cp);
            sb.AppendLine($"controller asset: {(ac ? ac.name : "null")} guid {AssetDatabase.AssetPathToGUID(cp)} layers {(ac ? ac.layers.Length : -1)} params {(ac ? ac.parameters.Length : -1)} dirty {(ac ? EditorUtility.IsDirty(ac) : false)}");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            foreach (var an in prefab ? prefab.GetComponentsInChildren<Animator>(true) : new Animator[0])
                sb.AppendLine($"prefab animator on {an.name}: controller {(an.runtimeAnimatorController ? AssetDatabase.GetAssetPath(an.runtimeAnimatorController) : "null")} avatar {(an.avatar ? an.avatar.name + " valid " + an.avatar.isValid : "null")}");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try { var an = inst.GetComponent<Animator>(); sb.AppendLine($"instance animator: {(an ? (an.runtimeAnimatorController ? an.runtimeAnimatorController.name : "controller null") : "no animator")}"); }
            finally { Object.DestroyImmediate(inst); }
            return sb.ToString();
        }

        /// <summary>serialized values on the player prefab (in memory) that differ from the code defaults of a fresh component</summary>
        [PrimalBridgeCommand]
        public static string PlayerFieldDiff()
        {
            var sb = new StringBuilder();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player/PFB_Player.prefab");
            if (!prefab) return "no prefab";
            var sceneMotor = Object.FindFirstObjectByType<PrimalFrontier.Player.PlayerMotor>(FindObjectsInactive.Include);
            var types = new[] { typeof(PrimalFrontier.Player.PlayerMotor), typeof(PrimalFrontier.Player.PlayerAnimationDriver), typeof(PrimalFrontier.Player.PlayerIK), typeof(PrimalFrontier.Player.PlayerInteraction) };
            var tmp = new GameObject("_fielddiff") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                foreach (var t in types)
                {
                    var fresh = tmp.GetComponent(t); if (!fresh) fresh = tmp.AddComponent(t);
                    var b = new SerializedObject(fresh);
                    foreach (var (label, host) in new[] { ("prefab", prefab), ("scene", sceneMotor ? sceneMotor.gameObject : null) })
                    {
                    var onHost = host ? host.GetComponentInChildren(t, true) : null;
                    if (!onHost) { sb.AppendLine($"{label} {t.Name}: missing"); continue; }
                    var a = new SerializedObject(onHost);
                    var it = a.GetIterator(); int n = 0, d = 0;
                    for (bool enter = true; it.NextVisible(enter); enter = false)
                    {
                        var q = b.FindProperty(it.propertyPath);
                        if (q == null || it.propertyType == SerializedPropertyType.ObjectReference) continue;
                        string va = Val(it), vb = Val(q); if (va == null) continue; n++;
                        if (va != vb) { d++; sb.AppendLine($"{label} {t.Name}.{it.propertyPath}: {va} (code {vb})"); }
                    }
                    sb.AppendLine($"{label} {t.Name}: {n} values, {d} differ");
                    }
                }
            }
            finally { Object.DestroyImmediate(tmp); }
            return sb.ToString();
        }

        /// <summary>
        /// wave 2a (U): read-only snapshot of the player import for before / after diffs. Writes
        /// Library/PrimalBridge/U_import_&lt;arg&gt;.txt: controller states -> clip (every layer), the resolved component list and
        /// every object reference of PFB_Player, PFB_Player_Survivor and the scene player, missing scripts / references, and the
        /// FBX clip events vs the anim json (frames). Returns the counts.
        /// </summary>
        [PrimalBridgeCommand]
        public static string PlayerImportCheck(string arg)
        {
            var sb = new StringBuilder(); int missScripts = 0, missRefs = 0, nullMotions = 0, evMismatch = 0;
            const string cp = "Assets/Art/Characters/Player/Animations/PlayerAnimator.controller";
            var ac = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(cp);
            int states = 0;
            if (ac)
            {
                sb.AppendLine($"## controller {cp} layers {ac.layers.Length} params {ac.parameters.Length}");
                sb.AppendLine("params: " + string.Join(", ", ac.parameters.Select(x => x.name + ":" + x.type)));
                foreach (var layer in ac.layers)
                    foreach (var cs in layer.stateMachine.states)
                    {
                        var st = cs.state; states++;
                        string m = st.motion is UnityEditor.Animations.BlendTree bt ? "BlendTree(" + string.Join(" ", bt.children.Select(c => c.motion ? c.motion.name : "NULL")) + ")"
                                 : st.motion ? st.motion.name : "NULL";
                        if (!st.motion && st.name != "Empty") nullMotions++;
                        sb.AppendLine($"state {layer.name}/{st.name} -> {m} speed {st.speed:0.##} tag {st.tag} out {st.transitions.Length}");
                    }
            }
            else sb.AppendLine("## controller MISSING");
            var roots = new List<(string label, GameObject go)> {
                ("PFB_Player", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player/PFB_Player.prefab")),
                ("PFB_Player_Survivor", AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab)) };
            var sceneMotor = Object.FindFirstObjectByType<PrimalFrontier.Player.PlayerMotor>(FindObjectsInactive.Include);
            if (sceneMotor) roots.Add(("scene", sceneMotor.gameObject));
            foreach (var (label, go) in roots)
            {
                if (!go) { sb.AppendLine($"## {label}: MISSING"); continue; }
                var all = go.GetComponentsInChildren<Transform>(true);
                sb.AppendLine($"## {label}: {all.Length} transforms");
                foreach (var t in all)
                {
                    string path = t == go.transform ? go.name : AnimationUtility.CalculateTransformPath(t, go.transform);
                    var comps = t.GetComponents<Component>();
                    var names = new List<string>();
                    foreach (var c in comps)
                    {
                        if (c == null) { missScripts++; names.Add("MISSING_SCRIPT"); continue; }
                        if (c is Transform) continue;
                        string extra = c is CharacterController cc ? $"(h {cc.height:0.###} r {cc.radius:0.###} c {cc.center})"
                                     : c is CapsuleCollider cap ? $"(h {cap.height:0.###} r {cap.radius:0.###} c {cap.center} trig {cap.isTrigger})"
                                     : c is SphereCollider sph ? $"(r {sph.radius:0.###} c {sph.center} trig {sph.isTrigger})"
                                     : c is BoxCollider box ? $"(s {box.size} c {box.center} trig {box.isTrigger})" : "";
                        names.Add(c.GetType().Name + extra);
                        var so = new SerializedObject(c); var it = so.GetIterator();
                        for (bool enter = true; it.Next(enter); enter = false)
                        {
                            if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                            if (it.propertyPath == "m_Script" || it.propertyPath == "m_GameObject" || it.propertyPath.StartsWith("m_PrefabInstance") || it.propertyPath.StartsWith("m_CorrespondingSourceObject") || it.propertyPath.StartsWith("m_PrefabAsset")) continue;
                            var v = it.objectReferenceValue;
                            if (v == null && it.objectReferenceInstanceIDValue != 0) { missRefs++; sb.AppendLine($"  MISSING_REF {path} {c.GetType().Name}.{it.propertyPath}"); }
                            else if (v != null) sb.AppendLine($"  ref {path} {c.GetType().Name}.{it.propertyPath} -> {v.name} ({v.GetType().Name})");
                        }
                    }
                    if (names.Count > 0) sb.AppendLine($"obj {path}: {string.Join(", ", names)}");
                }
            }
            // FBX clip events vs the anim json
            var metaFile = "Assets/Art/Characters/Player/Animations/PLAYER_Survivor_anim.json";
            var meta = System.IO.File.Exists(metaFile) ? JsonUtility.FromJson<PrimalCharacterBuilder.AnimMeta>(System.IO.File.ReadAllText(metaFile)) : null;
            var metaBy = meta?.clips?.ToDictionary(c => c.name, c => c) ?? new Dictionary<string, PrimalCharacterBuilder.ClipMeta>();
            var clips = AssetDatabase.LoadAllAssetsAtPath(PlayerModel).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).OrderBy(c => c.name).ToList();
            sb.AppendLine($"## FBX clips {clips.Count} (json {metaBy.Count})");
            foreach (var c in clips)
            {
                var ev = AnimationUtility.GetAnimationEvents(c);
                string evs = string.Join(" ", ev.Select(e => $"{e.functionName}@{Mathf.RoundToInt(e.time * c.frameRate)}{(string.IsNullOrEmpty(e.stringParameter) ? "" : "(" + e.stringParameter + ")")}"));
                string chk = "";
                if (metaBy.TryGetValue(c.name, out var cm))
                {
                    var want = (cm.events ?? new PrimalCharacterBuilder.ClipEvent[0]).Select(e => $"{e.function}@{e.frame}").OrderBy(x => x).ToArray();
                    var got = ev.Select(e => $"{e.functionName}@{Mathf.RoundToInt(e.time / Mathf.Max(1e-4f, c.length) * cm.frames)}").OrderBy(x => x).ToArray();
                    if (!want.SequenceEqual(got)) { evMismatch++; chk = $" EVENT_MISMATCH want [{string.Join(" ", want)}]"; }
                    if (cm.loop != AnimationUtility.GetAnimationClipSettings(c).loopTime) chk += " LOOP_MISMATCH";
                }
                else chk = " NO_META";
                sb.AppendLine($"clip {c.name} {Mathf.RoundToInt(c.length * c.frameRate)}f loop {AnimationUtility.GetAnimationClipSettings(c).loopTime} ev [{evs}]{chk}");
            }
            string file = $"Library/PrimalBridge/U_import_{(string.IsNullOrEmpty(arg) ? "now" : arg)}.txt";
            System.IO.File.WriteAllText(file, sb.ToString());
            return $"{file}: controller states {states} (null motions {nullMotions}), missing scripts {missScripts}, missing refs {missRefs}, FBX clips {clips.Count}, event mismatches {evMismatch}";
        }

        static string Val(SerializedProperty p) => p.propertyType switch
        {
            SerializedPropertyType.Float => p.floatValue.ToString("R"),
            SerializedPropertyType.Integer => p.intValue.ToString(),
            SerializedPropertyType.Boolean => p.boolValue.ToString(),
            SerializedPropertyType.Enum => p.enumValueIndex.ToString(),
            SerializedPropertyType.Vector3 => p.vector3Value.ToString("R"),
            SerializedPropertyType.Vector2 => p.vector2Value.ToString("R"),
            _ => null
        };

        /// <summary>every skinned bone to its skin bind pose (the pose the mesh was bound in)</summary>
        static void SetBindPose(Animator anim)
        {
            SkinnedMeshRenderer body = null;
            foreach (var smr in anim.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.sharedMesh && smr.bones.Length == smr.sharedMesh.bindposes.Length && (!body || smr.bones.Length > body.bones.Length)) body = smr;
            if (!body) return;
            var bp = body.sharedMesh.bindposes; var bones = body.bones; var toWorld = body.transform.localToWorldMatrix;
            var order = Enumerable.Range(0, bones.Length).Where(i => bones[i]).OrderBy(i => Depth(bones[i])).ToArray();
            foreach (int i in order) { var m = toWorld * bp[i].inverse; bones[i].SetPositionAndRotation(m.GetColumn(3), m.rotation); }
        }

        static int Depth(Transform t) { int d = 0; while (t.parent) { d++; t = t.parent; } return d; }
    }
}
