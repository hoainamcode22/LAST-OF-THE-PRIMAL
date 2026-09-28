# PRIMAL FRONTIER - Survival Systems Audit (Milestone 1 baseline)

Date: 2026-09-28. Scope: read-only audit of `src/Scripts` and `src/Tests` against the MASTER DIRECTIVE survival loop
(explore -> gather -> craft -> hunt -> cook -> drink -> build -> survive the night -> expand) and its first milestone
(gather -> campfire -> cooking -> water -> hunger -> thirst -> tent / shelter). No code was changed.
Line numbers are from the mirror at audit time; other agents are editing in parallel, so treat them as approximate.

Legend: **COMPLETE** = does what M1 needs; **PARTIAL** = exists, needs extension; **MISSING** = nothing today.

---

## 1. Executive summary

- The survival core already exists and is reasonably clean: `PlayerSurvival` (needs, stamina, temperature, wetness,
  sickness), `PlayerHealth`, `TimeManager` + `GameClock`, `WeatherManager` (real rain with `Intensity` and
  `RainingAt(p)`), `InventorySystem` / `ItemDatabase` / `ItemDefinition` (ScriptableObjects), `CraftingSystem`
  (queue, stations, tool requirements, unlocks), `BuildSystem` (ghost placement), `Campfire` (fuel, heat, cooking,
  boiling), `WaterSource` / `OceanShore`, `Shelter` / `Bedroll`, `ResourceNode` / `TreeHarvest`, `Carcass`, `SaveSystem` v3.
- Nothing needs to be rewritten. M1 is mostly **extension + consolidation**: water types, rain collector, cooking states,
  fuel as data, tiered need effects, tent, save v4.
- Main problems: survival rules are **scattered** (drinking in 4 classes, eating / sickness in `PlayerInteraction`,
  sleep costs / respawn stats / environment hooks in `GameManager`), **two cooking paths** for meat, many **magic
  numbers** in code, a few **dead features** (`Overweight` never set, `Shelter.warmth` unused, `Warm()` unused), and
  **state lost on save/load** (food on the fire, boiling, sickness).
- No `SurvivalManager`, `WaterSystem`, `CookingSystem` or `ConstructionSystem` class exists; the directive's systems map
  mostly onto existing classes (section 3). Only a few genuinely new types are needed (section 9).

---

## 2. Existing systems inventory

### 2.1 Player needs, health, state

| Class (file) | Key members | Status | Notes |
|---|---|---|---|
| `PlayerSurvival` (`Survival/PlayerSurvival.cs`) | `Hunger/Thirst/Stamina/BodyTemperature/Wetness/SickSeconds` (L39-45), `Consume()` L82, `MakeSick()` L91, `SetStats()` L99, static hooks `AirTemperature/RainingAt/HeatAt` L107-111, `Update()` L113-152, `Warning` event L56, implements `IStaminaSource` | PARTIAL | One component = hunger + thirst + stamina + temperature + wetness + sickness. Effects are mostly binary (see 5.1). `Overweight` (L52) is never assigned anywhere. `Warm()` (L97) is never called. |
| `PlayerHealth` (`Player/PlayerHealth.cs`) | `maxHealth` L9, `bleedDamagePerSecond` L10, `TakeDamage` L37, `ApplyRaw` L47, `Heal` L54, `Revive` L62, `SetHealth` L67, events `Damaged/Healed/Died/Revived` | COMPLETE | Survival damage uses `ApplyRaw` (no hit reaction). Doc comment still says "PlayerVitals" (stale name). |
| `IStaminaSource` (`Player/PlayerMotor.cs` L6-11) | `CanSprint`, `DrainSprint`, `MoveSpeedMultiplier` | COMPLETE | Clean seam: the motor has no survival logic. Keep this contract. |
| `PlayerState` (`Player/PlayerState.cs`) | `PlayerMode {Explore, Combat, Aiming, Climbing, Building, Busy, Sleeping, Dead}` L7, `Evaluate()` L37 | COMPLETE | Read-only derived state; reads `GameManager.State == Sleeping`. |
| `PlayerWetLook` (`Player/PlayerWetLook.cs`) | reads `PlayerSurvival.Wetness` | COMPLETE | Presentation only. |

### 2.2 Time, weather, environment

| Class | Key members | Status | Notes |
|---|---|---|---|
| `TimeManager` (`Core/TimeManager.cs`) | `hour/day`, `secondsPerHour = 90` L18 (36 min/day), `sunriseHour 6 / sunsetHour 19.5` L21, `IsNight` L28, `NightStarted/DayStarted/NewDay` L32-33, `SkipHours()` L100, `HoursUntil()` L105, `AirTemperature()` L140 | COMPLETE | Air temp 15..28 C (peak 15:00), -5 C at full overcast. |
| `GameClock` (`Core/GameClock.cs`) | `Now` (saved), `Hours(h)` | COMPLETE | All regrow / expiry timers use it (survives sleep + save). |
| `WeatherManager` (`Core/WeatherManager.cs`) | `WeatherState {Clear, Rain, Storm, Cloudy}` L7, `Intensity` L21, `Overcast`, `Wetness`, `SetWeather()` L68, `RainingAt(p)` L149 (Intensity > 0.25, not under `Shelter`, not indoor zone), `Changed` event | COMPLETE | Rain exists and is queryable per position. **Nothing collects rain yet.** Rain doubles campfire fuel burn and wets the player. |
| `ZoneManager` (`World/ZoneManager.cs`) | `IsIndoor(p)`, `TemperatureOffset(p)` (Rocky -2, Cave -4) | COMPLETE | Cave = indoor (no rain). |
| `GameManager.HookEnvironment()` (`Core/GameManager.cs` L151-182) | assigns the three `PlayerSurvival` static hooks; heat = `Campfire.HeatAt + Shelter.WarmthAt + torch 2` | PARTIAL | Survival wiring living in the game-flow class (refactor target). `GetComponent<PlayerEquipment>()` every frame inside `HeatAt`. |

### 2.3 Items, inventory, crafting

