# Owner directive: PHASE 3, MASTER SURVIVAL SYSTEMS (verbatim content, condensed layout)

Continue the EXISTING project. Do not rebuild, do not create a new project, do not delete working systems, do not restart the
character pipeline, do not replace the player without a verified reason, do not expand the island. Goal: a REAL survival game,
not a collection of systems. ONE SMALL ISLAND + DEEP SURVIVAL INTERACTIONS. No new island, giant world, multiplayer, online
backend, economy, vehicles, hundreds of items / recipes, huge quest database.

0 Read the current project and docs; compare DOCUMENTED vs ACTUALLY IMPLEMENTED; classify IMPLEMENTED / PARTIAL / NOT IMPLEMENTED / BROKEN; then implement. (Done: Documentation/Phase3/PHASE3_AUDIT.md.)
1 Checkpoint commit before major changes (done: c7f1c588 "checkpoint character phase A before survival systems").
3 Core loop, fully playable: wake up, explore, collect, drink, eat, craft, make fire, cook, craft tool, explore, hunt / avoid, get injured, treat injury, gather more, build shelter, rain / weather, stay warm / dry, night, survive, sleep / save, morning, continue exploration.
4 Stats: health, hunger, thirst, stamina, temperature, wetness; optional bleeding, injury, food poisoning; modular status effects; no survival logic scattered in random scripts (SurvivalSystem, StatusEffectSystem, PlayerHealth, PlayerStamina).
5 Thirst drops relatively quickly, hunger more slowly. Stamina used by sprint, jump, climbing, melee, heavy actions. Temperature from time of day, weather, rain, water exposure, shelter, fire (clothing later). Wetness rises in rain / water, falls gradually, faster near fire / shelter / dry. Health reduced by attacks, starvation, dehydration, severe temperature, bleeding, environment. Not frustrating.
6 Water types OCEAN (not drinkable), FRESH, DIRTY, CLEAN. Fresh source collects as DIRTY unless explicitly safe. Collect, purify, drink.
7 Sources River, Pond, Freshwater Pool: visual water, interaction point, water level if appropriate, drink, fill. Water VFX ripple, small splash, droplets; pooling.
8 Primitive Water Container (e.g. wood + fiber), later Waterskin. Data: capacity, current amount, quality (e.g. Dirty 500 ml / Clean 500 ml). Simple values.
9 Purify at the campfire: place water, start boiling, wait, collect clean water; steam VFX, boiling sound, small bubbling. No minigame.
10 Drinking: hold / tap drink, Drink animation, small droplets VFX, container movement, thirst up, feedback "+ Hydration", never instant 100 %.
11 Dirty water may cause food poisoning / sickness: temporary stamina reduction, higher thirst, small health loss; probability and severity configurable in a ScriptableObject.
12 Food: raw / cooked / spoiled-unsafe. First foods: berries, fruit, edible plant, raw meat, cooked meat, fish, cooked fish. Data: nutrition, hydration, safety, spoil time, weight. Simple.
13 Eating: Eat animation, small food VFX, subtle hand movement, sound; hunger up; optional small health from high-quality food; no flashy magic.
14 Spoilage fresh, aging, spoiled; no per-item real-time simulation, update important inventory items efficiently; spoiled = poor nutrition, possible sickness.
15 Cooking at the campfire: raw meat -> cooked meat, raw fish -> cooked fish (optional berry / plant recipes); duration, sound, VFX; no minigame.
16 Gathering for wood, stone, fiber, plants, fruit, berries, hide, bone, meat, water: animation, sound, VFX, inventory update, feedback; one ResourceNode / IInteractable design, no unique code per resource.
17 Tools stone axe, pick, knife, spear: durability, gather efficiency, damage where appropriate; break at zero with feedback; not too punishing.
18 Crafting categories TOOLS, WEAPONS, FOOD, WATER, FIRE, BUILDING, STORAGE, SURVIVAL; 20-30 useful recipes max.
19 Required recipes: stone axe, stone pick, stone knife; stone spear, thrown spear, primitive bow, arrow; campfire, torch; water container; bedroll, storage; foundation, wall, roof, door, simple shelter; cooked meat, cooked fish.
20 Crafting feedback: animation; show required / available materials and result; small wood dust / stone particles / subtle hand effect; crafting sound; no magic VFX.
21 Campfire: fuel, fire intensity, lit, cooking, warmth radius; states Unlit, Lighting, Burning, LowFuel, Extinguished; wood fuel; weaker fire as fuel drops, VFX adjusts.
22 Campfire functions: light, add fuel, cook, boil water, warm player, sleep / rest nearby; UI clearly shows Light / Fuel / Cook / Boil Water.
23 Fire safety: the "safe zone" in docs is NOT implemented until code exists. Make it CONFIGURABLE (CampfireFearRadius, DayRadius, NightRadius, RainMultiplier, FuelMultiplier, PredatorFear), not hard-coded to one species: small predators strongly avoid, large less afraid, apex may ignore under conditions.
24 With an active fire wildlife may avoid, observe, circle, wait outside range, leave; not everything flees instantly.
25 Rain: light rain lowers intensity, heavy rain may extinguish, sheltered fire more protected.
26 Firewood: fire consumes fuel; player periodically collects wood and adds fuel; not every minute.
27 Building: foundation, wall, roof, door, storage, bedroll, campfire; prefab-based, no voxels.
28 Building mode: enter, select, ghost preview green valid / red invalid, place, consume resources, build VFX, complete; validate ground, slope, collision, distance, resources.
29 Shelter gives rain protection, sun protection, temperature protection, sleep, save, respawn; a real reason to build.
30 Weather clear, cloudy, rain, storm; affects temperature, wetness, visibility, fire, wildlife, sound, water surface; controlled probability / duration, not every few minutes.
31 Day / night: morning, day, afternoon, sunset, night, dawn; night lower visibility / temperature, different animals, more danger, not impossible.
32 Temperature from sun, shade, water, rain, night, fire, shelter; simple.
33 Sunlight atmosphere: soft golden morning, warm day, strong afternoon, golden-orange sunset, cool night; directional light, sky, fog, ambient; few realtime lights.
34 Forest wind: leaves more, branches moderate, trunks very subtle.
35 Water shader: moving surface, subtle normal distortion, depth colour, shoreline foam, ripples, small splash, rain impact; mobile friendly, no full-screen effects.
36 Wet surfaces in rain (rocks, wood, ground) via shared shader parameters, no duplicated materials.
37 Volcano landmark: mountain, ash, smoke, heat haze, embers, lava cracks, small lava flow.
38 Volcano danger zone: temperature up, heat warning, reduced stamina, environmental damage, smoke; warning states, not instantly lethal.
39 Injury states: bleeding (health drains), leg injury (slower), arm injury (weaker attacks), heavy damage; not a medical simulator.
40 Bleeding when badly attacked; small status icon; health drain; bandage treats.
41 Bandage: craft from fiber / cloth; stops bleeding; animation Bandage_Use; very subtle VFX.
42 Healing gradual: bandage stops bleeding, cooked food small regeneration, rest gradual recovery; configurable.
43 Combat: spear, knife, bow; light, heavy, throw; anticipation, strike, impact, recovery; animation events for hit timing.
44 Dodge: short burst, small invulnerability, cooldown / stamina; dust VFX, sound, animation.
45 Climbing: designated trees, short ledges, small rocks, not everything; ClimbStart / Climb / ClimbEnd; hand / foot IK where practical.
46 Fruit trees: approach, climb, reach, harvest, inventory, small fruit VFX, climb down; respawn later.
47 Inventory: stacking, weight, capacity, durability, spoilage, water amount, equipment; hotbar 1-8; quick-use food, water, weapon, torch.
48 HUD: top-left health, stamina, hunger, thirst, temperature; wetness small indicator; bleeding icon; bottom hotbar 1-8; top-right minimap; centre prompt.
49 Minimap required, top-right: player, north, camp, water, shipwreck, cave, volcano, discovered landmarks; do not reveal all dinosaurs, all resources, unknown areas.
50 Compass N NE E SE S SW W NW, small.
51 Journal sections SURVIVAL, CRAFTING, CREATURES, RESOURCES, WORLD; pages unlock by exploration; creature page: name, appearance, habitat, behaviour, danger, known observations; not everything at once.
52-56 Perception: vision (distance, FOV, line of sight, light level, foliage cover; timed checks, no per-frame raycasts), hearing (running, gathering, attacks, building, chopping, large footsteps; noise attracts, e.g. chopping -> predator investigates), lightweight scent (raw meat, cooked meat, blood, injury; source position, strength, decay, radius; timed sampling; cooked meat weaker; raw strong; blood strong; campfire smoke may influence); all configurable.
57 Predator responses: ignore, investigate, approach, circle, attack, retreat; no forced instant attack; tension.
58 Campfire + predators: FearRadius, NightMultiplier, RainMultiplier, FuelMultiplier; species differ; campfire not universally safe.
59 Dinosaurs eat, drink, wander, rest, sleep, react, search, flee, hunt; not all constantly aggressive.
60 Dinosaur animation polish: eyes, jaw, head, tail, feet, weight, idle breathing, walk, run, turn, attack, hurt, death; fix foot sliding, skating, rigid movement, unnatural turns, deformation.
61 Player animation polish (keep Phase A): turn foot slide, chest / hip rotation, first-frame interaction hitch, arm swing, shoulders, pelvis, head stabilization, weight transfer.
62 Ground adaptation: foot IK on uneven terrain; no floating, sinking, knee snapping; efficient.
63-64 All interactables through the interaction API (prompt, icon, action); contextual prompts: Drink, Fill Container, Gather, Chop, Harvest, Light Fire, Add Fuel, Cook, Boil Water, Climb, Build, Sleep.
65 Building save: structure id, position, rotation, state, fuel; not the whole scene.
66 Save: player position, health, survival stats, inventory, equipment, hotbar, recipes, journal, time, weather, campfires, shelters, storage, structures, resource state; versioned.
67 Save safety: new, load, overwrite, corrupted-save fallback, versioning; never crash on partly invalid data.
68-69 Audio: player footsteps, breathing, hurt, attack, gather, craft, eat, drink, climb; environment ocean, wind, leaves, rain, thunder, water, fire, volcano; wildlife footsteps, breathing, eating, drinking, calls, roars; reacts to environment; footsteps per surface sand, dirt, mud, rock, wood.
70 VFX library (pooled): footsteps sand / dirt / mud, wood impact, stone impact, plant gather, water splash / drink / boil, campfire / smoke / embers, rain / heavy rain / lightning, hurt, blood small, bleed, heal, craft, build, volcano smoke, ash, lava, heat.
71 Shader library: water, foliage wind, wet surface, lava, heat haze, dirt variation; nothing unnecessarily expensive.
72 Resource visual states: tree full / damaged / harvested; fruit tree fruit / removed / regrow; stone full / damaged / depleted; lightweight.
73 World discovery: shipwreck, freshwater, forest, cave, volcano, high ground, dinosaur territory; small journal entry; no giant map reveal.
74-75 Tutorial through gameplay, paced: move, gather, craft axe, find water, find food, make fire, cook, craft spear, explore, encounter dinosaur, get injured / understand danger, return, build shelter, night, survive.
76 Opening: storm, shipwreck, blackout, wake up (eyes open, breathing, ocean, sand, wreckage, get up, control returns); objective "SURVIVE THE FIRST DAY"; no long cinematic.
77-78 First day: morning safe-ish gathering, midday exploration, afternoon first dinosaur, late afternoon predator signs, sunset realization, night needs food / water / fire / shelter, morning "DAY ONE SURVIVED". First night atmosphere: distant calls, forest noises, wind, fire crackle, occasional footsteps; no endless enemies.
79 Difficulty believable but playable; time to learn.
80-85 Mobile: left joystick, right look, jump, attack, interact, sprint, crouch, climb, contextual buttons; touch-friendly inventory; touch building (select, rotate, move, confirm); performance (LOD, culling, shared materials, GPU instancing, compressed textures, few realtime lights, particle pooling, AI tick intervals, animation culling); AI tiers near / medium / far / very far; VFX limits.
86-89 Modular systems (no giant PlayerController), ScriptableObject data (ItemDefinition, RecipeDefinition, FoodDefinition, WaterDefinition, StructureDefinition, DinosaurDefinition, StatusEffectDefinition, FireDefinition), GameStateManager states (Boot, MainMenu, Loading, Gameplay, Inventory, Crafting, Building, Journal, Paused, Cinematic, Dead, Sleeping), maintainable hierarchy (World / Environment / Gameplay (Player, Wildlife, Resources, Structures, WaterSources, Interactables) / WorldSystems / Lighting / VFX / Audio / Managers).
90 Test scenes: Test_Player, Test_Water, Test_Cooking, Test_Fire, Test_Building, Test_Climbing, Test_Dinosaur, Test_VFX.
91 Checkpoints: survival_stats, water, food, cooking, fire, building, climbing, minimap, perception, weather, vfx, mobile, vertical_slice_phase3.
92 Automated test: new game, wake, move, gather, inventory, craft, fill water, purify, drink, eat, light fire, cook, craft spear, fight, take damage, bleed, bandage, climb, harvest fruit, build shelter, rain, enter shelter, night, save, load, morning; no NullReference / MissingReference, broken animator / prefab, duplicate resources / inventory, infinite loops, broken save, stuck player / dinosaur.
93-94 Manual playtest questions, then a polish pass (blending, camera, UI transitions, sound / VFX timing, feedback, footsteps, environment motion, lighting, water, fire, weather), no excessive effects.
95 Bug priority: P0 game-breaking, save corruption, broken movement / survival / interaction; P1 animation, AI, UI; P2 visual / audio polish; P3 optional.
96 Never claim DONE / PASS / TESTED without verification; else "NOT TESTED - MANUAL TEST REQUIRED" or "NOT COMPLETED - TOOL LIMITATION".
97-99 Vertical slice: day zero, survive day one, basic camp, survive night, reach morning, with water, food, crafting, fire, cooking, hunting, injury, healing, climbing, fruit, building, rain, shelter, night, perception, exploration. Stop when complete; next is polish, profiling, content quality, release prep.
100 Docs: Documentation/PRIMAL_FRONTIER_SURVIVAL_SYSTEMS.md and PRIMAL_FRONTIER_SURVIVAL_TEST_REPORT.md with actual results. Final report items 1-20 with PASS / PARTIAL / FAIL / NOT TESTED.
Loop the owner drew: rain -> wet -> colder -> fire -> (warm up | cook) -> cooked food -> food smell -> predator -> combat -> bleed -> bandage -> build shelter -> night -> save / sleep -> morning.
