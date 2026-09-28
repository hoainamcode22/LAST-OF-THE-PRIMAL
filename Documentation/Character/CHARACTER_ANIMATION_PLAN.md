# CHARACTER ANIMATION PLAN (repair plan), 2026-09-28

Status: **proposed, not started.** The owner asked for an audit first; nothing below has been applied.
Targets come from `_motion_principles.md` (sourced; GK = general knowledge). Every step has a measurable acceptance check
run with `CharacterMotionProbe` (Unity) and the `aud_*.py` scripts (Blender), before and after.

Owners: **Unity animation agent** (avatar, controller, drivers, IK), **Character agent** (Blender clip generators, rig bake,
export), **Gameplay agent** (unarmed combat logic and input only), **QA agent** (probe, PlayMode suite, captures). The Lead
reviews each report and merges; no agent edits another agent's files.

## Phase A: fix the foundation (no new clips) — biggest effect first

| Step | What | Owner | Files | Acceptance |
|---|---|---|---|---|
| A1 | **Rebuild the avatar T-pose from the skin bind pose** and pick the arm direction by side (L = -X, R = +X), not `Mathf.Sign(dir.x)`. Blender complement: export with the armature in Rest Position so FBX node transforms equal the bind pose. Re-import; clips re-encode automatically. | Unity + Character | `PrimalCharacterBuilder.cs` (ConfigureImporter T-pose block), `PLAYER_Survivor.fbx.meta`, `pf_char_export.py` / `pf_player_ship.py` | probe E: L/R mirror equal on all arm bones; zero-muscle pose symmetric; bind round trip under 2 deg; **no 177 deg flips at 240 Hz**; left fist path in gather blends under 20 deg (same as right); TwistBoneDriver input near 0 in Idle. Character test PASS, held-weapon captures unchanged. |
| A2 | Gather / action timing: entry 0.35 s, exit 0.45 s (until A-clips exist); movement and turning only after the exit blend is 60 % done; never start the action above 0.5 m/s (brake first). | Unity | `PrimalCharacterBuilder.cs` (transition durations), `PlayerAnimationDriver.cs` (IsBusy / CanMove) | hips drop no faster than 1.2 m/s; no body yaw during the first 60 % of the exit |
| A3 | Focus turn eased (SmoothDampAngle, ~200 deg/s max) and only after the motor stops steering; find and prewarm what the first gather hit loads (136-308 ms hitch). | Unity | `PlayerInteraction.cs`, first-hit path (`ResourceNode.Hit`, feedback, HUD) | no frame over 50 ms at the first hit; no single-frame yaw step over 6 deg |
| A4 | Separate Idle from the time-synced Locomotion tree (Idle state + Walk / Run / Sprint tree). | Unity | `PrimalCharacterBuilder.cs`, controller | step cycle at Speed 0.7 within 20 % of Walk's cycle |
| A5 | Turn rate by speed (walk ~360, run ~300 deg/s, sprint ~200); on reversals over 135 deg rotate the velocity instead of braking through zero; later a pivot clip (C2). | Unity | `PlayerMotor.cs` | foot slide during a 180 deg reversal under 0.1 m; no yaw rate over the cap |
| A6 | IK-1 (pelvis drop) and IK-2 (look easing) from `CHARACTER_IK_PLAN.md`. | Unity | `PlayerIK.cs`, `PlayerMotor.cs` (step-up) | see IK plan |

## Phase B: fix clip content (Blender generators, rebake, export)

| Step | Clip(s) | Change | Target (from the principles doc) | Acceptance (Blender audit script) |
|---|---|---|---|---|
| B1 | Run, Sprint | Keep the elbow near 85-90 deg (elbow_amp 20 -> 0-8); arm_amp Run 42 -> ~30, Sprint 60 -> ~45; fore-aft path instead of vertical | elbow 80-100 deg; hand front peak at lower-sternum height, back peak at the hip; abduction 10-25 deg; hands never cross the sternum line | wrist front peak 0.15-0.25 m below the shoulder joint; fore-aft travel > vertical travel |
| B2 | Run, Sprint, Walk | Remove / flip the clavicle term that cancels chest yaw; move yaw from the pelvis to the chest | run: pelvis yaw ROM 10-16 deg, thorax counter-rotation ROM ~20-25 deg, head under 6 deg; walk: pelvis +-4, thorax +-4 opposite | shoulder line yaw in the world +-10-12 deg opposite the pelvis |
| B3 | Run, Run_Backward | Pelvis lowest at mid-stance (plant the foot under the hip / bent-knee contact solve) | bob 6-9 cm, low at "down" (just after contact), high in flight | pelvis minimum within 2 frames of mid-stance |
| B4 | Gather_Plant | Remove the frames 12-17 hold, ease in-out, lower the toss (+35 -> ~+10 deg), keep the right fist roll under 15 deg | no spikes; overlap 1-2 frames | max angular speed under 300 deg/s |
| B5 | Weapon clips | Continuity term in `pf_weapon_ik` (IK-5); true-twist bake | forearm roll under 10 deg per frame | no wrist step > 25 deg per frame |
| B6 | Get_Up, Wake_Up, Death, Climb_End | FK / IK blend over 4-6 frames | no pops | max angular speed under 600 deg/s |
| B7 | All locomotion | Asymmetry pass (L/R steps differ by 1 frame or a few deg), head stabilization, breathing in Idle, 1-2 frame overlap on arms / head | avoid perfect mirroring; secondary motion | visual review sheet + numbers unchanged within targets |

