# AI agent report (P_), Phase 2 World Exploration: wildlife of the six zones (2026-09-30 / 10-01)

Scope kept: existing species only (no new species, no new art), no git, no deletes, no PlayMode run by me. Every existing
file I changed has a backup in `Tools/_backups_P2/Assets/_Project/Scripts/...` (made before the first copy). Staging and
deploy script: `Tools/_P2_stage/` (`_deploy.sh`, `_orig_md5.txt`, `_deployed1_md5.txt`).

## 0. State in one paragraph

Code: deployed and compiled on the PC (lock 16:10 UTC; Refresh `P2_r_161250`; the three PrimalFrontier assemblies rebuilt
16:15; ConsoleCheck `P2_c_172924`: **0 errors, 0 warnings**). The scene was never baked by PrimalWildlifeBuilder (no
HerdGroup in `Island_VerticalSlice.unity`), so the whole plan, including every Phase 2 group and routine below, is laid
out at runtime by `DinosaurSpawner.Awake` (runtime fallback). The owner's own Play session after 16:19 ran it: the log
(`Library/PrimalBridge/console.txt`) shows every herd, territory, zone group and routine applied, no exception.
NOT DONE: the bake (`PrimalWildlifeBuilder.Build ""`), `Phase2Check` and the updated cave no-go circles: the editor was
in Play mode when I finally had the lock, and from about 18:20 UTC the PC was offline for this session. The runtime
fallback already gives the game the same layout, so the owner sees Phase 2 wildlife when playing now.

## 1. Per zone

Positions are Unity world metres, from the runtime layout log (fallback = the named anchor was not in the scene yet).
Scene path of every creature: `World/Gameplay/Wildlife/[Dinosaurs]/<group>/<name>`.

### 1. Migration Valley (centre (0, 9, -35), r 95)
- Herds (unchanged plan, checked in the valley): crested herd Parasaurolophus 9 (home Route_00 (-14, 8.9, 12), migrates
  over Markers/Migration Route_00..13 every 2nd day, ford stop at Route_06), horned herd Triceratops 5 at (18, 9.8, -62).
- Far visibility (new, `PerceptionConfig` "Far herds"):
  - herd members seen by a camera stay in the far tier out to `herdVisibleDistance` 340 m (were frozen beyond 200 m: no
    movement, Animator off): they keep walking every frame and their Animator is stepped by hand at `farAnimHz` 20 Hz
    (120-200 m) and `farHerdAnimHz` 10 Hz (200-340 m); thinking stays at 1 s. Out of view or beyond 340 m: frozen as before.
  - Animator cullingMode: CullUpdateTransforms (set in Awake as before; the far tiers switch the Animator off and step it
    manually, so the pose updates whether the renderer is visible or not). SkinnedMeshRenderer.updateWhenOffscreen stays
    false (the pose is only needed when seen). LODGroup: at Start every instance's last LOD threshold is lowered (instance
    only, prefabs untouched) so herbivores stay drawn to `herdCullDistance` 380 m and other land creatures to
    `creatureCullDistance` 260 m at the main camera FOV and QualitySettings.lodBias. No distance despawn / disable exists
    in DinosaurSpawner (checked): the only removal is death + sinking.
- Predators observing from the edge: the Carnotaurus (home (40, 11.6, -10)) afternoon patrol starts with a 45 s watch
  (Observe, head on the nearest herd) at the valley's west edge (-48, 10.6, -46), then crosses to Bone Valley (below).
  The existing herd watching (30-42 m) and test rushes stay.

