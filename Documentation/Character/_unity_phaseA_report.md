# Unity Phase A report (player animation), 2026-09-29

Scope: steps A1-A6 of `CHARACTER_ANIMATION_PLAN.md` (IK-1 and IK-2 of `CHARACTER_IK_PLAN.md`), and Phase C wiring for clips
that do not exist yet. Measured with `CharacterMotionProbe` A-F (PlayMode, off by default) and `PrimalAnimAudit`.
"Before" numbers are from `_unity_integration_audit.md`. "After" numbers are the final code, 2-3 probe passes (ranges).

## 1. Results per acceptance item

| Step | Acceptance | Before | After | Met |
|---|---|---|---|---|
| A1 | L/R mirror equal on arm bones (avatar T-pose) | UpperArm / LowerArm / LowerArmTwist 180 deg | 0 on every bone (clavicle to foot) | yes |
| A1 | zero-muscle pose symmetric | L elev 127 abd -52, R elev 60 abd 8 | L and R elev 53.0 abd 41.4 | yes |
| A1 | Idle arm muscles L vs R | Down-Up +1.30 / -0.84, Twist -1.95 / +0.70 | Down-Up -0.74 / -0.74, Twist 0.27 / 0.27 | yes |
| A1 | bind round trip < 2 deg | LowerArm_L 55.8, UpperArm_R 39.0, Calf_L 23.1 | 4 arm bones 3.13, Thigh_L 1.77, Calf_L 1.96, hands / clavicles 0 | **no (arms 3.1)** |
| A1 | no 177 deg flips at 240 Hz | 177-179 deg in one 1/240 s step (Run, Walk) | max roll step 0.59 deg | yes |
| A1 | left fist path in gather blends < 20 deg | 350-360 deg (right 4-11) | 7-10 deg | yes |
| A1 | TwistBoneDriver input near 0 in Idle | L -83, R -23 deg | L -1.1, R +1.1 deg | yes |
| A1 | `BuildAndTest "Player"` PASS; held weapons unchanged | - | PASS; HandPose and held-weapon grips unchanged against `held_weapons.png` | yes |
| A2 | hips drop no faster than 1.2 m/s | 2.04-2.26 m/s (0.2 s crossfade) | 1.13-1.17 m/s | yes |
| A2 | no body yaw in the first 60 % of the exit | 154 deg in 0.33 s, up to 530 deg/s | 0 deg in the first 0.28-0.38 s | yes |
| A2 | never start an action above 0.5 m/s | starts at 3.3-3.8 m/s | starts at 0.00 m/s (brake first, 0.6 s timeout) | yes |
| A3 | no single-frame yaw step > 6 deg | 15.7 / 11.9 deg; focus turn 540 deg/s | 3.7-5.0 deg; focus turn <= 180 deg/s | yes |
| A3 | no frame over 50 ms at the first hit | 136-308 ms | still 101 ms (first item of a session) and 42-70 ms per recipe learned | **no (not my files, see 3)** |
| A4 | step cycle at Speed ~0.7 within 20 % of Walk | 3.31 s (Walk 0.99 s) | 1.00 s (Walk 0.97 s) | yes |
| A5 | no yaw rate over the cap | 543 deg/s in a reversal | 282-314 deg/s (cap 342 at that speed); 90 deg turn 201-205 deg/s (was 196) | yes |
| A5 | reversal > 135 deg rotates the velocity | brakes through zero (min 0.02 m/s, 0.37 s straight skid) | velocity turns at the cap, min 2.09 m/s | yes |
| A5 | foot slide < 0.1 m in a 180 deg reversal | not measured | not measurable reliably (see 5) | **unverified** |
| IK-1 | hip dip < 0.05 m on step-ups, none on flat | pelvis offset min -0.29 m, dips 0.21-0.25 m | pelvis offset min -0.03 m, dip 0.02-0.03 m (vs IK off); flat: none | yes |
| IK-1 | no step-up onto resource nodes | 542 frames perched on the driftwood | 0 frames | yes |
| IK-2 | chest vs hips under 12 deg in a reversal | up to 33.2 deg | 13.5-13.7 deg (clip alone 11.7-11.8) | **close**: look adds under 2 deg, the rest is Run clip content (B2) |
| IK-2 | no single-frame look weight change > 0.1 | 0.75 to 0 at the cone edge | <= 0.08 per frame, never drops to 0 | yes |
| Suite | full PlayMode suite green, probe / capture skip | 68 / 0 / 6 | 68 passed, 0 failed, 7 skipped, twice in a row (U_30, U_31 on the final code; see 5) | yes |
| Compile | `cc.sh all` rc=0 | | runtime / editor / tests rc=0 | yes |

## 2. What changed, per step

