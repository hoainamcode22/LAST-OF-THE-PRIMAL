# RES agent report (R_), Phase 1 wave 2a (2026-09-30)

No PlayMode, no git, no deletes. Bridge cycle 09:06-09:12 UTC (16:06-16:12 editor time) (lock taken and released): ConsoleCheck before (R2_c0) and
after (R2_c1) **0 errors, 0 warnings**. Scene saved by the command and again by `PrimalEditorBridge.SaveScene` (R2_s1).

## Files
New: `Scripts/Editor/PrimalResourceBuilder.Lava.cs`, bridge `PrimalResourceBuilder.Phase1Lava` ("" apply + save, "dry" report
only; idempotent, a re-run finds nothing to move). Reads ENV's lava line and vent from
`Art/Environment/Terrain/env_features_v2.json`. Logs: `Documentation/Phase1/R2_lava_build.txt` (apply),
`R2_lava_build_dry.txt` (last dry run). Deploy zips `Tools/pf_up_R2a.zip`, `pf_up_R2b.zip` (this file only).

## 1. Vent rock
Two rocks in `World/Rocks` are named `PFB_ENV_Rock_Large_03`; the one on the vent is the one nearest to it (the other is 90 m
away at (-128.3, 34.2, -146.5) and was not touched). `stone_boulder` node, footprint r 3.5 m.
- Moved (-102.4, 45.9, -230.2) -> (-99.2, 45.0, -239.4), 9.8 m. Bounds centre now 7.9 m from the vent (was 2.6 m; pool r 1.9 m),
  7.9 m from the channel (was 0.1 m), slope 2 deg.
- Placed behind the vent as seen from the spawn / island centre (direction score -0.91, -1 = directly behind), so it no longer
  hides the vent pool. Dry ground, no fresh water within 2.5 m, no other collider in its footprint, same ground offset.
- Still a working node (component and data unchanged, pick required as before).

## 2. Nodes by the lava channel
ENV's 3 nodes within 3 m of the channel. None is a basalt / obsidian-style node (no such resource exists), so all moved to
5-10 m from the channel:
| Node | Def | From | To | Channel dist |
|---|---|---|---|---|
| `World/Rocks/PFB_ENV_Rock_Large_03` | stone_boulder | on the vent | see above | 0.1 -> 7.9 m |
| `World/Rocks/PFB_ENV_Rock_Large_02` | stone_boulder (r 3.9 m) | (-127.6, 45.0, -211.1) | (-124.4, 44.3, -207.2), 5.0 m | 2.6 -> 7.6 m |
| `[Resources]/Cluster_028_Rocky/Resource_Stone_Medium_0152` | stone_medium | (-137.2, 45.7, -207.3) | (-141.0, 45.0, -208.6), 4.0 m | 2.1 -> 5.4 m |

After: nodes within 3 m of the channel **0** (segment distance and ENV's point distance). One node remains within 6 m of the
vent: `Cluster_039_Rocky/Resource_Stone_Medium_0186` (small medium stone, outside the pool + 2 m and over 3 m from the channel):
kept, it does not block the vent.

## Checks
- `Phase1Lava` VERIFY: 1435 active nodes, off the ground 0, in fresh water 0, below sea level 0.
- `Phase1 "dry"` (my wave 1 ground / water check, R2_d4): in water 0, to snap 0, nodes to separate 0, scenery nodes in water 0.
- ConsoleCheck R2_c1: 0 errors, 0 warnings.

## Requests
| To | Request |
|---|---|
| ENV | optional: re-run your `Check` to confirm "resource nodes within 3 m of the channel: 0" and nothing left on the vent |
| HIER / Lead | two `World/Rocks` objects share the name `PFB_ENV_Rock_Large_03` (and possibly others): consider unique names in the hierarchy pass |
