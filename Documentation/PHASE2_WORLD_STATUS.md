# PRIMAL FRONTIER, Phase 2: World Exploration Expansion (2026-10-01)

## Task Requested
Six distinct natural environments on the existing island, without rebuilding the game, adding a second island, ruins,
ancient civilisations or modern structures:
1. Migration Valley
2. Prehistoric Wetland
3. Bone / Carcass Valley
4. Giant Fern Forest
5. Volcanic Foothills
6. Deep Water Cave

Each zone needs gradual transitions (no biome walls) and one recognisable landmark. Testing is limited to compiling and
checking references and imports; there are no gameplay bots and no PlayMode runs. The developer explores by hand.

## Implemented
| Zone | Built | Landmark |
|---|---|---|
| Migration Valley | Open grassland with a wind-blown tall grass layer (1.2-1.8 m), short grass, ferns at the edges, scattered trees, 28 moss boulders, 26 giant ferns, horsetails, mud wallows and a trampled herd trail along the migration route. River banks and the ford are mud and wet sand. Trees blocking the view from the lookout knoll to the ford, waterfall and meadow were moved aside (0 now block it). | LM_RockRidge (57 x 21 x 16 m sandstone ridge) |
| Prehistoric Wetland | Shallow lagoon (existing water), mud flats, reed beds, horsetail, ferns, 11 dead snags, 4 root plates, 2 fallen trunks. Effects: ground fog, water mist, insects, dragonflies by day, faint fireflies on dry nights. Ambience: frogs, insects, water. | LM_AncientFallenTree (38 m colossal trunk with root plate, crown in the water) |
| Bone / Carcass Valley | Darker soil and a muddy predator path. Carcasses: a fresh parasaurolophus you can butcher (knife: meat 3, hide 2, bone 2), a rotting triceratops and an old bone pile. At the entrance, a bone line across the path and a skull. Also 16 large prints, drag marks and blood, 28 bone props, 3 claw-marked snags, 5 broken trees and 6 examine spots. Effects and sound: flies and dry wind. Entering triggers the survivor line "Something hunts here." and the journal lesson. | LM_FossilSkeleton (original long-necked fossil, half buried, 28 x 16 m at scale 0.7) |
| Giant Fern Forest | Dense understory (fern 24950, giant fern 13407, bush 3035 instances). Own trees: 173 tree ferns, 14 cycads, 4 tall araucarias. Props: 220 giant fern clumps, 90 magnolias, 17 vine curtains, 8 fallen trunks, 16 moss rocks, 2 root plates. Three trails (A river approach 132 m, B game trail 153 m, C stalker path 73 m) around a 17.5 m clearing. Effects: mist, insects, dust motes in the light shaft, humid haze. | LM_GiantTree (60 m tall, 5-7 m trunk with buttress roots) |
| Volcanic Foothills | Gradual band: forest, then dry grass (new dry grass layer), then black basalt (90 groups), ash, then the Phase 1 volcanic ground (kept). 24 dead / broken trees, 5 dry trunks, sparse cycads. Effects: smoke wisps, ash fall, heat shimmer, dust devil, and 2 mild heat bands (+2.5 / +4 C, no damage). The offshore volcano and its eruption are untouched. | LM_BlackRidge (53 x 19 x 20 m columnar basalt ridge with obsidian streaks) |
| Deep Water Cave | A through-cave under the cliff arc, about 121 m walkable. The east mouth opens on the waterfall plunge pool, where the underground stream exits. The west back door is at the cliff foot. Inside: a 46 m stream passage, a 21 m chamber, a 40 m west passage, and a hidden branch behind a small water curtain through a crouch-only crawl (1.6 m) to a hidden chamber with an old nest of bones and a rare resource cluster. Wet dark rock and 7 drinkable water sources. Lighting: 4 lights without shadows that follow daylight, darkening ambient bands, so the torch matters. Effects: drips and mist. Sound: Cave-preset reverb in 34 small spheres, interior only. | Large underground pool (11.6 x 8.6 m, 1.7 m deep) under a ceiling cleft with a faint daylight spot |

