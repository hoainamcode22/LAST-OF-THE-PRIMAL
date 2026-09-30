# ENV report, PC phase (island depth: terrain, water, prehistoric vegetation, storytelling, volcano, wet surfaces)

Agent ENV, 2026-09-29 (UTC). Scene `Assets/_Project/Scenes/Island_VerticalSlice.unity`, Unity 6000.3.10f1, URP 17.3.
Locations, route, water bodies and Examinable ids: `LOCATIONS.md`. Terrain hand-off: `TERRAIN_READY.txt`.
Build log in the project: `Documentation/PCPhase/Env/env_build.txt` (every builder step appends to it).

## Status

| # | item | result | how checked |
|---|---|---|---|
| 1 | survey + 10 "before" captures | DONE-VERIFIED | `PrimalEnvironmentBuilder.Survey` / `Capture before` ran in the editor, 10 PNGs in `Documentation/Screenshots/PCPhase/Env/before_*.png`, looked at |
| 2 | terrain pass (heights v2.1, 9 layers, trees, details, re-snap) | DONE-VERIFIED | `TerrainPass` ran twice (second run idempotent: same 115 tree moves, 20 re-snaps), stats in env_build.txt, scene saved |
| 2 | LOCATIONS.md, `Markers/Zones/<id>` (18), `Markers/Migration/Route_00..13`, TERRAIN_READY.txt | DONE-VERIFIED | `Markers` ran; route steepest 28 deg at Route_12-13 |
| 3 | river, lower river, mouth, brook, spring, waterfall pool, lagoon + WaterSource | DONE-VERIFIED (ran) | `Water` + `PrimalWaterBuilder.Build` ran without errors: 8 fresh bodies, flow maps; visuals not yet seen in a capture |
| 3 | waterfall (sheet, rivulets, mist, spray, wet mossy rock face) | DONE-VERIFIED (ran) | 14.5 m drop built; wet rock material switches with the Wet step (not run yet) |
| 4 | prehistoric plant models + textures (original, Blender) | DONE-VERIFIED (offline) | 41 FBX with LODs from `tools/env/env_pc_assets.py` (bpy 4.2), contact sheet reviewed (`offline_models_sheet.jpg`) |
| 4 | vegetation placement in Unity (`Vegetation`) | DONE-NOT-TESTED | compiles (cloud); deployed in pf_up_E3 + E5 + E7; curtain / rim placement checked offline on the real heights (32 of 36 rims get a curtain, wall faces 40-72 deg); not run: editor closed |
| 5 | storytelling props + 18 Examinables (`Story`) | DONE-NOT-TESTED | compiles; deployed E3 + E4 + E6; placement math checked offline on the real heights (10 wreck pieces on dry sand, fossil slab at the canyon wall, cave / spring markings); not run |
| 6 | volcanic ridge: lava channel, vent pool, cracks, smoke, glow, shimmer (`Volcano`) | DONE-NOT-TESTED | compiles, PF/Lava DXC-checked (D3D11 + Vulkan paths, 3 keyword sets, 0 errors); channel edges checked offline: all 112 edge points under the banks; not run |
| 7 | wet surfaces (`Wet`: shared materials + terrain on the wet shaders) | DONE-NOT-TESTED | shaders DXC-checked, 0 errors; step deployed in E1/E3, not run |
| 8 | hierarchy under `World/Environment/{Terrain, Water, Forest, Rocks, WaterEdge, Volcano, Storytelling}` | DONE (in code) | Water and Waterfall groups exist in the scene; the others are made by the steps above |
| 9 | "after" captures + review + ConsoleCheck | NOT COMPLETED - TOOL LIMITATION | the Unity editor closed at about 17:33 UTC (no `Temp` folder, no bridge result from anyone since 17:33:14). Checked every 3 min until 19:03 UTC: still closed |

## What still has to run (ordered, when the editor is open)

All ENV files are already extracted into `Assets/_Project` (by manifest md5; zips in `Tools/`): `pf_up_E1` (builder,
shaders, v2 terrain data, textures), `pf_up_E2` (fixes, v2.1 terrain data), `pf_up_E3` (builder steps, PF_Lava, 41 FBX,
plant textures, features json), `pf_up_E4` + `pf_up_E6` (Story fixes), `pf_up_E5` + `pf_up_E7` (Vegetation: curtains follow
the wall face; v1 trees swapped to the new models). Verified at 18:15 UTC that the ENV scripts / shaders on the PC match
the cloud copies (md5). If a later copy ever overwrote them, extract E3 to E7 again in that order. Then, one command at a time through the bridge:

1. `PrimalEditorBridge.ConsoleCheck` (compile of the new files)
2. `PrimalEnvironmentBuilder.Wet` (switches the shared rock / wood / wreck materials in place, terrain to PF/Terrain Lit Wet; undo: `Wet revert`)
3. `PrimalEnvironmentBuilder.Vegetation` (imports the 41 models, makes prefabs, trees, details, placed plants and rocks)
4. `PrimalEnvironmentBuilder.Story`
5. `PrimalEnvironmentBuilder.Volcano` (its log says whether the landmark heat haze is set; if "NONE": `PrimalVolcanoBuilder.Atmosphere`, then `Volcano` again for the vent shimmer)
6. `PrimalEditorBridge.ConsoleCheck`
7. `PrimalEnvironmentBuilder.Capture after` (overview, beach, waterfall, river_valley, meadow_from_view, canyon, wetland, forest_floor, old_camp, volcano_ash)

Every step is idempotent (rebuilds only its own objects), logs to env_build.txt and saves the scene only when it was clean
before (or with the arg `force`). `Library/PrimalBridge/command.json` still holds ENV's last Refresh (harmless if the
editor picks it up on start). After the run, look at the captures for: skeleton / nest / slab sitting on the ground,
curtains on the canyon walls, cave markings not inside the cliff, lava channel edges, broadleaf-to-fern mix in the forest.

## Files

- Unity: builder `Scripts/Editor/PrimalEnvironmentBuilder*.cs` (8 files), `Scripts/World/EnvLocation.cs`, shaders
  `PF_WetSurface`, `PF_TerrainWet` (+ `.hlsl`, add pass), `PF_Waterfall`, `PF_Lava`, `PF_FoliageWind` (updated),
  `Art/Environment/{Terrain, Textures, Textures/Plants, Models, Materials, Generated}`, prefabs `Prefabs/Environment/PC`.
- Generators (cloud `tools/env/`, copy on the PC in `E:/Model game khủng long/scripts/env_pc/`): terrain_v2.py,
  terrain_post.py, env_textures.py, env_veg_textures.py, env_pc_assets.py, preview3d.py, preview_views.py.
- Blender source of the kit (my own new file, textures packed): `E:/Model game khủng long/ENV_PC_Kit_20260929.blend`.
  No existing .blend was opened or changed.
- Offline previews: `Documentation/Screenshots/PCPhase/Env/offline_*.png|jpg` (terrain views, model sheet).

## 1. Survey (before)

- Terrain `ENV_Island_Terrain`: 640 x 80 x 640 m at (-320, -10, -320), heightmap 1025, alphamap 1024, 6 layers, details 512
  (fern, grass, bush: 53k / 80k / 1.8k instances), 1322 trees (4 prototypes), URP Terrain Lit, pixel error 4.
- Water: pond, a thin stream, ocean + shore sheet. No river valley, no waterfall, no wetland.
- Looked modern / gamey: dense even "lawn" grass, generic round bushes, one kind of rock everywhere, flat forest floor,
  no water story. Kept: araucarias, fruit trees, interactive bushes and thickets (gameplay), cliffs, wreck.
- Scene roots found by code (not renamed): `World`, `Markers`, `Water`, `ENV_Island_Terrain`, `[Resources]`, `[Gameplay]`.

## 2. Terrain (applied, final)

