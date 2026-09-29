# Owner directive: PHASE 3.5, BARE-HAND COMBAT + RESOURCE PASS (verbatim content, condensed layout)

Fixes two weaknesses: no bare-hand combat; resources too sparse and many visible rocks cannot be collected.
First minutes: start with nothing, use hands, gather basics, craft first tool, progress naturally.

1-2 Bare hands MUST work (player starts with no weapon). Integrate into the existing PlayerCombat architecture (no separate incompatible system). Animations at least: BareHand_Idle, BareHand_Punch_1, _Punch_2, _Punch_3, BareHand_Heavy, BareHand_HitReaction, BareHand_Combo_End. Combo punch 1 -> 2 -> 3; hold attack -> heavy punch. Not a fighting game.
3 Human punch, not a rotating upper arm: foot planting, hip rotation, torso rotation, shoulder, elbow, wrist alignment, follow-through. Light quick / compact / responsive; heavy: larger hip / torso rotation, more weight transfer, stronger recovery. Keep previous animation fixes.
4 Flow: start, anticipation, strike, hit window, recovery; animation event (or equivalent) hit timing; never damage every frame.
5 Configurable damage, stamina cost, attack speed, range, knockback; weak damage ("hands = emergency, tools / weapons = progression").
6 Targets: small resource nodes, small creatures, very small hostiles, weak destructibles; bare hands cannot realistically kill giant dinosaurs.
7 VFX_PunchSmallImpact, VFX_PunchHeavyImpact, VFX_DustImpact: small impact, dust, sound, enemy hit reaction; nothing exaggerated.
8 Audio Punch_Whoosh, Punch_Hit, Punch_HeavyHit, Player_Grunt; original / licensed.
9 One player state architecture (Idle, Move, Run, Sprint, Jump, Attack, HeavyAttack, Gather, Interact, Climb, Build, Eat, Drink, Hurt, Dead); no conflicting attack booleans across scripts.
10-11 Abundant readable resources (wood, stone, fiber, food). Decorative objects vs gameplay resource nodes: similar look, but nodes recognizable by a subtle highlight, shape variation, placement cue, prompt when close; no bright floating icons.
12 Stone nodes Small (2-4 stone), Medium (4-8), Large (8-15): health / durability, yield, respawn time; ScriptableObject data; tunable.
13 Prompt "Gather Stone"; bare hands: small amount, more effort (1 stone / action); stone pick: 3-5 / action; configurable.
14 Bare-hand stone gather = short primitive motion; pick = pickaxe swing; player must face and be at the resource, no gathering from meters away.
15 Wood: fallen branches (1-3), small logs (4-8), driftwood, deadwood, harvestable tree (more, needs axe).
16 Fiber: fiber plant, long grass, fern-like plant, small bush; by hand; 1-4 fiber; hand animation, leaf VFX, sound, resource disappears.
17 Starting beach + forest edge: several wood, stone, fiber sources, several food sources, a visible freshwater route; no 20-minute wandering; difficulty from danger, not scarcity.
18 Placement by environment: beach driftwood, small stones, fruit, shell-like decoration; forest edge fiber, branches, small stones, berries; forest trees, larger stones, fruit trees; rocky zone more stone less food; water area plants, fish, small food.
19 Respawn / regrow: tree slow, fiber moderate, fruit regrows, stone slow; never instant.
20-22 Feedback: action -> result -> gain: "+3 Stone" small fading notification (no stacking wall), inventory sound, particles, visible change of the node. Visual states: tree healthy / partial / empty / regrowing; fruit tree fruit / none / regrowing; stone full / damaged / depleted / respawn; nothing vanishes without explanation.
23 Inventory capacity reasonable (weight or stack count), never blocked after a few actions.
24 Progression: bare hands -> gather wood / stone / fiber -> stone pick -> stone faster -> axe -> wood faster.
25 Bare hands: small stone, small branch, fiber, fruit, small edible plants; not efficient on large trees / large rocks.
26 Every gameplay resource uses the central interaction API; one clean resource architecture (no StoneInteraction / RockInteraction2 / FinalRockSystem).
27 ScriptableObjects ResourceDefinition (id, display name, category, yield, respawn time, required tool, gather time) and GatherToolDefinition (tool type, efficiency, durability, level if needed).
28 Efficiency: hands low; stone pick high on stone; stone axe high on wood; stone knife on meat / hide.
29 Small harmless creatures: basic interaction or hunting; food also from berries, fruit, plants, fish, small prey (combat not required for all food).
30 First 10 minutes: 0-1 explore, 1-3 gather, 3-5 craft a tool, 5-7 find water, 7-10 food / fire.
31 Readability: subtle outline, small interaction icon, contextual prompt, only when close; no quest markers on every resource.
32 VFX pooled: stone fragments + dust; wood chips, bark, dust; fiber leaf particles; fruit leaf / fruit movement.
33 Hands on small branches OK; large tree needs an axe; punching a large tree = small feedback, very low yield, no efficient hand chopping.
34 Bare-hand damage uses the same damage events, hit detection, status effects, health system as weapons.
35 Weapon transition: bare hands -> equip spear -> spear combat; hotbar updates; no duplicate combat controller.
36-38 Map pass: every visible gameplay resource must prompt, give items, show VFX, update inventory; decorative-only must look decorative. Resource pass over the whole current island (no expansion, no new terrain), natural clusters (stone near rocky areas, riverbanks, beach, cliff bases; wood fallen logs, forest edge, driftwood, tree clusters; fiber grass, ferns, forest floor; fruit designated trees, berry bushes).
39-40 Animation check with the new moves (punch hip / torso / arm / wrist; gather lean toward target; pick two-hand coordination); feet grounded on uneven terrain via the existing foot IK.
41 Cheap nodes: no hundreds of Update() calls; central management, events, pooling, distance activation.
42-44 Mobile: contextual GATHER / DRINK / FILL / HARVEST / CLIMB buttons only when relevant; attack button (hold may repeat light attacks), separate heavy attack; gather once on press, optional hold to repeat; no tiny UI.
45-47 QA bare hand (start unarmed, punch enemy, punch small resource, heavy, combo, take damage, defeat target, return to movement, equip spear, weapon combat still works); QA resources (stone by hand and pick, branch, axe on tree, fiber, fruit, inventory, notifications, VFX, sound, respawn); QA early survival from a fresh save (wood / stone / fiber, stone axe, more, water container, freshwater, fill, purify, drink, food, cook, spear) without unreasonable backtracking.
48-50 If many visible rocks cannot be collected, convert an appropriate portion into stone nodes (keep performance); starting area density supports the first loop; natural clusters with empty spaces, no spam.
51-52 Maintainable (BareHandCombatSystem, ResourceNode, ResourceManager, GatheringSystem, ResourceDefinition, GatherToolDefinition, reuse existing architecture); docs Documentation/BARE_HAND_COMBAT.md and Documentation/RESOURCE_SYSTEM.md (attack flow, damage flow, gathering flow, resource types, tool efficiency, respawn, interaction, mobile controls).
53 Checkpoint checkpoint_barehand_and_resource_system (git commit).
54-55 Acceptance: nothing -> hands -> stone -> wood -> fiber -> tool -> faster -> water -> purify -> drink -> food -> cook -> weapon -> fight -> injured -> heal -> explore; definition of done lists punch, heavy, combo, hit detection, damage, stamina, animation, VFX, sound, mobile control; stone / wood / fiber / fruit nodes, interaction, efficiency, yield, VFX, sound, inventory, respawn, natural placement; starting resources, water, food, first tool, first weapon; maintainable, ScriptableObjects, no duplicates, mobile, performance, no critical errors.
