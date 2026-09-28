# CHARACTER RIG PLAN (model, topology, rig, weights)

Owner role: Character Model / Rig / Animation specialist (project agent `technical-artist`; the Blender work runs
through the single Blender connection, so it is done by the lead in that role, one step at a time).
Source: `E:\Model game khủng long\characters\PLAYER_Survivor_v2_work.blend` (rig `PLAYER_Survivor_Rig`, meshes
`PLAYER_Survivor_LOD0/1/2`, body source `PLR2_Body`, sculpt `PLR2_Body_hi`). Every change keeps a backup `.blend`.

## 1. Topology evaluation (evidence: `renders/characters/audit_deform_sheet.png`)
| Zone | Test | Result | Decision |
|---|---|---|---|
| Elbow | flex 110 degrees | volume kept, slight inner thinning | keep topology, soften weights |
| Wrist | flex 60 | fine | keep |
| Wrist | twist +-80 | all twist at the wrist, forearm rigid | **add forearm twist bones** (deformation fix, not polygons) |
| Shoulder | arm raise 90 | deltoid / chest collapse, pelt tears | **re-weight shoulder** (clavicle / upper arm / chest falloff) + pelt weights; add a corrective shape only if weights are not enough |
| Hip, knee, ankle | crouch / climb / run poses | acceptable in renders | keep |
| Neck, spine | look / lean | acceptable | keep |
Polygon count is not the problem (body 13.4k verts, LOD0 72.9k tris incl. hair / clothes). No global subdivision.

## 2. Rig changes
- Add **LowerArmTwist_L/R** (child of LowerArm at 50 % of the forearm). Deforming, not humanoid-mapped. Blender
  constraint (Copy Rotation from the hand, Y only, local, influence 0.6) so Blender renders match; in Unity the
  **TwistBoneDriver** sets them each LateUpdate from the hand twist (60 %). *Done 2026-09-28 (`scripts/pf_rig_upgrade.py`).*
  UpperArmTwist was not added: the shoulder problem was solved by weights (see the deformation sheet).
- Keep bone names, hierarchy and rolls of the existing humanoid chain (animation and the avatar depend on them).
- Unity avatar: lower-arm twist set to 0 once twist bones exist (the driver does the spreading; default 0.5 would also
  roll the elbow).

## 3. Weights
- Forearm: gradient LowerArm (elbow) -> LowerArmTwist (mid) -> Hand (wrist band); wrist ring 50/50 twist / hand.
- Upper arm: UpperArm near the shoulder -> UpperArmTwist mid -> LowerArm at the elbow ring.
- Shoulder: Clavicle carries the top of the trapezius and the pectoral edge, UpperArm the deltoid, Chest the armpit
  front; smooth 3-ring falloff; pelt follows Clavicle / Chest / UpperArm with the same falloff as the skin under it.
- Keep max 4 influences, normalized (checked today: LOD0/1/2 pass).

## 4. Hands (the "robot hand" look)
- Rest / relaxed hand pose for animation: fingers curled 15-35 degrees increasing to the pinky, thumb relaxed toward the
  index, wrist neutral (palm toward the thigh). Done in the clip authoring (see ANIMATION_PLAN), not in the mesh.

## 5. Export and Unity
- Export LOD0/1/2 with the twist bones; `PrimalCharacterBuilder.BuildAndTest("Player")` must PASS (skin, loops, foot
  slide, facing). Twist driver added by the builder; avatar twist setting updated.

## 6. Acceptance
- Deformation sheet redone: wrist twist spread along the forearm, shoulder raise without collapse, pelt without tearing.
- Character test PASS; clips contact sheet shows relaxed hands.
