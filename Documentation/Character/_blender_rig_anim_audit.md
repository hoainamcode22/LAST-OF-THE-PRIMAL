# Blender rig and animation audit: player character (bones, orientation, constraints, clip content)

Audit date: 2026-09-28. Character art / rig agent. Read-only.

- Source: `E:\Model game khủng long\characters\PLAYER_Survivor_v2_work.blend` (saved 09:23), rig `PLAYER_Survivor_Rig`, 58 actions.
- Method: all measurements ran in separate background Blender 5.2.1 processes (launched from the owner's Blender with
  `bpy.app.binary_path -b`) on a copy (`renders\characters\audit\_work\audit_copy.blend`). The owner's open file was never
  changed, posed or saved (`is_dirty` stayed False). Scripts, raw JSON and single frames: `E:\Model game khủng long\renders\characters\audit\_work\`
  (`aud_inventory.py`, `aud_anim.py`, `aud_deform.py`, `aud_weights.py`, `aud_fbx.py`, `aud_sheets.py`; `inv2.json`, `anim3.json`,
  `deform2.json`, `weights.json`, `fbx2.json`, `fbx3.json`).
- Axes: armature space X = character's left, -Y = forward, Z = up; 1 unit = 1 m; 30 fps. "Wrist" = head of Hand_L/R.
  Abduction = angle of the upper arm out of the torso's sagittal plane (torso frame carried by the Chest bone).

## 1. Answers first

**Is the exported FBX the same as the .blend?** Yes for animation. `Assets\Art\Characters\Player\Model\PLAYER_Survivor.fbx`
(07:33) was re-imported into an empty Blender and measured with the same code: Run, Walk, Sprint, Idle, Gather_Plant, Run_Backward,
Strafe_Run_L, Crouch_Walk, Crouch, Jump, Land, Pickup, Bow_Draw, Sword_Attack_1, Attack_Spear and Throw_Spear give identical numbers
(arm angles, wrist heights, rolls, twist bone, pelvis / chest yaw) to 3 decimals. Rest bone axes differ by at most 0.03 deg.
So anything in the game that is not in the tables below was added after import (Unity retarget, Animator, IK, scripts).

| Video finding | In the Blender / FBX clip? | Evidence (measured) |
|---|---|---|
| V1 high hands, flared elbows, rigid torso (Run) | **Partly** | In the clip: at the forward end of the swing the wrist reaches shoulder-joint height (wrist - shoulder = +0.007 m, frame 10; wrist 1.362 m) with the forearm 24 deg from vertical, and the hand path is mostly vertical (38 cm up/down vs 21 cm fore-aft). The shoulder line barely rotates in the world (+-0.8 deg) while the pelvis yaws +-11 deg, so from behind the torso looks rigid. **Not** in the clip: flared elbows (abduction only 9.6-12.6 deg, elbow at most 0.06 m outboard of the shoulder joint, forearm within 12.5 deg of the sagittal plane) and hands high the whole cycle (wrist drops to 0.978 m, 0.38 m below the shoulder, at the back swing). Sprint is higher still: wrist +0.072 m above the shoulder joint. |
| (extra) Run vertical motion | **Yes** | Pelvis highest at mid/late stance (frame 4, 0.973 m) and lowest just before touchdown (frame 9, 0.887 m): the inverse of a running bounce, part of the "robotic" feel. Walk is correct. See 3.4. |
| V2 wrist / fist roll (Run) | **No** | Hand roll relative to the forearm is 0.0 deg in every frame of all 43 non-weapon clips; forearm roll relative to the upper arm is 0.0 (pure hinge); LowerArmTwist stays within -0.43..+0.09 deg in the Run and within +-2.5 deg in every non-weapon clip. Same in the FBX. (Weapon clips do contain large, fast wrist rolls, see 3.2.) |
| V3 turn without pivot step, foot slide | No clip fault | Turn_Left / Turn_Right exist (30 frames, 90 deg/s, in-place stepping). Whether they are used is a Unity question. |
| V4 stand -> deep squat in <= 0.1 s | **Content gap** | Gather_Plant starts and ends already in the squat (pelvis 0.600 m at frame 0 vs Idle 0.980-0.988 m). There is no enter / exit clip in the .blend, so the 0.38 m drop is produced entirely by the Unity crossfade. |
| V5 45 deg body yaw snap | No | Gather_Plant pelvis yaw range is 10.2 deg in total, smooth. |
| V6 right arm rises with the fist rotating, mechanical loop | **Yes** | Right arm lifts sideways to 51.6 deg abduction during the "toss" (above 25 deg from frame 30 to 42, peak at frame 34); upper-arm axial rotation changes 48.8 deg over the loop. Frames 12-17 are identical apart from the fingers (0.17 s hold), then the step 17 -> 18 moves at 643 deg/s (ease "out" starting from a hold): the largest spike of any unarmed loop (spike ratio 17.9). |
| V7 pop out of the squat, one frame with legs spread and arms flung out | No (content gap) | The clip's last frame equals its first (seam 0 deg); no frame has both arms raised (left arm max 17 deg). No exit clip exists (see V4). |
| V8 walk: arms almost straight, little swing, short steps | **No** | Walk clip: shoulder swing -11..+30 deg (41 deg), elbow 26-50 deg, pelvis bob 4.4 cm, ball-of-foot ground travel 0.675 m per step at 1.35 m/s. The game shows less than the clip contains. |

**Topology**: not a problem (see `CHARACTER_TOPOLOGY_AUDIT.md`). **Weights**: not a problem (see `CHARACTER_WEIGHT_AUDIT.md`).
**Bone orientation**: consistent and exactly mirrored; the elbow hinge used by the clips is exactly the LowerArm local X axis.
**Animation content**: the main Blender-side contributors are the Run / Sprint arm arc (V1 part), the missing shoulder counter-rotation
(V1 part), the inverted vertical bounce of the Run, the Gather_Plant toss and hold-then-snap timing (V6), the missing gather enter / exit clips (V4, V7), plus weapon-clip wrist
flips and FK/IK switch pops that are not in this video.

## 2. Bones

### 2.1 Hierarchy (57 bones, 54 deform)

```
Root (non-deform)
  Pelvis
    Spine
      Spine_Upper
        Chest
          Neck
            Head
          Clavicle_L / Clavicle_R            (not connected to Chest)
            UpperArm_*
              LowerArm_*
                Hand_*
                  Thumb/Index/Middle/Ring/Pinky _01/_02/_03_*   (15 per hand)
                  Weapon_* (non-deform)
                LowerArmTwist_*               (child of LowerArm, not connected, deform, constraint)
    Thigh_L / Thigh_R                         (not connected to Pelvis)
      Calf_*
        Foot_*
          Toe_*
