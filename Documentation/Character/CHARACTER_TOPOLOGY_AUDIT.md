# Player character topology audit (Blender side)

Audit date: 2026-09-28. Read-only: all measurements were taken in background Blender 5.2.1 processes on a copy of
`E:\Model game khủng long\characters\PLAYER_Survivor_v2_work.blend` (copy at `renders\characters\audit\_work\audit_copy.blend`).
The original file was never modified or saved; the owner's interactive Blender stayed clean (`is_dirty = False` checked before and after every call).

## Verdict

Topology is **not** a cause of the jerky / twisting look. LOD0 is a clean, 98.2 % quad MakeHuman-derived body with
no n-gons and no triangles inside any joint window, and at least 3 edge loops across every limb joint. LOD1 and LOD2 are
decimated triangle meshes (about 93 % triangles) that sit at 3 to 4 loops per joint diameter at the elbow, wrist and knee, with thin
triangles; that is acceptable for distance LODs but LOD0 must be the mesh used at the third-person gameplay distance (Unity LOD
settings are outside this audit).

## Mesh counts

| Mesh | Verts | Tris | Faces (quads / tris) | Skin verts (M_Player_Skin) | Hair verts | Eye verts | Cloth verts | Interior poles (valence: count) | Non-manifold edges |
|---|---|---|---|---|---|---|---|---|---|
| PLAYER_Survivor_LOD0 | 60,236 | 72,952 | 36,148 / 656 | 13,380 | 39,692 | 2,980 | 4,252 | 3: 222, 5: 210, 6: 10, 8: 56, 32: 4 | 0 |
| PLAYER_Survivor_LOD1 | 10,212 | 17,107 | 1,189 / 14,729 | 4,818 | 2,925 | 444 | 2,070 | 5: 1,747, 6: 3,201, 7: 1,500, 8: 416, other: 87 | 0 |
| PLAYER_Survivor_LOD2 | 3,344 | 5,668 | 366 / 4,936 | 1,740 | 932 | 0 | 702 | 5: 641, 6: 945, 7: 482, 8: 183, other: 47 | 0 |

Notes:
- Hair cards are 66 % of LOD0's vertices (39,692) and 22 % of its faces; the skin itself is 13,380 verts. Hair is open geometry
  (all 39,568 boundary verts of LOD0 are hair / cloth; the skin surface has 0 boundary edges, it is closed).
- Radial "star" poles are visible at the nipples and the navel in `aud_sheet_arm_deform.png`; the 4 valence-32 poles are
  consistent with those spots. Joint windows contain at most 4 poles, except the finger MCP knuckles (5-14) and the ball of the
  foot (26, toe roots).
- LOD1 / LOD2 are almost fully triangulated, so pole counts there only reflect triangulation.

## Edge loops across the joints (LOD0 measured, LOD1/2 estimated)

Method (script `aud_inventory.py`): for each joint, skin vertices on that limb within one limb diameter around the joint centre
(window = +-local radius along the limb axis) were taken. "Closed loops" = connected chains of edges running more than 50 deg from the
limb axis that close around the limb (largest angular gap < 60 deg). "Axial spacing" = median axial length of the edges that run
along the limb (within 35 deg of the axis); "loops per diameter" = 2 x radius / spacing + 1. The closed-loop detector is strict
(it misses loops that run diagonally or are cut by the window, e.g. at the shoulder, hip and finger knuckles), so both numbers are given.

