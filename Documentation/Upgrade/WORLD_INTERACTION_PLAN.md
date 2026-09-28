# WORLD INTERACTION PLAN (bushes, vegetation, trees, VFX)

Owner roles: `unity-shader-specialist` / `technical-artist` (vegetation, VFX), lead (models).

## Bushes
- Terrain detail bushes stay for density (cheap, instanced, no colliders). Density raised moderately on the detail map
  near paths and forest edges (performance checked per quality preset).
- **Interactive bushes**: a limited set of GameObject bushes (target 120-180, placed near paths, camps, forest edges, never
  on the beach or water) with one trigger collider each and a `BushInteraction` component:
  - player (or a creature) enters -> rustle: procedural shake of the bush transform (scale / rotation spring, no Animator),
    pooled leaf burst (VfxId.Leaves, small scale), rustle sound (SfxId.LeafRustle, random pitch) after a small random
    delay, cooldown 1.5-3 s per bush; running triggers a stronger shake than walking; crouching is quieter.
  - variants (data on the component): berries (ResourceNode on the same object), hidden item (one-time pickup), small
    animal flush (a bird / lizard burst effect and sound; real small prey comes with the hunting step).
  - distance culled (no work beyond 40 m), no per-frame allocation, no Instantiate after warm-up.
- Model: original bush meshes from Blender (2-3 variants, LOD0 / LOD1, shared foliage atlas).

## Vegetation wind
Retry with a lean, safe foliage shader (few keyword variants, simple lighting, instancing), switched on one material copy
first, editor stability checked, then applied to all trees (see the owner's earlier list; done at the end).

## Trees
Chopping (TreeHarvest) exists; the interaction range bug is fixed. Climbing (Climbable + PlayerClimb, controlled axis
from start to end point with stamina) and fruit gathering (FruitCluster) exist; they get the missing `OnHarvest` event
receiver and a play check.

## Acceptance
Walking through a bush makes it shake, sparse leaves and a rustle (capture + log); berry bushes give berries; no VFX spam
(cooldown); frame cost measured on the island (performance test or profiler numbers).
