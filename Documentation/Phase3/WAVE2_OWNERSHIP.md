# Phase 3, integration round + wave 2: agents and file ownership (2026-09-29)

Same rules as `WAVE1_OWNERSHIP.md` and `Documentation/AGENT_PROTOCOL.md` (bridge lock, hand-made zips, fresh ids, no git,
no deleting, never `PrimalGameplayBuilder`). Wave 1 is merged (reports `Phase3/_u_report.md`, `_ai_report.md`,
`_surv_report.md`, `_res_report.md`). CHAR (Blender clips) was stopped by the owner: bare-hand and gather actions keep
their placeholder clips until the owner restarts it.

## Lead decisions (owner-level questions from wave 1)
- Gathering: one action per press, hold to repeat (`ResourceManager.HoldToRepeat = true`, directive 44). Mobile GATHER works the same (tap = one, hold = repeat).
- Spelling: "Fiber" everywhere in English text (item "Plant Fiber", prompt "Gather Fiber"); ids unchanged.
- Building scope: the old "No modular base building" rule (`BuildSystem.cs:13`) is replaced by directive 27-29: a small prefab set (foundation, wall, doorway wall + door, roof, optional half wall / ramp not needed). No voxels, no free-form building.
- Storms: WORLD adds storms to the random weather (rare, short); only a storm or long heavy rain puts out an open fire.

## Integration round (short, owners of wave 1)
| Agent | Prefix | Tasks |
|---|---|---|
| U | U_ | arm-injury `PlayerStatusEffects.AttackMultiplier` on player attack damage; one ToolBreak sound per break (remove the duplicate in `WeaponBase.Broke` if SURV's `WearActive` already plays it); receivers for `OnUseItem` / `OnDrink` events; mobile label GATHER for "Catch Fish"; touch test asserts HARVEST at a food node; append `GameEvents.ProjectileLanded` (position in the event payload) and raise it when a missed arrow / thrown spear lands |
| AI | P_ | `CreatureSaveSection : ISaveSection` + register in `GameManager`; FireSense on SURV's Campfire API; PlayerSignature on `PlayerStatusEffects.Has(Bleeding)`, `TreeFelled`, `PlayerFeedback.LastFootSurface`; distraction noise from `ProjectileLanded` once U lands it; weather-aware behaviour only through reading `WeatherManager` (WeatherManager now belongs to WORLD) |
| SURV | S_ | "Plant Fiber" spelling in item data / journal / tips; keep a food stack's age through any drop path you own; then stop (wave 2 owners take Shelter / Bedroll / SurvivalEnvironment) |
| RES | R_ | `WorldPickup.DropStack` keeps food age (Spoilage stamp); fish shoal cue (small periodic ripples / fin splash, pooled, only near the player); felled tree leaves a stump (saved, regrows into a tree later); `Carcass` knife numbers stay with AI |

## Wave 2
| Agent | Prefix | Owns |
|---|---|---|
| BUILD | B_ | `Building/*` (BuildSystem, PlacedStructure, ISaveableStructure, new StructureDefinition / snapping files), `World/Shelter`, `World/Bedroll`, `World/StorageBox`, new `Editor/PrimalBuildingBuilder`, building piece art (original, Blender background process), building item / recipe ASSETS created by its own builder (not `Items/*` code), building tests |
| WORLD | W_ | `Core/WeatherManager`, `Core/TimeManager`, `Core/AmbienceManager`, `World/ZoneManager`, `World/VolcanoLandmark`, `VFX/NightSky`, `VFX/WaterGlobals` (rain globals only), `Survival/SurvivalEnvironment`, `Story/*` (TutorialManager, IntroSequence, JournalSystem), `UI/JournalUI`, `UI/Minimap`, `Editor/PrimalVolcanoBuilder`, new `Editor/PrimalWorldPassBuilder`, wet-surface shader work (new shader files or shared globals; never duplicate materials), world tests |

Shared append-only: `Core/GameEvents.cs` (re-read right before editing, append at the end). HUD changes (SURV's
`HUDManager`) and touch UI (U's `MobileHUD`) go through the Lead as requests unless stated otherwise.

## Interfaces for wave 2
- Shelter purpose: a built enclosure (roof piece over the point, walls on at least 2 sides) must count for `SurvivalEnvironment.ShelteredAt` and `Shelter.Covers` (rain, sun, temperature), for campfire rain protection (`Campfire.Sheltered`), and for rest / save / respawn when a bedroll is inside.
- Day phases: `TimeManager.Phase` (Dawn, Morning, Noon, Afternoon, Dusk, Night) + `GameEvents` value when it changes; AI may read it.
- Weather: `WeatherManager.State` / `Intensity` / `RainingAt` stay compatible (AI, SURV and RES read them).
