# Phase 2 art delivery (agent ART / A2)

Prefabs are in `Assets/_Project/Prefabs/Environment/Phase2/` (built by `PrimalPhase2ArtBuilder.Build`, checked by
`PrimalPhase2ArtBuilder.Check`). Models: `Art/Environment/Models/Phase2/*.fbx`, textures: `Art/Environment/Textures/Phase2/`,
materials (URP Lit): `Art/Environment/Materials/Phase2/`. Every prefab: LODGroup, MeshCollider from its `*_COL` mesh (where
listed), static flags (batching, occludee, reflection probe; occluder on landmarks and cave pieces). Use uniform scale only
(0.8-1.25 is fine); rotate freely around Y. Sizes are X x Y x Z in Unity metres (Y up), measured on LOD0.

Batch 1 (5 landmarks) delivered 2026-09-30 15:10 UTC. The long axis of every ridge / trunk / skeleton is local X. Cave kit and props follow in batch 2.

| Prefab | Zone (owner) | Size m (X x Y x Z) | Pivot | Tris LOD0/1/2 | Collider tris | Notes |
|---|---|---|---|---|---|---|
| `Prefabs/Environment/Phase2/LM_RockRidge.prefab` | Migration Valley (ENV-A) | 57.0 x 15.69 x 20.73 | base centre, ground level (z = 0), ~2 m buried skirt | 28652/10999/3399 | 1800 | ridge along local X; ramp to the top at +X end; steep cliff on -Z (south), moss on +Z (north) in Unity |
| `Prefabs/Environment/Phase2/LM_AncientFallenTree.prefab` | Prehistoric Wetland (ENV-A) | 38.27 x 7.96 x 10.72 | base centre; z = 0 is the intended water line (crown end sunk to -1.5 m) | 31000/10999/3500 | 1600 | trunk along local X: root plate at -X (rises ~6.5 m), broken hollow crown end at +X; moss on top and +Z (north) in Unity |
| `Prefabs/Environment/Phase2/LM_FossilSkeleton.prefab` | Bone / Carcass Valley (BONE) | 40.66 x 7.47 x 23.45 | base centre, z = 0 = surrounding ground (mound edge sinks to -0.4 m) | 36885/13000/4200 | 2600 | head / skull at local +X, tail at -X; rib arches 4-6 m tall (walk-under gaps); mound bank on -Z side in Unity |
| `Prefabs/Environment/Phase2/LM_GiantTree.prefab` | Giant Fern Forest (ENV-B) | 42.27 x 59.59 x 40.3 | trunk base centre at ground level (buttresses go ~1 m below z = 0) | 31873/9753/2898 | 1500 | collider covers trunk + buttresses up to 12 m; canopy is cut-out cards (M_P2_Canopy, double sided); wood tris per LOD 22985/7999/2200 |
| `Prefabs/Environment/Phase2/LM_BlackRidge.prefab` | Volcanic Foothills (ENV-B) | 52.76 x 19.04 x 20.45 | base centre, ground level (z = 0), columns ~2 m below | 32797/12000/3600 | 1800 | ridge along local X; steeper face on -Z (south) in Unity, talus of fallen columns mostly on that side |

Thumbnails: `Documentation/Screenshots/Phase2/Art/<Name>.png`. Blender sources: `E:\Model game khủng long\PHASE2_Landmarks.blend`,
scripts `E:\Model game khủng long\scripts\phase2\*.py` (one per asset family, regenerable), FBX `export\phase2\`.
