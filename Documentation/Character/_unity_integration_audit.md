# Unity animation integration audit (player), 2026-09-28

Read-only audit of what Unity adds to the player's animation (avatar, Animator, IK, runtime drivers), with measurements.
Nothing was fixed. Evidence comes from a new PlayMode probe (`CharacterMotionProbe`, 5 tests) and a new editor render command (`PrimalAnimAudit.RenderArmTwist`), run on the owner's PC.

**Short answer.** The single biggest Unity-side fault is the **humanoid avatar**. Its T-pose was built from the FBX's node transforms, and those transforms are a posed, asymmetric frame, not the rest pose. In that T-pose the **left arm points across the chest**. As a result:
- the left arm's muscle encoding sits outside its limits and wraps at 180 deg;
- the left upper arm and forearm bones flip about 177 deg twice per walk or run cycle;
- the left fist spins about 360 deg in every crossfade into or out of gathering;
- the left wrist is twisted -83 deg relative to the skin bind pose all the time.

This is V2 ("the arms keep twisting"), and it is Unity-only. The arm *joint positions* (V1: hands at shoulder height, elbows bent about 100 deg) come from the clip. Unity's controller, IK and TwistBoneDriver change them by less than 1 deg. V3, V4, V5 and V7 are mostly timing and turn-rate choices in Unity code, plus one frame hitch.

## 1. What was measured and how

