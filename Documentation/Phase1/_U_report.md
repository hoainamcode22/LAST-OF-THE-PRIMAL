# Agent U report, Phase 1 wave 1 (2026-09-30)

## Task 1: mobile / touch controls removed from the PC game
Code (backups `<file>.before_P1` next to each edited file):
- `UI/MobileHUD.cs`: `PF_TOUCH` defined only for `UNITY_ANDROID || UNITY_IOS`. New `MobileHUD.Supported` (false on PC).
  On PC: `Awake` disables the component and clears `PlayerInputReader.Virtual.Active`, `BakeLayout()` is a no-op (no
  `[Touch]` canvas is ever baked), `WantTouch()` returns false, `Update` returns when not built. Class, `Visible`,
  static `AttackLabel` / `ContextLabel`, `TouchButton`, `TouchZone` kept, so tests compile.
- `Core/GameManager.cs:97` (only this line): `if (MobileHUD.Supported) uiRoot.GetOrAdd<MobileHUD>();`
- `Editor/PrimalSceneBaker.cs:237` and `Editor/PrimalPhase2Builder.cs:306-307` (only these lines): add MobileHUD only
  when `MobileHUD.Supported`, so re-bakes never bring it back on PC.
- `UI/SettingsPanel.cs:41`: "Touch controls" row removed (comment left); summary comment updated.
- `UI/ContextHints.cs:294`: looks for a MobileHUD only when `MobileHUD.Supported`.
- `Core/GameSettings.cs:21`, `Player/PlayerInputReader.cs:47`: comments only. `TouchControls` / `TouchNames` kept
  (tests use them); the `Virtual` merge stays but never activates on PC (`Virtual.Active` false). Keyboard / mouse /
  gamepad paths untouched.
- New `Editor/PrimalPcCleanup.cs`: `[PrimalBridgeCommand] RemoveTouch` ("dry" = count only), idempotent, saves the scene.

Scene run (`PrimalPcCleanup.RemoveTouch`, id U_rt2_0752): removed `[UI]/[Touch]` canvas (1 canvas, 43 objects),
MobileHUD on `[UI]` (1), Settings "Touch controls" label + button in `[UI]/[Title]/Settings` and `[UI]/[Pause]/Settings`
(2 rows; it was the last row at y -326, so 0 rows needed moving), 0 stray TouchButton / TouchZone, scene saved.
Second run (U_rt3_0802): 0 / 0 / 0, "already clean". Scene file check: 0 MobileHUD guid refs, 0 `[Touch]`, 0 "Touch controls".

## Task 2: climbables placed
New `Editor/PrimalClimbBuilder.cs`: `Survey` (read-only) and `Build` ("dry" = report only). Per rock / cliff under
World/Cliffs, World/Rocks and the waterfall RockFace group: 12 directions, face rays at 0.25 m steps (continuous,
steep, no overhang, facing the climber), base on dry ground (terrain SampleHeight + physics, above sea level 0, not in a
WaterSource), flat standing room on top, width 0.9-1.6 m, then `Climbable.Validate()`. Best spot per rock, 2 best per
point of interest first, 12 m spacing, max 4 ledges + 6 faces. Output `[Gameplay]/Climbables/Climb_NN_<Kind>_<rock>`
(child `Climb_Ledge` / `Climb_RockFace` with Climbable + ClimbStart / ClimbEnd / ClimbExit, SaveId = object name).
Idempotent: rebuilds only its own `Climb_*` children (second run U_bb2_0802: same 10).
Build U_bb1_0801: 409 rocks checked, 4908 directions, 174 valid rocks, 10 placed, 0 failed validation, scene saved.

