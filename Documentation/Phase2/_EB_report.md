# ENV-B (EB_) Phase 2 report: zones 4 + 5, 2026-10-01

## Result
- Both zones are built, idempotent and saved.
- `FernFoothillsCheck` (EBf_24): **0 problems**.
- ConsoleCheck (EBf_26): 107 entries, **0 errors, 0 warnings**.
- No PlayMode, no git, no deletes. No other agent's files or groups touched, no resource nodes moved.
- Bridge: the Lead held the lock during this pass (EBf_* ids, no acquire / release by EB).

## Files (all EB-owned)
- `Assets/_Project/Scripts/Editor/PrimalZonesBuilder.FernForest.cs`
  - zone 4 builder;
  - shared EB helpers: trails, scatter with rejection stats, prefab / collider footprint, corridor clearing;
  - the read-only `FernFoothillsCheck`.
- `Assets/_Project/Scripts/Editor/PrimalZonesBuilder.Foothills.cs`: zone 5 builder and the landmark fit.
- Earlier versions are kept in `Tools/_eb_staging/v2/`.
- New assets:
  - `Prefabs/Environment/Zones/DET_EB_DryGrass.prefab`;
  - `Art/Environment/Materials/Zones/M_EB_DryGrass.mat` and `T_EB_DryGrass.png` (the grass atlas recoloured to straw / brown);
  - terrain backup `Art/Terrain/_Backup/TD_Island_before_EB.asset` (the baseline for every re-run);
  - tree records `Tools/_zones/EB_fern_trees.json` and `EB_foothills_trees.json`.

## Commands
| Command | What it does |
|---|---|
| `PrimalZonesBuilder.FernForest` | arg `dry` = survey only; `clearing=x,z` moves the clearing |
| `PrimalZonesBuilder.Foothills` | arg `dry` = survey only; `ridge=x,z` moves the landmark spot |
| `PrimalZonesBuilder.FernFoothillsCheck` | read only |

## Zone 4: Giant Fern Forest (`World/Environment/Forest/GiantFernForest`)
Zone centre (168, 8, 20), r 70 m + 30 m blend.

- **Landmark:** ART `LM_GiantTree` under `LM_GiantFernForest` at **(160, 8.8, 24)**.
  - The collider measures 11.6 m radius at walking height.
  - The clearing is therefore 17.5 m in radius, and the trails go round the tree on a 14 m ring.
- **Terrain** (EditSplat / EditDetails from the EB baseline):
  - splat: 77834 texels of forest floor and moss, worn DarkSoil / Mud trail lines, a grass and moss clearing;
  - details: 19454 cells.
- **Detail instances in the zone** (own weight > 0):

  | Layer | Instances |
  |---|---|
  | DET_ENV_Fern_01 | 24950 |
  | DET_PC_GiantFern | 13407 |
  | DET_ENV_Bush_01 | 3035 |
  | DET_ENV_Grass_01 | 1296 |
  | DET_PC_Horsetail | 254 |

  The dense 1-2 m understory comes from these layers plus the placed clumps. Trails and the clearing are thinned to 6-30 %.
- **Own terrain trees** (`EB_fern`): 191 = 173 tree ferns, 14 cycads, 4 tall araucarias for the canopy.
  - 6 earlier slots are parked at tiny scale, because trees are only ever removed from the tail.
- **Other terrain trees moved:** 41 v1 / Phase 1 trees were moved out of the clearing or the trail lanes over the runs. Each kept its index, prototype and scale, and recorded agent trees were never moved.
- **Placed props:**

  | Prop | Count |
  |---|---|
  | Giant fern clumps (scale 1.3-2.3, about 1.2-2.1 m) | 220 |
  | Magnolia shrubs (2.3-3.2 m) | 90 |
  | Fallen trunks (ENV_PC_FallenTrunk A 5 / B 3) | 8 |
  | Moss rocks | 16 |
  | Root plates | 2 |
  | Vine curtains on tree fern trunks | 17 |

  Fallen trunks and moss rocks were 0 before because they shared a spacing grid with the 310 understory props. They now have their own grid, and PFB_ENV_FallenLog_01 is the fallback.