## Phase C: new clips

| Step | Clips | Notes |
|---|---|---|
| C1 | Gather_Enter / Gather_Exit (stand to squat, squat to stand, 10-14 frames); same for Gather_Stone / Gather_Wood if they start in pose; Collect_Water (kneel, scoop) and Butcher (kneel, cut) | removes V4 / V7 at the source; A2 blends then shrink back to 0.15 s |
| C2 | Walk_Start, Walk_Stop, Run_Start, Run_Stop (left / right foot variants), Run_Pivot_180, Turn_180 in place | motor acceleration 10 m/s^2 implies a 45 deg lean; lower the walk / run acceleration to what the start clips show, or select start clips by speed |
| C3 | **Unarmed set:** Unarmed_Idle (relaxed guard), Punch_L (jab), Punch_R (cross), Punch_Heavy, Kick, Unarmed_Block | jab contact at frame 4-6, 12-16 frames total; heavy contact at frame 8-11, 24-30 total; kick contact at frame 9-13; kinetic chain legs -> hips -> torso -> shoulder -> arm; guard hand stays at the chin. Gameplay agent adds the attack when the hand is empty (today `PlayerCombat` returns when no item is held), low damage, stamina cost, hitbox on the hand / foot bone, block reuse. |
| C4 | Bow_Equip, Bow_Idle, Bow_Nock, Bow_FullDraw (hold), Spear_Idle, Spear_Recovery | complete the weapon sets listed in the master task |

Clips that already exist and only need review (Phase B / D): Idle, Walk, Run, Sprint, Turn_Left / Right, Walk_Left / Right,
Walk_Backward, Strafe_Run_L / R, Run_Backward, Jump, Fall, Land, Dodge, Hurt, Hurt_Heavy, Sword_* (Equip, Idle, Attack_1-3,
Heavy, Block, Unequip), Bow_Aim / Draw / Release, Attack_Spear, Spear_Attack_2, Attack_Spear_Heavy, Throw_Spear, Pickup,
Gather_Plant / Stone / Wood, Eat, Drink, Craft, Build, Climb_Start / Up / Down / Idle / End, Harvest_Fruit.

## Phase D: Unity integration decisions

- **Root motion: stays off.** The capsule motor moves the character (responsive input, predictable collisions, existing tuning,
  tests depend on it). Clips stay in place and are authored at the motor speeds (walk 1.35, run 3.8, sprint 6.2 m/s; speed
  scaling kept within 15-20 %). Start / stop / pivot clips are chosen to match the motor's acceleration curve instead of
  driving it. Revisit only if start / stop clips cannot hide the lean mismatch.
- Transitions for player input: no exit time; locomotion blends 0.15-0.25 s; actions 0.1-0.15 s once enter / exit clips exist.
- Masks: UpperBody for aim / bow / carry; HitReaction additive stays.
- Every change goes through `PrimalCharacterBuilder` (the controller is generated), never by hand in the Animator window.

## Phase E: QA (rig test scene and evidence)

- A capture tool (edit mode, no Play) lays out the 13 test poses of the master task (Idle, Walk, Run, Sprint, Turn, Jump,
  Land, Punch, Sword, Bow, Spear, Gather, Climb) and renders a sheet, before and after each phase.
- `CharacterMotionProbe` A-E rerun after each phase; numbers go into `CHARACTER_QA_REPORT.md`.
- Full PlayMode suite green after each phase; character test (`BuildAndTest`) PASS.
- Performance: bone count (57), vertex count, materials and skinned meshes stay the same; no brute-force polygons.

## Order

A1 first (it breaks every left-arm measurement), then A2-A6 and B1-B4 in parallel (Unity agent and Character agent work in
different files), then C1-C3, then B5-B7 and C4. Unarmed combat (C3) needs the gameplay agent after the clips exist.
