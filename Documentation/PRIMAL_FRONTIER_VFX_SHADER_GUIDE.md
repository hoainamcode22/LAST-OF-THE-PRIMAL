# PRIMAL FRONTIER - VFX and Shader Guide (Phase 2)

## 1. Shaders (`Assets/_Project/Shaders`)

All are hand-written URP HLSL (SRP-batcher compatible, instancing on). `PrimalShaderBuilder` checks a shader compiles
(`ShaderUtil`) before switching any material, and copies the material to `Art/Materials/_Backup` first.
`Primal Frontier > Tools > Shaders: Restore Backups` (or bridge `PrimalShaderBuilder.Restore`) puts the URP Lit set-up back.

| Shader | Used by | What it does | Status |
|---|---|---|---|
| `PF/Water` | `M_Ocean` (the sea) | no depth texture needed (mobile friendly): three scrolling layers of a tiling wave normal map in world space, sky reflection with fresnel (wave backs reflect the sky, never the ground half of the probe), sun glint, deep / shallow colour, waves speed up with the weather wind, rain ripples while it rains | in use; compiles; visual check in edit-mode captures |
| `PF/Landmark Lit` | volcano rock / ash / lava | simple lit (sun + probes) for far scenery, height and slope tint so the shape reads from far away, emission, fog scaled by `_FogStrength` so a mountain 500+ m away stays readable | in use; compiles; visual check |
| `PF/Particles Additive No Fog` | `M_VFX_AdditiveNoFog` (volcano crater glow, lava flow glow, embers) | additive particles that ignore fog so distant glows show at night | in use; compiles; visual check |
| `PF/Foliage` + `PF_Wind.hlsl` | not assigned | URP Lit look for trees / bark + wind bend + rain wetness, shadows and depth move with the wind | **reverted**: the editor crashed right after switching `M_Foliage` / `M_Bark` to it (cause not confirmed); both materials are back on URP Lit. Menu `Shaders: Foliage Wind (experimental)` asks before switching |

The pond / stream (`M_FreshWater`) stays on URP Lit: with only a sky reflection the small pond turned grey-white at
grazing angles; the old set-up looked better there.

### Global shader values (set by `WeatherManager` every frame)
| Name | Meaning |
|---|---|
| `_PF_Wind` | xy = wind direction (world xz), z = strength (calm 0.35 -> storm 1.4), w = gust 0..1 |
| `_PF_Wetness` | surfaces 0..1 (up in the rain, dries after) |
| `_PF_Overcast` | cloud cover 0..1 |

Any custom shader can read them (declare `float4 _PF_Wind; float _PF_Wetness;` outside the material CBUFFER).

## 2. Effect library (`Assets/_Project/VFX`, `VfxPool`)

Pooled one-shot effects by id (`VfxId`, append-only because ids are stored as ints): HitLight, HitHeavy, Bleed, FootSand /
Dirt / Mud / Rock, LandDust, WoodChips, StoneChips, Leaves, CraftDust, CraftSparks, FoodCrumbs, Steam, WaterSplash,
WaterDrops, FireIgnite, FireExtinguish, CookSmoke, SpearImpact, ArrowImpact, DinoFootDust, DinoImpactDust, BloodSpray,
BloodSprayHeavy, HitDust. Materials: `M_VFX_Soft` (alpha puff), `M_VFX_Additive` (glow), `M_VFX_Drop`, chips, leaves.

Caps: effects at once follow the quality preset (24 / 40 / 64 / 96); Low skips footstep dust, crumbs and drops further than
22 m from the camera. Blood follows the blood setting (Off = neutral dust, Reduced = smaller).

Phase 2 uses: Steam (boiling water), LandDust (dodge), Leaves (fruit picking), HitDust (resource node emptied).

## 3. Weather effects
- Rain: camera-following particle box (1400 max), stretched drops, rate from `Intensity`.
- Clouds: `Overcast` dims the sun and moon, lowers shadow strength and ambient, thickens and greys the fog.
- Lightning (storms): a separate directional light flashes (two pulses, about 0.45 s), thunder follows after 0.3-3 s.
- Wetness: `_PF_Wetness` (for shaders) and the player's wet look (`PlayerWetLook`, darker and shinier skin / clothes).

## 4. Volcano landmark (`PFB_ENV_Volcano`, `VolcanoLandmark`)
| Child | Effect |
|---|---|
| `Smoke_Plume` | 60-105 m soft puffs rising 7-11 m/s, 38-55 s life, growing to 2.8x, drifting with `_PF_Wind` (the script updates the drift) |
| `Embers` | additive sparks thrown from the crater, more at night (1.5 -> 10 per second) |
| `Crater_Glow` | one 150 m additive halo, alpha 0.05 by day -> 0.38 at night |
| `Flow_Glow_0..8` | overlapping halos along the lava flow, off by day, visible at dusk / night |
| lava mesh | emissive `M_VolcanoLava`, pulsing slowly (property block) |
Rumble every 4-9 minutes: low boom (thunder sound at half pitch), faint camera tremor, 3 dark smoke puffs, 25 embers.

Not done: heat haze (needs a distortion pass over the opaque texture; not added for mobile cost) and ash fall.

## 5. How to tune
- Water: select `M_Ocean` (colours, wave size, speed, strength, glint, reflection, rain ripples).
- Volcano: select `World/Landmarks/Volcano` and its children (particle systems) or the `VolcanoLandmark` fields.
- Weather: `[Systems]/Weather` (chances, durations, wind, lightning colour / intensity).
- Review a change without playing: bridge `PrimalReviewCapture.LookFrom` (writes `Documentation/Screenshots/Review`).
