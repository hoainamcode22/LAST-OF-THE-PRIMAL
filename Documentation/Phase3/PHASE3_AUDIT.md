# PHASE 3 / 3.5 AUDIT: documentation vs code (2026-09-29)

Read-only audit by the AUDIT agent. No code was changed. Source of truth for C#: the cloud mirror `src/Scripts`,
`src/Tests/PlayMode`, `src/Shaders`. Scene and asset data are not in the mirror: items that depend on them say
**NEEDS SCENE CHECK** and name what to check. Line numbers are mirror line numbers on 2026-09-29.

Status words:
- **IMPLEMENTED**: the directive item works in code (small polish gaps are listed).
- **PARTIAL**: a real part exists, a named part is missing.
- **NOT IMPLEMENTED**: no code for it (design only counts as not implemented).
- **BROKEN**: code exists and does the wrong thing.
- **NEEDS SCENE CHECK**: depends on scene / asset content the mirror does not hold.

Facts taken as given (from the Lead): PlayMode suite 68 pass before the water-reach fix (the 2 new `WaterReachTests`
were not re-counted here); Phase A animation fixes are in; the pond / stream / sea scan cull is fixed with
`Interactable.LargeArea`; perception is design only; no bare-hand attack; building has a ghost for items with
`placePrefab` and no foundation / wall / roof / door pieces.

## 0. Summary

| Status | Phase 3 (114 rows) | Phase 3.5 (22 rows) | Total |
|---|---|---|---|
| IMPLEMENTED | 55 | 3 | 58 |
| PARTIAL | 44 | 10 | 54 |
| NOT IMPLEMENTED | 12 | 6 | 18 |
| BROKEN | 2 | 0 | 2 |
| NEEDS SCENE CHECK | 1 | 3 | 4 |

The two BROKEN rows (16e, 46) share one root cause: `FruitCluster.Harvest` reads `InventorySystem.Add`'s return value
the wrong way round. The survival core (needs, water types, cooking, fire fuel, save of structures) is solid; the gaps
are in the Phase 3 breadth (status effects, spoilage, building pieces, fire fear, perception) and all of Phase 3.5 combat.

## 1. Phase 3 items

Grouped directive ranges whose topics do not map one-to-one onto numbers use letters (4-5a, 21-26c ...).

### 1.1 Survival stats (4-5)
| # | Item | Status | Evidence | Gap |
|---|---|---|---|---|
| 4-5a | Health | IMPLEMENTED | `Player/PlayerHealth.cs` TakeDamage / ApplyRaw / Heal / Revive; HUD bar | none |
| 4-5b | Hunger | IMPLEMENTED | `Survival/PlayerSurvival.cs:267`, tiers `SurvivalConfig.cs:55`; `SurvivalNeedsTests` | none |
| 4-5c | Thirst | IMPLEMENTED | `PlayerSurvival.cs:268` (hot air x1.3, sick x1.6), `SurvivalConfig.thirstTiers` | none |
| 4-5d | Stamina | IMPLEMENTED | `PlayerSurvival.cs:140-151` (IStaminaSource), regen `:273`; sprint, jump, dodge, attacks, climb, block pay it | none |
| 4-5e | Temperature | IMPLEMENTED | `PlayerSurvival.cs:281-288`, `Survival/SurvivalEnvironment.cs` AirAt / HeatAt | cold side only; no overheating |
| 4-5f | Wetness | IMPLEMENTED | `PlayerSurvival.cs:280` (rain in, fire / time out), feeds temperature; `PlayerWetLook` | only rain wets; wading / swimming does not |
| 4-5g | Bleeding | PARTIAL | `PlayerHealth.cs:13` timer from `DinosaurDefinition.bleedSeconds`, 1.2 HP/s | no severity; `StopBleeding` (`:75`) has no caller |
| 4-5h | Injury | NOT IMPLEMENTED | only a comment in `PlayerMotor.cs:10` | no injury / limp / fracture state |
| 4-5i | Food poisoning | IMPLEMENTED | `PlayerSurvival.MakeSick` `:202` (Stomach_Sick): raw meat 35 %, burnt meat 10 %, dirty water 20 % | one generic "sick" state |
| 4-5j | Modular status effects | NOT IMPLEMENTED | ad-hoc fields (SickSeconds, _bleedUntil, IsCold, Overweight) + HUD bitmask `UI/HUDManager.cs:340` | no StatusEffect data, stacking or duration framework |

### 1.2 Water (6-11)
| # | Item | Status | Evidence | Gap |
|---|---|---|---|---|
| 6 | Ocean / fresh / dirty / clean | IMPLEMENTED | `Items/ItemEnums.cs` WaterType {None, SaltWater, DirtyWater, CleanWater}; `WaterSource.SourceType` (spring `clean` flag) | none |
| 7 | River / pond / pool sources, drink + fill | IMPLEMENTED | `World/WaterSource.cs:94-141`, `World/OceanShore.cs`, `World/RainCollector.cs`; scan fix `Interactable.cs:59`, `PlayerInteraction.cs:166`; `WaterReachTests` | no separate pool type; see scene check S4 |
| 8 | Container capacity / amount / quality | IMPLEMENTED | `ItemDefinition.waterCharges`, `ItemStack.water / waterType`, `Survival/WaterRules.cs` CanFill (no mixing); leaf cup 1, gourd 3, waterskin 5 | none |
| 9 | Boil at the campfire with steam / bubbles / sound | PARTIAL | `World/Campfire.cs:189` TryBoil, `:265` CompleteBoil; steam only when done `VFX/CampfireFx.cs:63`, sizzle `:92` | nothing plays while it boils: `VfxId.BoilBubbles`, `SfxId.WaterBoil` registered, never called |
| 10 | Drink feedback "+ Hydration" | PARTIAL | `Player/PlayerFeedback.cs:75` splash, drops, drink sound; thirst bar | no "+N Hydration" note (`WaterRules.ApplyDrink` `:66` raises none); `SfxId.WaterFill` unused |
| 11 | Dirty-water sickness configurable in a ScriptableObject | IMPLEMENTED | `SurvivalConfig.water[]` `:71` (sickChance, sickSeconds, boil numbers), `handDrinkThirst` `:77` | none |