| # | Kind | h (m) | width | bottom | lip | exit | facing | rock | near |
|---|---|---|---|---|---|---|---|---|---|
| 01 | Ledge | 2.04 | 1.6 | (84.5, 29.9, -162.7) | (85.2, 31.9, -163.1) | (85.9, 32.0, -163.5) | 120 | Waterfall/RockFace/PoolRock_6 | waterfall 11 m |
| 02 | Ledge | 1.78 | 1.2 | (3.4, 41.7, -160.5) | (4.0, 43.5, -160.5) | (4.8, 43.3, -160.5) | 90 | Rock_Medium_02 | cliffs (cave) 29 m |
| 03 | RockFace | 3.05 | 1.6 | (-39.7, 42.3, -165.8) | (-39.5, 45.3, -165.8) | (-38.7, 45.7, -165.8) | 90 | Rock_Large_01 | cliffs 33 m |
| 04 | Ledge | 1.80 | 1.2 | (-4.4, 7.4, 160.4) | (-4.4, 9.2, 160.0) | (-4.4, 9.0, 159.2) | 180 | Rock_Medium_02 | start beach 53 m |
| 05 | Ledge | 1.51 | 1.6 | (-78.0, 5.7, 165.2) | (-78.2, 7.2, 165.2) | (-79.0, 7.5, 165.2) | 270 | Rock_Medium_02 | start beach 74 m |
| 06 | RockFace | 3.33 | 1.6 | (112.6, 32.2, -168.2) | (111.5, 35.6, -168.8) | (110.8, 35.3, -169.2) | 240 | Rock_Large_03 | waterfall 19 m |
| 07 | RockFace | 3.82 | 1.2 | (108.8, 36.8, -182.5) | (110.4, 40.6, -183.4) | (111.1, 40.7, -183.8) | 120 | Rock_Large_01 | waterfall 21 m |
| 08 | RockFace | 3.69 | 1.2 | (102.3, 44.6, -200.6) | (104.2, 48.3, -201.7) | (104.9, 48.0, -202.1) | 120 | Rock_Large_03 | waterfall 33 m |
| 09 | RockFace | 3.97 | 0.9 | (-8.9, 46.8, -172.7) | (-10.3, 50.8, -173.5) | (-11.0, 51.3, -173.9) | 240 | Rock_Large_03 | cliffs 33 m |
| 10 | RockFace | 3.71 | 1.6 | (-25.8, 48.0, -177.0) | (-24.6, 51.7, -177.0) | (-23.8, 51.6, -177.0) | 90 | Rock_Large_01 | cliffs 37 m |

Notes: player start is (-20, 1.5, 211); the nearest cliffs (cave entrance, logged as "cliffs near the start") are at
(-18, 24, -140), about 350 m north. Rocks right at the start beach are 1.5-1.7 m tall, so the start gets 2 ledges only.
Ledge max is 2.3 m (Climbable.Validate caps ledges at 2.4 m), faces 3-4 m. Not play-tested (no PlayMode by policy).

## Task 3: wave 2 prep (clips), nothing imported
Staging `export/staging/PLAYER_Survivor.fbx` + `clips_manifest.json` (04:54, 85 clips, identical to its
`_before_P1_20260930` copy) vs Unity `Assets/Art/Characters/Player/Model/PLAYER_Survivor.fbx` (09-28, 58 clips).
In staging now: BareHand_Punch_1/2/3, BareHand_Heavy, BareHand_Combo_End, Unarmed_Block, Gather_Branch,
Gather_Stone_Hand, Drink_Kneel, Collect_Water, Bandage_Use (+ BareHand_Idle, BareHand_HitReaction, BareHand_Block,
Ledge_Mantle, Climb_Rock_Up / Down / Idle). **Missing from staging: Kick, Unconscious.**

PlayerAnimator.controller states on placeholder clips (state -> clip it plays now):
BareHand_Punch_1 -> Knife_Attack, BareHand_Punch_2 -> Sword_Attack_1, BareHand_Punch_3 -> Sword_Attack_2,
BareHand_Heavy -> Sword_Heavy, Gather_Branch -> Gather_Plant, Gather_Stone_Hand -> Gather_Plant, Drink_Kneel -> Crouch,
Collect_Water -> Crouch, Bandage_Use -> Use_Item, Unconscious -> Wake_Up (frame 0, speed 0).
States missing (skipped until the clip exists): BareHand_Combo_End, Kick, Unarmed_Block (upper layer), BareHand_Idle,
BareHand_HitReaction. No states for Ledge_Mantle / Climb_Rock_*: ledges and rock faces play the tree clips
(Climb_Start / Up / Idle / Down / End) through PlayerClimb.

