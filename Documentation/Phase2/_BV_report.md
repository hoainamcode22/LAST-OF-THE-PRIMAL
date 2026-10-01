# BONE (BV_) Phase 2 report: Zone 3, Bone / Carcass Valley (2026-09-30)

Scope: predator_territory (-105, 21, -76), r 50 + 25 m blend, the kill-site basin, the massif's north slope and the canyon
mouth (z > -100). Content only under `World/Environment/Forest/BoneValley` (+ terrain splat / details inside my OwnWeight).
ENV's Storytelling kill site (GiantSkeleton, Bones_0..2, BloodTrail, TheropodTrail, ClawSnag, BrokenTrees) is untouched.
No PlayMode, no git, no deletes. Verification: compile + ConsoleCheck + builder logs.

## Files (all new, mine)
- `Scripts/Editor/PrimalZonesBuilder.BoneValley.cs`: partial of ENV-A's core (core not edited). Bridge commands
  `PrimalZonesBuilder.BoneValley "[terrain][;props][;rebake][;force]"` (none = both) and `PrimalZonesBuilder.BoneValleyCheck ""`.
  Idempotent: clears and rebuilds its 7 subgroups; terrain edits start from the BV backup.
- `Scripts/World/ZoneCarcass.cs` (runtime, new): marks every placed body (`ZoneCarcass.All`, `stage` Fresh / Rotting / Old /
  Ancient, `id`, `creatureId`, `radius`). On a Fresh body it calls `Carcass.Setup` at Start with preset loot (a scene Carcass
  without Setup has no items and would throw on the first cut) and saves what is left in save section `zone_carcasses`
  (by id; a butchered body stays gone after loading). `expireHours` 100000: it does not sink by the clock.
- Assets: `Art/Environment/Generated/BoneValley/ME_BV_Dead_Parasaurolophus.asset` (26267 verts), `ME_BV_Dead_Triceratops.asset`
  (30151 verts): the Death clip's last frame baked from the DINO fbx; `Art/Environment/Textures/Zones/T_BV_DragMark_D.png`
  (procedural); materials in `Art/Environment/Materials/Zones/`: `M_BV_DragMark`, `M_BV_FootprintFresh`, `M_BV_Rot_Triceratops_0..2`.
- Terrain backup: `Art/Terrain/_Backup/TD_Island_before_BV.asset` (baseline of every re-run).

## What is in the zone (scene paths under World/Environment/Forest/BoneValley)
| Group | Content |
|---|---|
| Carcasses | `BV_Carcass_Fresh` (-107.6, 14.5, -55.1): half-eaten parasaurolophus, Death pose mesh, capsule collider, **Carcass** (SaveId bv_carcass_fresh, "Butcher half-eaten carcass", knife: meat 3, hide 2, bone 2; hands: 2 meat) + ZoneCarcass Fresh, 6 fresh blood decals. `BV_Carcass_Rotting` (-117.1, 13.6, -44.2): triceratops days old, dark rot material, sunk 30 %, capsule collider, 3 bone props, 4 stains, Examinable bv_rotting_carcass. `BV_Carcass_Old` (-136, 13.3, -72) at the canyon mouth floor: bone pile (4 props), Examinable bv_old_bones. The ENV GiantSkeleton (-118, 14.4, -50) is the 4th, ancient stage. |
| Entrance | where the predator path crosses the low ridge into the basin (-98.9, 13.6, -62.1): a bone line of 5 bone props across the path, a skull placeholder beside it (Examinable bv_bone_line) |
| Tracks | 5 drag-mark decals + blood from the path (-102.5, -62.5) to the fresh body (Examinable bv_drag_marks); fresh big three-toed prints (M_BV_FootprintFresh) from the entrance to the body (Examinable bv_fresh_tracks); prints leading out from the body to the path and along it to the canyon mouth (-131, -40). 16 prints total (ENV's 22 prints on the path in are kept) |
| Bones | 14 scattered bone props over the basin (more near the carcasses, the landmark and the canyon mouth) |
| Trees | 2 PROP_PC_ClawSnag beside the path (gouges face the path) + ENV's one; 5 PROP_PC_BrokenTree_A/B |
| Landmark | see below |
| AI_Anchors | 11 scavenge, 9 predator route, 4 perches, 1 circle centre (table below) |

Totals (BV4 run): 27 bone props, 16 prints, 18 decals, 2 snags, 5 broken trees, 6 Examinables, 3 carcasses, no lights.
Decals / prints: no collider, no shadows, batching static. Bone props: no collider. Snags / broken trees: ENV prefab colliders.

Terrain (BV2 / BV4, inside OwnWeight only): splat 22945 texels: TL_DarkSoil through the zone (0.28-0.58 + up to 0.6 at the
carcasses, landmark, kill site, drag line), TL_Mud along the path (3 m), rock kept. Details (fern, grass, bush, giant fern):
5728 cells, 40-70 % kept, trampled patches x0.25, path x0.08-1, cleared at carcasses / drag line: 4912 -> 3834 instances in
the zone rect. No height edits, no terrain trees.

## Landmark (LM_FossilSkeleton, ART batch 1: 40.7 x 7.5 x 23.5 m)
**Not final.** In the current scene (run BV4_a_1, saved) LM_FossilSkeleton is placed at the fallback spot (-120, 13.6, -32),
scale 0.7, yaw 180 (exposed side faces the basin to the south), flat. A Survey of that footprint found 9 of 45 sample points on
gameplay objects or solid props (north basin bushes / thickets / resource nodes), so it may cover some of them: it must not stay.
Cause: the zone has no 28-40 m footprint that is clear of the path, the neighbours' cores (valley core on the east, foothills core
on the massif top) and the dense interactables; the massif's north slope is only ~20 m deep between the path and the plateau.
Fix, written and staged but NOT yet deployed (the editor was busy for about 70 min, then the PC connection dropped):
`Tools/_bv_staging/PrimalZonesBuilder.BoneValley.cs` (v3) tests every candidate footprint exactly against every interactable,
resource node, story prop and solid collider near the zone. It places the prefab only on a fully clear footprint; otherwise it
places only the marker `LM_BoneValley` and logs the spot with the fewest blockers and their names.
To finish (one bridge cycle): copy that file over `Assets/_Project/Scripts/Editor/PrimalZonesBuilder.BoneValley.cs`, Refresh, Ping,
ConsoleCheck, `PrimalZonesBuilder.BoneValley ""`, `PrimalZonesBuilder.BoneValleyCheck ""`, ConsoleCheck. If the log says "no clear
footprint", the Lead / owner picks one: let RES move the few listed nodes / bushes, or accept scale 0.7 at the logged spot.

