# Player character weight audit (Blender side)

Audit date: 2026-09-28. Read-only, measured in background Blender on a copy of `PLAYER_Survivor_v2_work.blend`
(scripts `aud_inventory.py`, `aud_weights.py`, `aud_deform.py` in `E:\Model game khủng long\renders\characters\audit\_work\`).
Left side is reported; the right side mirrors it (all gradient values within 0.001).

## Verdict

Weights are **not** a cause of the jerky / twisting look. All three LODs are limited to 4 influences, exactly normalized,
have no unweighted vertices and no weights on non-deform bones. Gradients across every joint are smooth (largest weight step
across one edge 0.18-0.51, Laplacian roughness max 0.11). The forearm twist split works as designed in Blender: a 90 deg hand
twist spreads along the forearm as a 0 -> 76 deg ramp and the wrist keeps 87 % of its cross-section area. Volume loss at bent
joints is the normal linear-blend-skinning amount (elbow 130 deg keeps 41 % of the joint ring area, arm forward 90 deg keeps
64 % at the shoulder, crouch knee 72 %); that looks soft at extreme poses but does not create jerks or rolls.

## Global checks

| Mesh | Verts | Influences per vertex (1 / 2 / 3 / 4) | > 4 | Unweighted | Weight sum min / max | Not normalized (>1 %) | Weights on non-deform bones (Root, Weapon_L/R) | Groups (deform bones) |
|---|---|---|---|---|---|---|---|---|
| LOD0 | 60,236 | 33,565 / 11,430 / 8,789 / 6,452 | 0 | 0 | 1.000 / 1.000 | 0 | none | 54 (54) |
| LOD1 | 10,212 | 3,406 / 2,584 / 2,384 / 1,838 | 0 | 0 | 1.000 / 1.000 | 0 | none | 54 (54) |
| LOD2 | 3,344 | 860 / 866 / 949 / 669 | 0 | 0 | 1.000 / 1.000 | 0 | none | 54 (54) |

- No empty groups, no orphan groups, every deform bone has a group. LOD0 holds 5,608 vertex-group entries below 1 % weight
  (harmless).
- Armature modifiers on all LODs: vertex groups only, `preserve volume` OFF. This is linear blend skinning, the same method Unity
  uses, so the Blender deformation previews below are representative of the game.

## Gradients along the limbs (LOD0 skin, left side)

Mean weight per 20 mm slice along the limb axis, measured from the joint (negative = parent side). n = vertices in the slice.

**Shoulder, top half (deltoid / trapezius side)**

| mm from joint | n | Spine_Upper | Chest | Neck | Clavicle_L | UpperArm_L | largest other |
|---|---|---|---|---|---|---|---|
| -90 | 1 | 0.00 | 0.00 | 0.00 | 0.90 | 0.10 |  |
| -70 | 5 | 0.00 | 0.00 | 0.00 | 0.85 | 0.15 | Clavicle_R:0.000 |
| -50 | 6 | 0.00 | 0.00 | 0.00 | 0.73 | 0.27 | Clavicle_R:0.000 |
| -30 | 3 | 0.00 | 0.01 | 0.00 | 0.62 | 0.38 |  |
| -10 | 8 | 0.00 | 0.00 | 0.00 | 0.55 | 0.45 | Clavicle_R:0.000 |
| 10 | 9 | 0.00 | 0.00 | 0.00 | 0.38 | 0.61 | Clavicle_R:0.000 |
| 30 | 10 | 0.00 | 0.00 | 0.00 | 0.24 | 0.76 |  |
| 50 | 12 | 0.00 | 0.00 | 0.00 | 0.14 | 0.86 |  |
| 70 | 14 | 0.00 | 0.00 | 0.00 | 0.07 | 0.93 |  |
| 90 | 11 | 0.00 | 0.00 | 0.00 | 0.02 | 0.98 |  |
| 110 | 7 | 0.00 | 0.00 | 0.00 | 0.00 | 1.00 |  |
| 130 | 10 | 0.00 | 0.00 | 0.00 | 0.00 | 1.00 |  |

**Shoulder, armpit half**

| mm from joint | n | Spine_Upper | Chest | Neck | Clavicle_L | UpperArm_L | largest other |
|---|---|---|---|---|---|---|---|
| -110 | 10 | 0.00 | 0.02 | 0.06 | 0.91 | 0.02 |  |
| -90 | 7 | 0.00 | 0.04 | 0.02 | 0.92 | 0.03 | Clavicle_R:0.000 |
| -70 | 6 | 0.00 | 0.04 | 0.01 | 0.87 | 0.08 | Clavicle_R:0.000 |
| -50 | 5 | 0.00 | 0.01 | 0.00 | 0.85 | 0.14 | Clavicle_R:0.000 |
| -30 | 6 | 0.02 | 0.03 | 0.00 | 0.76 | 0.20 | Clavicle_R:0.000 |
| -10 | 3 | 0.05 | 0.08 | 0.00 | 0.61 | 0.26 |  |
| 10 | 10 | 0.04 | 0.02 | 0.00 | 0.57 | 0.37 | Clavicle_R:0.000 |
| 30 | 12 | 0.04 | 0.01 | 0.00 | 0.46 | 0.49 |  |
| 50 | 21 | 0.06 | 0.02 | 0.00 | 0.38 | 0.55 |  |
| 70 | 23 | 0.08 | 0.02 | 0.00 | 0.25 | 0.66 |  |
| 90 | 17 | 0.09 | 0.03 | 0.00 | 0.19 | 0.69 |  |
| 110 | 9 | 0.03 | 0.01 | 0.00 | 0.06 | 0.90 |  |
| 130 | 10 | 0.00 | 0.00 | 0.00 | 0.00 | 1.00 |  |

**Elbow**

| mm from joint | n | UpperArm_L | LowerArm_L | LowerArmTwist_L | Hand_L | largest other |
|---|---|---|---|---|---|---|
| -70 | 17 | 0.99 | 0.01 | 0.00 | 0.00 |  |
| -50 | 24 | 0.95 | 0.05 | 0.00 | 0.00 |  |
| -30 | 23 | 0.86 | 0.14 | 0.00 | 0.00 |  |
| -10 | 26 | 0.68 | 0.32 | 0.00 | 0.00 |  |
| 10 | 22 | 0.36 | 0.64 | 0.00 | 0.00 |  |
| 30 | 22 | 0.15 | 0.85 | 0.00 | 0.00 |  |
| 50 | 13 | 0.04 | 0.96 | 0.00 | 0.00 |  |
| 70 | 10 | 0.01 | 0.98 | 0.00 | 0.00 |  |

**Forearm, elbow to hand (forearm length 280 mm)**

| mm from joint | n | UpperArm_L | LowerArm_L | LowerArmTwist_L | Hand_L | largest other |
|---|---|---|---|---|---|---|
| -30 | 21 | 0.86 | 0.14 | 0.00 | 0.00 |  |
| -10 | 25 | 0.68 | 0.32 | 0.00 | 0.00 |  |
| 10 | 23 | 0.37 | 0.63 | 0.00 | 0.00 |  |
| 30 | 20 | 0.15 | 0.85 | 0.00 | 0.00 |  |
| 50 | 15 | 0.04 | 0.96 | 0.00 | 0.00 |  |
| 70 | 10 | 0.02 | 0.98 | 0.00 | 0.00 |  |
| 90 | 19 | 0.00 | 0.93 | 0.07 | 0.00 |  |
| 110 | 1 | 0.00 | 0.89 | 0.11 | 0.00 |  |
| 130 | 20 | 0.00 | 0.72 | 0.28 | 0.00 |  |
| 150 | 2 | 0.00 | 0.50 | 0.50 | 0.00 |  |
| 170 | 18 | 0.00 | 0.46 | 0.54 | 0.00 |  |
| 190 | 15 | 0.00 | 0.25 | 0.75 | 0.00 |  |
| 210 | 13 | 0.00 | 0.17 | 0.83 | 0.00 |  |
| 230 | 18 | 0.00 | 0.11 | 0.89 | 0.00 |  |
| 250 | 24 | 0.00 | 0.08 | 0.89 | 0.03 |  |
| 270 | 37 | 0.00 | 0.06 | 0.69 | 0.23 | Thumb_01_L:0.020 |
| 290 | 42 | 0.00 | 0.02 | 0.18 | 0.68 | Thumb_01_L:0.123 |
| 310 | 36 | 0.00 | 0.00 | 0.03 | 0.61 | Thumb_01_L:0.339 |

**Hip, front**

| mm from joint | n | Pelvis | Spine | Thigh_L | Thigh_R | largest other |
|---|---|---|---|---|---|---|
| -110 | 9 | 0.09 | 0.37 | 0.45 | 0.00 | Spine_Upper:0.092 |
| -90 | 8 | 0.08 | 0.27 | 0.59 | 0.00 | Spine_Upper:0.064 |
| -70 | 6 | 0.09 | 0.21 | 0.66 | 0.00 | Spine_Upper:0.045 |
| -50 | 9 | 0.13 | 0.17 | 0.67 | 0.01 | Spine_Upper:0.017 |
| -30 | 8 | 0.13 | 0.12 | 0.73 | 0.02 | Spine_Upper:0.001 |
| -10 | 8 | 0.12 | 0.07 | 0.79 | 0.02 |  |
| 10 | 8 | 0.16 | 0.05 | 0.75 | 0.04 |  |
| 30 | 9 | 0.12 | 0.02 | 0.82 | 0.04 |  |
| 50 | 8 | 0.12 | 0.01 | 0.83 | 0.05 |  |
| 70 | 13 | 0.12 | 0.00 | 0.81 | 0.07 |  |
| 90 | 16 | 0.12 | 0.00 | 0.78 | 0.10 |  |
| 110 | 13 | 0.06 | 0.00 | 0.90 | 0.04 |  |
| 130 | 10 | 0.03 | 0.00 | 0.94 | 0.02 |  |
| 150 | 6 | 0.02 | 0.00 | 0.97 | 0.01 |  |

**Hip, back (buttock)**

| mm from joint | n | Pelvis | Spine | Thigh_L | Thigh_R | largest other |
|---|---|---|---|---|---|---|
| -110 | 10 | 0.23 | 0.36 | 0.38 | 0.00 | Spine_Upper:0.038 |
| -90 | 9 | 0.36 | 0.25 | 0.39 | 0.00 | Spine_Upper:0.008 |
| -70 | 8 | 0.45 | 0.12 | 0.43 | 0.00 | Spine_Upper:0.001 |
| -50 | 4 | 0.08 | 0.08 | 0.84 | 0.00 |  |
| -30 | 9 | 0.33 | 0.04 | 0.62 | 0.00 |  |
| -10 | 5 | 0.10 | 0.00 | 0.90 | 0.00 |  |
| 10 | 7 | 0.35 | 0.00 | 0.64 | 0.01 |  |
| 30 | 9 | 0.29 | 0.00 | 0.69 | 0.02 |  |
| 50 | 10 | 0.34 | 0.00 | 0.60 | 0.05 |  |
| 70 | 11 | 0.20 | 0.00 | 0.74 | 0.06 |  |
| 90 | 8 | 0.13 | 0.00 | 0.82 | 0.05 |  |
| 110 | 9 | 0.06 | 0.00 | 0.91 | 0.03 |  |
| 130 | 5 | 0.04 | 0.00 | 0.95 | 0.01 |  |
| 150 | 5 | 0.02 | 0.00 | 0.97 | 0.01 |  |

**Knee**

| mm from joint | n | Thigh_L | Calf_L | largest other |
|---|---|---|---|---|
| -90 | 11 | 0.99 | 0.01 |  |
| -70 | 17 | 0.97 | 0.03 |  |
| -50 | 18 | 0.92 | 0.08 |  |
| -30 | 16 | 0.80 | 0.20 |  |
| -10 | 12 | 0.66 | 0.34 |  |
| 10 | 19 | 0.41 | 0.59 |  |
| 30 | 18 | 0.18 | 0.82 |  |
| 50 | 15 | 0.08 | 0.92 |  |
| 70 | 7 | 0.02 | 0.98 |  |
| 90 | 10 | 0.01 | 0.99 |  |

**Ankle (negative = above the ankle along the shin)**

| mm from joint | n | Calf_L | Foot_L | Toe_L | largest other |
|---|---|---|---|---|---|
| -110 | 7 | 1.00 | 0.00 | 0.00 |  |
| -90 | 13 | 1.00 | 0.00 | 0.00 |  |
| -70 | 12 | 0.97 | 0.03 | 0.00 |  |
| -50 | 16 | 0.91 | 0.09 | 0.00 |  |
| -30 | 22 | 0.73 | 0.27 | 0.00 |  |
| -10 | 33 | 0.41 | 0.59 | 0.00 |  |
| 10 | 30 | 0.23 | 0.78 | 0.00 |  |
| 30 | 36 | 0.19 | 0.81 | 0.00 |  |
| 50 | 38 | 0.23 | 0.77 | 0.00 |  |

Reading:
- **Shoulder**: blended only between Clavicle_L and UpperArm_L (50/50 at about the joint on top, 20-30 mm down the arm in the
  armpit); Chest and Spine_Upper carry at most 0.09 (armpit side) and about 0 on top. The Clavicle influence extends about 110 mm
  down the upper arm. When the clips move the clavicle, the whole deltoid follows it.
- **Elbow**: smooth ramp UpperArm -> LowerArm from -70 to +70 mm (50/50 at the joint), 0.31 maximum step per edge.
- **Forearm**: LowerArm holds 0.98 up to 70 mm, LowerArmTwist ramps from 90 mm (0.07) to 230-250 mm (0.89), Hand from 270 mm.
  The twist -> hand hand-over at the wrist is the steepest transition on the body (0.47-0.51 per 9 mm edge, about two loops).
- **Hip**: below the joint the front is Thigh-dominant (0.75-0.97, Pelvis 0.02-0.16); above the joint on the front (groin / lower belly side) Thigh still carries 0.44-0.79 and Spine 0.07-0.37 while Pelvis carries only 0.08-0.16, so that skin follows the thigh when the leg lifts. The back (buttock) keeps more Pelvis (0.1-0.45). Slice means
  alternate on the back because each slice holds only 4-11 vertices; per-vertex roughness there is low (Laplacian p95 0.039, max 0.104).
- **Knee**: Thigh -> Calf over -50 .. +50 mm (50/50 near the joint), steepest step 0.50 per 22 mm edge.
- **Ankle**: Calf -> Foot over -70 .. +10 mm; Toe has no influence up to 50 mm below the ankle (it is used at the ball only).

### Steepest weight step across a single edge near each joint

| Region | group: largest weight step across one edge / 95th percentile (median edge length) |
|---|---|
| shoulder | Spine_Upper: 0.051 / 0.033; Chest: 0.044 / 0.02; Neck: 0.061 / 0.0; Clavicle_L: 0.184 / 0.158; UpperArm_L: 0.184 / 0.16 (19.7 mm) |
| elbow | UpperArm_L: 0.31 / 0.263; LowerArm_L: 0.31 / 0.263 (15.2 mm) |
| wrist | LowerArm_L: 0.041 / 0.028; LowerArmTwist_L: 0.469 / 0.325; Hand_L: 0.51 / 0.312 (9.1 mm) |
| hip | Pelvis: 0.335 / 0.125; Spine: 0.351 / 0.091; Thigh_L: 0.202 / 0.135; Thigh_R: 0.076 / 0.043 (22.8 mm) |
| knee | Thigh_L: 0.497 / 0.416; Calf_L: 0.497 / 0.416 (21.8 mm) |
| ankle | Calf_L: 0.482 / 0.232; Foot_L: 0.482 / 0.232 (14.5 mm) |

Laplacian roughness (|w - mean of neighbours|, max): hip back 0.104, hip front 0.023, shoulder 0.059, elbow 0.043, knee 0.107,
wrist 0.098. No blotches or islands were found.

## Deformation tests (posed, measured, rendered; pose restored, nothing keyed or saved)

Area / perimeter / min thickness of the closed edge ring nearest each joint (20-49 vertices), posed divided by rest. 1.00 = no
change; area is the volume-loss proxy. Arm tests use the left arm from the rest A-pose via the clip library's own anatomical setters
(`pf_anim.HumanRig`), clip poses use the stored actions.

| Pose | shoulder ring (area / perim / min-thickness) | elbow ring | wrist ring | knee ring | ankle ring |
|---|---|---|---|---|---|
| rest | 1.00 / 1.00 / 1.00 | 1.00 / 1.00 / 1.00 | 1.00 / 1.00 / 1.00 | 1.00 / 1.00 / 1.00 | 1.00 / 1.00 / 1.00 |
| arm_overhead | 0.79 / 1.15 / 0.96 | 1.00 / 1.00 / 1.00 | 1.00 / 1.00 / 1.00 | 1.00 / 1.00 / 1.00 | 1.00 / 1.00 / 1.00 |
| arm_side_90 | 1.05 / 1.09 / 1.00 | 1.00 / 1.00 / 1.00 | 1.00 / 1.00 / 1.00 | 1.00 / 1.00 / 1.00 | 1.00 / 1.00 / 1.00 |
| arm_forward_90 | 0.64 / 0.61 / 0.65 | 1.00 / 1.00 / 1.00 | 1.00 / 1.00 / 1.00 | 1.00 / 1.00 / 1.00 | 1.00 / 1.00 / 1.00 |
| arm_backward_45 | 0.75 / 0.87 / 0.79 | 1.00 / 1.00 / 1.00 | 1.00 / 1.00 / 1.00 | 1.00 / 1.00 / 1.00 | 1.00 / 1.00 / 1.00 |
| elbow_90 | 0.87 / 0.86 / 0.93 | 0.70 / 0.86 / 0.71 | 1.00 / 1.00 / 1.00 | 1.00 / 1.00 / 1.00 | 1.00 / 1.00 / 1.00 |
| elbow_130 | 0.87 / 0.86 / 0.93 | 0.41 / 0.75 / 0.43 | 1.00 / 1.00 / 1.00 | 1.00 / 1.00 / 1.00 | 1.00 / 1.00 / 1.00 |
| wrist_twist_p90 | 0.87 / 0.86 / 0.93 | 0.70 / 0.86 / 0.71 | 0.86 / 0.93 / 0.96 | 1.00 / 1.00 / 1.00 | 1.00 / 1.00 / 1.00 |
| wrist_twist_m90 | 0.87 / 0.86 / 0.93 | 0.70 / 0.86 / 0.71 | 0.88 / 0.94 / 0.94 | 1.00 / 1.00 / 1.00 | 1.00 / 1.00 / 1.00 |
| wrist_flex_60 | 0.87 / 0.86 / 0.93 | 0.70 / 0.86 / 0.71 | 0.93 / 0.97 / 0.94 | 1.00 / 1.00 / 1.00 | 1.00 / 1.00 / 1.00 |
| wrist_dev_30 | 0.87 / 0.86 / 0.93 | 0.70 / 0.86 / 0.71 | 0.97 / 0.98 / 1.00 | 1.00 / 1.00 / 1.00 | 1.00 / 1.00 / 1.00 |
| Sword_Attack_1_008 | 0.86 / 0.85 / 0.87 | 0.81 / 0.91 / 0.81 | 1.03 / 1.02 / 1.00 | 0.98 / 0.99 / 1.00 | 1.00 / 1.00 / 1.00 |
| Sword_Attack_1_014 | 0.89 / 0.91 / 0.94 | 0.96 / 0.98 / 0.96 | 1.03 / 1.02 / 1.00 | 0.94 / 0.97 / 0.99 | 0.99 / 0.99 / 1.00 |
| Bow_Draw_026 | 0.89 / 0.95 / 0.92 | 0.75 / 0.89 / 0.76 | 0.96 / 0.98 / 0.96 | 1.00 / 1.00 / 1.00 | 1.01 / 1.00 / 1.00 |
| Attack_Spear_012 | 0.71 / 0.83 / 0.66 | 1.00 / 1.00 / 1.00 | 0.80 / 0.89 / 0.94 | 0.96 / 0.98 / 1.00 | 0.98 / 0.99 / 1.00 |
| Run_000 | 0.87 / 0.88 / 0.90 | 0.75 / 0.89 / 0.76 | 1.03 / 1.02 / 1.00 | 0.95 / 0.98 / 1.00 | 1.00 / 1.00 / 1.00 |
| Run_005 | 0.88 / 0.88 / 0.99 | 0.72 / 0.87 / 0.73 | 1.03 / 1.02 / 0.99 | 0.94 / 0.97 / 0.99 | 1.00 / 1.00 / 1.00 |
| Crouch_000 | 0.83 / 0.80 / 0.94 | 0.95 / 0.98 / 0.95 | 1.03 / 1.02 / 1.00 | 0.72 / 0.85 / 0.88 | 0.92 / 0.95 / 1.00 |
| Crouch_Walk_010 | 0.87 / 0.87 / 0.98 | 0.93 / 0.96 / 0.93 | 1.03 / 1.02 / 0.99 | 0.74 / 0.86 / 0.90 | 0.92 / 0.95 / 1.00 |
| Jump_007 | 0.84 / 0.89 / 0.86 | 0.97 / 0.99 / 0.97 | 1.03 / 1.02 / 1.00 | 0.86 / 0.93 / 1.02 | 0.94 / 0.97 / 1.00 |
| Jump_011 | 0.79 / 0.78 / 0.84 | 0.96 / 0.98 / 0.96 | 1.03 / 1.02 / 1.00 | 0.95 / 0.98 / 1.00 | 1.02 / 1.01 / 1.01 |
| Land_003 | 0.88 / 0.92 / 0.99 | 0.95 / 0.98 / 0.95 | 1.03 / 1.02 / 1.00 | 0.83 / 0.91 / 0.99 | 0.94 / 0.96 / 1.00 |
| Gather_Plant_000 | 0.83 / 0.82 / 0.87 | 0.93 / 0.97 / 0.93 | 1.03 / 1.02 / 1.00 | 0.72 / 0.86 / 0.88 | 0.97 / 0.98 / 1.00 |
| Gather_Plant_012 | 0.72 / 0.76 / 0.75 | 0.99 / 1.00 / 0.99 | 1.03 / 1.02 / 1.00 | 0.72 / 0.86 / 0.88 | 0.97 / 0.98 / 1.00 |
| Gather_Plant_034 | 0.85 / 0.84 / 0.88 | 0.78 / 0.90 / 0.79 | 1.03 / 1.02 / 1.00 | 0.75 / 0.87 / 0.91 | 0.98 / 0.99 / 1.00 |
| Walk_000 | 0.87 / 0.90 / 0.94 | 0.98 / 0.99 / 0.98 | 1.03 / 1.02 / 1.00 | 0.98 / 0.99 / 1.00 | 0.99 / 0.99 / 1.00 |
| Idle_000 | 0.87 / 0.85 / 0.98 | 0.97 / 0.99 / 0.97 | 1.03 / 1.02 / 1.00 | 0.99 / 1.00 / 1.00 | 0.99 / 0.99 / 1.00 |

Forearm twist distribution (left arm, elbow 90 deg, arm hanging):

| Pose | hand twist about forearm (deg) | twist bone (deg) | ring rotation along the forearm, t = 0 elbow .. 1 wrist (deg) |
|---|---|---|---|
| wrist_twist_p90 | 89.7 | 54.0 | -0.01:-5, 0.05:0, 0.12:1, 0.21:1, 0.33:3, 0.46:14, 0.59:29, 0.70:42, 0.79:48, 0.87:50, 0.93:53, 0.97:60, 1.01:76 |
| wrist_twist_m90 | -89.7 | -54.0 | -0.01:-5, 0.05:0, 0.12:1, 0.21:1, 0.33:-3, 0.46:-14, 0.59:-29, 0.70:-42, 0.79:-48, 0.87:-50, 0.93:-53, 0.97:-60, 1.01:-76 |
| wrist_flex_60 | 0.0 | -1.6 | -0.01:-5, 0.05:0, 0.12:1, 0.21:1, 0.33:-0, 0.46:-0, 0.59:-1, 0.70:-1, 0.79:-1, 0.87:-1, 0.93:-1, 0.97:-2, 1.01:-3 |
| wrist_dev_30 | 1.4 | 1.5 | -0.01:-5, 0.05:0, 0.12:1, 0.21:1, 0.33:0, 0.46:0, 0.59:1, 0.70:1, 0.79:1, 0.87:1, 0.93:1, 0.97:2, 1.01:5 |

Reading: the hand's twist goes 60 % into LowerArmTwist (54.0 of 89.7 deg) and the skin rotates smoothly from 0 at the elbow to 76 deg
at the wrist ring; wrist ring area 0.87 (no candy-wrapper collapse). Wrist flexion 60 deg and deviation 30 deg leak only -1.6 and +1.5 deg
into the twist bone. See the rig / animation audit for how this constraint behaves under combined weapon-clip rotations (it does not
keep the 60 % share there).

## Renders (all in `E:\Model game khủng long\renders\characters\audit\`)

- `aud_sheet_arm_deform.png`: columns rest, arm overhead (170 deg + 15 deg clavicle shrug), arm side 90, arm forward 90, arm backward 45,
  elbow 90, elbow 130; top row front, bottom row side (arm forward: 3/4). Seen: the front of the shoulder pinches at arm forward 90
  and backward 45 (ring area 0.64 / 0.75); the inner elbow thins clearly at 130 deg (0.41); overhead stretches the deltoid under the
  fur pelt without tearing; no spikes, no stray vertices.
- `aud_sheet_wrist.png`: wrist twist +90, -90, flex 60, deviation 30 (front and top). The wireframe spirals gradually from mid forearm
  to the wrist; the wrist wrap ring stays intact.
- `aud_sheet_clip_poses.png`: Sword_Attack_1 f8/f14, Bow_Draw f26, Attack_Spear f12, Run f0/f5, Crouch f0, Crouch_Walk f10, Jump f7/f11,
  Land f3, Gather_Plant f0/f12/f34, Walk f0, Idle f0 (rows 1 and 3: front 3/4; rows 2 and 4: behind-above, like the game camera).
  Knee rings keep 0.72-0.75 of their area in the crouch and gather squats; Attack_Spear f12 compresses the left shoulder ring to 0.71.

## What can be repaired without restarting

Nothing is required for the jerk / twist problem. Optional quality work: soften the twist -> hand hand-over over one more loop
(250-300 mm) and, if the extreme poses matter, add elbow / knee corrective shapes or volume helper bones (not needed for locomotion).
