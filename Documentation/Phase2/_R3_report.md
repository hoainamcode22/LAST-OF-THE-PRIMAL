# RES (R3_) Phase 2 report: resource nodes in the six zones (2026-10-01)

I used the bridge while Lead held the lock (ids R3_1..R3_s4) and did not release it. No PlayMode, no git, no deletes.
Final ConsoleCheck (R3_s4): **0 errors, 0 warnings**. SaveScene (R3_s3): saved.

## Files
- New: `Scripts/Editor/PrimalResourceBuilder.Phase2.cs`.
  - Bridge command `PrimalResourceBuilder.Phase2` (arg "" applies and saves the scene, "dry" only reports). It is idempotent:
    `[Resources]/Phase2` is rebuilt on every run, and a moved node is no longer covered, so a re-run moves nothing.
    The R3_r1 dry run confirmed this: 817 nodes checked, 0 to move.
  - It also holds a small `PrimalZonesBuilder` partial (`R3Init`, `R3Keepout`, ...) that reads the zone builders' corridors.
    It only reads them and edits no zone file.
- Changed: `Scripts/Editor/PrimalResourceBuilder.Data.cs`, backed up to `Tools/_backups_P2/Assets/_Project/Scripts/Editor/`. New definitions:
  - `fiber_reeds` "Reeds": fibre 2-4, by hand, knife faster, respawn 16 h. Model: DET_PC_Reeds clump.
  - `stone_basalt` "Basalt chunks": stone 2-4, by hand, pick faster, respawn 36 h. Model: ENV_PC_Basalt_A at 0.75 m.
  - New node prefabs `Resource_Fiber_Reeds` and `Resource_Stone_Basalt`.
- Logs in `Documentation/Phase2/Logs/`:
  - `R3_phase2_build_run1.txt`: the 17 moves and the first placement;
  - `R3_phase2_build_run2.txt`: the 4 trail moves;
  - `R3_phase2_build.txt`: last run;
  - `R3_phase2_build_dry.txt`.

## 1. Covered nodes fixed (21 moved, 0 without a spot)
A node counts as covered when any of these is true:
- the zone checks' own test: a solid zone collider in a 0.55 m sphere 0.5 m above the node;
- it is inside the **mesh footprint** of LM_FossilSkeleton: its mesh bounds in its own frame, 28.5 x 16.4 m at scale 0.7, yaw 75, at (-114, 15.5, -30);
- it is under the colliders of LM_BlackRidge, LM_RockRidge or LM_AncientFallenTree (raycast from above);
- it is within 2 m of a Bone Valley prop;
- it is a solid node on a fern forest trail.

Each covered node moved to the nearest free, dry spot (1.5-9 m away). The new spot keeps every corridor clear. Rocks keep their sink.

**Foothills, the 6 from the check:**
- Rock_Large_01 (-142.6, -211.5) -> (-145.6, -211.5);
- Rock_Large_01 (-166.2, -188.4) -> (-167.8, -194.2);
- Stone_Medium_02_0120 -> (-156.5, -202.1);
- Rock_Large_01 (-162.1, -133.8) -> (-160.9, -129.5);
- Rock_Large_03 (-130, -165.4) -> (-134.5, -157.6);
- PFB_RES_Stone_01 (-74.3, -137.2) -> (-75.7, -137.8).

**Foothills, 6 more under the BlackRidge colliders:**
- Rock_Large_02 (-143.6, -194.7);
- Stone_Small_0121;
- LongGrass_0151;
- Stone_Medium_0152;
- Stone_Medium_0153.

**Bone Valley, the 2 from the check:**
- Rock_Medium_01 under BrokenTree_0 -> (-104.4, -47);
- SmallBush_0199 under Skull_Entrance -> (-94.4, -62.8).

**Fossil footprint, 4 moved:**
- Bush_053_Berries -> (-106.3, -20.9);
- ThicketBush_25_2_Berries -> (-119.6, -45.2);
- Rock_Medium_02 (-117, -29.4) -> (-125.4, -26.4);
- Fiber_Fern_0167 -> (-124.7, -28.1).

BONE's list also named Rock_Medium_03 and Fern_0168. Those were only inside BONE's search rectangle plus margin, not inside the fossil's mesh footprint, so I left them where they are.

**Fern trails, 4 solid nodes moved off them:**
- 2 ENV Rock_Medium_01 on Trail_A;
- Rock_Small_01 on Trail_B;
- Stone_Medium_0448 on Trail_A.

