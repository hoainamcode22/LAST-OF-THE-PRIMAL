# AI agent report (P_): perception, wildlife, fire fear, bare-hand scaling, creature save (2026-09-29)

Scope: directive 23-25, 30-31 (wildlife side), 52-60, 78 (night wildlife), 83-84; owner decisions on fire fear, wind,
HUD default and escapes. Design and final numbers: `Documentation/AI/PERCEPTION_DESIGN.md` (section 0 = what was
built). All code in the cloud mirror; deployed to the PC with zips `pf_up_P1` .. `pf_up_P9` (only my files), compiled
without errors; `PrimalPerceptionBuilder.Build` run (species table + `Resources/PerceptionConfig.asset`). No git.

## 1. Results

| # | Item | Status | Evidence (PlayMode, editor 6000.3) |
|---|---|---|---|
| 1 | Vision: distance, FOV, line of sight, light level, foliage / thicket cover; timed checks, no per-frame raycasts | **PASS** | `Crouched_Still_In_Bush_At_Night_Is_Not_Detected`: crouched in a bush at 23:00, cover 0.80, raptor effective sight 1.5 m, awareness 0.00 for 8 s at 12 m; standing 6 m away is noticed. `Still_At_25m_Unseen_Sprinting_Seen_Quickly`: still, sight 18-22 m, not noticed; sprinting, Engaged after 1.05 s at 19.9 m. Sight runs in the 0.2 / 0.4 / 1 s think ticks, at most 6 linecasts per frame for all creatures (measured max 1) |
| 2 | Hearing: running, gathering, attacks, building, chopping, large footsteps; noise attracts | **PASS** | `Chopping_Noise_Draws_Predator_To_Investigate`: carnotaurus goes to Investigate, destination 2.8 m from the chop, walks there. `Gameplay_Events_Become_Noises_*`: hit 1.0, build 0.9, hand gathering 0.25, tree fall 2.5 (flyers startle). Movement pulses = gait x surface (grass 0.8 .. water 1.5) |
| 3 | Scent: raw meat carried, cooked meat on fires (weaker), blood (carcass, bleeding player), campfire smoke; position, strength, decay, radius; timed; wind | **PASS** | `Scent_Is_Smelled_Downwind_Not_Upwind` (no scene); `Cooking_Meat_Downwind_Draws_A_Raptor_To_The_Fire_Edge`: the raptor follows the smell to 11.9 m from the fire and circles outside the 7.0 m fear radius; `Cooking_Meat_Upwind_Is_Not_Smelled`: interest 0.00. Puffs drift with `WeatherManager.WindDirection`, decay (tau 45 s), rain cuts them |
| 4 | Wind drifts slowly, foliage follows smoothly | **PASS** | Perlin swing +-60 deg over ~8 game hours in `WeatherManager`, same vector as `_PF_Wind`; `WindTests` green |
| 5 | Cover / cleared areas | **PASS** | `Felling_Trees_Opens_The_Ground`: 5 trees around the player -> 0, cover 0.35 -> 0.00, felled 5 -> clearing exposure 1.15 |
| 6 | Predator responses ignore / investigate / approach / circle / attack / retreat; no forced instant attack | **PASS** | investigate (item 2), approach (Engaged but outside aggro range: stalks at walk speed), circle / wait (item 8), attack (`DinosaurTests.Predator_Chases_*` green), retreat (hurt, fire patience over). `Lost_Target_Goes_To_Last_Known_Position_Then_Gives_Up`: after losing sight the raptor goes to the last sighting (0.0 m) instead of the real player (92 m away), searches, gives up. Herd / pack share: `Herd_Shares_An_Alert` (mates 0.60) |
| 7 | Fire safety configurable per species (radius day / night, rain, fuel, fear; small strongly avoid, large less, apex may ignore) | **PASS** | `FireFearProfile` on every DinosaurDefinition (table in the design doc 0.4). `Fire_Fear_Radius_Follows_Night_Rain_And_Fuel`: 7 m day, 10 m night, x0.6 in rain, weaker with low fuel, 0 below the apex's intensity threshold, sheltered fire keeps its radius |
| 8 | Wildlife near an active fire avoids, observes, circles, waits outside, leaves; campfire not universally safe | **PASS** | `Fire_Keeps_A_Raptor_Circling_Outside_Until_It_Goes_Out`: the raptor circles, closest 8.3 m (radius 7.0 m), and closes in once the fire is out. Responses per species: Avoid (triceratops, ankylosaurus), Leave (parasaurolophus), Circle (raptor), Wait (carnotaurus, apex), Observe (spinosaurus). Fear below 0.5 (apex 0.3) only hesitates, then walks in; provoked creatures ignore the fire for 8 s |
| 9 | Dinosaur life: eat, drink at water, wander, rest, sleep at night, react, search, flee, hunt; not all aggressive | **PASS** | `Herbivores_Sleep_At_Night_And_Go_To_Water`: diurnal herbivores sleep at 23:30 (eyes closed, senses x0.25 / x0.6); a thirsty parasaurolophus walks to the stream and drinks (`DinoState.Drink` was never used before). Raptors are nocturnal (rest 11-15 h, roam wider at night). Herbivores watch, back away, alarm-call and flee; only predators hunt. `DinosaurTests` 4/4 green |
| 10 | Night differences | **PASS** | light model (night ambient 0.25, night vision per species), torch cost (`Torch_At_Night_Extends_Detection`: 13.2 m -> 31.5 m), sleep / roam schedules, fire fear radius larger at night |
| 11 | AI distance tiers near / medium / far / very far (verify and fix) | **PASS** | was 2 tiers + frozen; now near < 60 m (0.2 s, full senses), medium < 120 m (0.4 s), far < 200 m (1 s, scent + loud noises), very far (frozen, Animator off). `AI_Tiers_*`: 30 / 90 / 160 / 259 m -> 0 / 1 / 2 / 3 |
| 12 | Bare-hand hits scaled by species / size, knockback on small creatures | **PASS** | full damage at body radius <= 0.5 m, x (0.5 / r)^2 above (per-species override possible). `Bare_Hands_Knock_Small_*`: raptor -4.0 and pushed at 1.88 m/s, ankylosaurus -0.59 (0.079 %) and not moved. U's `BareHandIslandTests.Punch_Small_Creature_Hurts_Large_Barely` green, logs "AI scales unarmed hits". Ambient creatures scale too |
| 13 | Creature save / restore through SURV's ISaveSection | **PARTIAL** | Logic done and tested: `Creature_Save_Restores_Dead_Bodies_Carcass_And_Health` (11 creatures restored after a respawn-all; the killed raptor stays dead where it fell with its butchering progress; a wounded triceratops keeps 500 HP; a broken section is skipped). **Not hooked into the save file yet**: `ISaveSection` / `SaveSystem.RegisterSection` do not exist in the mirror or the project. `AI/CreatureSaveSection` has the agreed members; registration is 2 lines in GameManager once SURV lands it |
| 14 | Killed creatures do not return on load, the island does not empty | **PASS** | `Killed_Creature_Returns_After_Its_Respawn_Time_When_Far`: a placed creature returns after `respawnHours` (48 game hours) once its body sank and the player is 120 m+ away |
| 15 | HUD indicator, default AUTO; tips | **PASS** | `Stealth_Indicator_Auto_Shows_When_Crouched_And_Marks_Threats`: hidden standing away from predators, shown crouched, a threat arc for a creature that noticed the player, OFF hides it. Settings row "Stealth hint (Ẩn nấp)" AUTO / ALWAYS / OFF. Six bilingual tips (stealth, noise, torch, bush, scent, wind) through `ContextHints.QueueTip` |
| 16 | Escapes moderately easier | **PASS** | tracking window 1.5 s, then last known position + 3 search points; raptors keep a 15 m scent track (item 6) |
| 17 | Zero GC steady state, perf budget | **PASS** | `Perception_Frame_Budget_And_Zero_Allocation` (island, 11 creatures, final full run): sensing 0.028 ms per frame on average, worst frame 0.374 ms, max 1 linecast in a frame; allocation median 15.4 KB/frame with 4800 extra sensing ticks per frame vs 16.3 KB idle (the idle KB are other systems; a 64 KB probe shows up as +64 KB). No scene: 16651 B/frame with 500 rounds = 16651 B idle. Editor numbers, not a phone |
| 18 | Tests: no-scene + island; the 4 DinosaurTests stay green | **PASS** | `PerceptionTests` 11/11, `PerceptionIslandTests` 19/19, `DinosaurTests` 4/4, `BareHandIslandTests` 2/2, `WindTests` 1/1 |
| 19 | Full suite green at the end | **PASS** | 110 passed, 0 failed, 7 skipped (section 5) |

