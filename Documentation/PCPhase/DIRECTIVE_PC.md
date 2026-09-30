# Owner directive: PC-FIRST IMMERSIVE SURVIVAL EXPANSION (2026-09-29, condensed, verbatim meaning)

Owner note (Vietnamese, translated): "This game is the prehistoric dinosaur era: every model must fit that world."
No modern objects, shapes, materials or decoration anywhere. Primitive human survival gear only (wood, stone, fiber,
leaves, hide, bone, rope, shipwreck scraps).

Continue the EXISTING project. Do not rebuild, do not delete working systems, do not reset the player, keep the
animation work. PC QUALITY > MOBILE (mobile is not a priority now; keep the architecture clean enough to optimize later).
Priorities: IMMERSION > MOBILE, REALISM > FEATURE COUNT, WORLD QUALITY > WORLD SIZE, GAMEPLAY DEPTH > MAP SIZE.

1 Fantasy: a human survivor, not a superhero, alone on a prehistoric island that does not care about them.
2 Prehistoric look: giant ferns, cycads, tree ferns, primitive vegetation, dense foliage, moss, vines, fallen trees,
  ancient rocks, mud, wet ground, large roots, primitive flowers, thick undergrowth, natural cliffs, rivers, coastal
  vegetation. No modern landscaping / architecture / roads / decoration / modern-looking plants / clean artificial terrain / gamey fantasy.
3 Fictional island, broad inspiration: prehistoric North / South American wilderness + Cretaceous ecology (river valleys,
  rock formations, forest, grassland, wetlands, coast, volcanic terrain). No claim of strict reconstruction.
4 Keep the island size. More DEPTH: Beach, Shipwreck, Forest, River, Waterfall, Meadow, Canyon, Wetland, Cave, Rocky
  Ridge, Volcano, Predator territory, Herbivore territory; feel larger through elevation, line-of-sight blocking, dense
  vegetation, landmarks, river paths, canyons, caves.
5 Terrain: natural slopes, elevation variation, ridges, valleys, erosion, river cuts, mud, exposed rock, roots,
  depressions, clearings; not flat, no obvious procedural pattern.
6 Landmark UPSTREAM WATERFALL: ridge -> small source -> stream -> waterfall -> pool -> river -> wetland -> sea;
  rock face, moss, ferns, mist, spray, wet rocks, rivulets, pool ripples. A memorable discovery.
7 Water sources ocean (not drinkable), river, waterfall, pond; container; find -> collect -> dirty / fresh -> purify -> store -> drink.
8 Drinking is physical: kneel / crouch at water or bring the container to the face; water sound, droplets, subtle hand
  movement, HUD hydration feedback.
9-11 Food from the world (berries, fruit, edible plants, fish, small prey, herbivore hunting); eating physical
  (hand-to-mouth, chewing, small feedback); states fresh / cooked / aging / spoiled (spoiled: sickness, stamina, thirst, minor damage).
12-13 Fire: warmth, light, cooking, boiling, drying, rest, landmark; fuel; low fuel = smaller / less warm / less light;
  heavy rain weakens or puts it out; shelter protects. Not a universal safe zone: per species FireFear, NightFear,
  Aggression, Investigation; responses avoid / observe / circle / wait / investigate / attack anyway; configurable.
14-15 Shelter from wood, fiber, leaves, rope; protects from rain, sun, temperature, night; holds bedroll, storage, fire
  nearby. Building mode physical: ghost green / red; checks ground, slope, collision, distance, resources; construction
  VFX + sound + natural material look; no futuristic effects.
16-17 Stats health, hunger, thirst, stamina, temperature, wetness (+ bleeding, injury, poisoning) interacting (rain ->
  wet -> cold -> shelter / fire; sprint -> stamina; hunger -> slower recovery; thirst -> worse stamina). Injury light /
  heavy wound, bleeding, leg, arm; treated by bandage, rest, food, water, sleep. No medical simulator.
18 Bare hands: gather, interact, defend, punch (light, heavy, combo) with hip / torso / shoulder / arm / wrist / recovery.
19 HIGH PRIORITY: player moves like a real human. Walk: opposite arm / leg, pelvis, subtle shoulder rotation, weight
  transfer, foot contact. Run: bigger stride, more torso, stronger arms. Idle: breathing, weight shift, head. Turn: feet,
  hips, torso, shoulders, head together. No mannequin.
