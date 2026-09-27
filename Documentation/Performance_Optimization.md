# Performance optimization pass (2026-09-27 18:14)

## Textures
- 185 textures, 152 updated; phone overrides ASTC 6x6 (UI ASTC 4x4)
- caps on phones: characters / terrain 1024, mouth 512, eyes / VFX / icons 256, props 512
- texel budget: PC 700 Mpx max, phone 124 Mpx max (before compression)

## Audio
- 159 clips: Vorbis; effects mono + decompress on load (< 3 s) or compressed in memory; ambience compressed in memory; music streamed

## Materials
- GPU instancing enabled on 10 more URP materials (SRP batcher stays on)

## Render pipeline
- Mobile (Mobile_RPAsset): shadows 45 m / 1 cascade(s), MSAA x1, render scale 0,9, HDR off
- PC (PC_RPAsset): shadows 120 m / 3 cascade(s), MSAA x4, render scale 1, HDR on
- LOD bias 0.9 phone / 1.6 PC, skin weights 2 / 4 bones, particle raycast budget 64 / 256

## Build
- build scene list: Assets/_Project/Scenes/Island_VerticalSlice.unity only (template sample scene removed from builds)
- GPU skinning on; Android: IL2CPP, ARM64, managed stripping low

