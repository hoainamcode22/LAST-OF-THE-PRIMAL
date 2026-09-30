# CHAR (C_) report, Phase 1 wave 1 (2026-09-30)

Scope: bare-hand and survival player clips in Blender, staging FBX + manifest for U (wave 2). No Unity, no git, nothing
deleted, the interactive Blender never touched. Mesh, rig, bone names, weights and rest pose unchanged (57 bones, 3 LODs).

## 1. Starting state (checked before any write)

- The staged FBX of 04:54 had **85 clips, not 64**: re-imported in an empty scene, 85 takes, names and frame ranges equal
  to `clips_manifest.json`, and equal to the 85 actions of `PLAYER_Survivor_v2_work.blend` (saved 04:52). So it already
  reflected the latest work file.
- 11 of the 13 requested clips already existed there as real clips from the earlier CHAR pass (Punch_1/2/3, Heavy,
  Combo_End, Unarmed_Block, Gather_Branch, Gather_Stone_Hand, Drink_Kneel, Collect_Water, Bandage_Use). Unity plays
  placeholders only because U has not imported that FBX yet. Missing: **Kick**, **Unconscious**.

## 2. What changed

| Clip | Frames | Loop | Events (frame) | Change |
|---|---|---|---|---|
| Kick | 26 | no | OnAttackStart 5, OnAttackActive 10, OnAttackHit 12, OnAttackEnd 15, OnFootstep R 22 | NEW. Rear-leg front push kick: weight onto the lead leg, rear heel peels off the ball, knee chambers to hip height, hips drive forward with the trunk leaning back, sole-first extension (knee ~90 % straight), push, re-chamber, foot lands back on its guard spot; support foot pivots out on its ball; rear hand swings back for balance. Contact 12 (0.46). |
| Unconscious | 150 | yes | none | NEW. Lying on the back, slow shallow breathing (12/min). Frame 0 = Wake_Up frame 0 (same pose), so the opening can hand over to Wake_Up. |
| Unconscious_Collapse | 56 | no | OnBodyFall "sit" 21, OnBodyFall 34 | NEW. Faint from Idle frame 0: head drops, arms go limp, knees buckle, sits down behind the heels, tips back onto the back (legs keep their world orientation, then slide out), head lands last with a small rebound; ends exactly on Unconscious frame 0. |
| BareHand_Heavy | 30 (was 28) | no | OnAttackStart 6, OnAttackActive 11, OnAttackHit 12, OnAttackEnd 15 | REWORKED to the requested overhand / haymaker: weight back, rear fist drawn up behind the ear, lead hand paws, rear heel pivots, hips then chest turn and bend over to the lead side, head slips off the line, fist arcs over the shoulder and down palm-down, follow-through across, slower recovery. Contact 12 (0.40). |
| BareHand_Heavy_Straight | 28 | no | Start 5, Active 9, Hit 10, End 13 | KEPT: the previous heavy rear straight, copied unchanged (curves bit-identical) under its own name. Not wired; optional. |
| Unarmed_Block, BareHand_Block | 40 | yes | none | FIXED: the breathing key was IK-solved separately and put the right elbow on another branch (right upper arm swung 25 deg, 56 deg/s, every loop). Now one arm solution held, breathing added on top: peak 3-4 deg/s, seam 0.0. Same forearm-guard pose. |
| Punch_1 / 2 / 3, Combo_End, Gather_Branch, Gather_Stone_Hand, Drink_Kneel, Collect_Water, Bandage_Use | 14 / 16 / 20, 24, 40, 40, 72, 64, 58 | as before | jab 1/4/5/7, cross 2/5/6/8, hook 3/6/7/9; OnGatherHit 16 / 17; OnScoop 27 + OnDrink 40; OnScoop 22 + OnDrink 34; OnUseItem 45 | UNCHANGED (reviewed on renders and measured, below; already real human motion with the events U expects). |

Total now 89 clips (85 + Kick, Unconscious, Unconscious_Collapse, BareHand_Heavy_Straight). 30 fps, in place, Root never
keyed (the collapse moves the pelvis 55 cm back as the body falls, like Death; the capsule stays).

## 3. Files written