### 1.3 Food (12-15)
| # | Item | Status | Evidence | Gap |
|---|---|---|---|---|
| 12 | Raw / cooked / spoiled | PARTIAL | `Campfire.cs:33` CookState {Empty, Raw, Cooking, Ready, Burned}; `burnt_meat` | no spoiled state |
| 13 | Berries, fruit, edible plant, raw / cooked meat, fish / cooked fish | PARTIAL | berries, raw / cooked meat `Editor/PrimalGameplayBuilder.cs:224-227`; fruit `PrimalPhase2Builder`; fruit_mash, burnt_meat | no edible plant, no fish / cooked fish, no fishing |
| 14 | Spoilage fresh -> aging -> spoiled | NOT IMPLEMENTED | no freshness field on `ItemDefinition` / `ItemStack` / `SlotData` | needs per-stack timer + save field |
| 15 | Cooking at the fire | IMPLEMENTED | `Campfire.cs:177-307` slots, burn timer, saved `:626`; `SurvivalM1Tests`, loop test | food on the fire hard to read at 7 m (Survival QA known issue 2) |

### 1.4 Gathering, tools, crafting (16-20)
| # | Item | Status | Evidence | Gap |
|---|---|---|---|---|
| 16a | Wood | IMPLEMENTED | Driftwood node `PrimalGameplayBuilder.cs:403`; `World/TreeHarvest.cs` (Chop tool) | no branch / log node types (scene check S2) |
| 16b | Stone | IMPLEMENTED | Loose stones `:404`, Rock (pick) `:407-411`, cave stones `:476-489` | yields: see R2 / R3 |
| 16c | Fiber | IMPLEMENTED | Fibrous plant `:405`; 25 % fibre from trees | none |
| 16d | Berries | IMPLEMENTED | berry ResourceNode (builder `:406`, `PrimalBushBuilder.cs:474`) | empty bush looks full (see 72) |
| 16e | Fruit | BROKEN | `World/FruitCluster.cs:34-35` | same bug as 46 |
| 16f | Hide / bone / meat | IMPLEMENTED | `World/Carcass.cs` Cut (knife full yield, hands half the meat); wreck meat / bones | only from kills (hunting not in the loop test) |
| 16g | Water | IMPLEMENTED | see 7 | none |
| 16h | Animation / sound / VFX / feedback | IMPLEMENTED | `PlayerInteraction.DoLoop` on OnGatherHit; `PlayerFeedback.Gather` `:102`; node wobble + shrink; HUD "+N item" `HUDManager.cs:257` | none |
| 16i | One ResourceNode / IInteractable design | PARTIAL | `IInteractable` shared (`Interactable.cs:12`) | five gather implementations (ResourceNode, TreeHarvest, Carcass, WaterSource, FruitCluster which is not an Interactable); yields are component fields, no shared data |
| 17 | Tool durability / efficiency / break feedback | PARTIAL | `InventorySystem.WearActive` `:232`; toolPower `ResourceNode.cs:98`, `TreeHarvest.cs:126`; "X broke!" `ResourceNode.cs:103` | break = text only (`SfxId.ToolBreak` unused, no VFX); efficiency scales yield, not speed |
| 18 | Crafting categories | IMPLEMENTED | `RecipeCategory` 7 values + ALL tab (Tools, Weapons, Survival, Food, Structures, Water, Resources) | none |
| 19a | 20-30 recipes | IMPLEMENTED | 27 recipes (Survival `QA_REPORT.md` 1): gameplay 16 + crafting 9 + survival 3 - retired cooked_meat | none |
| 19b | Required recipe list | PARTIAL | present: stone axe, stone pick, flint knife (stone knife), stone spear (thrown by aim + attack), bow, arrow, campfire, torch, water container (gourd / leaf cup / waterskin), bedroll, storage, shelter (+ tent) | missing foundation, wall, roof, door, cooked fish; cooked meat is fire-only (`PrimalSurvivalBuilder.cs:427` retired the recipe) |
| 20 | Crafting feedback | IMPLEMENTED | HUD "Crafted: X" + sound, Craft clip with dust / sparks (`PlayerFeedback.cs:67`), queue progress, "Missing: ..." reasons (`CraftingSystem.Check`) | first-learn hitch (top gap 12) |

