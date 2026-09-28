# Water: audit and new water (sea, beach, stream, pond)

Owner request: "nước chảy như thật có gợn sóng tạt vào bờ đất" (water that flows for real, with ripples and waves
washing onto the shore). This document covers what was wrong, what replaces it, how to deploy it and what to check.

## 1. Audit (before)

Sources read: `Shaders/PF_Water.shader`, `PrimalShaderBuilder.Water`, `PrimalWorldBuilder.BuildWater / WaterMaterial`,
`OceanShore.cs`, `WaterSource.cs`, `WeatherManager.cs`, the scene YAML, the water FBX files, the terrain heightmap,
the Lead's video sheets and `Documentation/Screenshots/Review/water_sea_noon.png`, `water_before_sea.png`,
`volc_beach_day.png`, `Screenshots/05_pond.png`.

Scene set-up:

| Object | Mesh | Material | Notes |
|---|---|---|---|
| `Water/ENV_Ocean` | Unity built-in Plane, scale 200 (2 km, 10 x 10 quads of 200 m) | `M_Ocean` on PF/Water | y = 0 = `OceanShore.seaLevel` |
| `Water/ENV_Stream_Water` | FBX ribbon, 482 verts, 5.2 m wide, ~205 m long, surface 27.5 m down to 9.67 m | `M_FreshWater` (URP Lit, transparent) | WaterSource "Stream water" |
| `Water/ENV_Pond_Water` | FBX single polygon (48 verts), flat at 9.55 m, ~46 m across, 2.6 m deep in the middle | `M_FreshWater` | WaterSource "Pond water" |

Terrain: 640 x 640 m centred on the origin, heights -10 .. 70 m, 1025 heightmap. The north beach (player spawn at
(-20, 1.5, 211)) slopes about 1:6 at the waterline and reaches -8 m some 60 m out.

What looks wrong:

1. **Hard line where the sea meets the sand.** The plane simply cuts the terrain at y = 0. No foam, no run-up, no wet
   band, no transparency ramp (visible in every beach frame of the video and in `water_sea_noon.png`).
2. **No depth.** PF/Water has no idea how deep the water is: "shallow / deep" colours are chosen by view angle only,
   so the shallows are the same grey-blue as open sea, and the 0.7 to 0.94 alpha shows the sea floor as brown smears.
3. **Visible tiling.** One 256 px normal map made of 48 integer sine waves, 12 m tile: reads as regular diagonal rows
   of ripples across the whole sea (`water_sea_noon.png`).
4. **Nothing moves toward the beach.** Waves scroll in the wind direction everywhere; there are no swells, no breaking,
   no swash. The plane has 200 m quads, so vertex waves were impossible.
5. **Stream and pond are mirrors.** URP Lit, smoothness 0.95: a flat sky-coloured sheet. The stream does not flow;
   its UVs are world XY / 10 from the Blender export, so there was no along-stream coordinate to flow along.
   The pond polygon edge shows as a jagged hard edge over the bank (`05_pond.png`).
6. **Geometry notes (not shader issues, left as they are):** the terrain rises above the stream surface for about
   10 m around z = -128 .. -116 (the water is hidden there, as before); the last metres of the stream overlap the pond
   and sit 12 cm above it (double layer). The new stream material fades out inside the pond to hide the overlap.
7. **Rain rings used `_PF_Wetness`,** which lingers minutes after the rain stops.

## 2. Design (after)

Everything is original and procedural; nothing is downloaded. The look works without the camera depth texture
(mobile), and gets extra soft edges and refraction on PC when the pipeline has them.

```
terrain ──(PrimalWaterBuilder.Build)──> T_Water_ShoreMap.asset  (height, distance to shore, wave direction)
                                     ├─> ME_Ocean.asset          (fine grid over the island, stretched to 1.5 km)
                                     └─> ME_OceanShore.asset     (shoreline band, 64 m chunks)
stream / pond mesh + terrain ────────> T_Water_Flow_<Body>.asset (flow direction x speed, bank distance, depth)
WeatherManager.Intensity ─(WaterGlobals)─> _PF_Rain ; URP asset ─> _PF_WaterSceneDepth / _PF_WaterRefraction
```

### PF/Ocean (the sea, `M_Water_Ocean`, object `Water/PF_Ocean`)
* Depth from the shore map (terrain height under the point), sharpened by the camera depth texture on PC (rocks,
  wreck, legs). Turquoise shallows to deep blue (`_DepthScale`), transparent shallows (`_ShallowAlpha`), alpha, fresnel
  and glint all go to zero at the waterline over `_EdgeSoftness` metres of depth: no hard line.
