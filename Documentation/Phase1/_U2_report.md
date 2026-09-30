# Agent U report, Phase 1 wave 2a (2026-09-30)

## Result
CHAR's 89-clip player FBX + manifest imported, `BuildAndTest "Player"` PASS, every bare-hand / survival state plays its
own clip, player prefabs and scene player keep every component and reference, 0 compile errors.

## Files changed (backups `<file>.before_P1b` next to each)
Assets:
- `Assets/Art/Characters/Player/Model/PLAYER_Survivor.fbx` = staging FBX (md5 264fcc02..., 52.2 MB, 89 clips). The .meta
  kept its guid a1e4ee4c...; the builder rewrote its clip settings.
- `Assets/Art/Characters/Player/Animations/PLAYER_Survivor_anim.json` = staging `clips_manifest.json` (md5 4c10c4ce...,
  89 entries; superset of the old 58: all 58 kept, 5 re-timed by CHAR earlier: Walk 32 f, Run 22, Sprint 18, Eat / Gather_Plant event frames).
- `PlayerAnimator.controller` rebuilt by the builder (guid 5c221cf9... unchanged), `PFB_Player_Survivor.prefab` rebuilt
  (byte-identical to its backup). Backups: FBX, FBX.meta, anim json, controller + .meta, PFB_Player_Survivor.prefab,
  `_Project/Prefabs/Player/PFB_Player.prefab` (unchanged, cmp equal).
Code:
- `Editor/PrimalCharacterBuilder.cs`: Unconscious state uses the real `Unconscious` loop (speed 1), falls back to frozen
  Wake_Up f0 only when the clip is missing (logged as placeholder). New `Unconscious_Collapse` state (tag Action) entered
  from AnyState on the new `Collapse` trigger (0.15 s), exit time 0.98 -> Unconscious (0.05 s). New param `Collapse`.
- `Animation/AnimParams.cs`: `AnimParams.Collapse`.
- `Player/PlayerAnimationDriver.cs`: `Collapse()` (trigger, falls back to `SetUnconscious()` if the state is missing);
  SetUnconscious comment updated. No gameplay knockout path exists yet (only the intro calls SetUnconscious), so
  `Collapse()` has no caller.
- `Player/PlayerFacial.cs`: eyes closed also in `Unconscious_Collapse`.
- `Player/PlayerInteraction.cs:428`: "Salt water makes you thirstier, and boiling does not remove the salt. Find fresh water."
- `Tests/PlayMode/BareHandCombatTests.cs:417`: `if (!MobileHUD.Supported) Assert.Ignore("touch controls: mobile builds only");`
- `Editor/PrimalAnimAudit.cs`: new read-only `[PrimalBridgeCommand] PlayerImportCheck "<tag>"` -> `Library/PrimalBridge/U_import_<tag>.txt`
  (controller state -> clip on all layers, resolved components + object refs of PFB_Player / PFB_Player_Survivor / scene
  player, missing scripts / refs, FBX clip events vs anim json).

