# PRIMAL FRONTIER - Vertical Slice Final Report

Date: 2026-09-27. Engine: Unity 6000.3.10f1 URP. Status words: **PASS** (automated test passed), **FAIL**, **NOT TESTED**
(no test was run for it; it may still work). Nothing below is claimed as done without a check listed next to it.

## 1. Summary
A playable PC vertical slice of an original prehistoric survival game on one small island: player character, shipwreck
intro, 19-step tutorial, survival loop, nine original dinosaurs with AI, a detailed hero predator (Rift Tyrant), blood
effects with a setting, and a performance pass. Automated PlayMode suite: **33 / 33 PASS**. Character pipeline tests:
**9 / 9 dinosaurs PASS**. Manual play-through by a human: **NOT TESTED**.

## 2. Player (rig, animation, controller, feedback)
| Item | Status | Evidence |
|---|---|---|
| Rig, 38 clips, facial blendshapes | PASS | character pipeline test (`Documentation/CharacterTests/Player_test.md`) |
| Walk / run / crouch / jump / slopes / steps / camera collision | PASS | `PlayerControllerTests` |
| Gather / eat / drink / craft / build actions on animation events | PASS | `PlayerFeedbackTests`, `SurvivalLoopTests` |
| Spear / bow combat, hurt / death / respawn / wake-up | PASS | `PlayerControllerTests` |
| Feel of controls, camera comfort | NOT TESTED | needs a person playing |

## 3. World
| Item | Status | Evidence |
|---|---|---|
| Island scene boots, player on the beach, nodes / water / loot present | PASS | `Island_Boots_Player_Tutorial_Nodes` |
| Sea water refused, fresh water drinkable | PASS | `Ocean_Is_Not_Drinkable_Fresh_Water_Is` |
| Day / night, weather, ambience | NOT TESTED | no automated test |
| Cave and forest traversal | NOT TESTED | no automated test |

## 4. Story intro and tutorial
| Item | Status | Evidence |
|---|---|---|
| Title / intro / journal UI | PASS (boot only) | island boot test starts with forced flags; intro skip NOT TESTED |
| 19 tutorial steps defined, compass targets | PASS (first steps) | boot test checks the tutorial runs; later steps (tracks, observe, shelter, night) NOT TESTED end to end |

## 5. Survival loop
| Item | Status | Evidence |
|---|---|---|
| Item database, inventory rules, crafting queue / refund | PASS | `SurvivalLoopTests` |
| Gather > craft > campfire > cook > save > load restores state | PASS | `Gather_Craft_Fire_Cook_Save_Load` |
| Hunger / thirst / temperature balance over a full night | NOT TESTED | tuning needs play time |

## 6. Dinosaurs
| Item | Status | Evidence |
|---|---|---|
| 9 species import, skin weights on every vertex, feet do not slide, loops seamless, face +Z, feet on the ground | PASS | `Documentation/CharacterTests/*_test.md`, `Dinosaurs_summary.md` |
| Spawn in habitats and walk on the terrain | PASS | `Dinosaurs_Spawn_And_Walk_On_Terrain` |
| Predator chases and bites, player can kill it | PASS | `Predator_Chases_And_Bites_Player_Player_Can_Kill_It` |
| Herd flees on predator warning | PASS | `Herbivores_Flee_From_Predator_Warning` |
| Rift Tyrant hero model: mouth opens cleanly, teeth / gums / tongue / eyelids animate | PASS (visual check in Blender renders + Unity test screenshots) | `Documentation/CharacterTests/Apex_clips.png` |
| Pteranodon / Mosasaurus full AI | NOT DONE | they are ambient (circling / swimming) only |
| Hero-level detail for the other 8 species | NOT DONE | they use the earlier procedural pipeline |

