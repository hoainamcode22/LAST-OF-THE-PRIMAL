# CAVE (CV_) Phase 2 report: Zone 6 Deep Water Cave (2026-09-30)

## Decision (evidence)
- Existing grotto `World/Environment/Terrain/Cliffs/PFB_ENV_Cave_Entrance` at (-18, 17.82, -140.5): mesh 23 x 11.8 x 18.8 m, floor 18.4, ceiling 23.6.
  Raycasts from (-18, 19.4, -131) hit its own back wall 14.4 m in (z -145.4). It is a closed grotto and the mesh is ENV's, so it was not extended.
- Cliff arc (heightmap v2 + live terrain): pad 18.4 m, face z -146..-160 rising 20 -> 41 m, plateau 45-55 m above the planned route (cover 15-25 m).
- Waterfall amphitheatre: west wall x 86-90 (19.4 -> 28 m), pool water 18.5 m. Cliff meshes `PFB_ENV_Cliff_01` line the arc at y >= 25.6 (z -147..-156).
- Result: a through-cave. Main mouth on the west wall of the plunge pool, the underground stream leaves there and runs into the pool.
  Back door at the foot of the cliff slope 19 m east of the old grotto (below the cliff meshes, between two RES rocks).

## Where (scene `World/Environment/Caves/DeepWaterCave`)
| Part | Position (floor) | Notes |
|---|---|---|
| East mouth (waterfall) | (90.0, 19.45, -157.4), faces +x | 4 m wide, 3.3 m high, stream spills 1.0 m down to the pool (18.5) |
| Stream passage | (85, 19.7, -159.6) -> (49.6, 20.9, -173) | 46 m, 3 bends, stream groove on the south side, side alcove pool at (68, 20.2, -161.7) |
| Chamber dome (landmark) | centre (40, 20.9, -173.6), 21 x 19 m, 10.8 m high | still pool 11.6 x 8.6 m, surface 20.78, 1.7 m deep; ceiling cleft over the pool (daylight spot) |
| West passage (branch 1) | (30.4, 20.9, -173.4) -> back door (12.4, 22.49, -141.4), faces +z | 40 m, climbs 1.6 m, 2 bends |
| Hidden branch (branch 2) | behind the water curtain (41.96, 23.62, -182.85) on the chamber south wall | low crawl (crouch) at (42.2, 21.0, -184.4), then hidden chamber (44.8, 21.25, -192.4) 8.8 x 7.6 x 4.3 m |
| Hidden nest | (45.4, 21.25, -192.8) | ring of 9 stones + 3 bone piles, Examinable `cave_hidden_nest` |
| Landmark marker | `LM_DeepWaterCave` (39.2, 20.78, -175.2) | Examinable `cave_deep_pool` on the pool's north rim |
Walkable length: stream passage 46 m + chamber 21 m + west passage 40 m + hidden branch 14 m = about 121 m.

## How it is built (`Scripts/Editor/PrimalCaveBuilder.cs`, new)
- Rock shell: signed distance field of the cave air (passage segments with flat floors, rooms, pool basins, stream grooves,
  ceiling cleft, value noise 0.03 m on floors up to 0.42 m on walls), surface nets at 0.4 m, clipped where it leaves the
  terrain, 12 chunks of 20 m, MeshCollider each, shadows two-sided. 40500 tris, 20991 verts.
- Materials `Art/Environment/Cave/Materials`: PF/Wet Surface on T_Rock (walls damp, floors wet, `_WetResponse` 0 so rain
  never wets it). Sky ambient scaled by a dark occlusion map in 5 bands by distance from the mouths
  (0.62 / 0.42 / 0.28 / 0.19 / 0.12); occlusion only dims indirect light, so the torch keeps full strength. Props in the
  cave get the same treatment through copies in `Materials/Variants`.
- Water (own flow maps, same encoding as PrimalWaterBuilder, rain rings off): `Water_Stream` (63 points, 20.76 -> 18.51 m,
  always downhill, thin sheet over the rock into the plunge pool), `Water_Runnel` (curtain foot -> pool),
  `Water_UndergroundPool` (calm), `Water_AlcovePool` (calm). Water curtain: PF/Waterfall copy (M_CV_Curtain).
- Drinking: 7 WaterSources (5 stream sections, 2 pools; fresh, unboiled) on renderer-less meshes, so
  `PrimalWaterBuilder.Build` never re-bakes (and breaks) the underground water.
- Light: 4 realtime lights, no shadows: east / west mouth daylight bounce and the pool shaft follow `TimeManager.Daylight01`
  (new runtime `Scripts/World/CaveDaylightLight.cs`, off at night), one dim cool chamber fill (3.4, range 15). 4 custom
  reflection probes with a dark cubemap (no sky reflections on wet rock / water inside). No emissive or fantasy glow.
