using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public static partial class PrimalCharacterBuilder
    {
        // ------------------------------------------------------------------ prefab
        static Bounds RendererBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        static GameObject BuildPrefab(Spec spec, string fbx, AnimatorController controller)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            go.name = spec.Prefab;
            var anim = go.GetComponent<Animator>(); if (anim == null) anim = go.AddComponent<Animator>();
            anim.runtimeAnimatorController = controller;
            anim.avatar = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Avatar>().FirstOrDefault();
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            if (!go.GetComponent<CharacterAnimationEvents>()) go.AddComponent<CharacterAnimationEvents>();
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.updateWhenOffscreen = false; smr.quality = SkinQuality.Bone4; smr.skinnedMotionVectors = true;
            }
            EnsureLodGroup(go, spec);
            var b = RendererBounds(go);
            if (spec.Humanoid)
            {
                var cc = go.GetComponent<CharacterController>(); if (cc == null) cc = go.AddComponent<CharacterController>();
                cc.height = 1.8f; cc.radius = 0.3f; cc.center = new Vector3(0, 0.9f, 0); cc.stepOffset = 0.35f; cc.slopeLimit = 45f; cc.skinWidth = 0.03f;
            }
            else AddDinoColliders(go, b);
            string path = $"{spec.Folder}/Prefab/{spec.Prefab}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            UnityEngine.Object.DestroyImmediate(go);
            L($"Prefab: {path}");
            return prefab;
        }

        static void EnsureLodGroup(GameObject go, Spec spec)
        {
            var lodRenderers = new SortedDictionary<int, List<Renderer>>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                int k = r.name.LastIndexOf("_LOD", StringComparison.Ordinal);
                if (k < 0 || !int.TryParse(r.name.Substring(k + 4), out int lod)) continue;
                if (!lodRenderers.ContainsKey(lod)) lodRenderers[lod] = new List<Renderer>();
                lodRenderers[lod].Add(r);
            }
            if (lodRenderers.Count < 2) { L("LOD: single LOD"); return; }
            var lg = go.GetComponent<LODGroup>(); if (lg == null) lg = go.AddComponent<LODGroup>();
            var thresholds = spec.Humanoid ? new[] { 0.25f, 0.02f } : new[] { 0.35f, 0.12f, 0.02f };
            var lods = lodRenderers.Select((kv, i) => new LOD(thresholds[Mathf.Min(i, thresholds.Length - 1)], kv.Value.ToArray())).ToArray();
            lg.SetLODs(lods); lg.RecalculateBounds();
            L($"LOD: {lods.Length} levels ({string.Join(", ", lodRenderers.Select(kv => $"LOD{kv.Key}:{kv.Value.Sum(r => TriCount(r))} tris"))})");
        }

        static int TriCount(Renderer r)
        {
            Mesh m = null; if (r is SkinnedMeshRenderer s) m = s.sharedMesh; else { var mf = r.GetComponent<MeshFilter>(); if (mf != null) m = mf.sharedMesh; }
            return m == null ? 0 : m.triangles.Length / 3;
        }

        // ------------------------------------------------------------------ test
        static Transform FootBone(Animator a, Spec spec, bool left)
        {
            // the ball of the foot (toe joint) is the pivot for both flat stance and heel-off, so it is the point that must not slide
            if (spec.Humanoid)
            {
                var toe = a.GetBoneTransform(left ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes);
                return toe != null ? toe : a.GetBoneTransform(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
            }
            return null;
        }

        static bool Test(Spec spec, GameObject prefab, List<AnimationClip> clips, AnimMeta meta)
        {
            int failsBefore = _fails;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.position = Vector3.zero;
            var anim = go.GetComponent<Animator>();
            if (anim == null || anim.runtimeAnimatorController == null) F("prefab has no Animator/controller");
            if (anim != null && (anim.avatar == null || !anim.avatar.isValid)) F("prefab avatar missing/invalid");
            var b = RendererBounds(go);
            L($"Scale: bounds size {b.size}, min y {b.min.y:F3}");
            if (Mathf.Abs(b.size.y - spec.ExpectedHeight) > spec.ExpectedHeight * 0.15f && spec.Humanoid) F($"height {b.size.y:F2} m, expected ~{spec.ExpectedHeight}");
            if (spec.Humanoid)
            {
                var cc = go.GetComponent<CharacterController>();
                if (cc == null) F("no CharacterController");
                else L($"Collider: CharacterController h={cc.height} r={cc.radius} (mesh height {b.size.y:F2})");
            }
            else TestDinoColliders(go, b);
            // every clip must be in the controller
            var used = new HashSet<AnimationClip>(anim.runtimeAnimatorController.animationClips);
            var unused = clips.Where(c => !used.Contains(c)).Select(c => c.name).ToList();
            if (unused.Count > 0) L("Clips not referenced by the controller: " + string.Join(", ", unused));
            // sample every clip
            var metaBy = meta?.clips?.ToDictionary(c => c.name, c => c) ?? new Dictionary<string, ClipMeta>();
            var bones = go.GetComponentsInChildren<Transform>(true);
            AnimationMode.StartAnimationMode();
            float maxMotion = 0f;
            foreach (var clip in clips)
            {
                int n = Mathf.Max(8, Mathf.RoundToInt(clip.length * 30));
                bool nan = false; float minY = 99, maxY = -99;
                var lf = new List<Vector3>(); var rf = new List<Vector3>();
                Vector3[] first = null, last = null;
                for (int i = 0; i <= n; i++)
                {
                    Sample(go, clip, clip.length * i / n);
                    foreach (var t in bones) { var p = t.position; if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsInfinity(p.x)) nan = true; }
                    var l = FootBone(anim, spec, true); var r = FootBone(anim, spec, false);
                    if (l && r) { lf.Add(l.position); rf.Add(r.position); minY = Mathf.Min(minY, l.position.y, r.position.y); maxY = Mathf.Max(maxY, l.position.y, r.position.y); }
                    if (i == 0) first = bones.Select(t => t.position).ToArray();
                    if (i == n) last = bones.Select(t => t.position).ToArray();
                    if (i > 0 && first != null) maxMotion = Mathf.Max(maxMotion, bones.Select((t, k) => (t.position - first[k]).magnitude).Max());
                }
                if (nan) F($"{clip.name}: NaN in pose");
                metaBy.TryGetValue(clip.name, out var cm);
                string line = $"{clip.name,-18} {clip.length:F2}s loop={(cm != null && cm.loop ? "Y" : "N")} events={clip.events.Length}";
                if (cm != null && cm.loop && first != null)
                {
                    float gap = first.Zip(last, (a2, b2) => (a2 - b2).magnitude).Max();
                    line += $" loopGap={gap * 100:F1}cm";
                    if (gap > 0.03f) F($"{clip.name}: loop not seamless ({gap * 100:F1} cm)");
                }
                if (cm != null && cm.speed > 0.01f && lf.Count > 4)
                {
                    var exp = ExpectedFootVelocity(clip.name, cm.speed);
                    float slide = Math.Max(FootSlide(lf, clip.length / n, exp, minY, out var vl, out int cl), FootSlide(rf, clip.length / n, exp, minY, out var vr, out int cr));
                    line += $" footSlide={slide:F2}m/s ({slide / cm.speed * 100:F0}% of {cm.speed:F2}) contactVel L{vl:F2} ({cl}f) R{vr:F2} ({cr}f)";
                    if (slide > Mathf.Max(0.15f, cm.speed * 0.12f)) F($"{clip.name}: foot sliding {slide:F2} m/s");
                }
                L(line);
            }
            if (maxMotion < 0.05f) F($"animation sampling produced no motion (max bone travel {maxMotion:F3} m) - clips not applied");
            else L($"Sampler check: max bone travel across clips {maxMotion:F2} m");
            // grounded check on the real skinned mesh in the first frame of IDLE
            var idle = clips.FirstOrDefault(c => c.name == "IDLE") ?? clips[0];
            Sample(go, idle, 0f);
            float minVertY = float.MaxValue;
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!smr.name.EndsWith("_LOD0") && go.GetComponentsInChildren<SkinnedMeshRenderer>().Length > 1 && smr.name.Contains("_LOD")) continue;
                var baked = new Mesh(); smr.BakeMesh(baked, true);
                foreach (var v in baked.vertices) minVertY = Mathf.Min(minVertY, smr.transform.TransformPoint(v).y);
                UnityEngine.Object.DestroyImmediate(baked);
            }
            L($"Ground contact (IDLE frame 0, skinned vertices): min y {minVertY:F3} m");
            if (spec.Humanoid)
            {
                var ls = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm).position; var rs = anim.GetBoneTransform(HumanBodyBones.RightUpperArm).position;
                var fwd = Vector3.Cross(Vector3.up, ls - rs).normalized;
                var toeDir = (anim.GetBoneTransform(HumanBodyBones.LeftToes).position - anim.GetBoneTransform(HumanBodyBones.LeftFoot).position); toeDir.y = 0;
                L($"Facing: shoulders -> {fwd}, left foot -> toes {toeDir.normalized} (expected +Z)");
                if (fwd.z < 0.8f) F("character does not face +Z");
            }
            if (Mathf.Abs(minVertY) > 0.05f) F($"feet not on the ground in IDLE: min y {minVertY:F3}");
            Screenshots(spec, go, clips);
            AnimationMode.StopAnimationMode();
            UnityEngine.Object.DestroyImmediate(go);
            bool ok = _fails == failsBefore;
            L(ok ? "RESULT: PASS" : $"RESULT: FAIL ({_fails - failsBefore} failures)");
            return ok;
        }

        static List<GameObject> BakeProxies(GameObject go)
        {
            var list = new List<GameObject>();
            var lg = go.GetComponent<LODGroup>();
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.enabled = false;
                if (smr.name.Contains("_LOD") && !smr.name.EndsWith("_LOD0")) continue;
                var m = new Mesh(); smr.BakeMesh(m, true);
                var p = new GameObject("proxy_" + smr.name);
                p.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
                p.AddComponent<MeshFilter>().sharedMesh = m;
                p.AddComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;
                list.Add(p);
            }
            return list;
        }

        static void Sample(GameObject go, AnimationClip clip, float time)
        {
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(go, clip, time);
            AnimationMode.EndSampling();
        }

        static Vector3 ExpectedFootVelocity(string clip, float speed)
        {
            // in-place clips: a planted foot moves opposite to the body's travel. Unity forward = +Z, character left = -X.
            if (clip.Contains("BACKWARD")) return new Vector3(0, 0, speed);
            if (clip.Contains("WALK_LEFT")) return new Vector3(speed, 0, 0);
            if (clip.Contains("WALK_RIGHT")) return new Vector3(-speed, 0, 0);
            return new Vector3(0, 0, -speed);
        }

        /// <summary>mean |v - expected| over frames where the foot is in ground contact (within 2 cm of its lowest height)</summary>
        static float FootSlide(List<Vector3> pos, float dt, Vector3 expected, float minY, out Vector3 meanV, out int cnt)
        {
            float lowest = pos.Min(p => p.y);
            float sum = 0; cnt = 0; meanV = Vector3.zero;
            for (int i = 1; i < pos.Count; i++)
            {
                if (pos[i].y > lowest + 0.02f || pos[i - 1].y > lowest + 0.02f) continue;
                var v = (pos[i] - pos[i - 1]) / dt; v.y = 0;
                sum += (v - expected).magnitude; meanV += v; cnt++;
            }
            if (cnt == 0) { meanV = Vector3.zero; return 999f; }
            meanV /= cnt;
            return sum / cnt;
        }

        // ------------------------------------------------------------------ screenshots
        static void Screenshots(Spec spec, GameObject go, List<AnimationClip> clips)
        {
            var anim = go.GetComponent<Animator>();
            var light = new GameObject("Key").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.3f; light.transform.rotation = Quaternion.Euler(40, 150, 0);
            var fill = new GameObject("Fill").AddComponent<Light>(); fill.type = LightType.Directional; fill.intensity = 0.5f; fill.color = new Color(0.8f, 0.87f, 1f); fill.transform.rotation = Quaternion.Euler(60, -60, 0);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.45f, 0.47f, 0.5f);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); ground.transform.localScale = Vector3.one * 5;
            var gm = new Material(Shader.Find("Universal Render Pipeline/Lit")); gm.SetColor("_BaseColor", new Color(0.35f, 0.33f, 0.3f)); ground.GetComponent<Renderer>().sharedMaterial = gm;
            var camGo = new GameObject("Cam"); var cam = camGo.AddComponent<Camera>(); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.42f, 0.44f, 0.47f);
            var b = RendererBounds(go);
            float h = Mathf.Max(b.size.y, b.size.z * 0.6f, 1.0f);
            float dist = h * 2.3f;
            const int W = 360, H = 440;
            var rt = new RenderTexture(W, H, 24) { antiAliasing = 4 };
            cam.targetTexture = rt; cam.fieldOfView = 35;
            var picks = clips.Where(c => !c.name.StartsWith("__")).ToList();
            int cols = 8, rows = Mathf.CeilToInt(picks.Count / (float)cols);
            var sheet = new Texture2D(cols * W, rows * H, TextureFormat.RGB24, false);
            var tmp = new Texture2D(W, H, TextureFormat.RGB24, false);
            for (int i = 0; i < picks.Count; i++)
            {
                var clip = picks[i];
                Sample(go, clip, clip.length * (clip.isLooping ? 0.25f : 0.45f));
                var proxies = BakeProxies(go);
                var c = b.center; c.y = h * 0.45f;
                cam.transform.position = c + Quaternion.Euler(12, 215, 0) * new Vector3(0, 0, -dist) * -1f;
                cam.transform.position = c + new Vector3(Mathf.Sin(Mathf.Deg2Rad * 35) * dist, h * 0.25f, Mathf.Cos(Mathf.Deg2Rad * 35) * dist);
                cam.transform.LookAt(c);
                cam.Render(); cam.Render();
                RenderTexture.active = rt; tmp.ReadPixels(new Rect(0, 0, W, H), 0, 0); tmp.Apply(); RenderTexture.active = null;
                int x = (i % cols) * W, y = (rows - 1 - i / cols) * H;
                sheet.SetPixels(x, y, W, H, tmp.GetPixels());
                foreach (var pr in proxies) UnityEngine.Object.DestroyImmediate(pr);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
            }
            sheet.Apply();
            Directory.CreateDirectory("Documentation/CharacterTests");
            File.WriteAllBytes($"Documentation/CharacterTests/{spec.Id}_clips.png", sheet.EncodeToPNG());
            File.WriteAllText($"Documentation/CharacterTests/{spec.Id}_clips_order.txt", string.Join("\n", picks.Select((c, i) => $"{i}: {c.name}")));
            L($"Screenshots: Documentation/CharacterTests/{spec.Id}_clips.png ({picks.Count} clips, 3/4 front view, character faces +Z)");
            UnityEngine.Object.DestroyImmediate(rt);
        }
    }
}