| Class | Key members | Status | Notes |
|---|---|---|---|
| `ItemDefinition` SO (`Items/ItemDefinition.cs`) | identity L10-16, food `hunger/thirst/health/stamina/sicknessChance/isHot` L26-31, `cookedResult` L32, `cookSeconds` L33, `tool/toolPower` L36-37, `waterCharges` L46, `placePrefab/placeRadius` L49-50, `lightRange` L53, `weaponData` L57, `IsFood/IsPlaceable/IsWaterContainer` L59-61 | PARTIAL | Data-driven, good. Missing for M1: fuel value, burn time / burnt result, water type rules. `IsFood` is true for the bandage (health only), so it is "eaten". |
| `ItemEnums` (`Items/ItemEnums.cs`) | `ItemCategory` L3, `ToolKind` (flags) L5, `WeaponKind` L6, `CraftStation {None, Campfire}` L7, `RecipeCategory` L8 | COMPLETE | All stored as ints: **append only**. No `WaterType`. |
| `ItemStack` (`Items/ItemStack.cs`) | `count`, `durability`, `water` (int charges) L12, `dirty` (bool) L14, `Clone()` | PARTIAL | Water is "charges + dirty flag", not a typed liquid. |
| `ItemDatabase` SO (`Items/ItemDatabase.cs`) | `Resources/ItemDatabase`, `items`, `recipes`, `Item(id)` L30, `Recipe(id)` L31 | COMPLETE | Single registry for items and recipes (= CraftingRecipeDatabase). 33 items / 25 recipes (IMPLEMENTATION_STATUS). |
| `InventorySystem` (`Items/InventorySystem.cs`) | `Add` L72, `AddStack` L109 (unique if `water != 0`, L112), `Remove` L135, `Move` L165, `QuickTransfer` L195 (L199 water check), `WearActive` L232, `IsOverweight` L27, `maxWeight 45` | COMPLETE | "Never duplicates or loses items" invariant, tested. Used by the player and `StorageBox`. |
| `CraftingSystem` (`Items/CraftingSystem.cs`) | `MaxQueue 5`, `Check` L132, `Enqueue` L161, `Cancel` (refund) L170, `Update` L184 (progress only when `StationsAvailable` + `HandsFree`), `StationAvailable` L64 (`Campfire.LitNear`), `CheckRequirements` (tool in pack / station / `minDay`), recipe learning on `ItemAdded` L53, save helpers L220-246 | COMPLETE | Reuse as is. |
| `RecipeDefinition` SO / `CraftingRequirement` / `CraftingResult` | `station`, `knownAtStart`, `requirements[]`, `extraResults[]` | COMPLETE | |

### 2.4 World: gathering, hunting, fire, water, shelter

| Class | Key members | Status | Notes |
|---|---|---|---|
| `ResourceNode` (`World/ResourceNode.cs`) | `yieldItem/yieldPerHit/charges` L17-19, `bonusItem/bonusChance` L20-21 (one-entry "drop table"), `requiredTool/fasterTool` L22-23, `regrowHours` (GameClock) L26, `Hit()` L92, `Restore()` L133 | COMPLETE | Driftwood, loose stones, fibre, berries, rocks (built by `PrimalGameplayBuilder`). No generic drop-table SO. |
| `TreeHarvest` (`World/TreeHarvest.cs`) | one interactable for all terrain trees, `hitsPerTree 6`, `regrowHours 72`, `Hit()` L123 (2 x toolPower wood, 25 % fibre), `Fell()` (+3 wood pickup) | COMPLETE | Needs a `Chop` tool in hand (hand stone works at 0.5 power). |
| `BushInteraction`, `FruitCluster`, `WorldPickup`, `LootContainer` | bush hidden loot table (`BushLoot`), fruit regrow 30 h, pickups (Instantiate, not pooled), wreck crates | COMPLETE | Start kit comes from wreck loot (rope 2, raw meat 2, gourd, berries 4, bone 2, fibre 4, wood 3, stone 2). |
| `Carcass` (`World/Carcass.cs`) | `KindIds = {"raw_meat","hide","bone"}` L59, `Setup()` L68, `Cut()` (knife = full yield, hands = half the meat), expiry 24 h, sink | PARTIAL | Butchering works. Item ids hardcoded; carcasses are not saved. |
| `DinosaurController`, `AmbientCreature`, `DinosaurSpawner`, `DinosaurDefinition` (`AI/`) | death -> `Carcass.Setup` (L347-353 / L79), `DinosaurDefinition.meat/hide/bone` ints L40, `SprayLoot()` hardcoded ids L364 | PARTIAL | Hunting loop works; no respawn over time; kills not saved (a load respawns every creature). Out of M1 scope. |
| `Campfire` (`World/Campfire.cs`) | `fuelItem` L19, `secondsPerFuel 240` L20, `maxFuelSeconds 1200` L21, `heatRadius 5 / heat 16` L22-23, `LitNear` L36, `HeatAt` L41, `Update` L53 (rain x2 burn L57-58, cooking L59-68), `SetLit` L73, boiling L82-107, `GetPrompt` L116, `Interact` L150, `Restore` L190 | PARTIAL | Fuel = one wood item reference; light = 1 fuel, no fire starter. Cooking: max 4 slots, timer = `raw.cookSeconds`, result dropped as a `WorldPickup` beside the fire, **no ready / burned state**, not visible on the fire, **not saved** (`Restore` clears `_cooking`, raw meat is lost), paused and unreachable while unlit. Boils one container at a time (8 s) by holding a reference to the inventory stack; boiling is not saved. |
| `CampfireFx` (`VFX/CampfireFx.cs`) | `SetLit`, `SetCooking` | COMPLETE | Presentation. `SetLit` allocates a 3-element array (`new[] {flames, smoke, embers}`) per call (rare). |
| `WaterSource` (`World/WaterSource.cs`) | `fresh`, `clean` L20, `thirstPerDrink 28` L21, `DirtySickChance 0.2` (static) L23, fill L95-100 (`dirty = !clean`), `Drink()` L109 (+28 thirst, +4 stamina; salt branch -6 L117-122), `IsNearFresh` | PARTIAL | Pond + stream (both dirty). Drinking by hand allowed (20 % sick for 60 s). |
| `OceanShore` (`World/OceanShore.cs`) | terrain-height shore detection `Update` L23, `Interact` L58: thirst -6 | PARTIAL | **Sea water is not drinkable today** (makes thirst worse) and **cannot be collected** in a container. Duplicates the `WaterSource` salt branch. |
| `Shelter` (`World/Shelter.cs`) | `coverRadius 2.3` L13, `warmth 4` L14 (**unused**), `Covers(p)` L21, `WarmthAt` L31 returns literal `4f`, `Interact` = rest: save + respawn point | PARTIAL | Lean-to only; no sleep. |
| `Bedroll` (`World/Bedroll.cs`) | `sleepFromHour 18 / sleepUntilHour 6` L11, `CanSleepNow` L18, `SleepRequested` static action | COMPLETE | Sleep flow lives in `GameManager.Sleep()` (L359-382). |
| `StorageBox` (`World/StorageBox.cs`) | own `InventorySystem` (20 slots, no weight) | COMPLETE | Saved via `StructureData.contents`. |
| `BuildSystem` (`Building/BuildSystem.cs`) | `Begin` L40, `Evaluate` L81 (slope 24, water, distance 6, overlap), `Build` L111 (Build loop, 2 hits), static `Spawn()` L127, `Placed` event | PARTIAL | Single-prefab placement only ("no modular base building" by earlier scope rule). Per-frame allocations in build mode (5.4). |
| `PlacedStructure` (`Building/PlacedStructure.cs`) | `itemId`, `uid`, `All` | COMPLETE | Save key for placed objects. |
| `PlayerEquipment` (torch) (`Player/PlayerEquipment.cs`) | `TorchLit` L28, flame from `lightRange` | COMPLETE | Torch has no fuel / burn time; gives +2 C (GameManager). |