## 2. Files (mine, per WAVE1_OWNERSHIP)

New: `Core/Stimuli.cs`, `AI/PerceptionConfig.cs`, `AI/DinoSenses.cs`, `AI/FireSense.cs`, `AI/CoverMap.cs`, `AI/DrinkSpots.cs`,
`AI/CreatureSave.cs`, `Player/PlayerSignature.cs`, `UI/PerceptionIndicator.cs`, `Editor/PrimalPerceptionBuilder.cs`,
`Tests/PlayMode/PerceptionTests.cs`, `Tests/PlayMode/PerceptionIslandTests.cs`; asset `Resources/PerceptionConfig.asset`
(PC, by the builder); DINO_*.asset perception fields (PC, by the builder).
Changed: `AI/DinosaurController.cs` (rewritten brain, same public members), `AI/DinosaurDefinition.cs` (appended fields),
`AI/AmbientCreature.cs`, `AI/DinoLife.cs`, `AI/DinosaurSpawner.cs`, `Core/WeatherManager.cs` (wind accessors + drift),
`Core/GameManager.cs` (add components, clear stimuli on new game / load / sleep), `Core/GameSettings.cs` (StealthHud),
`UI/SettingsPanel.cs` (one row), `World/BushInteraction.cs` (All, PlayerBush, Radius, stimuli), `World/Carcass.cs`
(scent, All, `ExpireAt`, `RestoreLeft`; OnEnable / OnDisable now override the Interactable ones), `Core/GameEvents.cs`
(appended `PlayerNoticed`, `ScentInvestigated`).
Not touched: Campfire, TreeHarvest, ResourceNode, PlayerFeedback, Projectile, weapons, PlayerHealth, SaveSystem, Items.

