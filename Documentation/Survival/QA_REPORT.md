# QA REPORT (Survival loop, Milestone 1, 2026-09-28)

What was checked, how, and what was NOT checked. Nobody has played this by hand yet.
Test results file: `Documentation/Tests/playmode_results.xml`. Screenshots: `Documentation/Screenshots/Survival/`.

## 1. Build / compile

| Check | How | Result |
|---|---|---|
| C# compile (runtime, editor, PlayMode tests) | cloud `csc` against the Unity 6000.3 reference DLLs, then the Unity editor compile after each deploy (pf_up_9 .. pf_up_15) | 0 errors |
| Content build | bridge `PrimalSurvivalBuilder.Build` | SurvivalConfig asset; items leaf_cup, burnt_meat, rain_collector, tent (model + icon); PFB_Tent, PFB_RainCollector; RCP_cooked_meat taken out; 37 items / 27 recipes |

## 2. PlayMode suite (Unity Test Runner, in the editor)

| Run | Result |
|---|---|
| Full suite after the survival merge | 67 passed, 0 failed (221 s) |
| Full suite with the new loop test and the test-save folder | **68 passed, 0 failed** (237 s) |

New survival tests: `SurvivalNeedsTests` (10), `SurvivalM1Tests` (8), `SurvivalM1LoopTest` (1). The upgrade-cycle tests
(combat block, hit layer, holster, key hints, wind) are in the same green run.

## 3. The Milestone 1 loop in Play

`SurvivalM1LoopTest.Milestone1_Spawn_To_Night_And_Save` loads the island, starts a new game and plays the chain with
the same calls the input uses (Interact on the node / fire / sea / collector / bed, the crafting queue, eat / drink from
the hand). Its log from the last run:

```
gathered stone by hand from Loose stones: 1
gathered fiber by hand from Fibrous plant: 2
fire lit with 1 wood: fuel 240 s, heat at the player 10,2 C
meat cooked, taken and eaten: hunger 40 -> 75
sea water -> boiled -> clean, drunk: thirst 30 -> 60
rain collector: 0 charges in clear weather, 1 after rain, 0 left after filling the cup (clean)
slept in the tent: day 1 -> 2, woke 6,0 h, hunger 72 -> 46
save / load: 3 structures back (fire, collector, tent), cup still clean water
```

Shortcuts the test takes (so this is NOT a hand play-through):
- Materials beyond one node each are added directly (the same items gathering gives). Raw meat is given (hunting is Milestone 2).
- Time runs 8-10x while waiting for cooking, boiling and rain. Hunger / thirst are set before eating / drinking so the gain is measurable.
- The fire, collector and tent are spawned the way a confirmed build ghost spawns them; placement rules are not tested here.
- The player is moved next to each target instead of walking there.

## 4. Screenshots from the running game

`SurvivalShowcase` (runs only when `Library/PrimalBridge/capture_survival.txt` exists):

| File | Shows |
|---|---|
| `camp_day.png` | fire lit with meat on it, rain collector, tent, survivor at the fire |
| `camp_dusk.png` | same camp at 19:18: fire glow on the sand, first stars |
| `holster_back_hip.png` | sword on the back while the axe is in the hand |

## 5. Found and fixed during QA

- The island tests saved into, and deleted, the real save slot 0 (`SaveSystem.Delete()` after save / load tests, and the
  sleep autosave). Test runs now write to a temp folder (`SaveSystem.FolderOverride`, set by `TestScenes.UseTestSaves`,
  cleared after each island test and when the run ends). **Runs before this fix may have removed a save in slot 0.**

## 6. NOT tested

- A person playing the loop: drain feel over a real day, prompts, HUD tier words, water colours.
- Walking the loop with real input and real distances; build ghost placement of the tent / collector.
- Hunting -> butchering -> cooking the carcass meat.
- Torch warmth at night, storage box in the loop.
- Performance with a camp of many fires / collectors (no profiler pass).

## 7. Known issues

1. The rain collector recipe is learned at the first wood / fibre / hide pickup, not at the first rain.
2. From the screenshots: the food on the fire is small and hard to read from 7 m; the tent reads as a lean-to sheet
   from behind.