### 2.5 Interaction, events, story, UI, save

| Class | Key members | Status | Notes |
|---|---|---|---|
| `IInteractable` / `Interactable` (`World/Interactable.cs`) | `GetPrompt/CanInteract/Interact`, hold action, `SaveId` L25, static `Active` list | COMPLETE | The one interaction contract. Reuse for rain collector / tent. |
| `IDamageable` (`Combat/IDamageable.cs`) | `IsAlive`, `TakeHit(HitInfo)` | COMPLETE | Creatures. |
| `PlayerInteraction` (`Player/PlayerInteraction.cs`) | target scan, `DoOneShot/DoLoop`, `UseActiveConsumable()` L278 (container: +30 thirst, +5 stamina, dirty sick roll L291-293), `Eat()` L300 (sickness 25 s L308), `GiveOrDrop`, `HasTool` | PARTIAL | Action runner is good; **consumption rules do not belong here**. `UpdatePrompt` calls `GetPrompt` every frame (string allocations). |
| `PlayerCombat` (`Player/PlayerCombat.cs` L97-98) | attack button on food/water -> `UseActiveConsumable`, on placeable -> `BuildSystem.Begin` | PARTIAL | Input routing only; acceptable, but should call one item-use entry point. |
| `GameEvents` (`Core/GameEvents.cs`) | `GameEventType` (append only) incl. `Ate, Drank, WaterFilled, TriedSaltWater, FireLit, FireOut, FoodCooked, StructurePlaced, Slept, NightStarted, WaterBoiled, GotSick, CarcassButchered`; `Raise()` L47 | COMPLETE | `Raise` calls `GetInvocationList()` (allocates per event). |
| `TutorialManager` (`Story/TutorialManager.cs`) | 19 steps L71-91 (look ... campfire, cook, ... shelter, night); `Count(id)` L62 | PARTIAL | Uses literal item ids; "water" step completes on any `Drank` (dirty water counts); no boil / rain step. Saved as an **index** (`tutorialStep`), so inserting steps breaks old saves. |
| `JournalSystem` (`Story/JournalSystem.cs`) | entries keyed by event type + id | COMPLETE | `fresh_water` page only unlocks from `Drank("fresh_water")` (hand drinking), not from containers (`clean_water`/`dirty_water`). |
| `HUDManager` (`UI/HUDManager.cs`) | vitals rows L94-98: **health, hunger, thirst, stamina, temperature**; status text (Bleeding, Stomach sick, Wet, Cold, Overburdened) L319-325; hotbar water `n/max`, dirty colour | PARTIAL | No wetness bar, no tier labels. Allocates strings every frame (5.4). Row names (`healthBar` ...) are pinned by `SceneAuthoringTests`. |
| `InventoryUI` (`UI/InventoryUI.cs`) | Use / Equip / Drop / Split; water text "unboiled / boiled" L290; crafting tabs from `RecipeCategory` L150 | PARTIAL | No Empty / Pour action. |
| `SaveData` / `SaveSystem` (`Core/SaveData.cs`, `Core/SaveSystem.cs`) | `CurrentVersion = 3` L11; saves time, clock, weather, player pos, health, hunger, thirst, stamina, body temp, wetness, inventory (`SlotData` water + dirty), known recipes, craft queue, tutorial, journal, zones, nodes, taken pickups, loot, examined, felled trees, structures (`StructureData`: campfire lit + fuel, storage contents), dropped items; newer versions refused | PARTIAL | **Not saved**: `SickSeconds`, campfire cooking slots, boiling, carcasses, creature deaths, torch. Storm loads as Rain (L150). |

### 2.6 Editor builders (who owns which data)

| Builder | Owns | Mode | Rule for M1 |
|---|---|---|---|
| `PrimalGameplayBuilder` | 21 base items (wood, stone, fiber, rope, hide, bone, berries, raw/cooked meat, tools, torch, arrow, spear, bow, water_container, campfire, shelter, storage, bedroll) L217-254; 16 recipes L271-286 incl. `RCP_cooked_meat` at Campfire; placeable prefabs L341-359 (campfire `fuelItem = wood`); resource nodes, water sources, wreck loot, zones, `[Game]` | **Overwrites** (resets optional stats on re-run, drops items added elsewhere) | **Never re-run, never edit.** |
| `PrimalCraftingBuilder` | 8 items (sharpened_flint, sinew_cord, bandage, fruit_mash, butcher_knife, bone_arrow, hunting_bow, leather_waterskin), 9 recipes, `SyncCookTimes()` (cooked_meat recipe = 14 s) | Additive, hand edits win | Do not extend; model the new builder on it. |
| `PrimalPhase2Builder` | `fruit` item, knife -> melee, fruit trees, journal pages (fruit, boiled_water, stomach_sick) | Additive | Leave. |
| `PrimalWeaponBuilder` | `WeaponData` assets, `flint_sword` | Additive | Leave (combat agent). |
| `PrimalBushBuilder`, `PrimalDinoBuilder`, `PrimalSceneBaker`, `PrimalPlayerSetup` | bushes + berry nodes; dinosaur definitions (meat/hide/bone ints); `[Systems]` bootstrap; `PFB_Player` components | Mixed | Touch `PrimalSceneBaker` only to register a new system object, if any. |

