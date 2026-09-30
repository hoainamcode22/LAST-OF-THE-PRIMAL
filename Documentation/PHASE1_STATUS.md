# PRIMAL FRONTIER, Phase 1: Core Survival Foundation (2026-09-30)

## TASK REQUESTED

Continue the existing project (PC first) and complete the core survival foundation, without rebuilding the game, resetting the player or deleting working systems. The 36 directive sections covered: an original natural island with no ancient structures; one island with beach, shipwreck, forest, freshwater, river, waterfall, meadow, rocky terrain, caves, volcano, wetland and predator / herbivore territory; a human player with the full action clip set; bare-hand light punch / combo / heavy punch timed by animation events; survival stats and their interactions; water fill, purify, drink with Dirty / Clean / Cold / Hot states (ocean not drinkable); food states and eating feedback; configurable food poisoning; campfire; ScriptableObject crafting in 8 categories with the 13 core items; resources incl. rope, charcoal and shipwreck materials; resource density; tool progression; primitive building with a green / red ghost; climbing; fruit trees; injury, bleeding and bandage; inventory and hotbar 1-8; physical dropped items; journal; day / night; weather; volcano foundation; waterfall landmark; prehistoric vegetation; shaders; pooled VFX; audio; separate code systems; the required scene hierarchy; the Blender MCP pipeline; dinosaur wildlife behaviour. Test policy: compile, Console, references, imports, prefabs only. Owner additions: remove the mobile on-screen buttons from the game. The offshore volcano stays and erupts, shooting fire and flying rocks toward the island.

## ACTUALLY IMPLEMENTED

Already present before this phase and kept: survival stats and status effects, food stages, food poisoning, campfire, cooking, boiling with bubbles / steam / audio, 13 core recipes, fruit-tree climb and harvest, hurt / blood / bleeding / bandage, inventory 32 slots + hotbar 1-8, journal (6 categories), day phases, weather, the waterfall chain (14.5 m drop), all shaders, 38 pooled VFX, audio.

New or finished in this phase:
- Mobile controls removed from PC: touch canvas (43 objects), MobileHUD component and the Settings "Touch controls" rows removed from the scene. MobileHUD compiles only for Android / iOS, and re-bakes cannot add it back.
- Building: Foundation, Wall, Doorway wall with a hinged door, Roof and Leaf shelter, as StructureDefinitions + prefabs + recipes, with socket snapping. The ghost checks ground, slope, water, support, distance, overlap and resources, and a dust puff plays when a piece appears. The Console error `BuildSystem.Start()` is fixed.
- Water: Cold / Hot state on each water stack. Hot water cools after 240 s, and hot clean water warms the player in cold, rain or night. Boiling salt water is refused, so the ocean is never drinkable. Water containers show in the hand.
- Crafting: Fire and Storage categories added (campfire and torch under Fire, storage under Storage). New recipes cloth_bandage, scrap_rope and nailed_crate. Now 50 items and 35 recipes.
- Charcoal: 1-2 pieces are left by a campfire that burns out on its own. The pieces can be picked up and are saved.
- Shipwreck materials: Shipwreck Scraps, Iron Nails and Sailcloth items, with 10 salvage nodes at the wreck.
- Dropped items are physical (Rigidbody + collider), settle, can be picked up, return to the inventory and are saved.
- Tool progression: trees and logs need an axe, and boulders need a pick. Hands gather branches, loose stones, fiber, berries and fruit. The start area has enough hand resources for the first axe, pick, knife and campfire. 15 nodes that sat in water and 309 floating or buried nodes were fixed.
- Climbing: 10 ledge and rock-face climb spots (start beach, cliffs, waterfall route) placed by a builder.
- Player animation: 89 clips imported. There are real clips for Punch 1/2/3, Heavy (overhand), Combo End, Kick, Block, Gather Branch, Gather Stone, Drink Kneel, Collect Water, Bandage and Unconscious, plus a new Collapse state. All earlier clips are kept (at most 0.19 deg difference). The player prefab is unchanged.
- Wildlife:
  - Weather reaction: rain and storms shelter herds, land pteranodons, reduce calls and senses, and make predators lie low.
  - Predator hunting: one hunt at a time, with a cooldown. A herd of 3 or fewer is never hunted, and there is no hunting near the spawn by day. A kill leaves a Carcass.
  - Rest and sleep use the new dinosaur Rest clips on the 7 land species, which were rebuilt from the undeployed D2 package.
- Environment:
  - All 23 PC environment models placed: tree ferns, cycads, vines, moss rocks, roots, basalt, giant ferns.
  - Waterfall dressing: moss / fern curtains, wet mossy boulders, ferns, horsetails.
  - River meshes that were facing down (invisible) were fixed.
  - Volcanic ground: ash, basalt and small lava areas in the volcano zone, with heat hazard zones.
  - The petroglyph carvings were replaced by predator claw gouges and castaway tally marks, so there are no ancient structures.
