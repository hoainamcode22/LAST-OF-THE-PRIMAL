# CHAR (C_) report: player clips in Blender (PC phase), 2026-09-30

Scope: `PC_ROUND_OWNERSHIP.md` row CHAR, directive 8, 10, 18-21 (19 = high priority: move like a real human), plan B1-B7 and
C1-C3 of `Character/CHARACTER_ANIMATION_PLAN.md`, Phase 3.5 items 1-4 and 39-40. No Unity work (editor closed).
Original motion from principles only (`_motion_principles.md`): procedural keys, IK and measured targets, no copied clips,
no mocap.

## 0. Result and where it is

| Item | Where |
|---|---|
| Staging FBX, all 85 player clips, same FBX settings as `pf_player_ship.export` | `E:\Model game khủng long\export\staging\PLAYER_Survivor.fbx` (49.9 MB, 04:54 UTC) |
| Manifest (per clip: name, frames, fps, loop, speed, events, notes, root_speed, root_distance; transitions also root_yaw, starts_in, ends_in) | `...\export\staging\clips_manifest.json` (rewritten after each batch; last one covers every clip) |
| Work file (baked, saved) | `characters\PLAYER_Survivor_v2_work.blend`; my dated backup `PLAYER_Survivor_v2_work_before_C_20260930.blend` + `scripts\_backup_before_C_20260930\` |
| Generators | `scripts\pf_clips_c.py` (new), `pf_clips_human.py`, `pf_clips_weapons.py`, `pf_player_ship.py`, `pf_char_export.py` (changed); copies in `E:\LAST OF THE PRIMAL\Tools\BlenderPipeline\` (+ `C_tools\`: runner, audit, sheet renderer) |
| Measurements (raw JSON) | `renders\characters\C_work\res_before.json` (before), `res_deliver_final.json` (after, all 85 clips), `res_deliver_b1..b4.json` |
| Review sheets (looked at, fixed what looked wrong) | `E:\LAST OF THE PRIMAL\Documentation\Screenshots\PCPhase\Character\C_*.png` (16 sheets; `C_b1_run_BEFORE.png` = old Run) |
| Import steps for U / Lead | `NEXT_SESSION.md`, section "CHAR (C_) steps" (C1-C11) |

Verification of the export: the staged FBX was re-imported in a separate Blender (cloud bpy 4.2): 85 clips, same names as the
manifest, frame ranges equal to the manifest, Run / Walk pelvis curves identical to the .blend (Run lowest at frames 4 and 15 =
mid-stance). Every loop closes exactly (seam 0.0 deg on all loops). All event frames inside their clip.

Status tags: DONE-VERIFIED = measured with the audit (numbers below); DONE-NOT-TESTED = made and reviewed on sheets, not
testable without Unity; PARTIAL; NOT COMPLETED.

## 1. Batch 1: locomotion (highest priority)

New `Gait2` generator (in `pf_clips_c.py`): stance feet move with the belt (no skid), pelvis height = the bounce shape kept
exactly and lowered only as far as the contact legs need, rear heel rises as far as the leg needs at toe-off (pivot on the ball),
swinging foot folds up or is drawn in instead of being dragged by the IK clamp, asymmetry, counter-rotation with lags.

Before / after (left arm; `aud_anim` code; ranges peak to peak, deg / m):

| Clip | pelvis yaw | chest yaw | shoulder line yaw (world) | head yaw | pelvis z (bob cm) | elbow | wrist below shoulder at front | hand fwd / vert travel | hand to sternum min | abduction |
|---|---|---|---|---|---|---|---|---|---|---|
| Walk before | 12.0 | 5.9 | 0.8 | 5.5 | 0.942-0.986 (4.4) | 26-50 | 0.315 | 0.38 / 0.22 | 0.30 | 9-10 |
| Walk after | 8.0 | 8.9 | 9.6 | 2.4 | 0.924-0.963 (3.9) | 29-43 | 0.414 | 0.26 / 0.11 | 0.29 | 9-10 |
| Run before | 22.0 | 10.7 | 1.6 | 10.2 | 0.887-0.973 (8.6) | 88-108 | -0.007 (at shoulder height) | 0.20 / 0.39 | 0.26 | 10-13 |
| Run after | 13.0 | 20.1 | 23.7 | 4.1 | 0.867-0.928 (6.1) | 88-101 | 0.206 | 0.31 / 0.17 | 0.24 | 10-12 |
| Sprint before | 26.1 | 12.3 | 0.7 | 12.1 | 0.953-0.967 (1.4) | 94-110 | -0.072 (above) | 0.26 / 0.44 | 0.26 | 11-14 |
| Sprint after | 16.0 | 24.5 | 29.0 | 5.2 | 0.882-0.946 (6.4) | 85-125 | 0.149 | 0.36 / 0.23 | 0.23 | 8-11 |
| Run_Backward before | 10.0 | 5.0 | 0.3 | 4.5 | 0.865-0.936 (7.1) | 70-84 | 0.116 | 0.19 / 0.28 | 0.27 | 8-11 |
| Run_Backward after | 7.0 | 9.8 | 11.9 | 3.0 | 0.867-0.912 (4.5) | 88-96 | 0.190 | 0.18 / 0.14 | 0.26 | 11-12 |

| Step | Target | Result | Status |
|---|---|---|---|
| B1 arm arc (Run, Sprint, Run_Backward) | elbow 80-100 (run); wrist front peak 0.15-0.25 m below the shoulder; fore-aft travel > vertical; hands never cross the sternum; abduction 10-25 | Run elbow 88-101, wrist 0.206 m below, 0.31 fore-aft vs 0.17 vertical, hand never closer than 0.24 m to the sternum line, abduction 10-12; Sprint wrist 0.149 below, elbow closes in front (85) and opens behind (125) | DONE-VERIFIED |
| B2 shoulder counter-rotation | clavicle term removed; run pelvis ROM 10-16, thorax ~20-25 opposite, head < 6; shoulder line +-10-12 in the world; walk pelvis +-4 / thorax +-4 | clavicle now protracts WITH the arm (small); Run pelvis 13, chest 20.1, shoulder line 23.7 (+-11.9), head 4.1, hip / shoulder correlation -0.95; Walk pelvis 8 (+-4), chest 8.9, head 2.4 | DONE-VERIFIED |
| B3 run pelvis lowest at mid-stance | minimum within 2 frames of mid-stance, bob 6-9 cm | minimum 0.3 frame from mid-stance (Run), 0.0 (Sprint); bob 6.1 / 6.4 cm; peak in flight | DONE-VERIFIED |
| B7 asymmetry, overlap, head | L/R not mirrored, 1-2 frame overlap, head stabilised | right foot lands 0.4 frame early, step height 0.93, toe-out 3/6 deg, arm amplitude 0.92, right elbow +2 deg; elbow 2 f and wrist 3 f behind the shoulder, chest 1.5 f and head 3 f behind the pelvis; head yaw under 5 deg in every gait | DONE-VERIFIED |
| Motor speeds, cycles | walk 1.35 / run 3.8 / sprint 6.2 m/s; 32 / 20-22 / 18 f | Walk 32 f (stance 60 %), Run 22 f (stance 34 %, 7.5 f contact, 3.5 f flight), Sprint 18 f (4.7 f contact); stance travel = speed x stance time | DONE-VERIFIED |
| Foot contact | no sliding | ball of the foot 0.0 m/s while planted in Walk / Run / Sprint; one frame at heel strike 0.5-1.2 m/s (the heel touching as the foot lands, 2-4 cm); U's test uses the mean ball slide (passes) | DONE-VERIFIED (small heel-strike frame noted) |
| Idle | breathing, weight shift, head | 2 breaths / 6 s (20 per min), weight ~60/40 moving leg to leg (lateral 2.6 cm), pelvis tilted 3.9 deg to the unloaded side, feet asymmetric (right toe out 10, left 4, right foot 3 cm back), head drift 6.7 deg range (was 9.0), elbows 19-25 (not mirrored); stays 180 f for the LocoRate set-up | DONE-VERIFIED |
| Turn_Left / Turn_Right | head, chest, pelvis, feet together; steps | loops at 90 deg/s kept (blend tree unchanged); head leads 20 deg, chest 9, pelvis 3, arms trail; planted feet now orbit the body centre (were rotating on the spot = foot skid): ball slide 0.1-0.2 m/s on one frame only | DONE-VERIFIED |
| Walk_Start / Walk_Stop / Run_Start / Run_Stop / Run_Pivot_180 / Turn_180 (C2) | 1-2 steps, starts committed from f0-2, stop counter-lean and settle, pivot over 135 deg, head leads | world-planned feet in the capsule frame (planted feet fixed in the world, capsule path from the motor), crossfaded into the loop at the matching loop time. Walk_Start 18 f (one left step, 0 -> 1.35 m/s, 0.372 m, ends on Walk f0); Walk_Stop 22 f (right foot alongside, counter-lean 9 deg, arms swing past and settle, ends on Idle f0, no slide); Run_Start 14 f (lean 13 deg, right leg drives, left lands on Run f0); Run_Stop 26 f (brake step, counter-lean 13 deg, left alongside, no slide); Run_Pivot_180 20 f (turns left over the right foot's ball, head leads 29 deg, lean into the turn; capsule yaw 0 -> 180 over f0-18, speed dips to 55 % like the motor); Turn_180 34 f (head 46 deg ahead, chest 23, 3 steps, ball pivots, no crossing). Peak bone speeds 567-1238 deg/s, no pops | DONE-VERIFIED in Blender; the pairing with the motor is DONE-NOT-TESTED (Unity) |

Remaining slides in the transitions (capsule-frame audit, one to three frames): Walk_Start 0.14-0.28 m/s at the end blend,
Run_Start 1.5 m/s on the right toe-off frame and 1.1 on the left heel strike, Run_Pivot_180 1.2 m/s on the last left heel frame.
These are the crossfade frames into the loop; U's foot lock / foot IK covers them.

Left-foot / left-turn variants only: the controller's `LocoMirror` makes the right ones (as U wired it).

## 2. Batch 2: bare hands (Phase 3.5 items 1-4)

Keyed body poses + fist targets solved with `pf_weapon_ik` (fist grip point, knuckle direction, thumb side), targets measured
from the POSED shoulder and arm length (a first version with fixed coordinates left the jab at 116 deg elbow flexion: fixed).
Keys solved in order, each seeded from the previous one (one IK branch); the in-between refinement fades out near keys.

| Clip | Frames | Contact / events | Measured | Status |
|---|---|---|---|---|
| BareHand_Idle (loop) | 60 | none | lead fist 0.30 m in front of the lead shoulder at cheek height, rear fist at the chin, elbows in front of the ribs, chin down, breathing; hands move < 0.1 m/s | DONE-VERIFIED |
| BareHand_Punch_1 jab | 14 | Start 1, Active 4, Hit 5, End 7 | contact f5 (target 4-6), elbow flexion 31 deg at contact (~95 % extension, not locked), extends last (122 -> 97 at f4 -> 31 at f5), fist peak 5.3 m/s at f4, palm down, returns on the same line; rear fist stays at the chin (0.63 m/s max) | DONE-VERIFIED |
| BareHand_Punch_2 cross | 16 | Start 2, Active 5, Hit 6, End 8 | contact f6, elbow 31 deg, fist 7.5 m/s, rear heel lifts 26 deg and pivots on the ball, pelvis turns 28 deg, shoulder line 58 deg (1-2 f after the hips), lead fist back to the chin | DONE-VERIFIED |
| BareHand_Punch_3 lead hook (finisher) | 20 | Start 3, Active 6, Hit 7, End 9 | load on the lead leg, hips 34 deg / shoulders 66 deg, elbow ~90 deg at shoulder height, flat arc, lead foot pivots; fist 8.5 m/s; ends in the follow-through | DONE-VERIFIED |
| BareHand_Heavy | 28 | Start 5, Active 9, Hit 10, End 13 | wind-up f0-5 (weight back, coil 30 deg), hips then chest then elbow, contact f10 (target 8-11), elbow 14 deg at contact, rear heel pivot out 38 deg, weight to the lead leg, follow-through f13, slower recovery; pelvis 58 deg, shoulders 120 deg range; fist 12 m/s (fast; real ~8) | DONE-VERIFIED |
| BareHand_HitReaction | 18 | OnHurt "bare" at 0 | starts and ends exactly on the guard (additive layer), head snaps back, fists cover | DONE-VERIFIED |
| BareHand_Combo_End | 24 | none | starts on the exact solved last pose of Punch_3, fists drop, feet step (lifted) back to Idle frame 0 | DONE-VERIFIED |
| BareHand_Block and Unarmed_Block (loop, same content) | 40 | none | high guard: forearms vertical at the temples, chin down, knees soft, breathing | DONE-VERIFIED |

Peak bone speeds: jab 2237 deg/s (upper arm at the strike), cross 2134, heavy 2870 (upper arm between the load and the strike),
all inside the strike frames. Wrist: knuckles are aligned with the strike line at contact (knuckle target on the arm line); the
guard wrists sit 38-45 deg bent (thumb-up fists).

## 3. Batch 3: survival actions (directive 8, 10; Phase 3.5 14-16)

| Clip | Frames | Events | What it does | Status |
|---|---|---|---|---|
| Drink_Kneel | 72 | OnScoop "kneel" 27, OnDrink "kneel" 40 (U: 1.35 s) | step, kneel on the right knee, both hands cup and scoop, water to the mouth (hands 0.16 m from the head point), drink, shake off, stand; all hand keys within 6 mm | DONE-VERIFIED |
| Collect_Water | 64 | OnScoop "fill" 22, OnDrink "fill" 34 (U: 1.15 s) | kneel, container hand dips and holds while it fills, lifts to the chest, stand | DONE-VERIFIED |
| Drink_Container (new name) | 44 | OnDrink 20 | standing, container to the lips, head tilts back, lower | DONE-VERIFIED |
| Eat (rebuilt) | 48 | OnEat 16, 34 | hand to the mouth (0.10 m), bite, chewing nods ~2.3 Hz, second bite, lower | DONE-VERIFIED |
| Gather_Enter / Gather_Exit | 12 / 14 | none | stand <-> the Gather_Plant ready squat with lifted steps (0.4 / 0.47 s, never a 0.1 s drop); end exactly on Gather_Plant f0 / Idle f0; peak 627 / 481 deg/s | DONE-VERIFIED |
| Gather_Stone_Hand (loop from the ready squat) | 40 | OnGatherHit 17 | reach, grip, wrench free, pouch at the hip | DONE-VERIFIED |
| Gather_Branch (loop from the ready squat) | 40 | OnGatherHit 16 | both hands grip, snap with a twist, set aside | DONE-VERIFIED |
| Bandage_Use | 58 | OnUseItem "bandage" 45 | left forearm across the front, head looks at it, right hand wraps twice, pulls the knot, lowers | DONE-VERIFIED |
| B4 Gather_Plant | 48 | OnGatherHit 24 | hold removed (static frames 10 -> 2), pull eased in-out, toss abduction 51.6 -> 18.0 deg; peak 643 -> 422 deg/s, spike ratio 17.9 -> 4.6 (target: max under 300 deg/s NOT met, 422) | PARTIAL (the hold and the snap are gone; peak 422 deg/s over the 300 target) |

Hands at water / ground are placed at the nearest point the arm reaches from the posed shoulder; U's hand IK finishes the reach
onto the real surface (as the placeholder programs did).

## 4. Batch 4: climbing, B5, B6

| Item | Result | Status |
|---|---|---|
| Climb_Rock_Up / Climb_Rock_Down (loops, 36 f, 0.6 / -0.55 m/s) | rock face 0.30 m in front of the pivot; one hand and the opposite foot move while the other pair holds (held limbs slide down with the climb), hips sway to the holding side, Catmull-Rom keys (no stutter), Down = the solved Up poses reversed; seam 0.0; named `Climb_...` so the builder's vertical skip applies | DONE-VERIFIED (Blender); NOT TESTED with `PlayerClimb` |
| Climb_Rock_Idle (loop, 60 f) | hanging on the face, breathing | DONE-VERIFIED |
| Ledge_Mantle (54 f) | reach and grab the lip (OnClimbGrab 10), pull with the feet walking the face, press on straight arms, right knee over, stand; timing matches PlayerClimb (reach 0.35 s, pull ~0.8 s, over 0.6 s); lip keys 0.1 m from the target (U's hold IK sets the hands) | DONE-NOT-TESTED (needs a U state and the code-driven body motion) |
| B6 FK / IK pops (Get_Up, Wake_Up, Death, Climb_End, Climb_Start) | FK and IK legs are now blended over 5-10 frames (IK solved, then slerped towards the FK rotations; the FK ground snap blended in) instead of flipping in one frame. Peak: Get_Up 2495 -> 468 deg/s, Wake_Up 2453 -> 701 (arm, legs < 600), Death 3559 -> 646 (frame 0 impact; the switch region 313), Climb_End 2669 -> 807 (arm drop, the drop pose now carried into the crouch), Climb_Start 678. Death also started its fall from leg angles that did not match the kneel, which stood the body up before falling: fixed (legs start from the kneel angles and stay folded while the trunk tips back) | DONE-VERIFIED (target < 600 met on the legs; Wake_Up / Climb_End arms 701 / 807) |
| B5 weapon wrist flips | continuity in `pf_clips_weapons.bake`: in-betweens start between the joint blend and the previous frame, the refinement fades out near keys and when it would jump. Max wrist roll step per frame: Throw_Spear 164 -> 29, Sword_Equip 108 -> 53, Sword_Heavy 64 -> 36, Sword_Unequip 64 -> 41, Spear_Attack_2 39 -> 33, Attack_Spear_Heavy 32 -> 30, Sword_Attack_1 28 -> 27, Bow_Release 27 -> 26; worse: Attack_Spear 26 -> 37, Sword_Attack_2 34 -> 37, Sword_Attack_3 34 -> 35. Weapon key solutions unchanged (`wtwist` kept at +-95: narrowing to +-80 was tried and moved the sword key grips, reverted). The true-twist bake of LowerArmTwist was not done (U's TwistBoneDriver drives it at run time) | PARTIAL (worst flips removed; target <= 25 deg per frame not met on 9 clips) |

## 5. What U / the Lead must do (details in `NEXT_SESSION.md` C1-C11)

1. Back up the Assets FBX, copy the staging FBX and `clips_manifest.json` in, `BuildAndTest "Player"`, Console check, probe once.
2. New states needed for clips the controller does not know: `Drink_Container`, `BareHand_Block` (same content as the wired
   `Unarmed_Block`), `Climb_Rock_Idle / Up / Down` (RockFace kind), `Ledge_Mantle` (Ledge kind pull-over).
3. Start / stop / pivot / turn clips have `speed 0` in the manifest on purpose (the builder's foot-slide test assumes a constant
   belt speed); their real speed and yaw curves are in `root_speed` / `root_yaw`. Enable `useStartStopClips` after a hand check.
4. No new event names (all receivers exist). `CombatPolishTests.ExpectAdditiveLightHurt` must accept BareHand_HitReaction.
5. Walk 32 f (was 30), Run 22 (20), Sprint 18 (14); Idle stays 180.

## 6. Notes, honest

- Blender's automatic `.blend1` rotation replaced the old `PLAYER_Survivor_v2_work.blend1` (a 07:31 Sep 28 state) with the
  09:23 state on my first save (identical to both named backups). Later saves use `save_version = 0` (no rotation). Named
  backups were never touched.
- The interactive Blender had no file open; every bake ran in separate background processes started from it
  (`bpy.app.binary_path -b`), because the MCP CLI tool has no Blender path configured on this PC.
- Pelvis heights are 2-4 cm lower than before in Walk / Run (contact knee kept slightly flexed so the landing foot is always in
  reach; the old Run reached with a locked knee and bounced the wrong way).
- The ledge / rock-face clips assume U's pivot conventions (`faceOffset 0.34`, `reachHeight 2.0`); U's hold IK and code-driven
  body path will decide how they read in the game.
- Files not mine were not edited; no Unity, no git, nothing deleted, no processes killed.
