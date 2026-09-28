using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// Read-only checks of how the player's humanoid clips come out in Unity (after retargeting), to compare with the
    /// Blender source: hand orientation (thumb / finger directions), elbow bend, per clip and time. Nothing is saved.
    /// Bridge arg: "Clip@time;Clip@time" (time in seconds), empty = a default set.
    /// </summary>
    public static class PrimalCharacterDiagnostics
    {
        const string PlayerModel = "Assets/Art/Characters/Player/Model/PLAYER_Survivor.fbx";

        [PrimalBridgeCommand]
        public static string HandPose(string arg)
        {
            var sb = new StringBuilder();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerModel);
            if (!model) return "no player model";
            var clips = AssetDatabase.LoadAllAssetsAtPath(PlayerModel).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__")).ToDictionary(c => c.name, c => c);
            var go = (GameObject)Object.Instantiate(model); go.hideFlags = HideFlags.HideAndDontSave;
            bool started = !AnimationMode.InAnimationMode(); if (started) AnimationMode.StartAnimationMode();
            try
            {
                var anim = go.GetComponent<Animator>();
                string spec = string.IsNullOrEmpty(arg) ? "Idle@0;Walk@0.23;Run@0.17;Attack_Spear@0.4;Bow_Draw@0.8" : arg;
                sb.AppendLine($"avatar human={anim && anim.isHuman}");
                foreach (var e in spec.Split(';'))
                {
                    var p = e.Split('@'); if (p.Length < 2 || !clips.TryGetValue(p[0], out var clip)) { sb.AppendLine(e + ": no clip"); continue; }
                    float t = float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture);
                    AnimationMode.BeginSampling(); AnimationMode.SampleAnimationClip(go, clip, t); AnimationMode.EndSampling();
                    foreach (bool left in new[] { true, false })
                    {
                        Transform B(HumanBodyBones b) => anim.GetBoneTransform(b);
                        var hand = B(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                        var idx = B(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
                        var lit = B(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
                        var mid = B(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
                        var ua = B(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                        var la = B(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                        Vector3 thumb = go.transform.InverseTransformDirection((idx.position - lit.position).normalized);
                        Vector3 fing = go.transform.InverseTransformDirection((mid.position - hand.position).normalized);
                        float elbow = Vector3.Angle(la.position - ua.position, hand.position - la.position);
                        sb.AppendLine(System.FormattableString.Invariant($"{p[0]}@{t} {(left ? "L" : "R")}: thumb ({thumb.x:F2},{thumb.y:F2},{thumb.z:F2}) fingers ({fing.x:F2},{fing.y:F2},{fing.z:F2}) elbow {elbow:F0}"));
                    }
                }
            }
            finally { if (started) AnimationMode.StopAnimationMode(); Object.DestroyImmediate(go); }
            return sb.ToString();
        }
    
        /// <summary>local renderer bounds of model assets (arg: asset paths separated by ';'), to check the prop axis convention</summary>
        [PrimalBridgeCommand]
        public static string ModelBounds(string arg)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var path in (arg ?? "").Split(';'))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path.Trim());
                if (!go) { sb.AppendLine("missing " + path); continue; }
                bool any = false; var b = new Bounds();
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!mf.sharedMesh) continue;
                    var m = go.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    var mb = mf.sharedMesh.bounds;
                    for (int k = 0; k < 8; k++)
                    {
                        var c = mb.center + Vector3.Scale(mb.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                        var w = m.MultiplyPoint3x4(c);
                        if (!any) { b = new Bounds(w, Vector3.zero); any = true; } else b.Encapsulate(w);
                    }
                }
                sb.AppendLine(System.FormattableString.Invariant($"{go.name}: min ({b.min.x:F3},{b.min.y:F3},{b.min.z:F3}) max ({b.max.x:F3},{b.max.y:F3},{b.max.z:F3})"));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Review renders of the player holding items in sampled clip poses, with the same grip frame and grip offsets
        /// the game uses (PlayerHierarchy.TryComputeGrip + WeaponData / item grip). arg: "clip@seconds:item[;...]"
        /// (item ids from Data/Items, e.g. flint_sword, stone_spear, bow). Output: Documentation/Screenshots/Review/held_*.png
        /// </summary>
        [PrimalBridgeCommand]
        public static string HeldWeapons(string arg)
        {
            var sb = new StringBuilder();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Characters/Player/Prefab/PFB_Player_Survivor.prefab");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerModel);
            if (!prefab || !model) return "player prefab / model missing";
            var clips = AssetDatabase.LoadAllAssetsAtPath(PlayerModel).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__")).ToDictionary(c => c.name, c => c);
            string dir = "Documentation/Screenshots/Review"; System.IO.Directory.CreateDirectory(dir);
            var pru = new PreviewRenderUtility();
            bool started = !AnimationMode.InAnimationMode(); if (started) AnimationMode.StartAnimationMode();
            try
            {
                pru.camera.fieldOfView = 30f; pru.camera.nearClipPlane = 0.05f; pru.camera.farClipPlane = 50f;
                pru.camera.clearFlags = CameraClearFlags.SolidColor; pru.camera.backgroundColor = new Color(0.42f, 0.44f, 0.47f);
                pru.lights[0].intensity = 1.2f; pru.lights[0].transform.rotation = Quaternion.Euler(40f, -30f, 0f);
                pru.lights[1].intensity = 0.6f; pru.lights[1].transform.rotation = Quaternion.Euler(20f, 150f, 0f);
                pru.ambientColor = new Color(0.35f, 0.35f, 0.38f);
                pru.BeginStaticPreview(new Rect(0, 0, 64, 64)); pru.camera.Render(); Object.DestroyImmediate(pru.EndStaticPreview());   // warm-up
                foreach (var e in (arg ?? "").Split(';'))
                {
                    var parts = e.Split(':'); var cp = parts[0].Split('@');
                    if (parts.Length < 2 || cp.Length < 2 || !clips.TryGetValue(cp[0], out var clip)) { sb.AppendLine(e + ": bad spec / no clip"); continue; }
                    float t = float.Parse(cp[1], System.Globalization.CultureInfo.InvariantCulture);
                    var item = AssetDatabase.LoadAssetAtPath<Items.ItemDefinition>($"Assets/_Project/Data/Items/ITEM_{parts[1]}.asset");
                    var go = (GameObject)Object.Instantiate(prefab);
                    foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;
                    pru.AddSingleGO(go);
                    var anim = go.GetComponentInChildren<Animator>();
                    AnimationMode.BeginSampling(); AnimationMode.SampleAnimationClip(anim.gameObject, clip, t); AnimationMode.EndSampling();
                    if (item && item.handPrefab)
                    {
                        var wd = item.weaponData;
                        bool left = wd ? wd.kind == Items.WeaponKind.Bow : item.weapon == Items.WeaponKind.Bow;
                        var hand = anim.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                        Player.PlayerHierarchy.TryComputeGrip(anim, left, out var gp, out var gr);
                        var sock = new GameObject("Socket").transform; sock.SetParent(hand, false); sock.localPosition = gp; sock.localRotation = gr;
                        var held = (GameObject)Object.Instantiate(item.handPrefab, sock);
                        held.transform.localRotation = Quaternion.Euler(wd ? wd.gripEuler : item.gripEuler);
                        held.transform.localPosition = wd ? wd.gripPosition : item.gripPosition;
                        if (wd && wd.offHandGrip != Vector3.zero)
                        {
                            var og = held.transform.TransformPoint(wd.offHandGrip);
                            var lh = anim.GetBoneTransform(HumanBodyBones.LeftHand);
                            sb.AppendLine(System.FormattableString.Invariant($"{e}: off-hand grip to left hand {(og - lh.position).magnitude:F3} m"));
                        }
                    }
                    else sb.AppendLine(e + ": item or hand model missing");
                    var b = new Bounds(go.transform.position + Vector3.up * 1.1f, Vector3.one);
                    foreach (var (tag, yaw) in new[] { ("front", 200f), ("side", 110f) })
                    {
                        var rot = Quaternion.Euler(8f, yaw, 0f);
                        pru.camera.transform.rotation = rot;
                        pru.camera.transform.position = b.center - rot * Vector3.forward * 4.6f;
                        pru.BeginStaticPreview(new Rect(0, 0, 480, 560));
                        pru.camera.Render();
                        var tex = pru.EndStaticPreview();
                        var file = System.FormattableString.Invariant($"{dir}/held_{cp[0]}_{(int)(t * 30f):00}_{parts[1]}_{tag}.png");
                        System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
                        Object.DestroyImmediate(tex);
                        sb.AppendLine(file);
                    }
                    Object.DestroyImmediate(go);
                }
            }
            finally { if (started) AnimationMode.StopAnimationMode(); pru.Cleanup(); }
            return sb.ToString();
        }

        /// <summary>compile state of shaders (arg: shader names separated by ';'): errors / warnings from ShaderUtil</summary>
        [PrimalBridgeCommand]
        public static string ShaderCheck(string arg)
        {
            var sb = new StringBuilder();
            foreach (var n in (arg ?? "").Split(';'))
            {
                var sh = Shader.Find(n.Trim());
                if (!sh) { sb.AppendLine(n + ": NOT FOUND"); continue; }
                var msgs = ShaderUtil.GetShaderMessages(sh);
                int err = msgs.Count(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error);
                sb.AppendLine($"{n}: supported={sh.isSupported} hasError={ShaderUtil.ShaderHasError(sh)} errors={err} messages={msgs.Length} passes={sh.passCount}");
                foreach (var m in msgs.Take(6)) sb.AppendLine($"   {m.severity} {m.message} (line {m.line}) {m.platform}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Edit-mode preview of the night sky (the NightSky dome is built at run time, and the sky exposure is set by
        /// TimeManager at run time): sets the hour, dims the skybox like TimeManager does at night, draws a temporary star
        /// dome with M_NightSky and renders from the player spawn. arg: "hour:pitch:yaw:fov" (default 23:-28:-40:70).
        /// Everything is restored afterwards. Output Documentation/Screenshots/Review/night_stars.png
        /// </summary>
        [PrimalBridgeCommand]
        public static string NightPreview(string arg)
        {
            var a = (string.IsNullOrEmpty(arg) ? "23:-28:-40:70" : arg).Split(':');
            float P(int k, float d) => a.Length > k ? float.Parse(a[k], System.Globalization.CultureInfo.InvariantCulture) : d;
            float hour = P(0, 23f), pitch = P(1, -28f), yaw = P(2, -40f), fov = P(3, 70f);
            var tm = Object.FindFirstObjectByType<Core.TimeManager>();
            var spawn = GameObject.Find("ZONE_PlayerSpawn");
            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/M_NightSky.mat");
            if (!tm || !spawn || !mat) return "missing TimeManager / ZONE_PlayerSpawn / M_NightSky";
            float oldHour = tm.hour; var sky = RenderSettings.skybox; float oldExp = sky && sky.HasProperty("_Exposure") ? sky.GetFloat("_Exposure") : -1f;
            var dome = new GameObject("_NightPreviewDome") { hideFlags = HideFlags.HideAndDontSave };
            var camGo = new GameObject("_NightPreviewCam") { hideFlags = HideFlags.HideAndDontSave };
            var mesh = VFX.NightSky.BuildDome(32, 16);
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            bool asyncWas = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
            try
            {
                tm.hour = hour; tm.Apply();
                if (oldExp >= 0f && tm.NightFactor > 0.5f) sky.SetFloat("_Exposure", 0.12f);
                Shader.SetGlobalFloat("_PF_StarRotation", 0.8f);
                if (tm.sun) { var f = -tm.sun.transform.forward; Shader.SetGlobalVector("_PF_MoonDir", new Vector4(f.x, f.y, f.z, 1f)); }
                var cam = camGo.AddComponent<Camera>(); if (Camera.main) cam.CopyFrom(Camera.main);
                cam.enabled = false; cam.fieldOfView = fov; cam.farClipPlane = 1500f;
                cam.transform.SetPositionAndRotation(spawn.transform.position + Vector3.up * 1.8f, Quaternion.Euler(pitch, yaw, 0f));
                dome.transform.position = cam.transform.position;
                dome.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = dome.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                cam.targetTexture = rt; cam.Render(); cam.Render();
                var prev = RenderTexture.active; RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply(); RenderTexture.active = prev; cam.targetTexture = null;
                System.IO.Directory.CreateDirectory("Documentation/Screenshots/Review");
                System.IO.File.WriteAllBytes("Documentation/Screenshots/Review/night_stars.png", tex.EncodeToPNG());
                return System.FormattableString.Invariant($"night_stars.png: hour {hour:F1}, night factor {tm.NightFactor:F2}, sky exposure {(oldExp >= 0f ? sky.GetFloat("_Exposure") : -1f):F2}");
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = asyncWas;
                tm.hour = oldHour; tm.Apply(); if (oldExp >= 0f) sky.SetFloat("_Exposure", oldExp);
                Object.DestroyImmediate(dome); Object.DestroyImmediate(camGo); Object.DestroyImmediate(mesh);
                rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            }
        }

        /// <summary>
        /// Edit-mode preview of the volcano ash fall + heat shimmer (both are built by VolcanoLandmark.Start at run time):
        /// builds them temporarily, simulates, renders from the player spawn towards the crater, then deletes them.
        /// arg: "hour:fov" (default 15:40). Output Documentation/Screenshots/Review/volcano_fx.png
        /// </summary>
        [PrimalBridgeCommand]
        public static string VolcanoFxPreview(string arg)
        {
            var a = (string.IsNullOrEmpty(arg) ? "15:40" : arg).Split(':');
            float hour = float.Parse(a[0], System.Globalization.CultureInfo.InvariantCulture), fov = a.Length > 1 ? float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture) : 40f;
            var vl = Object.FindFirstObjectByType<World.VolcanoLandmark>();
            var spawn = GameObject.Find("ZONE_PlayerSpawn");
            var tm = Object.FindFirstObjectByType<Core.TimeManager>();
            if (!vl || !spawn) return "missing VolcanoLandmark / ZONE_PlayerSpawn";
            var t = typeof(World.VolcanoLandmark);
            var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            float oldHour = tm ? tm.hour : 0f;
            var camGo = new GameObject("_VolcanoFxCam") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var made = new System.Collections.Generic.List<GameObject>();
            var sb = new StringBuilder();
            bool asyncWas = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
            try
            {
                if (tm) { tm.hour = hour; tm.Apply(); }
                Vector3 eye = spawn.transform.position + Vector3.up * 1.7f;
                var cam = camGo.AddComponent<Camera>(); if (Camera.main) cam.CopyFrom(Camera.main);
                cam.enabled = false; cam.fieldOfView = fov; cam.farClipPlane = 2000f;
                Vector3 crater = vl.smoke ? vl.smoke.transform.position : vl.transform.position + Vector3.up * 200f;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation((crater + Vector3.up * 30f - eye).normalized));
                var before = new System.Collections.Generic.HashSet<Transform>(vl.GetComponentsInChildren<Transform>(true));
                if (vl.ashFall) t.GetMethod("BuildAsh", bf)?.Invoke(vl, null);
                if (vl.heatHaze && vl.hazeMaterial) t.GetMethod("BuildHaze", bf)?.Invoke(vl, null);
                foreach (var tr in vl.GetComponentsInChildren<Transform>(true)) if (!before.Contains(tr) && tr.parent == vl.transform) made.Add(tr.gameObject);
                foreach (var g in made)
                {
                    var ps = g.GetComponent<ParticleSystem>(); if (!ps) continue;
                    if (g.name == "Ash_Fall")
                    {
                        g.transform.position = eye;
                        var em = ps.emission; em.rateOverTime = vl.ashRate;
                        ps.Simulate(12f, true, true, false);
                    }
                    else ps.Simulate(0.1f, true, false, false);
                    sb.AppendLine($"{g.name}: {ps.particleCount} particles");
                }
                foreach (var ps in new[] { vl.smoke, vl.embers, vl.glow }) if (ps) ps.Simulate(45f, true, true, false);
                cam.targetTexture = rt; cam.Render(); cam.Render();
                var prev = RenderTexture.active; RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply(); RenderTexture.active = prev; cam.targetTexture = null;
                System.IO.Directory.CreateDirectory("Documentation/Screenshots/Review");
                System.IO.File.WriteAllBytes("Documentation/Screenshots/Review/volcano_fx.png", tex.EncodeToPNG());
                sb.AppendLine("volcano_fx.png");
                return sb.ToString();
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = asyncWas;
                foreach (var g in made) if (g) Object.DestroyImmediate(g);
                foreach (var ps in new[] { vl.smoke, vl.embers, vl.glow }) if (ps) ps.Clear(false);
                if (tm) { tm.hour = oldHour; tm.Apply(); }
                Object.DestroyImmediate(camGo); rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            }
        }
}
}