---

## 3. Directive systems -> existing classes

| Directive system | Existing class(es) | Verdict | Action |
|---|---|---|---|
| SurvivalManager | `GameManager.HookEnvironment/Sleep/Respawn/ResetWorld/DeathCause` + `PlayerSurvival` statics | MISSING as a class (role split across game flow) | Do **not** add a new manager MonoBehaviour. Add `SurvivalConfig` SO + static `SurvivalEnvironment`; move rules out of `GameManager`. |
| PlayerNeeds | `PlayerSurvival` | REUSE (partial) | Keep class name (serialized on prefab/scene). Add tiers from config. |
| HealthSystem | `PlayerHealth` | REUSE | none |
| StaminaSystem | `PlayerSurvival` via `IStaminaSource` | REUSE | Tier multipliers only. |
| HungerSystem / ThirstSystem | `PlayerSurvival.Update` | REUSE (partial) | Data-driven tiers (medium / low / critical). No separate components. |
| TemperatureSystem | `PlayerSurvival` + `TimeManager.AirTemperature` + `ZoneManager` + `Campfire.HeatAt` + `Shelter.WarmthAt` | REUSE | Fix `Shelter.warmth`; move constants to config. |
| TimeOfDaySystem | `TimeManager`, `GameClock` | REUSE (complete) | none |
| WeatherSystem | `WeatherManager` | REUSE (complete) | Rain collector reads `RainingAt(p)` / `Intensity`. |
| InventorySystem | `InventorySystem` | REUSE (complete) | Keep invariants. |
| ItemDatabase | `ItemDatabase` | REUSE (complete) | New items via additive builder. |
| ResourceNodeSystem / HarvestSystem | `ResourceNode`, `TreeHarvest`, `BushInteraction`, `FruitCluster`, `PlayerInteraction.DoLoop` | REUSE (complete for M1) | Optional later: drop-table SO. |
| CraftingSystem / CraftingRecipeDatabase | `CraftingSystem`, `RecipeDefinition`, `ItemDatabase.recipes` | REUSE (complete) | Do not create a second recipe DB. |
| BuildingSystem | `BuildSystem`, `PlacedStructure` | REUSE (partial) | Tent / rain collector are single placeables; modular pieces later. |
| ConstructionSystem | none | MISSING (not M1) | Later milestone (modular pieces, staged build). |
| CookingSystem | `Campfire` cooking + `RCP_cooked_meat` crafting recipe | DUPLICATED / PARTIAL | One path: station slots with states (section 9). |
| FoodSystem | `ItemDefinition` food fields + `PlayerInteraction.Eat` + `PlayerSurvival.Consume` | PARTIAL | Move eat rules into `PlayerSurvival.ConsumeItem`. |
| FireSystem | `Campfire`, `CampfireFx`, torch in `PlayerEquipment` | PARTIAL | Fuel from item data; keep in `Campfire`. |
| HuntingSystem / AnimalSystem | `DinosaurController`, `AmbientCreature`, `DinosaurSpawner`, `DinosaurDefinition` | REUSE | Respawn / persistence later. |
| ButcheringSystem | `Carcass` | REUSE | Replace `KindIds` with item refs (later). |
| WaterSystem | `WaterSource`, `OceanShore`, `ItemStack.water/dirty`, `Campfire` boil, `PlayerInteraction` drink | SCATTERED / PARTIAL | New static `WaterRules` + `WaterType`; all four callers use it. |
| ShelterSystem | `Shelter`, `Bedroll`, `ZoneManager.IsIndoor` | PARTIAL | Tent = prefab with `Shelter` + `Bedroll`. |
| SaveSystem | `SaveSystem`, `SaveData` | REUSE | v4 with migration. |

---

## 4. Conflicts, duplication, refactor targets

