# Phase 2 finish queue (Lead, 2026-10-01)

Run by ONE agent (or the Lead) in this exact order, holding the bridge lock for the whole queue (no other agent runs).
Editor must be open and NOT in Play mode. Fresh ids `F_<n>_<hhmmss>`.

0. Free the stale lock: `Library/PrimalBridge/bridge.lock.taken` says "W 18:07:42" (W lost the PC while holding it).
   Remove a leftover `command.json` only if it is an old Refresh / Ping (harmless otherwise).
1. Copy container reports to the PC `Documentation/Phase2/`: `EA/_EA_report.md`, `P_p2/_P_report.md`, `a2/_A2_report.md`,
   `bv/_BV_report.md`, `W/_W_report.md` (replace the stale one), `eb/` notes as `_EB_report.md`.
2. Refresh -> Ping -> ConsoleCheck. Uncompiled code now in Assets: W7 (16 files), EA8, CAVE builder update, P WildlifeConfig
   (staged). If any `error CS`: fix or restore that agent's backup from `Tools/_backups_P2/` (W) / `Tools/pf_up_EA7.zip` (EA).
3. Deploy staged files: `Tools/_P2_stage/AI/WildlifeConfig.cs`, `Tools/_eb_staging/PrimalZonesBuilder.FernForest.cs`,
   `Tools/_bv_staging/PrimalZonesBuilder.BoneValley.cs` (v3). Refresh -> Ping -> ConsoleCheck (0 errors).
4. ART batch 2: `PrimalPhase2ArtBuilder.Build ""`, `Check ""` (LM_RockRidge LOD0 bounds centre must be near (-1.8, 6.6, -0.5)),
   then `python3 scripts/phase2/p2_delivery.py` to update ART_DELIVERY.md.
5. Zones, each followed by SaveScene:
   a. `PrimalZonesBuilder.Valley "terrain;props"`, `Wetland`, `ValleyCheck`, `WetlandCheck` (expect 0 covered nodes).
   b. `PrimalZonesBuilder.FernForest`, `Foothills dry`, `Foothills`, `FernFoothillsCheck`.
   c. `PrimalZonesBuilder.BoneValley ""` (landmark placed only on a clear footprint), `BoneValleyCheck ""`, `BoneValley "props"`.
   d. `PrimalCaveBuilder.Build`, `Check`, `Capture` (picks up ART's cave kit).
6. `PrimalWildlifeBuilder.Build ""`, `Phase2Check "survey"` (forbidden-area count 0).
7. `PrimalAtmosphereBuilder.Zones2 ""`, `Zones2Check ""`.
8. RES wave 2: resource nodes at CAVE's 12 RES_Anchors, fish shoals (lagoon east shallows, cave pools), reeds -> fiber in the
   wetland, bones in Bone Valley, stone / basalt in the foothills; keep landmark footprints, trails, routes, knoll view clear.
9. QA: ConsoleCheck, `PrimalHierarchyBuilder.Audit`, `PrefabCheck`, `PrimalQaCheck.Prefabs / Items / Animators`, all zone
   checks again; scene saved, `Island_VerticalSlice` open.
10. Lead: `Documentation/PHASE2_WORLD_STATUS.md`.