### 1.5 Campfire (21-26)
| # | Item | Status | Evidence | Gap |
|---|---|---|---|---|
| 21-26a | Fuel | IMPLEMENTED | `Campfire.cs:114` FuelSecondsOf, TryLight `:134`, TryAddFuel `:146`, `ItemDefinition.fuelSeconds` | none |
| 21-26b | Intensity | NOT IMPLEMENTED | only a visual light level (`CampfireFx._level`) | heat / light / cook speed do not follow fuel |
| 21-26c | States Unlit / Lighting / Burning / LowFuel / Extinguished | PARTIAL | `bool IsLit` `:66`, SetLit `:309` | no Lighting phase (instant), no LowFuel warning / look; extinguished = unlit |
| 21-26d | Functions light / fuel / cook / boil / warm / rest | PARTIAL | light (CampfireFx), cook, boil, warm (`Campfire.HeatAt` `:100`) | no rest / sit / save at the fire |
| 21-26e | Configurable fire fear per species | NOT IMPLEMENTED | no field in `AI/DinosaurDefinition.cs`; AI never reads `Campfire` | design only (`AI/PERCEPTION_DESIGN.md` 6.1, 7) |
| 21-26f | Wildlife reactions to fire | NOT IMPLEMENTED | as above | as above |
| 21-26g | Rain weakens / extinguishes; sheltered fire protected | PARTIAL | fuel x2 in rain `Campfire.cs:238-239`; `WeatherManager.RainingAt` `:149` checks `Shelter.Covers` | rain / storm never puts the fire out; `SfxId.FireHiss` unused |
| 21-26h | Fuel drain rate | IMPLEMENTED | `Campfire.cs:239`, `SurvivalConfig.maxFuelSeconds / legacyFuelSeconds` | none |

### 1.6 Building (27-29)
| # | Item | Status | Evidence | Gap |
|---|---|---|---|---|
| 27-29a | Foundation / wall / roof / door / storage / bedroll / campfire | PARTIAL | placeables: campfire, shelter, storage, bedroll, tent, rain collector | no foundation / wall / roof / door, no snapping (`Building/BuildSystem.cs:13` "No modular base building") |
| 27-29b | Ghost green / red, validation ground / slope / collision / distance / resources | IMPLEMENTED | `BuildSystem.cs:81-108` (ground hit, slope 24 deg, water, too close, overlap); item consumed on build `:119` | none for single placeables |
| 27-29c | Build VFX | PARTIAL | OnBuildHit chips + sound `PlayerFeedback.cs:81` during the 2-hit loop `BuildSystem.cs:116` | no puff when the structure appears (`VfxId.DustImpact` unused) |
| 27-29d | Shelter purpose rain / sun / temperature / sleep / save / respawn | PARTIAL | cover `Shelter.cs:31`, warmth `:18`, tent Bedroll sleep, rest = save + respawn `:70` | no sun / heat protection |

### 1.7 Weather, day / night, shaders (30-36)
| # | Item | Status | Evidence | Gap |
|---|---|---|---|---|
| 30 | Clear / cloudy / rain / storm, controlled duration | PARTIAL | `Core/WeatherManager.cs:7`, cloudHours / rainHours, `SetWeather(hours)` | storm never in the random schedule (`:90-91`); a loaded storm becomes rain |
| 31 | Day phases | PARTIAL | `Core/TimeManager.cs:28` IsNight, Daylight01, NightFactor `:119`, Night / DayStarted | no named phases (dawn, morning, noon, afternoon, dusk, night) or phase events |
| 32 | Effects on temperature / wetness / visibility / fire / wildlife / sound / water | PARTIAL | overcast -5 C, rain wets, fire x2, ambience + thunder, rain rings, collector | wildlife ignores weather; visibility = fog only |
| 33 | Sunlight atmosphere | IMPLEMENTED | `TimeManager.Apply` (sun colour / angle, trilight, fog, sky exposure, overcast), `VFX/NightSky.cs` | none |
| 34 | Foliage wind | IMPLEMENTED | `Shaders/PF_FoliageWind.shader`, `PF_Wind.hlsl`, `_PF_Wind`; `WindTests` | none |
| 35 | Water shader | IMPLEMENTED | `PF_Ocean`, `PF_OceanShore`, `PF_WaterFlow`, `VFX/WaterGlobals.cs` | visual sign-off only in captures |
| 36 | Wet surfaces via shared shader parameters | PARTIAL | `_PF_Wetness` set `WeatherManager.cs:113`, read by PF_Foliage / PF_FoliageWind / PF_Water / PF_WaterCommon; `PlayerWetLook` | terrain, rocks, props, structures (URP Lit) never look wet |

### 1.8 Volcano (37-38)
| # | Item | Status | Evidence | Gap |
|---|---|---|---|---|
| 37-38a | Ash, smoke, heat haze, embers, lava cracks / flow | IMPLEMENTED | `World/VolcanoLandmark.cs` BuildAsh `:83`, BuildHaze `:169` (needs hazeMaterial), smoke drift, embers, lava emission, flow glows | no crack detail; not play-tested |
| 37-38b | Danger zone warnings | NOT IMPLEMENTED | volcano is unreachable scenery | no zone, heat damage or warning |

### 1.9 Injury and healing (39-42)
| # | Item | Status | Evidence | Gap |
|---|---|---|---|---|
| 39 | Injury | NOT IMPLEMENTED | see 4-5h | no injury system |
| 40 | Bleeding | PARTIAL | `PlayerHealth.cs:13`, Bleed VFX `PlayerFeedback.OnBleeding`, HUD word | only from creature hits; no levels; no cure in play |
| 41 | Bandage | PARTIAL | `ITEM_bandage` health 25 `Editor/PrimalCraftingBuilder.cs:127`, recipe `:197` | used through Eat (`PlayerInteraction.cs:310`: eat clip, `Ate` event); never calls StopBleeding; `SfxId.BandageWrap` unused |
| 42 | Healing | PARTIAL | regen `PlayerSurvival.cs:291`, sleep heal, food health, `DamageOverlay.ShowHeal` | no heal-over-time item; `VfxId.Heal` unused |