Wildlife (existing species only):
- **Valley:** parasaurolophus herd of 9 migrating, triceratops herd of 5. Herds keep animating out to 340 m and are drawn to about 380 m. A Carnotaurus watches from the valley edge.
- **Wetland:** a reed trio of 3 parasaurolophus. A Velociraptor patrols at dusk. A Spinosaurus visits every 2nd day to wade and fish.
- **Bone Valley:** 2 pteranodons circle and land to scavenge, a raptor feeds at the fresh carcass, and a Carnotaurus patrols through.
- **Fern Forest:** a raptor pair stalks Trail C. Creatures see 0.6x as far in the forest, and a crouched player in dense plants is hidden further.
- **Foothills:** a high-circling pteranodon and a grazing ankylosaurus. Creatures avoid the heat rings.
- **Cave:** no large creatures allowed; fish shoals in its pools.

Resources: 73 new nodes and 6 fish shoals, all in natural clusters:
- Cave: 15 nodes plus 4 fish shoals, including a rare cluster in the hidden chamber.
- Wetland: 9 reed fiber nodes plus 2 fish shoals.
- Bone Valley: 5 bone piles.
- Foothills: 18 basalt / stone nodes.
- Fern Forest: 20 fiber / branch nodes.

Zones, journal and map:
- 6 zones with a 20-40 m blend and a zone-entry toast.
- 7 landmarks that are discovered when the player sees or reaches them.
- 14 Phase 2 journal pages, discovery texts for every examine spot, and minimap labels.

## Locations
Scene: `Assets/_Project/Scenes/Island_VerticalSlice.unity`. Roots stay World, Lighting, VFX, Audio, Managers.

| Zone | Scene group | Landmark position | Builder (bridge) |
|---|---|---|---|
| Migration Valley | World/Environment/Forest/MigrationValley | (-48, 9.2, 12) | `PrimalZonesBuilder.Valley`, `ValleyCheck` |
| Prehistoric Wetland | World/Environment/Wetlands/PrehistoricWetland | root (85.9, 2.1, 194.5) | `PrimalZonesBuilder.Wetland`, `WetlandCheck` |
| Bone Valley | World/Environment/Forest/BoneValley | (-114, 15.5, -30) | `PrimalZonesBuilder.BoneValley "force"`, `BoneValleyCheck` |
| Giant Fern Forest | World/Environment/Forest/GiantFernForest | (160, 8.8, 24) | `PrimalZonesBuilder.FernForest`, `FernFoothillsCheck` |
| Volcanic Foothills | World/Environment/Volcano/VolcanicFoothills | (-148, 44.1, -198) | `PrimalZonesBuilder.Foothills`, `FernFoothillsCheck` |
| Deep Water Cave | World/Environment/Caves/DeepWaterCave | pool (39.2, 20.8, -175.2); mouths (90, 19.5, -157.4), (12.4, 22.5, -141.4) | `PrimalCaveBuilder.Build`, `Check`, `Capture` |
| Zone FX / ambience / journal | World/Environment/Weather/Zones2, Audio/Emitters | | `PrimalAtmosphereBuilder.Zones2`, `Zones2Check` |
| Wildlife | World/Gameplay/Wildlife/[Dinosaurs] | | `PrimalWildlifeBuilder.Build`, `Phase2Check` |
| Resources | World/Gameplay/Resources/[Resources]/Phase2/<Zone> | | `PrimalResourceBuilder.Phase2` |

Agent reports: `Documentation/Phase2/_{A2,EA,EB,BV,CV,P,W,R3}_report.md`, `ART_DELIVERY.md`, `FINISH_QUEUE.md`.

