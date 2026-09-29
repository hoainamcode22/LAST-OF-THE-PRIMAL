# SURV report (S_): survival stats, status effects, food / water, campfire, save. Phase 3 wave 1, 2026-09-29

All code in the cloud mirror (`src/`), deployed with hand-made zips `pf_up_S1` .. `pf_up_S5` (only my files and my art),
each compiled on the PC without errors. Builder run: `PrimalSurvivalBuilder.Build` (bridge `S_3`, again after S5).
Evidence files on the PC: `Tools/playmode_results_S5.xml`, `_S6.xml`, `_S7.xml`, `_S8.xml` (copies of
`Documentation/Tests/playmode_results.xml`), bridge results `Library/PrimalBridge/result_S_*.json`, captures
`Documentation/Screenshots/Survival/phase3_*.png`. No git.

## 1. Result per item

| # | Item | Status | Evidence |
|---|---|---|---|
| 1a | `ISaveSection` registry | **PASS** | `Core/ISaveSection.cs` (`SectionKey`, `CaptureSection`, `RestoreSection`), `SaveSystem.RegisterSection / UnregisterSection / Sections / TryGetLoadedSection`, `SaveData.sections` (v5). `ISaveSection_Round_Trip_Missing_And_Broken_Sections_Are_Skipped`: JSON with quotes comes back exactly through the file; a throwing capture and a null capture are left out; a throwing restore is skipped while the rest loads; a section missing from the file is not restored; an unregistered blob in the file is ignored. Island: `Save_Load_Keeps_..._And_Sections` restores a test section through `GameManager.SaveGame / LoadGame` |
| 1b | `PlayerStatusEffects` + `StatusEffectDefinition` | **PASS** | `Survival/PlayerStatusEffects.cs`, `Survival/StatusEffectDefinition.cs`; assets `Resources/StatusEffects/SE_{bleeding, leg_injury, arm_injury, sickness, wet, cold, recovering}` made by the builder (code defaults when an asset is missing). API: `Apply(id or def, severity, seconds)`, `Has`, `Remove(id, cured)`, `RemainingOf`, `SeverityOf`, events `Changed` / `EffectChanged` / `Message`, combined `MoveSpeedMultiplier`, `SprintCostMultiplier`, `StaminaRegenMultiplier`, `MaxStaminaMultiplier`, `ThirstMultiplier`, `HungerMultiplier`, `AttackMultiplier`, `GatherMultiplier`, `BlocksRegen`; `PlayerStatusEffects.Player` (tagged player); GameEvents `StatusApplied / StatusEnded`. The old `SickSeconds` is now the "sickness" effect (`SickSeconds`, `MakeSick`, `RestoreSickness` keep their meaning: `SurvivalNeedsTests` green). Saved as section "status" (`Status_Effects_Save_Section_Round_Trip`) |
| 1c | Campfire read API | **PASS** | `Campfire.All`, `IsLit`, `State` (`FireState` Unlit / Lighting / Burning / LowFuel / Extinguished), `Fuel01`, `Intensity01` (fuel + lighting ramp + rain), `FuelIntensity01` (no weather, for AI's own rain rule), `CookingCount`, `Sheltered`, `RainedOn`, `HeavyRain`, `Douse01`, `CookRate`, event `StateChanged`, GameEvents `FireStateChanged / FireDoused` |
| 2 | Bandage (P1) | **PASS** | Its own use path: `SurvivalItemUse` registered in `PlayerInteraction.UseHandlers` (attack button, touch USE, inventory Use / double click). Island `Bandage_Use_Path_Stops_Bleeding_Shows_Icons_And_Hydration`: action seen = `PlayerActions.BandageUse` (placeholder clip Use_Item until CHAR's Bandage_Use), bleeding stops, one bandage used, `ItemUsed` raised, **no `Ate`**, the tutorial step did not move, Recovering starts (+15 HP over 60 s), BandageWrap sound, faint `VfxId.Heal` at the chest. Refused with "You have no wound to dress." when nothing to treat. Data: bandage `cures = [bleeding]`, `health 0`, `healOverTime 15` (was +25 instant through Eat); own roll model / icon (S5) |
| 3a | Bleeding / injuries from the damage path | **PASS** | `PlayerHealth.TakeDamage` -> `Wound`: attacker `bleedSeconds` bleed that long; a cut while bleeding or a hit >= `deepWoundDamage` (30) = deep wound (75 s unless bandaged); heavy hit >= `injuryMinDamage` (25) injures a limb with `injuryChance` 0.6 (half leg: speed x0.82, sprint cost x1.5; half arm: attack x0.7, gather x0.8), 300 s; landing >= 13 m/s hurts the leg. Dodged / fully blocked hits do neither. Tests `Bleeding_From_Hits_Deep_Wound_And_Bandage_Stops_It`, `Heavy_Hits_Injure_A_Limb_Leg_Slows_Arm_Weakens`; `PlayerFeedbackTests.HeavyHit_Bleeds_ThenStops` still green (2.5 s bleed stops by itself) |
| 3b | Gradual healing | **PASS** | Food `health` and `healOverTime` come back through Recovering (+0.25 HP/s), never at once; natural regeneration x1.75 under a roof or by a fire; limb injuries heal x2 there; sleep runs every effect's timer down (`ApplySleep` -> `Advance`). Test: `Heavy_Hits_...` (a night's sleep heals the leg), `Bleeding_...` (recovering heals, not all at once) |
| 3c | HUD status icons, wetness indicator, "+ Hydration" | **PASS** | Row of up to 8 icons right of the vitals panel (bleeding pulses), names in the status line, thin wetness bar under the temperature, "+30  Hydration" / "+N  Food" notes (merged within 3 s). Island test checks the bleeding icon sprite, the note and the wet bar; capture `phase3_hud_status.png` (wet icon + wet bar). 7 original icons (PIL) `Resources/UI/icon_status_*.png`. First layout under the panel was hidden by the touch menu buttons: moved (S4) |
| 4a | Water in ml | **PASS** | `SurvivalConfig.mlPerCharge` 250; charges stay the rule unit. Hotbar / slots "500ml", tooltip and detail "500 / 750 ml clean water", fill prompts and the rain collector prompt in ml. Island test: emptied cup shows "0ml" |
| 4b | Dirty-water sickness configurable | **PASS** | `SurvivalConfig.water[DirtyWater].sickChance / sickSeconds` (existing). `Dirty_Water_Poisoning_Follows_Config_With_A_Fixed_Seed`: seed 4242, 37 of 200 drinks sick at 20 %, same seed same result, 0 -> never, 1 -> always for the configured 40 s; effects: thirst x1.6, stamina regen x0.5, max stamina x0.85, -0.3 HP/s, no regen |
| 4c | Boiling FX / sounds, fill sound, drink drops | **PASS** (sound audibility NOT TESTED) | `CampfireFx.SetBoiling`: `BoilBubbles` burst every 1 s at the slot + the seamless `WaterBoil` clip on its own looping AudioSource while water heats (`Boiling_Plays_Bubbles_And_Loop_While_Water_Heats`: on while heating, off when boiled). `WaterFill` on every fill (`WaterRules.Fill`). Filling plays `PlayerActions.CollectWater` (placeholder Drink clip; drops / splash come from PlayerFeedback's OnDrink). Nobody listened to the sounds |
| 4d | Rain collector water surface (QA known issue 3) | **PASS** | Cause: the model's Water disc sits at 0.05 m (near the basin floor, 0.03 m) and the old code lowered it by up to 0.2 m, under the floor. Now placed at absolute heights 0.07 (one charge) .. 0.21 m (full), measured from the disc mesh, tinted with a property block. Capture `phase3_rain_collector_2of6_5of6.png`: blue water at 2/6 (0.098 m) and 5/6 (0.182 m) |
| 5a | Spoilage fresh -> aging -> spoiled | **PASS** | `ItemDefinition.spoilHours`, `ItemStack.madeAt` (count-weighted when stacks merge), rules in `Survival/Spoilage.cs`; stage worked out on use, on inventory open and a 5 s pack check (`PlayerSurvival.CheckSpoilage`, one refresh per stage change); no per-item Update. Aging from 50 % (food x0.8, +5 % sickness), spoiled (x0.35, sickness at least 50 %, 60 s). Cooking keeps the used-up part of the time; saved as an age. Slots tinted, detail "Getting old (spoils in about N h)". Tests `Spoilage_Stages_Nutrition_Sickness_And_Stack_Ages`, `Spoilage_Check_Refreshes_The_Pack_Once_Per_Stage`, island save keeps an aging stack aging |
| 5b | New items, icons, models | **PASS** | `raw_fish` (cooks into `cooked_fish`, 12 h), `cooked_fish` (burns into `burnt_fish`), `burnt_fish`, `edible_plant` ("Wild Greens"), each with an original Blender model (`Art/Models/Props/ITEM_{RawFish, CookedFish, BurntFish, EdiblePlant}.fbx`, 364-644 tris, made in an isolated scene of the running Blender and removed afterwards; the character .blend was not touched) and icon (`Art/Icons/ICON_*.png`). Spoil hours set on raw / cooked / burnt meat, berries, fruit, fruit mash. Builder log `result_S_3.json`, `PrimalSurvivalBuilder.Inspect` (`result_S_4.json`) |
| 5c | Fish cooking | **PASS** | `Fish_Cooks_On_The_Fire_And_Keeps_Its_Age`: the database links raw -> cooked fish; a fish on the fire cooks, is taken, and an aging raw fish gives aging cooked fish |
| 6 | Campfire completion | **PASS** (flames look in play NOT TESTED) | `Fire_States_Lighting_Burning_LowFuel_Extinguished` (Lighting ramp, LowFuel below 60 s with weaker intensity / heat / cooking, burning again after fuel, extinguished when out, state saved); `Heavy_Rain_Puts_Out_An_Open_Fire_Not_A_Sheltered_One` (normal rain: intensity x0.7, not out; storm: open fire out with FireHiss + puff + `FireDoused`, sheltered fire unaffected). Prompts: "Light the fire", "Add Fuel: Wood (+4:00)" with "burning low" / rain notes, "Cook Raw Fish (10 s)", "Boil Water (8 s)". Capture `phase3_fire_full_vs_low_fish.png`: the low fire's light is clearly dimmer (the offscreen capture shows no flame particles) |
| 7 | Temperature / wetness | **PASS** (feel NOT TESTED) | Water depth (sea below sea level, pond / stream surfaces) soaks you and chills 3 C; drying faster under a roof, by a fire and in the sun; sun +2.5 C on clear days, 0 in shade (one raycast to the sun every 0.5 s) and under a roof. `Water_Wets_Roof_And_Fire_Dry_Sun_Warms` |
| 8 | First-gather hitch (P2) | **PASS** | `SurvivalHitchTests` on the island, inventory closed. Before (S_2, old UI): wood 181.1 ms (3 recipes, next frame 88.9 ms), fiber 53.1 ms (1), hide 45.9 ms (1). After (S_7): wood 5.4 ms, fiber 2.8 ms, hide 4.9 ms; next frames 41-57 ms against a 43.9 ms median idle editor frame; 0 tile rebuilds while closed; the crafting tab builds its tiles once when first shown (69.3 ms, inside the menu), later learning updates them in place. One note for several recipes learned together |
| 9 | Save safety | **PASS** | Temp file, read back and checked, previous save kept as `save_N.json.bak` (only when it is itself readable), then replaced. Read falls back to the backup on a missing / damaged / unusable save (`LoadedFromBackup`, message in `LastError`). Parse checks version (newer refused, no version key = not a save), repairs NaN / infinite numbers, drops null / id-less entries; Apply runs each part and each structure on its own (a failing part is logged and skipped). Save v5: sections, food age on slots / drops, fire state in the campfire state. Tests `Save_Keeps_A_Backup_And_Falls_Back_When_Damaged`, island `Save_Load_Keeps_Status_Food_Age_Fire_State_And_Sections` |
| 10 | Tool break feedback | **PASS** | `InventorySystem.WearActive` raises `ToolBroke` + `GameEventType.ToolBroken`; the HUD plays ToolBreak + a small DustImpact and shows "X broke!" once (a repeat of the same note within 1 s is dropped, so the callers' own notes do not double). `Tool_Break_In_The_Wear_Path_Is_Reported_Once` |
| 11 | Recipes | **PASS** | 27 recipes (builder report): stone axe, stone pick, flint knife, stone spear (also thrown), bow, arrows, campfire, torch, water container / leaf cup / waterskin, bedroll, storage, bandage all present; cooked meat and cooked fish on the fire slots. Building pieces: wave 2 |
| T | Tests | **PASS** | New: `SurvivalPhase3Tests` (15), `SurvivalPhase3IslandTests` (3, one capture-only), `SurvivalHitchTests` (1); all `[Timeout]`, island tests `UseTestSaves`, no WaitForEndOfFrame. Existing survival tests green: SurvivalNeedsTests 10, SurvivalM1Tests 8, SurvivalM1LoopTest 1, WaterReachTests 2, SurvivalLoopTests 6, PlayerFeedbackTests 8, BareHandCombatTests 8. Full suite: see section 5 |

## 2. Files changed (mirror paths)

New: `Scripts/Core/ISaveSection.cs`, `Scripts/Survival/{PlayerStatusEffects, StatusEffectDefinition, Spoilage, SurvivalItemUse}.cs`,
`Tests/PlayMode/{SurvivalPhase3Tests, SurvivalPhase3IslandTests, SurvivalHitchTests}.cs`.
Changed: `Scripts/Survival/{PlayerSurvival, SurvivalConfig, SurvivalEnvironment, WaterRules}.cs`, `Scripts/Player/PlayerHealth.cs`,
`Scripts/Items/{ItemDefinition, ItemStack, InventorySystem, CraftingSystem}.cs`, `Scripts/World/{Campfire, WaterSource, OceanShore, RainCollector}.cs`,
`Scripts/VFX/CampfireFx.cs`, `Scripts/UI/{HUDManager, InventoryUI, SlotView}.cs`, `Scripts/Core/{SaveData, SaveSystem}.cs`,
`Scripts/Core/GameEvents.cs` (appended `ItemUsed, StatusApplied, StatusEnded, FoodSpoiled, ToolBroken, FireStateChanged, FireDoused`),
`Scripts/Editor/PrimalSurvivalBuilder.cs`.
Assets (deployed): `Resources/UI/icon_status_{bleeding, sickness, wet, cold, leg_injury, arm_injury, recovering}.png`,
`Art/Models/Props/ITEM_{RawFish, CookedFish, BurntFish, EdiblePlant, Bandage}.fbx`, `Art/Icons/ICON_{RawFish, CookedFish, BurntFish, EdiblePlant, Bandage}.png`.
Made by the builder on the PC: `Resources/StatusEffects/SE_*.asset`, `Data/Items/ITEM_{raw_fish, cooked_fish, burnt_fish, edible_plant}.asset`,
item data (spoil hours, bandage). Art sources: `Tools/S_art/` (Blender output), icons drawn with PIL in the mirror (`src_assets/phase3`).
`CraftingSystem` no longer calls `ResourceNode.ToolName` (RES moved it; crafting keeps its own tool words).

## 3. Tuning values (SurvivalConfig; hand edits in the asset win)

- Water: `mlPerCharge` 250.
- Fire: `fuelBurnRate` 1, `lightingSeconds` 3, `lowFuelSeconds` 60, `fullIntensityFuelSeconds` 180, `minFireIntensity` 0.3,
  `lowFireCookRate` 0.6, `rainFireIntensity` 0.7, `rainFuelMultiplier` 1.5 (was 2 on the component), `heavyRainIntensity` 0.9,
  `heavyRainFuelMultiplier` 2, `heavyRainFireIntensity` 0.5, `heavyRainExtinguishSeconds` 45 (x2 speed when low). Wood still
  240 s per piece (a refuel every 4 min, about 2.7 min in rain), max 1200 s.
- Wetness / sun (per second): rain +0.04, standing in water +0.3 at 0.8 m, water chill 3 C, drying 0.01 + 0.004 per C of warmth
  + 0.01 under a roof + 0.006 in sun, sun +2.5 C, Wet status above 0.3.
- Injuries: `deepWoundDamage` 30, `deepWoundSeconds` 75, `repeatCutDeepens` on, `injuryMinDamage` 25, `injuryChance` 0.6,
  `legInjuryShare` 0.5, `injurySeconds` 300, `fallInjurySpeed` 13 m/s.
- Healing: `restRegenMultiplier` 1.75, `restInjuryHealMultiplier` 2; Recovering +0.25 HP/s (SE_recovering).
- Spoilage: `agingAt` 0.5, `agingNutrition` 0.8, `spoiledNutrition` 0.35, `agingSickChance` +0.05, `spoiledSickChance` 0.5,
  `spoiledSickSeconds` 60, `spoilCheckSeconds` 5. Spoil hours (game hours): raw fish 12, raw meat 24, fruit mash 24, edible
  plant 30, berries 36, cooked fish 36, cooked meat 48, fruit 48, burnt meat / fish 72.
- Status effects (SE_* assets): bleeding -0.8 HP/s (default 20 s, max 180 s, blocks regen); sickness -0.3 HP/s, thirst x1.6,
  stamina regen x0.5, max stamina x0.85 (default 60 s); leg injury speed x0.82, sprint cost x1.5; arm injury attack x0.7,
  gather x0.8; recovering +0.25 HP/s (max 240 s).
- Items: raw fish 10 hunger / 2 thirst, 25 % sick, cooks 10 s; cooked fish 26 / 4 / +5 health (slowly); burnt fish 4;
  wild greens 6 hunger / 4 thirst / 3 stamina; bandage cures bleeding, +15 health slowly.

## 4. Requests to other agents

| To | Request | Why |
|---|---|---|
| AI (P_) | Register the creature section: `SaveSystem.RegisterSection(new AI.CreatureSaveSection(spawner))` in `GameManager.Awake` and `: ISaveSection` (namespace `PrimalFrontier.Core`) on `CreatureSaveSection` | item 1a is live on the PC |
| AI | `FireSense`: use `Campfire.FuelIntensity01` (fuel + lighting, no weather) with your rain rule, or `Intensity01` (rain included); `Campfire.State`, `Sheltered`, `RainedOn`. At 60 s of fuel `FuelIntensity01` is 0.53 (your interim 60/300 = 0.2): `PerceptionTests.Fire_Fear_Radius_...` expectations change with the switch | read API |
| AI | `PlayerSignature.IsBleeding` can stay on `PlayerHealth.IsBleeding` (it now reads the bleeding effect) or use `PlayerStatusEffects.Player.Has(StatusEffectIds.Bleeding)` | same value |
| AI | `WeatherManager`: let some rains turn into a storm in the random schedule (e.g. 15 %). Only heavy rain (intensity >= 0.9 = storm) puts out an open fire, and storms never happen on their own today | directive 25 in play |
| U (U_) | Arm injury: scale the player's attack damage by `PlayerStatusEffects.AttackMultiplier` (bare hands and weapons) | directive 39 "weaker attacks" |
| U | `WeaponBase.Broke` plays ToolBreak + DustImpact itself; `InventorySystem.WearActive` now does that for every break (HUD), so a weapon break plays the sound twice: drop the sound / dust from `Broke` (the note is deduplicated already) | item 10 |
| U / CHAR | Bandage_Use clip: event `OnUseItem` (the treatment happens on it, else at 1.6 s). Collect_Water clip: the fill happens on `OnDrink` or at the end of the action (2 s fallback); name the contact event `OnDrink` or tell me the new name | item 2, 4c |
| U | `ContextHints`: a hint for the bandage in hand ("[LMB] Use bandage") | usability |
| RES (R_) | Place the fish / edible plant nodes (items `raw_fish`, `edible_plant` exist with world models); `WorldPickup.DropStack` should keep a `uniqueStack` (or the `madeAt`) for food that spoils, otherwise dropping spoiled food and picking it up makes it fresh; ResourceNode / TreeHarvest / Carcass "X broke!" notes can go (WearActive reports breaks); optionally scale gathering by `PlayerStatusEffects.GatherMultiplier` (arm injury) | items 5, 10 |
| Lead | `GameEvents.cs`: 7 values appended. The bandage icon / model came from the fibre item; it has its own now (builder S5) | |

## 5. Full suite

- Full PlayMode suite `R_13` (started by RES, 2026-09-29 12:19-12:32 UTC, with all my runtime code from S2-S4 deployed):
  **143 passed, 0 failed, 8 skipped** in 714.6 s (skipped: 6 CharacterMotionProbe diagnostics, SurvivalShowcase and my
  capture test, both capture-only). Copy: `Tools/playmode_results_R13_full_after_S4.xml`.
- After S5 (builder + bandage art only, editor side): `S_10` SurvivalLoopTests + SurvivalPhase3IslandTests + SurvivalM1Tests
  16 passed, 0 failed, 1 skipped (capture) (`Tools/playmode_results_S10.xml`).
- Earlier targeted runs: `S_5` 32 / 0 (phase 3 + hitch + needs + M1), `S_6` 25 / 0 (M1 loop, water reach, player feedback,
  survival loop, bare-hand combat), `S_7` 17 / 1 (the island save test had a test bug: it read a destroyed campfire's position
  after the load; fixed in S4), `S_8` 3 / 0.
- Compile: `./cc.sh all` against the PC state of the other agents' files: runtime / editor / tests rc 0; every deploy
  compiled on the PC with no errors. All my files on the PC match the mirror (md5, 12:36 UTC).
- Bridge note for the Lead: at 12:19 I took the free lock, but a PlayMode run started by RES (R_13) was still going until
  12:32, so the editor did not take my refresh; the lock was released while their run was still busy.

## 6. Known limits

- Nobody has played it by hand: the feel of drain / spoil / fire times, the look of the status icons at phone size, and the
  sounds (boil loop, fill, hiss, bandage) are NOT TESTED - MANUAL TEST REQUIRED.
- Food dropped on the ground loses its age until RES keeps the stack (request above).
- A storm is the only heavy rain; storms are not in the random weather schedule yet (request to AI).
- The HUD status words ("Wet", "Bleeding") sit under the vitals panel where the touch menu buttons also are (scene layout);
  the icons were moved to the right of the panel for that reason.
- Wading detection for ponds uses the surface points of `WaterSource` (0.8 m apart): the very edge of the water may not count.
