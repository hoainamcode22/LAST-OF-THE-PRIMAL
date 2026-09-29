using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PrimalFrontier.Animation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;

using PA = PrimalFrontier.Animation.PlayerActions;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// Character + dinosaur integration: import settings (Humanoid / Generic), clip settings + AnimationEvents from the
    /// Blender *_anim.json, URP materials, Animator Controllers, prefabs (Animator, colliders, hit zones, LODGroup) and an
    /// automated test pass (avatar, clips, NaN, loop continuity, foot sliding, scale, colliders) with review screenshots.
    /// Menu: Primal Frontier/Characters/... ; batch: -executeMethod PrimalFrontier.EditorTools.PrimalCharacterBuilder.BuildFromCommandLine -character Player
    /// </summary>
    public static partial class PrimalCharacterBuilder
    {
        [Serializable] public class ClipEvent { public int frame; public string function; public string param; }
        [Serializable] public class ClipMeta { public string name; public int frames; public bool loop; public float speed; public string notes; public string move; public ClipEvent[] events; }
        [Serializable] public class AnimMeta { public string character; public string rig; public int fps; public ClipMeta[] clips; }

        public class Spec
        {
            public string Id;             // "Player", "Triceratops"...
            public string Folder;         // Assets/Art/Characters/Player
            public string Fbx;            // model file name (no extension)
            public bool Humanoid;
            public string Prefab;         // PFB_...
            public float ExpectedHeight;  // metres (bounds height in rest pose), tolerance 15 %
        }

        public static readonly Dictionary<string, Spec> Specs = new Dictionary<string, Spec>
        {
            { "Player", new Spec { Id = "Player", Folder = "Assets/Art/Characters/Player", Fbx = "PLAYER_Survivor", Humanoid = true, Prefab = "PFB_Player_Survivor", ExpectedHeight = 1.8f } },
        };

        static readonly StringBuilder Log = new StringBuilder();
        static int _fails;
        static void L(string s) { Log.AppendLine(s); Debug.Log("[PrimalCharacterBuilder] " + s); }
        static void F(string s) { _fails++; Log.AppendLine("FAIL: " + s); Debug.LogError("[PrimalCharacterBuilder] " + s); }

        public static void BuildFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-character");
            string id = i >= 0 && i + 1 < args.Length ? args[i + 1] : "Player";
            int code = 0;
            try { if (!BuildAndTest(id)) code = 2; }
            catch (Exception e) { Debug.LogError("[PrimalCharacterBuilder] " + e); code = 1; }
            EditorApplication.Exit(code);
        }

        /// <summary>Phase-1 rig check: import the rigged model (no clips needed), validate the humanoid avatar, report meshes.</summary>
        public static void RigCheckFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-character");
            string id = i >= 0 && i + 1 < args.Length ? args[i + 1] : "Player";
            int code = 0;
            try
            {
                Log.Clear(); _fails = 0;
                var spec = Specs[id];
                AssetDatabase.Refresh();
                string fbx = $"{spec.Folder}/Model/{spec.Fbx}.fbx";
                ConfigureImporter(spec, fbx, null);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var m = smr.sharedMesh;
                    L($"Mesh {smr.name}: tris={m.triangles.Length / 3} verts={m.vertexCount} submeshes={m.subMeshCount} bones={smr.bones.Length} blendshapes={m.blendShapeCount} bounds={smr.bounds.size}");
                }
                var inst = (GameObject)UnityEngine.Object.Instantiate(model);
                var anim = inst.GetOrAdd<Animator>();
                anim.avatar = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Avatar>().FirstOrDefault();
                if (anim.avatar != null && anim.avatar.isHuman)
                {
                    foreach (HumanBodyBones hb in new[] { HumanBodyBones.Hips, HumanBodyBones.Head, HumanBodyBones.LeftHand, HumanBodyBones.RightFoot, HumanBodyBones.LeftIndexDistal })
                        L($"  {hb} -> {(anim.GetBoneTransform(hb) ? anim.GetBoneTransform(hb).name : "NONE")}");
                    var hd = anim.GetBoneTransform(HumanBodyBones.Head).position; var ft = anim.GetBoneTransform(HumanBodyBones.LeftFoot).position;
                    L($"  head y {hd.y:F3}, left foot y {ft.y:F3}, left hand x {anim.GetBoneTransform(HumanBodyBones.LeftHand).position.x:F3}");
                    var ls = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm).position; var rs = anim.GetBoneTransform(HumanBodyBones.RightUpperArm).position;
                    var fwd = Vector3.Cross(Vector3.up, ls - rs).normalized;
                    var toe = anim.GetBoneTransform(HumanBodyBones.LeftToes).position - anim.GetBoneTransform(HumanBodyBones.LeftFoot).position; toe.y = 0;
                    L($"  facing (shoulders) {fwd}, foot->toe {toe.normalized}");
                    if (fwd.z < 0.8f) F("character does not face +Z");
                }
                UnityEngine.Object.DestroyImmediate(inst);
                Directory.CreateDirectory("Documentation/CharacterTests");
                File.WriteAllText($"Documentation/CharacterTests/{id}_rig_check.md", $"# {id} rig check\n\nResult: **{(_fails == 0 ? "PASS" : "FAIL")}**\n\n```\n{Log}```\n");
                L(_fails == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({_fails})");
                if (_fails > 0) code = 2;
            }
            catch (Exception e) { Debug.LogError("[PrimalCharacterBuilder] " + e); code = 1; }
            EditorApplication.Exit(code);
        }

        [MenuItem("Primal Frontier/Advanced (overwrites hand edits)/Re-import Player Model + Test", priority = 110)]
        public static void MenuPlayer() { if (PrimalSceneBaker.ConfirmRegenerate("The player model prefab (from the Blender export)")) BuildAndTest("Player"); }

        [PrimalBridgeCommand]
        public static bool BuildAndTest(string id)
        {
            Log.Clear(); _fails = 0;
            var spec = GetSpec(id);
            L($"=== {id} === {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            AssetDatabase.Refresh();
            string fbx = $"{spec.Folder}/Model/{spec.Fbx}.fbx";
            var meta = LoadMeta(spec);
            ConfigureImporter(spec, fbx, meta);
            var mats = BuildMaterials(spec);
            RemapMaterials(fbx, mats);
            var clips = LoadClips(fbx);
            L($"Clips in FBX: {clips.Count} (meta: {meta?.clips?.Length ?? 0})");
            var controller = spec.Humanoid ? BuildPlayerController(spec, clips) : BuildDinoController(spec, clips, meta);
            // write the controller before the prefab references it: an unsaved (dirty) controller was not resolved by the saved
            // prefab (2026-09-28: the prefab's Animator came back with a null controller, disk file = empty stub)
            EditorUtility.SetDirty(controller); AssetDatabase.SaveAssetIfDirty(controller);
            var prefab = BuildPrefab(spec, fbx, controller);
            bool ok = Test(spec, prefab, clips, meta);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Documentation/CharacterTests");
            File.WriteAllText($"Documentation/CharacterTests/{id}_test.md", $"# {id} automated test\n\nResult: **{(ok ? "PASS" : "FAIL")}**\n\n```\n{Log}```\n");
            return ok;
        }

        // ------------------------------------------------------------------ import
        static AnimMeta LoadMeta(Spec spec)
        {
            var dir = $"{spec.Folder}/Animations";
            var file = Directory.Exists(dir) ? Directory.GetFiles(dir, "*_anim.json").FirstOrDefault() : null;
            if (file == null) { F("anim meta json missing"); return null; }
            var meta = JsonUtility.FromJson<AnimMeta>(File.ReadAllText(file));
            // phase C: the Character agent's clips_manifest.json (same clip schema: name, frames, loop, speed, events) placed next to
            // the anim json adds / overrides clip entries (event frames for the new clips)
            string man = $"{dir}/clips_manifest.json";
            if (File.Exists(man))
            {
                try
                {
                    var extra = JsonUtility.FromJson<AnimMeta>(File.ReadAllText(man));
                    if (extra?.clips != null && extra.clips.Length > 0)
                    {
                        var by = (meta.clips ?? new ClipMeta[0]).ToDictionary(c => c.name, c => c);
                        foreach (var c in extra.clips) if (c != null && !string.IsNullOrEmpty(c.name)) by[c.name] = c;
                        meta.clips = by.Values.ToArray();
                        L($"clips_manifest.json: {extra.clips.Length} clip entries merged");
                    }
                }
                catch (Exception e) { F("clips_manifest.json unreadable: " + e.Message); }
            }
            return meta;
        }

        static readonly (HumanBodyBones hb, string bone)[] HumanMap =
        {
            (HumanBodyBones.Hips, "Pelvis"), (HumanBodyBones.Spine, "Spine"), (HumanBodyBones.Chest, "Spine_Upper"), (HumanBodyBones.UpperChest, "Chest"),
            (HumanBodyBones.Neck, "Neck"), (HumanBodyBones.Head, "Head"),
            (HumanBodyBones.LeftShoulder, "Clavicle_L"), (HumanBodyBones.LeftUpperArm, "UpperArm_L"), (HumanBodyBones.LeftLowerArm, "LowerArm_L"), (HumanBodyBones.LeftHand, "Hand_L"),
            (HumanBodyBones.RightShoulder, "Clavicle_R"), (HumanBodyBones.RightUpperArm, "UpperArm_R"), (HumanBodyBones.RightLowerArm, "LowerArm_R"), (HumanBodyBones.RightHand, "Hand_R"),
            (HumanBodyBones.LeftUpperLeg, "Thigh_L"), (HumanBodyBones.LeftLowerLeg, "Calf_L"), (HumanBodyBones.LeftFoot, "Foot_L"), (HumanBodyBones.LeftToes, "Toe_L"),
            (HumanBodyBones.RightUpperLeg, "Thigh_R"), (HumanBodyBones.RightLowerLeg, "Calf_R"), (HumanBodyBones.RightFoot, "Foot_R"), (HumanBodyBones.RightToes, "Toe_R"),
            (HumanBodyBones.LeftThumbProximal, "Thumb_01_L"), (HumanBodyBones.LeftThumbIntermediate, "Thumb_02_L"), (HumanBodyBones.LeftThumbDistal, "Thumb_03_L"),
            (HumanBodyBones.LeftIndexProximal, "Index_01_L"), (HumanBodyBones.LeftIndexIntermediate, "Index_02_L"), (HumanBodyBones.LeftIndexDistal, "Index_03_L"),
            (HumanBodyBones.LeftMiddleProximal, "Middle_01_L"), (HumanBodyBones.LeftMiddleIntermediate, "Middle_02_L"), (HumanBodyBones.LeftMiddleDistal, "Middle_03_L"),
            (HumanBodyBones.LeftRingProximal, "Ring_01_L"), (HumanBodyBones.LeftRingIntermediate, "Ring_02_L"), (HumanBodyBones.LeftRingDistal, "Ring_03_L"),
            (HumanBodyBones.LeftLittleProximal, "Pinky_01_L"), (HumanBodyBones.LeftLittleIntermediate, "Pinky_02_L"), (HumanBodyBones.LeftLittleDistal, "Pinky_03_L"),
            (HumanBodyBones.RightThumbProximal, "Thumb_01_R"), (HumanBodyBones.RightThumbIntermediate, "Thumb_02_R"), (HumanBodyBones.RightThumbDistal, "Thumb_03_R"),
            (HumanBodyBones.RightIndexProximal, "Index_01_R"), (HumanBodyBones.RightIndexIntermediate, "Index_02_R"), (HumanBodyBones.RightIndexDistal, "Index_03_R"),
            (HumanBodyBones.RightMiddleProximal, "Middle_01_R"), (HumanBodyBones.RightMiddleIntermediate, "Middle_02_R"), (HumanBodyBones.RightMiddleDistal, "Middle_03_R"),
            (HumanBodyBones.RightRingProximal, "Ring_01_R"), (HumanBodyBones.RightRingIntermediate, "Ring_02_R"), (HumanBodyBones.RightRingDistal, "Ring_03_R"),
            (HumanBodyBones.RightLittleProximal, "Pinky_01_R"), (HumanBodyBones.RightLittleIntermediate, "Pinky_02_R"), (HumanBodyBones.RightLittleDistal, "Pinky_03_R"),
        };

        static void ConfigureImporter(Spec spec, string fbx, AnimMeta meta)
        {
            var mi = AssetImporter.GetAtPath(fbx) as ModelImporter;
            if (mi == null) throw new Exception("model not found: " + fbx);
            mi.globalScale = 1f; mi.useFileScale = true; mi.bakeAxisConversion = true;
            mi.importCameras = false; mi.importLights = false; mi.importVisibility = false; mi.importBlendShapes = true; mi.importBlendShapeNormals = ModelImporterNormals.Calculate;
            mi.importNormals = ModelImporterNormals.Import; mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            mi.skinWeights = ModelImporterSkinWeights.Standard;             // 4 bones per vertex (mobile friendly)
            mi.optimizeGameObjects = false;                                 // keep bones for sockets / hit zones
            mi.importAnimation = true;
            mi.animationCompression = ModelImporterAnimationCompression.Optimal;
            mi.animationRotationError = 0.3f; mi.animationPositionError = 0.3f; mi.animationScaleError = 0.5f;
            mi.resampleCurves = true;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.animationType = spec.Humanoid ? ModelImporterAnimationType.Human : ModelImporterAnimationType.Generic;
            mi.SaveAndReimport();

            if (spec.Humanoid)
            {
                // explicit, deterministic humanoid mapping + enforced T-pose (the Blender rest pose is an A-pose)
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                var all = model.GetComponentsInChildren<Transform>(true);
                var byName = all.ToDictionary(t => t.name, t => t);
                var human = new List<HumanBone>();
                foreach (var (hb, bone) in HumanMap)
                {
                    if (!byName.ContainsKey(bone)) { F("humanoid bone missing in FBX: " + bone); continue; }
                    human.Add(new HumanBone { boneName = bone, humanName = HumanTrait.BoneName[(int)hb], limit = new HumanLimit { useDefaultValues = true } });
                }
                var skel = new List<SkeletonBone>();
                // T-pose = the SKIN BIND POSE (the pose the mesh was bound in: the Blender rest A-pose) with the arms raised to
                // horizontal. Never the model's node transforms: the FBX nodes can hold an animated frame (audit 2026-09-28: the
                // left arm crossed the chest, so every left-arm muscle wrapped at 180 deg). Arm target by side: the model faces
                // +Z, so the character's left is -X and its right is +X.
                var inst = UnityEngine.Object.Instantiate(model);
                try
                {
                    inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    int bound = ApplySkinBindPose(inst);
                    if (bound == 0) F("T-pose: no skinned mesh with bind poses, T-pose from the node transforms");
                    else L($"T-pose: from the skin bind pose ({bound} bones), arms to horizontal by side (L = -X, R = +X)");
                    var t2 = inst.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.First().name, g => g.First());
                    foreach (var side in new[] { "L", "R" })
                    {
                        var target = inst.transform.rotation * (side == "L" ? Vector3.left : Vector3.right);
                        var ua = t2["UpperArm_" + side]; var la = t2["LowerArm_" + side]; var ha = t2["Hand_" + side];
                        ua.rotation = Quaternion.FromToRotation((la.position - ua.position).normalized, target) * ua.rotation;
                        la.rotation = Quaternion.FromToRotation((ha.position - la.position).normalized, target) * la.rotation;
                        // hand straight along the forearm (wrist muscles centred); the roll (palm) stays as bound
                        if (t2.TryGetValue("Middle_01_" + side, out var mid))
                            ha.rotation = Quaternion.FromToRotation((mid.position - ha.position).normalized, target) * ha.rotation;
                        L(System.FormattableString.Invariant($"T-pose {side}: upper arm {inst.transform.InverseTransformDirection((la.position - ua.position).normalized)}, forearm {inst.transform.InverseTransformDirection((ha.position - la.position).normalized)}"));
                    }
                    // mirror check: left bone vs mirrored right bone (x -> -x); a symmetric rig gives the same angle on every arm bone
                    var mirror = new StringBuilder("T-pose mirror L vs R (deg):");
                    foreach (var b in new[] { "Clavicle", "UpperArm", "LowerArm", "Hand", "Thigh", "Calf", "Foot" })
                        if (t2.TryGetValue(b + "_L", out var tl) && t2.TryGetValue(b + "_R", out var tr))
                        {
                            var ql = tl.rotation; var m = new Quaternion(ql.x, -ql.y, -ql.z, ql.w);
                            mirror.Append(System.FormattableString.Invariant($" {b} {Quaternion.Angle(m, tr.rotation):F1}"));
                        }
                    L(mirror.ToString());
                    foreach (var t in inst.GetComponentsInChildren<Transform>(true))
                        skel.Add(new SkeletonBone { name = t == inst.transform ? model.name : t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale });
                }
                finally { UnityEngine.Object.DestroyImmediate(inst); }
                var hd = mi.humanDescription;
                hd.human = human.ToArray(); hd.skeleton = skel.ToArray();
                // forearm twist bones: TwistBoneDriver spreads the wrist twist, so the avatar must not roll the lower arm too
                bool twistBones = byName.ContainsKey("LowerArmTwist_L");
                // upper arm roll stays on the upper arm bone (as authored; no upper-arm twist bone): bind-pose round trip 3.1 deg vs 4.3 at
                // 0.5 (probe E twist sweep, 2026-09-28); forearm roll goes to the hand, the TwistBoneDriver spreads it (lowerArmTwist 0)
                hd.upperArmTwist = 1f; hd.lowerArmTwist = twistBones ? 0f : 0.5f; hd.upperLegTwist = 0.5f; hd.lowerLegTwist = 0.5f;
                L(twistBones ? "Twist bones LowerArmTwist_L/R found: avatar lowerArmTwist = 0 (TwistBoneDriver spreads the wrist twist)"
                             : "No forearm twist bones: avatar lowerArmTwist = 0.5");
                hd.armStretch = 0.05f; hd.legStretch = 0.05f; hd.feetSpacing = 0f; hd.hasTranslationDoF = false;
                mi.humanDescription = hd;
                mi.SaveAndReimport();
            }
            // clip settings from the Blender meta
            var defaults = mi.defaultClipAnimations;
            var metaBy = meta?.clips?.ToDictionary(c => c.name, c => c) ?? new Dictionary<string, ClipMeta>();
            var list = new List<ModelImporterClipAnimation>();
            foreach (var d in defaults)
            {
                string name = d.takeName.Contains("|") ? d.takeName.Substring(d.takeName.LastIndexOf('|') + 1) : d.takeName;
                var c = d; c.name = name;
                metaBy.TryGetValue(name, out var cm);
                bool loop = cm != null && cm.loop;
                c.loopTime = loop; c.loopPose = spec.Humanoid && loop;
                c.lockRootRotation = true; c.keepOriginalOrientation = true;
                c.lockRootHeightY = true; c.keepOriginalPositionY = true;
                c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;
                if (cm != null && cm.events != null && cm.events.Length > 0 && cm.frames > 0)
                    c.events = cm.events.Select(e => new AnimationEvent { functionName = e.function, stringParameter = e.param ?? "", time = Mathf.Clamp01(e.frame / (float)cm.frames) }).ToArray();
                else if (DefaultHitAt.TryGetValue(name, out float hitAt))
                {
                    c.events = new[] { new AnimationEvent { functionName = "OnAttackStart", time = 0.1f }, new AnimationEvent { functionName = "OnAttackActive", time = Mathf.Max(0.12f, hitAt - 0.06f) },
                                       new AnimationEvent { functionName = "OnAttackHit", time = hitAt }, new AnimationEvent { functionName = "OnAttackEnd", time = Mathf.Min(0.9f, hitAt + 0.2f) } };
                    L($"{name}: no events in the clip meta, default OnAttackStart / OnAttackActive / OnAttackHit ({hitAt:F2}) / OnAttackEnd used");
                }
                else if (DefaultGatherHitAt.TryGetValue(name, out float gatherAt))
                {
                    c.events = new[] { new AnimationEvent { functionName = gatherAt < 0f ? "OnUseItem" : "OnGatherHit", time = Mathf.Abs(gatherAt) } };
                    L($"{name}: no events in the clip meta, default {c.events[0].functionName} at {Mathf.Abs(gatherAt):F2}");
                }
                else c.events = new AnimationEvent[0];
                if (cm == null) { if (PhaseCClips.Contains(name)) L("new clip without meta (defaults): " + name); else F("clip without meta: " + name); }
                if (cm == null && PhaseCClips.Contains(name)) { c.loopTime = LoopByDefault.Contains(name); c.loopPose = c.loopTime; }
                list.Add(c);
            }
            mi.clipAnimations = list.ToArray();
            mi.SaveAndReimport();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Avatar>().FirstOrDefault();
            if (avatar == null) F("no avatar generated");
            else
            {
                L($"Avatar: valid={avatar.isValid} human={avatar.isHuman}");
                if (!avatar.isValid) F("avatar invalid");
                if (spec.Humanoid && !avatar.isHuman) F("avatar is not humanoid");
            }
            L($"Importer: {list.Count} clips configured ({list.Count(c => c.loopTime)} loops, {list.Sum(c => c.events.Length)} events)");
        }

        /// <summary>puts every bone of the largest skinned mesh at its bind pose (parents first); bones outside the skin keep their
        /// local transform, so they follow. Returns the number of bones placed.</summary>
        static int ApplySkinBindPose(GameObject inst)
        {
            SkinnedMeshRenderer body = null;
            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.sharedMesh && smr.bones.Length == smr.sharedMesh.bindposes.Length && (!body || smr.bones.Length > body.bones.Length)) body = smr;
            if (!body) return 0;
            var bp = body.sharedMesh.bindposes; var bones = body.bones; var toWorld = body.transform.localToWorldMatrix;
            int Depth(Transform t) { int d = 0; while (t.parent) { d++; t = t.parent; } return d; }
            int n = 0;
            foreach (int i in Enumerable.Range(0, bones.Length).Where(i => bones[i]).OrderBy(i => Depth(bones[i])))
            {
                var m = toWorld * bp[i].inverse;
                bones[i].SetPositionAndRotation(m.GetColumn(3), m.rotation); n++;
            }
            return n;
        }

        static List<AnimationClip> LoadClips(string fbx) =>
            AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).OrderBy(c => c.name).ToList();

        // ------------------------------------------------------------------ materials
        static Shader Lit => Shader.Find("Universal Render Pipeline/Lit");

        static Dictionary<string, Material> BuildMaterials(Spec spec)
        {
            var res = new Dictionary<string, Material>();
            var fbx = $"{spec.Folder}/Model/{spec.Fbx}.fbx";
            var mi = AssetImporter.GetAtPath(fbx) as ModelImporter;
            var names = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Material>().Select(m => m.name).ToList();
            names.AddRange(mi.GetExternalObjectMap().Where(kv => kv.Key.type == typeof(Material)).Select(kv => kv.Key.name));
            names = names.Distinct().ToList();
            foreach (var n in names)
            {
                string path = $"{spec.Folder}/Materials/{n}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) { mat = new Material(Lit) { name = n }; AssetDatabase.CreateAsset(mat, path); }
                string tex = n.StartsWith("M_") ? n.Substring(2) : n;
                tex = SharedTextureSet(spec.Folder, tex);
                var d = AssetDatabase.LoadAssetAtPath<Texture2D>($"{spec.Folder}/Textures/T_{tex}_D.png");
                var nm = AssetDatabase.LoadAssetAtPath<Texture2D>($"{spec.Folder}/Textures/T_{tex}_N.png");
                var m = AssetDatabase.LoadAssetAtPath<Texture2D>($"{spec.Folder}/Textures/T_{tex}_M.png");
                FixTexture($"{spec.Folder}/Textures/T_{tex}_N.png", normal: true);
                FixTexture($"{spec.Folder}/Textures/T_{tex}_M.png", linear: true);
                FixTexture($"{spec.Folder}/Textures/T_{tex}_D.png", alpha: n.Contains("Hair"));
                if (d == null) F($"material {n}: texture T_{tex}_D missing");
                mat.SetTexture("_BaseMap", d); mat.SetColor("_BaseColor", Color.white);
                if (nm != null) { mat.SetTexture("_BumpMap", nm); mat.EnableKeyword("_NORMALMAP"); }
                if (m != null)
                {
                    mat.SetTexture("_MetallicGlossMap", m); mat.EnableKeyword("_METALLICSPECGLOSSMAP"); mat.SetFloat("_Smoothness", 1f);
                    mat.SetTexture("_OcclusionMap", m); mat.EnableKeyword("_OCCLUSIONMAP"); mat.SetFloat("_OcclusionStrength", 1f);
                }
                else mat.SetFloat("_Smoothness", n.Contains("Eye") ? 0.85f : 0.3f);
                mat.SetFloat("_Metallic", 0f);
                if (n.Contains("Membrane")) { mat.SetFloat("_Cull", 0f); mat.doubleSidedGI = true; }
                if (n.Contains("Hair"))
                {
                    mat.SetFloat("_AlphaClip", 1f); mat.SetFloat("_Cutoff", 0.45f); mat.EnableKeyword("_ALPHATEST_ON");
                    mat.SetFloat("_Cull", 0f); mat.renderQueue = (int)RenderQueue.AlphaTest; mat.SetOverrideTag("RenderType", "TransparentCutout");
                }
                mat.enableInstancing = true;
                EditorUtility.SetDirty(mat);
                res[n] = mat;
            }
            L($"Materials: {string.Join(", ", res.Keys)}");
            return res;
        }

        /// <summary>older dinosaur exports ship the eye / membrane sets as byte copies of the skin atlas; share the skin
        /// textures instead of loading the same 2048 maps two or three times</summary>
        static string SharedTextureSet(string folder, string tex)
        {
            int k = tex.LastIndexOf('_'); if (k < 0) return tex;
            string suffix = tex.Substring(k + 1); if (suffix != "Eye" && suffix != "Membrane") return tex;
            string baseTex = tex.Substring(0, k);
            string a = $"{folder}/Textures/T_{tex}_D.png", b = $"{folder}/Textures/T_{baseTex}_D.png";
            if (!File.Exists(a)) return File.Exists(b) ? baseTex : tex;
            if (!File.Exists(b)) return tex;
            var fa = new FileInfo(a); var fb = new FileInfo(b);
            if (fa.Length != fb.Length) return tex;
            using (var md5 = System.Security.Cryptography.MD5.Create())
                return BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(a))) == BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(b))) ? baseTex : tex;
        }

        /// <summary>PC keeps the authored size (hero creatures ship 4096 skin atlases), phones get a 1024 ASTC copy</summary>
        static void FixTexture(string path, bool normal = false, bool linear = false, bool alpha = false)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) return;
            bool dirty = false;
            if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; dirty = true; }
            if (linear && ti.sRGBTexture) { ti.sRGBTexture = false; dirty = true; }
            if (alpha && !ti.alphaIsTransparency) { ti.alphaIsTransparency = true; ti.mipMapsPreserveCoverage = true; ti.alphaTestReferenceValue = 0.45f; dirty = true; }
            int src = 2048;
            try
            {   // PNG header: width / height big-endian at bytes 16..23
                var b = new byte[24]; using (var fs = File.OpenRead(path)) fs.Read(b, 0, 24);
                int w = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19], h = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
                if (w > 0 && h > 0) src = Mathf.Max(w, h);
            }
            catch { }
            int pc = Mathf.Clamp(Mathf.NextPowerOfTwo(src), 256, 4096);
            if (ti.maxTextureSize != pc) { ti.maxTextureSize = pc; dirty = true; }
            int mobile = Mathf.Min(pc, path.Contains("_Eye_") ? 256 : path.Contains("_Mouth_") ? 512 : 1024);
            foreach (var plat in new[] { "Android", "iPhone" })
            {
                var ps = ti.GetPlatformTextureSettings(plat);
                if (!ps.overridden || ps.maxTextureSize != mobile || ps.format != TextureImporterFormat.ASTC_6x6)
                {
                    ps.overridden = true; ps.maxTextureSize = mobile; ps.format = TextureImporterFormat.ASTC_6x6; ps.compressionQuality = 50;
                    ti.SetPlatformTextureSettings(ps); dirty = true;
                }
            }
            if (dirty) ti.SaveAndReimport();
        }

        static void RemapMaterials(string fbx, Dictionary<string, Material> mats)
        {
            var mi = AssetImporter.GetAtPath(fbx) as ModelImporter;
            foreach (var kv in mats) mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
            mi.SaveAndReimport();
        }

        // ------------------------------------------------------------------ controller (player)
        /// <summary>a clip that may not be in the FBX yet (phase C): null + a log line instead of a failure</summary>
        static AnimationClip Opt(List<AnimationClip> clips, string name)
        {
            var c = clips.FirstOrDefault(x => x.name == name);
            if (c == null) L("waiting for clip (state skipped): " + name);
            return c;
        }

        /// <summary>clips the Character agent delivers in phase C: a missing meta entry is logged, not failed</summary>
        static readonly HashSet<string> PhaseCClips = new HashSet<string> { "Gather_Enter", "Gather_Exit", "Walk_Start", "Walk_Stop", "Run_Start", "Run_Stop",
            "Run_Pivot_180", "Turn_180", "Kick", "Unarmed_Block", "Collect_Water", "Butcher",
            "Bow_Equip", "Bow_Idle", "Bow_Nock", "Bow_FullDraw", "Spear_Idle", "Spear_Recovery",
            // phase 3.5 (bare hands, new gathers, bandage)
            "BareHand_Idle", "BareHand_Punch_1", "BareHand_Punch_2", "BareHand_Punch_3", "BareHand_Heavy", "BareHand_HitReaction", "BareHand_Combo_End",
            "Gather_Stone_Hand", "Gather_Branch", "Bandage_Use" };

        /// <summary>new clips that loop when the manifest has no entry for them</summary>
        static readonly HashSet<string> LoopByDefault = new HashSet<string> { "Unarmed_Block", "BareHand_Idle", "Butcher", "Bow_Idle", "Spear_Idle", "Bow_FullDraw",
            "Gather_Stone_Hand", "Gather_Branch" };

        /// <summary>default attack events for unarmed clips exported without events (fraction of the clip at the contact); the clip meta wins</summary>
        static readonly Dictionary<string, float> DefaultHitAt = new Dictionary<string, float> {
            { "BareHand_Punch_1", 0.33f }, { "BareHand_Punch_2", 0.33f }, { "BareHand_Punch_3", 0.38f }, { "BareHand_Heavy", 0.42f }, { "Kick", 0.42f } };
        /// <summary>default gather contact (OnGatherHit) for new gather clips without events; negative = OnUseItem (bandage)</summary>
        static readonly Dictionary<string, float> DefaultGatherHitAt = new Dictionary<string, float> { { "Gather_Stone_Hand", 0.5f }, { "Gather_Branch", 0.5f }, { "Bandage_Use", -0.6f } };

        /// <summary>
        /// phase 3.5: a state whose clip is not in the FBX yet plays an existing clip meanwhile, so the gameplay (ids, events,
        /// tests) runs now; the real clip replaces it at the next build once it is exported
        /// </summary>
        static AnimationClip Placeholder(List<AnimationClip> clips, string name, string stand)
        {
            var c = clips.FirstOrDefault(x => x.name == name);
            if (c != null) return c;
            L($"placeholder clip for {name}: {stand} (waiting for the clip)");
            return Clip(clips, stand);
        }

        static AnimationClip Clip(List<AnimationClip> clips, string name)
        {
            var c = clips.FirstOrDefault(x => x.name == name);
            if (c == null) F("clip missing: " + name);
            return c;
        }

        static AnimatorController NewController(string path)
        {
            AssetDatabase.DeleteAsset(path);
            return AnimatorController.CreateAnimatorControllerAtPath(path);
        }

        static AnimatorStateTransition T(AnimatorState from, AnimatorState to, float dur, bool exit = false, float exitTime = 0.9f)
        {
            var t = from.AddTransition(to); t.duration = dur; t.hasExitTime = exit; t.exitTime = exitTime; t.hasFixedDuration = true; return t;
        }

        static AnimatorController BuildPlayerController(Spec spec, List<AnimationClip> clips)
        {
            // PlayerAnimator: parameters from the vertical-slice brief (+ TurnSpeed for turn-in-place)
            string path = $"{spec.Folder}/Animations/PlayerAnimator.controller";
            AssetDatabase.DeleteAsset($"{spec.Folder}/Animations/AC_Player.controller");
            var ac = NewController(path);
            foreach (var (n, t) in new (string, AnimatorControllerParameterType)[] {
                ("Speed", AnimatorControllerParameterType.Float), ("IsGrounded", AnimatorControllerParameterType.Bool),
                ("VerticalVelocity", AnimatorControllerParameterType.Float), ("IsCrouching", AnimatorControllerParameterType.Bool),
                ("IsAttacking", AnimatorControllerParameterType.Bool), ("Action", AnimatorControllerParameterType.Int),
                ("HealthState", AnimatorControllerParameterType.Int), ("TurnSpeed", AnimatorControllerParameterType.Float),
                ("IdleVariant", AnimatorControllerParameterType.Trigger),
                // combat / aim locomotion (PlayerAnimationDriver) and weapon state (WeaponAnimatorBridge)
                ("VelX", AnimatorControllerParameterType.Float), ("VelZ", AnimatorControllerParameterType.Float),
                ("Strafe", AnimatorControllerParameterType.Bool), ("CombatMode", AnimatorControllerParameterType.Bool),
                ("WeaponType", AnimatorControllerParameterType.Int), ("IsMoving", AnimatorControllerParameterType.Bool),
                ("AttackSpeed", AnimatorControllerParameterType.Float), ("FullBodyBusy", AnimatorControllerParameterType.Bool),
                // light hit: additive flinch on the HitReaction layer (PlayerAnimationDriver.Hurt(false))
                ("HurtLight", AnimatorControllerParameterType.Trigger),
                // Locomotion state speed (Idle time-scaled to Walk's cycle), phase C start / stop / pivot selection
                ("LocoRate", AnimatorControllerParameterType.Float), ("LocoIdleRate", AnimatorControllerParameterType.Float), ("UseLocoClips", AnimatorControllerParameterType.Bool),
                ("LocoEvent", AnimatorControllerParameterType.Int), ("LocoMirror", AnimatorControllerParameterType.Bool) })
                ac.AddParameter(n, t);
            var p = ac.parameters;
            // Idle's rate inside the time-synced Locomotion tree: Idle is time-scaled to Walk's cycle, the state speed brings it back
            var idleClip = Clip(clips, "Idle"); var walkClip = Clip(clips, "Walk");
            float idleScale = idleClip && walkClip && walkClip.length > 0.01f ? Mathf.Max(1f, idleClip.length / walkClip.length) : 1f;
            foreach (var x in p)
            {
                if (x.name == "IsGrounded") x.defaultBool = true; else if (x.name == "AttackSpeed") x.defaultFloat = 1f;
                else if (x.name == "LocoRate") x.defaultFloat = 1f;                  // no driver: normal rate (walk / run correct, Idle plays fast)
                else if (x.name == "LocoIdleRate") x.defaultFloat = 1f / idleScale;  // PlayerAnimationDriver: LocoRate while standing still
            }
            ac.parameters = p;
            var sm = ac.layers[0].stateMachine;
            // locomotion: 1D on planar speed (m/s); thresholds = the clips' authored speeds so feet do not slide
            var loco = ac.CreateBlendTreeInController("Locomotion", out var bt, 0);
            bt.blendType = BlendTreeType.Simple1D; bt.blendParameter = "Speed"; bt.useAutomaticThresholds = false;
            bt.AddChild(idleClip, 0f); bt.AddChild(walkClip, 1.35f); bt.AddChild(Clip(clips, "Run"), 3.8f); bt.AddChild(Clip(clips, "Sprint"), 6.2f);
            // 1D trees sync the children's normalized time: Idle (6 s) mixed with Walk (1 s) slowed the steps to 0.3x at 0.7 m/s.
            // Idle plays idleScale x faster inside the tree (same cycle as Walk); LocoRate (driver) slows the state to Idle's own
            // rate when standing still. The state stays "Locomotion" (gameplay, tests and the intro refer to it).
            var btc = bt.children; btc[0].timeScale = idleScale; bt.children = btc;
            loco.speedParameterActive = true; loco.speedParameter = "LocoRate";
            L($"Locomotion: Idle time-scaled x{idleScale:F2} to Walk's cycle, state speed = LocoRate (standing: LocoIdleRate {1f / idleScale:F3})");
            sm.defaultState = loco;
            loco.iKOnFeet = true;                        // humanoid foot IK: planted feet stay where the clip put them
            // idle life: the driver fires IdleVariant after standing still for a while
            var idleVar = sm.AddState("Idle_Variation"); idleVar.motion = Clip(clips, "Idle_Variation"); idleVar.iKOnFeet = true;
            var tiv = T(loco, idleVar, 0.4f); tiv.AddCondition(AnimatorConditionMode.If, 0, "IdleVariant"); tiv.AddCondition(AnimatorConditionMode.Less, 0.05f, "Speed");
            T(idleVar, loco, 0.5f, true, 0.92f);
            T(idleVar, loco, 0.2f).AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
            var crouch = ac.CreateBlendTreeInController("Crouch", out var ct, 0);
            ct.blendType = BlendTreeType.Simple1D; ct.blendParameter = "Speed"; ct.useAutomaticThresholds = false;
            ct.AddChild(Clip(clips, "Crouch"), 0f); ct.AddChild(Clip(clips, "Crouch_Walk"), 0.95f);
            var turn = ac.CreateBlendTreeInController("TurnInPlace", out var tt, 0);
            tt.blendType = BlendTreeType.Simple1D; tt.blendParameter = "TurnSpeed"; tt.useAutomaticThresholds = false;
            tt.AddChild(Clip(clips, "Turn_Right"), -90f); tt.AddChild(Clip(clips, "Idle"), 0f); tt.AddChild(Clip(clips, "Turn_Left"), 90f);
            crouch.iKOnFeet = true;
            T(loco, crouch, 0.25f).AddCondition(AnimatorConditionMode.If, 0, "IsCrouching");
            T(crouch, loco, 0.25f).AddCondition(AnimatorConditionMode.IfNot, 0, "IsCrouching");
            foreach (var sign in new[] { 1, -1 })
            {
                var tin = T(loco, turn, 0.2f); tin.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
                tin.AddCondition(sign > 0 ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less, sign * 45f, "TurnSpeed");
            }
            var tb = T(turn, loco, 0.2f); tb.AddCondition(AnimatorConditionMode.Greater, -30f, "TurnSpeed"); tb.AddCondition(AnimatorConditionMode.Less, 30f, "TurnSpeed");
            T(turn, loco, 0.2f).AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
            // aim / combat locomotion: 2D freeform directional on the local planar velocity (m/s) = the clips' authored
            // speeds and directions, so strafing and backing off do not moonwalk while the body faces the camera
            var strafe = ac.CreateBlendTreeInController("StrafeLocomotion", out var st2, 0);
            st2.blendType = BlendTreeType.FreeformDirectional2D; st2.blendParameter = "VelX"; st2.blendParameterY = "VelZ";
            foreach (var (clipName, x, z) in new (string, float, float)[] {
                ("Idle", 0f, 0f), ("Walk", 0f, 1.35f), ("Run", 0f, 3.8f), ("Walk_Backward", 0f, -1.05f), ("Run_Backward", 0f, -2.4f),
                ("Walk_Left", -1.1f, 0f), ("Strafe_Run_L", -3f, 0f), ("Walk_Right", 1.1f, 0f), ("Strafe_Run_R", 3f, 0f) })
                st2.AddChild(Clip(clips, clipName), new Vector2(x, z));
            strafe.iKOnFeet = true;
            { var sc = st2.children; for (int i = 0; i < sc.Length; i++) if (sc[i].motion == idleClip) sc[i].timeScale = idleScale; st2.children = sc; }
            strafe.speedParameterActive = true; strafe.speedParameter = "LocoRate";
            // every grounded "free" state: jump / fall / actions / attacks start from any of them
            // phase C start / stop / pivot / turn states (only when the clips exist; entered only with UseLocoClips, see the driver)
            var locoExtra = new List<AnimatorState>();
            foreach (var (clipName, ev) in new (string, int)[] { ("Walk_Start", LocoEvents.WalkStart), ("Run_Start", LocoEvents.RunStart), ("Walk_Stop", LocoEvents.WalkStop),
                                                                 ("Run_Stop", LocoEvents.RunStop), ("Run_Pivot_180", LocoEvents.RunPivot180), ("Turn_180", LocoEvents.Turn180) })
            {
                var c = Opt(clips, clipName); if (c == null) continue;
                var s = sm.AddState(clipName); s.motion = c; s.tag = "Loco"; s.iKOnFeet = true;
                s.mirrorParameterActive = true; s.mirrorParameter = "LocoMirror";
                var tin = T(loco, s, 0.1f); tin.AddCondition(AnimatorConditionMode.If, 0, "UseLocoClips"); tin.AddCondition(AnimatorConditionMode.Equals, ev, "LocoEvent");
                T(s, loco, 0.15f, true, 0.85f);
                locoExtra.Add(s);
            }
            var grounded = new[] { loco, strafe, crouch, turn, idleVar }.Concat(locoExtra).ToArray();
            // air: derived from IsGrounded + VerticalVelocity (no jump trigger needed)
            var jump = sm.AddState("Jump"); jump.motion = Clip(clips, "Jump");
            var fall = sm.AddState("Fall"); fall.motion = Clip(clips, "Fall");
            var land = sm.AddState("Land"); land.motion = Clip(clips, "Land");
            foreach (var from in grounded)
            {
                var tj = T(from, jump, 0.1f); tj.AddCondition(AnimatorConditionMode.IfNot, 0, "IsGrounded"); tj.AddCondition(AnimatorConditionMode.Greater, 1.0f, "VerticalVelocity");
                var tf = T(from, fall, 0.25f); tf.AddCondition(AnimatorConditionMode.IfNot, 0, "IsGrounded"); tf.AddCondition(AnimatorConditionMode.Less, -3f, "VerticalVelocity");
            }
            T(jump, fall, 0.2f, true, 0.85f);
            T(fall, land, 0.05f).AddCondition(AnimatorConditionMode.If, 0, "IsGrounded");
            T(jump, land, 0.05f).AddCondition(AnimatorConditionMode.If, 0, "IsGrounded");
            T(land, loco, 0.2f, true, 0.6f);
            // full-body actions: Action = id (gameplay resets one-shots to 0 once entered; loops run until Action changes)
            var actions = new List<(int id, string clip, bool loop)> {
                (PA.Pickup, "Pickup", false), (PA.GatherWood, "Gather_Wood", true), (PA.GatherStone, "Gather_Stone", true), (PA.GatherPlant, "Gather_Plant", true),
                (PA.Interact, "Interact", false), (PA.Craft, "Craft", true), (PA.Eat, "Eat", false), (PA.Drink, "Drink", false), (PA.Build, "Build", true),
                (PA.UseItem, "Use_Item", false), (PA.Sleep, "Sleep", true), (PA.WakeUp, "Wake_Up", false), (PA.GetUp, "Get_Up", false) };
            if (Opt(clips, "Butcher")) actions.Add((PA.Butcher, "Butcher", true));
            // phase 3.5 actions (RES / SURV use the ids now): placeholder clips until the real ones are exported
            var placeholders = new Dictionary<string, string> { { "Collect_Water", "Drink" }, { "Gather_Stone_Hand", "Gather_Plant" }, { "Gather_Branch", "Gather_Plant" }, { "Bandage_Use", "Use_Item" } };
            actions.Add((PA.CollectWater, "Collect_Water", false)); actions.Add((PA.GatherStoneHand, "Gather_Stone_Hand", true));
            actions.Add((PA.GatherBranch, "Gather_Branch", true)); actions.Add((PA.BandageUse, "Bandage_Use", false));
            // phase A timing until enter / exit clips exist: loops (squat / kneel work) blend in over 0.35 s and out over 0.45 s
            // (hips drop ~1.1 m/s instead of ~2); one-shots 0.3 s in, 0.3 s out. The driver keeps movement off for the first 60 %
            // of an exit and brakes the motor before an action starts.
            const float LoopIn = 0.35f, LoopOut = 0.45f, OneShotIn = 0.3f, OneShotOut = 0.3f;
            var gatherEnter = Opt(clips, "Gather_Enter"); var gatherExit = Opt(clips, "Gather_Exit");
            bool gatherClips = gatherEnter && gatherExit;
            var actionStates = new Dictionary<int, AnimatorState>();
            foreach (var (id, clipName, loop) in actions)
            {
                var s = sm.AddState(clipName); s.motion = placeholders.TryGetValue(clipName, out var stand) ? Placeholder(clips, clipName, stand) : Clip(clips, clipName);
                s.tag = "Action"; actionStates[id] = s;
                bool special = id == PA.Sleep || id == PA.WakeUp || id == PA.GetUp;
                if (id == PA.GatherPlant && gatherClips)
                {
                    // phase C: stand -> squat -> loop -> stand, short blends
                    var enter = sm.AddState("Gather_Enter"); enter.motion = gatherEnter; enter.tag = "Action";
                    var exit = sm.AddState("Gather_Exit"); exit.motion = gatherExit; exit.tag = "Action";
                    foreach (var from in grounded) T(from, enter, 0.12f).AddCondition(AnimatorConditionMode.Equals, id, "Action");
                    T(enter, s, 0.1f, true, 0.95f);
                    T(enter, exit, 0.1f).AddCondition(AnimatorConditionMode.NotEqual, id, "Action");
                    T(s, exit, 0.12f).AddCondition(AnimatorConditionMode.NotEqual, id, "Action");
                    T(exit, loco, 0.15f, true, 0.9f);
                    L("Gather_Plant: enter / exit clips wired (0.12 / 0.1 / 0.15 s blends)");
                    continue;
                }
                foreach (var from in grounded)
                    T(from, s, special ? 0.2f : loop ? LoopIn : OneShotIn).AddCondition(AnimatorConditionMode.Equals, id, "Action");
                if (loop && id != PA.Sleep) T(s, loco, LoopOut).AddCondition(AnimatorConditionMode.NotEqual, id, "Action");
                else if (!loop) T(s, loco, special ? 0.25f : OneShotOut, true, 0.94f);
            }
            T(actionStates[PA.Sleep], actionStates[PA.GetUp], 0.4f).AddCondition(AnimatorConditionMode.NotEqual, PA.Sleep, "Action");
            // opening: frozen first frame of Wake_Up (lying on the sand) until the intro sets Action = WakeUp
            var uncon = sm.AddState("Unconscious"); uncon.motion = Clip(clips, "Wake_Up"); uncon.speed = 0f; uncon.tag = "Action";
            T(uncon, actionStates[PA.WakeUp], 0.05f).AddCondition(AnimatorConditionMode.Equals, PA.WakeUp, "Action");
            // attacks: IsAttacking + Action id; clip speed x AttackSpeed (WeaponData.attackSpeed, default 1)
            var attackStates = new Dictionary<string, AnimatorState>();
            // bare hands (phase 3.5): BareHand_Punch_1 / 2 / 3 and BareHand_Heavy play placeholder clips until CHAR's clips are in the FBX
            var attackStand = new Dictionary<string, string> { { "BareHand_Punch_1", "Knife_Attack" }, { "BareHand_Punch_2", "Sword_Attack_1" }, { "BareHand_Punch_3", "Sword_Attack_2" }, { "BareHand_Heavy", "Sword_Heavy" } };
            var comboEnd = Opt(clips, "BareHand_Combo_End");
            foreach (var (id, clipName) in new (int, string)[] { (PA.AttackSpear, "Attack_Spear"), (PA.AttackSpearHeavy, "Attack_Spear_Heavy"), (PA.ThrowSpear, "Throw_Spear"),
                                                                 (PA.SpearAttack2, "Spear_Attack_2"), (PA.KnifeAttack, "Knife_Attack"),
                                                                 (PA.SwordAttack1, "Sword_Attack_1"), (PA.SwordAttack2, "Sword_Attack_2"), (PA.SwordAttack3, "Sword_Attack_3"),
                                                                 (PA.SwordHeavy, "Sword_Heavy"),
                                                                 (PA.BareHandPunch1, "BareHand_Punch_1"), (PA.BareHandPunch2, "BareHand_Punch_2"), (PA.BareHandPunch3, "BareHand_Punch_3"),
                                                                 (PA.BareHandHeavy, "BareHand_Heavy") }
                                                                 .Concat(new (int, string)[] { (PA.Kick, "Kick") }.Where(a => Opt(clips, a.Item2) != null)))
            {
                var s = sm.AddState(clipName); s.motion = attackStand.TryGetValue(clipName, out var stand) ? Placeholder(clips, clipName, stand) : Clip(clips, clipName);
                s.tag = "Attack"; attackStates[clipName] = s;
                s.speedParameterActive = true; s.speedParameter = "AttackSpeed";
                foreach (var from in grounded)
                {
                    var tin = T(from, s, 0.1f); tin.AddCondition(AnimatorConditionMode.If, 0, "IsAttacking"); tin.AddCondition(AnimatorConditionMode.Equals, id, "Action");
                }
                bool viaRecovery = (clipName == "Attack_Spear" || clipName == "Spear_Attack_2") && clips.Any(x => x.name == "Spear_Recovery");
                bool viaComboEnd = clipName == "BareHand_Punch_3" && comboEnd;
                if (!viaRecovery && !viaComboEnd)
                    T(s, loco, 0.2f, true, 0.9f);            // back to Locomotion; the Strafe transition moves on from there
            }
            // phase 3.5: the third punch settles back into the guard through BareHand_Combo_End (tag Attack: the body is busy until it ends)
            if (comboEnd)
            {
                var ce = sm.AddState("BareHand_Combo_End"); ce.motion = comboEnd; ce.tag = "Attack";
                ce.speedParameterActive = true; ce.speedParameter = "AttackSpeed";
                T(attackStates["BareHand_Punch_3"], ce, 0.1f, true, 0.88f);
                T(ce, loco, 0.2f, true, 0.9f);
            }
            // phase C: spear attacks recover through Spear_Recovery when the clip exists
            if (Opt(clips, "Spear_Recovery") is AnimationClip spearRec)
            {
                var rec = sm.AddState("Spear_Recovery"); rec.motion = spearRec; rec.tag = "Attack";
                rec.speedParameterActive = true; rec.speedParameter = "AttackSpeed";
                foreach (var n in new[] { "Attack_Spear", "Spear_Attack_2" }) if (attackStates.TryGetValue(n, out var a)) T(a, rec, 0.1f, true, 0.9f);
                T(rec, loco, 0.2f, true, 0.9f);
            }
            // dodge: from anywhere (also cancels an attack's recovery), short blends
            var dodge = sm.AddState("Dodge"); dodge.motion = Clip(clips, "Dodge"); dodge.tag = "Action";
            var ad0 = sm.AddAnyStateTransition(dodge); ad0.duration = 0.06f; ad0.canTransitionToSelf = false; ad0.AddCondition(AnimatorConditionMode.Equals, PA.Dodge, "Action");
            T(dodge, loco, 0.15f, true, 0.9f);
            // climbing: driven by PlayerClimb (CrossFade), tag Climb; start / pick / end hand over by exit time
            var climbStates = new Dictionary<string, AnimatorState>();
            foreach (var cn in new[] { "Climb_Start", "Climb_Idle", "Climb_Up", "Climb_Down", "Harvest_Fruit", "Climb_End" })
            {
                var s = sm.AddState(cn); s.motion = Clip(clips, cn); s.tag = cn == "Climb_End" ? "ClimbEnd" : "Climb"; climbStates[cn] = s;
            }
            T(climbStates["Climb_Start"], climbStates["Climb_Idle"], 0.2f, true, 0.95f);
            T(climbStates["Harvest_Fruit"], climbStates["Climb_Idle"], 0.25f, true, 0.95f);
            T(climbStates["Climb_End"], loco, 0.2f, true, 0.9f);
            // health: HealthState 1 = light hit, 2 = heavy hit (pulses, reset by gameplay), 3 = dead, back to 0 on respawn
            var hurt = sm.AddState("Hurt"); hurt.motion = Clip(clips, "Hurt"); hurt.tag = "Hurt";
            var hurtH = sm.AddState("Hurt_Heavy"); hurtH.motion = Clip(clips, "Hurt_Heavy"); hurtH.tag = "Hurt";
            var death = sm.AddState("Death"); death.motion = Clip(clips, "Death"); death.tag = "Dead";
            var getup = actionStates[PA.GetUp];
            var ah = sm.AddAnyStateTransition(hurt); ah.duration = 0.05f; ah.canTransitionToSelf = false; ah.AddCondition(AnimatorConditionMode.Equals, 1, "HealthState");
            var ahh = sm.AddAnyStateTransition(hurtH); ahh.duration = 0.05f; ahh.canTransitionToSelf = false; ahh.AddCondition(AnimatorConditionMode.Equals, 2, "HealthState");
            T(hurt, loco, 0.2f, true, 0.85f); T(hurtH, loco, 0.25f, true, 0.9f);
            var ad = sm.AddAnyStateTransition(death); ad.duration = 0.15f; ad.canTransitionToSelf = false; ad.AddCondition(AnimatorConditionMode.Equals, 3, "HealthState");
            T(death, getup, 0.3f).AddCondition(AnimatorConditionMode.Equals, 0, "HealthState");
            // free <-> strafe locomotion on Strafe (aim). Added last so actions / attacks / air keep priority in both lists.
            T(loco, strafe, 0.2f).AddCondition(AnimatorConditionMode.If, 0, "Strafe");
            T(strafe, loco, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0, "Strafe");
            T(strafe, crouch, 0.25f).AddCondition(AnimatorConditionMode.If, 0, "IsCrouching");
            // upper body layer: sword idle / block / equip, bow, carry (avatar mask = spine, arms, head)
            var mask = new AvatarMask();
            foreach (AvatarMaskBodyPart part in Enum.GetValues(typeof(AvatarMaskBodyPart)))
            {
                if (part == AvatarMaskBodyPart.LastBodyPart) continue;
                bool on = part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head || part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm
                          || part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers;
                mask.SetHumanoidBodyPartActive(part, on);
            }
            string maskPath = $"{spec.Folder}/Animations/AM_Player_UpperBody.mask";
            AssetDatabase.DeleteAsset(maskPath); AssetDatabase.CreateAsset(mask, maskPath);
            ac.AddLayer("UpperBody");
            var layers = ac.layers; layers[1].avatarMask = mask; layers[1].defaultWeight = 1f; layers[1].blendingMode = AnimatorLayerBlendingMode.Override; ac.layers = layers;
            var usm = ac.layers[1].stateMachine;
            var empty = usm.AddState("Empty"); usm.defaultState = empty;
            // upper-body actions (Action 30..40) enter from anywhere; each state leaves when its own condition is false
            AnimatorState Upper(int id, string clipName)
            {
                var s = usm.AddState(clipName); s.motion = Clip(clips, clipName);
                var tin = usm.AddAnyStateTransition(s); tin.duration = 0.2f; tin.hasExitTime = false; tin.hasFixedDuration = true; tin.canTransitionToSelf = false;
                tin.AddCondition(AnimatorConditionMode.Equals, id, "Action");
                return s;
            }
            AnimatorStateTransition Out(AnimatorState s, float dur, bool exit = false, float exitTime = 0.9f)
            {
                var o = s.AddTransition(empty); o.duration = dur; o.hasFixedDuration = true; o.hasExitTime = exit; o.exitTime = exitTime; return o;
            }
            // bow chain: Aim / Draw / Release pass between each other (AnyState), leave when Action is outside 30..32
            foreach (var (id, clipName) in new (int, string)[] { (PA.BowAim, "Bow_Aim"), (PA.BowDraw, "Bow_Draw"), (PA.BowRelease, "Bow_Release") })
            {
                var s = Upper(id, clipName);
                Out(s, 0.25f).AddCondition(AnimatorConditionMode.Less, PA.BowAim, "Action");
                Out(s, 0.25f).AddCondition(AnimatorConditionMode.Greater, PA.BowRelease, "Action");
            }
            var carry = Upper(PA.CarryItem, "Carry_Item");
            Out(carry, 0.25f).AddCondition(AnimatorConditionMode.NotEqual, PA.CarryItem, "Action");
            // sword block: loops while Action == SwordBlock
            var block = Upper(PA.SwordBlock, "Sword_Block");
            Out(block, 0.2f).AddCondition(AnimatorConditionMode.NotEqual, PA.SwordBlock, "Action");
            // sword equip / unequip: one-shots out at exit time 0.9; held on the last frame while gameplay still asks for
            // them (no replay through AnyState); an attack takes the arms at once
            foreach (var (id, clipName) in new (int, string)[] { (PA.SwordEquip, "Sword_Equip"), (PA.SwordUnequip, "Sword_Unequip") })
            {
                var s = Upper(id, clipName);
                Out(s, 0.2f, true, 0.9f).AddCondition(AnimatorConditionMode.NotEqual, id, "Action");
                Out(s, 0.1f).AddCondition(AnimatorConditionMode.If, 0, "IsAttacking");
            }
            // sword idle: sword in hand (WeaponType 4) in combat mode, no upper-body action, and the base layer is free
            // (attacks, dodge, hurt, actions and climbing keep their own arms)
            var swordIdle = usm.AddState("Sword_Idle"); swordIdle.motion = Clip(clips, "Sword_Idle");
            foreach (var (mode, v) in new (AnimatorConditionMode, int)[] { (AnimatorConditionMode.Less, PA.BowAim), (AnimatorConditionMode.Greater, PA.CarryItem) })
            {
                var tin = empty.AddTransition(swordIdle); tin.duration = 0.25f; tin.hasExitTime = false; tin.hasFixedDuration = true;
                tin.AddCondition(AnimatorConditionMode.Equals, (int)Items.WeaponKind.Sword, "WeaponType");
                tin.AddCondition(AnimatorConditionMode.If, 0, "CombatMode");
                tin.AddCondition(AnimatorConditionMode.IfNot, 0, "IsAttacking");
                tin.AddCondition(AnimatorConditionMode.IfNot, 0, "FullBodyBusy");
                tin.AddCondition(mode, v, "Action");
            }
            Out(swordIdle, 0.25f).AddCondition(AnimatorConditionMode.NotEqual, (int)Items.WeaponKind.Sword, "WeaponType");
            Out(swordIdle, 0.25f).AddCondition(AnimatorConditionMode.IfNot, 0, "CombatMode");
            Out(swordIdle, 0.1f).AddCondition(AnimatorConditionMode.If, 0, "IsAttacking");
            Out(swordIdle, 0.15f).AddCondition(AnimatorConditionMode.If, 0, "FullBodyBusy");
            var su = Out(swordIdle, 0.2f); su.AddCondition(AnimatorConditionMode.Greater, PA.BowAim - 1, "Action"); su.AddCondition(AnimatorConditionMode.Less, PA.CarryItem + 1, "Action");
            // phase C upper-body states (only when the clips exist): unarmed block (hold), bow equip / nock (one-shots), full draw
            // (hold), and combat idles for bare hands / bow / spear built like Sword_Idle
            if (Opt(clips, "Unarmed_Block")) { var ub = Upper(PA.UnarmedBlock, "Unarmed_Block"); Out(ub, 0.2f).AddCondition(AnimatorConditionMode.NotEqual, PA.UnarmedBlock, "Action"); }
            if (Opt(clips, "Bow_FullDraw")) { var fd = Upper(PA.BowFullDraw, "Bow_FullDraw"); Out(fd, 0.2f).AddCondition(AnimatorConditionMode.NotEqual, PA.BowFullDraw, "Action"); }
            foreach (var (id, clipName) in new (int, string)[] { (PA.BowEquip, "Bow_Equip"), (PA.BowNock, "Bow_Nock") })
            {
                if (!Opt(clips, clipName)) continue;
                var s = Upper(id, clipName);
                Out(s, 0.2f, true, 0.9f).AddCondition(AnimatorConditionMode.NotEqual, id, "Action");
                Out(s, 0.1f).AddCondition(AnimatorConditionMode.If, 0, "IsAttacking");
            }
            foreach (var (clipName, kind) in new (string, Items.WeaponKind)[] { ("BareHand_Idle", Items.WeaponKind.None), ("Bow_Idle", Items.WeaponKind.Bow), ("Spear_Idle", Items.WeaponKind.Spear) })
            {
                var c = Opt(clips, clipName); if (c == null) continue;               // bare hands: guard pose after a punch (CombatMode, WeaponType 0)
                var idleS = usm.AddState(clipName); idleS.motion = c;
                foreach (var (mode, v) in new (AnimatorConditionMode, int)[] { (AnimatorConditionMode.Less, PA.BowAim), (AnimatorConditionMode.Greater, PA.CarryItem) })
                {
                    var tin = empty.AddTransition(idleS); tin.duration = 0.25f; tin.hasExitTime = false; tin.hasFixedDuration = true;
                    tin.AddCondition(AnimatorConditionMode.Equals, (int)kind, "WeaponType");
                    tin.AddCondition(AnimatorConditionMode.If, 0, "CombatMode");
                    tin.AddCondition(AnimatorConditionMode.IfNot, 0, "IsAttacking");
                    tin.AddCondition(AnimatorConditionMode.IfNot, 0, "FullBodyBusy");
                    tin.AddCondition(mode, v, "Action");
                }
                Out(idleS, 0.25f).AddCondition(AnimatorConditionMode.NotEqual, (int)kind, "WeaponType");
                Out(idleS, 0.25f).AddCondition(AnimatorConditionMode.IfNot, 0, "CombatMode");
                Out(idleS, 0.1f).AddCondition(AnimatorConditionMode.If, 0, "IsAttacking");
                Out(idleS, 0.15f).AddCondition(AnimatorConditionMode.If, 0, "FullBodyBusy");
                var ou = Out(idleS, 0.2f); ou.AddCondition(AnimatorConditionMode.Greater, PA.BowAim - 1, "Action"); ou.AddCondition(AnimatorConditionMode.Less, PA.CarryItem + 1, "Action");
            }
            // hit reaction layer: a light hit adds the Hurt clip on top of whatever the body does (additive, upper-body mask),
            // so the base layer keeps its locomotion and the motor keeps moving. HurtLight (trigger) restarts it on every hit;
            // back to Empty at exit time. Heavy hits keep the full-body Hurt_Heavy state (HealthState 2); the base Hurt state
            // (HealthState 1) stays for code that still pulses it.
            ac.AddLayer("HitReaction");
            var hl = ac.layers; int hri = hl.Length - 1;
            hl[hri].avatarMask = mask; hl[hri].defaultWeight = 1f; hl[hri].blendingMode = AnimatorLayerBlendingMode.Additive; ac.layers = hl;
            var hsm = ac.layers[hri].stateMachine;
            var hrEmpty = hsm.AddState("Empty"); hsm.defaultState = hrEmpty;
            var hurtAdd = hsm.AddState("Hurt_Additive"); hurtAdd.motion = Clip(clips, "Hurt");
            var hrIn = hsm.AddAnyStateTransition(hurtAdd); hrIn.duration = 0.05f; hrIn.hasExitTime = false; hrIn.hasFixedDuration = true; hrIn.canTransitionToSelf = true;
            hrIn.AddCondition(AnimatorConditionMode.If, 0, "HurtLight");
            var hrOut = hurtAdd.AddTransition(hrEmpty); hrOut.hasExitTime = true; hrOut.exitTime = 0.85f; hrOut.duration = 0.15f; hrOut.hasFixedDuration = true;
            // phase 3.5: empty hands (WeaponType 0) flinch with BareHand_HitReaction when the clip exists; weapons keep Hurt_Additive
            if (Opt(clips, "BareHand_HitReaction") is AnimationClip bhr)
            {
                var bh = hsm.AddState("BareHand_HitReaction"); bh.motion = bhr;
                var bIn = hsm.AddAnyStateTransition(bh); bIn.duration = 0.05f; bIn.hasExitTime = false; bIn.hasFixedDuration = true; bIn.canTransitionToSelf = true;
                bIn.AddCondition(AnimatorConditionMode.If, 0, "HurtLight"); bIn.AddCondition(AnimatorConditionMode.Equals, 0, "WeaponType");
                hrIn.AddCondition(AnimatorConditionMode.NotEqual, 0, "WeaponType");
                var bOut = bh.AddTransition(hrEmpty); bOut.hasExitTime = true; bOut.exitTime = 0.85f; bOut.duration = 0.15f; bOut.hasFixedDuration = true;
            }
            // IK pass on the base layer: PlayerIK places the feet on the ground, turns the head, leans into turns
            var ls = ac.layers; ls[0].iKPass = true; ac.layers = ls;
            EditorUtility.SetDirty(ac);
            L($"Controller: {path} (states: {sm.states.Length} base + {usm.states.Length} upper body + {hsm.states.Length} hit reaction, params {ac.parameters.Length}, IK pass on)");
            L("Controller: new states StrafeLocomotion (2D VelX/VelZ, on Strafe), Sword_Attack_1/2/3 + Sword_Heavy (Attack, speed x AttackSpeed); " +
              "upper body Sword_Idle, Sword_Block, Sword_Equip, Sword_Unequip; HitReaction (additive) Hurt_Additive on HurtLight");
            return ac;
        }
    }
}
