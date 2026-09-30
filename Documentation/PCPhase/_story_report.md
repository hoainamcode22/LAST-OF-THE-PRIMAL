# STORY report (T_): chapters, missions, objective line, survivor lines, journal, minimap, death (PC phase, 2026-09-29)

Code in the cloud mirror (`src/`). Deploy zip `pf_up_T1` (only my files) is on the PC at `Tools/pf_up_T1.zip`.
Status words: DONE-VERIFIED (how), DONE-NOT-TESTED, PARTIAL, NOT COMPLETED - TOOL LIMITATION.
Editor state: it answered once (17:11 UTC) and has taken no command since 17:33 UTC; see section 9 for what ran and what waits.

## 1. Result per item

| # | Item | Status | Evidence / where |
|---|---|---|---|
| 1a | Mission system, data driven | DONE-NOT-TESTED (compiles in the cloud against the PC state; `StoryTests` written, not run) | `Story/MissionDefinition.cs` (ScriptableObject: id, chapter, order, main / optional, title, objective, late objective, clue, requires, start rules, complete rules Any / All, soft deadline Sunset / Nightfall / Hour, marker None / Location / NearestFreshWater / Camp / Shelter + delay, start / done / late lines, journal page, start cue, endsChapter). Rule kinds: Event (GameEventType names resolved at run time, ids with `|` and `*`, count, distinct), Location (visited, legacy zone names too), NearLocation (m), Check (named rules), Hour window, MissionDone. `Story/MissionSystem.cs` runs it (events + 2 Hz poll, no per-frame allocation) |
| 1b | Chapters 1-4, 12 main missions + optional | DONE-NOT-TESTED | `Story/MissionCatalog.cs` (section 2). Chapter N ends with its `endsChapter` mission; unfinished missions stay open but the objective prefers the current chapter. The last main mission ("Deeper Still") keeps the mystery open |
| 1c | Existing intro kept | DONE-NOT-TESTED | `Story/IntroSequence.cs` unchanged except the card: DAY ZERO / "Survive the first day" |
| 1d | 25-step tutorial folded into chapter 1 as paced tips | DONE-NOT-TESTED | `Story/TutorialManager.cs`: the steps are lessons learned in any order; a lesson's tip (keys + Vietnamese gloss, through `ContextHints.QueueTip`, saved in tipsSeen) shows only when relevant (thirsty, raw meat in the pack, evening without shelter...), at most one per 45 s, spread over the first in-game day. Old API kept (Index = first lesson not learned, CurrentId, Restore by id / old index) so `SurvivalNeedsTests` / `SurvivalLoopTests` expectations still hold |
| 1e | Save through ISaveSection | DONE-NOT-TESTED (round trip covered by `StoryTests`, not run) | sections `missions`, `lessons`, `voice`, `creatures`, `death`, `map`. Saves from before the mission system: chapter one missions are marked done from the old tutorial step / day / shelters (`MissionSystem.MigrateFromLegacy`), places from ZoneManager visits |
| 2a | Small CURRENT OBJECTIVE UI | DONE-NOT-TESTED | `UI/ObjectiveUI.cs`, own canvas `[Objective]` under the minimap: chapter name (accent) + one line + a clue line the first time; shows on change, near a soft deadline (pulses warm), after a menu closes; fades after 9 s; "Title  done" for 2.8 s when the followed objective completes. Hides the HUD's older objective box at start (the compass marker stays) |
| 2b | Minimal markers | DONE-NOT-TESTED | only find_water (nearest fresh water, after 4 in-game hours), first_night and opt_return (camp). HUD compass reads `TutorialManager.CurrentTarget` = `MissionSystem.MarkerTarget`; minimap clamps it to the edge. Exploration missions give text clues |
| 3 | Survivor lines | DONE-NOT-TESTED | `Story/ProtagonistVoice.cs`: subtitles (hand font), once per game unless repeatHours, 25 s gap between ordinary lines, story lines wait instead of being dropped, a breath with fear lines, only while playing. Examining a prop speaks its thought. List in section 3 |
| 4a | Journal 6 sections | DONE-NOT-TESTED | `Story/JournalSystem.cs`, `UI/JournalUI.cs` (6 tabs, "N of M found" counting only what exists on the island, long lists pack tighter). Enum kept by value: Survival 0, Creatures 1, Crafting 2, Locations 3 (old World), Resources 4, Discoveries 5. Migration: wreck -> LOCATIONS, prints / log / predator roar -> DISCOVERIES, fruit -> RESOURCES; page ids unchanged (saves store ids) |
| 4b | Creature pages by observation | DONE-NOT-TESTED | `Story/CreatureJournal.cs`: up to 6 creatures per 0.4 s tick, in view, 55 m (90 m large, 130 m flyers / swimmers), max 2 linecasts per tick. Notes: appearance (sighted), habitat (location it stands in), diet (seen eating), groups of up to N, drinking, sleeping / resting, fleeing, charging, hunting, fire behaviour (avoid / circle / wait / watch), threat (after it hunts, charges or hits the player; calm grazers after 45 s watched). Hits: the attacking creature nearest the hit source. Raises `CreatureObserved` ("herd:triceratops"...) |
| 4c | Locations when entered | DONE-NOT-TESTED | 18 location ids + legacy pond / rocky; ZoneEntered ids normalised (`ZONE_StartBeach` = `beach`, `DeepForest` = `deep_forest`) |
| 4d | Discoveries from Examinable | DONE-NOT-TESTED | `World/Examinable.cs`: empty thought / name use `StoryTexts`; unknown ids get a page from the prop. Ids and texts in section 4 |
| 5 | Minimap discovery only | DONE-NOT-TESTED | `UI/Minimap.cs`: fog texture 128 px (soft reveal 42 m around the player, updated at most every 0.35 s and only after moving 3 m); markers: fires, shelters, fresh water once within 22 m, belongings bundle, visited places (landmarks shipwreck / volcano / waterfall once within 120 m), objective direction; place names on the big map. No creatures, no resources. The per-frame HashSet is gone (visited set kept from ZoneEntered events) |
| 6 | Death | DONE-NOT-TESTED (share rule covered by `StoryTests`, not run) | `Story/DeathSystem.cs` + `Story/DeathBundle.cs`, flow in `Core/GameManager.cs`: half of every stack (rounded up) and every other single item go into one "Recover your belongings" bundle at the death spot (hide / bedroll model, marked on the map, max 3, no decay by default); the item in hand stays (`keepEquipped`), `keepToolsAndWeapons` optional; journal, recipes, world, structures stay. Respawn at the last rested shelter / bed if it still stands, else the beach; saved right after. Death screen (`UI/DeathScreenUI.cs`): cause, what was left, where you wake, one WAKE UP button |
| 7 | PrimalStoryBuilder | DONE-NOT-TESTED (compiles; not run: editor busy) | `Editor/PrimalStoryBuilder.cs` `Build(""|"map"|"reset-missions")`, `BakeMap`, `Inspect` (section 6) |

