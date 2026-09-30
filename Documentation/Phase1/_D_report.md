# DINO (D_) report, Phase 1 wave 1 (2026-09-30)

Result: `pf_up_D2` deployed, the new dinosaur clips imported with `PrimalDinoBuilder.RebuildClips`: 7 x PASS, Console 0 errors.
No PlayMode run, no git, no deletes, no Blender run.

## 1. Rest / Sleep / Eat / Drink / Call / Stop / Turn per species (for AI)

Layer 0 of `Assets/Art/Characters/Dinosaurs/<Species>/Animations/<Species>Animator.controller`. The state names equal the
clip names (clip = FBX take of the same name in `DINO_<Species>.fbx`). This applies to all 7 land species: Parasaurolophus,
Triceratops, Ankylosaurus, Velociraptor, Carnotaurus, Spinosaurus, Apex. Each controller has 22 states.

| behaviour | state(s) = clip(s) | tag | how to trigger it |
|---|---|---|---|
| Rest (lie down) | `Rest_Down` -> `Rest_Loop` (automatic at exit time 0.98) | Rest | `ActionType = 3` then fire trigger `Action`. **Starts from `Locomotion` only**, so Speed < walk and not in Alert / Turn / an action |
| Sleep | `Rest_Loop` (slow breathing loop; Apex closes its lids) | Rest | no separate Sleep state: keep `ActionType = 3` after Rest_Down, and it stays in the loop. Night sleep and midday rest are the same chain |
| Fidget while lying | `Rest_Shift` -> back to `Rest_Loop` | Rest | while in Rest_Loop, fire `Action` again with `ActionType` still 3. Do not re-fire Action every tick or it keeps fidgeting |
| Get up | `Rest_Up` -> `Locomotion` (exit 0.95) | Rest | set `ActionType` to anything other than 3 (from Rest_Loop or Rest_Shift). Speed > max(1.5, 1.3 x walk) cuts Rest_Up short; Speed > max(2, 1.3 x walk) in Rest_Loop jumps straight to Locomotion. Keep Speed 0 while tag = Rest |
| Eat | `Eat` (loop) | Action | `ActionType = 1` + `Action`; stays while ActionType is 1, leaves when it changes |
| Drink | `Drink` (loop) | Action | `ActionType = 2` + `Action`; same rule |
| Call | `Call` (one-shot, then Locomotion) | Action | `ActionType = 6` + `Action` |
| Stop | `Stop` (one-shot) | Stop | trigger `Stop` while in Locomotion; ends at 0.9 or when Speed > 1 |
| Turn in place | `Turn_Right` / `Turn_Left` (loops) | Turn | float `Turn` (deg/s, + = clockwise from above) with Speed < 0.3: Turn > 15 -> Turn_Right, Turn < -15 -> Turn_Left; back when \|Turn\| < 8 or Speed > 0.6. `TurnMul` = playback speed (default 1) |

Other actions, from standing states only (Locomotion, Alert, Turn, Stop, other actions) with `Action` fired: Look (4 LookAround,
9 Investigate), Roar (5), Idle_Variation (11), Breathe (12, loop), Charge (10, loop, grazers), Defend (7 Threaten / 8 Defend,
loop, grazers; hunters play Roar for 7), Recover (13, hunters). A lying animal given another ActionType plays Rest_Up first.
Attack (AttackType 0), Heavy_Attack (1), Bite (2, hunters), Hurt, Death start from anywhere.

Parameters in all 7 controllers: `Speed` float, `ActionType` int, `Action` trigger, `Attack` trigger, `AttackType` int,
`Hurt` trigger, `Dead` bool, `Alert` bool, `Intensity` float (run slot: 0 Run, 1 Flee (grazers) / Chase (hunters)), `Turn`
float, `TurnMul` float (default 1), `Stop` trigger. Runtime constants: `DinoActions` in `Animation/AnimParams.cs` already has
Rest 3, Eat 1, Drink 2, Call 6, IdleVariant 11, Breathe 12, Recover 13 (Charge 10 has no constant; use 10).

Per-species clip set (from the imported FBX, 25 takes each):

| species | 2 own states + run-blend clip | Rest_Down / Rest_Loop / Rest_Up (s) | Call (s) | Stop (s) | Turn clip (s, authored deg/s) |
|---|---|---|---|---|---|
| Parasaurolophus | Charge, Defend, Flee (in run blend) | 2.77 / 5.87 / 2.27 | 4.00 | 1.20 | 1.23, 49 |
| Triceratops | Charge, Defend, Flee | 3.27 / 6.53 / 2.70 | 3.50 | 1.43 | 1.30, 27 |
| Ankylosaurus | Charge, Defend, Flee | 3.37 / 6.70 / 2.80 | 3.50 | 1.50 | 1.20, 22 |
| Velociraptor | Bite, Recover, Chase (in run blend) | 1.57 / 4.23 / 1.27 | 1.50 | 0.63 | 0.50, 240 |
| Carnotaurus | Bite, Recover, Chase | 2.60 / 5.67 / 2.13 | 3.50 | 1.13 | 1.03, 52 |
| Spinosaurus | Bite, Recover, Chase | 3.20 / 6.47 / 2.67 | 3.50 | 1.40 | 1.47, 27 |
| Apex | Bite, Recover, Chase | 3.60 / 7.00 / 3.00 | 3.50 | 1.60 | 1.63, 22 |