| # | Problem | Where | Fix |
|---|---|---|---|
| 1 | Two cooking paths for meat | `Campfire.Interact` cook (L174-181, `raw.cookSeconds` 14 s) and `RCP_cooked_meat` (station Campfire, `PrimalGameplayBuilder` L282, synced to 14 s by `PrimalCraftingBuilder.SyncCookTimes`) | Keep station cooking; hide / remove the single-ingredient campfire recipe via the new builder (never via the gameplay builder). `fruit_mash` (2 ingredients) may stay a campfire recipe. |
| 2 | Drinking rules in 4 places | `WaterSource.Drink` L109, `OceanShore.Interact` L58, `PlayerInteraction.UseActiveConsumable` L278, `Campfire.UpdateBoil` L94 | One `WaterRules` service keyed by `WaterType`. |
| 3 | Salt water implemented twice | `WaterSource` (`fresh = false`) L117-122 and `OceanShore` L63 | Both call `WaterRules.Drink(SaltWater)`. |
| 4 | Eat / sickness rules in a player controller class | `PlayerInteraction.Eat` L300-311 (sick 25 s), container drink L291-293 (sick 60 s) | `PlayerSurvival.ConsumeItem(item)` / `DrinkWater(type)`; `PlayerInteraction` keeps only the animation + inventory removal. |
| 5 | Survival rules in `GameManager` | environment hooks L151-160, sleep costs L372, respawn stats L353, new-game stats L286, `DeathCause` L331 | Move numbers to `SurvivalConfig`; environment to `SurvivalEnvironment`; `PlayerSurvival.ApplySleep(hours)`. `GameManager` keeps flow (fade, time skip, save). |
| 6 | Start values defined twice | `PlayerSurvival` initializers 85 / 70 (L39-40) and `GameManager.ResetWorld` `SetStats(85, 70, 100, 37, 0)` | Single source in config. |
| 7 | Dead feature: overweight | `PlayerSurvival.Overweight` (L52) never set; `InventorySystem.IsOverweight` exists; HUD "Overburdened" never shows | `PlayerSurvival` reads the inventory (event `Changed`). |
| 8 | Dead field | `Shelter.warmth` (L14) ignored; `WarmthAt` returns `4f` (L31) | Use the nearest covering shelter's `warmth`. |
| 9 | Dead method | `PlayerSurvival.Warm()` L97 | Remove or use for "warm up at fire" action. |
| 10 | Food lost | `Campfire.Restore` clears `_cooking` (L192); unlit fire keeps raw items unreachable | Save slots; allow taking items from an unlit fire. |
| 11 | Boil holds an inventory stack reference | `Campfire._boiling` L83, `FindDirtyWater` | Dropping the container clones the stack (orphan boils). Boil should take the container into a fire slot (same as cooking). |
| 12 | Single fuel item | `Campfire.fuelItem` (prefab ref to wood) | `ItemDefinition.fuelSeconds`; any item with fuel > 0 burns. |
| 13 | Player referenced 4 ways | `PlayerLocator`, `GameManager.Player`, `PlayerState.Instance`, tag (PROJECT_AUDIT) | New code uses `PlayerLocator` / the `PlayerInteraction` passed to `Interact`. |
| 14 | Bandage counts as food | `ItemDefinition.IsFood` L59 (health only) | Later: `IsConsumable` vs `IsFood`; not M1-blocking. |
| 15 | Bootstrap duplicated | `GameManager.Ensure` and `PrimalSceneBaker` `[Systems]` list | Any new system object must be added in both. |

### 4.1 Hardcoded item / event ids in runtime code

| File:line | Literal | Risk |
|---|---|---|
| `World/Carcass.cs:59` | `"raw_meat","hide","bone"` | Loot ids; pinned by `DinosaurTests`. |
| `AI/DinosaurController.cs:364` | `"raw_meat","hide","bone"` (spray loot) | Same. |
| `Core/GameManager.cs:82-83` | `"wood","fiber"` (TreeHarvest fallback) | Also in `PrimalSceneBaker` L94. |
| `Story/TutorialManager.cs:74-90` | `"wood","stone","stone_axe","fiber","campfire","stone_spear","shelter"` | Tutorial logic in code. |
| `Story/JournalSystem.cs` defaults | `"fresh_water","shelter","stone_axe","rope","stone_spear","bow"` | Data list, editable in Inspector. |
| `Player/PlayerInteraction.cs:293`, `World/WaterSource.cs:115,121`, `World/OceanShore.cs:65` | event ids `"clean_water","dirty_water","fresh_water","ocean"` | Should come from `WaterType`. |
| `World/Campfire.cs:78,106`, `Core/GameManager.cs:374` | event ids `"campfire","water","bedroll"` | Should be the item id of the placed structure. |

Recommendation: an `ItemRefs` section in `SurvivalConfig` (fields of type `ItemDefinition`: rawMeat, hide, bone, wood, fiber, burntFood...) resolved once; no new literals.

---

## 5. Current numbers (drain rates, thresholds, magic numbers)

### 5.1 Needs, stamina, temperature (`PlayerSurvival`)

| Value | Default | Where | Inspector? |
|---|---|---|---|
| Hunger drain | 2.4 / real min (full -> empty ~42 min, ~1.16 game days) | L17 | yes |
| Thirst drain | 3.8 / real min (~26 min, ~0.73 game days) | L18 | yes |
| Sprint drain multiplier | x1.8 (hunger and thirst) | L19 | yes |
| Hot air thirst multiplier | x1.3 when env > 28 C | L119 | no |
| Sick thirst multiplier / stamina regen | x1.6 / x0.5 | L36 | yes |
| Start hunger / thirst | 85 / 70 | L39-40 + `GameManager` L286 | no |
| Max stamina | 100; `MaxStaminaNow` = lerp(0.6, 1, min(H,T)/25) | L21, L50 | partly |
| Sprint cost / regen / delay / jump | 16 /s, 14 /s, 0.9 s, 8 | L22-25 | yes |
| Can sprint threshold | stamina > 12 (0.5 while already sprinting) | L68 | no |
| Move speed penalties | overweight x0.72, H or T < 12 x0.9, freezing x0.9 | L70 | no |
| Cold stamina regen | x0.6 | L123 | no |
| Body temp normal / cold / freezing | 37 / 35.2 / 34.2 C | L27-29 | yes |
| Body response | 0.02 C/s (x3 when warming) | L30, L134 | partly |
| Body target curve | env >= 18 C -> 37; else lerp(33.5, 37, (env-4)/14) | L133 | no |
| Wetness | +0.04 /s in rain; dry 0.01 + heat x 0.004 /s; -5 C at full wet; sprint +2 C | L130-131 | no |
| Damage at empty / freezing / sick | starve 0.25, dehydrate 0.35, freeze 0.2, sick 0.3 HP/s | L32-35 | yes |
| Health regen | 0.12 HP/s when H > 55 and T > 55, not cold, not bleeding | L37, L143 | partly |
| Warnings | hungry < 20, thirsty < 20, cold, starving 0, dehydrated 0 | L146-150 | no |
| Bleed | 1.2 HP/s | `PlayerHealth` L10 | yes |

Gradual-effect gap vs the directive: medium (slower stamina regen) **missing**; low (lower max stamina) **exists** (lerp below 25, H and T combined); critical (health loss) exists **only at 0**.

### 5.2 Consumption, water, fire, shelter, sleep