## 2. Missions (built-in; assets `Resources/Story/Missions/MIS_<id>.asset` after the builder)

Main missions (the objective line):
| # | Id | Ch | Title / objective | Starts | Completes | Extra |
|---|---|---|---|---|---|---|
| 1 | wake | 1 THE SHORE | Wake / "Search the wreck for anything useful" | story begins (after the intro) | LootOpened, or any ItemAdded, or visited shipwreck | clue "The wreck lies along the shore." |
| 2 | find_water | 1 | Find Water / "Find fresh water before sunset" (late: "Find fresh water") | wake | Drank / WaterFilled dirty_water or clean_water, or within 3 m of fresh water | soft deadline sunset; late line; marker nearest fresh water after 4 in-game h; clue "The sea is no use. Streams run down from the high ground." |
| 3 | make_fire | 1 | Make Fire / "Build a fire and get it burning before dark" | wake | FireLit | soft deadline nightfall |
| 4 | find_food | 1 | Find Food / "Find something to eat" | wake | Ate | clue "Berry bushes grow where the forest meets the sand." |
| 5 | build_shelter | 1 | Build Shelter / "Build a shelter before nightfall" | wake + (13:00 or make_fire done) | a Shelter exists | soft deadline nightfall |
| 6 | first_night | 1 (ends ch 1) | Survive the First Night / "Survive until dawn" | wake + dusk (18:00) | DayStarted or Slept | start cue: distant roar, PredatorWarning ("That sound..."); marker camp; done line "I made it through the night." |
| 7 | follow_river | 2 THE RIVER | Follow the River / "Follow the river to its source" | first_night | within 110 m of the waterfall, or entered it | clue "The stream came down from somewhere in the hills. Follow it upstream."; done "Falling water. Close now." |
| 8 | find_waterfall | 2 | Find the Waterfall / "Find where the river falls" | follow_river | entered waterfall | line "So this is where the river begins." |
| 9 | observe_herd | 2 (ends ch 2) | Observe the Herd / "Watch a herd from a safe distance" | find_waterfall | HerdSighted (AI), or CreatureObserved herd:* (3+ of a species within its herd radius, 25-45 m, watched 6 s), or MigrationSeen | clue "Wide trails through the ferns lead down to the valley." |
| 10 | investigate_tracks | 3 THE TRACKS | Investigate Predator Tracks / "Find out what made the tracks" | observe_herd | TracksFound (AI), or 2 different predator signs examined (footprint, claw_marks, kill_site, blood_trail, broken_trees, bones), or entered predator_territory | clue "The prints lead away from the river, towards the broken trees." |
| 11 | old_camp | 3 (ends ch 3) | Discover the Old Camp / "Look for signs of other people" | investigate_tracks | entered old_camp, or examined old_shelter / old_firepit / old_tools | clue "Cut marks on a branch, a knot in old cord. Someone else was here." |
| 12 | volcanic_ridge | 4 THE WILDERNESS | Reach the Volcanic Ridge / "Climb to the smoking ridge" | old_camp | entered volcano | clue "The markings point inland, towards the smoke."; done "The ground is warm. The mountain is awake." |
| - | deeper | 4 (last) | Deeper Still / "Follow the markings deeper into the island" | volcanic_ridge | 2 different markings examined (strange_markings, cave_markings) | done "They knew this island better than I do." (mystery stays open) |

