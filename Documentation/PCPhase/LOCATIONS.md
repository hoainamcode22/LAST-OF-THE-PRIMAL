# Island locations (ENV, terrain v2, 2026-09-29)

Source of truth: `Assets/_Project/Art/Environment/Terrain/env_features_v2.json` (made by `tools/env/terrain_v2.py` +
`terrain_post.py`). In the scene: `Markers/Zones/<id>` (an `EnvLocation` component with `id`, `radius`, `note`) and
`Markers/Migration/Route_00..13`, placed by `PrimalEnvironmentBuilder.Markers`. The old `ZONE_*` markers stay unchanged
(tutorial, journal, minimap and ZoneManager still use them).

All positions are **Unity world coordinates** (x, y, z), y = ground of the v2 terrain. Unity +z is the start-beach side of
the island, -z the high ground; Blender design coordinates are (-x, -z). Radii in metres (circles, like ZoneManager zones).

## Locations

| id | position (x, y, z) | radius | what is there |
|---|---|---|---|
| `beach` | (0.0, 1.8, 205.0) | 70 | start beach (crescent bay), player spawn, driftwood, shipwreck remains along the shore |
| `shipwreck` | (-45.0, -0.1, 224.0) | 25 | the wreck: hull, ribs, mast, sail, crates, barrels (unchanged) |
| `forest` | (-40.0, 10.7, 125.0) | 60 | first forest inland from the beach and the camp: araucaria, cycads, ferns, the trail to the pond |
| `deep_forest` | (168.0, 8.2, 20.0) | 60 | dense forest on the far side of the river (+x): tree ferns, giant ferns, vines, hanging moss, big roots, fallen trunks |
| `river` | (66.0, 10.5, -45.0) | 55 | river valley on the old stream line: banks, terraces, gravel bars, horsetails; ford at (84.6, 10.6, -97.5) |
| `waterfall` | (93.5, 19.4, -160.0) | 24 | 14.5 m waterfall (lip 33.0 m, pool 18.5 m) in a rock amphitheatre, mist, spray, wet mossy rocks |
| `meadow` | (-2.0, 9.2, -28.0) | 70 | open grassland valley floor, mud wallows, the giant footprints |
| `canyon` | (-136.0, 23.4, -125.0) | 36 | narrow gorge through a rock massif (180 m, floor 12 -> 41 m, walls 8-16 m) up to the volcanic ridge; fossil bed |
| `wetland` | (102.0, 0.4, 172.0) | 38 | lagoon (water 0.85 m) behind the +x end of the start beach: pools, mud flats, reeds, horsetails; the river mouth crosses the beach |
| `cave` | (-18.0, 18.4, -131.0) | 15 | cave entrance in the cliff arc (unchanged) |
| `ridge` | (108.0, 47.8, -208.0) | 45 | rocky ridge on the high ground above the waterfall: crags, the spring source, strange markings |
| `volcano` | (-122.0, 44.4, -216.0) | 45 | volcanic ridge at the top of the canyon: ash field, basalt ridges, spatter vent, small lava channel (the big volcano stays across the sea) |
| `predator_territory` | (-105.0, 21.6, -76.0) | 45 | dark forest between the meadow and the canyon mouth: broken trees, claw marks, blood trail, kill site with a skeleton |
| `herbivore_valley` | (6.0, 9.2, -40.0) | 85 | the meadow valley with the river crossing: herds feed, drink and cross here |
| `old_camp` | (122.0, 11.4, -66.0) | 12 | another human was here: burnt fire ring, collapsed lean-to, tally marks, a worn stone tool |
| `fossil_bed` | (-128.4, 23.4, -120.0) | 10 | bones in the canyon wall |
| `nest` | (-152.0, 13.2, -28.0) | 14 | a ground nest with eggs (dangerous) |
| `migration_view` | (-40.0, 25.4, -92.0) | 10 | lookout knoll (top 25.4 m, rocky west face, ramp from the -x/-z side): view over the meadow, the ford and the waterfall |

## Migration route (walkable valley ground; AI moves herds along it)

`Markers/Migration/Route_00..13` (each marker faces along the route): meadow -> river crossing -> near the waterfall ->
grassland terrace -> resting hollow. Steepest ground on the route: about 22 degrees (the climb from the ford to the terrace);
the ford banks have 0.3-0.4 m steps.

| # | position | | # | position |
|---|---|---|---|---|
| 00 | (-14.0, 8.9, 12.0) meadow (gathering) | | 07 | (100.0, 13.7, -110.0) far bank |
| 01 | (-8.0, 9.1, -18.0) | | 08 | (112.0, 14.7, -122.0) |
| 02 | (2.0, 9.3, -48.0) | | 09 | (124.0, 16.9, -132.0) near the waterfall (pool 38 m away) |
| 03 | (18.0, 9.9, -72.0) | | 10 | (137.0, 21.1, -142.0) up onto the terrace |
| 04 | (46.0, 11.1, -88.0) | | 11 | (156.0, 23.0, -150.0) grassland terrace |
| 05 | (70.0, 12.2, -95.0) | | 12 | (172.0, 23.3, -154.0) |
| 06 | (84.6, 10.6, -97.5) **ford** (water 0.34 m deep, 10 m wide) | | 13 | (184.0, 24.7, -160.0) resting hollow |

