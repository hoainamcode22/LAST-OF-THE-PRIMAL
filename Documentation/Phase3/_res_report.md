# RES agent report (R_): resources, gathering, fruit P0, bare-hand strikes, island pass (2026-09-29)

Scope: Phase 3.5 directive 10-33, 36-38, 41-42 (resource side), 44 (gather button), 46, 48-52; Phase 3 16, 17
(efficiency), 46, 72. Design and numbers: `Documentation/RESOURCE_SYSTEM.md`. Code in the cloud mirror, deployed with
hand-made zips `pf_up_R1` .. `pf_up_R8` (only my files), every deploy compiled with no errors. Scene pass run with
`PrimalResourceBuilder.Build` (log `Documentation/Resources/resource_build.txt`). No git.

## 1. Results

| # | Task | Status | Evidence (PlayMode, editor 6000.3.10f1, runs R_12 and R_14: 16/16) |
|---|---|---|---|
| 1 | P0 fruit harvest (`FruitCluster` read `Add`'s "did not fit" as "added") | **PASS** | Fixed: `added = want - inv.Add(...)`; fruit that does not fit stays on the branch (`Left`). `Fruit_With_Room_Goes_Into_The_Pack_And_The_Bunch_Empties` (3 in the pack, bunch empty, second harvest refused), `Fruit_With_A_Full_Pack_Stays_On_The_Tree` (0 taken, still ripe, 3 left), `Fruit_That_Does_Not_Fit_Stays_On_The_Branch` (2 in, 1 left), `Fruit_Grows_Back_Only_After_Its_Time`; island: a real tree cluster +3 then empty, full pack gives nothing |
| 1b | Fruit state saved through SURV's `ISaveSection` | **PASS** | `ResourceSaveSection` ("resources": fruit left / regrow time per cluster, stable ids `fruit_<tree>_<n>`, plus chop hits on standing trees), registered by ResourceManager. `Save_Load_Keeps_Resource_State...`: picked bunch still empty after load, same regrow time; a used node keeps its amount |
| 2 | `ResourceDefinition` + `GatherToolDefinition` ScriptableObjects, one `ResourceNode` | **PASS** | 17 `RES_*` + 7 `TOOL_*` assets in `Data/Resources`, `Resources/ResourceDatabase`. All 1429 scene nodes have a definition (test). No second node class. Hands 1 stone / action, pick 3-5 (`Hands_Give_One_Stone_A_Pick_Three_To_Five`: 300 rolls all in 3-5, all three values seen; island: large rock hands 1 per 3 actions, pick 5), axe 2-3 on wood, knife 2-3 on fibre / food; large trees: hands 2 twigs in 12 actions with the hint, never felled; axe 2+3+3+3+2+2 in 6 hits then felled |
| 3 | Bare-hand strikes on small nodes (`IDamageable`, `HitInfo.unarmed`) | **PASS** | `ResourceNode` and `TreeHarvest` are IDamageable: damage / damagePerUnit x handsFactor units (jab 4 = 1 small stone, heavy 9 = 2). `Bare_Hand_Strike_Gives_A_Small_Result_Tools_Do_Not`; island: a real punch (Sim.Attack) on small stones +1, U's hitbox reported the node. Weapons / tools do nothing by hitting (they gather with E) |
| 4 | Node variants | **PASS** | Stone small 2-4 / medium 4-8 / large 8-15 (+ boulder 8-15 for the 6 m rocks); wood branch 1-3, small log 4-8, driftwood 2-5, deadwood 5-8, tree (axe); fibre plant 2-4, long grass 1-3, fern 1-3, small bush 2-4; berry bush 4-6, fallen fruit 1-3, edible plant 1-2, fish shoal 2-3. Prefabs `Prefabs/Resources/Nodes/Resource_*` (14 + 2 with SURV's models). Missing shape made new: `RES_Branch_01/02` (original forked fallen branches, 124 / 92 faces, background Blender process, generator `Tools/BlenderPipeline/res_models/res_models.py`, preview `preview_branches.png`); a generated berry clump mesh for the berry bushes. The character .blend was not touched |
| 5 | Visual states full / damaged / depleted / regrowing; slow respawn | **PASS** | Scale / settle / tilt, rubble / stripped / mined / hidden-with-a-burst, berries off (material slot swap) and green in the second half, fruit hidden then small green fruit swelling. `Node_Looks_Full_Damaged_Depleted_Regrowing`, `Hidden_Node_Never_Pops_In_Next_To_The_Player`; island: fibre gone with a puff, not back instantly, not while the player stands there, back once the player is 45 m away. Respawn 12-72 game hours (tree 72, stone 30-60, fruit 30, fibre 12-20) |
| 6 | Feedback: "+3 Stone", inventory sound, pooled particles per type, gather animation per type | **PASS** (animations: U's placeholders) | Island: three quick gathers show one merged note "+3  Stone" (HUD feed verified, no change needed). Per hit: stone fragments + dust, wood chips + bark dust (+ leaves on trees), leaves on plants / fruit; `StoneGatherHand` / `StoneHit`, `BranchSnap` / `WoodChop`, `LeafRustle`; soft `Pickup` sound; bigger burst on the last unit. Animations `GatherStoneHand` (16), `GatherBranch` (17), `GatherStone`, `GatherWood`, `GatherPlant`; reach re-checked every hit, the body turns to the node |
| 7 | Readability: highlight on the current target only, prompts | **PASS** | `InteractionHighlight` (MPB base colour +16 % pulsing): 26 / 26 targeted nodes glowed, no highlight left elsewhere (test). Prompts `Gather Stone` / `Gather Wood` / `Gather Fiber` / `Harvest` (every node near the spawn checked), `Chop Tree` with an axe. No floating icons |
| 8 | Performance: no per-node Update, one manager, pooling | **PASS** | Reflection test: ResourceNode / FruitCluster declare no Update / LateUpdate / FixedUpdate (803 per-node Updates removed). Island with 1429 nodes: `ResourceManager` 0.0024-0.0055 ms per frame average (editor). Particles and sounds pooled; prompt strings cached; loose nodes cull by LODGroup, small ones cast no shadow |
| 9 | World pass (`PrimalResourceBuilder`, idempotent, logged, scene saved), captures | **PASS** | Audit before (`Documentation/Resources/scene_audit_before.txt`): 136 small rocks and 14 fallen logs looked gatherable but were not; within 40 m of the spawn: 0 stone, 0 fibre nodes. After: 1429 nodes (803 before). Converted: 136 small rocks -> Stone, 426 medium rocks -> Large rock, 254 large rocks -> Boulder (by hand now too, slowly), 14 logs -> deadwood, 75 berry bushes got a visible crop. New `[Resources]` root, 476 nodes: start area 19 (first stones 7 m from the spawn), trail to the pond 5 clusters / 13 nodes, 12 bank clusters / 33 nodes + 7 fish shoals, fallen fruit under the 4 fruit trees, 149 biome clusters / 400 nodes (forest 63, rocky 29, forest edge 25, beach 18, meadow 13). Within 40 m of the spawn: stone 6 nodes / 26 units, wood 8 / 22, fibre 6 / 17, food 6 / 21. Test `Start_Area_Is_Stocked_And_Leads_To_Water`: nodes chain from the beach to the pond (137 m) with no gap over 30 m. No terrain change, no island growth. Captures `Documentation/Screenshots/Resources/` |
| 10 | Tests | **PASS** | `ResourceTests` 11 (no scene) + `ResourceIslandTests` 5 (island, `UseTestSaves`, `[Timeout]` on every UnityTest, no WaitForEndOfFrame): 16/16. Full suite: see section 5 |
| - | Docs | done | `Documentation/RESOURCE_SYSTEM.md`, this report |

## 2. Files (mine)

New: `World/ResourceDefinition.cs`, `World/GatherToolDefinition.cs`, `World/ResourceDatabase.cs`, `World/GatheringSystem.cs`,
`World/ResourceManager.cs`, `World/InteractionHighlight.cs`, `World/ResourceSaveSection.cs`,
`Editor/PrimalResourceBuilder.cs` (+ `.Data.cs`, `.World.cs`, `.Capture.cs`), `Tests/PlayMode/ResourceTests.cs`,
`Tests/PlayMode/ResourceIslandTests.cs`.
Changed: `World/ResourceNode.cs` (rewritten on the definitions; old public members kept), `World/FruitCluster.cs` (P0,
partial fit, no Update, regrow look, save id), `World/TreeHarvest.cs` (definition, hand twigs, punches, `TreeFelled`,
hit save), `Player/PlayerFeedback.cs` (per-type gather feedback, `NodeDrivesGatherFx`, `LastFootSurface`),
`Core/GameEvents.cs` (appended `TreeFelled`, `ResourceDepleted`).
Assets (PC, by the builder): `Data/Resources/RES_*.asset`, `TOOL_*.asset`, `Resources/ResourceDatabase.asset`,
`Prefabs/Resources/Nodes/Resource_*.prefab`, `Art/Materials/M_Berry_Unripe.mat`, `M_Foliage_DryGrass.mat`,
`Art/Models/Generated/MESH_BerryCluster.asset`, `Art/Models/Resources/RES_Branch_01/02.fbx`, the island scene
(`[Resources]` root, definitions on converted nodes, berry crops, fruit save ids, tree definition).
Tools: `Tools/BlenderPipeline/res_models/` (generator, preview, source .blend).

## 3. Requests to other agents

| To | Request | Why |
|---|---|---|
| U | `MobileHUD` label: map "Catch" (fish shoals, "Catch Fish") to GATHER or HARVEST (shows USE today) | directive 42 |
| U | `BareHandIslandTests.Touch_Buttons_Show_The_Context` picks the nearest free-standing node and expects GATHER; it passes (the first stones are nearest), but a food node there would correctly say HARVEST. Accept HARVEST or skip food nodes to keep it robust | section 5 |
| U / CHAR | real Gather_Stone_Hand / Gather_Branch clips (actions 16 / 17 already used by the stone / wood definitions) | placeholders are Gather_Plant |
| AI | `GameEventType.TreeFelled` now raised in `TreeHarvest.Fell` (id wood, position the tree) and `PlayerFeedback.LastFootSurface` exists: switch `PlayerSignature.CheckFelled` / footstep loudness to them | your requests, done |
| AI | optional: `Carcass` could take the knife numbers from `ResourceDatabase.Instance.ToolFor(knife)` (Food 2-3) so butchering follows the same tool table | directive 28 |
| SURV | spelling: prompts say "Gather Fiber" (task wording) while the item is "Plant Fibre"; pick one (owner) and I change the definitions | consistency |
| SURV | fish shoals use `raw_fish`'s world model on the bed of shallow water; under the water shader they read weakly. A small ripple / fin VFX or a surface cue would help | readability |
| Lead | architecture doc: new scene root `[Resources]` (resource clusters) | S8 |
| Lead | `PrimalGameplayBuilder` / `PrimalBushBuilder`, if ever re-run, re-add nodes without definitions (they keep working through the legacy path) and drop the berry crops: re-run `PrimalResourceBuilder.Build` after them | idempotent pass |
| Lead | owner choice: "one action per press, hold to repeat" now applies on every platform (`ResourceManager.HoldToRepeat = true`); set false for the old loop-until-empty | directive 44 |

## 4. Known limits (honest)

- Bridge: I held the lock 11:00-11:27 (forgot to release after the first two deploys) and at 12:19 my chained command
  started a full PlayMode run although SURV had just taken the lock (SURV's queued `S_9` waited for it). Reported to
  the Lead at once.
- The "before" island map was rendered with scene fog (washed out); the "after" map has fog off (the sea renders as
  sand-coloured sea floor from 600 m). Start-area and player-view captures are comparable before / after.
- Fish shoals: 7, reachable by wading in; visibility under the water shader is weak (request above).
- Terrain trees have no damaged / stump look (felled = hidden, as before); the hit count is saved now.
- A Hide node that is due waits while the player is within 18 m and facing it; a player camping next to it delays it.
- Editor timings, no phone profile.

## 5. Full suite

Run `R_13` (2026-09-29 19:32 editor time, after `pf_up_R7` / `R8` and the island pass): **143 passed, 0 failed,
8 skipped** in 715 s (skipped: the `CharacterMotionProbe` diagnostics and the capture-only showcase tests). The summary
is kept in `Documentation/Tests/playmode_tests_R13.txt`; the per-test list and XML were overwritten by SURV's next run
before I copied them. U's `Touch_Buttons_Show_The_Context` passed (the nearest free-standing node is the first stone
pile, GATHER); the request in section 3 is about keeping it robust.
Resource tests alone, run `R_12`: 16 / 16 (ResourceTests 11, ResourceIslandTests 5), 42 s; again after SURV's builder
and deploys, run `R_14`: 16 / 16 in 60 s, manager 0.0055 ms per frame with 1429 nodes (`Documentation/Tests/playmode_tests_R14.txt`,
`Tools/playmode_results_R14.xml`).
Compile: `./cc.sh all` runtime / editor / tests rc 0 in the mirror; a PC-state build (SURV's undeployed files swapped for
the PC copies) also compiled before the first runtime deploy.