| Test / tool | What it does | Output (PC, `E:\LAST OF THE PRIMAL\`) |
|---|---|---|
| `A_Clips_And_Controller` | Samples raw clips at 30 Hz on a bare model: retargeted humanoid pose, PlayableGraph, no foot IK, no IK, no scripts. Records muscle ranges. Runs the real controller alone (no motor, IK or driver): run, with and without layers 1/2; walk; half-walk; gather in and out. | `Documentation/Tests/motion_probe_clips.csv`, `motion_probe_controller.csv`, `motion_probe_muscles.csv` |
| `B_Gameplay_AllOn` | Island, simulated input: idle, walk 2 s, run 2.5 s, 90 deg turn, 180 deg reversal, stop. Then gather 1 (run up to the Driftwood node, press at 1.7 m, gather 4.5 s, cancel by moving away) and gather 2 (standing 40 deg off the focus, gather 3 s, cancel with E). | `motion_probe_gameplay_allon.csv` (rerun with ground columns) |
| `C_Gameplay_IKOff` | Same scenario with PlayerIK suspended (feet, look, hands, lean, pelvis all 0). | `motion_probe_gameplay_ikoff.csv` |
| `D_Gameplay_TwistDriverOff` | Same scenario with TwistBoneDriver disabled after load. | `motion_probe_gameplay_twistoff.csv` |
| `E_Avatar_Retarget` | Compares node rest with skin bind pose. Humanoid round trips. Zero-muscle pose. Importer T-pose. Mirror checks. 240 Hz sub-frame sampling of Run, Walk and Gather_Plant. 21-step muscle-space blends (walk/run, idle/gather, gather/run, gather/walk). | `motion_probe_avatar.txt`, `motion_probe_avatar.csv` |
| `PrimalAnimAudit.RenderArmTwist` | Edit-mode renders of the arms: bind pose vs humanoid round trip, Run frames before and after the flip, importer T-pose, each with and without the TwistBoneDriver. | `Documentation/Screenshots/Review/armtwist_*.png`, `_cmp_tpose.png`, `_cmp_runflip_L.png`, `_cmp_bind_L.png`, `_cmp_driver.png` |

**Pose metrics** are taken per frame in the character's torso frame, after the Animator, the IK pass and TwistBoneDriver:
- **abd:** upper arm out of the sagittal plane (deg; + means out, - means across the body).
- **elev:** upper arm angle from hanging.
- **swing:** fore-aft angle.
- **elbow:** flexion (0 is straight).
- **handUp:** hand minus shoulder along the torso up axis (m).
- **rollUpper, rollLower, rollHand, rollTwist:** each bone's roll about its long axis relative to its parent, zero at the skin bind pose. This is what the mesh deforms from.
- **thumbRoll:** absolute fist orientation about the forearm.
- **hipsY, chestYaw:** hips height, and shoulder line vs hip line.
- **Timing and state:** root yaw and yaw rate, Animator state, transition and parameters, PlayerIK private weights, frame time.

The probe forces `Animator.cullingMode = AlwaysAnimate` while recording (in-game setting: CullUpdateTransforms).

## 2. Settings found

### Avatar and import (`PLAYER_Survivor.fbx.meta`)

| Setting | Value |
|---|---|
| upperArmTwist / lowerArmTwist / upperLegTwist / lowerLegTwist | 0.5 / **0** (twist bones found) / 0.5 / 0.5 |
| armStretch / legStretch / feetSpacing / translation DoF | 0.05 / 0.05 / 0 / off |
| Muscle limits | all default (`modified: 0` on every bone) |
| T-pose (humanDescription.skeleton) | Built by `PrimalCharacterBuilder.ConfigureImporter` from the **model's node transforms**, with `target = (Mathf.Sign(dir.x),0,0)` for the upper and lower arms |
| Clips (all 58) | Bake into pose on for rotation, Y and XZ; based upon original; no mirror; cycle offset 0; loop time + loop pose on the 23 loops |
| Import warnings | `Thigh_L/R has scale animation that will be discarded` (Run, Walk, Sprint, attacks...) |
| Animator (prefab) | applyRootMotion off, updateMode Normal, culling CullUpdateTransforms, humanScale 1.02 |
| Hierarchy | Hand_L and LowerArmTwist_L are both children of LowerArm_L |

### Animator controller (`PlayerAnimator.controller`, matches the builder)

- **Layers:**
  - Base: IK pass on.
  - UpperBody: override, mask AM_Player_UpperBody, weight 1, default Empty.
  - HitReaction: additive, same mask, weight 1, default Empty.
  - Write Defaults is on everywhere.
- **Blend trees:**
  - Locomotion (1D on Speed): Idle 0, Walk 1.35, Run 3.8, Sprint 6.2.
  - StrafeLocomotion (2D freeform directional on VelX/VelZ): 9 clips.
  - Crouch (1D): 0 and 0.95.
  - TurnInPlace (1D on TurnSpeed): -90, 0, 90.
- **Transitions:**
  - Into Gather_* / Craft / Build: from Locomotion, Crouch, TurnInPlace, Strafe and Idle_Variation; 0.2 s fixed; no exit time; offset 0; interruption None.
  - Out of the gather loops: 0.3 s fixed, on `Action != id`; no exit time; interruption None.
  - Foot IK is on for Locomotion, Crouch, Strafe and Idle_Variation, and off for action states and TurnInPlace.
- **Measured effect:** the controller with both upper layers adds nothing to the run pose. Controller vs raw clip at the same normalized time: max difference 0.01 deg on every metric, with or without layers 1/2.

### Runtime

| Component | Setting |
|---|---|
| PlayerMotor | walk 1.35, run 3.8, accel 10, decel 14, **turnSpeed 600 deg/s, turnSmoothTime 0.09 s**, facing = velocity direction |
| PlayerAnimationDriver | speedDamp 0.08, velDamp 0.1. IsBusy goes false as soon as the *next* state is Locomotion, so CanMove returns at the start of an exit crossfade. |
| PlayerInteraction | Focus rotation `RotateTowards(..., 540 deg/s)` every frame while an action runs |
| PlayerIK | foot on (runningFootWeight 0.35), look on (w 0.75, body 0.15, head 0.75, clamp 0.55), hand IK on but weight 0 when unarmed, elbow hints only with hand IK, lean max 9 deg |
| TwistBoneDriver | share 0.6, clamp +-120 deg, rest taken from the skin bind poses |
| Camera | SphereCast r 0.22 against everything except the Player layer. Tree prototype PFB_ENV_Tree_01 has a capsule (r 0.45, h 6). Terrain tree colliders are on. |

## 3. Root causes, ranked, with evidence

### R1 (Unity-only, causes V2 and worsens V1, V6, V7): broken humanoid T-pose on the left arm

**The model's node rest is not the rest pose.** It is a posed, asymmetric frame. Angle between node rest and skin bind pose (aligned on Pelvis):

| Bone | deg |
|---|---|
| Spine | 10.8 |
| Chest | 20.6 |
| UpperArm_L | 49.7 |
| LowerArm_L | 83.1 |
| **Hand_L** | **163.6** |
| UpperArm_R | 44.1 |
| LowerArm_R | 94.0 |
| Hand_R | 70.9 |
| Thigh_L | 52.9 |
| Calf_L | 45.2 |

Mirror check, left bone vs mirrored right bone:
- **Bind pose:** 0 deg on every bone (symmetric).
- **Node rest:** UpperArm 68, LowerArm 46, Hand 175 deg.
- In the node rest the left hand is at x = +0.10 m, which is across the body midline.

**The importer T-pose points the left arm through the chest.**
- Upper-arm and forearm direction in model space: L (1,0,0) and R (1,0,0). The left arm should be (-1,0,0).
- Mirror L vs R on UpperArm, LowerArm and LowerArmTwist: 180 deg.
- Render: `Documentation/Screenshots/Review/_cmp_tpose.png`. The left arm crosses the chest, the torso is twisted and one knee is bent.
- Cause: `Mathf.Sign(dir.x)` read the posed frame, where the left elbow and hand lie to the +X side of the shoulder.

**Muscles: the same symmetric pose encodes asymmetrically.**

| Idle (arms hang symmetrically) | Left | Right |
|---|---|---|
| Arm Down-Up | +1.30 | -0.84 |
| Arm Twist | -1.95 | +0.70 |

- Left arm muscles are outside +-1 in every walk, run, idle and gather clip.
- The zero-muscle pose is wildly asymmetric: left arm elev 127, abd -52; right arm elev 60, abd 8.

**Retarget moves roll between bones.** A humanoid round trip of the skin bind pose (same joints) changes bone rotations by:

| Bone | deg |
|---|---|
| LowerArm_L | 55.8 |
| UpperArm_L | 9.1 |
| UpperArm_R | 39.0 |
| LowerArm_R | 14.1 |
| Calf_L | 23.1 |
| Hands, clavicles | 0 |

Joint directions change by 2 deg or less (L elev 41.8 to 43.7).

**Consequence 1: flips inside a clip, measured at 240 Hz.**

| Clip | Flip times | UpperArm_L roll jump | LowerArm_L roll jump | Left arm twist muscle | Right arm |
|---|---|---|---|---|---|
| Run | 0.167 s and 0.496 s | 177 deg in one 1/240 s step | -177 deg | wraps -1.97 to +1.99 | roll range 23 deg, no jumps |
| Walk | 0.258 s and 0.738 s | 179 deg | 179 deg | wraps | no jumps |

The hand's world orientation stays continuous. Render `_cmp_runflip_L.png` compares Run at 0.160 s and 0.172 s (12 ms apart). At 0.160 s the left shoulder skin and shoulder pad are crumpled and skin pokes through the pad; at 0.172 s it is normal. This happens twice per 0.667 s run cycle.

**Consequence 2: permanent wrist twist.** Hand relative to lower arm, vs bind pose:

| Side | Hand twist (every gameplay phase) | Twist bone (driver share 0.6) | Visible at the bind pose round trip |
|---|---|---|---|
| Left | -83 deg | -50 deg | -56 deg |
| Right | -23 deg | -14 deg | |

**Consequence 3: muscle-space blends spin the left fist.** Unwrapped path of the fist roll (thumbRoll) across a 21-step blend, left / right, in deg:

| Blend (w 0 to 1) | Left fist path | Right fist path | Left upper-arm roll path | L abd min |
|---|---|---|---|---|
| Idle -> Gather_Plant (entry) | **350** | 11 | 235 | -3.9 |
| Gather_Plant -> Idle (exit) | **352** | 8 | 256 | -14.0 |
| Gather_Plant -> Run phase 0 | **360** | 5 | 305 | **-31.1** (arm swings across the body) |
| Gather_Plant -> Walk | **353** | 4 | 237 | -8.7 |
| Walk <-> Run (4 phases) | 0-10 | 1-9 | 1-20 | 6.0 |

The same happens in gameplay. Fist path per phase (left / right, deg); passes B, C and D agree within 10 deg:

| Phase | Left | Right |
|---|---|---|
| gather1 | 435 | 284 |
| cancel_move | 382 | 54 |
| cancel_e | 354 | 24 |

- PlayerIK off gives 386 / 355, so the IK is not the cause.
- TwistBoneDriver off gives 383 / 354, so the driver is not the cause.

### R2 (Unity-only, V5 and part of "khựng"): frame hitch at the first gather hit, and the focus snap

**Frame hitch.** The first `OnGatherHit` event (frame 26/48 of Gather_Plant, about 1.0 to 1.2 s after pressing) costs one long frame:

| Pass | Frame time at first hit |
|---|---|
| B | 308 ms |
| C | 136 ms |
| D | 284 ms |

This matches the video: V5 appears 1.2 s after the press, and the video frames 10.15 to 10.38 s are frozen, then pose, camera and cursor jump together. The probe measured **zero yaw change after the first 0.4 s** of gathering (range 0.00 deg in all passes). So V5 is the hitch, not a rotation. The code that causes the hitch was not isolated; candidates are the hit handler's first VFX, SFX, inventory or HUD use.

**Focus snap at gather start** (this is real, and it sits in the video next to V4):
- From standing, 40 deg off: -540 deg/s for 3 frames, which is 40 deg in 0.087 s.
- Arriving at a run: +16 deg in one frame at the start.
- Then the motor keeps steering toward the decaying velocity (planar speed 3.3 to 0.5 m/s) while PlayerInteraction steers toward the focus. When the motor lets go, there is another single-frame jump of +13 deg at +0.27 s.

### R3 (Unity-only, V4): squat in 0.2 s linear crossfade

- The Gather_Plant clip is squatted from frame 0 (clip hipsY 0.60 to 0.62). The whole drop therefore happens inside the 0.2 s transition.
- Measured drop: hipsY 0.95 to 0.57 m in 0.20 s from standing, and 0.90 to 0.57 m in 0.23 s from a run.
- At 30 fps that is 6 frames, with half the drop in 3.
- Arriving at a run, the crossfade starts from the Run pose (Speed parameter 3.77), and the body still slides about 0.4 m while squatting (planar speed 3.3 to 0.5 m/s).

### R4 (Unity-only, V7): exit crossfade while the motor spins the body

- Exit transition: 0.30 s.
- IsBusy goes false on the second frame of the exit, so the motor turns and accelerates during the crossfade.
- Cancel by moving away: body yaw 326 to 172 deg (154 deg) within 0.33 s, at up to 530 deg/s. Speed parameter 0 to 1.9. PlayerIK lean up to 8.6 deg. hipsY 0.60 to 0.76 during the blend, 0.94 at 0.8 s.
- During the same 0.3 s the left fist makes a full revolution (R1), and the left hand swings to 0.22 m toward the midline (L abd -6.6).
- The right arm comes down from the gather pose (abd 35 to 9).
- Not reproduced: "both arms flung out sideways" as joint abduction. The probe shows the left-arm spin plus the 500 deg/s body spin and lean instead.

### R5 (Unity-only, V3): turning

**90 deg input change at a run:**
- Yaw rate peaks at 200 deg/s; the turn takes about 0.6 s.
- Speed dips to 2.7 m/s (planar velocity MoveTowards).
- PlayerIK lean reaches -8.8 deg.

**180 deg reversal:**
- 0.37 s of straight braking with no yaw change.
- Then 180 deg in about 0.38 s at about 500 deg/s, while planar speed is only 0.3 to 2 m/s.
- The Locomotion cycle lengthens to 3.66 s (see R6), so the legs almost stop stepping while the body spins. This is the foot slide with no pivot.

**Other factors:**
- TurnInPlace is never entered while moving: TurnSpeed is only fed when PlanarSpeed < 0.1.
- PlayerIK look (bodyWeight 0.15) twists the chest relative to the hips by up to -32 deg (turn 90) and -33 to +33 deg (reversal), against +-12 deg with IK off. The look weight drops to 0 and comes back when the camera target leaves the 95 deg cone. This is the "arms jerk".

### R6 (Unity-only, V8 and speed ramps): Idle time-synced with Walk in one 1D tree

- Locomotion mixes Idle (6.0 s loop) with Walk (1.0 s). Measured blended cycle length:
  - Speed 1.42: 0.99 s
  - Speed 0.73: **3.31 s** (steps at 0.3x rate)
  - Speed 0.05: 5.80 s
- Every start, stop and 180 deg reversal passes through this band (stLen up to 3.7 to 6.0 s in the stop and turn180 phases).
- Steady-walk arm swing is clip content: swing -11.6 to +30.4 deg, elbow 26 to 50 deg; the runtime matches the clip within 1 deg.

### R7 (secondary, "khựng" while running): PlayerIK pelvis drop at full weight

- hipsY dips of 0.21 to 0.25 m in the run, turn90 and cancel_move phases (IK off: none; hips range 0.09 m, same as the clip).
- Each dip coincides with the capsule sitting 0.30 to 0.34 m above the terrain. Terrain flat at 1.50 m, root rising to 1.84 m: a step-up or pass over an obstacle.
- In cancel_move the capsule stands on the Driftwood collider (right foot ray hits `PFB_RES_Wood_01`).
- `wantPelvis` follows the lowest planted foot at full weight, while the feet themselves only get 0.35 weight at run speed.

### Not Unity (clip content, confirmed by raw-clip = runtime)

**V1 joint geometry.** Raw retargeted Run clip compared with runtime:

| Metric | Raw clip | Runtime, all on | Runtime, IK off |
|---|---|---|---|
| Upper-arm abduction | 7.6-12.5 | 7.1-12.4 | 7.5-12.4 |
| Elbow flexion | 88-108 | 88-108 | 88-108 |
| handUp (m) | -0.37..+0.06 | -0.38..+0.05 | -0.37..+0.06 |
| Fore-aft swing | -27..+47 | | |

- Elbows are **not flared** by joint position: abduction is about 10 deg. The hands reach shoulder height because of the about 100 deg elbow bend, and that is in the clip.
- Chest counter-rotation in the clip: +-11.8 deg. Look IK adds a +7.8 deg static offset.
- The flared, bulky left-shoulder look in the video fits the R1 skin crumple, but that link was not measured.
- The Blender agent's numbers should match these joint values within about 2 deg if the retarget is sound for positions (it is at the bind pose: 2 deg or less).

**V6.** In Gather_Plant itself, the right arm rises (R abd 3.9 to 52.4 deg) and the right fist rolls 47 deg per loop. On top of that, the left fist spins during entry and exit (R1).

**TwistBoneDriver.** The math checks out:
- Its swing-twist output equals the probe's independent measure (-83.2 vs -83.1 deg; -55.7 vs -55.7 at the bind round trip).
- Max step 3.5 deg per frame; the +-120 clamp is never reached; no flip.
- It only writes the non-humanoid twist bones after the Animator, so it does not fight the pose.
- Its constant -83 / -23 deg input comes from R1.

**PlayerIK hands and elbows.** Unarmed: offW = drawW = 0 and hint weight 0 in every frame. They play no part in V1.

**V9 (camera through a tree).** Settings only, not measured. The camera collides with everything except Player, the trunk capsule (r 0.45, h 6) exists, and terrain tree colliders are on. The trunk should block a pull-back along the view line. Likely a trunk mesh wider than 0.45 m, near-plane clipping, or a lateral orbit past the trunk. It needs its own test.

## 4. Repair suggestions (not applied)

| # | Fix | Unity-only? | Files a repair would touch |
|---|---|---|---|
| 1 | **Rebuild the avatar T-pose from the skin bind pose**, not the node transforms. Choose the arm target by side name (L = -X, R = +X) instead of `Mathf.Sign(dir.x)`. Optionally square the hands (palm down) as well. Re-import; the humanoid clips are re-encoded automatically. Blender-side complement: export with the armature in Rest Position so the FBX node transforms equal the bind pose (`pf_char_export.py`). **Verify with probe E:** mirror L/R equal on all arm bones; zero-muscle pose symmetric; bind round trip under 2 deg; no 177 deg flips at 240 Hz; gather blend left fist path equal to the right one (under 20 deg); TwistBoneDriver input near 0 in Idle. | yes | `Scripts/Editor/PrimalCharacterBuilder.cs` (ConfigureImporter, T-pose block around lines 193-212), `PLAYER_Survivor.fbx.meta` (humanDescription.skeleton, regenerated) |
| 2 | Gather entry and exit: 0.2 / 0.3 s linear crossfades into a clip that starts squatted. Lengthen them (about 0.4 / 0.45 s), or add stand-to-squat and squat-to-stand clips (Blender). Do not start the action above about 0.5 m/s. Keep CanMove off, or cap the turn rate, until the exit blend is past about 60%. | yes (clips optional) | `PrimalCharacterBuilder.cs` (lines 467-468), `PlayerAnimator.controller`, `PlayerAnimationDriver.cs` (IsBusy / CanMove, line 83 and 120) |
| 3 | Focus rotation: eased and slower (about 200 deg/s, SmoothDampAngle), applied only once the motor stops steering. Find and prewarm what the first OnGatherHit loads (136-308 ms hitch). | yes | `PlayerInteraction.cs` (line 224), `PlayerFeedback` / `ResourceNode.Hit` / inventory UI path |
| 4 | Take Idle out of the time-synced Locomotion tree: separate Idle state, and a Walk/Run/Sprint tree on IsMoving or Speed. | yes | `PrimalCharacterBuilder.cs` (lines 407-411), controller |
| 5 | Moving turn rate: about 300-360 deg/s cap while moving. On reversals over 120 deg, rotate the velocity instead of braking through zero, or add pivot/turn clips (Blender). | yes (clips optional) | `PlayerMotor.cs` (lines 125-128, 175-183) |
| 6 | PlayerIK: scale the pelvis drop by the running foot weight and rate-limit it. Lower the look bodyWeight while turning or moving fast, and ease the target instead of dropping it at 95 deg. Stop TryStepUp onto resource-node colliders. | yes | `PlayerIK.cs` (lines 96-113, 287-307), `PlayerMotor.cs` (TryStepUp) |
| 7 | TwistBoneDriver: no change; re-check after fix 1. | | |

## 5. Files added or deployed (diagnostics, kept)

- `src/Tests/PlayMode/CharacterMotionProbe.cs` -> `Assets/_Project/Tests/PlayMode/CharacterMotionProbe.cs`
  - Tests A-E, each with `[Timeout(120000)]` and no WaitForEndOfFrame.
  - Skipped unless `Library/PrimalBridge/probe_on.txt` says "on". **It is now "off".**
- `src/Scripts/Editor/PrimalAnimAudit.cs` -> `Assets/_Project/Scripts/Editor/PrimalAnimAudit.cs` (bridge command `PrimalAnimAudit.RenderArmTwist "bind;bind_rt;tpose;Clip@sec"`, edit mode, writes only PNGs).
- Deploy packages: `Tools/pf_up_diag1.zip` to `pf_up_diag8.zip`.
- Data:
  - `Documentation/Tests/motion_probe_*.csv` and `.txt`.
  - `Documentation/Screenshots/Review/armtwist_*.png`, `_sheet_armtwist_*.png`, `_cmp_*.png`.
  - The allon CSV is the second run, with ground columns; ikoff and twistoff come from the first probe version, without ground columns.

Compile check: `./cc.sh all` gives runtime / editor / tests rc=0. No errors in other agents' files at the time of the checks.
