# CHARACTER RIG AUDIT (player survivor), 2026-09-28

Audit only: nothing in the character, the rig, the clips or the Unity setup was changed.

Detail reports behind this summary:
- `_video_findings.md`: the owner's recording (V1-V9).
- `_blender_rig_anim_audit.md`: bones, axes, constraints and per-frame clip measurements (Blender agent).
- `_unity_integration_audit.md`: avatar, Animator, IK, drivers, PlayMode probe with CSV data (Unity agent).
- `CHARACTER_TOPOLOGY_AUDIT.md`, `CHARACTER_WEIGHT_AUDIT.md`: mesh and skin.
- `_motion_principles.md`: sourced human-motion targets used to judge the clips.

## 1. The seven questions

| # | Question | Answer | Evidence |
|---|---|---|---|
| 1 | What is actually wrong? | Two big faults and several smaller ones, ranked in section 2. The biggest: the **Unity humanoid avatar has a broken T-pose on the left arm**, so the left arm flips and the left fist spins. Next: the **run / sprint clips carry the hands too high and keep the shoulders still**, and the **gather action has no stand-to-squat clip**, so a 0.2 s crossfade does the whole drop. | probe CSVs, 240 Hz clip sampling, renders `_cmp_tpose.png`, `_cmp_runflip_L.png`, `aud_sheet_run_cycle.png` |
| 2 | Is topology the problem? | **No.** LOD0 60,236 verts / 72,952 tris, 98.2 % quads, no n-gons or triangles at joints, 3-5 edge loops across every bend (elbow 5, wrist / knee / ankle 3, shoulder and hip 8-12 per limb diameter). | `CHARACTER_TOPOLOGY_AUDIT.md` |
| 3 | Are weights the problem? | **No.** Max 4 influences, sums exactly 1.0, 0 unweighted verts, smooth gradients, the forearm twist split works (90 deg hand twist becomes a 0-76 deg ramp, wrist keeps 87 % of its section). Volume loss at 130 deg elbow bend (41 % section) is normal for linear skinning. | `CHARACTER_WEIGHT_AUDIT.md`, `aud_sheet_arm_deform.png`, `aud_sheet_wrist.png` |
| 4 | Is bone orientation the problem? | **Not in Blender:** axes clean, elbow and knee hinges are exactly local X, L/R exact mirrors (0.00 mm / 0.00 deg). **Yes in Unity:** the avatar T-pose was built from the FBX node transforms, which are a posed, asymmetric frame (Hand_L 164 deg off the bind pose), and it points the left arm through the chest. | `_blender_rig_anim_audit.md` 2.3, `_unity_integration_audit.md` R1 |
| 5 | Is IK the problem? | **Minor.** Hand IK and elbow hints are at weight 0 when unarmed (under 1 deg effect). Two real IK side effects: the pelvis drops 0.21-0.25 m when the capsule steps up onto an obstacle (pelvis follows the planted foot at full weight while the feet only get 0.35), and the look-at IK twists the chest up to +-33 deg against the hips during turns. | probe passes B (IK on) vs C (IK off) |
| 6 | Is animation the problem? | **Yes, partly.** Run / sprint arm arc (wrist reaches shoulder height), shoulders almost fixed in the world (a clavicle term cancels the chest counter-rotation), run vertical bounce inverted, gather loop has a hold then a 643 deg/s snap, no enter / exit clips for gather, wrist flips in some weapon clips, FK/IK switch pops in Get_Up / Wake_Up / Death / Climb_End. Timing in Unity adds: gather crossfade 0.2 s, exit while the motor spins the body at 500 deg/s, focus turn 540 deg/s, Idle time-synced with Walk. | section 2 |
| 7 | What can be repaired without restarting? | **Everything.** No remodel, no re-rig, no re-weight. The avatar fix is one builder change and a re-import. Clip fixes are parameter changes in the generator scripts plus a rebake / export. Timing fixes are Unity code values. New clips (gather enter / exit, starts / stops, pivots, unarmed set) are added with the same generators. | `CHARACTER_ANIMATION_PLAN.md` |

## 2. Root causes, ranked