- **Trails for AI** (`Trails/<name>/WP_nn`, each waypoint faces the next):

  | Trail | Half width | Length | Max slope | Waypoints (x, z) |
  |---|---|---|---|---|
  | `Trail_A_RiverApproach` (main lane) | 1.5 m | 132 m | 12.8 deg | (96, 6) (108, 10) (120, 9) (132, 15) (144, 20), ring (146.5, 20.6) (154.7, 11.1) (167.3, 12.1) (173.9, 22.9), then (186, 22) (198, 14) (210, 8) |
  | `Trail_B_GameTrail` (from the old camp side) | 1.2 m | 153 m | 11.2 deg | (130, -46) (138, -32) (147, -16) (154, -2), ring (156.9, 10.4) (169.8, 14.1) (173.6, 27) (164.7, 37.1), then (170, 52) (178, 66) (185, 82) |
  | `Trail_C_StalkerPath` (narrow, through the thickest understory) | 0.9 m | 73 m | 13.7 deg | (100, 24) (108, 34) (118, 44) (130, 51) (142, 52) (152, 44), ends at the ring (154.8, 37) |

  Own colliders and own trees on all trails: 0. Colliders that are not mine (rocks, Phase 1 ForestDressing, dinosaurs) are listed under Requests.
- **FX anchors for WORLD** (`FX_Anchors`):
  - FX_GroundMist_01 (116, 12, 40)
  - FX_GroundMist_02 (134, 12.6, -8)
  - FX_GroundMist_03 (184, 6.9, 48)
  - FX_GroundMist_04 (194, 5.9, -18)
  - FX_Insects_01 (153, 10.6, 29)
  - FX_Insects_02 (168, 9.2, 18)
  - FX_Insects_03 (171.5, 8.2, -33.7), on a fallen trunk
  - FX_LightShaft_01 (160, 22.8, 24)
  - FX_Drip_01 (146, 13.9, 46)
- **Check (fern):**
  - 1228 objects;
  - missing refs 0;
  - 167 resource nodes in the zone + blend, 0 covered, 0 under own trees;
  - other interactables covered 0.

## Zone 5: Volcanic Foothills (`World/Environment/Volcano/VolcanicFoothills`)
Capsule (-136, 23, -125) -> (-122, 44, -216), r 40 m + 30 m blend. Everything fades to 0 at z > -100 (BONE).

- **Transition band:** forest -> dry (about z -129 to -154) -> black rock (to about -175) -> ash (to about -190) -> the Phase 1 volcanic ground.
  - Phase 1 ash is never reduced.
  - Ground within 3 m of the lava or vent is untouched.
  - Lava / Basalt / Hazards / Landmarks and the offshore volcano are not touched.
- **Landmark:** ART `LM_BlackRidge` under `LM_VolcanicFoothills` at **(-148, 44.1, -198)**, yaw offset 30 deg, steep -Z face towards the canyon exit.
  - The spot was fitted with the real colliders: of 511 poses tried, this one touches 0 canyon path / lava / gameplay probes.
  - It is 2 m east of the first spot (-150, -198), which overlapped the canyon path at (-159, -192).
- **Terrain:**
  - splat: 60931 texels;
  - details: 15227 cells, 6375 instances in the zone rect;
  - the new DET_EB_DryGrass layer (7) has 2217 instances in the zone;
  - green layers thinned upward: fern 2791, grass 969, giant fern 363, bush 14.
- **Own terrain trees** (`EB_foothills`): 4 = 3 cycads, 1 tree fern.
- **Placed props:**

  | Prop | Count |
  |---|---|
  | Dead / broken trees (PROP_PC_BrokenTree A 12 / B 12) | 24 |
  | Dry fallen trunks (ENV_PC_FallenTrunk_A 2, FallenLog 3) | 5 |
  | Basalt groups (Basalt_A 28, Basalt_B 39, BasaltBoulder 23) | 90 |

