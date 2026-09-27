# PRIMAL FRONTIER - Phase 2 Status (final report)

Phase 2 brief: "Realistic character + world + survival system", on the existing project (no rebuild, nothing working
deleted, world not expanded). Date: 2026-09-27.

Status words: **PASS** = checked (automated test, or a screenshot / report that was looked at); **PARTIAL** = built and
compiles, only part of it checked; **FAIL** = tried and did not work / reverted; **NOT TESTED - MANUAL TEST REQUIRED** =
built and compiles, needs a person playing; **NOT COMPLETED - TOOL LIMITATION** or **NOT COMPLETED** = not done.
By the owner's instruction no automated bot / batch runs were made in this phase: the PlayMode suite was not re-run.

## P0 - player movement, rig / skin, core loop
| Item | Status | Evidence / note |
|---|---|---|
| Inspect the character first, fix root causes | PASS | findings in `PRIMAL_FRONTIER_CHARACTER_PIPELINE.md` section 1 |
| Rig / skin (checkpoint_player_rig_v2) | PASS | skeleton kept; character test: 0 unweighted vertices, 100 % follow the bones |
| 47 reworked clips (checkpoint_player_animation_v2) | PASS | `CharacterTests/Player_test.md` RESULT: PASS, `Player_clips.png` reviewed; walk / run foot slide 1-2 % |
| Foot IK, lean, head look, climb hand IK (`PlayerIK`) | NOT TESTED - MANUAL TEST REQUIRED | compiles; controller IK pass on (test report) |
| Smoothed turning, idle variation | NOT TESTED - MANUAL TEST REQUIRED | compiles |
| Core survival loop still works | NOT TESTED | earlier 33 / 33 PlayMode PASS; not re-run after Phase 2 |

## P1
| Item | Status | Evidence / note |
|---|---|---|
| Mobile HUD (stick, look, buttons, safe area, hotbar taps, map tap, build buttons) | PARTIAL | in the scene (`[UI]/[Touch]`, baked); no phone build: NOT TESTED on a device |
| Minimap (baked picture, markers, big map) | PARTIAL | picture baked and checked (`Art/UI/T_MinimapIsland.png`, orientation verified against the pond / beach markers); in-game display NOT TESTED |
| Melee: spear combo, knife attack, dodge with i-frames | NOT TESTED - MANUAL TEST REQUIRED | compiles; clips PASS in the character test |
| Resources: hit wobble, shrink, empty puff | NOT TESTED - MANUAL TEST REQUIRED | compiles |
| Crafting tabs ALL / TOOLS / WEAPONS / FOOD / WATER / BUILDING / SURVIVAL | PARTIAL | in the scene (Cat0-Cat6 baked), waterskin recipe moved to WATER; not seen in play |
| Building (ghost, slope / water / overlap / distance checks, rotate, touch buttons) | NOT TESTED - MANUAL TEST REQUIRED | checks verified in code; touch buttons new |
| Climbing + fruit (checkpoint_player_climbing) | PARTIAL | four trees placed on dry flat ground (screenshots `Review/tree*.png`); climbing and picking NOT TESTED in play |
| Water purification (dirty water, boiling, sickness, save v2) | NOT TESTED - MANUAL TEST REQUIRED | compiles |

## P2
| Item | Status | Evidence / note |
|---|---|---|
| Volcano landmark (original model, smoke, embers, crater glow, lava flow) | PASS (visual) | `Review/volc_beach_day.png`, `volc_beach_night.png`, `volc_camp_dusk.png` (edit-mode captures with simulated particles) |
| Volcano rumble (sound, tremor, puff) | NOT TESTED - MANUAL TEST REQUIRED | compiles |
| Heat haze, ash fall | NOT COMPLETED | haze needs a distortion pass (mobile cost); ash not done |
| Weather: Cloudy state, lightning + delayed thunder, wind, surface wetness | NOT TESTED - MANUAL TEST REQUIRED | compiles |
| Sun / golden hour / sky / stars | NOT COMPLETED | existing TimeManager colour curves kept; no star field added |
| Water shader (sea) | PASS (visual) | `Review/water_before_sea.png` vs `water_sea_noon.png`; pond kept on URP Lit on purpose |
| Foliage wind shader | FAIL | the editor crashed right after switching tree materials (cause not confirmed); reverted to URP Lit from backups; shader kept unassigned, menu marked experimental |
| Global wetness parameter / player wetness look | PARTIAL | `_PF_Wetness` set every frame and used by the sea's rain ripples; player wet look compiles, NOT TESTED |
| Custom shaders compile | PASS | `PF/Water`, `PF/Landmark Lit`, `PF/Particles Additive No Fog` checked with ShaderUtil (0 errors) |
| VFX library naming / caps per quality | PASS (code review) | `PRIMAL_FRONTIER_VFX_SHADER_GUIDE.md` |
| Dinosaur eyes (8 species) | PASS (visual) | `Review/pface_*.png` after the fix (eyes on the heads, iris + pupil readable) |
| Dinosaur idle life (head look, blink, glance) | NOT TESTED - MANUAL TEST REQUIRED | compiles |
| Attack anticipation (tell before the strike) | NOT TESTED - MANUAL TEST REQUIRED | compiles |
| Quality presets Low / Medium / High / Ultra | PARTIAL | table in the systems doc; render settings apply in builds only, no device test |

## Architecture
| Item | Status | Note |
|---|---|---|
| Hierarchy mapping (World/Environment, Gameplay, Systems, Lighting, UI) | PASS (documented) | roots not renamed on purpose (find-by-name, hand edits), see architecture doc |
| GameStateManager / PlayerState | PARTIAL | GameManager states existed; `PlayerState` (read-only mode + Changed event) new, NOT TESTED in play |
| IInteractable | PASS (compiles) | interface + `Interactable` base |
| Save versioning | PARTIAL | version 2 with the dirty-water flag; old-save load NOT TESTED |
| Camera modes (climb / build / combat offsets, eased) | NOT TESTED - MANUAL TEST REQUIRED | compiles |
| Input architecture (single reader + Virtual touch) | PASS (code review) | |

## Docs
`PRIMAL_FRONTIER_ARCHITECTURE.md`, `PRIMAL_FRONTIER_SYSTEMS.md`, `PRIMAL_FRONTIER_CHARACTER_PIPELINE.md`,
`PRIMAL_FRONTIER_VFX_SHADER_GUIDE.md`, `PRIMAL_FRONTIER_MOBILE_GUIDE.md`, this file, and the root `README.md`.

## Checkpoints (git, local commits, not pushed)
| Checkpoint | Commit / tag |
|---|---|
| checkpoint_water_purification | tagged |
| checkpoint_player_rig_v2, checkpoint_player_animation_v2, checkpoint_player_combat, checkpoint_player_climbing | tagged (one commit) |
| checkpoint_mobile_hud, checkpoint_minimap | tagged (one commit) |
| checkpoint_building, checkpoint_volcano, checkpoint_weather, checkpoint_shader_vfx, checkpoint_world_polish, checkpoint_vertical_slice_v2 | see the final commits of this phase (`git tag --list "checkpoint_*"`) |

## Known issues
- Unity editor crash at 16:08 right after the foliage shader switch; materials reverted. The crash log was not read
  (folder access to the crash dump was not granted in time).
- Nothing in Phase 2 was play-tested by a person; the PlayMode suite was not re-run.
- Phone build not made.
