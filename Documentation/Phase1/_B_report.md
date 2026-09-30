# BUILD (B_) report, Phase 1 wave 1 (2026-09-30)

## Files changed (backups `*.before_P1` next to each)
- `Scripts/Building/BuildSystem.cs`: private `Start(def, item, payCost, prefab, quiet)` renamed `BeginGhost`; 5 callers updated (:77 :86 :407 :412 :413). No other code or test calls it (private; grep of Scripts + Tests clean). Build effect: if `VfxId.BuildDust` is not in the library, falls back to a `VfxId.DustImpact` puff.
- `Scripts/Editor/PrimalBuildingBuilder.cs`: recipe costs now use wood / fiber / stone / rope; doorway FBX door leaf is stood upright (the FBX leaf imported lying flat, doorway depth 2.14 m, now 0.41 m) and an existing flat-door prefab is rebuilt automatically; door collider sized from the leaf bounds; new `[PrimalBridgeCommand] Check` (missing scripts / null meshes / null materials / StructurePiece / definitions / other placeables).
- Created by the builder: `Resources/Structures/STR_{foundation,wall,doorway,roof,leaf_shelter}.asset`, `Prefabs/Building/BLD_{Foundation,Wall,Doorway,Roof,LeanTo}.prefab` (from `Art/Models/Building/BLD_*.fbx`), `Art/Building/Materials/M_Build{Wood,Atlas,Stone}.mat`, `Data/Items/ITEM_{foundation,wall,doorway,roof,leaf_shelter}.asset`, `Data/Recipes/RCP_{same}.asset`, `Art/Icons/ICON_BLD_*.png`, log `Documentation/PCPhase/Build/building_build.md`. `Resources/ItemDatabase.asset`: +5 items (46), +5 recipes (32 of cap 40).

## Pieces (Building tab = RecipeCategory.Structures, learned when a material is picked up)
| Piece | Cost | Sockets / snaps |
|---|---|---|
| Log Foundation | 8 wood, 4 stone, 1 rope | 8 (4 edge, 4 side); snaps to FoundationSide |
| Woven Wall | 6 wood, 6 fiber | 6 (2 top, 4 end); snaps to FoundationEdge / WallEnd |
| Doorway Wall + Door | 7 wood, 6 fiber, 1 rope | as wall; hinged woven door (DoorPiece, E opens / closes, saved) |
| Thatch Roof | 5 wood, 10 fiber, 2 rope | snaps to WallTop, needs 2 walls; enclosure Shelter |
| Leaf Shelter | 5 wood, 8 fiber (known at start) | free standing |

## Commands (bridge, under the lock 05:27 to 06:00 UTC; editor was restarting 05:14 to 05:33)
- Refresh / Ping OK; ConsoleCheck `B_c1`: 4 entries, 0 errors, 0 warnings; `B_c2`: 2 entries, 0 errors (Start() error gone).
- `Build` `B_b1`: 3 materials, 5 prefabs, 5 items, 5 icons, 5 definitions, 5 recipes created; Database +5 items, +5 recipes.
- `Build` `B_b2`: doorway leaf stood up (-90 deg X), prefab rebuilt (3 x 2.49 x 0.41 m); rest kept. `B_b4`: everything kept, +0 / +0 (idempotent).
- `Check` `B_k2`: prefabs 5/5, missing scripts 0, null meshes 0, null materials 0, no StructurePiece 0, no definition 0, definitions 5 (5 complete), `StructureDefinition.All` = 5, doorway leaf upright. Other placeables intact: campfire PFB_Campfire, shelter PFB_Shelter, storage PFB_Storage, bedroll PFB_Bedroll, tent PFB_Tent, rain_collector PFB_RainCollector.
- Final ConsoleCheck `B_c3`: 19 errors, all in `AI/WildlifeWeather.cs` (AI agent mid-edit: WildlifeConfig fields missing); none in BUILD files.
- Scene not touched. `PrimalEditorBridge.SaveScene` and `.SceneState` do not exist in this bridge (only Refresh, Ping, ConsoleCheck), so no scene save was run.

## Ghost validation (verified in code, no change needed)
Ground (raycast + 4-corner ground check), slope (maxSlope per piece), water, support (walls under roofs), distance (6 m + 3 m snap reach, too close), overlap box (terrain ignored), resources (MissingCost), valid / invalid material swap (M_GhostValid / M_GhostInvalid assigned in the scene). Dust (BuildDust, DustImpact fallback) + Build / BranchSnap / LeafRustle sounds when a piece appears.

## Requests
- AI (P_): `WildlifeWeather.cs` references ~19 missing `WildlifeConfig` fields; blocks compile for everyone.
- Lead: AGENT_PROTOCOL lists `SaveScene` / `SceneState`, the bridge has neither.

## Not done / notes
- Door is part of the Doorway Wall (per WAVE2 directive "doorway wall + door"), not a separate snap piece.
- No PlayMode tests (policy). Placement in play not tested by hand.