Optional missions (shown only when no main mission is open, all finish quietly):
| Id | Ch | Objective | Starts | Completes |
|---|---|---|---|---|
| opt_boil | 1 | Boil water at the fire to make it safe | find_water + make_fire | WaterBoiled |
| opt_bandage | 1 | Stop the bleeding with a bandage | wake + StatusApplied bleeding | ItemUsed bandage |
| opt_torch | 1 | Carry a torch for the night | make_fire + dusk | a torch in the pack |
| opt_firewood | 1 | Gather dry wood and keep the fire burning | make_fire + dusk | FuelAdded x2 |
| opt_weapon | 1 | Make a weapon before going inland | wake + (CreatureSighted or PredatorWarning or first_night) | a spear / bow / knife / sword crafted or carried |
| opt_migration | 2 | Watch the great herd cross the valley from high ground | find_waterfall | MigrationSeen (AI; the mission waits until that event exists) |
| opt_source | 2 | Climb above the waterfall to where the river begins | find_waterfall | examined the spring, or entered ridge |
| opt_hunt | 2 | Hunt an animal for meat and hide | first_night | CarcassButchered |
| opt_fish | 2 | Catch a fish in the shallows | follow_river | ItemAdded raw_fish |
| opt_return | 2 | Return to camp before darkness | first_night + (150 m from camp and 16:30-20:00) | within 15 m of a camp; soft deadline nightfall, marker camp |
| opt_prepare | 3 | Pack food, water and a weapon before going deeper | observe_herd | food + clean / dirty water in a container + a weapon in the pack |
| opt_field_notes | 3 | Study five different creatures | find_waterfall | 5 creature pages (progress shown) |
| opt_cave | 4 | Find where the cold air comes from | old_camp | entered cave |
| opt_fossils | 4 | Find the bones that turned to stone | old_camp | fossil_bed entered or examined |
| opt_nest | 4 | Find where the hunters raise their young | old_camp | nest entered or examined |
| opt_discoveries | 4 | Record eight discoveries in the journal | investigate_tracks | 8 DISCOVERIES pages |

## 3. Survivor lines (`ProtagonistVoice`, editable in `[Systems]/Story`)

