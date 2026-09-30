# RES agent report (R_), Phase 1 wave 1 (2026-09-30)

Compile + Console + reference checks only (no PlayMode). All four tasks done. Bridge cycles R_r1..R_c7 (08:32-08:38 and
08:40-08:42 UTC): Refresh -> Ping -> ConsoleCheck **0 errors, 0 warnings** before and after every builder run. The scene was
saved by the builder (`EditorSceneManager.SaveScene`, logged "scene saved"). `PrimalEditorBridge.SaveScene` does not exist
in the bridge ("not found or not marked [PrimalBridgeCommand]").

## Files
Changed (backups `*.before_P1`): `World/WorldPickup.cs`, `World/ResourceNode.cs`, `World/TreeHarvest.cs`,
`World/GatheringSystem.cs`, `World/GatherToolDefinition.cs`, `Editor/PrimalResourceBuilder.Data.cs`,
`Editor/PrimalResourceBuilder.World.cs`, `Tests/PlayMode/ResourceTests.cs`, `Tests/PlayMode/ResourceIslandTests.cs`,
`Documentation/RESOURCE_SYSTEM.md` (section 13 + table rows).
New: `Editor/PrimalResourceBuilder.Phase1.cs` (bridge `Phase1` "" / "dry" / "icons", `Phase1Capture`).
Assets made by the builder: `Data/Items/ITEM_wreck_scraps / ITEM_wreck_nails / ITEM_sailcloth` (listed in the ItemDatabase),
`Art/Icons/ICON_WreckScraps / ICON_WreckNails / ICON_Sailcloth.png` (rendered from the models),
`Prefabs/Resources/Items/PFB_ITEM_*` (3), `Prefabs/Resources/Models/MDL_NailedTimber / MDL_TornSail`,
`Art/Models/Generated/MESH_*` (5, original low poly, existing ship materials), `Data/Resources/RES_salvage_planks / nails / sail`
(+ `RES_rare_bones / hide / wreck_scraps` now written), node prefabs `Resource_Salvage_Planks / Nails / Sail`, `Resource_Rare_WreckScraps`.
Logs: `Documentation/Phase1/R_phase1_build_run1.txt` (first run, all moves), `R_phase1_build.txt` (last run),
`R_phase1_build_dry.txt`. Captures: `Documentation/Screenshots/Phase1/R_*.png` (wreck salvage, each salvage node, items on the ground, spawn view).