## Bridge commands (lock U 09:21:44-09:34:47 UTC, 13 min)
| id | command | result |
|---|---|---|
| U2_r1 / U2_p1 | Refresh, Ping | ok |
| U2_c1 | ConsoleCheck | 1 error, ENV's `Editor/PrimalVolcanoBuilder.cs(317,35)` CS0117 (saved 09:21:37); my code was already in the loaded domain (compiled by SURV's cycle), so I went on and told the Lead |
| U2_k1 | PlayerImportCheck before | 62 states, 0 null motions, 0 missing scripts, 0 missing refs, 58 clips, 0 event mismatches |
| (copy) | staging FBX + manifest in | md5 equal |
| U2_r2 / U2_p2 | Refresh (reimport), Ping | ok |
| U2_bt1 | PrimalCharacterBuilder.BuildAndTest "Player" | True, `Player_test.md` **PASS**: 89 clips configured (38 loops, 143 events), Clips in FBX 89 (meta 89), avatar valid humanoid, states 62 base + 11 upper + 3 hit reaction, params 25 |
| U2_k2 | PlayerImportCheck after | 76 states, 0 null motions, 0 missing scripts, 0 missing refs, 89 clips, 0 event mismatches |
| U2_cc1 | ControllerCheck | controller guid 5c221cf9..., 3 layers, 25 params, not dirty; prefab + instance animator -> PlayerAnimator, avatar valid |
| U2_ss1 | SceneState | empty scene: BuildAndTest's Test() opens a NewScene (island was not dirty) |
| U2_os1 / U2_ss2 | OpenScene island, SceneState | Island_VerticalSlice dirty=False (no save needed) |
| U2_k3 | PlayerImportCheck after_scene | same as U2_k2, scene player included |
| U2_c3 | ConsoleCheck | 133 entries, **0 errors, 0 warnings** (ENV's error fixed by then; Editor.dll 09:28) |
Test assembly compiled after the test edit (Tests.PlayMode.dll 09:22:25, 0 errors).

## State -> clip (before -> after)
| State (layer) | Before | After |
|---|---|---|
| BareHand_Punch_1 | Knife_Attack | BareHand_Punch_1 |
| BareHand_Punch_2 | Sword_Attack_1 | BareHand_Punch_2 |
| BareHand_Punch_3 | Sword_Attack_2 | BareHand_Punch_3 |
| BareHand_Heavy | Sword_Heavy | BareHand_Heavy (30 f overhand) |
| BareHand_Combo_End | (no state) | BareHand_Combo_End (after Punch_3) |
| Kick | (no state) | Kick |
| Unarmed_Block (UpperBody) | (no state) | Unarmed_Block |
| BareHand_Idle (UpperBody) | (no state) | BareHand_Idle |
| BareHand_HitReaction (HitReaction) | (no state) | BareHand_HitReaction |
| Gather_Branch | Gather_Plant | Gather_Branch |
| Gather_Stone_Hand | Gather_Plant | Gather_Stone_Hand |
| Drink_Kneel | Crouch | Drink_Kneel |
| Collect_Water | Crouch | Collect_Water |
| Bandage_Use | Use_Item | Bandage_Use |
| Unconscious | Wake_Up, speed 0 | Unconscious loop, speed 1 |
| Unconscious_Collapse | (no state) | Unconscious_Collapse -> Unconscious |
Also new because the clips now exist: Walk_Start, Run_Start, Walk_Stop, Run_Stop, Run_Pivot_180, Turn_180 (entered only
with UseLocoClips, i.e. `useStartStopClips` on the driver) and Gather_Enter / Gather_Exit around Gather_Plant. Idle
time-scale in Locomotion now x5.63 (Walk is 32 f).

## Events in the imported clips (frames, all equal to the manifest, 0 mismatches over 89 clips)
Punch_1 OnAttackStart 1 / Active 4 / Hit 5 / End 7; Punch_2 2/5/6/8; Punch_3 3/6/7/9; Heavy 6/11/12/15; Kick 5/10/12/15 +
OnFootstep R 22; Gather_Branch OnGatherHit 16; Gather_Stone_Hand OnGatherHit 17; Drink_Kneel OnScoop 27 + OnDrink 40;
Collect_Water OnScoop 22 + OnDrink 34; Bandage_Use OnUseItem 45; Unconscious_Collapse OnBodyFall "sit" 21 + OnBodyFall 34;
Combo_End, blocks, Unconscious none (as authored). Every event function in the FBX (27) has a receiver in
`CharacterAnimationEvents`.

## Prefab / scene check (PlayerImportCheck before vs after_scene)
PFB_Player (63 transforms), PFB_Player_Survivor (62), scene Player (63): component lists, collider values (CharacterController
h 1.8 r 0.3 c (0, 0.9, 0)), Animator avatar / controller, LODGroup, CharacterAnimationEvents, TwistBoneDriver, PlayerIK,
PlayerFacial / PlayerEquipment / PlayerAnimationDriver refs, sockets and SkinnedMeshRenderer root bones: **identical** (only
diff lines: params 24 -> 25, clips 58 -> 89). 0 missing scripts, 0 missing references.

## Player_test.md
PASS, 0 "placeholder clip for" lines (was 10). 7 "waiting for clip" lines remain, all for weapon clips CHAR was never asked
for: Butcher, Spear_Recovery, Bow_FullDraw, Bow_Equip, Bow_Nock, Bow_Idle, Spear_Idle (was 29 lines). Clips in the FBX but
not wired: BareHand_Block (Unarmed_Block is used), BareHand_Heavy_Straight (CHAR: optional), Climb_Rock_Up / Down / Idle,
Ledge_Mantle, Drink_Container.

## Requests
- SURV: `Tests/PlayMode/SurvivalNeedsTests.cs:366` asserts `StringAssert.Contains("Boil it first", ...)`; with the new
  text it fails. Assert e.g. "does not remove the salt" instead (sent to the Lead too).
- ENV: `PrimalVolcanoBuilder.cs(317,35)` error seen at 09:22 (gone by 09:34); sent to the Lead.
- Lead: `.before_P1b` backups inside Assets get imported as DefaultAssets (incl. the 37.8 MB old FBX); move them out of
  Assets when convenient (no deletes possible for agents).
- CHAR / Lead: optional wiring of Climb_Rock_* / Ledge_Mantle for PlayerClimb faces and ledges (not in this wave's task).

## Not done
- No PlayMode runs (policy): collapse / kick / unconscious loop not seen in game; the owner should check the opening,
  punches, kick, block and the drink / gather / bandage actions by hand.
- No gameplay knockout path calls `PlayerAnimationDriver.Collapse()` yet.