- **Canyon path:** own colliders within 2 m 0, own trees within 3 m 0. Own objects within 4 m of the lava / vent: 0.
- **FX anchors for WORLD** (`FX_Anchors`):
  - FX_AshFall_01 (-172.5, 50.6, -182.1)
  - FX_AshFall_02 (-118.2, 50.4, -181.1)
  - FX_AshFall_03 (-72.1, 54.4, -183.9)
  - FX_AshFall_04 (-142.7, 50.4, -183.1)
  - FX_Fumarole_01 (-127, 35.9, -163)
  - FX_Fumarole_02 (-93.9, 28.9, -161.4)
  - FX_Fumarole_03 (-159.8, 37.7, -163.1)
  - FX_HeatShimmer_01 (-112.1, 45.5, -232.6)
  - FX_HeatShimmer_02 (-119.5, 45, -211.7)
  - FX_DustDevil_01 (-76.9, 22.8, -141.2)
  - FX_DustDevil_02 (-146.4, 34.1, -138.2)
  - FX_SmokeDrift_01 (-148, 54.1, -198)

  The HZ_volcano / HZ_vent hazard zones are unchanged.
- **Check (foothills):**
  - 494 objects;
  - missing refs 0;
  - 227 resource nodes in the zone + blend, 0 covered;
  - other interactables covered 0.

## Fixes in this pass
- **Trail colliders** (Trail_A 8, Trail_B 7): these were the LM_GiantTree buttresses inside the old 8 m ring.
  - The ring is now taken from the tree's measured walking-height footprint.
  - A post-pass removes any own prop whose collider touches a trail probe: 0 needed this run.
- **Canyon path (3 hits):** these were the LM_BlackRidge overlapping the path. It was refitted as above, and the same post-pass covers canyon probes.
- **FX_DustDevil_02:** the band was relaxed and the spot search retries with a 0.6 x separation. It is now placed. FX_AshFall_04 uses the same retry.

## Commands (this pass)
| Id | Command | Result |
|---|---|---|
| EBf_1, 4, 8, 11, 14, 17, 21 | Refresh | after each deploy; ConsoleCheck 0 errors every time |
| EBf_3 | FernForest | first pass |
| EBf_6 | FernForest | final: numbers above |
| EBf_7 | Foothills | ridge touched the canyon |
| EBf_10, 13, 16, 19 | Foothills dry | ridge fit iterations |
| EBf_20, 23 | Foothills | final: numbers above, 0 warnings |
| EBf_24 | FernFoothillsCheck | 0 problems |
| EBf_25 | SaveScene | saved |
| EBf_26 | ConsoleCheck | 107 entries, 0 errors, 0 warnings |

## Requests
- **Lead / ENV:** Phase 1 `Forest/ForestDressing` ENV_PC_Roots_B and ENV_PC_FallenTrunk_A lie on Trail_C (11 probe hits). They are not mine. Move them, or treat Trail_C as the blocked stealth path it is.
- **RES / ENV:** PFB_ENV_Rock_Medium_03 (`Terrain/Rocks`) sits on Trail_A and Trail_B.
- **AI:** use the Trails waypoints. The Ankylosaurus herd currently stands on Trail_B.
- **WORLD:** attach mist / insects / light / drip and ash / fumarole / heat / dust / smoke VFX to the FX anchors above.

## Not done / notes
- The foothills has only 4 own trees and 5 dry trunks: dense gameplay objects and the narrow canyon / lava corridor leave few free spots, and "sparse" is intended there.
- Fern forest root plates: 2. The 10 m plates rarely fit beside the trails and nodes.
- **Protocol note:** in the first session I held the bridge lock for 26 min (13:12 to 13:38 on 2026-09-30, read-only commands only), because each tool call took minutes.