20-22 IK where useful (feet on terrain, hands, look-at; climbing, weapons, trees, objects), not overused. Climbing on
  selected trees, short ledges, rock ledges with hands / feet / body, no teleport. Several fruit trees: approach, climb,
  reach, harvest, inventory, regrow, natural VFX.
23-25 Resource clusters (wood: branches, logs, trees, driftwood; stone small / medium / large; fiber: grass, ferns, plants;
  food: berries, fruit, plants; rare: bone, hide, meat, shipwreck scraps). Tools hands -> stone axe / pick / knife / spear -> bow.
26-28 25-40 meaningful recipes (tools, weapons, food, water, fire, shelter, storage, survival), no filler. Cooking raw ->
  cooked meat / fish, optional simple meat / fruit / plant dishes. Purification with steam, bubbles, heat, boiling + fire audio.
29-30 Journal: SURVIVAL, CRAFTING, CREATURES, RESOURCES, LOCATIONS, DISCOVERIES; learned by experience. Creature entry:
  name, appearance, habitat, behavior, diet, threat level, observed behavior (filled in by observing).
31-36 Dinosaurs are wildlife: territory, food, water, rest, migration, social behavior. Herbivore herds of 3-12, no
  perfect formations (spacing, speeds, idle timing). Herd day: morning feeding, to water, drink, rest, move, safer ground
  (world time). Some large herbivores migrate as a WORLD EVENT (e.g. meadow -> river -> waterfall -> grassland -> resting
  area). One memorable scene: from high ground the player sees a herd crossing the valley (dust, footsteps, calls, birds,
  moving vegetation); just watch. Predators tied to prey: small at forest edge, medium at river / meadow boundary, large in
  open terrain, apex in rare territories.
37-40 Perception vision (distance, FOV, LOS, vegetation, movement, light; running easier, crouching harder), hearing
  (running, jumping, chopping, mining, combat, building, falling, big impacts), scent (blood, raw meat, cooked food, dead
  animal; strength, radius, decay); timed, no fluid simulation.
41 Tracking signs: footprints, broken plants, scratches, droppings, blood, carcass -> "this creature was here recently".
42 EVERY dinosaur has readable eyes (geometry, iris, pupil, specular highlight); head and eyes react with subtle look-at.
43 Dinosaur clips: Idle, Breathing, Look, Walk, Run, Turn, Stop, Eat, Drink, Rest, Alert, Call, Hurt, Death; predators
  Chase, Attack, Bite, Recover; herbivores Flee, Defend, Charge.
44 Weight: large = heavy acceleration, heavy footsteps, body sway, tail balance; small = quick moves, quick turns; no
  identical locomotion for all.
45 Herbivores may look at the player, pause eating, move away, ignore, return to feeding. Predators observe, investigate,
  stalk, chase, attack, retreat. Not every encounter is combat.
46 Environmental storytelling: giant footprints, claw marks, bones, nest, old shelter, shipwreck remains, strange
  markings, fossil bed, broken trees, blood trail, predator territory. No constant quest markers.
47-48 Volcano landmark (mountain, ash fields, dark rock, smoke, heat haze, embers, lava cracks, small lava streams; not
  giant). Near it temperature rises, warning, stamina reduction, heat damage, smoke visibility; danger grows gradually.
49-51 Weather clear / cloudy / rain / storm (storm: heavy rain, wind, thunder, lightning, darkness); rain: wetness,
  cooling, fire weakening, visibility, sound. Natural golden sunlight (soft warm morning, strong afternoon, golden sunset,
  not everything orange). Forest atmosphere: wind, leaves, insects, birds, distant calls, mist; moments of silence for tension.
52-54 Water shader quality for river, pond, waterfall, ocean (PC can be higher quality). Wet surfaces: ground, rocks,
  wood, player cloth darker / smoother / more reflective in rain via efficient global parameters. Foliage wind + subtle
  colour variation; leaves move more, trunks minimal.
