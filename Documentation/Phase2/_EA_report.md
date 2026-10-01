# ENV-A (EA_) Phase 2 report: zones 1 + 2, 2026-09-30 / 10-01

## Result
Zone 1 MIGRATION VALLEY and zone 2 PREHISTORIC WETLAND are built by idempotent bridge commands. Both use one EA terrain
baseline. Heights are not changed. Both ART landmarks from batch 1 are placed: LM_RockRidge and LM_AncientFallenTree.

The last saved scene is from cycle EA_v11 / EA_w11 (15:50 UTC):
- WetlandCheck: OK, 0 problems.
- ValleyCheck: 1 problem. The first LM_RockRidge pose covers one branch node:
  `Resource_Wood_Branch_02_0238` at (-66.6, 11.8, 6.3).
- Console: 0 errors, 0 warnings after every cycle (last read: EA_cc11).

A fix for the ridge and fallen-tree placement (EA8) is written into Assets but NOT compiled or run. From about 17:35 UTC the
Unity editor stopped answering, then closed (no Temp folder), and the device went offline. See "Not done".

## Files (all new, ENV-A)
- `Scripts/Editor/PrimalZonesBuilder.cs`: the shared core. It is a partial static class and its API is listed at the top of
  the file. ENV-B (FernForest, Foothills) already builds on it. It holds:
  - the zones table (6 circles and 1 capsule, with blend bands);
  - `OwnWeight`: a zone fades out where a neighbour's core starts (inside radius + blend/2);
  - `BackupTerrain`: one terrain baseline per prefix;
  - `EditSplat`, `EditDetails` and `EditHeights`: result = lerp(baseline, target, OwnWeight), so a re-run gives the same terrain;
  - `SetOwnTrees`: own terrain trees kept in recorded slots. Changes are made in place and only the tail is removed, so
    TreeHarvest indices stay valid;
  - placement helpers: `BuildBlockers` (every active Interactable, ZONE_* markers, story props, footprints, player),
    `IsBlocked`, `SolidAt`, `FootprintBlocked`, `Place`;
  - `Group` / `Marker` under SceneRoots paths, `SplatSampler`, `TreeGrid`;
  - `CheckZone` and the read-only `Survey`.
- `Scripts/Editor/PrimalZonesBuilder.Valley.cs`: `Valley` ("assets" / "terrain" / "props", default all) and `ValleyCheck`.
- `Scripts/Editor/PrimalZonesBuilder.Wetland.cs`: `Wetland` ("terrain" / "props", default both) and `WetlandCheck`.
- New assets:
  - `Prefabs/Environment/Zones/DET_Z_TallGrass.prefab`: a copy of DET_ENV_Grass_01, with shadows off.
  - `Art/Environment/Materials/Zones/M_Z_TallGrass_Wind.mat`: PF/Foliage Wind with the DET_ENV_Grass_01 texture. Wind height
    1.7 m, sway 0.22, flutter 0.035, instancing on, colour variation 0.2.
  - `Art/Terrain/_Backup/TD_Island_before_EA.asset`: the terrain baseline and backup (a full copy, made once).
  - `Tools/_zones/EA_valley_trees.json`, `EA_wetland_trees.json` and `EA_valley_moved.json`: tree slot records.
- Log: `Documentation/Phase2/Logs/EA_build.txt`.

## Zone 1 Migration Valley (centre (0, 9, -35), r 95 + 30 m blend)
Scene path: `World/Environment/Forest/MigrationValley/{Rocks, Props, Landmark, FX_Anchors}`.
- Splat (110878 texels):
  - thin forest floor on the open valley floor becomes Grass;
  - DarkSoil patches (9 m noise);
  - a trampled herd trail along the migration route (DarkSoil + Mud, 1.5-5 m wide);
  - Mud in and round the three existing wallows, which are kept (mud weight 0.75 / 0.82 / 0.82);
  - SandWet + Mud on the river banks and round the ford;
  - slopes over 32 degrees are untouched.
- Details (27729 texels; instances in the zone rect went from 23338 to 73134):
  - new layer 6 `DET_Z_TallGrass`, about 1.2-1.8 m tall with wind, in 14 m clumps on the open floor;
  - short grass between the clumps;
  - ferns and giant ferns at the forest edges and in the blend band (forest to meadow);
  - horsetail and reeds on the river banks;
  - kept low or bare: the route (tall grass at 15 %), the wallows, the knoll top (short grass only) and the water.
- Terrain trees: 11 own trees (araucaria 2, cycad 4, tree fern 5), in clusters in the outer band and along the river. The
  valley already had about 300 trees.
- Knoll view:
  - 13 older trees whose crowns cut a view line were moved 8-34 m aside. Index, prototype and scale are unchanged.
  - The moves are recorded, put back and redone on each run.
  - Result: 0 trees on the lines from the knoll top to the ford, to the waterfall pool and to the meadow centre.
