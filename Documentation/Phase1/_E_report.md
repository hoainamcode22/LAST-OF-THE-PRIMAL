# ENV (E_) Phase 1 wave 1 report, 2026-09-30

## Result
All pending ENV passes ran through their bridge commands and saved the scene, each ending with 0 warnings. ConsoleCheck
before and after every cycle: 0 errors, 0 warnings (last: E_c11). All 23 ENV_PC models are in the scene. No petroglyph is
active. The one volcano is now on the island: the offshore landmark is switched off (SetActive false, kept in the scene).

## Files
- `Scripts/Editor/PrimalEnvironmentBuilder.Story.cs`: PROP_PC_Petroglyph is never placed. By the spring: `PROP_PC_ClawRock`
  (a boulder raked by four deep claw gouges, pale fresh stone in the grooves, chips at its foot). By the cave:
  `PROP_PC_CastawayMarks` (knife tallies in rows of five, an arrow pointing into the cave, a sailcloth scrap pinned under
  a stone). Examinable ids unchanged (`env_markings_ridge`, `env_markings_cave`), so saves, StoryIds and the "deeper"
  mission still work. Any old petroglyph found in the scene is switched off.
- `Scripts/Story/StoryTexts.cs`: `strange_markings` is now "Raked Stone" (claw marks). I also changed `cave_markings`
  (castaway knife marks + sailcloth) and the last sentence of `spring`, because both still described carvings.
- `Scripts/Editor/PrimalEnvironmentBuilder.Vegetation.cs`: new `PlaceWaterfallDressing` step
  (World/Environment/WaterfallDressing, rebuilt on each run). Denser, bigger basalt on the volcanic ridge. Wider clearance
  from gameplay nodes for root plates (2.6 m) and basalt (3.5 m).
- `Scripts/Editor/PrimalEnvironmentBuilder.Volcano.cs`:
  - Fixed the triangle winding on the lava channel and cracks. They faced down and were culled, so no lava ever showed.
  - The vent pool now drapes over the vent mound (before, 4 % of it was above the ground).
  - Added embers over the vent and the channel, and a heat shimmer over the channel.
  - TL_Ash is painted into the splat inside the volcanic mask (14278 texels) and TL_Ash is tinted slightly darker.
  - `Lava/VentSystem` now carries a VolcanoLandmark: smoke drifts with the wind, embers are stronger at night, rare
    rumbles, light ash within 30..130 m.
  - The offshore landmark is switched off. Arg "offshore" switches it back on.
- `Scripts/Editor/PrimalEnvironmentBuilder.Water.cs`: `FaceUp` fix in RibbonMesh. The river, lower river, mouth and
  spring brook ribbons faced down, and PF/Water Flow is `Cull Back`, so they were invisible from above. Water pass re-run
  (7 bodies, 14.5 m falls).
- `Scripts/Editor/PrimalEnvironmentBuilder.Check.cs` (new, read-only): per-model counts, missing references, resource
  nodes covered or buried, spawn clearance, water / lava surface orientation, colliders on the lava, petroglyph and
  volcano state.
- `Scripts/Editor/PrimalEnvironmentBuilder.cs`: Capture `focus=Name@dist@height` plus two new views (`waterfall_close`,
  `volcano_close`).
- `Scripts/Editor/PrimalEnvironmentBuilder.Assets.cs`: M_SailCloth added to the shared material remap.
- `Scripts/World/VolcanoLandmark.cs`: `smokeWind` and `rumblePuffSize` fields (defaults keep the old behaviour).
- Art:
  - `Art/Environment/Models/PROP_PC_ClawRock.fbx`, `PROP_PC_CastawayMarks.fbx` (new, 3 LODs; 2868 / 2580 tris at LOD0).
  - Prefabs made by the builder in `Prefabs/Environment/PC`.
  - `TL_Ash.terrainlayer` remap.
  - Blender source: `E:\Model game khủng long\scripts\env_pc\env_pc_story_v2.py` (bpy 4.2, reuses env_pc_assets.py).
- Backups: `*.before_P1` next to every edited script. PrimalVolcanoBuilder.cs was backed up but not changed.
- Captures in `Documentation/Screenshots/PCPhase/Env/`: E1..E6 (waterfall, waterfall_close, volcano_ash, volcano_close,
  focus on ClawRock_Spring and CastawayMarks_Cave) and `E_story_v2_props_blender.png`.

## Commands (bridge ids) and results
- E_v1 / E_v2 / E_v3 `Vegetation`:
  - Terrain trees: 1322 v1 trees kept in order. Of these, 421 became ENV tree ferns and 117 became ENV cycads.
  - New terrain trees: 82 tree ferns and 75 cycads.
  - Details: horsetail 4375, reeds 5395, giant fern 8499. Grass / fern / bush went from 135741 to 90596.
  - Placed props: 30 fallen trunks, 25 root plates, 80 + 18 moss rocks, 55 giant ferns, 60 horsetail clumps,
    100 magnolias, 47 basalt groups, 31 curtains.
  - Waterfall dressing: 20 moss / fern curtains, 3 wet mossy boulders, 34 giant ferns, 4 horsetail clumps. Also near
    the pool: the 9 PoolRocks from the Water pass (now on M_Env_WetRockMossy) and 18 moss rocks.