## For AI (P_): how to find things
- Carcasses: component `PrimalFrontier.World.ZoneCarcass` (`ZoneCarcass.All`, `stage`, `id`, `radius`, `HasMeat`). The fresh one
  also has `Carcass` (in `Carcass.All` at runtime, so predators with a meat drive already smell it and `NearestCarcass` finds it).
  No tags were added (TagManager has none).
- Anchors: empties with `EnvLocation` (id = lower-case name, radius, note), faced as noted:
  - `AI_Anchors/Scavenge/BV_Scavenge_fresh_0..3`, `_rotting_0..2`, `_old_0..1`, `_killsite_0..1` (face the body).
  - `AI_Anchors/PredatorRoute/BV_PredRoute_00..08`: entrance, bone_line, fresh, killsite, rotting, path_west, landmark_front,
    canyon_mouth, exit_canyon (each faces the next).
  - `AI_Anchors/Perch/BV_Perch_0..3`: tops of the snapped trees / snags (and rim points when free), face the fresh carcass.
  - `AI_Anchors/BV_CircleCentre`: 28 m above the basin, radius 26 (pteranodon circling).

## Commands (bridge ids) and results
| id | command | result |
|---|---|---|
| BV1_c_1233 / BV1_sv_1233 | ConsoleCheck / Survey zone=bone | 0 errors; zone data (layers, details, trees, ring heights) |
| BV2_c_1414 | ConsoleCheck after first deploy | 3 errors in my file (CS0103 `Child`), fixed in 1 min; BV2_c2: 0 errors, 0 warnings |
| BV2_t_1417 | BoneValley terrain | backup made, splat 22945, details 4912 -> 3878 |
| BV2_a_1422 | BoneValley | all props, carcass meshes baked, 0 warnings; BV2_c4: 0 errors, 0 warnings |
| BV4_a_1 | BoneValley (landmark search v1) | 0 warnings except "no clear footprint"; LM_FossilSkeleton placed at the fallback (-120, 13.6, -32) scale 0.7 yaw 180 |
| BV4_sv_1 | Survey points on that footprint | 9 of 45 sample points blocked by gameplay objects / solids |
| (pending) | BoneValley v3 + BoneValleyCheck | not run: editor unresponsive 16:20-17:26 UTC under P's lock, then the lock went to CAVE / EA / W, then the PC went offline (from ~18:18 UTC) |

Last ConsoleCheck with my code compiled: BV4_c_1 = 0 errors, 0 warnings (before the BV4 build); BV2_c4 = 0 errors, 0 warnings after a full build.
BoneValleyCheck (covered nodes, missing refs) has not run yet: it is part of the pending cycle.

## Requests
- WORLD: journal / discovery texts (`StoryTexts.Discovery`) for bv_bone_line, bv_fresh_tracks, bv_drag_marks, bv_rotting_carcass,
  bv_old_bones, bv_fossil_skeleton (Examinable displayName "Something", empty thought, like ENV's). Zone name / minimap:
  "Bone Valley" around (-112, -50). Optional flies / dust VFX at the rotting carcass (-117.1, 13.6, -44.2).
- AI: scavengers to the Scavenge anchors, predator visits along PredRoute, pteranodons circling BV_CircleCentre / perching on
  BV_Perch_*; optionally seed permanent TrackSigns (Kill at the three ZoneCarcass bodies, Scratch at the claw snags) so the
  tracking prompts exist here too (my prints / drag marks are static scene decals, not TrackSigns).
- ART: when PROP_P2_BoneScatter_A/B, PROP_P2_Ribcage, PROP_P2_Skull_Large prefabs are delivered, re-run `BoneValley "props"`
  (the builder picks them up by name: skull at the entrance, ribcage at the old pile, scatter props in the basin).
- Lead / RES: the basin is dense with interactables (bushes, thickets, resource nodes), which is what makes a clear 28-40 m
  footprint for the landmark hard to find (see Landmark).

## Not done / notes
- No PlayMode: butchering the fresh carcass, the save section and AI use are compiled, not play-tested.
- A butchered fresh carcass does not come back (saved as gone); a new game restores it.
- The bone line and skull use PROP_PC_Bones until ART's bone props are delivered.
- The bridge lock was very contended (EA, EB, CAVE, A2, P); my holds ran over 10 minutes twice because each device call took
  1-3 minutes of wall time, not because of long commands.