## 2. New nodes: 73, under `World/Gameplay/Resources/[Resources]/Phase2/<Zone>`
Save ids are fixed per slot (`rn_p2_*`), so saves stay compatible.

| Zone | Nodes | Where |
|---|---|---|
| Deep Water Cave | 15 + 4 fish | All 12 RES_Anchors filled, each node 1.1-4.7 m off the passage centre lines, on the cave floor, not over water. Stone_0..2: 2 small stones each. Flint_0..1 and Clay_0..1: a small stone each. Bones_0..1: bone piles. Hidden chamber: 2 bone piles + 1 medium stone. Fish at AI_Fish_Pool_0..2 (bed 1.45-1.69 m deep) and AI_Fish_AlcovePool_0 (0.97 m). |
| Prehistoric Wetland | 9 + 2 fish | 3 nodes at each reed bed (FXA_Wetland_Insects_Reeds_0..2: (123, 169), (99, 187), (87, 157)): 2 Reeds + 1 Fibre plant, 0.5-3.5 m from the water, dry ground. Fish in the east shallows (108.5, 177.5) and (122.5, 155.5), 0.2-0.23 m deep. |
| Bone Valley | 5 | Bone piles ("Take Bones"): 2 by the rotting carcass, 2 at the old pile by the canyon mouth, 1 by the fresh carcass. All more than 2 m from BONE props and off the predator path. |
| Volcanic Foothills | 18 | 6 clusters on the black rock band (band value 0.49-0.64), each 2 basalt chunks + 1 small or medium stone. Centres: (-120, -155), (-170, -170), (-100, -155), (-155, -160), (-130, -170), (-80, -155). Off the canyon path and lava, out of the BlackRidge footprint. |
| Giant Fern Forest | 20 | 10 clusters along the trail edges, each 1 fern or fibre plant + 1 branch, half width + 2.6 m to the side. Trail_A 4, Trail_B 3, Trail_C 3. |

I added no flint or clay items, because no recipe uses them. The cave flint and clay anchors hold loose stone instead. SURV would need to add the items and recipes first.

Keep-outs applied to every new spot:
- migration route 4 m, wallows 6 m, knoll view lines;
- fern trails (half width + 0.8 m) and the fern clearing;
- predator path 2.5 m, canyon path 2.5 m, lava 5 m;
- landmark footprints, the player spawn 25 m, the camp / old camp / nest;
- in the cave, the CAVE walk lines (1.1 m from the centre).

Lagoon fish: 2 of 3 placed. Only 40 cells were 0.2-0.8 m deep with water all round, and shoals stay 10 m apart.

## 3. Checks (final)
| Check | Result |
|---|---|
| Phase2 own check (R3_p1, R3_r1) | 73 new + 4 moved: off the ground 0, in water 0, covered 0, on a corridor 0. Nodes near the zones still covered: 0. Interactables inside the fossil mesh footprint: 0 |
| FernFoothillsCheck (R3_q1) | Covered nodes: fern 0 of 167, foothills 0 of 227. Was 6. |
| BoneValleyCheck (R3_q2) | Covered nodes 0 of 140 (was 2). Nodes within 2 m of a BONE prop: 0 (was 1). |
| ValleyCheck (R3_q3) | VALLEY CHECK OK, 345 nodes, 0 covered |
| WetlandCheck (R3_q4) | WETLAND CHECK OK, 106 nodes, 0 covered |
| PrimalCaveBuilder.Check (R3_q5) | CAVE CHECK OK: 828 probes, blocked 0, holes 0, low 0 (the new stones do not block the walk lines) |
| ConsoleCheck (R3_s4) | 0 errors, 0 warnings |

Problems left in the checks are not resource nodes (owners named):
- EB (FernFoothillsCheck, 3 problems): own colliders on Trail_A (8) and Trail_B (7), and 3 on the canyon path. The trails' "other colliders" are now only dinosaurs and ENV dressing.
- BONE (BoneValleyCheck, 2 problems): the Examinables Ribcage (BV_Carcass_Old) and Skull_Entrance are covered by BONE's own OldBones_0 / BoneLine_4.

## Requests
- EB: clear your own props from Trail_A, Trail_B and the canyon path (FernFoothillsCheck).
- BONE: move the two covered Examinables. The fossil's mesh footprint is now free of interactables. BONE's own search rectangle (with margins) still counts Rock_Medium_03 and Fern_0168, so keep the forced placement.
- SURV: flint / clay items, if wanted, with uses; then I can switch the cave anchors to them.
- Lead: devices kill background jobs when a call ends, so I queue commands with `command.json` and poll the result file.
