# PRIMAL FRONTIER — Project Brief (source of truth)

Received from the project owner on 2026-09-26. Structured copy of all 54 sections. When in doubt, section 52
(Scope Rule) wins over everything else.

## Identity and IP (header, s1)
- Working title **PRIMAL FRONTIER** (repo folder: LAST OF THE PRIMAL). 3D single-player survival adventure.
- Unity 3D + C#, PC first, third-person camera. Art pipeline Blender MCP -> Blender -> Unity.
- ORIGINAL IP. ARK is a high-level genre reference only. Never use ARK assets, UI, logos, creatures as exact copies,
  maps, item icons, names, text, quest structure, textures, animations, exact progression or copied behaviour.
- Core fantasy: "I am alone in a prehistoric wilderness. I have nothing. I must learn how to survive."
  Player feels vulnerable, curious, isolated, threatened, rewarded by exploration, proud of learning, afraid of the unknown.
  Not a power fantasy at the start; the player starts extremely weak.

## World (s2-s4)
- Fictional alternate-prehistory island inspired by prehistoric Americas, western frontier landscapes, tropical
  wilderness and Cretaceous-inspired wildlife. Not strict history. Feels old, wild, untamed, beautiful, dangerous, mysterious.
- Vertical slice = ONE small island, ~500 x 500 m playable (may change for performance). Contains: starting beach,
  wrecked ship, medium forest, small grassland, freshwater stream/pond, rocky area, small cliff, cave entrance,
  camp area, dinosaur habitats, resource areas. Do not expand until the slice is complete.
- Layout: ocean + shipwreck (south) -> start beach -> water source -> meadow flanked by forest -> rocky high ground
  with cave (north). Danger increases inland.

## Visual direction (s5)
- Realistic-stylized premium indie survival look. NOT cartoon/mobile/anime/pixel/toy/flat low-poly.
- PBR, natural lighting, realistic proportions, strong silhouettes, foliage variation, cinematic atmosphere.
- Palette: warm earth, deep forest green, rock gray, sand, mud, natural brown, subtle warm sun, cool night. No oversaturation.

## Blender (s6-s8, s42, s47)
- Inspect real MCP tools first; never invent APIs; verify before assuming; build in stages; save checkpoints; export to Unity;
  split complex assets into steps; never fail silently; state exact limitations.
- Scene `PRIMAL_FRONTIER_WORLD`, collections 00_Blockout, 01_Terrain, 02_Forest, 03_Rocks, 04_Shipwreck, 05_Props,
  06_Resources, 07_Dinosaurs, 08_Player, 09_Weapons, 10_VFX, 11_Lighting.
- Names: ENV_Island_Terrain, ENV_Beach_01, ENV_Rock_Large/Medium/Small_01, ENV_Tree_01..03, ENV_Fern_01, ENV_Bush_01,
  ENV_Grass_01, PROP_Ship_Hull/Mast/Deck/Crate/Debris, PROP_Campfire, PROP_Shelter, PROP_WoodPile, RES_Wood_01,
  RES_Stone_01, RES_Fiber_01, RES_BerryPlant_01, DINO_Triceratops/Parasaurolophus/Ankylosaurus/Velociraptor/
  Carnotaurus/ApexPredator, PLAYER_Survivor, WEAPON_StoneAxe/StonePick/StoneSpear/Bow.
- Hero priority: player, dinosaurs, shipwreck, landmarks, weapons, campfire, shelter. Background assets cheaper.
- Every model: correct scale, applied transforms, correct origin, clean topology, sensible polycount, UVs, materials,
  clean naming, no hidden geometry, few material slots, no gigantic textures (no 4K for tiny rocks), LOD-ready.

## Player (s9)
- One adult human survivor, simple dirty/wet/torn survival cloth wrapped at waist/body. Not sexualized, no explicit
  anatomy. Barefoot, no shirt/armor, wet skin/hair, believable proportions. Later: leather wraps, sandals, primitive
  armor, bone accessories, cloth protection. No customization in v1.

## Shipwreck (s10)
- Broken wooden hull, damaged deck, broken mast, rope, torn sail, crates, scattered wood, debris, damaged cargo, partly
  buried. Storm-destroyed look. Gives short shelter, cloth, rope, wood, limited supplies, story clues. No guns, no modern
  or military items. Explorable in 3-5 minutes.

## Environment (s11, s33)
- Beach, forest, grassland, rocks, cave, freshwater, cliffs, mud, fallen trees, bushes, ferns, vines, prehistoric plants.
  Several tree variations; instancing/reuse.