Made offline (`tools/env/terrain_v2.py` + `terrain_post.py`, numpy/scipy, from the island's own v1 heights), applied by
`PrimalEnvironmentBuilder.TerrainPass` (backup `Art/Terrain/_Backup/TD_Island_before_env_v2.asset`). Same outline, no growth.
Rocky ridge with the spring (40.8 m) -> brook -> 14.5 m waterfall (lip 33.0 m) into a pool (18.5 m) -> river valley on
the old stream line with terraces, gravel bars and a ford -> pond (unchanged) -> lower river -> lagoon / wetland with mud
flats behind the +x end of the start beach -> river mouth across the beach. Narrow NE canyon (180 m, walls 8-16 m) up
to a volcanic ridge (ash shelf, basalt, lava channel, spatter vent). Open meadow / herbivore valley with wallows,
lookout knoll (25.4 m) over it, forest floor undulation, gullies from flow accumulation, clearings. Protected, same
heights: spawn beach, wreck, camp, cave, pond basin, cliff arc, fruit trees, giant footprints, captain's log.
Layers: Sand, Grass, ForestFloor, Rock (existing TL_*), Mud, SandWet (existing), Moss, DarkSoil, Ash (new, original
procedural textures `T_Moss/T_DarkSoil/T_Ash _D/_N/_M`).

Only local fix after the terrain hand-off: none. (v2.1 before hand-off: resting hollow at Route_13 deepened 0.6 m.)

## 3. Water

Bodies and WaterSource settings: table in LOCATIONS.md (spring clean, everything else fresh but dirty; SURV's component,
only configured). `Water/ENV_Stream_Water` switched off (kept) because the river replaces it. `PrimalWaterBuilder` patched
(ENV owns it): inactive / disabled WaterSources are skipped. Ocean untouched.
Waterfall (`World/Environment/Water/Waterfall`): PF/Waterfall sheet + back sheet (scrolling streaks, foam at the foot),
3 rivulets, pooled mist (28 max) and spray (90 max) particle systems on the existing VFX materials, one cliff piece moved
to the east face, 12 rock pieces + 9 pool boulders that get the always-wet mossy rock (M_Env_WetRockMossy) in the Wet step.

## 4. Vegetation

Original models (all made in Blender from code, no downloaded assets), wind vertex colours (R sway, G leaf flutter) for
PF/Foliage Wind, per-plant colour variation (world-position hash, no material copies), 2-3 LODs (last LOD no shadows):

| model | tris LOD0/1/2 | use |
|---|---|---|
| ENV_PC_TreeFern_A/B/C (6 m) | 884 / 280 / 80 | terrain trees: wet forest, deep forest, river banks |
| ENV_PC_Cycad_A/B/C (2.3-3.8 m) | 634 / 229 / 97 | terrain trees: forest edges, dry ground, volcanic soil |
| ENV_PC_GiantFern_A/B | 256 / 84 | riverside prefabs (no shadows) |
| ENV_PC_HorsetailClump | 896 / 222 | river and lagoon edges (no shadows) |
| DET_PC_GiantFern / DET_PC_Horsetail / DET_PC_Reeds | 84 / 6 / 8 | instanced terrain details |
| ENV_PC_Magnolia_A/B | 580 / 248 / 60 | flowering shrubs at forest edges |
| ENV_PC_VineCurtain_A/B | 288 / 80 | hanging moss / climbing fern laid along canyon and waterfall walls |
| ENV_PC_Roots_A/B | 972 / 360 / 128 | big exposed roots at old araucarias |
| ENV_PC_FallenTrunk_A/B | 702 / 278 / 43 | fallen trunks on the forest floor |
| ENV_PC_MossRock_A/B/C, ENV_PC_BasaltBoulder | 1280 / 320 / 80 | moss rocks (forest, water, pool), basalt boulder |
| ENV_PC_Basalt_A/B | 112 / 80 / 48 | basalt column groups (volcanic ridge) |
| props (skeleton 3096 / 1532 / 618, nest, fossil slab, petroglyph, ...) | see `env_pc_assets_stats.json` | storytelling |

Placement (`Vegetation`, deterministic). v1 trees (554 araucaria, 438 broadleaf, 290 old tree fern, 40 old cycad) keep
their index, position and scale; their prototype is swapped in place: all old tree ferns and cycads to the new models,
and the broadleaf (the modern-looking tree) partly to tree ferns (30-65 %, more near water and in the deep forest) and
cycads (18 %); about a third of the broadleaf stays as early flowering trees. Arg `noconvert` puts the v1 models back.
New tree ferns + cycads are appended as terrain trees after the 1322 v1 trees (TreeHarvest indices and saves stay valid), 3 m jittered grid by forest / water distance / deep forest, kept off the migration route
(5 m), water, steep slopes, gameplay objects. Details rebuilt from the v1 backup: grass x0.35 (part of it becomes low
ferns: a fern prairie instead of a lawn), bushes x0.5, new horsetail beds, reeds in the wetland, giant ferns in wet forest.
Interactive bushes, thickets, resource nodes and `[Gameplay]` are only avoided, never moved.
Draw cost: one shared material per surface (M_Env_Foliage, M_Env_FoliageLow, trunk materials), instancing on, SRP batcher
compatible, LOD fade off, ferns / clumps / curtains cast no shadows, details instanced. The trunk / foliage normal maps
were generated but PF/Foliage Wind has no normal input: unused (not in builds).

## 5. Storytelling (`World/Environment/Storytelling`)

Kill site (17.5 m skeleton, scattered bones), snapped trees and a claw-marked snag along the predator path, a dried
blood trail toward the skeleton, a line of big three-toed prints (the scene's own footprint mesh + M_FootprintMud),
ground nest with eggs, bones in the canyon wall (fossil slab facing the gorge), OLD HUMAN CAMP (burnt fire ring, collapsed
lean-to, tally marks facing the fire, a worn stone chopper), carved spirals / figures by the spring and beside the cave,
wreck planks along the beach (plus the existing debris prefabs), two look-at spots (knoll view, spring).
18 Examinables, ids = discoveryId = SaveId, verb "Examine" / "Look at", displayName left default so STORY's texts give
the name and thought (StoryIds already maps `env_*`). Ids in LOCATIONS.md; the Story log prints each id with its final
position.

## 6. Volcano

The landmark volcano stays offshore (unreachable scenery, unchanged). The island's `volcano` zone is the volcanic ridge at
the top of the canyon: Ash layer + basalt groups (Vegetation), a 1-1.5 m lava stream in the carved channel (PF/Lava:
2 texture samples, opaque, HDR glow for bloom; vertex colour = heat, so it crusts over toward the cooled front), a lava
pool in the vent pit, ~11 glowing cracks across the ash (M_Env_LavaCrack, mostly crust), thin vent smoke (18 particles
max), 2 warm point lights without shadows, a small heat shimmer over the vent (existing M_VFX_HeatShimmer, PF/Heat
Shimmer, no refraction). The Volcano step reports whether the landmark's heat haze is set (it needs
`VolcanoLandmark.hazeMaterial`, set by `PrimalVolcanoBuilder.Atmosphere`).

## 7. Wet surfaces

Global `_PF_Wetness` (WORLD's WeatherManager, 0..1 with drying lag). Readers:

- **PF/Wet Surface** (new): the shared M_Rock, M_Bark, M_Driftwood, M_ShipPlanks, M_ShipBeam, M_CratePlanks,
  M_BarrelStaves switch to it in place (textures, colours, cull kept; originals recorded in
  `Art/Environment/_Backup/wet_originals.json`), plus ENV's rock / wood / bone / soil materials. Wet = darker albedo by
  porosity, higher smoothness, flatter normals, gloss on up-facing parts; optional moss by world-space up-facing mask.
- **PF/Terrain Lit Wet** (+ add pass, new): URP Terrain Lit with a wetness hook (`#define UniversalFragmentPBR
  PF_WetTerrainPBR` before URP's TerrainLitPasses.hlsl), no extra texture samples.
- **PF/Foliage Wind** and **PF/Foliage** (`_WetDarken`), **PF/Water** (rain ripples), **PlayerWetLook** (U).
Caveat: re-running `PrimalShaderBuilder` WindSplit / WindRevert would put M_Bark back on its old shader (run `Wet` again).
ENV plants already use PF/Foliage Wind and do not need WindApply / WindSplit (WindSplit would only make `_Wind` copies of
their materials with the same numbers).

## Performance notes

- Terrain has 9 layers now = 3 terrain passes (URP draws 4 layers per pass). If the frame budget needs it, merging
  SandWet into Sand (or Moss into ForestFloor) gives 8 layers / 2 passes with no height change (splat only).
- New trees are terrain trees (instanced), new details instanced, placed prefabs share ~15 materials. Lights: 2 point
  lights, no shadows. Particles: waterfall 118 max, vent 22 max.

## Requests to other agents

- **RES**: re-run `PrimalResourceBuilder.Build` ([Resources], World/Resources, Markers/ResourceAreas were placed on the old
  terrain; 23 nodes in water after the terrain pass).
- **AI**: re-snap `[Gameplay]/[Dinosaurs]` and `Markers/Habitats` (3 dinosaurs in water, HAB_ApexPredator was 6.5 m off);
  herds can use `Markers/Migration/Route_00..13`; the nest is at `Markers/Zones/nest`.
- **STORY**: texts for the 18 `env_*` ids (LOCATIONS.md); mission positions can read `Markers/Zones/<id>`.
- **WORLD**: waterfall / river audio at the lip (94.0, 33.0, -169.6) and the impact (93.9, 18.6, -167.2); volcano hazard /
  heat at `Markers/Zones/volcano` (the island's ridge, not the offshore landmark); ZoneManager can read `Markers/Zones`.
- **SURV**: WaterSource settings as in LOCATIONS.md (only the spring is clean). Please say if you want other values.
- **Lead**: run the ordered list above when the editor is open (or wake ENV).
