# ENV (E_) Phase 1 wave 2a report, 2026-09-30

## Result
The offshore `World/Landmarks/Volcano` (PFB_ENV_Volcano + VolcanoLandmark at (-360, -2, 640)) is on again. It is the
only active VolcanoLandmark and it now erupts. The island copy `World/Environment/Volcano/Lava/VentSystem` is switched
off (SetActive false, eruptions false, kept). On-island ash, basalt, lava channel, cracks, vent smoke / embers / lights
are untouched. Atmosphere and Water builders re-run. Journal sea-water text fixed. Console: 0 errors, 0 warnings.

## Files
- `Scripts/World/VolcanoLandmark.cs` (backup `.before_P1b`): eruption cycle added.
  - Build-up (`rumbleBuildUp` 6 s): low rumble (Thunder, pitch 0.42), camera tremor growing every 0.45 s (scaled by distance), grey smoke puffs, embers.
  - Burst (`fountainTime` 4.5 s): `fountain` particles at 70/s plus an initial blast, `flash` point light flicker (12000, range 520), crater halo and lava emission boosted, 6 dark plume puffs of 190 m, 60 embers, ash boost for 90 s.
  - Boom: Thunder (pitch 0.72) + DinoStepHeavy (pitch 0.45), delayed by distance / 343 m/s, volume falls from 1 to 0.35 between 250 and 1400 m, with a delayed shake.
  - Bombs: pool built at Start (max 16, `bombCount.y`), plain C# state objects, no GC per frame. Ballistic arc solved to hit a target. Terrain / sea checked every frame.
  - Targets: aim lines from the volcano across the island with sideways spread. The first point above sea level is the volcano-facing shore. 30 % land 2..45 m inland, the rest 25..260 m offshore.
  - In flight: spinning emissive rock, FireTrail and SmokeTrail (by distance), SpearWhoosh (pitch 0.35) when within 40 m of the camera.
  - Sea impact: WaterSplash x3.5 + Steam x3 VFX, splash + FireHiss sound delayed by distance, then the rock is hidden.
  - Land impact: DustImpact + CraftSparks + FireIgnite VFX, flames + light that fade over 9 s, emission cooling to a dark rock, stays 60 s, sinks and returns to the pool. DinoStepHeavy + StoneHit thud, `Stimuli.Noise(p, 2.5, Impact, World)` for wildlife.
  - Danger: `bombDamage` 25 within `bombDamageRadius` 2.5 m through `PlayerHealth.TakeDamage(heavy)`; 0 disables. Camera shake within 45 m.
  - Timing: first eruption 90..200 s, then every 120..360 play seconds. `Erupt()` for debug. Raises `GameEventType.VolcanoRumble` ("volcano", 2).
- `Scripts/Editor/PrimalVolcanoBuilder.cs` (backup `.before_P1b`):
  - New bridge commands `Eruption` (build + wire + scene state + save) and `EruptionCheck` (read only).
  - `Offshore()` helper: finds the PFB_ENV_Volcano instance. `PlaceInScene` and `Atmosphere` use it now, so they cannot pick the island copy.
  - `BuildPrefab` includes the eruption parts on a full rebuild. The PFB_ENV_Volcano edit uses LoadPrefabContents, so the scene instance keeps its overrides.
- `Scripts/Editor/PrimalEnvironmentBuilder.Volcano.cs` (backup `.before_P1b`): a re-run of the Volcano pass now creates the VentSystem copy switched off (eruptions false) and keeps the offshore volcano ON (it no longer switches it off).
- `Scripts/Story/JournalSystem.cs` (backup `.before_P1b`), line 268 only: "I filled a container at the shore. Sea water is no good to drink, and boiling it does not help: the water goes, the salt stays. The sea will never be drinking water. I need a stream, a spring or the rain."
- New assets:
  - `Art/Models/Environment/ME_VolcanicBomb.asset`: 320-tri faceted rock, diameter about 1.
  - `Art/Materials/M_VolcanicBomb.mat`: URP Lit, basalt, T_Rock D/N, emission.
  - `Prefabs/Environment/PFB_ENV_VolcanicBomb.prefab`: Rock, FireTrail, SmokeTrail, Flames, Glow light.
  - `Eruption_Fountain` + `Eruption_Flash` children in PFB_ENV_Volcano.
- Existing VFX / SFX ids only, so VfxPool / SfxPlayer / GameEvents are unchanged.

## Commands (bridge ids) and results
- E2r/E2p/E2c_092151: Refresh / Ping / ConsoleCheck: 1 error, `PrefabUtility.GetPrefabAssetPathOfNearestPrefabInstanceRoot` does not exist in 6000.3. Fixed by using GetCorrespondingObjectFromOriginalSource.
  - Protocol slip: this cycle ran without the lock. My acquire lost to U by 7 s and the chained commands still ran. Main has been told. It was only Refresh / Ping / ConsoleCheck, no scene writes.