| File | What |
|---|---|
| `E:\Model game khủng long\characters\PLAYER_Survivor_v2_work.blend` | baked work file (md5 b23d9b77...) |
| `E:\Model game khủng long\export\staging\PLAYER_Survivor.fbx` | all 89 clips, same export code / settings (`pf_player_ship.export_staging`, face_unity_forward, flat) (52.2 MB) |
| `E:\Model game khủng long\export\staging\clips_manifest.json` | 89 entries, existing format (events, contact, root_speed ...) |
| `E:\Model game khủng long\scripts\pf_clips_c.py` | `heavy_overhand`, `kick_def`, `build_unconscious` (group P1), block fix, old heavy kept as `BareHand_Heavy_Straight` def; knee-pole hook installed on the rig instance only (`pf_anim.py` untouched) |
| `E:\LAST OF THE PRIMAL\Tools\BlenderPipeline\C_tools\p1\` | runner, FBX dump / analysis / blend compare scripts, `verify.json`, `verify_out.txt`, copy of `pf_clips_c.py` |
| `E:\LAST OF THE PRIMAL\Documentation\Screenshots\Phase1\Character\C_P1_{kick,heavy_overhand,unconscious}.png` | review sheets (8 frames x 2 views each) |

Backups (made before any write): `characters\PLAYER_Survivor_v2_work_before_P1_20260930.blend` (md5 equal to the 04:52
file), `export\staging\_before_P1_20260930\` (old FBX + manifest), `scripts\pf_clips_c.py.before_P1`,
`pf_clips_human.py.before_P1`, `pf_player_ship.py.before_P1` (the last two were not changed). Temp transfer folder
`export\staging\_P1_upload\` (binary parts truncated to 0 bytes, not deletable).

How Blender ran: the MCP `_for_cli` tool has no Blender path on this PC and `blender` is not on the device PATH, so all
bakes, exports and checks ran in background bpy 5.2.1 (same version as the file) in the cloud container on staged
copies; results were copied back and md5-checked on the PC.

## 4. Checks (all on the exported FBX re-imported into an empty scene, unless noted)

- 89 clips, names = manifest, frame counts = manifest, every event inside its clip.
- Existing work kept: in the .blend, 82 actions are bit-identical to the 04:52 file (curves and meta); only
  BareHand_Heavy, Unarmed_Block, BareHand_Block changed; BareHand_Heavy_Straight = old BareHand_Heavy exactly. In the FBX,
  unchanged clips differ from the old FBX by at most 0.19 deg (export float noise; a loop seam that is 0.0 in the .blend
  reads 0.17 after the FBX round trip).
- Motion present (rotation range from frame 0, deg; pelvis / spine / chest / upper arm / forearm): Kick 34 / 14 / 8 / 117 / 75,
  thigh 85, calf 62; Heavy 43 / 22 / 19 / 88 / 71, pelvis travel 9 / 16 / 8 cm; Collapse 88 / 24 / 8 / 63 / 32, thigh 113.
- Planted feet (ball on the ground, slide per frame): Kick support ball 0.0 mm (heel pivots about the ball), kicking foot
  0.1 mm before lift-off and after landing; Heavy rear ball 2.7 mm max (ball pivot); Collapse 0.0 mm both feet while
  standing / buckling; Punch_1/2/3 <= 0.8 mm; Drink_Kneel / Collect_Water <= 2.1 mm; Gather_*, Bandage_Use, blocks 0.0.
- Peak joint speeds (Blender audit): Kick calf 1324 deg/s at the snap, Heavy forearm 1871, Collapse foot 834; no flips
  (an early Kick version had a knee-plane flip when the leg pointed along the forward pole: fixed with a forward-up pole
  for the leg in the air).
- Collapse: no ground penetration (heels / toes kept above z = 0; body lifted at most 4 cm for 3 frames while the heels
  drag). Unconscious f0 vs Wake_Up f0: 0.17 deg, pelvis 0 mm; Collapse last frame vs Unconscious f0: same.

## 5. Requests to U (wave 2)

1. Import the staging FBX + manifest (89 clips). Kick is picked up by `PrimalCharacterBuilder` (PhaseCClips, `PA.Kick` via
   `Opt`), heavy / punches / gathers / drinks / bandage replace their placeholders.
2. `Unconscious` state: may use the new `Unconscious` loop instead of frozen Wake_Up frame 0 (same first frame).
3. `Unconscious_Collapse` (knockout) needs a state + trigger if wanted; `PlayerFacial` closes the eyes only for the state
   name "Unconscious", so add the collapse there too.
4. BareHand_Heavy is 30 frames now (contact 0.40; its events are in the manifest, the 0.42 default is unused).

## 6. Not done / open

- The shared lying pose (Wake_Up f0, Death end, Get_Up start, now Unconscious) lies with the legs slightly raised (left heel
  ~19 cm, right ~5 cm above the ground, toes up). Left as is so the opening hand-over to Wake_Up stays exact; fixing it
  means rebaking those four clips together (Lead decision).
- Kick is rear-leg only (no lead-leg variant); the controller can mirror if needed.
- No Unity import, no in-game test (by design).