## 3. Requests to other agents

| To | Request | Why |
|---|---|---|
| SURV | `ISaveSection` + `SaveSystem.RegisterSection` (agreed interface). Then the Lead / I add in `GameManager.Awake`: `SaveSystem.RegisterSection(new AI.CreatureSaveSection(FindFirstObjectByType<AI.DinosaurSpawner>()))` and `: ISaveSection` on `CreatureSaveSection` | item 13 |
| SURV | Campfire read API (`Fuel01` / `Intensity01` / `State` / `Sheltered`); I switch `AI/FireSense.cs` (interim: intensity = fuel / 300 s) | fire fear follows the real fire state (Lighting / LowFuel) |
| SURV | `PlayerStatusEffects.Has(Bleeding)`; I switch `PlayerSignature.IsBleeding` (interim: `PlayerHealth.IsBleeding`) | blood scent from the status effect |
| RES | a `GameEventType.TreeFelled` raised in `TreeHarvest.Fell` (id wood, position the tree) | today the tree-fall noise is inferred from `Felled.Count` within 5 s of a chop |
| RES | `PlayerFeedback.LastFootSurface` (the Surface of the last footstep) | footstep loudness on the real terrain layer (interim: height / slope rule, collider names) |
| U | a landing event for missed arrows / thrown spears (e.g. `GameEventType.ProjectileLanded`, position) | distraction noises (designed, `StimulusSource.Distraction` ready) |
| Lead | nothing blocking; `GameEvents.cs` got two appended values | |

## 4. Known limits (honest)