- Environmental storytelling: large footprints, claw marks, bones, broken trees, blood traces, abandoned camp,
  cargo, cave markings, strange stones. Do not explain mysteries immediately.

## Dinosaurs (s12-s14, s21-s22)
- Original designs (proportions, colours, markings, silhouettes, textures, behaviour). Slice: 3 herbivores
  (Triceratops, Parasaurolophus, Ankylosaurus), 2 predators (Velociraptor, Carnotaurus), 1 apex (Tyrannosaurus-inspired,
  not copied from any game).
- Animals, not random spawns: habitat, territory, wander, feed, rest, drink, alert, flee, hunt, attack.
  Triceratops peaceful unless attacked/approached/threatened/protecting young. Parasaurolophus groups, alert calls,
  flees. Ankylosaurus slow, defensive, tail attack. Velociraptor fast, small-group hunter. Carnotaurus territorial,
  aggressive, chases. Apex rare, extremely dangerous, builds tension before appearing.
- Predator psychology: warn before seeing (distant roar, footprints, broken branches, blood, disturbed vegetation,
  fleeing animals, shadows, silence). E.g. ROAR -> objective "Something is nearby." Never spawn in front of the player.
- Hunting is risky: small prey manageable, big herbivores dangerous when provoked, predators extremely dangerous,
  apex avoid unless heavily prepared.
- Lightweight FSM: Idle, Wander, Patrol, Eat, Drink, Rest, Observe, Investigate, Alert, Flee, Chase, Attack,
  ReturnToTerritory. Senses: sight, distance, noise. AI LOD: near full, medium simplified, far very light, very far disabled/pooled.

## Survival (s15-s18)
- Stats: Health, Hunger, Thirst, Stamina, Temperature (optional Wetness, Bleeding, Injury). Hunger slow, thirst faster,
  sprint uses stamina, rain/night lower temperature, extreme hunger/thirst damage health, low stats hurt performance.
  Survival feeling, not accounting software.
- Water: ocean NOT drinkable; freshwater stream/pond drinkable; basic water container, later waterskin.
- Food: berries, plants, fish, small animals, dinosaur meat. Raw meat risky; cooked safer and restores more. 5-8 food items.
- Resources: wood, stone, fiber, plant material, berries, water, hide, bone, meat, rope, charcoal; rare metal scrap from wreck.
  Gathering needs interaction, animation, sound, particles, resource depletion; pooling.

## Crafting, weapons, structures (s19-s20, s23-s25)
- ScriptableObjects ItemDefinition / RecipeDefinition. 15-25 recipes max. Categories TOOLS, WEAPONS, SURVIVAL, FOOD, STRUCTURES.
  Starters: Stone Axe, Stone Pick, Stone Knife, Stone Spear, Torch, Campfire, Water Container, Basic Shelter,
  Primitive Bow, Arrows, Storage, Simple Bedroll.
- Weapons: stone spear, thrown spear, primitive bow. Skill-based, simple, responsive (distance, stamina, timing, positioning).
- Campfire: warmth, light, cooking, safe landmark; Light/Cook/Extinguish; fire, smoke, embers, light, audio; controlled realtime light.
- Shelter: simple wood/leaf; sleep, save, respawn, temporary safety; prefab placement, no modular base building.
- Inventory: grid, stacks, weight/capacity, hotbar 1-8; equip/use/drop/craft; fast.

## Journal (s26, s37)
- Signature feature: handmade survival journal (leather cover, aged paper, sketches, notes). Categories SURVIVAL,
  CRAFTING, CREATURES, RESOURCES, DISCOVERIES (UI sections SURVIVAL, CREATURES, CRAFTING, WORLD). Knowledge unlocked by
  discovery. Page transitions, sketch, text, discovery status. Key J.

## Tutorial, loop, story (s27-s30)
- Opening: black screen, ocean, wind, storm, crash, fade in, wake on beach. Objective "SURVIVE UNTIL NIGHT."
- 21 tutorial steps: look, walk, approach wreck, wood, stone, open crafting, craft axe, fiber, freshwater, food,
  campfire, cook, spear, explore forest, tracks, observe herbivore, predator warning, return, shelter, survive night,
  morning -> "Day One Survived."
- Chapter 1 "DAY ZERO": storm, wake, wreck, supplies gone, giant footprint, dinosaur heard, freshwater, crafting,
  predator territory, first camp, night, sounds around camp, sunrise. End: beach at sunrise, huge silhouette crosses
  the ridge, the island is larger than expected. CUT.
