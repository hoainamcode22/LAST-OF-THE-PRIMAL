using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PrimalFrontier.Animation;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.World;
using Object = UnityEngine.Object;

namespace PrimalFrontier.Tests
{
    /// <summary>
    /// DIAGNOSTIC (animation audit, 2026-09-28): records how the player character is animated, frame by frame, in the
    /// character's own space: shoulder / elbow / hand, upper-arm abduction, elbow bend, bone rolls (upper arm, lower arm,
    /// hand, forearm twist bone), fist roll about the forearm, hips height, shoulder-vs-hip yaw, root yaw / yaw rate,
    /// Animator state / transition / parameters, PlayerIK internal weights, frame time.
    /// A: raw clips (PlayableGraph, no IK) + the controller alone (no motor / IK / twist driver) incl. gather in / out.
    /// B / C / D: the island, simulated input: walk, run, 90 and 180 deg turns, stop, gather (approach + cancel by moving
    /// away; from standing + cancel with E). B = everything on, C = PlayerIK suspended, D = TwistBoneDriver off.
    /// CSVs: Documentation/Tests/motion_probe_*.csv. Skipped unless Library/PrimalBridge/probe_on.txt says "on".
    /// The Animator is set to AlwaysAnimate while probing (a Game view that does not paint must not cull the bones).
    /// </summary>
    public class CharacterMotionProbe
    {
        const string ModelPath = "Assets/Art/Characters/Player/Model/PLAYER_Survivor.fbx";
        const string ControllerPath = "Assets/Art/Characters/Player/Animations/PlayerAnimator.controller";
        static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        static bool On
        {
            get
            {
                try { var f = Path.Combine(ProjectRoot, "Library/PrimalBridge/probe_on.txt"); return File.Exists(f) && File.ReadAllText(f).Trim().StartsWith("on", StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            }
        }
        static string Out(string file) { var d = Path.Combine(ProjectRoot, "Documentation/Tests"); Directory.CreateDirectory(d); return Path.Combine(d, file); }
        static readonly CultureInfo IC = CultureInfo.InvariantCulture;

        GameManager _gm;

        [TearDown] public void Cleanup()
        {
            GameManager.ForceShowTitle = null; GameManager.ForcePlayIntro = null; PlayerInputReader.Simulate = false; Time.timeScale = 1f;
            PlayerInputReader.Sim.Move = Vector2.zero; PlayerInputReader.Sim.Walk = false;
        }
        [UnityTearDown] public IEnumerator Unload() { yield return TestScenes.ClearIfGameplayLeft(); }

        // =====================================================================================================  A: clips
        [UnityTest, Timeout(120000)]
        public IEnumerator A_Clips_And_Controller()
        {
            if (!On) Assert.Ignore("diagnostic probe: off (Library/PrimalBridge/probe_on.txt)");
            LogAssert.ignoreFailingMessages = true;          // clip events (OnFootstep...) have no receiver on the bare model
#if UNITY_EDITOR
            yield return TestScenes.ClearIfGameplayLeft();
            var model = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Assert.IsNotNull(model, "player model");
            var clips = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__")).ToDictionary(c => c.name, c => c);
            var ctrl = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            Assert.IsNotNull(ctrl, "controller");
            // ---- 1. raw clips (retargeted humanoid pose, no foot IK, no playable IK, no controller)
            var goA = Object.Instantiate(model, Vector3.zero, Quaternion.identity); goA.name = "Probe_ClipSampler";
            var aA = goA.GetComponent<Animator>(); Assert.IsNotNull(aA, "animator on the model");
            aA.runtimeAnimatorController = null; aA.cullingMode = AnimatorCullingMode.AlwaysAnimate; aA.applyRootMotion = false;
            var rest = RestPose.FromAnimator(aA);             // skin bind pose = the reference the mesh deforms from
            var rigA = new ProbeRig(aA, goA.transform, rest);
            var sb = new StringBuilder();
            sb.AppendLine("source,clip,idx,t,norm," + ProbeRig.PoseHeader);
            var mus = new StringBuilder();
            mus.AppendLine("clip,muscle,min,max,framesAtLimit(|m|>=0.995),frames");
            string[] names = { "Run", "Walk", "Sprint", "Idle", "Gather_Plant", "Gather_Wood", "Gather_Stone", "Turn_Left", "Turn_Right", "Crouch", "Crouch_Walk", "Pickup", "Idle_Variation" };
            var hph = new HumanPoseHandler(aA.avatar, goA.transform); var hp = new HumanPose();
            foreach (var n in names)
            {
                if (!clips.TryGetValue(n, out var clip)) { mus.AppendLine(n + ",MISSING"); continue; }
                int frames = Mathf.RoundToInt(clip.length * 30f);
                var mn = Enumerable.Repeat(float.MaxValue, HumanTrait.MuscleCount).ToArray(); var mx = Enumerable.Repeat(float.MinValue, HumanTrait.MuscleCount).ToArray();
                var lim = new int[HumanTrait.MuscleCount];
                for (int f = 0; f <= frames; f++)
                {
                    float t = f / 30f;
                    Sample(aA, clip, t);
                    sb.Append("clip,").Append(n).Append(',').Append(f).Append(',').Append(F(t)).Append(',').Append(F(t / clip.length)).Append(',');
                    rigA.Pose(sb); sb.AppendLine();
                    hph.GetHumanPose(ref hp);
                    for (int m = 0; m < hp.muscles.Length; m++)
                    {
                        float v = hp.muscles[m]; mn[m] = Mathf.Min(mn[m], v); mx[m] = Mathf.Max(mx[m], v); if (Mathf.Abs(v) >= 0.995f) lim[m]++;
                    }
                }
                for (int m = 0; m < HumanTrait.MuscleCount; m++)
                {
                    string mname = HumanTrait.MuscleName[m];
                    if (mname.Contains("Finger") || mname.Contains("Thumb") || mname.Contains("Index") || mname.Contains("Middle") || mname.Contains("Ring") || mname.Contains("Little") || mname.Contains("Eye") || mname.Contains("Jaw")) continue;
                    mus.AppendLine($"{n},{mname},{F(mn[m])},{F(mx[m])},{lim[m]},{frames + 1}");
                }
            }
            // twist bones as the clip itself animates them (generic curves baked from the Blender constraint), for the TwistBoneDriver comparison
            File.WriteAllText(Out("motion_probe_muscles.csv"), mus.ToString());

            // ---- 2. the controller alone (no motor, no PlayerIK, no TwistBoneDriver): run with / without the upper layers,
            //         gather in / out (from standing; out into a run ramp like the motor's, and out standing)
            var goB = Object.Instantiate(model, new Vector3(6f, 0f, 0f), Quaternion.identity); goB.name = "Probe_Controller";
            var aB = goB.GetComponent<Animator>(); aB.runtimeAnimatorController = ctrl; aB.cullingMode = AnimatorCullingMode.AlwaysAnimate; aB.applyRootMotion = false;
            aB.SetBool("IsGrounded", true);
            var rec = goB.AddComponent<MotionProbeRecorder>(); rec.Init(new ProbeRig(aB, goB.transform, rest), null, null, null, null);
            float speedCmd = 0f;
            IEnumerator Drive(string phase, float seconds, float speedTarget, int action)
            {
                rec.phase = phase; aB.SetInteger("Action", action); float tt = 0f;
                while (tt < seconds)
                {
                    float dt = Time.deltaTime;
                    speedCmd = Mathf.MoveTowards(speedCmd, speedTarget, (speedTarget > speedCmd ? 10f : 14f) * dt);   // motor acceleration / deceleration
                    aB.SetFloat("Speed", speedCmd, 0.08f, dt);                                                              // driver damping
                    yield return null; tt += dt;
                }
            }
            yield return Drive("warm_run", 1.0f, 3.8f, 0);
            rec.recording = true; rec.t0 = Time.time;
            yield return Drive("ctrl_run", 1.4f, 3.8f, 0);
            aB.SetLayerWeight(1, 0f); if (aB.layerCount > 2) aB.SetLayerWeight(2, 0f);
            yield return Drive("ctrl_run_nolayers", 1.4f, 3.8f, 0);
            aB.SetLayerWeight(1, 1f); if (aB.layerCount > 2) aB.SetLayerWeight(2, 1f);
            yield return Drive("ctrl_walk", 1.6f, 1.35f, 0);
            yield return Drive("ctrl_halfwalk", 2.0f, 0.68f, 0);
            yield return Drive("ctrl_idle", 1.5f, 0f, 0);
            yield return Drive("ctrl_gather_in", 1.6f, 0f, PlayerActions.GatherPlant);
            yield return Drive("ctrl_gather_out_run", 1.4f, 3.8f, 0);
            yield return Drive("ctrl_idle2", 1.5f, 0f, 0);
            yield return Drive("ctrl_gatherwood_in", 1.6f, 0f, PlayerActions.GatherWood);
            yield return Drive("ctrl_gather_out_stand", 1.4f, 0f, 0);
            rec.recording = false;
            // raw Run clip at the controller's normalized times (same rows), to isolate what the controller / layers add
            var ctrlRows = rec.rows.Where(r => r.phase == "ctrl_run" || r.phase == "ctrl_run_nolayers").ToList();
            var sbC = new StringBuilder();
            sbC.AppendLine("source,phase,idx,t,norm," + ProbeRig.PoseHeader);
            if (clips.TryGetValue("Run", out var run))
            {
                foreach (var r in ctrlRows)
                {
                    if (r.state != "Locomotion" || r.inTrans) continue;
                    float nt = r.norm - Mathf.Floor(r.norm);
                    Sample(aA, run, nt * run.length);
                    sbC.Append("clip_match,").Append(r.phase).Append(',').Append(r.idx).Append(',').Append(F(r.t)).Append(',').Append(F(nt)).Append(',');
                    rigA.Pose(sbC); sbC.AppendLine();
                }
            }
            File.WriteAllText(Out("motion_probe_clips.csv"), sb.ToString() + sbC.ToString().Substring(sbC.ToString().IndexOf('\n') + 1));
            File.WriteAllText(Out("motion_probe_controller.csv"), rec.Header() + rec.sb);
            hph.Dispose();
            Object.Destroy(goA); Object.Destroy(goB);
            Debug.Log($"[MotionProbe] A: {names.Length} clips sampled, controller rows {rec.rows.Count}");
#else
            yield break;
#endif
        }

        // =====================================================================================================  E: avatar / retarget
        static readonly string[] ArmMuscles = { "Left Shoulder Down-Up", "Left Shoulder Front-Back", "Left Arm Down-Up", "Left Arm Front-Back", "Left Arm Twist In-Out", "Left Forearm Stretch",
            "Left Forearm Twist In-Out", "Left Hand Down-Up", "Left Hand In-Out", "Right Shoulder Down-Up", "Right Shoulder Front-Back", "Right Arm Down-Up", "Right Arm Front-Back",
            "Right Arm Twist In-Out", "Right Forearm Stretch", "Right Forearm Twist In-Out", "Right Hand Down-Up", "Right Hand In-Out" };

        /// <summary>
        /// what the humanoid avatar itself does, without controller / IK / scripts: skin bind pose vs the model's node rest,
        /// humanoid round trip of the bind pose (roll moved between bones), muscle-zero pose symmetry, the avatar T-pose,
        /// sub-frame sampling (keys at 30 fps, played between keys), blends in muscle space (walk/run, gather in / out).
        /// Writes motion_probe_avatar.txt / .csv.
        /// </summary>
        [UnityTest, Timeout(120000)]
        public IEnumerator E_Avatar_Retarget()
        {
            if (!On) Assert.Ignore("diagnostic probe: off (Library/PrimalBridge/probe_on.txt)");
            LogAssert.ignoreFailingMessages = true;
#if UNITY_EDITOR
            yield return TestScenes.ClearIfGameplayLeft();
            var model = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var clips = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__")).ToDictionary(c => c.name, c => c);
            var go = Object.Instantiate(model, Vector3.zero, Quaternion.identity); go.name = "Probe_Avatar";
            var a = go.GetComponent<Animator>(); a.runtimeAnimatorController = null; a.cullingMode = AnimatorCullingMode.AlwaysAnimate; a.applyRootMotion = false;
            var bindOnly = RestPose.FromAnimator(a, false);
            var bind = RestPose.FromAnimator(a);
            var node = RestPose.FromAsset(model);
            var rig = new ProbeRig(a, go.transform, bind);
            var all = go.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            var txt = new StringBuilder();
            var rows = new StringBuilder();
            rows.AppendLine("source,clip,idx,t,w," + ProbeRig.PoseHeader + "," + string.Join(",", ArmMuscles.Select(m => "m_" + m.Replace(" ", "").Replace("-", ""))));
            var hph = new HumanPoseHandler(a.avatar, go.transform); var hp = new HumanPose();
            var mIdx = ArmMuscles.Select(m => Array.IndexOf(HumanTrait.MuscleName, m)).ToArray();
            void Row(string src, string clip, int idx, float t, float w)
            {
                rows.Append(src).Append(',').Append(clip).Append(',').Append(idx).Append(',').Append(F(t)).Append(',').Append(F(w)).Append(',');
                rig.Pose(rows);
                hph.GetHumanPose(ref hp);
                foreach (var m in mIdx) rows.Append(',').Append(m >= 0 ? F(hp.muscles[m]) : "");
                rows.AppendLine();
            }
            string P(string n) => all.TryGetValue(n, out var t) && t.parent ? t.parent.name : "-";
            txt.AppendLine("Hierarchy: Hand_L parent " + P("Hand_L") + ", LowerArmTwist_L parent " + P("LowerArmTwist_L") + ", Hand_R parent " + P("Hand_R") + ", LowerArmTwist_R parent " + P("LowerArmTwist_R") +
                           ", LowerArm_L parent " + P("LowerArm_L") + ", UpperArm_L parent " + P("UpperArm_L") + ", Clavicle_L parent " + P("Clavicle_L"));
            txt.AppendLine($"Bones with a skin bind pose: {bindOnly.bones.Count}; missing among arm bones: " +
                           string.Join(" ", new[] { "Clavicle_L", "UpperArm_L", "LowerArm_L", "LowerArmTwist_L", "Hand_L", "Clavicle_R", "UpperArm_R", "LowerArm_R", "LowerArmTwist_R", "Hand_R" }.Where(n => !bindOnly.bones.ContainsKey(n))));

            // ---- 1. model node rest (what the avatar / T-pose were built from) vs skin bind pose (what the mesh deforms from)
            (string bone, string child)[] chain = { ("Pelvis", "Spine"), ("Spine", "Spine_Upper"), ("Spine_Upper", "Chest"), ("Chest", "Neck"), ("Clavicle_L", "UpperArm_L"), ("UpperArm_L", "LowerArm_L"), ("LowerArm_L", "Hand_L"),
                ("LowerArmTwist_L", "Hand_L"), ("Hand_L", "Middle_01_L"), ("Clavicle_R", "UpperArm_R"), ("UpperArm_R", "LowerArm_R"), ("LowerArm_R", "Hand_R"), ("LowerArmTwist_R", "Hand_R"), ("Hand_R", "Middle_01_R"),
                ("Thigh_L", "Calf_L"), ("Calf_L", "Foot_L"), ("Thigh_R", "Calf_R"), ("Calf_R", "Foot_R") };
            txt.AppendLine("Node rest vs skin bind pose (aligned on Pelvis): bone | total angle deg | roll about the bone deg | direction change deg");
            if (bindOnly.bones.TryGetValue("Pelvis", out var pb) && node.bones.TryGetValue("Pelvis", out var pa))
            {
                Quaternion k = pb.rot * Quaternion.Inverse(pa.rot);
                foreach (var (bone, child) in chain)
                {
                    if (!bindOnly.bones.TryGetValue(bone, out var b) || !node.bones.TryGetValue(bone, out var n0)) { txt.AppendLine($"  {bone}: no bind pose"); continue; }
                    Quaternion qn = k * n0.rot; Vector3 pn = pb.pos + k * (n0.pos - pa.pos);
                    Vector3 dirB = bindOnly.bones.TryGetValue(child, out var cb) ? (cb.pos - b.pos).normalized : Vector3.zero;
                    Vector3 dirN = node.bones.TryGetValue(child, out var cn) ? (k * (cn.pos - n0.pos)).normalized : Vector3.zero;
                    Quaternion d = Quaternion.Inverse(b.rot) * qn;
                    float roll = dirB.sqrMagnitude > 0 ? TwistDeg(d, Quaternion.Inverse(b.rot) * dirB) : float.NaN;
                    txt.AppendLine($"  {bone}: {F(Quaternion.Angle(qn, b.rot))} | {F(roll)} | {F(Vector3.Angle(dirB, dirN))} (pos diff {F((pn - b.pos).magnitude)} m)");
                }
            }

            // ---- 2. poses: bind, node rest, their humanoid round trips, muscle zero, avatar T-pose
            void SetBind()
            {
                foreach (var t in go.GetComponentsInChildren<Transform>(true).OrderBy(t => Depth(t)))
                    if (t != go.transform && bindOnly.bones.TryGetValue(t.name, out var v)) t.SetPositionAndRotation(v.pos, v.rot);   // captured in world space with the instance at the origin
            }
            void SetNode() { foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t != go.transform && node.local.TryGetValue(t.name, out var v)) { t.localPosition = v.pos; t.localRotation = v.rot; } }
            float[] saved = null;
            void RoundTrip(string label)
            {
                Row(label, "", 0, 0, 0);
                hph.GetHumanPose(ref hp); saved = (float[])hp.muscles.Clone();
                var before = go.GetComponentsInChildren<Transform>(true).ToDictionary(t => t, t => t.rotation);
                hph.SetHumanPose(ref hp);
                Row(label + "_roundtrip", "", 0, 0, 0);
                foreach (var n in new[] { "Clavicle_L", "UpperArm_L", "LowerArm_L", "Hand_L", "Clavicle_R", "UpperArm_R", "LowerArm_R", "Hand_R", "Spine", "Chest", "Thigh_L", "Calf_L" })
                    if (all.TryGetValue(n, out var t)) txt.Append($"{n} {F(Quaternion.Angle(before[t], t.rotation))}  ");
                txt.AppendLine();
            }
            SetBind(); txt.Append("Round trip of the BIND pose, bone rotation change deg: "); RoundTrip("bindpose");
            SetNode(); txt.Append("Round trip of the NODE rest, bone rotation change deg: "); RoundTrip("noderest");
            txt.AppendLine("  node-rest muscles: " + string.Join(" ", mIdx.Select((m, i) => ArmMuscles[i].Replace(" ", "") + "=" + (m >= 0 ? F(saved[m]) : "?"))));
            hph.GetHumanPose(ref hp); for (int i = 0; i < hp.muscles.Length; i++) hp.muscles[i] = 0f; hph.SetHumanPose(ref hp);
            Row("zero_muscle", "", 0, 0, 0);
            var mi = UnityEditor.AssetImporter.GetAtPath(ModelPath) as UnityEditor.ModelImporter;
            if (mi)
            {
                SetNode();
                foreach (var sk in mi.humanDescription.skeleton) if (all.TryGetValue(sk.name, out var t) && t != go.transform) { t.localPosition = sk.position; t.localRotation = sk.rotation; }
                Row("avatar_tpose", "", 0, 0, 0);
                foreach (var side in new[] { "L", "R" })
                {
                    Vector3 D(string x, string y) => all.ContainsKey(x) && all.ContainsKey(y) ? go.transform.InverseTransformDirection((all[y].position - all[x].position).normalized) : Vector3.zero;
                    txt.AppendLine($"Avatar T-pose {side}: upper arm dir {D("UpperArm_" + side, "LowerArm_" + side)} forearm dir {D("LowerArm_" + side, "Hand_" + side)} hand dir {D("Hand_" + side, "Middle_01_" + side)} palm normal (index-little x fwd) {Vector3.Cross(D("Pinky_01_" + side, "Index_01_" + side), D("Hand_" + side, "Middle_01_" + side))}");
                }
                MirrorReport(txt, "avatar T-pose", all, go.transform);
            }
            SetBind(); MirrorReport(txt, "bind pose", all, go.transform);
            SetNode(); MirrorReport(txt, "node rest", all, go.transform);

            // ---- 3. sub-frame sampling: the game samples between the 30 fps keys
            foreach (var n in new[] { "Run", "Walk", "Idle", "Gather_Plant" })
            {
                if (!clips.TryGetValue(n, out var clip)) continue;
                int steps = Mathf.RoundToInt(clip.length * (n == "Idle" ? 30f : 240f));
                for (int i = 0; i <= steps; i++) { float t = clip.length * i / steps; Sample(a, clip, t); Row(n == "Idle" ? "sub30" : "sub240", n, i, t, 0); }
            }
            // ---- 4. blends in muscle space (what a blend tree / crossfade does), w = weight of the second clip
            void Blend2(string src, string ca, float ta, string cb, float tb)
            {
                if (!clips.TryGetValue(ca, out var A) || !clips.TryGetValue(cb, out var B)) return;
                for (int i = 0; i <= 20; i++)
                {
                    float w = i / 20f;
                    var g = PlayableGraph.Create("MotionProbe_Blend");
                    try
                    {
                        g.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                        var o = AnimationPlayableOutput.Create(g, "out", a);
                        var mx = AnimationMixerPlayable.Create(g, 2);
                        var pA = AnimationClipPlayable.Create(g, A); pA.SetApplyFootIK(false); pA.SetTime(ta);
                        var pB = AnimationClipPlayable.Create(g, B); pB.SetApplyFootIK(false); pB.SetTime(tb);
                        g.Connect(pA, 0, mx, 0); g.Connect(pB, 0, mx, 1); mx.SetInputWeight(0, 1f - w); mx.SetInputWeight(1, w);
                        o.SetSourcePlayable(mx); g.Evaluate(0f);
                    }
                    finally { g.Destroy(); }
                    Row(src, ca + ">" + cb, i, 0, w);
                }
            }
            float L(string c) => clips.TryGetValue(c, out var x) ? x.length : 1f;
            Blend2("blend_walk_run_p0", "Walk", 0f, "Run", 0f);
            Blend2("blend_walk_run_p25", "Walk", 0.25f * L("Walk"), "Run", 0.25f * L("Run"));
            Blend2("blend_walk_run_p50", "Walk", 0.5f * L("Walk"), "Run", 0.5f * L("Run"));
            Blend2("blend_walk_run_p75", "Walk", 0.75f * L("Walk"), "Run", 0.75f * L("Run"));
            Blend2("blend_idle_walk_p25", "Idle", 0.25f * L("Idle"), "Walk", 0.25f * L("Walk"));
            Blend2("blend_idle_gather_in", "Idle", 0f, "Gather_Plant", 0f);
            Blend2("blend_gather_idle_out", "Gather_Plant", 0.6f, "Idle", 0f);
            Blend2("blend_gather_run_out_p0", "Gather_Plant", 0.6f, "Run", 0f);
            Blend2("blend_gather_run_out_p50", "Gather_Plant", 0.6f, "Run", 0.5f * L("Run"));
            Blend2("blend_gather_walk_out", "Gather_Plant", 0.6f, "Walk", 0.3f * L("Walk"));
            File.WriteAllText(Out("motion_probe_avatar.txt"), txt.ToString());
            File.WriteAllText(Out("motion_probe_avatar.csv"), rows.ToString());
            hph.Dispose(); Object.Destroy(go);
#else
            yield break;
#endif
        }

        static int Depth(Transform t) { int d = 0; while (t.parent) { d++; t = t.parent; } return d; }
        static float TwistDeg(Quaternion q, Vector3 ax)
        {
            ax.Normalize(); float pr = q.x * ax.x + q.y * ax.y + q.z * ax.z; float ang = 2f * Mathf.Atan2(pr, q.w) * Mathf.Rad2Deg;
            if (ang > 180f) ang -= 360f; else if (ang < -180f) ang += 360f; return ang;
        }
        /// <summary>left bone vs mirrored right bone (x -> -x in model space): same number on every arm bone = a mirrored rig</summary>
        static void MirrorReport(StringBuilder txt, string label, Dictionary<string, Transform> all, Transform root)
        {
            txt.Append("Mirror L vs R (" + label + "), angle deg: ");
            foreach (var b in new[] { "Clavicle", "UpperArm", "LowerArm", "LowerArmTwist", "Hand", "Thigh", "Calf", "Foot" })
            {
                if (!all.TryGetValue(b + "_L", out var l) || !all.TryGetValue(b + "_R", out var r)) continue;
                Quaternion ql = Quaternion.Inverse(root.rotation) * l.rotation, qr = Quaternion.Inverse(root.rotation) * r.rotation;
                var m = new Quaternion(ql.x, -ql.y, -ql.z, ql.w);
                txt.Append($"{b} {F(Quaternion.Angle(m, qr))}  ");
            }
            txt.AppendLine();
        }

        /// <summary>retargeted pose of a clip at time t: a throwaway PlayableGraph (no foot IK, no playable IK)</summary>
        static void Sample(Animator a, AnimationClip clip, float t)
        {
            var g = PlayableGraph.Create("MotionProbe_Sample");
            try
            {
                g.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var o = AnimationPlayableOutput.Create(g, "out", a);
                var cp = AnimationClipPlayable.Create(g, clip); cp.SetApplyFootIK(false); cp.SetApplyPlayableIK(false);
                o.SetSourcePlayable(cp);
                cp.SetTime(t);
                g.Evaluate(0f);
            }
            finally { g.Destroy(); }
        }

        // =====================================================================================================  B / C / D: gameplay
        [UnityTest, Timeout(120000)] public IEnumerator B_Gameplay_AllOn() { yield return Gameplay("allon", true, true); }
        [UnityTest, Timeout(120000)] public IEnumerator C_Gameplay_IKOff() { yield return Gameplay("ikoff", false, true); }
        [UnityTest, Timeout(120000)] public IEnumerator D_Gameplay_TwistDriverOff() { yield return Gameplay("twistoff", true, false); }

        IEnumerator LoadIsland()
        {
            TestScenes.UseTestSaves(); GameManager.ForceShowTitle = false; GameManager.ForcePlayIntro = false;
            PlayerInputReader.Simulate = true;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/Island_VerticalSlice.unity", new LoadSceneParameters(LoadSceneMode.Single));
#endif
            float t = 0; while ((_gm = GameManager.Instance) == null || _gm.State != GameState.Playing) { t += Time.unscaledDeltaTime; if (t > 25f) break; yield return null; }
            Assert.IsNotNull(_gm, "GameManager"); Assert.AreEqual(GameState.Playing, _gm.State, "game started");
            yield return new WaitForSeconds(0.5f);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
        static float TerrainY(Vector3 p) { var t = Terrain.activeTerrain; return t ? t.SampleHeight(p) + t.transform.position.y : p.y; }

        /// <summary>free walking distance along d (capsule sweep at body height, stops at water / steep ground)</summary>
        static float Clear(Vector3 from, Vector3 d, float len, int mask)
        {
            from.y = TerrainY(from);
            float dist = len;
            if (Physics.CapsuleCast(from + Vector3.up * 0.6f, from + Vector3.up * 1.4f, 0.32f, d, out var hit, len, mask, QueryTriggerInteraction.Ignore)) dist = hit.distance;
            float prev = from.y;
            for (float s = 0.5f; s <= dist; s += 0.5f)
            {
                float h = TerrainY(from + d * s);
                if (h < 0.3f || Mathf.Abs(h - prev) > 0.3f) return s - 0.5f;
                prev = h;
            }
            return dist;
        }

        IEnumerator Gameplay(string tag, bool ik, bool twist)
        {
            if (!On) Assert.Ignore("diagnostic probe: off (Library/PrimalBridge/probe_on.txt)");
            LogAssert.ignoreFailingMessages = true;          // diagnostic: gameplay warnings / errors must not abort the recording
            yield return LoadIsland();
            var p = _gm.Player; Assert.IsNotNull(p, "player");
            var motor = p.GetComponent<PlayerMotor>(); var pi = p.GetComponent<PlayerInteraction>(); var drv = p.GetComponent<PlayerAnimationDriver>();
            var anim = drv && drv.animator ? drv.animator : p.GetComponentInChildren<Animator>();
            var pik = anim.GetComponent<PlayerIK>(); var tbd = anim.GetComponent<TwistBoneDriver>();
            var cam = Object.FindFirstObjectByType<ThirdPersonCamera>();
            var settings = new StringBuilder();
            settings.AppendLine($"== {tag} {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            settings.AppendLine($"Animator: applyRootMotion {anim.applyRootMotion}, updateMode {anim.updateMode}, cullingMode {anim.cullingMode} (probe forces AlwaysAnimate), humanScale {F(anim.humanScale)}, isHuman {anim.isHuman}, layers {anim.layerCount}");
            for (int l = 0; l < anim.layerCount; l++) settings.AppendLine($"  layer {l} {anim.GetLayerName(l)} weight {F(anim.GetLayerWeight(l))}");
            settings.AppendLine($"Model child: {anim.transform.localPosition} {anim.transform.localEulerAngles}; root scale {p.transform.lossyScale}");
#if UNITY_EDITOR
            var mi = UnityEditor.AssetImporter.GetAtPath(ModelPath) as UnityEditor.ModelImporter;
            if (mi) { var hd = mi.humanDescription; settings.AppendLine($"Avatar: upperArmTwist {F(hd.upperArmTwist)} lowerArmTwist {F(hd.lowerArmTwist)} upperLegTwist {F(hd.upperLegTwist)} lowerLegTwist {F(hd.lowerLegTwist)} armStretch {F(hd.armStretch)} legStretch {F(hd.legStretch)} feetSpacing {F(hd.feetSpacing)} translationDoF {hd.hasTranslationDoF}"); }
#endif
            if (pik) settings.AppendLine($"PlayerIK: enabled {pik.enabled} foot {pik.footIK} look {pik.lookIK} (w {F(pik.lookWeight)} body {F(pik.bodyWeight)} head {F(pik.headWeight)} clamp {F(pik.clampWeight)}) hand {pik.handIK} elbowHint {pik.elbowHintOffset} lean max {F(pik.maxLean)} scale {F(pik.leanScale)} runningFootWeight {F(pik.runningFootWeight)}");
            if (tbd) settings.AppendLine($"TwistBoneDriver: enabled {tbd.enabled} ready {tbd.IsReady} share {F(tbd.share)} maxAngle {F(tbd.maxAngle)}");
            settings.AppendLine($"Motor: walk {F(motor.walkSpeed)} run {F(motor.runSpeed)} accel {F(motor.acceleration)} decel {F(motor.deceleration)} turnSpeed {F(motor.turnSpeed)} turnSmoothTime {F(motor.turnSmoothTime)}");
            if (drv) settings.AppendLine($"Driver: speedDamp {F(drv.speedDamp)} velDamp {F(drv.velDamp)}");
            settings.AppendLine($"Time: maximumDeltaTime {F(Time.maximumDeltaTime)} vSync {QualitySettings.vSyncCount} targetFrameRate {Application.targetFrameRate}");
            if (cam) settings.AppendLine($"Camera: distance {F(cam.distance)} collisionRadius {F(cam.collisionRadius)} collisionMask {cam.collisionMask.value} ignoreMask {cam.ignoreMask.value} (layers ignored: {string.Join("|", Enumerable.Range(0, 32).Where(i => (cam.ignoreMask.value & (1 << i)) != 0).Select(LayerMask.LayerToName))})");
            var terr = Terrain.activeTerrain;
            if (terr && terr.terrainData)
            {
                var protos = terr.terrainData.treePrototypes;
                settings.AppendLine($"Terrain trees: {terr.terrainData.treeInstanceCount} instances, prototypes: " + string.Join("; ", protos.Select(tp => tp.prefab ? $"{tp.prefab.name} collider={(tp.prefab.GetComponentInChildren<Collider>() != null)} layer={LayerMask.LayerToName(tp.prefab.layer)}" : "null")));
                var tc = terr.GetComponent<TerrainCollider>(); settings.AppendLine($"TerrainCollider: {(tc ? "yes" : "no")}");
            }
            File.AppendAllText(Out("motion_probe_settings.txt"), settings.ToString());

            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (!ik && pik) { pik.Suspended = true; pik.footIK = false; pik.lookIK = false; pik.handIK = false; }
            if (!twist && tbd) tbd.enabled = false;

            var rest = RestPose.FromAnimator(anim);
            var rec = anim.gameObject.AddComponent<MotionProbeRecorder>();
            rec.Init(new ProbeRig(anim, p.transform, rest), motor, drv, pik, tbd);

            int mask = Physics.DefaultRaycastLayers & ~(1 << p.layer) & ~(1 << 2);
            Vector3 spawn = p.transform.position;
            // run legs: walk 2 s + run 2.5 s along d, 90 deg right for 1.5 s, then back (180) for 1.5 s
            Vector3 bestD = p.transform.forward; float best = -1f;
            for (int i = 0; i < 24; i++)
            {
                var d = Quaternion.Euler(0f, i * 15f, 0f) * Vector3.forward; var r = Vector3.Cross(Vector3.up, d);
                float s = Mathf.Min(Clear(spawn, d, 13.5f, mask) / 13.5f, Clear(spawn + d * 12.5f, r, 6.5f, mask) / 6.5f);
                if (s > best) { best = s; bestD = d; }
            }
            var right = Vector3.Cross(Vector3.up, bestD);
            Vector3 start = spawn; start.y = TerrainY(start) + 0.05f;
            motor.Warp(start, Quaternion.LookRotation(bestD));
            if (cam) { cam.Yaw = p.transform.eulerAngles.y; cam.SnapBehindTarget(); }
            Debug.Log($"[MotionProbe] {tag}: run heading {bestD} clearance score {F(best)}");

            void Move(Vector3 w, bool walk)
            {
                var ct = motor.CameraTransform;
                Vector3 f = ct ? Flat(ct.forward).normalized : Vector3.forward; if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
                Vector3 r = Vector3.Cross(Vector3.up, f);
                var mv = w.sqrMagnitude > 1e-4f ? new Vector2(Vector3.Dot(w, r), Vector3.Dot(w, f)) : Vector2.zero;
                PlayerInputReader.Sim.Move = mv; PlayerInputReader.Sim.Walk = walk; rec.input = mv;
            }
            IEnumerator Hold(string phase, float seconds, Func<Vector3> dir, bool walk = false, Func<bool> until = null)
            {
                rec.phase = phase; float tt = 0f;
                while (tt < seconds && (until == null || !until())) { Move(dir != null ? dir() : Vector3.zero, walk); yield return null; tt += Time.deltaTime; }
            }

            rec.recording = true; rec.t0 = Time.time;
            yield return Hold("idle", 1.0f, null);
            yield return Hold("walk", 2.0f, () => bestD, true);
            yield return Hold("run", 2.5f, () => bestD);
            yield return Hold("turn90", 1.5f, () => right);
            yield return Hold("turn180", 1.5f, () => -right);
            yield return Hold("stop", 1.2f, null);

            // ---- gather 1: run up to a hand-gatherable node, press at 1.7 m (still moving), gather, cancel by moving away
            var node = Interactable.Active.OfType<ResourceNode>().Where(n => n && n.requiredTool == ToolKind.None && n.CanInteract(pi))
                .OrderBy(n => n.yieldItem && n.yieldItem.id == "wood" ? 0 : 1).ThenBy(n => (n.transform.position - spawn).sqrMagnitude).FirstOrDefault();
            if (node == null) { Debug.LogWarning("[MotionProbe] no hand-gatherable node"); }
            else
            {
                node.charges = 99; node.Regrow();                        // never depletes during the probe
                Vector3 focus = node.FocusPoint;
                Vector3 appr = Vector3.forward; float bestA = -1f;
                for (int i = 0; i < 12; i++)
                {
                    var d = Quaternion.Euler(0f, i * 30f, 0f) * Vector3.forward;
                    var s0 = focus + d * 5.5f; if (TerrainY(s0) < 0.3f) continue;
                    float s = Clear(s0, -d, 3.8f, mask) - Mathf.Abs(TerrainY(s0) - focus.y) * 0.5f;
                    if (s > bestA) { bestA = s; appr = d; }
                }
                Vector3 s1 = focus + appr * 5.5f; s1.y = TerrainY(s1) + 0.05f;
                motor.Warp(s1, Quaternion.LookRotation(-appr));
                if (cam) { cam.Yaw = p.transform.eulerAngles.y; cam.SnapBehindTarget(); }
                Vector3 aim = focus + Vector3.Cross(Vector3.up, appr) * 0.7f;   // pass beside the focus: the body arrives ~25 deg off it
                settings.Clear(); settings.AppendLine($"  node {node.name} '{node.displayName}' action {node.handAction} focus {focus} approach {appr}");
                File.AppendAllText(Out("motion_probe_settings.txt"), settings.ToString());
                yield return Hold("pre_g1", 0.8f, null);
                yield return Hold("approach", 3.0f, () => Flat(aim - p.transform.position).normalized, false, () => Flat(focus - p.transform.position).magnitude < 1.7f);
                Move(Vector3.zero, false);
                node.Interact(pi);
                rec.phase = "gather1";
                float tt = 0f; while (!pi.InAction && tt < 0.5f) { yield return null; tt += Time.deltaTime; if (!pi.InAction) node.Interact(pi); }
                yield return Hold("gather1", 4.5f, null);
                Vector3 awayDir = Flat(p.transform.position - focus).normalized;
                yield return Hold("cancel_move", 1.6f, () => awayDir);
                yield return Hold("after1", 1.2f, null);

                // ---- gather 2: from standing, 40 deg off the focus, cancel with E while standing
                Vector3 d2 = Flat(p.transform.position - focus).normalized; if (d2.sqrMagnitude < 0.01f) d2 = appr;
                Vector3 s2 = focus + d2 * (node.Radius + 1.0f); s2.y = TerrainY(s2) + 0.05f;
                motor.Warp(s2, Quaternion.Euler(0f, 40f, 0f) * Quaternion.LookRotation(-d2));
                if (cam) { cam.Yaw = p.transform.eulerAngles.y; cam.SnapBehindTarget(); }
                yield return Hold("idle2", 1.0f, null);
                node.Interact(pi);
                tt = 0f; while (!pi.InAction && tt < 0.5f) { yield return null; tt += Time.deltaTime; if (!pi.InAction) node.Interact(pi); }
                yield return Hold("gather2", 3.0f, null);
                PlayerInputReader.Sim.Interact = true;
                yield return Hold("cancel_e", 1.5f, null);
            }
            rec.recording = false;
            File.WriteAllText(Out($"motion_probe_gameplay_{tag}.csv"), rec.Header() + rec.sb);
            Debug.Log($"[MotionProbe] {tag}: {rec.rows.Count} rows");
            Object.Destroy(rec);
        }

        internal static string F(float v) => float.IsNaN(v) ? "" : v.ToString("0.####", IC);
    }

    /// <summary>rest (bind) pose of the bones by name: world rotation / position in one common frame</summary>
    public class RestPose
    {
        public readonly Dictionary<string, (Vector3 pos, Quaternion rot)> bones = new Dictionary<string, (Vector3, Quaternion)>();
        public readonly Dictionary<string, (Vector3 pos, Quaternion rot)> local = new Dictionary<string, (Vector3, Quaternion)>();

        /// <summary>from the model asset's own transforms (the FBX rest pose)</summary>
        public static RestPose FromAsset(GameObject asset)
        {
            var r = new RestPose();
            foreach (var t in asset.GetComponentsInChildren<Transform>(true)) if (!r.bones.ContainsKey(t.name)) { r.bones[t.name] = (t.position, t.rotation); r.local[t.name] = (t.localPosition, t.localRotation); }
            return r;
        }

        /// <summary>from the skinned meshes' bind poses (independent of the current pose); falls back to the model asset</summary>
        public static RestPose FromAnimator(Animator a, bool fallback = true)
        {
            var r = new RestPose();
            SkinnedMeshRenderer body = null; int bestN = -1;
            foreach (var smr in a.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.sharedMesh && smr.bones.Length == smr.sharedMesh.bindposes.Length && smr.bones.Length > bestN) { bestN = smr.bones.Length; body = smr; }
            if (body)
            {
                var bp = body.sharedMesh.bindposes; var bs = body.bones;
                var toWorld = body.transform.localToWorldMatrix;               // mesh space -> world, as the instance stands now
                for (int i = 0; i < bs.Length; i++) if (bs[i] && !r.bones.ContainsKey(bs[i].name)) { var m = toWorld * bp[i].inverse; r.bones[bs[i].name] = ((Vector3)m.GetColumn(3), m.rotation); }
            }
#if UNITY_EDITOR
            var asset = fallback ? UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Characters/Player/Model/PLAYER_Survivor.fbx") : null;
            if (asset)
            {
                // bones missing from the body mesh (clavicle...): asset rest, expressed in the bind frame through a shared bone (Pelvis)
                var ar = FromAsset(asset);
                if (r.bones.TryGetValue("Pelvis", out var pb) && ar.bones.TryGetValue("Pelvis", out var pa))
                {
                    Quaternion k = pb.rot * Quaternion.Inverse(pa.rot);
                    foreach (var kv in ar.bones) if (!r.bones.ContainsKey(kv.Key)) r.bones[kv.Key] = (pb.pos + k * (kv.Value.pos - pa.pos), k * kv.Value.rot);
                }
                else if (r.bones.Count == 0) return ar;
            }
#endif
            return r;
        }
    }

    /// <summary>pose measurements in the character's own space (see CharacterMotionProbe)</summary>
    public class ProbeRig
    {
        public readonly Animator a; public readonly Transform root;
        readonly Transform hips, neck, thighL, thighR;
        readonly Side L, R;

        class Side
        {
            public bool left; public Transform clav, upper, lower, hand, twist, index1, little1;
            public Quaternion c0, u0, l0, h0, t0; public bool okC, okU, okL, okH, okT;
            public Vector3 axU, axL, axH, axT;      // long axes in each bone's local frame (rest)
        }

        public ProbeRig(Animator a, Transform root, RestPose rest)
        {
            this.a = a; this.root = root;
            hips = a.GetBoneTransform(HumanBodyBones.Hips); neck = a.GetBoneTransform(HumanBodyBones.Neck) ?? a.GetBoneTransform(HumanBodyBones.Head);
            thighL = a.GetBoneTransform(HumanBodyBones.LeftUpperLeg); thighR = a.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            L = MakeSide(true, rest); R = MakeSide(false, rest);
        }

        Side MakeSide(bool left, RestPose rest)
        {
            var s = new Side { left = left };
            s.clav = a.GetBoneTransform(left ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder);
            s.upper = a.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            s.lower = a.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            s.hand = a.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            s.index1 = a.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
            s.little1 = a.GetBoneTransform(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
            s.twist = s.lower ? s.lower.Find(left ? "LowerArmTwist_L" : "LowerArmTwist_R") : null;
            if (!s.twist && s.lower) foreach (var t in s.lower.parent.GetComponentsInChildren<Transform>(true)) if (t.name == (left ? "LowerArmTwist_L" : "LowerArmTwist_R")) { s.twist = t; break; }
            bool Get(Transform t, out (Vector3 pos, Quaternion rot) v) { v = default; return t && rest != null && rest.bones.TryGetValue(t.name, out v); }
            if (Get(s.clav, out var c)) { s.c0 = c.rot; s.okC = true; }
            Get(s.upper, out var u); Get(s.lower, out var l); Get(s.hand, out var h);
            if (Get(s.upper, out _) && Get(s.lower, out _)) { s.u0 = u.rot; s.axU = Quaternion.Inverse(u.rot) * (l.pos - u.pos).normalized; s.okU = true; }
            if (Get(s.lower, out _) && Get(s.hand, out _))
            {
                Vector3 fa = (h.pos - l.pos).normalized;
                s.l0 = l.rot; s.axL = Quaternion.Inverse(l.rot) * fa; s.okL = true;
                s.h0 = h.rot; s.axH = Quaternion.Inverse(h.rot) * fa; s.okH = true;
                if (Get(s.twist, out var tw)) { s.t0 = tw.rot; s.axT = Quaternion.Inverse(tw.rot) * fa; s.okT = true; }
            }
            return s;
        }

        static readonly string[] SideCols = { "abd", "elev", "swing", "elbow", "handUp", "handFwd", "handOut", "elbowOut", "rollUpper", "rollLower", "rollHand", "rollTwist", "thumbRoll", "hx", "hy", "hz" };
        public static string PoseHeader => "hipsY,hipsZ,chestYaw,torsoPitch," + string.Join(",", SideCols.Select(c => "L_" + c)) + "," + string.Join(",", SideCols.Select(c => "R_" + c));

        /// <summary>swing-twist: rotation angle of q about axis (deg, -180..180)</summary>
        static float Twist(Quaternion q, Vector3 ax)
        {
            float pr = q.x * ax.x + q.y * ax.y + q.z * ax.z;
            float ang = 2f * Mathf.Atan2(pr, q.w) * Mathf.Rad2Deg;
            if (ang > 180f) ang -= 360f; else if (ang < -180f) ang += 360f;
            return ang;
        }
        /// <summary>roll of child relative to parent about the child's axis, zero at the rest relation</summary>
        static float RelTwist(Transform parent, Quaternion p0, Transform child, Quaternion c0, Vector3 axChild)
        {
            Quaternion rn = Quaternion.Inverse(parent.rotation) * child.rotation;
            Quaternion r0 = Quaternion.Inverse(p0) * c0;
            return Twist(Quaternion.Inverse(r0) * rn, axChild);
        }

        public void Pose(StringBuilder sb)
        {
            Vector3 up = (neck.position - hips.position).normalized;
            Vector3 right = R.upper.position - L.upper.position; right = (right - Vector3.Dot(right, up) * up).normalized;
            Vector3 fwd = Vector3.Cross(right, up);
            Vector3 hl = root.InverseTransformPoint(hips.position);
            Vector3 ru = root.up;
            float chestYaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(thighR.position - thighL.position, ru), Vector3.ProjectOnPlane(R.upper.position - L.upper.position, ru), ru);
            float pitch = Vector3.SignedAngle(ru, up, root.right);
            sb.Append(CharacterMotionProbe.F(hl.y)).Append(',').Append(CharacterMotionProbe.F(hl.z)).Append(',').Append(CharacterMotionProbe.F(chestYaw)).Append(',').Append(CharacterMotionProbe.F(pitch));
            foreach (var s in new[] { L, R })
            {
                Vector3 outv = s.left ? -right : right;
                Vector3 sh = s.upper.position, el = s.lower.position, ha = s.hand.position;
                Vector3 u = el - sh, f = ha - el;
                float abd = Mathf.Asin(Mathf.Clamp(Vector3.Dot(u.normalized, outv), -1f, 1f)) * Mathf.Rad2Deg;
                float elev = Vector3.Angle(u, -up);
                float swing = Mathf.Atan2(Vector3.Dot(u, fwd), -Vector3.Dot(u, up)) * Mathf.Rad2Deg;
                float elbow = Vector3.Angle(u, f);
                Vector3 hs = ha - sh;
                float rU = s.okU && s.okC && s.clav ? RelTwist(s.clav, s.c0, s.upper, s.u0, s.axU) : float.NaN;
                float rL = s.okU && s.okL ? RelTwist(s.upper, s.u0, s.lower, s.l0, s.axL) : float.NaN;
                float rH = s.okL && s.okH ? RelTwist(s.lower, s.l0, s.hand, s.h0, s.axH) : float.NaN;
                float rT = s.okL && s.okT && s.twist ? RelTwist(s.lower, s.l0, s.twist, s.t0, s.axT) : float.NaN;
                float thumb = float.NaN;
                if (s.index1 && s.little1 && f.sqrMagnitude > 1e-6f)
                {
                    Vector3 ax = f.normalized;
                    Vector3 th = Vector3.ProjectOnPlane(s.index1.position - s.little1.position, ax), rf = Vector3.ProjectOnPlane(outv, ax);
                    if (th.sqrMagnitude > 1e-8f && rf.sqrMagnitude > 1e-4f) thumb = Vector3.SignedAngle(rf, th, ax);
                }
                Vector3 hlp = root.InverseTransformPoint(ha);
                float[] v = { abd, elev, swing, elbow, Vector3.Dot(hs, up), Vector3.Dot(hs, fwd), Vector3.Dot(hs, outv), Vector3.Dot(u, outv), rU, rL, rH, rT, thumb, hlp.x, hlp.y, hlp.z };
                foreach (var x in v) sb.Append(',').Append(CharacterMotionProbe.F(x));
            }
        }
    }

    /// <summary>samples after everything (Animator, IK pass, TwistBoneDriver at order 90, camera at 100)</summary>
    [DefaultExecutionOrder(10000)]
    public class MotionProbeRecorder : MonoBehaviour
    {
        public struct Row { public int idx; public float t, norm; public string phase, state; public bool inTrans; }
        public readonly StringBuilder sb = new StringBuilder();
        public readonly List<Row> rows = new List<Row>();
        public string phase = ""; public Vector2 input; public bool recording; public float t0;
        ProbeRig _rig; PlayerMotor _motor; PlayerAnimationDriver _drv; PlayerIK _ik; TwistBoneDriver _tw;
        float _prevYaw = float.NaN;
        static readonly Dictionary<int, string> Names = new Dictionary<int, string>();
        static readonly string[] Fields = { "_wL", "_wR", "_lookW", "_offW", "_drawW", "_pelvis", "_lean" };
        FieldInfo[] _f;

        static MotionProbeRecorder()
        {
            foreach (var n in new[] { "Locomotion", "Idle_Variation", "Crouch", "TurnInPlace", "StrafeLocomotion", "Jump", "Fall", "Land", "Pickup", "Gather_Wood", "Gather_Stone", "Gather_Plant",
                "Interact", "Craft", "Eat", "Drink", "Build", "Use_Item", "Sleep", "Wake_Up", "Get_Up", "Unconscious", "Attack_Spear", "Attack_Spear_Heavy", "Throw_Spear", "Spear_Attack_2",
                "Knife_Attack", "Sword_Attack_1", "Sword_Attack_2", "Sword_Attack_3", "Sword_Heavy", "Dodge", "Climb_Start", "Climb_Idle", "Climb_Up", "Climb_Down", "Harvest_Fruit", "Climb_End",
                "Hurt", "Hurt_Heavy", "Death", "Empty", "Bow_Aim", "Bow_Draw", "Bow_Release", "Carry_Item", "Sword_Block", "Sword_Equip", "Sword_Unequip", "Sword_Idle", "Hurt_Additive" })
                Names[Animator.StringToHash(n)] = n;
        }
        static string N(int h) => Names.TryGetValue(h, out var n) ? n : h.ToString();

        public void Init(ProbeRig rig, PlayerMotor motor, PlayerAnimationDriver drv, PlayerIK ik, TwistBoneDriver tw)
        {
            _rig = rig; _motor = motor; _drv = drv; _ik = ik; _tw = tw;
            _f = Fields.Select(n => typeof(PlayerIK).GetField(n, BindingFlags.NonPublic | BindingFlags.Instance)).ToArray();
        }

        public string Header() => "t,dt,udt,phase,inX,inY,yaw,yawRate,motorTurnRate,planar,measured,busy,canMove,pSpeed,pTurnSpeed,pAction,pVelX,pVelZ,state,stNorm,stLen,inTrans,next,nxNorm,trNorm,trDur,L1w,L1state,L2state," +
                                  string.Join(",", Fields.Select(f => "ik" + f)) + ",ikSusp,twistL,twistR,rootY,terrainAtRoot,gFootL,gFootR,hitL,hitR," + ProbeRig.PoseHeader + "\n";

        static string F(float v) => CharacterMotionProbe.F(v);
        static string B(bool b) => b ? "1" : "0";

        void LateUpdate()
        {
            if (!recording || _rig == null) return;
            var a = _rig.a; float dt = Time.deltaTime; float t = Time.time - t0;
            float yaw = _rig.root.eulerAngles.y;
            float yr = float.IsNaN(_prevYaw) || dt <= 0f ? 0f : Mathf.DeltaAngle(_prevYaw, yaw) / dt; _prevYaw = yaw;
            var st = a.GetCurrentAnimatorStateInfo(0); bool inT = a.IsInTransition(0);
            var nx = inT ? a.GetNextAnimatorStateInfo(0) : default; var tr = inT ? a.GetAnimatorTransitionInfo(0) : default;
            sb.Append(F(t)).Append(',').Append(F(dt)).Append(',').Append(F(Time.unscaledDeltaTime)).Append(',').Append(phase).Append(',').Append(F(input.x)).Append(',').Append(F(input.y)).Append(',')
              .Append(F(yaw)).Append(',').Append(F(yr)).Append(',');
            if (_motor) sb.Append(F(_motor.TurnRate)).Append(',').Append(F(_motor.PlanarSpeed)).Append(',').Append(F(_motor.MeasuredPlanarSpeed)).Append(','); else sb.Append(",,,");
            if (_drv) sb.Append(B(_drv.IsBusy)).Append(',').Append(B(_motor && _motor.CanMove)).Append(','); else sb.Append(",,");
            sb.Append(F(a.GetFloat("Speed"))).Append(',').Append(F(a.GetFloat("TurnSpeed"))).Append(',').Append(a.GetInteger("Action")).Append(',').Append(F(a.GetFloat("VelX"))).Append(',').Append(F(a.GetFloat("VelZ"))).Append(',');
            sb.Append(N(st.shortNameHash)).Append(',').Append(F(st.normalizedTime)).Append(',').Append(F(st.length)).Append(',').Append(B(inT)).Append(',')
              .Append(inT ? N(nx.shortNameHash) : "").Append(',').Append(inT ? F(nx.normalizedTime) : "").Append(',').Append(inT ? F(tr.normalizedTime) : "").Append(',').Append(inT ? F(tr.duration) : "").Append(',');
            if (a.layerCount > 1) sb.Append(F(a.GetLayerWeight(1))).Append(',').Append(N(a.GetCurrentAnimatorStateInfo(1).shortNameHash)).Append(','); else sb.Append(",,");
            if (a.layerCount > 2) sb.Append(N(a.GetCurrentAnimatorStateInfo(2).shortNameHash)).Append(','); else sb.Append(',');
            for (int i = 0; i < Fields.Length; i++) sb.Append(_ik && _f[i] != null ? F((float)_f[i].GetValue(_ik)) : "").Append(',');
            sb.Append(_ik ? B(_ik.Suspended) : "").Append(',');
            sb.Append(_tw && _tw.enabled ? F(_tw.LeftTwist) : "").Append(',').Append(_tw && _tw.enabled ? F(_tw.RightTwist) : "").Append(',');
            // ground as PlayerIK.Foot sees it: ray from the ankle's xz, 0.5 m above the capsule bottom, 1.25 m down (height relative to the root)
            float ry = _rig.root.position.y; sb.Append(F(ry)).Append(',');
            var terr = Terrain.activeTerrain; sb.Append(terr ? F(terr.SampleHeight(_rig.root.position) + terr.transform.position.y - ry) : "").Append(',');
            string hitL = "", hitR = "";
            foreach (var (bone, isL) in new[] { (HumanBodyBones.LeftFoot, true), (HumanBodyBones.RightFoot, false) })
            {
                var ft = a.GetBoneTransform(bone); float gy = float.NaN;
                if (ft && _ik)
                {
                    var o = new Vector3(ft.position.x, ry + _ik.rayAbove, ft.position.z);
                    if (Physics.Raycast(o, Vector3.down, out var h, _ik.rayAbove + _ik.rayBelow, _ik.groundMask, QueryTriggerInteraction.Ignore))
                    { gy = h.point.y - ry; if (!(h.collider is TerrainCollider)) { if (isL) hitL = h.collider.name.Replace(',', ' '); else hitR = h.collider.name.Replace(',', ' '); } }
                }
                sb.Append(F(gy)).Append(',');
            }
            sb.Append(hitL).Append(',').Append(hitR).Append(',');
            _rig.Pose(sb); sb.Append('\n');
            rows.Add(new Row { idx = rows.Count, t = t, norm = st.normalizedTime, phase = phase, state = N(st.shortNameHash), inTrans = inT });
        }
    }
}