- Props:
  - 28 boulders (ENV_PC_MossRock_A 10, B 9, C 9);
  - 26 giant ferns (A 13, B 13);
  - 1 fallen trunk;
  - 14 horsetail clumps on the banks.
  - Nothing within 3 m of the route. Every collider prop is also checked by its whole bounds against gameplay objects.
- Landmark `Landmark/LM_MigrationValley` with LM_RockRidge:
  - Placed at (-72, 11.4, -8), yaw 90.6. Moss side (+Z) faces the valley. The ridge runs north-south on the rising west edge.
  - It is 60 m from the route and clear of the knoll view lines.
  - Known issue: the ground under the 57 x 21 m footprint varies by 4.35 m (the skirt is 2 m), and the footprint covers one
    branch node. EA8 fixes both but has not run (see "Not done").
- Checks (EA_vk11):
  - 341 resource nodes in the zone and blend band; 1 covered (by the ridge), 0 under own trees;
  - other interactables covered: 0;
  - missing refs: 0;
  - objects near the player spawn: 0.

## Zone 2 Prehistoric Wetland (centre (102, 0.4, 172), r 45 + 25 m blend)
Scene path: `World/Environment/Wetlands/PrehistoricWetland/{Props, Landmark, FX_Anchors}`. The lagoon water
`ENV_Wetland_Water` (0.85 m) is reused as it is. The flats already lie at 0.4-1.2 m, so I dug no pools and changed no heights.
- Splat (39412 texels):
  - Mud / SandWet (plus a little DarkSoil) on the lagoon bed and on the flats up to about 1.1 m above the water;
  - DarkSoil + Moss on the damp rim (1.1-4 m above the water);
  - the beach sand toward the river mouth is kept.
- Details (9851 texels; 9626 to 13266 instances):
  - reed beds in clumps, from 0.5 m deep to 0.4 m above the water;
  - horsetail on the flats;
  - ferns and giant ferns on the rim;
  - grass and bushes thinned on wet ground;
  - nothing in water deeper than 0.5 m.
