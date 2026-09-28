# SURVIVAL LOOP

## Day 1 (Milestone 1 target)
| Step | Player thought | Game logic |
|---|---|---|
| Wake on the beach | "I have nothing." | empty pack, wreck crates, tutorial objective "Find wood" |
| Gather | "I need resources." | branches / driftwood by hand, stones, fibre from bushes, berries |
| Craft | "I need tools." | stone axe (2 stone, known), flint knife (unlocked by finding flint / bone) |
| Fire | "I need fire." | campfire (wood 4, stone 5) placed with the ghost, lit with 1 wood, fuel burns down (wood 240 s) |
| Food | "I need food." | berries raw; raw meat from the wreck / hunt goes on the fire: raw -> cooking -> ready -> burned if left |
| Water | "I need water." | leaf cup / gourd; pond = unboiled (sick risk), sea = salt (worse thirst), rain collector = clean; boil at the fire |
| Shelter | "I need a safe place before night." | tent (wood, fibre, hide) = cover + warmth + bed; sleep from 18:00, saves the game |
| Night | "Return to camp." | colder air (-4 C), dark, predators active; fire and torch give light and warmth |

## Day 2 (Milestone 2 target)
Better tools -> hunt a small dinosaur -> track it -> butcher (knife = full yield) -> cook meat -> storage -> explore farther.

## Onboarding objectives (not a quest system)
Day 1: Find wood -> Collect stone -> Make a stone axe -> Build a campfire -> Cook food -> Get water (fill a container) ->
Boil water -> Build a tent -> Shelter before night -> Sleep. Day 2: Craft a weapon -> Hunt -> Butcher -> Cook meat ->
Build storage -> Explore. Steps are appended to TutorialManager and saved by id so old saves keep working.

## Success condition
A new player goes from nothing to a cooked meal, clean water, a lit camp and a tent, sleeps through the first night,
and on day 2 hunts and cooks meat. Not marked complete until this was played through in Play mode.