- **A1** (`PrimalCharacterBuilder`): the importer T-pose is built from the skin bind pose (largest skinned mesh, bones set
  parents first), then each arm is raised by side name (L = -X, R = +X) with the upper arm, forearm and hand straightened
  toward that axis. Twist distribution: `upperArmTwist 1`, `lowerArmTwist 0` (twist bones present; the TwistBoneDriver
  does the forearm share), legs 0.5. A sweep of 7 twist settings (probe E) shows the 3.13 deg arm residual in every setting,
  so it is not the twist split; it does not flip. A1 was done Unity-side only; the Blender complement (export in Rest
  Position) was not tried.
- **Builder fix**: `BuildAndTest` failed with "prefab has no Animator/controller" because the regenerated controller was not
  saved before the prefab build. The controller is now saved first. Backups of the old avatar and controller:
  `Tools/PLAYER_Survivor.fbx.meta.before_A1`, `Tools/PlayerAnimator.controller.before_A1`.
- **A2**: loop actions blend in 0.35 s and out 0.45 s, one-shots 0.3 / 0.3 s (Sleep / Wake_Up / Get_Up unchanged).
  `PlayerAnimationDriver` brakes first when a stand-still action (`AnimParams.NeedsStop`) is requested above 0.5 m/s, and
  keeps `CanMove` off until the exit transition is 60 % done (`ExitHold`).
- **A3**: `PlayerInteraction` focus turn uses SmoothDampAngle (180 deg/s max, 0.12 s, 5 deg per frame cap), only when the
  motor is not steering and the driver is not braking.
- **A4**: Idle stays a child of the Locomotion tree (tests and the intro look for the state name "Locomotion"), but its
  child timeScale is 6 and the tree plays at `LocoRate`, which the driver lerps from 1/6 at rest to 1 at 0.3 m/s. Net effect
  equals a separate Idle state: Idle plays at its own 6 s rate at rest, and walk-band speeds step at the Walk rate.
- **A5** (`PlayerMotor`): turn cap by speed (walk 360, run 300, sprint 200 deg/s; aim keeps 600). Above 0.5 m/s, a target
  more than 135 deg from the velocity rotates the velocity at the cap while easing to 55 % of the target speed.
- **IK-1** (`PlayerIK`, `PlayerMotor`): pelvis offset is scaled by foot weight and rate limited (1.5 m/s); when both feet
  are more than 0.12 m below the capsule ("perched") foot IK and pelvis drop are off. The motor drops its step offset near
  resource-node colliders and its own step-up skips them.
- **IK-2** (`PlayerIK`): look target eased (SmoothDamp 0.15 s), weight step capped at 0.08, body weight eased to 0.03 while
  moving, camera target clamped onto a cone (80 deg standing, 35 deg moving) instead of dropping, lean pivots on the root.

## 3. Requests to other agents

- **First-hit hitch (A3), owner of `UI/InventoryUI.cs` and `Items/CraftingSystem.cs`.** Probe F timed every listener of
  the first gather yield: VFX, SFX, ResourceNode.Hit and GameEvents listeners cost under 3 ms. The cost is:
  - `PlayerInteraction.GiveOrDrop` first call 78 ms, next frame 101 ms (first item of a session: `CraftingSystem` item-added
    chain learns recipes);
  - `Crafting.Learned -> InventoryUI.OnLearned` 24-43 ms per recipe (rebuilds all tiles each time), next frames 42-70 ms,
    three recipes on the first wood;
  - `HUDManager.OnLearned` 2-3 ms, next frames 25-48 ms (layout).
  Suggested fix: InventoryUI marks tiles dirty on `Learned` and rebuilds once when the crafting tab is opened (or once per
  frame); CraftingSystem raises one batched notification for several recipes. `ResourceNode` needs no change.
- **Character agent**: put `clips_manifest.json` next to the anim json in `Assets/Art/Characters/Player/Animations/` when
  the clips are exported (schema: `{"clips":[{"name","frames","loop","speed","events":[{"frame","function","param"}]}]}`).
  The builder merges it and uses its events. The staging file `export/staging/clips_manifest.json` does not exist yet.
- **Character agent (B2)**: Run clip alone gives 11.7-11.8 deg chest vs hips in the reversal; the IK-2 target (12) leaves
  no room for the look. Gather_Wood / Gather_Stone arm twist is past the muscle range on 7-9 frames (max 1.15-1.32).

## 4. Phase C: wired, waiting for clips

None of the 22 clips exist yet. The builder logs "waiting for clip (state skipped)" per clip and still PASSES; a clip
without meta gets defaults (loop flags for the holds and idles). Rebuild with `BuildAndTest "Player"` once they land.