- Terrain trees: 4 own trees (tree fern 3, cycad 1) on the drier rim.
- Props:
  - 9 dead snags (PROP_PC_BrokenTree_A/B; ART's PROP_P2_DeadSnag_A/B are used automatically once they exist);
  - 4 root plates (ENV_PC_Roots_A/B);
  - 2 fallen trunks (ENV_PC_FallenTrunk_A/B), partly on the flats;
  - 16 horsetail clumps at the water line;
  - 12 giant ferns on the rim.
  - Nothing stands in water deeper than 0.6 m.
- Landmark `Landmark/LM_PrehistoricWetland` with LM_AncientFallenTree:
  - The root end is on the west shore at (80, 2.1, 188). The trunk points (0.7, 0, -0.7) into the lagoon.
  - The prefab is turned so its local +X runs along the marker's +Z. Its pivot is at the water line (y 0.85) at
    (92.4, 0.9, 175.6), and the crown end is at (105.8, 0.6, 162.2), under water.
  - It covers 0 nodes. The builder logged it as a fallback spot, because the sampled footprint test was too strict.
    EA8 replaces that test with an exact one.
- Checks (EA_wk11): WETLAND CHECK OK. 95 nodes in the zone and blend band; 0 covered, 0 under own trees; interactables
  covered 0; missing refs 0.

## FX anchors for WORLD (empty transforms)
- `MigrationValley/FX_Anchors`:
  - FXA_Valley_Pollen_Meadow (-2, 10.7, -28)
  - FXA_Valley_Mist_Ford (84.6, 11.4, -97.5)
  - FXA_Valley_Dust_Trail (18, 10.9, -72)
  - FXA_Valley_Pollen_Knoll (-40, 27.4, -92)
  - FXA_Valley_Insects_Wallow_0 (16, 9.7, 2), _1 (-24, 9.7, -50), _2 (4, 9.7, -76)
- `PrehistoricWetland/FX_Anchors`:
  - FXA_Wetland_Fog_Lagoon (100, 1.5, 168), FXA_Wetland_Fog_West (83, 1.5, 175), FXA_Wetland_Fog_East (118, 1.5, 174)
  - FXA_Wetland_Mist_RiverMouth (106, 1, 200)
  - FXA_Wetland_Insects_Landmark (84.2, 3.6, 183.8)
  - FXA_Wetland_Insects_Reeds_0 (123, 2.1, 169), _1 (99, 2.1, 187), _2 (87, 2.1, 157): the three densest reed beds.
- The anchors are rebuilt on every run, so WORLD should find them by name, not keep references to them.

## Draw distances (sight lines)
- Scene terrain (unchanged): detail distance 90 m, density 1, tree distance 450 m, tree billboard 450 m, basemap 300 m.
- At runtime `Core/TerrainQuality.Apply` sets these per quality level (Low / Medium / High / Ultra):
  - detail distance 35 / 55 / 80 / 110 m;
  - tree distance 450 / 700 / 1000 / 1500 m;
  - billboard 60 / 90 / 130 / 180 m.
- Herds are GameObjects, so these limits do not hide them. The knoll-to-ford line is about 125 m, so herds at the ford are
  seen over short-grass splat at every level. Tall grass fades out at the detail distance.

## Commands (bridge ids) and results
- EA1 deploy (core), then EA_c1 ConsoleCheck: 0 errors.
- EA_sv1 / EA_sv2 `Survey` (valley, wetland): layers, detail and tree prototypes, the splat share (valley core: Grass 72 %,
  ForestFloor 18 %) and the ring heights.
- EA3 deploy, then EA_c3: 0 errors.
- EA_v4 `Valley`: made the baseline backup. Tall grass is layer 6. Details in the rect went from 23338 to 73134.
- EA_w5 `Wetland`: details 9626 to 13266.
- EA_c5: 0 errors, 0 warnings.
- EA4 deploy (tuning), then:
  - EA_v7 / EA_w7: re-runs gave the same splat and details (73134 to 73134, 13266 to 13266). Idempotency confirmed.
  - EA_vk7: 1 node covered by a fallen trunk. This led to `FootprintBlocked`.
  - EA_wk7: OK.
  - EA_cc7: 0 errors.
- EA5 deploy (knoll-view tree moves, footprint test):
  - EA_v8: 13 trees moved. Collider props were all rejected because collider bounds are invalid before a physics sync.
    Fixed in EA6.
- EA6 deploy:
  - EA_v10 / EA_w10: ART landmarks placed.
  - EA_vk10: VALLEY CHECK OK.
  - EA_wk10: 3 nodes covered by the fallen tree, because it was not yet turned to ART's axis.
  - EA_cc10: 0 errors, 0 warnings.
- EA7 deploy (landmark axes and poses, footprint exclusion zones):
  - EA_cc11: 0 errors.
  - EA_v11: ridge warnings (range 4.35 m, no clear yaw at the fixed spot).
  - EA_w11: fallen tree turned, pivot at the water line.
  - EA_wk11: OK.
  - EA_vk11: 1 node under the ridge.
  - Scene saved.
- EA8 (ridge spot search on the west edge; exact oriented-rectangle blocker test for the ridge and the fallen tree):
  extracted into Assets at 18:03 UTC. Its Refresh never ran: CAVE's Refresh (CVr_175112) was still queued, the editor had
  closed, and the device then went offline. I released the lock at 18:07.

## Requests
- Lead / whoever reopens Unity: after EA8 compiles, run this cycle:
  1. Refresh, then ConsoleCheck.
  2. `PrimalZonesBuilder.Valley "terrain;props"`, then `PrimalZonesBuilder.Wetland`.
  3. `ValleyCheck`, then `WetlandCheck`, then ConsoleCheck.

  Expected: the ridge moves to the flattest clear pose on the west edge, and 0 nodes are covered. If the console shows errors
  in PrimalZonesBuilder.Valley.cs or .Wetland.cs, `Tools/pf_up_EA7.zip` holds the last compiled version of both files.
- Lead: from about 16:20 to 17:27 UTC the bridge did not process commands. `Temp/__Backupscenes` existed, which looks like
  Play mode. Holds were long: EB 13:13-13:39, BONE 14:08-14:40, P 16:10-17:49, CAVE 17:49-18:02.
- Lead: my own holds also went over 10 minutes (12:48-13:02, 13:39-14:08, 14:40-14:51, 15:45-15:54). Compiles take about
  3 minutes, and each device call took 1-5 minutes in wall time.
- WORLD: fog, insect and mist VFX at the FX anchors above. Re-run `PrimalAtmosphereBuilder` if it reads zone markers.
- RES: new-zone resource nodes may go anywhere outside the landmark footprints. Keep the route, the wallows and the knoll
  view lines open.
- Lead / WORLD (optional): the Medium quality detail distance of 55 m hides the tall grass past 55 m. 80 m or more would make
  the valley read better.

## Not done / notes
- EA8 is not compiled or run (see above).
- No heights were changed and no lagoon pools were dug.
- Valley fallen trunks: only 1. The edge band has little flat forest floor clear of gameplay objects.
- Only 11 own trees in the valley. The existing trees already frame the edges, and extra trees would narrow the sight lines.
- Overlap with BONE: re-runs rewrote about 180 detail instances in the shared blend band. BONE had changed them after my
  baseline, in texels where my OwnWeight is above 0.
- No PlayMode, no git, no deletes. I edited only my own new files and assets, so no backups of existing files were needed.