- Offshore volcano: kept and erupting.
  - Timing: a rumble build-up, then a fire fountain, flash, smoke and a delayed boom, every 120-360 s.
  - Bombs: 3-12 glowing volcanic bombs arc toward the island. About 70 % land in the sea (splash, steam, hiss) and 30 % on the shore (embers, fire, cooling rock).
  - Danger: 25 damage within 2.5 m of the player (configurable, 0 = off) and camera shake. No bomb lands within 120 m of the spawn.
  - The copy of the volcano behaviour on the island was switched off.
- Scene hierarchy reorganised to World (Environment / Gameplay / WorldSystems), Lighting, VFX, Audio, Managers. The move used 62 moves, no world positions changed, and path lookups now go through `Core/SceneRoots.cs`.
- Salt-water texts and tests updated to the new rule. Test for touch buttons is ignored on PC.

## LOCATION

Scene for every system: `Assets/_Project/Scenes/Island_VerticalSlice.unity`.

- Mobile controls removal
  - Scene: Managers/[UI] (touch objects removed)
  - GameObject: none left
  - Prefab: n/a
  - Script: `Scripts/UI/MobileHUD.cs`, `Core/GameManager.cs`, `UI/SettingsPanel.cs`, `UI/ContextHints.cs`, `Editor/PrimalPcCleanup.cs`
  - ScriptableObject: n/a
  - Blender asset: n/a
- Building
  - Scene: World/Gameplay/Structures
  - GameObject: placed pieces at runtime
  - Prefab: `Prefabs/Building/BLD_{Foundation,Wall,Doorway,Roof,LeanTo}.prefab`
  - Script: `Scripts/Building/BuildSystem.cs`, `StructurePiece.cs`, `DoorPiece.cs`, `Editor/PrimalBuildingBuilder.cs`
  - ScriptableObject: `Resources/Structures/STR_*.asset`, `Data/Recipes/RCP_*` (5 pieces)
  - Blender asset: `Art/Models/Building/BLD_*.fbx`
- Survival stats / status effects
  - Scene: World/Gameplay/Player
  - GameObject: Player
  - Prefab: `Prefabs/Player/PFB_Player.prefab`
  - Script: `Scripts/Survival/PlayerSurvival.cs`, `PlayerStatusEffects.cs`, `SurvivalEnvironment.cs`, `Player/PlayerHealth.cs`
  - ScriptableObject: `Resources/SurvivalConfig.asset`, `Resources/StatusEffects/SE_*`
  - Blender asset: n/a
- Water and purification
  - Scene: World/Gameplay/WaterSources, World/Environment/Ocean
  - GameObject: WaterSource objects, ocean shore
  - Prefab: `PFB_Hand_{water_container,leather_waterskin,leaf_cup}.prefab`
  - Script: `Survival/WaterRules.cs`, `Items/ItemStack.cs`, `World/WaterSource.cs`, `World/OceanShore.cs`, `World/Campfire.cs`
  - ScriptableObject: `Resources/SurvivalConfig.asset`, `Data/Items/ITEM_water_container` / waterskin / leaf cup
  - Blender asset: `ITEM_WaterContainer.fbx`, `ITEM_LeafCup.fbx`
- Food / poisoning
  - Scene: resource nodes, carcasses
  - GameObject: n/a
  - Prefab: `Prefabs/Resources/*`
  - Script: `Survival/Spoilage.cs`, `Survival/SurvivalItemUse.cs`, `World/Carcass.cs`
  - ScriptableObject: `Data/Items`, `Data/Food`, `SurvivalConfig.asset`, `SE_sickness`
  - Blender asset: existing food models
- Campfire / charcoal
  - Scene: built by the player
  - GameObject: campfire instances
  - Prefab: `Prefabs/Gameplay/PFB_Campfire*`, `Prefabs/Gameplay/PFB_Charcoal.prefab`
  - Script: `World/Campfire.cs`, `VFX/CampfireFx.cs`
  - ScriptableObject: `Data/Items/ITEM_charcoal.asset`, `RCP_campfire`
  - Blender asset: n/a (charcoal built from primitives)
- Crafting
  - Scene: Managers/[UI] crafting panel
  - GameObject: n/a
  - Prefab: n/a
  - Script: `Items/CraftingSystem.cs`, `Items/ItemEnums.cs` (RecipeCategory), `UI/InventoryUI.cs`
  - ScriptableObject: `Resources/ItemDatabase.asset`, `Data/Recipes/RCP_*` (35)
  - Blender asset: tool / weapon models