## Water (for SURV, AI, WORLD audio)

| body | object (Water root) | surface | WaterSource |
|---|---|---|---|
| spring source | `ENV_Spring_Water` | 40.8 m at (104.0, -193.0) | "Spring water", fresh, **clean** |
| brook (spring -> lip) | `ENV_SpringStream_Water` | 40.3 -> 33.0 m | "Stream water", fresh, dirty |
| waterfall pool | `ENV_WaterfallPool_Water` | 18.5 m, r 8 m at (93.5, -158.5) | "Waterfall pool", fresh, dirty |
| river (pool -> pond) | `ENV_River_Water` | 18.5 -> 9.6 m, 214 m | "River water", fresh, dirty |
| pond (unchanged) | `ENV_Pond_Water` | 9.55 m | as before |
| lower river (pond -> lagoon) | `ENV_LowerRiver_Water` | 9.5 -> 0.95 m | "River water", fresh, dirty |
| lagoon | `ENV_Wetland_Water` | 0.85 m | "Marsh water", fresh, dirty |
| river mouth (lagoon -> sea) | `ENV_RiverMouth_Water` | 0.8 -> 0 m | "River water", fresh, dirty |
| old stream | `ENV_Stream_Water` | switched off (inactive, kept) | replaced by the river |

Waterfall sound / mist position for audio: lip (94.0, 33.0, -169.6), impact about (93.9, 18.6, -167.2).

## Storytelling (Examinable ids, placed by `PrimalEnvironmentBuilder.Story`; STORY writes the texts)

Each prop has an `Examinable` with `discoveryId` = id below, `SaveId` = id, `displayName` left at the default and an empty
`thought`, so the name and the thought come from STORY's texts (`StoryIds` already strips `env_`). Objects under
`World/Environment/Storytelling/{KillSite, PredatorPath, BloodTrail, TheropodTrail, Nest, FossilBed, OldCamp, Markings,
WreckRemains, Spots}`; the Story step logs each id with its final position.

| id | where (approx.) | what |
|---|---|---|
| `env_giant_skeleton` | (-118, 14.4, -50) | large dinosaur skeleton at the kill site |
| `env_scattered_bones` | (-111.5, -, -47) + two more along the predator path | scattered bones |
| `env_claw_marks` | (-112.5, -, -75), grooves facing the path | deep claw grooves on a dead trunk |
| `env_broken_trees` | beside the predator path (-80, -50) -> (-128, -50), first one examinable | snapped trunks, torn-off tops |
| `env_blood_trail` | along the path (-80, -, -50) -> the skeleton (-118, -, -50) | dried blood stains on the ground |
| `env_nest_eggs` | (-152, 13.2, -28) | ground nest with eggs |
| `env_fossil_bed` | canyon wall at about (-128.9, 20.4, -120), facing the gorge | bones set in the rock |
| `env_old_camp_firering` | (122, 11.4, -66) | burnt fire ring, charcoal |
| `env_old_camp_shelter` | old camp | collapsed lean-to |
| `env_old_camp_tally` | old camp | tally marks scratched into a boulder |
| `env_old_camp_tool` | old camp | a worn stone chopper |
| `env_markings_ridge` | 4-6 m uphill of the spring (about (104, -, -198)), facing it | carved spirals and animal figures |
| `env_markings_cave` | beside the cave mouth, about (-11.4, 18.4, -129.3), facing +z (the way in) | carved marks |
| `env_wreck_remains` | dry sand along the beach, x -148 .. 49 (first piece about (-23, 1.4, 214)) | broken planks and beams, debris |
| `env_theropod_trail` | predator path from (-65, 11.5, -40), about 20 prints | a line of giant three-toed prints |
| `env_lava_channel` | bank of the channel at about (-115.2, 44.2, -224.6) | glowing lava channel, heat |
| `env_view_knoll` | knoll top (-40, 25.4, -92) | the view over the valley |
| `env_spring` | (104, 40.8, -193) | the spring source |

## What changed for other agents

After the ENV terrain deploy (see `TERRAIN_READY.txt`) these roots must re-run their own builders (the terrain pass logs
exact counts under "OTHER ROOTS"): `[Resources]` + `World/Resources` (RES), `[Gameplay]/[Dinosaurs]` + `Markers/Habitats`
(AI), `Markers/ResourceAreas` (RES), story props / missions using positions (STORY), ZoneManager zones (WORLD, can read
`Markers/Zones`). Unchanged on purpose (protected, same heights): spawn beach, shipwreck, camp, cave, pond basin, cliff
arc, the four fruit trees, the giant footprints, the captain's log, the spawn pickups.
