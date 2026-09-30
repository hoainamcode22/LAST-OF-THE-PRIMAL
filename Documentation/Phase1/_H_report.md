# HIER report (Phase 1 wave 2b, scene hierarchy), 2026-09-30

Result: `Island_VerticalSlice` now has the owner's layout (roots `World, Lighting, VFX, Audio, Managers`), saved. No world
pose changed, missing scripts / references unchanged at 0, Console 0 errors / 0 warnings, all 128 lookup checks resolve.
Tree and rules: `Documentation/Scene_Hierarchy.md`. Scene backup: `Tools/_backups_P1/Island_VerticalSlice_before_HIER.unity`.

## Unity lookup semantics (checked in the editor, `PrimalHierarchyBuilder.FindProbe`, before the move)
- `GameObject.Find("Environment/Water")` -> `World/Environment/Water`: a relative path matches anywhere (A need not be a root).
- `GameObject.Find("/Environment/Water")` -> null: a leading "/" means a root. `GameObject.Find` skips inactive objects.
- Name-only finds were already ambiguous: `Find("Water")` -> `Markers/Water` (not the ocean root), `Find("Rocks")` ->
  `World/Environment/Rocks`. The minimap also builds UI objects named `Markers` and `Player`. Those lookups now go
  through `SceneRoots` (exact path) instead.
- `transform.root` is not used anywhere in Scripts or Tests; `DontDestroyOnLoad` is only called on objects the code
  creates at the root; Tests look up nothing by hierarchy path (only components and UI children).

## Lookup table (lookup -> file:line -> after the move)
New helper `Scripts/Core/SceneRoots.cs`: layout constants, `Moves` table (old path -> new path), `Legacy(path, create)`
(maps, inactive included, falls back to the old path on an old-layout scene), `LegacyParent`, `LegacyChildren`, `Find`.