Watched situations: "That's fresh water." (fresh water in view within 14 m) / "I need shelter." (day 1 after 16:00, no shelter) /
"It's getting dark." (dusk, once a day, days 1-3) / "I need to eat something." (hunger < 30) / "My throat is burning." (thirst < 30) / "So cold..." (cold).
Events (first time): "Anyone...? Anyone at all?" (wreck) / "What made those tracks?" (FootprintFound, TracksFound) / "That sound..." (PredatorWarning) /
"Rain. I need to stay dry." (first rain) / "I'm hurt. I need to be careful." (first bleeding or limb injury) / "Don't move. Don't breathe." (first predator) /
"What... is that?" (first dinosaur) / "Warmth. Finally." (first fire) / "Stay close to the fire." (first night) / "Salt... I can't drink this." /
"My stomach..." (sick) / "So this is where the river begins." (waterfall) / "So many of them..." (herd) / "The ground is shaking..." (migration) /
"Someone else was here." (old camp) / "Cold air... from deep inside." (cave) / "The ground is warm here." (volcano, heat warning) /
"Still breathing. Barely." (respawn) / "My things. Still here." (belongings recovered). Mission lines: see section 2.

## 4. Journal pages added (texts in `Story/StoryTexts.cs`; the scene's list keeps hand edits)

- LOCATIONS: beach, wreck (shipwreck), forest, deep_forest, river, waterfall, meadow, canyon, wetland, cave, ridge, volcano, predator_territory ("Hunting Ground"),
  herbivore_valley ("The Grazing Valley"), old_camp, fossil_bed, nest, migration_view ("The Lookout"), pond, rocky (legacy).
- DISCOVERIES (Examinable.discoveryId -> page, thought): footprint, herd_tracks, claw_marks, bones, kill_site, blood_trail, broken_trees, nest (eggs), fossil_bed
  (stone skull), old_shelter, old_firepit, old_tools, old_camp_tally, strange_markings, cave_markings, old_wreck ("Older Timbers": not our ship), shipwreck_remains,
  giant_skeleton, theropod_trail, lava_channel, view_knoll, spring, captains_log, predator_sign. ENV's ids from LOCATIONS.md are read as these: the `env_`
  prefix is dropped and scattered_bones = bones, nest_eggs = nest, old_camp_firering = old_firepit, old_camp_shelter = old_shelter, old_camp_tool = old_tools,
  markings_ridge = strange_markings, markings_cave = cave_markings, wreck_remains = shipwreck_remains (all 18 ENV props have a text).
- RESOURCES (first pickup): wood, stone, fiber, berries, edible_plant, raw_meat, raw_fish, hide, bone, ship scraps (`scrap|ship_scrap|...`), fruit (moved).
- SURVIVAL new: Wounds, Rain, Food Turns, Waking Again. CRAFTING new: Torch, Bandages, Carrying Water, The Knife.
- CREATURES: 7 old species + pteranodon ("Leather Wings"), mosasaurus ("Sea Serpent"); the old texts that told behaviour before it was seen are replaced when unedited.

## 5. First-day tips (TutorialManager lessons; `tips` list in the scene)

walk (4 s in), search (a prompt on screen), wood (after searching or 60 s), open_craft (wood + stone, unless U's wood tip already showed), fiber (10:30),
water (thirst < 70, 11:30 or near water), food (hunger < 70, 12:00 or food in the pack), campfire (13:30 or a campfire item), cook (raw meat / fish carried),
fill_water (empty container), boil_water (dirty / salt water + lit fire within 15 m), drink_clean (clean water, thirst < 85), tent (15:30, no shelter),
sleep (evening near a bed), spear (after the roar or day 2), observe (first creature, unless U's dinosaur tip showed), return (near a shelter: rest saves and
sets the wake-up point), journal (2 pages), map (3 places), bandage (bleeding), stealth (a creature noticed you). One tip per 45 s at most.

## 6. PrimalStoryBuilder (bridge commands)

`PrimalStoryBuilder.Build ""`: mission assets (create missing; `reset-missions` rewrites from the catalog), `[Systems]/Story` with MissionSystem (mission list),
ProtagonistVoice (adds missing lines), DeathSystem; TutorialManager tip texts; journal pages / sections (`SyncBuiltIns`); bakes `[Objective]`, minimap fog layer,
journal (6 tabs, the old 4-tab layout is re-laid), death screen; checks that the 18 location ids exist (ZoneManager + `Markers/Zones`) and which examinable
ids have texts; logs and saves the scene. `"map"` also bakes the survivor's map (`Art/UI/T_MinimapSketch.png`: parchment tones from a top-down render, ink
contours every 8 m, coast line, sea wash; `Resources/MinimapData` points to it). `BakeMap` alone, `Inspect` read-only.

