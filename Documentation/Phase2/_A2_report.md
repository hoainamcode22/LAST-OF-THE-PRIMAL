# A2 (ART) report, Phase 2 World Exploration (2026-09-30 / 10-01)

## Done
- Blender 5.2.1 (interactive, MCP): new working file `E:\Model game khủng long\PHASE2_Landmarks.blend`. The open file was `PHASE2_Assets.blend` (dirty); its state was saved as a copy to `checkpoints\PHASE2_Assets_unsaved_state_A2_20260930.blend` first, and nothing was saved over it.
- Scripts that regenerate everything (`scripts\phase2\`): `p2_common.py` (numpy noise, voxel remesh sculpt, LODs, smart UV + pack, Cycles bakes, FBX export, manifest, long jobs on a timer), `lm_rockridge.py`, `lm_fallentree.py`, `lm_fossilskeleton.py`, `lm_gianttree.py`, `lm_blackridge.py`, `cave_kit.py`, `props_p2.py`, `p2_textures.py` (tileable / atlas textures), `p2_shots.py`, `p2_delivery.py`, `unity\PrimalPhase2ArtBuilder.cs` (source copy).
- Textures: baked per asset (albedo, tangent normal from the high poly, mask T_*_M = R metal 0, G AO, B AO, A smoothness). Landmarks 2048, props 1024. Tileable 2048: T_P2_CaveRock, T_P2_CaveWet. Canopy atlas T_P2_Canopy (2048 RGBA cut-out). Detail normals 1024: DetailStone / DetailBark / DetailBone (URP Lit detail normal on the big baked assets).
- FBX: ENV kit settings (-Z forward, Y up, apply unit scale, bake space transform). Meshes are turned 180 deg about Z for the export only, so Unity gets Blender +X = +X and Blender +Y = +Z (north). 19 FBX in `export\phase2\` and `Assets/_Project/Art/Environment/Models/Phase2/` (+ `P2_Manifest.json`), 45 PNG in `Art/Environment/Textures/Phase2/`.
- Unity: new `Assets/_Project/Scripts/Editor/PrimalPhase2ArtBuilder.cs`. `Build [names]`: import settings, URP Lit materials in `Art/Environment/Materials/Phase2/`, prefabs in `Prefabs/Environment/Phase2/` with LODGroup, MeshCollider from `*_COL`, static flags. `Check`: missing refs + tris per LOD + LOD0 bounds.
- Batch 1 in Unity (lock hold A2 14:51 to 15:05 UTC): compile OK, ConsoleCheck 0 errors / 0 warnings, Build: 5 landmark prefabs, 0 warnings. Listed in `Documentation/Phase2/ART_DELIVERY.md`.
- Thumbnails: `Documentation/Screenshots/Phase2/Art/LM_*.png` (6).

| Asset | Size m (Blender X x Y x Z) | Tris LOD0/1/2 | COL |
|---|---|---|---|
| LM_RockRidge | 57 x 20.7 x 15.7 | 28652/10999/3399 | 1800 |
| LM_AncientFallenTree | 38.3 x 10.7 x 8.0 | 31000/10999/3500 | 1600 |
| LM_FossilSkeleton | 40.7 x 23.5 x 7.5 | 36885/13000/4200 | 2600 |
| LM_GiantTree (wood + canopy) | 42.3 x 40.3 x 59.6 | 31873/9753/2898 | 1500 |
| LM_BlackRidge | 52.8 x 20.5 x 19.0 | 32797/12000/3600 | 1800 |
| CAVE_Wall_A / B | 6.2 x 6.5 x 8.0 | 5408/1424 | 392 |
| CAVE_Tunnel_Straight | 6.2 x 7.5 x 7.0 | 6400/1664 | 448 |
| CAVE_Tunnel_Bend | 10 x 10 x 7 | 9472/2432 | 640 |
| CAVE_Chamber_Dome | 31.3 x 31.7 x 16.2 | 8327/2293 | 783 |
| CAVE_PoolRim (90 deg arc) | ~10 x 10 x 2.3 | 3768/1004 | 304 |
| CAVE_Stalactites | ~5 x 4 x 3.9 | 4200/1500 | none |
| CAVE_Rubble | ~5 x 4 x 1.6 | 3800/1398 | 1398 |
| PROP_P2_DeadSnag_A / B | 8.6 m / 6.4 m tall | 3770/1799/599, 4156/1999/649 | 320 |
| PROP_P2_Ribcage | ~4 x 4.5 x 2.5 | 7000/2400/800 | 500 |
| PROP_P2_Skull_Large | 1.7 m long | 6000/2000/700 | 500 |
| PROP_P2_BoneScatter_A / B | ~3.5 x 3 m | 4199/1500/500, 3999/1400/499 | 500 |

## Not done (blocked)
- Batch 2 in Unity (cave kit + props: `PrimalPhase2ArtBuilder.Build` for the 13 remaining assets), `Check` for all 19, final ConsoleCheck, and the ART_DELIVERY.md update for batch 2. The bridge lock stayed with other agents (P held it 16:10 to ~17:48, the editor did not answer bridge commands 16:20 to ~17:28), and from ~18:20 UTC the PC was unreachable.
- The 5 landmark FBX were re-exported after batch 1 with the orientation fix (180 deg about Z). Unity reimports them on its own and prefabs keep their mesh references, but the new orientation has not been checked in Unity yet. Expected LM_RockRidge LOD0 bounds centre in Unity: (-1.8, 6.6, -0.5). If Check shows (1.8, 6.6, 0.5), remove the rotation in `p2_common.export` and re-export.

## Next hold (about 3 minutes)
Run `PrimalPhase2ArtBuilder.Build ""`, then `PrimalPhase2ArtBuilder.Check ""`, then `PrimalEditorBridge.ConsoleCheck ""`. Then run `python3 p2_delivery.py <check result json> "<note>"` in `scripts\phase2`.

## Notes / requests
- The black ridge has a lot of surface in its joints, so its 2048 bake is only about 13 px/m (the detail normal helps up close). The other landmarks are 20 to 50 px/m.
- Unity auto-imported the FBX and PNG files when they were copied into Assets, which happened while other agents held the lock (no compile was involved). The only C# change went in during my own hold.
- The canopy uses URP Lit cut-out, double sided, with no wind. It could move to PF/Foliage Wind later (WORLD / ENV-B).