| Joint (L; R identical) | LOD0 radius (mm) | LOD0 window (mm) | LOD0 closed loops found in window (offsets mm) | LOD0 axial spacing (mm) | LOD0 loops per diameter (est.) | LOD0 poles / tris / n-gons in window | LOD1 spacing (mm) / loops per diam. / thin tris (worst aspect) | LOD2 spacing / loops per diam. / thin tris (worst) |
|---|---|---|---|---|---|---|---|---|
| shoulder_L | 72.3 | 144.6 | 1 (43.4) | 19.4 | 8.5 | 4 / 0 / 0 | 26.1 / 6.5 / 7 (27.2) | 39.7 / 4.6 / 7 (16.5) |
| elbow_L | 45.3 | 90.6 | 5 (-33.1, -17.8, -3.3, 12.9, 33.0) | 16.0 | 6.7 | 0 / 0 / 0 | 28.9 / 3.9 / 13 (13.1) | 40.5 / 3.4 / 10 (15.1) |
| forearm_mid_L | 38.4 | 76.7 | 2 (-11.3, 24.6) | 34.7 | 3.2 | 0 / 0 / 0 | 37.5 / 3.2 / 27 (21.4) | 33.7 / 3.1 / 17 (15.9) |
| wrist_L | 24.3 | 48.5 | 3 (-7.4, 3.1, 12.9) | 11.0 | 5.4 | 0 / 0 / 0 | 18.1 / 3.8 / 32 (22.7) | 25.8 / 3.1 / 9 (23.6) |
| hip_L | 114.3 | 228.5 | 2 (66.9, 78.0) | 21.3 | 11.7 | 2 / 0 / 0 | 29.7 / 8.6 / 11 (9.8) | 53.3 / 5.4 / 12 (9.2) |
| knee_L | 57.2 | 114.3 | 3 (-27.3, -3.1, 20.6) | 24.6 | 5.6 | 0 / 0 / 0 | 31.8 / 4.5 / 2 (16.8) | 60.8 / 3.1 / 1 (7.9) |
| ankle_L | 44.5 | 88.9 | 3 (-21.3, 6.7, 30.7) | 13.1 | 7.8 | 2 / 0 / 0 | 20.9 / 5.2 / 17 (13.3) | 31.3 / 3.8 / 10 (18.4) |
| ball_L | 33.1 | 66.2 | 1 (-22.0) | 3.4 | 20.3 | 26 / 0 / 0 | 11.2 / 6.3 / 15 (44.2) | 18.4 / 4.5 / 7 (40.6) |
| Thumb_01_L | 17.8 | 35.6 | 0 () | 8.6 | 5.1 | 1 / 0 / 0 | 13.5 / 3.6 / 6 (13.5) | - |
| Thumb_02_L | 14.8 | 29.5 | 0 () | 7.4 | 5.0 | 1 / 0 / 0 | 17.4 / 2.9 / 0 (5.2) | 20.0 / 2.8 / 4 (13.5) |
| Thumb_03_L | 13.6 | 27.2 | 0 () | 2.8 | 10.7 | 0 / 0 / 0 | 11.2 / 3.6 / 1 (6.3) | 12.6 / 3.3 / 3 (10.3) |
| Index_01_L | 15.3 | 30.7 | 0 () | 4.1 | 8.5 | 14 / 0 / 0 | 13.7 / 3.5 / 6 (7.1) | 18.1 / 2.4 / 5 (13.5) |
| Index_02_L | 13.1 | 26.3 | 0 () | 3.1 | 9.4 | 0 / 0 / 0 | 11.0 / 3.3 / 9 (9.5) | 9.6 / 3.4 / 3 (11.1) |
| Index_03_L | 11.1 | 22.2 | 3 (-0.1, 1.9, 4.5) | 2.3 | 10.8 | 0 / 0 / 0 | 9.2 / 3.3 / 7 (9.5) | 11.5 / 3.1 / 4 (11.1) |
| Middle_01_L | 7.2 | 14.5 | 0 () | 6.1 | 3.4 | 5 / 0 / 0 | 13.5 / 3.0 / 1 (8.1) | - |
| Middle_02_L | 13.1 | 26.1 | 0 () | 3.8 | 7.9 | 0 / 0 / 0 | 12.7 / 3.0 / 2 (9.7) | None / None / 4 (8.8) |
| Middle_03_L | 9.5 | 18.9 | 0 () | 2.5 | 8.5 | 0 / 0 / 0 | 9.2 / 3.5 / 4 (10.9) | 10.8 / 3.4 / 6 (12.0) |
| Ring_01_L | 9.6 | 19.1 | 0 () | 6.7 | 3.9 | 7 / 0 / 0 | None / None / 0 (4.7) | - |
| Ring_02_L | 11.7 | 23.4 | 1 (-0.4) | 2.6 | 10.1 | 0 / 0 / 0 | 7.7 / 4.2 / 17 (14.7) | None / None / 6 (16.7) |
| Ring_03_L | 9.4 | 18.9 | 0 () | 2.7 | 7.9 | 0 / 0 / 0 | 5.8 / 5.0 / 22 (14.7) | - |
| Pinky_01_L | 10.2 | 20.5 | 0 () | 4.6 | 5.5 | 7 / 0 / 0 | 10.8 / 3.6 / 4 (9.5) | 13.4 / 2.9 / 2 (10.6) |
| Pinky_02_L | 8.9 | 17.7 | 5 (-3.5, -1.1, 1.5, 4.0, 6.9) | 2.6 | 7.9 | 0 / 0 / 0 | 11.1 / 2.7 / 6 (7.1) | None / None / 0 (5.6) |
| Pinky_03_L | 7.8 | 15.7 | 4 (-2.8, -0.8, 1.2, 3.4) | 2.1 | 8.4 | 0 / 0 / 0 | 7.1 / 3.4 / 8 (9.5) | - |

Reading: joints with fewer than 3 supporting loops: **none on LOD0**. Elbow 5 closed rings of 20 verts within +-45 mm
(spacing 16 mm), wrist 3 within +-24 mm, knee 3 within +-57 mm, ankle 3 within +-44 mm; shoulder and hip have 19-21 mm spacing
(8-12 loops across one diameter). Finger PIP/DIP joints have 2-4 mm spacing (8-11 loops per diameter). The MCP knuckles
(Index_01, Middle_01, Ring_01, Pinky_01) carry 5-14 poles in their windows because the palm topology merges into the fingers there;
this is normal for a MakeHuman hand. LOD1 / LOD2 fall to about 3-4 loops per diameter at elbow, wrist and knee (LOD2 knee 3.1,
wrist 3.1, elbow 3.4) with thin triangles up to aspect 22-44 (ball of foot 44, shoulder 27, wrist 23).

## Shoulder / scapula density (LOD0 skin)

| Region (sphere) | Verts | Median edge (mm) | Max edge (mm) |
|---|---|---|---|
| Shoulder joint, r = 8 cm | 58 | 22.1 | 44.1 |
| Scapula (back), r = 7 cm | 29 | 23.0 | 34.7 |
| Upper arm mid, r = 6 cm | 19 | 20.9 | 28.4 |
| Chest front, r = 8 cm | 50 | 17.7 | 24.5 |

The shoulder / scapula area is about 25 % coarser than the chest front but has no poles in the joint window other than 4
(valence 3/5), no triangles and no n-gons. It deforms within normal linear-skinning limits (see the weight audit and
`aud_sheet_arm_deform.png`). The fur pelt (cloth material) covers the left shoulder.

## Renders

All in `E:\Model game khủng long\renders\characters\audit\`: `aud_sheet_arm_deform.png` (wireframe over the shaded LOD0,
rest and arm test poses) shows the loop flow at the shoulder, elbow and wrist. Individual frames are in `_work\aud_*.png`.

## What can be repaired without restarting

Nothing is required on LOD0. Optional: if LOD1 is ever visible at gameplay distance, re-decimate it with a quad-preserving method or
protect the elbow / knee / wrist loops so they keep 4-5 loops per diameter.