### 1.10 Combat, climbing, fruit (43-46)
| # | Item | Status | Evidence | Gap |
|---|---|---|---|---|
| 43 | Spear / knife / bow light / heavy / throw, animation events | PARTIAL | `Combat/Weapons/WeaponData.cs` chains from `PrimalWeaponBuilder.cs:68-175`: spear 2 + heavy + throw, sword 3 + heavy, bow draw; windows from OnAttackActive / End or fallback | knife and tools have no heavy; throw is a PlayerCombat special case |
| 44 | Dodge with i-frames | IMPLEMENTED | `Player/PlayerCombat.cs:130` TryDodge, `:149` InvulnerableUntil, `PlayerHealth.Evaded` | none |
| 45 | Climbing | IMPLEMENTED | `World/Climbable.cs`, `Player/PlayerClimb.cs` (stamina, drop on hit) | fruit trees only (scene check S6) |
| 46 | Fruit trees | BROKEN | `FruitCluster.cs:34-35` | see top gap 1 |

### 1.11 Inventory (47)
| # | Item | Status | Evidence | Gap |
|---|---|---|---|---|
| 47a | Stacking | IMPLEMENTED | `Items/InventorySystem.cs` Add / Move / Split / QuickTransfer; `SurvivalLoopTests` | none |
| 47b | Weight, capacity | IMPLEMENTED | 45 kg, 32 slots, overweight slows (`PlayerSurvival.overweightSpeed`), weight bar | none |
| 47c | Durability | IMPLEMENTED | `ItemStack.durability`, slot bar, item text | none |
| 47d | Spoilage | NOT IMPLEMENTED | see 14 | none built |
| 47e | Water amount | IMPLEMENTED | `ItemStack.water / waterType`, hotbar colour | none |
| 47f | Equipment | PARTIAL | `Player/PlayerEquipment.cs` hand model, back / hip carry, torch | no equipment slots (clothing, armour, bag) |
| 47g | Hotbar 1-8 | IMPLEMENTED | hotbarSize 8, `PlayerInteraction.cs:109-110` | none |
| 47h | Quick use | IMPLEMENTED | attack on food / water `PlayerCombat.cs:124`, inventory Use / double click | none |

### 1.12 HUD, minimap, journal (48-51)
| # | Item | Status | Evidence | Gap |
|---|---|---|---|---|
| 48-51a | Stats top-left | IMPLEMENTED | HUDManager vitals: health, hunger, thirst, stamina, temperature + tier words | none |
| 48-51b | Wetness / bleeding icons | PARTIAL | `HUDManager.cs:340` status words (Bleeding, Stomach sick, Wet, Cold, Overburdened) | words, not icons; no wetness bar |
| 48-51c | Minimap top-right, discovered landmarks only | PARTIAL | `UI/Minimap.cs` baked picture + markers, visited zones | full island picture from the start (`:65`); fresh water always marked (`:132`); new HashSet every frame (`:134`) |
| 48-51d | Compass | IMPLEMENTED | `HUDManager.cs:118` strip + objective marker | code default overlaps the minimap corner (scene check S7) |
| 48-51e | Journal sections + creature pages | PARTIAL | `Story/JournalSystem.cs:10` {Survival, Creatures, Crafting, World}; 7 species pages + predator sign | no Resources section |

### 1.13 Perception and wildlife (52-60)
| # | Item | Status | Evidence | Gap |
|---|---|---|---|---|
| 52-58a | Vision | PARTIAL | `AI/DinosaurController.cs:106` range, FOV, line of sight, crouch x0.6, night x0.6 | binary; no light / torch / cover / awareness build-up |
| 52-58b | Hearing | PARTIAL | `DinosaurController.cs:112` player gait only | no world noises (chop, fall, fight, bushes) |
| 52-58c | Scent | NOT IMPLEMENTED | design `PERCEPTION_DESIGN.md` 4.2 | no code |
| 52-58d | Predator responses | PARTIAL | FSM Observe / Alert / Investigate / Chase / Attack / Return, attack tell, herd flee | chase is omniscient; no memory / awareness meter |
| 52-58e | Campfire integration | NOT IMPLEMENTED | none | no fire fear, cook scent or light term |
| 59 | Dinosaur life | PARTIAL | idle / wander / eat / rest `DinosaurController.cs:189`, herds, calls | no needs / schedule; `DinoState.Drink` never entered; no night rest |
| 60 | Dinosaur animation | IMPLEMENTED | `AI/DinoLife.cs` head look, attack tell, eyes; Animator LOD | not play-tested by hand |