* Wave bands rolling to the beach: the phase follows the smoothed distance to the shoreline, so crests arrive parallel
  to any beach shape; analytic normals, a steep front and a long back, broken into segments along the shore.
* Two rotated chop layers (FFT-spectrum normal map, 12 m tile) + fine ripples (3.2 m tile) + a very large macro
  modulation (230 m) and distance fading, so the tiling does not read.
* Gentle swell on the vertices (3 long waves around the wind), zero under 2 m of depth, fading out 600 m away.
* Caustics on the sandy bottom in the shallows, light through the crests when looking toward the sun, sky probe
  reflection with fresnel, sun glint, main light shadows, fog, rain rings, storm whitecaps from `_PF_Wind`.

### PF/Ocean Shore (beach line, `M_Water_Shore`, objects `Water/PF_OceanShore/PF_OceanShore_xx_yy`)
Overlay mesh on the terrain band from 2.6 m under water to 1.2 m above sea level (on the sand, or on the water where
the sand is under it; pulled toward the camera so terrain LOD never covers it).
* Breaking foam: the crest line and the whitewater trailing behind it, only where the depth is under `_BreakDepth`.
* Swash: every crest that reaches the waterline becomes one run-up (vertical reach `_SwashHeight`, each wave its own
  reach, fast uprush, slower backwash), a thin reflective sheet with a lobed, bubbly leading edge.
* Wet sand: darker, glossy band that the backwash leaves behind, drying in `_DryTime` seconds, plus a damp band up to
  the highest reach. The foam, the swash and the ocean's wave bands share one phase, so they stay in sync.

### PF/Water Flow (stream `M_Water_Stream`, pond `M_Water_Pond`) (reworked in iteration 2)
* Flow map baked per water body: downstream direction = gradient of the distance along the channel from the highest
  water (works on any mesh, even where a bank bump splits the wet area), speed from the surface slope, slower along
  banks. The pond is calm with a fan of inflow where the stream enters; the stream fades out inside the pond.
* Surface: the streak texture is laid along the local flow. The flow angle is blended between the two nearest of 12
  fixed directions (continuous pattern, no cells or seams) and scrolled downstream with two cross-faded time phases
  (`_FlowCycle`), variance-preserving blend so mixes keep their contrast. Streaks stretch up to `_Stretch` where fast.
* Body: clear water with per-channel absorption along the view path (`_AbsorbColor`, red first) and back-scatter
  (`_ScatterColor`): the bed turns green-blue with depth. PC: refracted bed. Mobile: grey absorption (alpha).
* Reflection: sky probe with fresnel from a smoothed base normal (ripples and rain rings bend the reflection but do not
  kill it at grazing angles), plus screen-space reflection of trees and banks on PC (`_SSRStrength`). If the probe is
  not bound (much darker than the ambient light) the ambient light in the reflected direction stands in; at night the
  baked day-sky reflection is dimmed with `_PF_NightFactor`.
* White water only in thin bands: along banks and rocks (a band starting just inside the soft edge), on steep rapids
  (thin streaks) and on very shallow running water. The pond has none.

### Textures (tools/water_textures.py, numpy + PIL, deterministic, all seamless)

| File | Size | Import | Content |
|---|---|---|---|
| `T_Water_Normal_Waves.png` | 512 | Normal map | wind chop from a Phillips spectrum (FFT) + broad swell layer |
| `T_Water_Normal_Ripples.png` | 512 | Normal map | capillary ripples, log-normal band spectrum |
| `T_Water_Foam.png` | 512 | Default, sRGB off, RGBA | R bubbly lace foam (warped Worley holes), G fine bubble specks, B macro noise, A caustics (photon splat through a periodic height field) |
| `T_Water_RainRipple.png` | 256 | Default, sRGB off, uncompressed, no mips | RG ring direction, B drop time, A ring mask (110 drops) |
| `T_Water_FlowStreaks.png` | 512 | Default, sRGB off, RGBA | flowing water, x = downstream: RG slope of 4:1 stretched ripples, B thin foam streaks, A fine bubbles |

The builder sets these import settings itself. `textures_tiled_2x2.png` in this folder shows the four main channels
tiled 2 x 2 (no seams). Seam check printed by the script: ratio about 1.0 for every texture.

## 3. Files

Cloud mirror (`/home/claude/pf`):

