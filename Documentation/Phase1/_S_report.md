# SURV report (S_), Phase 1 wave 1, 2026-09-30

Edited directly on the PC (python read-modify-write), `.before_P1` copies next to every changed file. No git, no deletes,
no PlayMode runs.

## Done
| # | Task | Result |
|---|---|---|
| 1 | Water Cold / Hot | `ItemStack.hotUntil` (GameClock time; Clone copies it). `WaterRules`: `IsHot`, `MakeHot/MakeCold`, `HotSecondsLeft`, `RestoreHot`, `NameOf` ("Clean Water (Hot)"), `ColorOf`, `CanHeat`, `CanBoil`. Fill = Cold; boil / heat = Hot for `SurvivalConfig.hotWaterCoolSeconds` (240 s game clock, sleep counts); a container taken from a burning fire is Hot. Clean Cold water can be reheated (`heatWaterSeconds` 6 s, prompt "Heat Water"). Hot clean drink when cold body / felt air < `hotDrinkColdAir` 18 C / rain / night: body +0.8 C at once (max normal) and feels +6 C for 120 s (`PlayerSurvival.WarmFromDrink`, note "The hot water warms you through."); otherwise a normal drink. UI: tooltip + detail water line "500 / 750 ml Clean Water (Hot)", detail name "Gourd (Hot)", hotbar name "Gourd: Clean Water (Hot)", slot / hotbar count "HOT" over "500ml" in `hotWaterColor`; the 5 s pack check refreshes slots once when water cools. Saved: `SlotData.hot` / `DropData.hot` (inventory, storage, drops), campfire `SlotSave.hot`; old saves load Cold |
| 2 | Ocean never drinkable | Code rule in `WaterRules` (independent of the config): salt water is never purified. `Campfire`: holding salt water at a lit fire shows "Boil Salt Water / Boiling does not remove salt...", E and `TryBoil` refuse with `WaterRules.SaltBoilMessage`. `SurvivalConfig.cs:73` default fixed (boilSeconds 0, boilResult SaltWater, loss 0) and the builder fixed the asset (was 20 s -> CleanWater, loss 1). Salt drink keeps -6 thirst. Fill / inventory texts say boiling does not remove salt |
| 3 | Container in the hand | `PFB_Hand_water_container` (ITEM_WaterContainer.fbx, 0.26 m), `PFB_Hand_leather_waterskin` (same model, 0.30 m), `PFB_Hand_leaf_cup` (ITEM_LeafCup.fbx, 0.14 m), model centred on the grip, long axis on Z like the torch / axe hand models; set as `handPrefab` (was empty). Primitive gourd fallback (M_Gourd) in the builder when no model exists (not used) |
| 4 | Recipe categories | `RecipeCategory` + `Fire, Storage` (appended). Assets: campfire Structures -> Fire, torch Survival -> Fire, storage Structures -> Storage; shelter / tent / bedroll stay Building. Crafting tabs: ALL, TOOLS, WEAPONS, FOOD, WATER, FIRE, BUILDING, STORAGE, SURVIVAL, RESOURCES (Cat0..Cat9, positions set each build, 60 px step). Counts: Tools 6, Weapons 6, Survival 2, Food 1, Building 8, Water 4, Resources 2, Fire 2, Storage 1 |
| 5 | Charcoal | Item `charcoal` (Resource, stack 20, 0.15 kg, fuel 150 s, icon `ICON_Charcoal.png` drawn with PIL: `Tools/S_art/s_icon_charcoal.py`), world model `PFB_Charcoal` (primitive lumps, M_Charcoal / M_CharcoalAsh), `SurvivalConfig.charcoal`. A fire that burns out (fuel 0, not doused / put out) after 300 s of burning leaves 1, after 600 s 2 (`charcoalAfterBurnSeconds`, `charcoalMax`); shown on the stones; E "Take Charcoal (N)" (priority after ready food). Saved in the campfire state (`charcoal`, `burned`) |

## Files
Changed: `Scripts/Items/{ItemEnums, ItemStack}.cs`, `Scripts/Survival/{SurvivalConfig, WaterRules, PlayerSurvival}.cs`,
`Scripts/World/{Campfire, WaterSource, OceanShore}.cs`, `Scripts/Core/{SaveData, SaveSystem}.cs`, `Scripts/UI/{InventoryUI, HUDManager}.cs`,
`Scripts/Editor/PrimalSurvivalBuilder.cs` (new bridge commands `Phase1`, `Phase1Check`; `Build` runs the Phase 1 steps too).
New assets: `Data/Items/ITEM_charcoal.asset`, `Prefabs/Gameplay/PFB_Hand_{water_container, leather_waterskin, leaf_cup}.prefab`,
`Prefabs/Gameplay/PFB_Charcoal.prefab`, `Art/Materials/M_Charcoal{,Ash}.mat`, `Art/Icons/ICON_Charcoal.png`, `Tools/S_art/s_icon_charcoal.py`.
Changed assets: `Resources/SurvivalConfig.asset` (salt profile, charcoal ref), `Resources/ItemDatabase.asset` (+charcoal),
`Data/Items/ITEM_{water_container, leather_waterskin, leaf_cup}.asset` (handPrefab), `Data/Recipes/RCP_{campfire, torch, storage}.asset`.
Scene not touched (no SaveScene needed).

## Commands (lock held 08:21-08:27 UTC)
- Runtime + editor code compiled on the PC (Runtime.dll 06:45 via DINO's refresh, Editor.dll 08:25; strings checked in the dlls).
- `S1_r/p_082230` Refresh + Ping ok; `S1_c_082343` ConsoleCheck: 0 errors, 0 warnings.
- `S1_b_082343` `PrimalSurvivalBuilder.Phase1`: salt fixed, 3 hand prefabs, charcoal item + prefab + 2 materials + icon, 3 recipe categories; 47 items (+1), 32 recipes.
- `S1_k_*` `Phase1Check`: null items 0, null recipes 0, bad recipes 0, no icon 0, missing scripts 0, empty material slots 0; salt boil 0 s -> SaltWater.
- `S1_c2_*` ConsoleCheck: 0 errors, 0 warnings.

## Requests
| To | Request |
|---|---|
| Lead (tests) | `SurvivalM1Tests.Boiling_Salt_Water_Gives_Clean_Water_Minus_A_Charge` and `SurvivalM1LoopTest` step 6 ("sea water -> boiled -> clean") now fail by design: salt is refused. Rewrite as `TryBoil` on salt returns false, the container stays in the pack as SaltWater; the loop test should boil pond water. Line 217 (`TryBoil` false after boiling) still holds: the water is Hot, so it is not reheated. Please also confirm the PlayMode test assembly recompiles (`ApplyDrink` got an optional `hot` parameter; source compatible) |
| U | `PlayerInteraction.cs:428` `SaltDrinkWarning` "Salt water makes you thirstier. Boil it first." -> e.g. "Salt water makes you thirstier. Boiling does not remove salt." Optional: ContextHints for "Heat Water" / hot drink |
| Lead / Story | `JournalSystem.cs:268` says sea water boiled over a fire becomes fit to drink: change to "boiling does not remove the salt; fresh water is inland". `TutorialManager` boil step is unaffected (pond water) |

## Not done / not tested
- Nothing played by hand: hand pose of the gourd / cup, charcoal look on the stones, the HOT slot text layout at small hotbar size: MANUAL TEST REQUIRED.
- No new PlayMode tests (policy). Hot-drink warmth and hot state are not saved as a status effect (short-lived warmth is lost on load).