## 7. Blood and combat feedback
| Item | Status | Evidence |
|---|---|---|
| Spray on hit, ground blood, bleeding wound on the body | PASS | `Hits_Spray_Blood_Death_Leaves_A_Pool_And_Blood_Can_Be_Turned_Off` |
| Blood pool under a dead creature | PASS | same test |
| Setting Off: dust instead of blood, decals cleared | PASS | same test |
| Blood trail from badly hurt creatures | NOT TESTED | code path only |
| Blood painted onto the creature skin (wound textures) | NOT DONE | wounds are dripping particles on the bone, not skin decals |

## 8. Performance
| Item | Status | Evidence |
|---|---|---|
| Phone texture overrides, audio import, instancing, pipeline per quality, Android player settings | DONE | `Documentation/Performance_Optimization.md` |
| CPU frame on the island with all creatures (editor, batch mode, no rendering) | 4.1 ms avg, 8.8 ms worst, 6.3 KB GC / frame | `Documentation/Performance_Island.md` (PerformanceTests PASS) |
| Rendering cost (batches, GPU time) | NOT TESTED | batch mode does not render; counters read 0 |
| Frame rate on a phone | NOT TESTED | no device build was made; touch controls missing |
| Windows player build | see `Documentation/Build_Windows.md` | PrimalBuild |

## 9. QA summary
- PlayMode (run 2026-09-27): 33 / 33 PASS. PlayerControllerTests 14, PlayerFeedbackTests 8, SurvivalLoopTests 6, DinosaurTests 4, PerformanceTests 1.
- Character tests: 9 / 9 dinosaurs PASS after fixing missing skin weights, floating eyes / horns, stretched teeth,
  raptor foot slide and hero claws below the ground.
- Manual QA flow (start > tutorial > first night): NOT TESTED by a person.

## 9b. Scene authoring (hand-editable Hierarchy), added 2026-09-27
| Item | Status | Evidence |
|---|---|---|
| Runtime reads scene objects first (player, systems, UI, placed dinosaurs), creates only what is missing | DONE, compiles (runtime, player build defines, editor, tests: 0 errors) | cloud compile check |
| `Primal Frontier > Scene > Bake Everything Into Scene` puts them into the island scene | DONE (code); bake run by the owner in the editor | writes `Documentation/Scene_Hierarchy.md` when run |
| UI find-or-create keeps hand edits, placed dinosaurs restored on a new game | NOT TESTED (tests written: `SceneAuthoringTests`, not run) | |
| Existing 33 PlayMode tests after this change | NOT TESTED (not re-run) | |
| Editor opens the island scene on start, generator menus ask before overwriting | DONE (code) | |

## 10. Known issues, limits, next steps
- Models are procedural (generated by code), good for a slice but below hand-sculpted AAA quality; the Rift Tyrant is
  the most detailed. Mouth interiors on the older species are simplified.
- Phone: touch controls added in Phase 2 (see `PRIMAL_FRONTIER_MOBILE_GUIDE.md`); no phone build tested.
- Pteranodon / Mosasaurus are ambient only; no taming, no multiplayer (out of scope).
- Git: the remote `origin` is configured; pushing is left to the owner.
- Template leftovers from project creation (`Assets/Scenes/SampleScene`, `Assets/TutorialInfo`, `Assets/Readme`) are not part
  of the game and can be deleted. The stray `My project/` folder (a blank Unity template project) was removed in Phase 2.
- IP: every model, texture, sound and font source is listed in `IP_AND_ASSET_PROVENANCE.md`; no assets from other games.

## 11. Phase 2 (realistic character + world + survival), 2026-09-27
Full PASS / PARTIAL / FAIL / NOT TESTED table: `PRIMAL_FRONTIER_PHASE2_STATUS.md`. Summary: player animation v2 (47 clips)
PASS in the character test; climbing + fruit trees, dodge / combo / knife, water purification, minimap, touch HUD, weather
(cloudy, lightning, wind, wetness), volcano landmark, sea water shader, creature eyes, dinosaur head look and attack tells,
quality presets built and compiling; most of it NOT TESTED by a person. Foliage wind shader FAIL (reverted after an editor crash).