| Lookup (before) | File:line (now) | After |
|---|---|---|
| root `Markers` (GetRootGameObjects) | AI/WildlifePlan.cs:103 | `Legacy("Markers")` -> Managers/Markers (Route 14 pts, Resolve meadow ok) |
| root `Markers` / `Migration` | AI/MigrationDirector.cs:61 | `Legacy("Markers")` -> ReadRoute 14 pts |
| `GameObject.Find("Markers")` + Zones | Story/MissionSystem.cs:389 | `Legacy("Markers")` (was ambiguous with the minimap) |
| placed pieces at the root | Building/BuildSystem.cs:440, 454 | parented to World/Gameplay/Structures when that slot exists |
| `Find("[Atmosphere]") ?? new` + Emitters/Hazards/Reverb | Editor/PrimalAtmosphereBuilder.cs:81-82 | Legacy -> Audio/Emitters, Volcano/Hazards, Audio/Reverb; Sky -> Environment/Weather/Sky |
| root `Markers`/Zones | PrimalAtmosphereBuilder.cs:268 | `Legacy("Markers")` |
| root `Water` + `World/Environment/Water` | PrimalAtmosphereBuilder.cs:393-394 | Legacy both + Rivers, Wetlands, WaterSources slots |
| `world.transform.Find("Vegetation")` x2 | Editor/PrimalBushBuilder.cs:105, 499 | `Legacy("World/Vegetation")` -> Forest/Vegetation |
| `GameObject.Find("Markers")` | PrimalBushBuilder.cs:547 | `LegacyObject("Markers")` |
| World/Cliffs, World/Rocks hosts | Editor/PrimalClimbBuilder.cs:224, 241 | Legacy -> Terrain/Cliffs, Terrain/Rocks |
| root `[Gameplay]`/Climbables (created) | PrimalClimbBuilder.cs:261-262 | Legacy -> Interactables/Climbables (created there) |
| `[Dinosaurs]` created under `[Gameplay]` | Editor/PrimalDinoBuilder.cs:127 | LegacyParent -> Gameplay/Wildlife |
| ENV `Root(scene, name)` (World, Markers, Water) | Editor/PrimalEnvironmentBuilder.cs:115 | `Legacy(name)`: World, Managers/Markers, Ocean/Water |
| ENV `EnvGroup(group)` = World/Environment/<g> | PrimalEnvironmentBuilder.cs:131 | Legacy: Forest -> Forest/ForestDressing, Rocks -> Forest/Rocks, WaterEdge -> Wetlands/WaterEdge, Storytelling -> Interactables/Storytelling, WaterfallDressing + Water/Waterfall -> Waterfalls/..., Volcano/* unchanged |
| `world.Find("Environment/"+g)` | Editor/PrimalEnvironmentBuilder.Check.cs:27 | Legacy (same groups) |
| `GameObject.Find("Player")` | Check.cs:118 | `LegacyObject("Player")` (was ambiguous with the minimap) |
| root Water surfaces, Volcano/Lava, Water/Waterfall | Check.cs:129-131 | Ocean/Water + Rivers/Wetlands/WaterSources; Legacy for Lava / Waterfall |
| OwnedWorldGroups incl. "Environment" | Editor/PrimalEnvironmentBuilder.Terrain.cs:191-194, 202 | ENV groups listed one by one (Volcano as Basalt + Lava), Legacy each: same objects as before |
| root `[Resources]`, World/Resources, `[Gameplay]` children | Terrain.cs:251-257 | Legacy / `LegacyChildren("[Gameplay]")` (12 old children found) |
| `IsUnder(world.Find(group))` | Terrain.cs:264 | Legacy("World/"+group) |
| blockers World/<g>, Vegetation/Thickets, [Gameplay] children + grandchildren | Editor/PrimalEnvironmentBuilder.Vegetation.cs:101-109 | Legacy each; `LegacyChildren("[Gameplay]")` + their children (same set as before) |
| rockRoots Cliffs, Rocks, Waterfall/RockFace, Environment/Rocks | Vegetation.cs:391 | Legacy each |
| `waterRoot.Find(name)` + SetParent(waterRoot) | Editor/PrimalEnvironmentBuilder.Water.cs:51-52 | Legacy("Water/"+name), parent LegacyParent (Rivers / Wetlands / WaterSources) |
| `waterRoot.Find("ENV_Stream_Water")`, World.Find("Cliffs") | Water.cs:73, 230 | Legacy |
| World.Find("Environment/Water/Waterfall/RockFace") | Editor/PrimalEnvironmentBuilder.Wet.cs:109 | Legacy |
| `Find("Markers")`, `Find("Water")` (survey) | Editor/PrimalResourceBuilder.cs:52, 88 | LegacyObject (Water now really the ocean root) |
| `GameObject.Find("World/Rocks")` | Editor/PrimalResourceBuilder.Lava.cs:58 | LegacyObject -> Terrain/Rocks |
| World/Resources, World/Rocks + Vegetation, World/Shipwreck (x4) | Editor/PrimalResourceBuilder.Phase1.cs:437, 485, 572, 654, 788, 799 | Legacy / LegacyObject |
| World/Resources x2, `[Gameplay]/Cave` x2, Rocks, Vegetation, Storytelling/WreckRemains | Editor/PrimalResourceBuilder.World.cs:258-259, 269, 291, 613, 634-635 | Legacy / LegacyObject |
| new root `[Resources]` | World.cs:419-420 | created under Gameplay/Resources |
| `[Gameplay]`/FruitTrees (find + create) | Editor/PrimalPhase2Builder.cs:232, 380 | Legacy -> Interactables/FruitTrees |
| `[Systems]` (find or create) | Editor/PrimalStoryBuilder.cs:83 | LegacyObject -> Managers (Story would go to Managers/Story) |
| `Find("Markers")`, `Find("Water")` | PrimalStoryBuilder.cs:152, 279 | Legacy (Water now the ocean root, before it was Markers/Water) |
| NightSky under `[Systems]` | Editor/PrimalShaderBuilder.Wind.cs:676, 680 | LegacyParent -> VFX |
| World/Landmarks | Editor/PrimalVolcanoBuilder.cs:301 | Legacy -> Volcano/Landmarks |
| SceneBaker `Root(name)` (root only, else new) | Editor/PrimalSceneBaker.cs:126 | Legacy(name, true): no new `[Systems]` root on a re-run |

Left unchanged because they still resolve (names kept; checked by `Verify`): `GameObject.Find` of `World`, `[Dinosaurs]`,
`[Resources]`, `[Resources]/Shipwreck`, `[UI]`, `[UI]/[HUD]`, `GiantFootprints`, `Landmarks`, `ENV_Island_Terrain`,
`ENV_Ocean`, `ENV_Pond_Water`, `Water/ENV_Ocean`, `Water/PF_Ocean`, `Markers/ResourceAreas`, `ZONE_*`, `WaterfallSheet`
(files: PrimalBushBuilder, CharacterDiagnostics, EnvironmentBuilder(.Story/.Vegetation), Phase2Builder, ResourceBuilder*,
ShaderBuilder.Wind, VolcanoBuilder, WaterBuilder, ReviewCapture, CharacterBuilder, DinoBuilder). PrimalWaterBuilder.FindPath
already falls back to the leaf name. Whole-scene walks (GetRootGameObjects + GetComponentsInChildren) are layout-free.
Tests: nothing to change.

## Moves (Apply, 62 moves, 4 new empty slots + 19 slots made on the way, 3 old roots removed)
Old path -> new path, all by `SetParent(worldPositionStays: true)`:
`[Systems]/Time, Weather` -> World/WorldSystems; `[Systems]/Ambience, SfxPlayer` -> Audio; `[Systems]/VfxPool, BloodDecals,
NightSky` -> VFX; `[Systems]/Journal, Tutorial, Intro, Build, OceanShore, Trees, Input, EventSystem` -> Managers;
`[Gameplay]/[Dinosaurs]` -> Gameplay/Wildlife; `[Gameplay]/FruitTrees, Climbables, CaptainsLog, GiantFootprints, Pickup_wood x2,
Pickup_stone x2` -> Gameplay/Interactables; `[Gameplay]/Cave` -> Environment/Caves; `[Gameplay]/[Game], [Zones]` -> Managers;
`[Atmosphere]/Emitters, Reverb` -> Audio; `[Atmosphere]/Hazards` -> Environment/Volcano; `[Atmosphere]/Sky` -> Environment/Weather;
`Water/ENV_River, LowerRiver, RiverMouth, SpringStream, Stream(off)` -> Environment/Rivers; `Water/ENV_Wetland_Water` ->
Environment/Wetlands; `Water/ENV_Pond, Spring, WaterfallPool` -> Gameplay/WaterSources; root `Water` (rest) -> Environment/Ocean;
`World/Rocks, Cliffs` + `ENV_Island_Terrain` -> Environment/Terrain; `World/Shipwreck` -> Gameplay/Structures; `World/Props` ->
Gameplay/Interactables; `World/Resources` + `[Resources]` -> Gameplay/Resources (both kept); `World/Vegetation` ->
Environment/Forest; `World/Landmarks` -> Environment/Volcano; `World/Environment/Water, WaterfallDressing` -> Environment/Waterfalls;
`World/Environment/Forest` renamed ForestDressing and put in the new Environment/Forest slot; `World/Environment/Rocks` ->
Environment/Forest; `World/Environment/WaterEdge` -> Environment/Wetlands; `World/Environment/Storytelling` -> Gameplay/Interactables;
`Player` -> Gameplay (it is the Player slot); `[UI], Main Camera, Markers` -> Managers; `Sun, Global Volume` -> Lighting.
Empty new slots: Gameplay/Traps, WorldSystems/Wildlife, Perception, Save (nothing in the scene belongs there yet).
Removed: empty `[Systems]`, `[Gameplay]`, `[Atmosphere]` (no components, no children, no serialized reference to them).
Main Camera is under Managers: ThirdPersonCamera sets its world pose every frame, Camera.main still finds it.

## Checks (no PlayMode)
| Check | Before | After |
|---|---|---|
| objects / components | 8881 / 25725 | 8901 / 25745 (+23 containers, -3 old roots) |
| missing scripts | 0 | 0 |
| missing object references (SerializedObject walk) | 0 | 0 (also 0 after save) |
| world poses (GlobalObjectId, all 8878 shared objects, pos + rot) | | 0 changed, max delta 0.0000; Apply's own float check 0 changed (max 0.00000 m, 0.0000 deg); 50 samples in `Library/PrimalBridge/H_verify_after.txt` |
| lookup checks (`Verify`) | 98 ok, 30 not found (layout slots absent, old roots present) | 128 ok, 0 bad |
| prefab instance roots | | 2428, all Connected, 0 missing assets |
| Console after Refresh / Ping | 0 errors, 0 warnings | 0 errors, 0 warnings |
| Apply "dry" re-run | 62 moves planned | 0 moves (idempotent) |
Systems now: TimeManager World/WorldSystems/Time, WeatherManager World/WorldSystems/Weather, GameManager Managers/[Game],
UIManager Managers/[UI], RenderSettings.sun Lighting/Sun, Camera.main Managers/Main Camera.

## Rock rename (RES request): not done
There are 74 objects named `PFB_ENV_Rock_Large_03` under World/Rocks (now Terrain/Rocks), not two. The one at
(-99.17, 45.00, -239.43) is `saveId rock_26`. Code refers to the name: `PrimalResourceBuilder.Lava.cs:58-60` picks the
`PFB_ENV_Rock_Large_03` nearest the lava vent, and climb spots are named after their host rock
(`Climb_06/08/09_RockFace_Rock_Large_03`). By the rule "rename only if no code / data refers to that name" it stays.
Save ids are serialized (`rock_N`), so a later rename would not touch saves; RES can decide.

## Files changed
New: `Scripts/Core/SceneRoots.cs`, `Scripts/Editor/PrimalHierarchyBuilder.cs` (Dump, Systems, FindProbe, Audit, Verify,
PrefabCheck, Apply). Edited (backup `<file>.before_P1b`, VolcanoBuilder `.before_P1b_H` because a `.before_P1b` from another
agent existed): AI/WildlifePlan, AI/MigrationDirector (AI); Story/MissionSystem; Building/BuildSystem (BUILD);
Editor/PrimalAtmosphereBuilder, PrimalEnvironmentBuilder(.cs, .Check, .Terrain, .Vegetation, .Water, .Wet), PrimalVolcanoBuilder
(ENV); PrimalResourceBuilder(.cs, .Lava, .Phase1, .World), PrimalBushBuilder (RES); PrimalClimbBuilder (U); PrimalDinoBuilder
(DINO); PrimalPhase2Builder, PrimalStoryBuilder, PrimalShaderBuilder.Wind, PrimalSceneBaker. Only lookup lines changed.
Scene: `Assets/_Project/Scenes/Island_VerticalSlice.unity` (saved). Docs: `Documentation/Scene_Hierarchy.md` (old copy `.before_P1b`).

## Not done / notes
- `PrimalGameplayBuilder`, `PrimalWorldBuilder`, `PrimalPlayerSetup` (from-scratch scene generators, never run on this
  scene) still write the old roots; not mapped on purpose.
- The only runtime behaviour change: pieces placed in play (and restored from a save) are parented to
  World/Gameplay/Structures (world pose kept). Owner hand test: place a foundation + wall, save, load.
- Runtime-made helpers (`[TreeStumps]`, `[HazardMonitor]`, `[ResourceManager]`, pools) still appear at the root in Play mode.
- WorldSystems/Wildlife, Perception, Save and Gameplay/Traps are empty containers (their systems are static code or live on
  [Dinosaurs]).
- No PlayMode run. By mistake I ran one read-only `git status` in the PC shell; it timed out, left no `.git/index.lock`, changed nothing.
- Bridge lock taken 09:46, released 10:11 UTC (only agent, one long cycle).
