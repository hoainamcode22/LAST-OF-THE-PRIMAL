# FOOD SYSTEM

| Food | Source | Raw effect | Cooked |
|---|---|---|---|
| Berries | bushes, berry thickets | +8 hunger | (fruit mash recipe at the fire) |
| Fruit | fruit trees (climb) | +12 hunger, +4 thirst | - |
| Raw meat | wreck, carcass | +10 hunger, 35 % stomach sick | Cooked meat: +35 hunger, +6 health, hot |
| Cooked meat | campfire | - | left on the fire: Burnt meat (+4 hunger, 10 % sick) |

## Cooking (one path, at the fire)
- A lit campfire has `cookingSlots` (4). Interact with raw food in hand puts one on a slot (visible on the fire).
- Slot states: **Raw -> Cooking** (timer = item.cookSeconds) **-> Ready** (steam) **-> Burned** after
  `burnSeconds` (item) or cookSeconds x `burnAfterCookMultiplier` (config). Ready / burned food is taken with E.
- Unlit fire: timers pause, food can still be taken off. Slots are saved (ISaveableStructure).
- The old crafting recipe `RCP_cooked_meat` is retired (hidden) so there is one cooking path.

## Needs effects (SurvivalConfig tiers, gradual)
| Hunger | Effect |
|---|---|
| > 60 | none (start value 85 stays here) |
| 60-30 "Peckish" | stamina regen x0.85 |
| 30-10 "Hungry" | regen x0.65, max stamina x0.8 |
| 10-0 "Starving" | regen x0.5, max x0.65, speed x0.92, health -0.12/s; at 0: -0.25/s |
Drain 2.2 per real minute (~45 min full to empty); sprinting x1.8.
