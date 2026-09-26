# Game Concept — PRIMAL FRONTIER

## Overview
Third-person, single-player prehistoric survival adventure for PC (Unity 6.3 LTS, URP). The vertical slice ("Day Zero")
covers one small island (~500 x 500 m) from the shipwreck at dawn to surviving the first night. Full brief:
`design/PRIMAL_FRONTIER_BRIEF.md`.

## Player Fantasy
"I am alone in a prehistoric wilderness. I have nothing. I must learn how to survive." Vulnerable and curious at the
start, proud of every learned skill, never fully safe.

## Detailed Rules
Core loop: wake -> explore wreck -> gather wood/stone -> craft axe -> find water -> find food -> campfire -> cook ->
spear -> forest -> tracks -> observe herbivore -> predator sign -> return -> shelter -> night -> morning -> save.
Systems: survival stats (health, hunger, thirst, stamina, temperature), gathering, grid inventory + hotbar,
ScriptableObject crafting (15-25 recipes), campfire, shelter, 6 dinosaur species with FSM ecology, journal of
discoveries, day/night, rain, save/load.

## Formulas
Defined per system GDD (to be written in phases 6-15). Initial targets: thirst empties in ~2 in-game days of
neglect-equivalent real time (~25 min real), hunger ~2x slower, stamina regen after 1 s delay. Values live in
ScriptableObjects, never hardcoded.

## Edge Cases
Drinking ocean water: refused with feedback, never silently allowed. Night without fire: cold drains health slowly,
never instantly lethal. Predators never spawn inside the player's view or within 40 m.

## Dependencies
Terrain/world (M1) -> player (M2) -> wildlife (M3) -> story/journal/save (M4) -> polish (M5).

## Tuning Knobs
Day length (default 24 real minutes), stat decay rates, predator warning radius, AI LOD distances, resource respawn time.

## Acceptance Criteria
The 31-item Definition of Done in brief s53, verified by qa-lead and recorded in `Documentation/PRIMAL_FRONTIER_STATUS.md`.