55 VFX: rain, mist, waterfall spray, footstep dust, wood chips, stone particles, leaves, campfire smoke / embers, boiling
  steam, eating particles, water droplets, blood hit, bleeding, building dust, volcano ash, lava glow, heat haze; one
  consistent world style.
56 Audio: ocean, river, waterfall, rain, wind, forest, insects, birds, dinosaur calls, footsteps, campfire; sound warns
  of danger before visual contact.
57 Story chapters: 1 THE SHORE (shipwreck, wake, water, food, fire, first night); 2 THE RIVER (follow river upstream,
  waterfall, observe wildlife, migration); 3 THE TRACKS (predator signs, follow tracks, evidence another human was here);
  4 THE WILDERNESS (deeper island, volcano, cave, ancient clues). Do NOT resolve the final mystery.
58-60 Main missions: 1 Wake, 2 Find Water, 3 Make Fire, 4 Find Food, 5 Build Shelter, 6 Survive First Night, 7 Follow
  the River, 8 Find the Waterfall, 9 Observe the Herd, 10 Investigate Predator Tracks, 11 Discover the Old Camp, 12 Reach
  the Volcanic Ridge; optional hunting / gathering / exploration / journal. Gameplay goals ("Find freshwater before
  sunset", "Follow the river to its source", "Return before darkness"), not "collect 10 stones". Small CURRENT
  OBJECTIVE UI, minimal markers.
61 Protagonist: breathing, short reactions, body language, small journal notes; sparse lines ("That's fresh water.",
  "I need shelter.", "What made those tracks?", "That sound...").
62-64 Pressure without unfair punishment; early resources nearby, deeper areas dangerous; preparation loop (food, water,
  spear, torch, bandage before an expedition; go too deep unprepared -> return or take the risk). Natural risk
  progression: beach low, forest edge low / moderate, deep forest moderate, canyon danger, predator territory high,
  volcano environmental. No visible arbitrary boundaries.
65 Death: respawn at bedroll / shelter, lose some inventory, world and journal remain; meaningful, not frustrating.
66 Save: position, health, stats, inventory, equipment, hotbar, recipes, journal, structures, campfire, world time; compact, versioned.
67 Hierarchy: World / Environment (Terrain, Water, Forest, Rocks, Volcano, Weather) / Gameplay (Player, Wildlife,
  Resources, Structures, WaterSources, Interactables) / WorldSystems (Time, Weather, Wildlife, Perception, Migration,
  Save) / VFX / Audio / Lighting / Managers.
68-70 Blender pipeline for new assets: model -> UV -> material -> rig + animation (creatures) -> optimize -> save ->
  export -> Unity import. Match the established style (scale, lighting response, material quality, roughness, detail,
  palette); PC quality: normal maps where right, good silhouettes, natural proportions, quality eyes, believable skin;
  no generic primitives; no mixing cheap / AAA / cartoon.
71 Performance still matters: LOD, culling, GPU instancing, shared materials, pooling; distant wildlife simplified.
72 TESTING POLICY: do NOT run full game tests after small changes. After each major phase: compile check, Console check,
  reference check, asset import check. One technical pass at the END. The owner plays the finished phase by hand.
73 Order: 1 world / waterfall / environment, 2 resource density, 3 water / food / fire / shelter, 4 player movement /
  combat / climbing, 5 herbivores, 6 herds, 7 migration, 8 predator ecosystem, 9 environmental storytelling, 10 story +
  missions, 11 VFX + shaders + audio, 12 final polish, 13 technical validation.
74 Checkpoints (commit at milestones): world_realism, waterfall, resources, survival, player_motion, combat, climbing,
  herbivores, herds, migration, ecosystem, story, world_polish.
75 Chapter feel: DAY 0 shipwreck, wake, fear. DAY 1 water, gather, craft, eat, fire, shelter. DAY 2 river, waterfall,
  herd. DAY 3 predator tracks; "I am inside an entire ecosystem."
76-77 Definition of done (world, player, survival, wildlife, world life, story, technical lists as above). When the PC
  vertical slice is complete: STOP (no second island, multiplayer, giant tech tree, hundreds of items, many new
  dinosaurs, online). Next: manual playtest -> feedback -> targeted polish.