## 7. Files (mine)

New: `Story/{StoryIds, StoryTexts, MissionDefinition, MissionCatalog, MissionSystem, ProtagonistVoice, CreatureJournal, DeathSystem, DeathBundle}.cs`,
`UI/ObjectiveUI.cs`, `Editor/PrimalStoryBuilder.cs`, `Tests/PlayMode/StoryTests.cs`.
Changed: `Story/{TutorialManager, JournalSystem, IntroSequence}.cs`, `UI/{JournalUI, Minimap, DeathScreenUI}.cs`, `World/Examinable.cs`,
`Core/GameManager.cs` (story systems, ObjectiveUI, death / respawn; AI's creature-save lines untouched), `Core/GameEvents.cs` (appended
`MissionStarted, MissionCompleted, ChapterStarted, CreatureObserved, BelongingsRecovered`). `UI/DeathScreenUI` was in no one's list: taken as part of the death flow.

## 8. Requests

| To | Request | Why |
|---|---|---|
| ENV | none blocking: LOCATIONS.md ids and the 18 `env_*` prop ids are mapped (section 4). Keep `thought` empty so the story text is used | missions 10-12 and "Deeper Still" |
| AI | `HerdSighted` / `MigrationSeen` / `MigrationStarted` / `TracksFound` with id = species id (TracksFound: any id) | my own herd watch already completes mission 9; AI's events add to it and unlock opt_migration |
| SURV | `HUDManager`: the old objective box and `OnTutorialDone` ("DAY ONE SURVIVED") can go; the compass marker should keep reading `TutorialManager.CurrentTarget` (now the mission marker) | ObjectiveUI replaces the box (hidden at run time today) |
| Lead | after ENV's TERRAIN_READY: `PrimalStoryBuilder.Build "map"` (see 9) | map picture + location checks on the final terrain |

## 9. Deploy / verification state

- Cloud compile `./cc.sh all` at 17:40 UTC (mirror = PC state for every other agent's file, checked by md5 over all 215 .cs):
  runtime rc 0, editor rc 0, tests rc 0.
- `pf_up_T1` (21 files) extracted on the PC at 17:46 UTC under the bridge lock (`21 extracted, bad 0`), but the editor did not take the
  refresh ("editor did not take the refresh (busy / playing?)"): it has answered no bridge command since 17:33 UTC (last result `E_9`),
  so **the PC has NOT compiled my files yet** (Runtime.dll still 17:26). The editor answered my ping `T_ping1` at 17:11 UTC once.
- TERRAIN_READY.txt (17:33) is in: no story props to move (the story places none; missions read `Markers/Zones/<id>` at run time).
- Ordered deploy list for when the editor answers (one lock cycle):
  1. `$HOME/deploy.sh pf_up_T1` (re-extracts the same 21 files, refresh, compile; expect no `error CS`).
  2. `$HOME/run.sh T_2 PrimalStoryBuilder.Build "map" 12` (mission assets, [Systems]/Story, tips, journal pages, UI layouts, the survivor's map on
     the v2.1 terrain, location / prop id checks; saves the scene).
  3. `$HOME/run.sh T_3 PrimalEditorBridge.ConsoleCheck "" 5`.
  4. `$HOME/run.sh T_4 PrimalTestRunner.RunPlayMode "StoryTests" 3` (5 no-scene tests), optionally `"SurvivalNeedsTests"` (the tutorial API the
     survival tests use).

## 10. Known limits

- Nothing played by hand: pacing of tips / lines, the objective fade, the fog look, the bundle model (hide item model scaled 1.6) are MANUAL TEST REQUIRED.
- Mission 7-12 conditions depend on ENV's location markers and props; the builder log lists what is missing.
- Creature observation uses AI's public state (DinoState, Sleeping, FireBehaviour): "seen eating" for predators only happens if AI enters Eat at a carcass.
- Loading an old save migrates chapter one only from the tutorial step (no per-mission history existed).
