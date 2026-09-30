# Phase 2 (World Exploration Expansion): agents, zones and ownership (2026-09-30)

Owner directive: six distinct natural environments on the EXISTING island. Do not rebuild, no second island, no ruins /
ancient civilisations, no modern structures, no gameplay bots, no PlayMode runs. Only compile + Console + references /
imports / prefabs. The owner explores by hand. Do not create things that were not asked for; reuse existing kit first
(Art/Environment/Models: ENV_PC_*, DET_PC_*, PROP_PC_Bones / Skeleton / FossilSlab / ClawSnag / BrokenTree / Nest ...,
Prefabs/Environment, existing cave `PFB_ENV_Cave_Entrance`). The offshore volcano stays as it is (erupting).

Transitions: no biome walls. Blend terrain layers, vegetation density and species over 20-40 m bands:
forest -> meadow, meadow -> wetland, river -> cave, forest -> dry vegetation -> black rock -> ash -> volcanic terrain.
Each zone gets ONE recognisable landmark. Keep resource nodes, paths, the player spawn and existing gameplay objects clear.

## Zones (Unity world coordinates; source `Art/Environment/Terrain/env_features_v2.json`, `Documentation/PCPhase/LOCATIONS.md`)
| # | Zone | Built on | Area | Landmark | Owner |
|---|---|---|---|---|---|
| 1 | Migration Valley | meadow + herbivore_valley + migration route + lookout knoll | centre about (0, 9, -35), r ~95 | large rock ridge | ENV-A |
| 2 | Prehistoric Wetland | wetland lagoon + river mouth | (102, 0.4, 172), r ~45 (+ 25 m blend) | ancient fallen tree | ENV-A |
| 3 | Bone / Carcass Valley | predator_territory, kill site, skeleton prop, canyon mouth (z > -100) | (-105, 21, -76), r ~50 | massive fossilised skeleton | BONE |
| 4 | Giant Fern Forest | deep_forest | (168, 8, 20), r ~70 | giant tree | ENV-B |
| 5 | Volcanic Foothills | canyon upper half (z < -100) to the volcano ridge | (-136, 23, -125) -> (-122, 44, -216) | black ridge | ENV-B |
| 6 | Deep Water Cave | cliff arc: existing cave (-18, 18, -131) and / or beside the waterfall pool (93.5, 19.4, -160) | underground | large underground pool | CAVE |

## Agents
| Agent | Prefix | Owns |
|---|---|---|
| ART (Blender) | A2_ | the interactive Blender (Blender MCP, only ART uses it this phase), `E:\Model game khủng long\scripts\phase2\*`, `...\export\phase2\*`, `Assets/_Project/Art/Environment/Models/Phase2/*`, `Prefabs/Environment/Phase2/*`, new `Editor/PrimalPhase2ArtBuilder.cs`, `Documentation/Phase2/ART_DELIVERY.md` |
| ENV-A | EA_ | new `Editor/PrimalZonesBuilder.Valley.cs`, `.Wetland.cs` (+ shared `Editor/PrimalZonesBuilder.cs` core, ENV-A creates it first), terrain / vegetation / props inside zones 1 + 2 and their blend bands |
| ENV-B | EB_ | new `Editor/PrimalZonesBuilder.FernForest.cs`, `.Foothills.cs`, terrain / vegetation / props inside zones 4 + 5 |
| BONE | BV_ | new `Editor/PrimalZonesBuilder.BoneValley.cs`, props / bones / carcass dressing / tracks inside zone 3 |
| CAVE | CV_ | new `Editor/PrimalCaveBuilder.cs`, cave geometry / water / lighting inside zone 6, terrain holes only at its entrance(s) |
| AI | P_ | AI/*, Editor/PrimalWildlifeBuilder, Editor/PrimalPerceptionBuilder, World/Carcass (scavenging hooks), creature placement in the scene |
| WORLD | W_ | Editor/PrimalAtmosphereBuilder, Core/AmbienceManager, Core/AmbienceEmitter, World/ZoneManager, World/EnvLocation, Story/* (journal, texts, minimap names), UI/Minimap, UI/JournalUI, zone VFX (fog, insects, dust motes) through VfxPool / own prefabs |
Wave 2: RES (resource nodes in the new zones), QA (verification), Lead (`Documentation/PHASE2_WORLD_STATUS.md`).

## Rules
- Read `Documentation/AGENT_PROTOCOL.md`. Bridge: `cp "$HOME/mnt/LAST OF THE PRIMAL/Tools/Bridge/"*.sh "$HOME/"`, lock in
  `Library/PrimalBridge`, fresh ids `<prefix><n>_<hhmmss>`, `PrimalEditorBridge.Refresh / Ping / ConsoleCheck /
  SceneState / SaveScene`. Cycle: acquire (only on GOT) -> Refresh -> Ping -> ConsoleCheck -> your command -> SaveScene
  -> release. HARD LIMIT 10 minutes per hold; split long builds into steps. Never run a command while not holding the lock.
- Terrain (`TD_Island`) is shared: change heights / splat / details / trees ONLY inside your zone circle + its blend band,
  with smooth falloff, always from a builder that first backs up the TerrainData region it touches (or the asset once
  per agent to `Art/Terrain/_Backup/TD_Island_before_<prefix>.asset`). Never re-run the full terrain passes
  (`PrimalEnvironmentBuilder.Terrain` / `Survey`), never touch another zone's area.
- Scene paths: use `Core/SceneRoots.cs` (World/Environment/{Terrain, Ocean, Rivers, Waterfalls, Forest, Caves, Wetlands,
  Volcano, Weather}, World/Gameplay/..., Managers/Markers). Put zone content under
  `World/Environment/<fitting slot>/<ZoneName>` (e.g. Forest/MigrationValley, Wetlands/PrehistoricWetland,
  Forest/BoneValley, Forest/GiantFernForest, Volcano/VolcanicFoothills, Caves/DeepWaterCave). Builders are idempotent
  (re-run = same result, they clear and rebuild only their own group).
- Performance (PC): mass vegetation as terrain trees / details or GPU-instanced prefabs with LODGroup and cull distances;
  static colliders only where the player can touch; no realtime lights except where required (cave: few, no shadows or
  baked-style). Report counts.
- Edit only your files; requests to others go in your report. `Core/GameEvents.cs` shared append-only. No git, no
  deletes, NO PlayMode. Back up any existing file you edit to `Tools/_backups_P2/<same path>` (NOT inside Assets).
- ART delivery: zone agents build terrain / vegetation / transitions first, then place their landmark once it is listed in
  `Documentation/Phase2/ART_DELIVERY.md` (poll it; until then keep a marker empty `LM_<Zone>` at the chosen spot).
- Report: `Documentation/Phase2/_<prefix>_report.md`: what / where (scene path, positions), assets used, commands and
  results with numbers, requests, not done. Concise, no em-dashes.