| Mirror path | Goes to (PC) |
|---|---|
| `src/Shaders/PF_WaterCommon.hlsl` | `Assets/_Project/Shaders/` |
| `src/Shaders/PF_Ocean.shader` | `Assets/_Project/Shaders/` |
| `src/Shaders/PF_OceanShore.shader` | `Assets/_Project/Shaders/` |
| `src/Shaders/PF_WaterFlow.shader` | `Assets/_Project/Shaders/` |
| `src/Scripts/VFX/WaterGlobals.cs` | `Assets/_Project/Scripts/VFX/` |
| `src/Scripts/Editor/PrimalWaterBuilder.cs` | `Assets/_Project/Scripts/Editor/` |
| `src_assets/Water/T_Water_*.png` (5 files) | `Assets/_Project/Art/Water/` (new folder) |
| `tools/water_textures.py` | generator (optional copy to `Tools/`) |

Generated by Build in the project: `Art/Water/Generated/` (shore map, flow maps, ocean mesh, shore mesh),
`Art/Water/Materials/` (4 materials), `Art/Materials/_Backup/water_originals.json` (record for Revert).
`PF_Water.shader`, `M_Ocean`, `M_FreshWater`, `OceanShore`, `WaterSource` are unchanged.

## 4. Deploy (Lead)

1. Copy the files in the table above (keep existing `.meta` files; new ones get generated).
2. Bridge `Refresh`, wait for the script compile, check the Console: no errors from `PF/Ocean`, `PF/Ocean Shore`,
   `PF/Water Flow` (select each shader asset, the Inspector lists compile errors).
3. Bridge `PrimalWaterBuilder.Build` (arg empty). It refuses to change anything if a shader has errors or a texture is
   missing. It opens / saves `Island_VerticalSlice` (not if the open scene has other unsaved changes). The log lists the
   shore map size, mesh counts, flow texels per body, and "WARNING" lines if something looks off.
4. Bridge `PrimalWaterBuilder.Preview` `15;keep;only=none` (sets 3 pm), then `PrimalWaterBuilder.Preview` `15`
   (captures, then puts the scene hour back). Always use the second run: see section 8. Output in
   `Documentation/Screenshots/Water/`: `beach_close_0/1/2` (three moments of one swash cycle), `beach_low`,
   `beach_wide`, `stream_rapids`, `stream_downstream`, `pond`, `pond_rain`. Extra args: `only=pond+stream`,
   `debug=1..8` (shader debug views, `_dbgN` suffix), `dt=0.6` (water clock shift for motion checks), `suffix=_x`.
   `PrimalWaterBuilder.Inspect` prints the lighting / reflection set-up and flow map statistics.
5. Play mode: walk to the beach and the stream; `WeatherManager.Instance.SetWeather(WeatherState.Rain, 1, true)` for rain.
6. Rollback at any time: bridge `PrimalWaterBuilder.Revert` (arg `purge` also deletes the generated assets).
   Menu equivalents: Primal Frontier / Tools / Water: Build, Preview captures, Revert.

Build options (arg, `;` separated): `reset` (materials back to default numbers; otherwise tweaks are kept on re-run),
`shoreStep=1` (m, shore mesh grid; 1.5 for mobile), `oceanStep=5`, `mapRes=1024`, `flowTexel=0.25`, `noObstacles`.
Re-run Build after any terrain or water-mesh change (it re-bakes everything, keeps material numbers).

## 5. Parameters worth tuning (material Inspector)

