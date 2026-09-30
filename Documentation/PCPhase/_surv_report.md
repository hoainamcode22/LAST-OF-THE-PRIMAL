# SURV report (S_): fiber spelling, dishes, rare items, stat interactions, HUD (PC phase, 2026-09-30)

The Unity editor was closed and the PC link down for the whole round: everything here is **DONE-NOT-TESTED** unless marked
otherwise. It compiles in the cloud mirror with every agent's files (`./cc.sh all`: runtime / editor / tests rc 0). The
deploy zip `pf_up_S6` (10 SURV files) and the Blender script `S_art/s_dishes_props.py` wait in `/mnt/user-data/outputs/`;
the exact commands are appended to `NEXT_SESSION.md` (steps S1-S8). No git.

## 1. Result per task

| # | Task | Status | What / evidence |
|---|---|---|---|
| 1 | "Plant Fiber" spelling | DONE-NOT-TESTED (my files); REQUESTS for the rest | `PrimalSurvivalBuilder.FixSpelling` rewrites every item / recipe text in the database ("Fibre" -> "Fiber", "fibre" -> "fiber"; ids untouched), so the fiber item shows "Plant Fiber" and the torch / leaf cup / bandage descriptions follow; my own texts and tests fixed. English text in other agents' files is listed in section 4 (STORY journal / tips, U key hints, RES node name, BUILD doorway text, the wreck barrel's loot text in the scene) |
| 2 | Dishes at the fire | DONE-NOT-TESTED (unit test written) | Two new dishes, assembled at the crafting menu and cooked on the campfire slots like any raw food (one cooking path kept): **Meat and Berry Skewer** (`meat_skewer`: raw_meat 1 + berries 4 + wood 1 -> `meat_skewer_raw`, 16 s on the fire -> `meat_skewer`: hunger 48, thirst 10, health 8 slowly, stamina 10, hot, spoils in 48 h, burns into burnt meat) and **Leaf-Wrapped Fish** (`leaf_fish`: raw_fish 1 + edible_plant 2 -> `leaf_fish_raw`, 12 s -> `leaf_fish`: hunger 36, thirst 10, health 8 slowly, stamina 8, hot, 40 h, burns into burnt fish). Raw dishes carry the raw food's sickness chance (0.35 / 0.25). With `fruit_mash` (existing) that is 3 dishes. Recipes learned when a material is first picked up. Test `SurvivalPcPhaseTests.Dish_Is_Assembled_By_A_Recipe_And_Cooked_On_The_Fire` (recipe -> raw dish -> fire -> cooked -> eaten: +48 / +10 / +10, health slowly) |
| 2b | Recipe count | 36 after both builders | 27 today + 4 SURV (meat_skewer, leaf_fish, cloth_bandage, scrap_rope) + 5 BUILD (foundation, wall, doorway, roof, leaf_shelter) = **36** (35 if the Lead retires the old `shelter` recipe as BUILD suggests). Cap in my builder 40; `SurvivalLoopTests` range 15-40; the builder log prints "total N recipes (M building)" |
| 3 | Rare items with uses | DONE-NOT-TESTED (data check test written) | **Bone**: flint knife handle, bone arrows, butcher knife (3 recipes, existing). **Hide**: gourd flask, sinew cord, leather waterskin, bandage, rain collector, tent (6, existing; the bedroll recipe stays fiber + cord, BUILD's pieces do not use hide). **Shipwreck scraps** (new item `wreck_scraps`, "Torn sailcloth, tarred cord and splinters"): `cloth_bandage` (scraps 1 + fiber 1 -> bandage) and `scrap_rope` (scraps 1 -> rope); it also burns 60 s as fuel. RES places it at the wreck remains (request). No bone needle: bone already has three tool uses and the directive asks for no filler |
| 4 | Stat interactions (directive 16) | DONE-NOT-TESTED (tests written) | Present before: thirst tiers slow stamina regeneration (x0.85 / 0.6 / 0.45 / 0.35) and lower the maximum; rain -> wetness -> air felt 5 C colder per full wetness -> body cools -> Cold (regen x0.6, then freezing damage); fire and shelter add warmth and dry faster; sprint drains stamina and doubles hunger drain. Added: **hunger and thirst slow every kind of healing** (`PlayerSurvival.HealMultiplier` = lerp(0.25, 1, hunger) x lerp(0.5, 1, thirst): natural regeneration and the Recovering effect from food / bandages; `SurvivalConfig.healAtEmptyHunger / healAtEmptyThirst`), and **humid air dries slower** (`SurvivalConfig.humidityDryingPenalty` 0.5: drying x (1 - 0.5 x humidity) through WORLD's `SurvivalEnvironment.HumidityAt`, so the wetland / waterfall air (0.95) dries you about half as fast). Tests `Hunger_And_Thirst_Slow_Healing`, `Humid_Air_Dries_Slower` |
| 5 | HUD | DONE-NOT-TESTED (test written) | Hazard line above the prompt for `GameEventType.HazardWarning` (id "volcano" / "lava", amount 1 warm / 2 hot / 3 dangerous, 0 clears; level 3 pulses red): "The ground is warm here", "Hot air rises from the lava. Do not stay", "Get away from the lava!". The status icon row is generic (every `StatusEffectDefinition` with `showOnHud`), so WORLD's `heat` / `heat_severe` show with their own icons next to bleeding, poisoned, wet, cold, leg / arm injury, recovering. Removed per STORY: the old objective box (a saved scene copy is switched off at start, not rebuilt) and the "DAY ONE SURVIVED" banner; `ShowBanner` stays for "DAY N"; the compass marker still reads `TutorialManager.CurrentTarget` (= the mission marker). Test `Hazard_Warning_Shows_Levels_And_Clears` |
| 6 | Eat / drink use U's actions | VERIFIED by code reading | Eating: both paths (`PlayerInteraction.Eat` and `SurvivalItemUse.EatFromSlot` for food that spoils) play `PlayerActions.Eat` with the `OnEat` event; U's `PlayerBodyFx` adds the chewing on that event. Drinking from a container: `UseActiveConsumable` calls `DoOneShot(Drink, "OnDrink", ..., source null)`, which U routes to the ContainerDrink program (hand to the lips). Drinking at water: `WaterSource` / `OceanShore` call `DoOneShot(Drink, ..., FocusPoint, source this)` -> U's `DrinkKneel` + KneelDrink program; filling calls `CollectWater` -> KneelFill program. Gaps: (a) the **rain collector** drinks with the plain `Drink` clip and fills with `Interact` / `OnInteract` (its basin is at hip height, so no kneel; acceptable, but there is no container-to-lips program for it since the source is not null), (b) U's request to RES about skipping the hand splash on `OnDrink("kneel" / "fill")` is not mine, (c) `SurvivalItemUse` bandage uses `BandageUse` / `OnUseItem` as agreed |
| 7 | ENV's WaterSource values | VERIFIED by reading `Editor/PrimalEnvironmentBuilder.Water.cs` (lines 62-68) | 7 bodies, all `fresh = true`: `ENV_River_Water`, `ENV_LowerRiver_Water`, `ENV_RiverMouth_Water` ("River water", dirty), `ENV_SpringStream_Water` ("Stream water", dirty), `ENV_WaterfallPool_Water` ("Waterfall pool", dirty), `ENV_Wetland_Water` ("Marsh water", dirty), `ENV_Spring_Water` ("Spring water", **clean**). Only the spring is clean, as agreed; the old `ENV_Stream_Water` is switched off (inactive sources are skipped by `WaterSource.All`). Two small things on my side: the drink prompt now reads "Drink from the waterfall pool" instead of "Drink waterfall pool" (names that do not end in "water"), clean water gets a sub line "Clear spring water. Safe to drink.", and the river's few thousand surface points are scanned 12 times a second instead of every frame. Test `Water_Prompts_Read_Naturally` |
| + | WORLD request | DONE-NOT-TESTED | `SaveSystem.ApplyWorld` no longer turns a saved storm into rain (WORLD's weather section refines it anyway) |

## 2. Files (all SURV owned; mirror = truth)

Changed: `Scripts/UI/HUDManager.cs`, `Scripts/Survival/SurvivalConfig.cs`, `Scripts/Survival/PlayerSurvival.cs`,
`Scripts/Survival/PlayerStatusEffects.cs` (`HealMultiplier`), `Scripts/Core/SaveSystem.cs`, `Scripts/World/WaterSource.cs`,
`Scripts/Editor/PrimalSurvivalBuilder.cs` (`PcPhaseItems`, `FixSpelling`, recipe count in the report), `Tests/PlayMode/SurvivalM1LoopTest.cs`
and `SurvivalM1Tests.cs` (spelling in comments / messages). New: `Tests/PlayMode/SurvivalPcPhaseTests.cs` (6 tests, `[Timeout]`,
no scene, no WaitForEndOfFrame). Art script (not in Assets): `src_assets/phase3/Tools/S_art/s_dishes_props.py` -> copy to
`E:\LAST OF THE PRIMAL\Tools\S_art\` (five original low-poly models + icons, made in an isolated scene that is removed
afterwards; the builder borrows the meat / fish / rope visuals until the FBX / PNG exist).
Not touched: `Survival/SurvivalEnvironment` (WORLD), `World/Shelter`, `Bedroll`, `StorageBox` (BUILD).

## 3. Tuning values (SurvivalConfig, hand edits win)

- Healing: `healAtEmptyHunger` 0.25, `healAtEmptyThirst` 0.5 (linear to x1 at 100).
- Drying: `humidityDryingPenalty` 0.5.
- Dishes: skewer 48 hunger / 10 thirst / 8 health (slow) / 10 stamina, 48 h; raw skewer 14 / 2, sick 0.35, cook 16 s, 20 h.
  Leaf fish 36 / 10 / 8 / 8, 40 h; raw 14 / 5, sick 0.25, cook 12 s, 12 h. Scraps 0.3 kg, stack 20, fuel 60 s.

## 4. Requests

| To | Request | Why |
|---|---|---|
| STORY | "Fiber" spelling in player-facing text: `Story/JournalSystem.cs` lines 283, 291, 293, 297, 351; `Story/StoryTexts.cs:99` ("Plant Fibre" page title); `Story/TutorialManager.cs` 121-122, 161 | task 1 |
| U | `UI/ContextHints.cs:503` "Cut fibre / hide" -> "Cut fiber / hide" | task 1 |
| RES | `Editor/PrimalResourceBuilder.Data.cs:55` node display name "Fibre plant" -> "Fiber plant"; place `wreck_scraps` pickups / a rare node at the wreck remains (item exists after `PrimalSurvivalBuilder.Build`; icon / model borrowed from rope until the Blender step ran); keep the food age in `WorldPickup.DropStack` (still open from wave 1) | tasks 1, 3 |
| BUILD | `Editor/PrimalBuildingBuilder.cs:432` doorway description "fibre hinge" -> "fiber hinge" | task 1 |
| Lead | the wreck barrel's loot text "Oakum fibre for caulking" lives on a scene `LootContainer` (set by `PrimalGameplayBuilder.cs:437`, never re-run): a hand edit in the scene or a one-line fix in whoever owns loot; decide on retiring the old `shelter` recipe (36 -> 35) | task 1, 2b |
| WORLD | nothing needed; `HumidityAt` is read through `PlayerSurvival.HumidityAt` (a hook, tests replace it) | |

## 5. Not done / limits

- Nothing ran on the PC: the builder's asset creation, the spelling pass on the real assets, the HUD line and the tests are
  all waiting for NEXT_SESSION steps S1-S7. The five models exist only as a script (S4).
- The skewer recipe is also "learned" when wood is first picked up (CraftingSystem learns from any ingredient): a "New
  recipe" note for a dish appears early, with "Missing: 1 Raw Meat, 4 Berries" in the menu. Acceptable, noted.
- Manual play (dish feel, hazard line placement over the prompt, the removed objective box in the saved scene) is
  MANUAL TEST REQUIRED.
