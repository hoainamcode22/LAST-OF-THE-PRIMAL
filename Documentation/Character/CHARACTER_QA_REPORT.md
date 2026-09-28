# CHARACTER QA REPORT (audit phase), 2026-09-28

What was measured for the character audit, how, and what was not. No repair has been made yet, so there is no
before / after; these numbers are the "before" baseline for the repair plan.

## 1. Evidence

| Area | How | Result | Files |
|---|---|---|---|
| Owner's recording | ffmpeg frames: 2 fps overview, 10 fps crops of run, turn, gather start / end, walk | V1-V9 listed | `_video_findings.md`, frames in the Lead's scratch folder |
| FBX = .blend | FBX re-imported into an empty Blender, 16 clips measured with the same code as the .blend | identical to 3 decimals, rest axes within 0.03 deg | `_blender_rig_anim_audit.md` 1 |
| Topology | loop counting around joints, poles / n-gons / thin triangles at joints, counts per LOD | no joint under 3 loops, 98.2 % quads on LOD0 | `CHARACTER_TOPOLOGY_AUDIT.md` |
| Weights | influences, normalization, unweighted verts, gradients, twist split, section area under bends | no faults | `CHARACTER_WEIGHT_AUDIT.md`, `aud_sheet_arm_deform.png`, `aud_sheet_wrist.png` |
| Bones | hierarchy, axes, rolls, mirror check (25 pairs), constraints, bake check | clean in Blender | `_blender_rig_anim_audit.md` 2 |
| Clip content | per-frame arm / wrist / pelvis / chest / foot metrics on 43 non-weapon + weapon clips, jerk and seam checks | see RIG_AUDIT R2-R6, R11 | `_blender_rig_anim_audit.md` 3, `aud_sheet_run_cycle.png`, `aud_sheet_walk_cycle.png`, `aud_sheet_gather_plant_cycle.png` |
| Avatar / retarget | probe E: node rest vs bind pose, mirror, zero-muscle pose, 240 Hz sampling, 21-step muscle blends | left arm T-pose broken; flips; fist spin 350-360 deg | `_unity_integration_audit.md` R1, `motion_probe_avatar.*`, `_cmp_tpose.png`, `_cmp_runflip_L.png` |
| Controller / drivers / IK | probe A (raw clip vs controller), B / C / D gameplay with IK on / off / twist driver off | controller adds 0.01 deg; IK under 1 deg on arms; timing faults R5-R10 | `motion_probe_*.csv` |
| Motion targets | web research, 74 sources opened (GK marked) | per-clip target table | `_motion_principles.md` |
| Regression | full PlayMode suite after the audit files and the water work were deployed | 68 passed, 0 failed, 6 skipped (probe + capture tests, off by default) | `Documentation/Tests/playmode_results.xml` |

## 2. Rig diagnostics checklist (master task phase 17)

| Check | Result |
|---|---|
| Unweighted vertices | 0 (all LODs) |
| Bad bone hierarchy | none; LowerArmTwist is a child of LowerArm (intended) |
| Incorrect bone names | none for Unity (humanoid slots mapped); spec names differ, mapped in RIG_AUDIT 3, not renamed |
| Abnormal weights | none; max 4 influences |
| Broken constraints | none; `TwistFromHand` works but copies Euler Y, not a true twist (R12) |
| Disconnected bones | Pelvis, Clavicles, Thighs, LowerArmTwist, Weapon bones are unconnected by design |
| Non-normalized weights | 0 |
| **Avatar T-pose** | **broken on the left arm (R1)**: the one real rig-level fault, on the Unity side |

## 3. Not tested / limits

- Nobody played the character by hand for this audit; the probe drives input by script.
- V7 "both arms flung out" was not reproduced as joint abduction; the probe shows the left-fist spin plus a 500 deg/s body spin instead.
- The code behind the 136-308 ms hitch at the first gather hit was not isolated.
- V9 (camera through a tree) was not tested.
- The YouTube reference could not be opened (rate limited); principles come from the sources listed in `_motion_principles.md`.
- Blender-side bounce and COM numbers for walking are general knowledge (medical sources blocked by captcha).
