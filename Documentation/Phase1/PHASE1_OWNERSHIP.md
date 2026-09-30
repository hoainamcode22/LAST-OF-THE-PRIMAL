# Phase 1 (Core Survival Foundation), wave 1: agents and file ownership (2026-09-30)

Owner directive: continue the existing project, PC first. Never rebuild, reset the player, delete working systems or
destroy earlier animation work. Test policy: compile + Console + missing references + asset import + broken prefabs,
then stop. NO PlayMode test runs, no gameplay bot, no tutorial replays (the owner tests by hand).
Gap audit this wave is based on: Lead copy in `Documentation/Phase1/PHASE1_AUDIT.md`.

## Rules (short form of AGENT_PROTOCOL.md)
- Edit files directly on the PC (`$HOME/mnt/"LAST OF THE PRIMAL"`, `$HOME/mnt/"Model game khủng long"`) with
  device_bash: python read-modify-write or sed, never retype whole files from tool output. Before editing a file, copy
  it to `<file>.before_P1` only if it is not your own new file (no deletes are possible on the PC).
- Only edit files you own (table below). Need a change elsewhere: put it in your report as a request.
- `Core/GameEvents.cs` is shared append-only: re-read right before editing, append at the end of the enum / class.
- Unity bridge: one agent at a time via the lock in `Library/PrimalBridge` (see AGENT_PROTOCOL.md). Bridge scripts:
  `cp "$HOME/mnt/LAST OF THE PRIMAL/Tools/Bridge/"*.sh "$HOME/"` then `$HOME/run.sh <id> <Type.Method> [arg] [wait_min]`.
  Fresh ids `<prefix><n>_<hhmmss>`. Methods: `PrimalEditorBridge.Refresh`, `.Ping` (returns only after compile),
  `.ConsoleCheck`, `.SceneState`, `.SaveScene`, plus any `[PrimalBridgeCommand]` in the Editor scripts.
- A cycle under the lock: Refresh -> Ping -> ConsoleCheck -> (your builder command) -> SaveScene -> release. Keep it
  under 10 minutes. If the Console shows compile errors only in another agent's files, release and retry later.
- Scene edits only through editor builder commands that end with the scene saved. Never `PrimalGameplayBuilder`, never
  `PrimalSceneBaker` full bakes, never batch-mode Unity, never kill processes, no git.
- Blender: use the CLI/background tool (`execute_blender_code_for_cli`) or `blender -b` style scripts, never the open
  interactive Blender (the Lead keeps it free). Save to a new file or make a `_before_P1` copy first.
- Original IP only, no ancient structures (temples, ruins, shrines, towers, carvings by an old civilisation).
- Report: `Documentation/Phase1/_<code>_report.md`, concise, no em-dashes: what changed (files), commands run and their
  results (numbers), open requests to other agents, anything not done.