| Value | Default | Where |
|---|---|---|
| Hand drink fresh water | +28 thirst, +4 stamina (literal) | `WaterSource` L21, L113 |
| Container drink | +30 thirst, +5 stamina (literal) | `PlayerInteraction` L291 |
| Salt water | -6 thirst (two copies) | `WaterSource` L119, `OceanShore` L63 |
| Dirty water sickness | 20 % chance (static), 60 s | `WaterSource` L23, L114; `PlayerInteraction` L292 |
| Raw meat | +10 hunger, 35 % sickness (data), 25 s (literal) | builder L226; `PlayerInteraction` L308 |
| Cooked meat | +35 hunger, +6 health, hot | builder L225 |
| Cook time raw meat | 14 s (item), max 4 items on a fire (literal x3) | builder L226; `Campfire` L128, L164, L174 |
| Container capacity | gourd 3, waterskin 5 | builders |
| Boil time | 8 s, one container | `Campfire` L82 |
| Fuel | 240 s per wood, max 1200 s, x2 burn in rain | `Campfire` L20-21, L58 |
| Fire heat | +16 C at centre, linear to 0 at 5 m | `Campfire` L22-23 |
| Shelter | cover radius 2.3 m, height 2.5 m, warmth literal 4 C | `Shelter` L13, L27, L31 |
| Torch warmth | +2 C (literal) | `GameManager` L158 |
| Air temperature | 15..28 C, overcast -5, altitude -0.06 C/m above 30 m | `TimeManager` L144, `GameManager` L153 |
| Sleep window / wake | 18:00..05:00 / 06:00 | `Bedroll` L11, L18; `GameManager` L369 |
| Sleep cost | hunger -2.2 /h, thirst -2.8 /h (floor 5), health +4 /h, stamina +100 | `GameManager` L372 |
| Respawn | health 60 %, H/T floor 40, stamina 60, body 36.5 | `GameManager` L351-353 |
| Autosave | 600 s | `GameManager` L317 |
| Rain | intensity 0.75 (storm 1), counts as raining > 0.25, clouds 12 %/h, rain 25 %/h for 1-3 h | `WeatherManager` |

Design note: without rain a night is not dangerous (15 C before dawn -> body target ~36.3 C, above "cold"). Only wet +
overcast nights reach freezing. Predators see 40 % less at night (`DinosaurController` L106). "Survive the night"
currently has little pressure; M1 tuning should add it through the config, not new code.

### 5.3 Tests that pin current behaviour

| Test (file) | Pins | Breaks if |
|---|---|---|
| `SurvivalLoopTests.Database_Has_Items_And_15_To_40_Recipes` L21 | recipe count 15..40 (now 25); ids stone_axe, stone_pick, flint_knife, stone_spear, torch, campfire, water_container, shelter, storage, bedroll, bow, arrows; **every item has an icon**; campfire/shelter/storage/bedroll have `placePrefab` | > 40 recipes; a new item without icon; renaming ids |
| `SurvivalLoopTests.Inventory_Stacks_Weight_NoDuplication` L32 | wood weight 1.0 / stack >= 10; tools go to hotbar slot 0 | changing wood weight or `PrefersHotbar` |
| `SurvivalLoopTests.Crafting_Consumes_Then_Produces_And_Cancel_Refunds` L46 | stone_axe known at start, costs exactly 2 stone, no extra requirement | adding a tool / station requirement to the axe, changing its cost |
| `SurvivalLoopTests.Island_Boots_Player_Tutorial_Nodes` L77 | > 30 ResourceNodes, >= 6 loot, >= 1 `WaterSource.fresh`, > 8 zones, tutorial index 0 | changing `fresh` semantics |
| `SurvivalLoopTests.Gather_Craft_Fire_Cook_Save_Load` L89 | campfire recipe (wood 4 / stone 5); `BuildSystem.Spawn`; **light with 1 wood from the pack via `Interact`**; second `Interact` with raw meat -> `CookingCount == 1`; `PlayerSurvival.HeatAt > 0`; save/load keeps inventory, 1 `PlacedStructure`, `IsLit` | requiring a fire starter / kindling; renaming `CookingCount`; cooking needing a different input; save format break |
| `SurvivalLoopTests.Ocean_Is_Not_Drinkable_Fresh_Water_Is` L127 | `WaterSource.Drink(pi)` raises thirst 40 -> > 60; `OceanShore.PlayerAtShore`; empty-handed `OceanShore.Interact` raises `TriedSaltWater` and lowers thirst | sea interaction defaulting to "fill" without a container; thirstPerDrink < 21 |
| `DinosaurTests.Predator_Chases_And_Bites_Player_Player_Can_Kill_It` L50 | carcass, `Carcass.Hit` x `handCutsPerItem` gives +1 `raw_meat` | renaming raw_meat, changing hand butchering |
| `PlayerControllerTests.Sprint_And_Walk_Speeds` L87 | `MoveSpeedMultiplier == 1` at start stats (H 85, T 70) on `PFB_Player` | a tier that slows movement at thirst 70 |
| `PlayerControllerTests.OneShotAction_Eat...`, `LoopAction_GatherWood...` | action runner / animation | changing `DoOneShot` / `DoLoop` |
| `SceneAuthoringTests.UI_Edited_In_The_Scene...` L20 | HUD `Vitals` panel found by name, row `healthBar` inside | renaming HUD objects |
| `ControlsGuideTests` | controls table == `PlayerInputReader` bindings | adding a key (e.g. Empty) without updating `ControlsPanel` + `CONTROLS.md` |
| `PerformanceTests.Island_Frame_Budget...` | avg frame < 100 ms; reports GC per frame | heavy per-frame work |

### 5.4 Per-frame allocations relevant to M1 (directive: none)

| Where | Allocation |
|---|---|
| `HUDManager.Update` L309-325, L345, L348 | 4 x `int.ToString`, a new `StringBuilder` + `ToString`, clock string, `ObjectiveText()` concat every frame |
| `PlayerInteraction.UpdatePrompt` L169 -> `GetPrompt` | `Campfire` interpolated fuel strings (L128, L132), `WaterSource` `$"{water}/{max}"` + `ToLowerInvariant` (L82, L86), `ResourceNode` / `WorldPickup` concatenations, every frame while a target is shown |
| `BuildSystem.Evaluate` L84, L103, L108 | `LayerMask.GetMask(params)`, `Physics.OverlapBox` array, `sharedMaterials` array, every frame in build mode |
| `GameEvents.Raise` L53 | `GetInvocationList()` per event (bursts on gather / craft) |
| `WorldPickup.Spawn` | `Instantiate` per drop (cooked food, spills); not pooled |