Steps once CHAR confirms the export (Kick + Unconscious included):
1. Backup Unity FBX, its .meta, `PLAYER_Survivor_anim.json`, `PlayerAnimator.controller`, the player prefab (`_before_P1`).
2. Copy staging FBX -> `Assets/Art/Characters/Player/Model/PLAYER_Survivor.fbx`, `clips_manifest.json` ->
   `Assets/Art/Characters/Player/Animations/PLAYER_Survivor_anim.json` (keeps the FBX guid, so references hold).
3. Under the lock: Refresh -> Ping -> `PrimalCharacterBuilder.BuildAndTest "Player"`. It re-imports clips + events,
   and the controller builder then picks the real clips automatically (Placeholder() prefers the real clip; Combo_End,
   Kick, Unarmed_Block states appear when the clips exist). Check `Documentation/CharacterTests/Player_test.md`
   (PASS, 0 "placeholder clip for" lines, 0 "waiting for clip").
4. Unconscious: the builder hard-wires `Clip("Wake_Up")`; change it to prefer an `Unconscious` clip (loop, speed 1) when
   present (PrimalCharacterBuilder.cs:653, U owns it).
5. Optional: wire Ledge_Mantle / Climb_Rock_Up / Down / Idle states and use them in PlayerClimb for faces.
6. PrimalAnimAudit.ControllerCheck, ConsoleCheck 0 errors, SaveScene. Risk: BuildAndTest also rebuilds the player
   prefab ("overwrites hand edits"); diff the prefab against its backup before keeping it.

## Bridge commands run (all under the U lock; ids)
- Cycle 1 (06:07-06:12): U_r1 Refresh ok, U_p1 Ping ok, U_c1 ConsoleCheck 4 errors, all in `AI/HuntDirector.cs` (AI's
  file, mid-edit): released without changes.
- Cycle 2 (07:49-08:04): U_r2 Refresh, U_p2 Ping, U_c2 ConsoleCheck 0 errors / 0 warnings, U_rt1 dry, U_rt2 RemoveTouch,
  U_sv1 Survey (842 hosts), U_bd1 / U_bd2 dry, U_r3 Refresh + U_p3 Ping + U_c3 ConsoleCheck 0 / 0 (after a selection
  tweak), U_bb1 Build, U_bb2 Build (idempotence), U_rt3 RemoveTouch (clean), U_c4 ConsoleCheck 0 errors / 0 warnings.
  Lock held 15 min (over the 10 min aim, lost time waiting on compiles).
- U_s1 `PrimalEditorBridge.SaveScene` and U_ss1 `.SceneState` FAILED: "not found or not marked [PrimalBridgeCommand]".
  The PC copy of `Editor/PrimalEditorBridge.cs` (mtime 05:02) no longer contains SaveScene / SceneState. My builders
  save the scene themselves (EditorSceneManager.SaveScene: "scene saved" in both). Scene file checked afterwards.

## Requests
- Lead: `Editor/PrimalEditorBridge.cs` on the PC lost SaveScene / SceneState (someone restored an older copy?).
- Tests owner / Lead: `BareHandIslandTests.Touch_Buttons_Show_The_Context` (BareHandCombatTests.cs:415) needs the
  touch HUD and will fail on PC: add `if (!MobileHUD.Supported) Assert.Ignore("touch controls: mobile builds only");`
  at its start. `ContextHintsTests` (line 100) already copes with no MobileHUD. Static label tests are unaffected.
- SURV (`UI/HUDManager.cs:147`): the status-icon row sits right of the vitals because the space under the panel was
  kept for the touch menu buttons. On PC that space is free; move the row under the vitals if wanted (layout baked in
  the scene, so not a trivial change: left to SURV).
- CHAR: Kick and Unconscious are not in the staging export yet.
- HIER (wave 2): `[Gameplay]/Climbables` is the parent for the 10 climb spots; keep the name when reorganising.

## Not done
- No PlayMode runs (policy); climbs are validated by geometry checks only, the owner should try them by hand.
- No FBX import (wave 2).