### 2. Prehistoric Wetland (centre (102, 0.4, 172), r 45)
- New group `Wetland_Parasaurolophus` "reed trio": 3 Parasaurolophus (cloned from the crested herd's animal), HerdGroup
  with its own day, home (133.9, 3.5, 152.3) on the inland (east) shore. A herd of 3 is never hunted (huntMinHerd 3).
- Lone raptor at dusk: routine `raptor_wetland_dusk` on `Velociraptor/Velociraptor_2` (the third of the fern pack):
  17.5-20.5 h, every day, chance 0.6: walks a ground path from the pack's home to (140, 3.5, 152) (watch 30 s) and
  (132, 2.1, 192) (watch 25 s), then home (25 points, ~415 m). Both stops stay > 100 m from the player's start.
- Amphibious visitor: routine `spino_wetland_visit` on `Spinosaurus/Spinosaurus_0` (home stays the grassland terrace
  (150, 12, -118)): every 2nd day from day 2, start 11.5-15 h, chance 0.8: walks down along the river (ground path,
  36 points, ~707 m in all), wades and fishes at 3 lagoon spots (120, 1.0, 176) 70 s, (112, 0.7, 186) 60 s,
  (126, 1.2, 165) 60 s (head-down Eat loop, WaterSplash strikes at the water), then walks home. Shallow water within
  22 m of a wade stop counts as ground only during the visit (DinosaurController.SetWade); it never camps at the beach:
  predators also keep out of the new `start_beach` no-go area (55 m around the spawn point, destinations only).
- Small herbivores (small ambient creatures) and fish: there is no small-animal species or model in the project (the only
  AmbientCreature species are Pteranodon and Mosasaurus; fish exist only as RES's `Resource_Fish_Shoal` nodes with a
  ripple / fin cue). Nothing invented: see Requests (RES fish shoals in the lagoon).

### 3. Bone Valley (centre (-105, 21, -76), r 50)
- Uses BONE's anchors under `World/Environment/Forest/BoneValley/AI_Anchors` (found at runtime): `BV_CircleCentre`,
  `BV_Scavenge_*` (fresh, rotting, old, killsite), `BV_Perch_*` (snag tops and rim), `BV_PredRoute_00..08`; the fresh
  carcass is BONE's `BV_Carcass_Fresh` (a real `Carcass`).
- New group `BoneValley_Pteranodon`: 2 Pteranodons (cloned) circling BV_CircleCentre (-114.2, 14.7, -49.8) r 26 / 35 at
  28 / 33 m. Routines `ptera_bone_scavenge_a` (8-17.5 h, 3 landings a day) and `_b` (9-18 h, chance 0.85, 2 landings):
  every 150-420 s one glides down next to a body at a BV_Scavenge point (a Carcass there: 2.5-4.5 m from it) or onto a
  BV_Perch (snag top / rim, kept at its height), stays 25-70 s (perch x1.5), takes off; it never lands with the player
  within 33 m and lifts off when the player comes within 22 m (15 points each).
- New group `BoneValley_Velociraptor`: 1 raptor (cloned), den (-72, 14.7, -104) r 20 (fallback; no den anchor).
  Routine `raptor_bone_feed`: 15-18.5 h, chance 0.6: walks to BV_Scavenge_fresh_1 (-103.1, 14.2, -57.5), feeds 50 s at
  the fresh carcass (EatingCarcass, counted as a scavenger on the Carcass), back to the den (7 points, ~116 m).
- Carnotaurus patrol `carno_valley_bone_patrol` on `Carnotaurus/Carnotaurus_0`: 13.5-17.5 h, every day, chance 0.7:
  valley edge watch, then BONE's BV_PredRoute_00..08 in order (entrance, bone line, a 20 s sniff at the fresh carcass,
  kill site, rotting carcass, path west, landmark foot, canyon mouth, canyon exit), home (26 points, ~459 m).
- First visit readable: the Rift Tyrant's home moved off the kill site: `predator_territory` + (-23, 0, -22) =
  (-126.1, 32.9, -95.7), about 45 m from the fresh carcass (was (-113, -64), 8 m from it), home radius 30: evidence first,
  the tyrant can still wander in.