### 1.14 Player animation, interaction, save, audio, VFX (61-73)
| # | Item | Status | Evidence | Gap |
|---|---|---|---|---|
| 61 | Player animation polish | PARTIAL | `Character/_unity_phaseA_report.md` A1-A5, IK-2 met | A1 arm bind 3.1 deg, A3 first-hit hitch; 22 Phase C clips (start / stop / pivot, punches, gather enter / exit, bow, collect water, butcher) wired but missing |
| 62 | Foot IK | IMPLEMENTED | `Player/PlayerIK.cs:26, :104` (IK-1 met: pelvis dip 0.02-0.03 m) | none |
| 63 | Interaction system | IMPLEMENTED | IInteractable / Interactable; scan + DoOneShot / DoLoop on animation events with timer fallback | water priority -1 / -2 can lose to a nearby node (scene check S4) |
| 64 | Prompts | IMPLEMENTED | prompt + sub line, greyed when CanInteract is false, hold progress, `UI/ContextHints.cs`, touch label | none |
| 65-67a | Save: structures | IMPLEMENTED | `PlacedStructure`, `StructureData`, `ISaveableStructure` (campfire slots, collector water) | none |
| 65-67b | Save: full state | PARTIAL | `Core/SaveSystem.cs` Capture: player, inventory, recipes, queue, time, weather, journal, tutorial, tips, nodes, trees, pickups, loot, structures, drops | creatures, carcasses, fruit clusters, bleeding, tree hit counts, weather timer not saved; load respawns every creature (`GameManager.cs:279`) |
| 65-67c | Save: versioning | IMPLEMENTED | `SaveData.CurrentVersion = 4` (`SaveData.cs:17`), migrations, newer refused `SaveSystem.cs:60` | none |
| 65-67d | Corrupted-save fallback | PARTIAL | bad JSON -> null + LastError `SaveSystem.cs:63`; "Load failed" note | no backup to fall back to; old file deleted before the temp is moved (`:44`); one slot |
| 68 | Audio player / environment / wildlife | IMPLEMENTED | `Audio/SfxPlayer.cs`, `Core/AmbienceManager.cs`, dino calls / roars / steps | 11 Phase 3 SfxIds have no clips yet |
| 69 | Surface footsteps | IMPLEMENTED | `VFX/SurfaceDetector.cs`, `PlayerFeedback.cs:96-97` | grass plays dirt, wood plays rock |
| 70 | VFX library list | PARTIAL | `VFX/VfxPool.cs` VfxId (31), VfxLibrary | 5 Phase 3 ids (PunchImpactSmall / Heavy, DustImpact, Heal, BoilBubbles) have no prefab and no caller |
| 71 | Shader library | IMPLEMENTED | 12 files in `src/Shaders` | none |
| 72 | Resource visual states | PARTIAL | shrink by Remaining (`ResourceNode.cs:32`), wobble, hide + dust when empty (`:111`); felled trees hidden | no damaged mesh; berry bushes unchanged when empty; no stumps |
| 73 | World discovery | PARTIAL | `World/ZoneManager.cs` visits, journal on ZoneEntered / CreatureSighted / Discovery, examinables | minimap shows the whole island from the start |

### 1.15 Onboarding, difficulty, mobile, architecture, tests (74-92)
| # | Item | Status | Evidence | Gap |
|---|---|---|---|---|
| 74-78a | Tutorial | IMPLEMENTED | `Story/TutorialManager.cs:87-119` (25 steps, saved by id), compass / map target | one fixed chain (rework is wave 2) |
| 74-78b | Opening | IMPLEMENTED | `Story/IntroSequence.cs` storm, wreck, wake-up, DAY ZERO, skippable | none |
| 74-78c | First day | IMPLEMENTED | tutorial chain; `SurvivalM1LoopTest` spawn -> night | never played by hand |
| 74-78d | First night | PARTIAL | night air -4 C, "Shelter before night" / "Sleep" steps, roar cue at "return" | no night threat (predator visit, fire fear) |
| 79 | Difficulty | NOT IMPLEMENTED | no setting or scaling anywhere | none built |
| 80 | Mobile controls | IMPLEMENTED | `UI/MobileHUD.cs` stick, look, ATTACK (hold = heavy), JUMP, DODGE, contextual USE, AIM, RUN, CROUCH, safe area | never tested on a device |
| 81 | Mobile inventory | PARTIAL | BAG / CRAFT open `InventoryUI`; slots take pointer taps / drags | not laid out or tested for touch |
| 82 | Mobile building | IMPLEMENTED | BUILD / ROTATE / CANCEL `MobileHUD.cs:55-56, 192-195` | not device-tested |
| 83 | Mobile performance | PARTIAL | Low preset on phones (`Core/GameSettings.cs`), caps, AI LOD, baked minimap | no phone build / profile; 101 ms first-gather hitch; per-frame allocations (Minimap `:134`, build ghost `sharedMaterials`, `GameEvents.Raise` GetInvocationList) |
| 84 | AI tiers | IMPLEMENTED | `DinosaurController.cs:127` LOD 0 / 1 / 2, think 0.2 / 0.6 s, Animator off far, DinoLife 70 m | none |
| 85 | VFX limits | IMPLEMENTED | `VfxPool.cs:59, :85` MaxActive 48 and preset cap 24 / 40 / 64 / 96; far puffs skipped on Low | none |
| 86 | Modular systems | PARTIAL | GameEvents bus, IInteractable, ISaveableStructure, IStaminaSource, static WaterRules | no status-effect module; PlayerSurvival holds needs + temperature + wetness + sickness; two melee paths |
| 87 | ScriptableObjects | PARTIAL | see section 3 | no ResourceDefinition, GatherToolDefinition, PerceptionConfig, status-effect or fire config assets |
| 88 | GameStateManager states | PARTIAL | `GameManager.cs:15` {Boot, Title, Intro, Playing, Dead, Sleeping}; `UIManager.UIScreen`; `PlayerState.PlayerMode` | no Paused / Loading / Cutscene / Building game state in one place |
| 89 | Scene hierarchy | NEEDS SCENE CHECK | `PRIMAL_FRONTIER_ARCHITECTURE.md` 1 (roots mapped, not renamed on purpose) | check S8 |
| 90 | Test scenes | PARTIAL | `Player/TestEnvironment.cs` greybox built in code; island scene; `TestScenes` empty-scene helper | no saved per-system test scenes (survival, combat, AI, building) |
| 92 | Automated end-to-end test | PARTIAL | `SurvivalM1LoopTest.Milestone1_Spawn_To_Night_And_Save` | shortcuts (items added, teleports, meat given, structures spawned without the ghost); no hunt, combat, fruit or placement |

## 2. Phase 3.5 items