| Material | Property | Default | Effect |
|---|---|---|---|
| M_Water_Ocean | `_ShallowColor` / `_DeepColor` / `_DepthScale` | (0.10, 0.60, 0.56) / (0.015, 0.13, 0.27) / 2.8 m | colour by depth |
| | `_ShallowAlpha`, `_EdgeSoftness` | 0.3, 0.35 m | how much sand shows, width of the soft waterline |
| | `_ShoreWaveLength`, `_ShoreWavePeriod` | 9 m, 6.5 s | spacing / timing of waves arriving (copied to the shore material on Build) |
| | `_ShoreWaveHeight`, `_ShoreWaveReach` | 0.8, 42 m | visibility of the incoming bands, how far out they start |
| | `_WaveTiling`, `_WaveStrength`, `_RippleStrength` | 12 m, 0.55, 0.35 | chop size and strength |
| | `_SwellHeight`, `_SwellLength` | 0.16 m, 34 m | vertex swell offshore |
| | `_CausticsStrength`, `_Whitecaps` | 0.7, 0.6 | set 0 to skip them (mobile) |
| M_Water_Shore | `_SwashHeight`, `_SwashUprush` | 0.45 m, 0.35 | run-up reach (vertical) and uprush share |
| | `_BreakDepth`, `_SurfFoam`, `_EdgeFoam`, `_ShoreFoam` | 1.2 m, 1, 1.2, 0.6 | where and how much foam |
| | `_WetDarkness`, `_DryTime`, `_FilmAlpha` | 0.42, 6 s, 0.32 | wet band strength, drying, sheet opacity |
| M_Water_Stream | `_FlowSpeed`, `_FlowCycle`, `_Stretch` | 1.3 m/s, 2 s, 4 | flow speed at full, pattern cycle, streak length when fast |
| | `_StreakTiling`, `_StreakStrength` | 2.3 m, 0.32 | streak size across the flow, surface roughness |
| | `_AbsorbColor`, `_ScatterColor` | (0.9, 0.34, 0.27) /m, (0.06, 0.12, 0.11) | water tint over the bed |
| | `_BankFoam`, `_BankFoamWidth`, `_RapidsFoam`, `_ShallowFoam` | 0.8, 0.3 m, 0.45, 0.35 | white water bands |
| | `_ReflectionStrength`, `_SSRStrength` | 0.9, 1 | sky / probe reflection, screen reflection (PC) |
| M_Water_Pond | same shader, calm numbers set by Build | `_FlowSpeed` 0.4, `_StreakStrength` 0.1, no foam, `_RainTiling` 2.2 m, `_RainStrength` 1, `_AbsorbColor` (0.75, 0.3, 0.24) | |
| WaterGlobals (on `Water`) | `useSceneDepth`, `useRefraction`, `rainScale` | on, on, 1 | PC extras, rain ring scale |

## 6. What to check visually

* Beach: no straight hard line at the waterline; turquoise shallows turning blue further out; bands of waves moving
  toward the beach; foam lines appearing where they break; a thin water sheet running up 2 to 3 m and sliding back
  with a white lobed edge; a darker wet band that follows it and dries; no flicker of the sheet against the sand at
  3 to 40 m (if it flickers far away, raise `_ViewBias` on M_Water_Shore to 0.1).
* Open sea: no visible tile pattern from the beach or from the hill; gentle swell; reflections not white-washed.
* The wreck and rocks in the water: soft edges on PC (scene depth); on mobile a hard intersection is expected.
* Stream: water visibly running downhill toward the pond, faster and whiter in the steep upper part, foam along the
  banks, clear shallows; no double layer where it enters the pond.
* Pond: calm, soft edge on the bank (no jagged polygon line), rings when it rains.
* Night: water darkens with the light, no glowing foam. Fog over the far sea like the terrain.
* Gameplay unchanged: fill / drink at the pond, stream and sea still work (they never used the renderers).

Offline references in this folder (numpy approximations, NOT Unity captures; no sky probe, no shadows, no mipmaps):
`offline_beach_concept.png` (three swash moments + low view on the real heightmap), `offline_flowmap_stream.png`
and `offline_flowmap_pond.png` (red = speed, blue = depth, arrows = baked flow direction).

## 7. Risks

