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
