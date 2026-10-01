# Phase 2 art delivery (agent ART / A2)

Prefabs are in `Assets/_Project/Prefabs/Environment/Phase2/` (built by `PrimalPhase2ArtBuilder.Build`, checked by
`PrimalPhase2ArtBuilder.Check`). Models: `Art/Environment/Models/Phase2/*.fbx`, textures: `Art/Environment/Textures/Phase2/`,
materials (URP Lit): `Art/Environment/Materials/Phase2/`. Every prefab: LODGroup, MeshCollider from its `*_COL` mesh (where
listed), static flags (batching, occludee, reflection probe; occluder on landmarks and cave pieces). Use uniform scale only
(0.8-1.25 is fine); rotate freely around Y. Sizes are X x Y x Z in Unity metres (Y up), measured on LOD0.

batch 2 built by Lead finish queue 2026-10-01

| Prefab | Zone (owner) | Size m (X x Y x Z) | Pivot | Tris LOD0/1/2 | Collider tris | Notes |
|---|---|---|---|---|---|---|
| `Prefabs/Environment/Phase2/LM_RockRidge.prefab` | Migration Valley (ENV-A) | 57 x 15,7 x 20,7 | base centre, ground level (local y = 0), ~2 m buried skirt | 28652/10999/3399 | 1800 | ridge along local X; ramp to the top at +X end; steep cliff on -Z (south), moss on +Z (north) in Unity |
| `Prefabs/Environment/Phase2/LM_AncientFallenTree.prefab` | Prehistoric Wetland (ENV-A) | 38,3 x 8 x 10,7 | base centre; local y = 0 is the intended water line (crown end sunk to -1.5 m) | 31000/10999/3500 | 1600 | trunk along local X: root plate at -X (rises ~6.5 m), broken hollow crown end at +X; moss on top and +Z (north) in Unity |
| `Prefabs/Environment/Phase2/LM_FossilSkeleton.prefab` | Bone / Carcass Valley (BONE) | 40,7 x 7,5 x 23,4 | base centre, local y = 0 = surrounding ground (mound edge sinks to -0.4 m) | 36885/13000/4200 | 2600 | head / skull at local +X, tail at -X; rib arches 4-6 m tall (walk-under gaps); mound bank on -Z side in Unity |
| `Prefabs/Environment/Phase2/LM_GiantTree.prefab` | Giant Fern Forest (ENV-B) | 42,3 x 59,6 x 40,3 | trunk base centre at ground level (buttresses go ~1 m below local y = 0) | 31873/9753/2898 | 1500 | collider covers trunk + buttresses up to 12 m; canopy is cut-out cards (M_P2_Canopy, double sided); wood tris per LOD 31873/9753/2898 |
| `Prefabs/Environment/Phase2/LM_BlackRidge.prefab` | Volcanic Foothills (ENV-B) | 52,8 x 19 x 20,5 | base centre, ground level (local y = 0), columns ~2 m below | 32797/12000/3600 | 1800 | ridge along local X; steeper face on -Z (south) in Unity, talus of fallen columns mostly on that side |
| `Prefabs/Environment/Phase2/CAVE_Wall_A.prefab` | Deep Water Cave (CAVE) | 6,2 x 8 x 6,5 | snap corner: floor-level start of the wall line; runs 6 m along +X; cave interior on -Z, ceiling overhangs 4.6 m towards -Z | 5408/1424 | 392 | tileable CaveRock / CaveWet, 1 UV = 6 m; normals inward |
| `Prefabs/Environment/Phase2/CAVE_Wall_B.prefab` | Deep Water Cave (CAVE) | 6,2 x 8 x 6,5 | snap corner: floor-level start of the wall line; runs 6 m along +X; interior on -Z; flowstone bulge variant, same seams as A | 5408/1424 | 392 | tileable CaveRock / CaveWet, 1 UV = 6 m; normals inward |
| `Prefabs/Environment/Phase2/CAVE_Tunnel_Straight.prefab` | Deep Water Cave (CAVE) | 6,2 x 7 x 7,5 | snap point: entrance floor centre; exit floor centre at (+6, 0, 0); 5 m wide, 4.6 m high | 6400/1664 | 448 | tileable CaveRock / CaveWet, 1 UV = 6 m; normals inward |
| `Prefabs/Environment/Phase2/CAVE_Tunnel_Bend.prefab` | Deep Water Cave (CAVE) | 10 x 7 x 10 | snap point: entrance floor centre heading +X; 90 deg left bend, exit floor centre at (+6, 0, +6) heading +Z | 9472/2432 | 640 | tileable CaveRock / CaveWet, 1 UV = 6 m; normals inward |
| `Prefabs/Environment/Phase2/CAVE_Chamber_Dome.prefab` | Deep Water Cave (CAVE) | 31,3 x 16,2 x 31,7 | floor centre = pool basin centre (basin floor -2 m, rim ring r ~9 m); inner radius 14 m, 11 m high; tunnel openings with floor centres at (+-14.6, 0, 0) facing +-X | 8315/2281 | 773 | tileable CaveRock / CaveWet, 1 UV = 6 m; normals inward |
| `Prefabs/Environment/Phase2/CAVE_PoolRim.prefab` | Deep Water Cave (CAVE) | 10,3 x 2,5 x 10,3 | pool centre at the water line (y = 0); 90 deg arc from +X to +Z, radius 8.4 m; 4 copies rotated 90 deg about Y close the ring | 3768/1004 | 304 | tileable CaveRock / CaveWet, 1 UV = 6 m; normals inward |
| `Prefabs/Environment/Phase2/CAVE_Stalactites.prefab` | Deep Water Cave (CAVE) | 4,8 x 3,9 x 3,8 | ceiling attach point (top centre); hangs to ~-3.4 m | 4200/1500 | none | tileable CaveRock / CaveWet, 1 UV = 6 m; normals inward |
| `Prefabs/Environment/Phase2/CAVE_Rubble.prefab` | Deep Water Cave (CAVE) | 4,3 x 1,7 x 3,4 | base centre on the floor | 3800/1398 | 1398 | tileable CaveRock / CaveWet, 1 UV = 6 m; normals inward |
| `Prefabs/Environment/Phase2/PROP_P2_DeadSnag_A.prefab` | Prehistoric Wetland (ENV-A) | 2,1 x 9,8 x 2,2 | base centre at ground level | 3770/1799/599 | 320 |  |
| `Prefabs/Environment/Phase2/PROP_P2_DeadSnag_B.prefab` | Prehistoric Wetland (ENV-A) | 4,9 x 8,1 x 2,5 | base centre at ground level | 4156/1999/649 | 320 |  |
| `Prefabs/Environment/Phase2/PROP_P2_Ribcage.prefab` | Bone / Carcass Valley (BONE) | 4,3 x 1,4 x 4,3 | base centre at ground level | 7000/2400/800 | 500 |  |
| `Prefabs/Environment/Phase2/PROP_P2_Skull_Large.prefab` | Bone / Carcass Valley (BONE) | 1,8 x 0,8 x 0,8 | base centre at ground level | 6000/2000/700 | 500 |  |
| `Prefabs/Environment/Phase2/PROP_P2_BoneScatter_A.prefab` | Bone / Carcass Valley (BONE) | 3,2 x 0,4 x 3,1 | base centre at ground level | 4199/1500/500 | 500 |  |
| `Prefabs/Environment/Phase2/PROP_P2_BoneScatter_B.prefab` | Bone / Carcass Valley (BONE) | 3,7 x 0,5 x 3 | base centre at ground level | 3999/1400/499 | 500 |  |

Thumbnails: `Documentation/Screenshots/Phase2/Art/<Name>.png`. Blender sources: `E:\Model game khủng long\PHASE2_Landmarks.blend`,
scripts `E:\Model game khủng long\scripts\phase2\*.py` (one per asset family, regenerable), FBX `export\phase2\`.