- E_st / E_st2 `Story`: 14 story prefabs, 17 Examinables, 22 theropod prints, 18 blood stains, 10 wreck pieces,
  claw rock at (105.7, 43.3, -197.2), castaway marks at (-11.4, 18.2, -129.3), old petroglyphs switched off: 0.
- E_vo / E_vo2 / E_vo3 `Volcano`: lava channel 56 pts, vent pool, 11 cracks, smoke, embers x2, 2 lights, shimmer x2,
  Examinable env_lava_channel, VentSystem VolcanoLandmark, offshore volcano off.
- E_wa `Water`: 7 bodies re-wound. Waterfall sheet + back sheet + 3 rivulets. Spring -> brook -> falls -> pool ->
  river -> lower river -> mouth -> ocean, all bodies on and facing up.
- E_we `Wet`: 7 shared materials already on PF/Wet Surface, 12 waterfall rock slots on M_Env_WetRockMossy, terrain on
  M_Env_TerrainWet.
- E_k4 `Check` (final):
  - ENV_PC models present: 23 / 23. Placed instances per model:

    | Model | Placed | Terrain trees |
    |---|---|---|
    | TreeFern_A | 0 | 175 |
    | TreeFern_B | 0 | 162 |
    | TreeFern_C | 0 | 166 |
    | Cycad_A | 0 | 56 |
    | Cycad_B | 0 | 60 |
    | Cycad_C | 0 | 76 |
    | GiantFern_A | 43 | 0 |
    | GiantFern_B | 46 | 0 |
    | HorsetailClump | 64 | 0 |
    | Magnolia_A | 50 | 0 |
    | Magnolia_B | 50 | 0 |
    | VineCurtain_A | 25 | 0 |
    | VineCurtain_B | 26 | 0 |
    | Roots_A | 13 | 0 |
    | Roots_B | 12 | 0 |
    | FallenTrunk_A | 15 | 0 |
    | FallenTrunk_B | 15 | 0 |
    | MossRock_A | 34 | 0 |
    | MossRock_B | 33 | 0 |
    | MossRock_C | 34 | 0 |
    | Basalt_A | 16 | 0 |
    | Basalt_B | 16 | 0 |
    | BasaltBoulder | 15 | 0 |

  - LODs and culling are as the builder defines. Tree ferns / cycads are terrain trees. Details are instanced. Prefab
    LOD cut-offs are 0.28 / 0.09 / 0.012 or similar, with shadows off on LOD2.
  - Missing references among 1969 ENV objects: prefab 0, mesh 0, material 0, broken shader 0, script 0, LOD renderer 0,
    collider mesh 0, terrain prototypes 0.
  - Resource nodes (1440 active):
    - Covered by an ENV collider: 0 (8 in the first check, fixed).
    - Under a new ENV terrain tree: 0.
    - 7 are next to v1 trees that were only swapped to new models in place (same positions as before).
    - More than 0.6 m under the terrain: 0.
  - Player spawn (-20, 1.5, 211): the only ENV prop within 12 m is WreckPlanks_0 at 3.2 m. It is flat planks with no
    collider (Story wreck remains). There are no ENV trees within 20 m.
  - Active PROP_PC_Petroglyph: 0. VolcanoLandmark: island VentSystem ON, World/Landmarks/Volcano off.

## Requests
- RES: `World/Rocks/PFB_ENV_Rock_Large_03` (a resource node) sits on the lava vent at about (-100, 45, -232), 0.2 m
  away. It hides the vent pool from most angles. Please move it a few metres off the vent. 3 resource nodes are within
  3 m of the lava channel: check they make sense next to lava.
- Lead / WORLD:
  - Re-run `PrimalAtmosphereBuilder.Build`. The volcano marker exists now (Markers/Zones/volcano), the active
    VolcanoLandmark is on the island, and the lava renderers are visible, so the heat hazard zones and lava emitters can
    be built. Its last run found none.
  - Re-run `PrimalWaterBuilder.Build`. The river / brook ribbon meshes were rebuilt facing up.
- STORY / Lead: `MissionCatalog.cs` "deeper" mission text ("Follow the markings", "They knew this island better than I
  do") still works with claw marks plus castaway marks, but could be reworded. Ids are unchanged.
- Lead: `PrimalEditorBridge.SaveScene` does not exist ("not found or not marked [PrimalBridgeCommand]"). Every ENV
  builder command saves the scene itself ("scene saved" in each result).

## Not done / notes
- No PlayMode, no git, no deletes. The old `PROP_PC_Petroglyph.fbx` / prefab stay as unused assets.
- Only 3 extra wet boulders fit at the pool edge: 41 spots were rejected for gameplay blockers and 26 for spacing.
- Edit-mode captures do not show PF/Water Flow water or particles (smoke, embers, mist), so the river and the pool
  cannot be judged from captures. Surface check: all water bodies are on and facing up. The pool disc is 19 % above the
  terrain; its edges are under the banks by design.
- The Lead's bridge lock was held by other agents for long periods (DINO 06:42-07:33, U 07:49-08:21). I reported this
  to main and did not break any lock.