- Lock taken 09:36:57, released after about 2 min (RELEASED):
  - E2c_0937: ConsoleCheck: 136 entries, 0 errors, 0 warnings.
  - E2s: SceneState: dirty=False.
  - E2e: `PrimalVolcanoBuilder.Eruption`: mesh (320 tris), material, bomb prefab and volcano prefab written. Offshore volcano ON at (-360, -2, 640). VentSystem switched off. Scene saved.
  - E2k: `EruptionCheck`: ERUPTION CHECK OK.
    - Offshore volcano: activeInHierarchy True, component on, eruptions on. Island copy off. Active VolcanoLandmarks: 1.
    - PFB_ENV_Volcano: 16 renderers (13 particle), 0 missing refs. PFB_ENV_VolcanicBomb: 4 renderers (3 particle), 0 missing refs.
    - All 5 VfxIds and 6 SfxIds used have prefabs / clips.
    - 200 sample landings: 140 sea, 60 land, 478..605 m flat from the volcano.
  - E2a: `PrimalAtmosphereBuilder.Build`: 18 ENV locations (31 zones).
    - HZ_volcano at (-122, 44.4, -216), rings 61 / 32 / 10 m, +6 / +14 / +24 C.
    - HZ_vent at (-100, 44.7, -232), rings 20 / 11 / 5 m, +7 / +15 / +30 C, smoke 0.55 over 55 m.
    - 5 lava hazard zones along the channel, 3 lava emitters.
    - Emitters: River 13, Custom 3, Wetland 3, Stream 2, Waterfall 1. 1 cave reverb zone. Scene saved.
    - No heat zone at the offshore volcano: it is 500+ m out at sea and unreachable. The island heat covers the volcanic ridge, vent and channel.
  - E2w: `PrimalWaterBuilder.Build`:
    - Sea level 0. Shore map 1024 x 1024 over 640 x 640 m. Ocean mesh 32761 verts, out to 1500 m, so it covers the sea around the volcano.
    - Shore sheet: 43 chunks. 8 fresh water bodies, all flows reach every wet texel. ENV_Ocean renderer off. Done in 14.4 s, scene saved.
  - E2k2: EruptionCheck after both builders: OK, same numbers.
  - E2c3: ConsoleCheck: 237 entries, 0 errors, 0 warnings.
  - E2sv: SaveScene: saved.

## Config (VolcanoLandmark defaults, all in the Inspector)
- Timing: eruptionEvery 120..360 s, firstEruption 90..200 s, rumbleBuildUp 6 s, fountainTime 4.5 s.
- Fountain and plume: fountainRate 70/s, flashIntensity 12000, plumePuffs 6 x 190 m.
- Bomb count and flight: bombCount 3..12, bombShoreShare 0.3, bombFlightTime 11..15 s, bombLateralSpread 170 m.
- Bomb landing: bombSeaOffshore 25..260 m, bombInland 45 m, bombSize 1.2..2.4 m, bombGlowTime 9 s, bombStayTime 60 s.
- Danger: bombDamage 25, bombDamageRadius 2.5 m, bombShakeRadius 45 m.
- Sound and wildlife: boomVolume 0.9, whooshDistance 40 m, impactHearing 260 m, speedOfSound 343, impactNoise 2.5.

## Notes / not done
- No PlayMode run (policy), so nothing has been seen in play. Please check by hand: the first eruption comes 90..200 s after load. For a quick look, lower firstEruption or call `Erupt()`.
- The volcano-facing shore includes the start beach. The nearest sample landing was 14 m from the player spawn. The damage radius is 2.5 m, so a hit is rare. Lower `bombShoreShare` or set `bombDamage` 0 if the owner wants the start area safe.
- There is no crater decal. The bomb sounds reuse existing clips (Thunder, DinoStepHeavy, StoneHit, SpearWhoosh, WaterSplash, FireHiss) with changed pitch. A dedicated rumble / boom / hiss clip from AUDIO would sound better.
- `PrimalCharacterDiagnostics` and `PrimalReviewCapture` (not mine) still use FindFirstObjectByType<VolcanoLandmark>. With the island copy inactive they find the offshore one.
- No git, no deletes, no terrain change.

## Follow-up (Lead): start beach safe
- `VolcanoLandmark`: `spawnSafeRadius` 120 m (0 = off) and a fallback `spawnSafeCentre` (-20, 1.5, 211).
  - The centre is GameManager.spawnPoint when the scene has one.
  - `PickBombTarget` re-aims any landing within the radius. Up to 6 tries go to other volcano-facing shore, then 6 to the sea. As a last resort the target is pushed just outside the circle.
  - The cached terrain / sea / safe values are `[NonSerialized]`, so a script reload recomputes them.
  - Damage stays 25 outside the safe circle.
- `PrimalVolcanoBuilder.Eruption` writes spawnSafeRadius 120, the spawn centre (from GameManager.spawnPoint) and bombDamage 25 to the scene instance, as recorded overrides.
- `EruptionCheck` flags any sample landing inside the safe radius.
- E3e `Eruption`: "no bomb lands within 120 m of (-20.00, 1.49, 211.00) (GameManager.spawnPoint), damage 25 elsewhere", scene saved.
- E3k `EruptionCheck` gave a stale 14 m, because the reload kept the old cached centre. I added `[NonSerialized]` and ran again.
- E4k `EruptionCheck`: OK. 200 samples: 146 sea, 54 land (483..559 m from the volcano). Nearest landing to the spawn: 121 m.
- E4sv SaveScene: saved. E4c2 ConsoleCheck: 14 entries, 0 errors, 0 warnings. Lock released.