- Resources / shipwreck / gating
  - Scene: World/Gameplay/Resources/{[Resources], Resources}, World/Gameplay/Structures/Shipwreck
  - GameObject: 1435 active nodes
  - Prefab: `Prefabs/Resources/*`, salvage node prefabs
  - Script: `World/ResourceNode.cs`, `TreeHarvest.cs`, `GatheringSystem.cs`, `GatherToolDefinition.cs`, `Editor/PrimalResourceBuilder.Phase1.cs`, `Editor/PrimalResourceBuilder.Lava.cs`
  - ScriptableObject: `Resources/ResourceDatabase.asset`, `RES_salvage_*`, `ITEM_wreck_scraps` / `wreck_nails` / `sailcloth`
  - Blender asset: generated salvage meshes
- Dropped items
  - Scene: runtime
  - GameObject: WorldPickup instances
  - Prefab: item world prefabs
  - Script: `World/WorldPickup.cs`
  - ScriptableObject: item definitions
  - Blender asset: n/a
- Climbing / fruit trees
  - Scene: World/Gameplay/Interactables/{Climbables, FruitTrees}
  - GameObject: `Climb_01..10_*`, 4 fruit trees
  - Prefab: n/a
  - Script: `Player/PlayerClimb.cs`, `World/Climbable.cs`, `World/FruitCluster.cs`, `Editor/PrimalClimbBuilder.cs`
  - ScriptableObject: n/a
  - Blender asset: climb clips in `PLAYER_Survivor.fbx`
- Player animation / bare hands
  - Scene: World/Gameplay/Player
  - GameObject: Player
  - Prefab: `Prefabs/Player/PFB_Player.prefab`, `Art/Characters/Player/Prefab/PFB_Player_Survivor.prefab`
  - Script: `Player/PlayerAnimationDriver.cs`, `Player/PlayerCombat.cs`, `Player/PlayerFacial.cs`, `Animation/CharacterAnimationEvents.cs`, `Editor/PrimalCharacterBuilder*.cs`
  - ScriptableObject: `Resources/Combat/WPN_bare_hands.asset`, `Art/Characters/Player/Animations/PlayerAnimator.controller`
  - Blender asset: `E:\Model game khủng long\characters\PLAYER_Survivor_v2_work.blend`, `export\staging\PLAYER_Survivor.fbx` (89 clips) -> `Art/Characters/Player/Model/PLAYER_Survivor.fbx`
- Wildlife / perception
  - Scene: World/Gameplay/Wildlife/[Dinosaurs]
  - GameObject: dinosaur herds and loners
  - Prefab: `Prefabs/Dinosaurs/*`
  - Script: `AI/DinosaurController.cs`, `AI/WildlifeWeather.cs`, `AI/HuntDirector.cs`, `AI/DinoAnimCaps.cs`, `AI/DinoSenses.cs`, `AI/HerdGroup.cs`
  - ScriptableObject: `Resources/WildlifeConfig.asset`, `Resources/PerceptionConfig.asset`, `Data/Dinosaurs/DINO_*.asset`, `Art/Characters/Dinosaurs/<Species>/Animations/<Species>Animator.controller`
  - Blender asset: `E:\Model game khủng long\characters\dino_pc\DINO_Work_PC.blend`
- Journal / day-night / weather
  - Scene: World/WorldSystems/{Time, Weather}, Managers/Journal
  - GameObject: Time, Weather, Journal
  - Prefab: n/a
  - Script: `Core/TimeManager.cs`, `Core/WeatherManager.cs`, `Story/JournalSystem.cs`, `UI/JournalUI.cs`
  - ScriptableObject: `Resources/WeatherConfig.asset`
  - Blender asset: n/a
- Offshore volcano eruption
  - Scene: World/Environment/Volcano/Landmarks/Volcano
  - GameObject: PFB_ENV_Volcano (-360, -2, 640)
  - Prefab: `Prefabs/Environment/PFB_ENV_Volcano.prefab`, `Prefabs/Environment/PFB_ENV_VolcanicBomb.prefab`
  - Script: `World/VolcanoLandmark.cs`, `Editor/PrimalVolcanoBuilder.cs`
  - ScriptableObject: `ME_VolcanicBomb.asset`, `M_VolcanicBomb.mat`
  - Blender asset: volcano landmark model (existing)
- Island volcanic ground
  - Scene: World/Environment/Volcano/{Basalt, Lava, Hazards}
  - GameObject: basalt groups (47), lava channel, heat zones HZ_volcano / HZ_vent
  - Prefab: ENV_PC basalt
  - Script: `Editor/PrimalEnvironmentBuilder.Volcano.cs`, `Editor/PrimalAtmosphereBuilder.cs`
  - ScriptableObject: n/a
  - Blender asset: `ENV_PC_Kit_20260929.blend`