| # | Item | Status | Evidence | Gap |
|---|---|---|---|---|
| B1 | Punch combo 1-2-3 | NOT IMPLEMENTED | `PlayerCombat.cs:123` returns with no item; ids PunchL / PunchR / PunchHeavy / Kick 50-53 `Animation/AnimParams.cs:66` | no input branch, no clips (Phase A report 4) |
| B2 | Heavy on hold | NOT IMPLEMENTED | as B1 | as B1 |
| B3 | Animation-event hit timing | PARTIAL | controller states with default OnAttackStart / Hit / End for punches (Phase A report 4) | animator side only: no clips, no hand / foot hitbox, no listener |
| B4 | Configurable damage / stamina / speed / range / knockback | NOT IMPLEMENTED | no unarmed `WeaponData`; `Combat/IDamageable.cs:6` HitInfo has no knockback | no knockback anywhere |
| B5 | Weak vs large dinosaurs | NOT IMPLEMENTED | only HitZone multipliers | no size / mass rule |
| B6 | Punch VFX / audio | NOT IMPLEMENTED | ids registered `SfxPlayer.cs:21`, `VfxPool.cs:17` | no clips, prefabs or callers |
| B7 | Single combat state | PARTIAL | `PlayerState` Combat mode, `PlayerCombat.InCombat`, `WeaponController` | two melee paths (WeaponController and legacy `PlayerCombat.Melee` / `ResolveMelee`), no unarmed branch |
| R1 | Decoration vs gameplay nodes | NEEDS SCENE CHECK | builder makes nodes only from `RES_*` and `ENV_Rock_Medium / Large` under `World/Resources` / `World/Rocks` (`PrimalGameplayBuilder.cs:398-412`) | check S1 |
| R2 | Stone small / medium / large with yields | PARTIAL | Loose stones 1 per hit x4 (`:404`); Rock (medium = large) 2 per hit x5, pick needed (`:407-411`) | no small / large split; small rocks not gatherable |
| R3 | Hands 1 / pick 3-5 per action | PARTIAL | hands 1; pick on loose stones = max(n+1, round(2n x toolPower)) = 2 (`ResourceNode.cs:98`); Rock 2 | pick gives 2, not 3-5 |
| R4 | Branches / logs / driftwood; tree needs an axe | PARTIAL | driftwood by hand 1 (axe 2); trees need a Chop tool (`TreeHarvest.cs:125`) | hand stone also chops; no branch / log nodes (S2) |
| R5 | Fiber plants 1-4 | PARTIAL | 2 per hit by hand, 4 with a knife, 3 charges (`:405`) | fixed values, no 1-4 range |
| R6 | Early density near the beach | NEEDS SCENE CHECK | 4 beach pickups by the spawn (`:471`) + scene RES_ nodes | check S3 |
| R7 | Clusters | NEEDS SCENE CHECK | `RESAREA_*` markers (`Minimap.cs:45`) | check S3 |
| R8 | Regrow / respawn | IMPLEMENTED | regrowHours on GameClock (`ResourceNode.cs:26, :111`), trees 72 h, fruit 30 h; nodes / trees saved | fruit timers not saved |
| R9 | "+3 Stone" notifications | IMPLEMENTED | `HUDManager.cs:251-257` "+N Name" with icon, merged within 3 s | none |
| R10 | Visual states full / damaged / depleted | PARTIAL | see 72 | no damaged state |
| R11 | Capacity | IMPLEMENTED | charges / Remaining, hitsPerTree, carcass counts; full / too heavy stops the loop | none |
| R12 | ResourceDefinition / GatherToolDefinition ScriptableObjects | NOT IMPLEMENTED | yields on the `ResourceNode` component; tool data = `ItemDefinition.tool / toolPower` | none built |
| R13 | Tool efficiency | PARTIAL | toolPower scales yield | no speed effect, no per-resource table |
| R14 | Mobile GATHER / DRINK / FILL / HARVEST / CLIMB | PARTIAL | `MobileHUD.cs:190` labels DRINK / FILL / CLIMB / PICK / USE | no GATHER or HARVEST label (gathering shows USE) |
| R15 | Touch attack + heavy | PARTIAL | ATTACK tap / hold `MobileHUD.cs:51` works with weapons | empty hands do nothing (no punch) |

## 3. Existing classes and ScriptableObjects (do not create duplicates)