## Wave 1
| Agent | Prefix | Owns | Tasks |
|---|---|---|---|
| BUILD | B_ | `Building/*`, `World/Shelter`, `World/Bedroll`, `World/StorageBox`, `Editor/PrimalBuildingBuilder`, `Resources/Structures/*`, `Prefabs/Building/*` | rename `BuildSystem.Start(params)` (fix Console error); run / fix `PrimalBuildingBuilder` so Foundation, Wall, Roof, Door (+ doorway wall) exist as StructureDefinitions + prefabs + recipes (Building category); ghost green / red with ground, slope, collision, distance, resources checks for pieces; build dust puff |
| U | U_ | Player/*, Animation/*, Combat/*, Audio/*, VFX/VfxPool, UI/MobileHUD, UI/ControlsPanel, UI/ContextHints, UI/SettingsPanel, Core/GameSettings (touch fields only), Editor/PrimalCharacterBuilder*, Editor/PrimalAnimAudit, player FBX import + PlayerAnimator.controller, new `Editor/PrimalClimbBuilder` | remove mobile on-screen controls from the PC game (no MobileHUD on PC, no Touch toggle, no touch canvas in the scene, tests still compile); place ledge + rock-face `Climbable`s near the start area / cliffs with a builder; wave 2: import CHAR clips and wire them |
| SURV | S_ | Survival/*, Items/*, Player/PlayerHealth, World/Campfire, VFX/CampfireFx, World/WaterSource, World/OceanShore, World/RainCollector, UI/HUDManager, UI/InventoryUI, UI/SlotView, crafting UI, Core/SaveData, Core/SaveSystem, Editor/PrimalSurvivalBuilder, Editor/PrimalCraftingBuilder, `Data/Items`, `Data/Recipes` | water states Cold / Hot (temperature on the stack, cools over time), hot clean water hydrates + warms in cold / rain; ocean never drinkable (boiling salt water refused with a message); visible water container in the hand; recipe categories Fire + Storage (campfire, torch -> Fire; storage -> Storage) incl. crafting tabs; Charcoal item left by a burnt-out campfire |
| RES | R_ | World/ResourceNode, TreeHarvest, FruitCluster, WorldPickup, ResourceDefinition / Database / Manager, GatheringSystem, GatherToolDefinition, Player/PlayerFeedback, Editor/PrimalResourceBuilder*, resource data + placement | shipwreck materials (`wreck_scraps`, e.g. planks / nails / canvas as 1-3 items) with nodes at the wreck; physical dropped items (Rigidbody + collider, settles, pickable, returns to inventory, saved); tool gating: trees need Axe, boulders need Pick, hands get small branches / loose stones / fiber / berries only; enough hand resources near the start; move the nodes that sit in water |
| AI | P_ | AI/*, Core/Stimuli, Player/PlayerSignature, World/BushInteraction, World/Carcass, UI/PerceptionIndicator, Editor/PrimalPerceptionBuilder, Editor/PrimalWildlifeBuilder | wildlife reacts to weather (rain / storm: herbivores shelter under trees / rest more, predators hunt less in storms, all quieter) by reading `WeatherManager`; predators hunt herbivores (simple, rare, never kills a whole herd); Sleep / Rest use real clips once DINO lands them (fallback: current) |
| DINO | D_ | dinosaur Blender files, `Editor/PrimalDinoBuilder`, `Editor/PrimalCharacterBuilder.Dino`, `Editor/PrimalCreatureEyes`, dinosaur FBX import | deploy `Tools/pf_up_D2.zip` (undeployed), check the new dinosaur FBX clips (Rest / Sleep / Eat / Drink), run `PrimalDinoBuilder.RebuildClips` when the scene is saved; report clip names per species for AI |
| ENV | E_ | `Editor/PrimalEnvironmentBuilder*`, `Editor/PrimalVolcanoBuilder`, `World/VolcanoLandmark`, `World/EnvLocation`, `World/ZoneManager`, environment art, `Story/StoryTexts` (the carving line only) | run the pending Vegetation, Volcano, Wet and Story passes; replace the petroglyph "carvings" with natural or castaway content (no ancient people); ferns / moss / wet rocks at the waterfall; one volcano on the island (disable, never delete, the offshore landmark once the island volcano exists) |
| CHAR | C_ | player Blender files, `E:\Model game khủng long\scripts\pf_clips_*`, `export/staging/` | bake the real bare-hand + survival clips (BareHand_Punch_1/2/3, BareHand_Heavy, BareHand_Combo_End, Kick, Unarmed_Block, Gather_Branch, Gather_Stone_Hand, Drink_Kneel, Collect_Water, Bandage_Use, Unconscious) into the player work file, keep every existing clip, export the staging FBX + `clips_manifest.json` |

Wave 2 (after wave 1): U wires CHAR clips; AI wires DINO clips; HIER reorganises the scene hierarchy to the owner's
World / Gameplay / WorldSystems / Lighting / VFX / Audio / Managers layout; Lead verification + `PHASE1_STATUS.md`.

## Wave 2a (2026-09-30, after wave 1 reports `_B/_U/_S/_R/_P/_D/_E/_C_report.md`)
`PrimalEditorBridge.SaveScene` / `.SceneState` are back (the file had been overwritten at 05:02).
Owner decision: the volcano is the OFFSHORE one. It stays and erupts (fire fountain + glowing rock bombs arcing toward the
island, landing in the sea / on the shore with splash, embers, sound; small configurable damage on a direct hit; no
terrain destruction). The copy of VolcanoLandmark ENV put on the island vent is removed (disabled); on-island ash /
basalt / small lava stay unless the owner says otherwise.
| Agent | Prefix | Tasks |
|---|---|---|
| ENV | E_ | re-enable offshore `World/Landmarks/Volcano`; eruption on `World/VolcanoLandmark` (+ builder); disable the island-vent VolcanoLandmark copy; re-run `PrimalAtmosphereBuilder.Build` + `PrimalWaterBuilder.Build`; `Story/JournalSystem.cs:268` sea-water line |
| U | U_ | import CHAR's staging FBX (89 clips) + manifest, `BuildAndTest "Player"`, real Unconscious / Kick / Combo_End / Block; `PlayerInteraction.cs:428` salt text; `BareHandCombatTests` touch test ignore on PC |
| SURV | S_ | rewrite the 2 salt-water tests; create `cloth_bandage` / `scrap_rope` recipes; one use each for `sailcloth` and `wreck_nails`; HUDManager status icon spacing now that touch is gone |
| RES | R_ | move `World/Rocks/PFB_ENV_Rock_Large_03` off the lava vent; check the 3 nodes by the lava channel |
Wave 2b (after 2a): HIER scene hierarchy; Lead verification + `Documentation/PHASE1_STATUS.md`.
