# IMPLEMENTATION STATUS (Survival loop, Milestone 1)

Updated: 2026-09-28. Legend:
- **DONE (Play-verified)**: built, and a PlayMode test drove it in the running game on the island.
- **DONE (not play-tested)**: builds and passes its tests, but a person has not played it yet.
- **PARTIAL**: some of it is built. The note says what is missing.
- **NOT DONE**: not started.

Milestone 1 = Gather -> Campfire -> Cooking -> Water -> Hunger -> Thirst -> Tent. The whole chain was run as ONE
play-through by `SurvivalM1LoopTest.Milestone1_Spawn_To_Night_And_Save` (section 3). Nobody has played it by hand yet.

## 1. Milestone 1

| # | Part | Status | Where | Evidence |
|---|---|---|---|---|
| 1 | Start with nothing, gather by hand | DONE (Play-verified) | `ResourceNode` (existing) | loop test: empty pack at spawn, stone + fibre gathered from real nodes with the Interact action |
| 2 | Craft rope, stone axe, leaf cup | DONE (Play-verified) | `CraftingSystem` (existing), `RCP_leaf_cup` (new) | loop test crafts each through `Check` / `Enqueue` |
| 3 | Campfire: fuel as data | DONE (Play-verified) | `Campfire.TryLight / TryAddFuel`, `ItemDefinition.fuelSeconds` | wood 240 s from the item; lighting burns exactly one wood; heat reaches the player |
| 4 | Cooking: Raw -> Cooking -> Ready -> Burned, take back, saved | DONE (Play-verified) | `Campfire` slots (count from `SurvivalConfig.cookingSlots`), `CampfireFx`, `burnt_meat` | loop test cooks, takes and eats; `SurvivalM1Tests` covers burning, cold fire, slot save / load |
| 5 | One cooking path | DONE | `RCP_cooked_meat` taken out of the database | the fire is the only way to cook meat |
| 6 | Water types: salt / dirty / clean | DONE (Play-verified) | `WaterType`, `ItemStack.waterType`, `WaterRules`, `WaterSource`, `OceanShore` | sea fills salt, boiled at the fire it becomes clean (salt loses one charge, never below 1), clean drink +30 thirst |
| 7 | Empty a container | DONE (not play-tested) | `PlayerInteraction.EmptyContainer`, inventory EMPTY button | `SurvivalNeedsTests.Water_Fill_Types_Drink_And_Empty` |
| 8 | Rain collector | DONE (Play-verified) | `RainCollector` (new), `PFB_RainCollector`, recipe | loop test: 0 charges in clear weather, fills in rain, fills the cup with clean water |
| 9 | Hunger / thirst: slow drain, tiers, warnings | DONE (not play-tested) | `PlayerSurvival` tiers, `SurvivalConfig` | drain 2.2 / 3.2 per minute (about 45 / 31 min from full to empty); tiers 60 / 30 / 10 / 0; health only drains at 0; `SurvivalNeedsTests` (10 tests) |
| 10 | Tent: cover, warmth, bed, sleep through the night | DONE (Play-verified) | `PFB_Tent` (Shelter + Bedroll), `GameManager.Sleep` | loop test: sleep at 18:36, wake on the next day at dawn, the night costs food |
| 11 | Night air, sleep costs, respawn stats | DONE (not play-tested) | `SurvivalEnvironment`, `SurvivalConfig` | `Night_Air_Blends_Over_Dusk_And_Dawn`, `Eating_Sleeping_...` |
| 12 | Save v4: water type, structure state, sickness, tutorial step id | DONE (Play-verified) | `SaveData`, `SaveSystem`, `ISaveableStructure` | loop test: fire, collector and tent come back, the cup is still clean water; old saves map dirty -> DirtyWater, else CleanWater |
| 13 | HUD tier words, water colours, no per-frame strings | DONE (not play-tested) | `HUDManager`, `SlotView` | tests only check the data behind it |
| 14 | Onboarding steps (fill, boil, drink, tent, shelter, sleep) | DONE (not play-tested) | `TutorialManager` (saved by id) | `Tutorial_Restores_By_Id_And_Maps_Old_Indexes` |
| 15 | Models + icons: tent, rain collector, leaf cup, burnt meat | DONE | `pf_camp_props.py` (original designs) | built into the prefabs by `PrimalSurvivalBuilder` |

## 2. Rules from the directive

| Rule | State |
|---|---|
| No duplicate managers | kept: no new manager. New rules live in `SurvivalConfig` (data), `WaterRules` and `SurvivalEnvironment` (static helpers), `RainCollector` (a placed structure) |
| No survival logic in PlayerController | kept: `PlayerMotor` only reads `IStaminaSource`. `PlayerInteraction` starts the eat / drink action; the rules run in `PlayerSurvival` / `WaterRules` |
| Data-driven | numbers in `Resources/SurvivalConfig.asset`, food / fuel / cook times on the items, recipes in the database |
| Pooling, no per-frame allocations | campfire prompt cached, HUD strings rebuilt only when a value changes, collector ticks once a second |
| Needs must not drain too fast | see row 9 |

## 3. Vertical slice list (owner's advice)

| Piece | State |
|---|---|
| 1 dinosaur, 1 meat | herbivores + carcass exist (upgrade cycle); the loop test gives raw meat instead of hunting (hunting is Milestone 2) |
| 1 campfire, 1 bottle, 1 rain collector, 1 tent, 1 axe | in the loop test |
| 1 knife, 1 torch, 1 storage | exist (flint knife / butcher knife, torch, storage box); not in the loop test |

## 4. Not done / next (Milestone 2)

- Hunting as part of the loop: track -> kill -> butcher -> cook (carcass code exists, butchering was never play-tested).
- Day 2 onboarding steps (weapon, hunt, butcher, storage, explore).
- The rain collector recipe is learned when the player first picks up wood / fibre / hide, not on the first rain.
- Placement rules for the tent / collector were not part of the loop test (it spawns them the way a confirmed ghost does).
- A person has to play the loop once by hand: feel of the drain rates, prompts, HUD words.