- Terrain holes: only at the two mouths, 21 + 27 cells (8.2 + 10.6 m2). TerrainData backed up once to
  `Art/Terrain/_Backup/TD_Island_before_CV.asset`; each build restores the two 20 x 20 m mouth regions from it first.
- Props: 11 rubble rocks (wall bases), 4 framing boulders at the mouths, 4 bone piles, the nest (existing PFB_ENV_Rock_*,
  PROP_PC_Bones). ART kit: `PlaceKit` picks up `Prefabs/Environment/Phase2/*CAVE_*` (pool rim, stalactites, rubble, wall
  panels) on the next Build; batch 2 was not delivered yet.

## Anchors (empty transforms under DeepWaterCave)
- `AI_Anchors` (9): AI_SmallCreature_0_lizards (57.5, -170.4), _1_crickets (34.6, -167), _2_lizards (21.2, -164.6),
  _3_scavenger (47.2, -193.4, hidden chamber), _4_crickets (76, -160.9); AI_Fish_Pool_0..2 (0.6 m under the pool surface),
  AI_Fish_AlcovePool_0.
- `RES_Anchors` (12): RES_Stone_0..2, RES_Flint_0..1, RES_Clay_0..1 (alcove pool bank, pool outlet), RES_Bones_0..1,
  RES_RareCluster_Hidden_0_bones / _1_flint / _2_bones (hidden chamber; `Resource_Rare_Bones` fits).
- `FX_Anchors` (17): FX_Drip_0..5, FX_Stream_0..3, FX_Curtain, FX_WaterfallOutside, FX_LightShaftDust,
  FX_EchoZone_Chamber / StreamPassage / WestPassage / Hidden (box bounds = the anchor's localScale, centre = position).

## Commands and results
- Bridge commands: `PrimalCaveBuilder.Build` (idempotent, arg `noholes`), `Check` (read only), `SaveScene`, `Capture`, `Survey`, `Near`.
- CV1s Survey, CVn1/n2/n3 Near: evidence above (cliff meshes at the first back-door spot, so it moved to x 12.4, z -141.4).
- CVb1 Build: 21.7 s, 0 warnings. Field 221 x 50 x 160 samples, 4067 m3 of air; 20250 quads kept, 912 above the terrain dropped.
- CVk1 Check: walk 789 probes (centre + both sides, every 0.5 m, 5 lines incl. the pool walkway and the hidden chamber):
  holes 0, too steep (> 40 deg) 0, headroom < 2.2 m 0 (lowest 2.55 m), player capsule blocked 0. Rock cover min 1.52 m
  (> 7 m from the mouths). Stream uphill steps 0. Missing refs 0. Void rays: 2000, 4 without a hit (long sight lines along
  the passage and out of the back door, not holes; Check now tests the ray end: see Not done).
- CVs1 SaveScene: saved. CVc1 ConsoleCheck: 0 errors, 0 warnings.
- Counts: 12 shell chunks, 36 mesh colliders (shell + props), 62664 rendered tris in the group, 4 lights, 7 WaterSources, 2 Examinables.

## Requests
- WORLD: discovery texts for `cave_deep_pool` (the landmark pool) and `cave_hidden_nest`; a zone / minimap name for the
  Deep Water Cave (indoor, stable air) covering the chamber and passages (echo boxes in FX_Anchors); reverb, drip, stream
  and curtain sounds at FX_Anchors; dust motes at FX_LightShaftDust. Please lower the ambient on the player / creatures
  while indoors here (TimeManager.ambientMultiplier or probes): the rock is dark but characters still get the sky ambient.
- AI: small creatures and fish at `AI_Anchors`. RES (wave 2): nodes at `RES_Anchors` (types in the names).
- Lead: URP rendering layers (Sun excludes a "Cave" layer) would stop sunlight on cave surfaces farther than the 40 m
  shadow distance; the passages bend so long sight lines are rare. The cave kit (ART batch 2): re-run Build after delivery.
- ENV-A: the zone 1 blend band reaches the back door at (12.4, 22.5, -141.4); please keep terrain heights within 8 m of
  it unchanged, or tell CAVE to re-run Build (it re-resolves the mouths from the live terrain).
- Lead: bridge lock contention: EA 13:39-14:08, BONE 14:08-14:40, P from 16:10; my own first hold ran 15:24-15:44
  (20 min, over the 10 min limit: build + check + save in one hold).

## Not done / notes
- No PlayMode, no git, no deletes. Only my files (PrimalCaveBuilder.cs, CaveDaylightLight.cs, Art/Environment/Cave/*,
  terrain holes at the two mouths, the backup asset).
- ART cave kit not yet placed (not delivered); CAVE_Chamber_Dome will not replace the generated dome automatically.