| Directive system | Existing class(es) that already own the job |
|---|---|
| SurvivalSystem | `Survival/PlayerSurvival` (needs, stamina, temperature, wetness, sickness), `SurvivalEnvironment` (air / rain / heat), `SurvivalConfig` |
| StatusEffectSystem | none as a system; today: `PlayerSurvival.SickSeconds / MakeSick`, `PlayerHealth` bleeding, `IsCold / IsFreezing`, `Overweight`, HUD status mask. Build it by extending these, not beside them |
| WaterSystem | `Survival/WaterRules` (static rules), `World/WaterSource`, `World/OceanShore`, `World/RainCollector`, `SurvivalConfig.water` |
| FoodSystem | `ItemDefinition` food fields, `PlayerSurvival.ConsumeItem`, `PlayerInteraction.Eat / UseActiveConsumable` |
| CookingSystem | `World/Campfire` cook slots (+ `VFX/CampfireFx`) |
| FireSystem | `World/Campfire` (fuel, lit, heat, rain), `CampfireFx`, torch in `Player/PlayerEquipment` + `SurvivalEnvironment.HeatAt` |
| ShelterSystem | `World/Shelter`, `World/Bedroll`, `GameManager.Sleep` |
| BuildingSystem | `Building/BuildSystem`, `PlacedStructure`, `ISaveableStructure` |
| ResourceSystem | `World/ResourceNode`, `TreeHarvest`, `FruitCluster`, `Carcass`, `WorldPickup`, `LootContainer`, `BushInteraction` (berry variant) |
| ClimbingSystem | `Player/PlayerClimb`, `World/Climbable`, `PlayerIK.Climb` |
| ScentSystem | none; designed as the scent ring of `Core/Stimuli` + `AI/DinoSenses` (PERCEPTION_DESIGN 3) |
| PerceptionSystem | inline `DinosaurController.CanSee / CanHear`; designed `DinoSenses`, `PlayerSignature`, `Stimuli`, `PerceptionConfig` (none exist) |
| WildlifeSystem | `AI/DinosaurController`, `AmbientCreature`, `DinosaurSpawner`, `DinoLife`, `DinosaurDefinition` |
| TimeSystem | `Core/TimeManager`, `Core/GameClock` |
| WeatherSystem | `Core/WeatherManager` (+ `VFX/WaterGlobals`, `Core/AmbienceManager`) |
| GameStateManager | `Core/GameManager` (GameState) + `UI/UIManager` (UIScreen) + `Player/PlayerState` (PlayerMode) |
| Also present | `InventorySystem`, `CraftingSystem`, `ItemDatabase`, `JournalSystem`, `TutorialManager`, `ZoneManager`, `SaveSystem`, `GameEvents`, `VfxPool`, `SfxPlayer`, `HUDManager`, `Minimap`, `MobileHUD`, `PlayerCombat` + `WeaponController` / `MeleeWeapon` / `RangedWeapon`, `PlayerHealth` |

**ScriptableObjects today:** `SurvivalConfig` (Resources/SurvivalConfig; nested `NeedTier`, `WaterProfile`), `ItemDefinition`
(ITEM_*), `RecipeDefinition` (RCP_*; nested `Ingredient`, `CraftingRequirement`, `CraftingResult`), `ItemDatabase`
(Resources/ItemDatabase), `WeaponData` (WPN_stone_spear, flint_knife, stone_axe, stone_pick, stone_hammer, hand_stone,
torch, bow, flint_sword; nested `AttackProfile`), `DinosaurDefinition` (DINO_*), `MinimapData`, `VfxLibrary`,
`SfxLibrary`, `BloodLibrary`. Not present: ResourceDefinition, GatherToolDefinition, PerceptionConfig, any status-effect
or fire config asset. An unarmed attack can be a `WeaponData` asset (needs a knockback field) rather than a new type.

## 4. Doc drift (docs claim something the code does not do, or is stale)

| Doc | Claim | Code |
|---|---|---|
| `PRIMAL_FRONTIER_ARCHITECTURE.md` 6 | save "version 2" | `SaveData.CurrentVersion = 4` |
| `PRIMAL_FRONTIER_ARCHITECTURE.md` 2 | shaders: PF_Water, PF_LandmarkLit, PF_ParticlesAdditiveNoFog, PF_Foliage (not assigned) | 12 shader files incl. PF_Ocean, PF_OceanShore, PF_WaterFlow, PF_FoliageWind (in use, WindTests), PF_HeatShimmer, PF_NightSky |
| `PRIMAL_FRONTIER_PHASE2_STATUS.md` | foliage wind FAIL; heat haze / ash NOT COMPLETED; stars NOT COMPLETED; save v2 | all four done since (`PF_FoliageWind`, `VolcanoLandmark` ash / haze, `NightSky`, v4) |
| `PRIMAL_FRONTIER_VFX_SHADER_GUIDE.md` | PF/Water on the sea, pond on URP Lit; heat haze and ash "not done"; "Steam (boiling water)" | sea / shore / stream / pond on PF_Ocean / PF_OceanShore / PF_WaterFlow; haze and ash built; steam shows only after boiling |
| `PRIMAL_FRONTIER_SYSTEMS.md` Survival | "put dirty water in your pack near a lit campfire and use Boil water, about 8 s" | container goes on a fire slot (`Campfire.TryBoil`); salt water boils too (20 s, -1 charge) |
| `PRIMAL_FRONTIER_SYSTEMS.md` Map and HUD | "Compass moved to the top centre" | code default is top-right, same anchor as the minimap (`HUDManager.cs:118`); scene layout decides (S7) |
| `README.md` | "Boil water" next to the fire; cooked meat in the crafting list; 19-step tutorial; injury ("chấn thương") as a stat; sea shader "PF/Water" | fire slot boiling; cooked-meat recipe retired; 25 steps; no injury system; PF_Ocean |
| `Survival/FOOD_SYSTEM.md` | berries +8 hunger; fruit +12 hunger / +4 thirst | berries 7 hunger + 3 thirst; fruit 16 / 8 / +5 stamina (SYSTEMS doc has the code values) |
| `Survival/CRAFTING_PROGRESSION.md` | tent unlocked by the first campfire; rain collector by first rain; stone axe "stone 2" | tent `knownAtStart = true` (`PrimalSurvivalBuilder.cs:422`); collector learned on first wood / fibre / hide pickup (QA known issue 1); axe = stone 2 + wood 1 + rope 1 |
| `Survival/SURVIVAL_LOOP.md` Day 1 | "branches / driftwood by hand" | only driftwood nodes in the builder (S2) |
| `Player/PlayerHealth.cs:6` summary | stats "live in PlayerVitals" | class is `PlayerSurvival` |
| `Player/PlayerWetLook.cs` summary | wetness rises in rain and while swimming | `PlayerSurvival` raises wetness only in rain |
| `ITEM_bandage` description | "Use it to dress a wound" | heals 25 through the eat path; bleeding keeps going |
| `Building/BuildSystem.cs:13` | "No modular base building (scope rule)" | conflicts with directive 27 (foundation / wall / roof / door): owner decision needed before wave 2 BUILD |

