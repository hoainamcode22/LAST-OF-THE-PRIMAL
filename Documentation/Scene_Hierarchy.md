# Scene hierarchy: Island_VerticalSlice (HIER, 2026-09-30)

Owner's layout, applied by `PrimalHierarchyBuilder.Apply` (bridge, idempotent, "dry" = report only). World positions,
rotations and prefab instances were kept (SetParent with worldPositionStays, instance roots moved whole, nothing unpacked).
Objects kept their own names; the only rename is ENV's old `World/Environment/Forest` group, now `Forest/ForestDressing`.
The old roots `[Systems]`, `[Gameplay]` and `[Atmosphere]` were emptied into their slots and removed.

```
World
  Environment
    Terrain        ENV_Island_Terrain, Rocks (was World/Rocks, 816), Cliffs (was World/Cliffs)
    Ocean          Water (was the root "Water": WaterGlobals, ENV_Ocean, PF_Ocean, PF_OceanShore)
    Rivers         ENV_River_Water, ENV_LowerRiver_Water, ENV_RiverMouth_Water, ENV_SpringStream_Water, ENV_Stream_Water (off)
    Waterfalls     Water/Waterfall (was World/Environment/Water), WaterfallDressing
    Forest         ForestDressing (ENV forest group), Rocks (ENV moss rocks, was World/Environment/Rocks), Vegetation (was World/Vegetation)
    Caves          Cave (was [Gameplay]/Cave: cave stones + bone pickup)
    Wetlands       WaterEdge (ENV), ENV_Wetland_Water
    Volcano        Basalt, Lava (ENV), Hazards (was [Atmosphere]/Hazards), Landmarks (offshore VolcanoLandmark, was World/Landmarks)
    Weather        Sky (CloudVeil, was [Atmosphere]/Sky)
  Gameplay
    Player         the Player prefab instance itself (was the root Player)
    Wildlife       [Dinosaurs] (DinosaurSpawner + herds)
    Resources      [Resources] (RES placement, was a root), Resources (was World/Resources); two sets, not merged
    Structures     Shipwreck (hull pieces, was World/Shipwreck); pieces placed in play are parented here (BuildSystem.Spawn)
    WaterSources   ENV_Pond_Water, ENV_Spring_Water, ENV_WaterfallPool_Water
    Traps          (empty)
    Interactables  Storytelling (ENV), FruitTrees, Climbables, CaptainsLog, GiantFootprints, Props (ship crates), Pickup_wood x2, Pickup_stone x2
  WorldSystems
    Time           TimeManager (was [Systems]/Time)
    Weather        WeatherManager (was [Systems]/Weather)
    Wildlife       (empty: the wildlife logic lives on [Dinosaurs] and its herds)
    Perception     (empty: Stimuli is static code)
    Save           (empty: SaveSystem is static code)
Lighting           Sun, Global Volume
VFX                VfxPool, BloodDecals, NightSky
Audio              Ambience, SfxPlayer, Emitters (was [Atmosphere]/Emitters), Reverb (was [Atmosphere]/Reverb)
Managers           [Game] (GameManager), [Zones] (ZoneManager), [UI], Main Camera, Markers, Journal, Tutorial, Intro, Build,
                   OceanShore, Trees, Input, EventSystem
```

Runtime-only objects (made in Play mode, not saved): `[TreeStumps]`, `[HazardMonitor]`, `[ResourceManager]`, `[BirdFlush]`,
`[TrackSigns]`, dropped items, projectiles; they appear at the root as before.

## Rules for code
- New lookups: `PrimalFrontier.Core.SceneRoots` constants (`SceneRoots.Find(SceneRoots.Structures)`), never a root name.
- Code written for an old path uses `SceneRoots.Legacy("World/Rocks")` / `LegacyObject` / `LegacyParent(path, create)`:
  the old path is mapped to its new place (table `SceneRoots.Moves`), inactive objects included; a builder that creates
  a group creates it at the new place (`Legacy(path, true)`).
- `GameObject.Find("A/B")` matches any active B whose parent is named A, anywhere (checked in the editor: "Environment/Water"
  was found at World/Environment/Water); a leading "/" means a root. Name-only finds (`ZONE_PlayerSpawn`, `[Dinosaurs]`,
  `[Resources]/Shipwreck`, `Water/ENV_Ocean`) still work because the names were kept.
- `transform.root` is not used anywhere in the project; nothing depends on an object being a root.
- Do not add new top-level roots; put new content in the matching slot.

## Tools
- `PrimalHierarchyBuilder.Dump "levels"` or `"path:levels"`: the tree with components.
- `PrimalHierarchyBuilder.Verify`: resolves every lookup the code makes, lists the systems, checks the slots.
- `PrimalHierarchyBuilder.Audit "tag"`: missing scripts, missing object references, world pose snapshot.
- `PrimalHierarchyBuilder.PrefabCheck`: prefab instance status.
