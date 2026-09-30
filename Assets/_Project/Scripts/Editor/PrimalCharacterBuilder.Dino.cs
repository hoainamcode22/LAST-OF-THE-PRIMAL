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

        /// <summary>
        /// ActionType ids the dinosaur controller understands (DinoActions 0-10 are the runtime constants; 11-13 were added
        /// by DINO for the PC phase, see Documentation/PCPhase/DINO_CLIPS.md). 20-23 are the flyer's.
        /// </summary>
        public static class DinoAnimIds
        {
            public const int None = 0, Eat = 1, Drink = 2, Rest = 3, LookAround = 4, Roar = 5, Call = 6, Threaten = 7, Defend = 8, Investigate = 9,
                             Charge = 10, IdleVariant = 11, Breathe = 12, Recover = 13, Fly = 20, Glide = 21, Takeoff = 22, Land = 23;
            public const int AttackLunge = 0, AttackHeavy = 1, AttackBite = 2;
            /// <summary>Turn clips play when Speed &lt; TurnMaxSpeed and |Turn| (deg/s, + = clockwise seen from above) &gt; TurnEnter</summary>
            public const float TurnEnter = 15f, TurnExit = 8f, TurnMaxSpeed = 0.3f;
        }

        /// <summary>
        /// Dinosaur Animator (Generic). Parameters kept from phase 2: Speed, ActionType, Action, Attack, AttackType, Hurt, Dead,
        /// Alert. Added for the PC phase (all optional, their defaults change nothing): Intensity (0..1, run -> Chase / Flee
        /// clip inside the Locomotion tree), Turn (signed deg/s) + TurnMul (turn clip speed), Stop (trigger). States that
        /// need their clip are skipped when the FBX does not have it, so older exports keep working.
        /// One-shots and loops start only from standing states: a lying animal gets up first (Rest_Up), then plays them.
        /// </summary>
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
            ac.AddParameter("Intensity", AnimatorControllerParameterType.Float);
            ac.AddParameter("Turn", AnimatorControllerParameterType.Float);
            ac.AddParameter(new AnimatorControllerParameter { name = "TurnMul", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            ac.AddParameter("Stop", AnimatorControllerParameterType.Trigger);
            var sm = ac.layers[0].stateMachine;
            AnimationClip C(string n) => clips.FirstOrDefault(x => x.name == n);
            float walk = MetaSpeed(meta, "Walk", 2f), run = MetaSpeed(meta, "Run", Mathf.Max(walk * 3f, 6f));

            // locomotion blend: idle / walk / run at their authored speeds (feet stay planted); the run slot is itself a
            // blend on Intensity: 0 = Run, 1 = Chase (hunters) or Flee (grazers), both authored at the run speed
            var loco = ac.CreateBlendTreeInController("Locomotion", out var tree, 0);
            tree.blendType = BlendTreeType.Simple1D; tree.blendParameter = "Speed"; tree.useAutomaticThresholds = false;
            if (C("Idle")) tree.AddChild(C("Idle"), 0f);
            if (C("Walk")) tree.AddChild(C("Walk"), walk);
            var urgent = C("Chase") ?? C("Flee");
            if (C("Run") && urgent)
            {
                var sub = tree.CreateBlendTreeChild(run);
                sub.name = "Run_Intensity"; sub.blendType = BlendTreeType.Simple1D; sub.blendParameter = "Intensity"; sub.useAutomaticThresholds = false;
                sub.AddChild(C("Run"), 0f); sub.AddChild(urgent, 1f);
            }
            else if (C("Run")) tree.AddChild(C("Run"), run);
            sm.defaultState = loco;

            var standing = new List<AnimatorState> { loco };
            AnimatorState S(string clip, string tag = null, bool stand = true)
            {
                var c = C(clip); if (c == null) return null;
                var st = sm.AddState(clip); st.motion = c; if (tag != null) st.tag = tag;
                if (stand) standing.Add(st);
                return st;
            }
            void NotDead(AnimatorStateTransition t) => t.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
            AnimatorStateTransition Any(AnimatorState to, float dur, params (string p, AnimatorConditionMode m, float v)[] conds)
            {
                var t = sm.AddAnyStateTransition(to); t.duration = dur; t.hasExitTime = false; t.canTransitionToSelf = false;
                foreach (var c in conds) t.AddCondition(c.m, c.v, c.p);
                if (to.name != "Death") NotDead(t);
                return t;
            }
            // (target, duration, conditions) started from every standing state once all states exist
            var fromStanding = new List<(AnimatorState to, float dur, (string p, AnimatorConditionMode m, float v)[] conds)>();
            void FromStanding(AnimatorState to, float dur, params (string p, AnimatorConditionMode m, float v)[] conds) { if (to != null) fromStanding.Add((to, dur, conds)); }
            void Back(AnimatorState from, float dur, float exit = 0.92f) { if (from == null) return; var t = from.AddTransition(loco); t.hasExitTime = true; t.exitTime = exit; t.duration = dur; }
            AnimatorStateTransition BackWhen(AnimatorState from, float dur, params (string p, AnimatorConditionMode m, float v)[] conds)
            {
                if (from == null) return null; var t = from.AddTransition(loco); t.hasExitTime = false; t.duration = dur;
                foreach (var c in conds) t.AddCondition(c.m, c.v, c.p);
                return t;
            }
            (string, AnimatorConditionMode, float) Is(int type) => ("ActionType", AnimatorConditionMode.Equals, type);
            (string, AnimatorConditionMode, float) IsNot(int type) => ("ActionType", AnimatorConditionMode.NotEqual, type);
            var trig = ("Action", AnimatorConditionMode.If, 0f);

            // ---- looping actions: run while ActionType stays
            foreach (var (clip, type) in new[] { ("Eat", DinoAnimIds.Eat), ("Drink", DinoAnimIds.Drink), ("Charge", DinoAnimIds.Charge), ("Breathe", DinoAnimIds.Breathe), ("Fly", DinoAnimIds.Fly), ("Glide", DinoAnimIds.Glide) })
            {
                var st = S(clip, "Action"); if (st == null) continue;
                FromStanding(st, 0.35f, trig, Is(type));
                BackWhen(st, 0.35f, IsNot(type));
            }
            // threat display: grazers with a Defend clip hold it while ActionType is Threaten or Defend; others roar once
            var defend = S("Defend", "Action");
            if (defend != null)
            {
                FromStanding(defend, 0.3f, trig, Is(DinoAnimIds.Threaten)); FromStanding(defend, 0.3f, trig, Is(DinoAnimIds.Defend));
                BackWhen(defend, 0.35f, IsNot(DinoAnimIds.Threaten), IsNot(DinoAnimIds.Defend));
            }
            // ---- one-shots
            bool restSet = C("Rest_Down") && C("Rest_Loop") && C("Rest_Up");
            var oneShots = new List<(string clip, int type)> { ("Look", DinoAnimIds.LookAround), ("Look", DinoAnimIds.Investigate), ("Roar", DinoAnimIds.Roar), ("Idle_Variation", DinoAnimIds.IdleVariant),
                                                              ("Recover", DinoAnimIds.Recover), ("Takeoff", DinoAnimIds.Takeoff), ("Land", DinoAnimIds.Land) };
            if (!restSet) oneShots.Add(("Idle_Variation", DinoAnimIds.Rest));                    // phase 2 behaviour (flyer / swimmer)
            oneShots.Add((C("Call") ? "Call" : "Roar", DinoAnimIds.Call));
            if (defend == null) oneShots.Add(("Roar", DinoAnimIds.Threaten));
            var made = new Dictionary<string, AnimatorState>();
            foreach (var (clip, type) in oneShots)
            {
                if (!made.TryGetValue(clip, out var st))
                {
                    st = S(clip, "Action"); if (st == null) continue; made[clip] = st;
                    if (clip == "Takeoff" && sm.states.Any(x => x.state.name == "Fly")) { var t = st.AddTransition(sm.states.First(x => x.state.name == "Fly").state); t.hasExitTime = true; t.exitTime = 0.9f; t.duration = 0.2f; }
                    else Back(st, 0.3f);
                    if (clip == "Recover") BackWhen(st, 0.3f, ("Speed", AnimatorConditionMode.Greater, 1.0f));
                }
                FromStanding(st, 0.25f, trig, Is(type));
            }
            // ---- rest: lie down (hind, front, head), sleep loop, fidget while lying, get up
            if (restSet)
            {
                var down = S("Rest_Down", "Rest", false); var loop = S("Rest_Loop", "Rest", false); var up = S("Rest_Up", "Rest", false);
                var shift = S("Rest_Shift", "Rest", false);
                var t0 = loco.AddTransition(down); t0.hasExitTime = false; t0.duration = 0.3f; t0.AddCondition(AnimatorConditionMode.If, 0, "Action"); t0.AddCondition(AnimatorConditionMode.Equals, DinoAnimIds.Rest, "ActionType");
                var td = down.AddTransition(loop); td.hasExitTime = true; td.exitTime = 0.98f; td.duration = 0.1f;
                BackWhen(down, 0.6f, IsNot(DinoAnimIds.Rest));                                                   // interrupted while lying down
                var bolt = loop.AddTransition(loco); bolt.hasExitTime = false; bolt.duration = 0.5f; bolt.AddCondition(AnimatorConditionMode.Greater, Mathf.Max(2f, walk * 1.3f), "Speed");
                var tu = loop.AddTransition(up); tu.hasExitTime = false; tu.duration = 0.25f; tu.AddCondition(AnimatorConditionMode.NotEqual, DinoAnimIds.Rest, "ActionType");
                if (shift != null)
                {
                    var ts = loop.AddTransition(shift); ts.hasExitTime = false; ts.duration = 0.3f; ts.AddCondition(AnimatorConditionMode.If, 0, "Action"); ts.AddCondition(AnimatorConditionMode.Equals, DinoAnimIds.Rest, "ActionType");
                    var tb = shift.AddTransition(loop); tb.hasExitTime = true; tb.exitTime = 0.95f; tb.duration = 0.3f;
                    var tsu = shift.AddTransition(up); tsu.hasExitTime = false; tsu.duration = 0.3f; tsu.AddCondition(AnimatorConditionMode.NotEqual, DinoAnimIds.Rest, "ActionType");
                }
                Back(up, 0.25f, 0.95f);
                BackWhen(up, 0.4f, ("Speed", AnimatorConditionMode.Greater, Mathf.Max(1.5f, walk * 1.3f)));   // scramble up and go (faster than a walk)
            }
            // ---- alert stance
            var alert = S("Alert", "Alert");
            if (alert != null) { var t = loco.AddTransition(alert); t.hasExitTime = false; t.duration = 0.3f; t.AddCondition(AnimatorConditionMode.If, 0, "Alert"); t.AddCondition(AnimatorConditionMode.Less, 0.3f, "Speed"); BackWhen(alert, 0.3f, ("Alert", AnimatorConditionMode.IfNot, 0)); BackWhen(alert, 0.3f, ("Speed", AnimatorConditionMode.Greater, 0.5f)); }
            // ---- turning in place (Turn: signed deg/s, + = clockwise from above; TurnMul scales the stepping)
            foreach (var (clip, sign) in new[] { ("Turn_Right", 1f), ("Turn_Left", -1f) })
            {
                var st = S(clip, "Turn"); if (st == null) continue;
                st.speedParameterActive = true; st.speedParameter = "TurnMul";
                var t = loco.AddTransition(st); t.hasExitTime = false; t.duration = 0.25f;
                t.AddCondition(sign > 0 ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less, sign * DinoAnimIds.TurnEnter, "Turn");
                t.AddCondition(AnimatorConditionMode.Less, DinoAnimIds.TurnMaxSpeed, "Speed");
                BackWhen(st, 0.3f, ("Turn", sign > 0 ? AnimatorConditionMode.Less : AnimatorConditionMode.Greater, sign * DinoAnimIds.TurnExit));
                BackWhen(st, 0.3f, ("Speed", AnimatorConditionMode.Greater, 0.6f));
            }
            // ---- stop: braking body settle
            var stop = S("Stop", "Stop");
            if (stop != null) { var t = loco.AddTransition(stop); t.hasExitTime = false; t.duration = 0.2f; t.AddCondition(AnimatorConditionMode.If, 0, "Stop"); Back(stop, 0.3f, 0.9f); BackWhen(stop, 0.3f, ("Speed", AnimatorConditionMode.Greater, 1.0f)); }
            // ---- combat (from anywhere, as in phase 2)
            var atk = S("Attack", "Attack"); var heavy = S("Heavy_Attack", "Attack"); var bite = S("Bite", "Attack");
            made.TryGetValue("Recover", out var recover);
            if (atk != null) { Any(atk, 0.12f, ("Attack", AnimatorConditionMode.If, 0), ("AttackType", AnimatorConditionMode.Equals, DinoAnimIds.AttackLunge)); Back(atk, 0.25f); }
            if (heavy != null)
            {
                Any(heavy, 0.12f, ("Attack", AnimatorConditionMode.If, 0), ("AttackType", AnimatorConditionMode.Equals, DinoAnimIds.AttackHeavy));
                if (recover != null) { var t = heavy.AddTransition(recover); t.hasExitTime = true; t.exitTime = 0.85f; t.duration = 0.2f; BackWhen(heavy, 0.25f, ("Speed", AnimatorConditionMode.Greater, 1.5f)); }
                else Back(heavy, 0.25f);
            }
            else if (atk != null) Any(atk, 0.12f, ("Attack", AnimatorConditionMode.If, 0), ("AttackType", AnimatorConditionMode.Equals, DinoAnimIds.AttackHeavy));
            var biteTarget = bite ?? atk;
            if (biteTarget != null) Any(biteTarget, 0.1f, ("Attack", AnimatorConditionMode.If, 0), ("AttackType", AnimatorConditionMode.Equals, DinoAnimIds.AttackBite));
            if (bite != null) Back(bite, 0.2f, 0.9f);
            var hurt = S("Hurt", "Hurt"); if (hurt != null) { Any(hurt, 0.08f, ("Hurt", AnimatorConditionMode.If, 0)); Back(hurt, 0.2f, 0.85f); }
            var death = S("Death", "Dead", false); if (death != null) Any(death, 0.2f, ("Dead", AnimatorConditionMode.If, 0));

            // ---- wire the standing-state starts
            int n = 0;
            foreach (var (to, dur, conds) in fromStanding)
                foreach (var from in standing)
                {
                    if (from == to) continue;
                    var t = from.AddTransition(to); t.hasExitTime = false; t.duration = dur; t.canTransitionToSelf = false;
                    foreach (var c in conds) t.AddCondition(c.m, c.v, c.p);
                    NotDead(t); n++;
                }
            EditorUtility.SetDirty(ac);
            L($"Controller: {path} (walk {walk:F2} m/s, run {run:F2} m/s, {sm.states.Length} states, {n} standing-start transitions, run slot {(urgent ? "Run / " + urgent.name + " on Intensity" : "Run")}, rest {(restSet ? "Rest_Down / Rest_Loop / Rest_Shift / Rest_Up" : "phase 2 (Idle_Variation)")})");
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
