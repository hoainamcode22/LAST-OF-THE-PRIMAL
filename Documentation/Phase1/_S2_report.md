# SURV report (S_), Phase 1 wave 2a, 2026-09-30

Edited directly on the PC (python read-modify-write), `*.before_P1b` copies next to every changed file. No git, no deletes,
no PlayMode runs, scene not touched (no SaveScene needed).

## Done
| # | Task | Result |
|---|---|---|
| 1 | Salt-water tests | `SurvivalM1Tests.Boiling_Salt_Water_Gives_Clean_Water_Minus_A_Charge` -> `Boiling_Salt_Water_Is_Refused_And_Stays_Salt` ([Test], no waiting): even with a config that would boil salt, `CanBoil` false, `TryBoil` on a lit fire false, container stays in the pack (same stack), nothing on the fire, still SaltWater, 3 charges. Class summary updated. `SurvivalM1LoopTest` step 6: fills the cup at the nearest fresh unboiled `WaterSource` (closest to the camp, fallback first one), asserts DirtyWater, boils it at the fire -> clean, drinks; the sea / shore search is gone; summary + log text say pond water. `SurvivalPcPhaseTests` line 120: "scraps have two uses" (== 2) -> wreck_scraps, wreck_nails, sailcloth each have >= 1 use |
| 2 | Salvage recipes | New bridge command `PrimalSurvivalBuilder.Phase1b` (only creates missing recipe assets + database entries, never touches existing items / recipes); `WreckRecipes()` is also called from `Phase1Content`, and the old cloth_bandage / scrap_rope lines in `PcPhaseItems` were removed so `Build` makes the same recipes. `cloth_bandage` Survival: 1 sailcloth + 1 fiber -> 1 bandage (2 s; normal bandage 4 fiber + 1 hide). `scrap_rope` Resources: 2 wreck_scraps -> 1 rope (2 s; normal rope 3 fiber). `nailed_crate` Storage: 4 wood + 4 wreck_nails -> 1 storage (6 s; lashed storage 6 wood + 4 fiber + 1 rope). All learned when a material is picked up. Uses: wreck_scraps -> scrap_rope, wreck_nails -> nailed_crate, sailcloth -> cloth_bandage |
| 3 | HUD status icons | Layout is code-driven (`StatusIcons` is not in the saved scene; the baked Vitals / Status line match the code). `HUDManager.cs:147-151`: the row moved from the right of the vitals (x 364, where it overlapped the perception "Presence" indicator at x 352-448) to under the vitals panel, left aligned at (24, -254): panel 24 + 196 px, status words line 26 px, 8 px gap; 8 x 40 px = 320 px fits the 330 px panel. Canvas 1920x1080 reference |

## Files
Changed: `Tests/PlayMode/SurvivalM1Tests.cs`, `Tests/PlayMode/SurvivalM1LoopTest.cs`, `Tests/PlayMode/SurvivalPcPhaseTests.cs`,
`Scripts/Editor/PrimalSurvivalBuilder.cs`, `Scripts/UI/HUDManager.cs`.
New assets: `Data/Recipes/RCP_cloth_bandage.asset`, `RCP_scrap_rope.asset`, `RCP_nailed_crate.asset`. Changed: `Resources/ItemDatabase.asset` (+3 recipes).

## Commands (lock S 09:11-09:19 UTC, then compile check via the 09:22 refresh)
- `S2_r/p_091156` Refresh + Ping ok; Runtime / Editor / Tests.PlayMode dlls rebuilt 09:12. `S2_c_091300` ConsoleCheck: 0 errors, 0 warnings.
- `S2_b_091548` `PrimalSurvivalBuilder.Phase1b`: 3 recipes created; 50 items (+0), 35 recipes (+3, cap 40). Tabs: Tools 6, Weapons 6, Survival 3, Food 1, Structures 8, Water 4, Resources 3, Fire 2, Storage 2.
- `S2_k_*` `Phase1Check`: items 50, recipes 35; null items 0, null recipes 0, bad recipes 0, no icon 0, missing scripts 0, empty material slots 0; salt boil 0 s -> SaltWater.
- `S2_c2_*` ConsoleCheck: 0 errors, 0 warnings.
- SurvivalPcPhaseTests fix: Tests.PlayMode.dll rebuilt 09:22:25 with the new assert string (compiled). Console at 09:25 (U's check `U2_c1_0923`): 1 error, not mine: `Editor/PrimalVolcanoBuilder.cs(317,35) CS0117 PrefabUtility.GetPrefabAssetPathOfNearestPrefabInstanceRoot` (ENV).

## Requests / notes
| To | Note |
|---|---|
| Lead | Lock collision around 09:21-09:25: E_ and U_ ran bridge commands at 09:21-09:22 while the lock was free / changing hands; my acquire returned GOT at 09:25 (Refresh + Ping only, harmless), then the lock file was overwritten with "U 09:21:44 ... ~15 min". I did not release it (not mine now) and ran nothing more |
| ENV | `PrimalVolcanoBuilder.cs:317` compile error (API does not exist in Unity 6.3; e.g. `PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot`) |
| Lead / owner | Balance check wanted by hand: nailed_crate (4 wood + 4 nails) vs lashed storage; nails 2-4 per nailed-timber node, 4 in the start area |

## Not done / not tested
- No PlayMode runs (policy): the rewritten tests only compile. HUD status row position: MANUAL TEST REQUIRED (catch a status effect, e.g. rain -> Wet).
