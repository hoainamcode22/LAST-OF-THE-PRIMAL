using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PrimalFrontier.Animation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace PrimalFrontier.EditorTools
{
    public static partial class PrimalCharacterBuilder
    {
        public const string DinoRoot = "Assets/Art/Characters/Dinosaurs";

        /// <summary>dinosaur specs are discovered from the folders the Blender pipeline exports</summary>
        public static Spec GetSpec(string id)
        {
            if (Specs.TryGetValue(id, out var s)) return s;
            string folder = $"{DinoRoot}/{id}";
            if (!AssetDatabase.IsValidFolder(folder) && !Directory.Exists(folder)) throw new Exception("unknown character " + id);
            s = new Spec { Id = id, Folder = folder, Fbx = "DINO_" + id, Humanoid = false, Prefab = "DINO_" + id, ExpectedHeight = 0f };
            Specs[id] = s; return s;
        }

        public static string[] DinoIds() => Directory.Exists(DinoRoot) ? Directory.GetDirectories(DinoRoot).Select(Path.GetFileName).Where(n => File.Exists($"{DinoRoot}/{n}/Model/DINO_{n}.fbx")).OrderBy(n => n).ToArray() : new string[0];

        /// <summary>batch: -executeMethod PrimalFrontier.EditorTools.PrimalCharacterBuilder.BuildAllDinosFromCommandLine</summary>
        public static void BuildAllDinosFromCommandLine()
        {
            int code = 0; var summary = new List<string>();
            try
            {
                PrimalAudioBuilder.Build();                                    // picks up new SFX (distant roar)
                foreach (var id in DinoIds())
                {
                    bool ok = false;
                    try { ok = BuildAndTest(id); } catch (Exception e) { Debug.LogError($"[PrimalCharacterBuilder] {id}: {e}"); }
                    summary.Add($"{id}: {(ok ? "PASS" : "FAIL")}"); if (!ok) code = 2;
                }
                PrimalDinoBuilder.Build();
            }
            catch (Exception e) { Debug.LogError("[PrimalCharacterBuilder] " + e); code = 1; }
            Debug.Log("[PrimalCharacterBuilder] DINOS " + string.Join(" | ", summary));
            File.WriteAllText("Documentation/CharacterTests/Dinosaurs_summary.md", "# Dinosaur build + test summary\n\n" + string.Join("\n", summary.Select(s => "- " + s)) + "\n");
            EditorApplication.Exit(code);
        }

        static float MetaSpeed(AnimMeta meta, string clip, float fallback)
        {
            var c = meta?.clips?.FirstOrDefault(x => x.name == clip);
            return c != null && c.speed > 0.01f ? c.speed : fallback;
        }

        static AnimatorController BuildDinoController(Spec spec, List<AnimationClip> clips, AnimMeta meta)
        {
            string path = $"{spec.Folder}/Animations/{spec.Id}Animator.controller";
            Directory.CreateDirectory($"{spec.Folder}/Animations");
            var ac = NewController(path);
            ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ac.AddParameter("ActionType", AnimatorControllerParameterType.Int);
            ac.AddParameter("Action", AnimatorControllerParameterType.Trigger);
            ac.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            ac.AddParameter("AttackType", AnimatorControllerParameterType.Int);
            ac.AddParameter("Hurt", AnimatorControllerParameterType.Trigger);
            ac.AddParameter("Dead", AnimatorControllerParameterType.Bool);
            ac.AddParameter("Alert", AnimatorControllerParameterType.Bool);
            var sm = ac.layers[0].stateMachine;
            AnimationClip C(string n) => clips.FirstOrDefault(x => x.name == n);
            float walk = MetaSpeed(meta, "Walk", 2f), run = MetaSpeed(meta, "Run", Mathf.Max(walk * 3f, 6f));

            // locomotion blend: idle / walk / run at their authored speeds (feet stay planted)
            var loco = ac.CreateBlendTreeInController("Locomotion", out var tree, 0);
            tree.blendType = BlendTreeType.Simple1D; tree.blendParameter = "Speed"; tree.useAutomaticThresholds = false;
            if (C("Idle")) tree.AddChild(C("Idle"), 0f);
            if (C("Walk")) tree.AddChild(C("Walk"), walk);
            if (C("Run")) tree.AddChild(C("Run"), run);
            sm.defaultState = loco;

            AnimatorState S(string clip, string tag = null)
            {
                var c = C(clip); if (c == null) return null;
                var st = sm.AddState(clip); st.motion = c; if (tag != null) st.tag = tag; return st;
            }
            AnimatorStateTransition Any(AnimatorState to, float dur, params (string p, AnimatorConditionMode m, float v)[] conds)
            {
                var t = sm.AddAnyStateTransition(to); t.duration = dur; t.hasExitTime = false; t.canTransitionToSelf = false;
                foreach (var c in conds) t.AddCondition(c.m, c.v, c.p);
                if (to.name != "Death") t.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
                return t;
            }
            void Back(AnimatorState from, float dur, float exit = 0.92f) { if (from == null) return; var t = from.AddTransition(loco); t.hasExitTime = true; t.exitTime = exit; t.duration = dur; }
            void BackWhen(AnimatorState from, string p, AnimatorConditionMode m, float v, float dur) { if (from == null) return; var t = from.AddTransition(loco); t.hasExitTime = false; t.duration = dur; t.AddCondition(m, v, p); }

            // looping actions (ActionType) - leave when the AI changes ActionType
            foreach (var (clip, type) in new[] { ("Eat", DinoActions.Eat), ("Drink", DinoActions.Drink), ("Charge", 10), ("Fly", 20), ("Glide", 21) })
            {
                var st = S(clip, "Action"); if (st == null) continue;
                Any(st, 0.35f, ("Action", AnimatorConditionMode.If, 0), ("ActionType", AnimatorConditionMode.Equals, type));
                BackWhen(st, "ActionType", AnimatorConditionMode.NotEqual, type, 0.35f);
            }
            // one-shots
            foreach (var (clip, type) in new[] { ("Idle_Variation", DinoActions.Rest), ("Look", DinoActions.LookAround), ("Roar", DinoActions.Roar), ("Takeoff", 22), ("Land", 23) })
            {
                var st = S(clip, "Action"); if (st == null) continue;
                Any(st, 0.25f, ("Action", AnimatorConditionMode.If, 0), ("ActionType", AnimatorConditionMode.Equals, type));
                if (clip == "Takeoff" && sm.states.Any(x => x.state.name == "Fly")) { var t = st.AddTransition(sm.states.First(x => x.state.name == "Fly").state); t.hasExitTime = true; t.exitTime = 0.9f; t.duration = 0.2f; }
                else Back(st, 0.3f);
            }
            if (C("Roar")) { var call = sm.states.First(x => x.state.name == "Roar").state; Any(call, 0.25f, ("Action", AnimatorConditionMode.If, 0), ("ActionType", AnimatorConditionMode.Equals, DinoActions.Call)); Any(call, 0.25f, ("Action", AnimatorConditionMode.If, 0), ("ActionType", AnimatorConditionMode.Equals, DinoActions.Threaten)); }
            // alert stance
            var alert = S("Alert", "Alert");
            if (alert != null) { var t = loco.AddTransition(alert); t.hasExitTime = false; t.duration = 0.3f; t.AddCondition(AnimatorConditionMode.If, 0, "Alert"); t.AddCondition(AnimatorConditionMode.Less, 0.3f, "Speed"); BackWhen(alert, "Alert", AnimatorConditionMode.IfNot, 0, 0.3f); var t2 = alert.AddTransition(loco); t2.hasExitTime = false; t2.duration = 0.3f; t2.AddCondition(AnimatorConditionMode.Greater, 0.5f, "Speed"); }
            // combat
            var atk = S("Attack", "Attack"); var heavy = S("Heavy_Attack", "Attack");
            if (atk != null) { Any(atk, 0.12f, ("Attack", AnimatorConditionMode.If, 0), ("AttackType", AnimatorConditionMode.Equals, 0)); Back(atk, 0.25f); }
            if (heavy != null) { Any(heavy, 0.12f, ("Attack", AnimatorConditionMode.If, 0), ("AttackType", AnimatorConditionMode.Equals, 1)); Back(heavy, 0.25f); }
            else if (atk != null) Any(atk, 0.12f, ("Attack", AnimatorConditionMode.If, 0), ("AttackType", AnimatorConditionMode.Equals, 1));
            var hurt = S("Hurt", "Hurt"); if (hurt != null) { Any(hurt, 0.08f, ("Hurt", AnimatorConditionMode.If, 0)); Back(hurt, 0.2f, 0.85f); }
            var death = S("Death", "Dead"); if (death != null) Any(death, 0.2f, ("Dead", AnimatorConditionMode.If, 0));
            EditorUtility.SetDirty(ac);
            L($"Controller: {path} (walk {walk:F2} m/s, run {run:F2} m/s, {sm.states.Length} states)");
            return ac;
        }

        static void AddDinoColliders(GameObject go, Bounds b)
        {
            foreach (var c in go.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(c);
            var local = go.transform.InverseTransformPoint(b.center);
            var cap = go.AddComponent<CapsuleCollider>();
            float w = b.size.x, h = b.size.y, l = b.size.z;
            cap.direction = 2; cap.radius = Mathf.Max(0.15f, Mathf.Min(w, h) * 0.32f); cap.height = Mathf.Max(cap.radius * 2f, l * 0.72f);
            cap.center = new Vector3(0, Mathf.Max(cap.radius, local.y * 1.05f), local.z * 0.6f);
            var hz = go.GetOrAdd<HitZone>(); hz.Configure(HitZoneType.Body, 1f);
            var rb = go.GetComponent<Rigidbody>(); if (rb == null) rb = go.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
            var head = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Head");
            var jaw = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Jaw");
            if (head != null)
            {
                var old = head.Find("HitZone_Head"); if (old) UnityEngine.Object.DestroyImmediate(old.gameObject);
                var hgo = new GameObject("HitZone_Head"); hgo.transform.SetParent(head, false);
                float r = jaw ? Mathf.Max(0.08f, Vector3.Distance(head.position, jaw.position) * 1.1f) : Mathf.Max(0.1f, h * 0.12f);
                var sc = hgo.AddComponent<SphereCollider>(); sc.radius = r / Mathf.Max(0.0001f, head.lossyScale.x);
                hgo.transform.position = jaw ? Vector3.Lerp(head.position, jaw.position, 0.5f) : head.position;
                hgo.AddComponent<HitZone>().Configure(HitZoneType.Head, 1.6f);
            }
        }

        static void TestDinoColliders(GameObject go, Bounds b)
        {
            var cap = go.GetComponent<CapsuleCollider>();
            if (cap == null) F("no body collider"); else L($"Collider: capsule r={cap.radius:F2} h={cap.height:F2} (mesh {b.size})");
            if (!go.GetComponentsInChildren<HitZone>(true).Any(h => h.Zone == HitZoneType.Head)) F("no head hit zone");
            var head = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Head");
            var pelvis = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Pelvis");
            if (head && pelvis)
            {
                var d = go.transform.InverseTransformPoint(head.position) - go.transform.InverseTransformPoint(pelvis.position);
                L($"Facing: pelvis -> head {d.normalized} (expected +Z)");
                if (d.z < 0.2f * d.magnitude) F("dinosaur does not face +Z");
            }
        }
    }
}