- Future chapters 2-7 are documentation only; do not implement.

## Time, weather (s31-s32)
- Day, sunset, night, sunrise. Night: lower visibility, colder, more danger, different animal behaviour; not impossible.
- Weather: Clear + Rain (temperature, visibility, wet ground look, fire, atmosphere). No complex simulator.

## UI (s34-s38)
- Premium PC survival HUD: dark transparent panels, subtle leather/cloth/wood accents. No mobile/cartoon/sci-fi.
  Top-left Health/Hunger/Thirst/Stamina/Temperature; bottom hotbar 1-8; centre minimal prompt; top-right compass;
  small brief notifications.
- Inventory (TAB): character preview, grid, item info, weight; Equip/Use/Drop/Craft.
- Crafting: categories, icon, name, requirements, description, craft button, missing materials clear.
- Settings: master/music/SFX volume, mouse + camera sensitivity, quality, resolution, fullscreen, VSync; presets Low/Medium/High/Ultra.

## Save (s39)
- Save player position, health, hunger, thirst, temperature, inventory, hotbar, structures, campfire, journal,
  recipes, world time. Compact versioned data, never the scene hierarchy. `SaveSystem.cs`.

## Architecture (s40)
- Core: GameManager, GameStateManager, TimeManager, WeatherManager, SaveManager.
- Player: PlayerController, PlayerCamera, PlayerInteraction, PlayerSurvival, PlayerInventory, PlayerCombat.
- Crafting: InventorySystem, CraftingSystem, ItemDefinition, RecipeDefinition.
- World: ResourceNode, WaterSource, Campfire, Shelter, Interactable.
- Wildlife: DinosaurDefinition, DinosaurController, DinosaurAI, DinosaurSpawner.
- UI: HUDManager, InventoryUI, CraftingUI, JournalUI, SettingsUI.
- ScriptableObjects for items, recipes, dinosaur stats, weapons, food, loot.

## Performance, VFX, audio, feel (s41, s43-s45)
- LOD, occlusion + frustum culling, GPU instancing, shared materials, pooling, efficient colliders, limited realtime lights.
- VFX: campfire, rain, dust, water splash, footstep dust, weapon impact, moderate blood, dino footsteps/dust,
  environmental particles; pooled, no particle floods.
- Audio: ocean, wind, rain, forest, birds, insects, footsteps, gathering, crafting, campfire, water, dino footsteps,
  breathing, calls, roars, weapons. Hear the ecosystem before seeing it. Original or properly licensed audio only.
- Feel: lonely, dangerous, natural, immersive, primal, mysterious, grounded. Every tool valuable; every predator tense.

## Content limit (s46) and Scope rule (s52)
1 island, 1 shipwreck, 1 cave, 1 water source, 1 shelter, 1 campfire system, 3 herbivores, 2 predators, 1 apex,
15-25 recipes, 10-20 resources, 1 player, 3 primary weapons, 1 journal, 1 day/night cycle, 1 rain system. STOP.
DO NOT make the world bigger, add islands/biomes/20 more dinosaurs/multiplayer/base-building/vehicles/online/complex quests.

## Development order (s48) and checkpoints (s49)
1 audit, 2 Blender blockout, 3 terrain+beach+forest+shipwreck, 4 player, 5 movement/camera, 6 gathering, 7 inventory,
8 crafting, 9 campfire, 10 food/water, 11 shelter, 12 first dinosaur, 13 dino AI, 14 predators, 15 hunting/combat,
16 journal, 17 day/night, 18 rain, 19 tutorial/story, 20 UI polish, 21 save, 22 audio/VFX, 23 performance, 24 QA.
Save + commit after each major phase ("checkpoint world blockout", "checkpoint player", ...).

## QA (s50), polish (s51), Definition of Done (s53), report (s54)
- Full loop test from launch to save/load/continue; no NullReferenceException, missing refs, broken prefabs, infinite
  loading, stuck AI, duplicate items/resources, broken save, inventory corruption, major FPS drops.
- Polish pass on movement, camera, lighting, materials, water, foliage, animation, UI, audio, VFX, interaction, crafting, feedback.
- DoD checklist of 31 items (see s53). Report `Documentation/PRIMAL_FRONTIER_STATUS.md` with 15 sections. Never claim
  completion without implementation + test; untestable items marked "NOT TESTED — REQUIRES MANUAL TEST".
