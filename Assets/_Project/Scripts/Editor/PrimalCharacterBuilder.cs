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
                var anim = inst.GetComponent<Animator>() ?? inst.AddComponent<Animator>();
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

        [MenuItem("Primal Frontier/Characters/Build + Test Player")]
        public static void MenuPlayer() => BuildAndTest("Player");

        public static bool BuildAndTest(string id)
        {
            Log.Clear(); _fails = 0;
            var spec = Specs[id];
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
            return JsonUtility.FromJson<AnimMeta>(File.ReadAllText(file));
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
                // world-space T-pose fix for the upper arms
                var inst = UnityEngine.Object.Instantiate(model);
                try
                {
                    var t2 = inst.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name, t => t);
                    foreach (var side in new[] { "L", "R" })
                    {
                        var ua = t2["UpperArm_" + side]; var la = t2["LowerArm_" + side];
                        var dir = (la.position - ua.position).normalized;
                        var target = new Vector3(Mathf.Sign(dir.x), 0f, 0f);
                        ua.rotation = Quaternion.FromToRotation(dir, target) * ua.rotation;
                        var ha = t2["Hand_" + side];
                        var dir2 = (ha.position - la.position).normalized;
                        la.rotation = Quaternion.FromToRotation(dir2, target) * la.rotation;
                    }
                    foreach (var t in inst.GetComponentsInChildren<Transform>(true))
                        skel.Add(new SkeletonBone { name = t == inst.transform ? model.name : t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale });
                }
                finally { UnityEngine.Object.DestroyImmediate(inst); }
                var hd = mi.humanDescription;
                hd.human = human.ToArray(); hd.skeleton = skel.ToArray();
                hd.upperArmTwist = 0.5f; hd.lowerArmTwist = 0.5f; hd.upperLegTwist = 0.5f; hd.lowerLegTwist = 0.5f;
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
                if (cm != null && cm.events != null && cm.frames > 0)
                    c.events = cm.events.Select(e => new AnimationEvent { functionName = e.function, stringParameter = e.param ?? "", time = Mathf.Clamp01(e.frame / (float)cm.frames) }).ToArray();
                else c.events = new AnimationEvent[0];
                if (cm == null) F("clip without meta: " + name);
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

        static void FixTexture(string path, bool normal = false, bool linear = false, bool alpha = false)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) return;
            bool dirty = false;
            if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; dirty = true; }
            if (linear && ti.sRGBTexture) { ti.sRGBTexture = false; dirty = true; }
            if (alpha && !ti.alphaIsTransparency) { ti.alphaIsTransparency = true; ti.mipMapsPreserveCoverage = true; ti.alphaTestReferenceValue = 0.45f; dirty = true; }
            if (ti.maxTextureSize > 2048) { ti.maxTextureSize = 2048; dirty = true; }
            if (dirty) ti.SaveAndReimport();
        }

        static void RemapMaterials(string fbx, Dictionary<string, Material> mats)
        {
            var mi = AssetImporter.GetAtPath(fbx) as ModelImporter;
            foreach (var kv in mats) mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
            mi.SaveAndReimport();
        }

        // ------------------------------------------------------------------ controller (player)
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
            string path = $"{spec.Folder}/Animations/AC_Player.controller";
            var ac = NewController(path);
            foreach (var (n, t) in new (string, AnimatorControllerParameterType)[] {
                ("Speed", AnimatorControllerParameterType.Float), ("VelX", AnimatorControllerParameterType.Float), ("VelZ", AnimatorControllerParameterType.Float),
                ("Turn", AnimatorControllerParameterType.Float), ("VerticalVelocity", AnimatorControllerParameterType.Float),
                ("IsGrounded", AnimatorControllerParameterType.Bool), ("IsCrouching", AnimatorControllerParameterType.Bool), ("IsAttacking", AnimatorControllerParameterType.Bool),
                ("Dead", AnimatorControllerParameterType.Bool), ("Jump", AnimatorControllerParameterType.Trigger), ("Action", AnimatorControllerParameterType.Trigger),
                ("Attack", AnimatorControllerParameterType.Trigger), ("Hurt", AnimatorControllerParameterType.Trigger), ("Revive", AnimatorControllerParameterType.Trigger),
                ("ActionType", AnimatorControllerParameterType.Int), ("AttackType", AnimatorControllerParameterType.Int), ("HurtType", AnimatorControllerParameterType.Int),
                ("UpperBody", AnimatorControllerParameterType.Int) })
                ac.AddParameter(n, t);
            var p = ac.parameters; foreach (var x in p) if (x.name == "IsGrounded") x.defaultBool = true; ac.parameters = p;
            var sm = ac.layers[0].stateMachine;
            // locomotion: 2D freeform cartesian on local velocity (m/s)
            var loco = ac.CreateBlendTreeInController("Locomotion", out var bt, 0);
            bt.blendType = BlendTreeType.FreeformCartesian2D; bt.blendParameter = "VelX"; bt.blendParameterY = "VelZ";
            bt.AddChild(Clip(clips, "IDLE"), new Vector2(0, 0));
            bt.AddChild(Clip(clips, "WALK_FORWARD"), new Vector2(0, 1.35f));
            bt.AddChild(Clip(clips, "RUN_FORWARD"), new Vector2(0, 3.8f));
            bt.AddChild(Clip(clips, "SPRINT"), new Vector2(0, 6.2f));
            bt.AddChild(Clip(clips, "WALK_BACKWARD"), new Vector2(0, -1.05f));
            bt.AddChild(Clip(clips, "WALK_LEFT"), new Vector2(-1.1f, 0));
            bt.AddChild(Clip(clips, "WALK_RIGHT"), new Vector2(1.1f, 0));
            sm.defaultState = loco;
            var crouch = ac.CreateBlendTreeInController("Crouch", out var ct, 0);
            ct.blendType = BlendTreeType.Simple1D; ct.blendParameter = "Speed"; ct.useAutomaticThresholds = false;
            ct.AddChild(Clip(clips, "CROUCH"), 0f); ct.AddChild(Clip(clips, "CROUCH_WALK"), 0.95f);
            var turn = ac.CreateBlendTreeInController("TurnInPlace", out var tt, 0);
            tt.blendType = BlendTreeType.Simple1D; tt.blendParameter = "Turn"; tt.useAutomaticThresholds = false;
            tt.AddChild(Clip(clips, "TURN_RIGHT"), -1f); tt.AddChild(Clip(clips, "IDLE"), 0f); tt.AddChild(Clip(clips, "TURN_LEFT"), 1f);
            T(loco, crouch, 0.25f).AddCondition(AnimatorConditionMode.If, 0, "IsCrouching");
            T(crouch, loco, 0.25f).AddCondition(AnimatorConditionMode.IfNot, 0, "IsCrouching");
            var toTurn = T(loco, turn, 0.2f); toTurn.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed"); toTurn.AddCondition(AnimatorConditionMode.Greater, 0.5f, "Turn");
            var toTurn2 = T(loco, turn, 0.2f); toTurn2.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed"); toTurn2.AddCondition(AnimatorConditionMode.Less, -0.5f, "Turn");
            var back = T(turn, loco, 0.2f); back.AddCondition(AnimatorConditionMode.Greater, -0.5f, "Turn"); back.AddCondition(AnimatorConditionMode.Less, 0.5f, "Turn");
            T(turn, loco, 0.2f).AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
            // air
            var jump = sm.AddState("Jump"); jump.motion = Clip(clips, "JUMP");
            var fall = sm.AddState("Fall"); fall.motion = Clip(clips, "FALL");
            var land = sm.AddState("Land"); land.motion = Clip(clips, "LAND");
            T(loco, jump, 0.1f).AddCondition(AnimatorConditionMode.If, 0, "Jump");
            T(crouch, jump, 0.1f).AddCondition(AnimatorConditionMode.If, 0, "Jump");
            T(jump, fall, 0.2f, true, 0.85f);
            var lf = T(loco, fall, 0.25f); lf.AddCondition(AnimatorConditionMode.IfNot, 0, "IsGrounded"); lf.AddCondition(AnimatorConditionMode.Less, -2f, "VerticalVelocity");
            T(fall, land, 0.05f).AddCondition(AnimatorConditionMode.If, 0, "IsGrounded");
            T(jump, land, 0.05f).AddCondition(AnimatorConditionMode.If, 0, "IsGrounded");
            T(land, loco, 0.2f, true, 0.6f);
            // actions (full body)
            var actions = new (int id, string clip, bool loop)[] { (1, "PICKUP", false), (2, "GATHER_WOOD", true), (3, "GATHER_STONE", true), (4, "INTERACT", false),
                (5, "CRAFT", true), (6, "DRINK", false), (7, "EAT", false), (8, "BUILD", true), (9, "SLEEP", true), (10, "THROW_SPEAR", false) };
            foreach (var (id, clipName, loop) in actions)
            {
                var s = sm.AddState(clipName); s.motion = Clip(clips, clipName);
                var tin = T(loco, s, 0.2f); tin.AddCondition(AnimatorConditionMode.If, 0, "Action"); tin.AddCondition(AnimatorConditionMode.Equals, id, "ActionType");
                if (loop) T(s, loco, 0.3f).AddCondition(AnimatorConditionMode.NotEqual, id, "ActionType");
                else T(s, loco, 0.25f, true, 0.92f);
            }
            // attacks
            var attacks = new (int id, string clip)[] { (0, "ATTACK_SPEAR"), (1, "ATTACK_SPEAR_ALT"), (2, "THROW_SPEAR") };
            foreach (var (id, clipName) in attacks)
            {
                var s = sm.AddState("Attack_" + clipName); s.motion = Clip(clips, clipName);
                var tin = T(loco, s, 0.1f); tin.AddCondition(AnimatorConditionMode.If, 0, "Attack"); tin.AddCondition(AnimatorConditionMode.Equals, id, "AttackType");
                T(s, loco, 0.2f, true, 0.9f);
            }
            // hurt / death / revive
            var hurt = sm.AddState("Hurt"); hurt.motion = Clip(clips, "HURT");
            var hurtH = sm.AddState("HurtHeavy"); hurtH.motion = Clip(clips, "HURT_HEAVY");
            var death = sm.AddState("Death"); death.motion = Clip(clips, "DEATH");
            var revive = sm.AddState("Revive"); revive.motion = Clip(clips, "REVIVE");
            var ah = sm.AddAnyStateTransition(hurt); ah.duration = 0.05f; ah.AddCondition(AnimatorConditionMode.If, 0, "Hurt"); ah.AddCondition(AnimatorConditionMode.Equals, 0, "HurtType"); ah.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
            var ahh = sm.AddAnyStateTransition(hurtH); ahh.duration = 0.05f; ahh.AddCondition(AnimatorConditionMode.If, 0, "Hurt"); ahh.AddCondition(AnimatorConditionMode.Equals, 1, "HurtType"); ahh.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
            T(hurt, loco, 0.2f, true, 0.85f); T(hurtH, loco, 0.25f, true, 0.9f);
            var ad = sm.AddAnyStateTransition(death); ad.duration = 0.15f; ad.canTransitionToSelf = false; ad.AddCondition(AnimatorConditionMode.If, 0, "Dead");
            var dr = T(death, revive, 0.2f); dr.AddCondition(AnimatorConditionMode.If, 0, "Revive"); dr.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
            T(revive, loco, 0.25f, true, 0.95f);
            // upper body layer: carry + bow (avatar mask = spine, arms, head)
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
            var ub = new (int id, string clip)[] { (1, "CARRY_ITEM"), (2, "BOW_IDLE"), (3, "BOW_DRAW"), (4, "BOW_RELEASE") };
            var ubStates = new Dictionary<int, AnimatorState>();
            foreach (var (id, clipName) in ub)
            {
                var s = usm.AddState(clipName); s.motion = Clip(clips, clipName); ubStates[id] = s;
                var tin = usm.AddAnyStateTransition(s); tin.duration = 0.2f; tin.canTransitionToSelf = false; tin.AddCondition(AnimatorConditionMode.Equals, id, "UpperBody");
            }
            var toEmpty = usm.AddAnyStateTransition(empty); toEmpty.duration = 0.25f; toEmpty.canTransitionToSelf = false; toEmpty.AddCondition(AnimatorConditionMode.Equals, 0, "UpperBody");
            // BOW_DRAW holds its last frame (non-loop); after BOW_RELEASE gameplay sets UpperBody back to 2 (bow idle).
            EditorUtility.SetDirty(ac);
            L($"Controller: {path} (states: {sm.states.Length} base + {usm.states.Length} upper body)");
            return ac;
        }
    }
}