---

## 6. Files to modify for Milestone 1

| File | Change |
|---|---|
| `Items/ItemEnums.cs` | append `enum WaterType { None, SaltWater, DirtyWater, CleanWater }` (ints saved) |
| `Items/ItemDefinition.cs` | append: `fuelSeconds`, `burnSeconds`, `burntResult`, `cookable` flag (or keep `cookedResult != null`), `acceptsWater` (salt / dirty / clean mask) |
| `Items/ItemStack.cs` | add `WaterType waterType`; keep `dirty` as a read-only property (`waterType == DirtyWater`) for existing callers; keep "water > 0 = unique stack" |
| `Survival/PlayerSurvival.cs` | read `SurvivalConfig`; need tiers; `ConsumeItem(ItemDefinition)`, `DrinkWater(WaterType, charges)`, `ApplySleep(hours)`; set `Overweight` from inventory; expose `SickSeconds` for save |
| `Survival/` (new) | `SurvivalConfig.cs` (SO), `WaterRules.cs` (static), `SurvivalEnvironment.cs` (static, moved from GameManager) |
| `World/Campfire.cs` | fuel from item data; cooking slots with state (Raw / Cooking / Ready / Burned) and take-out interaction; boiling as a slot using `WaterRules.Boil`; save / restore slots; no per-frame strings |
| `World/WaterSource.cs`, `World/OceanShore.cs` | fill -> `DirtyWater` / `SaltWater` via `WaterRules`; hand drinking via `WaterRules` (sea: still thirst worse, test-safe) |
| `World/Shelter.cs` | use `warmth`; `WarmthAt` takes the covering shelter's value |
| `World/RainCollector.cs` (new) | placeable `Interactable`: fills with `CleanWater` while `WeatherManager.RainingAt(pos)`; 1 s tick, no alloc; fill container / drink |
| `Player/PlayerInteraction.cs` | `UseActiveConsumable` / `Eat` delegate rules to `PlayerSurvival` + `WaterRules`; add Empty (pour out) |
| `Core/GameManager.cs` | call `SurvivalEnvironment.Hook()`; sleep / respawn / new-game numbers from config; allow sleeping in a tent (Bedroll on tent prefab needs no change) |
| `Core/SaveData.cs`, `Core/SaveSystem.cs` | v4: `SlotData/DropData.waterType`, `StructureData.water` (collector), `StructureData.slots` (campfire contents + state + timer), player `sickSeconds`; migrate v2/v3 (`dirty` -> DirtyWater, clean water -> CleanWater); optional `tutorialStepId` |
| `Core/GameEvents.cs` | append only: e.g. `FoodBurned`, `WaterCollected`, `WaterEmptied`, `NeedTierChanged` |
| `Story/TutorialManager.cs` | boil / rain water steps appended or restored by id (index is saved) |
| `UI/HUDManager.cs` | tier status words, three water colours, cached numeric text (no per-frame strings) |
| `UI/InventoryUI.cs` | water type text, Empty button |
| `UI/ControlsPanel.cs` + `docs/CONTROLS.md` | only if a new key is bound |
| `Editor/PrimalSurvivalBuilder.cs` (new) | additive builder: `SurvivalConfig` asset, fuel / burn values on existing items, new items (tent, rain_collector, burnt_meat, maybe clay_pot later) with borrowed icons, recipes, prefabs, removal / hiding of `RCP_cooked_meat` |
| `Tests/PlayMode/SurvivalM1Tests.cs` (new) | water types, rain collector, boil salt, cook states, tiers, save v4 migration |

## 7. Files NOT to touch

| File(s) | Reason |
|---|---|
| `Editor/PrimalGameplayBuilder.cs` (and never run it) | overwrites item stats and drops items from other builders |
| `Editor/PrimalCraftingBuilder.cs`, `PrimalPhase2Builder.cs`, `PrimalWeaponBuilder.cs`, `PrimalBushBuilder.cs`, `PrimalDinoBuilder.cs`, `PrimalCharacterBuilder*.cs`, `PrimalWorldBuilder.cs` | owned data / other agents; new data goes in the new builder |
| `Items/InventorySystem.cs`, `Items/CraftingSystem.cs`, `Items/RecipeDefinition.cs`, `Items/CraftingRequirement.cs` | stable, tested, reused as is |
| `Player/PlayerMotor.cs` (keep `IStaminaSource`), `PlayerAnimationDriver.cs`, `PlayerIK.cs`, `PlayerHierarchy.cs`, `PlayerClimb.cs`, `Animation/*` | controller / animation, covered by tests, other agents |
| `Player/PlayerCombat.cs`, `Combat/**` | combat agent; only the two routing lines L97-98 may later point to one item-use call |
| `AI/**`, `World/Carcass.cs` | hunting works; persistence is a later milestone |
| `Core/TimeManager.cs`, `Core/WeatherManager.cs`, `Core/GameClock.cs` | complete; read only |
| `World/TreeHarvest.cs`, `ResourceNode.cs`, `BushInteraction.cs`, `FruitCluster.cs`, `WorldPickup.cs`, `LootContainer.cs` | stable gathering |
| `VFX/**`, `Audio/**` | reuse pools (`VfxPool`, `SfxPlayer`) only |

---

## 8. Milestone 1 gap list