- Sleep has no own clip: a sleeping creature holds the Rest / idle pose with its eyes closed and stops calling.
- Drinking walks to the nearest point of the pond / stream mesh; on a steep bank the snout can end slightly in the water.
- The wind swing also turns the foliage sway (slowly, as asked); a storm makes it stronger but not faster.
- Perf numbers are editor numbers (development PC); no phone profile.
- Creatures do not hunt each other (a "hunt" is of the player); a scavenger taking food from a fire is left for later.
- `GameEvents.Raise` still allocates per call (existing, not mine); perception never raises per frame.

## 5. Full suite

Final run `P_15` (2026-09-29 17:49, after `pf_up_P9`): **110 passed, 0 failed, 7 skipped** in 503 s. Skipped = the six
`CharacterMotionProbe` diagnostics (probe off) and `SurvivalShowcase.Capture_Camp_Holster_Hints` (capture only).
Earlier full runs found three flaky tests of mine, fixed before the final run: the cooking-scent test's patience counted
while the raptor was still walking to the fire (now it starts at the edge, a real behaviour fix), and the two allocation
tests compared averages over separate frame blocks (now interleaved frames compared by median). One run also failed U's
`PlayerControllerTests.Run_ReachesRunSpeed_AndLocomotionState` on a 197 ms editor hitch; it passed in the other runs
(that test runs in the greybox scene, where perception is not present).

Compile: `./cc.sh all` runtime / editor / tests rc 0 in the mirror; every deploy compiled on the PC with no errors.

## Integration round (2026-09-29, after wave 1)

| # | Task | Status | Evidence |
|---|---|---|---|
| 1 | `CreatureSaveSection : ISaveSection`, registered in `GameManager.Awake` (after `SaveSystem.Track`, unregistered in OnDestroy); `SaveSystem.Apply` restores it after `ResetWorld` respawned the creatures | RESULT_1 | `PerceptionIslandTests.Save_And_Load_Keep_Killed_And_Wounded_Creatures`: kill a raptor and butcher it, wound a triceratops, `SaveGame`, respawn everything, `LoadGame`; RESULT_1E |
| 2 | FireSense on SURV's Campfire API: intensity = `FuelIntensity01` (0 when Unlit / Extinguished), `Sheltered`, `RainedOn`; the fuel / 300 s stand-in is gone | RESULT_2 | `PerceptionTests.Fire_Fear_Radius_Follows_Night_Rain_And_Fuel` (now a UnityTest: the fire checks rain / roof once a second): 60 s of fuel = RESULT_2E |
| 3 | PlayerSignature: bleeding = `PlayerStatusEffects.Has(Bleeding)` (PlayerHealth only without the component); tree-fall noise from `TreeFelled` (the `Felled.Count` inference is removed); surface = `PlayerFeedback.LastFootSurface` (the height / slope rule stays only for a player without PlayerFeedback) | RESULT_3 | `Bleeding_Status_Leaves_A_Blood_Trail` (RESULT_3E), `Gameplay_Events_Become_Noises_*` (tree fall 2.5 from `TreeFelled`), `Still_At_25m_*` asserts the signature surface = `LastFootSurface` after running |
| 4 | `ProjectileLanded` -> distraction noise at the landing spot (loudness 0.5 x amount: arrow 0.5, spear 1.0; `StimulusSource.Distraction`) | RESULT_4 | `Missed_Projectile_Landing_Lures_An_Investigation`: RESULT_4E |
| 5 | WeatherManager (now WORLD) | done | not edited this round; perception only reads `Intensity`, `RainingAt` (through Campfire), `WindDirection` / `WindStrength` (the accessors added in wave 1) |

Files this round: `AI/CreatureSave.cs`, `AI/FireSense.cs`, `AI/PerceptionConfig.cs` (`projectileLandLoudness`),
`Core/GameManager.cs`, `Player/PlayerSignature.cs`, `Tests/PlayMode/PerceptionTests.cs`, `PerceptionIslandTests.cs`.
The zip also carries the mirror's `Core/GameEvents.cs` unchanged (U's `ProjectileLanded` value was in the mirror but
not yet on the PC; my PlayerSignature needs it to compile).