## 1. Shipwreck materials
- Items: `wreck_scraps` Shipwreck Scraps (SURV's definition verbatim: 0.3 kg, stack 20, fuel 60 s), `wreck_nails` Iron Nails
  (0.05 kg, stack 50), `sailcloth` Sailcloth (0.4 kg, stack 10, fuel 30 s). Each has a generated world model and icon.
- 10 hand nodes under `[Resources]/Shipwreck`, 13-16 m from the hull centre, all on dry sand: Broken planks x3 (scraps 2-3,
  bonus nails 30 %, respawn 72 h), Nailed timber x2 (nails 2-4, bonus scraps 35 %, 96 h), Torn sail x2 (sailcloth 1-2, bonus
  scraps 30 %, 120 h), Wreckage x3 (scraps 1-2, bonus nails 25 %, 96 h). Prompt "Salvage" / "Search Wreckage". Fixed save ids `rn_wreck_<def>_<i>`.
- Recipes: `cloth_bandage` and `scrap_rope` are not in the project yet (they are made by SURV's `PrimalSurvivalBuilder.Build`
  -> `PcPhaseItems`, which skipped them while the item was missing). The ingredient now resolves: request below.

## 2. Physical dropped items (`WorldPickup`)
Box collider around the model + Rigidbody (mass = item weight, interpolated, continuous speculative, low-bounce physics
material), Ignore Raycast layer, `Physics.IgnoreCollision` with the player's colliders. A drop next to the player (all current
callers: DropActive, inventory drop, GiveOrDrop, crafting leftovers) leaves the chest 0.55 m in front and is tossed forward
(1.6 m/s + 1.2 up, wall check). It settles (sleep or slow for 0.4 s, max 12 s) and goes kinematic; fall-through guard puts it
back on the terrain. A caller that poses the pickup right after spawning (Projectile's stuck arrow) keeps it pinned. New
`WorldPickup.Throw(stack, pos, velocity)` and `Pin()`. Pick-up prompt, stack data (durability, water, food age) and
`SaveData.dropped` unchanged; `FocusPoint` follows the collider. Old signatures kept.

## 3. Tool gating
Data (BuildData): trees, small logs, deadwood need an axe; large rocks and boulders a pick: `handsFactor` 0, prompt second line
"Need an axe" / "Need a pick" (greyed, `CanInteract` false), punches only give the hint. TreeHarvest shows "Chop Tree / Need an
axe" by hand and gives no twigs. The hand stone became a crude tool (`GatherToolDefinition.heavyWork = false`, new field,
default true): still faster on hand nodes, never fells a tree or breaks a boulder. Hands keep branches, driftwood, small /
medium stones, fibre, berries, fruit, plants, salvage. Carcass knife rule untouched.
Start area (within 60 m of the spawn, hand-gatherable only): stone 11 nodes / 50 units, wood 11 / 30, fibre 8 / 23, berries 6 / 29,
fruit 4 / 7, scraps 6 / 12, nails 2 / 4, sailcloth 2 / 2. Needs for stone axe + pick + knife + campfire: stone 10, wood 7, fibre 7
(rope 3 fibre), bone 1. Everything was enough except bone (0): added one "Old bones" pile at the forest edge 49 m from the spawn
(`[Resources]/StartArea/P1_01_bone_Forest`, 2 bones). Tool-gated nodes within 60 m: 4 large rocks.

## 4. Nodes in water
My check (visible fresh water cells, 1 m from its edge, or below sea level + 0.45 m): `[Resources]` 10 in water, `World/Resources`
5; all 15 moved to the nearest dry free spot (3-10.5 m), none left. ENV's report (23 + 5) uses its terrain wet mask, so it counts
more of the wet shore. Also: 285 `[Resources]` + 24 `World/Resources` nodes were floating / buried after terrain v2 (worst
-18.25 m / -5.78 m): snapped to the ground. 5 fish shoals re-seated on the new river bed (depth 0.65-0.93 m). 5 converted rocks
standing in water (`World/Rocks`) had their node switched off (rock kept as scenery). 1 pair of moved nodes ended up on top of
each other: separated. Final VERIFY: 1435 active nodes, off the ground 0, in fresh water 0, below sea level 0.
Overlap (nothing deleted): `[Resources]` 487 nodes, `World/Resources` 45 (driftwood 10, stone_small 12, fiber_plant 14, berry 9);
0 pairs within 1.5 m across the sets, 0 same-item doubles, 0 pairs within 0.3 m inside either set.

## Requests
| To | Request |
|---|---|
| SURV | run `PrimalSurvivalBuilder.Build` (PcPhaseItems) so `RCP_cloth_bandage` (scraps + fiber -> bandage) and `RCP_scrap_rope` (scraps -> rope) are created; ITEM_wreck_scraps now exists and is kept by your Item() |
| SURV / BUILD | give `sailcloth` and `wreck_nails` a use (e.g. sailcloth in tent / bedroll / bandage, nails in door / storage); they are gatherable but no recipe uses them yet |
| U | optional: `Projectile.Land` can call `pk.Pin()` for stuck arrows (auto-detected now); thrown items can use `WorldPickup.Throw` |
| Lead / owner | confirm the hand stone no longer fells trees / breaks boulders, and logs need an axe (my reading of "hands keep branches"); the flint knife needs 1 bone, so a bone pile now lies near the start |
| Lead | bridge has no `SaveScene` command; builders save the scene themselves |

## Not done
PlayMode tests not run (policy). `ResourceTests` / `ResourceIslandTests` updated for the gating (compile only). No git.