| Clips | Wiring when present |
|---|---|
| Gather_Enter, Gather_Exit (both needed) | GatherPlant: grounded -> Enter (0.12 s) -> Gather_Plant loop (0.1 s at 95 %) -> Exit (0.12 s on release) -> Locomotion (0.15 s at 90 %) |
| Walk_Start, Walk_Stop, Run_Start, Run_Stop, Run_Pivot_180, Turn_180 | states tagged "Loco", mirror by `LocoMirror`, entered from Locomotion on `UseLocoClips && LocoEvent == id` (0.1 s), back at 85 % (0.15 s) |
| Punch_L, Punch_R, Punch_Heavy, Kick | attack states, ids 50-53 (`IsAttacking` + Action), speed x AttackSpeed; events from the manifest, else OnAttackStart 0.1 / OnAttackHit 0.35 (Heavy 0.36, Kick 0.42) / OnAttackEnd 0.75 |
| Unarmed_Block | upper-body hold, id 36, like Sword_Block |
| Unarmed_Idle, Bow_Idle, Spear_Idle | upper-body combat idles like Sword_Idle (WeaponType None / Bow / Spear) |
| Bow_Equip, Bow_Nock (one-shots), Bow_FullDraw (hold) | upper body, ids 37 / 38 / 39 |
| Collect_Water (one-shot), Butcher (loop) | full-body actions, ids 14 / 15, brake-first like the other stand-still actions |
| Spear_Recovery | Attack_Spear and Spear_Attack_2 recover through it |

**Start / stop / pivot selection** (`PlayerAnimationDriver.UpdateLocoClips`, off: `useStartStopClips = false`):
- input starts with the character nearly still: Turn_180 if the input is more than 135 deg from the facing, else Run_Start
  above walk speed, else Walk_Start;
- input stops above 0.6 m/s: Run_Stop above walk speed + 0.4, else Walk_Stop;
- the motor reports a reversal (`PivotRequested`) above walk speed + 0.4: Run_Pivot_180;
- lead foot from the Locomotion phase (left planted at 0): phase 0.25-0.75 mirrors the clip.
Turn on after the clips exist, then retune motor acceleration to the start clips (plan C2).

Gameplay side still needed for C3: unarmed attack input when no item is held (`PlayerCombat`), hitbox on hand / foot.

## 5. Caveats

- **Run_ReachesRunSpeed_AndLocomotionState** failed in the first 2 of 4 full-suite runs (distance 3.38 and 3.36 m in 1 s,
  limit 3.45) and passed in class runs (14/14) and in the last two full runs. The motor clamps dt at 0.05 s, so long frames lose distance
  while `WaitForSeconds` counts game time; the failing run took 0.25 s longer than the passing one (suggestive, not proof).
  I added frame stats to the test (diagnostic only, expectation unchanged): the two passing runs had 51 and 62 frames, max 35 /
  32 ms, no clamped time, no blocked steps. A future failure will print the cause.
- **Foot slide metric**: the probe's stance detector gives up to 0.4 m difference between identical reversal runs. No
  foot-slide claim is made; Run_Pivot_180 is expected to be the real fix.
- **In-memory serialized values**: a new field's first default stays on in-memory objects across domain reloads until the
  asset reloads. `pivotSpeedFactor` was measured at 0.55, so the code default is 0.55. `PrimalAnimAudit.PlayerFieldDiff`
  confirms 0 differences between the prefab / scene values and the code defaults (PlayerIK is added at run time).
- `probe_on.txt` is "off".

## 6. Files changed (mirror paths, all deployed)

- `Scripts/Editor/PrimalCharacterBuilder.cs`: A1 T-pose and twist, controller save fix, A2 timing, A4 LocoRate,
  Phase C wiring, manifest merge, default attack events.
- `Scripts/Animation/AnimParams.cs`: LocoRate, LocoIdleRate, UseLocoClips, LocoEvent, LocoMirror; ids 14, 15, 36-39, 50-53;
  `NeedsStop`, `IsUnarmedAttack`; `LocoEvents`.
- `Scripts/Player/PlayerAnimationDriver.cs`: brake-first, exit hold, LocoRate, start / stop / pivot selection (off).
- `Scripts/Player/PlayerMotor.cs`: turn caps, reversal pivot, resource-node step rule, `IsSteering`, `TargetSpeed`,
  `TargetDirection`, `PivotRequested`.
- `Scripts/Player/PlayerIK.cs`: IK-1, IK-2, lean pivot.
- `Scripts/Player/PlayerInteraction.cs`: eased focus turn.
- `Scripts/Editor/PrimalAnimAudit.cs`: `SceneState`, `OpenScene`, `ControllerCheck`, `PlayerFieldDiff`.
- `Tests/PlayMode/CharacterMotionProbe.cs`: recorder columns, test F (first-hit breakdown), twist sweep, LocoRate.
- `Tests/PlayMode/PlayerControllerTests.cs`: frame stats in `MoveAndMeasure` (diagnostic only).
- Generated by `BuildAndTest`: `PLAYER_Survivor.fbx.meta` (avatar), `PlayerAnimator.controller`, player prefab.
- Not changed: `PlayerFeedback.cs` (VFX / SFX are not the hitch).