- Waterfall / vegetation
  - Scene: World/Environment/{Waterfalls, Forest, Rivers, Wetlands}
  - GameObject: Waterfall, WaterfallDressing, ForestDressing
  - Prefab: `ENV_PC_*` prefabs
  - Script: `Editor/PrimalEnvironmentBuilder*.cs`, `Editor/PrimalWaterBuilder.cs`
  - ScriptableObject: terrain data `TD_Island`
  - Blender asset: `ENV_PC_Kit_20260929.blend`, `scripts/env_pc/*.py`
- Story props (no ancient structures)
  - Scene: World/Gameplay/Interactables/Storytelling
  - GameObject: PROP_PC_ClawRock, PROP_PC_CastawayMarks
  - Prefab: same names
  - Script: `Story/StoryTexts.cs`, `Editor/PrimalEnvironmentBuilder.Story.cs`
  - ScriptableObject: n/a
  - Blender asset: `scripts/env_pc/env_pc_story_v2.py`
- Scene hierarchy
  - Scene: roots World, Lighting, VFX, Audio, Managers
  - GameObject: n/a
  - Prefab: n/a
  - Script: `Core/SceneRoots.cs`, `Editor/PrimalHierarchyBuilder.cs`
  - ScriptableObject: n/a
  - Blender asset: n/a
- QA checks
  - Scene: n/a
  - GameObject: n/a
  - Prefab: n/a
  - Script: `Editor/PrimalQaCheck.cs` (read-only checks)
  - ScriptableObject: n/a
  - Blender asset: n/a

Agent reports: `Documentation/Phase1/_{B,U,U2,S,S2,R,R2,P,D,E,E2,C,H,Q}_report.md`.

## STATUS

"PASS" here means the code compiles, the Console is clean and the references are verified in the editor. Nothing was checked in Play mode.

| System | Status |
|---|---|
| Compile / Console (0 errors, 0 warnings) | PASS |
| Scene references (8901 objects, 0 missing scripts, 0 missing references) | PASS |
| Prefabs (181 checked, 0 broken; 2428 instances connected) | PASS |
| Asset import (0 script / import errors) | PASS |
| Mobile controls removed | PASS |
| Building pieces + ghost | PASS |
| Water states / ocean not drinkable / container in hand | PASS |
| Crafting categories + recipes | PASS |
| Charcoal, shipwreck materials | PASS |
| Physical dropped items | PASS |
| Tool progression | PASS |
| Climbing (trees, ledges, rock faces) | PARTIAL (ledges / rock faces use tree-climb clips) |
| Player clips / bare-hand combat | PASS |
| Wildlife weather, hunting, rest / sleep | PASS |
| Environment vegetation, waterfall, rivers | PASS |
| Offshore volcano eruption | PASS |
| Scene hierarchy | PASS |
| Gameplay in Play mode | NOT TESTED |

## CRITICAL ERRORS

None. The final Console check shows 0 errors and 0 warnings, and there are no missing scripts, missing references or broken prefabs.

## MANUAL PLAYTEST

NOT PERFORMED.

Developer will test manually.

## KNOWN LIMITATIONS

- Ledges and rock faces reuse the tree-climb clips. Ledge_Mantle and Climb_Rock_* exist in the FBX but are not wired.
- Nothing calls the new Unconscious_Collapse state yet: there is no knockout gameplay, only the intro.
- The Kick is rear-leg only.
- The shared lying pose (Wake_Up, Death end, Get_Up, Unconscious) has the heels slightly raised: about 19 cm left, 5 cm right.
- Bow and spear idle / draw clips are still missing: Bow_FullDraw, Bow_Equip, Bow_Nock, Bow_Idle, Spear_Idle, Spear_Recovery, Butcher.
- The door is part of the doorway wall, not a separate piece that snaps in.
- Hunts are solo, with no raptor packs. The mosasaur ignores weather. A pteranodon whose flight circle is entirely over the sea keeps flying in storms.
- Dinosaurs do not use Turn in place, Stop or Recover yet.
- Volcano sounds reuse existing clips at a lower pitch. There is no crater decal.
- Warmth from a hot drink is not saved.
- There are two resource sets (`[Resources]` and the old `World/Resources`). They do not overlap, but they were not merged.
- 74 rocks share the name `PFB_ENV_Rock_Large_03`.
- The from-scratch generators (`PrimalGameplayBuilder`, `PrimalWorldBuilder`, `PrimalPlayerSetup`) still write the old root names. Do not run them on this scene.
- Objects created in Play mode (pools, `[TreeStumps]`, `[HazardMonitor]`) appear at the root.
- PlayMode tests that were edited (salt water, touch, SurvivalPcPhase) were only compiled, not run.