Common 20 states: Locomotion (blend Idle / Walk / Run_Intensity), Alert, Look, Roar, Idle_Variation, Breathe, Eat, Drink, Call,
Stop, Turn_Left, Turn_Right, Rest_Down, Rest_Loop, Rest_Shift, Rest_Up, Attack, Heavy_Attack, Hurt, Death; plus 2 own states = 22.
Flee / Chase are not states: they sit inside Locomotion (Run_Intensity on `Intensity`). Rest_Shift: 3.07 / 3.40 / 3.47 / 2.30 / 2.97 / 3.33 / 3.60 s in the same species order.
Pteranodon and Mosasaurus were not rebuilt (phase 2 controllers): Pteranodon plays Idle_Variation for ActionType 3; Mosasaurus
has no Rest / Eat / Drink / Call states. Full clip tables and events: `Documentation/PCPhase/DINO_CLIPS.md`.

## 2. Commands and results

| step | command | result |
|---|---|---|
| zip check | `unzip -p Tools/pf_up_D2.zip ... \| diff` against Assets | project files 03:13 UTC, zip files 03:58-04:12; diff only adds (RebuildClips, DinoAnimIds, rest chain, turn, stop, bite, run/intensity blend, eyelids); no project change newer than the zip. D1.zip is the older copy |
| deploy | `$HOME/deploy.sh pf_up_D2` (lock, 06:13) | 3 files written, md5 = manifest; Ping ok; Console 2 errors, both in AI's `HuntDirector.cs` (`GameEventType.PredatorHunt` missing). Released the lock and waited |
| recheck | Refresh, Ping, ConsoleCheck `D1_cc_0655` (lock, 06:42) | 0 errors (PredatorHunt appended by AI meanwhile) |
| scene | `PrimalAnimAudit.SceneState` `D1_ss_0656` | Island_VerticalSlice loaded, dirty=False (there is no `PrimalEditorBridge.SceneState` / `SaveScene` bridge command; SceneState lives in PrimalAnimAudit) |
| rebuild | `PrimalDinoBuilder.RebuildClips ""` `D1_072219` | 07:22:20-07:24:25 UTC: Parasaurolophus, Triceratops, Ankylosaurus, Velociraptor, Carnotaurus, Spinosaurus, Apex all PASS (25 clips in FBX = 25 in meta; 176 standing-start transitions grazers, 160 hunters); eyes + lids added on 8 species (Apex keeps its hero eyes). Log: 297 entries, 0 errors / warnings |
| after | ConsoleCheck `D1_cc2_0726`, SceneState `D1_ss2_0726` | 0 errors; scene reopened, dirty=False (not saved by DINO: nothing to save). Lock released |

## 3. Checks

- Controllers (YAML parse): 7 x 22 states, every state has a motion, 12 parameters, `Run_Intensity` blend present; rest transitions
  verified on Triceratops and Velociraptor (Locomotion -> Rest_Down on Action + ActionType == 3, Rest_Loop -> Rest_Up on
  ActionType != 3, Rest_Loop -> Rest_Shift on Action + ActionType == 3).
- Prefabs `DINO_<Species>.prefab` (9): 0 null scripts, 0 null meshes, 0 null material slots; 39 / 39 referenced guids resolve
  (scripts, FBX, eye / lid materials and meshes in `Assets/_Project/Art/Characters/Creatures/`); each of the 7 prefabs references
  its rebuilt controller (guid match) and its FBX.
- Spawner: the `DinosaurSpawner` block in `Island_VerticalSlice.unity` is byte-identical to the 05:09 baseline.
- `Data/Dinosaurs/DINO_*.asset`: md5 differs from the 05:09 baseline, but all 9 files have mtime 07:43:08 UTC, i.e. written by
  AI's `PrimalWildlifeBuilder.TuneHunting` (`P1_t_074100`, 07:43:08-09), after RebuildClips finished (07:24:25). RebuildClips has
  no code path to these assets. Not changed by DINO.

## 4. Files changed

- `Assets/_Project/Scripts/Editor/PrimalCharacterBuilder.Dino.cs`, `PrimalDinoBuilder.cs`, `PrimalCreatureEyes.cs` (pf_up_D2).
- By RebuildClips: 7 x `Model/DINO_<Species>.fbx.meta` (importer clips / events), 7 x `Animations/<Species>Animator.controller`,
  8 x `Prefab/DINO_<Species>.prefab` (7 land species except Apex rebuilt with byte-identical output, plus Pteranodon and
  Mosasaurus re-saved by the eyes "force" pass), 29 eye / lid assets in `Assets/_Project/Art/Characters/Creatures/`,
  `Documentation/CharacterTests/<Species>_test.md` / `_clips.png` / `_clips_order.txt`, `Dinosaurs_PC_clips.md`.
- `Documentation/PCPhase/DINO_CLIPS.md` status line (copy `DINO_CLIPS.md.before_P1`), this report.

## 5. Requests and not done

- AI: wire Rest / Sleep with ActionType 3 + one Action pulse from Locomotion (section 1); keep Speed 0 while tag = Rest.
- Lead: re-run AI's `PrimalWildlifeBuilder.Build` if placed herds must be re-checked against the re-saved prefabs (paths and
  walk / run speeds unchanged).
- Not done: no PlayMode / visual check of the new states in the running game (policy); eyelid look under URP not reviewed.
- Bridge note: the device shell kills calls after a variable 1-60 s, so long `run.sh` waits failed; commands were sent by writing
  `command.json` and polling `result_<id>.json`.
