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
| Chamber dome (landmark) | centre (40, 20.9, -173.6), 21 x 19 m, 10.8 m high | still pool 11.6 x 8.6 m, surface 20.62, 1.7 m deep; ceiling cleft over the pool (daylight spot) |
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
- Terrain holes: only at the two mouths (final 47 + 52 cells, see the fix pass). TerrainData backed up once to
  `Art/Terrain/_Backup/TD_Island_before_CV.asset`; each build restores the two 20 x 20 m mouth regions from it first.
- Props: 11 rubble rocks (wall bases), 4 framing boulders at the mouths, 4 bone piles, the nest (existing PFB_ENV_Rock_*,
  PROP_PC_Bones). ART kit: 7 stalactite clusters + 6 rubble piles that fit (see the fix pass).

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

## Visual fix pass (2026-10-01, Lead's finish queue, bridge ids CVf_1..120)
Lead's captures (F6c) showed white sawtooth gaps round both mouths, a floating kit wall slab and a stalactite "disc" in the
chamber, and white slivers along the pool rim. Causes found and fixed:
- **Stale meshes in the editor (main cause of what the captures showed):** `SaveAsset` rewrote existing mesh assets with
  `EditorUtility.CopySerialized`. The collider took the new data but the renderer kept drawing the first build's GPU buffers
  in the same editor session, so every rebuild looked identical. Meshes are now rewritten through the Mesh API (Clear, Set*,
  UploadMeshData). The first capture after this change (CV7) was the first that showed the real geometry.
- **Mouths:** a solid rock band in a 7 m zone round each mouth: inner face, outer skin 1.25 m out, and a top cap. The cap is
  0.06-0.3 m above the cliff surface (a rock lip round the opening, thicker where the cliff is higher). On the ground it is a thin
  apron at the cliff foot that tapers away under the turf. Terrain holes are cut only where a cell touches the cave air and
  lies in the band or on the cliff behind it; flat ground in front stays terrain. East: 47 cells (18.4 m2), west: 52 cells
  (20.3 m2). The holes texture is now uncompressed and synced: compressed holes were drawn in 4 x 4-cell (2.5 m) blocks,
  larger than the cut cells. Outside the zones the shell keeps only the cave wall itself (no seam pieces at the zone edge).
- **White surfaces:** the band-0 rock material had `_MossAmount` 0.18 but no `_MossMap`, so up-facing rock drew flat white.
  Moss is off. Rock near daylight is now matte (smoothness 0.1, wetness 0.05-0.08).
- **Shell quality:** non-planar surface-nets quads are split on the diagonal that faces the air (about 21 triangles re-wound
  per build). The floor / wall crease and the pool rim are rounded (smooth max 0.3 m, smooth basin union 0.35 m). The pool
  water is now 0.28 m below the rim (20.62) with the disc at 0.96 of the basin, so it no longer shows through the rim steps.
- **ART kit:** only pieces that fit the generated rock are placed. 7 `CAVE_Stalactites` hang from the chamber ceiling by
  raycast at scale 0.75-1.0, attach point embedded 0.3 m. Each is checked flush on 4 probes and needs tip headroom 2.4 m
  (0.6 m over the pool). 6 `CAVE_Rubble` sit on the floor at the wall bases, aligned to the floor normal. Each has all 4 footprint corners on
  the floor within -0.3..+0.35 m, stays clear of the walk lines by its half size + 0.9 m, and avoids the pools and stream.
  Skipped: CAVE_Wall_A/B, Tunnel_Straight/Bend, Chamber_Dome, PoolRim (modular or circular r 8.4 m, they do not match
  the generated shell; this removed the floating slab). My own rubble rocks are also checked now (one floated in the west
  passage).
- Reflection probes are now updated in place (destroying them in edit mode left URP's probe atlas throwing NullReference in
  `ReflectionProbeManager` on every render). Capture takes `reload` (reopen the saved scene first), `only=` and `hide=`.

### Results (final)
- CVf_115b Build: 42 s, 0 warnings. Shell 46,034 tris, 23,858 verts, 12 chunks.
- CVf_116k Check: **CAVE CHECK OK**. 828 probes, including 1.2 m of approach outside each mouth:
  - holes 0, too steep 0, headroom < 2.2 m 0, capsule blocked 0;
  - steps (> 0.32 m and steeper than 42 deg) 0; lowest headroom 2.5 m; crawl 1.6 m (crouch);
  - void rays 2000, 0 into the void (the test now ignores terrain hits, because the terrain collider also answers from below);
  - stream downhill.
- **Rendered tris at LOD0: 116,474** (kit 52,200 in 13 pieces). The earlier 491k counted every LOD level and 73 kit pieces.
- CVf_117s SaveScene saved. CVf_118p Capture CV12 (9 views, 0 exceptions). CVf_119c ConsoleCheck: 0 errors, 0 warnings.
  Scene clean.
- Looked at the captures myself (CV12_*):
  - west mouth: clean rock arch, no white or sky;
  - east mouth: rock lip all round, no white or sky; a darker rock apron / lip in front-left where the ground rises to the cliff;
  - chamber: no floating pieces and no see-through slivers; the faint light flecks left are lit rock facets of the basin
    wall under the shaft light.

## Not done / notes
- No PlayMode, no git, no deletes. Only my files (PrimalCaveBuilder.cs, CaveDaylightLight.cs, Art/Environment/Cave/*,
  terrain holes at the two mouths, the backup asset).
- ART kit pieces CAVE_Wall_A/B, Tunnel_*, Chamber_Dome and PoolRim are not used (they do not fit the generated rock).
- The earlier pending update (crawl, void check, Capture) ran in the fix pass above.
- Remaining look notes: surface-nets facets (0.4 m) still show on the steep basin wall under the shaft light; the east mouth's
  front-left lip is a broad rock shelf where the ground rises to the cliff. Shell resolution or a hand-placed rock there would
  refine it.