* **Shader compile (not compiled by Unity yet).** Checked in the cloud with glslang's HLSL front end against this
  project's URP 17.3 ShaderLibrary (copied from `Library/PackageCache`), for D3D11, Vulkan, GLES3 and Metal paths, with
  shadow / soft shadow / fog variants: all three shaders compile to SPIR-V/GLSL, and the same harness flags an injected
  error in a known-good shader. Not covered: FXC-only rules (for example gradient samples inside `[branch]` blocks:
  the scene depth / opaque samples are inside uniform branches, at worst warning X4121) and the `INSTANCING_ON` variant
  (the harness cannot expand Unity's instancing macros; the known-good PF/Foliage Wind fails there too).
* **Performance.** Ocean fragment: about 7 texture samples (plus 2 caustics in the shallows, 2 rain while raining);
  shore sheet 4; stream/pond about 9 but small on screen. Meshes: ocean ~33 k vertices (one draw), shore sheet
  ~32 k quads at 1 m in ~42 culled chunks (only the visible ones draw). Mobile: set `_CausticsStrength` and `_Whitecaps` to 0, Build with
  `shoreStep=1.5`, the scene depth / refraction paths switch off by themselves (Mobile_RPAsset has neither).
* **Shore map resolution** is 0.625 m (1024 over 640 m); on steep rock the swash / foam bands can look stepped.
  `mapRes=2048` quadruples memory (16 MB).
* **Terrain changed later** (sculpting, new builders): re-run Build, otherwise foam and edges follow the old shore.
* **Obstacle bake** treats any static, non-trigger collider under the stream surface as a rock. If a large invisible
  collider crosses the stream the log shows "texels blocked by colliders"; re-run with `noObstacles`.
* **Edit-mode time.** Preview freezes the water clock (`_PF_WaterTime`) per shot and sets it back to 0 in a `finally`
  block; if the water ever looks frozen in the Scene view, run Preview once more (or `Shader.SetGlobalFloat("_PF_WaterTime", 0)`).
* The old `PF_Water.shader` and `M_Ocean` stay; `ENV_Ocean` only has its renderer disabled (Revert turns it back on).

## 8. Tuning log

### Iteration 2 (2026-09-28, iterated in the editor through the bridge)

Feedback: beach good (thin razor line on the swash edge, faint ruler-straight lines mid sea); stream read as wet mud
with white speckles; pond almost invisible, blue speckles around it, black in the rain.

Findings (with the new `debug=N` views and `Inspect`):
* **Pond "invisible" / black in the rain were capture artefacts, not the shader.** In edit mode the procedural skybox
  follows the sun; Preview switches to 15:00, and after an hour change, a scene save or a script reload Unity rebuilds
  the sky reflection over the next editor frames. The first captures therefore show a missing (black) reflection; the
  same shader captured again is bright (pixel-identical between settled runs). Fixes: the shaders fall back to the
  ambient light when the probe is unbound, Preview renders every view once as warm-up, and gets `keep` so the hour can
  be set once and captured in a second run. In the game TimeManager uses Trilight ambient and the baked reflection,
  so this does not happen in Play mode; the baked day-sky reflection is now dimmed at night via `_PF_NightFactor`.
* **Stream mud look:** alpha ~1 with the refracted brown bed nearly untinted, foam covering most of the surface
  (bank band 1.2 m wide on a 5 m stream, shallow foam everywhere) and no directional pattern. Reworked shader (see
  section 2): absorption tint, fresnel from a base normal, SSR, flow-aligned scrolling streaks, thin foam bands.
  A first cell-based layout (2 x 2 rotated cells) showed seams and washed-out mixes; replaced by the 12-direction angle
  blend with two time phases (continuous) and a variance-preserving blend (foam threshold works again).
* **Pond grazing reflection** was killed by ripples and rain rings tilting facets toward the camera: fresnel now uses
  a smoothed base normal; rain rings bigger (2.2 m tile) so they read at 5 to 15 m.
* **Beach:** the swash edge band is now ~10 cm of water deep, textured and broken along the shore (no razor line);
  the band normals use a softer profile (no bright line on each crest); crest phase gets a mid-scale wobble
  (`PF_ShoreNoise`), and breaking crest foam comes in patches that differ per wave (no continuous ruler line).
* **Not the water (left as they are):** the blue speckles on the ground around the pond are terrain specular (they
  are in the old `Screenshots/05_pond.png` too; mask-map smoothness reflecting the sky). The saw-tooth basin rim is the
  terrain / hole edge. A tree trunk reflection (SSR) crosses the stream in `stream_rapids` as a diagonal darker band.

Deploys: `pf_up_wtr1` .. `pf_up_wtr11` (Tools/), each followed by ShaderCheck (0 errors) and, for new defaults,
`Build reset` (materials back to shader defaults, pond numbers re-applied). Scene hour left at 9 (saved value).
Final captures (settled, same viewpoints as iteration 1): `beach_close_0/1/2.png`, `beach_low.png`, `beach_wide.png`,
`pond.png`, `pond_rain.png`, `stream_rapids.png`, `stream_downstream.png` in `Documentation/Screenshots/Water/`.
Intermediate captures from the iteration (`*_v2 .. *_v9`, `*_dbg*`, `*_rep*`, `*_prime`, `*_settle`, `*_afterbuild`)
are still in that folder (no deletions); they can be removed by hand. Contact sheets: `iter2_final_captures.png`,
`iter2_beach_before_after.png` (left before, right after) in this folder.

Cost note: PF/Water Flow now takes 4 streak samples + 1 flow map + 2 detail normals + caustics (2, shallow only) and,
on PC, up to 18 + 4 depth samples for SSR. Small on screen here; on mobile SSR and refraction switch off by themselves.