| Step | Today | Gap to close |
|---|---|---|
| Spawn with little | empty pack, wreck loot + beach pickups | none (optional: tune loot, e.g. no gourd so the player must craft one) |
| Gather wood / stone / fibre | nodes, trees, bushes, pickups | none |
| Craft axe / knife | stone_axe (known), flint_knife (unlocked by bone), butcher_knife | none; keep the axe recipe unchanged (test) |
| Build + light campfire with fuel | recipe wood 4 / stone 5, ghost placement, light with 1 wood | fuel as item data (`fuelSeconds`: wood 240, fibre small); prompt shows burn time; keep "light with 1 wood" working (test) |
| Cook over time: raw / cooking / ready / burned | timer only; result dropped as pickup; no burn; lost on load | cooking slots with states, visible food on the fire, take out, burn after `burnSeconds`, `burnt_meat` item, save slots; one cooking path |
| Rain collector during rain | none (rain exists) | `RainCollector` placeable + item + recipe + save field |
| Sea water -> boil -> clean water | sea only drinkable (bad); cannot be collected | fill at `OceanShore` -> `SaltWater`; boil at fire -> `CleanWater` (design option: longer boil or fewer charges); drinking salt stays harmful |
| Containers with water types, fill / empty / drink / purify | charges + `dirty` bool; fill / drink / boil; no empty | `WaterType` on the stack, `WaterRules` (fill, empty, drink, boil), Empty action, no mixing types (fill replaces only same type or empty) |
| Hunger / thirst gradual effects | max stamina lerp below 25; damage only at 0 | tiers from config: medium -> stamina regen multiplier, low -> max stamina multiplier, critical -> health drain; HUD words; keep multiplier 1 at start stats (test) |
| Tent + sleep through the night | lean-to (rest / save) and separate bedroll (sleep) | `tent` item + prefab = `Shelter` + `Bedroll` components (reuse); sleep costs from config; optional "sleep only if not cold / not in danger" rule |
| Save / load keeps it | needs, inventory, structures, fuel, lit | v4: water type, fire slots, collector water, sickness; migration of v2/v3 saves |
| Tutorial | wood, stone, craft, water (any drink), food, campfire, cook, shelter, night | append steps: collect rain or fill sea water, boil, drink clean water, build tent, sleep; save step by id |

---

## 9. Recommended architecture delta

### 9.1 New types

| Type | Kind | Purpose |
|---|---|---|
| `WaterType` | enum (append-only ints) | `None, SaltWater, DirtyWater, CleanWater` |
| `SurvivalConfig` | ScriptableObject (`Resources/SurvivalConfig` or referenced by `GameManager`) | every tuning number from section 5 (drain, tiers, temperature, sleep, respawn, new-game stats, sickness durations, shelter / torch warmth); `WaterProfile[]` per `WaterType` (thirst per charge, stamina, sick chance, sick seconds, boil seconds, boil result, HUD colour, event id); `ItemRefs` (raw meat, hide, bone, wood, fiber, burnt food) |
| `NeedTier` | serializable struct inside the config | threshold, stamina regen x, max stamina x, move speed x, health drain /s, warning text |
| `WaterRules` | static class (`Survival/`) | `CanFill(stack, type)`, `Fill`, `Empty`, `Drink(PlayerSurvival, stack)`, `DrinkFromSource(type)`, `Boil(stack)`; the only place water rules live |
| `SurvivalEnvironment` | static class (`Survival/`) | the air / rain / heat hooks now in `GameManager.HookEnvironment`, reading config |
| `RainCollector` | `Interactable` (`World/`) | capacity + fill rate per rain hour from its item / config; `WeatherManager.RainingAt` on a 1 s tick; fill active container or drink; saved via `StructureData.water` |
| `CookingSlot` | small serializable class inside `Campfire` | item, timer, state (Raw, Cooking, Ready, Burned), optional water container; used for food and boiling |
| `PrimalSurvivalBuilder` | editor, additive | creates config + new items / recipes / prefabs, fills fuel / burn data, hides `RCP_cooked_meat`, adds to `ItemDatabase` by merge |

### 9.2 Extended types (no new duplicates)

| Existing | Extension |
|---|---|
| `ItemDefinition` | `fuelSeconds`, `burnSeconds`, `burntResult` (append fields; defaults keep old behaviour) |
| `ItemStack` | `waterType` (+ `dirty` as a compatibility property) |
| `PlayerSurvival` | tiers, `ConsumeItem`, `DrinkWater`, `ApplySleep`, overweight bridge, sickness save; still implements `IStaminaSource` |
| `Campfire` | fuel from data, slots with states, boiling as a slot, save / restore, cached prompt strings |
| `WaterSource` / `OceanShore` | fill with a water type; drinking through `WaterRules` |
| `Shelter` | real `warmth`; tent prefab reuses `Shelter` + `Bedroll` |
| `SaveData` / `SaveSystem` | v4 fields + migration |
| `GameManager` | flow only; numbers and rules from config / `PlayerSurvival` |
| `HUDManager` / `InventoryUI` | tier words, water type colours, Empty, no per-frame strings |
| `GameEventType` | append new events only |

### 9.3 Deliberately not added in M1

`SurvivalManager` MonoBehaviour, separate Hunger / Thirst / Stamina components, a second recipe database, a cooking
recipe database (cooking stays on `ItemDefinition.cookedResult`), `ConstructionSystem` / modular pieces, creature
respawn / carcass persistence, torch fuel, drop-table SO. Each is either covered by an existing class or a later milestone.

---

## 10. Risks

| Risk | Mitigation |
|---|---|
| Save format change (v3 -> v4) corrupts or loses old saves | keep old fields readable; migrate `dirty` on load; bump `CurrentVersion` once; round-trip test |
| Serialized enums reordered | `WaterType`, `GameEventType`, `CraftStation`, `ItemCategory`, `RecipeCategory` append only |
| Tests pinned to current numbers / flows (5.3) | keep axe recipe, 1-wood lighting, `CookingCount`, sea `Interact` behaviour, start-stat speed 1; icons for every new item; stay <= 40 recipes |
| Someone re-runs `PrimalGameplayBuilder` | new builder is additive and idempotent; document the rule in the builder header |
| `ItemStack` change breaks inventory invariants (unique stacks when `water > 0`) | only add a field; `Clone()` must copy it; no change to `InventorySystem` merge logic |
| Tutorial index saved as int | append new steps at the end or save by step id |
| Parallel agents editing `PlayerInteraction`, `PlayerCombat`, `HUDManager` | keep edits small and localized; survival logic goes into `Survival/` files |
| Bootstrap duplicated (`GameManager.Ensure` + `PrimalSceneBaker`) | new world objects are placeables (no new system object needed); if one is added, register it in both |
| Per-frame cost / GC (directive) | 1 s ticks for collectors, cached strings, no LINQ in `Update`, pooled VFX; perf test reports GC |
| Boiling salt water into clean water is a design simplification | make yield / time configurable in `WaterProfile` so the owner can tune it |
| Night has little threat today | tune through config (colder nights, rain chance at night), not new systems |
