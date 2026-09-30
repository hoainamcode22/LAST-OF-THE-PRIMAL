# RES report, PC phase (leftovers + resource placement on ENV's terrain v2)

Agent RES (R_), 2026-09-30 (UTC). Unity was closed and the PC link down for the whole round: everything below is written
in the cloud mirror, compiles there (`./cc.sh all` runtime / editor / tests rc 0 with every agent's files) and waits in
`/mnt/user-data/outputs/pf_up_R9.zip` (11 RES files, md5 `ab7c78cbf23af76483d2604ecfd9859f`; copy in
`src_assets/pc_res/`). Nothing has been compiled by Unity or run on the PC. The exact commands are in
`NEXT_SESSION.md`, section "RES (R_) steps". Design and numbers: `Documentation/RESOURCE_SYSTEM.md` (sections 8b-8d, 9, 9b, 12 updated).

## Status

| # | Task | Status | Evidence / how to check |
|---|---|---|---|
| 1 | `WorldPickup.DropStack` keeps a food stack's age | DONE-NOT-TESTED (cloud compile) | a dropped stack of an item with `spoilHours` keeps `uniqueStack` (madeAt); the prompt says "(aging)" / "(spoiled)"; `Collect` puts it back with `InventorySystem.Add(item, n, false, madeAt)` (partial pickup keeps the rest on the ground); `SaveSystem` already saves a pickup stack's age. Test `ResourceTests.Dropped_Food_Keeps_Its_Age` (no scene) written; run: `PrimalTestRunner.RunPlayMode "ResourceTests"` |
| 2 | Fish shoal cue | DONE-NOT-TESTED | `ResourceManager.FishCue`: shoals with fish left within 28 m of the player; every 1.4-3.2 s per shoal pooled `WaterDrops` on the surface (`ResourceNode.cueHeight` = water depth, set by the builder), one in three a small `WaterSplash` (0.35) + quiet `WaterSplash` sound. Nothing out of range, no new prefabs (existing pooled ids). Test `Fish_Shoal_Cue_Plays_Near_The_Player_Only` written (far: 0 cues; near: 1; not every tick) |
| 3 | Felled tree leaves a stump (saved) that regrows later | DONE-NOT-TESTED | `TreeHarvest`: pooled stump objects under `[TreeStumps]` (generated 12-sided mesh, jagged top, the prototype's bark material, capsule collider, sized by the instance scale) for the first half of the 72 h; second half: stump gone, the tree instance grows as a sapling 12 -> 100 % (twice a second) until it is back. Stumps follow `_felled` (already saved as `SaveData.felledTrees`), so `Restore` shows them after a load; `RestoreAll` / `Regrow` remove them. By hand: fell a tree, look; sleep two nights: sapling |
| 4 | U's `PlayerFeedback` requests | DONE-NOT-TESTED | `Surface.Wood -> FootWood / FootWood`, `Surface.Grass -> FootGrass / FootGrass` (U's ids, present in the mirror); `OnDrink` param `kneel` skips the hand `WaterSplash`, `fill` plays nothing (no drops, no drink sound), plain unchanged |
| 5a | Re-snap of all nodes to the new ground, none in water | DONE-NOT-TESTED | `[Resources]` is rebuilt on the current terrain; `ResnapExisting` snaps `World/Resources` (45), the 3 cave stones and `Markers/ResourceAreas` (5), moves nodes that are in water to the nearest dry spot within 10 m or switches them off (logged); rocks (ENV's, re-snapped by ENV) standing in water deeper than 0.4 m keep their node switched off (scenery). `Verify` ends the log with `VERIFY ... off the ground A, in fresh water (not fish) B, below sea level C`: expect 0 / 0 / 0. Water = the 8 active WaterSource meshes (the switched-off old stream is skipped) |
| 5b | Clusters for the new biomes | DONE-NOT-TESTED | biome from ENV's `Markers/Zones/<id>` (EnvLocation) first, then water distance, splat layer (Mud, Rock, Ash, Moss, Sand, Forest) and trees. New recipes: river bank (stones, reeds / fibre, edible plants, driftwood), waterfall pool (stones, ferns), wetland (reeds, fibre, edible plants, few stones), meadow (fibre, berries, few stones), canyon (much stone, almost no food), ridge (stone, logs / deadwood). Bank clusters every ~20 m of fresh shore with fish in the shallows (every bank in the wetland and at the pool); zone clusters: meadow 12, herbivore valley 4, canyon 9, ridge 7, wetland 7, waterfall 4, river 6, volcanic ridge 3; a second trail from the beach to the river mouth; the beach start area unchanged (spawn-relative) |
| 5c | Rare nodes | DONE-NOT-TESTED | `ResourceCategory.Rare` (appended), definitions `rare_bones` (bone 1-2, 120 h), `rare_hide` (hide 1, 168 h), `rare_wreck_scraps` (wreck_scraps 1-2, 96 h). Bones: 4 piles 5-16 m around `env_giant_skeleton`, 4 in `predator_territory`, 1 at `env_theropod_trail`; one hide at the kill site; scraps at ENV's `WreckRemains` pieces (max 6, dry sand only) + 2 at the wreck. Models: the item's `worldPrefab`, else ENV's `PROP_PC_Bones` / `PROP_PC_WreckPlanks` prefabs; a kind whose item or model is missing is skipped with a log line naming what is missing (re-run after ENV `Story` / SURV S5) |
| 5d | Positions never hard-coded | DONE | everything is read at build time: `EnvLocation` markers, `Examinable.discoveryId` props, the `WreckRemains` group, WaterSource meshes, the terrain, the old `ZONE_` markers as fallbacks (a scene without ENV's markers still builds, with a WARNING line per missing location) |
| 6 | Captures | NOT RUN | `PrimalResourceBuilder.Capture "v2"` (island map with dots incl. blue fish / violet rare, start area, player view) |

## Files (RES owned, all in pf_up_R9)

`World/WorldPickup.cs` (age), `World/ResourceNode.cs` (`cueHeight`), `World/ResourceManager.cs` (fish list + cue),
`World/ResourceDefinition.cs` (`Rare`), `World/ResourceDatabase.cs` (hands on Rare), `World/TreeHarvest.cs` (stumps,
saplings), `Player/PlayerFeedback.cs` (footsteps, drink params), `Editor/PrimalResourceBuilder.Data.cs` (rare
definitions, prefab kinds with fallbacks), `Editor/PrimalResourceBuilder.World.cs` (locations, biomes, banks, zone
clusters, rare, re-snap, verify), `Editor/PrimalResourceBuilder.Capture.cs` (dot colours), `Tests/PlayMode/ResourceTests.cs` (+2).
Docs: `RESOURCE_SYSTEM.md`, `NEXT_SESSION.md` (RES steps), this report.

## Requests

- **ENV:** run `PrimalEnvironmentBuilder.Story` before my builder (the bone / plank props are my fallback models); if a
  rock of `World/Rocks` should stay gatherable in shallow water, the 0.4 m depth rule is in `ConvertExisting`.
- **SURV:** a `worldPrefab` on `ITEM_bone` / `ITEM_hide` is used when present (the gameplay builder gave bone a model);
  `wreck_scraps` gets its model at S5. No change needed from you.
- **U:** `WaterDrops` / `WaterSplash` are reused for the fish cue; a dedicated ripple ring (`VfxId.FishRipple`, a flat
  expanding ring) would read better on the water shader if you have time.
- **Lead:** the island grid + zone clusters will raise the node count (about 1500-1700 expected); if the frame budget
  needs it, lower the zone cluster counts in `PlaceNew` (`Biomes` table) first, not the start area.

## Known limits

- Nothing above ran in Unity: the verify numbers, the cluster counts and the look of stumps / saplings are expectations.
- Fish placement needs 3 x 3 m of water at 0.15-1.5 m depth: the brook and the river mouth may get none (the river, pool,
  pond and lagoon should).
- A stump uses the prototype's first "Bark" / "Trunk" material; ENV's new tree ferns and cycads use their own trunk
  materials, which the lookup accepts by the same name rule (else the first material).
- The fruit tree save ids, berry crops and converted rocks are unchanged from wave 1.