```

All pose bones use QUATERNION rotation mode, inherit rotation on, inherit scale FULL. Armature object transform is identity.

### 2.2 Spec names vs existing names (no renaming done)

| Spec | Existing bone | Unity humanoid slot (from `PLAYER_Survivor.fbx.meta`, read only) |
|---|---|---|
| Root | Root (non-deform) | none |
| Pelvis | Pelvis | Hips |
| Spine | Spine | Spine |
| Spine_01 | Spine_Upper | Chest |
| Spine_02 | none (rig has 3 segments above the pelvis) | none |
| Chest | Chest | UpperChest |
| Neck / Head | Neck / Head | Neck / Head |
| Clavicle_L/R | Clavicle_L/R | LeftShoulder / RightShoulder |
| UpperArm / LowerArm / Hand L/R | UpperArm_* / LowerArm_* / Hand_* | Left/Right UpperArm, LowerArm, Hand |
| UpperLeg / LowerLeg / Foot / Toe L/R | Thigh_* / Calf_* / Foot_* / Toe_* | Left/Right UpperLeg, LowerLeg, Foot, Toes |
| ForearmTwist L/R | LowerArmTwist_L/R | no humanoid slot (not mapped) |

The meta also shows humanoid settings armTwist 0.5, foreArmTwist 0, upperLegTwist 0.5, legTwist 0.5 (Unity side, reported only).

### 2.3 Rest pose and local axes

| Item | Value |
|---|---|
| Rest pose | A-pose: upper arm 41.8 deg from vertical, forearm 43.0 deg, elbow bent 8.0 deg (forearm 8 deg forward), thigh 4.5 deg from vertical, knee bent 6.7 deg |
| Heights (rest) | Pelvis head 1.000 m, Chest head 1.372, shoulder joint (UpperArm head) 1.481, Head head 1.688, head top 1.851 |
| UpperArm_L axes | Y along the bone (0.667, -0.004, -0.745); X (-0.745, 0, -0.667) (in the frontal plane, down-medial); Z (0, -1, 0) forward; roll 138.19 |
| LowerArm_L axes | Y (0.668, -0.142, -0.731); X (-0.738, 0, -0.674); Z (-0.096, -0.990, 0.105); roll 137.59 |
| Elbow hinge used by the clips (`pf_anim.HumanRig.elbow`) | armature axis (-0.738, 0, -0.674) = **LowerArm local (1, 0, 0) exactly** (both sides). 4.3 deg from the normal of the rest bend plane. The elbow is a pure local-X hinge, so the forearm never rolls relative to the upper arm. |
| Hand_L axes | Y (0.616, -0.216, -0.757), 5.4 deg off the forearm axis; X (0.118, 0.976, -0.183) points backward; Z (-0.779, -0.023, -0.627) palm normal; roll -130.52. The hand frame is rotated about 88 deg about the bone axis relative to the forearm frame (harmless for Blender; relevant for the constraint below). |
| Thigh_L / Calf_L | Y down the leg, X (-1, 0, +-0.01) (knee hinge = local X), Z forward; rolls -179.5 / 179.3; rest knee bend plane normal 10 deg from X |
| Foot_L / Toe_L | Z up (0.013, -0.318, 0.948) / (-0.007, -0.179, 0.984); rolls 166.0 / -156.2 |
| Mirror check (all 25 L/R pairs incl. fingers) | head position 0.00 mm, Y/Z/X axis deviation 0.00 deg, rolls exactly opposite (e.g. +-138.19). |
| Connected | Spine, Spine_Upper, Chest, Neck, Head, UpperArm, LowerArm, Hand, Calf, Foot, Toe, finger chains. Not connected: Pelvis, Clavicles, Thighs, LowerArmTwist, Weapon. |
| Deform flags | all deform except Root, Weapon_L, Weapon_R (54 deform bones = 54 vertex groups). |

Bone orientation is therefore **not** a source of the problem on the Blender side: axes are clean, the elbow and knee hinges are
the local X axes, and left / right are exact mirrors.

### 2.4 Constraints and baking

- Only constraint in the rig: `TwistFromHand` on LowerArmTwist_L/R. COPY_ROTATION, target Hand_*, LOCAL -> LOCAL, Y only,
  mix REPLACE, influence 0.6, euler order AUTO.
- The actions store **identity** keys for LowerArmTwist (checked on Bow_Draw: w = 1, x = y = z = 0 on every key); the twist is produced by
  the constraint at evaluation.
- The FBX export **does** bake the constraint result: re-imported FBX LowerArmTwist values equal the evaluated Blender values
  (Bow_Draw R -27.8..+21.0 deg, Attack_Spear L 22.5..34.7, Throw_Spear R -20.9..+34.5, identical in both).
- Behaviour: single-axis tests give the intended 60 % (hand twist +-89.7 deg -> twist bone +-54.0; wrist flex 60 -> -1.6; deviation 30 -> +1.5).
  In weapon clips, where twist is combined with 55-78 deg of wrist flex / deviation, the share drops to 0.19-0.6
  (Throw_Spear R: hand roll 127.9 deg -> twist bone 34.5 deg; Sword_Attack_2 R: 127.9 -> 24.3; Spear_Attack_2 R: -111.6 -> -70.7).
  This is consistent with the constraint copying the Euler Y angle of the hand's local rotation rather than a true twist
  about the forearm; the effect is that the forearm skin gets an inconsistent amount of the wrist roll in those clips.

## 3. Animation content, measured per frame

Every action was evaluated frame by frame (constraints on) with mesh modifiers disabled for speed. Tables list the left arm
for symmetric clips (right = mirror) and both arms where they differ.

### 3.1 Arm posture

| Clip | frames | loop | abduction from sagittal plane (deg) min/max | frontal-plane abduction (deg) min/max | shoulder flex (+fwd) min/max | elbow flex min/max | forearm azimuth (+ = across body) min/max | wrist height rel. shoulder (m) min/max | wrist height abs (m) min/max | wrist fwd of shoulder (m) min/max | elbow out from shoulder (m) max |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Idle L | 180 | Y | 4.2 / 9.4 | 4.2 / 9.5 | 4.7 / 7.7 | 27.0 / 33.0 | -2.2 / -1.4 | -0.514 / -0.472 | 0.946 / 0.993 | 0.169 / 0.219 | 0.045 |
| Idle_Variation L | 150 | Y | 5.4 / 16.6 | 5.5 / 16.9 | 6.2 / 10.4 | 30.0 / 48.0 | -2.6 / -1.7 | -0.505 / -0.411 | 0.954 / 1.064 | 0.194 / 0.284 | 0.078 |
| Idle_Variation R | 150 | Y | 5.4 / 8.2 | 5.5 / 8.3 | 6.2 / 8.2 | 30.0 / 30.0 | -2.0 / -1.7 | -0.507 / -0.487 | 0.956 / 0.981 | 0.194 / 0.211 | 0.039 |
| Walk L | 30 | Y | 9.4 / 10.1 | 9.5 / 11.1 | -11.1 / 30.2 | 26.0 / 50.0 | -6.4 / 0.9 | -0.531 / -0.315 | 0.885 / 1.100 | 0.025 / 0.409 | 0.048 |
| Walk_Backward L | 30 | Y | 8.6 / 9.3 | 8.6 / 9.7 | -3.9 / 20.2 | 26.0 / 38.0 | -4.2 / -0.5 | -0.523 / -0.392 | 0.896 / 1.035 | 0.089 / 0.329 | 0.044 |
| Walk_Left L | 24 | Y | 7.8 / 8.6 | 7.8 / 8.7 | 1.9 / 12.2 | 22.0 / 28.0 | -3.0 / -1.7 | -0.526 / -0.481 | 0.897 / 0.946 | 0.123 / 0.236 | 0.041 |
| Walk_Right L | 24 | Y | 7.8 / 8.6 | 7.8 / 8.7 | 1.9 / 12.2 | 22.0 / 28.0 | -3.0 / -1.7 | -0.524 / -0.477 | 0.908 / 0.956 | 0.123 / 0.236 | 0.041 |
| Run L | 20 | Y | 9.6 / 12.6 | 9.6 / 17.7 | -24.0 / 48.2 | 88.0 / 108.0 | -12.5 / 5.2 | -0.384 / 0.007 | 0.978 / 1.362 | 0.144 / 0.349 | 0.060 |
| Sprint L | 14 | Y | 11.4 / 13.8 | 11.5 / 29.9 | -37.0 / 66.2 | 94.0 / 110.0 | -27.0 / 9.8 | -0.369 / 0.072 | 1.043 / 1.468 | 0.075 / 0.334 | 0.065 |
| Run_Backward L | 24 | Y | 7.9 / 10.8 | 7.9 / 12.1 | -9.6 / 28.2 | 70.0 / 83.9 | -5.4 / 1.5 | -0.391 / -0.116 | 0.961 / 1.246 | 0.199 / 0.387 | 0.051 |
| Strafe_Run_L L | 18 | Y | 7.7 / 10.3 | 7.7 / 11.0 | -5.3 / 22.2 | 70.0 / 82.0 | -3.9 / 0.7 | -0.406 / -0.216 | 0.947 / 1.148 | 0.229 / 0.373 | 0.049 |
| Strafe_Run_R L | 18 | Y | 7.7 / 10.3 | 7.7 / 11.0 | -5.3 / 22.2 | 70.0 / 82.0 | -3.9 / 0.7 | -0.397 / -0.208 | 0.983 / 1.173 | 0.229 / 0.373 | 0.049 |
| Crouch L | 60 | Y | 3.8 / 3.8 | 4.2 / 4.2 | 25.2 / 25.2 | 43.0 / 43.0 | -2.4 / -2.4 | -0.489 / -0.483 | 0.606 / 0.609 | 0.376 / 0.376 | 0.018 |
| Crouch_Walk L | 40 | Y | 8.2 / 9.5 | 8.2 / 10.0 | -3.9 / 20.2 | 46.0 / 60.0 | -3.8 / 0.1 | -0.500 / -0.439 | 0.622 / 0.674 | 0.171 / 0.368 | 0.045 |
| Turn_Left L | 30 | Y | 7.7 / 8.6 | 7.8 / 8.8 | 1.9 / 12.2 | 22.0 / 28.0 | -3.0 / -1.7 | -0.527 / -0.484 | 0.945 / 0.988 | 0.123 / 0.236 | 0.041 |
| Turn_Right L | 30 | Y | 7.7 / 8.6 | 7.8 / 8.8 | 1.9 / 12.2 | 22.0 / 28.0 | -3.0 / -1.7 | -0.528 / -0.486 | 0.940 / 0.983 | 0.123 / 0.236 | 0.041 |
| Jump L | 24 | N | 6.8 / 26.8 | 6.9 / 31.3 | -34.8 / 60.2 | 30.0 / 48.0 | -27.5 / 7.2 | -0.527 / -0.042 | 0.827 / 1.462 | -0.159 / 0.496 | 0.124 |
| Fall L | 30 | Y | 36.8 / 36.8 | 38.1 / 41.8 | 17.3 / 33.2 | 38.0 / 38.0 | -22.0 / -11.2 | -0.300 / -0.204 | 1.185 / 1.281 | 0.282 / 0.360 | 0.164 |
| Land L | 20 | N | 6.8 / 36.8 | 6.9 / 39.6 | 6.2 / 25.3 | 30.0 / 43.0 | -16.4 / -1.7 | -0.500 / -0.289 | 0.739 / 1.191 | 0.194 / 0.337 | 0.164 |
| Dodge L | 22 | N | 6.8 / 31.8 | 6.9 / 37.2 | 6.2 / 35.2 | 30.0 / 58.0 | -20.7 / -1.7 | -0.498 / -0.032 | 0.853 / 1.468 | 0.194 / 0.401 | 0.145 |
| Gather_Plant L | 48 | Y | 4.8 / 17.0 | 6.7 / 58.1 | 44.7 / 79.0 | 18.0 / 83.0 | -56.1 / 14.2 | -0.502 / -0.268 | 0.317 / 0.761 | 0.408 / 0.524 | 0.080 |
| Gather_Plant R | 48 | Y | 5.0 / 51.6 | 6.6 / 63.8 | 40.4 / 80.9 | 16.0 / 98.0 | -61.4 / 17.6 | -0.501 / -0.089 | 0.318 / 0.940 | 0.309 / 0.525 | 0.215 |
| Gather_Wood L | 36 | Y | -12.4 / 13.7 | -37.8 / 161.9 | 36.5 / 117.5 | 48.0 / 143.0 | 13.1 / 165.7 | -0.356 / 0.252 | 0.811 / 1.659 | -0.037 / 0.471 | 0.065 |
| Gather_Wood R | 36 | Y | -3.4 / 32.9 | -6.4 / 133.6 | 43.0 / 128.1 | 48.0 / 128.0 | -70.8 / 148.4 | -0.421 / 0.290 | 0.842 / 1.708 | -0.073 / 0.493 | 0.149 |
| Gather_Stone L | 34 | Y | -7.6 / 15.8 | -34.6 / 172.9 | 36.5 / 131.2 | 48.0 / 143.0 | 26.2 / 175.4 | -0.430 / 0.256 | 0.663 / 1.672 | -0.078 / 0.441 | 0.075 |
| Gather_Stone R | 34 | Y | 0.1 / 33.9 | 0.1 / 139.7 | 43.0 / 142.4 | 48.0 / 128.0 | -58.5 / 161.1 | -0.497 / 0.294 | 0.709 / 1.719 | -0.128 / 0.463 | 0.153 |
| Pickup L | 40 | N | 6.8 / 6.8 | 6.9 / 7.3 | 6.2 / 20.2 | 30.0 / 38.0 | -3.2 / -1.7 | -0.526 / -0.473 | 0.437 / 0.983 | 0.194 / 0.331 | 0.033 |
| Pickup R | 40 | N | 6.8 / 21.8 | 6.9 / 40.7 | 6.2 / 62.2 | 28.0 / 78.0 | -35.9 / -1.7 | -0.501 / -0.155 | 0.420 / 1.326 | 0.194 / 0.486 | 0.102 |
| Interact L | 30 | N | 6.8 / 6.8 | 6.9 / 6.9 | 6.2 / 6.2 | 30.0 / 30.0 | -1.7 / -1.7 | -0.519 / -0.498 | 0.949 / 0.983 | 0.194 / 0.194 | 0.033 |
| Interact R | 30 | N | 6.8 / 12.1 | 6.9 / 42.2 | 6.2 / 76.5 | 23.0 / 30.0 | -40.9 / -1.7 | -0.498 / -0.098 | 0.983 / 1.370 | 0.194 / 0.531 | 0.057 |
| Carry_Item L | 60 | Y | 11.2 / 11.2 | 14.5 / 14.5 | 40.0 / 40.0 | 103.0 / 103.0 | 10.1 / 10.1 | 0.022 / 0.034 | 1.516 / 1.529 | 0.341 / 0.341 | 0.053 |
| Hurt L | 24 | N | 6.8 / 16.8 | 6.9 / 17.0 | -9.8 / 6.2 | 30.0 / 38.0 | -1.7 / 2.2 | -0.498 / -0.447 | 0.983 / 0.995 | 0.089 / 0.194 | 0.079 |
| Get_Up L | 60 | N | 6.8 / 51.8 | 6.9 / 52.2 | -29.8 / 20.2 | 18.0 / 30.0 | -5.3 / 9.4 | -0.527 / 0.075 | 0.259 / 0.983 | -0.188 / 0.305 | 0.216 |
| Sword_Idle L | 60 | Y | 10.7 / 10.7 | 11.6 / 11.6 | 23.8 / 23.8 | 48.0 / 48.0 | 2.4 / 2.4 | -0.410 / -0.396 | 0.998 / 1.016 | 0.373 / 0.373 | 0.051 |
| Sword_Idle R | 60 | Y | -4.4 / -4.0 | -4.5 / -4.1 | 11.1 / 12.1 | 69.8 / 73.7 | 22.8 / 23.4 | -0.361 / -0.349 | 1.063 / 1.072 | 0.307 / 0.316 | -0.019 |
| Sword_Attack_1 L | 30 | N | 3.1 / 13.7 | 4.1 / 14.2 | -6.8 / 40.6 | 38.0 / 78.0 | -10.6 / 21.5 | -0.492 / -0.078 | 0.883 / 1.363 | 0.111 / 0.422 | 0.065 |
| Sword_Attack_1 R | 30 | N | -6.0 / 44.0 | -8.3 / 50.4 | -15.5 / 54.5 | 69.8 / 139.1 | -28.5 / 46.0 | -0.412 / 0.106 | 0.873 / 1.552 | 0.167 / 0.311 | 0.191 |
| Attack_Spear L | 30 | N | -31.4 / 25.4 | -69.8 / 29.9 | -2.6 / 77.0 | 8.0 / 78.6 | 2.0 / 126.9 | -0.336 / -0.220 | 1.079 / 1.184 | 0.260 / 0.513 | 0.118 |
| Attack_Spear R | 30 | N | 16.0 / 44.8 | 16.2 / 45.2 | -9.9 / 9.7 | 87.2 / 124.6 | 30.7 / 57.7 | -0.346 / -0.229 | 1.073 / 1.159 | 0.150 / 0.209 | 0.193 |
| Bow_Aim L | 60 | Y | 9.1 / 10.7 | 11.7 / 14.0 | -40.5 / -39.0 | 96.8 / 98.5 | -32.0 / -27.2 | -0.304 / -0.294 | 1.175 / 1.185 | 0.036 / 0.039 | 0.051 |
| Bow_Aim R | 60 | Y | -57.7 / -56.6 | -60.1 / -58.9 | 23.3 / 24.4 | 8.0 / 8.0 | 144.9 / 146.2 | -0.267 / -0.257 | 1.214 / 1.224 | 0.089 / 0.091 | -0.229 |
| Bow_Draw L | 26 | N | 10.7 / 50.2 | 14.0 / 51.8 | -40.5 / -18.4 | 88.7 / 105.5 | -44.8 / -27.2 | -0.304 / -0.041 | 1.175 / 1.440 | 0.036 / 0.138 | 0.211 |
| Bow_Draw R | 26 | N | -70.9 / -40.6 | -79.1 / -41.0 | -23.4 / 56.2 | 8.0 / 124.7 | 71.6 / 146.0 | -0.267 / 0.062 | 1.214 / 1.546 | 0.015 / 0.121 | -0.179 |
| Throw_Spear L | 46 | N | 6.8 / 24.3 | 6.9 / 51.1 | 6.2 / 75.2 | 16.0 / 44.6 | -44.4 / 6.1 | -0.506 / -0.015 | 0.931 / 1.409 | 0.194 / 0.522 | 0.113 |
| Throw_Spear R | 46 | N | -41.3 / 84.3 | -51.3 / 87.7 | -87.6 / 61.9 | 30.0 / 158.0 | -146.4 / 61.9 | -0.498 / 0.152 | 0.976 / 1.582 | -0.080 / 0.205 | 0.273 |
| Sword_Equip L | 22 | N | 6.8 / 10.7 | 6.9 / 11.6 | 6.2 / 23.8 | 30.0 / 48.0 | -1.7 / 2.4 | -0.504 / -0.396 | 0.972 / 1.016 | 0.194 / 0.373 | 0.051 |
| Sword_Equip R | 22 | N | 6.8 / 78.2 | 6.9 / 123.8 | 6.2 / 126.5 | 30.0 / 146.4 | -93.4 / 97.5 | -0.498 / 0.218 | 0.983 / 1.698 | 0.022 / 0.401 | 0.268 |

Run, frame by frame (20-frame loop, 0.667 s):

| frame | L shoulder flex | L elbow | L abduction | L wrist z (m) | L wrist - shoulder z (m) | L wrist fwd of shoulder (m) | pelvis yaw | chest yaw | shoulder-line yaw | pelvis z (m) |
|---|---|---|---|---|---|---|---|---|---|---|
| 0 | -24.0 | 88.7 | 12.1 | 0.978 | -0.383 | 0.144 | -11.0 | 5.3 | 0.82 | 0.891 |
| 1 | -22.6 | 88.0 | 11.4 | 0.992 | -0.384 | 0.151 | -10.5 | 5.1 | 0.82 | 0.907 |
| 2 | -18.3 | 88.3 | 10.6 | 1.025 | -0.377 | 0.178 | -8.9 | 4.4 | 0.75 | 0.933 |
| 3 | -11.6 | 89.5 | 9.9 | 1.069 | -0.358 | 0.219 | -6.5 | 3.3 | 0.60 | 0.958 |
| 4 | -3.2 | 91.6 | 9.6 | 1.119 | -0.323 | 0.264 | -3.4 | 1.8 | 0.39 | 0.973 |
| 5 | 6.2 | 94.3 | 9.7 | 1.165 | -0.274 | 0.304 | 0.0 | 0.1 | 0.14 | 0.970 |
| 6 | 19.2 | 97.3 | 10.8 | 1.221 | -0.197 | 0.339 | 3.4 | -1.5 | -0.12 | 0.948 |
| 7 | 30.9 | 100.5 | 11.8 | 1.269 | -0.118 | 0.349 | 6.5 | -3.0 | -0.36 | 0.918 |
| 8 | 40.3 | 103.3 | 12.3 | 1.310 | -0.053 | 0.341 | 8.9 | -4.2 | -0.57 | 0.894 |
| 9 | 46.2 | 105.7 | 12.3 | 1.342 | -0.010 | 0.327 | 10.5 | -5.0 | -0.73 | 0.887 |
| 10 | 48.2 | 107.3 | 12.0 | 1.360 | 0.007 | 0.318 | 11.0 | -5.3 | -0.82 | 0.891 |
| 11 | 46.1 | 108.0 | 11.4 | 1.362 | -0.002 | 0.317 | 10.5 | -5.1 | -0.82 | 0.907 |
| 12 | 40.1 | 107.7 | 10.7 | 1.351 | -0.037 | 0.323 | 8.9 | -4.4 | -0.75 | 0.933 |
| 13 | 30.8 | 106.4 | 10.0 | 1.318 | -0.094 | 0.329 | 6.5 | -3.3 | -0.60 | 0.958 |
| 14 | 19.2 | 104.4 | 9.6 | 1.261 | -0.166 | 0.322 | 3.4 | -1.8 | -0.39 | 0.973 |
| 15 | 6.2 | 101.7 | 9.7 | 1.185 | -0.241 | 0.295 | -0.0 | -0.1 | -0.14 | 0.970 |
| 16 | -3.1 | 98.6 | 10.9 | 1.115 | -0.293 | 0.263 | -3.4 | 1.5 | 0.12 | 0.948 |
| 17 | -11.6 | 95.5 | 11.9 | 1.049 | -0.333 | 0.224 | -6.5 | 3.0 | 0.36 | 0.918 |
| 18 | -18.3 | 92.6 | 12.5 | 1.002 | -0.360 | 0.185 | -8.9 | 4.2 | 0.57 | 0.894 |
| 19 | -22.6 | 90.3 | 12.6 | 0.981 | -0.376 | 0.156 | -10.5 | 5.0 | 0.73 | 0.887 |
| 20 | -24.0 | 88.7 | 12.1 | 0.978 | -0.383 | 0.144 | -11.0 | 5.3 | 0.82 | 0.891 |

Walk, every 3rd frame (30-frame loop):

| frame | L shoulder flex | L elbow | L abduction | L wrist z (m) | L wrist - shoulder z (m) | L wrist fwd of shoulder (m) | pelvis yaw | chest yaw | shoulder-line yaw | pelvis z (m) |
|---|---|---|---|---|---|---|---|---|---|---|
| 0 | -11.1 | 26.8 | 9.5 | 0.885 | -0.531 | 0.025 | -6.0 | 3.0 | 0.39 | 0.942 |
| 3 | -7.8 | 26.4 | 9.4 | 0.901 | -0.530 | 0.053 | -4.9 | 2.4 | 0.34 | 0.954 |
| 6 | 0.9 | 30.3 | 9.6 | 0.945 | -0.514 | 0.149 | -1.9 | 1.0 | 0.16 | 0.977 |
| 9 | 13.6 | 37.2 | 9.9 | 1.010 | -0.455 | 0.280 | 1.9 | -0.9 | -0.08 | 0.982 |
| 12 | 25.6 | 44.4 | 10.0 | 1.063 | -0.365 | 0.378 | 4.9 | -2.4 | -0.29 | 0.950 |
| 15 | 30.2 | 49.1 | 9.6 | 1.100 | -0.315 | 0.409 | 6.0 | -3.0 | -0.39 | 0.942 |
| 18 | 25.6 | 49.6 | 9.5 | 1.082 | -0.343 | 0.386 | 4.9 | -2.4 | -0.34 | 0.954 |
| 21 | 13.6 | 45.6 | 9.6 | 1.022 | -0.427 | 0.303 | 1.9 | -1.0 | -0.16 | 0.977 |
| 24 | 0.9 | 38.7 | 10.0 | 0.959 | -0.497 | 0.182 | -1.9 | 0.9 | 0.08 | 0.982 |
| 27 | -7.8 | 31.6 | 10.0 | 0.898 | -0.525 | 0.077 | -4.9 | 2.4 | 0.29 | 0.950 |
| 30 | -11.1 | 26.8 | 9.5 | 0.885 | -0.531 | 0.025 | -6.0 | 3.0 | 0.39 | 0.942 |

### 3.2 Forearm / wrist roll

Hand roll = swing-twist of the Hand's local rotation about the forearm axis, relative to rest. Twist bone = evaluated LowerArmTwist
rotation about its Y relative to rest (constraint result, as exported). Forearm roll = LowerArm local twist about its own Y.
Upper-arm axial rotation is a swing-twist relative to the A-pose rest, so part of it is the reference choice; use it for comparison
between clips only. Flags asked for: roll amplitude > 40 deg in locomotion (none) and single-frame jumps > 25 deg (only weapon clips).

| Clip | side | hand roll vs forearm (deg) min/max | max step/frame | twist bone (evaluated) min/max | forearm roll vs upper arm (range) | upper-arm axial rot. vs chest (range, rest-referenced) | wrist swing (flex/dev) max |
|---|---|---|---|---|---|---|---|
| Idle | L | -0.0 / 0.0 | 0.0 | -0.3 / -0.1 | 0.0 | 1.8 | 6.0 |
| Idle | R | -0.0 / 0.0 | 0.0 | -0.3 / -0.1 | 0.0 | 1.9 | 6.0 |
| Idle_Variation | L | -0.0 / 0.0 | 0.0 | -0.8 / -0.2 | 0.0 | 1.7 | 20.0 |
| Idle_Variation | R | 0.0 / 0.0 | 0.0 | -0.2 / -0.2 | 0.0 | 1.0 | 4.0 |
| Walk | L | -0.0 / 0.0 | 0.0 | -0.4 / 0.1 | 0.0 | 19.4 | 10.0 |
| Walk | R | 0.0 / 0.0 | 0.0 | -0.4 / 0.1 | 0.0 | 19.4 | 10.0 |
| Walk_Backward | L | -0.0 / 0.0 | 0.0 | -0.4 / 0.0 | 0.0 | 11.1 | 8.2 |
| Walk_Backward | R | -0.0 / 0.0 | 0.0 | -0.4 / 0.0 | 0.0 | 11.1 | 8.2 |
| Walk_Left | L | -0.0 / 0.0 | 0.0 | -0.3 / -0.1 | 0.0 | 4.7 | 5.8 |
| Walk_Left | R | 0.0 / 0.0 | 0.0 | -0.3 / -0.1 | 0.0 | 4.7 | 5.8 |
| Walk_Right | L | -0.0 / 0.0 | 0.0 | -0.3 / -0.1 | 0.0 | 4.7 | 5.8 |
| Walk_Right | R | 0.0 / 0.0 | 0.0 | -0.3 / -0.1 | 0.0 | 4.7 | 5.8 |
| Run | L | 0.0 / 0.0 | 0.0 | -0.4 / 0.1 | 0.0 | 34.5 | 10.0 |
| Run | R | 0.0 / 0.0 | 0.0 | -0.4 / 0.1 | 0.0 | 34.5 | 10.0 |
| Sprint | L | -0.0 / 0.0 | 0.0 | -0.4 / 0.1 | 0.0 | 52.6 | 10.0 |
| Sprint | R | -0.0 / 0.0 | 0.0 | -0.4 / 0.1 | 0.0 | 52.6 | 10.0 |
| Run_Backward | L | -0.0 / 0.0 | 0.0 | -0.4 / 0.1 | 0.0 | 17.1 | 10.0 |
| Run_Backward | R | 0.0 / 0.0 | 0.0 | -0.4 / 0.1 | 0.0 | 17.1 | 10.0 |
| Strafe_Run_L | L | -0.0 / 0.0 | 0.0 | -0.4 / 0.0 | 0.0 | 12.4 | 8.8 |
| Strafe_Run_L | R | 0.0 / 0.0 | 0.0 | -0.4 / 0.0 | 0.0 | 12.4 | 8.8 |
| Strafe_Run_R | L | -0.0 / 0.0 | 0.0 | -0.4 / 0.0 | 0.0 | 12.4 | 8.8 |
| Strafe_Run_R | R | 0.0 / 0.0 | 0.0 | -0.4 / 0.0 | 0.0 | 12.4 | 8.8 |
| Crouch | L | 0.0 / 0.0 | 0.0 | -0.2 / -0.2 | 0.0 | 0.0 | 4.0 |
| Crouch | R | 0.0 / 0.0 | 0.0 | -0.2 / -0.2 | 0.0 | 0.0 | 4.0 |
| Crouch_Walk | L | -0.0 / 0.0 | 0.0 | -0.4 / 0.0 | 0.0 | 11.0 | 8.2 |
| Crouch_Walk | R | -0.0 / 0.0 | 0.0 | -0.4 / 0.0 | 0.0 | 11.0 | 8.2 |
| Turn_Left | L | -0.0 / 0.0 | 0.0 | -0.3 / -0.1 | 0.0 | 4.7 | 5.8 |
| Turn_Left | R | 0.0 / 0.0 | 0.0 | -0.3 / -0.1 | 0.0 | 4.7 | 5.8 |
| Turn_Right | L | -0.0 / 0.0 | 0.0 | -0.3 / -0.1 | 0.0 | 4.7 | 5.8 |
| Turn_Right | R | 0.0 / 0.0 | 0.0 | -0.3 / -0.1 | 0.0 | 4.7 | 5.8 |
| Jump | L | 0.0 / 0.0 | 0.0 | -0.2 / -0.2 | 0.0 | 49.0 | 4.0 |
| Jump | R | 0.0 / 0.0 | 0.0 | -0.2 / -0.2 | 0.0 | 49.0 | 4.0 |
| Fall | L | 0.0 / 0.0 | 0.0 | -0.2 / -0.2 | 0.0 | 10.4 | 4.0 |
| Fall | R | 0.0 / 0.0 | 0.0 | -0.2 / -0.2 | 0.0 | 10.4 | 4.0 |
| Land | L | 0.0 / 0.0 | 0.0 | -0.2 / -0.2 | 0.0 | 13.3 | 4.0 |
| Land | R | 0.0 / 0.0 | 0.0 | -0.2 / -0.2 | 0.0 | 13.3 | 4.0 |
| Dodge | L | 0.0 / 0.0 | 0.0 | -0.2 / -0.2 | 0.0 | 18.8 | 4.0 |
| Dodge | R | 0.0 / 0.0 | 0.0 | -0.2 / -0.2 | 0.0 | 18.8 | 4.0 |
| Gather_Plant | L | 0.0 / 0.0 | 0.0 | -0.2 / -0.2 | 0.0 | 42.3 | 4.0 |
| Gather_Plant | R | 0.0 / 0.0 | 0.0 | -0.2 / -0.2 | 0.0 | 48.8 | 4.0 |
| Gather_Wood | L | 0.0 / 0.0 | 0.0 | -0.2 / -0.2 | 0.0 | 95.8 | 4.0 |
| Gather_Wood | R | -0.0 / 0.0 | 0.0 | -0.2 / 2.5 | 0.0 | 107.9 | 50.0 |
| Gather_Stone | L | 0.0 / 0.0 | 0.0 | -0.2 / -0.2 | 0.0 | 117.3 | 4.0 |
| Gather_Stone | R | -0.0 / 0.0 | 0.0 | -0.2 / 2.5 | 0.0 | 130.8 | 50.0 |
| Pickup | L | 0.0 / 0.0 | 0.0 | -0.2 / -0.2 | 0.0 | 6.1 | 4.0 |
| Pickup | R | 0.0 / 0.0 | 0.0 | -0.8 / -0.2 | 0.0 | 33.0 | 20.0 |
| Interact | L | 0.0 / 0.0 | 0.0 | -0.2 / -0.2 | 0.0 | 0.0 | 4.0 |
| Interact | R | -0.0 / 0.0 | 0.0 | -0.2 / 1.0 | 0.0 | 36.5 | 20.0 |
| Carry_Item | L | -0.0 / -0.0 | 0.0 | 0.5 / 0.5 | 0.0 | 0.0 | 10.0 |
| Carry_Item | R | -0.0 / -0.0 | 0.0 | 0.5 / 0.5 | 0.0 | 0.0 | 10.0 |
| Hurt | L | 0.0 / 0.0 | 0.0 | -0.2 / -0.2 | 0.0 | 7.7 | 4.0 |
| Hurt | R | 0.0 / 0.0 | 0.0 | -0.2 / -0.2 | 0.0 | 6.6 | 4.0 |
| Get_Up | L | 0.0 / 0.0 | 0.0 | -0.2 / -0.2 | 0.0 | 23.7 | 4.0 |
| Get_Up | R | 0.0 / 0.0 | 0.0 | -0.2 / -0.2 | 0.0 | 36.3 | 4.0 |
| Sword_Idle | L | 0.0 / 0.0 | 0.0 | -0.3 / -0.3 | 0.0 | 0.0 | 6.0 |
| Sword_Idle | R | 14.2 / 15.1 | 0.3 | 7.5 / 8.0 | 0.0 | 1.1 | 18.1 |
| Sword_Attack_1 | L | 0.0 / 0.0 | 0.0 | -0.3 / -0.3 | 0.0 | 16.5 | 6.0 |
| Sword_Attack_1 | R | -87.0 / 18.8 | 28.0 | -32.7 / 9.6 | 0.0 | 70.5 | 75.9 |
| Attack_Spear | L | 64.6 / 80.6 | 10.0 | 22.5 / 34.7 | 0.0 | 53.6 | 65.2 |
| Attack_Spear | R | -84.5 / -17.9 | 26.1 | -40.2 / -4.5 | 0.0 | 27.1 | 57.2 |
| Bow_Aim | L | -53.0 / -50.7 | 0.4 | -27.1 / -26.9 | 0.0 | 4.3 | 23.9 |
| Bow_Aim | R | -56.6 / -55.3 | 1.0 | -27.3 / -26.6 | 0.0 | 1.0 | 23.6 |
| Bow_Draw | L | -53.0 / -45.5 | 3.3 | -28.9 / -27.0 | 0.0 | 28.6 | 31.7 |
| Bow_Draw | R | -57.5 / 52.8 | 15.5 | -27.8 / 21.0 | 0.0 | 58.7 | 48.1 |
| Throw_Spear | L | 0.0 / 75.5 | 10.3 | -0.2 / 28.7 | 0.0 | 39.2 | 61.6 |
| Throw_Spear | R | -78.2 / 127.9 | 164.4 | -20.9 / 34.5 | 0.0 | 174.5 | 73.6 |
| Sword_Equip | L | 0.0 / 0.0 | 0.0 | -0.3 / -0.2 | 0.0 | 0.8 | 6.0 |
| Sword_Equip | R | -97.8 / 21.0 | 107.9 | -58.4 / 13.5 | 0.0 | 147.9 | 78.6 |
| Sword_Attack_2 | L | 0.0 / 0.0 | 0.0 | -0.3 / -0.3 | 0.0 | 34.3 | 6.0 |
| Sword_Attack_2 | R | 13.5 / 127.9 | 33.6 | 6.7 / 24.3 | 0.0 | 73.4 | 74.0 |
| Sword_Attack_3 | L | 0.0 / 0.0 | 0.0 | -0.3 / -0.3 | 0.0 | 20.0 | 6.0 |
| Sword_Attack_3 | R | 14.3 / 110.8 | 34.4 | 6.0 / 26.9 | 0.0 | 96.5 | 70.4 |
| Sword_Heavy | L | 0.0 / 100.5 | 21.6 | -0.3 / 25.0 | 0.0 | 101.4 | 71.6 |
| Sword_Heavy | R | -89.1 / 15.9 | 64.0 | -50.6 / 8.0 | 0.0 | 94.0 | 78.6 |
| Sword_Unequip | L | 0.0 / 0.0 | 0.0 | -0.3 / -0.2 | 0.0 | 0.8 | 6.0 |
| Sword_Unequip | R | -89.9 / 18.7 | 63.7 | -40.4 / 13.3 | 0.0 | 114.0 | 74.1 |
| Spear_Attack_2 | L | 60.3 / 76.4 | 6.1 | 15.0 / 29.7 | 0.0 | 19.4 | 73.4 |
| Spear_Attack_2 | R | -111.6 / -27.9 | 39.2 | -70.7 / -12.6 | 0.0 | 378.5 | 76.4 |
| Attack_Spear_Heavy | L | 64.4 / 84.1 | 6.0 | 21.2 / 39.1 | 0.0 | 57.3 | 67.7 |
| Attack_Spear_Heavy | R | -92.4 / -18.3 | 32.5 | -37.7 / -4.9 | 0.0 | 56.6 | 55.6 |
| Bow_Release | L | -32.5 / -14.6 | 8.2 | -20.0 / -9.5 | 0.0 | 18.4 | 28.4 |
| Bow_Release | R | 13.5 / 72.4 | 26.9 | 6.7 / 18.4 | 0.0 | 44.8 | 70.2 |
| Sword_Block | L | -97.3 / -85.6 | 4.1 | -61.0 / -43.0 | 0.0 | 1.9 | 47.3 |
| Sword_Block | R | 70.9 / 73.2 | 2.0 | 39.2 / 41.0 | 0.0 | 0.3 | 34.8 |

Non-weapon clips (43 actions): hand roll 0.0 and forearm roll 0.0 in every frame, twist bone within +-2.5 deg (wrist flexion leaking through the Euler copy). Weapon clips with single-frame wrist-roll jumps
above 25 deg: Throw_Spear R 164.4 deg, Sword_Equip R 107.9, Sword_Heavy R 64.0, Sword_Unequip R 63.7, Spear_Attack_2 R 39.2,
Sword_Attack_3 R 34.4, Sword_Attack_2 R 33.6, Attack_Spear_Heavy R 32.5, Sword_Attack_1 R 28.0, Bow_Release R 26.9, Attack_Spear R 26.1.
These are solver-driven (pf_weapon_ik, wtwist limit +-95 deg) and would show as "wrist snapping" in combat, not in this video.

### 3.3 Pelvis, chest, head

Yaw / tilt / obliquity are world-space rotations relative to rest (Euler ZXY of the bone delta). Shoulder-line yaw = world yaw of the
line between the two UpperArm heads; correlation = Pearson correlation of hip-line and shoulder-line yaw over the clip (-1 = anti-phase).

| Clip | pelvis yaw range | pelvis obliquity range | pelvis tilt range | chest yaw range (world) | shoulder-line yaw range (world) | shoulder-minus-hip yaw range | hip/shoulder yaw correlation | head yaw range | head pitch range | pelvis height min/max (m) | pelvis lateral range (m) |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Idle | 3.0 | 4.4 | 0.1 | 0.5 | 1.0 | 3.1 | 0.46 | 9.0 | 8.2 | 0.980 / 0.988 | 0.044 |
| Idle_Variation | 0.0 | 3.6 | 0.0 | 16.0 | 16.0 | - | - | 103.2 | 21.2 | 0.982 / 0.988 | 0.036 |
| Walk | 12.0 | 5.9 | 0.3 | 5.9 | 0.8 | 12.8 | -0.99 | 5.5 | 3.0 | 0.942 / 0.986 | 0.028 |
| Walk_Backward | 6.0 | 4.0 | 0.1 | 3.0 | 0.0 | 6.0 | -0.40 | 2.7 | 2.5 | 0.938 / 0.979 | 0.024 |
| Walk_Left | 4.0 | 2.6 | 3.1 | 2.0 | 0.7 | 4.7 | -1.00 | 1.8 | 3.1 | 0.948 / 0.971 | 0.008 |
| Walk_Right | 4.0 | 2.6 | 3.1 | 2.0 | 0.7 | 4.7 | -1.00 | 1.8 | 3.1 | 0.948 / 0.971 | 0.008 |
| Run | 22.0 | 7.6 | 0.7 | 10.7 | 1.6 | 23.6 | -0.99 | 10.2 | 4.9 | 0.887 / 0.973 | 0.020 |
| Sprint | 26.1 | 7.5 | 0.8 | 12.3 | 0.7 | 25.5 | 0.77 | 12.1 | 5.3 | 0.953 / 0.967 | 0.016 |
| Run_Backward | 10.0 | 4.0 | 0.2 | 5.0 | 0.3 | 10.3 | -0.99 | 4.5 | 3.1 | 0.865 / 0.936 | 0.020 |
| Strafe_Run_L | 6.0 | 2.0 | 3.4 | 2.9 | 0.5 | 5.5 | 0.94 | 2.8 | 4.3 | 0.885 / 0.963 | 0.008 |
| Strafe_Run_R | 6.0 | 2.0 | 3.4 | 2.9 | 0.5 | 5.5 | 0.94 | 2.8 | 4.3 | 0.885 / 0.963 | 0.008 |
| Crouch | 0.0 | 0.0 | 0.0 | 0.0 | 0.0 | - | - | 0.0 | 2.0 | 0.666 / 0.674 | 0.000 |
| Crouch_Walk | 8.1 | 3.8 | 0.1 | 3.6 | 0.6 | 8.5 | -0.83 | 3.9 | 2.1 | 0.690 / 0.695 | 0.036 |
| Turn_Left | 6.0 | 4.0 | 0.1 | 3.0 | 1.7 | 7.7 | -1.00 | 2.7 | 1.6 | 0.993 / 0.993 | 0.024 |
| Turn_Right | 6.0 | 4.0 | 0.1 | 3.0 | 1.7 | 7.7 | -1.00 | 2.7 | 1.6 | 0.993 / 0.993 | 0.024 |
| Jump | 0.0 | 0.0 | 14.0 | 0.0 | 0.0 | - | - | 0.0 | 28.0 | 0.830 / 1.020 | 0.000 |
| Fall | 0.0 | 0.0 | 0.0 | 0.0 | 0.0 | - | - | 0.0 | 0.0 | 1.000 / 1.000 | 0.000 |
| Land | 0.0 | 0.0 | 14.0 | 0.0 | 0.0 | - | - | 0.0 | 26.0 | 0.800 / 1.000 | 0.000 |
| Dodge | 0.0 | 0.0 | 20.0 | 0.0 | 0.0 | - | - | 0.0 | 38.0 | 0.840 / 1.020 | 0.000 |
| Gather_Plant | 10.2 | 2.1 | 12.1 | 24.6 | 20.0 | 10.0 | 0.93 | 50.4 | 41.4 | 0.600 / 0.620 | 0.000 |
| Gather_Wood | 12.4 | 3.5 | 13.6 | 49.4 | 34.4 | 22.4 | 1.00 | 50.1 | 47.9 | 0.870 / 0.950 | 0.000 |
| Gather_Stone | 12.4 | 3.5 | 13.6 | 58.2 | 32.1 | 20.1 | 1.00 | 59.4 | 57.1 | 0.870 / 0.950 | 0.000 |
| Pickup | 0.0 | 0.0 | 22.0 | 0.0 | 0.0 | - | - | 0.0 | 74.0 | 0.640 / 1.000 | 0.000 |
| Interact | 0.0 | 0.0 | 0.0 | 0.0 | 0.0 | - | - | 0.0 | 19.0 | 1.000 / 1.000 | 0.000 |
| Carry_Item | 0.0 | 0.0 | 0.0 | 0.0 | 0.0 | - | - | 0.0 | 2.0 | 1.000 / 1.000 | 0.000 |
| Hurt | 6.0 | 0.6 | 6.0 | 10.2 | 9.7 | 3.7 | 1.00 | 18.7 | 37.7 | 0.970 / 1.000 | 0.000 |
| Get_Up | 0.0 | 0.0 | 96.0 | 0.0 | 0.0 | - | - | 79.0 | 133.6 | 0.150 / 1.000 | 0.000 |

Run: pelvis yaw +-11.0 deg, chest bone yaw +-5.3 deg in anti-phase (r = -0.99), but the **shoulder line** only +-0.8 deg.
The reason is in `pf_clips_human.gait_fn`: `p["c"+sd] = (..., 0.12 * arm_amp * s0)` protracts each clavicle opposite to the
chest yaw (for the Run 0.12 x 42 = +-5.0 deg), which cancels the chest rotation at the shoulder joints. Relative to the pelvis the
shoulders counter-rotate +-11.8 deg, but in the world they stay square, and the hips do all the turning. Sprint: shoulder line +-0.4 deg,
pelvis +-13.1 deg. Head yaw follows the chest (+-5.1 deg Run), head pitch range 4.9 deg.

### 3.4 Centre of mass proxy (pelvis) and feet

Run pelvis height 0.887-0.973 m (8.7 cm oscillation), lateral 2 cm. **The Run's vertical phase is inverted**: the left foot is on the
ground frames 0-6 (heel 0-2, ball 1-6, mid-stance about frame 3), yet the pelvis is highest at frames 4-5 (0.973 / 0.970 m, mid-to-late
stance) and lowest at frame 9 (0.887 m, late flight, one frame before the right heel strikes). A running body is lowest around mid-stance
and highest in mid-flight. In `gait_fn` the intended bob (bob = -0.045, lowest at mid-stance) is overridden by the leg-reach clamp
(`z_clamped`, then erosion and blur): the contact foot lands far in front of the hip, so the pelvis must drop at touchdown and rises as the
foot passes under the hip. The result reads as a stiff "vaulting" run. Run_Backward shows the same pattern (0.865-0.936 m, highest at
frames 4-5 of a 0-10 stance). Sprint is almost flat (0.953-0.967 m, 1.4 cm). Walk is correct: 0.942-0.986 m (4.4 cm), highest at
mid-stance (frame 8 of the 0-16 left stance), lateral 2.8 cm.

Foot contact (in-place clips; slide = contact-point velocity minus the belt velocity from the clip speed, over consecutive grounded frames;
grounded = within 8 mm of its rest height):

| Clip | foot pitch min/max (deg) | ball slide max / mean (m/s) | ball drift per cycle (cm) | heel slide max (m/s) | toe slide max (m/s) |
|---|---|---|---|---|---|
| Idle | 0.0 / 0.0 | 0.000 / 0.000 | 0.00 | 0.002 | 0.000 |
| Walk | -54.5 / 14.0 | 0.074 / 0.005 | 0.26 | 0.227 | 0.074 |
| Walk_Backward | -14.0 / 24.8 | 0.140 / 0.026 | 0.96 | 0.227 | 0.191 |
| Walk_Left | -29.6 / 0.0 | 0.224 / 0.016 | 0.75 | 0.064 | 0.224 |
| Walk_Right | -29.6 / 0.0 | 0.224 / 0.016 | 0.75 | 0.069 | 0.224 |
| Run | -58.0 / 4.0 | 0.000 / 0.000 | 0.00 | 0.215 | 0.000 |
| Sprint | -65.2 / 0.0 | 0.000 / 0.000 | 0.00 | 0.018 | 0.000 |
| Run_Backward | -10.0 / 22.0 | 0.206 / 0.052 | 1.22 | 0.030 | 0.159 |
| Strafe_Run_L | -38.0 / 0.0 | 0.000 / 0.000 | 0.00 | 0.142 | 0.000 |
| Strafe_Run_R | -38.0 / 0.0 | 0.000 / 0.000 | 0.00 | 0.157 | 0.000 |
| Crouch_Walk | -18.6 / 8.0 | 0.077 / 0.003 | 0.28 | 0.152 | 0.105 |
| Crouch | 0.0 / 0.0 | 0.000 / 0.000 | 0.00 | 0.000 | 0.000 |
| Gather_Plant | -0.0 / 0.0 | 0.000 / 0.000 | 0.00 | 0.008 | 0.000 |

The ball of the foot does not slide in Run, Sprint and Strafe runs (0.000 m/s) and slides at most 0.07 m/s in Walk and Crouch_Walk.
Backward and sideways walks slide up to 0.14-0.22 m/s at the ball (about 1 cm drift per cycle). Heel values include the heel-lift
phase and are an upper bound.

### 3.5 Jerk, holds, seams, interpolation

Angular acceleration = change of per-frame angular velocity of each bone's local rotation (fingers excluded). Static hold = frames where
no main bone moves faster than 5 deg/s.

| Clip | ang. accel p95 (deg/s^2) | max (deg/s^2) | bone @ frame | spike ratio max/p95 | peak ang. speed (deg/s, bone) | static-hold frames | loop seam pose error (deg) | seam velocity change (deg/s, bone) | key interpolation |
|---|---|---|---|---|---|---|---|---|---|
| Idle | 56 | 83 | Clavicle_L @ 80 | 1.5 | 7 Calf_L | 20 (44-142) | 0.00 | 2 Hand_R | BEZIER:41811 |
| Idle_Variation | 103 | 388 | Neck @ 80 | 3.8 | 106 Neck | 16 (30-105) | 0.00 | 0 Calf_L | BEZIER:34881 |
| Walk | 4891 | 18216 | Foot_R @ 15 | 3.7 | 543 Toe_R | 0 | 0.00 | 607 Foot_L | BEZIER:7161 |
| Walk_Backward | 4335 | 14579 | Calf_L @ 28 | 3.4 | 477 Calf_R | 0 | 0.00 | 479 Toe_L | BEZIER:7161 |
| Walk_Left | 3666 | 9166 | Calf_L @ 23 | 2.5 | 351 Calf_L | 0 | 0.00 | 73 Calf_L | BEZIER:5775 |
| Walk_Right | 3666 | 9166 | Calf_R @ 11 | 2.5 | 351 Calf_R | 0 | 0.00 | 81 Calf_L | BEZIER:5775 |
| Run | 12404 | 23895 | Calf_L @ 17 | 1.9 | 796 Calf_L | 0 | 0.00 | 474 Foot_R | BEZIER:4851 |
| Sprint | 23682 | 44349 | Calf_R @ 4 | 1.9 | 1606 Calf_R | 0 | 0.00 | 949 Foot_R | BEZIER:3465 |
| Run_Backward | 7679 | 24963 | Calf_L @ 12 | 3.2 | 722 Calf_R | 0 | 0.00 | 832 Calf_R | BEZIER:5775 |
| Strafe_Run_L | 8622 | 21153 | Calf_L @ 16 | 2.5 | 535 Foot_R | 0 | 0.00 | 320 Foot_R | BEZIER:4389 |
| Strafe_Run_R | 8622 | 21153 | Calf_R @ 7 | 2.5 | 535 Foot_L | 0 | 0.00 | 306 Foot_R | BEZIER:4389 |
| Crouch | 0 | 67 | Spine_Upper @ 52 | 5591.4 | 3 Spine_Upper | 60 (0-59) | 0.00 | 0 Calf_R | BEZIER:14091 |
| Crouch_Walk | 3250 | 9065 | Foot_L @ 38 | 2.8 | 507 Thigh_L | 0 | 0.00 | 262 Foot_L | BEZIER:9471 |
| Turn_Left | 1516 | 4755 | Toe_R @ 3 | 3.1 | 324 Thigh_L | 0 | 0.00 | 98 Calf_L | BEZIER:7161 |
| Turn_Right | 1516 | 4755 | Toe_L @ 18 | 3.1 | 324 Thigh_R | 0 | 0.00 | 98 Calf_L | BEZIER:7161 |
| Jump | 9146 | 54434 | UpperArm_R @ 11 | 6.0 | 1650 UpperArm_R | 2 (6-17) | n/a | n/a | BEZIER:5775 |
| Fall | 185 | 313 | UpperArm_L @ 23 | 1.7 | 50 UpperArm_L | 2 (7-22) | 0.00 | 0 Thigh_L | BEZIER:7161 |
| Land | 2210 | 12169 | Thigh_L @ 1 | 5.5 | 626 Thigh_L | 1 (8-8) | n/a | n/a | BEZIER:4851 |
| Dodge | 5969 | 25355 | Calf_R @ 1 | 4.2 | 1156 Calf_R | 0 | n/a | n/a | BEZIER:5313 |
| Gather_Plant | 1076 | 19289 | LowerArm_R @ 17 | 17.9 | 643 LowerArm_R | 10 (0-47) | 0.00 | 7 UpperArm_R | BEZIER:11319 |
| Gather_Wood | 2290 | 36021 | LowerArm_L @ 21 | 15.7 | 1201 LowerArm_L | 1 (23-23) | 0.00 | 10 UpperArm_L | BEZIER:8547 |
| Gather_Stone | 2783 | 42412 | UpperArm_R @ 20 | 15.2 | 1414 UpperArm_R | 1 (22-22) | 0.00 | 11 UpperArm_R | BEZIER:8085 |
| Pickup | 1688 | 6978 | Calf_R @ 1 | 4.1 | 445 Calf_R | 0 | n/a | n/a | BEZIER:9471 |
| Interact | 620 | 3684 | Hand_R @ 10 | 5.9 | 354 UpperArm_R | 1 (15-15) | n/a | n/a | BEZIER:7161 |
| Carry_Item | 0 | 73 | Spine_Upper @ 53 | 73180089.6 | 3 Spine_Upper | 60 (0-59) | 0.00 | 0 Spine_Upper | BEZIER:14091 |
| Hurt | 1441 | 15702 | Calf_R @ 1 | 10.9 | 726 Calf_R | 1 (10-10) | n/a | n/a | BEZIER:5775 |
| Get_Up | 842 | 82206 | Calf_R @ 26 | 97.7 | 2495 Calf_R | 1 (34-34) | n/a | n/a | BEZIER:14091 |
| Sword_Idle | 106 | 507 | Hand_R @ 34 | 4.8 | 9 Hand_R | 34 (0-59) | 0.00 | 9 Hand_R | BEZIER:14091 |
| Sword_Attack_1 | 9759 | 62309 | UpperArm_R @ 11 | 6.4 | 1889 UpperArm_R | 0 | n/a | n/a | BEZIER:7161 |
| Attack_Spear | 5682 | 59601 | UpperArm_L @ 12 | 10.5 | 2048 UpperArm_L | 0 | n/a | n/a | BEZIER:7161 |
| Bow_Aim | 421 | 2027 | Hand_R @ 0 | 4.8 | 34 Hand_R | 4 (1-58) | 0.00 | 68 Hand_R | BEZIER:14091 |
| Bow_Draw | 2804 | 12071 | Hand_R @ 12 | 4.3 | 602 LowerArm_R | 0 | n/a | n/a | BEZIER:6237 |
| Throw_Spear | 7441 | 151378 | Hand_R @ 6 | 20.3 | 4486 Hand_R | 0 | n/a | n/a | BEZIER:10857 |
| Sword_Equip | 9107 | 111391 | Hand_R @ 12 | 12.2 | 3806 Hand_R | 0 | n/a | n/a | BEZIER:5313 |
| Wake_Up | 690 | 82021 | Calf_R @ 113 | 118.8 | 2453 Calf_R | 33 (4-179) | n/a | n/a | BEZIER:41811 |
| Death | 1075 | 106759 | Thigh_R @ 25 | 99.3 | 3559 Thigh_R | 13 (9-59) | n/a | n/a | BEZIER:14091 |
| Climb_End | 13403 | 107039 | Calf_L @ 11 | 8.0 | 2669 UpperArm_L | 4 (12-15) | n/a | n/a | BEZIER:6699 |
| Spear_Attack_2 | 10043 | 123124 | UpperArm_R @ 5 | 12.3 | 4143 UpperArm_R | 0 | n/a | n/a | BEZIER:8085 |
| Knife_Attack | 6870 | 99507 | UpperArm_R @ 10 | 14.5 | 3505 UpperArm_R | 0 | n/a | n/a | BEZIER:6237 |

- All keys are BEZIER on every frame (the clips are fully baked; Unity resamples anyway). No CONSTANT / LINEAR keys.
- Loops close exactly (seam pose error 0.00 deg). The velocity change across the seam is within the range of each clip's other frames
  (Run: 474 deg/s at the seam; the clip's 95th percentile per-frame change is 413 deg/s and its maximum 797 deg/s).
- Locomotion accelerations peak at the knees / ankles at touchdown and toe-off (Run max 23,895 deg/s^2, spike ratio 1.9): no outliers.
- **Gather_Plant** peak bone speed from each frame to the next (deg/s): 12 -> 17 = 0 (hold), 17 -> 18 = 643, 18 -> 19 = 501, 19 -> 20 = 376 ... 25 -> 26 = 3.
  The "pull" key uses ease "out", so the arms leave a dead stop at full speed.

| frame | R abduction | R frontal abd. | R shoulder flex | R elbow | R wrist z (m) | pelvis z (m) | max bone speed to next frame (deg/s) |
|---|---|---|---|---|---|---|---|
| 0 | 10.0 | 16.3 | 52.9 | 48.0 | 0.426 | 0.600 | 5 |
| 2 | 10.2 | 17.0 | 53.9 | 46.8 | 0.421 | 0.600 | 65 |
| 4 | 11.1 | 20.7 | 58.8 | 41.3 | 0.400 | 0.600 | 131 |
| 6 | 12.9 | 30.3 | 66.9 | 32.0 | 0.366 | 0.600 | 147 |
| 8 | 15.1 | 46.3 | 75.0 | 22.7 | 0.337 | 0.600 | 102 |
| 10 | 16.6 | 59.6 | 79.9 | 17.1 | 0.321 | 0.600 | 29 |
| 12 | 16.9 | 62.5 | 80.9 | 16.0 | 0.318 | 0.600 | 0 |
| 14 | 16.9 | 62.5 | 80.9 | 16.0 | 0.318 | 0.600 | 0 |
| 16 | 16.9 | 62.5 | 80.9 | 16.0 | 0.318 | 0.600 | 0 |
| 18 | 11.4 | 29.0 | 68.7 | 37.4 | 0.412 | 0.606 | 501 |
| 20 | 6.3 | 10.2 | 52.3 | 66.6 | 0.593 | 0.614 | 270 |
| 22 | 5.2 | 7.2 | 43.9 | 81.7 | 0.696 | 0.619 | 110 |
| 24 | 5.1 | 6.7 | 40.8 | 87.2 | 0.734 | 0.620 | 21 |
| 26 | 5.0 | 6.6 | 40.4 | 88.0 | 0.739 | 0.620 | 24 |
| 28 | 9.5 | 12.7 | 42.4 | 89.0 | 0.750 | 0.620 | 261 |
| 30 | 27.4 | 37.9 | 48.3 | 93.0 | 0.815 | 0.620 | 347 |
| 32 | 46.4 | 59.4 | 51.5 | 96.9 | 0.911 | 0.620 | 136 |
| 34 | 51.6 | 63.8 | 51.8 | 98.0 | 0.940 | 0.620 | 5 |
| 36 | 50.6 | 63.1 | 51.9 | 96.8 | 0.929 | 0.620 | 69 |
| 38 | 45.6 | 59.0 | 52.1 | 90.7 | 0.869 | 0.617 | 152 |
| 40 | 36.3 | 50.4 | 52.5 | 79.6 | 0.749 | 0.613 | 198 |
| 42 | 25.3 | 38.1 | 52.8 | 66.4 | 0.603 | 0.608 | 182 |
| 44 | 16.1 | 25.5 | 52.9 | 55.2 | 0.490 | 0.603 | 113 |
| 46 | 11.0 | 17.9 | 52.9 | 49.1 | 0.435 | 0.601 | 30 |
| 48 | 10.0 | 16.3 | 52.9 | 48.0 | 0.426 | 0.600 | 5 |

- **Gather entry**: the clip starts in the squat (pelvis 0.600 m, arms reaching forward 53 deg); the first frames move at 5-147 deg/s
  (smooth), so the V4 snap is not in the clip but in how the Animator enters it.
- **FK/IK switch pops** (not in this video): Get_Up frame 26 (2,495 deg/s, Calf_R), Wake_Up frame 113 (2,454 deg/s), Death frame 24
  (3,559 deg/s, Thigh_R), Climb_End frame 11 (2,669 deg/s). In `pf_clips_human` the FK flag flips at k = 0.5 in one frame.
- Jump starts at 1,093 deg/s on frame 0 (first segment eased "out" from the rest pose) and peaks at 1,650 deg/s at take-off (frame 10);
  Land and Hurt also start at full speed on frame 0 (intended impacts).

### 3.6 Comparison with typical human ranges (ranges given in the brief)

| Item | Typical | Blender Run | Blender Walk |
|---|---|---|---|
| Elbow flexion | run 80-100 deg; walk 10-30 deg | 88-108 deg (up to 108 at the forward end) | 26-50 deg (more bent than typical) |
| Hand height | run: hip to chest | wrist 0.98 m (hip) to 1.36 m (= shoulder joint height, 0.10 m above the Chest joint) | 0.89-1.10 m |
| Upper arm abduction | < about 20 deg | 9.6-12.6 deg (ok) | 9.4-10.1 deg (ok) |
| Shoulder counter-rotation vs pelvis | about 10-15 deg | +-11.8 deg relative, but shoulders fixed in the world (+-0.8 deg) | +-6.4 relative, +-0.4 world |
| Pelvis yaw | about 5-10 deg | +-11.0 deg (high end) | +-6.0 deg |
| Vertical motion (pelvis) | run: lowest at mid-stance, highest in flight; walk: highest at mid-stance | 8.7 cm, but highest at mid/late stance and lowest just before touchdown (inverted) | 4.4 cm, highest at mid-stance (correct) |
| Arm swing (walk) | modest, from the shoulder | n/a | shoulder -11..+30 deg |

## 4. What can be repaired without restarting (Blender side)

All clips are procedural (`scripts\pf_clips_human.py`, `pf_clips_weapons.py`, `pf_weapon_ik.py`) and re-bake per clip with
`pf_player_ship.bake(which=[...])` then `export(meta_from(B, old))`. No remodel, re-rig or re-weight is needed.

1. **Run / Sprint arm arc (V1 part)**: in `gait_fn` the elbow term `elbow0 + elbow_amp * (0.5 + 0.5 * se)` makes the elbow most bent when
   the arm is forward, which tips the forearm up to 24 deg from vertical. Keep the elbow nearly constant (e.g. Run elbow0 85-90,
   elbow_amp 0-8, or reverse the phase so it opens slightly on the forward swing) and reduce the forward flex (Run arm_amp 42 -> about 30,
   Sprint 60 -> about 45). Target: wrist top at chest height (about 0.15-0.2 m below the shoulder joint), a more fore-aft hand path.
   Rebake Run, Sprint, Run_Backward, Strafe runs.
2. **Shoulder counter-rotation (V1 part)**: flip the sign of (or remove) the clavicle "fwd" term `0.12 * arm_amp * s0` so the clavicles no
   longer cancel the chest yaw, and shift some yaw from the pelvis to the chest (Run: pelvis +-11 -> about +-7, shoulder line +-5 to +-8 in
   the world, anti-phase). Same for Walk and Sprint.
3. **Gather_Plant (V6)**: remove the 6-frame hold (keys 12 -> 17 differ only in the fingers), change the "pull" key easing from "out" to
   "inout", and lower the toss (aR abduct +35 -> about +10, less axial change). Re-bake Gather_Plant.
4. **Gather enter / exit (V4, V7)**: add two short one-shot clips (stand -> squat "ready" pose and back, about 10-14 frames each, ease
   "inout") so the Animator does not crossfade a 0.38 m pelvis drop. Same pattern for Pickup-like squats.
5. **Weapon wrist flips** (combat, not this video): add a continuity term to `pf_weapon_ik.solve` (penalise change from the previous
   frame's solution) and narrow wtwist to about +-80 deg; replace the Euler-based `TwistFromHand` with a per-frame swing-twist bake of
   the forearm twist (0.6 x true twist) before export.
6. **FK/IK pops** in Get_Up, Wake_Up, Death, Climb_End: blend the leg solution over 4-6 frames instead of flipping `fk` at k = 0.5.
7. **Run vertical phase**: make the pelvis lowest at mid-stance. Either land the foot closer under the hip (smaller forward reach at
   strike: raise `center_back`, shorten the stance travel or let the knee arrive flexed) so the reach clamp no longer dictates the curve,
   or compute the pelvis height from the bob term first and solve the contact leg with a flexed knee. Check with the same metric
   (pelvis minimum within the stance frames). Rebake Run, Run_Backward, Sprint.

## 5. Pointers for the Unity side (not measured here)

Because the FBX clip content equals the Blender content, the flared elbows, the constant high hands, the wrist roll (V2), the reduced
walk swing (V8), the squat snap (V4), the yaw snap (V5) and the abducted in-between frame (V7) should be looked for downstream:
humanoid retarget and avatar T-pose (the rig's rest is an A-pose with the arm at 41.8 deg and the elbow at 8 deg), the arm / forearm twist
settings above, the base layer's IK pass (on in `PlayerAnimator.controller`), the UpperBody override layer (weight 1, default state Empty;
note that Carry_Item holds the wrist at shoulder height, +0.02..+0.03 m, for the whole clip, which would look like V1 if it leaked into
locomotion), TwistBoneDriver, transition durations, and any frame where a state with no motion is entered.

## 6. Renders (`E:\Model game khủng long\renders\characters\audit\`)

| File | Content | What is seen |
|---|---|---|
| `aud_sheet_run_cycle.png` | Run frames 0, 2 .. 18; top row camera behind and above (like the game), bottom row left side | From behind, the forward fist rises beside the head at the top of each swing (frames 0, 8-12, 18) while the upper arms stay close to the ribs (no flare). The shoulders stay square to the camera while the hips turn. From the side the forearm points steeply up at the front and the back hand sits at the hip. |
| `aud_sheet_sprint_cycle.png` | Sprint frames 0, 2 .. 12, same layout | Same pattern, fist higher (above the shoulder joint). |
| `aud_sheet_walk_cycle.png` | Walk frames 0, 3 .. 27 | Clear arm swing with bent elbows, normal step length. |
| `aud_sheet_gather_plant_cycle.png` | Gather_Plant frames 0, 4 .. 44 | Starts in the squat; frames 12 and 16 are identical (hold); in frames 32-40 the right arm lifts sideways to head height seen from behind (V6 look). |
| `aud_sheet_clip_poses.png` | 16 clip poses x 2 views (list in the weight audit) | Jump f11: both arms forward-up, hands above head level from behind. |
| `aud_sheet_arm_deform.png`, `aud_sheet_wrist.png` | Deformation tests | See `CHARACTER_WEIGHT_AUDIT.md`. |

Individual frames: `_work\aud_<pose>_<view>.png`, `_work\seq_<clip>_<frame>_<view>.png`. The older files in `audit\` (for example
`Attack_Spear_012_front.png`, 04:48) are from an earlier session and were not touched.
