# WORLD report (W_): weather, day phases and light, volcano danger, atmosphere audio, zones. PC phase, 2026-09-29/30

All code is in the cloud mirror (`src/`) and compiles (`./cc.sh all`: runtime / editor / tests rc=0; also compiled against the
PC's current copies of every file I do not own: rc=0). The editor was in Play mode 15:34-17:11 UTC, answered 17:11-17:34, and
has been closed since about 17:35 (no `Temp/` folder; my `W_ref2` and STORY's refresh are still unanswered).
In the window I deployed and checked: `pf_up_W1` (code, compiled, no `error CS`), `W2-W4` (ambience WAVs, imported),
`PrimalAtmosphereBuilder.Build` (`W_b1`, ok, scene saved), ConsoleCheck `W_cc1` "27 entries, 0 errors, 0 warnings",
`PrimalAtmosphereBuilder.Capture` (`W_s1`, 40 shots in `Documentation/Screenshots/PCPhase/World/`, reviewed below).
`pf_up_W5` (light tuning after the review + weather clouds) and `pf_up_W6` (builder: ENV's waterfall group, reverb dedupe;
shader helper names) are extracted on the PC (17:34 / 17:54, editor closed) but NOT compiled yet: the editor compiles them
when it opens (cloud compile against the PC's current files: rc=0). Ordered list in section 5.

## 1. Result per item

| # | Item | Status | Evidence |
|---|---|---|---|
| 1a | Storms in the random schedule, rare / short / never back to back | DONE-VERIFIED (schedule simulation) | `Core/WeatherPlanner.cs` (engine-free). 400 simulated days with the defaults (dotnet harness on the real file): clear 66.6 %, cloudy 23.4 %, rain 9.4 %, storm 0.59 %; 1.27 rains / day; 73 storms = one every 5.5 days (about 3.3 h of play at 36 min / day), storm 0.45-1.1 h (avg 0.78 h = 70 real s), first storm at 27.5 h, closest two storms 30.6 h apart (rule: 30 h). Also a no-scene test `WorldAtmosphereTests.Storms_Are_Rare_Short_And_Never_Back_To_Back` (not run: editor busy) |
| 1b | Probability / duration data in a ScriptableObject | DONE-NOT-TESTED | `Core/WeatherConfig.cs` (Resources/WeatherConfig, created by my builder, code defaults when missing): schedule, rain kinds (light 0.5 / normal 0.75 / heavy 0.94 / storm 1), storm rules, cloud cover, wetness soak / lag / dry, fog, darkness, wind, lightning, rain particles |
| 1c | Clear / cloudy / rain / storm effects | DONE-NOT-TESTED | fog x1.25 cloudy, x1.9 rain, x1.7 more in a storm (visibility); sun x0.8 rain, x0.4 storm, ambient x0.88 / x0.62 (storm darkness through TimeManager multipliers); fog and sky drift to rain grey; wind 0.35 calm, 0.55 cloudy, 0.9 rain, 1.4 storm + gusts up to +0.45 (same `WindDirection` / `WindStrength` / `_PF_Wind`); rain slanted by the wind, off inside a cave; lightning every 6-16 s in storms, 25 % close (250-700 m) / far (1.2-3.8 km), thunder distance / 343 s later, flash also lifts the ambient |
| 1d | `_PF_Wetness` with a drying lag | DONE-NOT-TESTED | soak in 50 s of full rain (light rain: 0.8 at most), stays wet 45 s after the rain, then dries in 240 s (clear) .. 600 s (overcast), x1.6 slower at night. Test `Surfaces_Stay_Wet_After_Rain_Then_Dry` written |
| 1e | API compatibility | DONE-VERIFIED (compile + code reading) | `State`, `Intensity`, `Overcast`, `Wetness`, `RainingAt`, `SetWeather(s, hours, instant)`, `allowRandom`, `WindDirection`, `WindStrength`, `Changed` unchanged; hand-set Rain = 0.75, Storm = 1 (SURV / AI tests rely on it). New: `StormK`, `Gust`, `IsStorm`, `IsHeavyRain`, `Visibility01`, `FogMultiplier`, `RainKind`, `HoursLeft`, `StormCooldownHours`, event `LightningStruck(m)`. Weather saves itself as ISaveSection `"weather"` (state, time left, rain kind, storm cooldown, wetness): a loaded storm stays a storm (SaveSystem's own storm -> rain line runs first and is then overridden) |
| 1f | Only a storm or long heavy rain puts out an open fire | DONE-VERIFIED (code reading + simulation) | SURV's `Campfire.CheckWeather` counts `Intensity >= heavyRainIntensity (0.9)` as heavy and needs 45 s of it (22 s when low). Normal rain peaks at 0.81 (0.75 + swell) and never counts; a heavy rain starts as normal rain and turns heavy after 35 % of its 0.7-1.5 h (heavy part 35-80 real s: only the longer ones douse a well-fed fire); storm lead-in 0.94 + storm 1.0 always do. Longest simulated heavy spell 141 s |
| 2a | `TimeManager.Phase` + `DayPhaseChanged` | DONE-NOT-TESTED | nested enum `TimeManager.Phase { Dawn, Morning, Noon, Afternoon, Dusk, Night }`, `CurrentPhase`, `PhaseAt(h)`, `PhaseStart(p)`, event `PhaseChanged`; GameEvents `DayPhaseChanged` (id name, amount int) appended. Dawn 5:15, Morning 7:15, Noon 11, Afternoon 14, Dusk 18, Night 20:00 (= IsNight). `Set()` is silent. Test `Day_Phases_Raise_Events_And_Light_Stays_Natural` written |
| 2b | Natural sunlight | PARTIAL (seen in edit-mode captures, tuned once, the tuned keys not yet seen) | sun on a tilted arc (NE -> SE -> SW, 66 deg max; old path went through the zenith), strength by elevation (0.32 at the horizon -> full above 40 deg), morning x0.86 softer, afternoon full 1.75; golden 6-8 h and 17.5-19.5 h keys only on the direct light, ambient keys stay blue-grey (not all orange); sun fades out below the horizon, the same light fades in as a cool moon (0.2, blue) a little later (no pop); night ambient floor sky (0.10, 0.13, 0.22) (was 0.05, 0.07, 0.13); dawn mist in the fog curve; reflections dimmed at night; procedural sky tint greys with clouds. Lighting keys versioned (v3): an older scene gets them at load, the builder writes them once. Capture review (`W_s1`): afternoon 16 h strong neutral-warm with crisp shadows (good), noon / morning bright, night 22.5 h cool blue and readable (player, wreck, trees, shore all visible; night rain darker still readable), cloudy / rain grey and hazier, storm dim and foggy. Problems found and fixed in W5 (v3 keys): dawn 6.4 h too red-brown on the sand and an olive-green horizon at dawn / dusk (from my thicker-atmosphere boost: removed; dawn sun keys less red, dawn ambient / mist neutral, dusk equator less orange); the storm sky stayed clear blue (the procedural sky has no clouds): new cloud layer (item 1g) |
| 2c | Edit-mode captures at 6 hours, review, tune | PARTIAL | `PrimalAtmosphereBuilder.Capture` (own camera render with URP post, no WaitForEndOfFrame, every light / render setting restored, scene stays clean): 6.4 / 8.8 / 12.5 / 16 / 19.1 / 22.5 h + cloudy / rain / storm at 15 h + rain at night, views beach / meadow / water / player: 40 shots `W_s1` reviewed (numbers per hour in `result_W_s1.json`: e.g. 19.1 h sun 6.1 deg, 0.62, rgb 1.00 / 0.64 / 0.38; 22.5 h moon 0.20, sky ambient 0.14 / 0.16 / 0.27; storm sun 0.22, fog x4.2). Second capture after W5 (with clouds and stars in the shots) not done: editor closed |
| 1g | Weather clouds (storm darkness in the sky) | DONE-NOT-TESTED (in W5) | `Shaders/PF_CloudVeil.shader` + `VFX/CloudVeil.cs`: procedural cloud deck on a far-plane dome after the stars, cover from `_PF_Overcast` (0.5 = broken clouds, 1 = closed deck), drift with `_PF_Wind`, air colour of the hour (fog colour), sun-side light, storm darkening (`_PF_StormK`), lightning flash (`_PF_Flash`); off under a clear sky. Builder makes `Art/World/M_CloudVeil.mat` and `[Atmosphere]/Sky`; captures draw it (and the stars) on temporary domes |
| 3 | Volcano danger | DONE-VERIFIED (compile) / NOT-TESTED in play | `World/HazardZone.cs` (rings warm / hot / dangerous, smooth heat, height band, downwind smoke), `World/HazardZoneMonitor.cs` (4 Hz; `HazardWarning` on each level change with 6 % hysteresis; one line on a first approach; status effects `heat` (hot ring: stamina regen x0.7, sprint x1.25, thirst x1.5) and `heat_severe` (dangerous ring: regen x0.45, sprint x1.5, stamina capped at 75 %, thirst x1.8, -0.3 HP/s, no regen: 100 HP lasts 5.5 min, never instant); local smoke thickens and browns the fog (x6 at full)). `SurvivalEnvironment.AirAt` adds the ring heat to the air (so SURV's hot-air thirst x1.3 also applies above 28 C); `HazardLevelAt`, `HazardHeatAt`. Placement from ENV's data: `HZ_volcano` at the `volcano` location (r 45: rings 61 / 31 / 10 m, +6 / +14 / +24 C), `HZ_vent` at ENV's lava vent (20 / 11 / 5 m, +7 / +15 / +30 C, smoke 55 m), `HZ_lava_N` every ~10 m along ENV's lava line (13 / 7 / 3.5 m) and along any `*lava*` mesh on the island. Test `Volcano_Heat_Warns_Gradually_And_Hurts_Only_Near_The_Lava` written. Assets `SE_heat`, `SE_heat_severe` made by my builder (code defaults otherwise); icons `Resources/UI/icon_status_heat(_severe).png` (heat waves / flame, SURV's icon style) |
| 4 | Atmosphere audio | DONE-NOT-TESTED (nobody listened) | `Tools/Audio/ambience_synth.py` (numpy / scipy, own seeds per clip): 9 stereo 2D beds (ForestDay, InsectsDay, NightInsects, WindSoft, WindStrong, RainLight, RainHeavy, OceanSurf, CaveRoom), 7 mono 3D loops (River, Stream, Waterfall, WetlandDay, WetlandNight frogs, Pond, Lava), one-shots: 8 primitive bird calls (whistles, croak, rattle, hoots, trill, whoop, chips; no songbird melodies, no owls), 6 distant dinosaur calls (resonant hadrosaur honks, closed-mouth booms, bellow, bark, low moan; low-passed with a long outdoor tail), 4 night critters, 2 near / 3 far thunders. Loops seamless (equal-power cross-fade; seam spectral flux checked 1-2x the median, no click), DC removed, RMS-normalised. `Core/AmbienceManager.cs` (layers eased by day / night / rain / storm / wind / height / sea / cave; birds with answers, distant calls more at night, critters; thunder from WeatherManager; silence: insects / birds / frogs hush within 34 m x size of an awake predator (x1.6 once it is suspicious) and 24 s after a predator's PlayerNoticed, back over 9 s; cave: outside low-passed to 850 Hz + cave room tone + AudioReverbZone). `Core/AmbienceEmitter.cs` (3D loops, day / night clips, hushable frogs, rain masking, no voice beyond range). 61 MB WAV in all |
| 5 | ZoneManager + locations | PARTIAL (first build before ENV's markers) | zones with day / night offsets, humidity, stable cave air (15 C at weight 0.8: warmer than a cold night, cooler than a hot afternoon); the smallest zone decides with a soft edge (no summing of overlapping zones); `Register`, `AdjustAir`, `HumidityAt`, `LocationDefaults` for the 18 ids. The builder registers every `Markers/Zones/<id>` it finds (radius: EnvLocation.radius (read by reflection), else LOCATIONS.md, else collider / scale / default), turns the old ZONE_Cave (-4 C) into stable cave air. First build (`W_b1`, before ENV's markers were in the scene): 3 ids from the old markers (shipwreck, meadow, cave), ZONE_Cave made stable, 8 stream + 1 pond emitters, 2 cave reverb zones (W6 dedupes to one), no volcano / lava yet. Since then ENV deployed the terrain v2, 18 `Markers/Zones/<id>` with EnvLocation, the water bodies and `env_features_v2.json`, and wrote `LOCATIONS.md` (my parser reads its radius column); `TERRAIN_READY.txt` not seen. The next `Build` registers all 18, places river / waterfall / wetland / lava emitters and the volcano, vent and lava hazard zones |
| 5b | `Editor/PrimalAtmosphereBuilder` | DONE-VERIFIED (`W_b1` ok, Console 0 errors / 0 warnings) | bridge `Build` (idempotent: config / status assets created once, audio import settings, TimeManager keys v2 once, clips to AmbienceManager, zones, root `[Atmosphere]` with Emitters / Hazards / Reverb rebuilt from ENV's current water, markers and lava; hand-placed children kept; scene saved; log to `Documentation/PCPhase/World/atmosphere_build.md`), `Capture`, `Status` |
| 6 | Night / rain temperature feel | DONE-VERIFIED (numbers from the code's formulas) | table below |

## 2. Tuned numbers (temperature)
Air = TimeManager day curve (16 C at 5:00 .. 29 C at 15:00) - clouds by day 3 x cover + clouds at night 2 x cover - clear night
1.5 - rain 2 x intensity - storm 1, then SURV's night offset -4 (dusk / dawn blend), zone climate, altitude, volcano heat.
Body target (SURV, unchanged): 37 C at >= 18 C felt, down to 33.5 C at 4 C; Cold below 35.2 (stamina regen x0.6, no
healing), freezing below 34.2 (0.2 HP/s). Felt = air - 5 x wetness + fire / shelter. The body moves 0.02 C/s, so reaching
freezing takes about 140 s; storms last about 70 s.

| place | hour | weather | air C | body target dry | body target soaked |
|---|---|---|---|---|---|
| open | 03:00 | clear | 11.1 | 35.29 ok | 34.04 freezing (only after swimming at night; dries in ~100 s) |
| open | 03:00 | rain | 12.4 | 35.61 ok | 34.36 cold |
| open | 03:00 | storm | 11.6 | 35.41 ok | 34.16 freezing (storm shorter than the 140 s to get there) |
| open | 15:00 | clear / rain / storm | 29.0 / 25.1 / 23.0 | 37 ok | 37 ok |
| meadow (day +0.5, night -0.5) | 03:00 | clear / rain | 10.6 / 11.9 | 35.16 cold / 35.49 ok | 33.91 freezing / 34.24 cold |
| deep forest (day -2.5, night +1) | 03:00 | clear / rain / storm | 12.1 / 13.4 / 12.6 | ok | 34.29 / 34.61 / 34.41 cold |
| wetland (day -1.5, night -0.5) | 03:00 | clear / rain | 10.6 / 11.9 | 35.16 cold / 35.49 ok | 33.91 freezing / 34.24 cold |
| cave (stable 15 C) | 03:00 / 15:00 | any | 14.2 / 17.8 | 36.06 / 36.95 ok | no rain inside |
| any, by a lit campfire (1.5 m, ~9 C in rain) | 03:00 | rain | | | ~36.6 ok |

Result: a clear night is chilly (Cold only in the open meadow / wetland before dawn), rain at night makes a soaked player Cold
but not freezing, a fire or a roof fixes it; storms are dangerous only if you stay soaked after them. Zone offsets:
beach 0 / +0.5, forest -1 / +0.5, river -1 / -1, waterfall -2 / -1.5, canyon -2 / -1, ridge -1 / -1.5, volcano +3 / +3,
high ground -0.5 / -1 (day / night).

## 3. Files (all mine or append-only)
New: `Scripts/Core/{WeatherConfig, WeatherPlanner, AmbienceEmitter}.cs`, `Scripts/World/{HazardZone, HazardZoneMonitor}.cs`,
`Scripts/VFX/CloudVeil.cs`, `Shaders/PF_CloudVeil.shader`,
`Scripts/Editor/PrimalAtmosphereBuilder.cs`, `Tests/PlayMode/WorldAtmosphereTests.cs`, `Resources/UI/icon_status_heat.png`,
`icon_status_heat_severe.png`, `Audio/Ambience/AMB_*_Loop.wav` (16), `Audio/Ambience/OneShots/AMB_*.wav` (23),
`Tools/Audio/ambience_synth.py` (on the PC already). Changed: `Scripts/Core/{TimeManager, WeatherManager, AmbienceManager}.cs`,
`Scripts/World/ZoneManager.cs`, `Scripts/Survival/SurvivalEnvironment.cs`, `Scripts/VFX/NightSky.cs` (moon switch at the
same -2.5 deg as the light), `Scripts/Core/GameEvents.cs` (appended `DayPhaseChanged, HazardWarning`). The legacy
AMB_*_Loop clips (U's sfx_synth) are untouched and still the fallback.

## 4. Requests
| To | Request | Why |
|---|---|---|
| SURV | HUD line for `GameEventType.HazardWarning` (amount 1 warm "The ground is warm", 2 hot, 3 dangerous "Get away from the lava", 0 clears it); the heat status effects already show icon + name + applied text through PlayerStatusEffects | directive 48 |
| SURV | optional: slower drying in humid zones (`SurvivalEnvironment.HumidityAt(p)` 0..1: wetland 0.95, waterfall 0.95, deep forest 0.8), e.g. dry x (1 - 0.5 x humidity) | wetland "cool / humid" |
| SURV | optional: `SaveSystem.ApplyWorld` may keep a storm (the weather section overrides it anyway) | cleanup |
| ENV | keep `Markers/Zones/<id>` + EnvLocation.radius (read by name); deploy `Art/Environment/Terrain/env_features_v2.json` (I read `lava.points` / `lava.vent` read-only for the hazard zones); name lava meshes `*Lava*`; waterfall meshes under `Water/Waterfall` (emitters are placed at the sheet); tell the Lead when TERRAIN_READY.txt is written so `PrimalAtmosphereBuilder.Build` is re-run | placement |
| ENV | wet-surface shaders: `_PF_Wetness` keeps its meaning (0..1), now with a 45 s lag and slower drying at night / under cloud | interface |
| AI | read `TimeManager.Instance.CurrentPhase` / `DayPhaseChanged` for the herd day; `WeatherManager.StormK` / `Visibility01` if weather should change behaviour; `WindStrength` now carries storm gusts | interface |
| Lead | after the editor is back: deploy list below, then `PrimalAtmosphereBuilder.Build`, `Capture`, ConsoleCheck, and the 4 WorldAtmosphereTests | verification |

## 5. Deploy list (in order, one bridge-lock cycle each, when the editor is open)
Done: W1, W2, W3, W4, Build `W_b1`, ConsoleCheck `W_cc1`, Capture `W_s1`.
1. W5 + W6 files are already extracted: when the editor is open, `$HOME/run.sh W_ref3 PrimalEditorBridge.Refresh "" 15`, wait
   for the compile (check `Library/Bee/tundra.log.json` for `error CS`) and that `PF/Cloud Veil` imports without errors.
2. (nothing: W6 extracted)
3. `$HOME/run.sh W_b2 PrimalAtmosphereBuilder.Build "" 30` (registers ENV's 18 locations, emitters at ENV's water, volcano /
   vent / lava hazards, cloud layer; TimeManager keys v3), then `PrimalAtmosphereBuilder.Status`, then
   `$HOME/run.sh W_cc2 PrimalEditorBridge.ConsoleCheck "" 5`.
4. `$HOME/run.sh W_s2 PrimalAtmosphereBuilder.Capture "tag=v3" 30`: review dawn / dusk colour and the storm clouds.
5. `$HOME/run.sh W_t1 PrimalTestRunner.RunPlayMode "WorldAtmosphereTests" 3` (targeted: storm spacing, phases / light,
   wetness lag, the heat chain; nothing else checks them).
6. After any later ENV terrain / marker / lava change (or TERRAIN_READY.txt): `Build` again.

## 6. Not done / risks
- Seen once in captures (v2 keys); the v3 dawn / dusk tuning and the cloud layer are not seen yet. Nothing heard in Unity:
  the sound mix and the emitter placement are tuned on numbers and spectrograms only; one listen by the owner is the check.
- PF_CloudVeil is a new shader: if it fails to compile on the PC the builder logs a warning and skips the cloud layer
  (nothing else depends on it).
- Night brightness: ambient floor about 2x and moon 1.7x the old values; if the night reads too bright or too dark in the
  captures, change `skyAmbient` / `nightMoonIntensity`.
- Lava hazard zones depend on ENV's `env_features_v2.json` being in the project (or lava meshes named `*Lava*`); without them
  only `HZ_volcano` at the location exists.
- 61 MB of new WAVs (Vorbis-compressed on import, 2D beds kept stereo for PC).