## 5. Scene checks (Lead or an agent with the bridge)

| Id | What to check |
|---|---|
| S1 | Which rocks / logs are ResourceNodes vs decoration: count `ResourceNode` per type under `World/Resources` and `World/Rocks`; whether any `ENV_Rock_Small` or cliff rock looks gatherable but is not |
| S2 | Whether branch / log objects exist (only `RES_Wood` driftwood becomes a node) |
| S3 | Wood / stone / fibre nodes within 40 m of `ZONE_PlayerSpawn`, and node grouping around each `RESAREA_*` marker |
| S4 | Only 2 `WaterSource` (Pond water, Stream water) and both still on the visible water after water iteration 2; at pond / stream banks near berry bushes or nodes, water still wins the E target (priority -1 / -2) |
| S5 | Live recipe count in `Resources/ItemDatabase` (docs say 27) and that `RCP_cooked_meat` is out of the list |
| S6 | Any `Climbable` besides the 4 fruit trees under `[Gameplay]/FruitTrees` |
| S7 | `[UI]/[HUD]` compass and minimap positions do not overlap |
| S8 | Scene roots match `PRIMAL_FRONTIER_ARCHITECTURE.md` 1 and the directive's hierarchy |

## 6. Top 15 gaps, ranked (P0 broken interaction first)

| Rank | Pri | Gap | Where | Owner (WAVE1) |
|---|---|---|---|---|
| 1 | P0 | Fruit harvest inverted: `int added = inv.Add(item, amount)` is the count that did NOT fit. With room: fruit is added, "Your pack is full." shows, the bunch never empties or regrows, `FruitHarvested` never fires (infinite fruit). With a full pack: the bunch empties and nothing is given. No test covers it | `World/FruitCluster.cs:34-35`; callers `PlayerClimb.cs:133, :170` | RES (+ a fruit test) |
| 2 | P1 | Bandage is "eaten": eat clip, `Ate` event (also completes the tutorial "food" step), never stops bleeding | `PlayerInteraction.cs:310`, `ItemDefinition.IsFood`, `PlayerHealth.StopBleeding` unused | SURV (item / survival), request to U_ for the use path |
| 3 | P1 | No bare-hand combat: empty hands + attack does nothing, so the new player cannot fight back; no punch chain, heavy, hitbox, knockback, clips, VFX / SFX | `PlayerCombat.cs:123`; `AnimParams.cs:66`; `IDamageable.cs:6` | COMBAT + ANIM (clips from CHAR) |
| 4 | P1 | Load does not restore creatures, carcasses, fruit or bleeding: killed predators come back alive, butchering resets, fruit is all ripe | `GameManager.cs:279`, `SaveSystem.Capture` | SURV (save) + AI (creature state) + RES (fruit) |
| 5 | P1 | Save robustness: old file deleted before the temp is moved, no backup, one slot; a bad file means lost progress | `SaveSystem.cs:44, :63` | SURV |
| 6 | P1 | Fire safety loop missing: no fire fear per species, no wildlife reaction, rain never puts a fire out, no Lighting / LowFuel states | `DinosaurDefinition`, `Campfire.cs:66, :238` | SURV (Campfire) + AI (fear fields) |
| 7 | P1 | Perception is design only: no scent, no stimuli, binary sight / hearing, omniscient chase | `DinosaurController.cs:106, :112`; `PERCEPTION_DESIGN.md` | AI |
| 8 | P2 | Resource data and yields off-spec: pick 2 (not 3-5), no small / medium / large stones, no branch / log nodes, no ResourceDefinition / GatherToolDefinition | `ResourceNode.cs:98`, `PrimalGameplayBuilder.cs:403-411` | RES |
| 9 | P2 | No status-effect framework: injury, bleed levels, modular effects all missing | `PlayerSurvival`, `PlayerHealth` | SURV |
| 10 | P2 | Food spoilage, fish / cooked fish and edible plants missing | `ItemDefinition`, `ItemStack`, `SaveData.SlotData` | SURV (+ RES for fishing spots) |
| 11 | P2 | Building pieces foundation / wall / roof / door and snapping missing | `BuildSystem.cs:13` | wave 2 BUILD (owner decision on the scope rule) |
| 12 | P2 | First-gather hitch 78-101 ms and 24-43 ms per learned recipe (`InventoryUI.OnLearned` rebuilds every tile) | `UI/InventoryUI.cs:59`, `CraftingSystem.OnGameEvent` | SURV |
| 13 | P2 | Registered feedback never played: boil bubbles / boil sound, "+ Hydration", ToolBreak, WaterFill, BandageWrap, FireHiss, Heal, DustImpact | `SfxPlayer.cs:21-22`, `VfxPool.cs:17` | SURV / RES / COMBAT per system (clips from U_) |
| 14 | P2 | Minimap reveals the whole island and all fresh water at start; allocates a HashSet every frame | `Minimap.cs:65, :132, :134` | wave 2 WORLD / UI |
| 15 | P3 | Weather and day: storm never random, no named day phases, weather ignored by wildlife, wet look only on foliage / water / player | `WeatherManager.cs:90-91`, `TimeManager.cs:28`, shaders | AI (WeatherManager) + wave 2 WORLD |

Below the cut (P3): difficulty setting, per-system test scenes, knife / tool heavy attack, journal Resources section,
HUD icons instead of words, rain collector unlock timing, mobile GATHER / HARVEST labels, dino Drink state unused,
torch never burns out.
