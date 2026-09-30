# NEXT SESSION: what to run when Unity is open again (written 2026-09-30 by the Lead)

Unity was closed at about 17:33 UTC on 2026-09-29. Everything below is already ON THE PC (extracted into `Assets/_Project`,
md5-checked) or waits as a zip in `E:\LAST OF THE PRIMAL\Tools\`. Nothing has been compiled by Unity since then. The whole
cloud mirror compiles (runtime / editor / tests rc 0) with all agents' files together.

## Step 0 (owner): open Unity, let it import and compile. Expect a long import (41 new FBX, textures, ~60 WAVs).
Then the Lead (or the owner, one command at a time) runs, in this order, with fresh ids and the bridge lock:

| # | Command | Why |
|---|---|---|
| 1 | `$HOME/run.sh N_01 PrimalEditorBridge.ConsoleCheck "" 5` | baseline: import errors, compile errors |
| 2 | check `md5sum Assets/_Project/Scripts/Core/GameEvents.cs` = mirror `24cc3f82f3d92887a5c9f0d13e822578`; if not, deploy `pf_up_LGE.zip` (mirror copy of the shared file) | several agents appended to it |
| 3 | `$HOME/deploy.sh pf_up_P11` (AI wildlife, not extracted yet) | then `PrimalWildlifeBuilder.Build "" 15` |
| 4 | ENV: `PrimalEnvironmentBuilder.Wet`, `.Vegetation`, `.Story`, `.Volcano` (if log says heat haze NONE: `PrimalVolcanoBuilder.Atmosphere` then `.Volcano` again), `.Capture after` | terrain is final (v2.1); these place the rest |
| 5 | WORLD: `PrimalAtmosphereBuilder.Build` (W_b2), `.Capture "tag=v3"` | zones / hazard / emitters on the new markers |
| 6 | STORY: `$HOME/deploy.sh pf_up_T1` (re-extract is harmless), `PrimalStoryBuilder.Build "map" 12` | missions, journal, minimap bake |
| 7 | RES: `PrimalResourceBuilder.Build` | 23 nodes sit in water, 192 off the new ground |
| 8 | U: `PrimalCharacterBuilder.BuildAndTest "Player"`; if CHAR staged clips: import per `_u_report.md` section 5 first | placeholders / new clips |
| 9 | BUILD / SURV / DINO builders as their reports list (`_build_report.md`, `_surv_report.md`, `_dino_report.md`) | |
| 9a | BUILD: copy `/mnt/user-data/outputs/pf_up_B1.zip` to `E:\LAST OF THE PRIMAL\Tools\`, `$HOME/deploy.sh pf_up_B1` (5 FBX, 3 textures, 11 scripts; no `error CS`) | building code + art (never compiled by Unity yet) |
| 9b | BUILD: `$HOME/run.sh B_01 PrimalBuildingBuilder.Build "" 15` (materials, 5 prefabs, items, definitions, 5 recipes -> 32), then `B_02 PrimalBuildingBuilder.Inspect "" 5` | log must say "recipes (32 of 40)", every piece "model FBX" |
| 9c | BUILD: `$HOME/run.sh B_03 PrimalTestRunner.RunPlayMode "BuildingTests" 3` (7 tests, island one saves / loads in the test folder) | nothing else can check snapping / enclosure / door save |
| 9d | BUILD: copy `/mnt/user-data/outputs/B_art/build_pieces.py` to `E:\LAST OF THE PRIMAL\Tools\BlenderPipeline\` (source of the FBX, project root, not Assets) | keep the generator with the project |
| 10 | `PrimalEditorBridge.ConsoleCheck "" 5` again: must be 0 errors | owner policy: Console clean is the acceptance |
| 11 | Optional targeted tests only: `WorldAtmosphereTests`, `StoryTests`, `WildlifeTests`, `PhysicalActionTests`, `ClimbTests` | not the full suite |
| 12 | Lead: local commit (no trailers) + tags `checkpoint_world_realism`, `checkpoint_waterfall`, `checkpoint_story`, `checkpoint_herds`, `checkpoint_migration`, `checkpoint_player_motion`, `checkpoint_climbing` | |

Then the owner plays by hand (chapter 1 -> the river -> the waterfall -> the herd).

## Where each agent stands (details in `_<agent>_report.md`)
- ENV: terrain v2.1 applied and saved in the editor before it closed (waterfall 14.5 m, river, brook, spring, wetland
  lagoon, canyon, meadow, ridge; 18 location markers; migration route 14 points). Wet shaders, 41 vegetation models,
  storytelling props and volcano ridge visuals are on disk, not yet run (steps 4).
- WORLD: weather schedule with rare storms, day phases, hazard zones, ambience synth (39 clips), zones; W1-W4 compiled
  before the editor closed, W5-W6 on disk.
- STORY: missions (12 main + 17 optional, 4 chapters), objective UI, survivor lines, journal 6 sections, discovery
  minimap, death bundle; T1 on disk.
- AI: herds (9 parasaurolophus, 5 triceratops, 3 ankylosaurs), herd day, migration event, predator territories, weight,
  tracking signs, PrimalWildlifeBuilder; P11 zip NOT extracted yet.
- U: physical drink / eat, ledge + rock climbing, idle variety, head stabilization, footstep dust per surface, 19 sounds;
  U20-U24 compiled, U25-U26 on disk.
- BUILD: foundation / wall / doorway + door / thatch roof / leaf shelter, snapping, B piece menu (have / need), enclosure =
  Shelter (rain, warmth, fire, rest, bedroll inside), save of pieces + door, `PrimalBuildingBuilder`, `BuildingTests`; models
  generated offline (bpy) and reviewed; B1 zip on disk only, nothing compiled by Unity yet (`_build_report.md`).
- DINO, CHAR, SURV, RES: see their reports (started 2026-09-30 night).

## Known cross-agent notes
- Multiple agents appended to `Core/GameEvents.cs`, `Audio/SfxPlayer.cs` (SfxId) and `VFX/VfxPool.cs` (VfxId): the mirror
  is the truth; if a PC copy is older, deploy the mirror copy (step 2).
- The island's `volcano` zone is the new volcanic ridge at the top of the NE canyon (the offshore volcano is scenery).
- Re-running `PrimalShaderBuilder` WindSplit / WindRevert resets M_Bark: run `PrimalEnvironmentBuilder.Wet` again after.
- If `PrimalGameplayBuilder` or `PrimalBushBuilder` is ever re-run, run `PrimalResourceBuilder.Build` afterwards.
- BUILD's `Shelter.cs` / `Bedroll.cs` changed (box cover, `RestPoint`, `Bedroll.All`): the API the others call (`Covers`,
  `WarmthAt`, `Nearest`, `Rested`, `SleepRequested`) is unchanged. Requests to STORY / U / Lead in `_build_report.md` section 4.

## SURV (S_) steps, added 2026-09-30 (PC link was down: nothing below has run on the PC)

Zip `/mnt/user-data/outputs/pf_up_S6.zip` (10 scripts, only SURV files; cloud compile rc 0 with everybody's files) and the
Blender script `/mnt/user-data/outputs/S_art/s_dishes_props.py` are NOT on the PC yet. Both copies also sit in the mirror
(`src/`, `src_assets/phase3/Tools/S_art/`). Run after step 10's Console check, with the bridge lock and fresh ids:

| # | Command | Why / expect |
|---|---|---|
| S1 | copy `pf_up_S6.zip` to `E:\LAST OF THE PRIMAL\Tools\`, `$HOME/deploy.sh pf_up_S6` | HUD hazard line, old objective box gone, hunger / thirst slow healing, humid drying, dishes + scraps + spelling in the builder; no `error CS` |
| S2 | `$HOME/run.sh S_11 PrimalSurvivalBuilder.Build "" 12` | log must say: items meat_skewer_raw / meat_skewer / leaf_fish_raw / leaf_fish / wreck_scraps created (icons / models "pending, using the X icon" until S4), recipes meat_skewer, leaf_fish, cloth_bandage, scrap_rope created, "spelling: N item / recipe text(s) now say Fiber", "total 36 recipes (5 building)" (35 if the Lead retired `shelter`) |
| S3 | `$HOME/run.sh S_12 PrimalSurvivalBuilder.Inspect "" 5` | fiber: displayName "Plant Fiber"; the five new items with their numbers |
| S4 | Blender (owner's session open, or `execute_blender_code_for_cli` on any scratch .blend): run the text of `E:\LAST OF THE PRIMAL\Tools\S_art\s_dishes_props.py` (copy it there first). Result `err` must be empty; 5 FBX + 5 PNG appear in `Tools\S_art` | original models: skewer (cooked / raw), leaf-wrapped fish (cooked / raw), shipwreck scraps |
| S5 | `cp "$HOME/mnt/LAST OF THE PRIMAL/Tools/S_art/ITEM_{MeatSkewer,MeatSkewerRaw,LeafFish,LeafFishRaw,WreckScraps}.fbx" "$HOME/mnt/LAST OF THE PRIMAL/Assets/_Project/Art/Models/Props/"` and `cp ".../S_art/ICON_{MeatSkewer,MeatSkewerRaw,LeafFish,LeafFishRaw,WreckScraps}.png" ".../Assets/_Project/Art/Icons/"`, then `$HOME/run.sh S_13 PrimalEditorBridge.Refresh "" 5` and `S_14 PrimalSurvivalBuilder.Build "" 12` again | the log swaps the borrowed visuals: "meat_skewer: icon ICON_MeatSkewer.png", "model ITEM_MeatSkewer.fbx" (and the four others) |
| S6 | `$HOME/run.sh S_15 PrimalEditorBridge.ConsoleCheck "" 5` | 0 errors |
| S7 | optional targeted test: `$HOME/run.sh S_16 PrimalTestRunner.RunPlayMode "SurvivalPcPhaseTests" 3` (6 tests: dish recipe + fire, built data + spelling, healing slowed by hunger, humid drying, hazard line, water prompts) | `Built_Dishes_Rare_Items_And_Spelling` ignores itself until S2 ran |
| S8 | RES: `wreck_scraps` exists after S2: place the scrap pickups / nodes at the wreck remains (`PrimalResourceBuilder.Build`) | rare item in the world |

## RES (R_) steps, added 2026-09-30 (PC link was down: nothing below has run on the PC)

Zip `/mnt/user-data/outputs/pf_up_R9.zip` (11 scripts, only RES files, md5 ab7c78cbf23af76483d2604ecfd9859f; cloud compile
rc 0 with everybody's files) is NOT on the PC yet. Run after step 10's Console check and after ENV's Story step (step 4:
the bone / wreck-plank props are the fallback models for the rare nodes) and SURV's S2 / S5 (the `wreck_scraps` item and
its model), with the bridge lock and fresh ids:

| # | Command | Why / expect |
|---|---|---|
| R1 | copy `pf_up_R9.zip` to `E:\LAST OF THE PRIMAL\Tools\`, `$HOME/deploy.sh pf_up_R9` | food age on the ground, fish cue, tree stumps + saplings, wood / grass footsteps, drink params, rare nodes, the terrain v2 placement; no `error CS` |
| R2 | `$HOME/run.sh R_20 PrimalResourceBuilder.Build "" 14` (log also in `Documentation/Resources/resource_build.txt`) | must say: `locations (18)`, `fresh water cells N from 8 active bodies` (the old stream is inactive and skipped), `re-snap of kept nodes: 48 checked ...`, `biome meadow / canyon / ridge / wetland / waterfall / river / volcano: K clusters`, `rare bones: N piles`, `rare wreck scraps: N piles` (or the skip line naming what is missing), and at the end `VERIFY ... off the ground 0, in fresh water (not fish) 0, below sea level 0` |
| R3 | if R2 says a rare kind was skipped: after ENV `Story` / SURV S5 ran, `$HOME/run.sh R_21 PrimalResourceBuilder.Build "" 14` again (idempotent) | the rare nodes appear |
| R4 | `$HOME/run.sh R_22 PrimalResourceBuilder.Capture "v2" 10` | `Documentation/Screenshots/Resources/v2_island_map.png` (dots: grey stone, orange wood, green fibre, red food, blue fish, violet rare), `v2_start_area*.png`, `v2_player_view.png`; look for clusters on the river banks, the pool, the lagoon, the canyon floor and the ridge, empty ground between them |
| R5 | `$HOME/run.sh R_23 PrimalEditorBridge.ConsoleCheck "" 5` | 0 errors |
| R6 | optional targeted test: `$HOME/run.sh R_24 PrimalTestRunner.RunPlayMode "ResourceTests" 3` (13 tests, no scene: the fruit P0, the gathering rules, strikes, looks, respawn, `Dropped_Food_Keeps_Its_Age`, `Fish_Shoal_Cue_Plays_Near_The_Player_Only`) | all pass; the island tests (`ResourceIslandTests`, 5) only if the start-area layout is in doubt (they load the island: about 60 s) |
| R7 | by hand (owner): fell a tree with the axe, look at the stump; sleep two nights: a sapling stands there; wade to a shoal at the river ford or the lagoon: drops and a small splash every couple of seconds; drop raw meat, pick it up later: the prompt says "(aging)" | stump / sapling, fish cue, food age |

## DINO (D_) steps, added 2026-09-30 (Unity closed; Blender work and export done on the PC)

Already ON THE PC: 7 new `Assets/Art/Characters/Dinosaurs/<Species>/Model/DINO_<Species>.fbx` + `Animations/DINO_<Species>_anim.json`
(Parasaurolophus, Triceratops, Ankylosaurus, Velociraptor, Carnotaurus, Spinosaurus, Apex; 25 clips each). Unity imports
them at step 0 with the old .meta clip lists, so the current prefabs keep working until D2. Backups of the old FBX / meta /
json / controllers: `E:\Model game khủng long\characters\dino_pc\D_20260930\fbx_backup\`. Zip `Tools/pf_up_D2.zip` (3 editor
scripts, only DINO files, cloud compile rc 0 with everybody's files) is on the PC, NOT extracted (`pf_up_D1.zip` beside it is
an older copy: do not use). Run after step 10's Console check, with the bridge lock and fresh ids; save the open scene first
(RebuildClips refuses an unsaved scene):

| # | Command | Why / expect |
|---|---|---|
| D1 | `$HOME/deploy.sh pf_up_D2` | new dinosaur controller (rest chain, turn, stop, bite, recover, defend, Run / Chase-Flee on Intensity), eyelids + catchlight, `RebuildClips`; no `error CS` |
| D2 | `$HOME/run.sh D_01 PrimalDinoBuilder.RebuildClips "" 30` | per species BuildAndTest (importer clip list from the json, controller, prefab, test) then `PrimalCreatureEyes.Build("force")`; result must list 7 x `PASS`, and the eyes part "eyes added (... catchlight, skin lids), in LOD0/LOD1" for 8 species. Summary `Documentation/CharacterTests/Dinosaurs_PC_clips.md`; per species `<Species>_test.md` ends `RESULT: PASS` (25 clips, loops 0 cm, Chase / Flee / Charge foot slide under 12 %) |
| D2b | `$HOME/run.sh D_04 PrimalWildlifeBuilder.Build "" 15` (AI's builder, idempotent) | the prefabs were re-saved, so the herds / territories placed in the scene are re-checked against them; log ends `checks: OK` and `scene saved` |
| D3 | `$HOME/run.sh D_02 PrimalEditorBridge.ConsoleCheck "" 5` | 0 errors (no "AnimationEvent has no receiver": every new event name exists on CharacterAnimationEvents) |
| D4 | optional: `$HOME/run.sh D_03 PrimalReviewCapture.PrefabFaces "" 10` | `Documentation/Screenshots/Review/pface_DINO_*.png`: eyes in lids with a catchlight (compare with `Screenshots/PCPhase/Dino/eyes/`) |
| D5 | if a species FAILs in D2: read its `<Species>_test.md`; to go back, copy that species' 4 files from `fbx_backup\<Species>\` over `Assets/Art/Characters/Dinosaurs/<Species>/` (Model/*.fbx + .meta, Animations/*.json, *.controller) and run `PrimalCharacterBuilder.BuildAndTest "<Species>"` + `PrimalCreatureEyes.Build "force"` | rollback per species |

AI then drives the new states per `Documentation/PCPhase/DINO_CLIPS.md` (until then the old parameters behave as before,
except ActionType 3 = lie down / sleep / get up instead of a standing fidget). Owner by hand: watch a herd lie down at night,
drink at the river, a triceratops threaten (Defend), a raptor bite.

## CHAR (C_) steps, added 2026-09-30 (Blender work and staging export done on the PC; no Unity work)

Staged in `E:\Model game khủng long\export\staging\`: `PLAYER_Survivor.fbx` (all 85 player clips, same FBX settings as
`pf_player_ship.export`) and `clips_manifest.json` (per clip: name, frames, fps, loop, speed, events, notes, root_speed,
root_distance; start / stop / pivot / turn clips also root_yaw / starts_in / ends_in). Details and numbers: `_char_report.md`.
Run inside step 8 (U), with the bridge lock and fresh ids:

| # | Command / action | Why / expect |
|---|---|---|
| C1 | copy `Assets/Art/Characters/Player/Model/PLAYER_Survivor.fbx` and its `.meta` to `E:\LAST OF THE PRIMAL\Tools\PLAYER_Survivor.fbx.before_C_20260930` (+ `.meta.before_C_20260930`), outside `Assets` so Unity does not import the copy | keep the Phase A import |
| C2 | copy `export\staging\PLAYER_Survivor.fbx` over `Assets/Art/Characters/Player/Model/PLAYER_Survivor.fbx`; copy `export\staging\clips_manifest.json` to `Assets/Art/Characters/Player/Animations/clips_manifest.json` (next to `PLAYER_Survivor_anim.json`) | the builder merges the manifest over the anim json (all clips have entries) |
| C3 | U: add receivers only if the builder log names an event without one. New names used: none (only OnFootstep, OnAttackStart / Active / Hit / End, OnGatherHit, OnDrink, OnScoop, OnEat, OnUseItem, OnHurt, OnClimbStep, OnClimbGrab, OnLand, OnBodyFall, OnWakeUp) | `Every_Clip_Event_Has_A_Receiver` stays green |
| C4 | U: states for the NEW clip names the controller does not know yet: `Drink_Container` (container drink, replaces the `Drink` state for a held container), `BareHand_Block` (same content as `Unarmed_Block`, which is already wired), `Climb_Rock_Idle` / `Climb_Rock_Up` / `Climb_Rock_Down` (RockFace kind, play instead of `Climb_Idle` / `Climb_Up` / `Climb_Down` when `Current.kind == RockFace`), `Ledge_Mantle` (Ledge kind pull-over, 54 f = reach 0-10, pull 10-30, knee over 30-46, stand 46-54; plays instead of the Climb_Start -> Climb_Up -> Climb_End chain in `StartMantle`) | clips exist; without states they are only "not referenced" (log line, not a failure) |
| C5 | `$HOME/run.sh C_01 PrimalCharacterBuilder.BuildAndTest "Player" 15` | must PASS: loops seamless (seam 0.0), foot-slide check (Walk 1.35 / Run 3.8 / Sprint 6.2 / Run_Backward 2.4 m/s); start / stop / pivot / turn clips carry speed 0 in the manifest on purpose (they accelerate; the real curve is `root_speed`) so the constant-speed foot-slide check skips them; the rock-face clips start with `Climb` so the vertical skip applies |
| C6 | `PrimalEditorBridge.ConsoleCheck "" 5` | 0 errors |
| C7 | probe once (`probe_on.txt` on, `PrimalTestRunner.RunPlayMode "CharacterMotionProbe"`, then off): compare with `Character/_unity_phaseA_report.md`; expected change: chest vs hips in a reversal now comes from a counter-rotating Run clip (shoulder line +-12 deg against the pelvis +-6.5 deg) | IK-2 row "close" should improve or stay |
| C8 | `useStartStopClips`: turn on only after C7 and a hand check. Clip facts for the tuning: Walk_Start 18 f, 0 -> 1.35 m/s over 0.372 m, ends on Walk frame 0; Walk_Stop 22 f, 1.35 -> 0 in 15 f (0.326 m), ends on Idle frame 0; Run_Start 14 f (0 -> 3.8 m/s, 0.83 m); Run_Stop 26 f (3.8 -> 0 in 16 f, 1.01 m); Run_Pivot_180 20 f (capsule yaw 0 -> 180 over f0-18, speed dips to 55 %: matches the motor's reversal); Turn_180 34 f (capsule yaw 0 -> 180 over f2-28). All authored for the LEFT foot / LEFT turn: the controller's LocoMirror gives the right side | per-frame curves in the manifest (`root_speed`, `root_yaw`) |
| C9 | Walk is now 32 frames (was 30), Run 22 (was 20), Sprint 18 (was 14); Idle stays 180 (the LocoRate / timeScale 6 set-up is unchanged) | blend-tree time sync is normalized, no code change needed |
| C10 | `CombatPolishTests.ExpectAdditiveLightHurt`: expects Hurt_Additive with empty hands; `BareHand_HitReaction` now exists (U report section 5 item 7): that test needs to accept it | expected by design |
| C11 | Lead: copy of the changed generator scripts is already in `E:\LAST OF THE PRIMAL\Tools\BlenderPipeline\` (`pf_clips_c.py` new; `pf_clips_human.py`, `pf_clips_weapons.py`, `pf_player_ship.py`, `pf_char_export.py` changed; `C_tools\` = runner / audit / sheet scripts) | project copy of the pipeline |

## Lead run log 2026-09-30 05:00-05:25 UTC (Unity was open)
- All mirror code (270 files) + all PC-phase docs synced to the PC (pf_up_LALL2, pf_up_LDOCS); PC code list = mirror (md5).
- Fix: BuildSystem had `void Start(...)` with parameters (Unity error "Start() can not take parameters"): renamed to
  `BeginPlacement` (pf_up_L31). ConsoleCheck after: 0 errors.
- DONE on the PC: ENV `Wet`, `Vegetation`, `Story`, `Volcano` (0 warnings each, scene saved); WORLD
  `PrimalAtmosphereBuilder.Build` (18 locations, hazard zones, emitters, clouds, scene saved).
- STUCK: `PrimalStoryBuilder.Build "map"` (id L_t1, sent ~05:15) never wrote a result and the editor stopped taking commands
  (L_t2 still queued). Check the Unity window (modal dialog / long map bake / hang) before continuing.
- STILL TO RUN, in order: STORY Build "map" (or "" without the map bake first), SURV S2-S6, RES R2-R5, AI
  `PrimalWildlifeBuilder.Build`, BUILD `PrimalBuildingBuilder.Build`, DINO D2-D3, CHAR C1-C6 (staging FBX is ready:
  85 clips), final ConsoleCheck, commit + tags.