| Rank | Cause | Layer | Video | Measured |
|---|---|---|---|---|
| R1 | Avatar T-pose from posed node transforms; left arm target picked with `Mathf.Sign(dir.x)` of that posed frame | Unity import (`PrimalCharacterBuilder.ConfigureImporter`) | **V2**, worsens V1 / V6 / V7 | left UpperArm / LowerArm flip ~177 deg twice per run and walk cycle (240 Hz); left fist spins 350-360 deg in every crossfade into / out of gather (right 4-11 deg); left wrist twisted -83 deg against the bind pose all the time (right -23); same symmetric Idle pose encodes Arm Twist -1.95 (L) vs +0.70 (R) |
| R2 | Run / sprint arm arc: elbow bends more on the forward swing, big arm amplitude | Blender clip (`pf_clips_human.gait_fn`) | **V1** | Run wrist up to +0.007 m above the shoulder joint (sprint +0.072), hand path 38 cm vertical vs 21 cm fore-aft, elbow 88-108 deg; abduction only 7-13 deg (elbows are not flared at the joint; the bulky left shoulder in the video fits R1) |
| R3 | Shoulders do not counter-rotate | Blender clip (clavicle term `0.12*arm_amp*s0` cancels chest yaw) | V1 "rigid torso" | shoulder line +-0.8 deg in the world while the pelvis yaws +-11 deg (target: pelvis +-5-8, shoulders opposite +-10-12) |
| R4 | Run bounce inverted | Blender clip (leg-reach clamp overrides the bounce) | "robotic" feel | pelvis highest at mid / late stance (0.973 m), lowest before touchdown (0.887 m); a runner is lowest at mid-stance |
| R5 | Gather has no enter / exit clip; 0.2 s / 0.3 s linear crossfades; movement comes back on the 2nd frame of the exit | Blender content gap + Unity timing (`PlayerAnimationDriver.IsBusy`) | **V4, V7** | hips 0.95 to 0.57 m in 0.20 s; on exit the body spins 154 deg at up to 530 deg/s during the blend |
| R6 | Gather_Plant loop: toss raises the right arm to 52 deg, frames 12-17 frozen then 643 deg/s | Blender clip | **V6** | right fist rolls 47 deg per loop |
| R7 | Frame hitch at the first gather hit (136-308 ms) + focus turn at 540 deg/s fighting the motor | Unity (`PlayerInteraction`, first hit handler) | **V5** | yaw constant after 0.4 s; the "snap" is a frozen frame followed by a jump |
| R8 | Turning: 600 deg/s turn speed, reversal brakes to zero then spins 180 deg in 0.38 s; turn-in-place never runs while moving; look IK twists the chest | Unity (`PlayerMotor`, `PlayerIK`) | **V3** | leg cycle stretches to 3.7 s during the spin, feet slide |
| R9 | Idle (6 s loop) time-synced with Walk in one 1D blend tree | Unity (controller) | **V8**, every start / stop | at Speed 0.73 the step cycle is 3.3 s (0.3x rate) |
| R10 | Pelvis IK drop at full weight on step-ups | Unity (`PlayerIK`) | run "khựng" | hip dips 0.21-0.25 m |
| R11 | Weapon clip wrist flips; FK/IK switch pops | Blender clips | not in this video | Throw_Spear R 164 deg in one frame, Sword_Equip 108; Get_Up / Wake_Up / Death / Climb_End 2,450-3,560 deg/s |
| R12 | Twist constraint copies Euler Y, not a true twist | Blender rig | weapon clips only | share drops to 0.19-0.6 when wrist flex is large |

V9 (camera through a tree) is a camera issue, outside this task.

## 3. Bone architecture vs the spec

No bone was renamed or added. Unity uses the humanoid slots, so names only matter for our own scripts.

| Spec | Existing | Humanoid slot |
|---|---|---|
| Root | Root (non-deform) | none |
| Pelvis | Pelvis | Hips |
| Spine / Spine_01 / Spine_02 / Chest | Spine / Spine_Upper / (none) / Chest | Spine / Chest / - / UpperChest |
| Neck / Head | Neck / Head | Neck / Head |
| Left/RightClavicle | Clavicle_L/R | Left/RightShoulder |
| Upper / Lower arm, Hand | UpperArm_*, LowerArm_*, Hand_* | arm slots |
| Upper / Lower leg, Foot, Toe | Thigh_*, Calf_*, Foot_*, Toe_* | leg slots |
| Left/RightForearmTwist | LowerArmTwist_L/R (child of LowerArm, driven by `TwistFromHand` in Blender and by `TwistBoneDriver` in Unity) | none (not a humanoid bone) |
| Fingers | full finger chains | finger slots |

57 bones, 54 deform (Root, Weapon_L, Weapon_R are not deform). Three spine segments above the pelvis are enough for this character; a
fourth (Spine_02) is not needed. Rest pose is an A-pose (upper arm 41.8 deg from vertical).

## 4. Unity integration settings found

- Avatar: armTwist 0.5, foreArmTwist 0 (twist bones found), legTwist 0.5, arm / leg stretch 0.05, feet spacing 0, no translation DoF, default muscle limits. T-pose broken (R1).
- Clips: 58, all in place (bake into pose for rotation, Y and XZ), 23 loops. Root motion off on the Animator: the capsule motor moves the character. Import warning: Thigh scale curves are dropped (harmless).
- Animator: Base (IK pass on), UpperBody override (mask, default Empty), HitReaction additive; blend trees Locomotion (Idle / Walk / Run / Sprint on Speed), StrafeLocomotion (2D, 9 clips), Crouch, TurnInPlace; actions entered in 0.2 s, left in 0.3 s.
- Runtime: turn 600 deg/s, focus turn 540 deg/s, foot IK 0.35 at run, look IK body 0.15, TwistBoneDriver share 0.6 (math verified, no flips of its own).

## 5. Diagnostics added (kept, off by default)

- `Tests/PlayMode/CharacterMotionProbe.cs` (tests A-E, skipped unless `Library/PrimalBridge/probe_on.txt` says "on").
- `Scripts/Editor/PrimalAnimAudit.cs` (bridge `PrimalAnimAudit.RenderArmTwist`, writes PNGs only).
- Blender measurement scripts `aud_*.py` in `E:\Model game khủng long\renders\characters\audit\_work\` (run on a copy of the .blend in background Blender; the owner's file was never saved).
