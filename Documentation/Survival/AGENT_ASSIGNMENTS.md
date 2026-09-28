# AGENT ASSIGNMENTS (Milestone 1)

Studio roles from `.claude/agents` (Claude-Code-Game-Studios). The lead defines owner, files, dependencies and
acceptance for every task; agents never edit the same files at the same time and report what changed, why, files,
tests and known issues.

| Task | Owner (role) | Files (exclusive) | Depends on | Acceptance |
|---|---|---|---|---|
| Foundation: WaterType, ItemStack.waterType, ItemDefinition fire fields, SurvivalConfig, WaterRules, ISaveableStructure, save v4 water fields | lead | Items/ItemEnums, ItemStack, ItemDefinition, Survival/SurvivalConfig, Survival/WaterRules, Building/ISaveableStructure, Core/SaveData, SaveSystem (water + structure state only) | audit | compiles, old saves load (dirty -> DirtyWater, else CleanWater) |
| Needs, water sources, consumption, HUD, inventory, tutorial, environment, sleep, save migration of player state | systems-designer + gameplay-programmer (agent S1) | Survival/PlayerSurvival, Survival/SurvivalEnvironment (new), World/WaterSource, World/OceanShore, World/Shelter, Player/PlayerInteraction, Core/GameManager, Core/GameEvents (append), Core/SaveData + SaveSystem (player fields), UI/HUDManager, UI/InventoryUI, Story/TutorialManager, Story/JournalSystem, tests for these | foundation | tiers gradual and tier-free at start stats; fill / empty / drink by type; sea fill = salt; HUD tier words, water colours, no per-frame strings; tutorial steps by id; tests pass |
| Fire, cooking states, boiling, rain collector, new items / recipes / prefabs / config asset | gameplay-programmer + world-builder (agent S2) | World/Campfire, VFX/CampfireFx, World/RainCollector (new), Editor/PrimalSurvivalBuilder (new), Tests/PlayMode/SurvivalM1Tests (new) | foundation | fuel from item data; raw -> cooking -> ready -> burned visible and saved; boiling as a slot via WaterRules; collector fills only in rain; tent sleeps; one cooking path; builder idempotent |
| Models + icons: tent, rain collector, leaf cup, burnt meat | technical-artist (lead in Blender) | `E:\Model game khủng long\scripts`, Art/Models/Props, Art/Icons | - | original designs, LOD-light, icons for every new item (test) |
| QA: PlayMode suite, loop play-through, perf test | qa-lead (lead) | Tests (read), Documentation/Survival/QA_REPORT | all | suite green, loop verified in Play, bugs logged |