### 4. Giant Fern Forest (centre (168, 8, 20), r 70)
- Velociraptor pack of 3 (home (116.2, 12, 0.8), the forest's valley edge). Pair stalking: `raptor_fern_stalk` on
  Velociraptor_0 (15.5-22 h, every day, loops, 0.85 x walk) walks ENV-B's `Trail_C_StalkerPath` WP_nn forwards, waits
  25 s in ambush at the trail's east end (142, 10.8, 52), walks it back, round again; `raptor_fern_pair` on
  Velociraptor_1 follows it 5 m behind, 2.5 m to the side, and goes home when the leader does. At runtime the trail objects were not in the scene yet (ENV-B not deployed), so the patrol used its fallback points
  (100, 24) -> (142, 52) -> (100, 24) with a ground path (10 points, ~149 m); a re-run of Build after ENV-B picks the WP_nn up.
- Zone visibility (`PerceptionConfig.visibilityZones`, entry `giant_fern_forest`: centre anchor `fern_forest_centre`
  or (168, 8, 20), r 62 + 28 m fade): creature sight of the player x0.6 when either is inside; creature-to-creature
  noticing (herd <-> hunter distances in HerdGroup.Threats, prey search in HuntDirector.PickPrey) x0.6; a crouched
  player in dense plants (terrain detail density against the zone's 90th percentile >= 0.45, a standing tree within
  3.5 m, or inside a bush: BushInteraction.PlayerBush / CoverMap) gets another x0.55 on top (x0.33 in all). This stacks
  with the existing bush / thicket / tree cover of PlayerSignature. Motion flashes use the same multiplier.

### 5. Volcanic Foothills (canyon upper half -> volcano ridge)
- Sparse: new `Foothills_Pteranodon` 1 flyer circling (-130, 37.6, -168) r 48 at 44 m; new `Foothills_Ankylosaurus`
  1 loner grazing low at (-140, 31.4, -110), home radius 16.
- Heat: every HazardZone ring counts (WildlifeConfig): no destination, placement or perch in the warm ring or closer
  (`heatAvoidLevel` 1); a calm walking creature turns away at the hot ring (`heatHardLevel` 2); even a chase turns at the
  dangerous ring (`heatChaseLevel` 3). Scene rings: volcano warm 60.75 / hot 31.5 / danger 9.9 m, vent 20 / 11 / 5 m,
  5 lava rings 13 / 7 / 3.5 m. Ground paths for routines never cross the warm ring.

### 6. Deep Water Cave (CAVE's through-cave)
- No large dinosaurs inside: hard no-go areas (block every state but fleeing) at the three ways in: old grotto
  (-18, 18.4, -134) r 13, east mouth on the waterfall pool (90, 19.45, -157.4) r 7, back door (12.4, 22.5, -141.4) r 8.
  The chamber and passages lie under the cliff, and land creatures walk the terrain surface, so they cannot get under it.
- Small creatures at CAVE's `AI_SmallCreature_*` anchors (lizards, crickets, scavenger): none, no such species exists.
  Bats: none (no existing creature fits). Fish at `AI_Fish_Pool_0..2`, `AI_Fish_AlcovePool_0`: request to RES.

## 2. Code (all data-driven)

New: `AI/WildlifeZones.cs` (no-go areas, heat rings, zone visibility, AI anchor lookup), `AI/WildlifeRoutine.cs` (patrol /
visit / scavenge component), `AI/WildlifePlanZones.cs` (partial of WildlifePlan: `ZoneGroups` + `Routines` tables, apply,
A* ground paths on a 4 m terrain grid), `Editor/PrimalWildlifeBuilder.Phase2.cs` (`Phase2Check`).
Changed: `AI/DinosaurController.cs` (far herd tier, LOD cull extension, no-go / heat in wander points and steering,
wading, routine API `RoutineReady / RoutineWalk / RoutineAct / SetWade`, `Goal.Routine`), `AI/AmbientCreature.cs`
(`VisitGround` landings, perches skip no-go / heat), `AI/DinoSenses.cs` (zone sight multiplier), `AI/HerdGroup.cs` and
`AI/HuntDirector.cs` (creature sight multiplier), `AI/PerceptionConfig.cs` (appended "Zone visibility", "Far herds"),
`AI/WildlifeConfig.cs` (appended heat levels, `noGoAreas`, routine numbers), `AI/WildlifePlan.cs` (partial, zones step in
Apply, no-go aware `Ground`, apex home), `World/Carcass.cs` (scavenging hooks: `Scavengers`, `ScavengerArrived / Left`,
`Scavenge(n)`, `NearestBody`), `Editor/PrimalWildlifeBuilder.cs` (partial, zone checks after Build).
CreatureSave: unchanged format. New creatures have unique names (`<Species>_<Zone>_<n>`), so old saves load (they just do
not list them) and new saves match by name + species; routines are not saved (they restart from the clock after a load).

## 3. Commands and results

| id | command | result |
|---|---|---|
| (deploy) | `Tools/_P2_stage/_deploy.sh` (md5 check against the staged originals, backups) | 14 files written |
| P2_r_161250 | PrimalEditorBridge.Refresh | refreshed; assemblies rebuilt 16:15 UTC |
| P2_p_162036 | PrimalEditorBridge.Ping | answered only at 17:26 (editor in Play mode 16:19-17:26+) |
| P2_c_172924 | PrimalEditorBridge.ConsoleCheck | 2 entries, **0 errors, 0 warnings** |
| P2_ss_... | PrimalEditorBridge.SceneState | Island_VerticalSlice dirty=False **playing=True**: Build refused in Play mode, lock released |
| - | PrimalWildlifeBuilder.Build "", Phase2Check "survey" / "navmesh" | NOT RUN (Play mode, then PC offline) |

## 4. Requests

| To | Request |
|---|---|
| RES | fish shoals (`Resource_Fish_Shoal`, its ripple / fin cue) in the wetland lagoon shallows (0.2-0.8 m deep, east half, away from the beach) and at CAVE's `AI_Fish_Pool_0..2` / `AI_Fish_AlcovePool_0` |
| ENV-A | optional AI anchors (empty objects under an `AI_Anchors` object): `wetland_herbivores`, `wetland_wade_1..3`, `wetland_raptor_watch_1..2`, `valley_edge_watch`; the builder then uses them instead of my fallbacks (re-run `PrimalWildlifeBuilder.Build`) |
| ENV-B | optional `fern_forest_centre`, `foothills_sky`, `foothills_grazing` anchors; keep `Trail_C_StalkerPath` / WP_nn names (the raptor pair walks them) |
| CAVE | none needed (no small creature exists); mouth anchors `cave_east_mouth`, `cave_back_door` would replace my fallback circles |
| Lead / ART | small ambient animals (lizards, small mammals, bats) do not exist as species or models; if wanted they need art + a design decision (not made here) |
| Lead / QA | PlayMode when allowed: `WildlifeRoutine.ForceStart()` on Spinosaurus_0 (wading), a herd seen at 250-300 m from the lookout (walking, animated), a crouched player in the fern forest (EffectiveSight x0.33 of normal) |
| STORY / WORLD | `WildlifeRoutine.Changed` (routine, started) and `Carcass.Scavengers` can drive journal lines ("pteranodons at the carcass", "something wades in the lagoon") |

## 5. Not done / limits
- Bake + `Phase2Check` (counts per zone, ground-path and NavMesh reachability, creatures in forbidden areas = 0) not run.
  To finish, in one lock cycle: copy `Tools/_P2_stage/AI/WildlifeConfig.cs` into Assets (the only file newer than the
  deployed set: 3 cave no-go circles instead of 1; Assets copy must still match `_deployed1_md5.txt`), Refresh, Ping,
  ConsoleCheck, `PrimalWildlifeBuilder.Build ""` (also bakes the track art, which was never baked), `PrimalWildlifeBuilder.Phase2Check "survey"`
  (then optionally `"navmesh"`: a temporary NavMesh in memory for NavMesh.SamplePosition / CalculatePath), ConsoleCheck.
- The scene has no NavMesh: the AI moves kinematically on the terrain; routines use the A* ground path grid instead.
- No small ambient animals or bats (no species / art); no fish of my own (RES shoals).
- No PlayMode run by me: wading, landings, far animation and fern hiding are compiled and ran in the owner's session without
  errors, but are not play-verified.
- Routines are not saved; a hunt, the player or a fire interrupts a routine, which resumes or goes home after 90 s stuck.