## Assets
- **Blender** (`E:\Model game khủng long\PHASE2_Landmarks.blend`, scripts in `scripts\phase2\`, FBX in `export\phase2\`): 19 new assets. All have LOD0-LOD2 (cave pieces LOD0-1), collider meshes and PBR textures (45 PNG). Every prefab check reports 0 missing references.
  - 5 landmarks: LM_RockRidge, LM_AncientFallenTree, LM_FossilSkeleton, LM_GiantTree, LM_BlackRidge.
  - Cave kit: CAVE_Wall_A/B, CAVE_Tunnel_Straight/Bend, CAVE_Chamber_Dome, CAVE_PoolRim, CAVE_Stalactites, CAVE_Rubble.
  - Props: PROP_P2_DeadSnag_A/B, Ribcage, Skull_Large, BoneScatter_A/B.
- **Unity art:** prefabs `Prefabs/Environment/Phase2/*`, models `Art/Environment/Models/Phase2/*`, textures `Art/Environment/Textures/Phase2/*`. New detail layers DET_Z_TallGrass and DET_EB_DryGrass. 15 pooled zone effect kinds in `Prefabs/World/ZoneFx`. Generated cave meshes, flow maps and materials in `Art/Environment/Cave`. Baked carcass meshes in `Generated/BoneValley`. One new sound, `Audio/Ambience/AMB_Flies_Loop.wav`.
- **New code:**
  - Editor: `PrimalZonesBuilder*.cs`, `PrimalCaveBuilder.cs`, `PrimalPhase2ArtBuilder.cs`, `PrimalAtmosphereBuilder.Zones2.cs`, `PrimalWildlifeBuilder.Phase2.cs`, `PrimalResourceBuilder.Phase2.cs`.
  - Runtime: `World/ZoneCarcass.cs`, `World/CaveDaylightLight.cs`, `UI/ZoneToast.cs`, `VFX/ZoneFxAnchor.cs`, `VFX/ZoneFxManager.cs`, `AI/WildlifeZones.cs`, `AI/WildlifeRoutine.cs`, `AI/WildlifePlanZones.cs`.
- **Terrain backups:** `Art/Terrain/_Backup/TD_Island_before_{EA,EB,BV,CV}.asset`. Script backups are in `Tools/_backups_P2/`.

## Known Issues
- **Migration Valley ridge:** ground under the rock ridge varies 2.3 m against a 2 m buried skirt, so a small gap may show on its downhill edge.
- **Fossil skeleton size:** placed at scale 0.7 (ART suggested 0.8-1.25) because no larger clear footprint exists.
- **Fern Forest trails:**
  - Trail C is crossed by Phase 1 forest dressing (a root plate and a fallen trunk).
  - One ENV rock sits on Trails A and B.
  - The ankylosaurus group stands on Trail B.
- **Cave:**
  - It uses its generated rock shell. Of the ART kit, only stalactites and rubble fit; the modular walls, tunnels, dome and pool rim are not placed.
  - Faint light flecks show on the pool basin wall.
  - The pool has no sound emitter of its own.
  - Check the east mouth top edge by hand for a sky sliver.
- **Cave wildlife:** no small cave creatures, because no such species or models exist. Fish only.
- **Flint and clay:** no such items were added, since no recipe uses them, so the cave holds loose stone.
- **Wetland fish:** the lagoon fits only 2 fish shoals.
- **Tall grass distance:** at Medium quality the detail distance is 55 m, so the tall grass fades beyond that. High and Ultra show it further out.
- **Wildlife after loading:** routines are not saved and restart from the clock after a load. There is no NavMesh; creatures move on the terrain.
- **Wildlife spawn spots:** a few AI spawn points use fallbacks: the wetland herbivores, the bone-valley raptor den and the foothills sky and grazing spots.
- **Builder log:** BoneValley with `force` still prints an outdated "prefab is NOT placed" warning, but the prefab is placed.
- **Visual checks:** nothing was seen or heard in Play mode. Edit-mode captures do not show water or particles.

## Critical Errors
None. Final Console check: 0 errors, 0 warnings. Scene: 12551 objects, 0 missing scripts, 0 missing references. Prefab instances: 3186, all connected. Prefabs checked: 219, 0 problems. Item database: 50 items, 35 recipes, 0 problems. Hierarchy path lookups: 128 ok, 0 bad. All six zone checks pass, with 0 resource nodes covered and 0 off the ground or in water. The scene is saved.

## Manual Playtest
NOT PERFORMED.
